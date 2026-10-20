using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum EquipSlot { Head, Vest, Rig, Backpack, Primary, Secondary, Pistol, Melee, SecureContainer }

/// <summary>Where an item currently is: in an equipment slot or in a grid.</summary>
public readonly record struct ItemLocation(EquipSlot? Slot, InventoryGrid? Grid);

/// <summary>
/// Everything one character wears and carries (GDD s10): equipment slots, two small pocket
/// grids, and the grids provided by the worn rig, backpack and secure container. Pure logic.
/// </summary>
public sealed class CharacterInventory
{
	public static readonly GridSize[] DefaultPockets = { new(1, 2), new(1, 2) };

	private readonly Dictionary<EquipSlot, ItemInstance> _slots = new();

	public CharacterInventory(IEnumerable<GridSize>? pockets = null)
	{
		Pockets = (pockets ?? DefaultPockets).Select(p => new InventoryGrid(p.Width, p.Height)).ToList();
	}

	public IReadOnlyList<InventoryGrid> Pockets { get; }

	public ItemInstance? InSlot(EquipSlot slot) => _slots.TryGetValue(slot, out var i) ? i : null;

	public IEnumerable<KeyValuePair<EquipSlot, ItemInstance>> EquippedItems => _slots;

	/// <summary>Grids you can put things into during a raid (pockets, rig, backpack); not the secure container.</summary>
	public IEnumerable<InventoryGrid> StorageGrids
	{
		get
		{
			foreach (var p in Pockets)
				yield return p;
			foreach (var slot in new[] { EquipSlot.Vest, EquipSlot.Rig, EquipSlot.Backpack })
			{
				if (InSlot(slot) is { } item)
				{
					foreach (var g in item.Grids)
						yield return g;
				}
			}
		}
	}

	public IEnumerable<InventoryGrid> SecureGrids => InSlot(EquipSlot.SecureContainer)?.Grids ?? new List<InventoryGrid>();

	public IEnumerable<InventoryGrid> AllGrids => StorageGrids.Concat(SecureGrids);

	/// <summary>Total weight of everything worn and carried (drives mobility tiers, GDD s6).</summary>
	public float TotalWeightKg
		=> _slots.Values.Sum(i => i.WeightKg) + Pockets.Sum(p => p.Items.Sum(i => i.WeightKg));

	/// <summary>Weight that does not include the secure container (its contents are safe, not worn).</summary>
	public float CarriedWeightKg => TotalWeightKg;

	public static bool SlotAccepts(EquipSlot slot, ItemSpec spec) => slot switch
	{
		EquipSlot.Head => spec.Category == ItemCategory.Helmet,
		EquipSlot.Vest => spec.Category == ItemCategory.Armor,
		EquipSlot.Rig => spec.Category == ItemCategory.Rig,
		EquipSlot.Backpack => spec.Category == ItemCategory.Backpack,
		EquipSlot.Primary or EquipSlot.Secondary
			=> spec.Category == ItemCategory.Weapon && spec.WeaponClass == WeaponClass.Primary,
		EquipSlot.Pistol => spec.Category == ItemCategory.Weapon && spec.WeaponClass == WeaponClass.Pistol,
		EquipSlot.Melee => spec.Category == ItemCategory.Melee,
		EquipSlot.SecureContainer => spec.Category == ItemCategory.SecureContainer,
		_ => false,
	};

	/// <summary>Slot rules for a concrete item: the spec fits and a weapon has all its required parts.</summary>
	public static bool Accepts(EquipSlot slot, ItemInstance item)
		=> SlotAccepts(slot, item.Spec)
			&& (item.Spec.Category != ItemCategory.Weapon || WeaponBuild.IsComplete(item));

	public ItemLocation? Locate(ItemInstance item)
	{
		foreach (var (slot, i) in _slots)
		{
			if (i == item)
				return new ItemLocation(slot, null);
		}
		foreach (var grid in AllGrids)
		{
			if (grid.Contains(item))
				return new ItemLocation(null, grid);
		}
		return null;
	}

	/// <summary>Stores into pockets/rig/backpack (merging stacks first); false if nothing fits.</summary>
	public bool TryAutoStore(ItemInstance item)
	{
		foreach (var grid in StorageGrids)
		{
			if (item.Contains(grid))
				continue;
			if (grid.MergeInto(item) == 0)
				return true;
		}
		foreach (var grid in StorageGrids)
		{
			if (item.Contains(grid))
				continue;
			if (grid.TryPlaceAnywhere(item))
				return true;
		}
		return false;
	}

	/// <summary>Places an item that is not stored anywhere yet into a specific grid cell.</summary>
	public bool TryPlaceInGrid(ItemInstance item, InventoryGrid grid, int x, int y)
	{
		if (item.Contains(grid))
			return false;
		RemoveFromCurrentLocation(item, out var from);
		if (grid.TryPlace(item, x, y))
			return true;
		Restore(item, from);
		return false;
	}

	/// <summary>
	/// Equips an item into a slot. A previously equipped item is moved into storage; if there is
	/// no room for it the whole change is undone and false is returned.
	/// </summary>
	public bool TryEquip(ItemInstance item, EquipSlot slot)
	{
		if (!Accepts(slot, item))
			return false;
		if (InSlot(slot) == item)
			return true;
		RemoveFromCurrentLocation(item, out var from);
		var previous = InSlot(slot);
		if (previous is not null)
			_slots.Remove(slot);
		_slots[slot] = item;
		if (previous is not null && !TryAutoStore(previous))
		{
			_slots.Remove(slot);
			_slots[slot] = previous;
			Restore(item, from);
			return false;
		}
		return true;
	}

