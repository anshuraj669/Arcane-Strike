"""
Builds the monster world's extras (remains of a lost battle, spawn rocks, debris and flyer templates) for Unity:

    blender -b --disable-autoexec --python Tools/blender/build_world_extras.py

Writes Assets/_Project/Art/Environment/MonsterWorld/WorldExtras.fbx with WorldExtras.materials.json,
WorldExtras.placements.json and WorldExtras.points.json (same conventions as build_monster_world.py, which this
script reads for the ledge's height, the clear patches and where the world's rocks stand).

- Debris_00..: more of the battle strewn over the ledge: skulls (whole, cracked, half buried, piled into cairns),
  horned beast skulls, shattered breastplates, pauldrons, gauntlets, split helmets, round and kite shields (painted,
  split, clawed, leaning or driven into the ground), broken swords and spears, arrows, torn banners, heaps of mail.
  Small pieces only near the middle of the ledge (where the fight is); a clear path out from the cave.
- Scatter_Rocks_Ledge / Scatter_Bones_Ledge: small stones, loose bones and more skeletons strewn over the ledge.
- Scatter_Rocks_Plains / Scatter_Bones_Plains: boulders, skeletons and the great ribcages of dead beasts across the
  plains below. Each is one mesh in world position (at the origin) for one draw call, with no collider: the stones on
  the ledge are small enough to step over, and nothing here gets in the fight's way.

Every object is built around its own pivot (Unity metres: x right, y up, z forward) and placed by the placements file:
- Remains_00..: battle remains sitting on the ledge (skeletons, skulls, broken weapons, helmets, shields, a beast).
- SpawnRock_0..4 / SpawnRockCracks_0..4: boulders monsters burst out of (pivot at the base centre), with glowing
  crack ribbons on their surface (material M_World_RockCrack, emissive; brighten it before the burst).
- RockChunk_0..5: debris pieces (pivot at the centre) copied when a rock bursts; parked at y = -100.
- Bird_Body / Bird_WingL / Bird_WingR and Wyvern_Body / Wyvern_WingL / Wyvern_WingR: flyer templates parked out
  of sight. Bodies face +z; each wing's pivot is its shoulder joint and the wing lies spread level along -x (left)
  or +x (right), so a rotation about the wing's local z axis flaps it (+angle lifts the right wing, -angle the left).
WorldExtras.points.json: spawnRocks (rock base positions), spawnRockHeights, birdWings / wyvernWings (shoulder
offsets from each body's pivot).
"""

import json
import math
import os
import random
from contextlib import contextmanager

import bmesh
import bpy
from mathutils import Matrix, Vector, noise
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(PROJECT, "Assets/_Project/Art/Environment/MonsterWorld")
OUT_FBX = os.path.join(OUT_DIR, "WorldExtras.fbx")
WORLD_SRC = os.path.join(HERE, "build_monster_world.py")

TO_BLENDER = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0))).to_4x4()
I4 = Matrix.Identity(4)
UP = Vector((0, 1, 0))


# ---------------------------------------------------------------- the world (ledge height, pools, its rocks)


def load_world():
    """Runs build_monster_world.py without its main() to borrow ground_height, POOLS and pool_radius, and replays
    its random sequence to rebuild World_Rocks and the cave mouth (to keep clear of them)."""
    src = open(WORLD_SRC).read().rstrip()
    assert src.endswith("main()"), "build_monster_world.py no longer ends with main()"
    ns = {"__name__": "monster_world", "__file__": WORLD_SRC}
    exec(compile(src[: -len("main()")], WORLD_SRC, "exec"), ns)
    for step in ("build_cracks", "build_peak", "build_mountains"):  # the random calls made before build_rocks
        ns[step]()
    rocks = ns["build_rocks"]()
    mouth, _tunnel = ns["build_cave_mouth"]()
    deps = bpy.context.evaluated_depsgraph_get()
    blockers = [BVHTree.FromObject(o, deps) for o in (rocks, mouth)]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    return ns, blockers


W, BLOCKERS = load_world()
ground_height = W["ground_height"]
pool_radius = W["pool_radius"]
POOLS = W["POOLS"]


def blocked(x, z):
    """True where a world rock or the cave mound stands above the ground at (x, z)."""
    g = ground_height(x, z)
    origin = TO_BLENDER @ Vector((x, 60.0, z))
    for bvh in BLOCKERS:
        hit = bvh.ray_cast(origin, Vector((0, 0, -1)), 200.0)
        if hit[0] is not None and hit[0].z > g + 0.05:
            return True
    return False


def pool_gap(x, z):
    """Distance from (x, z) to the nearest pool's rim (negative inside a pool)."""
    return min(math.hypot(x - p[0], z - p[1]) - pool_radius(p, x, z) for p in POOLS)


# ---------------------------------------------------------------- materials and finishing

MATERIALS = []  # entries, in the order of material indices


def material(name, color, roughness=0.8, metallic=0.0, emission=None, tex=None, tile=1.0):
    full = "M_World_" + name
    m = bpy.data.materials.new(full)
    m.diffuse_color = (*color, 1.0)
    m.metallic = metallic
    m.roughness = roughness
    MATERIALS.append({"name": full, "color": list(color), "smoothness": 1.0 - roughness, "metallic": metallic,
                      "alpha": 1.0, "emission": list(emission) if emission else [0, 0, 0], "texture": tex or "",
                      "tile": tile, "emissive": emission is not None, "_mat": m})
    return len(MATERIALS) - 1


BONE = material("Bone", (0.72, 0.68, 0.58), 0.9)
SHADOW = material("BoneShadow", (0.05, 0.04, 0.035), 0.95)
RUST = material("RustIron", (0.2, 0.11, 0.065), 0.7, metallic=0.45)
WOOD = material("OldWood", (0.15, 0.095, 0.055), 0.9)
LEATHER = material("Leather", (0.11, 0.065, 0.04), 0.85)
ROCK = material("Rock", (0.24, 0.21, 0.21), 0.8, tex="Rock058", tile=4.0)
ROCKCRACK = material("RockCrack", (0.6, 0.16, 0.03), 0.5, emission=(1.6, 0.4, 0.05))
FEATHER = material("BirdFeather", (0.035, 0.03, 0.035), 0.75)
BEAK = material("BirdBeak", (0.22, 0.2, 0.17), 0.6)
WSKIN = material("WyvernSkin", (0.12, 0.085, 0.13), 0.65)
WMEMBRANE = material("WyvernMembrane", (0.36, 0.05, 0.05), 0.7)
WEYES = material("WyvernEyes", (1.0, 0.5, 0.1), 0.3, emission=(5.0, 2.0, 0.25))
WHORN = material("WyvernHorn", (0.28, 0.25, 0.21), 0.55)
# (the debris' own, after the rest so the earlier materials keep their order)
STEEL = material("RustedSteel", (0.3, 0.17, 0.09), 0.62, metallic=0.55)
BRONZE = material("TarnishedBronze", (0.3, 0.21, 0.1), 0.5, metallic=0.7)
PAINT = material("ShieldPaint", (0.3, 0.06, 0.045), 0.9)
EMBLEM = material("ShieldEmblem", (0.48, 0.4, 0.22), 0.85)
CLOTH = material("TornBanner", (0.22, 0.035, 0.04), 0.95)
MAIL = material("ChainMail", (0.16, 0.12, 0.09), 0.55, metallic=0.6)

TRIS = {}


def open_layer(bm):
    return bm.faces.layers.int.get("open") or bm.faces.layers.int.new("open")


def finish(name, bm, smooth=True):
    """A bmesh in Unity terms -> an object at the Blender origin, in Blender's axes, with box-projected UVs.
    Closed parts get outward normals; faces flagged "open" (ribbons, membranes) keep the winding they were built
    with (their right-hand normal must already face outward in Unity terms)."""
    lay = open_layer(bm)
    closed = [f for f in bm.faces if not f[lay]]
    bmesh.ops.recalc_face_normals(bm, faces=closed)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="EAR_CLIP")
    used = sorted({f.material_index for f in bm.faces})
    remap = {m: i for i, m in enumerate(used)}
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        tile = MATERIALS[f.material_index]["tile"]
        n = f.normal
        axis = max(range(3), key=lambda k: abs(n[k]))
        a, b = [k for k in range(3) if k != axis]
        for loop in f.loops:
            loop[uv].uv = (loop.vert.co[a] / tile, loop.vert.co[b] / tile)
        f.smooth = smooth
        f.material_index = remap[f.material_index]
    bmesh.ops.transform(bm, matrix=TO_BLENDER, verts=bm.verts[:])
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])  # TO_BLENDER mirrors
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in used:
        me.materials.append(MATERIALS[m]["_mat"])
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    TRIS[name] = sum(len(p.vertices) - 2 for p in me.polygons)
    print(f"[Extras] {name}: {TRIS[name]} tris")
    return obj


# ---------------------------------------------------------------- primitives (Unity coordinates)


@contextmanager
def frame(bm, M):
    """Everything built inside is transformed by M afterwards (frames nest)."""
    n0 = set(bm.verts)
    yield
    new = new_verts(bm, n0)
    bmesh.ops.transform(bm, matrix=M, verts=new)


def new_verts(bm, before):
    """The verts made since the snapshot `before` (a set of verts; bmesh ops may reuse freed slots, so order
    can't be trusted)."""
    return [v for v in bm.verts if v not in before]


def basis(x, y, z, origin=(0, 0, 0)):
    """4x4 matrix whose columns are the local axes x, y, z at origin."""
    M = Matrix((tuple(x), tuple(y), tuple(z))).transposed().to_4x4()
    M.translation = Vector(origin)
    return M


def look_frame(top, face, origin=(0, 0, 0)):
    """A frame with local +y along top and local +z along face (orthogonalised)."""
    face = Vector(face).normalized()
    top = Vector(top)
    top = (top - face * top.dot(face)).normalized()
    return basis(top.cross(face), top, face, origin)


def set_mat(faces, mat):
    for f in faces:
        f.material_index = mat


def ellipsoid(bm, c, r, mat, u=8, v=6, rot=None):
    res = bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    M = Matrix.Translation(Vector(c)) @ (rot if rot is not None else I4) @ Matrix.Diagonal((r[0], r[1], r[2], 1.0))
    bmesh.ops.transform(bm, matrix=M, verts=res["verts"])
    set_mat({f for v in res["verts"] for f in v.link_faces}, mat)
    return res["verts"]


