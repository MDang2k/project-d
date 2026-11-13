using System.Linq;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>
/// A frag grenade going off (throwables spec s3): fragments as short projectiles in a sphere through the projectile
/// system (armor, cover, stance and distance work as for bullets), plus the blast core on everyone with a clear line
/// (the thrower too). Makes the bang, the light and the noise AI hear. Thin node code over the pure
/// <see cref="FragmentBurst"/> and <see cref="BlastModel"/>.
/// </summary>
public static class GrenadeDetonation
{
	private const float FragmentSpeed = 450f;

	public static void Detonate(Node context, GrenadeSpec spec, Vector3 position, Rid thrower, Rid? ignoreBody = null)
	{
		switch (spec.Kind)
		{
			case GrenadeKind.Smoke or GrenadeKind.Gas:
				SmokeCloud.Spawn(context, spec, position);
				return;
			case GrenadeKind.Molotov or GrenadeKind.Thermite:
				FirePatch.Spawn(context, spec, position);
				return;
			case GrenadeKind.Flare:
				FlareLight.Spawn(context, spec, position);
				return;
			case GrenadeKind.Stun:
				Stun(context, spec, position, 1f);
				return;
		}
		var tree = context.GetTree();
		var projectiles = tree.GetFirstNodeInGroup("projectiles") as ProjectileManager;
		var rng = new System.Random();

		// fragments: leave the burst through the projectile system; the grenade's own body never blocks them
		if (projectiles is not null)
		{
			var exclude = new Array<Rid>();
			if (ignoreBody is { } own)
				exclude.Add(own);
			var ammo = new AmmoType("grenade_fragment", spec.FragmentDamage, spec.FragmentTier, FragmentSpeed);
			foreach (var dir in FragmentBurst.Directions(spec.Fragments, rng))
				projectiles.SpawnFragment(ammo, position + dir * 0.12f, dir, thrower, exclude, spec.FragmentRangeM, spec.FragmentMinScale);
			if (spec.Fragments > 0)
				projectiles.ReportBurst(position, spec.FragmentRangeM * 0.5f, thrower, ignoreBody);
		}

		// blast core: everyone in the core with a clear line from the burst to any of their body points
		var space = context.GetViewport().World3D.DirectSpaceState;
		foreach (var node in tree.GetNodesInGroup("perceivables").OfType<Node3D>().ToList())
		{
			if (node is not IPerceivable { IsAlive: true } target)
				continue;
			var health = node switch { Player p => p.Health, AiHuman a => a.Health, _ => null };
			if (health is null)
				continue;
			var distance = position.DistanceTo(node.GlobalPosition + Vector3.Up * 1f);
			if (distance >= spec.BlastCoreM)
				continue;
			var clear = target.BodyPoints().Any(point =>
			{
				var q = PhysicsRayQueryParameters3D.Create(position, point, 1u, ignoreBody is { } r ? new Array<Rid> { r } : new Array<Rid>());
				return space.IntersectRay(q).Count == 0; // layer 1 is the world: walls block, bodies do not
			});
			var hits = BlastModel.Hits(distance, clear, spec);
			if (hits.Count == 0)
				continue;
			var wasAlive = !health.IsDead;
			health.ReceiveExplosion(hits);
			projectiles?.ReportExplosionHit(health, wasAlive, thrower);
		}

		if (spec.Kind == GrenadeKind.Concussion)
			Stun(context, spec, position, 0.5f); // the concussion's stun, at half strength
		NoiseBus.Emit(position, spec.NoiseRadiusM, "explosion", context);
		Sfx.Play3D(context, "grenade_blast", position);
		Flash(context, position);
		GD.Print($"GRENADE {spec.Id} detonated at {position.X:0},{position.Y:0},{position.Z:0}");
	}

	/// <summary>
	/// The flashbang (throwables spec s4): everyone within the radius with a clear line to the burst is blinded and deafened,
	/// by distance and by whether they face it. The player gets the white-out and the ringing; AI the blinded and deafened states.
	/// </summary>
	public static void Stun(Node context, GrenadeSpec spec, Vector3 position, float scale)
	{
		var tree = context.GetTree();
		var space = context.GetViewport().World3D.DirectSpaceState;
		foreach (var node in tree.GetNodesInGroup("perceivables").OfType<Node3D>().ToList())
		{
			if (node is not IPerceivable { IsAlive: true } target)
				continue;
			var distance = position.DistanceTo(node.GlobalPosition + Vector3.Up * 1.4f);
			if (distance >= spec.CloudRadiusM)
				continue;
			var clear = target.BodyPoints().Any(p => space.IntersectRay(PhysicsRayQueryParameters3D.Create(position, p, 1u)).Count == 0);
			if (!clear)
				continue;
			var forward = node switch { Player p => p.ViewForward, _ => -node.GlobalBasis.Z };
			var facing = forward.Dot((position - node.GlobalPosition).Normalized()) > 0f;
			var strength = StunModel.Strength(distance, spec.CloudRadiusM, facing, scale);
			var blind = StunModel.BlindS(strength);
			var deaf = StunModel.DeafnessS(strength);
			switch (node)
			{
				case Player player:
					player.ApplyStun(strength, blind, deaf);
					break;
				case AiHuman ai:
					ai.Stun(blind, deaf);
					break;
			}
		}
		if (scale >= 1f)
		{
			NoiseBus.Emit(position, spec.NoiseRadiusM, "explosion", context);
			Sfx.Play3D(context, "stun_bang", position);
			Flash(context, position);
		}
	}

	/// <summary>A brief orange flash and a fireball that swells and fades: placeholder effect.</summary>
	private static void Flash(Node context, Vector3 position)
	{
		var holder = new Node3D { Position = position };
		var light = new OmniLight3D { LightColor = new Color(1f, 0.7f, 0.35f), LightEnergy = 8f, OmniRange = 14f };
		var ball = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.6f, Height = 1.2f, RadialSegments = 8, Rings = 4 },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				AlbedoColor = new Color(1f, 0.6f, 0.25f, 0.85f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			},
		};
		holder.AddChild(light);
		holder.AddChild(ball);
		context.GetTree().CurrentScene.AddChild(holder);
		var tween = holder.CreateTween().SetParallel();
		tween.TweenProperty(ball, "scale", Vector3.One * 3.2f, 0.35);
		tween.TweenProperty(light, "light_energy", 0f, 0.35);
		tween.TweenProperty(ball.MaterialOverride, "albedo_color:a", 0f, 0.35);
		tween.Chain().TweenCallback(Callable.From(holder.QueueFree));
	}
}
