using Godot;

namespace ProjectD;

/// <summary>Weapon definition (GDD s11). Placeholder numbers, tune in data/weapons/*.tres.</summary>
[GlobalClass]
public partial class WeaponData : Resource
{
	[Export] public string Id { get; set; } = "";
	/// <summary>Localization key for the display name.</summary>
	[Export] public string NameKey { get; set; } = "";
	[Export] public AmmoData? Ammo { get; set; }
	[Export] public int MagazineCapacity { get; set; } = 30;
	[Export] public float RoundsPerMinute { get; set; } = 600f;
	[Export] public bool AllowsSemi { get; set; } = true;
	[Export] public bool AllowsAuto { get; set; } = true;
	[Export] public float WeightKg { get; set; } = 3.5f;

	[ExportGroup("Placeholder model")]
	/// <summary>Box size in metres; the barrel points along -Z, Muzzle sits at the front.</summary>
	[Export] public Vector3 BodySize { get; set; } = new(0.05f, 0.1f, 0.8f);
	[Export] public Vector3 GripRightOffset { get; set; } = new(0f, -0.05f, 0.05f);
	[Export] public Vector3 GripLeftOffset { get; set; } = new(0f, -0.04f, -0.25f);

	public FireMode[] FireModes()
	{
		var modes = new System.Collections.Generic.List<FireMode>();
		if (AllowsSemi)
			modes.Add(FireMode.Semi);
		if (AllowsAuto)
			modes.Add(FireMode.Auto);
		return modes.Count > 0 ? modes.ToArray() : new[] { FireMode.Semi };
	}
}
