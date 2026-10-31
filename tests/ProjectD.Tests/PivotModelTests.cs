using System;
using Xunit;

namespace ProjectD.Tests;

public class PivotModelTests
{
	private static readonly (float Stand, float Crouch, float Prone) Stand = (1f, 0f, 0f);
	private static readonly (float Stand, float Crouch, float Prone) Crouch = (0f, 1f, 0f);
	private static readonly (float Stand, float Crouch, float Prone) Prone = (0f, 0f, 1f);

	private static void Run(PivotModel m, float seconds, float aim, (float, float, float) stance, float speed = 0f, bool locked = false, float? rate = null)
	{
		for (var t = 0f; t < seconds - 1e-4f; t += 0.01f)
			m.Update(0.01f, aim, stance, speed, locked, rate);
	}

	[Theory]
	[InlineData(1f, 0f, 0f, 50f)]
	[InlineData(0f, 1f, 0f, 45f)]
	[InlineData(0f, 0f, 1f, 30f)]
	[InlineData(0.5f, 0.5f, 0f, 47.5f)]
	public void TheConeNarrowsWithTheStance(float stand, float crouch, float prone, float expected)
		=> Assert.Equal(expected, PivotModel.ConeDeg(new PivotTuning(), (stand, crouch, prone)), 3);

	[Fact]
	public void InsideTheConeTheHipsStayPinnedAndOnlyTheTorsoTwists()
	{
		var m = new PivotModel();
		Run(m, 0.5f, 40f, Stand);
		Assert.Equal(0f, m.PelvisYawDeg);
		Assert.Equal(40f, m.TwistDeg, 3);
		Assert.False(m.Pivoting);
		Run(m, 0.5f, -49f, Stand);
		Assert.Equal(0f, m.PelvisYawDeg);
		Assert.False(m.Pivoting);
	}

	[Fact]
	public void PastTheConeAPivotStepSquaresThePelvisWithTheAim()
	{
		var m = new PivotModel();
		Run(m, 0.02f, 70f, Stand);
		Assert.True(m.Pivoting);
		Assert.Equal(1, m.Direction);
		Run(m, 0.6f, 70f, Stand);
		Assert.False(m.Pivoting);
		Assert.Equal(70f, m.PelvisYawDeg, 0);
		Assert.True(Math.Abs(m.TwistDeg) <= 4.5f);
	}

	[Fact]
	public void TheSumOfPelvisAndTwistIsAlwaysTheAim()
	{
		var m = new PivotModel();
		var aim = 0f;
		for (var i = 0; i < 600; i++)
		{
			aim = 120f * MathF.Sin(i * 0.02f);
			m.Update(0.01f, aim, Stand, 0f, false);
			Assert.Equal(PivotModel.WrapDeg(aim), PivotModel.WrapDeg(m.PelvisYawDeg + m.TwistDeg), 2);
		}
	}

	[Fact]
	public void ATurnToTheRightPivotsTheOtherWay()
	{
		var m = new PivotModel();
		Run(m, 0.02f, -80f, Stand);
		Assert.Equal(-1, m.Direction);
	}

	[Fact]
	public void ThePivotTakesAboutAHalfSecondPer90Degrees()
	{
		var m = new PivotModel();
		Run(m, 0.01f, 90f, Stand);
		Run(m, 0.40f, 90f, Stand);
		Assert.True(m.Pivoting);
		Run(m, 0.12f, 90f, Stand);
		Assert.False(m.Pivoting);
	}

	[Fact]
	public void TheShortWayRoundIsTaken()
	{
		var m = new PivotModel();
		m.Reset(170f);
		Run(m, 0.02f, -170f, Stand); // 20 degrees to the left through the back
		Assert.False(m.Pivoting);
		Assert.Equal(PivotModel.WrapDeg(-170f - 170f), m.TwistDeg, 2);
	}

	[Fact]
	public void MovingFollowsTheAimWithoutAPivotStep()
	{
		var m = new PivotModel();
		Run(m, 0.5f, 90f, Stand, speed: 2.5f);
		Assert.False(m.Pivoting);
		Assert.Equal(90f, m.PelvisYawDeg, 1);
		Assert.Equal(0f, m.TwistDeg, 1);
	}

	[Fact]
	public void MovingFollowsAtTheCappedRate()
	{
		var m = new PivotModel();
		Run(m, 0.1f, 180f, Stand, speed: 3f);
		Assert.Equal(36f, m.PelvisYawDeg, 0);
	}

	[Fact]
	public void LyingProneThePelvisFollowsTheAim()
	{
		var m = new PivotModel();
		Run(m, 0.05f, 100f, Prone);
		Assert.Equal(100f, m.PelvisYawDeg, 3);
		Assert.False(m.Pivoting);
	}

	[Fact]
	public void NoPivotStartsWhileLocked()
	{
		var m = new PivotModel();
		Run(m, 0.5f, 120f, Stand, locked: true);
		Assert.False(m.Pivoting);
		Assert.Equal(0f, m.PelvisYawDeg);
		Assert.Equal(120f, m.TwistDeg, 3);
		Run(m, 0.02f, 120f, Stand, locked: false);
		Assert.True(m.Pivoting);
	}

	[Fact]
	public void APivotThatStartedFinishesEvenIfLockedLater()
	{
		var m = new PivotModel();
		Run(m, 0.05f, 100f, Stand);
		Run(m, 1f, 100f, Stand, locked: true);
		Assert.False(m.Pivoting);
		Assert.Equal(100f, m.PelvisYawDeg, 0);
	}

	[Fact]
	public void TheCrouchedConeIsNarrower()
	{
		var stand = new PivotModel();
		var crouch = new PivotModel();
		Run(stand, 0.02f, 47f, Stand);
		Run(crouch, 0.02f, 47f, Crouch);
		Assert.False(stand.Pivoting);
		Assert.True(crouch.Pivoting);
	}

	[Fact]
	public void PivotStartedFiresOncePerPivot()
	{
		var m = new PivotModel();
		var started = 0;
		m.PivotStarted += _ => started++;
		Run(m, 1f, 90f, Stand);
		Assert.Equal(1, started);
		Run(m, 1f, -90f, Stand);
		Assert.Equal(2, started);
	}

	[Fact]
	public void TheCuesPeakAtTheStartOfAPivotAndDieAway()
	{
		var m = new PivotModel();
		Assert.Equal(0f, m.CameraDipM);
		Assert.Equal(1f, m.SwayBoost);
		Run(m, 0.12f, 90f, Stand);
		Assert.True(m.CameraDipM < -0.01f);
		Assert.True(m.SwayBoost > 1.3f);
		Run(m, 0.6f, 90f, Stand);
		Assert.Equal(0f, m.CameraDipM);
		Assert.Equal(0f, m.JitterDeg);
		Assert.Equal(1f, m.SwayBoost);
	}

	[Fact]
	public void TheJitterWobblesBothWaysAndStaysSmall()
	{
		var m = new PivotModel();
		float min = 0f, max = 0f;
		for (var t = 0; t < 40; t++)
		{
			m.Update(0.01f, 90f, Stand, 0f, false);
			min = Math.Min(min, m.JitterDeg);
			max = Math.Max(max, m.JitterDeg);
		}
		Assert.True(min < -0.1f && max > 0.1f);
		Assert.True(max <= 0.4f && min >= -0.4f);
	}

	[Fact]
	public void TheAiRateOverridesTheTuning()
	{
		var m = new PivotModel();
		Run(m, 0.1f, 90f, Stand, rate: 540f);
		Assert.Equal(54f, m.PelvisYawDeg, 0);
	}
}
