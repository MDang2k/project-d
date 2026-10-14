# M7a Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One versioned JSON autosave that restores the character's gear, quick-use bindings and raid counters across launches, and counts a raid abandoned by quitting or crashing as a death on next launch.

**Architecture:** Four plain C# units in `scripts/systems` (`SaveModels`/`SaveSerializer`, `SaveMigrator`, `SaveFileStore`, `RaidRecovery`) with xUnit tests, plus a thin `SaveManager` autoload that loads into the existing `RunState.Current` at boot and is called by `RaidLevel` at raid start and end. `RunProgress` stays the in-memory holder (no `GameState` autoload until 7b).

**Tech Stack:** Godot 4.7.2 mono, C# .NET 8, `System.Text.Json` (BCL, no new package), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-12-m7a-persistence-design.md`

## Global Constraints

- Save path `user://save.json`; temp file `save.json.tmp`; previous file kept as `save.json.bak`; unreadable files renamed `save.corrupt-<UTC yyyyMMddHHmmss>-<main|bak>.json`, never deleted.
- Save schema `version` starts at `1`. Item specs are resolved by id from `ItemDb`; they are never stored.
- Unknown item id on load: drop that item with a warning, keep loading the rest.
- Pure logic classes contain no `GodotObject`; Godot math types are fine. Nodes are thin.
- Autoloads: add only `SaveManager` (on the approved list). Do not add `GameState`. Do not touch the input map.
- `dotnet build "Project D.csproj"` has no warnings; `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj` passes.
- Dev args after `--`: `--nosave` (no load, no write) and `--savepath=<file>` (override save file). Dev/headless runs must never touch the user's real save.
- Units, naming, events per `CLAUDE.md` (past-tense events, `_camelCase` private fields, files named after their class).

## Review Focus

- Process killed mid-raid: next launch records a death, keeps only the secure container, and hands out the starting kit; raid counter does not double count.
- Corrupt `save.json` with a good `save.json.bak`: loads the backup, quarantines the corrupt file, deletes nothing.
- Both files unreadable, or a zero-length file: starts a fresh profile without throwing; the bad files are kept.
- Save written by a newer build (`version` greater than current): never parsed or overwritten silently; preserved under a corrupt name.
- Item JSON missing optional fields: `durability` defaults to `-1` ("full"), `count` to `1`, not `0`.
- Spec drift: item id removed, nested grid smaller, or stack above `MaxStack`: dropped or clamped with a warning, no crash.
- `--nosave` run creates no files; `--savepath` redirects all reads and writes.

## File Structure

| File | Responsibility |
|---|---|
| `scripts/systems/SaveModels.cs` (create) | DTO records and `SaveFile.CurrentVersion`. |
| `scripts/systems/SaveSerializer.cs` (create) | Inventory/run state to and from DTOs and JSON. |
| `scripts/systems/SaveMigrator.cs` (create) | Version chain over raw JSON; `SaveTooNewException`. |
| `scripts/systems/SaveFileStore.cs` (create) | Atomic write, `.bak` fallback, quarantine. |
| `scripts/systems/RaidRecovery.cs` (create) | Death rule for a save written during a raid. |
| `scripts/systems/RunProgress.cs` (modify) | `Restore(...)`. |
| `scripts/core/SaveManager.cs` (create) | Autoload node: boot load, `BeginRaid`, `EndRaid`, dev args. |
| `scripts/raid/RaidLevel.cs` (modify) | Call `BeginRaid` / `EndRaid`. |
| `project.godot` (modify) | Register `SaveManager` autoload after `ItemDb`. |
| `tests/ProjectD.Tests/SaveSerializerTests.cs`, `SaveMigratorTests.cs`, `SaveFileStoreTests.cs`, `RaidRecoveryTests.cs` (create) | xUnit. |

---

### Task 1: Models and SaveSerializer

**Files:**
- Create: `scripts/systems/SaveModels.cs`, `scripts/systems/SaveSerializer.cs`
- Modify: `scripts/systems/RunProgress.cs`
- Test: `tests/ProjectD.Tests/SaveSerializerTests.cs`

