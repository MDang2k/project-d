using Godot;

namespace ProjectD;

/// <summary>Per-map raid rules (GDD s15). Tune in data/raid/*.tres.</summary>
[GlobalClass]
public partial class RaidConfig : Resource
{
	[Export] public string MapId { get; set; } = "map1";
	/// <summary>Raid length in seconds; the first map is 20 minutes.</summary>
	[Export] public float DurationS { get; set; } = 1200f;
	/// <summary>How many of the map's AI spawn points are used each raid (random subset).</summary>
	[Export] public int AiCount { get; set; } = 10;
	/// <summary>AI spawn points closer than this to the player's start are skipped, metres.</summary>
	[Export] public float AiMinDistanceFromPlayer { get; set; } = 30f;
}
