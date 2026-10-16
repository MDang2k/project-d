using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

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

	public static SaveFile Parse(string json) => Parse(json, SaveMigrator.Default);

	public static SaveFile Parse(string json, SaveMigrator migrator)
	{
		var file = migrator.Migrate(json).Deserialize<SaveFile>(Options) ?? throw new JsonException("empty save");
		Validate(file);
		return file;
	}

	// System.Text.Json does not enforce non-nullable members; reject incomplete files here so the
	// store quarantines them and falls back to the backup instead of failing later in Apply.
	private static void Validate(SaveFile f)
	{
		if (f.Counters is null || f.QuickUse is null)
			throw new JsonException("save is missing counters or quickUse");
		if (f.Stash is not null)
			ValidateGrid(f.Stash);
		if (f.Character is not { } c)
			return;
		if (c.Slots is null || c.Pockets is null)
			throw new JsonException("save character is missing slots or pockets");
		foreach (var item in c.Slots.Values)
			ValidateItem(item);
		foreach (var grid in c.Pockets)
			ValidateGrid(grid);
	}

	private static void ValidateGrid(GridSave? grid)
	{
		if (grid?.Items is null)
			throw new JsonException("save grid has no items list");
		foreach (var placed in grid.Items)
			ValidateItem(placed?.Item);
	}

	private static void ValidateItem(ItemSave? item)
	{
		if (item is null || string.IsNullOrEmpty(item.Id))
			throw new JsonException("save item has no id");
		if (item.Magazine is not null)
			ValidateItem(item.Magazine);
		if (item.Grids is not null)
		{
			foreach (var grid in item.Grids)
				ValidateGrid(grid);
		}
	}

	public static SaveFile ToFile(RunProgress run, CharacterInventory? character, bool raidInProgress)
		=> ToFile(run, new GameProfile(), character, raidInProgress);

	public static SaveFile ToFile(RunProgress run, GameProfile profile, CharacterInventory? character, bool raidInProgress)
		=> new(SaveFile.CurrentVersion, raidInProgress,
			new CountersSave(run.Raids, run.Extractions, run.Deaths, run.Mia),
			Enumerable.Range(0, QuickUseBar.SlotCount).Select(run.QuickUse.IdAt).ToList(),
			character is null ? null : ToSave(character),
			profile.Side?.ToString(),
			ToSave(profile.Stash));

	public static void Apply(SaveFile file, RunProgress run, Func<string, ItemSpec?> lookup, List<string> warnings)
		=> Apply(file, run, new GameProfile(), lookup, warnings);

	public static void Apply(SaveFile file, RunProgress run, GameProfile profile, Func<string, ItemSpec?> lookup,
		List<string> warnings)
	{
		var inventory = file.Character is null ? null : LoadCharacter(file.Character, lookup, warnings);
		run.Restore(inventory, file.Counters.Raids, file.Counters.Extractions,
			file.Counters.Deaths, file.Counters.Mia);
		profile.Side = Enum.TryParse<PlayerSide>(file.Side, out var side) ? side : null;
		var stash = new InventoryGrid(GameProfile.StashWidth, GameProfile.StashHeight);
		if (file.Stash is { } stashSave)
			PlaceAll(stash, stashSave, lookup, warnings);
		profile.ReplaceStash(stash);
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
		// Pocket layout is a design constant, not save data: use the current one and re-place the items.
		var inv = new CharacterInventory();
		for (var i = 0; i < save.Pockets.Count; i++)
		{
			if (i >= inv.Pockets.Count)
			{
				warnings.Add($"pocket {i} no longer exists, its items were dropped");
				continue;
			}
			PlaceAll(inv.Pockets[i], save.Pockets[i], lookup, warnings);
		}
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
