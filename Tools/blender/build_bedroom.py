"""
Builds ARCANE STRIKE's Chapter 2 bedroom in Blender and exports it for Unity.

    blender -b -P Tools/blender/build_bedroom.py            (from the Unity project folder)

Writes Assets/_Project/Art/Environment/Bedroom/Models/Bedroom.fbx (+ .materials.json, .placements.json) and
Tools/blender/Bedroom.blend. Then in Unity: Arcane Strike → Chapter 2 → 2 · Create Bedroom Scene.

It reuses the attic script's tools (materials on the attic's CC0 textures, Part, the Poly Haven props) by running
build_attic.py without its main(). Unity coordinates, metres: the room runs x -2.2..2.2, z -1.4..3.6, floor y 0,
ceiling 2.6. The player sits on the edge of the bed near (-1.1, 0, 1.4) facing +Z: the desk ahead on the left, the
wardrobe ahead, and the dark corner beside it (where Ember appears) ahead on the right; the window with its blinds
is in the right-hand wall, so the moonlight falls across the floor in stripes. Walls, floor and ceiling are their own
objects (Room_*) because the room shatters at the end of the scene.
"""

import json
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_FBX = os.path.join(PROJECT, "Assets/_Project/Art/Environment/Bedroom/Models/Bedroom.fbx")
OUT_BLEND = os.path.join(HERE, "Bedroom.blend")

# The attic script's tools, without building the attic.
_argv = sys.argv
sys.argv = [a for a in sys.argv if a != "--"][:1]
with open(os.path.join(HERE, "build_attic.py")) as f:
    source = f.read()
source = source[:source.rindex("\nmain()")]
A = {"__file__": os.path.join(HERE, "build_attic.py"), "__name__": "build_attic_tools"}
exec(compile(source, "build_attic.py", "exec"), A)
sys.argv = _argv

import bpy  # noqa: E402  (after the attic script reset the scene)
from mathutils import Euler, Matrix, Vector  # noqa: E402

Part, empty, place_prop, MATERIALS, TO_BLENDER = A["Part"], A["empty"], A["place_prop"], A["MATERIALS"], A["TO_BLENDER"]
unity_from_blender = A["unity_from_blender"]
rng = random.Random(23)

X0, X1, Z0, Z1, H = -2.2, 2.2, -1.4, 3.6, 2.6
T = 0.12  # wall thickness
WINDOW = (0.5, 1.7, 0.85, 2.15)    # in the right wall: z0, z1, y0, y1
DOOR = (1.15, 2.0, 0.0, 2.05)      # in the back wall: x0, x1, y0, y1


def mat(name, color, roughness=0.8, metallic=0.0, **kw):
    """The attic's material(), named for the bedroom."""
    m = A["material"]("Bedroom" + name, color, roughness, metallic, **kw)  # (a name no attic material has)
    entry = MATERIALS.pop(m.name)
    m.name = "M_Bedroom_" + name
    entry["name"] = m.name
    MATERIALS[m.name] = entry
    return m


