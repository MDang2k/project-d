using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>Loads every trader once, in a fixed display order.</summary>
public static class TraderCatalog
{
	public static readonly string[] Order = { "allied_qm", "soviet_qm", "smuggler", "medic", "engineer", "scientist" };

	private static List<TraderSpec>? _all;

	public static IReadOnlyList<TraderSpec> All => _all ??= Load();

	private static List<TraderSpec> Load()
	{
		var list = new List<TraderSpec>();
		foreach (var id in Order)
		{
			if (GD.Load<TraderData>($"res://data/traders/{id}.tres") is { } data)
				list.Add(data.ToSpec());
		}
		return list;
	}

	public static TraderSpec? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
