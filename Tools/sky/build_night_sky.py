"""
Paints the monster world's night sky (after the user's reference, img.png) as a latitude-longitude panorama:
a pink moon straight ahead (+Z) above the peak, blue and magenta nebulae, a field of stars.

    python3 Tools/sky/build_night_sky.py          (from the Unity project folder; needs numpy and Pillow)

Writes Assets/_Project/Art/Environment/MonsterWorld/Materials/T_World_NightSky.png. Noise and clouds are sampled by
direction, so nothing pinches at the zenith where a VR player can look straight up.
Mapping (Unity's Skybox/Panoramic): u = 0.5 - atan2(z, x) / 2pi, top row = straight up, so +Z sits at u = 0.25.
"""

import os

import numpy as np
from PIL import Image

W, H = 4096, 2048
OUT = "Assets/_Project/Art/Environment/MonsterWorld/Materials/T_World_NightSky.png"
MOON_ELEVATION, MOON_RADIUS = np.radians(38.0), np.radians(7.5)
rng = np.random.default_rng(21)


def direction(azimuth_deg, elevation_deg):
    """Azimuth 0 = ahead (+Z), positive = to the player's right (+X)."""
    a, e = np.radians(azimuth_deg), np.radians(elevation_deg)
    return np.array([np.sin(a) * np.cos(e), np.sin(e), np.cos(a) * np.cos(e)])


def volume(size, falloff):
    """Periodic 3D noise with a 1/f^falloff spectrum, normalised to 0..1."""
    f = np.fft.fftfreq(size)
    r = np.sqrt(f[:, None, None] ** 2 + f[None, :, None] ** 2 + f[None, None, :] ** 2)
    r[0, 0, 0] = 1.0
    v = np.fft.ifftn(np.fft.fftn(rng.normal(size=(size,) * 3)) / r ** falloff).real
    return (v - v.min()) / (v.max() - v.min())


def sample(vol, p):
    """Trilinear lookup of the periodic volume at p (3, h, w), one unit = the whole volume."""
    n = vol.shape[0]
    q = p * n
    i = np.floor(q).astype(np.int32)
    t = q - i
    t = t * t * (3 - 2 * t)  # no lattice showing through
    i %= n
    j = (i + 1) % n
    out = 0.0
    for bits in range(8):
        ix = [j[a] if bits >> a & 1 else i[a] for a in range(3)]
        w = np.prod([t[a] if bits >> a & 1 else 1 - t[a] for a in range(3)], axis=0)
        out = out + vol[ix[0], ix[1], ix[2]] * w
    return out


def blob(d, centre, width):
    """Soft patch of sky round a direction."""
    return np.exp((np.sum(d * centre[:, None, None], axis=0) - 1.0) / width)


def soft(x, a, b):
    k = np.clip((x - a) / (b - a), 0, 1)
    return k * k * (3 - 2 * k)


v, u = np.mgrid[0:H, 0:W].astype(np.float32)
theta = (v + 0.5) / H * np.pi
lon = (0.5 - (u + 0.5) / W) * 2 * np.pi
d = np.stack([np.sin(theta) * np.cos(lon), np.cos(theta), np.sin(theta) * np.sin(lon)]).astype(np.float32)

big, fine = volume(128, 2.1), volume(128, 1.7)
warp = (sample(big, d * 0.13 + 0.3) - 0.5) * 0.28
gas = sample(big, d * 0.17 + warp) * 0.68 + sample(fine, d * 0.42 + warp) * 0.32
gas = soft(gas, 0.36, 0.66)
wisps = soft(sample(fine, d * 0.9 - warp), 0.4, 0.72)