def tube(bm, pts, radii, mat, sides=5, flat=1.0, up=None, caps=True):
    """A tube along pts with per-point radii (0 = a point). flat squashes the cross-section across `up`."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * n
    tans = [(pts[min(n - 1, i + 1)] - pts[max(0, i - 1)]).normalized() for i in range(n)]
    ref = Vector(up) if up is not None else (UP if abs(tans[0].y) < 0.9 else Vector((1, 0, 0)))
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
    set_mat(faces, mat)
    return rings


def bone(bm, a, b, r, mat=BONE, sides=5):
    a, b = Vector(a), Vector(b)
    d = b - a
    return tube(bm, [a, a + d * 0.1, b - d * 0.1, b], [r * 1.75, r, r, r * 1.75], mat, sides)


def lathe(bm, profile, segs, mats):
    """Revolves (radius, y) points about +y; mats is one material or one per profile segment."""
    rings = []
    for r, y in profile:
        if r <= 1e-6:
            rings.append([bm.verts.new((0, y, 0))])
        else:
            rings.append([bm.verts.new((math.cos(a) * r, y, math.sin(a) * r))
                          for a in (k / segs * math.tau for k in range(segs))])
    for i, (r0, r1) in enumerate(zip(rings, rings[1:])):
        mat = mats[i] if isinstance(mats, (list, tuple)) else mats
        fs = []
        for k in range(segs):
            k1 = (k + 1) % segs
            if len(r0) == 1:
                fs.append(bm.faces.new((r0[0], r1[k], r1[k1])))
            elif len(r1) == 1:
                fs.append(bm.faces.new((r0[k], r0[k1], r1[0])))
            else:
                fs.append(bm.faces.new((r0[k], r0[k1], r1[k1], r1[k])))
        set_mat(fs, mat)
    return rings


def slab(bm, outline, y, thick, mat, bend=None):
    """A flat plate from an (x, z) outline, `thick` through, at height y (+ bend(x, z))."""
    top, bot = [], []
    for x, z in outline:
        dy = bend(x, z) if bend else 0.0
        top.append(bm.verts.new((x, y + dy + thick / 2, z)))
        bot.append(bm.verts.new((x, y + dy - thick / 2, z)))
    fs = [bm.faces.new(top), bm.faces.new(bot[::-1])]
    n = len(outline)
    for k in range(n):
        k1 = (k + 1) % n
        fs.append(bm.faces.new((top[k], bot[k], bot[k1], top[k1])))
    set_mat(fs, mat)
    return top + bot


def craggy(bm, rng, size, subdiv, mat, chops=5, octaves=(0.22, 0.07)):
    """A craggy rock: a noisy icosphere of `size` (x, y, z extents) round the origin, with flat chopped facets."""
    res = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    verts = res["verts"]
    seed = Vector((rng.uniform(0, 100), rng.uniform(0, 100), rng.uniform(0, 100)))
    for v in verts:
        d = v.co.normalized()
        k = 1.0 + octaves[0] * noise.noise(d * 1.4 + seed) + octaves[1] * noise.noise(d * 4.2 + seed * 1.3)
        v.co = Vector((d.x * k * size[0] / 2, d.y * k * size[1] / 2, d.z * k * size[2] / 2))
    for _ in range(chops):
        n = Vector((rng.uniform(-1, 1), rng.uniform(-0.3, 0.9), rng.uniform(-1, 1))).normalized()
        reach = max(v.co.dot(n) for v in verts)
        cut = reach * rng.uniform(0.66, 0.88)
        for v in verts:
            e = v.co.dot(n)
            if e > cut:
                v.co -= n * (e - cut)
    set_mat({f for v in verts for f in v.link_faces}, mat)
    return verts


# ---------------------------------------------------------------- remains: parts


def human_skull(bm, jaw_open=0.0, jaw=True):
    """Skull round its centre: face +z, top +y (about 15 cm wide, 21 cm tall with the jaw)."""
    ellipsoid(bm, (0, 0.03, -0.012), (0.071, 0.07, 0.092), BONE, 10, 7)          # cranium
    ellipsoid(bm, (0, -0.024, 0.046), (0.054, 0.044, 0.044), BONE, 8, 5)        # maxilla / cheeks
    ellipsoid(bm, (0, 0.033, 0.064), (0.064, 0.012, 0.02), BONE, 6, 4)          # brow
    for s in (-1, 1):
        ellipsoid(bm, (s * 0.029, 0.003, 0.077), (0.024, 0.022, 0.013), SHADOW, 6, 4)  # eye sockets
    ellipsoid(bm, (0, -0.032, 0.085), (0.011, 0.017, 0.008), SHADOW, 5, 3)      # nasal cavity
    tube(bm, [(-0.034, -0.062, 0.055), (-0.018, -0.064, 0.076), (0.018, -0.064, 0.076), (0.034, -0.062, 0.055)],
         0.009, BONE, 4)                                                         # upper teeth
    if jaw:
        n0 = set(bm.verts)
        tube(bm, [(-0.056, -0.015, 0.0), (-0.053, -0.07, 0.008), (-0.038, -0.088, 0.05), (0, -0.094, 0.078),
                  (0.038, -0.088, 0.05), (0.053, -0.07, 0.008), (0.056, -0.015, 0.0)], 0.011, BONE, 4)
        tube(bm, [(-0.03, -0.075, 0.055), (0, -0.08, 0.072), (0.03, -0.075, 0.055)], 0.008, BONE, 4)  # lower teeth
        hinge = Vector((0, -0.015, 0))
        M = Matrix.Translation(hinge) @ Matrix.Rotation(jaw_open, 4, "X") @ Matrix.Translation(-hinge)
        bmesh.ops.transform(bm, matrix=M, verts=new_verts(bm, n0))


def skull_on_ground(bm, pos, top, face, sink=0.02, jaw_open=0.0, jaw=True):
    n0 = set(bm.verts)
    with frame(bm, look_frame(top, face, pos)):
        human_skull(bm, jaw_open, jaw)
    vs = new_verts(bm, n0)
    low = min(v.co.y for v in vs)
    for v in vs:
        v.co.y -= low + sink


def helmet(bm, rng, dents=2, guard=True):
    """A dented iron helmet round its rim's centre: dome up +y, nose guard at +z."""
    n0 = set(bm.verts)
    lathe(bm, [(0, 0.235), (0.045, 0.225), (0.085, 0.18), (0.11, 0.11), (0.118, 0.04), (0.122, 0.0),
               (0.128, -0.01), (0.11, -0.008), (0.106, 0.04), (0.098, 0.11), (0.075, 0.165), (0.0, 0.205)], 12, RUST)
    vs = new_verts(bm, n0)
    for v in vs:  # a low bowl
        v.co.y *= 0.72
    for _ in range(dents):
        a = rng.uniform(0, math.tau)
        c = Vector((math.cos(a) * 0.1, rng.uniform(0.08, 0.18), math.sin(a) * 0.1))
        for v in vs:
            d = (v.co - c).length
            if d < 0.07:
                radial = Vector((v.co.x, 0, v.co.z))
                if radial.length > 1e-4:
                    v.co -= radial.normalized() * 0.03 * (1 - d / 0.07)
    if guard:
        tube(bm, [(0, 0.07, 0.122), (0, -0.07, 0.128)], 0.012, RUST, 4, flat=0.45, up=(1, 0, 0))


def sword(bm, rng, blade=0.8, broken=False):
    """A sword round its guard: blade down -y, grip up +y, edges along x."""
    if broken:
        L = rng.uniform(0.28, 0.48)
        rings = tube(bm, [(0, 0, 0), (0, -L * 0.6, 0), (0, -L, 0)], [0.024, 0.023, 0.022], RUST, 4, flat=0.22,
                     up=(1, 0, 0))
        for v in rings[-1]:
            v.co.y += rng.uniform(-0.045, 0.03)  # snapped, jagged end
    else:
        tube(bm, [(0, 0, 0), (0, -blade * 0.5, 0), (0, -blade * 0.9, 0), (0, -blade, 0)], [0.024, 0.022, 0.017, 0.0],
             RUST, 4, flat=0.22, up=(1, 0, 0))
    tube(bm, [(-0.11, -0.012, 0), (0, 0.0, 0), (0.11, -0.012, 0)], 0.013, RUST, 4)  # cross-guard
    tube(bm, [(0, 0.005, 0), (0, 0.14, 0)], 0.015, LEATHER, 5)                       # grip
    ellipsoid(bm, (0, 0.163, 0), (0.026, 0.022, 0.026), RUST, 6, 4)                   # pommel


def spear(bm, rng, length=2.1, broken=False):
    """A spear from its butt (origin) up +y."""
    top = length * (rng.uniform(0.45, 0.6) if broken else 1.0)
    bend = rng.uniform(-0.03, 0.03)
    tube(bm, [(0, 0, 0), (bend, top * 0.5, 0), (0, top, 0)], 0.019, WOOD, 5)
    if not broken:
        tube(bm, [(0, top - 0.04, 0), (0, top + 0.06, 0)], 0.024, RUST, 5)
        tube(bm, [(0, top + 0.06, 0), (0, top + 0.12, 0), (0, top + 0.34, 0)], [0.03, 0.046, 0.0], RUST, 4, flat=0.25,
             up=(1, 0, 0))


def shield(bm, rng):
    """A round wooden shield (r 0.42) round its centre: face +y, iron boss and rim."""
    n0 = set(bm.verts)
    lathe(bm, [(0, 0.075), (0.05, 0.07), (0.085, 0.045), (0.1, 0.026), (0.2, 0.024), (0.4, 0.02), (0.425, 0.012),
               (0.425, -0.014), (0.4, -0.02), (0.0, -0.02)], 16,
          [RUST, RUST, RUST, WOOD, WOOD, RUST, RUST, RUST, WOOD])
    a = rng.uniform(0, math.tau)  # a hacked notch in the rim
    for v in new_verts(bm, n0):
        r = math.hypot(v.co.x, v.co.z)
        if r > 0.3:
            da = abs(math.atan2(math.sin(math.atan2(v.co.z, v.co.x) - a), math.cos(math.atan2(v.co.z, v.co.x) - a)))
            if da < 0.3:
                k = (1 - da / 0.3) * 0.08
                v.co.x -= v.co.x / r * k
                v.co.z -= v.co.z / r * k


