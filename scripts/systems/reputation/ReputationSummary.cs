using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>One line of the Character tab: who, the number and the colour class.</summary>
public sealed record StandingRow(string Target, string Name, int Hundredths, string Text, StandingTone Tone);

public enum StandingTone { Hostile, Wary, Neutral, Friendly }

/// <summary>Text for the HUD, the result screen, the Character tab and the trader screen (M8b spec s4). Pure; names come in as a lookup.</summary>
public static class ReputationSummary
{
	/// <summary>Targets of the Character tab, in order: the six traders, then the three factions.</summary>
	public static readonly IReadOnlyList<string> Traders = new[] { "allied_qm", "soviet_qm", "smuggler", "medic", "engineer", "scientist" };

	public static readonly IReadOnlyList<string> Factions = new[] { "allied_pmc", "soviet_pmc", "bandits" };

	/// <summary>Hundredths as the player sees them: "+0.08", "-0.17", "0.00".</summary>
	public static string Format(int hundredths)
		=> hundredths == 0 ? "0.00" : (hundredths < 0 ? "-" : "+") + (Math.Abs(hundredths) / 100f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>How the standing reads: hostile below the raid-start line, wary below zero, friendly from +0.30.</summary>
	public static StandingTone Tone(int hundredths, ReputationTuning? t = null)
	{
		t ??= new ReputationTuning();
		return hundredths < t.HostileBelow ? StandingTone.Hostile
			: hundredths < 0 ? StandingTone.Wary
			: hundredths >= 30 ? StandingTone.Friendly
			: StandingTone.Neutral;
	}

	/// <summary>One row for every trader and faction, in the Character tab order.</summary>
	public static IReadOnlyList<StandingRow> Rows(ReputationBook book, Func<string, string> nameOf)
		=> Traders.Concat(Factions)
			.Select(t => new StandingRow(t, nameOf(t), book.Hundredths(t), Format(book.Hundredths(t)), Tone(book.Hundredths(t), book.Tuning)))
			.ToList();

	/// <summary>The raid result lines: "Allied PMCs: -0.17", only for targets that changed, factions first.</summary>
	public static IReadOnlyList<string> RaidLines(IReadOnlyDictionary<string, int> changes, Func<string, string> nameOf)
		=> changes.Where(c => c.Value != 0)
			.OrderBy(c => Factions.Contains(c.Key) ? 0 : 1).ThenBy(c => c.Key, StringComparer.Ordinal)
			.Select(c => $"{nameOf(c.Key)}: {Format(c.Value)}")
			.ToList();

	/// <summary>
	/// The trader screen line: the standing with the price effect, or the refusal. Prices are shown as the percentage
	/// change of buy prices (the sell payout moves the opposite way).
	/// </summary>
	public static string TraderLine(ReputationBook book, string traderId, Func<string, string> text)
	{
		var h = book.Hundredths(traderId);
		if (book.Refuses(traderId))
			return text("rep.trader.refuses").Replace("{0}", Format(h));
		var buyPct = (int)Math.Round((book.BuyFactor(traderId) - 1f) * 100f);
		var sellPct = (int)Math.Round((book.SellFactor(traderId) - 1f) * 100f);
		return text("rep.trader.line").Replace("{0}", Format(h)).Replace("{1}", Signed(buyPct)).Replace("{2}", Signed(sellPct));
	}

	private static string Signed(int pct) => pct == 0 ? "0%" : (pct > 0 ? "+" : "") + pct + "%";
}
