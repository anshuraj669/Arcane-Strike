"""
Builds the main character, the Weaver, and exports it for Unity on its own (so the attic model stays light and
the character is only loaded when the story needs it):

    blender -b -P Tools/blender/build_weaver.py            (from the Unity project folder)
    blender -b -P build_weaver.py -- <out.fbx>             (choose the output)

Writes Assets/_Project/Art/Characters/Weaver/Weaver.fbx with Weaver.materials.json and Weaver.placements.json.
Then in Unity: Arcane Strike → Apply Attic Model (it also rebuilds Assets/_Project/Resources/Characters/Weaver.prefab,
which WarriorOutfit loads when the Ascension begins).

Sources (OBJ, 1 unit tall, feet at -0.5, facing +Z, an A-pose):
- props/weaver_body.obj: the boy himself (arkane 1.obj: hoodie, joggers, shoes).
- props/weaver_assassin.obj: the plague assassin whose clothes become the Weaver's outfit (hooded beak mask, long
  leather coat, gloves, boots), with its own UVs and textures (prepare_assassin.py makes it from the Tripo FBX).
The assassin's clothes are fitted onto the boy: the arms are bent onto his (the assassin's hang almost straight
down, the boy's are held out), joint by joint, so a sleeve sits where his sleeve was.

Objects (all under the root "Weaver", in Unity metres, 1.70 m tall):
- Body_Head, Body_Arms, Body_Torso, Body_Feet: the boy in his own clothes. Each outfit part that forms replaces
  one of them (sleeves → arms, boots → feet, coat → torso, the hood and mask → head).
- Avatar_Sleeve, Avatar_Boots, Avatar_Coat, Avatar_Scarf, Avatar_Cape, Avatar_Sword: the outfit on the whole figure
  (gloved hands, arms held out; Scarf is the hood and the beak mask), the katana sheathed across his back.
- Outfit_Sleeve_L/R, Outfit_Boots, Outfit_Coat, Outfit_Scarf, Outfit_Cape, Outfit_Sword_Hilt/_Scabbard: the parts
  fastened to the player in first person (no head, collar, upper arms or hands). Sleeves: anchor at the wrist, the sleeve runs back
  up the arm (-Z), the back of the hand is +Y.
- Weaver_Sword: the bare sword (a broad straight short sword, bronze-hilted with a red wrapped grip), pivot in the middle of the grip, blade up (+Y),
  flats facing ±Z, the edge's curve towards +X.
The sheath hangs diagonally across the back: the hilt over the right shoulder (to draw it with the right hand), the
tip at the left hip, lying over the blade moulded into the assassin's coat.
Everything except the sleeves and Weaver_Sword shares one anchor (WarriorOutfit's): 0.38 m below and 0.08 m behind
the eyes, front +Z.
"""

import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_FBX = args[0] if len(args) > 0 else os.path.join(PROJECT, "Assets/_Project/Art/Characters/Weaver/Weaver.fbx")
BODY_OBJ = os.path.join(HERE, "props", "weaver_body.obj")
OUTFIT_OBJ = os.path.join(HERE, "props", "weaver_assassin.obj")
OUTFIT_TEX = "Assets/_Project/Art/Characters/Weaver/Textures/Assassin"  # _Color.jpg, _Normal.jpg, _MetalSmooth.png
SWORD_GRIP = 0.15    # the middle of the grip, along the sword (m from the pommel)
SWORD_GUARD = 0.305  # the guard
SWORD_TIP = 0.99     # the point
TEXTURES = os.path.join(PROJECT, "Assets/_Project/Art/Environment/Attic/Textures")

bpy.ops.wm.read_factory_settings(use_empty=True)

# Unity (x right, y up, z forward) → Blender, as in build_attic.py.
TO_BLENDER = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0))).to_4x4()

