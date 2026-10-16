using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class TradeTests
{
	private static readonly ItemSpec Money = new("banknotes", "banknotes", ItemCategory.Misc, 1, 1, 0f, 1000, BaseValue: 1);
	private static readonly ItemSpec Rifle = new("ak", "ak", ItemCategory.Weapon, 5, 2, 3.6f, WeaponClass: WeaponClass.Primary, BaseValue: 600);
	private static readonly ItemSpec Mag = new("mag", "mag", ItemCategory.Magazine, 1, 2, 0.2f, MagazineCapacity: 30, BaseValue: 50);
	private static readonly ItemSpec Ammo = new("ammo", "ammo", ItemCategory.Ammo, 1, 1, 0.01f, 60, AmmoId: "round", BaseValue: 2);
	private static readonly ItemSpec Vest = new("vest", "vest", ItemCategory.Armor, 3, 3, 4f, BaseValue: 1000, MaxDurability: 60f);
	private static readonly ItemSpec Pack = new("pack", "pack", ItemCategory.Backpack, 4, 4, 1f,
		Grids: new[] { new GridSize(5, 4) }, BaseValue: 200);
	private static readonly ItemSpec Junk = new("junk", "junk", ItemCategory.Misc, 1, 1, 0.1f, BaseValue: 0);
	private static readonly ItemSpec Big = new("big", "big", ItemCategory.Misc, 10, 30, 1f, BaseValue: 1);

	private static readonly Dictionary<string, ItemSpec> Specs = new[] { Money, Rifle, Mag, Ammo, Vest, Pack, Junk, Big }
		.ToDictionary(s => s.Id);

	private static ItemInstance? Make(string id, int count) => Specs.TryGetValue(id, out var s) ? ItemInstance.Create(s, count) : null;

	private static TraderLevelSpec Level(float buy = 1.2f, float sell = 0.5f, params BarterSpec[] barters) => new(
		new[] { new ListingSpec("ak"), new ListingSpec("ammo", 2f), new ListingSpec("big") },
		new HashSet<ItemCategory> { ItemCategory.Weapon, ItemCategory.Magazine, ItemCategory.Armor, ItemCategory.Backpack, ItemCategory.Ammo },
		buy, sell, barters);

	private static (TradeEngine Engine, GameProfile Profile, CharacterInventory Character) Setup(int money = 0)
	{
		var profile = new GameProfile();
		var character = new CharacterInventory();
		if (money > 0)
			profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Money, money));
		return (new TradeEngine(profile, character, id => Specs.GetValueOrDefault(id), Make, id => id == "round" ? 2 : 0),
			profile, character);
	}

	[Fact]
	public void BuyPriceRoundsUp()
	{
		var (e, _, _) = Setup();
		Assert.Equal(720, e.BuyPrice(Level(), new ListingSpec("ak")));
		Assert.Equal(3, e.BuyPrice(Level(1.2f), new ListingSpec("ammo", 1.1f))); // 2 * 1.2 * 1.1 = 2.64
	}

	[Fact]
	public void BuyPaysAndPutsTheItemInTheStash()
	{
		var (e, profile, _) = Setup(1000);
		var state = new TraderState();
		var result = e.Buy(Level(), state, Level().Sells[0]);
		Assert.True(result.Ok);
		Assert.Equal(720, result.Money);
		Assert.Equal(280, e.Money);
		Assert.Contains(profile.Stash.Items, i => i.Spec.Id == "ak");
		Assert.Equal(720, state.Spent);
	}

	[Fact]
	public void NotEnoughMoneyChangesNothing()
	{
		var (e, profile, _) = Setup(100);
		var result = e.Buy(Level(), new TraderState(), Level().Sells[0]);
		Assert.Equal(TradeFailure.NotEnoughMoney, result.Failure);
		Assert.Equal(100, e.Money);
		Assert.Single(profile.Stash.Items);
	}

	[Fact]
	public void MoneyIsCountedAcrossTheStashAndCarriedGrids()
	{
		var (e, profile, character) = Setup(300);
		character.TryEquip(ItemInstance.Create(Specs["pack"]), EquipSlot.Backpack);
		character.TryAutoStore(ItemInstance.Create(Money, 500));
		Assert.Equal(800, e.Money);
		var result = e.Buy(Level(), new TraderState(), Level().Sells[0]); // 720: stash 300 first, then 420 from the carried stack
		Assert.True(result.Ok);
		Assert.Equal(80, e.Money);
		Assert.DoesNotContain(profile.Stash.Items, i => i.Spec.Id == "banknotes");
	}

	[Fact]
	public void StashFullRefusesTheBuyAndKeepsTheMoney()
	{
		var profile = new GameProfile();
		Assert.True(profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Specs["big"]))); // fills the whole stash
		var carried = new CharacterInventory();
		carried.TryAutoStore(ItemInstance.Create(Money, 1000));
		var engine = new TradeEngine(profile, carried, id => Specs.GetValueOrDefault(id), Make, _ => 0);
		var result = engine.Buy(Level(), new TraderState(), Level().Sells[0]);
		Assert.Equal(TradeFailure.StashFull, result.Failure);
		Assert.Equal(1000, engine.Money);
	}

	[Fact]
	public void SellPaysFloorOfValueTimesFractionAndConditionAndRemovesTheItem()
	{
		var (e, profile, character) = Setup();
		var vest = ItemInstance.Create(Vest);
		vest.Durability = 30f; // half worn
		character.TryEquip(vest, EquipSlot.Vest);
		var result = e.Sell(Level(), new TraderState(), vest);
		Assert.True(result.Ok);
		Assert.Equal(250, result.Money); // 1000 * 0.5 * 0.5
		Assert.Null(character.InSlot(EquipSlot.Vest));
		Assert.Equal(250, e.Money);
	}

	[Fact]
	public void MagazineSellsWithItsRoundsAndWeaponWithItsMagazineAndChamber()
	{
		var (e, profile, _) = Setup();
		var mag = ItemInstance.Create(Mag);
		for (var i = 0; i < 10; i++)
			mag.Rounds.Add("round");
		Assert.Equal(70, e.WorthOf(mag)); // 50 + 10 * 2
		var rifle = ItemInstance.Create(Rifle);
		rifle.InsertedMagazine = mag;
		rifle.Chambered = "round";
		Assert.Equal(600 + 70 + 2, e.WorthOf(rifle));
		profile.Stash.TryPlaceAnywhere(rifle);
		var result = e.Sell(Level(), new TraderState(), rifle);
		Assert.Equal(336, result.Money); // floor(672 * 0.5)
		Assert.DoesNotContain(profile.Stash.Items, i => i.Spec.Id == "ak");
	}

	[Fact]
	public void ContainersWithContentsCannotBeSoldUntilEmptied()
	{
		var (e, profile, _) = Setup();
		var pack = ItemInstance.Create(Specs["pack"]);
		pack.Grids[0].TryPlaceAnywhere(ItemInstance.Create(Ammo, 5));
		profile.Stash.TryPlaceAnywhere(pack);
		Assert.Equal(TradeFailure.HasContents, e.Sell(Level(), new TraderState(), pack).Failure);
		pack.Grids[0].Remove(pack.Grids[0].Items.First());
		Assert.True(e.Sell(Level(), new TraderState(), pack).Ok);
	}

	[Fact]
	public void ZeroValueAndOtherCategoriesAreNotSellable()
	{
		var (e, profile, _) = Setup();
		var junk = ItemInstance.Create(Junk);
		profile.Stash.TryPlaceAnywhere(junk);
		Assert.Equal(TradeFailure.NotSellableHere, e.Sell(Level(), new TraderState(), junk).Failure);
		var note = ItemInstance.Create(Money, 5);
		profile.Stash.TryPlaceAnywhere(note);
		Assert.Equal(TradeFailure.NotSellableHere, e.Sell(Level(), new TraderState(), note).Failure);
	}

	[Fact]
	public void BuyThenSellLosesTheMargin()
	{
		var (e, profile, _) = Setup(1000);
		var level = Level();
		Assert.True(e.Buy(level, new TraderState(), level.Sells[0]).Ok);
		var rifle = profile.Stash.Items.First(i => i.Spec.Id == "ak");
		Assert.True(e.Sell(level, new TraderState(), rifle).Ok);
		Assert.Equal(1000 - 720 + 300, e.Money);
	}

	[Fact]
	public void PayoutIsSplitIntoStacksOfMaxStack()
	{
		Assert.Equal(new[] { 1000, 1000, 500 }, MoneyLedger.SplitStacks(2500, 1000));
		Assert.Empty(MoneyLedger.SplitStacks(0, 1000));
	}

	[Fact]
	public void BarterIsAllOrNothing()
	{
		var offer = new BarterSpec(new[] { ("ak", 1), ("ammo", 50) }, "vest");
		var level = Level(barters: offer);
		var (e, profile, _) = Setup();
		profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Rifle));
		profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Ammo, 20));
		var state = new TraderState();
		Assert.Equal(TradeFailure.MissingBarterItems, e.Barter(level, state, offer).Failure);
		Assert.Contains(profile.Stash.Items, i => i.Spec.Id == "ak");
		profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Ammo, 40));
		var ok = e.Barter(level, state, offer);
		Assert.True(ok.Ok);
		Assert.DoesNotContain(profile.Stash.Items, i => i.Spec.Id == "ak");
		Assert.Contains(profile.Stash.Items, i => i.Spec.Id == "vest");
		Assert.Equal(10, profile.Stash.Items.Where(i => i.Spec.Id == "ammo").Sum(i => i.Count));
		Assert.Equal(1000, state.Spent);
	}

	[Fact]
	public void SellableItemsListsStashAndCarriedButNotWeaponMagazines()
	{
		var (e, profile, character) = Setup();
		var rifle = ItemInstance.Create(Rifle);
		rifle.InsertedMagazine = ItemInstance.Create(Mag);
		character.TryEquip(rifle, EquipSlot.Primary);
		profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Ammo, 10));
		var ids = e.SellableItems(Level()).Select(i => i.Spec.Id).ToList();
		Assert.Contains("ak", ids);
		Assert.Contains("ammo", ids);
		Assert.DoesNotContain("mag", ids);
	}

	[Fact]
	public void NonListedOrUnknownItemsCannotBeBought()
	{
		var (e, _, _) = Setup(10000);
		Assert.Equal(TradeFailure.NotBuyableHere, e.Buy(Level(), new TraderState(), new ListingSpec("vest")).Failure);
	}
}

