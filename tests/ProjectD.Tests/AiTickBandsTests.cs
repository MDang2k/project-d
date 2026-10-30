using Xunit;

namespace ProjectD.Tests;

public class AiTickBandsTests
{
	private static readonly AiBandTuning T = new();

	[Theory]
	[InlineData(10f, AiBand.Combat)]
	[InlineData(40f, AiBand.Combat)]
	[InlineData(41f, AiBand.Awareness)]
	[InlineData(120f, AiBand.Awareness)]
	[InlineData(121f, AiBand.Dormant)]
	[InlineData(500f, AiBand.Dormant)]
	public void DistanceDecidesTheBand(float distance, AiBand expected)
		=> Assert.Equal(expected, AiTickBands.Classify(T, distance, false, false));

	[Fact]
	public void AFightAnywhereKeepsABotAtLeastInTheAwarenessBand()
	{
		Assert.Equal(AiBand.Awareness, AiTickBands.Classify(T, 150f, true, false));
		Assert.Equal(AiBand.Awareness, AiTickBands.Classify(T, 400f, false, true));
		Assert.Equal(AiBand.Combat, AiTickBands.Classify(T, 20f, true, true));
	}

	[Fact]
	public void FarBandsDoLessWork()
	{
		Assert.Equal(0f, AiTickBands.ThinkIntervalS(T, AiBand.Combat));
		Assert.True(AiTickBands.ThinkIntervalS(T, AiBand.Dormant) > AiTickBands.ThinkIntervalS(T, AiBand.Awareness));
		Assert.True(AiTickBands.VisionPoints(AiBand.Combat) > AiTickBands.VisionPoints(AiBand.Awareness));
		Assert.Equal(0, AiTickBands.VisionPoints(AiBand.Dormant));
		Assert.Equal(32, AiTickBands.CoverRays(T, AiBand.Combat));
		Assert.Equal(8, AiTickBands.CoverRays(T, AiBand.Awareness));
		Assert.Equal(0, AiTickBands.CoverRays(T, AiBand.Dormant));
	}

	[Fact]
	public void TheSweepBudgetCapsSweepsPerFrameAndResetsNextFrame()
	{
		AiPerf.Reset();
		var granted = 0;
		for (var i = 0; i < 10; i++)
			if (AiPerf.TryBeginSweep(1, T))
				granted++;
		Assert.Equal(T.MaxSweepsPerFrame, granted);
		Assert.True(AiPerf.TryBeginSweep(2, T));
	}

	[Fact]
	public void CountersReportTheLastFullFrame()
	{
		AiPerf.Reset();
		AiPerf.Ray(1, 40);
		AiPerf.Ray(1, 2);
		AiPerf.AddMs(1, 0.8);
		AiPerf.Ray(2);
		Assert.Equal(42, AiPerf.RaysLastFrame);
		Assert.Equal(0.8, AiPerf.MsLastFrame, 3);
		Assert.Equal(42, AiPerf.PeakRays);
	}
}
