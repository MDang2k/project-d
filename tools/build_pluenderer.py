"""Builds the Pluenderer outfit pieces (followers and Bandenfuehrer) on the box body.

Spec: docs/superpowers/specs/factions/faction-looks-pluenderer-design.md section 3.

    blender -b assets/source/characters/base_character_v2_boxes.blend --python tools/build_pluenderer.py

Writes assets/source/characters/outfits_pluenderer.blend, its atlas and preview renders. Every piece is a
collection "Piece_<slot>_<option>" of shells on the box body's bones (copies of body boxes with a live Solidify,
plus a few small boxes and cylinders). A "Lineup" collection shows rolled variants in the A-pose.
Rebuilding overwrites hand edits in outfits_pluenderer.blend.
"""
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


def shell(piece, part, thickness, zmin=None, zmax=None, grow=1.0, drop=None, rise=None):
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
    key = part[:-2] if part.endswith(("_L", "_R")) else part
    o = attach(piece, f"{piece}__{part}", mesh, src.parent_bone, key)
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


# ---------------------------------------------------------------- pieces

def jacket(piece, t, hem, flare=1.05, collar_grow=1.35):
    shell(piece, "Chest", t)
    shell(piece, "Abdomen", t, drop=hem, grow=flare)
    both(piece, "Shoulder", t)
    both(piece, "Arm_Upper", t)
    both(piece, "Arm_Lower", t, zmin=0.865)
    shell(piece, "Neck", 0.006, zmin=1.425, zmax=1.475, grow=collar_grow)


# followers
jacket("jacket_work", 0.008, 0.775)
jacket("jacket_cord", 0.010, 0.80, flare=1.03)
shell("jacket_cord", "Pelvis", 0.014)
both("jacket_cord", "Leg_Upper", 0.012, zmin=0.57, grow=1.05)
jacket("jacket_parka", 0.014, 0.79, flare=1.06, collar_grow=1.55)
shell("jacket_parka", "Pelvis", 0.018, grow=1.04)
solid("jacket_parka", "Hood", "Chest", (0, -0.19, 1.41), (0.24, 0.07, 0.09), rot=(math.radians(-20), 0, 0))

for name in ("trousers_grey", "trousers_cord", "trousers_jeans", "bf_trousers"):
    shell(name, "Pelvis", 0.005)
    both(name, "Leg_Upper", 0.005)
    both(name, "Leg_Lower", 0.005, zmin=0.10)

for name, top, t in (("boots_brown", 0.19, 0.009), ("boots_rubber", 0.35, 0.013), ("bf_boots", 0.24, 0.009)):
    both(name, "Foot", t)
    both(name, "Leg_Lower", t, zmax=top)

both("gloves_wool", "Hand", 0.005)
both("bf_gloves", "Hand", 0.005)

for name in ("head_flatcap", "bf_head_flatcap"):
    shell(name, "Head", 0.012, zmin=1.735, rise=1.805, grow=1.07)
    solid(name, "Brim", "Head", (0, 0.095, 1.742), (0.19, 0.075, 0.016), rot=(math.radians(-8), 0, 0))

shell("head_watchcap", "Head", 0.012, zmin=1.715, rise=1.835, grow=1.03)

shell("face_scarf", "Head", 0.008, zmin=1.50, zmax=1.62, grow=1.04)
solid("face_scarf", "Knot", "Head", (0, 0.07, 1.49), (0.16, 0.03, 0.10), taper=0.25)

# a full-face mask: wraps the front half of the head from chin to brow
solid("face_gasmask", "Mask", "Head", (0, 0.025, 1.617), (0.25, 0.14, 0.25))
for s, x in (("_L", -0.055), ("_R", 0.055)):
    tube("face_gasmask", "Rim" + s, "Head", (x, 0.098, 1.662), 0.046, 0.014, rot=(math.radians(90), 0, 0), key="Rim")
    tube("face_gasmask", "Lens" + s, "Head", (x, 0.107, 1.662), 0.035, 0.012, rot=(math.radians(90), 0, 0), key="Lens")
