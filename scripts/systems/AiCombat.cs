using System;
using Godot;

namespace ProjectD;

// --- aiming ---------------------------------------------------------------------------------

/// <summary>Random cone jitter, used for gun and ammo dispersion. AI shooters aim through <see cref="AiAimMotor"/>.</summary>
public static class AiAim
{
	/// <summary>Rotates a direction by a random angle inside a cone of the given half-angle.</summary>
	public static Vector3 Jitter(Vector3 direction, float spreadDeg, Func<float> random01)
	{
		var dir = direction.Normalized();
		if (spreadDeg <= 0.0001f)
			return dir;
		var angle = Mathf.DegToRad(spreadDeg) * MathF.Sqrt(random01());
		var roll = random01() * Mathf.Tau;
		var helper = MathF.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up;
		var right = dir.Cross(helper).Normalized();
		var up = right.Cross(dir).Normalized();
		var offset = right * MathF.Cos(roll) + up * MathF.Sin(roll);
		return (dir * MathF.Cos(angle) + offset * MathF.Sin(angle)).Normalized();
	}
}

/// <summary>
/// Trigger discipline: a short reaction delay, then bursts of a few rounds with pauses between them.
/// Outputs the trigger flags to feed the weapon each physics frame.
/// </summary>
public sealed class AiBurstFire
{
	private enum Phase { Idle, Reacting, Burst, Pause }

	private readonly float _interval;
	private int _minRounds, _maxRounds;
	private float _pauseMin, _pauseMax, _reaction;
	private readonly Random _rng;
	private Phase _phase = Phase.Idle;
	private float _timer;

	public AiBurstFire(float roundsPerMinute, int minRounds, int maxRounds, float pauseMinS, float pauseMaxS,
		float reactionS, Random rng)
	{
		_interval = 60f / Math.Max(1f, roundsPerMinute);
		_rng = rng;
		Retune(minRounds, maxRounds, pauseMinS, pauseMaxS, reactionS);
	}

	/// <summary>New burst numbers (the actor's stats changed); a burst or pause under way finishes as it was.</summary>
	public void Retune(int minRounds, int maxRounds, float pauseMinS, float pauseMaxS, float reactionS)
	{
		_minRounds = Math.Max(1, minRounds);
		_maxRounds = Math.Max(_minRounds, maxRounds);
		_pauseMin = pauseMinS;
		_pauseMax = Math.Max(pauseMinS, pauseMaxS);
		_reaction = reactionS;
	}

	/// <summary>Trigger down this frame (feeds both pressed and held; the weapon applies its fire mode).</summary>
	public bool Firing { get; private set; }

	public event Action? BurstFinished;

	public void Update(float delta, bool wantsFire)
	{
		Firing = false;
		if (!wantsFire)
		{
			_phase = Phase.Idle;
			return;
		}
		switch (_phase)
		{
			case Phase.Idle:
				_phase = Phase.Reacting;
				_timer = _reaction;
				break;
			case Phase.Reacting:
				_timer -= delta;
				if (_timer <= 0f)
					StartBurst();
				break;
			case Phase.Burst:
				_timer -= delta;
				Firing = true;
				if (_timer <= 0f)
				{
					_phase = Phase.Pause;
					_timer = Mathf.Lerp(_pauseMin, _pauseMax, (float)_rng.NextDouble());
					Firing = false;
					BurstFinished?.Invoke();
				}
				break;
			case Phase.Pause:
				_timer -= delta;
				if (_timer <= 0f)
					StartBurst();
				break;
		}
	}

	private void StartBurst()
	{
		var rounds = _rng.Next(_minRounds, _maxRounds + 1);
		_phase = Phase.Burst;
		// A little over n intervals so n rounds go out at the weapon's rate.
		_timer = rounds * _interval - _interval * 0.5f;
	}
}

// --- tactics --------------------------------------------------------------------------------

