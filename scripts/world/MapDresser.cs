using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Look pass for graybox maps (Border Strip polish step 2): textures the plain boxes with cheap triplanar noise, builds
/// trees, pylons, vehicles and wrecks out of a few primitives on top of their collision boxes, scatters bushes inside
/// the foliage volumes (one MultiMesh, one draw call) and hides the blockout volumes (foliage, quest zones, transits)
/// unless the debug menu asks for them. Visual only: collision is never touched. Placeholder art, no assets.
/// </summary>
public static class MapDresser
{
	private const string VolumeGroup = "dresser_volumes";

	public static void Dress(Node mapRoot)
	{
		if (mapRoot.GetNodeOrNull("Graybox/Geometry") is not { } geometry)
			return;
		var rng = new RandomNumberGenerator { Seed = 7 };
		Retexture(geometry);
		foreach (var node in geometry.GetNodeOrNull("Props")?.GetChildren().OfType<StaticBody3D>() ?? Enumerable.Empty<StaticBody3D>())
			DressProp(node, rng);
		ScatterBushes(mapRoot, rng);
		HideVolumes(mapRoot);
	}

	// ---- materials

	private static NoiseTexture2D Noise(float frequency, int seed)
		=> new()
		{
			Width = 256,
			Height = 256,
			Seamless = true,
			Noise = new FastNoiseLite { Frequency = frequency, Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex },
			ColorRamp = new Gradient { Colors = new[] { new Color(0.62f, 0.62f, 0.62f), new Color(1f, 1f, 1f) } },
		};

	private static StandardMaterial3D Textured(Color tint, NoiseTexture2D tex, float uvScale, float roughness = 1f)
		=> new()
		{
			AlbedoColor = tint,
			AlbedoTexture = tex,
			Uv1Triplanar = true,
			Uv1Scale = new Vector3(uvScale, uvScale, uvScale),
			Roughness = roughness,
			MetallicSpecular = 0.1f,
		};

	private static StandardMaterial3D FenceMaterial()
	{
		var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
		img.Fill(new Color(0f, 0f, 0f, 0f));
		for (var i = 0; i < 16; i++)
		{
			img.SetPixel(i, 0, Colors.White);
			img.SetPixel(0, i, Colors.White);
			img.SetPixel(i, i, new Color(1f, 1f, 1f, 0.8f));
		}
		return new StandardMaterial3D
		{
			AlbedoColor = new Color(0.42f, 0.43f, 0.45f),
			AlbedoTexture = ImageTexture.CreateFromImage(img),
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			Uv1Triplanar = true,
			Uv1Scale = new Vector3(0.9f, 0.9f, 0.9f),
			Roughness = 0.8f,
		};
	}

	private static void Retexture(Node geometry)
	{
		var coarse = Noise(0.02f, 11);
		var fine = Noise(0.12f, 23);
		var materials = new Dictionary<string, StandardMaterial3D>
		{
			["ground"] = Textured(new Color(0.34f, 0.37f, 0.25f), coarse, 0.07f),
			["cutfloor"] = Textured(new Color(0.27f, 0.23f, 0.18f), coarse, 0.1f),
			["mound"] = Textured(new Color(0.45f, 0.4f, 0.28f), coarse, 0.09f),
			["wall"] = Textured(new Color(0.58f, 0.57f, 0.54f), fine, 0.35f),
			["slab"] = Textured(new Color(0.48f, 0.48f, 0.46f), fine, 0.3f),
			["stair"] = Textured(new Color(0.52f, 0.51f, 0.48f), fine, 0.3f),
			["roof"] = Textured(new Color(0.3f, 0.3f, 0.32f), fine, 0.4f, 0.8f),
			["cover"] = Textured(new Color(0.47f, 0.42f, 0.3f), fine, 0.5f),
			["vehicle"] = Textured(new Color(0.3f, 0.35f, 0.26f), fine, 0.45f, 0.7f),
			["prop"] = Textured(new Color(0.52f, 0.43f, 0.32f), fine, 0.6f),
			["fence"] = FenceMaterial(),
		};
		var done = new HashSet<BoxMesh>();
		foreach (var mesh in geometry.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
		{
			if (mesh.Mesh is not BoxMesh box || !done.Add(box) || box.Material is not { ResourceName: { Length: > 0 } key })
				continue;
			if (materials.TryGetValue(key, out var dressed))
				box.Material = dressed;
		}
	}

	// ---- props

	private static StandardMaterial3D Flat(Color c, float roughness = 0.9f) => new() { AlbedoColor = c, Roughness = roughness };

	private static MeshInstance3D Piece(Node3D parent, Mesh mesh, Material mat, Vector3 at, Vector3? rotDeg = null)
	{
		var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = at };
		if (rotDeg is { } r)
			m.RotationDegrees = r;
		parent.AddChild(m);
		return m;
	}