# Where the player's eyes go on the body (OBJ frame, y from the feet): a little above and in front of his own,
# so looking down the player sees the front of the chest and not into the collar.
EYE = Vector((0.0, 0.94, 0.125))
SCALE = 1.6 / EYE.y
ANCHOR = EYE * SCALE - Vector((0.0, 0.38, 0.08))  # WarriorOutfit's chestBelowEyes / chestBehindEyes

# ---------------------------------------------------------------- materials

MATERIALS = {}


def material(name, color, roughness=0.8, metallic=0.0, emission=None, tex=None, tile=1.0):
    full = "M_Weaver_" + name
    m = bpy.data.materials.new(full)
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 1.0
    if tex is not None:
        nodes, links = m.node_tree.nodes, m.node_tree.links
        base = nodes.new("ShaderNodeTexImage")
        base.image = bpy.data.images.load(os.path.join(TEXTURES, tex, f"{tex}_Color.jpg"), check_existing=True)
        tint = nodes.new("ShaderNodeMix")
        tint.data_type, tint.blend_type = "RGBA", "MULTIPLY"
        tint.inputs[0].default_value = 1.0
        tint.inputs[7].default_value = (*color, 1.0)
        links.new(base.outputs["Color"], tint.inputs[6])
        links.new(tint.outputs[2], bsdf.inputs["Base Color"])
    m.diffuse_color = (*color, 1.0)
    MATERIALS[full] = {"name": full, "color": list(color), "smoothness": 1.0 - roughness, "metallic": metallic, "alpha": 1.0,
                       "emission": list(emission) if emission else [0, 0, 0], "texture": tex or "", "tile": tile,
                       "emissive": emission is not None}
    return m


def outfit_material(name):
    """The assassin's own textures (one set for every part), under a tint WarriorOutfit sets per appearance."""
    m = material(name, (1.0, 1.0, 1.0), 0.5, 1.0)
    nodes, links = m.node_tree.nodes, m.node_tree.links
    image = nodes.new("ShaderNodeTexImage")
    image.image = bpy.data.images.load(os.path.join(PROJECT, OUTFIT_TEX + "_Color.jpg"), check_existing=True)
    links.new(image.outputs["Color"], nodes.get("Principled BSDF").inputs["Base Color"])
    MATERIALS["M_Weaver_" + name].update({"baseMap": OUTFIT_TEX + "_Color.jpg", "normalMap": OUTFIT_TEX + "_Normal.jpg",
                                          "maskMap": OUTFIT_TEX + "_MetalSmooth.png", "smoothnessScale": 1.0})
    return m


# The outfit (WarriorOutfit recolours these by name for each appearance).
COAT = outfit_material("OutfitCoat")
TRIM = outfit_material("OutfitTrim")    # the gloves
BOOTS = outfit_material("OutfitBoots")
CAPE = outfit_material("OutfitCape")    # the long back of the coat
SCARF = outfit_material("OutfitScarf")  # the hood and the beak mask
# The boy.
SKIN = material("Skin", (0.72, 0.48, 0.36), 0.6)
HAIR = material("Hair", (0.13, 0.08, 0.05), 0.5)
HOODIE = material("Hoodie", (0.5, 0.55, 0.62), 0.95, tex="Fabric061", tile=0.25)
JOGGERS = material("Joggers", (0.35, 0.35, 0.37), 0.95, tex="Fabric061", tile=0.25)
SHOES = material("Shoes", (0.92, 0.9, 0.86), 0.7, tex="Leather037", tile=0.2)
# The sword.
STEEL = material("SwordSteel", (0.62, 0.64, 0.68), 0.28, 1.0, tex="Metal063", tile=0.25)
EDGE = material("SwordEdge", (0.9, 0.91, 0.94), 0.15, 1.0, tex="Metal063", tile=0.25)
FULLER = material("SwordFuller", (0.3, 0.31, 0.34), 0.35, 1.0, tex="Metal063", tile=0.25)
WRAP = material("SwordWrap", (0.42, 0.07, 0.05), 0.6, tex="Leather037", tile=0.06)
SAYA = material("Saya", (0.11, 0.075, 0.05), 0.5, tex="Leather037", tile=0.15)  # a dark leather sheath
GOLD = material("OutfitTrimSword", (0.85, 0.62, 0.25), 0.3, 1.0, tex="Metal063", tile=0.1)

