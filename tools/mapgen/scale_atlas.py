"""Derive the 32x32 atlas from the 16x16 CC0 source pack.

    07_地图资源需求.md 定的是 32x32 瓦片；Kenney Tiny Town 是 16x16 的占位美术。
    这里用最近邻放大 2 倍凑合规格径，正式美术到位后直接替换输出文件即可。

Usage:
    python tools/mapgen/scale_atlas.py
"""

import os

from PIL import Image

SRC = os.path.join("assets", "tilesets", "kenney_tiny_town", "tilemap_packed.png")
OUT = os.path.join("assets", "tilesets", "_common", "tiny_town_32.png")
SCALE = 2


def main():
    image = Image.open(SRC).convert("RGBA")
    scaled = image.resize((image.width * SCALE, image.height * SCALE), Image.NEAREST)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    scaled.save(OUT)
    print(f"{SRC} {image.width}x{image.height} -> {OUT} {scaled.width}x{scaled.height} "
          f"({scaled.width // 32} x {scaled.height // 32} tiles of 32px)")


if __name__ == "__main__":
    main()
