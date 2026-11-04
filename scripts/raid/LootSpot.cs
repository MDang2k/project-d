using Godot;

namespace ProjectD;

public enum LootTier { Low, Mid, High }

/// <summary>
/// A place loot appears (GDD s19: different value tiers). Spawns a container of the matching kind
/// when the level starts: crate (low), locker (mid), weapons crate (high).
/// </summary>
[Tool]
public partial class LootSpot : Marker3D
{
	[Export] public LootTier Tier { get; set; } = LootTier.Low;

	/// <summary>A hidden cache (M8d): spawns only when this knowledge effect (stash.&lt;id&gt;) is known. Empty means always.</summary>
	[Export] public string RequiresKnowledgeId { get; set; } = "";

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
			return;
		if (RequiresKnowledgeId != "" && GameState.Instance?.Profile.Knowledge.Opens(RequiresKnowledgeId) != true)
			return; // the cache is there, but you do not know about it yet
		AddToGroup("loot_spots");
		var (title, table, grid, secs, noise, box, color) = Tier switch
		{
			LootTier.High => ("container.weapons_crate", "high_tier", new Vector2I(6, 4), 2.0f, 10f,
				new Vector3(1.2f, 0.5f, 0.6f), new Color(0.3f, 0.34f, 0.25f)),
			LootTier.Mid => ("container.locker", "mid_tier", new Vector2I(4, 6), 1.5f, 14f,
				new Vector3(0.7f, 1.9f, 0.5f), new Color(0.35f, 0.38f, 0.4f)),
			_ => ("container.crate", "low_tier", new Vector2I(5, 4), 1.2f, 8f,
				new Vector3(0.9f, 0.6f, 0.6f), new Color(0.45f, 0.33f, 0.2f)),
		};
		var container = new LootContainer
		{
			TitleKey = title,
			Table = GD.Load<LootTable>($"res://data/loot/{table}.tres"),
			GridSize = grid,
			SecondsPerItem = secs,
			NoiseRadius = noise,
			BoxSize = box,
			BoxColor = color,
			Position = GlobalPosition,
			Rotation = new Vector3(0f, GlobalRotation.Y, 0f),
		};
		GetParent().CallDeferred(Node.MethodName.AddChild, container);
	}
}