solid("face_gasmask", "Snout", "Head", (0, 0.105, 1.575), (0.095, 0.05, 0.075), taper=0.8)
tube("face_gasmask", "Filter", "Head", (0, 0.145, 1.545), 0.045, 0.06, rot=(math.radians(105), 0, 0))
for s, x in (("_L", -0.121), ("_R", 0.121)):
    solid("face_gasmask", "Strap" + s, "Head", (x, -0.115, 1.64), (0.012, 0.15, 0.022), key="Strap")
solid("face_gasmask", "StrapBack", "Head", (0, -0.193, 1.64), (0.254, 0.01, 0.022), key="Strap")

# the follower marker: a duffel sack slung on the back
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


lathe("sack", "Bag", "Chest", (0.02, -0.285, 1.10),
      [(-0.22, 0.09), (-0.17, 0.15), (0.00, 0.17), (0.13, 0.13), (0.20, 0.055), (0.25, 0.08)], rot=(0, math.radians(24), 0))
solid("sack", "StrapFront", "Chest", (0.0, 0.074, 1.27), (0.035, 0.008, 0.43), rot=(0, math.radians(38), 0), key="Belt")
solid("sack", "StrapTop", "Chest", (0.14, -0.055, 1.452), (0.035, 0.27, 0.008), key="Belt")


def skirt(piece, waist, gap_top, gap_bottom):
    """The long coat below the waist: one flared skirt, split down the middle into a half per thigh so the halves
    meet when standing and part when the legs move. `gap_*` open the front edges.

    The only piece on two bones (every vertex still 100% on one): the top edge and the back of the hem follow the
    hips, the front of the hem follows the thigh. In a crouch the back hangs down and the front lies over the legs.

    The top edge is the outline of the `waist` shell's lower rim, tucked just inside it, so the two read as one coat."""
    low = min(v.co.z for v in waist.data.vertices)
    edge = [v.co for v in waist.data.vertices if v.co.z < low + 0.001]
    wide = max(v.x for v in edge)
    front_y, back_y = max(v.y for v in edge), min(v.y for v in edge)
    middle, deep = (front_y + back_y) / 2, (front_y - back_y) / 2
    rim = [(0.0, front_y), (wide, front_y), (wide, back_y), (0.0, back_y)]  # front centre, round to back centre
    # the waist shell's surface is its thickness outside the rim; the skirt's own surface ends 2 mm inside that
    out = waist.modifiers["Solidify"].thickness - 0.007 - 0.002
    for s, sign in (("_L", -1), ("_R", 1)):
        top, bottom = [], []
        for x, y in rim:
            tx = x * (wide + out) / wide
            ty = middle + (y - middle) * (deep + out) / deep
            bx = x * 0.285 / wide
            by = -0.0635 + (y - middle) * 0.1815 / deep
            if x < 0.001:  # the centre line: the front edges may stand open
                if y > middle:
                    tx, bx = gap_top, gap_bottom
                else:
                    tx = bx = 0.0
            top.append((sign * tx, ty, low + 0.035))
            bottom.append((sign * bx, by, 0.34))
        faces = []
        for i in range(len(top) - 1):
            quad = [i, i + 1, len(top) + i + 1, len(top) + i]
            faces.append(quad if sign > 0 else quad[::-1])
        mesh = bpy.data.meshes.new(f"Skirt{s}")
        mesh.from_pydata(top + bottom, [], faces)
        o = attach(piece, f"{piece}__Skirt{s}", mesh, "Hips", "Skirt")
        o.parent_type = "OBJECT"  # skinned by the armature, not hung on one bone
        o.matrix_world = Matrix.Identity(4)
        front_hem = [len(top) + i for i, (_, y) in enumerate(rim) if y > middle]
        rest = [i for i in range(2 * len(top)) if i not in front_hem]
        for bone, indices in (("Hips", rest), ("Thigh" + s, front_hem)):
            o.vertex_groups.new(name=bone).add(indices, 1.0, "REPLACE")
        o.modifiers.new("Armature", "ARMATURE").object = rig
        solidify(o, 0.007)


def coat(piece, gap_top=0.0, gap_bottom=0.0):
    shell(piece, "Chest", 0.011)
    waist = shell(piece, "Abdomen", 0.011, drop=0.80, grow=1.03)
    for v in waist.data.vertices:
        if v.co.z < 0.801:
            v.co.x *= 1.04  # only the lower rim: wide enough to cover the trousers, no step under the chest
    skirt(piece, waist, gap_top, gap_bottom)
    both(piece, "Shoulder", 0.011)
    both(piece, "Arm_Upper", 0.010)
    both(piece, "Arm_Lower", 0.010, zmin=0.825)  # long enough to meet the gloves
    shell(piece, "Neck", 0.008, zmin=1.425, zmax=1.495, grow=1.5)


