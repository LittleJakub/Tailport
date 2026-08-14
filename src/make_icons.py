#!/usr/bin/env python3
"""Generate the tray app icon set: plugged / unplugged white glyphs.

Draws a minimalist cable+plug glyph on transparent, antialiased,
at 256px then downscales. Emits:
  assets/plugged.ico, assets/unplugged.ico   (multi-size, for the tray + exe)
  assets/plugged.png, assets/unplugged.png   (256px source)
  assets/menu_*.png                          (16px menu item icons)
Run with the Python311 interpreter.
"""
import os
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "assets")
os.makedirs(ASSETS, exist_ok=True)

S = 256  # master size, antialiased via supersampling


def draw_plug(connected: bool, color: tuple = (255, 255, 255, 255)) -> Image.Image:
    """Minimalist plug glyph, vertical composition. connected=True: the
    cable meets the plug head (one continuous shape). False: the cable is
    pulled up and away, leaving a clear gap above the head."""
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    WHITE = color
    # plug head (rounded rect), centered at x=120
    body = (88, 96, 152, 160)
    r = 20
    # two prongs pointing down
    prong_l = (100, 160, 112, 196)
    prong_r = (132, 160, 144, 196)
    # cable: vertical, centered x=120, width 14 (x 113..127)
    CABLE_X = 120
    CABLE_W = 14

    if connected:
        # one continuous shape: cable from the top down into the head
        d.line([(CABLE_X, 28), (CABLE_X, 96)], fill=WHITE, width=CABLE_W)
    else:
        # cable pulled away: short stub on the head + floating cable cap
        d.line([(CABLE_X, 96), (CABLE_X, 84)], fill=WHITE, width=CABLE_W)
        d.line([(CABLE_X, 28), (CABLE_X, 60)], fill=WHITE, width=CABLE_W)
        d.rounded_rectangle((104, 52, 136, 68), radius=8, fill=WHITE)  # cable end cap

    d.rounded_rectangle(body, radius=r, fill=WHITE)
    d.rectangle(prong_l, fill=WHITE)
    d.rectangle(prong_r, fill=WHITE)

    # soft antialias: supersample at 4x and downscale
    big = img.resize((S * 4, S * 4), Image.NEAREST)
    return big.resize((S, S), Image.LANCZOS)


def save_ico(img: Image.Image, path: str):
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    img.save(path, format="ICO", sizes=[(s, s) for s in sizes])


def menu_icon(draw_fn, name: str):
    """16px menu glyph on transparent."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    draw_fn(ImageDraw.Draw(img))
    img.save(os.path.join(ASSETS, f"menu_{name}.png"))


def main():
    plugged = draw_plug(True)
    unplugged = draw_plug(False)
    plugged_dark = draw_plug(True, (45, 45, 50, 255))
    unplugged_dark = draw_plug(False, (45, 45, 50, 255))
    plugged.save(os.path.join(ASSETS, "plugged.png"))
    unplugged.save(os.path.join(ASSETS, "unplugged.png"))
    save_ico(plugged, os.path.join(ASSETS, "plugged.ico"))
    save_ico(unplugged, os.path.join(ASSETS, "unplugged.ico"))
    save_ico(plugged_dark, os.path.join(ASSETS, "plugged_dark.ico"))
    save_ico(unplugged_dark, os.path.join(ASSETS, "unplugged_dark.ico"))

    # --- 16px menu icons ---
    W = (255, 255, 255, 255)
    menu_icon(lambda d: d.line([(3, 8), (13, 8)], fill=W, width=2) or d.line([(8, 3), (8, 13)], fill=W, width=2), "power")
    menu_icon(lambda d: d.rectangle((2, 6, 14, 12), outline=W, width=2), "check")
    menu_icon(lambda d: d.polygon([(8, 2), (14, 6), (8, 14), (2, 6)], outline=W, width=2), "refresh")
    menu_icon(lambda d: d.rectangle((2, 3, 14, 13), outline=W, width=2) or d.line([(2, 6), (14, 6)], fill=W, width=2), "folder")
    menu_icon(lambda d: d.rectangle((3, 2, 13, 14), outline=W, width=2) or d.line([(3, 5), (13, 5)], fill=W, width=2), "log")
    menu_icon(lambda d: d.line([(4, 4), (12, 12)], fill=W, width=2) or d.line([(12, 4), (4, 12)], fill=W, width=2), "quit")

    print("icons written to", ASSETS)
    for f in sorted(os.listdir(ASSETS)):
        print(" ", f)


if __name__ == "__main__":
    main()
