"""
Builds the Ascension cave (where the attic's rift drops the player) and exports it for Unity:

    blender -b -P Tools/blender/build_cave.py            (from the Unity project folder)

Writes Assets/_Project/Art/Environment/Cave/Cave.fbx with Cave.materials.json, Cave.placements.json and
Cave.lights.json (where the mushroom clusters are, for their warm lights). Then in
Unity: Arcane Strike → Create Ascension Cave Scene.

The cave (Unity metres; the player arrives at the origin facing +Z):
- Cave_Walls: an uneven rock dome about 12 x 14 m and 7 m high, a hole in the roof above the altar and a short
  chimney up to a pale sky glow (Cave_Sky), where the shaft of light comes down.
- Cave_Floor: uneven rock floor; Cave_Pebbles: a field of rounded stones, thickest round the altar.
- Cave_Stalactites / Cave_Stalagmites: hanging and standing spikes.
- Cave_Mushrooms (caps glowing a warm amber underneath) and Cave_Stems: shelf fungus up the walls, as in the
  reference (ascension.jpg).
- Cave_Altar: a slab of rock on a short column, 4 m ahead; its top is at 1.0 m.
- Cave_Katana: the katana (props/antique_katana_01.blend), driven point-first into the altar, hilt up.
- Cave_Passage: the way out, a rough tunnel from the back of the cave (behind and right of the altar) about 7 m long,
  closed at its far end, where the threshold into the monster world shimmers (the scene builder puts it there).
- Cave_ExitRocks: the rock pile blocking the tunnel's mouth until the Ascension is over.
  Cave.passage.json: where the mouth, the rock pile and the threshold are.
- Cave_Lantern: grandfather's brass lantern from the attic's chest (props/Lantern_01.blend), at the origin, base
  at the pivot. The scene keeps it hidden: when the cave is played on its own (no lantern carried through the rift
  from the attic), it is the lantern that comes out of the rift.
"""

import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_FBX = os.path.join(PROJECT, "Assets/_Project/Art/Environment/Cave/Cave.fbx")
KATANA_BLEND = os.path.join(HERE, "props", "antique_katana_01.blend")
LANTERN = os.path.join(HERE, "props", "Lantern_01")  # .blend + .json (prepare_prop.py), as in the attic
KATANA_TEX = "Assets/_Project/Art/Environment/Attic/Textures/Props/antique_katana_01/M_Prop_antique_katana_01"

# The altar (Unity metres) and the katana in it.
ALTAR = Vector((0.0, 0.0, 4.0))
ALTAR_TOP = 1.0
KATANA_SUNK = 0.2  # how deep the point is driven into the stone

bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(7)

# Unity (x right, y up, z forward) → Blender, as in the other builders.
TO_BLENDER = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0))).to_4x4()

MATERIALS = {}
GLOWS = []  # the mushroom clusters (Unity), where the scene builder puts their warm lights


def material(name, color, roughness=0.8, metallic=0.0, emission=None, tex=None, tile=1.0, alpha=1.0):
    full = "M_Cave_" + name
    m = bpy.data.materials.new(full)
    m.diffuse_color = (*color, alpha)
    MATERIALS[full] = {"name": full, "color": list(color), "smoothness": 1.0 - roughness, "metallic": metallic, "alpha": alpha,
                       "emission": list(emission) if emission else [0, 0, 0], "texture": tex or "", "tile": tile,
                       "emissive": emission is not None}
    return m


ROCK = material("Rock", (0.36, 0.38, 0.45), 0.85, tex="Rock058", tile=2.5)
FLOOR = material("Floor", (0.3, 0.31, 0.36), 0.75, tex="Rock058", tile=2.0)
PEBBLE = material("Pebble", (0.42, 0.43, 0.48), 0.45, tex="Rock058", tile=0.4)
ALTAR_STONE = material("Altar", (0.45, 0.46, 0.5), 0.7, tex="Rock058", tile=0.8)
CAP = material("MushroomCap", (0.55, 0.36, 0.2), 0.6, emission=(0.55, 0.26, 0.06))
STEM = material("MushroomStem", (0.62, 0.55, 0.45), 0.7, emission=(0.08, 0.05, 0.02))
SKY = material("Sky", (0.7, 0.85, 1.0), 1.0, emission=(1.6, 2.2, 3.2))
KATANA = material("Katana", (1.0, 1.0, 1.0), 0.4, 1.0)
MATERIALS["M_Cave_Katana"].update({"baseMap": KATANA_TEX + "_Color.jpg", "normalMap": KATANA_TEX + "_Normal.jpg",
                                   "maskMap": KATANA_TEX + "_MetalSmooth.png", "smoothnessScale": 1.0})

