using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace ProjectD;

public readonly record struct BulletImpact(Vector3 Position, Vector3 Normal, GodotObject Collider,
	AmmoType Ammo, Vector3 Velocity, float TravelDistance, HitResult? Hit = null);

/// <summary>
/// Steps every in-flight bullet each physics frame and raycasts the segment it travelled
/// (GDD s8, CLAUDE.md: simulated projectiles, not RigidBody, not pure hitscan).
/// </summary>
public partial class ProjectileManager : Node3D
{
	private const float MaxLifetimeS = 4f;

	private sealed class Bullet
	{
		public BulletState State;
		public AmmoType Ammo = null!;
		public Rid Shooter;
		public Vector3 Origin;
		public MeshInstance3D? Tracer;
		public List<Vector3> Path = new();
	}

	private readonly List<Bullet> _bullets = new();
	private float _gravity = 9.8f;

	private const float PreviewStepS = 0.02f;
	private const float TrailSeconds = 8f;

	[Export] public bool ShowTracers { get; set; } = true;
	/// <summary>Debug: keep each bullet flight path as a line for a few seconds. Off by default.</summary>
	[Export] public bool ShowTrajectories { get; set; }
	[Export(PropertyHint.Layers3DPhysics)] public uint HitMask { get; set; } = 0xFFFFFFFF;

	public event Action<BulletImpact>? Impact;

	public override void _Ready()
	{
		AddToGroup("projectiles");
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsSingle();
	}

	public void Spawn(AmmoType ammo, Vector3 position, Vector3 direction, Rid shooter)
	{
		var b = new Bullet
		{
			State = new BulletState { Position = position, Velocity = direction.Normalized() * ammo.MuzzleVelocity },
			Ammo = ammo,
			Shooter = shooter,
			Origin = position,
		};
		b.Path.Add(position);
		if (ShowTracers)
		{
			b.Tracer = new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = new Vector3(0.012f, 0.012f, 0.5f) },
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = new Color(1f, 0.85f, 0.3f),
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				},
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			AddChild(b.Tracer);
		}
		_bullets.Add(b);
	}

	public override void _PhysicsProcess(double deltaD)
	{
		var delta = (float)deltaD;
		var space = GetWorld3D().DirectSpaceState;
		for (var i = _bullets.Count - 1; i >= 0; i--)
		{
			var b = _bullets[i];
			var (from, to) = Ballistics.Step(ref b.State, _gravity, delta);
			b.Path.Add(to);
			var q = PhysicsRayQueryParameters3D.Create(from, to, HitMask, new Array<Rid> { b.Shooter });
			q.CollideWithAreas = true;
			var hit = space.IntersectRay(q);
			if (hit.Count > 0)
			{
				var pos = hit["position"].AsVector3();
				b.Path[^1] = pos;
				var collider = hit["collider"].AsGodotObject();
				var result = (collider as HitboxArea)?.ReceiveBullet(b.Ammo);
				Impact?.Invoke(new BulletImpact(pos, hit["normal"].AsVector3(), collider,
					b.Ammo, b.State.Velocity, b.Origin.DistanceTo(pos), result));
				Remove(i);
			}
			else if (b.State.Age > MaxLifetimeS)
			{
				Remove(i);
			}
			else if (b.Tracer is not null && b.State.Velocity.LengthSquared() > 0.01f)
			{
				b.Tracer.GlobalPosition = to;
				b.Tracer.LookAt(to + b.State.Velocity, Vector3.Up);
			}
		}
	}

	/// <summary>
	/// Predicted path of a shot from origin along direction, cut at the first collision.
	/// Debug aid; hitFound tells whether it ended on a surface.
	/// </summary>
	public List<Vector3> PredictPath(AmmoType ammo, Vector3 origin, Vector3 direction, Rid shooter, out bool hitFound)
	{
		var raw = Ballistics.Predict(origin, direction.Normalized() * ammo.MuzzleVelocity, _gravity,
			PreviewStepS, MaxLifetimeS);
		var space = GetWorld3D().DirectSpaceState;
		var path = new List<Vector3> { raw[0] };
		hitFound = false;
		for (var i = 1; i < raw.Count; i++)
		{
			var q = PhysicsRayQueryParameters3D.Create(raw[i - 1], raw[i], HitMask, new Array<Rid> { shooter });
			q.CollideWithAreas = true;
			var hit = space.IntersectRay(q);
			if (hit.Count > 0)
			{
				path.Add(hit["position"].AsVector3());
				hitFound = true;
				return path;
			}
			path.Add(raw[i]);
		}
		return path;
	}

	/// <summary>Draws a world-space polyline that frees itself after lifetimeS (0 = persistent).</summary>
	public MeshInstance3D DrawLine(IReadOnlyList<Vector3> points, Color color, float lifetimeS)
	{
		var mesh = new ImmediateMesh();
		var node = new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = color,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		FillLine(mesh, points);
		AddChild(node);
		if (lifetimeS > 0f)
			GetTree().CreateTimer(lifetimeS).Timeout += node.QueueFree;
		return node;
	}

	public static void FillLine(ImmediateMesh mesh, IReadOnlyList<Vector3> points)
	{
		mesh.ClearSurfaces();
		if (points.Count < 2)
			return;
		mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
		foreach (var p in points)
			mesh.SurfaceAddVertex(p);
		mesh.SurfaceEnd();
	}

	private void Remove(int index)
	{
		if (ShowTrajectories && _bullets[index].Path.Count > 1)
			DrawLine(_bullets[index].Path, new Color(0.2f, 0.9f, 1f), TrailSeconds);
		_bullets[index].Tracer?.QueueFree();
		_bullets.RemoveAt(index);
	}
}
