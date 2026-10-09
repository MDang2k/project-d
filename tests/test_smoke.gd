extends GutTest


func test_smoke() -> void:
	assert_eq(1 + 1, 2)


func test_jolt_selected() -> void:
	var engine: String = ProjectSettings.get_setting("physics/3d/physics_engine")
	assert_eq(engine, "Jolt Physics")
