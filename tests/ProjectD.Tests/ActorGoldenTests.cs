using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ProjectD.Tests;

/// <summary>
/// The old data/ai/*.tres presets are the oracle: each actor built from the exported sheets must give the same
/// tuning numbers (composable actors spec s6). Defaults are those of the former AiPreset class.
/// </summary>
public class ActorGoldenTests
{
	public static IEnumerable<object[]> Ids => new[] { "looter", "bandit", "allied_pmc", "soviet_pmc" }.Select(i => new object[] { i });

	private static string Root()
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return dir!;
	}

	private static float Old(string id, string field, float fallback)
	{
		var text = File.ReadAllText(Path.Combine(Root(), "data", "ai", id + ".tres"));
		var m = Regex.Match(text, "^" + field + @" = (-?[0-9.]+)\s*$", RegexOptions.Multiline);
		return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
	}

	private static string OldText(string id, string field)
	{
		var text = File.ReadAllText(Path.Combine(Root(), "data", "ai", id + ".tres"));
		var m = Regex.Match(text, "^" + field + @" = (.*)$", RegexOptions.Multiline);
		return m.Success ? m.Groups[1].Value.Trim() : "";
	}

	private static StatSet Actor(string id)
		=> ActorCatalog.Parse(File.ReadAllText(Path.Combine(Root(), "data", "actors", "actors.json"))).Create(id).Stats;

	[Theory, MemberData(nameof(Ids))]
	public void PerceptionMatches(string id)
	{
		var expected = new PerceptionTuning(
			Old(id, "ViewDistance", 80f), Old(id, "FovDeg", 110f), Old(id, "PeripheralDeg", 200f),
			Old(id, "PeripheralDistance", 8f), Old(id, "CrouchVisibility", 0.65f), Old(id, "ProneVisibility", 0.4f),
			Old(id, "StillVisibility", 0.8f), Old(id, "MovingVisibility", 1.25f), Old(id, "HearingMultiplier", 1f),
			Old(id, "NoticeSeconds", 1.2f), Old(id, "SuspicionThreshold", 0.35f), Old(id, "HoldSeconds", 3f),
			Old(id, "DecayPerS", 0.15f));
		Assert.Equal(expected, ActorTuning.Perception(Actor(id)));
	}

	[Theory, MemberData(nameof(Ids))]
	public void BrainAndTacticsMatch(string id)
	{
		var s = Actor(id);
		Assert.Equal(new BrainTuning(Old(id, "ArrivalRadius", 1.2f), Old(id, "LookAroundS", 3f), Old(id, "LostTargetS", 4f),
			Old(id, "SearchS", 10f), Old(id, "PatrolWaitS", 2f)), ActorTuning.Brain(s));
		Assert.Equal(TacticsTuning.Default with
		{
			Courage = Old(id, "Courage", 0.5f),
			Aggression = Old(id, "Aggression", 0.5f),
			EngageDistance = Old(id, "EngageDistance", 25f),
			BurstsBeforeCover = (int)Old(id, "BurstsBeforeCover", 3f),
		}, ActorTuning.Tactics(s));
	}

	[Theory, MemberData(nameof(Ids))]
	public void AimBurstSpeedCarryAndHealthMatch(string id)
	{
		var s = Actor(id);
		Assert.Equal(2f, ActorTuning.Aim(s).ErrorMaxDeg);
		Assert.Equal(1.8f, ActorTuning.Aim(s).MovingMult);
		Assert.Equal(2.5f, ActorTuning.Aim(s).SuppressedMult);
		Assert.Equal(Old(id, "Accuracy", 0.5f), s.Get(Stats.Aim.Accuracy));
		Assert.Equal((int)Old(id, "BurstMin", 2f), s.GetInt(Stats.Fire.BurstMin));
		Assert.Equal((int)Old(id, "BurstMax", 5f), s.GetInt(Stats.Fire.BurstMax));
		Assert.Equal(Old(id, "BurstPauseMinS", 0.5f), s.Get(Stats.Fire.BurstPauseMinS));
		Assert.Equal(Old(id, "BurstPauseMaxS", 1.4f), s.Get(Stats.Fire.BurstPauseMaxS));
		Assert.Equal(Old(id, "ReactionS", 0.6f), s.Get(Stats.Fire.ReactionS));
		Assert.Equal(Old(id, "WalkSpeed", 1.6f), s.Get(Stats.Move.WalkSpeed));
		Assert.Equal(Old(id, "RunSpeed", 3.4f), s.Get(Stats.Move.RunSpeed));
		Assert.Equal(Old(id, "LootRadius", 35f), s.Get(Stats.Scavenge.LootRadius));
		Assert.Equal(Old(id, "ArmorChance", 1f), s.Get(Stats.Loadout.ArmorChance));
		Assert.Equal(Old(id, "BackpackChance", 0.5f), s.Get(Stats.Carry.BackpackChance));
		Assert.Equal((int)Old(id, "SpareMagazines", 2f), s.GetInt(Stats.Carry.SpareMagazines));
		Assert.Equal(new[] { 35, 85, 70, 60, 65 }, new[] { Stats.Health.Head, Stats.Health.Thorax, Stats.Health.Stomach, Stats.Health.Arm, Stats.Health.Leg }.Select(s.GetInt));
	}

	[Theory, MemberData(nameof(Ids))]
	public void FactionAndKitMatch(string id)
	{
		var def = ActorCatalog.Parse(File.ReadAllText(Path.Combine(Root(), "data", "actors", "actors.json"))).Get(id);
		Assert.Equal((Faction)(int)Old(id, "Faction", 0f), ActorTuning.ParseFaction(def.Faction));
		Assert.Equal(OldText(id, "RigId").Trim('"'), def.Text("rig"));
		Assert.Equal(OldText(id, "BackpackId").Trim('"'), def.Text("backpack"));
		Assert.Equal(OldText(id, "LootsCorpses") == "true", def.Flag("loots_corpses"));
		var color = ActorTuning.ParseColor(def.List("body_color"));
		var old = Regex.Match(OldText(id, "BodyColor"), @"Color\(([^,]+), ([^,]+), ([^,]+),").Groups;
		Assert.Equal(float.Parse(old[1].Value, CultureInfo.InvariantCulture), color!.Value.R);
		Assert.Equal(float.Parse(old[2].Value, CultureInfo.InvariantCulture), color.Value.G);
		Assert.Equal(float.Parse(old[3].Value, CultureInfo.InvariantCulture), color.Value.B);
	}

	[Fact]
	public void FactionNamesParse()
	{
		Assert.Equal(Faction.AlliedPmc, ActorTuning.ParseFaction("Allied PMC"));
		Assert.Equal(Faction.SovietPmc, ActorTuning.ParseFaction("soviet_pmc"));
		Assert.Throws<ArgumentException>(() => ActorTuning.ParseFaction("aliens"));
	}
}
