using Godot;

namespace ProjectD;

/// <summary>
/// 3D view of the weapon on the bench (weapon modding v2 UI): the real <see cref="WeaponNode"/> placeholder model
/// with its parts, in a transparent viewport. Drag with the left or middle mouse button to rotate, wheel to
/// zoom, double-click to reset. Reports where slot pins project on screen so the board can tether to them.
/// </summary>
public partial class WeaponViewport : SubViewportContainer
{
	private const float RotateDegPerPx = 0.4f;

	private SubViewport _viewport = null!;
	private Node3D _pivot = null!;
	private Camera3D _camera = null!;
	private WeaponNode? _node;
	private float _yaw;
	private float _pitch;
	private float _distance = 1.2f;
	private float _defaultDistance = 1.2f;
	private Vector3 _size = new(0.05f, 0.1f, 0.8f);
	private bool _dragging;
	private string _shownId = "";

	public WeaponViewport()
	{
		Stretch = true;
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsPreset(LayoutPreset.FullRect);
	}

	public override void _Ready()
	{
		_viewport = new SubViewport { TransparentBg = true, OwnWorld3D = true, HandleInputLocally = false };
		AddChild(_viewport);
		_pivot = new Node3D();
		_viewport.AddChild(_pivot);
		_camera = new Camera3D { Fov = 30f, Current = true };
		_viewport.AddChild(_camera);
		_viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40f, -35f, 0f), LightEnergy = 1.1f });
		_viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(30f, 140f, 0f), LightEnergy = 0.5f });
		var env = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.45f, 0.47f, 0.52f), AmbientLightEnergy = 1f };
		_viewport.AddChild(new WorldEnvironment { Environment = env });
		ResetView();
		GuiInput += OnGuiInput;
	}

	/// <summary>Shows the weapon item with its current parts (rebuilt each call).</summary>
	public void ShowWeapon(ItemInstance weapon)
	{
		_node?.QueueFree();
		_node = null;
		if (ItemDb.Data(weapon.Spec.Id)?.Payload is not WeaponData data)
			return;
		_size = data.BodySize;
		_defaultDistance = Mathf.Max(2.0f, _size.Z * 4.4f);
		if (_shownId != weapon.Spec.Id)
		{
			_shownId = weapon.Spec.Id;
			_yaw = 0f;
			_pitch = 0f;
			_distance = _defaultDistance;
		}
		_node = WeaponNode.Create(data, weapon);
		// centre the model on the pivot so rotation spins it around its middle
		_node.Position = new Vector3(0f, 0f, _size.Z * 0.5f - 0.1f);
		_pivot.AddChild(_node);
		ApplyCamera();
	}

	public void ResetView()
	{
		_yaw = 0f;
		_pitch = 0f;
		_distance = _defaultDistance;
		ApplyCamera();
	}

	private void ApplyCamera()
	{
		if (_camera is null)
			return;
		_distance = Mathf.Clamp(_distance, _defaultDistance * 0.25f, _defaultDistance * 2f);
		// camera on the weapon's left side: the muzzle points to screen left
		_camera.Position = new Vector3(-_distance, 0f, 0f);
		_camera.LookAt(Vector3.Zero, Vector3.Up);
		_pivot.Rotation = new Vector3(Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0f);
	}

	private void OnGuiInput(InputEvent ev)
	{
		switch (ev)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
				_distance *= 0.9f;
				ApplyCamera();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
				_distance *= 1.1f;
				ApplyCamera();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle } b:
				_dragging = b.Pressed;
				if (b is { Pressed: true, DoubleClick: true })
					ResetView();
				break;
			case InputEventMouseMotion m when _dragging:
				_yaw += m.Relative.X * RotateDegPerPx;
				_pitch = Mathf.Clamp(_pitch + m.Relative.Y * RotateDegPerPx, -80f, 80f);
				ApplyCamera();
				break;
		}
	}

	/// <summary>Where a point in weapon space (0,0,0 = receiver origin) lands in this control; null when behind the camera.</summary>
	public Vector2? Project(Vector3 weaponLocal)
	{
		if (_node is null || _camera is null)
			return null;
		var world = _node.ToGlobal(weaponLocal);
		if (_camera.IsPositionBehind(world))
			return null;
		var inViewport = _camera.UnprojectPosition(world);
		var scale = Size / new Vector2(_viewport.Size.X, _viewport.Size.Y);
		return inViewport * scale;
	}

	/// <summary>Weapon-space point of a slot's anchor pin: its model marker, or a spot along the body by slot type.</summary>
	public Vector3 PinLocal(WeaponSlotSpec slot, Vector2 fraction)
	{
		if (_node is not null && slot.Marker != "")
			return _node.MarkerPosition(slot.Marker);
		return new Vector3(0f, _size.Y * (0.5f - fraction.Y), 0.1f - _size.Z * (1f - fraction.X));
	}
}
