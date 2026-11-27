using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public static class ModItems
{
	private static WeaponSlotSpec Slot(string id, string type, string[] accepts, bool required = false, bool accessory = false,
		string marker = "") => new(id, type, accepts, required, accessory, marker);

	public static readonly ItemSpec Rifle = new("rifle", "rifle", ItemCategory.Weapon, 5, 2, 3.0f,
		WeaponClass: WeaponClass.Primary, BaseValue: 1000,
		Build: new WeaponBuildSpec(new[]
		{
			Slot("stock", "stock", new[] { "rifle.stock" }, required: true),
			Slot("handguard", "handguard", new[] { "rifle.handguard" }, required: true),
			Slot("muzzle", "muzzle", new[] { "rifle.muzzle" }, accessory: true, marker: "Muzzle"),
			Slot("dovetail", "mount", new[] { "rifle.rail" }, accessory: true, marker: "OpticMount"),
		}, new Dictionary<string, string> { ["stock"] = "stock_wood", ["handguard"] = "hg_wood" }, 45f));

	public static readonly ItemSpec StockWood = new("stock_wood", "stock_wood", ItemCategory.WeaponPart, 2, 1, 0f,
		Part: new PartSpec(new[] { "rifle.stock" }, new List<WeaponSlotSpec>(), new List<string>()));

	// the default handguard adds a clamp slot and an underbarrel slot
	public static readonly ItemSpec HandguardWood = new("hg_wood", "hg_wood", ItemCategory.WeaponPart, 2, 1, 0f,
		Part: new PartSpec(new[] { "rifle.handguard" }, new[]
		{
			Slot("underbarrel", "underbarrel", new[] { "hg.under" }, accessory: true, marker: "BipodMount"),
		}, new List<string>()));

	public static readonly ItemSpec HandguardTactical = new("hg_tac", "hg_tac", ItemCategory.WeaponPart, 2, 1, 0.3f, BaseValue: 3000,
		Part: new PartSpec(new[] { "rifle.handguard" }, new List<WeaponSlotSpec>(), new[] { "muzzle" }, ErgonomicsPts: 3f));

	public static readonly ItemSpec Compensator = new("comp", "comp", ItemCategory.WeaponPart, 1, 1, 0.05f, BaseValue: 1500,
		Part: new PartSpec(new[] { "rifle.muzzle" }, new List<WeaponSlotSpec>(), new List<string>(), VerticalRecoilPts: 8f, NoisePct: 0.1f));

	public static readonly ItemSpec Brake = new("brake", "brake", ItemCategory.WeaponPart, 1, 1, 0.1f, BaseValue: 3000,
		Part: new PartSpec(new[] { "rifle.muzzle" }, new List<WeaponSlotSpec>(), new List<string>(), VerticalRecoilPts: 10f));

	public static readonly ItemSpec Mount = new("mount", "mount", ItemCategory.WeaponPart, 1, 1, 0.1f, BaseValue: 2000,
		Part: new PartSpec(new[] { "rifle.rail" }, new[]
		{
			Slot("optic", "optic", new[] { "optic.std" }, accessory: true, marker: "OpticMount"),
		}, new List<string>()));

	public static readonly ItemSpec Scope = new("scope", "scope", ItemCategory.WeaponPart, 2, 1, 0.6f, BaseValue: 14000,
		Part: new PartSpec(new[] { "rifle.rail", "optic.std" }, new List<WeaponSlotSpec>(), new List<string>(),
			ErgonomicsPts: -5f, Zoom: 4f));

	public static readonly ItemSpec Bipod = new("bipod", "bipod", ItemCategory.WeaponPart, 2, 1, 0.5f, BaseValue: 4000,
		Part: new PartSpec(new[] { "hg.under" }, new List<WeaponSlotSpec>(), new List<string>(), Bipod: true));

	public static readonly ItemSpec LongBarrel = new("longbarrel", "longbarrel", ItemCategory.WeaponPart, 3, 1, 1.2f, BaseValue: 15000,
		Part: new PartSpec(new[] { "rifle.muzzle" }, new List<WeaponSlotSpec>(), new List<string>(),
			VelocityPct: 0.05f, HipStabilityPts: 15f, GridWidthAdd: 1));

	private static readonly Dictionary<string, ItemSpec> All = new[]
	{
		StockWood, HandguardWood, HandguardTactical, Compensator, Brake, Mount, Scope, Bipod, LongBarrel,
	}.ToDictionary(s => s.Id);

	public static ItemInstance? Part(string id) => All.TryGetValue(id, out var s) ? ItemInstance.Create(s) : null;

	public static ItemInstance NewRifle(bool defaults = true)
	{
		var rifle = ItemInstance.Create(Rifle);
		if (defaults)
			WeaponBuild.ApplyDefaults(rifle, Part);
		return rifle;
	}
}

