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

	public override void _Ready()
	{
		BakeNavigation();
		AddEnvironment();
		Projectiles = new ProjectileManager { Name = "Projectiles" };
		AddChild(Projectiles);

		var scene = PlayerScene ?? GD.Load<PackedScene>("res://scenes/player/player.tscn");
		Player = scene.Instantiate<Player>();
		AddChild(Player);
		PlacePlayer();

		Raid = new RaidManager { Name = "Raid", Config = Config };
		AddChild(Raid);
		Raid.Ended += (outcome, lost) => GD.Print($"RAID {outcome} lost={lost.Count}");
		Raid.Start(Player);
		// Bodies stay where they fell for the whole raid.
		AiHuman.AutoRespawn = false;
		AiHuman.Paused = false;
		SpawnPopulation();
		ApplyDevArgs();
	}

	private void BakeNavigation()
	{
		NavigationServer3D.MapSetCellSize(GetWorld3D().NavigationMap, 0.5f);
		if (GetNodeOrNull<NavigationRegion3D>("Geometry") is { } region)
			region.BakeNavigationMesh(false);
	}

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
		for (var i = 0; i < 120 && NavigationServer3D.MapGetIterationId(map) == 0; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		var cfg = Config ?? new RaidConfig();
		var points = GetTree().GetNodesInGroup("ai_spawns").OfType<AiSpawnPoint>()
			.Where(p => p.GlobalPosition.DistanceTo(Player.GlobalPosition) >= cfg.AiMinDistanceFromPlayer)
			.OrderBy(_ => GD.Randi())
			.Take(cfg.AiCount)
			.ToList();
		var n = 0;
		foreach (var point in points)
		{
			var preset = GD.Load<AiPreset>($"res://data/ai/{point.PresetId}.tres");
			if (preset is null)
				continue;
			var centre = NavigationServer3D.MapGetClosestPoint(map, point.GlobalPosition);
			var loop = new Vector3[4];
			for (var i = 0; i < loop.Length; i++)
			{
				var a = Mathf.Tau * i / loop.Length + GD.Randf() * 0.5f;
				var spot = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * point.PatrolRadius;
				loop[i] = NavigationServer3D.MapGetClosestPoint(map, spot);
			}
			var ai = new AiHuman
			{
				Name = $"{point.PresetId}_{++n}",
				Preset = preset,
				PatrolPoints = loop,
				Position = centre + Vector3.Up * 0.1f,
				Rotation = new Vector3(0f, GD.Randf() * Mathf.Tau, 0f),
			};
			AddChild(ai);
			ai.ShowDebug = ShowAiDebug;
		}
	}

	/// <summary>Random spawn point from the map's set, facing the middle of the map.</summary>
	private void PlacePlayer()
	{
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
		var polys = GetNodeOrNull<NavigationRegion3D>("Geometry")?.NavigationMesh?.GetPolygonCount() ?? 0;
		GD.Print($"MAP spawns={GetTree().GetNodesInGroup("player_spawns").Count} " +
			$"ai={GetTree().GetNodesInGroup("ai_spawns").Count} zones={GetTree().GetNodesInGroup("extraction_zones").Count} " +
			$"loot={GetTree().GetNodesInGroup("loot_containers").Count} navpolys={polys} raid={Raid.IsRunning} " +
			$"bots={GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().Count()} " +
			string.Join(",", GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().Select(a => $"{a.Preset?.Id}:{a.Brain.State}")) + " " +
			$"player={Player.GlobalPosition.X:0},{Player.GlobalPosition.Z:0}");
	}

	private async void ApplyDevArgs()
	{
		string? shot = null;
		var wait = 0.0;
		var report = false;
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
			else if (arg == "--report")
				report = true;
			else if (arg == "--noclip")
				Player.DebugNoclip = true;
			else if (arg.StartsWith("--pos=") && arg["--pos=".Length..].Split(',') is { Length: 3 } xyz
				&& float.TryParse(xyz[0], out var px) && float.TryParse(xyz[1], out var py)
				&& float.TryParse(xyz[2], out var pz))
				Player.GlobalPosition = new Vector3(px, py, pz);
		}
		if (report)
		{
			await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
			PrintReport();
		}
		if (shot is null)
			return;
		await ToSignal(GetTree().CreateTimer(wait + 0.5), SceneTreeTimer.SignalName.Timeout);
		GetViewport().GetTexture().GetImage().SavePng(shot);
		GetTree().Quit();
	}
}
