"""
Builds the monster world (the Kalakh's dimension, after the user's night-lake reference, img.png) and exports it:

    blender -b -P Tools/blender/build_monster_world.py            (from the Unity project folder)

Writes Assets/_Project/Art/Environment/MonsterWorld/MonsterWorld.fbx with MonsterWorld.materials.json,
MonsterWorld.placements.json and MonsterWorld.points.json. Then in Unity: Arcane Strike → Create Monster World Scene.
The sky and moon are painted separately (Tools/sky/build_night_sky.py).

The world (Unity metres; the player steps out of the cave mouth at the origin, facing +Z towards the peak):
- World_Ground: a raised basalt ledge ~21 m across where the player arrives (the fighting ground, nearly level),
  falling away in jagged cliffs to bare rocky plains that run out to the mountains.
- World_Peak: one great jagged crimson peak straight ahead under the moon, with lower ridges on its flanks.
- World_Mountains: dark crimson ranges all round the horizon.
- World_Rocks: jagged outcrops and boulders, big ones close by at the sides, the far ones out on the plains.
- World_CaveMouth: the rock mound behind the arrival, with the opening the player has just come out of.
MonsterWorld.points.json: the peak's top (key "crater", for the flying beasts) and the cave mouth's threshold.
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
OUT_FBX = os.path.join(PROJECT, "Assets/_Project/Art/Environment/MonsterWorld/MonsterWorld.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(21)

TO_BLENDER = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0))).to_4x4()
MATERIALS = {}

ARENA = 16.0            # flat, solid fighting ground round the arrival
PLATEAU = ARENA + 5.0   # the raised ledge the player arrives on; beyond it cliffs drop to the plains
PLAINS = -9.0           # the rocky plains, far below the ledge
VIEW_DROP = 3.5         # how far the ledge's front rim falls below the fighting ground
PEAK = Vector((0.0, 0.0, 235.0))  # straight ahead, under the moon, far enough off for the lake to open before it
PEAK_Y = 102.0
PEAK_FOOT = 92.0  # radius


def material(name, color, roughness=0.8, metallic=0.0, emission=None, tex=None, tile=1.0):
    full = "M_World_" + name
    m = bpy.data.materials.new(full)
    m.diffuse_color = (*color, 1.0)
    MATERIALS[full] = {"name": full, "color": list(color), "smoothness": 1.0 - roughness, "metallic": metallic, "alpha": 1.0,
                       "emission": list(emission) if emission else [0, 0, 0], "texture": tex or "", "tile": tile,
                       "emissive": emission is not None}
    return m


BASALT = material("Basalt", (0.2, 0.15, 0.19), 0.7, tex="Rock058", tile=3.0)
ROCK = material("Rock", (0.3, 0.15, 0.18), 0.8, tex="Rock058", tile=4.0)
PEAK_ROCK = material("Peak", (0.62, 0.12, 0.15), 0.85, tex="Rock058", tile=14.0)
FAR = material("Mountains", (0.34, 0.08, 0.12), 0.9, tex="Rock058", tile=18.0)
UNUSED = material("Unused", (0.0, 0.0, 0.0))  # (build_cracks' throwaway mesh)

POINTS = {}

# World_Rocks, and the extras placed round them, must stay where they are. They follow on from the random calls made
# before build_rocks, so that sequence is kept: build_peak burns the calls the old lava streams made, and scenery that
# wants other randomness uses SCENERY.
SCENERY = random.Random(22)

# Clear patches of the fighting ground (x, z, radius; Unity metres): build_world_extras.py keeps its remains and
# boulders off them, so there is open footing to fight on.
POOLS = [(-6.5, 7.0, 1.8), (6.5, 5.0, 1.5), (2.5, 12.0, 2.0), (-9.0, -1.0, 1.5), (8.5, 11.0, 1.6), (-3.5, 14.0, 1.4)]


def pool_radius(pool, x, z):
    """The pool's ragged rim: its radius in the direction of (x, z)."""
    px, pz, r = pool
    a = math.atan2(z - pz, x - px)
    return r * (1.0 + 0.16 * math.sin(a * 3 + px) + 0.09 * math.sin(a * 7 + pz))


