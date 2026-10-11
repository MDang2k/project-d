using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public static class Items
{
	public static ItemSpec Make(string id, int w, int h, float kg = 0.5f, ItemCategory cat = ItemCategory.Misc,
		int stack = 1, WeaponClass wc = WeaponClass.None, GridSize[]? grids = null)
		=> new(id, id, cat, w, h, kg, stack, wc, grids);

	public static readonly ItemSpec Rifle = Make("ak", 5, 2, 3.6f, ItemCategory.Weapon, wc: WeaponClass.Primary);
	public static readonly ItemSpec Pistol = Make("pistol", 2, 1, 0.8f, ItemCategory.Weapon, wc: WeaponClass.Pistol);
	public static readonly ItemSpec Vest = Make("vest", 3, 3, 4.5f, ItemCategory.Armor);
	public static readonly ItemSpec Helmet = Make("helmet", 2, 2, 1.4f, ItemCategory.Helmet);
	public static readonly ItemSpec Rig = Make("rig", 3, 2, 1f, ItemCategory.Rig, grids: new[] { new GridSize(4, 2) });
	public static readonly ItemSpec Pack = Make("pack", 4, 4, 1.5f, ItemCategory.Backpack, grids: new[] { new GridSize(5, 4) });
	public static readonly ItemSpec Secure = Make("secure", 2, 2, 0.5f, ItemCategory.SecureContainer, grids: new[] { new GridSize(2, 2) });
	public static readonly ItemSpec Knife = Make("knife", 1, 2, 0.3f, ItemCategory.Melee);
	public static readonly ItemSpec Bandage = Make("bandage", 1, 1, 0.1f, ItemCategory.Medical, stack: 3);
	public static readonly ItemSpec Ammo = Make("ammo", 1, 1, 0.01f, ItemCategory.Ammo, stack: 60);
}

public class InventoryGridTests
{
	[Fact]
	public void PlacesWithinBoundsAndRejectsOverlap()
	{
		var g = new InventoryGrid(4, 3);
		var a = ItemInstance.Create(Items.Make("a", 2, 2));
		var b = ItemInstance.Create(Items.Make("b", 2, 2));
		Assert.True(g.TryPlace(a, 0, 0));
		Assert.False(g.TryPlace(b, 1, 1));
		Assert.True(g.TryPlace(b, 2, 0));
		Assert.False(g.TryPlace(ItemInstance.Create(Items.Make("c", 2, 2)), 3, 0));
	}

	[Fact]
	public void RotationSwapsFootprint()
	{
		var g = new InventoryGrid(2, 5);
		var longItem = ItemInstance.Create(Items.Make("long", 5, 1));
		Assert.False(g.CanPlace(longItem, 0, 0));
		longItem.Rotated = true;
		Assert.True(g.TryPlace(longItem, 0, 0));
		Assert.Equal(1, longItem.Width);
		Assert.Equal(5, longItem.Height);
	}

	[Fact]
	public void PlaceAnywhereFindsSpaceAndRotates()
	{
		var g = new InventoryGrid(3, 1);
		g.TryPlace(ItemInstance.Create(Items.Make("a", 1, 1)), 0, 0);
		var item = ItemInstance.Create(Items.Make("wide", 2, 1));
		Assert.True(g.TryPlaceAnywhere(item));
		Assert.Equal((1, 0), g.PositionOf(item));

		var tall = new InventoryGrid(1, 3);
		var rot = ItemInstance.Create(Items.Make("rot", 3, 1));
		Assert.True(tall.TryPlaceAnywhere(rot));
		Assert.True(rot.Rotated);
	}

	[Fact]
	public void NoSpaceReturnsFalseAndKeepsRotation()
	{
		var g = new InventoryGrid(1, 1);
		var item = ItemInstance.Create(Items.Make("big", 2, 2));
		Assert.False(g.TryPlaceAnywhere(item));
		Assert.False(item.Rotated);
	}

	[Fact]
	public void MovingWithinGridIgnoresSelfOverlap()
	{
		var g = new InventoryGrid(3, 1);
		var a = ItemInstance.Create(Items.Make("a", 2, 1));
		g.TryPlace(a, 0, 0);
		Assert.True(g.TryPlace(a, 1, 0));
		Assert.Equal(1, g.ItemCount);
	}

