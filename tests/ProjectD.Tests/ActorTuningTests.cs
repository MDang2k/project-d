using System;
using System.IO;
using Xunit;

namespace ProjectD.Tests;

/// <summary>
/// The Actors sheet is the source of the AI numbers (composable actors spec); these check that the stats reach the
/// tuning records the AI models run on. The migration oracle (the old data/ai presets) is retired.
/// </summary>
public class ActorTuningTests
{
	private static StatSet Actor(string id)
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return ActorCatalog.Parse(File.ReadAllText(Path.Combine(dir!, "data", "actors", "actors.json"))).Create(id).Stats;
	}

	[Theory]
	[InlineData("looter")]
	[InlineData("bandit")]
	[InlineData("allied_pmc")]
	[InlineData("soviet_pmc")]
	public void StatsReachTheTuningRecords(string id)
	{
		var s = Actor(id);
		var perception = ActorTuning.Perception(s);
		Assert.Equal(s.Get(Stats.Perception.ViewDistance), perception.ViewDistance);
		Assert.Equal(s.Get(Stats.Awareness.NoticeS), perception.NoticeSeconds);
		var tactics = ActorTuning.Tactics(s);
		Assert.Equal(s.Get(Stats.Tactics.Courage), tactics.Courage);
		Assert.Equal(s.Get(Stats.Tactics.EngageDistance), tactics.EngageDistance);
		Assert.Equal(s.Get(Stats.Brain.ArrivalRadius), ActorTuning.Brain(s).ArrivalRadius);
		Assert.Equal(s.Get(Stats.Aim.ErrorMaxDeg), ActorTuning.Aim(s).ErrorMaxDeg);
	}

	[Fact]
	public void FactionNamesParse()
	{
		Assert.Equal(Faction.AlliedPmc, ActorTuning.ParseFaction("Allied PMC"));
		Assert.Equal(Faction.SovietPmc, ActorTuning.ParseFaction("soviet_pmc"));
		Assert.Throws<ArgumentException>(() => ActorTuning.ParseFaction("aliens"));
	}
}
