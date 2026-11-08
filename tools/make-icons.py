#!/usr/bin/env python3
"""Draws the app's .ico files: the app icon and the tray icons, on and off, for light and dark taskbars.

Plain Python, no imaging library. Shapes are signed-distance functions or flattened SVG-style paths, sampled 4x4
per pixel. Run from anywhere; writes into BusyMouse.WinUI/Assets.
"""

import math
import os
import re
import struct
import zlib

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "BusyMouse.WinUI", "Assets")
SS = 4


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def rrect_sdf(px, py, x, y, w, h, r):
    """Signed distance to a rounded rectangle; negative inside."""
    cx, cy = x + w / 2, y + h / 2
    qx, qy = abs(px - cx) - (w / 2 - r), abs(py - cy) - (h / 2 - r)
    outside = math.hypot(max(qx, 0), max(qy, 0))
    return outside + min(max(qx, qy), 0) - r


def seg_dist(px, py, x0, y0, x1, y1):
    """Distance to a line segment."""
    dx, dy = x1 - x0, y1 - y0
    if dx == 0 and dy == 0:
        return math.hypot(px - x0, py - y0)
    t = max(0, min(1, ((px - x0) * dx + (py - y0) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - x0 - t * dx, py - y0 - t * dy)


# ---------- paths ----------

def _arc_points(x1, y1, rx, ry, phi, large, sweep, x2, y2, steps):
    """An SVG elliptical arc as points, the start left out."""
    cp, sp = math.cos(math.radians(phi)), math.sin(math.radians(phi))
    dx, dy = (x1 - x2) / 2, (y1 - y2) / 2
    x1p, y1p = cp * dx + sp * dy, -sp * dx + cp * dy
    lam = (x1p / rx) ** 2 + (y1p / ry) ** 2
    if lam > 1:
        rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
    k = math.sqrt(max(0, num / (rx * rx * y1p * y1p + ry * ry * x1p * x1p)))
    if large == sweep:
        k = -k
    cxp, cyp = k * rx * y1p / ry, -k * ry * x1p / rx
    cx, cy = cp * cxp - sp * cyp + (x1 + x2) / 2, sp * cxp + cp * cyp + (y1 + y2) / 2
    a0 = math.atan2((y1p - cyp) / ry, (x1p - cxp) / rx)
    a1 = math.atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx)
    da = a1 - a0
    if sweep and da < 0:
        da += 2 * math.pi
    elif not sweep and da > 0:
        da -= 2 * math.pi
    pts = []
    for i in range(1, steps + 1):
        a = a0 + da * i / steps
        ex, ey = rx * math.cos(a), ry * math.sin(a)
        pts.append((cp * ex - sp * ey + cx, sp * ex + cp * ey + cy))
    return pts


def path_polylines(d, steps=24):
    """SVG path data (M L H V C A Z, absolute or relative) to a list of polylines."""
    tokens = re.findall(r"[MmLlHhVvCcAaZz]|-?(?:\d+\.?\d*|\.\d+)(?:e-?\d+)?", d)
    lines, cur = [], []
    x = y = sx = sy = 0.0
    i, cmd = 0, None

    def num():
        nonlocal i
        i += 1
        return float(tokens[i - 1])

    while i < len(tokens):
        if tokens[i].isalpha():
            cmd = tokens[i]
            i += 1
            if cmd in "Zz":
                if cur:
                    cur.append((sx, sy))
                    lines.append(cur)
                cur = []
                x, y = sx, sy
                continue
        rel = cmd.islower()
        c = cmd.upper()
        ox, oy = (x, y) if rel else (0, 0)
        if c == "M":
            if cur:
                lines.append(cur)
            x, y = num() + ox, num() + oy
            sx, sy = x, y
            cur = [(x, y)]
            cmd = "l" if rel else "L"
        elif c == "L":
            x, y = num() + ox, num() + oy
            cur.append((x, y))
        elif c == "H":
            x = num() + (x if rel else 0)
            cur.append((x, y))
        elif c == "V":
            y = num() + (y if rel else 0)
            cur.append((x, y))
        elif c == "C":
            x1, y1, x2, y2 = num() + ox, num() + oy, num() + ox, num() + oy
            ex, ey = num() + ox, num() + oy
            for s in range(1, steps + 1):
                t = s / steps
                u = 1 - t
                cur.append((u ** 3 * x + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t ** 3 * ex,
                            u ** 3 * y + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t ** 3 * ey))
            x, y = ex, ey
        elif c == "A":
            rx, ry, phi, large, sweep = num(), num(), num(), num(), num()
            ex, ey = num() + ox, num() + oy
            cur += _arc_points(x, y, rx, ry, phi, int(large), int(sweep), ex, ey, steps)
            x, y = ex, ey
    if cur:
        lines.append(cur)
    return lines


def transform(lines, scale, ox, oy):
    return [[(ox + px * scale, oy + py * scale) for px, py in line] for line in lines]


def stroke(lines, half):
    """Inside test for polylines stroked half a width either side, with round caps and joins."""
    segs = []
    for line in lines:
        for (x0, y0), (x1, y1) in zip(line, line[1:] or line):
            segs.append((min(x0, x1) - half, min(y0, y1) - half, max(x0, x1) + half, max(y0, y1) + half, x0, y0, x1, y1))

    def inside(px, py):
        for l, t, r, b, x0, y0, x1, y1 in segs:
            if l <= px <= r and t <= py <= b and seg_dist(px, py, x0, y0, x1, y1) <= half:
                return True
        return False

    return inside


def fill(lines):
    """Inside test for closed polylines, even-odd."""
    edges = [e for line in lines for e in zip(line, line[1:] + line[:1])]

    def inside(px, py):
        hit = False
        for (x0, y0), (x1, y1) in edges:
            if (y0 > py) != (y1 > py) and px < x0 + (py - y0) * (x1 - x0) / (y1 - y0):
                hit = not hit
        return hit

    return inside


# ---------- rendering ----------

def render(size, shapes):
    """shapes: list of (inside(px, py) -> bool, color(px, py) -> (r, g, b, a) or None to erase); painted in order."""
    pixels = []
    step = 1 / SS
    for j in range(size):
        row = []
        for i in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]  # premultiplied
            for sj in range(SS):
                for si in range(SS):
                    px, py = i + (si + 0.5) * step, j + (sj + 0.5) * step
                    r = g = b = a = 0.0
                    for inside, color in shapes:
                        if inside(px, py):
                            if color is None:
                                r = g = b = a = 0.0
                                continue
                            cr, cg, cb, ca = color(px, py)
                            r, g, b = cr * ca + r * (1 - ca), cg * ca + g * (1 - ca), cb * ca + b * (1 - ca)
                            a = ca + a * (1 - ca)
                    acc[0] += r
                    acc[1] += g
                    acc[2] += b
                    acc[3] += a
            n = SS * SS
            a = acc[3] / n
            if a > 0:
                row.append((acc[0] / n / a, acc[1] / n / a, acc[2] / n / a, a))
            else:
                row.append((0, 0, 0, 0))
        pixels.append(row)
    return pixels