def finish(name, bm, mat, facing_up=False, smooth=True):
    """A bmesh in Unity terms → an object in Blender's axes, with box-projected UVs at the material's tiling."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if facing_up and sum(f.normal.y for f in bm.faces) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    uv = bm.loops.layers.uv.verify()
    tile = MATERIALS[mat.name]["tile"]
    for f in bm.faces:
        n = f.normal
        axis = max(range(3), key=lambda k: abs(n[k]))
        a, b = [k for k in range(3) if k != axis]
        for loop in f.loops:
            loop[uv].uv = (loop.vert.co[a] / tile, loop.vert.co[b] / tile)
        f.smooth = smooth
    bmesh.ops.transform(bm, matrix=TO_BLENDER, verts=bm.verts[:])
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])  # TO_BLENDER mirrors
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    print(f"[World] {name}: {len(me.polygons)} faces")
    return obj


def merge(bm, tmp):
    me = bpy.data.meshes.new("_t")
    tmp.to_mesh(me)
    tmp.free()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)


def polar_grid(bm, rings, segments, height, centre=Vector((0, 0, 0))):
    """A disc of quads in rings (dense in the middle, coarse far out); height(x, z) gives each vertex's y."""
    middle = bm.verts.new(Vector((centre.x, height(centre.x, centre.z), centre.z)))
    prev = None
    for r in rings:
        ring = []
        for k in range(segments):
            a = k / segments * math.tau
            x, z = centre.x + math.cos(a) * r, centre.z + math.sin(a) * r
            ring.append(bm.verts.new(Vector((x, height(x, z), z))))
        if prev is None:
            for k in range(segments):
                bm.faces.new((middle, ring[k], ring[(k + 1) % segments]))
        else:
            for k in range(segments):
                bm.faces.new((prev[k], ring[k], ring[(k + 1) % segments], prev[(k + 1) % segments]))
        prev = ring


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- the ground


def channel(x, z):
    """0..1: how deep in an old channel on the lake bed (x, z) is (a web of winding lines, none on the fighting ground)."""
    warp = Vector((x * 0.012 + noise.noise(Vector((x * 0.01, 0, z * 0.01))) * 0.8, 0, z * 0.012))
    n = abs(noise.noise(warp))  # ridged: zero along winding lines
    width = 0.07 + 0.03 * noise.noise(Vector((x * 0.004, 5, z * 0.004)))
    return smoothstep(width, 0.0, n) * smoothstep(PLATEAU + 10, PLATEAU + 20, math.hypot(x, z))


def ground_height(x, z):
    return ledge_height(x, z)


def ledge_height(x, z):
    d = math.hypot(x, z)
    rolling = noise.noise(Vector((x * 0.015, 1, z * 0.015))) * 2.2 + noise.noise(Vector((x * 0.05, 2, z * 0.05))) * 0.6
    rough = noise.noise(Vector((x * 0.5, 3, z * 0.5))) * 0.08
    plains = PLAINS + max(0.0, rolling) * smoothstep(PLATEAU + 10, PLATEAU + 50, d) + rough * 2
    c = channel(x, z)
    plains -= 1.0 * smoothstep(0.0, 0.6, c)  # the old channels, on the lake bed
    edge = PLATEAU + noise.noise(Vector((x * 0.08, 6, z * 0.08))) * 3.0  # a ragged rim
    k = smoothstep(edge, edge + 7.0, d)
    ledge = rough * 0.5 + max(0.0, noise.noise(Vector((x * 0.1, 7, z * 0.1)))) * 0.6 * smoothstep(ARENA, PLATEAU, d)
    # Beyond the fighting ground the ledge tips downhill ahead, so the lake shows over its rim from eye height.
    ahead = smoothstep(0.1, 0.8, z / max(d, 1e-3))
    ledge -= VIEW_DROP * smoothstep(ARENA + 2.0, PLATEAU, d) * ahead
    h = ledge + (plains - ledge) * k
    h += noise.noise(Vector((x * 0.3, 8, z * 0.3))) * 1.6 * k * (1 - k) * 4  # broken cliff faces
    # Rising towards the volcano's foot and the mountains far off.
    h += smoothstep(230, 300, d) * 10.0
    return h


def build_ground():
    rings = [0.6 * k for k in range(1, 34)]
    r = rings[-1]
    while r < 290:
        r *= 1.055
        rings.append(r)
    bm = bmesh.new()
    polar_grid(bm, rings, 144, ground_height)
    return finish("World_Ground", bm, BASALT, facing_up=True)


def ribbon(bm, points, width, lift=0.02, up=Vector((0, 1, 0))):
    """A flat strip along points (Unity), lying just above the surface."""
    left = []
    for i, p in enumerate(points):
        a = points[max(0, i - 1)]
        b = points[min(len(points) - 1, i + 1)]
        d = (b - a)
        d.y = 0
        d = d.normalized() if d.length > 1e-6 else Vector((1, 0, 0))
        side = Vector((d.z, 0, -d.x)) * width * (0.5 + 0.5 * math.sin(i / max(1, len(points) - 1) * math.pi))
        left.append((bm.verts.new(p - side + up * lift), bm.verts.new(p + side + up * lift)))
    for (a0, b0), (a1, b1) in zip(left, left[1:]):
        bm.faces.new((a0, a1, b1, b0))