coat("bf_coat_closed")
coat("bf_coat_open", gap_top=0.045, gap_bottom=0.11)

for name in ("bf_glasses",):
    for s, x in (("_L", -0.047), ("_R", 0.047)):
        solid(name, "Lens" + s, "Head", (x, 0.078, 1.668), (0.066, 0.01, 0.038), key="Lens")
        solid(name, "Arm" + s, "Head", (x * 2.55, -0.005, 1.675), (0.008, 0.16, 0.01), key="Arm")
        # the end piece joining the lens (outer edge at 0.080) to its side arm (x 0.120)
        solid(name, "Hinge" + s, "Head", (x / 0.047 * 0.101, 0.076, 1.676), (0.046, 0.008, 0.01), key="Arm")
    solid(name, "Bridge", "Head", (0, 0.078, 1.678), (0.03, 0.008, 0.008), key="Arm")

# the Bandenfuehrer marker: an ochre neckerchief
shell("bf_neckerchief", "Neck", 0.008, zmin=1.45, zmax=1.50, grow=1.28)
solid("bf_neckerchief", "Point", "Chest", (0, 0.078, 1.40), (0.15, 0.014, 0.12), taper=0.08)

# ---------------------------------------------------------------- atlas layout

def view_of(n):
    ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
    if az > ax and az > ay:
        return "top"
    if ay >= ax:
        return "front" if n.y > 0 else "back"
    return "side"


def plane(view, co):
    if view in ("front", "back"):
        return abs(co.x), co.z
    if view == "side":
        return co.y, co.z
    return abs(co.x), co.y


bounds = {}
for piece, objs in pieces.items():
    for o in objs:
        _, key = meta[o.name]
        for p in o.data.polygons:
            view = view_of(p.normal)
            b = bounds.setdefault((piece, key, view), [9e9, 9e9, -9e9, -9e9])
            for vi in p.vertices:
                u, v = plane(view, o.data.vertices[vi].co)
                b[0], b[1], b[2], b[3] = min(b[0], u), min(b[1], v), max(b[2], u), max(b[3], v)

rects = {}
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

# ---------------------------------------------------------------- painting (row 0 is the bottom)

pixels = np.zeros((ATLAS, ATLAS, 3), dtype=np.float32)
rng = np.random.default_rng(7)


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
        a, b = int(round(u0 * w)), int(round(u1 * w))
        c, d = int(round(v0 * h)), int(round(v1 * h))
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


# blue cotton work jacket
paint("jacket_work", "3f5472", noise=0.03, patch=0.025)
for key in ("Chest", "Abdomen"):
    mark("jacket_work", key, "front", 0, 0, 0.05, 1, "27344a")
buttons("jacket_work", "1d2433")
pocket("jacket_work", "Chest", 0.3, 0.28, 0.82, 0.66, "2b3a53", "46597a")
pocket("jacket_work", "Abdomen", 0.3, 0.22, 0.86, 0.60, "2b3a53", "46597a")
cuffs("jacket_work", "2b3a53")
mark("jacket_work", "Neck", "front back side top", 0, 0, 1, 1, "4a6086")

# brown corduroy coat
paint("jacket_cord", "6b4a32", noise=0.015, ribs=0.045)
for key in ("Chest", "Abdomen", "Pelvis"):
    mark("jacket_cord", key, "front", 0, 0, 0.05, 1, "3d281a")
buttons("jacket_cord", "241710", keys=("Abdomen", "Pelvis"))
for i in range(6):  # lapels falling to a V
    mark("jacket_cord", "Chest", "front", 0.04 + i * 0.07, 0.30 + i * 0.12, 0.14 + i * 0.07, 0.44 + i * 0.12, "4a3120")
    mark("jacket_cord", "Chest", "front", 0.0, 0.40 + i * 0.12, 0.04 + i * 0.07, 0.55 + i * 0.12, "8b8578")
