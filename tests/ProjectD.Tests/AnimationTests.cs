using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class SpringTests
{
	private static float Settle(float stiffness, float ratio, out float peak)
	{
		var s = new SpringDamper();
		s.Impulse(10f);
		peak = 0f;
		var t = 0f;
		var settled = -1f;
		for (; t < 5f; t += 0.005f)
		{
			s.Update(0.005f, stiffness, ratio);
			peak = Math.Max(peak, Math.Abs(s.Value));
			if (settled < 0f && t > 0.05f && Math.Abs(s.Value) < 0.001f && Math.Abs(s.Velocity) < 0.01f)
				settled = t;
		}
		return settled;
	}

	[Fact]
	public void CriticallyDampedNeverOvershoots()
	{
		var s = new SpringDamper();
		s.Impulse(10f);
		var crossed = false;
		for (var i = 0; i < 2000; i++)
		{
			s.Update(0.005f, 200f, 1f);
			if (s.Value < -0.0001f)
				crossed = true;
		}
		Assert.False(crossed);
		Assert.True(Math.Abs(s.Value) < 0.001f);
	}

	[Fact]
	public void UnderDampedBouncesPastZero()
	{
		var s = new SpringDamper();
		s.Impulse(10f);
		var crossed = false;
		for (var i = 0; i < 400; i++)
		{
			s.Update(0.005f, 200f, 0.3f);
			if (s.Value < -0.001f)
				crossed = true;
		}
		Assert.True(crossed);
	}

	[Fact]
	public void StifferSpringsSettleFasterAndKickLess()
	{
		var soft = Settle(60f, 1f, out var softPeak);
		var stiff = Settle(400f, 1f, out var stiffPeak);
		Assert.True(stiff < soft);
		Assert.True(stiffPeak < softPeak);
	}

	[Fact]
	public void ImpulsesAccumulateAtHighFireRates()
	{
		var one = new SpringDamper();
		one.Impulse(5f);
		for (var i = 0; i < 6; i++)
			one.Update(0.01f, 200f, 1f);
		var burst = new SpringDamper();
		for (var shot = 0; shot < 5; shot++)
		{
			burst.Impulse(5f);
			for (var i = 0; i < 6; i++)
				burst.Update(0.01f, 200f, 1f);
		}
		Assert.True(burst.Value > one.Value * 2f);
	}

	[Fact]
	public void SameInputsGiveTheSameResult()
	{
		float Run()
		{
			var s = new Spring3();
			s.Impulse(new Vector3(3f, -2f, 1f));
			for (var i = 0; i < 100; i++)
				s.Update(0.016f, 150f, 0.6f);
			return s.Value.X + s.Value.Y * 10f + s.Value.Z * 100f;
		}
		Assert.Equal(Run(), Run());
	}
}

public class ViewBobTests
{
	private static readonly ViewBobTuning T = ViewBobTuning.Default;

	[Fact]
	public void NoBobStandingStillAndSprintBobsMoreThanWalk()
	{
		Assert.Equal((0f, 0f, 0f), ViewBob.Offset(T, BobMove.Idle, 1f));
		var walk = Enumerable.Range(0, 100).Max(i => Math.Abs(ViewBob.Offset(T, BobMove.Walk, i * 0.0628f).Vertical));
		var sprint = Enumerable.Range(0, 100).Max(i => Math.Abs(ViewBob.Offset(T, BobMove.Sprint, i * 0.0628f).Vertical));
		var crouch = Enumerable.Range(0, 100).Max(i => Math.Abs(ViewBob.Offset(T, BobMove.CrouchWalk, i * 0.0628f).Vertical));
		Assert.True(sprint > walk);
		Assert.Equal(walk * 0.6f, crouch, 4);
	}

	[Fact]
	public void AdsReducesRestRemovesAndSliderScales()
	{
		Assert.Equal(1f, ViewBob.Scale(T, 0f, 0f, 1f), 3);
		Assert.Equal(0.2f, ViewBob.Scale(T, 1f, 0f, 1f), 3);
		Assert.Equal(0f, ViewBob.Scale(T, 0f, 1f, 1f), 3);
		Assert.Equal(0f, ViewBob.Scale(T, 0f, 0f, 0f), 3);
		Assert.Equal(1.5f, ViewBob.Scale(T, 0f, 0f, 9f), 3);
	}

