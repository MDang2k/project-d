using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>
/// Thins out spawn points that share a roll group (Border Strip spec s3: 8 looter points, 4 to 6 active each raid).
/// Points without a group always stay. Pure logic; <c>RaidLevel</c> applies it to the map's AI spawn points.
/// </summary>
public static class SpawnRoll
{
	/// <summary>
	/// Keeps a random number between min and max (inclusive, capped to what exists) of each group's points, chosen at
	/// random, in their original order. A group takes its limits from its first point.
	/// </summary>
	public static List<T> Apply<T>(IEnumerable<T> points, Func<T, string> group, Func<T, int> min, Func<T, int> max, Random rng)
	{
		var all = points.ToList();
		var dropped = new HashSet<int>();
		foreach (var members in all.Select((p, i) => (p, i)).Where(x => group(x.p) != "").GroupBy(x => group(x.p)))
		{
			var list = members.ToList();
			var lo = Math.Max(0, min(list[0].p));
			var hi = Math.Max(lo, max(list[0].p));
			var keep = Math.Min(list.Count, rng.Next(lo, hi + 1));
			foreach (var x in list.OrderBy(_ => rng.Next()).Skip(keep))
				dropped.Add(x.i);
		}
		return all.Where((_, i) => !dropped.Contains(i)).ToList();
	}
}
