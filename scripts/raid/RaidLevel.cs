using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Root of a raid map scene (map 1). Bakes the navmesh, adds lighting, projectiles and the player at
/// a random spawn point, then starts the raid. Geometry, spawn points, extraction zones and loot
/// spots are plain nodes in the scene so they can be edited in the Godot editor.
/// Dev (F2): noclip fly. Dev args after "--": --pos=x,z --yaw=deg --noclip --shot=path --wait=secs.
/// </summary>
public partial class RaidLevel : Node3D
{
	[Export] public RaidConfig? Config { get; set; }
	[Export] public PackedScene? PlayerScene { get; set; }

	public Player Player { get; private set; } = null!;
	public RaidManager Raid { get; private set; } = null!;
	public ProjectileManager Projectiles { get; private set; } = null!;
	private RaidQuests _quests = null!;
	private bool _arrivedByTransit;

	public override void _Ready()
	{
		BakeNavigation();
		AddEnvironment();
		if (OS.IsDebugBuild())
			AddChild(GD.Load<PackedScene>("res://scenes/ui/actor_inspector.tscn").Instantiate());
		Projectiles = new ProjectileManager { Name = "Projectiles" };
		AddChild(Projectiles);

		var scene = PlayerScene ?? GD.Load<PackedScene>("res://scenes/player/player.tscn");
		Player = scene.Instantiate<Player>();
		AddChild(Player);
		PlacePlayer();

		// Dev: --raidtime=secs overrides the raid length (testing the MIA path).
		var dev = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--raidtime="));
		var cfg = Config ?? new RaidConfig();
		if (dev is not null && float.TryParse(dev["--raidtime=".Length..], out var seconds))
		{
			cfg = (RaidConfig)cfg.Duplicate();
			cfg.DurationS = seconds;
		}
		Raid = new RaidManager { Name = "Raid", Config = cfg };
		AddChild(Raid);
		Raid.Ended += OnRaidEnded;
		Raid.Start(Player);
		var inv0 = Player.Inventory.Inventory;
		SaveManager.Instance?.BeginRaid(inv0);
		GD.Print($"RAIDSTART run={RunState.Current.Raids} items={inv0.AllItems().Count()} " +
			$"primary={inv0.InSlot(EquipSlot.Primary)?.Spec.Id ?? "none"} secure={inv0.SecureGrids.SelectMany(g => g.Items).Count()}");
		// Bodies stay where they fell for the whole raid.
		AiHuman.AutoRespawn = false;
		AiHuman.Paused = false;
		SpawnPopulation();
		StartWaves(cfg);
		var transit = new TransitManager { Name = "Transit" };
		AddChild(transit);
		transit.Start(Raid, Player, cfg.MapId, _arrivedByTransit);
		var boss = new BossSpawner { Name = "Boss", ShowDebug = ShowAiDebug };
		AddChild(boss);
		boss.Start(cfg, Player);
		_quests = new RaidQuests { Name = "Quests", MapId = Config?.MapId ?? "map1" };
		AddChild(_quests);
		_quests.Start(Player, Projectiles);
		AddChild(new ObjectiveHud { Name = "ObjectiveHud" });
		AddChild(new ExtractionHud(Raid) { Name = "ExtractionHud" });
		ApplyDevArgs();
	}

