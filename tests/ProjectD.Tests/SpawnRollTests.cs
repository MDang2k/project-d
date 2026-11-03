using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class SpawnRollTests
{
	private sealed record P(string Id, string Group = "", int Min = 0, int Max = 0);

	private static List<P> Roll(IEnumerable<P> points, int seed)
		=> SpawnRoll.Apply(points, p => p.Group, p => p.Min, p => p.Max, new Random(seed));

	private static IEnumerable<P> Looters(int n) => Enumerable.Range(0, n).Select(i => new P($"l{i}", "looters", 4, 6));

	[Fact]
	public void AGroupKeepsBetweenMinAndMaxOfItsPoints()
	{
		var counts = Enumerable.Range(1, 200).Select(s => Roll(Looters(8), s).Count).ToList();
		Assert.All(counts, c => Assert.InRange(c, 4, 6));
		Assert.Equal(new[] { 4, 5, 6 }, counts.Distinct().OrderBy(c => c));
	}

	[Fact]
	public void UngroupedPointsAlwaysStay()
	{
		var points = Looters(8).Concat(new[] { new P("pmc1"), new P("pmc2") });
		for (var seed = 1; seed <= 50; seed++)
			Assert.Equal(2, Roll(points, seed).Count(p => p.Group == ""));
	}

	[Fact]
	public void TheChoiceVariesAndKeepsTheOriginalOrder()
	{
		var picks = Enumerable.Range(1, 30).Select(s => string.Join(",", Roll(Looters(8), s).Select(p => p.Id))).Distinct().ToList();
		Assert.True(picks.Count > 5);
		foreach (var pick in picks)
		{
			var ids = pick.Split(',').Select(s => int.Parse(s[1..])).ToList();
			Assert.Equal(ids.OrderBy(i => i), ids);
		}
	}

	[Fact]
	public void SameSeedSameRoll()
		=> Assert.Equal(Roll(Looters(8), 5), Roll(Looters(8), 5));

	[Fact]
	public void AGroupSmallerThanItsMinimumKeepsEverything()
		=> Assert.Equal(3, Roll(Looters(3), 1).Count);

	[Fact]
	public void GroupsRollIndependently()
	{
		var points = Looters(8).Concat(Enumerable.Range(0, 4).Select(i => new P($"b{i}", "bandits", 1, 1)));
		var rolled = Roll(points, 3);
		Assert.Equal(1, rolled.Count(p => p.Group == "bandits"));
		Assert.InRange(rolled.Count(p => p.Group == "looters"), 4, 6);
	}

	[Fact]
	public void ANegativeOrInvertedRangeIsClamped()
	{
		Assert.Empty(Roll(Enumerable.Range(0, 5).Select(i => new P($"x{i}", "g", -3, -1)), 1));
		Assert.Equal(5, Roll(Enumerable.Range(0, 5).Select(i => new P($"x{i}", "g", 5, 2)), 1).Count);
	}

	[Fact]
	public void NothingInNothingOut()
		=> Assert.Empty(Roll(Array.Empty<P>(), 1));
}
