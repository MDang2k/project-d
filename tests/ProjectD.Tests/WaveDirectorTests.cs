using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class WaveDirectorTests
{
	private static readonly WaveTuning T = new();

	private static WaveEntry Patrol(string id, float atS = 480f) => new(id, "patrol", atS, "allied_pmc", 3, "west");
	private static WaveEntry Bandit(string id, float atS) => new(id, "bandit", atS, "bandit", 4);

	private static (WaveDirector D, List<string> Log) Make(params WaveEntry[] entries)
	{
		var d = new WaveDirector(entries, T);
		var log = new List<string>();
		d.Telegraphed += e => log.Add($"tele {e.PointId}@{e.AtS}");
		d.Arrived += e => log.Add($"arrive {e.PointId}@{e.AtS}");
		return (d, log);
	}

	[Fact]
	public void TelegraphsAFixedLeadBeforeArrivalThenArrives()
	{
		var (d, log) = Make(Patrol("a"));
		d.Update(100f);
		Assert.Empty(log);
		d.Update(459f);
		Assert.Empty(log);
		d.Update(460f);
		Assert.Equal(new[] { "tele a@480" }, log);
		d.Update(479f);
		Assert.Single(log);
		d.Update(480f);
		Assert.Equal(new[] { "tele a@480", "arrive a@480" }, log);
	}

	[Fact]
	public void BanditsGiveLessWarningThanPatrols()
	{
		var (d, log) = Make(Bandit("b", 480f));
		d.Update(464f);
		Assert.Empty(log);
		d.Update(465f);
		Assert.Single(log);
	}

	[Fact]
	public void AClockSkipFiresBothEventsOnceInOrder()
	{
		var (d, log) = Make(Patrol("a"));
		d.Update(5000f);
		d.Update(5001f);
		d.Update(9000f);
		Assert.Equal(new[] { "tele a@480", "arrive a@480" }, log);
		Assert.True(d.Finished);
	}

	[Fact]
	public void NothingRepeatsWhenTheClockGoesBackwards()
	{
		var (d, log) = Make(Patrol("a"));
		d.Update(480f);
		d.Update(10f);
		d.Update(480f);
		Assert.Equal(2, log.Count);
	}

	[Fact]
	public void WavesFireInTimeOrder()
	{
		var (d, log) = Make(Patrol("late", 960f), Patrol("early", 480f));
		d.Update(2000f);
		Assert.Equal(new[] { "tele early@480", "arrive early@480", "tele late@960", "arrive late@960" }, log);
	}

	[Fact]
	public void NoEntriesIsFinishedAndQuiet()
	{
		var (d, log) = Make();
		d.Update(1000f);
		Assert.Empty(log);
		Assert.True(d.Finished);
	}

	[Fact]
	public void TuningPicksTheLeadByKind()
	{
		var t = new WaveTuning(PatrolLeadS: 30f, BanditLeadS: 10f);
		Assert.Equal(30f, t.LeadS("patrol"));
		Assert.Equal(10f, t.LeadS("bandit"));
	}

	[Fact]
	public void BanditWavesTakeTurnsBetweenTheMarkers()
	{
		var entries = new[] { "north", "south" }.SelectMany(p => new[] { 480f, 960f, 1440f }.Select(t => Bandit(p, t)));
		var chosen = WaveDirector.Alternate(entries).OrderBy(e => e.AtS).Select(e => e.PointId).ToList();
		Assert.Equal(new[] { "north", "south", "north" }, chosen);
	}

	[Fact]
	public void PatrolWavesUseEveryMarker()
	{
		var chosen = WaveDirector.Alternate(new[] { Patrol("west"), Patrol("east") }).Select(e => e.PointId).ToList();
		Assert.Equal(2, chosen.Count);
	}

	[Fact]
	public void ASingleBanditMarkerFiresEveryTime()
	{
		var chosen = WaveDirector.Alternate(new[] { Bandit("only", 480f), Bandit("only", 960f) }).ToList();
		Assert.Equal(2, chosen.Count);
	}

	[Theory]
	[InlineData("8", new[] { 480f })]
	[InlineData("8,16,24", new[] { 480f, 960f, 1440f })]
	[InlineData(" 8 , x, 16 ,", new[] { 480f, 960f })]
	[InlineData("", new float[0])]
	[InlineData("0.5", new[] { 30f })]
	public void MinutesParseToRaidSeconds(string text, float[] expected)
		=> Assert.Equal(expected, WaveDirector.ParseMinutes(text));
}

public class WavePlannerTests
{
	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(5)]
	public void OffsetsSpreadAGroupWithoutStacking(int count)
	{
		var o = WavePlanner.Offsets(count);
		Assert.Equal(count, o.Count);
		Assert.Equal(Vector3.Zero, o[0]);
		for (var i = 0; i < count; i++)
			for (var j = i + 1; j < count; j++)
				Assert.True(o[i].DistanceTo(o[j]) > 1.5f, $"{i} and {j} too close");
		Assert.Equal(o, WavePlanner.Offsets(count));
	}

	[Fact]
	public void NoAiNoOffsets() => Assert.Empty(WavePlanner.Offsets(0));

	[Fact]
	public void NearestChainWalksNearestFirstAndStopsAtTheLimit()
	{
		var pts = new[] { new Vector3(30, 0, 0), new Vector3(10, 0, 0), new Vector3(20, 0, 0), new Vector3(100, 0, 0) };
		var chain = WavePlanner.NearestChain(Vector3.Zero, pts, 3);
		Assert.Equal(new[] { new Vector3(10, 0, 0), new Vector3(20, 0, 0), new Vector3(30, 0, 0) }, chain);
		Assert.Empty(WavePlanner.NearestChain(Vector3.Zero, new Vector3[0], 3));
	}

	[Fact]
	public void LoopSitsOnTheRadius()
	{
		var loop = WavePlanner.Loop(new Vector3(5, 0, 5), 15f);
		Assert.Equal(4, loop.Count);
		Assert.All(loop, p => Assert.Equal(15f, p.DistanceTo(new Vector3(5, 0, 5)), 3));
	}

	[Fact]
	public void AWaveWaitsForTheFarPlayerButNotForever()
	{
		var t = new WaveTuning();
		Assert.False(WavePlanner.MayAppear(t, 42f, 0f));
		Assert.True(WavePlanner.MayAppear(t, 60f, 0f));
		Assert.False(WavePlanner.MayAppear(t, 10f, 59f));
		Assert.True(WavePlanner.MayAppear(t, 10f, 60f));
	}
}
