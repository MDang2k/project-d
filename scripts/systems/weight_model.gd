class_name WeightModel
extends RefCounted
## Maps carried weight to a mobility tier and its multipliers (GDD s6). Pure logic.

enum Tier { LIGHT, MODERATE, HEAVY, OVERLOADED }

var _config: PlayerConfig


func _init(config: PlayerConfig) -> void:
	_config = config


## strength_bonus_kg raises every threshold (Strength skill).
func tier_for(weight_kg: float, strength_bonus_kg: float = 0.0) -> Tier:
	var tier: int = 0
	for threshold: float in _config.tier_thresholds_kg:
		if weight_kg >= threshold + strength_bonus_kg:
			tier += 1
	return tier as Tier


func speed_mult(tier: Tier) -> float:
	return _config.tier_speed_mult[tier]


func accel_mult(tier: Tier) -> float:
	return _config.tier_accel_mult[tier]


func turn_mult(tier: Tier) -> float:
	return _config.tier_turn_mult[tier]


func stamina_drain_mult(tier: Tier) -> float:
	return _config.tier_stamina_drain_mult[tier]


func stamina_recovery_mult(tier: Tier) -> float:
	return _config.tier_stamina_recovery_mult[tier]


func can_sprint(tier: Tier) -> bool:
	return tier <= _config.sprint_max_tier