def build_cracks():
    bm = bmesh.new()
    for i in range(40):
        a = random.uniform(0, math.tau)
        r = random.uniform(2.5, ARENA + 6)
        x, z = math.cos(a) * r, math.sin(a) * r
        if z < -1.5 and abs(x) < 6:
            continue  # not across the cave mouth
        heading = random.uniform(0, math.tau)
        pts = []
        for s in range(random.randint(4, 9)):
            pts.append(Vector((x, ground_height(x, z), z)))
            heading += random.uniform(-0.6, 0.6)
            step = random.uniform(0.4, 1.0)
            x += math.cos(heading) * step
            z += math.sin(heading) * step
        ribbon(bm, pts, random.uniform(0.04, 0.12), lift=0.015)
    return finish("World_Cracks", bm, UNUSED, smooth=False)


# ---------------------------------------------------------------- the peak


def peak_height(x, z):
    d = math.hypot(x - PEAK.x, z - PEAK.z)
    k = max(0.0, 1.0 - d / PEAK_FOOT)
    # Sharp ridgelines running off the top, with broken rock over them; both fade out at the very tip.
    ridges = 1.0 - abs(noise.noise(Vector((x * 0.03, 9, z * 0.03)))) * 3.0
    ridges += (1.0 - abs(noise.noise(Vector((x * 0.075, 5, z * 0.075)))) * 3.0) * 0.4
    broken = noise.noise(Vector((x * 0.16, 4, z * 0.16))) * 2.5
    slope = (PEAK_Y - PLAINS) * k ** 1.5
    return max(PLAINS - 1.0, PLAINS + slope + (ridges * 20.0 + broken) * min(1.0, k * 3) * (1.0 - k ** 3))


