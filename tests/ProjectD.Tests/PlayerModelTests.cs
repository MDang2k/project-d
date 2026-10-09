using Godot;
using Xunit;

namespace ProjectD.Tests;

public class WeightModelTests
{
	private readonly WeightModel _w = new(WeightTuning.Default);

	[Fact]
	public void TierBoundaries()
	{
		Assert.Equal(WeightTier.Light, _w.TierFor(0f));
		Assert.Equal(WeightTier.Light, _w.TierFor(19.9f));
		Assert.Equal(WeightTier.Moderate, _w.TierFor(20f));
		Assert.Equal(WeightTier.Heavy, _w.TierFor(35f));
		Assert.Equal(WeightTier.Overloaded, _w.TierFor(50f));
	}

	[Fact]
	public void StrengthBonusRaisesThresholds()
	{
		Assert.Equal(WeightTier.Light, _w.TierFor(22f, 5f));
		Assert.Equal(WeightTier.Moderate, _w.TierFor(25f, 5f));
	}

	[Fact]
	public void SprintBlockedFromHeavy()
	{
		Assert.True(_w.CanSprint(WeightTier.Moderate));
		Assert.False(_w.CanSprint(WeightTier.Heavy));
	}
}

public class StaminaPoolTests
{
	[Fact]
	public void DepletesAndLocks()
	{
		var pool = new StaminaPool(100f, 20f, 1f);
		pool.Spend(150f);
		Assert.Equal(0f, pool.Value);
		Assert.True(pool.IsExhausted);
	}

	[Fact]
	public void RecoveryWaitsForDelay()
	{
		var pool = new StaminaPool(100f, 20f, 1f);
		pool.Spend(50f);
		pool.Tick(0.5f, 10f);
		Assert.Equal(50f, pool.Value);
		pool.Tick(0.5f, 10f);
		pool.Tick(1f, 10f);
		Assert.Equal(60f, pool.Value);
	}

	[Fact]
	public void ResumesAtThreshold()
	{
		var pool = new StaminaPool(100f, 20f, 0f);
		pool.Spend(100f);
		pool.Tick(1f, 10f);
		Assert.True(pool.IsExhausted);
		pool.Tick(1f, 10f);
		Assert.False(pool.IsExhausted);
	}
}

public class MovementModelTests
{
	private readonly MovementTuning _t = MovementTuning.Default;
	private readonly MovementModel _m;

	public MovementModelTests() => _m = new MovementModel(_t, new WeightModel(WeightTuning.Default));

	[Fact]
	public void StanceSpeedOrder()
	{
		var tier = WeightTier.Light;
		var stand = _m.TargetSpeed(Stance.Stand, false, tier);
		var sprint = _m.TargetSpeed(Stance.Stand, true, tier);
		var crouch = _m.TargetSpeed(Stance.Crouch, false, tier);
		var prone = _m.TargetSpeed(Stance.Prone, true, tier);
		Assert.True(sprint > stand);
		Assert.True(stand > crouch);
		Assert.True(crouch > prone);
		Assert.Equal(prone, _m.TargetSpeed(Stance.Prone, false, tier));
	}

	[Fact]
	public void OverloadedIsSlower()
	{
		Assert.True(_m.TargetSpeed(Stance.Stand, false, WeightTier.Overloaded)
			< _m.TargetSpeed(Stance.Stand, false, WeightTier.Light));
	}

	[Fact]
	public void AccelerationIsGradual()
	{
		var v = _m.StepHorizontal(Vector2.Zero, Vector2.Right, 5f, WeightTier.Light, true, 0.1f);
		Assert.Equal(_t.Acceleration * 0.1f, v.X, 3);
		Assert.True(v.X < 5f);
	}

	[Fact]
	public void HeavyAcceleratesSlower()
	{
		var light = _m.StepHorizontal(Vector2.Zero, Vector2.Right, 5f, WeightTier.Light, true, 0.1f);
		var heavy = _m.StepHorizontal(Vector2.Zero, Vector2.Right, 5f, WeightTier.Heavy, true, 0.1f);
		Assert.True(heavy.X < light.X);
	}

	[Fact]
	public void AirborneControlReduced()
	{
		var ground = _m.StepHorizontal(Vector2.Zero, Vector2.Right, 5f, WeightTier.Light, true, 0.1f);
		var air = _m.StepHorizontal(Vector2.Zero, Vector2.Right, 5f, WeightTier.Light, false, 0.1f);
		Assert.True(air.X < ground.X);
	}

	[Fact]
	public void ReachesTargetSpeed()
	{
		var v = _m.StepHorizontal(new Vector2(4.9f, 0f), Vector2.Right, 5f, WeightTier.Light, true, 1f);
		Assert.Equal(new Vector2(5f, 0f), v);
	}
}
