using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class MapValidatorTests
{
	/// <summary>Scripted answers so each test breaks exactly one rule.</summary>
	private sealed class Fake : IMapQueries
	{
		public Func<Vector2, Vector2, bool> Los = (_, b) => b.Y == 100f; // only the landmarks (y 100) are in sight
		public Func<Vector2, Vector2, float> Path = (a, b) => a.DistanceTo(b);
		public bool LineOfSight(Vector2 from, Vector2 to) => Los(from, to);
		public float PathLength(Vector2 from, Vector2 to) => Path(from, to);
	}

	private static LayoutMarker M(string id, string type, float x, float y, params (string, string)[] props)
		=> new() { Id = id, Type = type, Pos = new[] { x, y }, Props = props.ToDictionary(p => p.Item1, p => p.Item2) };

	/// <summary>A 180 x 180 map that passes every rule with the fake queries.</summary>
	private static MapLayout Valid()
	{
		var l = new MapLayout { Id = "t", Size = new[] { 180f, 180f }, SizeClass = "small", RaidMinutes = 20 };
		float[][] spawns = { new[] { 10f, 10f }, new[] { 90f, 5f }, new[] { 170f, 10f }, new[] { 10f, 170f }, new[] { 170f, 170f } };
		for (var i = 0; i < spawns.Length; i++)
			l.Markers.Add(M($"s{i}", "playerSpawn", spawns[i][0], spawns[i][1], ("side", i % 2 == 0 ? "west" : "east")));
		l.Markers.Add(M("a1", "aiSpawn", 90, 90));
		l.Markers.Add(M("e1", "extract", 90, 175, ("kind", "open"), ("side", "west")));
		l.Markers.Add(M("e2", "extract", 175, 90, ("kind", "open"), ("side", "east")));
		l.Markers.Add(M("e3", "extract", 5, 90, ("kind", "item")));
		for (var i = 0; i < 4; i++) l.Markers.Add(M($"lo{i}", "loot", 20 + i, 50, ("tier", "low")));
		for (var i = 0; i < 4; i++) l.Markers.Add(M($"mi{i}", "loot", 40 + i, 60, ("tier", "mid")));
		for (var i = 0; i < 3; i++) l.Markers.Add(M($"hi{i}", "loot", 90 + i, 90, ("tier", "high")));
		for (var i = 0; i < 3; i++) l.Markers.Add(M($"lm{i}", "landmark", 60 + i, 100));
		l.Buildings.Add(new LayoutBuilding { Id = "b1", Rect = new[] { 70f, 70f, 20f, 10f }, Floors = 2 });
		l.Terrain.Add(new LayoutTerrain { Id = "trench", Kind = "cut", Rect = new[] { 10f, 90f, 40f, 4f }, Height = 1.5f });
		return l;
	}

	private static MapReport Run(MapLayout l, Fake? q = null, MapRules? r = null) => MapValidator.Validate(l, q ?? new Fake(), r);

	private static bool Has(MapReport r, string rule, string text = "") => r.Issues.Any(i => i.Rule == rule && i.Message.Contains(text));

	[Fact]
	public void AValidLayoutHasNoIssues()
	{
		var r = Run(Valid());
		Assert.True(r.Ok, string.Join("\n", r.Issues.Select(i => i.Message)));
		Assert.Equal(1, r.Counts["buildings"]);
	}

	[Fact]
	public void TooFewOrTooManySpawnsFail()
	{
		var few = Valid(); few.Markers.RemoveAll(m => m.Id is "s3" or "s4");
		Assert.True(Has(Run(few), "spawns", "3 player spawns"));
		var many = Valid();
		for (var i = 0; i < 4; i++) many.Markers.Add(M($"x{i}", "playerSpawn", 100, 40 + i * 30));
		Assert.True(Has(Run(many), "spawns", "9 player spawns"));
	}

	[Fact]
	public void SpawnSpacingScalesWithTheShortSide()
	{
		var l = Valid();
		l.Markers.First(m => m.Id == "s1").Pos = new[] { 40f, 10f }; // 30 m from s0, limit 72 m
		Assert.True(Has(Run(l), "spawns", "apart"));
		l.Size = new[] { 60f, 180f }; // short side 60 -> limit 24 m
		Assert.False(Has(Run(l), "spawns", "apart"));
	}

	[Fact]
	public void SpawnsMustNotSeeEachOtherOrHighLoot()
	{
		var l = Valid();
		var seeSpawn = new Fake { Los = (a, b) => b.Y == 100f || (a == new Vector2(10, 10) && b == new Vector2(90, 5)) };
		Assert.True(Has(Run(l, seeSpawn), "spawns", "sees 's1'"));
		var seeLoot = new Fake { Los = (a, b) => b.Y == 100f || (a == new Vector2(10, 10) && b == new Vector2(90, 90)) };
		Assert.True(Has(Run(l, seeLoot), "spawns", "high-tier loot"));
		var midLoot = new Fake { Los = (a, b) => b.Y == 100f || (a == new Vector2(10, 10) && b == new Vector2(40, 60)) };
		Assert.False(Has(Run(l, midLoot), "spawns"));
	}

	[Fact]
	public void ExtractCountAndKindsAreChecked()
	{
		var none = Valid(); none.Markers.RemoveAll(m => m.Id == "e3");
		Assert.True(Has(Run(none), "extracts", "2 extracts"));
		Assert.True(Has(Run(none), "extracts", "no conditional"));
		var allConditional = Valid();
		foreach (var m in allConditional.OfType("extract")) m.Props["kind"] = "item";
		Assert.True(Has(Run(allConditional), "extracts", "no always-open"));
		var five = Valid();
		five.Markers.Add(M("e4", "extract", 100, 100, ("kind", "open")));
		five.Markers.Add(M("e5", "extract", 100, 120, ("kind", "open")));
		Assert.True(Has(Run(five), "extracts", "5 extracts"));
	}

	[Fact]
	public void ExtractsSharingAGroupCountOnce()
	{
		var l = Valid();
		l.Markers.Add(M("e4", "extract", 60, 90, ("kind", "paid"), ("group", "tunnel")));
		l.Markers.Add(M("e5", "extract", 120, 90, ("kind", "paid"), ("group", "tunnel")));
		l.Markers.First(m => m.Id == "e3").Props["group"] = "tunnel";
		Assert.False(Has(Run(l), "extracts", "extracts, need"));
	}

	[Fact]
	public void SpawnsNeedTwoExtractsFarEnoughByPath()
	{
		var l = Valid();
		var shortPaths = new Fake { Path = (a, b) => a == new Vector2(10, 10) ? 30f : a.DistanceTo(b) };
		Assert.True(Has(Run(l, shortPaths), "extracts", "'s0' reaches 0"));
		var oneFar = new Fake { Path = (a, b) => a == new Vector2(10, 10) && b != new Vector2(90, 175) ? float.PositiveInfinity : a.DistanceTo(b) };
		Assert.True(Has(Run(l, oneFar), "extracts", "'s0' reaches 1"));
	}

	[Fact]
	public void SpawnNeedsAnExtractOnItsOwnSide()
	{
		var l = Valid();
		l.Markers.First(m => m.Id == "e1").Props["side"] = "east";
		Assert.True(Has(Run(l), "extracts", "own side (west)"));
	}

	[Fact]
	public void LootMinimumsDependOnSizeClass()
	{
		var l = Valid();
		l.Markers.RemoveAll(m => m.Id == "hi2");
		Assert.True(Has(Run(l), "loot", "2 high-tier"));
		l.SizeClass = "medium";
		var r = Run(l);
		Assert.True(Has(r, "loot", "need 8"));
		Assert.True(Has(r, "loot", "need 6"));
	}

	[Fact]
	public void EverySpawnNeedsThreeLandmarksInSight()
	{
		var l = Valid();
		var blind = new Fake { Los = (a, b) => a != new Vector2(10, 10) && b.Y == 100f };
		Assert.True(Has(Run(l, blind), "landmarks", "'s0' sees"));
	}

	[Fact]
	public void LongOpenRoutesFailUnlessExposedByDesign()
	{
		var l = Valid();
		l.Routes.Add(new LayoutRoute { Id = "open", Points = { new[] { 10f, 130f }, new[] { 90f, 130f } } });
		Assert.True(Has(Run(l), "cover", "route 'open'"));
		l.Routes[0].Exposed = true;
		Assert.False(Has(Run(l), "cover"));
	}

	[Fact]
	public void CoverEveryFewMetresKeepsARouteLegal()
	{
		var l = Valid();
		l.Routes.Add(new LayoutRoute { Id = "r", Points = { new[] { 10f, 130f }, new[] { 90f, 130f } } });
		for (var x = 20; x < 90; x += 20)
			l.Props.Add(new LayoutProp { Id = $"w{x}", Kind = "lowWall", Pos = new float[] { x, 130 }, Size = new[] { 2f, 1f, 1f } });
		Assert.False(Has(Run(l), "cover"));
		l.Props.RemoveAll(p => p.Id is "w40" or "w60");
		Assert.True(Has(Run(l), "cover"));
	}

	[Fact]
	public void LowPropsDoNotCountAsCover()
	{
		var l = Valid();
		l.Routes.Add(new LayoutRoute { Id = "r", Points = { new[] { 10f, 130f }, new[] { 90f, 130f } } });
		for (var x = 20; x < 90; x += 20)
			l.Props.Add(new LayoutProp { Id = $"g{x}", Kind = "curb", Pos = new float[] { x, 130 }, Size = new[] { 2f, 1f, 0.2f } });
		Assert.True(Has(Run(l), "cover"));
	}

	[Fact]
	public void ThreeHeightBandsAreNeeded()
	{
		var noCut = Valid(); noCut.Terrain.Clear();
		Assert.True(Has(Run(noCut), "height", "2 height bands"));
		var flat = Valid(); flat.Buildings[0].Floors = 1;
		Assert.True(Has(Run(flat), "height"));
		flat.Props.Add(new LayoutProp { Id = "tw", Kind = "tower", Pos = new float[] { 50, 50 }, Size = new[] { 4f, 4f, 12f } });
		Assert.False(Has(Run(flat), "height"));
	}

	[Fact]
	public void HalfOfTheBuildingsMustBeEnterable()
	{
		var l = Valid();
		l.Buildings.Add(new LayoutBuilding { Id = "b2", Rect = new[] { 100f, 100f, 10f, 10f }, Enterable = false });
		l.Buildings.Add(new LayoutBuilding { Id = "b3", Rect = new[] { 120f, 100f, 10f, 10f }, Enterable = false });
		Assert.True(Has(Run(l), "buildings", "33%"));
		l.Buildings[2].Enterable = true;
		Assert.False(Has(Run(l), "buildings"));
	}

	[Fact]
	public void RequiredMarkerTypesMustExist()
	{
		var l = Valid(); l.Markers.RemoveAll(m => m.Type == "landmark");
		Assert.True(Has(Run(l), "markers", "'landmark'"));
		var quest = Valid(); quest.RequiredMarkers.Add("questZone");
		Assert.True(Has(Run(quest), "markers", "'questZone'"));
		quest.Markers.Add(M("q1", "questZone", 50, 50));
		Assert.False(Has(Run(quest), "markers"));
	}

	[Fact]
	public void LocksNeedAKeySource()
	{
		var l = Valid();
		l.Markers.Add(M("door1", "lock", 80, 75));
		Assert.True(Has(Run(l), "markers", "no key or code"));
		l.Markers.Last().Props["key"] = "radio_key";
		Assert.True(Has(Run(l), "markers", "no marker gives it"));
		l.Markers.First(m => m.Id == "hi0").Props["gives"] = "radio_key";
		Assert.False(Has(Run(l), "markers"));
	}

	[Fact]
	public void StaticBodyBudgetIsReported()
	{
		var l = Valid();
		var r = Run(l, null, new MapRules(MaxStaticBodies: 1));
		Assert.True(Has(r, "performance", "budget is 1"));
		Assert.Equal(2, r.Counts["staticBodies"]);
	}

	[Fact]
	public void DefaultGeometryQueriesRunOnARealLayout()
	{
		var r = MapValidator.Validate(Valid());
		Assert.NotEmpty(r.Issues); // open ground: spawns see each other without blockers
		Assert.True(Has(r, "spawns", "sees"));
	}
}