# ---------------------------------------------------------------- helpers (built in Unity terms)


def finish(name, bm, mat, smooth=True, project_uv=True, facing_up=False):
    """A bmesh in Unity terms → an object in Blender's axes, with box-projected UVs at the material's tiling.
    facing_up: an open sheet (the floor) whose faces must look up (recalculating can't tell which side is out)."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if facing_up and sum(f.normal.y for f in bm.faces) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    if project_uv:
        uv = bm.loops.layers.uv.verify()
        tile = MATERIALS[mat.name]["tile"]
        for f in bm.faces:
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
    print(f"[Cave] {name}: {len(me.polygons)} faces")
    return obj


def lumpy(bm, verts, scale, amount, centre=None):
    """Pushes vertices in and out with noise (along the direction from `centre`, or their normal)."""
    for v in verts:
        n = (v.co - centre).normalized() if centre is not None else v.normal
        v.co += n * noise.noise(v.co * scale) * amount


def add_sphere(bm, centre, radius, squash=(1, 1, 1), segs=8, rings=5, lump=0.0, seed=0.0):
    tmp = bmesh.new()
    bmesh.ops.create_uvsphere(tmp, u_segments=segs, v_segments=rings, radius=radius)
    for v in tmp.verts:
        v.co = Vector((v.co.x * squash[0], v.co.z * squash[1], v.co.y * squash[2]))  # (z up → y up)
        if lump:
            v.co += v.co.normalized() * noise.noise(v.co * 3.0 + Vector((seed, seed, seed))) * lump * radius
        v.co += centre
    merge(bm, tmp)


def add_cone(bm, base, tip, radius, segs=7):
    tmp = bmesh.new()
    axis = (tip - base)
    length = axis.length
    bmesh.ops.create_cone(tmp, cap_ends=True, segments=segs, radius1=radius, radius2=0.0, depth=length)
    rot = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(tmp, matrix=Matrix.Translation((base + tip) / 2) @ rot, verts=tmp.verts[:])
    for v in tmp.verts:
        v.co += Vector((noise.noise(v.co * 4.0), 0, noise.noise(v.co * 4.0 + Vector((5, 5, 5))))) * radius * 0.25
    merge(bm, tmp)


def merge(bm, tmp):
    me = bpy.data.meshes.new("_t")
    tmp.to_mesh(me)
    tmp.free()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)


# ---------------------------------------------------------------- the cave

HALL = Vector((0.0, 0.0, 3.5))  # the middle of the floor of the chamber
RX, RY, RZ = 6.0, 7.0, 7.0      # half-width, height, half-length


# The way out: from the hall's middle towards the back right.
PASSAGE_DIR = Vector((0.32, 0.0, 1.0)).normalized()
PASSAGE_SIDE = Vector((PASSAGE_DIR.z, 0.0, -PASSAGE_DIR.x))  # (its right)
PASSAGE_LENGTH = 7.0


def wall_point(direction):
    """Where the (unlumped) dome's surface is along a direction from the hall's middle."""
    d = Vector((direction.x / RX, direction.y / RY, direction.z / RZ))
    return HALL + direction / d.length


MOUTH = wall_point(PASSAGE_DIR)
MOUTH.y = 0.0


def passage_point(t):
    """The tunnel's centreline at the floor, t metres on from the cave's wall (it bends a little to the right)."""
    return MOUTH + PASSAGE_DIR * t + PASSAGE_SIDE * (0.9 * (max(t, 0.0) / PASSAGE_LENGTH) ** 2)


def in_passage(p, margin=0.0):
    """Near the tunnel's line (from 3.5 m inside the wall onwards): kept clear of pebbles, spikes and mushrooms."""
    rel = p - HALL
    along = rel.x * PASSAGE_DIR.x + rel.z * PASSAGE_DIR.z
    across = abs(rel.x * PASSAGE_SIDE.x + rel.z * PASSAGE_SIDE.z)
    return along > (MOUTH - HALL).length - 3.5 and across < 1.9 + margin