WALL = mat("Plaster", (0.72, 0.76, 0.82), 0.95, tex="Plaster007", tile=1.5)
CEILING = mat("Ceiling", (0.85, 0.86, 0.88), 0.95, tex="Plaster007", tile=1.8)
FLOOR = mat("Floor", (0.78, 0.66, 0.55), 0.7, tex="Wood049", tile=1.1)
TRIM = mat("Trim", (0.55, 0.42, 0.32), 0.6, tex="Wood060", tile=0.8)
WOOD = mat("Wood", (0.8, 0.66, 0.52), 0.65, tex="Wood049", tile=0.8)
DARK_WOOD = mat("DarkWood", (0.5, 0.38, 0.3), 0.6, tex="Wood060", tile=0.8)
MATTRESS = mat("Mattress", (0.86, 0.86, 0.84), 1.0, tex="Fabric085", tile=0.4)
DUVET = mat("Duvet", (0.42, 0.52, 0.75), 1.0, tex="Fabric061", tile=0.3)
SHEET = mat("Sheet", (0.9, 0.9, 0.88), 1.0, tex="Fabric061", tile=0.3)
PILLOW = mat("Pillow", (0.92, 0.92, 0.9), 1.0, tex="Fabric061", tile=0.25)
RUG = mat("Rug", (0.5, 0.17, 0.13), 1.0, tex="Fabric085", tile=0.3)
SHIRT = mat("Shirt", (0.25, 0.27, 0.3), 1.0, tex="Fabric061", tile=0.2)
BLIND = mat("Blind", (0.86, 0.85, 0.8), 0.5)
GLASS = mat("NightGlass", (0.1, 0.13, 0.2), 0.05, alpha=0.3)
BRASS = mat("Brass", (1.0, 0.78, 0.4), 0.35, 0.8, tex="Metal063", tile=0.3)
PLASTIC = mat("Plastic", (0.08, 0.08, 0.09), 0.45)
SCREEN = mat("ClockFace", (0.02, 0.01, 0.01), 0.2)
LAMPSHADE = mat("Lampshade", (0.85, 0.78, 0.62), 1.0, tex="Fabric061", tile=0.3)
PAPER = mat("Poster", (0.75, 0.72, 0.66), 0.9, tex="Paper006", tile=0.6)
POSTER_INK = mat("PosterInk", (0.15, 0.22, 0.4), 0.9, tex="Paper006", tile=0.6)
BOOKS = [mat("Book" + n, c, 0.85, tex="Paper006", tile=0.3) for n, c in
         (("Red", (0.6, 0.15, 0.12)), ("Blue", (0.15, 0.25, 0.5)), ("Green", (0.18, 0.4, 0.25)), ("Ochre", (0.7, 0.52, 0.2)))]
MUG = mat("Mug", (0.85, 0.82, 0.75), 0.3)


# ---------------------------------------------------------------- the room


def build_room(root):
    """Walls, floor and ceiling, each its own object; skirting and the window and door frames."""
    wall = A["wall"]
    floor = Part("Room_Floor")
    floor.box(((X0 + X1) / 2, -0.03, (Z0 + Z1) / 2), (X1 - X0, 0.06, Z1 - Z0), FLOOR, bevel=0, world_uv=True)
    floor.build(root)
    ceiling = Part("Room_Ceiling")
    ceiling.box(((X0 + X1) / 2, H + 0.03, (Z0 + Z1) / 2), (X1 - X0 + 2 * T, 0.06, Z1 - Z0 + 2 * T), CEILING, bevel=0, world_uv=True)
    ceiling.build(root)
    for name, axis, at, u0, u1, holes in (
            ("Room_WallLeft", "x", X0 - T / 2, Z0 - T, Z1 + T, ()),
            ("Room_WallRight", "x", X1 + T / 2, Z0 - T, Z1 + T, (WINDOW,)),
            ("Room_WallFront", "z", Z1 + T / 2, X0, X1, ()),
            ("Room_WallBack", "z", Z0 - T / 2, X0, X1, (DOOR,))):
        p = Part(name)
        wall(p, axis, at, u0, u1, 0.0, H, T, WALL, holes)
        # Skirting along the foot of the wall (not across the door).
        if axis == "x":
            inside = at - T / 2 * (1 if at > 0 else -1) - 0.01 * (1 if at > 0 else -1)
            p.box((inside, 0.05, (Z0 + Z1) / 2), (0.02, 0.1, Z1 - Z0), TRIM, bevel=0.003)
        else:
            inside = at - (T / 2 + 0.01) * (1 if at > 0 else -1)
            spans = [(X0, X1)] if not holes else [(X0, DOOR[0] - 0.08), (DOOR[1] + 0.08, X1)]
            for a, b in spans:
                p.box(((a + b) / 2, 0.05, inside), (b - a, 0.1, 0.02), TRIM, bevel=0.003)
        p.build(root)

    # The window: frame, sill and dark glass (the night beyond); the blinds are their own object.
    z0, z1, y0, y1 = WINDOW
    win = Part("Decor_Window")
    at = X1 + T / 2
    for y in (y0, y1):
        win.box((at, y, (z0 + z1) / 2), (T + 0.04, 0.06, z1 - z0 + 0.12), TRIM)
    for z in (z0, z1):
        win.box((at, (y0 + y1) / 2, z), (T + 0.04, y1 - y0 + 0.12, 0.06), TRIM)
    win.box((at, (y0 + y1) / 2, (z0 + z1) / 2), (0.04, y1 - y0, 0.04), TRIM)
    win.box((X1 - 0.05, y0 - 0.02, (z0 + z1) / 2), (0.16, 0.035, z1 - z0 + 0.2), TRIM)  # sill
    win.box((at + 0.02, (y0 + y1) / 2, (z0 + z1) / 2), (0.01, y1 - y0, z1 - z0), GLASS)
    win.build(root)

    # The door: frame and a closed panelled door with a brass knob.
    x0, x1, _, y1 = DOOR
    door = Part("Decor_Door")
    at = Z0 - T / 2
    for x in (x0 - 0.04, x1 + 0.04):
        door.box((x, y1 / 2, at), (0.08, y1 + 0.04, T + 0.04), TRIM)
    door.box(((x0 + x1) / 2, y1 + 0.04, at), (x1 - x0 + 0.16, 0.08, T + 0.04), TRIM)
    door.box(((x0 + x1) / 2, y1 / 2, at + 0.01), (x1 - x0, y1, 0.04), DARK_WOOD)
    for py in (0.55, 1.45):
        door.box(((x0 + x1) / 2, py, at + 0.035), (x1 - x0 - 0.2, 0.62, 0.012), WOOD)
    door.cylinder((x0 + 0.08, 1.0, at + 0.06), 0.028, 0.05, BRASS, axis="z")
    door.build(root)


