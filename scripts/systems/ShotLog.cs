using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>What one of the player's shots did, for the debug hit log.</summary>
public sealed record ShotReport(
	float RangeM,
	string Target,
	bool HitCharacter,
	BodyPart? Part = null,
	float Damage = 0f,
	bool ArmorHit = false,
	bool Penetrated = false,
	float DurabilityLoss = 0f,
	float HpBefore = 0f,
	float HpAfter = 0f,
	bool Killed = false,
	string Effects = "");

/// <summary>Formats shot reports and keeps the last few lines with their age. Pure logic.</summary>
public sealed class ShotLog
{
	private readonly List<(string Text, float Age, bool Hit)> _lines = new();

	public ShotLog(int capacity = 8, float lifetimeS = 12f)
	{
		Capacity = capacity;
		LifetimeS = lifetimeS;
	}

	public int Capacity { get; }
	public float LifetimeS { get; }

	public IReadOnlyList<(string Text, float Age, bool Hit)> Lines => _lines;

	public void Add(ShotReport report)
	{
		_lines.Add((Format(report), 0f, report.HitCharacter));
		while (_lines.Count > Capacity)
			_lines.RemoveAt(0);
	}

	public void Update(float delta)
	{
		for (var i = _lines.Count - 1; i >= 0; i--)
		{
			var l = _lines[i];
			if (l.Age + delta > LifetimeS)
				_lines.RemoveAt(i);
			else
				_lines[i] = l with { Age = l.Age + delta };
		}
	}

	/// <summary>Fades the last 3 seconds of a line's life.</summary>
	public float Alpha(float age) => Math.Clamp((LifetimeS - age) / 3f, 0f, 1f);

	public static string Format(ShotReport r)
	{
		if (!r.HitCharacter)
			return $"MISS  {r.Target}  {r.RangeM:0.0} m";
		var armor = !r.ArmorHit ? "no armor" : r.Penetrated ? $"armor penetrated (-{r.DurabilityLoss:0.0} dur)" : $"armor stopped (-{r.DurabilityLoss:0.0} dur)";
		var tail = r.Killed ? "KILLED" : $"hp {r.HpBefore:0}>{r.HpAfter:0}";
		var effects = r.Effects == "" ? "" : "  fx: " + r.Effects;
		return $"HIT  {r.Target} {r.Part}  {r.RangeM:0.0} m  dmg {r.Damage:0.0}  {armor}  {tail}{effects}";
	}

	/// <summary>Short list of active effects on a character: bleeds and fractures.</summary>
	public static string EffectsText(IEnumerable<(BodyPart Part, BleedLevel Level)> bleeds, IEnumerable<BodyPart> fractures)
	{
		var parts = bleeds.Where(b => b.Level != BleedLevel.None).Select(b => $"{(b.Level == BleedLevel.Heavy ? "heavy bleed" : "bleed")} {b.Part}")
			.Concat(fractures.Select(f => $"fracture {f}"));
		return string.Join(", ", parts);
	}
}
