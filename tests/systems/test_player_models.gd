extends GutTest
## Weight tiers, stamina pools and movement inertia (GDD s6).

var _config: PlayerConfig
var _weights: WeightModel
var _move: MovementModel


func before_each() -> void:
	_config = PlayerConfig.new()
	_weights = WeightModel.new(_config)
	_move = MovementModel.new(_config, _weights)


func test_tier_boundaries() -> void:
	assert_eq(_weights.tier_for(0.0), WeightModel.Tier.LIGHT)
	assert_eq(_weights.tier_for(19.9), WeightModel.Tier.LIGHT)
	assert_eq(_weights.tier_for(20.0), WeightModel.Tier.MODERATE)
	assert_eq(_weights.tier_for(35.0), WeightModel.Tier.HEAVY)
	assert_eq(_weights.tier_for(50.0), WeightModel.Tier.OVERLOADED)


func test_strength_bonus_raises_thresholds() -> void:
	assert_eq(_weights.tier_for(22.0, 5.0), WeightModel.Tier.LIGHT)
	assert_eq(_weights.tier_for(25.0, 5.0), WeightModel.Tier.MODERATE)


func test_sprint_blocked_from_heavy() -> void:
	assert_true(_weights.can_sprint(WeightModel.Tier.MODERATE))
	assert_false(_weights.can_sprint(WeightModel.Tier.HEAVY))


func test_stamina_depletes_and_locks() -> void:
	var pool: StaminaPool = StaminaPool.new(100.0, 20.0, 1.0)
	pool.spend(150.0)
	assert_eq(pool.value, 0.0)
	assert_true(pool.is_exhausted)


func test_stamina_recovery_waits_for_delay() -> void:
	var pool: StaminaPool = StaminaPool.new(100.0, 20.0, 1.0)
	pool.spend(50.0)
	pool.tick(0.5, 10.0)
	assert_eq(pool.value, 50.0)
	pool.tick(0.5, 10.0)
	pool.tick(1.0, 10.0)
	assert_eq(pool.value, 60.0)


func test_stamina_resumes_at_threshold() -> void:
	var pool: StaminaPool = StaminaPool.new(100.0, 20.0, 0.0)
	pool.spend(100.0)
	pool.tick(1.0, 10.0)
	assert_true(pool.is_exhausted)
	pool.tick(1.0, 10.0)
	assert_false(pool.is_exhausted)


func test_stance_speed_order() -> void:
	var tier: WeightModel.Tier = WeightModel.Tier.LIGHT
	var stand: float = _move.target_speed(MovementModel.Stance.STAND, false, tier)
	var sprint: float = _move.target_speed(MovementModel.Stance.STAND, true, tier)
	var crouch: float = _move.target_speed(MovementModel.Stance.CROUCH, false, tier)
	var prone: float = _move.target_speed(MovementModel.Stance.PRONE, true, tier)
	assert_gt(sprint, stand)
	assert_gt(stand, crouch)
	assert_gt(crouch, prone)
	assert_eq(prone, _move.target_speed(MovementModel.Stance.PRONE, false, tier))


func test_overloaded_is_slower() -> void:
	var light: float = _move.target_speed(MovementModel.Stance.STAND, false, WeightModel.Tier.LIGHT)
	var over: float = _move.target_speed(MovementModel.Stance.STAND, false, WeightModel.Tier.OVERLOADED)
	assert_lt(over, light)


func test_acceleration_is_gradual() -> void:
	var v: Vector2 = _move.step_horizontal(Vector2.ZERO, Vector2.RIGHT, 5.0, WeightModel.Tier.LIGHT, true, 0.1)
	assert_almost_eq(v.x, _config.acceleration * 0.1, 0.001)
	assert_lt(v.x, 5.0)


func test_heavy_accelerates_slower() -> void:
	var light: Vector2 = _move.step_horizontal(Vector2.ZERO, Vector2.RIGHT, 5.0, WeightModel.Tier.LIGHT, true, 0.1)
	var heavy: Vector2 = _move.step_horizontal(Vector2.ZERO, Vector2.RIGHT, 5.0, WeightModel.Tier.HEAVY, true, 0.1)
	assert_lt(heavy.x, light.x)


func test_airborne_control_reduced() -> void:
	var ground: Vector2 = _move.step_horizontal(Vector2.ZERO, Vector2.RIGHT, 5.0, WeightModel.Tier.LIGHT, true, 0.1)
	var air: Vector2 = _move.step_horizontal(Vector2.ZERO, Vector2.RIGHT, 5.0, WeightModel.Tier.LIGHT, false, 0.1)
	assert_lt(air.x, ground.x)


func test_reaches_target_speed() -> void:
	var v: Vector2 = _move.step_horizontal(Vector2(4.9, 0.0), Vector2.RIGHT, 5.0, WeightModel.Tier.LIGHT, true, 1.0)
	assert_eq(v, Vector2(5.0, 0.0))
