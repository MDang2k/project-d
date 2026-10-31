using Xunit;

namespace ProjectD.Tests;

public class StanceTransitionTests
{
	private static readonly StanceTransitionTuning T = new();

	private static void Run(StanceTransition s, float seconds)
	{
		for (var t = 0f; t < seconds - 1e-4f; t += 0.01f)
			s.Update(0.01f);
	}

	[Theory]
	[InlineData(Stance.Stand, Stance.Crouch, 0.35f)]
	[InlineData(Stance.Crouch, Stance.Stand, 0.40f)]
	[InlineData(Stance.Stand, Stance.Prone, 1.25f)]
	[InlineData(Stance.Crouch, Stance.Prone, 1.25f)]
	[InlineData(Stance.Prone, Stance.Stand, 1.30f)]
	[InlineData(Stance.Prone, Stance.Crouch, 1.30f)]
	public void DurationsFollowTheSpec(Stance from, Stance to, float seconds)
		=> Assert.Equal(seconds, StanceTransition.DurationS(T, from, to));

	[Fact]
	public void CrouchChangesDoNotLockAnything()
	{
		var s = new StanceTransition();
		Assert.True(s.TryStart(Stance.Stand, Stance.Crouch));
		Assert.Equal(TransitionKind.Change, s.Kind);
		Assert.False(s.Locked);
		Assert.False(s.BlocksFire);
		Assert.False(s.BlocksMovement);
		Assert.False(s.BlocksAds);
	}

	[Fact]
	public void ACrouchChangeCanBeReversedMidWay()
	{
		var s = new StanceTransition();
		s.TryStart(Stance.Stand, Stance.Crouch);
		Run(s, 0.15f);
		Assert.True(s.TryStart(Stance.Crouch, Stance.Stand));
		Assert.Equal(0f, s.Progress);
	}

	[Fact]
	public void GoingProneLocksEverythingForTheWholeAnimation()
	{
		var s = new StanceTransition();
		Assert.True(s.TryStart(Stance.Stand, Stance.Prone));
		for (var i = 0; i < 124; i++)
		{
			s.Update(0.01f);
			Assert.True(s.Locked);
			Assert.True(s.BlocksFire && s.BlocksAds && s.BlocksWeaponActions && s.BlocksMovement && s.BlocksStanceInput, $"unlocked at step {i}");
		}
		Run(s, 0.02f);
		Assert.False(s.Locked);
	}

	[Fact]
	public void StanceInputsAreIgnoredWhileLocked()
	{
		var s = new StanceTransition();
		s.TryStart(Stance.Stand, Stance.Prone);
		Run(s, 0.5f);
		Assert.False(s.TryStart(Stance.Prone, Stance.Stand));
		Assert.False(s.TryStart(Stance.Prone, Stance.Crouch));
		Assert.False(s.StartStep(Stance.Crouch, Stance.Stand));
		Assert.Equal(Stance.Prone, s.To);
		Assert.Equal(TransitionKind.ProneEntry, s.Kind);
	}

	[Fact]
	public void GettingUpCannotBeCancelledByGoingDownAgain()
	{
		var s = new StanceTransition();
		s.TryStart(Stance.Prone, Stance.Stand);
		Run(s, 0.65f);
		Assert.False(s.TryStart(Stance.Stand, Stance.Prone));
		Assert.True(s.BlocksFire && s.BlocksAds && s.BlocksMovement);
		Assert.Equal(Stance.Stand, s.To);
	}

	[Fact]
	public void TheWeaponComesBackToTheViewOnlyAfterTheAnimation()
	{
		var s = new StanceTransition();
		s.TryStart(Stance.Prone, Stance.Crouch);
		Run(s, 1.0f);
		Assert.Equal(0f, s.WeaponFollowsView);
		Run(s, 0.32f);
		Assert.False(s.Locked);
		Assert.True(s.BlocksFire); // still re-centring
		Assert.InRange(s.WeaponFollowsView, 0f, 0.5f);
		Run(s, 0.25f);
		Assert.False(s.BlocksFire);
		Assert.Equal(1f, s.WeaponFollowsView);
	}

