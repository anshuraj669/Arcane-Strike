"""
Paints the Ascension temple's textures into Assets/_Project/Art/Environment/Temple:

    python3 Tools/textures/build_temple_textures.py      (from the Unity project folder; needs numpy and Pillow)

- T_Temple_Mosaic.jpg: the round inlaid floor, rings of gold, cream and jade round a blue star.
- T_Temple_Ornament.jpg: ivory stone with gold scrollwork, for the arches and the band above them.
- T_Temple_Bronze.jpg: an ancient cast-bronze skin with a meander band, a row of petals and verdigris, wrapped round
  the cauldron (u round it, v up it).
- T_Temple_Sky.png: a golden cloudy sky (latitude-longitude, like Tools/sky/build_night_sky.py).
"""

import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = "Assets/_Project/Art/Environment/Temple"
rng = np.random.default_rng(5)
GOLD, DARK_GOLD, CREAM, JADE, BLUE = (214, 170, 84), (142, 100, 44), (236, 224, 196), (64, 150, 132), (70, 150, 205)


def mosaic(size=1024):
    im = Image.new("RGB", (size * 2, size * 2), CREAM)
    d = ImageDraw.Draw(im)
    c, s = size, size / 512.0

    def ring(r, color):
        d.ellipse((c - r * s * 2, c - r * s * 2, c + r * s * 2, c + r * s * 2), fill=color)

    def petals(n, r0, r1, width, color, turn=0.0):
        for i in range(n):
            a = turn + i / n * math.tau
            ca, sa, w = math.cos(a), math.sin(a), width * s * 2 / r1 * r1
            p = [(c + ca * r0 * s * 2, c + sa * r0 * s * 2),
                 (c + ca * (r0 + r1) / 2 * s * 2 - sa * w, c + sa * (r0 + r1) / 2 * s * 2 + ca * w),
                 (c + ca * r1 * s * 2, c + sa * r1 * s * 2),
                 (c + ca * (r0 + r1) / 2 * s * 2 + sa * w, c + sa * (r0 + r1) / 2 * s * 2 - ca * w)]
            d.polygon(p, fill=color)

    for r, col in ((506, DARK_GOLD), (496, GOLD), (476, CREAM), (420, GOLD), (412, JADE), (340, GOLD), (332, CREAM),
                   (250, DARK_GOLD), (244, BLUE), (150, GOLD), (142, CREAM)):
        ring(r, col)
    petals(48, 424, 474, 8, DARK_GOLD)
    petals(24, 346, 410, 13, CREAM)
    petals(24, 346, 396, 6, GOLD, math.tau / 48)
    petals(16, 254, 330, 18, GOLD)
    petals(16, 262, 322, 9, JADE)
    petals(12, 150, 242, 22, CREAM, math.tau / 24)
    petals(12, 156, 226, 11, (150, 205, 235), math.tau / 24)
    petals(8, 30, 140, 24, BLUE)
    petals(8, 40, 128, 11, GOLD)
    ring(34, DARK_GOLD)
    im = im.resize((size, size), Image.LANCZOS)
    a = np.asarray(im).astype(np.float32)
    # Little tesserae: a faint grid, each stone a slightly different shade.
    cell = 7
    shade = rng.uniform(0.92, 1.06, (size // cell + 1, size // cell + 1))
    y, x = np.mgrid[0:size, 0:size]
    a *= shade[y // cell, x // cell][..., None]
    a *= (0.9 + 0.1 * ((x % cell > 0) & (y % cell > 0)))[..., None]
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(f"{OUT}/T_Temple_Mosaic.jpg", quality=90)


def ornament(w=1024, h=512):
    """Mirrored left to right so it tiles along the band and sits symmetrically on an arch."""
    half = Image.new("RGB", (w // 2 * 2, h * 2), (226, 212, 180))
    d = ImageDraw.Draw(half)
    W, H = half.size

    def scroll(cx, cy, r, turns, flip, width):
        pts = []
        for i in range(70):
            t = i / 69
            a = flip * (t * turns * math.tau) + cx * 0.01
            rr = r * (1 - t * 0.9)
            pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
        d.line(pts, fill=GOLD, width=width, joint="curve")

    d.rectangle((0, 0, W, 22), fill=DARK_GOLD)
    d.rectangle((0, H - 22, W, H), fill=DARK_GOLD)
    d.rectangle((0, 22, W, 34), fill=GOLD)
    d.rectangle((0, H - 34, W, H - 22), fill=GOLD)
    for i in range(5):
        cx = 110 + i * 220
        for j, cy in enumerate((H * 0.33, H * 0.67)):
            flip = 1 if (i + j) % 2 else -1
            scroll(cx, cy, 170, 1.6, flip, 13)
            scroll(cx + 80 * flip, cy + 60 * flip, 70, 1.3, -flip, 8)
    for i in range(24):
        x, y = rng.uniform(0, W), rng.uniform(60, H - 60)
        d.ellipse((x - 9, y - 9, x + 9, y + 9), fill=GOLD)
    half = half.filter(ImageFilter.GaussianBlur(1.0)).resize((w // 2, h), Image.LANCZOS)
    a = np.asarray(half)
    a = np.concatenate([a, a[:, ::-1]], axis=1)
    Image.fromarray(a).save(f"{OUT}/T_Temple_Ornament.jpg", quality=90)


def bronze(w=1024, h=512):
    """Olive bronze, a band of squared scrolls near the top, long petals below, green patina in blotches."""
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    f = np.hypot(*np.meshgrid(np.fft.fftfreq(w), np.fft.fftfreq(h)))
    def field(power, seed):
        n = np.fft.ifft2(np.fft.fft2(np.random.default_rng(seed).normal(size=(h, w))) / (f + 0.004) ** power).real
        return (n - n.min()) / (n.max() - n.min())
    im = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(im)
    # (v = 0 is the top row here: the band high on the shoulder, the petals on the belly.)
    d.rectangle((0, 96, w, 102), fill=255)
    d.rectangle((0, 196, w, 202), fill=255)
    cell = w // 16
    for i in range(16):
        x0 = i * cell + 8
        pts = [(x0, 186), (x0, 112), (x0 + 42, 112), (x0 + 42, 168), (x0 + 16, 168), (x0 + 16, 136), (x0 + 28, 136)]
        d.line(pts, fill=255, width=6)
        d.line([(x0 + 52, 112), (x0 + 52, 186), (x0 + cell - 8, 186)], fill=255, width=6)
        d.ellipse((x0 + 54, 128, x0 + 54 + 12, 140), fill=255)
    pw = w // 12
    for i in range(12):
        cx = i * pw + pw // 2
        d.line([(cx - pw * 0.42, 214), (cx, 420), (cx + pw * 0.42, 214)], fill=255, width=7)
        d.line([(cx - pw * 0.24, 232), (cx, 350), (cx + pw * 0.24, 232)], fill=255, width=4)
        for k in range(3):
            d.ellipse((cx - 12, 246 + k * 26, cx + 12, 262 + k * 26), outline=255, width=3)
    lines = np.asarray(im.filter(ImageFilter.GaussianBlur(0.9))).astype(np.float32) / 255
    base = np.array([96, 88, 58], np.float32) + (field(1.2, 1)[..., None] - 0.5) * np.array([70, 66, 44], np.float32)
    base *= (1 - lines[..., None] * 0.62)                                     # the cast lines sit dark
    patina = np.clip((field(1.5, 2) - 0.56) / 0.12, 0, 1) * (0.5 + 0.5 * field(0.8, 3))
    patina[:70] = np.maximum(patina[:70], np.clip((field(1.2, 4)[:70] - 0.38) / 0.1, 0, 1))  # thickest round the lip
    a = base * (1 - patina[..., None]) + np.array([68, 138, 112], np.float32) * patina[..., None]
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(f"{OUT}/T_Temple_Bronze.jpg", quality=90)


def sky(W=2048, H=1024):
    """Soft cream and gold cloud, brighter low down, blue-free, as in the reference."""
    f = np.fft.fftfreq(128)
    r = np.sqrt(f[:, None, None] ** 2 + f[None, :, None] ** 2 + f[None, None, :] ** 2)
    r[0, 0, 0] = 1
    vol = np.fft.ifftn(np.fft.fftn(rng.normal(size=(128, 128, 128))) / r ** 1.9).real
    vol = (vol - vol.min()) / (vol.max() - vol.min())
    v, u = np.mgrid[0:H, 0:W].astype(np.float32)
    th, lon = (v + 0.5) / H * np.pi, (0.5 - (u + 0.5) / W) * 2 * np.pi
    d = np.stack([np.sin(th) * np.cos(lon), np.cos(th) * 2.2, np.sin(th) * np.sin(lon)])

    def sample(p):
        q = p * 128
        i = np.floor(q).astype(np.int32)
        t = q - i
        t = t * t * (3 - 2 * t)
        i %= 128
        j = (i + 1) % 128
        out = 0.0
        for bits in range(8):
            ix = [j[k] if bits >> k & 1 else i[k] for k in range(3)]
            w = np.prod([t[k] if bits >> k & 1 else 1 - t[k] for k in range(3)], axis=0)
            out = out + vol[ix[0], ix[1], ix[2]] * w
        return out

    n = sample(d * 0.2 + 0.1) * 0.65 + sample(d * 0.5 + 0.4) * 0.35
    n = np.clip((n - 0.38) / 0.24, 0, 1)
    n = n * n * (3 - 2 * n)
    el = np.clip(1 - th / (np.pi / 2), -1, 1)
    deep = np.array([0.9, 0.56, 0.14], np.float32)
    pale = np.array([1.0, 0.88, 0.58], np.float32)
    col = deep[None, None] * (1 - n[..., None]) + pale[None, None] * n[..., None]
    col *= (0.86 + 0.14 * (1 - np.clip(el, 0, 1)))[..., None]
    Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8)).save(f"{OUT}/T_Temple_Sky.png", optimize=True)


os.makedirs(OUT, exist_ok=True)
mosaic()
ornament()
bronze()
sky()
print("wrote", OUT)