	[Fact]
	public void MovementKindFromState()
	{
		Assert.Equal(BobMove.Idle, ViewBob.MoveOf(true, 0.1f, false, Stance.Stand));
		Assert.Equal(BobMove.Idle, ViewBob.MoveOf(false, 5f, false, Stance.Stand));
		Assert.Equal(BobMove.Sprint, ViewBob.MoveOf(true, 5f, true, Stance.Stand));
		Assert.Equal(BobMove.CrouchWalk, ViewBob.MoveOf(true, 2f, false, Stance.Crouch));
		Assert.Equal(BobMove.Crawl, ViewBob.MoveOf(true, 1f, false, Stance.Prone));
	}

	[Fact]
	public void LandingImpulseIsDownwardAndCapped()
	{
		Assert.True(ViewBob.LandingImpulse(T, 4f) < 0f);
		Assert.Equal(ViewBob.LandingImpulse(T, 50f), ViewBob.LandingImpulse(T, 100f), 4);
	}
}

public class InertiaSwayTests
{
	private static float PeakLag(float weight, float ergo, float arms)
	{
		var s = new InertiaSway();
		s.Update(0.016f, new Vector2(5f, 0f), weight, ergo, arms);
		return Math.Abs(s.Offset.X);
	}

	[Fact]
	public void HeavierClumsierAndTiredLagMore()
	{
		Assert.True(PeakLag(8f, 50f, 1f) > PeakLag(3f, 50f, 1f));
		Assert.True(PeakLag(4f, 20f, 1f) > PeakLag(4f, 80f, 1f));
		Assert.True(PeakLag(4f, 50f, 0.2f) > PeakLag(4f, 50f, 1f));
	}

	[Fact]
	public void LagsAgainstTheTurnThenReturnsToZero()
	{
		var s = new InertiaSway();
		s.Update(0.016f, new Vector2(5f, 0f), 4f, 50f, 1f);
		Assert.True(s.Offset.X < 0f);
		for (var i = 0; i < 300; i++)
			s.Update(0.016f, Vector2.Zero, 4f, 50f, 1f);
		Assert.True(Math.Abs(s.Offset.X) < 0.01f);
	}

	[Fact]
	public void LagIsCapped()
	{
		var s = new InertiaSway();
		s.Update(0.016f, new Vector2(500f, 500f), 10f, 0f, 0f);
		Assert.True(Math.Abs(s.Offset.X) <= s.MaxDeg + 0.01f);
	}
}

public class AdsSolveTests
{
	[Fact]
	public void SocketLandsOnTheViewAxisAtEyeRelief()
	{
		var socket = new Transform3D(Basis.Identity, new Vector3(0.01f, 0.13f, 0.05f));
		var weapon = AdsSolve.WeaponTransform(socket, 0.3f);
		var onScreen = weapon * socket.Origin;
		Assert.True(onScreen.IsEqualApprox(new Vector3(0f, 0f, -0.3f)));
		Assert.True(AdsSolve.WeaponOffset(socket.Origin, 0.3f).IsEqualApprox(weapon.Origin));
	}

	[Fact]
	public void RotatedSocketAxisIsAlignedWithTheView()
	{
		var socket = new Transform3D(Basis.FromEuler(new Vector3(Mathf.DegToRad(5f), 0f, 0f)), new Vector3(0f, 0.1f, 0f));
		var weapon = AdsSolve.WeaponTransform(socket, 0.2f);
		var forward = (weapon.Basis * socket.Basis * Vector3.Forward).Normalized();
		Assert.True(forward.IsEqualApprox(Vector3.Forward));
		Assert.True((weapon * socket.Origin).IsEqualApprox(new Vector3(0f, 0f, -0.2f)));
	}

	[Fact]
	public void AnyOpticHeightGivesTheSameSightPoint()
	{
		foreach (var h in new[] { 0.09f, 0.12f, 0.18f })
		{
			var socket = new Transform3D(Basis.Identity, new Vector3(0f, h, -0.02f));
			Assert.True((AdsSolve.WeaponTransform(socket, 0.25f) * socket.Origin).IsEqualApprox(new Vector3(0f, 0f, -0.25f)));
		}
	}
}

public class SupportGripTests
{
	private static readonly GripCandidate Default = new(99, new Vector3(0f, 0f, -0.3f), "wrap");