def ribcage(bm, spine_pts, f_dir, ribs=6, width=0.13, depth=0.21, missing=(), rib_r=0.008, sides=3, phi=2.75,
            mat=BONE, sternum=True, droop=0.07):
    """Rib pairs from the spine points (bottom to top of the thorax) round the chest towards f_dir."""
    tops = []
    for i in range(ribs):
        t = (i + 0.5) / ribs
        k = min(int(t * (len(spine_pts) - 1)), len(spine_pts) - 2)
        a, b = spine_pts[k], spine_pts[k + 1]
        frac = t * (len(spine_pts) - 1) - k
        anchor = a.lerp(b, frac)
        u = (b - a).normalized()
        f = Vector(f_dir) - u * Vector(f_dir).dot(u)
        f.normalize()
        s = u.cross(f)
        bulge = math.sin(math.pi * (i + 1) / (ribs + 1))
        w = width * (0.65 + 0.45 * bulge)
        h = depth * (0.7 + 0.35 * bulge)
        for side in (-1, 1):
            if (i, side) in missing:
                continue
            end = phi if (i, side, "short") not in missing else phi * 0.5
            pts = []
            for j in range(5):
                p = 0.25 + (end - 0.25) * j / 4
                pts.append(anchor + s * (side * w * math.sin(p)) + f * (h / 2 * (1 - math.cos(p))) -
                           u * (droop * p / math.pi))
            tube(bm, pts, [rib_r * 1.3, rib_r, rib_r, rib_r * 0.9, rib_r * 0.7], mat, sides, flat=0.6,
                 up=s)
        tops.append(anchor + f * h * 0.97 - u * droop * 0.9)
    if sternum and len(tops) > 3:
        tube(bm, [tops[1], tops[len(tops) // 2], tops[-1]], [0.01, 0.014, 0.012], BONE, 4, flat=0.5)


def resample(ctrl, n):
    ctrl = [Vector(p) for p in ctrl]
    lens = [0.0]
    for a, b in zip(ctrl, ctrl[1:]):
        lens.append(lens[-1] + (b - a).length)
    out = []
    for i in range(n):
        s = lens[-1] * i / (n - 1)
        k = max(j for j in range(len(lens) - 1) if lens[j] <= s + 1e-9) if s < lens[-1] else len(ctrl) - 2
        seg = lens[k + 1] - lens[k]
        out.append(ctrl[k].lerp(ctrl[k + 1], (s - lens[k]) / seg if seg > 0 else 0))
    return out


def spine(bm, ctrl, n=11, r=0.019):
    pts = resample(ctrl, n)
    tube(bm, pts, [r * (1.15 if i % 2 else 0.8) for i in range(n)], BONE, 5)
    return pts


def hand(bm, wrist, direction, splay):
    d = Vector(direction).normalized()
    side = d.cross(UP)
    if side.length < 1e-3:
        side = Vector((1, 0, 0))
    side.normalize()
    palm = Vector(wrist) + d * 0.045
    ellipsoid(bm, palm, (0.03, 0.012, 0.035), BONE, 5, 3, rot=look_frame(UP, d))
    for k in (-1, 0, 1):
        fd = (d + side * (k * splay)).normalized()
        a = palm + d * 0.03 + side * k * 0.018
        tube(bm, [a, a + fd * 0.05, a + fd * 0.085 + Vector((0, -0.01, 0))], 0.006, BONE, 3)


def foot(bm, ankle, toe_dir):
    d = Vector(toe_dir)
    ellipsoid(bm, Vector(ankle) + d * 0.5, (0.035, 0.018, d.length * 0.6), BONE, 5, 3, rot=look_frame(UP, d))


def arm(bm, shoulder, elbow, wrist, hand_dir=None, splay=0.35):
    shoulder, elbow, wrist = Vector(shoulder), Vector(elbow), Vector(wrist)
    bone(bm, shoulder, elbow, 0.0135)
    d = wrist - elbow
    off = d.cross(UP)
    off = off.normalized() * 0.011 if off.length > 1e-4 else Vector((0.011, 0, 0))
    bone(bm, elbow + off, wrist + off, 0.0085, sides=4)
    bone(bm, elbow - off, wrist - off, 0.0075, sides=4)
    hand(bm, wrist, hand_dir if hand_dir is not None else d, splay)


def leg(bm, hip, knee, ankle, toe_dir):
    hip, knee, ankle = Vector(hip), Vector(knee), Vector(ankle)
    bone(bm, hip, knee, 0.019)
    d = ankle - knee
    off = d.cross(UP)
    off = off.normalized() * 0.015 if off.length > 1e-4 else Vector((0.015, 0, 0))
    bone(bm, knee - off * 0.3, ankle - off * 0.3, 0.015)
    bone(bm, knee + off, ankle + off, 0.0075, sides=4)
    foot(bm, ankle, toe_dir)


def pelvis(bm, centre, u, f):
    """Hip bones round centre: u up the spine, f towards the belly."""
    u, f = Vector(u).normalized(), Vector(f).normalized()
    s = u.cross(f)
    M = basis(s, u, f, centre)
    with frame(bm, M):
        for side in (-1, 1):
            rot = Matrix.Rotation(side * math.radians(40), 4, "Y")
            ellipsoid(bm, (side * 0.072, 0.025, 0.0), (0.062, 0.07, 0.016), BONE, 6, 4, rot=rot)
            tube(bm, [(side * 0.08, -0.04, 0.02), (side * 0.045, -0.085, 0.05), (0, -0.09, 0.06)], 0.012, BONE, 4)
        ellipsoid(bm, (0, -0.005, -0.03), (0.04, 0.06, 0.018), BONE, 6, 4)  # sacrum


def skeleton(bm, rng, spine_ctrl, f_dir, skull_pose, arms=(), legs=(), ribs_missing=(), helmet_on=False, jaw=None):
    """A skeleton: spine_ctrl runs pelvis -> top of the neck; f_dir is the way the chest faces; skull_pose is
    (centre, top, face) or None; arms are (shoulder, elbow, wrist[, hand_dir]); legs (hip, knee, ankle, toe_dir)."""
    pts = spine(bm, spine_ctrl)
    n = len(pts)
    thorax = pts[int(n * 0.42): int(n * 0.86) + 1]
    ribcage(bm, thorax, f_dir, missing=ribs_missing)
    u0 = (pts[2] - pts[0]).normalized()
    f0 = Vector(f_dir) - u0 * Vector(f_dir).dot(u0)
    pelvis(bm, pts[0] - u0 * 0.02, u0, f0)
    neck = pts[int(n * 0.86)]
    for a in arms:
        if a is None:
            continue
        sh = Vector(a[0])
        bone(bm, neck + f0 * 0.03, sh, 0.008, sides=4)  # clavicle
        arm(bm, sh, a[1], a[2], a[3] if len(a) > 3 else None)
    for l in legs:
        if l is not None:
            leg(bm, *l)
    if skull_pose is not None:
        c, top, face = skull_pose
        n0 = set(bm.verts)
        with frame(bm, look_frame(top, face, c)):
            human_skull(bm, jaw if jaw is not None else rng.uniform(0.0, 0.35))
            if helmet_on:
                with frame(bm, Matrix.Translation((0, 0.06, -0.03)) @ Matrix.Rotation(-0.35, 4, "X")):
                    helmet(bm, rng)
        return new_verts(bm, n0)
    return []


def lift_skull(bm, verts, floor=0.0):
    low = min(v.co.y for v in verts)
    if low < floor:
        for v in verts:
            v.co.y += floor - low


# ---------------------------------------------------------------- remains: poses and groups


def supine(bm, rng, arm_on_chest=False, legs_bent=False):
    """A skeleton lying on its back, head towards +z, sprawled; pelvis at the origin."""
    lat = [rng.uniform(-0.035, 0.035) for _ in range(3)]
    ctrl = [(0, 0.045, 0), (lat[0], 0.045, 0.2), (lat[1], 0.05, 0.42), (lat[2], 0.065, 0.6)]
    neck = Vector((lat[1] * 0.8, 0.06, 0.52))
    arms = []
    for sx in (-1, 1):
        sh = neck + Vector((sx * 0.17, -0.02, -0.03))
        if arm_on_chest and sx == 1:
            el = sh + Vector((0.09, 0.0, -0.24))
            wr = Vector((0.03, 0.25, 0.3))
            arms.append((sh, el, wr, Vector((-1, -0.2, 0.3))))
            continue
        a = math.radians(rng.uniform(15, 115))
        el = sh + Vector((sx * math.sin(a), 0, -math.cos(a))) * 0.29
        b = a + math.radians(rng.uniform(-45, 45))
        wr = el + Vector((sx * math.sin(b), 0, -math.cos(b))) * 0.25
        el.y, wr.y = 0.035, 0.03
        arms.append((sh, el, wr))
    legs = []
    for sx in (-1, 1):
        hip = Vector((sx * 0.09, 0.05, -0.05))
        g = math.radians(rng.uniform(3, 28))
        knee = hip + Vector((sx * math.sin(g), 0, -math.cos(g))) * 0.44
        if legs_bent and sx == -1:
            knee = hip + Vector((sx * 0.28, 0.0, -0.33))
        d = g + math.radians(rng.uniform(-25, 25))
        ankle = knee + Vector((sx * math.sin(d), 0, -math.cos(d))) * 0.41
        if legs_bent and sx == -1:
            ankle = knee + Vector((sx * -0.05, 0.0, -0.4))
        knee.y, ankle.y = 0.04, 0.045
        legs.append((hip, knee, ankle, Vector((sx * 0.1, 0.11, -0.07))))
    roll = rng.uniform(-1.1, 1.1)
    face = Vector((math.sin(roll), math.cos(roll), 0.15))
    skull_pos = Vector((lat[2] + rng.uniform(-0.03, 0.03), 0.1, 0.71))
    sv = skeleton(bm, rng, ctrl, (0, 1, 0), (skull_pos, (rng.uniform(-0.2, 0.2), 0.15, 1), face), arms, legs,
                  ribs_missing={(rng.randrange(6), rng.choice((-1, 1)))})
    lift_skull(bm, sv, -0.01)


def g_sprawl_broken_sword(bm, rng):
    supine(bm, rng)
    with frame(bm, Matrix.Translation((0.55, 0.42, 0.15)) @ Matrix.Rotation(0.35, 4, "Z") @ Matrix.Rotation(0.2, 4, "X")):
        sword(bm, rng, broken=True)  # stuck in the ground by the hand


def g_arm_on_chest_helmet(bm, rng):
    supine(bm, rng, arm_on_chest=True, legs_bent=True)
    with frame(bm, Matrix.Translation((-0.42, 0.06, 0.78)) @ Matrix.Rotation(1.9, 4, "Z") @ Matrix.Rotation(0.4, 4, "Y")):
        helmet(bm, rng, dents=3)  # rolled off, on its side


def g_slumped(bm, rng):
    """Sitting slumped against a rock (behind, at -z), legs out towards +z, helmet still on."""
    n0 = set(bm.verts)
    craggy(bm, rng, (1.5, 1.35, 1.0), 3, ROCK, chops=4)
    bmesh.ops.transform(bm, matrix=Matrix.Translation((0.0, 0.42, -0.86)), verts=new_verts(bm, n0))
    u = Vector((0, 0.86, -0.5)).normalized()
    f = Vector((0, 0.5, 0.86)).normalized()
    p = Vector((0, 0.12, 0.02))
    ctrl = [p, p + u * 0.22, p + u * 0.44 + Vector((0.01, 0, 0)), p + u * 0.6 + Vector((0.03, 0, 0.0))]
    neck = p + u * 0.52
    arms = [(neck + Vector((-0.17, -0.02, 0.02)), Vector((-0.28, 0.17, -0.02)), Vector((-0.33, 0.035, 0.2)),
             Vector((0.0, -0.3, 1.0))),
            (neck + Vector((0.17, -0.02, 0.02)), Vector((0.25, 0.26, 0.05)), Vector((0.16, 0.33, 0.3)),
             Vector((-0.4, -0.5, 0.6)))]
    legs = [(Vector((-0.09, 0.1, 0.05)), Vector((-0.13, 0.05, 0.48)), Vector((-0.17, 0.05, 0.88)), Vector((-0.04, 0.14, 0.06))),
            (Vector((0.09, 0.1, 0.05)), Vector((0.16, 0.33, 0.36)), Vector((0.2, 0.05, 0.64)), Vector((0.04, 0.03, 0.15)))]
    top = p + u * 0.6
    skull_pos = top + Vector((0.04, 0.05, 0.09))
    skeleton(bm, rng, ctrl, f, (skull_pos, (0.12, 0.85, 0.3), (0.3, -0.3, 0.9)), arms, legs, helmet_on=True, jaw=0.25)
    with frame(bm, Matrix.Translation((0.5, 0.025, 0.35)) @ Matrix.Rotation(0.25, 4, "Y") @ Matrix.Rotation(-math.pi / 2, 4, "X")):
        sword(bm, rng)  # dropped beside him


def g_sword_through(bm, rng):
    supine(bm, rng)
    with frame(bm, Matrix.Translation((0.03, 0.66, 0.33)) @ Matrix.Rotation(0.16, 4, "Z") @ Matrix.Rotation(-0.1, 4, "X")):
        sword(bm, rng, blade=0.9)


def g_sunk_ribcage(bm, rng):
    """A ribcage half sunk in the ground, a skull and a thigh bone near it, a snapped spear."""
    sp = [Vector((-0.3, -0.07, 0.0)), Vector((0.0, -0.06, 0.02)), Vector((0.3, -0.07, 0.0))]
    tube(bm, resample(sp, 9), [0.02 if i % 2 else 0.016 for i in range(9)], BONE, 5)
    ribcage(bm, resample(sp, 6), (0, 0.9, 0.45), ribs=7, width=0.15, depth=0.3, rib_r=0.01, droop=0.05,
            missing={(1, -1), (5, 1)})
    skull_on_ground(bm, Vector((0.55, 0, 0.35)), (0.3, 0.2, 1), (0.9, 0.5, -0.4), jaw=False)
    bone(bm, (-0.6, 0.02, 0.45), (-0.2, 0.02, 0.72), 0.019)
    with frame(bm, Matrix.Translation((-0.45, -0.3, -0.35)) @ Matrix.Rotation(0.4, 4, "X") @ Matrix.Rotation(-0.2, 4, "Z")):
        spear(bm, rng, broken=True)


def g_skull_pile(bm, rng):
    for c, top, face, jaw in (((0, 0, 0), (0, 1, 0.1), (0.3, 0.1, 1), True), ((0.17, 0, 0.06), (0.4, 0.8, 0), (1, -0.3, 0.6), False),
                              ((-0.12, 0, 0.13), (-0.2, 0.5, 1), (-0.6, 0.8, -0.2), True)):
        n0 = set(bm.verts)
        with frame(bm, look_frame(top, face, c)):
            human_skull(bm, rng.uniform(0, 0.3), jaw)
        vs = new_verts(bm, n0)
        if c[1] == 0:
            low = min(v.co.y for v in vs)
            for v in vs:
                v.co.y -= low + 0.015
    bone(bm, (0.3, 0.02, -0.2), (0.1, 0.02, -0.6), 0.019)
    bone(bm, (-0.35, 0.02, -0.05), (-0.25, 0.02, -0.45), 0.014)
    bone(bm, (-0.15, 0.015, -0.3), (0.1, 0.015, -0.38), 0.009, sides=4)
    with frame(bm, Matrix.Translation((0.45, 0.06, 0.35)) @ Matrix.Rotation(1.7, 4, "X")):
        helmet(bm, rng)


def g_weapons(bm, rng):
    """A spear stuck upright, a broken sword stuck in the ground, a shield fallen on its side against them."""
    with frame(bm, Matrix.Translation((0, -0.3, 0)) @ Matrix.Rotation(0.12, 4, "Z") @ Matrix.Rotation(-0.08, 4, "X")):
        spear(bm, rng)
    with frame(bm, Matrix.Translation((0.55, 0.3, 0.4)) @ Matrix.Rotation(-0.25, 4, "Z")):
        sword(bm, rng, broken=True)
    with frame(bm, Matrix.Translation((-0.32, 0.36, 0.2)) @ Matrix.Rotation(0.5, 4, "Y") @ Matrix.Rotation(1.25, 4, "X")):
        shield(bm, rng)


def g_partial(bm, rng):
    """Legs and hips of a fallen warrior (the rest scattered), a skull and a shield lying face down."""
    p = Vector((0, 0.05, 0))
    tube(bm, resample([p, p + Vector((0.02, 0, 0.22))], 5), [0.016, 0.021, 0.016, 0.021, 0.016], BONE, 5)
    pelvis(bm, p, (0, 0, 1), (0, 1, 0))
    leg(bm, (-0.09, 0.05, -0.05), (-0.2, 0.045, -0.47), (-0.18, 0.045, -0.88), (-0.06, 0.1, -0.08))
    leg(bm, (0.09, 0.05, -0.05), (0.32, 0.045, -0.4), (0.38, 0.045, -0.8), (0.1, 0.09, -0.06))
    ribcage(bm, resample([Vector((0.05, 0.04, 0.45)), Vector((0.1, 0.04, 0.62))], 4), (0, 1, 0), ribs=3,
            missing={(0, 1), (2, -1)}, sternum=False)
    bone(bm, (0.4, 0.02, 0.35), (0.62, 0.02, 0.62), 0.0135)
    skull_on_ground(bm, Vector((-0.35, 0, 0.75)), (-0.4, 0.3, 1), (0.5, 0.6, 0.6))
    with frame(bm, Matrix.Translation((0.7, 0.11, -0.25)) @ Matrix.Rotation(0.15, 4, "X") @ Matrix.Rotation(math.pi + 0.1, 4, "Z")):
        shield(bm, rng)


def g_grave_sword(bm, rng):
    """A sword driven upright into the ground as a grave marker, a helmet and a skull at its foot."""
    with frame(bm, Matrix.Translation((0, 0.62, 0)) @ Matrix.Rotation(0.06, 4, "Z")):
        sword(bm, rng, blade=0.95)
    with frame(bm, Matrix.Translation((0.2, 0.0, 0.18)) @ Matrix.Rotation(0.15, 4, "Z")):
        helmet(bm, rng)
    skull_on_ground(bm, Vector((-0.2, 0, 0.2)), (0, 1, 0.1), (-0.5, 0.1, 1))
    bone(bm, (-0.45, 0.02, -0.2), (-0.15, 0.02, -0.5), 0.019)
    bone(bm, (0.35, 0.015, -0.3), (0.55, 0.015, 0.0), 0.0135)


def g_sprawl_spear(bm, rng):
    supine(bm, rng, legs_bent=True)
    with frame(bm, Matrix.Translation((-0.5, 0.025, 0.4)) @ Matrix.Rotation(-0.3, 4, "Y") @ Matrix.Rotation(-math.pi / 2, 4, "X")):
        spear(bm, rng)  # lying where it fell
    with frame(bm, Matrix.Translation((0.62, 0.18, 0.62)) @ Matrix.Rotation(1.35, 4, "X") @ Matrix.Rotation(0.3, 4, "Z")):
        shield(bm, rng)


def g_sprawl_spear_upright(bm, rng):
    supine(bm, rng)
    with frame(bm, Matrix.Translation((0.45, -0.35, -0.55)) @ Matrix.Rotation(-0.15, 4, "Z") @ Matrix.Rotation(0.1, 4, "X")):
        spear(bm, rng)
    with frame(bm, Matrix.Translation((-0.5, 0.06, -0.25)) @ Matrix.Rotation(-1.6, 4, "Z")):
        helmet(bm, rng, dents=3, guard=False)


def beast_skull(bm, rng):
    """A giant horned skull round its centre: face +z, top +y (about 1.3 m long, horns 1.6 m across)."""
    ellipsoid(bm, (0, 0.05, 0), (0.3, 0.27, 0.38), BONE, 12, 8)
    tube(bm, [(0, 0.0, 0.18), (0, -0.04, 0.45), (0, -0.08, 0.7), (0, -0.1, 0.88)], [0.24, 0.2, 0.15, 0.1], BONE, 8,
         flat=0.85, up=(0, 1, 0))
    for s in (-1, 1):
        ellipsoid(bm, (s * 0.17, 0.2, 0.25), (0.12, 0.06, 0.12), BONE, 6, 4)  # brow ridge
        ellipsoid(bm, (s * 0.205, 0.08, 0.25), (0.09, 0.075, 0.05), SHADOW, 8, 5,
                  rot=Matrix.Rotation(s * 0.55, 4, "Y"))  # eye socket
        ellipsoid(bm, (s * 0.045, -0.06, 0.92), (0.03, 0.035, 0.03), SHADOW, 5, 3)  # nostril
        ellipsoid(bm, (s * 0.25, -0.12, 0.05), (0.09, 0.1, 0.16), BONE, 6, 4)  # cheekbone
        base = Vector((s * 0.2, 0.22, -0.08))
        tube(bm, [base + Vector(p) for p in ((0, 0, 0), (s * 0.25, 0.06, -0.2), (s * 0.52, 0.12, -0.25), (s * 0.72, 0.28, -0.1),
                                              (s * 0.82, 0.5, 0.15), (s * 0.78, 0.68, 0.36))],
             [0.11, 0.095, 0.08, 0.06, 0.035, 0.0], BONE, 7)
        small = Vector((s * 0.12, 0.28, 0.12))
        tube(bm, [small, small + Vector((s * 0.06, 0.16, -0.04)), small + Vector((s * 0.05, 0.27, -0.12))],
             [0.045, 0.025, 0.0], BONE, 5)
        for k in range(5):  # upper teeth
            z = 0.42 + k * 0.1
            x = s * (0.15 - k * 0.012)
            tube(bm, [(x, -0.16 + k * 0.01, z), (x * 0.95, -0.27 + k * 0.012, z + 0.02)], [0.028, 0.0], BONE, 4)


def beast_jaw(bm):
    """Its lower jaw round the hinge: a V of two bones meeting at the chin, teeth up."""
    for s in (-1, 1):
        tube(bm, [(s * 0.26, 0.0, 0.0), (s * 0.2, -0.06, 0.35), (s * 0.08, -0.07, 0.78), (0, -0.07, 0.84)],
             [0.07, 0.06, 0.05, 0.05], BONE, 6, flat=0.6, up=(0, 1, 0))
        for k in range(4):
            z = 0.32 + k * 0.12
            x = s * (0.2 - k * 0.03)
            tube(bm, [(x, -0.03, z), (x, 0.08, z + 0.02)], [0.025, 0.0], BONE, 4)


def g_beast(bm, rng):
    """A great horned beast's skeleton (~4.4 m): spine on the ground, ribs arching up, skull and jaw at the front."""
    ctrl = [(0.0, 0.12, 1.0), (0.05, 0.14, 0.2), (0.0, 0.13, -0.7), (-0.1, 0.1, -1.3), (0.15, 0.07, -1.9), (0.55, 0.05, -2.4)]
    pts = resample(ctrl, 22)
    tube(bm, pts, [(0.085 if i % 2 else 0.06) * (1.0 if i < 12 else 1.0 - (i - 11) / 14) for i in range(22)], BONE, 6)
    thorax = [p for p in pts if -0.8 < p.z < 0.95]
    ribcage(bm, thorax[::-1], (0, 1, 0), ribs=6, width=0.8, depth=1.75, rib_r=0.05, sides=5, phi=2.45, droop=0.25,
            missing={(1, 1, "short"), (4, -1, "short"), (5, 1)}, sternum=False)
    pelvis_c = Vector((-0.05, 0.12, -1.15))
    for s in (-1, 1):
        ellipsoid(bm, pelvis_c + Vector((s * 0.28, 0.12, 0)), (0.28, 0.07, 0.22), BONE, 7, 4,
                  rot=Matrix.Rotation(s * 0.6, 4, "Z"))
    bone(bm, (-0.75, 0.06, -1.1), (-1.25, 0.06, -0.2), 0.06, sides=6)
    bone(bm, (0.7, 0.05, -1.5), (1.1, 0.05, -2.35), 0.05, sides=6)
    n0 = set(bm.verts)
    with frame(bm, Matrix.Translation((0.15, 0.0, 1.55)) @ Matrix.Rotation(0.3, 4, "Y") @ Matrix.Rotation(0.2, 4, "Z")):
        beast_skull(bm, rng)
    vs = new_verts(bm, n0)
    low = min(v.co.y for v in vs)
    for v in vs:
        v.co.y -= low + 0.12  # resting sunk on its chin and cheek
    with frame(bm, Matrix.Translation((-0.65, 0.04, 1.8)) @ Matrix.Rotation(-0.6, 4, "Y") @ Matrix.Rotation(0.15, 4, "Z")):
        beast_jaw(bm)


GROUPS = [g_beast, g_slumped, g_sword_through, g_sprawl_broken_sword, g_arm_on_chest_helmet, g_sunk_ribcage,
          g_skull_pile, g_weapons, g_partial, g_grave_sword, g_sprawl_spear, g_sprawl_spear_upright]


# ---------------------------------------------------------------- debris: parts


def sheet(bm, f, nu, nv, thick, mat, keep=None):
    """A thin plate over a grid: f(u, v) -> point for u, v in 0..1 (nu x nv cells), `thick` through, closed round
    its edges. keep(u, v) -> False drops a cell (torn cloth, broken and holed metal)."""
    grid = {(i, j): Vector(f(i / nu, j / nv)) for i in range(nu + 1) for j in range(nv + 1)}

    def normal(i, j):
        du = grid[(min(i + 1, nu), j)] - grid[(max(i - 1, 0), j)]
        dv = grid[(i, min(j + 1, nv))] - grid[(i, max(j - 1, 0))]
        n = du.cross(dv)
        return n.normalized() if n.length > 1e-9 else UP

    cells = {(i, j) for i in range(nu) for j in range(nv) if keep is None or keep((i + 0.5) / nu, (j + 0.5) / nv)}
    used = {c for i, j in cells for c in ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))}
    top, bot = {}, {}
    for c in used:
        n = normal(*c)
        top[c] = bm.verts.new(grid[c] + n * thick / 2)
        bot[c] = bm.verts.new(grid[c] - n * thick / 2)
    faces = []
    for i, j in cells:
        a, b, c, d = (i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)
        faces.append(bm.faces.new([top[k] for k in (a, b, c, d)]))
        faces.append(bm.faces.new([bot[k] for k in (d, c, b, a)]))
        for (p0, p1), nb in (((a, b), (i, j - 1)), ((b, c), (i + 1, j)), ((c, d), (i, j + 1)), ((d, a), (i - 1, j))):
            if nb not in cells:
                faces.append(bm.faces.new((top[p1], top[p0], bot[p0], bot[p1])))
    set_mat(faces, mat)
    return list(top.values()) + list(bot.values())


def dent(verts, centre, radius, depth, toward):
    """Pushes the verts near `centre` in along `toward` (a dent, a hammer blow)."""
    centre, toward = Vector(centre), Vector(toward).normalized()
    for v in verts:
        d = (v.co - centre).length
        if d < radius:
            v.co += toward * depth * (1 - d / radius) ** 2


def split(bm, verts, co, no, rng, jag=0.03):
    """Cuts the piece made of `verts` along a plane and throws away the part in front of it; the cut edge jagged."""
    co, no = Vector(co), Vector(no).normalized()
    vs = set(verts)
    geom = list(vs) + list({e for v in vs for e in v.link_edges}) + list({f for v in vs for f in v.link_faces})
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_outer=True, dist=1e-5)
    for v in res["geom_cut"]:
        if isinstance(v, bmesh.types.BMVert):
            v.co -= no * rng.uniform(0.0, jag)


