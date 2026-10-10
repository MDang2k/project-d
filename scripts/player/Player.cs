using System;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>
/// First-person player body (GDD s6). Thin wrapper: rules live in WeightModel, StaminaPool,
/// MovementModel and LookModel. Camera rig node chain:
/// LeanPivot (lean offset + roll) > Head (eye height, pitch) > Recoil (view kick) >
/// FreeLook (free-look offset) > Camera, with WeaponSway (sway + hip/ADS position) under Recoil.
/// Weapon sway and recoil nodes join the rig in M2.
/// </summary>
public partial class Player : CharacterBody3D
{
	[Export] public PlayerConfig? Config { get; set; }
	[Export] public float CarriedWeightKg { get; set; } = 15f;
	[Export] public float StrengthBonusKg { get; set; }
	[Export] public WeaponData? StartWeapon { get; set; }
	[Export] public HealthConfig? HealthConfig { get; set; }

	public Stance Stance { get; private set; } = Stance.Stand;
	public WeightTier Tier { get; private set; } = WeightTier.Light;
	public bool IsSprinting { get; private set; }
	public StaminaPool Legs { get; private set; } = null!;
	public StaminaPool Arms { get; private set; } = null!;

	public event Action<Stance>? StanceChanged;
	public event Action? MantleStarted;

	/// <summary>Short player-facing feedback (mag/chamber check results, empty click).</summary>
	public event Action<string>? WeaponMessage;

	public WeaponNode? Weapon => _body.HeldItem;

	/// <summary>Per-limb health and worn armor (hitboxes for AI shots arrive with M5).</summary>
	public HealthComponent Health => _health ??= CreateHealth();

	/// <summary>0 deepest crouch .. 1 standing height (prone 0).</summary>
	public float HeightLevel => _heights.LevelFor(Stance);
	/// <summary>Current spring-arm retraction of the weapon in metres (debug).</summary>
	public float WeaponRetract => _clearance.Retract;
	public bool IsHoldingBreath { get; private set; }
	public float RestAmount => _support.RestAmount;
	public bool BipodDeployed => _support.BipodDeployed;
	public float AdsAmount => _aim.Amount;
	public float SwayAmplitudeDeg { get; private set; }
	public Vector2 RecoilOffsetDeg => _recoil.Offset;
	/// <summary>Dev/screenshot helper: holds aim-down-sights regardless of input.</summary>
	public bool DebugForceAds { get; set; }

	/// <summary>Dev/screenshot helper: holds the trigger regardless of input.</summary>
	public bool DebugForceTrigger { get; set; }

	private WeightModel _weights = null!;
	private MovementModel _move = null!;
	private PlayerConfig _cfg = null!;
	private CapsuleShape3D _capsule = null!;
	private CollisionShape3D _collision = null!;
	private Node3D _pivot = null!;
	private Node3D _head = null!;
	private Node3D _free = null!;
	private PlayerBody _body = null!;
	private Node3D _recoilNode = null!;
	private Node3D _swayNode = null!;
	private Camera3D _camera = null!;
	private HandlingTuning _handling = null!;
	private readonly SwayModel _swayModel = new();
	private readonly RecoilModel _recoil = new();
	private readonly AimModel _aim = new();
	private SupportModel _support = null!;
	private HealthComponent? _health;
	private StanceHeightModel _heights = null!;
	private ClearanceModel _clearance = null!;
	private bool _crouchAdjusted;
	private ProjectileManager? _projectiles;

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
		_recoilNode = GetNode<Node3D>("LeanPivot/Head/Recoil");
		_free = GetNode<Node3D>("LeanPivot/Head/Recoil/FreeLook");
		_swayNode = GetNode<Node3D>("LeanPivot/Head/Recoil/WeaponSway");
		_camera = GetNode<Camera3D>("LeanPivot/Head/Recoil/FreeLook/Camera");
		_handling = _cfg.ToHandlingTuning();
		_support = new SupportModel(_cfg.ToSupportTuning());
		_heights = new StanceHeightModel(_cfg.HeightWheelStep);
		_clearance = new ClearanceModel(_cfg.ToClearanceTuning());

