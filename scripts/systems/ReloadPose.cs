using System;
using Godot;

namespace ProjectD;

/// <summary>Places the support hand can be during an action, in weapon space (animation spec s1.3).</summary>
public enum HandAnchor { Grip, MagWell, Pouch, Charging }

/// <summary>
/// Where the left hand goes and how the weapon tilts at one moment of an action: the hand moves from one anchor to
/// another (Blend 0..1), the weapon pivot is eased toward <see cref="Tilt"/> (0 = ready, 1 = fully turned for the reload).
/// </summary>
public readonly record struct ActionPose(HandAnchor From, HandAnchor To, float Blend, float Tilt);

/// <summary>Weapon pivot offsets and anchor points of a reload (placeholder numbers, tune; replaced by authored curves).</summary>
public sealed record ReloadPoseTuning(
	/// <summary>Weapon offset and rotation (pitch, yaw, roll degrees) at Tilt 1.</summary>
	Vector3 PivotPos,
	Vector3 PivotRotDeg,
	/// <summary>The pouch on the body, in weapon space (left, below, behind the grip).</summary>
	Vector3 PouchPos,
	/// <summary>Where the hand holds a magazine relative to the magazine well.</summary>
	Vector3 MagHoldOffset,
	Vector3 ChargingFallback)
{
	public static ReloadPoseTuning Default { get; } = new(
		new Vector3(-0.03f, -0.1f, 0.04f), new Vector3(-12f, 14f, 24f),
		new Vector3(-0.26f, -0.32f, 0.1f), new Vector3(0f, -0.08f, 0f), new Vector3(0f, 0.09f, -0.05f));
}

/// <summary>
/// Reload choreography from the phase and its progress (animation spec s1.3, s5.1): the logic owns time, this only
/// turns "which phase, how far" into hand anchors and a weapon tilt. Pure logic.
/// </summary>
public static class ReloadPose
{
	/// <param name="t">Progress within the phase, 0..1.</param>
	/// <param name="fast">Fast reload: the old magazine is dropped, so the hand goes straight to the pouch.</param>
	/// <param name="racked">The reload includes a bolt rack; without one the hand ends on the magazine well.</param>
	public static ActionPose Evaluate(ReloadPhase phase, float t, bool fast, bool racked)
	{
		t = Math.Clamp(t, 0f, 1f);
		var s = Mathf.SmoothStep(0f, 1f, t);
		return phase switch
		{
			ReloadPhase.Enter => new ActionPose(HandAnchor.Grip, HandAnchor.Grip, 0f, s),
			ReloadPhase.MagDetach => new ActionPose(HandAnchor.Grip, HandAnchor.MagWell, Mathf.SmoothStep(0f, 1f, Math.Min(1f, t / 0.6f)), 1f),
			ReloadPhase.MagStow => new ActionPose(HandAnchor.MagWell, HandAnchor.Pouch, s, 1f),
			ReloadPhase.MagFetch => fast
				? new ActionPose(HandAnchor.MagWell, HandAnchor.Pouch, s, 1f)
				: new ActionPose(HandAnchor.Pouch, HandAnchor.Pouch, 0f, 1f),
			ReloadPhase.MagInsert => new ActionPose(HandAnchor.Pouch, HandAnchor.MagWell, s, 1f),
			ReloadPhase.BoltRack => t < 0.5f
				? new ActionPose(HandAnchor.MagWell, HandAnchor.Charging, Mathf.SmoothStep(0f, 1f, t * 2f), Mathf.Lerp(1f, 0.4f, t * 2f))
				: new ActionPose(HandAnchor.Charging, HandAnchor.Grip, Mathf.SmoothStep(0f, 1f, (t - 0.5f) * 2f), 0.4f),
			ReloadPhase.Exit => racked
				? new ActionPose(HandAnchor.Grip, HandAnchor.Grip, 0f, 0.4f * (1f - s))
				: new ActionPose(HandAnchor.MagWell, HandAnchor.Grip, s, 1f - s),
			_ => new ActionPose(HandAnchor.Grip, HandAnchor.Grip, 0f, 0f),
		};
	}

	/// <summary>Weapon space position of an anchor.</summary>
	public static Vector3 AnchorPosition(HandAnchor anchor, ReloadPoseTuning t, Vector3 grip, Vector3 magWell, Vector3? charging)
		=> anchor switch
		{
			HandAnchor.MagWell => magWell + t.MagHoldOffset,
			HandAnchor.Pouch => t.PouchPos,
			HandAnchor.Charging => charging ?? t.ChargingFallback,
			_ => grip,
		};

	/// <summary>The hand position for a pose.</summary>
	public static Vector3 HandPosition(in ActionPose pose, ReloadPoseTuning t, Vector3 grip, Vector3 magWell, Vector3? charging)
		=> AnchorPosition(pose.From, t, grip, magWell, charging)
			.Lerp(AnchorPosition(pose.To, t, grip, magWell, charging), pose.Blend);
}
