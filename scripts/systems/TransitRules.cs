using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ProjectD;

/// <summary>A tunnel crosses within one map; a road leads into another map's raid (world map spec s3).</summary>
public enum TransitKind { Tunnel, Road }

/// <summary>Why standing in a transit zone is not progressing.</summary>
public enum TransitBlock { None, Closed, MissingItem, CannotPay }

/// <summary>One transit zone: how long to stand, what it costs and where it leads.</summary>
/// <param name="PairId">Tunnel: the zone at the other end. Road: empty.</param>
/// <param name="Destination">Road: map id of the destination map. Tunnel: empty.</param>
public sealed record TransitSpec(string Id, TransitKind Kind, float Seconds, int Cost = 0, string? RequiredItemId = null,
	string PairId = "", string Destination = "");

/// <summary>Gate check of a transit zone (world map spec s3): pure, like the extraction gate.</summary>
public static class TransitRules
{
	/// <summary>
	/// What stops the crossing, in order: a road to a closed map, a missing item, too little carried money (banknotes in
	/// storage grids, not the secure container).
	/// </summary>
	public static TransitBlock Check(TransitSpec spec, int carriedMoney, Func<string, bool> hasItem, bool destinationOpen)
	{
		if (spec.Kind == TransitKind.Road && !destinationOpen)
			return TransitBlock.Closed;
		if (spec.RequiredItemId is { Length: > 0 } item && !hasItem(item))
			return TransitBlock.MissingItem;
		if (spec.Cost > 0 && carriedMoney < spec.Cost)
			return TransitBlock.CannotPay;
		return TransitBlock.None;
	}
}

/// <summary>
/// The stand-still timer of transit zones: progress builds while the player stays in an unblocked zone, resets on
/// leaving or blocking, and the zone completes once. After completing, the zone must be left before it can arm again
/// (a tunnel drops you inside the zone at its other end).
/// </summary>
public sealed class TransitCountdown
{
	private string? _armedAgainAfter;

	public string? ZoneId { get; private set; }
	public float Progress { get; private set; }
	public float Remaining { get; private set; }

	/// <summary>Advances the timer; returns the spec of a zone that just completed, else null.</summary>
	public TransitSpec? Update(float delta, TransitSpec? zone, TransitBlock block)
	{
		if (zone is null)
			_armedAgainAfter = null; // out of every zone: the last one works again
		if (zone is null || block != TransitBlock.None || zone.Id == _armedAgainAfter)
		{
			ZoneId = null;
			Progress = 0f;
			Remaining = 0f;
			return null;
		}
		if (ZoneId != zone.Id)
		{
			ZoneId = zone.Id;
			Progress = 0f;
		}
		Progress = zone.Seconds <= 0f ? 1f : Math.Min(1f, Progress + delta / zone.Seconds);
		Remaining = zone.Seconds * (1f - Progress);
		if (Progress < 1f)
			return null;
		_armedAgainAfter = zone.Id;
		ZoneId = null;
		Progress = 0f;
		Remaining = 0f;
		return zone;
	}

	/// <summary>The player was moved (a tunnel exit): the zone they land in must be left before it works again.</summary>
	public void Landed(string zoneId)
	{
		_armedAgainAfter = zoneId;
		ZoneId = null;
		Progress = 0f;
	}
}

/// <summary>The roster of raid maps the transits lead to (data/maps/registry.json): which exist and which are open.</summary>
public sealed class MapRegistry
{
	public sealed record Entry(string Id, string Name, string Scene, bool Open);

	private readonly Dictionary<string, Entry> _maps;

	private MapRegistry(IEnumerable<Entry> maps) => _maps = maps.ToDictionary(m => m.Id);

	public IEnumerable<Entry> Maps => _maps.Values;

	public static MapRegistry Parse(string json)
	{
		using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
		var entries = doc.RootElement.GetProperty("maps").EnumerateArray().Select(e => new Entry(
			e.GetProperty("id").GetString()!,
			e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
			e.TryGetProperty("scene", out var s) ? s.GetString() ?? "" : "",
			e.TryGetProperty("open", out var o) && o.GetBoolean())).ToList();
		var dup = entries.GroupBy(e => e.Id).FirstOrDefault(g => g.Count() > 1);
		if (dup is not null)
			throw new ArgumentException($"duplicate map id '{dup.Key}'");
		return new MapRegistry(entries);
	}

	public bool Has(string id) => _maps.ContainsKey(id);

	/// <summary>A map can be entered when it has a scene and is unlocked; unknown ids are closed.</summary>
	public bool IsOpen(string id) => _maps.TryGetValue(id, out var m) && m.Open && m.Scene != "";

	public string? SceneOf(string id) => _maps.TryGetValue(id, out var m) && m.Scene != "" ? m.Scene : null;
}
