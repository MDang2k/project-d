using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>Armor definition: tier 1-5, durability and protected zones (GDD s8).</summary>
public sealed record ArmorSpec(string Id, int Tier, float MaxDurability, IReadOnlyCollection<BodyPart> Zones);

/// <summary>A worn armor piece; at 0 durability its tier effectively drops to 0.</summary>
public sealed class ArmorState
{
	public ArmorSpec Spec { get; }
	public float Durability { get; private set; }

	/// <param name="durability">Starting durability; null means full.</param>
	public ArmorState(ArmorSpec spec, float? durability = null)
	{
		Spec = spec;
		Durability = Math.Clamp(durability ?? spec.MaxDurability, 0f, spec.MaxDurability);
	}

	public int EffectiveTier => Durability > 0f ? Spec.Tier : 0;

	public bool Protects(BodyPart part) => Spec.Zones.Contains(part);

	public void Wear(float amount) => Durability = Math.Max(0f, Durability - amount);
}

/// <summary>Armor resolution numbers (GDD s8). Placeholders to tune in playtest.</summary>
public sealed record DamageTuning(
	/// <summary>Damage kept when the bullet penetrates the armor.</summary>
	float PenetratedDamageMultiplier,
	/// <summary>Damage the limb takes when armor stops the bullet (blunt).</summary>
	float BluntDamageFraction,
	/// <summary>Durability lost per point of ammo damage when penetrating.</summary>
	float DurabilityPerDamagePenetrated,
	/// <summary>Durability lost per point of ammo damage when stopped.</summary>
	float DurabilityPerDamageStopped)
{
	public static DamageTuning Default => new(
		PenetratedDamageMultiplier: 0.8f, BluntDamageFraction: 0.25f,
		DurabilityPerDamagePenetrated: 0.1f, DurabilityPerDamageStopped: 0.08f);
}

/// <param name="ArmorHit">An armor piece with a tier above 0 covered the zone.</param>
public readonly record struct HitResult(
	BodyPart Part,
	float Damage,
	bool ArmorHit,
	bool Penetrated,
	float DurabilityLoss,
	ArmorState? Armor);

/// <summary>Turns a bullet hit on a body part into damage and armor wear (GDD s8). Pure logic.</summary>
public static class DamageResolver
{
	/// <summary>Resolves a hit; armor durability is reduced as part of the result.</summary>
	public static HitResult Resolve(AmmoType ammo, BodyPart part, IEnumerable<ArmorState> armor, DamageTuning t)
		=> Resolve(ammo.Damage, ammo.PenetrationTier, part, armor, t);

	/// <summary>Damage that bypasses armor entirely (melee, GDD s11): full damage, no durability wear.</summary>
	public static HitResult Unmitigated(BodyPart part, float damage)
		=> new(part, damage, false, true, 0f, null);

	/// <summary>Same, for non-bullet sources that are still stopped by armor tiers.</summary>
	public static HitResult Resolve(float damage, int penetrationTier, BodyPart part,
		IEnumerable<ArmorState> armor, DamageTuning t)
	{
		foreach (var a in armor)
		{
			if (!a.Protects(part) || a.EffectiveTier <= 0)
				continue;
			var penetrates = penetrationTier >= a.EffectiveTier;
			var wear = damage * (penetrates ? t.DurabilityPerDamagePenetrated : t.DurabilityPerDamageStopped);
			var before = a.Durability;
			a.Wear(wear);
			var applied = penetrates
				? damage * t.PenetratedDamageMultiplier
				: damage * t.BluntDamageFraction;
			return new HitResult(part, applied, true, penetrates, before - a.Durability, a);
		}
		return new HitResult(part, damage, false, true, 0f, null);
	}
}
