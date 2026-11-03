using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace ProjectD;

/// <summary>A layout file that cannot be used; the message lists every problem found.</summary>
public sealed class LayoutException : Exception
{
	public IReadOnlyList<string> Problems { get; }

	public LayoutException(IReadOnlyList<string> problems)
		: base("invalid map layout:\n" + string.Join("\n", problems.Select(p => "  - " + p)))
		=> Problems = problems;
}

/// <summary>A rectangle zone of the map (perimeter, transit, poi, core, boss, deposit) with its loot tier.</summary>
public sealed class LayoutZone
{
	public string Id { get; set; } = "";
	public string Type { get; set; } = "";
	/// <summary>x, y (north-west corner on the ground plane), width, depth in metres.</summary>
	public float[] Rect { get; set; } = Array.Empty<float>();
	public string LootTier { get; set; } = "";
	[JsonIgnore] public Rect2 Area => MapLayout.ToRect(Rect);
}

/// <summary>A hole in a building wall: a door or a window with a sill height.</summary>
public sealed class LayoutOpening
{
	/// <summary>Wall letter: N, E, S or W.</summary>
	public string Wall { get; set; } = "";
	/// <summary>Door or window.</summary>
	public string Kind { get; set; } = "door";
	/// <summary>Distance in metres along the wall from its start (west end of N and S, north end of E and W).</summary>
	public float Offset { get; set; }
	public float Width { get; set; } = 1.2f;
	public float Height { get; set; } = 2.2f;
	public float Sill { get; set; }
	/// <summary>Floor the opening is on, 0 for the ground floor.</summary>
	public int Floor { get; set; }
}

/// <summary>A straight stair between floors: a footprint rectangle and the compass direction it climbs towards.</summary>
public sealed class LayoutStair
{
	public float[] Rect { get; set; } = Array.Empty<float>();
	/// <summary>N, E, S or W: the end of the stair that is the top.</summary>
	public string Dir { get; set; } = "";
	[JsonIgnore] public Rect2 Area => MapLayout.ToRect(Rect);
}

/// <summary>A building footprint with floors, wall openings and simple interior walls.</summary>
public sealed class LayoutBuilding
{
	public string Id { get; set; } = "";
	public float[] Rect { get; set; } = Array.Empty<float>();
	public int Floors { get; set; } = 1;
	public float FloorHeight { get; set; } = 3f;
	public bool Enterable { get; set; } = true;
	/// <summary>Height of the ground floor above the ground plane, for a building standing on a mound.</summary>
	public float Elevation { get; set; }
	public List<LayoutOpening> Openings { get; set; } = new();
	/// <summary>Interior wall segments, each x1, y1, x2, y2 in map metres.</summary>
	public List<float[]> InteriorWalls { get; set; } = new();
	/// <summary>Stairs from each floor to the next; the same footprint is cut from every floor slab above.</summary>
	public List<LayoutStair> Stairs { get; set; } = new();
	[JsonIgnore] public Rect2 Area => MapLayout.ToRect(Rect);
}

/// <summary>A cover or set-dressing piece: a low wall, sandbags, a vehicle, a crate stack, a fence segment, a tree.</summary>
public sealed class LayoutProp
{
	public string Id { get; set; } = "";
	public string Kind { get; set; } = "";
	public float[] Pos { get; set; } = Array.Empty<float>();
	/// <summary>Degrees about the vertical axis.</summary>
	public float Rot { get; set; }
	/// <summary>Width, depth, height.</summary>
	public float[] Size { get; set; } = Array.Empty<float>();
	/// <summary>Height of the base above the ground plane, for a prop standing on a mound.</summary>
	public float Elevation { get; set; }
	[JsonIgnore] public Vector2 Position => new(Pos[0], Pos[1]);
	[JsonIgnore] public Rect2 Area => new(Pos[0] - Size[0] / 2f, Pos[1] - Size[1] / 2f, Size[0], Size[1]);
	[JsonIgnore] public float Height => Size[2];
}

/// <summary>A trench or tunnel cut below grade, or a raised mound.</summary>
public sealed class LayoutTerrain
{
	public string Id { get; set; } = "";
	/// <summary>cut or mound.</summary>
	public string Kind { get; set; } = "";
	public float[] Rect { get; set; } = Array.Empty<float>();
	/// <summary>Metres below (cut) or above (mound) the ground.</summary>
	public float Height { get; set; }
	[JsonIgnore] public Rect2 Area => MapLayout.ToRect(Rect);
}

/// <summary>A named polyline the validator walks: a main route between spawns, points of interest and extracts.</summary>
public sealed class LayoutRoute
{
	public string Id { get; set; } = "";
	public List<float[]> Points { get; set; } = new();
	/// <summary>A route that is open ground by design (the death strip); it skips the cover-gap rule.</summary>
	public bool Exposed { get; set; }
	[JsonIgnore] public IEnumerable<Vector2> Path => Points.Select(p => new Vector2(p[0], p[1]));
}

