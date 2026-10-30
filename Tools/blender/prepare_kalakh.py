"""
Prepares the Kalakh shadow monster (the tentacle demon) for Unity:

    blender -b -P Tools/blender/prepare_kalakh.py -- <monster.fbx> <texture folder>

Reads the Tripo FBX (a clawed, tailed demon facing +X, no rig), turns it to face Unity's +Z, stands it with its feet
at the pivot (the middle between the feet, so the tail trails behind), scales it to 1 m tall (ShadowMonster and
RiftBoss scale it to each kind's height), lightens it for Quest (about 7 k triangles), and writes
Assets/_Project/Art/Characters/Kalakh/Kalakh.fbx with Kalakh.materials.json, plus its textures (Kalakh_Color.jpg,
_Normal.jpg, _MetalSmooth.png). Then in Unity: Arcane Strike → Build Kalakh Monster.
It has no skeleton: the shadows move it as a whole (ShadowMonster rears it back, lunges, sways and bobs it).
"""

import json
import math
import os
import shutil
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
args = sys.argv[sys.argv.index("--") + 1:]
FBX, TEXTURES = args[0], args[1]
FOLDER = "Assets/_Project/Art/Characters/Kalakh"
OUT = os.path.join(PROJECT, FOLDER)
TARGET_TRIS = 7000


def find(word):
    for name in sorted(os.listdir(TEXTURES)):
        if word in name.lower():
            return os.path.join(TEXTURES, name)
    raise FileNotFoundError(f"no *{word}* texture in {TEXTURES}")


def textures():
    tex = os.path.join(OUT, "Textures")
    os.makedirs(tex, exist_ok=True)
    shutil.copy(find("basecolor"), os.path.join(tex, "Kalakh_Color.jpg"))
    shutil.copy(find("normal"), os.path.join(tex, "Kalakh_Normal.jpg"))
    metal = bpy.data.images.load(find("metallic"))
    rough = bpy.data.images.load(find("roughness"))
    w, h = metal.size
    if tuple(rough.size) != (w, h):
        rough.scale(w, h)
    m = np.array(metal.pixels[:]).reshape(h, w, 4)
    r = np.array(rough.pixels[:]).reshape(h, w, 4)
    out = np.zeros((h, w, 4), dtype=np.float32)
    out[..., 0] = out[..., 1] = out[..., 2] = m[..., 0]
    out[..., 3] = 1.0 - r[..., 0]
    img = bpy.data.images.new("Kalakh_MetalSmooth", w, h, alpha=True)
    img.pixels = out.ravel()
    img.filepath_raw = os.path.join(tex, "Kalakh_MetalSmooth.png")
    img.file_format = "PNG"
    img.save()


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX)
    obj = [o for o in bpy.data.objects if o.type == "MESH"][0]
    for o in list(bpy.data.objects):
        if o is not obj:
            bpy.data.objects.remove(o)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    obj.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Facing +X → facing -Y (Blender's front, which is Unity's +Z after export).
    me = obj.data
    me.transform(Matrix.Rotation(math.radians(-90), 4, "Z"))
    co = np.array([v.co[:] for v in me.vertices])
    floor, height = co[:, 2].min(), co[:, 2].max() - co[:, 2].min()
    feet = co[co[:, 2] < floor + height * 0.08]  # the feet (and the tail's tip, if it drags: use the front-most half)
    feet = feet[feet[:, 1] < np.median(feet[:, 1]) + 1e-6] if len(feet) > 20 else feet
    centre = Vector((feet[:, 0].mean(), feet[:, 1].mean(), floor))
    me.transform(Matrix.Scale(1.0 / height, 4) @ Matrix.Translation(-centre))

    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    if tris > TARGET_TRIS:
        d = obj.modifiers.new("Decimate", "DECIMATE")
        d.ratio = TARGET_TRIS / tris
        bpy.ops.object.modifier_apply(modifier=d.name)
    for p in me.polygons:
        p.use_smooth = True
    obj.name = me.name = "Kalakh"
    mat = bpy.data.materials.new("M_Kalakh")
    me.materials.clear()
    me.materials.append(mat)

    os.makedirs(OUT, exist_ok=True)
    textures()
    path = os.path.join(OUT, "Kalakh.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=False, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False,
        bake_anim=False, path_mode="AUTO")
    tex = FOLDER + "/Textures/Kalakh"
    # A shadow: the texture darkened and cooled, with a faint violet glow in its cracks.
    materials = [{"name": "M_Kalakh", "color": [0.55, 0.5, 0.62], "smoothness": 1.0, "metallic": 1.0, "alpha": 1.0,
                  "emission": [0.05, 0.0, 0.1], "texture": "", "tile": 1.0, "emissive": True,
                  "baseMap": tex + "_Color.jpg", "normalMap": tex + "_Normal.jpg", "maskMap": tex + "_MetalSmooth.png",
                  "smoothnessScale": 0.8}]
    with open(os.path.join(OUT, "Kalakh.materials.json"), "w") as f:
        json.dump({"materials": materials}, f, indent=1)
    print(f"[Kalakh] {len(me.polygons)} faces, 1 m tall, facing +Z → {path}")


main()
