using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>A returned item waiting to be delivered to the stash after the raid that follows the loss.</summary>
public sealed class PendingReturn
{
	public PendingReturn(ItemInstance item, int raidsRemaining)
	{
		Item = item;
		RaidsRemaining = raidsRemaining;
	}

	public ItemInstance Item { get; }
	public int RaidsRemaining { get; set; }
}

public enum InsureFailure { None, NotInsurable, AlreadyInsured, NotEnoughMoney, NotCarried, TraderLocked }

public sealed record InsureResult(InsureFailure Failure, int Premium = 0)
{
	public bool Ok => Failure == InsureFailure.None;
}

/// <summary>What the insurance roll did at the end of a raid. The nemesis (M8) takes only NotReturned.</summary>
public sealed record InsuranceRaidResult(List<ItemInstance> Returned, List<ItemInstance> NotReturned)
{
	public static InsuranceRaidResult Empty => new(new List<ItemInstance>(), new List<ItemInstance>());
}

/// <summary>The persisted queue of insured returns (GDD s15).</summary>
public sealed class InsuranceQueue
{
	private readonly List<PendingReturn> _entries = new();

	public IReadOnlyList<PendingReturn> Entries => _entries;

	public int Count => _entries.Count;

	public void Add(PendingReturn entry) => _entries.Add(entry);

	public bool Remove(PendingReturn entry) => _entries.Remove(entry);

	public void Clear() => _entries.Clear();
}

/// <summary>
/// Insurance (GDD s15, M7d): buy per raid from your quartermaster, roll each insured lost item at
/// the end of a death or MIA raid, queue returns and deliver them to the stash after the next raid.
/// Pure logic; random numbers are injected.
/// </summary>
public static class InsuranceService
{
	// --- buying -----------------------------------------------------------------------------

	/// <summary>Carried, insurable items: equipped, pockets, and grids of worn gear except the secure container.</summary>
	public static IEnumerable<ItemInstance> InsurableItems(CharacterInventory character)
		=> CarriedItems(character).Where(i => i.Spec.Insurable && i.Spec.BaseValue > 0);

	/// <summary>Everything carried that insurance can apply to, secure container and its contents excluded.</summary>
	public static IEnumerable<ItemInstance> CarriedItems(CharacterInventory character)
	{
		foreach (var kv in character.EquippedItems.ToList())
		{
			if (kv.Key == EquipSlot.SecureContainer)
				continue;
			foreach (var i in WithNested(kv.Value))
				yield return i;
		}
		foreach (var pocket in character.Pockets)
		{
			foreach (var item in pocket.Items.ToList())
			{
				foreach (var i in WithNested(item))
					yield return i;
			}
		}
	}

	/// <summary>An item, its inserted magazine, and everything inside its grids, depth first.</summary>
	public static IEnumerable<ItemInstance> WithNested(ItemInstance item)
	{
		yield return item;
		if (item.InsertedMagazine is { } mag)
		{
			foreach (var i in WithNested(mag))
				yield return i;
		}
		foreach (var grid in item.Grids)
		{
			foreach (var nested in grid.Items.ToList())
			{
				foreach (var i in WithNested(nested))
					yield return i;
			}
		}
	}

	public static int Premium(InsuranceTerms terms, ItemInstance item)
		=> (int)Math.Ceiling(WeaponBuild.TotalValue(item) * terms.PremiumFraction); // a weapon is insured with its parts

	public static InsureResult Insure(GameProfile profile, CharacterInventory character, TraderSpec trader,
		ItemInstance item)
	{
		if (!profile.IsTraderAvailable(trader) || trader.LevelSpec(profile.Trader(trader.Id).Level).Insurance is not { } terms)
			return new InsureResult(InsureFailure.TraderLocked);
		if (!item.Spec.Insurable || item.Spec.BaseValue <= 0)
			return new InsureResult(InsureFailure.NotInsurable);
		if (item.Insured)
			return new InsureResult(InsureFailure.AlreadyInsured);
		if (!CarriedItems(character).Contains(item))
			return new InsureResult(InsureFailure.NotCarried);
		var premium = Premium(terms, item);
		var grids = new[] { profile.Stash }.Concat(character.AllGrids).ToList();
		if (MoneyLedger.Count(grids) < premium)
			return new InsureResult(InsureFailure.NotEnoughMoney);
		MoneyLedger.Remove(grids, premium);
		item.Insured = true;
		item.InsuredWith = trader.Id;
		profile.Trader(trader.Id).Spent += premium;
		return new InsureResult(InsureFailure.None, premium);
	}

	// --- raid end ---------------------------------------------------------------------------

	/// <summary>
	/// Runs at the end of every raid, including the crash-recovery path. Order: age the queue, roll lost
	/// insured items (death or MIA only), then clear every insurance flag.
	/// </summary>
	public static InsuranceRaidResult OnRaidEnd(RaidOutcome outcome, IReadOnlyList<ItemInstance> lost,
		GameProfile profile, CharacterInventory? character, Func<string, TraderLevelSpec?> levelOf, Func<double> random)
	{
		// 1. entries from earlier raids come one raid closer
		foreach (var entry in profile.InsuranceReturns.Entries)
			entry.RaidsRemaining = Math.Max(0, entry.RaidsRemaining - 1);

		var result = InsuranceRaidResult.Empty;
		if (outcome is RaidOutcome.Killed or RaidOutcome.Mia)
		{
			foreach (var top in lost)
			{
				foreach (var item in WithNested(top).ToList())
				{
					if (!item.Insured)
						continue;
					var chance = item.InsuredWith is { } id ? levelOf(id)?.Insurance?.ReturnChance ?? 0f : 0f;
					if (random() < chance)
					{
						var copy = StrippedCopy(item);
						result.Returned.Add(copy);
						profile.InsuranceReturns.Add(new PendingReturn(copy, 1));
					}
					else
					{
						result.NotReturned.Add(item);
					}
				}
			}
		}

		// 4. unused insurance is wasted, everywhere
		ClearFlags(character, profile);
		return result;
	}

	/// <summary>The returned copy: same spec and wear, no contents, no magazine, no rounds, uninsured.</summary>
	public static ItemInstance StrippedCopy(ItemInstance item)
	{
		var copy = ItemInstance.Create(item.Spec, 1);
		copy.Durability = item.Durability;
		copy.FoundInRaid = item.FoundInRaid;
		WeaponBuild.CopyParts(item, copy); // the weapon returns with its build, but not its magazine or rounds
		return copy;
	}

	public static void ClearFlags(CharacterInventory? character, GameProfile profile)
	{
		if (character is not null)
		{
			foreach (var i in character.AllItems().SelectMany(WithNested).ToList())
				Clear(i);
		}
		foreach (var i in profile.Stash.Items.ToList().SelectMany(WithNested).ToList())
			Clear(i);
	}

	private static void Clear(ItemInstance item)
	{
		item.Insured = false;
		item.InsuredWith = null;
	}

	// --- delivery ---------------------------------------------------------------------------

	/// <summary>
	/// Delivers every entry that is due into the stash. A full stash keeps the entry queued for the next
	/// try (hub entry or stash change). Returns the delivered items.
	/// </summary>
	public static List<ItemInstance> TryDeliver(GameProfile profile)
	{
		var delivered = new List<ItemInstance>();
		foreach (var entry in profile.InsuranceReturns.Entries.Where(e => e.RaidsRemaining <= 0).ToList())
		{
			if (!profile.Stash.TryPlaceAnywhere(entry.Item))
				continue;
			profile.InsuranceReturns.Remove(entry);
			delivered.Add(entry.Item);
		}
		return delivered;
	}
}
