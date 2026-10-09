class_name MovementModel
extends RefCounted
## Speed selection and horizontal inertia (GDD s6). Pure logic, no nodes.

enum Stance { STAND, CROUCH, PRONE }

var _config: PlayerConfig
var _weights: WeightModel


func _init(config: PlayerConfig, weights: WeightModel) -> void:
	_config = config
	_weights = weights


## Prone never sprints; sprinting while crouched is the crouch-sprint.
func target_speed(stance: Stance, sprinting: bool, tier: WeightModel.Tier) -> float:
	var base: float
	match stance:
		Stance.PRONE:
			base = _config.prone_speed
		Stance.CROUCH:
			base = _config.crouch_sprint_speed if sprinting else _config.crouch_speed
		_:
			base = _config.sprint_speed if sprinting else _config.walk_speed
	return base * _weights.speed_mult(tier)


## Moves current horizontal velocity toward wish_dir * speed. Heavier tiers
## accelerate and brake more slowly; airborne control is reduced.
func step_horizontal(current: Vector2, wish_dir: Vector2, speed: float,
		tier: WeightModel.Tier, grounded: bool, delta: float) -> Vector2:
	var target: Vector2 = wish_dir * speed
	var speeding_up: bool = target.length() >= current.length()
	var rate: float = _config.acceleration if speeding_up else _config.deceleration
	rate *= _weights.accel_mult(tier)
	if not grounded:
		rate *= _config.air_control
	return current.move_toward(target, rate * delta)


func capsule_height(stance: Stance) -> float:
	match stance:
		Stance.PRONE:
			return _config.prone_height
		Stance.CROUCH:
			return _config.crouch_height
		_:
			return _config.stand_height


func eye_height(stance: Stance) -> float:
	match stance:
		Stance.PRONE:
			return _config.prone_eye_height
		Stance.CROUCH:
			return _config.crouch_eye_height
		_:
			return _config.stand_eye_height