	/// <summary>
	/// Moves an item from a known source (a slot, or any grid including a loot container) into a
	/// grid cell. Undone if it does not fit. Merges into a same-type stack if one is at the cell.
	/// </summary>
	public bool TryMoveToGrid(ItemInstance item, ItemLocation from, InventoryGrid dest, int x, int y)
	{
		if (item.Contains(dest))
			return false;
		if (dest.ItemAt(x, y) is { } target && target != item && target.Spec.Id == item.Spec.Id
			&& target.Spec.MaxStack > 1 && target.Count < target.Spec.MaxStack)
		{
			var moved = Math.Min(item.Count, target.Spec.MaxStack - target.Count);
			target.Count += moved;
			item.Count -= moved;
			if (item.Count == 0)
				Detach(item, from);
			return true;
		}
		var wasRotated = item.Rotated;
		Detach(item, from);
		if (dest.TryPlace(item, x, y))
			return true;
		item.Rotated = wasRotated;
		Reattach(item, from);
		return false;
	}

	/// <summary>
	/// Takes an item from a known source (a loot container) into pockets, rig or backpack,
	/// merging stacks first. Undone if there is no room.
	/// </summary>
	public bool TryTake(ItemInstance item, ItemLocation from)
	{
		var wasRotated = item.Rotated;
		var count = item.Count;
		Detach(item, from);
		if (TryAutoStore(item))
			return true;
		item.Count = count;
		item.Rotated = wasRotated;
		Reattach(item, from);
		return false;
	}

	/// <summary>Equips an item from a known source; a displaced item goes to storage or the move is undone.</summary>
	public bool TryEquipFrom(ItemInstance item, ItemLocation from, EquipSlot slot)
	{
		if (!Accepts(slot, item))
			return false;
		Detach(item, from);
		var previous = InSlot(slot);
		if (previous is not null)
			_slots.Remove(slot);
		_slots[slot] = item;
		if (previous is not null && !TryAutoStore(previous))
		{
			_slots[slot] = previous;
			Reattach(item, from);
			return false;
		}
		return true;
	}

	private void Detach(ItemInstance item, ItemLocation from)
	{
		if (from.Slot is { } s && _slots.TryGetValue(s, out var inSlot) && inSlot == item)
			_slots.Remove(s);
		else
			from.Grid?.Remove(item);
	}

	private void Reattach(ItemInstance item, ItemLocation from)
	{
		if (from.Slot is { } s)
			_slots[s] = item;
		else
			from.Grid?.TryPlaceAnywhere(item);
	}

	/// <summary>Takes an item out of its slot and stores it; false (unchanged) if there is no room.</summary>
	public bool TryUnequip(EquipSlot slot)
	{
		if (InSlot(slot) is not { } item)
			return false;
		_slots.Remove(slot);
		if (TryAutoStore(item))
			return true;
		_slots[slot] = item;
		return false;
	}

	/// <summary>Removes an item from wherever it is (slot or grid) without storing it elsewhere.</summary>
	public bool Remove(ItemInstance item) => RemoveFromCurrentLocation(item, out _);

	private bool RemoveFromCurrentLocation(ItemInstance item, out ItemLocation? from)
	{
		from = Locate(item);
		if (from is null)
			return false;
		if (from.Value.Slot is { } s)
			_slots.Remove(s);
		else
			from.Value.Grid!.Remove(item);
		return true;
	}

	private void Restore(ItemInstance item, ItemLocation? from)
	{
		if (from is null)
			return;
		if (from.Value.Slot is { } s)
			_slots[s] = item;
		else
			from.Value.Grid!.TryPlaceAnywhere(item);
	}

	/// <summary>
	/// Death or MIA (GDD s15): removes and returns every worn and carried item except the secure
	/// container and its contents, which stay with the character.
	/// </summary>
	public List<ItemInstance> TakeEverythingExceptSecure()
	{
		var lost = new List<ItemInstance>();
		foreach (var slot in _slots.Keys.Where(k => k != EquipSlot.SecureContainer).ToList())
		{
			lost.Add(_slots[slot]);
			_slots.Remove(slot);
		}
		foreach (var pocket in Pockets)
		{
			foreach (var item in pocket.Items.ToList())
			{
				lost.Add(item);
				pocket.Remove(item);
			}
		}
		return lost;
	}

	/// <summary>Everything worn or carried, flattened (for saving and loot transfer).</summary>
	public IEnumerable<ItemInstance> AllItems()
	{
		foreach (var i in _slots.Values)
		{
			yield return i;
			foreach (var nested in Flatten(i))
				yield return nested;
		}
		foreach (var p in Pockets)
		{
			foreach (var i in p.Items)
			{
				yield return i;
				foreach (var nested in Flatten(i))
					yield return nested;
			}
		}
	}

	private static IEnumerable<ItemInstance> Flatten(ItemInstance item)
	{
		foreach (var grid in item.Grids)
		{
			foreach (var i in grid.Items)
			{
				yield return i;
				foreach (var n in Flatten(i))
					yield return n;
			}
		}
	}
}
