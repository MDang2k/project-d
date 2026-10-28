using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>
/// Authored animation for one weapon or weapon family (animation spec s8): which clips play its reloads and where
/// the events of the clips fall. Clips are Blender actions exported inside the weapon .glb (or in an optional library
/// .glb); see docs/art/weapon-action-animation-guide.md. Anything left empty keeps the procedural choreography.
/// </summary>
[GlobalClass]
public partial class AnimationSetData : Resource
{
	/// <summary>Family id, for bookkeeping (ak, fal, ar, mg, pistol...).</summary>
	[Export] public string Family { get; set; } = "";

	/// <summary>Optional .glb whose AnimationPlayer holds the clips; empty means the weapon model itself.</summary>
	[Export(PropertyHint.File, "*.glb")] public string LibraryPath { get; set; } = "";

	[ExportGroup("Reload")]
	[Export] public string TacticalReloadClip { get; set; } = "Reload_Tactical";
	[Export] public string FastReloadClip { get; set; } = "Reload_Fast";

	/// <summary>
	/// Event name (MagDetached, MagStowed, MagFetched, MagInserted, BoltRacked) to seconds in the clip. Events not
	/// listed fall at the usual proportions of the clip length.
	/// </summary>
	[Export] public Dictionary<string, float> TacticalEvents { get; set; } = new();
	[Export] public Dictionary<string, float> FastEvents { get; set; } = new();
}
