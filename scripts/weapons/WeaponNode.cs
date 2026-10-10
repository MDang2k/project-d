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
	public Marker3D GripRight { get; private set; } = null!;
	public Marker3D GripLeft { get; private set; } = null!;
	public Marker3D Muzzle { get; private set; } = null!;

	public bool TriggerPressed { get; set; }
	public bool TriggerHeld { get; set; }
	/// <summary>False while sprinting, mantling or otherwise unable to shoot.</summary>
	public bool CanFire { get; set; } = true;

	/// <summary>Ammo, muzzle position, direction.</summary>
	public event Action<AmmoType, Vector3, Vector3>? ShotFired;

	public static WeaponNode Create(WeaponData data)
	{
		var w = new WeaponNode { Name = "Weapon", Data = data };
		var ammo = data.Ammo?.ToType() ?? new AmmoType("generic", 40f, 2, 700f);
		w.State = new WeaponState(new FireControl(data.FireModes(), data.RoundsPerMinute));
		w.RefillDebug(ammo);

		var size = data.BodySize;
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
		w.GripRight = new Marker3D { Name = "GripRight", Position = data.GripRightOffset };
		w.GripLeft = new Marker3D { Name = "GripLeft", Position = data.GripLeftOffset };
		w.Muzzle = new Marker3D { Name = "Muzzle", Position = new Vector3(0f, 0f, -size.Z + 0.1f) };
		w.AddChild(w.GripRight);
		w.AddChild(w.GripLeft);
		w.AddChild(w.Muzzle);
		return w;
	}

	/// <summary>Dev helper: full magazine and a chambered round.</summary>
	public void RefillDebug(AmmoType? ammo = null)
	{
		ammo ??= Data.Ammo?.ToType() ?? new AmmoType("generic", 40f, 2, 700f);
		State.InsertMagazine(Magazine.Full(Data.MagazineCapacity, ammo));
		State.ChamberRound();
	}

	public override void _PhysicsProcess(double delta)
	{
		var pressed = CanFire && TriggerPressed;
		var held = CanFire && TriggerHeld;
		foreach (var ammo in State.Update((float)delta, pressed, held))
			ShotFired?.Invoke(ammo, Muzzle.GlobalPosition, -Muzzle.GlobalBasis.Z);
		TriggerPressed = false;
	}
}
