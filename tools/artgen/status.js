// 异常状态图标（32×32）：`icons/status/<status_id>.png`。
//
// 路径按 Q80 定稿：**异常走 `assets/icons/status/`，增益走 `assets/icons/buff/`，
// 两边都按表里 `icon` 列的值交图**（`status_effect.icon` 现在是 `status_poison` 这种）。
//
// 这四张出现在**战斗界面的「增益减益」chip 列表**里（`battle_screen._make_effect_chip()`），
// chip 是 `Button`，图标按**原生尺寸**摆在 12px 的文字旁边。所以这一类跟别的类反着画：
//
//   · 武学／物品那些图标是「**压暗圆底 ＋ 亮记号**」（列表里排一行，底要沉下去、不抢字）；
//   · 异常与增益是「**亮色圆底 ＋ 深色记号**」——HUD 那条 chip 本来就小、又要一眼认出，
//     亮底在深色面板上先抓住眼睛，再靠记号分辨是哪一个。
//
// 颜色照 15 §二：**绿＝毒、朱红只给血**（流血），灼伤用火把橙那一族（不是朱红），
// 内伤给**暗紫**——它不是掉血是"内力被截断"，用红会和流血撞、用蓝会和增益撞。

"use strict";

const { hex } = require("./pixel");
const { badge } = require("./badge");   // 骨架与增益那一族共用（为什么单开一份见 badge.js）

/** 中毒：三粒**各不相连**的泡往上飘（连在一起就成一团死绿，第一版就是这样）。 */
function statusPoison() {
  return badge("#4A7A46", "#8ABE72", (c, d) => {
    // 三粒泡**沿一条斜线往上**排（别摆成"两眼一嘴"——第一版一左一右加中间一颗，
    // 16 px 下就是个鬼脸）。左上再补一颗小的，表示在冒。
    c.disk(11, 21, 3.5, d);
    c.disk(16, 15.5, 3, d);
    c.disk(20.5, 10, 2.5, d);
    c.set(15, 8, d);
    c.set(12, 11, d);
  });
}

/** 灼伤：一簇火——**外焰下宽上尖 ＋ 两侧各一簇舔边 ＋ 亮内焰**（只画一个三角不像火）。 */
function statusBurn() {
  return badge("#A8432A", "#E8823A", (c, d, light) => {
    for (let k = 0; k < 14; k++) {                  // 外焰
      const w = Math.max(1, Math.round((1 - k / 14) * 6.5));
      c.hLine(15 - w, 24 - k, w * 2 + 1, d);
    }
    for (let k = 0; k < 5; k++) c.set(15 + (k % 2), 12 - k, [0, 0, 0, 0]);  // 顶上开个豁口 → 两片火舌
    c.vLine(9, 15, 5, d);                           // 左侧舔边
    c.set(8, 18, d);
    c.set(9, 19, d);
    c.vLine(21, 12, 6, d);                          // 右侧舔边（高一点）
    c.set(22, 16, d);
    c.set(21, 18, d);
    for (let k = 0; k < 8; k++) {                   // 内焰：亮
      const w = Math.max(1, Math.round((1 - k / 8) * 3.5));
      c.hLine(15 - w, 22 - k, w * 2 + 1, hex(light));
    }
    c.set(15, 18, hex("#F0C060"));
  });
}

/** 流血：一滴血 ＋ 下面一小滩（15 §二允许朱红给血）。 */
function statusBleed() {
  return badge("#8A2A22", "#C84A38", (c, d, light) => {
    c.disk(15.5, 18, 5, d);                         // 血滴的圆肚
    for (let k = 0; k < 10; k++) {                  // 上头的尖：1 px 起，慢慢张开
      const w = Math.round(k * 0.5);
      c.hLine(15 - w, 7 + k, w * 2 + 1, d);
    }
    c.disk(13, 15, 2, hex(light));                  // 一点高光：不然是个黑灯泡
    c.hLine(10, 25, 6, d);                          // 溅出去的一小截
    c.hLine(21, 26, 4, d);
  });
}

/** 内伤：一圈**断开的**气环 ＋ 一道裂纹——"内力被截断了"，不是掉血。 */
function statusInternal() {
  return badge("#5A4A6E", "#8A76A8", (c, d) => {
    for (let a = 0; a < 360; a += 3) {
      if (a > 40 && a < 100) continue;              // 断口：环在这儿被截断
      const rad = (a * Math.PI) / 180;
      for (const r of [9, 8, 7]) {
        c.set(15.5 + Math.cos(rad) * r, 15.5 + Math.sin(rad) * r, d);
      }
    }
    // 那道裂纹：从环的断口一路斜切进去
    for (const [x, y] of [[21, 21], [19, 19], [20, 17], [18, 15], [19, 13], [17, 11], [18, 9]]) {
      c.set(x, y, d);
      c.set(x + 1, y, d);
    }
  });
}

/** 相对 `assets/` 的路径 → 画布。**文件名 = `status_effect.icon` 的值，一位不能差。** */
function buildAll() {
  return {
    "icons/status/status_poison.png": statusPoison(),
    "icons/status/status_burn.png": statusBurn(),
    "icons/status/status_bleed.png": statusBleed(),
    "icons/status/status_internal.png": statusInternal(),
  };
}

module.exports = { buildAll, statusPoison, statusBurn, statusBleed, statusInternal };
