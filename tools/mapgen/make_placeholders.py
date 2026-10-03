"""Generate every placeholder asset the maps need but no free pack ships.

    07_地图资源需求.md 第八节列了一大堆美术需求；正式美术没到之前，
    这里生成「一眼能看出是占位」的替代品，让地图能先跑、能验收结构。

Usage:
    python tools/mapgen/make_placeholders.py

Outputs (all 32x32, 与瓦片同格径):
    assets/tilesets/_common/fog_tile.png        探索迷雾（逐格铺）
    assets/sprites/icons/icon_<id>.png          大地图 7 个地标图标
    assets/sprites/icons/icon_<id>_dim.png      同上，未解锁/未探索的灰态
    assets/sprites/icons/icon_highlight.png     当前所在地高亮环
    assets/sprites/props/chest_<grade>.png      宝箱（铜/银/金）
    assets/sprites/props/chest_<grade>_open.png 已开
    assets/sprites/props/brazier_off.png        火盆 灭
    assets/sprites/props/brazier_on.png         火盆 燃
    assets/sprites/props/prop_placeholder.png   通用可交互物占位
    assets/sprites/characters/npc_placeholder.png  NPC 占位
"""

import os

from PIL import Image, ImageDraw

SIZE = 32

TILESET_DIR = os.path.join("assets", "tilesets", "_common")
ICON_DIR = os.path.join("assets", "sprites", "icons")
PROP_DIR = os.path.join("assets", "sprites", "props")
CHAR_DIR = os.path.join("assets", "sprites", "characters")

# 图标底色按地标类型分，和 map_region.node_type 对齐。
ICON_COLOURS = {
    "town": (196, 152, 62),
    "post": (72, 152, 176),
    "wild": (86, 150, 74),
    "dungeon": (150, 66, 66),
    "cave": (120, 120, 130),
    "ruin": (146, 108, 74),
    "ferry": (74, 116, 168),
}


def blank():
    return Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))


def dashed_box(draw, colour):
    for x in range(1, SIZE - 1, 2):
        draw.point((x, 1), fill=colour)
        draw.point((x, SIZE - 2), fill=colour)
    for y in range(1, SIZE - 1, 2):
        draw.point((1, y), fill=colour)
        draw.point((SIZE - 2, y), fill=colour)


def make_fog():
    """半透明云雾，逐格铺；四边留平，拼接不出现缝。"""
    image = Image.new("RGBA", (SIZE, SIZE), (206, 216, 228, 236))
    draw = ImageDraw.Draw(image)
    for cx, cy, r, colour in (
        (10, 10, 6, (232, 240, 248, 226)),
        (24, 8, 4, (232, 240, 248, 222)),
        (16, 23, 6, (186, 198, 212, 228)),
        (6, 25, 4, (186, 198, 212, 226)),
        (27, 25, 4, (232, 240, 248, 224)),
    ):
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=colour)
    return image


def make_npc_placeholder():
    image = blank()
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle([1, 1, SIZE - 2, SIZE - 2], radius=5, fill=(70, 80, 95, 90))
    dashed_box(draw, (240, 245, 255, 220))
    draw.ellipse([11, 6, 20, 15], fill=(120, 130, 148, 235))
    draw.rounded_rectangle([8, 17, 23, 30], radius=4, fill=(120, 130, 148, 235))
    return image


def make_prop_placeholder():
    image = blank()
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle([2, 2, SIZE - 3, SIZE - 3], radius=4, fill=(190, 175, 120, 110))
    dashed_box(draw, (255, 240, 180, 220))
    draw.rectangle([12, 12, 19, 19], fill=(150, 135, 90, 235))
    return image


