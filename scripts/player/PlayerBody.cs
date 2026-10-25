using Godot;

namespace ProjectD;

/// <summary>
/// Full-body first-person rig (GDD s6, CLAUDE.md "Full-body first person").
/// Uses the character models (assets/models/characters/player_body.glb plus player_outfit_{side}.glb,
/// character visuals spec) when both exist, else a procedural capsule placeholder with the same bones.
/// Legs and torso are visible and cast shadows; the head and cap cast a shadow only. Arms are driven
/// by one TwoBoneIK3D reaching the held item's GripRight / GripLeft markers, so a weapon needs no
/// hand-made animation. Held item markers: GripRight, GripLeft, Muzzle (documented marker set).
/// </summary>
public partial class PlayerBody : Node3D
{
	private static readonly Color Cloth = new(0.28f, 0.33f, 0.25f);
	private static readonly Color Skin = new(0.8f, 0.62f, 0.5f);
	private static readonly Color Metal = new(0.12f, 0.12f, 0.13f);

	private PlayerConfig _cfg = null!;
	private Skeleton3D _skel = null!;
	private int _hips, _spine, _chest, _headBone, _thighL, _thighR, _shinL, _shinR, _footL, _footR;
	private Vector3 _spineRest;
	private int _upperArmL, _upperArmR;
	private Quaternion _armDownL, _armDownR;

	private Node3D _head = null!;
	private Node3D _weaponParent = null!;
	private TwoBoneIK3D _ik = null!;
	private HandPoseModifier _hands = null!;
	/// <summary>Palm point in hand-bone space (model's Palm_L / Palm_R); identity on the placeholder.</summary>
	private Transform3D _palmL = Transform3D.Identity, _palmR = Transform3D.Identity;
	private AnimationPlayer? _handPoses;

	public WeaponNode? HeldItem { get; private set; }

	/// <param name="head">Camera pitch node; elbow hints live here.</param>
	/// <param name="weaponParent">Node the held item attaches to (recoil and sway apply to it).</param>
	/// <param name="side">Picks the outfit model.</param>
	public void Build(PlayerConfig cfg, Node3D head, Node3D weaponParent, PlayerSide side)
	{
		_cfg = cfg;
		_head = head;
		_weaponParent = weaponParent;
		_skel = LoadModelSkeleton(side) ?? BuildPlaceholderSkeleton();
		_hips = _skel.FindBone("Hips");
		_spine = _skel.FindBone("Spine");
		_chest = _skel.FindBone("Chest");
		_headBone = _skel.FindBone("Head");
		_thighL = _skel.FindBone("Thigh_L");
		_thighR = _skel.FindBone("Thigh_R");
		_shinL = _skel.FindBone("Shin_L");
		_shinR = _skel.FindBone("Shin_R");
		_footL = _skel.FindBone("Foot_L");
		_footR = _skel.FindBone("Foot_R");
		_spineRest = _skel.GetBoneRest(_spine).Origin;
		_upperArmL = _skel.FindBone("UpperArm_L");
		_upperArmR = _skel.FindBone("UpperArm_R");
		_armDownL = ArmDown(_upperArmL, _skel.FindBone("Forearm_L"));
		_armDownR = ArmDown(_upperArmR, _skel.FindBone("Forearm_R"));
		_skel.ResetBonePoses();
		// Dev hand/elbow probes (DebugBonePositions); the placeholder already has attachments on these bones.
		foreach (var bone in new[] { "Hand_L", "Hand_R", "Forearm_L", "Forearm_R" })
			_skel.AddChild(new BoneAttachment3D { Name = $"Probe_{bone}", BoneName = bone });
		BuildArmIk(head);
		_hands = new HandPoseModifier { Name = "HandPose" };
		_skel.AddChild(_hands); // after ArmIK: modifiers run in child order
		_hands.Setup(_skel, _handPoses);
	}

	/// <summary>Removes the held weapon; the arms hang down, hands relaxed.</summary>
	public void ClearHeldItem()
	{
		HeldItem?.QueueFree();
		HeldItem = null;
		_ik.Active = false;
		_hands.TargetLeft = _hands.TargetRight = null;
		_hands.PoseLeft = "HandRelaxed_L";
		_hands.PoseRight = "HandRelaxed_R";
	}

