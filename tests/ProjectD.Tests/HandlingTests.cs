using Godot;
using Xunit;

namespace ProjectD.Tests;

public class HandlingModelTests
{
	private readonly HandlingTuning _t = HandlingTuning.Default;

	private static HandlingContext Ctx(Stance stance = Stance.Stand, float arms = 1f, float speed = 0f,
		float ads = 0f, bool breath = false, float kg = 0f)
		=> new(stance, kg, arms, speed, ads, breath);

	[Fact]
	public void StanceReducesSway()
	{
		var stand = HandlingModel.SwayAmplitude(_t, 1f, Ctx());
		var crouch = HandlingModel.SwayAmplitude(_t, 1f, Ctx(Stance.Crouch));
		var prone = HandlingModel.SwayAmplitude(_t, 1f, Ctx(Stance.Prone));
		Assert.True(stand > crouch);
		Assert.True(crouch > prone);
	}

	[Fact]
	public void ExhaustedArmsIncreaseSway()
	{
		var fresh = HandlingModel.SwayAmplitude(_t, 1f, Ctx(arms: 1f));
		var tired = HandlingModel.SwayAmplitude(_t, 1f, Ctx(arms: 0f));
		Assert.Equal(fresh * (1f + _t.ExhaustionSwayGain), tired, 4);
	}

	[Fact]
	public void BreathHoldAndAdsReduceSway()
	{
		var baseSway = HandlingModel.SwayAmplitude(_t, 1f, Ctx());
		Assert.True(HandlingModel.SwayAmplitude(_t, 1f, Ctx(ads: 1f)) < baseSway);
		Assert.True(HandlingModel.SwayAmplitude(_t, 1f, Ctx(ads: 1f, breath: true))
			< HandlingModel.SwayAmplitude(_t, 1f, Ctx(ads: 1f)));
	}

	[Fact]
	public void HeavierWeaponAndMovementIncreaseSway()
	{
		var baseSway = HandlingModel.SwayAmplitude(_t, 1f, Ctx());
		Assert.True(HandlingModel.SwayAmplitude(_t, 1f, Ctx(kg: 10f)) > baseSway);
		Assert.True(HandlingModel.SwayAmplitude(_t, 1f, Ctx(speed: 1f)) > baseSway);
	}

	[Fact]
	public void RecoilMultiplierOrdering()
	{
		var stand = HandlingModel.RecoilMultiplier(_t, Stance.Stand, 0f);
		Assert.Equal(1f, stand);
		Assert.True(HandlingModel.RecoilMultiplier(_t, Stance.Prone, 0f) < HandlingModel.RecoilMultiplier(_t, Stance.Crouch, 0f));
		Assert.True(HandlingModel.RecoilMultiplier(_t, Stance.Stand, 1f) < stand);
	}
}

public class SwayModelTests
{
	[Fact]
	public void StaysWithinAmplitude()
	{
		var sway = new SwayModel();
		for (var i = 0; i < 2000; i++)
		{
			var v = sway.Update(0.01f, 0.6f);
			Assert.InRange(v.X, -0.6f, 0.6f);
			Assert.InRange(v.Y, -0.6f, 0.6f);
		}
	}

	[Fact]
	public void ZeroAmplitudeIsStill()
	{
		var sway = new SwayModel();
		Assert.Equal(Vector2.Zero, sway.Update(0.5f, 0f));
	}
}

public class RecoilModelTests
{
	// 1 deg climb, 0.5 drift, settle 30% over 0.5 s after a 0.1 s release delay
	private static readonly RecoilTuning Tune = new(1f, 0.5f, 0.3f, 0.5f, 0.1f);

	[Fact]
	public void KickClimbsAndDriftsDeterministically()
	{
		var r = new RecoilModel();
		r.Kick(Tune, 1f, 1f);
		r.Kick(Tune, 1f, -1f);
		Assert.Equal(0f, r.Offset.X, 4);
		Assert.Equal(2f, r.Offset.Y, 4);
	}

	[Fact]
	public void MultiplierScalesKick()
	{
		var r = new RecoilModel();
		r.Kick(Tune, 0.5f, 0f);
		Assert.Equal(0.5f, r.Offset.Y, 4);
	}

