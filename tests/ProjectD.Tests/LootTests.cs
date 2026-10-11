using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class LootRollerTests
{
	private static readonly LootEntry[] Entries =
	{
		new("bandage", 3f, 1, 2),
		new("ammo", 1f, 10, 30),
	};

	[Fact]
	public void WeightsDecideThePick()
	{
		var low = LootRoller.Roll(Entries, 1, () => 0.1);
		Assert.Equal("bandage", low[0].ItemId);
		var high = LootRoller.Roll(Entries, 1, () => 0.9);
		Assert.Equal("ammo", high[0].ItemId);
	}

	[Fact]
	public void CountStaysWithinRange()
	{
		var rng = new System.Random(5);
		var rolled = LootRoller.Roll(Entries, 50, rng.NextDouble);
		Assert.Equal(50, rolled.Count);
		foreach (var (id, count) in rolled)
		{
			if (id == "bandage")
				Assert.InRange(count, 1, 2);
			else
				Assert.InRange(count, 10, 30);
		}
	}

	[Fact]
	public void EmptyTableRollsNothing()
		=> Assert.Empty(LootRoller.Roll(new List<LootEntry>(), 5, () => 0.5));

	[Fact]
	public void ZeroWeightEntryIsNeverPicked()
	{
		var entries = new[] { new LootEntry("never", 0f, 1, 1), new LootEntry("always", 1f, 1, 1) };
		var rng = new System.Random(2);
		Assert.All(LootRoller.Roll(entries, 30, rng.NextDouble), r => Assert.Equal("always", r.ItemId));
	}
}

public class LootContainerTests
{
	private static ItemInstance Thing(string id) => ItemInstance.Create(Items.Make(id, 1, 1));

	[Fact]
	public void ItemsStartHiddenAndRevealOneByOne()
	{
		var c = new LootContainerModel(4, 4, secondsPerItem: 1f, noiseRadius: 8f);
		var a = Thing("a");
		var b = Thing("b");
		c.AddHidden(a);
		c.AddHidden(b);
		Assert.False(c.IsRevealed(a));
		c.StartSearch();
		Assert.False(c.Update(0.5f));
		Assert.True(c.Update(0.6f));
		Assert.True(c.IsRevealed(a));
		Assert.False(c.IsRevealed(b));
		Assert.True(c.Update(1.0f));
		Assert.True(c.FullyRevealed);
		Assert.False(c.IsSearching);
	}

	[Fact]
	public void NoRevealWithoutSearching()
	{
		var c = new LootContainerModel(2, 2, 0.1f, 5f);
		c.AddHidden(Thing("a"));
		Assert.False(c.Update(5f));
		Assert.Equal(1, c.HiddenCount);
	}

	[Fact]
	public void StoppingKeepsProgress()
	{
		var c = new LootContainerModel(2, 2, 1f, 5f);
		c.AddHidden(Thing("a"));
		c.StartSearch();
		c.Update(0.6f);
		c.StopSearch();
		Assert.False(c.Update(5f));
		c.StartSearch();
		Assert.True(c.Update(0.5f));
	}

	[Fact]
	public void SearchSpeedMultiplierShortensWait()
	{
		var c = new LootContainerModel(2, 2, 2f, 5f);
		c.AddHidden(Thing("a"));
		c.StartSearch();
		Assert.True(c.Update(1.1f, speedMultiplier: 2f));
	}

	[Fact]
	public void RevealedAddsAreImmediatelyVisible()
	{
		var c = new LootContainerModel(2, 2, 1f, 5f);
		var item = Thing("worn");
		Assert.True(c.AddRevealed(item));
		Assert.True(c.IsRevealed(item));
		Assert.True(c.FullyRevealed);
	}

	[Fact]
	public void FullContainerRejectsAdds()
	{
		var c = new LootContainerModel(1, 1, 1f, 5f);
		Assert.True(c.AddHidden(Thing("a")));
		Assert.False(c.AddHidden(Thing("b")));
	}

