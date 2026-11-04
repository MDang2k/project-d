using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class NemesisTests
{
	private static readonly ItemSpec Rifle = new("ak", "ak", ItemCategory.Weapon, 5, 2, 3.6f, WeaponClass: WeaponClass.Primary, BaseValue: 600);
	private static readonly ItemSpec Pistol = new("pistol", "pistol", ItemCategory.Weapon, 2, 1, 1f, WeaponClass: WeaponClass.Pistol, BaseValue: 200);
	private static readonly ItemSpec VestT2 = new("vest2", "vest2", ItemCategory.Armor, 3, 3, 4f, BaseValue: 1000, MaxDurability: 60f);
	private static readonly ItemSpec VestT4 = new("vest4", "vest4", ItemCategory.Armor, 3, 3, 6f, BaseValue: 3000, MaxDurability: 80f);
	private static readonly ItemSpec Helmet = new("helm", "helm", ItemCategory.Helmet, 2, 2, 1f, BaseValue: 500, MaxDurability: 40f);
	private static readonly ItemSpec Pack = new("pack", "pack", ItemCategory.Backpack, 4, 4, 1f, Grids: new[] { new GridSize(5, 4) }, BaseValue: 200);
	private static readonly ItemSpec Bandage = new("bandage", "bandage", ItemCategory.Medical, 1, 1, 0.1f, 5, BaseValue: 40, Insurable: false);
	private static readonly ItemSpec Mag = new("mag", "mag", ItemCategory.Magazine, 1, 2, 0.2f, MagazineCapacity: 30, BaseValue: 50);

	private static int Tier(ItemSpec s) => s.Id switch { "vest2" => 2, "vest4" => 4, _ => 0 };

	private static ItemInstance I(ItemSpec spec, int count = 1) => ItemInstance.Create(spec, count);

	private static NemesisNames Names() => NemesisNames.Parse("""
		{ "allied_pmc": { "first": ["Jack"], "last": ["Cole"], "nicknames": ["Ghost"] },
		  "looters": { "first": ["Hans"], "last": ["Becker"], "nicknames": ["Rattle"] } }
		""");

	private static NemesisKill Kill(int raid = 1, string map = "map1", float x = 10f, float z = 20f) => new(raid, map, x, z);

	private static List<LostItem> Loadout() => new()
	{
		new(I(Rifle), EquipSlot.Primary), new(I(VestT2), EquipSlot.Vest), new(I(Helmet), EquipSlot.Head),
		new(I(Pack), EquipSlot.Backpack), new(I(Bandage, 3), null),
	};

	/// <summary>A nemesis inventory with a backpack on, so there is room to carry spare gear (pockets are tiny).</summary>
	private static CharacterInventory WithPack()
	{
		var gear = new CharacterInventory();
		Assert.True(gear.TryEquip(I(Pack), EquipSlot.Backpack));
		return gear;
	}

	private static NemesisKillResult Register(NemesisRoster r, string? existing = null, IEnumerable<LostItem>? lost = null,
		Faction faction = Faction.AlliedPmc, bool friendly = false, NemesisKill? kill = null)
		=> r.RegisterKill(existing, faction, friendly, kill ?? Kill(), lost ?? Loadout(), Names(), Tier, new Random(1));

	// ---- who killed the player

	[Fact]
	public void TheKillingBlowIsTheKiller()
	{
		var k = new KillAttribution();
		k.RecordHit(7, 100f);
		Assert.Equal(9L, k.Killer(9, 101f));
	}

	[Fact]
	public void ABleedOutBlamesTheLastAttackerWithinTheWindow()
	{
		var k = new KillAttribution(60f);
		k.RecordHit(7, 100f);
		k.RecordHit(8, 110f);
		Assert.Equal(8L, k.Killer(null, 170f));  // 60 s after the last hit: still in
		Assert.Null(k.Killer(null, 170.5f));     // just outside
	}

	[Fact]
	public void NoAttackerOrAResetMeansNobodyIsBlamed()
	{
		var k = new KillAttribution();
		Assert.Null(k.Killer(null, 5f)); // MIA, a mine, a fall
		k.RecordHit(3, 10f);
		k.Reset();
		Assert.Null(k.Killer(null, 12f));
	}

	// ---- gear

	[Fact]
	public void ReturnedOriginalsAreNotKeptByTheNemesis()
	{
		var rifle = I(Rifle);
		var pistol = I(Pistol);
		var pack = I(Pack);
		var insuredBandageBox = I(Mag);
		Assert.True(pack.Grids[0].TryPlaceAnywhere(insuredBandageBox));
		var other = I(Mag);
		Assert.True(pack.Grids[0].TryPlaceAnywhere(other));
		rifle.InsertedMagazine = I(Mag);
		var lost = new List<LostItem> { new(rifle, EquipSlot.Primary), new(pistol, EquipSlot.Pistol), new(pack, EquipSlot.Backpack) };
		var kept = NemesisGear.Salvage(lost, new[] { pistol, insuredBandageBox, rifle.InsertedMagazine });
		Assert.Equal(new[] { rifle, pack }, kept.Select(l => l.Item)); // the insured pistol is gone
		Assert.Null(rifle.InsertedMagazine);                          // and the insured magazine in the rifle
		Assert.Equal(new[] { other }, pack.Grids[0].Items);           // and the insured thing in the pack
	}

	[Fact]
	public void NothingReturnedKeepsEverythingUntouched()
	{
		var loadout = Loadout();
		var kept = NemesisGear.Salvage(loadout, Array.Empty<ItemInstance>());
		Assert.Equal(loadout.Count, kept.Count);
	}

	[Fact]
	public void GearGoesBackToTheSlotsItWasWornIn()
	{
		var gear = new CharacterInventory();
		var items = Loadout();
		var noRoom = NemesisGear.Equip(gear, items, Tier);
		Assert.Equal(0, noRoom);
		Assert.Equal(items[0].Item, gear.InSlot(EquipSlot.Primary));
		Assert.Equal(items[1].Item, gear.InSlot(EquipSlot.Vest));
		Assert.Equal(items[2].Item, gear.InSlot(EquipSlot.Head));
		Assert.Equal(items[3].Item, gear.InSlot(EquipSlot.Backpack));
		Assert.Contains(items[4].Item, gear.AllItems());
		Assert.Equal(items[0].Item, NemesisGear.BestWeapon(gear));
	}

	[Fact]
	public void BestWeaponIsPrimaryThenSecondaryThenPistol()
	{
		var gear = new CharacterInventory();
		Assert.Null(NemesisGear.BestWeapon(gear));
		var pistol = I(Pistol);
		gear.TryEquip(pistol, EquipSlot.Pistol);
		Assert.Equal(pistol, NemesisGear.BestWeapon(gear));
		var second = I(Rifle);
		gear.TryEquip(second, EquipSlot.Secondary);
		Assert.Equal(second, NemesisGear.BestWeapon(gear));
		var first = I(Rifle);
		gear.TryEquip(first, EquipSlot.Primary);
		Assert.Equal(first, NemesisGear.BestWeapon(gear));
	}

	[Fact]
	public void BetterArmorReplacesWorseAndWorseIsCarried()
	{
		var gear = WithPack();
		var low = I(VestT2);
		NemesisGear.Equip(gear, new[] { new LostItem(low, EquipSlot.Vest) }, Tier);
		var high = I(VestT4);
		NemesisGear.Equip(gear, new[] { new LostItem(high, EquipSlot.Vest) }, Tier);
		Assert.Equal(high, gear.InSlot(EquipSlot.Vest));
		Assert.Contains(low, gear.AllItems()); // kept, carried
		var worse = I(VestT2);
		var noRoom = NemesisGear.Equip(gear, new[] { new LostItem(worse, EquipSlot.Vest) }, Tier);
		Assert.Equal(high, gear.InSlot(EquipSlot.Vest)); // a worse vest never replaces it
		Assert.Equal(1, noRoom);                         // and the pack (5x4) already holds one spare vest, so this one is left behind
	}

	[Fact]
	public void AWeaponTakesAnEmptySlotButDoesNotReplaceTheOneItHas()
	{
		var gear = WithPack();
		var first = I(Rifle);
		NemesisGear.Equip(gear, new[] { new LostItem(first, EquipSlot.Primary) }, Tier);
		var newer = I(Rifle);
		NemesisGear.Equip(gear, new[] { new LostItem(newer, EquipSlot.Primary) }, Tier);
		Assert.Equal(first, gear.InSlot(EquipSlot.Primary));
		Assert.Contains(newer, gear.AllItems());
	}

	[Fact]
	public void ItemsWithNoRoomAreCounted()
	{
		var gear = new CharacterInventory(new[] { new GridSize(1, 1) });
		var noRoom = NemesisGear.Equip(gear, new[] { new LostItem(I(Bandage), null), new LostItem(I(Mag), null), new LostItem(I(Mag), null) }, Tier);
		Assert.Equal(2, noRoom);
	}

	// ---- creation, growth, cap

	[Fact]
	public void AKillCreatesANemesisWithTheKillTheGearAndAGeneratedName()
	{
		var roster = new NemesisRoster();
		var r = Register(roster, kill: Kill(raid: 4, x: 12f, z: -7f));
		Assert.True(r.IsNew);
		var n = r.Nemesis;
		Assert.Equal("n1", n.Id);
		Assert.Equal("Jack Cole", n.Name);
		Assert.Equal("Ghost", n.Nickname);
		Assert.Equal(Faction.AlliedPmc, n.Faction);
		Assert.Equal(1, n.Rank);
		Assert.Equal(1, n.KillCount);
		Assert.Equal((12f, -7f), (n.X, n.Z));
		Assert.Equal(4, n.RaidCreated);
		Assert.NotNull(n.Gear.InSlot(EquipSlot.Primary));
		Assert.Equal(new[] { n }, roster.All);
		Assert.Equal("Jack Cole \"Ghost\"", n.DisplayName);
	}

	[Fact]
	public void AFriendlyKillerIsMarkedSoItIsHostileToThePlayerOnly()
		=> Assert.True(Register(new NemesisRoster(), friendly: true).Nemesis.FriendlyOrigin);

	[Fact]
	public void KillingThePlayerAgainGrowsItAndAddsTheNewGear()
	{
		var roster = new NemesisRoster();
		var first = Register(roster).Nemesis;
		var second = Register(roster, first.Id, new List<LostItem> { new(I(VestT4), EquipSlot.Vest), new(I(Bandage, 2), null) },
			kill: Kill(raid: 6, x: 40f, z: 41f));
		Assert.False(second.IsNew);
		Assert.Same(first, second.Nemesis);
		Assert.Equal(2, first.Rank);
		Assert.Equal(2, first.KillCount);
		Assert.Equal(new[] { 1, 6 }, first.Kills.Select(k => k.Raid)); // the dossier keeps both
		Assert.Equal((40f, 41f), (first.X, first.Z));
		Assert.Equal("vest4", first.Gear.InSlot(EquipSlot.Vest)!.Spec.Id); // better armor put on
		Assert.Single(roster.All);
	}

	[Fact]
	public void RankStopsAtThree()
	{
		var roster = new NemesisRoster();
		var n = Register(roster).Nemesis;
		for (var i = 0; i < 5; i++)
			Register(roster, n.Id, new List<LostItem>());
		Assert.Equal(3, n.Rank);
		Assert.Equal(6, n.KillCount);
	}

	[Fact]
	public void ToughnessIsTenPercentPerRank()
	{
		var t = new NemesisTuning();
		var n = Register(new NemesisRoster()).Nemesis;
		Assert.Equal(1.1f, n.Toughness(t), 3);
		n.Rank = 3;
		Assert.Equal(1.3f, n.Toughness(t), 3);
	}

	[Fact]
	public void AFourthNemesisOnAMapRetiresTheOldest()
	{
		var roster = new NemesisRoster();
		var a = Register(roster).Nemesis;
		Register(roster);
		Register(roster);
		var other = Register(roster, kill: Kill(map: "map2")).Nemesis; // another map has its own cap
		Assert.Equal(4, roster.All.Count);
		var fourth = Register(roster);
		Assert.Same(a, fourth.Retired);
		Assert.DoesNotContain(a, roster.All);
		Assert.Equal(3, roster.ForMap("map1").Count());
		Assert.Contains(other, roster.All);
	}

	[Fact]
	public void UnknownExistingIdMakesANewNemesisInstead()
		=> Assert.True(Register(new NemesisRoster(), existing: "n99").IsNew);

	[Fact]
	public void IdsKeepCountingUpAfterALoad()
	{
		var roster = new NemesisRoster();
		roster.Add(new Nemesis("n7", "A", "B", Faction.Bandits, "map1", 0, 0, 1, false, new CharacterInventory()));
		Assert.Equal("n8", Register(roster).Nemesis.Id);
	}

	// ---- spawning and removal

	[Fact]
	public void EachNemesisRollsAtThirtyPercentAndAtMostOneSpawns()
	{
		var roster = new NemesisRoster();
		for (var i = 0; i < 3; i++)
			Register(roster);
		var spawned = Enumerable.Range(0, 2000).Count(s => roster.RollSpawn("map1", new Random(s)) is not null);
		// P(at least one of three rolls succeeds) = 1 - 0.7^3 = 0.657
		Assert.InRange(spawned / 2000.0, 0.60, 0.71);
		Assert.Null(roster.RollSpawn("map2", new Random(1)));
	}

	[Fact]
	public void NeverMoreThanOneComesBackFromARoll()
	{
		var roster = new NemesisRoster(new NemesisTuning(SpawnChance: 1f));
		for (var i = 0; i < 3; i++)
			Register(roster);
		Assert.NotNull(roster.RollSpawn("map1", new Random(1))); // a single nemesis, never a list
	}

	[Fact]
	public void ADeadNemesisLeavesTheRosterAndIsNoted()
	{
		var roster = new NemesisRoster();
		var n = Register(roster).Nemesis;
		Assert.True(roster.Remove(n.Id, "the player", 9));
		Assert.Empty(roster.All);
		Assert.Equal(new FallenNemesis("Jack Cole", "Ghost", "the player", 9), roster.Fallen.Single());
		Assert.False(roster.Remove(n.Id, "x", 9));
		roster.AcknowledgeFallen();
		Assert.Empty(roster.Fallen);
	}

	// ---- names

	[Fact]
	public void NamesFallBackToLootersThenToAPlainLabel()
	{
		Assert.Equal(("Hans Becker", "Rattle"), Names().Generate(Faction.Bandits, new Random(1)));
		Assert.Equal(("Unknown", "the Survivor"), NemesisNames.Parse("{}").Generate(Faction.Bandits, new Random(1)));
		Assert.Equal(("Unknown Unnamed", "the Survivor"), NemesisNames.Parse("""{ "looters": {} }""").Generate(Faction.Looters, new Random(1)));
	}

	[Fact]
	public void TheShippedNameListsCoverEveryFactionAndNeverRepeatInAList()
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		var names = NemesisNames.Parse(File.ReadAllText(Path.Combine(dir!, "data/ai/nemesis_names.json")));
		foreach (var faction in new[] { Faction.AlliedPmc, Faction.SovietPmc, Faction.Bandits, Faction.Looters, Faction.Deserters })
		{
			var lists = names.Lists(NemesisNames.KeyOf(faction));
			Assert.True(lists.First.Count >= 6 && lists.Last.Count >= 6 && lists.Nicknames.Count >= 5, faction.ToString());
			Assert.Equal(lists.First.Count, lists.First.Distinct().Count());
			Assert.Equal(lists.Last.Count, lists.Last.Distinct().Count());
			Assert.Equal(lists.Nicknames.Count, lists.Nicknames.Distinct().Count());
		}
	}

	// ---- insurance and save

	[Fact]
	public void InsuranceReportsTheOriginalsOfItemsItIsReturning()
	{
		var money = new ItemSpec("banknotes", "banknotes", ItemCategory.Misc, 1, 1, 0f, 5000, BaseValue: 1, Insurable: false);
		var profile = new GameProfile { Side = PlayerSide.Allied };
		profile.Stash.TryPlaceAnywhere(ItemInstance.Create(money, 1000));
		var rifle = I(Rifle);
		rifle.Insured = true;
		rifle.InsuredWith = "qm";
		var other = I(Pistol);
		var level = new TraderLevelSpec(new List<ListingSpec>(), new HashSet<ItemCategory>(), 1f, 0.5f, new List<BarterSpec>(), new InsuranceTerms(0.1f, 1f));
		var result = InsuranceService.OnRaidEnd(RaidOutcome.Killed, new List<ItemInstance> { rifle, other }, profile, null,
			id => id == "qm" ? level : null, () => 0.0); // the roll always succeeds
		Assert.Single(result.Returned);
		Assert.Equal(new[] { rifle }, result.ReturnedOriginals);
		var kept = NemesisGear.Salvage(new[] { new LostItem(rifle, EquipSlot.Primary), new LostItem(other, EquipSlot.Pistol) }, result.ReturnedOriginals);
		Assert.Equal(new[] { other }, kept.Select(l => l.Item));
	}

	private static ItemSpec? Lookup(string id) => new[] { Rifle, Pistol, VestT2, VestT4, Helmet, Pack, Bandage, Mag }.FirstOrDefault(s => s.Id == id);

	[Fact]
	public void NemesesSurviveASaveRoundTripWithTheirGear()
	{
		var profile = new GameProfile();
		var n = Register(profile.Nemeses, kill: Kill(raid: 3, x: 5f, z: 6f)).Nemesis;
		Register(profile.Nemeses, n.Id, new List<LostItem> { new(I(VestT4), EquipSlot.Vest) }, kill: Kill(raid: 5, x: 7f, z: 8f));
		n.Gear.InSlot(EquipSlot.Vest)!.Durability = 33f;
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, null, false));
		Assert.Contains("\"nemeses\"", json);

		var loaded = new GameProfile();
		var warnings = new List<string>();
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, Lookup, warnings);
		Assert.Empty(warnings);
		var back = loaded.Nemeses.All.Single();
		Assert.Equal((n.Id, n.Name, n.Nickname, n.Faction, n.MapId), (back.Id, back.Name, back.Nickname, back.Faction, back.MapId));
		Assert.Equal((7f, 8f, 2, 3), (back.X, back.Z, back.Rank, back.RaidCreated));
		Assert.Equal(new[] { 3, 5 }, back.Kills.Select(k => k.Raid));
		Assert.Equal("vest4", back.Gear.InSlot(EquipSlot.Vest)!.Spec.Id);
		Assert.Equal(33f, back.Gear.InSlot(EquipSlot.Vest)!.Durability);
		Assert.Equal("ak", back.Gear.InSlot(EquipSlot.Primary)!.Spec.Id);
		Assert.Equal(n.Gear.AllItems().Count(), back.Gear.AllItems().Count());
		Assert.Equal("n2", Register(loaded.Nemeses).Nemesis.Id);
	}

	[Fact]
	public void AVersion8SaveLoadsWithNoNemeses()
	{
		const string v8 = """{"version":8,"raidInProgress":false,"counters":{"raids":2,"extractions":1,"deaths":1,"mia":0},"quickUse":[]}""";
		var file = SaveSerializer.Parse(v8);
		Assert.Equal(SaveFile.CurrentVersion, file.Version);
		Assert.True(SaveFile.CurrentVersion >= 9);
		Assert.NotNull(file.Nemeses);
		Assert.Empty(file.Nemeses!);
	}

	[Fact]
	public void ANemesisWithAnUnknownFactionIsDroppedWithAWarning()
	{
		var profile = new GameProfile();
		Register(profile.Nemeses);
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, null, false)).Replace("\"AlliedPmc\"", "\"Martians\"");
		var warnings = new List<string>();
		var loaded = new GameProfile();
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, Lookup, warnings);
		Assert.Empty(loaded.Nemeses.All);
		Assert.Single(warnings);
	}

	[Fact]
	public void AnOutOfRangeRankInASaveIsClamped()
	{
		var profile = new GameProfile();
		Register(profile.Nemeses);
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, null, false));
		json = System.Text.RegularExpressions.Regex.Replace(json, @"""rank"":\s*1", "\"rank\":99");
		var loaded = new GameProfile();
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, Lookup, new List<string>());
		Assert.Equal(3, loaded.Nemeses.All.Single().Rank);
	}
}
