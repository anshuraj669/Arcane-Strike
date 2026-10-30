"""
Builds the Ascension temple (where the attic's rift drops the player) and exports it for Unity:

    blender -b -P Tools/blender/build_temple.py            (from the Unity project folder)

Writes Assets/_Project/Art/Environment/Temple/Temple.fbx with Temple.materials.json, Temple.placements.json and
Temple.passage.json. Paint its textures first (Tools/textures/build_temple_textures.py). Then in Unity:
Arcane Strike → Create Ascension Temple Scene.

The temple (Unity metres; the player arrives at the origin facing +Z):
- Temple_Floor: a round stone floor raised out of the water, its centre 4 m ahead; Temple_Mosaic: the inlay on it.
- Temple_Piers / Temple_Arches / Temple_Band / Temple_Gold: an open ring of twelve bays of pointed arches, open to the sky.
- Temple_Altar: a low gilded step at the centre; Temple_Cauldron: the old bronze cauldron on it (a three-legged ding with
  upright handles), which the player touches to begin the Ascension; Temple_CauldronFire: the light inside it.
- Temple_ExitSeal: a sealed gate filling the bay straight ahead until the Ascension is over.
- Temple_Bridge: the walkway beyond it over the water to Temple_Shrine, whose arch holds the threshold into the monster
  world. Temple_Orb: the blue light in the shrine. Temple.passage.json: where the seal and the threshold are.
- Temple_Water: jade water all round (MoonLake). Temple_Banks / Temple_Trees: the far shore. Temple_Statues: robed figures.
- Temple_Lantern: grandfather's brass lantern (kept hidden by the scene until it comes out of the rift).
"""

import json
import math
import os
import random

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
FOLDER = "Assets/_Project/Art/Environment/Temple"
OUT_FBX = os.path.join(PROJECT, FOLDER, "Temple.fbx")
LANTERN = os.path.join(HERE, "props", "Lantern_01")

C = Vector((0.0, 0.0, 4.0))  # the temple's centre: the Ascension's cauldron stands here
STEP = 0.16                  # the low step it stands on
R = 9.5                      # the colonnade's radius
BAYS = 12
FLOOR_R = R + 1.15
WATER_Y = -0.6               # more than a step below the floor, so nobody walks off the edge
SPRING, TOP = 3.3, 5.45      # where the arches spring from; the top of the piers
SHRINE_Z = 27.5

bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(12)
TO_BLENDER = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0))).to_4x4()
UP = Vector((0, 1, 0))
MATERIALS = {}


def material(name, color, roughness=0.8, metallic=0.0, emission=None, tex=None, tile=1.0, base=None):
    full = "M_Temple_" + name
    m = bpy.data.materials.new(full)
    m.diffuse_color = (*color, 1.0)
    MATERIALS[full] = {"name": full, "color": list(color), "smoothness": 1.0 - roughness, "metallic": metallic, "alpha": 1.0,
                       "emission": list(emission) if emission else [0, 0, 0], "texture": tex or "", "tile": tile,
                       "emissive": emission is not None}
    if base:
        MATERIALS[full]["baseMap"] = f"{FOLDER}/{base}"
    return m


IVORY = material("Ivory", (0.96, 0.9, 0.76), 0.75, tex="Plaster007", tile=1.5)
PIER = material("Pier", (0.86, 0.76, 0.58), 0.7, tex="Plaster007", tile=1.2)
PAVING = material("Paving", (1.0, 0.95, 0.82), 0.6, tex="PavingStones138", tile=1.6)
GOLD = material("Gold", (1.0, 0.76, 0.36), 0.35, 1.0, tex="Metal063", tile=0.5)
MOSAIC = material("Mosaic", (1.0, 1.0, 1.0), 0.45, base="T_Temple_Mosaic.jpg")
ORNAMENT = material("Ornament", (1.0, 1.0, 1.0), 0.55, base="T_Temple_Ornament.jpg")
WATER = material("Water", (0.06, 0.36, 0.3), 0.1)
BANK = material("Bank", (0.42, 0.3, 0.17), 0.95, tex="Rock058", tile=8.0)
LEAF = material("Leaf", (0.22, 0.38, 0.12), 0.9)
SEAL = material("Seal", (0.9, 0.8, 0.6), 0.6, tex="Plaster007", tile=1.0)
ORB = material("Orb", (0.4, 0.8, 1.0), 0.2, emission=(1.2, 2.6, 3.6))
BANNER = material("Banner", (0.62, 0.08, 0.05), 0.8)
BRONZE = material("Bronze", (1.0, 1.0, 1.0), 0.5, 0.35, base="T_Temple_Bronze.jpg")
FIRE = material("CauldronFire", (0.02, 0.03, 0.06), 0.5, emission=(0.03, 0.14, 0.45))