/// <summary>A gameplay marker (map standard s5). Free-form properties carry side, tier, kind, cost, key and so on.</summary>
public sealed class LayoutMarker
{
	public string Id { get; set; } = "";
	public string Type { get; set; } = "";
	public float[] Pos { get; set; } = Array.Empty<float>();
	/// <summary>Height above the ground, metres.</summary>
	public float Height { get; set; }
	public Dictionary<string, string> Props { get; set; } = new();
	[JsonIgnore] public Vector2 Position => new(Pos[0], Pos[1]);
	public string Prop(string key) => Props.TryGetValue(key, out var v) ? v : "";

	/// <summary>The "size" property as width, height, depth ("8,3,8"), or the fallback when it is missing or malformed.</summary>
	public Vector3 SizeOr(Vector3 fallback)
	{
		var parts = Prop("size").Split(',');
		return parts.Length == 3 && parts.All(p => float.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
			? new Vector3(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]))
			: fallback;
	}

	private static float Parse(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A raid map as data (raid map standard s1): data/maps/&lt;id&gt;.layout.json. Pure model; the graybox generator and
/// the <see cref="MapValidator"/> both read it. Ground plane coordinates are x east, y south, in metres.
/// </summary>
public sealed class MapLayout
{
	public static readonly string[] SizeClasses = { "small", "medium" };
	public static readonly string[] ZoneTypes = { "perimeter", "transit", "poi", "core", "boss", "deposit" };
	public static readonly string[] LootTiers = { "low", "mid", "high" };
	public static readonly string[] TerrainKinds = { "cut", "mound" };
	public static readonly string[] Walls = { "N", "E", "S", "W" };
	public static readonly string[] OpeningKinds = { "door", "window" };
	public static readonly string[] MarkerTypes =
	{
		"playerSpawn", "aiSpawn", "waveSpawn", "extract", "tunnel", "transit", "arrival", "loot", "questZone", "landmark",
		"bossRoute", "lock", "knowledgeSpot", "deposit", "minefield", "safeLane", "foliage",
	};

	public string Id { get; set; } = "";
	/// <summary>Width (x) and depth (y) of the map in metres.</summary>
	public float[] Size { get; set; } = Array.Empty<float>();
	public string SizeClass { get; set; } = "";
	public int RaidMinutes { get; set; }
	/// <summary>Marker types this map needs on top of the core set the validator always checks.</summary>
	public List<string> RequiredMarkers { get; set; } = new();
	public List<LayoutZone> Zones { get; set; } = new();
	public List<LayoutBuilding> Buildings { get; set; } = new();
	public List<LayoutProp> Props { get; set; } = new();
	public List<LayoutTerrain> Terrain { get; set; } = new();
	public List<LayoutRoute> Routes { get; set; } = new();
	public List<LayoutMarker> Markers { get; set; } = new();

	[JsonIgnore] public Rect2 Bounds => new(0f, 0f, Size[0], Size[1]);
	[JsonIgnore] public float ShortSide => Mathf.Min(Size[0], Size[1]);

	public IEnumerable<LayoutMarker> OfType(string type) => Markers.Where(m => m.Type == type);

	internal static Rect2 ToRect(float[] r) => new(r[0], r[1], r[2], r[3]);

	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	/// <summary>Parses and checks a layout file; throws <see cref="LayoutException"/> listing every problem.</summary>
	public static MapLayout Parse(string json)
	{
		MapLayout layout;
		try
		{
			layout = JsonSerializer.Deserialize<MapLayout>(json, Options) ?? throw new LayoutException(new[] { "file is empty" });
		}
		catch (JsonException e)
		{
			throw new LayoutException(new[] { "bad JSON: " + e.Message });
		}
		var problems = layout.Check();
		if (problems.Count > 0)
			throw new LayoutException(problems);
		return layout;
	}

	/// <summary>Structural problems only (shape, ranges, names); design rules live in <see cref="MapValidator"/>.</summary>
	public List<string> Check()
	{
		var p = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) p.Add("map id is missing");
		if (Size.Length != 2 || Size.Any(s => s <= 0f)) { p.Add("size must be [width, depth] above zero"); return p; }
		if (!SizeClasses.Contains(SizeClass)) p.Add($"sizeClass '{SizeClass}' must be one of {string.Join(", ", SizeClasses)}");
		if (RaidMinutes <= 0) p.Add("raidMinutes must be above zero");
		foreach (var r in RequiredMarkers.Where(r => !MarkerTypes.Contains(r)))
			p.Add($"requiredMarkers: unknown marker type '{r}'");

		var ids = new HashSet<string>();
		void NewId(string kind, string id)
		{
			if (string.IsNullOrWhiteSpace(id)) p.Add($"{kind} without an id");
			else if (!ids.Add(id)) p.Add($"duplicate id '{id}' ({kind})");
		}
		void CheckRect(string id, float[] r)
		{
			if (r.Length != 4 || r[2] <= 0f || r[3] <= 0f) { p.Add($"'{id}': rect must be [x, y, width, depth] above zero"); return; }
			if (!Bounds.Encloses(ToRect(r))) p.Add($"'{id}': rect outside the map");
		}
		void CheckPoint(string id, float[] pt)
		{
			if (pt.Length != 2) p.Add($"'{id}': position must be [x, y]");
			else if (!Bounds.HasPoint(new Vector2(pt[0], pt[1]))) p.Add($"'{id}': position outside the map");
		}

		foreach (var z in Zones)
		{
			NewId("zone", z.Id); CheckRect(z.Id, z.Rect);
			if (!ZoneTypes.Contains(z.Type)) p.Add($"'{z.Id}': zone type '{z.Type}' unknown");
			if (z.LootTier != "" && !LootTiers.Contains(z.LootTier)) p.Add($"'{z.Id}': loot tier '{z.LootTier}' unknown");
		}
		foreach (var b in Buildings)
		{
			NewId("building", b.Id); CheckRect(b.Id, b.Rect);
			if (b.Floors < 1) p.Add($"'{b.Id}': floors must be at least 1");
			if (b.FloorHeight <= 0f) p.Add($"'{b.Id}': floorHeight must be above zero");
			foreach (var o in b.Openings)
			{
				if (!Walls.Contains(o.Wall)) p.Add($"'{b.Id}': opening wall '{o.Wall}' must be N, E, S or W");
				if (!OpeningKinds.Contains(o.Kind)) p.Add($"'{b.Id}': opening kind '{o.Kind}' must be door or window");
				if (o.Width <= 0f || o.Height <= 0f) p.Add($"'{b.Id}': opening size must be above zero");
				if (b.Rect.Length == 4)
				{
					var wallLength = o.Wall is "N" or "S" ? b.Rect[2] : b.Rect[3];
					if (o.Offset < 0f || o.Offset + o.Width > wallLength) p.Add($"'{b.Id}': opening runs past the {o.Wall} wall");
				}
			}
			foreach (var _ in b.InteriorWalls.Where(w => w.Length != 4))
				p.Add($"'{b.Id}': interior wall must be [x1, y1, x2, y2]");
			foreach (var o in b.Openings.Where(o => o.Floor < 0 || o.Floor >= b.Floors))
				p.Add($"'{b.Id}': opening on floor {o.Floor} but the building has {b.Floors}");
			foreach (var st in b.Stairs)
			{
				if (!Walls.Contains(st.Dir)) p.Add($"'{b.Id}': stair dir '{st.Dir}' must be N, E, S or W");
				if (st.Rect.Length != 4 || st.Rect[2] <= 0f || st.Rect[3] <= 0f) p.Add($"'{b.Id}': stair rect must be [x, y, width, depth]");
				else if (b.Rect.Length == 4 && !ToRect(b.Rect).Encloses(st.Area)) p.Add($"'{b.Id}': stair outside the building");
			}
			if (b.Stairs.Count == 0 && b.Floors > 1 && b.Enterable) p.Add($"'{b.Id}': {b.Floors} floors but no stairs");
		}
		foreach (var pr in Props)
		{
			NewId("prop", pr.Id);
			if (string.IsNullOrWhiteSpace(pr.Kind)) p.Add($"'{pr.Id}': prop kind is missing");
			if (pr.Size.Length != 3 || pr.Size.Any(s => s <= 0f)) p.Add($"'{pr.Id}': size must be [width, depth, height] above zero");
			CheckPoint(pr.Id, pr.Pos);
		}
		foreach (var t in Terrain)
		{
			NewId("terrain", t.Id); CheckRect(t.Id, t.Rect);
			if (!TerrainKinds.Contains(t.Kind)) p.Add($"'{t.Id}': terrain kind '{t.Kind}' must be cut or mound");
			if (t.Height <= 0f) p.Add($"'{t.Id}': terrain height must be above zero");
		}
		foreach (var r in Routes)
		{
			NewId("route", r.Id);
			if (r.Points.Count < 2) p.Add($"'{r.Id}': a route needs at least 2 points");
			foreach (var pt in r.Points) CheckPoint(r.Id, pt);
		}
		foreach (var m in Markers)
		{
			NewId("marker", m.Id); CheckPoint(m.Id, m.Pos);
			if (!MarkerTypes.Contains(m.Type)) p.Add($"'{m.Id}': marker type '{m.Type}' unknown");
			if (m.Type == "loot" && !LootTiers.Contains(m.Prop("tier"))) p.Add($"'{m.Id}': loot marker needs tier low, mid or high");
		}
		return p;
	}
}
