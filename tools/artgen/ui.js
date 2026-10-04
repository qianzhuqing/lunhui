// UI 贴图：面板底板（九宫格）与状态条的「临界表现」。
//
// 分工（设计 0.31.x 定）：**色值表／按钮四态／两档字号／三条状态条的配色都归程序**
// （一个 `Theme` 资源就能落），美术这半只出**九宫格底板**与**需要画出来的那一处**。
//
// 九宫格规格（15 §4.4，一字不改）：切图 **32×32**、四角 **8×8 不拉伸**、
// 四边 **16×8／8×16 可拉伸**、中心 **16×16 可平铺**。
// 装饰取向：**素边 ＋ 一条细石纹**（A10 拍板，2026-10-04）——理由写在 `地图搭建说明.md`：
// 石纹是**横向连续**的，最窄的浮层（线索本约 392px）也成立；四角竹节是四个点，窄面板上两头重。

"use strict";

const { Canvas, hex, mix, shade, noise } = require("./pixel");

const S = 32;
const CORNER = 8;

/**
 * 面板底板：一张 32×32，八个浮层共用。
 *
 * 三层结构，从外到内：**石灰描边**（1px，15 §4.4 的 #6E6A63）→ **一条细石纹**
 * （1px 亮 ＋ 1px 暗，成一道浅槽）→ **面板底**（#1E2224，α 0.95）。
 * 四角把描边加厚一格：素边不加花，全靠「角比边重一档」立住，
 * 这样拉伸到任意长宽，角都不会跟着变形。
 */
function panelPlate() {
  const c = new Canvas(S, S);
  // 底：比地图的墨黑更沉（15 §4.4：面板浮在地图上不能跟场景抢视线），半透明
  c.rect(0, 0, S, S, [30, 34, 36, 242]);
  // 极淡的颗粒：大面积纯色在像素风里会显得"没画完"，但对比度要压到几乎看不见。
  // **中心那 16×16 按 16 取模生成**——它是「可平铺」区，颗粒不取模的话，
  // 平铺到第二块时接缝处会突然多一颗/少一颗，长面板上就是一条可见的竖纹。
  for (let y = 8; y < 24; y++) {
    for (let x = 8; x < 24; x++) {
      const gx = x - 8;
      const gy = y - 8;
      if (noise(gx, gy, 11) < 0.06) c.set(x, y, hex("#242A2E"));
      if (noise(gx, gy, 29) < 0.03) c.set(x, y, hex("#3A4247"));
    }
  }
  // 四条边带与四角各撒一点（它们不参与平铺，取模与否都一样）
  c.speckle(0, 0, S, 8, hex("#242A2E"), 0.06, 11);
  c.speckle(0, 24, S, 8, hex("#242A2E"), 0.06, 11);
  c.speckle(0, 8, 8, 16, hex("#242A2E"), 0.06, 11);
  c.speckle(24, 8, 8, 16, hex("#242A2E"), 0.06, 11);
  // 外描边（石灰 1px）
  c.frame(0, 0, S, S, hex("#6E6A63"));
  // 顶端一道极淡的高光：全项目的光源统一自左上，面板也得跟着（15 §一）
  c.hLine(1, 1, S - 2, hex("#3A4247"));
  // 细石纹：内缩 3px 的一道浅槽（亮 1px ＋ 暗 1px）
  const inset = 3;
  c.frame(inset, inset, S - inset * 2, S - inset * 2, hex("#454C51"));
  c.frame(inset + 1, inset + 1, S - (inset + 1) * 2, S - (inset + 1) * 2, hex("#171A1C"));
  // 四角加厚：角上再补一格描边，长宽随便拉伸时角都是"实"的
  for (const [x, y] of [[0, 0], [S - 2, 0], [0, S - 2], [S - 2, S - 2]]) {
    c.rect(x, y, 2, 2, hex("#6E6A63"));
  }
  return c;
}

/**
 * 架势条的「即将破防」临界表现（15 §4.5：这是核心循环的爽点，不能只是一条变短的色块）。
 *
 * 交给程序的是一个**可叠加的横条贴图**（12 高，与 15 §4.4 的「状态条高度 12px」同高）：
 * 程序在架势低于阈值时把它叠在条上，打空那一刻换成 `poise_break.png`。
 * 条本身的颜色仍由程序按色值表刷——这张只管**加什么**。
 */
function poiseCritical() {
  const w = 32;
  const h = 12;
  const c = new Canvas(w, h);
  // 裂纹：自左下往右上走的不规则缝，越靠右越密（血越少裂得越狠）
  for (let x = 0; x < w; x++) {
    const t = x / (w - 1);
    if (noise(x, 7, 5) > 0.42 + t * 0.25) {
      const y = 6 + Math.round(Math.sin(x * 0.9) * 2);
      c.set(x, y, hex(mix("#5A2A24", "#8A3A2E", t)));
      c.set(x, y + 1, hex("#2A1410"));
    }
  }
  // 顶端一线枯黄（与架势条的 #C8A24A 同色系，但更亮一档）
  for (let x = 0; x < w; x++) {
    if (noise(x, 3, 17) > 0.3) c.set(x, 0, hex("#E8C46A"));
  }
  return c;
}

/** 破防那一刻的爆发：整条压亮 + 四道向外炸的短光（只在打空的那一帧叠）。 */
function poiseBreak() {
  const w = 32;
  const h = 12;
  const c = new Canvas(w, h);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) c.set(x, y, hex(mix("#D8A24A", "#F0E0A0", noise(x, y, 3))));
  }
  for (let i = 0; i < 4; i++) {
    const x = 4 + i * 8;
    for (let k = 0; k < 5; k++) {
      c.set(x + k, 1 + k, hex("#FFF6D8"));
      c.set(x - k + 4, h - 2 - k, hex("#FFF6D8"));
    }
  }
  return c;
}

/** 相对 `assets/` 的路径 → 画布。 */
function buildAll() {
  return {
    // 15 §六：界面资源放 `ui/<panel>/`；八个浮层共用一张，所以是 `common`
    "ui/common/panel_bg.png": panelPlate(),
    "ui/battle/poise_critical.png": poiseCritical(),
    "ui/battle/poise_break.png": poiseBreak(),
  };
}

/** 把 32×32 的九宫格按任意尺寸铺开（给预览用，也顺便验「边能不能拉」。） */
function nineSlice(src, w, h) {
  const out = new Canvas(w, h);
  const px = (sx, sy) => src.get(((sx % S) + S) % S, ((sy % S) + S) % S);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      let sx;
      let sy;
      if (x < CORNER) sx = x;
      else if (x >= w - CORNER) sx = S - (w - x);
      else sx = CORNER + ((x - CORNER) % (S - CORNER * 2));
      if (y < CORNER) sy = y;
      else if (y >= h - CORNER) sy = S - (h - y);
      else sy = CORNER + ((y - CORNER) % (S - CORNER * 2));
      out.set(x, y, px(sx, sy));
    }
  }
  return out;
}

module.exports = { buildAll, panelPlate, poiseCritical, poiseBreak, nineSlice, S, CORNER };
