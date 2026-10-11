using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>
/// One AI archetype (GDD s16): faction, senses and loadout. Behavior numbers join in later M5 steps.
/// Tune in data/ai/*.tres.
/// </summary>
[GlobalClass]
public partial class AiPreset : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public Faction Faction { get; set; } = Faction.Looters;
	[Export] public Color BodyColor { get; set; } = new(0.45f, 0.4f, 0.3f);

	[ExportGroup("Vision")]
	[Export] public float ViewDistance { get; set; } = 80f;
	[Export] public float FovDeg { get; set; } = 110f;
	[Export] public float PeripheralDeg { get; set; } = 200f;
	[Export] public float PeripheralDistance { get; set; } = 8f;
	[Export] public float CrouchVisibility { get; set; } = 0.65f;
	[Export] public float ProneVisibility { get; set; } = 0.4f;
	[Export] public float StillVisibility { get; set; } = 0.8f;
	[Export] public float MovingVisibility { get; set; } = 1.25f;

	[ExportGroup("Awareness")]
	[Export] public float HearingMultiplier { get; set; } = 1f;
	[Export] public float NoticeSeconds { get; set; } = 1.2f;
	[Export] public float SuspicionThreshold { get; set; } = 0.35f;
	[Export] public float HoldSeconds { get; set; } = 3f;
	[Export] public float DecayPerS { get; set; } = 0.15f;

	[ExportGroup("Behavior")]
	/// <summary>Distance at which the AI stops closing in on a target it can see, metres.</summary>
	[Export] public float EngageDistance { get; set; } = 25f;
	[Export] public float WalkSpeed { get; set; } = 1.6f;
	[Export] public float RunSpeed { get; set; } = 3.4f;
	[Export] public float ArrivalRadius { get; set; } = 1.2f;
	[Export] public float LookAroundS { get; set; } = 3f;
	[Export] public float LostTargetS { get; set; } = 4f;
	[Export] public float SearchS { get; set; } = 10f;
	[Export] public float PatrolWaitS { get; set; } = 2f;

	[ExportGroup("Loadout")]
	[Export] public WeaponData? Weapon { get; set; }
	[Export] public Array<ArmorData> Armor { get; set; } = new();
	[Export] public HealthConfig? Health { get; set; }

	public BrainTuning ToBrainTuning() => new(ArrivalRadius, LookAroundS, LostTargetS, SearchS, PatrolWaitS);

	public PerceptionTuning ToPerceptionTuning() => new(
		ViewDistance, FovDeg, PeripheralDeg, PeripheralDistance, CrouchVisibility, ProneVisibility,
		StillVisibility, MovingVisibility, HearingMultiplier, NoticeSeconds, SuspicionThreshold,
		HoldSeconds, DecayPerS);
}