	private static Vector3 BoxSize(StaticBody3D body)
		=> body.GetNodeOrNull<CollisionShape3D>("Shape")?.Shape is BoxShape3D box ? box.Size : Vector3.One;

	private static void HideBox(StaticBody3D body)
	{
		if (body.GetNodeOrNull<MeshInstance3D>("Mesh") is { } mesh)
			mesh.Visible = false;
	}

	private static void DressProp(StaticBody3D body, RandomNumberGenerator rng)
	{
		var name = body.Name.ToString();
		var size = BoxSize(body);
		if (name.StartsWith("tree"))
			Tree(body, size, rng);
		else if (name.StartsWith("pylon") || name == "relay_mast")
			Mast(body, size, name == "relay_mast");
		else if (name.StartsWith("queue") || name.StartsWith("boxcar"))
			Vehicle(body, size, burnt: false);
		else if (name.StartsWith("convoy"))
			Vehicle(body, size, burnt: true);
		else if (name == "heli_fuselage")
			Helicopter(body, size);
	}

	private static void Tree(StaticBody3D body, Vector3 size, RandomNumberGenerator rng)
	{
		HideBox(body);
		var h = size.Y;
		var bark = Flat(new Color(0.28f, 0.2f, 0.14f));
		var leaf = Flat(new Color(0.14f + rng.Randf() * 0.06f, 0.3f + rng.Randf() * 0.1f, 0.13f));
		Piece(body, new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.26f, Height = h * 0.45f, RadialSegments = 6, Rings = 1 }, bark, new Vector3(0f, -h / 2f + h * 0.225f, 0f));
		for (var i = 0; i < 3; i++)
		{
			var radius = 1.7f - i * 0.45f;
			var cone = new CylinderMesh { TopRadius = 0f, BottomRadius = radius, Height = h * 0.38f, RadialSegments = 7, Rings = 1 };
			Piece(body, cone, leaf, new Vector3(0f, -h / 2f + h * (0.32f + i * 0.2f), 0f), new Vector3(0f, rng.Randf() * 90f, 0f));
		}
	}

	private static void Mast(StaticBody3D body, Vector3 size, bool relay)
	{
		HideBox(body);
		var steel = Flat(new Color(0.36f, 0.38f, 0.4f), 0.6f);
		var h = size.Y;
		// a tapering lattice-ish mast: a thick base section, a thin upper one, cross arms and a cap
		Piece(body, new BoxMesh { Size = new Vector3(0.55f, h * 0.6f, 0.55f) }, steel, new Vector3(0f, -h / 2f + h * 0.3f, 0f));
		Piece(body, new BoxMesh { Size = new Vector3(0.3f, h * 0.45f, 0.3f) }, steel, new Vector3(0f, -h / 2f + h * 0.75f, 0f));
		foreach (var at in new[] { 0.82f, 0.68f })
			Piece(body, new BoxMesh { Size = new Vector3(relay ? 2.2f : 4.2f, 0.18f, 0.18f) }, steel, new Vector3(0f, -h / 2f + h * at, 0f));
		if (!relay)
			return;
		var lamp = new OmniLight3D { LightColor = new Color(1f, 0.2f, 0.15f), OmniRange = 12f, LightEnergy = 0.2f, Position = new Vector3(0f, h / 2f + 0.3f, 0f) };
		body.AddChild(lamp);
		body.AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.15f, Height = 0.3f },
			Position = lamp.Position,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.25f, 0.2f) },
		});
		var tween = lamp.CreateTween().SetLoops();
		tween.TweenProperty(lamp, "light_energy", 3f, 0.15);
		tween.TweenProperty(lamp, "light_energy", 0.1f, 0.9);
	}

	private static void Vehicle(StaticBody3D body, Vector3 size, bool burnt)
	{
		var bodyColor = burnt ? new Color(0.12f, 0.12f, 0.11f) : new Color(0.3f, 0.35f, 0.26f);
		if (body.GetNodeOrNull<MeshInstance3D>("Mesh") is { } mesh)
			mesh.MaterialOverride = Flat(bodyColor, 0.8f);
		var dark = Flat(new Color(0.08f, 0.08f, 0.08f));
		var glass = Flat(burnt ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.15f, 0.2f, 0.22f), 0.3f);
		// wheels along the length, a cab above the front end
		var wheelR = Mathf.Clamp(size.Y * 0.22f, 0.25f, 0.6f);
		foreach (var x in new[] { -0.32f, 0.3f })
		{
			foreach (var z in new[] { -1f, 1f })
				Piece(body, new CylinderMesh { TopRadius = wheelR, BottomRadius = wheelR, Height = 0.3f, RadialSegments = 8, Rings = 1 }, dark,
					new Vector3(size.X * x, -size.Y / 2f + wheelR * 0.8f, z * (size.Z / 2f)), new Vector3(90f, 0f, 0f));
		}
		if (size.X > 3.5f)
			Piece(body, new BoxMesh { Size = new Vector3(size.X * 0.28f, size.Y * 0.5f, size.Z * 0.92f) }, Flat(bodyColor.Darkened(0.1f)),
				new Vector3(size.X * 0.36f, size.Y / 2f + size.Y * 0.25f, 0f));
		Piece(body, new BoxMesh { Size = new Vector3(size.X * 0.2f, size.Y * 0.22f, size.Z * 0.94f) }, glass, new Vector3(size.X * 0.4f, size.Y / 2f + size.Y * 0.3f, 0f));
	}

	private static void Helicopter(StaticBody3D body, Vector3 size)
	{
		var dark = Flat(new Color(0.12f, 0.13f, 0.12f), 0.8f);
		if (body.GetNodeOrNull<MeshInstance3D>("Mesh") is { } mesh)
			mesh.MaterialOverride = dark;
		Piece(body, new BoxMesh { Size = new Vector3(size.X * 0.45f, 0.25f, 0.3f) }, dark, new Vector3(size.X * 0.2f, size.Y / 2f + 0.9f, 0f), new Vector3(0f, 25f, 12f)); // bent rotor blade
		Piece(body, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 1.0f, RadialSegments = 6, Rings = 1 }, dark, new Vector3(0f, size.Y / 2f + 0.5f, 0f));
	}

	// ---- bushes and hidden volumes

	private static void ScatterBushes(Node root, RandomNumberGenerator rng)
	{
		var volumes = root.FindChildren("*", "StaticBody3D", true, false).OfType<FoliageVolume>().ToList();
		if (volumes.Count == 0)
			return;
		var transforms = new List<(Transform3D T, Color C)>();
		foreach (var v in volumes)
		{
			if (v.GetNodeOrNull<CollisionShape3D>("Shape")?.Shape is not BoxShape3D box)
				continue;
			var count = Mathf.Clamp((int)(box.Size.X * box.Size.Z * 0.18f), 4, 30);
			var floorY = v.GlobalPosition.Y - box.Size.Y / 2f;
			for (var i = 0; i < count; i++)
			{
				var at = v.GlobalPosition + new Vector3((rng.Randf() - 0.5f) * box.Size.X, 0f, (rng.Randf() - 0.5f) * box.Size.Z);
				at.Y = floorY;
				var s = 0.55f + rng.Randf() * 0.6f;
				var basis = Basis.FromEuler(new Vector3(0f, rng.Randf() * Mathf.Tau, 0f)).Scaled(new Vector3(s, s * (0.9f + rng.Randf() * 0.6f), s));
				transforms.Add((new Transform3D(basis, at), new Color(0.13f + rng.Randf() * 0.07f, 0.25f + rng.Randf() * 0.1f, 0.1f)));
			}
		}
		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = new SphereMesh { Radius = 0.55f, Height = 0.8f, RadialSegments = 6, Rings = 3 },
			InstanceCount = transforms.Count,
		};
		for (var i = 0; i < transforms.Count; i++)
		{
			var (t, c) = transforms[i];
			mm.SetInstanceTransform(i, t * new Transform3D(Basis.Identity, Vector3.Up * 0.3f));
			mm.SetInstanceColor(i, c);
		}
		root.AddChild(new MultiMeshInstance3D
		{
			Name = "Bushes",
			Multimesh = mm,
			MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	/// <summary>Blockout volumes (foliage, quest zones, transits) are invisible in play; the debug menu can show them.</summary>
	private static void HideVolumes(Node root)
	{
		foreach (var n in VolumeNodes(root))
		{
			n.AddToGroup(VolumeGroup);
			foreach (var mesh in n.GetChildren().OfType<MeshInstance3D>().Where(m => m.Mesh is BoxMesh))
				mesh.Visible = false;
		}
	}

	private static IEnumerable<Node> VolumeNodes(Node root)
		=> root.FindChildren("*", "Node3D", true, false).Where(n => n is FoliageVolume or QuestZone or TransitZone);

	/// <summary>Debug menu switch: shows or hides the blockout volumes again.</summary>
	public static void SetVolumesVisible(SceneTree tree, bool on)
	{
		foreach (var n in tree.GetNodesInGroup(VolumeGroup).OfType<Node3D>())
		{
			foreach (var mesh in n.GetChildren().OfType<MeshInstance3D>().Where(m => m.Mesh is BoxMesh))
				mesh.Visible = on;
		}
	}

	// ---- sky, light, fog

	/// <summary>Overcast cold-war sky, soft sun with short shadows, light fog that also hides the edge of the map, and a far ground plane.</summary>
	public static void AddEnvironment(Node parent)
	{
		var fog = new Color(0.66f, 0.7f, 0.74f);
		var sky = new Sky
		{
			SkyMaterial = new ProceduralSkyMaterial
			{
				SkyTopColor = new Color(0.34f, 0.45f, 0.58f),
				SkyHorizonColor = fog,
				GroundHorizonColor = fog,
				GroundBottomColor = new Color(0.3f, 0.32f, 0.3f),
				SunAngleMax = 25f,
				SunCurve = 0.2f,
			},
		};
		parent.AddChild(new DirectionalLight3D
		{
			RotationDegrees = new Vector3(-42f, 35f, 0f),
			LightColor = new Color(1f, 0.93f, 0.82f),
			LightEnergy = 1.15f,
			ShadowEnabled = true,
			DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
			DirectionalShadowMaxDistance = 140f,
		});
		parent.AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Sky,
				Sky = sky,
				AmbientLightSource = Godot.Environment.AmbientSource.Sky,
				AmbientLightEnergy = 0.8f,
				ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
				TonemapMode = Godot.Environment.ToneMapper.Filmic,
				FogEnabled = true,
				FogLightColor = fog,
				FogDensity = 0.0018f,
				FogSkyAffect = 1f,
			},
		});
		// beyond the playable map: flat ground that the fog swallows, so the map does not end in a visible edge
		parent.AddChild(new MeshInstance3D
		{
			Name = "FarGround",
			Mesh = new PlaneMesh { Size = new Vector2(3000f, 3000f) },
			Position = new Vector3(0f, -0.1f, 0f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.33f, 0.24f), Roughness = 1f },
		});
	}
}
