using Godot;

namespace ProjectD;

/// <summary>
/// Autoload (CLAUDE.md approved list): holds the long-lived <see cref="GameProfile"/> (side, stash)
/// next to the in-memory run. <see cref="RunState.Current"/> stays the run holder so the player and
/// raid code need no changes. Created before SaveManager so the save can fill it at boot.
/// </summary>
public partial class GameState : Node
{
	public static GameState? Instance { get; private set; }

	public GameProfile Profile { get; } = new();

	public RunProgress Run => RunState.Current;

	public override void _Ready() => Instance = this;
}
