using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class SquadCoordinatorTests
{
	private static SquadMemberState M(int id, float aggression = 0.5f, bool scope = false, bool inPos = false, bool alive = true, bool engaged = true)
		=> new(id, alive, engaged, aggression, scope, inPos);

	private static SquadCoordinator Squad(int n)
	{
		var s = new SquadCoordinator();
		for (var i = 1; i <= n; i++)
			s.Join(i);
		return s;
	}

	private static void Run(SquadCoordinator s, float seconds, params SquadMemberState[] states)
	{
		for (var t = 0f; t < seconds; t += 0.1f)
			s.Update(0.1f, states);
	}

	[Fact]
	public void TheCalmestIsAnchorAndTheBoldestFlank()
	{
		var s = Squad(3);
		Run(s, 0.2f, M(1, 0.2f), M(2, 0.9f), M(3, 0.7f));
		Assert.Equal(SquadRole.Anchor, s.RoleOf(1));
		Assert.Equal(SquadRole.Flanker, s.RoleOf(2));
		Assert.Equal(SquadRole.Flanker, s.RoleOf(3));
	}

	[Fact]
	public void OnlyTwoFlankersAndAScopeMakesAMarksman()
	{
		var s = Squad(5);
		Run(s, 0.2f, M(1, 0.1f), M(2, 0.9f), M(3, 0.8f), M(4, 0.7f), M(5, 0.6f, scope: true));
		Assert.Equal(SquadRole.Marksman, s.RoleOf(5));
		Assert.Equal(SquadRole.Anchor, s.RoleOf(1));
		Assert.Equal(2, new[] { 1, 2, 3, 4, 5 }.Count(i => s.RoleOf(i) == SquadRole.Flanker));
		Assert.Equal(SquadRole.None, s.RoleOf(4));
	}

	[Fact]
	public void ALoneFighterGetsNoRole()
	{
		var s = Squad(2);
		Run(s, 0.2f, M(1), M(2, engaged: false));
		Assert.Equal(SquadRole.None, s.RoleOf(1));
	}

	[Fact]
	public void RolesAreStableWhileMembersStayEngaged()
	{
		var s = Squad(3);
		Run(s, 0.2f, M(1, 0.2f), M(2, 0.9f), M(3, 0.7f));
		var before = Enumerable.Range(1, 3).Select(s.RoleOf).ToList();
		Run(s, 3f, M(1, 0.95f), M(2, 0.1f), M(3, 0.7f)); // temperaments swap, roles do not
		Assert.Equal(before, Enumerable.Range(1, 3).Select(s.RoleOf).ToList());
	}

	[Fact]
	public void ThePincerWaitsForEveryFlankerThenGoes()
	{
		var s = Squad(3);
		Run(s, 1.5f, M(1, 0.1f), M(2, 0.9f), M(3, 0.8f, inPos: true));
		Assert.False(s.PincerGo);
		Run(s, 0.3f, M(1, 0.1f), M(2, 0.9f, inPos: true), M(3, 0.8f, inPos: true));
		Assert.True(s.PincerGo);
	}

	[Fact]
	public void ThePincerGoesAnywayAfterTheTimeout()
	{
		var s = Squad(2);
		Run(s, 24f, M(1, 0.1f), M(2, 0.9f));
		Assert.False(s.PincerGo);
		Run(s, 2f, M(1, 0.1f), M(2, 0.9f));
		Assert.True(s.PincerGo);
	}

	[Fact]
	public void BoundingTakesTurnsAfterTenMetres()
	{
		var s = Squad(2);
		Run(s, 0.2f, M(1), M(2));
		var first = new[] { 1, 2 }.Single(s.MayAdvance);
		var other = first == 1 ? 2 : 1;
		Assert.False(s.MayAdvance(other));
		s.ReportAdvance(first, 10.5f);
		Run(s, 0.2f, M(1), M(2));
		Assert.True(s.MayAdvance(other));
		Assert.False(s.MayAdvance(first));
	}

	[Fact]
	public void ABoundingMemberThatIsStuckLosesItsTurn()
	{
		var s = Squad(2);
		Run(s, 0.2f, M(1), M(2));
		var first = new[] { 1, 2 }.Single(s.MayAdvance);
		Run(s, 6.5f, M(1), M(2));
		Assert.False(s.MayAdvance(first));
	}

	[Fact]
	public void ASoloBotMayAlwaysAdvance()
		=> Assert.True(Squad(1).MayAdvance(1));

	[Fact]
	public void TheLeaderDyingBreaksNerve()
	{
		var s = Squad(3);
		var broke = 0;
		s.MoraleBroken += () => broke++;
		Run(s, 1f, M(1), M(2), M(3));
		s.Died(1);
		Run(s, 0.3f, M(2), M(3));
		Assert.Equal(1, broke);
		Run(s, 2f, M(2), M(3));
		Assert.Equal(1, broke); // once
	}

	[Fact]
	public void LosingMostOfTheSquadQuicklyBreaksNerveButSlowLossesDoNot()
	{
		var quick = Squad(4);
		var broke = 0;
		quick.MoraleBroken += () => broke++;
		Run(quick, 1f, M(1), M(2), M(3), M(4));
		quick.Died(2);
		quick.Died(3);
		quick.Died(4);
		Run(quick, 0.2f, M(1));
		Assert.Equal(1, broke);

		var slow = Squad(4);
		var slowBroke = 0;
		slow.MoraleBroken += () => slowBroke++;
		Run(slow, 1f, M(1), M(2), M(3), M(4));
		slow.Died(4);
		Run(slow, 11f, M(1), M(2), M(3));
		slow.Died(3);
		Run(slow, 11f, M(1), M(2));
		Assert.Equal(0, slowBroke);
	}

	[Fact]
	public void MoraleRollFollowsPanic()
	{
		var rng = new Random(3);
		var panicky = Enumerable.Range(0, 1000).Count(_ => SquadCoordinator.Flees(0.8f, rng));
		var steady = Enumerable.Range(0, 1000).Count(_ => SquadCoordinator.Flees(0.2f, rng));
		Assert.InRange(panicky, 740, 860);
		Assert.InRange(steady, 140, 260);
		Assert.False(SquadCoordinator.Flees(0f, rng));
	}

	[Fact]
	public void SizeCountsOnlyTheLiving()
	{
		var s = Squad(3);
		s.Died(2);
		Assert.Equal(2, s.Size);
		s.Left(3);
		Assert.Equal(1, s.Size);
	}
}
