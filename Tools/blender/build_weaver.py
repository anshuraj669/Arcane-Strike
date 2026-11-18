"""
Builds the main character, the Weaver, and exports it for Unity on its own (so the attic model stays light and
the character is only loaded when the story needs it):

    blender -b -P Tools/blender/build_weaver.py            (from the Unity project folder)
    blender -b -P build_weaver.py -- <out.fbx>             (choose the output)

Writes Assets/_Project/Art/Characters/Weaver/Weaver.fbx with Weaver.materials.json and Weaver.placements.json.
Then in Unity: Arcane Strike → Apply Attic Model (it also rebuilds Assets/_Project/Resources/Characters/Weaver.prefab,
which WarriorOutfit loads when the Ascension begins).

Source (OBJ, 1 unit tall, feet at -0.5, facing +Z, an A-pose):
- props/weaver_body.obj: the boy himself (arkane 1.obj: hoodie, joggers, shoes).
The outfit is modelled here (no outside model): white techwear after the user's img_1.png reference (18 Nov): a deep
white hood over a black ribbed face mask, a fitted jacket with dark seams, a zip and slanting zip pockets, black
ribbed cuffs and dark gloves, a black harness strap with buckles, cargo joggers (thigh pockets, a thigh strap,
drawstrings, gathered ankles) and dark sneaker-boots. Plain colours: no photo textures (they tinted the white brown).
The "Cape" step is the harness and thigh strap. Each piece is lofted from rings fitted round the boy's
own body (the furthest the body reaches in each direction round an axis, then pushed out), so it sits on him.

Objects (all under the root "Weaver", in Unity metres, 1.70 m tall):
- Body_Head, Body_Arms, Body_Torso, Body_Feet: the boy in his own clothes. Each outfit part that forms replaces
  one of them (sleeves → arms, boots → feet, coat → torso); his head stays his own.
- Avatar_Sleeve, Avatar_Boots, Avatar_Coat, Avatar_Scarf, Avatar_Cape, Avatar_Sword: the outfit on the whole figure
  (sleeves: the jacket's sleeves and gloves; boots: leather boots; coat: the jacket, belt and trousers; Scarf: the
  collar and the cape's clasp; cape: the long white cape), the sword
  sheathed across his back.
- Outfit_Sleeve_L/R, Outfit_Boots, Outfit_Coat, Outfit_Cape, Outfit_Sword_Hilt/_Scabbard: the parts
  fastened to the player in first person (no head, collar, upper arms or hands). Sleeves: anchor at the wrist, the sleeve runs back
  up the arm (-Z), the back of the hand is +Y.
- Weaver_Sword: the bare sword (a broad straight short sword, bronze-hilted with a red wrapped grip), pivot in the middle of the grip, blade up (+Y),
  flats facing ±Z, the edge's curve towards +X.
The sheath hangs diagonally across the back: the hilt over the right shoulder (to draw it with the right hand), the
tip at the left hip, over the jacket and in front of the cape.
Everything except the sleeves and Weaver_Sword shares one anchor (WarriorOutfit's): 0.38 m below and 0.08 m behind
the eyes, front +Z.
"""

import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_FBX = args[0] if len(args) > 0 else os.path.join(PROJECT, "Assets/_Project/Art/Characters/Weaver/Weaver.fbx")
BODY_OBJ = os.path.join(HERE, "props", "weaver_body.obj")
SWORD_GRIP = 0.15    # the middle of the grip, along the sword (m from the pommel)
SWORD_GUARD = 0.305  # the guard
SWORD_TIP = 1.16     # the point (a long blade: 0.855 m from the guard)
TEXTURES = os.path.join(PROJECT, "Assets/_Project/Art/Environment/Attic/Textures")

bpy.ops.wm.read_factory_settings(use_empty=True)

# Unity (x right, y up, z forward) → Blender, as in build_attic.py.
TO_BLENDER = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0))).to_4x4()

# Where the player's eyes go on the body (OBJ frame, y from the feet): a little above and in front of his own,
# so looking down the player sees the front of the chest and not into the collar.
EYE = Vector((0.0, 0.94, 0.125))
SCALE = 1.6 / EYE.y
ANCHOR = EYE * SCALE - Vector((0.0, 0.38, 0.08))  # WarriorOutfit's chestBelowEyes / chestBehindEyes

# ---------------------------------------------------------------- materials

MATERIALS = {}


def material(name, color, roughness=0.8, metallic=0.0, emission=None, tex=None, tile=1.0):
    full = "M_Weaver_" + name
    m = bpy.data.materials.new(full)
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 1.0
    if tex is not None:
        nodes, links = m.node_tree.nodes, m.node_tree.links
        base = nodes.new("ShaderNodeTexImage")
        base.image = bpy.data.images.load(os.path.join(TEXTURES, tex, f"{tex}_Color.jpg"), check_existing=True)
        tint = nodes.new("ShaderNodeMix")
        tint.data_type, tint.blend_type = "RGBA", "MULTIPLY"
        tint.inputs[0].default_value = 1.0
        tint.inputs[7].default_value = (*color, 1.0)
        links.new(base.outputs["Color"], tint.inputs[6])
        links.new(tint.outputs[2], bsdf.inputs["Base Color"])
    m.diffuse_color = (*color, 1.0)
    MATERIALS[full] = {"name": full, "color": list(color), "smoothness": 1.0 - roughness, "metallic": metallic, "alpha": 1.0,
                       "emission": list(emission) if emission else [0, 0, 0], "texture": tex or "", "tile": tile,
                       "emissive": emission is not None}
    return m


