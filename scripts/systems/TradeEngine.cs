using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum TradeFailure
{
	None, NotEnoughMoney, StashFull, NotBuyableHere, NotSellableHere, HasContents, MissingBarterItems,
	/// <summary>The trader dislikes you too much to deal (reputation below -0.50).</summary>
	Refused,
}

/// <summary>Outcome of a trade; <see cref="Money"/> is the price paid or the payout received.</summary>
public sealed record TradeResult(TradeFailure Failure, int Money = 0)
{
	public bool Ok => Failure == TradeFailure.None;
}

/// <summary>
/// Buy, sell and barter between a trader level, the stash and the carried character (GDD s13).
/// Pure logic: item specs, item creation and ammo values are injected. Every trade is all-or-nothing:
/// the result is placed or checked before anything is taken.
/// </summary>
public sealed class TradeEngine
{
	private readonly GameProfile _profile;
	private readonly CharacterInventory _character;
	private readonly Func<string, ItemSpec?> _spec;
	private readonly Func<string, int, ItemInstance?> _make;
	private readonly Func<string, int> _roundValue;

	/// <param name="roundValue">Value of one loaded round by ammo id (a magazine sells for its rounds too).</param>
	public TradeEngine(GameProfile profile, CharacterInventory character, Func<string, ItemSpec?> spec,
		Func<string, int, ItemInstance?> make, Func<string, int> roundValue)
	{
		_profile = profile;
		_character = character;
		_spec = spec;
		_make = make;
		_roundValue = roundValue;
	}

	/// <summary>Stash first, then everything carried (the hub carries no risk, so the secure pouch counts).</summary>
	private IReadOnlyList<InventoryGrid> MoneyGrids()
		=> new[] { _profile.Stash }.Concat(_character.AllGrids).ToList();

	public int Money => MoneyLedger.Count(MoneyGrids());

	// --- prices -----------------------------------------------------------------------------

	/// <summary>Buy price; with the trader's state the reputation factor applies (dearer when disliked), rounded up.</summary>
	public int BuyPrice(TraderLevelSpec level, ListingSpec listing, TraderState? trader = null)
	{
		var value = _spec(listing.ItemId)?.BaseValue ?? 0;
		return (int)Math.Ceiling(value * level.BuyMultiplier * listing.PriceMultiplier * BuyFactor(trader));
	}

	private float BuyFactor(TraderState? trader) => trader is { Id.Length: > 0 } ? _profile.Reputation.BuyFactor(trader.Id) : 1f;

	private float SellFactor(TraderState? trader) => trader is { Id.Length: > 0 } ? _profile.Reputation.SellFactor(trader.Id) : 1f;

	/// <summary>Whether this trader refuses to deal because of reputation.</summary>
	public bool Refuses(TraderState? trader) => trader is { Id.Length: > 0 } && _profile.Reputation.Refuses(trader.Id);

	/// <summary>A completed trade with the other side's quartermaster costs a little standing with your own faction.</summary>
	private void AfterTrade(TraderState trader)
	{
		if (trader.Id.Length > 0)
			_profile.Reputation.OnTrade(trader.Id, _profile.Side);
	}

	/// <summary>
	/// Value of an item as the trader sees it: base value, a magazine's rounds, a weapon's loaded
	/// magazine and chambered round, times the stack size.
	/// </summary>
	public int WorthOf(ItemInstance item)
	{
		var total = WeaponBuild.TotalValue(item) + item.Rounds.Sum(_roundValue);
		if (item.InsertedMagazine is { } mag)
			total += WorthOf(mag);
		if (item.Chambered is { } round)
			total += _roundValue(round);
		return total * Math.Max(1, item.Count);
	}

	/// <summary>Armor and helmets lose value with wear; everything else is worth full price.</summary>
	public static float Condition(ItemInstance item)
		=> item.Spec.MaxDurability > 0f && item.Durability >= 0f
			? Math.Clamp(item.Durability / item.Spec.MaxDurability, 0f, 1f)
			: 1f;

	/// <summary>Sell payout; with the trader's state the reputation factor applies, rounded down.</summary>
	public int SellPayout(TraderLevelSpec level, ItemInstance item, TraderState? trader = null)
		=> (int)Math.Floor(WorthOf(item) * level.SellFraction * Condition(item) * SellFactor(trader));

	/// <summary>Why this trader will not buy the item, or None.</summary>
	public TradeFailure CanSell(TraderLevelSpec level, ItemInstance item)
	{
		if (item.Spec.BaseValue <= 0 || !level.Buys.Contains(item.Spec.Category))
			return TradeFailure.NotSellableHere;
		if (item.Grids.Any(g => g.ItemCount > 0))
			return TradeFailure.HasContents;
		return TradeFailure.None;
	}

	// --- actions ----------------------------------------------------------------------------

	public TradeResult Buy(TraderLevelSpec level, TraderState state, ListingSpec listing, int count = 1)
	{
		if (Refuses(state))
			return new TradeResult(TradeFailure.Refused);
		if (!level.Sells.Contains(listing) || _spec(listing.ItemId) is null)
			return new TradeResult(TradeFailure.NotBuyableHere);
		var price = BuyPrice(level, listing, state) * Math.Max(1, count);
		if (Money < price)
			return new TradeResult(TradeFailure.NotEnoughMoney);
		if (!TryPlaceInStash(listing.ItemId, Math.Max(1, count)))
			return new TradeResult(TradeFailure.StashFull);
		MoneyLedger.Remove(MoneyGrids(), price);
		state.Spent += price;
		AfterTrade(state);
		return new TradeResult(TradeFailure.None, price);
	}

