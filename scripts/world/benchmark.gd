class_name Benchmark
extends Node3D
## M0 renderer benchmark. Builds a compound-like test scene in code, flies a fixed
## camera path and reports fps. Keys: N night/day, L extra lights, S shadows,
## Tab free-cam (WASD + mouse), Space restart run, Esc quit.

const SIZE: float = 120.0
const BUILDING_COUNT: int = 90
const LIGHT_COUNT: int = 12
const RUN_SECONDS: float = 30.0
const PATH_RADIUS: float = 40.0
const FREE_SPEED: float = 12.0
const MOUSE_SENS: float = 0.002

var _cam: Camera3D
var _sun: DirectionalLight3D
var _env: Environment
var _lights: Array[Light3D] = []
var _label: Label
var _night: bool = false
var _free: bool = false
var _t: float = 0.0
var _frame_ms: PackedFloat32Array = PackedFloat32Array()
var _result: String = ""


func _ready() -> void:
	_apply_lowest_settings()
	_build_world()
	_build_ui()
	_set_night(false)


func _apply_lowest_settings() -> void:
	var vp: Viewport = get_viewport()
	vp.msaa_3d = Viewport.MSAA_DISABLED
	vp.screen_space_aa = Viewport.SCREEN_SPACE_AA_DISABLED
	vp.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR
	vp.scaling_3d_scale = 1.0
	RenderingServer.directional_shadow_atlas_set_size(2048, false)
	RenderingServer.directional_soft_shadow_filter_set_quality(RenderingServer.SHADOW_QUALITY_HARD)
	RenderingServer.positional_soft_shadow_filter_set_quality(RenderingServer.SHADOW_QUALITY_HARD)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)


func _build_world() -> void:
	var rng: RandomNumberGenerator = RandomNumberGenerator.new()
	rng.seed = 1970
	var ground: MeshInstance3D = MeshInstance3D.new()
	var plane: PlaneMesh = PlaneMesh.new()
	plane.size = Vector2(SIZE * 2.0, SIZE * 2.0)
	ground.mesh = plane
	ground.material_override = _mat(Color(0.25, 0.3, 0.2))
	add_child(ground)

	var box_mat: StandardMaterial3D = _mat(Color(0.55, 0.55, 0.5))
	for i: int in BUILDING_COUNT:
		var m: MeshInstance3D = MeshInstance3D.new()
		var b: BoxMesh = BoxMesh.new()
		b.size = Vector3(rng.randf_range(4.0, 14.0), rng.randf_range(3.0, 12.0), rng.randf_range(4.0, 14.0))
		m.mesh = b
		m.material_override = box_mat
		m.position = Vector3(rng.randf_range(-SIZE, SIZE), b.size.y * 0.5, rng.randf_range(-SIZE, SIZE))
		add_child(m)

	_sun = DirectionalLight3D.new()
	_sun.shadow_enabled = true
	_sun.directional_shadow_max_distance = 80.0
	_sun.rotation_degrees = Vector3(-50.0, 30.0, 0.0)
	add_child(_sun)

	for i: int in LIGHT_COUNT:
		var l: OmniLight3D = OmniLight3D.new()
		l.omni_range = 18.0
		l.light_energy = 3.0
		l.light_color = Color(1.0, 0.8, 0.5)
		l.shadow_enabled = true
		l.position = Vector3(rng.randf_range(-PATH_RADIUS, PATH_RADIUS), 4.0, rng.randf_range(-PATH_RADIUS, PATH_RADIUS))
		add_child(l)
		_lights.append(l)

	_env = Environment.new()
	_env.background_mode = Environment.BG_COLOR
	_env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	var we: WorldEnvironment = WorldEnvironment.new()
	we.environment = _env
	add_child(we)

	_cam = Camera3D.new()
	_cam.current = true
	_cam.far = 300.0
	add_child(_cam)


func _build_ui() -> void:
	var layer: CanvasLayer = CanvasLayer.new()
	_label = Label.new()
	_label.position = Vector2(12.0, 12.0)
	layer.add_child(_label)
	add_child(layer)


func _mat(c: Color) -> StandardMaterial3D:
	var m: StandardMaterial3D = StandardMaterial3D.new()
	m.albedo_color = c
	return m