# The outfit: white techwear (the user's img_1.png reference). WarriorOutfit tints the cloth by name for each
# appearance (OutfitCoat, OutfitTrim, OutfitBoots, OutfitCape, OutfitScarf); the rest keep their own colours.
# Plain colours, no photo textures: the leather texture is brown and turned the white brown in Unity.
COAT = material("OutfitCoat", (0.93, 0.93, 0.92), 0.78)      # the jacket
TRIM = material("OutfitTrim", (0.9, 0.9, 0.89), 0.82)        # the cargo joggers
BOOTS = material("OutfitBoots", (0.13, 0.13, 0.14), 0.6)     # dark sneaker-boots
GLOVES = material("OutfitGloves", (0.09, 0.09, 0.1), 0.65)   # dark gloves
SOLE = material("OutfitSole", (0.88, 0.88, 0.86), 0.7)       # white soles
CAPE = material("OutfitCape", (0.08, 0.08, 0.09), 0.6)       # the harness and thigh straps (black webbing)
SCARF = material("OutfitScarf", (0.94, 0.94, 0.93), 0.8)     # the hood
MASK = material("OutfitMask", (0.07, 0.07, 0.08), 0.9)       # the ribbed face mask and cuffs
BELT = material("OutfitBelt", (0.1, 0.1, 0.11), 0.7)         # the belt, drawstrings
STRAP = material("OutfitStrap", (0.16, 0.16, 0.17), 0.6)     # seams, zips, pocket edges
BUCKLE = material("OutfitSteel", (0.55, 0.56, 0.58), 0.35, 0.5)  # buckles, zip pulls
# The boy.
SKIN = material("Skin", (0.72, 0.48, 0.36), 0.6)
HAIR = material("Hair", (0.13, 0.08, 0.05), 0.5)
HOODIE = material("Hoodie", (0.5, 0.55, 0.62), 0.95, tex="Fabric061", tile=0.25)
JOGGERS = material("Joggers", (0.35, 0.35, 0.37), 0.95, tex="Fabric061", tile=0.25)
SHOES = material("Shoes", (0.92, 0.9, 0.86), 0.7, tex="Leather037", tile=0.2)
# The sword.
STEEL = material("SwordSteel", (0.62, 0.64, 0.68), 0.28, 1.0, tex="Metal063", tile=0.25)
EDGE = material("SwordEdge", (0.9, 0.91, 0.94), 0.15, 1.0, tex="Metal063", tile=0.25)
FULLER = material("SwordFuller", (0.3, 0.31, 0.34), 0.35, 1.0, tex="Metal063", tile=0.25)
WRAP = material("SwordWrap", (0.42, 0.07, 0.05), 0.6, tex="Leather037", tile=0.06)
SAYA = material("Saya", (0.11, 0.075, 0.05), 0.5, tex="Leather037", tile=0.15)  # a dark leather sheath
GOLD = material("OutfitTrimSword", (0.85, 0.62, 0.25), 0.3, 1.0, tex="Metal063", tile=0.1)

# ---------------------------------------------------------------- loading


def load_obj(path):
    """Vertices (x, y from the feet, z front: OBJ frame) merged by position, and faces."""
    positions, faces = [], []
    with open(path) as f:
        for line in f:
            if line.startswith("v "):
                x, y, z = map(float, line.split()[1:4])
                positions.append(Vector((x, y + 0.5, z)))
            elif line.startswith("f "):
                faces.append([int(t.split("/")[0]) - 1 for t in line.split()[1:]])
    index, merged, remap = {}, [], []
    for p in positions:
        key = (round(p.x, 5), round(p.y, 5), round(p.z, 5))
        if key not in index:
            index[key] = len(merged)
            merged.append(p)
        remap.append(index[key])
    return merged, [[remap[i] for i in face] for face in faces]


def seg(p, a, b):
    """(distance from p to the segment a-b, how far along it 0..1)"""
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length, t


def chain(side, points):
    return [Vector((side * x, y, z)) for x, y, z in points]


# Shoulder, elbow, wrist, fingertips of the boy's arms (OBJ frame).
BOY_ARM = ((0.13, 0.735, -0.015), (0.215, 0.655, -0.015), (0.28, 0.58, 0.015), (0.335, 0.48, 0.045))
BOY = {1: chain(1, BOY_ARM), -1: chain(-1, BOY_ARM)}


def to_unity(p):
    """OBJ frame → Unity metres (the figure faces +Z either way; OBJ x is the figure's left)."""
    return Vector((-p.x, p.y, p.z)) * SCALE


# ---------------------------------------------------------------- building meshes


def make_part(name, items, positions, root, to_local, decimate=6000, keep_winding=False):
    """One object from [(face, material)], vertices through to_local(Unity point) → local Unity point. The mirror into
    Blender's axes turns faces inside out, so their corners are reversed, unless `keep_winding` (the boy's model is
    wound the other way round already)."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    used, mats = {}, []
    for item in items:
        face, mat = item[0], item[1]
        face_uv = item[2] if len(item) > 2 else None
        vs = []
        for i in face:
            if i not in used:
                used[i] = bm.verts.new(TO_BLENDER @ to_local(positions[i]))
            vs.append(used[i])
        try:
            f = bm.faces.new(vs if keep_winding else list(reversed(vs)))
        except ValueError:
            continue  # a duplicate face (or a corner used twice)
        if mat not in mats:
            mats.append(mat)
        f.material_index = mats.index(mat)
        f.smooth = True
        if face_uv is not None and None not in face_uv:
            for loop, t in zip(f.loops, face_uv if keep_winding else reversed(face_uv)):  # the model's own UVs
                loop[uv].uv = t
            continue
        n = f.normal
        axis = max(range(3), key=lambda k: abs(n[k]))
        a, b = [k for k in range(3) if k != axis]
        tile = MATERIALS[mat.name]["tile"]
        for loop in f.loops:
            loop[uv].uv = (loop.vert.co[a] / tile, loop.vert.co[b] / tile)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for m in mats:
        mesh.materials.append(m)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = root
    if len(mesh.polygons) > decimate:  # light enough for Quest: halve the big parts
        obj.modifiers.new("Decimate", "DECIMATE").ratio = 0.5
    print(f"[Weaver] {name}: {len(mesh.polygons)} faces")
    return obj


def body_local(p):
    return p - ANCHOR


def build_body(root):
    """The boy, cut into the parts the outfit replaces."""
    positions, faces = load_obj(BODY_OBJ)
    parts = {}
    for face in faces:
        c = sum((positions[i] for i in face), Vector()) / len(face)
        side = 1 if c.x >= 0 else -1
        s_, e_, w_, h_ = BOY[side]
        if abs(c.x) > 0.27 and seg(c, w_, h_)[0] < 0.06 and c.y > 0.42:
            part, mat = "Arms", SKIN  # the hand
        elif 0.45 < c.y < 0.8 and abs(c.x) > 0.105 and min(seg(c, s_, e_)[0], seg(c, e_, w_)[0]) < 0.06 \
                and (abs(c.x) > 0.14 or c.y < 0.715):
            part, mat = "Arms", HOODIE
        elif c.y > 0.84:
            if (c.z < -0.045 and c.y < 0.9) or (abs(c.x) > 0.052 and c.y < 0.875):
                part, mat = "Torso", HOODIE  # the hood, round the back of the neck
            else:
                hair = c.y > 0.928 or (c.z < 0.02 and c.y > 0.875) or (abs(c.x) > 0.05 and c.y > 0.89)
                part, mat = "Head", HAIR if hair else SKIN
        elif c.y > 0.79 and abs(c.x) < 0.04 and -0.03 < c.z < 0.06:
            part, mat = "Head", SKIN  # the neck
        elif c.y < 0.085:
            part, mat = "Feet", SHOES
        elif c.y < 0.22:
            part, mat = "Feet", JOGGERS
        else:
            part, mat = "Torso", JOGGERS if c.y < 0.545 else HOODIE
        parts.setdefault("Body_" + part, []).append((face, mat))
    unity = [to_unity(p) for p in positions]
    for name, items in parts.items():
        make_part(name, items, unity, root, body_local, keep_winding=True)


# ---------------------------------------------------------------- the armour, fitted to the boy

_body_vertices = None


def body_vertices():
    """The boy's vertices (OBJ frame, y from the feet), to fit the armour round."""
    global _body_vertices
    if _body_vertices is None:
        _body_vertices = np.array([tuple(p) for p in load_obj(BODY_OBJ)[0]])
    return _body_vertices


