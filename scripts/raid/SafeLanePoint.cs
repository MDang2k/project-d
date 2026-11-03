using Godot;

namespace ProjectD;

/// <summary>One waypoint of a safe lane through a minefield. Points with the same Lane join in Order; <see cref="MineField"/> keeps mines out of the corridor.</summary>
[Tool]
public partial class SafeLanePoint : Marker3D
{
	[Export] public string Lane { get; set; } = "";
	[Export] public int Order { get; set; }

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("safe_lanes");
	}
}
