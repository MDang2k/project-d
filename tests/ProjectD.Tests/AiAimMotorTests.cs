using System;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class AiAimMotorTests
{
	private static readonly RecoilTuning Rifle = new(1.1f, 0.5f, 0.3f, 0.5f, 0.1f);

	private static AimMotorTuning Skill(float accuracy, float control) => new(
		accuracy, 2f, 0.1f, 1f, 1.8f, 2.5f, 1.2f, 0.3f, control, 0.8f, 1.5f);

	private static AimContext Ctx(float distance = 50f, float own = 0f, float target = 0f, float suppression = 0f)
		=> new(distance, own, target, suppression, 0f, 1f);

	private static AiAimMotor Make(float accuracy, float control, int seed = 5) => new(Skill(accuracy, control), new Random(seed));

	/// <summary>Fire n rounds of full-auto and report the vertical climb left.</summary>
	private static float Climb(float control, int rounds)
	{
		var m = Make(0.5f, control);
		for (var i = 0; i < rounds; i++)
		{
			m.Shot(Rifle, 1f);
			m.Update(0.05f, Ctx(), Rifle);
		}
		return m.Recoil.Y;
	}

	[Fact]
	public void FullAutoClimbsAndPoorControlClimbsMore()
	{
		Assert.True(Climb(0.3f, 20) > Climb(0.7f, 20));
		Assert.True(Climb(0.3f, 20) > Climb(0.3f, 5));
	}

	[Fact]
	public void PerfectControlHoldsTheMuzzleDown()
		=> Assert.True(Climb(1f, 20) < 0.01f);

	[Fact]
	public void ZeroControlClimbsLikeThePlayerWhoDoesNotPullDown()
		=> Assert.Equal(20 * Rifle.VerticalDeg, Climb(0f, 20), 0);

	[Fact]
	public void HoldingAimOnAStillTargetShrinksTheError()
	{
		var m = Make(0.5f, 0.5f);
		m.Update(0.016f, Ctx(), Rifle);
		var fresh = m.ErrorAmplitudeDeg();
		for (var i = 0; i < 200; i++)
			m.Update(0.016f, Ctx(), Rifle);
		Assert.True(m.ErrorAmplitudeDeg() < fresh * 0.5f);
	}

	[Fact]
	public void MovementSuppressionAndDistanceGrowTheError()
	{
		float Amp(AimContext c)
		{
			var m = Make(0.5f, 0.5f);
			for (var i = 0; i < 400; i++)
				m.Update(0.016f, c, Rifle);
			return m.ErrorAmplitudeDeg();
		}
		var calm = Amp(Ctx());
		Assert.True(Amp(Ctx(own: 1f)) > calm);
		Assert.True(Amp(Ctx(target: 1f)) > calm);
		Assert.True(Amp(Ctx(suppression: 1f)) > calm);
		Assert.True(Amp(Ctx(distance: 150f)) > calm);
	}

	[Fact]
	public void BetterAccuracyMeansSmallerError()
	{
		var a = Make(0.2f, 0.5f);
		var b = Make(0.9f, 0.5f);
		a.Update(0.016f, Ctx(), Rifle);
		b.Update(0.016f, Ctx(), Rifle);
		Assert.True(b.ErrorAmplitudeDeg() < a.ErrorAmplitudeDeg());
	}

	[Fact]
	public void TheOffsetWandersSmoothlyNotInJumps()
	{
		var m = Make(0.3f, 0.5f);
		m.Update(0.016f, Ctx(), Rifle);
		var last = m.OffsetDeg();
		for (var i = 0; i < 300; i++)
		{
			m.Update(0.016f, Ctx(), Rifle);
			var now = m.OffsetDeg();
			Assert.True((now - last).Length() < 0.5f);
			last = now;
		}
	}

	[Fact]
	public void DirectionIsTurnedByTheOffsetAndStaysUnit()
	{
		var m = Make(0.5f, 0.5f);
		m.Shot(Rifle, 1f);
		var dir = m.Direction(Vector3.Zero, new Vector3(0f, 0f, -50f));
		Assert.Equal(1f, dir.Length(), 3);
		var expected = m.OffsetDeg();
		Assert.Equal(expected.X, Mathf.RadToDeg(MathF.Atan2(-dir.X, -dir.Z)), 2);
		Assert.Equal(expected.Y, Mathf.RadToDeg(MathF.Asin(dir.Y)), 2);
	}

	[Fact]
	public void LeadAimsAheadOfAMovingTargetAndAboveForDrop()
	{
		var target = new Vector3(0f, 0f, -100f);
		var lead = AiAimMotor.LeadPoint(Vector3.Zero, target, new Vector3(5f, 0f, 0f), 700f, 9.8f, 1f);
		Assert.Equal(5f * (100f / 700f), lead.X, 3);
		Assert.Equal(0.5f * 9.8f * MathF.Pow(100f / 700f, 2f), lead.Y, 3);
		var poor = AiAimMotor.LeadPoint(Vector3.Zero, target, new Vector3(5f, 0f, 0f), 700f, 9.8f, 0.2f);
		Assert.True(poor.X < lead.X);
		Assert.Equal(target, AiAimMotor.LeadPoint(Vector3.Zero, target, Vector3.Zero, 700f, 0f, 1f));
	}

	/// <summary>Share of rounds landing within 0.4 m of a still target 50 m away, bursts of 4 with pauses.</summary>
	private static float HitShare(float accuracy, float control, int seed)
	{
		var m = Make(accuracy, control, seed);
		var hits = 0;
		var total = 0;
		for (var burst = 0; burst < 60; burst++)
		{
			for (var shot = 0; shot < 4; shot++)
			{
				m.Update(0.1f, Ctx(), Rifle);
				m.Shot(Rifle, 1f);
				var o = m.OffsetDeg();
				var miss = 50f * MathF.Tan(Mathf.DegToRad(o.Length()));
				total++;
				if (miss < 0.4f)
					hits++;
			}
			for (var t = 0; t < 12; t++)
				m.Update(0.1f, Ctx(), Rifle);
		}
		return hits / (float)total;
	}

	[Fact]
	public void SkilledBotsHitMoreOftenThanSloppyOnes()
	{
		var looter = HitShare(0.25f, 0.3f, 1);
		var pmc = HitShare(0.75f, 0.7f, 1);
		var boss = HitShare(0.9f, 0.85f, 1);
		Assert.True(looter < pmc, $"looter {looter:0.00} pmc {pmc:0.00} boss {boss:0.00}");
		Assert.True(pmc < boss, $"pmc {pmc:0.00} boss {boss:0.00}");
		Assert.True(looter < 0.2f, $"looter {looter:0.00}");
		Assert.True(pmc > 0.12f, $"pmc {pmc:0.00}");
	}
}