def axes(d, e1):
    """An axis d and two directions across it: e1 (made square to d) and d × e1."""
    d = np.asarray(d, float) / np.linalg.norm(d)
    e1 = np.asarray(e1, float) - d * np.dot(e1, d)
    e1 /= np.linalg.norm(e1)
    return d, e1, np.cross(d, e1)


def body_radii(c, d, e1, n, reach, slab=0.012, ignore=None):
    """How far the body reaches from an axis (through c along d) in n directions round it, over a thin slab across
    it and out to `reach` (so a nearby limb isn't counted). Directions the body misses are filled from their
    neighbours, and the ring is smoothed."""
    d, e1, e2 = axes(d, e1)
    v = body_vertices()
    if ignore is not None:
        v = v[~ignore(v)]
    rel = v - np.asarray(c, float)
    along = rel @ d
    near = np.abs(along) < slab
    flat = rel[near] - np.outer(along[near], d)
    r = np.linalg.norm(flat, axis=1)
    flat, r = flat[r < reach], r[r < reach]
    sector = (np.mod(np.arctan2(flat @ e2, flat @ e1), math.tau) / math.tau * n).astype(int) % n
    radii = np.full(n, np.nan)
    for k in range(n):
        if np.any(sector == k):
            radii[k] = r[sector == k].max()
    good = ~np.isnan(radii)
    if not good.any():
        return np.full(n, 0.03)
    k = np.arange(n)
    radii = np.interp(k, k[good], radii[good], period=n)
    for _ in range(2):
        radii = 0.25 * np.roll(radii, 1) + 0.5 * radii + 0.25 * np.roll(radii, -1)
    return radii


def rings_round(centres, dirs, e1, n, reach, offset, extra=None, ignore=None):
    """A ring round the body at each centre (axis along the matching dir), `offset` out from it, plus
    extra(direction, t) where t runs 0..1 along the rings; smoothed along the length. Unity points."""
    radii = np.array([body_radii(c, d, e1, n, reach, ignore=ignore) for c, d in zip(centres, dirs)])
    for _ in range(2):
        radii[1:-1] = 0.25 * radii[:-2] + 0.5 * radii[1:-1] + 0.25 * radii[2:]
    rings = []
    for i, (c, d) in enumerate(zip(centres, dirs)):
        t = i / max(1, len(centres) - 1)
        _, a1, a2 = axes(d, e1)
        ring = []
        for k in range(n):
            a = (k + 0.5) / n * math.tau
            v = math.cos(a) * a1 + math.sin(a) * a2
            r = radii[i][k] + offset + (extra(v, t) if extra else 0.0)
            ring.append(to_unity(Vector((np.asarray(c, float) + v * r).tolist())))
        rings.append(ring)
    return rings


def facing(rings, front, limit):
    """The part of each ring that faces within `limit` degrees of `front` (Unity), in order round it."""
    centre = sum(rings[0], Vector()) / len(rings[0])
    axis = (sum(rings[-1], Vector()) / len(rings[-1]) - centre).normalized()
    front = (front - axis * front.dot(axis)).normalized()
    side = axis.cross(front)
    angles = [math.atan2((p - centre).dot(side), (p - centre).dot(front)) for p in rings[0]]
    cols = sorted((k for k, a in enumerate(angles) if abs(a) < math.radians(limit)), key=lambda k: angles[k])
    return [[ring[k] for k in cols] for ring in rings]


def middle(y, x0=0.0, half=0.1, ignore=None):
    """The middle of the body's slice at height y (OBJ frame), among the points within `half` of x0."""
    v = body_vertices()
    if ignore is not None:
        v = v[~ignore(v)]
    s = v[(np.abs(v[:, 1] - y) < 0.01) & (np.abs(v[:, 0] - x0) < half)]
    if len(s) == 0:
        return np.array([x0, y, 0.0])
    x = (s[:, 0].min() + s[:, 0].max()) / 2 if x0 else 0.0
    return np.array([x, y, (s[:, 2].min() + s[:, 2].max()) / 2])


def steps(a, b, count):
    return [a + (b - a) * k / (count - 1) for k in range(count)]


def along_arm(side, start, end, count):
    """Centres and directions along the arm (OBJ frame), `start`..`end` in joints (0 shoulder, 1 elbow, 2 wrist,
    3 fingertips); the direction turns smoothly through each joint."""
    joints = [np.array(tuple(p)) for p in BOY[side]]
    centres, dirs = [], []
    for s in steps(start, end, count):
        i = min(int(s), 2)
        f = s - i
        centres.append(joints[i] + (joints[i + 1] - joints[i]) * f)
        here = joints[i + 1] - joints[i]
        if f < 0.25 and i > 0:
            here = here + (joints[i] - joints[i - 1]) * (0.25 - f) * 2
        elif f > 0.75 and i < 2:
            here = here + (joints[i + 2] - joints[i + 1]) * (f - 0.75) * 2
        dirs.append(here / np.linalg.norm(here))
    return centres, dirs


UP = (0.0, 1.0, 0.0)
FRONT = Vector((0.0, 0.0, 1.0))  # (Unity)
# The boy's hood, lying on his back: not the body. Round the neck (y > 0.79) his own back is at about z -0.05, and
# everything further back (to -0.1) is that hood, which would push the collar and the mask out through the cowl.
HOOD = lambda v: (v[:, 1] > 0.72) & ((v[:, 2] < -0.075) | ((v[:, 1] > 0.79) & (v[:, 2] < -0.058)))