public class TraderAvailabilityTests
{
	private static TraderSpec Trader(TraderUnlock unlock, PlayerSide? side = null)
		=> new("t", "t", unlock, side, new[] { new TraderLevelSpec(new List<ListingSpec>(), new HashSet<ItemCategory>(), 1f, 0.5f, new List<BarterSpec>()) });

	[Fact]
	public void AlwaysIsAlwaysAvailable()
		=> Assert.True(new GameProfile().IsTraderAvailable(Trader(TraderUnlock.Always)));

	[Fact]
	public void QuartermasterServesOnlyItsSideUntilUnlocked()
	{
		var allied = Trader(TraderUnlock.ForSide, PlayerSide.Allied);
		var profile = new GameProfile { Side = PlayerSide.Soviet };
		Assert.False(profile.IsTraderAvailable(allied));
		Assert.Equal("trader.locked.side", profile.LockReasonKey(allied));
		profile.UnlockTrader("t");
		Assert.True(profile.IsTraderAvailable(allied));
		Assert.True(new GameProfile { Side = PlayerSide.Allied }.IsTraderAvailable(allied));
	}

	[Fact]
	public void LockedNeedsAnUnlock()
	{
		var locked = Trader(TraderUnlock.Locked);
		var profile = new GameProfile();
		Assert.False(profile.IsTraderAvailable(locked));
		profile.UnlockTrader("t");
		Assert.True(profile.IsTraderAvailable(locked));
	}
}
