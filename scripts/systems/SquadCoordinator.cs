using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>What a squad member does for the squad (AI combat v2 spec s8).</summary>
public enum SquadRole { None, Anchor, Flanker, Marksman }

/// <summary>What one member reports to its squad each frame.</summary>
public readonly record struct SquadMemberState(
	int Id,
	bool Alive,
	/// <summary>Fighting or aware of the target.</summary>
	bool Engaged,
	float Aggression,
	bool HasScope,
	/// <summary>At the end of its flank route.</summary>
	bool InFlankPosition);

/// <summary>Numbers of squad play. Placeholders to tune in the AI range.</summary>
public sealed record SquadTuning(
	int MaxFlankers = 2,
	/// <summary>The pincer goes after this long even if a flanker is not in place.</summary>
	float PincerTimeoutS = 25f,
	/// <summary>A bounding member moves this far, then the other takes a turn.</summary>
	float BoundM = 10f,
	/// <summary>Or after this long if it is stuck.</summary>
	float BoundTimeoutS = 6f,
	/// <summary>Losses inside the window that break nerves, as a fraction of the squad.</summary>
	float MoraleLossFraction = 0.6f,
	float MoraleWindowS = 10f,
	/// <summary>Seconds the roles hold before they are worked out again.</summary>
	float RoleRefreshS = 1f);

/// <summary>
/// One plain object per squad of two or more bots (not a node per bot). Gives out roles (anchor, flankers, marksman),
/// times the pincer, takes turns for bounding overwatch and watches morale. Pure logic; the AI nodes report their state
/// and ask what they may do.
/// </summary>
public sealed class SquadCoordinator
{
	private readonly SquadTuning _t;
	private readonly List<int> _members = new();
	private readonly Dictionary<int, SquadRole> _roles = new();
	private readonly List<(int Id, float At)> _deaths = new();
	private float _clock;
	private float _roleTimer;
	private float _pincerTimer;
	private float _goTimer;
	private int _mover = -1;
	private float _moverMetres;
	private float _moverTimer;
	private int _startSize;
	private bool _moraleReported;

	public SquadCoordinator(SquadTuning? tuning = null) => _t = tuning ?? new SquadTuning();

	/// <summary>The first to join; if it dies the squad's nerve may break.</summary>
	public int Leader => _members.Count > 0 ? _members[0] : -1;
	public IReadOnlyList<int> Members => _members;

	/// <summary>Members still alive.</summary>
	public int Size => _members.Count - _deaths.Count(d => _members.Contains(d.Id));

	/// <summary>All flankers are in place (or the wait ran out): attack together.</summary>
	public bool PincerGo { get; private set; }

	/// <summary>Raised once when the squad breaks.</summary>
	public event Action? MoraleBroken;

	public void Join(int id)
	{
		if (_members.Contains(id))
			return;
		_members.Add(id);
		_startSize = Math.Max(_startSize, _members.Count);
	}

	public void Died(int id)
	{
		if (_members.Contains(id) && _deaths.All(d => d.Id != id))
			_deaths.Add((id, _clock));
		_roles.Remove(id);
		if (_mover == id)
			_mover = -1;
	}

	/// <summary>Removes a member that left without dying (fled and despawned).</summary>
	public void Left(int id)
	{
		_members.Remove(id);
		_deaths.RemoveAll(d => d.Id == id);
		_roles.Remove(id);
		if (_mover == id)
			_mover = -1;
	}

	public SquadRole RoleOf(int id) => _roles.TryGetValue(id, out var r) ? r : SquadRole.None;

	public void Update(float delta, IReadOnlyCollection<SquadMemberState> states)
	{
		_clock += delta;
		_deaths.RemoveAll(d => !_members.Contains(d.Id));
		var engaged = states.Where(s => s.Alive && s.Engaged).ToList();

		_roleTimer -= delta;
		if (_roleTimer <= 0f)
		{
			_roleTimer = _t.RoleRefreshS;
			AssignRoles(engaged);
		}

		UpdatePincer(delta, states);
		UpdateBounding(delta, engaged);
		CheckMorale();
	}

