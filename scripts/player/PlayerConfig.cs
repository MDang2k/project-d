using Godot;

namespace ProjectD;

/// <summary>
/// Tuning for player movement (GDD s6). Numbers are placeholders to tune in playtest.
/// Per-tier arrays are indexed by <see cref="WeightTier"/>: Light, Moderate, Heavy, Overloaded.
/// </summary>
[GlobalClass]
public partial class PlayerConfig : Resource
{
	[ExportGroup("Speeds (m/s)")]
	[Export] public float WalkSpeed { get; set; } = 2.8f;
	[Export] public float SprintSpeed { get; set; } = 5.2f;
	[Export] public float CrouchSpeed { get; set; } = 1.6f;
	[Export] public float CrouchSprintSpeed { get; set; } = 3.0f;
	[Export] public float ProneSpeed { get; set; } = 0.7f;

	[ExportGroup("Inertia (m/s^2)")]
	[Export] public float Acceleration { get; set; } = 10f;
	[Export] public float Deceleration { get; set; } = 14f;
	/// <summary>Fraction of acceleration available while airborne.</summary>
	[Export] public float AirControl { get; set; } = 0.2f;

	[ExportGroup("Jump")]
	[Export] public float JumpVelocity { get; set; } = 4.2f;
	[Export] public float JumpStaminaCost { get; set; } = 12f;

	[ExportGroup("Stances")]
	[Export] public float StandHeight { get; set; } = 1.8f;
	[Export] public float CrouchHeight { get; set; } = 1.2f;
	[Export] public float ProneHeight { get; set; } = 0.6f;
	[Export] public float CapsuleRadius { get; set; } = 0.3f;
	[Export] public float StandEyeHeight { get; set; } = 1.65f;
	[Export] public float CrouchEyeHeight { get; set; } = 1.1f;
	[Export] public float ProneEyeHeight { get; set; } = 0.4f;
	/// <summary>Eye height change speed in m/s.</summary>
	[Export] public float EyeHeightSpeed { get; set; } = 4f;

	[ExportGroup("Weight tiers")]
	/// <summary>Carried kg at which Moderate, Heavy, Overloaded begin.</summary>
	[Export] public float[] TierThresholdsKg { get; set; } = WeightTuning.Default.ThresholdsKg;
	[Export] public float[] TierSpeedMult { get; set; } = WeightTuning.Default.SpeedMult;
	[Export] public float[] TierAccelMult { get; set; } = WeightTuning.Default.AccelMult;
	[Export] public float[] TierTurnMult { get; set; } = WeightTuning.Default.TurnMult;
	[Export] public float[] TierStaminaDrainMult { get; set; } = WeightTuning.Default.StaminaDrainMult;
	[Export] public float[] TierStaminaRecoveryMult { get; set; } = WeightTuning.Default.StaminaRecoveryMult;
	/// <summary>Highest tier index that may still sprint (1 = Moderate).</summary>
	[Export] public int SprintMaxTier { get; set; } = 1;

	[ExportGroup("Stamina")]
	[Export] public float LegsMax { get; set; } = 100f;
	[Export] public float ArmsMax { get; set; } = 100f;
	[Export] public float SprintDrainPerS { get; set; } = 9f;
	[Export] public float RecoveryPerS { get; set; } = 8f;
	[Export] public float RecoveryDelayS { get; set; } = 1f;
	/// <summary>After running out, the pool must refill to this value before use resumes.</summary>
	[Export] public float ResumeThreshold { get; set; } = 20f;

	[ExportGroup("Lean")]
	/// <summary>Sideways head shift in metres at full lean.</summary>
	[Export] public float LeanDistance { get; set; } = 0.4f;
	[Export] public float LeanRollDeg { get; set; } = 12f;
	/// <summary>Lean units per second (full lean takes 1 / speed s).</summary>
	[Export] public float LeanSpeed { get; set; } = 6f;
	/// <summary>Roll pivot height as a fraction of eye height (waist).</summary>
	[Export] public float LeanPivotFraction { get; set; } = 0.55f;
	/// <summary>Clearance kept between head and wall when leaning.</summary>
	[Export] public float LeanWallMargin { get; set; } = 0.2f;

	[ExportGroup("Free look")]
	[Export] public float FreeLookYawDeg { get; set; } = 110f;
	[Export] public float FreeLookPitchDeg { get; set; } = 60f;
	[Export] public float FreeLookReturnDegS { get; set; } = 360f;

