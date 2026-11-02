"""
Builds ARCANE STRIKE's attic store room in Blender and exports it for Unity.

    blender -b -P Tools/blender/build_attic.py            (from the Unity project folder)
    blender -b -P build_attic.py -- <out.fbx> <out.blend>  (choose the outputs)

Or open Blender → Scripting → Open this file → Run Script.

Writes Assets/_Project/Art/Environment/Attic/Models/Attic_Room.fbx and Tools/blender/Attic_Room.blend
(the .blend stays outside Assets so Unity doesn't try to import it). Then in Unity: open the Attic scene →
Arcane Strike → Apply Attic Model.

Everything is laid out in Unity's coordinates, in metres, with the player at the origin facing +Z, the same
layout as AtticSceneBuilder. The story props (Chest_Body, Chest_Lid, Lantern_Dirty/Clean, Crate_Left/Right,
Book_1..6) each have their pivot where the game's placeholder sits, so Apply Attic Model can drop them in.
Keep those names and pivots if you edit the model by hand. Objects named Grab_* become physical props the
player can pick up and throw. Materials (colour tint, texture set, smoothness) are also written to
Attic_Room.materials.json, which Unity uses to make URP materials with the textures in ../Textures.
"""

import json
import math
import os
import random
import sys
from contextlib import contextmanager

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_FBX = args[0] if len(args) > 0 else os.path.join(PROJECT, "Assets/_Project/Art/Environment/Attic/Models/Attic_Room.fbx")
OUT_BLEND = args[1] if len(args) > 1 else os.path.join(HERE, "Attic_Room.blend")

random.seed(7)
bpy.ops.wm.read_factory_settings(use_empty=True)  # start from an empty scene (before the materials below)

# Unity (x right, y up, z forward) → Blender (x right, y forward, z up). The FBX export below
# (forward -Z, up Y) together with Unity's import turns Blender (x, y, z) into Unity (-x, z, -y);
# this is its inverse, so positions written in Unity terms land in the same place in Unity.
TO_BLENDER = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0))).to_4x4()

# Sized for real furniture (the Poly Haven props): 5 m wide, 6 m deep, 3.4 m to the ridge.
HALF_W, Z_FRONT, Z_BACK = 2.5, -1.8, 4.2
KNEE, RIDGE = 1.2, 3.4
# The attic is the top floor of an old house. Its front gable is an inside wall with the castle gate; in front of
# it the roof space continues as a loft over the hall, where the stairs come up. Heights are from the attic floor.
Z_HOUSE = -7.4             # front of the house (the loft and the hall start here)
GROUND = -3.2              # ground floor (the hall)
TERRAIN = GROUND - 0.45    # the yard outside
WALL_OUT = HALF_W + 0.12   # outside face of the house walls
STAIR_X0, STAIR_X1 = 0.2, 1.2                # the stairs climb towards +Z ...
STAIR_Z0, STAIR_Z1 = -5.8, -2.6              # ... from the hall floor to the loft floor
OPENING = (0.1, 1.3, -4.6, -2.6)             # hole in the loft floor over the stairs (x0, x1, z0, z1)
GATE_HALF, GATE_HEIGHT = 0.73, 2.12          # doorway behind the castle gate (scaled to 0.75)
SLOPE = (RIDGE - KNEE) / HALF_W
ROOF_ANGLE = math.atan2(RIDGE - KNEE, HALF_W)


def roof_y(x):
    """Height of the roof's inner surface above x."""
    return RIDGE - SLOPE * abs(x)


# ---------------------------------------------------------------- materials

# CC0 PBR textures from ambientCG (ambientcg.com), in Assets/_Project/Art/Environment/Attic/Textures/<set>/.
TEXTURES = os.path.join(PROJECT, "Assets/_Project/Art/Environment/Attic/Textures")
MATERIALS = {}  # name → settings, written next to the FBX for Unity's Apply Attic Model


def material(name, color, roughness=0.8, metallic=0.0, alpha=1.0, emission=None, tex=None, tile=1.0, emissive=False):
    """`color` tints the texture set `tex` (or is the plain colour); `tile` is metres per texture repeat.
    `emissive` keeps glow switched on in Unity even while it is black, so a script can light it up later."""
    full = "M_Attic_" + name
    m = bpy.data.materials.new(full)
    if bpy.app.version < (5, 0, 0):
        m.use_nodes = True  # always on from Blender 5
    nodes, links = m.node_tree.nodes, m.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 1.0
    if tex is not None:  # texture × tint, plus the normal map, so the .blend previews like Unity
        base = nodes.new("ShaderNodeTexImage")
        base.image = bpy.data.images.load(os.path.join(TEXTURES, tex, f"{tex}_Color.jpg"), check_existing=True)
        tint = nodes.new("ShaderNodeMix")
        tint.data_type, tint.blend_type = "RGBA", "MULTIPLY"
        tint.inputs[0].default_value = 1.0
        tint.inputs[7].default_value = (*color, 1.0)
        links.new(base.outputs["Color"], tint.inputs[6])
        links.new(tint.outputs[2], bsdf.inputs["Base Color"])
        normal = nodes.new("ShaderNodeTexImage")
        normal.image = bpy.data.images.load(os.path.join(TEXTURES, tex, f"{tex}_Normal.jpg"), check_existing=True)
        normal.image.colorspace_settings.name = "Non-Color"
        bump = nodes.new("ShaderNodeNormalMap")
        links.new(normal.outputs["Color"], bump.inputs["Color"])
        links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    m.diffuse_color = (*color, alpha)
    m.metallic = metallic
    m.roughness = roughness
    MATERIALS[full] = {"name": full, "color": list(color), "smoothness": 1.0 - roughness, "metallic": metallic,
                       "alpha": alpha, "emission": list(emission) if emission else [0, 0, 0], "texture": tex or "", "tile": tile,
                       "emissive": emissive or emission is not None}
    return m


FLOOR = [material("FloorA", (0.85, 0.80, 0.75), tex="Wood049", tile=1.2),
         material("FloorB", (0.95, 0.88, 0.80), tex="Wood049", tile=1.2),
         material("FloorC", (0.70, 0.65, 0.60), tex="Wood049", tile=1.2)]
WALL_BOARDS = [material("WallBoardA", (0.90, 0.85, 0.80), tex="Wood049", tile=1.2),
               material("WallBoardB", (0.75, 0.70, 0.65), tex="Wood049", tile=1.2)]
PLASTER = material("Plaster", (0.85, 0.80, 0.70), 0.95, tex="Plaster007", tile=1.5)
ROOF = [material("RoofBoardA", (1.0, 1.0, 1.0), tex="Wood060", tile=1.2), material("RoofBoardB", (0.85, 0.85, 0.85), tex="Wood060", tile=1.2)]
BEAM = material("Beam", (0.80, 0.75, 0.70), tex="Wood060", tile=1.0)
WOOD = material("Wood", (0.85, 0.80, 0.75), tex="Wood049")
WOOD_DARK = material("WoodDark", (1.0, 1.0, 1.0), tex="Wood060")
WOOD_LIGHT = material("WoodLight", (1.0, 0.95, 0.90), tex="Wood049")
IRON = material("Iron", (0.35, 0.33, 0.30), 0.55, 0.9, tex="Metal041B", tile=0.5)
BRASS = material("Brass", (1.0, 0.75, 0.35), 0.3, 1.0, tex="Metal063", tile=0.5)
DIRTY_METAL = material("DirtyMetal", (0.50, 0.45, 0.38), 0.85, 0.4, tex="Metal041B", tile=0.4)
GLASS = material("Glass", (1.00, 0.92, 0.70), 0.05, 0.0, alpha=0.35)
DIRTY_GLASS = material("DirtyGlass", (0.45, 0.42, 0.35), 0.9)
WAX = material("Wax", (0.92, 0.88, 0.76), 0.6)
PAGES = material("Pages", (1.0, 0.97, 0.90), 0.9, tex="Paper006", tile=0.3)
BOOKS = [material("BookRed", (0.75, 0.20, 0.15), 0.7, tex="Paper006", tile=0.3),
         material("BookGreen", (0.25, 0.50, 0.30), 0.7, tex="Paper006", tile=0.3),
         material("BookBlue", (0.25, 0.32, 0.65), 0.7, tex="Paper006", tile=0.3)]
LEATHER = material("Leather", (1.0, 1.0, 1.0), 0.7, tex="Leather037", tile=0.5)
CLOTH = material("Cloth", (1.0, 1.0, 1.0), 1.0, tex="Fabric085", tile=0.5)
CANVAS = material("Painting", (0.25, 0.32, 0.22), 0.9, tex="Paper006", tile=0.5)
JARS = [material("JarGreen", (0.25, 0.45, 0.30), 0.2, alpha=0.6), material("JarAmber", (0.60, 0.35, 0.10), 0.2, alpha=0.6)]
CORK = material("Cork", (0.55, 0.42, 0.28))
STRAW = material("Straw", (0.70, 0.58, 0.32), 1.0)
COBWEB = material("Cobweb", (0.85, 0.85, 0.82), 1.0, alpha=0.4)
SHADOW = material("Shadow", (0.03, 0.025, 0.02), 1.0)
SKY = material("WindowLight", (0.55, 0.65, 0.85), 0.05, alpha=0.18, emission=(0.04, 0.06, 0.1))  # moonlit glass: lets the moon in
TIN = material("Tin", (0.45, 0.45, 0.47), 0.4, 0.8, tex="Metal063", tile=0.3)
BOTTLE = material("BottleGlass", (0.20, 0.35, 0.25), 0.1, alpha=0.7)
SHEET = material("DustSheet", (0.82, 0.80, 0.74), 1.0, tex="Fabric061", tile=0.3)
# The house and the yard around it.
SIDING = material("Siding", (0.85, 0.82, 0.78), 0.9, tex="WoodSiding008", tile=1.6)
ROOF_TILES = material("RoofTiles", (1.0, 1.0, 1.0), 0.8, tex="RoofTerracotta", tile=1.4)
# Half-timbering, and the stone and growth of the old yard.
HOUSE_PLASTER = material("HousePlaster", (0.92, 0.76, 0.54), 0.95, tex="Plaster007", tile=1.5)
TIMBER = material("Timber", (0.62, 0.48, 0.38), 0.85, tex="Wood060", tile=1.0)
YARD_STONE = material("YardStone", (0.66, 0.61, 0.53), 0.95, tex="Plaster007", tile=0.7)  # pale: the rock texture is too dark
MOSS_STONE = material("MossStone", (0.38, 0.47, 0.24), 1.0, tex="Plaster007", tile=0.7)
GRAVE_STONE = material("GraveStone", (0.6, 0.6, 0.58), 0.9, tex="Plaster007", tile=0.5)
MOSS = material("Moss", (0.2, 0.34, 0.1), 1.0)
BARK = material("DeadBark", (0.13, 0.115, 0.1), 1.0)
STONE = material("StoneWall", (0.8, 0.8, 0.8), 0.9, tex="Bricks113A", tile=1.2)
GRASS = material("Grass", (0.55, 0.6, 0.55), 1.0, tex="Grass004", tile=2.0)
PATH = material("PathStones", (0.75, 0.75, 0.75), 0.9, tex="PavingStones138", tile=1.2)
NIGHT_GLASS = material("NightGlass", (0.10, 0.13, 0.18), 0.05, 0.0, alpha=0.45)
LAMP_GLASS = material("LampGlass", (1.0, 0.75, 0.4), 0.2, emission=(2.4, 1.5, 0.6))
RUNE = material("RuneGlyph", (0.20, 0.15, 0.09), 0.4, 0.7, tex="Metal063", tile=0.2, emissive=True)  # RuneGlow lights it
SHARP = material("SharpMetal", (0.95, 0.95, 1.0), 0.15, 1.0, tex="Metal063", tile=0.2)
FLAME = material("Flame", (1.0, 0.75, 0.35), 0.5, emission=(2.0, 1.2, 0.45))
RAG = material("Rag", (0.80, 0.72, 0.58), 1.0, tex="Fabric061", tile=0.12)
STONE = material("PortalStone", (0.80, 0.80, 0.82), 0.9, tex="Rock058", tile=0.6)
# The old things (build_old_things): nobody has touched these for decades.
GRIMY_WOOD = material("GrimyWood", (1.0, 1.0, 1.0), 0.92, tex="WoodGrimy", tile=0.7)
GRIMY_PALE = material("GrimyPaleWood", (1.45, 1.3, 1.1), 0.95, tex="WoodGrimy", tile=0.5)
GRIMY_CASE = material("GrimyCase", (0.72, 0.6, 0.52), 0.7, tex="WoodGrimy", tile=0.9)
CARDBOARD = material("Cardboard", (1.0, 1.0, 1.0), 0.97, tex="CardboardOld", tile=0.8)
BURLAP = material("Burlap", (1.0, 1.0, 1.0), 1.0, tex="BurlapOld", tile=0.45)
TARNISH = material("TarnishedBrass", (1.0, 1.0, 1.0), 0.62, 0.55, tex="BrassTarnished", tile=0.45)
SCREEN = material("DustyScreen", (1.0, 1.0, 1.0), 0.55, tex="ScreenDusty", tile=0.5)
OLD_RECORD = material("OldRecord", (0.05, 0.05, 0.055), 0.6)
OLD_PAPER = material("OldPaper", (0.62, 0.54, 0.4), 0.95, tex="Paper006", tile=0.3)
TWINE = material("Twine", (0.26, 0.2, 0.12), 1.0)
OLD_BOOKS = [material("OldBook" + n, c, 0.85, tex="Paper006", tile=0.3) for n, c in (
    ("Oxblood", (0.24, 0.08, 0.06)), ("Moss", (0.12, 0.16, 0.1)), ("Ink", (0.08, 0.1, 0.15)),
    ("Umber", (0.22, 0.14, 0.08)), ("Faded", (0.4, 0.33, 0.24)))]
PORTAL_RUNE = material("PortalRune", (0.30, 0.89, 1.0), 0.3, emission=(0.6, 1.8, 2.0))  # Volt cyan


# ---------------------------------------------------------------- mesh building


def trs(pos=(0, 0, 0), rot=(0, 0, 0), size=(1, 1, 1)):
    """Position, rotation (degrees about x, y, z) and size, in Unity terms."""
    r = Euler(tuple(math.radians(a) for a in rot), "XYZ").to_matrix().to_4x4()
    return Matrix.Translation(pos) @ r @ Matrix.Diagonal((*size, 1.0))


_SCRATCH_MESH = bpy.data.meshes.new("_scratch")


