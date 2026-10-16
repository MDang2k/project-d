using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class InsuranceTests
{
	private static readonly ItemSpec Money = new("banknotes", "banknotes", ItemCategory.Misc, 1, 1, 0f, 5000, BaseValue: 1, Insurable: false);
	private static readonly ItemSpec Rifle = new("ak", "ak", ItemCategory.Weapon, 5, 2, 3.6f, WeaponClass: WeaponClass.Primary, BaseValue: 600);
	private static readonly ItemSpec Mag = new("mag", "mag", ItemCategory.Magazine, 1, 2, 0.2f, MagazineCapacity: 30, BaseValue: 50);
	private static readonly ItemSpec Vest = new("vest", "vest", ItemCategory.Armor, 3, 3, 4f, BaseValue: 1000, MaxDurability: 60f);
	private static readonly ItemSpec Pack = new("pack", "pack", ItemCategory.Backpack, 4, 4, 1f,
		Grids: new[] { new GridSize(5, 4) }, BaseValue: 200);
	private static readonly ItemSpec Secure = new("secure", "secure", ItemCategory.SecureContainer, 2, 2, 0.5f,
		Grids: new[] { new GridSize(2, 2) }, Insurable: false);
	private static readonly ItemSpec Bandage = new("bandage", "bandage", ItemCategory.Medical, 1, 1, 0.1f, 5, BaseValue: 40, Insurable: false);

	private static readonly InsuranceTerms Terms = new(0.1f, 0.5f);
	private static readonly TraderSpec Qm = new("qm", "qm", TraderUnlock.ForSide, PlayerSide.Allied, new[]
	{
		new TraderLevelSpec(new List<ListingSpec>(), new HashSet<ItemCategory>(), 1f, 0.5f, new List<BarterSpec>(), Terms),
	});
	private static readonly TraderSpec Medic = new("medic", "medic", TraderUnlock.Always, null, new[]
	{
		new TraderLevelSpec(new List<ListingSpec>(), new HashSet<ItemCategory>(), 1f, 0.5f, new List<BarterSpec>()),
	});

	private static GameProfile Profile(int money = 1000)
	{
		var p = new GameProfile { Side = PlayerSide.Allied };
		if (money > 0)
			p.Stash.TryPlaceAnywhere(ItemInstance.Create(Money, money));
		return p;
	}

	private static TraderLevelSpec? LevelOf(string id) => id == "qm" ? Qm.Levels[0] : null;

	[Fact]
	public void InsuringChargesTheCeilingPremiumFlagsTheItemAndCountsAsSpent()
	{
		var profile = Profile();
		var character = new CharacterInventory();
		var rifle = ItemInstance.Create(Rifle);
		character.TryEquip(rifle, EquipSlot.Primary);
		var result = InsuranceService.Insure(profile, character, Qm, rifle);
		Assert.True(result.Ok);
		Assert.Equal(60, result.Premium);
		Assert.True(rifle.Insured);
		Assert.Equal("qm", rifle.InsuredWith);
		Assert.Equal(940, MoneyLedger.Count(new[] { profile.Stash }));
		Assert.Equal(60, profile.Trader("qm").Spent);
	}

	[Fact]
	public void FailureReasonsAreReported()
	{
		var profile = Profile(10);
		var character = new CharacterInventory();
		var vest = ItemInstance.Create(Vest);
		character.TryEquip(vest, EquipSlot.Vest);
		Assert.Equal(InsureFailure.NotEnoughMoney, InsuranceService.Insure(profile, character, Qm, vest).Failure);
		Assert.Equal(InsureFailure.TraderLocked, InsuranceService.Insure(profile, character, Medic, vest).Failure);
		Assert.Equal(InsureFailure.TraderLocked, InsuranceService.Insure(new GameProfile { Side = PlayerSide.Soviet }, character, Qm, vest).Failure);
		var bandage = ItemInstance.Create(Bandage);
		character.TryAutoStore(bandage);
		Assert.Equal(InsureFailure.NotInsurable, InsuranceService.Insure(Profile(), character, Qm, bandage).Failure);
		Assert.Equal(InsureFailure.NotCarried, InsuranceService.Insure(Profile(), character, Qm, ItemInstance.Create(Rifle)).Failure);
		var rich = Profile();
		Assert.True(InsuranceService.Insure(rich, character, Qm, vest).Ok);
		Assert.Equal(InsureFailure.AlreadyInsured, InsuranceService.Insure(rich, character, Qm, vest).Failure);
	}

	[Fact]
	public void StashAndSecureContainerItemsAreNotListed()
	{
		var character = new CharacterInventory();
		character.TryEquip(ItemInstance.Create(Secure), EquipSlot.SecureContainer);
		character.SecureGrids.First().TryPlaceAnywhere(ItemInstance.Create(Vest));
		var carried = ItemInstance.Create(Rifle);
		character.TryEquip(carried, EquipSlot.Primary);
		var ids = InsuranceService.InsurableItems(character).Select(i => i.Spec.Id).ToList();
		Assert.Equal(new[] { "ak" }, ids);
	}

	[Fact]
	public void InsertedMagazineIsItsOwnInsurableItem()
	{
		var character = new CharacterInventory();
		var rifle = ItemInstance.Create(Rifle);
		rifle.InsertedMagazine = ItemInstance.Create(Mag);
		character.TryEquip(rifle, EquipSlot.Primary);
		Assert.Contains(InsuranceService.InsurableItems(character), i => i.Spec.Id == "mag");
	}

	[Fact]
	public void RollReturnsAtChanceZeroPointFiveAndCopyIsStripped()
	{
		var profile = Profile();
		var rifle = ItemInstance.Create(Rifle);
		rifle.Insured = true;
		rifle.InsuredWith = "qm";
		var mag = ItemInstance.Create(Mag);
		mag.Rounds.AddRange(new[] { "a", "b" });
		rifle.InsertedMagazine = mag;
		rifle.Chambered = "a";
		rifle.Durability = 12f;
		var result = InsuranceService.OnRaidEnd(RaidOutcome.Killed, new[] { rifle }, profile, null, LevelOf, () => 0.0);
		Assert.Single(result.Returned);
		Assert.Empty(result.NotReturned);
		var copy = result.Returned[0];
		Assert.Null(copy.InsertedMagazine);
		Assert.Null(copy.Chambered);
		Assert.Empty(copy.Rounds);
		Assert.False(copy.Insured);
		Assert.Equal(12f, copy.Durability);
		Assert.Equal(1, profile.InsuranceReturns.Count);
		Assert.Equal(1, profile.InsuranceReturns.Entries[0].RaidsRemaining);
	}

	[Fact]
	public void RollAtMaxRandomNeverReturns()
	{
		var rifle = ItemInstance.Create(Rifle);
		rifle.Insured = true;
		rifle.InsuredWith = "qm";
		var result = InsuranceService.OnRaidEnd(RaidOutcome.Killed, new[] { rifle }, Profile(), null, LevelOf, () => 0.999);
		Assert.Empty(result.Returned);
		Assert.Single(result.NotReturned);
	}

	[Fact]
	public void InsuredItemNestedInALostPackStillRolls()
	{
		var pack = ItemInstance.Create(Pack);
		var vest = ItemInstance.Create(Vest);
		vest.Insured = true;
		vest.InsuredWith = "qm";
		pack.Grids[0].TryPlaceAnywhere(vest);
		var profile = Profile();
		var result = InsuranceService.OnRaidEnd(RaidOutcome.Mia, new[] { pack }, profile, null, LevelOf, () => 0.0);
		Assert.Single(result.Returned);
		Assert.Equal("vest", result.Returned[0].Spec.Id);
	}

	[Fact]
	public void FlagsAreClearedAfterExtractionAndNothingIsRolled()
	{
		var profile = Profile();
		var character = new CharacterInventory();
		var rifle = ItemInstance.Create(Rifle);
		character.TryEquip(rifle, EquipSlot.Primary);
		InsuranceService.Insure(profile, character, Qm, rifle);
		var stashed = ItemInstance.Create(Vest);
		stashed.Insured = true;
		profile.Stash.TryPlaceAnywhere(stashed);
		var result = InsuranceService.OnRaidEnd(RaidOutcome.Extracted, new List<ItemInstance>(), profile, character, LevelOf, () => 0.0);
		Assert.Empty(result.Returned);
		Assert.False(rifle.Insured);
		Assert.False(stashed.Insured);
		Assert.Null(rifle.InsuredWith);
	}

	[Fact]
	public void QueueAgesEachRaidAndDeliversTheRaidAfterTheLoss()
	{
		var profile = Profile();
		var rifle = ItemInstance.Create(Rifle);
		rifle.Insured = true;
		rifle.InsuredWith = "qm";
		InsuranceService.OnRaidEnd(RaidOutcome.Killed, new[] { rifle }, profile, null, LevelOf, () => 0.0); // raid N: queued
		Assert.Empty(InsuranceService.TryDeliver(profile)); // still waiting after raid N
		InsuranceService.OnRaidEnd(RaidOutcome.Extracted, new List<ItemInstance>(), profile, null, LevelOf, () => 0.0); // raid N+1
		var delivered = InsuranceService.TryDeliver(profile);
		Assert.Single(delivered);
		Assert.Equal(0, profile.InsuranceReturns.Count);
		Assert.Contains(profile.Stash.Items, i => i.Spec.Id == "ak");
	}

	[Fact]
	public void FullStashKeepsTheEntryQueued()
	{
		var profile = new GameProfile { Side = PlayerSide.Allied };
		var filler = new ItemSpec("filler", "filler", ItemCategory.Misc, GameProfile.StashWidth, 1, 0.1f);
		for (var i = 0; i < GameProfile.StashHeight; i++)
			profile.Stash.TryPlaceAnywhere(ItemInstance.Create(filler));
		profile.InsuranceReturns.Add(new PendingReturn(ItemInstance.Create(Rifle), 0));
		Assert.Empty(InsuranceService.TryDeliver(profile));
		Assert.Equal(1, profile.InsuranceReturns.Count);
		foreach (var row in profile.Stash.Items.Take(2).ToList())
			profile.Stash.Remove(row); // a 5x2 rifle needs two free rows
		Assert.Single(InsuranceService.TryDeliver(profile));
	}

	[Fact]
	public void CrashRecoveryPathRollsInsuranceToo()
	{
		var run = new RunProgress();
		var character = new CharacterInventory();
		var rifle = ItemInstance.Create(Rifle);
		rifle.Insured = true;
		rifle.InsuredWith = "qm";
		character.TryEquip(rifle, EquipSlot.Primary);
		run.Restore(character, 0, 0, 0, 0);
		var profile = Profile();
		InsuranceRaidResult? result = null;
		var applied = RaidRecovery.Apply(true, run, lost =>
			result = InsuranceService.OnRaidEnd(RaidOutcome.Killed, lost, profile, run.Inventory, LevelOf, () => 0.0));
		Assert.True(applied);
		Assert.Single(result!.Returned);
		Assert.Equal(1, profile.InsuranceReturns.Count);
	}
}

