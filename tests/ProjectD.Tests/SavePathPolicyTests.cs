using Xunit;

namespace ProjectD.Tests;

public class SavePathPolicyTests
{
	private const string Default = "C:/u/save.json";

	private static string? Resolve(bool headless, params string[] args)
		=> SavePathPolicy.Resolve(args, headless, Default, p => "G:" + p);

	[Fact]
	public void NormalLaunchUsesTheDefaultPath() => Assert.Equal(Default, Resolve(false));

	[Fact]
	public void NosaveWinsEvenOverAnExplicitPath() => Assert.Null(Resolve(false, "--nosave", "--savepath=x.json"));

	[Theory]
	[InlineData("--autonew")]
	[InlineData("--shot=a.png")]
	public void DevRunsDoNotTouchTheRealSave(string arg) => Assert.Null(Resolve(false, arg));

	[Fact]
	public void HeadlessDoesNotTouchTheRealSave() => Assert.Null(Resolve(true));

	[Fact]
	public void ExplicitPathOptsBackInEvenForDevOrHeadlessRuns()
		=> Assert.Equal("x.json", Resolve(true, "--autonew", "--savepath=x.json"));

	[Fact]
	public void ExplicitUserPathIsGlobalisedAndEmptyPathIsIgnored()
	{
		Assert.Equal("G:user://s.json", Resolve(false, "--savepath=user://s.json"));
		Assert.Equal(Default, Resolve(false, "--savepath="));
	}
}