def finish(name, bm, mat, smooth=False, project_uv=True, recalc=True):
    """A bmesh in Unity terms → an object in Blender's axes, with box-projected UVs at the material's tiling."""
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if project_uv:
        uv = bm.loops.layers.uv.verify()
        tile = MATERIALS[mat.name]["tile"]
        for f in bm.faces:
            f.normal_update()
            n = f.normal
            axis = max(range(3), key=lambda k: abs(n[k]))
            a, b = [k for k in range(3) if k != axis]
            for loop in f.loops:
                loop[uv].uv = (loop.vert.co[a] / tile, loop.vert.co[b] / tile)
    for f in bm.faces:
        f.smooth = smooth
    bmesh.ops.transform(bm, matrix=TO_BLENDER, verts=bm.verts[:])
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])  # TO_BLENDER mirrors
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    print(f"[Temple] {name}: {len(me.polygons)} faces")
    return obj


def box(bm, centre, size, yaw=0.0):
    """A box, turned `yaw` degrees about y."""
    res = bmesh.ops.create_cube(bm, size=1.0)
    M = Matrix.Translation(centre) @ Matrix.Rotation(math.radians(yaw), 4, "Y") @ Matrix.Diagonal((*size, 1.0))
    bmesh.ops.transform(bm, matrix=M, verts=res["verts"])


def column(bm, centre, r0, height, r1=None, segs=10):
    """A cylinder or cone standing on `centre`."""
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r0, radius2=r0 if r1 is None else r1, depth=height)
    M = Matrix.Translation(centre + UP * height / 2) @ Matrix.Rotation(math.radians(-90), 4, "X")
    bmesh.ops.transform(bm, matrix=M, verts=res["verts"])


def blob(bm, centre, radius, squash=1.0, subdiv=2, seed=0.0):
    res = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    s = Vector((seed, seed * 0.7, seed * 1.3))
    for v in res["verts"]:
        d = v.co.normalized()
        k = 1.0 + 0.3 * noise.noise(d * 1.8 + s)
        v.co = centre + Vector((d.x * k * radius, d.y * k * radius * squash, d.z * k * radius))


def direction(deg):
    a = math.radians(deg)
    return Vector((math.sin(a), 0, math.cos(a)))


def annulus(bm, r0, r1, y, segs=72, centre=C):
    """A flat ring, facing up."""
    inner = [bm.verts.new(centre + direction(i / segs * 360) * r0 + UP * y) for i in range(segs)]
    outer = [bm.verts.new(centre + direction(i / segs * 360) * r1 + UP * y) for i in range(segs)]
    for i in range(segs):
        j = (i + 1) % segs
        bm.faces.new((inner[i], outer[i], outer[j], inner[j]))


def wall_ring(bm, r, y0, y1, segs=72, uv_tiles=0):
    """A vertical ring. uv_tiles: the texture repeats this many times round it (the whole height of it)."""
    lower = [bm.verts.new(C + direction(i / segs * 360) * r + UP * y0) for i in range(segs + 1)]
    upper = [bm.verts.new(C + direction(i / segs * 360) * r + UP * y1) for i in range(segs + 1)]
    uv = bm.loops.layers.uv.verify()
    for i in range(segs):
        f = bm.faces.new((lower[i], lower[i + 1], upper[i + 1], upper[i]))
        if uv_tiles:
            for loop, (u, v) in zip(f.loops, ((i, 0), (i + 1, 0), (i + 1, 1), (i, 1))):
                loop[uv].uv = (u / segs * uv_tiles, v)


