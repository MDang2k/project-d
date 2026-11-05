using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Dev performance probe (<c>--perf</c>): flies the player through a few spots of the map (the spawn, each extraction
/// zone, the map centre) and prints fps, draw calls, primitives, objects and AI counts after a settle time at each, so
/// the 60 fps / GTX 1060 target can be checked per area. <c>--perfquit</c> exits at the end. Windowed runs only: headless
/// has no renderer to measure.
/// </summary>
public partial class RaidPerfProbe : Node
{
	public async void Start(Player player)
	{
		var spots = new List<(string Name, Vector3 At)>();
		foreach (var s in GetTree().GetNodesInGroup("player_spawns").OfType<Node3D>().Take(1))
			spots.Add(("spawn", s.GlobalPosition));
		foreach (var z in GetTree().GetNodesInGroup("extraction_zones").OfType<Node3D>())
			spots.Add(("exit " + z.Name, z.GlobalPosition));
		spots.Add(("centre", new Vector3(0f, 1f, 0f)));
		foreach (var (name, at) in spots)
		{
			player.DebugTeleport(at);
			await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout); // settle
			var fps = new List<double>();
			for (var i = 0; i < 20; i++)
			{
				await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
				fps.Add(Engine.GetFramesPerSecond());
			}
			GD.Print($"PERF {name,-18} fps avg {fps.Average():0} min {fps.Min():0}  draws {Perf(Performance.Monitor.RenderTotalDrawCallsInFrame)}  " +
				$"prims {Perf(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1000}k  objects {Perf(Performance.Monitor.RenderTotalObjectsInFrame)}  " +
				$"ai {GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().Count(a => a.IsAlive)}  physObjs {Perf(Performance.Monitor.Physics3DActiveObjects)}  " +
				$"mem {Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576:0} MB");
		}
		if (OS.GetCmdlineUserArgs().Contains("--perfquit"))
			GetTree().Quit();
	}

	private static long Perf(Performance.Monitor m) => (long)Performance.GetMonitor(m);
}
