using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Autoload (CLAUDE.md approved list): loads the single autosave at boot into
/// <see cref="RunState.Current"/> and writes it at raid start (loadout at risk) and raid end.
/// All rules live in the pure classes; this node only touches files and the engine.
/// Dev args after "--": --nosave (no load, no write), --savepath=&lt;file&gt; (override location).
/// </summary>
public partial class SaveManager : Node
{
	public static SaveManager? Instance { get; private set; }

	private SaveFileStore? _store;

	public override void _Ready()
	{
		Instance = this;
		var args = OS.GetCmdlineUserArgs();
		if (args.Contains("--nosave"))
			return;
		var path = args.FirstOrDefault(a => a.StartsWith("--savepath="))?["--savepath=".Length..]
			?? ProjectSettings.GlobalizePath("user://save.json");
		_store = new SaveFileStore(path);
		Load();
	}

	/// <summary>Writes the loadout the player carries into the raid, marked in progress.</summary>
	public void BeginRaid(CharacterInventory carried) => Write(carried, true);

	/// <summary>Writes the state after the outcome rules were applied and recorded.</summary>
	public void EndRaid() => Write(RunState.Current.Inventory, false);

	private void Write(CharacterInventory? character, bool raidInProgress)
	{
		if (_store is null)
			return;
		var file = SaveSerializer.ToFile(RunState.Current, character, raidInProgress);
		_store.Write(SaveSerializer.ToJson(file));
	}

	private void Load()
	{
		var (file, source) = _store!.Load(json => SaveSerializer.Parse(json));
		if (file is null)
		{
			GD.Print("SAVE none, starting a new profile");
			return;
		}
		if (source == SaveSource.Backup)
			GD.PushWarning("[Save] main save unreadable, loaded the backup");
		var warnings = new List<string>();
		SaveSerializer.Apply(file, RunState.Current, id => ItemDb.Instance?.Registry.Spec(id), warnings);
		foreach (var w in warnings)
			GD.PushWarning($"[Save] {w}");
		GD.Print($"SAVE loaded raids={RunState.Current.Raids} inRaid={file.RaidInProgress}");
		if (RaidRecovery.Apply(file.RaidInProgress, RunState.Current))
		{
			GD.Print("SAVE raid was in progress at launch: counted as death");
			Write(RunState.Current.Inventory, false);
		}
	}
}
