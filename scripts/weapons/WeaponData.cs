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

	[ExportGroup("Handling")]
	/// <summary>Vertical kick per shot in degrees.</summary>
	[Export] public float RecoilVerticalDeg { get; set; } = 1.1f;
	[Export] public float RecoilHorizontalDeg { get; set; } = 0.5f;
	[Export] public float RecoilRecoveryDegS { get; set; } = 6f;
	[Export] public float RecoilRecoveryDelayS { get; set; } = 0.1f;
	[Export] public float AdsTimeS { get; set; } = 0.22f;
	/// <summary>Peak sway in degrees before stance, stamina and weight modifiers.</summary>
	[Export] public float SwayBaseDeg { get; set; } = 0.35f;

	[Export] public bool HasBipod { get; set; }

	/// <summary>How far a shot carries (metres) for AI hearing. Suppressors will lower this later.</summary>
	[Export] public float NoiseRadius { get; set; } = 150f;

	[ExportGroup("Actions (seconds)")]
	[Export] public float TacticalReloadS { get; set; } = 2.4f;
	[Export] public float FastReloadS { get; set; } = 1.6f;
	[Export] public float MagCheckS { get; set; } = 1.2f;
	[Export] public float ChamberCheckS { get; set; } = 0.9f;
	/// <summary>Extra time to charge the weapon when the chamber is empty at reload.</summary>
	[Export] public float ChargeS { get; set; } = 0.7f;

	[ExportGroup("Placeholder model")]
	/// <summary>Box size in metres; the barrel points along -Z, Muzzle sits at the front.</summary>
	[Export] public Vector3 BodySize { get; set; } = new(0.05f, 0.1f, 0.8f);
	[Export] public Vector3 GripRightOffset { get; set; } = new(0f, -0.05f, 0.05f);
	[Export] public Vector3 GripLeftOffset { get; set; } = new(0f, -0.04f, -0.25f);

	public ActionTuning ToActionTuning() => ActionTuning.Default with
	{
		TacticalReloadS = TacticalReloadS,
		FastReloadS = FastReloadS,
		MagCheckS = MagCheckS,
		ChamberCheckS = ChamberCheckS,
		ChargeS = ChargeS,
	};

	public RecoilTuning ToRecoilTuning()
		=> new(RecoilVerticalDeg, RecoilHorizontalDeg, RecoilRecoveryDegS, RecoilRecoveryDelayS);

	/// <summary>Height of the sight line above the weapon origin (placeholder model).</summary>
	public float SightHeight => BodySize.Y * 0.5f + 0.025f;

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