class Part:
    """One Unity GameObject: any number of shapes in one mesh, with its pivot at `pivot` (Unity coordinates)."""

    def __init__(self, name, pivot=(0, 0, 0)):
        self.name = name
        self.pivot = Vector(pivot)
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.materials = []

    # Every shape is made at the origin, then moved into place in Unity coordinates. New elements are
    # the untagged ones.
    def _begin(self):
        for v in self.bm.verts:
            v.tag = True

    def _end(self, local, mat, smooth=False, flat=(), world_uv=False):
        verts = [v for v in self.bm.verts if not v.tag]
        faces = {f for v in verts for f in v.link_faces}
        self._project_uvs(verts, faces, MATERIALS[mat.name]["tile"], local if world_uv else None)
        world = TO_BLENDER @ Matrix.Translation(-self.pivot) @ local
        bmesh.ops.transform(self.bm, matrix=world, verts=verts)
        if mat not in self.materials:
            self.materials.append(mat)
        for f in faces:
            f.material_index = self.materials.index(mat)
            f.smooth = smooth and len(f.verts) == 4 and f not in flat
        for v in verts:
            v.tag = True

    def _project_uvs(self, verts, faces, tile, world=None):
        """Box projection in metres (the shape's own frame, before it is turned), so every surface gets the
        same texture scale. The grain runs along the shape's longest side; each shape gets its own offset.
        With `world` (the shape's placement), it projects in room space instead, so neighbouring pieces of a
        wall or floor line up like one surface."""
        co = {v: (world @ v.co if world is not None else v.co) for v in verts}
        lo = [min(co[v][k] for v in verts) for k in range(3)]
        hi = [max(co[v][k] for v in verts) for k in range(3)]
        extent = [hi[k] - lo[k] for k in range(3)]
        offset = (0.0, 0.0) if world is not None else (random.random(), random.random())
        for f in faces:
            f.normal_update()
            n = [abs(c) for c in f.normal]
            axis = n.index(max(n))
            a, b = [k for k in range(3) if k != axis]
            u, v = (a, b) if extent[a] >= extent[b] else (b, a)
            for loop in f.loops:
                c = co[loop.vert]
                loop[self.uv].uv = (c[u] / tile + offset[0], c[v] / tile + offset[1])

    def box(self, pos, size, mat, rot=(0, 0, 0), bevel=0.005, world_uv=False):
        """A box with its edges slightly rounded off (real edges catch the light)."""
        self._begin()
        # Bevel on its own mesh first (bevel replaces vertices, which would lose track of the new ones).
        temp = bmesh.new()
        bmesh.ops.create_cube(temp, size=1.0)
        bmesh.ops.scale(temp, vec=size, verts=temp.verts[:])
        bevel = min(bevel, 0.25 * min(size))
        if bevel >= 0.001:
            bmesh.ops.bevel(temp, geom=temp.verts[:] + temp.edges[:], offset=bevel, segments=1, affect="EDGES", clamp_overlap=True)
        temp.to_mesh(_SCRATCH_MESH)
        temp.free()
        self.bm.from_mesh(_SCRATCH_MESH)
        self._end(trs(pos, rot), mat, smooth=False, world_uv=world_uv)

    def stroke(self, a, b, normal, mat, width=0.0018, depth=0.0015):
        """A thin raised line from a to b (Unity coordinates) on a surface facing `normal`: carving."""
        a, b, n = Vector(a), Vector(b), Vector(normal).normalized()
        x = b - a
        length = x.length
        x.normalize()
        y = n.cross(x)
        basis = Matrix((x, y, n)).transposed().to_4x4()
        self._begin()
        bmesh.ops.create_cube(self.bm, size=1.0)
        local = Matrix.Translation((a + b) / 2 + n * depth / 2) @ basis @ Matrix.Diagonal((length + width, width, depth, 1.0))
        self._end(local, mat, smooth=False)

    def sheet(self, pos, size, mat, folds, rot=(0, 0, 0), thickness=0.003, segments=(12, 10)):
        """A cloth: a thin sheet lying flat, crumpled by folds(u, v) → height (u, v from -0.5 to 0.5)."""
        self._begin()
        temp = bmesh.new()
        bmesh.ops.create_grid(temp, x_segments=segments[0], y_segments=segments[1], size=0.5)
        for v in temp.verts:
            u, w = v.co.x, v.co.y
            v.co = Vector((u * size[0], folds(u, w), w * size[1]))
        bmesh.ops.solidify(temp, geom=temp.faces[:], thickness=thickness)
        temp.to_mesh(_SCRATCH_MESH)
        temp.free()
        self.bm.from_mesh(_SCRATCH_MESH)
        self._end(trs(pos, rot), mat, smooth=True)

    def cylinder(self, pos, radius, height, mat, axis="y", rot=(0, 0, 0), top_radius=None, segments=16):
        """A cylinder (or cone) standing along `axis`."""
        self._begin()
        bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segments, radius1=radius,
                              radius2=radius if top_radius is None else top_radius, depth=height, calc_uvs=True)
        to_axis = {"y": Euler((math.radians(-90), 0, 0)), "x": Euler((0, math.radians(90), 0)), "z": Euler((0, 0, 0))}[axis]
        self._end(trs(pos, rot) @ to_axis.to_matrix().to_4x4(), mat, smooth=True)

    def dome(self, pos, radius, length, mat, segments=16):
        """Half a cylinder lying along x with its round side up (a barrel lid), flat side at pos.y."""
        self._begin()
        ends = []
        for x in (-length / 2, length / 2):
            ends.append([self.bm.verts.new((x, radius * math.sin(a), radius * math.cos(a)))
                         for a in (math.pi * k / segments for k in range(segments + 1))])
        left, right = ends
        flat = {self.bm.faces.new(left), self.bm.faces.new(list(reversed(right))),
                self.bm.faces.new((left[0], right[0], right[-1], left[-1]))}
        for k in range(segments):
            self.bm.faces.new((left[k], left[k + 1], right[k + 1], right[k]))
        self._end(trs(pos), mat, smooth=True, flat=flat)

    def torus(self, pos, major, minor, mat, rot=(0, 0, 0), segments=20, sides=8):
        """A ring lying in the x-y plane (its hole looks along z), then rotated."""
        self._begin()
        rings = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            ring = []
            for j in range(sides):
                b = 2 * math.pi * j / sides
                r = major + minor * math.cos(b)
                ring.append(self.bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
            rings.append(ring)
        for i in range(segments):
            for j in range(sides):
                a, b = rings[i], rings[(i + 1) % segments]
                self.bm.faces.new((a[j], b[j], b[(j + 1) % sides], a[(j + 1) % sides]))
        self._end(trs(pos, rot), mat, smooth=True)

    def prism(self, outline, z0, z1, mat):
        """A flat slab: `outline` is a list of (x, y) points, extruded from z0 to z1."""
        self._begin()
        front = [self.bm.verts.new((x, y, z0)) for x, y in outline]
        back = [self.bm.verts.new((x, y, z1)) for x, y in outline]
        self.bm.faces.new(front)
        self.bm.faces.new(list(reversed(back)))
        n = len(outline)
        for i in range(n):
            self.bm.faces.new((front[i], front[(i + 1) % n], back[(i + 1) % n], back[i]))
        self._end(Matrix.Identity(4), mat, smooth=False, world_uv=True)

    def turned(self, a, b, profile, mat, segments=10):
        """A turned shape from a to b: `profile` is (0..1 along, radius) pairs. It may double back on itself, so a horn
        can have an inside and an outside."""
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized()
        x = axis.orthogonal().normalized()
        y = axis.cross(x)
        self._begin()
        rings = []
        for k, r in profile:
            c = a.lerp(b, k)
            rings.append([self.bm.verts.new(c + (x * math.cos(i * math.tau / segments) + y * math.sin(i * math.tau / segments)) * r)
                          for i in range(segments)])
        self.bm.faces.new(rings[0])
        self.bm.faces.new(list(reversed(rings[-1])))
        for p, q in zip(rings, rings[1:]):
            for i in range(segments):
                self.bm.faces.new((p[i], p[(i + 1) % segments], q[(i + 1) % segments], q[i]))
        self._end(Matrix.Identity(4), mat, smooth=True)

    def slab(self, outline, y0, y1, mat):
        """A flat level slab: `outline` is a list of (x, z) points, from height y0 to y1."""
        self._begin()
        low = [self.bm.verts.new((x, y0, z)) for x, z in outline]
        high = [self.bm.verts.new((x, y1, z)) for x, z in outline]
        self.bm.faces.new(low)
        self.bm.faces.new(list(reversed(high)))
        n = len(outline)
        for i in range(n):
            self.bm.faces.new((low[i], low[(i + 1) % n], high[(i + 1) % n], high[i]))
        self._end(Matrix.Identity(4), mat, smooth=False)

    def lump(self, shape, mat, rings=16, sides=20):
        """A soft closed bag: shape(k, a) → a point, for k 0..1 from bottom to top and a the angle round."""
        self._begin()
        loops = [[self.bm.verts.new(shape(k / rings, i * math.tau / sides)) for i in range(sides)] for k in range(rings + 1)]
        self.bm.faces.new(loops[0])
        self.bm.faces.new(list(reversed(loops[-1])))
        for p, q in zip(loops, loops[1:]):
            for i in range(sides):
                self.bm.faces.new((p[i], p[(i + 1) % sides], q[(i + 1) % sides], q[i]))
        self._end(Matrix.Identity(4), mat, smooth=True)

    def triangle(self, a, b, c, mat, thickness=0.003):
        """A thin triangular sheet (cobwebs)."""
        self._begin()
        n = (Vector(b) - Vector(a)).cross(Vector(c) - Vector(a)).normalized() * thickness * 0.5
        f = [self.bm.verts.new(Vector(p) + n) for p in (a, b, c)]
        k = [self.bm.verts.new(Vector(p) - n) for p in (a, b, c)]
        self.bm.faces.new(f)
        self.bm.faces.new(list(reversed(k)))
        for i in range(3):
            self.bm.faces.new((f[i], f[(i + 1) % 3], k[(i + 1) % 3], k[i]))
        self._end(Matrix.Identity(4), mat, smooth=False)

    def build(self, parent=None):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])  # TO_BLENDER mirrors, so fix the winding
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for m in self.materials:
            mesh.materials.append(m)
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.location = (TO_BLENDER @ self.pivot.to_4d()).to_3d()
        if parent is not None:
            obj.parent = parent
        return obj


def empty(name, parent=None):
    obj = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    return obj


# ---------------------------------------------------------------- the room


def plank_runs(x, z0, z1, hole):
    """Pieces of a board running along z at x, minus the stair hole."""
    hx0, hx1, hz0, hz1 = hole
    if hx0 < x < hx1:
        return [(z0, hz0), (hz1, z1)]
    return [(z0, z1)]


def build_room(root):
    room = root  # everything directly under the root: the FBX exporter's "Apply Transform" gets deeper levels wrong

    floor = Part("Room_Floor")
    x = -HALF_W
    while x < HALF_W - 0.01:
        width = 0.19
        top = random.uniform(-0.004, 0.003)
        for a, b in plank_runs(x + 0.1, Z_HOUSE, Z_BACK, OPENING):
            split = random.uniform(a + 0.6, b - 0.6) if b - a > 1.5 else None
            for z0, z1 in (((a, split), (split + 0.005, b)) if split else ((a, b),)):
                floor.box((x + 0.1, top - 0.015, (z0 + z1) / 2), (width, 0.03, z1 - z0), random.choice(FLOOR))
        x += 0.2
    hx0, hx1, hz0, hz1 = OPENING  # under the boards, closing the gaps (and the hall's ceiling from below)
    for (x0, x1, z0, z1) in ((-HALF_W, hx0, Z_HOUSE, Z_BACK), (hx1, HALF_W, Z_HOUSE, Z_BACK), (hx0, hx1, Z_HOUSE, hz0), (hx0, hx1, hz1, Z_BACK)):
        floor.box(((x0 + x1) / 2, -0.05, (z0 + z1) / 2), (x1 - x0, 0.03, z1 - z0), SHADOW, bevel=0)
    floor.build(room)

    walls = Part("Room_Walls")
    # Knee walls of vertical boards, along the attic and the loft.
    for side in (-1, 1):
        z = Z_HOUSE
        i = 0
        while z < Z_BACK - 0.01:
            walls.box((side * (HALF_W + 0.03), KNEE / 2, z + 0.1), (0.06, KNEE, 0.195), WALL_BOARDS[i % 2])
            z += 0.2
            i += 1
        walls.box((side * (HALF_W - 0.01), 0.06, (Z_HOUSE + Z_BACK) / 2), (0.03, 0.12, Z_BACK - Z_HOUSE), BEAM)  # skirting
    # The attic's front wall, with the doorway behind the castle gate.
    zf0, zf1 = Z_FRONT - 0.08, Z_FRONT
    gx, gy = GATE_HALF, GATE_HEIGHT
    walls.prism([(-HALF_W, 0), (-gx, 0), (-gx, roof_y(gx)), (-HALF_W, KNEE)], zf0, zf1, PLASTER)
    walls.prism([(gx, 0), (HALF_W, 0), (HALF_W, KNEE), (gx, roof_y(gx))], zf0, zf1, PLASTER)
    walls.prism([(-gx, gy), (gx, gy), (gx, roof_y(gx)), (0, RIDGE), (-gx, roof_y(gx))], zf0, zf1, PLASTER)
    # The back wall, with the window the moon shines through, and the loft's front wall with a small one.
    wx, wy0, wy1 = 0.3, 2.5, 3.05  # above the bookshelf
    gable_with_window(walls, Z_BACK, Z_BACK + 0.08, wx, wy0, wy1, PLASTER)
    lw, ly0, ly1 = 0.25, 1.95, 2.45
    gable_with_window(walls, Z_HOUSE - 0.08, Z_HOUSE, lw, ly0, ly1, PLASTER)
    for z in (Z_FRONT + 0.03, Z_BACK - 0.03):
        for px in (-1.0, 1.0):
            walls.box((px, roof_y(px) / 2, z), (0.1, roof_y(px), 0.06), BEAM)
    walls.box((0, KNEE, Z_BACK - 0.03), (2 * HALF_W, 0.1, 0.06), BEAM)
    for side in (-1, 1):  # on the front wall the cross beam stops at the doorway
        walls.box((side * (HALF_W + gx) / 2, KNEE, Z_FRONT + 0.03), (HALF_W - gx, 0.1, 0.06), BEAM)
    walls.build(room)

    build_window(room, "Room_Window", wx, wy0, wy1, Z_BACK, inward=-1)
    build_window(room, "House_LoftWindow", lw, ly0, ly1, Z_HOUSE - 0.08, inward=1)

    roof = Part("Room_Roof")
    length = Z_BACK - Z_HOUSE + 0.16
    mid_z = (Z_HOUSE + Z_BACK) / 2
    slope_len = math.hypot(HALF_W, RIDGE - KNEE)
    boards = int(slope_len / 0.2)
    for side in (-1, 1):
        along = Vector((-side * math.cos(ROOF_ANGLE), math.sin(ROOF_ANGLE), 0))  # from the eave up to the ridge
        out = Vector((side * math.sin(ROOF_ANGLE), math.cos(ROOF_ANGLE), 0))
        eave = Vector((side * HALF_W, KNEE, 0))
        angle = math.degrees(ROOF_ANGLE) * -side
        for i in range(boards + 1):
            centre = eave + along * (0.1 + i * 0.2) + out * 0.012
            roof.box((centre.x, centre.y, mid_z), (0.195, 0.024, length), ROOF[i % 2], rot=(0, 0, angle))
        roof.box(((eave + along * slope_len / 2 + out * 0.04).x, (eave + along * slope_len / 2 + out * 0.04).y, mid_z),
                 (slope_len + 0.1, 0.02, length), SHADOW, rot=(0, 0, angle))
    roof.build(room)

    beams = Part("Room_Beams")
    z = Z_HOUSE + 0.4
    while z < Z_BACK - 0.2:
        for side in (-1, 1):
            along = Vector((-side * math.cos(ROOF_ANGLE), math.sin(ROOF_ANGLE), 0))
            out = Vector((side * math.sin(ROOF_ANGLE), math.cos(ROOF_ANGLE), 0))
            mid = Vector((side * HALF_W, KNEE, 0)) + along * (slope_len / 2) - out * 0.06
            beams.box((mid.x, mid.y, z), (slope_len, 0.12, 0.08), BEAM, rot=(0, 0, math.degrees(ROOF_ANGLE) * -side))
        tie_y = KNEE + (RIDGE - KNEE) * 0.62
        tie_half = (RIDGE - tie_y) / SLOPE + 0.05
        beams.box((0, tie_y, z), (2 * tie_half, 0.1, 0.07), BEAM)
        z += 0.75
    beams.box((0, RIDGE - 0.08, mid_z), (0.1, 0.16, Z_BACK - Z_HOUSE), BEAM)
    for side in (-1, 1):
        beams.box((side * (HALF_W - 0.05), KNEE - 0.05, mid_z), (0.1, 0.1, Z_BACK - Z_HOUSE), BEAM)
    beams.build(room)

    build_props(room)
    build_cobwebs(room)


