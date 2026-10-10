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
	public float TargetSpeed(Stance stance, bool sprinting, WeightTier tier, float crouchLevel = 0f)
	{
		var standSpeed = sprinting ? _t.SprintSpeed : _t.WalkSpeed;
		var baseSpeed = stance switch
		{
			Stance.Prone => _t.ProneSpeed,
			// A raised crouch (hold C + wheel) moves between crouch and standing speed.
			Stance.Crouch => Mathf.Lerp(sprinting ? _t.CrouchSprintSpeed : _t.CrouchSpeed, standSpeed,
				Mathf.Clamp(crouchLevel, 0f, 1f)),
			_ => standSpeed,
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

	/// <param name="crouchLevel">0 full crouch .. 1 standing height; only used for Crouch.</param>
	public float CapsuleHeight(Stance stance, float crouchLevel = 0f) => stance switch
	{
		Stance.Prone => _t.ProneHeight,
		Stance.Crouch => Mathf.Lerp(_t.CrouchHeight, _t.StandHeight, Mathf.Clamp(crouchLevel, 0f, 1f)),
		_ => _t.StandHeight,
	};

	public float EyeHeight(Stance stance, float crouchLevel = 0f) => stance switch
	{
		Stance.Prone => _t.ProneEyeHeight,
		Stance.Crouch => Mathf.Lerp(_t.CrouchEyeHeight, _t.StandEyeHeight, Mathf.Clamp(crouchLevel, 0f, 1f)),
		_ => _t.StandEyeHeight,
	};
}
