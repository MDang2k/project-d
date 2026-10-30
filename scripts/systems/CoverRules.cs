using System;

namespace ProjectD;

/// <summary>What an obstacle lets the AI do behind it (AI combat v2 spec s5).</summary>
public enum CoverClass
{
	/// <summary>Too low to hide behind.</summary>
	None,
	/// <summary>Low enough that only lying down hides the body.</summary>
	Prone,
	/// <summary>Hip to chest height: crouch behind it, rise to peek over.</summary>
	Crouch,
	/// <summary>Taller than a standing person: stand behind it, lean around the edge to peek.</summary>
	Standing,
}

/// <summary>A cover spot found by the sweep, with what the scorer needs to know about it.</summary>
public readonly record struct CoverCandidate(
	CoverClass Class,
	/// <summary>Metres the AI walks to get there.</summary>
	float Distance,
	/// <summary>0..1 share of the way there that the threat can see.</summary>
	float Exposure,
	/// <summary>The spot allows a shot at the threat by leaning or rising.</summary>
	bool HasFiringAngle,
	/// <summary>Distance from the spot to the threat.</summary>
	float ThreatDistance);

/// <summary>Pure rules for classifying and scoring cover. Lower score is better.</summary>
public static class CoverRules
{
	public const float StandingMinHeight = 1.8f;
	public const float CrouchMinHeight = 0.8f;
	public const float ProneMinHeight = 0.3f;

	/// <summary>Obstacle height to class; heights between the thresholds count as the lower class.</summary>
	public static CoverClass Classify(float height)
		=> height >= StandingMinHeight ? CoverClass.Standing
			: height >= CrouchMinHeight ? CoverClass.Crouch
			: height >= ProneMinHeight ? CoverClass.Prone
			: CoverClass.None;

	/// <summary>Eye height used to check that a spot is hidden, metres.</summary>
	public static float HiddenEyeHeight(CoverClass c) => c switch
	{
		CoverClass.Standing => 1.6f,
		CoverClass.Crouch => 1.0f,
		CoverClass.Prone => 0.3f,
		_ => 1.6f,
	};

	/// <summary>Eye height after rising to peek over the cover, metres. Standing cover is peeked by leaning instead.</summary>
	public static float PeekEyeHeight(CoverClass c) => c switch
	{
		CoverClass.Prone => 1.0f,
		CoverClass.Crouch => 1.6f,
		_ => 1.6f,
	};

	public static float Score(in CoverCandidate c, float myThreatDistance, bool retreat)
	{
		var score = c.Distance + c.Exposure * 8f
			+ c.Class switch { CoverClass.Standing => 0f, CoverClass.Crouch => 1f, _ => 2.5f }
			+ (c.HasFiringAngle ? 0f : 6f);
		score += retreat
			? -0.3f * c.ThreatDistance
			: 0.4f * Math.Max(0f, myThreatDistance - c.ThreatDistance);
		return score;
	}
}