**Interfaces:**
- Produces:
  - `record ItemSave(string Id, int Count = 1, bool Rotated = false, float Durability = -1f, List<string>? Rounds = null, ItemSave? Magazine = null, string? Chambered = null, List<GridSave>? Grids = null)`
  - `record PlacedItemSave(int X, int Y, ItemSave Item)`; `record GridSave(int Width, int Height, List<PlacedItemSave> Items)`
  - `record CharacterSave(Dictionary<string, ItemSave> Slots, List<GridSave> Pockets)`
  - `record CountersSave(int Raids, int Extractions, int Deaths, int Mia)`
  - `record SaveFile(int Version, bool RaidInProgress, bool NeedsKit, CountersSave Counters, List<string?> QuickUse, CharacterSave? Character)` with `const int CurrentVersion = 1`
  - `static class SaveSerializer`: `string ToJson(SaveFile)`, `SaveFile Parse(string json)` (uses `SaveMigrator.Default`, defined in Task 2; until then it deserializes directly), `SaveFile ToFile(RunProgress run, CharacterInventory? character, bool raidInProgress)`, `void Apply(SaveFile file, RunProgress run, Func<string, ItemSpec?> lookup, List<string> warnings)`, `CharacterInventory LoadCharacter(CharacterSave, Func<string, ItemSpec?>, List<string>)`.
  - `RunProgress.Restore(CharacterInventory? inventory, bool needsKit, int raids, int extractions, int deaths, int mia)`.

- [ ] **Step 1: Write the failing tests** (`tests/ProjectD.Tests/SaveSerializerTests.cs`)

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter SaveSerializerTests`
Expected: FAIL (compile error: `SaveSerializer`, `SaveFile` not defined).

- [ ] **Step 3: Write `scripts/systems/SaveModels.cs`**

```csharp
using System.Collections.Generic;

namespace ProjectD;

/// <summary>One item in a save: id plus mutable state. The spec itself is never stored.</summary>
public sealed record ItemSave(
	string Id,
	int Count = 1,
	bool Rotated = false,
	float Durability = -1f,
	List<string>? Rounds = null,
	ItemSave? Magazine = null,
	string? Chambered = null,
	List<GridSave>? Grids = null);

public sealed record PlacedItemSave(int X, int Y, ItemSave Item);

public sealed record GridSave(int Width, int Height, List<PlacedItemSave> Items);

/// <summary>Equipment by <see cref="EquipSlot"/> name, plus the pocket grids.</summary>
public sealed record CharacterSave(Dictionary<string, ItemSave> Slots, List<GridSave> Pockets);

public sealed record CountersSave(int Raids, int Extractions, int Deaths, int Mia);

/// <summary>The single autosave (GDD s15). Bump <see cref="CurrentVersion"/> with a migration when the shape changes.</summary>
public sealed record SaveFile(
	int Version,
	bool RaidInProgress,
	bool NeedsKit,
	CountersSave Counters,
	List<string?> QuickUse,
	CharacterSave? Character)
{
	public const int CurrentVersion = 1;
}
```

- [ ] **Step 4: Write `scripts/systems/SaveSerializer.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ProjectD;

