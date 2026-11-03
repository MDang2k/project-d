using Godot;

namespace ProjectD;

/// <summary>One waypoint of a boss squad's roaming route (raid map standard s5). Points join in Order; <see cref="BossSpawner"/> reads them from the group "boss_route".</summary>
[Tool]
public partial class BossRoutePoint : Marker3D
{
	/// <summary>Which squad walks this route, for maps with more than one.</summary>
	[Export] public string Squad { get; set; } = "";
	[Export] public int Order { get; set; }

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("boss_route");
	}
}
