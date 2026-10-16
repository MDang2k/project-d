using System.Collections.Generic;

namespace ProjectD;

/// <summary>Who may use a trader (GDD s13): always, only your side's quartermaster, or locked until a quest opens it.</summary>
public enum TraderUnlock { Always, ForSide, Locked }

/// <summary>One thing a trader sells; the multiplier scales the level's buy multiplier for this item.</summary>
public sealed record ListingSpec(string ItemId, float PriceMultiplier = 1f);

/// <summary>A barter: hand over these items, get one item (stacks count).</summary>
public sealed record BarterSpec(IReadOnlyList<(string ItemId, int Count)> Give, string GetId, int GetCount = 1);

/// <summary>Insurance terms of a quartermaster level (GDD s15), used by the insurance service (M7d).</summary>
public sealed record InsuranceTerms(float PremiumFraction, float ReturnChance);

/// <summary>One trader level: catalog, what the trader buys and the price numbers.</summary>
public sealed record TraderLevelSpec(
	IReadOnlyList<ListingSpec> Sells,
	IReadOnlySet<ItemCategory> Buys,
	/// <summary>Buy price = ceil(BaseValue x this).</summary>
	float BuyMultiplier,
	/// <summary>Sell payout = floor(BaseValue x this x condition).</summary>
	float SellFraction,
	IReadOnlyList<BarterSpec> Barters,
	InsuranceTerms? Insurance = null);

/// <summary>A trader with four levels (only level 1 is reachable in the slice until quests raise it).</summary>
public sealed record TraderSpec(
	string Id,
	string NameKey,
	TraderUnlock Unlock,
	/// <summary>For <see cref="TraderUnlock.ForSide"/>: the side this quartermaster serves.</summary>
	PlayerSide? UnlockSide,
	IReadOnlyList<TraderLevelSpec> Levels)
{
	public const int MaxLevel = 4;

	public TraderLevelSpec LevelSpec(int level) => Levels[System.Math.Clamp(level, 1, Levels.Count) - 1];
}

/// <summary>Per-trader progress kept in the profile and the save.</summary>
public sealed class TraderState
{
	public int Level { get; set; } = 1;

	/// <summary>Money traded with this trader in total (drives level-ups later).</summary>
	public long Spent { get; set; }
}