# ---------------------------------------------------------------- loading


def load_obj(path):
    """Vertices (x, y from the feet, z front: OBJ frame) merged by position, and faces."""
    positions, faces = [], []
    with open(path) as f:
        for line in f:
            if line.startswith("v "):
                x, y, z = map(float, line.split()[1:4])
                positions.append(Vector((x, y + 0.5, z)))
            elif line.startswith("f "):
                faces.append([int(t.split("/")[0]) - 1 for t in line.split()[1:]])
    index, merged, remap = {}, [], []
    for p in positions:
        key = (round(p.x, 5), round(p.y, 5), round(p.z, 5))
        if key not in index:
            index[key] = len(merged)
            merged.append(p)
        remap.append(index[key])
    return merged, [[remap[i] for i in face] for face in faces]


def load_obj_uv(path):
    """Vertices (OBJ frame, y from the feet), faces as vertex indices, and each face's UVs."""
    positions, uvs, faces, face_uvs = [], [], [], []
    with open(path) as f:
        for line in f:
            if line.startswith("v "):
                x, y, z = map(float, line.split()[1:4])
                positions.append(Vector((x, y + 0.5, z)))
            elif line.startswith("vt "):
                uvs.append(tuple(map(float, line.split()[1:3])))
            elif line.startswith("f "):
                corners = [t.split("/") for t in line.split()[1:]]
                faces.append([int(c[0]) - 1 for c in corners])
                face_uvs.append([uvs[int(c[1]) - 1] if len(c) > 1 and c[1] else None for c in corners])
    return positions, faces, face_uvs


def seg(p, a, b):
    """(distance from p to the segment a-b, how far along it 0..1)"""
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length, t


def chain(side, points):
    return [Vector((side * x, y, z)) for x, y, z in points]


# Shoulder, elbow, wrist, fingertips of each figure (OBJ frame).
BOY_ARM = ((0.13, 0.735, -0.015), (0.215, 0.655, -0.015), (0.28, 0.58, 0.015), (0.335, 0.48, 0.045))
WARRIOR_ARM = ((0.115, 0.775, -0.005), (0.16, 0.63, -0.01), (0.19, 0.53, 0.025), (0.19, 0.43, 0.045))  # the assassin
BOY = {1: chain(1, BOY_ARM), -1: chain(-1, BOY_ARM)}
WARRIOR = {1: chain(1, WARRIOR_ARM), -1: chain(-1, WARRIOR_ARM)}
BODY_SHIFT = Vector((0.0, 0.0, 0.0))  # the assassin already stands where the boy does


def to_unity(p):
    """OBJ frame → Unity metres (the figure faces +Z either way; OBJ x is the figure's left)."""
    return Vector((-p.x, p.y, p.z)) * SCALE


# ---------------------------------------------------------------- fitting the warrior's arms onto the boy's


def fit_arm(p, side):
    """Moves a point of the warrior's arm onto the boy's arm: each bone (upper arm, forearm, hand) is turned and
    stretched onto his, blended by distance near the joints, and eased into the body's own shift at the shoulder."""
    src, dst = WARRIOR[side], BOY[side]
    total, weight = Vector(), 0.0
    for i in range(3):
        a, b, a2, b2 = src[i], src[i + 1], dst[i], dst[i + 1]
        u, u2 = (b - a).normalized(), (b2 - a2).normalized()
        turn = u.rotation_difference(u2)
        stretch = (b2 - a2).length / (b - a).length
        d = p - a
        along = u * d.dot(u)
        moved = a2 + turn @ (along * stretch + (d - along))
        w = 1.0 / (seg(p, a, b)[0] + 0.01) ** 4
        total += moved * w
        weight += w
    arm = total / weight
    s, e = src[0], src[1]
    t = (p - s).dot(e - s) / (e - s).length_squared  # unclamped: below 0 is the shoulder itself
    k = max(0.0, min(1.0, t / 0.35))
    k = k * k * (3 - 2 * k)
    return (p + BODY_SHIFT).lerp(arm, k)


