using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class BodyHealthTests
{
	private static BodyHealth Make() => new(HealthTuning.Default);

	[Fact]
	public void StartsAtMax()
	{
		var h = Make();
		Assert.Equal(35f, h.Hp(BodyPart.Head));
		Assert.Equal(85f, h.Hp(BodyPart.Thorax));
		Assert.Equal(60f, h.Hp(BodyPart.LeftArm));
		Assert.Equal(65f, h.Hp(BodyPart.RightLeg));
	}

	[Fact]
	public void HeadAtZeroKills()
	{
		var h = Make();
		BodyPart? cause = null;
		h.Died += p => cause = p;
		h.TakeDamage(BodyPart.Head, 40f);
		Assert.True(h.IsDead);
		Assert.Equal(BodyPart.Head, cause);
	}

	[Fact]
	public void ThoraxAtZeroKills()
	{
		var h = Make();
		h.TakeDamage(BodyPart.Thorax, 85f);
		Assert.True(h.IsDead);
	}

	[Fact]
	public void LimbDestroyedDoesNotKill()
	{
		var h = Make();
		BodyPart? destroyed = null;
		h.LimbDestroyed += p => destroyed = p;
		h.TakeDamage(BodyPart.LeftLeg, 70f);
		Assert.False(h.IsDead);
		Assert.True(h.IsDestroyed(BodyPart.LeftLeg));
		Assert.Equal(BodyPart.LeftLeg, destroyed);
	}

	[Fact]
	public void DamageToDestroyedLimbSpreadsExceptHead()
	{
		var h = Make();
		h.TakeDamage(BodyPart.LeftArm, 60f);
		var before = BodyHealth.AllParts.ToDictionary(p => p, h.Hp);
		h.TakeDamage(BodyPart.LeftArm, 30f);
		Assert.Equal(before[BodyPart.Head], h.Hp(BodyPart.Head));
		Assert.Equal(0f, h.Hp(BodyPart.LeftArm));
		var lost = BodyHealth.AllParts.Sum(p => before[p] - h.Hp(p));
		Assert.Equal(30f * 0.6f, lost, 3);
	}

	[Fact]
	public void SpreadCanKillThroughThorax()
	{
		var h = Make();
		h.TakeDamage(BodyPart.LeftArm, 60f);
		h.TakeDamage(BodyPart.RightArm, 60f);
		h.TakeDamage(BodyPart.LeftLeg, 65f);
		h.TakeDamage(BodyPart.RightLeg, 65f);
		h.TakeDamage(BodyPart.Stomach, 70f);
		Assert.False(h.IsDead);
		h.TakeDamage(BodyPart.Stomach, 500f);
		Assert.True(h.IsDead);
		Assert.Equal(BodyPart.Thorax, h.CauseOfDeath);
	}

	[Fact]
	public void HealClampsAndSkipsDestroyed()
	{
		var h = Make();
		h.TakeDamage(BodyPart.Thorax, 20f);
		h.Heal(BodyPart.Thorax, 100f);
		Assert.Equal(85f, h.Hp(BodyPart.Thorax));
		h.TakeDamage(BodyPart.RightArm, 60f);
		h.Heal(BodyPart.RightArm, 30f);
		Assert.Equal(0f, h.Hp(BodyPart.RightArm));
	}

	[Fact]
	public void RestoreBringsLimbBackReduced()
	{
		var h = Make();
		h.TakeDamage(BodyPart.RightLeg, 65f);
		Assert.True(h.Restore(BodyPart.RightLeg, 0.5f));
		Assert.Equal(32.5f, h.Hp(BodyPart.RightLeg), 3);
		h.Heal(BodyPart.RightLeg, 100f);
		Assert.Equal(32.5f, h.Hp(BodyPart.RightLeg), 3);
		Assert.False(h.Restore(BodyPart.Head, 0.5f));
	}

	[Fact]
	public void DeadTakesNoMoreDamage()
	{
		var h = Make();
		h.TakeDamage(BodyPart.Head, 100f);
		var hp = h.Hp(BodyPart.Thorax);
		h.TakeDamage(BodyPart.Thorax, 30f);
		Assert.Equal(hp, h.Hp(BodyPart.Thorax));
	}
}

public class DamageResolverTests
{
	private static readonly DamageTuning T = DamageTuning.Default;
	private static readonly AmmoType Ak = new("ak", 55f, 3, 715f);
	private static readonly AmmoType Pistol = new("9x19", 28f, 1, 360f);

	private static ArmorState Vest(int tier, float durability = 60f)
		=> new(new ArmorSpec("vest", tier, durability, new[] { BodyPart.Thorax, BodyPart.Stomach }));

	[Fact]
	public void NoArmorFullDamage()
	{
		var r = DamageResolver.Resolve(Ak, BodyPart.Thorax, new ArmorState[0], T);
		Assert.Equal(55f, r.Damage);
		Assert.False(r.ArmorHit);
	}

	[Fact]
	public void UnprotectedZoneIgnoresArmor()
	{
		var vest = Vest(2);
		var r = DamageResolver.Resolve(Ak, BodyPart.LeftArm, new[] { vest }, T);
		Assert.False(r.ArmorHit);
		Assert.Equal(60f, vest.Durability);
	}

	[Fact]
	public void PenetrationReducesDamageAndWearsArmor()
	{
		var vest = Vest(2);
		var r = DamageResolver.Resolve(Ak, BodyPart.Thorax, new[] { vest }, T);
		Assert.True(r.ArmorHit);
		Assert.True(r.Penetrated);
		Assert.Equal(55f * 0.8f, r.Damage, 3);
		Assert.Equal(5.5f, r.DurabilityLoss, 3);
		Assert.Equal(54.5f, vest.Durability, 3);
	}

	[Fact]
	public void SameTierPenetrates()
	{
		var r = DamageResolver.Resolve(Ak, BodyPart.Thorax, new[] { Vest(3) }, T);
		Assert.True(r.Penetrated);
	}

	[Fact]
	public void LowerTierIsStoppedAsBlunt()
	{
		var vest = Vest(2);
		var r = DamageResolver.Resolve(Pistol, BodyPart.Thorax, new[] { vest }, T);
		Assert.True(r.ArmorHit);
		Assert.False(r.Penetrated);
		Assert.Equal(28f * 0.25f, r.Damage, 3);
		Assert.Equal(28f * 0.08f, vest.Durability == 60f ? 0f : 60f - vest.Durability, 3);
	}

	[Fact]
	public void UnmitigatedDamageSkipsArmorAndWear()
	{
		var vest = Vest(4);
		var r = DamageResolver.Unmitigated(BodyPart.Thorax, 30f);
		Assert.Equal(30f, r.Damage);
		Assert.False(r.ArmorHit);
		Assert.Equal(60f, vest.Durability);
	}

	[Fact]
	public void BrokenArmorActsAsTierZero()
	{
		var vest = Vest(4, durability: 1f);
		DamageResolver.Resolve(Ak, BodyPart.Thorax, new[] { vest }, T);
		Assert.Equal(0f, vest.Durability);
		Assert.Equal(0, vest.EffectiveTier);
		var r = DamageResolver.Resolve(Pistol, BodyPart.Thorax, new[] { vest }, T);
		Assert.False(r.ArmorHit);
		Assert.Equal(28f, r.Damage);
	}
}
