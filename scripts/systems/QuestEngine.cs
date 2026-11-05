using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum TurnInFailure { None, NotReady, MissingItems, UnknownQuest }

/// <summary>
/// Quest rules (GDD s14, M7e): prerequisites, accepting, raid-event progress that survives death,
/// hand-in objectives checked against the stash and carried gear, rewards, tracking. Pure logic.
/// </summary>
public static class QuestEngine
{
	public const string SideQuartermaster = "side_qm";
	public const string OppositeQuartermaster = "opposite_qm";

	/// <summary>Resolves "side_qm" and "opposite_qm" to a trader id for the player's side.</summary>
	public static string? ResolveTrader(string id, PlayerSide? side)
	{
		if (id is not (SideQuartermaster or OppositeQuartermaster))
			return id;
		if (side is null)
			return null;
		var own = side == PlayerSide.Allied ? "allied_qm" : "soviet_qm";
		var other = side == PlayerSide.Allied ? "soviet_qm" : "allied_qm";
		return id == SideQuartermaster ? own : other;
	}

	// --- availability -----------------------------------------------------------------------

	/// <summary>Re-evaluates Locked and Available quests against the current profile (hub entry, after a turn-in).</summary>
	public static void Refresh(IReadOnlyList<QuestSpec> catalog, QuestLog log, GameProfile profile)
	{
		foreach (var spec in catalog)
		{
			var entry = log.Entry(spec);
			if (entry.State is QuestState.Locked or QuestState.Available)
				entry.State = PrerequisitesMet(spec, log, profile, catalog) ? QuestState.Available : QuestState.Locked;
		}
		log.NotifyChanged();
	}

	public static bool PrerequisitesMet(QuestSpec spec, QuestLog log, GameProfile profile, IReadOnlyList<QuestSpec> catalog)
	{
		if (spec.RequiresSide is { } side && profile.Side != side)
			return false;
		if (ResolveTrader(spec.GiverTraderId, profile.Side) is not { } giver)
			return false;
		if (profile.Trader(giver).Level < spec.MinTraderLevel)
			return false;
		if (spec.RequiresKnowledgeIds is { } knowledge && knowledge.Any(k => !profile.Knowledge.IsActive(k)))
			return false;
		foreach (var required in spec.RequiresQuestIds)
		{
			var other = catalog.FirstOrDefault(q => q.Id == required);
			if (other is null || log.StateOf(other) != QuestState.Completed)
				return false;
		}
		return true;
	}

	/// <summary>Whether the quest giver refuses to hand out new quests because of reputation.</summary>
	public static bool GiverRefuses(QuestSpec spec, GameProfile profile)
		=> ResolveTrader(spec.GiverTraderId, profile.Side) is { } giver && profile.Reputation.Refuses(giver);

	/// <summary>Accepts an available quest; with the profile, a giver who refuses you (reputation below -0.50) blocks it.</summary>
	public static bool Accept(QuestLog log, QuestSpec spec, GameProfile? profile = null)
	{
		var entry = log.Entry(spec);
		if (entry.State != QuestState.Available)
			return false;
		if (profile is not null && GiverRefuses(spec, profile))
			return false;
		entry.State = QuestState.Active;
		log.TrackedId = spec.Id; // the most recently accepted quest is tracked
		log.NotifyChanged();
		return true;
	}

	/// <summary>Tracks an accepted quest (hub Track button).</summary>
	public static bool Track(QuestLog log, QuestSpec spec)
	{
		if (log.StateOf(spec) is not (QuestState.Active or QuestState.ReadyToTurnIn))
			return false;
		log.TrackedId = spec.Id;
		log.NotifyChanged();
		return true;
	}

	// --- raid events ------------------------------------------------------------------------

