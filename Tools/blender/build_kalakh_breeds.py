"""
Builds the Kalakh breeds: four new monsters for the monster world's hunt, each on the Kalakh's own skeleton, so
KalakhBody's poses and the fifty-odd moves (KalakhMoves) drive them as they drive the Kalakh:

    blender -b --disable-autoexec --python Tools/blender/build_kalakh_breeds.py [-- Breed ...]

- Cinderfang: lean and fast, a jackal's long fanged snout, a spiky mane down the neck and back, a barbed whip tail.
- Ravager: a hulking brute, huge hunched shoulders and forearms, a small low tusked head, bony plates on its back,
  a short clubbed tail.
- DreadWarden: a tall armoured sentinel: carapace like a knight's armour (breastplate, pauldrons, greaves), a horned
  helm of a skull, a bony shield on the left forearm, a bladed tail.
- Warlord: the biggest: a crown of swept-back horns, spiked pauldrons, ragged wings folded on its back like a cape, a
  heavy jaw, a long spiked tail ending in a blade.

Two more, made for the monster world's fight, each with its own colours (PALETTES):
- Ashhorn: the Kalakh's soldiers. Gaunt and hunched, arms hanging past the knees, a stag's skull for a face under tall
  branching antlers, a collar of ribs, a blood-red waist cloth.
- DemonLord: the boss. A towering crimson demon in black plate: a great crescent of horns, four blades fanning from the
  shoulder blades, burning seams down the chest, a bladed tail.

All black (obsidian skin and horn) with a few fire cracks, and eyes on fire. Built like rig_kalakh.py's Kalakh: 1 m
tall, z up, facing -y, +x the monster's own left, feet at z = 0. Same 22 bones (Hips, Spine, Chest, Neck, Head,
UpperArm/Forearm/Hand/Thigh/Shin/Foot .L/.R, Tail1..Tail5), each breed with its own proportions. The body is sculpted
from overlapping volumes (voxel remeshed into one skin) and skinned by bone heat; horns, spikes, plates and claws are
rigid pieces riding the skin under them. The eyes are separate objects, Eye.L and Eye.R, parented to the Head bone.

Writes Assets/_Project/Art/Characters/Kalakh/Breeds/<Breed>.fbx and <Breed>.materials.json (materials <Breed>_Body,
<Breed>_Horn, <Breed>_Embers, <Breed>_Eyes).
"""

import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(PROJECT, "Assets/_Project/Art/Characters/Kalakh/Breeds")

# The Kalakh's joints (rig_kalakh.py); each breed moves some of them.
BASE_J = {
    "root": (0.0, 0.17, 0.0),
    "hips": (0.0, 0.18, 0.43), "spine": (0.0, 0.15, 0.55), "chest": (0.0, 0.12, 0.66), "neck": (0.0, 0.05, 0.78),
    "head": (0.0, 0.0, 0.81), "head_end": (0.0, -0.15, 0.86),
    "shoulder.L": (0.2, 0.1, 0.69), "elbow.L": (0.285, 0.13, 0.52), "wrist.L": (0.33, 0.03, 0.37), "claw.L": (0.34, -0.03, 0.2),
    "hip.L": (0.095, 0.17, 0.43), "knee.L": (0.19, 0.0, 0.24), "hock.L": (0.2, 0.13, 0.14), "toe.L": (0.21, -0.04, 0.02),
    "tail0": (0.0, 0.25, 0.42), "tail1": (0.0, 0.36, 0.25), "tail2": (0.0, 0.53, 0.1), "tail3": (-0.02, 0.76, 0.09),
    "tail4": (-0.08, 0.88, 0.25), "tail5": (-0.15, 0.91, 0.47),
}

# (bone, head joint, tail joint, parent): exactly rig_kalakh.py's.
BONES = [
    ("Hips", "hips", "spine", None),
    ("Spine", "spine", "chest", "Hips"),
    ("Chest", "chest", "neck", "Spine"),
    ("Neck", "neck", "head", "Chest"),
    ("Head", "head", "head_end", "Neck"),
    ("Tail1", "tail0", "tail1", "Hips"),
    ("Tail2", "tail1", "tail2", "Tail1"),
    ("Tail3", "tail2", "tail3", "Tail2"),
    ("Tail4", "tail3", "tail4", "Tail3"),
    ("Tail5", "tail4", "tail5", "Tail4"),
]
for _s in ("L", "R"):
    BONES += [
        (f"UpperArm.{_s}", f"shoulder.{_s}", f"elbow.{_s}", "Chest"),
        (f"Forearm.{_s}", f"elbow.{_s}", f"wrist.{_s}", f"UpperArm.{_s}"),
        (f"Hand.{_s}", f"wrist.{_s}", f"claw.{_s}", f"Forearm.{_s}"),
        (f"Thigh.{_s}", f"hip.{_s}", f"knee.{_s}", "Hips"),
        (f"Shin.{_s}", f"knee.{_s}", f"hock.{_s}", f"Thigh.{_s}"),
        (f"Foot.{_s}", f"hock.{_s}", f"toe.{_s}", f"Shin.{_s}"),
    ]

BODY, HORN, EMBERS, EYES, ANTLER, DARK = range(6)
EYE_COLOR, EYE_GLOW = (1.0, 0.45, 0.08), (6.0, 2.2, 0.3)


def joints(over):
    """The breed's joints: the Kalakh's, with `over` (left side only: the right mirrors it), as Vectors."""
    J = dict(BASE_J)
    J.update(over)
    for k in list(J):
        if k.endswith(".L"):
            x, y, z = J[k]
            J[k[:-2] + ".R"] = (-x, y, z)
    return {k: Vector(v) for k, v in J.items()}


# ---------------------------------------------------------------- materials


# The breeds that aren't black, in material order (name, colour, smoothness, glow).
PALETTES = {
    "Ashhorn": [("Body", (0.17, 0.09, 0.055), 0.3, None), ("Horn", (0.72, 0.68, 0.55), 0.3, None),
                ("Embers", (0.42, 0.035, 0.03), 0.25, None), ("Eyes", (1.0, 0.25, 0.05), 0.3, (6.0, 1.2, 0.15)),
                ("Antler", (0.32, 0.15, 0.07), 0.4, None), ("Dark", (0.012, 0.01, 0.01), 0.1, None)],
    "DemonLord": [("Body", (0.34, 0.03, 0.018), 0.5, None), ("Horn", (0.025, 0.018, 0.018), 0.6, None),
                  ("Embers", (1.0, 0.22, 0.04), 0.4, (3.4, 0.7, 0.06)), ("Eyes", (1.0, 0.3, 0.04), 0.3, (8.0, 1.8, 0.2)),
                  ("Antler", (0.06, 0.02, 0.018), 0.55, None), ("Dark", (0.012, 0.01, 0.01), 0.1, None)],
}


def make_materials(breed, horn=0.06, body_smooth=0.42):
    defs = [
        (f"{breed}_Body", (0.03, 0.028, 0.03), body_smooth, 0.0, None),
        (f"{breed}_Horn", (horn, horn * 0.95, horn * 0.92), 0.55, 0.0, None),
        (f"{breed}_Embers", (1.0, 0.38, 0.06), 0.4, 0.0, (2.6, 0.75, 0.08)),
        (f"{breed}_Eyes", EYE_COLOR, 0.3, 0.0, EYE_GLOW),
    ]
    if breed in PALETTES:
        defs = [(f"{breed}_{n}", c, sm, 0.0, g) for n, c, sm, g in PALETTES[breed]]
    mats, entries = [], []
    for name, color, smooth, metal, glow in defs:
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        m.diffuse_color = (*color, 1.0)
        m.roughness = 1.0 - smooth
        m.metallic = metal
        style(m, color, 1.0 - smooth, glow)
        mats.append(m)
        entries.append({"name": name, "color": list(color), "smoothness": smooth, "metallic": metal, "alpha": 1.0,
                        "emission": list(glow) if glow else [0.0, 0.0, 0.0], "texture": "", "tile": 1.0,
                        "emissive": glow is not None})
    return mats, entries


def style(m, color, roughness, glow):
    """Principled BSDF settings (for the preview renders; Unity builds its own from the .materials.json)."""
    try:
        m.use_nodes = True
    except Exception:
        pass
    nodes = m.node_tree.nodes if m.node_tree else None
    bsdf = nodes.get("Principled BSDF") if nodes else None
    if bsdf is None:
        return
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    if glow:
        strength = max(glow)
        bsdf.inputs["Emission Color"].default_value = (glow[0] / strength, glow[1] / strength, glow[2] / strength, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength


# ---------------------------------------------------------------- primitives (Blender coordinates)


def V(x, y, z):
    return Vector((x, y, z))


def catmull(pts, per=4):
    """A smooth curve through pts (Catmull-Rom), `per` points per span."""
    pts = [Vector(p) for p in pts]
    if len(pts) < 3:
        return pts
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for k in range(per):
            t = k / per
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t +
                              (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t))
    out.append(pts[-1])
    return out


def lerp_list(vals, n):
    """Resamples a list of radii to n values."""
    if not isinstance(vals, (list, tuple)):
        return [vals] * n
    xs = np.linspace(0, 1, len(vals))
    return list(np.interp(np.linspace(0, 1, n), xs, vals))


