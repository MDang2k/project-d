using System;
using Godot;

namespace ProjectD;

/// <summary>Ground under one foot, in skeleton space: where the probe hit and the surface normal.</summary>
public struct FootProbe
{
	public bool Hit;
	public float GroundY;
	public Vector3 Normal;
}

/// <summary>
/// Foot IK (animation spec s4.5): runs after the locomotion graph. Each foot keeps the animated stride but follows
/// the ground under it (steps, slopes), is pulled onto the ground when it would hover or sink, tilts to the surface,
/// and the pelvis lowers (up to MaxDrop) when the lower foot would be out of reach. The probes come from
/// PlayerBody each physics frame, so this modifier needs no physics access. Works on any rig orientation: each
/// leg bone is turned from its animated limb direction to the solved one, so twist and bone axes are kept.
/// </summary>
public partial class FootIkModifier : SkeletonModifier3D
{
	private static readonly string[] Sides = { "L", "R" };

	private FootIkTuning _t = FootIkTuning.Default;
	private int _hips;
	private readonly int[] _thigh = new int[2];
	private readonly int[] _shin = new int[2];
	private readonly int[] _foot = new int[2];
	private float _footHeight;
	private float _drop;
	private float _weight;
	private bool _ready;

	/// <summary>Set by PlayerBody: false while airborne, mantling or prone.</summary>
	public bool Planting { get; set; }

	/// <summary>Where Pose() put the hips this frame; the pelvis drop is applied on top of it.</summary>
	public Vector3 HipsBase { get; set; }

	public FootProbe[] Probes { get; } = new FootProbe[2];

	/// <summary>Animated ankle positions (before IK) in skeleton space, for the next probe.</summary>
	public Vector3[] AnkleLocal { get; } = new Vector3[2];

	/// <summary>Dev: current pelvis drop and the ankle height above the floor after IK.</summary>
	public float PelvisDrop => _drop;
	public float[] FinalAnkleY { get; } = new float[2];

	public FootIkTuning Tuning => _t;
	public float RestAnkleY => _footHeight;

	public void Setup(Skeleton3D skel, FootIkTuning tuning)
	{
		_t = tuning;
		_hips = skel.FindBone("Hips");
		for (var i = 0; i < 2; i++)
		{
			_thigh[i] = skel.FindBone($"Thigh_{Sides[i]}");
			_shin[i] = skel.FindBone($"Shin_{Sides[i]}");
			_foot[i] = skel.FindBone($"Foot_{Sides[i]}");
		}
		if (_hips < 0 || Array.Exists(_thigh, b => b < 0) || Array.Exists(_shin, b => b < 0) || Array.Exists(_foot, b => b < 0))
			return;
		_footHeight = skel.GetBoneGlobalRest(_foot[0]).Origin.Y; // ankle height above the sole when standing at rest
		HipsBase = skel.GetBonePosePosition(_hips);
		_ready = true;
	}

