using System.Collections.Generic;

namespace ProjectD;

/// <summary>
/// A distinct magazine with its own rounds (GDD s7). Rounds are a stack: the last pushed
/// round feeds first, so load order matters.
/// </summary>
public sealed class Magazine
{
	private readonly List<AmmoType> _rounds = new();

	public int Capacity { get; }
	public int Count => _rounds.Count;
	public bool IsEmpty => _rounds.Count == 0;

	public Magazine(int capacity) => Capacity = capacity;

	public bool Push(AmmoType round)
	{
		if (_rounds.Count >= Capacity)
			return false;
		_rounds.Add(round);
		return true;
	}

	public AmmoType? Pop()
	{
		if (_rounds.Count == 0)
			return null;
		var top = _rounds[^1];
		_rounds.RemoveAt(_rounds.Count - 1);
		return top;
	}

	public AmmoType? Peek() => _rounds.Count == 0 ? null : _rounds[^1];

	public static Magazine Full(int capacity, AmmoType round)
	{
		var m = new Magazine(capacity);
		for (var i = 0; i < capacity; i++)
			m.Push(round);
		return m;
	}
}
