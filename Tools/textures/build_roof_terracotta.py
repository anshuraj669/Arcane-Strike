"""
Paints the old house's terracotta roof tiles (seamless), in the Textures/<set>/<set>_Color.jpg + _Normal.jpg layout
that build_attic.py and Apply Attic Model expect. The stock tile texture is too dark to tint to terracotta.

    python3 Tools/textures/build_roof_terracotta.py          (from the Unity project folder; needs numpy and Pillow)
"""

import os

import numpy as np
from PIL import Image

SIZE, ROWS, COLS = 1024, 8, 8  # with tile = 1.4 m in build_attic.py: tiles about 17 cm wide
OUT = "Assets/_Project/Art/Environment/Attic/Textures/RoofTerracotta/RoofTerracotta"
rng = np.random.default_rng(11)

y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) / SIZE
row = np.floor(y * ROWS).astype(np.int32)
v = y * ROWS - row                                  # 0 at a row's top .. 1 at its lower lip
shifted = (x * COLS + (row % 2) * 0.5) % COLS       # alternate rows offset by half a tile
col = np.floor(shifted).astype(np.int32)
u = shifted - col

# Each tile bows up across its width and rises towards its lower lip, where it overlaps the row below.
bow = np.sqrt(np.clip(1 - (2 * u - 1) ** 2, 0, 1))
height = bow * 0.55 + v * 0.6
lip = np.clip((v - 0.93) / 0.07, 0, 1)
height -= lip * 1.0
edge = np.minimum(u, 1 - u)

# Fired clay: every tile its own shade from orange-red to brown, a few burnt dark, mottled and weathered.
base = np.array([[0.62, 0.27, 0.15], [0.70, 0.33, 0.18], [0.55, 0.22, 0.13], [0.74, 0.39, 0.21], [0.42, 0.20, 0.13]], np.float32)
pick = rng.integers(0, len(base), (ROWS, COLS))
shade = rng.uniform(0.82, 1.1, (ROWS, COLS, 1)).astype(np.float32)
color = (base[pick] * shade)[row, col]

noise = np.fft.ifft2(np.fft.fft2(rng.normal(size=(SIZE, SIZE))) /
                     (np.hypot(*np.meshgrid(np.fft.fftfreq(SIZE), np.fft.fftfreq(SIZE))) + 0.004)).real
noise = (noise - noise.mean()) / noise.std()
color *= (1 + noise[..., None] * 0.07).astype(np.float32)
color *= (0.5 + 0.5 * bow[..., None]) * (1 - 0.65 * np.clip((v[..., None] - 0.86) / 0.14, 0, 1) ** 2)
color *= 0.55 + 0.45 * np.clip(edge[..., None] / 0.05, 0, 1)
color = np.clip(color, 0, 1)

gy = np.roll(height, -1, 0) - np.roll(height, 1, 0)
gx = np.roll(height, -1, 1) - np.roll(height, 1, 1)
strength = 7.0
n = np.dstack([-gx * strength, gy * strength, np.ones_like(gx)])
n /= np.linalg.norm(n, axis=2, keepdims=True)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
Image.fromarray((color * 255).astype(np.uint8)).save(OUT + "_Color.jpg", quality=92)
Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8)).save(OUT + "_Normal.jpg", quality=95)
print("wrote", OUT)
