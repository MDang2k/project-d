using System;
using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// Layer 1 of the animation graph (animation spec s1.1): an AnimationTree that drives the legs from the body's
/// local velocity. Three directional blend spaces (stand, crouch, prone) with idle at the centre, a walk ring at
/// 0.5 and a sprint ring at 1.0, blended by the stance weights, time-scaled by ground speed. The clips are
/// generated placeholders (GaitModel); real clips with the same names replace them without touching the graph.
/// </summary>
public sealed class LocomotionLayer
{
	private const int Steps = 16;
	private static readonly string[] Bones = { "Thigh_L", "Shin_L", "Foot_L", "Thigh_R", "Shin_R", "Foot_R" };

	private static readonly Dictionary<(string, GaitTuning), AnimationLibrary> Libraries = new();

	private readonly AnimationTree _tree;

	public AnimationTree Tree => _tree;

	private LocomotionLayer(AnimationTree tree) => _tree = tree;

	/// <summary>Builds the tree under the skeleton's parent. Clip tracks use the skeleton's real path.</summary>
	public static LocomotionLayer Build(Skeleton3D skeleton, GaitTuning gait)
	{
		var parent = skeleton.GetParent();
		var skeletonPath = PathFrom(parent, skeleton);
		var tree = new AnimationTree
		{
			Name = "LocomotionTree",
			CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Physics,
		};
		// keys are leg motion on top of each bone's rest (oriented rigs keep their bone directions)
		var rests = new Dictionary<string, Quaternion>();
		for (var i = 0; i < skeleton.GetBoneCount(); i++)
			rests[skeleton.GetBoneName(i)] = skeleton.GetBoneRest(i).Basis.GetRotationQuaternion();
		// every body on the same rig and gait shares one library (building the clips is not cheap)
		var key = (skeletonPath, gait);
		if (!Libraries.TryGetValue(key, out var library))
			Libraries[key] = library = BuildLibrary(skeletonPath, gait, rests);
		tree.AddAnimationLibrary("", library);
		tree.TreeRoot = BuildGraph();
		parent.AddChild(tree);
		tree.Active = true;
		// stop the mixer before the node leaves the tree: an active tree freed with the scene crashed the mono shutdown
		tree.TreeExiting += () => tree.Active = false;
		return new LocomotionLayer(tree);
	}

	private static string PathFrom(Node ancestor, Node node)
	{
		var parts = new List<string>();
		for (var n = node; n is not null && n != ancestor; n = n.GetParent())
			parts.Insert(0, n.Name);
		return string.Join("/", parts);
	}

	// --- clips ----------------------------------------------------------------------------------

	private static Quaternion Rest(Dictionary<string, Quaternion> rests, string bone)
		=> rests.TryGetValue(bone, out var q) ? q : Quaternion.Identity;

	private static readonly (string Name, float Ring)[] Rings = { ("walk", 0.5f), ("sprint", 1f) };

	private static Vector2 Dir(int index) => new(MathF.Sin(index * Mathf.Pi / 4f), MathF.Cos(index * Mathf.Pi / 4f));

	private static string ClipName(GaitStance stance, string ring, int dir) => $"Loco_{stance}_{ring}_{dir}";

	private static AnimationLibrary BuildLibrary(string skeletonPath, GaitTuning gait, Dictionary<string, Quaternion> rests)
	{
		var library = new AnimationLibrary();
		foreach (GaitStance stance in Enum.GetValues(typeof(GaitStance)))
		{
			library.AddAnimation(ClipName(stance, "idle", 0), BuildAny(skeletonPath, gait, rests, stance, 0f, Vector2.Zero, 0));
			foreach (var (ringName, ring) in Rings)
			{
				for (var d = 0; d < 8; d++)
					library.AddAnimation(ClipName(stance, ringName, d), BuildAny(skeletonPath, gait, rests, stance, ring, Dir(d), d));
			}
		}
		return library;
	}

	/// <summary>The retargeted pack clip where one exists for this stance, ring and direction, else the generated gait.</summary>
	private static Animation BuildAny(string skeletonPath, GaitTuning gait, Dictionary<string, Quaternion> rests, GaitStance stance, float ring, Vector2 dir, int dirIndex)
	{
		var (real, reverse) = ring <= 0f ? (true, false) : LocomotionClips.DirectionUse(dirIndex);
		if (real && LocomotionClips.ClipFor(stance, ring) is { } name && LocomotionClips.All.TryGetValue(name, out var clip))
			return BuildRealClip(skeletonPath, gait, rests, stance, clip, reverse);
		return BuildClip(skeletonPath, gait, rests, stance, ring, dir);
	}

	/// <summary>Stance base rotation of a leg bone (the pack keys are motion around it); straight legs standing.</summary>
	private static Quaternion BaseRotation(GaitTuning gait, GaitStance stance, string bone)
	{
		if (stance != GaitStance.Crouch)
			return Quaternion.Identity;
		var degrees = bone.StartsWith("Thigh") ? gait.CrouchBaseThighDeg
			: bone.StartsWith("Shin") ? -gait.CrouchBaseShinDeg
			: gait.CrouchBaseShinDeg - gait.CrouchBaseThighDeg;
		return new Quaternion(Vector3.Right, Mathf.DegToRad(degrees));
	}

