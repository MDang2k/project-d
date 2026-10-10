using Xunit;

namespace ProjectD.Tests;

public class VitalsModelTests
{
	private readonly VitalsTuning _t = VitalsTuning.Default;
	private readonly VitalsModel _v = new(VitalsTuning.Default);
	private static readonly VitalsActivity Idle = new(false, false, false);

	[Fact]
	public void StartsFullAndDrainsSlowly()
	{
		Assert.Equal(100f, _v.Energy);
		_v.Update(60f, Idle);
		Assert.Equal(100f - 60f * _t.EnergyDrainPerS, _v.Energy, 3);
		Assert.Equal(100f - 60f * _t.HydrationDrainPerS, _v.Hydration, 3);
	}

	[Fact]
	public void SprintingLoadAndInjuryDrainFaster()
	{
		var a = new VitalsModel(_t);
		var b = new VitalsModel(_t);
		a.Update(10f, Idle);
		b.Update(10f, new VitalsActivity(true, true, true));
		Assert.True(b.Energy < a.Energy);
		var expected = _t.EnergyDrainPerS * _t.SprintDrainMultiplier * _t.HeavyLoadDrainMultiplier
			* _t.InjuredDrainMultiplier * 10f;
		Assert.Equal(100f - expected, b.Energy, 3);
	}

	[Fact]
	public void NoStarvationDamageUntilEmpty()
	{
		Assert.Equal(0f, _v.Update(1f, Idle));
		_v.Set(0f, 50f);
		Assert.Equal(_t.StarvationDamagePerS, _v.Update(1f, Idle));
		Assert.True(_v.Starving);
	}

	[Fact]
	public void LowMetersSlowStaminaRecoveryWorstWins()
	{
		Assert.Equal(1f, _v.StaminaRecoveryMultiplier);
		_v.Set(100f, 0f);
		Assert.Equal(_t.EmptyRecoveryMultiplier, _v.StaminaRecoveryMultiplier, 3);
		_v.Set(12.5f, 100f);
		Assert.Equal((1f + _t.EmptyRecoveryMultiplier) * 0.5f, _v.StaminaRecoveryMultiplier, 3);
	}

	[Fact]
	public void LowHydrationAddsSwayButEnergyDoesNot()
	{
		_v.Set(0f, 100f);
		Assert.Equal(1f, _v.SwayMultiplier);
		_v.Set(100f, 0f);
		Assert.Equal(_t.EmptyHydrationSwayMultiplier, _v.SwayMultiplier, 3);
	}

	[Fact]
	public void EatingClampsAtFull()
	{
		_v.Set(90f, 95f);
		_v.Eat(50f, 20f);
		Assert.Equal(100f, _v.Energy);
		Assert.Equal(100f, _v.Hydration);
	}

	[Fact]
	public void ResetRestoresFull()
	{
		_v.Set(1f, 2f);
		_v.Reset();
		Assert.Equal(100f, _v.Energy);
		Assert.Equal(100f, _v.Hydration);
	}
}

public class ConsumableControllerTests
{
	[Fact]
	public void UseTakesTimeThenRestores()
	{
		var v = new VitalsModel(VitalsTuning.Default);
		v.Set(10f, 10f);
		var c = new ConsumableController(v);
		var can = new ConsumableSpec("canned_food", 6f, 30f, 0f);
		Assert.True(c.Start(can));
		Assert.False(c.Start(can));
		c.Update(3f);
		Assert.Equal(10f, v.Energy);
		c.Update(3.1f);
		Assert.Equal(40f, v.Energy);
		Assert.False(c.IsBusy);
	}

	[Fact]
	public void CancelDiscardsItem()
	{
		var v = new VitalsModel(VitalsTuning.Default);
		v.Set(10f, 10f);
		var c = new ConsumableController(v);
		c.Start(new ConsumableSpec("water", 5f, 0f, 40f));
		c.Update(2f);
		c.Cancel();
		c.Update(10f);
		Assert.Equal(10f, v.Hydration);
	}
}

public class MeleeModelTests
{
	private static readonly MeleeSpec Knife = new("knife", 30f, 1, 1.6f, 0.15f, 0.45f, 8f);

	[Fact]
	public void StrikeLandsAtEndOfWindupOnce()
	{
		var m = new MeleeModel(Knife);
		Assert.True(m.TryStart(false));
		Assert.False(m.Update(0.1f));
		Assert.True(m.Update(0.06f));
		Assert.Equal(MeleePhase.Recover, m.Phase);
		Assert.False(m.Update(0.1f));
	}

	[Fact]
	public void CannotChainDuringAttackThenReady()
	{
		var m = new MeleeModel(Knife);
		m.TryStart(false);
		Assert.False(m.TryStart(false));
		m.Update(0.2f);
		Assert.False(m.TryStart(false));
		m.Update(0.5f);
		Assert.False(m.IsBusy);
		Assert.True(m.TryStart(false));
	}

	[Fact]
	public void ExhaustedArmsCannotStrike()
	{
		var m = new MeleeModel(Knife);
		Assert.False(m.TryStart(true));
		Assert.False(m.IsBusy);
	}

	[Fact]
	public void ProgressSpansWholeAttack()
	{
		var m = new MeleeModel(Knife);
		Assert.Equal(0f, m.Progress);
		m.TryStart(false);
		m.Update(0.3f);
		Assert.Equal(0.5f, m.Progress, 3);
	}
}
