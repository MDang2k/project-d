using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum BodyPart { Head, Thorax, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }

/// <summary>Per-limb health numbers (GDD s9). HP values are placeholders to tune.</summary>
public sealed record HealthTuning(
	float HeadHp,
	float ThoraxHp,
	float StomachHp,
	float ArmHp,
	float LegHp,
	/// <summary>Share of damage kept when a hit on a destroyed part is spread to the rest.</summary>
	float DestroyedSpreadMultiplier,
	/// <summary>If false the head is spared by spread damage.</summary>
	bool SpreadHitsHead)
{
	public static HealthTuning Default => new(
		HeadHp: 35f, ThoraxHp: 85f, StomachHp: 70f, ArmHp: 60f, LegHp: 65f,
		DestroyedSpreadMultiplier: 0.6f, SpreadHitsHead: false);

	public float MaxHp(BodyPart part) => part switch
	{
		BodyPart.Head => HeadHp,
		BodyPart.Thorax => ThoraxHp,
		BodyPart.Stomach => StomachHp,
		BodyPart.LeftArm or BodyPart.RightArm => ArmHp,
		_ => LegHp,
	};
}

/// <summary>
/// Tarkov-style per-limb health (GDD s9). Head or thorax at 0 HP is death; other parts become
/// destroyed, and further damage to a destroyed part spreads over the remaining parts.
/// Pure logic.
/// </summary>
public sealed class BodyHealth
{
	public static readonly BodyPart[] AllParts = Enum.GetValues<BodyPart>();

	private readonly HealthTuning _t;
	private readonly Dictionary<BodyPart, float> _hp = new();
	private readonly Dictionary<BodyPart, float> _max = new();

	public bool IsDead { get; private set; }
	public BodyPart? CauseOfDeath { get; private set; }

	public event Action<BodyPart>? LimbDestroyed;
	public event Action<BodyPart>? Died;

	public BodyHealth(HealthTuning tuning)
	{
		_t = tuning;
		foreach (var p in AllParts)
		{
			_hp[p] = tuning.MaxHp(p);
			_max[p] = tuning.MaxHp(p);
		}
	}

	public float Hp(BodyPart part) => _hp[part];
	public float MaxHp(BodyPart part) => _max[part];
	public bool IsDestroyed(BodyPart part) => _hp[part] <= 0f;
	public static bool IsVital(BodyPart part) => part is BodyPart.Head or BodyPart.Thorax;
	public float TotalHp => _hp.Values.Sum();

	/// <summary>Current and maximum HP of every part, to carry the body into another map.</summary>
	public BodyState State() => new(AllParts.ToDictionary(p => p, p => (_hp[p], _max[p])));

	/// <summary>Puts a saved state back; limbs at 0 HP stay destroyed. Does nothing for a dead body.</summary>
	public void LoadState(BodyState state)
	{
		foreach (var (part, (hp, max)) in state.Parts)
		{
			_max[part] = max;
			_hp[part] = Math.Clamp(hp, 0f, max);
		}
	}

	/// <summary>Applies damage to a part (spreading it if the part is already destroyed).</summary>
	public void TakeDamage(BodyPart part, float amount)
	{
		if (IsDead || amount <= 0f)
			return;
		if (!IsDestroyed(part))
		{
			Apply(part, amount);
			return;
		}
		// Vital parts are never "destroyed and alive", so only limbs and stomach spread.
		var targets = AllParts
			.Where(p => !IsDestroyed(p) && p != part && (_t.SpreadHitsHead || p != BodyPart.Head))
			.ToList();
		if (targets.Count == 0)
			return;
		var share = amount * _t.DestroyedSpreadMultiplier / targets.Count;
		foreach (var p in targets)
			Apply(p, share);
	}

	/// <summary>Restores HP to a part (clamped to its current maximum); destroyed parts stay destroyed.</summary>
	public void Heal(BodyPart part, float amount)
	{
		if (IsDead || IsDestroyed(part) || amount <= 0f)
			return;
		_hp[part] = Math.Min(_max[part], _hp[part] + amount);
	}

	/// <summary>Brings a destroyed non-vital part back with a reduced maximum (surgery kit).</summary>
	public bool Restore(BodyPart part, float maxHpFraction)
	{
		if (IsDead || IsVital(part) || !IsDestroyed(part))
			return false;
		_max[part] = _t.MaxHp(part) * Math.Clamp(maxHpFraction, 0.05f, 1f);
		_hp[part] = _max[part];
		return true;
	}

	private void Apply(BodyPart part, float amount)
	{
		var wasAlive = _hp[part] > 0f;
		_hp[part] = Math.Max(0f, _hp[part] - amount);
		if (!wasAlive || _hp[part] > 0f)
			return;
		if (IsVital(part))
		{
			IsDead = true;
			CauseOfDeath = part;
			Died?.Invoke(part);
		}
		else
		{
			LimbDestroyed?.Invoke(part);
		}
	}
}
