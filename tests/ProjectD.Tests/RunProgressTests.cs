using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class RunProgressTests
{
	[Fact]
	public void ExtractionKeepsGearAndNeedsNoKit()
	{
		var p = new RunProgress();
		var inv = new CharacterInventory();
		p.Record(RaidOutcome.Extracted, inv);
		Assert.Same(inv, p.Inventory);
		Assert.False(p.NeedsKit);
		Assert.Equal(1, p.Raids);
		Assert.Equal(1, p.Extractions);
	}

	[Theory]
	[InlineData(RaidOutcome.Killed)]
	[InlineData(RaidOutcome.Mia)]
	public void DeathAndMiaNeedAFreshKit(RaidOutcome outcome)
	{
		var p = new RunProgress();
		p.Record(outcome, new CharacterInventory());
		Assert.True(p.NeedsKit);
		Assert.Equal(1, outcome == RaidOutcome.Killed ? p.Deaths : p.Mia);
		p.MarkKitGiven();
		Assert.False(p.NeedsKit);
	}

	[Fact]
	public void CountersAccumulateAcrossRaids()
	{
		var p = new RunProgress();
		var inv = new CharacterInventory();
		p.Record(RaidOutcome.Extracted, inv);
		p.Record(RaidOutcome.Killed, inv);
		p.Record(RaidOutcome.Extracted, inv);
		Assert.Equal(3, p.Raids);
		Assert.Equal(2, p.Extractions);
		Assert.Equal(1, p.Deaths);
	}

	[Fact]
	public void NoneIsIgnoredAndResetClears()
	{
		var p = new RunProgress();
		p.Record(RaidOutcome.None, new CharacterInventory());
		Assert.Equal(0, p.Raids);
		p.Record(RaidOutcome.Mia, new CharacterInventory());
		p.Reset();
		Assert.Null(p.Inventory);
		Assert.Equal(0, p.Raids);
	}

	[Fact]
	public void SecureContainerSurvivesIntoTheNextRaid()
	{
		var inv = new CharacterInventory();
		inv.TryEquip(ItemInstance.Create(Items.Secure), EquipSlot.SecureContainer);
		inv.TryEquip(ItemInstance.Create(Items.Rifle), EquipSlot.Primary);
		var keep = ItemInstance.Create(Items.Bandage);
		inv.SecureGrids.First().TryPlace(keep, 0, 0);

		RaidOutcomeRules.Apply(RaidOutcome.Killed, inv);
		var p = new RunProgress();
		p.Record(RaidOutcome.Killed, inv);

		Assert.NotNull(p.Inventory!.InSlot(EquipSlot.SecureContainer));
		Assert.Null(p.Inventory.InSlot(EquipSlot.Primary));
		Assert.True(p.Inventory.SecureGrids.First().Contains(keep));
	}
}
