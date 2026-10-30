using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class StatRegistryTests
{
	[Fact]
	public void IdsAreUniqueDottedLowercaseAndDefaultsFitTheirLimits()
	{
		Assert.Equal(Stats.All.Count, Stats.All.Select(s => s.Id).Distinct().Count());
		foreach (var s in Stats.All)
		{
			Assert.Matches("^[a-z]+\\.[a-z0-9_]+$", s.Id);
			Assert.True(s.Min <= s.Default && s.Default <= s.Max, s.Id);
			Assert.NotEmpty(s.Group);
		}
	}

	[Fact]
	public void EveryConstantInTheGroupClassesIsRegistered()
	{
		var constants = typeof(Stats).GetNestedTypes()
			.SelectMany(t => t.GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!))
			.ToList();
		Assert.True(constants.Count >= 40);
		foreach (var id in constants)
			Assert.True(Stats.Exists(id), id);
		Assert.Equal(constants.Count, Stats.All.Count);
	}

	[Fact]
	public void UnknownIdsFailLoudly()
	{
		Assert.False(Stats.Exists("perception.view_distanse"));
		Assert.Throws<ArgumentException>(() => Stats.Get("nope.nothing"));
		Assert.Throws<ArgumentException>(() => new StatSet().Get("typo"));
		Assert.Throws<ArgumentException>(() => new StatSet().Add(new StatModifier("typo", ModOp.Add, 1f, "x")));
	}

	[Fact]
	public void DefaultsMatchTheFormerAiPreset()
	{
		Assert.Equal(80f, Stats.Get(Stats.Perception.ViewDistance).Default);
		Assert.Equal(1.2f, Stats.Get(Stats.Awareness.NoticeS).Default);
		Assert.Equal(25f, Stats.Get(Stats.Tactics.EngageDistance).Default);
		Assert.Equal(2f, Stats.Get(Stats.Aim.ErrorMaxDeg).Default);
		Assert.Equal(35f, Stats.Get(Stats.Health.Head).Default);
		Assert.True(Stats.Get(Stats.Fire.BurstMax).Integer);
	}
}

public class StatResolverTests
{
	private static readonly StatDef Plain = new("test.value", 10f, 0f, 1000f);
	private static StatModifier M(ModOp op, float v, string source = "s", int priority = 0) => new("test.value", op, v, source, priority);

	[Fact]
	public void NoModifiersGivesTheBase()
		=> Assert.Equal(10f, StatResolver.Resolve(Plain, 10f, Array.Empty<StatModifier>()));

	[Fact]
	public void PercentagesAddUpInsteadOfCompounding()
	{
		var v = StatResolver.Resolve(Plain, 100f, new[] { M(ModOp.Pct, 0.1f, "a"), M(ModOp.Pct, 0.1f, "b") });
		Assert.Equal(120f, v, 3); // +20%, not +21%
	}

	[Fact]
	public void FlatAddsComeBeforeThePercentage()
	{
		var v = StatResolver.Resolve(Plain, 100f, new[] { M(ModOp.Pct, 0.5f), M(ModOp.Add, 20f) });
		Assert.Equal(180f, v, 3); // (100 + 20) x 1.5
	}

	[Fact]
	public void NegativePercentagesReduceAndNeverGoBelowZeroFactor()
	{
		Assert.Equal(75f, StatResolver.Resolve(Plain, 100f, new[] { M(ModOp.Pct, -0.25f) }), 3);
		Assert.Equal(0f, StatResolver.Resolve(Plain, 100f, new[] { M(ModOp.Pct, -2f) }), 3);
	}

	[Fact]
	public void MinAndMaxClampTheResult()
	{
		Assert.Equal(50f, StatResolver.Resolve(Plain, 10f, new[] { M(ModOp.Min, 50f) }), 3);
		Assert.Equal(20f, StatResolver.Resolve(Plain, 100f, new[] { M(ModOp.Max, 20f) }), 3);
		// several: the highest floor and the lowest cap win
		Assert.Equal(60f, StatResolver.Resolve(Plain, 10f, new[] { M(ModOp.Min, 40f, "a"), M(ModOp.Min, 60f, "b") }), 3);
		Assert.Equal(30f, StatResolver.Resolve(Plain, 100f, new[] { M(ModOp.Max, 30f, "a"), M(ModOp.Max, 70f, "b") }), 3);
	}