# ---------------------------------------------------------------- building meshes


def make_part(name, items, positions, root, to_local, decimate=6000):
    """One object from [(face, material)], vertices through to_local(Unity point) → local Unity point."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    used, mats = {}, []
    for item in items:
        face, mat = item[0], item[1]
        face_uv = item[2] if len(item) > 2 else None
        vs = []
        for i in face:
            if i not in used:
                used[i] = bm.verts.new(TO_BLENDER @ to_local(positions[i]))
            vs.append(used[i])
        try:
            f = bm.faces.new(list(reversed(vs)))  # TO_BLENDER mirrors: reverse the winding to keep faces outward
        except ValueError:
            continue  # a duplicate face (or a corner used twice)
        if mat not in mats:
            mats.append(mat)
        f.material_index = mats.index(mat)
        f.smooth = True
        if face_uv is not None and None not in face_uv:
            for loop, t in zip(f.loops, reversed(face_uv)):  # the model's own UVs (corners reversed with the face)
                loop[uv].uv = t
            continue
        n = f.normal
        axis = max(range(3), key=lambda k: abs(n[k]))
        a, b = [k for k in range(3) if k != axis]
        tile = MATERIALS[mat.name]["tile"]
        for loop in f.loops:
            loop[uv].uv = (loop.vert.co[a] / tile, loop.vert.co[b] / tile)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for m in mats:
        mesh.materials.append(m)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = root
    if len(mesh.polygons) > decimate:  # light enough for Quest: halve the big parts
        obj.modifiers.new("Decimate", "DECIMATE").ratio = 0.5
    print(f"[Weaver] {name}: {len(mesh.polygons)} faces")
    return obj


def body_local(p):
    return p - ANCHOR


def build_body(root):
    """The boy, cut into the parts the outfit replaces."""
    positions, faces = load_obj(BODY_OBJ)
    parts = {}
    for face in faces:
        c = sum((positions[i] for i in face), Vector()) / len(face)
        side = 1 if c.x >= 0 else -1
        s_, e_, w_, h_ = BOY[side]
        if abs(c.x) > 0.27 and seg(c, w_, h_)[0] < 0.06 and c.y > 0.42:
            part, mat = "Arms", SKIN  # the hand
        elif 0.45 < c.y < 0.8 and abs(c.x) > 0.105 and min(seg(c, s_, e_)[0], seg(c, e_, w_)[0]) < 0.06 \
                and (abs(c.x) > 0.14 or c.y < 0.715):
            part, mat = "Arms", HOODIE
        elif c.y > 0.84:
            if (c.z < -0.045 and c.y < 0.9) or (abs(c.x) > 0.052 and c.y < 0.875):
                part, mat = "Torso", HOODIE  # the hood, round the back of the neck
            else:
                hair = c.y > 0.928 or (c.z < 0.02 and c.y > 0.875) or (abs(c.x) > 0.05 and c.y > 0.89)
                part, mat = "Head", HAIR if hair else SKIN
        elif c.y > 0.79 and abs(c.x) < 0.04 and -0.03 < c.z < 0.06:
            part, mat = "Head", SKIN  # the neck
        elif c.y < 0.085:
            part, mat = "Feet", SHOES
        elif c.y < 0.22:
            part, mat = "Feet", JOGGERS
        else:
            part, mat = "Torso", JOGGERS if c.y < 0.545 else HOODIE
        parts.setdefault("Body_" + part, []).append((face, mat))
    unity = [to_unity(p) for p in positions]
    for name, items in parts.items():
        make_part(name, items, unity, root, body_local)


def arm_x(y, side):
    """How far out (|x|) the middle of the assassin's arm is at height y (along shoulder, elbow, wrist)."""
    s_, e_, w_, _ = WARRIOR[side]
    for a, b in ((s_, e_), (e_, w_)):
        if b.y <= y <= a.y:
            return abs(a.lerp(b, (a.y - y) / (a.y - b.y)).x)
    return abs(s_.x) if y > s_.y else abs(w_.x)


