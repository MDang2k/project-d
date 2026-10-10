using Godot;

namespace ProjectD;

/// <summary>Melee weapon (GDD s11): knife, bayonet, entrenching tool. Tune in data/items/*.tres.</summary>
[GlobalClass]
public partial class MeleeData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string NameKey { get; set; } = "";
	[Export] public float Damage { get; set; } = 30f;
	[Export] public int PenetrationTier { get; set; } = 1;
	[Export] public float Range { get; set; } = 1.6f;
	[Export] public float WindupS { get; set; } = 0.15f;
	[Export] public float RecoverS { get; set; } = 0.45f;
	[Export] public float StaminaCost { get; set; } = 8f;
	[Export] public float WeightKg { get; set; } = 0.3f;
	/// <summary>Melee skips armor: full damage to the limb and no durability loss.</summary>
	[Export] public bool IgnoresArmor { get; set; } = true;

	public MeleeSpec ToSpec()
		=> new(Id, Damage, PenetrationTier, Range, WindupS, RecoverS, StaminaCost, IgnoresArmor);
}
