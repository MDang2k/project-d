using System;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>
/// Combat half of <see cref="AiHuman"/>: runs <see cref="CombatTactics"/>, picks cover, peek and
/// flank points, and shoots real projectiles through the shared weapon and projectile systems.
/// </summary>
public partial class AiHuman
{
	private readonly record struct CombatPlan(Vector3? Destination, float Speed, float? FaceYaw, bool Crouch, bool Prone = false);

	private const float CoverRefreshS = 1f;

	private CombatTactics _tactics = null!;
	private AiBurstFire _burst = null!;
	private AiAimMotor _motor = null!;
	private bool _blindFiring;
	private TacticAction _action;
	private float _decideS;
	private Vector3? _fleePoint;
	private bool _leaning;
	private static readonly HandlingTuning Handling = HandlingTuning.Default;
	private Vector3 _lastTargetPos;
	private Vector3 _targetVelocity;
	private bool _hadTarget;
	private MedicalData? _healItem;
	private AiUnderFire _underFire = null!;
	private bool _prone;

	/// <summary>Raised when any AI takes a hit, so squadmates nearby can duck too.</summary>
	private static event Action<AiHuman>? AllyHit;
	private readonly Random _rng = new();
	private ProjectileManager? _projectiles;
	private readonly Array<Rid> _ownRids = new();
	private Node3D _hitboxRoot = null!;
	private Node3D? _bodyMesh;
	private AiBodyRig? _rig;

	private float _suppression;
	private float _coverRefresh;
	private Vector3? _coverPoint;
	private Vector3? _peekPoint;
	private Vector3? _flankPoint;
	private bool _healStarted;
	private bool _healed;
	private bool _crouching;
	private float _crouchScale = 1f;
	private bool _wantsFire;
	private Vector3 _aimPoint;
	private TacticState _lastTactic = TacticState.Advance;

	/// <summary>Current combat behavior (null outside combat); partners read this to coordinate flanks.</summary>
	public TacticState? CombatState => Brain.State == AiState.Combat ? _tactics.State : null;
	public bool IsCrouching => _crouching;
	public UnderFirePhase UnderFirePhase => _underFire.Phase;
	public TacticAction Action => _action;

	private void InitCombat()
	{
		_tactics = new CombatTactics(ActorTuning.Tactics(Actor!.Stats), _rng) { UtilityControlled = true };
		_action = TacticAction.Push;
		_healItem = ActorDb.Medical(Def.Text("heal_item"));
		_motor = new AiAimMotor(ActorTuning.Aim(Actor.Stats), _rng);
		var rpm = _weaponData?.RoundsPerMinute ?? 600f;
		_burst = new AiBurstFire(rpm, SInt(Stats.Fire.BurstMin), SInt(Stats.Fire.BurstMax), S(Stats.Fire.BurstPauseMinS),
			S(Stats.Fire.BurstPauseMaxS), S(Stats.Fire.ReactionS), _rng);
		_burst.BurstFinished += () => _tactics.OnBurstFinished();
		Actor.Stats.Changed += OnStatsChanged;
		_underFire = new AiUnderFire(ActorTuning.UnderFire(Actor.Stats), _rng);
		AllyHit += OnAllyHit;
		Brain.StateChanged += (_, next) =>
		{
			if (next == AiState.Combat)
				_tactics.Begin();
		};
		Health.HitTaken += _ =>
		{
			_suppression = Mathf.Min(1f, _suppression + 0.6f);
			TriggerUnderFire();
			AllyHit?.Invoke(this);
		};
		Health.MedicalFinished += (_, _) => _healed = true;
		if (Weapon is { } w)
		{
			// Rifles fire bursts in full-auto, pistols stay semi.
			if (w.Data.AllowsAuto && w.State.Fire.Mode == FireMode.Semi)
				w.State.Fire.CycleMode();
			w.ShotFired += OnShotFired;
		}
		_ownRids.Add(GetRid());
		foreach (var child in _hitboxRoot.GetChildren())
		{
			if (child is HitboxArea a)
				_ownRids.Add(a.GetRid());
		}
		_projectiles = GetTree().GetFirstNodeInGroup("projectiles") as ProjectileManager;
		if (_projectiles is not null)
			_projectiles.Passed += OnBulletPassed;
	}

