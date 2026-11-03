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

	/// <summary>Seconds of warning before a PMC patrol wave arrives (truck engines, radio).</summary>
	[Export] public float WavePatrolLeadS { get; set; } = 20f;
	/// <summary>Seconds of warning before a bandit wave arrives (shouting, a flare).</summary>
	[Export] public float WaveBanditLeadS { get; set; } = 15f;
	/// <summary>A wave does not appear closer to the player than this, metres.</summary>
	[Export] public float WaveMinPlayerDistanceM { get; set; } = 60f;
	/// <summary>A wave held back by a nearby player appears anyway after this long, seconds.</summary>
	[Export] public float WaveRetryMaxS { get; set; } = 60f;

	/// <summary>Actor id of this map's boss (the Bosses sheet row); empty for no boss.</summary>
	[Export] public string BossActorId { get; set; } = "";
	/// <summary>Actor id of the boss's escorts.</summary>
	[Export] public string BossEscortId { get; set; } = "";
	[Export] public int BossEscorts { get; set; } = 3;
	/// <summary>Chance the boss appears in a raid; the Bosses sheet "Spawn chance".</summary>
	[Export] public float BossChance { get; set; } = 0.3f;

	public WaveTuning ToWaveTuning() => new(WavePatrolLeadS, WaveBanditLeadS, WaveMinPlayerDistanceM, WaveRetryMaxS);
}