public class WeaponBuildTests
{
	[Fact]
	public void FitsByInterfaceAndRefusesTheWrongOne()
	{
		var rifle = ModItems.NewRifle();
		Assert.Equal(AttachFailure.None, WeaponBuild.CanAttach(rifle, "muzzle", ModItems.Part("comp")!));
		Assert.Equal(AttachFailure.WrongInterface, WeaponBuild.CanAttach(rifle, "muzzle", ModItems.Part("mount")!));
		Assert.Equal(AttachFailure.NoSuchSlot, WeaponBuild.CanAttach(rifle, "nope", ModItems.Part("comp")!));
		Assert.Equal(AttachFailure.NotAPart, WeaponBuild.CanAttach(rifle, "muzzle", ItemInstance.Create(ModItems.Rifle)));
	}

	[Fact]
	public void SameThreadFitsAcrossParts()
	{
		var rifle = ModItems.NewRifle();
		Assert.True(WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("brake")!));
		Assert.NotNull(WeaponBuild.Detach(rifle, "muzzle"));
		Assert.True(WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("comp")!)); // shared interface tag
	}

	[Fact]
	public void TakenSlotRefusesUntilDetached()
	{
		var rifle = ModItems.NewRifle();
		Assert.True(WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("comp")!));
		Assert.Equal(AttachFailure.SlotTaken, WeaponBuild.CanAttach(rifle, "muzzle", ModItems.Part("brake")!));
	}

	[Fact]
	public void BlockedSlotCannotBeUsed()
	{
		var rifle = ModItems.NewRifle(defaults: false);
		WeaponBuild.Attach(rifle, "stock", ModItems.Part("stock_wood")!);
		Assert.True(WeaponBuild.Attach(rifle, "handguard", ModItems.Part("hg_tac")!)); // blocks the muzzle slot
		Assert.Equal(AttachFailure.SlotBlocked, WeaponBuild.CanAttach(rifle, "muzzle", ModItems.Part("comp")!));
	}

	[Fact]
	public void DetachTakesTheChildrenAlong()
	{
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "underbarrel", ModItems.Part("bipod")!);
		var handguard = WeaponBuild.Detach(rifle, "handguard")!;
		Assert.Equal("hg_wood", handguard.Spec.Id);
		Assert.Equal("bipod", handguard.Parts["underbarrel"].Spec.Id);
		Assert.Null(WeaponBuild.Find(rifle, "underbarrel")); // the slot went with its handguard
	}

	[Fact]
	public void MountAddsAnOpticSlotThatNeedsTheMount()
	{
		var rifle = ModItems.NewRifle();
		Assert.Null(WeaponBuild.Find(rifle, "optic"));
		WeaponBuild.Attach(rifle, "dovetail", ModItems.Part("mount")!);
		Assert.Equal(AttachFailure.None, WeaponBuild.CanAttach(rifle, "optic", ModItems.Part("scope")!));
		Assert.True(WeaponBuild.Attach(rifle, "optic", ModItems.Part("scope")!));
		Assert.Equal(4f, WeaponBuild.Stats(rifle).Zoom);
	}

	[Fact]
	public void ScopeAlsoFitsTheRailDirectly()
		=> Assert.True(WeaponBuild.Attach(ModItems.NewRifle(), "dovetail", ModItems.Part("scope")!));

	// --- completeness ----------------------------------------------------------------------

	[Fact]
	public void RequiredSlotsMustBeFilled()
	{
		var rifle = ModItems.NewRifle(defaults: false);
		Assert.False(WeaponBuild.IsComplete(rifle));
		Assert.Equal(new[] { "stock", "handguard" }, WeaponBuild.Missing(rifle).Select(s => s.Id).ToArray());
		WeaponBuild.ApplyDefaults(rifle, ModItems.Part);
		Assert.True(WeaponBuild.IsComplete(rifle));
	}

	[Fact]
	public void RequiredSlotAddedByAnAttachedPartCounts()
	{
		var needy = new ItemSpec("needy", "needy", ItemCategory.WeaponPart, 1, 1, 0f,
			Part: new PartSpec(new[] { "rifle.muzzle" }, new[]
			{
				new WeaponSlotSpec("core", "barrel", new[] { "core.tag" }, Required: true),
			}, new List<string>()));
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "muzzle", ItemInstance.Create(needy));
		Assert.False(WeaponBuild.IsComplete(rifle));
		Assert.Equal("core", WeaponBuild.Missing(rifle).Single().Id);
	}

	[Fact]
	public void IncompleteWeaponCannotBeEquippedButCompleteOneCan()
	{
		var inv = new CharacterInventory();
		Assert.False(inv.TryEquip(ModItems.NewRifle(defaults: false), EquipSlot.Primary));
		Assert.True(inv.TryEquip(ModItems.NewRifle(), EquipSlot.Primary));
	}

	// --- stats -----------------------------------------------------------------------------

	[Fact]
	public void PercentModifiersAddAndPointsAddAndErgonomicsClamps()
	{
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("comp")!);
		var stats = WeaponBuild.Stats(rifle);
		Assert.Equal(58f, stats.VerticalRecoil, 3);
		Assert.Equal(50f, stats.HorizontalRecoil, 3);
		Assert.Equal(1.1f, stats.NoiseMult, 3);
		Assert.Equal(45f, stats.Ergonomics, 3);

		var heavy = ItemInstance.Create(ModItems.Rifle with { Build = ModItems.Rifle.Build! with { Ergonomics = 98f } });
		WeaponBuild.ApplyDefaults(heavy, ModItems.Part);
		WeaponBuild.Attach(heavy, "handguard", ModItems.Part("hg_tac")!); // detach check: slot is taken
		Assert.Equal(98f, WeaponBuild.Stats(heavy).Ergonomics, 3);
		WeaponBuild.Detach(heavy, "handguard");
		WeaponBuild.Attach(heavy, "handguard", ModItems.Part("hg_tac")!);
		Assert.Equal(100f, WeaponBuild.Stats(heavy).Ergonomics, 3); // 98 + 3 clamped
	}

	[Fact]
	public void PercentModifiersOfSeveralPartsAddUp()
	{
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("longbarrel")!);
		WeaponBuild.Attach(rifle, "dovetail", ModItems.Part("mount")!);
		WeaponBuild.Attach(rifle, "optic", ModItems.Part("scope")!);
		var s = WeaponBuild.Stats(rifle);
		Assert.Equal(1.05f, s.VelocityMult, 3);
		Assert.Equal(65f, s.HipStability, 3);
		Assert.Equal(40f, s.Ergonomics, 3);
	}

	[Fact]
	public void WeightValueAndGridWidthIncludeParts()
	{
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("longbarrel")!);
		WeaponBuild.Attach(rifle, "underbarrel", ModItems.Part("bipod")!);
		var s = WeaponBuild.Stats(rifle);
		Assert.Equal(3.0f + 1.2f + 0.5f, s.WeightKg, 3);
		Assert.Equal(1000 + 15000 + 4000, s.Value);
		Assert.Equal(6, s.GridWidth);
		Assert.Equal(s.WeightKg, rifle.WeightKg, 3);
		Assert.True(s.HasBipod);
	}

	[Fact]
	public void PreviewShowsTheDeltaWithoutChangingTheBuild()
	{
		var rifle = ModItems.NewRifle();
		var (before, after) = WeaponBuild.Preview(rifle, "muzzle", ModItems.Part("brake")!)!.Value;
		Assert.Equal(50f, before.VerticalRecoil, 3);
		Assert.Equal(60f, after.VerticalRecoil, 3);
		Assert.DoesNotContain("muzzle", rifle.Parts.Keys);
		Assert.Null(WeaponBuild.Preview(rifle, "muzzle", ModItems.Part("mount")!));
	}

	[Fact]
	public void PreviewOfAReplacementRestoresTheOriginalPart()
	{
		var rifle = ModItems.NewRifle();
		var comp = ModItems.Part("comp")!;
		WeaponBuild.Attach(rifle, "muzzle", comp);
		var preview = WeaponBuild.Preview(rifle, "muzzle", ModItems.Part("brake")!)!.Value;
		Assert.Equal(58f, preview.Before.VerticalRecoil, 3);
		Assert.Equal(60f, preview.After.VerticalRecoil, 3);
		Assert.Same(comp, rifle.Parts["muzzle"]);
	}

	[Fact]
	public void ErgonomicsCurveIsNeutralAtFifty()
	{
		var t = ErgonomicsTuning.Default;
		Assert.Equal(1f, t.AdsTimeMult(50f), 3);
		Assert.True(t.AdsTimeMult(20f) > 1f && t.AdsTimeMult(80f) < 1f);
		Assert.True(t.SwayMult(20f) > t.SwayMult(80f));
		Assert.True(t.StaminaDrainMult(0f) > t.StaminaDrainMult(100f));
	}

	[Fact]
	public void DefaultsFillRequiredSlotsAndSkipBlockedOnes()
	{
		var rifle = ModItems.NewRifle();
		Assert.Equal("stock_wood", rifle.Parts["stock"].Spec.Id);
		Assert.Equal("hg_wood", rifle.Parts["handguard"].Spec.Id);
	}

	[Fact]
	public void CopyPartsDuplicatesTheWholeTree()
	{
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "underbarrel", ModItems.Part("bipod")!);
		var copy = ItemInstance.Create(ModItems.Rifle);
		WeaponBuild.CopyParts(rifle, copy);
		Assert.NotSame(rifle.Parts["handguard"], copy.Parts["handguard"]);
		Assert.Equal("bipod", copy.Parts["handguard"].Parts["underbarrel"].Spec.Id);
	}
}

