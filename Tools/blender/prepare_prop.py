"""
Turns a downloaded model .blend (Poly Haven style: Principled BSDF with colour / roughness / metal / normal
textures) into an attic prop that build_attic.py can place:

    blender -b --disable-autoexec <source.blend> -P Tools/blender/prepare_prop.py -- <name> [only=prefix,...] [skip=prefix,...]
            [decimate=0.15] [groups=Body:prefix+prefix;Lid@back:prefix]

- applies every modifier and joins the parts (or only those whose names start with an `only` prefix);
  `decimate` keeps that share of the triangles (for heavy scans); `groups` keeps moving parts as separate
  objects, each with its own pivot (base = the prop's origin, back = back bottom edge, outer = outer bottom edge);
- moves the prop's origin to its bottom centre and keeps real-world size;
- writes 1K textures to Assets/_Project/Art/Environment/Attic/Textures/Props/<name>/:
  <mat>_Color.jpg, <mat>_Normal.jpg and a half-size <mat>_MetalSmooth.png (metal in R, smoothness in A, for URP Lit);
- skips subdivision modifiers (they only multiply triangles; smooth shading keeps the look);
- saves Tools/blender/props/<name>.blend (just the prop) and <name>.json (size and materials for Unity).
Always open downloaded files with --disable-autoexec so scripts inside them don't run.
"""

import json
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
args = sys.argv[sys.argv.index("--") + 1:]
NAME = args[0]
ONLY = [p for a in args[1:] if a.startswith("only=") for p in a[5:].split(",")]
SKIP = [p for a in args[1:] if a.startswith("skip=") for p in a[5:].split(",")]
DECIMATE = float(next((a[9:] for a in args[1:] if a.startswith("decimate=")), 1.0))
# groups=Body:chest_bottom+chest_lock;Lid@back:chest_lid  → objects <name>_Body and <name>_Lid (pivot "back")
GROUPS = []
for a in args[1:]:
    if a.startswith("groups="):
        for g in a[7:].split(";"):
            head, prefixes = g.split(":")
            gname, _, kind = head.partition("@")
            GROUPS.append((gname, kind or "base", prefixes.split("+")))
TEX_REL = f"Assets/_Project/Art/Environment/Attic/Textures/Props/{NAME}"
TEX_DIR = os.path.join(PROJECT, TEX_REL)
OUT_DIR = os.path.join(HERE, "props")
MAX_SIZE = 1024


def wanted(obj):
    if obj.type != "MESH" or obj.hide_render:
        return False
    if ONLY and not any(obj.name.startswith(p) for p in ONLY):
        return False
    return not any(obj.name.startswith(p) for p in SKIP)


# ---------------------------------------------------------------- geometry: apply, simplify, join, pivots

parts = [o for o in bpy.data.objects if wanted(o)]
if not parts:
    sys.exit(f"[Prop] {NAME}: no mesh objects matched")
for obj in parts:
    for mod in obj.modifiers:  # subdivision only multiplies triangles; smooth shading keeps the look
        if mod.type == "SUBSURF":
            mod.levels = 0
    if DECIMATE < 1.0:  # heavy scans: keep this share of the triangles
        dec = obj.modifiers.new("Simplify", "DECIMATE")
        dec.ratio = DECIMATE
        dec.use_collapse_triangulate = True
depsgraph = bpy.context.evaluated_depsgraph_get()

# Each group becomes its own object (a chest body and its lid, a gate's two doors); one group by default.
groups = GROUPS or [("", "base", [p.name for p in parts])]
materials = []
meshes = {}
for gname, _, prefixes in groups:
    bm = bmesh.new()
    for obj in parts:
        if GROUPS and not any(obj.name.startswith(p) for p in prefixes):
            continue
        mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
        mesh.transform(obj.matrix_world)
        if obj.matrix_world.determinant() < 0:
            mesh.flip_normals()
        remap = []
        for m in mesh.materials:
            if m not in materials:
                materials.append(m)
            remap.append(materials.index(m))
        before = len(bm.faces)
        bm.from_mesh(mesh)
        bm.faces.ensure_lookup_table()
        for f in bm.faces[before:]:
            f.material_index = remap[f.material_index] if remap else 0
        bpy.data.meshes.remove(mesh)
    meshes[gname] = bm

# The whole prop's bottom centre is its origin; each group's pivot is given relative to it.
all_verts = [v.co for bm in meshes.values() for v in bm.verts]
lo = Vector([min(c[i] for c in all_verts) for i in range(3)])
hi = Vector([max(c[i] for c in all_verts) for i in range(3)])
offset = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
size = hi - lo


