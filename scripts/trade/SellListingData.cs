using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>One thing a trader sells, in a .tres file.</summary>
[GlobalClass]
public partial class SellListingData : Resource
{
	[Export] public string ItemId { get; set; } = "";
	[Export] public float PriceMultiplier { get; set; } = 1f;

	public ListingSpec ToSpec() => new(ItemId, PriceMultiplier);
}
