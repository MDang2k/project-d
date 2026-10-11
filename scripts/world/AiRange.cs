using System.Linq;
using System.Text;
using Godot;

namespace ProjectD;

/// <summary>
/// M5 AI test range, built in code: open ground, cover walls and one AI of each faction preset.
/// Keys: F4 toggles AI debug (cones and awareness labels), Backspace respawns the player.
/// Dev args after "--": --pos=x,z --yaw=deg --crouch --prone --fire=secs --wait=secs --report --shot=path.
/// </summary>
public partial class AiRange : Node3D
{
	private static readonly string[] PresetIds = { "looter", "bandit", "allied_pmc", "soviet_pmc" };
	private static readonly Vector3 SpawnPoint = new(0f, 0.1f, 0f);

	private Player _player = null!;
	private Label _label = null!;
	private ProjectileManager _projectiles = null!;
	private bool _debug = true;
	private string _lastNoise = "-";

	public override void _Ready()
	{
		BuildWorld();
		_projectiles = new ProjectileManager { Name = "Projectiles" };
		AddChild(_projectiles);

		var scene = GD.Load<PackedScene>("res://scenes/player/player.tscn");
		_player = scene.Instantiate<Player>();
		_player.Position = SpawnPoint;
		AddChild(_player);

		// Dev: --only=bandit keeps a single AI so one faction's senses can be measured alone.
		var only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--only="))?["--only=".Length..];
		var standStill = OS.GetCmdlineUserArgs().Contains("--stand");
		var standSolo = OS.GetCmdlineUserArgs().Contains("--solo");
		var spots = new[]
		{
			(new Vector3(10f, 0.1f, -18f), 150f), (new Vector3(-12f, 0.1f, -26f), 200f),
			(new Vector3(16f, 0.1f, -34f), 170f), (new Vector3(-6f, 0.1f, -48f), 180f),
		};
		for (var i = 0; i < PresetIds.Length; i++)
		{
			if (only is not null && only != PresetIds[i])
				continue;
			// Each AI patrols a small loop around its start point (or stands with --stand).
			var c = spots[i].Item1;
			var patrol = standStill
				? System.Array.Empty<Vector3>()
				: new[] { c, c + new Vector3(8f, 0f, 0f), c + new Vector3(8f, 0f, -8f), c + new Vector3(0f, 0f, -8f) };
			SpawnAi(PresetIds[i], c, spots[i].Item2, patrol);
			// A partner standing close by so pairs can flank together.
			if (!standSolo)
				SpawnAi(PresetIds[i], c + new Vector3(2.5f, 0f, 1.5f), spots[i].Item2, patrol, "_2");
		}

		NoiseBus.Noise += n => _lastNoise = $"{n.Kind} ({n.Radius:0} m)";
		var layer = new CanvasLayer();
		_label = new Label { Position = new Vector2(12f, 12f) };
		layer.AddChild(_label);
		AddChild(layer);
		ApplyDevArgs();
	}

	private void SpawnAi(string presetId, Vector3 position, float yawDeg, Vector3[] patrol, string suffix = "")
	{
		var ai = new AiHuman
		{
			Name = presetId + suffix,
			Preset = GD.Load<AiPreset>($"res://data/ai/{presetId}.tres"),
			Position = position,
			RotationDegrees = new Vector3(0f, yawDeg, 0f),
			PatrolPoints = patrol,
		};
		AddChild(ai);
	}

	private NavigationRegion3D _nav = null!;