func _set_night(on: bool) -> void:
	_night = on
	_sun.light_energy = 0.05 if on else 1.0
	_sun.light_color = Color(0.5, 0.6, 1.0) if on else Color(1.0, 0.95, 0.85)
	_env.background_color = Color(0.01, 0.015, 0.03) if on else Color(0.55, 0.7, 0.9)
	_env.ambient_light_color = Color(0.03, 0.04, 0.08) if on else Color(0.6, 0.65, 0.7)
	_env.ambient_light_energy = 0.3 if on else 0.8
	for l: Light3D in _lights:
		l.visible = on


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey:
		var k: InputEventKey = event as InputEventKey
		if not k.pressed or k.echo:
			return
		match k.physical_keycode:
			KEY_N:
				_set_night(not _night)
			KEY_L:
				for l: Light3D in _lights:
					l.visible = not l.visible
			KEY_S:
				_sun.shadow_enabled = not _sun.shadow_enabled
			KEY_TAB:
				_free = not _free
				Input.mouse_mode = Input.MOUSE_MODE_CAPTURED if _free else Input.MOUSE_MODE_VISIBLE
			KEY_SPACE:
				_restart()
			KEY_ESCAPE:
				get_tree().quit()
	elif event is InputEventMouseMotion and _free:
		var mm: InputEventMouseMotion = event as InputEventMouseMotion
		_cam.rotation.y -= mm.relative.x * MOUSE_SENS
		_cam.rotation.x = clampf(_cam.rotation.x - mm.relative.y * MOUSE_SENS, -1.5, 1.5)


func _restart() -> void:
	_t = 0.0
	_frame_ms.clear()
	_result = ""


func _process(delta: float) -> void:
	if _free:
		_move_free(delta)
	else:
		_fly_path(delta)
		if _result == "":
			_frame_ms.append(delta * 1000.0)
			if _t >= RUN_SECONDS:
				_finish()
	_update_label()


func _fly_path(delta: float) -> void:
	_t += delta
	var a: float = _t * 0.15
	_cam.position = Vector3(cos(a) * PATH_RADIUS, 6.0, sin(a) * PATH_RADIUS)
	_cam.look_at(Vector3(cos(a + 0.6) * PATH_RADIUS * 0.5, 2.0, sin(a + 0.6) * PATH_RADIUS * 0.5))


func _move_free(delta: float) -> void:
	var dir: Vector3 = Vector3.ZERO
	if Input.is_physical_key_pressed(KEY_W): dir -= _cam.global_basis.z
	if Input.is_physical_key_pressed(KEY_S): dir += _cam.global_basis.z
	if Input.is_physical_key_pressed(KEY_A): dir -= _cam.global_basis.x
	if Input.is_physical_key_pressed(KEY_D): dir += _cam.global_basis.x
	_cam.position += dir.normalized() * FREE_SPEED * delta


func _finish() -> void:
	var sorted: PackedFloat32Array = _frame_ms.duplicate()
	sorted.sort()
	var sum: float = 0.0
	for v: float in sorted:
		sum += v
	var avg_ms: float = sum / sorted.size()
	var worst_1pct: float = 0.0
	var n: int = maxi(1, int(sorted.size() * 0.01))
	for i: int in n:
		worst_1pct += sorted[sorted.size() - 1 - i]
	worst_1pct /= n
	_result = "RESULT %s | %s | night=%s | avg %.1f fps | 1%% low %.1f fps | max %.1f ms" % [
		RenderingServer.get_video_adapter_name(), get_window().size, _night,
		1000.0 / avg_ms, 1000.0 / worst_1pct, sorted[sorted.size() - 1]]
	print(_result)
	var f: FileAccess = FileAccess.open("user://benchmark_results.txt", FileAccess.READ_WRITE)
	if f == null:
		f = FileAccess.open("user://benchmark_results.txt", FileAccess.WRITE)
	f.seek_end()
	f.store_line(_result)


func _update_label() -> void:
	var calls: int = int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TOTAL_DRAW_CALLS_IN_FRAME))
	var tris: int = int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TOTAL_PRIMITIVES_IN_FRAME))
	_label.text = "%d fps  %.1f ms  calls %d  tris %d\n%s  %s\nnight=%s shadows=%s %s\n[N]ight [L]ights [S]hadows [Tab]free-cam [Space]restart\n%s" % [
		Engine.get_frames_per_second(), 1000.0 / maxf(1.0, Engine.get_frames_per_second()), calls, tris,
		RenderingServer.get_video_adapter_name(), get_window().size, _night, _sun.shadow_enabled,
		"" if _free else "run %.0f/%.0fs" % [minf(_t, RUN_SECONDS), RUN_SECONDS], _result]