def gable_with_window(part, z0, z1, wx, wy0, wy1, mat, half=HALF_W, base=0.0, lift=0.0):
    """A gable wall (house width, floor to roof) with a window hole, as four slabs.
    `lift` raises the roof line (the outside of the roof is higher than the inside)."""
    ry = lambda x: roof_y(x) + lift
    part.prism([(-half, base), (-wx, base), (-wx, ry(wx)), (-half, ry(half))], z0, z1, mat)
    part.prism([(wx, base), (half, base), (half, ry(half)), (wx, ry(wx))], z0, z1, mat)
    part.prism([(-wx, base), (wx, base), (wx, wy0), (-wx, wy0)], z0, z1, mat)
    part.prism([(-wx, wy1), (wx, wy1), (wx, ry(wx)), (0, RIDGE + lift), (-wx, ry(wx))], z0, z1, mat)


def build_window(parent, name, wx, wy0, wy1, z, inward):
    """A small window in a gable at z: frame, cross bars, moonlit glass and a sill on the room side."""
    window = Part(name, (0, (wy0 + wy1) / 2, z))
    cz = z + 0.04
    for y in (wy0, wy1):
        window.box((0, y, cz), (2 * wx + 0.1, 0.05, 0.12), WOOD_DARK)
    for x in (-wx, wx):
        window.box((x, (wy0 + wy1) / 2, cz), (0.05, wy1 - wy0 + 0.1, 0.12), WOOD_DARK)
    window.box((0, (wy0 + wy1) / 2, cz), (0.03, wy1 - wy0, 0.04), WOOD_DARK)
    window.box((0, (wy0 + wy1) / 2, cz), (2 * wx, 0.03, 0.04), WOOD_DARK)
    window.box((0, (wy0 + wy1) / 2, cz + 0.01), (2 * wx, wy1 - wy0, 0.01), SKY)
    window.box((0, wy0 - 0.04, z + 0.04 + inward * 0.09), (2 * wx + 0.16, 0.03, 0.12), WOOD_DARK)  # sill
    window.build(parent)


def build_props(room):
    # Shelf against the back wall, left of the window, with jars, boxes and scrolls.
    sx, sz = -1.45, Z_BACK - 0.18
    shelf = Part("Prop_Shelf", (sx, 0, sz))
    for dx in (-0.42, 0.42):
        for dz in (-0.12, 0.12):
            shelf.box((sx + dx, 0.6, sz + dz), (0.04, 1.2, 0.04), WOOD_DARK)
    for y in (0.08, 0.5, 0.9, 1.2):
        shelf.box((sx, y, sz), (0.9, 0.025, 0.3), WOOD)
    shelf.box((sx, 0.6, sz + 0.14), (0.88, 1.2, 0.01), WOOD_DARK)
    shelf.build(room)

    # Loose things on the shelf: separate objects the player can pick up, knock over and throw (Grab_*).
    items = [(-0.3, 0.5, "jar"), (-0.15, 0.5, "jar"), (0.1, 0.5, "box"), (0.3, 0.5, "scroll"),
             (-0.28, 0.9, "box"), (0.0, 0.9, "jar"), (0.22, 0.9, "books"), (-0.25, 0.08, "box"), (0.2, 0.08, "jar")]
    for dx, y, kind in items:
        x, base = sx + dx, y + 0.0125 + 0.002  # resting just above the board
        if kind == "jar":
            h, r = random.uniform(0.1, 0.17), random.uniform(0.035, 0.055)
            build_jar(room, (x, base, sz), h, r)
        elif kind == "box":
            w, h = random.uniform(0.12, 0.2), random.uniform(0.07, 0.14)
            g = grab("Box", (x, base + h / 2, sz))
            g.box((x, base + h / 2, sz), (w, h, 0.18), random.choice((WOOD_LIGHT, LEATHER)), rot=(0, random.uniform(-12, 12), 0))
            g.box((x, base + h + 0.001, sz), (w * 0.3, 0.003, 0.185), BRASS, rot=(0, 0, 0), bevel=0)  # clasp strip
            g.build(room)
        elif kind == "scroll":
            for k in range(3):
                c = (x, base + 0.022 + k * 0.043, sz - 0.02 + (k % 2) * 0.03)
                g = grab("Scroll", c)
                g.cylinder(c, 0.022, 0.24, PAGES, axis="x", segments=10)
                g.cylinder(c, 0.0235, 0.02, CLOTH, axis="x", segments=10)  # ribbon
                g.build(room)
        elif kind == "books":
            for k in range(4):
                h = 0.2 - (k % 2) * 0.03
                c = (x - 0.06 + k * 0.035, base + h / 2, sz)
                g = grab("Book", c)
                g.box(c, (0.03, h, 0.15), BOOKS[k % 3])
                g.box((c[0], c[1], c[2] + 0.002), (0.024, h - 0.008, 0.148), PAGES, bevel=0)
                g.build(room)

    # Real furniture and clutter (CC0 models from Poly Haven, made game-ready by prepare_prop.py).
    # The way in from the loft: the castle gate, its two doors on their hinges (AtticIntro swings them open).
    place_prop(room, "large_castle_door", (0, 0, Z_FRONT + 0.115), scale=0.75,
               names={"large_castle_door_Frame": "Gate_Frame", "large_castle_door_Left": "Gate_Left",
                      "large_castle_door_Right": "Gate_Right"})
    shelf_z = Z_BACK - 0.32
    place_prop(room, "wooden_bookshelf_worn", (0, 0, shelf_z), yaw=180)
    build_shelf_books(room, shelf_z)
    place_prop(room, "brass_pot_01", (0.42, 0.965, shelf_z), yaw=200)
    place_prop(room, "standing_picture_frame_01", (-0.4, 1.285, shelf_z - 0.05), yaw=10)
    place_prop(room, "vintage_oil_lamp", (-0.48, 2.065, shelf_z), yaw=30)
    place_prop(room, "fancy_picture_frame_01", (-1.45, 1.45, Z_BACK - 0.012), yaw=180)  # over the little shelf

    dresser_x = HALF_W - 0.03 - 0.505
    place_prop(room, "vintage_wooden_drawer_01", (dresser_x, 0, 1.35), yaw=-90)
    place_prop(room, "ceramic_vase_01", (dresser_x + 0.1, 1.09, 1.85), yaw=40,  # years of dust on it
               look={"color": [0.62, 0.56, 0.46], "smoothnessScale": 0.25})
    place_prop(room, "antique_katana_01", (dresser_x - 0.05, 1.09 + 0.046, 1.95), yaw=0, tilt=90)  # lying on the dresser

    barrel_x, barrel_z = 1.45, Z_BACK - 0.45
    place_prop(room, "wine_barrel_01", (barrel_x, 0, barrel_z), yaw=25)
    place_prop(room, "wooden_crate_01", (HALF_W - 0.3, 0, 2.85), yaw=90)
    place_prop(room, "brass_blowtorch", (HALF_W - 0.3, 0.35, 2.55), yaw=30)
    place_prop(room, "chinese_armchair", (-1.6, 0, 0.3), yaw=65)  # half turned towards the chest
    place_prop(room, "painted_wooden_stool", (-1.0, 0, -0.55), yaw=15)
    place_prop(room, "vintage_suitcase", (-HALF_W + 0.15, 0, -1.0), yaw=90)  # stood against the wall
    place_prop(room, "vintage_suitcase_02", (-1.85, 0.12, -1.2), yaw=110, tilt=90)  # another, lying on its back
    place_prop(room, "kite_shield", (-HALF_W + 0.2, 0, 0.95), yaw=90, tilt=12)  # leaning on the wall
    place_prop(room, "wooden_broom", (HALF_W - 0.25, 0, Z_FRONT + 0.25), yaw=-135, tilt=8)  # in the corner
    build_dust_sheet(room, (-1.55, 0, 2.55))
    place_prop(room, "wooden_bucket_01", (1.95, 0, -1.25), yaw=-30)

    # A candle in a brass holder on the barrel (Unity lights its flame; it can be picked up).
    top = 0.872  # the barrel lid
    cx, cz = barrel_x + 0.1, barrel_z - 0.08
    candle = grab("Candle", (cx, top + 0.032, cz))
    candle.cylinder((cx, top + 0.01, cz), 0.05, 0.02, BRASS)
    candle.torus((cx + 0.058, top + 0.017, cz), 0.018, 0.004, BRASS, rot=(90, 0, 0))  # finger ring
    candle.cylinder((cx, top + 0.055, cz), 0.018, 0.07, WAX, segments=10)
    candle.cylinder((cx + 0.003, top + 0.032, cz + 0.018), 0.006, 0.03, WAX, segments=6)  # drip
    candle.cylinder((cx, top + 0.096, cz), 0.0015, 0.012, SHADOW, segments=4)  # wick
    candle.build(room)

    # Things lying about within reach of the chest.
    build_bottle(room, (-0.55, 0.002, 0.45))
    mug = grab("Mug", (0.8, 0.4 + 0.045, 1.48))
    mug.cylinder((0.8, 0.445, 1.48), 0.038, 0.085, TIN, segments=16)
    mug.torus((0.845, 0.445, 1.48), 0.025, 0.006, TIN, rot=(90, 0, 0))
    mug.build(room)

    # A rolled-up rug along the left wall.
    rx, rz = -HALF_W + 0.2, 2.5
    rug = Part("Prop_Rug", (rx, 0, rz))
    rug.cylinder((rx, 0.11, rz), 0.11, 1.6, CLOTH, axis="z", segments=14)
    for z in (-0.25, 0.75):
        rug.cylinder((rx, 0.11, rz + z), 0.113, 0.03, STRAW, axis="z", segments=14)
    rug.build(room)



def build_dust_sheet(room, pos):
    """Old furniture under a dust sheet: a white cloth over a hidden cabinet, hanging in folds to the floor."""
    x, y, z = pos
    w, d, h = 1.0, 0.9, 0.78
    cover = Part("Prop_DustSheet", pos)

    def drape(u, v):
        # Flat on top of the hidden furniture, falling away past its edges, with folds in the fall.
        edge = max(abs(u) * 2 * (w + 0.36) / w, abs(v) * 2 * (d + 0.36) / d)
        top = h if edge < 1.0 else max(0.02, h - (edge - 1.0) * 2.6)
        folds = 0.02 * math.sin(u * 37.0) * math.sin(v * 23.0) + 0.012 * math.sin((u + v) * 51.0)
        return top + (folds if edge > 0.9 else folds * 0.3)

    cover.sheet((x, y, z), (w + 0.36, d + 0.36), SHEET, drape, thickness=0.006, segments=(30, 28))
    cover.build(room)


PROPS_DIR = os.path.join(HERE, "props")


def unity_from_blender(v):
    """Blender (x, y, z) → Unity (x, y, z): the inverse of TO_BLENDER."""
    return Vector((-v[0], v[2], -v[1]))


_PROP_CACHE = {}


def place_prop(parent, asset, pos, yaw=0.0, scale=1.0, tilt=0.0, names=None, look=None, suffix="", reuse=False):
    """Puts a prepared prop (Tools/blender/props/<asset>.blend) in the room. `pos` is where its bottom centre goes
    (Unity coordinates); `yaw` turns it like Unity's Y rotation (0 = its front faces +Z); `tilt` leans it back.
    A prop made of groups (a chest and its lid) keeps each part on its own pivot; `names` renames them
    (default Prop_<asset>[_<group>]). `look` changes its materials, e.g. {"color": [...], "smoothnessScale": 0.3},
    saved under the material name + `suffix` (so a dirty copy doesn't change the clean one).
    `reuse` makes another copy of an already placed prop that shares its mesh and materials (garden plants)."""
    path = os.path.join(PROPS_DIR, asset + ".blend")
    with open(os.path.join(PROPS_DIR, asset + ".json")) as f:
        info = json.load(f)
    parts = info.get("objects") or [{"name": asset, "pivot": [0, 0, 0]}]
    if reuse and asset in _PROP_CACHE:
        dst_objects = [o.copy() for o in _PROP_CACHE[asset]]  # same mesh data, new object
    else:
        with bpy.data.libraries.load(path, link=False) as (src, dst):
            dst.objects = [p["name"] for p in parts]
        dst_objects = list(dst.objects)
        if reuse:
            _PROP_CACHE[asset] = dst_objects
    turn = Matrix.Rotation(math.radians(-yaw), 4, "Y")  # Unity's Y turn, in Unity's frame (left-handed: negate)
    entries = {m["name"]: m for m in info["materials"]}
    placed = []
    for part, obj in zip(parts, dst_objects):
        name = (names or {}).get(part["name"]) or ("Prop_" + part["name"])
        obj.name = name
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = parent
        offset = turn @ (unity_from_blender(part["pivot"]) * scale)
        obj.location = (TO_BLENDER @ (Vector(pos) + offset).to_4d()).to_3d()
        obj.rotation_euler = Euler((math.radians(-tilt), 0, math.radians(-yaw)), "XYZ")  # lean, then turn
        obj.scale = (scale, scale, scale)
        for slot in obj.material_slots:
            base = slot.material.name.split(".")[0]
            entry = dict(entries.get(base, {"name": base}))
            if look:
                entry.update(look)
                if suffix:
                    slot.material = slot.material.copy()
                    slot.material.name = base + suffix
                entry["name"] = slot.material.name
            MATERIALS[slot.material.name] = entry
        placed.append(obj)
    return placed[0] if len(placed) == 1 else placed


