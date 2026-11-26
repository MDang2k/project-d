"""Builds the garrison holdout outfit pieces on the box body.

Spec: docs/superpowers/specs/factions/faction-looks-pluenderer-design.md section 3, "Garrison holdouts".

    blender -b assets/source/characters/base_character_v2_boxes.blend --python tools/build_holdout.py

Writes assets/source/characters/outfits_holdout.blend, its atlas and preview renders. The faction marker, a fictional
garrison patch (white tower on dark green) on the right shoulder, is part of every jacket piece so it sits at that
jacket's thickness. Rebuilding overwrites hand edits.
"""
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
sys.dont_write_bytecode = True
from outfit_lib import *  # noqa: E402,F403


def patch(piece, thickness):
    """The garrison patch: a thin square on the outer face of the right upper arm, near the shoulder."""
    src = body["Arm_Upper_R"]
    world = src.matrix_world
    face = max((p for p in src.data.polygons), key=lambda p: (world.to_3x3() @ p.normal).x * p.area)
    normal = (world.to_3x3() @ face.normal).normalized()
    centre = world @ face.center
    down = Vector((normal.z, 0, -normal.x))
    if down.z > 0:
        down = -down
    point = centre + down * ((1.318 - centre.z) / down.z) + normal * (thickness + 0.004)
    solid(piece, "Patch", "Arm_R", point, (0.008, 0.075, 0.082), rot=(0, math.atan2(-normal.z, normal.x), 0))


def sleeves(piece, thickness):
    both(piece, "Shoulder", thickness)
    both(piece, "Arm_Upper", thickness)
    both(piece, "Arm_Lower", thickness, zmin=0.855)


def poncho(piece):
    """A blanket over the shoulders: one loft from the neck hole to the hem, open to the arms at the sides. Its upper
    rings follow the chest and its hem the hips, so a lean bends it instead of tipping it."""
    rings = [(1.462, 0.095, 0.080, -0.200), (1.438, 0.300, 0.100, -0.222), (1.300, 0.240, 0.112, -0.235),
             (1.080, 0.272, 0.120, -0.245), (0.760, 0.310, 0.140, -0.265)]
    verts, faces = [], []
    for z, half, front, back in rings:
        verts += [(-half, front, z), (half, front, z), (half, back, z), (-half, back, z)]
    for ring in range(len(rings) - 1):
        for i in range(4):
            j = (i + 1) % 4
            faces.append([ring * 4 + i, ring * 4 + j, ring * 4 + 4 + j, ring * 4 + 4 + i])
    mesh = bpy.data.meshes.new("Poncho")
    mesh.from_pydata(verts, [], faces)
    o = attach(piece, f"{piece}__Poncho", mesh, "Chest", "Poncho")
    o.parent_type = "OBJECT"
    o.matrix_world = Matrix.Identity(4)
    hem = list(range(len(verts) - 4, len(verts)))
    for bone, indices in (("Chest", [i for i in range(len(verts)) if i not in hem]), ("Hips", hem)):
        o.vertex_groups.new(name=bone).add(indices, 1.0, "REPLACE")
    o.modifiers.new("Armature", "ARMATURE").object = rig
    solidify(o, 0.008)


# ---------------------------------------------------------------- pieces

# faded, patched 1965 OG-107 field shirt
jacket("jacket_og107", 0.008, 0.80, flare=1.03, collar_grow=1.4)
patch("jacket_og107", 0.008)

# the same with the sleeves torn off
shell("jacket_torn", "Chest", 0.008)
shell("jacket_torn", "Abdomen", 0.008, drop=0.80, grow=1.03)
both("jacket_torn", "Shoulder", 0.008)
both("jacket_torn", "Arm_Upper", 0.008, zmin=1.275)
shell("jacket_torn", "Neck", 0.006, zmin=1.425, zmax=1.475, grow=1.4)
patch("jacket_torn", 0.008)

# olive blanket poncho over a shirt
shell("jacket_poncho", "Chest", 0.005)
shell("jacket_poncho", "Abdomen", 0.005)
sleeves("jacket_poncho", 0.006)
poncho("jacket_poncho")
patch("jacket_poncho", 0.008)

# grey civilian wool sweater
shell("jacket_sweater", "Chest", 0.010)
shell("jacket_sweater", "Abdomen", 0.010, drop=0.79, grow=1.04)
sleeves("jacket_sweater", 0.009)
shell("jacket_sweater", "Neck", 0.010, zmin=1.428, zmax=1.505, grow=1.2)
patch("jacket_sweater", 0.010)

