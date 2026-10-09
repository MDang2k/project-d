using Godot;

namespace ProjectD;

/// <summary>Lean, free look and mantle maths (GDD s6). Pure static functions.</summary>
public static class LookModel
{
	/// <summary>Lean intent in [-1, 1]: -1 left, +1 right. Blocked while prone or sprinting.</summary>
	public static float LeanTarget(bool left, bool right, bool allowed)
	{
		if (!allowed)
			return 0f;
		return (right ? 1f : 0f) - (left ? 1f : 0f);
	}

	public static float LeanStep(float current, float target, float speed, float delta)
		=> Mathf.MoveToward(current, target, speed * delta);

	/// <summary>Free look offset in degrees (x = yaw, y = pitch), clamped to limits.</summary>
	public static Vector2 FreeLookApply(Vector2 offset, Vector2 mouseRelative, float sensitivityDeg,
		float yawLimitDeg, float pitchLimitDeg)
	{
		var v = offset + new Vector2(-mouseRelative.X, -mouseRelative.Y) * sensitivityDeg;
		return new Vector2(Mathf.Clamp(v.X, -yawLimitDeg, yawLimitDeg),
			Mathf.Clamp(v.Y, -pitchLimitDeg, pitchLimitDeg));
	}

	public static Vector2 FreeLookReturn(Vector2 offset, float speedDeg, float delta)
		=> offset.MoveToward(Vector2.Zero, speedDeg * delta);

	/// <summary>Mantle path: rise first (t 0..0.6), then travel forward (t 0.4..1).</summary>
	public static Vector3 MantlePosition(Vector3 from, Vector3 to, float t)
	{
		var up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(t / 0.6f, 0f, 1f));
		var fwd = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - 0.4f) / 0.6f, 0f, 1f));
		return new Vector3(Mathf.Lerp(from.X, to.X, fwd), Mathf.Lerp(from.Y, to.Y, up),
			Mathf.Lerp(from.Z, to.Z, fwd));
	}

	public static bool MantleHeightOk(float ledgeHeight, float minHeight, float maxHeight)
		=> ledgeHeight >= minHeight && ledgeHeight <= maxHeight;
}