	private static Animation BuildRealClip(string skeletonPath, GaitTuning gait, Dictionary<string, Quaternion> rests, GaitStance stance, RealClip clip, bool reverse)
	{
		var anim = new Animation { Length = clip.Length, LoopMode = Animation.LoopModeEnum.Linear };
		foreach (var (bone, keys) in clip.Bones)
		{
			var track = anim.AddTrack(Animation.TrackType.Rotation3D);
			anim.TrackSetPath(track, new NodePath($"{skeletonPath}:{bone}"));
			var baseRot = BaseRotation(gait, stance, bone);
			for (var i = 0; i < keys.Length; i++)
			{
				var (time, rot) = keys[reverse ? keys.Length - 1 - i : i];
				anim.RotationTrackInsertKey(track, reverse ? clip.Length - time : time, baseRot * rot * Rest(rests, bone));
			}
		}
		return anim;
	}

	private static Animation BuildClip(string skeletonPath, GaitTuning gait, Dictionary<string, Quaternion> rests, GaitStance stance, float ring, Vector2 dir)
	{
		var anim = new Animation { Length = 1f, LoopMode = Animation.LoopModeEnum.Linear };
		var tracks = new Dictionary<string, int>();
		foreach (var bone in Bones)
		{
			var track = anim.AddTrack(Animation.TrackType.Rotation3D);
			anim.TrackSetPath(track, new NodePath($"{skeletonPath}:{bone}"));
			tracks[bone] = track;
		}
		for (var i = 0; i <= Steps; i++)
		{
			var t = i / (float)Steps;
			var (left, right) = GaitModel.Legs(gait, stance, ring, dir, t % 1f);
			Key(anim, tracks, rests, "L", left, t);
			Key(anim, tracks, rests, "R", right, t);
		}
		return anim;
	}

	private static void Key(Animation anim, Dictionary<string, int> tracks, Dictionary<string, Quaternion> rests, string side, LegAngles a, float time)
	{
		anim.RotationTrackInsertKey(tracks[$"Thigh_{side}"], time,
			new Quaternion(Vector3.Right, Mathf.DegToRad(a.ThighDeg)) * new Quaternion(Vector3.Back, Mathf.DegToRad(a.ThighRollDeg))
			* Rest(rests, $"Thigh_{side}"));
		anim.RotationTrackInsertKey(tracks[$"Shin_{side}"], time,
			new Quaternion(Vector3.Right, Mathf.DegToRad(a.ShinDeg)) * Rest(rests, $"Shin_{side}"));
		anim.RotationTrackInsertKey(tracks[$"Foot_{side}"], time,
			new Quaternion(Vector3.Right, Mathf.DegToRad(a.FootDeg)) * Rest(rests, $"Foot_{side}"));
	}

	// --- graph ----------------------------------------------------------------------------------

	private static AnimationNodeBlendTree BuildGraph()
	{
		var root = new AnimationNodeBlendTree();
		var y = 0f;
		foreach (GaitStance stance in Enum.GetValues(typeof(GaitStance)))
		{
			var space = new AnimationNodeBlendSpace2D
			{
				MinSpace = new Vector2(-1f, -1f),
				MaxSpace = new Vector2(1f, 1f),
				BlendMode = AnimationNodeBlendSpace2D.BlendModeEnum.Interpolated,
				SyncMode = AnimationNodeBlendSpace2D.SyncModeEnum.CyclicMutable,
			};
			space.AddBlendPoint(Clip(stance, "idle", 0), Vector2.Zero, -1, "idle");
			foreach (var (ringName, ring) in Rings)
			{
				for (var d = 0; d < 8; d++)
					space.AddBlendPoint(Clip(stance, ringName, d), Dir(d) * ring, -1, $"{ringName}_{d}");
			}
			root.AddNode(stance.ToString(), space, new Vector2(0f, y));
			y += 160f;
		}
		root.AddNode("StandCrouch", new AnimationNodeBlend2 { Sync = true }, new Vector2(300f, 0f));
		root.AddNode("WithProne", new AnimationNodeBlend2 { Sync = true }, new Vector2(500f, 0f));
		root.AddNode("Speed", new AnimationNodeTimeScale(), new Vector2(700f, 0f));
		root.ConnectNode("StandCrouch", 0, "Stand");
		root.ConnectNode("StandCrouch", 1, "Crouch");
		root.ConnectNode("WithProne", 0, "StandCrouch");
		root.ConnectNode("WithProne", 1, "Prone");
		root.ConnectNode("Speed", 0, "WithProne");
		root.ConnectNode("output", 0, "Speed");
		return root;
	}

	private static AnimationNodeAnimation Clip(GaitStance stance, string ring, int dir)
		=> new() { Animation = ClipName(stance, ring, dir) };

	// --- per frame ------------------------------------------------------------------------------

	/// <param name="localVelocity">Velocity in the body frame (forward is -Z).</param>
	/// <param name="stance">Stand, crouch and prone weights (BodyPoseModel.StanceBlend).</param>
	public void Update(Vector3 localVelocity, float walkSpeed, float sprintSpeed,
		(float Stand, float Crouch, float Prone) stance, bool airborne)
	{
		var velocity = airborne ? Vector3.Zero : localVelocity;
		var position = LocomotionParams.BlendPosition(velocity, walkSpeed, sprintSpeed);
		foreach (GaitStance s in Enum.GetValues(typeof(GaitStance)))
			_tree.Set($"parameters/{s}/blend_position", position);
		var (crouch, prone) = LocomotionParams.StanceFactors(stance);
		_tree.Set("parameters/StandCrouch/blend_amount", crouch);
		_tree.Set("parameters/WithProne/blend_amount", prone);
		var speed = new Vector2(velocity.X, velocity.Z).Length();
		_tree.Set("parameters/Speed/scale", LocomotionParams.PlayRate(speed));
	}
}
