using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum KnowledgeKind { Lore, DoorCode, StashLocation, Frequency, LabNote }

/// <summary>
/// One piece of knowledge (M8d spec s1): what it is, the item that carries it, how many raids a translation takes
/// (0 for none) and what it opens. Unlock ids are free strings the rest of the game checks: <c>lock.&lt;id&gt;</c> for a
/// door code, <c>stash.&lt;id&gt;</c> for a hidden cache, <c>quest.&lt;id&gt;</c> for a quest prerequisite.
/// </summary>
public sealed record KnowledgeSpec(
	string Id,
	KnowledgeKind Kind,
	string TitleKey,
	string TextKey,
	string ItemId,
	int TranslationRaids,
	IReadOnlyList<string> Unlocks,
	string LandmarkKey = "");

/// <summary>Where a piece of knowledge stands. A lab note that needs translating is Unlocked until a translation starts.</summary>
public enum KnowledgeState { Unlocked, Translating, Translated }

/// <summary>The saved state of one piece of knowledge.</summary>
public readonly record struct KnowledgeEntry(KnowledgeState State, int RaidsLeft);

/// <summary>
/// What the player knows (M8d spec s2 to s4): unlocked by reading or extracting the item, kept for ever, with one
/// translation at a time for lab notes that need it. Text and effects of an untranslated note are withheld. Pure logic.
/// </summary>
public sealed class KnowledgeBook
{
	private readonly Dictionary<string, KnowledgeSpec> _specs = new();
	private readonly Dictionary<string, KnowledgeEntry> _entries = new();

	/// <summary>Knowledge was unlocked (read or extracted).</summary>
	public event Action<KnowledgeSpec>? Unlocked;

	/// <summary>A translation finished; the text and effects are now available.</summary>
	public event Action<KnowledgeSpec>? Translated;

	public IEnumerable<KnowledgeSpec> Specs => _specs.Values;

	public void Register(KnowledgeSpec spec) => _specs[spec.Id] = spec;

	public KnowledgeSpec? Spec(string id) => _specs.GetValueOrDefault(id);

	public bool NeedsTranslation(KnowledgeSpec spec) => spec.Kind == KnowledgeKind.LabNote && spec.TranslationRaids > 0;

	/// <summary>Known at all: the title shows in the journal.</summary>
	public bool IsUnlocked(string id) => _entries.ContainsKey(id);

	/// <summary>
	/// Known and usable: unlocked, and translated when it needs translating. Codes, stashes and quest prerequisites check
	/// this, never <see cref="IsUnlocked"/>.
	/// </summary>
	public bool IsActive(string id)
		=> _entries.TryGetValue(id, out var e) && _specs.TryGetValue(id, out var spec)
			&& (!NeedsTranslation(spec) || e.State == KnowledgeState.Translated);

	/// <summary>Whether any usable knowledge opens this effect id ("lock.side_room", "stash.tower_cache", "quest.x").</summary>
	public bool Opens(string effectId) => _specs.Values.Any(s => s.Unlocks.Contains(effectId) && IsActive(s.Id));

	public KnowledgeEntry? Entry(string id) => _entries.TryGetValue(id, out var e) ? e : null;

	/// <summary>The lab note being translated, or null.</summary>
	public string? ActiveTranslation => _entries.FirstOrDefault(kv => kv.Value.State == KnowledgeState.Translating).Key;

	/// <summary>
	/// Unlocks knowledge by its id. A piece already unlocked changes nothing. Returns whether it is newly unlocked.
	/// Notes with no translation, and every other kind, are usable at once.
	/// </summary>
	public bool Unlock(string id)
	{
		if (!_specs.TryGetValue(id, out var spec) || _entries.ContainsKey(id))
			return false;
		_entries[id] = new KnowledgeEntry(NeedsTranslation(spec) ? KnowledgeState.Unlocked : KnowledgeState.Translated, 0);
		Unlocked?.Invoke(spec);
		return true;
	}

	/// <summary>The knowledge a carried item gives, or null for an item that carries none.</summary>
	public KnowledgeSpec? ForItem(string itemId) => _specs.Values.FirstOrDefault(s => s.ItemId == itemId);

	/// <summary>Unlocks what an item carries (the item was read or extracted). Returns the newly unlocked knowledge.</summary>
	public KnowledgeSpec? UnlockFromItem(string itemId)
		=> ForItem(itemId) is { } spec && Unlock(spec.Id) ? spec : null;