	private void ExitCombat()
	{
		if (Actor is not null)
			Actor.Stats.Changed -= OnStatsChanged;
		AllyHit -= OnAllyHit;
		if (_projectiles is not null)
			_projectiles.Passed -= OnBulletPassed;
	}

	/// <summary>A modifier came or went (veteran, tier, injury...): hand the new numbers to every model.</summary>
	private void OnStatsChanged(System.Collections.Generic.IReadOnlyCollection<string> _)
	{
		var stats = Actor!.Stats;
		_tuning = ActorTuning.Perception(stats);
		Perception.Tuning = _tuning;
		Brain.Tuning = ActorTuning.Brain(stats);
		_tactics.Tuning = ActorTuning.Tactics(stats);
		_motor.Tuning = ActorTuning.Aim(stats);
		_burst.Retune(SInt(Stats.Fire.BurstMin), SInt(Stats.Fire.BurstMax), S(Stats.Fire.BurstPauseMinS),
			S(Stats.Fire.BurstPauseMaxS), S(Stats.Fire.ReactionS));
		_agent.TargetDesiredDistance = S(Stats.Brain.ArrivalRadius);
		_underFire.Tuning = ActorTuning.UnderFire(stats);
	}

	/// <summary>Being shot at only matters once the AI is fighting; patrol and search have their own senses.</summary>
	private void TriggerUnderFire()
	{
		if (!Health.IsDead && Brain.State == AiState.Combat)
			_underFire.Trigger();
	}

	private void OnAllyHit(AiHuman who)
	{
		if (!ReferenceEquals(who, this) && who.Faction == Faction && who.GlobalPosition.DistanceTo(GlobalPosition) < 15f)
			TriggerUnderFire();
	}

	/// <summary>Metres to the nearest cover from the threat, searched now rather than at the next refresh.</summary>
	private float? CoverDistanceNow()
	{
		var threat = Perception.InterestPoint ?? GlobalPosition - GlobalBasis.Z * 10f;
		_coverPoint = FindCover(threat, false);
		_coverRefresh = CoverRefreshS;
		return _coverPoint is { } c ? GlobalPosition.DistanceTo(c) : null;
	}

	/// <summary>Bullets that whip past make the AI keep its head down.</summary>
	private void OnBulletPassed(Vector3 from, Vector3 to, Rid shooter)
	{
		if (Health.IsDead || shooter == GetRid())
			return;
		var chest = GlobalPosition + Vector3.Up * 1.2f;
		var seg = to - from;
		var len2 = seg.LengthSquared();
		var t = len2 > 0.0001f ? Mathf.Clamp((chest - from).Dot(seg) / len2, 0f, 1f) : 0f;
		var miss = chest.DistanceTo(from + seg * t);
		if (miss < 1.5f)
			_suppression = Mathf.Min(1f, _suppression + 0.12f);
		if (miss < S(Stats.React.NearMissM))
			TriggerUnderFire();
	}

	private float Injury()
	{
		var b = Health.Body;
		float hp = 0f, max = 0f;
		foreach (var part in new[] { BodyPart.Head, BodyPart.Thorax, BodyPart.Stomach })
		{
			hp += b.Hp(part);
			max += b.MaxHp(part);
		}
		var injury = max > 0f ? 1f - hp / max : 1f;
		injury += System.Linq.Enumerable.Count(Health.Effects.BleedingParts) * 0.12f;
		return Mathf.Clamp(injury, 0f, 1f);
	}

