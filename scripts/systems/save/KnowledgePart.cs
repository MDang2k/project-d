using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>The state of one piece of knowledge (v10): unlocked, translating or translated, and the raids a translation has left.</summary>
public sealed record KnowledgeSave(string State, int RaidsLeft);

/// <summary>Knowledge by id ("knowledge", v10).</summary>
public sealed class KnowledgePart(KnowledgeBook book) : ISavePart
{
	public void Write(JsonObject root)
		=> SaveJson.Set(root, "knowledge", book.Snapshot().ToDictionary(kv => kv.Key, kv => new KnowledgeSave(kv.Value.State.ToString(), kv.Value.RaidsLeft)));

	public void Read(JsonObject root, LoadContext ctx)
	{
		var saved = SaveJson.Get<Dictionary<string, KnowledgeSave>>(root, "knowledge") ?? new();
		book.Load(saved.Select(kv => new KeyValuePair<string, KnowledgeEntry>(kv.Key, new KnowledgeEntry(
			Enum.TryParse<KnowledgeState>(kv.Value.State, out var state) ? state : KnowledgeState.Unlocked,
			Math.Max(0, kv.Value.RaidsLeft)))));
	}
}