public class WeaponModdingIntegrationTests
{
	private static readonly ItemSpec Money = new("banknotes", "banknotes", ItemCategory.Misc, 1, 1, 0f, 50000, BaseValue: 1, Insurable: false);

	private static ItemSpec? Lookup(string id) => id switch
	{
		"rifle" => ModItems.Rifle,
		"stock_wood" => ModItems.StockWood,
		"hg_wood" => ModItems.HandguardWood,
		"hg_tac" => ModItems.HandguardTactical,
		"scope" => ModItems.Scope,
		"mount" => ModItems.Mount,
		"banknotes" => Money,
		_ => null,
	};

	[Fact]
	public void SellingAWeaponPaysForItsParts()
	{
		var profile = new GameProfile();
		var character = new CharacterInventory();
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("comp")!);
		profile.Stash.TryPlaceAnywhere(rifle);
		var level = new TraderLevelSpec(new List<ListingSpec>(), new HashSet<ItemCategory> { ItemCategory.Weapon }, 1f, 0.5f, new List<BarterSpec>());
		var engine = new TradeEngine(profile, character, id => id == "banknotes" ? Money : null,
			(id, n) => id == "banknotes" ? ItemInstance.Create(Money, n) : null, _ => 0);
		Assert.Equal(1000 + 1500, engine.WorthOf(rifle));
		var result = engine.Sell(level, new TraderState(), rifle);
		Assert.Equal(1250, result.Money);
		Assert.DoesNotContain(profile.Stash.Items, i => i.Spec.Id == "rifle");
	}

	[Fact]
	public void InsuringAWeaponCoversItsPartsAndTheReturnKeepsTheBuildButNotTheMagazine()
	{
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("comp")!);
		rifle.InsertedMagazine = ItemInstance.Create(new ItemSpec("mag", "mag", ItemCategory.Magazine, 1, 2, 0.2f, MagazineCapacity: 30));
		Assert.Equal(250, InsuranceService.Premium(new InsuranceTerms(0.1f, 0.5f), rifle)); // 10% of 1000 + 1500
		var copy = InsuranceService.StrippedCopy(rifle);
		Assert.Null(copy.InsertedMagazine);
		Assert.Equal("comp", copy.Parts["muzzle"].Spec.Id);
		Assert.False(copy.Insured);
		Assert.True(WeaponBuild.IsComplete(copy));
	}

	[Fact]
	public void PartsAreNotInsuredSeparately()
	{
		var character = new CharacterInventory();
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("comp")!);
		character.TryEquip(rifle, EquipSlot.Primary);
		var ids = InsuranceService.InsurableItems(character).Select(i => i.Spec.Id).ToList();
		Assert.Contains("rifle", ids);
		Assert.DoesNotContain("comp", ids);
	}

	[Fact]
	public void NestedPartsSurviveTheSave()
	{
		var rifle = ModItems.NewRifle();
		WeaponBuild.Attach(rifle, "dovetail", ModItems.Part("mount")!);
		WeaponBuild.Attach(rifle, "optic", ModItems.Part("scope")!);
		var inv = new CharacterInventory();
		inv.TryEquip(rifle, EquipSlot.Primary);
		var run = new RunProgress();
		var json = SaveSerializer.ToJson(run, new GameProfile(), inv, false);
		var loaded = SaveSerializer.Parse(json, Lookup).Run;
		var back = loaded.Inventory!.InSlot(EquipSlot.Primary)!;
		Assert.Equal("scope", back.Parts["dovetail"].Parts["optic"].Spec.Id);
		Assert.Equal(4f, WeaponBuild.Stats(back).Zoom);
		Assert.Equal(rifle.WeightKg, back.WeightKg, 3);
	}

	[Fact]
	public void PartThatNoLongerFitsIsDetachedIntoTheStashWithAWarning()
	{
		// Save a scope in the stock slot (as if the spec had changed): it cannot attach on load.
		const string json = """
			{"version":6,"raidInProgress":false,"counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[],
			 "stash":{"width":10,"height":30,"items":[{"x":0,"y":0,"item":{"id":"rifle","parts":{"stock":{"id":"scope"}}}}]}}
			""";
		var (_, _, profile, warnings) = SaveSerializer.Parse(json, Lookup);
		var rifle = profile.Stash.Items.First(i => i.Spec.Id == "rifle");
		Assert.Contains(warnings, w => w.Contains("no longer fits"));
		Assert.Contains(profile.Stash.Items, i => i.Spec.Id == "scope");
		// the missing required stock and handguard were refilled with the defaults
		Assert.True(WeaponBuild.IsComplete(rifle));
	}

	[Fact]
	public void OldSavesWithoutPartsGetDefaultPartsSoWeaponsStayUsable()
	{
		const string json = """
			{"version":5,"raidInProgress":false,"counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[],
			 "character":{"slots":{"Primary":{"id":"rifle"}},"pockets":[]}}
			""";
		var run = SaveSerializer.Parse(json, Lookup).Run;
		Assert.NotNull(run.Inventory!.InSlot(EquipSlot.Primary));
	}
}

