# Draws the Kovan Term logo with Pillow (4x supersampled). "python tools/draw-logo.py <dir> app" writes kovan.ico,
# logo.png and logo-64.png (variant B); with only <dir>, a sheet of the drafts (A, B, C) on light and dark.
import math, sys
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
S = 4  # supersampling


def hexagon(cx, cy, r, rot=90):
    return [(cx + r * math.cos(math.radians(rot + 60 * i)), cy + r * math.sin(math.radians(rot + 60 * i))) for i in range(6)]


def rounded_hex(draw, cx, cy, r, radius, fill):
    """A hexagon with rounded corners: circles at the corners joined by the polygon inside them."""
    inner = r - radius / math.cos(math.radians(30))
    pts = hexagon(cx, cy, inner)
    draw.polygon(hexagon(cx, cy, inner + radius, rot=90), fill=None)
    # Body: the hexagon grown by `radius` without corners = inner hexagon + rectangles along edges + corner circles
    for x, y in pts:
        draw.ellipse([x - radius, y - radius, x + radius, y + radius], fill=fill)
    draw.polygon(pts, fill=fill)
    for i in range(6):
        (x1, y1), (x2, y2) = pts[i], pts[(i + 1) % 6]
        nx, ny = (y2 - y1), -(x2 - x1)
        n = math.hypot(nx, ny)
        nx, ny = nx / n * radius, ny / n * radius
        # outward normal: away from the center
        if (x1 + nx - cx) ** 2 + (y1 + ny - cy) ** 2 < (x1 - cx) ** 2 + (y1 - cy) ** 2:
            nx, ny = -nx, -ny
        draw.polygon([(x1, y1), (x2, y2), (x2 + nx, y2 + ny), (x1 + nx, y1 + ny)], fill=fill)


def gradient_fill(size, top, bottom, mask):
    grad = Image.new("RGBA", (size, size))
    gd = ImageDraw.Draw(grad)
    for y in range(size):
        t = y / (size - 1)
        gd.line([(0, y), (size, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out


def prompt(draw, cx, cy, h, color, width):
    """A '>_' terminal prompt drawn with strokes, centered at (cx, cy), h tall."""
    w = width
    # '>' chevron
    x0, y0 = cx - h * 0.62, cy - h * 0.42
    draw.line([(x0, y0), (x0 + h * 0.42, cy), (x0, cy + h * 0.42)], fill=color, width=w, joint="curve")
    for x, y in [(x0, y0), (x0, cy + h * 0.42)]:
        draw.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=color)
    # '_' cursor
    ux, uy = cx + h * 0.05, cy + h * 0.42
    draw.rounded_rectangle([ux, uy - w / 2, ux + h * 0.55, uy + w / 2], radius=w / 2, fill=color)


HONEY_TOP, HONEY_BOTTOM = (255, 196, 61), (232, 140, 20)
INK = (33, 28, 22)


def variant_a(n):
    """One honey hexagon with a dark prompt."""
    size = n * S
    mask = Image.new("L", (size, size), 0)
    rounded_hex(ImageDraw.Draw(mask), size / 2, size / 2, size * 0.47, size * 0.07, 255)
    img = gradient_fill(size, HONEY_TOP, HONEY_BOTTOM, mask)
    prompt(ImageDraw.Draw(img), size * 0.52, size * 0.5, size * 0.36, INK + (255,), int(size * 0.075))
    return img.resize((n, n), Image.LANCZOS)


def variant_b(n):
    """A dark hexagon (a terminal) with a honey prompt and a honey rim."""
    size = n * S
    outer = Image.new("L", (size, size), 0)
    rounded_hex(ImageDraw.Draw(outer), size / 2, size / 2, size * 0.47, size * 0.07, 255)
    img = gradient_fill(size, HONEY_TOP, HONEY_BOTTOM, outer)
    inner = Image.new("L", (size, size), 0)
    rounded_hex(ImageDraw.Draw(inner), size / 2, size / 2, size * 0.40, size * 0.05, 255)
    img.paste(Image.new("RGBA", (size, size), (30, 30, 34, 255)), (0, 0), inner)
    prompt(ImageDraw.Draw(img), size * 0.52, size * 0.5, size * 0.30, HONEY_TOP + (255,), int(size * 0.065))
    return img.resize((n, n), Image.LANCZOS)


def variant_c(n):
    """A small hive: three hexagons, the front one a terminal with a prompt."""
    size = n * S
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    r = size * 0.245
    dx = r * math.sqrt(3)
    centers = [(size / 2 - dx / 2, size * 0.30), (size / 2 + dx / 2, size * 0.30), (size / 2, size * 0.30 + r * 1.5)]
    shades = [((255, 214, 102), (240, 170, 40)), ((250, 190, 60), (225, 135, 15))]
    for (cx, cy), (top, bottom) in zip(centers[:2], shades):
        m = Image.new("L", (size, size), 0)
        rounded_hex(ImageDraw.Draw(m), cx, cy, r * 0.94, size * 0.035, 255)
        img.alpha_composite(gradient_fill(size, top, bottom, m))
    cx, cy = centers[2]
    m = Image.new("L", (size, size), 0)
    rounded_hex(ImageDraw.Draw(m), cx, cy, r * 0.94, size * 0.035, 255)
    img.paste(Image.new("RGBA", (size, size), (30, 30, 34, 255)), (0, 0), m)
    prompt(ImageDraw.Draw(img), cx + r * 0.05, cy, r * 0.72, HONEY_TOP + (255,), int(size * 0.045))
    return img.resize((n, n), Image.LANCZOS)


def sheet(variants):
    """Each variant at 256 and 32 px, on light and dark."""
    w, h = 40 + len(variants) * 340, 2 * 330 + 20
    out = Image.new("RGB", (w, h), (255, 255, 255))
    d = ImageDraw.Draw(out)
    d.rectangle([0, h // 2, w, h], fill=(32, 32, 32))
    for i, (name, fn) in enumerate(variants):
        for row, bg in enumerate([(255, 255, 255), (32, 32, 32)]):
            x, y = 40 + i * 340, 20 + row * 340
            out.paste(fn(256), (x, y), fn(256))
            small = fn(32)
            out.paste(small, (x + 270, y + 20), small)
            mid = fn(16)
            out.paste(mid, (x + 278, y + 70), mid)
        d.text((40 + i * 340, h // 2 - 22), name, fill=(0, 0, 0))
    return out


variants = [("A: honey hexagon", variant_a), ("B: terminal in a honeycomb", variant_b), ("C: hive of three", variant_c)]

if len(sys.argv) > 2 and sys.argv[2] == "app":
    # The app's icon (variant B): each .ico size drawn on its own, so small ones stay sharp, plus PNGs for docs
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    images = [variant_b(n) for n in sizes]
    images[-1].save(f"{OUT}/kovan.ico", format="ICO", sizes=[(n, n) for n in sizes], append_images=images[:-1])
    variant_b(512).save(f"{OUT}/logo.png")
    variant_b(64).save(f"{OUT}/logo-64.png")
    print("app icon written")
else:
    sheet(variants).save(f"{OUT}/drafts.png")
    for name, fn in variants:
        fn(512).save(f"{OUT}/{name[0]}.png")
    print("ok")
