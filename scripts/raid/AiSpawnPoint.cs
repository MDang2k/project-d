using Godot;

namespace ProjectD;

/// <summary>An AI start position with the preset that should spawn there. Move in the editor.</summary>
[Tool]
public partial class AiSpawnPoint : Marker3D
{
	[Export] public string PresetId { get; set; } = "bandit";

	/// <summary>
	/// Points with the same group are thinned each raid to between <see cref="RollMin"/> and <see cref="RollMax"/> of them
	/// (see <see cref="SpawnRoll"/>); empty means always active.
	/// </summary>
	[Export] public string RollGroup { get; set; } = "";
	[Export] public int RollMin { get; set; }
	[Export] public int RollMax { get; set; }

	/// <summary>Radius of the patrol loop built around this point.</summary>
	[Export] public float PatrolRadius { get; set; } = 10f;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("ai_spawns");
	}
}