def solid(rgb, alpha=1.0):
    return lambda px, py: (*rgb, alpha)


# ---------- tray icons ----------

def mouse_shapes(scale, ox, oy, colour, stroke_w, tint=0.0, line=(1.5, 8)):
    """The glyph on the 16-unit tray grid: a mouse body x3.5 y3.5 w8 h11 rx4, open 40 degrees either side of
    the top, and a line M7.5 1.5V8 down through the opening. The mouse doubles as the power symbol.

    Outlined, with the inside tinted when tint > 0. line gives where the middle line starts and ends.
    """
    x, y, w, h, r = ox + 3.5 * scale, oy + 3.5 * scale, 8 * scale, 11 * scale, 4 * scale
    cx, cy = x + r, y + r
    half = stroke_w / 2
    gap = math.radians(40)
    ends = [(cx + side * r * math.sin(gap), cy - r * math.cos(gap)) for side in (-1, 1)]
    ly0, ly1 = oy + line[0] * scale, oy + line[1] * scale

    def outline(px, py):
        if seg_dist(px, py, cx, ly0, cx, ly1) <= half:
            return True
        if any(math.hypot(px - ex, py - ey) <= half for ex, ey in ends):
            return True
        if abs(rrect_sdf(px, py, x, y, w, h, r)) > half:
            return False
        return py >= cy or abs(math.atan2(px - cx, cy - py)) >= gap

    shapes = []
    if tint > 0:
        shapes.append((lambda px, py: rrect_sdf(px, py, x, y, w, h, r) <= 0, solid(colour, tint)))
    shapes.append((outline, solid(colour)))
    return shapes