	/// <summary>Applies a raid event to every active quest. Progress is kept on death or MIA.</summary>
	public static void OnEvent(IReadOnlyList<QuestSpec> catalog, QuestLog log, QuestEvent ev)
	{
		var changed = false;
		foreach (var spec in catalog)
		{
			var entry = log.Entry(spec);
			if (entry.State != QuestState.Active)
				continue;
			for (var i = 0; i < spec.Objectives.Count; i++)
			{
				var o = spec.Objectives[i];
				switch (o.Kind, ev)
				{
					case (ObjectiveKind.Eliminate, KilledEvent k)
						when entry.Progress[i] < o.Count
							&& (o.Faction is null || o.Faction == k.Faction)
							&& (o.WeaponClass is null || o.WeaponClass == k.WeaponClass)
							&& (o.MapId is null || o.MapId == k.MapId):
						entry.Progress[i]++;
						changed = true;
						break;
					case (ObjectiveKind.Locate, ZoneReachedEvent z)
						when entry.Progress[i] == 0 && o.ZoneId == z.ZoneId && (o.MapId is null || o.MapId == z.MapId):
						entry.Progress[i] = 1;
						changed = true;
						break;
					case (ObjectiveKind.Extract, ExtractedEvent x)
						when entry.Progress[i] == 0 && o.ZoneId == x.ZoneId && (o.MapId is null || o.MapId == x.MapId)
							&& (o.ItemId is null || x.CarriedItemIds.Contains(o.ItemId)):
						entry.Progress[i] = 1;
						changed = true;
						break;
				}
			}
			if (EventObjectivesDone(spec, entry) && !HasHandIn(spec))
				entry.State = QuestState.ReadyToTurnIn;
		}
		if (changed)
			log.NotifyChanged();
	}

	private static bool HasHandIn(QuestSpec spec)
		=> spec.Objectives.Any(o => o.Kind is ObjectiveKind.Fetch or ObjectiveKind.FindInRaid);

	private static bool EventObjectivesDone(QuestSpec spec, QuestEntry entry)
	{
		for (var i = 0; i < spec.Objectives.Count; i++)
		{
			var o = spec.Objectives[i];
			var done = o.Kind switch
			{
				ObjectiveKind.Eliminate => entry.Progress[i] >= o.Count,
				ObjectiveKind.Locate or ObjectiveKind.Extract => entry.Progress[i] >= 1,
				_ => true,
			};
			if (!done)
				return false;
		}
		return true;
	}

	// --- hand-in objectives -----------------------------------------------------------------

	private static IEnumerable<InventoryGrid> Grids(GameProfile profile, CharacterInventory character)
		=> new[] { profile.Stash }.Concat(character.AllGrids).SelectMany(WithNested);

	/// <summary>A grid and the grids of items inside it (a shard in a canister counts, M8e).</summary>
	private static IEnumerable<InventoryGrid> WithNested(InventoryGrid grid)
		=> new[] { grid }.Concat(grid.Items.SelectMany(i => i.Grids).SelectMany(WithNested));

	/// <summary>How many matching items are held in the stash and carried grids.</summary>
	public static int CountItems(GameProfile profile, CharacterInventory character, string itemId, bool foundInRaidOnly)
		=> Grids(profile, character).SelectMany(g => g.Items)
			.Where(i => i.Spec.Id == itemId && (!foundInRaidOnly || i.FoundInRaid))
			.Sum(i => Math.Max(1, i.Count));

	/// <summary>Updates the held counts of hand-in objectives and flips Active and ReadyToTurnIn accordingly (hub entry, stash change, trade).</summary>
	public static void Recount(IReadOnlyList<QuestSpec> catalog, QuestLog log, GameProfile profile, CharacterInventory character)
	{
		foreach (var spec in catalog)
		{
			var entry = log.Entry(spec);
			if (entry.State is not (QuestState.Active or QuestState.ReadyToTurnIn))
				continue;
			var handIn = false;
			var satisfied = true;
			for (var i = 0; i < spec.Objectives.Count; i++)
			{
				var o = spec.Objectives[i];
				if (o.Kind is ObjectiveKind.Fetch or ObjectiveKind.FindInRaid && o.ItemId is { } id)
				{
					handIn = true;
					entry.Progress[i] = CountItems(profile, character, id, o.Kind == ObjectiveKind.FindInRaid);
					satisfied &= entry.Progress[i] >= o.Count;
				}
			}
			if (!handIn)
				continue;
			entry.State = satisfied && EventObjectivesDone(spec, entry) ? QuestState.ReadyToTurnIn : QuestState.Active;
		}
		log.NotifyChanged();
	}

