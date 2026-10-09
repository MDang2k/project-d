using Godot;

namespace ProjectD;

public enum Stance { Stand, Crouch, Prone }

/// <summary>Speeds, stance sizes and inertia numbers (GDD s6).</summary>
public sealed record MovementTuning(
	float WalkSpeed,
	float SprintSpeed,
	float CrouchSpeed,
	float CrouchSprintSpeed,
	float ProneSpeed,
	float Acceleration,
	float Deceleration,
	float AirControl,
	float StandHeight,
	float CrouchHeight,
	float ProneHeight,
	float StandEyeHeight,
	float CrouchEyeHeight,
	float ProneEyeHeight)
{
	public static MovementTuning Default => new(
		WalkSpeed: 2.8f, SprintSpeed: 5.2f, CrouchSpeed: 1.6f, CrouchSprintSpeed: 3.0f, ProneSpeed: 0.7f,
		Acceleration: 10f, Deceleration: 14f, AirControl: 0.2f,
		StandHeight: 1.8f, CrouchHeight: 1.2f, ProneHeight: 0.6f,
		StandEyeHeight: 1.65f, CrouchEyeHeight: 1.1f, ProneEyeHeight: 0.4f);
}

/// <summary>Speed selection and horizontal inertia. Pure logic, no nodes.</summary>
public sealed class MovementModel
{
	private readonly MovementTuning _t;
	private readonly WeightModel _weights;

	public MovementModel(MovementTuning tuning, WeightModel weights)
	{
		_t = tuning;
		_weights = weights;
	}

	/// <summary>Prone never sprints; sprinting while crouched is the crouch-sprint.</summary>
	public float TargetSpeed(Stance stance, bool sprinting, WeightTier tier)
	{
		var baseSpeed = stance switch
		{
			Stance.Prone => _t.ProneSpeed,
			Stance.Crouch => sprinting ? _t.CrouchSprintSpeed : _t.CrouchSpeed,
			_ => sprinting ? _t.SprintSpeed : _t.WalkSpeed,
		};
		return baseSpeed * _weights.SpeedMult(tier);
	}

	/// <summary>
	/// Moves current horizontal velocity toward wishDir * speed. Heavier tiers accelerate
	/// and brake more slowly; airborne control is reduced.
	/// </summary>
	public Vector2 StepHorizontal(Vector2 current, Vector2 wishDir, float speed,
		WeightTier tier, bool grounded, float delta)
	{
		var target = wishDir * speed;
		var speedingUp = target.Length() >= current.Length();
		var rate = speedingUp ? _t.Acceleration : _t.Deceleration;
		rate *= _weights.AccelMult(tier);
		if (!grounded)
			rate *= _t.AirControl;
		return current.MoveToward(target, rate * delta);
	}

	public float CapsuleHeight(Stance stance) => stance switch
	{
		Stance.Prone => _t.ProneHeight,
		Stance.Crouch => _t.CrouchHeight,
		_ => _t.StandHeight,
	};

	public float EyeHeight(Stance stance) => stance switch
	{
		Stance.Prone => _t.ProneEyeHeight,
		Stance.Crouch => _t.CrouchEyeHeight,
		_ => _t.StandEyeHeight,
	};
}