# Coffee on the 16-unit grid: a cup with a handle; on, it's two thirds full and steaming.
CUP = "M1.5 6.5H11.5V11A3.5 3.5 0 0 1 8 14.5H5A3.5 3.5 0 0 1 1.5 11Z"
HANDLE = "M11.5 7.5H12.5A2 2 0 0 1 12.5 11.5H11.5"
COFFEE = "M1.5 9H11.5V11A3.5 3.5 0 0 1 8 14.5H5A3.5 3.5 0 0 1 1.5 11Z"
STEAM = "M5 0.5C3.8 1.8 6.2 2.7 5 4.5M8 0.5C6.8 1.8 9.2 2.7 8 4.5"

# Doze's rays, beside the glyph moved 2 left and 1 down.
RAYS = "M12.5 0.5V2.5M15 2L13.6 3.4M15.5 5.5H13.5"

TINT = 0.3


def snap(size, centre=7.5):
    """How far to move a design so its middle line, at x = centre on the 16 grid, lands crisp: centred on a pixel
    when the stroke's whole pixels are odd, between two when even."""
    scale = size / 16
    c = centre * scale
    return (math.floor(c) + 0.5 if int(scale) % 2 else round(c)) - c


def tray(style, size, colour, on):
    scale = size / 16
    w = scale  # one unit
    if style == "Coffee":
        dx = snap(size, 1.5)
        shapes = []
        if on:
            shapes.append((fill(transform(path_polylines(COFFEE), scale, dx, 0)), solid(colour, TINT)))
            shapes.append((stroke(transform(path_polylines(STEAM), scale, dx, 0), w / 2), solid(colour)))
        shapes.append((stroke(transform(path_polylines(CUP) + path_polylines(HANDLE), scale, dx, 0), w / 2), solid(colour)))
        return render(size, shapes)
    if style == "Doze" and on:
        dx = snap(size, 5.5)
        shapes = mouse_shapes(scale, dx - 2 * scale, scale, colour, w, TINT)
        shapes.append((stroke(transform(path_polylines(RAYS), scale, dx, 0), w / 2), solid(colour)))
        return render(size, shapes)
    if style == "Rise" and not on:
        return render(size, mouse_shapes(scale, snap(size), 0, colour, w, line=(5.5, 8)))
    # Rise on, and Doze off: the plain glyph, tinted when on.
    return render(size, mouse_shapes(scale, snap(size), 0, colour, w, TINT if on else 0.0))


# ---------- app icon ----------

def squircle_limit(steps=2000):
    """The Android "squircle" icon mask Samsung's One UI uses, M50,0 C10,0 0,10 0,50 and so on round, as a
    table: for u = |x| from the centre over the half width, the most |y| can be."""
    pts = []
    for s in range(steps + 1):
        t = s / steps
        u = 1 - t
        # From the top middle (0, 1) to the left middle (1, 0), controls at (0.8, 1) and (1, 0.8), centred.
        pts.append((3 * u * u * t * 0.8 + 3 * u * t * t * 1 + t ** 3, u ** 3 + 3 * u * u * t * 1 + 3 * u * t * t * 0.8))
    table = [0.0] * (steps + 1)
    j = 0
    for k in range(steps + 1):
        target = k / steps
        while j < steps and pts[j + 1][0] < target:
            j += 1
        (u0, v0), (u1, v1) = pts[j], pts[min(j + 1, steps)]
        table[k] = v0 if u1 == u0 else v0 + (v1 - v0) * (target - u0) / (u1 - u0)
    return table