	[ExportGroup("Mantle")]
	[Export] public float MantleMinHeight { get; set; } = 0.3f;
	[Export] public float MantleMaxHeight { get; set; } = 1.3f;
	[Export] public float MantleReach { get; set; } = 0.8f;
	/// <summary>How far past the ledge edge the player lands.</summary>
	[Export] public float MantleTopDepth { get; set; } = 0.4f;
	[Export] public float MantleDurationS { get; set; } = 0.7f;
	[Export] public float MantleStaminaCost { get; set; } = 15f;

	[ExportGroup("Body (placeholder)")]
	/// <summary>Hips-to-eye distance when upright; keeps the head under the camera.</summary>
	[Export] public float TorsoToEyeHeight { get; set; } = 0.70f;
	[Export] public float UpperArmLength { get; set; } = 0.32f;
	[Export] public float ForearmLength { get; set; } = 0.30f;
	[Export] public float ProneHipsHeight { get; set; } = 0.15f;
	/// <summary>How far behind the camera the hips sit when prone (body lies forward).</summary>
	[Export] public float ProneBodyBack { get; set; } = 0.7f;
	[Export] public float CrouchThighDeg { get; set; } = 80f;
	[Export] public float CrouchShinDeg { get; set; } = 100f;
	/// <summary>Held item position relative to the head (camera pitch node).</summary>
	[Export] public Vector3 HeldItemOffset { get; set; } = new(0.08f, -0.22f, -0.36f);
	[Export] public Vector3 GripRightOffset { get; set; } = new(0f, -0.04f, 0.12f);
	[Export] public Vector3 GripLeftOffset { get; set; } = new(0f, -0.03f, -0.15f);
	[Export] public Vector3 ElbowHintRight { get; set; } = new(0.45f, -0.55f, 0.05f);
	[Export] public Vector3 ElbowHintLeft { get; set; } = new(-0.45f, -0.55f, 0.05f);

	[ExportGroup("Handling")]
	[Export] public float HipFovDeg { get; set; } = 75f;
	[Export] public float AdsFovDeg { get; set; } = 58f;
	/// <summary>Mouse sensitivity multiplier at full ADS.</summary>
	[Export] public float AdsSensitivityMult { get; set; } = 0.55f;
	/// <summary>Distance of the rear sight from the eye at full ADS, metres.</summary>
	[Export] public float AdsEyeRelief { get; set; } = 0.3f;
	[Export] public float BreathHoldDrainPerS { get; set; } = 12f;
	[Export] public float CrouchSwayMult { get; set; } = 0.75f;
	[Export] public float ProneSwayMult { get; set; } = 0.5f;
	[Export] public float ExhaustionSwayGain { get; set; } = 1.5f;
	[Export] public float MoveSwayGain { get; set; } = 1.2f;
	[Export] public float AdsSwayMult { get; set; } = 0.6f;
	[Export] public float BreathHoldSwayMult { get; set; } = 0.1f;
	[Export] public float WeightSwayPerKg { get; set; } = 0.04f;
	[Export] public float CrouchRecoilMult { get; set; } = 0.85f;
	[Export] public float ProneRecoilMult { get; set; } = 0.65f;
	[Export] public float AdsRecoilMult { get; set; } = 0.8f;

	[ExportGroup("Look")]
	[Export] public float MouseSensitivity { get; set; } = 0.0022f;
	[Export] public float SprintTurnMult { get; set; } = 0.6f;
	[Export] public float PitchLimitDeg { get; set; } = 85f;

	public WeightTuning ToWeightTuning() => new(
		TierThresholdsKg, TierSpeedMult, TierAccelMult, TierTurnMult,
		TierStaminaDrainMult, TierStaminaRecoveryMult, SprintMaxTier);

	public HandlingTuning ToHandlingTuning() => new(
		CrouchSwayMult, ProneSwayMult, ExhaustionSwayGain, MoveSwayGain, AdsSwayMult,
		BreathHoldSwayMult, WeightSwayPerKg, CrouchRecoilMult, ProneRecoilMult, AdsRecoilMult);

	public MovementTuning ToMovementTuning() => new(
		WalkSpeed, SprintSpeed, CrouchSpeed, CrouchSprintSpeed, ProneSpeed,
		Acceleration, Deceleration, AirControl,
		StandHeight, CrouchHeight, ProneHeight,
		StandEyeHeight, CrouchEyeHeight, ProneEyeHeight);
}
