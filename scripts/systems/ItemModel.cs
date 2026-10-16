using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum ItemCategory
{
	Weapon, Magazine, Ammo, Armor, Helmet, Rig, Backpack, Medical, Food, Drink, Melee, SecureContainer, Misc,
}

/// <summary>Primary weapons go in the primary or secondary slot, pistols only in the pistol slot.</summary>
public enum WeaponClass { None, Primary, Pistol }

/// <param name="Width">Grid cells wide (before rotation).</param>
public readonly record struct GridSize(int Width, int Height);

/// <summary>Static definition of an item type (GDD s10). Built from data resources.</summary>
public sealed record ItemSpec(
	string Id,
	string NameKey,
	ItemCategory Category,
	int Width,
	int Height,
	float WeightKg,
	int MaxStack = 1,
	WeaponClass WeaponClass = WeaponClass.None,
	/// <summary>Grids this item provides when worn (rigs, backpacks, secure container).</summary>
	IReadOnlyList<GridSize>? Grids = null,
	/// <summary>Magazines: rounds capacity and per-round weight.</summary>
	int MagazineCapacity = 0,
	float RoundWeightKg = 0f,
	/// <summary>Ammo and magazines: caliber string used to match them.</summary>
	string? Caliber = null,
	/// <summary>Ammo: the AmmoData id this stack represents.</summary>
	string? AmmoId = null,
	/// <summary>Trade value in currency units; 0 means not sellable (GDD s13).</summary>
	int BaseValue = 0,
	/// <summary>Armor and helmets: full durability, so wear can price them down; 0 for everything else.</summary>
	float MaxDurability = 0f);

/// <summary>One concrete item in the world, a grid or a slot: spec plus mutable state.</summary>
public sealed class ItemInstance
{
	private static int _nextUid = 1;

	private ItemInstance(ItemSpec spec, int count)
	{
		Uid = _nextUid++;
		Spec = spec;
		Count = Math.Clamp(count, 1, Math.Max(1, spec.MaxStack));
		if (spec.Grids is { } grids)
			Grids = grids.Select(g => new InventoryGrid(g.Width, g.Height)).ToList();
	}

	public int Uid { get; }
	public ItemSpec Spec { get; }
	/// <summary>Stack size (ammo, meds); 1 for everything else.</summary>
	public int Count { get; set; }
	public bool Rotated { get; set; }
	/// <summary>Armor: remaining durability; negative means "full" (not yet set).</summary>
	public float Durability { get; set; } = -1f;
	/// <summary>Grids this item provides (rigs, backpacks, secure container).</summary>
	public List<InventoryGrid> Grids { get; } = new();
	/// <summary>Magazines: loaded rounds as ammo ids, last is fed first.</summary>
	public List<string> Rounds { get; } = new();
	/// <summary>Weapons: the magazine currently inserted (its weight counts with the weapon).</summary>
	public ItemInstance? InsertedMagazine { get; set; }
	/// <summary>Weapons: ammo id of the round in the chamber, if any.</summary>
	public string? Chambered { get; set; }

	public int Width => Rotated ? Spec.Height : Spec.Width;
	public int Height => Rotated ? Spec.Width : Spec.Height;

	public static ItemInstance Create(ItemSpec spec, int count = 1) => new(spec, count);

	/// <summary>Weight of this item with everything inside it.</summary>
	public float WeightKg
	{
		get
		{
			var w = Spec.WeightKg * Count + Rounds.Count * Spec.RoundWeightKg + (InsertedMagazine?.WeightKg ?? 0f);
			foreach (var grid in Grids)
				w += grid.Items.Sum(i => i.WeightKg);
			return w;
		}
	}

	/// <summary>True if this item (or anything nested inside it) is the given grid's owner chain.</summary>
	public bool Contains(InventoryGrid grid)
		=> Grids.Contains(grid) || Grids.Any(g => g.Items.Any(i => i.Contains(grid)));
}

/// <summary>A rectangular grid of cells holding rotated or unrotated items (GDD s10). Pure logic.</summary>
public sealed class InventoryGrid
{
	private readonly List<(ItemInstance Item, int X, int Y)> _entries = new();

	public InventoryGrid(int width, int height)
	{
		Width = width;
		Height = height;
	}

	public int Width { get; }
	public int Height { get; }
	public IEnumerable<ItemInstance> Items => _entries.Select(e => e.Item);
	public int ItemCount => _entries.Count;

	public bool Contains(ItemInstance item) => _entries.Any(e => e.Item == item);

	public (int X, int Y)? PositionOf(ItemInstance item)
	{
		foreach (var e in _entries)
		{
			if (e.Item == item)
				return (e.X, e.Y);
		}
		return null;
	}

	public ItemInstance? ItemAt(int x, int y)
	{
		foreach (var (item, ix, iy) in _entries)
		{
			if (x >= ix && x < ix + item.Width && y >= iy && y < iy + item.Height)
				return item;
		}
		return null;
	}

	/// <summary>Whether the item fits at the cell using its current rotation (ignoring itself).</summary>
	public bool CanPlace(ItemInstance item, int x, int y)
	{
		if (x < 0 || y < 0 || x + item.Width > Width || y + item.Height > Height)
			return false;
		foreach (var (other, ox, oy) in _entries)
		{
			if (other == item)
				continue;
			var overlapX = x < ox + other.Width && ox < x + item.Width;
			var overlapY = y < oy + other.Height && oy < y + item.Height;
			if (overlapX && overlapY)
				return false;
		}
		return true;
	}

	public bool TryPlace(ItemInstance item, int x, int y)
	{
		if (!CanPlace(item, x, y))
			return false;
		_entries.RemoveAll(e => e.Item == item);
		_entries.Add((item, x, y));
		return true;
	}

	/// <summary>Finds the first free spot, trying the item's rotation then the rotated one.</summary>
	public bool TryPlaceAnywhere(ItemInstance item, bool allowRotate = true)
	{
		var original = item.Rotated;
		for (var pass = 0; pass < (allowRotate ? 2 : 1); pass++)
		{
			item.Rotated = pass == 0 ? original : !original;
			for (var y = 0; y <= Height - item.Height; y++)
			{
				for (var x = 0; x <= Width - item.Width; x++)
				{
					if (TryPlace(item, x, y))
						return true;
				}
			}
		}
		item.Rotated = original;
		return false;
	}

	public bool Remove(ItemInstance item) => _entries.RemoveAll(e => e.Item == item) > 0;

	/// <summary>
	/// Merges as much of a stackable item as fits into existing stacks of the same type.
	/// Returns what is left over (0 means fully merged; the item should then be discarded).
	/// </summary>
	public int MergeInto(ItemInstance item)
	{
		if (item.Spec.MaxStack <= 1)
			return item.Count;
		foreach (var other in Items)
		{
			if (other == item || other.Spec.Id != item.Spec.Id || other.Count >= other.Spec.MaxStack)
				continue;
			var moved = Math.Min(item.Count, other.Spec.MaxStack - other.Count);
			other.Count += moved;
			item.Count -= moved;
			if (item.Count == 0)
				break;
		}
		return item.Count;
	}
}