	[Fact]
	public void TheLimitsOfTheStatAlwaysHold()
	{
		Assert.Equal(1000f, StatResolver.Resolve(Plain, 100f, new[] { M(ModOp.Pct, 50f) }), 3);
		Assert.Equal(0f, StatResolver.Resolve(Plain, 5f, new[] { M(ModOp.Add, -50f) }), 3);
	}

	[Fact]
	public void SetWinsByPriorityThenByLatest()
	{
		var low = M(ModOp.Set, 5f, "low", 0);
		var high = M(ModOp.Set, 7f, "high", 5);
		var later = M(ModOp.Set, 9f, "later", 5);
		Assert.Equal(5f, StatResolver.Resolve(Plain, 100f, new[] { low }), 3);
		Assert.Equal(7f, StatResolver.Resolve(Plain, 100f, new[] { low, high }), 3);
		Assert.Equal(9f, StatResolver.Resolve(Plain, 100f, new[] { low, high, later }), 3);
		Assert.Equal(5f, StatResolver.Resolve(Plain, 100f, new[] { M(ModOp.Add, 500f), low }), 3); // Set ignores the rest
	}

	[Fact]
	public void WholeNumberStatsAreRounded()
	{
		var whole = new StatDef("test.count", 3f, 0f, 100f, Integer: true);
		Assert.Equal(4f, StatResolver.Resolve(whole, 3f, new[] { new StatModifier("test.count", ModOp.Pct, 0.25f, "x") }), 3); // 3.75
		Assert.Equal(3f, StatResolver.Resolve(whole, 3f, new[] { new StatModifier("test.count", ModOp.Pct, 0.1f, "x") }), 3);  // 3.3
	}

	[Fact]
	public void ExplainListsTheModifiersInTheOrderTheyAct()
	{
		var b = StatResolver.Explain(Plain, 100f, new[] { M(ModOp.Pct, 0.1f, "veteran"), M(ModOp.Add, 5f, "kit") });
		Assert.Equal(115.5f, b.Final, 3);
		Assert.Equal(new[] { "kit", "veteran" }, b.Modifiers.Select(m => m.Source));
		Assert.Contains("base 100", b.ToString());
		Assert.Contains("veteran +10%", b.ToString());
	}

	[Fact]
	public void SameInputsGiveTheSameResultInAnyOrderForAddAndPct()
	{
		var mods = new[] { M(ModOp.Add, 3f, "a"), M(ModOp.Pct, 0.2f, "b"), M(ModOp.Add, 4f, "c"), M(ModOp.Pct, -0.05f, "d") };
		var forward = StatResolver.Resolve(Plain, 50f, mods);
		var reversed = StatResolver.Resolve(Plain, 50f, mods.Reverse());
		Assert.Equal(forward, reversed, 4);
	}
}

public class StatSetTests
{
	private const string View = Stats.Perception.ViewDistance;

	[Fact]
	public void MissingBasesUseTheRegistryDefault()
	{
		var set = new StatSet();
		Assert.Equal(80f, set.Get(View));
		var custom = new StatSet(new Dictionary<string, float> { [View] = 120f });
		Assert.Equal(120f, custom.Get(View));
		Assert.Throws<ArgumentException>(() => new StatSet(new Dictionary<string, float> { ["perception.nope"] = 1f }));
	}

	[Fact]
	public void ModifiersChangeTheResolvedValueAndRemovingBringsItBack()
	{
		var set = new StatSet();
		set.Add(new StatModifier(View, ModOp.Pct, 0.25f, "veteran"));
		Assert.Equal(100f, set.Get(View), 3);
		Assert.Equal(1, set.Remove("veteran"));
		Assert.Equal(80f, set.Get(View), 3);
		Assert.Equal(0, set.Remove("veteran"));
	}

	[Fact]
	public void TheSameSourceOnTheSameStatAndOpReplacesInsteadOfStacking()
	{
		var set = new StatSet();
		set.Add(new StatModifier(Stats.Aim.ErrorMaxDeg, ModOp.Pct, 0.5f, "suppressed"));
		set.Add(new StatModifier(Stats.Aim.ErrorMaxDeg, ModOp.Pct, 0.5f, "suppressed"));
		set.Add(new StatModifier(Stats.Aim.ErrorMaxDeg, ModOp.Pct, 0.8f, "suppressed"));
		Assert.Single(set.Modifiers(Stats.Aim.ErrorMaxDeg));
		Assert.Equal(2f * 1.8f, set.Get(Stats.Aim.ErrorMaxDeg), 3);
		// a different op from the same source is a separate entry
		set.Add(new StatModifier(Stats.Aim.ErrorMaxDeg, ModOp.Add, 1f, "suppressed"));
		Assert.Equal(2, set.Modifiers(Stats.Aim.ErrorMaxDeg).Count);
	}

