using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ProjectD.Tests;

public class SkillTests
{
	private static readonly float[] Table = { 0, 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 };

	private static SkillSpec Spec(string id = "endurance", float decay = 0.9f, float floor = 0.1f)
		=> new(id, Table, new Dictionary<string, float> { ["sprint"] = 10f, ["mantle"] = 20f }, decay, floor);

	private static SkillBook Book(params SkillSpec[] specs)
	{
		var b = new SkillBook();
		foreach (var s in specs.DefaultIfEmpty(Spec()))
			b.Register(s);
		return b;
	}

	// ---- levels

	[Theory]
	[InlineData(0f, 1)]
	[InlineData(99.9f, 1)]
	[InlineData(100f, 2)]
	[InlineData(449f, 3)]
	[InlineData(450f, 4)]
	[InlineData(3199f, 9)]
	[InlineData(3200f, 10)]
	[InlineData(99999f, 10)]
	public void LevelsFollowTheCumulativeTable(float xp, int level)
		=> Assert.Equal(level, Spec().LevelFor(xp));

	[Fact]
	public void ProgressRunsFromZeroToOneWithinALevel()
	{
		var s = Spec();
		Assert.Equal(0f, s.Progress(100f));
		Assert.Equal(0.5f, s.Progress(175f), 3); // halfway from 100 to 250
		Assert.Equal(1f, s.Progress(5000f));
		Assert.Equal(250f, s.NextThreshold(2));
		Assert.Null(s.NextThreshold(10));
	}

	[Fact]
	public void BadTablesAreReported()
	{
		Assert.Empty(Spec().Problems());
		Assert.NotEmpty(new SkillSpec("x", new float[] { 0, 1 }, new Dictionary<string, float> { ["a"] = 1 }).Problems());
		Assert.NotEmpty(new SkillSpec("x", new float[] { 5, 10, 20, 30, 40, 50, 60, 70, 80, 90 }, new Dictionary<string, float> { ["a"] = 1 }).Problems());
		Assert.NotEmpty(new SkillSpec("x", new float[] { 0, 10, 10, 30, 40, 50, 60, 70, 80, 90 }, new Dictionary<string, float> { ["a"] = 1 }).Problems());
		Assert.NotEmpty(new SkillSpec("x", Table, new Dictionary<string, float>()).Problems());
		Assert.NotEmpty(new SkillSpec("x", Table, new Dictionary<string, float> { ["a"] = 0 }).Problems());
		Assert.NotEmpty(Spec(decay: 0f).Problems());
		Assert.NotEmpty(Spec(floor: 1.5f).Problems());
	}

	// ---- XP

	[Fact]
	public void XpOnlyCountsInARaid()
	{
		var b = Book();
		Assert.Equal(0f, b.Report("endurance", "sprint"));
		Assert.Equal(0f, b.Xp("endurance"));
		b.BeginRaid();
		Assert.Equal(10f, b.Report("endurance", "sprint"));
		b.EndRaid();
		Assert.Equal(0f, b.Report("endurance", "sprint"));
		Assert.Equal(10f, b.Xp("endurance"));
	}

	[Fact]
	public void RepeatingAnActionPaysLessThenHitsTheFloor()
	{
		var b = Book();
		b.BeginRaid();
		Assert.Equal(10f, b.Report("endurance", "sprint"), 3);
		Assert.Equal(9f, b.Report("endurance", "sprint"), 3);
		Assert.Equal(8.1f, b.Report("endurance", "sprint"), 3);
		for (var i = 0; i < 100; i++)
			b.Report("endurance", "sprint");
		Assert.Equal(1f, b.Report("endurance", "sprint"), 3); // floor: 10% of the base
	}

	[Fact]
	public void ActionsDecayIndependentlyAndCountersResetEachRaid()
	{
		var b = Book();
		b.BeginRaid();
		b.Report("endurance", "sprint");
		b.Report("endurance", "sprint");
		Assert.Equal(20f, b.Report("endurance", "mantle"), 3); // its own counter
		b.BeginRaid();
		Assert.Equal(10f, b.Report("endurance", "sprint"), 3);
	}

	[Fact]
	public void TheAmountScalesTheXp()
	{
		var b = Book();
		b.BeginRaid();
		Assert.Equal(35f, b.Report("endurance", "sprint", 3.5f), 3);
		Assert.Equal(0f, b.Report("endurance", "sprint", 0f));
		Assert.Equal(0f, b.Report("endurance", "sprint", -2f));
	}

	[Fact]
	public void UnknownSkillsAndActionsAreIgnored()
	{
		var b = Book();
		b.BeginRaid();
		Assert.Equal(0f, b.Report("telepathy", "sprint"));
		Assert.Equal(0f, b.Report("endurance", "dance"));
		Assert.Empty(b.Snapshot());
	}

