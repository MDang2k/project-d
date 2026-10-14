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