for name in ("trousers_og107", "trousers_grey"):
    shell(name, "Pelvis", 0.005)
    both(name, "Leg_Upper", 0.005)
    both(name, "Leg_Lower", 0.005, zmin=0.10)

both("boots_black", "Foot", 0.009)
both("boots_black", "Leg_Lower", 0.009, zmax=0.23)

both("hands_cutoff", "Hand", 0.005, zmin=0.735)  # the fingers stay bare

def helmet(piece):
    """The designer's own helmet (hand edit of 2026-11-26): ten-sided rings from the brow to a flat crown, a lip
    extruded down from the bottom edge, no separate rim, and a 1 cm Solidify inward."""
    rings = [(0.16168, 0.09992, 1.738, (-0.00116, 0.09404, 0.1304, -0.11884, -0.21404, -0.2504)),
             (0.15883, 0.09816, 1.795, (-0.0022, 0.09132, 0.12704, -0.1178, -0.21132, -0.24704)),
             (0.12934, 0.07994, 1.842, (-0.01293, 0.06323, 0.09232, -0.10707, -0.18323, -0.21232)),
             (0.06657, 0.04114, 1.87293, (-0.03577, 0.00343, 0.0184, -0.08423, -0.12343, -0.1384))]
    verts = []
    for wide, narrow, z, (side_f, mid_f, front, side_b, mid_b, back) in rings:
        verts += [(wide, side_f, z), (narrow, mid_f, z), (0, front, z), (-narrow, mid_f, z), (-wide, side_f, z),
                  (-wide, side_b, z), (-narrow, mid_b, z), (0, back, z), (narrow, mid_b, z), (wide, side_b, z)]
    verts += [(0.1685, 0.02382, 1.71241), (0.10013, 0.11294, 1.71628), (0, 0.15522, 1.71117),
              (-0.10013, 0.11294, 1.71628), (-0.1685, 0.02382, 1.71241), (-0.1685, -0.12094, 1.69426),
              (-0.10414, -0.21576, 1.70607), (0, -0.2539, 1.71354), (0.10414, -0.21576, 1.70607),
              (0.1685, -0.12094, 1.69426)]  # the lip, one point under each bottom-ring point
    faces = [[r * 10 + i, r * 10 + (i + 1) % 10, r * 10 + 10 + (i + 1) % 10, r * 10 + 10 + i]
             for r in range(3) for i in range(10)]
    faces.append(list(range(30, 40)))  # crown
    faces += [[(i + 1) % 10, i, 40 + i, 40 + (i + 1) % 10] for i in range(10)]
    mesh = bpy.data.meshes.new("Dome")
    mesh.from_pydata(verts, [], faces)
    modifier = attach(piece, f"{piece}__Dome", mesh, "Head", "Dome").modifiers.new("Solidify", "SOLIDIFY")
    modifier.thickness = 0.01
    modifier.offset = -1.0


helmet("head_helmet")

# bare head, long hair
solid("head_hair", "Top", "Head", (0, -0.06, 1.808), (0.252, 0.286, 0.034))
solid("head_hair", "Back", "Head", (0, -0.2, 1.635), (0.252, 0.024, 0.345), taper=0.9)
for s, x in (("_L", -0.123), ("_R", 0.123)):
    solid("head_hair", "Side" + s, "Head", (x, -0.115, 1.645), (0.018, 0.17, 0.31), key="Side")
solid("head_hair", "Fringe", "Head", (0, 0.073, 1.772), (0.236, 0.014, 0.045))

# long beard
solid("face_beard", "Chin", "Head", (0, 0.084, 1.497), (0.168, 0.032, 0.150), taper=0.45)
solid("face_beard", "Moustache", "Head", (0, 0.082, 1.578), (0.118, 0.018, 0.026))
for s, x in (("_L", -0.100), ("_R", 0.100)):
    solid("face_beard", "Cheek" + s, "Head", (x, 0.058, 1.555), (0.034, 0.046, 0.105), key="Cheek")

# gas-mask bag at the left hip (its strap is painted onto the jackets)
solid("bag_gasmask", "Bag", "Hips", (-0.262, -0.055, 0.775), (0.082, 0.170, 0.200))
solid("bag_gasmask", "Flap", "Hips", (-0.266, -0.055, 0.858), (0.096, 0.182, 0.048))

JACKETS = ("jacket_og107", "jacket_torn", "jacket_poncho", "jacket_sweater")


