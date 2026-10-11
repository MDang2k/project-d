using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <param name="MinCount">Minimum stack size when this entry is rolled.</param>
public readonly record struct LootEntry(string ItemId, float Weight, int MinCount, int MaxCount);

/// <summary>Weighted random loot rolls (GDD s10, s19 value tiers). Pure logic, injected random.</summary>
public static class LootRoller
{
	/// <summary>Picks <paramref name="rolls"/> entries with replacement, weighted; returns (id, count).</summary>
	public static List<(string ItemId, int Count)> Roll(IReadOnlyList<LootEntry> entries, int rolls, Func<double> random)
	{
		var result = new List<(string, int)>();
		var total = entries.Sum(e => Math.Max(0f, e.Weight));
		if (entries.Count == 0 || total <= 0f)
			return result;
		for (var i = 0; i < rolls; i++)
		{
			var pick = random() * total;
			foreach (var e in entries)
			{
				pick -= Math.Max(0f, e.Weight);
				if (pick > 0f)
					continue;
				var lo = Math.Max(1, e.MinCount);
				var hi = Math.Max(lo, e.MaxCount);
				result.Add((e.ItemId, lo + (int)Math.Floor(random() * (hi - lo + 1))));
				break;
			}
		}
		return result;
	}
}

/// <summary>
/// A world container (crate, locker, body): a grid whose items start hidden and are revealed
/// one at a time while the player searches (GDD s10). Searching is noisy. Pure logic.
/// </summary>
public sealed class LootContainerModel
{
	private readonly HashSet<ItemInstance> _hidden = new();
	private readonly List<ItemInstance> _revealOrder = new();
	private float _timer;

	public LootContainerModel(int width, int height, float secondsPerItem, float noiseRadius)
	{
		Grid = new InventoryGrid(width, height);
		SecondsPerItem = secondsPerItem;
		NoiseRadius = noiseRadius;
	}

	public InventoryGrid Grid { get; }
	public float SecondsPerItem { get; }
	/// <summary>Distance in metres at which AI can hear the search (GDD s16).</summary>
	public float NoiseRadius { get; }
	public bool IsSearching { get; private set; }
	public int HiddenCount => _hidden.Count;
	public bool FullyRevealed => _hidden.Count == 0;

	public event Action<ItemInstance>? ItemRevealed;

	/// <summary>Adds an item that will be hidden until a search reveals it.</summary>
	public bool AddHidden(ItemInstance item)
	{
		if (!Grid.TryPlaceAnywhere(item))
			return false;
		_hidden.Add(item);
		_revealOrder.Add(item);
		return true;
	}

	/// <summary>Adds an item that is visible immediately (a looted body's worn gear, dropped items).</summary>
	public bool AddRevealed(ItemInstance item) => Grid.TryPlaceAnywhere(item);

	public bool IsRevealed(ItemInstance item) => !_hidden.Contains(item);

	public void StartSearch()
	{
		if (!FullyRevealed)
			IsSearching = true;
	}

	/// <summary>Pauses searching (window closed or player moved away); progress is kept.</summary>
	public void StopSearch() => IsSearching = false;

	/// <summary>Advances the search; returns true if an item was revealed this tick.</summary>
	public bool Update(float delta, float speedMultiplier = 1f)
	{
		if (!IsSearching || FullyRevealed)
		{
			IsSearching &= !FullyRevealed;
			return false;
		}
		_timer += delta * Math.Max(0.01f, speedMultiplier);
		if (_timer < SecondsPerItem)
			return false;
		_timer = 0f;
		var next = _revealOrder.First(_hidden.Contains);
		_hidden.Remove(next);
		_revealOrder.Remove(next);
		ItemRevealed?.Invoke(next);
		if (FullyRevealed)
			IsSearching = false;
		return true;
	}

	/// <summary>0..1 progress toward the next reveal (for the progress bar).</summary>
	public float Progress => SecondsPerItem > 0f ? Math.Clamp(_timer / SecondsPerItem, 0f, 1f) : 0f;
}

/// <summary>Where a sound happened and how far it carries; AI subscribes in M5.</summary>
public readonly record struct NoiseEvent(Vector3 Position, float Radius, string Kind);

/// <summary>Global noise broadcast. Static so any system can emit without a node.</summary>
public static class NoiseBus
{
	public static event Action<NoiseEvent>? Noise;

	public static void Emit(Vector3 position, float radius, string kind)
		=> Noise?.Invoke(new NoiseEvent(position, radius, kind));
}
