using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// The model and texture variants of one kind of AI body. The face textures are full copies of the body texture with
/// only the head repainted, the outfit textures recolour the outfit model, so any face goes with any outfit.
/// </summary>
public sealed record BodyModel(string OutfitModel, string[] Faces, string[] OutfitTextures);

/// <summary>
/// Builds the visible body of an AI from the character models: the shared body (with head) plus an outfit on one
/// skeleton and a random face and outfit texture. The result is an <see cref="AiBodyRig"/> that animates it.
/// </summary>
public static class AiBodyVisual
{
	private const string Models = "res://assets/models/characters/";
	private const string Textures = "res://assets/textures/characters/";

	public static readonly IReadOnlyDictionary<string, BodyModel> Kinds = new Dictionary<string, BodyModel>
	{
		["looter"] = new("outfit_looter",
			new[] { "face_looter_01", "face_looter_02", "face_looter_03", "face_looter_04", "face_looter_05" },
			new[] { "outfit_looter", "outfit_looter_02", "outfit_looter_03", "outfit_looter_04" }),
	};

	// Loaded once and kept: a finalized wrapper of a reloaded resource aborts Godot's script bridge in busy scenes.
	private static readonly Dictionary<string, Resource?> Cache = new();

	private static OutfitWardrobes? _wardrobes;
	private static OutfitWardrobes? Wardrobes => _wardrobes ??= LoadWardrobes();

	private static T? Load<T>(string path) where T : Resource
	{
		if (Cache.TryGetValue(path, out var cached))
			return cached as T;
		var loaded = ResourceLoader.Exists(path) ? GD.Load<T>(path) : null;
		Cache[path] = loaded;
		return loaded;
	}

	/// <summary>The body for a kind, or null when the kind or one of its files is missing (the AI keeps its capsule).</summary>
	public static AiBodyRig? Build(string kind, System.Random rng, out string description)
	{
		description = "";
		if (!Kinds.TryGetValue(kind, out var model))
			return null;
		var bodyScene = Load<PackedScene>(Models + "player_body.glb");
		var outfitScene = Load<PackedScene>(Models + model.OutfitModel + ".glb");
		if (bodyScene is null || outfitScene is null)
			return null;

		var face = model.Faces[rng.Next(model.Faces.Length)];
		var outfitTexture = model.OutfitTextures[rng.Next(model.OutfitTextures.Length)];
		var faceTex = Load<Texture2D>(Textures + face + ".png");
		var outfitTex = Load<Texture2D>(Textures + outfitTexture + ".png");

		var (root, skel) = NewRig(bodyScene);
		Wear(skel, outfitScene);
		foreach (var node in skel.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)node;
			var texture = mi.Name == "HeadMesh" || mi.Name == "Body" ? faceTex : outfitTex;
			Retexture(mi, texture);
		}
		SetSkeleton(root, skel);
		description = $"{face} / {outfitTexture}";
		return root;
	}

	/// <summary>
	/// The box body dressed from the actor's wardrobe, picked by its behaviour type. Null when the actor has no
	/// wardrobe or a file is missing (the caller then tries the older body, or keeps the capsule).
	/// </summary>
	public static AiBodyRig? BuildFromWardrobe(string actorId, string archetype, System.Random rng, out string description)
	{
		description = "";
		if (Wardrobes?.Roll(actorId, archetype, rng) is not { } outfit)
			return null;
		// the box body with bare hands (the Soviet file); gloves are pieces that hide them
		var bodyScene = Load<PackedScene>(Models + "boxes_body_soviet.glb");
		var pieces = outfit.Pieces.Select(p => Load<PackedScene>($"{Models}outfit_{p}.glb")).ToArray();
		if (bodyScene is null || pieces.Any(p => p is null))
			return null;

		var (root, skel) = NewRig(bodyScene);
		foreach (var piece in pieces)
			Wear(skel, piece!);
		foreach (var node in skel.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)node;
			var material = mi.Mesh.SurfaceGetMaterial(0);
			if (material is not null && Wardrobes.Textures.TryGetValue(material.ResourceName, out var texture))
				mi.SetSurfaceOverrideMaterial(0, PieceMaterial(texture));
			else
				Retexture(mi, null); // the body keeps its own texture, drawn crisp
			if (mi.Name.ToString().StartsWith("Body_Hand") && Wardrobes.HidesHands(outfit))
				mi.Visible = false;
		}
		SetSkeleton(root, skel);
		description = $"{outfit.Name} ({archetype})";
		return root;
	}

	private static (AiBodyRig Root, Skeleton3D Skeleton) NewRig(PackedScene bodyScene)
	{
		var root = new AiBodyRig { Name = "BodyVisual" };
		var body = bodyScene.Instantiate<Node3D>();
		root.AddChild(body);
		return (root, (Skeleton3D)body.FindChildren("*", "Skeleton3D", true, false)[0]);
	}

	/// <summary>Moves a model's skinned meshes onto the body's skeleton (they bind by bone name).</summary>
	private static void Wear(Skeleton3D skel, PackedScene scene)
	{
		var model = scene.Instantiate<Node3D>();
		var from = model.FindChildren("*", "Skeleton3D", true, false)[0];
		foreach (var child in from.GetChildren())
		{
			child.Owner = null;
			foreach (var sub in child.FindChildren("*", "", true, false))
				sub.Owner = null;
			from.RemoveChild(child);
			skel.AddChild(child);
		}
		model.Free();
	}

	private static void SetSkeleton(AiBodyRig root, Skeleton3D skel)
		=> root.SetSkeleton(skel,
			skel.FindChild("Palm_L", true, false) is Node3D pl ? pl.Transform : Transform3D.Identity,
			skel.FindChild("Palm_R", true, false) is Node3D pr ? pr.Transform : Transform3D.Identity);

	/// <summary>One material per shared piece texture, so bots in the same kit batch together.</summary>
	private static StandardMaterial3D PieceMaterial(string texture)
	{
		if (Cache.TryGetValue("material:" + texture, out var cached))
			return (StandardMaterial3D)cached!;
		var material = new StandardMaterial3D
		{
			AlbedoTexture = Load<Texture2D>(Textures + texture),
			TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
			Roughness = 0.9f,
		};
		Cache["material:" + texture] = material;
		return material;
	}

	private static OutfitWardrobes? LoadWardrobes()
	{
		using var file = FileAccess.Open("res://data/actors/wardrobes.json", FileAccess.ModeFlags.Read);
		return file is null ? null : OutfitWardrobes.Parse(file.GetAsText());
	}

	/// <summary>A private copy of each surface material with another albedo texture, kept pixel-crisp.</summary>
	private static void Retexture(MeshInstance3D mi, Texture2D? texture)
	{
		for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
		{
			if (mi.Mesh.SurfaceGetMaterial(i) is not BaseMaterial3D source)
				continue;
			var copy = (BaseMaterial3D)source.Duplicate();
			if (texture is not null)
				copy.AlbedoTexture = texture;
			copy.TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps;
			mi.SetSurfaceOverrideMaterial(i, copy);
		}
	}

}
