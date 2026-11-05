using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// The raid's boss squad (GDD s8, Border Strip spec s5): by chance a named boss with escorts appears at the far end of
/// the map's boss route and walks it for the whole raid. Hostile to everyone through its faction. Thin node; the roll and
/// the route order are in <see cref="BossRoll"/>. A map without boss route points, or a config without a boss, does nothing.
/// Dev: <c>--boss</c> makes the boss certain.
/// </summary>
public partial class BossSpawner : Node
{
	public bool ShowDebug { get; set; }

	public async void Start(RaidConfig cfg, Player player)
	{
		if (cfg.BossActorId == "")
			return;
		var route = GetTree().GetNodesInGroup("boss_route").OfType<BossRoutePoint>().OrderBy(p => p.Order).ToList();
		if (route.Count < 2)
			return;
		var forced = OS.GetCmdlineUserArgs().Contains("--boss");
		if (!forced && !BossRoll.Rolls(cfg.BossChance, new System.Random((int)GD.Randi())))
		{
			GD.Print($"BOSS {cfg.BossActorId}: not this raid");
			return;
		}
		var map = player.GetWorld3D().NavigationMap;
		// the baked region only joins the navigation map after a few physics frames
		for (var i = 0; i < 120 && (NavigationServer3D.MapGetIterationId(map) == 0 || NavigationServer3D.MapGetRegions(map).Count == 0); i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		for (var i = 0; i < 6; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		var patrol = BossRoll.FromFarthest(route.Select(p => p.GlobalPosition).ToList(), player.GlobalPosition)
			.Select(p => AiSpawner.Snap(map, p)).ToArray();
		var offsets = WavePlanner.Offsets(1 + cfg.BossEscorts);
		for (var i = 0; i < offsets.Count; i++)
		{
			var preset = i == 0 ? cfg.BossActorId : cfg.BossEscortId;
			var name = i == 0 ? cfg.BossActorId : $"{cfg.BossEscortId}_{i}";
			var ai = AiSpawner.Spawn(GetParent(), preset, name, AiSpawner.Snap(map, patrol[0] + offsets[i]), patrol, ShowDebug,
				a => a.Ambush = new AmbushGate()); // Harker's deserters let the player come close before they open fire
			if (i == 0 && ai is not null)
				GD.Print($"BOSS gear: {string.Join(", ", ai.Gear.AllItems().Select(it => it.Spec.Id))}");
		}
		GD.Print($"BOSS {cfg.BossActorId} with {cfg.BossEscorts} escorts at {patrol[0].X:0},{patrol[0].Z:0}, route points {patrol.Length}");
	}
}