	/// <summary>
	/// Replaces the held weapon. The IK puts each wrist where the palm lands on the grip marker,
	/// then the hands turn to the markers' rotation and close round the grips.
	/// </summary>
	public void AttachHeldItem(WeaponNode weapon)
	{
		HeldItem?.QueueFree();
		HeldItem = weapon;
		_weaponParent.AddChild(weapon);
		_ik.Active = true;
		_ik.SetTargetNode(0, _ik.GetPathTo(WristTarget(weapon.GripRight, _palmR)));
		_ik.SetTargetNode(1, _ik.GetPathTo(WristTarget(weapon.GripLeft, _palmL)));
		_hands.TargetRight = weapon.GripRight;
		_hands.TargetLeft = weapon.GripLeft;
		_hands.PoseRight = "HandGrip_R";
		_hands.PoseLeft = "HandSupport_L";
	}

	/// <summary>Wrist position for a grip: the marker offset back by the palm point (palm frame = hand frame).</summary>
	private static Node3D WristTarget(Node3D grip, Transform3D palm)
	{
		var wrist = new Node3D { Name = "WristTarget", Transform = palm.AffineInverse() };
		grip.AddChild(wrist);
		return wrist;
	}

	/// <summary>Upper-arm rotation that points the arm straight down from its rest (T-pose or hanging).</summary>
	private Quaternion ArmDown(int upperArm, int forearm)
	{
		var rest = _skel.GetBoneRest(upperArm).Basis;
		var dir = (rest * _skel.GetBoneRest(forearm).Origin).Normalized();
		var axis = dir.Cross(Vector3.Down);
		var arc = axis.LengthSquared() < 1e-6f ? Quaternion.Identity : new Quaternion(axis.Normalized(), dir.AngleTo(Vector3.Down));
		return arc * rest.GetRotationQuaternion();
	}

