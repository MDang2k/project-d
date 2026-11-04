using System;

namespace ProjectD;

/// <summary>Reputation numbers (M8b spec s2 and s3). Placeholders to tune.</summary>
public sealed record ReputationTuning(
	/// <summary>Buy prices and premiums change by this fraction per full point of reputation (-1 to +1).</summary>
	float PriceSwing = 0.10f,
	/// <summary>A trader below this (hundredths) refuses to deal.</summary>
	int RefuseBelow = -50,
	/// <summary>Your own faction below this (hundredths) is hostile to you for the whole raid.</summary>
	int HostileBelow = -30,
	/// <summary>Hundredths lost with your own faction for each trade with the other side's quartermaster.</summary>
	int OppositeTradePenalty = 1);

/// <summary>
/// Reputation per trader and faction, in integer hundredths from -100 to +100 (shown as -1.00 to +1.00). It is the
/// quest log's reputation map, so saves already carry it. Pure logic.
/// </summary>
public sealed class ReputationBook
{
	public const int Min = -100;
	public const int Max = 100;

	private readonly QuestLog _log;

	public ReputationBook(QuestLog log, ReputationTuning? tuning = null)
	{
		_log = log;
		Tuning = tuning ?? new ReputationTuning();
	}

	public ReputationTuning Tuning { get; }

	/// <summary>Faction id used as a reputation target, or null for a faction that is not tracked.</summary>
	public static string? FactionId(Faction faction) => faction switch
	{
		Faction.AlliedPmc => "allied_pmc",
		Faction.SovietPmc => "soviet_pmc",
		Faction.Bandits => "bandits",
		_ => null,
	};

	public static string QuartermasterOf(PlayerSide side) => side == PlayerSide.Allied ? "allied_qm" : "soviet_qm";

	/// <summary>The side a quartermaster serves, or null for any other trader.</summary>
	public static PlayerSide? QuartermasterSide(string traderId) => traderId switch
	{
		"allied_qm" => PlayerSide.Allied,
		"soviet_qm" => PlayerSide.Soviet,
		_ => null,
	};

	/// <summary>Hundredths for a trader or faction; 0 when nothing is recorded.</summary>
	public int Hundredths(string target) => Math.Clamp(_log.Reputation.TryGetValue(target, out var v) ? v : 0, Min, Max);

	/// <summary>-1 to +1.</summary>
	public float Value(string target) => Hundredths(target) / 100f;

	/// <summary>Adds a change, clamped; returns the change that actually happened.</summary>
	public int Add(string target, int delta)
	{
		var before = Hundredths(target);
		_log.SetReputation(target, Math.Clamp(before + delta, Min, Max));
		return Hundredths(target) - before;
	}

	/// <summary>Whether this trader will not trade, insure or give new quests (turning in finished quests still works).</summary>
	public bool Refuses(string traderId) => Hundredths(traderId) < Tuning.RefuseBelow;

	/// <summary>Factor on buy prices and insurance premiums: dearer when disliked, cheaper when liked.</summary>
	public float BuyFactor(string traderId) => 1f - Tuning.PriceSwing * Value(traderId);

	/// <summary>Factor on sell payouts.</summary>
	public float SellFactor(string traderId) => 1f + Tuning.PriceSwing * Value(traderId);

	/// <summary>Whether the player's own faction starts the raid hostile to them.</summary>
	public bool OwnFactionHostileAtStart(Faction ownFaction)
		=> FactionId(ownFaction) is { } id && Hundredths(id) < Tuning.HostileBelow;

	/// <summary>
	/// A trade with the other side's quartermaster costs a little reputation with the player's own faction. Returns the
	/// change made (0 for any other trader or before a side is chosen).
	/// </summary>
	public int OnTrade(string traderId, PlayerSide? side)
	{
		if (side is not { } own || QuartermasterSide(traderId) is not { } served || served == own)
			return 0;
		return FactionId(own.ToFaction()) is { } faction ? Add(faction, -Tuning.OppositeTradePenalty) : 0;
	}
}
