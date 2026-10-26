using System;
using Godot;

namespace ProjectD;

/// <summary>Foot IK numbers (animation spec s4.5, tune). Metres and degrees.</summary>
public sealed record FootIkTuning(
	/// <summary>Ground probe starts this far above the floor under the character and ends RayDown below it.</summary>
	float RayUp = 0.6f,
	float RayDown = 0.6f,
	/// <summary>Most the pelvis lowers to keep the lower foot reachable.</summary>
	float MaxDrop = 0.25f,
	/// <summary>Ground height difference under a foot that is followed at all (steps, slopes).</summary>
	float MaxOffset = 0.35f,
	/// <summary>A foot closer than this to the ground is pulled onto it (stops hovering and sinking).</summary>
	float PlantBand = 0.06f,
	float MaxTiltDeg = 30f,
	float DropSpeed = 2.5f,
	float BlendSpeed = 8f)
{
	public static FootIkTuning Default { get; } = new();
}

/// <summary>Pure maths for the leg IK: two-bone solve, planting height, pelvis drop, ground tilt.</summary>
public static class LegIk
{
	/// <summary>
	/// Knee and ankle positions for a leg whose hip is at <paramref name="hip"/> reaching for <paramref name="target"/>.
	/// The ankle stops short when the target is out of reach. The knee bends toward <paramref name="pole"/>.
	/// </summary>
	public static (Vector3 Knee, Vector3 Ankle) TwoBone(Vector3 hip, Vector3 target, float l1, float l2, Vector3 pole)
	{
		var toTarget = target - hip;
		var distance = toTarget.Length();
		if (distance < 0.0001f)
			return (hip + pole.Normalized() * l1, hip);
		var dir = toTarget / distance;
		var d = Math.Clamp(distance, Math.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
		var along = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
		var height = MathF.Sqrt(Math.Max(0f, l1 * l1 - along * along));
		var side = pole - dir * pole.Dot(dir);
		side = side.LengthSquared() < 1e-6f ? dir.Cross(Vector3.Right) : side.Normalized();
		var knee = hip + dir * along + side * height;
		return (knee, hip + dir * d);
	}

	/// <summary>
	/// Height of the sole above the ground the foot should have, from the animated height: below the ground or within
	/// the band it is pulled to (or toward) the ground, higher it is left alone so swing and step-ups survive.
	/// </summary>
	public static float SoleHeight(float animated, float band)
	{
		if (animated <= 0f)
			return 0f;
		if (animated >= band)
			return animated;
		return animated * Mathf.SmoothStep(0f, 1f, animated / band);
	}

	/// <summary>How far the pelvis must lower so a target that is <paramref name="shortBy"/> out of reach becomes reachable.</summary>
	public static float PelvisDrop(float shortBy, float maxDrop) => Math.Clamp(shortBy, 0f, maxDrop);

	/// <summary>Rotation that tilts a flat sole to the ground normal, limited, scaled by how planted the foot is (0..1).</summary>
	public static Quaternion GroundTilt(Vector3 normal, float maxDeg, float planted)
	{
		var n = normal.LengthSquared() < 1e-6f ? Vector3.Up : normal.Normalized();
		var angle = Math.Min(Vector3.Up.AngleTo(n), Mathf.DegToRad(maxDeg));
		var axis = Vector3.Up.Cross(n);
		if (axis.LengthSquared() < 1e-8f || angle < 1e-5f)
			return Quaternion.Identity;
		return new Quaternion(axis.Normalized(), angle * Math.Clamp(planted, 0f, 1f));
	}
}