	public override void _ProcessModificationWithDelta(double deltaD)
	{
		var skel = GetSkeleton();
		if (skel is null || !_ready)
			return;
		var delta = (float)deltaD;
		_weight = Mathf.MoveToward(_weight, Planting ? 1f : 0f, _t.BlendSpeed * delta);
		skel.SetBonePosePosition(_hips, HipsBase);

		// the animated state, before anything is changed
		var hipsRot = skel.GetBoneGlobalPose(_hips).Basis.GetRotationQuaternion();
		var thigh = new Transform3D[2];
		var shin = new Transform3D[2];
		var foot = new Transform3D[2];
		var local = new Quaternion[6];
		for (var i = 0; i < 2; i++)
		{
			thigh[i] = skel.GetBoneGlobalPose(_thigh[i]);
			shin[i] = skel.GetBoneGlobalPose(_shin[i]);
			foot[i] = skel.GetBoneGlobalPose(_foot[i]);
			local[i * 3] = skel.GetBonePoseRotation(_thigh[i]);
			local[i * 3 + 1] = skel.GetBonePoseRotation(_shin[i]);
			local[i * 3 + 2] = skel.GetBonePoseRotation(_foot[i]);
			AnkleLocal[i] = foot[i].Origin;
		}
		if (_weight < 0.001f)
		{
			_drop = Mathf.MoveToward(_drop, 0f, _t.DropSpeed * delta);
			for (var i = 0; i < 2; i++)
				FinalAnkleY[i] = AnkleLocal[i].Y;
			return;
		}

		// where each ankle should be: follow the ground under the foot, pull it onto the ground when it would hover or sink
		var targets = new Vector3[2];
		var planted = new float[2];
		for (var i = 0; i < 2; i++)
		{
			var probe = Probes[i];
			var offset = probe.Hit ? Math.Clamp(probe.GroundY, -_t.MaxOffset, _t.MaxOffset) : 0f;
			var sole = LegIk.SoleHeight(AnkleLocal[i].Y - _footHeight, _t.PlantBand);
			planted[i] = 1f - Math.Clamp(sole / (_t.PlantBand * 2f), 0f, 1f);
			targets[i] = new Vector3(AnkleLocal[i].X, offset + _footHeight + sole, AnkleLocal[i].Z);
		}

		// lower the pelvis when the lower foot would be out of reach
		var shortBy = 0f;
		for (var i = 0; i < 2; i++)
		{
			var reach = ((shin[i].Origin - thigh[i].Origin).Length() + (foot[i].Origin - shin[i].Origin).Length()) * 0.998f;
			shortBy = Math.Max(shortBy, (targets[i] - thigh[i].Origin).Length() - reach);
		}
		_drop = Mathf.MoveToward(_drop, LegIk.PelvisDrop(shortBy, _t.MaxDrop), _t.DropSpeed * delta);
		var lowered = new Vector3(0f, _drop * _weight, 0f);
		skel.SetBonePosePosition(_hips, HipsBase - lowered);

		for (var i = 0; i < 2; i++)
		{
			var hip = thigh[i].Origin - lowered;
			var l1 = (shin[i].Origin - thigh[i].Origin).Length();
			var l2 = (foot[i].Origin - shin[i].Origin).Length();
			// the knee keeps bending the way the animation bends it
			var pole = shin[i].Origin - lowered - (hip + (foot[i].Origin - lowered)) * 0.5f;
			if (pole.LengthSquared() < 1e-6f)
				pole = hipsRot * Vector3.Forward;
			var (knee, ankle) = LegIk.TwoBone(hip, targets[i], l1, l2, pole);

			var thighDes = new Basis(new Quaternion((shin[i].Origin - thigh[i].Origin).Normalized(), (knee - hip).Normalized())
				* thigh[i].Basis.GetRotationQuaternion());
			var shinDes = new Basis(new Quaternion((foot[i].Origin - shin[i].Origin).Normalized(), (ankle - knee).Normalized())
				* shin[i].Basis.GetRotationQuaternion());
			var tilt = LegIk.GroundTilt(Probes[i].Hit ? Probes[i].Normal : Vector3.Up, _t.MaxTiltDeg, planted[i]);
			var footDes = new Basis(tilt * foot[i].Basis.GetRotationQuaternion());

			var thighQ = thighDes.GetRotationQuaternion();
			var shinQ = shinDes.GetRotationQuaternion();
			var thighLocal = (hipsRot.Inverse() * thighQ).Normalized();
			var shinLocal = (thighQ.Inverse() * shinQ).Normalized();
			var footLocal = (shinQ.Inverse() * footDes.GetRotationQuaternion()).Normalized();
			skel.SetBonePoseRotation(_thigh[i], local[i * 3].Slerp(thighLocal, _weight));
			skel.SetBonePoseRotation(_shin[i], local[i * 3 + 1].Slerp(shinLocal, _weight));
			skel.SetBonePoseRotation(_foot[i], local[i * 3 + 2].Slerp(footLocal, _weight));
			FinalAnkleY[i] = ankle.Y;
		}
	}
}
