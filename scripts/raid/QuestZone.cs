using Godot;

namespace ProjectD;

/// <summary>
/// A box the player must walk into to progress a Locate objective (GDD s14). Place and resize it in
/// the editor; <see cref="RaidQuests"/> raises the event once per raid.
/// </summary>
[Tool]
public partial class QuestZone : Node3D
{
	[Export] public string ZoneId { get; set; } = "zone";
	/// <summary>Map the zone belongs to; empty means any map.</summary>
	[Export] public string MapId { get; set; } = "";
	[Export] public Vector3 Size { get; set; } = new(6f, 3f, 6f);
	/// <summary>Draws a faint blue box for the blockout (the player normally gets no marker).</summary>
	[Export] public bool ShowMarker { get; set; }

	public bool Contains(Vector3 worldPoint)
	{
		var local = GlobalTransform.AffineInverse() * worldPoint;
		var half = Size * 0.5f;
		return Mathf.Abs(local.X) <= half.X && Mathf.Abs(local.Y) <= half.Y && Mathf.Abs(local.Z) <= half.Z;
	}

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("quest_zones");
		if (!ShowMarker && !Engine.IsEditorHint())
			return;
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = Size },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.3f, 0.5f, 1f, 0.1f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}
}
