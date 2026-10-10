using Godot;

namespace ProjectD;

/// <summary>Health and armor-resolution tuning (GDD s8, s9). Placeholders to tune in playtest.</summary>
[GlobalClass]
public partial class HealthConfig : Resource
{
	[ExportGroup("Limb HP")]
	[Export] public float HeadHp { get; set; } = 35f;
	[Export] public float ThoraxHp { get; set; } = 85f;
	[Export] public float StomachHp { get; set; } = 70f;
	[Export] public float ArmHp { get; set; } = 60f;
	[Export] public float LegHp { get; set; } = 65f;
	/// <summary>Share of damage kept when a hit on a destroyed part spreads to the rest.</summary>
	[Export] public float DestroyedSpreadMultiplier { get; set; } = 0.6f;
	[Export] public bool SpreadHitsHead { get; set; }

	[ExportGroup("Armor resolution")]
	[Export] public float PenetratedDamageMultiplier { get; set; } = 0.8f;
	[Export] public float BluntDamageFraction { get; set; } = 0.25f;
	[Export] public float DurabilityPerDamagePenetrated { get; set; } = 0.1f;
	[Export] public float DurabilityPerDamageStopped { get; set; } = 0.08f;

	[ExportGroup("Status effects")]
	[Export] public float LightBleedPerS { get; set; } = 0.3f;
	[Export] public float HeavyBleedPerS { get; set; } = 1.0f;
	[Export] public float LightBleedChance { get; set; } = 0.25f;
	[Export] public float HeavyBleedChance { get; set; } = 0.15f;
	[Export] public float StoppedChanceMultiplier { get; set; } = 0.2f;
	[Export] public float FractureChance { get; set; } = 0.25f;
	[Export] public float FractureDamageThreshold { get; set; } = 25f;
	[Export] public float PainPerDamage { get; set; } = 0.8f;
	[Export] public float PainDecayPerS { get; set; } = 1.5f;
	[Export] public float PainFromFracture { get; set; } = 15f;
	[Export] public float MaxPain { get; set; } = 100f;
	[Export] public float PainEffectStart { get; set; } = 0.2f;
	[Export] public float ArmDestroyedSwayMultiplier { get; set; } = 2.0f;
	[Export] public float ArmFractureSwayMultiplier { get; set; } = 1.4f;
	[Export] public float PainSwayMultiplier { get; set; } = 1.6f;
	[Export] public float LegDestroyedSpeedMultiplier { get; set; } = 0.5f;
	[Export] public float LegFractureSpeedMultiplier { get; set; } = 0.7f;
	[Export] public float ArmDestroyedReloadMultiplier { get; set; } = 1.6f;

	[ExportGroup("Energy and hydration")]
	[Export] public float EnergyDrainPerS { get; set; } = 0.03f;
	[Export] public float HydrationDrainPerS { get; set; } = 0.04f;
	[Export] public float SprintDrainMultiplier { get; set; } = 4f;
	[Export] public float HeavyLoadDrainMultiplier { get; set; } = 1.5f;
	[Export] public float InjuredDrainMultiplier { get; set; } = 2f;
	[Export] public float LowThreshold { get; set; } = 25f;
	[Export] public float EmptyRecoveryMultiplier { get; set; } = 0.5f;
	[Export] public float EmptyHydrationSwayMultiplier { get; set; } = 1.4f;
	[Export] public float StarvationDamagePerS { get; set; } = 0.15f;

	public VitalsTuning ToVitalsTuning() => new(
		EnergyDrainPerS, HydrationDrainPerS, SprintDrainMultiplier, HeavyLoadDrainMultiplier,
		InjuredDrainMultiplier, LowThreshold, EmptyRecoveryMultiplier, EmptyHydrationSwayMultiplier,
		StarvationDamagePerS);

	public EffectsTuning ToEffectsTuning() => new(
		LightBleedPerS, HeavyBleedPerS, LightBleedChance, HeavyBleedChance, StoppedChanceMultiplier,
		FractureChance, FractureDamageThreshold, PainPerDamage, PainDecayPerS, PainFromFracture, MaxPain,
		PainEffectStart, ArmDestroyedSwayMultiplier, ArmFractureSwayMultiplier, PainSwayMultiplier,
		LegDestroyedSpeedMultiplier, LegFractureSpeedMultiplier, ArmDestroyedReloadMultiplier);

	public HealthTuning ToHealthTuning() => new(
		HeadHp, ThoraxHp, StomachHp, ArmHp, LegHp, DestroyedSpreadMultiplier, SpreadHitsHead);

	public DamageTuning ToDamageTuning() => new(
		PenetratedDamageMultiplier, BluntDamageFraction, DurabilityPerDamagePenetrated,
		DurabilityPerDamageStopped);
}