def lay_flat(bm, verts, sink):
    """Drops the piece so its lowest point is `sink` below the ground (y = 0)."""
    verts = [v for v in verts if v.is_valid]
    low = min(v.co.y for v in verts)
    for v in verts:
        v.co.y -= low + sink


def cracked_skull(bm, rng, pos, top, face, sink):
    """A skull with a hole knocked in its crown and a crack running from it."""
    n0 = set(bm.verts)
    with frame(bm, look_frame(top, face, pos)):
        human_skull(bm, rng.uniform(0, 0.35), rng.random() < 0.6)
        s = rng.choice((-1, 1))
        ellipsoid(bm, (s * 0.04, 0.082, -0.03), (0.032, 0.022, 0.03), SHADOW, 6, 4)  # the hole
        tube(bm, [(s * 0.02, 0.098, -0.02), (0, 0.1, 0.02), (-s * 0.015, 0.088, 0.06)], 0.004, SHADOW, 3)  # the crack
    lay_flat(bm, new_verts(bm, n0), sink)


def small_beast_skull(bm, rng, scale):
    """A horned beast's skull (the giant one, smaller), its jaw near it."""
    n0 = set(bm.verts)
    with frame(bm, Matrix.Rotation(rng.uniform(-0.4, 0.4), 4, "Z") @ Matrix.Diagonal((scale, scale, scale, 1.0))):
        beast_skull(bm, rng)
    lay_flat(bm, new_verts(bm, n0), 0.05 * scale)
    n1 = set(bm.verts)
    with frame(bm, Matrix.Translation((0.5 * scale, 0, 0.7 * scale)) @ Matrix.Rotation(rng.uniform(-1, 1), 4, "Y")
               @ Matrix.Diagonal((scale, scale, scale, 1.0))):
        beast_jaw(bm)
    lay_flat(bm, new_verts(bm, n1), 0.02 * scale)


def breastplate(bm, rng, keep=None, mat=STEEL):
    """A breastplate round the middle of its bottom edge: height up +y, front +z (a curved shell, neck and arm
    holes cut out, a ridge down the front, dented)."""
    def f(u, v):
        a = (u - 0.5) * 2.5
        r = 0.16 + 0.035 * math.sin(math.pi * min(v * 1.3, 1.0)) + 0.012 * max(0.0, 1 - abs(a) / 0.35)
        return (math.sin(a) * r * 1.2, v * 0.44, math.cos(a) * r * 0.75 - 0.08)

    def holes(u, v):
        a = abs(u - 0.5) * 2.5
        if v > 0.8 and a < 0.55 + (v - 0.8):
            return False  # the neck
        if v > 0.6 and a > 0.95:
            return False  # the arms
        return keep is None or keep(u, v)

    vs = sheet(bm, f, 9, 7, 0.012, mat, holes)
    for _ in range(rng.randint(1, 3)):
        a = rng.uniform(-0.9, 0.9)
        dent(vs, (math.sin(a) * 0.2, rng.uniform(0.1, 0.35), math.cos(a) * 0.13 - 0.08), rng.uniform(0.07, 0.12),
             0.035, (-math.sin(a), 0, -math.cos(a)))
    return vs


def pauldron(bm, rng):
    """A shoulder guard round its top: two overlapping lames, the dome up +y, the outside towards +x."""
    vs = []
    for k, (r, drop) in enumerate(((0.15, 0.0), (0.135, 0.055))):
        def f(u, v, r=r, drop=drop):
            th = (u - 0.5) * 2.6
            ph = 0.2 + v * 1.15
            return (math.sin(ph) * math.cos(th) * r, math.cos(ph) * r * 0.8 - drop, math.sin(ph) * math.sin(th) * r * 1.1)
        vs += sheet(bm, f, 7, 4, 0.01, STEEL if k == 0 else RUST)
    dent(vs, (0.1, 0.08, 0.03), 0.07, 0.03, (-1, -0.3, 0))
    return vs


def gauntlet(bm, rng):
    """An iron gauntlet lying palm down: the cuff at -z, the fingers curled forward towards +z."""
    tube(bm, [(0, 0.04, -0.2), (0, 0.035, -0.1), (0, 0.035, -0.04)], [0.055, 0.045, 0.042], STEEL, 7)
    ellipsoid(bm, (0, 0.035, 0.02), (0.05, 0.022, 0.06), STEEL, 6, 4)  # the back of the hand
    curl = rng.uniform(0.3, 1.2)
    for k, x in enumerate((-0.033, -0.011, 0.011, 0.033)):
        a = Vector((x, 0.03, 0.07))
        b = a + Vector((x * 0.1, -0.005, 0.045))
        c = b + Vector((0, -0.02 * curl, 0.035 * (1.2 - curl * 0.5)))
        tube(bm, [a, b, c], [0.012, 0.011, 0.009], RUST, 4)
    tube(bm, [(0.048, 0.025, 0.0), (0.075, 0.02, 0.04), (0.085, 0.015, 0.07)], [0.013, 0.011, 0.009], RUST, 4)  # thumb


def round_shield(bm, rng, face=PAINT, emblem=True, gouges=0):
    """A round shield (r 0.42) round its centre, face +y: painted boards, an iron boss and rim, an emblem ring and
    spokes, maybe claw gouges raked across it."""
    n0 = set(bm.verts)
    lathe(bm, [(0, 0.075), (0.05, 0.07), (0.085, 0.045), (0.1, 0.026), (0.2, 0.024), (0.4, 0.02), (0.425, 0.012),
               (0.425, -0.014), (0.4, -0.02), (0.0, -0.02)], 14,
          [STEEL, STEEL, STEEL, face, face, RUST, RUST, RUST, WOOD])
    if emblem:
        ring = [(math.cos(a) * 0.28, 0.026, math.sin(a) * 0.28) for a in (k / 12 * math.tau for k in range(13))]
        tube(bm, ring, 0.018, EMBLEM, 4, flat=0.35, up=(0, 1, 0), caps=False)
        for k in range(4):
            a = k / 4 * math.tau + 0.4
            tube(bm, [(math.cos(a) * 0.11, 0.028, math.sin(a) * 0.11), (math.cos(a) * 0.27, 0.025, math.sin(a) * 0.27)],
                 0.015, EMBLEM, 4, flat=0.35, up=(0, 1, 0))
    a0 = rng.uniform(0, math.tau)
    for k in range(gouges):  # three parallel rakes of a claw
        off = (k - (gouges - 1) / 2) * 0.07
        d = Vector((math.cos(a0), 0, math.sin(a0)))
        side = Vector((-d.z, 0, d.x))
        p0 = side * off - d * 0.3 + Vector((0, 0.031, 0))
        tube(bm, [p0, p0 + d * 0.3 + Vector((0, 0.002, 0)), p0 + d * 0.55], [0.004, 0.013, 0.004], SHADOW, 4, flat=0.25,
             up=(0, 1, 0))
    return new_verts(bm, n0)


