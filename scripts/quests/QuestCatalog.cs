using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>Loads every quest once, in a fixed order (the slice quests, GDD s20 M7).</summary>
public static class QuestCatalog
{
	public static readonly string[] Order =
	{
		"q01_looters", "q02_medical", "q03_bandits", "q04_recon", "q05_drain", "q06_helmets",
	};

	private static List<QuestSpec>? _all;

	public static IReadOnlyList<QuestSpec> All => _all ??= Order
		.Select(id => GD.Load<QuestData>($"res://data/quests/{id}.tres")?.ToSpec())
		.Where(q => q is not null).Select(q => q!).ToList();

	public static QuestSpec? Find(string id) => All.FirstOrDefault(q => q.Id == id);
}
