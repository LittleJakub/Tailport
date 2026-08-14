#!/usr/bin/env python3
"""Process the user's refresh-loop icon into tray states.

Source: black glyph on white background -> white (ON) and grey (OFF)
versions, background stripped to transparency, multi-size ICOs.
"""
import os
from PIL import Image

SRC = r"C:\Users\user\Downloads\two-clockwise-arrows-with-rectangular-rotation.png"
HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "assets")

GREY = (160, 160, 160)


def strip_white_bg(img: Image.Image) -> Image.Image:
    """White pixels -> transparent; glyph darkness becomes alpha."""
    data = list(img.convert("RGBA").getdata())
    new = [(0, 0, 0, ((255 - max(p[0], p[1], p[2])) * p[3]) // 255) for p in data]
    out = Image.new("RGBA", img.size)
    out.putdata(new)
    return out


def recolor(img: Image.Image, color) -> Image.Image:
    r, g, b, a = img.split()
    solid = Image.new("RGBA", img.size, color + (255,))
    solid.putalpha(a)
    return solid


def save_ico(img: Image.Image, path: str):
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    img.save(path, format="ICO", sizes=[(s, s) for s in sizes])


def main():
    src = Image.open(SRC)
    print("source:", src.size, src.mode)
    glyph = strip_white_bg(src)

    on = recolor(glyph, (255, 255, 255))
    off = recolor(glyph, GREY)

    on.save(os.path.join(ASSETS, "on.png"))
    off.save(os.path.join(ASSETS, "off.png"))
    save_ico(on, os.path.join(ASSETS, "on.ico"))
    save_ico(off, os.path.join(ASSETS, "off.ico"))

    # previews: ON/OFF at real tray sizes on dark and light backgrounds
    for size in (64, 32):
        for name, img in (("on", on), ("off", off)):
            g = img.resize((size, size), Image.LANCZOS)
            for label, bgc in (("dark", (32, 32, 36, 255)), ("light", (240, 240, 240, 255))):
                bg = Image.new("RGBA", (size * 4, size * 2 + 10), bgc)
                bg.alpha_composite(g, (size * 2, 5))
                bg.convert("RGB").save(os.path.join(ASSETS, f"{name}_{size}px_{label}.png"))
    print("done - on/off icons + previews in", ASSETS)


if __name__ == "__main__":
    main()