	[Fact]
	public void TimedModifiersExpireAndTheValueReturns()
	{
		var set = new StatSet();
		set.Add(new StatModifier(Stats.Aim.Accuracy, ModOp.Add, -0.3f, "dazzled", DurationS: 2f));
		Assert.Equal(0.2f, set.Get(Stats.Aim.Accuracy), 3);
		set.Update(1.5f);
		Assert.Equal(0.2f, set.Get(Stats.Aim.Accuracy), 3);
		Assert.Equal(0.5f, set.Active().Single().Remaining!.Value, 3);
		set.Update(0.6f);
		Assert.Equal(0.5f, set.Get(Stats.Aim.Accuracy), 3);
		Assert.Empty(set.Active());
	}

	[Fact]
	public void ReaddingATimedModifierRestartsItsTimer()
	{
		var set = new StatSet();
		var m = new StatModifier(Stats.Aim.Accuracy, ModOp.Add, -0.1f, "pain", DurationS: 2f);
		set.Add(m);
		set.Update(1.5f);
		set.Add(m);
		set.Update(1.5f);
		Assert.Equal(0.4f, set.Get(Stats.Aim.Accuracy), 3);
	}

	[Fact]
	public void ChangedFiresOncePerChangeWithTheAffectedStats()
	{
		var set = new StatSet();
		var calls = new List<string[]>();
		set.Changed += ids => calls.Add(ids.OrderBy(x => x).ToArray());
		set.AddRange(new[]
		{
			new StatModifier(Stats.Health.Head, ModOp.Pct, 0.3f, "boss"),
			new StatModifier(Stats.Health.Thorax, ModOp.Pct, 0.3f, "boss"),
		});
		Assert.Single(calls);
		Assert.Equal(new[] { Stats.Health.Head, Stats.Health.Thorax }, calls[0]);
		set.Remove("boss");
		Assert.Equal(2, calls.Count);
		set.Remove("boss"); // nothing to remove, no event
		Assert.Equal(2, calls.Count);
	}

	[Fact]
	public void SettingTheSameBaseAgainDoesNotRaiseChanged()
	{
		var set = new StatSet();
		var n = 0;
		set.Changed += _ => n++;
		set.SetBase(View, 90f);
		set.SetBase(View, 90f);
		Assert.Equal(1, n);
		Assert.Equal(90f, set.Get(View));
	}

	[Fact]
	public void BossHealthMultiplierRoundsToWholeHp()
	{
		var set = new StatSet();
		foreach (var id in new[] { Stats.Health.Head, Stats.Health.Thorax, Stats.Health.Stomach, Stats.Health.Arm, Stats.Health.Leg })
			set.Add(new StatModifier(id, ModOp.Pct, 0.35f, "boss_hp_vogt"));
		// Vogt in the factions spec: x1.35 gives 47 / 115 / 95 / 81 / 88
		Assert.Equal(new[] { 47, 115, 95, 81, 88 }, new[]
		{
			set.GetInt(Stats.Health.Head), set.GetInt(Stats.Health.Thorax), set.GetInt(Stats.Health.Stomach),
			set.GetInt(Stats.Health.Arm), set.GetInt(Stats.Health.Leg),
		});
	}

	[Fact]
	public void ExplainShowsBaseAndSources()
	{
		var set = new StatSet(new Dictionary<string, float> { [Stats.Aim.Accuracy] = 0.45f });
		set.Add(new StatModifier(Stats.Aim.Accuracy, ModOp.Add, 0.1f, "veteran"));
		var b = set.Explain(Stats.Aim.Accuracy);
		Assert.Equal(0.45f, b.Base, 3);
		Assert.Equal(0.55f, b.Final, 3);
		Assert.Contains("veteran", b.ToString());
	}

	[Fact]
	public void CachedValuesStayCorrectWhenAnotherStatChanges()
	{
		var set = new StatSet();
		Assert.Equal(80f, set.Get(View));
		set.Add(new StatModifier(Stats.Aim.Accuracy, ModOp.Add, 0.2f, "x"));
		Assert.Equal(80f, set.Get(View));
		Assert.Equal(0.7f, set.Get(Stats.Aim.Accuracy), 3);
	}
}
