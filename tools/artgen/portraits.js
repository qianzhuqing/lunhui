// 立绘占位（设计 0.19.1／0.19.2）：**人形剪影**，一人一张，同名替换。
//
// 两条规格来自 15 §4.6 与 19 §三：
//   · **原生 80×120，显示时 4 倍放大到 320×480**——按原生 320×480 画，颗粒会比地图细得多，
//     和 32px 瓦片并排会割裂；
//   · 初期**不能用纯空白**（空白框玩家会以为是 bug），统一用人形剪影占位。
//
// 文件名按**角色 id**（`character_base.char_id`）：角色面板按 char_id 取，
// 创建界面按出身卡的 `origin_def.char_id` 取——一张图两处用。
"use strict";

const { Canvas, hex, shade } = require("./pixel");

const W = 80;
const H = 120;
const INK = "#1E2224";
const PAPER = "#E8E2D4";

// 五个出身各给一点「一眼看得出是谁」的剪影特征（武器／身形／发式）
const CAST = [
  { id: "scholar_fallen", body: "#5A6472", accent: "#8FA3BF", weapon: "sword" },
  { id: "ch_gang", body: "#6B5A46", accent: "#A8875C", weapon: "blade" },
  { id: "ch_du", body: "#5C6B52", accent: "#9BB07A", weapon: "none" },
  { id: "ch_ci", body: "#4E5666", accent: "#7E8CA6", weapon: "sword" },
  { id: "ch_qi", body: "#6A6070", accent: "#A79BB8", weapon: "none" },
];

/** 一张立绘剪影：脚下淡影 ＋ 头肩躯干 ＋ 该角色的武器剪影。 */
function portrait(spec) {
  const c = new Canvas(W, H);
  const ink = hex(INK);
  const body = hex(spec.body);
  const accent = hex(spec.accent);

  // 脚下的淡影：让人物「站」在地上，而不是飘着
  for (let y = 108; y < 116; y++) {
    const half = 26 - Math.abs(y - 111);
    for (let x = 40 - half; x <= 40 + half; x++) c.blend(x, y, ink, 0.18);
  }
  // 躯干（梯形）：肩宽、下摆略收
  for (let y = 46; y < 112; y++) {
    const t = (y - 46) / 66;
    const half = Math.round(24 - 6 * t);
    c.rect(40 - half, y, half * 2, 1, body);
    c.rect(40 - half, y, 2, 1, ink);            // 左侧轮廓
    c.rect(40 + half - 2, y, 2, 1, ink);        // 右侧轮廓
  }
  // 腰带（accent）：把剪影切成两段，像个人形而不是一根柱子
  c.rect(18, 84, 44, 4, accent);
  c.rect(18, 88, 44, 1, ink);
  // 头 ＋ 发
  c.ellipse(40, 30, 12, 14, body);
  c.ellipse(40, 30, 12, 14, ink);
  c.ellipse(40, 29, 11, 13, body);
  c.rect(28, 16, 24, 8, ink);                   // 发顶
  c.rect(28, 16, 24, 3, shade(spec.body, -0.3) ? hex(shade(spec.body, -0.3)) : body);
  // 脖领
  c.rect(36, 42, 8, 6, hex(PAPER));
  c.rect(36, 42, 8, 1, ink);

  // 武器剪影：剑直、刀略弯（都不画细节——占位只求「一眼看得出是谁」）
  if (spec.weapon === "sword") {
    c.rect(63, 22, 3, 74, ink);
    c.rect(60, 92, 9, 3, accent);
  } else if (spec.weapon === "blade") {
    for (let i = 0; i < 66; i++) {
      const x = 63 + Math.round(Math.sin(i / 66 * Math.PI) * 3);
      c.rect(x, 24 + i, 3, 1, ink);
    }
    c.rect(60, 88, 9, 3, accent);
  } else {
    // 空手：垂下的一只手（拳／内功路线）
    c.rect(56, 70, 6, 18, body);
    c.rect(56, 70, 6, 1, ink);
  }
  // 外描边（15 §4.4 浅底连带，2026-10-04）：立绘原来是"没有描边"的，
  // 换成宣纸浅底后剪影的亮边会化掉，所以整张剪影补一圈 pixel.EDGE。
  // 脚底那道淡影是半透明的（α 0.18 < outline 的 128 门槛），不会被圈进来——
  // 圈的是"人"，不是"人和影子"。
  c.edge();
  return c;
}

function buildAll() {
  const out = {};
  for (const spec of CAST) out[`portrait_${spec.id}.png`] = portrait(spec);
  return out;
}

module.exports = { buildAll, W, H };
