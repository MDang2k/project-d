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

	/// <summary>Dev helper: same as pressing Tab.</summary>
	public void DebugToggleInventory() => _screen.Toggle();

	/// <summary>Result of a melee strike that landed on a hitbox.</summary>
	public event Action<HitResult>? MeleeHit;

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
	private InventoryComponent? _inventory;
	private MeleeModel _melee = null!;
	private InventoryScreen _screen = null!;
	private Label _prompt = null!;
	private ItemInstance? _pendingUse;
	private int? _heldWeaponUid;
	private MagazineLoader _loader = null!;
	private ItemInstance? _loadAmmo;
	private PainOverlay _pain = null!;
	private Penalties _penalties = new(1f, 1f, false, 1f, 0f);
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
		_loader.RoundUnloaded += OnRoundUnloaded;
		_screen = new InventoryScreen { Name = "InventoryScreen" };
		AddChild(_screen);
		_screen.Bind(this);
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
		AddChild(promptLayer);
		Inventory.Changed += RefreshHeldWeapon;
		Health.MedicalFinished += (_, ok) => FinishPendingUse(ok);
		Health.ConsumableFinished += _ => FinishPendingUse(true);

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
		if (run.Inventory is { } carried)
			inv.Adopt(carried);
		if (run.Inventory is null || run.NeedsKit)
		{
			inv.GiveStartingKit();
			run.MarkKitGiven();
		}
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
		AddChild(h);
		return h;
	}

	/// <summary>Injury penalties, vitals activity and the pain screen effect.</summary>
	private void UpdateHealthLinks()
	{
		var h = Health;
		h.Sprinting = IsSprinting;
		h.HeavyLoad = Tier >= WeightTier.Heavy;
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
		_prompt.Visible = (container is not null || dropped is not null) && !_screen.IsOpen && !Health.IsDead;
		if (!_prompt.Visible)
			return;
		_prompt.Text = dropped is not null
			? GameText.Get("msg.pickup.prompt", dropped.Title)
			: GameText.Get("msg.search.prompt", container!.Title);
		if (!Input.IsActionJustPressed("interact") || Input.MouseMode != Input.MouseModeEnum.Captured)
			return;
		if (dropped is not null)
			PickUp(dropped);
		else
			_screen.OpenWithContainer(container!);
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

	/// <summary>Melee on G: windup, then a short ray from the camera hits one hitbox.</summary>
	private void UpdateMelee(float delta)
	{
		var captured = Input.MouseMode == Input.MouseModeEnum.Captured;
		var weaponBusy = Weapon?.Controller.IsBusy ?? false;
		if (captured && !_mantling && !weaponBusy && Input.IsActionJustPressed("melee")
			&& _melee.TryStart(Arms.IsExhausted))
			Arms.Spend(_melee.Spec.StaminaCost);
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
			var result = spec.IgnoresArmor
				? target.ReceiveUnarmoredDamage(area.Part, spec.Damage)
				: target.ReceiveDamage(area.Part, spec.Damage, spec.PenetrationTier);
			var key = !result.ArmorHit ? "msg.melee.hit" : result.Penetrated ? "msg.melee.hit_armor" : "msg.melee.hit_stopped";
			WeaponMessage?.Invoke(GameText.Get(key, area.Part, result.Damage.ToString("0.#")));
			MeleeHit?.Invoke(result);
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
		if (Inventory.Inventory.InSlot(slot) is null)
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
			return;
		if (ItemDb.Data(item.Spec.Id)?.Payload is WeaponData data)
		{
			EquipWeapon(data, item);
			_heldWeaponUid = item.Uid;
		}
	}

	/// <summary>Uses a medical, food or drink item from the inventory; consumed when finished.</summary>
	public bool UseItem(ItemInstance item)
	{
		if (_pendingUse is not null || Health.IsDead)
			return false;
		var started = ItemDb.Data(item.Spec.Id)?.Payload switch
		{
			MedicalData m => Health.UseMedical(m),
			ConsumableData c => Health.UseConsumable(c),
			_ => false,
		};
		if (started)
			_pendingUse = item;
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
		weapon.DryFired += () => WeaponMessage?.Invoke(GameText.Get("msg.weapon.click"));
		weapon.Controller.MagChecked += r => WeaponMessage?.Invoke(GameText.ForMagCheck(r));
		weapon.Controller.ChamberChecked += loaded =>
			WeaponMessage?.Invoke(GameText.Get(loaded ? "msg.chamber.loaded" : "msg.chamber.empty"));
		weapon.Controller.MagazineDropped += m => OnMagazineDropped(weapon, m);
		_body.AttachHeldItem(weapon);
	}

	private void OnShotFired(AmmoType ammo, Vector3 position, Vector3 direction)
	{
		_projectiles ??= GetTree().GetFirstNodeInGroup("projectiles") as ProjectileManager;
		_projectiles?.Spawn(ammo, position, direction, GetRid(), _ownRids);
		NoiseBus.Emit(position, Weapon?.Data.NoiseRadius ?? 150f, "gunshot", this);
		if (Weapon is { } w)
			_recoil.Kick(w.Data.ToRecoilTuning(),
				HandlingModel.RecoilMultiplier(_handling, Stance, _aim.Amount, _support.RestAmount,
					_support.BipodDeployed),
				GD.Randf() * 2f - 1f);
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
		=> Weapon is not null && !_mantling && !Weapon.Controller.IsBusy && !_melee.IsBusy
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
		var pitchDeg = Mathf.RadToDeg(_head.Rotation.X);
		var pitchLimit = -_cfg.RestMaxPitchDownDeg - (_restPitchOk ? _cfg.RestPitchHysteresisDeg : 0f);
		_restPitchOk = pitchDeg >= pitchLimit;
		var restSurface = FindRestSurface(weapon, out var surfaceY) && _restPitchOk;
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
		SwayAmplitudeDeg = HandlingModel.SwayAmplitude(_handling, weapon.Data.SwayBaseDeg, ctx)
			* _penalties.SwayMultiplier * Health.Vitals.SwayMultiplier;
		var sway = _swayModel.Update(delta, SwayAmplitudeDeg);
		var (posOffset, rotDeg) = WeaponPoseModel.Pose(weapon.Controller.Action, weapon.Controller.Progress);
		_swayNode.Rotation = new Vector3(Mathf.DegToRad(sway.Y + rotDeg.X), Mathf.DegToRad(sway.X + rotDeg.Y),
			Mathf.DegToRad(rotDeg.Z));

		_recoil.Update(delta, weapon.Data.ToRecoilTuning());
		_recoilNode.Rotation = new Vector3(Mathf.DegToRad(_recoil.Offset.Y), Mathf.DegToRad(_recoil.Offset.X), 0f);

		var t = Mathf.SmoothStep(0f, 1f, _aim.Amount);
		var adsPos = new Vector3(0f, -weapon.Data.SightHeight, -_cfg.AdsEyeRelief);
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
		if (_melee.IsBusy)
		{
			var strikeAt = _melee.Spec.WindupS / Mathf.Max(0.01f, _melee.Spec.WindupS + _melee.Spec.RecoverS);
			var (mp, mr) = WeaponPoseModel.MeleePose(_melee.Progress, strikeAt);
			_swayNode.Position += mp;
			_swayNode.Rotation += new Vector3(Mathf.DegToRad(mr.X), Mathf.DegToRad(mr.Y), Mathf.DegToRad(mr.Z));
		}
		_camera.Fov = Mathf.Lerp(_cfg.HipFovDeg, _cfg.AdsFovDeg, t);
	}

	private void UpdateWeaponInput()
	{
		var weapon = Weapon;
		if (weapon is null)
			return;
		var captured = Input.MouseMode == Input.MouseModeEnum.Captured;
		weapon.Controller.ReloadTimeMultiplier = _penalties.ReloadTimeMultiplier;
		weapon.CanFire = (captured || DebugForceTrigger) && !IsSprinting && !_mantling
			&& !_clearance.BlocksFire && !_melee.IsBusy;
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
		if (_melee.IsBusy)
			return;
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
		Tier = _weights.TierFor(TotalCarriedKg, StrengthBonusKg);
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
		UpdateWeaponInput();
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

		var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		if (Input.IsActionJustPressed("jump") && IsOnFloor() && input.Y < -0.1f
			&& Stance == Stance.Stand && TryMantle())
			return;

		var wish = GlobalBasis * new Vector3(input.X, 0f, input.Y);
		// While aiming, Shift holds breath instead of sprinting (GDD s7).
		var wantsSprint = Input.IsActionPressed("sprint") && input.Y < -0.1f && Stance != Stance.Prone
			&& !AimHeld();
		IsSprinting = wantsSprint && _weights.CanSprint(Tier) && !Legs.IsExhausted && !_penalties.SprintBlocked;

		var speed = _move.TargetSpeed(Stance, IsSprinting, Tier, _heights.CrouchLevel)
			* _penalties.MoveSpeedMultiplier;
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
		UpdateFootsteps(delta);
		UpdateHitboxes();
		UpdateHandling(delta);
		UpdateRig(delta);
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
		var scale = Mathf.Clamp(HeightFor(Stance) / Mathf.Max(0.1f, _cfg.StandHeight), 0.3f, 1f);
		HitboxRig.SetHeightScale(_hitboxRoot, scale);
	}

	// --- IPerceivable ----------------------------------------------------------------------

	/// <summary>Dev: overrides the configured side to test who is hostile.</summary>
	public Faction? DebugFaction { get; set; }

	public Faction Faction => DebugFaction ?? _cfg.PlayerFaction;
	public bool IsAlive => !Health.IsDead && !_noclip;
	public Stance PerceivedStance => Stance;
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
