using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>One line of a starter kit: an item, how many, and for magazines the ammo and round count.</summary>
/// <param name="Rounds">Magazines only: rounds loaded; -1 fills the magazine.</param>
public sealed record StarterEntry(string ItemId, int Count = 1, string? AmmoId = null, int Rounds = -1);

/// <summary>A side's starter kit and its safety-net kit (GDD s5, s10). Contents come from data resources.</summary>
public sealed record StarterKitSpec(IReadOnlyList<StarterEntry> Entries, IReadOnlyList<StarterEntry> SafetyNet);

/// <summary>Result of placing generated items into a grid.</summary>
public sealed record PlaceResult(int Placed, List<ItemInstance> Dropped);

/// <summary>Builds kits into a grid. Pure logic: items come from an injected factory.</summary>
public static class StarterKit
{
	public static IEnumerable<ItemInstance> Flatten(ItemInstance item)
	{
		if (item.InsertedMagazine is { } mag)
		{
			yield return mag;
		}
		foreach (var grid in item.Grids)
		{
			foreach (var nested in grid.Items)
			{
				yield return nested;
				foreach (var deeper in Flatten(nested))
					yield return deeper;
			}
		}
	}

	/// <summary>Creates the items of a kit; magazines are loaded with their ammo.</summary>
	public static List<ItemInstance> Build(IEnumerable<StarterEntry> entries, Func<string, int, ItemInstance?> make)
	{
		var items = new List<ItemInstance>();
		foreach (var e in entries)
		{
			var count = Math.Max(1, e.Count);
			var probe = make(e.ItemId, 1);
			if (probe is null)
				continue;
			if (probe.Spec.MagazineCapacity > 0)
			{
				for (var i = 0; i < count; i++)
				{
					var mag = i == 0 ? probe : make(e.ItemId, 1)!;
					var rounds = e.Rounds < 0 ? mag.Spec.MagazineCapacity : Math.Min(e.Rounds, mag.Spec.MagazineCapacity);
					for (var r = 0; r < rounds && e.AmmoId is not null; r++)
						mag.Rounds.Add(e.AmmoId);
					items.Add(mag);
				}
				continue;
			}
			// Stackable items go in as stacks of at most MaxStack; unstackable ones one by one.
			var remaining = count;
			var perStack = Math.Max(1, probe.Spec.MaxStack);
			var first = true;
			while (remaining > 0)
			{
				var n = Math.Min(perStack, remaining);
				var item = first ? probe : make(e.ItemId, 1)!;
				first = false;
				item.Count = n;
				items.Add(item);
				remaining -= n;
			}
		}
		return items;
	}

	/// <summary>Places items anywhere they fit; the rest are returned as dropped (the hub never blocks).</summary>
	public static PlaceResult PlaceInGrid(InventoryGrid grid, IEnumerable<ItemInstance> items)
	{
		var placed = 0;
		var dropped = new List<ItemInstance>();
		foreach (var item in items)
		{
			if (grid.TryPlaceAnywhere(item))
				placed++;
			else
				dropped.Add(item);
		}
		return new PlaceResult(placed, dropped);
	}
}

/// <summary>
/// The "no weapon anywhere" rule (GDD s5, s10): when the character and the stash hold no weapon,
/// the hub issues the side's basic kit so the player is never stuck.
/// </summary>
public static class SafetyNet
{
	public static bool HasWeapon(IEnumerable<ItemInstance> items)
		=> items.Any(i => i.Spec.Category == ItemCategory.Weapon);

	public static bool NeedsIssue(CharacterInventory character, GameProfile profile)
		=> !HasWeapon(character.AllItems()) && !HasWeapon(profile.AllStashItems());

	/// <summary>Issues the safety-net kit into the stash when needed; null when nothing was issued.</summary>
	public static PlaceResult? Issue(CharacterInventory character, GameProfile profile, StarterKitSpec spec,
		Func<string, int, ItemInstance?> make)
	{
		if (!NeedsIssue(character, profile))
			return null;
		return StarterKit.PlaceInGrid(profile.Stash, StarterKit.Build(spec.SafetyNet, make));
	}
}