def pivot_of(bm, kind):
    """base: the prop's origin. back: the group's back bottom edge (a lid's hinge; models face -Y, so the
    back is +Y). outer: the group's bottom edge furthest from the middle (a door leaf's hinge)."""
    if kind == "base":
        return Vector((0, 0, 0))
    glo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    ghi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    mid = (glo + ghi) / 2
    if kind == "back":
        return Vector((mid.x, ghi.y, glo.z))
    if kind == "outer":
        return Vector((glo.x if mid.x < 0 else ghi.x, mid.y, glo.z))
    sys.exit(f"[Prop] unknown pivot {kind}")


objects = []  # (object name, mesh, pivot relative to the prop's origin)
tris = 0
for gname, kind, _ in groups:
    bm = meshes[gname]
    bmesh.ops.translate(bm, vec=-offset, verts=bm.verts[:])
    pivot = pivot_of(bm, kind)
    bmesh.ops.translate(bm, vec=-pivot, verts=bm.verts[:])
    oname = f"{NAME}_{gname}" if gname else NAME
    mesh = bpy.data.meshes.new(oname)
    bm.to_mesh(mesh)
    bm.free()
    tris += sum(len(p.vertices) - 2 for p in mesh.polygons)
    objects.append((oname, mesh, pivot))

# ---------------------------------------------------------------- textures


def linked_image(socket):
    """The image feeding a socket, looking through a Normal Map node, colour ramps and the like."""
    if socket is None or not socket.is_linked:
        return None
    node = socket.links[0].from_node
    if node.type == "TEX_IMAGE":
        return node.image
    for s in node.inputs:
        img = linked_image(s)
        if img is not None:
            return img
    return None


def shader_of(mat):
    """The surface shader: a Principled BSDF, or a custom node group feeding the material output."""
    if not mat or not mat.node_tree:
        return None
    nodes = mat.node_tree.nodes
    bsdf = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is not None:
        return bsdf
    out = next((n for n in nodes if n.type == "OUTPUT_MATERIAL" and n.inputs["Surface"].is_linked), None)
    return out.inputs["Surface"].links[0].from_node if out else None


def socket(node, *names):
    """The node's first input with one of these names (Principled and custom groups name them differently)."""
    return next((node.inputs[n] for n in names if n in node.inputs), None)


def load_pixels(img, colorspace, size):
    """RGBA float pixels of an image at `size`, read from disk again so the source stays untouched."""
    path = bpy.path.abspath(img.filepath, library=img.library)
    copy = bpy.data.images.load(path, check_existing=False)
    copy.colorspace_settings.name = colorspace
    if tuple(copy.size) != (size, size):
        copy.scale(size, size)
    px = np.empty(size * size * 4, dtype=np.float32)
    copy.pixels.foreach_get(px)
    bpy.data.images.remove(copy)
    return px.reshape(size, size, 4)