public class GunsmithTests
{
	private static (GameProfile Profile, CharacterInventory Character, ItemInstance Rifle) Setup(bool equip = false)
	{
		var profile = new GameProfile();
		var character = new CharacterInventory();
		var rifle = ModItems.NewRifle();
		if (equip)
			character.TryEquip(rifle, EquipSlot.Primary);
		else
			profile.Stash.TryPlaceAnywhere(rifle);
		return (profile, character, rifle);
	}

	[Fact]
	public void AttachMovesThePartOutOfTheStashOntoTheWeapon()
	{
		var (profile, character, rifle) = Setup();
		var comp = ModItems.Part("comp")!;
		profile.Stash.TryPlaceAnywhere(comp);
		Assert.Equal(GunsmithResult.Ok, Gunsmith.Attach(profile, character, rifle, "muzzle", comp));
		Assert.Same(comp, rifle.Parts["muzzle"]);
		Assert.DoesNotContain(profile.Stash.Items, i => i == comp);
	}

	[Fact]
	public void PartsFromCarriedGridsCanBeUsedToo()
	{
		var (profile, character, rifle) = Setup();
		var pack = ItemInstance.Create(new ItemSpec("pack", "pack", ItemCategory.Backpack, 4, 4, 1f, Grids: new[] { new GridSize(5, 4) }));
		character.TryEquip(pack, EquipSlot.Backpack);
		var brake = ModItems.Part("brake")!;
		pack.Grids[0].TryPlaceAnywhere(brake);
		Assert.Single(Gunsmith.PartPool(profile, character));
		Assert.Equal(GunsmithResult.Ok, Gunsmith.Attach(profile, character, rifle, "muzzle", brake));
		Assert.Empty(pack.Grids[0].Items);
	}