SQUIRCLE = squircle_limit()

# Blue at the top left to violet at the bottom right, saturated enough to hold its own beside other apps'
# icons on the taskbar at 16 px.
GRADIENT = [(0.0, "#1E6BFF"), (0.5, "#4B3FF2"), (1.0, "#8B2BE8")]


def gradient_at(t):
    t = min(max(t, 0), 1)
    for (t0, c0), (t1, c1) in zip(GRADIENT, GRADIENT[1:]):
        if t <= t1:
            a, b = hex_rgb(c0), hex_rgb(c1)
            f = (t - t0) / (t1 - t0)
            return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f, 1.0)
    return (*hex_rgb(GRADIENT[-1][1]), 1.0)


def app_icon(size):
    # Edge to edge, as big as the taskbar will show it.
    pad = 0
    side = size
    c = size / 2
    half = side / 2
    n = len(SQUIRCLE) - 1

    def tile(px, py):
        u, v = abs(px - c) / half, abs(py - c) / half
        return u <= 1 and v <= SQUIRCLE[min(n, int(u * n))]

    def grad(px, py):
        return gradient_at((px + py - 2 * pad) / (2 * side))

    shapes = [(tile, grad)]

    # The critter from the 24 grid, filled, with its ears a little bigger so it reads as a mouse rather than a
    # bear: ears r3.8 at (5.9, 7) and (18.1, 7), head 7.2 by 6.3 at (12, 14.2), eyes r1.1 at (9.6, 12.9) and
    # (14.4, 12.9), nose r0.9 at (12, 16.4). Its 19.8 units of width fill 68% of the tile, centred on its own
    # middle, (12, 11.85); any bigger and the white swamps the colour at 16 px.
    g = side * 0.68 / 19.8
    ox, oy = c - 12 * g, c - 11.85 * g

    def at(x, y):
        return ox + x * g, oy + y * g

    def disc(x, y, r):
        cx, cy = at(x, y)
        rr = (r * g) ** 2
        return lambda px, py: (px - cx) ** 2 + (py - cy) ** 2 <= rr

    def oval(x, y, rx, ry):
        cx, cy = at(x, y)
        ax, ay = rx * g, ry * g
        return lambda px, py: ((px - cx) / ax) ** 2 + ((py - cy) / ay) ** 2 <= 1

    white = solid((1, 1, 1))
    ink = solid(hex_rgb("#2A1240"))
    shapes += [(disc(5.9, 7, 3.8), white), (disc(18.1, 7, 3.8), white)]
    # Inner ears, and a hairline of the tile between ears and head, where there are pixels enough for them.
    if size >= 32:
        shapes += [(disc(6.2, 7.3, 2.1), solid(hex_rgb("#FFB3C8"))), (disc(17.8, 7.3, 2.1), solid(hex_rgb("#FFB3C8")))]
        shapes.append((oval(12, 14.2, 7.9, 7.0), grad))
    shapes.append((oval(12, 14.2, 7.2, 6.3), white))
    # Eyes bigger than the outline icon's, so they still read as eyes at 16 px; at 24 px that size runs them
    # together.
    eye = 1.6 if size <= 20 else 1.35 if size < 48 else 1.1
    shapes += [(disc(9.6, 12.9, eye), ink), (disc(14.4, 12.9, eye), ink), (disc(12, 16.4, 0.9), ink)]
    if size >= 48:
        shapes += [(disc(9.95, 12.5, 0.38), white), (disc(14.75, 12.5, 0.38), white)]
    return render(size, shapes)


# ---------- files ----------

