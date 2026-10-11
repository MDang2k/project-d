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
	private const float TurnSpeedDegS = 140f;

	[Export] public AiPreset? Preset { get; set; }

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

	public HealthComponent Health { get; private set; } = null!;
	public PerceptionModel Perception { get; private set; } = null!;
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
	private float _baseYaw;
	private float _gravity = 9.8f;

	// --- IPerceivable ----------------------------------------------------------------------

	public Faction Faction => Preset?.Faction ?? Faction.Looters;
	public bool IsAlive => !Health.IsDead;
	public Stance PerceivedStance => Stance.Stand;
	public float PerceivedSpeedFraction
		=> Mathf.Clamp(new Vector2(Velocity.X, Velocity.Z).Length() / 5.2f, 0f, 1f);
	public Rid BodyRid => GetRid();

	public Vector3[] BodyPoints()
	{
		var p = GlobalPosition;
		return new[] { p + Vector3.Up * 1.65f, p + Vector3.Up * 1.25f, p + Vector3.Up * 0.4f };
	}

	// --- setup -----------------------------------------------------------------------------

	public override void _Ready()
	{
		Preset ??= new AiPreset();
		_tuning = Preset.ToPerceptionTuning();
		Perception = new PerceptionModel(_tuning);
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsSingle();
		CollisionLayer = 1u << 3;
		CollisionMask = 1u;
		AddToGroup("perceivables");
		_baseYaw = Rotation.Y;

		AddChild(new CollisionShape3D
		{
			Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.8f },
			Position = new Vector3(0f, 0.9f, 0f),
		});

		Health = new HealthComponent { Name = "Health", Config = Preset.Health };
		AddChild(Health);
		foreach (var a in Preset.Armor)
			Health.Equip(a);
		Health.Died += _ => _deadTimer = RespawnS;
		HitboxRig.Build(this, Health);

		BuildBody();
		BuildDebug();
		NoiseBus.Noise += OnNoise;
	}

	public override void _ExitTree() => NoiseBus.Noise -= OnNoise;

	private void BuildBody()
	{
		var mat = new StandardMaterial3D { AlbedoColor = Preset!.BodyColor };
		AddChild(new MeshInstance3D
		{
			Mesh = new CapsuleMesh { Radius = 0.24f, Height = 1.45f },
			Position = new Vector3(0f, 0.85f, 0f),
			MaterialOverride = mat,
		});
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
		if (Preset.Weapon is { } data)
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
		if (!ReferenceEquals(e.Source, this) && Health is { IsDead: false })
			_noises.Add(e);
	}

	public override void _PhysicsProcess(double deltaD)
	{
		var delta = (float)deltaD;
		if (Health.IsDead)
		{
			_noises.Clear();
			_deadTimer -= delta;
			if (_deadTimer <= 0f)
				Respawn();
			UpdateLabel();
			return;
		}

		var vel = Velocity;
		vel.X = vel.Z = 0f;
		vel.Y = IsOnFloor() ? 0f : vel.Y - _gravity * delta;
		Velocity = vel;
		MoveAndSlide();

		_tickAcc += delta;
		if (_tickAcc >= TickS)
		{
			PerceiveTick(_tickAcc);
			_tickAcc = 0f;
		}
		Turn(delta);
		UpdateLabel();
	}

	private void Respawn()
	{
		Health.Reset();
		Perception.Reset();
		LastCue = "-";
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
			var visible = 0;
			foreach (var p in points)
			{
				var q = PhysicsRayQueryParameters3D.Create(eye, p, 1u,
					new Godot.Collections.Array<Rid> { GetRid(), other.BodyRid });
				if (space.IntersectRay(q).Count == 0)
					visible++;
			}
			var strength = PerceptionModel.VisionStrength(_tuning, distance, angle,
				new TargetProfile(other.PerceivedStance, other.PerceivedSpeedFraction),
				visible / (float)points.Length);
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
			var loudness = PerceptionModel.Loudness(_tuning, eye.DistanceTo(n.Position), n.Radius);
			if (loudness > 0f)
			{
				Perception.Hear(loudness, n.Position);
				LastCue = $"heard {n.Kind}";
			}
		}
		_noises.Clear();
		Perception.Tick(dt);
	}

	/// <summary>Idle: sweep the view back and forth. Noticed something: turn toward it.</summary>
	private void Turn(float delta)
	{
		float targetYaw;
		if (Perception.InterestPoint is { } point && Perception.Level != AwarenessLevel.Unaware)
		{
			var to = point - GlobalPosition;
			targetYaw = Mathf.Atan2(-to.X, -to.Z);
		}
		else
		{
			_scanClock += delta;
			targetYaw = _baseYaw + Mathf.DegToRad(Mathf.Sin(_scanClock * 0.5f) * 70f);
		}
		var yaw = Mathf.RotateToward(Rotation.Y, targetYaw, Mathf.DegToRad(TurnSpeedDegS) * delta);
		Rotation = new Vector3(0f, yaw, 0f);
	}

	private void UpdateLabel()
	{
		if (_label is null || !_showDebug)
			return;
		if (Health.IsDead)
		{
			_label.Text = $"{Preset!.Id}\nDEAD";
			return;
		}
		var seeing = Perception.SeesTarget ? "  SEES" : "";
		_label.Text = $"{Preset!.Id}\n{Perception.Level}  {Perception.Awareness * 100f:0}%{seeing}\n{LastCue}";
		_label.Modulate = Perception.Level switch
		{
			AwarenessLevel.Alert => new Color(1f, 0.3f, 0.25f),
			AwarenessLevel.Suspicious => new Color(1f, 0.85f, 0.2f),
			_ => new Color(0.8f, 1f, 0.8f),
		};
	}
}
