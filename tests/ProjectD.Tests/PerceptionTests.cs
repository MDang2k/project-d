using Godot;
using Xunit;

namespace ProjectD.Tests;

public class PerceptionModelTests
{
	private static readonly PerceptionTuning T = PerceptionTuning.Default;
	private static readonly TargetProfile Standing = new(Stance.Stand, 0.5f);

	private static float Vision(float distance, float angle, TargetProfile? p = null, float visible = 1f,
		float light = 1f)
		=> PerceptionModel.VisionStrength(T, distance, angle, p ?? Standing, visible, light);

	[Fact]
	public void InConeAndRangeIsSeen()
		=> Assert.True(Vision(20f, 0f) > 0f);

	[Fact]
	public void BeyondViewDistanceIsNotSeen()
		=> Assert.Equal(0f, Vision(200f, 0f));

	[Fact]
	public void OutsideConeIsNotSeenFarAway()
		=> Assert.Equal(0f, Vision(30f, 90f));

	[Fact]
	public void PeripheralCatchesCloseTargetsOnlyAtReducedStrength()
	{
		var close = Vision(5f, 90f);
		Assert.True(close > 0f);
		Assert.True(close < Vision(5f, 0f));
		Assert.Equal(0f, Vision(20f, 90f));
	}

	[Fact]
	public void CloserIsStronger()
		=> Assert.True(Vision(10f, 0f) > Vision(60f, 0f));

	[Fact]
	public void CrouchingAndProneShrinkRange()
	{
		var stand = Vision(50f, 0f, new TargetProfile(Stance.Stand, 0f));
		var crouch = Vision(50f, 0f, new TargetProfile(Stance.Crouch, 0f));
		var prone = Vision(50f, 0f, new TargetProfile(Stance.Prone, 0f));
		Assert.True(stand > crouch);
		Assert.True(crouch >= prone);
		Assert.Equal(0f, prone);
	}

	[Fact]
	public void MovingIsMoreVisibleThanStill()
		=> Assert.True(Vision(60f, 0f, new TargetProfile(Stance.Stand, 1f))
			> Vision(60f, 0f, new TargetProfile(Stance.Stand, 0f)));

	[Fact]
	public void CoverAndDarknessReduceSight()
	{
		Assert.Equal(0f, Vision(20f, 0f, visible: 0f));
		Assert.True(Vision(20f, 0f, visible: 0.4f) < Vision(20f, 0f));
		Assert.True(Vision(60f, 0f, light: 0.3f) < Vision(60f, 0f));
	}

	[Fact]
	public void LoudnessFallsWithDistance()
	{
		Assert.Equal(1f, PerceptionModel.Loudness(T, 0f, 20f), 3);
		Assert.Equal(0.5f, PerceptionModel.Loudness(T, 10f, 20f), 3);
		Assert.Equal(0f, PerceptionModel.Loudness(T, 25f, 20f));
	}

	[Fact]
	public void HearingMultiplierExtendsReach()
	{
		var keen = T with { HearingMultiplier = 2f };
		Assert.Equal(0f, PerceptionModel.Loudness(T, 30f, 20f));
		Assert.True(PerceptionModel.Loudness(keen, 30f, 20f) > 0f);
	}

	[Fact]
	public void ClearCloseSightingBecomesAlertWithinNoticeTime()
	{
		var m = new PerceptionModel(T);
		var seen = new Vector3(0f, 0f, -3f);
		for (var i = 0; i < 20; i++)
		{
			m.See(1f, seen, 0.1f);
			m.Tick(0.1f);
		}
		Assert.Equal(AwarenessLevel.Alert, m.Level);
		Assert.True(m.SeesTarget);
		Assert.Equal(seen, m.InterestPoint);
	}

	[Fact]
	public void WeakSightingIsSlower()
	{
		var strong = new PerceptionModel(T);
		var weak = new PerceptionModel(T);
		strong.See(1f, Vector3.Zero, 0.5f);
		weak.See(0.1f, Vector3.Zero, 0.5f);
		Assert.True(strong.Awareness > weak.Awareness);
	}

	[Fact]
	public void LoudNoiseAlertsAtOnceQuietOneOnlySuspicious()
	{
		var loud = new PerceptionModel(T);
		loud.Hear(1f, new Vector3(5f, 0f, 0f));
		Assert.Equal(AwarenessLevel.Alert, loud.Level);

		var quiet = new PerceptionModel(T);
		quiet.Hear(0.1f, new Vector3(5f, 0f, 0f));
		Assert.Equal(AwarenessLevel.Suspicious, quiet.Level);
	}

	[Fact]
	public void BarelyAudibleNoiseStaysBelowSuspicion()
	{
		var m = new PerceptionModel(T with { SuspicionThreshold = 0.35f });
		m.Hear(0.0001f, Vector3.Zero);
		Assert.Equal(AwarenessLevel.Suspicious, m.Level);
	}

	[Fact]
	public void AwarenessHoldsThenFadesAndForgets()
	{
		var m = new PerceptionModel(T);
		m.Hear(0.5f, new Vector3(1f, 0f, 1f));
		m.Tick(0.1f); // consumes the cue tick
		var start = m.Awareness;
		for (var i = 0; i < 20; i++)
			m.Tick(0.1f); // 2 s, still inside the hold time
		Assert.Equal(start, m.Awareness, 3);
		for (var i = 0; i < 400; i++)
			m.Tick(0.1f);
		Assert.Equal(0f, m.Awareness);
		Assert.Null(m.InterestPoint);
		Assert.Equal(AwarenessLevel.Unaware, m.Level);
	}

	[Fact]
	public void AlertDropsBackToSuspiciousAfterFading()
	{
		var m = new PerceptionModel(T);
		m.Hear(1f, Vector3.Zero);
		m.Tick(0.1f);
		var sawSuspicious = false;
		for (var i = 0; i < 400; i++)
		{
			m.Tick(0.1f);
			if (m.Level == AwarenessLevel.Suspicious)
				sawSuspicious = true;
		}
		Assert.True(sawSuspicious);
		Assert.Equal(AwarenessLevel.Unaware, m.Level);
	}

	[Fact]
	public void ResetClearsState()
	{
		var m = new PerceptionModel(T);
		m.Hear(1f, Vector3.One);
		m.Reset();
		Assert.Equal(0f, m.Awareness);
		Assert.Null(m.InterestPoint);
		Assert.Equal(AwarenessLevel.Unaware, m.Level);
	}
}

public class FactionRelationsTests
{
	[Theory]
	[InlineData(Faction.Looters, Faction.Looters, false)]
	[InlineData(Faction.Looters, Faction.Bandits, true)]
	[InlineData(Faction.Bandits, Faction.Bandits, false)]
	[InlineData(Faction.Bandits, Faction.AlliedPmc, true)]
	[InlineData(Faction.AlliedPmc, Faction.AlliedPmc, false)]
	[InlineData(Faction.AlliedPmc, Faction.SovietPmc, true)]
	[InlineData(Faction.SovietPmc, Faction.Looters, true)]
	[InlineData(Faction.SovietPmc, Faction.SovietPmc, false)]
	public void MatchesGddMatrix(Faction a, Faction b, bool hostile)
	{
		Assert.Equal(hostile, FactionRelations.IsHostile(a, b));
		Assert.Equal(hostile, FactionRelations.IsHostile(b, a));
	}
}
