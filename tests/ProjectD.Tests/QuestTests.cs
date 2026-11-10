using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class QuestTests
{
	private static readonly ItemSpec Money = new("banknotes", "banknotes", ItemCategory.Misc, 1, 1, 0f, 50000, BaseValue: 1);
	private static readonly ItemSpec Bandage = new("bandage", "bandage", ItemCategory.Medical, 1, 1, 0.1f, 5, BaseValue: 40);
	private static readonly ItemSpec Helmet = new("helmet", "helmet", ItemCategory.Helmet, 2, 2, 1f, BaseValue: 150);
	private static readonly ItemSpec Vest = new("vest", "vest", ItemCategory.Armor, 3, 3, 4f, BaseValue: 3000);
	private static readonly Dictionary<string, ItemSpec> Specs = new[] { Money, Bandage, Helmet, Vest }.ToDictionary(s => s.Id);

	private static ItemInstance? Make(string id, int count) => Specs.TryGetValue(id, out var s) ? ItemInstance.Create(s, count) : null;

	private static QuestSpec Quest(string id, ObjectiveSpec[] objectives, RewardSpec[]? rewards = null,
		string giver = "medic", int minLevel = 1, string[]? requires = null, PlayerSide? side = null)
		=> new(id, id, id, giver, minLevel, requires ?? System.Array.Empty<string>(), side, objectives,
			rewards ?? System.Array.Empty<RewardSpec>());

	private static (GameProfile Profile, QuestLog Log, CharacterInventory Character) Setup(PlayerSide side = PlayerSide.Allied)
		=> (new GameProfile { Side = side }, new QuestLog(), new CharacterInventory());

	private static IReadOnlyList<QuestSpec> One(QuestSpec q) => new[] { q };

	private static void Activate(IReadOnlyList<QuestSpec> catalog, QuestLog log, GameProfile profile)
	{
		QuestEngine.Refresh(catalog, log, profile);
		foreach (var q in catalog)
			QuestEngine.Accept(log, q);
	}

	// --- eliminate -------------------------------------------------------------------------

	[Fact]
	public void EliminateCountsOnlyMatchingFactionWeaponAndMap()
	{
		var q = Quest("k", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 2, Faction: Faction.Looters,
			WeaponClass: WeaponClass.Pistol, MapId: "map1") });
		var (profile, log, _) = Setup();
		var cat = One(q);
		Activate(cat, log, profile);
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Bandits, WeaponClass.Pistol, "map1"));
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Looters, WeaponClass.Primary, "map1"));
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Looters, WeaponClass.Pistol, "map2"));
		Assert.Equal(0, log.Entry(q).Progress[0]);
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Looters, WeaponClass.Pistol, "map1"));
		Assert.Equal(1, log.Entry(q).Progress[0]);
		Assert.Equal(QuestState.Active, log.StateOf(q));
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Looters, WeaponClass.Pistol, "map1"));
		Assert.Equal(QuestState.ReadyToTurnIn, log.StateOf(q));
	}

	[Fact]
	public void ProgressOnlyCountsForActiveQuests()
	{
		var q = Quest("k", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) });
		var (profile, log, _) = Setup();
		var cat = One(q);
		QuestEngine.Refresh(cat, log, profile); // available, not accepted
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Looters, WeaponClass.Primary, "map1"));
		Assert.Equal(0, log.Entry(q).Progress[0]);
	}

	[Fact]
	public void KillProgressIsKeptAcrossARoundTripThroughTheSave()
	{
		var q = Quest("k", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 3) });
		var (profile, log, _) = Setup();
		var cat = One(q);
		Activate(cat, profile.Quests, profile);
		QuestEngine.OnEvent(cat, profile.Quests, new KilledEvent(Faction.Looters, WeaponClass.Primary, "map1"));
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, null, false));
		var loaded = new GameProfile();
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, _ => null, new List<string>());
		Assert.Equal(1, loaded.Quests.Entry(q).Progress[0]);
		Assert.Equal(QuestState.Active, loaded.Quests.StateOf(q));
		Assert.Equal("k", loaded.Quests.TrackedId);
	}

	// --- locate and extract ----------------------------------------------------------------

	[Fact]
	public void LocateAndExtractNeedTheirZoneAndOptionalItem()
	{
		var q = Quest("z", new[]
		{
			new ObjectiveSpec(ObjectiveKind.Locate, ZoneId: "tower"),
			new ObjectiveSpec(ObjectiveKind.Extract, ZoneId: "drain", ItemId: "bandage"),
		});
		var (profile, log, _) = Setup();
		var cat = One(q);
		Activate(cat, log, profile);
		QuestEngine.OnEvent(cat, log, new ZoneReachedEvent("plaza", "map1"));
		QuestEngine.OnEvent(cat, log, new ExtractedEvent("drain", new[] { "helmet" }, "map1"));
		Assert.Equal(new[] { 0, 0 }, log.Entry(q).Progress);
		QuestEngine.OnEvent(cat, log, new ZoneReachedEvent("tower", "map1"));
		QuestEngine.OnEvent(cat, log, new ExtractedEvent("drain", new[] { "bandage" }, "map1"));
		Assert.Equal(QuestState.ReadyToTurnIn, log.StateOf(q));
	}

	// --- hand-ins --------------------------------------------------------------------------

	[Fact]
	public void FindInRaidCountsOnlyFlaggedItems()
	{
		var q = Quest("f", new[] { new ObjectiveSpec(ObjectiveKind.FindInRaid, 2, "bandage") });
		var (profile, log, character) = Setup();
		var cat = One(q);
		Activate(cat, log, profile);
		profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Bandage, 3)); // bought: not flagged
		QuestEngine.Recount(cat, log, profile, character);
		Assert.Equal(QuestState.Active, log.StateOf(q));
		Assert.Equal(0, log.Entry(q).Progress[0]);
		var found = ItemInstance.Create(Bandage, 2);
		found.FoundInRaid = true;
		profile.Stash.TryPlaceAnywhere(found);
		QuestEngine.Recount(cat, log, profile, character);
		Assert.Equal(QuestState.ReadyToTurnIn, log.StateOf(q));
	}

	[Fact]
	public void FetchConsumesFromStashAndCarriedAndLosingItemsRevertsReadiness()
	{
		var q = Quest("h", new[] { new ObjectiveSpec(ObjectiveKind.Fetch, 2, "helmet") },
			new[] { new RewardSpec(RewardKind.Item, 1, "vest") });
		var (profile, log, character) = Setup();
		var cat = One(q);
		Activate(cat, log, profile);
		var inStash = ItemInstance.Create(Helmet);
		profile.Stash.TryPlaceAnywhere(inStash);
		var pack = ItemInstance.Create(new ItemSpec("pack", "pack", ItemCategory.Backpack, 4, 4, 1f, Grids: new[] { new GridSize(5, 4) }));
		character.TryEquip(pack, EquipSlot.Backpack);
		pack.Grids[0].TryPlaceAnywhere(ItemInstance.Create(Helmet)); // carried helmet counts too
		QuestEngine.Recount(cat, log, profile, character);
		Assert.Equal(QuestState.ReadyToTurnIn, log.StateOf(q));
		profile.Stash.Remove(inStash);
		QuestEngine.Recount(cat, log, profile, character);
		Assert.Equal(QuestState.Active, log.StateOf(q));
		profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Helmet));
		QuestEngine.Recount(cat, log, profile, character);
		Assert.Equal(TurnInFailure.None, QuestEngine.TurnIn(cat, log, profile, character, q));
		Assert.Equal(0, QuestEngine.CountItems(profile, character, "helmet", false));
		Assert.Equal(QuestState.Completed, log.StateOf(q));
	}

	[Fact]
	public void TurnInRefusesWhenNotReady()
	{
		var q = Quest("h", new[] { new ObjectiveSpec(ObjectiveKind.Fetch, 1, "helmet") });
		var (profile, log, character) = Setup();
		var cat = One(q);
		Activate(cat, log, profile);
		Assert.Equal(TurnInFailure.NotReady, QuestEngine.TurnIn(cat, log, profile, character, q));
	}

	// --- prerequisites ---------------------------------------------------------------------

	[Fact]
	public void PrerequisiteChainLevelAndSide()
	{
		var first = Quest("one", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) });
		var second = Quest("two", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) }, requires: new[] { "one" });
		var leveled = Quest("lvl", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) }, minLevel: 2);
		var sided = Quest("sov", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) }, side: PlayerSide.Soviet);
		var cat = new[] { first, second, leveled, sided };
		var (profile, log, character) = Setup(PlayerSide.Allied);
		QuestEngine.Refresh(cat, log, profile);
		Assert.Equal(QuestState.Available, log.StateOf(first));
		Assert.Equal(QuestState.Locked, log.StateOf(second));
		Assert.Equal(QuestState.Locked, log.StateOf(leveled));
		Assert.Equal(QuestState.Locked, log.StateOf(sided));
		QuestEngine.Accept(log, first);
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Looters, WeaponClass.Primary, "map1"));
		Assert.Equal(TurnInFailure.None, QuestEngine.TurnIn(cat, log, profile, character, first));
		Assert.Equal(QuestState.Available, log.StateOf(second)); // unlocked by the turn-in
		profile.Trader("medic").Level = 2;
		QuestEngine.Refresh(cat, log, profile);
		Assert.Equal(QuestState.Available, log.StateOf(leveled));
	}

	[Fact]
	public void SideQuartermasterGiverResolvesToYourSideAndLevelChecks()
	{
		var q = Quest("qm", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) }, giver: QuestEngine.SideQuartermaster, minLevel: 2);
		var (profile, log, _) = Setup(PlayerSide.Soviet);
		QuestEngine.Refresh(One(q), log, profile);
		Assert.Equal(QuestState.Locked, log.StateOf(q));
		profile.Trader("soviet_qm").Level = 2;
		QuestEngine.Refresh(One(q), log, profile);
		Assert.Equal(QuestState.Available, log.StateOf(q));
		Assert.Equal("allied_qm", QuestEngine.ResolveTrader(QuestEngine.OppositeQuartermaster, PlayerSide.Soviet));
	}

	// --- rewards ---------------------------------------------------------------------------

	[Fact]
	public void RewardsLevelNeverLowersUnlockAndMoneyAndItemsArePendingUntilDelivered()
	{
		var q = Quest("r", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) }, new[]
		{
			new RewardSpec(RewardKind.TraderLevel, TraderId: "medic", Level: 3),
			new RewardSpec(RewardKind.TraderLevel, TraderId: "engineer", Level: 1),
			new RewardSpec(RewardKind.UnlockTrader, TraderId: QuestEngine.OppositeQuartermaster),
			new RewardSpec(RewardKind.Money, 1500),
			new RewardSpec(RewardKind.Item, 1, "vest"),
			new RewardSpec(RewardKind.Reputation, 5, Target: "bandits"),
		});
		var (profile, log, character) = Setup(PlayerSide.Allied);
		profile.Trader("engineer").Level = 3;
		var cat = One(q);
		Activate(cat, log, profile);
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Looters, WeaponClass.Primary, "map1"));
		QuestEngine.TurnIn(cat, log, profile, character, q);
		Assert.Equal(3, profile.Trader("medic").Level);
		Assert.Equal(3, profile.Trader("engineer").Level); // not lowered
		Assert.Contains("soviet_qm", profile.UnlockedTraders);
		Assert.Equal(2, log.Pending.Count);
		Assert.Equal(5, profile.Reputation.Hundredths("bandits")); // through the clamped book, not the detached test log
		var delivered = QuestEngine.DeliverPending(log, profile, Make);
		Assert.Equal(2, delivered.Count);
		Assert.Empty(log.Pending);
		Assert.Equal(1500, QuestEngine.CountItems(profile, character, "banknotes", false));
		Assert.Equal(1, QuestEngine.CountItems(profile, character, "vest", false));
	}

	[Fact]
	public void FullStashKeepsTheRewardPending()
	{
		var (profile, log, _) = Setup();
		var filler = new ItemSpec("filler", "filler", ItemCategory.Misc, GameProfile.StashWidth, 1, 0.1f);
		for (var i = 0; i < GameProfile.StashHeight; i++)
			profile.Stash.TryPlaceAnywhere(ItemInstance.Create(filler));
		log.AddPending(new PendingReward("vest", 1));
		Assert.Empty(QuestEngine.DeliverPending(log, profile, Make));
		Assert.Single(log.Pending);
		foreach (var row in profile.Stash.Items.Take(3).ToList())
			profile.Stash.Remove(row);
		Assert.Single(QuestEngine.DeliverPending(log, profile, Make));
		Assert.Empty(log.Pending);
	}

	// --- tracking --------------------------------------------------------------------------

	[Fact]
	public void MostRecentlyAcceptedIsTrackedAndCompletionMovesTrackingOn()
	{
		var a = Quest("a", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) });
		var b = Quest("b", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 5) });
		var cat = new[] { a, b };
		var (profile, log, character) = Setup();
		QuestEngine.Refresh(cat, log, profile);
		QuestEngine.Accept(log, a);
		QuestEngine.Accept(log, b);
		Assert.Equal("b", log.TrackedId);
		QuestEngine.Track(log, a);
		Assert.Equal("a", log.TrackedId);
		QuestEngine.OnEvent(cat, log, new KilledEvent(Faction.Looters, WeaponClass.Primary, "map1"));
		QuestEngine.TurnIn(cat, log, profile, character, a);
		Assert.Equal("b", log.TrackedId);
		Assert.Equal("b", QuestEngine.Tracked(cat, log)?.Id);
	}

	[Fact]
	public void LockedQuestsCannotBeAcceptedOrTracked()
	{
		var q = Quest("l", new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) }, minLevel: 3);
		var (profile, log, _) = Setup();
		QuestEngine.Refresh(One(q), log, profile);
		Assert.False(QuestEngine.Accept(log, q));
		Assert.False(QuestEngine.Track(log, q));
	}
}

