using System.Linq;
using System.Text;
using Godot;

namespace ProjectD;

/// <summary>
/// M5 AI test range, built in code: open ground, cover walls and one AI of each faction preset.
/// Keys: F2 noclip ghost fly, F4 toggles AI debug (cones and awareness labels), Backspace respawns the player.
/// Dev args after "--": --pos=x,z --yaw=deg --move=x,y --sprint --crouch --prone --fire=secs --wait=secs --report --shot=path.
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
		// Two firefights set up ~55 m apart: looter vs bandit at z -30, allied vs soviet PMC at z -90.
		// Each side patrols toward the other so they meet around the cover in the middle.
		var spots = new[]
		{
			(new Vector3(-30f, 0.1f, -30f), 1f), (new Vector3(25f, 0.1f, -30f), -1f),
			(new Vector3(-30f, 0.1f, -90f), 1f), (new Vector3(25f, 0.1f, -90f), -1f),
		};
		var order = new[] { 0, 1, 2, 3 }; // preset ids: looter, bandit, allied_pmc, soviet_pmc
		for (var i = 0; i < PresetIds.Length; i++)
		{
			var id = PresetIds[order[i]];
			if (only is not null && only != id)
				continue;
			var (c, toward) = spots[i];
			var forward = new Vector3(toward * 18f, 0f, 0f);
			var patrol = standStill
				? System.Array.Empty<Vector3>()
				: new[] { c, c + forward, c + forward + new Vector3(0f, 0f, -8f), c + new Vector3(0f, 0f, -8f) };
			var yaw = toward > 0f ? -90f : 90f;
			SpawnAi(id, c, yaw, patrol);
			// A partner standing close by so pairs can flank together.
			if (!standSolo)
				SpawnAi(id, c + new Vector3(0f, 0f, 3f), yaw, patrol, "_2");
		}

		NoiseBus.Noise += n => _lastNoise = $"{n.Kind} ({n.Radius:0} m)";
		var layer = new CanvasLayer();
		_label = new Label { Position = new Vector2(12f, 12f) };
		layer.AddChild(_label);
		AddChild(layer);
		AddChild(GD.Load<PackedScene>("res://scenes/ui/actor_inspector.tscn").Instantiate());
		BuildMenu(layer);
		ApplyDevArgs();
	}

	private void SpawnAi(string presetId, Vector3 position, float yawDeg, Vector3[] patrol, string suffix = "")
	{
		var ai = new AiHuman
		{
			Name = presetId + suffix,
			Actor = ActorDb.Create(presetId),
			Position = position,
			RotationDegrees = new Vector3(0f, yawDeg, 0f),
			PatrolPoints = patrol,
		};
		AddChild(ai);
		ai.ShowDebug = _debug;
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
		// Cover in the middle of both firefights.
		foreach (var z in new[] { -30f, -90f })
		{
			Box(new Vector3(-12f, 0.45f, z + 4f), new Vector3(4f, 0.9f, 0.5f), cover);
			Box(new Vector3(-2f, 1.25f, z), new Vector3(0.5f, 2.5f, 8f), new Color(0.5f, 0.45f, 0.45f));
			Box(new Vector3(6f, 0.45f, z - 5f), new Vector3(4f, 0.9f, 0.5f), cover);
			Box(new Vector3(12f, 0.6f, z + 3f), new Vector3(0.5f, 1.2f, 4f), cover);
		}
		// the foot IK course (stairs, curb, ledge, ramps) is authored in the scene; it joins the navigation region to be baked
		GetNodeOrNull<Node3D>("FootIkCourse")?.Reparent(_nav, false);
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
			case Key.F1:
				ToggleMenu();
				break;
			case Key.F2:
				_player.DebugNoclip = !_player.DebugNoclip;
				break;
			case Key.F3:
				_player.DebugFaction = (Faction)(((int)_player.Faction + 1) % 4);
				break;
			case Key.F4:
				_debug = !_debug;
				foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>())
					ai.ShowDebug = _debug;
				break;
			case Key.Quoteleft:
				_player.CycleDebugView();
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
		RefreshMenu();
		if (_traceOn)
		{
			foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>())
			{
				var now = $"{ai.Brain.State}/{ai.CombatState}";
				if (_trace.TryGetValue(ai, out var old) && old == now)
					continue;
				_trace[ai] = now;
				GD.Print($"TRACE {Time.GetTicksMsec() / 1000.0:0.0}s {ai.ActorId} {now}");
			}
		}
		var sb = new StringBuilder();
		var w = _player.Weapon;
		sb.AppendLine($"you: {_player.Faction}  stance {_player.Stance}{(_player.IsSprinting ? " sprinting" : "")}  hp {(_player.IsDead ? "DEAD" : "ok")}");
		sb.AppendLine($"last noise: {_lastNoise}");
		foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>())
		{
			var d = ai.GlobalPosition.DistanceTo(_player.GlobalPosition);
			sb.AppendLine($"{ai.ActorId,-11} {d,5:0} m  {ai.Perception.Level,-10} {ai.Perception.Awareness * 100f,3:0}%  {ai.LastCue}");
		}
		if (_raid is { IsRunning: true } r)
			sb.AppendLine($"raid {r.Session.RemainingS:0}s left  extract {r.Session.ExtractProgress * 100f:0}%");
		else if (_raid is not null)
			sb.AppendLine($"raid over: {_raid.Session.Outcome}");
		sb.Append("V fire  RMB aim  Shift sprint  C crouch  Z prone  F4 AI debug " + (_debug ? "ON" : "off") + "  F1 DEBUG MENU  F3 your side " + _player.Faction + "  ` view " + _player.DebugView + "  F2 noclip " + (_player.DebugNoclip ? "ON (Space up, C down, Shift fast)" : "off") + "  Backspace respawn");
		_label.Text = sb.ToString();
		_ = w;
	}

	private RaidManager? _raid;

	/// <summary>Dev (--raid=secs): runs a raid with a short timer and one extraction zone at the spawn.</summary>
	private void StartTestRaid(float seconds)
	{
		var zone = new ExtractionZone { ZoneId = "test_gate", Position = new Vector3(0f, 1.5f, 0f), Seconds = 5f };
		AddChild(zone);
		_raid = new RaidManager { Config = new RaidConfig { DurationS = seconds } };
		AddChild(_raid);
		_raid.Ended += (outcome, lost) => GD.Print($"RAID {outcome} lost={lost.Count}");
		_raid.Start(_player);
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
			else if (arg.StartsWith("--corpse=") && arg["--corpse=".Length..].Split(',') is { Length: 2 } cxy
				&& float.TryParse(cxy[0], out var cx) && float.TryParse(cxy[1], out var cz))
				CorpseSpawner.Spawn(this, new Vector3(cx, 0f, cz), new[] { "flak_vest_t1" });
			else if (arg.StartsWith("--raid=") && float.TryParse(arg["--raid=".Length..], out var raidSeconds))
				StartTestRaid(raidSeconds);
			else if (arg == "--menu")
				ToggleMenu();
			else if (arg == "--norespawn")
				AiHuman.AutoRespawn = false;
			else if (arg == "--noclip")
				_player.DebugNoclip = true;
			else if (arg == "--report")
				report = true;
			else if (arg.StartsWith("--move=") && arg["--move=".Length..].Split(',') is { Length: 2 } mv
				&& float.TryParse(mv[0], out var mx) && float.TryParse(mv[1], out var my))
				_player.DebugMove = new Vector2(mx, my);
			else if (arg == "--sprint")
				_player.DebugSprint = true;
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
		var userArgs = OS.GetCmdlineUserArgs();
		if (userArgs.Contains("--killall"))
		{
			await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
			foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().ToList())
				ai.Health.Body.TakeDamage(BodyPart.Head, 9999f);
		}
		if (userArgs.Contains("--openbody"))
		{
			await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
			var body = GetTree().GetNodesInGroup("loot_containers").OfType<LootContainer>().FirstOrDefault(c => c.Body is not null);
			if (body is not null)
			{
				GD.Print($"BODY items={body.Body!.AllItems().Count()} hidden={body.Model.HiddenCount} primary={body.Body.InSlot(EquipSlot.Primary)?.Spec.Id ?? "none"} " +
					$"vest={body.Body.InSlot(EquipSlot.Vest)?.Spec.Id ?? "none"} rig={body.Body.InSlot(EquipSlot.Rig)?.Spec.Id ?? "none"}");
				_player.Screen.OpenWithContainer(body);
				await ToSignal(GetTree().CreateTimer(6.0), SceneTreeTimer.SignalName.Timeout);
				GD.Print($"BODY after search hidden={body.Model.HiddenCount}");
				if (userArgs.Contains("--takebody"))
				{
					var before = _player.Inventory.Inventory.AllItems().Count();
					var tookVest = _player.Screen.DebugTakeFromBody(EquipSlot.Primary);
					await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
					var inv = _player.Inventory.Inventory;
					var found = inv.AllItems().FirstOrDefault(i => i.Spec.Id == "fal_type");
					GD.Print($"BODY take primary: clicked={tookVest} carried {before}->{inv.AllItems().Count()} fal found={found is not null} " +
						$"foundInRaid={found?.FoundInRaid} bodyPrimary={body.Body!.InSlot(EquipSlot.Primary)?.Spec.Id ?? "none"}");
				}
			}
		}
		if (wait > 0.0)
			await ToSignal(GetTree().CreateTimer(wait), SceneTreeTimer.SignalName.Timeout);
		if (report)
		{
			var b = _player.Health.Body;
			GD.Print($"PLAYER maxCamStep={_player.MaxCameraYStep:0.000} pos={_player.GlobalPosition.X:0.00},{_player.GlobalPosition.Y:0.00},{_player.GlobalPosition.Z:0.00} dead={_player.IsDead} H{b.Hp(BodyPart.Head):0} T{b.Hp(BodyPart.Thorax):0} S{b.Hp(BodyPart.Stomach):0} LA{b.Hp(BodyPart.LeftArm):0} RA{b.Hp(BodyPart.RightArm):0} LL{b.Hp(BodyPart.LeftLeg):0} RL{b.Hp(BodyPart.RightLeg):0}");
			foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>())
				GD.Print($"AI {ai.ActorId} state={ai.Brain.State}/{ai.CombatState} pos={ai.GlobalPosition.X:0.0},{ai.GlobalPosition.Z:0.0} level={ai.Perception.Level} awareness={ai.Perception.Awareness:0.00} sees={ai.Perception.SeesTarget} cue={ai.LastCue} shots={ai.ShotsFired} looted={ai.LootedItems}");
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
