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
		public const string SprintSpeed = "move.sprint_speed";
		public const string CrouchSpeed = "move.crouch_speed";
		public const string ProneSpeed = "move.prone_speed";
		public const string TurnDegS = "move.turn_deg_s";
		public const string Accel = "move.accel";
		public const string Decel = "move.decel";
		public const string GetUpS = "move.get_up_s";
	}

	/// <summary>The fast reaction to being shot at (AI combat v2 spec s4).</summary>
	public static class React
	{
		public const string DelayMinS = "react.delay_min_s";
		public const string DelayMaxS = "react.delay_max_s";
		public const string TurnDegS = "react.turn_deg_s";
		public const string CoverMaxM = "react.cover_max_m";
		public const string NearMissM = "react.near_miss_m";
		public const string ProneHoldS = "react.prone_hold_s";
	}

	/// <summary>Taking cover, peeking and firing blind (AI combat v2 spec s5).</summary>
	public static class Cover
	{
		public const string SearchM = "cover.search_m";
		public const string LeanM = "cover.lean_m";
		public const string BlindInjury = "cover.blind_injury";
		public const string BlindErrorMult = "cover.blind_error_mult";
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
		public const string ErrorMaxDeg = "aim.error_max_deg";
		public const string ErrorMinDeg = "aim.error_min_deg";
		public const string ErrorPer100MDeg = "aim.error_per_100m_deg";
		public const string SettleS = "aim.settle_s";
		public const string SettleFloor = "aim.settle_floor";
		public const string RecoilControl = "aim.recoil_control";
		public const string WanderHz = "aim.wander_hz";
		public const string ShakeDeg = "aim.shake_deg";
		public const string Consistency = "aim.consistency";
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

	/// <summary>Squad behaviour: shouting, discipline, despawning after a rout (AI combat v2 spec s8).</summary>
	public static class Squad
	{
		public const string CalloutRadiusM = "squad.callout_radius_m";
		public const string CalloutEveryS = "squad.callout_every_s";
		public const string Disciplined = "squad.disciplined";
		public const string DespawnM = "squad.despawn_m";
	}

	/// <summary>Temperament beside aggression and courage (those are in Tactics): how easily the nerve breaks.</summary>
	public static class Temper
	{
		public const string Panic = "temper.panic";
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
		F(Move.SprintSpeed, 5.2f, 0.1f, 12f, "m/s"),
		F(Move.CrouchSpeed, 1.6f, 0.1f, 10f, "m/s"),
		F(Move.ProneSpeed, 0.7f, 0.1f, 5f, "m/s"),
		F(Move.TurnDegS, 220f, 30f, 1080f, "deg/s"),
		F(Move.Accel, 10f, 1f, 100f, "m/s2"),
		F(Move.Decel, 14f, 1f, 100f, "m/s2"),
		F(Move.GetUpS, 0.8f, 0f, 5f, "s", false),

		F(React.DelayMinS, 0.15f, 0f, 3f, "s", false),
		F(React.DelayMaxS, 0.30f, 0f, 3f, "s", false),
		F(React.TurnDegS, 540f, 30f, 1440f, "deg/s"),
		F(React.CoverMaxM, 8f, 0f, 40f, "m"),
		F(React.NearMissM, 3f, 0f, 10f, "m"),
		F(React.ProneHoldS, 3f, 0f, 30f, "s"),

		F(Cover.SearchM, 10f, 2f, 40f, "m"),
		F(Cover.LeanM, 0.45f, 0f, 1f, "m"),
		F(Cover.BlindInjury, 0.6f, 0f, 1f, "", false),
		F(Cover.BlindErrorMult, 4f, 1f, 20f, "x", false),

		F(Brain.ArrivalRadius, 1.2f, 0.1f, 10f, "m"),
		F(Brain.LookAroundS, 3f, 0f, 60f, "s"),
		F(Brain.LostTargetS, 4f, 0f, 60f, "s"),
		F(Brain.SearchS, 10f, 0f, 120f, "s"),
		F(Brain.PatrolWaitS, 2f, 0f, 60f, "s"),

		F(Aim.Accuracy, 0.5f, 0f, 1f),
		F(Aim.ErrorMaxDeg, 2f, 0f, 30f, "deg", false),
		F(Aim.ErrorMinDeg, 0.1f, 0f, 30f, "deg", false),
		F(Aim.ErrorPer100MDeg, 1f, 0f, 30f, "deg", false),
		F(Aim.SettleS, 1.2f, 0.1f, 10f, "s", false),
		F(Aim.SettleFloor, 0.3f, 0f, 1f, "x", false),
		F(Aim.RecoilControl, 0.45f, 0f, 1f),
		F(Aim.WanderHz, 0.8f, 0.1f, 5f, "Hz"),
		F(Aim.ShakeDeg, 1.5f, 0f, 10f, "deg", false),
		F(Aim.Consistency, 0.7f, 0f, 1f),
		F(Aim.MovingMult, 1.8f, 1f, 10f, "x", false),
		F(Aim.SuppressedMult, 2.5f, 1f, 10f, "x", false),

		I(Fire.BurstMin, 2f, 1f, 60f, "rounds"),
		I(Fire.BurstMax, 5f, 1f, 60f, "rounds"),
		F(Fire.BurstPauseMinS, 0.5f, 0f, 30f, "s", false),
		F(Fire.BurstPauseMaxS, 1.4f, 0f, 30f, "s", false),
		F(Fire.ReactionS, 0.6f, 0f, 10f, "s", false),

		F(Temper.Panic, 0.3f, 0f, 1f),

		F(Squad.CalloutRadiusM, 30f, 0f, 100f, "m"),
		F(Squad.CalloutEveryS, 6f, 1f, 60f, "s", false),
		F(Squad.Disciplined, 0f, 0f, 1f),
		F(Squad.DespawnM, 60f, 10f, 300f, "m"),

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