def build_shelf_books(room, shelf_z):
    """Rows of old books on three of the bookshelf's shelves (it stands at x 0, its shelves 1.37 m wide)."""
    books = Part("Prop_ShelfBooks", (0, 0, shelf_z))
    for shelf_y, gap in ((0.395, 0.27), (0.675, 0.27), (1.665, 0.38)):
        x = -0.64 + random.uniform(0, 0.08)
        while x < 0.62:
            if random.random() < 0.12:  # a gap now and then
                x += random.uniform(0.06, 0.15)
                continue
            w, h, d = random.uniform(0.025, 0.055), random.uniform(0.16, min(0.26, gap - 0.02)), random.uniform(0.15, 0.21)
            lean = random.uniform(-14, 14) if random.random() < 0.15 else 0
            colour = random.choice(BOOKS + [LEATHER, LEATHER])
            books.box((x + w / 2, shelf_y + h / 2 * math.cos(math.radians(lean)), shelf_z + 0.12 - d / 2), (w, h, d), colour, rot=(0, 0, lean))
            books.box((x + w / 2, shelf_y + h / 2, shelf_z + 0.12 - d / 2 + 0.002), (w - 0.006, h - 0.01, d), PAGES, rot=(0, 0, lean), bevel=0)
            x += w + 0.002
    books.build(room)


GRAB_COUNTS = {}


def grab(kind, centre):
    """A loose object for Unity to make physical and grabbable: named Grab_<Kind>_<n>, pivot at its centre."""
    GRAB_COUNTS[kind] = GRAB_COUNTS.get(kind, 0) + 1
    return Part(f"Grab_{kind}_{GRAB_COUNTS[kind]}", centre)


def build_jar(room, base, h, r):
    x, y, z = base
    g = grab("Jar", (x, y + h / 2, z))
    g.cylinder((x, y + h * 0.45, z), r, h * 0.9, random.choice(JARS), segments=16)
    g.cylinder((x, y + h * 0.93, z), r * 0.65, h * 0.1, random.choice(JARS), top_radius=r * 0.6, segments=16)
    g.cylinder((x, y + h + 0.005, z), r * 0.55, 0.03, CORK, segments=12)
    g.build(room)


def build_bottle(room, base):
    x, y, z = base
    g = grab("Bottle", (x, y + 0.14, z))
    g.cylinder((x, y + 0.09, z), 0.037, 0.18, BOTTLE, segments=16)
    g.cylinder((x, y + 0.2, z), 0.037, 0.04, BOTTLE, top_radius=0.014, segments=16)
    g.cylinder((x, y + 0.245, z), 0.014, 0.05, BOTTLE, segments=12)
    g.cylinder((x, y + 0.274, z), 0.012, 0.012, CORK, segments=10)
    g.build(room)


def build_cobwebs(room):
    webs = Part("Prop_Cobwebs")
    # Where the roof meets the gables.
    for z, dz in ((Z_BACK - 0.01, -0.3), (Z_FRONT + 0.01, 0.3)):
        for x in (-1.3, -0.5, 0.7, 1.4):
            p = Vector((x, roof_y(x) - 0.02, z))
            webs.triangle(p, p + Vector((0, -0.3 - random.uniform(0, 0.15), 0)), p + Vector((0, -0.02, dz)), COBWEB)
    # Along the knee walls, under the eaves.
    for side in (-1, 1):
        for z in (-1.0, 0.9, 2.8):
            p = Vector((side * (HALF_W - 0.04), KNEE + 0.02, z))
            webs.triangle(p, p + Vector((0, -0.28, 0)), p + Vector((-side * 0.3, 0.3 * SLOPE - 0.02, 0)), COBWEB)
    # Between the rafters and the ridge.
    for z in (-0.45, 1.05, 2.55):
        p = Vector((0, RIDGE - 0.17, z))
        webs.triangle(p, p + Vector((0.32, -0.32 * SLOPE, 0)), p + Vector((0, 0, 0.35)), COBWEB)
    webs.build(room)


# ---------------------------------------------------------------- story props (pivots match the game)

CHEST_POS = Vector((0, 0, 0.9))
CW, CH, CD, CT = 0.7, 0.4, 0.45, 0.03  # chest width, height, depth, wall thickness (as AtticSceneBuilder)


CHEST_SCALE = 0.8  # the treasure chest model, scaled to the story's chest


def build_chest(root):
    """The treasure chest (Poly Haven), front towards the player. The lid keeps its own hinge pivot; Apply Attic
    Model moves the game's LidHinge onto it, so the lid still swings open."""
    place_prop(root, "treasure_chest", CHEST_POS, yaw=180, scale=CHEST_SCALE,
               names={"treasure_chest_Body": "Chest_Body", "treasure_chest_Lid": "Chest_Lid"})


def build_lantern(root):
    """Grandfather's brass lantern (Poly Haven Lantern_01), standing in the chest: a clean copy and a grimy one
    (the game swaps them when cleaning is done)."""
    base = CHEST_POS + Vector((0, CT, 0))
    place_prop(root, "Lantern_01", base, names={"Lantern_01": "Lantern_Clean"})
    place_prop(root, "Lantern_01", base, names={"Lantern_01": "Lantern_Dirty"},
               look={"color": [0.42, 0.37, 0.30], "smoothnessScale": 0.3}, suffix="_Dirty")


# Futhark-like glyphs as strokes in a 2 x 2.4 box (x across, y up), used on the lantern and the portal.
GLYPHS = [
    [((0, -1.2), (0, 1.2)), ((0, 0.2), (0.9, 1.0)), ((0, -0.4), (0.9, 0.4))],             # fehu
    [((-0.8, -1.2), (-0.8, 1.2)), ((-0.8, 1.2), (0.8, 0.4)), ((0.8, 0.4), (0.8, -1.2))],   # uruz
    [((0, -1.2), (0, 1.2)), ((0, 0.7), (0.8, 0)), ((0.8, 0), (0, -0.7))],                  # thurisaz
    [((-0.9, -1.2), (0, 1.2)), ((0, 1.2), (0.9, -1.2))],                                    # (arrow)
    [((-0.9, -1.2), (0.9, 1.2)), ((-0.9, 1.2), (0.9, -1.2))],                               # gebo
    [((0, -1.2), (0, 1.2)), ((0, 0.3), (-0.9, 1.2)), ((0, 0.3), (0.9, 1.2))],              # algiz
    [((0, 1.2), (0.9, 0.2)), ((0.9, 0.2), (0, -0.6)), ((0, -0.6), (-0.9, 0.2)), ((-0.9, 0.2), (0, 1.2)),
     ((0, -0.6), (-0.8, -1.2)), ((0, -0.6), (0.8, -1.2))],                                  # othala
    [((-0.9, -1.2), (-0.9, 1.2)), ((0.9, -1.2), (0.9, 1.2)), ((-0.9, 1.2), (0.9, -1.2)), ((-0.9, -1.2), (0.9, 1.2))],  # dagaz
    [((0.8, 1.2), (-0.6, 0)), ((-0.6, 0), (0.8, -1.2))],                                    # kenaz
    [((-0.8, -1.2), (-0.8, 1.2)), ((0.8, -1.2), (0.8, 1.2)), ((-0.8, 0.5), (0.8, -0.3))],  # hagalaz
    [((0, -1.2), (0, 1.2)), ((0, 1.2), (0.8, 0.6)), ((0.8, 0.6), (0, 0))],                 # wunjo
    [((0, -1.2), (0, 1.2)), ((-0.7, 0.5), (0.7, -0.5))],                                    # nauthiz
]


def carve_glyph(part, glyph, centre, right, up, normal, scale, mat, width):
    """Strokes of `glyph` on a surface at `centre`, `right`/`up` along the surface, `normal` out of it."""
    c, r, u = Vector(centre), Vector(right), Vector(up)
    for (x0, y0), (x1, y1) in glyph:
        part.stroke(c + (r * x0 + u * y0) * scale, c + (r * x1 + u * y1) * scale, normal, mat, width=width)


def build_lantern_details(root):
    """Runes, the torn shard that cuts the finger, and the flame, fitted to the brass lantern: its round base
    (radius about 6 cm at 5-6 cm up), glass chamber (6-13 cm) and cap rim (16 cm)."""
    base = CHEST_POS + Vector((0, CT, 0))

    # RuneGlow: twelve glyphs carved round the base. Pivot where the game's Runes placeholder sits.
    runes = Part("Lantern_Runes", base + Vector((0, 0.11, 0)))
    radius, y = 0.0605, base.y + 0.057
    for i, glyph in enumerate(GLYPHS):
        a = 2 * math.pi * i / len(GLYPHS)
        normal = Vector((math.cos(a), 0, math.sin(a)))
        right = Vector((-math.sin(a), 0, math.cos(a)))
        carve_glyph(runes, glyph, Vector((base.x, y, base.z)) + normal * radius, right, Vector((0, 1, 0)), normal, 0.0075, RUNE, 0.0013)
    runes.build(root)

    # FingerCutEvent: a sliver of the cap's rim torn loose, sticking out towards the player. Its pivot is the
    # tip, and Apply Attic Model moves the game's SharpEdge there.
    out = Vector((0.7071, 0, -0.7071))
    side = Vector((0.7071, 0, 0.7071))
    rim = base + Vector((0, 0.158, 0)) + out * 0.052
    tip = rim + out * 0.03 + Vector((0, 0.012, 0))
    shard = Part("Lantern_Shard", tip)
    shard.triangle(rim + side * 0.008, rim - side * 0.006, tip, SHARP, thickness=0.0012)
    shard.triangle(rim - side * 0.004, rim - side * 0.012, rim + out * 0.017 - side * 0.01 + Vector((0, 0.005, 0)), SHARP, thickness=0.0012)
    shard.build(root)

    # LanternGlow: the flame in the glass chamber (it grows with the glow). Pivot on the lantern's Center.
    flame = Part("Lantern_Flame", base + Vector((0, 0.12, 0)))
    flame.cylinder(base + Vector((0, 0.084, 0)), 0.005, 0.006, FLAME, segments=10)
    flame.cylinder(base + Vector((0, 0.096, 0)), 0.005, 0.018, FLAME, top_radius=0.0004, segments=10)
    flame.build(root)


def build_cloth(room):
    """CleaningCloth: an old rag left in the chest beside the lantern."""
    centre = CHEST_POS + Vector((-0.21, CT + 0.015, -0.04))
    rag = grab("Cloth", centre)

    def folds(u, v):
        ridge = 0.012 * math.sin(u * 9.0 + v * 3.0) + 0.008 * math.sin(v * 13.0 - u * 4.0)
        lump = 0.02 * math.exp(-((u - 0.15) ** 2 + (v + 0.1) ** 2) * 18.0)  # bunched up on one side
        return ridge + lump

    rag.sheet(centre, (0.2, 0.16), RAG, folds, rot=(0, 25, 0))
    rag.build(room)


# ---------------------------------------------------------------- the house around the attic


def wall(part, axis, at, u0, u1, v0, v1, thickness, mat, holes=()):
    """A flat wall at x = `at` (axis "x", spanning z u0..u1) or z = `at` (axis "z", spanning x u0..u1), from
    height v0 to v1, with rectangular holes (u0, u1, v0, v1) for doors and windows."""
    us = sorted({u0, u1} | {h[0] for h in holes} | {h[1] for h in holes})
    vs = sorted({v0, v1} | {h[2] for h in holes} | {h[3] for h in holes})
    for a, b in zip(us, us[1:]):
        for c, d in zip(vs, vs[1:]):
            mu, mv = (a + b) / 2, (c + d) / 2
            if any(h[0] < mu < h[1] and h[2] < mv < h[3] for h in holes) or not (u0 <= mu <= u1 and v0 <= mv <= v1):
                continue
            if axis == "x":
                part.box((at, mv, mu), (thickness, d - c, b - a), mat, bevel=0, world_uv=True)
            else:
                part.box((mu, mv, at), (b - a, d - c, thickness), mat, bevel=0, world_uv=True)


def window_frame(part, axis, at, u0, u1, v0, v1):
    """Frame, cross bars and dark glass in a wall hole."""
    cu, cv, w, h = (u0 + u1) / 2, (v0 + v1) / 2, u1 - u0, v1 - v0
    def box(u, v, su, sv, depth, mat):
        if axis == "x":
            part.box((at, v, u), (depth, sv, su), mat)
        else:
            part.box((u, v, at), (su, sv, depth), mat)
    for v in (v0, v1):
        box(cu, v, w + 0.1, 0.06, 0.2, WOOD_DARK)
    for u in (u0, u1):
        box(u, cv, 0.06, h + 0.1, 0.2, WOOD_DARK)
    box(cu, cv, 0.04, h, 0.06, WOOD_DARK)
    box(cu, cv, w, 0.04, 0.06, WOOD_DARK)
    box(cu, cv, w, h, 0.01, NIGHT_GLASS)


