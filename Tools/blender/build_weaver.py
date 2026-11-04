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
The outfit is the Weaver's armour, modelled here (no outside model): silver plate over dark padding, a white
tabard and half cape, gilt trim and a blue gem at the heart. Each piece is lofted from rings fitted round the boy's
own body (the furthest the body reaches in each direction round an axis, then pushed out), so it sits on him.

Objects (all under the root "Weaver", in Unity metres, 1.70 m tall):
- Body_Head, Body_Arms, Body_Torso, Body_Feet: the boy in his own clothes. Each outfit part that forms replaces
  one of them (sleeves → arms, boots → feet, coat → torso); his head stays his own.
- Avatar_Sleeve, Avatar_Boots, Avatar_Coat, Avatar_Scarf, Avatar_Cape, Avatar_Sword: the armour on the whole figure
  (sleeves: padded arms, elbow guards, vambraces and gauntlets; boots: boots, greaves, knee guards; coat: padding,
  breastplate, belt, faulds, tabard; Scarf: the gorget, collar and pauldrons; cape: the half cape), the sword
  sheathed across his back.
- Outfit_Sleeve_L/R, Outfit_Boots, Outfit_Coat, Outfit_Cape, Outfit_Sword_Hilt/_Scabbard: the parts
  fastened to the player in first person (no head, collar, upper arms or hands). Sleeves: anchor at the wrist, the sleeve runs back
  up the arm (-Z), the back of the hand is +Y.
- Weaver_Sword: the bare sword (a broad straight short sword, bronze-hilted with a red wrapped grip), pivot in the middle of the grip, blade up (+Y),
  flats facing ±Z, the edge's curve towards +X.
The sheath hangs diagonally across the back: the hilt over the right shoulder (to draw it with the right hand), the
tip at the left hip, over the backplate and in front of the cape.
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


# The armour. WarriorOutfit tints the cloth and leather by name for each appearance (OutfitCoat, OutfitTrim,
# OutfitBoots, OutfitCape, OutfitScarf); the plate, gilt and gem keep their own colours.
COAT = material("OutfitCoat", (0.9, 0.89, 0.86), 0.85, tex="Fabric061", tile=0.25)     # the tabard
TRIM = material("OutfitTrim", (0.12, 0.115, 0.135), 0.9, tex="Fabric061", tile=0.25)   # the padding under the plate
BOOTS = material("OutfitBoots", (0.13, 0.095, 0.075), 0.6, tex="Leather037", tile=0.2)  # boots and gloves
CAPE = material("OutfitCape", (0.88, 0.88, 0.9), 0.85, tex="Fabric061", tile=0.3)
SCARF = material("OutfitScarf", (0.86, 0.86, 0.84), 0.85, tex="Fabric061", tile=0.2)   # the standing collar
PLATE = material("OutfitSteel", (0.74, 0.76, 0.8), 0.3, 1.0, tex="Metal063", tile=0.25)
GILT = material("OutfitGilt", (0.86, 0.66, 0.3), 0.3, 1.0, tex="Metal063", tile=0.1)
BELT = material("OutfitBelt", (0.1, 0.07, 0.05), 0.55, tex="Leather037", tile=0.15)
GEM = material("OutfitGem", (0.2, 0.55, 1.0), 0.1, emission=(0.15, 0.5, 1.0))
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


def make_part(name, items, positions, root, to_local, decimate=6000):
    """One object from [(face, material)], vertices through to_local(Unity point) → local Unity point."""
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
            f = bm.faces.new(list(reversed(vs)))  # TO_BLENDER mirrors: reverse the winding to keep faces outward
        except ValueError:
            continue  # a duplicate face (or a corner used twice)
        if mat not in mats:
            mats.append(mat)
        f.material_index = mats.index(mat)
        f.smooth = True
        if face_uv is not None and None not in face_uv:
            for loop, t in zip(f.loops, reversed(face_uv)):  # the model's own UVs (corners reversed with the face)
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
        make_part(name, items, unity, root, body_local)


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
HOOD = lambda v: (v[:, 1] > 0.72) & (v[:, 2] < -0.075)  # the boy's hood, lying on his back: not the body


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


