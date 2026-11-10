using System;
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class ThrowablesTests
{
	private static readonly GrenadeSpec M67 = new("frag_grenade", 4.5f, 0.3f, 70, 28f, 2, 25f, 0.3f, 4.8f, 60f, 2, 200f);

	// ---- fuse

	[Fact]
	public void FuseSpreadStaysWithinTheStatedRange()
	{
		var rng = new Random(3);
		for (var i = 0; i < 200; i++)
			Assert.InRange(new GrenadeFuse(4.5f, 0.3f, rng).FuseS, 4.2f, 4.8f);
	}

	[Fact]
	public void PinOutThenThrowStartsTheFuseAtTheThrow()
	{
		var f = new GrenadeFuse(4f, 0f, new Random(1));
		Assert.Equal(-1f, f.Release()); // pin still in: nothing to release
		Assert.True(f.PullPin());
		f.Update(10f); // pin out, spoon in: the fuse does not run
		Assert.Equal(GrenadeStage.PinOut, f.Stage);
		Assert.Equal(4f, f.Release());
		Assert.Equal(GrenadeStage.Thrown, f.Stage);
	}

	[Fact]
	public void CookingRunsTheFuseInHandAndReleaseKeepsWhatIsLeft()
	{
		var f = new GrenadeFuse(4f, 0f, new Random(1));
		f.PullPin();
		Assert.True(f.Cook());
		f.Update(1.5f);
		Assert.Equal(2.5f, f.Release(), 3);
	}

	[Fact]
	public void ACookedGrenadeThatRunsOutGoesOffInTheHand()
	{
		var f = new GrenadeFuse(2f, 0f, new Random(1));
		var boom = false;
		f.ExplodedInHand += () => boom = true;
		f.PullPin();
		f.Cook();
		f.Update(1.9f);
		Assert.False(boom);
		f.Update(0.2f);
		Assert.True(boom);
		Assert.Equal(GrenadeStage.Detonated, f.Stage);
	}

	[Fact]
	public void PinBackOnlyBeforeTheSpoonIsReleased()
	{
		var f = new GrenadeFuse(4f, 0f, new Random(1));
		Assert.False(f.PutPinBack());
		f.PullPin();
		Assert.True(f.CanPutAway);
		Assert.True(f.PutPinBack());
		f.PullPin();
		f.Cook();
		Assert.False(f.CanPutAway);
		Assert.False(f.PutPinBack());
		Assert.False(f.PullPin());
	}

	// ---- throw

	[Fact]
	public void WindUpFractionGoesFromSixtyToHundredPercent()
	{
		Assert.Equal(0.6f, ThrowModel.WindUpFraction(0f), 3);
		Assert.Equal(0.8f, ThrowModel.WindUpFraction(0.25f), 3);
		Assert.Equal(1f, ThrowModel.WindUpFraction(0.5f), 3);
		Assert.Equal(1f, ThrowModel.WindUpFraction(3f), 3);
	}

	[Fact]
	public void SpeedByStanceUnderhandStrengthAndItem()
	{
		Assert.Equal(20f, ThrowModel.Speed(Stance.Stand, false, 1f), 3);
		Assert.Equal(17f, ThrowModel.Speed(Stance.Crouch, false, 1f), 3);
		Assert.Equal(10f, ThrowModel.Speed(Stance.Prone, false, 1f), 3);
		Assert.Equal(12f, ThrowModel.Speed(Stance.Stand, false, 0.6f), 3);
		Assert.Equal(6f, ThrowModel.Speed(Stance.Stand, true, 0.6f), 3); // a lob ignores the wind-up
		Assert.Equal(20f * 1.1f, ThrowModel.Speed(Stance.Stand, false, 1f, 0.1f), 3);
		Assert.Equal(20f * 1.25f, ThrowModel.Speed(Stance.Stand, false, 1f, 0f, 1.25f), 3);
	}

	[Fact]
	public void ThrowLeavesTippedUpByTheBias()
	{
		var forward = new Vector3(0f, 0f, -1f);
		var d = ThrowModel.Direction(forward, underhand: false);
		Assert.Equal(15f, Mathf.RadToDeg(Mathf.Asin(d.Y)), 1);
		Assert.True(d.Z < 0f);
		Assert.True(ThrowModel.Direction(forward, true).Y < d.Y);
	}

	// ---- fragments and blast

	[Fact]
	public void FragmentDirectionsAreUnitVectorsSquashedTowardTheHorizontal()
	{
		var dirs = FragmentBurst.Directions(2000, new Random(2), 0.75f);
		Assert.Equal(2000, dirs.Count);
		Assert.All(dirs, d => Assert.Equal(1f, d.Length(), 3));
		var flat = FragmentBurst.Directions(2000, new Random(2), 1f).Average(d => Mathf.Abs(d.Y));
		Assert.True(dirs.Average(d => Mathf.Abs(d.Y)) < flat);
	}

	[Fact]
	public void FragmentDamageFallsLinearlyToThirtyPercent()
	{
		Assert.Equal(1f, FragmentBurst.DamageScale(0f, 25f, 0.3f), 3);
		Assert.Equal(0.65f, FragmentBurst.DamageScale(12.5f, 25f, 0.3f), 3);
		Assert.Equal(0.3f, FragmentBurst.DamageScale(25f, 25f, 0.3f), 3);
		Assert.Equal(0.3f, FragmentBurst.DamageScale(90f, 25f, 0.3f), 3);
	}

	[Fact]
	public void BlastSharesSumToOneAndTotalFallsToZeroAtTheCoreEdge()
	{
		Assert.Equal(1f, BlastModel.Shares.Sum(s => s.Share), 3);
		Assert.Equal(60f, BlastModel.TotalDamage(0f, 4.8f, 60f));
		Assert.Equal(30f, BlastModel.TotalDamage(2.4f, 4.8f, 60f), 3);
		Assert.Equal(0f, BlastModel.TotalDamage(4.8f, 4.8f, 60f));
		Assert.Equal(0f, BlastModel.TotalDamage(10f, 4.8f, 60f));
	}

	[Fact]
	public void BlastHitsSpreadOverTheBodyAndNeedAClearLine()
	{
		var hits = BlastModel.Hits(0f, true, M67);
		Assert.Equal(6, hits.Count);
		Assert.Equal(60f, hits.Sum(h => h.Damage), 3);
		Assert.Equal(18f, hits.First(h => h.Part == BodyPart.Thorax).Damage, 3);
		Assert.All(hits, h => Assert.Equal(2, h.PenetrationTier));
		Assert.Empty(BlastModel.Hits(0f, false, M67)); // a wall in the way
		Assert.Empty(BlastModel.Hits(6f, true, M67)); // outside the core
	}
}
