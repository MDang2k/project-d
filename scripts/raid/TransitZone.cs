using Godot;

namespace ProjectD;

/// <summary>
/// A tunnel end or a road to another map (world map spec s3, Border Strip spec s4): stand in the box for a few seconds
/// while meeting its gate. <see cref="TransitManager"/> reads every zone in the group "transit_zones". Place and resize in
/// the editor.
/// </summary>
[Tool]
public partial class TransitZone : Node3D
{
	[Export] public string ZoneId { get; set; } = "transit";
	[Export] public TransitKind Kind { get; set; } = TransitKind.Road;
	[Export] public float Seconds { get; set; } = 6f;
	/// <summary>Banknotes charged on crossing, taken from carried storage grids.</summary>
	[Export] public int Cost { get; set; }
	/// <summary>Item that must be carried; empty for none.</summary>
	[Export] public string RequiredItemId { get; set; } = "";
	/// <summary>Tunnel: the zone at the other end.</summary>
	[Export] public string PairId { get; set; } = "";
	/// <summary>Road: map id of the destination map (see data/maps/registry.json).</summary>
	[Export] public string Destination { get; set; } = "";
	[Export] public Vector3 Size { get; set; } = new(6f, 3f, 6f);
	/// <summary>Draws a faint box for the blockout; art replaces it later.</summary>
	[Export] public bool ShowMarker { get; set; } = true;

	public TransitSpec ToSpec() => new(ZoneId, Kind, Seconds, Cost, RequiredItemId == "" ? null : RequiredItemId, PairId, Destination);

	public bool Contains(Vector3 worldPoint)
	{
		var local = GlobalTransform.AffineInverse() * worldPoint;
		var half = Size * 0.5f;
		return Mathf.Abs(local.X) <= half.X && Mathf.Abs(local.Y) <= half.Y && Mathf.Abs(local.Z) <= half.Z;
	}

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("transit_zones");
		if (!ShowMarker)
			return;
		var color = Kind == TransitKind.Tunnel ? new Color(0.6f, 0.4f, 0.9f, 0.14f) : new Color(0.2f, 0.75f, 0.85f, 0.14f);
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
