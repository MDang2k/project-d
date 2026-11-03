using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>Geometry questions the validator asks; the graybox tools inject navmesh and physics versions.</summary>
public interface IMapQueries
{
	/// <summary>Whether one ground point sees the other.</summary>
	bool LineOfSight(Vector2 from, Vector2 to);

	/// <summary>Walking distance between two points, or <see cref="float.PositiveInfinity"/> when unreachable.</summary>
	float PathLength(Vector2 from, Vector2 to);
}

/// <summary>
/// Layout-only answers: sight is blocked by building footprints, mounds of 2 m or more and props of 1.8 m or more;
/// the path is the straight line stretched by a detour factor. Good enough to catch design mistakes before a navmesh
/// exists.
/// </summary>
public sealed class GeometryQueries : IMapQueries
{
	private readonly List<Rect2> _blockers;
	private readonly float _detour;

	public GeometryQueries(MapLayout layout, float detourFactor = 1.3f)
	{
		_detour = detourFactor;
		_blockers = layout.Buildings.Select(b => b.Area)
			.Concat(layout.Terrain.Where(t => t.Kind == "mound" && t.Height >= 2f).Select(t => t.Area))
			.Concat(layout.Props.Where(p => p.Height >= 1.8f).Select(p => p.Area))
			.ToList();
	}

	public bool LineOfSight(Vector2 from, Vector2 to) => !_blockers.Any(r => SegmentHitsRect(from, to, r));

	public float PathLength(Vector2 from, Vector2 to) => from.DistanceTo(to) * _detour;

	/// <summary>Slab test of a segment against a rectangle.</summary>
	public static bool SegmentHitsRect(Vector2 a, Vector2 b, Rect2 r)
	{
		var t0 = 0f;
		var t1 = 1f;
		var d = b - a;
		for (var axis = 0; axis < 2; axis++)
		{
			var origin = axis == 0 ? a.X : a.Y;
			var delta = axis == 0 ? d.X : d.Y;
			var lo = axis == 0 ? r.Position.X : r.Position.Y;
			var hi = axis == 0 ? r.End.X : r.End.Y;
			if (Mathf.Abs(delta) < 1e-6f)
			{
				if (origin < lo || origin > hi)
					return false;
				continue;
			}
			var near = (lo - origin) / delta;
			var far = (hi - origin) / delta;
			if (near > far)
				(near, far) = (far, near);
			t0 = Mathf.Max(t0, near);
			t1 = Mathf.Min(t1, far);
			if (t0 > t1)
				return false;
		}
		return true;
	}
}

/// <summary>One broken rule: the rule name and what is wrong.</summary>
public sealed record MapIssue(string Rule, string Message);

/// <summary>The outcome of a validation run: broken rules, plus counts for the performance budget.</summary>
public sealed record MapReport(IReadOnlyList<MapIssue> Issues, IReadOnlyDictionary<string, int> Counts)
{
	public bool Ok => Issues.Count == 0;
}

/// <summary>Numbers of the checkable rules (raid map standard s4). Placeholders to tune.</summary>
public sealed record MapRules(
	int MinSpawns = 5,
	int MaxSpawns = 8,
	/// <summary>Spawn spacing as a fraction of the short map side.</summary>
	float SpawnSpacing = 0.4f,
	int MinExtracts = 3,
	int MaxExtracts = 4,
	float MinExtractPathM = 60f,
	int ExtractsPerSpawn = 2,
	int MinLandmarksVisible = 3,
	float MaxOpenStretchM = 25f,
	float CoverReachM = 2.5f,
	float RouteStepM = 1f,
	int MinHeightBands = 3,
	float MinEnterableShare = 0.5f,
	int MaxStaticBodies = 600);

/// <summary>
/// Checks a <see cref="MapLayout"/> against the raid map standard (s4) with injected sight and path queries. Pure
/// logic: every rule passes and fails on synthetic layouts in the tests.
/// </summary>
public static class MapValidator
{
	private static readonly string[] CoreMarkers = { "playerSpawn", "aiSpawn", "extract", "loot", "landmark" };

	/// <summary>Minimum loot spots per tier for each size class (low, mid, high).</summary>
	private static (int Low, int Mid, int High) LootMinimum(string sizeClass)
		=> sizeClass == "small" ? (4, 4, 3) : (8, 6, 4);

