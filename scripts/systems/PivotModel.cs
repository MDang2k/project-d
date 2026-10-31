using System;

namespace ProjectD;

/// <summary>Torso cone, pivot step and the first-person cues of a pivot (animation spec s1.5). Placeholders to tune.</summary>
public sealed record PivotTuning(
	float ConeStandDeg = 50f,
	float ConeCrouchDeg = 45f,
	float ConeProneDeg = 30f,
	/// <summary>Pelvis yaw rate while the body follows the aim (moving).</summary>
	float FollowDegS = 360f,
	/// <summary>Pelvis yaw rate during a pivot step: 90 degrees in 0.45 s.</summary>
	float PivotDegS = 200f,
	/// <summary>Moving faster than this follows the aim without a pivot step, m/s.</summary>
	float MovingSpeed = 1.2f,
	/// <summary>The pivot ends when the torso twist is down to this.</summary>
	float SquaredDeg = 0.5f,
	float DipM = 0.015f,
	float DipS = 0.25f,
	float JitterDeg = 0.4f,
	float JitterS = 0.3f,
	float SwayBoost = 1.6f,
	float SwayBoostS = 0.4f);

/// <summary>
/// Splits the aim yaw into a pelvis yaw and a torso twist. Inside the cone the hips and feet stay pinned and only the
/// upper body twists; past it a pivot step turns the pelvis to square with the aim again. Moving, or prone, the pelvis
/// simply follows the aim. The sum of pelvis yaw and twist is always the aim yaw. Pure logic; the player and the AI
/// feed it the yaw they aim at and apply the pelvis yaw to the legs and the twist to spine, chest and head.
/// </summary>
public sealed class PivotModel
{
	private readonly PivotTuning _t;
	private float _cueS = 99f;

	public PivotModel(PivotTuning? tuning = null) => _t = tuning ?? new PivotTuning();

	public PivotTuning Tuning => _t;

	/// <summary>Yaw the hips and feet face, degrees (positive turns left, as Godot's Y rotation).</summary>
	public float PelvisYawDeg { get; private set; }

	/// <summary>Aim yaw minus pelvis yaw, -180..180: what the upper body twists.</summary>
	public float TwistDeg { get; private set; }

	public bool Pivoting { get; private set; }

	/// <summary>+1 while turning left (counter-clockwise from above), -1 right.</summary>
	public int Direction { get; private set; }

	/// <summary>Raised once when a pivot step begins, with its direction; the foot-drag sound hangs on it.</summary>
	public event Action<int>? PivotStarted;

	public void Reset(float yawDeg)
	{
		PelvisYawDeg = yawDeg;
		TwistDeg = 0f;
		Pivoting = false;
		_cueS = 99f;
	}

	public static float WrapDeg(float deg)
	{
		deg %= 360f;
		if (deg > 180f)
			deg -= 360f;
		else if (deg < -180f)
			deg += 360f;
		return deg;
	}

	/// <summary>Half-angle of the cone for a stance mix.</summary>
	public static float ConeDeg(PivotTuning t, (float Stand, float Crouch, float Prone) w)
		=> w.Stand * t.ConeStandDeg + w.Crouch * t.ConeCrouchDeg + w.Prone * t.ConeProneDeg;

	private static float Toward(float current, float target, float maxStep)
	{
		var diff = WrapDeg(target - current);
		return Math.Abs(diff) <= maxStep ? current + diff : current + Math.Sign(diff) * maxStep;
	}

	/// <param name="stance">Stand, crouch and prone weights (BodyPoseModel.StanceBlend).</param>
	/// <param name="locked">Mantling, a locked stance change or airborne: no new pivot starts, the pelvis stays.</param>
	/// <param name="turnRateDegS">Overrides both pelvis rates (the AI uses its own turn speeds).</param>
	public void Update(float delta, float aimYawDeg, (float Stand, float Crouch, float Prone) stance, float groundSpeed,
		bool locked, float? turnRateDegS = null)
	{
		_cueS += delta;
		var cone = ConeDeg(_t, stance);
		var followRate = turnRateDegS ?? _t.FollowDegS;
		var pivotRate = turnRateDegS ?? _t.PivotDegS;
		var twist = WrapDeg(aimYawDeg - PelvisYawDeg);

		if (stance.Prone >= 0.5f || groundSpeed > _t.MovingSpeed)
		{
			// lying down the turn is the prone turn's business; moving, the legs face where the player aims
			Pivoting = false;
			PelvisYawDeg = stance.Prone >= 0.5f ? aimYawDeg : Toward(PelvisYawDeg, aimYawDeg, followRate * delta);
		}
		else if (Pivoting)
		{
			PelvisYawDeg = Toward(PelvisYawDeg, aimYawDeg, pivotRate * delta);
			if (Math.Abs(WrapDeg(aimYawDeg - PelvisYawDeg)) <= _t.SquaredDeg)
				Pivoting = false;
		}
		else if (!locked && Math.Abs(twist) > cone)
		{
			Pivoting = true;
			Direction = twist > 0f ? 1 : -1;
			_cueS = 0f;
			PelvisYawDeg = Toward(PelvisYawDeg, aimYawDeg, pivotRate * delta);
			PivotStarted?.Invoke(Direction);
		}
		TwistDeg = WrapDeg(aimYawDeg - PelvisYawDeg);
	}

	/// <summary>Metres to move the camera down: a dip as the foot lifts (0 when no pivot is fresh).</summary>
	public float CameraDipM => _cueS < _t.DipS ? -_t.DipM * MathF.Sin(MathF.PI * _cueS / _t.DipS) : 0f;

	/// <summary>Quick side-to-side camera yaw wobble in degrees, two cycles that die away.</summary>
	public float JitterDeg => _cueS < _t.JitterS
		? _t.JitterDeg * MathF.Sin(4f * MathF.PI * _cueS / _t.JitterS) * (1f - _cueS / _t.JitterS)
		: 0f;

	/// <summary>Multiplier on the weapon sway amplitude: raised for a moment, then back to 1.</summary>
	public float SwayBoost => _cueS < _t.SwayBoostS ? 1f + (_t.SwayBoost - 1f) * (1f - _cueS / _t.SwayBoostS) : 1f;
}