public class SaveV5Tests
{
	[Fact]
	public void FoundInRaidFlagAndUnlocksAndPendingRewardsRoundTrip()
	{
		var spec = new ItemSpec("bandage", "bandage", ItemCategory.Medical, 1, 1, 0.1f, 5, BaseValue: 40);
		var profile = new GameProfile();
		var found = ItemInstance.Create(spec, 2);
		found.FoundInRaid = true;
		profile.Stash.TryPlaceAnywhere(found);
		profile.UnlockTrader("soviet_qm");
		profile.Quests.AddPending(new PendingReward("vest", 1));
		profile.Quests.AddReputation("bandits", 3);
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, null, false));
		var loaded = new GameProfile();
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, id => id == "bandage" ? spec : null, new List<string>());
		Assert.True(loaded.Stash.Items.First().FoundInRaid);
		Assert.Contains("soviet_qm", loaded.UnlockedTraders);
		Assert.Single(loaded.Quests.Pending);
		Assert.Equal(3, loaded.Quests.Reputation["bandits"]);
	}

	[Fact]
	public void V4SaveMigratesToEmptyQuestState()
	{
		const string v4 = """{"version":4,"raidInProgress":false,"counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[],"insuranceReturns":[]}""";
		var file = SaveSerializer.Parse(v4);
		Assert.Equal(SaveFile.CurrentVersion, file.Version);
		Assert.Empty(file.Quests!);
		Assert.Null(file.Tracked);
		Assert.Empty(file.UnlockedTraders!);
	}
}