# ---------------------------------------------------------------- the temple


def build_floor():
    bm = bmesh.new()
    column(bm, C + UP * (WATER_Y - 0.6), FLOOR_R, 0.6 - WATER_Y, segs=72)
    finish("Temple_Floor", bm, PAVING)
    bm = bmesh.new()  # the inlay, a hair above the paving, its texture laid flat across it
    r = 6.3
    centre = bm.verts.new(C + UP * 0.012)
    ring = [bm.verts.new(C + direction(i / 64 * 360) * r + UP * 0.012) for i in range(64)]
    uv = bm.loops.layers.uv.verify()
    for i in range(64):
        f = bm.faces.new((centre, ring[i], ring[(i + 1) % 64]))
        for loop in f.loops:
            p = loop.vert.co - C
            loop[uv].uv = (p.x / (2 * r) + 0.5, p.z / (2 * r) + 0.5)
        if f.normal.y < 0 or True:
            f.normal_update()
    finish("Temple_Mosaic", bm, MOSAIC, project_uv=False)
    bm = bmesh.new()
    annulus(bm, 6.3, 6.5, 0.014)
    annulus(bm, FLOOR_R - 0.4, FLOOR_R - 0.25, 0.014)
    finish("Temple_FloorGold", bm, GOLD)


def pointed_arch(w, spring, n=9):
    """The left half and right half of a pointed arch of half-width w: two arcs meeting at the apex."""
    c = 0.45 * w
    r = w + c
    end = math.acos(c / r)
    left = [(c - r * math.cos(end * i / n), spring + r * math.sin(end * i / n)) for i in range(n + 1)]
    return left + [(-x, y) for x, y in reversed(left[:-1])]


def arch_panel(bm, P, t, out, w, W, spring, top, depth):
    """A wall of half-width W from `spring` to `top` with a pointed arch cut from it, its face mapped by ORNAMENT."""
    hole = pointed_arch(w, spring)
    m = len(hole)
    edge = [(-W, spring), (-W, top), (W, top), (W, spring)]
    lengths = [math.dist(edge[i], edge[i + 1]) for i in range(3)]
    total = sum(lengths)
    rim = []
    for i in range(m):
        s, leg = i / (m - 1) * total, 0
        while leg < 2 and s > lengths[leg]:
            s -= lengths[leg]
            leg += 1
        k = s / lengths[leg]
        rim.append((edge[leg][0] + (edge[leg + 1][0] - edge[leg][0]) * k, edge[leg][1] + (edge[leg + 1][1] - edge[leg][1]) * k))
    uv = bm.loops.layers.uv.verify()

    def point(p, side):
        return P + t * p[0] + UP * p[1] + out * side * depth / 2

    for side in (1, -1):
        a = [bm.verts.new(point(p, side)) for p in hole]
        b = [bm.verts.new(point(p, side)) for p in rim]
        for i in range(m - 1):
            f = bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
            for loop in f.loops:
                c = loop.vert.co - P
                loop[uv].uv = (c.dot(t) / (2 * W) + 0.5, (c.y - spring) / (top - spring))
    for i in range(m - 1):  # the reveal, inside the arch
        f = bm.faces.new((bm.verts.new(point(hole[i], 1)), bm.verts.new(point(hole[i + 1], 1)),
                          bm.verts.new(point(hole[i + 1], -1)), bm.verts.new(point(hole[i], -1))))
        for loop in f.loops:
            loop[uv].uv = (0.5, 0.06)


def gold_trace(bm, pts, r=0.045):
    """A thin gilded moulding along a line of points."""
    for a, b in zip(pts, pts[1:]):
        d = b - a
        length = d.length
        if length < 1e-4:
            continue
        res = bmesh.ops.create_cone(bm, cap_ends=False, cap_tris=False, segments=5, radius1=r, radius2=r, depth=length * 1.04)
        q = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ q, verts=res["verts"])


