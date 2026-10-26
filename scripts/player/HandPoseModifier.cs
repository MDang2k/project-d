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
	private int _handL = -1, _handR = -1, _twistL = -1, _twistR = -1;

	/// <summary>Share of the hand's roll about the forearm axis copied onto ForearmTwist_* (wrist half of the forearm).</summary>
	public float ForearmTwistShare { get; set; } = 0.7f;

	/// <summary>Grip-aligned targets (set while holding a weapon); null leaves the hand as the IK left it.</summary>
	public Node3D? TargetLeft { get; set; }
	public Node3D? TargetRight { get; set; }
	public string PoseLeft { get; set; } = "HandRelaxed_L";
	public string PoseRight { get; set; } = "HandRelaxed_R";

	public bool HasPose(string name) => _poses.ContainsKey(name);

	/// <summary>Reads finger rotations from the animations (first key of each rotation track).</summary>
	public void Setup(Skeleton3D skel, AnimationPlayer? player)
	{
		_handL = skel.FindBone("Hand_L");
		_handR = skel.FindBone("Hand_R");
		_twistL = skel.FindBone("ForearmTwist_L");
		_twistR = skel.FindBone("ForearmTwist_R");
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
		Twist(skel, _handL, _twistL, TargetLeft is null ? 0f : ForearmTwistShare);
		Twist(skel, _handR, _twistR, TargetRight is null ? 0f : ForearmTwistShare);
	}

	private void Apply(Skeleton3D skel, string pose)
	{
		if (!_poses.TryGetValue(pose, out var bones))
			return;
		foreach (var (bone, rot) in bones)
			skel.SetBonePoseRotation(bone, rot);
	}

	/// <summary>Palm point in hand-bone space (model's Palm_L / Palm_R); its frame follows the grip convention.</summary>
	public Transform3D PalmLeft { get; set; } = Transform3D.Identity;
	public Transform3D PalmRight { get; set; } = Transform3D.Identity;

	/// <summary>Sets the hand's rotation so its palm frame matches the target's (grip convention, pipeline spec).</summary>
	private void Align(Skeleton3D skel, int hand, Node3D? target)
	{
		if (hand < 0 || target is null || !IsInstanceValid(target))
			return;
		var palm = hand == _handL ? PalmLeft : PalmRight;
		var parent = skel.GetBoneGlobalPose(skel.GetBoneParent(hand)).Basis;
		var want = (skel.GlobalTransform.AffineInverse() * target.GlobalTransform).Basis.Orthonormalized()
			* palm.Basis.Orthonormalized().Inverse();
		skel.SetBonePoseRotation(hand, (parent.Orthonormalized().Inverse() * want).GetRotationQuaternion());
	}

	/// <summary>
	/// Copies part of the hand's roll about the forearm axis onto the twist bone, so the wrist half of the
	/// sleeve and glove turns with the hand instead of the wrist taking the whole roll. The twist bone is a
	/// leaf beside Hand_* (not its parent), so the hand itself does not move. Rolls are measured from the
	/// bones' rests, so oriented rigs work too.
	/// </summary>
	private static void Twist(Skeleton3D skel, int hand, int twistBone, float share)
	{
		if (hand < 0 || twistBone < 0)
			return;
		var twistRest = skel.GetBoneRest(twistBone).Basis.GetRotationQuaternion();
		var axis = skel.GetBoneRest(hand).Origin.Normalized();
		var q = skel.GetBonePoseRotation(hand) * skel.GetBoneRest(hand).Basis.GetRotationQuaternion().Inverse();
		var along = axis * new Vector3(q.X, q.Y, q.Z).Dot(axis);
		var roll = new Quaternion(along.X, along.Y, along.Z, q.W);
		skel.SetBonePoseRotation(twistBone, roll.LengthSquared() > 1e-8f
			? Quaternion.Identity.Slerp(roll.Normalized(), share) * twistRest
			: twistRest);
	}
}