def kite_outline(v):
    """Half width of a kite shield at v (0 the top edge .. 1 the point)."""
    if v < 0.18:
        return 0.27 * math.sqrt(max(0.0, 1 - ((0.18 - v) / 0.2) ** 2))
    return 0.27 * (1 - ((v - 0.18) / 0.82) ** 1.5)


def kite_shield(bm, rng, keep=None, gouges=0):
    """A kite shield round its middle, face +y, the point towards -z (0.95 m long): curved painted boards, an iron
    rim, a cross on its face."""
    L = 0.95
    curve = lambda x: -0.07 * (x / 0.27) ** 2

    def f(u, v):
        x = (u - 0.5) * 2 * max(kite_outline(v), 0.004)
        return (x, curve(x), L / 2 - v * L)

    n0 = set(bm.verts)
    sheet(bm, f, 6, 9, 0.025, PAINT, keep)
    if keep is None:
        for s in (-1, 1):  # the rim, down each side to the point
            pts = [(s * kite_outline(v), curve(kite_outline(v)) + 0.004, L / 2 - v * L) for v in (0, 0.1, 0.2, 0.4, 0.6, 0.8, 1.0)]
            tube(bm, pts, 0.013, RUST, 4)
        tube(bm, [(-0.25, 0.006, L / 2 - 0.01), (0.25, 0.006, L / 2 - 0.01)], 0.013, RUST, 4)
        tube(bm, [(0, 0.018, 0.4), (0, 0.016, -0.32)], 0.035, EMBLEM, 4, flat=0.3, up=(0, 1, 0))
        tube(bm, [(-0.2, 0.0, 0.18), (0, 0.018, 0.18), (0.2, 0.0, 0.18)], 0.035, EMBLEM, 4, flat=0.3, up=(0, 1, 0))
    for k in range(gouges):
        x = (k - (gouges - 1) / 2) * 0.07
        tube(bm, [(x - 0.1, curve(x) + 0.022, 0.3), (x, curve(x) + 0.026, 0.05), (x + 0.08, curve(x) + 0.022, -0.2)],
             [0.004, 0.012, 0.004], SHADOW, 4, flat=0.25, up=(0, 1, 0))
    return new_verts(bm, n0)


def arrow(bm, rng, stuck=True, length=0.72):
    """An arrow from its head (the origin) back along +y: a thin shaft, an iron head, two crossed fletchings."""
    snap = rng.uniform(0.35, 0.7) if rng.random() < 0.25 else 1.0
    tube(bm, [(0, 0.0, 0), (0, length * snap, 0)], 0.0055, WOOD, 3)
    tube(bm, [(0, -0.07, 0), (0, -0.02, 0), (0, 0.01, 0)], [0.0, 0.014, 0.006], RUST, 3)
    if snap >= 1.0:
        for up in ((1, 0, 0), (0, 0, 1)):
            tube(bm, [(0, length - 0.13, 0), (0, length - 0.05, 0), (0, length - 0.01, 0)], [0.002, 0.016, 0.01], FEATHER,
                 4, flat=0.08, up=up)


def banner(bm, rng, fallen=False):
    """A war banner: a pole driven into the ground (from 0.35 under it) leaning a little, a crossbar and a torn
    cloth hanging from it, stirring. Fallen: the pole snapped and the cloth draped over the ground."""
    seed = rng.uniform(0, 100)
    if fallen:
        tube(bm, [(0, 0.035, -1.1), (0.04, 0.035, 0.2), (0, 0.035, 0.75)], 0.03, WOOD, 5)
        tube(bm, [(-0.4, 0.07, 0.62), (0.4, 0.07, 0.62)], 0.02, WOOD, 4)

        def f(u, v):
            x = (u - 0.5) * 0.7
            z = 0.6 - v * 1.05
            return (x + 0.08 * math.sin(v * 4 + seed), 0.025 + 0.05 * max(0.0, math.sin(u * 5 + v * 7 + seed)), z)
    else:
        h = rng.uniform(2.2, 2.7)
        lean = Vector((rng.uniform(-0.25, 0.25), 1, rng.uniform(-0.2, 0.2))).normalized()
        base = Vector((0, -0.35, 0))
        top = base + lean * (h + 0.35)
        tube(bm, [base, base.lerp(top, 0.5) + Vector((0.02, 0, 0)), top], [0.033, 0.03, 0.026], WOOD, 5)
        tube(bm, [top - lean * 0.02, top + lean * 0.12], [0.03, 0.0], RUST, 4)  # the finial
        bar = top - lean * 0.18
        side = Vector((1, 0, 0))
        tube(bm, [bar - side * 0.38, bar + side * 0.38], 0.018, WOOD, 4)

        def f(u, v):
            x = (u - 0.5) * 0.7
            sway = 0.06 * math.sin(u * 6 + v * 2.5 + seed) * (0.3 + v)
            return tuple(bar + Vector((x, -v * 1.15, sway + 0.03 * v)))

    def torn(u, v):
        edge = 0.62 + 0.3 * abs(math.sin(u * 9.0 + seed)) + 0.08 * noise.noise(Vector((u * 6, seed, 0)))
        hole = noise.noise(Vector((u * 5 + seed, v * 5, 1.3))) > 0.45
        return v < edge and not hole

    sheet(bm, f, 7, 8, 0.008, CLOTH, torn)


def mail_heap(bm, rng, size=0.45):
    """A heap of rusted chain mail: a slumped, lumpy pile with a fold or two."""
    seed = Vector((rng.uniform(0, 50), rng.uniform(0, 50), rng.uniform(0, 50)))
    for k in range(2):
        c = Vector((k * size * 0.6 - size * 0.2, 0.0, k * size * 0.35))
        r = size * (1.0 if k == 0 else 0.6)
        vs = ellipsoid(bm, c, (r, r * 0.32, r * 0.8), MAIL, 10, 6)
        for v in vs:
            v.co += (v.co - c).normalized() * r * 0.18 * noise.noise((v.co - c) * 6 + seed)
            v.co.y = max(v.co.y, -0.04)


def stone(bm, rng, size, at):
    n0 = set(bm.verts)
    craggy(bm, rng, size, 2, ROCK, chops=4)
    vs = new_verts(bm, n0)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(at) + Vector((0, size[1] * 0.4, 0))), verts=vs)


# ---------------------------------------------------------------- debris: pieces (around (0, 0, 0) on the ground)


def d_skull(bm, rng):
    skull_on_ground(bm, Vector((0, 0, 0)), (rng.uniform(-0.6, 0.6), 1, rng.uniform(-0.3, 0.6)),
                    (rng.uniform(-1, 1), rng.uniform(-0.2, 0.4), 1), sink=rng.uniform(0.01, 0.03),
                    jaw_open=rng.uniform(0, 0.4), jaw=rng.random() < 0.5)


def d_buried_skull(bm, rng):
    skull_on_ground(bm, Vector((0, 0, 0)), (rng.uniform(-0.3, 0.3), 1, rng.uniform(-0.2, 0.2)), (rng.uniform(-1, 1), 0.1, 1),
                    sink=rng.uniform(0.07, 0.1), jaw=False)  # half sunk in the ash
    bone(bm, (0.15, 0.0, -0.1), (0.4, 0.0, -0.25), 0.012, sides=4)


def d_cracked_skull(bm, rng):
    cracked_skull(bm, rng, Vector((0, 0, 0)), (rng.uniform(-0.5, 0.5), 1, 0.2), (rng.uniform(-1, 1), 0.2, 1), 0.02)
    if rng.random() < 0.6:
        bone(bm, (-0.3, 0.015, 0.1), (-0.15, 0.015, 0.45), 0.017)


def d_skull_cairn(bm, rng):
    """Skulls piled into a cairn, a sword laid at its foot."""
    k = 0
    for layer, (n, r, y) in enumerate(((4, 0.15, 0.0), (2, 0.07, 0.12), (1, 0.0, 0.24))):
        for i in range(n):
            a = i / n * math.tau + layer * 0.5 + rng.uniform(-0.2, 0.2)
            c = Vector((math.cos(a) * r, y, math.sin(a) * r))
            face = (math.cos(a) + rng.uniform(-0.3, 0.3), rng.uniform(-0.1, 0.3), math.sin(a) + rng.uniform(-0.3, 0.3))
            with frame(bm, look_frame((rng.uniform(-0.2, 0.2), 1, rng.uniform(-0.2, 0.2)), face, c)):
                human_skull(bm, 0.0, False)
            k += 1
    n0 = set(bm.verts)
    lay_flat(bm, list(bm.verts), 0.03)
    with frame(bm, Matrix.Translation((0.05, 0.03, 0.42)) @ Matrix.Rotation(1.45, 4, "Z") @ Matrix.Rotation(0.3, 4, "X")):
        sword(bm, rng, broken=rng.random() < 0.5)


def d_beast_skull(bm, rng):
    small_beast_skull(bm, rng, rng.uniform(0.35, 0.5))


def d_breastplate(bm, rng):
    """A breastplate split in two by a blow, the halves lying apart."""
    seed = rng.uniform(0, 10)
    cut = lambda u, v: u + 0.12 * math.sin(v * 9 + seed) < 0.5
    for k, (keep, M) in enumerate(((cut, Matrix.Translation((-0.08, 0, 0)) @ Matrix.Rotation(-math.pi / 2, 4, "X")),
                                   (lambda u, v: not cut(u, v),
                                    Matrix.Translation((0.25, 0, 0.2)) @ Matrix.Rotation(0.7, 4, "Y") @ Matrix.Rotation(math.pi / 2 + 0.3, 4, "X")))):
        n0 = set(bm.verts)
        with frame(bm, M):
            breastplate(bm, rng, keep, mat=STEEL if k == 0 else RUST)
        lay_flat(bm, new_verts(bm, n0), 0.015)


def d_breastplate_whole(bm, rng):
    """A dented breastplate on its back, a pauldron beside it."""
    n0 = set(bm.verts)
    with frame(bm, Matrix.Rotation(rng.uniform(-0.5, 0.5), 4, "Y") @ Matrix.Rotation(-math.pi / 2, 4, "X")):
        breastplate(bm, rng)
    lay_flat(bm, new_verts(bm, n0), 0.02)
    n1 = set(bm.verts)
    with frame(bm, Matrix.Translation((0.4, 0, -0.1)) @ Matrix.Rotation(rng.uniform(-1, 1), 4, "Y")):
        pauldron(bm, rng)
    lay_flat(bm, new_verts(bm, n1), 0.02)


def d_pauldron(bm, rng):
    n0 = set(bm.verts)
    with frame(bm, Matrix.Rotation(rng.uniform(0, math.tau), 4, "Y") @ Matrix.Rotation(rng.uniform(-0.6, 0.6), 4, "Z")):
        pauldron(bm, rng)
    lay_flat(bm, new_verts(bm, n0), 0.02)


def d_gauntlet(bm, rng):
    gauntlet(bm, rng)
    if rng.random() < 0.5:
        bone(bm, (0.12, 0.012, 0.08), (0.3, 0.012, -0.12), 0.011, sides=4)


def d_split_helmet(bm, rng):
    """A helmet cloven in two, the halves rolled apart."""
    for k, (sign, M) in enumerate(((1, Matrix.Translation((-0.1, 0, 0)) @ Matrix.Rotation(-0.6, 4, "Z")),
                                   (-1, Matrix.Translation((0.18, 0, 0.12)) @ Matrix.Rotation(0.9, 4, "Z") @ Matrix.Rotation(2.0, 4, "Y")))):
        n0 = set(bm.verts)
        with frame(bm, M):
            helmet(bm, rng, dents=1, guard=k == 0)
        vs = new_verts(bm, n0)
        split(bm, vs, M.translation, M.to_3x3() @ Vector((sign, 0, 0)), rng)
        lay_flat(bm, [v for v in bm.verts if v not in n0], 0.01)


def d_helmet_sword(bm, rng):
    """A helmet with a sword driven down through it into the ground."""
    n0 = set(bm.verts)
    with frame(bm, Matrix.Rotation(rng.uniform(-0.3, 0.3), 4, "Z")):
        helmet(bm, rng, dents=2)
    lay_flat(bm, new_verts(bm, n0), 0.03)
    with frame(bm, Matrix.Translation((0.02, 0.72, 0.0)) @ Matrix.Rotation(rng.uniform(-0.15, 0.15), 4, "X") @ Matrix.Rotation(0.12, 4, "Z")):
        sword(bm, rng, blade=0.95)


def d_round_shield(bm, rng):
    """A painted round shield lying face up or down, maybe raked by claws."""
    up = rng.random() < 0.75
    n0 = set(bm.verts)
    with frame(bm, Matrix.Rotation(rng.uniform(-0.15, 0.15), 4, "X") @ (I4 if up else Matrix.Rotation(math.pi, 4, "Z"))):
        round_shield(bm, rng, PAINT if rng.random() < 0.6 else WOOD, emblem=rng.random() < 0.7, gouges=rng.choice((0, 3, 3)))
    lay_flat(bm, new_verts(bm, n0), 0.01)


def d_split_shield(bm, rng):
    """A round shield split down its boards, the halves apart, an arrow through one."""
    for k, (sign, M) in enumerate(((1, Matrix.Translation((-0.12, 0, 0))),
                                   (-1, Matrix.Translation((0.25, 0, 0.15)) @ Matrix.Rotation(0.5, 4, "Y") @ Matrix.Rotation(0.12, 4, "Z")))):
        n0 = set(bm.verts)
        with frame(bm, M):
            vs = round_shield(bm, rng, PAINT if k == 0 else WOOD, emblem=True)
        vs = [v for v in bm.verts if v not in n0]
        split(bm, vs, M.translation + M.to_3x3() @ Vector((sign * 0.05, 0, 0)), M.to_3x3() @ Vector((sign, 0, 0.2)), rng, 0.05)
        lay_flat(bm, [v for v in bm.verts if v not in n0], 0.01)
    with frame(bm, Matrix.Translation((-0.25, -0.06, 0.1)) @ Matrix.Rotation(0.5, 4, "Z")):
        arrow(bm, rng)


