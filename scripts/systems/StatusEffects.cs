using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum BleedLevel { None, Light, Heavy }

/// <summary>Status-effect numbers (GDD s9). Placeholders to tune in playtest.</summary>
public sealed record EffectsTuning(
	float LightBleedPerS,
	float HeavyBleedPerS,
	/// <summary>Chance a damaging hit causes a light bleed on the part.</summary>
	float LightBleedChance,
	/// <summary>Chance a penetrating or unarmored hit causes a heavy bleed.</summary>
	float HeavyBleedChance,
	/// <summary>Multiplier on bleed chances when armor stopped the bullet.</summary>
	float StoppedChanceMultiplier,
	float FractureChance,
	/// <summary>Minimum damage to an arm or leg that can fracture it.</summary>
	float FractureDamageThreshold,
	float PainPerDamage,
	float PainDecayPerS,
	float PainFromFracture,
	float MaxPain,
	/// <summary>Pain above this fraction of MaxPain starts to show effects.</summary>
	float PainEffectStart,
	float ArmDestroyedSwayMultiplier,
	float ArmFractureSwayMultiplier,
	float PainSwayMultiplier,
	float LegDestroyedSpeedMultiplier,
	float LegFractureSpeedMultiplier,
	float ArmDestroyedReloadMultiplier)
{
	public static EffectsTuning Default => new(
		LightBleedPerS: 0.3f, HeavyBleedPerS: 1.0f, LightBleedChance: 0.25f, HeavyBleedChance: 0.15f,
		StoppedChanceMultiplier: 0.2f, FractureChance: 0.25f, FractureDamageThreshold: 25f,
		PainPerDamage: 0.8f, PainDecayPerS: 1.5f, PainFromFracture: 15f, MaxPain: 100f, PainEffectStart: 0.2f,
		ArmDestroyedSwayMultiplier: 2.0f, ArmFractureSwayMultiplier: 1.4f, PainSwayMultiplier: 1.6f,
		LegDestroyedSpeedMultiplier: 0.5f, LegFractureSpeedMultiplier: 0.7f, ArmDestroyedReloadMultiplier: 1.6f);
}

/// <summary>Gameplay penalties derived from injuries and pain (GDD s9). Consumed by handling and movement.</summary>
public readonly record struct Penalties(
	float SwayMultiplier,
	float MoveSpeedMultiplier,
	bool SprintBlocked,
	float ReloadTimeMultiplier,
	/// <summary>0..1 screen-edge blur and tremor strength.</summary>
	float PainStrength);

/// <summary>
/// Bleeds, fractures, pain and heal-over-time for one character (GDD s9). Triggers are chance
/// per hit with an injected random source so tests are deterministic. Pure logic.
/// </summary>
public sealed class StatusEffects
{
	private static readonly BodyPart[] Limbs =
		{ BodyPart.LeftArm, BodyPart.RightArm, BodyPart.LeftLeg, BodyPart.RightLeg };

	private readonly EffectsTuning _t;
	private readonly Dictionary<BodyPart, BleedLevel> _bleed = new();
	private readonly HashSet<BodyPart> _fractures = new();
	private float _healPool;
	private float _healRate;

	public StatusEffects(EffectsTuning tuning) => _t = tuning;

	public float Pain { get; private set; }
	/// <summary>Seconds of morphine left; pain effects are suppressed while above 0.</summary>
	public float MorphineLeft { get; private set; }
	public bool PainSuppressed => MorphineLeft > 0f;
	public float HealPoolLeft => _healPool;

	public BleedLevel Bleed(BodyPart part) => _bleed.TryGetValue(part, out var b) ? b : BleedLevel.None;
	public bool IsFractured(BodyPart part) => _fractures.Contains(part);
	public IReadOnlyCollection<BodyPart> Fractures => _fractures;
	public IEnumerable<BodyPart> BleedingParts => _bleed.Keys;

	/// <summary>Fractures, bleeds, pain and healing in progress, to carry into another map.</summary>
	public EffectsState State() => new(_fractures.ToList(), new Dictionary<BodyPart, BleedLevel>(_bleed), Pain, MorphineLeft, _healPool, _healRate);

	public void LoadState(EffectsState state)
	{
		_fractures.Clear();
		foreach (var f in state.Fractures)
			_fractures.Add(f);
		_bleed.Clear();
		foreach (var (part, level) in state.Bleeds)
			_bleed[part] = level;
		Pain = state.Pain;
		MorphineLeft = state.MorphineLeft;
		_healPool = state.HealPool;
		_healRate = state.HealRate;
	}

	/// <summary>Rolls bleed and fracture chances for a resolved hit; adds pain.</summary>
	public void OnHit(in HitResult hit, Func<double> random)
	{
		if (hit.Damage <= 0f)
			return;
		var mult = hit.ArmorHit && !hit.Penetrated ? _t.StoppedChanceMultiplier : 1f;
		var heavyChance = _t.HeavyBleedChance * mult;
		var lightChance = _t.LightBleedChance * mult;
		if (hit.Penetrated && random() < heavyChance)
			SetBleed(hit.Part, BleedLevel.Heavy);
		else if (random() < lightChance)
			SetBleed(hit.Part, BleedLevel.Light);

		var isLimb = Array.IndexOf(Limbs, hit.Part) >= 0;
		if (isLimb && hit.Damage >= _t.FractureDamageThreshold && random() < _t.FractureChance * mult
			&& _fractures.Add(hit.Part))
			Pain = Math.Min(_t.MaxPain, Pain + _t.PainFromFracture);
		Pain = Math.Min(_t.MaxPain, Pain + hit.Damage * _t.PainPerDamage);
	}

