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
public partial class Player : CharacterBody3D, IPerceivable
{
	[Export] public PlayerConfig? Config { get; set; }
	/// <summary>Extra weight on top of the inventory (debug adjust keys); the inventory total is added.</summary>
	[Export] public float CarriedWeightKg { get; set; }
	[Export] public float StrengthBonusKg { get; set; }
	[Export] public WeaponData? StartWeapon { get; set; }
	[Export] public HealthConfig? HealthConfig { get; set; }
	[Export] public MeleeData? MeleeWeapon { get; set; }

	public Stance Stance { get; private set; } = Stance.Stand;
	public WeightTier Tier { get; private set; } = WeightTier.Light;
	public bool IsSprinting { get; private set; }
	public StaminaPool Legs { get; private set; } = null!;
	public StaminaPool Arms { get; private set; } = null!;

	public event Action<Stance>? StanceChanged;
	public event Action? MantleStarted;

	/// <summary>Weapon slot currently in hand (primary, secondary or pistol).</summary>
	public EquipSlot ActiveSlot { get; private set; } = EquipSlot.Primary;

	public bool InventoryOpen => _screen.IsOpen;

	public InventoryScreen Screen => _screen;

	/// <summary>Items bound to the quick-use keys (kept across raids in <see cref="RunState"/>).</summary>
	public QuickUseBar QuickUse => RunState.Current.QuickUse;

	/// <summary>A quick-use key was pressed (slot 0-3); the HUD shows the bar briefly.</summary>
	public event Action<int>? QuickUsePressed;

	/// <summary>Dev helper: same as pressing Tab.</summary>
	public void DebugToggleInventory() => _screen.Toggle();

	/// <summary>Result of a melee strike that landed on a hitbox.</summary>
	public event Action<HitResult>? MeleeHit;

	/// <summary>The player's melee strike dealt the killing damage to this body (quests use it).</summary>
	public event Action<HealthComponent>? MeleeKilled;

	/// <summary>Short player-facing feedback (mag/chamber check results, empty click).</summary>
	public event Action<string>? WeaponMessage;

	public WeaponNode? Weapon => _body.HeldItem;

	/// <summary>Everything worn and carried; created with the starting kit on first use.</summary>
	public InventoryComponent Inventory => _inventory ??= CreateInventory();

	/// <summary>Inventory weight plus any debug extra, in kg.</summary>
	public float TotalCarriedKg => (_inventory?.Inventory.TotalWeightKg ?? 0f) + CarriedWeightKg;

	/// <summary>Per-limb health and worn armor (hitboxes for AI shots arrive with M5).</summary>
	public HealthComponent Health => _health ??= CreateHealth();

	/// <summary>0 deepest crouch .. 1 standing height (prone 0).</summary>
	public float HeightLevel => _heights.LevelFor(Stance);
	/// <summary>Current spring-arm retraction of the weapon in metres (debug).</summary>
	public float WeaponRetract => _clearance.Retract;
	public string ClearanceDebug { get; private set; } = "";
	public Penalties CurrentPenalties => _penalties;
	public bool IsDead => Health.IsDead;
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

	public (float Left, float Right) DebugThighPitchDeg() => _body.DebugThighPitchDeg();
	public string DebugFootIk() => _body.DebugFootIk();

	/// <summary>Dev: constant move input (x right, y back like the real axes) and forced sprint, for screenshots.</summary>
	public Vector2 DebugMove { get; set; }
	public bool DebugSprint { get; set; }

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
	private readonly FieldSwapTimer _swap = new();

	// animation post-pass (animation spec s4): gun and camera springs, inertia, view bob, ready pose
	private readonly ReloadPoseTuning _reloadPose = ReloadPoseTuning.Default;
	private float _reloadTilt;
	private Vector3 _handPos;
	private WeaponNode? _actionWeapon;
	private Vector3 _rightPos;
	private int _clipReloadId = -1;
	private System.Collections.Generic.IReadOnlyList<(float Logic, float Clip)>? _clipWarp;
	private readonly Spring3 _gunRot = new();
	private readonly SpringDamper _gunBack = new();
	private readonly Spring3 _camKick = new();
	private readonly InertiaSway _inertia = new();
	private Vector2 _lookDeltaDeg;
	private readonly SpringDamper _landDip = new();
	// camera height lag after a step (stairs): the body jumps, the camera eases
	private float _stepOffset;
	private float _prevBodyY;
	private float? _prevCamY;

	/// <summary>Dev: largest camera height change in one physics frame since start (stair smoothness check).</summary>
	public float MaxCameraYStep { get; private set; }
	private bool _prevOnFloor;
	private readonly ViewBobTuning _bobTuning = ViewBobTuning.Default;
	private readonly ReadyPoseTuning _readyTuning = ReadyPoseTuning.Default;
	private float _bobPhase;
	private bool _wasOnFloor = true;
	private float _fallSpeed;
	private float _readyAmount;
	private ReadyPose _readyPose = ReadyPose.None;
	private ProgressBar _swapBar = null!;
	private Label _swapLabel = null!;

	/// <summary>A part swap is running: the weapon cannot be fired or aimed (weapon modding v2 s7).</summary>
	public bool SwapActive => _swap.Active;

	/// <summary>Starts a timed swap; sprinting or taking damage cancels it. False when one is already running.</summary>
	public bool BeginFieldSwap(float seconds, string label, Action apply)
		=> !Health.IsDead && _swap.Start(seconds, label, () =>
		{
			apply();
			Inventory.NotifyChanged();
		});

	/// <summary>The held weapon is missing a vital part and cannot chamber or fire.</summary>
	private bool WeaponRedFlagged => Weapon?.Item is { } held && !WeaponBuild.IsComplete(held);
	private readonly AimModel _aim = new();
	private SupportModel _support = null!;
	private HealthComponent? _health;
	private InventoryComponent? _inventory;
	private MeleeModel _melee = null!;
	private InventoryScreen _screen = null!;
	private Label _prompt = null!;
	private ItemInstance? _pendingUse;
	private int? _heldWeaponUid;
	private MagazineLoader _loader = null!;
	private readonly PlayerSkills _skills = new(() => GameState.Instance?.Profile.Skills);
	private readonly System.Collections.Generic.HashSet<LootContainer> _searchTracked = new();
	private ItemInstance? _loadAmmo;
	private PainOverlay _pain = null!;
	private Penalties _penalties = new(1f, 1f, false, 1f, 0f);
	private StanceHeightModel _heights = null!;
	private readonly StanceTransition _transition = new();
	private readonly PivotModel _pivotModel = new();
	private readonly ProneTurnModel _proneTurn = new();
	private readonly CrawlModel _crawl = new();
	private readonly ProneLeanTuning _proneLean = new();
	private float _leanRatio;
	private float _proneViewYaw;
	private float _bodyTwistDeg;
	private PhysicsShapeQueryParameters3D? _proneProbe;

