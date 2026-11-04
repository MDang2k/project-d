using System.Linq;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>A quest (GDD s14) in data/quests/*.tres. All text goes through GameText keys.</summary>
[GlobalClass]
public partial class QuestData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string TitleKey { get; set; } = "";
	[Export] public string DescriptionKey { get; set; } = "";
	/// <summary>Trader id, or "side_qm" for your side's quartermaster.</summary>
	[Export] public string GiverTraderId { get; set; } = "";
	[Export] public int MinTraderLevel { get; set; } = 1;
	[Export] public string[] RequiresQuestIds { get; set; } = System.Array.Empty<string>();
	/// <summary>Allied or Soviet; empty means either side.</summary>
	[Export] public string RequiresSide { get; set; } = "";
	/// <summary>Knowledge ids that must be unlocked (and translated) before this quest is available.</summary>
	[Export] public string[] RequiresKnowledgeIds { get; set; } = System.Array.Empty<string>();
	[Export] public Array<QuestObjectiveData> Objectives { get; set; } = new();
	[Export] public Array<QuestRewardData> Rewards { get; set; } = new();

	public QuestSpec ToSpec() => new(Id, TitleKey, DescriptionKey, GiverTraderId, MinTraderLevel,
		RequiresQuestIds.ToList(),
		System.Enum.TryParse<PlayerSide>(RequiresSide, out var side) ? side : null,
		Objectives.Select(o => o.ToSpec()).ToList(),
		Rewards.Select(r => r.ToSpec()).ToList(),
		RequiresKnowledgeIds.ToList());
}
