class_name StaminaPool
extends RefCounted
## One stamina pool (legs or arms, GDD s6). Recovery pauses briefly after spending;
## after hitting zero the pool is locked until it refills to resume_threshold.

signal depleted
signal recovered

var max_value: float
var value: float
var resume_threshold: float
var recovery_delay_s: float
var is_exhausted: bool = false

var _delay_left: float = 0.0


func _init(max_amount: float, resume_at: float, delay_s: float) -> void:
	max_value = max_amount
	value = max_amount
	resume_threshold = resume_at
	recovery_delay_s = delay_s


func spend(amount: float) -> void:
	if amount <= 0.0:
		return
	value = maxf(0.0, value - amount)
	_delay_left = recovery_delay_s
	if value <= 0.0 and not is_exhausted:
		is_exhausted = true
		depleted.emit()


## Call every frame; recovery_rate is units per second (already scaled by weight tier).
func tick(delta: float, recovery_rate: float) -> void:
	if _delay_left > 0.0:
		_delay_left = maxf(0.0, _delay_left - delta)
		return
	value = minf(max_value, value + recovery_rate * delta)
	if is_exhausted and value >= resume_threshold:
		is_exhausted = false
		recovered.emit()


func fraction() -> float:
	return value / max_value
