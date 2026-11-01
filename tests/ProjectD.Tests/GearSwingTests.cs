using System;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class GearSwingTests
{
	private static Vector3 Peak(GearSwing g, float seconds, Func<float, (float Yaw, float Accel)> input, float kg = 0f)
	{
		var peak = Vector3.Zero;
		for (var t = 0f; t < seconds; t += 0.01f)
		{
			var (yaw, accel) = input(t);
			g.Update(0.01f, yaw, accel, kg);
			var r = g.RotationDeg;
			if (r.Abs().Length() > peak.Abs().Length())
				peak = r;
		}
		return peak;
	}

	[Fact]
	public void TurningSwingsThePackTheOtherWayAndATurnTheOtherWayMirrorsIt()
	{
		var left = Peak(new GearSwing(), 0.3f, _ => (200f, 0f));
		var right = Peak(new GearSwing(), 0.3f, _ => (-200f, 0f));
		Assert.True(left.Z < -1f);
		Assert.True(right.Z > 1f);
		Assert.Equal(left.Z, -right.Z, 3);
	}

	[Fact]
	public void SpeedingUpTipsThePackBack()
	{
		var peak = Peak(new GearSwing(), 0.3f, _ => (0f, 3f));
		Assert.True(peak.X < -1f);
	}

	[Fact]
	public void ItSettlesBackToRest()
	{
		var g = new GearSwing();
		Peak(g, 0.3f, _ => (300f, 4f));
		for (var i = 0; i < 300; i++)
			g.Update(0.01f, 0f, 0f, 0f);
		Assert.True(g.RotationDeg.Length() < 0.05f);
	}

	[Fact]
	public void TheSwingNeverPassesTheLimit()
	{
		var peak = Peak(new GearSwing(), 1f, _ => (4000f, 40f));
		Assert.True(Math.Abs(peak.X) <= 25.001f && Math.Abs(peak.Z) <= 25.001f);
	}

	[Fact]
	public void HeavyGearSwingsLess()
	{
		var light = Peak(new GearSwing(), 0.3f, _ => (200f, 0f), 0f);
		var heavy = Peak(new GearSwing(), 0.3f, _ => (200f, 0f), 15f);
		Assert.True(Math.Abs(heavy.Z) < Math.Abs(light.Z) * 0.7f);
	}

	[Fact]
	public void ABumpShiftsThePackAndItComesBack()
	{
		var g = new GearSwing();
		g.Bump(new Vector3(-120f, 0f, 0f));
		g.Update(0.05f, 0f, 0f, 0f);
		Assert.True(g.RotationDeg.X < -1f);
		for (var i = 0; i < 300; i++)
			g.Update(0.01f, 0f, 0f, 0f);
		Assert.True(g.RotationDeg.Length() < 0.05f);
	}

	[Fact]
	public void StandingStillStaysStill()
	{
		var g = new GearSwing();
		for (var i = 0; i < 100; i++)
			g.Update(0.01f, 0f, 0f, 5f);
		Assert.Equal(Vector3.Zero, g.RotationDeg);
	}
}
