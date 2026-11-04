using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class ReputationSummaryTests
{
	private static string Name(string id) => id.ToUpperInvariant();

	private static ReputationBook Book(params (string Target, int Value)[] values)
	{
		var log = new QuestLog();
		foreach (var (t, v) in values)
			log.SetReputation(t, v);
		return new ReputationBook(log);
	}

	[Theory]
	[InlineData(0, "0.00")]
	[InlineData(8, "+0.08")]
	[InlineData(100, "+1.00")]
	[InlineData(-17, "-0.17")]
	[InlineData(-100, "-1.00")]
	[InlineData(5, "+0.05")]
	public void NumbersShowAsSignedHundredths(int hundredths, string text)
		=> Assert.Equal(text, ReputationSummary.Format(hundredths));

	[Theory]
	[InlineData(-100, StandingTone.Hostile)]
	[InlineData(-31, StandingTone.Hostile)]
	[InlineData(-30, StandingTone.Wary)]
	[InlineData(-1, StandingTone.Wary)]
	[InlineData(0, StandingTone.Neutral)]
	[InlineData(29, StandingTone.Neutral)]
	[InlineData(30, StandingTone.Friendly)]
	[InlineData(100, StandingTone.Friendly)]
	public void TonesFollowTheRaidStartLine(int hundredths, StandingTone tone)
		=> Assert.Equal(tone, ReputationSummary.Tone(hundredths));

	[Fact]
	public void RowsListSixTradersThenThreeFactionsWithTheirNumbers()
	{
		var rows = ReputationSummary.Rows(Book(("medic", 60), ("allied_pmc", -40)), Name);
		Assert.Equal(9, rows.Count);
		Assert.Equal(new[] { "allied_qm", "soviet_qm", "smuggler", "medic", "engineer", "scientist", "allied_pmc", "soviet_pmc", "bandits" },
			rows.Select(r => r.Target));
		var medic = rows.Single(r => r.Target == "medic");
		Assert.Equal("+0.60", medic.Text);
		Assert.Equal(StandingTone.Friendly, medic.Tone);
		Assert.Equal("MEDIC", medic.Name);
		Assert.Equal(StandingTone.Hostile, rows.Single(r => r.Target == "allied_pmc").Tone);
		Assert.Equal("0.00", rows.Single(r => r.Target == "bandits").Text);
	}

	[Fact]
	public void RaidLinesShowOnlyChangedTargetsFactionsFirst()
	{
		var lines = ReputationSummary.RaidLines(new Dictionary<string, int>
		{
			["allied_qm"] = -17, ["allied_pmc"] = -17, ["medic"] = 0, ["bandits"] = 4,
		}, Name);
		Assert.Equal(new[] { "ALLIED_PMC: -0.17", "BANDITS: +0.04", "ALLIED_QM: -0.17" }, lines);
		Assert.Empty(ReputationSummary.RaidLines(new Dictionary<string, int>(), Name));
	}

	private static string Text(string key) => key switch
	{
		"rep.trader.line" => "Standing {0}: buy {1}, sell {2}",
		"rep.trader.refuses" => "Standing {0}: refuses",
		_ => key,
	};

	[Fact]
	public void TheTraderLineShowsStandingAndThePriceEffect()
	{
		Assert.Equal("Standing 0.00: buy 0%, sell 0%", ReputationSummary.TraderLine(Book(), "medic", Text));
		Assert.Equal("Standing +0.50: buy -5%, sell +5%", ReputationSummary.TraderLine(Book(("medic", 50)), "medic", Text));
		Assert.Equal("Standing -0.40: buy +4%, sell -4%", ReputationSummary.TraderLine(Book(("medic", -40)), "medic", Text));
	}

	[Fact]
	public void TheTraderLineSaysSoWhenTheTraderRefuses()
		=> Assert.Equal("Standing -0.62: refuses", ReputationSummary.TraderLine(Book(("smuggler", -62)), "smuggler", Text));

	[Fact]
	public void EveryReputationTargetHasADisplayName()
	{
		var dir = System.AppContext.BaseDirectory;
		while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "Project D.csproj")))
			dir = System.IO.Path.GetDirectoryName(dir);
		var text = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!, "scripts/core/GameText.cs"));
		foreach (var id in ReputationSummary.Traders)
			Assert.Contains($"[\"trader.{id}\"]", text);
		foreach (var id in ReputationSummary.Factions)
			Assert.Contains($"[\"faction.{id}\"]", text);
		foreach (var key in new[] { "rep.trader.line", "rep.trader.refuses", "rep.hostile", "rep.raid.title", "character.reputation" })
			Assert.Contains($"[\"{key}\"]", text);
	}
}
