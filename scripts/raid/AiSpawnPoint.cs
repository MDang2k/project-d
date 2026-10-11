using Godot;

namespace ProjectD;

/// <summary>An AI start position with the preset that should spawn there. Move in the editor.</summary>
[Tool]
public partial class AiSpawnPoint : Marker3D
{
	[Export] public string PresetId { get; set; } = "bandit";

	/// <summary>Radius of the patrol loop built around this point.</summary>
	[Export] public float PatrolRadius { get; set; } = 10f;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("ai_spawns");
	}
}
