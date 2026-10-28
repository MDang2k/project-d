using Godot;

namespace ProjectD;

/// <summary>
/// The weapon side of weapon actions (animation spec s1.3): the point the support hand follows (moves away from
/// the grip during reloads) and the magazine shown in that hand. Which phase is playing comes from the controller.
/// </summary>
public partial class WeaponNode
{
	private Node3D? _heldMag;
	private string _heldMagId = "";

	/// <summary>The support hand's target. Sits on <see cref="GripLeft"/> while idle; the player moves it during actions.</summary>
	public Marker3D LeftHandAction { get; private set; } = null!;

	/// <summary>Where the weapon model's magazine well, charging handle and grip are, weapon space.</summary>
	public Vector3 MagWellPoint => WeaponMarker("MagWell");
	public Vector3? ChargingPoint => ModelMarker("ChargingHandle");

	private void CreateActionNodes()
	{
		LeftHandAction = new Marker3D { Name = "LeftHandAction", Position = GripLeft.Position, Rotation = GripLeft.Rotation };
		AddChild(LeftHandAction);
	}

	/// <summary>Keeps a magazine model in the support hand, matching the weapon's current magazine item.</summary>
	private void EnsureHeldMagazine()
	{
		var id = Item?.InsertedMagazine?.Spec.Id ?? ItemDb.MagazineFor(Data.Ammo?.Caliber ?? "")?.Id ?? "";
		if (id == _heldMagId && _heldMag is not null)
			return;
		_heldMag?.QueueFree();
		_heldMag = null;
		_heldMagId = id;
		if (id == "" || LoadModel(id, "parts") is not { } model)
			return;
		_heldMag = new Node3D { Name = "HeldMagazine", Visible = false };
		model.Position = new Vector3(0f, 0.09f, 0f); // the hand holds the body, the top (Attach) points up
		_heldMag.AddChild(model);
		LeftHandAction.AddChild(_heldMag);
	}

	/// <summary>Shows the magazine in the hand and hides the one in the weapon while the reload has it out.</summary>
	public void SetReloadVisual(ReloadPhase phase, float progress)
	{
		var inHand = phase switch
		{
			ReloadPhase.MagDetach => progress > 0.5f,
			ReloadPhase.MagStow or ReloadPhase.MagFetch or ReloadPhase.MagInsert => true,
			_ => false,
		};
		if (_heldMag is not null)
			_heldMag.Visible = inHand;
		if (_magModel is not null)
			_magModel.Visible = !inHand;
	}
}