def build_house(root):
    """An old two-storey house around the attic, for the walk in at midnight: a yard with a stone path, a porch
    and front door, a hall with stairs up to the loft, and the gable roof over the attic. Everything that is
    walked on or bumped into is named House_* (Apply Attic Model gives those colliders)."""
    house = root  # (see build_room)
    zf, zb = Z_HOUSE, Z_BACK
    out_f, out_b = zf - 0.12, zb + 0.12

    # The yard and the path to the porch.
    yard = Part("House_Terrain", (0, TERRAIN, -6))
    yard.box((0, TERRAIN - 0.05, -6), (80, 0.1, 80), GRASS, bevel=0, world_uv=True)
    yard.build(house)
    path = Part("House_Path", (0, TERRAIN, -16))
    porch_front = zf - 1.8
    path_end = porch_front - 0.9  # the foot of the porch steps
    path.box((0, TERRAIN + 0.012, (path_end - 24) / 2), (1.3, 0.025, path_end + 24), PATH, bevel=0, world_uv=True)
    path.build(house)

    # Outside walls: siding over a stone foundation, with the front door and windows.
    door = (-0.5, 0.5, GROUND, GROUND + 2.1)
    front_holes = [door, (-2.0, -1.2, GROUND + 0.9, GROUND + 2.2), (1.2, 2.0, GROUND + 0.9, GROUND + 2.2)]
    left_holes = [(-5.0, -4.0, GROUND + 0.9, GROUND + 2.2), (0.5, 1.5, GROUND + 0.9, GROUND + 2.2)]
    right_holes = [(-6.3, -5.5, GROUND + 0.9, GROUND + 2.2), (0.5, 1.5, GROUND + 0.9, GROUND + 2.2)]
    back_holes = [(-0.5, 0.5, GROUND + 0.9, GROUND + 2.2)]
    walls = Part("House_Walls", (0, GROUND, (zf + zb) / 2))
    top = KNEE + 0.02  # the siding goes up to the eaves
    wall(walls, "z", out_f, -WALL_OUT, WALL_OUT, GROUND, -0.1, 0.05, HOUSE_PLASTER, front_holes)
    wall(walls, "z", out_b, -WALL_OUT, WALL_OUT, GROUND, -0.1, 0.05, HOUSE_PLASTER, back_holes)
    wall(walls, "x", -WALL_OUT, out_f, out_b, GROUND, top, 0.05, HOUSE_PLASTER, left_holes)
    wall(walls, "x", WALL_OUT, out_f, out_b, GROUND, top, 0.05, HOUSE_PLASTER, right_holes)
    # Gables above the floor line, outside: the attic window at the back, the loft window at the front.
    gable_with_window(walls, out_b - 0.025, out_b + 0.025, 0.3, 2.5, 3.05, HOUSE_PLASTER, half=WALL_OUT, base=-0.1, lift=0.15)
    gable_with_window(walls, out_f - 0.025, out_f + 0.025, 0.25, 1.95, 2.45, HOUSE_PLASTER, half=WALL_OUT, base=-0.1, lift=0.15)
    build_timber_frame(walls, out_f, out_b, front_holes, back_holes, left_holes, right_holes)
    # Inside the hall: plaster walls with the same holes, and a wooden ceiling under the loft floor.
    wall(walls, "z", zf + 0.02, -HALF_W, HALF_W, GROUND, -0.1, 0.04, PLASTER, front_holes)
    wall(walls, "z", zb - 0.02, -HALF_W, HALF_W, GROUND, -0.1, 0.04, PLASTER, back_holes)
    wall(walls, "x", -HALF_W - 0.02, zf, zb, GROUND, -0.1, 0.04, PLASTER, left_holes)
    wall(walls, "x", HALF_W + 0.02, zf, zb, GROUND, -0.1, 0.04, PLASTER, right_holes)
    for side, holes in ((-1, left_holes), (1, right_holes)):  # wall thickness between the two faces
        for h in holes:
            window_frame(walls, "x", side * (HALF_W + 0.06), h[0], h[1], h[2], h[3])
    for h in front_holes[1:]:
        window_frame(walls, "z", zf - 0.05, h[0], h[1], h[2], h[3])
    for h in back_holes:
        window_frame(walls, "z", zb + 0.05, h[0], h[1], h[2], h[3])
    # Stone foundation all round.
    fz = (out_f + out_b) / 2
    for side in (-1, 1):
        walls.box((side * (WALL_OUT + 0.05), (TERRAIN + GROUND + 0.1) / 2, fz), (0.14, GROUND + 0.1 - TERRAIN, out_b - out_f + 0.24), STONE, bevel=0.01, world_uv=True)
    for zz in (out_f - 0.05, out_b + 0.05):
        walls.box((0, (TERRAIN + GROUND + 0.1) / 2, zz), (2 * WALL_OUT + 0.24, GROUND + 0.1 - TERRAIN, 0.14), STONE, bevel=0.01, world_uv=True)
    # Door frame.
    for x in (-0.55, 0.55):
        walls.box((x, GROUND + 1.08, zf - 0.04), (0.1, 2.2, 0.24), WOOD_DARK)
    walls.box((0, GROUND + 2.15, zf - 0.04), (1.2, 0.12, 0.24), WOOD_DARK)
    walls.build(house)

    # Hall floor and ceiling (under the loft).
    hall = Part("House_Floor", (0, GROUND, (zf + zb) / 2))
    x = -HALF_W
    while x < HALF_W - 0.01:
        hall.box((x + 0.1, GROUND - 0.015, (zf + zb) / 2), (0.19, 0.03, zb - zf), random.choice(FLOOR), bevel=0.004)
        x += 0.2
    hall.box((0, GROUND - 0.05, (zf + zb) / 2), (2 * HALF_W, 0.04, zb - zf), SHADOW, bevel=0)
    hx0, hx1, hz0, hz1 = OPENING
    x = -HALF_W
    while x < HALF_W - 0.01:  # ceiling boards, leaving the hole over the stairs
        for a, b in plank_runs(x + 0.1, zf, zb, OPENING):
            hall.box((x + 0.1, -0.09, (a + b) / 2), (0.19, 0.02, b - a), WOOD_DARK, bevel=0.003)
        x += 0.2
    hall.build(house)

    # Stairs up to the loft, with a handrail, and a rail round the hole at the top.
    stairs = Part("House_Stairs", (STAIR_X0, GROUND, STAIR_Z0))
    steps = 16
    rise, run = -GROUND / steps, (STAIR_Z1 - STAIR_Z0) / steps
    cx, width = (STAIR_X0 + STAIR_X1) / 2, STAIR_X1 - STAIR_X0
    for k in range(steps):
        y = GROUND + (k + 1) * rise
        z = STAIR_Z0 + (k + 0.5) * run
        stairs.box((cx, y - 0.02, z), (width, 0.04, run + 0.03), WOOD)
        stairs.box((cx, y - rise / 2 - 0.02, STAIR_Z0 + k * run + 0.012), (width - 0.02, rise, 0.025), WOOD_DARK)
    slope = math.degrees(math.atan2(-GROUND, STAIR_Z1 - STAIR_Z0))
    length = math.hypot(-GROUND, STAIR_Z1 - STAIR_Z0)
    mid_y, mid_z = GROUND / 2, (STAIR_Z0 + STAIR_Z1) / 2
    for x in (STAIR_X0 - 0.03, STAIR_X1 + 0.03):
        stairs.box((x, mid_y - 0.05, mid_z), (0.05, 0.25, length), BEAM, rot=(-slope, 0, 0))  # stringers
    stairs.box((STAIR_X0 - 0.05, mid_y + 0.9, mid_z), (0.05, 0.06, length), WOOD_DARK, rot=(-slope, 0, 0))  # handrail
    for z, y in ((STAIR_Z0 + 0.1, GROUND), (STAIR_Z1 - 0.3, 0.0)):
        stairs.box((STAIR_X0 - 0.05, y + 0.5, z), (0.07, 1.0, 0.07), BEAM)
    hx0, hx1, hz0, hz1 = OPENING
    for x0, z0, x1, z1 in ((hx0, hz0, hx0, STAIR_Z1 - 0.3), (hx0, hz0, hx1, hz0), (hx1, hz0, hx1, hz1)):
        stairs.box(((x0 + x1) / 2, 0.95, (z0 + z1) / 2), (abs(x1 - x0) + 0.06, 0.06, abs(z1 - z0) + 0.06), WOOD_DARK)
        for t in (0.0, 0.5, 1.0):
            stairs.box((x0 + (x1 - x0) * t, 0.48, z0 + (z1 - z0) * t), (0.05, 0.95, 0.05), BEAM)
    stairs.build(house)

    # The front door, hinged on its left (AtticIntro opens it). Pivot on the hinge.
    hinge = Vector((-0.49, GROUND, zf - 0.06))
    front = Part("House_FrontDoor", hinge)
    for i in range(5):  # vertical planks
        front.box((hinge.x + 0.1 + i * 0.196, GROUND + 1.04, hinge.z), (0.19, 2.06, 0.05), random.choice((WOOD, WOOD_DARK)))
    for y in (0.35, 1.75):  # iron straps
        front.box((hinge.x + 0.49, GROUND + y, hinge.z - 0.03), (0.94, 0.06, 0.008), IRON)
    front.torus((hinge.x + 0.84, GROUND + 1.0, hinge.z - 0.045), 0.035, 0.006, IRON)  # ring handle
    front.build(house)

    # Porch: deck, steps, posts and a little roof.
    porch = Part("House_Porch", (0, GROUND, zf - 0.9))
    porch.box((0, GROUND - 0.03, zf - 0.9), (3.4, 0.06, 1.8), WOOD, world_uv=True)
    porch.box((0, (TERRAIN + GROUND) / 2, zf - 0.9), (3.3, GROUND - TERRAIN - 0.06, 1.7), STONE, bevel=0.01, world_uv=True)
    for i in range(3):
        h = (GROUND - TERRAIN) * (i + 1) / 4
        porch.box((0, TERRAIN + h / 2, porch_front - 0.15 - (2 - i) * 0.3), (1.6, h, 0.3), WOOD_DARK)
    for x in (-1.55, 1.55):
        porch.box((x, GROUND + 1.3, porch_front + 0.12), (0.12, 2.6, 0.12), BEAM)
    porch.box((0, GROUND + 2.62, porch_front + 0.12), (3.4, 0.14, 0.14), BEAM)
    porch.box((0, GROUND + 2.75, zf - 0.95), (3.8, 0.06, 2.2), ROOF_TILES, rot=(-8, 0, 0), world_uv=True)
    porch.build(house)

    # A lamp by the front door, lit (Unity adds its warm light): something to walk towards in the dark.
    lx, ly, lz = 0.85, GROUND + 1.9, zf - 0.16
    lamp = Part("House_PorchLamp", (lx, ly, lz))
    lamp.box((lx, ly + 0.2, lz + 0.09), (0.03, 0.03, 0.18), IRON)               # bracket
    lamp.box((lx, ly + 0.02, lz), (0.16, 0.025, 0.16), IRON)                     # base
    lamp.box((lx, ly + 0.13, lz), (0.12, 0.2, 0.12), LAMP_GLASS, bevel=0.002)    # glowing glass
    lamp.cylinder((lx, ly + 0.27, lz), 0.1, 0.08, IRON, top_radius=0.02, segments=4, rot=(0, 45, 0))  # cap
    lamp.build(house)

    # The roof outside: tiles over the boards, overhanging the walls, a ridge cap, and the brick chimney.
    roof = Part("House_Roof", (0, RIDGE, (zf + zb) / 2))
    eave_x = WALL_OUT + 0.4
    slope_len = math.hypot(eave_x, RIDGE - roof_y(eave_x))
    length = zb - zf + 0.9
    for side in (-1, 1):
        along = Vector((-side * math.cos(ROOF_ANGLE), math.sin(ROOF_ANGLE), 0))
        out = Vector((side * math.sin(ROOF_ANGLE), math.cos(ROOF_ANGLE), 0))
        eave = Vector((side * eave_x, roof_y(eave_x), 0))
        mid = eave + along * slope_len / 2 + out * 0.18
        angle = math.degrees(ROOF_ANGLE) * -side
        roof.box((mid.x, mid.y, (zf + zb) / 2), (slope_len, 0.06, length), ROOF_TILES, rot=(0, 0, angle), bevel=0)
        fascia = eave + out * 0.12
        roof.box((fascia.x, fascia.y - 0.05, (zf + zb) / 2), (0.04, 0.22, length), WOOD_DARK, bevel=0)
        for zz in (zf - 0.45, zb + 0.45):  # boards along the gable edges
            roof.box((mid.x, mid.y - 0.02, zz), (slope_len, 0.2, 0.05), WOOD_DARK, rot=(0, 0, angle), bevel=0)
        with kept_random():  # courses of tiles stepping down the slope, each lapping the one below
            courses = 11
            for k in range(courses):
                t = (k + 0.5) / courses
                at = eave + along * slope_len * t + out * 0.215
                roof.box((at.x, at.y, (zf + zb) / 2), (slope_len / courses * 1.3, 0.035, length + 0.06), ROOF_TILES,
                         rot=(0, 0, angle + side * 1.6), bevel=0)
    with kept_random():
        roof.cylinder((0, RIDGE + 0.3, (zf + zb) / 2), 0.13, length + 0.08, ROOF_TILES, axis="z", segments=8)
    chimney_x, chimney_z = WALL_OUT + 0.35, -4.3
    top = RIDGE + 0.9
    roof.box((chimney_x, (TERRAIN + top) / 2, chimney_z), (0.7, top - TERRAIN, 0.7), STONE, bevel=0.01, world_uv=True)
    roof.box((chimney_x, top + 0.05, chimney_z), (0.85, 0.1, 0.85), STONE, bevel=0.01)
    roof.build(house)

    # A few old boxes in the loft, by the stairs.
    loft = Part("House_LoftBoxes", (-1.4, 0, -4.5))
    loft.box((-1.4, 0.2, -4.6), (0.6, 0.4, 0.5), WOOD_LIGHT, rot=(0, 10, 0))
    loft.box((-1.45, 0.55, -4.55), (0.45, 0.3, 0.4), WOOD, rot=(0, -12, 0))
    loft.box((-1.6, 0.18, -3.7), (0.5, 0.36, 0.45), WOOD_DARK, rot=(0, 25, 0))
    loft.build(house)


def build_ground_floor(root):
    """Furniture on the ground floor (CC0 Poly Haven models), kept clear of the way in (front door to the foot
    of the stairs) and of the stairs: a living corner by the front, a dining table in the middle, a bed at the back."""
    g = GROUND
    # Living corner, front left: sofa on the wall, coffee table, rocking chair, and a table with a flower vase.
    place_prop(root, "Sofa_01", (-HALF_W + 0.36, g, -6.1), yaw=90)
    place_prop(root, "CoffeeTable_01", (-0.95, g, -6.1), yaw=90)
    place_prop(root, "Rockingchair_01", (-1.15, g, -4.3), yaw=150)
    place_prop(root, "round_wooden_table_02", (-1.95, g, -3.1), yaw=20)
    vase_top = g + 0.75 + 0.41
    place_prop(root, "ceramic_vase_03", (-1.95, g + 0.75, -3.1), yaw=0)
    for i in range(6):  # a small bunch of yellow wildflowers in the vase, fanned out
        a = 360 * i / 6 + random.uniform(-15, 15)
        place_prop(root, "flower_empodium", (-1.95, vase_top - 0.16, -3.1), yaw=a, tilt=random.uniform(8, 22),
                   scale=random.uniform(1.5, 1.9), names={"flower_empodium": f"Prop_Flower_{i + 1}"}, reuse=True)
    # By the front door: the grandfather clock.
    place_prop(root, "vintage_grandfather_clock_01", (0.9, g, Z_HOUSE + 0.25), yaw=0)
    # Dining in the middle: the table (cloth and all) on a rug, with four old wooden chairs.
    tx, tz = -0.9, 0.3
    rug = Part("House_Rug", (tx, g, tz))
    rug.box((tx, g + 0.006, tz), (3.0, 0.012, 2.2), CLOTH, bevel=0.002, world_uv=True)
    rug.build(root)
    place_prop(root, "dining_table", (tx, g, tz), yaw=0)
    for i, (dx, side) in enumerate(((-0.55, -1), (0.55, -1), (-0.55, 1), (0.55, 1))):
        place_prop(root, "painted_wooden_chair_02", (tx + dx, g, tz + side * 0.98), yaw=0 if side < 0 else 180,
                   names={"painted_wooden_chair_02": f"Prop_Chair_{i + 1}"}, reuse=True)
    # Right wall: a tall carved cabinet.
    place_prop(root, "GothicCabinet_01", (HALF_W - 0.6, g, -1.3), yaw=-90)
    # Back right: the bed, head against the back wall, and a nightstand with a candlestick.
    place_prop(root, "GothicBed_01", (HALF_W - 0.8, g, Z_BACK - 1.05), yaw=180)
    place_prop(root, "ClassicNightstand_01", (0.5, g, Z_BACK - 0.25), yaw=180)
    place_prop(root, "wooden_candlestick", (0.45, g + 0.7, Z_BACK - 0.28), yaw=0)