	private async void OnRaidEnded(RaidOutcome outcome, System.Collections.Generic.IReadOnlyList<ItemInstance> lost)
	{
		var inv = Player.Inventory.Inventory;
		var run = RunState.Current;
		run.Record(outcome, inv);
		if (outcome == RaidOutcome.Extracted && Raid.Session.ExtractedZoneId is { } zoneId)
			_quests.OnExtracted(zoneId);
		// Insurance rolls for the lost gear and ages the return queue, before the save is written.
		var profile = GameState.Instance?.Profile ?? new GameProfile();
		var insurance = InsuranceService.OnRaidEnd(outcome, lost, profile, inv, SaveManager.LevelOf,
			System.Random.Shared.NextDouble);
		InsuranceService.TryDeliver(profile);
		SaveManager.Instance?.EndRaid();
		GD.Print($"RAID {outcome} lost={lost.Count} runs={run.Raids} extracted={run.Extractions}");
		// Let the death play out for a moment; everything else shows at once.
		if (outcome == RaidOutcome.Killed)
			await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
		var kept = System.Linq.Enumerable.ToList(inv.AllItems());
		var screen = RaidResultScreen.Show(this, outcome, Raid.Session.ElapsedS, lost, kept, run, insurance);
		screen.NewRaidRequested += () => GetTree().ChangeSceneToFile("res://scenes/hub/hub.tscn");
		if (OS.GetCmdlineUserArgs().Contains("--quitafterraid"))
		{
			GetTree().Quit();
			return;
		}
		// Dev: --autonew presses the button for headless flow tests (stops after 3 raids).
		if (OS.GetCmdlineUserArgs().Contains("--autonew") && run.Raids < 3)
		{
			await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
			GetTree().Paused = false;
			GetTree().ReloadCurrentScene();
		}
		else if (OS.GetCmdlineUserArgs().Contains("--autonew"))
		{
			GetTree().Quit();
		}
	}

	private void BakeNavigation()
	{
		NavigationServer3D.MapSetCellSize(GetWorld3D().NavigationMap, 0.5f);
		if (NavRegion is { } region)
			region.BakeNavigationMesh(false);
	}

	/// <summary>The map's navigation region: "Geometry" under the root, or inside an instanced generated graybox.</summary>
	private NavigationRegion3D? NavRegion
		=> GetNodeOrNull<NavigationRegion3D>("Geometry") ?? FindChild("Geometry", true, false) as NavigationRegion3D;

