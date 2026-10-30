using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class AiUtilityTests
{
	private static readonly Temperament Infiltrator = new(0.9f, 0.2f, 0.2f);
	private static readonly Temperament Rat = new(0.1f, 0.95f, 0.3f);
	private static readonly Temperament Operator = new(0.6f, 0.7f, 0.3f);
	private static readonly Temperament Conscript = new(0.4f, 0.5f, 0.8f);

	private static UtilityFacts Facts(bool sees = true, float distance = 40f, float sinceSeen = 0f, float injury = 0f,
		float suppression = 0f, bool cover = true, bool item = false, bool healed = false, bool partner = false,
		bool flank = false, int bursts = 0)
		=> new(sees, distance, 25f, sinceSeen, injury, suppression, cover, item, healed, partner, flank, bursts);

	private static TacticAction Pick(UtilityFacts f, Temperament t, TacticAction current = TacticAction.Hold)
		=> AiUtility.Choose(f, t, current);

	[Fact]
	public void AggressiveBotsPushAndTimidOnesHold()
	{
		Assert.Equal(TacticAction.Push, Pick(Facts(), Infiltrator));
		Assert.Equal(TacticAction.Hold, Pick(Facts(), Rat));
	}

	[Fact]
	public void HeavyFireSendsABotToCoverAndPanickyOnesSooner()
	{
		var fire = Facts(suppression: 0.7f);
		Assert.Equal(TacticAction.SeekCover, Pick(fire, Rat));
		Assert.Equal(TacticAction.SeekCover, Pick(fire, Conscript));
		Assert.NotEqual(TacticAction.SeekCover, Pick(Facts(suppression: 0.25f), Infiltrator));
	}

	[Fact]
	public void NoCoverMeansNoCoverAction()
		=> Assert.NotEqual(TacticAction.SeekCover, Pick(Facts(suppression: 0.9f, cover: false), Rat));

	[Fact]
	public void BadlyHurtBotsRetreatAndCowardsDoSoEarlier()
	{
		Assert.Equal(TacticAction.Retreat, Pick(Facts(injury: 0.7f), Operator));
		var f = Facts(injury: 0.4f);
		Assert.True(AiUtility.Score(TacticAction.Retreat, f, Rat) > AiUtility.Score(TacticAction.Retreat, f, Infiltrator));
	}

	[Fact]
	public void HurtAndUnseenWithAnItemMeansHeal()
		=> Assert.Equal(TacticAction.Heal, Pick(Facts(sees: false, sinceSeen: 20f, injury: 0.5f, item: true, cover: false), Operator));

	[Fact]
	public void FleeNeedsAHurtPanickyBotWithNothingToPatchUpWith()
	{
		var f = Facts(injury: 0.8f, suppression: 0.8f, item: false);
		Assert.Equal(TacticAction.Flee, Pick(f, Conscript));
		Assert.NotEqual(TacticAction.Flee, Pick(f, Operator));
		Assert.NotEqual(TacticAction.Flee, Pick(Facts(injury: 0.8f, suppression: 0.8f, item: true), Conscript));
	}

	[Fact]
	public void FlankingNeedsAPartnerAndAPoint()
	{
		Assert.Equal(0f, AiUtility.Score(TacticAction.Flank, Facts(partner: true), Infiltrator));
		Assert.Equal(0f, AiUtility.Score(TacticAction.Flank, Facts(flank: true), Infiltrator));
		Assert.True(AiUtility.Score(TacticAction.Flank, Facts(partner: true, flank: true), Infiltrator)
			> AiUtility.Score(TacticAction.Flank, Facts(partner: true, flank: true), Rat));
	}

	[Fact]
	public void LosingTheTargetRecentlyMeansSuppress()
		=> Assert.Equal(TacticAction.Suppress, Pick(Facts(sees: false, sinceSeen: 3f, distance: 20f), Operator, TacticAction.Suppress));

	[Fact]
	public void StickinessKeepsTheCurrentActionOnANearTie()
	{
		var found = false;
		for (var injury = 0f; injury <= 1f && !found; injury += 0.01f)
		{
			var f = Facts(injury: injury);
			var ranked = Enum.GetValues<TacticAction>().OrderByDescending(a => AiUtility.Score(a, f, Operator)).ToList();
			var gap = AiUtility.Score(ranked[0], f, Operator) - AiUtility.Score(ranked[1], f, Operator);
			if (gap <= 0f || gap >= AiUtility.Stickiness)
				continue;
			found = true;
			Assert.Equal(ranked[0], Pick(f, Operator, ranked[0]));
			Assert.Equal(ranked[1], Pick(f, Operator, ranked[1])); // the runner-up stays when it is already the plan
		}
		Assert.True(found, "no near tie in the sweep");
	}

	[Fact]
	public void TheStateOfAnActionNeedsNoSwitch()
	{
		Assert.True(AiUtility.Serves(TacticState.Shoot, TacticAction.Push));
		Assert.True(AiUtility.Serves(TacticState.Shoot, TacticAction.Hold));
		Assert.False(AiUtility.Serves(TacticState.Advance, TacticAction.Hold));
		Assert.Equal(TacticState.SeekCover, AiUtility.EntryState(TacticAction.Hold, true));
		Assert.Equal(TacticState.Shoot, AiUtility.EntryState(TacticAction.Hold, false));
		Assert.Equal(TacticState.Flee, AiUtility.EntryState(TacticAction.Flee, true));
	}
}

