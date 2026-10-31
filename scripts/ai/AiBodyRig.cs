using Godot;

namespace ProjectD;

/// <summary>
/// The animated body of an AI (AI combat v2 spec s4: bots share the player body rig). Reuses the player's pieces: the
/// locomotion graph for the legs, stance by lowering the hips (prone lies forward), a two-bone IK that puts both hands
/// on the weapon's grips and the hand pose modifier. No foot IK yet.
/// </summary>
public partial class AiBodyRig : Node3D
{
	private static readonly GaitTuning Gait = GaitTuning.Default;
	private static readonly PlayerConfig Cfg = new();

	private Skeleton3D _skel = null!;
	private LocomotionLayer? _loco;
	private TwoBoneIK3D? _ik;
	private HandPoseModifier? _hands;
	private Transform3D _palmL = Transform3D.Identity, _palmR = Transform3D.Identity;
	private Quaternion _armDownL, _armDownR;
	private int _hips, _upperArmL, _upperArmR;
	private Vector3 _hipsRest;
	private WeaponNode? _held;
	private float _stance = 1f;

	/// <summary>Height of the gun above the feet for the current stance, metres.</summary>
	public float HoldHeight { get; private set; } = 1.3f;

	public void SetSkeleton(Skeleton3D skeleton, Transform3D palmL, Transform3D palmR)
	{
		_skel = skeleton;
		_palmL = palmL;
		_palmR = palmR;
	}

	public override void _Ready()
	{
		_skel.ResetBonePoses();
		_hips = _skel.FindBone("Hips");
		_hipsRest = _skel.GetBoneRest(_hips).Origin;
		_upperArmL = _skel.FindBone("Arm_L");
		_upperArmR = _skel.FindBone("Arm_R");
		_armDownL = ArmDown(_upperArmL, _skel.FindBone("Forearm_L"));
		_armDownR = ArmDown(_upperArmR, _skel.FindBone("Forearm_R"));

		_loco = LocomotionLayer.Build(_skel, Gait);

		var hints = new Node3D { Name = "ElbowHints", Position = new Vector3(0f, Cfg.StandEyeHeight, 0f) };
		var poleR = new Marker3D { Name = "ElbowHintRight", Position = Cfg.ElbowHintRight };
		var poleL = new Marker3D { Name = "ElbowHintLeft", Position = Cfg.ElbowHintLeft };
		hints.AddChild(poleR);
		hints.AddChild(poleL);
		AddChild(hints);
		_ik = new TwoBoneIK3D { Name = "ArmIK", SettingCount = 2, Active = false };
		_skel.AddChild(_ik);
		SetupArm(_ik, 0, "R", poleR);
		SetupArm(_ik, 1, "L", poleL);
		_hands = new HandPoseModifier { Name = "HandPose", ForearmTwistShare = Cfg.ForearmTwistShare, PalmLeft = _palmL, PalmRight = _palmR };
		_skel.AddChild(_hands); // after the IK: modifiers run in child order
		_hands.Setup(_skel, null);
	}

	private static void SetupArm(TwoBoneIK3D ik, int index, string side, Node3D pole)
	{
		ik.SetRootBoneName(index, $"Arm_{side}");
		ik.SetMiddleBoneName(index, $"Forearm_{side}");
		ik.SetEndBoneName(index, $"Hand_{side}");
		ik.SetPoleNode(index, ik.GetPathTo(pole));
	}

	/// <summary>Upper-arm rotation that hangs the arm down from the T-pose rest (as the player body does).</summary>
	private Quaternion ArmDown(int upperArm, int forearm)
	{
		var rest = _skel.GetBoneRest(upperArm).Basis;
		var dir = (rest * _skel.GetBoneRest(forearm).Origin).Normalized();
		var side = Mathf.Sign(_skel.GetBoneGlobalRest(upperArm).Origin.X);
		var hang = new Quaternion(Vector3.Back, Mathf.DegToRad(Cfg.ArmHangOutDeg) * side) * Vector3.Down;
		var down = (_skel.GetBoneGlobalRest(_skel.GetBoneParent(upperArm)).Basis.Orthonormalized().Inverse() * hang).Normalized();
		var axis = dir.Cross(down);
		var arc = axis.LengthSquared() < 1e-6f ? Quaternion.Identity : new Quaternion(axis.Normalized(), dir.AngleTo(down));
		return arc * rest.GetRotationQuaternion();
	}

