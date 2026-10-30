using System;

namespace ProjectD;

/// <summary>How a modifier changes a stat (composable actors spec s1.3).</summary>
public enum ModOp
{
	/// <summary>Flat amount added to the base.</summary>
	Add,
	/// <summary>Fraction added to one shared percentage: +0.1 and +0.1 make +20%, not +21%.</summary>
	Pct,
	/// <summary>The result cannot go below this.</summary>
	Min,
	/// <summary>The result cannot go above this.</summary>
	Max,
	/// <summary>Forces the value; the highest priority wins, the latest added among equals.</summary>
	Set,
}

/// <summary>
/// One change to one stat. Adding a modifier with the same stat, source and op as an existing one replaces it, so
/// an effect like "suppressed" never stacks with itself.
/// </summary>
/// <param name="Source">Where it comes from (veteran, tier_2, suppressed...), shown by the inspector and used to remove it.</param>
/// <param name="DurationS">Seconds it lasts; null is permanent.</param>
public sealed record StatModifier(string StatId, ModOp Op, float Value, string Source, int Priority = 0, float? DurationS = null)
{
	public override string ToString() => Op switch
	{
		ModOp.Add => $"{Source} {Value:+0.###;-0.###}",
		ModOp.Pct => $"{Source} {Value * 100f:+0.#;-0.#}%",
		ModOp.Min => $"{Source} min {Value:0.###}",
		ModOp.Max => $"{Source} max {Value:0.###}",
		_ => $"{Source} set {Value:0.###}",
	};

	public static ModOp ParseOp(string text) => Enum.TryParse<ModOp>(text.Trim(), true, out var op)
		? op
		: throw new ArgumentException($"unknown modifier op '{text}' (use Add, Pct, Min, Max or Set)");
}
