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

		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(run, profile, null, false));
		var loaded = new GameProfile();
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, Lookup, new List<string>());

		Assert.Equal(PlayerSide.Soviet, loaded.Side);
		Assert.Equal(2, loaded.Stash.ItemCount);
		Assert.Equal((3, 7), loaded.Stash.PositionOf(loaded.Stash.Items.First(i => i.Spec.Id == "ammo"))!.Value);
		Assert.Equal(42, loaded.Stash.Items.First(i => i.Spec.Id == "ammo").Count);
	}

	[Fact]
	public void NoSideLoadsAsNull()
	{
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), new GameProfile(), null, false));
		var loaded = new GameProfile { Side = PlayerSide.Allied };
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, Lookup, new List<string>());
		Assert.Null(loaded.Side);
		Assert.False(loaded.HasChosenSide);
	}

	[Fact]
	public void V1SaveMigratesToAnEmptyStashAndNoSide()
	{
		const string v1 = """{"version":1,"raidInProgress":false,"needsKit":true,"counters":{"raids":2,"extractions":1,"deaths":1,"mia":0},"quickUse":[null,null,null,null]}""";
		var profile = new GameProfile();
		SaveSerializer.Apply(SaveSerializer.Parse(v1), new RunProgress(), profile, Lookup, new List<string>());
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
		var profile = new GameProfile();
		var warnings = new List<string>();
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), profile, Lookup, warnings);
		Assert.Single(profile.Stash.Items);
		Assert.Contains(warnings, w => w.Contains("ghost"));
	}
}
