using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class InventoryMoveTests
{
	[Fact]
	public void MoveFromExternalGridIntoPocket()
	{
		var inv = new CharacterInventory();
		var crate = new InventoryGrid(4, 4);
		var item = ItemInstance.Create(Items.Make("tool", 1, 2));
		crate.TryPlace(item, 0, 0);
		var from = new ItemLocation(null, crate);
		Assert.True(inv.TryMoveToGrid(item, from, inv.Pockets[0], 0, 0));
		Assert.False(crate.Contains(item));
		Assert.True(inv.Pockets[0].Contains(item));
	}

	[Fact]
	public void FailedMoveRestoresSourceAndRotation()
	{
		var inv = new CharacterInventory();
		var crate = new InventoryGrid(4, 4);
		var item = ItemInstance.Create(Items.Make("big", 2, 2));
		crate.TryPlace(item, 1, 1);
		item.Rotated = true;
		Assert.False(inv.TryMoveToGrid(item, new ItemLocation(null, crate), inv.Pockets[0], 0, 0));
		Assert.True(crate.Contains(item));
		Assert.True(item.Rotated);
	}

	[Fact]
	public void DroppingOnSameStackMerges()
	{
		var inv = new CharacterInventory();
		var pack = ItemInstance.Create(Items.Pack);
		inv.TryEquip(pack, EquipSlot.Backpack);
		var grid = pack.Grids[0];
		var a = ItemInstance.Create(Items.Ammo, 30);
		var b = ItemInstance.Create(Items.Ammo, 20);
		grid.TryPlace(a, 0, 0);
		grid.TryPlace(b, 2, 0);
		Assert.True(inv.TryMoveToGrid(b, new ItemLocation(null, grid), grid, 0, 0));
		Assert.Equal(50, a.Count);
		Assert.False(grid.Contains(b));
	}

	[Fact]
	public void PartialMergeKeepsRemainder()
	{
		var inv = new CharacterInventory();
		var pack = ItemInstance.Create(Items.Pack);
		inv.TryEquip(pack, EquipSlot.Backpack);
		var grid = pack.Grids[0];
		var a = ItemInstance.Create(Items.Ammo, 50);
		var b = ItemInstance.Create(Items.Ammo, 30);
		grid.TryPlace(a, 0, 0);
		grid.TryPlace(b, 2, 0);
		Assert.True(inv.TryMoveToGrid(b, new ItemLocation(null, grid), grid, 0, 0));
		Assert.Equal(60, a.Count);
		Assert.Equal(20, b.Count);
		Assert.True(grid.Contains(b));
	}

	[Fact]
	public void EquipFromLootGridWorksAndRollsBack()
	{
		var inv = new CharacterInventory();
		var crate = new InventoryGrid(4, 4);
		var vest = ItemInstance.Create(Items.Vest);
		crate.TryPlace(vest, 0, 0);
		Assert.True(inv.TryEquipFrom(vest, new ItemLocation(null, crate), EquipSlot.Vest));
		Assert.Same(vest, inv.InSlot(EquipSlot.Vest));
		Assert.False(crate.Contains(vest));

		var better = ItemInstance.Create(Items.Vest);
		crate.TryPlace(better, 0, 0);
		// No storage for the old vest: swap must be undone.
		Assert.False(inv.TryEquipFrom(better, new ItemLocation(null, crate), EquipSlot.Vest));
		Assert.Same(vest, inv.InSlot(EquipSlot.Vest));
		Assert.True(crate.Contains(better));
	}
}

public class QuickActionTests
{
	[Fact]
	public void MedicalAndFoodAreUsed()
	{
		var inv = new CharacterInventory();
		Assert.Equal(QuickActionKind.Use,
			InventoryActions.Quick(inv, ItemInstance.Create(Items.Bandage), null).Kind);
	}

	[Fact]
	public void WeaponGoesToEmptyPrimaryThenSecondary()
	{
		var inv = new CharacterInventory();
		var a = ItemInstance.Create(Items.Rifle);
		var first = InventoryActions.Quick(inv, a, null);
		Assert.Equal(QuickActionKind.Equip, first.Kind);
		Assert.Equal(EquipSlot.Primary, first.Slot);
		inv.TryEquip(a, EquipSlot.Primary);
		var second = InventoryActions.Quick(inv, ItemInstance.Create(Items.Rifle), null);
		Assert.Equal(EquipSlot.Secondary, second.Slot);
	}

	[Fact]
	public void EquippedItemUnequips()
	{
		var inv = new CharacterInventory();
		var v = ItemInstance.Create(Items.Vest);
		inv.TryEquip(v, EquipSlot.Vest);
		var action = InventoryActions.Quick(inv, v, inv.Locate(v));
		Assert.Equal(QuickActionKind.Unequip, action.Kind);
		Assert.Equal(EquipSlot.Vest, action.Slot);
	}

	[Fact]
	public void AmmoHasNoQuickAction()
		=> Assert.Equal(QuickActionKind.None,
			InventoryActions.Quick(new CharacterInventory(), ItemInstance.Create(Items.Ammo), null).Kind);
}
