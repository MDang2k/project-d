using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum ObjectiveKind { Fetch, FindInRaid, Eliminate, Locate, Extract }

/// <summary>One thing a quest asks for (GDD s14). Unused filters are null.</summary>
public sealed record ObjectiveSpec(
	ObjectiveKind Kind,
	int Count = 1,
	/// <summary>Fetch, FindInRaid: the item id; Extract: optionally an item that must be carried out.</summary>
	string? ItemId = null,
	/// <summary>Eliminate: only kills of this faction count.</summary>
	Faction? Faction = null,
	string? MapId = null,
	/// <summary>Eliminate: only kills made with this weapon class (primary, pistol, melee).</summary>
	WeaponClass? WeaponClass = null,
	/// <summary>Locate: the quest zone to reach; Extract: the extraction zone to use.</summary>
	string? ZoneId = null);

public enum RewardKind { Money, Item, TraderLevel, UnlockTrader, Reputation }

/// <summary>
/// What completing a quest gives. TraderId may be "side_qm" (your side's quartermaster) or
/// "opposite_qm" (the other one), resolved when the reward is granted.
/// </summary>
public sealed record RewardSpec(
	RewardKind Kind,
	int Amount = 0,
	string? ItemId = null,
	string? TraderId = null,
	int Level = 0,
	string? Target = null);

public sealed record QuestSpec(
	string Id,
	string TitleKey,
	string DescriptionKey,
	/// <summary>Trader id, or "side_qm" for your side's quartermaster.</summary>
	string GiverTraderId,
	int MinTraderLevel,
	IReadOnlyList<string> RequiresQuestIds,
	PlayerSide? RequiresSide,
	IReadOnlyList<ObjectiveSpec> Objectives,
	IReadOnlyList<RewardSpec> Rewards);

public enum QuestState { Locked, Available, Active, ReadyToTurnIn, Completed }

/// <summary>State and per-objective progress of one quest.</summary>
public sealed class QuestEntry
{
	public QuestEntry(int objectives) => Progress = new int[objectives];

	public QuestState State { get; set; } = QuestState.Locked;

	/// <summary>Eliminate: kills; Locate and Extract: 0 or 1; Fetch and FindInRaid: items currently held (informational).</summary>
	public int[] Progress { get; set; }
}

/// <summary>A reward that did not fit in the stash yet (same rule as insurance returns).</summary>
public sealed record PendingReward(string ItemId, int Count);

/// <summary>The player's quest progress (kept in the profile and the save).</summary>
public sealed class QuestLog
{
	private readonly Dictionary<string, QuestEntry> _entries = new();
	private readonly List<PendingReward> _pending = new();
	private readonly Dictionary<string, int> _reputation = new();

	public string? TrackedId { get; set; }

	public IReadOnlyDictionary<string, QuestEntry> Entries => _entries;
	public IReadOnlyList<PendingReward> Pending => _pending;

	/// <summary>Reputation in hundredths (-100 to 100) per trader and faction; <see cref="ReputationBook"/> reads it.</summary>
	public IReadOnlyDictionary<string, int> Reputation => _reputation;

	public event Action? Changed;

	public QuestEntry Entry(QuestSpec spec)
	{
		if (!_entries.TryGetValue(spec.Id, out var e) || e.Progress.Length != spec.Objectives.Count)
			_entries[spec.Id] = e = new QuestEntry(spec.Objectives.Count) { State = e?.State ?? QuestState.Locked };
		return e;
	}

	public QuestState StateOf(QuestSpec spec) => Entry(spec).State;

	public void SetEntry(string id, QuestEntry entry) => _entries[id] = entry;

	public void AddPending(PendingReward reward) => _pending.Add(reward);

	public bool RemovePending(PendingReward reward) => _pending.Remove(reward);

	public void AddReputation(string target, int delta)
		=> _reputation[target] = Math.Clamp(_reputation.GetValueOrDefault(target) + delta, ReputationBook.Min, ReputationBook.Max);

	public void SetReputation(string target, int value) => _reputation[target] = Math.Clamp(value, ReputationBook.Min, ReputationBook.Max);

	public void NotifyChanged() => Changed?.Invoke();

	public void Clear()
	{
		_entries.Clear();
		_pending.Clear();
		_reputation.Clear();
		TrackedId = null;
	}
}

/// <summary>Raid events the quest engine reacts to; thin nodes raise them.</summary>
public abstract record QuestEvent;

public sealed record KilledEvent(Faction Faction, WeaponClass WeaponClass, string MapId) : QuestEvent;

public sealed record ZoneReachedEvent(string ZoneId, string MapId) : QuestEvent;

public sealed record ExtractedEvent(string ZoneId, IReadOnlyCollection<string> CarriedItemIds, string MapId) : QuestEvent;
