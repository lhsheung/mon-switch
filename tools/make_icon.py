#!/usr/bin/env python3
"""Generate the mon-switch application icon.

Produces a multi-resolution .ico so the notification area stays crisp from 100% to 200%
display scaling. Frames up to 64x64 are written as 32bpp DIBs (maximum compatibility);
128 and 256 are PNG-compressed to keep the file small.

Only the Python standard library is used - no Pillow, no build dependency.

Usage:
    python tools/make_icon.py [output.ico]
"""

from __future__ import annotations

import os
import struct
import sys
import zlib

# Sizes written as uncompressed DIB frames. Windows uses 16px in the notification area,
# 20/24px at 125%/150% scaling and 32px in the Alt-Tab dialog.
DIB_SIZES = (16, 20, 24, 32, 48, 64)

# Sizes written as PNG frames (Vista and later read these).
PNG_SIZES = (128, 256)

# Supersampling factor used to fake anti-aliasing. 4 is the sweet spot between edge
# quality and the pure-Python render time (the whole icon takes a few seconds).
SS = 4

# Palette
BG_TOP = (59, 130, 246)
BG_BOTTOM = (29, 78, 216)
BODY = (248, 250, 252)
SCREEN = (15, 23, 42)
TRANSPARENT = None


def inside_round_rect(x: float, y: float, x0: float, y0: float, x1: float, y1: float, r: float) -> bool:
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    # Corner circles
    for cx, cy in ((x0 + r, y0 + r), (x1 - r, y0 + r), (x0 + r, y1 - r), (x1 - r, y1 - r)):
        if (x < x0 + r or x > x1 - r) and (y < y0 + r or y > y1 - r):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                return True
    return not ((x < x0 + r or x > x1 - r) and (y < y0 + r or y > y1 - r))


def inside_rect(x: float, y: float, x0: float, y0: float, x1: float, y1: float) -> bool:
    return x0 <= x <= x1 and y0 <= y <= y1


def color_at(x: float, y: float):
    """x and y are normalised to 0..1."""
    # Monitor screen
    in_screen = inside_round_rect(x, y, 0.175, 0.255, 0.825, 0.585, 0.030)
    # Divider drawn on top of the screen: hints at "two displays".
    in_divider = inside_rect(x, y, 0.492, 0.255, 0.508, 0.585)

    if in_screen and in_divider:
        return BODY
    if in_screen:
        return SCREEN

    # Monitor body, neck and foot
    if inside_round_rect(x, y, 0.120, 0.200, 0.880, 0.640, 0.055):
        return BODY
    if inside_rect(x, y, 0.455, 0.640, 0.545, 0.752):
        return BODY
    if inside_round_rect(x, y, 0.315, 0.752, 0.685, 0.845, 0.045):
        return BODY

    # Rounded square background with a vertical gradient
    if inside_round_rect(x, y, 0.0, 0.0, 1.0, 1.0, 0.220):
        return (
            round(BG_TOP[0] + (BG_BOTTOM[0] - BG_TOP[0]) * y),
            round(BG_TOP[1] + (BG_BOTTOM[1] - BG_TOP[1]) * y),
            round(BG_TOP[2] + (BG_BOTTOM[2] - BG_TOP[2]) * y),
        )

    return TRANSPARENT


def render(size: int) -> bytes:
    """Render one frame as straight RGBA, top-down."""
    rgb = bytearray(size * size * 4)
    step = 1.0 / (size * SS)

    for py in range(size):
        for px in range(size):
            r = g = b = 0
            a = 0
            for sy in range(SS):
                for sx in range(SS):
                    x = (px * SS + sx + 0.5) * step
                    y = (py * SS + sy + 0.5) * step
                    sample = color_at(x, y)
                    if sample is not None:
                        r += sample[0]
                        g += sample[1]
                        b += sample[2]
                        a += 255

            total = SS * SS
            if a == 0:
                continue

            # Average only over covered samples so edges keep their colour, not a dark fringe.
            coverage = a // total
            covered = a // 255
            offset = (py * size + px) * 4
            rgb[offset] = r // covered
            rgb[offset + 1] = g // covered
            rgb[offset + 2] = b // covered
            rgb[offset + 3] = coverage

    return bytes(rgb)


def png_bytes(size: int, rgba: bytes) -> bytes:
    raw = bytearray()
    for y in range(size):
        raw.append(0)  # filter type 0
        raw += rgba[y * size * 4 : (y + 1) * size * 4]

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )


def dib_bytes(size: int, rgba: bytes) -> bytes:
    """32bpp BITMAPINFOHEADER frame, bottom-up, with a 1bpp AND mask."""
    header = struct.pack(
        "<IiiHHIIiiII",
        40,          # biSize
        size,        # biWidth
        size * 2,    # biHeight (XOR + AND)
        1,           # biPlanes
        32,          # biBitCount
        0,           # biCompression
        size * size * 4,
        0,
        0,
        0,
        0,
    )

    xor = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            offset = (y * size + x) * 4
            r, g, b, a = rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]
            xor += bytes((b, g, r, a))

    # AND mask: 1 = transparent. Rows are padded to 4 bytes and stored bottom-up.
    stride = ((size + 31) // 32) * 4
    mask = bytearray()
    for y in range(size - 1, -1, -1):
        row = bytearray(stride)
        for x in range(size):
            if rgba[(y * size + x) * 4 + 3] < 128:
                row[x // 8] |= 0x80 >> (x % 8)
        mask += row

    return header + bytes(xor) + bytes(mask)


def build_ico(path: str) -> None:
    frames: list[tuple[int, bytes]] = []
    for size in DIB_SIZES:
        frames.append((size, dib_bytes(size, render(size))))
    for size in PNG_SIZES:
        frames.append((size, png_bytes(size, render(size))))

    count = len(frames)
    directory = bytearray()
    payload = bytearray()
    offset = 6 + (16 * count)

    for size, data in frames:
        directory += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,
            0 if size >= 256 else size,
            0,               # colour count
            0,               # reserved
            1,               # planes
            32,              # bit count
            len(data),
            offset,
        )
        payload += data
        offset += len(data)

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(struct.pack("<HHH", 0, 1, count))
        handle.write(directory)
        handle.write(payload)

    print(f"wrote {path}")
    print(f"  frames : {', '.join(str(s) for s, _ in frames)}")
    print(f"  bytes  : {os.path.getsize(path)}")


def main() -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    default = os.path.join(here, "..", "src", "mon-switch", "Resources", "app.ico")
    target = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.abspath(default)
    build_ico(target)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