	/// <summary>A destroyed part has nothing left to bleed or break.</summary>
	public void OnLimbDestroyed(BodyPart part)
	{
		_bleed.Remove(part);
		_fractures.Remove(part);
	}

	public void Update(float delta, BodyHealth body)
	{
		if (body.IsDead)
			return;
		foreach (var (part, level) in _bleed.ToList())
		{
			if (body.IsDestroyed(part))
			{
				_bleed.Remove(part);
				continue;
			}
			var rate = level == BleedLevel.Heavy ? _t.HeavyBleedPerS : _t.LightBleedPerS;
			body.TakeDamage(part, rate * delta);
		}
		Pain = Math.Max(0f, Pain - _t.PainDecayPerS * delta);
		MorphineLeft = Math.Max(0f, MorphineLeft - delta);
		HealOverTime(delta, body);
	}

	private void HealOverTime(float delta, BodyHealth body)
	{
		if (_healPool <= 0f)
			return;
		var budget = Math.Min(_healPool, _healRate * delta);
		var candidates = BodyHealth.AllParts
			.Where(p => !body.IsDestroyed(p) && body.Hp(p) < body.MaxHp(p))
			.OrderByDescending(p => body.MaxHp(p) - body.Hp(p))
			.ToList();
		if (candidates.Count == 0)
		{
			_healPool = 0f;
			return;
		}
		var part = candidates[0];
		var healed = Math.Min(budget, body.MaxHp(part) - body.Hp(part));
		body.Heal(part, healed);
		_healPool -= healed;
	}

	// --- treatments -------------------------------------------------------------------------

	/// <summary>Cures a light bleed (the given part, or the first one).</summary>
	public bool Bandage(BodyPart? target = null) => Cure(BleedLevel.Light, target, includeHeavy: false);

	/// <summary>Cures a heavy bleed, or a light one if no heavy bleed exists.</summary>
	public bool Tourniquet(BodyPart? target = null)
		=> Cure(BleedLevel.Heavy, target, includeHeavy: true) || Cure(BleedLevel.Light, target, includeHeavy: false);

	public bool Splint(BodyPart? target = null)
	{
		var part = target is { } t && _fractures.Contains(t) ? t : _fractures.Cast<BodyPart?>().FirstOrDefault();
		return part is { } p && _fractures.Remove(p);
	}

	public void Morphine(float durationS) => MorphineLeft = Math.Max(MorphineLeft, durationS);

	public void StartHealOverTime(float pool, float ratePerS)
	{
		_healPool += pool;
		_healRate = Math.Max(_healRate, ratePerS);
	}

	/// <summary>True if treating this level on this target would do something.</summary>
	public bool HasBleed(BleedLevel level, BodyPart? target = null)
		=> _bleed.Any(kv => kv.Value == level && (target is null || kv.Key == target));

	private bool Cure(BleedLevel level, BodyPart? target, bool includeHeavy)
	{
		foreach (var (part, current) in _bleed)
		{
			if (target is not null && part != target)
				continue;
			if (current == level || (includeHeavy && current == BleedLevel.Heavy))
			{
				_bleed.Remove(part);
				return true;
			}
		}
		return false;
	}

	/// <summary>Breaks and/or makes a part bleed without a roll (a mine blast); a destroyed part has nothing left to injure.</summary>
	public void Inflict(BodyPart part, bool fracture, BleedLevel bleed, BodyHealth body)
	{
		if (body.IsDestroyed(part))
			return;
		if (bleed != BleedLevel.None)
			SetBleed(part, bleed);
		if (fracture && Array.IndexOf(Limbs, part) >= 0 && _fractures.Add(part))
			Pain = Math.Min(_t.MaxPain, Pain + _t.PainFromFracture);
	}

	private void SetBleed(BodyPart part, BleedLevel level)
	{
		// A heavy bleed never downgrades to light.
		if (Bleed(part) == BleedLevel.Heavy && level == BleedLevel.Light)
			return;
		_bleed[part] = level;
	}

	// --- penalties --------------------------------------------------------------------------

	public Penalties ComputePenalties(BodyHealth body)
	{
		var sway = 1f;
		var speed = 1f;
		var reload = 1f;
		var sprintBlocked = false;
		foreach (var arm in new[] { BodyPart.LeftArm, BodyPart.RightArm })
		{
			if (body.IsDestroyed(arm))
			{
				sway *= _t.ArmDestroyedSwayMultiplier;
				reload *= _t.ArmDestroyedReloadMultiplier;
			}
			else if (IsFractured(arm))
			{
				sway *= _t.ArmFractureSwayMultiplier;
			}
		}
		foreach (var leg in new[] { BodyPart.LeftLeg, BodyPart.RightLeg })
		{
			if (body.IsDestroyed(leg))
			{
				speed *= _t.LegDestroyedSpeedMultiplier;
				sprintBlocked = true;
			}
			else if (IsFractured(leg))
			{
				speed *= _t.LegFractureSpeedMultiplier;
			}
		}
		var strength = PainSuppressed ? 0f : PainStrengthRaw();
		sway *= 1f + (_t.PainSwayMultiplier - 1f) * strength;
		return new Penalties(sway, speed, sprintBlocked, reload, strength);
	}

	private float PainStrengthRaw()
	{
		var fraction = _t.MaxPain > 0f ? Pain / _t.MaxPain : 0f;
		if (fraction <= _t.PainEffectStart)
			return 0f;
		return Math.Clamp((fraction - _t.PainEffectStart) / (1f - _t.PainEffectStart), 0f, 1f);
	}
}