def build_blinds(root):
    """Venetian blinds over the window, slats tilted half open so the moon comes through in stripes; one bent."""
    z0, z1, y0, y1 = WINDOW
    p = Part("Decor_Blinds")
    x = X1 - 0.07
    width = z1 - z0 + 0.08
    p.box((x, y1 + 0.03, (z0 + z1) / 2), (0.06, 0.04, width + 0.02), BLIND, bevel=0.004)  # head rail
    count = 22
    for i in range(count):
        y = y1 - 0.04 - i * (y1 - y0 - 0.06) / (count - 1)
        tilt = 38 + rng.uniform(-3, 3)
        if i == 15:
            # A bent slat, bowed down in the middle (someone peeked through it).
            for k in range(6):
                seg = width / 6
                z = z0 - 0.04 + seg * (k + 0.5)
                sag = -0.025 * math.sin(math.pi * (k + 0.5) / 6)
                p.box((x, y + sag, z), (0.05, 0.0035, seg + 0.002), BLIND, rot=(0, 0, tilt + 12 * math.sin(math.pi * (k + 0.5) / 6)), bevel=0)
            continue
        p.box((x, y, (z0 + z1) / 2), (0.05, 0.0035, width), BLIND, rot=(0, 0, tilt), bevel=0)
    p.box((x, y0 - 0.0, (z0 + z1) / 2), (0.05, 0.02, width), BLIND, bevel=0.003)  # bottom rail
    for z in (z0 + 0.15, z1 - 0.15):
        p.cylinder((x - 0.005, (y0 + y1) / 2, z), 0.0015, y1 - y0, BLIND, segments=6)  # ladder cords
    p.cylinder((x - 0.035, y0 + 0.2, z1 + 0.02), 0.002, (y1 - y0) * 0.7, PLASTIC, segments=6)  # pull cord
    p.build(root)


# ---------------------------------------------------------------- furniture


