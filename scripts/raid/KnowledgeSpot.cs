using Godot;

namespace ProjectD;

/// <summary>
/// A fixed spot where a knowledge document lies at raid start (M8d spec s2). The item is planted unless the player already
/// knows what it says. <see cref="RaidKnowledge"/> reads the spots from the group "knowledge_spots".
/// </summary>
[Tool]
public partial class KnowledgeSpot : Marker3D
{
	/// <summary>Item id of the document (the knowledge item), for example "tower_cache_note".</summary>
	[Export] public string ItemId { get; set; } = "";

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("knowledge_spots");
	}
}
