using System;
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
		var json = SaveSerializer.ToJson(RunState.Current, Profile, character, raidInProgress);
		if (!_store.TryWrite(json, out var error))
			GD.PushError($"[Save] could not write the save: {error}");
	}

	private void Load()
	{
		LoadedSave? save;
		SaveSource source;
		try
		{
			// All or nothing: the parts fill fresh state, and only a complete result replaces the live one.
			(save, source) = _store!.Load(
				json => SaveSerializer.Parse(json, id => ItemDb.Instance?.Registry.Spec(id), GameState.Instance?.NewProfile()),
				(path, e) => GD.PushWarning($"[Save] unreadable file moved aside ({path}): {e.Message}"));
		}
		catch (SaveTooNewException e)
		{
			GD.PushError($"[Save] {e.Message}; saving is disabled this session and the file is left untouched");
			_store = null;
			return;
		}
		if (save is null)
		{
			GD.Print("SAVE none, starting a new profile");
			return;
		}
		if (source == SaveSource.Backup)
			GD.PushWarning("[Save] main save unreadable, loaded the backup");
		RunState.Current = save.Run;
		GameState.Instance?.ReplaceProfile(save.Profile);
		foreach (var w in save.Warnings)
			GD.PushWarning($"[Save] {w}");
		GD.Print($"SAVE loaded raids={RunState.Current.Raids} inRaid={save.RaidInProgress}");
		if (RaidRecovery.Apply(save.RaidInProgress, RunState.Current, lost =>
			InsuranceService.OnRaidEnd(RaidOutcome.Killed, lost, Profile, RunState.Current.Inventory, LevelOf, Random.Shared.NextDouble)))
		{
			GD.Print("SAVE raid was in progress at launch: counted as death");
			Write(RunState.Current.Inventory, false);
		}
	}
}