	[Fact]
	public void WrongPartOrUnownedPartIsRefused()
	{
		var (profile, character, rifle) = Setup();
		var mount = ModItems.Part("mount")!;
		profile.Stash.TryPlaceAnywhere(mount);
		Assert.Equal(GunsmithResult.CannotFit, Gunsmith.Attach(profile, character, rifle, "muzzle", mount));
		Assert.Equal(GunsmithResult.PartNotOwned, Gunsmith.Attach(profile, character, rifle, "muzzle", ModItems.Part("comp")!));
		Assert.Contains(profile.Stash.Items, i => i == mount); // unchanged
	}

	[Fact]
	public void DetachPutsThePartAndItsChildrenInTheStash()
	{
		var (profile, character, rifle) = Setup();
		WeaponBuild.Attach(rifle, "underbarrel", ModItems.Part("bipod")!);
		Assert.Equal(GunsmithResult.Ok, Gunsmith.Detach(profile, character, rifle, "handguard"));
		var handguard = profile.Stash.Items.Single(i => i.Spec.Id == "hg_wood");
		Assert.Equal("bipod", handguard.Parts["underbarrel"].Spec.Id);
	}

	[Fact]
	public void DetachingAStructuralPartOffAnEquippedWeaponUnequipsIt()
	{
		var (profile, character, rifle) = Setup(equip: true);
		Assert.Equal(GunsmithResult.Ok, Gunsmith.Detach(profile, character, rifle, "stock"));
		Assert.Null(character.InSlot(EquipSlot.Primary));
		Assert.Contains(profile.Stash.Items, i => i == rifle);
		Assert.Equal("stock", Gunsmith.MissingText(rifle));
	}

	[Fact]
	public void DetachingAnAccessoryKeepsTheWeaponEquipped()
	{
		var (profile, character, rifle) = Setup(equip: true);
		WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("comp")!);
		Assert.Equal(GunsmithResult.Ok, Gunsmith.Detach(profile, character, rifle, "muzzle"));
		Assert.Same(rifle, character.InSlot(EquipSlot.Primary));
	}

	[Fact]
	public void FullStashBlocksDetachAndChangesNothing()
	{
		var (profile, character, rifle) = Setup(equip: true);
		var filler = new ItemSpec("filler", "filler", ItemCategory.Misc, GameProfile.StashWidth, 1, 0.1f);
		for (var i = 0; i < GameProfile.StashHeight; i++)
			profile.Stash.TryPlaceAnywhere(ItemInstance.Create(filler));
		WeaponBuild.Attach(rifle, "muzzle", ModItems.Part("comp")!);
		Assert.Equal(GunsmithResult.StashFull, Gunsmith.Detach(profile, character, rifle, "muzzle"));
		Assert.Equal("comp", rifle.Parts["muzzle"].Spec.Id);
		Assert.Same(rifle, character.InSlot(EquipSlot.Primary));
	}

	[Fact]
	public void WeaponsListCoversEquippedCarriedAndStashed()
	{
		var (profile, character, stashed) = Setup();
		var equipped = ModItems.NewRifle();
		character.TryEquip(equipped, EquipSlot.Primary);
		var weapons = Gunsmith.Weapons(profile, character).ToList();
		Assert.Contains(stashed, weapons);
		Assert.Contains(equipped, weapons);
	}
}

public class WeaponClearanceTests
{
	private static WeaponSlotSpec Slot(string id, string type, string accepts, string rail = "", int start = 0, bool required = false)
		=> new(id, type, new[] { accepts }, required, true, "", rail, start);

