using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ProjectD;

/// <summary>An actor after inheritance: every blank cell filled from the parent chain (composable actors spec s2).</summary>
public sealed record ActorDef(
	string Id,
	string Name,
	string Faction,
	IReadOnlyList<string> Tags,
	IReadOnlyList<string> Sets,
	IReadOnlyDictionary<string, IReadOnlyList<string>> Props,
	IReadOnlyDictionary<string, float> Stats,
	IReadOnlyDictionary<string, string> Links)
{
	public IReadOnlyList<string> List(string prop) => Props.TryGetValue(prop, out var v) ? v : Array.Empty<string>();
	public string Text(string prop) => List(prop).FirstOrDefault() ?? "";
	public bool Flag(string prop) => string.Equals(Text(prop), "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A live actor: its resolved definition and its stats with the modifier sets already applied.</summary>
public sealed record ActorInstance(ActorDef Def, StatSet Stats);

/// <summary>
/// The actors exported from the catalog sheets (data/actors/actors.json). Pure logic: parse, resolve parents,
/// build <see cref="StatSet"/>s. Child cells override the parent; tags and modifier sets add up along the chain.
/// </summary>
public sealed class ActorCatalog
{
	private readonly Dictionary<string, ActorDef> _actors = new();
	private readonly Dictionary<string, List<StatModifier>> _sets = new();

	public IEnumerable<ActorDef> Actors => _actors.Values;
	public IReadOnlyCollection<string> SetNames => _sets.Keys;

	public ActorDef Get(string id) => _actors.TryGetValue(id, out var d) ? d : throw new ArgumentException($"unknown actor '{id}'");
	public bool Has(string id) => _actors.ContainsKey(id);

	/// <summary>The modifiers of a set, tagged with the set name as their source.</summary>
	public IReadOnlyList<StatModifier> Set(string name)
		=> _sets.TryGetValue(name, out var s) ? s : throw new ArgumentException($"unknown modifier set '{name}'");

	/// <summary>Stats at their resolved bases with the actor's own modifier sets applied.</summary>
	public ActorInstance Create(string id, IEnumerable<string>? extraSets = null)
	{
		var def = Get(id);
		var stats = new StatSet(def.Stats);
		foreach (var name in def.Sets.Concat(extraSets ?? Array.Empty<string>()))
			stats.AddRange(Set(name));
		return new ActorInstance(def, stats);
	}

	/// <summary>
	/// Create with the modifier sets a spawner picks by tier and act: <c>tier_N</c> and <c>act_N</c> are added
	/// when the sheet defines them, so an unused tier or act costs nothing.
	/// </summary>
	public ActorInstance Create(string id, int? tier, int? act)
	{
		var extra = new List<string>();
		if (tier is { } t && _sets.ContainsKey($"tier_{t}"))
			extra.Add($"tier_{t}");
		if (act is { } a && _sets.ContainsKey($"act_{a}"))
			extra.Add($"act_{a}");
		return Create(id, extra);
	}

	public static ActorCatalog Parse(string json)
	{
		var catalog = new ActorCatalog();
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;

		foreach (var set in root.GetProperty("modifier_sets").EnumerateObject())
			catalog._sets[set.Name] = set.Value.EnumerateArray()
				.Select(m => new StatModifier(
					Stats.Get(m.GetProperty("stat").GetString()!).Id,
					StatModifier.ParseOp(m.GetProperty("op").GetString()!),
					m.GetProperty("value").GetSingle(),
					set.Name))
				.ToList();

		var raw = root.GetProperty("actors").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString()!);
		foreach (var (id, el) in raw)
		{
			if (el.GetProperty("kind").GetString() == "template")
				continue;
			catalog._actors[id] = Resolve(id, raw, catalog._sets);
		}
		return catalog;
	}

	private static ActorDef Resolve(string id, Dictionary<string, JsonElement> raw, Dictionary<string, List<StatModifier>> sets)
	{
		var chain = new List<JsonElement>();
		var seen = new HashSet<string>();
		for (var cur = id; cur != ""; )
		{
			if (!seen.Add(cur))
				throw new ArgumentException($"actor '{id}': parent cycle at '{cur}'");
			if (!raw.TryGetValue(cur, out var el))
				throw new ArgumentException($"actor '{id}': unknown parent '{cur}'");
			chain.Insert(0, el); // root first, so later (child) cells win
			cur = el.GetProperty("parent").GetString() ?? "";
		}

		string name = "", faction = "";
		var tags = new List<string>();
		var setNames = new List<string>();
		var props = new Dictionary<string, IReadOnlyList<string>>();
		var stats = new Dictionary<string, float>();
		var links = new Dictionary<string, string>();
		foreach (var el in chain)
		{
			if (el.GetProperty("name").GetString() is { Length: > 0 } n) name = n;
			if (el.GetProperty("faction").GetString() is { Length: > 0 } f) faction = f;
			foreach (var t in el.GetProperty("tags").EnumerateArray())
				if (!tags.Contains(t.GetString()!)) tags.Add(t.GetString()!);
			foreach (var s in el.GetProperty("sets").EnumerateArray())
			{
				var sn = s.GetString()!;
				if (!sets.ContainsKey(sn))
					throw new ArgumentException($"actor '{id}': unknown modifier set '{sn}'");
				if (!setNames.Contains(sn)) setNames.Add(sn);
			}
			foreach (var p in el.GetProperty("props").EnumerateObject())
				props[p.Name] = p.Value.ValueKind == JsonValueKind.Array
					? p.Value.EnumerateArray().Select(x => x.GetString()!).ToList()
					: new[] { p.Value.GetString()! };
			foreach (var s in el.GetProperty("stats").EnumerateObject())
				stats[Stats.Get(s.Name).Id] = s.Value.GetSingle();
			foreach (var l in el.GetProperty("links").EnumerateObject())
				links[l.Name] = l.Value.GetString()!;
		}
		return new ActorDef(id, name, faction, tags, setNames, props, stats, links);
	}
}