def build_bed(root):
    """A plain wooden bed against the left wall (headboard at -x), slept in: the duvet thrown back."""
    p = Part("Prop_Bed", (-1.2, 0, 0.5))
    bx0, bx1, bz0, bz1 = -2.17, -0.2, -0.2, 1.2
    cx, cz, w, l = (bx0 + bx1) / 2, (bz0 + bz1) / 2, bz1 - bz0, bx1 - bx0
    for x in (bx0 + 0.04, bx1 - 0.04):
        for z in (bz0 + 0.04, bz1 - 0.04):
            p.box((x, 0.15, z), (0.07, 0.3, 0.07), DARK_WOOD)
    for z in (bz0 + 0.03, bz1 - 0.03):
        p.box((cx, 0.27, z), (l, 0.16, 0.05), DARK_WOOD)  # side rails
    p.box((bx0 + 0.02, 0.62, cz), (0.05, 1.0, w + 0.04), DARK_WOOD)   # headboard
    p.box((bx0 + 0.02, 1.13, cz), (0.08, 0.05, w + 0.1), DARK_WOOD)
    p.box((bx1 - 0.02, 0.38, cz), (0.05, 0.42, w + 0.04), DARK_WOOD)  # footboard
    p.box((cx, 0.36, cz), (l - 0.08, 0.04, w - 0.08), WOOD, bevel=0)  # slats board
    p.box((cx, 0.47, cz), (l - 0.1, 0.18, w - 0.1), MATTRESS, bevel=0.04)
    p.sheet((cx, 0.565, cz), (l - 0.08, w - 0.06), SHEET, lambda u, v: 0.006 * math.sin(u * 17) * math.cos(v * 13), segments=(16, 12))

    # The duvet, thrown back towards the wall side and the foot, rumpled.
    def duvet_folds(u, v):
        ridge = 0.05 * math.exp(-((u + 0.15) ** 2) * 30)
        return 0.03 * math.sin(u * 9 + v * 4) * math.cos(v * 7) + ridge + 0.015 * math.sin(u * 23)
    p.sheet((cx + 0.25, 0.6, cz - 0.15), (l - 0.55, w - 0.1), DUVET, duvet_folds, thickness=0.03, segments=(20, 14))
    p.box((cx + 0.25, 0.575, cz - 0.15), (l - 0.6, 0.03, w - 0.16), DUVET, bevel=0.012)

    # Two pillows by the headboard, one knocked askew.
    for z, tilt, squash in ((cz - 0.32, 4, 1.0), (cz + 0.3, -9, 0.85)):
        def pillow(k, a, z=z, squash=squash):
            r = math.sin(math.pi * k) ** 0.6
            return Vector((bx0 + 0.25 + 0.17 * r * math.cos(a), 0.58 + 0.12 * k * squash, z + 0.3 * r * math.sin(a)))
        p.lump(pillow, PILLOW, rings=10, sides=16)
    p.build(root)


def build_nightstand_things(root):
    """On the nightstand: the alarm clock (Unity writes 23:42 on its face), a lamp (off) and a glass."""
    top = 0.70
    clock = Part("Decor_AlarmClock", (-1.93, top, 1.48))
    clock.box((-1.93, top + 0.045, 1.48), (0.07, 0.09, 0.16), PLASTIC, bevel=0.01)
    clock.box((-1.895, top + 0.05, 1.48), (0.004, 0.055, 0.13), SCREEN, bevel=0)
    clock.build(root)
    lamp = Part("Decor_BedLamp")
    lamp.cylinder((-2.0, top + 0.015, 1.72), 0.07, 0.03, BRASS)
    lamp.cylinder((-2.0, top + 0.17, 1.72), 0.01, 0.3, BRASS, segments=8)
    lamp.cylinder((-2.0, top + 0.36, 1.72), 0.13, 0.17, LAMPSHADE, top_radius=0.08)
    lamp.build(root)
    glass = Part("Decor_WaterGlass")
    glass.cylinder((-1.84, top + 0.055, 1.66), 0.032, 0.11, GLASS, top_radius=0.036, segments=14)
    glass.build(root)


