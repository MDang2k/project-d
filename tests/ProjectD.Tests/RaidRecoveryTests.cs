using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class RaidRecoveryTests
{
	private static ItemInstance Item(string id, ItemCategory cat, int w = 1, int h = 1, GridSize[]? grids = null)
		=> ItemInstance.Create(new ItemSpec(id, "n", cat, w, h, 1f, Grids: grids));

	[Fact]
	public void SaveWrittenMidRaidCountsAsDeathAndKeepsOnlyTheSecureContainer()
	{
		var inv = new CharacterInventory();
		var secure = Item("secure", ItemCategory.SecureContainer, 2, 2, new[] { new GridSize(2, 2) });
		inv.TryEquip(secure, EquipSlot.SecureContainer);
		secure.Grids[0].TryPlaceAnywhere(Item("note", ItemCategory.Misc));
		inv.TryEquip(Item("vest", ItemCategory.Armor, 3, 3), EquipSlot.Vest);
		inv.TryAutoStore(Item("bandage", ItemCategory.Medical));
		var run = new RunProgress();
		run.Restore(inv, false, 3, 2, 0, 1);

		Assert.True(RaidRecovery.Apply(true, run));

		Assert.Equal(4, run.Raids);
		Assert.Equal(1, run.Deaths);
		Assert.True(run.NeedsKit);
		Assert.Equal(new[] { "secure", "note" }, run.Inventory!.AllItems().Select(i => i.Spec.Id).ToArray());
	}

	[Fact]
	public void CleanSaveIsLeftAlone()
	{
		var run = new RunProgress();
		run.Restore(new CharacterInventory(), false, 3, 2, 0, 1);
		Assert.False(RaidRecovery.Apply(false, run));
		Assert.Equal(3, run.Raids);
		Assert.False(run.NeedsKit);
	}

	[Fact]
	public void RaidInProgressWithNoSavedGearStillCountsTheDeath()
	{
		var run = new RunProgress();
		Assert.True(RaidRecovery.Apply(true, run));
		Assert.NotNull(run.Inventory);
		Assert.True(run.NeedsKit);
		Assert.Equal(1, run.Deaths);
	}
}