	/// <summary>Both hands go to the weapon's grips; fingers close round them.</summary>
	public void Hold(WeaponNode weapon)
	{
		_held = weapon;
		if (_ik is null || _hands is null)
			return;
		_ik.Active = true;
		_ik.SetTargetNode(0, _ik.GetPathTo(WristTarget(weapon.RightHandAction, _palmR)));
		_ik.SetTargetNode(1, _ik.GetPathTo(WristTarget(weapon.LeftHandAction, _palmL)));
		_hands.TargetRight = weapon.RightHandAction;
		_hands.TargetLeft = weapon.LeftHandAction;
		_hands.PoseRight = "HandGrip_R";
		_hands.PoseLeft = "HandSupport_L";
	}

	private static Node3D WristTarget(Node3D grip, Transform3D palm)
	{
		var wrist = new Node3D { Name = "WristTarget", Transform = palm.AffineInverse() };
		grip.AddChild(wrist);
		return wrist;
	}

	/// <summary>Dev: thigh pitch in degrees (left, right) after the locomotion graph, and the hips height.</summary>
	public string DebugPose()
	{
		float Pitch(string bone) => Mathf.RadToDeg(_skel.GetBonePoseRotation(_skel.FindBone(bone)).GetEuler().X);
		return $"thighs={Pitch("Thigh_L"):0},{Pitch("Thigh_R"):0} hips={_skel.GetBonePosePosition(_hips).Y:0.00}";
	}

	/// <summary>Stops the mixer when the AI is far away (it needs no legs there).</summary>
	public void SetActive(bool on)
	{
		if (_loco is not null && _loco.Tree.Active != on)
			_loco.Tree.Active = on;
	}

	/// <summary>
	/// Feeds the legs from the body's velocity and poses the hips for the stance. <paramref name="stanceScale"/> is 1
	/// standing, about 0.65 crouched and 0.35 prone (the scale the AI already uses for its hitboxes).
	/// </summary>
	public void Animate(Vector3 localVelocity, float walkSpeed, float sprintSpeed, float stanceScale, bool airborne)
	{
		if (_loco is null)
			return;
		_stance = stanceScale;
		var eyeY = stanceScale >= 0.65f
			? Mathf.Lerp(Cfg.CrouchEyeHeight, Cfg.StandEyeHeight, (stanceScale - 0.65f) / 0.35f)
			: Mathf.Lerp(Cfg.ProneEyeHeight, Cfg.CrouchEyeHeight, Mathf.Clamp((stanceScale - 0.35f) / 0.30f, 0f, 1f));
		var weights = BodyPoseModel.StanceBlend(eyeY, Cfg.StandEyeHeight, Cfg.CrouchEyeHeight, Cfg.ProneEyeHeight);
		_loco.Update(localVelocity, walkSpeed, sprintSpeed, weights, airborne);

		var prone = weights.Prone;
		var hipsY = Mathf.Lerp(eyeY - Cfg.TorsoToEyeHeight, Cfg.ProneHipsHeight, prone);
		var hipsZ = Mathf.Lerp(0f, Cfg.ProneBodyBack, prone);
		_skel.SetBonePosePosition(_hips, new Vector3(_hipsRest.X, hipsY, _hipsRest.Z + hipsZ));
		_skel.SetBonePoseRotation(_hips, new Quaternion(Vector3.Right, Mathf.DegToRad(-90f * prone)) * _skel.GetBoneRest(_hips).Basis.GetRotationQuaternion());
		HoldHeight = Mathf.Lerp(hipsY + 0.4f, 0.3f, prone);
		if (_held is null)
		{
			_skel.SetBonePoseRotation(_upperArmL, _armDownL);
			_skel.SetBonePoseRotation(_upperArmR, _armDownR);
		}
	}
}