def chest_out(v, t):
    """The breastplate: rounded out in front (most at the chest, little at the waist), a ridge down the middle."""
    f = max(0.0, v[2])  # (OBJ +z is the front)
    return (0.3 + 0.7 * t) * (0.011 * f * f + 0.005 * max(0.0, f - 0.94) / 0.06)


def coat(shape):
    # The padding: the whole torso, to the neck.
    shape.sheet(torso(0.385, 0.845, 30, 0.004), TRIM, closed=True, cap=True)
    # The breastplate and backplate, one shell, with gilt at its edges and the sun and gem on the chest.
    plate = torso(0.57, 0.785, 15, 0.016, chest_out, shoulders=0.165)  # (its top under the gorget, its foot under the belt)
    shape.sheet(plate, PLATE, closed=True, thickness=0.005)
    heart = plate[10]
    centre = max(heart, key=lambda p: p.z) + Vector((0.0, 0.0, 0.007))
    shape.sphere(centre, 0.034, GILT, squash=(1, 1, 0.22), segs=16)
    for k in range(12):  # rays of the sun round the gem
        a = k / 12 * math.tau
        shape.sphere(centre + Vector((math.cos(a), math.sin(a), 0.0)) * 0.047, 0.008 if k % 2 else 0.011, GILT,
                     squash=(1, 1, 0.5), segs=8)
    shape.sphere(centre + Vector((0.0, 0.0, 0.007)), 0.016, GEM, squash=(1, 1.25, 0.6), segs=12)
    # The belt and its buckle.
    belt = torso(0.548, 0.588, 3, 0.03)
    shape.sheet(belt, BELT, closed=True, thickness=0.004)
    front = max(belt[1], key=lambda p: p.z)
    shape.box(front + Vector((0.0, 0.0, 0.006)), (0.05, 0.04, 0.012), GILT)
    # The faulds: three plates round the hips under the belt, each flaring out over the next.
    for k, (top, bottom) in enumerate(((0.555, 0.51), (0.525, 0.48), (0.495, 0.45))):
        lame = facing(torso(bottom, top, 4, 0.02 + 0.007 * k, lambda v, t: 0.008 * (1.0 - t)), FRONT, 118)
        shape.sheet(lame, PLATE, thickness=0.004)
        shape.rim(lame[0], 0.004, GILT)
    # The legs' padding, from the knees into the torso.
    for side in (1, -1):
        ys = steps(0.2, 0.44, 14)
        shape.sheet(rings_round([middle(y, 0.065 * side, 0.05) for y in ys], [UP] * len(ys), (1, 0, 0), 20, 0.075, 0.005),
                    TRIM, closed=True, thickness=0.003)
    # The tabard: a front and a back panel hanging from under the faulds to the knees, edged and studded in steel.
    for facing_front in (True, False):
        rows, depth = [], None
        for k, y in enumerate(steps(0.5, 0.2, 14)):
            t = k / 13
            w = 0.068 + 0.03 * t
            v = body_vertices()
            s = v[(np.abs(v[:, 1] - y) < 0.01) & (np.abs(v[:, 0]) < w + 0.02)]
            z = (s[:, 2].max() + 0.05) if facing_front else (s[:, 2].min() - 0.03)  # (in front of the knee guards)
            depth = z if depth is None else (max(z, depth - 0.008) if facing_front else min(z, depth + 0.008))
            row = []
            for j in range(9):
                u = -1.0 + 2.0 * j / 8
                fold = 0.005 * t * math.sin(u * math.pi * 1.5)
                row.append(to_unity(Vector((u * w, y, depth + (fold if facing_front else -fold)))))
            rows.append(row)
        shape.sheet(rows, COAT, thickness=0.004)
        out = Vector((0.0, 0.0, 0.004 if facing_front else -0.004))
        shape.rim([r[0] + out for r in rows], 0.005, PLATE)
        shape.rim([r[-1] + out for r in rows], 0.005, PLATE)
        shape.rim([p + out for p in rows[-1]], 0.005, PLATE)
        if facing_front:
            for k in (2, 4, 6):
                shape.sphere(rows[k][4] + out * 1.5, 0.007, GILT, squash=(1, 1, 0.6), segs=8)


