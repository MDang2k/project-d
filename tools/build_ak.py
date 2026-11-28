"""Builds the AK (ak_type) and its furniture parts, traced from the designer's side-view reference.

Run from the repo root:  blender -b --python tools/build_ak.py
Writes assets/source/{weapons,parts}/<id>.blend, assets/models/{weapons,parts}/<id>.glb and preview renders.
Rebuilding overwrites hand edits in the .blend files.

Outline coordinates are pixels of the reference side view (muzzle to the right, 1233 px wide);
P() turns them into Blender space (+Y forward, Z up, origin by the pistol grip as on the FAL).
"""
import math
import os

import bmesh
import bpy
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

ROOT = os.getcwd()
PPM = 1295.0  # reference pixels per metre (rifle length 870 mm)
MAG_ROUND = os.path.join(ROOT, "assets", "source", "parts", "ak_mag_round.blend")

# palette cells (column, row): row 0 base, 1 dark, 2 light
METAL, METAL_D, METAL_L = (5, 0), (5, 1), (5, 2)
STEEL, STEEL_D, STEEL_L = (6, 0), (6, 1), (6, 2)
WOOD, WOOD_D, WOOD_L = (4, 0), (4, 1), (4, 2)
BLACK = (15, 0)


def P(px, py):
    return (px - 400) / PPM - 0.023, (205 - py) / PPM - 0.025


def V(x, px, py):
    y, z = P(px, py)
    return Vector((x, y, z))