# Where the clouds gather, composed like the reference: blue left of the moon, rose beside it, red low on the right.
blue = blob(d, direction(-33, 42), 0.26) + 0.6 * blob(d, direction(120, 38), 0.3) + 0.5 * blob(d, direction(-140, 25), 0.26)
rose = blob(d, direction(-52, 22), 0.13) + 0.7 * blob(d, direction(-18, 20), 0.05) + 0.7 * blob(d, direction(170, 30), 0.2)
red = blob(d, direction(62, 14), 0.13) + 0.8 * blob(d, direction(40, 7), 0.06) + 0.6 * blob(d, direction(-105, 12), 0.16)

sky = np.zeros((H, W, 3), np.float32) + np.array([0.012, 0.009, 0.024], np.float32)
sky += (gas * (0.4 + wisps))[..., None] * np.array([0.05, 0.03, 0.075], np.float32)
sky += (gas * blue * (0.55 + wisps))[..., None] * np.array([0.07, 0.3, 0.72], np.float32)
sky += (gas * rose * (0.55 + wisps))[..., None] * np.array([0.95, 0.27, 0.42], np.float32)
sky += (gas * red * (0.55 + wisps))[..., None] * np.array([0.9, 0.2, 0.3], np.float32)

# Stars: thick where the clouds are, with a few coloured ones. Each is stretched sideways near the poles to stay round.
stars = np.zeros((H, W, 3), np.float32)
count = 60000
z = rng.uniform(-0.25, 1.0, count)
az = rng.uniform(0, 2 * np.pi, count)
sx = ((az / (2 * np.pi)) * W).astype(np.int32)
sy = np.clip(((np.arccos(z) / np.pi) * H).astype(np.int32), 0, H - 1)
keep = rng.random(count) < 0.25 + 0.75 * gas[sy, sx]
sx, sy = sx[keep], sy[keep]
level = rng.pareto(2.2, sx.size) * 0.3 + 0.1
tints = np.array([[1, 1, 1], [0.7, 0.8, 1], [1, 0.72, 0.6], [1, 0.55, 0.75], [0.6, 0.9, 1]], np.float32)
tint = tints[rng.integers(0, len(tints), sx.size)]
stretch = np.clip(1 / np.maximum(np.sin((sy + 0.5) / H * np.pi), 0.05), 1, 8).astype(np.int32)
for x, y, l, c, s in zip(sx, sy, level, tint, stretch):
    r = 2 if l > 0.5 else 1
    for dy in range(-r, r + 1):
        for dx in range(-r * s, r * s + 1):
            q = (dx / s) ** 2 + dy ** 2
            stars[min(max(y + dy, 0), H - 1), (x + dx) % W] += c * l * np.exp(-q / (0.35 + 0.3 * r))
sky += stars

# Near the horizon the sky sinks into the dark air over the lake.
elevation = np.arcsin(np.clip(d[1], -1, 1))
sky *= (0.25 + 0.75 * soft(elevation, np.radians(-1), np.radians(10)))[..., None]

# The moon: flat pale pink, a little warmer across one side, with a soft bloom round it.
to_moon = direction(0, np.degrees(MOON_ELEVATION)).astype(np.float32)
angle = np.arccos(np.clip(np.sum(d * to_moon[:, None, None], axis=0), -1, 1))
sky += (np.exp(-np.maximum(angle - MOON_RADIUS, 0) / np.radians(5)) * 0.2)[..., None] * np.array([1, 0.45, 0.85], np.float32)
moon = soft(MOON_RADIUS + 0.0012 - angle, 0, 0.0024)
shade = soft(d[0] * 5 + d[1] * 3 - 1.7, -0.5, 0.5)
face = np.array([1.0, 0.68, 0.98], np.float32) * (1 - shade[..., None]) + np.array([1.0, 0.74, 0.86], np.float32) * shade[..., None]
sky = sky * (1 - moon[..., None]) + face * moon[..., None]

os.makedirs(os.path.dirname(OUT), exist_ok=True)
Image.fromarray((np.clip(sky, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT, optimize=True)
print("wrote", OUT, f"({os.path.getsize(OUT) // 1024} KB)")