	public static MapReport Validate(MapLayout layout, IMapQueries? queries = null, MapRules? rules = null)
	{
		queries ??= new GeometryQueries(layout);
		rules ??= new MapRules();
		var issues = new List<MapIssue>();
		void Fail(string rule, string message) => issues.Add(new MapIssue(rule, message));

		CheckSpawns(layout, queries, rules, Fail);
		CheckExtracts(layout, queries, rules, Fail);
		CheckLoot(layout, Fail);
		CheckLandmarks(layout, queries, rules, Fail);
		CheckCover(layout, rules, Fail);
		CheckHeight(layout, rules, Fail);
		CheckBuildings(layout, rules, Fail);
		CheckMarkers(layout, Fail);

		var bodies = layout.Buildings.Count + layout.Props.Count + layout.Terrain.Count;
		if (bodies > rules.MaxStaticBodies)
			Fail("performance", $"{bodies} static bodies, budget is {rules.MaxStaticBodies}");
		var counts = new Dictionary<string, int>
		{
			["buildings"] = layout.Buildings.Count,
			["props"] = layout.Props.Count,
			["terrain"] = layout.Terrain.Count,
			["markers"] = layout.Markers.Count,
			["staticBodies"] = bodies,
		};
		return new MapReport(issues, counts);
	}

	private static void CheckSpawns(MapLayout l, IMapQueries q, MapRules r, Action<string, string> fail)
	{
		var spawns = l.OfType("playerSpawn").ToList();
		if (spawns.Count < r.MinSpawns || spawns.Count > r.MaxSpawns)
			fail("spawns", $"{spawns.Count} player spawns, need {r.MinSpawns} to {r.MaxSpawns}");
		var spacing = r.SpawnSpacing * l.ShortSide;
		var highLoot = l.OfType("loot").Where(m => m.Prop("tier") == "high").ToList();
		for (var i = 0; i < spawns.Count; i++)
		{
			for (var j = i + 1; j < spawns.Count; j++)
			{
				var d = spawns[i].Position.DistanceTo(spawns[j].Position);
				if (d < spacing)
					fail("spawns", $"'{spawns[i].Id}' and '{spawns[j].Id}' are {d:0} m apart, need {spacing:0} m");
				if (q.LineOfSight(spawns[i].Position, spawns[j].Position))
					fail("spawns", $"'{spawns[i].Id}' sees '{spawns[j].Id}'");
			}
			foreach (var loot in highLoot.Where(h => q.LineOfSight(spawns[i].Position, h.Position)))
				fail("spawns", $"'{spawns[i].Id}' sees high-tier loot '{loot.Id}'");
		}
	}

	private static void CheckExtracts(MapLayout l, IMapQueries q, MapRules r, Action<string, string> fail)
	{
		// extracts sharing a group (a tunnel with two ends) count as one exit
		var groups = l.OfType("extract").GroupBy(e => e.Prop("group") is { Length: > 0 } g ? g : e.Id).ToList();
		if (groups.Count < r.MinExtracts || groups.Count > r.MaxExtracts)
			fail("extracts", $"{groups.Count} extracts, need {r.MinExtracts} to {r.MaxExtracts}");
		if (!groups.Any(g => g.Any(e => e.Prop("kind") is "" or "open")))
			fail("extracts", "no always-open extract");
		if (!groups.Any(g => g.All(e => e.Prop("kind") is not ("" or "open"))))
			fail("extracts", "no conditional extract");

		foreach (var spawn in l.OfType("playerSpawn"))
		{
			var side = spawn.Prop("side");
			var reachable = groups
				.Where(g => g.Min(e => q.PathLength(spawn.Position, e.Position)) is var d && d >= r.MinExtractPathM && !float.IsInfinity(d))
				.ToList();
			if (reachable.Count < r.ExtractsPerSpawn)
				fail("extracts", $"'{spawn.Id}' reaches {reachable.Count} extracts at {r.MinExtractPathM:0} m or more, need {r.ExtractsPerSpawn}");
			if (side != "" && groups.Any(g => g.Any(e => e.Prop("side") != "")) && !reachable.Any(g => g.Any(e => e.Prop("side") == side)))
				fail("extracts", $"'{spawn.Id}' has no extract on its own side ({side})");
		}
	}

