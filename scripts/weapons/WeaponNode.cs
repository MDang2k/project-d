using System;
using Godot;

namespace ProjectD;

/// <summary>
/// A held weapon: placeholder box model plus the documented marker set (GripRight, GripLeft,
/// Muzzle). Wraps <see cref="WeaponState"/>; the owner sets the trigger flags each physics frame.
/// </summary>
public partial class WeaponNode : Node3D
{
	public WeaponData Data { get; private set; } = null!;
	public WeaponState State { get; private set; } = null!;
	public WeaponController Controller { get; private set; } = null!;
	public Marker3D GripRight { get; private set; } = null!;
	public Marker3D GripLeft { get; private set; } = null!;
	public Marker3D Muzzle { get; private set; } = null!;
	public Marker3D BipodMount { get; private set; } = null!;

	private Node3D? _bipodLegs;

	/// <summary>Shows or hides the placeholder bipod legs.</summary>
	public bool BipodDeployed
	{
		get => _bipodLegs?.Visible ?? false;
		set
		{
			if (_bipodLegs is not null)
				_bipodLegs.Visible = value;
		}
	}

	public bool TriggerPressed { get; set; }
	public bool TriggerHeld { get; set; }
	/// <summary>False while sprinting, mantling or otherwise unable to shoot.</summary>
	public bool CanFire { get; set; } = true;

	/// <summary>Ammo, muzzle position, direction.</summary>
	public event Action<AmmoType, Vector3, Vector3>? ShotFired;

	/// <summary>Trigger pulled with nothing to fire (empty click).</summary>
	public event Action? DryFired;

	/// <summary>The inventory item this weapon is drawn from; its magazine and chamber live there.</summary>
	public ItemInstance? Item { get; private set; }

	public static WeaponNode Create(WeaponData data, ItemInstance? item = null)
	{
		var w = new WeaponNode { Name = "Weapon", Data = data };
		var ammo = data.Ammo?.ToType() ?? new AmmoType("generic", 40f, 2, 700f);
		w.State = new WeaponState(new FireControl(data.FireModes(), data.RoundsPerMinute));
		w.Controller = new WeaponController(w.State, data.ToActionTuning());
		w.Item = item;
		if (item is null)
			w.RefillDebug(ammo);
		else
			w.LoadFromItem();

		var size = data.BodySize;
		w._model = LoadModel(data.Id);
		if (w._model is null)
		{
			w.AddChild(new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = size },
				Position = new Vector3(0f, 0f, -size.Z * 0.5f + 0.1f),
				MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.13f) },
			});
			var sightMat = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f) };
			var sightY = size.Y * 0.5f + 0.0125f;
			w.AddChild(new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = new Vector3(0.012f, 0.025f, 0.012f) },
				Position = new Vector3(0f, sightY, 0.04f),
				MaterialOverride = sightMat,
			});
			w.AddChild(new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = new Vector3(0.008f, 0.025f, 0.008f) },
				Position = new Vector3(0f, sightY, -size.Z + 0.11f),
				MaterialOverride = sightMat,
			});
		}
		else
			w.AddChild(w._model);
		w.GripRight = new Marker3D
		{
			Name = "GripRight",
			Transform = w.ModelMarkerTransform("GripRight") ?? new Transform3D(new Basis(DefaultGripRight), data.GripRightOffset),
		};
		w.GripLeft = new Marker3D
		{
			Name = "GripLeft",
			Transform = w.ModelMarkerTransform("GripLeft") ?? new Transform3D(new Basis(DefaultGripLeft), data.GripLeftOffset),
		};
		w.Muzzle = new Marker3D { Name = "Muzzle", Position = w.ModelMarker("Muzzle") ?? new Vector3(0f, 0f, -size.Z + 0.1f) };
		w.BipodMount = new Marker3D
		{
			Name = "BipodMount",
			Position = w.ModelMarker("BipodMount") ?? new Vector3(0f, -size.Y * 0.5f, -size.Z * 0.7f + 0.1f),
		};
		w.AddChild(w.BipodMount);
		{
			// Two splayed legs reaching about 0.25 m down; hidden until deployed.
			var legs = new Node3D { Name = "BipodLegs", Visible = false, Position = w.BipodMount.Position };
			var legMat = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.1f) };
			foreach (var side in new[] { -1f, 1f })
			{
				legs.AddChild(new MeshInstance3D
				{
					Mesh = new BoxMesh { Size = new Vector3(0.015f, 0.26f, 0.015f) },
					Position = new Vector3(0.05f * side, -0.12f, 0f),
					RotationDegrees = new Vector3(0f, 0f, -12f * side),
					MaterialOverride = legMat,
				});
			}
			w._bipodLegs = legs;
			w.AddChild(legs);
		}
		w.AddChild(w.GripRight);
		w.AddChild(w.GripLeft);
		w.AddChild(w.Muzzle);
		w._baseMuzzle = w.Muzzle.Position;
		w._baseGripLeft = w.GripLeft.Position;
		w.CreateActionNodes();
		w.RefreshBuild();
		return w;
	}

	/// <summary>Restores the inserted magazine and chambered round from the weapon item.</summary>
	private void LoadFromItem()
	{
		if (Item?.InsertedMagazine is { } mag)
			State.InsertMagazine(InventoryMagazineStore.ToMagazine(mag));
		if (Item?.Chambered is { } id)
			State.SetChamber(ItemDb.AmmoTypeById(id) ?? new AmmoType(id, 40f, 2, 700f));
	}

	/// <summary>Writes the live magazine and chamber back onto the item so state survives swaps.</summary>
	private void SaveToItem()
	{
		if (Item is null)
			return;
		Item.Chambered = State.Chamber?.Id;
		Item.InsertedMagazine = State.Magazine is { } m
			? InventoryMagazineStore.FromMagazine(m, Data.Ammo?.Caliber)
			: null;
		if (State.Magazine is { } live && Item.InsertedMagazine is { } stored)
			live.Tag = stored;
	}

	/// <summary>
	/// Dev helper: full magazine, a chambered round and three spares (full, about half, almost
	/// empty) so reloads and magazine checks can be tried before the inventory exists.
	/// </summary>
	public void RefillDebug(AmmoType? ammo = null)
	{
		ammo ??= Data.Ammo?.ToType() ?? new AmmoType("generic", 40f, 2, 700f);
		State.EjectMagazine();
		State.InsertMagazine(Magazine.Full(Data.MagazineCapacity, ammo));
		State.ChamberRound();
		Controller.Pouch.Clear();
		var cap = Data.MagazineCapacity;
		foreach (var count in new[] { cap, cap / 2, Math.Max(2, cap / 6) })
		{
			var spare = new Magazine(cap);
			for (var i = 0; i < count; i++)
				spare.Push(ammo);
			Controller.Pouch.Add(spare);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		Controller.Update((float)delta);
		var ready = CanFire && !Controller.IsBusy;
		var pressed = ready && TriggerPressed;
		var held = ready && TriggerHeld;
		if (pressed && State.RoundsAvailable == 0)
			DryFired?.Invoke();
		foreach (var ammo in State.Update((float)delta, pressed, held))
			ShotFired?.Invoke(ammo, Muzzle.GlobalPosition, -Muzzle.GlobalBasis.Z);
		TriggerPressed = false;
		SaveToItem();
	}
}
