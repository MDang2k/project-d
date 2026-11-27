using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
	List<GridSave>? Grids = null,
	/// <summary>Insured for the next raid, and by whom (v4).</summary>
	bool Insured = false,
	string? InsuredWith = null,
	/// <summary>Taken from a container or body during a raid (v5).</summary>
	bool FoundInRaid = false,
	/// <summary>Attached weapon parts by slot id, each with its own children (v6).</summary>
	Dictionary<string, ItemSave>? Parts = null);

public sealed record PlacedItemSave(int X, int Y, ItemSave Item);

public sealed record GridSave(int Width, int Height, List<PlacedItemSave> Items);

/// <summary>Equipment by <see cref="EquipSlot"/> name, plus the pocket grids.</summary>
public sealed record CharacterSave(Dictionary<string, ItemSave> Slots, List<GridSave> Pockets);

/// <summary>
/// Items, grids and characters to and from their save types, shared by every save part that holds items.
/// System.Text.Json does not enforce non-nullable members, so loading rejects incomplete data here.
/// </summary>
public static class ItemSaves
{
	public static CharacterSave ToSave(CharacterInventory inv) => new(
		inv.EquippedItems.ToDictionary(kv => kv.Key.ToString(), kv => ToSave(kv.Value)),
		inv.Pockets.Select(ToSave).ToList());

	public static GridSave ToSave(InventoryGrid grid) => new(grid.Width, grid.Height,
		grid.Items.Select(i =>
		{
			var (x, y) = grid.PositionOf(i)!.Value;
			return new PlacedItemSave(x, y, ToSave(i));
		}).ToList());

	public static ItemSave ToSave(ItemInstance item) => new(
		item.Spec.Id, item.Count, item.Rotated, item.Durability,
		item.Rounds.Count > 0 ? item.Rounds.ToList() : null,
		item.InsertedMagazine is { } m ? ToSave(m) : null,
		item.Chambered,
		item.Grids.Count > 0 ? item.Grids.Select(ToSave).ToList() : null,
		item.Insured, item.InsuredWith, item.FoundInRaid,
		item.Parts.Count > 0 ? item.Parts.ToDictionary(kv => kv.Key, kv => ToSave(kv.Value)) : null);

	public static CharacterInventory LoadCharacter(CharacterSave? save, LoadContext ctx)
	{
		if (save?.Slots is null || save.Pockets is null)
			throw new JsonException("save character is missing slots or pockets");
		// Pocket layout is a design constant, not save data: use the current one and re-place the items.
		var inv = new CharacterInventory();
		for (var i = 0; i < save.Pockets.Count; i++)
		{
			if (i >= inv.Pockets.Count)
			{
				ctx.Warnings.Add($"pocket {i} no longer exists, its items were dropped");
				continue;
			}
			PlaceAll(inv.Pockets[i], save.Pockets[i], ctx);
		}
		foreach (var (key, itemSave) in save.Slots)
		{
			if (!Enum.TryParse<EquipSlot>(key, out var slot))
			{
				ctx.Warnings.Add($"unknown equipment slot '{key}' dropped");
				continue;
			}
			if (LoadItem(itemSave, ctx) is { } item && !inv.TryEquip(item, slot))
				ctx.Warnings.Add($"'{item.Spec.Id}' no longer fits slot {slot}, dropped");
		}
		return inv;
	}

	public static void PlaceAll(InventoryGrid grid, GridSave? save, LoadContext ctx)
	{
		if (save?.Items is null)
			throw new JsonException("save grid has no items list");
		foreach (var placed in save.Items)
		{
			if (LoadItem(placed?.Item, ctx) is not { } item)
				continue;
			if (!grid.TryPlace(item, placed!.X, placed.Y))
				ctx.Warnings.Add($"'{item.Spec.Id}' no longer fits at {placed.X},{placed.Y}, dropped");
		}
	}

	/// <summary>The item, or null (with a warning) when its id is no longer known.</summary>
	public static ItemInstance? LoadItem(ItemSave? save, LoadContext ctx)
	{
		if (save is null || string.IsNullOrEmpty(save.Id))
			throw new JsonException("save item has no id");
		if (ctx.Lookup(save.Id) is not { } spec)
		{
			ctx.Warnings.Add($"unknown item '{save.Id}' dropped");
			return null;
		}
		var item = ItemInstance.Create(spec, save.Count);
		item.Rotated = save.Rotated;
		item.Durability = save.Durability;
		if (save.Rounds is not null)
			item.Rounds.AddRange(save.Rounds);
		if (save.Magazine is not null)
			item.InsertedMagazine = LoadItem(save.Magazine, ctx);
		item.Chambered = save.Chambered;
		item.Insured = save.Insured;
		item.InsuredWith = save.InsuredWith;
		item.FoundInRaid = save.FoundInRaid;
		if (save.Parts is not null)
		{
			foreach (var (slotId, partSave) in save.Parts)
			{
				if (LoadItem(partSave, ctx) is not { } part)
					continue;
				if (!WeaponBuild.Attach(item, slotId, part))
				{
					ctx.Warnings.Add($"part '{part.Spec.Id}' no longer fits slot '{slotId}' of '{item.Spec.Id}', detached");
					ctx.Detached.Add(part);
				}
			}
		}
		// A weapon whose spec gained a required slot gets the default part for it.
		WeaponBuild.ApplyDefaults(item, id => ctx.Lookup(id) is { } partSpec ? ItemInstance.Create(partSpec) : null);
		if (save.Grids is not null)
		{
			for (var i = 0; i < save.Grids.Count && i < item.Grids.Count; i++)
				PlaceAll(item.Grids[i], save.Grids[i], ctx);
		}
		return item;
	}
}
