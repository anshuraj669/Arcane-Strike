"""
Paints the noise the fire shader flows through, into Assets/_Project/Resources/FX/T_FireNoise.png (seamless):
R = ridged (the thin bright filaments of a flame), G = large soft billows (bends the whole fire), B = fine turbulence.

    python3 Tools/textures/build_fire_noise.py      (from the Unity project folder; needs numpy and Pillow)
"""

import numpy as np
from PIL import Image

SIZE = 256
OUT = "Assets/_Project/Resources/FX/T_FireNoise.png"
rng = np.random.default_rng(9)


def field(falloff, low=0.0):
    f = np.fft.fftfreq(SIZE) * SIZE
    r = np.hypot(*np.meshgrid(f, f))
    r[0, 0] = 1.0
    spectrum = np.fft.fft2(rng.normal(size=(SIZE, SIZE))) / r ** falloff
    spectrum[r < low] = 0
    n = np.fft.ifft2(spectrum).real
    return (n - n.mean()) / n.std()


def unit(n):
    return np.clip(n * 0.22 + 0.5, 0, 1)


ridged = 1 - np.clip(np.abs(field(1.5, 2.0)) * 0.75, 0, 1)
ridged = np.clip(ridged * 0.75 + unit(field(1.3, 3.0)) * 0.25, 0, 1)
soft = unit(field(2.1))
fine = unit(field(1.15, 6.0))
Image.fromarray((np.dstack([ridged, soft, fine]) * 255).astype(np.uint8)).save(OUT, optimize=True)
print("wrote", OUT)