def boots(shape):
    for side in (1, -1):
        x0 = 0.065 * side
        ys = steps(0.004, 0.25, 22)
        shape.sheet(rings_round([middle(y, x0, 0.06) for y in ys], [UP] * len(ys), (1, 0, 0), 24, 0.092, 0.007),
                    BOOTS, closed=True, cap=True)
        ys = steps(0.222, 0.258, 3)  # the boot's turned-down top
        shape.sheet(rings_round([middle(y, x0, 0.06) for y in ys], [UP] * 3, (1, 0, 0), 24, 0.09, 0.017),
                    BOOTS, closed=True, thickness=0.004)
        # The greave down the shin, the knee guard over the knee, the plates over the toes.
        ys = steps(0.09, 0.235, 10)
        greave = facing(rings_round([middle(y, x0, 0.06) for y in ys], [UP] * len(ys), (1, 0, 0), 24, 0.09, 0.02), FRONT, 95)
        shape.sheet(greave, PLATE, thickness=0.005)
        shape.rim(greave[-1], 0.004, GILT)
        ys = steps(0.255, 0.335, 8)
        knee = facing(rings_round([middle(y, x0, 0.06) for y in ys], [UP] * len(ys), (1, 0, 0), 24, 0.08, 0.022,
                                  lambda v, t: 0.014 * math.sin(math.pi * t) * max(0.0, v[2])), FRONT, 85)
        shape.sheet(knee, PLATE, thickness=0.005)
        shape.rim([row[0] for row in knee], 0.004, GILT)
        shape.rim([row[-1] for row in knee], 0.004, GILT)
        ys = steps(0.028, 0.075, 5)
        toes = facing(rings_round([middle(y, x0, 0.06) for y in ys], [UP] * len(ys), (1, 0, 0), 24, 0.092, 0.012), FRONT, 55)
        shape.sheet(toes, PLATE, thickness=0.004)


def forearm(shape, side):
    """The padded forearm and the vambrace over it (also the first-person sleeve)."""
    centres, dirs = along_arm(side, 1.0, 2.0, 12)
    shape.sheet(rings_round(centres, dirs, (side, 0, 0), 16, 0.065, 0.005), TRIM, closed=True, thickness=0.003)
    centres, dirs = along_arm(side, 1.25, 2.0, 10)
    vambrace = rings_round(centres, dirs, (side, 0, 0), 16, 0.065, 0.015, lambda v, t: 0.008 * t * t)
    shape.sheet(vambrace, PLATE, closed=True, thickness=0.004)
    shape.rim(vambrace[0], 0.004, GILT, closed=True)
    shape.rim(vambrace[-1], 0.004, GILT, closed=True)


def sleeves(shape):
    for side in (1, -1):
        centres, dirs = along_arm(side, 0.1, 1.05, 12)
        shape.sheet(rings_round(centres, dirs, (side, 0, 0), 16, 0.065, 0.005), TRIM, closed=True, thickness=0.003)
        forearm(shape, side)
        # The elbow guard: a cup over the back of the elbow.
        centres, dirs = along_arm(side, 0.85, 1.2, 7)
        couter = facing(rings_round(centres, dirs, (side, 0, 0), 16, 0.065, 0.017,
                                    lambda v, t: 0.013 * math.sin(math.pi * t)), Vector((side * -0.5, 0.0, -1.0)), 100)
        shape.sheet(couter, PLATE, thickness=0.004)
        # The gauntlet: a leather glove, a flared steel cuff, a plate over the back of the hand.
        centres, dirs = along_arm(side, 2.0, 2.97, 10)
        shape.sheet(rings_round(centres, dirs, (side, 0, 0), 16, 0.06, 0.004), BOOTS, closed=True, cap=True)
        centres, dirs = along_arm(side, 1.9, 2.3, 5)
        cuff = rings_round(centres, dirs, (side, 0, 0), 16, 0.06, 0.017, lambda v, t: 0.01 * t)
        shape.sheet(cuff, PLATE, closed=True, thickness=0.004)
        shape.rim(cuff[-1], 0.004, GILT, closed=True)
        centres, dirs = along_arm(side, 2.3, 2.62, 5)
        shape.sheet(facing(rings_round(centres, dirs, (side, 0, 0), 16, 0.06, 0.011), Vector((-side, 0.0, 0.0)), 60),
                    PLATE, thickness=0.003)