def torso_reach(y):
    """How far the torso is looked for: under the arms only the body; from the armpits up, the shoulders too."""
    return 0.118 if y < 0.705 else 0.2


def torso(y0, y1, count, offset, extra=None, shoulders=0.2):
    """Rings round the torso from y0 up to y1; above the armpits out to `shoulders` from the middle."""
    rings = []
    for i, y in enumerate(steps(y0, y1, count)):  # one ring at a time: the reach changes with the height
        reach = min(torso_reach(y), shoulders)
        bulge = (lambda v, t, i=i: extra(v, i / max(1, count - 1))) if extra else None
        rings.append(rings_round([middle(y, ignore=HOOD)], [UP], (1, 0, 0), 28, reach, offset, bulge, HOOD)[0])
    return rings


def jacket_hem(v, t):
    """The jacket hangs a little loose, flaring out towards its hem."""
    return 0.004 * (1.0 - t) ** 2


JACKET_YS = steps(0.49, 0.845, 26)


def wrinkles(amp, freq=9.0, phase=0.0):
    """Soft cloth folds for rings_round's `extra`: ripples round the ring that drift along it (amp in OBJ units)."""
    def fold(v, t):
        a = math.atan2(v[2], v[0])
        return amp * (0.55 * math.sin(a * 5.0 + t * freq + phase) + 0.3 * math.sin(a * 9.0 - t * freq * 1.7 + 1.3 * phase)
                      + 0.15 * math.sin(a * 2.0 + t * freq * 0.6))
    return fold


def on_rings(rings, ys, y, x, lift=0.0):
    """The point on the front of the ring nearest height y (OBJ frame), at Unity x, pushed out by `lift`."""
    i = min(range(len(ys)), key=lambda k: abs(ys[k] - y))
    return front_point(rings[i], x) + Vector((0.0, 0.0, lift))


def front_arc(ring, lift=0.003, spread=0.55):
    """The front part of a ring (Unity), pushed out a little: a seam across the chest."""
    c = sum(ring, Vector()) / len(ring)
    half = max((p - c).length for p in ring)
    return [p + (p - c).normalized() * lift for p in ring if (p.z - c.z) > half * (1.0 - spread)]


def coat(shape):
    """Loose white cargo joggers gathered at the ankles, with thigh pockets and drawstrings, a dark belt, and a fitted
    white jacket: dark seams, a front zip and two slanting zip pockets on the chest."""
    for side in (1, -1):  # the trouser legs, a little baggy, gathered into the ankle cuffs
        ys = steps(0.1, 0.5, 21)
        leg = rings_round([middle(y, 0.065 * side, 0.05) for y in ys], [UP] * len(ys), (1, 0, 0), 20, 0.075, 0.004,
                          lambda v, t: 0.006 * math.sin(min(1.0, t * 1.6) * math.pi * 0.5) - 0.007 * (1.0 - t) ** 4 - 0.008 * max(0.0, t - 0.85) / 0.15
                          + wrinkles(0.004, 22.0, side)(v, t) * (1.0 - t) ** 0.5)
        shape.sheet(leg, TRIM, closed=True, thickness=0.003)
        shape.sheet(leg[:3], MASK, closed=True, thickness=0.004)  # the gathered dark ankle band
        # The cargo pocket on the outside of the thigh, its flap and zip; a seam down the outside of the leg.
        out = Vector((-side, 0.0, 0.0))
        i = ys.index(min(ys, key=lambda y: abs(y - 0.33)))
        p = max(leg[i], key=lambda q: q.dot(out))
        shape.box(p + out * 0.006, (0.02, 0.15, 0.11), TRIM)
        shape.box(p + out * 0.016 + Vector((0.0, 0.07, 0.0)), (0.008, 0.035, 0.115), TRIM)
        shape.box(p + out * 0.021 + Vector((0.0, 0.05, 0.0)), (0.003, 0.004, 0.09), STRAP)
        shape.rim([max(r, key=lambda q: q.dot(out)) + out * 0.002 for r in leg[4:]], 0.0018, STRAP)
    shape.sheet(torso(0.47, 0.56, 5, 0.0), TRIM, closed=True, thickness=0.003)
    belt = torso(0.53, 0.55, 2, 0.01)
    shape.sheet(belt, BELT, closed=True, thickness=0.003)
    front = front_point(belt[0], 0.0)
    shape.box(front + Vector((0.0, 0.017, 0.006)), (0.05, 0.03, 0.008), BUCKLE)
    for x, length in ((-0.1, 0.3), (0.075, 0.22)):  # loose black straps hanging from the belt at the hips
        top = front_point(belt[0], x) + Vector((0.0, 0.0, 0.006))
        rows = [[top + Vector((-0.011, -length * t, 0.008 * t)), top + Vector((0.011, -length * t, 0.008 * t))] for t in steps(0.0, 1.0, 6)]
        shape.sheet(rows, BELT, thickness=0.003)
        shape.box(top + Vector((0.0, -length * 0.55, 0.008)), (0.03, 0.022, 0.007), BUCKLE)
    for x in (-0.022, 0.022):  # the drawstrings, hanging from the waistband
        top = front + Vector((x, 0.0, 0.01))
        cord = [top + Vector((0.004 * math.sin(t * 2.0), -0.2 * t, 0.012 * t)) for t in steps(0.0, 1.0, 8)]
        shape.rim(cord, 0.0022, BELT)
        shape.cylinder(cord[-1] - Vector((0.0, 0.01, 0.0)), 0.004, 0.022, BUCKLE, segs=8)
    # The jacket, close to the body, from the hips to the neck.
    folds = wrinkles(0.0035, 14.0)
    jacket = torso(0.49, 0.845, 26, -0.009, lambda v, t: folds(v, t) * (1.0 - t) + 0.006 * max(0.0, 0.25 - t) / 0.25)
    ys = JACKET_YS
    shape.sheet(jacket, COAT, closed=True, thickness=0.004)
    shape.rim(jacket[0], 0.0045, COAT, closed=True)
    shape.rim(front_line(jacket[1:24], 0.008, 0.004), 0.0025, STRAP)                 # the main zip, a little off centre
    for x in (-0.085, 0.09):                                                       # seams down the front panels
        shape.rim(front_line(jacket[1:21], x, 0.003), 0.0016, STRAP)
    shape.rim(front_arc(jacket[ys.index(min(ys, key=lambda y: abs(y - 0.77)))]), 0.0016, STRAP)  # the chest yoke
    shape.rim(front_arc(jacket[ys.index(min(ys, key=lambda y: abs(y - 0.6)))], spread=0.4), 0.0016, STRAP)
    for (y0, x0), (y1, x1) in (((0.73, 0.03), (0.715, 0.075)), ((0.735, -0.03), (0.72, -0.075)), ((0.65, 0.035), (0.64, 0.075))):
        a, b = on_rings(jacket, ys, y0, x0, 0.004), on_rings(jacket, ys, y1, x1, 0.004)
        shape.rim([a, a.lerp(b, 0.5) + Vector((0.0, 0.0, 0.002)), b], 0.0022, STRAP)  # a slanting zip pocket
        shape.box(a + Vector((0.0, -0.012, 0.003)), (0.006, 0.016, 0.003), BUCKLE)    # its pull


