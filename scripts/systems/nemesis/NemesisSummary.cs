using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>One place a map names: a text key and where it is on the ground.</summary>
public readonly record struct Landmark(string NameKey, float X, float Z);

/// <summary>Text for the Nemeses tab, the death screen and the HUD (M8c spec s4 and s5). Pure; names come in as lookups.</summary>
public static class NemesisSummary
{
	/// <summary>How near a landmark must be to give the place its name, metres.</summary>
	public const float LandmarkReachM = 90f;

	/// <summary>The nearest landmark within reach of a point, or null in open ground.</summary>
	public static string? NearestLandmark(float x, float z, IEnumerable<Landmark> landmarks, float reachM = LandmarkReachM)
	{
		string? best = null;
		var bestDistance = reachM;
		foreach (var l in landmarks)
		{
			var d = MathF.Sqrt((l.X - x) * (l.X - x) + (l.Z - z) * (l.Z - z));
			if (d <= bestDistance)
			{
				best = l.NameKey;
				bestDistance = d;
			}
		}
		return best;
	}

	/// <summary>Where it killed you: "near the water tower (Border Strip)" or just the map when no landmark was close.</summary>
	public static string Location(NemesisKill kill, Func<string, string> text)
	{
		var map = text("map." + kill.MapId);
		return kill.Place == ""
			? text("nemesis.where.open").Replace("{0}", map)
			: text("nemesis.where.near").Replace("{0}", text(kill.Place)).Replace("{1}", map);
	}

	/// <summary>What it carries: item names with counts, most numerous first, trimmed to a few lines.</summary>
	public static IReadOnlyList<string> Carries(Nemesis nemesis, Func<string, string> itemName, int max = 12)
	{
		var groups = nemesis.Gear.AllItems().GroupBy(i => i.Spec.Id)
			.Select(g => (Name: itemName(g.Key), Count: g.Sum(i => Math.Max(1, i.Count))))
			.OrderByDescending(g => g.Count).ThenBy(g => g.Name, StringComparer.Ordinal)
			.ToList();
		var lines = groups.Take(max).Select(g => g.Count > 1 ? $"{g.Name} x{g.Count}" : g.Name).ToList();
		if (groups.Count > max)
			lines.Add($"... and {groups.Count - max} more kinds");
		return lines;
	}

	/// <summary>One line per time it killed you, oldest first: "Raid 3: near the church (Border Strip)".</summary>
	public static IReadOnlyList<string> KillLog(Nemesis nemesis, Func<string, string> text)
		=> nemesis.Kills.Select(k => text("nemesis.raid").Replace("{0}", k.Raid.ToString()) + " " + Location(k, text)).ToList();

	/// <summary>The death screen line: who has your gear.</summary>
	public static string KilledBy(Nemesis nemesis, Func<string, string> text)
		=> text("nemesis.killed_by").Replace("{0}", nemesis.Name).Replace("{1}", nemesis.Nickname);

	/// <summary>The HUD line when the player kills a nemesis.</summary>
	public static string KilledLine(Nemesis nemesis, Func<string, string> text)
		=> text("nemesis.killed").Replace("{0}", nemesis.Name);

	/// <summary>The hub news line for a nemesis that died without the player: "X was killed by Y."</summary>
	public static string FallenLine(FallenNemesis fallen, Func<string, string> text)
		=> text(fallen.KilledBy == "the player" ? "nemesis.fallen.player" : "nemesis.fallen.other")
			.Replace("{0}", fallen.Name).Replace("{1}", fallen.KilledBy);
}
