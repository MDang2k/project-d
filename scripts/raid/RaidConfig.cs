using Godot;

namespace ProjectD;

/// <summary>Per-map raid rules (GDD s15). Tune in data/raid/*.tres.</summary>
[GlobalClass]
public partial class RaidConfig : Resource
{
	[Export] public string MapId { get; set; } = "map1";
	/// <summary>Raid length in seconds; the first map is 20 minutes.</summary>
	[Export] public float DurationS { get; set; } = 1200f;
}
