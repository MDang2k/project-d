using System;
using Xunit;

namespace ProjectD.Tests;

public class ProneTurnModelTests
{
	private static float Run(ProneTurnModel m, float seconds, float view, Func<float, float> request, bool left = false, bool right = false)
	{
		var t = 0f;
		while (t < seconds - 1e-4f)
		{
			view = m.Step(0.01f, view, request(view), left, right);
			t += 0.01f;
		}
		return view;
	}

	[Fact]
	public void InsideTheConeTheViewTurnsFreelyAndThePelvisStays()
	{
		var m = new ProneTurnModel();
		var view = m.Step(0.01f, 0f, 25f, false, false);
		Assert.Equal(25f, view, 3);
		Assert.Equal(0f, m.PelvisYawDeg);
		Assert.Equal(25f, m.TwistDeg, 3);
		Assert.False(m.Dragging);
	}

	[Fact]
	public void PastTheConeTheTurnIsClampedToTheFixedRate()
	{
		var m = new ProneTurnModel();
		var view = Run(m, 1f, 0f, v => v + 90f); // the mouse asks for a huge swing every frame
		Assert.Equal(30f + 45f, view, 0);
		Assert.Equal(45f, m.PelvisYawDeg, 0);
		Assert.True(m.Dragging);
	}

	[Fact]
	public void TheRateIsTheSameForAGentleAndAViolentMouse()
	{
		float PelvisRate(float perFrame)
		{
			var m = new ProneTurnModel();
			var view = Run(m, 0.8f, 0f, v => v + perFrame); // well past the cone edge
			var before = m.PelvisYawDeg;
			Run(m, 0.5f, view, v => v + perFrame);
			return (m.PelvisYawDeg - before) / 0.5f;
		}
		Assert.Equal(45f, PelvisRate(2f), 0);
		Assert.Equal(45f, PelvisRate(500f), 0);
	}

	[Fact]
	public void TheMouseDeltaBeyondTheClampIsDroppedNotQueued()
	{
		var m = new ProneTurnModel();
		var view = Run(m, 0.5f, 0f, v => v + 200f);
		// the mouse goes still: the view must stop where it is, not go on catching up with the old requests
		var stopped = Run(m, 1f, view, v => v);
		Assert.Equal(view, stopped, 2);
	}

	[Fact]
	public void TurningBackInsideTheConeIsFreeAgain()
	{
		var m = new ProneTurnModel();
		var view = Run(m, 1f, 0f, v => v + 90f);
		var back = m.Step(0.01f, view, view - 40f, false, false);
		Assert.Equal(view - 40f, back, 2);
		Assert.Equal(m.PelvisYawDeg + 30f - 40f, back, 2);
	}

	[Fact]
	public void ABlockedPelvisStopsTheTurnAndLocksTheViewAtTheConeEdge()
	{
		var m = new ProneTurnModel();
		var view = Run(m, 1f, 0f, v => v + 90f, left: true);
		Assert.Equal(30f, view, 2);
		Assert.Equal(0f, m.PelvisYawDeg);
		Assert.True(m.Blocked);
		Assert.False(m.Dragging);
	}

	[Fact]
	public void ABlockOnTheOtherSideDoesNotStopThisTurn()
	{
		var m = new ProneTurnModel();
		var view = Run(m, 1f, 0f, v => v + 90f, right: true);
		Assert.True(view > 60f);
		Assert.False(m.Blocked);
	}

	[Fact]
	public void TurningRightMirrorsTurningLeft()
	{
		var m = new ProneTurnModel();
		var view = Run(m, 1f, 0f, v => v - 90f);
		Assert.Equal(-75f, view, 0);
		Assert.True(m.BankDeg < 0f);
		var mirror = new ProneTurnModel();
		Run(mirror, 1f, 0f, v => v + 90f);
		Assert.True(mirror.BankDeg > 0f);
	}

	[Fact]
	public void TheCameraBanksWhileDraggingAndLevelsAfterwards()
	{
		var m = new ProneTurnModel();
		var view = Run(m, 0.6f, 0f, v => v + 90f);
		Assert.InRange(m.BankDeg, 6f, 8.01f);
		Run(m, 1f, view, v => v);
		Assert.True(Math.Abs(m.BankDeg) < 0.1f);
	}

	[Fact]
	public void ViewAndPelvisAlwaysAddUpAcrossTheBackSeam()
	{
		var m = new ProneTurnModel();
		m.Reset(170f);
		var view = 170f;
		for (var i = 0; i < 300; i++)
		{
			view = m.Step(0.01f, view, view + 60f, false, false);
			Assert.Equal(PivotModel.WrapDeg(m.PelvisYawDeg + m.TwistDeg), view, 2);
			Assert.True(Math.Abs(m.TwistDeg) <= 30.01f);
		}
	}
}
