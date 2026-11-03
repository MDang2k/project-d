using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace ProjectD;

/// <summary>One box of graybox geometry: a static body with a box collider and box mesh. World space, metres.</summary>
public sealed record GrayBox(string Name, string Group, Vector3 Center, Vector3 Size, float RotYDeg, string Material);

/// <summary>A gameplay marker placed in world space, with the layout properties it carries.</summary>
public sealed record GrayMarker(string Name, string Type, Vector3 Position, float YawDeg, IReadOnlyDictionary<string, string> Props);

/// <summary>The geometry and markers of a layout, ready to be written as a scene.</summary>
public sealed record Graybox(string MapId, IReadOnlyList<GrayBox> Boxes, IReadOnlyList<GrayMarker> Markers);

/// <summary>
/// Turns a <see cref="MapLayout"/> into boxes (raid map standard s1). Pure: the same layout always gives the same
/// boxes with the same names, so overrides in the hand scene survive regeneration. The map is centred on the world
/// origin; layout x runs east (+X) and layout y runs south (+Z).
/// </summary>
public static class GrayboxBuilder
{
	public const float WallThickness = 0.3f;
	public const float InteriorThickness = 0.2f;
	public const float SlabThickness = 0.2f;
	public const float RoofThickness = 0.3f;
	public const float StairRise = 0.2f;

	/// <summary>Node-safe name: letters, digits and underscores only.</summary>
	public static string Safe(string id) => Regex.Replace(id, "[^A-Za-z0-9_]", "_");

	public static Graybox Build(MapLayout layout)
	{
		var b = new Builder(layout);
		b.Ground();
		b.Terrain();
		foreach (var building in layout.Buildings)
			b.Building(building);
		foreach (var prop in layout.Props)
			b.Prop(prop);
		b.Markers();
		return new Graybox(layout.Id, b.Boxes, b.MarkerList);
	}

	private sealed class Builder
	{
		private readonly MapLayout _l;
		private readonly Vector2 _origin;
		public readonly List<GrayBox> Boxes = new();
		public readonly List<GrayMarker> MarkerList = new();

		public Builder(MapLayout layout)
		{
			_l = layout;
			_origin = new Vector2(layout.Size[0] / 2f, layout.Size[1] / 2f);
		}

		private Vector3 World(float x, float y, float height) => new(x - _origin.X, height, y - _origin.Y);

		/// <summary>A box from a ground rectangle and a vertical span.</summary>
		private void Box(string name, string group, Rect2 r, float bottom, float top, string material)
		{
			if (r.Size.X <= 0.001f || r.Size.Y <= 0.001f || top - bottom <= 0.001f)
				return;
			Boxes.Add(new GrayBox(name, group, World(r.Position.X + r.Size.X / 2f, r.Position.Y + r.Size.Y / 2f, (bottom + top) / 2f),
				new Vector3(r.Size.X, top - bottom, r.Size.Y), 0f, material));
		}

		public void Ground()
		{
			var cuts = _l.Terrain.Where(t => t.Kind == "cut").ToList();
			var depth = 1f + (cuts.Count == 0 ? 0f : cuts.Max(c => c.Height));
			var xs = new SortedSet<float> { 0f, _l.Size[0] };
			var ys = new SortedSet<float> { 0f, _l.Size[1] };
			foreach (var c in cuts)
			{
				xs.Add(c.Area.Position.X); xs.Add(c.Area.End.X);
				ys.Add(c.Area.Position.Y); ys.Add(c.Area.End.Y);
			}
			var xl = xs.ToList();
			var yl = ys.ToList();
			var n = 0;
			for (var j = 0; j + 1 < yl.Count; j++)
			{
				var runStart = -1;
				for (var i = 0; i <= xl.Count - 1; i++)
				{
					var solid = i + 1 < xl.Count && !cuts.Any(c => c.Area.HasPoint(new Vector2((xl[i] + xl[i + 1]) / 2f, (yl[j] + yl[j + 1]) / 2f)));
					if (solid && runStart < 0)
						runStart = i;
					else if (!solid && runStart >= 0)
					{
						Box($"ground_{n++}", "Ground", new Rect2(xl[runStart], yl[j], xl[i] - xl[runStart], yl[j + 1] - yl[j]), -depth, 0f, "ground");
						runStart = -1;
					}
				}
			}
		}