def to_bgra(pixels):
    out = bytearray()
    for row in pixels:
        for r, g, b, a in row:
            out += bytes((round(b * 255), round(g * 255), round(r * 255), round(a * 255)))
    return out


def png(pixels):
    height, width = len(pixels), len(pixels[0])
    raw = bytearray()
    for row in pixels:
        raw.append(0)
        for r, g, b, a in row:
            raw += bytes((round(r * 255), round(g * 255), round(b * 255), round(a * 255)))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")


def dib(pixels):
    size = len(pixels)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    colour = bytearray()
    for row in reversed(pixels):
        colour += to_bgra([row])
    mask_row = ((size + 31) // 32) * 4
    return header + bytes(colour) + bytes(mask_row * size)


def write_ico(name, images):
    entries, blobs = [], []
    offset = 6 + 16 * len(images)
    for pixels in images:
        size = len(pixels)
        blob = png(pixels) if size >= 256 else dib(pixels)
        entries.append(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(blob), offset))
        blobs.append(blob)
        offset += len(blob)
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(images)))
        f.write(b"".join(entries))
        f.write(b"".join(blobs))


TRAY_STYLES = ("Coffee", "Doze", "Rise")


def centred(icon, width, height):
    """The icon in the middle of a transparent width x height image."""
    side = len(icon)
    left, top = (width - side) // 2, (height - side) // 2
    blank = (0, 0, 0, 0)
    return [[icon[j - top][i - left] if 0 <= i - left < side and 0 <= j - top < side else blank
             for i in range(width)] for j in range(height)]


# The Microsoft Store (MSIX) package's tiles: (file name, width, height, icon size), at 100% and 200% scale.
STORE_IMAGES = [
    ("Square150x150Logo.scale-100", 150, 150, 100), ("Square150x150Logo.scale-200", 300, 300, 200),
    ("Wide310x150Logo.scale-100", 310, 150, 100), ("Wide310x150Logo.scale-200", 620, 300, 200),
    ("Square44x44Logo.scale-100", 44, 44, 36), ("Square44x44Logo.scale-200", 88, 88, 72),
    ("StoreLogo.scale-100", 50, 50, 50), ("StoreLogo.scale-200", 100, 100, 100),
    ("SplashScreen.scale-100", 620, 300, 200), ("SplashScreen.scale-200", 1240, 600, 400),
] + [
    # Taskbar and Start list icons, edge to edge, with and without the accent plate behind them.
    (f"Square44x44Logo.targetsize-{s}{alt}", s, s, s)
    for s in (16, 24, 32, 48, 256) for alt in ("", "_altform-unplated")
]


def write_store_images():
    folder = os.path.join(OUT, "Store")
    os.makedirs(folder, exist_ok=True)
    icons = {}
    for name, width, height, side in STORE_IMAGES:
        if side not in icons:
            icons[side] = app_icon(side)
        with open(os.path.join(folder, name + ".png"), "wb") as f:
            f.write(png(centred(icons[side], width, height)))
        print(f"Store/{name}.png")


def main():
    os.makedirs(OUT, exist_ok=True)
    tray_sizes = (16, 20, 24, 32, 40, 48)
    # One colour, like the system's own tray icons: dark on a light taskbar, white on a dark one. Off is an
    # outline and on tints the inside lightly; a solid fill stood out as heavier than the icons beside it.
    for style in TRAY_STYLES:
        for state, on in (("On", True), ("Off", False)):
            for theme, colour in (("Light", hex_rgb("#1B1B1B")), ("Dark", hex_rgb("#FFFFFF"))):
                name = f"Tray{style}{state}{theme}.ico"
                write_ico(name, [tray(style, s, colour, on) for s in tray_sizes])
                print(name)
    write_ico("BusyMouse.ico", [app_icon(s) for s in (16, 20, 24, 32, 40, 48, 64, 256)])
    print("BusyMouse.ico")
    write_store_images()


if __name__ == "__main__":
    main()
