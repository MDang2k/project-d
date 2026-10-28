using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>
/// Where each reload event sits inside an authored clip (seconds), and its length. Missing events fall back to the
/// nominal proportions of the reload.
/// </summary>
public sealed record ClipEvents(float Length, IReadOnlyDictionary<ReloadEvent, float> Times);

/// <summary>
/// Plays an authored reload clip on the logic clock (animation spec s5.6): the weapon logic fires the events at
/// its own times, so the clip is stretched piece by piece to put each of its event marks on the matching logic
/// event, and a resumed reload starts the clip at the event the weapon has already passed. Pure logic.
/// </summary>
public static class ReloadClipWarp
{
	/// <summary>Event times of a clip authored to the usual proportions of a reload (used when the clip names none).</summary>
	public static ClipEvents Nominal(ActionTuning tuning, bool fast, bool needsRack, float clipLength)
	{
		var seq = new ReloadSequence(tuning, fast, ReloadEntry.Full, needsRack);
		var times = seq.Timeline.ToDictionary(e => e.Event, e => e.Time / seq.Total * clipLength);
		return new ClipEvents(clipLength, times);
	}

	/// <summary>Fills in events the clip does not name with the nominal proportions.</summary>
	public static ClipEvents WithNominalDefaults(ClipEvents named, ActionTuning tuning, bool fast, bool needsRack)
	{
		var nominal = Nominal(tuning, fast, needsRack, named.Length);
		var merged = new Dictionary<ReloadEvent, float>(nominal.Times);
		foreach (var (e, t) in named.Times)
			merged[e] = Math.Clamp(t, 0f, named.Length);
		return new ClipEvents(named.Length, merged);
	}

	/// <summary>Clip time to start at: the event before the phase a resumed reload begins in.</summary>
	public static float StartTime(ReloadEntry entry, bool fast, ClipEvents clip) => entry switch
	{
		ReloadEntry.ResumeRack => clip.Times.GetValueOrDefault(ReloadEvent.MagInserted),
		ReloadEntry.ResumeInsert or ReloadEntry.ResumeEmpty
			=> clip.Times.GetValueOrDefault(fast ? ReloadEvent.MagDetached : ReloadEvent.MagStowed),
		_ => 0f,
	};

	/// <summary>Control points (logic seconds, clip seconds) from the reload timeline; always increasing in both.</summary>
	public static IReadOnlyList<(float Logic, float Clip)> Points(
		IReadOnlyList<(ReloadEvent Event, float Time)> timeline, float logicTotal, ReloadEntry entry, bool fast, ClipEvents clip)
	{
		var start = StartTime(entry, fast, clip);
		var points = new List<(float, float)> { (0f, start) };
		foreach (var (e, logic) in timeline)
		{
			var clipTime = e == ReloadEvent.Finished ? clip.Length : clip.Times.GetValueOrDefault(e, -1f);
			if (clipTime < 0f || logic <= points[^1].Item1 || clipTime <= points[^1].Item2)
				continue;
			points.Add((logic, clipTime));
		}
		if (points[^1].Item1 < logicTotal && points[^1].Item2 < clip.Length)
			points.Add((logicTotal, clip.Length));
		return points;
	}

	/// <summary>Clip time for the elapsed logic time, interpolating between control points.</summary>
	public static float ClipTime(IReadOnlyList<(float Logic, float Clip)> points, float logicElapsed)
	{
		if (points.Count == 0)
			return 0f;
		if (logicElapsed <= points[0].Logic)
			return points[0].Clip;
		for (var i = 1; i < points.Count; i++)
		{
			if (logicElapsed > points[i].Logic)
				continue;
			var a = points[i - 1];
			var b = points[i];
			var f = (logicElapsed - a.Logic) / Math.Max(0.0001f, b.Logic - a.Logic);
			return a.Clip + (b.Clip - a.Clip) * f;
		}
		return points[^1].Clip;
	}
}