	[Fact]
	public void HandsLeaveTheGripEarlyAndReturnWithTheWeapon()
	{
		var s = new StanceTransition();
		Assert.Equal(1f, s.GripWeight);
		s.TryStart(Stance.Stand, Stance.Prone);
		Run(s, 0.05f);
		Assert.InRange(s.GripWeight, 0.4f, 0.9f);
		Run(s, 0.3f);
		Assert.Equal(0f, s.GripWeight);
		Run(s, 0.5f);
		Assert.Equal(0f, s.GripWeight);
		Run(s, 0.5f);
		Assert.InRange(s.GripWeight, 0.01f, 1f);
	}

	[Fact]
	public void AGettingUpBodyCountsAsStandingFromHalfWayButNotBefore()
	{
		var s = new StanceTransition();
		s.TryStart(Stance.Prone, Stance.Stand);
		Run(s, 0.6f);
		Assert.Equal(Stance.Prone, s.PerceivedStance(Stance.Stand));
		Run(s, 0.1f);
		Assert.Equal(Stance.Stand, s.PerceivedStance(Stance.Stand));
	}

	[Fact]
	public void ProneEntryIsPerceivedAsProneFromHalfWay()
	{
		var s = new StanceTransition();
		s.TryStart(Stance.Stand, Stance.Prone);
		Run(s, 0.5f);
		Assert.Equal(Stance.Stand, s.PerceivedStance(Stance.Prone));
		Run(s, 0.3f);
		Assert.Equal(Stance.Prone, s.PerceivedStance(Stance.Prone));
	}

	[Fact]
	public void TheEyeSlamsDownLateAndRisesSlowly()
	{
		var down = new StanceTransition();
		down.TryStart(Stance.Stand, Stance.Prone);
		Run(down, 0.625f); // half the time
		var downHalf = down.EyeBlend;
		var up = new StanceTransition();
		up.TryStart(Stance.Prone, Stance.Stand);
		Run(up, 0.65f);
		Assert.True(downHalf < 0.35f, $"slam eased in, was {downHalf}");
		Assert.Equal(0.5f, up.EyeBlend, 1);
		Run(down, 0.5f);
		Assert.True(down.EyeBlend > 0.95f);
	}

	[Fact]
	public void TheSlamDipsBelowTheFinalHeightAndSettles()
	{
		var s = new StanceTransition();
		s.TryStart(Stance.Stand, Stance.Prone);
		Run(s, 1.125f); // progress 0.9
		Assert.True(s.EyeSettleM < -0.02f);
		Run(s, 0.2f);
		Assert.Equal(0f, s.EyeSettleM);
	}

	[Fact]
	public void TheCameraPitchesForwardAtTheSlamAndTiltsUpOnTheWayOut()
	{
		var s = new StanceTransition();
		s.TryStart(Stance.Stand, Stance.Prone);
		Run(s, 0.625f);
		Assert.True(s.CameraPitchDeg < -10f);
		Run(s, 0.7f);
		Assert.Equal(0f, s.CameraPitchDeg, 1);
		s.TryStart(Stance.Prone, Stance.Stand);
		Assert.True(s.CameraPitchDeg < -10f);
		Run(s, 1.3f);
		Assert.Equal(0f, s.CameraPitchDeg, 1);
	}

	[Fact]
	public void GranularStepsAreShortAndReversible()
	{
		var s = new StanceTransition();
		Assert.True(s.StartStep(Stance.Crouch, Stance.Crouch));
		Run(s, 0.16f);
		Assert.False(s.Active);
		Assert.False(s.StartStep(Stance.Crouch, Stance.Prone));
	}

	[Fact]
	public void ASpeedFactorShortensTheTransition()
	{
		var s = new StanceTransition();
		s.TryStart(Stance.Stand, Stance.Prone, 1.25f);
		Run(s, 1.01f);
		Assert.False(s.Locked);
	}

	[Fact]
	public void SameStanceStartsNothing()
		=> Assert.False(new StanceTransition().TryStart(Stance.Prone, Stance.Prone));
}