def build_walls():
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=72, v_segments=40, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * RX, v.co.z * RY, v.co.y * RZ)) + HALL
    # The floor cuts the dome; the roof opens above the altar.
    dead = [f for f in bm.faces if f.calc_center_median().y < -0.6 or
            (Vector((f.calc_center_median().x - ALTAR.x, 0, f.calc_center_median().z - ALTAR.z)).length < 1.1 and f.calc_center_median().y > 5.0)]
    bmesh.ops.delete(bm, geom=dead, context="FACES")
    # The opening at the back for the tunnel out.
    hole = [f for f in bm.faces if in_passage(f.calc_center_median(), -0.25) and f.calc_center_median().y < 3.3]
    bmesh.ops.delete(bm, geom=hole, context="FACES")
    for v in bm.verts:
        out = v.co - HALL
        rough = noise.noise(v.co * 0.35) * 1.1 + noise.noise(v.co * 1.3) * 0.3
        if abs(v.co.x - ALTAR.x) ** 2 + (v.co.z - ALTAR.z) ** 2 < 1.6 ** 2 and v.co.y > 4.5:
            rough *= 0.2  # keep the roof's hole round
        if in_passage(v.co, 1.0) and v.co.y < 4.0:
            rough *= 0.3  # and the tunnel's mouth clear
        v.co += out.normalized() * rough
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=1, use_grid_fill=True)
    lumpy(bm, bm.verts, 2.2, 0.12, centre=HALL)
    # The chimney up from the hole to the sky.
    tmp = bmesh.new()
    bmesh.ops.create_cone(tmp, cap_ends=False, segments=18, radius1=1.15, radius2=1.4, depth=4.0)
    for v in tmp.verts:
        v.co = Vector((v.co.x, v.co.z + 2.0, v.co.y))
        v.co += Vector((ALTAR.x, 6.2, ALTAR.z))
        v.co += Vector((noise.noise(v.co * 1.5), 0, noise.noise(v.co * 1.5 + Vector((3, 3, 3))))) * 0.25
    merge(bm, tmp)
    obj = finish("Cave_Walls", bm, ROCK)
    # Faces point outward from building the sphere; the cave is seen from inside.
    obj.data.flip_normals() if hasattr(obj.data, "flip_normals") else None
    return obj


def build_sky():
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=True, segments=20, radius=1.6)
    for v in bm.verts:
        v.co = Vector((v.co.x + ALTAR.x, 11.8, v.co.y + ALTAR.z))
    return finish("Cave_Sky", bm, SKY, smooth=False)


def build_floor():
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=60, y_segments=70, size=1.0)
    for v in bm.verts:
        x, z = v.co.x * RX * 1.05, v.co.y * RZ * 1.05 + HALL.z
        h = noise.noise(Vector((x, 0, z)) * 0.4) * 0.12 + noise.noise(Vector((x, 1, z)) * 1.7) * 0.03
        d = Vector((x - ALTAR.x, 0, z - ALTAR.z)).length
        h *= min(1.0, d / 2.0)  # flat round the altar and where the player stands
        h *= min(1.0, Vector((x, 0, z)).length / 2.0)
        # Rising into the walls at the edge.
        e = (x / RX) ** 2 + ((z - HALL.z) / RZ) ** 2
        if not in_passage(Vector((x, 0, z)), 0.6):
            h += max(0.0, e - 0.75) * 2.5
        else:
            h *= 0.3  # level going into the tunnel
        v.co = Vector((x, h, z))
    return finish("Cave_Floor", bm, FLOOR, facing_up=True)


def inside(x, z, margin=0.85):
    return (x / RX) ** 2 + ((z - HALL.z) / RZ) ** 2 < margin


def build_pebbles():
    bm = bmesh.new()
    n = 0
    while n < 520:
        # Thickest round the altar and along the sides; the path from the player to the altar stays clearer.
        if random.random() < 0.55:
            a, r = random.uniform(0, math.tau), abs(random.gauss(0, 1.6)) + 0.9
            x, z = ALTAR.x + math.cos(a) * r, ALTAR.z + math.sin(a) * r * 1.1
        else:
            x, z = random.uniform(-RX, RX), random.uniform(HALL.z - RZ, HALL.z + RZ)
        if not inside(x, z, 0.8) or (abs(x) < 0.7 and -0.5 < z < ALTAR.z - 0.9) or in_passage(Vector((x, 0, z)), 0.3):
            continue
        s = random.uniform(0.04, 0.14) * (1.5 if random.random() < 0.12 else 1.0)
        add_sphere(bm, Vector((x, s * 0.2, z)), s, squash=(random.uniform(0.8, 1.3), random.uniform(0.3, 0.55), random.uniform(0.7, 1.2)),
                   segs=7, rings=4, lump=0.45, seed=n)
        n += 1
    return finish("Cave_Pebbles", bm, PEBBLE)