# The assassin's own swords (OBJ frame): one hanging at his right hip, hilt forward, and one strapped flat across
# his back with its hilts over the right shoulder. The hip sword and the back sword's hilts are left out (the
# Weaver carries the Jyoti blade instead); the back blade itself is moulded into the coat, so it stays as part of it.
HIP_BLADE = (Vector((-0.105, 0.635, 0.075)), Vector((-0.165, 0.185, -0.105)), 0.024)


def own_blade(c):
    a, b, r = HIP_BLADE
    if seg(c, a, b)[0] < r:
        return True
    if -0.125 < c.x < -0.088 and 0.53 < c.y < 0.67 and c.z > 0.055:
        return True  # the hip sword's hilt, in front of the belt
    return c.y > 0.83 and c.x < -0.065 and c.z < -0.06  # the back sword's hilts


def build_outfit(root):
    """The assassin's clothes, sorted into parts by where each face is on his body, then fitted onto the boy."""
    positions, faces, face_uvs = load_obj_uv(OUTFIT_OBJ)
    parts = {}

    def add(name, face, mat, uvs):
        parts.setdefault(name, []).append((face, mat, uvs))

    arm_verts = {}  # vertex → side, for the faces of the arms and hands (fitted onto the boy's arms)
    for face, uvs in zip(faces, face_uvs):
        c = sum((positions[i] for i in face), Vector()) / len(face)
        side = 1 if c.x >= 0 else -1
        s_, e_, w_, h_ = WARRIOR[side]
        lr = "R" if side < 0 else "L"  # OBJ +x is the figure's left
        if own_blade(c):
            continue  # the assassin's own swords: the Weaver carries the Jyoti blade instead
        on_arm = abs(c.x) > arm_x(c.y, side) - 0.028  # the arms hang close by the coat: only their outer side
        if on_arm and seg(c, w_.lerp(h_, 0.15), h_)[0] < 0.045:
            add("Avatar_Sleeve", face, TRIM, uvs)  # the gloved hand
            arm_verts.update({i: side for i in face})
            continue
        if c.y > 0.8 and (abs(c.x) < 0.1 or c.y > 0.84):
            add("Avatar_Scarf", face, SCARF, uvs)  # the hood and the beak mask: they hide his head
            continue
        cape = c.z < -0.045 and 0.27 < c.y < 0.6
        if on_arm and not cape and c.y < 0.79:
            d_fore, t_fore = seg(c, e_, w_)
            d_upper, _ = seg(c, s_, e_)
            if d_fore < 0.045 and d_fore <= d_upper:
                add(f"Outfit_Sleeve_{lr}", face, COAT, uvs)
                add("Avatar_Sleeve", face, COAT, uvs)
                arm_verts.update({i: side for i in face})
                continue
            if d_upper < 0.045:
                add("Avatar_Sleeve", face, COAT, uvs)  # the upper arm
                arm_verts.update({i: side for i in face})
                continue
        if c.y > 0.8:
            name, mat = "Scarf", SCARF  # the top of the mantle, round the hood
        elif cape:
            name, mat = "Cape", CAPE
        elif c.y < 0.27:
            name, mat = "Boots", BOOTS
        else:
            name, mat = "Coat", COAT
        if name != "Scarf":
            add("Outfit_" + name, face, mat, uvs)  # (first person: nothing up round the eyes)
        add("Avatar_" + name, face, mat, uvs)

    fitted = [to_unity(fit_arm(p, arm_verts[i]) if i in arm_verts else p + BODY_SHIFT) for i, p in enumerate(positions)]
    for name, items in parts.items():
        to_local = body_local
        if name.startswith("Outfit_Sleeve"):
            side = -1 if name.endswith("_R") else 1
            _, elbow, wrist, _ = (to_unity(p) for p in BOY[side])
            fwd = (wrist - elbow).normalized()
            out = Vector((-side, 0.0, 0.0))  # outward, in Unity
            up = (out - fwd * out.dot(fwd)).normalized()
            right = up.cross(fwd)
            to_local = lambda p, o=wrist, r=right, u=up, f=fwd: Vector(((p - o).dot(r), (p - o).dot(u), (p - o).dot(f)))
        make_part(name, items, fitted, root, to_local)


