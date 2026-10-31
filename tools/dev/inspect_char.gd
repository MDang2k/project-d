extends SceneTree
# Dev: prints the structure of the character models (python/godot tooling, not game code).
func _init() -> void:
	for id: String in ["player_body", "outfit_looter"]:
		var ps: PackedScene = load("res://assets/models/characters/%s.glb" % id)
		var n: Node = ps.instantiate()
		print("== ", id)
		_walk(n, 0)
		var skel: Skeleton3D = n.find_children("*", "Skeleton3D", true, false)[0]
		print("bones ", skel.get_bone_count())
		for b in skel.get_bone_count():
			print("  ", b, " ", skel.get_bone_name(b), " parent ", skel.get_bone_parent(b), " rest ", skel.get_bone_rest(b).origin, " globalrest ", skel.get_bone_global_rest(b).origin)
		n.free()
	quit()
func _walk(n: Node, d: int) -> void:
	var extra := ""
	if n is MeshInstance3D:
		var mi := n as MeshInstance3D
		extra = " mesh surfaces=%d aabb=%s" % [mi.mesh.get_surface_count(), mi.get_aabb()]
		for i in mi.mesh.get_surface_count():
			var m: Material = mi.mesh.surface_get_material(i)
			extra += " mat%d=%s tex=%s" % [i, m, ((m as BaseMaterial3D).albedo_texture.resource_path if m is BaseMaterial3D and (m as BaseMaterial3D).albedo_texture else "none")]
	print("  ".repeat(d), n.name, " (", n.get_class(), ")", extra)
	for c in n.get_children():
		if d < 5: _walk(c, d + 1)
