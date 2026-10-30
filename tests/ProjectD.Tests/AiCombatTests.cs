using System;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class AiAimTests
{
	[Fact]
	public void JitterStaysInsideCone()
	{
		var rng = new Random(7);
		var dir = new Vector3(0.2f, 0.1f, -1f).Normalized();
		for (var i = 0; i < 200; i++)
		{
			var j = AiAim.Jitter(dir, 3f, () => (float)rng.NextDouble());
			var angle = Mathf.RadToDeg(dir.AngleTo(j));
			Assert.True(angle <= 3.001f);
			Assert.Equal(1f, j.Length(), 3);
		}
	}

	[Fact]
	public void ZeroSpreadIsExact()
	{
		var dir = new Vector3(0f, 0f, -1f);
		Assert.Equal(dir, AiAim.Jitter(dir, 0f, () => 0.5f));
	}
}

public class AiBurstFireTests
{
	private static AiBurstFire Make(int seed = 1)
		=> new(600f, 3, 3, 0.5f, 0.5f, 0.4f, new Random(seed));

	[Fact]
	public void WaitsForReactionThenFires()
	{
		var f = Make();
		var firedAt = -1f;
		for (var t = 0f; t < 2f; t += 0.02f)
		{
			f.Update(0.02f, true);
			if (f.Firing && firedAt < 0f)
				firedAt = t;
		}
		Assert.InRange(firedAt, 0.38f, 0.5f);
	}

	[Fact]
	public void BurstsAreSeparatedByPauses()
	{
		var f = Make();
		var finished = 0;
		f.BurstFinished += () => finished++;
		var firingFrames = 0;
		var total = 0;
		for (var t = 0f; t < 3f; t += 0.02f)
		{
			f.Update(0.02f, true);
			total++;
			if (f.Firing)
				firingFrames++;
		}
		Assert.True(finished >= 2);
		Assert.True(firingFrames < total * 0.6f);
	}

	[Fact]
	public void StopsImmediatelyWhenNotWanted()
	{
		var f = Make();
		for (var i = 0; i < 40; i++)
			f.Update(0.02f, true);
		f.Update(0.02f, false);
		Assert.False(f.Firing);
	}
}

public class CombatTacticsTests
{
	private static CombatSituation Sit(bool sees = true, float distance = 20f, float sinceSeen = 0f,
		float injury = 0f, float suppression = 0f, bool cover = true, bool at = false, bool item = true,
		bool healed = false, bool partner = false, bool flank = false)
		=> new(sees, distance, sinceSeen, injury, suppression, cover, at, item, healed, partner, flank);

	private static CombatTactics Make(float courage = 0.5f, float aggression = 0.5f, int seed = 3)
	{
		var t = new CombatTactics(TacticsTuning.Default with { Courage = courage, Aggression = aggression },
			new Random(seed));
		t.Begin();
		return t;
	}

	private static void Run(CombatTactics t, CombatSituation s, float seconds)
	{
		for (var x = 0f; x < seconds; x += 0.1f)
			t.Update(0.1f, s);
	}

	[Fact]
	public void AdvancesThenShootsInRange()
	{
		var t = Make();
		Run(t, Sit(distance: 60f), 0.2f);
		Assert.Equal(TacticState.Advance, t.State);
		Run(t, Sit(distance: 20f), 0.2f);
		Assert.Equal(TacticState.Shoot, t.State);
	}

	[Fact]
	public void TakesCoverAfterEnoughBursts()
	{
		var t = Make();
		Run(t, Sit(), 0.2f);
		for (var i = 0; i < 3; i++)
			t.OnBurstFinished();
		Run(t, Sit(), 0.1f);
		Assert.Equal(TacticState.SeekCover, t.State);
		Run(t, Sit(at: true), 0.1f);
		Assert.Equal(TacticState.Hide, t.State);
	}

	[Fact]
	public void CoverIsOnlyUsedWhenAvailable()
	{
		var t = Make();
		Run(t, Sit(cover: false), 0.2f);
		for (var i = 0; i < 5; i++)
			t.OnBurstFinished();
		Run(t, Sit(cover: false), 0.2f);
		Assert.Equal(TacticState.Shoot, t.State);
	}

