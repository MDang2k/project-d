using System;
using Godot;

namespace ProjectD;

/// <summary>Secondary motion of a worn piece (backpack, plate carrier): how it follows the body. Placeholders to tune.</summary>
public sealed record GearSwingTuning(
	float Stiffness = 90f,
	float Damping = 0.35f,
	/// <summary>Roll kick in degrees per second per degree per second of body yaw rate.</summary>
	float YawGain = 0.9f,
	/// <summary>Pitch kick in degrees per second per m/s2 of forward acceleration.</summary>
	float AccelGain = 25f,
	float MaxDeg = 25f,
	/// <summary>Heavier gear swings less and slower: stiffness grows and the kick shrinks per kilogram.</summary>
	float StiffnessPerKg = 0.04f,
	float KickDropPerKg = 0.06f);

/// <summary>
/// A spring-damper on the rotation of a worn piece, driven by the body's yaw rate and acceleration (animation spec
/// s1.5): turning swings a backpack the other way, speeding up tips it back, and stance changes bump it. Pure logic;
/// a node applies <see cref="RotationDeg"/> to the gear mesh.
/// </summary>
public sealed class GearSwing
{
	private readonly Spring3 _spring = new();
	private readonly GearSwingTuning _t;

	public GearSwing(GearSwingTuning? tuning = null) => _t = tuning ?? new GearSwingTuning();

	/// <summary>Pitch (x), yaw (y) and roll (z) in degrees, clamped.</summary>
	public Vector3 RotationDeg
	{
		get
		{
			var v = _spring.Value;
			return new Vector3(Math.Clamp(v.X, -_t.MaxDeg, _t.MaxDeg), Math.Clamp(v.Y, -_t.MaxDeg, _t.MaxDeg), Math.Clamp(v.Z, -_t.MaxDeg, _t.MaxDeg));
		}
	}

	/// <param name="yawRateDegS">Body yaw rate, positive turning left.</param>
	/// <param name="forwardAccel">Acceleration along the body's forward axis, m/s2 (positive speeding up).</param>
	/// <param name="weightKg">Mass of the piece and what it carries.</param>
	public void Update(float delta, float yawRateDegS, float forwardAccel, float weightKg)
	{
		var kick = 1f / (1f + _t.KickDropPerKg * Math.Max(0f, weightKg));
		var stiffness = _t.Stiffness * (1f + _t.StiffnessPerKg * Math.Max(0f, weightKg));
		// the pack stays behind the turn and the speed-up: a force opposite to them
		_spring.Z.Impulse(-yawRateDegS * _t.YawGain * kick * delta);
		_spring.X.Impulse(-forwardAccel * _t.AccelGain * kick * delta);
		_spring.Update(delta, stiffness, _t.Damping);
	}

	/// <summary>A sudden shove (dropping to the ground shifts a pack up the back), degrees per second.</summary>
	public void Bump(Vector3 velocityDegS) => _spring.Impulse(velocityDegS);

	public void Reset() => _spring.Reset();
}
