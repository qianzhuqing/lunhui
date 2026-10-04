// 石隙迷窟「死路画法」示意图（给设计看的一页）。
//
//   node tools/artgen/deadend_mock.js
//
// 16 §4.8 要求两条死路**看起来能走**：「画明显了迷宫就不成立」。
// 这句话在**俯视 2D** 里有个前提必须先说清——玩家能把整条岔缝一眼看完，
// 那死路再怎么画都是明摆着的。所以这张图比的是**三种做法**：
//
//   A（枯黄分隔带）推荐：岔缝内段压一张「岩檐」（Overlay 遮挡），缝口只看到黑，
//                        走进去岩檐才揭开 —— 玩家得走到底才知道白走一趟。
//   B（朱红分隔带）反例：不遮挡。同样的迷宫、同样的瓦片，尽头一眼看穿 ——
//                        瓦片一点没画错，要求照样不成立。
//   C（石灰分隔带）对照：塌陷山洞。证明两个岩洞不撞脸（砌块岩壁 vs 笔直裂缝）。
//
// 面板尺寸 = 一屏宽（36 格 ≈ 1152px，就是 1152×648 下的真实视口宽度），
// 不是缩略图：比例本身就是论据。

"use strict";

const fs = require("fs");
const path = require("path");
const { Canvas, encodePNG } = require("./pixel");
const { TILE, buildAtlas } = require("./tiles");
const props = require("./props");

// 图集坐标（与 map_kit.gd 的 T_* 一一对应）
const FLOOR = [1, 9];
const WALL = [4, 8];
const GAP = [4, 9];
const SKY_SLIT = [6, 6];
const RUBBLE = [6, 8];
const EAVE = [6, 9];

const COLS = 36;
const ROWS = 14;

/**
 * 一条主缝 + 两条岔缝，三条「岔缝口」用的是**同一张瓦片**：
 * 左边那条是死路（拐一次弯、尽头有箱），右边那条能绕回去。
 * 差别只在「内段有没有被岩檐盖住」。
 */
function layout() {
  const rows = [
    "####################################",
    "#####...############################",
    "#####...############################",
    "#####...############################",
    "#####...############################",
    "#####...############################",
    "#####...############################",
    // 岔缝口**不是特殊瓦片**，就是岩壁带上的一个三格开口——和 mapgen 的
    // `carve_corridor` 铺出来的东西一致（3 格宽的走廊 + 自动砌墙留下的缺口）。
    "#####...############################",
    "#..................................#",
    "#...s.................s............#",
    "#..................................#",
    "#..................................#",
    "#######g############################",
    "####################################",
  ].map((s) => s.split(""));
  // 岔缝内段（岩檐要盖住的那一段）与箱子的位置：箱子压在岩檐底下，
  // 只从檐边漏一点光出来——这就是「看起来里面有东西」的诱饵。
  const branch = [];
  for (let y = 1; y <= 7; y++) for (let x = 5; x <= 7; x++) branch.push([x, y]);
  return { rows, branch, chestAt: [6, 2] };
}

function renderMap(theme, spec, withEave, chestGlow) {
  const atlas = buildAtlas(theme);
  const { rows, branch, chestAt } = spec;
  const out = new Canvas(COLS * TILE, ROWS * TILE);
  const put = (x, y, coord) => {
    for (let j = 0; j < TILE; j++) {
      for (let i = 0; i < TILE; i++) {
        const p = atlas.get(coord[0] * TILE + i, coord[1] * TILE + j);
        if (p[3] === 0) continue;
        if (p[3] === 255) out.set(x * TILE + i, y * TILE + j, p);
        else out.blend(x * TILE + i, y * TILE + j, p, p[3] / 255);
      }
    }
  };
  for (let y = 0; y < ROWS; y++) {
    for (let x = 0; x < COLS; x++) {
      const ch = rows[y][x];
      if (ch === ".") put(x, y, FLOOR);
      else if (ch === "g") put(x, y, GAP);
      else if (ch === "r") put(x, y, RUBBLE);
      else if (ch === "s") put(x, y, SKY_SLIT);
      else put(x, y, WALL);
    }
  }
  // 岩檐盖住岔缝的**内段**（外圈留一格看得见缝口），玩家在缝口读不出里面有多深
  if (withEave) {
    for (const [x, y] of branch) if (y <= 4) put(x, y, EAVE);
  }
  if (chestAt) {
    const chest = props.buildAll()["chest_silver.png"];
    if (chest) out.blit(chest, chestAt[0] * TILE, chestAt[1] * TILE);
    if (chestGlow) {
      for (let j = -TILE; j < TILE * 2; j++) {
        for (let i = -TILE; i < TILE * 2; i++) {
          const d = Math.hypot(i, j);
          if (d > TILE) continue;
          out.blend(
            chestAt[0] * TILE + 16 + i, chestAt[1] * TILE + 16 + j,
            [232, 163, 61], (1 - d / TILE) * chestGlow,
          );
        }
      }
    }
  }
  return out;
}

/** 三张图纵向拼一页，用分隔带的颜色区分三种做法。 */
function stack(panels) {
  const band = 8;
  const w = Math.max(...panels.map((p) => p.canvas.w));
  const h = panels.reduce((sum, p) => sum + p.canvas.h + band, band);
  const out = new Canvas(w, h);
  out.fill([26, 30, 32, 255]);
  let y = band;
  for (const panel of panels) {
    out.rect(0, y - band, w, band, panel.band);
    out.blit(panel.canvas, 0, y);
    y += panel.canvas.h + band;
  }
  return out;
}

const spec = layout();
const panels = [
  // A：推荐——岩檐盖住岔缝内段，缝口只有黑，走进去才揭开
  { canvas: renderMap("shixi", spec, true, 0.9), band: [200, 162, 74, 255] },
  // B：反例——不遮挡。同样的瓦片、同样的迷宫，尽头一眼看穿
  { canvas: renderMap("shixi", spec, false, 0.35), band: [168, 58, 46, 255] },
  // C：对照组——塌陷山洞：砌块岩壁、几乎无光，和上面两张不像同一个地方
  { canvas: renderMap("cave", spec, false, 0), band: [110, 106, 99, 255] },
];

const sheet = stack(panels);
const outPath = path.resolve(__dirname, "..", "..", "docs/dev/images/石隙死路画法_示意.png");
fs.writeFileSync(outPath, encodePNG(sheet.scaled(2)));
console.log(`写了 ${outPath}（${sheet.w * 2}x${sheet.h * 2}）`);
