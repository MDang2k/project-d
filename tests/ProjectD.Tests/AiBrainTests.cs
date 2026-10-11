using Godot;
using Xunit;

namespace ProjectD.Tests;

public class AiBrainTests
{
	private static readonly Vector3 Spot = new(10f, 0f, -5f);

	private static AiSenses None => new(AwarenessLevel.Unaware, false, null, 99f);
	private static AiSenses Heard(float age = 0f) => new(AwarenessLevel.Suspicious, false, Spot, age);
	private static AiSenses Seen => new(AwarenessLevel.Alert, true, Spot, 0f);
	private static AiSenses Lost(float age) => new(AwarenessLevel.Unaware, false, Spot, age);

	private static void Run(AiBrain b, AiSenses s, float seconds, bool arrived = false)
	{
		for (var t = 0f; t < seconds; t += 0.1f)
			b.Update(0.1f, s, arrived, false);
	}

	[Fact]
	public void StartsIdleWithoutPatrol()
	{
		var b = new AiBrain(BrainTuning.Default);
		Run(b, None, 1f);
		Assert.Equal(AiState.Idle, b.State);
		Assert.Null(b.Destination);
		Assert.True(b.Looking);
	}

	[Fact]
	public void PatrolWalksPointsAndLoops()
	{
		var b = new AiBrain(BrainTuning.Default);
		var a = new Vector3(0f, 0f, -10f);
		var c = new Vector3(5f, 0f, -10f);
		b.SetPatrol(new[] { a, c });
		Run(b, None, 0.1f);
		Assert.Equal(AiState.Patrol, b.State);
		Assert.Equal(a, b.Destination);
		Run(b, None, 2.5f, arrived: true); // wait time elapses at the first point
		Assert.Equal(c, b.Destination);
		Run(b, None, 2.5f, arrived: true);
		Assert.Equal(a, b.Destination);
	}

	[Fact]
	public void NoiseSendsPatrolToInvestigate()
	{
		var b = new AiBrain(BrainTuning.Default);
		b.SetPatrol(new[] { Vector3.Zero });
		Run(b, Heard(), 0.1f);
		Assert.Equal(AiState.Investigate, b.State);
		Assert.Equal(Spot, b.Destination);
	}

	[Fact]
	public void InvestigateLooksAroundThenReturnsToPatrol()
	{
		var b = new AiBrain(BrainTuning.Default);
		b.SetPatrol(new[] { Vector3.Zero });
		Run(b, Heard(), 0.1f);
		Run(b, Lost(5f), 0.5f, arrived: true);
		Assert.Equal(AiState.Investigate, b.State);
		Assert.True(b.Looking);
		Run(b, Lost(5f), 3f, arrived: true);
		Assert.Equal(AiState.Patrol, b.State);
	}

	[Fact]
	public void SeeingTargetWhileAlertEntersCombatFromAnywhere()
	{
		var b = new AiBrain(BrainTuning.Default);
		Run(b, Seen, 0.1f);
		Assert.Equal(AiState.Combat, b.State);
		Assert.Null(b.Destination);
	}

	[Fact]
	public void SuspiciousSightingAloneDoesNotStartCombat()
	{
		var b = new AiBrain(BrainTuning.Default);
		Run(b, new AiSenses(AwarenessLevel.Suspicious, true, Spot, 0f), 0.2f);
		Assert.Equal(AiState.Investigate, b.State);
	}

	[Fact]
	public void LosingTargetSearchesLastKnownThenGivesUp()
	{
		var b = new AiBrain(BrainTuning.Default);
		Run(b, Seen, 0.1f);
		Run(b, Lost(1f), 0.1f);
		Assert.Equal(AiState.Combat, b.State); // grace period
		Run(b, Lost(5f), 0.1f);
		Assert.Equal(AiState.Search, b.State);
		Assert.Equal(Spot, b.Destination);
		Run(b, Lost(20f), 11f, arrived: true);
		Assert.Equal(AiState.Idle, b.State);
	}

	[Fact]
	public void SeeingAgainDuringSearchResumesCombat()
	{
		var b = new AiBrain(BrainTuning.Default);
		Run(b, Seen, 0.1f);
		Run(b, Lost(5f), 0.1f);
		Run(b, Seen, 0.1f);
		Assert.Equal(AiState.Combat, b.State);
	}

	[Fact]
	public void DeathStopsEverythingAndRespawnReturnsToRest()
	{
		var b = new AiBrain(BrainTuning.Default);
		Run(b, Seen, 0.1f);
		b.Update(0.1f, Seen, false, dead: true);
		Assert.Equal(AiState.Dead, b.State);
		Assert.Null(b.Destination);
		b.Update(0.1f, None, false, dead: false);
		Assert.Equal(AiState.Idle, b.State);
	}

	[Fact]
	public void StateChangedEventFires()
	{
		var b = new AiBrain(BrainTuning.Default);
		(AiState From, AiState To)? seen = null;
		b.StateChanged += (f, t) => seen = (f, t);
		Run(b, Seen, 0.1f);
		Assert.Equal((AiState.Idle, AiState.Combat), seen);
	}
}