public class ArchetypeCatalogTests
{
	private static ActorCatalog Real()
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return ActorCatalog.Parse(File.ReadAllText(Path.Combine(dir!, "data/actors/actors.json")));
	}

	[Fact]
	public void EveryActorWeightNamesAnExistingArchetypeSet()
	{
		var c = Real();
		foreach (var a in c.Actors)
			foreach (var entry in a.List("archetypes"))
				Assert.Contains("archetype_" + entry.Split(':')[0], c.SetNames);
	}

	[Fact]
	public void RollingFollowsTheWeightsAndAppliesTheTemperament()
	{
		var c = Real();
		var rng = new Random(11);
		var counts = Enumerable.Range(0, 1000).Select(_ => c.Create("looter", null, null, rng).Archetype)
			.GroupBy(x => x).ToDictionary(g => g.Key!, g => g.Count());
		Assert.InRange(counts["conscript"], 640, 760);
		Assert.InRange(counts["rat"], 240, 360);

		var conscript = c.Create("looter", null, null, new Random(1));
		while (conscript.Archetype != "conscript")
			conscript = c.Create("looter", null, null, new Random(Guid.NewGuid().GetHashCode()));
		Assert.Equal(0.8f, conscript.Stats.Get(Stats.Temper.Panic), 3);
		Assert.Equal(0.4f, conscript.Stats.Get(Stats.Tactics.Aggression), 3);
	}

	[Fact]
	public void CreateWithoutARngRollsNothing()
		=> Assert.Null(Real().Create("looter").Archetype);
}

public class UtilityControlledTacticsTests
{
	private static CombatSituation Sit(bool sees = true, float distance = 20f, float suppression = 0f, float injury = 0f)
		=> new(sees, distance, 0f, injury, suppression, true, false, true, false, false, false);

	[Fact]
	public void BuiltInCrossActionRulesAreOffAndForceWorks()
	{
		var t = new CombatTactics(TacticsTuning.Default with { Courage = 0f }, new Random(1)) { UtilityControlled = true };
		t.Begin();
		for (var i = 0; i < 5; i++)
			t.Update(0.1f, Sit(suppression: 0.9f, injury: 0.9f)); // would retreat or seek cover on its own
		Assert.NotEqual(TacticState.Retreat, t.State);
		Assert.NotEqual(TacticState.SeekCover, t.State);
		t.Force(TacticState.Flee);
		t.Update(0.1f, Sit(suppression: 0.9f));
		Assert.Equal(TacticState.Flee, t.State);
		Assert.True(t.NeedsDecision);
	}
}

public class SquadRoleUtilityTests
{
	private static UtilityFacts Facts(SquadRole role, bool partner = true, bool point = true)
		=> new(true, 30f, 25f, 0f, 0f, 0f, true, false, false, partner, point, 0, role);

	private static readonly Temperament Calm = new(0.3f, 0.7f, 0.3f);

	[Fact]
	public void AFlankerFlanksEvenIfCalmAndAnAnchorNeverDoes()
	{
		Assert.Equal(TacticAction.Flank, AiUtility.Choose(Facts(SquadRole.Flanker), Calm, TacticAction.Hold));
		Assert.Equal(0f, AiUtility.Score(TacticAction.Flank, Facts(SquadRole.Anchor), new Temperament(1f, 0f, 0f)));
		Assert.NotEqual(TacticAction.Flank, AiUtility.Choose(Facts(SquadRole.Anchor), new Temperament(0.9f, 0.2f, 0.2f), TacticAction.Hold));
	}

	[Fact]
	public void AFlankerNeedsARouteButNotAPartnerSignal()
	{
		Assert.True(AiUtility.Score(TacticAction.Flank, Facts(SquadRole.Flanker, partner: false), Calm) > 0f);
		Assert.Equal(0f, AiUtility.Score(TacticAction.Flank, Facts(SquadRole.Flanker, point: false), Calm));
		Assert.Equal(0f, AiUtility.Score(TacticAction.Flank, Facts(SquadRole.None, partner: false), Calm));
	}
}
