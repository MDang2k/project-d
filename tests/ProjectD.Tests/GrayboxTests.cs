using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class GrayboxTests
{
	private static MapLayout Map(string body = "")
		=> MapLayout.Parse("{ \"id\": \"t\", \"size\": [100, 80], \"sizeClass\": \"small\", \"raidMinutes\": 20," + (body == "" ? "\"zones\": []" : body) + "}");

	private static Graybox Build(string body = "") => GrayboxBuilder.Build(Map(body));

	private const string House = "\"buildings\": [ { \"id\": \"house\", \"rect\": [20, 20, 10, 8], \"floors\": 2, \"floorHeight\": 3,"
		+ " \"openings\": [ { \"wall\": \"S\", \"kind\": \"door\", \"offset\": 4, \"width\": 1.2, \"height\": 2.2 },"
		+ " { \"wall\": \"N\", \"kind\": \"window\", \"offset\": 2, \"width\": 1, \"height\": 1, \"sill\": 1 } ],"
		+ " \"stairs\": [ { \"rect\": [21, 21, 3, 1.5], \"dir\": \"E\" } ] } ]";

	[Fact]
	public void PlainGroundIsOneSlabCentredOnTheOrigin()
	{
		var g = Build();
		var ground = Assert.Single(g.Boxes);
		Assert.Equal(new Vector3(100, 1, 80), ground.Size);
		Assert.Equal(new Vector3(0, -0.5f, 0), ground.Center);
	}

	[Fact]
	public void ATrenchCutsAHoleInTheGroundAndGetsAFloor()
	{
		var g = Build("\"terrain\": [ { \"id\": \"trench\", \"kind\": \"cut\", \"rect\": [40, 30, 20, 4], \"height\": 1.5 } ]");
		var ground = g.Boxes.Where(b => b.Group == "Ground").ToList();
		Assert.Equal(4, ground.Count); // rows above and below the hole, then left and right of it
		Assert.Equal(100f * 80f - 20f * 4f, ground.Sum(b => b.Size.X * b.Size.Z), 2);
		Assert.All(ground, b => Assert.Equal(2.5f, b.Size.Y)); // thick enough for the deepest cut
		var floor = Assert.Single(g.Boxes, b => b.Name == "trench");
		Assert.Equal(-1.5f - 0.5f, floor.Center.Y);
	}

	[Fact]
	public void AMoundIsABoxOnTheGround()
	{
		var b = Assert.Single(Build("\"terrain\": [ { \"id\": \"hill\", \"kind\": \"mound\", \"rect\": [10, 10, 10, 10], \"height\": 4 } ]").Boxes, x => x.Name == "hill");
		Assert.Equal(new Vector3(10, 4, 10), b.Size);
		Assert.Equal(new Vector3(-35, 2, -25), b.Center);
	}

	[Fact]
	public void PropsKeepPositionRotationAndSize()
	{
		var p = Build("\"props\": [ { \"id\": \"w1\", \"kind\": \"lowWall\", \"pos\": [50, 40], \"rot\": 30, \"size\": [4, 0.5, 1] } ]").Boxes.Single(b => b.Name == "w1");
		Assert.Equal(new Vector3(0, 0.5f, 0), p.Center);
		Assert.Equal(new Vector3(4, 1, 0.5f), p.Size);
		Assert.Equal(30f, p.RotYDeg);
		Assert.Equal("cover", p.Material);
	}

	[Fact]
	public void NonEnterableBuildingsAreSolidBlocks()
	{
		var g = Build("\"buildings\": [ { \"id\": \"shed\", \"rect\": [10, 10, 6, 6], \"floors\": 1, \"enterable\": false } ]");
		var b = Assert.Single(g.Boxes, x => x.Group == "Buildings");
		Assert.Equal(new Vector3(6, 3, 6), b.Size);
	}

	[Fact]
	public void WallsLeaveAGapForADoorWithALintelAbove()
	{
		var g = Build(House);
		var south = g.Boxes.Where(b => b.Name.StartsWith("house_f0_S_")).ToList();
		Assert.Equal(3, south.Count); // left piece, lintel, right piece (a door has no sill piece)
		var lintel = south.Single(b => b.Size.Y < 1f);
		Assert.Equal(3f - 2.2f, lintel.Size.Y, 3);
		Assert.Equal(1.2f, lintel.Size.X, 3);
		var north = g.Boxes.Where(b => b.Name.StartsWith("house_f0_N_")).ToList();
		Assert.Equal(4, north.Count); // left, under the sill, above the window, right
	}

	[Fact]
	public void UpperFloorsGetASlabWithTheStairHoleAndSteps()
	{
		var g = Build(House);
		var slabs = g.Boxes.Where(b => b.Name.StartsWith("house_slab1_")).ToList();
		Assert.Equal(10f * 8f - 3f * 1.5f, slabs.Sum(b => b.Size.X * b.Size.Z), 2);
		Assert.All(slabs, b => Assert.Equal(3f - 0.1f, b.Center.Y, 3));
		var steps = g.Boxes.Where(b => b.Name.StartsWith("house_stair0_0_")).ToList();
		Assert.Equal(15, steps.Count);
		Assert.Equal(3f, steps.Max(b => b.Center.Y + b.Size.Y / 2f), 3);
		Assert.True(steps.Zip(steps.Skip(1), (a, b) => b.Size.Y > a.Size.Y && b.Center.X > a.Center.X).All(x => x)); // climbs east
		Assert.DoesNotContain(g.Boxes, b => b.Name.StartsWith("house_stair1_"));
		Assert.Contains(g.Boxes, b => b.Name == "house_roof" && b.Center.Y > 6f);
	}

	[Fact]
	public void MarkersFaceTheMapCentreUnlessToldOtherwise()
	{
		var g = Build("\"markers\": [ { \"id\": \"s-1\", \"type\": \"playerSpawn\", \"pos\": [10, 40] }, { \"id\": \"s2\", \"type\": \"playerSpawn\", \"pos\": [50, 5], \"props\": { \"facing\": \"90\" } } ]");
		var west = g.Markers[0];
		Assert.Equal("s_1", west.Name);
		Assert.Equal(new Vector3(-40, 0.1f, 0), west.Position);
		Assert.Equal(-90f, west.YawDeg, 2); // faces east: forward (-Z) turned to +X
		Assert.Equal(90f, g.Markers[1].YawDeg);
	}

	[Fact]
	public void ZoneAndDoorBoxesStayOnTheAxesWhileSpawnsAndArrivalsFaceTheCentre()
	{
		var g = Build("\"markers\": [ { \"id\": \"d\", \"type\": \"lock\", \"pos\": [10, 40], \"props\": { \"key\": \"k\" } },"
			+ " { \"id\": \"z\", \"type\": \"extract\", \"pos\": [10, 40] }, { \"id\": \"a\", \"type\": \"arrival\", \"pos\": [10, 40] } ]");
		Assert.Equal(0f, g.Markers.Single(m => m.Name == "d").YawDeg);
		Assert.Equal(0f, g.Markers.Single(m => m.Name == "z").YawDeg);
		Assert.Equal(-90f, g.Markers.Single(m => m.Name == "a").YawDeg, 2);
	}

	[Fact]
	public void ALockBecomesASolidDoorBodyWithItsKeyAndTitle()
	{
		var text = GrayboxTscn.Write(Build("\"markers\": [ { \"id\": \"door1\", \"type\": \"lock\", \"pos\": [30, 40],"
			+ " \"props\": { \"key\": \"radio_room_key\", \"title\": \"radio_room\", \"size\": \"0.3,2.6,3\" } } ]"));
		Assert.Contains("[node name=\"door1\" type=\"StaticBody3D\" parent=\"Locks\"]", text);
		Assert.Contains("KeyId = \"radio_room_key\"", text);
		Assert.Contains("TitleKey = \"lock.radio_room\"", text);
		Assert.Contains("OpenLift = 2.6", text);
		Assert.Contains("size = Vector3(0.3, 2.6, 3)", text);
		Assert.Contains("Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, -20, 1.3, 0)", text);
	}

	[Fact]
	public void BuildingTheSameLayoutTwiceGivesIdenticalOutputWithUniqueNames()
	{
		var layout = Map(House + ", \"markers\": [ { \"id\": \"a\", \"type\": \"loot\", \"pos\": [25, 25], \"props\": { \"tier\": \"high\" } } ]");
		Assert.Equal(GrayboxTscn.Write(GrayboxBuilder.Build(layout)), GrayboxTscn.Write(GrayboxBuilder.Build(layout)));
		var boxes = GrayboxBuilder.Build(layout).Boxes;
		Assert.Equal(boxes.Count, boxes.Select(b => b.Name).Distinct().Count());
	}

	[Fact]
	public void SceneTextHasTheMarkerScriptsAndProperties()
	{
		var text = GrayboxTscn.Write(Build(
			"\"markers\": ["
			+ " { \"id\": \"sp\", \"type\": \"playerSpawn\", \"pos\": [10, 40], \"props\": { \"side\": \"west\" } },"
			+ " { \"id\": \"ai1\", \"type\": \"aiSpawn\", \"pos\": [30, 40], \"props\": { \"preset\": \"looter\" } },"
			+ " { \"id\": \"ex1\", \"type\": \"extract\", \"pos\": [90, 40], \"props\": { \"kind\": \"paid\", \"cost\": \"5000\", \"size\": \"8,3,8\" } },"
			+ " { \"id\": \"ex2\", \"type\": \"extract\", \"pos\": [80, 40], \"props\": { \"kind\": \"item\", \"item\": \"flare\" } },"
			+ " { \"id\": \"lo\", \"type\": \"loot\", \"pos\": [50, 40], \"props\": { \"tier\": \"high\" } },"
			+ " { \"id\": \"wv\", \"type\": \"waveSpawn\", \"pos\": [70, 40], \"props\": { \"wave\": \"bandit\", \"atMin\": \"8,16\", \"preset\": \"bandit\", \"count\": \"4\" } },"
			+ " { \"id\": \"mf\", \"type\": \"minefield\", \"pos\": [30, 40], \"props\": { \"size\": \"38,2,80\" } },"
			+ " { \"id\": \"sl\", \"type\": \"safeLane\", \"pos\": [30, 40], \"props\": { \"lane\": \"north\", \"order\": \"2\" } },"
			+ " { \"id\": \"lm\", \"type\": \"landmark\", \"pos\": [60, 40] } ]"));
		Assert.Contains("[node name=\"wv\" type=\"Marker3D\" parent=\"WaveSpawns\"]", text);
		Assert.Contains("[node name=\"mf\" type=\"Node3D\" parent=\"Markers\"]", text);
		Assert.Contains("Size = Vector3(38, 2, 80)", text);
		Assert.Contains("Lane = \"north\"", text);
		Assert.Contains("Order = 2", text);
		Assert.Contains("AtMinutes = \"8,16\"", text);
		Assert.Contains("Count = 4", text);
		Assert.Contains("[node name=\"sp\" type=\"Marker3D\" parent=\"Spawns\"]", text);
		Assert.Contains("metadata/side = \"west\"", text);
		Assert.Contains("PresetId = \"looter\"", text);
		Assert.Contains("Kind = 2", text);
		Assert.Contains("Cost = 5000", text);
		Assert.Contains("Size = Vector3(8, 3, 8)", text);
		Assert.Contains("RequiredItemId = \"flare\"", text);
		Assert.Contains("Tier = 2", text);
		Assert.Contains("[node name=\"lm\" type=\"Marker3D\" parent=\"Markers\"]", text);
		Assert.Contains("metadata/layout_type = \"landmark\"", text);
	}

	[Fact]
	public void SceneTextSharesShapesMeshesAndMaterials()
	{
		var text = GrayboxTscn.Write(Build("\"props\": [ { \"id\": \"a\", \"kind\": \"lowWall\", \"pos\": [10, 10], \"size\": [2, 1, 1] }, { \"id\": \"b\", \"kind\": \"lowWall\", \"pos\": [20, 10], \"size\": [2, 1, 1] } ]"));
		Assert.Equal(2, text.Split("type=\"BoxShape3D\"").Length - 1); // ground, then one shape for both walls
		Assert.Equal(2, text.Split("type=\"BoxMesh\"").Length - 1);
		Assert.Equal(2, text.Split("type=\"StandardMaterial3D\"").Length - 1);
		Assert.Contains("[node name=\"Geometry\" type=\"NavigationRegion3D\" parent=\".\"]", text);
		Assert.StartsWith("[gd_scene", text);
	}

	[Fact]
	public void KnowledgeSpotsCodeLocksAndHiddenCachesCarryTheirKnowledgeLinks()
	{
		var text = GrayboxTscn.Write(Build(
			"\"markers\": ["
			+ " { \"id\": \"ks\", \"type\": \"knowledgeSpot\", \"pos\": [10, 40], \"props\": { \"item\": \"tower_cache_note\" } },"
			+ " { \"id\": \"cache\", \"type\": \"loot\", \"pos\": [20, 40], \"props\": { \"tier\": \"mid\", \"requires\": \"stash.tower_cache\" } },"
			+ " { \"id\": \"plain\", \"type\": \"loot\", \"pos\": [30, 40], \"props\": { \"tier\": \"low\" } },"
			+ " { \"id\": \"door\", \"type\": \"lock\", \"pos\": [40, 40], \"props\": { \"key\": \"k\", \"code\": \"lock.bunker_side_room\" } } ]"));
		Assert.Contains("[node name=\"ks\" type=\"Marker3D\" parent=\"Markers\"]", text);
		Assert.Contains("ItemId = \"tower_cache_note\"", text);
		Assert.Contains("RequiresKnowledgeId = \"stash.tower_cache\"", text);
		Assert.Equal(1, text.Split("RequiresKnowledgeId").Length - 1); // only the cache, not the plain loot
		Assert.Contains("CodeKnowledgeId = \"lock.bunker_side_room\"", text);
	}
}