pocket("jacket_cord", "Abdomen", 0.34, 0.12, 0.86, 0.40, "4a3120", "725137")
cuffs("jacket_cord", "4a3120")
mark("jacket_cord", "Neck", "front back side top", 0, 0, 1, 1, "4f3523")

# faded Bundeswehr olive moleskin parka
paint("jacket_parka", "6a6c4a", noise=0.03, patch=0.045)
for key in ("Chest", "Abdomen", "Pelvis"):
    mark("jacket_parka", key, "front", 0, 0, 0.09, 1, "4b4d33")
mark("jacket_parka", "Abdomen", "front back side", 0, 0.55, 1, 0.66, "4b4d33")
mark("jacket_parka", "Abdomen", "front", 0.12, 0.30, 0.17, 0.58, "b9b49a")  # drawstring
pocket("jacket_parka", "Chest", 0.34, 0.12, 0.88, 0.55, "484a31", "73754f")
pocket("jacket_parka", "Abdomen", 0.30, 0.02, 0.90, 0.46, "484a31", "73754f")
mark("jacket_parka", "Pelvis", "front back side", 0, 0, 1, 0.18, "55573a")
mark("jacket_parka", "Arm_Upper", "side", 0.2, 0.45, 0.8, 0.75, "55573a")
cuffs("jacket_parka", "4b4d33")
paint_hood = ("jacket_parka", "Hood", "back top", 0.1, 0.35, 0.9, 0.45, "4b4d33")
mark(*paint_hood)

# trousers
for name, base, line, light, kw in (
        ("trousers_grey", "63635f", "47474a", "74746f", dict(noise=0.03, patch=0.02)),
        ("trousers_cord", "56402f", "37281c", "654c39", dict(noise=0.012, ribs=0.04)),
        ("trousers_jeans", "56708c", "394d66", "7f97b0", dict(noise=0.055, patch=0.04)),
        ("bf_trousers", "3d2d22", "281c14", "493729", dict(noise=0.018))):
    paint(name, base, **kw)
    mark(name, "Pelvis", "front back side", 0, 0.74, 1, 0.97, line)      # waistband / belt
    mark(name, "Pelvis", "front", 0, 0, 0.07, 0.74, line)                # fly
    mark(name, "Leg_Upper", "side", 0.46, 0, 0.54, 1, line)              # outer seam
    mark(name, "Leg_Lower", "side", 0.46, 0, 0.54, 1, line)
    mark(name, "Leg_Upper", "front", 0.2, 0.05, 0.8, 0.16, light)        # worn knee
    mark(name, "Pelvis", "front", 0.62, 0.52, 0.9, 0.58, line)           # pocket mouth
    mark(name, "Pelvis", "back", 0.28, 0.18, 0.76, 0.55, line)
    mark(name, "Pelvis", "back", 0.34, 0.24, 0.70, 0.49, base)

# boots
for name, base, sole, lace in (("boots_brown", "5a3b27", "2a1c12", "b59a6c"), ("bf_boots", "1f1e1e", "0f0f0f", "55524e")):
    paint(name, base, noise=0.02)
    mark(name, "Foot", "front back side", 0, 0, 1, 0.26, sole)
    for i in range(4):
        mark(name, "Leg_Lower", "front", 0.28, 0.12 + i * 0.22, 0.72, 0.19 + i * 0.22, lace)
    mark(name, "Leg_Lower", "front back side", 0, 0.9, 1, 1, sole)
paint("boots_rubber", "45603f", noise=0.012)
mark("boots_rubber", "Foot", "front back side", 0, 0, 1, 0.24, "26301f")
mark("boots_rubber", "Leg_Lower", "front back side", 0, 0.9, 1, 1, "5c7752")
mark("boots_rubber", "Leg_Lower", "front", 0.12, 0.1, 0.24, 0.85, "6f8d63")  # rubber shine

paint("gloves_wool", "7b7a72", noise=0.07)
mark("gloves_wool", "Hand", "front back side", 0, 0.82, 1, 1, "5e5d57")
paint("bf_gloves", "232120", noise=0.015)
mark("bf_gloves", "Hand", "side", 0.2, 0.55, 0.8, 0.62, "45413e")

