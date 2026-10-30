using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ProjectD;

/// <summary>One row of the Faction Loadouts sheet that the game can use: a game item id and its weight.</summary>
public sealed record LoadoutEntry(string Item, float Weight);

/// <summary>What an AI is dealt: weapons to choose from, armor pieces, rig and backpack (empty string for none).</summary>
public sealed record ResolvedLoadout(
	IReadOnlyList<string> Weapons,
	IReadOnlyList<string> Armor,
	string Rig,
	string Backpack);

/// <summary>
/// The Faction Loadouts rows (faction, tier, slot, item, weight) exported to actors.json, and the rule that turns
/// them into a loadout (composable actors spec s2.4): cells filled on the actor row win, blank slots are rolled
/// from the table of the actor's loadout faction and tier.
/// </summary>
public sealed class LoadoutTable
{
	private readonly Dictionary<(string Faction, int Tier, string Slot), List<LoadoutEntry>> _rows = new();

	public static LoadoutTable Parse(JsonElement loadouts)
	{
		var table = new LoadoutTable();
		foreach (var faction in loadouts.EnumerateObject())
			foreach (var tier in faction.Value.EnumerateObject())
				foreach (var slot in tier.Value.EnumerateObject())
					table._rows[(Key(faction.Name), int.Parse(tier.Name), slot.Name)] = slot.Value.EnumerateArray()
						.Select(e => new LoadoutEntry(e.GetProperty("item").GetString()!, e.GetProperty("weight").GetSingle()))
						.ToList();
		return table;
	}

	private static string Key(string faction) => faction.Replace(" ", "").Replace("_", "").ToLowerInvariant();

	/// <summary>Rows for a slot at the tier, else at the nearest lower tier that has any.</summary>
	public IReadOnlyList<LoadoutEntry> Rows(string faction, int tier, string slot)
	{
		for (var t = tier; t >= 1; t--)
			if (_rows.TryGetValue((Key(faction), t, slot), out var rows))
				return rows;
		return Array.Empty<LoadoutEntry>();
	}

	/// <summary>Weighted pick; null when the table has no usable row for the slot.</summary>
	public string? Pick(string faction, int tier, string slot, Random rng)
	{
		var rows = Rows(faction, tier, slot);
		if (rows.Count == 0)
			return null;
		var roll = rng.NextDouble() * rows.Sum(r => r.Weight);
		foreach (var r in rows)
		{
			roll -= r.Weight;
			if (roll < 0)
				return r.Item;
		}
		return rows[^1].Item;
	}

	/// <summary>
	/// Actor cells first; a blank weapon, armor, rig or backpack cell is rolled from the table when the actor names a
	/// loadout faction. The tier is the spawner's, else the actor's own <c>loadout_tier</c>, else 1.
	/// </summary>
	public ResolvedLoadout Resolve(ActorDef def, int? spawnTier, Random rng)
	{
		var faction = def.Links.TryGetValue("loadout_faction", out var f) ? f : "";
		var tier = spawnTier ?? (def.Links.TryGetValue("loadout_tier", out var t) && int.TryParse(t, out var n) ? n : 1);
		string? Roll(string slot) => faction == "" ? null : Pick(faction, tier, slot, rng);

		var weapons = def.List("weapon_choices").ToList();
		if (weapons.Count == 0 && def.Text("weapon") != "")
			weapons.Add(def.Text("weapon"));
		if (weapons.Count == 0 && (Roll("Primary") ?? Roll("Pistol")) is { } rolled)
			weapons.Add(rolled);

		var armor = def.List("armor").ToList();
		if (armor.Count == 0)
			armor.AddRange(new[] { Roll("Vest"), Roll("Helmet") }.OfType<string>());

		var rig = def.Text("rig") != "" ? def.Text("rig") : Roll("Rig") ?? "";
		var pack = def.Text("backpack") != "" ? def.Text("backpack") : Roll("Backpack") ?? "";
		return new ResolvedLoadout(weapons, armor, rig, pack);
	}
}
