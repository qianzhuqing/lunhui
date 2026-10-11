// 从示意图裁素材：`docs/dev/images/墨色江湖_*.png` 那批**示意图**本身就是
// 整数倍放大的像素画（`pngread.js::probeScale` 判倍数，武学图标那张是 6 倍），
// 所以一格一格裁下来是**逐像素无损**的原生 32×32，不是"像"，就是那张图本身。
//
// 这条路为什么优于从零画：风格＝用户已经认过的那版（15 §4.4「色值以示意图为准」同一批图），
// 且不需要联网、不需要第三方素材授权。
//
// 用法：
//   node tools/artgen/cut.js docs/dev/images/墨色江湖_武学图标.png --cell 240 --out DIR
//   node tools/artgen/cut.js <png> --cell 240 --out DIR --csv data/tables/skill_base.csv \
//        --group-col skill_kind --group-order passive,active
//   node tools/artgen/cut.js <png> --cell 240 --out DIR --hires 3 --montage FILE --zoom 6
//     --cell    宫格边长（源图像素）；不给就按 --cols 推
//     --scale   放大倍数；不给就用探针判
//     --cols    每行几格；不给就按 --cell 推（默认 16）
//     --hires   K：同时导一份 K 倍整数放大的（64／96 这类）。**放大不增细节**，
//               只是给"显示端按 K 倍摆、别再缩放"用；真加细节是另一道活（见交接单「精细化」）。
//     --montage 顺带拼一张联络表（按源图排布，宣纸底），给设计对着点格子用
//     --csv     按这张配置表给格子命名（`--name-col` 给列名，默认 skill_id）
//     --group-col ＋ --group-order：先按这一列**稳定分组**（例如武学那张表是
//               招式 35 在前、内功 30 在后，而示意图是内功段在前）——
//               "按配置排序"这句落到代码里就是这两条：**组按 group-order、组内按表序**。
//
// ⚠️ 裁出来的是**图**，不是"哪张对应哪个 id"——示意图里没有名字。
// 落进 `assets/` 之前必须把 宫格 → 行 id 的对应关系核准（别照序号猜）。

"use strict";

const fs = require("fs");
const path = require("path");
const { Canvas, encodePNG } = require("./pixel");
const { decode, at, probeScale } = require("./pngread");

function arg(name, def) {
  const i = process.argv.indexOf("--" + name);
  return i >= 0 ? process.argv[i + 1] : def;
}

const file = process.argv[2];
if (!file) {
  console.error("用法：node tools/artgen/cut.js <sheet.png> --cell N --out DIR [--scale N] [--cols N] [--hires K] [--montage F --zoom N]");
  process.exit(2);
}

const img = decode(file);
const cols = parseInt(arg("cols", "16"), 10);
const cell = parseInt(arg("cell", String(Math.floor(img.w / cols))), 10);
const scale = parseInt(arg("scale", "0"), 10) || probeScale(img);
const outDir = arg("out");
const hires = parseInt(arg("hires", "1"), 10);
const rows = Math.floor(img.h / cell);

/** 按配置表算"第 k 格该叫什么"：可选先按某一列稳定分组，组内保持表序。 */
function namesFromCsv(csvPath) {
  const lines = fs.readFileSync(csvPath, "utf8").split(/\r?\n/).filter((l) => l.trim());
  const head = lines[0].replace(/^\ufeff/, "").split(",");   // 表头带 UTF-8 BOM，先剥掉
  const nameCol = head.indexOf(arg("name-col", "skill_id"));
  const groupCol = arg("group-col") ? head.indexOf(arg("group-col")) : -1;
  if (nameCol < 0) throw new Error(`表里没有 ${arg("name-col", "skill_id")} 这一列`);
  let rowsCsv = lines.slice(1).map((l) => l.split(","));
  if (groupCol >= 0) {
    const order = (arg("group-order") || "")
      .split(",")
      .filter(Boolean);
    const seen = [...new Set(rowsCsv.map((r) => r[groupCol]))];
    const groups = order.length ? order : seen;
    const missing = seen.filter((g) => !groups.includes(g));
    if (missing.length) throw new Error(`--group-order 漏了这些组：${missing.join(",")}`);
    // 稳定分组：同一个组内的行仍按表里原来的先后
    rowsCsv = groups.flatMap((g) => rowsCsv.filter((r) => r[groupCol] === g));
  }
  return rowsCsv.map((r) => r[nameCol]);
}