# ---------------------------------------------------------------- the sword

class Shape:
    """Closed shapes built in Unity terms (a local frame), gathered into one bmesh per material."""

    def __init__(self):
        self.pieces = []  # (bmesh, material)

    def add(self, bm, mat):
        self.pieces.append((bm, mat))

    def loft(self, sections, mat, cap_tip=None):
        """Rings of points (lists of Vector, same count) joined into a tube, capped; cap_tip closes the last end to a point."""
        bm = bmesh.new()
        rings = [[bm.verts.new(p) for p in ring] for ring in sections]
        n = len(sections[0])
        for r0, r1 in zip(rings, rings[1:]):
            for k in range(n):
                bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]))
        bm.faces.new(list(reversed(rings[0])))
        if cap_tip is not None:
            tip = bm.verts.new(cap_tip)
            for k in range(n):
                bm.faces.new((rings[-1][k], rings[-1][(k + 1) % n], tip))
        else:
            bm.faces.new(rings[-1])
        self.add(bm, mat)

    def box(self, centre, size, mat):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=size, verts=bm.verts[:])
        bmesh.ops.translate(bm, vec=centre, verts=bm.verts[:])
        self.add(bm, mat)

    def sphere(self, centre, radius, mat, squash=(1, 1, 1), segs=12):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=segs // 2 + 2, radius=radius)
        bmesh.ops.scale(bm, vec=squash, verts=bm.verts[:])
        bmesh.ops.translate(bm, vec=centre, verts=bm.verts[:])
        self.add(bm, mat)

    def cylinder(self, centre, radius, height, mat, top=None, segs=12):
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius if top is None else top, depth=height)
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90), 3, "X"), verts=bm.verts[:])  # z → y
        bmesh.ops.translate(bm, vec=centre, verts=bm.verts[:])
        self.add(bm, mat)

    def build(self, name, root, place=Matrix.Identity(4)):
        """All pieces into one object; `place` moves them (Unity terms) before the turn into Blender's axes."""
        out = bmesh.new()
        uv = out.loops.layers.uv.new("UVMap")
        mats = []
        temp = bpy.data.meshes.new("_t")
        for bm, mat in self.pieces:
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            for f in bm.faces:
                f.smooth = True
            bm.to_mesh(temp)
            start = len(out.faces)
            out.from_mesh(temp)
            out.faces.ensure_lookup_table()
            if mat not in mats:
                mats.append(mat)
            tile = MATERIALS[mat.name]["tile"]
            for f in list(out.faces)[start:]:
                f.material_index = mats.index(mat)
                n = f.normal
                axis = max(range(3), key=lambda k: abs(n[k]))
                a, b = [k for k in range(3) if k != axis]
                for loop in f.loops:
                    loop[uv].uv = (loop.vert.co[a] / tile, loop.vert.co[b] / tile)
            bm.free()
        bpy.data.meshes.remove(temp)
        bmesh.ops.transform(out, matrix=TO_BLENDER @ place, verts=out.verts[:])
        bmesh.ops.recalc_face_normals(out, faces=out.faces[:])
        mesh = bpy.data.meshes.new(name)
        out.to_mesh(mesh)
        out.free()
        for m in mats:
            mesh.materials.append(m)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = root
        print(f"[Weaver] {name}: {len(mesh.polygons)} faces")
        return obj


