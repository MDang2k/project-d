using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Supplies reloads from the player's real inventory (GDD s7, s10): the fullest compatible
/// magazine in pockets, rig or backpack, and takes the old one back (or leaves it on the ground).
/// </summary>
public sealed class InventoryMagazineStore : IMagazineStore
{
	private readonly CharacterInventory _inventory;
	private readonly string? _caliber;
	private readonly System.Action _changed;
	private readonly System.Action<ItemInstance> _dropToWorld;

	public InventoryMagazineStore(CharacterInventory inventory, string? caliber,
		System.Action changed, System.Action<ItemInstance> dropToWorld)
	{
		_inventory = inventory;
		_caliber = caliber;
		_changed = changed;
		_dropToWorld = dropToWorld;
	}

	public Magazine? TakeFullest()
	{
		ItemInstance? best = null;
		foreach (var grid in _inventory.StorageGrids)
		{
			foreach (var item in grid.Items)
			{
				if (item.Spec.Category != ItemCategory.Magazine || item.Spec.Caliber != _caliber || item.Rounds.Count == 0)
					continue;
				if (best is null || item.Rounds.Count > best.Rounds.Count)
					best = item;
			}
		}
		if (best is null)
			return null;
		_inventory.Remove(best);
		_changed();
		return ToMagazine(best);
	}

	public void Return(Magazine magazine)
	{
		var item = FromMagazine(magazine, _caliber);
		if (item is null)
			return;
		if (!_inventory.TryAutoStore(item))
			_dropToWorld(item);
		_changed();
	}

	/// <summary>Builds the runtime magazine for an item; the item rides along as the tag.</summary>
	public static Magazine ToMagazine(ItemInstance item)
	{
		var m = new Magazine(item.Spec.MagazineCapacity) { Tag = item };
		foreach (var id in item.Rounds)
			m.Push(ItemDb.AmmoTypeById(id) ?? new AmmoType(id, 40f, 2, 700f));
		return m;
	}

	/// <summary>Writes a runtime magazine back onto its item (or creates one for debug magazines).</summary>
	public static ItemInstance? FromMagazine(Magazine magazine, string? caliber)
	{
		var item = magazine.Tag as ItemInstance;
		if (item is null && caliber is not null && ItemDb.MagazineFor(caliber) is { } data)
			item = ItemDb.Create(data.Id);
		if (item is null)
			return null;
		item.Rounds.Clear();
		item.Rounds.AddRange(magazine.RoundsBottomToTop.Select(r => r.Id));
		return item;
	}
}
