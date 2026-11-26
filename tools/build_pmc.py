"""Builds the six new PMC outfits (flanker, marksman and assault for each side) and their headwear on the box body.

Spec: docs/superpowers/specs/factions/faction-looks-pluenderer-design.md section 2.

    blender -b assets/source/characters/base_character_v2_boxes.blend --python tools/build_pmc.py -- allied
    blender -b assets/source/characters/base_character_v2_boxes.blend --python tools/build_pmc.py -- soviet

Writes assets/source/characters/outfits_pmc_<side>.blend, its atlas and preview renders. Only the upper body and
headwear are new (the KLMK coverall also covers the legs); trousers, boots and hands are the side's player outfit,
which the lineup wears next to each new piece. Rebuilding overwrites hand edits.
"""
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
sys.dont_write_bytecode = True
from outfit_lib import *  # noqa: E402,F403

SIDE = sys.argv[sys.argv.index("--") + 1]
assert SIDE in ("allied", "soviet")
ARMBAND = {"allied": "8fa6c8", "soviet": "b9343b"}[SIDE]


def armband(piece, thickness):
    """The side's marker, left upper arm only, so it has its own atlas region."""
    shell(piece, "Arm_Upper_L", thickness + 0.005, zmin=1.225, zmax=1.295, key="Armband")


def rolled_sleeves(piece, thickness):
    """Upper sleeves with a thick roll above the elbow; the forearms stay bare."""
    both(piece, "Shoulder", thickness)
    both(piece, "Arm_Upper", thickness)
    for s in ("_L", "_R"):
        shell(piece, "Arm_Upper" + s, thickness + 0.007, zmax=1.115, key="Roll")


def beret(piece):
    """A square cap with a painted band and a ridge off centre: the designer's own edit of 2026-11-26 (a loop cut
    at x -0.0798 with its top raised to z 1.8668)."""
    ridge_x, ridge_z = -0.07981, 1.86682
    mesh = solid(piece, "Cap", "Head", (0, -0.06, 1.805), (0.256, 0.288, 0.07)).data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(ridge_x, 0, 0),
                           plane_no=(1, 0, 0))
    for v in bm.verts:
        if abs(v.co.x - ridge_x) < 1e-4 and v.co.z > 1.8:
            v.co.z = ridge_z
    bm.to_mesh(mesh)
    bm.free()


# ---------------------------------------------------------------- pieces

if SIDE == "allied":
    jacket("jacket_erdl", 0.008, 0.785, collar_grow=1.4)
    jacket("jacket_dpm", 0.012, 0.77, flare=1.07, collar_grow=1.45)
    shell("jacket_dpm", "Pelvis", 0.017, zmin=0.755, grow=1.04)
    solid("jacket_dpm", "Hood", "Chest", (0, -0.19, 1.41), (0.22, 0.06, 0.08), rot=(math.radians(-20), 0, 0))
    shell("jacket_og107", "Chest", 0.008)
    shell("jacket_og107", "Abdomen", 0.008)
    rolled_sleeves("jacket_og107", 0.008)
    shell("jacket_og107", "Neck", 0.006, zmin=1.425, zmax=1.463, grow=1.4)
    for name, t in (("jacket_erdl", 0.008), ("jacket_dpm", 0.012), ("jacket_og107", 0.008)):
        armband(name, t)
    shell("head_boonie", "Head", 0.012, zmin=1.738, rise=1.832, grow=1.05)
    solid("head_boonie", "Brim", "Head", (0, -0.06, 1.744), (0.315, 0.335, 0.012))
    beret("head_beret")
else:
    jacket("jacket_klmk", 0.012, 0.80, flare=1.04, collar_grow=1.4)
    shell("jacket_klmk", "Pelvis", 0.014)
    both("jacket_klmk", "Leg_Upper", 0.011)
    both("jacket_klmk", "Leg_Lower", 0.011, zmin=0.248)  # ends at the boot top
    solid("jacket_klmk", "Hood", "Chest", (0, -0.195, 1.39), (0.25, 0.075, 0.13), rot=(math.radians(-12), 0, 0), taper=0.7)
    jacket("jacket_strichtarn", 0.008, 0.785)
    shell("jacket_telnyashka", "Chest", 0.006)
    shell("jacket_telnyashka", "Abdomen", 0.006)
    rolled_sleeves("jacket_telnyashka", 0.006)
    for name, t in (("jacket_klmk", 0.012), ("jacket_strichtarn", 0.008), ("jacket_telnyashka", 0.006)):
        armband(name, t)
    beret("head_beret")

layout()

# ---------------------------------------------------------------- painting


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


def stripes(piece, color, period=4, thick=2):
    for (p, _, _), (rx, ry, w, h) in rects.items():
        if p != piece:
            continue
        for row in range(h + 2):
            if row % period < thick:
                pixels[ry - 1 + row, rx - 1:rx + w + 1] = rgb(color)