def front_point(ring, x):
    """Where a ring crosses `x` on its front (between the two corners either side), so a line over rings runs smooth."""
    best = None
    for p, q in zip(ring, ring[1:] + ring[:1]):
        if (p.x - x) * (q.x - x) > 0 or p.x == q.x:
            continue
        r = p.lerp(q, (x - p.x) / (q.x - p.x))
        if best is None or r.z > best.z:
            best = r
    return best if best is not None else max(ring, key=lambda q: q.z)


def front_line(rings, x, lift=0.004):
    """A smooth path over the front of a set of rings at `x` (a number, or one per ring), pushed out a little."""
    xs = x if isinstance(x, (list, tuple)) else [x] * len(rings)
    pts = [front_point(r, xi) + Vector((0.0, 0.0, lift)) for r, xi in zip(rings, xs)]
    return [pts[0]] + [(pts[i - 1] + pts[i] * 2 + pts[i + 1]) / 4 for i in range(1, len(pts) - 1)] + [pts[-1]]


def boots(shape):
    """Dark sneaker-boots up over the ankle, white soles, laced."""
    for side in (1, -1):
        x0 = 0.065 * side
        ys = steps(0.004, 0.11, 12)
        shape.sheet(rings_round([middle(y, x0, 0.06) for y in ys], [UP] * len(ys), (1, 0, 0), 24, 0.092, 0.003),
                    BOOTS, closed=True, cap=True)
        ys = steps(0.002, 0.026, 3)  # the sole
        shape.sheet(rings_round([middle(y, x0, 0.06) for y in ys], [UP] * 3, (1, 0, 0), 24, 0.092, 0.011),
                    SOLE, closed=True, cap=True)


def forearm(shape, side):
    """The jacket's forearm, its folds bunching towards the wrist, and the long black ribbed cuff that runs on over
    the back of the hand to the knuckles (thumb-hole style) (also the first-person sleeve)."""
    centres, dirs = along_arm(side, 1.0, 1.8, 10)
    sleeve = rings_round(centres, dirs, (side, 0, 0), 16, 0.065, 0.001,
                         lambda v, t: 0.002 * t + wrinkles(0.004, 20.0, side)(v, t) * (0.3 + 0.7 * t))
    shape.sheet(sleeve, COAT, closed=True, thickness=0.003)
    centres, dirs = along_arm(side, 1.72, 2.42, 10)
    cuff = rings_round(centres, dirs, (side, 0, 0), 16, 0.065, 0.002)
    shape.sheet(cuff, MASK, closed=True, thickness=0.003)
    for ring in cuff[1:6]:
        shape.rim(ring, 0.0016, MASK, closed=True)


def sleeves(shape):
    """The jacket's sleeves (a seam round the upper arm) and dark gloves."""
    for side in (1, -1):
        centres, dirs = along_arm(side, 0.1, 1.05, 12)
        upper = rings_round(centres, dirs, (side, 0, 0), 16, 0.065, -0.002, wrinkles(0.003, 12.0, side))
        shape.sheet(upper, COAT, closed=True, thickness=0.003)
        shape.rim([p + (p - sum(upper[3], Vector()) / 16).normalized() * 0.003 for p in upper[3]], 0.0016, STRAP, closed=True)
        forearm(shape, side)
        centres, dirs = along_arm(side, 2.38, 2.97, 8)  # bare fingers out of the cuff
        shape.sheet(rings_round(centres, dirs, (side, 0, 0), 16, 0.06, 0.0005), SKIN, closed=True, cap=True)


CHIN = 0.024  # how much lower the collar sits at the front (his chin is low)


def collar_ring(y, rx, rz, zc, base):
    return [to_unity(Vector((rx * math.cos(a), y - CHIN * (y - base) / 0.05 * max(0.0, math.sin(a)), zc + rz * math.sin(a))))
            for a in (k / 28 * math.tau for k in range(28))]


