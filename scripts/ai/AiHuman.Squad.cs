using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Squad half of <see cref="AiHuman"/> (AI combat v2 spec s8): joins nearby squadmates of its faction, reports to the
/// shared <see cref="SquadCoordinator"/>, shouts when undisciplined, and breaks and runs when the squad's nerve goes.
/// </summary>
public partial class AiHuman
{
	private const float JoinRadius = 25f;
	private const int MaxSquad = 4;

	private static readonly Dictionary<int, AiHuman> Members = new();
	private static readonly Dictionary<SquadCoordinator, ulong> LastTick = new();

	private int _id;
	private float _calloutS;
	private float _shareS;
	private float _fleeCheckS;
	private bool _fleeing;
	private Vector3 _lastPos;

	public SquadCoordinator? Squad { get; private set; }
	public SquadRole SquadRole => Squad?.RoleOf(_id) ?? SquadRole.None;

	/// <summary>Squad size and role for dev reporting, for example "3/Anchor".</summary>
	public string SquadLabel => Squad is null ? "-" : $"{Squad.Size}/{SquadRole}";

	private void JoinSquad()
	{
		_id = (int)(GetInstanceId() & 0x7fffffff);
		Members[_id] = this;
		var mate = Members.Values.FirstOrDefault(m => !ReferenceEquals(m, this) && m.IsAlive && m.Squad is { } s
			&& m.Faction == Faction && s.Size < MaxSquad && m.GlobalPosition.DistanceTo(GlobalPosition) <= JoinRadius);
		Squad = mate?.Squad ?? new SquadCoordinator();
		Squad.Join(_id);
		Squad.MoraleBroken += OnMoraleBroken;
		_lastPos = GlobalPosition;
	}

	private void LeaveSquad()
	{
		Members.Remove(_id);
		if (Squad is null)
			return;
		Squad.MoraleBroken -= OnMoraleBroken;
		Squad.Left(_id);
		if (Squad.Members.Count == 0)
			LastTick.Remove(Squad);
	}

	private SquadMemberState MyState() => new(_id, IsAlive, Brain.State == AiState.Combat,
		S(Stats.Tactics.Aggression), Weapon is { } w && w.Stats.Zoom > 0f,
		_tactics.State == TacticState.Flank && _arrived);

	/// <summary>One member per physics frame updates the shared squad state from everyone's report.</summary>
	private void TickSquad(float delta)
	{
		if (Squad is null)
			return;
		var frame = Engine.GetPhysicsFrames();
		if (LastTick.TryGetValue(Squad, out var done) && done == frame)
			return;
		LastTick[Squad] = frame;
		Squad.Update(delta, Squad.Members.Where(Members.ContainsKey).Select(i => Members[i].MyState()).ToList());
	}

	/// <summary>Squad-level facts for the planner: whose turn it is to move, whether to wait for the pincer.</summary>
	private bool MayAdvanceNow => Squad is null || Squad.MayAdvance(_id);
	private bool HoldFlankNow => Squad is not null && SquadRole == SquadRole.Flanker && _tactics.State == TacticState.Flank && !Squad.PincerGo;

	private void SquadFrame(Vector3 target, bool sees, float delta)
	{
		if (Squad is null)
			return;
		var moved = GlobalPosition.DistanceTo(_lastPos);
		_lastPos = GlobalPosition;
		if (_tactics.State == TacticState.Advance)
			Squad.ReportAdvance(_id, moved);

		// What one sees the others learn: a rough cue at the spot, enough to turn and come.
		_shareS -= delta;
		if (sees && _shareS <= 0f)
		{
			_shareS = 1.5f;
			foreach (var id in Squad.Members)
				if (id != _id && Members.TryGetValue(id, out var mate) && mate.IsAlive)
					mate.Perception.Hear(0.55f, target);
		}

		// Undisciplined squads shout positions and orders, which carries.
		_calloutS -= delta;
		if (_calloutS <= 0f && S(Stats.Squad.Disciplined) < 0.5f && (sees || Perception.InterestPoint is not null))
		{
			_calloutS = S(Stats.Squad.CalloutEveryS) * (0.7f + (float)_rng.NextDouble() * 0.6f);
			NoiseBus.Emit(GlobalPosition, S(Stats.Squad.CalloutRadiusM), "callout", this);
		}
	}

	/// <summary>Low-tier bots (looters, bandits, tier 1) may drop everything and run when the squad breaks.</summary>
	private bool LowTier => Faction is Faction.Looters or Faction.Bandits || (Actor?.Tier ?? 9) <= 1;

	private void OnMoraleBroken()
	{
		if (!IsAlive || _fleeing || !LowTier || !SquadCoordinator.Flees(S(Stats.Temper.Panic), _rng))
			return;
		_fleeing = true;
		if (Gear.InSlot(EquipSlot.Backpack) is { } pack && Gear.Remove(pack))
			GetParent().AddChild(DroppedItem.Create(pack, GlobalPosition + Vector3.Up * 0.8f, Vector3.Zero));
		_tactics.Force(TacticState.Flee);
	}

	/// <summary>A fleeing bot leaves the world once the player cannot see it and it is far away.</summary>
	private void CheckFledAway(float delta)
	{
		_fleeCheckS -= delta;
		if (!_fleeing || _fleeCheckS > 0f)
			return;
		_fleeCheckS = 1f;
		var player = GetTree().GetNodesInGroup("perceivables").OfType<Player>().FirstOrDefault();
		if (player is null)
			return;
		var gone = GlobalPosition.DistanceTo(player.GlobalPosition) > S(Stats.Squad.DespawnM)
			&& Blocked(player.GlobalPosition + Vector3.Up * 1.6f, GlobalPosition + Vector3.Up * 1.2f);
		if (!gone)
			return;
		LeaveSquad();
		QueueFree();
	}
}