def build_desk(root):
    """A study desk against the front wall on the left: a closed laptop, books, a mug, a desk lamp."""
    x0, x1, z0, z1, top = -2.12, -0.85, 3.0, 3.56, 0.76
    p = Part("Prop_Desk", ((x0 + x1) / 2, 0, (z0 + z1) / 2))
    p.box(((x0 + x1) / 2, top - 0.02, (z0 + z1) / 2), (x1 - x0, 0.04, z1 - z0), WOOD)
    p.box((x0 + 0.22, (top - 0.04) / 2, (z0 + z1) / 2), (0.42, top - 0.04, z1 - z0 - 0.02), WOOD)  # drawers
    for y in (0.16, 0.4, 0.62):
        p.box((x0 + 0.22, y, z0 + 0.005), (0.38, 0.2, 0.01), DARK_WOOD)
        p.box((x0 + 0.22, y + 0.05, z0 - 0.01), (0.1, 0.015, 0.02), BRASS)
    for z in (z0 + 0.03, z1 - 0.03):
        p.box((x1 - 0.03, (top - 0.04) / 2, z), (0.05, top - 0.04, 0.05), WOOD)
    p.box((x1 - 0.03, 0.12, (z0 + z1) / 2), (0.04, 0.04, z1 - z0 - 0.06), WOOD)
    p.build(root)

    things = Part("Decor_DeskThings")
    things.box((-1.3, top + 0.01, 3.24), (0.34, 0.012, 0.24), PLASTIC, rot=(0, 8, 0), bevel=0.004)          # laptop
    things.box((-1.3, top + 0.022, 3.24), (0.33, 0.01, 0.23), mat("Laptop", (0.45, 0.46, 0.48), 0.35, 0.6), rot=(0, 8, 0), bevel=0.003)
    y = top
    for i, (th, sx, sz, yaw) in enumerate(((0.035, 0.24, 0.17, 3), (0.03, 0.22, 0.15, -7), (0.045, 0.26, 0.19, 12))):
        things.box((-1.85, y + th / 2, 3.25), (sx, th, sz), BOOKS[i % len(BOOKS)], rot=(0, yaw, 0), bevel=0.003)
        y += th
    things.cylinder((-1.05, top + 0.05, 3.1), 0.04, 0.1, MUG)
    things.torus((-1.0, top + 0.05, 3.1), 0.025, 0.007, MUG, rot=(0, 90, 0), segments=12, sides=6)
    things.cylinder((-2.0, top + 0.012, 3.42), 0.07, 0.025, PLASTIC)                       # desk lamp
    things.cylinder((-2.0, top + 0.2, 3.42), 0.008, 0.36, PLASTIC, segments=8, rot=(14, 0, 0))
    things.cylinder((-2.0, top + 0.38, 3.33), 0.07, 0.12, PLASTIC, top_radius=0.03, rot=(-30, 0, 0))
    things.build(root)


def build_shelf(root):
    """A wall shelf over the desk with a row of books."""
    p = Part("Decor_Shelf")
    p.box((-1.5, 1.5, 3.47), (1.0, 0.025, 0.22), DARK_WOOD)
    for x in (-1.9, -1.1):
        p.box((x, 1.45, 3.55), (0.02, 0.1, 0.06), BRASS)
    x = -1.95
    while x < -1.2:
        th = rng.uniform(0.025, 0.05)
        h = rng.uniform(0.17, 0.26)
        lean = 0 if rng.random() > 0.15 else 12
        p.box((x + th / 2, 1.513 + h / 2, 3.47), (th, h, rng.uniform(0.13, 0.18)), rng.choice(BOOKS), rot=(0, 0, -lean), bevel=0.002)
        x += th + 0.003
    p.build(root)


