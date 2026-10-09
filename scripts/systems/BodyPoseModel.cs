using Godot;

namespace ProjectD;

/// <summary>Stance pose blending for the placeholder body (GDD s6). Pure static functions.</summary>
public static class BodyPoseModel
{
	/// <summary>
	/// Blend weights (Stand, Crouch, Prone, summing to 1) from the current eye height.
	/// Between standing and crouching eye height it mixes those two; below, crouch and prone.
	/// </summary>
	public static (float Stand, float Crouch, float Prone) StanceBlend(
		float eyeHeight, float standEye, float crouchEye, float proneEye)
	{
		if (eyeHeight >= crouchEye)
		{
			var span = standEye - crouchEye;
			var t = span > 0.0001f ? Mathf.Clamp((eyeHeight - crouchEye) / span, 0f, 1f) : 1f;
			return (t, 1f - t, 0f);
		}
		var low = crouchEye - proneEye;
		var p = low > 0.0001f ? Mathf.Clamp((eyeHeight - proneEye) / low, 0f, 1f) : 1f;
		return (0f, p, 1f - p);
	}
}
