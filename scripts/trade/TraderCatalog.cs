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
	private static readonly System.Collections.Generic.Dictionary<string, TraderData> _data = new();

	public static IReadOnlyList<TraderSpec> All => _all ??= Load();

	private static List<TraderSpec> Load()
	{
		var list = new List<TraderSpec>();
		foreach (var id in Order)
		{
			if (GD.Load<TraderData>($"res://data/traders/{id}.tres") is { } data)
			{
				list.Add(data.ToSpec());
				_data[id] = data;
			}
		}
		return list;
	}

	public static TraderSpec? Find(string id) => All.FirstOrDefault(t => t.Id == id);

	/// <summary>The trader's portrait and display name key, revealed once its reveal quest is completed.</summary>
	public static (Texture2D? Portrait, string NameKey) Face(string id, QuestLog quests)
	{
		_ = All;
		if (!_data.TryGetValue(id, out var d))
			return (null, "");
		var revealed = TraderReveal.IsRevealed(d.RevealQuestId,
			q => quests.Entries.TryGetValue(q, out var e) && e.State == QuestState.Completed);
		return revealed
			? (d.RevealedPortrait ?? d.Portrait, d.RevealedNameKey != "" ? d.RevealedNameKey : d.NameKey)
			: (d.Portrait, d.NameKey);
	}
}