	[Fact]
	public void RevealRaisesEvent()
	{
		var c = new LootContainerModel(2, 2, 0.5f, 5f);
		var item = Thing("a");
		c.AddHidden(item);
		ItemInstance? seen = null;
		c.ItemRevealed += i => seen = i;
		c.StartSearch();
		c.Update(0.6f);
		Assert.Same(item, seen);
	}
}

public class TakeFromContainerTests
{
	[Fact]
	public void TakeStoresInPocketsAndRemovesFromSource()
	{
		var inv = new CharacterInventory();
		var crate = new InventoryGrid(4, 4);
		var item = ItemInstance.Create(Items.Make("tool", 1, 2));
		crate.TryPlace(item, 0, 0);
		Assert.True(inv.TryTake(item, new ItemLocation(null, crate)));
		Assert.False(crate.Contains(item));
		Assert.NotNull(inv.Locate(item)?.Grid);
	}

	[Fact]
	public void TakeFailsWhenFullAndKeepsItemInSource()
	{
		var inv = new CharacterInventory();
		var crate = new InventoryGrid(4, 4);
		var big = ItemInstance.Create(Items.Make("big", 3, 3));
		crate.TryPlace(big, 0, 0);
		Assert.False(inv.TryTake(big, new ItemLocation(null, crate)));
		Assert.True(crate.Contains(big));
	}

	[Fact]
	public void TakeMergesIntoExistingStack()
	{
		var inv = new CharacterInventory();
		inv.TryEquip(ItemInstance.Create(Items.Pack), EquipSlot.Backpack);
		var mine = ItemInstance.Create(Items.Ammo, 40);
		inv.TryAutoStore(mine);
		var crate = new InventoryGrid(4, 4);
		var found = ItemInstance.Create(Items.Ammo, 15);
		crate.TryPlace(found, 0, 0);
		Assert.True(inv.TryTake(found, new ItemLocation(null, crate)));
		Assert.Equal(55, mine.Count);
	}
}

public class NoiseBusTests
{
	[Fact]
	public void SubscribersReceiveEvents()
	{
		NoiseEvent? got = null;
		void Handler(NoiseEvent e) => got = e;
		NoiseBus.Noise += Handler;
		NoiseBus.Emit(new Vector3(1f, 0f, 2f), 12f, "search");
		NoiseBus.Noise -= Handler;
		Assert.Equal(12f, got!.Value.Radius);
		Assert.Equal("search", got.Value.Kind);
	}
}

public class DeathLossTests
{
	[Fact]
	public void SecureContainerContentsSurviveDeath()
	{
		var inv = new CharacterInventory();
		inv.TryEquip(ItemInstance.Create(Items.Pack), EquipSlot.Backpack);
		inv.TryEquip(ItemInstance.Create(Items.Secure), EquipSlot.SecureContainer);
		inv.TryEquip(ItemInstance.Create(Items.Rifle), EquipSlot.Primary);
		var keep = ItemInstance.Create(Items.Bandage);
		inv.SecureGrids.First().TryPlace(keep, 0, 0);
		inv.TryAutoStore(ItemInstance.Create(Items.Ammo, 30));

		var lost = inv.TakeEverythingExceptSecure();

		Assert.Contains(lost, i => i.Spec.Id == "ak");
		Assert.Contains(lost, i => i.Spec.Id == "pack");
		Assert.Contains(lost, i => i.Spec.Id == "ammo");
		Assert.DoesNotContain(lost, i => i.Spec.Id == "secure");
		Assert.DoesNotContain(lost, i => i == keep);
		Assert.NotNull(inv.InSlot(EquipSlot.SecureContainer));
		Assert.True(inv.SecureGrids.First().Contains(keep));
		Assert.Null(inv.InSlot(EquipSlot.Primary));
		Assert.Empty(inv.StorageGrids.SelectMany(g => g.Items));
	}
}
