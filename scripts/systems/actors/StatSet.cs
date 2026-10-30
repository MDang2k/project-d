using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>
/// The numbers of one actor (composable actors spec s4): base values for the registered stats, a stack of modifiers,
/// and the resolved results cached until something changes. Missing bases fall back to the registry default.
/// Pure logic; the owner calls <see cref="Update"/> with the frame time so timed modifiers expire.
/// </summary>
public sealed class StatSet
{
	private sealed class Entry(StatModifier modifier)
	{
		public StatModifier Modifier { get; set; } = modifier;
		public float? Remaining { get; set; } = modifier.DurationS;
	}

	private readonly Dictionary<string, float> _bases = new();
	private readonly List<Entry> _entries = new();
	private readonly Dictionary<string, float> _cache = new();

	/// <summary>Raised once per change with the ids of the stats whose inputs changed.</summary>
	public event Action<IReadOnlyCollection<string>>? Changed;

	public StatSet(IReadOnlyDictionary<string, float>? bases = null)
	{
		if (bases is null)
			return;
		foreach (var (id, value) in bases)
		{
			Stats.Get(id);
			_bases[id] = value;
		}
	}

	public float Base(string id) => _bases.TryGetValue(id, out var v) ? v : Stats.Get(id).Default;

	public void SetBase(string id, float value)
	{
		Stats.Get(id);
		if (_bases.TryGetValue(id, out var old) && old == value)
			return;
		_bases[id] = value;
		Invalidate(new[] { id });
	}

	/// <summary>The resolved value of a stat.</summary>
	public float Get(string id)
	{
		if (_cache.TryGetValue(id, out var cached))
			return cached;
		var def = Stats.Get(id);
		var value = StatResolver.Resolve(def, Base(id), ModifiersOf(id));
		_cache[id] = value;
		return value;
	}

	/// <summary>The resolved value of a whole-number stat.</summary>
	public int GetInt(string id) => (int)MathF.Round(Get(id), MidpointRounding.AwayFromZero);

	public StatBreakdown Explain(string id)
		=> StatResolver.Explain(Stats.Get(id), Base(id), ModifiersOf(id));

	/// <summary>All modifiers acting on a stat, in the order they were added.</summary>
	public IReadOnlyList<StatModifier> Modifiers(string id) => ModifiersOf(id).ToList();

	/// <summary>Every modifier on the actor, with the seconds left for timed ones.</summary>
	public IReadOnlyList<(StatModifier Modifier, float? Remaining)> Active()
		=> _entries.Select(e => (e.Modifier, e.Remaining)).ToList();

	private IEnumerable<StatModifier> ModifiersOf(string id)
		=> _entries.Where(e => e.Modifier.StatId == id).Select(e => e.Modifier);

	public void Add(StatModifier modifier) => AddRange(new[] { modifier });

	/// <summary>Adds several modifiers as one change (for a modifier set). Same stat, source and op replaces.</summary>
	public void AddRange(IEnumerable<StatModifier> modifiers)
	{
		var changed = new HashSet<string>();
		foreach (var m in modifiers)
		{
			Stats.Get(m.StatId);
			var existing = _entries.FirstOrDefault(e => e.Modifier.StatId == m.StatId && e.Modifier.Source == m.Source && e.Modifier.Op == m.Op);
			if (existing is null)
				_entries.Add(new Entry(m));
			else
			{
				existing.Modifier = m;
				existing.Remaining = m.DurationS;
			}
			changed.Add(m.StatId);
		}
		Invalidate(changed);
	}

	/// <summary>Removes every modifier of a source (all stats). Returns how many went.</summary>
	public int Remove(string source) => RemoveWhere(e => e.Modifier.Source == source);

	/// <summary>Removes the modifiers of a source on one stat.</summary>
	public int Remove(string statId, string source)
		=> RemoveWhere(e => e.Modifier.StatId == statId && e.Modifier.Source == source);

	private int RemoveWhere(Func<Entry, bool> match)
	{
		var gone = _entries.Where(match).ToList();
		if (gone.Count == 0)
			return 0;
		foreach (var e in gone)
			_entries.Remove(e);
		Invalidate(gone.Select(e => e.Modifier.StatId).ToHashSet());
		return gone.Count;
	}

	/// <summary>Counts down timed modifiers and drops the ones that ran out.</summary>
	public void Update(float delta)
	{
		var expired = new List<Entry>();
		foreach (var e in _entries)
		{
			if (e.Remaining is not { } left)
				continue;
			e.Remaining = left - delta;
			if (e.Remaining <= 0f)
				expired.Add(e);
		}
		if (expired.Count == 0)
			return;
		foreach (var e in expired)
			_entries.Remove(e);
		Invalidate(expired.Select(e => e.Modifier.StatId).ToHashSet());
	}

	private void Invalidate(IReadOnlyCollection<string> ids)
	{
		foreach (var id in ids)
			_cache.Remove(id);
		if (ids.Count > 0)
			Changed?.Invoke(ids);
	}
}
