"""
Builds the bronze cauldron at the heart of the Lantern Void (Ember burns in it) and exports it for Unity:

    blender -b -P Tools/blender/build_cauldron.py      (from the Unity project folder)

Writes Assets/_Project/Art/Environment/LanternVoid/Models/Cauldron.fbx: two objects, Cauldron (bowl, lip, legs,
handles) and Cauldron_Coals (the glowing bed Ember rises from), pivots at the floor under the centre.
Arcane Strike → Create Lantern Void Scene gives them their materials.
"""

import math
import os

import random

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(PROJECT, "Assets/_Project/Art/Environment/LanternVoid/Models/Cauldron.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)


def lathe(bm, profile, segments=48):
    """Spins a (radius, height) profile round the vertical axis into a closed solid of revolution."""
    verts = [bm.verts.new((r, 0.0, h)) for r, h in profile]
    edges = [bm.edges.new((a, b)) for a, b in zip(verts, verts[1:])]
    bmesh.ops.spin(bm, geom=verts + edges, cent=(0, 0, 0), axis=(0, 0, 1), angle=math.tau, steps=segments, use_duplicate=False)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)


def mesh_object(name, bm):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.smooth = True
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


# The bowl: a round belly on a low foot, a thick rolled lip, and the inside wall back down (Blender: Z up, metres).
bowl = bmesh.new()
lathe(bowl, [
    (0.0, 0.12), (0.2, 0.125), (0.34, 0.17), (0.45, 0.27), (0.52, 0.4), (0.53, 0.52), (0.5, 0.62),
    (0.46, 0.68), (0.47, 0.71), (0.53, 0.73), (0.56, 0.76), (0.54, 0.79), (0.49, 0.785),  # the rolled lip
    (0.44, 0.74), (0.46, 0.62), (0.48, 0.5), (0.46, 0.38), (0.4, 0.27), (0.3, 0.2), (0.15, 0.17), (0.0, 0.165),
])
# Three curved legs from the belly down to round feet.
for i in range(3):
    a = math.tau * i / 3 + math.pi / 6
    d = Vector((math.cos(a), math.sin(a), 0.0))
    for k in range(6):  # a chain of short, tapering segments curving outwards
        t0, t1 = k / 6, (k + 1) / 6
        p0 = d * (0.36 + 0.12 * t0 * t0) + Vector((0, 0, 0.24 * (1 - t0)))
        p1 = d * (0.36 + 0.12 * t1 * t1) + Vector((0, 0, 0.24 * (1 - t1)))
        r = 0.045 - 0.015 * t0
        seg = bmesh.ops.create_cone(bowl, cap_ends=True, segments=10, radius1=r, radius2=r * 0.92, depth=(p1 - p0).length + 0.01)
        rot = Vector((0, 0, 1)).rotation_difference((p1 - p0).normalized()).to_matrix().to_4x4()
        bmesh.ops.transform(bowl, matrix=Matrix.Translation((p0 + p1) / 2) @ rot, verts=seg["verts"])
    foot = bmesh.ops.create_uvsphere(bowl, u_segments=10, v_segments=6, radius=0.06)
    bmesh.ops.scale(bowl, vec=(1.0, 1.0, 0.5), verts=foot["verts"])
    bmesh.ops.translate(bowl, vec=d * 0.48 + Vector((0, 0, 0.02)), verts=foot["verts"])

# Two ring handles on the shoulders, hanging flat against the bowl (ring in the plane facing out).
major, minor, segs, sides = 0.085, 0.014, 20, 8
for side in (-1, 1):
    centre = Vector((side * 0.6, 0.0, 0.58))
    out = Vector((side, 0.0, 0.0))
    rings = []
    for i in range(segs):
        a = math.tau * i / segs
        n = Vector((0.0, math.cos(a), math.sin(a)))  # round the ring
        rings.append([bowl.verts.new(centre + n * major + (n * math.cos(b) + out * math.sin(b)) * minor)
                      for b in (math.tau * j / sides for j in range(sides))])
    for i in range(segs):
        for j in range(sides):
            ra, rb = rings[i], rings[(i + 1) % segs]
            bowl.faces.new((ra[j], rb[j], rb[(j + 1) % sides], ra[(j + 1) % sides]))
cauldron = mesh_object("Cauldron", bowl)

# The coals: a lumpy, glowing bed a little below the lip.
coals = bmesh.new()
bmesh.ops.create_circle(coals, cap_ends=True, segments=40, radius=0.45)
bmesh.ops.subdivide_edges(coals, edges=coals.edges[:], cuts=3, use_grid_fill=True)
random.seed(3)
for v in coals.verts:
    v.co.z = 0.58 + random.uniform(0.0, 0.035)
coals_obj = mesh_object("Cauldron_Coals", coals)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False)
tris = sum(len(p.vertices) - 2 for o in (cauldron, coals_obj) for p in o.data.polygons)
print(f"[Cauldron] wrote {OUT} ({tris} triangles)")
