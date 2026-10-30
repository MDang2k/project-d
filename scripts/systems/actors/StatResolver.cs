using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>How a final value came about: the base, then each modifier in the order it was applied.</summary>
public sealed record StatBreakdown(string StatId, float Base, IReadOnlyList<StatModifier> Modifiers, float Final)
{
	/// <summary>One line, for example "100 (base 80, veteran +25%)".</summary>
	public override string ToString()
		=> Modifiers.Count == 0 ? $"{Final:0.###}" : $"{Final:0.###} (base {Base:0.###}, {string.Join(", ", Modifiers)})";
}

/// <summary>
/// The fixed order of operations (composable actors spec s1.3), pure and deterministic:
/// <c>(base + sum Add) x (1 + sum Pct)</c>, then Min and Max, then the limits of the stat, rounding for whole-number
/// stats, and last Set when there is one.
/// </summary>
public static class StatResolver
{
	public static float Resolve(StatDef def, float baseValue, IEnumerable<StatModifier> modifiers)
		=> Explain(def, baseValue, modifiers).Final;

	public static StatBreakdown Explain(StatDef def, float baseValue, IEnumerable<StatModifier> modifiers)
	{
		var mods = modifiers.ToList();
		var add = mods.Where(m => m.Op == ModOp.Add).Sum(m => m.Value);
		var pct = mods.Where(m => m.Op == ModOp.Pct).Sum(m => m.Value);
		var value = (baseValue + add) * Math.Max(0f, 1f + pct);

		var floors = mods.Where(m => m.Op == ModOp.Min).Select(m => m.Value).ToList();
		var caps = mods.Where(m => m.Op == ModOp.Max).Select(m => m.Value).ToList();
		if (floors.Count > 0)
			value = Math.Max(value, floors.Max());
		if (caps.Count > 0)
			value = Math.Min(value, caps.Min());

		value = Math.Clamp(value, def.Min, def.Max);
		if (def.Integer)
			value = MathF.Round(value, MidpointRounding.AwayFromZero);

		var forced = mods.Where(m => m.Op == ModOp.Set)
			.Select((m, order) => (m, order))
			.OrderByDescending(x => x.m.Priority).ThenByDescending(x => x.order)
			.Select(x => x.m)
			.FirstOrDefault();
		if (forced is not null)
			value = Math.Clamp(forced.Value, def.Min, def.Max);

		// listed in the order they act: adds, percentages, clamps, set
		var ordered = mods.Where(m => m.Op == ModOp.Add)
			.Concat(mods.Where(m => m.Op == ModOp.Pct))
			.Concat(mods.Where(m => m.Op is ModOp.Min or ModOp.Max))
			.Concat(forced is null ? Array.Empty<StatModifier>() : new[] { forced })
			.ToList();
		return new StatBreakdown(def.Id, baseValue, ordered, value);
	}
}
