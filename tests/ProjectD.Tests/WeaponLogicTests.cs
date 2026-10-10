using Godot;
using Xunit;

namespace ProjectD.Tests;

public class FireControlTests
{
	private static FireControl Make(FireMode mode, float rpm = 600f) => new(new[] { mode }, rpm);

	[Fact]
	public void SemiFiresOncePerPress()
	{
		var f = Make(FireMode.Semi);
		Assert.Equal(1, f.Update(0.016f, true, true, 30));
		Assert.Equal(0, f.Update(0.016f, false, true, 30));
	}

	[Fact]
	public void AutoRateMatchesRpm()
	{
		var f = Make(FireMode.Auto, 600f);
		var shots = 0;
		for (var i = 0; i < 60; i++)
			shots += f.Update(1f / 60f, i == 0, true, 999);
		Assert.InRange(shots, 9, 11);
	}

	[Fact]
	public void HighRateCatchesUpOnLongFrames()
	{
		var f = Make(FireMode.Auto, 1200f);
		Assert.Equal(1, f.Update(0.001f, true, true, 99));
		Assert.InRange(f.Update(0.1f, false, true, 99), 1, 2);
	}

	[Fact]
	public void NoDoubleShotAfterIdle()
	{
		var f = Make(FireMode.Auto, 600f);
		f.Update(1f, false, false, 30);
		Assert.Equal(1, f.Update(0.016f, true, true, 30));
	}

	[Fact]
	public void CappedByRoundsAvailable()
	{
		var f = Make(FireMode.Auto, 1200f);
		Assert.Equal(1, f.Update(0.2f, true, true, 1));
	}

	[Fact]
	public void CyclesModes()
	{
		var f = new FireControl(new[] { FireMode.Semi, FireMode.Auto }, 600f);
		Assert.Equal(FireMode.Semi, f.Mode);
		f.CycleMode();
		Assert.Equal(FireMode.Auto, f.Mode);
		f.CycleMode();
		Assert.Equal(FireMode.Semi, f.Mode);
	}
}

public class MagazineTests
{
	private static readonly AmmoType Ball = new("ball", 50f, 3, 700f);
	private static readonly AmmoType Ap = new("ap", 50f, 4, 700f);

	[Fact]
	public void RespectsCapacity()
	{
		var m = new Magazine(2);
		Assert.True(m.Push(Ball));
		Assert.True(m.Push(Ball));
		Assert.False(m.Push(Ball));
		Assert.Equal(2, m.Count);
	}

	[Fact]
	public void LoadOrderMatters()
	{
		var m = new Magazine(3);
		m.Push(Ball);
		m.Push(Ap);
		Assert.Equal(Ap, m.Pop());
		Assert.Equal(Ball, m.Pop());
		Assert.Null(m.Pop());
	}
}

public class WeaponStateTests
{
	private static readonly AmmoType Ball = new("ball", 50f, 3, 700f);

	private static WeaponState Loaded(int rounds, FireMode mode = FireMode.Auto)
	{
		var w = new WeaponState(new FireControl(new[] { mode }, 600f));
		var m = new Magazine(30);
		for (var i = 0; i < rounds; i++)
			m.Push(Ball);
		w.InsertMagazine(m);
		w.ChamberRound();
		return w;
	}

	[Fact]
	public void ChamberTakesTopRound()
	{
		var w = Loaded(5);
		Assert.Equal(5, w.RoundsAvailable);
		Assert.NotNull(w.Chamber);
		Assert.Equal(4, w.Magazine!.Count);
	}

	[Fact]
	public void FiringConsumesAndRechambers()
	{
		var w = Loaded(3, FireMode.Semi);
		var fired = w.Update(0.016f, true, true);
		Assert.Single(fired);
		Assert.Equal(2, w.RoundsAvailable);
		Assert.NotNull(w.Chamber);
	}

	[Fact]
	public void EmptyWeaponDoesNotFire()
	{
		var w = Loaded(1, FireMode.Semi);
		Assert.Single(w.Update(0.016f, true, true));
		Assert.Empty(w.Update(1f, true, true));
		Assert.Equal(0, w.RoundsAvailable);
		Assert.Null(w.Chamber);
	}

	[Fact]
	public void AutoStopsWhenRoundsRunOut()
	{
		var w = Loaded(3);
		var total = 0;
		for (var i = 0; i < 120; i++)
			total += w.Update(1f / 60f, i == 0, true).Count;
		Assert.Equal(3, total);
	}
}

public class BallisticsTests
{
	[Fact]
	public void BulletDropsUnderGravity()
	{
		var s = new BulletState { Position = Vector3.Zero, Velocity = new Vector3(0f, 0f, -700f) };
		for (var i = 0; i < 100; i++)
			Ballistics.Step(ref s, 9.8f, 0.0014f);
		// ~0.14 s of flight: z close to -98 m, drop close to 0.5 * g * t^2 = 0.096 m.
		Assert.InRange(s.Position.Z, -99f, -97f);
		Assert.InRange(s.Position.Y, -0.11f, -0.085f);
	}

	[Fact]
	public void StepReturnsTravelledSegment()
	{
		var s = new BulletState { Position = new Vector3(0f, 1f, 0f), Velocity = new Vector3(0f, 0f, -100f) };
		var (from, to) = Ballistics.Step(ref s, 0f, 0.1f);
		Assert.Equal(new Vector3(0f, 1f, 0f), from);
		Assert.Equal(new Vector3(0f, 1f, -10f), to);
		Assert.Equal(0.1f, s.Age, 4);
	}
}
