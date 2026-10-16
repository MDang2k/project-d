using Godot;

namespace ProjectD;

/// <summary>One quest reward in a .tres file (see <see cref="QuestData"/>).</summary>
[GlobalClass]
public partial class QuestRewardData : Resource
{
	[Export] public RewardKind Kind { get; set; } = RewardKind.Money;
	/// <summary>Money: amount; Item: count; Reputation: delta.</summary>
	[Export] public int Amount { get; set; }
	[Export] public string ItemId { get; set; } = "";
	/// <summary>Trader id, "side_qm" or "opposite_qm".</summary>
	[Export] public string TraderId { get; set; } = "";
	[Export] public int Level { get; set; }
	[Export] public string Target { get; set; } = "";

	public RewardSpec ToSpec() => new(Kind, Amount,
		string.IsNullOrEmpty(ItemId) ? null : ItemId,
		string.IsNullOrEmpty(TraderId) ? null : TraderId,
		Level,
		string.IsNullOrEmpty(Target) ? null : Target);
}