/// <summary>
/// Converts run state and gear to and from the save format (GDD s15). Pure logic: item specs are
/// supplied through a lookup so nothing here needs the engine.
/// </summary>
public static class SaveSerializer
{
	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
	};

	public static string ToJson(SaveFile file) => JsonSerializer.Serialize(file, Options);

	public static SaveFile Parse(string json)
		=> JsonSerializer.Deserialize<SaveFile>(json, Options) ?? throw new JsonException("empty save");

	public static SaveFile ToFile(RunProgress run, CharacterInventory? character, bool raidInProgress)
		=> new(SaveFile.CurrentVersion, raidInProgress, run.NeedsKit,
			new CountersSave(run.Raids, run.Extractions, run.Deaths, run.Mia),
			Enumerable.Range(0, QuickUseBar.SlotCount).Select(run.QuickUse.IdAt).ToList(),
			character is null ? null : ToSave(character));

	public static void Apply(SaveFile file, RunProgress run, Func<string, ItemSpec?> lookup, List<string> warnings)
	{
		var inventory = file.Character is null ? null : LoadCharacter(file.Character, lookup, warnings);
		run.Restore(inventory, file.NeedsKit, file.Counters.Raids, file.Counters.Extractions,
			file.Counters.Deaths, file.Counters.Mia);
		for (var i = 0; i < QuickUseBar.SlotCount; i++)
		{
			var id = i < file.QuickUse.Count ? file.QuickUse[i] : null;
			if (id is null)
				run.QuickUse.Clear(i);
			else
				run.QuickUse.Bind(i, id);
		}
	}

	private static CharacterSave ToSave(CharacterInventory inv) => new(
		inv.EquippedItems.ToDictionary(kv => kv.Key.ToString(), kv => ToSave(kv.Value)),
		inv.Pockets.Select(ToSave).ToList());

	private static GridSave ToSave(InventoryGrid grid) => new(grid.Width, grid.Height,
		grid.Items.Select(i =>
		{
			var (x, y) = grid.PositionOf(i)!.Value;
			return new PlacedItemSave(x, y, ToSave(i));
		}).ToList());

	private static ItemSave ToSave(ItemInstance item) => new(
		item.Spec.Id, item.Count, item.Rotated, item.Durability,
		item.Rounds.Count > 0 ? item.Rounds.ToList() : null,
		item.InsertedMagazine is { } m ? ToSave(m) : null,
		item.Chambered,
		item.Grids.Count > 0 ? item.Grids.Select(ToSave).ToList() : null);

	public static CharacterInventory LoadCharacter(CharacterSave save, Func<string, ItemSpec?> lookup, List<string> warnings)
	{
		var inv = new CharacterInventory(save.Pockets.Select(p => new GridSize(p.Width, p.Height)));
		for (var i = 0; i < save.Pockets.Count; i++)
			PlaceAll(inv.Pockets[i], save.Pockets[i], lookup, warnings);
		foreach (var (key, itemSave) in save.Slots)
		{
			if (!Enum.TryParse<EquipSlot>(key, out var slot))
			{
				warnings.Add($"unknown equipment slot '{key}' dropped");
				continue;
			}
			if (FromSave(itemSave, lookup, warnings) is { } item && !inv.TryEquip(item, slot))
				warnings.Add($"'{item.Spec.Id}' no longer fits slot {slot}, dropped");
		}
		return inv;
	}

	private static void PlaceAll(InventoryGrid grid, GridSave save, Func<string, ItemSpec?> lookup, List<string> warnings)
	{
		foreach (var placed in save.Items)
		{
			if (FromSave(placed.Item, lookup, warnings) is not { } item)
				continue;
			if (!grid.TryPlace(item, placed.X, placed.Y))
				warnings.Add($"'{item.Spec.Id}' no longer fits at {placed.X},{placed.Y}, dropped");
		}
	}

	private static ItemInstance? FromSave(ItemSave save, Func<string, ItemSpec?> lookup, List<string> warnings)
	{
		if (lookup(save.Id) is not { } spec)
		{
			warnings.Add($"unknown item '{save.Id}' dropped");
			return null;
		}
		var item = ItemInstance.Create(spec, save.Count);
		item.Rotated = save.Rotated;
		item.Durability = save.Durability;
		if (save.Rounds is not null)
			item.Rounds.AddRange(save.Rounds);
		if (save.Magazine is not null)
			item.InsertedMagazine = FromSave(save.Magazine, lookup, warnings);
		item.Chambered = save.Chambered;
		if (save.Grids is not null)
		{
			for (var i = 0; i < save.Grids.Count && i < item.Grids.Count; i++)
				PlaceAll(item.Grids[i], save.Grids[i], lookup, warnings);
		}
		return item;
	}
}
```

- [ ] **Step 5: Add `Restore` to `RunProgress`** (`scripts/systems/RunProgress.cs`, after `MarkKitGiven`):

```csharp
	/// <summary>Replaces the run with state read from the save file.</summary>
	public void Restore(CharacterInventory? inventory, bool needsKit, int raids, int extractions, int deaths, int mia)
	{
		Inventory = inventory;
		NeedsKit = needsKit;
		Raids = raids;
		Extractions = extractions;
		Deaths = deaths;
		Mia = mia;
	}
```

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter SaveSerializerTests`
Expected: PASS (5 tests). If `MissingOptionalItemFieldsUseDeclaredDefaults` fails with durability `0`, the JSON ctor binding ignored the declared default: change `ItemSave` to a class with `init` properties and the same defaults.

- [ ] **Step 7: Commit**

