using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class RaidSessionTests
{
	private static readonly ExtractionSpec Open = new("gate", ExtractKind.AlwaysOpen, 5f);
	private static readonly ExtractionSpec Keyed = new("tunnel", ExtractKind.RequiresItem, 5f, "bunker_key");

	private static RaidSession Make(float duration = 100f)
		=> new(duration, new[] { Open, Keyed });

	private static void Run(RaidSession s, float seconds, string? zone = null, bool hasKey = false)
	{
		for (var t = 0f; t < seconds && s.IsActive; t += 0.1f)
			s.Update(0.1f, zone, id => hasKey && id == "bunker_key");
	}

	[Fact]
	public void TimerCountsDownAndExpiryIsMia()
	{
		var s = Make(10f);
		RaidOutcome? ended = null;
		s.Ended += o => ended = o;
		Run(s, 5f);
		Assert.True(s.IsActive);
		Assert.InRange(s.RemainingS, 4.5f, 5.5f);
		Run(s, 6f);
		Assert.Equal(RaidOutcome.Mia, s.Outcome);
		Assert.Equal(RaidOutcome.Mia, ended);
	}

	[Fact]
	public void StandingInOpenZoneExtracts()
	{
		var s = Make();
		Run(s, 4f, "gate");
		Assert.True(s.IsActive);
		Assert.InRange(s.ExtractProgress, 0.7f, 0.9f);
		Run(s, 2f, "gate");
		Assert.Equal(RaidOutcome.Extracted, s.Outcome);
	}

	[Fact]
	public void LeavingTheZoneResetsProgress()
	{
		var s = Make();
		var aborted = false;
		s.ExtractionAborted += _ => aborted = true;
		Run(s, 3f, "gate");
		Run(s, 0.2f, null);
		Assert.Equal(0f, s.ExtractProgress);
		Assert.True(aborted);
		Run(s, 3f, "gate");
		Assert.True(s.IsActive);
	}

	[Fact]
	public void ConditionalZoneNeedsItsItem()
	{
		var s = Make();
		Run(s, 8f, "tunnel", hasKey: false);
		Assert.True(s.IsActive);
		Assert.Equal(ExtractBlock.MissingItem, s.Blocked);
		Assert.Equal(0f, s.ExtractProgress);
		Run(s, 6f, "tunnel", hasKey: true);
		Assert.Equal(RaidOutcome.Extracted, s.Outcome);
	}

	[Fact]
	public void DeathEndsTheRaidOnce()
	{
		var s = Make();
		var count = 0;
		s.Ended += _ => count++;
		s.PlayerDied();
		s.PlayerDied();
		Run(s, 200f);
		Assert.Equal(RaidOutcome.Killed, s.Outcome);
		Assert.Equal(1, count);
	}

	[Fact]
	public void ExtractedRaidCannotBeKilledLater()
	{
		var s = Make();
		Run(s, 6f, "gate");
		s.PlayerDied();
		Assert.Equal(RaidOutcome.Extracted, s.Outcome);
	}

	[Fact]
	public void UnknownZoneDoesNothing()
	{
		var s = Make();
		Run(s, 10f, "nowhere");
		Assert.True(s.IsActive);
		Assert.Equal(0f, s.ExtractProgress);
	}
}

public class RaidOutcomeRulesTests
{
	private static CharacterInventory Kit(out ItemInstance secured)
	{
		var inv = new CharacterInventory();
		inv.TryEquip(ItemInstance.Create(Items.Secure), EquipSlot.SecureContainer);
		secured = ItemInstance.Create(Items.Ammo, 10);
		inv.SecureGrids.First().TryPlace(secured, 0, 0);
		inv.TryAutoStore(ItemInstance.Create(Items.Ammo, 30));
		return inv;
	}

	[Fact]
	public void ExtractionKeepsEverything()
	{
		var inv = Kit(out _);
		var lost = RaidOutcomeRules.Apply(RaidOutcome.Extracted, inv);
		Assert.Empty(lost);
		Assert.Single(inv.StorageGrids.SelectMany(g => g.Items));
	}

	[Theory]
	[InlineData(RaidOutcome.Killed)]
	[InlineData(RaidOutcome.Mia)]
	public void DeathAndMiaKeepOnlyTheSecureContainer(RaidOutcome outcome)
	{
		var inv = Kit(out var secured);
		var lost = RaidOutcomeRules.Apply(outcome, inv);
		Assert.Single(lost);
		Assert.True(inv.SecureGrids.First().Contains(secured));
		Assert.Empty(inv.StorageGrids.SelectMany(g => g.Items));
	}
}
