using Godot;

namespace ProjectD;

/// <summary>
/// A named place on a map (M8c spec s4): the nemesis dossier says "near the water tower" from the nearest one to where
/// you died. Place it in map data; the generated graybox writes the layout's landmark markers as these.
/// </summary>
[Tool]
public partial class MapLandmark : Marker3D
{
	/// <summary>Text key of the name, for example "landmark.water_tower".</summary>
	[Export] public string NameKey { get; set; } = "";

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("map_landmarks");
	}

	public Landmark ToLandmark() => new(NameKey, GlobalPosition.X, GlobalPosition.Z);
}
