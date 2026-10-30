using System;

namespace ProjectD;

/// <summary>How much work an AI does this frame, by distance to the player (AI combat v2 spec s9).</summary>
public enum AiBand
{
	/// <summary>Near the player: full rate, 3-point vision, full cover sweeps.</summary>
	Combat,
	/// <summary>Middle distance, or fighting something anywhere: 10 Hz, 2-point vision, a cheap cover sweep.</summary>
	Awareness,
	/// <summary>Far away and quiet: a slow tick, no vision, woken by sound or damage.</summary>
	Dormant,
}

/// <summary>Distances and rates of the bands. Placeholders to tune against the frame budget.</summary>
public sealed record AiBandTuning(
	float CombatM = 40f,
	float AwarenessM = 120f,
	float AwarenessIntervalS = 0.1f,
	float DormantIntervalS = 0.6f,
	int CombatCoverRays = 32,
	int AwarenessCoverRays = 8,
	/// <summary>Cover sweeps (32-ray searches) allowed across all AI in one physics frame.</summary>
	int MaxSweepsPerFrame = 2,
	/// <summary>Seconds a sound or hit keeps a dormant AI awake.</summary>
	float WakeS = 6f);

/// <summary>Pure rules for the bands.</summary>
public static class AiTickBands
{
	/// <summary>
	/// An AI that is fighting anything (the player or another bot) or was woken is at least Awareness, so a fight
	/// 150 m away still runs, at 10 Hz.
	/// </summary>
	public static AiBand Classify(AiBandTuning t, float distanceToPlayer, bool fighting, bool woken)
	{
		if (distanceToPlayer <= t.CombatM)
			return AiBand.Combat;
		if (distanceToPlayer <= t.AwarenessM || fighting || woken)
			return AiBand.Awareness;
		return AiBand.Dormant;
	}

	/// <summary>Seconds between full thinking steps; zero means every physics frame.</summary>
	public static float ThinkIntervalS(AiBandTuning t, AiBand band) => band switch
	{
		AiBand.Combat => 0f,
		AiBand.Awareness => t.AwarenessIntervalS,
		_ => t.DormantIntervalS,
	};

	/// <summary>Seconds between sense ticks (vision and hearing).</summary>
	public static float SenseIntervalS(AiBandTuning t, AiBand band) => band switch
	{
		AiBand.Dormant => t.DormantIntervalS,
		_ => 0.1f,
	};

	/// <summary>Body points tested for sight: all three near the player, head and centre at middle range, none far away.</summary>
	public static int VisionPoints(AiBand band) => band switch
	{
		AiBand.Combat => 3,
		AiBand.Awareness => 2,
		_ => 0,
	};

	public static int CoverRays(AiBandTuning t, AiBand band) => band switch
	{
		AiBand.Combat => t.CombatCoverRays,
		AiBand.Awareness => t.AwarenessCoverRays,
		_ => 0,
	};
}

/// <summary>
/// Counters for the AI range and a per-frame budget for cover sweeps. Static because every AI shares them.
/// Frame numbers come from the caller so the type stays free of engine calls.
/// </summary>
public static class AiPerf
{
	private static ulong _frame = ulong.MaxValue;
	private static int _rays;
	private static int _sweeps;
	private static double _ms;

	public static int RaysLastFrame { get; private set; }
	public static int SweepsLastFrame { get; private set; }
	public static double MsLastFrame { get; private set; }

	/// <summary>Smoothed over about a second.</summary>
	public static float RaysAverage { get; private set; }
	public static double MsAverage { get; private set; }
	public static int PeakRays { get; private set; }

	private static void Roll(ulong frame)
	{
		if (frame == _frame)
			return;
		if (_frame != ulong.MaxValue)
		{
			RaysLastFrame = _rays;
			SweepsLastFrame = _sweeps;
			MsLastFrame = _ms;
			RaysAverage += (_rays - RaysAverage) * 0.05f;
			MsAverage += (_ms - MsAverage) * 0.05;
			PeakRays = Math.Max(PeakRays, _rays);
		}
		_frame = frame;
		_rays = 0;
		_sweeps = 0;
		_ms = 0;
	}

	public static void Ray(ulong frame, int count = 1)
	{
		Roll(frame);
		_rays += count;
	}

	public static void AddMs(ulong frame, double ms)
	{
		Roll(frame);
		_ms += ms;
	}

	/// <summary>True and counted when a sweep may run this frame; false when the budget is used up.</summary>
	public static bool TryBeginSweep(ulong frame, AiBandTuning t)
	{
		Roll(frame);
		if (_sweeps >= t.MaxSweepsPerFrame)
			return false;
		_sweeps++;
		return true;
	}

	public static void Reset()
	{
		_frame = ulong.MaxValue;
		_rays = _sweeps = RaysLastFrame = SweepsLastFrame = PeakRays = 0;
		_ms = MsLastFrame = MsAverage = 0;
		RaysAverage = 0;
	}
}
