using Godot;

namespace ProjectD;

/// <summary>
/// M2 shooting range, built in code. Targets at 15/50/100/200 m along -Z.
/// Debug keys: 1-5 weapon (pistol, AK, FAL, EM-2, MG), Delete refills the magazine, Backspace respawns.
/// The overlay never shows ammo (GDD s18); it shows weapon, fire mode and last impact.
/// </summary>
public partial class M2Range : Node3D
{
	private static readonly string[] WeaponPaths =
	{
		"res://data/weapons/pistol.tres",
		"res://data/weapons/ak_type.tres",
		"res://data/weapons/fal_type.tres",
		"res://data/weapons/em2.tres",
		"res://data/weapons/mg_type.tres",
	};
	private static readonly float[] TargetDistances = { 15f, 50f, 100f, 200f };
	private static readonly Vector3 SpawnPoint = new(0f, 0.1f, 0f);

	private Player _player = null!;
	private Label _label = null!;
	private ProjectileManager _projectiles = null!;
	private string _lastImpact = "-";
	private int _hits;
	private static readonly float[] MarkDistances = { 25f, 50f, 100f, 150f, 200f };
	private bool _showTrajectory;
	private ImmediateMesh _previewMesh = null!;
	private MeshInstance3D _previewLine = null!;
	private readonly Label3D[] _marks = new Label3D[MarkDistances.Length];

	public override void _Ready()
	{
		BuildWorld();
		_projectiles = new ProjectileManager { Name = "Projectiles" };
		AddChild(_projectiles);
		_projectiles.Impact += OnImpact;

		var scene = GD.Load<PackedScene>("res://scenes/player/player.tscn");
		_player = scene.Instantiate<Player>();
		_player.Position = SpawnPoint;
		AddChild(_player);

		var layer = new CanvasLayer();
		_label = new Label { Position = new Vector2(12f, 12f) };
		layer.AddChild(_label);
		AddChild(layer);
		BuildTrajectoryDebug();
		ApplyDevArgs();
	}

	private void BuildTrajectoryDebug()
	{
		_previewMesh = new ImmediateMesh();
		_previewLine = new MeshInstance3D
		{
			Mesh = _previewMesh,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.2f, 1f, 0.4f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			TopLevel = true,
			Visible = false,
		};
		AddChild(_previewLine);
		for (var i = 0; i < _marks.Length; i++)
		{
			_marks[i] = new Label3D
			{
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				NoDepthTest = true,
				FontSize = 32,
				FixedSize = true,
				PixelSize = 0.0007f,
				Offset = new Vector2(70f, -45f * i),
				Modulate = new Color(0.2f, 1f, 0.4f),
				TopLevel = true,
				Visible = false,
			};
			AddChild(_marks[i]);
		}
	}

	private void SetTrajectoryDebug(bool on)
	{
		_showTrajectory = on;
		_projectiles.ShowTrajectories = on;
		_previewLine.Visible = on;
		foreach (var m in _marks)
			m.Visible = on;
	}

	/// <summary>Draws the predicted bullet path from the muzzle with drop labels (debug, F3).</summary>
	public override void _PhysicsProcess(double delta)
	{
		if (!_showTrajectory || _player.Weapon is not { } w)
			return;
		var ammo = w.State.Chamber ?? w.Data.Ammo?.ToType() ?? new AmmoType("generic", 40f, 2, 700f);
		var origin = w.Muzzle.GlobalPosition;
		var dir = -w.Muzzle.GlobalBasis.Z;
		var path = _projectiles.PredictPath(ammo, origin, dir, _player.GetRid(), out _);
		ProjectileManager.FillLine(_previewMesh, path);

		for (var i = 0; i < _marks.Length; i++)
		{
			var d = MarkDistances[i];
			var found = false;
			foreach (var p in path)
			{
				if (origin.DistanceTo(p) < d)
					continue;
				var bore = origin + dir.Normalized() * d;
				_marks[i].GlobalPosition = p + Vector3.Up * 0.25f;
				_marks[i].Text = $"{d:0} m  drop {(bore.Y - p.Y) * 100f:0} cm";
				found = true;
				break;
			}
			_marks[i].Visible = found;
		}
	}

