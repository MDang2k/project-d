using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class StarterKitTests
{
	private static readonly Dictionary<string, ItemSpec> Specs = new()
	{
		["ak"] = Items.Rifle,
		["pistol"] = Items.Pistol,
		["ammo"] = Items.Ammo,
		["bandage"] = Items.Bandage,
		["mag"] = Items.Make("mag", 1, 2, 0.2f, ItemCategory.Magazine) with { MagazineCapacity = 30 },
		["brick"] = Items.Make("brick", 4, 4, 1f),
	};

	private static ItemInstance? Make(string id, int count) => Specs.TryGetValue(id, out var s) ? ItemInstance.Create(s, count) : null;

	[Fact]
	public void StacksSplitAtMaxStackAndMagazinesAreLoaded()
	{
		var items = StarterKit.Build(new[]
		{
			new StarterEntry("ammo", 130),
			new StarterEntry("mag", 2, "ammo_id"),
			new StarterEntry("bandage", 4),
		}, Make);
		Assert.Equal(new[] { 60, 60, 10 }, items.Where(i => i.Spec.Id == "ammo").Select(i => i.Count).ToArray());
		var mags = items.Where(i => i.Spec.Id == "mag").ToList();
		Assert.Equal(2, mags.Count);
		Assert.All(mags, m => Assert.Equal(30, m.Rounds.Count));
		Assert.Equal(new[] { 3, 1 }, items.Where(i => i.Spec.Id == "bandage").Select(i => i.Count).ToArray());
	}

	[Fact]
	public void UnknownItemsAreSkipped()
	{
		Assert.Empty(StarterKit.Build(new[] { new StarterEntry("nope", 3) }, Make));
	}

	[Fact]
	public void OverflowIsReportedNotThrown()
	{
		var grid = new InventoryGrid(4, 4);
		var items = StarterKit.Build(new[] { new StarterEntry("brick", 3) }, Make);
		var result = StarterKit.PlaceInGrid(grid, items);
		Assert.Equal(1, result.Placed);
		Assert.Equal(2, result.Dropped.Count);
	}
}

public class SafetyNetTests
{
	private static readonly StarterKitSpec Spec = new(new List<StarterEntry>(), new[]
	{
		new StarterEntry("ak"), new StarterEntry("bandage", 2),
	});

	private static ItemInstance? Make(string id, int count) => id switch
	{
		"ak" => ItemInstance.Create(Items.Rifle),
		"bandage" => ItemInstance.Create(Items.Bandage, count),
		_ => null,
	};

	[Fact]
	public void IssuesWhenNoWeaponAnywhere()
	{
		var profile = new GameProfile();
		var result = SafetyNet.Issue(new CharacterInventory(), profile, Spec, Make);
		Assert.NotNull(result);
		Assert.Empty(result!.Dropped);
		Assert.Contains(profile.Stash.Items, i => i.Spec.Id == "ak");
	}

	[Fact]
	public void NothingIssuedWhenTheStashHoldsAWeapon()
	{
		var profile = new GameProfile();
		profile.Stash.TryPlaceAnywhere(ItemInstance.Create(Items.Pistol));
		Assert.Null(SafetyNet.Issue(new CharacterInventory(), profile, Spec, Make));
		Assert.Single(profile.Stash.Items);
	}

	[Fact]
	public void NothingIssuedWhenTheCharacterCarriesAWeapon()
	{
		var inv = new CharacterInventory();
		inv.TryEquip(ItemInstance.Create(Items.Rifle), EquipSlot.Primary);
		Assert.Null(SafetyNet.Issue(inv, new GameProfile(), Spec, Make));
	}

	[Fact]
	public void AWeaponInsideAContainerCounts()
	{
		var profile = new GameProfile();
		var pack = ItemInstance.Create(Items.Pack);
		pack.Grids[0].TryPlaceAnywhere(ItemInstance.Create(Items.Pistol));
		profile.Stash.TryPlaceAnywhere(pack);
		Assert.False(SafetyNet.NeedsIssue(new CharacterInventory(), profile));
	}

	[Fact]
	public void FullStashDropsWhatDoesNotFitAndNeverBlocks()
	{
		var profile = new GameProfile();
		var filler = Items.Make("filler", GameProfile.StashWidth, 1, cat: ItemCategory.Misc);
		for (var i = 0; i < GameProfile.StashHeight; i++)
			Assert.True(profile.Stash.TryPlaceAnywhere(ItemInstance.Create(filler)));
		var result = SafetyNet.Issue(new CharacterInventory(), profile, Spec, Make);
		Assert.NotNull(result);
		Assert.NotEmpty(result!.Dropped);
	}
}

