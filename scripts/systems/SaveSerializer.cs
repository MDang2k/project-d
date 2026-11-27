using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>A completely read save: nothing in it is shared with the live game.</summary>
public sealed record LoadedSave(bool RaidInProgress, RunProgress Run, GameProfile Profile, IReadOnlyList<string> Warnings);

/// <summary>
/// The single autosave (GDD s15) to and from JSON, one <see cref="ISavePart"/> per piece of state. Pure logic: item
/// specs are supplied through a lookup so nothing here needs the engine. Bump <see cref="CurrentVersion"/> with a
/// <see cref="SaveMigrator"/> step when the shape changes.
/// </summary>
public static class SaveSerializer
{
	public const int CurrentVersion = 10;

	/// <summary>Every save part over this state. The order here is the load order.</summary>
	/// <param name="carried">The gear to write, which at raid start is not yet the run's.</param>
	public static IReadOnlyList<ISavePart> Parts(RunProgress run, GameProfile profile, CharacterInventory? carried) => new ISavePart[]
	{
		new CharacterPart(run, carried),
		new CountersPart(run),
		new SidePart(profile),
		new StashPart(profile),
		new TradersPart(profile),
		new QuestsPart(profile.Quests),
		new ExaminedPartsPart(profile),
		new PresetsPart(profile),
		new SkillsPart(profile.Skills),
		new KnowledgePart(profile.Knowledge),
		new NemesesPart(profile.Nemeses),
		new InsuranceReturnsPart(profile.InsuranceReturns),
		new QuickUsePart(run.QuickUse),
	};

	public static string ToJson(RunProgress run, GameProfile profile, CharacterInventory? carried, bool raidInProgress)
	{
		var root = new JsonObject { ["version"] = CurrentVersion, ["raidInProgress"] = raidInProgress };
		foreach (var part in Parts(run, profile, carried))
			part.Write(root);
		return root.ToJsonString(SaveJson.Options);
	}

	/// <summary>Reads a whole save or throws; there is no half-loaded result.</summary>
	/// <param name="fresh">A blank profile to read into, with whatever numbers the game registers on it.</param>
	public static LoadedSave Parse(string json, Func<string, ItemSpec?> lookup, GameProfile? fresh = null)
	{
		var root = SaveMigrator.Default.Migrate(json);
		var run = new RunProgress();
		var profile = fresh ?? new GameProfile();
		var ctx = new LoadContext(lookup);
		foreach (var part in Parts(run, profile, null))
			part.Read(root, ctx);
		// Only now has every part had the chance to detach something.
		foreach (var orphan in ctx.Detached)
		{
			if (!profile.Stash.TryPlaceAnywhere(orphan))
				ctx.Warnings.Add($"stash full: detached part '{orphan.Spec.Id}' was dropped");
		}
		return new LoadedSave(SaveJson.Get<bool>(root, "raidInProgress"), run, profile, ctx.Warnings);
	}
}
