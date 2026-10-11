using System;
using Godot;

namespace ProjectD;

/// <summary>Weapon wall-clearance numbers (spring-arm retraction).</summary>
public sealed record ClearanceTuning(
	/// <summary>Gap kept between the muzzle and a surface, metres.</summary>
	float Margin,
	float MaxRetract,
	/// <summary>Muzzle-up tilt at full retraction, degrees.</summary>
	float MaxTiltDeg,
	float InSpeed,
	float OutSpeed,
	/// <summary>Fraction of MaxRetract at which firing is blocked.</summary>
	float FireBlockFraction)
{
	public static ClearanceTuning Default => new(
		Margin: 0.04f, MaxRetract: 0.4f, MaxTiltDeg: 25f, InSpeed: 4f, OutSpeed: 2.5f, FireBlockFraction: 0.6f);
}

/// <summary>
/// Spring-arm style weapon retraction. The owner probes from the camera toward the muzzle;
/// this model turns the free distance into a smooth retraction, a tilt and a fire block.
/// Pure logic.
/// </summary>
public sealed class ClearanceModel
{
	private readonly ClearanceTuning _t;

	public ClearanceModel(ClearanceTuning tuning) => _t = tuning;

	/// <summary>Current retraction along the weapon axis, metres (toward the camera).</summary>
	public float Retract { get; private set; }

	public float Fraction => _t.MaxRetract > 0.0001f ? Retract / _t.MaxRetract : 0f;
	public float TiltDeg => Fraction * _t.MaxTiltDeg;
	public bool BlocksFire => Fraction >= _t.FireBlockFraction;

	/// <summary>
	/// How far the weapon must retract so its tip stays clear of the hit.
	/// fullLength is camera-to-muzzle with no retraction; hitDistance is the free distance from
	/// the camera along that line (use fullLength + margin or more when nothing was hit).
	/// </summary>
	public float TargetRetract(float fullLength, float hitDistance)
		=> Math.Clamp(fullLength + _t.Margin - hitDistance, 0f, _t.MaxRetract);

	public void Update(float delta, float target)
	{
		var speed = target > Retract ? _t.InSpeed : _t.OutSpeed;
		Retract = Mathf.MoveToward(Retract, target, speed * delta);
	}
}