	/// <summary>Hands in the items, grants the rewards and completes the quest.</summary>
	public static TurnInFailure TurnIn(IReadOnlyList<QuestSpec> catalog, QuestLog log, GameProfile profile,
		CharacterInventory character, QuestSpec spec)
	{
		var entry = log.Entry(spec);
		if (entry.State != QuestState.ReadyToTurnIn)
			return TurnInFailure.NotReady;
		foreach (var o in spec.Objectives.Where(o => o.Kind is ObjectiveKind.Fetch or ObjectiveKind.FindInRaid))
		{
			if (o.ItemId is null || CountItems(profile, character, o.ItemId, o.Kind == ObjectiveKind.FindInRaid) < o.Count)
				return TurnInFailure.MissingItems;
		}
		foreach (var o in spec.Objectives.Where(o => o.Kind is ObjectiveKind.Fetch or ObjectiveKind.FindInRaid))
			Consume(profile, character, o.ItemId!, o.Count, o.Kind == ObjectiveKind.FindInRaid);

		foreach (var reward in spec.Rewards)
			Grant(log, profile, reward);
		entry.State = QuestState.Completed;
		if (log.TrackedId == spec.Id)
		{
			log.TrackedId = catalog.FirstOrDefault(q => log.StateOf(q) is QuestState.Active or QuestState.ReadyToTurnIn)?.Id;
		}
		Refresh(catalog, log, profile); // dependents may unlock now
		return TurnInFailure.None;
	}

	private static void Consume(GameProfile profile, CharacterInventory character, string itemId, int count, bool foundOnly)
	{
		var left = count;
		foreach (var grid in Grids(profile, character).ToList())
		{
			foreach (var stack in grid.Items.Where(i => i.Spec.Id == itemId && (!foundOnly || i.FoundInRaid)).ToList())
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

	public const string SideFaction = "side_faction";

	/// <summary>A reputation reward target: a trader id, "side_qm", "opposite_qm", "side_faction" (your own faction) or a faction id.</summary>
	public static string? ResolveReputationTarget(string target, PlayerSide? side)
		=> target == SideFaction ? (side is { } s ? ReputationBook.FactionId(s.ToFaction()) : null) : ResolveTrader(target, side);

	// --- rewards ----------------------------------------------------------------------------

	private static void Grant(QuestLog log, GameProfile profile, RewardSpec r)
	{
		switch (r.Kind)
		{
			case RewardKind.Money:
				log.AddPending(new PendingReward(MoneyLedger.MoneyId, r.Amount));
				break;
			case RewardKind.Item when r.ItemId is not null:
				log.AddPending(new PendingReward(r.ItemId, Math.Max(1, r.Amount)));
				break;
			case RewardKind.TraderLevel when r.TraderId is not null && ResolveTrader(r.TraderId, profile.Side) is { } trader:
				var state = profile.Trader(trader);
				state.Level = Math.Clamp(Math.Max(state.Level, r.Level), 1, TraderSpec.MaxLevel); // never lowers
				break;
			case RewardKind.UnlockTrader when r.TraderId is not null && ResolveTrader(r.TraderId, profile.Side) is { } unlocked:
				profile.UnlockTrader(unlocked);
				break;
			case RewardKind.Knowledge when r.ItemId is not null:
				profile.Knowledge.Unlock(r.ItemId);
				break;
			case RewardKind.Reputation when r.Target is not null && ResolveReputationTarget(r.Target, profile.Side) is { } target:
				log.AddReputation(target, r.Amount);
				break;
		}
	}

	/// <summary>
	/// Puts pending money and items into the stash. An entry that does not fit stays pending for the
	/// next try (hub entry or stash change); nothing is dropped.
	/// </summary>
	public static List<PendingReward> DeliverPending(QuestLog log, GameProfile profile, Func<string, int, ItemInstance?> make)
	{
		var delivered = new List<PendingReward>();
		foreach (var reward in log.Pending.ToList())
		{
			var items = StarterKit.Build(new[] { new StarterEntry(reward.ItemId, reward.Count) }, make);
			if (items.Count == 0)
			{
				log.RemovePending(reward); // unknown item id: nothing to give
				continue;
			}
			var placed = new List<ItemInstance>();
			var ok = true;
			foreach (var item in items)
			{
				if (profile.Stash.TryPlaceAnywhere(item))
				{
					placed.Add(item);
				}
				else
				{
					ok = false;
					break;
				}
			}
			if (!ok)
			{
				foreach (var p in placed)
					profile.Stash.Remove(p);
				continue;
			}
			log.RemovePending(reward);
			delivered.Add(reward);
		}
		if (delivered.Count > 0)
			log.NotifyChanged();
		return delivered;
	}

	// --- text -------------------------------------------------------------------------------

	/// <summary>The quest the HUD and the Tasks tab should show first, or null.</summary>
	public static QuestSpec? Tracked(IReadOnlyList<QuestSpec> catalog, QuestLog log)
		=> catalog.FirstOrDefault(q => q.Id == log.TrackedId
			&& log.StateOf(q) is QuestState.Active or QuestState.ReadyToTurnIn);
}