CHIN = 0.024  # how much lower the gorget sits at the front (his chin is low)


def gorget_rings():
    """The neck guard: wide on the shoulders, closing up round the neck (y, half width, half depth, middle z); lower
    at the front, under the chin."""
    out = []
    for y, rx, rz, zc in ((0.772, 0.15, 0.112, -0.012), (0.792, 0.132, 0.1, -0.009), (0.812, 0.108, 0.088, -0.006),
                          (0.83, 0.084, 0.077, -0.003), (0.845, 0.069, 0.07, 0.0)):
        out.append([to_unity(Vector((rx * math.cos(a), y - CHIN * (y - 0.772) / 0.073 * max(0.0, math.sin(a)), zc + rz * math.sin(a))))
                    for a in (k / 28 * math.tau for k in range(28))])
    return out


def pauldron(shape, side):
    """Three overlapping plates over the shoulder, each a band of a dome, edged in gilt."""
    out = np.array((side, 0.0, 0.0))
    up = np.array(UP)
    arm = np.array(BOY_ARM[1]) - np.array(BOY_ARM[0])
    arm = np.array((arm[0] * side, arm[1], arm[2]))
    arm /= np.linalg.norm(arm)
    axis = up * 0.85 + out * 0.45
    axis /= np.linalg.norm(axis)
    across = out - axis * np.dot(out, axis)  # round the dome from its outer side, front and back alike
    across /= np.linalg.norm(across)
    fwd = np.cross(axis, across)
    top = np.array((0.158 * side, 0.742, -0.012))
    for k, (r, a0, a1) in enumerate(((0.078, 3, 52), (0.083, 38, 76), (0.088, 62, 98))):
        centre = top + arm * 0.022 * k
        rows = []
        for phi in steps(math.radians(a0), math.radians(a1), 7):
            row = []
            for psi in steps(math.radians(-115), math.radians(115), 15):
                around = across * math.cos(psi) + fwd * math.sin(psi)
                p = centre + r * (math.cos(phi) * axis + math.sin(phi) * around)
                row.append(to_unity(Vector(p.tolist())))
            rows.append(row)
        shape.sheet(rows, PLATE, thickness=0.006)
        shape.rim(rows[-1], 0.005, GILT)


def scarf(shape):
    rings = gorget_rings()
    shape.sheet(rings, PLATE, closed=True, thickness=0.005)
    shape.rim(rings[0], 0.006, GILT, closed=True)
    top = [to_unity(Vector((0.066 * math.cos(a), 0.858 - (CHIN + 0.012) * max(0.0, math.sin(a)), 0.001 + 0.068 * math.sin(a))))
           for a in (k / 28 * math.tau for k in range(28))]
    shape.sheet([rings[-1], top], SCARF, closed=True, thickness=0.004)
    shape.rim(top, 0.004, GILT, closed=True)
    for side in (1, -1):
        pauldron(shape, side)


def cape(shape):
    """A half cape from the backs of the shoulders to the calves, falling in folds, gilt at the top and hem."""
    rows = []
    for t in steps(0.0, 1.0, 16):
        y = 0.8 - 0.63 * t
        w = 0.17 + 0.09 * t
        row = []
        for s in steps(-1.0, 1.0, 15):
            z = -0.118 - 0.07 * t + s * s * (0.07 - 0.02 * t) + 0.014 * t * math.sin(s * math.pi * 3.0)
            row.append(to_unity(Vector((s * w, y, z))))
        rows.append(row)
    shape.sheet(rows, CAPE, thickness=0.005)
    shape.rim(rows[0], 0.006, GILT)
    shape.rim(rows[-1], 0.005, GILT)


def build_outfit(root):
    """The armour: each part twice, on the whole figure (Avatar_*) and worn in first person (Outfit_*)."""
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
