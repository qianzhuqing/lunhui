// 物品图标（32×32）。
//
// 路径照 15 §六：`icons/item/<item_id>.png`，**文件名 = `item_base.item_id`**。
// 注意这条目录现在与大地图图标（`assets/sprites/icons/`）并存：
// 后者是 07 §8.1 定的、代码已经写死 `ICON_DIR` 的那一套；两者别混。
//
// 接线情况：`item_base.icon` 现在**有界面读了**——角色页／行囊页走 `src/ui/icon_paths.gd` 的
// `IconPaths.item()`（有图才摆、没图不留空位，见框架说明决策 309／312）。
// 装备图标（`equips.js`）与这一批**同框**，所以两边的画法口径要一起看：都是透明底 ＋ 墨线勾边。

"use strict";

const { Canvas, hex, mix, shade, noise } = require("./pixel");

const S = 32;
const INK = "#1E2224";

/**
 * 藏宝图：一卷旧图纸 + 朱红系带，露出一角画着山形与朱印。
 * 它是「偷来的唯一入口」，所以要比普通纸卷更像「有人藏过的东西」。
 */
function itemTreasureMap() {
  const c = new Canvas(S, S);
  // 卷身（左低右高的斜卷）
  for (let i = 0; i < 20; i++) {
    const x = 5 + i;
    const y = 20 - Math.round(i * 0.45);
    c.rect(x, y, 1, 12, hex(i < 12 ? "#C8B48A" : "#B0A078"));
  }
  c.rect(4, 20, 2, 12, hex("#8A7A55"));
  c.rect(24, 10, 2, 12, hex("#8A7A55"));
  // 纸面（摊开的一角）
  c.rect(6, 12, 20, 10, hex("#D8D2C4"));
  c.rect(6, 12, 20, 1, hex("#B0AA9C"));
  c.rect(6, 21, 20, 1, hex("#B0AA9C"));
  // 图上画的：两笔山形 + 一条河
  for (let i = 0; i < 5; i++) {
    c.set(9 + i, 19 - i, hex("#4A5258"));
    c.set(13 - i, 19 - i, hex("#4A5258"));
    c.set(17 + i, 18 - i, hex("#4A5258"));
    c.set(21 - i, 18 - i, hex("#4A5258"));
  }
  c.hLine(8, 19, 16, hex("#5A6E7A"));
  // 朱印：藏宝图这类东西的标记
  c.rect(21, 14, 4, 4, hex("#A83A2E"));
  c.set(22, 15, hex("#D8D2C4"));
  c.set(23, 16, hex("#D8D2C4"));
  // 系带
  c.rect(4, 23, 22, 2, hex("#8A3A2E"));
  c.rect(12, 20, 2, 6, hex("#A83A2E"));
  c.outline(hex(INK));
  return c;
}

/**
 * 遗篇残卷：石隙迷窟终点那部 ★5 内功的秘籍。
 * 与普通书卷的区别用**幽蓝**（16 §4.8 的石台光）＋撕口表达——它是前朝遗物。
 */
function itemScrollYipian() {
  const c = new Canvas(S, S);
  // 竹简／卷轴主体：右上角是撕口
  c.rect(6, 6, 20, 20, hex("#B0A078"));
  c.rect(7, 7, 18, 18, hex("#C8B48A"));
  // 竹简的竖条
  for (let x = 9; x < 25; x += 4) c.vLine(x, 7, 18, hex("#A89070"));
  // 撕口：右下角缺一块（残卷）
  for (let y = 17; y < 26; y++) {
    const cut = 26 - (y - 17) * 1.4;
    for (let x = Math.round(cut); x < 26; x++) c.set(x, y, [0, 0, 0, 0]);
  }
  for (let i = 0; i < 6; i++) c.set(24 - i, 16 + Math.round(i * 0.5), hex("#8A7A55"));
  // 前朝的字：三行朱笔（不是墨黑——它被朱批过）
  for (const y of [10, 13, 16]) {
    for (let x = 9; x < 20; x += 3) c.hLine(x, y, 2, hex("#8A3A2E"));
  }
  // 幽蓝的光：与石隙石台同一色，一眼知道它出自哪儿
  for (const [x, y] of [[8, 8], [24, 8], [8, 24]]) {
    c.set(x, y, hex("#3E5A7A"));
    c.set(x + 1, y, hex("#5E7EA8"));
  }
  c.outline(hex(INK));
  return c;
}

/**
 * 账册：第一章暗线的核心道具（十年前白鹭渡沉银案的真账）。
 *
 * 设计要求是**「旧、普通、不像宝贝」**——玩家第一眼看不出它重要。
 * 所以刻意不画成书册的端庄样：封皮磨白、边角起毛、夹着半张纸条，
 * 连朱印都不给它（宝贝才盖印，这只是一本被人翻烂的账）。
 */
function itemBdLedger() {
  const c = new Canvas(S, S);
  // 封皮：压暗的旧蓝布，四角磨白
  c.rect(4, 6, 24, 21, hex("#3E4650"));
  c.rect(5, 7, 22, 19, hex("#4A545E"));
  c.hLine(5, 7, 22, hex("#5A646E"));
  c.hLine(5, 25, 22, hex("#2E363E"));
  // 装订线：线装书的针脚，缝得歪
  for (let y = 9; y < 25; y += 4) {
    c.hLine(4, y, 4, hex("#8A7A55"));
    c.set(3, y + 1, hex("#6E6248"));
  }
  // 磨白的角落（旧而不破得夸张）
  for (const [x, y] of [[5, 8], [25, 8], [5, 24], [25, 24]]) {
    c.rect(x - 1, y - 1, 3, 2, hex("#B0AA9C"));
  }
  // 起毛的边角：右下缺一小口
  for (let i = 0; i < 4; i++) c.set(26 - i, 26, [0, 0, 0, 0]);
  c.set(26, 25, [0, 0, 0, 0]);
  // 夹着的半张纸条（露在外面一截，暗示有人查过）
  c.rect(18, 3, 9, 5, hex("#D8D2C4"));
  c.hLine(18, 3, 9, hex("#B0AA9C"));
  c.hLine(19, 5, 6, hex("#8A8578"));
  c.hLine(19, 7, 4, hex("#8A8578"));
  // 封皮上的题签（褪色的字，不是朱印）
  c.rect(9, 12, 12, 7, hex("#C8C0AE"));
  c.hLine(10, 14, 10, hex("#8A8578"));
  c.hLine(10, 16, 7, hex("#8A8578"));
  c.outline(hex(INK));
  return c;
}

/** 相对 `assets/` 的路径 → 画布。 */
function buildAll() {
  return {
    "icons/item/item_treasure_map.png": itemTreasureMap(),
    "icons/item/item_scroll_yipian.png": itemScrollYipian(),
    "icons/item/item_bd_ledger.png": itemBdLedger(),
  };
}

module.exports = { buildAll, itemTreasureMap, itemScrollYipian, itemBdLedger };
