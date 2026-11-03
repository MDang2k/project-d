using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>The seven skills (GDD s12, M8a spec).</summary>
public static class SkillIds
{
	public const string Endurance = "endurance";
	public const string Strength = "strength";
	public const string WeaponHandling = "weapon_handling";
	public const string RecoilControl = "recoil_control";
	public const string FirstAid = "first_aid";
	public const string Searching = "searching";
	public const string Vitality = "vitality";

	public static readonly IReadOnlyList<string> All = new[]
	{
		Endurance, Strength, WeaponHandling, RecoilControl, FirstAid, Searching, Vitality,
	};
}

/// <summary>
/// One skill's numbers: the cumulative XP needed for each level and the base XP of each action that trains it, plus how
/// quickly repeating an action in one raid stops paying (M8a spec s1). Placeholders to tune in data/skills.
/// </summary>
public sealed record SkillSpec(
	string Id,
	/// <summary>Cumulative XP to reach levels 1 to 10; the first entry is 0.</summary>
	IReadOnlyList<float> XpTable,
	IReadOnlyDictionary<string, float> ActionXp,
	/// <summary>Each repeat of an action within a raid pays this fraction of the one before.</summary>
	float Decay = 0.9f,
	/// <summary>Repeats never pay less than this fraction of the base XP.</summary>
	float DecayFloor = 0.1f)
{
	public const int MaxLevel = 10;

	/// <summary>Level 1 to 10 for a total of XP.</summary>
	public int LevelFor(float xp) => Math.Clamp(XpTable.Count(t => xp >= t), 1, MaxLevel);

	/// <summary>XP needed for the next level, or null at the top level.</summary>
	public float? NextThreshold(int level) => level >= MaxLevel || level >= XpTable.Count ? null : XpTable[level];

	/// <summary>Progress through the current level, 0 to 1 (1 at the top level).</summary>
	public float Progress(float xp)
	{
		var level = LevelFor(xp);
		if (NextThreshold(level) is not { } next)
			return 1f;
		var from = XpTable[level - 1];
		return Math.Clamp((xp - from) / (next - from), 0f, 1f);
	}

	/// <summary>Problems with the numbers, empty when they are usable.</summary>
	public IReadOnlyList<string> Problems()
	{
		var p = new List<string>();
		if (XpTable.Count != MaxLevel)
			p.Add($"{Id}: XP table needs {MaxLevel} entries, has {XpTable.Count}");
		else
		{
			if (XpTable[0] != 0f)
				p.Add($"{Id}: level 1 must need 0 XP");
			for (var i = 1; i < XpTable.Count; i++)
				if (XpTable[i] <= XpTable[i - 1])
					p.Add($"{Id}: XP table must rise (level {i + 1})");
		}
		if (ActionXp.Count == 0)
			p.Add($"{Id}: no action trains it");
		if (ActionXp.Values.Any(v => v <= 0f))
			p.Add($"{Id}: action XP must be above zero");
		if (Decay is <= 0f or > 1f)
			p.Add($"{Id}: decay must be above 0 and at most 1");
		if (DecayFloor is < 0f or > 1f)
			p.Add($"{Id}: decay floor must be between 0 and 1");
		return p;
	}
}