def hood(shape):
    """A tall, soft white hood up over the head: it rises to a peak above the brow (the crown leaning forward), opens
    in a narrow oval round the eyes (the mask fills it below), is lined dark so the face sits in shadow, and falls in
    soft folds onto the shoulders like a cowl, longer behind.
    Fitted to the boy's head (OBJ frame: about ±0.065 wide, z -0.06 behind to +0.09 at the nose, crown at y 1.0), a
    finger or two clear of it all round, so it reads as cloth and not a helmet; the cowl ends at the shoulders' width
    instead of flaring past them. The face inside is a black shadow (as in the reference), so only the mask shows."""
    rows, shadow = [], []
    for t in steps(0.0, 1.0, 30):
        y = 0.79 + 0.245 * t
        if y < 0.87:                                  # the cowl, settling onto the shoulders
            u = (0.87 - y) / 0.08
            # (deeper behind: the jacket's collar and the boy's own hood bulk out his upper back)
            rx, rz, zc = 0.08 + 0.038 * u ** 1.3, 0.088 + 0.022 * u, 0.008 - 0.012 * u
            front_rz = 0.088 - 0.012 * u
            fold = 0.004 * u
        elif y < 0.96:                                # round the head
            rx, rz, zc, front_rz, fold = 0.08, 0.088, 0.008, 0.088, 0.0
        else:                                         # up to a low peak over the brow, the crown leaning forward
            u = min(1.0, (y - 0.96) / 0.075)
            rx = 0.08 * math.sqrt(max(0.0, 1.0 - u ** 1.6)) + 0.002
            rz = 0.088 * math.sqrt(max(0.0, 1.0 - u ** 2.2)) + 0.002
            zc = 0.008 + 0.022 * u ** 1.5
            front_rz, fold = rz, 0.0
        if y < 0.855:
            open_ = 0.5
        elif y < 0.875:
            open_ = 0.5 + 0.32 * (y - 0.855) / 0.02
        elif y < 0.965:
            open_ = 0.82
        else:
            open_ = max(0.04, 0.82 * (1.0 - min(1.0, (y - 0.965) / 0.035)) ** 0.7)
        row = []
        for a in steps(open_, math.tau - open_, 36):
            back = max(0.0, -math.cos(a))
            yy = y - (0.02 * back * (1.0 - t / 0.3) if t < 0.3 else 0.0)
            depth = (rz if math.cos(a) < 0.0 else front_rz) + fold * math.sin(a * 6.0)
            row.append(to_unity(Vector(((rx + fold * math.sin(a * 6.0)) * math.sin(a), yy, zc + depth * math.cos(a)))))
        rows.append(row)
        if 0.895 < y < 0.995 and open_ > 0.05:
            # The shadow across the opening, from the mask's top to under the peak: just inside the hood's front
            # (still clear of the nose, which reaches z 0.091).
            shadow.append([to_unity(Vector((rx * math.sin(a), y, zc + (front_rz - 0.003) * math.cos(a))))
                           for a in steps(-open_ - 0.08, open_ + 0.08, 9)])
    shape.sheet(rows, SCARF, thickness=0.007)
    if len(shadow) > 1:
        shape.sheet(shadow, MASK, thickness=0.002)
    edge = [r[0] for r in rows] + [r[-1] for r in reversed(rows)]
    shape.rim(edge, 0.005, SCARF)
    shape.rim(rows[0], 0.0045, SCARF)
    # The dark lining showing just inside the opening (the face in the hood's shadow), and a seam over the crown.
    inner = [[p.lerp(q, 0.0) + (sum(r, Vector()) / len(r) - p).normalized() * 0.006 for p, q in zip(r[:4], r[1:5])] for r in rows[5:]]
    shape.sheet(inner, MASK, thickness=0.002)
    inner = [[p + (sum(r, Vector()) / len(r) - p).normalized() * 0.006 for p in r[-4:]] for r in rows[5:]]
    shape.sheet(inner, MASK, thickness=0.002)
    shape.rim([r[len(r) // 2] for r in rows[10:]], 0.0016, STRAP)


def mask(shape):
    """The black ribbed mask: a snug tube from the collar up over the chin and mouth to just under the nose."""
    ys = steps(0.81, 0.905, 9)
    head = lambda v: HOOD(v) | ((v[:, 1] > 0.72) & (np.abs(v[:, 0]) > 0.075))
    rings = rings_round([middle(y, ignore=HOOD) for y in ys], [UP] * len(ys), (1, 0, 0), 28, 0.1, 0.006,
                        lambda v, t: 0.008 * (1.0 - t) ** 2 - 0.014 * max(0.0, -v[2]), ignore=head)  # (tucked in behind, under the cowl)
    shape.sheet(rings, MASK, closed=True, thickness=0.004)
    for ring in rings[1:]:
        shape.rim(ring, 0.0022, MASK, closed=True)


def scarf(shape):
    """The jacket's high collar, the hood and the mask."""
    hood(shape)
    mask(shape)
    rings = [collar_ring(y, rx, rz, zc, 0.815) for y, rx, rz, zc in
             ((0.8, 0.098, 0.09, -0.01), (0.83, 0.088, 0.084, -0.006))]
    shape.sheet(rings, COAT, closed=True, thickness=0.004)
    shape.rim(rings[-1], 0.004, COAT, closed=True)


def cape(shape):
    """The black webbing: a harness strap over the right shoulder down the chest to the belt (two buckles), and a strap
    round the left thigh with a buckle and a hanging end. (Kept under the name Cape for the suit-up's steps.)"""
    jacket = torso(0.49, 0.845, 26, 0.004)
    ys = JACKET_YS
    x = -0.1  # (Unity: the figure's right)
    path = [on_rings(jacket, ys, y, x + 0.025 * (y - 0.55) / 0.27, 0.006) for y in steps(0.55, 0.82, 12)]
    over = jacket[-1]
    top = min(over, key=lambda q: abs(q.x - x) - q.y)
    back = max((q for q in over if q.z < top.z), key=lambda q: -abs(q.x - x), default=top)
    path += [top + Vector((0.0, 0.012, -0.02))]  # (ends on the shoulder, under the hood's cowl)
    rows = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        w = t.cross(Vector((0.0, 0.0, 1.0))).normalized() * 0.015 if abs(t.z) < 0.9 else Vector((0.015, 0.0, 0.0))
        rows.append([p - w, p + w])
    shape.sheet(rows, CAPE, thickness=0.004)
    for k in (3, 8):  # the buckles
        shape.box(path[k] + Vector((0.0, 0.0, 0.006)), (0.038, 0.026, 0.008), BUCKLE)
        shape.box(path[k] + Vector((0.0, 0.0, 0.01)), (0.026, 0.012, 0.004), CAPE)
    # The thigh strap.
    side = -1  # (OBJ: the figure's left)
    ys2 = steps(0.37, 0.39, 2)
    ring = rings_round([middle(y, 0.065 * side, 0.05) for y in ys2], [UP] * 2, (1, 0, 0), 20, 0.075, 0.022)
    shape.sheet(ring, CAPE, closed=True, thickness=0.003)
    out = Vector((-side, 0.0, 0.0))
    p = max(ring[0], key=lambda q: q.dot(out + Vector((0.0, 0.0, 0.6))))
    shape.box(p + out * 0.004, (0.012, 0.03, 0.036), BUCKLE)
    end = [p + Vector((0.004 * k, -0.025 * k, 0.006 * k)) + out * 0.006 for k in range(6)]
    shape.sheet([[q - Vector((0.0, 0.0, 0.012)), q + Vector((0.0, 0.0, 0.012))] for q in end], CAPE, thickness=0.003)


def build_outfit(root):
    """The outfit: each part twice, on the whole figure (Avatar_*) and worn in first person (Outfit_*)."""
    place = Matrix.Translation(-ANCHOR)
    for name, make, worn in (("Sleeve", sleeves, False), ("Boots", boots, True), ("Coat", coat, True),
                             ("Scarf", scarf, False), ("Cape", cape, True)):
        for prefix in ("Avatar_",) + (("Outfit_",) if worn else ()):
            shape = Shape()
            make(shape)
            shape.build(prefix + name, root, place)
    for side in (1, -1):
        # First person: the forearm in the wrist's frame (anchor at the wrist, back up the arm along -Z, +Y out).
        _, elbow, wrist, _ = (to_unity(p) for p in BOY[side])
        fwd = (wrist - elbow).normalized()
        out = Vector((-side, 0.0, 0.0))
        up = (out - fwd * out.dot(fwd)).normalized()
        right = up.cross(fwd)
        shape = Shape()
        forearm(shape, side)
        shape.build("Outfit_Sleeve_" + ("L" if side > 0 else "R"), root,
                    Matrix((right, up, fwd)).to_4x4() @ Matrix.Translation(-wrist))


# ---------------------------------------------------------------- the sword

class Shape:
    """Closed shapes built in Unity terms (a local frame), gathered into one bmesh per material."""

    def __init__(self):
        self.pieces = []  # (bmesh, material)

    def add(self, bm, mat):
        self.pieces.append((bm, mat))

    def loft(self, sections, mat, cap_tip=None):
        """Rings of points (lists of Vector, same count) joined into a tube, capped; cap_tip closes the last end to a point."""
        bm = bmesh.new()
        rings = [[bm.verts.new(p) for p in ring] for ring in sections]
        n = len(sections[0])
        for r0, r1 in zip(rings, rings[1:]):
            for k in range(n):
                bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]))
        bm.faces.new(list(reversed(rings[0])))
        if cap_tip is not None:
            tip = bm.verts.new(cap_tip)
            for k in range(n):
                bm.faces.new((rings[-1][k], rings[-1][(k + 1) % n], tip))
        else:
            bm.faces.new(rings[-1])
        self.add(bm, mat)

    def box(self, centre, size, mat):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=size, verts=bm.verts[:])
        bmesh.ops.translate(bm, vec=centre, verts=bm.verts[:])
        self.add(bm, mat)

    def sphere(self, centre, radius, mat, squash=(1, 1, 1), segs=12):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=segs // 2 + 2, radius=radius)
        bmesh.ops.scale(bm, vec=squash, verts=bm.verts[:])
        bmesh.ops.translate(bm, vec=centre, verts=bm.verts[:])
        self.add(bm, mat)

    def cylinder(self, centre, radius, height, mat, top=None, segs=12):
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius if top is None else top, depth=height)
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90), 3, "X"), verts=bm.verts[:])  # z → y
        bmesh.ops.translate(bm, vec=centre, verts=bm.verts[:])
        self.add(bm, mat)

    def sheet(self, rows, mat, closed=False, cap=False, thickness=0.0):
        """A surface through rows of points (each row the same count): round (closed) or open; a closed one can be
        capped at both ends, and any can be given a thickness (open ones need it to be seen from both sides)."""
        bm = bmesh.new()
        grid = [[bm.verts.new(p) for p in row] for row in rows]
        n = len(rows[0])
        for r0, r1 in zip(grid, grid[1:]):
            for k in range(n if closed else n - 1):
                bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]))
        if closed and cap:
            bm.faces.new(list(reversed(grid[0])))
            bm.faces.new(grid[-1])
        if thickness > 0:
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            bm.normal_update()
            bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=thickness)
        self.add(bm, mat)

    def rim(self, path, radius, mat, closed=False, sides=6):
        """A round bead along a path of points: trim along an edge."""
        points = list(path) + ([path[0]] if closed else [])
        rings = []
        for i, p in enumerate(points):
            t = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
            ref = Vector((0, 1, 0)) if abs(t.y) < 0.9 else Vector((1, 0, 0))
            a = t.cross(ref).normalized()
            b = t.cross(a)
            rings.append([p + (a * math.cos(k / sides * math.tau) + b * math.sin(k / sides * math.tau)) * radius
                          for k in range(sides)])
        self.loft(rings, mat)

    def build(self, name, root, place=Matrix.Identity(4)):
        """All pieces into one object; `place` moves them (Unity terms) before the turn into Blender's axes."""
        out = bmesh.new()
        uv = out.loops.layers.uv.new("UVMap")
        mats = []
        temp = bpy.data.meshes.new("_t")
        for bm, mat in self.pieces:
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            for f in bm.faces:
                f.smooth = True
            bm.to_mesh(temp)
            start = len(out.faces)
            out.from_mesh(temp)
            out.faces.ensure_lookup_table()
            if mat not in mats:
                mats.append(mat)
            tile = MATERIALS[mat.name]["tile"]
            for f in list(out.faces)[start:]:
                f.material_index = mats.index(mat)
                n = f.normal
                axis = max(range(3), key=lambda k: abs(n[k]))
                a, b = [k for k in range(3) if k != axis]
                for loop in f.loops:
                    loop[uv].uv = (loop.vert.co[a] / tile, loop.vert.co[b] / tile)
            bm.free()
        bpy.data.meshes.remove(temp)
        bmesh.ops.transform(out, matrix=TO_BLENDER @ place, verts=out.verts[:])
        bmesh.ops.recalc_face_normals(out, faces=out.faces[:])
        mesh = bpy.data.meshes.new(name)
        out.to_mesh(mesh)
        out.free()
        for m in mats:
            mesh.materials.append(m)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = root
        print(f"[Weaver] {name}: {len(mesh.polygons)} faces")
        return obj


