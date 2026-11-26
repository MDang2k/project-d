"""Exports the AI outfit pieces, one .glb per slot option, from whichever outfit .blend is open.

    blender -b assets/source/characters/<file>.blend --python tools/export_outfits.py

for outfits_pluenderer, outfits_pmc_allied, outfits_pmc_soviet, outfits_holdout (the new pieces) and outfit_allied_solidify,
outfit_soviet_solidify (the player outfits, split into the slots the PMC bots share). Writes
assets/models/characters/outfit_<name>.glb and copies that file's texture to assets/textures/characters/. The .blend
is never saved.

Each piece becomes one skinned mesh (one draw call) on the box body's skeleton: parts hung on a bone get that bone
at weight 1.0, the leather coat's skirt keeps its own hips/thigh weights. The .glb carries no image (pieces share a
texture, which the game assigns by material name) and no animations.
"""
import os
import shutil

import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "assets", "models", "characters")

# new pieces: collection "Piece_<key>" -> exported name
PIECES = {
    "outfits_pluenderer": {
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
    },
    "outfits_pmc_allied": {
        "jacket_erdl": "allied_jacket_erdl",
        "jacket_dpm": "allied_jacket_dpm",
        "jacket_og107": "allied_jacket_og107",
        "head_boonie": "allied_head_boonie",
        "head_beret": "allied_head_beret",
    },
    "outfits_pmc_soviet": {
        "jacket_klmk": "soviet_jacket_klmk",
        "jacket_strichtarn": "soviet_jacket_strichtarn",
        "jacket_telnyashka": "soviet_jacket_telnyashka",
        "head_beret": "soviet_head_beret",
    },
}
PIECES["outfits_holdout"] = {key: "holdout_" + key for key in (
    "jacket_og107", "jacket_torn", "jacket_poncho", "jacket_sweater", "trousers_og107", "trousers_grey",
    "boots_black", "hands_cutoff", "head_helmet", "head_hair", "face_beard", "bag_gasmask")}
# player outfits: exported name -> the prefixes of its part names
SPLITS = {
    "outfit_allied_solidify": {
        "allied_jacket_m65": ("Allied_Jacket", "Allied_Collar", "Allied_Sleeve"),
        "allied_trousers_og107": ("Allied_Trouser",),
        "allied_boots_black": ("Allied_Boot",),
        "allied_hands_olive": ("Allied_Glove",),
    },
    "outfit_soviet_solidify": {
        "soviet_jacket_afghanka": ("Soviet_Jacket", "Soviet_Collar", "Soviet_Sleeve"),
        "soviet_trousers_olive": ("Soviet_Trouser",),
        "soviet_boots_brown": ("Soviet_Boot",),
    },
}
# the texture each source file's pieces share: source image -> name in assets/textures/characters
TEXTURES = {
    "outfits_holdout": ("outfits_holdout_tex.png", "outfit_holdout.png"),
    "outfits_pluenderer": ("outfits_pluenderer_tex.png", "outfit_pluenderer.png"),
    "outfits_pmc_allied": ("outfits_pmc_allied_tex.png", "outfit_allied_pmc.png"),
    "outfits_pmc_soviet": ("outfits_pmc_soviet_tex.png", "outfit_soviet_pmc.png"),
    "outfit_allied_solidify": ("outfit_allied_solidify_tex.png", "outfit_allied_detail.png"),
    "outfit_soviet_solidify": ("outfit_soviet_solidify_tex.png", "outfit_soviet_detail.png"),
}

source = os.path.splitext(os.path.basename(bpy.data.filepath))[0]
assert source in TEXTURES, f"not an outfit source file: {source}"

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

if source in PIECES:
    names = PIECES[source]
    found = {c.name[len("Piece_"):] for c in bpy.data.collections
             if c.name.startswith("Piece_") and not c.name.startswith("Piece_shared_")}
    assert found == set(names), f"pieces without a file name, or the other way round: {found ^ set(names)}"
    parts = {name: list(bpy.data.collections["Piece_" + piece].objects) for piece, name in names.items()}
    for obj in bpy.data.collections["Lineup"].objects:
        obj.hide_set(True)
else:
    parts = {name: [o for o in bpy.data.objects if o.type == "MESH" and o.parent == rig and o.name.startswith(prefixes)]
             for name, prefixes in SPLITS[source].items()}

rig.hide_viewport = False
rig.hide_set(False)
for objects in parts.values():
    for o in objects:  # a hidden object is not evaluated, so its modifiers would be lost
        o.hide_viewport = False
        o.hide_set(False)
bpy.context.view_layer.update()

out = bpy.data.collections.new("EXPORT")
bpy.context.scene.collection.children.link(out)
for name, objects in parts.items():
    assert objects, f"{name}: no parts"
    dg = bpy.context.evaluated_depsgraph_get()
    made = []
    for o in objects:
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
    assert not loose, f"{name}: {len(loose)} vertices without a single full weight"

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

image, target = TEXTURES[source]
shutil.copyfile(os.path.join(ROOT, "assets", "source", "characters", image),
                os.path.join(ROOT, "assets", "textures", "characters", target))