public class SaveV4Tests
{
	private static ItemSpec? Lookup(string id) => id switch
	{
		"ak" => new ItemSpec("ak", "ak", ItemCategory.Weapon, 5, 2, 3.6f, WeaponClass: WeaponClass.Primary, BaseValue: 600),
		_ => null,
	};

	[Fact]
	public void InsuredFlagAndReturnQueueRoundTrip()
	{
		var profile = new GameProfile();
		var inv = new CharacterInventory();
		var rifle = ItemInstance.Create(Lookup("ak")!);
		rifle.Insured = true;
		rifle.InsuredWith = "allied_qm";
		inv.TryEquip(rifle, EquipSlot.Primary);
		profile.InsuranceReturns.Add(new PendingReturn(ItemInstance.Create(Lookup("ak")!), 1));
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, inv, false));

		var loaded = new GameProfile();
		var run = new RunProgress();
		SaveSerializer.Apply(SaveSerializer.Parse(json), run, loaded, Lookup, new List<string>());
		var loadedRifle = run.Inventory!.InSlot(EquipSlot.Primary)!;
		Assert.True(loadedRifle.Insured);
		Assert.Equal("allied_qm", loadedRifle.InsuredWith);
		Assert.Equal(1, loaded.InsuranceReturns.Count);
		Assert.Equal(1, loaded.InsuranceReturns.Entries[0].RaidsRemaining);
	}

	[Fact]
	public void V3SaveMigratesWithAnEmptyQueue()
	{
		const string v3 = """{"version":3,"raidInProgress":false,"counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[],"traders":{}}""";
		var file = SaveSerializer.Parse(v3);
		Assert.Equal(SaveFile.CurrentVersion, file.Version);
		Assert.Empty(file.InsuranceReturns!);
	}
}
