using Godot;

namespace ProjectD;

/// <summary>
/// The puff of dust a body kicks up when it drops to the ground or pushes up from it (animation spec s1.6). One-shot
/// CPU particles with primitive meshes (no art needed). Hard surfaces (a collider with the metadata surface set to
/// concrete, metal, wood or tile) kick up nothing; anything else is dirt.
/// </summary>
public static class GroundDust
{
	private static readonly string[] Hard = { "concrete", "metal", "wood", "tile", "hard" };

	public static bool KicksUp(string? surface) => surface is null || System.Array.IndexOf(Hard, surface.ToLowerInvariant()) < 0;

	/// <summary>The surface tag under a point (collider metadata "surface"), or null when untagged.</summary>
	public static string? SurfaceAt(Node3D from, Vector3 position)
	{
		var query = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * 0.3f, position + Vector3.Down * 0.6f, 1u);
		var hit = from.GetWorld3D().DirectSpaceState.IntersectRay(query);
		return hit.Count > 0 && hit["collider"].AsGodotObject() is Node n && n.HasMeta("surface") ? n.GetMeta("surface").AsString() : null;
	}

	/// <summary>Puffs dust at the feet; <paramref name="strength"/> 1 for a body dropping prone, less for a get-up.</summary>
	public static void Puff(Node3D from, Vector3 position, float strength = 1f)
	{
		if (!KicksUp(SurfaceAt(from, position)) || from.GetTree()?.CurrentScene is not { } scene)
			return;
		var gradient = new Gradient();
		gradient.SetColor(0, new Color(0.62f, 0.55f, 0.42f, 0.55f));
		gradient.SetColor(1, new Color(0.62f, 0.55f, 0.42f, 0f));
		var puff = new CpuParticles3D
		{
			Emitting = true,
			OneShot = true,
			Explosiveness = 0.9f,
			Amount = (int)(26 * strength),
			Lifetime = 0.8f,
			Direction = Vector3.Up,
			Spread = 70f,
			InitialVelocityMin = 0.6f,
			InitialVelocityMax = 1.8f * strength,
			Gravity = new Vector3(0f, -0.4f, 0f),
			ScaleAmountMin = 0.6f,
			ScaleAmountMax = 1.4f,
			ColorRamp = gradient,
			Mesh = new SphereMesh { Radius = 0.1f, Height = 0.2f, RadialSegments = 6, Rings = 3,
				Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		scene.AddChild(puff);
		puff.GlobalPosition = position + Vector3.Up * 0.05f;
		from.GetTree().CreateTimer(1.4).Timeout += puff.QueueFree;
	}
}