def tube(bm, pts, radii, mat, sides=8, flat=1.0, up=None, caps=True):
    """A tube along pts with per-point radii (0 = a point). flat squashes the section across `up`."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    radii = lerp_list(radii, n)
    tans = [(pts[min(n - 1, i + 1)] - pts[max(0, i - 1)]).normalized() for i in range(n)]
    ref = Vector(up) if up is not None else (V(0, 0, 1) if abs(tans[0].z) < 0.9 else V(0, -1, 0))
    nrm = ref - tans[0] * ref.dot(tans[0])
    rings = []
    for i, p in enumerate(pts):
        t = tans[i]
        nrm = nrm - t * nrm.dot(t)
        if nrm.length < 1e-6:
            nrm = t.orthogonal()
        nrm.normalize()
        b = t.cross(nrm)
        r = radii[i]
        if r <= 1e-6:
            rings.append([bm.verts.new(p)])
        else:
            rings.append([bm.verts.new(p + nrm * math.cos(a) * r + b * math.sin(a) * r * flat)
                          for a in (k / sides * math.tau for k in range(sides))])
    faces = []
    for r0, r1 in zip(rings, rings[1:]):
        if len(r0) == 1 and len(r1) == 1:
            continue
        for k in range(sides):
            k1 = (k + 1) % sides
            if len(r0) == 1:
                faces.append(bm.faces.new((r0[0], r1[k], r1[k1])))
            elif len(r1) == 1:
                faces.append(bm.faces.new((r0[k], r0[k1], r1[0])))
            else:
                faces.append(bm.faces.new((r0[k], r0[k1], r1[k1], r1[k])))
    if caps:
        for ring in (rings[0], rings[-1]):
            if len(ring) > 1:
                faces.append(bm.faces.new(ring))
    for f in faces:
        f.material_index = mat
    return rings


def align(direction, up=V(0, 0, 1)):
    """A rotation taking local +z to `direction` (local +y towards `up`)."""
    d = Vector(direction).normalized()
    u = Vector(up) - d * Vector(up).dot(d)
    if u.length < 1e-6:
        u = d.orthogonal()
    u.normalize()
    return Matrix((tuple(u.cross(d)), tuple(u), tuple(d))).transposed()


def ellipsoid(bm, c, r, mat, u=14, v=10, rot=None):
    res = bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    R = (rot if rot is not None else Matrix.Identity(3)).to_4x4()
    M = Matrix.Translation(Vector(c)) @ R @ Matrix.Diagonal((r[0], r[1], r[2], 1.0))
    bmesh.ops.transform(bm, matrix=M, verts=res["verts"])
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = mat
    return res["verts"]


def spike(bm, base, tip, r, mat, bend=None, sides=5):
    """A horn or claw: from base (radius r) to a point at tip, bowed by `bend` (a vector, at the middle)."""
    base, tip = Vector(base), Vector(tip)
    mid = base.lerp(tip, 0.5) + (Vector(bend) if bend is not None else V(0, 0, 0))
    pts = catmull([base, mid, tip], 2)
    tube(bm, pts, list(np.linspace(r, 0.0, len(pts))), mat, sides)


def shell(bm, c, r, rot, keep, thick, mat, u=16, v=11):
    """A curved plate: the part of an ellipsoid where keep(unit direction) holds, `thick` thick (inwards)."""
    before = set(bm.verts)
    res = bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    drop = [vt for vt in res["verts"] if not keep(vt.co.copy())]
    bmesh.ops.delete(bm, geom=drop, context="VERTS")
    kept = [vt for vt in bm.verts if vt not in before]
    faces = list({f for vt in kept for f in vt.link_faces})
    R = (rot if rot is not None else Matrix.Identity(3)).to_4x4()
    M = Matrix.Translation(Vector(c)) @ R @ Matrix.Diagonal((r[0], r[1], r[2], 1.0))
    bmesh.ops.transform(bm, matrix=M, verts=kept)
    if faces:
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        bmesh.ops.solidify(bm, geom=faces, thickness=thick)
    for f in {f for vt in bm.verts if vt not in before for f in vt.link_faces}:
        f.material_index = mat


def plate(bm, outline, origin, ux, uy, thick, mat, bulge=0.0):
    """A flat plate from a 2D outline in the plane (ux, uy) at origin, `thick` through, its middle domed by bulge."""
    origin, ux, uy = Vector(origin), Vector(ux).normalized(), Vector(uy).normalized()
    n = ux.cross(uy).normalized()
    cx = sum(p[0] for p in outline) / len(outline)
    cy = sum(p[1] for p in outline) / len(outline)
    reach = max(math.hypot(p[0] - cx, p[1] - cy) for p in outline) or 1.0
    top, bot = [], []
    for x, y in outline:
        d = 1.0 - (math.hypot(x - cx, y - cy) / reach) ** 2
        p = origin + ux * x + uy * y + n * bulge * d
        top.append(bm.verts.new(p + n * thick / 2))
        bot.append(bm.verts.new(p - n * thick / 2))
    centre_t = bm.verts.new(origin + ux * cx + uy * cy + n * (bulge + thick / 2))
    centre_b = bm.verts.new(origin + ux * cx + uy * cy + n * (bulge - thick / 2))
    fs = []
    k = len(outline)
    for i in range(k):
        j = (i + 1) % k
        fs.append(bm.faces.new((centre_t, top[i], top[j])))
        fs.append(bm.faces.new((centre_b, bot[j], bot[i])))
        fs.append(bm.faces.new((top[i], bot[i], bot[j], top[j])))
    for f in fs:
        f.material_index = mat


class Head:
    """The head's frame: x the monster's left, y forward (towards head_end), z up; origin at the head joint."""

    def __init__(self, J):
        self.o = J["head"]
        f = (J["head_end"] - J["head"]).normalized()
        u = V(0, 0, 1) - f * f.z
        u.normalize()
        s = u.cross(f)
        self.f, self.u, self.s = f, u, s
        self.R = Matrix((tuple(s), tuple(f), tuple(u))).transposed()

    def __call__(self, x, y, z):
        return self.o + self.s * x + self.f * y + self.u * z

    def d(self, x, y, z):
        return self.s * x + self.f * y + self.u * z


# ---------------------------------------------------------------- the body (shared)


class Build:
    """One breed under construction: `skin` gets the volumes that melt into one body; `parts` the rigid pieces."""

    def __init__(self, name, J):
        self.name, self.J = name, J
        self.skin = bmesh.new()
        self.parts = bmesh.new()
        self.layer = self.parts.verts.layers.int.new("piece")
        self.anchors = []  # per piece: (anchor point, follow the skin per vertex?)
        self.eyes = []      # (local head-frame position, outward direction) for the left eye; mirrored for the right
        self.veins = []     # polylines on the skin for ember cracks

    def piece(self, anchor, skin=False, snap=None):
        """Everything added to `parts` until the next piece() rides the skin nearest `anchor` (skin: each vertex
        rides the skin nearest it instead). snap: a direction; the piece slides along it until its anchor sits on
        the finished skin (for spikes and plates on the back, wherever the sculpted back turns out to be)."""
        self._close()
        self.anchors.append((Vector(anchor), skin, Vector(snap).normalized() if snap is not None else None))
        self._mark = set(self.parts.verts)

    def _close(self):
        if self.anchors and hasattr(self, "_mark"):
            idx = len(self.anchors) - 1
            for vt in self.parts.verts:
                if vt not in self._mark:
                    vt[self.layer] = idx + 1
            self._mark = set(self.parts.verts)

    def finish_parts(self):
        self._close()


def torso(b, w=1.0, chest=(0.135, 0.11, 0.13), belly=(0.1, 0.085, 0.1), pelvis=(0.1, 0.085, 0.075), neck=0.05,
          hump=None, pecs=0.065):
    J, s = b.J, b.skin
    up = (J["chest"] - J["hips"]).normalized()
    ellipsoid(s, J["hips"] + V(0, -0.01, -0.01), (pelvis[0] * w, pelvis[1], pelvis[2]), BODY, rot=align(up, V(0, -1, 0)))
    ellipsoid(s, J["spine"].lerp(J["hips"], 0.3) + V(0, -0.02, 0), (belly[0] * w, belly[1], belly[2]), BODY,
              rot=align(up, V(0, -1, 0)))
    rib = J["spine"].lerp(J["chest"], 0.75) + V(0, -0.02, 0.0)
    ellipsoid(s, rib, (chest[0] * w, chest[1], chest[2]), BODY, rot=align((J["neck"] - J["spine"]), V(0, -1, 0)))
    for sd in (1, -1):
        sh = J[f"shoulder.{'L' if sd > 0 else 'R'}"]
        pec = J["chest"].lerp(sh, 0.45) + V(0, -0.07, -0.03)
        ellipsoid(s, pec, (pecs * 1.1 * w, pecs * 0.6, pecs), BODY, rot=align(V(0, -0.3, 1)))
        # Collar and shoulder: the arm grows out of the torso.
        tube(s, [J["chest"] + V(0, 0, 0.03), J["chest"].lerp(sh, 0.6) + V(0, 0, 0.02), sh], [0.05 * w, 0.05, 0.045], BODY, 8)
        ellipsoid(s, sh + V(sd * 0.01, 0, 0.01), (0.06 * w, 0.065, 0.06), BODY)
        # The lats: the back flares into the arms.
        ellipsoid(s, J["spine"].lerp(sh, 0.5) + V(0, 0.03, 0.0), (0.06 * w, 0.06, 0.1), BODY,
                  rot=align(sh - J["spine"]))
    tube(s, catmull([J["chest"] + V(0, 0.01, 0.0), J["neck"], J["head"]], 3), [neck * 1.5, neck * 1.1, neck * 0.9], BODY, 10)
    if hump:
        c, r = hump
        ellipsoid(s, c, r, BODY, rot=align(J["neck"] - J["spine"], V(0, 1, 0)))


def arm(b, side, ua=0.042, fa=0.04, hand=0.032, fingers=4, flen=1.0, claw=0.05, claw_r=0.008, bulk=1.0):
    J, s = b.J, b.skin
    S, E, W, C = (J[f"{k}.{side}"] for k in ("shoulder", "elbow", "wrist", "claw"))
    sd = 1 if side == "L" else -1
    tube(s, catmull([S, S.lerp(E, 0.45) + V(0, -0.01, 0), E], 2), [ua * 1.05, ua * 1.2 * bulk, ua * 0.8], BODY, 10)
    tube(s, catmull([E + (E - S).normalized() * -0.01, E.lerp(W, 0.35), W], 2), [fa * 0.85, fa * 1.15 * bulk, fa * 0.62], BODY, 10)
    # The hand: a palm and clawed fingers, curling a little towards the body.
    d = (C - W).normalized()
    across = d.cross(V(1, 0, 0)).normalized()  # along the knuckles
    if across.y > 0:
        across = -across
    inward = V(-sd, 0, 0)
    L = (C - W).length
    ellipsoid(s, W + d * L * 0.3, (hand * 0.75, hand * 1.2, L * 0.34), BODY, rot=align(d, across))
    tips = []
    for i, t in enumerate(np.linspace(-1, 1, fingers)):
        base = W + d * L * 0.5 + across * t * hand * 0.85
        tip = C + across * t * hand * 1.3 + inward * 0.012 + d * (flen - 1.0) * L
        mid = base.lerp(tip, 0.5) + inward * 0.012 * flen
        tube(s, [base, mid, tip], [hand * 0.36, hand * 0.3, hand * 0.22], BODY, 6)
        tips.append((tip, (tip - mid).normalized()))
    thumb_base = W + d * L * 0.25 + across * hand * 1.1
    thumb_tip = thumb_base + d * L * 0.45 + across * hand * 0.6 + inward * 0.01
    tube(s, [thumb_base, thumb_tip], [hand * 0.36, hand * 0.25], BODY, 6)
    tips.append((thumb_tip, (thumb_tip - thumb_base).normalized()))
    for tip, dirn in tips:
        b.piece(tip)
        spike(b.parts, tip - dirn * 0.004, tip + dirn * claw + inward * claw * 0.3, claw_r, HORN, bend=inward * claw * 0.12)


def leg(b, side, th=0.06, sh=0.034, foot=0.026, toes=3, claw=0.03):
    J, s = b.J, b.skin
    H, K, Hk, T = (J[f"{k}.{side}"] for k in ("hip", "knee", "hock", "toe"))
    sd = 1 if side == "L" else -1
    tube(s, catmull([H + V(0, 0.01, 0.02), H.lerp(K, 0.45) + V(sd * 0.01, 0.0, 0), K], 2), [th * 1.05, th * 1.15, th * 0.6], BODY, 10)
    ellipsoid(s, H.lerp(K, 0.35) + V(sd * 0.012, -0.015, 0), (th * 0.95, th * 1.0, th * 1.5), BODY, rot=align(K - H))
    tube(s, catmull([K, K.lerp(Hk, 0.4) + V(0, 0.01, 0), Hk], 2), [th * 0.62, sh * 1.1, sh * 0.85], BODY, 8)
    ellipsoid(s, K.lerp(Hk, 0.3) + V(0, 0.02, 0), (sh * 1.15, sh * 1.3, sh * 2.2), BODY, rot=align(Hk - K))  # calf
    tube(s, [Hk, Hk.lerp(T, 0.6), T + V(0, 0.01, 0.005)], [sh * 0.85, foot * 0.9, foot * 0.8], BODY, 8)
    fwd = V(0, -1, 0)
    for t in np.linspace(-1, 1, toes):
        base = T + V(t * foot * 1.0, 0.01, 0.0)
        tip = base + fwd * 0.05 + V(t * foot * 0.8, 0, -0.008)
        tip.z = max(tip.z, 0.012)
        tube(s, [base, base.lerp(tip, 0.5) + V(0, 0, 0.006), tip], [foot * 0.42, foot * 0.36, foot * 0.28], BODY, 6)
        b.piece(tip)
        spike(b.parts, tip - fwd * 0.004 + V(0, 0, 0.003), tip + fwd * claw + V(0, 0, -0.012), 0.008, HORN, bend=V(0, 0, 0.005))
    b.piece(Hk)  # a spur at the back of the hock
    spike(b.parts, Hk + V(0, 0.02, 0.0), Hk + V(sd * 0.01, 0.07, -0.03), 0.01, HORN, bend=V(0, 0, 0.01))


def tail(b, r0=0.06, r1=0.008, per=4):
    J = b.J
    pts = catmull([J[f"tail{i}"] for i in range(6)], per)
    pts = [J["hips"] + V(0, 0.02, 0)] + pts
    tube(b.skin, pts, list(np.linspace(r0, r1, len(pts)) ** 1.0), BODY, 10)
    return pts


def dorsal(b, n=8, start=0.0, end=1.0):
    """Points along the back (neck to tail root) with the outward (up/back) direction there."""
    J = b.J
    line = catmull([J["head"] + V(0, 0.03, 0.03), J["neck"] + V(0, 0.05, 0.03), J["chest"] + V(0, 0.09, 0.02),
                    J["spine"] + V(0, 0.08, 0.0), J["hips"] + V(0, 0.08, -0.02), J["tail0"] + V(0, 0.04, 0.02)], 6)
    out = []
    for t in np.linspace(start, end, n):
        i = min(int(t * (len(line) - 2)), len(line) - 2)
        p = line[i]
        tng = (line[i + 1] - line[i]).normalized()
        nrm = V(0, 0, 1) - tng * tng.z
        nrm = (nrm + V(0, 0.6, 0)).normalized()
        out.append((p, tng, nrm))
    return out


def snap_pieces(b, body):
    """Slides each snapping piece along its direction so its anchor sits on the skin."""
    bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    moves = {}
    for i, (anchor, _, snap) in enumerate(b.anchors):
        if snap is None:
            continue
        hit, _, _, _ = bvh.ray_cast(anchor + snap * 0.4, -snap, 0.8)
        if hit is not None:
            moves[i + 1] = snap * (hit - anchor).dot(snap)
            b.anchors[i] = (hit, b.anchors[i][1], snap)
    for vt in b.parts.verts:
        m = moves.get(vt[b.layer])
        if m is not None:
            vt.co += m


def brows(b, H, eye, r=0.008, lift=0.016):
    """A heavy ridge over each eye, low at the nose end (a scowl)."""
    x, y, z = eye
    for sd in (1, -1):
        b.piece(H(sd * x, y - 0.01, z + lift))
        tube(b.parts, [H(sd * (x - 0.026), y + 0.006, z + lift * 0.45), H(sd * (x - 0.004), y - 0.002, z + lift),
                       H(sd * (x + 0.022), y - 0.022, z + lift * 1.15)], [r * 0.6, r, r * 0.7], HORN, 6)


def vein(b, pts):
    b.veins.append([Vector(p) for p in pts])


# ---------------------------------------------------------------- the breeds


def cinderfang():
    J = joints({
        "hips": (0, 0.2, 0.45), "spine": (0, 0.12, 0.56), "chest": (0, 0.04, 0.65), "neck": (0, -0.05, 0.73),
        "head": (0, -0.11, 0.77), "head_end": (0, -0.33, 0.74),
        "shoulder.L": (0.15, 0.04, 0.67), "elbow.L": (0.21, 0.08, 0.49), "wrist.L": (0.24, -0.04, 0.33),
        "claw.L": (0.25, -0.12, 0.17),
        "hip.L": (0.085, 0.2, 0.44), "knee.L": (0.15, 0.06, 0.28), "hock.L": (0.16, 0.22, 0.13), "toe.L": (0.17, 0.08, 0.02),
        "tail0": (0, 0.28, 0.45), "tail1": (0, 0.42, 0.41), "tail2": (0, 0.6, 0.35), "tail3": (0.02, 0.8, 0.31),
        "tail4": (0.05, 1.0, 0.31), "tail5": (0.06, 1.18, 0.37),
    })
    b = Build("Cinderfang", J)
    torso(b, w=0.8, chest=(0.12, 0.1, 0.12), belly=(0.075, 0.07, 0.09), pelvis=(0.085, 0.075, 0.07), neck=0.042, pecs=0.05)
    for sd in "LR":
        arm(b, sd, ua=0.034, fa=0.03, hand=0.026, fingers=3, flen=1.15, claw=0.065, claw_r=0.007)
        leg(b, sd, th=0.05, sh=0.026, foot=0.022, claw=0.028)
    tpts = tail(b, 0.045, 0.006)
    # Barbs along the whip.
    for i in range(6, len(tpts) - 1, 3):
        p, q = tpts[i], tpts[i + 1]
        dirn = (q - p).normalized()
        r = 0.04 * (1 - i / len(tpts)) + 0.008
        for sd in (1, -1):
            side = dirn.cross(V(0, 0, 1)).normalized() * sd
            b.piece(p)
            spike(b.parts, p + side * r * 0.6, p + side * (r + 0.035) + dirn * 0.03 + V(0, 0, 0.01), 0.008, HORN)
    b.piece(tpts[-1])
    d = (tpts[-1] - tpts[-3]).normalized()
    spike(b.parts, tpts[-2], tpts[-1] + d * 0.09, 0.012, HORN)

    # The head: a long jackal snout, fangs, tall ears, a mouth of fire.
    H = Head(J)
    s = b.skin
    ellipsoid(s, H(0, 0.0, 0.02), (0.05, 0.065, 0.048), BODY, rot=H.R)
    tube(s, [H(0, 0.04, 0.015), H(0, 0.13, 0.0), H(0, 0.21, -0.012)], [0.038, 0.026, 0.016], BODY, 10, flat=1.25, up=H.u)
    tube(s, [H(0, 0.02, -0.035), H(0, 0.1, -0.05), H(0, 0.19, -0.05)], [0.026, 0.018, 0.011], BODY, 8, flat=0.8, up=H.u)
    ellipsoid(s, H(0, -0.01, -0.025), (0.045, 0.045, 0.035), BODY, rot=H.R)  # cheeks / jaw muscle
    b.piece(H(0, 0.0, 0.05))
    for sd in (1, -1):
        b.piece(H(sd * 0.035, -0.02, 0.05))
        tube(b.parts, [H(sd * 0.035, -0.015, 0.04), H(sd * 0.05, -0.04, 0.11), H(sd * 0.055, -0.07, 0.17)],
             [0.022, 0.014, 0.0], BODY, 6, flat=0.45, up=H.f)  # ears
        for k, y in enumerate((0.19, 0.15, 0.11)):
            b.piece(H(sd * 0.012, y, -0.015))
            spike(b.parts, H(sd * 0.013, y, -0.012), H(sd * 0.014, y + 0.004, -0.045 + k * 0.006), 0.0055, HORN)
            b.piece(H(sd * 0.01, y - 0.01, -0.045))
            spike(b.parts, H(sd * 0.01, y - 0.01, -0.048), H(sd * 0.011, y - 0.008, -0.02), 0.004, HORN)
    b.piece(H(0, 0.1, -0.03))
    ellipsoid(b.parts, H(0, 0.11, -0.032), (0.012, 0.07, 0.008), EMBERS, 8, 6, rot=H.R)  # fire in the throat
    b.eyes.append((V(0.031, 0.065, 0.024), V(0.7, 0.7, 0.15)))
    brows(b, H, (0.031, 0.06, 0.024), 0.006, 0.014)
    # The mane: a ruff of long spikes down the neck and back.
    for i, (p, tng, nrm) in enumerate(dorsal(b, 16, 0.0, 0.85)):
        L = 0.11 * (1.0 - 0.6 * i / 16) + 0.02
        for sd in (-1, 0, 1):
            if sd and i % 2:
                continue
            side = tng.cross(nrm).normalized() * sd
            b.piece(p, snap=nrm)
            spike(b.parts, p - nrm * 0.01 + side * 0.012, p + nrm * L * (0.8 if sd else 1.0) + tng * -L * 0.6 + side * L * 0.4,
                  0.011, HORN, bend=nrm * 0.01)
    vein(b, [J["chest"] + V(x, -0.11, z) for x, z in ((-0.05, 0.02), (-0.02, -0.03), (0.01, -0.06), (0.03, -0.1))])
    vein(b, [J["spine"] + V(x, -0.08, z) for x, z in ((0.05, 0.03), (0.02, -0.02), (0.04, -0.07))])
    return b


def ravager():
    J = joints({
        "hips": (0, 0.2, 0.42), "spine": (0, 0.13, 0.56), "chest": (0, 0.05, 0.69), "neck": (0, -0.05, 0.75),
        "head": (0, -0.11, 0.72), "head_end": (0, -0.25, 0.69),
        "shoulder.L": (0.25, 0.05, 0.73), "elbow.L": (0.36, 0.08, 0.5), "wrist.L": (0.38, -0.05, 0.24),
        "claw.L": (0.385, -0.1, 0.06),
        "hip.L": (0.11, 0.2, 0.42), "knee.L": (0.18, 0.05, 0.24), "hock.L": (0.18, 0.17, 0.1), "toe.L": (0.19, 0.03, 0.02),
        "tail0": (0, 0.28, 0.4), "tail1": (0, 0.37, 0.31), "tail2": (0, 0.46, 0.23), "tail3": (0, 0.55, 0.18),
        "tail4": (0, 0.63, 0.16), "tail5": (0, 0.71, 0.17),
    })
    b = Build("Ravager", J)
    torso(b, w=1.35, chest=(0.15, 0.13, 0.14), belly=(0.11, 0.1, 0.1), pelvis=(0.1, 0.09, 0.08), neck=0.07,
          hump=(J["chest"] + V(0, 0.05, 0.05), (0.17, 0.11, 0.1)), pecs=0.08)
    for sd in "LR":
        arm(b, sd, ua=0.065, fa=0.07, hand=0.045, fingers=4, flen=0.8, claw=0.035, claw_r=0.012, bulk=1.15)
        leg(b, sd, th=0.068, sh=0.04, foot=0.03, claw=0.025)
        side = 1 if sd == "L" else -1
        W, E = J[f"wrist.{sd}"], J[f"elbow.{sd}"]
        ellipsoid(b.skin, E.lerp(W, 0.45) + V(side * 0.015, 0.0, 0), (0.08, 0.075, 0.11), BODY, rot=align(W - E))
        # Bone knuckle-plates and elbow spikes.
        b.piece(W + V(side * 0.02, -0.03, -0.02))
        shell(b.parts, E.lerp(W, 0.5), (0.085, 0.082, 0.13), align(W - E, V(side, 0, 0)),
              lambda d: d.y > 0.25 and abs(d.z) < 0.75, 0.01, HORN)
        b.piece(E)
        spike(b.parts, E + V(side * 0.02, 0.04, 0), E + V(side * 0.06, 0.13, 0.04), 0.022, HORN, bend=V(0, 0, 0.02))
    tpts = tail(b, 0.07, 0.05, 3)
    club = tpts[-1]
    ellipsoid(b.skin, club, (0.075, 0.09, 0.07), BODY)
    for d in (V(1, 0.3, 0.4), V(-1, 0.3, 0.4), V(0, 0.5, 1), V(0, 1, 0.1), V(0.7, 0.3, -0.6), V(-0.7, 0.3, -0.6)):
        d = d.normalized()
        b.piece(club)
        spike(b.parts, club + d * 0.05, club + d * 0.13, 0.022, HORN)
    # A small low head with tusks under a heavy brow.
    H = Head(J)
    s = b.skin
    ellipsoid(s, H(0, 0.0, 0.0), (0.06, 0.065, 0.05), BODY, rot=H.R)
    tube(s, [H(0, -0.02, -0.04), H(0, 0.06, -0.05), H(0, 0.1, -0.04)], [0.06, 0.05, 0.032], BODY, 10, flat=0.75, up=H.u)  # jaw
    tube(s, [H(0, 0.02, 0.0), H(0, 0.09, -0.01)], [0.045, 0.032], BODY, 8, flat=0.8, up=H.u)  # muzzle
    brows(b, H, (0.028, 0.05, 0.012), 0.012, 0.018)
    for sd in (1, -1):
        b.piece(H(sd * 0.045, 0.07, -0.05))
        spike(b.parts, H(sd * 0.042, 0.08, -0.05), H(sd * 0.1, 0.12, 0.04), 0.017, HORN, bend=H.d(sd * 0.02, 0.03, -0.01))
        for y in (0.03, 0.0):
            b.piece(H(sd * 0.05, y, 0.03))
            spike(b.parts, H(sd * 0.05, y, 0.03), H(sd * 0.08, y - 0.06, 0.07), 0.012, HORN)
    b.piece(H(0, 0.09, -0.035))
    ellipsoid(b.parts, H(0, 0.085, -0.032), (0.03, 0.02, 0.006), EMBERS, 8, 5, rot=H.R)
    b.eyes.append((V(0.028, 0.055, 0.012), V(0.4, 0.9, 0.0)))
    # Bony plates down its back, overlapping like a shell.
    for i, (p, tng, nrm) in enumerate(dorsal(b, 6, 0.36, 0.95)):
        b.piece(p, skin=True, snap=nrm)
        sz = 0.11 - i * 0.006
        shell(b.parts, p - nrm * 0.08, (sz * 1.15, sz * 0.8, 0.11), align(nrm, -tng), lambda d: d.z > 0.3, 0.014, HORN)
        b.piece(p, snap=nrm)
        spike(b.parts, p + nrm * 0.015, p + nrm * 0.1 - tng * 0.06, 0.024, HORN, bend=nrm * 0.01)
        for sd in (1, -1):
            side = tng.cross(nrm).normalized() * sd
            b.piece(p + side * sz * 0.9 - nrm * 0.03, snap=nrm * 0.5 + side)
            spike(b.parts, p + side * sz * 0.9 - nrm * 0.03, p + side * (sz * 0.9 + 0.06) - tng * 0.03, 0.016, HORN)
    for sd in (1, -1):
        sh = J["shoulder.L" if sd > 0 else "shoulder.R"]
        vein(b, [sh + V(sd * x, -0.05, z) for x, z in ((0.0, 0.0), (-0.03, -0.05), (-0.01, -0.1))])
        W, E = J["wrist.L" if sd > 0 else "wrist.R"], J["elbow.L" if sd > 0 else "elbow.R"]
        vein(b, [E.lerp(W, t) + V(sd * 0.06, -0.06, 0) for t in (0.2, 0.45, 0.7)])
    vein(b, [J["chest"] + V(x, -0.14, z) for x, z in ((0.0, 0.0), (0.02, -0.06), (-0.01, -0.12), (0.02, -0.18))])
    return b


def dread_warden():
    J = joints({
        "hips": (0, 0.12, 0.48), "spine": (0, 0.11, 0.61), "chest": (0, 0.08, 0.73), "neck": (0, 0.05, 0.84),
        "head": (0, 0.02, 0.87), "head_end": (0, -0.12, 0.9),
        "shoulder.L": (0.17, 0.08, 0.78), "elbow.L": (0.23, 0.1, 0.6), "wrist.L": (0.26, 0.0, 0.45),
        "claw.L": (0.27, -0.05, 0.3),
        "hip.L": (0.09, 0.12, 0.47), "knee.L": (0.13, 0.0, 0.27), "hock.L": (0.14, 0.12, 0.12), "toe.L": (0.15, -0.03, 0.02),
        "tail0": (0, 0.18, 0.46), "tail1": (0, 0.3, 0.38), "tail2": (0, 0.46, 0.29), "tail3": (0, 0.64, 0.22),
        "tail4": (0, 0.8, 0.2), "tail5": (0, 0.95, 0.23),
    })
    b = Build("DreadWarden", J)
    torso(b, w=0.95, chest=(0.13, 0.1, 0.13), belly=(0.085, 0.075, 0.09), pelvis=(0.095, 0.08, 0.07), neck=0.045, pecs=0.06)
    for sd in "LR":
        arm(b, sd, ua=0.04, fa=0.037, hand=0.03, fingers=4, flen=1.0, claw=0.04, claw_r=0.008)
        leg(b, sd, th=0.058, sh=0.032, foot=0.026, claw=0.026)
    tpts = tail(b, 0.05, 0.012)
    # The armour.
    up = (J["neck"] - J["spine"]).normalized()
    rib = J["spine"].lerp(J["chest"], 0.75) + V(0, -0.02, 0)
    b.piece(rib + V(0, -0.1, 0), skin=True)
    shell(b.parts, rib + V(0, 0.015, 0.0), (0.14, 0.115, 0.155), align(up, V(0, -1, 0)), lambda d: d.y > 0.05 and d.z > -0.75, 0.014, HORN)  # breastplate
    keel = [rib + align(up, V(0, -1, 0)) @ V(0, 0.122 * math.cos(a), 0.155 * math.sin(a)) + V(0, 0.015, 0) for a in np.linspace(-0.7, 1.1, 6)]
    tube(b.parts, keel, [0.006, 0.012, 0.014, 0.014, 0.012, 0.006], HORN, 5)  # its keel
    b.piece(J["spine"] + V(0, -0.1, -0.03), skin=True)
    for k in range(3):  # the faulds: bands over the belly
        c = J["spine"].lerp(J["hips"], 0.3 + k * 0.3) + V(0, -0.005, 0)
        shell(b.parts, c, (0.11, 0.095, 0.05), align(up, V(0, -1, 0)), lambda d: d.y > 0.1 and abs(d.z) < 0.6, 0.012, HORN)
    for sd, name in ((1, "L"), (-1, "R")):
        S, E, W = J[f"shoulder.{name}"], J[f"elbow.{name}"], J[f"wrist.{name}"]
        b.piece(S + V(sd * 0.05, 0, 0.04))
        shell(b.parts, S + V(sd * 0.01, 0, 0.0), (0.085, 0.085, 0.075), align(V(sd, 0, 0.6), V(0, -1, 0)), lambda d: d.z > -0.1, 0.012, HORN)
        b.piece(S + V(sd * 0.04, 0, 0.08))
        for k in range(3):
            spike(b.parts, S + V(sd * (0.04 + k * 0.012), -0.03 + k * 0.03, 0.07), S + V(sd * (0.07 + k * 0.02), 0.0 + k * 0.03, 0.15 - k * 0.02), 0.012, HORN)
        b.piece(E.lerp(W, 0.5), skin=True)
        shell(b.parts, E.lerp(W, 0.5), (0.05, 0.05, 0.085), align(W - E, V(sd, 0, 0)), lambda d: d.y > -0.2, 0.01, HORN)  # vambrace
        K, Hk = J[f"knee.{name}"], J[f"hock.{name}"]
        b.piece(K.lerp(Hk, 0.4), skin=True)
        shell(b.parts, K.lerp(Hk, 0.4) + V(0, -0.01, 0), (0.05, 0.05, 0.1), align(Hk - K, V(0, -1, 0)), lambda d: d.y > -0.1, 0.01, HORN)  # greave
        b.piece(K)
        shell(b.parts, K + V(0, -0.012, 0.0), (0.04, 0.042, 0.045), None, lambda d: d.y < -0.1, 0.01, HORN)  # knee cop
        spike(b.parts, K + V(0, -0.048, 0.0), K + V(0, -0.09, 0.035), 0.013, HORN)
        H_ = J[f"hip.{name}"]
        b.piece(H_.lerp(K, 0.4), skin=True)
        shell(b.parts, H_.lerp(K, 0.4), (0.07, 0.07, 0.1), align(K - H_, V(0, -1, 0)), lambda d: d.y > 0.0 and d.x * sd > -0.3, 0.01, HORN)  # cuisse
    # The shield on the left forearm (the monster's left: +x).
    E, W = J["elbow.L"], J["wrist.L"]
    axis = (W - E).normalized()
    out = V(1, 0, 0) - axis * axis.x
    out.normalize()
    b.piece(E.lerp(W, 0.5))
    plate(b.parts, [(0.0, 0.12), (0.075, 0.07), (0.07, -0.08), (0.0, -0.18), (-0.07, -0.08), (-0.075, 0.07)],
          E.lerp(W, 0.45) + out * 0.06, out.cross(axis), axis, 0.016, HORN, bulge=0.025)
    b.piece(E.lerp(W, 0.45) + out * 0.09)
    spike(b.parts, E.lerp(W, 0.45) + out * 0.09, E.lerp(W, 0.45) + out * 0.16, 0.018, HORN)
    # The bladed tail.
    p, q = tpts[-4], tpts[-1]
    d = (q - p).normalized()
    side = d.cross(V(0, 0, 1)).normalized()
    b.piece(q)
    plate(b.parts, [(-0.01, -0.03), (0.035, 0.02), (0.02, 0.1), (0.0, 0.2), (-0.02, 0.1), (-0.035, 0.02)],
          q - d * 0.02, side, d, 0.01, HORN)
    for i in range(6, len(tpts) - 4, 3):
        b.piece(tpts[i])
        spike(b.parts, tpts[i] + V(0, 0, 0.02), tpts[i] + V(0, 0.02, 0.07), 0.013, HORN)
    # A horned helm of a skull: a visor of bone with eye slits, two great horns.
    H = Head(J)
    s = b.skin
    ellipsoid(s, H(0, 0.0, 0.0), (0.055, 0.07, 0.055), BODY, rot=H.R)
    tube(s, [H(0, 0.04, -0.03), H(0, 0.1, -0.045)], [0.04, 0.025], BODY, 8, flat=0.8, up=H.u)
    b.piece(H(0, 0.02, 0.06))
    shell(b.parts, H(0, 0.005, 0.005), (0.068, 0.085, 0.07), H.R,
          lambda d: d.z > 0.32 or (abs(d.x) < 0.17 and d.y > 0) or (d.y > 0.2 and -0.6 < d.z < 0.02) or (d.y < 0.25 and d.z > -0.05),
          0.012, HORN)  # the helm, open in two slits for the eyes
    b.piece(H(0, 0.12, -0.03))
    tube(b.parts, [H(0, 0.06, 0.06), H(0, 0.1, 0.02), H(0, 0.12, -0.04)], [0.012, 0.016, 0.01], HORN, 6)  # nasal ridge
    for sd in (1, -1):
        b.piece(H(sd * 0.05, 0.0, 0.05))
        spike(b.parts, H(sd * 0.05, 0.01, 0.045), H(sd * 0.13, -0.02, 0.2), 0.022, HORN, bend=H.d(sd * 0.05, 0.05, -0.02))

    b.eyes.append((V(0.03, 0.072, 0.018), V(0.45, 0.85, 0.1)))
    brows(b, H, (0.028, 0.07, 0.018), 0.008, 0.016)
    vein(b, [rib + V(0, -0.135, z) for z in (0.06, 0.02, -0.03, -0.08)])
    for sd in (1, -1):
        vein(b, [rib + V(sd * x, -0.12, z) for x, z in ((0.0, 0.0), (0.04, -0.03), (0.08, -0.03))])
    return b


def warlord():
    J = joints({
        "hips": (0, 0.18, 0.44), "spine": (0, 0.13, 0.57), "chest": (0, 0.08, 0.69), "neck": (0, 0.0, 0.79),
        "head": (0, -0.05, 0.82), "head_end": (0, -0.2, 0.83),
        "shoulder.L": (0.23, 0.08, 0.73), "elbow.L": (0.32, 0.1, 0.54), "wrist.L": (0.36, 0.0, 0.38),
        "claw.L": (0.37, -0.06, 0.22),
        "hip.L": (0.11, 0.17, 0.44), "knee.L": (0.2, 0.02, 0.25), "hock.L": (0.2, 0.16, 0.13), "toe.L": (0.21, 0.0, 0.02),
        "tail0": (0, 0.25, 0.43), "tail1": (0, 0.38, 0.34), "tail2": (0, 0.56, 0.24), "tail3": (0, 0.78, 0.17),
        "tail4": (0, 1.0, 0.16), "tail5": (0, 1.2, 0.22),
    })
    b = Build("Warlord", J)
    torso(b, w=1.2, chest=(0.15, 0.12, 0.14), belly=(0.1, 0.09, 0.1), pelvis=(0.1, 0.09, 0.075), neck=0.058,
          hump=(J["chest"] + V(0, 0.05, 0.05), (0.15, 0.1, 0.09)), pecs=0.075)
    for sd in "LR":
        arm(b, sd, ua=0.052, fa=0.05, hand=0.038, fingers=4, flen=1.05, claw=0.055, claw_r=0.01, bulk=1.1)
        leg(b, sd, th=0.068, sh=0.038, foot=0.03, claw=0.03)
    tpts = tail(b, 0.065, 0.014)
    for i in range(4, len(tpts) - 3, 2):
        p = tpts[i]
        b.piece(p, snap=V(0, 0, 1))
        r = 0.065 * (1 - i / len(tpts)) + 0.014
        spike(b.parts, p + V(0, 0, r * 0.7), p + V(0, 0.03, r + 0.05), 0.015, HORN)
    p, q = tpts[-3], tpts[-1]
    d = (q - p).normalized()
    side = d.cross(V(0, 0, 1)).normalized()
    b.piece(q)
    plate(b.parts, [(0.0, -0.03), (0.06, 0.03), (0.03, 0.08), (0.0, 0.22), (-0.03, 0.08), (-0.06, 0.03)], q - d * 0.02,
          side, d, 0.012, HORN)
    # Spiked pauldrons.
    for sd, name in ((1, "L"), (-1, "R")):
        S = J[f"shoulder.{name}"]
        b.piece(S + V(sd * 0.05, 0, 0.05))
        shell(b.parts, S + V(sd * 0.01, 0, 0.005), (0.1, 0.1, 0.085), align(V(sd, 0, 0.7), V(0, -1, 0)), lambda d: d.z > -0.05, 0.014, HORN)
        for k in range(4):
            a = -0.6 + k * 0.4
            base = S + V(sd * 0.05, a * 0.07, 0.07)
            spike(b.parts, base, base + V(sd * 0.07, a * 0.05 + 0.02, 0.12 - abs(a) * 0.04), 0.017, HORN, bend=V(0, 0.02, 0))
    # Wings folded on the back like a ragged cape.
    for sd in (1, -1):
        root = J["chest"] + V(sd * 0.09, 0.11, 0.04)
        elbow = root + V(sd * 0.1, 0.07, 0.14)
        wrist = elbow + V(sd * 0.06, 0.1, -0.2)
        b.piece(root, skin=True)
        tube(b.parts, [root, root.lerp(elbow, 0.5), elbow], [0.03, 0.026, 0.022], BODY, 7)
        tube(b.parts, [elbow, elbow.lerp(wrist, 0.5), wrist], [0.022, 0.018, 0.015], BODY, 7)
        spike(b.parts, elbow + V(0, 0, 0.01), elbow + V(sd * 0.03, -0.02, 0.09), 0.016, HORN)
        tips = [wrist + V(sd * 0.02, 0.06, -0.33), wrist + V(sd * -0.04, 0.04, -0.42), wrist + V(sd * -0.11, 0.0, -0.4)]
        for t in tips:
            tube(b.parts, [wrist, wrist.lerp(t, 0.5) + V(0, 0.02, 0), t], [0.012, 0.009, 0.003], BODY, 5)
        # Membrane panels between the fingers, ragged at the hem.
        rim = [elbow, wrist, tips[0], tips[0].lerp(tips[1], 0.5) + V(0, 0, 0.07), tips[1],
               tips[1].lerp(tips[2], 0.5) + V(0, 0, 0.09), tips[2], J["spine"] + V(sd * 0.04, 0.12, -0.05), root]
        centre = sum(rim, Vector()) / len(rim)
        out = V(0, 1, 0.1).normalized()
        top = [b.parts.verts.new(p + out * 0.003) for p in rim]
        bot = [b.parts.verts.new(p - out * 0.003) for p in rim]
        ct = b.parts.verts.new(centre + out * 0.01)
        cb = b.parts.verts.new(centre - out * 0.004)
        for i in range(len(rim)):
            j = (i + 1) % len(rim)
            for f in (b.parts.faces.new((ct, top[i], top[j])), b.parts.faces.new((cb, bot[j], bot[i])),
                      b.parts.faces.new((top[i], bot[i], bot[j], top[j]))):
                f.material_index = BODY
        for t in tips:
            vein_pts = [wrist.lerp(t, k) + out * 0.006 for k in (0.15, 0.5, 0.85)]
            tube(b.parts, vein_pts, [0.004, 0.004, 0.002], EMBERS, 4)
    # The head: a heavy jaw full of teeth, a crown of horns.
    H = Head(J)
    s = b.skin
    ellipsoid(s, H(0, 0.0, 0.01), (0.065, 0.075, 0.06), BODY, rot=H.R)
    tube(s, [H(0, 0.04, 0.0), H(0, 0.12, -0.01), H(0, 0.17, -0.02)], [0.05, 0.038, 0.028], BODY, 10, flat=0.85, up=H.u)
    tube(s, [H(0, -0.02, -0.055), H(0, 0.08, -0.08), H(0, 0.16, -0.07)], [0.06, 0.045, 0.03], BODY, 10, flat=0.7, up=H.u)
    for sd in (1, -1):
        for k, y in enumerate((0.15, 0.12, 0.09, 0.06)):
            b.piece(H(sd * 0.022, y, -0.03))
            spike(b.parts, H(sd * 0.024, y, -0.028), H(sd * 0.026, y + 0.003, -0.06 + k * 0.004), 0.007, HORN)
            b.piece(H(sd * 0.02, y - 0.01, -0.06))
            spike(b.parts, H(sd * 0.02, y - 0.012, -0.065), H(sd * 0.021, y - 0.01, -0.033), 0.006, HORN)

    b.piece(H(0, 0.1, -0.045))
    ellipsoid(b.parts, H(0, 0.1, -0.045), (0.022, 0.065, 0.012), EMBERS, 8, 6, rot=H.R)
    # The crown: two great horns and a ring of smaller ones swept back.
    for sd in (1, -1):
        b.piece(H(sd * 0.05, -0.01, 0.05))
        spike(b.parts, H(sd * 0.045, 0.0, 0.045), H(sd * 0.16, -0.16, 0.2), 0.028, HORN, bend=H.d(sd * 0.04, 0.06, 0.04))
        for k in range(4):
            a = 0.3 + k * 0.35
            base = H(sd * math.sin(a) * 0.06, -math.cos(a) * 0.06 + 0.01, 0.045 - k * 0.01)
            b.piece(base)
            spike(b.parts, base, base + H.d(sd * math.sin(a) * 0.07, -0.13 - k * 0.01, 0.1 - k * 0.025), 0.014, HORN,
                  bend=H.d(0, 0, 0.015))
    b.piece(H(0, -0.03, 0.06))
    spike(b.parts, H(0, -0.03, 0.055), H(0, -0.12, 0.18), 0.018, HORN)
    b.eyes.append((V(0.034, 0.07, 0.025), V(0.55, 0.8, 0.15)))
    brows(b, H, (0.032, 0.066, 0.025), 0.01, 0.017)
    for sd in (1, -1):
        S = J["shoulder.L" if sd > 0 else "shoulder.R"]
        vein(b, [J["chest"].lerp(S, t) + V(0, -0.12 + t * 0.04, -0.02 - t * 0.04) for t in (0.0, 0.35, 0.7)])
    vein(b, [J["spine"] + V(x, -0.09, z) for x, z in ((0.0, 0.06), (0.02, 0.0), (-0.01, -0.05), (0.01, -0.1))])
    return b


def ashhorn():
    """A soldier: gaunt and stooped, the arms too long, a stag's skull where its face should be."""
    J = joints({
        "hips": (0, 0.14, 0.45), "spine": (0, 0.1, 0.56), "chest": (0, 0.03, 0.66), "neck": (0, -0.07, 0.73),
        "head": (0, -0.11, 0.75), "head_end": (0, -0.24, 0.68),
        "shoulder.L": (0.17, 0.03, 0.68), "elbow.L": (0.29, 0.03, 0.47), "wrist.L": (0.35, -0.04, 0.26),
        "claw.L": (0.37, -0.09, 0.09),
        "hip.L": (0.085, 0.14, 0.44), "knee.L": (0.13, 0.03, 0.25), "hock.L": (0.13, 0.12, 0.13), "toe.L": (0.14, 0.0, 0.02),
        "tail0": (0, 0.2, 0.44), "tail1": (0, 0.24, 0.4), "tail2": (0, 0.27, 0.36), "tail3": (0, 0.29, 0.32),
        "tail4": (0, 0.3, 0.29), "tail5": (0, 0.31, 0.26),
    })
    b = Build("Ashhorn", J)
    torso(b, w=0.8, chest=(0.11, 0.09, 0.12), belly=(0.095, 0.08, 0.11), pelvis=(0.095, 0.08, 0.08), neck=0.036,
          hump=(J["chest"] + V(0, 0.06, 0.03), (0.09, 0.07, 0.07)), pecs=0.045)
    for sd in "LR":
        arm(b, sd, ua=0.03, fa=0.027, hand=0.03, fingers=3, flen=1.25, claw=0.05, claw_r=0.007, bulk=0.9)
        leg(b, sd, th=0.06, sh=0.03, foot=0.028, toes=3, claw=0.02)
    tail(b, 0.022, 0.006, per=2)

    H = Head(J)
    ellipsoid(b.skin, H(0, 0.0, 0.0), (0.036, 0.05, 0.036), BODY, rot=H.R)  # (the head the skull is worn over)
    b.eyes.append((V(0.03, 0.05, 0.016), V(0.8, 0.5, 0.1)))
    # The stag's skull: cranium, a long tapering muzzle, cheekbones, a loose jaw, sockets with embers in them.
    b.piece(H(0, 0.05, 0.0))
    p = b.parts
    ellipsoid(p, H(0, 0.03, 0.012), (0.053, 0.072, 0.052), HORN, rot=H.R)
    tube(p, [H(0, 0.08, 0.005), H(0, 0.15, -0.012), H(0, 0.23, -0.03), H(0, 0.29, -0.042)], [0.04, 0.03, 0.021, 0.013], HORN, 9,
         flat=0.8, up=H.u)
    for sd in (1, -1):
        ellipsoid(p, H(sd * 0.044, 0.075, 0.018), (0.02, 0.03, 0.017), HORN, 8, 6, rot=H.R)      # brow
        tube(p, [H(sd * 0.05, 0.06, -0.012), H(sd * 0.036, 0.13, -0.03), H(sd * 0.018, 0.21, -0.04)], [0.011, 0.009, 0.005], HORN, 5)
        ellipsoid(p, H(sd * 0.042, 0.075, 0.006), (0.015, 0.019, 0.014), DARK, 8, 6, rot=H.R)    # socket
        ellipsoid(p, H(sd * 0.05, 0.078, 0.006), (0.007, 0.008, 0.006), EYES, 6, 4, rot=H.R)
        ellipsoid(p, H(sd * 0.008, 0.285, -0.036), (0.006, 0.014, 0.005), DARK, 6, 4, rot=H.R)   # nostril
        tube(p, [H(sd * 0.04, 0.03, -0.035), H(sd * 0.028, 0.12, -0.07), H(sd * 0.012, 0.24, -0.075)], [0.008, 0.008, 0.005], HORN, 5)
        for k in range(4):
            y = 0.13 + k * 0.03
            spike(p, H(sd * 0.018, y, -0.03), H(sd * 0.018, y, -0.05), 0.004, HORN, sides=4)
    # The antlers: a long beam sweeping up and out, tines off it.
    for sd in (1, -1):
        b.piece(H(sd * 0.03, -0.01, 0.05))
        base = H(sd * 0.03, 0.0, 0.05)
        beam = catmull([base, base + H.d(sd * 0.06, -0.05, 0.12), base + H.d(sd * 0.07, -0.08, 0.27), base + H.d(sd * 0.13, -0.03, 0.41)], 3)
        tube(p, beam, list(np.linspace(0.016, 0.003, len(beam))), ANTLER, 6)
        for k, (t, out, up, fwd) in enumerate(((0.2, 0.05, 0.08, 0.1), (0.42, 0.11, 0.1, 0.05), (0.62, -0.05, 0.11, 0.07), (0.8, 0.09, 0.08, -0.03))):
            at = beam[int(t * (len(beam) - 1))]
            spike(p, at, at + H.d(sd * out, fwd, up), 0.008, ANTLER, bend=H.d(sd * 0.01, 0, 0.012), sides=4)
    # A collar of ribs over the chest, riding the skin.
    C = J["chest"]
    for k in range(4):
        z = 0.06 - k * 0.035
        w = 0.1 - k * 0.012
        b.piece(C + V(0, -0.08, z), skin=True)
        for sd in (1, -1):
            tube(p, catmull([C + V(0, -0.095, z), C + V(sd * w * 0.6, -0.095 + k * 0.004, z + 0.012), C + V(sd * w, -0.04, z - 0.01)], 2),
                 [0.008, 0.0075, 0.003], HORN, 4)
    # The waist: a hide skirt and a blood-red cloth hanging to the knees.
    hp = J["hips"]
    b.piece(hp + V(0, -0.09, 0.0))
    plate(p, [(-0.05, 0.03), (0.05, 0.03), (0.042, -0.14), (0.016, -0.24), (0.0, -0.27), (-0.018, -0.23), (-0.044, -0.13)],
          hp + V(0, -0.095, 0.0), V(1, 0, 0), V(0, -0.15, 1), 0.006, EMBERS)
    for sd in (1, -1):
        b.piece(hp + V(sd * 0.095, 0.0, -0.02))
        plate(p, [(-0.055, 0.03), (0.055, 0.03), (0.06, -0.13), (0.015, -0.15), (-0.05, -0.12)], hp + V(sd * 0.1, 0.0, 0.0),
              V(0, 1, 0), V(sd * 0.25, 0, 1), 0.006, ANTLER, bulge=0.01)
    b.piece(hp + V(0, 0.09, 0.0))
    plate(p, [(-0.06, 0.03), (0.06, 0.03), (0.05, -0.11), (-0.05, -0.12)], hp + V(0, 0.095, 0.0), V(1, 0, 0), V(0, 0.1, 1), 0.006, ANTLER)
    # Old blood down the arms and chest.
    for side in "LR":
        vein(b, [J[f"shoulder.{side}"].lerp(J[f"elbow.{side}"], t) for t in (0.2, 0.55, 0.9)])
        vein(b, [J[f"elbow.{side}"].lerp(J[f"wrist.{side}"], t) for t in (0.1, 0.5, 0.9)])
    return b


