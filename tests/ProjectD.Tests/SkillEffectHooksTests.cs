using Xunit;

namespace ProjectD.Tests;

/// <summary>The models that skills reach into (M8a spec s2): each must obey the scale it is given and be unchanged at 1.</summary>
public class SkillEffectHooksTests
{
	// ---- Endurance: leg stamina

	[Fact]
	public void StaminaScaleRaisesTheMaximumKeepingTheFillFraction()
	{
		var pool = new StaminaPool(100f, 20f, 1f);
		pool.Spend(50f);
		Assert.Equal(0.5f, pool.Fraction, 3);
		pool.SetScale(1.3f);
		Assert.Equal(130f, pool.MaxValue, 3);
		Assert.Equal(0.5f, pool.Fraction, 3);
		Assert.Equal(65f, pool.Value, 3);
	}

	[Fact]
	public void StaminaScaleSpeedsUpRecovery()
	{
		var plain = new StaminaPool(100f, 20f, 0f);
		var skilled = new StaminaPool(100f, 20f, 0f);
		skilled.SetScale(1.3f);
		plain.Spend(60f);
		skilled.Spend(78f); // the same 60 % of a bigger pool
		plain.Tick(1f, 10f);
		skilled.Tick(1f, 10f);
		Assert.Equal(50f, plain.Value, 3);
		Assert.Equal(52f + 13f, skilled.Value, 3); // 130 - 78 = 52, then +10 x 1.3
	}

	[Fact]
	public void StaminaScaleOfOneChangesNothingAndBadScalesAreIgnored()
	{
		var pool = new StaminaPool(100f, 20f, 1f);
		pool.SetScale(1f);
		pool.SetScale(0f);
		pool.SetScale(-2f);
		Assert.Equal(100f, pool.MaxValue);
		Assert.Equal(100f, pool.Value);
	}

	[Fact]
	public void StaminaDoesNotDrainOrRefillOnALevelUpWhenExhausted()
	{
		var pool = new StaminaPool(100f, 20f, 1f);
		pool.Spend(100f);
		Assert.True(pool.IsExhausted);
		pool.SetScale(1.2f);
		Assert.Equal(0f, pool.Value);
		Assert.True(pool.IsExhausted);
	}

	// ---- Strength: weight thresholds

	[Fact]
	public void StrengthRaisesEveryWeightThreshold()
	{
		var w = new WeightModel(WeightTuning.Default); // 20, 35, 50
		Assert.Equal(WeightTier.Moderate, w.TierFor(22f));
		Assert.Equal(WeightTier.Light, w.TierFor(22f, 5f));
		Assert.Equal(WeightTier.Moderate, w.TierFor(25f, 5f));
	}

	// ---- Weapon Handling: reload and check times

	private static (WeaponController C, WeaponState S) Make()
	{
		var state = new WeaponState(new FireControl(new[] { FireMode.Semi }, 600f));
		var ball = new AmmoType("ball", 50f, 3, 700f);
		var mag = new Magazine(30);
		for (var i = 0; i < 10; i++)
			mag.Push(ball);
		state.InsertMagazine(mag);
		state.ChamberRound();
		return (new WeaponController(state, ActionTuning.Default), state);
	}

	private static float Seconds(WeaponController c, System.Func<bool> start)
	{
		Assert.True(start());
		var t = 0f;
		while (c.IsBusy && t < 30f)
		{
			c.Update(0.01f);
			t += 0.01f;
		}
		return t;
	}

	[Fact]
	public void TheHandlingScaleShortensChecksInProportion()
	{
		var (plain, _) = Make();
		var (skilled, _) = Make();
		skilled.SkillTimeMultiplier = 0.75f;
		var a = Seconds(plain, plain.StartMagCheck);
		var b = Seconds(skilled, skilled.StartMagCheck);
		Assert.InRange(b / a, 0.72f, 0.78f);
		var c = Seconds(plain, plain.StartChamberCheck);
		var d = Seconds(skilled, skilled.StartChamberCheck);
		Assert.InRange(d / c, 0.72f, 0.78f);
	}

	[Fact]
	public void TheHandlingScaleShortensReloads()
	{
		var (plain, _) = Make();
		var (skilled, _) = Make();
		plain.Pouch.Add(MakeMag());
		skilled.Pouch.Add(MakeMag());
		skilled.SkillTimeMultiplier = 0.75f;
		var a = Seconds(plain, () => plain.StartReload(false));
		var b = Seconds(skilled, () => skilled.StartReload(false));
		Assert.InRange(b / a, 0.72f, 0.78f);
	}

	private static Magazine MakeMag()
	{
		var m = new Magazine(30);
		for (var i = 0; i < 30; i++)
			m.Push(new AmmoType("ball", 50f, 3, 700f));
		return m;
	}

	[Fact]
	public void TheDefaultHandlingScaleChangesNothing()
	{
		var (c, _) = Make();
		Assert.Equal(1f, c.SkillTimeMultiplier);
		Assert.False(c.ExactMagCheck);
	}

	[Fact]
	public void ExactMagCheckGivesTheCountWhenTheSkillAllowsIt()
	{
		var (c, _) = Make();
		c.ExactMagCheck = true;
		MagCheckResult? seen = null;
		c.MagChecked += r => seen = r;
		Seconds(c, c.StartMagCheck);
		Assert.Equal(9, seen!.Value.ExactCount); // ten loaded, one of them chambered
	}

	// ---- Vitality: maximum HP

	[Fact]
	public void VitalityScalesEveryPartsMaximumHp()
	{
		var scaled = HealthTuning.Default.Scaled(1.15f);
		var body = new BodyHealth(scaled);
		foreach (var part in BodyHealth.AllParts)
			Assert.Equal(HealthTuning.Default.MaxHp(part) * 1.15f, body.MaxHp(part), 3);
		Assert.Equal(body.MaxHp(BodyPart.Head), body.Hp(BodyPart.Head));
	}

	[Fact]
	public void AScaleOfOneLeavesTheBodyAsItWas()
	{
		var body = new BodyHealth(HealthTuning.Default.Scaled(1f));
		foreach (var part in BodyHealth.AllParts)
			Assert.Equal(HealthTuning.Default.MaxHp(part), body.MaxHp(part));
	}
}
