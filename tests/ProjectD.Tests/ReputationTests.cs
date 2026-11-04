using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ProjectD.Tests;

public class ReputationTests
{
	private static ReputationBook Book(out QuestLog log)
	{
		log = new QuestLog();
		return new ReputationBook(log);
	}

	// ---- the book

	[Fact]
	public void MissingEntriesReadAsZero()
	{
		var b = Book(out _);
		Assert.Equal(0, b.Hundredths("medic"));
		Assert.Equal(0f, b.Value("medic"));
	}

	[Fact]
	public void ChangesAreClampedAtPlusAndMinusOneHundred()
	{
		var b = Book(out var log);
		Assert.Equal(60, b.Add("medic", 60));
		Assert.Equal(40, b.Add("medic", 60)); // only 40 fitted
		Assert.Equal(100, b.Hundredths("medic"));
		b.Add("medic", -500);
		Assert.Equal(-100, b.Hundredths("medic"));
		Assert.Equal(-100, log.Reputation["medic"]);
		log.AddReputation("engineer", 9999);
		log.SetReputation("scientist", -9999);
		Assert.Equal(100, log.Reputation["engineer"]);
		Assert.Equal(-100, log.Reputation["scientist"]);
	}

	[Theory]
	[InlineData(-100, 1.10f, 0.90f)]
	[InlineData(0, 1.00f, 1.00f)]
	[InlineData(50, 0.95f, 1.05f)]
	[InlineData(100, 0.90f, 1.10f)]
	public void PriceFactorsSwingTenPercentAcrossTheRange(int rep, float buy, float sell)
	{
		var b = Book(out var log);
		log.SetReputation("medic", rep);
		Assert.Equal(buy, b.BuyFactor("medic"), 3);
		Assert.Equal(sell, b.SellFactor("medic"), 3);
	}

	[Theory]
	[InlineData(0, false)]
	[InlineData(-50, false)] // exactly -0.50 still trades
	[InlineData(-51, true)]
	[InlineData(-100, true)]
	public void ATraderBelowMinusHalfRefuses(int rep, bool refuses)
	{
		var b = Book(out var log);
		log.SetReputation("medic", rep);
		Assert.Equal(refuses, b.Refuses("medic"));
	}

	[Theory]
	[InlineData(-30, false)]
	[InlineData(-31, true)]
	[InlineData(40, false)]
	public void YourOwnFactionStartsHostileBelowMinusPointThree(int rep, bool hostile)
	{
		var b = Book(out var log);
		log.SetReputation("allied_pmc", rep);
		Assert.Equal(hostile, b.OwnFactionHostileAtStart(Faction.AlliedPmc));
		Assert.False(b.OwnFactionHostileAtStart(Faction.SovietPmc));
	}

	[Fact]
	public void UntrackedFactionsHaveNoReputationTarget()
	{
		Assert.Null(ReputationBook.FactionId(Faction.Looters));
		Assert.Null(ReputationBook.FactionId(Faction.Deserters));
		Assert.Equal("bandits", ReputationBook.FactionId(Faction.Bandits));
		Assert.Equal("allied_qm", ReputationBook.QuartermasterOf(PlayerSide.Allied));
		Assert.Null(ReputationBook.QuartermasterSide("medic"));
	}

	[Fact]
	public void TradingWithTheOtherQuartermasterCostsOwnFactionStandingAndNothingElseDoes()
	{
		var b = Book(out _);
		Assert.Equal(-1, b.OnTrade("soviet_qm", PlayerSide.Allied));
		Assert.Equal(-1, b.Hundredths("allied_pmc"));
		Assert.Equal(0, b.OnTrade("allied_qm", PlayerSide.Allied));
		Assert.Equal(0, b.OnTrade("medic", PlayerSide.Allied));
		Assert.Equal(0, b.OnTrade("soviet_qm", null));
		Assert.Equal(-1, b.Hundredths("allied_pmc"));
	}

	// ---- friendly fire

	private static FriendlyFireTracker Tracker() => new(Faction.AlliedPmc, "allied_qm");

