using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>
/// The player's skill XP (M8a spec s1): totals that persist, and per-raid state that does not. XP only counts while a
/// raid is running, repeating the same action within a raid pays less each time, skills never decay and XP is kept
/// on death. Pure logic; player-side nodes report actions to <see cref="Report"/>.
/// </summary>
public sealed class SkillBook
{
	private readonly Dictionary<string, SkillSpec> _specs = new();
	private readonly Dictionary<string, float> _xp = new();
	private readonly Dictionary<(string Skill, string Action), int> _repeats = new();
	private readonly Dictionary<string, float> _raidGains = new();
	private readonly List<(string Skill, int From, int To)> _raidLevelUps = new();

	public bool InRaid { get; private set; }

	/// <summary>A skill went up a level: skill id, old level, new level.</summary>
	public event Action<string, int, int>? LevelUp;

	public IEnumerable<SkillSpec> Specs => _specs.Values;

	/// <summary>Adds or replaces a skill's numbers. XP already held for it is kept.</summary>
	public void Register(SkillSpec spec) => _specs[spec.Id] = spec;

	public bool Knows(string skill) => _specs.ContainsKey(skill);

	public float Xp(string skill) => _xp.TryGetValue(skill, out var v) ? v : 0f;

	/// <summary>Level 1 to 10; level 1 for a skill with no numbers registered.</summary>
	public int Level(string skill) => _specs.TryGetValue(skill, out var s) ? s.LevelFor(Xp(skill)) : 1;

	/// <summary>Progress through the current level, 0 to 1.</summary>
	public float Progress(string skill) => _specs.TryGetValue(skill, out var s) ? s.Progress(Xp(skill)) : 0f;

	/// <summary>XP gained per skill in the raid so far, for the result screen.</summary>
	public IReadOnlyDictionary<string, float> RaidGains => _raidGains;

	/// <summary>Level-ups of the raid so far, in order.</summary>
	public IReadOnlyList<(string Skill, int From, int To)> RaidLevelUps => _raidLevelUps;

	/// <summary>A raid starts: XP counts from now and the repeat counters start again.</summary>
	public void BeginRaid()
	{
		InRaid = true;
		_repeats.Clear();
		_raidGains.Clear();
		_raidLevelUps.Clear();
	}

	/// <summary>The raid is over (any outcome): XP stops counting. What was earned is kept.</summary>
	public void EndRaid() => InRaid = false;

	/// <summary>
	/// Reports an action that trains a skill. <paramref name="amount"/> scales the base XP (units of continuous actions,
	/// damage taken). Returns the XP awarded: 0 outside a raid, for an unknown skill or action.
	/// </summary>
	public float Report(string skill, string action, float amount = 1f)
	{
		if (!InRaid || amount <= 0f || !_specs.TryGetValue(skill, out var spec) || !spec.ActionXp.TryGetValue(action, out var baseXp))
			return 0f;
		var key = (skill, action);
		var repeats = _repeats.GetValueOrDefault(key);
		_repeats[key] = repeats + 1;
		var xp = baseXp * amount * Math.Max(spec.DecayFloor, MathF.Pow(spec.Decay, repeats));
		var before = spec.LevelFor(Xp(skill));
		_xp[skill] = Xp(skill) + xp;
		_raidGains[skill] = _raidGains.GetValueOrDefault(skill) + xp;
		var after = spec.LevelFor(Xp(skill));
		if (after > before)
		{
			_raidLevelUps.Add((skill, before, after));
			LevelUp?.Invoke(skill, before, after);
		}
		return xp;
	}

	/// <summary>Totals to save: skill id to XP, only skills with some.</summary>
	public Dictionary<string, float> Snapshot() => _xp.Where(kv => kv.Value > 0f).ToDictionary(kv => kv.Key, kv => kv.Value);

	/// <summary>Replaces the totals from a save. Unknown skills keep their XP; bad values are dropped.</summary>
	public void Load(IEnumerable<KeyValuePair<string, float>> saved)
	{
		_xp.Clear();
		foreach (var (skill, xp) in saved)
			if (float.IsFinite(xp) && xp > 0f)
				_xp[skill] = xp;
	}
}
