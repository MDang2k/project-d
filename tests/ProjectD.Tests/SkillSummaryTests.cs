using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class SkillSummaryTests
{
	private static readonly float[] Table = { 0, 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 };

	private static SkillBook Book()
	{
		var book = new SkillBook();
		foreach (var id in SkillIds.All)
			book.Register(new SkillSpec(id, Table, new Dictionary<string, float> { ["a"] = 10f }, Decay: 1f, DecayFloor: 1f));
		return book;
	}

	private static string Name(string id) => id.ToUpperInvariant();

	[Fact]
	public void NothingEarnedGivesNoLines()
	{
		var book = Book();
		book.BeginRaid();
		Assert.Empty(SkillSummary.RaidLines(book, Name));
	}

	[Fact]
	public void ASkillThatGainedXpGetsALineWithItsCurrentLevel()
	{
		var book = Book();
		book.BeginRaid();
		book.Report(SkillIds.Endurance, "a", 3f); // 30 XP
		Assert.Equal(new[] { "ENDURANCE: +30 XP, level 1" }, SkillSummary.RaidLines(book, Name));
	}

	[Fact]
	public void ALevelUpShowsFromAndTo()
	{
		var book = Book();
		book.BeginRaid();
		book.Report(SkillIds.Endurance, "a", 30f); // 300 XP: level 1 to 3
		Assert.Equal(new[] { "ENDURANCE: +300 XP, level 1 to 3" }, SkillSummary.RaidLines(book, Name));
	}

	[Fact]
	public void SeveralLevelUpsInOneRaidCollapseIntoOneRange()
	{
		var book = Book();
		book.BeginRaid();
		book.Report(SkillIds.Strength, "a", 10f); // 100: level 2
		book.Report(SkillIds.Strength, "a", 20f); // 300 total: level 3
		Assert.Equal(new[] { "STRENGTH: +300 XP, level 1 to 3" }, SkillSummary.RaidLines(book, Name));
	}

	[Fact]
	public void LinesFollowTheSkillOrderNotTheOrderEarned()
	{
		var book = Book();
		book.BeginRaid();
		book.Report(SkillIds.Vitality, "a");
		book.Report(SkillIds.Endurance, "a");
		book.Report(SkillIds.Searching, "a");
		var lines = SkillSummary.RaidLines(book, Name);
		Assert.Equal(new[] { "ENDURANCE", "SEARCHING", "VITALITY" }, lines.Select(l => l.Split(':')[0]));
	}

	[Fact]
	public void RowsListEverySkillInOrderWithProgress()
	{
		var book = Book();
		book.Load(new Dictionary<string, float> { [SkillIds.Endurance] = 175f, [SkillIds.Vitality] = 5000f });
		var rows = SkillSummary.Rows(book, Name);
		Assert.Equal(SkillIds.All, rows.Select(r => r.Id));
		var endurance = rows[0];
		Assert.Equal(2, endurance.Level);
		Assert.Equal(0.5f, endurance.Progress, 3);
		Assert.Equal("175 / 250 XP", endurance.XpText);
		var vitality = rows.Single(r => r.Id == SkillIds.Vitality);
		Assert.Equal(10, vitality.Level);
		Assert.Equal(1f, vitality.Progress);
		Assert.Equal("5000 XP (max)", vitality.XpText);
		Assert.Equal(1, rows.Single(r => r.Id == SkillIds.Strength).Level);
	}

	[Fact]
	public void RowsSkipSkillsWithoutNumbers()
	{
		var book = new SkillBook();
		book.Register(new SkillSpec(SkillIds.Searching, Table, new Dictionary<string, float> { ["a"] = 1f }));
		Assert.Equal(new[] { SkillIds.Searching }, SkillSummary.Rows(book, Name).Select(r => r.Id));
	}
}
