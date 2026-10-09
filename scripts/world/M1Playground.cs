using Godot;

namespace ProjectD;

/// <summary>
/// M1 movement test yard, built in code. Debug keys: [ / ] carried weight -/+ 5 kg,
/// Backspace respawn. Overlay shows speed, stance, weight tier and stamina.
/// </summary>
public partial class M1Playground : Node3D
{
	private const float WeightStepKg = 5f;
	private static readonly Vector3 SpawnPoint = new(0f, 0.1f, 0f);

	private Player _player = null!;
	private Label _label = null!;

	public override void _Ready()
	{
		BuildWorld();
		var scene = GD.Load<PackedScene>("res://scenes/player/player.tscn");
		_player = scene.Instantiate<Player>();
		_player.Position = SpawnPoint;
		AddChild(_player);

		var layer = new CanvasLayer();
		_label = new Label { Position = new Vector2(12f, 12f) };
		layer.AddChild(_label);
		AddChild(layer);
	}

	private void BuildWorld()
	{
		Box(new Vector3(0f, -0.5f, 0f), new Vector3(120f, 1f, 120f), new Color(0.3f, 0.35f, 0.25f));
		// Low walls and ledges (mantle targets).
		Box(new Vector3(-6f, 0.25f, -8f), new Vector3(4f, 0.5f, 0.5f), new Color(0.6f, 0.55f, 0.5f));
		Box(new Vector3(-6f, 0.5f, -11f), new Vector3(4f, 1f, 0.5f), new Color(0.6f, 0.55f, 0.5f));
		// Too high to mantle (1.6 m), and a pillar to lean around.
		Box(new Vector3(-11f, 0.8f, -8f), new Vector3(4f, 1.6f, 0.5f), new Color(0.6f, 0.55f, 0.5f));
		Box(new Vector3(3f, 1.5f, -4f), new Vector3(1f, 3f, 1f), new Color(0.45f, 0.45f, 0.5f));
		// Crouch tunnel: 1.4 m clearance (standing 1.8 m does not fit, crouching 1.2 m does).
		Arch(new Vector3(0f, 0f, -10f), 1.4f);
		// Prone tunnel: 0.8 m clearance (only prone 0.6 m fits).
		Arch(new Vector3(6f, 0f, -10f), 0.8f);
		// Ramp.
		var ramp = Box(new Vector3(12f, 0.9f, -8f), new Vector3(4f, 0.3f, 10f), new Color(0.5f, 0.5f, 0.55f));
		ramp.RotationDegrees = new Vector3(-15f, 0f, 0f);
		// Run lane markers every 10 m.
		for (var i = 0; i < 6; i++)
			Box(new Vector3(-2f, 0.05f, -10f * (i + 1) - 20f), new Vector3(0.3f, 0.1f, 0.3f), new Color(0.9f, 0.8f, 0.2f));

		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, 30f, 0f), ShadowEnabled = true });
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.55f, 0.7f, 0.9f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.6f, 0.65f, 0.7f),
		};
		AddChild(new WorldEnvironment { Environment = env });
	}

	/// <summary>Two pillars plus a lintel whose underside sits at `clearance` metres.</summary>
	private void Arch(Vector3 basePos, float clearance)
	{
		var c = new Color(0.5f, 0.45f, 0.4f);
		Box(basePos + new Vector3(-2f, clearance * 0.5f, 0f), new Vector3(0.6f, clearance, 3f), c);
		Box(basePos + new Vector3(2f, clearance * 0.5f, 0f), new Vector3(0.6f, clearance, 3f), c);
		Box(basePos + new Vector3(0f, clearance + 0.25f, 0f), new Vector3(4.6f, 0.5f, 3f), c);
	}

	private StaticBody3D Box(Vector3 pos, Vector3 size, Color color)
	{
		var body = new StaticBody3D { Position = pos };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		var mat = new StandardMaterial3D { AlbedoColor = color };
		body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = mat } });
		AddChild(body);
		return body;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } k)
			return;
		switch (k.PhysicalKeycode)
		{
			case Key.Bracketleft:
				_player.CarriedWeightKg = Mathf.Max(0f, _player.CarriedWeightKg - WeightStepKg);
				break;
			case Key.Bracketright:
				_player.CarriedWeightKg += WeightStepKg;
				break;
			case Key.Backspace:
				_player.Position = SpawnPoint;
				_player.Velocity = Vector3.Zero;
				break;
		}
	}

	public override void _Process(double delta)
	{
		var h = new Vector2(_player.Velocity.X, _player.Velocity.Z).Length();
		_label.Text =
			$"speed {h:0.00} m/s  stance {_player.Stance}{(_player.IsSprinting ? "  SPRINT" : "")}\n" +
			$"weight {_player.CarriedWeightKg:0} kg  tier {_player.Tier}\n" +
			$"legs {_player.Legs.Fraction * 100f:0}%{(_player.Legs.IsExhausted ? "  EXHAUSTED" : "")}  arms {_player.Arms.Fraction * 100f:0}%\n" +
			"[ ] weight  Backspace respawn";
	}
}
