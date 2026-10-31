using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// The model and texture variants of one kind of AI body. The face textures are full copies of the body texture with
/// only the head repainted, the outfit textures recolour the outfit model, so any face goes with any outfit.
/// </summary>
public sealed record BodyModel(string OutfitModel, string[] Faces, string[] OutfitTextures);

/// <summary>
/// Builds the visible body of an AI from the character models: the shared body (with head) plus an outfit on one
/// skeleton, a random face and outfit texture, and the arms hanging down. Static pose for now; the player's animation
/// and hand IK are not used on AI yet.
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

	private static T? Load<T>(string path) where T : Resource
	{
		if (Cache.TryGetValue(path, out var cached))
			return cached as T;
		var loaded = ResourceLoader.Exists(path) ? GD.Load<T>(path) : null;
		Cache[path] = loaded;
		return loaded;
	}

	/// <summary>The body for a kind, or null when the kind or one of its files is missing (the AI keeps its capsule).</summary>
	public static Node3D? Build(string kind, System.Random rng, out string description)
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

		var root = new Node3D { Name = "BodyVisual" };
		var body = bodyScene.Instantiate<Node3D>();
		var outfit = outfitScene.Instantiate<Node3D>();
		root.AddChild(body);
		var skel = (Skeleton3D)body.FindChildren("*", "Skeleton3D", true, false)[0];
		var outfitSkel = outfit.FindChildren("*", "Skeleton3D", true, false)[0];
		foreach (var child in outfitSkel.GetChildren())
		{
			child.Owner = null;
			foreach (var sub in child.FindChildren("*", "", true, false))
				sub.Owner = null;
			outfitSkel.RemoveChild(child);
			skel.AddChild(child);
		}
		outfit.Free();

		foreach (var node in skel.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)node;
			var texture = mi.Name == "HeadMesh" || mi.Name == "Body" ? faceTex : outfitTex;
			Retexture(mi, texture);
		}
		// the pose goes on once the skeleton is in the tree (set earlier, the import's rest pose came back)
		root.TreeEntered += () => Callable.From(() => HangArms(skel)).CallDeferred();
		description = $"{face} / {outfitTexture}";
		return root;
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

	/// <summary>The rig rests in a T pose; swing each upper arm down, a little out from the body.</summary>
	private static void HangArms(Skeleton3D skel)
	{
		foreach (var (arm, fore) in new[] { ("Arm_L", "Forearm_L"), ("Arm_R", "Forearm_R") })
		{
			var upper = skel.FindBone(arm);
			var lower = skel.FindBone(fore);
			if (upper < 0 || lower < 0)
				continue;
			var rest = skel.GetBoneRest(upper).Basis;
			var dir = (rest * skel.GetBoneRest(lower).Origin).Normalized();
			var side = Mathf.Sign(skel.GetBoneGlobalRest(upper).Origin.X);
			var hang = new Quaternion(Vector3.Back, Mathf.DegToRad(8f) * side) * Vector3.Down;
			var down = (skel.GetBoneGlobalRest(skel.GetBoneParent(upper)).Basis.Orthonormalized().Inverse() * hang).Normalized();
			var axis = dir.Cross(down);
			var arc = axis.LengthSquared() < 1e-6f ? Quaternion.Identity : new Quaternion(axis.Normalized(), dir.AngleTo(down));
			skel.SetBonePoseRotation(upper, arc * rest.GetRotationQuaternion());
		}
	}
}
