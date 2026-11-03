using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>Whether a boss shows up this raid, and where its squad starts (GDD s8: at most one boss per raid, by chance). Pure logic.</summary>
public static class BossRoll
{
	/// <summary>True with probability <paramref name="chance"/>; a chance of 1 or more always, 0 or less never.</summary>
	public static bool Rolls(float chance, Random rng) => chance >= 1f || (chance > 0f && rng.NextDouble() < chance);

	/// <summary>
	/// The route point the squad starts at: the farthest from the player, so it never appears on top of them. The route
	/// is then walked from there in order and wraps round.
	/// </summary>
	public static IReadOnlyList<Vector3> FromFarthest(IReadOnlyList<Vector3> route, Vector3 player)
	{
		if (route.Count == 0)
			return route;
		var start = Enumerable.Range(0, route.Count).OrderByDescending(i => route[i].DistanceSquaredTo(player)).First();
		return route.Skip(start).Concat(route.Take(start)).ToList();
	}
}
