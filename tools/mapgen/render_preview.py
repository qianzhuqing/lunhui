"""Render a PNG preview from the JSON dump written by the map builders.

Usage:
    python tools/mapgen/render_preview.py <dump.json> <out.png> [project_root] [hide_layers]

`hide_layers` is a comma separated layer list (e.g. "Fog") for when you want the
reference picture without fog on top.

It only reads the dump + the PNGs it points at, so it works without launching
Godot -- useful for eyeballing a map change without opening the editor.
"""

import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

TILE = 16
SCALE = 2

# 颜色按 Marker 名字前缀走（07_地图资源需求.md 第二节的绑约），一眼分得清类别。
MARKER_STYLE = {
    "Node_": ((255, 235, 120, 255), (40, 30, 0, 255)),
    "Portal_": ((255, 255, 255, 255), (60, 60, 60, 255)),
    "Spawn_": ((230, 70, 70, 255), (255, 255, 255, 255)),
    "Room_": ((40, 170, 255, 255), (255, 255, 255, 255)),
    "Chest_": ((255, 190, 60, 255), (60, 40, 0, 255)),
    "Event_": ((60, 200, 90, 255), (255, 255, 255, 255)),
    "Trigger_": ((190, 90, 220, 255), (255, 255, 255, 255)),
    "Team_": ((240, 120, 40, 255), (255, 255, 255, 255)),
    "Exit_": ((120, 230, 230, 255), (20, 40, 40, 255)),
}
MARKER_DEFAULT = ((255, 255, 255, 255), (0, 0, 0, 255))


def marker_style(name):
    for prefix, style in MARKER_STYLE.items():
        if name.startswith(prefix):
            return style
    return MARKER_DEFAULT


def tile_img(atlas, coord):
    col, row = coord
    return atlas.crop((col * TILE, row * TILE, col * TILE + TILE, row * TILE + TILE))


def resolve(path, project_root):
    return path.replace("res://", project_root.rstrip("/\\") + os.sep)


def main(dump_path, out_path, project_root=".", hide_layers="", crop="", scale=0):
    with open(dump_path, encoding="utf-8") as handle:
        payload = json.load(handle)

    global SCALE, TILE
    TILE = int(payload.get("tile_px", 16))
    if scale:
        SCALE = scale
    elif payload["map_h"] > 34 or payload["map_w"] > 44:
        SCALE = 1

    crop_rows = None
    if crop:
        row0, row1 = crop.split(":")
        crop_rows = (int(row0), int(row1))

    hidden = {name.strip() for name in hide_layers.split(",") if name.strip()}
    sources = {}
    for entry in payload.get("sources", [{"id": 0, "texture": payload["atlas"]}]):
        sources[entry["id"]] = Image.open(resolve(entry["texture"], project_root)).convert("RGBA")
    atlas = sources[0]

    width = payload["map_w"] * TILE
    height = payload["map_h"] * TILE
    canvas = Image.new("RGBA", (width, height), (255, 0, 255, 255))

    for layer in payload["layers"]:
        if layer["name"] in hidden:
            continue
        for x, y, source, col, row in layer["cells"]:
            cell = tile_img(sources.get(source, atlas), (col, row))
            canvas.alpha_composite(cell, (x * TILE, y * TILE))

    canvas = canvas.resize((width * SCALE, height * SCALE), Image.NEAREST)
    draw = ImageDraw.Draw(canvas)

    # 先画精灵（NPC 占位图），标记再压上去，免得圈和名字被盖住。
    sprite_cache = {}
    for sprite in payload.get("sprites", []):
        path = sprite["texture"]
        if path not in sprite_cache:
            sprite_cache[path] = Image.open(resolve(path, project_root)).convert("RGBA")
        image = sprite_cache[path]
        image = image.resize((image.width * SCALE, image.height * SCALE), Image.NEAREST)
        px = int(sprite["pos"][0] * SCALE - image.width / 2)
        py = int(sprite["pos"][1] * SCALE - image.height / 2)
        canvas.alpha_composite(image, (px, py))

    for path in payload["paths"]:
        points = [(px * SCALE, py * SCALE) for px, py in path["points"]]
        if len(points) > 1:
            draw.line(points + [points[0]], fill=(0, 90, 255, 255), width=3)
        for px, py in points:
            draw.ellipse([px - 4, py - 4, px + 4, py + 4], fill=(0, 90, 255, 255))
        draw.text((points[0][0] + 6, points[0][1] - 20), path["name"], fill=(0, 60, 200, 255))

    try:
        font = ImageFont.load_default(size=14)
    except TypeError:
        font = ImageFont.load_default()

    for marker in payload["markers"]:
        px, py = marker["pos"][0] * SCALE, marker["pos"][1] * SCALE
        ring, inner = marker_style(marker["name"])
        radius = 9 if marker["name"].startswith("Node_") else 6
        draw.ellipse([px - radius - 2, py - radius - 2, px + radius + 2, py + radius + 2], fill=inner)
        draw.ellipse([px - radius, py - radius, px + radius, py + radius], outline=ring, width=3)
        draw.text((px + radius + 4, py - 8), marker["name"], fill=(0, 0, 0, 255), font=font)

    if crop_rows:
        row0, row1 = crop_rows
        canvas = canvas.crop((0, row0 * TILE * SCALE, canvas.width, row1 * TILE * SCALE))
    canvas.convert("RGB").save(out_path)
    print(f"wrote {out_path} {canvas.width}x{canvas.height} "
          f"({len(payload['markers'])} markers, {len(payload['paths'])} paths)")


if __name__ == "__main__":
    dump = sys.argv[1] if len(sys.argv) > 1 else ".logs/overworld_preview.json"
    out = sys.argv[2] if len(sys.argv) > 2 else ".logs/overworld_preview.png"
    root = sys.argv[3] if len(sys.argv) > 3 else "."
    hide = sys.argv[4] if len(sys.argv) > 4 else ""
    crop = sys.argv[5] if len(sys.argv) > 5 else ""
    zoom = int(sys.argv[6]) if len(sys.argv) > 6 else 0
    main(dump, out, root, hide, crop, zoom)