	[Fact]
	public void LevelUpsFireOnceWithOldAndNewLevel()
	{
		var b = Book();
		var ups = new List<(string, int, int)>();
		b.LevelUp += (s, from, to) => ups.Add((s, from, to));
		b.BeginRaid();
		for (var i = 0; i < 9; i++)
			b.Report("endurance", "mantle", 1f); // about 20 + 18 + 16.2 ... passes 100 around the seventh
		Assert.Equal(new[] { ("endurance", 1, 2) }, ups);
		Assert.Equal(2, b.Level("endurance"));
		Assert.Equal(ups, b.RaidLevelUps);
	}

	[Fact]
	public void ABigGainCanSkipLevelsInOneEvent()
	{
		var b = Book();
		var ups = new List<(string, int, int)>();
		b.LevelUp += (s, f, t) => ups.Add((s, f, t));
		b.BeginRaid();
		b.Report("endurance", "mantle", 20f); // 400 XP
		Assert.Equal(new[] { ("endurance", 1, 3) }, ups);
	}

	[Fact]
	public void RaidGainsAreTrackedPerSkillAndClearedAtTheNextRaid()
	{
		var b = Book(Spec("endurance"), Spec("strength"));
		b.BeginRaid();
		b.Report("endurance", "sprint");
		b.Report("strength", "sprint");
		b.Report("strength", "sprint");
		Assert.Equal(10f, b.RaidGains["endurance"], 3);
		Assert.Equal(19f, b.RaidGains["strength"], 3);
		b.EndRaid();
		Assert.Equal(2, b.RaidGains.Count); // still there for the result screen
		b.BeginRaid();
		Assert.Empty(b.RaidGains);
		Assert.Empty(b.RaidLevelUps);
	}

	[Fact]
	public void XpIsKeptWhenTheRaidEndsInDeath()
	{
		var b = Book();
		b.BeginRaid();
		b.Report("endurance", "mantle", 5f);
		b.EndRaid(); // death and MIA end a raid like an extraction: nothing is taken away
		Assert.Equal(100f, b.Xp("endurance"), 3);
	}

	// ---- save

	[Fact]
	public void TotalsRoundTripAndBadValuesAreDropped()
	{
		var b = Book();
		b.BeginRaid();
		b.Report("endurance", "mantle", 6f);
		var saved = b.Snapshot();
		var b2 = Book();
		b2.Load(saved);
		Assert.Equal(b.Xp("endurance"), b2.Xp("endurance"));
		Assert.Equal(b.Level("endurance"), b2.Level("endurance"));

		b2.Load(new Dictionary<string, float> { ["endurance"] = float.NaN, ["strength"] = -5f, ["future_skill"] = 40f });
		Assert.Equal(0f, b2.Xp("endurance"));
		Assert.Equal(40f, b2.Snapshot()["future_skill"]); // a skill this build does not know keeps its XP
	}

