using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class MineRulesTests
{
	private static readonly MineTuning T = new();
	private static readonly Rect2 Field = new(100f, 50f, 38f, 80f);
	private static readonly IReadOnlyList<Vector2> Lane = new[] { new Vector2(110f, 50f), new Vector2(110f, 80f), new Vector2(125f, 100f), new Vector2(125f, 130f) };

	private static List<Vector2> Place(int seed, params IReadOnlyList<Vector2>[] lanes) => MineFieldPlanner.Place(Field, lanes, T, new Random(seed));

	[Fact]
	public void MinesStayInsideTheFieldAwayFromTheEdge()
	{
		var mines = Place(1);
		Assert.NotEmpty(mines);
		var inner = Field.GrowIndividual(-T.EdgeMarginM, -T.EdgeMarginM, -T.EdgeMarginM, -T.EdgeMarginM);
		Assert.All(mines, m => Assert.True(inner.HasPoint(m), $"{m} outside"));
	}

	[Fact]
	public void NoMineSitsInsideASafeLane()
	{
		for (var seed = 1; seed <= 20; seed++)
			Assert.All(Place(seed, Lane), m => Assert.True(MineFieldPlanner.DistanceToPolyline(m, Lane) >= T.LaneHalfWidthM, $"seed {seed}: {m}"));
	}

	[Fact]
	public void WithoutLanesTheFieldIsDenserThanWith()
	{
		var open = Enumerable.Range(1, 20).Sum(s => Place(s).Count);
		var laned = Enumerable.Range(1, 20).Sum(s => Place(s, Lane).Count);
		Assert.True(laned < open);
	}

	[Fact]
	public void DensityMatchesTheCellsAndFillChance()
	{
		var cells = Mathf.Ceil(Field.Size.X / T.SpacingM) * Mathf.Ceil(Field.Size.Y / T.SpacingM);
		var average = Enumerable.Range(1, 40).Average(s => Place(s).Count);
		Assert.InRange(average, cells * T.FillChance * 0.6f, cells * T.FillChance);
	}

	[Fact]
	public void SameSeedSameField()
		=> Assert.Equal(Place(7, Lane), Place(7, Lane));

	[Fact]
	public void DifferentSeedsGiveDifferentFields()
		=> Assert.NotEqual(Place(7, Lane), Place(8, Lane));

	[Fact]
	public void NoFillMeansNoMines()
		=> Assert.Empty(MineFieldPlanner.Place(Field, Array.Empty<IReadOnlyList<Vector2>>(), T with { FillChance = 0f }, new Random(1)));

	[Fact]
	public void DistanceToAPolylineUsesTheNearestSegment()
	{
		var line = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) };
		Assert.Equal(3f, MineFieldPlanner.DistanceToPolyline(new Vector2(5, 3), line), 3);
		Assert.Equal(2f, MineFieldPlanner.DistanceToPolyline(new Vector2(12, 5), line), 3);
		Assert.Equal(5f, MineFieldPlanner.DistanceToPolyline(new Vector2(-3, 4), line), 3); // off the end: nearest point is the start
		Assert.Equal(5f, MineFieldPlanner.DistanceToPolyline(new Vector2(3, 4), new[] { new Vector2(0, 0) }), 3);
		Assert.True(float.IsPositiveInfinity(MineFieldPlanner.DistanceToPolyline(Vector2.Zero, Array.Empty<Vector2>())));
	}

	[Fact]
	public void SignsStandOnTheEdgeFacingOut()
	{
		var signs = MineFieldPlanner.Signs(Field, T with { SignMissingChance = 0f }, new Random(1));
		Assert.NotEmpty(signs);
		foreach (var (pos, yaw) in signs)
		{
			var onEdge = Mathf.IsEqualApprox(pos.X, Field.Position.X) || Mathf.IsEqualApprox(pos.X, Field.End.X)
				|| Mathf.IsEqualApprox(pos.Y, Field.Position.Y) || Mathf.IsEqualApprox(pos.Y, Field.End.Y);
			Assert.True(onEdge, $"{pos} not on the edge");
			// the sign's forward (-Z) rotated by yaw must point away from the field centre
			var r = Mathf.DegToRad(yaw);
			var forward = new Vector2(-Mathf.Sin(r), -Mathf.Cos(r));
			Assert.True(forward.Dot(pos - Field.GetCenter()) > 0f, $"{pos} faces inward (yaw {yaw})");
		}
	}

	[Fact]
	public void SomeSignsGoMissing()
	{
		var all = MineFieldPlanner.Signs(Field, T with { SignMissingChance = 0f }, new Random(1)).Count;
		var none = MineFieldPlanner.Signs(Field, T with { SignMissingChance = 1f }, new Random(1)).Count;
		var some = Enumerable.Range(1, 30).Average(s => MineFieldPlanner.Signs(Field, T, new Random(s)).Count);
		Assert.Equal(0, none);
		Assert.InRange(some, all * 0.4, all * 0.95);
	}

	[Fact]
	public void ABlastHitsBothLegsTheStomachAndTheThorax()
	{
		var plan = MineBlast.Plan(T, new Random(1));
		Assert.Equal(4, plan.Hits.Count);
		Assert.Equal(T.LegDamage, plan.Hits[0].Damage);
		Assert.Equal(plan.SteppedLeg, plan.Hits[0].Part);
		var other = plan.SteppedLeg == BodyPart.LeftLeg ? BodyPart.RightLeg : BodyPart.LeftLeg;
		Assert.Equal(other, plan.Hits[1].Part);
		Assert.Contains(plan.Hits, h => h.Part == BodyPart.Stomach);
		Assert.Contains(plan.Hits, h => h.Part == BodyPart.Thorax);
		Assert.All(plan.Hits, h => Assert.Equal(T.PenetrationTier, h.PenetrationTier));
	}

	[Fact]
	public void EitherLegCanBeTheOneSteppedOn()
	{
		var legs = Enumerable.Range(1, 40).Select(s => MineBlast.Plan(T, new Random(s)).SteppedLeg).Distinct().ToList();
		Assert.Equal(2, legs.Count);
	}

	[Fact]
	public void FractureAndHeavyBleedAreLikelyButNotCertain()
	{
		var plans = Enumerable.Range(1, 400).Select(s => MineBlast.Plan(T, new Random(s))).ToList();
		var fractures = plans.Count(p => p.Fracture) / 400.0;
		var bleeds = plans.Count(p => p.HeavyBleed) / 400.0;
		Assert.InRange(fractures, 0.75, 0.95);
		Assert.InRange(bleeds, 0.6, 0.8);
		Assert.Contains(plans, p => !p.Fracture);
	}

	[Fact]
	public void AMineBreaksAndBleedsALegThroughTheStatusEffects()
	{
		var body = new BodyHealth(HealthTuning.Default);
		var fx = new StatusEffects(EffectsTuning.Default);
		var plan = new BlastPlan(new[] { new BlastHit(BodyPart.LeftLeg, 55f, 3) }, BodyPart.LeftLeg, true, true);
		body.TakeDamage(BodyPart.LeftLeg, 55f);
		fx.Inflict(plan.SteppedLeg, plan.Fracture, BleedLevel.Heavy, body);
		Assert.True(fx.IsFractured(BodyPart.LeftLeg));
		Assert.Equal(BleedLevel.Heavy, fx.Bleed(BodyPart.LeftLeg));
		Assert.True(fx.Pain > 0f);
	}

	[Fact]
	public void ADestroyedLegCannotBeInjuredFurther()
	{
		var body = new BodyHealth(HealthTuning.Default);
		var fx = new StatusEffects(EffectsTuning.Default);
		body.TakeDamage(BodyPart.RightLeg, 500f);
		fx.Inflict(BodyPart.RightLeg, true, BleedLevel.Heavy, body);
		Assert.False(fx.IsFractured(BodyPart.RightLeg));
		Assert.Equal(BleedLevel.None, fx.Bleed(BodyPart.RightLeg));
	}

	[Fact]
	public void InflictingNothingChangesNothingAndTorsoCannotFracture()
	{
		var body = new BodyHealth(HealthTuning.Default);
		var fx = new StatusEffects(EffectsTuning.Default);
		fx.Inflict(BodyPart.LeftLeg, false, BleedLevel.None, body);
		fx.Inflict(BodyPart.Thorax, true, BleedLevel.None, body);
		Assert.Empty(fx.Fractures);
		Assert.Empty(fx.BleedingParts);
		Assert.Equal(0f, fx.Pain);
	}

	[Fact]
	public void ABlastGoingThroughArmorWearsIt()
	{
		var vest = new ArmorState(new ArmorSpec("vest", 2, 40f, new[] { BodyPart.Thorax, BodyPart.Stomach }));
		var tuning = DamageTuning.Default;
		var hit = DamageResolver.Resolve(T.ThoraxDamage, T.PenetrationTier, BodyPart.Thorax, new[] { vest }, tuning);
		Assert.True(hit.ArmorHit);
		Assert.True(hit.Penetrated); // a mine outclasses tier 2
		Assert.True(vest.Durability < 40f);
	}
}
