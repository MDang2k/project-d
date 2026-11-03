using Godot;

namespace ProjectD;

/// <summary>
/// Where a timed wave appears (raid map standard s5). One marker can serve several arrivals ("8,16,24") and one map can
/// have several markers; <see cref="WaveSpawner"/> reads them from the group "wave_spawns".
/// </summary>
[Tool]
public partial class WaveSpawnPoint : Marker3D
{
	/// <summary>patrol (friendly PMCs relieving a post) or bandit (hostile to everyone).</summary>
	[Export] public string Wave { get; set; } = "patrol";

	/// <summary>Raid minutes of each arrival, comma separated.</summary>
	[Export] public string AtMinutes { get; set; } = "8";

	[Export] public string PresetId { get; set; } = "bandit";

	[Export] public int Count { get; set; } = 3;

	/// <summary>west or east for a patrol wave; empty for bandits.</summary>
	[Export] public string Side { get; set; } = "";

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("wave_spawns");
	}
}
