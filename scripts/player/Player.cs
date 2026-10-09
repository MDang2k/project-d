using System;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>
/// First-person player body (GDD s6). Thin wrapper: rules live in WeightModel, StaminaPool,
/// MovementModel and LookModel. Camera rig node chain:
/// LeanPivot (lean offset + roll) > Head (eye height, pitch) > FreeLook (free-look offset) > Camera.
/// Weapon sway and recoil nodes join the rig in M2.
/// </summary>
public partial class Player : CharacterBody3D
{
	[Export] public PlayerConfig? Config { get; set; }
	[Export] public float CarriedWeightKg { get; set; } = 15f;
	[Export] public float StrengthBonusKg { get; set; }

	public Stance Stance { get; private set; } = Stance.Stand;
	public WeightTier Tier { get; private set; } = WeightTier.Light;
	public bool IsSprinting { get; private set; }
	public StaminaPool Legs { get; private set; } = null!;
	public StaminaPool Arms { get; private set; } = null!;

	public event Action<Stance>? StanceChanged;
	public event Action? MantleStarted;

	private WeightModel _weights = null!;
	private MovementModel _move = null!;
	private PlayerConfig _cfg = null!;
	private CapsuleShape3D _capsule = null!;
	private CollisionShape3D _collision = null!;
	private Node3D _pivot = null!;
	private Node3D _head = null!;
	private Node3D _free = null!;

	private float _gravity = 9.8f;
	private float _eyeY = 1.65f;
	private float _lean;
	private Vector2 _freeLook = Vector2.Zero;
	private bool _mantling;
	private float _mantleT;
	private float _mantleDuration = 0.7f;
	private Vector3 _mantleFrom;
	private Vector3 _mantleTo;

