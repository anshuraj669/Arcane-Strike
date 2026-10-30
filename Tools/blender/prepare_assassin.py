"""
Prepares the plague-assassin outfit model (the Weaver's outfit) for build_weaver.py:

    blender -b -P Tools/blender/prepare_assassin.py -- <assassin.fbx> <texture folder>

Reads the Tripo FBX (1.9 M triangles, 0.98 units tall, Z up, facing -Y), cuts it down to about 60 k triangles
(keeping its UVs), and writes props/weaver_assassin.obj in the frame the other figures use: 1 unit tall, feet at
-0.5, facing +Z, x towards the figure's left, with UVs. Copies its textures into the Unity project as
Assets/_Project/Art/Characters/Weaver/Textures/Assassin_Color.jpg, _Normal.jpg and _MetalSmooth.png (metallic in R,
smoothness = 1 - roughness in A, URP's packing).
"""

import os
import shutil
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
args = sys.argv[sys.argv.index("--") + 1:]
FBX, TEXTURES = args[0], args[1]
OUT_OBJ = os.path.join(HERE, "props", "weaver_assassin.obj")
OUT_TEX = os.path.join(PROJECT, "Assets/_Project/Art/Characters/Weaver/Textures")
TARGET_TRIS = 60000


def find(word):
    for name in os.listdir(TEXTURES):
        if word in name.lower():
            return os.path.join(TEXTURES, name)
    raise FileNotFoundError(f"no *{word}* texture in {TEXTURES}")


def textures():
    os.makedirs(OUT_TEX, exist_ok=True)
    shutil.copy(find("basecolor"), os.path.join(OUT_TEX, "Assassin_Color.jpg"))
    shutil.copy(find("normal"), os.path.join(OUT_TEX, "Assassin_Normal.jpg"))
    metal = bpy.data.images.load(find("metallic"))
    rough = bpy.data.images.load(find("roughness"))
    w, h = metal.size
    if tuple(rough.size) != (w, h):
        rough.scale(w, h)
    m = np.array(metal.pixels[:]).reshape(h, w, 4)
    r = np.array(rough.pixels[:]).reshape(h, w, 4)
    out = np.zeros((h, w, 4), dtype=np.float32)
    out[..., 0] = m[..., 0]
    out[..., 1] = m[..., 0]
    out[..., 2] = m[..., 0]
    out[..., 3] = 1.0 - r[..., 0]
    img = bpy.data.images.new("Assassin_MetalSmooth", w, h, alpha=True)
    img.pixels = out.ravel()
    img.filepath_raw = os.path.join(OUT_TEX, "Assassin_MetalSmooth.png")
    img.file_format = "PNG"
    img.save()
    print(f"[Assassin] textures → {OUT_TEX}")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX)
    obj = [o for o in bpy.data.objects if o.type == "MESH"][0]
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris > TARGET_TRIS:
        d = obj.modifiers.new("Decimate", "DECIMATE")
        d.ratio = TARGET_TRIS / tris
        d.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=d.name)
    me = obj.data
    zs = [v.co.z for v in me.vertices]
    floor, height = min(zs), max(zs) - min(zs)
    uv = me.uv_layers.active.data
    os.makedirs(os.path.dirname(OUT_OBJ), exist_ok=True)
    with open(OUT_OBJ, "w") as f:
        f.write("# plague assassin (Tripo), prepared by prepare_assassin.py: 1 unit tall, feet at -0.5, facing +Z\n")
        for v in me.vertices:
            x, y, z = v.co
            # Blender: facing -Y, Z up, the figure's left at +X → x left, y up from -0.5, z front.
            f.write(f"v {x / height:.5f} {(z - floor) / height - 0.5:.5f} {-y / height:.5f}\n")
        for loop in me.loops:
            u, w = uv[loop.index].uv
            f.write(f"vt {u:.5f} {w:.5f}\n")
        for p in me.polygons:
            f.write("f " + " ".join(f"{me.loops[li].vertex_index + 1}/{li + 1}" for li in p.loop_indices) + "\n")
    print(f"[Assassin] {len(me.polygons)} faces → {OUT_OBJ}")
    textures()


main()