def back_frame():
    """Where the sheathed katana hangs on the body (Unity, relative to the anchor): diagonally across the back, the
    guard behind the right shoulder and the tip down at the left hip, its flats against the back. WarriorOutfit.cs
    (SwordDown, SwordGuard, SwordGripToGuard) uses the same numbers to slide the bare sword into it and to draw it
    out: change both together."""
    down = Vector((-0.474, -0.88, -0.034)).normalized()  # the blade's direction (local +Y)
    back = Vector((0.0, 0.0, -1.0))
    z = (back - down * back.dot(down)).normalized()  # the flats face backwards
    x = down.cross(z)
    basis = Matrix((x, down, z)).transposed().to_4x4()
    guard = Vector((0.119, 1.396, -0.172))
    return Matrix.Translation(body_local(guard - down * (SWORD_GUARD - SWORD_GRIP))) @ basis


def blade_width(y):
    """The blade's half width along it: broad and straight, a shallow step where the point section begins."""
    base, tip = SWORD_GUARD - SWORD_GRIP, SWORD_TIP - SWORD_GRIP
    t = (y - base) / (tip - base)
    if t < 0.62:
        return 0.03 - 0.003 * t
    if t < 0.66:
        return 0.0282 + (0.0335 - 0.0282) * (t - 0.62) / 0.04  # the step out to the leaf
    return 0.0335 * (1.0 - ((t - 0.66) / 0.34) ** 1.7)


def sword_shape(shape, hilt_only=False):
    """In the sword's own frame (Unity terms): grip middle at the origin, blade +Y, flats facing ±Z."""
    base, tip = SWORD_GUARD - SWORD_GRIP, SWORD_TIP - SWORD_GRIP
    if not hilt_only:
        # The blade: bevelled to both edges, each edge its own bright strip, a dark fuller down the middle.
        ys = [base + (tip - base) * k / 24 for k in range(25)]
        for side in (1, -1):
            def flat(w, z):
                return lambda y: Vector((side * blade_width(y) * w, y, z))
            ridge = 0.0055
            shape.loft([[Vector((side * blade_width(y) * 0.28, y, ridge)), Vector((side * blade_width(y) * 0.28, y, -ridge)),
                         Vector((side * blade_width(y) * 0.72, y, -ridge * 0.62)), Vector((side * blade_width(y) * 0.72, y, ridge * 0.62))] for y in ys[:-1]],
                       STEEL, cap_tip=Vector((0, tip, 0)))
            shape.loft([[Vector((side * blade_width(y) * 0.72, y, ridge * 0.62)), Vector((side * blade_width(y) * 0.72, y, -ridge * 0.62)),
                         Vector((side * blade_width(y), y, -0.0005)), Vector((side * blade_width(y), y, 0.0005))] for y in ys[:-1]],
                       EDGE, cap_tip=Vector((0, tip, 0)))
        fuller_end = base + (tip - base) * 0.74
        shape.loft([[Vector((0.009 * s, y, z)) for s, z in ((1, 0.0052), (1, -0.0052), (-1, -0.0052), (-1, 0.0052))]
                    for y in (base, (base + fuller_end) / 2, fuller_end)], FULLER, cap_tip=Vector((0, fuller_end + 0.04, 0)))
        shape.loft([[Vector((blade_width(base) * 0.3 * s, base, z)) for s, z in ((1, 0.0056), (1, -0.0056), (-1, -0.0056), (-1, 0.0056))],
                    [Vector((blade_width(base + 0.02) * 0.3 * s, base + 0.02, z)) for s, z in ((1, 0.0056), (1, -0.0056), (-1, -0.0056), (-1, 0.0056))]], STEEL)
    # The guard: a bronze collar with two short down-swept quillons.
    shape.loft([[Vector((0.04 * s, y, 0.017 * z)) for s, z in ((1, 1), (1, -1), (-1, -1), (-1, 1))] for y in (base - 0.012, base + 0.02)], GOLD)
    for s in (1, -1):
        shape.loft([[Vector((x * s, y + o, 0.011 * z)) for o, z in ((0.008, 1), (0.008, -1), (-0.008, -1), (-0.008, 1))]
                    for x, y in ((0.035, base + 0.002), (0.062, base - 0.008), (0.088, base - 0.03))], GOLD)
    # The grip: red leather wound round, in ridges.
    bottom, top = -SWORD_GRIP + 0.03, base - 0.012
    for k in range(11):
        a, b = bottom + (top - bottom) * k / 11, bottom + (top - bottom) * (k + 1) / 11
        shape.cylinder(Vector((0, (a + b) / 2, 0)), 0.0155, (b - a) * 0.86, WRAP, top=0.0145, segs=10)
    shape.cylinder(Vector((0, (bottom + top) / 2, 0)), 0.013, top - bottom, WRAP, segs=8)
    # The pommel: a flattened bronze cap, wider than the grip.
    shape.sphere(Vector((0, -SWORD_GRIP + 0.016, 0)), 0.024, GOLD, squash=(1.5, 0.8, 0.9))


