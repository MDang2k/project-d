using Godot;

namespace ProjectD;

/// <summary>Where the player can start a raid (GDD s15: one of several, picked at random). Move in the editor.</summary>
[Tool]
public partial class PlayerSpawnPoint : Marker3D
{
	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("player_spawns");
	}
}
