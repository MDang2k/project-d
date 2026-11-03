using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>Where a wave appears and where it walks. Pure geometry; the spawner supplies the candidate points.</summary>
public static class WavePlanner
{
	/// <summary>
	/// Offsets from the marker so a group does not stack on one spot: the first at the marker, the rest on a ring that
	/// grows with the group, evenly spaced and the same every time.
	/// </summary>
	public static IReadOnlyList<Vector3> Offsets(int count, float spacingM = 2.5f)
	{
		var result = new List<Vector3>();
		for (var i = 0; i < count; i++)
		{
			if (i == 0)
			{
				result.Add(Vector3.Zero);
				continue;
			}
			var radius = spacingM * Mathf.Max(1f, (count - 1) / 3f) * 0.8f + spacingM * 0.4f;
			var angle = Mathf.Tau * (i - 1) / Mathf.Max(1, count - 1);
			result.Add(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
		}
		return result;
	}

	/// <summary>Visits the points nearest-first from <paramref name="start"/>, keeping at most <paramref name="take"/>.</summary>
	public static IReadOnlyList<Vector3> NearestChain(Vector3 start, IEnumerable<Vector3> points, int take)
	{
		var left = points.ToList();
		var chain = new List<Vector3>();
		var at = start;
		while (left.Count > 0 && chain.Count < take)
		{
			var next = left.OrderBy(p => p.DistanceSquaredTo(at)).First();
			chain.Add(next);
			left.Remove(next);
			at = next;
		}
		return chain;
	}

	/// <summary>A small loop around a point, for a wave with nowhere better to walk.</summary>
	public static IReadOnlyList<Vector3> Loop(Vector3 centre, float radiusM, int points = 4)
		=> Enumerable.Range(0, points)
			.Select(i => centre + new Vector3(Mathf.Cos(Mathf.Tau * i / points), 0f, Mathf.Sin(Mathf.Tau * i / points)) * radiusM)
			.ToList();

	/// <summary>Whether a due wave may appear now: the player is far enough, or it has waited long enough.</summary>
	public static bool MayAppear(WaveTuning t, float playerDistanceM, float waitedS)
		=> playerDistanceM >= t.MinPlayerDistanceM || waitedS >= t.RetryMaxS;
}