paint("head_flatcap", "6d6657", noise=0.06)
mark("head_flatcap", "Head", "front back side", 0, 0, 1, 0.2, "4a453a")
paint("bf_head_flatcap", "2e2b2a", noise=0.03)
paint("head_watchcap", "3b4250", noise=0.025, ribs=0.035)
mark("head_watchcap", "Head", "front back side", 0, 0, 1, 0.32, "2d333f")

paint("face_scarf", "7d4a3b", noise=0.04)
mark("face_scarf", "Head", "front back side", 0, 0.5, 1, 0.62, "9c6a52")
mark("face_scarf", "Head", "front back side", 0, 0.12, 1, 0.24, "5e342a")
paint("face_gasmask", "50564a", noise=0.02)
mark("face_gasmask", "Mask", "front", 0, 0, 0.05, 0.4, "3c4137")      # centre seam
mark("face_gasmask", "Mask", "front side", 0, 0.9, 1, 1, "3c4137")    # edge binding
mark("face_gasmask", "Mask", "front side", 0, 0, 1, 0.08, "3c4137")
mark("face_gasmask", "Lens", "front", 0, 0, 1, 1, "a7bcc0")
mark("face_gasmask", "Lens", "front", 0.0, 0.0, 0.55, 0.4, "56686c")
for key in ("Rim", "Filter"):
    mark("face_gasmask", key, "side top front back", 0, 0, 1, 1, "34382f")
mark("face_gasmask", "Strap", "side top front back", 0, 0, 1, 1, "262822")

paint("sack", "8a7d5c", noise=0.045, patch=-0.04)
mark("sack", "Bag", "back side front", 0, 0.80, 1, 0.88, "4a3a28")  # tie
mark("sack", "Bag", "back", 0.2, 0.22, 0.75, 0.46, "6f6549")  # sewn patch
for key in ("Belt",):
    mark("sack", key, "front back side top", 0, 0, 1, 1, "4a3a28")

# long black leather coat, buttoned and open
for name in ("bf_coat_closed", "bf_coat_open"):
    paint(name, "2a2727", noise=0.016, patch=0.02)
    for key in ("Chest", "Arm_Upper"):
        mark(name, key, "front", 0.55, 0.2, 0.62, 0.9, "46403e")      # leather sheen
    mark(name, "Abdomen", "front back side", 0, 0.30, 1, 0.45, "171515")  # belt
    mark(name, "Abdomen", "front", 0.0, 0.31, 0.16, 0.44, "8a7f66")       # buckle
    mark(name, "Skirt", "front", 0.42, 0.74, 0.86, 0.80, "151313")      # pocket flap
    mark(name, "Skirt", "front", 0.55, 0.06, 0.61, 0.70, "46403e")      # leather folds
    mark(name, "Skirt", "back", 0.45, 0.04, 0.50, 0.85, "46403e")
    mark(name, "Skirt", "side", 0.35, 0.04, 0.40, 0.90, "3d3837")
    mark(name, "Skirt", "back", 0.0, 0.0, 0.04, 0.6, "121111")          # back vent
    mark(name, "Skirt", "front back side", 0, 0, 1, 0.05, "4a4342")     # hem edge
    cuffs(name, "171515", 0.1)
    mark(name, "Neck", "front back side top", 0, 0, 1, 1, "383333")
for i in range(6):
    mark("bf_coat_closed", "Chest", "front", 0.05 + i * 0.08, 0.28 + i * 0.12, 0.17 + i * 0.08, 0.42 + i * 0.12, "453f3e")
for key in ("Chest", "Abdomen", "Skirt"):
    mark("bf_coat_closed", key, "front", 0.0, 0, 0.05, 1, "121111")
buttons("bf_coat_closed", "6e675c", keys=("Chest",), u=0.12)
for v in (0.9, 0.68, 0.46):
    mark("bf_coat_closed", "Skirt", "front", 0.09, v, 0.15, v + 0.035, "6e675c")
for key in ("Chest", "Abdomen"):                                           # open: dark roll-neck sweater
    mark("bf_coat_open", key, "front", 0.0, 0, 0.26, 1, "56473a")
    mark("bf_coat_open", key, "front", 0.26, 0, 0.34, 1, "4a4443")
mark("bf_coat_open", "Abdomen", "front", 0.0, 0.30, 0.26, 0.40, "221a14")
mark("bf_coat_open", "Chest", "front", 0.34, 0.55, 0.52, 1.0, "403a39")    # lapel