	[Fact]
	public void ForegripBeatsHandguardBeatsReceiverBeatsDefault()
	{
		var fore = new GripCandidate(SupportGripResolver.PriorityOf("underbarrel"), new Vector3(0f, -0.05f, -0.4f), "vertical");
		var hand = new GripCandidate(SupportGripResolver.PriorityOf("handguard"), new Vector3(0f, 0f, -0.35f), "wrap");
		var recv = new GripCandidate(SupportGripResolver.PriorityOf("mount"), new Vector3(0f, -0.04f, -0.15f), "magwell");
		Assert.Equal("vertical", SupportGripResolver.Resolve(new[] { recv, hand, fore }, Default).Pose);
		Assert.Equal("wrap", SupportGripResolver.Resolve(new[] { recv, hand }, Default).Pose);
		Assert.Equal("magwell", SupportGripResolver.Resolve(new[] { recv }, Default).Pose);
		Assert.Equal(Default, SupportGripResolver.Resolve(Array.Empty<GripCandidate>(), Default));
	}
}

public class ReadyPoseTests
{
	private static readonly ReadyPoseTuning T = ReadyPoseTuning.Default;

	[Fact]
	public void OverheadObstructionGivesLowReadyOtherwiseHigh()
	{
		Assert.Equal(ReadyPose.High, ReadyPoseModel.Choose(0f, false, T));
		Assert.Equal(ReadyPose.High, ReadyPoseModel.Choose(-0.3f, false, T));
		Assert.Equal(ReadyPose.Low, ReadyPoseModel.Choose(0.3f, false, T));
		Assert.Equal(ReadyPose.Low, ReadyPoseModel.Choose(0f, true, T));
	}

	[Fact]
	public void PoseBlendsInOnlyPastTheStartFraction()
	{
		Assert.Equal(0f, ReadyPoseModel.Amount(0.3f, T), 3);
		Assert.Equal(0f, ReadyPoseModel.Amount(0.5f, T), 3);
		Assert.Equal(0.5f, ReadyPoseModel.Amount(0.75f, T), 3);
		Assert.Equal(1f, ReadyPoseModel.Amount(1f, T), 3);
		Assert.Equal(60f, ReadyPoseModel.PitchDeg(ReadyPose.High, 1f, T), 3);
		Assert.Equal(-50f, ReadyPoseModel.PitchDeg(ReadyPose.Low, 1f, T), 3);
		Assert.Equal(0f, ReadyPoseModel.PitchDeg(ReadyPose.None, 1f, T), 3);
	}
}

public class ReloadSequenceTests
{
	private static readonly AmmoType Ball = new("ball", 50f, 3, 700f);

	private static Magazine Mag(int rounds)
	{
		var m = new Magazine(30);
		for (var i = 0; i < rounds; i++)
			m.Push(Ball);
		return m;
	}

	private static WeaponController Make(int? inMag, bool chambered)
	{
		var state = new WeaponState(new FireControl(new[] { FireMode.Semi }, 600f));
		if (inMag is { } n)
			state.InsertMagazine(Mag(n));
		if (chambered)
			state.SetChamber(Ball);
		return new WeaponController(state, ActionTuning.Default);
	}

	private static void Run(WeaponController c, float seconds)
	{
		for (var t = 0f; t < seconds; t += 0.01f)
			c.Update(0.01f);
	}

	[Theory]
	[InlineData(true, 10, true, ReloadEntry.Full)]
	[InlineData(true, 0, false, ReloadEntry.Full)]
	[InlineData(true, 5, false, ReloadEntry.ResumeRack)]
	[InlineData(false, 0, true, ReloadEntry.ResumeInsert)]
	[InlineData(false, 0, false, ReloadEntry.ResumeEmpty)]
	public void EntryMatchesThePhysicalState(bool hasMag, int rounds, bool chambered, ReloadEntry expected)
		=> Assert.Equal(expected, ReloadSequence.EntryFor(hasMag, rounds, chambered));

	[Fact]
	public void EventsComeInOrderOncePerReload()
	{
		var seq = new ReloadSequence(ActionTuning.Default, false, ReloadEntry.Full, true);
		var events = new List<ReloadEvent>();
		for (var i = 0; i < 1000 && !seq.IsDone; i++)
			events.AddRange(seq.Advance(0.01f));
		Assert.Equal(new[]
		{
			ReloadEvent.MagDetached, ReloadEvent.MagStowed, ReloadEvent.MagFetched,
			ReloadEvent.MagInserted, ReloadEvent.BoltRacked, ReloadEvent.Finished,
		}, events);
	}

