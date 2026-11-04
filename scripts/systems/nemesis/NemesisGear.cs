using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>A top-level item the player lost, and the slot it was worn in (null for something carried in a pocket).</summary>
public sealed record LostItem(ItemInstance Item, EquipSlot? Slot);

/// <summary>What a nemesis takes from a dead player and how it puts it on (M8c spec s1 and s2). Pure logic.</summary>
public static class NemesisGear
{
	/// <summary>
	/// The lost gear without anything insurance is sending back: an item is either with the nemesis or returned, never
	/// both. The originals of returned items are pulled out of whatever held them (a backpack, a rig, a weapon).
	/// </summary>
	public static List<LostItem> Salvage(IEnumerable<LostItem> lost, IReadOnlyCollection<ItemInstance> returnedOriginals)
	{
		var gone = new HashSet<ItemInstance>(returnedOriginals);
		var kept = new List<LostItem>();
		foreach (var l in lost)
		{
			if (gone.Contains(l.Item))
				continue;
			Prune(l.Item, gone);
			kept.Add(l);
		}
		return kept;
	}

	private static void Prune(ItemInstance item, HashSet<ItemInstance> gone)
	{
		if (item.InsertedMagazine is { } mag)
		{
			if (gone.Contains(mag))
				item.InsertedMagazine = null;
			else
				Prune(mag, gone);
		}
		foreach (var grid in item.Grids)
		{
			foreach (var nested in grid.Items.ToList())
			{
				if (gone.Contains(nested))
					grid.Remove(nested);
				else
					Prune(nested, gone);
			}
		}
	}

	/// <summary>
	/// Puts the gear on: each item goes to the slot it was worn in when that is free; armor and helmets replace worse ones
	/// (higher tier wins); everything else is carried. Items that fit nowhere are lost with the nemesis's pockets full.
	/// Returns how many items had no room.
	/// </summary>
	public static int Equip(CharacterInventory gear, IEnumerable<LostItem> items, Func<ItemSpec, int> armorTier)
	{
		var noRoom = 0;
		foreach (var l in items.OrderByDescending(i => Priority(i.Slot)))
		{
			if (l.Slot is { } slot && CharacterInventory.Accepts(slot, l.Item))
			{
				var current = gear.InSlot(slot);
				var replace = current is null
					|| (slot is EquipSlot.Head or EquipSlot.Vest && armorTier(l.Item.Spec) > armorTier(current.Spec));
				if (replace && gear.TryEquip(l.Item, slot))
					continue;
			}
			if (!gear.TryAutoStore(l.Item))
				noRoom++;
		}
		return noRoom;
	}

	// worn things first so the backpack and rig exist before the pocket items go looking for room
	private static int Priority(EquipSlot? slot) => slot switch
	{
		EquipSlot.Backpack or EquipSlot.Rig => 3,
		not null => 2,
		_ => 1,
	};

	/// <summary>The weapon the nemesis fights with: primary, then secondary, then pistol; null falls back to its preset's weapon.</summary>
	public static ItemInstance? BestWeapon(CharacterInventory gear)
		=> gear.InSlot(EquipSlot.Primary) ?? gear.InSlot(EquipSlot.Secondary) ?? gear.InSlot(EquipSlot.Pistol);

	/// <summary>Every item the nemesis carries, flattened, for the dossier and for tests.</summary>
	public static IReadOnlyList<ItemInstance> Carried(CharacterInventory gear) => gear.AllItems().ToList();
}