console.log(`${path.basename(file)} ${img.w}×${img.h} 宫格 ${cell}px × ${cols} 列 ${rows} 行，放大 ${scale} 倍`);
if (scale < 2) console.log("⚠️ 倍数 <2：这张不像整数倍像素画，裁出来会脏");

const csvPath = arg("csv");
const names = csvPath ? namesFromCsv(csvPath) : null;
if (names) console.log(`命名表 ${path.basename(csvPath)}：${names.length} 行`);

if (outDir) fs.mkdirSync(outDir, { recursive: true });

const zoom = parseInt(arg("zoom", "6"), 10);
const montageFile = arg("montage");
let montage = null;
if (montageFile) {
  montage = new Canvas(cols * 32 * zoom, rows * 32 * zoom);
  montage.fill([0xf4, 0xec, 0xd9, 255]);
}

let written = 0;
let filled = 0;              // 已填格子的序号（按行优先）——命名表就是照它对齐的
const odd = [];
for (let r = 0; r < rows; r++) {
  for (let c = 0; c < cols; c++) {
    const cx0 = c * cell, cy0 = r * cell;
    let x0 = 1e9, y0 = 1e9, x1 = -1, y1 = -1;
    for (let y = cy0; y < Math.min(cy0 + cell, img.h); y++) {
      for (let x = cx0; x < Math.min(cx0 + cell, img.w); x++) {
        if (at(img, x, y)[3] > 8) {
          if (x < x0) x0 = x;
          if (y < y0) y0 = y;
          if (x > x1) x1 = x;
          if (y > y1) y1 = y;
        }
      }
    }
    if (x1 < 0) continue;                      // 空格子（未用的宫格），跳过
    // 画布是 32 逻辑像素，但图案只占中间 28（那圈圆底）——所以裁"以外接框中心为中心的
    // 32×scale 方块"，别拿外接框当画布（拿外接框会得到 28×28）。
    const ox = Math.round(x0 + (x1 - x0 + 1) / 2 - (32 * scale) / 2);
    const oy = Math.round(y0 + (y1 - y0 + 1) / 2 - (32 * scale) / 2);
    const w = x1 - x0 + 1, h = y1 - y0 + 1;
    if (w !== 28 * scale || h !== 28 * scale) odd.push(`r${r + 1}c${c + 1}=${w / scale}×${h / scale}`);

    if (outDir) {
      const label = names ? names[filled] : `r${r + 1}c${c + 1}`;
      if (names && !label) throw new Error(`第 ${filled + 1} 格没有对应的表行，命名表对不上`);
      for (const k of hires > 1 ? [1, hires] : [1]) {
        const icon = new Canvas(32 * k, 32 * k);
        for (let y = 0; y < 32 * k; y++) {
          for (let x = 0; x < 32 * k; x++) {
            icon.set(x, y, at(img, ox + Math.floor(x / k) * scale, oy + Math.floor(y / k) * scale));
          }
        }
        const name = `${label}${k > 1 ? `_x${k}` : ""}.png`;
        fs.writeFileSync(path.join(outDir, name), encodePNG(icon));
        written++;
      }
      filled++;
    }
    if (montage) {
      for (let y = 0; y < 32 * zoom; y++) {
        for (let x = 0; x < 32 * zoom; x++) {
          const p = at(img, ox + Math.floor(x / zoom) * scale, oy + Math.floor(y / zoom) * scale);
          if (p[3] > 8) montage.set(c * 32 * zoom + x, r * 32 * zoom + y, p);
        }
      }
    }
  }
}

if (montageFile) fs.writeFileSync(montageFile, encodePNG(montage));
console.log(`写出 ${written} 张${outDir ? " → " + outDir : ""}${montageFile ? "，联络表 " + montageFile : ""}`);
if (odd.length) console.log(`⚠️ ${odd.length} 格的外接框不是 28×28（可能是别的画法或空格）：${odd.slice(0, 12).join(" ")}`);
