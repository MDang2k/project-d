using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>One skill as the Character tab shows it: name, level, progress to the next level and the XP text.</summary>
public sealed record SkillRow(string Id, string Name, int Level, float Progress, string XpText);

/// <summary>Text for the raid result screen and the hub Character tab (M8a spec s3). Pure; names come in as a lookup.</summary>
public static class SkillSummary
{
	/// <summary>
	/// What the raid taught: one line per skill that gained XP, with the level change when it went up. Skills in the
	/// order of <see cref="SkillIds.All"/>, then any other skill by id. Empty when nothing was earned.
	/// </summary>
	public static IReadOnlyList<string> RaidLines(SkillBook book, Func<string, string> nameOf)
	{
		var order = SkillIds.All.Concat(book.RaidGains.Keys.Except(SkillIds.All).OrderBy(k => k, StringComparer.Ordinal));
		var lines = new List<string>();
		foreach (var id in order)
		{
			if (!book.RaidGains.TryGetValue(id, out var gained) || gained <= 0f)
				continue;
			var ups = book.RaidLevelUps.Where(u => u.Skill == id).ToList();
			var change = ups.Count > 0 ? $", level {ups[0].From} to {ups[^1].To}" : $", level {book.Level(id)}";
			lines.Add($"{nameOf(id)}: +{gained:0} XP{change}");
		}
		return lines;
	}

	/// <summary>One row per registered skill, in the order of <see cref="SkillIds.All"/>.</summary>
	public static IReadOnlyList<SkillRow> Rows(SkillBook book, Func<string, string> nameOf)
	{
		var rows = new List<SkillRow>();
		foreach (var id in SkillIds.All.Where(book.Knows))
		{
			var level = book.Level(id);
			var spec = book.Specs.First(s => s.Id == id);
			var xp = book.Xp(id);
			var text = spec.NextThreshold(level) is { } next ? $"{xp:0} / {next:0} XP" : $"{xp:0} XP (max)";
			rows.Add(new SkillRow(id, nameOf(id), level, book.Progress(id), text));
		}
		return rows;
	}
}