	[Fact]
	public void FastReloadSkipsTheStowAndDropsNothingTwice()
	{
		var seq = new ReloadSequence(ActionTuning.Default, true, ReloadEntry.Full, false);
		var events = new List<ReloadEvent>();
		for (var i = 0; i < 1000 && !seq.IsDone; i++)
			events.AddRange(seq.Advance(0.01f));
		Assert.DoesNotContain(ReloadEvent.MagStowed, events);
		Assert.DoesNotContain(ReloadEvent.BoltRacked, events);
		Assert.Equal(ReloadEvent.Finished, events[^1]);
	}

	[Fact]
	public void TimeMultiplierSlowsTheWholeSequence()
	{
		var normal = new ReloadSequence(ActionTuning.Default, false, ReloadEntry.Full, false);
		var slow = new ReloadSequence(ActionTuning.Default, false, ReloadEntry.Full, false, 2f);
		Assert.Equal(normal.Total * 2f, slow.Total, 3);
	}

	[Fact]
	public void InterruptAtEveryMomentThenResumeKeepsEveryRoundAndMagazine()
	{
		for (var ms = 0; ms <= 2800; ms += 40)
		{
			var c = Make(inMag: 7, chambered: true);
			c.Pouch.Add(Mag(30));
			var dropped = new List<Magazine>();
			c.MagazineDropped += dropped.Add;
			var before = c.State.RoundsAvailable + 30;
			Assert.True(c.StartReload(fast: false));
			Run(c, ms / 1000f);
			c.Interrupt();
			Assert.False(c.IsBusy);
			Assert.Equal(before, c.State.RoundsAvailable + c.PouchRounds() + dropped.Sum(m => m.Count));
			// resume (or start again if the reload had already finished)
			if (c.StartReload(fast: false))
				Run(c, 4f);
			Assert.False(c.IsBusy);
			Assert.NotNull(c.State.Magazine);
			Assert.NotNull(c.State.Chamber);
			Assert.Equal(before, c.State.RoundsAvailable + c.PouchRounds() + dropped.Sum(m => m.Count));
		}
	}

	[Fact]
	public void InterruptedAfterDetachLeavesNoMagazineAndResumesWithInsert()
	{
		var c = Make(inMag: 7, chambered: true);
		c.Pouch.Add(Mag(30));
		c.StartReload(false);
		Run(c, 1.2f); // past MagDetached and MagStowed, before MagInserted
		Assert.Null(c.State.Magazine);
		c.Interrupt();
		Assert.Null(c.State.Magazine);
		Assert.NotNull(c.State.Chamber);
		Assert.True(c.StartReload(false));
		Assert.Equal(ReloadPhase.MagFetch, c.Phase);
		Run(c, 3f);
		Assert.Equal(30, c.State.Magazine!.Count);
	}

	[Fact]
	public void MagazineInTheHandGoesBackWhenInterrupted()
	{
		var c = Make(inMag: 7, chambered: true);
		c.Pouch.Add(Mag(30));
		c.StartReload(false);
		Run(c, 1.1f); // detached, not yet stowed
		Assert.Null(c.State.Magazine);
		Assert.Equal(1, c.Pouch.Count);
		c.Interrupt();
		Assert.Equal(2, c.Pouch.Count);
	}

	[Fact]
	public void ChamberEmptyWithRoundsInTheMagazineOnlyRacks()
	{
		var c = Make(inMag: 5, chambered: false);
		Assert.True(c.StartReload(false));
		Assert.Equal(ReloadPhase.BoltRack, c.Phase);
		Run(c, 3f);
		Assert.NotNull(c.State.Chamber);
		Assert.Equal(4, c.State.Magazine!.Count);
		Assert.Equal(0, c.Pouch.Count);
	}

	[Fact]
	public void NoMagazineAndNothingChamberedFetchesInsertsRacks()
	{
		var c = Make(inMag: null, chambered: false);
		c.Pouch.Add(Mag(30));
		Assert.True(c.StartReload(false));
		Assert.Equal(ReloadPhase.MagFetch, c.Phase);
		Run(c, 4f);
		Assert.Equal(29, c.State.Magazine!.Count);
		Assert.NotNull(c.State.Chamber);
	}

	[Fact]
	public void EmptyStoreMidReloadEndsTheSequenceWithoutLosingRounds()
	{
		var c = Make(inMag: 7, chambered: true);
		c.Pouch.Add(Mag(30));
		var interrupted = 0;
		c.ReloadInterrupted += () => interrupted++;
		c.StartReload(true);
		c.Pouch.TakeFullest(); // someone else takes the spare after the reload started
		Run(c, 4f);
		Assert.False(c.IsBusy);
		Assert.Equal(1, interrupted);
		Assert.Null(c.State.Magazine);
	}
}