def demon_lord():
    """The boss: a tower of crimson muscle in black plate, crowned by a crescent of horns."""
    J = joints({
        "hips": (0, 0.14, 0.47), "spine": (0, 0.11, 0.59), "chest": (0, 0.08, 0.72), "neck": (0, 0.03, 0.83),
        "head": (0, 0.0, 0.86), "head_end": (0, -0.15, 0.87),
        "shoulder.L": (0.25, 0.08, 0.77), "elbow.L": (0.36, 0.1, 0.57), "wrist.L": (0.42, 0.0, 0.39), "claw.L": (0.44, -0.06, 0.23),
        "hip.L": (0.085, 0.14, 0.46), "knee.L": (0.13, 0.03, 0.26), "hock.L": (0.13, 0.13, 0.13), "toe.L": (0.14, -0.01, 0.02),
        "tail0": (0, 0.22, 0.5), "tail1": (0, 0.36, 0.47), "tail2": (0, 0.54, 0.42), "tail3": (0, 0.74, 0.36),
        "tail4": (0, 0.94, 0.34), "tail5": (0, 1.12, 0.4),
    })
    b = Build("DemonLord", J)
    torso(b, w=1.15, chest=(0.15, 0.115, 0.14), belly=(0.08, 0.075, 0.1), pelvis=(0.09, 0.08, 0.07), neck=0.05, pecs=0.075)
    for sd in "LR":
        arm(b, sd, ua=0.05, fa=0.046, hand=0.036, fingers=4, flen=1.15, claw=0.075, claw_r=0.011, bulk=1.1)
        leg(b, sd, th=0.066, sh=0.036, foot=0.03, claw=0.035)
    tpts = tail(b, 0.05, 0.01)
    p = b.parts
    q, d = tpts[-1], (tpts[-1] - tpts[-3]).normalized()
    b.piece(q)
    plate(p, [(0.0, -0.03), (0.07, 0.05), (0.02, 0.1), (0.0, 0.26), (-0.02, 0.1), (-0.07, 0.05)], q - d * 0.02, d.cross(V(0, 0, 1)).normalized(), d, 0.012, HORN)

    # Black plate: shoulders, chest ridges, forearms, shins.
    for sd, name in ((1, "L"), (-1, "R")):
        S, E, W, K, Hk = (J[f"{k}.{name}"] for k in ("shoulder", "elbow", "wrist", "knee", "hock"))
        b.piece(S + V(sd * 0.05, 0, 0.05))
        shell(p, S + V(sd * 0.012, 0, 0.01), (0.115, 0.115, 0.09), align(V(sd, 0, 0.6), V(0, -1, 0)), lambda v: v.z > -0.1, 0.016, HORN)
        for k in range(3):
            base = S + V(sd * (0.05 + k * 0.03), 0.03 - k * 0.04, 0.075 - k * 0.015)
            spike(p, base, base + V(sd * 0.13, 0.02, 0.1 - k * 0.04), 0.018, HORN, bend=V(0, 0.02, 0.02))
        b.piece(E.lerp(W, 0.5), skin=True)
        for k in range(3):
            base = E.lerp(W, 0.25 + k * 0.25) + V(sd * 0.04, 0.02, 0)
            spike(p, base, base + V(sd * 0.09, 0.05, 0.03), 0.014, HORN)
        b.piece(K.lerp(Hk, 0.4), skin=True)
        spike(p, K + V(0, -0.04, 0.01), K + V(sd * 0.02, -0.13, 0.08), 0.022, HORN)
        for k in range(3):
            base = K.lerp(Hk, 0.2 + k * 0.25) + V(sd * 0.03, -0.03, 0)
            spike(p, base, base + V(sd * 0.06, -0.03, -0.05), 0.012, HORN)
    # The war-belt: black plates hanging over the hips.
    hp = J["hips"]
    b.piece(hp + V(0, -0.09, 0.0))
    plate(p, [(-0.08, 0.05), (0.08, 0.05), (0.07, -0.08), (0.02, -0.17), (0.0, -0.2), (-0.02, -0.17), (-0.07, -0.08)],
          hp + V(0, -0.098, 0.0), V(1, 0, 0), V(0, -0.12, 1), 0.012, HORN, bulge=0.012)
    for sd in (1, -1):
        b.piece(hp + V(sd * 0.1, -0.01, 0.0))
        plate(p, [(-0.07, 0.04), (0.07, 0.04), (0.06, -0.12), (0.0, -0.17), (-0.06, -0.11)], hp + V(sd * 0.105, -0.015, 0.0),
              V(0, 1, 0), V(sd * 0.2, 0, 1), 0.012, HORN, bulge=0.012)
    # Four blades fanning backwards from the shoulder blades (it has no wings: they are its spine's own bone).
    for sd in (1, -1):
        for k, (out, up, back, long) in enumerate(((0.5, 0.9, 0.5, 0.5), (0.95, 0.35, 0.6, 0.42))):
            root = J["chest"] + V(sd * (0.07 + k * 0.05), 0.1, 0.05 - k * 0.04)
            tip = root + V(sd * out, back, up).normalized() * long
            b.piece(root, snap=V(0, 1, 0.1))
            spike(p, root, tip, 0.032, HORN, bend=V(sd * 0.03, 0.05, -0.08), sides=6)
            spike(p, root.lerp(tip, 0.45), root.lerp(tip, 0.45) + V(sd * 0.05, 0.06, -0.08), 0.012, HORN)
    # The head: a long plated face, a crescent of two great horns rising and curling inward, a low crown of spines.
    H = Head(J)
    s = b.skin
    ellipsoid(s, H(0, 0.0, 0.01), (0.056, 0.07, 0.06), BODY, rot=H.R)
    tube(s, [H(0, 0.03, -0.015), H(0, 0.1, -0.05), H(0, 0.14, -0.075)], [0.05, 0.036, 0.022], BODY, 10, flat=0.85, up=H.u)
    b.eyes.append((V(0.03, 0.062, 0.02), V(0.55, 0.8, 0.15)))
    brows(b, H, (0.028, 0.06, 0.02), 0.01, 0.015)
    b.piece(H(0, 0.05, 0.05))
    plate(p, [(-0.05, -0.04), (0.05, -0.04), (0.03, 0.05), (0.0, 0.09), (-0.03, 0.05)], H(0, 0.04, 0.05), H.s, H.f, 0.01, HORN, bulge=0.014)
    for sd in (1, -1):
        b.piece(H(sd * 0.05, 0.0, 0.05))
        horn = catmull([H(sd * 0.05, 0.0, 0.045), H(sd * 0.17, 0.0, 0.08), H(sd * 0.25, 0.02, 0.22), H(sd * 0.2, 0.04, 0.38), H(sd * 0.08, 0.05, 0.47)], 3)
        tube(p, horn, list(np.linspace(0.03, 0.0, len(horn))), HORN, 7)
        for k in range(3):
            base = H(sd * (0.02 + k * 0.022), -0.05 - k * 0.012, 0.05 - k * 0.012)
            spike(p, base, base + H.d(sd * 0.03, -0.09, 0.09 - k * 0.02), 0.012, HORN)
    for k in range(5):  # a jaw of teeth
        y, x = 0.13 - k * 0.018, 0.016 + k * 0.004
        for sd in (1, -1):
            b.piece(H(sd * x, y, -0.07))
            spike(p, H(sd * x, y, -0.065), H(sd * x, y, -0.105), 0.006, ANTLER, sides=4)
    # Fire in the seams of the chest, and down the arms and flanks.
    C = J["chest"]
    vein(b, [C + V(x, -0.13, z) for x, z in ((0.0, 0.08), (0.012, 0.03), (-0.01, -0.03), (0.008, -0.09))])
    vein(b, [J["spine"] + V(x, -0.09, z) for x, z in ((0.0, 0.07), (-0.012, 0.0), (0.01, -0.06))])
    for sd, name in ((1, "L"), (-1, "R")):
        S = J[f"shoulder.{name}"]
        vein(b, [C.lerp(S, t) + V(0, -0.11 + t * 0.04, 0.01 - t * 0.03) for t in (0.1, 0.45, 0.8)])
        vein(b, [J[f"elbow.{name}"].lerp(J[f"wrist.{name}"], t) for t in (0.1, 0.5, 0.9)])
        vein(b, [J["spine"] + V(sd * x, -0.05, z) for x, z in ((0.085, 0.06), (0.078, -0.01), (0.07, -0.08))])
    return b