def build_decor(root):
    """Posters, the ceiling light (off), a rug, a shirt on the floor, a backpack."""
    poster = Part("Decor_Posters")
    poster.box((-1.5, 2.0, Z1 - 0.005), (0.5, 0.68, 0.006), PAPER, bevel=0)
    for k in range(5):  # a mountain-and-moon print, in blocks
        poster.box((-1.5 + (k - 2) * 0.08, 1.82 + 0.05 * (2 - abs(k - 2)), Z1 - 0.01), (0.08, 0.12 + 0.1 * (2 - abs(k - 2)), 0.004), POSTER_INK, bevel=0)
    poster.cylinder((-1.38, 2.18, Z1 - 0.01), 0.06, 0.004, MUG, axis="z")
    poster.box((X0 + 0.005, 1.65, 2.2), (0.006, 0.6, 0.45), POSTER_INK, bevel=0)
    poster.box((X0 + 0.009, 1.65, 2.2), (0.004, 0.52, 0.37), PAPER, bevel=0)
    poster.build(root)

    light = Part("Decor_CeilingLight")
    light.cylinder((0.1, H - 0.02, 1.0), 0.07, 0.03, BRASS)
    light.cylinder((0.1, H - 0.2, 1.0), 0.005, 0.34, PLASTIC, segments=6)
    light.cylinder((0.1, H - 0.42, 1.0), 0.13, 0.16, LAMPSHADE, top_radius=0.22)
    light.build(root)

    rug = Part("Decor_Rug")
    rug.sheet((0.35, 0.006, 1.65), (2.0, 1.4), RUG, lambda u, v: 0.004 * math.sin(u * 30) + (0.02 if u > 0.44 else 0) * math.sin(v * 9) ** 2,
              thickness=0.008, segments=(20, 14))
    rug.build(root)

    shirt = Part("Decor_Shirt")
    shirt.sheet((-0.35, 0.02, 1.95), (0.5, 0.42), SHIRT, lambda u, v: 0.03 * math.sin(u * 11 + 1) * math.cos(v * 8) + 0.02 * math.exp(-(u * u + v * v) * 12),
                rot=(0, 37, 0), thickness=0.006, segments=(12, 10))
    shirt.build(root)

    bag = Part("Decor_Backpack")
    def pack(k, a):
        r = math.sin(math.pi * min(1.0, k * 1.15)) ** 0.5
        return Vector((-0.62 + 0.17 * r * math.cos(a), 0.44 * k, 3.35 + 0.1 * r * math.sin(a)))
    bag.lump(pack, DUVET, rings=10, sides=14)
    bag.build(root)


def build_props(root):
    place_prop(root, "ClassicNightstand_01", (-1.93, 0, 1.6), yaw=90, names={"ClassicNightstand_01": "Prop_Nightstand"})
    place_prop(root, "GothicCabinet_01", (0.45, 0, Z1 - 0.47), yaw=180, scale=0.82, names={"GothicCabinet_01": "Prop_Wardrobe"})
    place_prop(root, "painted_wooden_chair_02", (-1.45, 0, 2.62), yaw=18, names={"painted_wooden_chair_02": "Prop_DeskChair"})
    place_prop(root, "standing_picture_frame_01", (-1.97, 0.70, 1.82), yaw=110, names={"standing_picture_frame_01": "Prop_PhotoFrame"})


# ---------------------------------------------------------------- run


def main():
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    # The attic script's leftovers (none are linked to the scene, but keep the file clean).
    root = empty("Bedroom_Model")
    build_room(root)
    build_blinds(root)
    build_bed(root)
    build_nightstand_things(root)
    build_desk(root)
    build_shelf(root)
    build_decor(root)
    build_props(root)

    # Export triangles: the rounded parts (pillows, the backpack) have pinched caps that Unity's importer would
    # otherwise throw away as "self-intersecting" polygons, leaving holes.
    for obj in bpy.data.objects:
        if obj.type == "MESH" and not any(m.type == "TRIANGULATE" for m in obj.modifiers):
            tri = obj.modifiers.new("Triangulate", "TRIANGULATE")
            tri.min_vertices = 4
            tri.quad_method = "BEAUTY"
            tri.ngon_method = "BEAUTY"

    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=False, object_types={"EMPTY", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
    used = {s.material.name for o in bpy.data.objects if o.type == "MESH" for s in o.material_slots if s.material}
    with open(os.path.splitext(OUT_FBX)[0] + ".materials.json", "w") as f:
        json.dump({"materials": [m for name, m in MATERIALS.items() if name in used]}, f, indent=1)
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
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, relative_remap=True)
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_mainfile()
    tris = sum(len(p.vertices) - 2 for o in bpy.data.objects if o.type == "MESH" for p in o.data.polygons)
    print(f"[Bedroom] {len(placements)} objects, ~{tris} triangles")
    print(f"[Bedroom] wrote {OUT_FBX}")


main()