def d_shield_upright(bm, rng):
    """A kite shield driven point first into the ground, standing, clawed."""
    with frame(bm, Matrix.Translation((0, 0.3, 0)) @ Matrix.Rotation(rng.uniform(-0.15, 0.15), 4, "Z")
               @ Matrix.Rotation(-math.pi / 2 + rng.uniform(-0.15, 0.1), 4, "X")):
        kite_shield(bm, rng, gouges=rng.choice((0, 3)))


def d_shield_leaning(bm, rng):
    """A shield leaning against a rock, a skull at its foot."""
    stone(bm, rng, (0.9, 0.7, 0.7), (0, 0, -0.45))
    if rng.random() < 0.5:
        with frame(bm, Matrix.Translation((0, 0.42, 0.05)) @ Matrix.Rotation(1.1, 4, "X") @ Matrix.Rotation(math.pi, 4, "Y")):
            kite_shield(bm, rng, gouges=rng.choice((0, 3)))  # (face out, its top resting on the rock, point down)
    else:
        with frame(bm, Matrix.Translation((0, 0.42, 0.03)) @ Matrix.Rotation(1.15, 4, "X")):
            round_shield(bm, rng, PAINT, emblem=True, gouges=rng.choice((0, 3)))
    skull_on_ground(bm, Vector((0.45, 0, 0.35)), (0.2, 1, 0.1), (0.6, 0.1, 1), jaw=False)


def d_kite_lying(bm, rng):
    """A kite shield lying in two pieces."""
    seed = rng.uniform(0, 10)
    cut = lambda u, v: v + 0.1 * math.sin(u * 8 + seed) < 0.55
    for keep, M in ((cut, Matrix.Rotation(rng.uniform(-0.3, 0.3), 4, "Y")),
                    (lambda u, v: not cut(u, v), Matrix.Translation((0.15, 0, -0.25)) @ Matrix.Rotation(0.6, 4, "Y"))):
        n0 = set(bm.verts)
        with frame(bm, M):
            kite_shield(bm, rng, keep)
        lay_flat(bm, new_verts(bm, n0), 0.01)


def d_stuck_blades(bm, rng):
    """Broken swords and a spear stuck in the ground at angles."""
    for i in range(rng.randint(2, 3)):
        a = rng.uniform(0, math.tau)
        at = Vector((math.cos(a) * 0.35 * i, 0.0, math.sin(a) * 0.35 * i))
        with frame(bm, Matrix.Translation(at + Vector((0, rng.uniform(0.2, 0.4), 0))) @ Matrix.Rotation(rng.uniform(-0.45, 0.45), 4, "Z")
                   @ Matrix.Rotation(rng.uniform(-0.45, 0.45), 4, "X")):
            sword(bm, rng, broken=rng.random() < 0.7)
    with frame(bm, Matrix.Translation((-0.3, -0.3, 0.3)) @ Matrix.Rotation(rng.uniform(-0.5, 0.5), 4, "Z") @ Matrix.Rotation(rng.uniform(-0.4, 0.4), 4, "X")):
        spear(bm, rng, broken=rng.random() < 0.6)


def d_broken_sword(bm, rng):
    """A sword snapped in two, lying, the point a little way off."""
    with frame(bm, Matrix.Translation((0, 0.025, 0)) @ Matrix.Rotation(-math.pi / 2, 4, "X")):
        sword(bm, rng, broken=True)
    tube(bm, [(0.1, 0.012, -0.55), (0.15, 0.012, -0.85), (0.17, 0.012, -0.95)], [0.022, 0.018, 0.0], RUST, 4, flat=0.22,
         up=(0, 1, 0))


def d_arrows(bm, rng):
    """A scatter of arrows: most stuck in the ground at a slant, one or two lying."""
    lean = Vector((rng.uniform(-0.5, 0.5), 1, rng.uniform(-0.6, -0.2)))
    for i in range(rng.randint(4, 7)):
        at = Vector((rng.uniform(-0.45, 0.45), 0, rng.uniform(-0.45, 0.45)))
        if rng.random() < 0.75:
            d = (lean + Vector((rng.uniform(-0.2, 0.2), 0, rng.uniform(-0.2, 0.2)))).normalized()
            M = look_frame(d, d.orthogonal(), at - d * rng.uniform(0.08, 0.2))
        else:
            d = Vector((rng.uniform(-1, 1), 0.0, rng.uniform(-1, 1))).normalized()
            M = look_frame(d, (0, 1, 0), at + Vector((0, 0.006, 0)))
        with frame(bm, M):
            arrow(bm, rng)


def d_banner(bm, rng):
    banner(bm, rng)
    if rng.random() < 0.5:
        skull_on_ground(bm, Vector((0.35, 0, 0.3)), (0, 1, 0.2), (0.4, 0.1, 1), jaw=False)


def d_fallen_banner(bm, rng):
    banner(bm, rng, fallen=True)


def d_mail(bm, rng):
    """A heap of mail with a helmet on it and an arm bone sticking out."""
    mail_heap(bm, rng, rng.uniform(0.35, 0.5))
    with frame(bm, Matrix.Translation((0.05, 0.1, 0.0)) @ Matrix.Rotation(rng.uniform(-0.6, 0.6), 4, "Z")):
        helmet(bm, rng, dents=2, guard=rng.random() < 0.5)
    bone(bm, (-0.3, 0.05, 0.2), (-0.65, 0.02, 0.35), 0.016)


# (builder, big: kept out of the middle of the ledge where the fight is)
DEBRIS = ([(d_skull, False)] * 6 + [(d_buried_skull, False)] * 4 + [(d_cracked_skull, False)] * 4 +
          [(d_gauntlet, False)] * 3 + [(d_pauldron, False)] * 2 + [(d_broken_sword, False)] * 3 + [(d_arrows, False)] * 5 +
          [(d_skull_cairn, True)] + [(d_beast_skull, True)] * 2 + [(d_breastplate, True)] * 2 +
          [(d_breastplate_whole, True)] * 2 + [(d_split_helmet, False)] * 2 + [(d_helmet_sword, True)] * 2 +
          [(d_round_shield, True)] * 3 + [(d_split_shield, True)] * 2 + [(d_shield_upright, True)] * 3 +
          [(d_shield_leaning, True)] * 2 + [(d_kite_lying, True)] * 2 + [(d_stuck_blades, True)] * 3 +
          [(d_banner, True)] * 3 + [(d_fallen_banner, True)] + [(d_mail, True)] * 2)


def place_debris(rng, fp, big, placed, rocks, centre_count):
    """Somewhere on the ledge for a piece of footprint radius fp: off the cave path, out of the lava, clear of the
    boulders and the other pieces; big ones out of the middle of the ledge."""
    for attempt in range(20000):
        ang = rng.uniform(-math.pi, math.pi)
        r = rng.uniform(2.0 + fp, 18.5 - fp)
        x, z = r * math.sin(ang), r * math.cos(ang)
        mid = math.hypot(x, z - 6.0) < 5.5 + fp
        if mid and (big or centre_count[0] >= 12):
            continue
        ring = [(x + math.cos(a) * fp, z + math.sin(a) * fp) for a in (k / 10 * math.tau for k in range(10))]
        pts = [(x, z)] + ring
        if any((pz < -1.0 and abs(px) < 6.5) or (pz < 5.0 and abs(px) < 1.6) for px, pz in pts):
            continue  # the way out of the cave
        if any(pool_gap(px, pz) < 0.6 for px, pz in pts):
            continue
        if any(math.hypot(x - rx, z - rz) < fp + rr + 2.5 for rx, rz, rr in rocks):
            continue
        if any(math.hypot(x - qx, z - qz) < fp + qf + 0.35 for qx, qz, qf in placed):
            continue
        hs = [ground_height(px, pz) for px, pz in pts]
        if max(hs) - min(hs) > 0.45:
            continue
        if any(blocked(px, pz) for px, pz in pts):
            continue
        if mid:
            centre_count[0] += 1
        return x, z
    raise RuntimeError(f"no room for a debris piece (footprint {fp:.2f})")


# ---------------------------------------------------------------- spawn rocks, cracks and chunks


def spawn_rock(rng, index):
    """A craggy boulder on its base centre (sunk 0.3 m below it), its BVH and its height."""
    bm = bmesh.new()
    w = rng.uniform(2.1, 2.7)
    h = rng.uniform(2.5, 3.1)
    craggy(bm, rng, (w, h + 0.4, w * rng.uniform(0.8, 0.95)), 4, ROCK, chops=13, octaves=(0.26, 0.1))
    lean = Vector((rng.uniform(-0.15, 0.15), 0, rng.uniform(-0.15, 0.15)))
    lo = min(v.co.y for v in bm.verts)
    for v in bm.verts:
        v.co.y -= lo + 0.3
        t = max(0.0, v.co.y) / h
        v.co.x *= 1.0 - 0.28 * t
        v.co.z *= 1.0 - 0.28 * t
        v.co += lean * t * t
        v.co.y = max(v.co.y, -0.3)
    top = max(v.co.y for v in bm.verts)
    if not 2.45 <= top <= 3.15:  # keep it 2.45-3.15 m tall above the ground
        k = min(3.15, max(2.45, top)) / top
        for v in bm.verts:
            v.co.y = max(-0.3, v.co.y * k)
        top = max(v.co.y for v in bm.verts)
    width = max(max(abs(v.co.x), abs(v.co.z)) for v in bm.verts) * 2
    return bm, top, width


