using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>Loads every knowledge definition in data/knowledge once, in a fixed order (the slice entries, M8d spec s6).</summary>
public static class KnowledgeCatalog
{
	public static readonly string[] Order =
	{
		"bunker_side_room_door_code", "tower_cache_note", "german_lab_note_probe_7",
		"radio_log_with_frequency", "guard_s_diary_march_1965", "evacuation_order_1965", "first_delivery",
	};

	private static List<KnowledgeSpec>? _all;

	public static IReadOnlyList<KnowledgeSpec> All => _all ??= Order
		.Select(id => GD.Load<KnowledgeData>($"res://data/knowledge/{id}.tres")?.ToSpec())
		.Where(k => k is not null).Select(k => k!).ToList();
}