		public void Terrain()
		{
			foreach (var t in _l.Terrain)
			{
				var name = Safe(t.Id);
				if (t.Kind == "cut")
					Box(name, "Terrain", t.Area, -t.Height - 1f, -t.Height, "cutfloor");
				else
					Box(name, "Terrain", t.Area, 0f, t.Height, "mound");
			}
		}

		public void Prop(LayoutProp p)
			=> Boxes.Add(new GrayBox(Safe(p.Id), "Props", World(p.Pos[0], p.Pos[1], p.Elevation + p.Size[2] / 2f), new Vector3(p.Size[0], p.Size[2], p.Size[1]),
				p.Rot, PropMaterial(p.Kind)));

		private static string PropMaterial(string kind) => kind switch
		{
			"lowWall" or "sandbags" or "crateStack" => "cover",
			"vehicle" or "wreck" => "vehicle",
			"fence" => "fence",
			"tree" => "tree",
			"tower" => "wall",
			_ => "prop",
		};

		public void Building(LayoutBuilding b)
		{
			var id = Safe(b.Id);
			var area = b.Area;
			var height = b.Floors * b.FloorHeight;
			var e = b.Elevation;
			if (!b.Enterable)
			{
				Box(id, "Buildings", area, e, e + height, "wall");
				return;
			}
			for (var f = 0; f < b.Floors; f++)
			{
				var bottom = e + f * b.FloorHeight;
				Walls(b, id, f, bottom);
				var k = 0;
				foreach (var w in b.InteriorWalls)
					InteriorWall(id, f, k++, w, bottom, b.FloorHeight);
				if (f > 0)
					Slab(b, id, f, bottom);
				if (f + 1 < b.Floors)
					foreach (var (stair, index) in b.Stairs.Select((s, i) => (s, i)))
						Stair(id, f, index, stair, bottom, b.FloorHeight);
			}
			Box($"{id}_roof", "Buildings", area, e + height, e + height + RoofThickness, "roof");
		}

		private void Walls(LayoutBuilding b, string id, int floor, float bottom)
		{
			var a = b.Area;
			var t = WallThickness;
			var top = bottom + b.FloorHeight;
			// N and S run between E and W so the corners are not doubled
			WallRun(b, id, floor, "N", a.Position.X + t, a.End.X - t, bottom, top, x0 => new Rect2(x0.Lo, a.Position.Y, x0.Hi - x0.Lo, t), a.Position.X);
			WallRun(b, id, floor, "S", a.Position.X + t, a.End.X - t, bottom, top, x0 => new Rect2(x0.Lo, a.End.Y - t, x0.Hi - x0.Lo, t), a.Position.X);
			WallRun(b, id, floor, "W", a.Position.Y, a.End.Y, bottom, top, y0 => new Rect2(a.Position.X, y0.Lo, t, y0.Hi - y0.Lo), a.Position.Y);
			WallRun(b, id, floor, "E", a.Position.Y, a.End.Y, bottom, top, y0 => new Rect2(a.End.X - t, y0.Lo, t, y0.Hi - y0.Lo), a.Position.Y);
		}

		private readonly record struct Span(float Lo, float Hi);

		/// <summary>One wall: solid pieces between its openings, a piece under each sill and a lintel over each opening.</summary>
		private void WallRun(LayoutBuilding b, string id, int floor, string wall, float lo, float hi, float bottom, float top,
			Func<Span, Rect2> rect, float wallStart)
		{
			var cuts = b.Openings.Where(o => o.Wall == wall && o.Floor == floor)
				.Select(o => (o, Span: new Span(Mathf.Max(lo, wallStart + o.Offset), Mathf.Min(hi, wallStart + o.Offset + o.Width))))
				.Where(c => c.Span.Hi > c.Span.Lo).OrderBy(c => c.Span.Lo).ToList();
			var n = 0;
			var cursor = lo;
			foreach (var (o, span) in cuts)
			{
				Box($"{id}_f{floor}_{wall}_{n++}", "Buildings", rect(new Span(cursor, span.Lo)), bottom, top, "wall");
				var rect2 = rect(span);
				Box($"{id}_f{floor}_{wall}_{n++}", "Buildings", rect2, bottom, bottom + o.Sill, "wall");
				Box($"{id}_f{floor}_{wall}_{n++}", "Buildings", rect2, bottom + o.Sill + o.Height, top, "wall");
				cursor = span.Hi;
			}
			Box($"{id}_f{floor}_{wall}_{n}", "Buildings", rect(new Span(cursor, hi)), bottom, top, "wall");
		}