internal static class ControllerTestExtensions
{
	public static int PouchRounds(this WeaponController c)
	{
		var total = 0;
		var taken = new List<Magazine>();
		while (c.Pouch.Count > 0)
		{
			var m = c.Pouch.TakeFullest()!;
			total += m.Count;
			taken.Add(m);
		}
		foreach (var m in taken)
			c.Pouch.Add(m);
		return total;
	}
}

public class GaitModelTests
{
	private static readonly GaitTuning T = GaitTuning.Default;

	[Fact]
	public void StandingIdleIsStraightLegs()
	{
		var (l, r) = GaitModel.Legs(T, GaitStance.Stand, 0f, Vector2.Zero, 0.3f);
		Assert.Equal(0f, l.ThighDeg, 3);
		Assert.Equal(0f, r.ShinDeg, 3);
		Assert.Equal(0f, l.FootDeg, 3);
	}

	[Fact]
	public void LegsSwingInAntiphaseAndFlipWhenWalkingBackward()
	{
		var (l, r) = GaitModel.Legs(T, GaitStance.Stand, 0.5f, new Vector2(0f, 1f), 0.25f);
		Assert.Equal(-r.ThighDeg, l.ThighDeg, 3);
		Assert.True(Math.Abs(l.ThighDeg) > 10f);
		var (lb, _) = GaitModel.Legs(T, GaitStance.Stand, 0.5f, new Vector2(0f, -1f), 0.25f);
		Assert.Equal(-l.ThighDeg, lb.ThighDeg, 3);
	}

	[Fact]
	public void SprintSwingsWiderThanWalk()
	{
		float Peak(float ring) => Enumerable.Range(0, 32).Max(i => Math.Abs(GaitModel.Legs(T, GaitStance.Stand, ring, new Vector2(0f, 1f), i / 32f).Left.ThighDeg));
		Assert.True(Peak(1f) > Peak(0.5f));
		Assert.True(Peak(0.5f) > Peak(0.1f));
	}

	[Fact]
	public void CrouchHoldsTheBaseFlexionAndKeepsTheSoleFlat()
	{
		var (l, _) = GaitModel.Legs(T, GaitStance.Crouch, 0f, Vector2.Zero, 0f);
		Assert.Equal(T.CrouchBaseThighDeg, l.ThighDeg, 3);
		Assert.Equal(-T.CrouchBaseShinDeg, l.ShinDeg, 3);
		Assert.Equal(0f, l.ThighDeg + l.ShinDeg + l.FootDeg, 3);
	}

	[Fact]
	public void StrafingRollsTheHipsForwardWalkingDoesNot()
	{
		var strafe = Enumerable.Range(0, 32).Max(i => Math.Abs(GaitModel.Legs(T, GaitStance.Stand, 0.5f, new Vector2(1f, 0f), i / 32f).Left.ThighRollDeg));
		var forward = Enumerable.Range(0, 32).Max(i => Math.Abs(GaitModel.Legs(T, GaitStance.Stand, 0.5f, new Vector2(0f, 1f), i / 32f).Left.ThighRollDeg));
		Assert.True(strafe > 3f);
		Assert.Equal(0f, forward, 3);
	}

	[Fact]
	public void TheCycleLoops()
	{
		var a = GaitModel.Legs(T, GaitStance.Stand, 1f, new Vector2(0.6f, 0.8f), 0f);
		var b = GaitModel.Legs(T, GaitStance.Stand, 1f, new Vector2(0.6f, 0.8f), 1f);
		Assert.Equal(a.Left.ThighDeg, b.Left.ThighDeg, 3);
		Assert.Equal(a.Right.ShinDeg, b.Right.ShinDeg, 3);
	}
}

public class LocomotionParamsTests
{
	[Fact]
	public void BlendPositionPointsWhereTheBodyMovesOnTheRightRing()
	{
		Assert.Equal(Vector2.Zero, LocomotionParams.BlendPosition(Vector3.Zero, 2.8f, 5.2f));
		var forwardWalk = LocomotionParams.BlendPosition(new Vector3(0f, 0f, -2.8f), 2.8f, 5.2f);
		Assert.Equal(0f, forwardWalk.X, 3);
		Assert.Equal(0.5f, forwardWalk.Y, 3);
		var sprint = LocomotionParams.BlendPosition(new Vector3(0f, 0f, -5.2f), 2.8f, 5.2f);
		Assert.Equal(1f, sprint.Y, 3);
		var right = LocomotionParams.BlendPosition(new Vector3(2.8f, 0f, 0f), 2.8f, 5.2f);
		Assert.Equal(0.5f, right.X, 3);
		var back = LocomotionParams.BlendPosition(new Vector3(0f, 0f, 2f), 2.8f, 5.2f);
		Assert.True(back.Y < 0f);
	}

