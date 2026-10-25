using System;
using Godot;

namespace ProjectD;

public enum GaitStance { Stand, Crouch, Prone }

/// <summary>Leg bone angles in degrees (thigh, shin, foot pitch; positive thigh pitch swings the leg forward).</summary>
public readonly record struct LegAngles(float ThighDeg, float ShinDeg, float FootDeg, float ThighRollDeg);

/// <summary>Placeholder gait amplitudes (animation spec s1.1, tune). Replaced by real clips later.</summary>
public sealed record GaitTuning(
	float WalkSwingDeg = 22f, float WalkKneeDeg = 35f,
	float SprintSwingDeg = 42f, float SprintKneeDeg = 75f,
	float StrafeRollDeg = 10f,
	float CrouchBaseThighDeg = 80f, float CrouchBaseShinDeg = 100f,
	float CrouchSwingScale = 0.5f,
	float ProneSwingDeg = 10f, float ProneKneeDeg = 20f)
{
	public static GaitTuning Default { get; } = new();
}

/// <summary>
/// Leg angles over a stride for the placeholder locomotion clips (animation spec s1.1): a walk and sprint
/// cycle in any direction and stance. The clip builder samples this; real clips replace it. Pure logic.
/// </summary>
public static class GaitModel
{
	/// <summary>Swing amplitude for a speed ring: 0 idle, 0.5 walk, 1 sprint, blended between.</summary>
	private static float Ring(float ring, float idle, float walk, float sprint)
		=> ring <= 0.5f ? Mathf.Lerp(idle, walk, Math.Clamp(ring, 0f, 0.5f) / 0.5f)
			: Mathf.Lerp(walk, sprint, Math.Clamp((ring - 0.5f) / 0.5f, 0f, 1f));

	/// <param name="ring">0 idle, 0.5 walk, 1 sprint.</param>
	/// <param name="dir">Movement direction in the body frame: x right, y forward (unit length, or zero).</param>
	/// <param name="phase">Stride phase 0..1 (left leg; the right leg is half a cycle behind).</param>
	public static (LegAngles Left, LegAngles Right) Legs(GaitTuning t, GaitStance stance, float ring, Vector2 dir, float phase)
	{
		var swing = Ring(ring, 0f, t.WalkSwingDeg, t.SprintSwingDeg);
		var knee = Ring(ring, 0f, t.WalkKneeDeg, t.SprintKneeDeg);
		var baseThigh = 0f;
		var baseShin = 0f;
		switch (stance)
		{
			case GaitStance.Crouch:
				baseThigh = t.CrouchBaseThighDeg;
				baseShin = -t.CrouchBaseShinDeg;
				swing *= t.CrouchSwingScale;
				knee *= t.CrouchSwingScale;
				break;
			case GaitStance.Prone:
				swing = Ring(ring, 0f, t.ProneSwingDeg, t.ProneSwingDeg);
				knee = Ring(ring, 0f, t.ProneKneeDeg, t.ProneKneeDeg);
				break;
		}
		LegAngles One(float q)
		{
			var s = MathF.Sin(q * Mathf.Tau);
			var c = MathF.Cos(q * Mathf.Tau);
			var forward = dir.Y;
			var thighSwing = swing * forward * s;
			var kneeLift = knee * Math.Max(0f, c * (forward >= -0.1f ? 1f : -1f));
			var thigh = baseThigh + thighSwing;
			var shin = baseShin - kneeLift;
			var foot = -(baseThigh + baseShin) - 0.6f * (thighSwing - kneeLift);
			var roll = t.StrafeRollDeg * dir.X * s * Math.Min(1f, ring * 2f);
			return new LegAngles(thigh, shin, foot, roll);
		}
		return (One(phase), One(phase + 0.5f));
	}
}

/// <summary>Inputs for the locomotion blend (animation spec s1.1). Pure logic.</summary>
public static class LocomotionParams
{
	/// <summary>
	/// Blend space coordinate: direction of travel in the body frame (x right, y forward) scaled by the speed ring
	/// (0.5 at walking speed, 1 at sprint). The body faces -Z, so forward is -Z of the local velocity.
	/// </summary>
	public static Vector2 BlendPosition(Vector3 localVelocity, float walkSpeed, float sprintSpeed)
	{
		var flat = new Vector2(localVelocity.X, -localVelocity.Z);
		var speed = flat.Length();
		if (speed < 0.05f)
			return Vector2.Zero;
		var ring = speed <= walkSpeed
			? 0.5f * speed / Math.Max(0.01f, walkSpeed)
			: 0.5f + 0.5f * Math.Clamp((speed - walkSpeed) / Math.Max(0.01f, sprintSpeed - walkSpeed), 0f, 1f);
		return flat / speed * ring;
	}

	/// <summary>Clip play rate from ground speed, so the feet do not slide (stride frequency grows with speed).</summary>
	public static float PlayRate(float groundSpeed)
		=> groundSpeed < 0.05f ? 1f : Math.Clamp(0.7f + 0.3f * groundSpeed, 0.35f, 2.2f);

	/// <summary>Blend factors for (stand to crouch, then that to prone) from the stance weights (stand, crouch, prone).</summary>
	public static (float CrouchOverStand, float ProneOverRest) StanceFactors((float Stand, float Crouch, float Prone) w)
	{
		var rest = w.Stand + w.Crouch;
		return (rest > 0.0001f ? w.Crouch / rest : 1f, Math.Clamp(w.Prone, 0f, 1f));
	}
}

/// <summary>Aim offset (animation spec s1.2): how much of the look pitch the spine and chest take, and what the head keeps.</summary>
public sealed record AimOffsetTuning(
	/// <summary>Share of an upward look the torso follows (leaning back); the head takes the rest so it still faces the camera.</summary>
	float BodyShare = 0.5f,
	float MaxBodyDeg = 35f,
	/// <summary>
	/// Looking down the torso leans toward the camera and would cover the weapon and hands (the camera, not the head,
	/// is the authority), so it follows only a little until a head-anchored correction exists.
	/// </summary>
	float DownShare = 0.1f,
	float MaxDownDeg = 8f,
	/// <summary>Of the torso share, the lower spine takes this fraction and the chest the rest.</summary>
	float SpineFraction = 0.4f,
	float PronePitchMinDeg = -15f,
	float PronePitchMaxDeg = 35f)
{
	public static AimOffsetTuning Default { get; } = new();
}

public static class AimOffsetSolver
{
	/// <summary>Pitch rotations in degrees for the spine, chest and head bones (positive = looking up).</summary>
	public static (float Spine, float Chest, float Head) Distribute(AimOffsetTuning t, float pitchDeg, float proneAmount)
	{
		var prone = Math.Clamp(proneAmount, 0f, 1f);
		var min = Mathf.Lerp(-90f, t.PronePitchMinDeg, prone);
		var max = Mathf.Lerp(90f, t.PronePitchMaxDeg, prone);
		var pitch = Math.Clamp(pitchDeg, min, max); // anything past the clamp turns the weapon only
		var body = pitch >= 0f
			? Math.Min(pitch * t.BodyShare, t.MaxBodyDeg)
			: Math.Max(pitch * t.DownShare, -t.MaxDownDeg);
		return (body * t.SpineFraction, body * (1f - t.SpineFraction), pitch - body);
	}
}
