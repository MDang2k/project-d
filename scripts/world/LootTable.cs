using System;
using Godot;

namespace ProjectD;

/// <summary>Loot table (GDD s19 value tiers): weighted item ids with stack ranges. Tune in data/loot.</summary>
[GlobalClass]
public partial class LootTable : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string[] ItemIds { get; set; } = Array.Empty<string>();
	[Export] public float[] Weights { get; set; } = Array.Empty<float>();
	[Export] public int[] MinCounts { get; set; } = Array.Empty<int>();
	[Export] public int[] MaxCounts { get; set; } = Array.Empty<int>();
	[Export] public int MinRolls { get; set; } = 3;
	[Export] public int MaxRolls { get; set; } = 6;

	public System.Collections.Generic.List<LootEntry> ToEntries()
	{
		var list = new System.Collections.Generic.List<LootEntry>();
		for (var i = 0; i < ItemIds.Length; i++)
		{
			list.Add(new LootEntry(ItemIds[i],
				i < Weights.Length ? Weights[i] : 1f,
				i < MinCounts.Length ? MinCounts[i] : 1,
				i < MaxCounts.Length ? MaxCounts[i] : 1));
		}
		return list;
	}
}
