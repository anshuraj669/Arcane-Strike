"""
Aged texture sets for the attic's old things, in the attic's Textures folder (run from the Unity project folder):

    python3 Tools/textures/build_attic_grime.py

The real wood, sacking and metal scans with years of dirt laid over them (blotches, dust settled in the grain, stains
running down), plus cardboard and a filthy screen made from noise. All of it tiles. Everything comes from CC0 scans
or this script.
"""
import os
import shutil

import numpy as np
from PIL import Image

OUT = "Assets/_Project/Art/Environment/Attic/Textures"
S = 1024
rng = np.random.default_rng(11)


def soft(blur):
    """Seamless noise, 0..1: white noise blurred in frequency space (so it wraps)."""
    f = np.fft.fftfreq(S)
    fx, fy = np.meshgrid(f, f)
    n = np.fft.ifft2(np.fft.fft2(rng.normal(size=(S, S))) * np.exp(-2 * (np.pi * blur) ** 2 * (fx * fx + fy * fy))).real
    n -= n.min()
    return n / n.max()


def band(x, a, b):
    k = np.clip((x - a) / (b - a), 0, 1)
    return k * k * (3 - 2 * k)


def grime(amount=1.0):
    """How dirty each spot is, 0..1: broad blotches with a fine crust on them."""
    broad = soft(26) * 0.5 + soft(9) * 0.5
    return np.clip(band(broad, 0.3, 0.8) * (0.35 + 0.65 * soft(2)) * amount, 0, 1)


def dust(amount=1.0):
    return np.clip(band(soft(14), 0.3, 0.75) * (0.4 + 0.6 * soft(1.2)) * amount, 0, 1)


def over(rgb, mask, colour):
    return rgb * (1 - mask[..., None]) + np.array(colour) * mask[..., None]


def load(name):
    return np.asarray(Image.open(f"{OUT}/{name}/{name}_Color.jpg").convert("RGB")).astype(np.float32) / 255


def save(name, rgb, normal_from=None, height=None):
    os.makedirs(f"{OUT}/{name}", exist_ok=True)
    Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).save(f"{OUT}/{name}/{name}_Color.jpg", quality=92)
    path = f"{OUT}/{name}/{name}_Normal.jpg"
    if normal_from:
        shutil.copy(f"{OUT}/{normal_from}/{normal_from}_Normal.jpg", path)
    else:
        dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 5
        dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 5
        n = np.stack([-dx, dy, np.ones_like(dx)], -1)
        n /= np.linalg.norm(n, axis=-1, keepdims=True)
        Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8)).save(path, quality=92)


wood = load("Wood060") * 0.62
wood = over(wood, grime() * 0.6, (0.07, 0.052, 0.035))
save("WoodGrimy", over(wood, dust() * 0.42, (0.5, 0.47, 0.41)), normal_from="Wood060")

sack = load("Fabric061") * np.array([0.62, 0.52, 0.34])
sack = over(sack, grime() * 0.55, (0.12, 0.09, 0.05))
save("BurlapOld", over(sack, dust(0.8) * 0.3, (0.48, 0.43, 0.33)), normal_from="Fabric061")

metal = load("Metal063") * np.array([0.72, 0.56, 0.32])
metal = over(metal, band(soft(12), 0.42, 0.72) * 0.35, (0.16, 0.24, 0.19))  # verdigris
metal = over(metal, grime() * 0.6, (0.06, 0.05, 0.035))
save("BrassTarnished", metal, normal_from="Metal063")

# Cardboard: faint flutes, water stains with a dark tide line, scuffs.
y = np.arange(S)[:, None] / S
flutes = 0.5 + 0.5 * np.sin(y * 2 * np.pi * 90)
base = np.array([0.5, 0.39, 0.26]) * (0.86 + 0.14 * soft(40)[..., None]) * (0.96 + 0.04 * flutes[..., None])
wet = soft(70)
board = over(base, band(wet, 0.5, 0.62) * 0.5, (0.27, 0.2, 0.13))
board = over(board, (band(wet, 0.49, 0.5) - band(wet, 0.51, 0.53)).clip(0, 1) * 0.6, (0.13, 0.09, 0.06))
board = over(board, grime() * 0.65, (0.08, 0.06, 0.04))
board = over(board, band(soft(4), 0.62, 0.72) * 0.5, (0.66, 0.6, 0.5))
save("CardboardOld", board, height=flutes * 0.15 + soft(3) * 0.6)

# Screen: dark glass under dust, with streaks where damp ran down it.
f = np.fft.fftfreq(S)
fx, fy = np.meshgrid(f, f)
runs = np.fft.ifft2(np.fft.fft2(rng.normal(size=(S, S))) * np.exp(-2 * np.pi ** 2 * (60 * 60 * fy * fy + 2 * 2 * fx * fx))).real
runs = (runs - runs.min()) / (runs.max() - runs.min())
glass = np.ones((S, S, 3)) * np.array([0.05, 0.075, 0.07])
glass = over(glass, band(soft(45), 0.3, 0.7) * 0.75, (0.36, 0.38, 0.34))
glass = over(glass, band(runs, 0.55, 0.75) * 0.5, (0.03, 0.04, 0.035))
save("ScreenDusty", over(glass, band(soft(1.5), 0.6, 0.72) * 0.3, (0.6, 0.6, 0.55)), height=soft(3) * 0.1)