BREEDS = {"Cinderfang": cinderfang, "Ravager": ravager, "DreadWarden": dread_warden, "Warlord": warlord,
          "Ashhorn": ashhorn, "DemonLord": demon_lord}


# ---------------------------------------------------------------- assembling, rigging, exporting


def to_object(name, bm, mats):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    for m in mats:
        me.materials.append(m)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def activate(obj):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)


def apply_mod(obj, mod):
    activate(obj)
    bpy.ops.object.modifier_apply(modifier=mod.name)


def sculpt_skin(obj, target_tris):
    """The volumes melt into one skin: voxel remesh, smoothed, a little lumpy, then down to the triangle budget."""
    m = obj.modifiers.new("Remesh", "REMESH")
    m.mode = "VOXEL"
    m.voxel_size = 0.0055
    m.adaptivity = 0.0
    apply_mod(obj, m)
    m = obj.modifiers.new("Smooth", "SMOOTH")
    m.factor = 0.6
    m.iterations = 4
    apply_mod(obj, m)
    tex = bpy.data.textures.new(obj.name + "_Lumps", "CLOUDS")
    tex.noise_scale = 0.025
    m = obj.modifiers.new("Lumps", "DISPLACE")
    m.texture = tex
    m.strength = 0.004
    m.mid_level = 0.5
    apply_mod(obj, m)
    faces = len(obj.data.polygons) * 2
    m = obj.modifiers.new("Decimate", "DECIMATE")
    m.ratio = min(1.0, target_tris / max(faces, 1))
    apply_mod(obj, m)
    for p in obj.data.polygons:
        p.use_smooth = True