	private void AssignRoles(List<SquadMemberState> engaged)
	{
		var keep = _roles.Where(r => engaged.Any(e => e.Id == r.Key)).ToDictionary(r => r.Key, r => r.Value);
		_roles.Clear();
		if (engaged.Count < 2)
			return; // a lone fighter acts on its own
		foreach (var r in keep)
			_roles[r.Key] = r.Value;
		var free = engaged.Where(e => !_roles.ContainsKey(e.Id)).ToList();

		if (!_roles.ContainsValue(SquadRole.Marksman) && free.FirstOrDefault(e => e.HasScope) is { Id: var m } scope && scope.HasScope)
		{
			_roles[m] = SquadRole.Marksman;
			free.RemoveAll(e => e.Id == m);
		}
		if (!_roles.ContainsValue(SquadRole.Anchor) && free.Count > 0)
		{
			var anchor = free.OrderBy(e => e.Aggression).ThenBy(e => e.Id).First();
			_roles[anchor.Id] = SquadRole.Anchor;
			free.Remove(anchor);
		}
		var flankers = _roles.Count(r => r.Value == SquadRole.Flanker);
		foreach (var e in free.OrderByDescending(e => e.Aggression).ThenBy(e => e.Id))
		{
			if (flankers >= _t.MaxFlankers)
				break;
			_roles[e.Id] = SquadRole.Flanker;
			flankers++;
		}
	}

	private void UpdatePincer(float delta, IReadOnlyCollection<SquadMemberState> states)
	{
		var flankers = states.Where(s => s.Alive && RoleOf(s.Id) == SquadRole.Flanker).ToList();
		if (flankers.Count == 0)
		{
			_pincerTimer = 0f;
			_goTimer = 0f;
			PincerGo = false;
			return;
		}
		if (PincerGo)
		{
			_goTimer += delta;
			if (_goTimer > 10f)
			{
				PincerGo = false;
				_goTimer = 0f;
				_pincerTimer = 0f;
			}
			return;
		}
		_pincerTimer += delta;
		if (flankers.All(f => f.InFlankPosition) || _pincerTimer >= _t.PincerTimeoutS)
			PincerGo = true;
	}

	private void UpdateBounding(float delta, List<SquadMemberState> engaged)
	{
		if (engaged.Count < 2)
		{
			_mover = -1;
			return;
		}
		_moverTimer += delta;
		if (_mover == -1 || engaged.All(e => e.Id != _mover) || _moverMetres >= _t.BoundM || _moverTimer >= _t.BoundTimeoutS)
		{
			var order = engaged.Select(e => e.Id).OrderBy(i => i).ToList();
			var next = order.FirstOrDefault(i => i > _mover, order[0]);
			_mover = next;
			_moverMetres = 0f;
			_moverTimer = 0f;
		}
	}

	/// <summary>False when it is another member's turn to move up and this one should hold and cover.</summary>
	public bool MayAdvance(int id) => Size < 2 || _mover == -1 || _mover == id;

	/// <summary>The member that has the move reports how far it went.</summary>
	public void ReportAdvance(int id, float metres)
	{
		if (id == _mover)
			_moverMetres += metres;
	}

	private void CheckMorale()
	{
		if (_moraleReported || _members.Count < 2)
			return;
		var leaderDown = _deaths.Any(d => d.Id == Leader);
		var recent = _deaths.Count(d => _clock - d.At <= _t.MoraleWindowS);
		if (leaderDown || recent > _startSize * _t.MoraleLossFraction)
		{
			_moraleReported = true;
			MoraleBroken?.Invoke();
		}
	}

	/// <summary>One survivor's morale roll: it flees with a chance of its panic.</summary>
	public static bool Flees(float panic, Random rng) => rng.NextDouble() < Math.Clamp(panic, 0f, 1f);
}
