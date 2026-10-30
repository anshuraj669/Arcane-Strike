"""
Gives the Kalakh shadow monster a skeleton, so it can really move (ShadowMonster / KalakhBody animate the bones by code):

    blender -b --disable-autoexec --python Tools/blender/rig_kalakh.py

Reads Assets/_Project/Art/Characters/Kalakh/Kalakh.fbx as prepare_kalakh.py wrote it (unrigged; or Kalakh_Static.fbx,
the copy this script keeps of it), builds an armature for its hunched, clawed, digitigrade body and long tail, skins
the mesh to it (smooth weights from each vertex's distance to the bones, with rules so an arm never pulls the chest or a
leg the tail), and writes Kalakh.fbx again, now skinned. Bone names (Unity): Hips, Spine, Chest, Neck, Head,
UpperArm.L/R, Forearm.L/R, Hand.L/R, Thigh.L/R, Shin.L/R, Foot.L/R, Tail1..Tail5. ".L" is the monster's own left.
Its eyes are on fire: two glowing almonds, Eye.L and Eye.R (material Kalakh_Eyes), parented to the Head bone and
sat in the face under the brow (found by casting at the head from in front of each eye).
Then in Unity: Arcane Strike → Build Kalakh Monster.
"""

import math
import os
import shutil

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
FOLDER = os.path.join(PROJECT, "Assets/_Project/Art/Characters/Kalakh")
MODEL = os.path.join(FOLDER, "Kalakh.fbx")
STATIC = os.path.join(FOLDER, "Kalakh_Static.fbx~")  # the unrigged original (the ~ keeps Unity from importing it)

# Joints in Blender's frame as the model imports (1 m tall, z up, facing -y, +x is the monster's left).
J = {
    "root": (0.0, 0.17, 0.0),
    "hips": (0.0, 0.18, 0.43), "spine": (0.0, 0.15, 0.55), "chest": (0.0, 0.12, 0.66), "neck": (0.0, 0.05, 0.78),
    "head": (0.0, 0.0, 0.81), "head_end": (0.0, -0.15, 0.86),
    "shoulder.L": (0.2, 0.1, 0.69), "elbow.L": (0.285, 0.13, 0.52), "wrist.L": (0.33, 0.03, 0.37), "claw.L": (0.34, -0.03, 0.2),
    "hip.L": (0.095, 0.17, 0.43), "knee.L": (0.19, 0.0, 0.24), "hock.L": (0.2, 0.13, 0.14), "toe.L": (0.21, -0.04, 0.02),
    "tail0": (0.0, 0.25, 0.42), "tail1": (0.0, 0.36, 0.25), "tail2": (0.0, 0.53, 0.1), "tail3": (-0.02, 0.76, 0.09),
    "tail4": (-0.08, 0.88, 0.25), "tail5": (-0.15, 0.91, 0.47),
}
for k in list(J):  # the right side mirrors the left
    if k.endswith(".L"):
        x, y, z = J[k]
        J[k[:-2] + ".R"] = (-x - 0.01, y, z)

# (bone, head joint, tail joint, parent)
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
for s in ("L", "R"):
    BONES += [
        (f"UpperArm.{s}", f"shoulder.{s}", f"elbow.{s}", "Chest"),
        (f"Forearm.{s}", f"elbow.{s}", f"wrist.{s}", f"UpperArm.{s}"),
        (f"Hand.{s}", f"wrist.{s}", f"claw.{s}", f"Forearm.{s}"),
        (f"Thigh.{s}", f"hip.{s}", f"knee.{s}", "Hips"),
        (f"Shin.{s}", f"knee.{s}", f"hock.{s}", f"Thigh.{s}"),
        (f"Foot.{s}", f"hock.{s}", f"toe.{s}", f"Shin.{s}"),
    ]


def allowed(bone, p):
    """Which bones may move this vertex (keeps limbs from tugging at the body and the tail from the legs)."""
    x, y, z = p
    side = "L" if x > 0 else "R"
    tail = y > 0.33 or (y > 0.22 and z < 0.42 and abs(x) < 0.12)  # behind the body (the tail curls up behind it)
    if bone.startswith("Tail"):
        return tail
    if tail:
        return bone == "Hips"  # (only where the tail meets the body)
    if bone.startswith(("UpperArm", "Forearm", "Hand")):
        if not bone.endswith(side):
            return False
        return abs(x) > 0.16 and z < 0.75 and z > 0.12
    if bone.startswith(("Thigh", "Shin", "Foot")):
        if not bone.endswith(side):
            return False
        return z < 0.5 and abs(x) > 0.02
    if bone == "Head":
        return z > 0.7
    if bone == "Neck":
        return z > 0.62
    return z > 0.3  # the trunk


def segment_distance(p, a, b):
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(ab @ ab, 1e-9), 0.0, 1.0)
    return np.linalg.norm(p - (a + np.outer(t, ab)), axis=1)


# The eyes, in the head's frame (x the monster's left, y along the head towards its snout, z up): where to look for
# the left one, and from which side to cast at it. The face's middle is a little to the right of x = 0.
EYE_LOCAL = Vector((0.04, 0.085, 0.022))
EYE_OUT = Vector((0.55, 0.8, 0.12))
EYE_MIDDLE = -0.008
EYE_SIZE = (0.017, 0.006, 0.0095)  # half-width, half-depth, half-height
EYE_COLOR, EYE_GLOW = (1.0, 0.45, 0.08), (6.0, 2.2, 0.3)