	[Fact]
	public void ItemAtReportsCoveredCells()
	{
		var g = new InventoryGrid(4, 4);
		var a = ItemInstance.Create(Items.Make("a", 2, 2));
		g.TryPlace(a, 1, 1);
		Assert.Same(a, g.ItemAt(2, 2));
		Assert.Null(g.ItemAt(0, 0));
		Assert.Null(g.ItemAt(3, 3));
	}

	[Fact]
	public void StacksMergeUpToMax()
	{
		var g = new InventoryGrid(4, 4);
		var first = ItemInstance.Create(Items.Ammo, 50);
		g.TryPlace(first, 0, 0);
		var more = ItemInstance.Create(Items.Ammo, 30);
		var left = g.MergeInto(more);
		Assert.Equal(60, first.Count);
		Assert.Equal(20, left);
		Assert.Equal(20, more.Count);
	}

	[Fact]
	public void NonStackableDoesNotMerge()
	{
		var g = new InventoryGrid(4, 4);
		g.TryPlace(ItemInstance.Create(Items.Knife), 0, 0);
		Assert.Equal(1, g.MergeInto(ItemInstance.Create(Items.Knife)));
	}
}

public class CharacterInventoryTests
{
	[Fact]
	public void SlotRulesAcceptOnlyMatchingCategories()
	{
		Assert.True(CharacterInventory.SlotAccepts(EquipSlot.Primary, Items.Rifle));
		Assert.True(CharacterInventory.SlotAccepts(EquipSlot.Secondary, Items.Rifle));
		Assert.False(CharacterInventory.SlotAccepts(EquipSlot.Pistol, Items.Rifle));
		Assert.True(CharacterInventory.SlotAccepts(EquipSlot.Pistol, Items.Pistol));
		Assert.False(CharacterInventory.SlotAccepts(EquipSlot.Primary, Items.Pistol));
		Assert.True(CharacterInventory.SlotAccepts(EquipSlot.Head, Items.Helmet));
		Assert.False(CharacterInventory.SlotAccepts(EquipSlot.Vest, Items.Helmet));
		Assert.True(CharacterInventory.SlotAccepts(EquipSlot.Melee, Items.Knife));
		Assert.True(CharacterInventory.SlotAccepts(EquipSlot.SecureContainer, Items.Secure));
	}

	[Fact]
	public void HasTwoOneByTwoPocketsByDefault()
	{
		var inv = new CharacterInventory();
		Assert.Equal(2, inv.Pockets.Count);
		Assert.All(inv.Pockets, p => Assert.Equal((1, 2), (p.Width, p.Height)));
	}

	[Fact]
	public void WearingARigAddsItsGrid()
	{
		var inv = new CharacterInventory();
		Assert.Equal(2, inv.StorageGrids.Count());
		Assert.True(inv.TryEquip(ItemInstance.Create(Items.Rig), EquipSlot.Rig));
		Assert.Equal(3, inv.StorageGrids.Count());
	}

	[Fact]
	public void WrongSlotIsRejected()
	{
		var inv = new CharacterInventory();
		Assert.False(inv.TryEquip(ItemInstance.Create(Items.Helmet), EquipSlot.Vest));
	}

	[Fact]
	public void EquippingMovesItemOutOfGrid()
	{
		var inv = new CharacterInventory();
		var pack = ItemInstance.Create(Items.Pack);
		inv.TryEquip(pack, EquipSlot.Backpack);
		var vest = ItemInstance.Create(Items.Vest);
		Assert.True(inv.TryAutoStore(vest));
		Assert.NotNull(inv.Locate(vest)?.Grid);
		Assert.True(inv.TryEquip(vest, EquipSlot.Vest));
		Assert.Equal(EquipSlot.Vest, inv.Locate(vest)?.Slot);
		Assert.All(inv.StorageGrids, g => Assert.False(g.Contains(vest)));
	}

	[Fact]
	public void SwapSendsOldItemToStorage()
	{
		var inv = new CharacterInventory();
		inv.TryEquip(ItemInstance.Create(Items.Pack), EquipSlot.Backpack);
		var old = ItemInstance.Create(Items.Helmet);
		var neu = ItemInstance.Create(Items.Helmet);
		inv.TryEquip(old, EquipSlot.Head);
		Assert.True(inv.TryAutoStore(neu));
		Assert.True(inv.TryEquip(neu, EquipSlot.Head));
		Assert.Same(neu, inv.InSlot(EquipSlot.Head));
		Assert.NotNull(inv.Locate(old)?.Grid);
	}

