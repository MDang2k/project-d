using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ProjectD;

/// <summary>First names, last names and nicknames of one faction.</summary>
public sealed record NameLists(IReadOnlyList<string> First, IReadOnlyList<string> Last, IReadOnlyList<string> Nicknames);

/// <summary>
/// The name lists of data/ai/nemesis_names.json, one per faction id (allied_pmc, soviet_pmc, bandits, looters,
/// deserters). Generates a name and a nickname for a new nemesis. Pure logic.
/// </summary>
public sealed class NemesisNames
{
	private readonly Dictionary<string, NameLists> _lists;

	private NemesisNames(Dictionary<string, NameLists> lists) => _lists = lists;

	public static string KeyOf(Faction faction) => faction switch
	{
		Faction.AlliedPmc => "allied_pmc",
		Faction.SovietPmc => "soviet_pmc",
		Faction.Bandits => "bandits",
		Faction.Deserters => "deserters",
		_ => "looters",
	};

	public static NemesisNames Parse(string json)
	{
		using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
		var lists = new Dictionary<string, NameLists>();
		foreach (var faction in doc.RootElement.EnumerateObject())
		{
			IReadOnlyList<string> Read(string key) => faction.Value.TryGetProperty(key, out var a)
				? a.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s != "").ToList()
				: new List<string>();
			lists[faction.Name] = new NameLists(Read("first"), Read("last"), Read("nicknames"));
		}
		return new NemesisNames(lists);
	}

	public bool Has(string factionKey) => _lists.ContainsKey(factionKey);

	public IEnumerable<string> Factions => _lists.Keys;

	public NameLists Lists(string factionKey) => _lists[factionKey];

	/// <summary>A name and a nickname for a faction; falls back to the looters' lists, then to a plain label, so it never throws.</summary>
	public (string Name, string Nickname) Generate(Faction faction, Random rng)
	{
		var lists = _lists.GetValueOrDefault(KeyOf(faction)) ?? _lists.GetValueOrDefault("looters");
		if (lists is null)
			return ("Unknown", "the Survivor");
		string Pick(IReadOnlyList<string> l, string fallback) => l.Count == 0 ? fallback : l[rng.Next(l.Count)];
		return ($"{Pick(lists.First, "Unknown")} {Pick(lists.Last, "Unnamed")}", Pick(lists.Nicknames, "the Survivor"));
	}
}
