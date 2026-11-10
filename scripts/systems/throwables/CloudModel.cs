using System;
using Godot;

namespace ProjectD;

/// <summary>
/// A smoke cloud over time (throwables spec s4): grows to its full radius, stays, then shrinks away in its last seconds;
/// it hides whoever is inside or behind its dense core. Pure logic.
/// </summary>
public static class CloudModel
{
	/// <summary>Seconds at the end of its life in which the cloud thins out and shrinks.</summary>
	public const float FadeS = 5f;

	/// <summary>Share of the radius that is dense enough to block sight completely.</summary>
	public const float DenseShare = 0.65f;

	public static float Radius(float ageS, float maxRadiusM, float growS, float lifeS)
	{
		if (ageS < 0f || ageS >= lifeS)
			return 0f;
		var grow = growS <= 0f ? 1f : Math.Clamp(ageS / growS, 0f, 1f);
		var fade = ageS > lifeS - FadeS ? Math.Clamp((lifeS - ageS) / FadeS, 0f, 1f) : 1f;
		return maxRadiusM * grow * fade;
	}

	public static bool IsAlive(float ageS, float lifeS) => ageS >= 0f && ageS < lifeS;

	/// <summary>How thick the cloud is at a distance from its centre: 1 at the centre, 0 at the edge.</summary>
	public static float Opacity(float distanceM, float radiusM)
		=> radiusM <= 0f || distanceM >= radiusM ? 0f : 1f - distanceM / radiusM;

	/// <summary>
	/// Whether a sight line is blocked: it ends inside the dense core, or passes through it. The line is the segment
	/// from <paramref name="a"/> to <paramref name="b"/> (a watcher's eye to a body point).
	/// </summary>
	public static bool SegmentBlocked(Vector3 a, Vector3 b, Vector3 center, float radiusM)
	{
		if (radiusM <= 0f)
			return false;
		var ab = b - a;
		var lengthSq = ab.LengthSquared();
		var t = lengthSq < 1e-6f ? 0f : Math.Clamp((center - a).Dot(ab) / lengthSq, 0f, 1f);
		return (a + ab * t).DistanceTo(center) < radiusM * DenseShare;
	}
}
