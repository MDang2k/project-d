using Godot;

namespace ProjectD;

/// <summary>Where a player who comes in by a transit appears (world map spec s5). The point whose From matches the map they left is used.</summary>
[Tool]
public partial class ArrivalPoint : Marker3D
{
	/// <summary>Map id the player is coming from; empty matches any.</summary>
	[Export] public string From { get; set; } = "";

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("transit_arrivals");
	}
}
