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
	List<GridSave>? Grids = null,
	/// <summary>Insured for the next raid, and by whom (v4).</summary>
	bool Insured = false,
	string? InsuredWith = null,
	/// <summary>Taken from a container or body during a raid (v5).</summary>
	bool FoundInRaid = false);

public sealed record PlacedItemSave(int X, int Y, ItemSave Item);

public sealed record GridSave(int Width, int Height, List<PlacedItemSave> Items);

/// <summary>Equipment by <see cref="EquipSlot"/> name, plus the pocket grids.</summary>
public sealed record CharacterSave(Dictionary<string, ItemSave> Slots, List<GridSave> Pockets);

/// <summary>Quest state by name and per-objective progress (v5).</summary>
public sealed record QuestSave(string State, List<int> Progress);

/// <summary>A reward waiting for stash space (v5).</summary>
public sealed record PendingRewardSave(string ItemId, int Count);

/// <summary>An insured item waiting for delivery (v4).</summary>
public sealed record InsuranceReturnSave(ItemSave Item, int RaidsRemaining);

/// <summary>Per-trader progress (v3).</summary>
public sealed record TraderSave(int Level = 1, long Spent = 0);

public sealed record CountersSave(int Raids, int Extractions, int Deaths, int Mia);

/// <summary>The single autosave (GDD s15). Bump <see cref="CurrentVersion"/> with a migration when the shape changes.</summary>
public sealed record SaveFile(
	int Version,
	bool RaidInProgress,
	CountersSave Counters,
	List<string?> QuickUse,
	CharacterSave? Character,
	/// <summary>"Allied", "Soviet" or null until chosen (v2).</summary>
	string? Side = null,
	GridSave? Stash = null,
	Dictionary<string, TraderSave>? Traders = null,
	List<InsuranceReturnSave>? InsuranceReturns = null,
	Dictionary<string, QuestSave>? Quests = null,
	string? Tracked = null,
	List<string>? UnlockedTraders = null,
	List<PendingRewardSave>? PendingRewards = null,
	Dictionary<string, int>? Reputation = null)
{
	public const int CurrentVersion = 5;
}
