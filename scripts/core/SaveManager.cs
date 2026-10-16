using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Autoload (CLAUDE.md approved list): loads the single autosave at boot into
/// <see cref="RunState.Current"/> and writes it at raid start (loadout at risk) and raid end.
/// All rules live in the pure classes; this node only touches files and the engine.
/// Dev args after "--": --nosave (no load, no write), --savepath=&lt;file&gt; (override location). Headless,
/// --autonew and --shot runs disable saving unless --savepath is given (see <see cref="SavePathPolicy"/>).
/// </summary>
public partial class SaveManager : Node
{
	public static SaveManager? Instance { get; private set; }

	private SaveFileStore? _store;

	public override void _Ready()
	{
		Instance = this;
		var path = SavePathPolicy.Resolve(OS.GetCmdlineUserArgs(), DisplayServer.GetName() == "headless",
			ProjectSettings.GlobalizePath("user://save.json"), ProjectSettings.GlobalizePath);
		if (path is null)
			return;
		_store = new SaveFileStore(path);
		Load();
	}

	/// <summary>Writes the loadout the player carries into the raid, marked in progress.</summary>
	public void BeginRaid(CharacterInventory carried) => Write(carried, true);

	/// <summary>Writes the state after the outcome rules were applied and recorded.</summary>
	public void EndRaid() => Write(RunState.Current.Inventory, false);

	/// <summary>Writes the profile and character after a hub change (stash move, side choice) and on hub entry.</summary>
	public void SaveInHub() => Write(RunState.Current.Inventory, false);

	/// <summary>The trader level a given trader is at now (for insurance terms).</summary>
	public static TraderLevelSpec? LevelOf(string traderId)
		=> TraderCatalog.Find(traderId)?.LevelSpec((GameState.Instance?.Profile ?? new GameProfile()).Trader(traderId).Level);

	private static GameProfile Profile => GameState.Instance?.Profile ?? new GameProfile();

	private void Write(CharacterInventory? character, bool raidInProgress)
	{
		if (_store is null)
			return;
		var file = SaveSerializer.ToFile(RunState.Current, Profile, character, raidInProgress);
		if (!_store.TryWrite(SaveSerializer.ToJson(file), out var error))
			GD.PushError($"[Save] could not write the save: {error}");
	}

	private void Load()
	{
		SaveFile? file;
		SaveSource source;
		try
		{
			(file, source) = _store!.Load(json => SaveSerializer.Parse(json),
				(path, e) => GD.PushWarning($"[Save] unreadable file moved aside ({path}): {e.Message}"));
		}
		catch (SaveTooNewException e)
		{
			GD.PushError($"[Save] {e.Message}; saving is disabled this session and the file is left untouched");
			_store = null;
			return;
		}
		if (file is null)
		{
			GD.Print("SAVE none, starting a new profile");
			return;
		}
		if (source == SaveSource.Backup)
			GD.PushWarning("[Save] main save unreadable, loaded the backup");
		var warnings = new List<string>();
		SaveSerializer.Apply(file, RunState.Current, Profile, id => ItemDb.Instance?.Registry.Spec(id), warnings);
		foreach (var w in warnings)
			GD.PushWarning($"[Save] {w}");
		GD.Print($"SAVE loaded raids={RunState.Current.Raids} inRaid={file.RaidInProgress}");
		if (RaidRecovery.Apply(file.RaidInProgress, RunState.Current, lost =>
			InsuranceService.OnRaidEnd(RaidOutcome.Killed, lost, Profile, RunState.Current.Inventory, LevelOf, Random.Shared.NextDouble)))
		{
			GD.Print("SAVE raid was in progress at launch: counted as death");
			Write(RunState.Current.Inventory, false);
		}
	}
}