```bash
git add scripts/systems/SaveModels.cs scripts/systems/SaveSerializer.cs scripts/systems/RunProgress.cs tests/ProjectD.Tests/SaveSerializerTests.cs
git commit -m "feat(save): save models and SaveSerializer round-trip for gear, counters, quick-use"
```

---

### Task 2: SaveMigrator

**Files:**
- Create: `scripts/systems/SaveMigrator.cs`
- Modify: `scripts/systems/SaveSerializer.cs` (`Parse` goes through the migrator)
- Test: `tests/ProjectD.Tests/SaveMigratorTests.cs`

**Interfaces:**
- Produces: `class SaveTooNewException(int found, int supported) : Exception`; `sealed class SaveMigrator(int currentVersion, IDictionary<int, Action<JsonObject>>? steps = null)` with `static SaveMigrator Default` and `JsonObject Migrate(string json)`. A step for version N mutates the object in place to the shape of N+1; the migrator writes `version`.
- Consumes: `SaveSerializer.Parse(string)`, `SaveFile.CurrentVersion`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectD.Tests;

public class SaveMigratorTests
{
	/// <summary>Frozen v1 sample: if this stops loading, v1 saves in the wild broke.</summary>
	private const string V1Fixture = """
		{"version":1,"raidInProgress":true,"needsKit":false,
		 "counters":{"raids":4,"extractions":2,"deaths":1,"mia":1},
		 "quickUse":["bandage","morphine",null,"water_canteen"],
		 "character":{"slots":{"Melee":{"id":"combat_knife"}},"pockets":[{"width":1,"height":2,"items":[]}]}}
		""";

	[Fact]
	public void FrozenV1FixtureStillParses()
	{
		var file = SaveSerializer.Parse(V1Fixture);
		Assert.Equal(1, file.Version);
		Assert.True(file.RaidInProgress);
		Assert.Equal(4, file.Counters.Raids);
		Assert.Equal("morphine", file.QuickUse[1]);
	}

	[Fact]
	public void StepsRunInOrderAndVersionIsStamped()
	{
		var m = new SaveMigrator(3, new Dictionary<int, Action<JsonObject>>
		{
			[1] = o => o["a"] = 1,
			[2] = o => o["b"] = (int)o["a"]! + 1,
		});
		var result = m.Migrate("""{"version":1}""");
		Assert.Equal(3, (int)result["version"]!);
		Assert.Equal(2, (int)result["b"]!);
	}

	[Fact]
	public void NewerThanSupportedThrowsSaveTooNew()
		=> Assert.Throws<SaveTooNewException>(() => new SaveMigrator(1).Migrate("""{"version":2}"""));