	[Fact]
	public void SkillsSurviveASaveFileRoundTrip()
	{
		var profile = new GameProfile();
		profile.Skills.Register(Spec());
		profile.Skills.Load(new Dictionary<string, float> { ["endurance"] = 260f });
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, null, false));
		Assert.Contains("\"skills\"", json);

		var loaded = new GameProfile();
		loaded.Skills.Register(Spec());
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, _ => null, new List<string>());
		Assert.Equal(260f, loaded.Skills.Xp("endurance"));
		Assert.Equal(3, loaded.Skills.Level("endurance"));
	}

	[Fact]
	public void AVersion7SaveLoadsWithNoSkillXp()
	{
		const string v7 = """{"version":7,"raidInProgress":false,"counters":{"raids":2,"extractions":1,"deaths":1,"mia":0},"quickUse":[]}""";
		var file = SaveSerializer.Parse(v7);
		Assert.Equal(SaveFile.CurrentVersion, file.Version);
		Assert.True(SaveFile.CurrentVersion >= 8);
		Assert.NotNull(file.Skills);
		Assert.Empty(file.Skills!);
		var profile = new GameProfile();
		SaveSerializer.Apply(file, new RunProgress(), profile, _ => null, new List<string>());
		Assert.Empty(profile.Skills.Snapshot());
	}

	// ---- effects

	private static SkillEffects EffectsAt(string skill, int level)
	{
		var spec = new SkillSpec(skill, Table, new Dictionary<string, float> { ["a"] = 1f });
		var b = new SkillBook();
		b.Register(spec);
		b.Load(new Dictionary<string, float> { [skill] = Table[level - 1] });
		return new SkillEffects(b);
	}

	[Fact]
	public void EveryEffectIsNeutralAtLevelOne()
	{
		var e = new SkillEffects(new SkillBook());
		Assert.Equal(1f, e.StaminaMultiplier);
		Assert.Equal(0f, e.WeightBonusKg);
		Assert.Equal(1f, e.HandlingTimeMultiplier);
		Assert.False(e.ExactMagCheck);
		Assert.Equal(1f, e.RecoilMultiplier);
		Assert.Equal(1f, e.MedTimeMultiplier);
		Assert.Equal(1f, e.SearchTimeMultiplier);
		Assert.Equal(1f, e.MaxHpMultiplier);
		Assert.Equal(1f, e.HitDamageMultiplier);
	}

	[Fact]
	public void EveryEffectIsFullAtLevelTen()
	{
		Assert.Equal(1.3f, EffectsAt(SkillIds.Endurance, 10).StaminaMultiplier, 3);
		Assert.Equal(5f, EffectsAt(SkillIds.Strength, 10).WeightBonusKg, 3);
		Assert.Equal(0.75f, EffectsAt(SkillIds.WeaponHandling, 10).HandlingTimeMultiplier, 3);
		Assert.Equal(0.8f, EffectsAt(SkillIds.RecoilControl, 10).RecoilMultiplier, 3);
		Assert.Equal(0.7f, EffectsAt(SkillIds.FirstAid, 10).MedTimeMultiplier, 3);
		Assert.Equal(0.7f, EffectsAt(SkillIds.Searching, 10).SearchTimeMultiplier, 3);
		Assert.Equal(1.15f, EffectsAt(SkillIds.Vitality, 10).MaxHpMultiplier, 3);
		Assert.Equal(0.9f, EffectsAt(SkillIds.Vitality, 10).HitDamageMultiplier, 3);
	}

	[Fact]
	public void EffectsAreLinearBetweenTheEnds()
	{
		// level 5 is 4 of 9 steps
		Assert.Equal(1f + 0.3f * 4f / 9f, EffectsAt(SkillIds.Endurance, 5).StaminaMultiplier, 3);
		Assert.Equal(5f * 4f / 9f, EffectsAt(SkillIds.Strength, 5).WeightBonusKg, 3);
		Assert.Equal(1f - 0.2f * 4f / 9f, EffectsAt(SkillIds.RecoilControl, 5).RecoilMultiplier, 3);
	}

	[Fact]
	public void OneSkillDoesNotMoveAnotherSkillsEffect()
	{
		var e = EffectsAt(SkillIds.Endurance, 10);
		Assert.Equal(1f, e.RecoilMultiplier);
		Assert.Equal(1f, e.MaxHpMultiplier);
	}

	[Theory]
	[InlineData(7, false)]
	[InlineData(8, true)]
	[InlineData(10, true)]
	public void ExactMagCheckComesAtLevelEight(int level, bool exact)
		=> Assert.Equal(exact, EffectsAt(SkillIds.WeaponHandling, level).ExactMagCheck);

	[Fact]
	public void VitalityTakesAShareOffHitDamage()
	{
		var e = EffectsAt(SkillIds.Vitality, 10);
		Assert.Equal(45f, e.AfterVitality(50f), 3);
		Assert.Equal(0f, e.AfterVitality(0f));
		Assert.Equal(0f, e.AfterVitality(-5f));
	}

	[Fact]
	public void TuningChangesTheEffect()
	{
		var b = new SkillBook();
		b.Register(Spec(SkillIds.Endurance));
		b.Load(new Dictionary<string, float> { [SkillIds.Endurance] = 3200f });
		Assert.Equal(1.5f, new SkillEffects(b, new SkillEffectTuning(EnduranceStamina: 0.5f)).StaminaMultiplier, 3);
	}

	// ---- shipped data

	private static string RepoFile(string rel)
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return Path.Combine(dir!, rel);
	}

	[Fact]
	public void TheSevenShippedSkillsHaveUsableNumbers()
	{
		var dir = RepoFile("data/skills");
		var files = Directory.GetFiles(dir, "*.tres");
		Assert.Equal(SkillIds.All.OrderBy(i => i), files.Select(Path.GetFileNameWithoutExtension).OrderBy(i => i));
		foreach (var file in files)
		{
			var text = File.ReadAllText(file);
			string Field(string name) => Regex.Match(text, $"^{name} = (.+)$", RegexOptions.Multiline).Groups[1].Value;
			float[] Floats(string name) => Regex.Match(Field(name), @"\((.*)\)").Groups[1].Value
				.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
				.Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
			var actions = Regex.Matches(Field("ActionIds"), "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
			var xp = Floats("ActionXp");
			Assert.Equal(actions.Length, xp.Length);
			var spec = new SkillSpec(Field("Id").Trim('"'), Floats("XpTable"), actions.Zip(xp).ToDictionary(p => p.First, p => p.Second));
			Assert.Empty(spec.Problems());
			Assert.Equal(Path.GetFileNameWithoutExtension(file), spec.Id);
			Assert.Equal($"\"skill.{spec.Id}\"", Field("NameKey"));
		}
	}

	[Fact]
	public void EverySkillHasADisplayNameKey()
	{
		var text = File.ReadAllText(RepoFile("scripts/core/GameText.cs"));
		foreach (var id in SkillIds.All)
			Assert.Contains($"[\"skill.{id}\"]", text);
	}
}