public class SaveV2Tests
{
	private static ItemSpec? Lookup(string id) => id switch
	{
		"ak" => Items.Rifle,
		"pistol" => Items.Pistol,
		"ammo" => Items.Ammo,
		"secure" => Items.Secure,
		_ => null,
	};

	[Fact]
	public void SideAndStashRoundTrip()
	{
		var run = new RunProgress();
		var profile = new GameProfile { Side = PlayerSide.Soviet };
		var stack = ItemInstance.Create(Items.Ammo, 42);
		profile.Stash.TryPlace(stack, 3, 7);
		profile.Stash.TryPlace(ItemInstance.Create(Items.Rifle), 0, 0);

		var json = SaveSerializer.ToJson(run, profile, null, false);
		var loaded = SaveSerializer.Parse(json, Lookup).Profile;

		Assert.Equal(PlayerSide.Soviet, loaded.Side);
		Assert.Equal(2, loaded.Stash.ItemCount);
		Assert.Equal((3, 7), loaded.Stash.PositionOf(loaded.Stash.Items.First(i => i.Spec.Id == "ammo"))!.Value);
		Assert.Equal(42, loaded.Stash.Items.First(i => i.Spec.Id == "ammo").Count);
	}

	[Fact]
	public void NoSideLoadsAsNull()
	{
		var json = SaveSerializer.ToJson(new RunProgress(), new GameProfile(), null, false);
		var loaded = SaveSerializer.Parse(json, Lookup, new GameProfile { Side = PlayerSide.Allied }).Profile;
		Assert.Null(loaded.Side);
		Assert.False(loaded.HasChosenSide);
	}

	[Fact]
	public void V1SaveMigratesToAnEmptyStashAndNoSide()
	{
		const string v1 = """{"version":1,"raidInProgress":false,"needsKit":true,"counters":{"raids":2,"extractions":1,"deaths":1,"mia":0},"quickUse":[null,null,null,null]}""";
		var profile = SaveSerializer.Parse(v1, Lookup).Profile;
		Assert.Null(profile.Side);
		Assert.Equal(0, profile.Stash.ItemCount);
	}

	[Fact]
	public void SideMapsToFaction()
	{
		Assert.Equal(Faction.AlliedPmc, PlayerSide.Allied.ToFaction());
		Assert.Equal(Faction.SovietPmc, PlayerSide.Soviet.ToFaction());
	}

	[Fact]
	public void UnknownStashItemIsDroppedWithAWarning()
	{
		const string json = """{"version":2,"raidInProgress":false,"counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[],"side":"Allied","stash":{"width":10,"height":30,"items":[{"x":0,"y":0,"item":{"id":"ghost"}},{"x":2,"y":0,"item":{"id":"pistol"}}]}}""";
		var (_, _, profile, warnings) = SaveSerializer.Parse(json, Lookup);
		Assert.Single(profile.Stash.Items);
		Assert.Contains(warnings, w => w.Contains("ghost"));
	}
}

public class SaveV3Tests
{
	[Fact]
	public void TraderProgressRoundTripsAndIsClamped()
	{
		var profile = new GameProfile();
		profile.Trader("medic").Level = 2;
		profile.Trader("medic").Spent = 1234;
		var json = SaveSerializer.ToJson(new RunProgress(), profile, null, false);
		var loaded = SaveSerializer.Parse(json, _ => null).Profile;
		Assert.Equal(2, loaded.Trader("medic").Level);
		Assert.Equal(1234, loaded.Trader("medic").Spent);
		Assert.Equal(1, loaded.Trader("smuggler").Level);
	}

	[Fact]
	public void V2SaveMigratesWithAnEmptyTraderMap()
	{
		const string v2 = """{"version":2,"raidInProgress":false,"counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[],"side":"Allied","stash":{"width":10,"height":30,"items":[]}}""";
		Assert.Empty(SaveSerializer.Parse(v2, _ => null).Profile.Traders);
	}

