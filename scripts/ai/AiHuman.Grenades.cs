using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Grenade half of <see cref="AiHuman"/> (throwables spec s6): carries grenades in its gear (they drop as loot), throws
/// one at a known target it cannot shoot, with the pin sound as a tell and a shout from undisciplined factions, throws
/// smoke when an operator retreats, and dodges grenades it sees land or hears clink. The decisions, solver and loadout
/// table are the pure <see cref="AiGrenadeDecision"/>, <see cref="ThrowSolver"/> and <see cref="AiGrenadeLoadout"/>.
/// </summary>
public partial class AiHuman
{
	private const float ThrowSpeedMs = 18f;
	private const float ThrowWindUpS = 0.8f;

	private float _grenadeCooldownS = 6f;
	private float _grenadeCheckS;
	private float _noLineOfFireS;
	private float _throwLeftS;
	private ItemInstance? _throwItem;
	private GrenadeSpec? _throwSpec;
	private Vector3 _throwTarget;
	private float _throwCookS;
	private Vector3? _dodgeThreat;
	private float _dodgeThreatLeftS;
	private Vector3? _pendingDodgeFrom;
	private readonly System.Collections.Generic.HashSet<ThrownGrenade> _dodged = new();

	private float _blindS;
	private float _deafS;
	private float _gasS;

	/// <summary>A flashbang reached this AI (throwables spec s4): it sees almost nothing for a few seconds (acquisition x0.15, aim goes wild) and hears nothing.</summary>
	public void Stun(float blindS, float deafS)
	{
		_blindS = Mathf.Max(_blindS, blindS);
		_deafS = Mathf.Max(_deafS, deafS);
		GD.Print($"AI {Name} stunned: blind {blindS:0.0} s, deaf {deafS:0.0} s");
	}

	/// <summary>Multiplier on this AI's aim error from being blinded or gassed.</summary>
	private float AimErrorFromStatus() => (_blindS > 0f ? 3f : 1f) * (_gasS > 0f ? GasModel.AimErrorMultiplier : 1f);

	/// <summary>The AI is mid-throw: it stands still and does not shoot.</summary>
	public bool IsThrowing => _throwLeftS > 0f;

	/// <summary>Rolls the faction's grenades into the gear (they are lost to the AI when thrown and dropped as loot when it dies).</summary>
	private void GiveGrenades()
	{
		foreach (var id in AiGrenadeLoadout.Roll(Faction, _rng))
		{
			if (ItemDb.Create(id) is { } grenade)
				Gear.TryAutoStore(grenade);
		}
	}

	private ItemInstance? FindGrenade(System.Func<GrenadeKind, bool> kind)
		=> Gear.AllItems().FirstOrDefault(i => ItemDb.Data(i.Spec.Id)?.Payload is GrenadeData g && kind(g.Kind));

	private static GrenadeSpec SpecOf(ItemInstance item) => ((GrenadeData)ItemDb.Data(item.Spec.Id)!.Payload!).ToSpec();

	private Vector3 ThrowHandPoint() => GlobalPosition + Vector3.Up * 1.5f - GlobalBasis.Z * 0.4f;

	/// <summary>Called every combat step: counts the time without a line of fire and, once a second, decides whether to throw.</summary>
	private void UpdateGrenadesInCombat(float delta, Vector3 target, bool sees, float distance, float faceYaw)
	{
		if (_throwLeftS > 0f)
			return; // mid-throw
		_noLineOfFireS = sees ? 0f : _noLineOfFireS + delta;
		_grenadeCheckS -= delta;
		if (_grenadeCheckS > 0f)
			return;
		_grenadeCheckS = 1f;

		var retreating = _tactics.State == TacticState.Retreat;
		if (FindGrenade(k => k == GrenadeKind.Smoke) is { } smoke
			&& AiGrenadeDecision.ShouldSmoke(Archetype, retreating, true, _grenadeCooldownS))
		{
			// smoke between the AI and what it flees from
			var toTarget = (target - GlobalPosition) with { Y = 0f };
			BeginThrow(smoke, GlobalPosition + toTarget.Normalized() * Mathf.Min(6f, distance * 0.5f));
			return;
		}
		if (FindGrenade(k => k is GrenadeKind.Frag or GrenadeKind.Concussion) is not { } frag)
			return;
		var landing = target + Vector3.Down * 1f;
		var from = ThrowHandPoint();
		var velocity = ThrowSolver.Velocity(from, landing, ThrowSpeedMs);
		var arcClear = velocity is { } v && ArcIsClear(from, v);
		var squadmateNear = Members.Values.Any(m => !ReferenceEquals(m, this) && m.IsAlive && m.Faction == Faction
			&& m.GlobalPosition.DistanceTo(landing) < 8f);
		var inputs = new AiGrenadeInputs(true, Perception.SecondsSinceCue, distance, _noLineOfFireS, arcClear, squadmateNear,
			_grenadeCooldownS, Archetype, Cornered: Injury() > 0.6f);
		if (AiGrenadeDecision.ShouldThrow(inputs, _rng))
			BeginThrow(frag, landing);
	}

	/// <summary>A few samples along the flight, tested against the world: nothing may be in the way.</summary>
	private bool ArcIsClear(Vector3 from, Vector3 velocity)
	{
		var space = GetWorld3D().DirectSpaceState;
		var arc = ThrowSolver.Arc(from, velocity, 9.8f, 0.15f, 3f);
		for (var i = 1; i < arc.Count; i++)
		{
			var q = PhysicsRayQueryParameters3D.Create(arc[i - 1], arc[i], 1u, new Godot.Collections.Array<Rid> { GetRid() });
			if (space.IntersectRay(q).Count > 0)
				return false;
		}
		return true;
	}

