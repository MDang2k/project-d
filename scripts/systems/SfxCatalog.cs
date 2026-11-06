using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ProjectD;

/// <summary>One sound event of data/audio/sfx.json: which files it picks from and how it is played. Pure data.</summary>
public sealed record SfxEvent(
	string Id,
	IReadOnlyList<string> Files,
	float VolumeDb,
	/// <summary>Random pitch spread: 0.06 means pitch scale between 0.94 and 1.06.</summary>
	float PitchJitter,
	string Bus,
	/// <summary>3D falloff: how far the sound can be heard, in metres.</summary>
	float MaxDistanceM,
	/// <summary>3D falloff: the distance at which the volume has dropped by 6 dB (larger is louder from afar).</summary>
	float UnitSizeM);

/// <summary>What to play for one pick: the file name, the volume offset and the pitch scale.</summary>
public readonly record struct SfxPick(string File, float VolumeDb, float PitchScale);

/// <summary>
/// The sound event table (audio pass): event id to files and playback settings, read from JSON so a real recording can
/// replace a placeholder by editing data. Picks avoid repeating the file just played. Pure logic.
/// </summary>
public sealed class SfxCatalog
{
	private readonly Dictionary<string, SfxEvent> _events;
	private readonly Dictionary<string, int> _last = new();

	private SfxCatalog(Dictionary<string, SfxEvent> events) => _events = events;

	public IReadOnlyCollection<SfxEvent> Events => _events.Values;

	public SfxEvent? Event(string id) => _events.GetValueOrDefault(id);

	public static SfxCatalog Parse(string json)
	{
		using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
		var events = new Dictionary<string, SfxEvent>();
		foreach (var prop in doc.RootElement.GetProperty("events").EnumerateObject())
		{
			var e = prop.Value;
			var files = e.GetProperty("files").EnumerateArray().Select(f => f.GetString() ?? "").Where(f => f != "").ToList();
			if (files.Count == 0)
				throw new FormatException($"sfx event '{prop.Name}' has no files");
			events[prop.Name] = new SfxEvent(prop.Name, files,
				e.TryGetProperty("db", out var db) ? db.GetSingle() : 0f,
				e.TryGetProperty("pitch", out var p) ? p.GetSingle() : 0f,
				e.TryGetProperty("bus", out var bus) ? bus.GetString() ?? "SFX" : "SFX",
				e.TryGetProperty("maxDistance", out var md) ? md.GetSingle() : 150f,
				e.TryGetProperty("unit", out var u) ? u.GetSingle() : 10f);
		}
		return new SfxCatalog(events);
	}

	/// <summary>Picks a file (never the one picked last for this event when there is a choice) with a random pitch.</summary>
	public SfxPick? Pick(string id, Random rng)
	{
		if (!_events.TryGetValue(id, out var e))
			return null;
		var index = rng.Next(e.Files.Count);
		if (e.Files.Count > 1 && _last.TryGetValue(id, out var last) && index == last)
			index = (index + 1 + rng.Next(e.Files.Count - 1)) % e.Files.Count;
		_last[id] = index;
		var pitch = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * e.PitchJitter;
		return new SfxPick(e.Files[index], e.VolumeDb, pitch);
	}
}
