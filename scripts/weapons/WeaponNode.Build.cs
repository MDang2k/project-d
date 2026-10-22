using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Weapon modding on the node (weapon modding spec): final stats from the build, and placeholder part
/// meshes at the slot markers. The muzzle marker moves to the end of any muzzle device so bullets
/// leave from there.
/// </summary>
public partial class WeaponNode
{
	private Node3D? _partsRoot;
	private readonly System.Collections.Generic.List<MeshInstance3D> _opticMeshes = new();
	private string _buildSignature = "";
	private Vector3 _baseMuzzle;

	/// <summary>Final handling numbers of the weapon with its parts (neutral when it has no inventory item).</summary>
	public WeaponStats Stats { get; private set; } = null!;

	/// <summary>Integrated bipod (MG) or a clamp-on bipod part.</summary>
	public bool HasBipod => Stats.HasBipod;

	/// <summary>Recoil of the base weapon scaled by the build's recoil modifiers.</summary>
	public RecoilTuning RecoilTuning
	{
		get
		{
			var t = Data.ToRecoilTuning();
			return t with { VerticalDeg = t.VerticalDeg * Stats.VerticalRecoilMult, HorizontalDeg = t.HorizontalDeg * Stats.HorizontalRecoilMult };
		}
	}

	/// <summary>How far a shot carries, metres, after muzzle devices.</summary>
	public float NoiseRadius => Data.NoiseRadius * Stats.NoiseMult;

	/// <summary>Sight line height above the weapon origin; an optic sits higher than iron sights.</summary>
	public float SightHeight => Data.SightHeight + (Stats.Zoom > 0f ? 0.035f : 0f);

	private WeaponStats NeutralStats() => new(50f, 50f, 50f, 50f, Data.Ergonomics, 1f, 1f, 0f, Data.WeightKg, 0, 0,
		Data.HasBipod, false);

	/// <summary>Recomputes the stats and rebuilds the part meshes when the attached parts changed.</summary>
	public void RefreshBuild()
	{
		Stats = Item is null ? NeutralStats() : WeaponBuild.Stats(Item);
		var signature = Item is null
			? ""
			: string.Join(",", WeaponBuild.Slots(Item).Where(s => s.Part is not null).Select(s => $"{s.Spec.Id}={s.Part!.Spec.Id}"));
		if (signature == _buildSignature)
			return;
		_buildSignature = signature;
		RebuildParts();
	}

	/// <summary>Where a slot marker (Muzzle, OpticMount, Stock, ...) sits on the model, weapon space.</summary>
	public Vector3 MarkerPosition(string marker)
	{
		var size = Data.BodySize;
		return marker switch
		{
			"OpticMount" => new Vector3(0f, size.Y * 0.5f + 0.01f, -size.Z * 0.35f + 0.1f),
			"Stock" => new Vector3(0f, 0f, 0.14f),
			"Handguard" => new Vector3(0f, -0.01f, -size.Z * 0.6f + 0.1f),
			"Light" => new Vector3(0.035f, -size.Y * 0.5f, -size.Z * 0.75f + 0.1f),
			"BipodMount" => BipodMount.Position,
			_ => _baseMuzzle,
		};
	}

	private void RebuildParts()
	{
		_partsRoot?.QueueFree();
		_opticMeshes.Clear();
		_partsRoot = new Node3D { Name = "Parts" };
		AddChild(_partsRoot);
		var muzzleExtra = 0f;
		if (Item is not null)
		{
			foreach (var slot in WeaponBuild.Slots(Item).Where(s => s.Part is not null))
			{
				var part = slot.Part!;
				var width = Mathf.Max(1, part.Spec.Width);
				switch (slot.Spec.Type)
				{
					case "muzzle":
						AddPartMesh(part, new Vector3(0.03f, 0.03f, 0.05f * width), MarkerPosition("Muzzle") + new Vector3(0f, 0f, -0.025f * width));
						muzzleExtra += 0.05f * width;
						break;
					case "optic":
						_opticMeshes.Add(AddPartMesh(part, new Vector3(0.04f, 0.05f, 0.04f * width + 0.06f),
							MarkerPosition(slot.Spec.Marker) + new Vector3(0f, 0.035f, 0f)));
						break;
					case "mount":
						AddPartMesh(part, new Vector3(0.035f, 0.02f, 0.07f), MarkerPosition(slot.Spec.Marker));
						break;
					case "underbarrel":
						AddPartMesh(part, new Vector3(0.03f, 0.03f, 0.05f), MarkerPosition("BipodMount"));
						break;
					case "light":
						AddPartMesh(part, new Vector3(0.03f, 0.03f, 0.08f), MarkerPosition("Light"));
						break;
				}
			}
		}
		Muzzle.Position = _baseMuzzle + new Vector3(0f, 0f, -muzzleExtra);
	}

	private MeshInstance3D AddPartMesh(ItemInstance part, Vector3 size, Vector3 position)
	{
		var color = ItemDb.Data(part.Spec.Id)?.IconColor ?? new Color(0.25f, 0.25f, 0.28f);
		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			Position = position,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = color.Darkened(0.2f) },
		};
		_partsRoot!.AddChild(mesh);
		return mesh;
	}

	/// <summary>While aiming through a magnifying optic its model is hidden (the scope overlay stands in for it).</summary>
	public void SetAds(float amount)
	{
		var hide = Stats.Zoom > 0f && amount > 0.5f;
		foreach (var m in _opticMeshes)
			m.Visible = !hide;
	}
}
