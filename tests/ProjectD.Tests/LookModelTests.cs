using Godot;
using Xunit;

namespace ProjectD.Tests;

public class LookModelTests
{
	[Fact]
	public void LeanTargetDirections()
	{
		Assert.Equal(-1f, LookModel.LeanTarget(true, false, true));
		Assert.Equal(1f, LookModel.LeanTarget(false, true, true));
		Assert.Equal(0f, LookModel.LeanTarget(true, true, true));
		Assert.Equal(0f, LookModel.LeanTarget(false, false, true));
	}

	[Fact]
	public void LeanBlockedWhenNotAllowed() => Assert.Equal(0f, LookModel.LeanTarget(false, true, false));

	[Fact]
	public void LeanStepIsGradualAndClamped()
	{
		Assert.Equal(0.3f, LookModel.LeanStep(0f, 1f, 6f, 0.05f), 3);
		Assert.Equal(1f, LookModel.LeanStep(0.9f, 1f, 6f, 0.5f));
	}

	[Fact]
	public void FreeLookClamped()
	{
		var v = LookModel.FreeLookApply(Vector2.Zero, new Vector2(10000f, -10000f), 0.1f, 110f, 60f);
		Assert.Equal(new Vector2(-110f, 60f), v);
	}

	[Fact]
	public void FreeLookReturnsToZero()
	{
		Assert.Equal(Vector2.Zero, LookModel.FreeLookReturn(new Vector2(50f, -20f), 360f, 1f));
		var part = LookModel.FreeLookReturn(new Vector2(100f, 0f), 360f, 0.1f);
		Assert.Equal(64f, part.X, 3);
	}

	[Fact]
	public void MantlePathEndpoints()
	{
		var from = Vector3.Zero;
		var to = new Vector3(0f, 1f, -1f);
		Assert.Equal(from, LookModel.MantlePosition(from, to, 0f));
		Assert.Equal(to, LookModel.MantlePosition(from, to, 1f));
	}

	[Fact]
	public void MantleRisesBeforeMovingForward()
	{
		var to = new Vector3(0f, 1f, -1f);
		var early = LookModel.MantlePosition(Vector3.Zero, to, 0.3f);
		Assert.True(early.Y > 0f);
		Assert.Equal(0f, early.Z);
		Assert.Equal(1f, LookModel.MantlePosition(Vector3.Zero, to, 0.6f).Y, 4);
	}

	[Fact]
	public void MantleHeightRange()
	{
		Assert.True(LookModel.MantleHeightOk(0.5f, 0.3f, 1.3f));
		Assert.False(LookModel.MantleHeightOk(0.2f, 0.3f, 1.3f));
		Assert.False(LookModel.MantleHeightOk(1.5f, 0.3f, 1.3f));
	}
}