	[Fact]
	public void HitsOnOtherFactionsChangeNothing()
	{
		var t = Tracker();
		Assert.Empty(t.RegisterHit(Faction.SovietPmc, false).Changes);
		Assert.Empty(t.RegisterHit(Faction.Bandits, true).Changes);
		Assert.Equal(0, t.Hits);
		Assert.Empty(t.Hostile);
	}

	[Fact]
	public void AFriendlyHitCostsTwoWithTheFactionAndTheQuartermaster()
	{
		var t = Tracker();
		var r = t.RegisterHit(Faction.AlliedPmc, false);
		Assert.Equal(new[] { new ReputationChange("allied_pmc", -2), new ReputationChange("allied_qm", -2) }, r.Changes);
		Assert.False(r.TurnedHostile);
	}

	[Fact]
	public void AKillCostsFifteenInsteadOfTheHitPenalty()
	{
		var t = Tracker();
		var r = t.RegisterHit(Faction.AlliedPmc, true);
		Assert.Equal(new[] { new ReputationChange("allied_pmc", -15), new ReputationChange("allied_qm", -15) }, r.Changes);
		Assert.Equal(2, r.Changes.Count); // not charged both penalties
	}

	[Fact]
	public void TheThirdFriendlyHitTurnsTheFactionHostileOnce()
	{
		var t = Tracker();
		var seen = new List<Faction>();
		t.BecameHostile += seen.Add;
		Assert.False(t.RegisterHit(Faction.AlliedPmc, false).TurnedHostile);
		Assert.False(t.RegisterHit(Faction.AlliedPmc, false).TurnedHostile);
		Assert.False(t.IsHostileToPlayer(Faction.AlliedPmc));
		Assert.True(t.RegisterHit(Faction.AlliedPmc, false).TurnedHostile);
		Assert.True(t.IsHostileToPlayer(Faction.AlliedPmc));
		Assert.False(t.RegisterHit(Faction.AlliedPmc, false).TurnedHostile); // already hostile
		Assert.Equal(new[] { Faction.AlliedPmc }, seen);
	}

	[Fact]
	public void AKillCountsAsOneOfTheThreeHits()
	{
		var t = Tracker();
		t.RegisterHit(Faction.AlliedPmc, false);
		t.RegisterHit(Faction.AlliedPmc, false);
		Assert.True(t.RegisterHit(Faction.AlliedPmc, true).TurnedHostile);
	}

	[Fact]
	public void ANewTrackerStartsCleanSoHostilityResetsEachRaid()
	{
		var first = Tracker();
		for (var i = 0; i < 3; i++)
			first.RegisterHit(Faction.AlliedPmc, false);
		Assert.True(first.IsHostileToPlayer(Faction.AlliedPmc));
		Assert.False(Tracker().IsHostileToPlayer(Faction.AlliedPmc));
	}

	[Fact]
	public void LowReputationMakesTheFactionHostileFromTheStart()
	{
		var t = Tracker();
		var seen = new List<Faction>();
		t.BecameHostile += seen.Add;
		t.StartHostile(Faction.AlliedPmc);
		t.StartHostile(Faction.AlliedPmc);
		Assert.True(t.IsHostileToPlayer(Faction.AlliedPmc));
		Assert.Single(seen);
	}

	[Fact]
	public void ResultsApplyToTheBookAndSumPerTarget()
	{
		var b = Book(out _);
		var t = Tracker();
		var results = new[] { t.RegisterHit(Faction.AlliedPmc, false), t.RegisterHit(Faction.AlliedPmc, true) };
		foreach (var r in results)
			FriendlyFireTracker.Apply(r, b);
		Assert.Equal(-17, b.Hundredths("allied_pmc"));
		Assert.Equal(-17, b.Hundredths("allied_qm"));
		var total = FriendlyFireTracker.Total(results);
		Assert.Equal(-17, total["allied_pmc"]);
		Assert.Equal(-17, total["allied_qm"]);
	}

	[Fact]
	public void FactionRelationsBetweenAiAreUnchangedByThePlayersStanding()
	{
		Assert.False(FactionRelations.IsHostile(Faction.AlliedPmc, Faction.AlliedPmc));
		Assert.True(FactionRelations.IsHostile(Faction.AlliedPmc, Faction.SovietPmc));
	}

	// ---- trade, insurance, quests

