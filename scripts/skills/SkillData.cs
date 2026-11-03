using System;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>A skill as authored in data/skills/*.tres (M8a spec): XP table and the actions that train it. Tune here, not in code.</summary>
[GlobalClass]
public partial class SkillData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string NameKey { get; set; } = "";
	/// <summary>Cumulative XP to reach levels 1 to 10; the first is 0.</summary>
	[Export] public float[] XpTable { get; set; } = Array.Empty<float>();
	/// <summary>Action ids and their base XP, side by side.</summary>
	[Export] public string[] ActionIds { get; set; } = Array.Empty<string>();
	[Export] public float[] ActionXp { get; set; } = Array.Empty<float>();
	/// <summary>Each repeat of an action in one raid pays this fraction of the one before.</summary>
	[Export] public float Decay { get; set; } = 0.9f;
	[Export] public float DecayFloor { get; set; } = 0.1f;

	public SkillSpec ToSpec() => new(Id, XpTable, ActionIds.Zip(ActionXp).ToDictionary(p => p.First, p => p.Second), Decay, DecayFloor);
}
