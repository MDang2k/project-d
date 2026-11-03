using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProjectD;

/// <summary>One wave arrival from one marker: who comes, how many, and when (raid seconds elapsed).</summary>
public sealed record WaveEntry(string PointId, string Kind, float AtS, string Preset, int Count, string Side = "");

/// <summary>Timing and distance numbers of the waves (map standard s6). Defaults match the Border Strip spec.</summary>
public sealed record WaveTuning(
	/// <summary>Seconds before a PMC patrol wave arrives that its truck engines and radio start.</summary>
	float PatrolLeadS = 20f,
	/// <summary>Seconds before a bandit wave arrives that its shouting and flare start.</summary>
	float BanditLeadS = 15f,
	/// <summary>A wave does not appear closer to the player than this.</summary>
	float MinPlayerDistanceM = 60f,
	/// <summary>A wave held back by a nearby player spawns anyway after this long.</summary>
	float RetryMaxS = 60f)
{
	public float LeadS(string kind) => kind == "bandit" ? BanditLeadS : PatrolLeadS;
}

/// <summary>
/// Fires each <see cref="WaveEntry"/> twice, in order: Telegraphed a lead time before its arrival time, then Arrived.
/// A first update already past both thresholds (a dev clock skip) fires both in that order, once. Pure logic; the
/// <c>WaveSpawner</c> node drives it from the raid clock.
/// </summary>
public sealed class WaveDirector
{
	private sealed class State
	{
		public WaveEntry Entry = null!;
		public bool Telegraphed;
		public bool Arrived;
	}

	private readonly List<State> _states;
	private readonly WaveTuning _tuning;

	public WaveDirector(IEnumerable<WaveEntry> entries, WaveTuning tuning)
	{
		_tuning = tuning;
		_states = Alternate(entries).OrderBy(e => e.AtS).ThenBy(e => e.PointId, StringComparer.Ordinal)
			.Select(e => new State { Entry = e }).ToList();
	}

	public event Action<WaveEntry>? Telegraphed;
	public event Action<WaveEntry>? Arrived;

	public IEnumerable<WaveEntry> Entries => _states.Select(s => s.Entry);

	/// <summary>Whether every wave has arrived.</summary>
	public bool Finished => _states.All(s => s.Arrived);

	public void Update(float elapsedS)
	{
		foreach (var s in _states)
		{
			if (!s.Telegraphed && elapsedS >= s.Entry.AtS - _tuning.LeadS(s.Entry.Kind))
			{
				s.Telegraphed = true;
				Telegraphed?.Invoke(s.Entry);
			}
			if (s.Telegraphed && !s.Arrived && elapsedS >= s.Entry.AtS)
			{
				s.Arrived = true;
				Arrived?.Invoke(s.Entry);
			}
		}
	}

	/// <summary>
	/// Bandit waves come from one entry at a time, taking turns between the markers by wave number (first wave from the
	/// first marker by id, the next from the second, and so on). Patrol waves use every marker.
	/// </summary>
	public static IEnumerable<WaveEntry> Alternate(IEnumerable<WaveEntry> entries)
	{
		var list = entries.ToList();
		foreach (var e in list.Where(e => e.Kind != "bandit"))
			yield return e;
		foreach (var group in list.Where(e => e.Kind == "bandit").GroupBy(e => e.Preset))
		{
			var points = group.Select(e => e.PointId).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
			var times = group.Select(e => e.AtS).Distinct().OrderBy(t => t).ToList();
			for (var i = 0; i < times.Count; i++)
			{
				var chosen = points[i % points.Count];
				var entry = group.FirstOrDefault(e => e.PointId == chosen && e.AtS == times[i]);
				if (entry is not null)
					yield return entry;
			}
		}
	}

	/// <summary>Minutes list ("8" or "8,16,24") as raid seconds; malformed parts are skipped.</summary>
	public static IReadOnlyList<float> ParseMinutes(string minutes)
		=> minutes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(m => float.TryParse(m, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v * 60f : -1f)
			.Where(s => s >= 0f).ToList();
}
