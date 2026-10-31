using System;
using Xunit;

namespace ProjectD.Tests;

public class CrawlModelTests
{
	private static void Run(CrawlModel m, float seconds, bool prone, float speed)
	{
		for (var t = 0f; t < seconds - 1e-4f; t += 0.01f)
			m.Update(0.01f, prone, speed);
	}

	[Fact]
	public void CrawlingPutsTheWeaponInOneHandWithoutADS()
	{
		var m = new CrawlModel();
		Run(m, 0.3f, true, 0.7f);
		Assert.True(m.Moving);
		Assert.Equal(1f, m.OneHanded);
		Assert.True(m.BlocksAds);
		Assert.Equal(3f, m.HipSpreadMult, 3);
		Assert.Equal(1.5f, m.RecoilMult, 3);
	}

	[Fact]
	public void StoppingBringsTheSecondHandBackOverAboutFourTenths()
	{
		var m = new CrawlModel();
		Run(m, 0.5f, true, 0.7f);
		Run(m, 0.3f, true, 0f);
		Assert.True(m.BlocksAds);
		Run(m, 0.2f, true, 0f);
		Assert.False(m.BlocksAds);
		Assert.Equal(1f, m.HipSpreadMult, 3);
	}

	[Fact]
	public void LyingStillThereIsNothingToBlock()
	{
		var m = new CrawlModel();
		Run(m, 1f, true, 0f);
		Assert.False(m.BlocksAds);
		Assert.False(m.Moving);
		Assert.Equal(0f, m.RollDeg);
	}

	[Fact]
	public void TheCameraRollsInStepWithTheStrideAndBothWays()
	{
		var m = new CrawlModel();
		float min = 0f, max = 0f;
		for (var i = 0; i < 400; i++)
		{
			m.Update(0.01f, true, 0.7f);
			min = Math.Min(min, m.RollDeg);
			max = Math.Max(max, m.RollDeg);
		}
		Assert.InRange(max, 3f, 3.51f);
		Assert.InRange(min, -3.51f, -3f);
	}

	[Fact]
	public void ADoubleStrideSpeedRollsTwiceAsOften()
	{
		int Crossings(float speed)
		{
			var m = new CrawlModel();
			var prev = 0f;
			var n = 0;
			for (var i = 0; i < 1000; i++)
			{
				m.Update(0.01f, true, speed);
				if (prev < 0f && m.RollDeg >= 0f)
					n++;
				prev = m.RollDeg;
			}
			return n;
		}
		Assert.InRange(Crossings(1.2f), Crossings(0.6f) * 2 - 1, Crossings(0.6f) * 2 + 1);
	}

	[Fact]
	public void StandingUpClearsEverythingAtOnce()
	{
		var m = new CrawlModel();
		Run(m, 0.5f, true, 0.7f);
		m.Update(0.01f, false, 0f);
		Assert.Equal(0f, m.OneHanded);
		Assert.False(m.BlocksAds);
	}
}

public class ProneLeanTests
{
	private static readonly ProneLeanTuning T = new();

	[Fact]
	public void TheLeanRollsTheCameraAndShiftsItToThatSide()
	{
		Assert.Equal(40f, ProneLean.CameraRollDeg(T, 1f));
		Assert.Equal(-40f, ProneLean.CameraRollDeg(T, -1f));
		Assert.Equal(0.25f, ProneLean.ShiftM(T, 1f));
		Assert.Equal(0f, ProneLean.ShiftM(T, 0f));
		Assert.Equal(20f, ProneLean.CameraRollDeg(T, 0.5f));
	}

	[Fact]
	public void TheWeaponCantsFurtherThanTheCameraUpToNearHorizontal()
	{
		Assert.Equal(40f + 35f, ProneLean.CameraRollDeg(T, 1f) + ProneLean.ExtraCantDeg(T, 1f));
		Assert.Equal(-(40f + 35f), ProneLean.CameraRollDeg(T, -1f) + ProneLean.ExtraCantDeg(T, -1f));
		Assert.Equal(0f, ProneLean.ExtraCantDeg(T, 0f));
	}

	[Fact]
	public void ALeanBlocksADSPastASmallAmount()
	{
		Assert.False(ProneLean.BlocksAds(T, 0.1f));
		Assert.True(ProneLean.BlocksAds(T, 0.2f));
		Assert.True(ProneLean.BlocksAds(T, -0.9f));
	}

	[Fact]
	public void InputsBeyondTheRangeAreClamped()
		=> Assert.Equal(40f, ProneLean.CameraRollDeg(T, 3f));
}
