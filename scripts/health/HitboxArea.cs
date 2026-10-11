using Godot;

namespace ProjectD;

/// <summary>
/// Hitbox for one body region (CLAUDE.md: separate shape per region, shared by player and AI).
/// Bullets raycast against areas; <see cref="ProjectileManager"/> forwards hits here.
/// </summary>
public partial class HitboxArea : Area3D
{
	public BodyPart Part { get; set; }
	public HealthComponent? Health { get; set; }

	public override void _Ready()
	{
		AddToGroup("hitboxes");
	}

	public HitResult? ReceiveBullet(AmmoType ammo) => Health?.ReceiveHit(Part, ammo);
}

/// <summary>Builds a standing humanoid hitbox set under a node (placeholder proportions).</summary>
public static class HitboxRig
{
	public static void Build(Node3D parent, HealthComponent health)
	{
		Add(parent, health, BodyPart.Head, new SphereShape3D { Radius = 0.12f }, new Vector3(0f, 1.65f, 0f));
		Add(parent, health, BodyPart.Thorax, new BoxShape3D { Size = new Vector3(0.38f, 0.36f, 0.22f) },
			new Vector3(0f, 1.30f, 0f));
		Add(parent, health, BodyPart.Stomach, new BoxShape3D { Size = new Vector3(0.34f, 0.26f, 0.2f) },
			new Vector3(0f, 1.02f, 0f));
		Add(parent, health, BodyPart.LeftArm, new CapsuleShape3D { Radius = 0.055f, Height = 0.62f },
			new Vector3(-0.27f, 1.28f, 0f));
		Add(parent, health, BodyPart.RightArm, new CapsuleShape3D { Radius = 0.055f, Height = 0.62f },
			new Vector3(0.27f, 1.28f, 0f));
		Add(parent, health, BodyPart.LeftLeg, new CapsuleShape3D { Radius = 0.085f, Height = 0.88f },
			new Vector3(-0.1f, 0.44f, 0f));
		Add(parent, health, BodyPart.RightLeg, new CapsuleShape3D { Radius = 0.085f, Height = 0.88f },
			new Vector3(0.1f, 0.44f, 0f));
	}

	/// <summary>
	/// Lowers the hitboxes toward the ground for crouched or prone bodies. Only positions move (Jolt does
	/// not support non-uniform scale on spheres and capsules), so the shapes keep their size.
	/// </summary>
	public static void SetHeightScale(Node3D parent, float scale)
	{
		foreach (var child in parent.GetChildren())
		{
			if (child is not HitboxArea area)
				continue;
			var baseY = area.GetMeta("base_y").AsSingle();
			area.Position = new Vector3(area.Position.X, baseY * scale, area.Position.Z);
		}
	}

	private static void Add(Node3D parent, HealthComponent health, BodyPart part, Shape3D shape, Vector3 position)
	{
		var area = new HitboxArea { Name = $"Hitbox{part}", Part = part, Health = health, Position = position };
		area.SetMeta("base_y", position.Y);
		area.AddChild(new CollisionShape3D { Shape = shape });
		parent.AddChild(area);
	}
}
