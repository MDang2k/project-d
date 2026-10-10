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

	public HealthTuning ToHealthTuning() => new(
		HeadHp, ThoraxHp, StomachHp, ArmHp, LegHp, DestroyedSpreadMultiplier, SpreadHitsHead);

	public DamageTuning ToDamageTuning() => new(
		PenetratedDamageMultiplier, BluntDamageFraction, DurabilityPerDamagePenetrated,
		DurabilityPerDamageStopped);
}