	[Fact]
	public void OutOfRangeTraderLevelIsClamped()
	{
		const string json = """{"version":3,"raidInProgress":false,"counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[],"traders":{"medic":{"level":99,"spent":-5}}}""";
		var profile = SaveSerializer.Parse(json, _ => null).Profile;
		Assert.Equal(TraderSpec.MaxLevel, profile.Trader("medic").Level);
		Assert.Equal(0, profile.Trader("medic").Spent);
	}
}

public class CorpseLootTests
{
	private static readonly ItemSpec Rifle = new("ak", "ak", ItemCategory.Weapon, 5, 2, 3.6f, WeaponClass: WeaponClass.Primary);
	private static readonly ItemSpec Mag = new("mag", "mag", ItemCategory.Magazine, 1, 2, 0.2f, MagazineCapacity: 30);
	private static readonly ItemSpec Vest = new("vest", "vest", ItemCategory.Armor, 3, 3, 4f);
	private static readonly ItemSpec Rig = new("rig", "rig", ItemCategory.Rig, 3, 2, 1f, Grids: new[] { new GridSize(4, 2) });
	private static readonly ItemSpec Bandage = new("bandage", "bandage", ItemCategory.Medical, 1, 1, 0.1f, 5);

	private static CharacterInventory Body(out ItemInstance rigItem)
	{
		var inv = new CharacterInventory();
		var rifle = ItemInstance.Create(Rifle);
		rifle.InsertedMagazine = ItemInstance.Create(Mag);
		inv.TryEquip(rifle, EquipSlot.Primary);
		inv.TryEquip(ItemInstance.Create(Vest), EquipSlot.Vest);
		rigItem = ItemInstance.Create(Rig);
		inv.TryEquip(rigItem, EquipSlot.Rig);
		rigItem.Grids[0].TryPlaceAnywhere(ItemInstance.Create(Mag));
		inv.TryAutoStore(ItemInstance.Create(Bandage, 2)); // pocket
		return inv;
	}

	[Fact]
	public void WornGearIsVisibleAtOnceButGridContentsAreHidden()
	{
		var body = Body(out var rig);
		var model = CorpseLoot.Build(body, 1f, 6f);
		Assert.Equal(2, model.HiddenCount); // the spare magazine in the rig and the bandages in a pocket
		Assert.True(model.IsRevealed(body.InSlot(EquipSlot.Primary)!));
		Assert.True(model.IsRevealed(body.InSlot(EquipSlot.Vest)!));
		Assert.True(model.IsRevealed(rig));
		Assert.False(model.IsRevealed(rig.Grids[0].Items.First()));
	}

	[Fact]
	public void TheInsertedMagazineStaysWithTheWeapon()
	{
		var body = Body(out _);
		var inserted = body.InSlot(EquipSlot.Primary)!.InsertedMagazine!;
		var model = CorpseLoot.Build(body, 1f, 6f);
		Assert.True(model.IsRevealed(inserted));
	}

	[Fact]
	public void SearchingRevealsOneItemPerInterval()
	{
		var body = Body(out _);
		var model = CorpseLoot.Build(body, 1f, 6f);
		model.StartSearch();
		model.Update(1.01f);
		Assert.Equal(1, model.HiddenCount);
		model.Update(1.01f);
		Assert.True(model.FullyRevealed);
	}

	[Fact]
	public void EmptyBodyIsFullyRevealed()
		=> Assert.True(CorpseLoot.Build(new CharacterInventory(), 1f, 6f).FullyRevealed);
}

public class ExtractionCountdownTests
{
	[Fact]
	public void RemainingTimeCountsDownAndResetsOnLeaving()
	{
		var s = new RaidSession(100f, new[] { new ExtractionSpec("gate", ExtractKind.AlwaysOpen, 6f) });
		for (var i = 0; i < 20; i++)
			s.Update(0.1f, "gate", _ => false);
		Assert.InRange(s.ExtractRemainingS, 3.8f, 4.2f);
		s.Update(0.1f, null, _ => false);
		Assert.Equal(0f, s.ExtractRemainingS);
	}

	[Fact]
	public void BlockedZoneReportsWhatItNeeds()
	{
		var spec = new ExtractionSpec("drain", ExtractKind.RequiresItem, 5f, "bandage");
		var s = new RaidSession(100f, new[] { spec });
		s.Update(0.1f, "drain", _ => false);
		Assert.Equal(ExtractBlock.MissingItem, s.Blocked);
		Assert.Same(spec, s.BlockedSpec);
		s.Update(0.1f, null, _ => false);
		Assert.Null(s.BlockedSpec);
	}
}