def obj(name, bm, color, group="body", bevel=0.0, solidify=0.0):
    """Finishes a bmesh into an object: normals, optional chamfer, palette UVs, sharp edges."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if solidify:
        bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=solidify)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bevel:
        edges = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(35)]
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=1, profile=0.5, affect="EDGES", clamp_overlap=True)
    uv = bm.loops.layers.uv.new("UVMap")
    u, v = (color[0] * 16 + 8) / 256, 1 - (color[1] * 16 + 8) / 256
    for f in bm.faces:
        f.smooth = True
        for loop in f.loops:
            loop[uv].uv = (u, v)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.set_sharp_from_angle(angle=math.radians(30))
    mesh.materials.append(material())
    o = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(o)
    o["group"] = group
    return o


def material():
    mat = bpy.data.materials.get("palette")
    if mat:
        return mat
    mat = bpy.data.materials.new("palette")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(ROOT, "assets", "textures", "palette.png"))
    tex.interpolation = "Closest"
    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.7
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nodes.active = tex
    return mat


def skin(bm, rings, closed=True, caps=True):
    """Quads between successive rings of points; end caps on closed rings."""
    vr = [[bm.verts.new(p) for p in ring] for ring in rings]
    for a, b in zip(vr, vr[1:]):
        n = len(a)
        for i in range(n if closed else n - 1):
            bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
    if caps and closed:
        bm.faces.new(vr[0])
        bm.faces.new(vr[-1])


def prism(name, pts, x0, x1, color, group="body", bevel=0.0):
    """A side-view outline extruded across the gun from x0 to x1 (x+ is the right side)."""
    bm = bmesh.new()
    skin(bm, [[V(x0, *p) for p in pts], [V(x1, *p) for p in pts]])
    return obj(name, bm, color, group, bevel)


def loft(name, sections, color, group="body", bevel=0.0):
    """Chamfered cross-sections: each is (outline point A, outline point B, half width)."""
    shape = [(0, -.55), (0, .55), (.2, 1), (.8, 1), (1, .55), (1, -.55), (.8, -1), (.2, -1)]
    rings = []
    for a, b, hw in sections:
        va, vb = V(0, *a), V(0, *b)
        rings.append([va.lerp(vb, t) + Vector((s * hw, 0, 0)) for t, s in shape])
    bm = bmesh.new()
    skin(bm, rings)
    return obj(name, bm, color, group, bevel)


def tube(name, px0, px1, py, r, color, group="body", sides=10, x=0.0):
    """A cylinder along the barrel axis."""
    rings = []
    for px in (px0, px1):
        c = V(x, px, py)
        rings.append([c + Vector((math.cos(a) * r, 0, math.sin(a) * r))
                      for a in (2 * math.pi * (i + .5) / sides for i in range(sides))])
    bm = bmesh.new()
    skin(bm, rings)
    return obj(name, bm, color, group)


def pin(name, px, py, r, x0, x1, color, group="body", sides=8):
    """A cylinder across the gun (pins, rivets, screws)."""
    rings = []
    for x in (x0, x1):
        c = V(x, px, py)
        rings.append([c + Vector((0, math.cos(a) * r, math.sin(a) * r))
                      for a in (2 * math.pi * i / sides for i in range(sides))])
    bm = bmesh.new()
    skin(bm, rings)
    return obj(name, bm, color, group)


def band(name, line, width, x0, x1, color, group="body"):
    """A flat strip following a side-view polyline (trigger guard, lever, sling loop)."""
    pts = [Vector(p) for p in line]
    left, right = [], []
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        n = Vector((-d.y, d.x)) * width / 2
        left.append(p + n)
        right.append(p - n)
    bm = bmesh.new()
    rings = [[V(x0, *a), V(x1, *a), V(x1, *b), V(x0, *b)] for a, b in zip(left, right)]
    skin(bm, rings)
    return obj(name, bm, color, group)


def join(name, objs, origin=None):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    group = objs[0]["group"]
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = objs[0]
    o.name = o.data.name = name
    o["group"] = group
    o.data.set_sharp_from_angle(angle=math.radians(30))
    if origin is not None:
        o.data.transform(__import__("mathutils").Matrix.Translation(-origin))
        o.location = origin
    return o


# ---------------------------------------------------------------- the gun

def receiver():
    w, t = 0.017, 0.0022
    out = []
    # stamped-and-milled box, open on top so the carrier shows when the cover is off
    out.append(prism("wall_l", [(355, 96), (700, 88), (700, 148), (362, 150), (355, 140)], -w, -w + t, METAL))
    out.append(prism("wall_r", [(355, 96), (545, 96), (545, 100), (662, 98), (662, 88), (700, 88),
                                (700, 148), (362, 150), (355, 140)], w - t, w, METAL))
    out.append(prism("floor", [(362, 138), (700, 138), (700, 148), (362, 150)], -w + t, w - t, METAL_D))
    out.append(prism("rear_block", [(352, 96), (378, 96), (378, 150), (362, 150), (352, 140)], -w - .0004, w + .0004,
                     METAL, bevel=0.0012))
    out.append(prism("tang", [(325, 100), (356, 97), (362, 148), (334, 152)], -.0125, .0125, METAL_L, bevel=0.001))
    out.append(prism("trunnion", [(662, 88), (700, 87), (700, 146), (662, 146)], -w - .0005, w + .0005, METAL,
                     bevel=0.001))
    out.append(prism("front_collar", [(690, 84), (699, 84), (699, 146), (690, 146)], -w - .0012, w + .0012, METAL_L,
                     bevel=0.001))
    # lightening cuts of the milled receiver, both sides
    for s in (-1, 1):
        back = 10.9 if s > 0 else 0  # designer edit: the right-hand cut sits further back
        x0, x1 = sorted((s * w, s * (w + .0007)))
        out.append(prism("cut_frame", [(px - back, py) for px, py in (
            (574, 116), (666, 116), (668, 119), (668, 135), (665, 138), (574, 138), (571, 135), (571, 119))],
            x0, x1, METAL_L))
        x0, x1 = sorted((s * w, s * (w + .0010)))
        out.append(prism("cut", [(px - back, py) for px, py in ((578, 120), (662, 120), (662, 134), (578, 134))],
                         x0, x1, METAL_D))
    # magazine well lips and catch
    out.append(prism("well_front", [(630, 146), (640, 146), (640, 153), (630, 153)], -.0155, .0155, METAL_D))
    out.append(prism("mag_catch", [(512, 148), (532, 148), (530, 176), (522, 190), (517, 189), (520, 174), (512, 172)],
                     -.0045, .0045, METAL_L, bevel=0.0008))
    out.append(pin("catch_pin", 521, 160, .0022, -.006, .006, STEEL_D))
    # trigger group
    out.append(band("guard", [(447, 150), (447, 176), (452, 185), (464, 188), (506, 188), (515, 183)], 4.0,
                    -.006, .006, METAL_L))
    out.append(band("trigger", [(477, 150), (475, 163), (466, 170), (461, 178), (464, 183)], 6.0,
                    -.003, .003, STEEL))
    # pins and rivets
    for px, py, r in ((470, 128, .0030), (497, 130, .0034), (463, 141, .0030), (500, 141, .0024), (380, 117, .0026), (655, 105, .0024),
                      (655, 142, .0024)):
        out.append(pin("rivet", px, py, r, -w - .0012, w + .0012, STEEL_D))
    return join("Receiver", out)


def plate(name, outline, x0, x1, color, bevel=0.0006):
    return prism(name, outline, x0, x1, color, bevel=bevel)


def selector():
    """The big stamped lever: domed pivot boss, a plate with a hump that tapers to the thumb end, raised rim."""
    w = 0.017
    outline = [(423.5, 103.5), (433, 100), (442.5, 103), (453, 108), (462, 106), (470, 102), (481, 103), (498, 108),
               (526, 119), (549, 128), (556, 131), (556, 143), (549, 148), (535, 146), (509, 141), (479, 134),
               (451, 126), (434, 126.5), (423.5, 122.5), (419, 115), (419.5, 108)]
    # the pressed centre panel, a rim's width inside the outline
    panel = [(440, 109), (456, 113), (465, 110), (472, 106), (481, 107), (497, 112), (523, 122), (536, 128),
             (532, 139), (510, 136), (480, 129), (452, 122), (440, 120)]
    out = [plate("sel_plate", outline, w, w + .0022, METAL_L),
           plate("sel_panel", panel, w + .0022, w + .0030, STEEL_D, bevel=0.0005),
           pin("sel_boss", 432, 113, 12.6 / PPM, w + .0022, w + .0038, METAL_L, sides=14),
           pin("sel_ring", 432, 113, 9.6 / PPM, w + .0038, w + .0046, STEEL_D, sides=14),
           pin("sel_dome", 432, 113, 7.2 / PPM, w + .0046, w + .0062, STEEL, sides=12),
           pin("sel_dome_tip", 432, 113, 3.8 / PPM, w + .0062, w + .0069, STEEL_L, sides=10),
           # end: riveted tab, and the two lips bent outwards for the thumb
           plate("sel_tab", [(540, 132), (554, 135), (551, 146), (537, 143)], w + .0022, w + .0036, STEEL),
           pin("sel_tab_rivet", 546, 139, 2.0 / PPM, w + .0036, w + .0040, BLACK),
           band("sel_lip_top", [(531, 120), (557, 130)], 3.2, w + .0010, w + .0068, STEEL_L),
           band("sel_lip_low", [(526, 145), (550, 149)], 2.6, w + .0010, w + .0060, STEEL)]
    return join("Selector", out)


def dust_cover():
    """Lofted stamped cover: a hump between two shoulders, ejection cut on the right, and a sloped rear end with
    the square hole the spring guide goes through."""
    sections = [  # px, top py, half width, right-side bottom py
        (396.6, 58, .0158, 97), (538, 58, .0158, 97), (552, 58, .0158, 80), (662, 58, .0158, 80),
    ]
    # cross-section from the designer's end-on reference: straight sides, curved shoulders, a rounded hump
    left = [(-1, 0), (-1, .45), (-.88, .54), (-.66, .60), (-.55, .69), (-.53, .81), (-.42, .92), (-.23, .985)]
    profile = left + [(0, 1)] + [(-fx, t) for fx, t in reversed(left)]
    top_py = 58.5  # top of the rear end (designer edit: raised a touch)

    def slope(py):
        """The rear end is one flat slope, from the bottom at px 359 up to the top at px 384.6."""
        return 359 + (97 - py) * 25.6 / (97 - top_py)

    rear = []  # (x, py) of the end ring, on that slope
    for fx, t in profile:
        rear.append((fx * .0158, 97 + (top_py - 97) * t))
    rings = [[V(x, slope(py), py) for x, py in rear]]
    for px, top, hw, right_bottom in sections:
        ring = []
        for fx, t in profile:
            py = 97 + (top - 97) * t
            if fx > 0:
                py = min(py, right_bottom)
            ring.append(V(fx * hw, px, py))
        rings.append(ring)
    bm = bmesh.new()
    skin(bm, rings, closed=False, caps=False)
    shell = obj("cover_shell", bm, METAL, solidify=0.0011)

    # the end plate: the ring's outline with a square hole through it
    hole = [(-.0053, 75), (.0053, 75), (.0053, 83.5), (-.0053, 83.5)]
    flat = [[Vector((x * PPM, py, 0)) for x, py in loop] for loop in (rear, hole)]
    triangles = tessellate_polygon(flat)
    points = rear + hole
    bm = bmesh.new()
    front = [bm.verts.new(V(x, slope(py), py)) for x, py in points]
    back = [bm.verts.new(V(x, slope(py) + 1.5, py)) for x, py in points]
    for tri in triangles:
        bm.faces.new([front[k] for k in tri])
        bm.faces.new([back[k] for k in tri])
    for start, count in ((0, len(rear)), (len(rear), len(hole))):
        for k in range(count):
            a, b = start + k, start + (k + 1) % count
            bm.faces.new((front[a], front[b], back[b], back[a]))
    end = obj("cover_end", bm, METAL)
    return join("DustCover", [shell, end])


def bolt_carrier():
    """Carrier, bolt and the long gas piston running up the gas tube."""
    out = [prism("carrier_top", [(545, 66), (652, 66), (660, 72), (660, 86), (545, 86)], -.0073, .0073, STEEL,
                 bevel=0.0018),
           prism("carrier_low", [(520, 84), (640, 84), (640, 100), (532, 100), (520, 94)], -.0125, .0125, STEEL_D,
                 bevel=0.0012),
           prism("carrier_rail", [(548, 99), (548, 90), (553, 88), (594, 88), (601, 93), (643, 93), (643, 99)],
                 .0125, .0140, STEEL_L, bevel=0.0004),
           tube("bolt", 640, 676, 93, .0085, STEEL_L, sides=10),
           tube("bolt_lug", 664, 672, 93, .0105, STEEL, sides=8),
           tube("piston_base", 660, 756, 71, .0074, STEEL),
           tube("piston_rod", 756, 934, 71, .0048, STEEL_L),
           tube("piston_head", 934, 957, 71, .0070, STEEL)]
    for px in (936, 944, 952):
        out.append(tube("piston_ring", px, px + 4, 71, .0079, STEEL_L))
    return join("BoltCarrier", out)


def recoil_spring():
    """Recoil spring on its guide rod, with the rear bracket whose button locks the dust cover."""
    axis_py, start, end = 71, 402, 552
    out = [tube("rs_rod", start, 560, axis_py, .0036, STEEL, sides=8)]
    # the coil: a triangular wire swept round the rod
    turns, steps = 19, 8
    coil_r, wire_r = .0048, .0010
    y0, z = P(start, axis_py)
    y1, _ = P(end, axis_py)
    rings = []
    for i in range(turns * steps + 1):
        a = 2 * math.pi * i / steps
        radial = Vector((math.cos(a), 0, math.sin(a)))
        c = Vector((0, y0 + (y1 - y0) * i / (turns * steps), z)) + radial * coil_r
        rings.append([c + radial * wire_r, c + Vector((0, wire_r, 0)) - radial * wire_r * .5,
                      c - Vector((0, wire_r, 0)) - radial * wire_r * .5])
    bm = bmesh.new()
    skin(bm, rings)
    out.append(obj("rs_coil", bm, STEEL_L))
    # bracket: a boss round the rod end, a slanted arm and a foot in the receiver rails
    out.append(prism("rs_bracket", [(405, 65), (395, 65), (383, 76), (380, 79), (380, 88), (372, 97), (389, 97),
                                    (392, 87), (399, 77), (405, 77)], -.0072, .0072, STEEL, bevel=0.0008))
    out.append(pin("rs_eye", 398, 71, .0026, -.00728, .00728, BLACK))
    # the serrated button that pokes through the square hole of the dust cover
    out.append(prism("rs_button", [(390, 74.5), (362, 77.5), (360, 79), (362, 82.8), (390, 81.3)], -.0032, .0032,
                     STEEL_L, bevel=0.0004))
    for px in (363.5, 368.5):
        py = 77.5 - (px - 362) * 3 / 28
        out.append(prism("rs_tooth", [(px, py), (px + 1.5, py - 2.4), (px + 3.5, py - .4)], -.0040, .0040, STEEL_L))
    return join("RecoilSpring", out)


def charging_handle():
    """A flat paddle at the front of the ejection port: short neck, then a rounded tongue swept forward."""
    base = V(0.0165, 641, 90)
    # (x out from the gun, px, py, half thickness, half height); the cross-section is square to the path
    path = [(.0095, 641, 90, 5.5, 5.5), (.0195, 643, 90, 3.6, 4.8), (.0265, 647, 90.5, 3.2, 7.6),
            (.0355, 656, 91.5, 3.0, 8.6), (.0420, 662, 92.5, 2.7, 7.6), (.0450, 665, 93, 1.8, 4.6)]
    shape = [(1, .45), (.55, 1), (-.55, 1), (-1, .45), (-1, -.45), (-.55, -1), (.55, -1), (1, -.45)]
    centres = [V(x, px, py) for x, px, py, _, _ in path]
    rings = []
    for i, (_, _, _, t, hz) in enumerate(path):
        d = centres[min(i + 1, len(path) - 1)] - centres[max(i - 1, 0)]
        n = Vector((-d.y, d.x, 0)).normalized()
        rings.append([centres[i] + n * (a * t / PPM) + Vector((0, 0, b * hz / PPM)) for a, b in shape])
    bm = bmesh.new()
    skin(bm, rings)
    return join("ChargingHandle", [obj("handle", bm, STEEL_L, bevel=0.0004)], origin=base)


def barrel_group():
    out = [tube("barrel", 690, 1156, 102, .0077, METAL),
           tube("barrel_seat", 893, 906, 102, .0112, METAL_L),
           tube("muzzle_nut", 1155, 1170, 102, .0098, METAL_L),
           tube("muzzle_groove", 1161, 1164, 102, .0102, METAL_D),
           tube("bore", 1170, 1170.6, 102, .0040, BLACK),
           tube("cleaning_rod", 893, 1142, 120, .0026, STEEL_D, sides=6),
           tube("rod_head", 1136, 1145, 120, .0038, STEEL, sides=6)]
    # gas tube with its vent holes, and the handguard cap
    out.append(tube("gas_tube", 884, 954, 71, .0098, METAL_L))
    for px in (905, 916, 927, 938):
        for s in (-1, 1):
            out.append(pin("vent", px, 66, .0014, s * .0070, s * .0101, BLACK, sides=6))
    out.append(tube("gas_inner", 757, 884, 71, .0092, METAL_D))
    # gas block
    out.append(prism("gas_block_slope", [(952, 59), (981, 59), (1008, 89), (1014, 94), (976, 94), (952, 82)],
                     -.0085, .0085, METAL, bevel=0.0012))
    out.append(prism("gas_block", [(975, 91), (1030, 91), (1030, 113), (975, 113)], -.0115, .0115, METAL,
                     bevel=0.0018))
    out.append(prism("rod_guide", [(984, 112), (994, 112), (994, 124), (984, 124)], -.006, .006, METAL_L))
    out.append(pin("gas_pin", 1003, 103, .0025, -.0125, .0125, STEEL_D))
    # lower handguard retainer
    out.append(prism("retainer", [(884, 84), (894, 84), (894, 126), (884, 126)], -.0205, .0205, METAL_L,
                     bevel=0.002))
    # front sight: base, open tower, protective ears and post
    out.append(prism("fs_base", [(1113, 87), (1156, 87), (1156, 113), (1146, 124), (1120, 124), (1113, 113)],
                     -.0115, .0115, METAL, bevel=0.0018))
    out.append(prism("fs_leg_rear", [(1115, 89), (1128, 54), (1137, 54), (1126, 89)], -.0065, .0065, METAL))
    out.append(prism("fs_leg_front", [(1145, 89), (1144, 54), (1152, 54), (1156, 89)], -.0065, .0065, METAL))
    out.append(prism("fs_bridge", [(1126, 54), (1153, 54), (1153, 65), (1124, 65)], -.0070, .0070, METAL))
    out.append(prism("fs_web", [(1122, 78), (1150, 78), (1152, 89), (1118, 89)], -.0065, .0065, METAL))
    for s in (-1, 1):
        x0, x1 = sorted((s * .0042, s * .0072))
        out.append(prism("fs_ear", [(1129, 55), (1131, 40), (1139, 36), (1150, 38), (1152, 55)], x0, x1, METAL_L))
    out.append(prism("fs_post", [(1140, 55), (1140, 42), (1144, 42), (1144, 55)], -.0011, .0011, STEEL_L))
    return join("Barrel", out)


def rear_sight():
    out = [prism("rs_block", [(662, 88), (662, 62), (672, 56), (736, 56), (748, 64), (757, 68), (757, 88), (700, 91)],
                 -.0160, .0160, METAL, bevel=0.0022),
           prism("rs_collar", [(748, 54), (757, 54), (757, 90), (748, 90)], -.0188, .0188, METAL_L, bevel=0.0025),
           # tangent leaf, slider, hinge
           band("rs_leaf", [(668, 49), (735, 49)], 4.0, -.0050, .0050, STEEL),
           prism("rs_slider", [(702, 44), (712, 44), (712, 53), (702, 53)], -.0085, .0085, STEEL_D),
           pin("rs_hinge", 737, 52, .0052, -.0115, .0115, METAL_L),
           prism("rs_foot", [(670, 50), (682, 50), (682, 58), (670, 58)], -.0060, .0060, METAL)]
    for s in (-1, 1):  # notch
        x0, x1 = sorted((s * .0012, s * .0055))
        out.append(prism("rs_notch", [(658, 41.5), (668, 41.5), (668, 51), (658, 51)], x0, x1, STEEL_D))
    return join("RearSightBlock", out)


def pistol_grip():
    out = [prism("grip_plate", [(368, 148), (449, 148), (449, 160), (368, 160)], -.0140, .0140, METAL_L, bevel=0.001),
           loft("grip_wood", [((370, 159), (444, 159), .0132), ((373, 176), (437, 180), .0140),
                              ((371, 195), (427, 203), .0148), ((362, 222), (415, 232), .0150),
                              ((353, 248), (409, 252), .0152), ((352, 262), (408, 266), .0145),
                              ((362, 270), (400, 274), .0120)], WOOD, bevel=0.0008),
           pin("grip_screw", 385, 268, .0040, -.003, .003, STEEL_D)]
    return join("PistolGrip", out)


def handguard():
    """Lower and upper wood. Between them the gas tube and the two vent gaps show."""
    out = [loft("hg_lower", [((699, 90), (699, 147), .0195), ((716, 90), (716, 149), .0230),
                             ((734, 90), (734, 142), .0232), ((752, 89), (752, 135), .0214),
                             ((800, 89), (800, 132), .0208), ((884, 88), (884, 128), .0196)],
                WOOD_L, "handguard", bevel=0.001),
           loft("hg_upper", [((757, 50), (757, 82), .0172), ((772, 48), (772, 82), .0186),
                             ((850, 50), (850, 82), .0178), ((884, 52), (884, 82), .0165)],
                WOOD, "handguard", bevel=0.001),
           prism("hg_upper_ferrule", [(884, 50), (893, 50), (893, 84), (884, 84)], -.0168, .0168, METAL_L,
                 "handguard", bevel=0.003)]
    # the three wooden bridges that leave the two vent gaps of the reference
    for a, b in ((757, 772), (806, 824), (866, 884)):
        out.append(prism("hg_bridge", [(a, 81), (b, 81), (b, 90), (a, 90)], -.0158, .0158, WOOD_D, "handguard"))
    return join("HandguardWood", out)


def stock():
    out = [loft("st_wood", [((331, 100), (331, 149), .0135), ((300, 101), (300, 156), .0140),
                            ((250, 104), (250, 171), .0150), ((231, 94), (231, 178), .0165),
                            ((200, 96), (200, 188), .0185), ((120, 100), (126, 210), .0208),
                            ((50, 105), (62, 227), .0215)], WOOD_L, "stock", bevel=0.0012),
           loft("st_butt", [((44, 103), (56, 229), .0215), ((50.5, 104), (62.5, 228), .0222)], METAL, "stock",
                bevel=0.001),
           band("st_swivel_plate", [(121, 211), (140, 206)], 4.0, -.007, .007, METAL_L, "stock"),
           band("st_loop", [(126, 209), (123, 220), (125, 227), (134, 229), (138, 222), (136, 208)], 3.4,
                -.0022, .0022, METAL_L, "stock")]
    for py in (122, 205):
        px = 44 + (py - 103) * 12 / 126
        out.append(tube("st_screw", px - 1.6, px + 1, py, .0048, STEEL, "stock", sides=8))
    out.append(prism("st_tang_screw", [(305, 98), (318, 97), (318, 101), (305, 102)], -.004, .004, STEEL_D, "stock"))
    return join("StockWood", out)


# magazine outline from the reference: (back edge point, front edge point) down the curve
MAG = [((541, 140), (615, 140)), ((541, 150), (615, 150)), ((545, 186), (620, 185)), ((554, 225), (633, 220)),
       ((570, 264), (655, 253)), ((595, 303), (681, 279)), ((632, 340), (707, 295)), ((682, 372), (733, 308))]
MAG_HW = .0132


def mag_point(i, t):
    """A point across the magazine at section i, t from the back edge (0) to the front edge (1)."""
    return tuple(Vector(MAG[i][0]).lerp(Vector(MAG[i][1]), t))


def magazine(top_round_from):
    out = [loft("mag_body", [(a, b, MAG_HW) for a, b in MAG], METAL, "mag", bevel=0.0007)]
    sides = ((MAG_HW, MAG_HW + .0013), (-MAG_HW - .0013, -MAG_HW))
    for x0, x1 in sides:
        # four long pressed ribs, then the short cross ribs above the floor plate
        for t in (.22, .39, .56, .73):
            out.append(band("mag_rib", [mag_point(i, t) for i in range(2, 7)], 5.0, x0, x1, METAL_L, "mag"))
        base_a, base_b = Vector(MAG[-1][0]), Vector(MAG[-1][1])
        up = Vector((-(base_b - base_a).y, (base_b - base_a).x)).normalized()
        if up.y > 0:
            up = -up
        for lift, t0, t1 in ((10, .06, .55), (10, .66, .93), (20, .06, .55), (20, .66, .93)):
            line = [tuple(base_a.lerp(base_b, t) + up * lift) for t in (t0, t1)]
            out.append(band("mag_cross", line, 4.5, x0, x1, METAL_L, "mag"))
    # spine seam, floor plate, front locking lug and rear catch notch
    out.append(band("mag_seam", [mag_point(i, .02) for i in range(1, 8)], 3.0, -.0045, .0045, METAL_L, "mag"))
    out.append(band("mag_floor", [(677, 375), (736, 305)], 7.0, -.0148, .0148, METAL_L, "mag"))
    out.append(prism("mag_lug", [(614, 150), (624, 152), (624, 160), (615, 163)], -.006, .006, METAL_L, "mag"))
    out.append(prism("mag_catch_lug", [(532, 156), (542, 156), (542, 170), (535, 168)], -.006, .006, METAL_L, "mag"))
    join("ak_mag", out)
    with bpy.data.libraries.load(top_round_from, link=False) as (_, wanted):
        wanted.objects = ["ak_mag_top_round"]
    top = wanted.objects[0]
    bpy.context.scene.collection.objects.link(top)
    top["group"] = "mag"
    top.data.materials.clear()
    top.data.materials.append(material())
    top.location = V(0, 578, 148)  # it sits at the lips, as it did on the old magazine's Attach


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    receiver(), selector(), dust_cover(), bolt_carrier(), recoil_spring(), charging_handle(), barrel_group()
    rear_sight()
    pistol_grip(), handguard(), stock()
    magazine(MAG_ROUND)


# ---------------------------------------------------------------- markers, files

def empty(name, pos, rot=(0, 0, 0), **props):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.025
    e.location = pos
    e.rotation_euler = rot
    for k, val in props.items():
        e[k] = val
    bpy.context.scene.collection.objects.link(e)
    return e


# GripRight: the FAL's marker, with the AK's grip rake. GripLeft: same hand pose as the FAL under the handguard.
GRIP_R_POS, GRIP_R_ROT = (0.0, -0.023, -0.025), (math.pi / 2, math.radians(23), math.pi / 2)
GRIP_L_ROT = (0.0, math.radians(-135), 0.0)
BORE_PY = 102
HANDGUARD_AT = (0.0, *P(699, BORE_PY))
STOCK_AT = (0.0, *P(352, 124))
GRIP_L_AT = (0.0, P(800, BORE_PY)[0], P(800, BORE_PY)[1] + 0.008)


def weapon_markers():
    empty("GripRight", GRIP_R_POS, GRIP_R_ROT)
    empty("GripLeft", GRIP_L_AT, GRIP_L_ROT, grip_pose="wrap")
    empty("Muzzle", V(0, 1170, BORE_PY))
    empty("MagWell", V(0, 578, 148))
    empty("OpticMount", V(-0.019, 520, 108))
    empty("BipodMount", V(0, 985, 124))
    empty("Handguard", HANDGUARD_AT)
    empty("Stock", STOCK_AT)
    empty("RearSight", V(0, 663, 42))
    empty("FrontSight", V(0, 1142, 42))
    empty("MagRelease", V(0, 521, 175))
    empty("EjectionPort", V(0.017, 600, 86))


def save(out_id, folder):
    src = os.path.join(ROOT, "assets", "source", folder, out_id + ".blend")
    bpy.ops.wm.save_as_mainfile(filepath=src, relative_remap=True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(ROOT, "assets", "models", folder, out_id + ".glb"),
                              export_format="GLB", export_apply=True, export_extras=True, export_cameras=False,
                              export_lights=False)
    bpy.context.view_layer.update()
    tris = 0
    for o in bpy.context.scene.collection.all_objects:
        if o.type == "MESH":
            o.data.calc_loop_triangles()
            tris += len(o.data.loop_triangles)
    print("SAVED", out_id, "tris", tris)


def export(group, out_id, folder, at, markers):
    build()
    for o in list(bpy.data.objects):
        if o["group"] != group:
            bpy.data.objects.remove(o)
    offset = Vector(at)
    for o in bpy.data.objects:
        if offset.length:
            if o.location.length:  # keeps its own origin (the charging handle)
                o.location -= offset
            else:
                o.data.transform(__import__("mathutils").Matrix.Translation(-offset))
    markers()
    save(out_id, folder)


def render(path, direction, scale, size, target):
    scene = bpy.context.scene
    cam = bpy.data.objects.get("cam") or bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    if cam.name not in scene.collection.objects:
        scene.collection.objects.link(cam)
    d = Vector(direction).normalized()
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = scale
    cam.location = Vector(target) + d * 3
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "WORLD"
    scene.view_settings.view_transform = "Standard"
    scene.world = scene.world or bpy.data.worlds.new("w")
    scene.world.color = (0.05, 0.05, 0.05)
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def previews():
    """The rifle assembled with its magazine: reference-matching side views, three-quarter, and field stripped."""
    build()
    weapon_markers()
    for o in list(bpy.data.objects):
        if o.get("group") == "mag":  # the exported magazine is imported instead
            bpy.data.objects.remove(o)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(ROOT, "assets", "models", "parts", "ak_mag.glb"))
    for o in set(bpy.data.objects) - before:
        if o.parent is None:
            o.location += V(0, 578, 148)
    base = os.path.join(ROOT, "assets", "source", "weapons", "ak_type_preview")
    # the side views frame exactly the reference picture (1233 x 428 px)
    mid_y, mid_z = P(616.5, 214)
    render(base + "_side.png", (1, 0, 0), 1233 / PPM, (1233, 428), (0, mid_y, mid_z))
    render(base + ".png", (1, -1.1, 0.55), 0.92, (1600, 900), (0, mid_y, mid_z))
    for name, lift in (("DustCover", 0.11), ("BoltCarrier", 0.055), ("ChargingHandle", 0.055),
                       ("RecoilSpring", 0.055)):
        bpy.data.objects[name].location.z += lift
    render(base + "_open.png", (1, -0.9, 0.5), 0.95, (1600, 900), (0, mid_y, mid_z + 0.03))


export("body", "ak_type", "weapons", (0, 0, 0), weapon_markers)
export("handguard", "ak_handguard_std", "parts", HANDGUARD_AT, lambda: (
    empty("Attach", (0, 0, 0)),
    empty("GripLeft", Vector(GRIP_L_AT) - Vector(HANDGUARD_AT), GRIP_L_ROT, grip_pose="wrap")))
export("stock", "ak_stock_std", "parts", STOCK_AT, lambda: empty("Attach", (0, 0, 0)))
MAG_AT = (0.0, *P(578, 148))
export("mag", "ak_mag", "parts", MAG_AT, lambda: (
    empty("Attach", (0, 0, 0)),
    empty("Magazine", V(0, *mag_point(3, .5)) - Vector(MAG_AT))))
render(os.path.join(ROOT, "assets", "source", "parts", "ak_mag_preview.png"), (1, -0.7, 0.3), 0.27, (768, 768),
       (0, 0.03, -0.085))
previews()
