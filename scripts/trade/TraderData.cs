using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>A trader with four levels, in data/traders/*.tres.</summary>
[GlobalClass]
public partial class TraderData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string NameKey { get; set; } = "";
	[Export] public TraderUnlock Unlock { get; set; } = TraderUnlock.Always;
	[Export] public PlayerSide UnlockSide { get; set; } = PlayerSide.Allied;
	[Export] public Array<TraderLevelData> Levels { get; set; } = new();

	public TraderSpec ToSpec() => new(Id, NameKey, Unlock,
		Unlock == TraderUnlock.ForSide ? UnlockSide : null,
		System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(Levels, l => l.ToSpec())));
}
