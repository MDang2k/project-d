"""Shared pieces of the outfit build scripts (build_pluenderer.py, build_pmc.py): garment shells on the box body,
the texture atlas and its painting, the A-pose lineup and the preview renders. Import it from a script that Blender
runs with base_character_v2_boxes.blend open."""
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "source", "characters")
ATLAS = 512
DENSITY = 110  # texels per metre

rig = bpy.data.objects["Rig"]
if rig.animation_data:
    rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.location = (0, 0, 0)
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()

body = {o.name: o for o in bpy.data.objects if o.type == "MESH" and o.parent == rig}
pieces = {}  # name -> list of objects
meta = {}    # object name -> (piece, part key used to share atlas rects between left and right)


def collection(name):
    c = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(c)
    return c


def attach(piece, obj_name, mesh, bone, key):
    o = bpy.data.objects.new(obj_name, mesh)
    pieces.setdefault(piece, []).append(o)
    coll = bpy.data.collections.get("Piece_" + piece) or collection("Piece_" + piece)
    coll.objects.link(o)
    o.parent = rig
    o.parent_type = "BONE"
    o.parent_bone = bone
    bpy.context.view_layer.update()
    o.matrix_world = Matrix.Identity(4)  # vertices are in world (rest) space
    meta[obj_name] = (piece, key)
    return o


def solidify(o, thickness):
    m = o.modifiers.new("Solidify", "SOLIDIFY")
    m.thickness = thickness
    m.offset = 1.0
    m.use_even_offset = True


def shell(piece, part, thickness, zmin=None, zmax=None, grow=1.0, drop=None, rise=None, key=None):
    """A copy of a body box: optionally cut to zmin..zmax, widened by `grow`, its bottom pulled down to `drop`."""
    src = body[part]
    mesh = src.data.copy()
    mesh.transform(src.matrix_world)
    mirrored = any(m.type == "MIRROR" for m in src.modifiers)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    for z, normal in ((zmin, (0, 0, -1)), (zmax, (0, 0, 1))):
        if z is None:
            continue
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, z),
                               plane_no=normal, clear_outer=True)
    zs = [v.co.z for v in bm.verts]
    lo, hi = min(zs), max(zs)
    cx = 0.0 if mirrored else sum(v.co.x for v in bm.verts) / len(bm.verts)
    cy = (min(v.co.y for v in bm.verts) + max(v.co.y for v in bm.verts)) / 2
    for v in bm.verts:
        if grow != 1.0:
            v.co.x = cx + (v.co.x - cx) * grow
            v.co.y = cy + (v.co.y - cy) * grow
        if drop is not None and v.co.z < lo + 0.03:
            v.co.z = drop
        if rise is not None and v.co.z > hi - 0.03:
            v.co.z = rise
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.clear()
    name = f"{piece}__{part}" if key is None else f"{piece}__{key}_{part}"
    key = key or (part[:-2] if part.endswith(("_L", "_R")) else part)
    o = attach(piece, name, mesh, src.parent_bone, key)
    if mirrored:
        mm = o.modifiers.new("Mirror", "MIRROR")
        mm.use_clip = True
        mm.merge_threshold = 0.0005
    solidify(o, thickness)
    return o


def both(piece, part, thickness, **kw):
    for s in ("_L", "_R"):
        shell(piece, part + s, thickness, **kw)