def scabbard_shape(shape):
    """A straight leather sheath the blade's shape, bronze at the mouth and the tip."""
    base, tip = SWORD_GUARD - SWORD_GRIP + 0.012, SWORD_TIP - SWORD_GRIP + 0.012
    def ring(y, n=10):
        w = max(blade_width(min(y, tip - 0.03)), 0.006) + 0.006
        return [Vector((w * math.cos(a), y, 0.012 * math.sin(a))) for a in (math.tau * k / n for k in range(n))]
    ys = [base + (tip - base) * k / 16 for k in range(17)]
    shape.loft([ring(y) for y in ys], SAYA, cap_tip=Vector((0, tip + 0.02, 0)))
    shape.loft([[p * 1.0 + Vector((0, 0, 0)) for p in ring(base - 0.002)], ring(base + 0.04)], GOLD)


def build_sword(root):
    bare = Shape()
    sword_shape(bare)
    bare.build("Weaver_Sword", root)
    # On the back: the sword in its sheath (the whole body), and apart for first person, so it can leave the sheath.
    frame = back_frame()
    hilt = Shape()
    sword_shape(hilt, hilt_only=True)
    hilt.build("Outfit_Sword_Hilt", root, frame)
    sheath = Shape()
    scabbard_shape(sheath)
    sheath.build("Outfit_Sword_Scabbard", root, frame)
    worn = Shape()
    scabbard_shape(worn)
    sword_shape(worn, hilt_only=True)
    worn.build("Avatar_Sword", root, frame)


# ---------------------------------------------------------------- run


def main():
    root = bpy.data.objects.new("Weaver", None)
    bpy.context.scene.collection.objects.link(root)
    build_body(root)
    build_outfit(root)
    build_sword(root)

    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=False, object_types={"EMPTY", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
    base = os.path.splitext(OUT_FBX)[0]
    with open(base + ".materials.json", "w") as f:
        json.dump({"materials": list(MATERIALS.values())}, f, indent=1)
    to_unity_axes = TO_BLENDER.to_3x3().inverted()
    placements = []
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        loc, rot, scale = obj.matrix_world.decompose()
        q = (to_unity_axes @ rot.to_matrix() @ TO_BLENDER.to_3x3()).to_quaternion()
        placements.append({"name": obj.name, "position": list(to_unity_axes @ loc), "rotation": [q.x, q.y, q.z, q.w], "scale": scale.x})
    with open(base + ".placements.json", "w") as f:
        json.dump({"objects": placements}, f, indent=1)
    deps = bpy.context.evaluated_depsgraph_get()
    tris = sum(sum(len(p.vertices) - 2 for p in o.evaluated_get(deps).data.polygons) for o in bpy.data.objects if o.type == "MESH")
    print(f"[Weaver] {len([o for o in bpy.data.objects if o.type == 'MESH'])} objects, ~{tris} triangles after decimation")
    print(f"[Weaver] wrote {OUT_FBX}")


main()