def eye_material():
    m = bpy.data.materials.new("Kalakh_Eyes")
    m.diffuse_color = (*EYE_COLOR, 1.0)
    try:
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (*EYE_COLOR, 1.0)
        bsdf.inputs["Emission Color"].default_value = (1.0, 0.37, 0.05, 1.0)
        bsdf.inputs["Emission Strength"].default_value = max(EYE_GLOW)
    except Exception:
        pass
    return m


def add_eyes(rig, mesh):
    """Eye.L / Eye.R in the face: cast in from in front of each, half sunk in the skin, slanted up at the outer corner,
    parented to the Head bone."""
    bvh = BVHTree.FromObject(mesh, bpy.context.evaluated_depsgraph_get())
    o = Vector(J["head"])
    f = (Vector(J["head_end"]) - o).normalized()
    u = Vector((0, 0, 1)) - f * f.z
    u.normalize()
    s = u.cross(f)
    mat = eye_material()
    placed = {}
    for side, sd in (("L", 1), ("R", -1)):
        guess = o + s * (EYE_MIDDLE + EYE_LOCAL.x * sd) + f * EYE_LOCAL.y + u * EYE_LOCAL.z
        out = (s * EYE_OUT.x * sd + f * EYE_OUT.y + u * EYE_OUT.z).normalized()
        hit, nrm, _, _ = bvh.ray_cast(guess + out * 0.12, -out, 0.3)
        if hit is None:
            hit, nrm, _, _ = bvh.find_nearest(guess)
        nrm = (nrm + out).normalized()
        centre = hit + nrm * EYE_SIZE[1] * 0.3
        y = -nrm
        z = u - y * u.dot(y)
        z.normalize()
        x = y.cross(z)
        a = math.radians(18.0) * sd
        x, z = x * math.cos(a) + z * math.sin(a), z * math.cos(a) - x * math.sin(a)
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=1.0)
        for v in bm.verts:
            v.co = Vector((v.co.x * EYE_SIZE[0], v.co.y * EYE_SIZE[1], v.co.z * EYE_SIZE[2]))
            v.co.z *= 1.0 - 0.55 * (abs(v.co.x) / EYE_SIZE[0]) ** 2  # pointed corners
        me = bpy.data.meshes.new(f"Eye.{side}")
        bm.to_mesh(me)
        bm.free()
        me.materials.append(mat)
        for poly in me.polygons:
            poly.use_smooth = True
        eye = bpy.data.objects.new(f"Eye.{side}", me)
        bpy.context.scene.collection.objects.link(eye)
        eye.parent = rig
        eye.parent_type = "BONE"
        eye.parent_bone = "Head"
        bpy.context.view_layer.update()
        eye.matrix_world = Matrix.Translation(centre) @ Matrix((tuple(x), tuple(y), tuple(z))).transposed().to_4x4()
        placed[side] = centre
    return placed


def main():
    if not os.path.exists(STATIC):
        shutil.copyfile(MODEL, STATIC)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=STATIC)
    mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    arm_data = bpy.data.armatures.new("KalakhRig")
    rig = bpy.data.objects.new("KalakhRig", arm_data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for name, h, t, parent in BONES:
        b = arm_data.edit_bones.new(name)
        b.head = Vector(J[h])
        b.tail = Vector(J[t])
        b.roll = 0.0
        if parent:
            b.parent = arm_data.edit_bones[parent]
            b.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")

    # Weights: each allowed bone pulls by 1/d^4 of the vertex's distance to it; the strongest three, normalised.
    verts = np.array([v.co[:] for v in mesh.data.vertices])
    names = [b[0] for b in BONES]
    dist = np.stack([segment_distance(verts, np.array(J[h]), np.array(J[t])) for _, h, t, _ in BONES], axis=1)
    weights = 1.0 / (dist ** 4 + 1e-7)
    for j, name in enumerate(names):
        mask = np.array([allowed(name, p) for p in verts])
        weights[:, j] *= mask
    empty = weights.sum(axis=1) <= 0
    weights[empty, names.index("Chest")] = 1.0
    keep = np.argsort(-weights, axis=1)[:, :3]
    groups = {n: mesh.vertex_groups.new(name=n) for n in names}
    for i in range(len(verts)):
        top = weights[i, keep[i]]
        top = top / top.sum()
        for j, w in zip(keep[i], top):
            if w > 0.01:
                groups[names[j]].add([i], float(w), "REPLACE")
    mesh.parent = rig
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = rig

    eyes = add_eyes(rig, mesh)
    print(f"[Kalakh] Eyes at L {tuple(round(c, 3) for c in eyes['L'])}, R {tuple(round(c, 3) for c in eyes['R'])}")

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=MODEL, use_selection=False, object_types={"MESH", "ARMATURE"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False, mesh_smooth_type="FACE", add_leaf_bones=False,
        armature_nodetype="NULL", use_armature_deform_only=True, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=False, path_mode="AUTO")
    print(f"[Kalakh] Rigged: {len(names)} bones, {len(verts)} vertices → {MODEL}")


main()