	private void AddEnvironment()
	{
		if (GetNodeOrNull<WorldEnvironment>("WorldEnvironment") is not null)
			return;
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, 30f, 0f), ShadowEnabled = true });
		AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color(0.62f, 0.72f, 0.85f),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color(0.6f, 0.6f, 0.65f),
			},
		});
	}

	/// <summary>Shows AI cones and labels (dev); off in a normal raid.</summary>
	[Export] public bool ShowAiDebug { get; set; }

	/// <summary>
	/// Places AI on a random subset of the map's AiSpawnPoints, away from the player's start, each
	/// patrolling a loop around its point. Waits two physics frames so the navmesh is live.
	/// </summary>
	private async void SpawnPopulation()
	{
		var map = GetWorld3D().NavigationMap;
		// The baked region only joins the navigation map after a few physics frames; until then closest-point
		// queries answer with the map origin, which put every bot on the same spot.
		for (var i = 0; i < 120 && (NavigationServer3D.MapGetIterationId(map) == 0
			|| NavigationServer3D.MapGetRegions(map).Count == 0); i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		for (var i = 0; i < 6; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		var cfg = Config ?? new RaidConfig();
		var points = GetTree().GetNodesInGroup("ai_spawns").OfType<AiSpawnPoint>()
			.Where(p => p.GlobalPosition.DistanceTo(Player.GlobalPosition) >= cfg.AiMinDistanceFromPlayer)
			.ToList();
		// rolled groups (looters: 4 to 6 of 8) are thinned before the overall cap
		points = SpawnRoll.Apply(points, p => p.RollGroup, p => p.RollMin, p => p.RollMax, new System.Random((int)GD.Randi()))
			.OrderBy(_ => GD.Randi())
			.Take(cfg.AiCount)
			.ToList();
		var n = 0;
		foreach (var point in points)
		{
			if (!ActorDb.Catalog.Has(point.PresetId))
			{
				GD.PushWarning($"[Raid] spawn point uses unknown actor '{point.PresetId}'");
				continue;
			}
			var centre = Snap(map, point.GlobalPosition);
			var loop = new Vector3[4];
			for (var i = 0; i < loop.Length; i++)
			{
				var a = Mathf.Tau * i / loop.Length + GD.Randf() * 0.5f;
				var spot = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * point.PatrolRadius;
				loop[i] = Snap(map, spot);
			}
			var ai = new AiHuman
			{
				Name = $"{point.PresetId}_{++n}",
				Actor = ActorDb.Create(point.PresetId),
				PatrolPoints = loop,
				Position = centre + Vector3.Up * 0.1f,
				Rotation = new Vector3(0f, GD.Randf() * Mathf.Tau, 0f),
			};
			AddChild(ai);
			ai.ShowDebug = ShowAiDebug;
		}
	}

	private static Vector3 Snap(Rid map, Vector3 p) => AiSpawner.Snap(map, p);

	/// <summary>Timed waves of the map (none on a map without wave markers). Dev: --waveclock=secs skips the wave clock ahead.</summary>
	private void StartWaves(RaidConfig cfg)
	{
		var waves = new WaveSpawner { Name = "Waves", ShowDebug = ShowAiDebug };
		var skip = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--waveclock="));
		if (skip is not null && float.TryParse(skip["--waveclock=".Length..], out var offset))
			waves.ClockOffsetS = offset;
		AddChild(waves);
		waves.Start(Raid, Player, cfg.ToWaveTuning());
	}

	/// <summary>Random spawn point from the map's set, facing the middle of the map.</summary>
	private void PlacePlayer()
	{
		// a player who came in by a transit appears at the arrival point for the map they left, carrying their body state
		var arrivals = GetTree().GetNodesInGroup("transit_arrivals").OfType<ArrivalPoint>().ToList();
		if (TransitCarry.Pending is { } carried)
		{
			_arrivedByTransit = true;
			Player.Health.Restore(carried);
			if (arrivals.Count > 0)
			{
				var arrival = arrivals.FirstOrDefault(a => a.From == TransitCarry.FromMap) ?? arrivals[0];
				Player.GlobalPosition = arrival.GlobalPosition + Vector3.Up * 0.1f;
				Player.Rotation = new Vector3(0f, arrival.GlobalRotation.Y, 0f);
				GD.Print($"TRANSIT arrived from '{TransitCarry.FromMap}' at {arrival.Name}, thorax={Player.Health.Body.Hp(BodyPart.Thorax):0} leftLeg={Player.Health.Body.Hp(BodyPart.LeftLeg):0} fractured={Player.Health.Effects.IsFractured(BodyPart.LeftLeg)}");
				TransitCarry.Clear();
				return;
			}
			TransitCarry.Clear();
		}
		var points = GetTree().GetNodesInGroup("player_spawns").OfType<PlayerSpawnPoint>().ToList();
		if (points.Count == 0)
		{
			Player.GlobalPosition = new Vector3(0f, 0.2f, 0f);
			return;
		}
		var p = points[(int)(GD.Randi() % (uint)points.Count)];
		Player.GlobalPosition = p.GlobalPosition + Vector3.Up * 0.1f;
		var to = -p.GlobalPosition;
		Player.Rotation = new Vector3(0f, Mathf.Atan2(-to.X, -to.Z), 0f);
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (!OS.IsDebugBuild() || @event is not InputEventKey { Pressed: true, Echo: false } key)
			return;
		if (key.Keycode == Key.F2)
			Player.DebugNoclip = !Player.DebugNoclip;
		else if (key.Keycode == Key.F4)
		{
			ShowAiDebug = !ShowAiDebug;
			foreach (var ai in GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>())
				ai.ShowDebug = ShowAiDebug;
		}
	}

	private void PrintReport()
	{
		GD.Print($"HAND left={Player.Weapon?.LeftHandAction.Position} right={Player.Weapon?.RightHandAction.Position} grip={Player.Weapon?.GripLeft.Position}");
		GD.Print($"FOOTIK (drop, ankleY L, ankleY R) = {Player.DebugFootIk()}");
		GD.Print($"GAIT thigh L/R pitch = {Player.DebugThighPitchDeg()} speed={Player.Velocity.Length():0.0}");
		var polys = NavRegion?.NavigationMesh?.GetPolygonCount() ?? 0;
		GD.Print($"MAP spawns={GetTree().GetNodesInGroup("player_spawns").Count} " +
			$"ai={GetTree().GetNodesInGroup("ai_spawns").Count} zones={GetTree().GetNodesInGroup("extraction_zones").Count} " +
			$"loot={GetTree().GetNodesInGroup("loot_containers").Count} navpolys={polys} raid={Raid.IsRunning} " +
			$"bots={GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().Count()} " +
			string.Join(" ", GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().Select(b => $"[{b.GlobalPosition.X:0},{b.GlobalPosition.Z:0}]")) + " " +
			string.Join(",", GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().Select(a => $"{a.ActorId}:{a.Brain.State}")) + " " +
			$"player={Player.GlobalPosition.X:0},{Player.GlobalPosition.Z:0}");
	}

	private async void ApplyDevArgs()
	{
		string? shot = null;
		var wait = 0.0;
		var report = false;
		var reportDelay = 0.5;
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--shot="))
				shot = arg["--shot=".Length..];
			else if (arg.StartsWith("--wait=") && double.TryParse(arg["--wait=".Length..], out var w))
				wait = w;
			else if (arg.StartsWith("--yaw=") && float.TryParse(arg["--yaw=".Length..], out var yaw))
				Player.RotationDegrees = new Vector3(0f, yaw, 0f);
			else if (arg.StartsWith("--pitch=") && float.TryParse(arg["--pitch=".Length..], out var pitch))
				Player.DebugSetPitchDeg(pitch);
			else if (arg.StartsWith("--attach=") && arg["--attach=".Length..].Split(':') is { Length: 2 } ap
				&& Player.Inventory.Inventory.InSlot(EquipSlot.Primary) is { } primary && ItemDb.Create(ap[1]) is { } newPart)
			{
				GD.Print($"ATTACH {ap[1]} -> {ap[0]}: {WeaponBuild.Attach(primary, ap[0], newPart)} zoom={WeaponBuild.Stats(primary).Zoom}");
				Player.Inventory.NotifyChanged();
			}
			else if (arg.StartsWith("--move=") && arg["--move=".Length..].Split(',') is { Length: 2 } mv
				&& float.TryParse(mv[0], out var mx) && float.TryParse(mv[1], out var my))
				Player.DebugMove = new Vector2(mx, my);
			else if (arg == "--sprint")
				Player.DebugSprint = true;
			else if (arg.StartsWith("--stance=") && System.Enum.TryParse<Stance>(arg["--stance=".Length..], true, out var st))
				Player.DebugSetStance(st);
			else if (arg == "--clipdemo")
				GetTree().CreateTimer(0.3).Timeout += Player.DebugClipDemo;
			else if (arg is "--reload" or "--reloadfast")
				GetTree().CreateTimer(0.5).Timeout += () => Player.DebugReload(arg == "--reloadfast");
			else if (arg == "--ads")
				Player.DebugForceAds = true;
			else if (arg.StartsWith("--reportdelay=") && double.TryParse(arg["--reportdelay=".Length..], out var rd))
				reportDelay = rd;
			else if (arg == "--report")
				report = true;
			else if (arg.StartsWith("--questkill=") && arg["--questkill=".Length..].Split(':') is { Length: 2 } kv
				&& System.Enum.TryParse<Faction>(kv[0], out var killFaction) && int.TryParse(kv[1], out var kills))
				_quests.DebugKills(killFaction, kills);
			else if (arg.StartsWith("--questzone="))
				_quests.DebugZone(arg["--questzone=".Length..]);
			else if (arg == "--noclip")
				Player.DebugNoclip = true;
			else if (arg.StartsWith("--pos=") && arg["--pos=".Length..].Split(',') is { Length: 3 } xyz
				&& float.TryParse(xyz[0], out var px) && float.TryParse(xyz[1], out var py)
				&& float.TryParse(xyz[2], out var pz))
				Player.GlobalPosition = new Vector3(px, py, pz);
		}
		if (report)
		{
			await ToSignal(GetTree().CreateTimer(reportDelay), SceneTreeTimer.SignalName.Timeout);
			PrintReport();
		}
		if (shot is null)
			return;
		await ToSignal(GetTree().CreateTimer(wait + 0.5), SceneTreeTimer.SignalName.Timeout);
		GetViewport().GetTexture().GetImage().SavePng(shot);
		GetTree().Quit();
	}
}