def rain(piece, color, spacing=3):
    """Broken vertical strokes (the East German rain pattern)."""
    for (p, _, _), (rx, ry, w, h) in rects.items():
        if p != piece:
            continue
        for column in range(-1, w + 1, spacing):
            row = -1 - rng.integers(0, 4)
            while row < h + 1:
                length = rng.integers(3, 7)
                pixels[ry + max(row, -1):ry + min(row + length, h + 1), rx + column] = rgb(color)
                row += length + rng.integers(2, 5)


def solid_color(piece, key, color):
    mark(piece, key, "front back side top", 0, 0, 1, 1, color)


def front_details(piece, line, flap, buttoned=True):
    for key in ("Chest", "Abdomen"):
        mark(piece, key, "front", 0, 0, 0.05, 1, line)
    pocket(piece, "Chest", 0.32, 0.22, 0.84, 0.62, line, flap)
    pocket(piece, "Abdomen", 0.32, 0.24, 0.86, 0.62, line, flap)
    cuffs(piece, line)
    if buttoned:
        buttons(piece, line)


if SIDE == "allied":
    # ERDL leaf-pattern jungle jacket
    paint("jacket_erdl", "5a6b3c")
    camo("jacket_erdl", [("7f8a4e", 0.28, 5, 3), ("4d3b28", 0.17, 4, 3), ("20251a", 0.14, 3, 2)])
    front_details("jacket_erdl", "262c1c", "56653a")
    solid_color("jacket_erdl", "Neck", "4a5832")
    # British DPM 1968 smock: brush strokes
    paint("jacket_dpm", "8b7b54")
    camo("jacket_dpm", [("506038", 0.30, 8, 3), ("6a432c", 0.17, 7, 2), ("27231a", 0.09, 6, 2)])
    for key in ("Chest", "Abdomen", "Pelvis"):
        mark("jacket_dpm", key, "front", 0, 0, 0.07, 1, "3a3524")
    pocket("jacket_dpm", "Chest", 0.34, 0.20, 0.86, 0.58, "3a3524", "7b6d4a")
    pocket("jacket_dpm", "Abdomen", 0.30, 0.08, 0.90, 0.52, "3a3524", "6e6444")
    mark("jacket_dpm", "Pelvis", "front back side", 0, 0, 1, 0.14, "3a3524")
    cuffs("jacket_dpm", "3a3524")
    solid_color("jacket_dpm", "Neck", "74663f")
    # OG-107 shirt, sleeves rolled, olive T-shirt at the neck
    paint("jacket_og107", "5b6240", noise=0.02)
    for key in ("Chest", "Abdomen"):
        mark("jacket_og107", key, "front", 0, 0, 0.05, 1, "40462c")
    buttons("jacket_og107", "33381f", keys=("Abdomen",))
    for v in (0.2, 0.5):
        mark("jacket_og107", "Chest", "front", 0.07, v, 0.13, v + 0.05, "33381f")
    pocket("jacket_og107", "Chest", 0.30, 0.24, 0.84, 0.62, "40462c", "666e48")
    for i in range(4):  # the open collar's V over the T-shirt
        mark("jacket_og107", "Chest", "front", 0.0, 0.70 + i * 0.08, 0.06 + i * 0.08, 0.80 + i * 0.08, "8a7f58")
        mark("jacket_og107", "Chest", "front", 0.06 + i * 0.08, 0.70 + i * 0.08, 0.13 + i * 0.08, 0.82 + i * 0.08, "40462c")
    mark("jacket_og107", "Abdomen", "front back side", 0, 0, 1, 0.12, "3a3524")   # belt
    mark("jacket_og107", "Abdomen", "front", 0, 0.01, 0.16, 0.11, "9a8a5a")
    solid_color("jacket_og107", "Roll", "70784f")
    mark("jacket_og107", "Roll", "front back side", 0, 0.45, 1, 0.55, "40462c")
    solid_color("jacket_og107", "Neck", "666e48")
    # headwear
    paint("head_boonie", "5a6b3c")
    camo("head_boonie", [("7f8a4e", 0.25, 4, 2), ("4d3b28", 0.18, 3, 2), ("20251a", 0.12, 3, 2)])
    mark("head_boonie", "Head", "front back side", 0, 0.10, 1, 0.30, "3d4529")
    paint("head_beret", "3a4a34", noise=0.03)
    mark("head_beret", "Cap", "front back side", 0, 0, 1, 0.3, "151a14")
