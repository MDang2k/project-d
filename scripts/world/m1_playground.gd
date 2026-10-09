class_name M1Playground
extends Node3D
## M1 movement test yard, built in code. Debug keys: [ / ] carried weight -/+ 5 kg,
## Backspace respawn. Overlay shows speed, stance, weight tier and stamina.

const PLAYER_SCENE: PackedScene = preload("res://scenes/player/player.tscn")
const WEIGHT_STEP_KG: float = 5.0

var _player: Player
var _label: Label


func _ready() -> void:
	_build_world()
	_player = PLAYER_SCENE.instantiate() as Player
	_player.position = Vector3(0.0, 0.1, 0.0)
	add_child(_player)
	var layer: CanvasLayer = CanvasLayer.new()
	_label = Label.new()
	_label.position = Vector2(12.0, 12.0)
	layer.add_child(_label)
	add_child(layer)


func _build_world() -> void:
	_box(Vector3(0.0, -0.5, 0.0), Vector3(120.0, 1.0, 120.0), Color(0.3, 0.35, 0.25))
	# Low walls and ledges (mantle targets in step 2).
	_box(Vector3(-6.0, 0.25, -8.0), Vector3(4.0, 0.5, 0.5), Color(0.6, 0.55, 0.5))
	_box(Vector3(-6.0, 0.5, -11.0), Vector3(4.0, 1.0, 0.5), Color(0.6, 0.55, 0.5))
	# Crouch tunnel: 1.4 m clearance (standing 1.8 m does not fit, crouching 1.2 m does).
	_arch(Vector3(0.0, 0.0, -10.0), 1.4)
	# Prone tunnel: 0.8 m clearance (only prone 0.6 m fits).
	_arch(Vector3(6.0, 0.0, -10.0), 0.8)
	# Ramp.
	var ramp: StaticBody3D = _box(Vector3(12.0, 0.9, -8.0), Vector3(4.0, 0.3, 10.0), Color(0.5, 0.5, 0.55))
	ramp.rotation_degrees = Vector3(-15.0, 0.0, 0.0)
	# Run lane marker every 10 m.
	for i: int in 6:
		_box(Vector3(-2.0, 0.05, -10.0 * float(i + 1) - 20.0), Vector3(0.3, 0.1, 0.3), Color(0.9, 0.8, 0.2))
	var sun: DirectionalLight3D = DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50.0, 30.0, 0.0)
	sun.shadow_enabled = true
	add_child(sun)
	var env: Environment = Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.55, 0.7, 0.9)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.6, 0.65, 0.7)
	var we: WorldEnvironment = WorldEnvironment.new()
	we.environment = env
	add_child(we)


## Two pillars plus a lintel whose underside sits at `clearance` metres.
func _arch(base: Vector3, clearance: float) -> void:
	var c: Color = Color(0.5, 0.45, 0.4)
	_box(base + Vector3(-2.0, clearance * 0.5, 0.0), Vector3(0.6, clearance, 3.0), c)
	_box(base + Vector3(2.0, clearance * 0.5, 0.0), Vector3(0.6, clearance, 3.0), c)
	_box(base + Vector3(0.0, clearance + 0.25, 0.0), Vector3(4.6, 0.5, 3.0), c)


func _box(pos: Vector3, size: Vector3, color: Color) -> StaticBody3D:
	var body: StaticBody3D = StaticBody3D.new()
	body.position = pos
	var shape: CollisionShape3D = CollisionShape3D.new()
	var bs: BoxShape3D = BoxShape3D.new()
	bs.size = size
	shape.shape = bs
	body.add_child(shape)
	var mesh: MeshInstance3D = MeshInstance3D.new()
	var bm: BoxMesh = BoxMesh.new()
	bm.size = size
	var mat: StandardMaterial3D = StandardMaterial3D.new()
	mat.albedo_color = color
	bm.material = mat
	mesh.mesh = bm
	body.add_child(mesh)
	add_child(body)
	return body


func _unhandled_input(event: InputEvent) -> void:
	if not (event is InputEventKey):
		return
	var k: InputEventKey = event as InputEventKey
	if not k.pressed or k.echo:
		return
	match k.physical_keycode:
		KEY_BRACKETLEFT:
			_player.carried_weight_kg = maxf(0.0, _player.carried_weight_kg - WEIGHT_STEP_KG)
		KEY_BRACKETRIGHT:
			_player.carried_weight_kg += WEIGHT_STEP_KG
		KEY_BACKSPACE:
			_player.position = Vector3(0.0, 0.1, 0.0)
			_player.velocity = Vector3.ZERO


func _process(_delta: float) -> void:
	var h: float = Vector2(_player.velocity.x, _player.velocity.z).length()
	_label.text = "speed %.2f m/s  stance %s%s\nweight %.0f kg  tier %s\nlegs %.0f%%%s  arms %.0f%%\n[ ] weight  Backspace respawn" % [
		h, MovementModel.Stance.keys()[_player.stance], "  SPRINT" if _player.is_sprinting else "",
		_player.carried_weight_kg, WeightModel.Tier.keys()[_player.tier],
		_player.legs.fraction() * 100.0, "  EXHAUSTED" if _player.legs.is_exhausted else "",
		_player.arms.fraction() * 100.0]
