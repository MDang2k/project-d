using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>Mine numbers (Border Strip spec s2: heavy leg damage, fracture and heavy bleed likely, armor damaged). Placeholders to tune.</summary>
public sealed record MineTuning(
	/// <summary>How close to a mine a foot must come to set it off, metres.</summary>
	float TriggerRadiusM = 0.5f,
	/// <summary>Damage to the stepped leg (a leg has 65 HP).</summary>
	float LegDamage = 55f,
	float OtherLegDamage = 25f,
	float StomachDamage = 20f,
	float ThoraxDamage = 15f,
	/// <summary>Penetration tier of the blast against armor; armor on the torso takes wear like it would from a bullet.</summary>
	int PenetrationTier = 3,
	float FractureChance = 0.85f,
	float HeavyBleedChance = 0.7f,
	/// <summary>How far the bang carries for AI hearing, metres.</summary>
	float NoiseRadiusM = 150f,
	/// <summary>Mines sit one per grid cell of this size, with some cells empty.</summary>
	float SpacingM = 5f,
	float FillChance = 0.7f,
	/// <summary>How far a mine may sit from its cell centre, metres.</summary>
	float JitterM = 1.6f,
	/// <summary>Half the width of a safe lane; no mines inside it.</summary>
	float LaneHalfWidthM = 1.8f,
	/// <summary>No mines this close to the edge of the field, where the warning signs stand.</summary>
	float EdgeMarginM = 1.5f,
	float SignSpacingM = 12f,
	/// <summary>Chance a sign has gone missing (some mined ground is unmarked).</summary>
	float SignMissingChance = 0.3f);

/// <summary>One part of the body hit by a blast.</summary>
public readonly record struct BlastHit(BodyPart Part, float Damage, int PenetrationTier);

/// <summary>What a mine does to whoever stepped on it: the hits, and whether the stepped leg breaks and bleeds heavily.</summary>
public sealed record BlastPlan(IReadOnlyList<BlastHit> Hits, BodyPart SteppedLeg, bool Fracture, bool HeavyBleed);

/// <summary>Pure rules of a mine going off.</summary>
public static class MineBlast
{
	public static BlastPlan Plan(MineTuning t, Random rng)
	{
		var left = rng.NextDouble() < 0.5;
		var stepped = left ? BodyPart.LeftLeg : BodyPart.RightLeg;
		var other = left ? BodyPart.RightLeg : BodyPart.LeftLeg;
		var hits = new List<BlastHit>
		{
			new(stepped, t.LegDamage, t.PenetrationTier),
			new(other, t.OtherLegDamage, t.PenetrationTier),
			new(BodyPart.Stomach, t.StomachDamage, t.PenetrationTier),
			new(BodyPart.Thorax, t.ThoraxDamage, t.PenetrationTier),
		};
		return new BlastPlan(hits, stepped, rng.NextDouble() < t.FractureChance, rng.NextDouble() < t.HeavyBleedChance);
	}
}

/// <summary>Where the mines and the warning signs of a field go. Pure geometry over a ground rectangle (x, z).</summary>
public static class MineFieldPlanner
{
	/// <summary>
	/// One jittered mine per grid cell, some cells left empty, none in a safe lane or at the field's edge. Lanes are
	/// polylines of ground points; their corridor is <see cref="MineTuning.LaneHalfWidthM"/> wide on each side.
	/// </summary>
	public static List<Vector2> Place(Rect2 area, IReadOnlyList<IReadOnlyList<Vector2>> lanes, MineTuning t, Random rng)
	{
		var mines = new List<Vector2>();
		var inner = area.GrowIndividual(-t.EdgeMarginM, -t.EdgeMarginM, -t.EdgeMarginM, -t.EdgeMarginM);
		for (var x = area.Position.X; x < area.End.X; x += t.SpacingM)
		{
			for (var z = area.Position.Y; z < area.End.Y; z += t.SpacingM)
			{
				if (rng.NextDouble() >= t.FillChance)
					continue;
				var p = new Vector2(x + t.SpacingM / 2f + Jitter(rng, t), z + t.SpacingM / 2f + Jitter(rng, t));
				if (!inner.HasPoint(p) || lanes.Any(l => DistanceToPolyline(p, l) < t.LaneHalfWidthM))
					continue;
				mines.Add(p);
			}
		}
		return mines;
	}

	private static float Jitter(Random rng, MineTuning t) => (float)(rng.NextDouble() * 2.0 - 1.0) * t.JitterM;

	public static float DistanceToPolyline(Vector2 p, IReadOnlyList<Vector2> line)
	{
		if (line.Count == 0)
			return float.PositiveInfinity;
		if (line.Count == 1)
			return p.DistanceTo(line[0]);
		var best = float.PositiveInfinity;
		for (var i = 0; i + 1 < line.Count; i++)
		{
			var a = line[i];
			var ab = line[i + 1] - a;
			var len2 = ab.LengthSquared();
			var s = len2 < 1e-6f ? 0f : Mathf.Clamp((p - a).Dot(ab) / len2, 0f, 1f);
			best = Mathf.Min(best, p.DistanceTo(a + ab * s));
		}
		return best;
	}

	/// <summary>Warning signs along the field's edge, facing outward (yaw degrees about the vertical); some are missing.</summary>
	public static List<(Vector2 Position, float YawDeg)> Signs(Rect2 area, MineTuning t, Random rng)
	{
		var signs = new List<(Vector2, float)>();
		void Edge(Vector2 from, Vector2 to, float yaw)
		{
			var length = from.DistanceTo(to);
			var count = Mathf.Max(1, Mathf.FloorToInt(length / t.SignSpacingM));
			for (var i = 0; i < count; i++)
			{
				var p = from.Lerp(to, (i + 0.5f) / count);
				if (rng.NextDouble() >= t.SignMissingChance)
					signs.Add((p, yaw));
			}
		}
		var (a, b, c, d) = (area.Position, new Vector2(area.End.X, area.Position.Y), area.End, new Vector2(area.Position.X, area.End.Y));
		Edge(a, b, 0f);   // north edge: forward (-Z) points out of the field
		Edge(b, c, 270f); // east edge: out is +X
		Edge(c, d, 180f); // south edge: out is +Z
		Edge(d, a, 90f);  // west edge: out is -X
		return signs;
	}
}
