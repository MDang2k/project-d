using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace ProjectD;

public readonly record struct BulletImpact(Vector3 Position, Vector3 Normal, GodotObject Collider,
	AmmoType Ammo, Vector3 Velocity, float TravelDistance, HitResult? Hit = null, Rid Shooter = default, bool Fragment = false);

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
		public Array<Rid> Exclude = new();
		public Rid Shooter;
		public Vector3 Origin;
		public MeshInstance3D? Tracer;
		public List<Vector3> Path = new();
		/// <summary>Grenade fragment: no tracer, no near-miss event, a range limit and damage that falls off with distance.</summary>
		public bool Fragment;
		public float MaxRangeM;
		public float MinScale = 1f;
	}

	private readonly List<Bullet> _bullets = new();
	private float _gravity = 9.8f;

	private const float PreviewStepS = 0.02f;
	private const float TrailSeconds = 8f;

	[Export] public bool ShowTracers { get; set; } = true;
	/// <summary>Debug: keep each bullet flight path as a line for a few seconds. Off by default.</summary>
	[Export] public bool ShowTrajectories { get; set; }
	/// <summary>Everything but the AI movement capsules (layer 4): bullets must hit the per-limb hitboxes instead.</summary>
	[Export(PropertyHint.Layers3DPhysics)] public uint HitMask { get; set; } = 0xFFFFFFFF & ~(1u << 3);

	public event Action<BulletImpact>? Impact;

	/// <summary>A bullet landed on a living body: victim health, whether it died of it, shooter body Rid (friendly fire uses it).</summary>
	public event Action<HealthComponent, bool, Rid>? Hit;

	/// <summary>A bullet dealt the killing damage to a body (victim health, shooter body Rid); quests use it.</summary>
	public event Action<HealthComponent, Rid>? Killed;

	/// <summary>A bullet travelled from one point to another this frame (AI uses it to feel near misses).</summary>
	public event Action<Vector3, Vector3, Rid>? Passed;

	public override void _Ready()
	{
		AddToGroup("projectiles");
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsSingle();
	}

	public void Spawn(AmmoType ammo, Vector3 position, Vector3 direction, Rid shooter)
		=> Spawn(ammo, position, direction, shooter, new Array<Rid> { shooter });

	/// <summary>Spawns a bullet that ignores every collider in exclude (the shooter's body and own hitboxes).</summary>
	public void Spawn(AmmoType ammo, Vector3 position, Vector3 direction, Rid shooter, Array<Rid> exclude)
	{
		var b = new Bullet
		{
			State = new BulletState { Position = position, Velocity = direction.Normalized() * ammo.MuzzleVelocity },
			Ammo = ammo,
			Shooter = shooter,
			Exclude = exclude,
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

	/// <summary>
	/// Spawns a grenade fragment (throwables spec s3): a short-lived projectile that hits like a bullet but loses damage with
	/// distance, down to <paramref name="minScale"/> at <paramref name="maxRangeM"/>, and vanishes beyond it.
	/// </summary>
	public void SpawnFragment(AmmoType ammo, Vector3 position, Vector3 direction, Rid shooter, Array<Rid> exclude, float maxRangeM, float minScale)
	{
		_bullets.Add(new Bullet
		{
			State = new BulletState { Position = position, Velocity = direction.Normalized() * ammo.MuzzleVelocity },
			Ammo = ammo,
			Shooter = shooter,
			Exclude = exclude,
			Origin = position,
			Fragment = true,
			MaxRangeM = maxRangeM,
			MinScale = minScale,
		});
	}

	/// <summary>
	/// A fragment burst counts as one near miss for everyone within <paramref name="radiusM"/> with a clear line to it
	/// (throwables spec s3): fragments do not raise <see cref="Passed"/> one by one, so a bot gets one suppression and
	/// under-fire trigger per burst and the player hears one whiz. Raised as a segment from the burst to their chest.
	/// </summary>
	public void ReportBurst(Vector3 position, float radiusM, Rid shooter, Rid? ignoreBody)
	{
		var space = GetWorld3D().DirectSpaceState;
		var exclude = ignoreBody is { } own ? new Array<Rid> { own } : new Array<Rid>();
		foreach (var node in GetTree().GetNodesInGroup("perceivables").OfType<Node3D>().ToList())
		{
			if (node is not IPerceivable { IsAlive: true })
				continue;
			var chest = node.GlobalPosition + Vector3.Up * 1.2f;
			if (chest.DistanceTo(position) > radiusM)
				continue;
			var q = PhysicsRayQueryParameters3D.Create(position, chest, 1u, exclude); // layer 1 is the world: walls shield
			if (space.IntersectRay(q).Count == 0)
				Passed?.Invoke(position, chest, shooter);
		}
	}

	/// <summary>A blast hurt a living body: raises the same Hit and Killed events a bullet would, so friendly fire, nemesis and quests see it.</summary>
	public void ReportExplosionHit(HealthComponent victim, bool wasAlive, Rid thrower)
	{
		if (!wasAlive)
			return;
		Hit?.Invoke(victim, victim.IsDead, thrower);
		if (victim.IsDead)
			Killed?.Invoke(victim, thrower);
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
			var q = PhysicsRayQueryParameters3D.Create(from, to, HitMask, b.Exclude);
			q.CollideWithAreas = true;
			var hit = space.IntersectRay(q);
			if (!b.Fragment)
				Passed?.Invoke(from, hit.Count > 0 ? hit["position"].AsVector3() : to, b.Shooter);
			if (b.Fragment && b.Origin.DistanceTo(to) > b.MaxRangeM)
			{
				Remove(i);
				continue;
			}
			if (hit.Count > 0)
			{
				var pos = hit["position"].AsVector3();
				b.Path[^1] = pos;
				var collider = hit["collider"].AsGodotObject();
				var area = collider as HitboxArea;
				var wasAlive = area?.Health is { IsDead: false };
				var ammoHere = b.Fragment
					? b.Ammo with { Damage = b.Ammo.Damage * FragmentBurst.DamageScale(b.Origin.DistanceTo(pos), b.MaxRangeM, b.MinScale) }
					: b.Ammo;
				var result = area?.ReceiveBullet(ammoHere);
				if (wasAlive && area?.Health is { } struck)
					Hit?.Invoke(struck, struck.IsDead, b.Shooter);
				if (wasAlive && area?.Health is { IsDead: true } victim)
					Killed?.Invoke(victim, b.Shooter);
				Impact?.Invoke(new BulletImpact(pos, hit["normal"].AsVector3(), collider,
					b.Ammo, b.State.Velocity, b.Origin.DistanceTo(pos), result, b.Shooter, b.Fragment));
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
