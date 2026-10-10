using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class StatusEffectsTests
{
	private readonly EffectsTuning _t = EffectsTuning.Default;
	private readonly BodyHealth _body = new(HealthTuning.Default);
	private readonly StatusEffects _fx;

	public StatusEffectsTests() => _fx = new StatusEffects(EffectsTuning.Default);

	private static HitResult Hit(BodyPart part, float damage, bool armor = false, bool penetrated = true)
		=> new(part, damage, armor, penetrated, 0f, null);

	[Fact]
	public void LowRollCausesLightBleed()
	{
		_fx.OnHit(Hit(BodyPart.Stomach, 20f), () => 0.2);
		Assert.Equal(BleedLevel.Light, _fx.Bleed(BodyPart.Stomach));
	}

	[Fact]
	public void VeryLowRollOnPenetrationCausesHeavyBleed()
	{
		_fx.OnHit(Hit(BodyPart.Thorax, 40f), () => 0.05);
		Assert.Equal(BleedLevel.Heavy, _fx.Bleed(BodyPart.Thorax));
	}

	[Fact]
	public void HighRollCausesNothing()
	{
		_fx.OnHit(Hit(BodyPart.LeftArm, 40f), () => 0.99);
		Assert.Equal(BleedLevel.None, _fx.Bleed(BodyPart.LeftArm));
		Assert.False(_fx.IsFractured(BodyPart.LeftArm));
	}

	[Fact]
	public void StoppedHitRarelyBleeds()
	{
		_fx.OnHit(Hit(BodyPart.Thorax, 7f, armor: true, penetrated: false), () => 0.1);
		Assert.Equal(BleedLevel.None, _fx.Bleed(BodyPart.Thorax));
	}

	[Fact]
	public void FractureNeedsLimbAndDamageThreshold()
	{
		_fx.OnHit(Hit(BodyPart.LeftLeg, 10f), () => 0.0);
		Assert.False(_fx.IsFractured(BodyPart.LeftLeg));
		_fx.OnHit(Hit(BodyPart.Thorax, 60f), () => 0.0);
		Assert.False(_fx.IsFractured(BodyPart.Thorax));
		_fx.OnHit(Hit(BodyPart.LeftLeg, 40f), () => 0.0);
		Assert.True(_fx.IsFractured(BodyPart.LeftLeg));
	}

	[Fact]
	public void BleedDrainsPartOverTime()
	{
		_fx.OnHit(Hit(BodyPart.Stomach, 1f), () => 0.05);
		var before = _body.Hp(BodyPart.Stomach);
		_fx.Update(10f, _body);
		Assert.Equal(before - 10f * _t.HeavyBleedPerS, _body.Hp(BodyPart.Stomach), 3);
	}

	[Fact]
	public void HeavyBleedOnThoraxEventuallyKills()
	{
		_fx.OnHit(Hit(BodyPart.Thorax, 1f), () => 0.05);
		for (var i = 0; i < 100 && !_body.IsDead; i++)
			_fx.Update(1f, _body);
		Assert.True(_body.IsDead);
	}

	[Fact]
	public void BandageCuresLightOnlyTourniquetCuresHeavy()
	{
		_fx.OnHit(Hit(BodyPart.LeftArm, 5f), () => 0.2);
		_fx.OnHit(Hit(BodyPart.Thorax, 5f), () => 0.05);
		Assert.True(_fx.Bandage());
		Assert.Equal(BleedLevel.None, _fx.Bleed(BodyPart.LeftArm));
		Assert.False(_fx.Bandage());
		Assert.Equal(BleedLevel.Heavy, _fx.Bleed(BodyPart.Thorax));
		Assert.True(_fx.Tourniquet());
		Assert.Equal(BleedLevel.None, _fx.Bleed(BodyPart.Thorax));
	}

	[Fact]
	public void SplintCuresFracture()
	{
		_fx.OnHit(Hit(BodyPart.RightLeg, 40f), () => 0.0);
		Assert.True(_fx.Splint());
		Assert.False(_fx.IsFractured(BodyPart.RightLeg));
		Assert.False(_fx.Splint());
	}

	[Fact]
	public void DestroyedLimbClearsItsEffects()
	{
		_fx.OnHit(Hit(BodyPart.LeftLeg, 40f), () => 0.0);
		_fx.OnLimbDestroyed(BodyPart.LeftLeg);
		Assert.False(_fx.IsFractured(BodyPart.LeftLeg));
		Assert.Equal(BleedLevel.None, _fx.Bleed(BodyPart.LeftLeg));
	}

	[Fact]
	public void PainBuildsDecaysAndMorphineSuppresses()
	{
		_fx.OnHit(Hit(BodyPart.Thorax, 80f), () => 0.99);
		Assert.True(_fx.Pain > 0f);
		var penalties = _fx.ComputePenalties(_body);
		Assert.True(penalties.PainStrength > 0f);
		Assert.True(penalties.SwayMultiplier > 1f);
		_fx.Morphine(60f);
		var suppressed = _fx.ComputePenalties(_body);
		Assert.Equal(0f, suppressed.PainStrength);
		Assert.Equal(1f, suppressed.SwayMultiplier);
		_fx.Update(120f, _body);
		Assert.Equal(0f, _fx.Pain);
	}

	[Fact]
	public void InjuriesProducePenalties()
	{
		_body.TakeDamage(BodyPart.LeftArm, 70f);
		_body.TakeDamage(BodyPart.RightLeg, 70f);
		var p = _fx.ComputePenalties(_body);
		Assert.Equal(_t.ArmDestroyedSwayMultiplier, p.SwayMultiplier, 3);
		Assert.Equal(_t.ArmDestroyedReloadMultiplier, p.ReloadTimeMultiplier, 3);
		Assert.Equal(_t.LegDestroyedSpeedMultiplier, p.MoveSpeedMultiplier, 3);
		Assert.True(p.SprintBlocked);
	}

	[Fact]
	public void FractureAddsLimp()
	{
		_fx.OnHit(Hit(BodyPart.LeftLeg, 40f), () => 0.0);
		var p = _fx.ComputePenalties(_body);
		Assert.True(p.MoveSpeedMultiplier < 1f);
		Assert.False(p.SprintBlocked);
	}

	[Fact]
	public void MedkitHealsMostDamagedPartFirstAndEmptiesPool()
	{
		_body.TakeDamage(BodyPart.Thorax, 30f);
		_body.TakeDamage(BodyPart.RightArm, 10f);
		_fx.StartHealOverTime(35f, 10f);
		_fx.Update(2f, _body);
		Assert.Equal(75f, _body.Hp(BodyPart.Thorax), 3);
		for (var i = 0; i < 10; i++)
			_fx.Update(1f, _body);
		Assert.Equal(85f, _body.Hp(BodyPart.Thorax), 3);
		Assert.Equal(55f, _body.Hp(BodyPart.RightArm), 3); // pool of 35 ran out
		Assert.Equal(0f, _fx.HealPoolLeft, 3);
	}
}