def make_icon(kind, dim=False):
    """一个圆角徽章 + 类型剪影。dim 版本整体压暗降透明度。"""
    image = blank()
    draw = ImageDraw.Draw(image)
    base = ICON_COLOURS[kind]
    if dim:
        base = tuple(int(channel * 0.55) for channel in base)
    alpha = 130 if dim else 255
    draw.rounded_rectangle([2, 2, SIZE - 3, SIZE - 3], radius=7, fill=base + (alpha,))
    ink = (250, 246, 235, alpha)
    shadow = (30, 26, 22, alpha)
    if kind == "town":
        draw.polygon([(7, 15), (16, 7), (25, 15)], fill=shadow)
        draw.rectangle([10, 15, 22, 25], fill=ink)
        draw.rectangle([14, 19, 18, 25], fill=shadow)
    elif kind == "post":
        draw.rectangle([15, 8, 17, 26], fill=ink)
        draw.rectangle([8, 11, 24, 15], fill=shadow)
        draw.rectangle([10, 18, 22, 21], fill=ink)
    elif kind == "wild":
        for x in (8, 16, 24):
            draw.polygon([(x, 22), (x - 4, 22), (x, 12)], fill=ink)
        draw.rectangle([4, 24, 28, 26], fill=shadow)
    elif kind == "dungeon":
        draw.rectangle([7, 13, 25, 26], fill=ink)
        for x in (7, 13, 19, 25):
            draw.rectangle([x, 9, x + 3, 13], fill=ink)
        draw.rectangle([13, 19, 19, 26], fill=shadow)
    elif kind == "cave":
        draw.pieslice([5, 8, 27, 30], 180, 360, fill=ink)
        draw.pieslice([11, 17, 21, 29], 180, 360, fill=shadow)
    elif kind == "ruin":
        draw.rectangle([6, 14, 14, 26], fill=ink)
        draw.rectangle([17, 18, 26, 26], fill=ink)
        draw.rectangle([14, 8, 18, 14], fill=shadow)
    elif kind == "ferry":
        draw.polygon([(5, 18), (27, 18), (23, 26), (9, 26)], fill=ink)
        draw.rectangle([15, 7, 17, 18], fill=shadow)
        draw.polygon([(17, 8), (25, 14), (17, 14)], fill=ink)
    return image


def make_highlight():
    image = blank()
    draw = ImageDraw.Draw(image)
    draw.ellipse([1, 1, SIZE - 2, SIZE - 2], outline=(255, 226, 120, 235), width=3)
    return image


CHEST_COLOURS = {
    "copper": ((168, 112, 62), (120, 78, 42)),
    "silver": ((188, 194, 205), (132, 138, 150)),
    "gold": ((232, 190, 74), (170, 132, 40)),
}


def make_chest(grade, open_state):
    body, dark = CHEST_COLOURS[grade]
    image = blank()
    draw = ImageDraw.Draw(image)
    draw.rectangle([4, 14, 27, 27], fill=body + (255,), outline=dark + (255,))
    draw.rectangle([4, 22, 27, 27], fill=dark + (255,))
    if open_state:
        draw.rectangle([4, 6, 27, 12], fill=dark + (255,))
        draw.rectangle([6, 14, 25, 20], fill=(40, 34, 28, 255))
        draw.rectangle([4, 13, 27, 14], fill=dark + (255,))
    else:
        draw.rectangle([4, 9, 27, 15], fill=body + (255,), outline=dark + (255,))
        draw.rectangle([4, 14, 27, 16], fill=dark + (255,))
        draw.rectangle([14, 12, 17, 18], fill=(238, 214, 120, 255))
    return image


def make_brazier(lit):
    image = blank()
    draw = ImageDraw.Draw(image)
    draw.rectangle([12, 20, 19, 28], fill=(90, 84, 78, 255))
    draw.polygon([(6, 14), (25, 14), (21, 22), (10, 22)], fill=(120, 112, 104, 255))
    draw.ellipse([8, 11, 23, 17], fill=(58, 54, 50, 255))
    if lit:
        draw.polygon([(15, 3), (21, 13), (15, 15), (11, 13)], fill=(240, 140, 50, 255))
        draw.polygon([(15, 6), (18, 12), (15, 14), (13, 12)], fill=(252, 214, 96, 255))
    else:
        draw.ellipse([11, 11, 15, 14], fill=(80, 76, 72, 255))
        draw.ellipse([17, 12, 20, 15], fill=(80, 76, 72, 255))
    return image


def save(image, directory, name):
    os.makedirs(directory, exist_ok=True)
    path = os.path.join(directory, name)
    image.save(path)
    return path


def main():
    written = [save(make_fog(), TILESET_DIR, "fog_tile.png")]
    written.append(save(make_npc_placeholder(), CHAR_DIR, "npc_placeholder.png"))
    written.append(save(make_prop_placeholder(), PROP_DIR, "prop_placeholder.png"))
    written.append(save(make_highlight(), ICON_DIR, "icon_highlight.png"))
    for kind in ICON_COLOURS:
        written.append(save(make_icon(kind), ICON_DIR, f"icon_{kind}.png"))
        written.append(save(make_icon(kind, dim=True), ICON_DIR, f"icon_{kind}_dim.png"))
    for grade in CHEST_COLOURS:
        written.append(save(make_chest(grade, False), PROP_DIR, f"chest_{grade}.png"))
        written.append(save(make_chest(grade, True), PROP_DIR, f"chest_{grade}_open.png"))
    written.append(save(make_brazier(False), PROP_DIR, "brazier_off.png"))
    written.append(save(make_brazier(True), PROP_DIR, "brazier_on.png"))
    print(f"wrote {len(written)} placeholder files")


if __name__ == "__main__":
    main()
