using Godot;

namespace ProjectD;

/// <summary>Medical item definition (GDD s9). Placeholder numbers, tune in data/items/*.tres.</summary>
[GlobalClass]
public partial class MedicalData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string NameKey { get; set; } = "";
	[Export] public MedicalKind Kind { get; set; } = MedicalKind.Bandage;
	[Export] public float UseTimeS { get; set; } = 3f;
	[Export] public float WeightKg { get; set; } = 0.1f;
	/// <summary>Morphine: how long pain stays suppressed.</summary>
	[Export] public float EffectDurationS { get; set; }
	/// <summary>Field medkit: total HP restored over time, and how fast.</summary>
	[Export] public float HealPool { get; set; }
	[Export] public float HealRatePerS { get; set; }
	/// <summary>Surgery kit: restored limb max HP as a fraction of normal until the raid ends.</summary>
	[Export] public float RestoredMaxHpFraction { get; set; } = 0.5f;

	public MedicalSpec ToSpec()
		=> new(Kind, UseTimeS, EffectDurationS, HealPool, HealRatePerS, RestoredMaxHpFraction);
}
