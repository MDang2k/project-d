using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>A lootable body: the worn gear stays visible, plus a rolled scavenger table (GDD s10).</summary>
public static class CorpseSpawner
{
	public static LootContainer Spawn(Node parent, Vector3 position, IEnumerable<string> wornItemIds)
	{
		var corpse = new LootContainer
		{
			TitleKey = "container.body",
			Table = GD.Load<LootTable>("res://data/loot/body_scav.tres"),
			GridSize = new Vector2I(4, 4),
			SecondsPerItem = 1.0f,
			NoiseRadius = 6f,
			BoxSize = new Vector3(1.6f, 0.25f, 0.6f),
			BoxColor = new Color(0.25f, 0.28f, 0.22f),
			Position = position,
		};
		parent.AddChild(corpse);
		foreach (var id in wornItemIds)
		{
			if (ItemDb.Create(id) is { } worn)
				corpse.AddVisible(worn);
		}
		corpse.GetTree().CreateTimer(180.0).Timeout += corpse.QueueFree;
		return corpse;
	}
}
