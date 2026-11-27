using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>Per-trader progress (v3).</summary>
public sealed record TraderSave(int Level = 1, long Spent = 0);

/// <summary>Trader levels and spending ("traders") and the traders opened by quests ("unlockedTraders").</summary>
public sealed class TradersPart(GameProfile profile) : ISavePart
{
	public void Write(JsonObject root)
	{
		SaveJson.Set(root, "traders", profile.Traders.ToDictionary(kv => kv.Key, kv => new TraderSave(kv.Value.Level, kv.Value.Spent)));
		SaveJson.Set(root, "unlockedTraders", profile.UnlockedTraders.ToList());
	}

	public void Read(JsonObject root, LoadContext ctx)
	{
		var traders = SaveJson.Get<Dictionary<string, TraderSave>>(root, "traders") ?? new();
		profile.ReplaceTraders(traders.Select(kv => new KeyValuePair<string, TraderState>(kv.Key,
			new TraderState { Level = Math.Clamp(kv.Value.Level, 1, TraderSpec.MaxLevel), Spent = Math.Max(0, kv.Value.Spent) })));
		foreach (var id in SaveJson.Get<List<string>>(root, "unlockedTraders") ?? new())
			profile.UnlockTrader(id);
	}
}
