"""Retarget Quaternius Universal Animation Library leg clips onto the Project D rig (model-space rotation deltas).

usage: python tools/retarget_ual.py <AnimationLibrary_Godot_Standard.glb> data/animation/locomotion_clips.json [--info]

The pack (CC0) is not stored in the repo: download universal_animation_librarystandard.zip from
https://opengameart.org/node/174563 and unzip the Godot/*.glb. Output keeps only the leg motion around a
reference pose (see LocomotionClips.cs); the stance base is added at runtime.
"""
import json, struct, sys, math

# ---- tiny quaternion math: (x, y, z, w) ----
def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by,
            aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw,
            aw*bw - ax*bx - ay*by - az*bz)
def qinv(a): return (-a[0], -a[1], -a[2], a[3])
def qnorm(a):
    n = math.sqrt(sum(c*c for c in a)) or 1.0
    return tuple(c/n for c in a)
def qrot(q, v):
    p = qmul(qmul(q, (v[0], v[1], v[2], 0.0)), qinv(q))
    return (p[0], p[1], p[2])
def slerp(a, b, t):
    d = sum(x*y for x, y in zip(a, b))
    if d < 0: b = tuple(-c for c in b); d = -d
    if d > 0.9995:
        return qnorm(tuple(x + (y-x)*t for x, y in zip(a, b)))
    th = math.acos(d); s = math.sin(th)
    wa = math.sin((1-t)*th)/s; wb = math.sin(t*th)/s
    return qnorm(tuple(wa*x + wb*y for x, y in zip(a, b)))

# ---- glb ----
def load(path):
    b = open(path, 'rb').read()
    jl = struct.unpack('<I', b[12:16])[0]
    j = json.loads(b[20:20+jl])
    off = 20 + jl
    bl = struct.unpack('<I', b[off:off+4])[0]
    binary = b[off+8:off+8+bl]
    return j, binary

def accessor(j, binary, idx):
    a = j['accessors'][idx]
    bv = j['bufferViews'][a['bufferView']]
    n = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    start = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', n*4)
    out = []
    for i in range(a['count']):
        out.append(struct.unpack_from('<%df' % n, binary, start + i*stride))
    return out

j, binary = load(sys.argv[1])
nodes = j['nodes']
parent = {}
for i, n in enumerate(nodes):
    for c in n.get('children', []):
        parent[c] = i
name_to = {n.get('name'): i for i, n in enumerate(nodes)}

def rest_rot(i): return tuple(nodes[i].get('rotation', (0, 0, 0, 1)))
def rest_pos(i): return tuple(nodes[i].get('translation', (0, 0, 0)))

def chain(i):
    c = []
    while i is not None:
        c.insert(0, i); i = parent.get(i)
    return c

def rest_global_rot(i):
    q = (0, 0, 0, 1)
    for k in chain(i): q = qmul(q, rest_rot(k))
    return q

def rest_global_pos(i):
    p = (0.0, 0.0, 0.0); q = (0, 0, 0, 1)
    for k in chain(i):
        t = rest_pos(k)
        r = qrot(q, t)
        p = (p[0]+r[0], p[1]+r[1], p[2]+r[2])
        q = qmul(q, rest_rot(k))
    return p

MAP = {'Thigh_L': 'DEF-thigh.L', 'Shin_L': 'DEF-shin.L', 'Foot_L': 'DEF-foot.L',
       'Thigh_R': 'DEF-thigh.R', 'Shin_R': 'DEF-shin.R', 'Foot_R': 'DEF-foot.R'}

thigh = rest_global_pos(name_to['DEF-thigh.L'])
foot = rest_global_pos(name_to['DEF-foot.L'])
toe = rest_global_pos(name_to['DEF-toe.L'])
facing_z = toe[2] - foot[2]
print('rest thigh.L', [round(x, 3) for x in thigh], 'foot->toe z', round(facing_z, 3))
# Project D faces -Z with left at -X. If the pack faces +Z (left at +X) spin its deltas 180 degrees about Y.
spin = (0, 1, 0, 0) if facing_z > 0 else (0, 0, 0, 1)

if '--info' in sys.argv:
    sys.exit(0)

CLIPS = ['Idle_Loop', 'Walk_Loop', 'Jog_Fwd_Loop', 'Sprint_Loop', 'Crouch_Idle_Loop', 'Crouch_Fwd_Loop']
FPS = 30

