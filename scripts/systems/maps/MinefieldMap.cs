using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>A safe lane through a minefield: waypoints in order, map metres.</summary>
public sealed record SafeLane(string Name, IReadOnlyList<Vector2> Points);

/// <summary>
/// What the journal's minefield map draws (Border Strip spec s5): the map outline, the minefield rectangles and the safe
/// lanes, read from the layout. The mines on the lanes stay; the map only shows where the corridors run. Pure logic.
/// </summary>
public sealed record MinefieldMap(Vector2 MapSize, IReadOnlyList<Rect2> Minefields, IReadOnlyList<SafeLane> Lanes)
{
	/// <summary>Knowledge effect id that lets the journal show this map for a given map id.</summary>
	public static string EffectId(string mapId) => "minefield." + mapId;

	public static MinefieldMap From(MapLayout layout)
	{
		var fields = layout.OfType("minefield").Select(m =>
		{
			var size = m.SizeOr(new Vector3(38f, 2f, 80f));
			return new Rect2(m.Position.X - size.X / 2f, m.Position.Y - size.Z / 2f, size.X, size.Z);
		}).ToList();
		var lanes = layout.OfType("safeLane")
			.GroupBy(m => m.Prop("lane"))
			.Select(g => new SafeLane(g.Key, g.OrderBy(m => int.TryParse(m.Prop("order"), out var o) ? o : 0).Select(m => m.Position).ToList()))
			.ToList();
		return new MinefieldMap(new Vector2(layout.Size[0], layout.Size[1]), fields, lanes);
	}
}
