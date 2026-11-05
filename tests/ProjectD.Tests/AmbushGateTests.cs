using Xunit;

namespace ProjectD.Tests;

public class AmbushGateTests
{
	[Fact]
	public void SeeingThePlayerFarAwayDoesNotSpringTheTrap()
	{
		var g = new AmbushGate(16f, 6f);
		Assert.False(g.Update(40f, true, false, false));
		Assert.False(g.Update(17f, true, false, false));
		Assert.False(g.Sprung);
	}

	[Fact]
	public void SpringsWhenThePlayerIsCloseAndSeen()
	{
		var g = new AmbushGate(16f, 6f);
		Assert.False(g.Update(15f, false, false, false)); // close but unseen: not yet
		Assert.True(g.Update(15f, true, false, false));
	}

	[Fact]
	public void SpringsWhenStumbledOntoHitOrASquadmateFights()
	{
		Assert.True(new AmbushGate(16f, 6f).Update(5f, false, false, false));
		Assert.True(new AmbushGate().Update(100f, false, false, true));
		Assert.True(new AmbushGate().Update(100f, false, true, false));
	}

	[Fact]
	public void OnceSprungItStaysSprung()
	{
		var g = new AmbushGate();
		g.Update(3f, true, false, false);
		Assert.True(g.Update(200f, false, false, false));
	}
}
