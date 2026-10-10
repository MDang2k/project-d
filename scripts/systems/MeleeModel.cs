using System;

namespace ProjectD;

/// <summary>Melee weapon numbers (GDD s11). Placeholders to tune in data/items.</summary>
public sealed record MeleeSpec(
	string Id,
	/// <summary>Damage dealt to the single limb that is hit.</summary>
	float Damage,
	/// <summary>Counts as ammo penetration tier for armor (GDD: low-penetration).</summary>
	int PenetrationTier,
	float Range,
	float WindupS,
	float RecoverS,
	/// <summary>Arm stamina spent per attack.</summary>
	float StaminaCost,
	/// <summary>True: the hit skips armor entirely (GDD s11 decision).</summary>
	bool IgnoresArmor = true);

public enum MeleePhase { Idle, Windup, Recover }

/// <summary>
/// Quick melee attack timing (GDD s11): press, windup, strike on the last windup frame, recover.
/// Pure logic; the node does the hit test when <see cref="Update"/> reports a strike.
/// </summary>
public sealed class MeleeModel
{
	private MeleeSpec _spec;
	private float _t;

	public MeleeModel(MeleeSpec spec) => _spec = spec;

	public MeleePhase Phase { get; private set; }
	public bool IsBusy => Phase != MeleePhase.Idle;
	public MeleeSpec Spec => _spec;

	/// <summary>0..1 over the whole attack (windup then recover) for poses.</summary>
	public float Progress
	{
		get
		{
			var total = _spec.WindupS + _spec.RecoverS;
			return total > 0f ? Math.Min((Phase == MeleePhase.Idle ? 0f : _t) / total, 1f) : 0f;
		}
	}

	public void SetSpec(MeleeSpec spec)
	{
		_spec = spec;
		Phase = MeleePhase.Idle;
		_t = 0f;
	}

	/// <summary>Starts an attack if idle and the arms can pay for it.</summary>
	public bool TryStart(bool armsExhausted)
	{
		if (IsBusy || armsExhausted)
			return false;
		Phase = MeleePhase.Windup;
		_t = 0f;
		return true;
	}

	/// <returns>True on the frame the strike lands (end of windup).</returns>
	public bool Update(float delta)
	{
		if (Phase == MeleePhase.Idle)
			return false;
		_t += delta;
		if (Phase == MeleePhase.Windup && _t >= _spec.WindupS)
		{
			Phase = MeleePhase.Recover;
			return true;
		}
		if (Phase == MeleePhase.Recover && _t >= _spec.WindupS + _spec.RecoverS)
		{
			Phase = MeleePhase.Idle;
			_t = 0f;
		}
		return false;
	}
}
