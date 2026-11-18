"""Exports the overlapping-boxes character for the game (pipeline spec, characters).

Run once per side and kind with Blender in the background:

    blender -b assets/source/characters/outfit_allied_solidify.blend --python tools/export_characters.py -- \
        assets/models/characters allied body
    blender -b assets/source/characters/outfit_allied_solidify.blend --python tools/export_characters.py -- \
        assets/models/characters allied outfit

(`soviet` the same with outfit_soviet_solidify.blend.) Writes boxes_<kind>_<side>.glb.

What it does, in a throwaway scene state (the .blend is never saved):
- rest pose, Rig unhidden;
- every mesh parented to a Rig bone becomes a mesh with Solidify/Mirror applied, one vertex group named after its
  bone at weight 1.0 and an Armature modifier, so the glTF is a skinned mesh that is 100% one bone per part;
- body kind: the parts without the outfit prefix (the head box is renamed HeadMesh, which the game hides from the
  first-person camera) plus the Palm_L / Palm_R markers on the hand bones; outfit kind: the Allied_ / Soviet_ parts;
- no animations (the game animates the skeleton itself).

Palm marker convention (pipeline spec, grip convention): in Godot space local +Y points out of the back of the hand,
local -Z runs from pinky to index (forward, the thumb side), X = Y x Z. With the arms hanging, the back of the hand
faces outward, so +Y is outward (away from the body) and +Z is backward. The point sits PALM_ALONG metres down the
fingers and PALM_DEPTH metres in from the back-of-hand axis, toward the palm (values taken from the old body).
"""
import sys

import bpy
import mathutils

PALM_ALONG = 0.064
PALM_DEPTH = 0.045
RENAME = {"Head": "HeadMesh"}

out_dir, side, kind = sys.argv[sys.argv.index("--") + 1:][:3]
prefix = {"allied": "Allied_", "soviet": "Soviet_"}[side]
rig = bpy.data.objects["Rig"]

if rig.animation_data:
    rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.location = (0, 0, 0)
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.rotation_euler = (0, 0, 0)
    pb.scale = (1, 1, 1)
rig.hide_viewport = False
rig.hide_set(False)
bpy.context.view_layer.update()

parts = []
for o in bpy.data.objects:
    if o.type != "MESH" or o.parent != rig or o.parent_type != "BONE":
        continue
    if o.hide_get() or o.hide_viewport:
        continue
    if (kind == "outfit") == o.name.startswith(prefix):
        parts.append(o)

coll = bpy.data.collections.new("EXPORT")
bpy.context.scene.collection.children.link(coll)
dg = bpy.context.evaluated_depsgraph_get()
made = []
for o in parts:
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, depsgraph=dg)
    # Body parts share names with bones (Chest, Hand_L, ...); Godot would rename the bones to Chest_2, so prefix them.
    final = RENAME.get(o.name, o.name if kind == "outfit" else "Body_" + o.name)
    world = o.matrix_world.copy()
    bone = o.parent_bone
    o.name = o.name + "__src"
    n = bpy.data.objects.new(final, me)
    coll.objects.link(n)
    n.parent = rig
    n.parent_type = "OBJECT"
    n.matrix_parent_inverse = rig.matrix_world.inverted()
    n.matrix_world = world
    vg = n.vertex_groups.new(name=bone)
    vg.add(list(range(len(me.vertices))), 1.0, "REPLACE")
    mod = n.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    made.append(n)
    o.hide_set(True)

markers = []
if kind == "body":
    for s, sign in (("L", -1.0), ("R", 1.0)):
        bone = rig.data.bones["Hand_" + s]
        rest = rig.matrix_world @ bone.matrix_local
        head = rest.translation
        fingers = (rest.to_3x3() @ mathutils.Vector((0, 1, 0))).normalized()
        out_dir_v = mathutils.Vector((sign, 0, 0))
        outward = (out_dir_v - fingers * fingers.dot(out_dir_v)).normalized()
        back = mathutils.Vector((0, -1, 0))  # Blender -Y is Godot +Z
        x = outward.cross(back)
        basis = mathutils.Matrix((x, outward, back)).transposed()
        if "Palm_" + s in bpy.data.objects:  # a leftover marker would force the suffix .001 on the new one
            bpy.data.objects["Palm_" + s].name = "Palm_" + s + "__src"
        e = bpy.data.objects.new("Palm_" + s, None)
        coll.objects.link(e)
        e.empty_display_type = "ARROWS"
        e.empty_display_size = 0.03
        e.parent = rig
        e.parent_type = "BONE"
        e.parent_bone = "Hand_" + s
        pos = head + fingers * PALM_ALONG - outward * PALM_DEPTH
        e.matrix_world = mathutils.Matrix.Translation(pos) @ basis.to_4x4()
        markers.append(e)
        bpy.context.view_layer.update()

bpy.ops.object.select_all(action="DESELECT")
for n in made + markers:
    n.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig

path = f"{out_dir}/boxes_{kind}_{side}.glb"
bpy.ops.export_scene.gltf(
    filepath=path, export_format="GLB", use_selection=True, export_apply=False, export_skins=True,
    export_extras=True, export_yup=True, export_cameras=False, export_lights=False, export_animations=False,
)
print("EXPORTED", path, [n.name for n in made + markers], "tris", sum(len(n.data.polygons) for n in made))
