using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>A barter offer in a .tres file: the items handed over and the item received.</summary>
[GlobalClass]
public partial class BarterOfferData : Resource
{
	[Export] public string[] GiveIds { get; set; } = System.Array.Empty<string>();
	[Export] public int[] GiveCounts { get; set; } = System.Array.Empty<int>();
	[Export] public string GetId { get; set; } = "";
	[Export] public int GetCount { get; set; } = 1;

	public BarterSpec ToSpec() => new(
		GiveIds.Select((id, i) => (id, i < GiveCounts.Length ? GiveCounts[i] : 1)).ToList(), GetId, GetCount);
}