	[Fact]
	public void PlayRateGrowsWithSpeedAndIsClamped()
	{
		Assert.Equal(1f, LocomotionParams.PlayRate(0f), 3);
		Assert.True(LocomotionParams.PlayRate(5f) > LocomotionParams.PlayRate(2f));
		Assert.True(LocomotionParams.PlayRate(100f) <= 2.2f);
		Assert.True(LocomotionParams.PlayRate(0.1f) >= 0.35f);
	}

	[Fact]
	public void StanceWeightsBecomeTwoBlendFactors()
	{
		Assert.Equal((0f, 0f), LocomotionParams.StanceFactors((1f, 0f, 0f)));
		Assert.Equal((1f, 0f), LocomotionParams.StanceFactors((0f, 1f, 0f)));
		var mid = LocomotionParams.StanceFactors((0.5f, 0.5f, 0f));
		Assert.Equal(0.5f, mid.CrouchOverStand, 3);
		Assert.Equal(1f, LocomotionParams.StanceFactors((0f, 0f, 1f)).ProneOverRest, 3);
	}
}

public class AimOffsetTests
{
	private static readonly AimOffsetTuning T = AimOffsetTuning.Default;

	[Fact]
	public void TheHeadKeepsFacingTheCamera()
	{
		foreach (var pitch in new[] { -60f, -20f, 0f, 30f, 80f })
		{
			var (spine, chest, head) = AimOffsetSolver.Distribute(T, pitch, 0f);
			Assert.Equal(pitch, spine + chest + head, 3);
		}
	}

	[Fact]
	public void LookingDownBendsTheTorsoLessThanLookingUp()
	{
		var down = AimOffsetSolver.Distribute(T, -60f, 0f);
		var up = AimOffsetSolver.Distribute(T, 60f, 0f);
		Assert.True(Math.Abs(down.Spine + down.Chest) < Math.Abs(up.Spine + up.Chest));
		Assert.True(Math.Abs(down.Spine + down.Chest) <= T.MaxDownDeg + 0.001f);
		Assert.True(up.Spine + up.Chest <= T.MaxBodyDeg + 0.001f);
	}

	[Fact]
	public void PronePitchIsClamped()
	{
		var (spine, chest, head) = AimOffsetSolver.Distribute(T, 80f, 1f);
		Assert.Equal(T.PronePitchMaxDeg, spine + chest + head, 3);
		var (s2, c2, h2) = AimOffsetSolver.Distribute(T, -80f, 1f);
		Assert.Equal(T.PronePitchMinDeg, s2 + c2 + h2, 3);
	}
}

public class RealClipMappingTests
{
	[Fact]
	public void StandAndCrouchMapToPackClipsProneStaysGenerated()
	{
		Assert.Equal("Idle_Loop", LocomotionClips.ClipFor(GaitStance.Stand, 0f));
		Assert.Equal("Walk_Loop", LocomotionClips.ClipFor(GaitStance.Stand, 0.5f));
		Assert.Equal("Sprint_Loop", LocomotionClips.ClipFor(GaitStance.Stand, 1f));
		Assert.Equal("Crouch_Idle_Loop", LocomotionClips.ClipFor(GaitStance.Crouch, 0f));
		Assert.Equal("Crouch_Fwd_Loop", LocomotionClips.ClipFor(GaitStance.Crouch, 1f));
		Assert.Null(LocomotionClips.ClipFor(GaitStance.Prone, 0.5f));
	}

	[Fact]
	public void FrontPlaysForwardBackPlaysReversedSidesStayGenerated()
	{
		foreach (var d in new[] { 0, 1, 7 })
			Assert.Equal((true, false), LocomotionClips.DirectionUse(d));
		foreach (var d in new[] { 3, 4, 5 })
			Assert.Equal((true, true), LocomotionClips.DirectionUse(d));
		foreach (var d in new[] { 2, 6 })
			Assert.False(LocomotionClips.DirectionUse(d).Real);
	}
}