	private static ItemSpec Receiver(params WeaponSlotSpec[] slots) => new("recv", "recv", ItemCategory.Weapon, 5, 2, 3f,
		WeaponClass: WeaponClass.Primary,
		Build: new WeaponBuildSpec(slots, new Dictionary<string, string>(), 50f, false, null,
			new Dictionary<string, int> { ["top"] = 20 }));

	private static ItemInstance Part(string id, string iface, int steps = 0, string[]? zones = null)
		=> ItemInstance.Create(new ItemSpec(id, id, ItemCategory.WeaponPart, 1, 1, 0.1f,
			Part: new PartSpec(new[] { iface }, new List<WeaponSlotSpec>(), new List<string>(), Steps: steps, Zones: zones)));

	[Fact]
	public void PartsOnTheSameRailMayNotOverlap()
	{
		var gun = ItemInstance.Create(Receiver(Slot("front", "mount", "m", "top", 0), Slot("mid", "mount", "m", "top", 4), Slot("rear", "mount", "m", "top", 10)));
		Assert.True(WeaponBuild.Attach(gun, "front", Part("a", "m", steps: 5)));
		Assert.Equal(AttachFailure.RailOverlap, WeaponBuild.CanAttach(gun, "mid", Part("b", "m", steps: 3)));
		Assert.Equal(AttachFailure.None, WeaponBuild.CanAttach(gun, "rear", Part("c", "m", steps: 3)));
	}

	[Fact]
	public void PartsMustFitTheRailLength()
	{
		var gun = ItemInstance.Create(Receiver(Slot("rear", "mount", "m", "top", 15)));
		Assert.Equal(AttachFailure.RailOverflow, WeaponBuild.CanAttach(gun, "rear", Part("long", "m", steps: 8)));
		Assert.Equal(AttachFailure.None, WeaponBuild.CanAttach(gun, "rear", Part("short", "m", steps: 5)));
	}

	[Fact]
	public void TwoPartsInOneZoneConflictUntilALowProfilePartReplacesOne()
	{
		var gun = ItemInstance.Create(Receiver(Slot("sight", "sight", "s"), Slot("optic", "optic", "o")));
		Assert.True(WeaponBuild.Attach(gun, "sight", Part("tall_rear", "s", zones: new[] { "sight_line" })));
		Assert.Equal(AttachFailure.ZoneConflict, WeaponBuild.CanAttach(gun, "optic", Part("scope", "o", zones: new[] { "sight_line" })));
		WeaponBuild.Detach(gun, "sight");
		Assert.True(WeaponBuild.Attach(gun, "sight", Part("flip_rear", "s")));
		Assert.Equal(AttachFailure.None, WeaponBuild.CanAttach(gun, "optic", Part("scope", "o", zones: new[] { "sight_line" })));
	}

	[Fact]
	public void AHandguardProvidesRailsForItsOwnMounts()
	{
		var handguard = ItemInstance.Create(new ItemSpec("hg", "hg", ItemCategory.WeaponPart, 1, 1, 0.1f,
			Part: new PartSpec(new[] { "hg" }, new[] { Slot("hg_mount", "mount", "m", "hg_top", 0) }, new List<string>(),
				Rails: new Dictionary<string, int> { ["hg_top"] = 6 })));
		var gun = ItemInstance.Create(Receiver(Slot("handguard", "handguard", "hg")));
		Assert.True(WeaponBuild.Attach(gun, "handguard", handguard));
		Assert.Equal(AttachFailure.RailOverflow, WeaponBuild.CanAttach(gun, "hg_mount", Part("big", "m", steps: 8)));
		Assert.Equal(AttachFailure.None, WeaponBuild.CanAttach(gun, "hg_mount", Part("ok", "m", steps: 6)));
	}

	[Fact]
	public void MissingVitalPartRedFlagsTheWeaponButStructuralDoesNot()
	{
		var gun = ItemInstance.Create(Receiver(Slot("bolt", "bolt", "b", required: true), Slot("stock", "stock", "s")));
		Assert.False(WeaponBuild.IsComplete(gun));
		WeaponBuild.Attach(gun, "bolt", Part("bolt", "b"));
		Assert.True(WeaponBuild.IsComplete(gun));
	}

	[Fact]
	public void RecoilSkillPointsRaiseBothRecoilRatings()
	{
		var gun = ItemInstance.Create(Receiver());
		var s = WeaponBuild.Stats(gun, recoilSkillPts: 10f);
		Assert.Equal(60f, s.VerticalRecoil, 3);
		Assert.Equal(60f, s.HorizontalRecoil, 3);
		Assert.Equal(50f, s.Stability, 3);
	}
}

public class WeaponWorkshopTests
{
	private static ItemSpec? Lookup(string id) => ModItems.Part(id)?.Spec;

	private static (GameProfile Profile, CharacterInventory Character, ItemInstance Rifle) Setup()
	{
		var profile = new GameProfile();
		var character = new CharacterInventory();
		var rifle = ModItems.NewRifle();
		profile.Stash.TryPlaceAnywhere(rifle);
		return (profile, character, rifle);
	}