def build_rig(name, J):
    arm_data = bpy.data.armatures.new(name + "Rig")
    rig = bpy.data.objects.new(name + "Rig", arm_data)
    bpy.context.scene.collection.objects.link(rig)
    activate(rig)
    bpy.ops.object.mode_set(mode="EDIT")
    for bname, h, t, parent in BONES:
        eb = arm_data.edit_bones.new(bname)
        eb.head = J[h]
        eb.tail = J[t]
        eb.roll = 0.0
        if parent:
            eb.parent = arm_data.edit_bones[parent]
            eb.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def segment_distance(p, a, b):
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(ab @ ab, 1e-9), 0.0, 1.0)
    return np.linalg.norm(p - (a + np.outer(t, ab)), axis=1)


def distance_weights(obj, J):
    """Fallback skinning (if bone heat fails): 1/d^4 to each bone, top three, with region rules by side."""
    verts = np.array([v.co[:] for v in obj.data.vertices])
    names = [bn[0] for bn in BONES]
    dist = np.stack([segment_distance(verts, np.array(J[h][:]), np.array(J[t][:])) for _, h, t, _ in BONES], axis=1)
    w = 1.0 / (dist ** 4 + 1e-7)
    for j, n in enumerate(names):
        if n.endswith(".L"):
            w[verts[:, 0] < -0.01, j] = 0
        if n.endswith(".R"):
            w[verts[:, 0] > 0.01, j] = 0
    keep = np.argsort(-w, axis=1)[:, :3]
    groups = {n: obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n) for n in names}
    for i in range(len(verts)):
        top = w[i, keep[i]]
        top = top / top.sum()
        for j, x in zip(keep[i], top):
            if x > 0.01:
                groups[names[j]].add([i], float(x), "REPLACE")