	[Fact]
	public void NoRecoveryDuringABurst()
	{
		var r = new RecoilModel();
		for (var i = 0; i < 10; i++)
		{
			r.Kick(Tune, 1f, 0f);
			r.Update(0.05f, Tune); // shots 50 ms apart, inside the release delay
		}
		Assert.Equal(10f, r.Offset.Y, 3);
	}

	[Fact]
	public void SettlesThirtyPercentAfterReleaseAndKeepsTheRest()
	{
		var r = new RecoilModel();
		for (var i = 0; i < 10; i++)
			r.Kick(Tune, 1f, 0f);
		r.Update(0.1f, Tune);
		r.Update(5f, Tune);
		Assert.Equal(7f, r.Offset.Y, 3);
		r.Update(10f, Tune);
		Assert.Equal(7f, r.Offset.Y, 3);
	}

	[Fact]
	public void PullingDownPaysTheKickBeforeMovingTheView()
	{
		var r = new RecoilModel();
		r.Kick(Tune, 3f, 0f); // 3 deg up
		var rest = r.Pull(new Vector2(0f, -2f));
		Assert.Equal(1f, r.Offset.Y, 4);
		Assert.Equal(0f, rest.Y, 4);
		rest = r.Pull(new Vector2(0f, -1.5f));
		Assert.Equal(0f, r.Offset.Y, 4);
		Assert.Equal(-0.5f, rest.Y, 4);
	}

	[Fact]
	public void PullingUpOrSidewaysAgainstNothingPassesThrough()
	{
		var r = new RecoilModel();
		r.Kick(Tune, 1f, 0f);
		var rest = r.Pull(new Vector2(0.3f, 0.4f));
		Assert.Equal(new Vector2(0.3f, 0.4f), rest);
		Assert.Equal(1f, r.Offset.Y, 4);
	}
}

public class RatingCurveTests
{
	[Fact]
	public void FiftyIsNeutralAndHundredStillKicks()
	{
		Assert.Equal(1f, RatingCurves.RecoilMult(50f), 4);
		Assert.Equal(RatingCurves.RecoilFloor, RatingCurves.RecoilMult(100f), 4);
		Assert.True(RatingCurves.RecoilMult(150f) >= RatingCurves.RecoilFloor);
	}

	[Fact]
	public void CurvesAreMonotonic()
	{
		for (var r = 1; r <= 100; r++)
		{
			Assert.True(RatingCurves.RecoilMult(r) <= RatingCurves.RecoilMult(r - 1));
			Assert.True(RatingCurves.SwayMult(r) <= RatingCurves.SwayMult(r - 1));
		}
	}
}

public class AimAndBreathTests
{
	[Fact]
	public void AdsBlendsOverTime()
	{
		var a = new AimModel();
		a.Update(true, 0.1f, 0.2f);
		Assert.Equal(0.5f, a.Amount, 3);
		a.Update(true, 0.5f, 0.2f);
		Assert.Equal(1f, a.Amount);
		a.Update(false, 0.1f, 0.2f);
		Assert.Equal(0.5f, a.Amount, 3);
	}

	[Fact]
	public void BreathHoldDrainsArms()
	{
		var arms = new StaminaPool(100f, 20f, 1f);
		Assert.True(BreathHold.Update(true, arms, 10f, 1f));
		Assert.Equal(90f, arms.Value, 3);
	}

	[Fact]
	public void BreathHoldFailsWhenExhaustedAndNotHeld()
	{
		var arms = new StaminaPool(100f, 20f, 1f);
		Assert.False(BreathHold.Update(false, arms, 10f, 1f));
		Assert.Equal(100f, arms.Value);
		Assert.False(BreathHold.Update(true, arms, 200f, 1f));
		Assert.True(arms.IsExhausted);
		Assert.False(BreathHold.Update(true, arms, 10f, 0.1f));
	}
}

public class HipSpreadTests
{
	[Fact]
	public void SteadierWeaponsSpreadLessAndAimingClosesTheCone()
	{
		var loose = HandlingModel.HipSpreadDeg(2.5f, RatingCurves.SwayMult(20f), 0f);
		var steady = HandlingModel.HipSpreadDeg(2.5f, RatingCurves.SwayMult(80f), 0f);
		Assert.True(steady < loose);
		Assert.Equal(0f, HandlingModel.HipSpreadDeg(2.5f, 1f, 1f), 4);
		Assert.Equal(2.5f, HandlingModel.HipSpreadDeg(2.5f, RatingCurves.SwayMult(50f), 0f), 4);
	}
}
