using Godot;

namespace ProjectD;

/// <summary>Placing AI on the navmesh; shared by the raid start population and the timed waves.</summary>
public static class AiSpawner
{
	/// <summary>Waits until the baked navmesh has joined the navigation map (it takes a few physics frames).</summary>
	public static async System.Threading.Tasks.Task WaitForNavigation(SceneTree tree, Rid map)
	{
		for (var i = 0; i < 120 && (NavigationServer3D.MapGetIterationId(map) == 0 || NavigationServer3D.MapGetRegions(map).Count == 0); i++)
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		for (var i = 0; i < 6; i++)
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
	}

	/// <summary>Closest navmesh point, or the point itself when the answer is implausibly far (map not ready).</summary>
	public static Vector3 Snap(Rid map, Vector3 p)
	{
		var snapped = NavigationServer3D.MapGetClosestPoint(map, p);
		return snapped.DistanceTo(p) < 6f ? snapped : p;
	}

	/// <summary>
	/// An AI of the actor <paramref name="presetId"/> at a point, walking a patrol loop; null for an unknown actor.
	/// <paramref name="configure"/> runs before the AI enters the tree (nemesis gear, toughness).
	/// </summary>
	public static AiHuman? Spawn(Node parent, string presetId, string name, Vector3 position, Vector3[] patrol, bool debug,
		System.Action<AiHuman>? configure = null)
	{
		if (!ActorDb.Catalog.Has(presetId))
		{
			GD.PushWarning($"[Raid] unknown actor '{presetId}'");
			return null;
		}
		var ai = new AiHuman
		{
			Name = name,
			Actor = ActorDb.Create(presetId),
			PatrolPoints = patrol,
			Position = position + Vector3.Up * 0.1f,
			Rotation = new Vector3(0f, GD.Randf() * Mathf.Tau, 0f),
		};
		configure?.Invoke(ai);
		parent.AddChild(ai);
		ai.ShowDebug = debug;
		return ai;
	}
}
