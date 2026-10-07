"""Build the SessionPilot icon from the black-on-white line art.

Same look as the Media Streaming Family icons: the dark rounded tile, glowing cyan tubes,
and bright magenta accents (the heading pointer and the monitor's inner screen).

The art is simplified before it is lit:
- the middle double ring is removed, so the mark stays readable at 24-32 px;
- the horizontal bars are extended out to the outer ring;
- each remaining double ring is merged into one solid line.

Usage:
    python tools/build-icon.py

Writes src/SessionPilot.App/app.ico (16-256 px) and brand/icon-512.png.
The 16 px frame is hand-placed pixel art (SMALL_16) rather than a shrunk copy.
Needs Pillow and NumPy.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "assets" / "sessionpilot-icon-line-art.jpg"
ICO = ROOT / "src" / "SessionPilot.App" / "app.ico"
PNG = ROOT / "brand" / "icon-512.png"

MASTER = 1024
FILL = 0.84
TILE_TOP = np.array([20, 28, 34])
TILE_BOT = np.array([9, 13, 17])
CORNER_R = 0.225
CYAN, CYAN_HOT = (12, 224, 239), (216, 255, 255)
MAGENTA, MAGENTA_HOT = (236, 24, 168), (255, 196, 236)
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]


def tile(size: int) -> Image.Image:
    grad = TILE_TOP + (TILE_BOT - TILE_TOP) * (np.arange(size) / (size - 1))[:, None]
    base = Image.fromarray(np.repeat(grad[:, None, :], size, axis=1).astype(np.uint8), "RGB").convert("RGBA")
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=int(CORNER_R * size), fill=255)
    hi = Image.new("L", (size, size), 0)
    ImageDraw.Draw(hi).rounded_rectangle([0, 0, size - 1, int(size * 0.5)], radius=int(CORNER_R * size), fill=26)
    hi = Image.composite(hi.filter(ImageFilter.GaussianBlur(size * 0.06)), Image.new("L", (size, size), 0), mask)
    base = Image.alpha_composite(base, Image.merge("RGBA", [Image.new("L", (size, size), 255)] * 3 + [hi]))
    base.putalpha(mask)
    return base


def simplify(gray: np.ndarray) -> np.ndarray:
    """Remove the middle double ring and extend the horizontal bars to the outer ring."""
    size = gray.shape[0]
    c = size / 2
    k = size / 2048
    yy, xx = np.mgrid[:size, :size]
    r = np.hypot(xx - c, yy - c)
    out = gray.copy()
    out[(r > 628 * k) & (r < 714 * k)] = 255
    column = out[:, int(500 * k)] < 128
    rows = [y for y in range(int(980 * k), int(1070 * k)) if column[y]]
    y0, y1 = min(rows), max(rows)
    image = Image.fromarray(out)
    draw = ImageDraw.Draw(image)
    radius = int((y1 - y0) / 2)
    draw.rounded_rectangle([c - 812 * k, y0, c - 600 * k, y1], radius=radius, fill=0)
    draw.rounded_rectangle([c + 600 * k, y0, c + 812 * k, y1], radius=radius, fill=0)
    return np.asarray(image)


def component(binary: np.ndarray, seed: tuple[int, int]) -> np.ndarray:
    """Mask of the connected stroke that contains seed (x, y)."""
    image = Image.fromarray((binary * 255).astype(np.uint8), "L").copy()
    ImageDraw.floodfill(image, seed, 128)
    return np.asarray(image) == 128


def to_image(alpha: np.ndarray) -> Image.Image:
    return Image.fromarray(np.clip(alpha * 255, 0, 255).astype(np.uint8), "L")


def to_alpha(image: Image.Image) -> np.ndarray:
    return np.asarray(image, np.float32) / 255


def layer(alpha: np.ndarray, rgb: tuple[int, int, int]) -> Image.Image:
    out = Image.new("RGBA", (alpha.shape[1], alpha.shape[0]), rgb + (0,))
    out.putalpha(to_image(alpha))
    return out


def neon(stroke: np.ndarray, color, hot) -> Image.Image:
    """Glowing tube around a stroke: bloom, a saturated wall, and a bright core."""
    size = stroke.shape[0]
    grown = to_alpha(to_image(stroke).filter(ImageFilter.MaxFilter(max(3, int(size * 0.0034)) | 1))
                     .filter(ImageFilter.GaussianBlur(size * 0.0012)))
    rim = np.clip(grown - stroke * 0.92, 0, 1)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    for radius, gain in ((0.020, 0.38), (0.009, 0.55), (0.004, 0.75)):
        bloom = to_alpha(to_image(grown).filter(ImageFilter.GaussianBlur(size * radius)))
        out = Image.alpha_composite(out, layer(np.clip(bloom * gain, 0, 1), color))
    out = Image.alpha_composite(out, layer(stroke * 0.10, color))
    out = Image.alpha_composite(out, layer(rim, color))
    out = Image.alpha_composite(out, layer(to_alpha(to_image(rim).filter(ImageFilter.MinFilter(3))) * 0.55, hot))
    return out


def render() -> Image.Image:
    gray = np.asarray(Image.open(SRC).convert("L"))
    if gray.shape[0] != 2048:
        gray = np.asarray(Image.fromarray(gray).resize((2048, 2048), Image.LANCZOS))
    gray = simplify(gray)
    size = gray.shape[0]
    original = np.clip((1 - gray.astype(np.float32) / 255 - 0.08) / 0.84, 0, 1)
    binary = original > 0.5

    # Accent strokes, picked from the unmerged art so they stay separate from the rings.
    cx = size // 2
    py = next(y for y in range(int(size * 0.19), size) if binary[y, cx] and not binary[y, cx - int(size * 0.02)])
    accent = component(binary, (cx, py))                                   # heading pointer
    sy = int(size * 0.44)
    sx = next(x for x in range(cx, 0, -1) if binary[sy, x])
    accent |= component(binary, (sx, sy))                                  # monitor's inner screen

    # Merge each double ring into one solid line.
    gap = int(size * 0.012) | 1
    merged = to_alpha(to_image(original).filter(ImageFilter.MaxFilter(gap)).filter(ImageFilter.MinFilter(gap))
                      .filter(ImageFilter.GaussianBlur(1.2)))
    near = to_alpha(to_image(accent.astype(np.float32)).filter(ImageFilter.MaxFilter(int(size * 0.03) | 1)))
    merged = merged * (1 - near) + original * near                         # no merge bridges next to an accent
    grow = to_alpha(to_image(accent.astype(np.float32)).filter(ImageFilter.MaxFilter(9)))
    art = Image.alpha_composite(neon(merged * (1 - grow), CYAN, CYAN_HOT),
                                neon(original * grow, MAGENTA, MAGENTA_HOT))

    ys, xs = np.nonzero(binary)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    scale = FILL * MASTER / max(x1 - x0, y1 - y0)
    art = art.resize((round(size * scale), round(size * scale)), Image.LANCZOS)
    out = tile(MASTER)
    out.alpha_composite(art, (round(MASTER / 2 - (x0 + x1) / 2 * scale), round(MASTER / 2 - (y0 + y1) / 2 * scale)))
    clip = np.minimum(np.asarray(out)[..., 3], np.asarray(tile(MASTER))[..., 3])
    out.putalpha(Image.fromarray(clip.astype(np.uint8)))
    return out


# Hand-placed 16 px version. Shrinking the full art leaves a smudge at this size, so this keeps
# only the outer ring, the four ticks, the monitor, and the magenta screen and pointer.
SMALL_16 = """
.......cc.......
.....cccccc.....
...cc......cc...
..c....mm....c..
..c..........c..
.c..cccccccc..c.
.c..cmmmmmmc..c.
cccccmmmmmmccccc
cccccmmmmmmccccc
.c..cmmmmmmc..c.
.c..cccccccc..c.
..c....cc....c..
..c..cccccc..c..
...cc......cc...
.....cccccc.....
.......cc.......
""".split()


def small16() -> Image.Image:
    cyan = np.array([[ch == "c" for ch in row] for row in SMALL_16], np.float32)
    magenta = np.array([[ch == "m" for ch in row] for row in SMALL_16], np.float32)
    for y, x in ((5, 4), (5, 11), (10, 4), (10, 11)):                       # soften the bezel corners
        cyan[y, x] = 0.55
    background = tile(256).resize((16, 16), Image.LANCZOS)
    out = background.copy()
    for mask, color in ((cyan, CYAN), (magenta, MAGENTA)):
        big = to_image(mask).resize((64, 64), Image.NEAREST)
        glow = big.filter(ImageFilter.GaussianBlur(3)).resize((16, 16), Image.LANCZOS)
        out = Image.alpha_composite(out, layer(to_alpha(glow) * 0.22, color))
        out = Image.alpha_composite(out, layer(mask, color))
    clip = np.minimum(np.asarray(out)[..., 3], np.asarray(background)[..., 3])
    out.putalpha(Image.fromarray(clip.astype(np.uint8)))
    return out


def downscale(master: Image.Image, size: int) -> Image.Image:
    if size == 16:
        return small16()
    small = master.resize((size, size), Image.LANCZOS)
    if size <= 72:
        small = small.filter(ImageFilter.UnsharpMask(radius=1.0, percent=70, threshold=0))
    return small


def main() -> None:
    master = render()
    PNG.parent.mkdir(parents=True, exist_ok=True)
    downscale(master, 512).save(PNG)
    frames = [downscale(master, size) for size in ICO_SIZES]
    frames[-1].save(ICO, format="ICO", sizes=[(s, s) for s in ICO_SIZES], append_images=frames[:-1])
    print(f"wrote {PNG.relative_to(ROOT)} and {ICO.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
