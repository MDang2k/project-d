using System.Linq;
using Xunit;

namespace ProjectD.Tests;

/// <summary>RaidStanding is process-wide state, so its tests share one class (xUnit runs a class's tests one at a time).</summary>
public class RaidStandingTests
{
	private static FriendlyFireTracker Tracker() => new(Faction.SovietPmc, "soviet_qm");

	[Fact]
	public void NobodyIsHostileOutsideARaid()
	{
		RaidStanding.End();
		Assert.False(RaidStanding.IsHostileToPlayer(Faction.SovietPmc));
	}

	[Fact]
	public void ABeginnedRaidReportsTheTrackersHostileFactions()
	{
		var t = Tracker();
		RaidStanding.Begin(t);
		Assert.False(RaidStanding.IsHostileToPlayer(Faction.SovietPmc));
		for (var i = 0; i < 3; i++)
			t.RegisterHit(Faction.SovietPmc, false);
		Assert.True(RaidStanding.IsHostileToPlayer(Faction.SovietPmc));
		Assert.False(RaidStanding.IsHostileToPlayer(Faction.AlliedPmc)); // only the faction that was shot at
		RaidStanding.End();
		Assert.False(RaidStanding.IsHostileToPlayer(Faction.SovietPmc)); // and only for that raid
	}

	[Fact]
	public void ChangesAddUpForTheResultScreenAndResetAtTheNextRaid()
	{
		var t = Tracker();
		RaidStanding.Begin(t);
		RaidStanding.Record(t.RegisterHit(Faction.SovietPmc, false));
		RaidStanding.Record(t.RegisterHit(Faction.SovietPmc, true));
		RaidStanding.Record(t.RegisterHit(Faction.AlliedPmc, true)); // not a friend: nothing to record
		Assert.Equal(-17, RaidStanding.Changes["soviet_pmc"]);
		Assert.Equal(-17, RaidStanding.Changes["soviet_qm"]);
		RaidStanding.End();
		Assert.Equal(-17, RaidStanding.Changes["soviet_pmc"]); // kept for the result screen
		RaidStanding.Begin(Tracker());
		Assert.Empty(RaidStanding.Changes);
		RaidStanding.End();
	}

	[Fact]
	public void StartingHostileFromLowReputationShowsUpAtOnce()
	{
		var t = Tracker();
		RaidStanding.Begin(t);
		t.StartHostile(Faction.SovietPmc);
		Assert.True(RaidStanding.IsHostileToPlayer(Faction.SovietPmc));
		RaidStanding.End();
	}
}
