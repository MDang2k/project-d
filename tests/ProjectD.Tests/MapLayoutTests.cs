using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class MapLayoutTests
{
	private const string Good = """
	{
	  // comments and trailing commas are fine
	  "id": "t", "size": [180, 180], "sizeClass": "small", "raidMinutes": 20,
	  "zones": [ { "id": "z1", "type": "core", "rect": [60, 60, 60, 60], "lootTier": "high" }, ],
	  "buildings": [ { "id": "b1", "rect": [70, 70, 20, 10], "floors": 2,
	    "openings": [ { "wall": "N", "kind": "door", "offset": 2 }, { "wall": "E", "kind": "window", "offset": 3, "sill": 1 } ],
	    "interiorWalls": [ [80, 70, 80, 80] ], "stairs": [ { "rect": [72, 72, 4, 2], "dir": "E" } ] } ],
	  "props": [ { "id": "p1", "kind": "sandbags", "pos": [30, 30], "rot": 45, "size": [3, 1, 1] } ],
	  "terrain": [ { "id": "t1", "kind": "cut", "rect": [10, 90, 40, 4], "height": 1.5 } ],
	  "routes": [ { "id": "r1", "points": [[10, 10], [90, 90]] } ],
	  "markers": [ { "id": "s1", "type": "playerSpawn", "pos": [10, 10], "props": { "side": "west" } },
	               { "id": "l1", "type": "loot", "pos": [75, 75], "height": 3, "props": { "tier": "high" } } ]
	}
	""";

	[Fact]
	public void ParsesAGoodFile()
	{
		var l = MapLayout.Parse(Good);
		Assert.Equal("t", l.Id);
		Assert.Equal(new Rect2(60, 60, 60, 60), l.Zones[0].Area);
		Assert.Equal(2, l.Buildings[0].Openings.Count);
		Assert.Equal(1f, l.Buildings[0].Openings[1].Sill);
		Assert.Equal(new Vector2(30, 30), l.Props[0].Position);
		Assert.Equal("west", l.Markers[0].Prop("side"));
		Assert.Equal("", l.Markers[0].Prop("nothing"));
		Assert.Equal(180f, l.ShortSide);
	}

	[Fact]
	public void BadJsonIsReportedAsALayoutError()
		=> Assert.Throws<LayoutException>(() => MapLayout.Parse("{ not json"));

	private static LayoutException Fails(System.Func<string, string> edit)
		=> Assert.Throws<LayoutException>(() => MapLayout.Parse(edit(Good)));

	[Theory]
	[InlineData("\"sizeClass\": \"small\"", "\"sizeClass\": \"huge\"", "sizeClass")]
	[InlineData("\"type\": \"core\"", "\"type\": \"castle\"", "zone type")]
	[InlineData("[70, 70, 20, 10]", "[170, 70, 20, 10]", "outside the map")]
	[InlineData("\"offset\": 2", "\"offset\": 19.5", "runs past")]
	[InlineData("\"kind\": \"cut\"", "\"kind\": \"hole\"", "cut or mound")]
	[InlineData("\"type\": \"playerSpawn\"", "\"type\": \"spawnn\"", "marker type")]
	[InlineData("\"tier\": \"high\"", "\"tier\": \"epic\"", "loot marker needs tier")]
	[InlineData("[[10, 10], [90, 90]]", "[[10, 10]]", "at least 2 points")]
	[InlineData("[80, 70, 80, 80]", "[80, 70]", "interior wall")]
	public void StructuralMistakesAreReported(string from, string to, string expected)
		=> Assert.Contains(Fails(s => s.Replace(from, to)).Problems, m => m.Contains(expected));

	[Fact]
	public void MultiFloorBuildingsNeedStairsInsideTheFootprint()
	{
		Assert.Contains(Fails(s => s.Replace("\"stairs\": [ { \"rect\": [72, 72, 4, 2], \"dir\": \"E\" } ]", "\"stairs\": []")).Problems, m => m.Contains("no stairs"));
		Assert.Contains(Fails(s => s.Replace("[72, 72, 4, 2]", "[60, 72, 4, 2]")).Problems, m => m.Contains("stair outside"));
		Assert.Contains(Fails(s => s.Replace("\"dir\": \"E\"", "\"dir\": \"Q\"")).Problems, m => m.Contains("stair dir"));
		Assert.Contains(Fails(s => s.Replace("\"offset\": 2", "\"offset\": 2, \"floor\": 5")).Problems, m => m.Contains("floor 5"));
	}

	[Fact]
	public void DuplicateIdsAcrossKindsAreReported()
		=> Assert.Contains(Fails(s => s.Replace("\"id\": \"p1\"", "\"id\": \"b1\"")).Problems, m => m.Contains("duplicate id 'b1'"));

	[Fact]
	public void EveryProblemIsListedAtOnce()
	{
		var e = Fails(s => s.Replace("\"huge\"", "x").Replace("\"core\"", "\"castle\"").Replace("\"sizeClass\": \"small\"", "\"sizeClass\": \"huge\"").Replace("\"cut\"", "\"hole\""));
		Assert.True(e.Problems.Count >= 3);
	}

	[Fact]
	public void SizeCheckStopsEarlyWhenTheMapHasNoUsableSize()
	{
		var e = Assert.Throws<LayoutException>(() => MapLayout.Parse("{\"id\":\"t\"}"));
		Assert.Single(e.Problems);
	}

	[Fact]
	public void SegmentRectTestHandlesHitsMissesAndParallelLines()
	{
		var r = new Rect2(10, 10, 10, 10);
		Assert.True(GeometryQueries.SegmentHitsRect(new Vector2(0, 15), new Vector2(30, 15), r));
		Assert.False(GeometryQueries.SegmentHitsRect(new Vector2(0, 5), new Vector2(30, 5), r));
		Assert.False(GeometryQueries.SegmentHitsRect(new Vector2(0, 0), new Vector2(8, 30), r));
		Assert.True(GeometryQueries.SegmentHitsRect(new Vector2(12, 12), new Vector2(14, 14), r));
		Assert.True(GeometryQueries.SegmentHitsRect(new Vector2(0, 0), new Vector2(30, 30), r));
	}

	[Fact]
	public void GeometryQueriesBlockSightWithBuildingsAndTallProps()
	{
		var l = MapLayout.Parse(Good);
		var q = new GeometryQueries(l);
		Assert.False(q.LineOfSight(new Vector2(10, 75), new Vector2(150, 75)));
		Assert.True(q.LineOfSight(new Vector2(10, 150), new Vector2(150, 150)));
		Assert.Equal(130f, q.PathLength(new Vector2(0, 0), new Vector2(100, 0)));
	}
}
