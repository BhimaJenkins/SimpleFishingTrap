"""Paints the 256x256 texture atlas for the fishing trap. Regions are in UV space (v=0 at bottom)."""
import numpy as np
from PIL import Image

SIZE = 256
REGIONS = {  # u0, v0, u1, v1
    "wood":  (0.00, 0.00, 0.50, 1.00),
    "nylon": (0.50, 0.50, 0.75, 1.00),
    "twine": (0.75, 0.50, 1.00, 1.00),
    "stone": (0.50, 0.00, 0.75, 0.50),
    "cork":  (0.75, 0.25, 1.00, 0.50),
    "fish":  (0.75, 0.00, 1.00, 0.25),
}

rng = np.random.default_rng(7)


def blur(a, k=1):
    out = a.copy()
    for _ in range(k):
        out = (out + np.roll(out, 1, 0) + np.roll(out, -1, 0) + np.roll(out, 1, 1) + np.roll(out, -1, 1)) / 5
    return out


def value_noise(h, w, cell):
    gh, gw = h // cell + 2, w // cell + 2
    g = rng.random((gh, gw))
    ys = np.linspace(0, h / cell, h, endpoint=False)
    xs = np.linspace(0, w / cell, w, endpoint=False)
    y0, x0 = ys.astype(int), xs.astype(int)
    fy, fx = (ys - y0)[:, None], (xs - x0)[None, :]
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    a = g[y0][:, x0]; b = g[y0][:, x0 + 1]; c = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
    return a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy


def colorize(t, dark, light):
    dark, light = np.array(dark, float), np.array(light, float)
    return dark[None, None, :] * (1 - t[..., None]) + light[None, None, :] * t[..., None]


def wood(h, w):
    # vertical grain (beam length runs along v), Valheim-ish warm brown
    streak = blur(rng.random((1, w)).repeat(h, 0), 1)
    streak = np.clip((streak - 0.5) * 1.6 + 0.5, 0, 1)
    wobble = value_noise(h, w, 16)
    grain = 0.55 * streak + 0.30 * wobble + 0.15 * rng.random((h, w))
    # a few darker knots / stripes
    for _ in range(5):
        cx, cy = rng.integers(0, w), rng.integers(0, h)
        yy, xx = np.ogrid[:h, :w]
        grain -= 0.35 * np.exp(-(((xx - cx) / 3.0) ** 2 + ((yy - cy) / 6.0) ** 2))
    grain = np.clip(grain, 0, 1)
    return colorize(grain, (58, 38, 24), (132, 95, 62))


def nylon(h, w):
    n = 0.6 * value_noise(h, w, 8) + 0.4 * rng.random((h, w))
    return colorize(n, (150, 146, 132), (205, 200, 184))


def twine(h, w):
    yy, xx = np.mgrid[:h, :w]
    stripes = (np.sin((xx + yy) * 0.9) * 0.5 + 0.5)
    n = 0.6 * stripes + 0.4 * rng.random((h, w))
    return colorize(n, (78, 58, 38), (150, 118, 80))


def stone(h, w):
    n = 0.55 * value_noise(h, w, 10) + 0.30 * value_noise(h, w, 4) + 0.15 * rng.random((h, w))
    return colorize(n, (82, 84, 82), (150, 150, 144))


def cork(h, w):
    n = 0.5 * value_noise(h, w, 6) + 0.5 * rng.random((h, w))
    base = colorize(n, (150, 112, 62), (214, 176, 112))
    dots = rng.random((h, w)) > 0.93
    base[dots] *= 0.7
    return base


def fish(h, w):
    # v (rows) = back (top) to belly (bottom)
    t = np.linspace(0, 1, h)[:, None].repeat(w, 1)
    t = np.clip(t + 0.12 * (value_noise(h, w, 6) - 0.5), 0, 1)
    img = colorize(t, (48, 66, 82), (198, 206, 208))
    # speckles on the back
    speck = (rng.random((h, w)) > 0.9) & (t < 0.45)
    img[speck] *= 0.65
    return img


PAINTERS = {"wood": wood, "nylon": nylon, "twine": twine, "stone": stone, "cork": cork, "fish": fish}


def main(path):
    img = np.zeros((SIZE, SIZE, 3), float)
    for name, (u0, v0, u1, v1) in REGIONS.items():
        x0, x1 = int(u0 * SIZE), int(u1 * SIZE)
        y0, y1 = int((1 - v1) * SIZE), int((1 - v0) * SIZE)
        img[y0:y1, x0:x1] = PAINTERS[name](y1 - y0, x1 - x0)
    Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGB").save(path)


if __name__ == "__main__":
    import sys
    main(sys.argv[1] if len(sys.argv) > 1 else "SimpleFishingTrap_Albedo.png")