	/// <summary>
	/// An item that carries knowledge you already have is just an item: it no longer spawns in raids (M8d spec s2).
	/// Items that carry no knowledge always may.
	/// </summary>
	public bool ShouldSpawn(string itemId) => ForItem(itemId) is not { } spec || !IsUnlocked(spec.Id);

	// ---- translation

	/// <summary>
	/// Starts translating an unlocked lab note. One at a time; no fee. False when another is running, the note is not
	/// unlocked, needs no translation, or is already translating or done.
	/// </summary>
	public bool StartTranslation(string id)
	{
		if (!_specs.TryGetValue(id, out var spec) || !NeedsTranslation(spec) || ActiveTranslation is not null)
			return false;
		if (!_entries.TryGetValue(id, out var entry) || entry.State != KnowledgeState.Unlocked)
			return false; // not read yet, or already running or done
		_entries[id] = new KnowledgeEntry(KnowledgeState.Translating, spec.TranslationRaids);
		return true;
	}

	/// <summary>
	/// A raid ended, whatever its outcome (M8d spec s4): the running translation comes one raid closer and, at zero,
	/// is done. Returns the finished note, else null.
	/// </summary>
	public KnowledgeSpec? OnRaidEnded()
	{
		if (ActiveTranslation is not { } id || !_specs.TryGetValue(id, out var spec))
			return null;
		var left = Math.Max(0, _entries[id].RaidsLeft - 1);
		if (left > 0)
		{
			_entries[id] = new KnowledgeEntry(KnowledgeState.Translating, left);
			return null;
		}
		_entries[id] = new KnowledgeEntry(KnowledgeState.Translated, 0);
		Translated?.Invoke(spec);
		return spec;
	}

	/// <summary>Raids left on a translation, or 0.</summary>
	public int RaidsLeft(string id) => _entries.TryGetValue(id, out var e) && e.State == KnowledgeState.Translating ? e.RaidsLeft : 0;

	// ---- save

	public Dictionary<string, KnowledgeEntry> Snapshot() => new(_entries);

	/// <summary>Replaces the state from a save. Unknown ids keep their entry (a later build may know them); at most one translation is kept running.</summary>
	public void Load(IEnumerable<KeyValuePair<string, KnowledgeEntry>> saved)
	{
		_entries.Clear();
		var translating = false;
		foreach (var (id, e) in saved)
		{
			var entry = e;
			if (e.State == KnowledgeState.Translating)
			{
				if (translating)
					entry = new KnowledgeEntry(KnowledgeState.Unlocked, 0); // a bad save with two: the second waits
				else
				{
					translating = true;
					entry = new KnowledgeEntry(KnowledgeState.Translating, Math.Max(1, e.RaidsLeft));
				}
			}
			_entries[id] = entry;
		}
	}

	public void Clear() => _entries.Clear();
}

/// <summary>The in-raid Read action (M8d spec s2): four seconds, broken by damage. Pure logic.</summary>
public sealed class ReadTimer
{
	private float _elapsed;

	public ReadTimer(float durationS = 4f) => DurationS = durationS;

	public float DurationS { get; }

	/// <summary>The item being read, or null when idle.</summary>
	public string? ItemId { get; private set; }

	public bool IsReading => ItemId is not null;

	public float Progress => IsReading && DurationS > 0f ? Math.Min(1f, _elapsed / DurationS) : 0f;

	/// <summary>The read finished: the item id.</summary>
	public event Action<string>? Finished;

	/// <summary>The read was broken before it finished.</summary>
	public event Action? Cancelled;

	public bool Start(string itemId)
	{
		if (IsReading)
			return false;
		ItemId = itemId;
		_elapsed = 0f;
		return true;
	}

	public void Update(float delta)
	{
		if (ItemId is not { } id)
			return;
		_elapsed += delta;
		if (_elapsed < DurationS)
			return;
		ItemId = null;
		Finished?.Invoke(id);
	}

	/// <summary>Damage (or closing the inventory) stops the read; nothing is unlocked.</summary>
	public void Cancel()
	{
		if (!IsReading)
			return;
		ItemId = null;
		_elapsed = 0f;
		Cancelled?.Invoke();
	}
}

/// <summary>Extraction rule of M8d: knowledge items carried out unread still unlock.</summary>
public static class KnowledgeRules
{
	/// <summary>
	/// Unlocks the knowledge of every item in the carried grids and the secure container (they are all in the character's
	/// grids); returns what was newly unlocked, in item order.
	/// </summary>
	public static List<KnowledgeSpec> OnExtracted(KnowledgeBook book, CharacterInventory character)
		=> character.AllItems().Select(i => book.UnlockFromItem(i.Spec.Id)).OfType<KnowledgeSpec>().ToList();
}
