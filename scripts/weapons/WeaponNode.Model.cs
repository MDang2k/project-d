using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// Real 3D models for a weapon and its parts (asset pipeline spec): assets/models/weapons/{id}.glb for the weapon,
/// assets/models/parts/{id}.glb for parts and magazines. A model brings its own markers (empties named as the
/// spec lists); anything without a model keeps the placeholder box.
/// </summary>
public partial class WeaponNode
{
	private Node3D? _model;
	private readonly Dictionary<ItemInstance, Node3D> _partNodes = new();

	/// <summary>Loads and instantiates a model; null when the file is missing.</summary>
	private static Node3D? LoadModel(string id, string folder = "weapons")
	{
		var path = $"res://assets/models/{folder}/{id}.glb";
		return ResourceLoader.Exists(path) && GD.Load<PackedScene>(path) is { } scene ? scene.Instantiate<Node3D>() : null;
	}

	/// <summary>Position of a named empty in the weapon model, or null (no model, or no such marker).</summary>
	private Vector3? ModelMarker(string name)
		=> _model?.FindChild(name, true, false) is Node3D n ? n.Position : null;

	/// <summary>Position and rotation of a named empty (grip markers carry a rotation, pipeline spec).</summary>
	private Transform3D? ModelMarkerTransform(string name)
		=> _model?.FindChild(name, true, false) is Node3D n ? n.Transform : null;

	// Grip rotations for weapons without a model (grip convention: -Z along the grip from pinky to index,
	// +Y out of the back of the hand). Right: pistol grip raked 35 deg (as on the FAL), back of the hand to the right.
	// Left: under the handguard, -Z to the muzzle, back of the hand down-left.
	private static readonly Quaternion DefaultGripRight = new(0.32651f, 0.32651f, -0.62721f, 0.62721f);
	private static readonly Quaternion DefaultGripLeft = new(0f, 0f, 0.9239f, 0.3827f);

	/// <summary>Bounds of the weapon model (or the placeholder body), weapon space; for centring in the gunsmith view.</summary>
	public Aabb ModelBounds
	{
		get
		{
			if (_model is null)
			{
				var s = Data.BodySize;
				return new Aabb(new Vector3(-s.X / 2f, -s.Y / 2f, -s.Z + 0.1f), s);
			}
			Aabb? total = null;
			foreach (var child in _model.FindChildren("*", "MeshInstance3D", true, false))
			{
				var mi = (MeshInstance3D)child;
				var box = mi.Transform * mi.GetAabb();
				total = total is null ? box : total.Value.Merge(box);
			}
			return total ?? new Aabb(Vector3.Zero, new Vector3(0.1f, 0.1f, 1f));
		}
	}

	/// <summary>Marker point for a slot on the weapon; models without the empty fall back to a body-relative guess.</summary>
	private Vector3 WeaponMarker(string marker)
	{
		if (ModelMarker(marker) is { } p)
			return p;
		if (_model is not null && marker == "OpticMount" && ModelMarker("Upper") is { } upper)
			return upper;
		return MarkerPositionFallback(marker);
	}

	/// <summary>Where a slot's part sits in weapon space: the owning part's own marker, or the weapon's.</summary>
	private Vector3 SlotPoint(SlotEntry slot)
	{
		if (!ReferenceEquals(slot.Owner, Item) && _partNodes.TryGetValue(slot.Owner, out var node)
			&& node.FindChild(slot.Spec.Marker, true, false) is Node3D marker)
			return node.Position + marker.Position;
		return WeaponMarker(slot.Spec.Marker);
	}

	/// <summary>Instantiates a part model at a marker of its parent (weapon model or an attached part).</summary>
	private Node3D? PlacePartModel(ItemInstance part, WeaponSlotSpec slot, ItemInstance owner)
	{
		if (slot.Marker == "" || LoadModel(part.Spec.Id, "parts") is not { } inst)
			return null;
		Node3D parent;
		Vector3 at;
		var rot = Vector3.Zero;
		if (!ReferenceEquals(owner, Item) && _partNodes.TryGetValue(owner, out var ownerNode)
			&& ownerNode.FindChild(slot.Marker, true, false) is Node3D marker)
		{
			parent = ownerNode;
			at = marker.Position;
			rot = marker.Rotation;
		}
		else
		{
			parent = _partsRoot!;
			at = WeaponMarker(slot.Marker);
		}
		inst.Position = at;
		inst.Rotation = rot;
		parent.AddChild(inst);
		_partNodes[part] = inst;
		return inst;
	}

	/// <summary>Shows the magazine model at the MagWell while one is inserted.</summary>
	private void PlaceMagazineModel()
	{
		if (Item?.InsertedMagazine is not { } mag || LoadModel(mag.Spec.Id, "parts") is not { } inst)
			return;
		inst.Position = WeaponMarker("MagWell");
		_partsRoot!.AddChild(inst);
	}

	/// <summary>The integral flash hider of a model hides when a muzzle device is fitted.</summary>
	private void SyncIntegralParts(bool muzzleFitted)
	{
		if (_model?.FindChild("FlashHider", true, false) is Node3D hider)
			hider.Visible = !muzzleFitted;
	}
}
