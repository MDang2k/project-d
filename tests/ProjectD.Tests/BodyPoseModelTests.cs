using Xunit;

namespace ProjectD.Tests;

public class BodyPoseModelTests
{
	private static (float Stand, float Crouch, float Prone) Blend(float eye)
		=> BodyPoseModel.StanceBlend(eye, 1.65f, 1.1f, 0.4f);

	[Fact]
	public void PureStances()
	{
		Assert.Equal((1f, 0f, 0f), Blend(1.65f));
		Assert.Equal((0f, 1f, 0f), Blend(1.1f));
		Assert.Equal((0f, 0f, 1f), Blend(0.4f));
	}

	[Fact]
	public void WeightsSumToOneAndClamp()
	{
		foreach (var eye in new[] { 0.2f, 0.4f, 0.75f, 1.1f, 1.4f, 1.65f, 2.0f })
		{
			var (s, c, p) = Blend(eye);
			Assert.Equal(1f, s + c + p, 4);
		}
		Assert.Equal((1f, 0f, 0f), Blend(2.0f));
		Assert.Equal((0f, 0f, 1f), Blend(0.2f));
	}

	[Fact]
	public void MidwayBlends()
	{
		var (s, c, p) = Blend(1.375f);
		Assert.Equal(0.5f, s, 3);
		Assert.Equal(0.5f, c, 3);
		Assert.Equal(0f, p);
	}
}