def build_colonnade():
    piers, arches, band, gold = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
    chord = 2 * R * math.sin(math.pi / BAYS)
    for i in range(BAYS):
        deg = 15 + i * 30
        d, at = direction(deg), C + direction(deg) * R
        box(piers, at + UP * 0.28, (1.42, 0.56, 1.22), deg)      # plinth
        box(piers, at + UP * (TOP / 2), (1.12, TOP, 0.92), deg)
        for s in (1, -1):
            column(piers, at + d * (s * 0.56), 0.24, TOP, segs=10)
        box(gold, at + UP * 0.6, (1.2, 0.09, 1.0), deg)
        box(gold, at + UP * (SPRING - 0.14), (1.26, 0.26, 1.08), deg)  # capital
    for i in range(BAYS):
        deg = i * 30
        d, t = direction(deg), direction(deg + 90)
        at = C + d * (R * math.cos(math.pi / BAYS))
        W, w = chord / 2 - 0.5, chord / 2 - 0.62
        arch_panel(arches, at, t, d, w, W, SPRING, TOP, 0.56)
        hole = pointed_arch(w - 0.03, SPRING)
        for side in (1, -1):
            gold_trace(gold, [at + t * x + UP * y + d * side * 0.3 for x, y in hole])
            apex = at + UP * (hole[9][1] + 0.1) + d * side * 0.3
            res = bmesh.ops.create_icosphere(gold, subdivisions=1, radius=0.17)
            bmesh.ops.transform(gold, matrix=Matrix.Translation(apex) @ Matrix.Diagonal((1.2, 1.5, 0.45, 1.0)), verts=res["verts"])
        if i not in (0, 6):  # a thin gilded rail across the bays that aren't ways through
            for y in (0.95, 0.55):
                gold_trace(gold, [at + t * (-W) + UP * y, at + t * W + UP * y], 0.025)
    # The ring above the arches: a carved band, open to the sky.
    wall_ring(band, R + 0.62, TOP, TOP + 1.0, uv_tiles=BAYS * 2)
    wall_ring(band, R - 0.62, TOP, TOP + 1.0, uv_tiles=BAYS * 2)
    finish("Temple_Band", band, ORNAMENT, project_uv=False, recalc=False)
    cap = bmesh.new()
    annulus(cap, R - 0.72, R + 0.72, TOP + 1.0)
    annulus(cap, R - 0.62, R + 0.62, TOP + 0.001)
    finish("Temple_BandCap", cap, IVORY)
    for r in (R + 0.65, R - 0.65):
        for y in (TOP + 0.04, TOP + 0.98):
            gold_trace(gold, [C + direction(k * 5.0) * r + UP * y for k in range(73)], 0.05)
    finish("Temple_Piers", piers, PIER)
    finish("Temple_Arches", arches, ORNAMENT, project_uv=False, recalc=False)
    finish("Temple_Gold", gold, GOLD)


def build_altar():
    bm = bmesh.new()
    column(bm, C, 1.15, STEP, segs=24)
    finish("Temple_Altar", bm, IVORY)
    bm = bmesh.new()
    annulus(bm, 0.95, 1.05, STEP + 0.003, segs=32)
    finish("Temple_AltarGold", bm, GOLD)