	[Fact]
	public void HidePeekCycleRepeats()
	{
		var t = Make();
		Run(t, Sit(), 0.2f);
		for (var i = 0; i < 3; i++)
			t.OnBurstFinished();
		Run(t, Sit(), 0.1f);
		Run(t, Sit(at: true), 0.1f);
		for (var i = 0; i < 80 && t.State == TacticState.Hide; i++)
			t.Update(0.1f, Sit(sees: false, at: true)); // hide timer runs out
		Assert.Equal(TacticState.Peek, t.State);
		t.OnBurstFinished();
		t.OnBurstFinished();
		Run(t, Sit(), 0.1f);
		Assert.Equal(TacticState.Hide, t.State);
	}

	[Fact]
	public void HeavyFireSendsAdvancingAiToCover()
	{
		var t = Make(courage: 0.2f);
		Run(t, Sit(distance: 60f, suppression: 0.6f), 0.2f);
		Assert.Equal(TacticState.SeekCover, t.State);
	}

	[Fact]
	public void BraveAiIgnoresModerateFire()
	{
		var t = Make(courage: 1f);
		Run(t, Sit(distance: 20f, suppression: 0.5f), 0.3f);
		Assert.Equal(TacticState.Shoot, t.State);
	}

	[Fact]
	public void BadlyHurtRetreatsThenHeals()
	{
		var t = Make();
		Run(t, Sit(injury: 0.7f), 0.2f);
		Assert.Equal(TacticState.Retreat, t.State);
		Run(t, Sit(injury: 0.7f, sees: false, at: true), 0.2f);
		Assert.Equal(TacticState.Heal, t.State);
		Run(t, Sit(injury: 0.3f, sees: false, healed: true), 0.2f);
		Assert.Equal(TacticState.Hide, t.State);
	}

	[Fact]
	public void RetreatDoesNotFlickerBackToBackWithHide()
	{
		var t = Make(courage: 0f);
		Run(t, Sit(injury: 0.9f), 0.2f);
		Run(t, Sit(injury: 0.9f, sees: false, at: true, item: false), 0.2f);
		Assert.Equal(TacticState.Hide, t.State);
		Run(t, Sit(injury: 0.9f, sees: false, at: true, item: false), 3f);
		Assert.NotEqual(TacticState.Retreat, t.State);
	}

	[Fact]
	public void CowardRetreatsEarlierThanBraveAi()
	{
		var coward = Make(courage: 0f);
		var brave = Make(courage: 1f);
		Run(coward, Sit(injury: 0.45f), 0.2f);
		Run(brave, Sit(injury: 0.45f), 0.2f);
		Assert.Equal(TacticState.Retreat, coward.State);
		Assert.NotEqual(TacticState.Retreat, brave.State);
	}

	[Fact]
	public void NoCoverMeansFightOn()
	{
		var t = Make();
		Run(t, Sit(injury: 0.9f, cover: false), 0.3f);
		Assert.NotEqual(TacticState.Retreat, t.State);
	}

	[Fact]
	public void LosingSightWhileShootingSuppressesLastKnownPosition()
	{
		var t = Make();
		Run(t, Sit(), 0.2f);
		Run(t, Sit(sees: false, sinceSeen: 2f), 1.5f);
		Assert.Equal(TacticState.Suppress, t.State);
		Run(t, Sit(), 0.1f);
		Assert.Equal(TacticState.Shoot, t.State);
	}

	[Fact]
	public void AggressivePairMemberFlanks()
	{
		var t = Make(aggression: 1f);
		Run(t, Sit(distance: 24f), 0.2f);
		Run(t, Sit(distance: 24f, partner: true, flank: true), 0.2f);
		Assert.Equal(TacticState.Flank, t.State);
		Run(t, Sit(sees: false, distance: 24f, at: true), 0.1f);
		Assert.Equal(TacticState.Shoot, t.State);
	}

	[Fact]
	public void PassiveAiDoesNotFlankEveryTime()
	{
		var flanked = 0;
		for (var seed = 0; seed < 20; seed++)
		{
			var t = Make(aggression: 0.1f, seed: seed);
			Run(t, Sit(distance: 24f), 0.2f);
			Run(t, Sit(distance: 24f, partner: true, flank: true), 0.5f);
			if (t.State == TacticState.Flank)
				flanked++;
		}
		Assert.InRange(flanked, 0, 8);
	}
}
