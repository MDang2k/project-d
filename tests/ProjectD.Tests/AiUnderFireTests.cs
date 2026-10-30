using System;
using Xunit;

namespace ProjectD.Tests;

public class AiUnderFireTests
{
	private static readonly UnderFireTuning Tune = new(0.15f, 0.30f, 8f, 3f, 0.8f);
	private static AiUnderFire Make() => new(Tune, new Random(3));

	private static void Run(AiUnderFire f, float seconds, Func<float?> cover, bool arrived = false)
	{
		for (var t = 0f; t < seconds; t += 0.05f)
			f.Update(0.05f, cover, arrived);
	}

	[Fact]
	public void ReactsWithinTheConfiguredDelay()
	{
		var f = Make();
		f.Trigger();
		Assert.Equal(UnderFirePhase.Reacting, f.Phase);
		Assert.True(f.BlocksFire);
		Run(f, 0.14f, () => 3f);
		Assert.Equal(UnderFirePhase.Reacting, f.Phase);
		Run(f, 0.2f, () => 3f);
		Assert.Equal(UnderFirePhase.TakingCover, f.Phase);
		Assert.True(f.Sprints);
	}

	[Fact]
	public void CoverOutOfReachMeansProne()
	{
		var f = Make();
		f.Trigger();
		Run(f, 0.4f, () => 12f);
		Assert.Equal(UnderFirePhase.Prone, f.Phase);
		Assert.True(f.IsProne);
		Run(f, 0.4f, () => 12f);
	}

	[Fact]
	public void NoCoverAtAllMeansProne()
	{
		var f = Make();
		f.Trigger();
		Run(f, 0.4f, () => null);
		Assert.Equal(UnderFirePhase.Prone, f.Phase);
	}

	[Fact]
	public void ArrivingAtCoverEndsTheResponse()
	{
		var f = Make();
		f.Trigger();
		Run(f, 0.4f, () => 3f);
		f.Update(0.05f, () => 3f, arrived: true);
		Assert.Equal(UnderFirePhase.None, f.Phase);
	}

	[Fact]
	public void ProneIsHeldThenLeftThroughAGetUp()
	{
		var f = Make();
		f.Trigger();
		Run(f, 0.4f, () => null);
		Run(f, 2.5f, () => null);
		Assert.Equal(UnderFirePhase.Prone, f.Phase);
		Run(f, 0.7f, () => null);
		Assert.Equal(UnderFirePhase.GettingUp, f.Phase);
		Assert.True(f.BlocksFire && f.IsProne);
		Run(f, 0.9f, () => null);
		Assert.Equal(UnderFirePhase.None, f.Phase);
	}

	[Fact]
	public void MoreFireWhileProneKeepsItDown()
	{
		var f = Make();
		f.Trigger();
		Run(f, 0.4f, () => null);
		Run(f, 2.5f, () => null);
		f.Trigger();
		Run(f, 2.5f, () => null);
		Assert.Equal(UnderFirePhase.Prone, f.Phase);
	}

	[Fact]
	public void FireWhileRunningToCoverDoesNotRestartIt()
	{
		var f = Make();
		f.Trigger();
		Run(f, 0.4f, () => 3f);
		f.Trigger();
		Assert.Equal(UnderFirePhase.TakingCover, f.Phase);
	}

	[Fact]
	public void CoverIsOnlyAskedOnceWhenTheDelayEnds()
	{
		var f = Make();
		var asked = 0;
		f.Trigger();
		Run(f, 1f, () => { asked++; return 3f; });
		Assert.Equal(1, asked);
	}
}