def build_garden(root):
    """An overgrown garden round the house (the garden_plants clump, model.obj: small trees over ferns): along
    both sides of the path, beside the house and behind it. Each copy is turned and sized differently. They
    are named Garden_* and sway in the wind in Unity (WindSway)."""
    spots = [  # (x, z) in the yard, clear of the path (x ±0.65), the porch, the chimney and the yard's stonework
        (-4.4, -14.6), (6.6, -15.0), (-6.4, -20.4), (6.8, -20.8),     # lining the path
        (-5.2, -6.6), (4.6, -2.0), (-4.7, 1.4), (4.9, 1.6),           # beside the house
        (-2.6, 7.6), (2.9, 8.2),                                        # behind it
        (-8.8, -11.5), (8.2, -12.5),                                    # out in the corners
    ]
    for i, (x, z) in enumerate(spots):
        place_prop(root, "garden_plants", (x + random.uniform(-0.3, 0.3), TERRAIN, z + random.uniform(-0.3, 0.3)),
                   yaw=random.uniform(0, 360), scale=random.uniform(3.0, 4.2),
                   names={"garden_plants": f"Garden_Plants_{i + 1}"}, reuse=True)


def build_portal(root):
    """The portal's stone ring: weathered blocks with glowing runes on the face towards the player (+Z).
    Pivot where the game's Portal sits; the swirl inside is made at runtime by PortalController."""
    centre = Vector((0, 1.3, 2.6))
    ring = Part("Portal_Frame", centre)
    inner, outer, depth, blocks, gap = 0.86, 1.06, 0.14, 13, 0.014
    for i in range(blocks):
        a0 = 2 * math.pi * i / blocks + math.pi / 2 - math.pi / blocks + gap / inner
        a1 = 2 * math.pi * (i + 1) / blocks + math.pi / 2 - math.pi / blocks - gap / inner
        keystone = i == 0
        o = outer + (0.06 if keystone else random.uniform(-0.015, 0.015))
        n = inner - (0.02 if keystone else random.uniform(0.0, 0.012))
        mids = [a0 + (a1 - a0) * k / 3 for k in range(4)]
        outline = [(centre.x + n * math.cos(a), centre.y + n * math.sin(a)) for a in mids] + \
                  [(centre.x + o * math.cos(a), centre.y + o * math.sin(a)) for a in reversed(mids)]
        dz = random.uniform(-0.012, 0.012)
        ring.prism(outline, centre.z - depth / 2 + dz, centre.z + depth / 2 + dz, STONE)
        mid = (a0 + a1) / 2
        radial = Vector((math.cos(mid), math.sin(mid), 0))
        tangent = Vector((-math.sin(mid), math.cos(mid), 0))
        face = centre + radial * ((n + o) / 2) + Vector((0, 0, depth / 2 + dz))
        carve_glyph(ring, GLYPHS[i % len(GLYPHS)], face, tangent, radial, Vector((0, 0, 1)), 0.035, PORTAL_RUNE, 0.006)
    ring.build(root)


def build_crate(root, name, pos, size):
    pos, (w, h, d) = Vector(pos), size
    p = Part(name, pos)
    b = 0.045
    for x in (-1, 1):
        for z in (-1, 1):
            p.box(pos + Vector((x * (w - b) / 2, -0.008, z * (d - b) / 2)), (b, h - 0.016, b), WOOD_DARK)
    for y in (-1, 1):
        fy = y * (h - b) / 2 - (0.016 if y > 0 else 0)  # the top frame sits under the lid planks
        for z in (-1, 1):
            p.box(pos + Vector((0, fy, z * (d - b) / 2)), (w - 2 * b, b, b), WOOD_DARK)
        for x in (-1, 1):
            p.box(pos + Vector((x * (w - b) / 2, fy, 0)), (b, b, d - 2 * b), WOOD_DARK)
    slats = 3
    for i in range(slats):
        y = -h / 2 + b + (h - 2 * b) * (i + 0.5) / slats
        sh = (h - 2 * b) / slats - 0.012
        for z in (-1, 1):
            p.box(pos + Vector((0, y, z * (d / 2 - 0.012))), (w - 2 * b, sh, 0.016), WOOD)
        for x in (-1, 1):
            p.box(pos + Vector((x * (w / 2 - 0.012), y, 0)), (0.016, sh, d - 2 * b), WOOD)
    for i in range(4):  # closed lid, the books lie on it
        z = -d / 2 + d * (i + 0.5) / 4
        p.box(pos + Vector((0, h / 2 - 0.008, z)), (w - 0.01, 0.016, d / 4 - 0.008), WOOD_LIGHT)
    diag = math.degrees(math.atan2(h - 2 * b, w - 2 * b))
    for z in (-1, 1):  # diagonal braces
        p.box(pos + Vector((0, 0, z * (d / 2 + 0.002))), (math.hypot(w - 2 * b, h - 2 * b), 0.04, 0.012), WOOD_DARK, rot=(0, 0, diag * z))
    p.box(pos, (w - 0.05, h - 0.05, d - 0.05), SHADOW)
    p.build(root)


BOOK_SPOTS = [(-0.7, 0.02, 1.05), (-0.68, 0.055, 1.07), (-0.72, 0.09, 1.04), (0.9, 0.42, 1.25), (1.05, 0.42, 1.35), (-1.0, 0.52, 1.4)]


def build_book(root, i, pos):
    pos = Vector(pos)
    p = Part(f"Book_{i + 1}", pos)
    cover = BOOKS[i % 3]
    p.box(pos + Vector((0, 0.0155, 0)), (0.172, 0.004, 0.232), cover)
    p.box(pos + Vector((0, -0.0155, 0)), (0.172, 0.004, 0.232), cover)
    p.box(pos + Vector((-0.084, 0, 0)), (0.008, 0.035, 0.232), cover)
    p.box(pos + Vector((0.004, 0, 0)), (0.16, 0.027, 0.222), PAGES)
    for z in (-0.06, 0.06):  # bands on the spine
        p.box(pos + Vector((-0.089, 0, z)), (0.003, 0.037, 0.012), BRASS)
    p.build(root)


# ---------------------------------------------------------------- half-timbering and the old yard


@contextmanager
def kept_random():
    """The attic's looks hang off one random sequence (props, plank colours, flowers). The exterior was added later and
    uses its own (YARD); box UVs draw from the shared one, so wrap new shapes in this to leave the rest undisturbed."""
    state = random.getstate()
    try:
        yield
    finally:
        random.setstate(state)


YARD = random.Random(41)


def spans_clear(u0, u1, v, holes):
    """The stretches of u0..u1 at height v that don't cross a door or window."""
    cuts = sorted((h[0], h[1]) for h in holes if h[2] < v < h[3])
    spans, at = [], u0
    for a, b in cuts:
        if a > at:
            spans.append((at, a))
        at = max(at, b)
    if at < u1:
        spans.append((at, u1))
    return spans


def timber_wall(part, axis, at, u0, u1, holes, v_top, side):
    """Dark oak over the plaster of one wall: rails, posts and raking braces. `side` is the wall's outward direction."""
    face = at + side * 0.045

    def beam(u, v, su, sv, rot=0.0):
        if axis == "x":
            part.box((face, v, u), (0.07, sv, su), TIMBER, rot=(-rot, 0, 0), bevel=0.008)
        else:
            part.box((u, v, face), (su, sv, 0.07), TIMBER, rot=(0, 0, rot), bevel=0.008)

    base, sill, head, floor = GROUND + 0.1, GROUND + 0.8, GROUND + 2.3, -0.1
    rails = [base, sill, head]
    for v in rails:
        for a, b in spans_clear(u0, u1, v, holes):
            beam((a + b) / 2, v, b - a, 0.11)
    beam((u0 + u1) / 2, floor, u1 - u0 + 0.14, 0.2)  # the heavy beam at the upper floor
    if v_top > floor + 0.5:
        beam((u0 + u1) / 2, v_top - 0.07, u1 - u0, 0.12)

    posts = {u0, u1}
    for h in holes:
        posts |= {h[0] - 0.07, h[1] + 0.07}
    posts = sorted(p for p in posts if u0 <= p <= u1)
    filled = []
    for a, b in zip(posts, posts[1:]):
        filled.append(a)
        if b - a > 1.5:
            filled.append((a + b) / 2)
    filled.append(posts[-1])
    for u in filled:
        beam(u, (GROUND + floor) / 2, 0.11, floor - GROUND)
        if v_top > floor + 0.5:
            beam(u, (floor + v_top) / 2, 0.11, v_top - floor)
    # A brace leaning across each bay with no opening in it.
    for k, (a, b) in enumerate(zip(filled, filled[1:])):
        if b - a < 0.55 or any(h[0] < (a + b) / 2 < h[1] for h in holes):
            continue
        du, dv = b - a, head - sill
        lean = math.degrees(math.atan2(dv, du)) * (1 if k % 2 else -1)
        beam((a + b) / 2, (head + sill) / 2, math.hypot(du, dv) - 0.04, 0.09, lean)


def build_timber_frame(part, out_f, out_b, front_holes, back_holes, left_holes, right_holes):
    with kept_random():
        timber_wall(part, "z", out_f, -WALL_OUT, WALL_OUT, front_holes, -0.1, -1)
        timber_wall(part, "z", out_b, -WALL_OUT, WALL_OUT, back_holes, -0.1, 1)
        timber_wall(part, "x", -WALL_OUT, out_f, out_b, left_holes, KNEE, -1)
        timber_wall(part, "x", WALL_OUT, out_f, out_b, right_holes, KNEE, 1)
        for z, side, wx, wy0, wy1 in ((out_f, -1, 0.25, 1.95, 2.45), (out_b, 1, 0.3, 2.5, 3.05)):  # the gables
            face = z + side * 0.04
            part.box((0, 1.15, face), (2 * WALL_OUT, 0.12, 0.07), TIMBER, bevel=0.008)
            part.box((0, wy0 - 0.08, face), (2 * wx + 0.36, 0.1, 0.07), TIMBER, bevel=0.008)
            part.box((0, wy1 + 0.08, face), (2 * wx + 0.1, 0.1, 0.07), TIMBER, bevel=0.008)
            for x in (-1.55, -(wx + 0.09), wx + 0.09, 1.55):
                top = roof_y(abs(x)) + 0.1
                part.box((x, (-0.1 + top) / 2, face), (0.1, top + 0.1, 0.07), TIMBER, bevel=0.008)
            for s in (-1, 1):  # raking struts in the gable ends
                part.box((s * 2.0, 0.55, face), (0.09, 1.25, 0.07), TIMBER, rot=(0, 0, s * 32), bevel=0.008)


def stone_run(part, points, height=0.75, width=0.42, ruin=0.2):
    """A low dry-stone wall along `points` (x, z): rough courses of blocks, mossy towards the top, broken in places."""
    for (x0, z0), (x1, z1) in zip(points, points[1:]):
        length = math.hypot(x1 - x0, z1 - z0)
        yaw = -math.degrees(math.atan2(z1 - z0, x1 - x0))
        dx, dz = (x1 - x0) / length, (z1 - z0) / length
        courses = 4
        for c in range(courses):
            along = YARD.uniform(-0.1, 0.1)
            base = TERRAIN + c * height / courses
            while along < length:
                size = YARD.uniform(0.32, 0.62)
                t = along + size / 2
                # The top courses have fallen in stretches.
                fallen = c >= 2 and math.sin(t * 0.9 + x0 * 2.1 + z0) + (c - 2) * 0.9 > 2 * (1 - ruin) - 0.7
                if not fallen and t < length:
                    tall = height / courses * YARD.uniform(0.92, 1.25)
                    mat = MOSS_STONE if YARD.random() < 0.18 + 0.2 * c else YARD_STONE
                    part.box((x0 + dx * t + YARD.uniform(-0.03, 0.03), base + tall / 2, z0 + dz * t + YARD.uniform(-0.03, 0.03)),
                             (size, tall, width * YARD.uniform(0.85, 1.05)), mat,
                             rot=(YARD.uniform(-2, 2), yaw + YARD.uniform(-5, 5), YARD.uniform(-2.5, 2.5)), bevel=0)
                along += size * YARD.uniform(0.96, 1.08)


def build_yard_walls(root):
    """The wall round the yard, gapped for the path, ruined here and there, and the stone gate pillars."""
    walls = Part("House_YardWalls", (0, TERRAIN, -17))
    stone_run(walls, [(-1.6, -17), (-6.2, -17.6), (-10.6, -15.8), (-12.0, -9.0), (-11.6, -2.0)])
    stone_run(walls, [(1.6, -17), (5.6, -17.5), (10.8, -16.4), (11.6, -10.6)], ruin=0.32)
    for x in (-1.45, 1.45):
        walls.box((x, TERRAIN + 0.65, -17), (0.6, 1.3, 0.6), YARD_STONE, bevel=0.02, rot=(0, YARD.uniform(-4, 4), 0))
        walls.box((x, TERRAIN + 1.36, -17), (0.72, 0.14, 0.72), MOSS_STONE, bevel=0.02)
    walls.build(root)


def build_annex(root):
    """A stone lean-to against the house's left side, under an old plank roof, grown over with moss."""
    x0, x1, z0, z1, h = -WALL_OUT - 0.02, -5.1, -3.7, -0.6, 2.05
    a = Part("House_Annex", ((x0 + x1) / 2, TERRAIN, (z0 + z1) / 2))
    mx, mz = (x0 + x1) / 2, (z0 + z1) / 2
    a.box((x1, TERRAIN + h / 2, mz), (0.35, h, z1 - z0 + 0.35), YARD_STONE, bevel=0.01, world_uv=True)
    for z in (z0, z1):
        a.box((mx, TERRAIN + h / 2 + 0.1, z), (x0 - x1, h + 0.2, 0.35), YARD_STONE, bevel=0.01, world_uv=True)
    a.box((x1 - 0.19, TERRAIN + 1.05, mz), (0.02, 1.7, 0.95), SHADOW, bevel=0)                  # the old doorway
    a.box((x1 - 0.21, TERRAIN + 1.05, mz), (0.05, 1.75, 1.05), TIMBER, bevel=0)
    a.box((x1 - 0.235, TERRAIN + 1.0, mz), (0.04, 1.66, 0.88), WOOD_DARK, bevel=0)
    pitch = math.degrees(math.atan2(0.75, x0 - x1))
    for k in range(9):                                                                           # the plank roof
        z = z0 - 0.3 + (z1 - z0 + 0.6) * (k + 0.5) / 9
        a.box((mx - 0.1, TERRAIN + h + 0.5 + YARD.uniform(-0.012, 0.012), z), (x0 - x1 + 0.75, 0.05, (z1 - z0 + 0.6) / 9 + 0.01),
              WOOD_DARK, rot=(0, 0, pitch), bevel=0.004)
    for k in range(16):                                                                          # creeping moss
        y = TERRAIN + YARD.uniform(0.05, h)
        z = YARD.uniform(z0, z1)
        a.box((x1 - 0.185, y, z), (0.03, YARD.uniform(0.2, 0.55), YARD.uniform(0.3, 0.8)), MOSS, bevel=0,
              rot=(YARD.uniform(-25, 25), 0, 0))
    a.build(root)


