using Godot;

namespace ProjectD;

/// <summary>One quest objective in a .tres file (see <see cref="QuestData"/>).</summary>
[GlobalClass]
public partial class QuestObjectiveData : Resource
{
	[Export] public ObjectiveKind Kind { get; set; } = ObjectiveKind.Eliminate;
	[Export] public int Count { get; set; } = 1;
	/// <summary>Fetch, FindInRaid: item id; Extract: optional item that must be carried out.</summary>
	[Export] public string ItemId { get; set; } = "";
	/// <summary>Eliminate: faction name (Looters, Bandits, AlliedPmc, SovietPmc); empty means any.</summary>
	[Export] public string FactionName { get; set; } = "";
	[Export] public string MapId { get; set; } = "";
	/// <summary>Eliminate: Primary, Pistol or Melee; empty means any.</summary>
	[Export] public string WeaponClassName { get; set; } = "";
	/// <summary>Locate: quest zone id; Extract: extraction zone id.</summary>
	[Export] public string ZoneId { get; set; } = "";

	public ObjectiveSpec ToSpec() => new(Kind, Count,
		string.IsNullOrEmpty(ItemId) ? null : ItemId,
		System.Enum.TryParse<Faction>(FactionName, out var f) ? f : null,
		string.IsNullOrEmpty(MapId) ? null : MapId,
		System.Enum.TryParse<WeaponClass>(WeaponClassName, out var w) ? w : null,
		string.IsNullOrEmpty(ZoneId) ? null : ZoneId);
}
