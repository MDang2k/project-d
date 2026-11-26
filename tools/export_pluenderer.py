"""Exports the Pluenderer and Bandenfuehrer outfit pieces, one .glb per slot option.

    blender -b assets/source/characters/outfits_pluenderer.blend --python tools/export_pluenderer.py

Writes assets/models/characters/outfit_<faction>_<slot>_<option>.glb and copies the shared atlas to
assets/textures/characters/outfit_pluenderer.png. The .blend is never saved.

Each piece becomes one skinned mesh (one draw call) on the box body's skeleton: parts hung on a bone get that bone
at weight 1.0, the coat skirt keeps its own hips/thigh weights. The .glb carries no image (the pieces share the one
atlas, which the game assigns to the material "pluenderer_detail") and no animations.
"""
import os
import shutil

import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "assets", "models", "characters")

# piece collection -> exported name
FILES = {
    "jacket_work": "pluenderer_jacket_work",
    "jacket_cord": "pluenderer_jacket_cord",
    "jacket_parka": "pluenderer_jacket_parka",
    "trousers_grey": "pluenderer_trousers_grey",
    "trousers_cord": "pluenderer_trousers_cord",
    "trousers_jeans": "pluenderer_trousers_jeans",
    "boots_brown": "pluenderer_boots_brown",
    "boots_rubber": "pluenderer_boots_rubber",
    "gloves_wool": "pluenderer_hands_wool",
    "head_flatcap": "pluenderer_head_flatcap",
    "head_watchcap": "pluenderer_head_watchcap",
    "face_scarf": "pluenderer_face_scarf",
    "face_gasmask": "pluenderer_face_gasmask",
    "sack": "pluenderer_marker_sack",
    "bf_coat_closed": "bandenfuehrer_coat_closed",
    "bf_coat_open": "bandenfuehrer_coat_open",
    "bf_trousers": "bandenfuehrer_trousers_brown",
    "bf_boots": "bandenfuehrer_boots_black",
    "bf_gloves": "bandenfuehrer_hands_leather",
    "bf_head_flatcap": "bandenfuehrer_head_flatcap",
    "bf_glasses": "bandenfuehrer_face_glasses",
    "bf_neckerchief": "bandenfuehrer_marker_neckerchief",
}

rig = bpy.data.objects["Rig"]
if rig.animation_data:
    rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.location = (0, 0, 0)
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.rotation_euler = (0, 0, 0)


def show_all(layer):
    layer.exclude = False
    layer.hide_viewport = False
    for child in layer.children:
        show_all(child)


show_all(bpy.context.view_layer.layer_collection)
rig.hide_viewport = False
rig.hide_set(False)
for obj in bpy.data.collections["Lineup"].objects:
    obj.hide_set(True)
bpy.context.view_layer.update()

found = {c.name[len("Piece_"):] for c in bpy.data.collections if c.name.startswith("Piece_")}
assert found == set(FILES), f"pieces without a file name, or the other way round: {found ^ set(FILES)}"

out = bpy.data.collections.new("EXPORT")
bpy.context.scene.collection.children.link(out)
for piece, name in FILES.items():
    dg = bpy.context.evaluated_depsgraph_get()
    made = []
    for o in bpy.data.collections["Piece_" + piece].objects:
        mesh = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        n = bpy.data.objects.new(o.name + "__x", mesh)
        out.objects.link(n)
        n.matrix_world = o.matrix_world.copy()
        if o.parent_type == "BONE":
            n.vertex_groups.new(name=o.parent_bone).add(list(range(len(mesh.vertices))), 1.0, "REPLACE")
        else:  # the coat skirt: weights painted by the build script
            for group in o.vertex_groups:
                n.vertex_groups.new(name=group.name)
        made.append(n)

    bpy.ops.object.select_all(action="DESELECT")
    for n in made:
        n.select_set(True)
    bpy.context.view_layer.objects.active = made[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = name
    joined.data.name = name
    loose = [v.index for v in joined.data.vertices if abs(sum(g.weight for g in v.groups) - 1.0) > 1e-4]
    assert not loose, f"{piece}: {len(loose)} vertices without a single full weight"

    joined.parent = rig
    joined.matrix_parent_inverse = rig.matrix_world.inverted()
    joined.modifiers.new("Armature", "ARMATURE").object = rig
    rig.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=os.path.join(MODELS, f"outfit_{name}.glb"), export_format="GLB", use_selection=True,
        export_apply=False, export_skins=True, export_extras=True, export_yup=True, export_cameras=False,
        export_lights=False, export_animations=False, export_image_format="NONE",
    )
    joined.data.calc_loop_triangles()
    print("EXPORTED", name, "tris", len(joined.data.loop_triangles))
    bpy.data.objects.remove(joined)  # frees the name and keeps the next export clean

shutil.copyfile(os.path.join(ROOT, "assets", "source", "characters", "outfits_pluenderer_tex.png"),
                os.path.join(ROOT, "assets", "textures", "characters", "outfit_pluenderer.png"))