	/// <summary>Pulls the pin (the tell), shouts if undisciplined, and starts the throwing motion.</summary>
	private void BeginThrow(ItemInstance item, Vector3 landing)
	{
		_throwItem = item;
		_throwSpec = SpecOf(item);
		_throwTarget = landing;
		_throwCookS = _throwSpec.Kind == GrenadeKind.Frag ? AiGrenadeDecision.CookS(Archetype, _rng) : 0f;
		_throwLeftS = ThrowWindUpS + _throwCookS;
		NoiseBus.Emit(GlobalPosition, 10f, "grenade_pin", this);
		Sfx.Play3D(this, "grenade_pin", GlobalPosition);
		if (AiGrenadeLoadout.Shouts(Faction))
			NoiseBus.Emit(GlobalPosition, 30f, "callout", this); // "grenade!"
		GD.Print($"AI {Name} ({Archetype}) pulls the pin: {_throwSpec.Id}, cook {_throwCookS:0.0} s, target {landing.X:0},{landing.Z:0}");
	}

	private void ReleaseAiGrenade()
	{
		if (_throwItem is not { } item || _throwSpec is not { } spec)
			return;
		_throwItem = null;
		_throwSpec = null;
		// the error: good throwers land close, conscripts wide
		var radius = AiGrenadeDecision.ErrorRadiusM(Archetype) * Mathf.Sqrt((float)_rng.NextDouble());
		var angle = (float)_rng.NextDouble() * Mathf.Tau;
		var aim = _throwTarget + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
		var from = ThrowHandPoint();
		if (ThrowSolver.Velocity(from, aim, ThrowSpeedMs) is not { } velocity)
		{
			GD.Print($"AI {Name} cannot reach the target and keeps the grenade");
			return;
		}
		var fuseLeft = Mathf.Max(0.8f, spec.FuseS - _throwCookS);
		var grenade = ThrownGrenade.Create(spec, fuseLeft, GetRid(), velocity, this);
		GetParent().AddChild(grenade);
		grenade.GlobalPosition = from;
		Gear.Remove(item);
		_grenadeCooldownS = AiGrenadeDecision.CooldownS;
		GD.Print($"AI {Name} throws {spec.Id}, fuse left {fuseLeft:0.0} s");
	}

	/// <summary>
	/// Every think step, outside combat too: sees a live grenade land close or hears it clink and gets away from it; a
	/// hostile pin pulled nearby does the same (infiltrators excepted, they rush the thrower).
	/// </summary>
	private void GrenadeAwarenessTick(float delta)
	{
		_grenadeCooldownS = Mathf.Max(0f, _grenadeCooldownS - delta);
		_blindS = Mathf.Max(0f, _blindS - delta);
		_deafS = Mathf.Max(0f, _deafS - delta);
		_gasS = Mathf.Max(0f, _gasS - delta);
		if (SmokeCloud.GasCentreAt(GlobalPosition + Vector3.Up * 1.2f) is { } gasAt)
		{
			_gasS = 1f; // coughing: aims badly, and gets out of the cloud
			if (!_fleeing)
			{
				_fleeing = true;
				_fleePoint = FindFleePoint(gasAt);
			}
		}
		foreach (var patch in FirePatch.Active)
		{
			var flat = new Vector2(patch.GlobalPosition.X - GlobalPosition.X, patch.GlobalPosition.Z - GlobalPosition.Z).Length();
			if (flat < patch.RadiusM + 1.5f && !_fleeing)
			{
				_fleeing = true; // keeps away from fire
				_fleePoint = FindFleePoint(patch.GlobalPosition);
			}
		}
		if (_throwLeftS > 0f && (_throwLeftS -= delta) <= 0f)
			ReleaseAiGrenade();
		if (_dodgeThreatLeftS > 0f && (_dodgeThreatLeftS -= delta) <= 0f)
			_dodgeThreat = null;
		if (_pendingDodgeFrom is { } pin)
		{
			_pendingDodgeFrom = null;
			Dodge(pin);
		}
		if (ThrownGrenade.Live.Count == 0)
			return;
		_dodged.RemoveWhere(g => !IsInstanceValid(g));
		var eye = GlobalPosition + Vector3.Up * 1.5f;
		var space = GetWorld3D().DirectSpaceState;
		foreach (var g in ThrownGrenade.Live.ToList())
		{
			if (!g.IsDangerous || _dodged.Contains(g))
				continue;
			var d = GlobalPosition.DistanceTo(g.GlobalPosition);
			if (d > 12f)
				continue;
			var sees = d <= 10f && space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, g.GlobalPosition, 1u, new Godot.Collections.Array<Rid> { GetRid() })).Count == 0;
			if (!AiGrenadeDecision.ShouldDodge(d, sees, g.Clinked, g.ThrowerRid == GetRid()))
				continue;
			_dodged.Add(g);
			Dodge(g.GlobalPosition);
		}
	}

	/// <summary>Gets away from a point: the under-fire reaction toward cover that does not face it (in combat), else a run.</summary>
	private void Dodge(Vector3 from)
	{
		if (Health.IsDead)
			return;
		_dodgeThreat = from;
		_dodgeThreatLeftS = 3f;
		Wake();
		GD.Print($"AI {Name} dodges a grenade at {from.X:0},{from.Z:0}");
		if (Brain.State == AiState.Combat)
			TriggerUnderFire();
		else
		{
			_fleeing = true;
			_fleePoint = FindFleePoint(from);
		}
	}

	/// <summary>Dev: this AI gets a frag and throws it at a point at once.</summary>
	public void DebugThrowAt(Vector3 point, string grenadeId = "frag_grenade")
	{
		if (ItemDb.Create(grenadeId) is not { } grenade)
			return;
		Gear.TryAutoStore(grenade);
		BeginThrow(grenade, point);
	}
}