	/// <summary>
	/// Dev screenshots: godot ... -- --weapon=3 --fire=1.5 --shot=C:/x.png
	/// equips weapon N, holds the trigger for the given seconds, saves a screenshot and quits.
	/// </summary>
	private async void ApplyDevArgs()
	{
		string? shot = null;
		var fireSeconds = 0.0;
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--shot="))
				shot = arg["--shot=".Length..];
			else if (arg == "--traj")
				SetTrajectoryDebug(true);
			else if (arg.StartsWith("--fire=") && double.TryParse(arg["--fire=".Length..], out var f))
				fireSeconds = f;
			else if (arg.StartsWith("--weapon=") && int.TryParse(arg["--weapon=".Length..], out var w)
				&& w >= 1 && w <= WeaponPaths.Length)
				_player.EquipWeapon(GD.Load<WeaponData>(WeaponPaths[w - 1]));
		}
		if (fireSeconds > 0.0)
		{
			await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
			_player.DebugForceTrigger = true;
			await ToSignal(GetTree().CreateTimer(fireSeconds), SceneTreeTimer.SignalName.Timeout);
			_player.DebugForceTrigger = false;
		}
		if (shot is null)
			return;
		await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
		GetViewport().GetTexture().GetImage().SavePng(shot);
		GD.Print($"IMPACT {_lastImpact} hits={_hits}");
		GetTree().Quit();
	}

	private void BuildWorld()
	{
		Box(new Vector3(0f, -0.5f, -100f), new Vector3(60f, 1f, 260f), new Color(0.3f, 0.35f, 0.25f));
		Box(new Vector3(0f, 4f, -235f), new Vector3(60f, 8f, 4f), new Color(0.4f, 0.32f, 0.25f));
		// Targets sit off the centre line (alternating sides) so a straight shot is never blocked:
		// the trajectory debug can then show drop labels out to the backstop.
		for (var i = 0; i < TargetDistances.Length; i++)
		{
			var d = TargetDistances[i];
			var x = (i % 2 == 0 ? -1f : 1f) * (2.5f + i);
			var t = Box(new Vector3(x, 0.9f, -d), new Vector3(0.6f, 1.8f, 0.1f), new Color(0.75f, 0.2f, 0.2f));
			t.SetMeta("target", true);
			Box(new Vector3(0f, 0.05f, -d), new Vector3(0.3f, 0.1f, 0.3f), new Color(0.9f, 0.8f, 0.2f));
		}
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, 30f, 0f), ShadowEnabled = true });
		AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color(0.55f, 0.7f, 0.9f),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color(0.6f, 0.65f, 0.7f),
			},
		});
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

	private void OnImpact(BulletImpact hit)
	{
		var marker = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.9f, 0.2f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
			Position = hit.Position,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(marker);
		GetTree().CreateTimer(8.0).Timeout += marker.QueueFree;

		_lastImpact = $"{hit.TravelDistance:0} m, speed {hit.Velocity.Length():0} m/s";
		if (hit.Collider is Node node && node.HasMeta("target"))
		{
			_hits++;
			_lastImpact += " TARGET";
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } k)
			return;
		var index = (int)k.PhysicalKeycode - (int)Key.Key1;
		if (index >= 0 && index < WeaponPaths.Length)
		{
			_player.EquipWeapon(GD.Load<WeaponData>(WeaponPaths[index]));
			return;
		}
		switch (k.PhysicalKeycode)
		{
			case Key.F3:
				SetTrajectoryDebug(!_showTrajectory);
				break;
			case Key.Delete:
				_player.Weapon?.RefillDebug();
				break;
			case Key.Backspace:
				_player.Position = SpawnPoint;
				_player.Velocity = Vector3.Zero;
				break;
		}
	}

	public override void _Process(double delta)
	{
		var w = _player.Weapon;
		var name = w is null ? "none" : Tr(w.Data.NameKey);
		var mode = w?.State.Fire.Mode.ToString() ?? "-";
		_label.Text =
			$"{name}  mode {mode}  stance {_player.Stance}{(_player.IsSprinting ? "  SPRINT" : "")}\n" +
			$"last impact: {_lastImpact}   target hits {_hits}\n" +
			"1-5 weapon  V fire mode  Delete refill (debug)  F3 trajectory debug " + (_showTrajectory ? "ON" : "off") + "  Backspace respawn";
	}
}
