using System;
using Godot;

namespace ProjectD;

/// <summary>
/// Weapon lag behind the camera (animation spec s4.2): turning the view drags the gun the other way, and it
/// catches up on a spring. Heavier weapons, low ergonomics and tired arms lag more. Pure logic.
/// </summary>
public sealed class InertiaSway
{
	private readonly SpringDamper _yaw = new();
	private readonly SpringDamper _pitch = new();

	public float Stiffness { get; set; } = 45f;
	public float DampingRatio { get; set; } = 0.8f;
	public float BaseLag { get; set; } = 0.35f;
	public float MaxDeg { get; set; } = 4f;

	/// <summary>x = yaw, y = pitch, degrees.</summary>
	public Vector2 Offset => new(_yaw.Value, _pitch.Value);

	public static float LagFactor(float baseLag, float weightKg, float ergonomics, float armsFraction)
		=> baseLag * (1f + weightKg * 0.08f) * (1f + (50f - Math.Clamp(ergonomics, 0f, 100f)) / 100f)
			* (1f + (1f - Math.Clamp(armsFraction, 0f, 1f)) * 0.5f);

	/// <param name="lookDeltaDeg">View rotation this frame (x = yaw, y = pitch, degrees).</param>
	public void Update(float delta, Vector2 lookDeltaDeg, float weightKg, float ergonomics, float armsFraction)
	{
		var lag = Math.Min(0.9f, LagFactor(BaseLag, weightKg, ergonomics, armsFraction));
		_yaw.Value = Math.Clamp(_yaw.Value - lookDeltaDeg.X * lag, -MaxDeg, MaxDeg);
		_pitch.Value = Math.Clamp(_pitch.Value - lookDeltaDeg.Y * lag, -MaxDeg, MaxDeg);
		_yaw.Update(delta, Stiffness, DampingRatio);
		_pitch.Update(delta, Stiffness, DampingRatio);
	}

	public void Reset()
	{
		_yaw.Reset();
		_pitch.Reset();
	}
}