/// <summary>Combat decision numbers for one AI (GDD s16: aggression and courage are preset values).</summary>
public sealed record TacticsTuning(
	/// <summary>0 cowardly .. 1 fearless: when injury or suppression makes the AI back off.</summary>
	float Courage,
	/// <summary>0 passive .. 1 pushes and flanks.</summary>
	float Aggression,
	float EngageDistance,
	float HideMinS,
	float HideMaxS,
	float PeekS,
	float SuppressS,
	float HealS,
	/// <summary>Bursts fired from open ground before looking for cover.</summary>
	int BurstsBeforeCover,
	float FlankMinDistance,
	float FlankCooldownS)
{
	public static TacticsTuning Default => new(0.5f, 0.5f, 25f, 1.5f, 3.5f, 2.5f, 3f, 8f, 3, 12f, 20f);
}

public enum TacticState { Advance, Shoot, SeekCover, Hide, Peek, Suppress, Flank, Retreat, Heal, Flee }

/// <summary>Facts the combat state machine reacts to, gathered by the AI node each frame.</summary>
public readonly record struct CombatSituation(
	bool SeesTarget,
	float TargetDistance,
	float SecondsSinceSeen,
	/// <summary>0 unhurt .. 1 about to die.</summary>
	float Injury,
	/// <summary>0..1 recent incoming fire.</summary>
	float Suppression,
	bool CoverAvailable,
	/// <summary>Reached the point asked for by the current state (cover, flank point).</summary>
	bool AtDestination,
	bool HealItemAvailable,
	/// <summary>Healing finished (item use done or bleeding stopped).</summary>
	bool Healed,
	bool PartnerEngaged,
	bool FlankPointAvailable);

/// <summary>
/// Combat behaviors: advance, shoot, take cover, peek, suppress, flank, retreat, heal, flee. Pure logic
/// layered under <see cref="AiBrain"/>'s Combat state; the node executes the chosen state. With
/// <see cref="UtilityControlled"/> the choice between actions belongs to <see cref="AiUtility"/> and the states
/// only sequence themselves (hide, peek, arrive, finish); without it the older built-in rules decide.
/// </summary>
public sealed class CombatTactics
{
	private TacticsTuning _t;
	private readonly Random _rng;
	private float _hideFor;
	private int _bursts;
	private float _flankCooldown;
	private float _retreatLock;

	/// <summary>Replaced when the actor's stats change.</summary>
	public TacticsTuning Tuning { get => _t; set => _t = value; }

	public CombatTactics(TacticsTuning tuning, Random rng)
	{
		_t = tuning;
		_rng = rng;
	}

	/// <summary>The utility layer picks actions; the built-in cross-action rules are off.</summary>
	public bool UtilityControlled { get; set; }

	/// <summary>A state ended or changed, so the utility layer should score again now instead of at its next tick.</summary>
	public bool NeedsDecision { get; set; }

	public TacticState State { get; private set; } = TacticState.Advance;
	public float StateTimeS { get; private set; }

	/// <summary>Called by the shooter each time a burst ends.</summary>
	public void OnBurstFinished() => _bursts++;

	/// <summary>Bursts fired in this state (for the utility scorer).</summary>
	public int BurstsInState => _bursts;

	/// <summary>Combat starts or restarts (target spotted anew).</summary>
	public void Begin() => Enter(TacticState.Advance);

	/// <summary>The utility layer chose an action this state does not serve.</summary>
	public void Force(TacticState next) => Enter(next);

	private void Enter(TacticState next)
	{
		// Once a retreat is over, do not start another straight away (it would flicker with Hide).
		if (State is TacticState.Retreat or TacticState.Heal && next is not (TacticState.Retreat or TacticState.Heal))
			_retreatLock = 10f;
		State = next;
		StateTimeS = 0f;
		_bursts = 0;
		NeedsDecision = true;
		if (next == TacticState.Hide)
			_hideFor = Mathf.Lerp(_t.HideMinS, _t.HideMaxS, (float)_rng.NextDouble());
	}

	private float RetreatInjury => Mathf.Lerp(0.35f, 0.85f, _t.Courage);
	private float CoverSuppression => Mathf.Lerp(0.3f, 0.8f, _t.Courage);