def build_cauldron():
    """A three-legged bronze ding: a round belly, a flared lip, two upright loop handles, thick columnar legs. The body
    is turned from a profile, its texture wrapped round it; legs and handles share the cast skin."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.verify()
    base = C + UP * STEP
    # (radius, height) from the bottom centre up over the belly to the lip and down inside it.
    profile = [(0.02, 0.42), (0.22, 0.43), (0.38, 0.5), (0.47, 0.6), (0.49, 0.68), (0.46, 0.76), (0.41, 0.84), (0.4, 0.92),
               (0.43, 0.98), (0.5, 1.03), (0.51, 1.06), (0.47, 1.06), (0.42, 1.0), (0.39, 0.92)]
    segs = 40
    rings = [[bm.verts.new(base + direction(i / segs * 360) * r + UP * y) for i in range(segs + 1)] for r, y in profile]
    tall = 1.06 - 0.42
    for k in range(len(rings) - 1):
        for i in range(segs):
            f = bm.faces.new((rings[k][i], rings[k][i + 1], rings[k + 1][i + 1], rings[k + 1][i]))
            for loop in f.loops:
                p = loop.vert.co - base
                a = (math.atan2(p.x, p.z) / math.tau) % 1.0
                if i == segs - 1 and a < 0.5:
                    a += 1.0  # (the last column wraps to 1, not back to 0)
                loop[uv].uv = (a * 2.0, min(0.99, (p.y - 0.42) / tall))
    # Legs and handles: plain cast bronze (the texture's clean lower edge).
    def cast(tmp_verts):
        faces = {f for v in tmp_verts for f in v.link_faces}
        for f in faces:
            for loop in f.loops:
                p = loop.vert.co
                loop[uv].uv = (p.x * 0.7 + p.z * 0.7, 0.03 + (p.y * 0.3 % 0.12))
    for deg in (60, 180, 300):
        at = base + direction(deg + 180) * 0.3
        res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=12, radius1=0.085, radius2=0.11, depth=0.56)
        bmesh.ops.transform(bm, matrix=Matrix.Translation(at + UP * 0.28) @ Matrix.Rotation(math.radians(-90), 4, "X"), verts=res["verts"])
        cast(res["verts"])
    for side in (-1, 1):
        r = 0.45
        pieces = [((side * r, 1.04), (0.09, 0.1)), ((side * (r + 0.02), 1.2), (0.05, 0.34)), ((side * (r + 0.02), 1.37), (0.13, 0.05))]
        for (x, y), (t, h) in pieces:
            res = bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.transform(bm, matrix=Matrix.Translation(base + Vector((x, y, 0))) @ Matrix.Diagonal((0.05 if t == 0.05 else 0.07, h, 0.16 if t != 0.05 else 0.05, 1.0)), verts=res["verts"])
            cast(res["verts"])
        for z in (-0.075, 0.075):
            res = bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.transform(bm, matrix=Matrix.Translation(base + Vector((side * (r + 0.02), 1.2, z))) @ Matrix.Diagonal((0.06, 0.36, 0.05, 1.0)), verts=res["verts"])
            cast(res["verts"])
    finish("Temple_Cauldron", bm, BRONZE, smooth=True, project_uv=False)
    bm = bmesh.new()
    column(bm, base + UP * 0.9, 0.39, 0.02, segs=24)
    finish("Temple_CauldronFire", bm, FIRE)


def build_lantern():
    with bpy.data.libraries.load(LANTERN + ".blend") as (src, dst):
        dst.meshes = [m for m in src.meshes]
    me = dst.meshes[0]
    with open(LANTERN + ".json") as f:
        for m in json.load(f)["materials"]:
            MATERIALS[m["name"]] = m
    obj = bpy.data.objects.new("Temple_Lantern", me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def build_seal():
    """A sealed gate in the bay straight ahead: two stone leaves with a gilded edge. It breaks when the way opens."""
    bm = bmesh.new()
    at = C + direction(0) * (R * math.cos(math.pi / BAYS))
    box(bm, at + UP * 2.35, (3.9, 4.7, 0.42))
    for side in (-1, 1):  # its gilding: a raised frame, a centre seam, and a sun over the seam
        for y, w, h in ((0.3, 3.5, 0.14), (2.4, 3.5, 0.14), (4.5, 3.5, 0.14)):
            box(bm, at + UP * y + Vector((0, 0, side * 0.24)), (w, h, 0.06))
        box(bm, at + UP * 2.4 + Vector((0, 0, side * 0.24)), (0.14, 4.3, 0.06))
        column_sun = at + UP * 2.4 + Vector((0, 0, side * 0.24))
        for k in range(8):
            box(bm, column_sun, (0.1, 1.5, 0.05), 0.0)
            bm.verts.ensure_lookup_table()
            turn = Matrix.Translation(column_sun) @ Matrix.Rotation(k * math.pi / 8, 4, "Z") @ Matrix.Translation(-column_sun)
            bmesh.ops.transform(bm, matrix=turn, verts=bm.verts[-8:])
        box(bm, column_sun + Vector((0, 0, side * 0.02)), (0.3, 0.3, 0.08))  # the sun's hub
    finish("Temple_ExitSeal", bm, SEAL)
    return at


def build_bridge():
    z0, z1 = C.z + FLOOR_R - 0.3, SHRINE_Z - 4.0
    bm = bmesh.new()
    box(bm, Vector((0, -0.3, (z0 + z1) / 2)), (3.0, 0.6, z1 - z0))
    finish("Temple_Bridge", bm, PAVING)
    bm = bmesh.new()
    for s in (-1, 1):
        for k in range(7):
            z = z0 + (z1 - z0) * k / 6
            box(bm, Vector((s * 1.42, 0.45, z)), (0.16, 0.9, 0.16))
    finish("Temple_BridgePosts", bm, IVORY)
    bm = bmesh.new()
    for s in (-1, 1):
        gold_trace(bm, [Vector((s * 1.42, 0.92, z0)), Vector((s * 1.42, 0.92, z1))], 0.05)
    finish("Temple_BridgeRail", bm, GOLD)


def statue(bm, at, facing):
    """A tall robed figure on a plinth, hands folded, more shape than likeness."""
    box(bm, at + UP * 0.4, (0.95, 0.8, 0.95))
    column(bm, at + UP * 0.8, 0.42, 2.5, 0.2, segs=12)
    column(bm, at + UP * 3.3, 0.24, 0.22, 0.17, segs=10)
    blob(bm, at + UP * 3.62, 0.2, 1.25, 2, seed=at.x)
    box(bm, at + UP * 2.2 + facing * 0.2, (0.6, 0.2, 0.26))
    column(bm, at + UP * 3.82, 0.22, 0.32, 0.03, segs=8)   # a hood's peak


def build_shrine():
    z, stone, gold, orb, leaf = SHRINE_Z, bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
    column(stone, Vector((0, WATER_Y - 0.6, z - 0.5)), 5.2, 0.6 - WATER_Y, segs=40)
    finish("Temple_ShrineFloor", stone, PAVING)
    arches, stone = bmesh.new(), bmesh.new()
    P, t, out = Vector((0, 0, z + 1.6)), Vector((1, 0, 0)), Vector((0, 0, 1))
    for s in (-1, 1):
        box(stone, Vector((s * 2.15, 3.0, z + 1.6)), (1.1, 6.0, 0.9))
        column(stone, Vector((s * 1.72, 0, z + 1.08)), 0.2, 6.0, segs=10)
    arch_panel(arches, P, t, out, 1.6, 1.62, 2.4, 6.4, 0.9)
    hole = pointed_arch(1.56, 2.4)
    gold_trace(gold, [P + t * x + UP * y - out * 0.5 for x, y in hole], 0.07)
    box(stone, P + UP * 3.0 + out * 1.4, (4.2, 6.0, 0.3))                       # the back of the niche
    gold_trace(gold, [Vector((-1.25 + 2.5 * i / 16, 6.9 + 0.62 * ((i / 8 - 1) ** 2), z + 1.1)) for i in range(17)], 0.11)  # the crescent
    column(stone, Vector((0, 0, z + 2.3)), 0.34, 1.1, 0.22, segs=12)
    res = bmesh.ops.create_icosphere(orb, subdivisions=3, radius=0.3)
    bmesh.ops.transform(orb, matrix=Matrix.Translation(Vector((0, 1.5, z + 2.3))), verts=res["verts"])
    for k in range(26):  # greenery spilling over the arch
        x = random.uniform(-3.0, 3.0)
        blob(leaf, Vector((x, 6.3 + random.uniform(-0.6, 0.4) - abs(x) * 0.5, z + 1.4 + random.uniform(-0.4, 0.5))), random.uniform(0.35, 0.7), 0.8, 2, seed=k)
    statue(stone, Vector((-3.7, 0, z - 0.5)), Vector((0, 0, -1)))
    statue(stone, Vector((3.7, 0, z - 0.5)), Vector((0, 0, -1)))
    finish("Temple_Shrine", stone, IVORY)
    finish("Temple_ShrineArch", arches, ORNAMENT, project_uv=False, recalc=False)
    finish("Temple_ShrineGold", gold, GOLD)
    finish("Temple_Orb", orb, ORB, smooth=True)
    finish("Temple_Vines", leaf, LEAF, smooth=True)
    bm = bmesh.new()
    for s in (-1, 1):
        box(bm, Vector((s * 2.15, 3.3, z + 1.1)), (0.5, 2.4, 0.02))
    finish("Temple_Banners", bm, BANNER)
    return Vector((0, 0, z + 0.4))


def build_statues():
    bm = bmesh.new()
    for deg in (-46, 46):
        statue(bm, C + direction(deg) * (R - 1.7), -direction(deg))
    finish("Temple_Statues", bm, IVORY)


def bank_height(x, z):
    d = math.hypot(x, z - C.z)
    k = max(0.0, min(1.0, (d - 62) / 30))
    return WATER_Y - 1.0 + k * k * (3 - 2 * k) * (9 + 7 * noise.noise(Vector((x * 0.02, 3, z * 0.02)))) + \
        noise.noise(Vector((x * 0.07, 1, z * 0.07))) * 1.6 * k


def build_world():
    bm = bmesh.new()
    annulus(bm, 0.5, 118, WATER_Y, segs=64)
    finish("Temple_Water", bm, WATER)
    bm = bmesh.new()
    rings = [54 + k * 5.0 for k in range(16)]
    grid = [[bm.verts.new(Vector((C.x + math.sin(a) * r, 0, C.z + math.cos(a) * r))) for a in (i / 96 * math.tau for i in range(96))] for r in rings]
    for row in grid:
        for v in row:
            v.co.y = bank_height(v.co.x, v.co.z)
    for a, b in zip(grid, grid[1:]):
        for i in range(96):
            j = (i + 1) % 96
            bm.faces.new((a[i], b[i], b[j], a[j]))
    finish("Temple_Banks", bm, BANK)
    bm = bmesh.new()
    for k in range(170):
        a = random.uniform(0, 360)
        r = random.uniform(66, 112)
        p = C + direction(a) * r
        y = bank_height(p.x, p.z)
        size = random.uniform(2.2, 4.6)
        for b in range(2):
            blob(bm, p + Vector((random.uniform(-1.5, 1.5), y + size * (1.1 + b * 0.8), random.uniform(-1.5, 1.5))), size * (1 - b * 0.3), 1.35, 2, seed=k + b)
    finish("Temple_Trees", bm, LEAF, smooth=True)


def main():
    build_floor()
    build_colonnade()
    build_altar()
    build_cauldron()
    seal = build_seal()
    build_bridge()
    threshold = build_shrine()
    build_statues()
    build_world()
    build_lantern()
    out = os.path.join(PROJECT, FOLDER)
    os.makedirs(out, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=False, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
    base = os.path.splitext(OUT_FBX)[0]
    with open(base + ".materials.json", "w") as f:
        json.dump({"materials": list(MATERIALS.values())}, f, indent=1)
    to_unity = TO_BLENDER.to_3x3().inverted()
    with open(base + ".placements.json", "w") as f:
        json.dump({"objects": [{"name": o.name, "position": list(to_unity @ o.matrix_world.translation),
                                "rotation": [0, 0, 0, 1], "scale": 1.0} for o in bpy.data.objects if o.type == "MESH"]}, f, indent=1)
    with open(base + ".passage.json", "w") as f:
        json.dump({"mouth": list(seal), "threshold": list(threshold), "facing": [0.0, 0.0, -1.0]}, f, indent=1)
    tris = sum(len(p.vertices) - 2 for o in bpy.data.objects if o.type == "MESH" for p in o.data.polygons)
    print(f"[Temple] ~{tris} triangles → {OUT_FBX}")


main()