	[Fact]
	public void SandboxEditsDoNotTouchTheRealWeapon()
	{
		var (_, _, rifle) = Setup();
		var sandbox = WeaponWorkshop.Copy(rifle);
		WeaponBuild.Attach(sandbox, "muzzle", ModItems.Part("brake")!);
		Assert.False(rifle.Parts.ContainsKey("muzzle"));
		Assert.Equal(60f, WeaponBuild.Stats(sandbox).VerticalRecoil, 3);
	}

	[Fact]
	public void OnlyOwnedOrExaminedPartsAreOffered()
	{
		var (profile, character, rifle) = Setup();
		profile.Stash.TryPlaceAnywhere(ModItems.Part("comp")!);
		var ids = WeaponWorkshop.ExaminedIds(profile, character).ToList();
		Assert.Contains("comp", ids);
		Assert.DoesNotContain("brake", ids);
		profile.ExaminedParts.Add("brake");
		Assert.Contains("brake", WeaponWorkshop.ExaminedIds(profile, character));
		Assert.Contains("stock_wood", ids); // default part attached to an owned weapon
	}

	[Fact]
	public void PresetCapturesNestedPartsAndListsWhatIsMissing()
	{
		var (profile, character, rifle) = Setup();
		var sandbox = WeaponWorkshop.Copy(rifle);
		WeaponBuild.Attach(sandbox, "dovetail", ModItems.Part("mount")!);
		WeaponBuild.Attach(sandbox, "optic", ModItems.Part("scope")!);
		WeaponBuild.Attach(sandbox, "muzzle", ModItems.Part("brake")!);
		var preset = WeaponWorkshop.Capture("sniper", sandbox);
		Assert.Equal("scope", preset.Parts["optic"]);
		var missing = WeaponWorkshop.Missing(preset, profile, character, rifle);
		Assert.Equal(3, missing.Count);
		profile.Stash.TryPlaceAnywhere(ModItems.Part("brake")!);
		Assert.Equal(2, WeaponWorkshop.Missing(preset, profile, character, rifle).Count);
	}

	[Fact]
	public void PresetBuildsAVirtualWeaponWithoutOwnership()
	{
		var (_, _, rifle) = Setup();
		var sandbox = WeaponWorkshop.Copy(rifle);
		WeaponBuild.Attach(sandbox, "dovetail", ModItems.Part("mount")!);
		WeaponBuild.Attach(sandbox, "optic", ModItems.Part("scope")!);
		var preset = WeaponWorkshop.Capture("p", sandbox);
		var built = WeaponWorkshop.Build(preset, ModItems.Rifle, Lookup);
		Assert.Equal("scope", built.Parts["dovetail"].Parts["optic"].Spec.Id);
	}

	[Fact]
	public void ApplyRefusesWhileAnyPartIsMissing()
	{
		var (profile, character, rifle) = Setup();
		var preset = new PartPreset("p", "rifle", new Dictionary<string, string> { ["muzzle"] = "brake" });
		Assert.Equal(GunsmithResult.PartNotOwned, WeaponWorkshop.Apply(profile, character, rifle, preset));
		Assert.False(rifle.Parts.ContainsKey("muzzle"));
	}

	[Fact]
	public void ApplyRebuildsFromOwnedPartsAndReturnsTheOldOnesToTheStash()
	{
		var (profile, character, rifle) = Setup();
		var comp = ModItems.Part("comp")!;
		WeaponBuild.Attach(rifle, "muzzle", comp);
		profile.Stash.TryPlaceAnywhere(ModItems.Part("brake")!);
		profile.Stash.TryPlaceAnywhere(ModItems.Part("hg_tac")!);
		var preset = new PartPreset("p", "rifle", new Dictionary<string, string>
		{
			["muzzle"] = "brake", ["handguard"] = "hg_tac", ["stock"] = "stock_wood",
		});
		// hg_tac blocks the muzzle slot in the test data, so the brake stays in the stash
		Assert.Equal(GunsmithResult.Ok, WeaponWorkshop.Apply(profile, character, rifle, preset));
		Assert.Equal("hg_tac", rifle.Parts["handguard"].Spec.Id);
		Assert.Contains(profile.Stash.Items, i => i == comp);
	}

	[Fact]
	public void PresetsAndExaminedPartsSurviveSaving()
	{
		var run = new RunProgress();
		var profile = new GameProfile();
		profile.ExaminedParts.Add("brake");
		profile.Presets.Add(new PartPreset("sniper", "rifle", new Dictionary<string, string> { ["muzzle"] = "brake" }));
		var json = SaveSerializer.ToJson(run, profile, null, false);
		var loaded = SaveSerializer.Parse(json, Lookup).Profile;
		Assert.Contains("brake", loaded.ExaminedParts);
		Assert.Equal("brake", Assert.Single(loaded.Presets).Parts["muzzle"]);
	}
}