public class MedicalControllerTests
{
	private readonly BodyHealth _body = new(HealthTuning.Default);
	private readonly StatusEffects _fx = new(EffectsTuning.Default);
	private readonly MedicalController _med;

	public MedicalControllerTests() => _med = new MedicalController(_body, _fx);

	private static readonly MedicalSpec Bandage = new(MedicalKind.Bandage, 3f);
	private static readonly MedicalSpec Surgery = new(MedicalKind.SurgeryKit, 20f, RestoredMaxHpFraction: 0.5f);
	private static readonly MedicalSpec Morphine = new(MedicalKind.Morphine, 2f, EffectDurationS: 100f);

	private void Run(float seconds)
	{
		for (var t = 0f; t < seconds; t += 0.05f)
		{
			_med.Update(0.05f);
			_fx.Update(0.05f, _body);
		}
	}

	private static HitResult Hit(BodyPart part, float damage) => new(part, damage, false, true, 0f, null);

	[Fact]
	public void NothingToTreatMeansNoStart()
	{
		Assert.False(_med.Start(Bandage));
		Assert.False(_med.Start(Surgery));
		Assert.False(_med.Start(new MedicalSpec(MedicalKind.Splint, 5f)));
	}

	[Fact]
	public void BandageTakesTimeThenCures()
	{
		_fx.OnHit(Hit(BodyPart.LeftArm, 5f), () => 0.2);
		Assert.True(_med.Start(Bandage));
		Run(1.5f);
		Assert.True(_med.IsBusy);
		Assert.Equal(BleedLevel.Light, _fx.Bleed(BodyPart.LeftArm));
		Run(2f);
		Assert.False(_med.IsBusy);
		Assert.Equal(BleedLevel.None, _fx.Bleed(BodyPart.LeftArm));
	}

	[Fact]
	public void OnlyOneItemAtATime()
	{
		_fx.OnHit(Hit(BodyPart.LeftArm, 5f), () => 0.2);
		Assert.True(_med.Start(Bandage));
		Assert.False(_med.Start(Morphine));
	}

	[Fact]
	public void CancelAbandonsItem()
	{
		_fx.OnHit(Hit(BodyPart.LeftArm, 5f), () => 0.2);
		_med.Start(Bandage);
		Run(1f);
		_med.Cancel();
		Run(5f);
		Assert.Equal(BleedLevel.Light, _fx.Bleed(BodyPart.LeftArm));
	}

	[Fact]
	public void SurgeryRestoresLimbReducedAndNeverVitals()
	{
		_body.TakeDamage(BodyPart.RightLeg, 65f);
		Assert.True(_med.Start(Surgery));
		Run(21f);
		Assert.False(_body.IsDestroyed(BodyPart.RightLeg));
		Assert.Equal(32.5f, _body.Hp(BodyPart.RightLeg), 3);
		Assert.False(_med.CanApply(Surgery, BodyPart.Head));
	}

	[Fact]
	public void MorphineSuppressesPain()
	{
		_fx.OnHit(Hit(BodyPart.Thorax, 80f), () => 0.99);
		Assert.True(_fx.ComputePenalties(_body).PainStrength > 0f);
		_med.Start(Morphine);
		Run(2.5f);
		Assert.True(_fx.PainSuppressed);
		Assert.Equal(0f, _fx.ComputePenalties(_body).PainStrength);
	}

	[Fact]
	public void DeathCancelsUse()
	{
		_fx.OnHit(Hit(BodyPart.LeftArm, 5f), () => 0.2);
		_med.Start(Bandage);
		_body.TakeDamage(BodyPart.Head, 100f);
		Run(1f);
		Assert.False(_med.IsBusy);
	}

	[Fact]
	public void FinishedEventReportsApplied()
	{
		_fx.OnHit(Hit(BodyPart.LeftArm, 5f), () => 0.2);
		var applied = false;
		_med.Finished += (_, ok) => applied = ok;
		_med.Start(Bandage);
		Run(3.5f);
		Assert.True(applied);
	}
}