	[Fact]
	public void MissingStepOrMissingVersionIsAnError()
	{
		Assert.Throws<JsonException>(() => new SaveMigrator(2).Migrate("""{"version":1}"""));
		Assert.Throws<JsonException>(() => new SaveMigrator(1).Migrate("""{"nope":1}"""));
		Assert.Throws<JsonException>(() => new SaveMigrator(1).Migrate(""));
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter SaveMigratorTests`
Expected: FAIL (compile error, `SaveMigrator` not defined).

- [ ] **Step 3: Write `scripts/systems/SaveMigrator.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>The file was written by a newer build than this one understands.</summary>
public sealed class SaveTooNewException(int found, int supported)
	: Exception($"save version {found} is newer than supported {supported}");

/// <summary>
/// Brings an older save up to <see cref="SaveFile.CurrentVersion"/> by running one step per
/// version over the raw JSON. Add a step to <see cref="Default"/> whenever the format changes.
/// </summary>
public sealed class SaveMigrator
{
	private readonly int _current;
	private readonly IDictionary<int, Action<JsonObject>> _steps;

	public SaveMigrator(int currentVersion, IDictionary<int, Action<JsonObject>>? steps = null)
	{
		_current = currentVersion;
		_steps = steps ?? new Dictionary<int, Action<JsonObject>>();
	}

	// Each entry N upgrades a version-N object in place to the shape of N+1; the migrator stamps the version.
	public static SaveMigrator Default { get; } = new(SaveFile.CurrentVersion, new Dictionary<int, Action<JsonObject>>());

	public JsonObject Migrate(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
			throw new JsonException("save file is empty");
		if (JsonNode.Parse(json) is not JsonObject root)
			throw new JsonException("save is not a JSON object");
		var version = root["version"] is JsonValue v && v.TryGetValue<int>(out var n)
			? n
			: throw new JsonException("save has no version");
		if (version > _current)
			throw new SaveTooNewException(version, _current);
		while (version < _current)
		{
			if (!_steps.TryGetValue(version, out var step))
				throw new JsonException($"no migration from save version {version}");
			step(root);
			version++;
			root["version"] = version;
		}
		return root;
	}
}
```

- [ ] **Step 4: Route `SaveSerializer.Parse` through the migrator** (replace the existing `Parse`):

```csharp
	public static SaveFile Parse(string json) => Parse(json, SaveMigrator.Default);

	public static SaveFile Parse(string json, SaveMigrator migrator)
		=> migrator.Migrate(json).Deserialize<SaveFile>(Options) ?? throw new JsonException("empty save");
```

Add `using System.Text.Json.Nodes;` is not needed (extension `Deserialize` is on `JsonNode` in `System.Text.Json.Nodes`; add `using System.Text.Json.Nodes;` to `SaveSerializer.cs`).

- [ ] **Step 5: Run to verify all save tests pass**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter "SaveMigratorTests|SaveSerializerTests"`
Expected: PASS (9 tests).

- [ ] **Step 6: Commit**

```bash
git add scripts/systems/SaveMigrator.cs scripts/systems/SaveSerializer.cs tests/ProjectD.Tests/SaveMigratorTests.cs
git commit -m "feat(save): SaveMigrator version chain and frozen v1 fixture"
```

---

### Task 3: SaveFileStore

**Files:**
- Create: `scripts/systems/SaveFileStore.cs`
- Test: `tests/ProjectD.Tests/SaveFileStoreTests.cs`

**Interfaces:**
- Produces: `enum SaveSource { None, Main, Backup }`; `sealed class SaveFileStore(string path)` with `string BackupPath`, `void Write(string json)`, `(T? Value, SaveSource Source) Load<T>(Func<string, T> parse) where T : class`. `Load` tries main then backup; a file whose `parse` throws is renamed to `<dir>/<name>.corrupt-<UTC yyyyMMddHHmmss>-<main|bak>.json` and the next candidate is tried.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public sealed class SaveFileStoreTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "pd_save_" + Guid.NewGuid().ToString("N"));
	private readonly SaveFileStore _store;

	public SaveFileStoreTests() => _store = new SaveFileStore(Path.Combine(_dir, "save.json"));