def build_peak():
    bm = bmesh.new()
    rings = [PEAK_FOOT * (k / 44) ** 1.25 for k in range(1, 45)]
    polar_grid(bm, rings, 144, peak_height, PEAK)
    # Lower ridges stepping away on both sides, as in the reference.
    for i in range(16):
        side = -1 if i % 2 else 1
        x = side * (55 + (i // 2) * 15 + SCENERY.uniform(-6, 6))
        z = PEAK.z + SCENERY.uniform(-25, 15)
        h = SCENERY.uniform(30, 50) * (1.0 - (i // 2) * 0.075)
        add_spire(bm, Vector((x, PLAINS, z)), h, h * SCENERY.uniform(0.75, 1.0), segs=12, jag=0.5, seed=200 + i)
    for _ in range(14):
        random.random()  # (see SCENERY)
    POINTS["crater"] = [PEAK.x, PEAK_Y, PEAK.z]  # (the top: the flying beasts circle it)
    return finish("World_Peak", bm, PEAK_ROCK)


# ---------------------------------------------------------------- mountains, rocks and the cave mouth


def add_spire(bm, base, height, radius, segs=9, lean=Vector((0, 0, 0)), jag=0.35, seed=0.0):
    tmp = bmesh.new()
    bmesh.ops.create_cone(tmp, cap_ends=True, segments=segs, radius1=radius, radius2=radius * 0.05, depth=height)
    bmesh.ops.subdivide_edges(tmp, edges=[e for e in tmp.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 1e-3], cuts=3)
    for v in tmp.verts:
        k = (v.co.z + height / 2) / height  # 0 at the base .. 1 at the tip
        v.co = Vector((v.co.x, v.co.z + height / 2, v.co.y))
        v.co += lean * k * k
        r = Vector((v.co.x, 0, v.co.z))
        if r.length > 1e-4:
            v.co += r.normalized() * noise.noise(v.co * 0.15 + Vector((seed, seed, seed))) * radius * jag
        v.co += base
    merge(bm, tmp)


def build_mountains():
    bm = bmesh.new()
    for i in range(60):
        a = random.uniform(0, math.tau)
        if abs(math.atan2(math.sin(a - math.radians(75)), math.cos(a - math.radians(75)))) < 0.18:
            continue  # (see SCENERY)
        r = random.uniform(230, 300)
        height, radius = random.uniform(25, 75) * 0.77, random.uniform(25, 45)
        if abs(math.atan2(math.sin(a - math.radians(90)), math.cos(a - math.radians(90)))) < 0.5:
            continue  # leave the sky round the peak clear
        base = Vector((math.cos(a) * r, PLAINS + 6.0, math.sin(a) * r))
        add_spire(bm, base, height, radius, segs=8, jag=0.45, seed=i)
    return finish("World_Mountains", bm, FAR)


def build_rocks():
    bm = bmesh.new()
    placed = 0
    while placed < 34:
        a = random.uniform(0, math.tau)
        r = random.uniform(PLATEAU + 8, 90) if placed > 6 else random.uniform(ARENA + 3, ARENA + 12)
        x, z = math.cos(a) * r, math.sin(a) * r
        if placed <= 6:
            x = random.choice((-1, 1)) * random.uniform(12, 22)  # the big ones at the sides, near
            z = random.uniform(2, 20)
        if channel(x, z) > 0.3:
            continue
        h = random.uniform(2, 9) if placed > 6 else random.uniform(6, 14)
        base = Vector((x, ground_height(x, z) - 0.5, z))
        add_spire(bm, base, h, h * random.uniform(0.5, 0.9), segs=7, lean=Vector((random.uniform(-2, 2), 0, random.uniform(-2, 2))),
                  jag=0.5, seed=50 + placed)
        if random.random() < 0.35:  # (see SCENERY)
            random.random(), random.random()
        placed += 1
    return finish("World_Rocks", bm, ROCK)


def build_cave_mouth():
    """The rock mound the player has come out of: a lumpy hill behind the arrival with an arched opening."""
    bm = bmesh.new()
    tmp = bmesh.new()
    bmesh.ops.create_uvsphere(tmp, u_segments=40, v_segments=20, radius=1.0)
    centre = Vector((0.0, -1.5, -11.0))
    for v in tmp.verts:
        v.co = Vector((v.co.x * 12.0, v.co.z * 10.0, v.co.y * 9.0)) + centre
        v.co += (v.co - centre).normalized() * noise.noise(v.co * 0.18) * 2.2
    dead = [f for f in tmp.faces if f.calc_center_median().y < -1.2 or
            (abs(f.calc_center_median().x) < 2.0 and f.calc_center_median().y < 3.4 and f.calc_center_median().z > -6)]
    bmesh.ops.delete(tmp, geom=dead, context="FACES")
    merge(bm, tmp)
    # The tunnel going back into the hill from the opening (dark inside), closed at its end.
    tunnel = bmesh.new()
    rings = []
    for i in range(9):
        z = -0.5 - i * 0.8
        ring = []
        for k in range(18):
            a = k / 18 * math.tau
            flare = 1.0 + 0.6 * max(0.0, 1.0 - i / 2.0)
            y = max(0.0, 1.2 * flare + math.sin(a) * 1.75 * flare)
            p = Vector((math.cos(a) * 1.5 * flare, y, z))
            if y > 0.05:
                p += Vector((math.cos(a), math.sin(a), 0)) * noise.noise(p * 1.1) * 0.3
            ring.append(tunnel.verts.new(p))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(18):
            tunnel.faces.new((r0[k], r1[k], r1[(k + 1) % 18], r0[(k + 1) % 18]))
    end = tunnel.verts.new(Vector((0, 1.2, -7.6)))
    for k in range(18):
        tunnel.faces.new((rings[-1][k], end, rings[-1][(k + 1) % 18]))
    POINTS["threshold"] = [0.0, 0.0, -2.2]
    return finish("World_CaveMouth", bm, ROCK), finish("World_CaveTunnel", tunnel, ROCK)


def main():
    build_ground()
    bpy.data.objects.remove(build_cracks())  # no cracks now; it still runs for its random calls (see SCENERY)
    build_peak()
    build_mountains()
    build_rocks()
    mouth, tunnel = build_cave_mouth()
    tunnel.data.flip_normals()  # seen from inside
    for o in bpy.data.objects:
        if o.type == "MESH" and len(o.data.polygons) > 22000:
            o.modifiers.new("Decimate", "DECIMATE").ratio = 22000 / len(o.data.polygons)
    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=False, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
    base = os.path.splitext(OUT_FBX)[0]
    with open(base + ".materials.json", "w") as f:
        json.dump({"materials": list(MATERIALS.values())}, f, indent=1)
    with open(base + ".placements.json", "w") as f:
        json.dump({"objects": [{"name": o.name, "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": 1.0}
                               for o in bpy.data.objects if o.type == "MESH"]}, f, indent=1)
    with open(base + ".points.json", "w") as f:
        json.dump(POINTS, f, indent=1)
    deps = bpy.context.evaluated_depsgraph_get()
    tris = sum(sum(len(p.vertices) - 2 for p in o.evaluated_get(deps).data.polygons) for o in bpy.data.objects if o.type == "MESH")
    print(f"[World] ~{tris} triangles → {OUT_FBX}")


main()