paint("bf_glasses", "151515", noise=0.0)
mark("bf_glasses", "Lens", "front", 0.12, 0.55, 0.5, 0.8, "5a6670")
paint("bf_neckerchief", "c08a2b", noise=0.04)
mark("bf_neckerchief", "Neck", "front back side", 0, 0.0, 1, 0.2, "93671c")

# spread each region's colours into the unused space, so smaller mipmaps never blend in a black background
filled = np.zeros((ATLAS, ATLAS), dtype=bool)
for rx, ry, w, h in rects.values():
    filled[ry - 1:ry + h + 1, rx - 1:rx + w + 1] = True
while not filled.all():
    for axis, step in ((0, 1), (0, -1), (1, 1), (1, -1)):
        grow_into = ~filled & np.roll(filled, step, axis)
        pixels[grow_into] = np.roll(pixels, step, axis)[grow_into]
        filled |= grow_into

img = bpy.data.images.new("outfits_pluenderer_tex", ATLAS, ATLAS, alpha=True)
rgba = np.ones((ATLAS, ATLAS, 4), dtype=np.float32)
rgba[..., :3] = np.clip(pixels, 0, 1)
img.pixels.foreach_set(rgba.ravel())
img.filepath_raw = os.path.join(OUT, "outfits_pluenderer_tex.png")
img.file_format = "PNG"
img.save()

mat = bpy.data.materials.new("pluenderer_detail")
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
                u, v = plane(view, o.data.vertices[o.data.loops[li].vertex_index].co)
                fu = (u - b[0]) / max(b[2] - b[0], 1e-6)
                fv = (v - b[1]) / max(b[3] - b[1], 1e-6)
                uv.data[li].uv = ((rx + 0.05 + fu * (w - 0.1)) / ATLAS, (ry + 0.05 + fv * (h - 0.1)) / ATLAS)

# ---------------------------------------------------------------- lineup in the A-pose

LINEUP = {
    "follower_a": ["jacket_work", "trousers_grey", "boots_brown", "head_flatcap", "sack"],
    "follower_b": ["jacket_cord", "trousers_jeans", "boots_rubber", "gloves_wool", "head_watchcap", "face_scarf", "sack"],
    "follower_c": ["jacket_parka", "trousers_cord", "boots_brown", "face_gasmask", "sack"],
    "bandenfuehrer_a": ["bf_coat_closed", "bf_trousers", "bf_boots", "bf_gloves", "bf_head_flatcap", "bf_neckerchief"],
    "bandenfuehrer_b": ["bf_coat_open", "bf_trousers", "bf_boots", "bf_gloves", "bf_glasses", "bf_neckerchief"],
}
lineup = collection("Lineup")
for i, (name, chosen) in enumerate(LINEUP.items()):
    r = rig.copy()
    r.name = "Rig_" + name
    lineup.objects.link(r)
    for src in list(body.values()) + [o for p in chosen for o in pieces[p]]:
        c = src.copy()
        c.name = f"{name}__{src.name}"
        lineup.objects.link(c)
        c.parent = r
        c.matrix_parent_inverse = src.matrix_parent_inverse.copy()  # assigning a parent clears it
        for m in c.modifiers:
            if m.type == "ARMATURE":
                m.object = r
    r.location.x = rig.location.x + (i - 2) * -1.25  # screen left to right, seen from the front
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
rigs = [bpy.data.objects["Rig_" + n] for n in LINEUP]
for view, yaw, turn in (("front", 0, 0), ("34", 0, -35), ("back", 0, 180), ("side", 0, 90)):
    for r in rigs:
        r.rotation_euler = (0, 0, math.radians(turn))
    bpy.context.view_layer.update()
    cam_data.ortho_scale = 6.4
    cam.location = (rig.location.x, 8, 0.93)
    cam.rotation_euler = (math.radians(90), 0, math.radians(180))
    scene.render.filepath = os.path.join(OUT, f"outfits_pluenderer_preview_{view}.png")
    bpy.ops.render.render(write_still=True)
for r in rigs:
    r.rotation_euler = (0, 0, 0)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "outfits_pluenderer.blend"), copy=False)
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
