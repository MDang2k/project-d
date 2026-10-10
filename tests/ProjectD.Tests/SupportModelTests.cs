using Godot;
using Xunit;

namespace ProjectD.Tests;

public class SupportModelTests
{
	private static SupportModel Make() => new(SupportTuning.Default);

	[Theory]
	[InlineData(1.0f, 1.0f, true)]
	[InlineData(0.2f, 1.0f, false)]
	[InlineData(1.9f, 1.0f, false)]
	[InlineData(1.0f, 0.3f, false)]
	public void RestSurfaceRules(float height, float normalY, bool expected)
		=> Assert.Equal(expected, Make().IsRestSurface(height, normalY));

	[Fact]
	public void RestBlendsInAndOut()
	{
		var s = Make();
		s.Update(0.125f, true, false, false, Stance.Stand, 0f, false);
		Assert.Equal(0.5f, s.RestAmount, 3);
		s.Update(1f, true, false, false, Stance.Stand, 0f, false);
		Assert.Equal(1f, s.RestAmount);
		s.Update(1f, true, false, false, Stance.Stand, 3f, false);
		Assert.Equal(0f, s.RestAmount);
	}

	[Fact]
	public void SprintLosesRest()
	{
		var s = Make();
		s.Update(1f, true, false, false, Stance.Stand, 0f, false);
		s.Update(1f, true, false, false, Stance.Stand, 0f, true);
		Assert.Equal(0f, s.RestAmount);
	}

	[Fact]
	public void BipodNeedsProneOrSurface()
	{
		var s = Make();
		Assert.False(s.TryDeployBipod(true, Stance.Stand, false, 0f));
		Assert.False(s.TryDeployBipod(false, Stance.Prone, false, 0f));
		Assert.True(s.TryDeployBipod(true, Stance.Prone, false, 0f));
		Assert.True(s.BipodDeployed);
	}

	[Fact]
	public void SurfaceDeployFoldsWhenSurfaceLost()
	{
		var s = Make();
		Assert.True(s.TryDeployBipod(true, Stance.Crouch, true, 0f));
		Assert.False(s.Update(0.1f, true, true, true, Stance.Crouch, 0f, false));
		Assert.True(s.BipodDeployed);
		Assert.True(s.Update(0.1f, false, false, true, Stance.Crouch, 0f, false));
		Assert.False(s.BipodDeployed);
	}

	[Fact]
	public void ProneDeployFoldsWhenStanding()
	{
		var s = Make();
		s.TryDeployBipod(true, Stance.Prone, false, 0f);
		Assert.True(s.Update(0.1f, false, false, true, Stance.Stand, 0f, false));
		Assert.False(s.BipodDeployed);
	}

	[Fact]
	public void MovingFoldsBipod()
	{
		var s = Make();
		s.TryDeployBipod(true, Stance.Prone, false, 0f);
		Assert.True(s.Update(0.1f, false, false, true, Stance.Prone, 2f, false));
	}

	[Fact]
	public void YawClampedAroundDeployDirection()
	{
		var s = Make();
		s.TryDeployBipod(true, Stance.Prone, false, 1f);
		var limit = Mathf.DegToRad(30f);
		Assert.Equal(1f + limit, s.ClampYaw(3f), 4);
		Assert.Equal(1f - limit, s.ClampYaw(-0.5f), 4);
		Assert.Equal(1.1f, s.ClampYaw(1.1f), 4);
	}

	[Fact]
	public void YawFreeWhenFolded()
		=> Assert.Equal(2.5f, Make().ClampYaw(2.5f));
}