def crack_ribbons(rock_bm, height, rng):
    """Thin glowing crack ribbons lying just on the rock's surface."""
    bvh = BVHTree.FromBMesh(rock_bm)
    centre = Vector((0, height * 0.42, 0))
    out = bmesh.new()
    lay = open_layer(out)

    def project(p):
        d = p - centre
        if d.length < 1e-4:
            return None
        hit = bvh.ray_cast(centre, d.normalized(), 10.0)
        if hit[0] is None:
            return None
        n = hit[1] if hit[1].dot(d) > 0 else -hit[1]
        return hit[0], n

    def walk(p, heading, steps, width):
        pts = []
        for _ in range(steps):
            hit = project(p)
            if hit is None:
                break
            q, n = hit
            if q.y < 0.02 or q.y > height * 0.97:
                break
            pts.append((q, n))
            heading = heading - n * heading.dot(n)
            heading = (heading.normalized() + Vector((rng.uniform(-0.45, 0.45), rng.uniform(-0.25, 0.35),
                                                      rng.uniform(-0.45, 0.45)))).normalized()
            p = q + heading * 0.13
        if len(pts) >= 3:
            ribbon(pts, width)
        return pts

    def ribbon(pts, width):
        rows = []
        for i, (q, n) in enumerate(pts):
            a = pts[max(0, i - 1)][0]
            b = pts[min(len(pts) - 1, i + 1)][0]
            t = (b - a).normalized()
            side = n.cross(t).normalized()
            w = width * (0.3 + 0.7 * math.sin(math.pi * (i + 0.5) / len(pts)))
            lift = n * 0.022
            rows.append((out.verts.new(q - side * w / 2 + lift), out.verts.new(q + side * w / 2 + lift), n))
        for (l0, r0, n0), (l1, r1, n1) in zip(rows, rows[1:]):
            f = out.faces.new((l0, r0, r1, l1))
            f.normal_update()
            if f.normal.dot(n0 + n1) < 0:
                f.normal_flip()
            f.material_index = ROCKCRACK
            f[lay] = 1

    for c in range(7):
        a = c / 7 * math.tau + rng.uniform(-0.3, 0.3)
        y = rng.uniform(0.1, 0.45) * height
        start = Vector((math.cos(a) * 3, y, math.sin(a) * 3))
        heading = Vector((rng.uniform(-0.4, 0.4), 1.0, rng.uniform(-0.4, 0.4)))
        pts = walk(start, heading, rng.randint(10, 17), rng.uniform(0.045, 0.075))
        if len(pts) > 6:
            q, n = pts[len(pts) // 2]
            br = n.cross(UP).normalized() * rng.choice((-1, 1)) + UP * 0.4
            walk(q + br * 0.1, br, rng.randint(4, 7), rng.uniform(0.03, 0.045))
    return out


def chunk(rng, size):
    bm = bmesh.new()
    s = (size * rng.uniform(0.75, 1.0), size * rng.uniform(0.5, 0.8), size * rng.uniform(0.6, 0.95))
    craggy(bm, rng, s, 2, ROCK, chops=3, octaves=(0.25, 0.0))
    c = sum((v.co for v in bm.verts), Vector()) / len(bm.verts)
    for v in bm.verts:
        v.co -= c
    return bm


# ---------------------------------------------------------------- flyers


def bird_body():
    bm = bmesh.new()
    pts = [(0, 0.0, -0.15), (0, 0.0, -0.08), (0, 0.005, 0.0), (0, 0.015, 0.07), (0, 0.03, 0.12), (0, 0.05, 0.16),
           (0, 0.06, 0.2), (0, 0.062, 0.235), (0, 0.058, 0.255)]
    tube(bm, pts, [0.035, 0.06, 0.072, 0.068, 0.048, 0.04, 0.045, 0.035, 0.018], FEATHER, 7)
    tube(bm, [(0, 0.062, 0.25), (0, 0.058, 0.29), (0, 0.04, 0.325)], [0.019, 0.012, 0.0], BEAK, 4, flat=0.7,
         up=(0, 1, 0))
    slab(bm, [(-0.03, -0.12), (-0.09, -0.33), (-0.03, -0.355), (0, -0.335), (0.03, -0.355), (0.09, -0.33), (0.03, -0.12)],
         0.005, 0.014, FEATHER, bend=lambda x, z: 0.04 * (z + 0.12))
    for s in (-1, 1):
        tube(bm, [(s * 0.03, -0.045, -0.03), (s * 0.032, -0.06, -0.13)], 0.008, BEAK, 3)
    return bm


def bird_wing(side):
    """side +1: right wing along +x; -1: left wing along -x. Pivot at the shoulder."""
    out = [(0.0, 0.05), (0.12, 0.075), (0.26, 0.08), (0.38, 0.06), (0.47, 0.04)]
    for k in range(5):  # primary feathers spread like fingers
        zc = 0.025 - k * 0.04
        tip = (0.63 - k * 0.03, zc - 0.012 - k * 0.012)
        out += [(0.49 - k * 0.012, zc + 0.016), tip, (0.49 - k * 0.012, zc - 0.016)]
        if k < 4:
            out.append((0.46 - k * 0.012, zc - 0.02))
    out += [(0.4, -0.17), (0.3, -0.2), (0.18, -0.19), (0.08, -0.15), (0.0, -0.1)]
    bm = bmesh.new()
    slab(bm, [(side * x, z) for x, z in out], 0.0, 0.016, FEATHER, bend=lambda x, z: 0.06 * (abs(x) / 0.63) ** 1.5)
    return bm


WYV_SPINE = [(0.0, -0.02, -3.3, 0.02), (0.0, 0.0, -2.9, 0.04), (0.0, 0.02, -2.4, 0.07), (0.0, 0.03, -1.9, 0.1),
             (0.0, 0.03, -1.4, 0.15), (0.0, 0.02, -0.95, 0.22), (0.0, 0.0, -0.5, 0.3), (0.0, 0.0, -0.1, 0.35),
             (0.0, 0.02, 0.3, 0.35), (0.0, 0.06, 0.7, 0.29), (0.0, 0.14, 1.05, 0.2), (0.0, 0.26, 1.4, 0.15),
             (0.0, 0.4, 1.75, 0.13), (0.0, 0.5, 2.05, 0.12), (0.0, 0.57, 2.28, 0.13)]


def wyvern_body():
    bm = bmesh.new()
    pts = []
    for x, y, z, r in WYV_SPINE:
        if z < -1.0:
            x = 0.16 * math.sin((z + 1.0) * 1.3)
        pts.append(Vector((x, y, z)))
    tube(bm, pts, [p[3] for p in WYV_SPINE], WSKIN, 9, flat=1.12, up=(0, 1, 0))
    tail = pts[0]
    d = (pts[0] - pts[1]).normalized()
    sd = Vector((d.z, 0, -d.x))
    spade = [tail - d * 0.05, tail + sd * 0.16 + d * 0.12, tail + d * 0.38, tail - sd * 0.16 + d * 0.12]
    slab(bm, [(p.x, p.z) for p in spade], tail.y, 0.03, WSKIN)
    for i in range(2, len(pts) - 2):  # dorsal spikes
        p = pts[i]
        r = WYV_SPINE[i][3]
        tube(bm, [p + Vector((0, r * 1.05 * 0.9, 0)), p + Vector((0, r * 0.9 + 0.08 + r * 0.35, -0.12))],
             [0.035 + r * 0.12, 0.0], WHORN, 4)
    # Head.
    ellipsoid(bm, (0, 0.6, 2.4), (0.16, 0.15, 0.24), WSKIN, 10, 7)
    tube(bm, [(0, 0.61, 2.45), (0, 0.57, 2.75), (0, 0.52, 2.98)], [0.13, 0.09, 0.05], WSKIN, 7, flat=1.2, up=(0, 1, 0))
    tube(bm, [(0, 0.48, 2.33), (0, 0.42, 2.63), (0, 0.38, 2.88)], [0.1, 0.065, 0.03], WSKIN, 6, flat=1.15, up=(0, 1, 0))
    for s in (-1, 1):
        ellipsoid(bm, (s * 0.1, 0.69, 2.48), (0.065, 0.032, 0.09), WSKIN, 6, 4)
        ellipsoid(bm, (s * 0.118, 0.645, 2.53), (0.035, 0.025, 0.045), WEYES, 6, 4)
        b = Vector((s * 0.09, 0.71, 2.33))
        tube(bm, [b, b + Vector((s * 0.05, 0.11, -0.15)), b + Vector((s * 0.1, 0.16, -0.36)), b + Vector((s * 0.12, 0.13, -0.58))],
             [0.05, 0.038, 0.022, 0.0], WHORN, 6)
        b = Vector((s * 0.14, 0.63, 2.28))
        tube(bm, [b, b + Vector((s * 0.12, 0.04, -0.1)), b + Vector((s * 0.2, 0.02, -0.22))], [0.03, 0.018, 0.0], WHORN, 5)
        for k in range(4):
            z = 2.62 + k * 0.09
            tube(bm, [(s * 0.065, 0.53, z), (s * 0.06, 0.46, z + 0.01)], [0.014, 0.0], WHORN, 3)
    tube(bm, [(0, 0.63, 2.8), (0, 0.72, 2.76)], [0.025, 0.0], WHORN, 4)  # nose horn
    # Legs, tucked back in flight, with clawed feet.
    for s in (-1, 1):
        hip, knee, ankle = Vector((s * 0.2, -0.1, -0.55)), Vector((s * 0.3, -0.33, -0.33)), Vector((s * 0.28, -0.45, -0.82))
        tube(bm, [hip, hip.lerp(knee, 0.5), knee], [0.12, 0.1, 0.07], WSKIN, 7)
        tube(bm, [knee, ankle], [0.065, 0.045], WSKIN, 6)
        for dx in (-0.07, 0.0, 0.07):
            toe = ankle + Vector((s * dx, -0.08, -0.22))
            tube(bm, [ankle, toe], [0.035, 0.022], WSKIN, 5)
            tube(bm, [toe, toe + Vector((0, -0.07, -0.05)), toe + Vector((0, -0.12, 0.0))], [0.022, 0.012, 0.0], WHORN, 4)
    return bm


def wyvern_wing(side):
    """Bat-like wing round its shoulder: arm and finger bones (skin), a double-sided membrane between them."""
    bm = bmesh.new()
    lay = open_layer(bm)
    X = lambda x, y, z: Vector((side * x, y, z))
    S, E, Wr = X(0, 0, 0), X(0.85, 0.08, 0.3), X(1.75, 0.12, 0.05)
    tips = [X(3.4, 0.0, -0.05), X(3.0, -0.03, -0.95), X(2.35, -0.05, -1.6), X(1.45, -0.05, -1.88)]
    R = X(0.0, -0.05, -1.35)
    tube(bm, [S, S.lerp(E, 0.5), E], [0.11, 0.085, 0.07], WSKIN, 7)
    tube(bm, [E, E.lerp(Wr, 0.5), Wr], [0.065, 0.05, 0.05], WSKIN, 6)
    ellipsoid(bm, Wr, (0.07, 0.06, 0.07), WSKIN, 6, 4)
    for t in tips:
        mid = Wr.lerp(t, 0.5) + Vector((0, 0.03, 0))
        tube(bm, [Wr, mid, t], [0.04, 0.026, 0.008], WSKIN, 5)
    tube(bm, [Wr, Wr + X(0.02, 0.06, 0.18), Wr + X(-0.04, 0.02, 0.3)], [0.03, 0.018, 0.0], WHORN, 4)  # thumb claw

    # Membrane: panels fanning from the wrist, with scalloped trailing edges between the fingers.
    def scallop(a, b, toward, k=0.24):
        m = a.lerp(b, 0.5)
        return m.lerp(toward, k) + Vector((0, -0.04, 0))

    panels = []
    for a, b in zip(tips, tips[1:]):
        panels.append([Wr, a, scallop(a, b, Wr), b])
    panels.append([Wr, tips[-1], scallop(tips[-1], R, Wr, 0.2), R, S])
    panels.append([S, E, Wr])
    for poly in panels:
        c = sum(poly, Vector()) / len(poly) + Vector((0, -0.07, 0))
        for i in range(len(poly)):
            a, b = poly[i], poly[(i + 1) % len(poly)]
            for sgn in (1, -1):
                off = Vector((0, sgn * 0.006, 0))
                f = bm.faces.new((bm.verts.new(a + off), bm.verts.new(b + off), bm.verts.new(c + off)))
                f.normal_update()
                if f.normal.y * sgn < 0:
                    f.normal_flip()
                f.material_index = WMEMBRANE
                f[lay] = 1
    bmesh.ops.remove_doubles(bm, verts=[v for v in bm.verts if v.link_faces and all(f[lay] for f in v.link_faces)],
                             dist=1e-5)
    return bm


# ---------------------------------------------------------------- placement


def yaw_quat(a):
    return [0.0, math.sin(a / 2), 0.0, math.cos(a / 2)]


def yaw_xz(a, x, z):
    """Unity: the local (x, z) turned by a yaw of a radians about +y."""
    c, s = math.cos(a), math.sin(a)
    return x * c + z * s, -x * s + z * c


def place_spawn_rocks(rng, widths):
    """Five rocks round the front of the ledge. (The world's big outcrop fills the left side between about -110 and
    -40 degrees from +z, and the cave mound is behind, so the first rock stands back-left past the outcrop.)"""
    rocks = []
    for i, (target, r0, r1) in enumerate(((-122, 15.5, 17.0), (-26, 12.5, 16.0), (18, 12.5, 16.0), (62, 12.0, 16.0),
                                          (110, 12.0, 16.0))):
        best = None
        for _ in range(600):
            ang = math.radians(target + rng.uniform(-14, 14))
            r = rng.uniform(r0, r1)
            x, z = r * math.sin(ang), r * math.cos(ang)
            rad = widths[i] / 2
            ring = [(x + math.cos(a) * (rad + 0.5), z + math.sin(a) * (rad + 0.5)) for a in
                    (k / 12 * math.tau for k in range(12))]
            if any(blocked(px, pz) for px, pz in [(x, z)] + ring):
                continue
            gap = pool_gap(x, z) - rad
            if gap < 1.2:
                continue
            score = min(gap, 3.0) - abs(math.degrees(ang) - target) * 0.02
            if best is None or score > best[0]:
                best = (score, x, z)
        assert best, f"no room for spawn rock {i}"
        rocks.append((best[1], best[2], widths[i] / 2))
    return rocks


def place_group(rng, fp, placed, rocks, prefer=None):
    for attempt in range(20000):
        if prefer and attempt < 5000:
            ang = math.radians(rng.uniform(*prefer[0]))
            r = rng.uniform(*prefer[1])
        else:
            ang = rng.uniform(-math.pi, math.pi)
            r = rng.uniform(3.5 + fp, 18.0 - fp)
        x, z = r * math.sin(ang), r * math.cos(ang)
        if r - fp < 3.5 or r + fp > 18.0:
            continue
        ring = [(x + math.cos(a) * fp, z + math.sin(a) * fp) for a in (k / 14 * math.tau for k in range(14))]
        pts = [(x, z)] + ring
        if any(pz < -1.0 and abs(px) < 6.5 for px, pz in pts):
            continue
        if any(pool_gap(px, pz) < 0.8 for px, pz in pts):
            continue
        if any(math.hypot(x - rx, z - rz) < fp + max(2.5, rr + 1.5) for rx, rz, rr in rocks):
            continue
        if any(math.hypot(x - qx, z - qz) < fp + qf + 0.4 for qx, qz, qf in placed):
            continue
        hs = [ground_height(px, pz) for px, pz in pts]
        if max(hs) - min(hs) > 0.4:
            continue
        if any(blocked(px, pz) for px, pz in pts + [(x + math.cos(a) * (fp + 0.5), z + math.sin(a) * (fp + 0.5))
                                                     for a in (k / 10 * math.tau for k in range(10))]):
            continue
        return x, z
    raise RuntimeError(f"no room for a remains group (footprint {fp:.2f})")


# ---------------------------------------------------------------- scattered rocks and bones


def s_stone(size, subdiv):
    def build(bm, rng):
        s = (size * rng.uniform(0.75, 1.25), size * rng.uniform(0.45, 0.85), size * rng.uniform(0.75, 1.25))
        craggy(bm, rng, s, subdiv, ROCK, chops=3, octaves=(0.26, 0.0))
    return build


def s_long_bone(bm, rng):
    length = rng.uniform(0.28, 0.46)
    bone(bm, (-length / 2, 0.02, 0), (length / 2, 0.02, rng.uniform(-0.05, 0.05)), rng.uniform(0.013, 0.019), sides=4)


def s_crossed_bones(bm, rng):
    for k in range(rng.randint(2, 4)):
        a = rng.uniform(0, math.tau)
        c = Vector((math.cos(a), 0, math.sin(a))) * rng.uniform(0.14, 0.24)
        o = Vector((rng.uniform(-0.1, 0.1), 0.02 + k * 0.012, rng.uniform(-0.1, 0.1)))
        bone(bm, o - c, o + c, rng.uniform(0.012, 0.018), sides=4)


def s_ribs(bm, rng):
    for k in range(rng.randint(2, 4)):
        z, w, h = k * 0.09 + rng.uniform(-0.02, 0.02), rng.uniform(0.2, 0.3), rng.uniform(0.02, 0.12)
        tube(bm, [(0, 0.02, z), (w * 0.5, h, z + 0.02), (w, 0.015, z + 0.05)], [0.012, 0.01, 0.006], BONE, 3)


def s_spine(bm, rng):
    pts = resample([(-0.3, 0.03, 0), (-0.1, 0.03, rng.uniform(-0.08, 0.08)), (0.1, 0.03, rng.uniform(-0.08, 0.08)),
                    (0.3, 0.03, 0)], 9)
    tube(bm, pts, [0.024 if i % 2 else 0.015 for i in range(9)], BONE, 4)


def s_giant_rib(bm, rng):
    """One great rib standing out of the ground like a tusk."""
    h, w = rng.uniform(1.0, 1.7), rng.uniform(0.3, 0.6)
    tube(bm, [(0, -0.1, 0), (w * 0.35, h * 0.55, 0), (w * 0.8, h * 0.85, 0), (w * 1.3, h, 0)], [0.05, 0.04, 0.028, 0.0], BONE, 5)


SMALL_BONES = [s_long_bone] * 4 + [s_crossed_bones] * 3 + [s_ribs] * 3 + [s_spine] * 2 + [d_skull] * 3 + \
              [d_buried_skull] * 2 + [d_cracked_skull] * 2
SKELETONS = [g_sprawl_broken_sword, g_arm_on_chest_helmet, g_sunk_ribcage, g_partial, g_sprawl_spear, g_skull_pile]


def scatter_spot(rng, rmin, rmax, fp, taken, rocks, ledge, fp_gap=0.25, tries=600, around=(), spread=0.0):
    """A free place for a piece of footprint radius fp at rmin..rmax from the arrival, or None. Most land ahead of the
    player, where they're seen; with `around`, many land beside an earlier piece, so things lie in natural clusters."""
    for _ in range(tries):
        if around and rng.random() < 0.65:
            cx, cz = rng.choice(around)
            x, z = cx + rng.gauss(0, spread), cz + rng.gauss(0, spread)
            if not rmin <= math.hypot(x, z) <= rmax:
                continue
        else:
            a, r = rng.uniform(-math.pi, math.pi), math.sqrt(rng.uniform(rmin ** 2, rmax ** 2))
            if not ledge and rng.random() < 0.6:
                a *= 0.42
            x, z = r * math.sin(a), r * math.cos(a)
        ring = [(x + math.cos(t) * fp, z + math.sin(t) * fp) for t in (k / 6 * math.tau for k in range(6))]
        pts = [(x, z)] + ring
        if ledge:
            if any((pz < -1.0 and abs(px) < 6.5) or (pz < 5.0 and abs(px) < 1.6) for px, pz in pts):
                continue  # the way out of the cave
            if any(math.hypot(x - rx, z - rz) < fp + rr + 1.5 for rx, rz, rr in rocks):
                continue  # the Kalakh's boulders
            if fp > 0.3 and math.hypot(x, z - 6.0) < 5.5 + fp:
                continue  # the middle of the fight: small things only
        if any(math.hypot(x - qx, z - qz) < fp + qf + fp_gap for qx, qz, qf in taken):
            continue
        hs = [ground_height(px, pz) for px, pz in pts]
        if max(hs) - min(hs) > (0.45 if ledge else max(0.9, fp * 0.8)):
            continue
        if any(blocked(px, pz) for px, pz in pts):
            continue
        return x, z
    return None


def add_piece(target, scratch, build, rng, x, z, scale=1.0, sink=0.0):
    """Builds a piece, turns it, lays it on the ground at (x, z) and adds it to `target`. Returns its footprint."""
    tmp = bmesh.new()
    build(tmp, rng)
    cx = (min(v.co.x for v in tmp.verts) + max(v.co.x for v in tmp.verts)) / 2
    cz = (min(v.co.z for v in tmp.verts) + max(v.co.z for v in tmp.verts)) / 2
    yaw = rng.uniform(0, math.tau)
    g0 = ground_height(x, z)
    for v in tmp.verts:
        wx, wz = yaw_xz(yaw, (v.co.x - cx) * scale, (v.co.z - cz) * scale)
        v.co = Vector((x + wx, v.co.y * scale + ground_height(x + wx, z + wz) - sink, z + wz))
    fp = max(math.hypot(v.co.x - x, v.co.z - z) for v in tmp.verts)
    tmp.to_mesh(scratch)
    tmp.free()
    target.from_mesh(scratch)
    return fp


def scatter(placed, rocks, put):
    rng = random.Random(7719)
    scratch = bpy.data.meshes.new("_scatter")
    rocks_ledge, bones_ledge, rocks_plains, bones_plains = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
    taken = list(placed)
    count = {}

    def scatter_many(name, target, builds, n, rmin, rmax, fp, ledge, scale=lambda: 1.0, sink=lambda: 0.0, gap=0.25, spread=0.0):
        done = 0
        here = []
        for i in range(n):
            spot = scatter_spot(rng, rmin, rmax, fp, taken, rocks, ledge, gap, around=here, spread=spread)
            if spot is None:
                continue
            build = builds[i % len(builds)] if len(builds) < 8 else rng.choice(builds)
            real = add_piece(target, scratch, build, rng, spot[0], spot[1], scale(), sink())
            taken.append((spot[0], spot[1], min(real, fp * 1.5)))
            here.append(spot)
            done += 1
        count[name] = done

    # The biggest first: they need the most room. The dead beasts lie where they can be seen from the ledge.
    scatter_many("beasts", bones_plains, [g_beast], 7, 36, 105, 9.0, False, lambda: rng.uniform(2.3, 3.2), lambda: 0.25, 3.0)
    scatter_many("human skeletons, ledge", bones_ledge, SKELETONS, 6, 7, 19, 1.0, True, gap=0.3)
    scatter_many("human skeletons, plains", bones_plains, SKELETONS, 8, 30, 62, 1.0, False, spread=5.0)
    scatter_many("great ribs", bones_plains, [s_giant_rib], 40, 32, 100, 1.0, False, lambda: rng.uniform(1.0, 2.2), lambda: 0.1, spread=6.0)
    scatter_many("boulders", rocks_plains, [s_stone(1.0, 2)], 75, 33, 170, 1.8, False, lambda: rng.uniform(1.7, 4.2), lambda: 0.35, 0.8, spread=9.0)
    scatter_many("rocks, plains", rocks_plains, [s_stone(1.0, 1)], 520, 30, 160, 0.7, False, lambda: rng.uniform(0.4, 1.7), lambda: 0.1, 0.1, spread=5.0)
    scatter_many("bones, plains", bones_plains, SMALL_BONES, 110, 30, 80, 0.45, False, lambda: 1.0, spread=5.0)
    scatter_many("stones, ledge", rocks_ledge, [s_stone(1.0, 2)], 190, 2.5, 19.5, 0.22, True, lambda: rng.uniform(0.12, 0.58), lambda: 0.04, 0.08, spread=1.3)
    scatter_many("bones, ledge", bones_ledge, SMALL_BONES, 95, 3, 19.5, 0.32, True, gap=0.12, spread=1.5)

    bpy.data.meshes.remove(scratch)
    for name, bm in (("Scatter_Rocks_Ledge", rocks_ledge), ("Scatter_Bones_Ledge", bones_ledge),
                     ("Scatter_Rocks_Plains", rocks_plains), ("Scatter_Bones_Plains", bones_plains)):
        finish(name, bm, smooth=False)
        put(name, (0.0, 0.0, 0.0))
    print("[Extras] scattered " + ", ".join(f"{n} {c}" for n, c in count.items()))


# ---------------------------------------------------------------- main


def main():
    rng = random.Random(1307)
    placements = []
    points = {}

    def put(name, pos, rot=(0.0, 0.0, 0.0, 1.0)):
        placements.append({"name": name, "position": [round(float(c), 4) for c in pos],
                           "rotation": [round(float(c), 6) for c in rot], "scale": 1.0})

    # Spawn rocks first (the remains keep clear of them).
    rock_meshes = [spawn_rock(rng, i) for i in range(5)]
    rocks = place_spawn_rocks(rng, [w for _bm, _h, w in rock_meshes])
    points["spawnRocks"], points["spawnRockHeights"] = [], []
    for i, ((bm, h, w), (x, z, _r)) in enumerate(zip(rock_meshes, rocks)):
        cracks = crack_ribbons(bm, h, rng)
        finish(f"SpawnRock_{i}", bm, smooth=False)
        finish(f"SpawnRockCracks_{i}", cracks, smooth=False)
        y = ground_height(x, z)
        yaw = rng.uniform(0, math.tau)
        for name in (f"SpawnRock_{i}", f"SpawnRockCracks_{i}"):
            put(name, (x, y, z), yaw_quat(yaw))
        points["spawnRocks"].append([round(x, 4), round(y, 4), round(z, 4)])
        points["spawnRockHeights"].append(round(h, 3))

    # Remains.
    placed = []
    for i, build in enumerate(GROUPS):
        grng = random.Random(500 + i)
        bm = bmesh.new()
        build(bm, grng)
        cx = (min(v.co.x for v in bm.verts) + max(v.co.x for v in bm.verts)) / 2
        cz = (min(v.co.z for v in bm.verts) + max(v.co.z for v in bm.verts)) / 2
        for v in bm.verts:  # pivot at the middle of the group's footprint
            v.co.x -= cx
            v.co.z -= cz
        fp = max(math.hypot(v.co.x, v.co.z) for v in bm.verts) + 0.1
        prefer = ((-60, 60), (8, 15)) if build is g_beast else ((-80, 80), (4.5, 10)) if build is g_slumped else None
        x, z = place_group(rng, fp, placed, rocks, prefer)
        placed.append((x, z, fp))
        yaw = rng.uniform(0, math.tau)
        if build is g_slumped:  # facing the arrival spot, so the player sees him
            yaw = math.atan2(-x, -z)
        g0 = ground_height(x, z)
        for v in bm.verts:  # follow the ground's small rolls
            wx, wz = yaw_xz(yaw, v.co.x, v.co.z)
            v.co.y += ground_height(x + wx, z + wz) - g0
        finish(f"Remains_{i:02d}", bm)
        put(f"Remains_{i:02d}", (x, g0 - 0.03, z), yaw_quat(yaw))
        print(f"[Extras] Remains_{i:02d} = {build.__name__} at ({x:.1f}, {z:.1f}) r {math.hypot(x, z):.1f} fp {fp:.2f}")

    # Debris chunks, parked out of sight.
    for i in range(6):
        size = 0.25 + 0.45 * i / 5
        finish(f"RockChunk_{i}", chunk(rng, size), smooth=False)
        put(f"RockChunk_{i}", (-6.0 + i * 2.0, -100.0, 20.0))

    # Flyers, parked out of sight.
    bird_l, bird_r = Vector((-0.05, 0.03, 0.04)), Vector((0.05, 0.03, 0.04))
    finish("Bird_Body", bird_body())
    finish("Bird_WingL", bird_wing(-1))
    finish("Bird_WingR", bird_wing(1))
    body = Vector((0, -100, 0))
    put("Bird_Body", body)
    put("Bird_WingL", body + bird_l)
    put("Bird_WingR", body + bird_r)
    wy_l, wy_r = Vector((-0.28, 0.14, 0.62)), Vector((0.28, 0.14, 0.62))
    finish("Wyvern_Body", wyvern_body())
    finish("Wyvern_WingL", wyvern_wing(-1))
    finish("Wyvern_WingR", wyvern_wing(1))
    body = Vector((0, -120, 0))
    put("Wyvern_Body", body)
    put("Wyvern_WingL", body + wy_l)
    put("Wyvern_WingR", body + wy_r)
    points["birdWings"] = [[round(c, 4) for c in bird_l], [round(c, 4) for c in bird_r]]
    points["wyvernWings"] = [[round(c, 4) for c in wy_l], [round(c, 4) for c in wy_r]]

    # More of the battle strewn about (own random sequence, so everything above stays as it was).
    drng = random.Random(4242)
    centre_count = [0]
    built = []
    for i, (build, big) in enumerate(DEBRIS):
        grng = random.Random(900 + i)
        bm = bmesh.new()
        build(bm, grng)
        cx = (min(v.co.x for v in bm.verts) + max(v.co.x for v in bm.verts)) / 2
        cz = (min(v.co.z for v in bm.verts) + max(v.co.z for v in bm.verts)) / 2
        for v in bm.verts:
            v.co.x -= cx
            v.co.z -= cz
        fp = max(math.hypot(v.co.x, v.co.z) for v in bm.verts) + 0.08
        built.append((fp, big, build, bm))
    built.sort(key=lambda b: -b[0])  # the widest first (they need the most room)
    for i, (fp, big, build, bm) in enumerate(built):
        x, z = place_debris(drng, fp, big, placed, rocks, centre_count)
        placed.append((x, z, fp))
        yaw = drng.uniform(0, math.tau)
        g0 = ground_height(x, z)
        for v in bm.verts:  # follow the ground's small rolls
            wx, wz = yaw_xz(yaw, v.co.x, v.co.z)
            v.co.y += ground_height(x + wx, z + wz) - g0
        finish(f"Debris_{i:02d}", bm)
        put(f"Debris_{i:02d}", (x, g0 - 0.02, z), yaw_quat(yaw))
        print(f"[Extras] Debris_{i:02d} = {build.__name__} at ({x:.1f}, {z:.1f}) r {math.hypot(x, z):.1f} fp {fp:.2f}")

    # Rocks and bones over the ledge and the plains (own random sequence, like the debris).
    scatter(placed, rocks, put)

    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=False, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
    base = os.path.splitext(OUT_FBX)[0]
    with open(base + ".materials.json", "w") as f:
        json.dump({"materials": [{k: v for k, v in m.items() if k != "_mat"} for m in MATERIALS]}, f, indent=1)
    with open(base + ".placements.json", "w") as f:
        json.dump({"objects": placements}, f, indent=1)
    with open(base + ".points.json", "w") as f:
        json.dump(points, f, indent=1)
    remains = sum(t for n, t in TRIS.items() if n.startswith("Remains_"))
    debris = sum(t for n, t in TRIS.items() if n.startswith("Debris_"))
    print(f"[Extras] debris {debris} tris in {sum(1 for n in TRIS if n.startswith('Debris_'))} pieces")
    print(f"[Extras] remains {remains} tris; total {sum(TRIS.values())} tris -> {OUT_FBX}")
    print("[Extras] TRIS " + json.dumps(TRIS))


main()
