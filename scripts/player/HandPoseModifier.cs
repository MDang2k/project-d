using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// Runs after the arm IK: turns each hand to its grip marker's rotation and applies a finger pose
/// (character visuals spec, pipeline spec grip convention). Finger poses are the body model's
/// HandGrip_R / HandSupport_L / HandRelaxed_* animations, read once as per-bone rotations.
/// </summary>
public partial class HandPoseModifier : SkeletonModifier3D
{
	private readonly Dictionary<string, (int Bone, Quaternion Rot)[]> _poses = new();
	private int _handL = -1, _handR = -1;

	/// <summary>Grip-aligned targets (set while holding a weapon); null leaves the hand as the IK left it.</summary>
	public Node3D? TargetLeft { get; set; }
	public Node3D? TargetRight { get; set; }
	public string PoseLeft { get; set; } = "HandRelaxed_L";
	public string PoseRight { get; set; } = "HandRelaxed_R";

	/// <summary>Reads finger rotations from the animations (first key of each rotation track).</summary>
	public void Setup(Skeleton3D skel, AnimationPlayer? player)
	{
		_handL = skel.FindBone("Hand_L");
		_handR = skel.FindBone("Hand_R");
		if (player is null)
			return;
		foreach (var name in player.GetAnimationList())
		{
			var anim = player.GetAnimation(name);
			var bones = new List<(int, Quaternion)>();
			for (var t = 0; t < anim.GetTrackCount(); t++)
			{
				if (anim.TrackGetType(t) != Animation.TrackType.Rotation3D)
					continue;
				// A pose drives only its own hand (name suffix _L / _R); the clips also key the other hand.
				var boneName = anim.TrackGetPath(t).GetConcatenatedSubNames();
				var bone = skel.FindBone(boneName);
				if (bone >= 0 && boneName.EndsWith(((string)name)[^2..]))
					bones.Add((bone, anim.RotationTrackInterpolate(t, 0.0)));
			}
			_poses[name] = bones.ToArray();
		}
	}

	public override void _ProcessModificationWithDelta(double delta)
	{
		var skel = GetSkeleton();
		if (skel is null)
			return;
		Apply(skel, PoseLeft);
		Apply(skel, PoseRight);
		Align(skel, _handL, TargetLeft);
		Align(skel, _handR, TargetRight);
	}

	private void Apply(Skeleton3D skel, string pose)
	{
		if (!_poses.TryGetValue(pose, out var bones))
			return;
		foreach (var (bone, rot) in bones)
			skel.SetBonePoseRotation(bone, rot);
	}

	/// <summary>Sets the hand's rotation so its frame matches the target's (palm frame = hand bone frame).</summary>
	private static void Align(Skeleton3D skel, int hand, Node3D? target)
	{
		if (hand < 0 || target is null || !IsInstanceValid(target))
			return;
		var parent = skel.GetBoneGlobalPose(skel.GetBoneParent(hand)).Basis;
		var want = (skel.GlobalTransform.AffineInverse() * target.GlobalTransform).Basis.Orthonormalized();
		skel.SetBonePoseRotation(hand, (parent.Orthonormalized().Inverse() * want).GetRotationQuaternion());
	}
}
