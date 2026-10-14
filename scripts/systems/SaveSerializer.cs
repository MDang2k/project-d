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