else:
    # KLMK camouflage coverall: pale jagged "sunray" patches
    paint("jacket_klmk", "6c7350")
    camo("jacket_klmk", [("c4bd8d", 0.20, 4, 2), ("aeb07a", 0.12, 2, 2), ("586040", 0.12, 3, 3)])
    for key in ("Chest", "Abdomen", "Pelvis"):
        mark("jacket_klmk", key, "front", 0, 0, 0.05, 1, "3f442e")
    pocket("jacket_klmk", "Chest", 0.34, 0.20, 0.84, 0.58, "4a5036", "7a8058")
    pocket("jacket_klmk", "Leg_Upper", 0.18, 0.18, 0.84, 0.52, "4a5036", "7a8058")
    mark("jacket_klmk", "Abdomen", "front back side", 0, 0.20, 1, 0.31, "4a5036")  # drawstring waist
    mark("jacket_klmk", "Leg_Lower", "front back side", 0, 0, 1, 0.08, "3f442e")
    cuffs("jacket_klmk", "3f442e")
    solid_color("jacket_klmk", "Neck", "626947")
    # East German rain pattern
    paint("jacket_strichtarn", "77796a")
    rain("jacket_strichtarn", "4a4136")
    front_details("jacket_strichtarn", "37342c", "6c6e60")
    solid_color("jacket_strichtarn", "Neck", "626456")
    # telnyashka: striped undershirt, sleeves rolled
    paint("jacket_telnyashka", "d8d8d0", noise=0.015)
    stripes("jacket_telnyashka", "3d5c8e", period=5, thick=2)
    mark("jacket_telnyashka", "Chest", "front back", 0, 0.93, 0.5, 1, "33507e")   # neckline binding
    solid_color("jacket_telnyashka", "Roll", "c9cac4")
    for v in (0.12, 0.56):
        mark("jacket_telnyashka", "Roll", "front back side", 0, v, 1, v + 0.3, "3d5c8e")
    mark("jacket_telnyashka", "Abdomen", "front back side", 0, 0, 1, 0.12, "4a3526")  # belt
    mark("jacket_telnyashka", "Abdomen", "front", 0, 0.01, 0.18, 0.11, "b59a4e")
    # blue beret
    paint("head_beret", "4d86b8", noise=0.03)
    mark("head_beret", "Cap", "front back side", 0, 0, 1, 0.3, "1d2630")

for name in [p for p in pieces if p.startswith("jacket_")]:
    solid_color(name, "Armband", ARMBAND)

# ---------------------------------------------------------------- the side's shared pieces, from its player outfit

PREFIX = {"allied": "Allied_", "soviet": "Soviet_"}[SIDE]
SHARED = {"jacket": ("Jacket", "Collar", "Sleeve"), "trousers": ("Trouser",), "boots": ("Boot",)}
if SIDE == "allied":
    SHARED["hands"] = ("Glove",)


def borrow():
    """Copies the player outfit's parts onto this rig, keeping their own texture; only for the lineup."""
    path = os.path.join(OUT, f"outfit_{SIDE}_solidify.blend")
    mine = set(bpy.data.objects)
    with bpy.data.libraries.load(path) as (source, target):
        target.objects = [n for n in source.objects if n.startswith(PREFIX)]
    temp = collection("temp")
    for o in target.objects:
        temp.objects.link(o)
    other = target.objects[0].parent
    temp.objects.link(other)
    other.animation_data_clear()
    for pb in other.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    shared = {}
    for slot, kinds in SHARED.items():
        name = f"{SIDE}_{slot}"
        coll = collection("Piece_shared_" + name)
        for o in [o for o in target.objects if o.name.startswith(tuple(PREFIX + k for k in kinds))]:
            mesh = o.data.copy()
            mesh.transform(o.matrix_world)
            mesh.shade_flat()
            n = bpy.data.objects.new(f"{name}__{o.name}", mesh)
            coll.objects.link(n)
            n.parent = rig
            n.parent_type = "BONE"
            n.parent_bone = o.parent_bone
            bpy.context.view_layer.update()
            n.matrix_world = Matrix.Identity(4)
            for m in o.modifiers:
                if m.type == "MIRROR":
                    mirror = n.modifiers.new("Mirror", "MIRROR")
                    mirror.use_clip = True
                    mirror.merge_threshold = 0.0005
                elif m.type == "SOLIDIFY":
                    solidify(n, m.thickness)
            shared.setdefault(name, []).append(n)
        assert shared.get(name), f"no parts for {name}"
    copies = {o for objects in shared.values() for o in objects}
    for o in list(bpy.data.objects):  # appending also drags in the source rig and what it hangs from
        if o not in mine and o not in copies:
            bpy.data.objects.remove(o)
    bpy.data.collections.remove(temp)
    return shared


shared = borrow()
trousers, boots = f"{SIDE}_trousers", f"{SIDE}_boots"
if SIDE == "allied":
    legs = [trousers, boots, "allied_hands"]
    LINEUP = {
        "rifleman": ["allied_jacket"] + legs,
        "flanker": ["jacket_erdl", "head_boonie"] + legs,
        "marksman": ["jacket_dpm", "head_beret"] + legs,
        "assault": ["jacket_og107"] + legs,
    }
else:
    legs = [trousers, boots]
    LINEUP = {
        "rifleman": ["soviet_jacket"] + legs,
        "flanker": ["jacket_klmk", boots],
        "marksman": ["jacket_strichtarn"] + legs,
        "assault": ["jacket_telnyashka", "head_beret"] + legs,
    }

finish(f"outfits_pmc_{SIDE}", f"{SIDE}_pmc_detail", LINEUP, shared)
