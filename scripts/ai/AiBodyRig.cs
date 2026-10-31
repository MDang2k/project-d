using Godot;

namespace ProjectD;

/// <summary>
/// The animated body of an AI (AI combat v2 spec s4: bots share the player body rig). Reuses the player's pieces: the
/// locomotion graph for the legs, stance by lowering the hips (prone lies forward), a two-bone IK that puts both hands
/// on the weapon's grips and the hand pose modifier, foot IK against the ground, and the upper body (spine, chest and
/// head) turning toward what the AI aims at. The weapon rides on the chest so it follows the turn and the stance.
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
	private FootIkModifier? _footIk;
	private int _spine, _chest, _headBone;
	private float _aimYaw, _aimPitch;
	private float _prone;
	private BoneAttachment3D? _mount;
	private static readonly AimOffsetTuning AimOffset = AimOffsetTuning.Default;

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
		_spine = _skel.FindBone("Spine");
		_chest = _skel.FindBone("Chest");
		_headBone = _skel.FindBone("Head");
		_hipsRest = _skel.GetBoneRest(_hips).Origin;
		_upperArmL = _skel.FindBone("Arm_L");
		_upperArmR = _skel.FindBone("Arm_R");
		_armDownL = ArmDown(_upperArmL, _skel.FindBone("Forearm_L"));
		_armDownR = ArmDown(_upperArmR, _skel.FindBone("Forearm_R"));

		_loco = LocomotionLayer.Build(_skel, Gait);
		_footIk = new FootIkModifier { Name = "FootIK" };
		_skel.AddChild(_footIk);
		_footIk.Setup(_skel, FootIkTuning.Default);

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

	/// <summary>
	/// Puts the weapon on a mount at the chest (so it follows the torso turn and the stance) and takes both hands to it.
	/// </summary>
	public void Hold(WeaponNode weapon)
	{
		_mount ??= new BoneAttachment3D { Name = "WeaponMount", BoneName = "Chest" };
		if (_mount.GetParent() is null)
			_skel.AddChild(_mount);
		// the chest bone's own axes are not the body's: an aligned pivot makes -Z forward and X right in the body frame
		var toBody = _skel.GetBoneGlobalRest(_chest).Basis.Orthonormalized().Inverse();
		var pivot = new Node3D { Name = "HoldPivot", Basis = toBody, Position = toBody * new Vector3(0.1f, -0.05f, -0.35f) };
		_mount.AddChild(pivot);
		weapon.GetParent()?.RemoveChild(weapon);
		pivot.AddChild(weapon);
		weapon.Transform = Transform3D.Identity;
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

	private static Quaternion Pitch(float degrees) => new(Vector3.Right, Mathf.DegToRad(degrees));
	private static Quaternion Yaw(float degrees) => new(Vector3.Up, Mathf.DegToRad(degrees));

	private void Rotate(int bone, Quaternion delta)
		=> _skel.SetBonePoseRotation(bone, delta * _skel.GetBoneRest(bone).Basis.GetRotationQuaternion());

	/// <summary>
	/// Turns spine, chest and head toward the aim: pitch through the player's aim-offset solver, yaw (the part the legs have
	/// not caught up with) shared 25 / 40 / 35. 	/// </summary>
	private void TurnUpperBody(float delta, float yawDeg, float pitchDeg)
	{
		_aimYaw = Mathf.MoveToward(_aimYaw, Mathf.Clamp(yawDeg, -80f, 80f), 420f * delta);
		_aimPitch = Mathf.MoveToward(_aimPitch, pitchDeg, 240f * delta);
		var upright = 1f - _prone;
		var (spinePitch, chestPitch, headPitch) = AimOffsetSolver.Distribute(AimOffset, _aimPitch, _prone);
		// no bladed stance for AI: the weapon rides on the chest and has to point where the body faces
		const float twist = 0f;
		var yaw = _aimYaw * upright;
		Rotate(_spine, Yaw(yaw * 0.25f) * Pitch(spinePitch));
		Rotate(_chest, Yaw(-twist + yaw * 0.4f) * Pitch(chestPitch));
		Rotate(_headBone, Yaw(twist + yaw * 0.35f) * Pitch(headPitch));
	}

	/// <summary>
	/// Foot IK input: probes the ground under each animated foot (skeleton space), as the player body does. Call each
	/// physics frame while the AI is near; <paramref name="planting"/> false lets the feet go free.
	/// </summary>
	public void UpdateFootIk(PhysicsDirectSpaceState3D space, uint mask, Rid exclude, bool planting)
	{
		if (_footIk is null)
			return;
		_footIk.Planting = planting && _prone < 0.3f;
		if (!_footIk.Planting)
			return;
		var t = _footIk.Tuning;
		var toWorld = _skel.GlobalTransform;
		var toLocal = toWorld.AffineInverse();
		var floor = toWorld.Origin.Y;
		for (var i = 0; i < 2; i++)
		{
			var ankle = toWorld * _footIk.AnkleLocal[i];
			var query = PhysicsRayQueryParameters3D.Create(new Vector3(ankle.X, floor + t.RayUp, ankle.Z),
				new Vector3(ankle.X, floor - t.RayDown, ankle.Z), mask, new Godot.Collections.Array<Rid> { exclude });
			var hit = space.IntersectRay(query);
			_footIk.Probes[i] = hit.Count == 0
				? new FootProbe()
				: new FootProbe
				{
					Hit = true,
					GroundY = (toLocal * hit["position"].AsVector3()).Y,
					Normal = (toLocal.Basis * hit["normal"].AsVector3()).Normalized(),
				};
		}
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
	public void Animate(float delta, Vector3 localVelocity, float walkSpeed, float sprintSpeed, float stanceScale, bool airborne,
		float aimYawDeg, float aimPitchDeg)
	{
		if (_loco is null)
			return;
		var eyeY = stanceScale >= 0.65f
			? Mathf.Lerp(Cfg.CrouchEyeHeight, Cfg.StandEyeHeight, (stanceScale - 0.65f) / 0.35f)
			: Mathf.Lerp(Cfg.ProneEyeHeight, Cfg.CrouchEyeHeight, Mathf.Clamp((stanceScale - 0.35f) / 0.30f, 0f, 1f));
		var weights = BodyPoseModel.StanceBlend(eyeY, Cfg.StandEyeHeight, Cfg.CrouchEyeHeight, Cfg.ProneEyeHeight);
		_loco.Update(localVelocity, walkSpeed, sprintSpeed, weights, airborne);

		var prone = weights.Prone;
		var hipsY = Mathf.Lerp(eyeY - Cfg.TorsoToEyeHeight, Cfg.ProneHipsHeight, prone);
		var hipsZ = Mathf.Lerp(0f, Cfg.ProneBodyBack, prone);
		var hipsPos = new Vector3(_hipsRest.X, hipsY, _hipsRest.Z + hipsZ);
		_skel.SetBonePosePosition(_hips, hipsPos);
		if (_footIk is not null)
			_footIk.HipsBase = hipsPos;
		_prone = prone;
		_skel.SetBonePoseRotation(_hips, new Quaternion(Vector3.Right, Mathf.DegToRad(-90f * prone)) * _skel.GetBoneRest(_hips).Basis.GetRotationQuaternion());
		HoldHeight = Mathf.Lerp(hipsY + 0.4f, 0.3f, prone);
		TurnUpperBody(delta, aimYawDeg, aimPitchDeg);
		if (_held is null)
		{
			_skel.SetBonePoseRotation(_upperArmL, _armDownL);
			_skel.SetBonePoseRotation(_upperArmR, _armDownR);
		}
	}
}
