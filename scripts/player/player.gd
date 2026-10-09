class_name Player
extends CharacterBody3D
## First-person player body (GDD s6). Thin wrapper: rules live in WeightModel,
## StaminaPool and MovementModel. Camera rig (lean, free look, sway) arrives in M1 step 2.

signal stance_changed(stance: MovementModel.Stance)

@export var config: PlayerConfig
@export var carried_weight_kg: float = 15.0
@export var strength_bonus_kg: float = 0.0

var stance: MovementModel.Stance = MovementModel.Stance.STAND
var tier: WeightModel.Tier = WeightModel.Tier.LIGHT
var is_sprinting: bool = false
var legs: StaminaPool
var arms: StaminaPool

var _weights: WeightModel
var _move: MovementModel
var _capsule: CapsuleShape3D
var _gravity: float = 9.8

@onready var _collision: CollisionShape3D = $Collision
@onready var _head: Node3D = $Head


func _ready() -> void:
	if config == null:
		config = PlayerConfig.new()
	_weights = WeightModel.new(config)
	_move = MovementModel.new(config, _weights)
	legs = StaminaPool.new(config.legs_max, config.resume_threshold, config.recovery_delay_s)
	arms = StaminaPool.new(config.arms_max, config.resume_threshold, config.recovery_delay_s)
	_gravity = ProjectSettings.get_setting("physics/3d/default_gravity", 9.8) as float
	_capsule = CapsuleShape3D.new()
	_capsule.radius = config.capsule_radius
	_collision.shape = _capsule
	_apply_stance_shape()
	_head.position.y = _move.eye_height(stance)
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		var mm: InputEventMouseMotion = event as InputEventMouseMotion
		var turn: float = config.mouse_sensitivity * _weights.turn_mult(tier)
		if is_sprinting:
			turn *= config.sprint_turn_mult
		rotate_y(-mm.relative.x * turn)
		var limit: float = deg_to_rad(config.pitch_limit_deg)
		_head.rotation.x = clampf(_head.rotation.x - mm.relative.y * config.mouse_sensitivity, -limit, limit)
	elif event is InputEventMouseButton and Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	elif event is InputEventKey and (event as InputEventKey).physical_keycode == KEY_ESCAPE:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE


func _physics_process(delta: float) -> void:
	tier = _weights.tier_for(carried_weight_kg, strength_bonus_kg)
	_handle_stance_input()

	var input: Vector2 = Input.get_vector("move_left", "move_right", "move_forward", "move_back")
	var wish: Vector3 = global_basis * Vector3(input.x, 0.0, input.y)
	var wants_sprint: bool = Input.is_action_pressed("sprint") and input.y < -0.1 \
			and stance != MovementModel.Stance.PRONE
	is_sprinting = wants_sprint and _weights.can_sprint(tier) and not legs.is_exhausted

	var speed: float = _move.target_speed(stance, is_sprinting, tier)
	var horizontal: Vector2 = _move.step_horizontal(Vector2(velocity.x, velocity.z),
			Vector2(wish.x, wish.z), speed, tier, is_on_floor(), delta)
	velocity.x = horizontal.x
	velocity.z = horizontal.y

	if is_on_floor():
		if Input.is_action_just_pressed("jump") and stance == MovementModel.Stance.STAND \
				and not legs.is_exhausted:
			velocity.y = config.jump_velocity
			legs.spend(config.jump_stamina_cost * _weights.stamina_drain_mult(tier))
	else:
		velocity.y -= _gravity * delta

	if is_sprinting and input != Vector2.ZERO:
		legs.spend(config.sprint_drain_per_s * _weights.stamina_drain_mult(tier) * delta)
	var recovery: float = config.recovery_per_s * _weights.stamina_recovery_mult(tier)
	legs.tick(delta, recovery)
	arms.tick(delta, recovery)

	move_and_slide()
	_head.position.y = move_toward(_head.position.y, _move.eye_height(stance),
			config.eye_height_speed * delta)


func _handle_stance_input() -> void:
	if Input.is_action_just_pressed("crouch"):
		_toggle_stance(MovementModel.Stance.CROUCH)
	elif Input.is_action_just_pressed("prone"):
		_toggle_stance(MovementModel.Stance.PRONE)
	elif Input.is_action_just_pressed("jump") and stance != MovementModel.Stance.STAND:
		_set_stance(MovementModel.Stance.STAND)


func _toggle_stance(target: MovementModel.Stance) -> void:
	_set_stance(MovementModel.Stance.STAND if stance == target else target)


func _set_stance(new_stance: MovementModel.Stance) -> void:
	if new_stance == stance:
		return
	if _move.capsule_height(new_stance) > _move.capsule_height(stance) and not _can_fit(new_stance):
		return
	stance = new_stance
	_apply_stance_shape()
	stance_changed.emit(stance)


func _apply_stance_shape() -> void:
	var h: float = _move.capsule_height(stance)
	_capsule.height = h
	_collision.position.y = h * 0.5


func _can_fit(target: MovementModel.Stance) -> bool:
	var h: float = _move.capsule_height(target)
	var probe: CapsuleShape3D = CapsuleShape3D.new()
	probe.radius = config.capsule_radius
	probe.height = h - 0.04
	var q: PhysicsShapeQueryParameters3D = PhysicsShapeQueryParameters3D.new()
	q.shape = probe
	q.transform = Transform3D(Basis.IDENTITY, global_position + Vector3.UP * (h * 0.5 + 0.02))
	q.collision_mask = collision_mask
	q.exclude = [get_rid()]
	return get_world_3d().direct_space_state.intersect_shape(q, 1).is_empty()
