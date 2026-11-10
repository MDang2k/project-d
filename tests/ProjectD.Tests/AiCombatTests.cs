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

	private static CombatTactics Make(int seed = 3)
	{
		var t = new CombatTactics(TacticsTuning.Default, new Random(seed));
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
	public void CoverLeadsToAHidePeekCycle()
	{
		var t = Make();
		t.Force(TacticState.SeekCover);
		Run(t, Sit(at: true), 0.1f);
		Assert.Equal(TacticState.Hide, t.State);
		for (var i = 0; i < 80 && t.State == TacticState.Hide; i++)
			t.Update(0.1f, Sit(sees: false, at: true)); // hide timer runs out
		Assert.Equal(TacticState.Peek, t.State);
		t.OnBurstFinished();
		t.OnBurstFinished();
		Run(t, Sit(), 0.1f);
		Assert.Equal(TacticState.Hide, t.State);
	}

	[Fact]
	public void RetreatThenHealsThenHides()
	{
		var t = Make();
		t.Force(TacticState.Retreat);
		Run(t, Sit(injury: 0.7f, sees: false, at: true), 0.2f);
		Assert.Equal(TacticState.Heal, t.State);
		Run(t, Sit(injury: 0.3f, sees: false, healed: true), 0.2f);
		Assert.Equal(TacticState.Hide, t.State);
	}

	[Fact]
	public void SuppressShootsOnSightAndHidesWhenDone()
	{
		var t = Make();
		t.Force(TacticState.Suppress);
		Run(t, Sit(), 0.1f);
		Assert.Equal(TacticState.Shoot, t.State);
		t.Force(TacticState.Suppress);
		Run(t, Sit(sees: false), TacticsTuning.Default.SuppressS + 0.2f);
		Assert.Equal(TacticState.Hide, t.State);
	}

	[Fact]
	public void AFlankEndsInShootingOnArrival()
	{
		var t = Make();
		t.Force(TacticState.Flank);
		Run(t, Sit(sees: false, distance: 24f, at: true), 0.1f);
		Assert.Equal(TacticState.Shoot, t.State);
	}
}
