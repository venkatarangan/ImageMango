"""Draws the LocalImageGen app icon (a mango with a leaf and an AI 'sparkle') and writes a multi-size .ico
(PNG-compressed entries) plus a 256 px preview PNG. Pure Python, no Pillow.
usage: python3 -I make_icon.py <out.ico> <preview.png>"""
import math
import struct
import sys
import zlib

SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]


def lerp(a, b, t):
    return a + (b - a) * t


def mix(c1, c2, t):
    t = max(0.0, min(1.0, t))
    return tuple(lerp(x, y, t) for x, y in zip(c1, c2))


def hexc(h):
    return tuple(int(h[i:i + 2], 16) / 255 for i in (1, 3, 5))


YELLOW, ORANGE, RED, DEEP = hexc("#FFD23F"), hexc("#FF9F1C"), hexc("#F2542D"), hexc("#B8322A")
LEAF_L, LEAF_D, STEM = hexc("#5CC45A"), hexc("#2E8B3E"), hexc("#6B4226")
OUTLINE = hexc("#5A1E12")
SPARK, SPARK_EDGE, SPARK_LINE = hexc("#FFF6D6"), hexc("#FFC21A"), hexc("#B87400")


def rot(x, y, cx, cy, deg):
    a = math.radians(deg)
    dx, dy = x - cx, y - cy
    return dx * math.cos(a) + dy * math.sin(a), -dx * math.sin(a) + dy * math.cos(a)


def mango_f(x, y):
    """<1 inside the mango body. A rotated ellipse bent into a kidney shape."""
    u, v = rot(x, y, 0.50, 0.58, -35)
    u += 0.22 * (v * v) - 0.03  # bend
    return (u / 0.33) ** 2 + (v / 0.39) ** 2


def leaf_f(x, y):
    u, v = rot(x, y, 0.69, 0.17, 30)
    return (u / 0.17) ** 2 + (v / 0.065) ** 2


def stem_in(x, y):
    u, v = rot(x, y, 0.565, 0.205, 25)
    return abs(u) < 0.022 and abs(v) < 0.07


def star_f(x, y, cx, cy, r):
    """Astroid 4-point star: <1 inside."""
    dx, dy = abs(x - cx) / r, abs(y - cy) / r
    return dx ** 0.5 + dy ** 0.5


def shade(x, y, px):
    """Colour (r,g,b,a) of one sample point in unit coordinates; px = size of one pixel in unit coords."""
    edge = max(1.2 * px, 0.022)  # outline width, at least ~1 px at small sizes
    # sparkles (drawn on top)
    for cx, cy, r in ((0.20, 0.21, 0.15), (0.10, 0.42, 0.075)):
        s = star_f(x, y, cx, cy, r)
        if s < 1:
            return (*(mix(SPARK, SPARK_EDGE, s ** 1.5) if s < 0.86 else SPARK_LINE), 1.0)
    # leaf
    lf = leaf_f(x, y)
    if lf < 1:
        if lf > 1 - edge * 9:
            return (*mix(LEAF_D, OUTLINE, 0.5), 1.0)
        u, v = rot(x, y, 0.69, 0.17, 30)
        c = mix(LEAF_L, LEAF_D, (v / 0.065 + 1) / 2)
        if abs(v) < 0.006 and u > -0.13:  # vein
            c = mix(c, LEAF_D, 0.8)
        return (*c, 1.0)
    if stem_in(x, y):
        return (*STEM, 1.0)
    # body
    f = mango_f(x, y)
    if f < 1:
        # distance to edge approximated from the implicit value
        if f > (1 - edge * 5.5):
            return (*OUTLINE, 1.0)
        t = (x - y + 0.55) / 1.1  # bottom-left (yellow) -> top-right (red)
        c = mix(YELLOW, ORANGE, t * 1.8) if t < 0.55 else mix(ORANGE, RED, (t - 0.55) / 0.45)
        c = mix(c, DEEP, max(0.0, f - 0.55) * 0.9)  # darker rim
        hl = math.hypot(x - 0.40, y - 0.47)  # soft highlight
        if hl < 0.13:
            c = mix(c, (1, 1, 1), (1 - hl / 0.13) * 0.55)
        return (*c, 1.0)
    return (0, 0, 0, 0.0)


def render(n):
    ss = 4 if n <= 64 else 3
    px = 1.0 / n
    rows = []
    for j in range(n):
        row = bytearray([0])
        for i in range(n):
            r = g = b = a = 0.0
            for sj in range(ss):
                for si in range(ss):
                    x = (i + (si + 0.5) / ss) / n
                    y = (j + (sj + 0.5) / ss) / n
                    cr, cg, cb, ca = shade(x, y, px)
                    r += cr * ca; g += cg * ca; b += cb * ca; a += ca
            k = ss * ss
            if a > 0:
                row += bytes((round(r / a * 255), round(g / a * 255), round(b / a * 255), round(a / k * 255)))
            else:
                row += b"\0\0\0\0"
        rows.append(bytes(row))
    return rows


def png(n, rows):
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    ihdr = struct.pack(">IIBBBBB", n, n, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(b"".join(rows), 9)) + chunk(b"IEND", b"")


def main():
    ico_path, preview_path = sys.argv[1], sys.argv[2]
    images = [(n, png(n, render(n))) for n in SIZES]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, blobs = b"", b""
    for n, data in images:
        entries += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(data), offset + len(blobs))
        blobs += data
    open(ico_path, "wb").write(header + entries + blobs)
    open(preview_path, "wb").write(dict(images)[256])
    print("wrote", ico_path, len(header + entries + blobs), "bytes")


main()