def build_well(root, cx, cz):
    """The old stone well: three courses of blocks round a dark shaft, two oak posts and a little shingled roof."""
    w = Part("House_Well", (cx, TERRAIN, cz))
    radius = 0.68
    for c in range(4):
        n = 13
        for i in range(n):
            a = 2 * math.pi * (i + (c % 2) * 0.5) / n
            y = TERRAIN + 0.13 + c * 0.25
            mat = MOSS_STONE if YARD.random() < 0.15 + 0.14 * c else YARD_STONE
            w.box((cx + math.cos(a) * radius, y, cz + math.sin(a) * radius), (0.3, 0.26, 2 * math.pi * radius / n * 1.05), mat,
                  rot=(0, -math.degrees(a) + YARD.uniform(-5, 5), YARD.uniform(-2, 2)), bevel=0)
    w.cylinder((cx, TERRAIN + 0.86, cz), radius - 0.05, 0.02, SHADOW, segments=16)
    for s in (-1, 1):
        w.box((cx + s * 0.86, TERRAIN + 1.2, cz), (0.13, 2.4, 0.13), TIMBER, bevel=0.01)
    w.cylinder((cx, TERRAIN + 1.72, cz), 0.06, 1.8, WOOD, axis="x", segments=8)                  # the windlass
    w.cylinder((cx, TERRAIN + 1.32, cz), 0.009, 0.78, RAG, segments=5)                            # the rope
    ridge, half, pitch = TERRAIN + 2.48, 0.95, 36.0
    run = half * math.cos(math.radians(pitch))
    for s in (-1, 1):
        rows = 5
        for k in range(rows):
            t = (k + 0.5) / rows
            w.box((cx, ridge - t * half * math.sin(math.radians(pitch)) - 0.01 * k, cz + s * t * run),
                  (2.15, 0.04, half / rows * 1.3), WOOD_DARK, rot=(s * pitch, 0, 0), bevel=0.004)
    w.box((cx, ridge + 0.03, cz), (2.2, 0.1, 0.12), TIMBER, bevel=0.01)
    w.build(root)
    place_prop(root, "wooden_bucket_01", (cx + 0.55, TERRAIN + 1.0, cz + 0.3), yaw=30)


def build_graves(root):
    """The family plot in the yard's corner: a low wall, leaning headstones, a tomb and two weathered stone mourners."""
    g = Part("House_Graves", (7.5, TERRAIN, -7))
    stone_run(g, [(5.2, -10.2), (10.4, -10.4), (10.8, -3.4), (5.6, -3.2)], height=0.55, width=0.34, ruin=0.3)
    stones = [(6.1, -9.0, 0.5), (7.2, -9.2, 0.62), (8.4, -8.9, 0.46), (9.5, -9.1, 0.56), (9.4, -7.5, 0.52), (6.0, -5.2, 0.6), (7.2, -4.7, 0.44)]
    for x, z, h in stones:
        lean = (YARD.uniform(-7, 7), YARD.uniform(-14, 14), YARD.uniform(-7, 7))
        g.box((x, TERRAIN + h / 2 + 0.1, z), (0.5, h, 0.14), GRAVE_STONE, rot=lean, bevel=0.012)
        g.cylinder((x, TERRAIN + h + 0.1, z), 0.25, 0.14, GRAVE_STONE, axis="z", rot=lean, segments=10)
        g.box((x, TERRAIN + 0.05, z), (0.68, 0.1, 0.34), MOSS_STONE, bevel=0.01)
    # The tomb.
    g.box((7.8, TERRAIN + 0.3, -6.8), (1.9, 0.6, 0.95), GRAVE_STONE, bevel=0.015)
    g.box((7.8, TERRAIN + 0.65, -6.8), (2.05, 0.1, 1.08), MOSS_STONE, bevel=0.015)
    # Two hooded mourners on plinths, more worn shapes than statues.
    for x, z in ((5.95, -9.85), (10.0, -4.0)):
        base = TERRAIN + 0.75
        g.box((x, TERRAIN + 0.38, z), (0.7, 0.76, 0.7), GRAVE_STONE, bevel=0.015)
        g.cylinder((x, base + 0.62, z), 0.3, 1.25, GRAVE_STONE, top_radius=0.13, segments=10)
        g.cylinder((x, base + 1.33, z), 0.15, 0.3, GRAVE_STONE, top_radius=0.06, segments=10)
        g.box((x, base + 0.72, z - 0.08), (0.42, 0.12, 0.2), GRAVE_STONE, rot=(0, 0, 8), bevel=0.02)   # folded arms
    g.build(root)


def build_dead_tree(root, name, cx, cz, height, seed):
    """A bare dead tree: a leaning trunk forking into thinning branches."""
    rng = random.Random(seed)
    t = Part(name, (cx, TERRAIN, cz))

    def limb(start, direction, length, radius, depth):
        direction = direction.normalized()
        end = start + direction * length
        up = Vector((0, 1, 0))
        turn = up.rotation_difference(direction).to_euler("XYZ")
        mid = (start + end) / 2
        t.cylinder(tuple(mid), radius, length * 1.04, BARK, rot=tuple(math.degrees(a) for a in turn),
                   top_radius=radius * 0.68, segments=7 if depth < 2 else 5)
        if depth >= 4 or radius < 0.012:
            return
        for _ in range(3 if depth else 2):
            bend = direction + Vector((rng.uniform(-0.75, 0.75), rng.uniform(-0.1, 0.5), rng.uniform(-0.75, 0.75)))
            limb(end, bend, length * rng.uniform(0.62, 0.82), radius * 0.62, depth + 1)

    limb(Vector((cx, TERRAIN - 0.05, cz)), Vector((0.12, 1, 0.06)), height * 0.36, 0.16 * height / 6, 0)
    t.build(root)


def build_yard_props(root):
    """A bench and an iron lamp post by the path. Unity lights the lamp (AtticAtmosphere)."""
    b = Part("House_Bench", (-2.5, TERRAIN, -12.4))
    x, z = -2.5, -12.4
    for k in range(3):
        b.box((x + k * 0.14 - 0.14, TERRAIN + 0.46, z), (0.12, 0.05, 1.6), WOOD_DARK, bevel=0.006)
    for dz in (-0.65, 0.65):
        b.box((x, TERRAIN + 0.22, z + dz), (0.45, 0.44, 0.08), TIMBER, bevel=0.008)
    b.build(root)

    lx, lz, h = 2.3, -16.2, 2.5
    lamp = Part("House_YardLamp", (lx, TERRAIN + h, lz))
    lamp.cylinder((lx, TERRAIN + h / 2, lz), 0.045, h, IRON, top_radius=0.03, segments=8)
    lamp.cylinder((lx, TERRAIN + 0.09, lz), 0.13, 0.18, IRON, top_radius=0.06, segments=8)
    lamp.box((lx, TERRAIN + h + 0.02, lz), (0.2, 0.03, 0.2), IRON)
    lamp.box((lx, TERRAIN + h + 0.17, lz), (0.15, 0.27, 0.15), LAMP_GLASS, bevel=0.002)
    lamp.cylinder((lx, TERRAIN + h + 0.37, lz), 0.14, 0.12, IRON, top_radius=0.02, segments=4, rot=(0, 45, 0))
    lamp.build(root)


def build_yard(root):
    with kept_random():
        build_yard_walls(root)
        build_annex(root)
        build_well(root, 3.9, -12.6)
        build_graves(root)
        build_yard_props(root)
        build_dead_tree(root, "House_DeadTree_1", -7.0, -13.2, 7.5, 3)
        build_dead_tree(root, "House_DeadTree_2", 7.4, -14.2, 5.5, 8)
        # Two tall old trees standing over the house.
        for i, (x, z, size) in enumerate(((-9.6, -4.2, 6.2), (8.2, 7.0, 6.8))):
            place_prop(root, "garden_plants", (x, TERRAIN, z), yaw=YARD.uniform(0, 360), scale=size,
                       names={"garden_plants": f"Garden_Tree_{i + 1}"}, reuse=True)


# ---------------------------------------------------------------- the old things

OLD = random.Random(29)  # their own dice: the room's other random choices stay as they were


def set_down(part, room, pos, yaw=0.0, tilt=0.0):
    """Builds a thing made round its own foot at the origin and sets it in the room, as place_prop does. Tipped over,
    it comes to rest on the floor (or whatever height `pos` gives)."""
    obj = part.build(room)
    obj.location = (TO_BLENDER @ Vector(pos).to_4d()).to_3d()
    obj.rotation_euler = Euler((math.radians(-tilt), 0, math.radians(-yaw)), "XYZ")
    bpy.context.view_layer.update()
    if tilt:
        low = min((obj.matrix_world @ v.co).z for v in obj.data.vertices)
        obj.location.z += pos[1] + 0.002 - low
    return obj


def turned_leg(a, b):
    a, b = Vector(a), Vector(b)
    return a, b, [(0, 0.012), (0.05, 0.016), (0.16, 0.019), (0.2, 0.013), (0.26, 0.02), (0.5, 0.024), (0.72, 0.019), (0.78, 0.024),
                  (0.84, 0.016), (1, 0.017)]


def build_old_chair(name, broken):
    """A spindle-back kitchen chair, its paint long gone. `broken` has lost a leg and a spindle."""
    p = Part(name)
    seat = [(0.22 * math.cos(a) * (1.0 + 0.1 * math.cos(2 * a)), 0.2 * math.sin(a) + 0.015 * math.cos(3 * a))
            for a in (i * math.tau / 20 for i in range(20))]
    p.slab(seat, 0.43, 0.465, GRIMY_PALE)
    p.box((0.03, 0.466, 0.02), (0.006, 0.002, 0.36), SHADOW, rot=(0, 3, 0), bevel=0)  # the seat has split
    feet = [(-0.21, 0.22), (0.21, 0.22), (-0.19, -0.2), (0.19, -0.2)]
    tops = [(-0.15, 0.14), (0.15, 0.14), (-0.14, -0.13), (0.14, -0.13)]
    for i, (f, t) in enumerate(zip(feet, tops)):
        a, b, prof = turned_leg((f[0], 0, f[1]), (t[0], 0.435, t[1]))
        if broken and i == 1:
            p.turned(a.lerp(b, 0.72), b, [(0, 0.012), (0.1, 0.02), (1, 0.017)], GRIMY_WOOD, segments=7)  # snapped off
            continue
        p.turned(a, b, prof, GRIMY_WOOD, segments=8)
    p.turned((-0.2, 0.2, 0.2), (-0.17, 0.2, -0.18), [(0, 0.011), (0.5, 0.015), (1, 0.011)], GRIMY_WOOD, segments=7)
    p.turned((0.17, 0.2, -0.18), (0.2, 0.2, 0.2), [(0, 0.011), (0.5, 0.015), (1, 0.011)], GRIMY_WOOD, segments=7)
    p.turned((-0.18, 0.15, -0.0), (0.05, 0.13, 0.02), [(0, 0.011), (0.6, 0.015), (0.95, 0.011), (1, 0.004)], GRIMY_PALE, segments=7)  # snapped
    top_y, back_z = 0.96, -0.24
    for i, x in enumerate((-0.15, -0.075, 0.0, 0.075, 0.15)):
        low = (x * 0.92, 0.46, -0.145)
        high = (x * 1.08, top_y - 0.06, back_z)
        if broken and i == 3:
            continue
        p.turned(low, high, [(0, 0.011), (0.2, 0.016), (0.45, 0.012), (0.75, 0.014), (1, 0.01)], GRIMY_WOOD, segments=7)
    crest = [(-0.235, top_y - 0.085), (0.235, top_y - 0.085), (0.25, top_y - 0.01), (0.15, top_y - 0.0), (0.06, top_y + 0.035),
             (-0.06, top_y + 0.035), (-0.15, top_y - 0.0), (-0.25, top_y - 0.01)]
    p.prism(crest, back_z - 0.014, back_z + 0.014, GRIMY_PALE)
    return p


def build_old_bookcase(name):
    """A tall dark bookcase, shelves bowing, its books faded and fallen, loose papers in the bottom."""
    p = Part(name)
    w, h, d, t = 0.7, 1.7, 0.3, 0.025
    for s in (-1, 1):
        p.box((s * (w - t) / 2, h / 2, 0), (t, h, d), GRIMY_PALE)
    p.box((0, h / 2, -d / 2 + 0.005), (w, h, 0.01), GRIMY_WOOD)
    p.box((0, h + 0.015, 0.012), (w + 0.1, 0.03, d + 0.05), GRIMY_WOOD)
    p.box((0, h + 0.042, 0.012), (w + 0.05, 0.024, d + 0.03), GRIMY_WOOD)
    p.box((0, 0.05, 0.005), (w + 0.03, 0.1, d + 0.01), GRIMY_WOOD)
    p.prism([(-w / 2 + t, h - 0.12), (w / 2 - t, h - 0.12), (w / 2 - t, h), (-w / 2 + t, h)], d / 2 - 0.018, d / 2 - 0.002, GRIMY_WOOD)
    shelves = (0.12, 0.42, 0.71, 1.0, 1.29)
    inner = w - 2 * t
    for k, y in enumerate(shelves):
        sag = 1.4 if k == 3 else 0.0  # this one has worked loose at one end
        p.box((0, y + 0.01, 0.008), (inner, 0.02, d - 0.03), GRIMY_WOOD, rot=(0, 0, sag))
    for k, y in enumerate(shelves):
        if k == 0:
            continue  # the bottom shelf only has papers on it
        base = y + 0.02 + (0.0 if k != 3 else 0.004)
        x = -inner / 2 + 0.008
        room = inner / 2 - 0.02
        while x < room:
            th = OLD.uniform(0.018, 0.042)
            if OLD.random() < 0.13:  # a gap where one has gone
                x += OLD.uniform(0.04, 0.09)
                continue
            bh, bd = OLD.uniform(0.16, 0.24), OLD.uniform(0.13, 0.19)
            lean = OLD.choice((0, 0, 0, 0, 7, 12)) if x > -inner / 2 + 0.1 else 0
            c = (x + th / 2, base + bh / 2, 0.01)
            p.box(c, (th, bh, bd), OLD.choice(OLD_BOOKS), rot=(0, 0, -lean), bevel=0.002)
            p.box((c[0], c[1], c[2] - 0.004), (th * 0.8, bh - 0.008, bd), OLD_PAPER, rot=(0, 0, -lean), bevel=0)
            x += th + 0.002 + bh * math.sin(math.radians(lean)) * 0.4
    # A pile lying down on top, and the loose papers.
    for k in range(3):
        p.box((-0.12 + k * 0.012, 1.325 + k * 0.03, 0.01), (0.22, 0.028, 0.16), OLD_BOOKS[k], rot=(0, 8 * k - 8, 0), bevel=0.002)
    for k in range(7):
        p.box((OLD.uniform(-0.2, 0.2), 0.142 + k * 0.0025, OLD.uniform(-0.04, 0.05)), (0.17, 0.0015, 0.22), OLD_PAPER,
              rot=(OLD.uniform(-2, 2), OLD.uniform(-40, 40), OLD.uniform(-2, 2)), bevel=0)
    return p