		_weights = new WeightModel(_cfg.ToWeightTuning());
		_move = new MovementModel(_cfg.ToMovementTuning(), _weights);
		Legs = new StaminaPool(_cfg.LegsMax, _cfg.ResumeThreshold, _cfg.RecoveryDelayS);
		Arms = new StaminaPool(_cfg.ArmsMax, _cfg.ResumeThreshold, _cfg.RecoveryDelayS);
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsSingle();

		_capsule = new CapsuleShape3D { Radius = _cfg.CapsuleRadius };
		_collision.Shape = _capsule;
		ApplyStanceShape();
		_eyeY = _move.EyeHeight(Stance, _heights.CrouchLevel);
		_body = new PlayerBody { Name = "Body" };
		AddChild(_body);
		_body.Build(_cfg, _head, _swayNode);
		_camera.Near = 0.03f;
		_camera.Fov = _cfg.HipFovDeg;
		UpdateRig(0f);
		if (StartWeapon is not null)
			EquipWeapon(StartWeapon);
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseMotion mm when Input.MouseMode == Input.MouseModeEnum.Captured:
				Look(mm.Relative);
				break;
			case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel
				when Input.IsActionPressed("crouch"):
				AdjustHeight(wheel.ButtonIndex == MouseButton.WheelUp ? 1f : -1f);
				GetViewport().SetInputAsHandled();
				break;
			case InputEventMouseButton when Input.MouseMode != Input.MouseModeEnum.Captured:
				Input.MouseMode = Input.MouseModeEnum.Captured;
				break;
			case InputEventKey { PhysicalKeycode: Key.Escape }:
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
		}
	}

	private HealthComponent CreateHealth()
	{
		var h = new HealthComponent { Name = "Health", Config = HealthConfig };
		AddChild(h);
		return h;
	}

	/// <summary>Replaces the held weapon (full magazine, chambered).</summary>
	public void EquipWeapon(WeaponData data)
	{
		_support.FoldBipod();
		var weapon = WeaponNode.Create(data);
		weapon.ShotFired += OnShotFired;
		weapon.DryFired += () => WeaponMessage?.Invoke(GameText.Get("msg.weapon.click"));
		weapon.Controller.MagChecked += r => WeaponMessage?.Invoke(GameText.ForMagCheck(r));
		weapon.Controller.ChamberChecked += loaded =>
			WeaponMessage?.Invoke(GameText.Get(loaded ? "msg.chamber.loaded" : "msg.chamber.empty"));
		weapon.Controller.MagazineDropped += _ => SpawnDroppedMagazine(weapon);
		_body.AttachHeldItem(weapon);
	}

	private void OnShotFired(AmmoType ammo, Vector3 position, Vector3 direction)
	{
		_projectiles ??= GetTree().GetFirstNodeInGroup("projectiles") as ProjectileManager;
		_projectiles?.Spawn(ammo, position, direction, GetRid());
		if (Weapon is { } w)
			_recoil.Kick(w.Data.ToRecoilTuning(),
				HandlingModel.RecoilMultiplier(_handling, Stance, _aim.Amount, _support.RestAmount,
					_support.BipodDeployed),
				GD.Randf() * 2f - 1f);
	}

	private bool _muzzleSurface;
	private bool _wasRested;
	private float _restSnap;

	/// <summary>Height of the surface the weapon rests on (debug overlay), NaN when none.</summary>
	public float RestSurfaceHeight { get; private set; } = float.NaN;

	/// <summary>
	/// Probes three points along the weapon (grips, middle, muzzle) with a vertical ray that
	/// starts above the weapon, so a weapon sunk into a low wall still finds its top.
	/// Returns the highest valid surface.
	/// </summary>
	private bool FindRestSurface(WeaponNode weapon, out float surfaceY)
	{
		surfaceY = float.MinValue;
		var found = false;
		var a = (weapon.GripRight.GlobalPosition + weapon.GripLeft.GlobalPosition) * 0.5f;
		var b = weapon.Muzzle.GlobalPosition;
		foreach (var t in new[] { 0f, 0.5f, 1f })
		{
			var p = a.Lerp(b, t);
			var hit = Ray(p + Vector3.Up * _cfg.RestUpReach, p + Vector3.Down * _cfg.RestReach);
			if (hit.Count == 0)
				continue;
			var y = hit["position"].AsVector3().Y;
			if (!_support.IsRestSurface(y - GlobalPosition.Y, hit["normal"].AsVector3().Y))
				continue;
			found = true;
			surfaceY = Mathf.Max(surfaceY, y);
		}
		return found;
	}

	/// <summary>True if a restable horizontal surface lies within RestReach below the point.</summary>
	private bool SurfaceUnder(Vector3 origin)
	{
		var hit = Ray(origin + Vector3.Up * _cfg.RestUpReach, origin + Vector3.Down * _cfg.RestReach);
		return hit.Count > 0
			&& _support.IsRestSurface(hit["position"].AsVector3().Y - GlobalPosition.Y,
				hit["normal"].AsVector3().Y);
	}

	private void ToggleBipod(WeaponNode weapon)
	{
		if (_support.BipodDeployed)
		{
			_support.FoldBipod();
			weapon.BipodDeployed = false;
			WeaponMessage?.Invoke(GameText.Get("msg.bipod.folded"));
			return;
		}
		if (!weapon.Data.HasBipod)
			WeaponMessage?.Invoke(GameText.Get("msg.bipod.none"));
		else if (_support.TryDeployBipod(true, Stance, _muzzleSurface, Rotation.Y))
			WeaponMessage?.Invoke(GameText.Get("msg.bipod.deployed"));
		else
			WeaponMessage?.Invoke(GameText.Get("msg.bipod.no_surface"));
		weapon.BipodDeployed = _support.BipodDeployed;
	}

	/// <summary>
	/// Spring-arm retraction: sweeps a sphere from the camera toward the muzzle and pulls the
	/// weapon back (and tips it up) so it never pokes through walls.
	/// </summary>
	private void UpdateClearance(float delta, WeaponNode weapon)
	{
		var origin = _camera.GlobalPosition;
		var toMuzzle = weapon.Muzzle.GlobalPosition - origin;
		var fullLength = toMuzzle.Length() + _clearance.Retract;
		var travel = fullLength + _cfg.ClearanceMargin;
		var hitDistance = travel;
		if (toMuzzle.LengthSquared() > 0.0001f)
		{
			var q = new PhysicsShapeQueryParameters3D
			{
				Shape = new SphereShape3D { Radius = _cfg.ClearanceRadius },
				Transform = new Transform3D(Basis.Identity, origin),
				Motion = toMuzzle.Normalized() * travel,
				CollisionMask = CollisionMask,
				Exclude = new Array<Rid> { GetRid() },
			};
			var result = GetWorld3D().DirectSpaceState.CastMotion(q);
			if (result.Length > 0)
				hitDistance = result[0] * travel;
		}
		_clearance.Update(delta, _clearance.TargetRetract(fullLength, hitDistance));
	}

	/// <summary>Fast reload drops the old magazine on the ground (GDD s7).</summary>
	private void SpawnDroppedMagazine(WeaponNode weapon)
	{
		var body = new RigidBody3D { Mass = 0.3f, TopLevel = true };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.04f, 0.14f, 0.07f) } });
		body.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.14f, 0.07f) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.2f, 0.22f) },
		});
		var scene = GetTree().CurrentScene;
		scene.AddChild(body);
		body.GlobalPosition = weapon.GlobalPosition + Vector3.Down * 0.1f;
		body.LinearVelocity = GlobalBasis.X * 0.3f + Vector3.Down * 0.5f;
		GetTree().CreateTimer(30.0).Timeout += body.QueueFree;
	}

	private bool AimHeld()
		=> Weapon is not null && !_mantling && !Weapon.Controller.IsBusy
			&& (DebugForceAds || (Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed("aim")));

	/// <summary>ADS blend, breath hold, sway, recoil recovery, weapon position and FOV.</summary>
	private void UpdateHandling(float delta)
	{
		var weapon = Weapon;
		var aimHeld = AimHeld();
		_aim.Update(aimHeld && !IsSprinting, delta, weapon?.Data.AdsTimeS ?? 0.2f);
		IsHoldingBreath = BreathHold.Update(aimHeld && Input.IsActionPressed("sprint"), Arms,
			_cfg.BreathHoldDrainPerS, delta);

		if (weapon is null)
			return;
		var speedFraction = new Vector2(Velocity.X, Velocity.Z).Length() / Mathf.Max(0.1f, _cfg.SprintSpeed);
		var speed = new Vector2(Velocity.X, Velocity.Z).Length();
		var restSurface = FindRestSurface(weapon, out var surfaceY);
		RestSurfaceHeight = restSurface ? surfaceY - GlobalPosition.Y : float.NaN;
		var muzzleSurface = SurfaceUnder(weapon.Muzzle.GlobalPosition);
		if (_support.Update(delta, restSurface, muzzleSurface, weapon.Data.HasBipod, Stance, speed, IsSprinting))
			WeaponMessage?.Invoke(GameText.Get("msg.bipod.folded"));
		weapon.BipodDeployed = _support.BipodDeployed;
		_muzzleSurface = muzzleSurface;
		var rested = _support.RestAmount > 0.6f;
		if (rested != _wasRested)
		{
			_wasRested = rested;
			if (rested)
				WeaponMessage?.Invoke(GameText.Get("msg.weapon.rested"));
		}

		var ctx = new HandlingContext(Stance, weapon.Data.WeightKg, Arms.Fraction, speedFraction,
			_aim.Amount, IsHoldingBreath, _support.RestAmount, _support.BipodDeployed);
		SwayAmplitudeDeg = HandlingModel.SwayAmplitude(_handling, weapon.Data.SwayBaseDeg, ctx);
		var sway = _swayModel.Update(delta, SwayAmplitudeDeg);
		var (posOffset, rotDeg) = WeaponPoseModel.Pose(weapon.Controller.Action, weapon.Controller.Progress);
		_swayNode.Rotation = new Vector3(Mathf.DegToRad(sway.Y + rotDeg.X), Mathf.DegToRad(sway.X + rotDeg.Y),
			Mathf.DegToRad(rotDeg.Z));

		_recoil.Update(delta, weapon.Data.ToRecoilTuning());
		_recoilNode.Rotation = new Vector3(Mathf.DegToRad(_recoil.Offset.Y), Mathf.DegToRad(_recoil.Offset.X), 0f);

		var t = Mathf.SmoothStep(0f, 1f, _aim.Amount);
		var adsPos = new Vector3(0f, -weapon.Data.SightHeight, -_cfg.AdsEyeRelief);
		UpdateClearance(delta, weapon);

		// Weapon visibly settles onto the surface it rests on; ADS keeps the sight line instead.
		var snapTarget = 0f;
		if (restSurface)
		{
			var weaponY = (weapon.GripRight.GlobalPosition.Y + weapon.GripLeft.GlobalPosition.Y) * 0.5f - _restSnap;
			var wanted = surfaceY + weapon.Data.BodySize.Y * 0.5f + 0.01f - weaponY;
			snapTarget = Mathf.Clamp(wanted, -_cfg.RestSnapMax, _cfg.RestSnapMax);
		}
		_restSnap = snapTarget * _support.RestAmount * (1f - t);
		_swayNode.Position = _cfg.HeldItemOffset.Lerp(adsPos, t) + posOffset
			+ new Vector3(0f, _restSnap, _clearance.Retract);
		_swayNode.Rotation += new Vector3(Mathf.DegToRad(_clearance.TiltDeg), 0f, 0f);
		_camera.Fov = Mathf.Lerp(_cfg.HipFovDeg, _cfg.AdsFovDeg, t);
	}

	private void UpdateWeaponInput()
	{
		var weapon = Weapon;
		if (weapon is null)
			return;
		var captured = Input.MouseMode == Input.MouseModeEnum.Captured;
		weapon.CanFire = (captured || DebugForceTrigger) && !IsSprinting && !_mantling
			&& !_clearance.BlocksFire;
		weapon.TriggerHeld = DebugForceTrigger || Input.IsActionPressed("fire");
		if (Input.IsActionJustPressed("fire") || DebugForceTrigger)
			weapon.TriggerPressed = true;
		if (Input.IsActionJustPressed("fire_mode"))
			weapon.State.Fire.CycleMode();
		if (!captured)
			return;
		if (Input.IsActionJustPressed("rest_bipod"))
			ToggleBipod(weapon);
		var c = weapon.Controller;
		if (Input.IsActionJustPressed("reload"))
		{
			// Second tap shortly after the first upgrades the tactical reload to a fast one.
			if (c.IsBusy)
				c.UpgradeToFast();
			else if (!c.StartReload(fast: false))
				WeaponMessage?.Invoke(GameText.Get("msg.reload.no_spare"));
		}
		else if (Input.IsActionJustPressed("chamber_check", exactMatch: true))
		{
			c.StartChamberCheck();
		}
		else if (Input.IsActionJustPressed("mag_check", exactMatch: true))
		{
			c.StartMagCheck();
		}
	}

	/// <summary>Debug/screenshot helper: switches stance without input or headroom checks.</summary>
	public void DebugSetStance(Stance stance)
	{
		Stance = stance;
		ApplyStanceShape();
	}

	/// <summary>Dev helper: same as scrolling the wheel while holding C (positive = up).</summary>
	public void DebugAdjustHeight(float notches) => AdjustHeight(notches);

	/// <summary>Dev helper: same as pressing the bipod key.</summary>
	public void DebugToggleBipod()
	{
		if (Weapon is { } w)
			ToggleBipod(w);
	}

	/// <summary>Debug/screenshot helper: sets camera pitch directly.</summary>
	public void DebugSetPitchDeg(float degrees)
	{
		var rot = _head.Rotation;
		rot.X = Mathf.DegToRad(degrees);
		_head.Rotation = rot;
	}

	private void Look(Vector2 rel)
	{
		if (Input.IsActionPressed("free_look"))
		{
			_freeLook = LookModel.FreeLookApply(_freeLook, rel, Mathf.RadToDeg(_cfg.MouseSensitivity),
				_cfg.FreeLookYawDeg, _cfg.FreeLookPitchDeg);
			return;
		}
		var sens = _cfg.MouseSensitivity * Mathf.Lerp(1f, _cfg.AdsSensitivityMult, _aim.Amount);
		var turn = sens * _weights.TurnMult(Tier);
		if (IsSprinting)
			turn *= _cfg.SprintTurnMult;
		RotateY(-rel.X * turn);
		if (_support.BipodDeployed)
		{
			var r = Rotation;
			r.Y = _support.ClampYaw(r.Y);
			Rotation = r;
		}
		var limit = Mathf.DegToRad(_cfg.PitchLimitDeg);
		var rot = _head.Rotation;
		rot.X = Mathf.Clamp(rot.X - rel.Y * sens, -limit, limit);
		_head.Rotation = rot;
	}

	public override void _PhysicsProcess(double deltaD)
	{
		var delta = (float)deltaD;
		Tier = _weights.TierFor(CarriedWeightKg, StrengthBonusKg);
		UpdateWeaponInput();
		if (_mantling)
		{
			StepMantle(delta);
			TickStamina(delta);
			UpdateHandling(delta);
			UpdateRig(delta);
			return;
		}
		HandleStanceInput();

		var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		if (Input.IsActionJustPressed("jump") && IsOnFloor() && input.Y < -0.1f
			&& Stance == Stance.Stand && TryMantle())
			return;

		var wish = GlobalBasis * new Vector3(input.X, 0f, input.Y);
		// While aiming, Shift holds breath instead of sprinting (GDD s7).
		var wantsSprint = Input.IsActionPressed("sprint") && input.Y < -0.1f && Stance != Stance.Prone
			&& !AimHeld();
		IsSprinting = wantsSprint && _weights.CanSprint(Tier) && !Legs.IsExhausted;

		var speed = _move.TargetSpeed(Stance, IsSprinting, Tier, _heights.CrouchLevel);
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
		UpdateHandling(delta);
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
		_eyeY = Mathf.MoveToward(_eyeY, _move.EyeHeight(Stance, _heights.CrouchLevel), _cfg.EyeHeightSpeed * delta);
		var pivotY = _eyeY * _cfg.LeanPivotFraction;
		var shift = ClampLeanToWalls(_lean * _cfg.LeanDistance);
		var ratio = _cfg.LeanDistance > 0f ? shift / _cfg.LeanDistance : 0f;
		_pivot.Position = new Vector3(shift, pivotY, 0f);
		_pivot.Rotation = new Vector3(0f, 0f, -Mathf.DegToRad(_cfg.LeanRollDeg) * ratio);
		_head.Position = new Vector3(0f, _eyeY - pivotY, 0f);

		if (!Input.IsActionPressed("free_look"))
			_freeLook = LookModel.FreeLookReturn(_freeLook, _cfg.FreeLookReturnDegS, delta);
		_free.Rotation = new Vector3(Mathf.DegToRad(_freeLook.Y), Mathf.DegToRad(_freeLook.X), 0f);
		_body.Pose(_eyeY, _pivot.Rotation.Z, shift);
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
		// Tap C toggles crouch on release; holding C and scrolling adjusts height instead.
		if (Input.IsActionJustPressed("crouch"))
			_crouchAdjusted = false;
		if (Input.IsActionJustReleased("crouch"))
		{
			if (!_crouchAdjusted)
				ToggleStance(Stance.Crouch);
		}
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
		if (HeightFor(next) > HeightFor(Stance) && !CanFitAt(GlobalPosition, next))
			return;
		Stance = next;
		ApplyStanceShape();
		StanceChanged?.Invoke(Stance);
	}

	private float HeightFor(Stance stance) => _move.CapsuleHeight(stance, _heights.CrouchLevel);

	/// <summary>Hold C and scroll: raises or lowers the crouch, standing up at the top.</summary>
	private void AdjustHeight(float notches)
	{
		_crouchAdjusted = true;
		if (Stance == Stance.Prone)
			return;
		var oldStance = Stance;
		var oldLevel = _heights.CrouchLevel;
		var oldHeight = HeightFor(oldStance);
		var next = _heights.Adjust(oldStance, notches);
		var newHeight = HeightFor(next);
		if (newHeight > oldHeight && !CanFitHeight(GlobalPosition, newHeight))
		{
			_heights.Restore(oldLevel);
			return;
		}
		var changed = next != Stance;
		Stance = next;
		ApplyStanceShape();
		if (changed)
			StanceChanged?.Invoke(Stance);
	}

	private void ApplyStanceShape()
	{
		var h = HeightFor(Stance);
		_capsule.Height = h;
		_collision.Position = new Vector3(0f, h * 0.5f, 0f);
	}

	private bool CanFitAt(Vector3 feet, Stance target) => CanFitHeight(feet, HeightFor(target));

	private bool CanFitHeight(Vector3 feet, float h)
	{
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
