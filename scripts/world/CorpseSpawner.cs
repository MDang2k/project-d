using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>A lootable body (GDD s10): either a dead character with equipment and pockets, or a simple box of loot.</summary>
public static class CorpseSpawner
{
	/// <summary>A dead AI: its weapon, armor, rig, backpack and pockets, searched like in Tarkov.</summary>
	public static LootContainer SpawnBody(Node parent, Vector3 position, CharacterInventory body)
	{
		var corpse = new LootContainer
		{
			TitleKey = "container.body",
			Body = body,
			SecondsPerItem = 1.0f,
			NoiseRadius = 6f,
			BoxSize = new Vector3(1.6f, 0.25f, 0.6f),
			BoxColor = new Color(0.25f, 0.28f, 0.22f),
			Position = position,
		};
		parent.AddChild(corpse);
		corpse.GetTree().CreateTimer(180.0).Timeout += corpse.QueueFree;
		return corpse;
	}

	/// <summary>Test dummies and dev corpses: worn gear stays visible, plus a rolled scavenger table.</summary>
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