def back_frame():
    """Where the sheathed katana hangs on the body (Unity, relative to the anchor): diagonally across the back, the
    guard behind the right shoulder and the tip down at the left hip, its flats against the back. WarriorOutfit.cs
    (SwordDown, SwordGuard, SwordGripToGuard) uses the same numbers to slide the bare sword into it and to draw it
    out: change both together."""
    down = Vector((-0.474, -0.88, -0.034)).normalized()  # the blade's direction (local +Y)
    back = Vector((0.0, 0.0, -1.0))
    z = (back - down * back.dot(down)).normalized()  # the flats face backwards
    x = down.cross(z)
    basis = Matrix((x, down, z)).transposed().to_4x4()
    guard = Vector((0.119, 1.396, -0.172))
    return Matrix.Translation(body_local(guard - down * (SWORD_GUARD - SWORD_GRIP))) @ basis


def blade_width(y):
    """The blade's half width along it: broad and straight, a shallow step where the point section begins."""
    base, tip = SWORD_GUARD - SWORD_GRIP, SWORD_TIP - SWORD_GRIP
    t = (y - base) / (tip - base)
    if t < 0.62:
        return 0.036 - 0.0036 * t
    if t < 0.66:
        return 0.0338 + (0.0402 - 0.0338) * (t - 0.62) / 0.04  # the step out to the leaf
    return 0.0402 * (1.0 - ((t - 0.66) / 0.34) ** 1.7)


