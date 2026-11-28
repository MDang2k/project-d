using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>Quest state by name and per-objective progress (v5).</summary>
public sealed record QuestSave(string State, List<int> Progress);

/// <summary>A reward waiting for stash space (v5).</summary>
public sealed record PendingRewardSave(string ItemId, int Count);

/// <summary>The quest log: "quests", "tracked", "pendingRewards" and "reputation", which lives in the log.</summary>
public sealed class QuestsPart(QuestLog log) : ISavePart
{
	public void Write(JsonObject root)
	{
		SaveJson.Set(root, "quests", log.Entries.ToDictionary(kv => kv.Key, kv => new QuestSave(kv.Value.State.ToString(), kv.Value.Progress.ToList())));
		SaveJson.Set(root, "tracked", log.TrackedId);
		SaveJson.Set(root, "pendingRewards", log.Pending.Select(p => new PendingRewardSave(p.ItemId, p.Count)).ToList());
		SaveJson.Set(root, "reputation", log.Reputation.ToDictionary(kv => kv.Key, kv => kv.Value));
	}

	public void Read(JsonObject root, LoadContext ctx)
	{
		foreach (var (id, q) in SaveJson.Get<Dictionary<string, QuestSave>>(root, "quests") ?? new())
		{
			var state = Enum.TryParse<QuestState>(q.State, out var parsed) ? parsed : QuestState.Locked;
			log.SetEntry(id, new QuestEntry(q.Progress?.Count ?? 0) { State = state, Progress = q.Progress?.ToArray() ?? Array.Empty<int>() });
		}
		log.TrackedId = SaveJson.Get<string>(root, "tracked");
		foreach (var pending in SaveJson.Get<List<PendingRewardSave>>(root, "pendingRewards") ?? new())
			log.AddPending(new PendingReward(pending.ItemId, pending.Count));
		foreach (var (target, value) in SaveJson.Get<Dictionary<string, int>>(root, "reputation") ?? new())
			log.SetReputation(target, value);
	}
}