	/// <summary>Chooses movement, facing and trigger for this frame from the tactics state.</summary>
	private CombatPlan PlanCombat(float delta)
	{
		var target = Perception.InterestPoint ?? GlobalPosition - GlobalBasis.Z * 5f;
		var sees = Perception.SeesTarget;
		var me = GlobalPosition;
		var toTarget = new Vector3(target.X - me.X, 0f, target.Z - me.Z);
		var distance = toTarget.Length();
		var faceTarget = Mathf.Atan2(-toTarget.X, -toTarget.Z);

		_suppression = Mathf.Max(0f, _suppression - 0.3f * delta);
		TickSquad(delta);
		_underFire.Update(delta, CoverDistanceNow, _arrived);
		_coverRefresh -= delta;
		if (_coverRefresh <= 0f)
		{
			// the budget caps sweeps across all AI in a frame; a bot that is refused tries again next frame
			if (AiPerf.TryBeginSweep(Frame, BandTuning))
			{
				_coverRefresh = CoverRefreshS;
				RefreshPoints(target, retreat: _tactics.State is TacticState.Retreat);
			}
			else
				_coverRefresh = 0.02f;
		}

		var situation = new CombatSituation(sees, distance, Perception.SecondsSinceCue, Injury(), _suppression,
			_coverPoint is not null, _arrived, _healItem is not null, _healed,
			PartnerIsEngaged(), _flankPoint is not null, HoldFlankNow);
		_tactics.Update(delta, situation);
		Decide(delta, situation);
		if (_tactics.State != _lastTactic)
		{
			OnTacticEntered(_tactics.State);
			_lastTactic = _tactics.State;
		}

		var plan = new CombatPlan(null, S(Stats.Move.RunSpeed), faceTarget, false);
		_wantsFire = false;
		_blindFiring = false;
		_leaning = false;
		switch (_tactics.State)
		{
			case TacticState.Advance:
				plan = plan with { Destination = MayAdvanceNow ? target : null, FaceYaw = MayAdvanceNow ? null : faceTarget };
				_wantsFire = sees && distance <= S(Stats.Tactics.EngageDistance) * 1.2f;
				break;
			case TacticState.Shoot:
				_wantsFire = sees;
				break;
			case TacticState.SeekCover:
				plan = plan with { Destination = _coverPoint, FaceYaw = null };
				break;
			case TacticState.Retreat:
				plan = plan with { Destination = _coverPoint, FaceYaw = null };
				break;
			case TacticState.Hide:
				plan = plan with
				{
					Destination = _coverPoint,
					Crouch = _coverClass != CoverClass.Standing,
					Prone = _coverClass == CoverClass.Prone && _arrived,
					Speed = S(Stats.Move.WalkSpeed),
				};
				// pinned and badly hurt: hold the gun round the cover and shoot at where the target was
				_blindFiring = Injury() > S(Stats.Cover.BlindInjury) && Perception.InterestPoint is not null && _arrived;
				_wantsFire = _blindFiring;
				break;
			case TacticState.Peek:
				plan = plan with
				{
					Destination = _peekPoint ?? _coverPoint,
					Speed = S(Stats.Move.WalkSpeed),
					Crouch = _coverClass == CoverClass.Prone,
				};
				_leaning = _coverClass == CoverClass.Standing && _leanSide != 0f && _arrived;
				_wantsFire = sees;
				break;
			case TacticState.Suppress:
				_wantsFire = true;
				break;
			case TacticState.Flank:
				plan = plan with { Destination = _flankPoint, FaceYaw = null };
				_wantsFire = sees;
				break;
			case TacticState.Heal:
				plan = plan with { Crouch = true, Destination = null };
				break;
			case TacticState.Flee:
				plan = plan with { Destination = _fleePoint, FaceYaw = null, Speed = S(Stats.Move.SprintSpeed) };
				break;
		}

		switch (_underFire.Phase)
		{
			case UnderFirePhase.Reacting:
			case UnderFirePhase.GettingUp:
				plan = plan with { Destination = null, FaceYaw = faceTarget };
				break;
			case UnderFirePhase.TakingCover:
				plan = plan with { Destination = _coverPoint, FaceYaw = null, Speed = S(Stats.Move.SprintSpeed), Crouch = false };
				break;
			case UnderFirePhase.Prone:
				plan = plan with { Destination = null, FaceYaw = faceTarget };
				break;
		}
		if (_underFire.BlocksFire || StanceLocked)
			_wantsFire = false;

		// Never open fire until roughly facing the target.
		if (_wantsFire)
		{
			var error = Mathf.Abs(Mathf.AngleDifference(Rotation.Y + Mathf.DegToRad(_pivot.TwistDeg), faceTarget));
			if (error > Mathf.DegToRad(25f))
				_wantsFire = false;
		}
		TrackTarget(target, sees, delta);
		SquadFrame(target, sees, delta);
		var point = sees ? target : target + Vector3.Up * 1.1f;
		if (sees && Weapon is { } gun)
		{
			var muzzle = gun.State.Chamber?.MuzzleVelocity ?? 700f;
			point = AiAimMotor.LeadPoint(gun.GlobalPosition, point, _targetVelocity, muzzle, _gravity, S(Stats.Aim.Accuracy));
		}
		_aimPoint = point;
		return plan;
	}