	public override void _Ready()
	{
		_cfg = Config ?? new PlayerConfig();
		_collision = GetNode<CollisionShape3D>("Collision");
		_pivot = GetNode<Node3D>("LeanPivot");
		_head = GetNode<Node3D>("LeanPivot/Head");
		_free = GetNode<Node3D>("LeanPivot/Head/FreeLook");

		_weights = new WeightModel(_cfg.ToWeightTuning());
		_move = new MovementModel(_cfg.ToMovementTuning(), _weights);
		Legs = new StaminaPool(_cfg.LegsMax, _cfg.ResumeThreshold, _cfg.RecoveryDelayS);
		Arms = new StaminaPool(_cfg.ArmsMax, _cfg.ResumeThreshold, _cfg.RecoveryDelayS);
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsSingle();

		_capsule = new CapsuleShape3D { Radius = _cfg.CapsuleRadius };
		_collision.Shape = _capsule;
		ApplyStanceShape();
		_eyeY = _move.EyeHeight(Stance);
		UpdateRig(0f);
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseMotion mm when Input.MouseMode == Input.MouseModeEnum.Captured:
				Look(mm.Relative);
				break;
			case InputEventMouseButton when Input.MouseMode != Input.MouseModeEnum.Captured:
				Input.MouseMode = Input.MouseModeEnum.Captured;
				break;
			case InputEventKey { PhysicalKeycode: Key.Escape }:
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
		}
	}

	private void Look(Vector2 rel)
	{
		if (Input.IsActionPressed("free_look"))
		{
			_freeLook = LookModel.FreeLookApply(_freeLook, rel, Mathf.RadToDeg(_cfg.MouseSensitivity),
				_cfg.FreeLookYawDeg, _cfg.FreeLookPitchDeg);
			return;
		}
		var turn = _cfg.MouseSensitivity * _weights.TurnMult(Tier);
		if (IsSprinting)
			turn *= _cfg.SprintTurnMult;
		RotateY(-rel.X * turn);
		var limit = Mathf.DegToRad(_cfg.PitchLimitDeg);
		var rot = _head.Rotation;
		rot.X = Mathf.Clamp(rot.X - rel.Y * _cfg.MouseSensitivity, -limit, limit);
		_head.Rotation = rot;
	}

	public override void _PhysicsProcess(double deltaD)
	{
		var delta = (float)deltaD;
		Tier = _weights.TierFor(CarriedWeightKg, StrengthBonusKg);
		if (_mantling)
		{
			StepMantle(delta);
			TickStamina(delta);
			UpdateRig(delta);
			return;
		}
		HandleStanceInput();

		var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		if (Input.IsActionJustPressed("jump") && IsOnFloor() && input.Y < -0.1f
			&& Stance == Stance.Stand && TryMantle())
			return;

		var wish = GlobalBasis * new Vector3(input.X, 0f, input.Y);
		var wantsSprint = Input.IsActionPressed("sprint") && input.Y < -0.1f && Stance != Stance.Prone;
		IsSprinting = wantsSprint && _weights.CanSprint(Tier) && !Legs.IsExhausted;

		var speed = _move.TargetSpeed(Stance, IsSprinting, Tier);
		var horizontal = _move.StepHorizontal(new Vector2(Velocity.X, Velocity.Z),
			new Vector2(wish.X, wish.Z), speed, Tier, IsOnFloor(), delta);
		var vel = Velocity;
		vel.X = horizontal.X;
		vel.Z = horizontal.Y;

		if (IsOnFloor())
		{
			if (Input.IsActionJustPressed("jump") && Stance == Stance.Stand && !Legs.IsExhausted)
			{
				vel.Y = _cfg.JumpVelocity;
				Legs.Spend(_cfg.JumpStaminaCost * _weights.StaminaDrainMult(Tier));
			}
		}
		else
		{
			vel.Y -= _gravity * delta;
		}
		Velocity = vel;

		if (IsSprinting && input != Vector2.Zero)
			Legs.Spend(_cfg.SprintDrainPerS * _weights.StaminaDrainMult(Tier) * delta);
		TickStamina(delta);
		MoveAndSlide();
		UpdateRig(delta);
	}

	private void TickStamina(float delta)
	{
		var recovery = _cfg.RecoveryPerS * _weights.StaminaRecoveryMult(Tier);
		Legs.Tick(delta, recovery);
		Arms.Tick(delta, recovery);
	}

	private void UpdateRig(float delta)
	{
		var allowed = Stance != Stance.Prone && !IsSprinting && !_mantling;
		var target = LookModel.LeanTarget(Input.IsActionPressed("lean_left"),
			Input.IsActionPressed("lean_right"), allowed);
		_lean = LookModel.LeanStep(_lean, target, _cfg.LeanSpeed, delta);
		_eyeY = Mathf.MoveToward(_eyeY, _move.EyeHeight(Stance), _cfg.EyeHeightSpeed * delta);
		var pivotY = _eyeY * _cfg.LeanPivotFraction;
		var shift = ClampLeanToWalls(_lean * _cfg.LeanDistance);
		var ratio = _cfg.LeanDistance > 0f ? shift / _cfg.LeanDistance : 0f;
		_pivot.Position = new Vector3(shift, pivotY, 0f);
		_pivot.Rotation = new Vector3(0f, 0f, -Mathf.DegToRad(_cfg.LeanRollDeg) * ratio);
		_head.Position = new Vector3(0f, _eyeY - pivotY, 0f);

		if (!Input.IsActionPressed("free_look"))
			_freeLook = LookModel.FreeLookReturn(_freeLook, _cfg.FreeLookReturnDegS, delta);
		_free.Rotation = new Vector3(Mathf.DegToRad(_freeLook.Y), Mathf.DegToRad(_freeLook.X), 0f);
	}

	/// <summary>Shortens a lean so the head never ends up inside a wall.</summary>
	private float ClampLeanToWalls(float side)
	{
		if (Mathf.Abs(side) < 0.001f)
			return side;
		var sign = Mathf.Sign(side);
		var origin = GlobalPosition + Vector3.Up * _eyeY;
		var hit = Ray(origin, origin + GlobalBasis.X * sign * (Mathf.Abs(side) + _cfg.LeanWallMargin));
		if (hit.Count == 0)
			return side;
		var freeDist = origin.DistanceTo(hit["position"].AsVector3()) - _cfg.LeanWallMargin;
		return sign * Mathf.Clamp(freeDist, 0f, Mathf.Abs(side));
	}

	/// <summary>Mantle (GDD s6): Space while moving forward at a ledge between min and max height.</summary>
	private bool TryMantle()
	{
		if (Legs.IsExhausted)
			return false;
		var fwd = -GlobalBasis.Z;
		fwd.Y = 0f;
		fwd = fwd.Normalized();
		var b = GlobalPosition;
		var lowFrom = b + Vector3.Up * _cfg.MantleMinHeight;
		var wall = Ray(lowFrom, lowFrom + fwd * _cfg.MantleReach);
		if (wall.Count == 0)
			return false;
		var highFrom = b + Vector3.Up * (_cfg.MantleMaxHeight + 0.05f);
		if (Ray(highFrom, highFrom + fwd * _cfg.MantleReach).Count != 0)
			return false;
		var edge = wall["position"].AsVector3();
		var topFrom = new Vector3(edge.X, highFrom.Y, edge.Z) + fwd * 0.1f;
		var top = Ray(topFrom, topFrom + Vector3.Down * (_cfg.MantleMaxHeight + 0.05f));
		if (top.Count == 0)
			return false;
		var topY = top["position"].AsVector3().Y;
		if (!LookModel.MantleHeightOk(topY - b.Y, _cfg.MantleMinHeight, _cfg.MantleMaxHeight))
			return false;
		var landing = new Vector3(edge.X, topY, edge.Z) + fwd * _cfg.MantleTopDepth;
		if (!CanFitAt(landing, Stance.Stand))
			return false;

		_mantleFrom = b;
		_mantleTo = landing;
		_mantleT = 0f;
		_mantleDuration = _cfg.MantleDurationS / Mathf.Max(0.1f, _weights.SpeedMult(Tier));
		_mantling = true;
		IsSprinting = false;
		Velocity = Vector3.Zero;
		Legs.Spend(_cfg.MantleStaminaCost * _weights.StaminaDrainMult(Tier));
		MantleStarted?.Invoke();
		return true;
	}

	private void StepMantle(float delta)
	{
		_mantleT += delta / _mantleDuration;
		GlobalPosition = LookModel.MantlePosition(_mantleFrom, _mantleTo, Mathf.Min(_mantleT, 1f));
		if (_mantleT >= 1f)
		{
			_mantling = false;
			Velocity = Vector3.Zero;
		}
	}

	private Dictionary Ray(Vector3 from, Vector3 to)
	{
		var q = PhysicsRayQueryParameters3D.Create(from, to, CollisionMask, new Array<Rid> { GetRid() });
		return GetWorld3D().DirectSpaceState.IntersectRay(q);
	}

	private void HandleStanceInput()
	{
		if (Input.IsActionJustPressed("crouch"))
			ToggleStance(Stance.Crouch);
		else if (Input.IsActionJustPressed("prone"))
			ToggleStance(Stance.Prone);
		else if (Input.IsActionJustPressed("jump") && Stance != Stance.Stand)
			SetStance(Stance.Stand);
	}

	private void ToggleStance(Stance target) => SetStance(Stance == target ? Stance.Stand : target);

	private void SetStance(Stance next)
	{
		if (next == Stance)
			return;
		if (_move.CapsuleHeight(next) > _move.CapsuleHeight(Stance) && !CanFitAt(GlobalPosition, next))
			return;
		Stance = next;
		ApplyStanceShape();
		StanceChanged?.Invoke(Stance);
	}

	private void ApplyStanceShape()
	{
		var h = _move.CapsuleHeight(Stance);
		_capsule.Height = h;
		_collision.Position = new Vector3(0f, h * 0.5f, 0f);
	}

	private bool CanFitAt(Vector3 feet, Stance target)
	{
		var h = _move.CapsuleHeight(target);
		var probe = new CapsuleShape3D { Radius = _cfg.CapsuleRadius, Height = h - 0.04f };
		var q = new PhysicsShapeQueryParameters3D
		{
			Shape = probe,
			Transform = new Transform3D(Basis.Identity, feet + Vector3.Up * (h * 0.5f + 0.02f)),
			CollisionMask = CollisionMask,
			Exclude = new Array<Rid> { GetRid() },
		};
		return GetWorld3D().DirectSpaceState.IntersectShape(q, 1).Count == 0;
	}
}