def solid(piece, name, bone, center, size, rot=(0, 0, 0), taper=1.0, key=None):
    """A box; `taper` scales its lower end in x and y."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        k = taper if v.co.z < 0 else 1.0
        v.co = Vector((v.co.x * size[0] * k, v.co.y * size[1] * k, v.co.z * size[2]))
    return _place(piece, name, bone, bm, center, rot, key or name.rstrip("LR_"))


def tube(piece, name, bone, center, radius, depth, rot=(0, 0, 0), segments=8, key=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=radius, depth=depth)
    return _place(piece, name, bone, bm, center, rot, key or name.rstrip("LR_"))


def _place(piece, name, bone, bm, center, rot, key):
    mat = Matrix.Translation(center) @ (Matrix.Rotation(rot[2], 4, "Z") @ Matrix.Rotation(rot[1], 4, "Y")
                                        @ Matrix.Rotation(rot[0], 4, "X"))
    bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    return attach(piece, f"{piece}__{name}", mesh, bone, key)


def jacket(piece, t, hem, flare=1.05, collar_grow=1.35):
    shell(piece, "Chest", t)
    shell(piece, "Abdomen", t, drop=hem, grow=flare)
    both(piece, "Shoulder", t)
    both(piece, "Arm_Upper", t)
    both(piece, "Arm_Lower", t, zmin=0.865)
    shell(piece, "Neck", 0.006, zmin=1.425, zmax=1.475, grow=collar_grow)

def lathe(piece, name, bone, center, rings, rot=(0, 0, 0), flat=0.75, segments=8, key=None):
    """A soft round shape: `rings` are (height, radius) from the bottom; `flat` squashes it front to back."""
    bm = bmesh.new()
    loops = [[bm.verts.new((r * math.cos(a), r * math.sin(a) * flat, z))
              for a in (2 * math.pi * (i + 0.5) / segments for i in range(segments))] for z, r in rings]
    for low, high in zip(loops, loops[1:]):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((low[i], low[j], high[j], high[i]))
    bm.faces.new(list(reversed(loops[0])))
    bm.faces.new(loops[-1])
    return _place(piece, name, bone, bm, center, rot, key or name)


# ---------------------------------------------------------------- atlas layout

def view_of(n):
    ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
    if az > ax and az > ay:
        return "top"
    if ay >= ax:
        return "front" if n.y > 0 else "back"
    return "side"


SIGNED = set()  # pieces whose parts that cross the centre line map left to right instead of mirrored
FULL = set()  # (piece, part) made whole instead of mirrored; mark() still takes half coordinates and paints both sides


def across(piece, o):
    """True for a part of a SIGNED piece that reaches both sides of the centre line (a poncho, a beard)."""
    xs = [v.co.x for v in o.data.vertices]
    return piece in SIGNED and min(xs) < -0.01 and max(xs) > 0.01


def plane(view, co, signed=False):
    x = co.x if signed else abs(co.x)
    if view in ("front", "back"):
        return x, co.z
    if view == "side":
        return co.y, co.z
    return x, co.y


bounds = {}
rects = {}
pixels = np.zeros((ATLAS, ATLAS, 3), dtype=np.float32)  # row 0 is the bottom
rng = np.random.default_rng(7)


def layout():
    """Gives every (piece, part, view) its rectangle in the atlas. Call after the last part, before painting."""
    for piece, objs in pieces.items():
        for o in objs:
            _, key = meta[o.name]
            for p in o.data.polygons:
                view = view_of(p.normal)
                b = bounds.setdefault((piece, key, view), [9e9, 9e9, -9e9, -9e9])
                for vi in p.vertices:
                    u, v = plane(view, o.data.vertices[vi].co, across(piece, o))
                    b[0], b[1], b[2], b[3] = min(b[0], u), min(b[1], v), max(b[2], u), max(b[3], v)

    x = y = row = 0
    for k, b in sorted(bounds.items(), key=lambda kv: -(kv[1][3] - kv[1][1])):
        w = max(2, math.ceil((b[2] - b[0]) * DENSITY))
        h = max(2, math.ceil((b[3] - b[1]) * DENSITY))
        if x + w + 2 > ATLAS:
            x, y, row = 0, y + row, 0
        rects[k] = (x + 1, y + 1, w, h)
        x += w + 2
        row = max(row, h + 2)
    assert y + row <= ATLAS, f"atlas full: {y + row}"
    print("ATLAS rows used", y + row)


def rgb(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32) / 255


def paint(piece, base, noise=0.025, ribs=0.0, patch=0.0):
    for (p, key, view), (rx, ry, w, h) in rects.items():
        if p != piece:
            continue
        img = np.ones((h, w, 3), dtype=np.float32) * rgb(base)
        img += (rng.random((h, w, 1)) - 0.5) * 2 * noise
        if ribs:
            img[:, ::2] -= ribs
        if patch:
            for _ in range(max(1, w * h // 220)):
                px, py = rng.integers(0, w), rng.integers(0, h)
                img[py:py + 3, px:px + 4] += patch
        if view == "side":
            img *= 0.93
        if view == "top":
            img *= 1.07
        pixels[ry - 1:ry + h + 1, rx - 1:rx + w + 1] = np.pad(img, ((1, 1), (1, 1), (0, 0)), mode="edge")


def mark(piece, key, views, u0, v0, u1, v1, color):
    """A rectangle in 0..1 of the part's atlas rect: u from the centre line (or the part's left), v from the bottom."""
    for view in views.split():
        if (piece, key, view) not in rects:
            continue
        rx, ry, w, h = rects[(piece, key, view)]
        spans = [(u0, u1)]
        if (piece, key) in FULL and view != "side":
            spans = [(0.5 + u0 / 2, 0.5 + u1 / 2), (0.5 - u1 / 2, 0.5 - u0 / 2)]
        c, d = int(round(v0 * h)), int(round(v1 * h))
        for s0, s1 in spans:
            a, b = int(round(s0 * w)), int(round(s1 * w))
            pixels[ry + c:ry + max(d, c + 1), rx + a:rx + max(b, a + 1)] = rgb(color)


def pocket(piece, key, u0, v0, u1, v1, line, flap):
    mark(piece, key, "front", u0, v0, u1, v1, line)
    mark(piece, key, "front", u0 + 0.05, v0 + 0.05, u1 - 0.05, v1 - 0.12, flap)
    mark(piece, key, "front", u0, v1 - 0.09, u1, v1, line)


def cuffs(piece, color, v=0.13):
    mark(piece, "Arm_Lower", "front back side", 0, 0, 1, v, color)


def buttons(piece, color, keys=("Chest", "Abdomen"), u=0.07):
    for key in keys:
        for v in (0.2, 0.55, 0.88):
            mark(piece, key, "front", u, v - 0.04, u + 0.07, v + 0.02, color)


def camo(piece, layers):
    """Scatters blotches over every region of a piece: (colour, share of the area, width, height) in texels."""
    for (p, _, view), (rx, ry, w, h) in rects.items():
        if p != piece:
            continue
        region = pixels[ry - 1:ry + h + 1, rx - 1:rx + w + 1]
        shade = 0.93 if view == "side" else 1.07 if view == "top" else 1.0
        for color, share, bw, bh in layers:
            for _ in range(int(region.shape[0] * region.shape[1] * share / (bw * bh)) + 1):
                x = rng.integers(-bw // 2, region.shape[1])
                y = rng.integers(-bh // 2, region.shape[0])
                # two overlapping rectangles make a ragged blotch
                for dx, dy in ((0, 0), (bw // 2, max(1, bh // 2))):
                    region[max(y + dy, 0):max(y + dy + bh, 0), max(x + dx, 0):max(x + dx + bw, 0)] = rgb(color) * shade


def finish(prefix, material, lineup_sets, shared=None):
    """Saves the atlas, maps every part onto it, lines up `lineup_sets` (name -> pieces) in the A-pose, renders the
    previews as <prefix>_preview_*.png and saves <prefix>.blend. `shared` (name -> objects that keep their own
    texture) can be worn in the lineup too; a name ending in "hands" hides the body's own hands."""
    # spread each region's colours into the unused space, so smaller mipmaps never blend in a black background
    filled = np.zeros((ATLAS, ATLAS), dtype=bool)
    for rx, ry, w, h in rects.values():
        filled[ry - 1:ry + h + 1, rx - 1:rx + w + 1] = True
    while not filled.all():
        for axis, step in ((0, 1), (0, -1), (1, 1), (1, -1)):
            grow_into = ~filled & np.roll(filled, step, axis)
            pixels[grow_into] = np.roll(pixels, step, axis)[grow_into]
            filled |= grow_into

    img = bpy.data.images.new(f"{prefix}_tex", ATLAS, ATLAS, alpha=True)
    rgba = np.ones((ATLAS, ATLAS, 4), dtype=np.float32)
    rgba[..., :3] = np.clip(pixels, 0, 1)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(OUT, f"{prefix}_tex.png")
    img.file_format = "PNG"
    img.save()

    mat = bpy.data.materials.new(material)
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    mat.node_tree.links.new(tex.outputs["Color"], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    mat.node_tree.nodes.active = tex

    for piece, objs in pieces.items():
        for o in objs:
            _, key = meta[o.name]
            o.data.materials.clear()
            o.data.materials.append(mat)
            while o.data.uv_layers:  # one layer with one name everywhere, so joining parts on export keeps every UV
                o.data.uv_layers.remove(o.data.uv_layers[0])
            uv = o.data.uv_layers.new(name="UVMap")
            for p in o.data.polygons:
                view = view_of(p.normal)
                b = bounds[(piece, key, view)]
                rx, ry, w, h = rects[(piece, key, view)]
                for li in p.loop_indices:
                    u, v = plane(view, o.data.vertices[o.data.loops[li].vertex_index].co, across(piece, o))
                    fu = (u - b[0]) / max(b[2] - b[0], 1e-6)
                    fv = (v - b[1]) / max(b[3] - b[1], 1e-6)
                    uv.data[li].uv = ((rx + 0.05 + fu * (w - 0.1)) / ATLAS, (ry + 0.05 + fv * (h - 0.1)) / ATLAS)

    lineup = collection("Lineup")
    for i, (name, chosen) in enumerate(lineup_sets.items()):
        r = rig.copy()
        r.name = "Rig_" + name
        lineup.objects.link(r)
        gloved = any(p.endswith("hands") for p in chosen)
        worn = [o for p in chosen for o in (pieces[p] if p in pieces else shared[p])]
        for src in [o for n, o in body.items() if not (gloved and n.startswith("Hand_"))] + worn:
            c = src.copy()
            c.name = f"{name}__{src.name}"
            lineup.objects.link(c)
            c.parent = r
            c.matrix_parent_inverse = src.matrix_parent_inverse.copy()  # assigning a parent clears it
            for m in c.modifiers:
                if m.type == "ARMATURE":
                    m.object = r
        r.location.x = rig.location.x + (i - (len(lineup_sets) - 1) / 2) * -1.25  # screen left to right, seen from the front
        # the A-pose the game's outfits are shown in: arms 5 degrees and forearms 2 degrees wider than the rest pose
        for s in ("_L", "_R"):
            for bone, deg in (("Arm", 5), ("Forearm", 2)):
                pb = r.pose.bones[bone + s]
                pb.rotation_mode = "XYZ"
                best = None
                for sign in (1, -1):
                    pb.rotation_euler = (0, 0, math.radians(deg * sign))
                    bpy.context.view_layer.update()
                    reach = abs((r.pose.bones["Hand" + s].tail).x)
                    if best is None or reach > best[0]:
                        best = (reach, sign)
                pb.rotation_euler = (0, 0, math.radians(deg * best[1]))

    # keep the master pieces for export but out of sight
    for o in list(body.values()) + [rig]:
        o.hide_set(True)
        o.hide_render = True
    for lc in bpy.context.view_layer.layer_collection.children:
        if lc.name.startswith("Piece_"):
            lc.hide_viewport = True
            lc.collection.hide_render = True
    for o in bpy.data.objects:
        if o.type == "EMPTY" or o.name == "Cube":
            o.hide_render = True
            o.hide_set(True)
        if o.type == "ARMATURE":
            o.show_in_front = False
            o.hide_set(True)

    # ---------------------------------------------------------------- renders

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.exposure = 0.9
    scene.world = scene.world or bpy.data.worlds.new("w")
    scene.world.color = (0.045, 0.05, 0.058)
    scene.render.resolution_x, scene.render.resolution_y = 1900, 800
    cam_data = bpy.data.cameras.new("lineup_cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("lineup_cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    rigs = [bpy.data.objects["Rig_" + n] for n in lineup_sets]
    for view, yaw, turn in (("front", 0, 0), ("34", 0, -35), ("back", 0, 180), ("side", 0, 90)):
        for r in rigs:
            r.rotation_euler = (0, 0, math.radians(turn))
        bpy.context.view_layer.update()
        cam_data.ortho_scale = 1.28 * len(lineup_sets)
        cam.location = (rig.location.x, 8, 0.93)
        cam.rotation_euler = (math.radians(90), 0, math.radians(180))
        scene.render.filepath = os.path.join(OUT, f"{prefix}_preview_{view}.png")
        bpy.ops.render.render(write_still=True)
    for r in rigs:
        r.rotation_euler = (0, 0, 0)

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, f"{prefix}.blend"), copy=False)
    tris = {p: 0 for p in pieces}
    dg = bpy.context.evaluated_depsgraph_get()
    for lc in bpy.context.view_layer.layer_collection.children:
        lc.hide_viewport = False
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    for p, objs in pieces.items():
        for o in objs:
            me = o.evaluated_get(dg).to_mesh()
            me.calc_loop_triangles()
            tris[p] += len(me.loop_triangles)
    print("TRIS", tris)
