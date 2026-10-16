using Godot;

namespace ProjectD;

/// <summary>One line of a starter kit in a .tres file (see <see cref="StarterKitData"/>).</summary>
[GlobalClass]
public partial class StarterKitEntryData : Resource
{
	[Export] public string ItemId { get; set; } = "";
	[Export] public int Count { get; set; } = 1;
	/// <summary>Magazines: ammo id loaded into each one (for example 7_62x39).</summary>
	[Export] public string AmmoId { get; set; } = "";
	/// <summary>Magazines: rounds loaded; -1 fills the magazine.</summary>
	[Export] public int Rounds { get; set; } = -1;

	public StarterEntry ToEntry() => new(ItemId, Count, string.IsNullOrEmpty(AmmoId) ? null : AmmoId, Rounds);
}