	private static void CheckLoot(MapLayout l, Action<string, string> fail)
	{
		var min = LootMinimum(l.SizeClass);
		int Count(string tier) => l.OfType("loot").Count(m => m.Prop("tier") == tier);
		if (Count("low") < min.Low) fail("loot", $"{Count("low")} low-tier spots, need {min.Low}");
		if (Count("mid") < min.Mid) fail("loot", $"{Count("mid")} mid-tier spots, need {min.Mid}");
		if (Count("high") < min.High) fail("loot", $"{Count("high")} high-tier spots, need {min.High}");
	}

	private static void CheckLandmarks(MapLayout l, IMapQueries q, MapRules r, Action<string, string> fail)
	{
		var landmarks = l.OfType("landmark").ToList();
		foreach (var spawn in l.OfType("playerSpawn"))
		{
			var seen = landmarks.Count(m => q.LineOfSight(spawn.Position, m.Position));
			if (seen < r.MinLandmarksVisible)
				fail("landmarks", $"'{spawn.Id}' sees {seen} landmarks, need {r.MinLandmarksVisible}");
		}
	}

	private static void CheckCover(MapLayout l, MapRules r, Action<string, string> fail)
	{
		var cover = l.Buildings.Select(b => b.Area)
			.Concat(l.Props.Where(p => p.Height >= 0.5f).Select(p => p.Area))
			.Concat(l.Terrain.Where(t => t.Kind == "mound" && t.Height >= 0.5f).Select(t => t.Area))
			.Select(a => a.Grow(r.CoverReachM))
			.ToList();
		foreach (var route in l.Routes.Where(x => !x.Exposed))
		{
			var open = 0f;
			var worst = 0f;
			var points = route.Path.ToList();
			for (var i = 0; i + 1 < points.Count; i++)
			{
				var length = points[i].DistanceTo(points[i + 1]);
				var steps = Mathf.Max(1, Mathf.CeilToInt(length / r.RouteStepM));
				for (var s = 0; s < steps; s++)
				{
					var p = points[i].Lerp(points[i + 1], (s + 0.5f) / steps);
					if (cover.Any(c => c.HasPoint(p)))
						open = 0f;
					else
					{
						open += length / steps;
						worst = Mathf.Max(worst, open);
					}
				}
			}
			if (worst > r.MaxOpenStretchM)
				fail("cover", $"route '{route.Id}' has an open stretch of {worst:0} m, limit {r.MaxOpenStretchM:0} m");
		}
	}

	private static void CheckHeight(MapLayout l, MapRules r, Action<string, string> fail)
	{
		var bands = 1; // ground
		if (l.Terrain.Any(t => t.Kind == "cut")) bands++;
		if (l.Buildings.Any(b => b.Floors >= 2) || l.Terrain.Any(t => t.Kind == "mound" && t.Height >= 3f) || l.Props.Any(p => p.Kind == "tower"))
			bands++;
		if (bands < r.MinHeightBands)
			fail("height", $"{bands} height bands, need {r.MinHeightBands} (below grade, ground, upper floors or towers)");
	}

	private static void CheckBuildings(MapLayout l, MapRules r, Action<string, string> fail)
	{
		if (l.Buildings.Count == 0)
			return;
		var share = l.Buildings.Count(b => b.Enterable) / (float)l.Buildings.Count;
		if (share < r.MinEnterableShare)
			fail("buildings", $"{share:P0} of buildings enterable, need {r.MinEnterableShare:P0}");
	}

	private static void CheckMarkers(MapLayout l, Action<string, string> fail)
	{
		foreach (var type in CoreMarkers.Concat(l.RequiredMarkers).Distinct().Where(t => !l.OfType(t).Any()))
			fail("markers", $"no '{type}' marker");
		var gives = l.Markers.Select(m => m.Prop("gives")).Where(g => g != "").ToHashSet();
		foreach (var lockMarker in l.OfType("lock"))
		{
			var key = lockMarker.Prop("key");
			if (key == "")
				fail("markers", $"lock '{lockMarker.Id}' has no key or code");
			else if (!gives.Contains(key))
				fail("markers", $"lock '{lockMarker.Id}' needs '{key}' but no marker gives it");
		}
	}
}