def build_old_box(name, w, h, d, flaps_open):
    """A cardboard carton gone soft with age, its tape peeling, the handling marks faded."""
    p = Part(name)
    p.box((0, h / 2, 0), (w, h, d), CARDBOARD, bevel=0.012)
    f = 0.006
    if flaps_open:
        p.box((0, h + 0.001, 0), (w - 0.03, 0.004, d - 0.03), SHADOW, bevel=0)  # dark inside
        p.box((-w * 0.25, h + 0.018, 0), (w * 0.5, f, d), CARDBOARD, rot=(0, 0, -7), bevel=0.002)
        p.box((w * 0.33, h + 0.05, d * 0.02), (w * 0.42, f, d * 0.92), CARDBOARD, rot=(5, 4, 19), bevel=0.002)
        p.box((w * 0.02, h + 0.04, -d * 0.36), (w * 0.82, f, d * 0.34), CARDBOARD, rot=(-14, 2, 0), bevel=0.002)
    else:
        p.box((-w * 0.25, h + 0.004, 0), (w * 0.5 - 0.004, f, d), CARDBOARD, bevel=0.002)
        p.box((w * 0.25, h + 0.006, 0), (w * 0.5 - 0.004, f, d), CARDBOARD, rot=(0, 0, 1.5), bevel=0.002)
    p.box((w * 0.2, h + 0.012, -d * 0.2), (w * 0.3, 0.002, d * 0.26), OLD_PAPER, rot=(0, 4, 0), bevel=0)  # a faded label
    # Faded stencil marks on the front: this way up, and a glass.
    z = d / 2 + 0.0005
    n = (0, 0, 1)
    x0, y0, size = -w * 0.32, h * 0.2, min(h, w) * 0.28
    for k in (0.0, 0.42):
        c = x0 + k * size
        p.stroke((c, y0, z), (c, y0 + size * 0.8, z), n, SHADOW, width=0.006)
        p.stroke((c, y0 + size * 0.8, z), (c - size * 0.13, y0 + size * 0.55, z), n, SHADOW, width=0.006)
        p.stroke((c, y0 + size * 0.8, z), (c + size * 0.13, y0 + size * 0.55, z), n, SHADOW, width=0.006)
    return p


def sack_shape(height, width, slump, seed):
    wob = lambda k, a, f, s: math.sin(a * f + k * 7.0 + s + seed)
    def shape(k, a):
        body = min(1.0, (k / 0.12) ** 0.5) * (1.0 - 0.72 * ((k / 0.84) ** 3.2))  # a broad foot, drawn in at the shoulders
        r = width * max(body, 0.2)
        if k > 0.86:
            r = width * (0.2 + (k - 0.86) * 1.5 - max(0.0, k - 0.95) * 5.5)  # the gathered top, bunched above the tie
        gather = max(0.0, k - 0.45) * 0.55  # creases run down from the tie
        r *= 1.0 + 0.1 * wob(k, a, 2, 0.0) + 0.06 * wob(k, a, 3, 2.0) + gather * math.sin(a * 11 + seed) + 1.6 * max(0.0, k - 0.86) * math.sin(a * 5 + k * 9)
        bow = slump * (k ** 1.6) * height * 0.5
        side = 1.0 - 0.2 * slump * math.sin(a)
        y = height * k * (1.0 - 0.32 * slump)
        return Vector((math.cos(a) * r + bow, y, math.sin(a) * r * side))
    return shape


def build_old_sack(name, height, width, slump, seed):
    """A burlap sack tied at the neck, sagging where what is in it has settled."""
    p = Part(name)
    p.lump(sack_shape(height, width, slump, seed), BURLAP)
    k = 0.86
    c = sack_shape(height, width, slump, seed)(k, 0.0)
    p.torus((c.x - width * 0.2, c.y, c.z), width * 0.21, 0.007, TWINE, rot=(90, 0, 0), segments=12, sides=5)
    return p


def build_old_tv(name):
    """An old valve television in a wooden case, its plate rubbed blank, the screen under decades of dust."""
    p = Part(name)
    w, h, d = 0.52, 0.48, 0.42
    p.box((0, h / 2, 0), (w, h, d), GRIMY_CASE, bevel=0.022)
    fy, fz = h / 2 + 0.03, d / 2 + 0.002
    sw, sh = w - 0.1, h - 0.17
    for y, s in ((fy + sh / 2 + 0.008, (sw + 0.03, 0.02, 0.012)), (fy - sh / 2 - 0.008, (sw + 0.03, 0.02, 0.012))):
        p.box((0, y, fz), s, TARNISH, bevel=0.004)
    for x in (-1, 1):
        p.box((x * (sw / 2 + 0.008), fy, fz), (0.02, sh + 0.03, 0.012), TARNISH, bevel=0.004)
    def bulge(u, v):
        return 0.014 * (1 - (2 * u) ** 2) * (1 - (2 * v) ** 2)
    p.sheet((0, fy, fz + 0.001), (sw, sh), SCREEN, bulge, rot=(90, 0, 0), segments=(10, 8), thickness=0.004)
    py = 0.055
    p.box((0, py, fz + 0.001), (w - 0.1, 0.075, 0.01), TARNISH, bevel=0.003)
    for x in (-0.17, 0.17):
        p.cylinder((x, py, fz + 0.016), 0.024, 0.024, TARNISH, axis="z", segments=14)
        p.cylinder((x, py, fz + 0.032), 0.017, 0.012, SHADOW, axis="z", segments=12)
    p.box((0, py + 0.016, fz + 0.007), (0.1, 0.022, 0.003), SHADOW, bevel=0)  # the name wore off it years ago
    for i in range(4):
        p.box((-0.045 + i * 0.03, py - 0.018, fz + 0.009), (0.02, 0.012, 0.008), GRIMY_WOOD, bevel=0.002)
    for i in range(8):
        p.box((0, 0.16 + i * 0.03, -d / 2 - 0.002), (w * 0.6, 0.012, 0.006), SHADOW, bevel=0)
    p.turned((0.12, h - 0.005, -0.1), (0.2, h + 0.48, -0.16), [(0, 0.008), (0.03, 0.008), (0.04, 0.0035), (1, 0.002)], DIRTY_METAL, segments=5)
    return p


def build_old_gramophone(name):
    """A wind-up gramophone: a stepped dark case, a dusty record, a tarnished horn gone green at the seams."""
    p = Part(name)
    p.box((0, 0.02, 0), (0.42, 0.04, 0.42), GRIMY_WOOD, bevel=0.008)
    p.box((0, 0.05, 0), (0.39, 0.024, 0.39), GRIMY_PALE, bevel=0.006)
    p.box((0, 0.14, 0), (0.34, 0.16, 0.34), GRIMY_CASE, bevel=0.006)
    p.box((0, 0.232, 0), (0.37, 0.026, 0.37), GRIMY_WOOD, bevel=0.008)
    for s in (-1, 1):
        p.box((0, 0.14, s * 0.173), (0.24, 0.1, 0.008), GRIMY_WOOD, bevel=0.002)
        p.box((s * 0.173, 0.14, 0), (0.008, 0.1, 0.24), GRIMY_WOOD, bevel=0.002)
        for t in (-1, 1):
            p.turned((s * 0.168, 0.065, t * 0.168), (s * 0.168, 0.215, t * 0.168),
                     [(0, 0.01), (0.2, 0.015), (0.5, 0.01), (0.8, 0.015), (1, 0.01)], GRIMY_WOOD, segments=7)
    top = 0.245
    p.cylinder((0, top + 0.008, 0), 0.15, 0.016, DIRTY_METAL, segments=28)
    p.cylinder((0, top + 0.018, 0), 0.142, 0.004, OLD_RECORD, segments=28)
    p.cylinder((0, top + 0.021, 0), 0.046, 0.002, OLD_PAPER, segments=14)
    p.cylinder((0, top + 0.03, 0), 0.005, 0.02, TARNISH, segments=6)
    # The crank on the side, and the arm: up from the back corner, over, and into the horn's throat.
    p.cylinder((0.22, 0.16, 0), 0.008, 0.08, DIRTY_METAL, axis="x", segments=6)
    p.cylinder((0.26, 0.2, 0), 0.007, 0.09, DIRTY_METAL, segments=6)
    p.cylinder((0.26, 0.25, 0.02), 0.012, 0.05, GRIMY_WOOD, axis="z", segments=8)
    arm = Vector((-0.14, top, -0.14))
    elbow = arm + Vector((0, 0.13, 0))
    p.cylinder(arm + Vector((0, 0.012, 0)), 0.03, 0.024, TARNISH, segments=12)
    p.turned(arm, elbow, [(0, 0.016), (1, 0.016)], DIRTY_METAL, segments=8)
    p.turned(elbow, (0.03, top + 0.075, 0.09), [(0, 0.015), (0.8, 0.011), (0.92, 0.02), (1, 0.016)], DIRTY_METAL, segments=8)
    throat = elbow + Vector((0, 0.01, 0))
    mouth = throat + Vector((-0.1, 0.33, 0.55))
    ribs = [0.018, 0.024, 0.038, 0.062, 0.1, 0.16, 0.25, 0.3]
    along = [0.0, 0.22, 0.42, 0.6, 0.74, 0.86, 0.96, 1.0]
    outside = list(zip(along, ribs))
    p.turned(throat, mouth, outside + [(k, max(r - 0.006, 0.004)) for k, r in reversed(outside)], TARNISH, segments=20)
    axis = (mouth - throat).normalized()
    x = axis.orthogonal().normalized()
    y = axis.cross(x)
    turn = axis.to_track_quat("Z", "Y").to_euler()
    p.torus(mouth, 0.3, 0.012, TARNISH, rot=[math.degrees(c) for c in turn], segments=28, sides=6)
    for i in range(8):  # the seams between the horn's petals
        a = i * math.tau / 8
        for k0, k1 in zip(range(3, 7), range(4, 8)):
            d = x * math.cos(a) + y * math.sin(a)
            p.stroke(throat + (mouth - throat) * along[k0] + d * ribs[k0], throat + (mouth - throat) * along[k1] + d * ribs[k1], d, SHADOW, width=0.005)
    return p


def build_old_webs(room):
    """Old webs strung off the new arrivals, into the corners."""
    webs = Part("Decor_OldWebs")
    v = Vector
    for a, b, c in (((1.0, 1.72, -1.78), (1.65, 1.72, -1.45), (1.66, 1.9, -1.78)),):
        webs.triangle(v(a), v(b), v(c), COBWEB)
    webs.build(room)


def build_old_things(root):
    set_down(build_old_bookcase("Prop_OldBookcase"), root, (1.32, 0, Z_FRONT + 0.17))
    set_down(build_old_chair("Prop_OldChair", False), root, (-0.72, 0, 3.15), yaw=205)
    set_down(build_old_chair("Prop_OldChairFallen", True), root, (1.12, 0, 2.5), yaw=100, tilt=90)
    set_down(build_old_box("Prop_OldBox_Under", 0.6, 0.4, 0.44, False), root, (1.88, 0, -0.3), yaw=-82)
    set_down(build_old_box("Prop_OldBox_Stack", 0.56, 0.36, 0.42, False), root, (0.66, 0, 3.15), yaw=14)
    set_down(build_old_box("Prop_OldBox_Top", 0.42, 0.26, 0.32, True), root, (0.64, 0.37, 3.14), yaw=-16)
    set_down(build_old_sack("Prop_OldSack_1", 0.8, 0.27, 0.15, 1.0), root, (-1.78, 0, 1.4), yaw=30)
    set_down(build_old_sack("Prop_OldSack_2", 0.6, 0.27, 0.5, 3.7), root, (1.5, 0, 2.78), yaw=200)
    set_down(build_old_tv("Prop_OldTelevision"), root, (HALF_W - 0.3, 0.35, 2.98), yaw=-96)
    set_down(build_old_gramophone("Prop_OldGramophone"), root, (1.88, 0.415, -0.3), yaw=-62)
    build_old_webs(root)


# ---------------------------------------------------------------- run


def main():
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    root = empty("Attic_Model")
    build_room(root)
    build_chest(root)
    build_lantern(root)
    build_lantern_details(root)
    build_house(root)
    build_ground_floor(root)
    build_garden(root)
    build_yard(root)
    build_portal(root)
    build_cloth(root)
    build_crate(root, "Crate_Left", (-1.05, 0.25, 1.45), (0.5, 0.5, 0.5))
    build_crate(root, "Crate_Right", (0.95, 0.2, 1.3), (0.5, 0.4, 0.5))
    for i, spot in enumerate(BOOK_SPOTS):
        build_book(root, i, spot)
    build_old_things(root)

    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=False, object_types={"EMPTY", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
    with open(os.path.splitext(OUT_FBX)[0] + ".materials.json", "w") as f:
        json.dump({"materials": list(MATERIALS.values())}, f, indent=1)
    # Where every object belongs, in Unity terms: Apply Attic Model puts them exactly there, so a quirk in how the
    # FBX axes are converted can't tip anything over.
    to_unity = TO_BLENDER.to_3x3().inverted()
    placements = []
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        loc, rot, scale = obj.matrix_world.decompose()
        q = (to_unity @ rot.to_matrix() @ TO_BLENDER.to_3x3()).to_quaternion()
        placements.append({"name": obj.name, "position": list(unity_from_blender(loc)),
                           "rotation": [q.x, q.y, q.z, q.w], "scale": scale.x})
    with open(os.path.splitext(OUT_FBX)[0] + ".placements.json", "w") as f:
        json.dump({"objects": placements}, f, indent=1)
    bpy.data.meshes.remove(_SCRATCH_MESH)
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backups
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, relative_remap=True)
    bpy.ops.file.make_paths_relative()  # textures resolve on any laptop that has the project
    bpy.ops.wm.save_mainfile()

    tris = sum(len(p.vertices) - 2 for o in bpy.data.objects if o.type == "MESH" for p in o.data.polygons)
    print(f"[Attic] {len([o for o in bpy.data.objects if o.type == 'MESH'])} objects, ~{tris} triangles")
    print(f"[Attic] wrote {OUT_FBX}")
    print(f"[Attic] wrote {OUT_BLEND}")


main()
