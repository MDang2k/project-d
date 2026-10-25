using System;
using Godot;

namespace ProjectD;

public enum BobMove { Idle, Walk, Sprint, CrouchWalk, Crawl }

/// <summary>View bob amplitudes (animation spec s4.6, tune). Metres and degrees.</summary>
public sealed record ViewBobTuning(
	float WalkVertical = 0.006f, float WalkLateral = 0.003f, float WalkRollDeg = 0.3f,
	float SprintVertical = 0.012f, float SprintLateral = 0.006f, float SprintRollDeg = 0.5f,
	float CrouchFraction = 0.6f,
	float CrawlVertical = 0.003f,
	float AdsScale = 0.2f,
	float LandingDipMaxM = 0.03f,
	/// <summary>Metres walked per full stride (two steps).</summary>
	float StrideM = 1.5f)
{
	public static ViewBobTuning Default { get; } = new();
}

/// <summary>Procedural camera bob from the locomotion phase. Pure logic.</summary>
public static class ViewBob
{
	/// <summary>Bob offset for a stride phase (radians): vertical m (twice per stride), lateral m, roll degrees.</summary>
	public static (float Vertical, float Lateral, float RollDeg) Offset(ViewBobTuning t, BobMove move, float phase)
	{
		var (v, l, r) = move switch
		{
			BobMove.Walk => (t.WalkVertical, t.WalkLateral, t.WalkRollDeg),
			BobMove.Sprint => (t.SprintVertical, t.SprintLateral, t.SprintRollDeg),
			BobMove.CrouchWalk => (t.WalkVertical * t.CrouchFraction, t.WalkLateral * t.CrouchFraction, t.WalkRollDeg * t.CrouchFraction),
			BobMove.Crawl => (t.CrawlVertical, t.CrawlVertical * 0.5f, 0f),
			_ => (0f, 0f, 0f),
		};
		return (v * MathF.Sin(phase * 2f), l * MathF.Sin(phase), r * MathF.Sin(phase));
	}

	/// <summary>Multiplier for aiming (reduced), resting or on a bipod (off), and the 0 to 1.5 settings slider.</summary>
	public static float Scale(ViewBobTuning t, float adsAmount, float restAmount, float slider)
		=> Math.Clamp(slider, 0f, 1.5f) * Mathf.Lerp(1f, t.AdsScale, Math.Clamp(adsAmount, 0f, 1f)) * (1f - Math.Clamp(restAmount, 0f, 1f));

	public static BobMove MoveOf(bool onFloor, float speed, bool sprinting, Stance stance)
	{
		if (!onFloor || speed < 0.2f)
			return BobMove.Idle;
		if (stance == Stance.Prone)
			return BobMove.Crawl;
		if (sprinting)
			return BobMove.Sprint;
		return stance == Stance.Crouch ? BobMove.CrouchWalk : BobMove.Walk;
	}

	/// <summary>Landing dip impulse (downward velocity) for a fall speed, capped so the dip peaks near LandingDipMaxM.</summary>
	public static float LandingImpulse(ViewBobTuning t, float fallSpeed)
		=> -Math.Min(fallSpeed * 0.9f, t.LandingDipMaxM * 40f);
}
