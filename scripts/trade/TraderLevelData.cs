using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>One trader level (GDD s13): catalog, what it buys, price numbers, barters.</summary>
[GlobalClass]
public partial class TraderLevelData : Resource
{
	[Export] public Array<SellListingData> Sells { get; set; } = new();
	/// <summary>Item category names the trader buys (Weapon, Magazine, Ammo, Armor, Helmet, Rig, Backpack, Medical, Food, Drink, Melee, Misc).</summary>
	[Export] public string[] BuyCategories { get; set; } = System.Array.Empty<string>();
	[Export] public float BuyMultiplier { get; set; } = 1.25f;
	[Export] public float SellFraction { get; set; } = 0.45f;
	[Export] public Array<BarterOfferData> Barters { get; set; } = new();

	public TraderLevelSpec ToSpec()
	{
		var categories = BuyCategories
			.Select(n => System.Enum.TryParse<ItemCategory>(n, out var c) ? (ItemCategory?)c : null)
			.Where(c => c is not null).Select(c => c!.Value).ToHashSet();
		return new TraderLevelSpec(
			Sells.Select(s => s.ToSpec()).ToList(), categories, BuyMultiplier, SellFraction,
			Barters.Select(b => b.ToSpec()).ToList());
	}
}
