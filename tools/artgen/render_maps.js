// 把 `scenes/maps/*.tscn` 按新图集画成 PNG（区域地图预览）。
//
// 为什么要自己渲染：这台机器上没有 Godot，跑不了引擎也跑不了 `tools/mapgen/render_preview.py`；
// 而「换了瓦片到底长什么样」必须看一眼才知道。这里直接读场景里的 `tile_map_data`
// （4 字节头 + 每格 12 字节：x, y, source, atlas_x, atlas_y, alternative），
// 按图层顺序铺图，再把 Marker 上的图标叠上去——渲染结果与场景数据是同一份来源，
// 不是另画一张概念图。

"use strict";

const fs = require("fs");
const path = require("path");
const { Canvas, encodePNG } = require("./pixel");
const { TILE, buildAtlas, buildFogSeamless } = require("./tiles");
const { buildAll: buildIcons } = require("./icons");

// ---------------------------------------------------------------- tscn 解析

/** 够用的 .tscn 解析：只要 node 的顺序、类型、父节点与几条属性。 */
function parseScene(text) {
  const ext = new Map();
  for (const m of text.matchAll(/\[ext_resource type="([^"]+)"(?: uid="[^"]*")? path="([^"]+)" id="([^"]+)"\]/g)) {
    ext.set(m[3], { type: m[1], path: m[2] });
  }
  const nodes = [];
  const lines = text.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const m = /^\[node name="([^"]+)" type="([^"]+)"(?: parent="([^"]*)")?/.exec(lines[i]);
    if (!m) continue;
    const node = { name: m[1], type: m[2], parent: m[3] || null, props: {} };
    for (let j = i + 1; j < lines.length; j++) {
      if (lines[j].startsWith("[")) break;
      const p = /^([a-z_0-9]+) = (.+)$/.exec(lines[j]);
      if (p) node.props[p[1]] = p[2];
    }
    nodes.push(node);
  }
  return { ext, nodes };
}

function vector2(text) {
  const m = /Vector2\(([-0-9.]+), ([-0-9.]+)\)/.exec(text || "");
  return m ? [parseFloat(m[1]), parseFloat(m[2])] : [0, 0];
}

function decodeTiles(props) {
  const m = /PackedByteArray\("([^"]*)"\)/.exec(props.tile_map_data || "");
  if (!m) return [];
  const buf = Buffer.from(m[1], "base64");
  const head = buf.length % 12;
  const cells = [];
  for (let i = 0; i < (buf.length - head) / 12; i++) {
    const o = head + i * 12;
    cells.push({
      x: buf.readInt16LE(o),
      y: buf.readInt16LE(o + 2),
      source: buf.readInt16LE(o + 4),
      ax: buf.readInt16LE(o + 6),
      ay: buf.readInt16LE(o + 8),
    });
  }
  return cells;
}

// ---------------------------------------------------------------- 渲染

const SKIP_LAYERS = new Set(["Fog"]); // 默认不画雾层：预览看的是「美术长什么样」，雾会把整张图盖住

function themeOfScene(ext, nodes) {
  for (const node of nodes) {
    const ref = /ExtResource\("([^"]+)"\)/.exec(node.props.tile_set || "");
    if (!ref) continue;
    const res = ext.get(ref[1]);
    if (!res) continue;
    const m = /tilesets\/([^/]+)\//.exec(res.path);
    if (m) return m[1];
  }
  return null;
}

function renderScene(scenePath, scale, options) {
  const text = fs.readFileSync(scenePath, "utf8");
  const { ext, nodes } = parseScene(text);
  const theme = themeOfScene(ext, nodes);
  if (!theme) return null;

  const withFog = !!(options && options.withFog);
  const layers = nodes.filter(
    (n) => n.type === "TileMapLayer" && (withFog || !SKIP_LAYERS.has(n.name)),
  );
  let maxX = 0;
  let maxY = 0;
  for (const layer of layers) {
    for (const cell of decodeTiles(layer.props)) {
      maxX = Math.max(maxX, cell.x);
      maxY = Math.max(maxY, cell.y);
    }
  }
  const w = (maxX + 1) * TILE;
  const h = (maxY + 1) * TILE;

  const atlas = buildAtlas(theme);
  const fog = buildFogSeamless();
  const out = new Canvas(w, h);
  for (const layer of layers) {
    for (const cell of decodeTiles(layer.props)) {
      const src = cell.source === 1 ? fog : atlas;
      if (cell.source !== 1 && (cell.ax * TILE + TILE > atlas.w || cell.ay * TILE + TILE > atlas.h)) continue;
      for (let y = 0; y < TILE; y++) {
        for (let x = 0; x < TILE; x++) {
          const p = src.get(cell.ax * TILE + x, cell.ay * TILE + y);
          if (p[3] === 0) continue;
          if (p[3] === 255) out.set(cell.x * TILE + x, cell.y * TILE + y, p);
          else out.blend(cell.x * TILE + x, cell.y * TILE + y, p, p[3] / 255);
        }
      }
    }
  }

  // Marker 上的图标：位置 = 沿途所有父节点的 position 之和
  const byName = new Map();
  for (const node of nodes) byName.set(node.name, node);
  const worldPos = (node) => {
    let [x, y] = vector2(node.props.position);
    let parent = node.parent;
    while (parent && parent !== ".") {
      const key = parent.split("/").pop();
      const p = byName.get(key);
      if (!p) break;
      const [px, py] = vector2(p.props.position);
      x += px;
      y += py;
      parent = p.parent;
    }
    return [x, y];
  };

  const icons = buildIcons();
  const drawn = [];
  for (const node of nodes) {
    if (node.type !== "Sprite2D") continue;
    const ref = /ExtResource\("([^"]+)"\)/.exec(node.props.texture || "");
    if (!ref) continue;
    const res = ext.get(ref[1]);
    if (!res) continue;
    const file = path.basename(res.path);
    const canvas = icons[file];
    if (!canvas) continue;
    const [x, y] = worldPos(node);
    out.blit(canvas, Math.round(x - canvas.w / 2), Math.round(y - canvas.h / 2));
    drawn.push({ file, x, y, theme });
  }

  return { canvas: scale ? out.scaled(scale) : out, theme, w, h, icons: drawn };
}

/** 按**格**裁一块出来（黑风寨三层纵向堆叠在一张图上，得裁开才看得清）。 */
function cropCells(scenePath, [cx, cy, cw, ch], scale) {
  const rendered = renderScene(scenePath, 1);
  if (!rendered) return null;
  const out = new Canvas(cw * TILE, ch * TILE);
  for (let y = 0; y < ch * TILE; y++) {
    for (let x = 0; x < cw * TILE; x++) {
      out.set(x, y, rendered.canvas.get(cx * TILE + x, cy * TILE + y));
    }
  }
  return scale ? out.scaled(scale) : out;
}

module.exports = { renderScene, cropCells, parseScene, decodeTiles, themeOfScene, encodePNG };
