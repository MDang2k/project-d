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

	/// <summary>Hub portrait (character visuals spec s2), assets/textures/portraits/&lt;id&gt;.png.</summary>
	[Export] public Texture2D? Portrait { get; set; }
	/// <summary>Portrait shown once <see cref="RevealQuestId"/> is completed (Weiss after mission 4).</summary>
	[Export] public Texture2D? RevealedPortrait { get; set; }
	/// <summary>Quest whose completion swaps in the revealed portrait and name; empty = never.</summary>
	[Export] public string RevealQuestId { get; set; } = "";
	[Export] public string RevealedNameKey { get; set; } = "";

	public TraderSpec ToSpec() => new(Id, NameKey, Unlock,
		Unlock == TraderUnlock.ForSide ? UnlockSide : null,
		System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(Levels, l => l.ToSpec())));
}