	public void Update(float delta, in CombatSituation s)
	{
		StateTimeS += delta;
		_flankCooldown = Math.Max(0f, _flankCooldown - delta);
		_retreatLock = Math.Max(0f, _retreatLock - delta);

		// Badly hurt: pull back to cover and patch up, whatever else is going on.
		if (!UtilityControlled && s.Injury >= RetreatInjury && State is not (TacticState.Retreat or TacticState.Heal) && s.CoverAvailable
			&& _retreatLock <= 0f)
		{
			Enter(TacticState.Retreat);
			return;
		}

		switch (State)
		{
			case TacticState.Retreat:
				if (s.AtDestination && !s.SeesTarget)
					Enter(s.HealItemAvailable && !s.Healed ? TacticState.Heal : TacticState.Hide);
				else if (StateTimeS > 12f)
					Enter(TacticState.Hide);
				break;

			case TacticState.Heal:
				if (s.Healed || StateTimeS > _t.HealS)
					Enter(TacticState.Hide);
				else if (s.SeesTarget && s.TargetDistance < 10f)
					Enter(TacticState.Shoot);
				break;

			case TacticState.Advance:
				if (!UtilityControlled && s.Suppression > CoverSuppression && s.CoverAvailable)
					Enter(TacticState.SeekCover);
				else if (s.SeesTarget && s.TargetDistance <= _t.EngageDistance)
					Enter(TacticState.Shoot);
				break;

			case TacticState.Shoot:
				if (UtilityControlled)
				{
					if (s.SeesTarget && s.TargetDistance > _t.EngageDistance * 1.25f)
						Enter(TacticState.Advance);
				}
				else if (s.Suppression > CoverSuppression && s.CoverAvailable
					|| _bursts >= _t.BurstsBeforeCover && s.CoverAvailable && _t.Courage < 0.9f)
					Enter(TacticState.SeekCover);
				else if (TryFlank(s))
					Enter(TacticState.Flank);
				else if (!s.SeesTarget && StateTimeS > 1.2f)
					Enter(TacticState.Suppress);
				else if (s.SeesTarget && s.TargetDistance > _t.EngageDistance * 1.25f)
					Enter(TacticState.Advance);
				break;

			case TacticState.SeekCover:
				if (s.AtDestination)
					Enter(TacticState.Hide);
				else if (StateTimeS > 6f)
					Enter(TacticState.Shoot);
				break;

			case TacticState.Hide:
				// Heavy fire keeps heads down a little longer.
				var hold = _hideFor * (1f + s.Suppression);
				if (StateTimeS >= hold)
					Enter(TacticState.Peek);
				break;

			case TacticState.Peek:
				if (!s.SeesTarget && StateTimeS > 1.5f)
					Enter(s.SecondsSinceSeen < 12f ? TacticState.Suppress : TacticState.Hide);
				else if (_bursts >= 2 || StateTimeS > _t.PeekS)
					Enter(TacticState.Hide);
				break;

			case TacticState.Suppress:
				if (s.SeesTarget)
					Enter(TacticState.Shoot);
				else if (StateTimeS >= _t.SuppressS && UtilityControlled)
					Enter(TacticState.Hide);
				else if (StateTimeS >= _t.SuppressS)
					Enter(TryFlank(s) ? TacticState.Flank
						: _t.Aggression > 0.5f ? TacticState.Advance : TacticState.Hide);
				break;

			case TacticState.Flank:
				if (s.SeesTarget && s.TargetDistance <= _t.EngageDistance * 0.7f || s.AtDestination
					|| StateTimeS > 14f)
					Enter(TacticState.Shoot);
				else if (s.Suppression > 0.8f && s.CoverAvailable)
					Enter(TacticState.SeekCover);
				break;
		}
	}

	/// <summary>Pairs flank: while the partner keeps the target busy, the more aggressive one swings around.</summary>
	private bool TryFlank(in CombatSituation s)
	{
		if (!s.PartnerEngaged || !s.FlankPointAvailable || _flankCooldown > 0f || s.TargetDistance < _t.FlankMinDistance)
			return false;
		if (_rng.NextDouble() > _t.Aggression)
		{
			_flankCooldown = 3f; // roll again in a few seconds, not every frame
			return false;
		}
		_flankCooldown = _t.FlankCooldownS;
		return true;
	}
}
