using Godot;

namespace ProjectD;

/// <summary>
/// Placeholder full-body first-person rig (GDD s6, CLAUDE.md "Full-body first person").
/// Procedural Skeleton3D with capsule limbs; legs and torso are visible and cast shadows,
/// the head casts a shadow only. Arms are driven by one TwoBoneIK3D reaching the held item's
/// GripRight / GripLeft markers, so a placeholder weapon needs no hand-made animation.
/// Held item markers: GripRight, GripLeft, Muzzle (documented marker set).
/// </summary>
public partial class PlayerBody : Node3D
{
	private static readonly Color Cloth = new(0.28f, 0.33f, 0.25f);
	private static readonly Color Skin = new(0.8f, 0.62f, 0.5f);
	private static readonly Color Metal = new(0.12f, 0.12f, 0.13f);

	private PlayerConfig _cfg = null!;
	private Skeleton3D _skel = null!;
	private int _hips, _spine, _thighL, _thighR, _shinL, _shinR, _footL, _footR;
	private Vector3 _spineRest;

	private Node3D _head = null!;
	private Node3D _weaponParent = null!;
	private TwoBoneIK3D _ik = null!;

	public WeaponNode? HeldItem { get; private set; }

	/// <param name="head">Camera pitch node; elbow hints live here.</param>
	/// <param name="weaponParent">Node the held item attaches to (recoil and sway apply to it).</param>
	public void Build(PlayerConfig cfg, Node3D head, Node3D weaponParent)
	{
		_cfg = cfg;
		_head = head;
		_weaponParent = weaponParent;
		BuildSkeleton();
		BuildArmIk(head);
	}

	/// <summary>Removes the held weapon; the arms drop to their rest pose.</summary>
	public void ClearHeldItem()
	{
		HeldItem?.QueueFree();
		HeldItem = null;
		_ik.Active = false;
	}

	/// <summary>Replaces the held weapon and re-aims both arms at its grip markers.</summary>
	public void AttachHeldItem(WeaponNode weapon)
	{
		HeldItem?.QueueFree();
		HeldItem = weapon;
		_weaponParent.AddChild(weapon);
		_ik.Active = true;
		_ik.SetTargetNode(0, _ik.GetPathTo(weapon.GripRight));
		_ik.SetTargetNode(1, _ik.GetPathTo(weapon.GripLeft));
	}

	private void BuildSkeleton()
	{
		_skel = new Skeleton3D { Name = "Skeleton" };
		AddChild(_skel);

		_hips = Bone("Hips", -1, new Vector3(0f, 0.95f, 0f));
		_spine = Bone("Spine", _hips, new Vector3(0f, 0.20f, 0f));
		var chest = Bone("Chest", _spine, new Vector3(0f, 0.25f, 0f));
		var head = Bone("Head", chest, new Vector3(0f, 0.25f, 0f));
		_spineRest = new Vector3(0f, 0.20f, 0f);

		foreach (var side in new[] { 1f, -1f })
		{
			var s = side > 0f ? "R" : "L";
			var upper = Bone($"UpperArm_{s}", chest, new Vector3(0.20f * side, 0.12f, 0f));
			var fore = Bone($"Forearm_{s}", upper, new Vector3(0f, -_cfg.UpperArmLength, 0f));
			var hand = Bone($"Hand_{s}", fore, new Vector3(0f, -_cfg.ForearmLength, 0f));
			Part(upper, Capsule(0.045f, _cfg.UpperArmLength), new Vector3(0f, -_cfg.UpperArmLength * 0.5f, 0f), Cloth);
			Part(fore, Capsule(0.04f, _cfg.ForearmLength), new Vector3(0f, -_cfg.ForearmLength * 0.5f, 0f), Skin);
			Part(hand, new BoxMesh { Size = new Vector3(0.07f, 0.09f, 0.1f) }, new Vector3(0f, -0.04f, 0f), Skin);

			var thigh = Bone($"Thigh_{s}", _hips, new Vector3(0.10f * side, 0f, 0f));
			var shin = Bone($"Shin_{s}", thigh, new Vector3(0f, -0.45f, 0f));
			var foot = Bone($"Foot_{s}", shin, new Vector3(0f, -0.45f, 0f));
			Part(thigh, Capsule(0.07f, 0.45f), new Vector3(0f, -0.225f, 0f), Cloth);
			Part(shin, Capsule(0.055f, 0.45f), new Vector3(0f, -0.225f, 0f), Cloth);
			Part(foot, new BoxMesh { Size = new Vector3(0.1f, 0.08f, 0.26f) }, new Vector3(0f, -0.04f, -0.06f), Metal);
			if (side > 0f)
			{
				_thighR = thigh;
				_shinR = shin;
				_footR = foot;
			}
			else
			{
				_thighL = thigh;
				_shinL = shin;
				_footL = foot;
			}
		}

		// Torso sits behind the camera plane (front face at z=+0.05) so it never fills the view
		// or covers the hands; looking down shows its top face, shoulders and the legs.
		Part(_hips, new BoxMesh { Size = new Vector3(0.34f, 0.2f, 0.2f) }, new Vector3(0f, 0f, 0.15f), Cloth);
		Part(_spine, new BoxMesh { Size = new Vector3(0.36f, 0.55f, 0.2f) }, new Vector3(0f, 0.12f, 0.15f), Cloth);
		// Head is hidden from the local camera but still casts a shadow.
		Part(head, new SphereMesh { Radius = 0.11f, Height = 0.22f }, Vector3.Zero, Skin, shadowOnly: true);
		_skel.ResetBonePoses();
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
	public (Vector3 Left, Vector3 Right) DebugHandPositions()
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
		return (Find("Hand_L"), Find("Hand_R"));
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

		_skel.SetBonePosePosition(_spine, _spineRest + new Vector3(leanShift * 0.5f, 0f, 0f));
		_skel.SetBonePoseRotation(_spine, new Quaternion(Vector3.Back, leanRoll));
	}

	private static Quaternion Pitch(float degrees) => new(Vector3.Right, Mathf.DegToRad(degrees));
}
