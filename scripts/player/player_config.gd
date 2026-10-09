class_name PlayerConfig
extends Resource
## Tuning for player movement (GDD s6). All numbers are placeholders to tune in playtest.
## Per-tier arrays are indexed by WeightModel.Tier: LIGHT, MODERATE, HEAVY, OVERLOADED.

@export_group("Speeds (m/s)")
@export var walk_speed: float = 2.8
@export var sprint_speed: float = 5.2
@export var crouch_speed: float = 1.6
@export var crouch_sprint_speed: float = 3.0
@export var prone_speed: float = 0.7

@export_group("Inertia (m/s^2)")
@export var acceleration: float = 10.0
@export var deceleration: float = 14.0
## Fraction of acceleration available while airborne.
@export var air_control: float = 0.2

@export_group("Jump")
@export var jump_velocity: float = 4.2
@export var jump_stamina_cost: float = 12.0

@export_group("Stances")
@export var stand_height: float = 1.8
@export var crouch_height: float = 1.2
@export var prone_height: float = 0.6
@export var capsule_radius: float = 0.3
@export var stand_eye_height: float = 1.65
@export var crouch_eye_height: float = 1.1
@export var prone_eye_height: float = 0.4
## Eye height change speed in m/s.
@export var eye_height_speed: float = 4.0

@export_group("Weight tiers")
## Carried kg at which MODERATE, HEAVY, OVERLOADED begin.
@export var tier_thresholds_kg: PackedFloat32Array = PackedFloat32Array([20.0, 35.0, 50.0])
@export var tier_speed_mult: PackedFloat32Array = PackedFloat32Array([1.0, 0.9, 0.7, 0.3])
@export var tier_accel_mult: PackedFloat32Array = PackedFloat32Array([1.0, 0.8, 0.55, 0.35])
@export var tier_turn_mult: PackedFloat32Array = PackedFloat32Array([1.0, 0.9, 0.75, 0.5])
@export var tier_stamina_drain_mult: PackedFloat32Array = PackedFloat32Array([1.0, 1.4, 1.8, 2.5])
@export var tier_stamina_recovery_mult: PackedFloat32Array = PackedFloat32Array([1.0, 0.8, 0.6, 0.4])
## Highest tier index that may still sprint (1 = MODERATE).
@export var sprint_max_tier: int = 1

@export_group("Stamina")
@export var legs_max: float = 100.0
@export var arms_max: float = 100.0
@export var sprint_drain_per_s: float = 9.0
@export var recovery_per_s: float = 8.0
@export var recovery_delay_s: float = 1.0
## After running out, the pool must refill to this value before use resumes.
@export var resume_threshold: float = 20.0

@export_group("Look")
@export var mouse_sensitivity: float = 0.0022
@export var sprint_turn_mult: float = 0.6
@export var pitch_limit_deg: float = 85.0
