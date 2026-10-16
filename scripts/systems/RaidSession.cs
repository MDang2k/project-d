using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum RaidOutcome { None, Extracted, Killed, Mia }

/// <summary>Extraction point type (GDD s15). Payment arrives with the economy in M7.</summary>
public enum ExtractKind { AlwaysOpen, RequiresItem, RequiresPayment }

/// <summary>One extraction point: how long you must stand in it and what it needs.</summary>
public sealed record ExtractionSpec(string Id, ExtractKind Kind, float Seconds, string? RequiredItemId = null, int Cost = 0);

/// <summary>Why standing in a zone is not progressing.</summary>
public enum ExtractBlock { None, MissingItem, CannotPay }

/// <summary>
/// One raid from deploy to outcome (GDD s15): the countdown, extraction progress and the result
/// (extracted, killed, or MIA when the timer runs out). Pure logic; a node feeds in the clock.
/// </summary>
public sealed class RaidSession
{
	private readonly Dictionary<string, ExtractionSpec> _zones;

	public RaidSession(float durationS, IEnumerable<ExtractionSpec> zones)
	{
		DurationS = durationS;
		RemainingS = durationS;
		_zones = zones.ToDictionary(z => z.Id);
	}

	public float DurationS { get; }
	public float RemainingS { get; private set; }
	public float ElapsedS => DurationS - RemainingS;
	public RaidOutcome Outcome { get; private set; } = RaidOutcome.None;
	public bool IsActive => Outcome == RaidOutcome.None;

	/// <summary>Zone the player is currently extracting in, or null.</summary>
	public string? ExtractingZoneId { get; private set; }

	/// <summary>0..1 progress of the current extraction.</summary>
	public float ExtractProgress { get; private set; }
	public ExtractBlock Blocked { get; private set; }

	/// <summary>The zone the player extracted through; null until the raid ends extracted.</summary>
	public string? ExtractedZoneId { get; private set; }

	/// <summary>Banknotes the player must pay for the extraction that just completed (0 for free extracts).</summary>
	public int PaidCost { get; private set; }

	public event Action<RaidOutcome>? Ended;
	public event Action<string>? ExtractionStarted;
	public event Action<string>? ExtractionAborted;

	/// <param name="zoneId">Zone the player stands in right now (null if none).</param>
	/// <param name="hasItem">Whether the player carries an item id (for conditional zones).</param>
	/// <param name="carriedMoney">Banknotes in carried storage grids (not the secure container); paid zones need the cost.</param>
	public void Update(float delta, string? zoneId, Func<string, bool> hasItem, Func<int>? carriedMoney = null)
	{
		if (!IsActive)
			return;
		RemainingS = Math.Max(0f, RemainingS - delta);
		if (RemainingS <= 0f)
		{
			End(RaidOutcome.Mia);
			return;
		}

		ExtractBlock block = ExtractBlock.None;
		ExtractionSpec? spec = null;
		if (zoneId is not null && _zones.TryGetValue(zoneId, out spec)
			&& spec.Kind == ExtractKind.RequiresItem
			&& (spec.RequiredItemId is null || !hasItem(spec.RequiredItemId)))
		{
			block = ExtractBlock.MissingItem;
			spec = null;
		}
		else if (spec is { Kind: ExtractKind.RequiresPayment } && (carriedMoney?.Invoke() ?? 0) < spec.Cost)
		{
			block = ExtractBlock.CannotPay;
			spec = null;
		}
		Blocked = block;

		if (spec is null)
		{
			Abort();
			return;
		}
		if (ExtractingZoneId != spec.Id)
		{
			Abort();
			ExtractingZoneId = spec.Id;
			ExtractProgress = 0f;
			ExtractionStarted?.Invoke(spec.Id);
		}
		ExtractProgress = Math.Min(1f, ExtractProgress + delta / Math.Max(0.1f, spec.Seconds));
		if (ExtractProgress >= 1f)
		{
			PaidCost = spec.Kind == ExtractKind.RequiresPayment ? spec.Cost : 0;
			ExtractedZoneId = spec.Id;
			End(RaidOutcome.Extracted);
		}
	}

	/// <summary>The player died (GDD s15: lose everything except the secure container).</summary>
	public void PlayerDied()
	{
		if (IsActive)
			End(RaidOutcome.Killed);
	}

	private void Abort()
	{
		if (ExtractingZoneId is { } id)
			ExtractionAborted?.Invoke(id);
		ExtractingZoneId = null;
		ExtractProgress = 0f;
	}

	private void End(RaidOutcome outcome)
	{
		Outcome = outcome;
		if (outcome != RaidOutcome.Extracted)
		{
			ExtractingZoneId = null;
			ExtractProgress = 0f;
		}
		Ended?.Invoke(outcome);
	}
}

/// <summary>What a raid outcome does to the character's gear.</summary>
public static class RaidOutcomeRules
{
	/// <summary>
	/// Extracted keeps everything. Killed and MIA lose everything except the secure container and its
	/// contents; the lost items are returned (the nemesis, insurance and bodies use them later).
	/// </summary>
	public static List<ItemInstance> Apply(RaidOutcome outcome, CharacterInventory inventory)
		=> outcome is RaidOutcome.Killed or RaidOutcome.Mia
			? inventory.TakeEverythingExceptSecure()
			: new List<ItemInstance>();
}