def sword_shape(shape, hilt_only=False):
    """In the sword's own frame (Unity terms): grip middle at the origin, blade +Y, flats facing ±Z."""
    base, tip = SWORD_GUARD - SWORD_GRIP, SWORD_TIP - SWORD_GRIP
    if not hilt_only:
        # The blade: bevelled to both edges, each edge its own bright strip, a dark fuller down the middle.
        ys = [base + (tip - base) * k / 24 for k in range(25)]
        for side in (1, -1):
            def flat(w, z):
                return lambda y: Vector((side * blade_width(y) * w, y, z))
            ridge = 0.0055
            shape.loft([[Vector((side * blade_width(y) * 0.28, y, ridge)), Vector((side * blade_width(y) * 0.28, y, -ridge)),
                         Vector((side * blade_width(y) * 0.72, y, -ridge * 0.62)), Vector((side * blade_width(y) * 0.72, y, ridge * 0.62))] for y in ys[:-1]],
                       STEEL, cap_tip=Vector((0, tip, 0)))
            shape.loft([[Vector((side * blade_width(y) * 0.72, y, ridge * 0.62)), Vector((side * blade_width(y) * 0.72, y, -ridge * 0.62)),
                         Vector((side * blade_width(y), y, -0.0005)), Vector((side * blade_width(y), y, 0.0005))] for y in ys[:-1]],
                       EDGE, cap_tip=Vector((0, tip, 0)))
        fuller_end = base + (tip - base) * 0.74
        shape.loft([[Vector((0.009 * s, y, z)) for s, z in ((1, 0.0052), (1, -0.0052), (-1, -0.0052), (-1, 0.0052))]
                    for y in (base, (base + fuller_end) / 2, fuller_end)], FULLER, cap_tip=Vector((0, fuller_end + 0.04, 0)))
        shape.loft([[Vector((blade_width(base) * 0.3 * s, base, z)) for s, z in ((1, 0.0056), (1, -0.0056), (-1, -0.0056), (-1, 0.0056))],
                    [Vector((blade_width(base + 0.02) * 0.3 * s, base + 0.02, z)) for s, z in ((1, 0.0056), (1, -0.0056), (-1, -0.0056), (-1, 0.0056))]], STEEL)
    # The guard: a bronze collar with two short down-swept quillons.
    shape.loft([[Vector((0.04 * s, y, 0.017 * z)) for s, z in ((1, 1), (1, -1), (-1, -1), (-1, 1))] for y in (base - 0.012, base + 0.02)], GOLD)
    for s in (1, -1):
        shape.loft([[Vector((x * s, y + o, 0.011 * z)) for o, z in ((0.008, 1), (0.008, -1), (-0.008, -1), (-0.008, 1))]
                    for x, y in ((0.035, base + 0.002), (0.062, base - 0.008), (0.088, base - 0.03))], GOLD)
    # The grip: red leather wound round, in ridges.
    bottom, top = -SWORD_GRIP + 0.03, base - 0.012
    for k in range(11):
        a, b = bottom + (top - bottom) * k / 11, bottom + (top - bottom) * (k + 1) / 11
        shape.cylinder(Vector((0, (a + b) / 2, 0)), 0.0155, (b - a) * 0.86, WRAP, top=0.0145, segs=10)
    shape.cylinder(Vector((0, (bottom + top) / 2, 0)), 0.013, top - bottom, WRAP, segs=8)
    # The pommel: a flattened bronze cap, wider than the grip.
    shape.sphere(Vector((0, -SWORD_GRIP + 0.016, 0)), 0.024, GOLD, squash=(1.5, 0.8, 0.9))


def scabbard_shape(shape):
    """A straight leather sheath the blade's shape, bronze at the mouth and the tip."""
    base, tip = SWORD_GUARD - SWORD_GRIP + 0.012, SWORD_TIP - SWORD_GRIP + 0.012
    def ring(y, n=10):
        w = max(blade_width(min(y, tip - 0.03)), 0.006) + 0.006
        return [Vector((w * math.cos(a), y, 0.012 * math.sin(a))) for a in (math.tau * k / n for k in range(n))]
    ys = [base + (tip - base) * k / 16 for k in range(17)]
    shape.loft([ring(y) for y in ys], SAYA, cap_tip=Vector((0, tip + 0.02, 0)))
    shape.loft([[p * 1.0 + Vector((0, 0, 0)) for p in ring(base - 0.002)], ring(base + 0.04)], GOLD)


def build_sword(root):
    bare = Shape()
    sword_shape(bare)
    bare.build("Weaver_Sword", root)
    # On the back: the sword in its sheath (the whole body), and apart for first person, so it can leave the sheath.
    frame = back_frame()
    hilt = Shape()
    sword_shape(hilt, hilt_only=True)
    hilt.build("Outfit_Sword_Hilt", root, frame)
    sheath = Shape()
    scabbard_shape(sheath)
    sheath.build("Outfit_Sword_Scabbard", root, frame)
    worn = Shape()
    scabbard_shape(worn)
    sword_shape(worn, hilt_only=True)
    worn.build("Avatar_Sword", root, frame)


# ---------------------------------------------------------------- run


def main():
    root = bpy.data.objects.new("Weaver", None)
    bpy.context.scene.collection.objects.link(root)
    build_body(root)
    build_outfit(root)
    build_sword(root)

    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=False, object_types={"EMPTY", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
    base = os.path.splitext(OUT_FBX)[0]
    with open(base + ".materials.json", "w") as f:
        json.dump({"materials": list(MATERIALS.values())}, f, indent=1)
    to_unity_axes = TO_BLENDER.to_3x3().inverted()
    placements = []
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        loc, rot, scale = obj.matrix_world.decompose()
        q = (to_unity_axes @ rot.to_matrix() @ TO_BLENDER.to_3x3()).to_quaternion()
        placements.append({"name": obj.name, "position": list(to_unity_axes @ loc), "rotation": [q.x, q.y, q.z, q.w], "scale": scale.x})
    with open(base + ".placements.json", "w") as f:
        json.dump({"objects": placements}, f, indent=1)
    deps = bpy.context.evaluated_depsgraph_get()
    tris = sum(sum(len(p.vertices) - 2 for p in o.evaluated_get(deps).data.polygons) for o in bpy.data.objects if o.type == "MESH")
    print(f"[Weaver] {len([o for o in bpy.data.objects if o.type == 'MESH'])} objects, ~{tris} triangles after decimation")
    print(f"[Weaver] wrote {OUT_FBX}")


main()
