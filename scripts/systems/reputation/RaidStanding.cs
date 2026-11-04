using System.Collections.Generic;

namespace ProjectD;

/// <summary>
/// The player's standing inside the raid that is running (M8b spec s3): which factions are hostile to them on top of
/// the normal faction relations, and the friendly-fire results so far for the result screen. Static so the AI target
/// check can read it without a node; the raid sets it at the start and clears it at the end. AI against AI is never
/// touched by it.
/// </summary>
public static class RaidStanding
{
	private static readonly List<FriendlyFireResult> Results = new();

	public static FriendlyFireTracker? Tracker { get; private set; }

	/// <summary>A faction turned hostile to the player for the rest of the raid (the HUD shows a line).</summary>
	public static event System.Action<Faction>? FactionTurnedHostile;

	/// <summary>Whether this faction attacks the player on sight this raid for a reason beyond the normal relations.</summary>
	public static bool IsHostileToPlayer(Faction faction) => Tracker?.IsHostileToPlayer(faction) ?? false;

	/// <summary>Reputation changes made by friendly fire this raid, per target.</summary>
	public static IReadOnlyDictionary<string, int> Changes => FriendlyFireTracker.Total(Results);

	public static void Begin(FriendlyFireTracker tracker)
	{
		Results.Clear();
		Tracker = tracker;
		tracker.BecameHostile += faction => FactionTurnedHostile?.Invoke(faction);
	}

	public static void Record(FriendlyFireResult result)
	{
		if (result.Changes.Count > 0)
			Results.Add(result);
	}

	/// <summary>The raid is over: nobody is hostile to the player any more (the changes stay for the result screen until the next raid).</summary>
	public static void End() => Tracker = null;
}
