using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>Friendly-fire numbers (M8b spec s2 and s3). Placeholders to tune.</summary>
public sealed record FriendlyFireTuning(
	/// <summary>Hundredths lost with the faction and your quartermaster for a hit.</summary>
	int HitPenalty = 2,
	/// <summary>Hundredths lost for a kill; it replaces the hit penalty for the killing hit.</summary>
	int KillPenalty = 15,
	/// <summary>Friendly hits (a kill counts) in one raid that turn the faction hostile for the rest of it.</summary>
	int HitsToHostile = 3);

/// <summary>One reputation change caused by a friendly hit.</summary>
public readonly record struct ReputationChange(string Target, int Delta);

/// <summary>What a friendly hit did: the reputation changes and whether the faction just turned hostile.</summary>
public sealed record FriendlyFireResult(IReadOnlyList<ReputationChange> Changes, bool TurnedHostile)
{
	public static readonly FriendlyFireResult None = new(Array.Empty<ReputationChange>(), false);
}

/// <summary>
/// One raid's friendly fire (M8b spec s2 and s3): a hit by the player on an AI of their own side's faction costs
/// reputation with that faction and the player's quartermaster, a kill costs much more, and three hits make the
/// faction hostile for the rest of the raid. Also holds the factions that start the raid hostile because their
/// reputation is low. Pure logic; the raid feeds it the hits.
/// </summary>
public sealed class FriendlyFireTracker
{
	private readonly Faction _own;
	private readonly string _quartermaster;
	private readonly FriendlyFireTuning _t;
	private readonly HashSet<Faction> _hostile = new();
	private int _hits;

	public FriendlyFireTracker(Faction ownFaction, string quartermasterId, FriendlyFireTuning? tuning = null)
	{
		_own = ownFaction;
		_quartermaster = quartermasterId;
		_t = tuning ?? new FriendlyFireTuning();
	}

	/// <summary>A faction turned hostile to the player for the rest of the raid.</summary>
	public event Action<Faction>? BecameHostile;

	public IReadOnlySet<Faction> Hostile => _hostile;

	public int Hits => _hits;

	/// <summary>Whether this faction attacks the player on sight this raid, on top of the normal faction relations.</summary>
	public bool IsHostileToPlayer(Faction faction) => _hostile.Contains(faction);

	/// <summary>The raid starts with the faction hostile (its reputation with the player is too low).</summary>
	public void StartHostile(Faction faction)
	{
		if (_hostile.Add(faction))
			BecameHostile?.Invoke(faction);
	}

	/// <summary>
	/// The player hit an AI of <paramref name="victim"/>, killing it or not. Only hits on the player's own faction count;
	/// everything else changes nothing.
	/// </summary>
	public FriendlyFireResult RegisterHit(Faction victim, bool killed)
	{
		if (victim != _own)
			return FriendlyFireResult.None;
		_hits++;
		var delta = -(killed ? _t.KillPenalty : _t.HitPenalty);
		var changes = new List<ReputationChange> { new(_quartermaster, delta) };
		if (ReputationBook.FactionId(victim) is { } faction)
			changes.Insert(0, new ReputationChange(faction, delta));
		var turned = _hits >= _t.HitsToHostile && _hostile.Add(victim);
		if (turned)
			BecameHostile?.Invoke(victim);
		return new FriendlyFireResult(changes, turned);
	}

	/// <summary>Applies a result's changes to the reputation book.</summary>
	public static void Apply(FriendlyFireResult result, ReputationBook book)
	{
		foreach (var c in result.Changes)
			book.Add(c.Target, c.Delta);
	}

	/// <summary>Reputation changes made so far this raid, summed per target, for the result screen.</summary>
	public static IReadOnlyDictionary<string, int> Total(IEnumerable<FriendlyFireResult> results)
		=> results.SelectMany(r => r.Changes).GroupBy(c => c.Target).ToDictionary(g => g.Key, g => g.Sum(c => c.Delta));
}