		private void InteriorWall(string id, int floor, int index, float[] w, float bottom, float height)
		{
			var from = new Vector2(w[0], w[1]);
			var to = new Vector2(w[2], w[3]);
			var length = from.DistanceTo(to);
			if (length < 0.01f)
				return;
			var mid = (from + to) / 2f;
			var yaw = Mathf.RadToDeg(Mathf.Atan2(-(to.Y - from.Y), to.X - from.X));
			Boxes.Add(new GrayBox($"{id}_f{floor}_in{index}", "Buildings", World(mid.X, mid.Y, bottom + height / 2f),
				new Vector3(length, height, InteriorThickness), yaw, "wall"));
		}

		/// <summary>A floor slab with the stair footprint cut out.</summary>
		private void Slab(LayoutBuilding b, string id, int floor, float level)
		{
			var pieces = new List<Rect2> { b.Area };
			foreach (var s in b.Stairs)
				pieces = pieces.SelectMany(p => Subtract(p, s.Area)).ToList();
			var k = 0;
			foreach (var p in pieces)
				Box($"{id}_slab{floor}_{k++}", "Buildings", p, level - SlabThickness, level, "slab");
		}

		/// <summary>The parts of <paramref name="r"/> outside <paramref name="cut"/>.</summary>
		internal static IEnumerable<Rect2> Subtract(Rect2 r, Rect2 cut)
		{
			var c = r.Intersection(cut);
			if (c.Size.X <= 0f || c.Size.Y <= 0f)
			{
				yield return r;
				yield break;
			}
			yield return new Rect2(r.Position.X, r.Position.Y, r.Size.X, c.Position.Y - r.Position.Y);
			yield return new Rect2(r.Position.X, c.End.Y, r.Size.X, r.End.Y - c.End.Y);
			yield return new Rect2(r.Position.X, c.Position.Y, c.Position.X - r.Position.X, c.Size.Y);
			yield return new Rect2(c.End.X, c.Position.Y, r.End.X - c.End.X, c.Size.Y);
		}

		/// <summary>Steps rising from the floor to the next one, towards the stair's direction.</summary>
		private void Stair(string id, int floor, int index, LayoutStair s, float level, float floorHeight)
		{
			var steps = Mathf.CeilToInt(floorHeight / StairRise);
			var rise = floorHeight / steps;
			var a = s.Area;
			var alongX = s.Dir is "E" or "W";
			var run = (alongX ? a.Size.X : a.Size.Y) / steps;
			for (var i = 0; i < steps; i++)
			{
				var from = s.Dir is "E" or "S" ? i * run : (alongX ? a.Size.X : a.Size.Y) - (i + 1) * run;
				var r = alongX ? new Rect2(a.Position.X + from, a.Position.Y, run, a.Size.Y) : new Rect2(a.Position.X, a.Position.Y + from, a.Size.X, run);
				Box($"{id}_stair{floor}_{index}_{i}", "Buildings", r, level, level + (i + 1) * rise, "stair");
			}
		}

		public void Markers()
		{
			foreach (var m in _l.Markers)
			{
				var pos = World(m.Pos[0], m.Pos[1], m.Type == "playerSpawn" ? m.Height + 0.1f : m.Height);
				var yaw = float.TryParse(m.Prop("facing"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)
					? f
					// spawns and arrivals face the map centre (forward is -Z, so yaw = atan2(-dx, -dz) with d = -pos); boxes stay on the axes
					: m.Type is "playerSpawn" or "arrival" ? Mathf.RadToDeg(Mathf.Atan2(pos.X, pos.Z)) : 0f;
				MarkerList.Add(new GrayMarker(Safe(m.Id), m.Type, pos, yaw, m.Props));
			}
		}
	}
}
