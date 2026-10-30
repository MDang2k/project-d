using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>
/// One named tuning number (composable actors spec s1.1): its default, its hard limits and how it is shown.
/// </summary>
/// <param name="Integer">Whole numbers only (counts); the resolved value is rounded.</param>
public sealed record StatDef(
	string Id,
	float Default,
	float Min,
	float Max,
	string Unit = "",
	bool HigherIsBetter = true,
	bool Integer = false)
{
	/// <summary>The area before the first dot (perception, aim, health...), used to group the inspector.</summary>
	public string Group => Id[..Id.IndexOf('.')];
}

/// <summary>
/// The registry of stats. A sheet column, a modifier or a code read with an id that is not here is an error, so a
/// typo cannot silently do nothing. Adding a stat means adding it here and the code that reads it.
/// </summary>
public static class Stats
{
	public static class Perception
	{
		public const string ViewDistance = "perception.view_distance";
		public const string FovDeg = "perception.fov_deg";
		public const string PeripheralDeg = "perception.peripheral_deg";
		public const string PeripheralDistance = "perception.peripheral_distance";
		public const string CrouchVisibility = "perception.crouch_visibility";
		public const string ProneVisibility = "perception.prone_visibility";
		public const string StillVisibility = "perception.still_visibility";
		public const string MovingVisibility = "perception.moving_visibility";
	}

	public static class Awareness
	{
		public const string HearingMult = "awareness.hearing_mult";
		public const string NoticeS = "awareness.notice_s";
		public const string SuspicionThreshold = "awareness.suspicion_threshold";
		public const string HoldS = "awareness.hold_s";
		public const string DecayPerS = "awareness.decay_per_s";
	}

	public static class Move
	{
		public const string WalkSpeed = "move.walk_speed";
		public const string RunSpeed = "move.run_speed";
	}

	public static class Brain
	{
		public const string ArrivalRadius = "brain.arrival_radius";
		public const string LookAroundS = "brain.look_around_s";
		public const string LostTargetS = "brain.lost_target_s";
		public const string SearchS = "brain.search_s";
		public const string PatrolWaitS = "brain.patrol_wait_s";
	}

	public static class Aim
	{
		public const string Accuracy = "aim.accuracy";
		public const string MaxSpreadDeg = "aim.max_spread_deg";
		public const string MinSpreadDeg = "aim.min_spread_deg";
		public const string DistanceSpreadPer100M = "aim.distance_spread_per_100m";
		public const string MovingMult = "aim.moving_mult";
		public const string SuppressedMult = "aim.suppressed_mult";
	}

	public static class Fire
	{
		public const string BurstMin = "fire.burst_min";
		public const string BurstMax = "fire.burst_max";
		public const string BurstPauseMinS = "fire.burst_pause_min_s";
		public const string BurstPauseMaxS = "fire.burst_pause_max_s";
		public const string ReactionS = "fire.reaction_s";
	}

	public static class Tactics
	{
		public const string Courage = "tactics.courage";
		public const string Aggression = "tactics.aggression";
		public const string EngageDistance = "tactics.engage_distance";
		public const string BurstsBeforeCover = "tactics.bursts_before_cover";
	}

	public static class Scavenge
	{
		public const string LootRadius = "scavenge.loot_radius";
	}

	public static class Carry
	{
		public const string BackpackChance = "carry.backpack_chance";
		public const string SpareMagazines = "carry.spare_magazines";
	}

	public static class Loadout
	{
		public const string ArmorChance = "loadout.armor_chance";
	}

	public static class Health
	{
		public const string Head = "health.head";
		public const string Thorax = "health.thorax";
		public const string Stomach = "health.stomach";
		public const string Arm = "health.arm";
		public const string Leg = "health.leg";
	}

	private static StatDef F(string id, float def, float min, float max, string unit = "", bool higherIsBetter = true)
		=> new(id, def, min, max, unit, higherIsBetter);

	private static StatDef I(string id, float def, float min, float max, string unit = "", bool higherIsBetter = true)
		=> new(id, def, min, max, unit, higherIsBetter, Integer: true);

