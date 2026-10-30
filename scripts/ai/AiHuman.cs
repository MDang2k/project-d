using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// Human AI body (GDD s16). M5 step 1: placeholder body, shared health and hitboxes, a held weapon,
/// and perception (vision cone with line of sight, hearing through <see cref="NoiseBus"/>).
/// It only turns toward what it notices; the state machine arrives in the next step.
/// </summary>
public partial class AiHuman : CharacterBody3D, IPerceivable
{
	private const float TickS = 0.1f;
	private const float EyeHeight = 1.6f;
	private const float RespawnS = 8f;

	/// <summary>The actor this AI plays (stats, tags, item ids); set before the node enters the tree.</summary>
	public ActorInstance? Actor { get; set; }

	public string ActorId => Actor?.Def.Id ?? "";

	/// <summary>Temperament rolled at spawn (infiltrator, rat, operator, conscript), or empty.</summary>
	public string Archetype => Actor?.Archetype ?? "";

	private float S(string stat) => Actor!.Stats.Get(stat);
	private int SInt(string stat) => Actor!.Stats.GetInt(stat);
	private ActorDef Def => Actor!.Def;

	/// <summary>Dev: dead AI come back after a few seconds. Off lets bodies stay and be looted.</summary>
	public static bool AutoRespawn { get; set; } = true;

	/// <summary>Dev: freezes every AI in place (they stop thinking, moving and shooting).</summary>
	public static bool Paused { get; set; }

	/// <summary>Draw the vision cone and awareness label (dev).</summary>
	public bool ShowDebug
	{
		get => _showDebug;
		set
		{
			_showDebug = value;
			if (_label is not null)
				_label.Visible = value;
			if (_cone is not null)
				_cone.Visible = value;
		}
	}

	/// <summary>Patrol route (world positions); set before the node enters the tree. Empty means stand and scan.</summary>
	public Vector3[] PatrolPoints { get; set; } = System.Array.Empty<Vector3>();

	public HealthComponent Health { get; private set; } = null!;
	public PerceptionModel Perception { get; private set; } = null!;
	public AiBrain Brain { get; private set; } = null!;
	public WeaponNode? Weapon { get; private set; }
	public string LastCue { get; private set; } = "-";

	private PerceptionTuning _tuning = PerceptionTuning.Default;
	private readonly List<NoiseEvent> _noises = new();
	private Label3D? _label;
	private MeshInstance3D? _cone;
	private bool _showDebug = true;
	private float _tickAcc;
	private float _deadTimer;
	private float _scanClock;
	private float _scanBaseYaw;
	private NavigationAgent3D _agent = null!;
	private Vector3 _agentTarget = new(float.NaN, 0f, 0f);
	private float _gravity = 9.8f;

	// --- IPerceivable ----------------------------------------------------------------------

	public Faction Faction => Actor is null ? Faction.Looters : ActorTuning.ParseFaction(Actor.Def.Faction);
	public bool IsAlive => !Health.IsDead;
	public Stance PerceivedStance => _prone ? Stance.Prone : IsCrouching ? Stance.Crouch : Stance.Stand;
	public float PerceivedSpeedFraction
		=> Mathf.Clamp(new Vector2(Velocity.X, Velocity.Z).Length() / 5.2f, 0f, 1f);
	public Rid BodyRid => GetRid();

	public Vector3[] BodyPoints()
	{
		var p = GlobalPosition;
		var lean = GlobalBasis.X * LeanOffset;
		return new[] { p + lean + Vector3.Up * 1.65f, p + lean + Vector3.Up * 1.25f, p + Vector3.Up * 0.4f };
	}

	// --- setup -----------------------------------------------------------------------------

	public override void _Ready()
	{
		Actor ??= ActorDb.Create("looter");
		_tuning = ActorTuning.Perception(Actor.Stats);
		Perception = new PerceptionModel(_tuning);
		Brain = new AiBrain(ActorTuning.Brain(Actor.Stats));
		Brain.SetPatrol(PatrolPoints);
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsSingle();
		CollisionLayer = 1u << 3;
		CollisionMask = 1u;
		AddToGroup("perceivables");
		_scanBaseYaw = Rotation.Y;
		_agent = new NavigationAgent3D
		{
			PathDesiredDistance = 0.6f,
			TargetDesiredDistance = S(Stats.Brain.ArrivalRadius),
			Radius = 0.4f,
			Height = 1.8f,
		};
		AddChild(_agent);

		AddChild(new CollisionShape3D
		{
			Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.8f },
			Position = new Vector3(0f, 0.9f, 0f),
		});