def build_spikes():
    hang, stand = bmesh.new(), bmesh.new()
    for i in range(46):
        a, r = random.uniform(0, math.tau), random.uniform(0.25, 0.9)
        d = Vector((math.cos(a) * r, 1.0, math.sin(a) * r)).normalized()
        top = wall_point(d)
        if Vector((top.x - ALTAR.x, 0, top.z - ALTAR.z)).length < 1.9:
            continue  # keep the shaft clear
        length = random.uniform(0.6, 2.2)
        add_cone(hang, top + Vector((0, 0.6, 0)), top - Vector((0, length, 0)), random.uniform(0.12, 0.35))
    for i in range(22):
        a = random.uniform(0, math.tau)
        x, z = math.cos(a) * RX * random.uniform(0.62, 0.85), HALL.z + math.sin(a) * RZ * random.uniform(0.62, 0.85)
        if (z < 0.8 and abs(x) < 2.5) or in_passage(Vector((x, 0, z)), 0.5):
            continue  # not right behind the player's arrival, nor in the way out
        add_cone(stand, Vector((x, -0.1, z)), Vector((x, random.uniform(0.5, 1.8), z)), random.uniform(0.15, 0.4))
    return finish("Cave_Stalactites", hang, ROCK), finish("Cave_Stalagmites", stand, ROCK)


def build_mushrooms():
    caps, stems = bmesh.new(), bmesh.new()
    clusters = 0
    while clusters < 16:
        # On the walls left and right of the altar (as in the reference), low down.
        side = random.choice((-1, 1))
        z = random.uniform(1.0, 7.0)
        y = random.uniform(0.3, 2.4)
        d = Vector((side * 1.0, (y - 1.2) / RY, (z - HALL.z) / RZ * 0.6)).normalized()
        wall = wall_point(d)
        wall.y = y
        if in_passage(wall, 0.8):
            continue
        into = Vector((-side, 0, 0))
        GLOWS.append(list(wall + into * 0.7 + Vector((0, 0.3, 0))))
        for k in range(random.randint(3, 6)):
            p = wall + into * random.uniform(0.15, 0.45) + Vector((0, k * random.uniform(0.18, 0.32), random.uniform(-0.25, 0.25)))
            r = random.uniform(0.12, 0.32)
            add_sphere(caps, p, r, squash=(1.0, 0.22, 0.9), segs=10, rings=4, lump=0.15, seed=clusters * 10 + k)
            add_cone(stems, p - Vector((0, r * 0.9, 0)) + into * 0.02, p, r * 0.25, segs=6)
        clusters += 1
    return finish("Cave_Mushrooms", caps, CAP), finish("Cave_Stems", stems, STEM)


def build_altar():
    bm = bmesh.new()
    # The column, then a broad rounded slab on top (like the reference's stone).
    tmp = bmesh.new()
    bmesh.ops.create_cone(tmp, cap_ends=True, segments=12, radius1=0.42, radius2=0.3, depth=0.8)
    for v in tmp.verts:
        v.co = Vector((v.co.x, v.co.z + 0.4, v.co.y)) + ALTAR
    lumpy(tmp, tmp.verts, 3.0, 0.05)
    merge(bm, tmp)
    add_sphere(bm, ALTAR + Vector((0, ALTAR_TOP - 0.13, 0)), 0.75, squash=(1.0, 0.2, 0.85), segs=16, rings=8, lump=0.18, seed=3)
    # Rubble round its foot.
    for i in range(10):
        a = i / 10 * math.tau + random.uniform(-0.2, 0.2)
        add_sphere(bm, ALTAR + Vector((math.cos(a) * 0.55, 0.06, math.sin(a) * 0.55)), random.uniform(0.1, 0.2),
                   squash=(1, 0.6, 1), segs=7, rings=4, lump=0.3, seed=40 + i)
    return finish("Cave_Altar", bm, ALTAR_STONE)


def build_katana():
    with bpy.data.libraries.load(KATANA_BLEND) as (src, dst):
        dst.meshes = [m for m in src.meshes]
    me = dst.meshes[0]
    # .blend: pommel at z 0, blade up +Z (tip at z 1.0). Point down into the altar: flip it, the tip sunk in.
    tip_y = ALTAR_TOP - KATANA_SUNK
    me.transform(TO_BLENDER @ Matrix.Translation(Vector((ALTAR.x, tip_y + 1.0, ALTAR.z))) @
                 Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1))) @  # z (blade) → -y (down)
                 Matrix.Rotation(math.radians(90), 4, "Z"))  # the edge facing the player's left
    me.flip_normals()
    me.materials.clear()
    me.materials.append(KATANA)
    obj = bpy.data.objects.new("Cave_Katana", me)
    bpy.context.scene.collection.objects.link(obj)
    print(f"[Cave] Cave_Katana: {len(me.polygons)} faces")
    return obj


