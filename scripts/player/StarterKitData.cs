using System.Linq;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>
/// A side's starter kit (GDD s5, s10): what a new profile gets in its stash, and the safety-net kit
/// issued when the player has no weapon anywhere. Edit in data/player/starter_*.tres.
/// </summary>
[GlobalClass]
public partial class StarterKitData : Resource
{
	[Export] public Array<StarterKitEntryData> Entries { get; set; } = new();
	[Export] public Array<StarterKitEntryData> SafetyNet { get; set; } = new();

	public StarterKitSpec ToSpec() => new(
		Entries.Select(e => e.ToEntry()).ToList(),
		SafetyNet.Select(e => e.ToEntry()).ToList());

	public static StarterKitSpec For(PlayerSide side)
		=> GD.Load<StarterKitData>($"res://data/player/starter_{side.ToString().ToLowerInvariant()}.tres").ToSpec();
}
