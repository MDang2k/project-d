using Godot;
using Xunit;

namespace ProjectD.Tests;

public class PredictTests
{
	[Fact]
	public void PredictMatchesStepIntegration()
	{
		var v = new Vector3(0f, 0f, -715f);
		var points = Ballistics.Predict(Vector3.Zero, v, 9.8f, 0.02f, 0.5f);
		var s = new BulletState { Position = Vector3.Zero, Velocity = v };
		for (var i = 0; i < points.Count - 1; i++)
			Ballistics.Step(ref s, 9.8f, 0.02f);
		Assert.Equal(s.Position.Z, points[^1].Z, 3);
		Assert.Equal(s.Position.Y, points[^1].Y, 3);
	}

	[Fact]
	public void DropAt100mIsAboutTenCentimetres()
	{
		var points = Ballistics.Predict(Vector3.Zero, new Vector3(0f, 0f, -715f), 9.8f, 0.005f, 0.5f);
		foreach (var p in points)
		{
			if (-p.Z >= 100f)
			{
				Assert.InRange(-p.Y, 0.08f, 0.12f);
				return;
			}
		}
		Assert.Fail("path never reached 100 m");
	}

	[Fact]
	public void PredictStopsAtMaxTime()
	{
		var points = Ballistics.Predict(Vector3.Zero, Vector3.Zero, 9.8f, 0.1f, 1f);
		Assert.InRange(points.Count, 10, 12);
	}
}
