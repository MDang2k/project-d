using System;
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class AiGrenadeTests
{
	private static AiGrenadeInputs Ok(string archetype = "infiltrator") => new(true, 2f, 20f, 4f, true, false, 0f, archetype);

	[Fact]
	public void AllSpecConditionsMustHold()
	{
		Assert.True(AiGrenadeDecision.Eligible(Ok()));
		Assert.False(AiGrenadeDecision.Eligible(Ok() with { HasGrenade = false }));
		Assert.False(AiGrenadeDecision.Eligible(Ok() with { SecondsSinceTargetKnown = 9f }));
		Assert.False(AiGrenadeDecision.Eligible(Ok() with { DistanceM = 7f }));
		Assert.False(AiGrenadeDecision.Eligible(Ok() with { DistanceM = 36f }));
		Assert.False(AiGrenadeDecision.Eligible(Ok() with { NoLineOfFireS = 2.9f })); // it can still shoot them
		Assert.False(AiGrenadeDecision.Eligible(Ok() with { ArcClear = false }));
		Assert.False(AiGrenadeDecision.Eligible(Ok() with { SquadmateNearLanding = true }));
		Assert.False(AiGrenadeDecision.Eligible(Ok() with { CooldownLeftS = 5f }));
	}

	[Fact]
	public void InfiltratorsThrowOftenConscriptsRarelyRatsAlmostNever()
	{
		static int Throws(string archetype, bool cornered = false)
		{
			var rng = new Random(4);
			return Enumerable.Range(0, 2000).Count(_ => AiGrenadeDecision.ShouldThrow(Ok(archetype) with { Cornered = cornered }, rng));
		}
		Assert.True(Throws("infiltrator") > Throws("operator"));
		Assert.True(Throws("operator") > Throws("conscript"));
		Assert.True(Throws("conscript") > Throws("rat"));
		Assert.True(Throws("rat", cornered: true) > 500); // cornered, a rat does throw
	}

	[Fact]
	public void IneligibleNeverThrowsWhateverTheArchetype()
	{
		var rng = new Random(1);
		Assert.All(Enumerable.Range(0, 500), _ => Assert.False(AiGrenadeDecision.ShouldThrow(Ok() with { ArcClear = false }, rng)));
	}

	[Fact]
	public void CookingAndErrorByArchetype()
	{
		var rng = new Random(2);
		Assert.Equal(1f, AiGrenadeDecision.CookS("infiltrator", rng));
		Assert.Equal(0f, AiGrenadeDecision.CookS("conscript", rng));
		Assert.True(AiGrenadeDecision.ErrorRadiusM("conscript") > AiGrenadeDecision.ErrorRadiusM("operator"));
		Assert.True(AiGrenadeDecision.ErrorRadiusM("operator") > AiGrenadeDecision.ErrorRadiusM("infiltrator"));
	}

	[Fact]
	public void OnlyRetreatingOperatorsWithSmokeThrowSmoke()
	{
		Assert.True(AiGrenadeDecision.ShouldSmoke("operator", true, true, 0f));
		Assert.False(AiGrenadeDecision.ShouldSmoke("operator", false, true, 0f));
		Assert.False(AiGrenadeDecision.ShouldSmoke("operator", true, false, 0f));
		Assert.False(AiGrenadeDecision.ShouldSmoke("infiltrator", true, true, 0f));
		Assert.False(AiGrenadeDecision.ShouldSmoke("operator", true, true, 3f));
	}

	[Fact]
	public void DodgeWhenSeeingWithinTenOrHearingWithinEightAndNeverOwnGrenades()
	{
		Assert.True(AiGrenadeDecision.ShouldDodge(9f, true, false, false));
		Assert.False(AiGrenadeDecision.ShouldDodge(11f, true, false, false));
		Assert.True(AiGrenadeDecision.ShouldDodge(7f, false, true, false));
		Assert.False(AiGrenadeDecision.ShouldDodge(9f, false, true, false));
		Assert.False(AiGrenadeDecision.ShouldDodge(3f, true, true, true));
		Assert.False(AiGrenadeDecision.PinSendsBotToCover("infiltrator"));
		Assert.True(AiGrenadeDecision.PinSendsBotToCover("operator"));
	}

	[Fact]
	public void SolverLandsOnTheTargetOrReportsOutOfRange()
	{
		var from = new Vector3(0f, 1.6f, 0f);
		var to = new Vector3(0f, 0f, -18f);
		var v = ThrowSolver.Velocity(from, to, 18f)!.Value;
		Assert.Equal(18f, v.Length(), 2);
		// follow the arc until it passes the target's distance and read the height there
		var arc = ThrowSolver.Arc(from, v, 9.8f, 0.01f, 5f);
		var at = arc.First(p => p.Z <= to.Z);
		Assert.Equal(to.Y, at.Y, 0);
		Assert.Null(ThrowSolver.Velocity(from, new Vector3(0f, 0f, -60f), 18f)); // too far for 18 m/s
	}

	[Fact]
	public void LoadoutsFollowTheFactionTable()
	{
		var rng = new Random(9);
		var allied = Enumerable.Range(0, 500).SelectMany(_ => AiGrenadeLoadout.Roll(Faction.AlliedPmc, rng)).ToList();
		Assert.Contains("frag_grenade", allied);
		Assert.DoesNotContain("offensive_frag_grenade", allied);
		var soviet = Enumerable.Range(0, 500).SelectMany(_ => AiGrenadeLoadout.Roll(Faction.SovietPmc, rng)).ToList();
		Assert.Contains("offensive_frag_grenade", soviet);
		Assert.DoesNotContain("frag_grenade", soviet);
		Assert.All(Enumerable.Range(0, 100), _ => Assert.NotEmpty(AiGrenadeLoadout.Roll(Faction.Deserters, rng))); // deserters always carry
		Assert.True(Enumerable.Range(0, 1000).Count(_ => AiGrenadeLoadout.Roll(Faction.Looters, rng).Count > 0) < 250);
		Assert.True(AiGrenadeLoadout.Shouts(Faction.Bandits));
		Assert.False(AiGrenadeLoadout.Shouts(Faction.SovietPmc));
	}
}