	private Temperament Temperament => new(S(Stats.Tactics.Aggression), 1f - S(Stats.Tactics.Courage), S(Stats.Temper.Panic));

	/// <summary>
	/// The utility layer: every 0.25 s, or at once when a state ends, score the actions and, if the best one is not
	/// served by the state the AI is in, switch to the state that starts it (AI combat v2 spec s7).
	/// </summary>
	private void Decide(float delta, in CombatSituation s)
	{
		if (_fleeing)
		{
			if (_tactics.State != TacticState.Flee)
				_tactics.Force(TacticState.Flee);
			_tactics.NeedsDecision = false;
			return;
		}
		_decideS -= delta;
		if (_decideS > 0f && !_tactics.NeedsDecision)
			return;
		_decideS = 0.25f;
		_tactics.NeedsDecision = false;
		var facts = new UtilityFacts(s.SeesTarget, s.TargetDistance, S(Stats.Tactics.EngageDistance), s.SecondsSinceSeen,
			s.Injury, s.Suppression, s.CoverAvailable, s.HealItemAvailable, s.Healed, s.PartnerEngaged, s.FlankPointAvailable,
			_tactics.BurstsInState, SquadRole);
		_action = AiUtility.Choose(facts, Temperament, _action, () => (float)_rng.NextDouble() - 0.5f);
		if (AiUtility.Serves(_tactics.State, _action))
			return;
		_tactics.Force(AiUtility.EntryState(_action, s.CoverAvailable));
		_tactics.NeedsDecision = false; // the forced entry should not trigger another score straight away
	}

	/// <summary>Where a fleeing AI runs: the reachable spot farthest from the threat that the threat cannot see.</summary>
	private Vector3? FindFleePoint(Vector3 threat)
	{
		var me = GlobalPosition;
		Vector3? best = null;
		var bestScore = float.MinValue;
		for (var i = 0; i < 16; i++)
		{
			var a = Mathf.Tau * i / 16f;
			var spot = SnapToNav(me + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 25f);
			var score = spot.DistanceTo(threat) + (Blocked(threat + Vector3.Up * 1.4f, spot + Vector3.Up * 1.4f) ? 20f : 0f);
			if (score > bestScore)
			{
				bestScore = score;
				best = spot;
			}
		}
		return best;
	}

	private void OnTacticEntered(TacticState state)
	{
		if (state == TacticState.Flee)
			_fleePoint = FindFleePoint(Perception.InterestPoint ?? GlobalPosition - GlobalBasis.Z * 10f);
		if (state == TacticState.Retreat)
		{
			_healStarted = false;
			_healed = false;
			_coverRefresh = 0f;
		}
		if (state == TacticState.Heal && !_healStarted && _healItem is { } item)
		{
			_healStarted = Health.UseMedical(item);
			if (_healStarted)
				NoiseBus.Emit(GlobalPosition, PerceptionEnvironment.CueRadius("heal"), "heal", this);
			if (!_healStarted)
				_healed = true;
		}
		if (state is TacticState.Peek or TacticState.Hide)
			_peekPoint = null;
		if (state == TacticState.Peek)
			_peekPoint = FindPeekPoint();
	}

