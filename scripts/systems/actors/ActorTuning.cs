using System;
using System.Globalization;

namespace ProjectD;

/// <summary>
/// Turns an actor's resolved stats into the tuning records the AI systems already use (composable actors spec s5).
/// Pure; read again whenever a stat changed.
/// </summary>
public static class ActorTuning
{
	public static PerceptionTuning Perception(StatSet s) => new(
		s.Get(Stats.Perception.ViewDistance), s.Get(Stats.Perception.FovDeg), s.Get(Stats.Perception.PeripheralDeg),
		s.Get(Stats.Perception.PeripheralDistance), s.Get(Stats.Perception.CrouchVisibility),
		s.Get(Stats.Perception.ProneVisibility), s.Get(Stats.Perception.StillVisibility),
		s.Get(Stats.Perception.MovingVisibility), s.Get(Stats.Awareness.HearingMult), s.Get(Stats.Awareness.NoticeS),
		s.Get(Stats.Awareness.SuspicionThreshold), s.Get(Stats.Awareness.HoldS), s.Get(Stats.Awareness.DecayPerS));

	public static BrainTuning Brain(StatSet s) => new(
		s.Get(Stats.Brain.ArrivalRadius), s.Get(Stats.Brain.LookAroundS), s.Get(Stats.Brain.LostTargetS),
		s.Get(Stats.Brain.SearchS), s.Get(Stats.Brain.PatrolWaitS));

	public static TacticsTuning Tactics(StatSet s) => TacticsTuning.Default with
	{
		Courage = s.Get(Stats.Tactics.Courage),
		Aggression = s.Get(Stats.Tactics.Aggression),
		EngageDistance = s.Get(Stats.Tactics.EngageDistance),
		BurstsBeforeCover = s.GetInt(Stats.Tactics.BurstsBeforeCover),
	};

	public static AimMotorTuning Aim(StatSet s) => new(
		s.Get(Stats.Aim.Accuracy), s.Get(Stats.Aim.ErrorMaxDeg), s.Get(Stats.Aim.ErrorMinDeg),
		s.Get(Stats.Aim.ErrorPer100MDeg), s.Get(Stats.Aim.MovingMult), s.Get(Stats.Aim.SuppressedMult),
		s.Get(Stats.Aim.SettleS), s.Get(Stats.Aim.SettleFloor), s.Get(Stats.Aim.RecoilControl),
		s.Get(Stats.Aim.WanderHz), s.Get(Stats.Aim.ShakeDeg));

	public static UnderFireTuning UnderFire(StatSet s) => new(
		s.Get(Stats.React.DelayMinS), s.Get(Stats.React.DelayMaxS), s.Get(Stats.React.CoverMaxM),
		s.Get(Stats.React.ProneHoldS), s.Get(Stats.Move.GetUpS));

	/// <summary>"Allied PMC", "allied_pmc" and "AlliedPmc" all name the same faction.</summary>
	public static Faction ParseFaction(string text)
	{
		var key = text.Replace(" ", "").Replace("_", "");
		return Enum.TryParse<Faction>(key, true, out var f)
			? f
			: throw new ArgumentException($"unknown faction '{text}'");
	}

	/// <summary>Body colour cells are three numbers, red, green, blue in 0..1; null when absent or malformed.</summary>
	public static (float R, float G, float B)? ParseColor(System.Collections.Generic.IReadOnlyList<string> cells)
	{
		if (cells.Count != 3)
			return null;
		var v = new float[3];
		for (var i = 0; i < 3; i++)
			if (!float.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
				return null;
		return (v[0], v[1], v[2]);
	}
}