def make_whole(piece, key):
    """Replaces a half part's Mirror with real geometry: the diagonal strap needs the two sides painted differently."""
    for o in pieces[piece]:
        if meta[o.name][1] != key or "Mirror" not in o.modifiers:
            continue
        bm = bmesh.new()
        bm.from_mesh(o.data)
        copy = bmesh.ops.duplicate(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:])["geom"]
        for v in (g for g in copy if isinstance(g, bmesh.types.BMVert)):
            v.co.x = -v.co.x
        bmesh.ops.reverse_faces(bm, faces=[g for g in copy if isinstance(g, bmesh.types.BMFace)])
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.001)
        bm.to_mesh(o.data)
        bm.free()
        o.modifiers.remove(o.modifiers["Mirror"])
        FULL.add((piece, key))


for name in JACKETS:
    for key in ("Chest", "Abdomen"):
        make_whole(name, key)

SIGNED.update({*JACKETS, "head_hair", "face_beard", "head_helmet"})
layout()

# ---------------------------------------------------------------- painting


def patches(piece, spots, stitch):
    """Sewn-on repairs: (part, views, u0, v0, u1, v1, colour) with a stitched edge."""
    for key, views, u0, v0, u1, v1, color in spots:
        mark(piece, key, views, u0, v0, u1, v1, stitch)
        mark(piece, key, views, u0 + 0.06, v0 + 0.05, u1 - 0.06, v1 - 0.05, color)


def tower(piece):
    """The fictional garrison patch: a white tower with three merlons on dark green."""
    paint_at = lambda *box: mark(piece, "Patch", "side", *box)
    paint_at(0, 0, 1, 1, "22402c")
    paint_at(0.30, 0.15, 0.70, 0.62, "e6e4d8")
    paint_at(0.20, 0.62, 0.80, 0.74, "e6e4d8")
    for u in (0.20, 0.44, 0.68):
        paint_at(u, 0.74, u + 0.13, 0.87, "e6e4d8")
    paint_at(0.43, 0.15, 0.57, 0.34, "22402c")
    mark(piece, "Patch", "front back top", 0, 0, 1, 1, "22402c")


for name in ("jacket_og107", "jacket_torn"):
    paint(name, "7d805e", noise=0.04, patch=0.035)
    for key in ("Chest", "Abdomen"):
        mark(name, key, "front", 0, 0, 0.05, 1, "575a3e")
    buttons(name, "44472f")
    pocket(name, "Chest", 0.32, 0.22, 0.84, 0.62, "595c40", "73765a")
    patches(name, [("Abdomen", "front", 0.40, 0.18, 0.80, 0.50, "8f8966"),
                   ("Chest", "back", 0.20, 0.30, 0.62, 0.62, "626647")], "3f4230")
    mark(name, "Neck", "front back side top", 0, 0, 1, 1, "6d7050")
patches("jacket_og107", [("Arm_Upper", "side", 0.2, 0.05, 0.8, 0.35, "626647")], "3f4230")
cuffs("jacket_og107", "575a3e")
for u in range(10):  # torn edge: ragged threads under the short stubs
    mark("jacket_torn", "Arm_Upper", "front back side", u / 10, 0, u / 10 + 0.06, 0.12 + 0.18 * (u % 3) / 2, "4a4d36")

paint("jacket_poncho", "5a6042", noise=0.05, patch=0.03)
for v0, color in ((0.06, "3d4130"), (0.13, "827b58"), (0.19, "3d4130")):
    mark("jacket_poncho", "Poncho", "front back side", 0, v0, 1, v0 + 0.045, color)
mark("jacket_poncho", "Poncho", "front back side", 0, 0, 1, 0.025, "33362a")      # frayed hem
mark("jacket_poncho", "Poncho", "front", 0.46, 0.80, 0.54, 1.0, "2f3226")         # neck slit
for u in (0.08, 0.84):                                                            # woven border bands
    mark("jacket_poncho", "Poncho", "front back", u, 0, u + 0.08, 0.92, "444933")
patches("jacket_poncho", [("Poncho", "back", 0.20, 0.42, 0.55, 0.60, "6e6a4a")], "33362a")
for key in ("Chest", "Abdomen"):
    mark("jacket_poncho", key, "front back side", 0, 0, 1, 1, "68624c")
for key in ("Shoulder", "Arm_Upper", "Arm_Lower"):
    mark("jacket_poncho", key, "front back side top", 0, 0, 1, 1, "6a6a54")
cuffs("jacket_poncho", "4c4c3c")

