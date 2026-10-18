using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>
/// A dead body is searched like in Tarkov (GDD s10): worn gear and the weapon are visible at once,
/// the contents of pockets, rig and backpack are hidden and revealed one by one while searching.
/// Pure logic: builds the search model around the dead character's inventory.
/// </summary>
public static class CorpseLoot
{
	public static LootContainerModel Build(CharacterInventory body, float secondsPerItem, float noiseRadius)
	{
		// The model's own grid is unused for bodies; everything lives in the body inventory.
		var model = new LootContainerModel(1, 1, secondsPerItem, noiseRadius);
		foreach (var item in HiddenItems(body))
			model.MarkHidden(item);
		return model;
	}

	/// <summary>Everything that sits inside a grid of the body (pockets, rig, backpack), nested items included.</summary>
	public static IEnumerable<ItemInstance> HiddenItems(CharacterInventory body)
	{
		var grids = body.Pockets.Concat(body.EquippedItems.SelectMany(kv => kv.Value.Grids)).ToList();
		foreach (var grid in grids)
		{
			foreach (var item in grid.Items.ToList())
			{
				foreach (var nested in InsuranceService.WithNested(item).Where(i => i == item || !ReferenceEquals(i, item.InsertedMagazine)))
					yield return nested;
			}
		}
	}
}
