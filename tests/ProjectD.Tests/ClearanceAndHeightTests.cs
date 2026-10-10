using Xunit;

namespace ProjectD.Tests;

public class ClearanceModelTests
{
	private readonly ClearanceModel _m = new(ClearanceTuning.Default);

	[Fact]
	public void NoHitNoRetract()
		=> Assert.Equal(0f, _m.TargetRetract(1.0f, 1.2f));

	[Fact]
	public void RetractsByOverlapPlusMargin()
		=> Assert.Equal(0.24f, _m.TargetRetract(1.0f, 0.8f), 3);

	[Fact]
	public void RetractIsCapped()
		=> Assert.Equal(0.4f, _m.TargetRetract(1.0f, 0.1f), 3);

	[Fact]
	public void UpdateMovesGraduallyAndFasterIn()
	{
		var a = new ClearanceModel(ClearanceTuning.Default);
		a.Update(0.01f, 0.4f);
		Assert.Equal(0.1f, a.Retract, 3);
		a.Update(1f, 0.4f);
		Assert.Equal(0.4f, a.Retract, 3);
		a.Update(0.1f, 0f);
		Assert.Equal(0.4f - 0.4f, a.Retract, 3);
	}

	[Fact]
	public void TiltAndFireBlockFollowFraction()
	{
		var a = new ClearanceModel(ClearanceTuning.Default);
		a.Update(1f, 0.2f);
		Assert.Equal(12.5f, a.TiltDeg, 3);
		Assert.False(a.BlocksFire);
		a.Update(1f, 0.3f);
		Assert.True(a.BlocksFire);
	}
}

public class StanceHeightModelTests
{
	[Fact]
	public void StartsAtFullCrouch()
	{
		var m = new StanceHeightModel(0.1f);
		Assert.Equal(0f, m.CrouchLevel);
		Assert.Equal(1f, m.LevelFor(Stance.Stand));
		Assert.Equal(0f, m.LevelFor(Stance.Prone));
	}

	[Fact]
	public void ScrollDownFromStandCrouchesJustBelow()
	{
		var m = new StanceHeightModel(0.1f);
		Assert.Equal(Stance.Crouch, m.Adjust(Stance.Stand, -1f));
		Assert.Equal(0.9f, m.CrouchLevel, 3);
	}

	[Fact]
	public void ScrollingDownFurtherLowersAndClamps()
	{
		var m = new StanceHeightModel(0.1f);
		var s = Stance.Stand;
		for (var i = 0; i < 20; i++)
			s = m.Adjust(s, -1f);
		Assert.Equal(Stance.Crouch, s);
		Assert.Equal(0f, m.CrouchLevel, 3);
	}

	[Fact]
	public void ScrollUpToTopStands()
	{
		var m = new StanceHeightModel(0.25f);
		var s = m.Adjust(Stance.Stand, -2f);
		Assert.Equal(Stance.Crouch, s);
		Assert.Equal(0.5f, m.CrouchLevel, 3);
		s = m.Adjust(s, 1f);
		Assert.Equal(Stance.Crouch, s);
		s = m.Adjust(s, 2f);
		Assert.Equal(Stance.Stand, s);
	}

	[Fact]
	public void LevelIsRememberedAfterStanding()
	{
		var m = new StanceHeightModel(0.1f);
		m.Adjust(Stance.Stand, -4f);
		Assert.Equal(0.6f, m.CrouchLevel, 3);
		Assert.Equal(0.6f, m.LevelFor(Stance.Crouch), 3);
	}

	[Fact]
	public void ProneIgnoresWheel()
	{
		var m = new StanceHeightModel(0.1f);
		Assert.Equal(Stance.Prone, m.Adjust(Stance.Prone, 5f));
		Assert.Equal(0f, m.CrouchLevel);
	}
}

public class MovementLevelTests
{
	private readonly MovementModel _m = new(MovementTuning.Default, new WeightModel(WeightTuning.Default));

	[Fact]
	public void HeightsInterpolateByLevel()
	{
		var t = MovementTuning.Default;
		Assert.Equal(t.CrouchHeight, _m.CapsuleHeight(Stance.Crouch, 0f), 4);
		Assert.Equal(t.StandHeight, _m.CapsuleHeight(Stance.Crouch, 1f), 4);
		Assert.Equal((t.CrouchEyeHeight + t.StandEyeHeight) * 0.5f, _m.EyeHeight(Stance.Crouch, 0.5f), 4);
		Assert.Equal(t.StandHeight, _m.CapsuleHeight(Stance.Stand), 4);
	}

	[Fact]
	public void CrouchSpeedBlendsTowardWalk()
	{
		var tier = WeightTier.Light;
		var low = _m.TargetSpeed(Stance.Crouch, false, tier, 0f);
		var half = _m.TargetSpeed(Stance.Crouch, false, tier, 0.5f);
		var high = _m.TargetSpeed(Stance.Crouch, false, tier, 1f);
		Assert.True(low < half && half < high);
		Assert.Equal(_m.TargetSpeed(Stance.Stand, false, tier), high, 4);
	}
}