	private bool PartnerIsEngaged()
	{
		foreach (var node in Perceivables())
		{
			if (IsInstanceValid(node) && node is AiHuman other && !ReferenceEquals(other, this) && other.Faction == Faction
				&& other.IsAlive && other.GlobalPosition.DistanceTo(GlobalPosition) < 35f
				&& other.CombatState is TacticState.Shoot or TacticState.Suppress or TacticState.Peek
					or TacticState.Hide)
				return true;
		}
		return false;
	}

	// --- cover, peek and flank points --------------------------------------------------------

	private void RefreshPoints(Vector3 target, bool retreat)
	{
		_coverPoint = FindCover(target, retreat);
		_flankPoint = FindFlankPoint(target);
	}

	private Vector3 SnapToNav(Vector3 p)
		=> NavigationServer3D.MapGetClosestPoint(_agent.GetNavigationMap(), p);

	private bool Blocked(Vector3 from, Vector3 to)
	{
		return Cast(PhysicsRayQueryParameters3D.Create(from, to, 1u)).Count > 0;
	}

	/// <summary>A spot about 70 degrees around the target with a clear line to it.</summary>
	private Vector3? FindFlankPoint(Vector3 target)
	{
		var me = GlobalPosition;
		var away = new Vector3(me.X - target.X, 0f, me.Z - target.Z);
		var distance = away.Length();
		if (distance < 1f)
			return null;
		var radius = Mathf.Clamp(distance * 0.8f, 10f, S(Stats.Tactics.EngageDistance));
		var dir = away.Normalized();
		foreach (var sign in new[] { 1f, -1f })
		{
			var rotated = dir.Rotated(Vector3.Up, Mathf.DegToRad(70f * sign));
			var p = SnapToNav(target + rotated * radius);
			if (!Blocked(p + Vector3.Up * 1.5f, target))
				return p;
		}
		return null;
	}

	// --- shooting ----------------------------------------------------------------------------

	/// <summary>Drives the trigger from the burst controller and reloads when empty.</summary>
	private void UpdateWeapon(float delta, bool inCombat)
	{
		_burst.Update(delta, inCombat && _wantsFire);
		if (Weapon is not { } w)
			return;
		UpdateMotor(delta, w);
		w.CanFire = true;
		w.TriggerHeld = _burst.Firing;
		w.TriggerPressed = _burst.Firing;
		if (w.State.RoundsAvailable == 0 && !w.Controller.IsBusy)
			w.Controller.StartReload(false);
		// Point the weapon at what it shoots at, or let it hang forward.
		if (inCombat && _wantsFire)
		{
			var aim = (_aimPoint - w.GlobalPosition).Normalized();
			if (aim.LengthSquared() > 0.5f)
				w.GlobalBasis = Basis.LookingAt(aim, Vector3.Up);
		}
		else
		{
			w.Rotation = Vector3.Zero;
		}
	}

	/// <summary>Target speed from its successive positions, smoothed; zero when it is not seen.</summary>
	private void TrackTarget(Vector3 target, bool sees, float delta)
	{
		if (!sees)
		{
			_hadTarget = false;
			_targetVelocity = Vector3.Zero;
			return;
		}
		if (_hadTarget && delta > 0.0001f)
		{
			var raw = (target - _lastTargetPos) / delta;
			_targetVelocity = _targetVelocity.Lerp(raw, Mathf.Clamp(delta * 6f, 0f, 1f));
		}
		else
			_motor.Disturb(); // a target just came into view: the aim starts rough
		_hadTarget = true;
		_lastTargetPos = target;
	}

