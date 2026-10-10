using Godot;

namespace ProjectD;

/// <summary>Cartridge definition (GDD s8). Placeholder numbers, tune in data/ammo/*.tres.</summary>
[GlobalClass]
public partial class AmmoData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string Caliber { get; set; } = "";
	[Export] public float Damage { get; set; } = 40f;
	/// <summary>Penetration tier 1-5.</summary>
	[Export] public int PenetrationTier { get; set; } = 2;
	/// <summary>Muzzle velocity in m/s.</summary>
	[Export] public float MuzzleVelocity { get; set; } = 700f;

	public AmmoType ToType() => new(Id, Damage, PenetrationTier, MuzzleVelocity);
}