	public TradeResult Sell(TraderLevelSpec level, TraderState state, ItemInstance item)
	{
		if (Refuses(state))
			return new TradeResult(TradeFailure.Refused);
		var reason = CanSell(level, item);
		if (reason != TradeFailure.None)
			return new TradeResult(reason);
		var payout = SellPayout(level, item, state);
		var moneySpec = _spec(MoneyLedger.MoneyId);
		if (moneySpec is null)
			return new TradeResult(TradeFailure.NotSellableHere);
		// Place the banknotes first so a full stash never loses the item.
		var placed = new List<ItemInstance>();
		foreach (var amount in MoneyLedger.SplitStacks(payout, moneySpec.MaxStack))
		{
			var note = _make(MoneyLedger.MoneyId, amount);
			if (note is null || !_profile.Stash.TryPlaceAnywhere(note))
			{
				foreach (var p in placed)
					_profile.Stash.Remove(p);
				return new TradeResult(TradeFailure.StashFull);
			}
			placed.Add(note);
		}
		if (!RemoveEverywhere(item))
		{
			foreach (var p in placed)
				_profile.Stash.Remove(p);
			return new TradeResult(TradeFailure.NotSellableHere);
		}
		state.Spent += payout;
		AfterTrade(state);
		return new TradeResult(TradeFailure.None, payout);
	}

	public TradeResult Barter(TraderLevelSpec level, TraderState state, BarterSpec offer)
	{
		if (Refuses(state))
			return new TradeResult(TradeFailure.Refused);
		if (!level.Barters.Contains(offer))
			return new TradeResult(TradeFailure.NotBuyableHere);
		foreach (var (id, count) in offer.Give)
		{
			if (CountOf(id) < count)
				return new TradeResult(TradeFailure.MissingBarterItems);
		}
		if (!TryPlaceInStash(offer.GetId, offer.GetCount))
			return new TradeResult(TradeFailure.StashFull);
		foreach (var (id, count) in offer.Give)
			Consume(id, count);
		var value = (_spec(offer.GetId)?.BaseValue ?? 0) * offer.GetCount;
		state.Spent += value;
		AfterTrade(state);
		return new TradeResult(TradeFailure.None, value);
	}

	/// <summary>Items a trader level would buy right now, from the stash and the character.</summary>
	public IEnumerable<ItemInstance> SellableItems(TraderLevelSpec level)
		=> OwnedItems().Where(i => CanSell(level, i) == TradeFailure.None);

	// --- helpers ----------------------------------------------------------------------------

	/// <summary>Every item sitting in a grid or slot, stash first (a weapon's magazine is not listed on its own).</summary>
	public IEnumerable<ItemInstance> OwnedItems()
	{
		foreach (var item in Walk(_profile.Stash.Items))
			yield return item;
		foreach (var slotItem in _character.EquippedItems.Select(kv => kv.Value).ToList())
		{
			yield return slotItem;
			foreach (var nested in Walk(slotItem.Grids.SelectMany(g => g.Items)))
				yield return nested;
		}
		foreach (var pocket in _character.Pockets)
		{
			foreach (var item in Walk(pocket.Items))
				yield return item;
		}
	}

	private static IEnumerable<ItemInstance> Walk(IEnumerable<ItemInstance> items)
	{
		foreach (var item in items.ToList())
		{
			yield return item;
			foreach (var nested in Walk(item.Grids.SelectMany(g => g.Items)))
				yield return nested;
		}
	}

	private bool TryPlaceInStash(string itemId, int count)
	{
		var placed = new List<ItemInstance>();
		var built = StarterKit.Build(new[] { new StarterEntry(itemId, count) }, _make);
		foreach (var item in built)
		{
			if (!_profile.Stash.TryPlaceAnywhere(item))
			{
				foreach (var p in placed)
					_profile.Stash.Remove(p);
				return false;
			}
			placed.Add(item);
		}
		return built.Count > 0;
	}

	private IEnumerable<InventoryGrid> BarterGrids()
		=> new[] { _profile.Stash }.Concat(_character.AllGrids);

	private int CountOf(string id)
		=> BarterGrids().SelectMany(g => g.Items).Where(i => i.Spec.Id == id).Sum(i => Math.Max(1, i.Count));

	private void Consume(string id, int count)
	{
		var left = count;
		foreach (var grid in BarterGrids().ToList())
		{
			foreach (var stack in grid.Items.Where(i => i.Spec.Id == id).ToList())
			{
				var take = Math.Min(left, Math.Max(1, stack.Count));
				stack.Count -= take;
				left -= take;
				if (stack.Count <= 0)
					grid.Remove(stack);
				if (left == 0)
					return;
			}
		}
	}

	/// <summary>Removes an item wherever it is: stash, a container in the stash, or the character.</summary>
	private bool RemoveEverywhere(ItemInstance item)
	{
		if (_profile.Stash.Remove(item))
			return true;
		foreach (var owner in Walk(_profile.Stash.Items))
		{
			foreach (var grid in owner.Grids)
			{
				if (grid.Remove(item))
					return true;
			}
		}
		return _character.Remove(item);
	}
}
