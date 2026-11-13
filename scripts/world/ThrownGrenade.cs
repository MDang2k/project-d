using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// A hand grenade in flight (throwables spec s2): a small rigid body that bounces, rolls and, when its fuse is out,
/// goes off through <see cref="GrenadeDetonation"/>. Clinks on its first landing so anyone near can hear where it is.
/// The model is the item's glb when present, else a green ball.
/// </summary>
public partial class ThrownGrenade : RigidBody3D
{
	/// <summary>Every grenade in flight or lying armed, for the AI that watches for them.</summary>
	public static readonly System.Collections.Generic.List<ThrownGrenade> Live = new();

	private GrenadeSpec _spec = null!;
	private float _fuseLeft;
	private Rid _thrower;
	private bool _clinked;
	private bool _detonated;

	/// <summary>Builds a grenade at a point with a velocity and the fuse time left. Add it to the tree afterwards.</summary>
	public static ThrownGrenade Create(GrenadeSpec spec, float fuseLeftS, Rid thrower, Vector3 velocity, CollisionObject3D? ignore)
	{
		var g = new ThrownGrenade { _spec = spec, _fuseLeft = fuseLeftS, _thrower = thrower, LinearVelocity = velocity, Name = "Grenade" };
		g.Mass = 0.4f;
		g.ContinuousCd = true;
		g.ContactMonitor = true;
		g.MaxContactsReported = 2;
		g.PhysicsMaterialOverride = new PhysicsMaterial { Bounce = 0.35f, Friction = 0.8f };
		g.AngularVelocity = new Vector3(8f, 3f, 5f);
		g.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.05f } });
		if (ignore is not null)
			g.AddCollisionExceptionWith(ignore); // not off the thrower's own body at the moment of release
		g.AddChild(GrenadeModel.Build(spec.Id));
		return g;
	}

	/// <summary>It has landed and clinked (AI within 8 m hear that).</summary>
	public bool Clinked => _clinked;

	public Rid ThrowerRid => _thrower;

	/// <summary>Frags and concussions hurt; smoke and bottles do not.</summary>
	public bool IsDangerous => _spec.Kind is GrenadeKind.Frag or GrenadeKind.Concussion;

	public override void _ExitTree() => Live.Remove(this);

	public override void _Ready()
	{
		Live.Add(this);
		BodyEntered += OnBodyEntered;
		// the thrower's body counts again after a moment, so a bounce off a wall can come back at them
		var timer = GetTree().CreateTimer(0.4);
		timer.Timeout += () =>
		{
			foreach (var ex in GetCollisionExceptions().ToArray())
				RemoveCollisionExceptionWith(ex);
		};
	}

	private void OnBodyEntered(Node body)
	{
		if (_spec.ImpactTriggered() && _spec.Kind != GrenadeKind.Distraction && !_detonated)
		{
			_detonated = true; // molotov and flare go off where they land
			GrenadeDetonation.Detonate(this, _spec, GlobalPosition, _thrower, GetRid());
			QueueFree();
			return;
		}
		if (_spec.Kind == GrenadeKind.Distraction && !_detonated)
		{
			_detonated = true;
			NoiseBus.Emit(GlobalPosition, _spec.ImpactNoiseM, "distraction", this);
			Sfx.Play3D(this, _spec.ImpactNoiseM >= 20f ? "glass_break" : "rock_hit", GlobalPosition);
			GD.Print($"DISTRACTION {_spec.Id} landed at {GlobalPosition.X:0},{GlobalPosition.Z:0}, noise {_spec.ImpactNoiseM:0} m");
			QueueFree();
			return;
		}
		if (_clinked)
			return;
		_clinked = true;
		NoiseBus.Emit(GlobalPosition, _spec.ClinkNoiseM, "grenade_clink", this);
		Sfx.Play3D(this, "grenade_clink", GlobalPosition);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_detonated)
			return;
		_fuseLeft -= (float)delta;
		if (_fuseLeft > 0f)
			return;
		_detonated = true;
		if (!_spec.ImpactTriggered()) // an impact item that never hit anything just disappears
			GrenadeDetonation.Detonate(this, _spec, GlobalPosition, _thrower, GetRid());
		QueueFree();
	}
}

/// <summary>The little model of a grenade: the item's glb scaled to hand size, or a ball when there is none.</summary>
public static class GrenadeModel
{
	public static Node3D Build(string itemId)
	{
		var path = $"res://assets/models/items/{itemId}.glb";
		if (ResourceLoader.Exists(path) && GD.Load<PackedScene>(path) is { } scene && scene.Instantiate() is Node3D model)
			return model;
		return new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.32f, 0.2f) },
		};
	}
}