def skin_to(obj, rig, J):
    activate(obj)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    except RuntimeError as e:
        print(f"[Breeds] bone heat failed ({e}); distance weights")
    weighted = sum(1 for v in obj.data.vertices if v.groups)
    if weighted < len(obj.data.vertices) * 0.98:
        print(f"[Breeds] {obj.name}: bone heat weighted only {weighted}/{len(obj.data.vertices)}: distance weights")
        for g in list(obj.vertex_groups):
            obj.vertex_groups.remove(g)
        distance_weights(obj, J)
        if obj.parent != rig:
            obj.parent = rig
        if not any(m.type == "ARMATURE" for m in obj.modifiers):
            obj.modifiers.new("Armature", "ARMATURE").object = rig


def vertex_weights(obj):
    names = {g.index: g.name for g in obj.vertex_groups}
    return [{names[g.group]: g.weight for g in v.groups} for v in obj.data.vertices]


def ride_skin(parts, body, b):
    """Each rigid piece takes the weights of the skin at its anchor; skin pieces take them vertex by vertex."""
    weights = vertex_weights(body)
    kd = KDTree(len(body.data.vertices))
    for i, v in enumerate(body.data.vertices):
        kd.insert(v.co, i)
    kd.balance()
    pieces = parts.data.attributes.get("piece")
    ids = [d.value for d in pieces.data] if pieces else [0] * len(parts.data.vertices)
    groups = {}
    for vi, v in enumerate(parts.data.vertices):
        pid = ids[vi]
        if pid > 0 and b is not None and b.anchors[pid - 1][1] is False:
            _, near, _ = kd.find(b.anchors[pid - 1][0])
        else:
            _, near, _ = kd.find(v.co)
        for g, w in weights[near].items():
            if g not in groups:
                groups[g] = parts.vertex_groups.new(name=g)
            groups[g].add([vi], w, "REPLACE")