	private StaticBody3D Box(Vector3 pos, Vector3 size, Color color)
	{
		var body = new StaticBody3D { Position = pos };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		body.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
		});
		_nav.AddChild(body);
		return body;
	}

	private void BuildWorld()
	{
		_nav = new NavigationRegion3D
		{
			NavigationMesh = new NavigationMesh
			{
				AgentRadius = 0.5f,
				AgentHeight = 2f,
				AgentMaxClimb = 0.25f,
				GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
				CellSize = 0.25f,
				CellHeight = 0.25f,
			},
		};
		AddChild(_nav);
		Box(new Vector3(0f, -0.5f, -40f), new Vector3(120f, 1f, 160f), new Color(0.3f, 0.35f, 0.25f));
		var cover = new Color(0.55f, 0.5f, 0.4f);
		// A wall hiding the second bandit from the spawn, low walls to crouch behind, a window frame.
		Box(new Vector3(-6f, 1.25f, -14f), new Vector3(8f, 2.5f, 0.5f), new Color(0.5f, 0.45f, 0.45f));
		Box(new Vector3(4f, 0.45f, -8f), new Vector3(4f, 0.9f, 0.5f), cover);
		Box(new Vector3(-3f, 0.45f, -5f), new Vector3(3f, 0.9f, 0.5f), cover);
		Box(new Vector3(8f, 1.25f, -26f), new Vector3(0.5f, 2.5f, 10f), new Color(0.5f, 0.45f, 0.45f));
		Box(new Vector3(-10f, 0.6f, -36f), new Vector3(5f, 1.2f, 0.5f), cover);
		_nav.BakeNavigationMesh(false);
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, 30f, 0f), ShadowEnabled = true });
		AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color(0.55f, 0.7f, 0.9f),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color(0.6f, 0.6f, 0.65f),
			},
		});
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key)
			return;
		switch (key.Keycode)
		{
			case Key.F4:
				_debug = !_debug;
				foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>())
					ai.ShowDebug = _debug;
				break;
			case Key.Backspace:
				_player.Position = SpawnPoint;
				_player.Velocity = Vector3.Zero;
				_player.Health.Reset();
				break;
		}
	}

	private readonly System.Collections.Generic.Dictionary<AiHuman, string> _trace = new();
	private readonly bool _traceOn = OS.GetCmdlineUserArgs().Contains("--trace");

	public override void _Process(double delta)
	{
		if (_traceOn)
		{
			foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>())
			{
				var now = $"{ai.Brain.State}/{ai.CombatState}";
				if (_trace.TryGetValue(ai, out var old) && old == now)
					continue;
				_trace[ai] = now;
				GD.Print($"TRACE {Time.GetTicksMsec() / 1000.0:0.0}s {ai.Preset?.Id} {now}");
			}
		}
		var sb = new StringBuilder();
		var w = _player.Weapon;
		sb.AppendLine($"you: {_player.Faction}  stance {_player.Stance}{(_player.IsSprinting ? " sprinting" : "")}  hp {(_player.IsDead ? "DEAD" : "ok")}");
		sb.AppendLine($"last noise: {_lastNoise}");
		foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>())
		{
			var d = ai.GlobalPosition.DistanceTo(_player.GlobalPosition);
			sb.AppendLine($"{ai.Preset?.Id,-11} {d,5:0} m  {ai.Perception.Level,-10} {ai.Perception.Awareness * 100f,3:0}%  {ai.LastCue}");
		}
		sb.Append("V fire  RMB aim  Shift sprint  C crouch  Z prone  F4 AI debug " + (_debug ? "ON" : "off") + "  Backspace respawn");
		_label.Text = sb.ToString();
		_ = w;
	}

	private async void ApplyDevArgs()
	{
		string? shot = null;
		var wait = 0.0;
		var fire = 0.0;
		var report = false;
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--shot="))
				shot = arg["--shot=".Length..];
			else if (arg.StartsWith("--wait=") && double.TryParse(arg["--wait=".Length..], out var w))
				wait = w;
			else if (arg.StartsWith("--fire=") && double.TryParse(arg["--fire=".Length..], out var f))
				fire = f;
			else if (arg.StartsWith("--yaw=") && float.TryParse(arg["--yaw=".Length..], out var yaw))
				_player.RotationDegrees = new Vector3(0f, yaw, 0f);
			else if (arg == "--crouch")
				_player.DebugSetStance(Stance.Crouch);
			else if (arg == "--prone")
				_player.DebugSetStance(Stance.Prone);
			else if (arg == "--report")
				report = true;
			else if (arg.StartsWith("--pos=") && arg["--pos=".Length..].Split(',') is { Length: 2 } xy
				&& float.TryParse(xy[0], out var px) && float.TryParse(xy[1], out var pz))
				_player.Position = new Vector3(px, 0.1f, pz);
		}
		if (fire > 0.0)
		{
			await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
			_player.DebugForceTrigger = true;
			await ToSignal(GetTree().CreateTimer(fire), SceneTreeTimer.SignalName.Timeout);
			_player.DebugForceTrigger = false;
		}
		if (wait > 0.0)
			await ToSignal(GetTree().CreateTimer(wait), SceneTreeTimer.SignalName.Timeout);
		if (report)
		{
			var b = _player.Health.Body;
			GD.Print($"PLAYER dead={_player.IsDead} H{b.Hp(BodyPart.Head):0} T{b.Hp(BodyPart.Thorax):0} S{b.Hp(BodyPart.Stomach):0} LA{b.Hp(BodyPart.LeftArm):0} RA{b.Hp(BodyPart.RightArm):0} LL{b.Hp(BodyPart.LeftLeg):0} RL{b.Hp(BodyPart.RightLeg):0}");
			foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>())
				GD.Print($"AI {ai.Preset?.Id} state={ai.Brain.State}/{ai.CombatState} pos={ai.GlobalPosition.X:0.0},{ai.GlobalPosition.Z:0.0} level={ai.Perception.Level} awareness={ai.Perception.Awareness:0.00} sees={ai.Perception.SeesTarget} cue={ai.LastCue} shots={ai.ShotsFired}");
		}
		if (shot is null)
		{
			if (report)
				GetTree().Quit();
			return;
		}
		await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
		GetViewport().GetTexture().GetImage().SavePng(shot);
		GetTree().Quit();
	}
}
