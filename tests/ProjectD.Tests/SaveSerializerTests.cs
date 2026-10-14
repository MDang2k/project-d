using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class SaveSerializerTests
{
	private static readonly Dictionary<string, ItemSpec> Specs = new[]
	{
		new ItemSpec("ak", "n", ItemCategory.Weapon, 5, 2, 3.5f, WeaponClass: WeaponClass.Primary),
		new ItemSpec("ak_mag", "n", ItemCategory.Magazine, 1, 2, 0.2f, MagazineCapacity: 30, RoundWeightKg: 0.01f, Caliber: "7.62x39"),
		new ItemSpec("vest", "n", ItemCategory.Armor, 3, 3, 4f),
		new ItemSpec("rig", "n", ItemCategory.Rig, 3, 2, 1f, Grids: new[] { new GridSize(4, 3) }),
		new ItemSpec("secure", "n", ItemCategory.SecureContainer, 2, 2, 0.5f, Grids: new[] { new GridSize(2, 2) }),
		new ItemSpec("bandage", "n", ItemCategory.Medical, 1, 1, 0.1f, MaxStack: 4),
		new ItemSpec("knife", "n", ItemCategory.Melee, 1, 2, 0.3f),
	}.ToDictionary(s => s.Id);

	private static ItemSpec? Lookup(string id) => Specs.GetValueOrDefault(id);
	private static ItemInstance Item(string id, int count = 1) => ItemInstance.Create(Specs[id], count);

	private static RunProgress RoundTrip(RunProgress run, CharacterInventory? character, bool raid, List<string> warnings)
	{
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(run, character, raid));
		var loaded = new RunProgress();
		SaveSerializer.Apply(SaveSerializer.Parse(json), loaded, Lookup, warnings);
		return loaded;
	}

	[Fact]
	public void FullyLoadedCharacterSurvivesRoundTrip()
	{
		var inv = new CharacterInventory();
		var ak = Item("ak");
		var mag = Item("ak_mag");
		mag.Rounds.AddRange(new[] { "a", "a", "b" });
		ak.InsertedMagazine = mag;
		ak.Chambered = "a";
		inv.TryEquip(ak, EquipSlot.Primary);
		var vest = Item("vest");
		vest.Durability = 12.5f;
		inv.TryEquip(vest, EquipSlot.Vest);
		var rig = Item("rig");
		inv.TryEquip(rig, EquipSlot.Rig);
		var spare = Item("ak_mag");
		spare.Rotated = true;
		Assert.True(rig.Grids[0].TryPlace(spare, 2, 1));
		var secure = Item("secure");
		inv.TryEquip(secure, EquipSlot.SecureContainer);
		Assert.True(secure.Grids[0].TryPlaceAnywhere(Item("bandage", 3)));
		Assert.True(inv.TryAutoStore(Item("knife")));

		var warnings = new List<string>();
		var run = new RunProgress();
		var loaded = RoundTrip(run, inv, false, warnings).Inventory!;

		Assert.Empty(warnings);
		var ak2 = loaded.InSlot(EquipSlot.Primary)!;
		Assert.Equal(new[] { "a", "a", "b" }, ak2.InsertedMagazine!.Rounds);
		Assert.Equal("a", ak2.Chambered);
		Assert.Equal(12.5f, loaded.InSlot(EquipSlot.Vest)!.Durability);
		var rigGrid = loaded.InSlot(EquipSlot.Rig)!.Grids[0];
		var placed = rigGrid.ItemAt(2, 1)!;
		Assert.Equal("ak_mag", placed.Spec.Id);
		Assert.True(placed.Rotated);
		Assert.Equal(3, loaded.InSlot(EquipSlot.SecureContainer)!.Grids[0].Items.Single().Count);
		Assert.Equal(inv.TotalWeightKg, loaded.TotalWeightKg, 3);
		Assert.Single(loaded.Pockets.SelectMany(p => p.Items), i => i.Spec.Id == "knife");
	}

	[Fact]
	public void CountersNeedsKitAndQuickUseSurvive()
	{
		var run = new RunProgress();
		run.Record(RaidOutcome.Extracted, new CharacterInventory());
		run.Record(RaidOutcome.Killed, new CharacterInventory());
		run.QuickUse.Clear(2);
		var loaded = RoundTrip(run, run.Inventory, false, new List<string>());
		Assert.Equal(2, loaded.Raids);
		Assert.Equal(1, loaded.Extractions);
		Assert.Equal(1, loaded.Deaths);
		Assert.True(loaded.NeedsKit);
		Assert.Equal(run.QuickUse.IdAt(0), loaded.QuickUse.IdAt(0));
		Assert.Null(loaded.QuickUse.IdAt(2));
	}

	[Fact]
	public void UnknownItemIdIsDroppedWithAWarningAndTheRestLoads()
	{
		const string json = """
			{"version":1,"raidInProgress":false,"needsKit":false,
			 "counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[null,null,null,null],
			 "character":{"slots":{"Primary":{"id":"gone_weapon"},"Melee":{"id":"knife"}},
			              "pockets":[{"width":1,"height":2,"items":[]}]}}
			""";
		var run = new RunProgress();
		var warnings = new List<string>();
		SaveSerializer.Apply(SaveSerializer.Parse(json), run, Lookup, warnings);
		Assert.Contains(warnings, w => w.Contains("gone_weapon"));
		Assert.NotNull(run.Inventory!.InSlot(EquipSlot.Melee));
		Assert.Null(run.Inventory.InSlot(EquipSlot.Primary));
	}

	[Fact]
	public void MissingOptionalItemFieldsUseDeclaredDefaults()
	{
		const string json = """
			{"version":1,"raidInProgress":false,"needsKit":false,
			 "counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[null,null,null,null],
			 "character":{"slots":{"Vest":{"id":"vest"}},
			              "pockets":[{"width":1,"height":2,"items":[{"x":0,"y":0,"item":{"id":"bandage"}}]}]}}
			""";
		var run = new RunProgress();
		SaveSerializer.Apply(SaveSerializer.Parse(json), run, Lookup, new List<string>());
		Assert.Equal(-1f, run.Inventory!.InSlot(EquipSlot.Vest)!.Durability);
		Assert.Equal(1, run.Inventory.Pockets[0].Items.Single().Count);
	}

	[Fact]
	public void ItemThatNoLongerFitsIsDroppedWithAWarning()
	{
		const string json = """
			{"version":1,"raidInProgress":false,"needsKit":false,
			 "counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[null,null,null,null],
			 "character":{"slots":{},"pockets":[{"width":1,"height":2,"items":[{"x":0,"y":0,"item":{"id":"vest"}}]}]}}
			""";
		var run = new RunProgress();
		var warnings = new List<string>();
		SaveSerializer.Apply(SaveSerializer.Parse(json), run, Lookup, warnings);
		Assert.Empty(run.Inventory!.Pockets[0].Items);
		Assert.Single(warnings);
	}
}