	private static readonly ItemSpec Money = new("banknotes", "banknotes", ItemCategory.Misc, 1, 1, 0f, 5000, BaseValue: 1, Insurable: false);
	private static readonly ItemSpec Rifle = new("ak", "ak", ItemCategory.Weapon, 5, 2, 3.6f, WeaponClass: WeaponClass.Primary, BaseValue: 600);
	private static readonly Dictionary<string, ItemSpec> Specs = new[] { Money, Rifle }.ToDictionary(s => s.Id);

	private static ItemInstance? Make(string id, int count) => Specs.TryGetValue(id, out var s) ? ItemInstance.Create(s, count) : null;

	private static TraderLevelSpec Level() => new(new[] { new ListingSpec("ak") },
		new HashSet<ItemCategory> { ItemCategory.Weapon }, 1f, 0.5f,
		new[] { new BarterSpec(new[] { ("ak", 1) }, "banknotes", 10) }, new InsuranceTerms(0.1f, 0.5f));

	private static (TradeEngine E, GameProfile P) Trade(PlayerSide? side = PlayerSide.Allied, int money = 5000)
	{
		var profile = new GameProfile { Side = side };
		profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Money, money));
		return (new TradeEngine(profile, new CharacterInventory(), id => Specs.GetValueOrDefault(id), Make, _ => 0), profile);
	}

	[Fact]
	public void BuyPricesRiseWhenDislikedAndFallWhenLikedRoundedUp()
	{
		var (e, p) = Trade();
		var state = p.Trader("medic");
		p.Reputation.Add("medic", 50);
		Assert.Equal(570, e.BuyPrice(Level(), Level().Sells[0], state)); // 600 x 0.95
		p.Reputation.Add("medic", -150);                                // -1.00
		Assert.Equal(660, e.BuyPrice(Level(), Level().Sells[0], state)); // 600 x 1.10
		Assert.Equal(600, e.BuyPrice(Level(), Level().Sells[0])); // no trader: no factor
		p.Reputation.Add("medic", 100);
		p.Reputation.Add("medic", -49); // -0.49 -> 1.049 x 600 = 629.4 rounds up
		Assert.Equal(630, e.BuyPrice(Level(), Level().Sells[0], state));
	}

	[Fact]
	public void SellPayoutsRoundDownAndFollowReputation()
	{
		var (e, p) = Trade();
		var state = p.Trader("medic");
		var rifle = ItemInstance.Create(Rifle);
		Assert.Equal(300, e.SellPayout(Level(), rifle, state));
		p.Reputation.Add("medic", 100);
		Assert.Equal(330, e.SellPayout(Level(), rifle, state));
		p.Reputation.Add("medic", -100);
		p.Reputation.Add("medic", -33); // -0.33 -> 0.967 x 300 = 290.1
		Assert.Equal(290, e.SellPayout(Level(), rifle, state));
	}

	[Fact]
	public void ADisgustedTraderRefusesBuySellAndBarterAndChangesNothing()
	{
		var (e, p) = Trade();
		p.Reputation.Add("medic", -60);
		var state = p.Trader("medic");
		var rifle = ItemInstance.Create(Rifle);
		p.Stash.TryPlaceAnywhere(rifle);
		var before = e.Money;
		Assert.Equal(TradeFailure.Refused, e.Buy(Level(), state, Level().Sells[0]).Failure);
		Assert.Equal(TradeFailure.Refused, e.Sell(Level(), state, rifle).Failure);
		Assert.Equal(TradeFailure.Refused, e.Barter(Level(), state, Level().Barters[0]).Failure);
		Assert.Equal(before, e.Money);
		Assert.Contains(p.Stash.Items, i => i == rifle);
		Assert.Equal(0, state.Spent);
		Assert.True(e.Refuses(state));
	}

	[Fact]
	public void TradingJustAboveTheRefusalLineStillWorks()
	{
		var (e, p) = Trade();
		p.Reputation.Add("medic", -50);
		Assert.True(e.Buy(Level(), p.Trader("medic"), Level().Sells[0]).Ok);
	}

	[Fact]
	public void EveryTradeWithTheOtherQuartermasterCostsOneHundredthWithYourFaction()
	{
		var (e, p) = Trade(PlayerSide.Allied);
		var soviet = p.Trader("soviet_qm");
		Assert.True(e.Buy(Level(), soviet, Level().Sells[0]).Ok);
		Assert.Equal(-1, p.Reputation.Hundredths("allied_pmc"));
		var rifle = ItemInstance.Create(Rifle);
		p.Stash.TryPlaceAnywhere(rifle);
		Assert.True(e.Sell(Level(), soviet, rifle).Ok);
		Assert.Equal(-2, p.Reputation.Hundredths("allied_pmc"));
		p.Stash.TryPlaceAnywhere(ItemInstance.Create(Rifle)); // the barter hands one over
		var level = Level(); // one level object: a barter is matched by reference
		Assert.True(e.Barter(level, soviet, level.Barters[0]).Ok);
		Assert.Equal(-3, p.Reputation.Hundredths("allied_pmc"));
		// your own quartermaster and other traders cost nothing
		Assert.True(e.Buy(Level(), p.Trader("allied_qm"), Level().Sells[0]).Ok);
		Assert.True(e.Buy(Level(), p.Trader("medic"), Level().Sells[0]).Ok);
		Assert.Equal(-3, p.Reputation.Hundredths("allied_pmc"));
	}

	[Fact]
	public void AFailedTradeCostsNoReputation()
	{
		var (e, p) = Trade(PlayerSide.Allied, money: 10);
		Assert.Equal(TradeFailure.NotEnoughMoney, e.Buy(Level(), p.Trader("soviet_qm"), Level().Sells[0]).Failure);
		Assert.Equal(0, p.Reputation.Hundredths("allied_pmc"));
	}

	[Fact]
	public void TraderStatesKnowTheirIdAfterCreationAndAfterLoading()
	{
		var p = new GameProfile();
		Assert.Equal("medic", p.Trader("medic").Id);
		p.ReplaceTraders(new[] { new KeyValuePair<string, TraderState>("engineer", new TraderState { Level = 2 }) });
		Assert.Equal("engineer", p.Trader("engineer").Id);
		Assert.Equal(2, p.Trader("engineer").Level);
	}

	private static readonly TraderSpec Qm = new("allied_qm", "qm", TraderUnlock.ForSide, PlayerSide.Allied, new[] { Level() });

	[Fact]
	public void InsurancePremiumsFollowReputationAndRefuseBelowTheLine()
	{
		var (_, profile) = Trade(PlayerSide.Allied);
		var character = new CharacterInventory();
		var rifle = ItemInstance.Create(Rifle);
		character.TryEquip(rifle, EquipSlot.Primary);
		profile.Reputation.Add("allied_qm", -100);
		profile.Reputation.Add("allied_qm", 55); // -0.45: premium 60 x 1.045 = 62.7 -> 63
		var ok = InsuranceService.Insure(profile, character, Qm, rifle);
		Assert.True(ok.Ok);
		Assert.Equal(63, ok.Premium);

		var rifle2 = ItemInstance.Create(Rifle);
		character.TryEquip(rifle2, EquipSlot.Secondary);
		profile.Reputation.Add("allied_qm", -20); // -0.65
		Assert.Equal(InsureFailure.Refused, InsuranceService.Insure(profile, character, Qm, rifle2).Failure);
		Assert.False(rifle2.Insured);
	}

	[Fact]
	public void ThePremiumDisplayHelperMatchesTheCharge()
		=> Assert.Equal(66, InsuranceService.Premium(new InsuranceTerms(0.1f, 0.5f), ItemInstance.Create(Rifle), 1.1f));

	private static QuestSpec Quest(string giver, params RewardSpec[] rewards) => new("q", "t", "d", giver, 1, Array.Empty<string>(), null,
		new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) }, rewards);

	[Fact]
	public void ReputationRewardsResolveSideTargetsAndClamp()
	{
		var profile = new GameProfile { Side = PlayerSide.Soviet };
		var q = Quest("side_qm", new RewardSpec(RewardKind.Reputation, 8, Target: "side_qm"),
			new RewardSpec(RewardKind.Reputation, 5, Target: "side_faction"), new RewardSpec(RewardKind.Reputation, 7, Target: "medic"),
			new RewardSpec(RewardKind.Reputation, 3, Target: "opposite_qm"));
		var log = profile.Quests;
		var cat = new[] { q };
		QuestEngine.Refresh(cat, log, profile);
		QuestEngine.Accept(log, q);
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Looters, WeaponClass.Primary, "map1"));
		QuestEngine.TurnIn(cat, log, profile, new CharacterInventory(), q);
		Assert.Equal(8, profile.Reputation.Hundredths("soviet_qm"));
		Assert.Equal(5, profile.Reputation.Hundredths("soviet_pmc"));
		Assert.Equal(7, profile.Reputation.Hundredths("medic"));
		Assert.Equal(3, profile.Reputation.Hundredths("allied_qm"));
	}

	[Fact]
	public void AGiverWhoRefusesYouHandsOutNoNewQuestsButTurnInsStillWork()
	{
		var profile = new GameProfile { Side = PlayerSide.Allied };
		var q = Quest("medic");
		var log = profile.Quests;
		QuestEngine.Refresh(new[] { q }, log, profile);
		Assert.Equal(QuestState.Available, log.StateOf(q));
		profile.Reputation.Add("medic", -60);
		Assert.True(QuestEngine.GiverRefuses(q, profile));
		Assert.False(QuestEngine.Accept(log, q, profile));
		Assert.Equal(QuestState.Available, log.StateOf(q));
		profile.Reputation.Add("medic", 20); // -0.40: fine again
		Assert.True(QuestEngine.Accept(log, q, profile));
		// once accepted, a later drop in standing does not take the quest away or block the turn-in
		profile.Reputation.Add("medic", -80);
		Assert.Equal(QuestState.Active, log.StateOf(q));
		QuestEngine.OnEvent(new[] { q }, log, new KilledEvent(Faction.Looters, WeaponClass.Primary, "map1"));
		Assert.Equal(QuestState.ReadyToTurnIn, log.StateOf(q));
		Assert.Equal(TurnInFailure.None, QuestEngine.TurnIn(new[] { q }, log, profile, new CharacterInventory(), q));
	}

	[Fact]
	public void AcceptingWithoutAProfileKeepsTheOldBehaviour()
	{
		var profile = new GameProfile { Side = PlayerSide.Allied };
		var q = Quest("medic");
		QuestEngine.Refresh(new[] { q }, profile.Quests, profile);
		profile.Reputation.Add("medic", -90);
		Assert.True(QuestEngine.Accept(profile.Quests, q));
	}

	[Fact]
	public void ReputationSurvivesTheSaveWithoutAMigration()
	{
		var profile = new GameProfile();
		profile.Reputation.Add("allied_pmc", -17);
		profile.Reputation.Add("medic", 8);
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, null, false));
		var loaded = new GameProfile();
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, _ => null, new List<string>());
		Assert.Equal(-17, loaded.Reputation.Hundredths("allied_pmc"));
		Assert.Equal(8, loaded.Reputation.Hundredths("medic"));
	}

	// ---- shipped quest data

	private static string RepoFile(string rel)
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return Path.Combine(dir!, rel);
	}

	[Fact]
	public void EverySliceQuestGivesTheGiverReputationAndSideQuartermasterQuestsAlsoTheOwnFaction()
	{
		var files = Directory.GetFiles(RepoFile("data/quests"), "q0*.tres");
		Assert.Equal(6, files.Length);
		foreach (var file in files)
		{
			var text = File.ReadAllText(file);
			var giver = Regex.Match(text, "^GiverTraderId = \"([^\"]+)\"", RegexOptions.Multiline).Groups[1].Value;
			var rewards = Regex.Matches(text, "Kind = 4\nAmount = (\\d+)\nItemId = \"\"\nTraderId = \"\"\nLevel = 0\nTarget = \"([^\"]+)\"")
				.Select(m => (Target: m.Groups[2].Value, Amount: int.Parse(m.Groups[1].Value))).ToList();
			var mine = rewards.Where(r => r.Target == giver).ToList();
			Assert.True(mine.Count == 1 && mine[0].Amount is >= 5 and <= 10, $"{Path.GetFileName(file)}: giver reward {string.Join(",", rewards)}");
			Assert.Equal(giver == "side_qm", rewards.Any(r => r.Target == "side_faction" && r.Amount == 5));
		}
	}
}