paint("jacket_sweater", "80807a", noise=0.05, ribs=0.03)
mark("jacket_sweater", "Abdomen", "front back side", 0, 0, 1, 0.20, "6a6a64")
cuffs("jacket_sweater", "6a6a64", 0.18)
mark("jacket_sweater", "Neck", "front back side top", 0, 0, 1, 1, "6a6a64")
patches("jacket_sweater", [("Arm_Lower", "side", 0.1, 0.45, 0.9, 0.80, "6b5a46")], "4a3e30")

for name in ("jacket_og107", "jacket_torn", "jacket_poncho", "jacket_sweater"):
    tower(name)

for name, base, line, knee in (("trousers_og107", "74785a", "52563c", "8c8a68"),
                               ("trousers_grey", "6c6a66", "4a4946", "5a4f42")):
    paint(name, base, noise=0.04, patch=0.03)
    mark(name, "Pelvis", "front back side", 0, 0.76, 1, 0.97, line)
    mark(name, "Pelvis", "front", 0, 0, 0.07, 0.76, line)
    mark(name, "Leg_Upper", "side", 0.46, 0, 0.54, 1, line)
    mark(name, "Leg_Lower", "side", 0.46, 0, 0.54, 1, line)
    patches(name, [("Leg_Upper", "front", 0.15, 0.0, 0.85, 0.28, knee)], line)

paint("boots_black", "262524", noise=0.03, patch=0.07)
mark("boots_black", "Foot", "front back side", 0, 0, 1, 0.26, "131313")
for i in range(4):
    mark("boots_black", "Leg_Lower", "front", 0.28, 0.12 + i * 0.22, 0.72, 0.19 + i * 0.22, "55514a")

paint("hands_cutoff", "6e6a5c", noise=0.08)
mark("hands_cutoff", "Hand", "front back side", 0, 0, 1, 0.16, "4c4940")

paint("head_helmet", "5f6644", noise=0.04)
camo("head_helmet", [("4a4a30", 0.14, 4, 2), ("767a52", 0.10, 3, 2)])
mark("head_helmet", "Dome", "front", 0.56, 0.35, 0.74, 0.62, "33362f")   # torn cover: bare steel
mark("head_helmet", "Dome", "side", 0.18, 0.45, 0.34, 0.70, "33362f")
mark("head_helmet", "Dome", "front back side", 0, 0, 1, 0.24, "3a3e30")  # lip and band

for piece, base, grey in (("head_hair", "4b3829", "83786a"), ("face_beard", "58422f", "8a7f70")):
    paint(piece, base, noise=0.05)
    camo(piece, [(grey, 0.13, 1, 4), ("35271c", 0.15, 1, 5)])

paint("bag_gasmask", "6d6b4b", noise=0.04, patch=0.02)
mark("bag_gasmask", "Flap", "front back side top", 0, 0, 1, 0.35, "52503a")
mark("bag_gasmask", "Flap", "side", 0.42, 0.1, 0.58, 0.75, "b0a681")  # buckle


def strap(piece, top=(0.123, 1.425), bottom=(-0.215, 0.885)):
    """The bag's strap, right shoulder to left hip, front and back, painted in world (x, z) on the torso."""
    (x0, z0), (x1, z1) = bottom, top
    for (p, key, view), (rx, ry, w, h) in rects.items():
        if p != piece or key not in ("Chest", "Abdomen", "Poncho") or view not in ("front", "back"):
            continue
        left, low, right, high = bounds[(p, key, view)]
        for j in range(h):
            for i in range(w):
                x = left + (i + 0.5) / w * (right - left)
                z = low + (j + 0.5) / h * (high - low)
                along = max(0.0, min(1.0, ((x - x0) * (x1 - x0) + (z - z0) * (z1 - z0)) /
                                     ((x1 - x0) ** 2 + (z1 - z0) ** 2)))
                away = math.hypot(x - x0 - along * (x1 - x0), z - z0 - along * (z1 - z0))
                if away < 0.018:
                    pixels[ry + j, rx + i] = rgb("4a4632" if away < 0.011 else "312e20")


for name in JACKETS:
    strap(name)

LINEUP = {
    "holdout_a": ["jacket_og107", "trousers_og107", "boots_black", "head_helmet", "face_beard", "bag_gasmask"],
    "holdout_b": ["jacket_torn", "trousers_grey", "boots_black", "hands_cutoff", "head_hair", "face_beard"],
    "holdout_c": ["jacket_poncho", "trousers_og107", "boots_black", "hands_cutoff", "head_helmet"],
    "holdout_d": ["jacket_sweater", "trousers_grey", "boots_black", "head_hair", "face_beard", "bag_gasmask"],
}

finish("outfits_holdout", "holdout_detail", LINEUP)