	private float _transitionFromEye;
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
		AddToGroup("perceivables");
		BuildHitboxes();
		// AI movement capsules live on layer 4 so bullets pass through them to the hitboxes.
		CollisionMask |= 1u << 3;
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
		_ = Health;
		_ = Inventory;
		Health.HitTaken += _ => SyncArmorDurability();
		_melee = new MeleeModel(MeleeWeapon?.ToSpec() ?? new MeleeSpec("fists", 12f, 1, 1.4f, 0.2f, 0.5f, 6f));
		_pain = new PainOverlay { Name = "PainOverlay" };
		AddChild(_pain);
		_loader = new MagazineLoader(_cfg.MagLoadSecondsPerRound);
		_loader.Finished += OnLoaderFinished;
		_loader.RoundLoaded += _skills.RoundLoaded;
		Health.EnemyHitTaken += hit => _skills.HitTaken(hit.Damage);
		_loader.RoundUnloaded += OnRoundUnloaded;
		_screen = new InventoryScreen { Name = "InventoryScreen" };
		AddChild(_screen);
		_screen.Bind(this);
		PlayerHud.Attach(this);
		_scope = new ScopeOverlay { Name = "ScopeOverlay" };
		AddChild(_scope);
		var promptLayer = new CanvasLayer { Layer = 2 };
		_prompt = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			Visible = false,
		};
		_prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_prompt.OffsetTop = -160f;
		_prompt.OffsetLeft = -200f;
		_prompt.OffsetRight = 200f;
		promptLayer.AddChild(_prompt);
		_swapBar = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, Visible = false, CustomMinimumSize = new Vector2(240f, 14f) };
		_swapBar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_swapBar.OffsetTop = -120f;
		_swapBar.OffsetBottom = -106f;
		_swapBar.OffsetLeft = -120f;
		_swapBar.OffsetRight = 120f;
		promptLayer.AddChild(_swapBar);
		_swapLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
		_swapLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_swapLabel.OffsetTop = -146f;
		_swapLabel.OffsetLeft = -200f;
		_swapLabel.OffsetRight = 200f;
		promptLayer.AddChild(_swapLabel);
		AddChild(promptLayer);
		Health.HitTaken += _ => _swap.Cancel();
		_swap.Cancelled += () => GD.Print("[swap] cancelled");
		Inventory.Changed += RefreshHeldWeapon;
		Health.MedicalFinished += (_, ok) =>
		{
			FinishPendingUse(ok);
			if (ok)
				_skills.MedUsed();
		};
		Health.ConsumableFinished += _ => FinishPendingUse(true);

		_weights = new WeightModel(_cfg.ToWeightTuning());
		_move = new MovementModel(_cfg.ToMovementTuning(), _weights);
		Legs = new StaminaPool(_cfg.LegsMax, _cfg.ResumeThreshold, _cfg.RecoveryDelayS);
		Arms = new StaminaPool(_cfg.ArmsMax, _cfg.ResumeThreshold, _cfg.RecoveryDelayS);
		FloorSnapLength = 0.35f; // stay on the ground going down stairs
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsSingle();

		_capsule = new CapsuleShape3D { Radius = _cfg.CapsuleRadius };
		_collision.Shape = _capsule;
		ApplyStanceShape();
		_eyeY = _move.EyeHeight(Stance, _heights.CrouchLevel);
		_body = new PlayerBody { Name = "Body" };
		AddChild(_body);
		_body.Build(_cfg, _head, _swayNode, GameState.Instance?.Profile.Side ?? PlayerSide.Allied);
		_pivotModel.Reset(Mathf.RadToDeg(Rotation.Y));
		_proneTurn.Reset(Mathf.RadToDeg(Rotation.Y));
		_proneViewYaw = Mathf.RadToDeg(Rotation.Y);
		_pivotModel.PivotStarted += _ => NoiseBus.Emit(GlobalPosition, 6f, "footstep", this); // the foot drags on the floor
		_camera.Near = 0.03f;
		_camera.Fov = _cfg.HipFovDeg;
		UpdateRig(0f);
		RefreshHeldWeapon();
		if (Weapon is null && StartWeapon is not null && Inventory.Inventory.InSlot(ActiveSlot) is null)
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
			case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Key1 } when !_screen.IsOpen:
				SelectSlot(EquipSlot.Primary);
				break;
			case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Key2 } when !_screen.IsOpen:
				SelectSlot(EquipSlot.Secondary);
				break;
			case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Key3 } when !_screen.IsOpen:
				SelectSlot(EquipSlot.Pistol);
				break;
			case InputEventKey { PhysicalKeycode: Key.Escape }:
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
		}
	}

	private InventoryComponent CreateInventory()
	{
		var inv = new InventoryComponent { Name = "Inventory" };
		_inventory = inv;
		AddChild(inv);
		inv.Changed += SyncWornArmor;
		var run = RunState.Current;
		// A raid adopts the character saved in the hub; the dev kit only exists for ranges opened from the editor.
		if (run.Inventory is { } carried)
			inv.Adopt(carried);
		else
			inv.GiveStartingKit();
		return inv;
	}

	/// <summary>Pushes the vest and helmet from the inventory into the health system.</summary>
	private void SyncWornArmor()
	{
		var worn = new System.Collections.Generic.List<(ArmorSpec, float)>();
		foreach (var slot in new[] { EquipSlot.Vest, EquipSlot.Head })
		{
			if (_inventory!.Inventory.InSlot(slot) is not { } item || ItemDb.Data(item.Spec.Id)?.Payload is not ArmorData data)
				continue;
			var spec = data.ToSpec();
			worn.Add((spec, item.Durability < 0f ? spec.MaxDurability : item.Durability));
		}
		Health.SetArmor(worn);
	}

	/// <summary>Writes armor wear from the health system back onto the item instances.</summary>
	private void SyncArmorDurability()
	{
		if (_inventory is null)
			return;
		foreach (var slot in new[] { EquipSlot.Vest, EquipSlot.Head })
		{
			if (_inventory.Inventory.InSlot(slot) is not { } item)
				continue;
			foreach (var a in Health.Armor)
			{
				if (a.Spec.Id == ItemDb.Data(item.Spec.Id)?.Payload?.Get("Id").AsString())
					item.Durability = a.Durability;
			}
		}
	}

	private HealthComponent CreateHealth()
	{
		var h = new HealthComponent { Name = "Health", Config = HealthConfig };
		h.MaxHpScale = GameState.Instance?.Profile.SkillEffects.MaxHpMultiplier ?? 1f; // the body is built at raid start
		AddChild(h);
		return h;
	}

	/// <summary>
	/// Hands the skill levels to the models that use them (M8a spec s2). Called every frame so a level-up applies at once;
	/// the models read the numbers when they use them. Max HP is the exception: it is set when the body is built.
	/// </summary>
	private void ApplySkillEffects()
	{
		if (GameState.Instance?.Profile.SkillEffects is not { } fx)
			return;
		Legs?.SetScale(fx.StaminaMultiplier);
		if (_health is { } h)
		{
			h.DamageScale = fx.HitDamageMultiplier;
			h.MedTimeScale = fx.MedTimeMultiplier;
		}
		if (Weapon is { } w)
		{
			w.SkillRecoilMultiplier = fx.RecoilMultiplier;
			w.Controller.SkillTimeMultiplier = fx.HandlingTimeMultiplier;
			w.Controller.ExactMagCheck = fx.ExactMagCheck;
		}
		if (_screen?.Container is { } open)
			open.SearchSpeed = 1f / MathF.Max(0.01f, fx.SearchTimeMultiplier);
	}

	/// <summary>Injury penalties, vitals activity and the pain screen effect.</summary>
	private void UpdateHealthLinks()
	{
		var h = Health;
		ApplySkillEffects();
		h.Sprinting = IsSprinting;
		h.HeavyLoad = Tier >= WeightTier.Heavy;
		_skills.Update((float)GetPhysicsProcessDeltaTime(), IsSprinting && !h.IsDead, new Vector2(Velocity.X, Velocity.Z).Length(), TotalCarriedKg);
		_penalties = h.Effects.ComputePenalties(h.Body);
		_pain.SetStrength(h.IsDead ? 0f : _penalties.PainStrength);
	}

	/// <summary>Dead: no input, the body settles low and the weapon is dropped from use.</summary>
	private void StepDeath(float delta)
	{
		IsSprinting = false;
		if (Weapon is { } w)
		{
			w.CanFire = false;
			w.TriggerHeld = false;
		}
		var v = Velocity;
		v.X = Mathf.MoveToward(v.X, 0f, 12f * delta);
		v.Z = Mathf.MoveToward(v.Z, 0f, 12f * delta);
		if (!IsOnFloor())
			v.Y -= _gravity * delta;
		Velocity = v;
		MoveAndSlide();
		_eyeY = Mathf.MoveToward(_eyeY, 0.25f, _cfg.EyeHeightSpeed * delta);
		UpdateRig(delta);
	}

	/// <summary>
	/// F: search the container in front of you (moving away closes its window) or pick up a
	/// dropped item directly.
	/// </summary>
	private void UpdateInteract()
	{
		var from = _camera.GlobalPosition;
		var forward = -_camera.GlobalBasis.Z;
		LootContainer? container = null;
		DroppedItem? dropped = null;
		LockedDoor? door = null;
		var bestDist = float.MaxValue;

		bool Candidate(Vector3 point, out float dist)
		{
			var to = point - from;
			dist = to.Length();
			return dist <= InteractReach && dist > 0.01f && forward.Dot(to / dist) >= 0.5f && dist < bestDist;
		}

		foreach (var node in GetTree().GetNodesInGroup("loot_containers"))
		{
			if (node is LootContainer c && Candidate(c.GlobalPosition + Vector3.Up * 0.4f, out var d))
			{
				container = c;
				dropped = null;
				bestDist = d;
			}
		}
		foreach (var node in GetTree().GetNodesInGroup("dropped_items"))
		{
			if (node is DroppedItem di && Candidate(di.GlobalPosition, out var d))
			{
				dropped = di;
				container = null;
				door = null;
				bestDist = d;
			}
		}
		foreach (var node in GetTree().GetNodesInGroup("locked_doors"))
		{
			if (node is LockedDoor ld && !ld.IsOpen && Candidate(ld.GlobalPosition, out var d))
			{
				door = ld;
				container = null;
				dropped = null;
				bestDist = d;
			}
		}

		if (_screen.Container is { } open)
		{
			if ((open.GlobalPosition - GlobalPosition).Length() > InteractReach + 1.2f)
				_screen.CloseContainer();
			_prompt.Visible = false;
			return;
		}
		_prompt.Visible = (container is not null || dropped is not null || door is not null) && !_screen.IsOpen && !Health.IsDead;
		if (!_prompt.Visible)
			return;
		_prompt.Text = door is not null
			? GameText.Get("lock.prompt", door.Title)
			: dropped is not null
				? GameText.Get("msg.pickup.prompt", dropped.Title)
				: GameText.Get("msg.search.prompt", container!.Title);
		if (!Input.IsActionJustPressed("interact") || Input.MouseMode != Input.MouseModeEnum.Captured)
			return;
		if (door is not null)
			door.TryOpen(this);
		else if (dropped is not null)
			PickUp(dropped);
		else
		{
			if (_searchTracked.Add(container!))
				container!.Model.ItemRevealed += _ => _skills.ItemRevealed();
			_screen.OpenWithContainer(container!);
		}
	}

	/// <summary>Dev helper: picks up the nearest dropped item regardless of aim.</summary>
	public bool DebugPickUpNearest()
	{
		DroppedItem? best = null;
		var bestDist = float.MaxValue;
		foreach (var node in GetTree().GetNodesInGroup("dropped_items"))
		{
			if (node is DroppedItem d && d.GlobalPosition.DistanceTo(GlobalPosition) < bestDist)
			{
				best = d;
				bestDist = d.GlobalPosition.DistanceTo(GlobalPosition);
			}
		}
		if (best is null)
			return false;
		PickUp(best);
		return true;
	}

	/// <summary>Takes a dropped item into pockets, rig or backpack; equips it if only a slot is free.</summary>
	private void PickUp(DroppedItem dropped)
	{
		var item = dropped.Item;
		var inv = Inventory.Inventory;
		var taken = inv.TryAutoStore(item);
		if (!taken)
		{
			var action = InventoryActions.Quick(inv, item, null);
			if (action is { Kind: QuickActionKind.Equip, Slot: { } slot } && inv.InSlot(slot) is null)
				taken = inv.TryEquip(item, slot);
		}
		if (!taken)
		{
			WeaponMessage?.Invoke(GameText.Get("msg.pickup.no_room"));
			return;
		}
		dropped.QueueFree();
		WeaponMessage?.Invoke(GameText.Get("msg.pickup.done", GameText.ItemName(item.Spec.Id)));
		Inventory.NotifyChanged();
	}

	private const float InteractReach = 2.4f;

	/// <summary>Shows a short line to the player, like the pickup and magazine messages.</summary>
	public void NotifyMessage(string text) => WeaponMessage?.Invoke(text);

	/// <summary>Melee on G: windup, then a short ray from the camera hits one hitbox.</summary>
	private void UpdateMelee(float delta)
	{
		var captured = Input.MouseMode == Input.MouseModeEnum.Captured;
		if (captured && !_mantling && !_transition.BlocksWeaponActions && Input.IsActionJustPressed("melee") && _melee.TryStart(Arms.IsExhausted))
		{
			Weapon?.Controller.Interrupt();
			Arms.Spend(_melee.Spec.StaminaCost);
		}
		if (_melee.Update(delta))
			MeleeStrike();
	}

	private void MeleeStrike()
	{
		var spec = _melee.Spec;
		var from = _camera.GlobalPosition;
		var to = from - _camera.GlobalBasis.Z * spec.Range;
		var q = PhysicsRayQueryParameters3D.Create(from, to, CollisionMask, new Array<Rid> { GetRid() });
		q.CollideWithAreas = true;
		MeleeDebug = "swing: " + DescribeNearestHitbox();
		var space = GetWorld3D().DirectSpaceState;
		var hit = space.IntersectRay(q);
		var area = hit.Count > 0 ? hit["collider"].AsGodotObject() as HitboxArea : null;
		// The centre ray is thin and fails from inside a target, so sweep spheres along the swing.
		if (area is null)
			area = FindMeleeTarget(space, from, -_camera.GlobalBasis.Z, spec.Range);
		if (area?.Health is { } target)
		{
			var wasAlive = !target.IsDead;
			var result = spec.IgnoresArmor
				? target.ReceiveUnarmoredDamage(area.Part, spec.Damage)
				: target.ReceiveDamage(area.Part, spec.Damage, spec.PenetrationTier);
			var key = !result.ArmorHit ? "msg.melee.hit" : result.Penetrated ? "msg.melee.hit_armor" : "msg.melee.hit_stopped";
			WeaponMessage?.Invoke(GameText.Get(key, area.Part, result.Damage.ToString("0.#")));
			MeleeHit?.Invoke(result);
			if (wasAlive && target.IsDead)
				MeleeKilled?.Invoke(target);
			return;
		}
		WeaponMessage?.Invoke(GameText.Get("msg.melee.miss"));
	}

	/// <summary>
	/// Melee sweep: overlaps small spheres along the swing, starting at the camera so it still
	/// works when standing inside the target, and picks the hitbox nearest the aim line.
	/// </summary>
	private HitboxArea? FindMeleeTarget(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 dir, float range)
	{
		var radius = _cfg.MeleeAssistRadius;
		var sphere = new SphereShape3D { Radius = radius };
		var samples = Mathf.Max(2, Mathf.CeilToInt(range / radius));
		HitboxArea? best = null;
		var bestScore = float.MaxValue;
		for (var i = 0; i <= samples; i++)
		{
			var d = range * i / samples;
			var q = new PhysicsShapeQueryParameters3D
			{
				Shape = sphere,
				Transform = new Transform3D(Basis.Identity, from + dir * d),
				CollisionMask = CollisionMask,
				CollideWithAreas = true,
				CollideWithBodies = false,
				Exclude = new Array<Rid> { GetRid() },
			};
			foreach (var r in space.IntersectShape(q, 16))
			{
				if (r["collider"].AsGodotObject() is not HitboxArea candidate)
					continue;
				var toBox = candidate.GlobalPosition - from;
				var along = toBox.Dot(dir);
				var offAxis = (toBox - dir * along).Length();
				// Prefer what is closest to the aim line, then what is nearer.
				var score = offAxis + Mathf.Abs(along) * 0.1f;
				if (score < bestScore)
				{
					bestScore = score;
					best = candidate;
				}
			}
		}
		return best;
	}

	/// <summary>Draws the weapon from a slot (1, 2, 3 keys).</summary>
	public void SelectSlot(EquipSlot slot)
	{
		if (Inventory.Inventory.InSlot(slot) is null || _transition.BlocksWeaponActions)
			return;
		ActiveSlot = slot;
		RefreshHeldWeapon();
	}

	/// <summary>Makes the hands match the weapon item in the active slot.</summary>
	private void RefreshHeldWeapon()
	{
		var item = Inventory.Inventory.InSlot(ActiveSlot);
		if (item is null)
		{
			if (_heldWeaponUid is not null)
			{
				_support.FoldBipod();
				_body.ClearHeldItem();
				_heldWeaponUid = null;
			}
			return;
		}
		if (_heldWeaponUid == item.Uid)
		{
			Weapon?.RefreshBuild(); // parts may have been swapped
			return;
		}
		if (ItemDb.Data(item.Spec.Id)?.Payload is WeaponData data)
		{
			Weapon?.Controller.Interrupt(); // a magazine in the hand goes back before the weapon is put away
			EquipWeapon(data, item);
			_heldWeaponUid = item.Uid;
		}
	}

	/// <summary>Keys 4-7: use the item bound to that slot, if one is carried.</summary>
	private void UpdateQuickUse()
	{
		if (_screen.IsOpen || Input.MouseMode != Input.MouseModeEnum.Captured || _mantling)
			return;
		for (var i = 0; i < QuickUseBar.SlotCount; i++)
		{
			if (!Input.IsActionJustPressed($"quick_use_{i + 1}"))
				continue;
			QuickUsePressed?.Invoke(i);
			if (QuickUse.IdAt(i) is null)
				WeaponMessage?.Invoke(GameText.Get("msg.quickuse.unbound"));
			else if (QuickUse.Resolve(i, Inventory.Inventory) is not { } item)
				WeaponMessage?.Invoke(GameText.Get("msg.quickuse.none", GameText.ItemName(QuickUse.IdAt(i)!)));
			else
				UseItem(item);
		}
	}

	/// <summary>Uses a medical, food or drink item from the inventory; consumed when finished.</summary>
	public bool UseItem(ItemInstance item)
	{
		if (_pendingUse is not null || Health.IsDead || _transition.BlocksWeaponActions)
			return false;
		Weapon?.Controller.Interrupt();
		var started = ItemDb.Data(item.Spec.Id)?.Payload switch
		{
			MedicalData m => Health.UseMedical(m),
			ConsumableData c => Health.UseConsumable(c),
			_ => false,
		};
		if (started)
		{
			_pendingUse = item;
			if (ItemDb.Data(item.Spec.Id)?.Payload is MedicalData med)
			{
				var cue = med.Kind == MedicalKind.SurgeryKit ? "surgery_start" : "heal";
				NoiseBus.Emit(GlobalPosition, PerceptionEnvironment.CueRadius(cue), cue, this);
			}
		}
		else
			WeaponMessage?.Invoke(GameText.Get("msg.use.cannot"));
		return started;
	}

	private void FinishPendingUse(bool applied)
	{
		var item = _pendingUse;
		_pendingUse = null;
		if (item is null || !applied)
			return;
		item.Count--;
		if (item.Count <= 0)
			Inventory.Inventory.Remove(item);
		Inventory.NotifyChanged();
	}

	/// <summary>Replaces the held weapon (full magazine, chambered).</summary>
	public void EquipWeapon(WeaponData data, ItemInstance? item = null)
	{
		_support.FoldBipod();
		var weapon = WeaponNode.Create(data, item);
		if (item is not null)
			weapon.Controller.Store = new InventoryMagazineStore(Inventory.Inventory, data.Ammo?.Caliber,
				Inventory.NotifyChanged, DropItemToWorld);
		weapon.ShotFired += OnShotFired;
		weapon.ShotFired += (_, _, _) => _skills.Shot();
		weapon.DryFired += () =>
		{
			WeaponMessage?.Invoke(GameText.Get("msg.weapon.click"));
			NoiseBus.Emit(weapon.GlobalPosition, PerceptionEnvironment.CueRadius("dry_click"), "dry_click", this);
		};
		weapon.Controller.MagChecked += r =>
		{
			_skills.MagChecked();
			WeaponMessage?.Invoke(GameText.ForMagCheck(r));
		};
		weapon.Controller.ReloadFinished += _skills.Reloaded;
		weapon.Controller.ChamberChecked += loaded =>
		{
			_skills.ChamberChecked();
			WeaponMessage?.Invoke(GameText.Get(loaded ? "msg.chamber.loaded" : "msg.chamber.empty"));
		};
		weapon.Controller.MagazineDropped += m => OnMagazineDropped(weapon, m);
		_body.AttachHeldItem(weapon);
	}

	private void OnShotFired(AmmoType ammo, Vector3 position, Vector3 direction)
	{
		_projectiles ??= GetTree().GetFirstNodeInGroup("projectiles") as ProjectileManager;
		// Barrel and muzzle parts change muzzle velocity.
		var fired = Weapon is { } held && held.Stats.VelocityMult != 1f
			? ammo with { MuzzleVelocity = ammo.MuzzleVelocity * held.Stats.VelocityMult }
			: ammo;
		var cone = HandlingModel.HipSpreadDeg(_cfg.HipSpreadDeg, Weapon?.Stats.DispersionMult ?? 1f, _aim.Amount) * _crawl.HipSpreadMult;
		direction = AiAim.Jitter(direction, cone, GD.Randf);
		// the gun itself: bullet dispersion in MOA, tightened or widened by the barrel parts and the ammo
		direction = Dispersion.Apply(direction, Dispersion.FinalMoa(Weapon?.Stats.Moa ?? 4f, 0f, ammo.Dispersion), GD.Randf);
		_projectiles?.Spawn(fired, position, direction, GetRid(), _ownRids);
		NoiseBus.Emit(position, Weapon?.NoiseRadius ?? 150f, "gunshot", this);
		if (Weapon is { } w)
		{
			var handling = HandlingModel.RecoilMultiplier(_handling, Stance, _aim.Amount, _support.RestAmount,
				_support.BipodDeployed);
			handling *= _crawl.RecoilMult; // one hand on the gun while crawling
			var tuning = w.RecoilTuning;
			_recoil.Kick(tuning, handling, GD.Randf() * 2f - 1f);
			KickSprings(tuning, handling);
		}
	}

	/// <summary>Fast gun and camera kick on top of the aim climb: springs that settle between shots.</summary>
	private void KickSprings(RecoilTuning t, float handling)
	{
		// impulse velocity that produces the wanted peak displacement for a (nearly) critically damped spring
		var toVelocity = Mathf.Sqrt(t.SpringStiffness) * 2.718f * handling;
		var yaw = (GD.Randf() < 0.5f ? -1f : 1f) * (0.5f + GD.Randf() * 0.5f);
		_gunRot.Impulse(new Vector3(t.KickPitchDeg, t.KickYawDeg * yaw, t.KickRollDeg * (GD.Randf() * 2f - 1f)) * toVelocity);
		_gunBack.Impulse(t.KickBackM * toVelocity);
		_camKick.Impulse(new Vector3(t.CameraKickDeg, t.CameraKickDeg * 0.5f * yaw, t.CameraKickDeg * 0.5f * (GD.Randf() * 2f - 1f)) * toVelocity);
	}

	private bool _muzzleSurface;
	private bool _wasRested;
	private float _restSnap;
	private bool _restPitchOk = true;

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
		// Probe the weapon where it would be with no retraction or snap, so the rest decision cannot
		// depend on the very motion it causes (that loop flickered rest and the spring arm).
		var a = (Unretracted(weapon.GripRight) + Unretracted(weapon.GripLeft)) * 0.5f;
		var b = Unretracted(weapon.Muzzle);
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

	private Transform3D _baseSwayLocal = Transform3D.Identity;

	/// <summary>World position of a weapon part as if retraction, tilt and rest snap were zero.</summary>
	private Vector3 Unretracted(Node3D part)
		=> _swayNode.GetParent<Node3D>().GlobalTransform * _baseSwayLocal * _swayNode.ToLocal(part.GlobalPosition);

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
		if (!weapon.HasBipod)
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
	private void UpdateClearance(float delta, WeaponNode weapon, Vector3 basePosition)
	{
		var origin = _camera.GlobalPosition;
		// Cast toward where the muzzle would be with NO retraction, snap or tilt. Using the live muzzle
		// closes a feedback loop (retract tilts the muzzle up, the cast leaves the wall, retract drops,
		// the muzzle tips back down...) that shows up as arm twitching when looking down at a wall.
		_baseSwayLocal = new Transform3D(Basis.FromEuler(_swayNode.Rotation), basePosition);
		var toMuzzle = Unretracted(weapon.Muzzle) - origin;
		var fullLength = toMuzzle.Length();
		var travel = fullLength + _cfg.ClearanceMargin;
		var hitDistance = travel;
		if (toMuzzle.LengthSquared() > 0.0001f)
		{
			var exclude = new Array<Rid> { GetRid() };
			var q = new PhysicsShapeQueryParameters3D
			{
				Shape = new SphereShape3D { Radius = _cfg.ClearanceRadius },
				Transform = new Transform3D(Basis.Identity, origin),
				Motion = toMuzzle.Normalized() * travel,
				CollisionMask = CollisionMask,
				Exclude = exclude,
			};
			var result = GetWorld3D().DirectSpaceState.CastMotion(q);
			if (result.Length > 0)
				hitDistance = result[0] * travel;
			ClearanceDebug = $"excluded={exclude.Count - 1} cast={(result.Length > 0 ? result[0] : 1f):0.00} restSurf={RestSurfaceHeight:0.00}";
		}
		// While resting, the surface under the weapon is where it is meant to be; the rest snap handles
		// it. Fade the retraction out by RestAmount (no on/off threshold) so entering and leaving rest
		// blends smoothly instead of the two systems switching and fighting.
		_clearance.Update(delta, _clearance.TargetRetract(fullLength, hitDistance) * (1f - _support.RestAmount));
		UpdateReadyPose(delta, weapon, origin, toMuzzle, hitDistance);
	}

	/// <summary>Wall tuck stage 2: past the start fraction the arms lift the muzzle (high ready) or drop it (low ready).</summary>
	private void UpdateReadyPose(float delta, WeaponNode weapon, Vector3 origin, Vector3 toMuzzle, float hitDistance)
	{
		var fraction = _clearance.Fraction;
		if (fraction > _readyTuning.StartFraction && _readyAmount < 0.01f && toMuzzle.LengthSquared() > 0.0001f)
		{
			var muzzle = Unretracted(weapon.Muzzle);
			var hitPoint = origin + toMuzzle.Normalized() * hitDistance;
			var overhead = Ray(origin, muzzle + Vector3.Up * 0.25f).Count != 0;
			_readyPose = ReadyPoseModel.Choose(hitPoint.Y - muzzle.Y, overhead, _readyTuning);
		}
		var target = _readyPose == ReadyPose.None ? 0f : ReadyPoseModel.Amount(fraction, _readyTuning);
		_readyAmount = Mathf.MoveToward(_readyAmount, target, _readyTuning.BlendSpeed * delta);
		if (_readyAmount < 0.001f && target <= 0f)
			_readyPose = ReadyPose.None;
	}

	private void OnMagazineDropped(WeaponNode weapon, Magazine magazine)
	{
		SpawnDroppedMagazine(weapon);
		if (InventoryMagazineStore.FromMagazine(magazine, weapon.Data.Ammo?.Caliber) is { } item)
			DropItemToWorld(item);
	}

	/// <summary>Drops an item from the inventory screen onto the ground beside the player.</summary>
	public void DropItem(ItemInstance item) => DropItemToWorld(item);

	/// <summary>Throws an item onto the ground in front of the player as a physical object.</summary>
	private void DropItemToWorld(ItemInstance item)
	{
		var forward = -_camera.GlobalBasis.Z;
		var spot = _camera.GlobalPosition + forward * 0.6f;
		var toss = forward * 1.5f + Vector3.Up * 0.8f + Velocity;
		var dropped = DroppedItem.Create(item, spot, toss);
		dropped.AddCollisionExceptionWith(this);
		GetTree().CurrentScene.AddChild(dropped);
	}

	// --- loading rounds from the inventory (GDD s7) ---------------------------------------

	/// <summary>Short status text for the inventory header while loading or unloading.</summary>
	public string LoaderStatus => _loader.Mode switch
	{
		LoaderMode.Loading => $"Loading magazine {_loader.Magazine?.Rounds.Count}/{_loader.Magazine?.Spec.MagazineCapacity}...",
		LoaderMode.Unloading => $"Unloading magazine ({_loader.Magazine?.Rounds.Count} left)...",
		_ => "",
	};

	public bool TryLoadMagazine(ItemInstance magazine, ItemInstance ammo)
	{
		if (!_loader.StartLoad(magazine, ammo))
			return false;
		_loadAmmo = ammo;
		return true;
	}

	public bool TryUnloadMagazine(ItemInstance magazine) => _loader.StartUnload(magazine);

	private void UpdateLoader(float delta)
	{
		if (!_loader.IsBusy)
			return;
		// Cancel if the magazine left the inventory (moved to the ground or into a weapon).
		if (_loader.Magazine is { } m && Inventory.Inventory.Locate(m) is null)
		{
			_loader.Cancel();
			return;
		}
		_loader.Update(delta);
	}

	private void OnLoaderFinished()
	{
		if (_loadAmmo is { Count: <= 0 } spent)
			Inventory.Inventory.Remove(spent);
		_loadAmmo = null;
		Inventory.NotifyChanged();
	}

	private void OnRoundUnloaded(string ammoId)
	{
		var itemId = ItemDb.AmmoItemId(ammoId);
		var stack = itemId is null ? null : InventoryComponent.Make(itemId);
		if (stack is null)
			return;
		if (!Inventory.Inventory.TryAutoStore(stack))
			DropItemToWorld(stack);
		Inventory.NotifyChanged();
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
		=> Weapon is not null && !_mantling && !Weapon.Controller.IsBusy && !_melee.IsBusy && !_swap.Active && !WeaponRedFlagged
			&& !_transition.BlocksAds && !_crawl.BlocksAds && !(Stance == Stance.Prone && ProneLean.BlocksAds(_proneLean, _leanRatio))
			&& (DebugForceAds || (Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed("aim")));

	/// <summary>ADS blend, breath hold, sway, recoil recovery, weapon position and FOV.</summary>
	private void UpdateHandling(float delta)
	{
		_swap.Update(delta, IsSprinting);
		_swapBar.Visible = _swapLabel.Visible = _swap.Active;
		if (_swap.Active)
		{
			_swapBar.Value = _swap.Progress;
			_swapLabel.Text = _swap.Label;
		}
		var weapon = Weapon;
		var springs = weapon?.RecoilTuning;
		_gunRot.Update(delta, springs?.SpringStiffness ?? 180f, springs?.SpringDamping ?? 0.9f);
		_gunBack.Update(delta, springs?.SpringStiffness ?? 180f, springs?.SpringDamping ?? 0.9f);
		_camKick.Update(delta, springs?.SpringStiffness ?? 180f, springs?.SpringDamping ?? 0.9f);
		_camera.Rotation = new Vector3(Mathf.DegToRad(_camKick.X.Value), Mathf.DegToRad(_camKick.Y.Value), Mathf.DegToRad(_camKick.Z.Value));
		var aimHeld = AimHeld();
		var ergo = _cfg.ToErgonomicsTuning();
		var ergonomics = weapon?.Stats.Ergonomics ?? 50f;
		_aim.Update(aimHeld && !IsSprinting, delta, (weapon?.Data.AdsTimeS ?? 0.2f) * ergo.AdsTimeMult(ergonomics));
		IsHoldingBreath = BreathHold.Update(aimHeld && Input.IsActionPressed("sprint"), Arms,
			_cfg.BreathHoldDrainPerS * ergo.StaminaDrainMult(ergonomics), delta);

		if (weapon is null)
			return;
		var speedFraction = new Vector2(Velocity.X, Velocity.Z).Length() / Mathf.Max(0.1f, _cfg.SprintSpeed);
		var speed = new Vector2(Velocity.X, Velocity.Z).Length();
		var pitchDeg = Mathf.RadToDeg(_head.Rotation.X);
		var pitchLimit = -_cfg.RestMaxPitchDownDeg - (_restPitchOk ? _cfg.RestPitchHysteresisDeg : 0f);
		_restPitchOk = pitchDeg >= pitchLimit;
		var restSurface = FindRestSurface(weapon, out var surfaceY) && _restPitchOk;
		RestSurfaceHeight = restSurface ? surfaceY - GlobalPosition.Y : float.NaN;
		var muzzleSurface = SurfaceUnder(weapon.Muzzle.GlobalPosition);
		if (_support.Update(delta, restSurface, muzzleSurface, weapon.HasBipod, Stance, speed, IsSprinting))
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

		var ctx = new HandlingContext(Stance, weapon.Stats.WeightKg, Arms.Fraction, speedFraction,
			_aim.Amount, IsHoldingBreath, _support.RestAmount, _support.BipodDeployed);
		SwayAmplitudeDeg = HandlingModel.SwayAmplitude(_handling,
				weapon.Data.SwayBaseDeg * weapon.Stats.SwayMult * ergo.SwayMult(ergonomics), ctx)
			* _penalties.SwayMultiplier * Health.Vitals.SwayMultiplier * _pivotModel.SwayBoost;
		var sway = _swayModel.Update(delta, SwayAmplitudeDeg);
		var (posOffset, rotDeg) = UpdateActionPose(delta, weapon);
		_swayNode.Rotation = new Vector3(Mathf.DegToRad(sway.Y + rotDeg.X), Mathf.DegToRad(sway.X + rotDeg.Y),
			Mathf.DegToRad(rotDeg.Z));

		_recoil.Update(delta, weapon.RecoilTuning);
		_recoilNode.Rotation = new Vector3(Mathf.DegToRad(_recoil.Offset.Y), Mathf.DegToRad(_recoil.Offset.X), 0f);

		var t = Mathf.SmoothStep(0f, 1f, _aim.Amount);
		// socket-based ADS: put the active sight (optic SightEye, model SightEye or rear sight) on the view axis
		var (sightPos, eyeRelief, _) = weapon.ActiveSight(_cfg.AdsEyeRelief, _cfg.AdsEyeReliefOptic);
		var adsPos = AdsSolve.WeaponOffset(sightPos, eyeRelief);
		UpdateClearance(delta, weapon, _cfg.HeldItemOffset.Lerp(adsPos, t) + posOffset);

		// Weapon visibly settles onto the surface it rests on; ADS keeps the sight line instead.
		var snapTarget = 0f;
		if (restSurface)
		{
			var weaponY = (weapon.GripRight.GlobalPosition.Y + weapon.GripLeft.GlobalPosition.Y) * 0.5f - _restSnap;
			var wanted = surfaceY + weapon.Data.BodySize.Y * 0.5f + 0.01f - weaponY;
			snapTarget = Mathf.Clamp(wanted, -_cfg.RestSnapMax, _cfg.RestSnapMax);
		}
		// Ease toward the target: when rest is released the weapon must glide off the surface, not jump.
		_restSnap = Mathf.MoveToward(_restSnap, snapTarget * _support.RestAmount * (1f - t),
			_cfg.RestSnapSpeed * delta);
		_swayNode.Position = _cfg.HeldItemOffset.Lerp(adsPos, t) + posOffset
			+ new Vector3(0f, _restSnap, _clearance.Retract);
		_swayNode.Rotation += new Vector3(Mathf.DegToRad(_clearance.TiltDeg), 0f, 0f);
		ApplyPostPass(delta, weapon);
		if (_melee.IsBusy)
		{
			var strikeAt = _melee.Spec.WindupS / Mathf.Max(0.01f, _melee.Spec.WindupS + _melee.Spec.RecoverS);
			var (mp, mr) = WeaponPoseModel.MeleePose(_melee.Progress, strikeAt);
			_swayNode.Position += mp;
			_swayNode.Rotation += new Vector3(Mathf.DegToRad(mr.X), Mathf.DegToRad(mr.Y), Mathf.DegToRad(mr.Z));
		}
		// A magnifying optic narrows the field of view while aiming and darkens the screen edge.
		var zoom = Mathf.Max(1f, weapon.Stats.Zoom);
		_camera.Fov = Mathf.Lerp(_cfg.HipFovDeg, _cfg.AdsFovDeg / zoom, t);
		_scope.SetAmount(zoom > 1f ? t : 0f);
		weapon.SetAds(t);
	}

	/// <summary>
	/// The weapon action layer (animation spec s1.3): during a reload the support hand walks its anchors (magazine
	/// well, pouch, charging handle) and the weapon tilts; other actions keep the placeholder pose. Returns the weapon pivot offset.
	/// </summary>
	private (Vector3 Position, Vector3 RotationDeg) UpdateActionPose(float delta, WeaponNode weapon)
	{
		var c = weapon.Controller;
		var grip = weapon.GripLeft.Position;
		if (!ReferenceEquals(_actionWeapon, weapon))
		{
			_actionWeapon = weapon;
			_handPos = grip;
			_rightPos = weapon.GripRight.Position;
			_reloadTilt = 0f;
			_clipReloadId = -1;
		}
		var phase = c.Phase;
		var reloading = c.Action is WeaponActionKind.TacticalReload or WeaponActionKind.FastReload;
		var pose = phase == ReloadPhase.None ? default : ReloadPose.Evaluate(phase, c.PhaseProgress, c.ReloadIsFast, c.ReloadHasRack);

		// an authored clip for this weapon, stretched onto the logic events, overrides whatever tracks it animates
		ClipSample? clip = null;
		if (reloading && phase != ReloadPhase.None && weapon.ReloadClip(c.ReloadIsFast) is { } sampler)
		{
			if (c.ReloadId != _clipReloadId)
			{
				_clipReloadId = c.ReloadId;
				var events = weapon.ReloadClipEvents(weapon.Data.ToActionTuning(), sampler, c.ReloadIsFast, c.ReloadHasRack);
				_clipWarp = ReloadClipWarp.Points(c.ReloadTimeline, c.ReloadTotal, c.ReloadEntryKind, c.ReloadIsFast, events);
			}
			clip = sampler.Sample(ReloadClipWarp.ClipTime(_clipWarp!, c.ReloadElapsed));
		}

		_reloadTilt = Mathf.MoveToward(_reloadTilt, phase == ReloadPhase.None ? 0f : pose.Tilt, 6f * delta);
		var goal = clip?.LeftPos ?? (phase == ReloadPhase.None
			? grip
			: ReloadPose.HandPosition(pose, _reloadPose, grip, weapon.MagWellPoint, weapon.ChargingPoint));
		var k = 1f - Mathf.Exp(-delta / 0.04f);
		_handPos = _handPos.Lerp(goal, k);
		if (phase == ReloadPhase.None && _handPos.DistanceTo(grip) < 0.002f)
			_handPos = grip;
		weapon.LeftHandAction.Position = _handPos;
		weapon.LeftHandAction.Quaternion = clip?.LeftRot ?? weapon.GripLeft.Quaternion;

		_rightPos = _rightPos.Lerp(clip?.RightPos ?? weapon.GripRight.Position, k);
		weapon.RightHandAction.Position = _rightPos;
		weapon.RightHandAction.Quaternion = clip?.RightRot ?? weapon.GripRight.Quaternion;
		weapon.SetReloadVisual(phase, c.PhaseProgress);

		if (clip is { PivotPos: not null } or { PivotRot: not null })
		{
			var rot = clip.Value.PivotRot is { } q ? q.GetEuler() * (180f / Mathf.Pi) : Vector3.Zero;
			return (clip.Value.PivotPos ?? Vector3.Zero, rot);
		}
		if (reloading || _reloadTilt > 0.001f)
			return (_reloadPose.PivotPos * _reloadTilt, _reloadPose.PivotRotDeg * _reloadTilt);
		return WeaponPoseModel.Pose(c.Action, c.Progress);
	}

	/// <summary>Dev: plays a test clip for every reload (hand circles and a weapon roll) to check the authored-clip pipeline.</summary>
	public void DebugClipDemo()
	{
		var clip = new Animation { Length = 2f };
		var left = clip.AddTrack(Animation.TrackType.Position3D);
		clip.TrackSetPath(left, new NodePath("LeftHandAction"));
		var pivot = clip.AddTrack(Animation.TrackType.Rotation3D);
		clip.TrackSetPath(pivot, new NodePath("WeaponPivot"));
		for (var i = 0; i <= 8; i++)
		{
			var t = i / 8f * 2f;
			var a = i / 8f * Mathf.Tau;
			clip.PositionTrackInsertKey(left, t, new Vector3(-0.15f + 0.1f * Mathf.Cos(a), -0.15f + 0.1f * Mathf.Sin(a), -0.3f));
			clip.RotationTrackInsertKey(pivot, t, new Quaternion(Vector3.Forward, Mathf.DegToRad(30f * Mathf.Sin(a))));
		}
		Weapon?.DebugSetReloadClip(clip);
	}

	/// <summary>Dev: starts a reload (fast or tactical).</summary>
	public bool DebugReload(bool fast) => Weapon?.Controller.StartReload(fast) ?? false;

	/// <summary>Springs, inertia and the ready pose on top of the aim pose (animation spec s4).</summary>
	private void ApplyPostPass(float delta, WeaponNode weapon)
	{
		var ergonomics = weapon.Stats.Ergonomics;
		_inertia.BaseLag = _cfg.InertiaLag;
		_inertia.Update(delta, _lookDeltaDeg, weapon.Stats.WeightKg, ergonomics, Arms.Fraction);
		_lookDeltaDeg = Vector2.Zero;
		var gun = _gunRot.Value;
		var cap = Mathf.Max(2f, weapon.RecoilTuning.KickPitchDeg * 2.5f);
		var pitch = Mathf.Clamp(gun.X, -cap, cap) + _inertia.Offset.Y;
		var yaw = Mathf.Clamp(gun.Y, -cap, cap) + _inertia.Offset.X;
		var readyPitch = ReadyPoseModel.PitchDeg(_readyPose, _readyAmount, _readyTuning);
		_swayNode.Rotation += new Vector3(Mathf.DegToRad(pitch + readyPitch), Mathf.DegToRad(yaw), Mathf.DegToRad(Mathf.Clamp(gun.Z, -cap, cap)));
		_swayNode.Position += new Vector3(0f, 0f, _gunBack.Value);
		// crawling: the gun in one hand, low and off the screen centre; prone lean: the rifle rolled nearly horizontal
		if (_crawl.OneHanded > 0f)
		{
			_swayNode.Position += new Vector3(0.12f, -0.14f, 0.05f) * _crawl.OneHanded;
			_swayNode.Rotation += new Vector3(0f, 0f, Mathf.DegToRad(8f) * _crawl.OneHanded);
		}
		if (Stance == Stance.Prone && Mathf.Abs(_leanRatio) > 0.001f)
			_swayNode.Rotation += new Vector3(0f, 0f, -Mathf.DegToRad(ProneLean.ExtraCantDeg(_proneLean, _leanRatio)));
		var carry = 1f - _transition.WeaponFollowsView;
		if (carry > 0f)
		{
			// hands on the ground: the weapon hangs low and tilted down instead of following the view
			_swayNode.Position += new Vector3(0.05f, -0.22f, 0.08f) * carry;
			_swayNode.Rotation += new Vector3(Mathf.DegToRad(-30f) * carry, 0f, 0f);
		}
	}

	private void UpdateWeaponInput()
	{
		var weapon = Weapon;
		if (weapon is null)
			return;
		var captured = Input.MouseMode == Input.MouseModeEnum.Captured;
		weapon.Controller.ReloadTimeMultiplier = _penalties.ReloadTimeMultiplier;
		weapon.CanFire = (captured || DebugForceTrigger) && !IsSprinting && !_mantling && !_swap.Active && !WeaponRedFlagged
			&& !_clearance.BlocksFire && !_melee.IsBusy && !_transition.BlocksFire;
		weapon.TriggerHeld = DebugForceTrigger || Input.IsActionPressed("fire");
		if (Input.IsActionJustPressed("fire") || DebugForceTrigger)
			weapon.TriggerPressed = true;
		if (Input.IsActionJustPressed("fire_mode"))
		{
			weapon.State.Fire.CycleMode();
			WeaponMessage?.Invoke(GameText.Get("msg.firemode", weapon.State.Fire.Mode));
		}
		if (!captured)
			return;
		if (Input.IsActionJustPressed("rest_bipod"))
			ToggleBipod(weapon);
		var c = weapon.Controller;
		if (_melee.IsBusy || _transition.BlocksWeaponActions)
			return;
		if (Input.IsActionJustPressed("reload") && !IsSprinting)
		{
			// Second tap shortly after the first upgrades the tactical reload to a fast one.
			if (c.IsBusy)
				c.UpgradeToFast();
			else
			{
				var wasEmpty = c.State.RoundsAvailable == 0;
				if (!c.StartReload(fast: false))
					WeaponMessage?.Invoke(GameText.Get("msg.reload.no_spare"));
				else if (wasEmpty)
					NoiseBus.Emit(GlobalPosition, PerceptionEnvironment.CueRadius("reload_empty"), "reload_empty", this);
			}
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
	/// <summary>Dev: a stance change through the real path (transition, locks), as a key press would make.</summary>
	public void DebugGoStance(Stance stance) => SetStance(stance);

	/// <summary>Dev: pelvis yaw, torso twist and the pivot state.</summary>
	public string DebugPivotLine()
		=> $"aim={Mathf.RadToDeg(Rotation.Y):0} pelvis={(_proneTurn.Dragging || _bodyTwistDeg != _pivotModel.TwistDeg ? _proneTurn.PelvisYawDeg : _pivotModel.PelvisYawDeg):0} twist={_bodyTwistDeg:0} pivoting={_pivotModel.Pivoting} dragging={_proneTurn.Dragging} blocked={_proneTurn.Blocked} bank={_proneTurn.BankDeg:0.0}";

	/// <summary>Dev: turns the view by this much, as a mouse movement would.</summary>
	public void DebugTurnBy(float deg) => RotateY(Mathf.DegToRad(deg));

	/// <summary>Dev: yaws the view as the mouse would.</summary>
	public void DebugTurnTo(float yawDeg) => Rotation = new Vector3(0f, Mathf.DegToRad(yawDeg), 0f);

	/// <summary>Dev: stance, eye height and the transition state.</summary>
	public string DebugStanceLine()
		=> $"stance={Stance} oneHanded={_crawl.OneHanded:0.00} adsBlocked={_crawl.BlocksAds} leanRatio={_leanRatio:0.00} camRollZ={Mathf.RadToDeg(_pivot.Rotation.Z):0.0} eye={_eyeY:0.00} kind={_transition.Kind} progress={_transition.Progress:0.00} blockFire={_transition.BlocksFire} blockMove={_transition.BlocksMovement} grip={_transition.GripWeight:0.00}";

	public void DebugSetStance(Stance stance)
	{
		Stance = stance;
		ApplyStanceShape();
	}

	/// <summary>Dev helper: same as scrolling the wheel while holding C (positive = up).</summary>
	public void DebugAdjustHeight(float notches) => AdjustHeight(notches);

	/// <summary>Dev: hand positions in camera space, so body movement does not count as jitter.</summary>
	public (Vector3 Left, Vector3 Right) DebugElbows()
	{
		var inv = _camera.GlobalTransform.AffineInverse();
		var (l, r) = _body.DebugElbowPositions();
		return (inv * l, inv * r);
	}

	public (Vector3 Left, Vector3 Right) DebugHands()
	{
		var inv = _camera.GlobalTransform.AffineInverse();
		var (l, r) = _body.DebugHandPositions();
		return (inv * l, inv * r);
	}

	/// <summary>Dev helper: same as pressing the melee key.</summary>
	public bool DebugMelee()
	{
		if (!_melee.TryStart(Arms.IsExhausted))
			return false;
		Arms.Spend(_melee.Spec.StaminaCost);
		return true;
	}

	public bool MeleeBusy => _melee.IsBusy;

	/// <summary>Debug text: nearest hitbox to the camera and what the last swing found.</summary>
	public string MeleeDebug { get; private set; } = "-";

	private string DescribeNearestHitbox()
	{
		var from = _camera.GlobalPosition;
		HitboxArea? best = null;
		var bestDist = float.MaxValue;
		var count = 0;
		foreach (var node in GetTree().GetNodesInGroup("hitboxes"))
		{
			if (node is not HitboxArea a)
				continue;
			count++;
			var d = a.GlobalPosition.DistanceTo(from);
			if (d < bestDist)
			{
				bestDist = d;
				best = a;
			}
		}
		return best is null ? "no hitboxes in scene"
			: $"{count} hitboxes, nearest {best.Part} {bestDist:0.0} m (reach {_melee.Spec.Range:0.0}+{_cfg.MeleeAssistRadius:0.0})";
	}


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
		// Pulling against the accumulated recoil pays it down first; only the rest moves the view.
		var rest = _recoil.Pull(new Vector2(-rel.X * Mathf.RadToDeg(turn), -rel.Y * Mathf.RadToDeg(sens)));
		_lookDeltaDeg += rest;
		rel = new Vector2(-rest.X / Mathf.Max(Mathf.RadToDeg(turn), 1e-6f), -rest.Y / Mathf.Max(Mathf.RadToDeg(sens), 1e-6f));
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
		Tier = _weights.TierFor(TotalCarriedKg, StrengthBonusKg + (GameState.Instance?.Profile.SkillEffects.WeightBonusKg ?? 0f));
		UpdateHealthLinks();
		if (Health.IsDead)
		{
			StepDeath(delta);
			return;
		}
		if (_noclip)
		{
			NoclipStep(delta);
			return;
		}
		UpdateLoader(delta);
		UpdateInteract();
		UpdateQuickUse();
		UpdateWeaponInput();
		_transition.Update(delta);
		UpdateMelee(delta);
		if (_mantling)
		{
			StepMantle(delta);
			TickStamina(delta);
			UpdateHandling(delta);
			UpdateRig(delta);
			return;
		}
		HandleStanceInput();

		var input = DebugMove != Vector2.Zero ? DebugMove : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		if (_transition.BlocksMovement)
			input = Vector2.Zero; // pushing off the floor with both arms
		if (Stance == Stance.Prone)
			input.X = 0f; // no lateral crawl: sideways play prone is the lean and the turn shimmy
		if (Input.IsActionJustPressed("jump") && IsOnFloor() && input.Y < -0.1f
			&& Stance == Stance.Stand && TryMantle())
			return;

		var wish = GlobalBasis * new Vector3(input.X, 0f, input.Y);
		// While aiming, Shift holds breath instead of sprinting (GDD s7).
		var wantsSprint = (Input.IsActionPressed("sprint") || DebugSprint) && input.Y < -0.1f && Stance != Stance.Prone
			&& !AimHeld();
		IsSprinting = wantsSprint && _weights.CanSprint(Tier) && !Legs.IsExhausted && !_penalties.SprintBlocked;
		if (IsSprinting)
			Weapon?.Controller.Interrupt();

		var speed = _move.TargetSpeed(Stance, IsSprinting, Tier, _heights.CrouchLevel)
			* _penalties.MoveSpeedMultiplier;
		if (Stance == Stance.Prone && input.Y > 0f)
			speed *= _crawl.Tuning.BackSpeedShare; // crawling backwards is slower
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
				Weapon?.Controller.Interrupt();
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
		var lifted = false;
		if (Stance != Stance.Prone)
			lifted = StepUp.Try(this, new Vector3(vel.X, 0f, vel.Z), delta, _cfg.StepHeight);
		MoveAndSlide();
		if (Stance != Stance.Prone && !_mantling && !lifted)
			StepUp.Down(this, _cfg.StepHeight, _prevOnFloor);
		// A step (or a snap down one) moves the body at once; leave the camera where it was and let it catch up.
		var bodyY = GlobalPosition.Y;
		var stepped = bodyY - _prevBodyY;
		if (_prevOnFloor && !_mantling && Velocity.Y <= 0.5f && Mathf.Abs(stepped) is > 0.08f and < 0.4f)
			_stepOffset -= stepped;
		_prevBodyY = bodyY;
		_prevOnFloor = IsOnFloor();
		UpdateFootsteps(delta);
		UpdateHitboxes();
		UpdateHandling(delta);
		UpdateRig(delta);
		var camY = _camera.GlobalPosition.Y;
		if (_prevCamY.HasValue)
			MaxCameraYStep = Mathf.Max(MaxCameraYStep, Mathf.Abs(camY - _prevCamY.Value));
		_prevCamY = camY;
	}

	private float _stepTimer;

	/// <summary>Walking makes noise AI can hear; sprinting is loud, crouching and crawling are quiet.</summary>
	private void UpdateFootsteps(float delta)
	{
		var speed = new Vector2(Velocity.X, Velocity.Z).Length();
		if (!IsOnFloor() || speed < 0.6f)
		{
			_stepTimer = 0f;
			return;
		}
		_stepTimer += delta;
		var interval = IsSprinting ? _cfg.StepIntervalSprintS : _cfg.StepIntervalWalkS;
		if (_stepTimer < interval)
			return;
		_stepTimer = 0f;
		var radius = Stance switch
		{
			Stance.Prone => _cfg.FootstepProneRadius,
			Stance.Crouch => _cfg.FootstepCrouchRadius,
			_ => IsSprinting ? _cfg.FootstepSprintRadius : _cfg.FootstepWalkRadius,
		};
		NoiseBus.Emit(GlobalPosition, radius, "footstep", this);
	}

	private bool _noclip;

	/// <summary>
	/// Dev ghost mode: fly through everything, invisible to AI and untouchable. Space up, C down,
	/// Shift fast.
	/// </summary>
	public bool DebugNoclip
	{
		get => _noclip;
		set
		{
			_noclip = value;
			_collision.Disabled = value;
			foreach (var child in _hitboxRoot.GetChildren())
			{
				if (child is HitboxArea area)
					area.CollisionLayer = value ? 0u : 1u << 4;
			}
			Velocity = Vector3.Zero;
		}
	}

	private void NoclipStep(float delta)
	{
		var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		var dir = _camera.GlobalBasis * new Vector3(input.X, 0f, input.Y);
		if (Input.IsActionPressed("jump"))
			dir += Vector3.Up;
		if (Input.IsKeyPressed(Key.C))
			dir += Vector3.Down;
		var speed = Input.IsActionPressed("sprint") ? 40f : 12f;
		GlobalPosition += dir.LimitLength(1f) * speed * delta;
		Velocity = Vector3.Zero;
		UpdateHandling(delta);
		UpdateRig(delta);
	}

	private ScopeOverlay _scope = null!;
	private Node3D _hitboxRoot = null!;
	private readonly Array<Rid> _ownRids = new();

	/// <summary>Per-limb hitboxes so AI bullets hurt the player; on layer 5 so the player's own melee ignores them.</summary>
	private void BuildHitboxes()
	{
		_hitboxRoot = new Node3D { Name = "Hitboxes" };
		AddChild(_hitboxRoot);
		HitboxRig.Build(_hitboxRoot, Health);
		_ownRids.Add(GetRid());
		foreach (var child in _hitboxRoot.GetChildren())
		{
			if (child is not HitboxArea area)
				continue;
			area.CollisionLayer = 1u << 4;
			area.CollisionMask = 0;
			_ownRids.Add(area.GetRid());
		}
	}

	/// <summary>Squashes the standing hitbox set to the current stance height.</summary>
	private void UpdateHitboxes()
	{
		var scale = Mathf.Clamp(HeightFor(_transition.PerceivedStance(Stance)) / Mathf.Max(0.1f, _cfg.StandHeight), 0.3f, 1f);
		HitboxRig.SetHeightScale(_hitboxRoot, scale);
	}

	// --- IPerceivable ----------------------------------------------------------------------

	/// <summary>Dev: overrides the configured side to test who is hostile.</summary>
	public Faction? DebugFaction { get; set; }

	public Faction Faction => DebugFaction ?? GameState.Instance?.Profile.Side?.ToFaction() ?? _cfg.PlayerFaction;
	public bool IsAlive => !Health.IsDead && !_noclip;
	public Stance PerceivedStance => _transition.PerceivedStance(Stance);
	public float PerceivedSpeedFraction
		=> Mathf.Clamp(new Vector2(Velocity.X, Velocity.Z).Length() / Mathf.Max(0.1f, _cfg.SprintSpeed), 0f, 1f);
	public Rid BodyRid => GetRid();

	public Vector3[] BodyPoints()
	{
		var h = HeightFor(Stance);
		var p = GlobalPosition;
		return new[]
		{
			p + Vector3.Up * (h * 0.92f),
			p + Vector3.Up * (h * 0.62f),
			p + Vector3.Up * (h * 0.2f),
		};
	}

	private void TickStamina(float delta)
	{
		var recovery = _cfg.RecoveryPerS * _weights.StaminaRecoveryMult(Tier)
			* Health.Vitals.StaminaRecoveryMultiplier;
		Legs.Tick(delta, recovery);
		Arms.Tick(delta, recovery);
	}

	private void UpdateRig(float delta)
	{
		var allowed = (Stance != Stance.Prone || !_transition.Locked) && !IsSprinting && !_mantling;
		var target = LookModel.LeanTarget(Input.IsActionPressed("lean_left"),
			Input.IsActionPressed("lean_right"), allowed);
		_lean = LookModel.LeanStep(_lean, target, _cfg.LeanSpeed, delta);
		var targetEye = _move.EyeHeight(Stance, _heights.CrouchLevel);
		_eyeY = _transition.Locked
			? Mathf.Lerp(_transitionFromEye, targetEye, _transition.EyeBlend) + _transition.EyeSettleM
			: Mathf.MoveToward(_eyeY, targetEye, _cfg.EyeHeightSpeed * delta);
		var pivotY = _eyeY * _cfg.LeanPivotFraction;
		// prone the lean rolls the torso onto the shoulder and hip: a bigger roll and a smaller shift
		var proneLeaning = Stance == Stance.Prone;
		var leanDistance = proneLeaning ? _proneLean.ShiftM : _cfg.LeanDistance;
		var leanRoll = proneLeaning ? _proneLean.MaxRollDeg : _cfg.LeanRollDeg;
		var shift = ClampLeanToWalls(_lean * leanDistance);
		var ratio = leanDistance > 0f ? shift / leanDistance : 0f;
		_leanRatio = proneLeaning ? ratio : 0f;
		_pivot.Position = new Vector3(shift, pivotY, 0f);
		_pivot.Rotation = new Vector3(Mathf.DegToRad(_transition.CameraPitchDeg), 0f, -Mathf.DegToRad(leanRoll) * ratio);
		_body.SetGripWeight(_transition.GripWeight);
		var stanceMix = BodyPoseModel.StanceBlend(_eyeY, _cfg.StandEyeHeight, _cfg.CrouchEyeHeight, _cfg.ProneEyeHeight);
		var proneActive = stanceMix.Prone >= 0.5f && !_mantling;
		if (proneActive)
		{
			// lying down: a free cone, then a rate-clamped hip drag that stops against geometry (animation spec s1.7)
			var pelvis = _proneTurn.PelvisYawDeg;
			var proneYaw = _proneTurn.Step(delta, _proneViewYaw, Mathf.RadToDeg(Rotation.Y),
				ProneBodyHits(pelvis + 1.5f), ProneBodyHits(pelvis - 1.5f));
			Rotation = new Vector3(0f, Mathf.DegToRad(proneYaw), 0f);
			_proneViewYaw = proneYaw;
			_bodyTwistDeg = _proneTurn.TwistDeg;
			_pivotModel.Reset(_proneTurn.PelvisYawDeg);
		}
		else
		{
			_proneTurn.Reset(Mathf.RadToDeg(Rotation.Y));
			_proneViewYaw = Mathf.RadToDeg(Rotation.Y);
			_pivotModel.Update(delta, Mathf.RadToDeg(Rotation.Y), stanceMix, new Vector2(Velocity.X, Velocity.Z).Length(),
				_mantling || !IsOnFloor() || _transition.Locked);
			_bodyTwistDeg = _pivotModel.TwistDeg;
		}
		_body.Rotation = new Vector3(0f, Mathf.DegToRad(-_bodyTwistDeg), 0f); // hips and boots stay where the pivot or the drag left them
		_pivot.Rotation += new Vector3(0f, 0f, Mathf.DegToRad(_proneTurn.BankDeg) * _cfg.ViewBobScale); // the camera banks as the hips are dragged round
		_crawl.Update(delta, proneActive && !_transition.Locked, new Vector2(Velocity.X, Velocity.Z).Length());
		_pivot.Rotation += new Vector3(Mathf.DegToRad(_crawl.PitchDeg), 0f, Mathf.DegToRad(_crawl.RollDeg)) * _cfg.ViewBobScale; // the army crawl's rhythmic sway
		ApplyViewBob(delta, pivotY);
		// first-person pivot cues: a dip and a quick sideways jitter as the foot lifts (the view bob slider scales them)
		_head.Position += Vector3.Up * _pivotModel.CameraDipM * _cfg.ViewBobScale;
		_head.Rotation += new Vector3(0f, Mathf.DegToRad(_pivotModel.JitterDeg) * _cfg.ViewBobScale, 0f);

		if (!Input.IsActionPressed("free_look"))
			_freeLook = LookModel.FreeLookReturn(_freeLook, _cfg.FreeLookReturnDegS, delta);
		_free.Rotation = new Vector3(Mathf.DegToRad(_freeLook.Y), Mathf.DegToRad(_freeLook.X), 0f);
		var local = GlobalBasis.Inverse() * Velocity;
		// the legs animate against the pelvis, which can lag the view inside the cone
		local = new Basis(Vector3.Up, Mathf.DegToRad(_bodyTwistDeg)) * local;
		_body.Locomote(local, _eyeY, !IsOnFloor() || _mantling);
		var proneWeight = BodyPoseModel.StanceBlend(_eyeY, _cfg.StandEyeHeight, _cfg.CrouchEyeHeight, _cfg.ProneEyeHeight).Prone;
		_body.UpdateFootIk(GetWorld3D().DirectSpaceState, CollisionMask, GetRid(), IsOnFloor() && !_mantling, proneWeight);
		_body.Pose(_eyeY, _pivot.Rotation.Z, shift, Mathf.RadToDeg(_head.Rotation.X), _freeLook, _bodyTwistDeg);
	}

	/// <summary>Small procedural camera bob in step with the feet, plus a dip on landing (animation spec s4.6).</summary>
	private void ApplyViewBob(float delta, float pivotY)
	{
		var horizontal = new Vector2(Velocity.X, Velocity.Z).Length();
		var onFloor = IsOnFloor();
		var move = ViewBob.MoveOf(onFloor && !_mantling, horizontal, IsSprinting, Stance);
		_bobPhase += horizontal * delta / _bobTuning.StrideM * Mathf.Tau;
		var scale = ViewBob.Scale(_bobTuning, _aim.Amount, _support.RestAmount, _cfg.ViewBobScale);
		var (vertical, lateral, rollDeg) = ViewBob.Offset(_bobTuning, move, _bobPhase);
		if (!onFloor)
			_fallSpeed = Mathf.Max(_fallSpeed, -Velocity.Y);
		else if (!_wasOnFloor && _fallSpeed > 2f)
			_landDip.Impulse(ViewBob.LandingImpulse(_bobTuning, _fallSpeed));
		if (onFloor)
			_fallSpeed = 0f;
		_wasOnFloor = onFloor;
		_landDip.Update(delta, 120f, 0.7f);
		_stepOffset = Mathf.Lerp(_stepOffset, 0f, 1f - Mathf.Exp(-delta / Mathf.Max(0.01f, _cfg.StepSmoothS)));
		_head.Position = new Vector3(lateral * scale, _eyeY - pivotY + vertical * scale + _landDip.Value + _stepOffset, 0f);
		var headRot = _head.Rotation;
		headRot.Z = Mathf.DegToRad(rollDeg * scale);
		_head.Rotation = headRot;
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
		Weapon?.Controller.Interrupt();
		IsSprinting = false;
		Velocity = Vector3.Zero;
		Legs.Spend(_cfg.MantleStaminaCost * _weights.StaminaDrainMult(Tier));
		_skills.Mantled();
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
		if (_transition.BlocksStanceInput)
			return; // going prone and getting up cannot be cancelled
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
		var from = Stance;
		Stance = next;
		ApplyStanceShape();
		_transitionFromEye = _eyeY;
		if (_transition.TryStart(from, next) && _transition.Locked)
			BeginLockedTransition(next == Stance.Prone);
		StanceChanged?.Invoke(Stance);
	}

	/// <summary>Going prone or getting up: the weapon action stops, the body makes a noise (gear thud, scuffing).</summary>
	private void BeginLockedTransition(bool prone)
	{
		Weapon?.Controller.Interrupt();
		GroundDust.Puff(this, GlobalPosition, prone ? 1f : 0.5f);
		NoiseBus.Emit(GlobalPosition, prone ? 8f : 4f, "footstep", this);
	}

	private float HeightFor(Stance stance) => _move.CapsuleHeight(stance, _heights.CrouchLevel);

	/// <summary>Hold C and scroll: raises or lowers the crouch, standing up at the top.</summary>
	private void AdjustHeight(float notches)
	{
		_crouchAdjusted = true;
		if (Stance == Stance.Prone || _transition.BlocksStanceInput)
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
		_transitionFromEye = _eyeY;
		_transition.StartStep(oldStance, next);
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

	/// <summary>
	/// True when the prone body, turned to this pelvis yaw, would touch the world: a capsule 1.8 m long lying behind the
	/// head. It floats a few centimetres above the floor so only things standing on it count.
	/// </summary>
	private bool ProneBodyHits(float pelvisYawDeg)
	{
		_proneProbe ??= new PhysicsShapeQueryParameters3D
		{
			Shape = new CapsuleShape3D { Radius = 0.25f, Height = 1.8f },
			CollisionMask = CollisionMask,
			Exclude = new Array<Rid> { GetRid() },
		};
		var yaw = new Basis(Vector3.Up, Mathf.DegToRad(pelvisYawDeg));
		// the capsule's axis is Y: lay it along the body (Z), centred 0.8 m behind the head and 0.32 m up
		var lying = yaw * new Basis(Vector3.Right, Mathf.Pi * 0.5f);
		_proneProbe.Transform = new Transform3D(lying, GlobalPosition + yaw * new Vector3(0f, 0.32f, 0.8f));
		return GetWorld3D().DirectSpaceState.IntersectShape(_proneProbe, 1).Count > 0;
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