	/// <summary>Every stat, defaults taken from the former AiPreset (removed in phase 3) and the code defaults of AimTuning and the body.</summary>
	public static IReadOnlyList<StatDef> All { get; } = new[]
	{
		F(Perception.ViewDistance, 80f, 0f, 400f, "m"),
		F(Perception.FovDeg, 110f, 10f, 360f, "deg"),
		F(Perception.PeripheralDeg, 200f, 10f, 360f, "deg"),
		F(Perception.PeripheralDistance, 8f, 0f, 100f, "m"),
		F(Perception.CrouchVisibility, 0.65f, 0f, 2f, "x", false),
		F(Perception.ProneVisibility, 0.4f, 0f, 2f, "x", false),
		F(Perception.StillVisibility, 0.8f, 0f, 2f, "x", false),
		F(Perception.MovingVisibility, 1.25f, 0f, 3f, "x", false),

		F(Awareness.HearingMult, 1f, 0f, 5f, "x"),
		F(Awareness.NoticeS, 1.2f, 0.05f, 30f, "s", false),
		F(Awareness.SuspicionThreshold, 0.35f, 0f, 1f),
		F(Awareness.HoldS, 3f, 0f, 60f, "s"),
		F(Awareness.DecayPerS, 0.15f, 0f, 5f, "/s", false),

		F(Move.WalkSpeed, 1.6f, 0.1f, 10f, "m/s"),
		F(Move.RunSpeed, 3.4f, 0.1f, 12f, "m/s"),

		F(Brain.ArrivalRadius, 1.2f, 0.1f, 10f, "m"),
		F(Brain.LookAroundS, 3f, 0f, 60f, "s"),
		F(Brain.LostTargetS, 4f, 0f, 60f, "s"),
		F(Brain.SearchS, 10f, 0f, 120f, "s"),
		F(Brain.PatrolWaitS, 2f, 0f, 60f, "s"),

		F(Aim.Accuracy, 0.5f, 0f, 1f),
		F(Aim.MaxSpreadDeg, 3f, 0f, 30f, "deg", false),
		F(Aim.MinSpreadDeg, 0.15f, 0f, 30f, "deg", false),
		F(Aim.DistanceSpreadPer100M, 1.5f, 0f, 30f, "deg", false),
		F(Aim.MovingMult, 1.8f, 1f, 10f, "x", false),
		F(Aim.SuppressedMult, 2.5f, 1f, 10f, "x", false),

		I(Fire.BurstMin, 2f, 1f, 60f, "rounds"),
		I(Fire.BurstMax, 5f, 1f, 60f, "rounds"),
		F(Fire.BurstPauseMinS, 0.5f, 0f, 30f, "s", false),
		F(Fire.BurstPauseMaxS, 1.4f, 0f, 30f, "s", false),
		F(Fire.ReactionS, 0.6f, 0f, 10f, "s", false),

		F(Tactics.Courage, 0.5f, 0f, 1f),
		F(Tactics.Aggression, 0.5f, 0f, 1f),
		F(Tactics.EngageDistance, 25f, 1f, 300f, "m"),
		I(Tactics.BurstsBeforeCover, 3f, 1f, 30f, "bursts"),

		F(Scavenge.LootRadius, 35f, 0f, 300f, "m"),

		F(Carry.BackpackChance, 0.5f, 0f, 1f),
		I(Carry.SpareMagazines, 2f, 0f, 20f, "mags"),

		F(Loadout.ArmorChance, 1f, 0f, 1f),

		I(Health.Head, 35f, 1f, 2000f, "hp"),
		I(Health.Thorax, 85f, 1f, 2000f, "hp"),
		I(Health.Stomach, 70f, 1f, 2000f, "hp"),
		I(Health.Arm, 60f, 1f, 2000f, "hp"),
		I(Health.Leg, 65f, 1f, 2000f, "hp"),
	};

	private static readonly Dictionary<string, StatDef> ById = All.ToDictionary(s => s.Id);

	public static bool Exists(string id) => ById.ContainsKey(id);

	public static StatDef Get(string id)
		=> ById.TryGetValue(id, out var def) ? def : throw new ArgumentException($"unknown stat id '{id}'", nameof(id));

	public static bool TryGet(string id, out StatDef def) => ById.TryGetValue(id, out def!);
}