		Health = new HealthComponent { Name = "Health", Config = new HealthConfig
		{
			HeadHp = S(Stats.Health.Head), ThoraxHp = S(Stats.Health.Thorax), StomachHp = S(Stats.Health.Stomach),
			ArmHp = S(Stats.Health.Arm), LegHp = S(Stats.Health.Leg),
		} };
		AddChild(Health);
		RollLoadout();
		BuildGear();
		Health.Died += _ => OnDied();
		_hitboxRoot = new Node3D { Name = "Hitboxes" };
		AddChild(_hitboxRoot);
		HitboxRig.Build(_hitboxRoot, Health);

		BuildBody();
		BuildDebug();
		NoiseBus.Noise += OnNoise;
		InitCombat();
		JoinSquad();
	}

	public override void _ExitTree()
	{
		NoiseBus.Noise -= OnNoise;
		LeaveSquad();
		ExitCombat();
	}

	private void BuildBody()
	{
		var mat = new StandardMaterial3D { AlbedoColor = BodyColor() };
		_bodyMesh = new MeshInstance3D
		{
			Mesh = new CapsuleMesh { Radius = 0.24f, Height = 1.45f },
			Position = new Vector3(0f, 0.85f, 0f),
			MaterialOverride = mat,
		};
		AddChild(_bodyMesh);
		AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.11f, Height = 0.22f },
			Position = new Vector3(0f, 1.65f, 0f),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.6f, 0.5f) },
		});
		// A small nose block shows which way the AI faces.
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.12f) },
			Position = new Vector3(0f, 1.65f, -0.13f),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.1f) },
		});
		if (_weaponData is { } data)
		{
			Weapon = WeaponNode.Create(data);
			Weapon.Position = new Vector3(0.18f, 1.2f, -0.2f);
			AddChild(Weapon);
		}
	}

	private void BuildDebug()
	{
		_label = new Label3D
		{
			Position = new Vector3(0f, 2.3f, 0f),
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FontSize = 32,
			FixedSize = true,
			PixelSize = 0.0007f,
			NoDepthTest = true,
			Modulate = new Color(1f, 1f, 0.7f),
			Visible = _showDebug,
		};
		AddChild(_label);

		// Flat wedge on the ground showing the vision cone (capped at 25 m so it stays readable).
		var radius = Mathf.Min(_tuning.ViewDistance, 25f);
		var half = Mathf.DegToRad(_tuning.FovDeg * 0.5f);
		var mesh = new ImmediateMesh();
		mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
		const int steps = 16;
		for (var i = 0; i < steps; i++)
		{
			var a0 = Mathf.Lerp(-half, half, i / (float)steps);
			var a1 = Mathf.Lerp(-half, half, (i + 1) / (float)steps);
			mesh.SurfaceAddVertex(new Vector3(0f, 0.04f, 0f));
			mesh.SurfaceAddVertex(new Vector3(Mathf.Sin(a0), 0.04f, -Mathf.Cos(a0)) * radius);
			mesh.SurfaceAddVertex(new Vector3(Mathf.Sin(a1), 0.04f, -Mathf.Cos(a1)) * radius);
		}
		mesh.SurfaceEnd();
		_cone = new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.9f, 0.2f, 0.12f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = _showDebug,
		};
		AddChild(_cone);
	}

	// --- perception ------------------------------------------------------------------------

	private void OnNoise(NoiseEvent e)
	{
		// Ignore own noise, including the search of the body being looted right now.
		if (!ReferenceEquals(e.Source, this) && !ReferenceEquals(e.Source, _lootTarget)
			&& Health is { IsDead: false })
			_noises.Add(e);
	}

	public override void _PhysicsProcess(double deltaD)
	{
		var delta = (float)deltaD;
		if (Health.IsDead)
		{
			Brain.Update(delta, new AiSenses(AwarenessLevel.Unaware, false, null, 99f), false, true);
			_noises.Clear();
			if (AutoRespawn)
				_deadTimer -= delta;
			if (_deadTimer <= 0f)
				Respawn();
			UpdateLabel();
			return;
		}

		if (Paused)
		{
			Velocity = Vector3.Zero;
			_noises.Clear();
			if (Weapon is not null)
			{
				Weapon.TriggerHeld = false;
				Weapon.TriggerPressed = false;
			}
			return;
		}

		_tickAcc += delta;
		if (_tickAcc >= TickS)
		{
			PerceiveTick(_tickAcc);
			_tickAcc = 0f;
		}
		var senses = new AiSenses(Perception.Level, Perception.SeesTarget, Perception.InterestPoint,
			Perception.SecondsSinceCue, FindLootPoint(delta));
		Brain.Update(delta, senses, _arrived, false);
		Move(delta);
		UpdateLooting();
		UpdateLabel();
	}

	private bool _arrived;
	private bool _proneCover;

	/// <summary>Walks the brain's destination over the navmesh; in combat closes in and faces the target.</summary>
	private void Move(float delta)
	{
		Vector3? destination = Brain.Destination;
		var speed = Brain.State == AiState.Patrol ? S(Stats.Move.WalkSpeed) : S(Stats.Move.RunSpeed) * 0.75f;
		float? faceYaw = null;

		var crouch = false;
		var inCombat = Brain.State == AiState.Combat;
		CheckFledAway(delta);
		if (_fleeing && !inCombat)
		{
			destination = _fleePoint;
			speed = S(Stats.Move.SprintSpeed);
		}
		if (inCombat)
		{
			var plan = PlanCombat(delta);
			destination = plan.Destination;
			speed = plan.Speed;
			faceYaw = plan.FaceYaw;
			crouch = plan.Crouch;
			_proneCover = plan.Prone;
		}
		else
		{
			_wantsFire = false;
			_underFire.Reset();
			_suppression = Mathf.Max(0f, _suppression - 0.3f * delta);
		}
		UpdateWeapon(delta, inCombat);
		ApplyCrouch(delta, crouch, _underFire.IsProne || (inCombat && _proneCover));
		ApplyLean(delta, _leaning ? _leanSide : 0f);

		var flatVel = Vector3.Zero;
		_arrived = destination is null;
		if (destination is { } dest)
		{
			if (!_agentTarget.IsFinite() || _agentTarget.DistanceTo(dest) > 0.5f)
			{
				_agentTarget = dest;
				_agent.TargetPosition = dest;
			}
			var flat = new Vector2(dest.X - GlobalPosition.X, dest.Z - GlobalPosition.Z);
			if (flat.Length() <= S(Stats.Brain.ArrivalRadius) || _agent.IsNavigationFinished())
			{
				_arrived = true;
			}
			else
			{
				var next = _agent.GetNextPathPosition() - GlobalPosition;
				next.Y = 0f;
				if (next.LengthSquared() > 0.0001f)
				{
					flatVel = next.Normalized() * speed;
					faceYaw ??= Mathf.Atan2(-next.X, -next.Z);
				}
			}
		}
		else
		{
			_agentTarget = new Vector3(float.NaN, 0f, 0f);
		}

		var vel = Velocity;
		// accelerate like the player instead of snapping to speed; brake harder than we start
		var horizontal = new Vector2(vel.X, vel.Z);
		var wanted = new Vector2(flatVel.X, flatVel.Z);
		horizontal = horizontal.MoveToward(wanted, (wanted.Length() > horizontal.Length() ? S(Stats.Move.Accel) : S(Stats.Move.Decel)) * delta);
		vel.X = horizontal.X;
		vel.Z = horizontal.Y;
		vel.Y = IsOnFloor() ? 0f : vel.Y - _gravity * delta;
		Velocity = vel;
		var wasOnFloor = IsOnFloor();
		var lifted = StepUp.Try(this, flatVel, delta, 0.3f);
		MoveAndSlide();
		if (!lifted)
			StepUp.Down(this, 0.3f, wasOnFloor);

		// Standing around: sweep the view around where the AI was last facing.
		if (faceYaw is null)
		{
			_scanClock += delta;
			faceYaw = _scanBaseYaw + Mathf.DegToRad(Mathf.Sin(_scanClock * 0.5f) * 70f);
		}
		else
		{
			_scanBaseYaw = faceYaw.Value;
		}
		var yaw = Mathf.RotateToward(Rotation.Y, faceYaw.Value, Mathf.DegToRad(_underFire.TurnsFast ? S(Stats.React.TurnDegS) : S(Stats.Move.TurnDegS)) * delta);
		Rotation = new Vector3(0f, yaw, 0f);
	}

	/// <summary>Body colour from the actor sheet (red, green, blue), a drab default when missing.</summary>
	private Color BodyColor()
		=> ActorTuning.ParseColor(Def.List("body_color")) is { } c ? new Color(c.R, c.G, c.B) : new Color(0.45f, 0.4f, 0.3f);

	private ResolvedLoadout _loadout = new(System.Array.Empty<string>(), System.Array.Empty<string>(), "", "");
	private WeaponData? _weaponData;
	private readonly List<string> _wornIds = new();
	private LootContainer? _lootTarget;
	private float _lootScanS;

	/// <summary>Picks a weapon from the preset's table and wears each armor piece with its chance.</summary>
	private void RollLoadout()
	{
		var rng = new System.Random();
		_loadout = ActorDb.Catalog.Loadouts.Resolve(Def, Actor!.Tier, rng);
		var choices = _loadout.Weapons;
		_weaponData = choices.Count > 0 ? ActorDb.Weapon(choices[rng.Next(choices.Count)]) : null;
		_wornIds.Clear();
		foreach (var armorId in _loadout.Armor)
		{
			if (rng.NextDouble() > S(Stats.Loadout.ArmorChance) || ActorDb.Armor(armorId) is not { } a)
				continue;
			Health.Equip(a);
			_wornIds.Add(a.Id);
		}
	}

	private void OnDied()
	{
		_deadTimer = RespawnS;
		Squad?.Died(_id);
		_lootTarget?.Model.StopSearch();
		_lootTarget = null;
		SyncGearWithWeapon();
		CorpseSpawner.SpawnBody(GetParent(), GlobalPosition, Gear);
		Gear = new CharacterInventory(); // the body owns the gear now
	}

	/// <summary>Nearest untouched body within reach, when this AI scavenges.</summary>
	private Vector3? FindLootPoint(float delta)
	{
		if (!Def.Flag("loots_corpses"))
			return null;
		if (_lootTarget is { } t && (!IsInstanceValid(t) || t.Taken))
			_lootTarget = null;
		_lootScanS -= delta;
		if (_lootTarget is null && _lootScanS <= 0f && Brain.State is AiState.Idle or AiState.Patrol)
		{
			_lootScanS = 1f;
			var best = S(Stats.Scavenge.LootRadius);
			foreach (var node in GetTree().GetNodesInGroup("loot_containers"))
			{
				if (node is not LootContainer c || !c.IsCorpse || c.Taken)
					continue;
				var d = c.GlobalPosition.DistanceTo(GlobalPosition);
				if (d < best)
				{
					best = d;
					_lootTarget = c;
				}
			}
		}
		return _lootTarget?.GlobalPosition;
	}

	/// <summary>At the body: search it (noisy, like the player), then take everything.</summary>
	private void UpdateLooting()
	{
		if (_lootTarget is null || !IsInstanceValid(_lootTarget))
			return;
		if (Brain.State == AiState.Loot && _arrived)
		{
			_lootTarget.Model.StartSearch();
			if (_lootTarget.Model.FullyRevealed)
			{
				LootedItems += _lootTarget.TakeAll();
				_lootTarget = null;
			}
		}
		else if (Brain.State != AiState.Loot)
		{
			_lootTarget.Model.StopSearch();
			if (Brain.State is not (AiState.Idle or AiState.Patrol))
				_lootTarget = null;
		}
	}

	/// <summary>Items this AI has stripped from bodies (dev reporting).</summary>
	public int LootedItems { get; private set; }

	private void Respawn()
	{
		BuildGear();
		Health.Reset();
		Perception.Reset();
		_fleeing = false;
		LeaveSquad();
		JoinSquad(); // back among whoever is near, with a clean record
		LastCue = "-";
		Brain.Update(0f, new AiSenses(AwarenessLevel.Unaware, false, null, 99f), false, false);
	}

	/// <summary>Opacity of each foliage volume a sight line crosses, up to six.</summary>
	private System.Collections.Generic.List<float> FoliageOpacities(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to)
	{
		var found = new System.Collections.Generic.List<float>();
		var skip = new Godot.Collections.Array<Rid>();
		for (var i = 0; i < 6; i++)
		{
			var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, FoliageVolume.Layer, skip));
			if (hit.Count == 0)
				break;
			found.Add(hit["collider"].AsGodotObject() is FoliageVolume f ? f.Opacity : PerceptionEnvironment.DefaultFoliageOpacity);
			skip.Add((Rid)hit["rid"]);
		}
		return found;
	}

	/// <summary>Material tag of each wall between a noise and the listener (metadata "material", concrete when missing).</summary>
	private System.Collections.Generic.List<string?> WallMaterials(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to)
	{
		var walls = new System.Collections.Generic.List<string?>();
		var skip = new Godot.Collections.Array<Rid> { GetRid() };
		for (var i = 0; i < 6; i++)
		{
			var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1u, skip));
			if (hit.Count == 0)
				break;
			var body = hit["collider"].AsGodotObject();
			walls.Add(body is Node node && node.HasMeta("material") ? node.GetMeta("material").AsString() : null);
			skip.Add((Rid)hit["rid"]);
		}
		return walls;
	}

	private void PerceiveTick(float dt)
	{
		var eye = GlobalPosition + Vector3.Up * EyeHeight;
		var forward = -GlobalBasis.Z;
		forward.Y = 0f;
		forward = forward.Normalized();

		// Vision: strongest sighting among hostile perceivables.
		var best = 0f;
		var bestPoint = Vector3.Zero;
		var bestName = "";
		var space = GetWorld3D().DirectSpaceState;
		var hasFoliage = GetTree().GetNodeCountInGroup("foliage") > 0;
		foreach (var node in GetTree().GetNodesInGroup("perceivables"))
		{
			if (node is not IPerceivable other || ReferenceEquals(node, this) || !other.IsAlive
				|| !FactionRelations.IsHostile(Faction, other.Faction))
				continue;
			var points = other.BodyPoints();
			var center = points[1];
			var to = center - eye;
			var distance = to.Length();
			if (distance > _tuning.ViewDistance * 1.4f)
				continue;
			var flat = new Vector3(to.X, 0f, to.Z);
			var angle = flat.LengthSquared() < 0.0001f
				? 0f
				: Mathf.RadToDeg(forward.AngleTo(flat.Normalized()));
			var visible = 0f;
			foreach (var p in points)
			{
				var q = PhysicsRayQueryParameters3D.Create(eye, p, 1u,
					new Godot.Collections.Array<Rid> { GetRid(), other.BodyRid });
				if (space.IntersectRay(q).Count == 0)
					visible += hasFoliage ? PerceptionEnvironment.FoliageVisibility(FoliageOpacities(space, eye, p)) : 1f;
			}
			var light = PerceptionEnvironment.LightFactor(LightZone.At(GetTree(), center));
			var strength = PerceptionModel.VisionStrength(_tuning, distance, angle,
				new TargetProfile(other.PerceivedStance, other.PerceivedSpeedFraction),
				visible / points.Length, light);
			if (strength > best)
			{
				best = strength;
				bestPoint = center;
				bestName = node.Name;
			}
		}
		if (best > 0f)
		{
			Perception.See(best, bestPoint, dt);
			LastCue = $"saw {bestName}";
		}

		// Hearing: every noise since the last tick, loudest wins inside the model.
		foreach (var n in _noises)
		{
			var radius = PerceptionEnvironment.EffectiveRadius(n.Radius, WallMaterials(space, n.Position, eye));
			var loudness = PerceptionModel.Loudness(_tuning, eye.DistanceTo(n.Position), radius)
				* PerceptionEnvironment.CueMultiplier(n.Kind, S(Stats.Tactics.Aggression));
			if (loudness > 0f)
			{
				Perception.Hear(loudness, n.Position);
				LastCue = $"heard {n.Kind}";
			}
		}
		_noises.Clear();
		Perception.Tick(dt);
	}

	private void UpdateLabel()
	{
		if (_label is null || !_showDebug)
			return;
		if (Health.IsDead)
		{
			_label.Text = $"{ActorId}\nDEAD";
			return;
		}
		var seeing = Perception.SeesTarget ? "  SEES" : "";
		var tactic = Brain.State == AiState.Combat ? $"/{_tactics.State}" + (_underFire.Phase == UnderFirePhase.None ? "" : $" !{_underFire.Phase}") : "";
		_label.Text = $"{ActorId}/{Archetype}  [{Brain.State}{tactic}]\n{Perception.Level}  {Perception.Awareness * 100f:0}%{seeing}\n{LastCue}";
		_label.Modulate = Perception.Level switch
		{
			AwarenessLevel.Alert => new Color(1f, 0.3f, 0.25f),
			AwarenessLevel.Suspicious => new Color(1f, 0.85f, 0.2f),
			_ => new Color(0.8f, 1f, 0.8f),
		};
	}
}