	[Fact]
	public void SwapFailsAndRollsBackWhenNoRoom()
	{
		var inv = new CharacterInventory();
		var old = ItemInstance.Create(Items.Helmet);
		var neu = ItemInstance.Create(Items.Helmet);
		inv.TryEquip(old, EquipSlot.Head);
		// New helmet is only reachable if it sits in a grid; use the secure container as its home.
		var secure = ItemInstance.Create(Items.Secure);
		inv.TryEquip(secure, EquipSlot.SecureContainer);
		var grid = inv.SecureGrids.First();
		Assert.True(grid.TryPlace(neu, 0, 0));
		Assert.False(inv.TryEquip(neu, EquipSlot.Head));
		Assert.Same(old, inv.InSlot(EquipSlot.Head));
		Assert.True(grid.Contains(neu));
	}

	[Fact]
	public void UnequipNeedsRoom()
	{
		var inv = new CharacterInventory();
		var vest = ItemInstance.Create(Items.Vest);
		inv.TryEquip(vest, EquipSlot.Vest);
		Assert.False(inv.TryUnequip(EquipSlot.Vest));
		Assert.Same(vest, inv.InSlot(EquipSlot.Vest));
		inv.TryEquip(ItemInstance.Create(Items.Pack), EquipSlot.Backpack);
		Assert.True(inv.TryUnequip(EquipSlot.Vest));
		Assert.Null(inv.InSlot(EquipSlot.Vest));
	}

	[Fact]
	public void WeightSumsEverythingIncludingNested()
	{
		var inv = new CharacterInventory();
		inv.TryEquip(ItemInstance.Create(Items.Rifle), EquipSlot.Primary);
		var pack = ItemInstance.Create(Items.Pack);
		inv.TryEquip(pack, EquipSlot.Backpack);
		inv.TryAutoStore(ItemInstance.Create(Items.Ammo, 40));
		inv.TryAutoStore(ItemInstance.Create(Items.Bandage, 2));
		Assert.Equal(3.6f + 1.5f + 40 * 0.01f + 2 * 0.1f, inv.TotalWeightKg, 3);
	}

	[Fact]
	public void MagazineWeightIncludesRounds()
	{
		var magSpec = new ItemSpec("mag", "mag", ItemCategory.Magazine, 1, 2, 0.25f, MagazineCapacity: 30,
			RoundWeightKg: 0.016f, Caliber: "7.62x39");
		var mag = ItemInstance.Create(magSpec);
		for (var i = 0; i < 30; i++)
			mag.Rounds.Add("7_62x39");
		Assert.Equal(0.25f + 30 * 0.016f, mag.WeightKg, 3);
	}

	[Fact]
	public void CannotPutAContainerInsideItself()
	{
		var inv = new CharacterInventory();
		var pack = ItemInstance.Create(Items.Pack);
		inv.TryEquip(pack, EquipSlot.Backpack);
		Assert.False(inv.TryPlaceInGrid(pack, pack.Grids[0], 0, 0));
		Assert.Equal(EquipSlot.Backpack, inv.Locate(pack)?.Slot);
	}

	[Fact]
	public void AutoStoreMergesStacksFirst()
	{
		var inv = new CharacterInventory();
		inv.TryEquip(ItemInstance.Create(Items.Pack), EquipSlot.Backpack);
		inv.TryAutoStore(ItemInstance.Create(Items.Ammo, 40));
		var more = ItemInstance.Create(Items.Ammo, 15);
		Assert.True(inv.TryAutoStore(more));
		var stacks = inv.AllItems().Where(i => i.Spec.Id == "ammo").ToList();
		Assert.Single(stacks);
		Assert.Equal(55, stacks[0].Count);
	}

	[Fact]
	public void TryPlaceInGridRestoresOnFailure()
	{
		var inv = new CharacterInventory();
		var vest = ItemInstance.Create(Items.Vest);
		inv.TryEquip(vest, EquipSlot.Vest);
		Assert.False(inv.TryPlaceInGrid(vest, inv.Pockets[0], 0, 0));
		Assert.Equal(EquipSlot.Vest, inv.Locate(vest)?.Slot);
	}
}