	private void UpdateMotor(float delta, WeaponNode w)
	{
		var distance = GlobalPosition.DistanceTo(_aimPoint);
		var ads = distance > 10f ? 1f : 0f; // hip fire only up close
		var ctx = new HandlingContext(PerceivedStance, w.Stats.WeightKg, 1f, PerceivedSpeedFraction, ads, false);
		var sway = HandlingModel.SwayAmplitude(Handling, w.Data.SwayBaseDeg * w.Stats.SwayMult, ctx);
		_motor.Update(delta, new AimContext(distance, PerceivedSpeedFraction, _targetVelocity.Length() / 5.2f, _suppression,
			sway, HandlingModel.RecoilMultiplier(Handling, PerceivedStance, ads)), w.RecoilTuning);
	}

	/// <summary>Rounds this AI has fired (dev reporting).</summary>
	public int ShotsFired { get; private set; }

	private void OnShotFired(AmmoType ammo, Vector3 position, Vector3 direction)
	{
		ShotsFired++;
		_projectiles ??= GetTree().GetFirstNodeInGroup("projectiles") as ProjectileManager;
		if (_projectiles is null)
			return;
		if (Weapon is { } gun)
		{
			var ads = position.DistanceTo(_aimPoint) > 10f ? 1f : 0f;
			_motor.Shot(gun.RecoilTuning, HandlingModel.RecoilMultiplier(Handling, PerceivedStance, ads));
		}
		_motor.ErrorScale = (_blindFiring ? S(Stats.Cover.BlindErrorMult) : 1f) * (1f + 0.7f - S(Stats.Aim.Consistency));
		var dir = _motor.Direction(position, _aimPoint);
		// the gun and ammo themselves, in MOA, on top of the shooter's aim
		dir = Dispersion.Apply(dir, Dispersion.FinalMoa(Weapon?.Stats.Moa ?? 4f, 0f, ammo.Dispersion), () => (float)_rng.NextDouble());
		_projectiles.Spawn(ammo, position, dir, GetRid(), _ownRids);
		NoiseBus.Emit(position, Weapon?.NoiseRadius ?? 150f, "gunshot", this);
	}

	private static readonly StanceTransitionTuning StanceTimes = new();
	private Stance _stanceFrom = Stance.Stand, _stanceTo = Stance.Stand;
	private float _stanceSpan = 0.35f, _stanceS = 0.35f;

	private static Stance StanceOf(float scale) => scale >= 0.8f ? Stance.Stand : scale >= 0.5f ? Stance.Crouch : Stance.Prone;
	private static float ScaleOf(Stance s) => s switch { Stance.Stand => 1f, Stance.Crouch => 0.65f, _ => 0.35f };

	/// <summary>Going prone or getting up is under way: no fire, as for the player (the hands are off the weapon).</summary>
	private bool StanceLocked => (_stanceFrom == Stance.Prone || _stanceTo == Stance.Prone)
		&& Mathf.Abs(_crouchScale - ScaleOf(_stanceTo)) > 0.01f;

	private void ApplyCrouch(float delta, bool crouch, bool prone)
	{
		var target = prone ? 0.35f : crouch ? 0.65f : 1f;
		var targetStance = prone ? Stance.Prone : crouch ? Stance.Crouch : Stance.Stand;
		if (targetStance != _stanceTo)
		{
			// the same durations as the player's stance changes (animation spec s1.6)
			_stanceFrom = StanceOf(_crouchScale);
			_stanceTo = targetStance;
			_stanceSpan = Mathf.Max(0.05f, Mathf.Abs(ScaleOf(_stanceFrom) - ScaleOf(_stanceTo)));
			_stanceS = StanceTransition.DurationS(StanceTimes, _stanceFrom, _stanceTo);
		}
		var next = Mathf.MoveToward(_crouchScale, target, _stanceSpan / _stanceS * delta);
		if (next == _crouchScale)
			return; // nothing to move: the hitbox walk below is not free
		_crouchScale = next;
		HitboxRig.SetHeightScale(_hitboxRoot, next);
		if (_bodyMesh is not null && _rig is null)
			_bodyMesh.Scale = new Vector3(1f, next, 1f); // the animated body lowers its own hips instead
		_crouching = next < 0.85f;
		_prone = next < 0.5f;
		PlaceWeapon();
	}
}