def sampler(anim, node, path):
    for ch in anim['channels']:
        if ch['target'].get('node') == node and ch['target']['path'] == path:
            s = anim['samplers'][ch['sampler']]
            return [t[0] for t in accessor(j, binary, s['input'])], accessor(j, binary, s['output']), s.get('interpolation', 'LINEAR')
    return None

def eval_rot(smp, t, rest):
    if smp is None: return rest
    times, vals, interp = smp
    if t <= times[0]: return tuple(vals[0])
    if t >= times[-1]: return tuple(vals[-1])
    lo = 0
    for k in range(len(times)-1):
        if times[k] <= t <= times[k+1]: lo = k; break
    f = (t - times[lo]) / (times[lo+1] - times[lo]) if times[lo+1] > times[lo] else 0
    if interp == 'STEP': return tuple(vals[lo])
    return slerp(tuple(vals[lo]), tuple(vals[lo+1]), f)

out = {'source': 'Quaternius Universal Animation Library (Standard), CC0', 'fps': FPS, 'clips': {}}
anims = {a['name']: a for a in j['animations']}
for clip in CLIPS:
    anim = anims[clip]
    length = max(max(accessor(j, binary, s['input'])) [0] for s in anim['samplers'])
    n = max(2, int(round(length * FPS)))
    cache = {}
    def smp(i, path='rotation'):
        key = (i, path)
        if key not in cache: cache[key] = sampler(anim, i, path)
        return cache[key]
    bones = {k: [] for k in MAP}
    for step in range(n + 1):
        t = length * step / n
        def global_rot(i):
            q = (0, 0, 0, 1)
            for k in chain(i): q = qmul(q, eval_rot(smp(k), t, rest_rot(k)))
            return qnorm(q)
        delta = {}
        for ours, theirs in MAP.items():
            i = name_to[theirs]
            d = qmul(global_rot(i), qinv(rest_global_rot(i)))
            delta[ours] = qnorm(qmul(qmul(spin, d), qinv(spin)))
        hi = name_to['DEF-hips']
        hd = qmul(global_rot(hi), qinv(rest_global_rot(hi)))
        hips = qnorm(qmul(qmul(spin, hd), qinv(spin)))
        for side in 'LR':
            # thigh relative to the pelvis: the game keeps the hips upright and moves them procedurally
            th, sh, ft = delta['Thigh_' + side], delta['Shin_' + side], delta['Foot_' + side]
            local = {'Thigh_' + side: qmul(qinv(hips), th),
                     'Shin_' + side: qmul(qinv(th), sh),
                     'Foot_' + side: qmul(qinv(sh), ft)}
            for k, q in local.items():
                bones[k].append([round(t, 4)] + [round(c, 5) for c in qnorm(q)])
    # Keep only the motion, not the pack's posture: our hips height and stance base come from the game
    # (no foot IK yet), so each bone's keys become rotations relative to a reference orientation.
    def pitch_x(q):
        x, y, z, w = q
        return math.degrees(math.atan2(2*(w*x + y*z), 1 - 2*(x*x + y*y)))
    def mean_quat(keys):
        qs = [tuple(k[1:]) for k in keys[:-1]]
        ref = qs[0]
        acc = [0, 0, 0, 0]
        for q in qs:
            if sum(a*b for a, b in zip(q, ref)) < 0: q = tuple(-c for c in q)
            acc = [a + c for a, c in zip(acc, q)]
        return qnorm(tuple(acc))
    crouch = clip.startswith('Crouch')
    for name, keys in bones.items():
        if name.startswith('Shin') and not crouch:
            # straight at the most extended frame, flexed below that: the knee never hyperextends
            ref = tuple(max(keys[:-1], key=lambda k: pitch_x(tuple(k[1:])))[1:])
        else:
            ref = mean_quat(keys)
        gain = 0.5 if name.startswith('Foot') else 1.0
        for k in keys:
            q = qmul(qinv(ref), tuple(k[1:]))   # rotation relative to the reference, in the bone's own frame
            if gain != 1.0: q = slerp((0, 0, 0, 1), q, gain)
            k[1:] = [round(c, 5) for c in qnorm(q)]
    out['clips'][clip] = {'length': round(length, 4), 'bones': bones}
    print(clip, round(length, 3), 's', n + 1, 'keys')

json.dump(out, open(sys.argv[2], 'w'), separators=(',', ':'))
print('written', sys.argv[2])