public class FieldStripTests
{
	private static ItemSpec PartSpec(string id, SwapKind swap, string iface = "rifle.muzzle", int w = 1)
		=> new(id, id, ItemCategory.WeaponPart, w, 1, 0.1f, Part: new PartSpec(new[] { iface }, new List<WeaponSlotSpec>(), new List<string>(), Swap: swap));

	private static readonly ItemSpec Kit = new(FieldStrip.KitItemId, "kit", ItemCategory.Misc, 1, 1, 0.8f);

	private static (CharacterInventory Character, ItemInstance Rifle) Setup()
	{
		var character = new CharacterInventory();
		var rifle = ModItems.NewRifle();
		character.TryEquip(rifle, EquipSlot.Primary);
		return (character, rifle);
	}

	[Fact]
	public void ToolFreePartsNeedNoKitTooledPartsDo()
	{
		var (character, rifle) = Setup();
		var free = ItemInstance.Create(PartSpec("qd", SwapKind.Free));
		var tooled = ItemInstance.Create(PartSpec("thr", SwapKind.Tooled));
		character.Pockets[0].TryPlaceAnywhere(free);
		character.Pockets[1].TryPlaceAnywhere(tooled);
		Assert.Equal(FieldStripResult.Ok, FieldStrip.Install(character, rifle, "muzzle", free));
		Assert.Equal(FieldStripResult.Ok, FieldStrip.Remove(character, rifle, "muzzle"));
		Assert.Equal(FieldStripResult.NeedsKit, FieldStrip.Install(character, rifle, "muzzle", tooled));
		character.Pockets[0].TryPlaceAnywhere(ItemInstance.Create(Kit));
		Assert.Equal(FieldStripResult.Ok, FieldStrip.Install(character, rifle, "muzzle", tooled));
		Assert.Same(tooled, rifle.Parts["muzzle"]);
	}

	[Fact]
	public void NeverPartsCannotBeTouchedEvenWithTheKit()
	{
		var (character, rifle) = Setup();
		character.Pockets[0].TryPlaceAnywhere(ItemInstance.Create(Kit));
		var locked = ItemInstance.Create(PartSpec("fixed", SwapKind.Never));
		character.Pockets[1].TryPlaceAnywhere(locked);
		Assert.Equal(FieldStripResult.Never, FieldStrip.Install(character, rifle, "muzzle", locked));
	}

	[Fact]
	public void KitInTheBackpackDoesNotCount()
	{
		var (character, _) = Setup();
		var pack = ItemInstance.Create(new ItemSpec("pack", "pack", ItemCategory.Backpack, 4, 4, 1f, Grids: new[] { new GridSize(5, 4) }));
		character.TryEquip(pack, EquipSlot.Backpack);
		pack.Grids[0].TryPlaceAnywhere(ItemInstance.Create(Kit));
		Assert.False(FieldStrip.HasKit(character));
		character.Pockets[0].TryPlaceAnywhere(ItemInstance.Create(Kit));
		Assert.True(FieldStrip.HasKit(character));
	}

	[Fact]
	public void RemoveWithNoRoomChangesNothing()
	{
		var (character, rifle) = Setup();
		var qd = ItemInstance.Create(PartSpec("qd", SwapKind.Free));
		WeaponBuild.Attach(rifle, "muzzle", qd);
		foreach (var grid in character.StorageGrids)
		{
			var filler = 0;
			while (grid.TryPlaceAnywhere(ItemInstance.Create(new ItemSpec("junk" + filler++, "j", ItemCategory.Misc, 1, 1, 0f))))
			{
			}
		}
		Assert.Equal(FieldStripResult.NoRoom, FieldStrip.Remove(character, rifle, "muzzle"));
		Assert.Same(qd, rifle.Parts["muzzle"]);
	}

	[Fact]
	public void DurationsFollowKindAndSlot()
	{
		var optic = new WeaponSlotSpec("o", "optic", new[] { "x" });
		var barrel = new WeaponSlotSpec("b", "barrel", new[] { "x" });
		Assert.Equal(2.5f, FieldStrip.Duration(optic, SwapKind.Free), 3);
		Assert.Equal(15f, FieldStrip.Duration(barrel, SwapKind.Tooled), 3);
		Assert.True(FieldStrip.Duration(optic, SwapKind.Tooled) is >= 8f);
	}

	[Fact]
	public void TimerCompletesOnceAndCancelsWhenInterrupted()
	{
		var timer = new FieldSwapTimer();
		var done = 0;
		Assert.True(timer.Start(2f, "x", () => done++));
		Assert.False(timer.Start(1f, "y", () => done += 10));
		timer.Update(1f, false);
		Assert.Equal(0.5f, timer.Progress, 3);
		timer.Update(1.2f, false);
		Assert.Equal(1, done);
		Assert.False(timer.Active);
		timer.Update(5f, false);
		Assert.Equal(1, done);

		var cancelled = 0;
		timer.Cancelled += () => cancelled++;
		timer.Start(2f, "z", () => done++);
		timer.Update(1f, true);
		Assert.Equal(1, cancelled);
		Assert.False(timer.Active);
		Assert.Equal(1, done);
	}
}
