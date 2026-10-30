using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>
/// The text of the in-game stat inspector (composable actors spec s5): the actor, its tags and modifier sources,
/// then every stat by area with the final value and, when something changed it, how it came about.
/// </summary>
public static class ActorInspectorText
{
	public static IReadOnlyList<string> Lines(ActorInstance actor, bool onlyChanged = false)
	{
		var lines = new List<string>
		{
			$"{actor.Def.Id}  ({actor.Def.Name})  {actor.Def.Faction}",
			"tags: " + (actor.Def.Tags.Count == 0 ? "-" : string.Join(", ", actor.Def.Tags)),
		};
		var active = actor.Stats.Active();
		lines.Add("modifiers: " + (active.Count == 0 ? "-" : string.Join(", ", active
			.GroupBy(a => a.Modifier.Source)
			.Select(g => g.Key + (g.Any(a => a.Remaining is not null) ? $" ({g.Max(a => a.Remaining):0.#}s)" : "")))));
		foreach (var group in Stats.All.GroupBy(d => d.Group))
		{
			var rows = group.Where(d => !onlyChanged || actor.Stats.Modifiers(d.Id).Count > 0).ToList();
			if (rows.Count == 0)
				continue;
			lines.Add("");
			lines.Add("[" + group.Key + "]");
			foreach (var def in rows)
				lines.Add($"  {def.Id[(def.Group.Length + 1)..],-26}{Line(actor.Stats.Explain(def.Id), def)}");
		}
		return lines;
	}

	private static string Line(StatBreakdown b, StatDef def)
		=> b.Modifiers.Count == 0 ? Format(b.Final, def) : $"{Format(b.Final, def)} (base {Format(b.Base, def)}, {string.Join(", ", b.Modifiers)})";

	private static string Format(float v, StatDef def) => def.Unit is "" or "x" ? $"{v:0.###}" : $"{v:0.###} {def.Unit}";
}