def build_passage():
    """The tunnel out: an arched tube (flat floor) from just inside the cave's wall to a closed far end."""
    bm = bmesh.new()
    rings = []
    steps = 16
    for i in range(steps + 1):
        t = -1.4 + (PASSAGE_LENGTH + 1.4) * i / steps
        c = passage_point(t)
        ahead = (passage_point(t + 0.1) - c).normalized()
        side = Vector((ahead.z, 0, -ahead.x))
        ring = []
        for k in range(20):
            a = k / 20 * math.tau
            flare = 1.0 + 0.8 * min(1.0, max(0.0, -t) / 1.4)  # the mouth opens wide to meet the cave's wall
            w, hgt = (1.45 + 0.15 * math.sin(t * 1.3)) * flare, 1.7 * flare
            y = max(0.0, 1.15 * flare + math.sin(a) * hgt)
            p = c + side * math.cos(a) * w + Vector((0, y, 0))
            if y > 0.05:  # rough walls and roof, a flat floor
                p += (p - c - Vector((0, 1.0, 0))).normalized() * noise.noise(p * 1.1) * 0.35
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(20):
            bm.faces.new((r0[k], r0[(k + 1) % 20], r1[(k + 1) % 20], r1[k]))
    end = bm.verts.new(passage_point(PASSAGE_LENGTH) + Vector((0, 1.2, 0)) + PASSAGE_DIR * 0.4)
    for k in range(20):
        bm.faces.new((rings[-1][k], rings[-1][(k + 1) % 20], end))
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.01)
    obj = finish("Cave_Passage", bm, ROCK)
    obj.data.flip_normals()  # seen from inside
    return obj


def build_exit_rocks():
    """A pile of boulders choking the tunnel's mouth."""
    bm = bmesh.new()
    centre = passage_point(-0.6)
    side = PASSAGE_SIDE
    for i in range(16):
        across = random.uniform(-1.4, 1.4)
        up = random.uniform(0.0, 2.6) * (1.0 - abs(across) / 2.2)
        r = random.uniform(0.45, 0.85)
        p = centre + side * across + Vector((0, up + r * 0.5, 0)) + PASSAGE_DIR * random.uniform(-0.4, 0.4)
        add_sphere(bm, p, r, squash=(1.0, random.uniform(0.6, 0.9), 1.0), segs=9, rings=6, lump=0.35, seed=100 + i)
    return finish("Cave_ExitRocks", bm, ROCK)


def build_lantern():
    """The attic's lantern, with its own materials (the same as the attic's: textures from prepare_prop.py)."""
    with bpy.data.libraries.load(LANTERN + ".blend") as (src, dst):
        dst.meshes = [m for m in src.meshes]
    me = dst.meshes[0]  # (Blender's own axes already: upright, base at the origin)
    with open(LANTERN + ".json") as f:
        for m in json.load(f)["materials"]:
            MATERIALS[m["name"]] = m
    obj = bpy.data.objects.new("Cave_Lantern", me)
    bpy.context.scene.collection.objects.link(obj)
    print(f"[Cave] Cave_Lantern: {len(me.polygons)} faces")
    return obj


def main():
    build_walls()
    build_sky()
    build_floor()
    build_pebbles()
    build_spikes()
    build_mushrooms()
    build_altar()
    build_katana()
    build_passage()
    build_exit_rocks()
    build_lantern()
    for o in bpy.data.objects:
        if o.type == "MESH" and len(o.data.polygons) > 12000 and o.name not in ("Cave_Katana", "Cave_Lantern"):
            o.modifiers.new("Decimate", "DECIMATE").ratio = 12000 / len(o.data.polygons)
    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
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
    with open(base + ".lights.json", "w") as f:
        json.dump({"glows": GLOWS}, f, indent=1)
    with open(base + ".passage.json", "w") as f:
        threshold = passage_point(PASSAGE_LENGTH - 0.6)
        json.dump({"mouth": list(passage_point(-0.6)), "threshold": list(threshold),
                   "facing": list(-(passage_point(PASSAGE_LENGTH) - passage_point(PASSAGE_LENGTH - 0.5)).normalized())}, f, indent=1)
    deps = bpy.context.evaluated_depsgraph_get()
    tris = sum(sum(len(p.vertices) - 2 for p in o.evaluated_get(deps).data.polygons) for o in bpy.data.objects if o.type == "MESH")
    print(f"[Cave] ~{tris} triangles → {OUT_FBX}")


main()