	/// <summary>
	/// Body and outfit models moved onto one skeleton (both share the rig; skins bind by bone name).
	/// Null when either file is missing.
	/// </summary>
	private Skeleton3D? LoadModelSkeleton(PlayerSide side)
	{
		var body = LoadCharacter("player_body");
		// ponytail: no Soviet outfit model yet, so the Soviet side keeps the placeholder until it exists.
		var outfit = LoadCharacter($"player_outfit_{side.ToString().ToLowerInvariant()}");
		if (body is null || outfit is null)
		{
			body?.Free();
			outfit?.Free();
			return null;
		}
		AddChild(body);
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
			// Head and cap are hidden from the local camera but still cast a shadow.
			if (mi.Name == "HeadMesh" || mi.Name.ToString().StartsWith("Cap_"))
				mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
			// Pixel-art textures stay crisp (pipeline spec, characters).
			for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
			{
				if (mi.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D mat)
					mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps;
			}
		}
		// Palm empties sit on the hand bones' attachments, so their transform is in hand-bone space.
		if (skel.FindChild("Palm_L", true, false) is Node3D palmL)
			_palmL = palmL.Transform;
		if (skel.FindChild("Palm_R", true, false) is Node3D palmR)
			_palmR = palmR.Transform;
		_handPoses = body.FindChildren("*", "AnimationPlayer", true, false) is { Count: > 0 } players
			? (AnimationPlayer)players[0]
			: null;
		return skel;
	}

	private static Node3D? LoadCharacter(string id)
	{
		var path = $"res://assets/models/characters/{id}.glb";
		return ResourceLoader.Exists(path) && GD.Load<PackedScene>(path) is { } scene ? scene.Instantiate<Node3D>() : null;
	}

	private Skeleton3D BuildPlaceholderSkeleton()
	{
		_skel = new Skeleton3D { Name = "Skeleton" };
		AddChild(_skel);

		var hips = Bone("Hips", -1, new Vector3(0f, 0.95f, 0f));
		var spine = Bone("Spine", hips, new Vector3(0f, 0.20f, 0f));
		var chest = Bone("Chest", spine, new Vector3(0f, 0.25f, 0f));
		var head = Bone("Head", chest, new Vector3(0f, 0.25f, 0f));

		foreach (var side in new[] { 1f, -1f })
		{
			var s = side > 0f ? "R" : "L";
			var upper = Bone($"UpperArm_{s}", chest, new Vector3(0.20f * side, 0.12f, 0f));
			var fore = Bone($"Forearm_{s}", upper, new Vector3(0f, -_cfg.UpperArmLength, 0f));
			var hand = Bone($"Hand_{s}", fore, new Vector3(0f, -_cfg.ForearmLength, 0f));
			Part(upper, Capsule(0.045f, _cfg.UpperArmLength), new Vector3(0f, -_cfg.UpperArmLength * 0.5f, 0f), Cloth);
			Part(fore, Capsule(0.04f, _cfg.ForearmLength), new Vector3(0f, -_cfg.ForearmLength * 0.5f, 0f), Skin);
			Part(hand, new BoxMesh { Size = new Vector3(0.07f, 0.09f, 0.1f) }, new Vector3(0f, -0.04f, 0f), Skin);

			var thigh = Bone($"Thigh_{s}", hips, new Vector3(0.10f * side, 0f, 0f));
			var shin = Bone($"Shin_{s}", thigh, new Vector3(0f, -0.45f, 0f));
			var foot = Bone($"Foot_{s}", shin, new Vector3(0f, -0.45f, 0f));
			Part(thigh, Capsule(0.07f, 0.45f), new Vector3(0f, -0.225f, 0f), Cloth);
			Part(shin, Capsule(0.055f, 0.45f), new Vector3(0f, -0.225f, 0f), Cloth);
			Part(foot, new BoxMesh { Size = new Vector3(0.1f, 0.08f, 0.26f) }, new Vector3(0f, -0.04f, -0.06f), Metal);
		}

		// Torso sits behind the camera plane (front face at z=+0.05) so it never fills the view
		// or covers the hands; looking down shows its top face, shoulders and the legs.
		Part(hips, new BoxMesh { Size = new Vector3(0.34f, 0.2f, 0.2f) }, new Vector3(0f, 0f, 0.15f), Cloth);
		Part(spine, new BoxMesh { Size = new Vector3(0.36f, 0.55f, 0.2f) }, new Vector3(0f, 0.12f, 0.15f), Cloth);
		// Head is hidden from the local camera but still casts a shadow.
		Part(head, new SphereMesh { Radius = 0.11f, Height = 0.22f }, Vector3.Zero, Skin, shadowOnly: true);
		return _skel;
	}

	private int Bone(string name, int parent, Vector3 pos)
	{
		var i = _skel.AddBone(name);
		if (parent >= 0)
			_skel.SetBoneParent(i, parent);
		_skel.SetBoneRest(i, new Transform3D(Basis.Identity, pos));
		return i;
	}

	private static CapsuleMesh Capsule(float radius, float height) => new() { Radius = radius, Height = height };

	private void Part(int bone, Mesh mesh, Vector3 offset, Color color, bool shadowOnly = false)
	{
		var attach = new BoneAttachment3D { BoneName = _skel.GetBoneName(bone) };
		_skel.AddChild(attach);
		attach.AddChild(new MeshInstance3D
		{
			Mesh = mesh,
			Position = offset,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
			CastShadow = shadowOnly
				? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly
				: GeometryInstance3D.ShadowCastingSetting.On,
		});
	}

	private void BuildArmIk(Node3D head)
	{
		var poleR = new Marker3D { Name = "ElbowHintRight", Position = _cfg.ElbowHintRight };
		var poleL = new Marker3D { Name = "ElbowHintLeft", Position = _cfg.ElbowHintLeft };
		head.AddChild(poleR);
		head.AddChild(poleL);

		_ik = new TwoBoneIK3D { Name = "ArmIK", SettingCount = 2 };
		_skel.AddChild(_ik);
		SetupArm(_ik, 0, "R", poleR);
		SetupArm(_ik, 1, "L", poleL);
	}

	private static void SetupArm(TwoBoneIK3D ik, int index, string side, Node3D pole)
	{
		ik.SetRootBoneName(index, $"UpperArm_{side}");
		ik.SetMiddleBoneName(index, $"Forearm_{side}");
		ik.SetEndBoneName(index, $"Hand_{side}");
		ik.SetPoleNode(index, ik.GetPathTo(pole));
	}

	/// <summary>Dev: world positions of both hand bones (after IK) for jitter measurements.</summary>
	public (Vector3 Left, Vector3 Right) DebugHandPositions() => DebugBonePositions("Hand_L", "Hand_R");

	/// <summary>Dev: world positions of both elbow (forearm) bones after IK.</summary>
	public (Vector3 Left, Vector3 Right) DebugElbowPositions() => DebugBonePositions("Forearm_L", "Forearm_R");

	private (Vector3 Left, Vector3 Right) DebugBonePositions(string left, string right)
	{
		// Bone attachments follow the final (post-IK) pose; raw bone poses do not include modifiers.
		Vector3 Find(string bone)
		{
			foreach (var child in _skel.GetChildren())
			{
				if (child is BoneAttachment3D a && a.BoneName == bone)
					return a.GlobalPosition;
			}
			return Vector3.Zero;
		}
		return (Find(left), Find(right));
	}

	/// <summary>Poses legs and torso from the current eye height and lean (call every frame).</summary>
	public void Pose(float eyeY, float leanRoll, float leanShift)
	{
		var (_, crouch, prone) = BodyPoseModel.StanceBlend(
			eyeY, _cfg.StandEyeHeight, _cfg.CrouchEyeHeight, _cfg.ProneEyeHeight);
		// Hips follow the camera so the head stays under it; prone lies forward along -Z.
		var hipsY = Mathf.Lerp(eyeY - _cfg.TorsoToEyeHeight, _cfg.ProneHipsHeight, prone);
		_skel.SetBonePosePosition(_hips, new Vector3(0f, hipsY, prone * _cfg.ProneBodyBack));
		_skel.SetBonePoseRotation(_hips, Pitch(-90f * prone));

		var thigh = Pitch(_cfg.CrouchThighDeg * crouch);
		var shin = Pitch(-_cfg.CrouchShinDeg * crouch);
		var foot = Pitch((_cfg.CrouchShinDeg - _cfg.CrouchThighDeg) * crouch);
		_skel.SetBonePoseRotation(_thighL, thigh);
		_skel.SetBonePoseRotation(_thighR, thigh);
		_skel.SetBonePoseRotation(_shinL, shin);
		_skel.SetBonePoseRotation(_shinR, shin);
		_skel.SetBonePoseRotation(_footL, foot);
		_skel.SetBonePoseRotation(_footR, foot);

		// Upper body sits behind the camera so looking down shows the legs, not the inside of the chest.
		_skel.SetBonePosePosition(_spine, _spineRest + new Vector3(leanShift * 0.5f, 0f, _cfg.UpperBodyBack * (1f - prone)));
		_skel.SetBonePoseRotation(_spine, new Quaternion(Vector3.Back, leanRoll));

		// Bladed stance while holding a weapon: chest turns right so the left shoulder comes forward,
		// head turns back so it keeps facing the camera. Off when prone (the chest lies along -Z).
		var twist = HeldItem is null ? 0f : _cfg.HoldChestTwistDeg * (1f - prone);
		_skel.SetBonePoseRotation(_chest, Yaw(-twist));
		_skel.SetBonePoseRotation(_headBone, Yaw(twist));

		// No weapon: the IK is off, so the arms hang down instead of staying in the model's T-pose rest.
		if (HeldItem is null)
		{
			_skel.SetBonePoseRotation(_upperArmL, _armDownL);
			_skel.SetBonePoseRotation(_upperArmR, _armDownR);
		}
	}

	private static Quaternion Pitch(float degrees) => new(Vector3.Right, Mathf.DegToRad(degrees));
	private static Quaternion Yaw(float degrees) => new(Vector3.Up, Mathf.DegToRad(degrees));
}
