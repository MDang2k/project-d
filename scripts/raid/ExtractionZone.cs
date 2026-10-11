using Godot;

namespace ProjectD;

/// <summary>
/// An extraction point (GDD s15): a box the player must stand in for a few seconds. Place and
/// resize in the editor; <see cref="RaidManager"/> finds every zone in the group "extraction_zones".
/// </summary>
[Tool]
public partial class ExtractionZone : Node3D
{
	[Export] public string ZoneId { get; set; } = "extract";
	[Export] public ExtractKind Kind { get; set; } = ExtractKind.AlwaysOpen;
	[Export] public float Seconds { get; set; } = 5f;
	/// <summary>Item id that must be carried (conditional zones).</summary>
	[Export] public string RequiredItemId { get; set; } = "";
	[Export] public Vector3 Size { get; set; } = new(6f, 3f, 6f);
	/// <summary>Draws a faint box for the blockout; art replaces it with a real landmark later.</summary>
	[Export] public bool ShowMarker { get; set; } = true;

	public ExtractionSpec ToSpec()
		=> new(ZoneId, Kind, Seconds, string.IsNullOrEmpty(RequiredItemId) ? null : RequiredItemId);

	/// <summary>True if the world point is inside the zone box.</summary>
	public bool Contains(Vector3 worldPoint)
	{
		var local = GlobalTransform.AffineInverse() * worldPoint;
		var half = Size * 0.5f;
		return Mathf.Abs(local.X) <= half.X && Mathf.Abs(local.Y) <= half.Y && Mathf.Abs(local.Z) <= half.Z;
	}

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("extraction_zones");
		if (!ShowMarker)
			return;
		var color = Kind == ExtractKind.AlwaysOpen ? new Color(0.2f, 0.9f, 0.3f, 0.12f) : new Color(0.9f, 0.7f, 0.2f, 0.12f);
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = Size },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = color,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}
}