	public void Dispose()
	{
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, true);
	}

	private static string Parse(string text) => text.StartsWith("ok") ? text : throw new FormatException("bad");

	private string[] Files() => Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(x => x).ToArray()!;

	[Fact]
	public void MissingFilesGiveNone()
	{
		var (value, source) = _store.Load(Parse);
		Assert.Null(value);
		Assert.Equal(SaveSource.None, source);
	}

	[Fact]
	public void WriteThenLoadAndSecondWriteKeepsBackup()
	{
		_store.Write("ok-1");
		_store.Write("ok-2");
		Assert.Equal(new[] { "save.json", "save.json.bak" }, Files());
		Assert.Equal("ok-1", File.ReadAllText(_store.BackupPath));
		var (value, source) = _store.Load(Parse);
		Assert.Equal("ok-2", value);
		Assert.Equal(SaveSource.Main, source);
	}

	[Fact]
	public void CorruptMainFallsBackToBackupAndIsQuarantinedNotDeleted()
	{
		_store.Write("ok-1");
		_store.Write("ok-2");
		File.WriteAllText(Path.Combine(_dir, "save.json"), "garbage");
		var (value, source) = _store.Load(Parse);
		Assert.Equal("ok-1", value);
		Assert.Equal(SaveSource.Backup, source);
		Assert.Contains(Files(), f => f.StartsWith("save.corrupt-") && f.EndsWith("-main.json"));
		Assert.False(File.Exists(Path.Combine(_dir, "save.json")));
	}

	[Fact]
	public void BothUnreadableGiveNoneAndKeepBothFiles()
	{
		Directory.CreateDirectory(_dir);
		File.WriteAllText(Path.Combine(_dir, "save.json"), "");
		File.WriteAllText(Path.Combine(_dir, "save.json.bak"), "garbage");
		var (value, source) = _store.Load(Parse);
		Assert.Null(value);
		Assert.Equal(SaveSource.None, source);
		Assert.Equal(2, Files().Count(f => f.StartsWith("save.corrupt-")));
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter SaveFileStoreTests`
Expected: FAIL (compile error, `SaveFileStore` not defined).

- [ ] **Step 3: Write `scripts/systems/SaveFileStore.cs`**

```csharp
using System;
using System.IO;

namespace ProjectD;

public enum SaveSource { None, Main, Backup }

/// <summary>
/// Reads and writes the save file safely: a crash while writing never leaves a half-written
/// save, the previous save is kept as <c>.bak</c>, and a file that cannot be parsed is renamed
/// aside, never deleted. Plain System.IO so it is testable without the engine.
/// </summary>
public sealed class SaveFileStore
{
	private readonly string _path;

	public SaveFileStore(string path) => _path = path;

	public string BackupPath => _path + ".bak";

	public void Write(string json)
	{
		var dir = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
		var temp = _path + ".tmp";
		File.WriteAllText(temp, json);
		if (File.Exists(_path))
			File.Copy(_path, BackupPath, true);
		File.Move(temp, _path, true);
	}

	/// <param name="parse">Throws when the text is not a usable save (corrupt, too new).</param>
	public (T? Value, SaveSource Source) Load<T>(Func<string, T> parse) where T : class
	{
		foreach (var (file, source, tag) in new[] { (_path, SaveSource.Main, "main"), (BackupPath, SaveSource.Backup, "bak") })
		{
			if (!File.Exists(file))
				continue;
			try
			{
				return (parse(File.ReadAllText(file)), source);
			}
			catch (Exception)
			{
				Quarantine(file, tag);
			}
		}
		return (null, SaveSource.None);
	}

	private void Quarantine(string file, string tag)
	{
		var dir = Path.GetDirectoryName(_path) ?? "";
		var name = Path.GetFileNameWithoutExtension(_path);
		var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
		File.Move(file, Path.Combine(dir, $"{name}.corrupt-{stamp}-{tag}.json"), true);
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter SaveFileStoreTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add scripts/systems/SaveFileStore.cs tests/ProjectD.Tests/SaveFileStoreTests.cs
git commit -m "feat(save): SaveFileStore with atomic write, backup fallback and quarantine"
```

---

### Task 4: RaidRecovery

**Files:**
- Create: `scripts/systems/RaidRecovery.cs`
- Test: `tests/ProjectD.Tests/RaidRecoveryTests.cs`

**Interfaces:**
- Consumes: `RunProgress.Restore/Record/Inventory`, `RaidOutcomeRules.Apply`, `RaidOutcome.Killed`.
- Produces: `static class RaidRecovery` with `bool Apply(bool raidInProgress, RunProgress run)` returning true when a death was recorded.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class RaidRecoveryTests
{
	private static ItemInstance Item(string id, ItemCategory cat, int w = 1, int h = 1, GridSize[]? grids = null)
		=> ItemInstance.Create(new ItemSpec(id, "n", cat, w, h, 1f, Grids: grids));

	[Fact]
	public void SaveWrittenMidRaidCountsAsDeathAndKeepsOnlyTheSecureContainer()
	{
		var inv = new CharacterInventory();
		var secure = Item("secure", ItemCategory.SecureContainer, 2, 2, new[] { new GridSize(2, 2) });
		inv.TryEquip(secure, EquipSlot.SecureContainer);
		secure.Grids[0].TryPlaceAnywhere(Item("note", ItemCategory.Misc));
		inv.TryEquip(Item("vest", ItemCategory.Armor, 3, 3), EquipSlot.Vest);
		inv.TryAutoStore(Item("bandage", ItemCategory.Medical));
		var run = new RunProgress();
		run.Restore(inv, false, 3, 2, 0, 1);

		Assert.True(RaidRecovery.Apply(true, run));

		Assert.Equal(4, run.Raids);
		Assert.Equal(1, run.Deaths);
		Assert.True(run.NeedsKit);
		Assert.Equal(new[] { "secure", "note" }, run.Inventory!.AllItems().Select(i => i.Spec.Id).ToArray());
	}

	[Fact]
	public void CleanSaveIsLeftAlone()
	{
		var run = new RunProgress();
		run.Restore(new CharacterInventory(), false, 3, 2, 0, 1);
		Assert.False(RaidRecovery.Apply(false, run));
		Assert.Equal(3, run.Raids);
		Assert.False(run.NeedsKit);
	}

	[Fact]
	public void RaidInProgressWithNoSavedGearStillCountsTheDeath()
	{
		var run = new RunProgress();
		Assert.True(RaidRecovery.Apply(true, run));
		Assert.NotNull(run.Inventory);
		Assert.True(run.NeedsKit);
		Assert.Equal(1, run.Deaths);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter RaidRecoveryTests`
Expected: FAIL (compile error, `RaidRecovery` not defined).

- [ ] **Step 3: Write `scripts/systems/RaidRecovery.cs`**

```csharp
namespace ProjectD;

/// <summary>
/// Quitting or crashing mid-raid counts as death on next launch (GDD s15). The save written at
/// raid start carries the at-risk loadout and <c>raidInProgress</c>; this applies the death rules.
/// Nemesis and insurance hook in here in later sub-projects.
/// </summary>
public static class RaidRecovery
{
	/// <returns>True when a death was recorded.</returns>
	public static bool Apply(bool raidInProgress, RunProgress run)
	{
		if (!raidInProgress)
			return false;
		var inventory = run.Inventory ?? new CharacterInventory();
		RaidOutcomeRules.Apply(RaidOutcome.Killed, inventory);
		run.Record(RaidOutcome.Killed, inventory);
		return true;
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter RaidRecoveryTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add scripts/systems/RaidRecovery.cs tests/ProjectD.Tests/RaidRecoveryTests.cs
git commit -m "feat(save): RaidRecovery counts an abandoned raid as a death"
```

---

### Task 5: SaveManager autoload and raid wiring

**Files:**
- Create: `scripts/core/SaveManager.cs`
- Modify: `scripts/raid/RaidLevel.cs` (after `inv0` at line ~42; after `run.Record` at line ~56), `project.godot` (`[autoload]`)

**Interfaces:**
- Consumes: `SaveFileStore`, `SaveSerializer.Parse/ToFile/ToJson/Apply`, `RaidRecovery.Apply`, `RunState.Current`, `ItemDb.Instance.Registry.Spec(id)`.
- Produces: `SaveManager.Instance` (static, nullable), `void BeginRaid(CharacterInventory carried)`, `void EndRaid()`.

- [ ] **Step 1: Write `scripts/core/SaveManager.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Autoload (CLAUDE.md approved list): loads the single autosave at boot into
/// <see cref="RunState.Current"/> and writes it at raid start (loadout at risk) and raid end.
/// All rules live in the pure classes; this node only touches files and the engine.
/// Dev args after "--": --nosave (no load, no write), --savepath=&lt;file&gt; (override location).
/// </summary>
public partial class SaveManager : Node
{
	public static SaveManager? Instance { get; private set; }

	private SaveFileStore? _store;

	public override void _Ready()
	{
		Instance = this;
		var args = OS.GetCmdlineUserArgs();
		if (args.Contains("--nosave"))
			return;
		var path = args.FirstOrDefault(a => a.StartsWith("--savepath="))?["--savepath=".Length..]
			?? ProjectSettings.GlobalizePath("user://save.json");
		_store = new SaveFileStore(path);
		Load();
	}

	/// <summary>Writes the loadout the player carries into the raid, marked in progress.</summary>
	public void BeginRaid(CharacterInventory carried) => Write(carried, true);

	/// <summary>Writes the state after the outcome rules were applied and recorded.</summary>
	public void EndRaid() => Write(RunState.Current.Inventory, false);

	private void Write(CharacterInventory? character, bool raidInProgress)
	{
		if (_store is null)
			return;
		var file = SaveSerializer.ToFile(RunState.Current, character, raidInProgress);
		_store.Write(SaveSerializer.ToJson(file));
	}

	private void Load()
	{
		var (file, source) = _store!.Load(json => SaveSerializer.Parse(json));
		if (file is null)
		{
			GD.Print("SAVE none, starting a new profile");
			return;
		}
		if (source == SaveSource.Backup)
			GD.PushWarning("[Save] main save unreadable, loaded the backup");
		var warnings = new List<string>();
		SaveSerializer.Apply(file, RunState.Current, id => ItemDb.Instance?.Registry.Spec(id), warnings);
		foreach (var w in warnings)
			GD.PushWarning($"[Save] {w}");
		GD.Print($"SAVE loaded raids={RunState.Current.Raids} inRaid={file.RaidInProgress}");
		if (RaidRecovery.Apply(file.RaidInProgress, RunState.Current))
		{
			GD.Print("SAVE raid was in progress at launch: counted as death");
			Write(RunState.Current.Inventory, false);
		}
	}
}
```

- [ ] **Step 2: Register the autoload.** In `project.godot` `[autoload]`, after the `ItemDb=` line add:

```
SaveManager="*res://scripts/core/SaveManager.cs"
```

- [ ] **Step 3: Wire `RaidLevel`.** After `var inv0 = Player.Inventory.Inventory;` add `SaveManager.Instance?.BeginRaid(inv0);`. In `OnRaidEnded`, directly after `run.Record(outcome, inv);` add `SaveManager.Instance?.EndRaid();`.

- [ ] **Step 4: Build and test**

Run: `dotnet build "Project D.csproj"` then `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj`
Expected: no warnings, all tests pass.

- [ ] **Step 5: Headless smoke: normal run persists**

```bash
SAVE="$(cygpath -m "$(mktemp -d)")/save.json"
D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://scenes/maps/map1.tscn -- --raidtime=8 --autonew --savepath=$SAVE
cat "$SAVE"
```
Expected: three raids run, then the process quits. `save.json` shows `"raidInProgress": false`, `"counters"` with `raids: 3` and `mia: 3`, `needsKit: true`, a `character` with only the secure container.

- [ ] **Step 6: Headless smoke: kill mid-raid, next launch counts a death**

```bash
timeout 12 D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://scenes/maps/map1.tscn -- --raidtime=600 --savepath=$SAVE
grep -E "raidInProgress|\"raids\"" "$SAVE"
D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://scenes/maps/map1.tscn -- --raidtime=3 --autonew --savepath=$SAVE 2>&1 | grep -E "SAVE|RAIDSTART" | head
```
Expected: after the killed run the file has `"raidInProgress": true`. The next launch prints `SAVE raid was in progress at launch: counted as death`, and `RAIDSTART run=4` (3 earlier raids plus the recovered death) on its first raid.

- [ ] **Step 7: Headless smoke: `--nosave` writes nothing**

```bash
EMPTY="$(cygpath -m "$(mktemp -d)")"
D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://scenes/maps/map1.tscn -- --raidtime=3 --autonew --nosave --savepath=$EMPTY/save.json
ls "$EMPTY"
```
Expected: the directory is empty.

- [ ] **Step 8: Commit** (include any `.cs.uid` files Godot generated for the new scripts)

```bash
git add scripts project.godot
git status --short
git commit -m "feat(save): SaveManager autoload, raid start/end saves, --nosave and --savepath dev args"
```

---

### Task 6: Verification and handoff

**Files:**
- Modify: `docs/superpowers/specs/2026-10-12-m7a-persistence-design.md` (status line)

- [ ] **Step 1: Full verification**

Run: `dotnet build "Project D.csproj"`, `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj`, then the GUT smoke command from `CLAUDE.md`.
Expected: no warnings, all pass.

- [ ] **Step 2: Mark the spec implemented.** Change `Status: approved design, not implemented.` to `Status: implemented (plan 2026-10-15-m7a-persistence).`

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-10-12-m7a-persistence-design.md
git commit -m "docs: M7a persistence implemented"
```

- [ ] **Step 4: Handoff checklist for the user**

Playtest: start map 1 from the editor, extract with some gear, close the game, relaunch and start a raid: gear and quick-use bindings are the same, and the raid counter went up. Then start a raid, close the window mid-raid, relaunch: the next raid starts with the basic kit and your secure container contents (counted as a death). Report: any item that came back wrong (missing, wrong magazine rounds, armor durability), and any `[Save]` warnings in the output. Your real save is at `%APPDATA%\Godot\app_userdata\Project D\save.json`; delete it to start a fresh profile.
