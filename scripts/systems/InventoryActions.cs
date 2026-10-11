using System.Linq;

namespace ProjectD;

public enum QuickActionKind { None, Equip, Unequip, Use }

public readonly record struct QuickAction(QuickActionKind Kind, EquipSlot? Slot = null);

/// <summary>What double-clicking an item does (GDD s10 quick actions). Pure logic.</summary>
public static class InventoryActions
{
	private static readonly EquipSlot[] SlotOrder =
	{
		EquipSlot.Primary, EquipSlot.Secondary, EquipSlot.Pistol, EquipSlot.Melee, EquipSlot.Vest,
		EquipSlot.Head, EquipSlot.Rig, EquipSlot.Backpack, EquipSlot.SecureContainer,
	};

	public static QuickAction Quick(CharacterInventory inv, ItemInstance item, ItemLocation? location)
	{
		if (location is { Slot: not null })
			return new QuickAction(QuickActionKind.Unequip, location.Value.Slot);

		switch (item.Spec.Category)
		{
			case ItemCategory.Medical:
			case ItemCategory.Food:
			case ItemCategory.Drink:
				return new QuickAction(QuickActionKind.Use);
		}

		var accepting = SlotOrder.Where(s => CharacterInventory.SlotAccepts(s, item.Spec)).ToList();
		if (accepting.Count == 0)
			return new QuickAction(QuickActionKind.None);
		// Prefer an empty slot, otherwise swap into the first accepting one.
		var slot = accepting.FirstOrDefault(s => inv.InSlot(s) is null, accepting[0]);
		return new QuickAction(QuickActionKind.Equip, slot);
	}
}
