using System.Linq;
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

	private const string SkillsDir = "res://data/skills";

	public override void _Ready()
	{
		Instance = this;
		LoadSkills();
	}

	/// <summary>Registers every skill in data/skills with the profile's skill book; bad numbers are skipped with a warning.</summary>
	private void LoadSkills()
	{
		using var dir = DirAccess.Open(SkillsDir);
		if (dir is null)
		{
			GD.PushWarning($"[Skills] {SkillsDir} not found");
			return;
		}
		dir.ListDirBegin();
		for (var name = dir.GetNext(); name != ""; name = dir.GetNext())
		{
			var file = name.EndsWith(".remap") ? name[..^".remap".Length] : name;
			if (dir.CurrentIsDir() || !file.EndsWith(".tres") || GD.Load<SkillData>($"{SkillsDir}/{file}") is not { } data)
				continue;
			var spec = data.ToSpec();
			if (spec.Problems() is { Count: > 0 } problems)
				GD.PushWarning($"[Skills] {file}: {string.Join("; ", problems)}");
			else
				Profile.Skills.Register(spec);
		}
		dir.ListDirEnd();
		GD.Print($"SKILLS registered {Profile.Skills.Specs.Count()}");
	}
}