def project_veins(b, body):
    """The ember cracks: each polyline pressed onto the skin, a thin glowing seam just proud of it."""
    if not b.veins:
        return None
    bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    bm = bmesh.new()
    for line in b.veins:
        dense = catmull(line, 4)
        pts, nrms = [], []
        for p in dense:
            loc, nrm, _, _ = bvh.find_nearest(p)
            if loc is None:
                continue
            pts.append(loc + nrm * 0.0018)
            nrms.append(nrm)
        if len(pts) < 2:
            continue
        r = [0.0] + [0.0045] * (len(pts) - 2) + [0.0]
        tube(bm, pts, r, EMBERS, 4, flat=0.35, up=nrms[len(nrms) // 2])
    return bm


def eye_mesh(name, mats, mat_index=EYES, size=(0.019, 0.007, 0.011)):
    """An almond of fire round its own centre: wide along x, tall along z, its face towards -y."""
    bm = bmesh.new()
    ellipsoid(bm, (0, 0, 0), size, 0, 12, 8)
    for v in bm.verts:  # pointed corners
        k = abs(v.co.x) / size[0]
        v.co.z *= 1.0 - 0.55 * k ** 2
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mats[mat_index])
    for p in me.polygons:
        p.use_smooth = True
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def place_eyes(rig, body, head_pos, head_frame, local, outward, mats, mat_index=EYES, size=(0.019, 0.007, 0.011),
               slant=18.0, sink=0.35):
    """Eye.L / Eye.R on the skin: cast in from in front of each eye's spot, sat in the surface (sink: how much of
    it is buried), slanted up at the outer corner, parented to the Head bone."""
    bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    s, f, u = head_frame
    placed = {}
    for side, sd in (("L", 1), ("R", -1)):
        guess = head_pos + s * local.x * sd + f * local.y + u * local.z
        out = (s * outward.x * sd + f * outward.y + u * outward.z).normalized()
        hit, nrm, _, _ = bvh.ray_cast(guess + out * 0.12, -out, 0.3)
        if hit is None:
            hit, nrm, _, _ = bvh.find_nearest(guess)
        nrm = (nrm + out).normalized()
        centre = hit - nrm * size[1] * (2 * sink - 1)
        y = -nrm                      # the eye's face (-y) looks out
        z = u - y * u.dot(y)
        z.normalize()
        x = y.cross(z)
        a = math.radians(slant) * sd
        x, z = x * math.cos(a) + z * math.sin(a), z * math.cos(a) - x * math.sin(a)
        eye = eye_mesh(f"Eye.{side}", mats, mat_index, size)
        eye.parent = rig
        eye.parent_type = "BONE"
        eye.parent_bone = "Head"
        bpy.context.view_layer.update()
        eye.matrix_world = Matrix.Translation(centre) @ Matrix((tuple(x), tuple(y), tuple(z))).transposed().to_4x4()
        placed[side] = centre
    return placed


def export(path):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=False, object_types={"MESH", "ARMATURE"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False, mesh_smooth_type="FACE", add_leaf_bones=False,
        armature_nodetype="NULL", use_armature_deform_only=True, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=False, path_mode="AUTO")


def build(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats, entries = make_materials(name, horn=0.07 if name == "DreadWarden" else 0.06)
    b = BREEDS[name]()
    b.finish_parts()
    body = to_object(name, b.skin, mats)
    b.skin.free()
    sculpt_skin(body, 4200 if name == "Ashhorn" else 6400)  # (Ashhorn: up to eight at once)
    snap_pieces(b, body)
    # Feet on the ground.
    lowest = min(v.co.z for v in body.data.vertices)
    rig = build_rig(name, b.J)
    skin_to(body, rig, b.J)
    # The eyes sit in the bare skin (before horns and brows could get in the way of finding it).
    H = Head(b.J)
    local, outward = b.eyes[0]
    eyes = place_eyes(rig, body, H.o, (H.s, H.f, H.u), local, outward, mats)
    extras = [to_object(name + "_Parts", b.parts, mats)]
    b.parts.free()
    ride_skin(extras[0], body, b)
    vb = project_veins(b, body)
    if vb is not None:
        extras.append(to_object(name + "_Veins", vb, mats))
        vb.free()
        ride_skin(extras[-1], body, None)
    for o in extras:
        for p in o.data.polygons:
            p.use_smooth = True
    activate(body)
    for o in extras:
        o.select_set(True)
    bpy.ops.object.join()
    if "piece" in body.data.attributes:
        body.data.attributes.remove(body.data.attributes["piece"])
    activate(body)
    bpy.ops.object.vertex_group_limit_total(limit=3)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.material_slot_remove_unused()
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    os.makedirs(OUT, exist_ok=True)
    export(os.path.join(OUT, name + ".fbx"))
    with open(os.path.join(OUT, name + ".materials.json"), "w") as fh:
        json.dump({"materials": entries}, fh, indent=1)
    print(f"[Breeds] {name}: {tris} tris (+ eyes), lowest z {lowest:.3f}, eyes L {tuple(round(c, 3) for c in eyes['L'])}"
          f" R {tuple(round(c, 3) for c in eyes['R'])}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name in argv or list(BREEDS):
        build(name)


main()
