using System;

namespace ProjectD;

public enum ReadyPose { None, High, Low }

/// <summary>Wall tuck stage 2 numbers (animation spec s4.3, tune).</summary>
public sealed record ReadyPoseTuning(
	/// <summary>Clearance fraction at which the arms start moving toward a ready pose.</summary>
	float StartFraction = 0.5f,
	float HighPitchDeg = 60f,
	float LowPitchDeg = 50f,
	/// <summary>An obstruction this far above the bore counts as overhead (low ready).</summary>
	float OverheadMarginM = 0.08f,
	float BlendSpeed = 4f)
{
	public static ReadyPoseTuning Default { get; } = new();
}

/// <summary>Chooses high or low ready from where the obstruction is and blends the pose in. Pure logic.</summary>
public static class ReadyPoseModel
{
	/// <summary>Low ready when the obstruction is above the bore (low ceiling, overhead ledge); otherwise high ready.</summary>
	public static ReadyPose Choose(float hitHeightAboveBoreM, bool overheadBlocked, ReadyPoseTuning t)
		=> overheadBlocked || hitHeightAboveBoreM > t.OverheadMarginM ? ReadyPose.Low : ReadyPose.High;

	/// <summary>0 below the start fraction, 1 at full retraction.</summary>
	public static float Amount(float clearanceFraction, ReadyPoseTuning t)
		=> Math.Clamp((clearanceFraction - t.StartFraction) / Math.Max(0.01f, 1f - t.StartFraction), 0f, 1f);

	/// <summary>Weapon pitch in degrees (positive = muzzle up) for a pose and blend amount.</summary>
	public static float PitchDeg(ReadyPose pose, float amount, ReadyPoseTuning t) => pose switch
	{
		ReadyPose.High => t.HighPitchDeg * amount,
		ReadyPose.Low => -t.LowPitchDeg * amount,
		_ => 0f,
	};
}