def save(px, path, colorspace, fmt):
    h, w = px.shape[:2]
    # An 8-bit image saves its values exactly. (A float image gets its colour divided by alpha on saving,
    # which wrecks the metal/smoothness mask: smoothness lives in alpha.)
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True, float_buffer=False)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(px.astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = fmt
    if fmt == "JPEG":
        bpy.context.scene.render.image_settings.quality = 90
    img.save()
    bpy.data.images.remove(img)


os.makedirs(TEX_DIR, exist_ok=True)
os.makedirs(OUT_DIR, exist_ok=True)
entries = []
for i, mat in enumerate(materials):
    out_name = f"M_Prop_{NAME}" + (f"_{i}" if len(materials) > 1 else "")
    bsdf = shader_of(mat)
    entry = {"name": out_name, "color": [1.0, 1.0, 1.0], "smoothness": 0.4, "metallic": 0.0, "alpha": 1.0,
             "emission": [0, 0, 0], "texture": "", "tile": 1.0, "emissive": False,
             "baseMap": "", "normalMap": "", "maskMap": "", "cutout": False}
    if bsdf is not None:
        base_s, rough_s = socket(bsdf, "Base Color", "Diffuse", "Color", "Albedo"), socket(bsdf, "Roughness", "Rough")
        metal_s, normal_s = socket(bsdf, "Metallic", "Metal"), socket(bsdf, "Normal")
        alpha_s, trans_s = socket(bsdf, "Alpha", "Opacity"), socket(bsdf, "Transmission Weight", "Transmission")
        base_img, rough_img, metal_img = linked_image(base_s), linked_image(rough_s), linked_image(metal_s)
        normal_img, alpha_img = linked_image(normal_s), linked_image(alpha_s)
        if base_s is not None and not base_s.is_linked and hasattr(base_s, "default_value"):
            entry["color"] = list(base_s.default_value)[:3]
        if rough_s is not None and not rough_s.is_linked and hasattr(rough_s, "default_value"):
            entry["smoothness"] = 1.0 - rough_s.default_value
        if metal_s is not None and not metal_s.is_linked and hasattr(metal_s, "default_value"):
            entry["metallic"] = metal_s.default_value
        # Glass is see-through (without its alpha texture). Leaves and petals cut out of flat cards by an alpha
        # map get "cutout" (Unity clips them to shape). Parts that merely share a glass's opacity map stay solid.
        glass = "glass" in (mat.name if mat else "").lower() or \
            (trans_s is not None and not trans_s.is_linked and trans_s.default_value > 0.5) or \
            (alpha_s is not None and not alpha_s.is_linked and hasattr(alpha_s, "default_value") and alpha_s.default_value < 0.99)
        if glass:
            entry["alpha"] = 0.3
        cutout = alpha_img is not None and not glass and "lantern" not in NAME.lower()
        tex = MAX_SIZE
        if base_img is not None:
            tex = min(MAX_SIZE, max(base_img.size[0], 4))
            px = load_pixels(base_img, "sRGB", tex)
            if cutout:  # the alpha map goes into the colour texture's alpha
                px[..., 3] = load_pixels(alpha_img, "Non-Color", tex)[..., 0]
                save(px, os.path.join(TEX_DIR, f"{out_name}_Color.png"), "sRGB", "PNG")
                entry["baseMap"], entry["cutout"] = f"{TEX_REL}/{out_name}_Color.png", True
            else:
                save(px, os.path.join(TEX_DIR, f"{out_name}_Color.jpg"), "sRGB", "JPEG")
                entry["baseMap"] = f"{TEX_REL}/{out_name}_Color.jpg"
        if normal_img is not None:
            save(load_pixels(normal_img, "Non-Color", tex), os.path.join(TEX_DIR, f"{out_name}_Normal.jpg"), "Non-Color", "JPEG")
            entry["normalMap"] = f"{TEX_REL}/{out_name}_Normal.jpg"
        if rough_img is not None or metal_img is not None:
            small = max(4, tex // 2)  # gloss detail reads fine at half size
            rough = load_pixels(rough_img, "Non-Color", small)[..., 0] if rough_img else np.full((small, small), 1 - entry["smoothness"])
            metal = load_pixels(metal_img, "Non-Color", small)[..., 0] if metal_img else np.full((small, small), entry["metallic"])
            mask = np.stack([metal, metal, metal, 1.0 - rough], -1)
            save(mask, os.path.join(TEX_DIR, f"{out_name}_MetalSmooth.png"), "Non-Color", "PNG")
            entry["maskMap"] = f"{TEX_REL}/{out_name}_MetalSmooth.png"

    # A plain preview material with the new textures, under the name Unity will use.
    new = bpy.data.materials.new(out_name)
    if bpy.app.version < (5, 0, 0):
        new.use_nodes = True
    nb = new.node_tree.nodes.get("Principled BSDF")
    nb.inputs["Base Color"].default_value = (*entry["color"], 1.0)
    nb.inputs["Roughness"].default_value = 1.0 - entry["smoothness"]
    nb.inputs["Metallic"].default_value = entry["metallic"]
    if entry["baseMap"]:
        t = new.node_tree.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(os.path.join(PROJECT, entry["baseMap"]))
        new.node_tree.links.new(t.outputs["Color"], nb.inputs["Base Color"])
    if entry["normalMap"]:
        t = new.node_tree.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(os.path.join(PROJECT, entry["normalMap"]))
        t.image.colorspace_settings.name = "Non-Color"
        nm = new.node_tree.nodes.new("ShaderNodeNormalMap")
        new.node_tree.links.new(t.outputs["Color"], nm.inputs["Color"])
        new.node_tree.links.new(nm.outputs["Normal"], nb.inputs["Normal"])
    if entry["alpha"] < 1:
        nb.inputs["Alpha"].default_value = entry["alpha"]
    for _, m, _ in objects:
        m.materials.append(new)
    entries.append(entry)

# ---------------------------------------------------------------- save just the prop

for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj)
for coll in list(bpy.data.collections):
    bpy.data.collections.remove(coll)
for oname, mesh, pivot in objects:
    obj = bpy.data.objects.new(oname + "_new", mesh)
    obj.location = pivot
    bpy.context.scene.collection.objects.link(obj)
bpy.data.orphans_purge(do_recursive=True)
for oname, mesh, _ in objects:
    bpy.data.objects[oname + "_new"].name = mesh.name = oname  # the source's own objects of these names are gone now
bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backups
blend_path = os.path.join(OUT_DIR, f"{NAME}.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_path, relative_remap=True)
bpy.ops.file.make_paths_relative()
bpy.ops.wm.save_mainfile()

with open(os.path.join(OUT_DIR, f"{NAME}.json"), "w") as f:
    json.dump({"name": NAME, "size": [size.x, size.y, size.z], "triangles": tris, "materials": entries,
               "objects": [{"name": o, "pivot": [p.x, p.y, p.z]} for o, _, p in objects]}, f, indent=1)
print(f"[Prop] {NAME}: {len(parts)} parts -> {len(objects)} objects, {tris} triangles, "
      f"size {size.x:.2f} x {size.y:.2f} x {size.z:.2f} m, {len(entries)} materials")
