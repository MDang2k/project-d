using Xunit;

namespace ProjectD.Tests;

public class TraderRevealTests
{
	[Fact]
	public void NoRevealQuestNeverReveals() => Assert.False(TraderReveal.IsRevealed("", _ => true));

	[Fact]
	public void RevealsOnlyWhenTheQuestIsCompleted()
	{
		Assert.False(TraderReveal.IsRevealed("mission_resonanz", _ => false));
		Assert.True(TraderReveal.IsRevealed("mission_resonanz", q => q == "mission_resonanz"));
	}
}
