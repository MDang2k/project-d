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

	[ExportGroup("Combat")]
	/// <summary>0 hopeless .. 1 deadly shot (see AimTuning).</summary>
	[Export] public float Accuracy { get; set; } = 0.5f;
	/// <summary>0 cowardly .. 1 fearless: when injury and incoming fire make the AI back off.</summary>
	[Export] public float Courage { get; set; } = 0.5f;
	/// <summary>0 passive .. 1 pushes and flanks.</summary>
	[Export] public float Aggression { get; set; } = 0.5f;
	[Export] public int BurstMin { get; set; } = 2;
	[Export] public int BurstMax { get; set; } = 5;
	[Export] public float BurstPauseMinS { get; set; } = 0.5f;
	[Export] public float BurstPauseMaxS { get; set; } = 1.4f;
	/// <summary>Delay between first seeing a target and the first shot.</summary>
	[Export] public float ReactionS { get; set; } = 0.6f;
	[Export] public int BurstsBeforeCover { get; set; } = 3;
	/// <summary>Bandage-like item used when badly hurt; null means this AI cannot heal.</summary>
	[Export] public MedicalData? HealItem { get; set; }

	[ExportGroup("Scavenging")]
	/// <summary>Walks to nearby bodies and strips them when nothing else is going on.</summary>
	[Export] public bool LootsCorpses { get; set; }
	[Export] public float LootRadius { get; set; } = 35f;

	[ExportGroup("Carrying kit (what a body holds when searched)")]
	/// <summary>Chest rig item id, or empty for none.</summary>
	[Export] public string RigId { get; set; } = "";
	/// <summary>Backpack item id, or empty for none.</summary>
	[Export] public string BackpackId { get; set; } = "";
	[Export] public float BackpackChance { get; set; } = 0.5f;
	/// <summary>Full spare magazines for the weapon, kept in the rig, backpack or pockets.</summary>
	[Export] public int SpareMagazines { get; set; } = 2;
	/// <summary>Rolled into the pockets (rig and backpack too when room is left).</summary>
	[Export] public LootTable? PocketTable { get; set; }

	[ExportGroup("Loadout")]
	/// <summary>Weapon when WeaponChoices is empty.</summary>
	[Export] public WeaponData? Weapon { get; set; }
	/// <summary>One of these is picked at random per spawn (loadout table).</summary>
	[Export] public Array<WeaponData> WeaponChoices { get; set; } = new();
	[Export] public Array<ArmorData> Armor { get; set; } = new();
	/// <summary>Chance that each armor piece is actually worn.</summary>
	[Export] public float ArmorChance { get; set; } = 1f;
	[Export] public HealthConfig? Health { get; set; }

	public TacticsTuning ToTacticsTuning() => TacticsTuning.Default with
	{
		Courage = Courage,
		Aggression = Aggression,
		EngageDistance = EngageDistance,
		BurstsBeforeCover = BurstsBeforeCover,
	};

	public BrainTuning ToBrainTuning() => new(ArrivalRadius, LookAroundS, LostTargetS, SearchS, PatrolWaitS);

	public PerceptionTuning ToPerceptionTuning() => new(
		ViewDistance, FovDeg, PeripheralDeg, PeripheralDistance, CrouchVisibility, ProneVisibility,
		StillVisibility, MovingVisibility, HearingMultiplier, NoticeSeconds, SuspicionThreshold,
		HoldSeconds, DecayPerS);
}
