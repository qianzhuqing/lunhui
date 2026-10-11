// 七维图标（32×32）：`icons/attr/<attr_id>.png`，7 张。
//
// 设计口径（交接单_美术 §九 四 ＋ `15 §六` 命名表）：按 `attribute_def` 的行 id 交图、
// **不进 `icon` 列**（与武学同一套：文件名就是行 id），摆**角色面板属性页中栏**那七行。
// 七行里 str／con／agi／int／luk 是**可加点**、wu／gen 是**资质不可加点**——这一层由界面说
// （14 §四），图标不承担（A15 那条「图标不承担星级」的同一个道理），所以不要把两张画得"灰一点"。
//
// 记号一律是**一个实心墨块 ＋ 掏空的细节**（`pictos.js::inkIcon`），靠形状分辨、不靠颜色：
// 这七行里五行是单字（力／体／敏／智／运）、两行是资质（悟性／根骨），旁边都有文字，
// 图标只负责"扫一眼知道是哪一行"。

"use strict";

const { inkIcon, punch } = require("./pictos");

/** 力：拳（外功伤害与破架势）。指缝掏空三道，不然整块墨看不出是拳头。 */
function str() {
  return inkIcon((c, col) => {
    c.disk(9.5, 16, 4, col);
    c.disk(21.5, 16, 4, col);
    c.rect(9, 11, 14, 10, col);
    for (const x of [11, 14.5, 18, 21]) c.disk(x, 11, 2.6, col);   // 四个指节
    c.rect(12, 19, 8, 6, col);                                     // 腕
  }, (c) => {
    for (const x of [13, 16, 19]) punch(c, x, 10, 1, 9);
    punch(c, 10, 18, 2, 4);
  });
}

/** 体：躯干（气血、外功防御、内伤抗性）——颈 ＋ 肩 ＋ 收腰，胸口掏一道窄缝、腋下掏两个缺口。
 *  不掏这几笔的话，肩和躯干连成一整个圆团，"躯干"和"根骨那根竹竿"就分不出来了。 */
function con() {
  return inkIcon((c, col) => {
    c.rect(13, 5, 6, 5, col);
    c.rect(8, 10, 15, 4, col);
    for (let y = 14; y <= 25; y++) {
      const half = Math.max(5, Math.round(7.5 - (y - 14) * 0.28));
      c.hLine(Math.round(15.5 - half), y, half * 2, col);
    }
  }, (c) => {
    punch(c, 14, 11, 3, 5);
    punch(c, 9, 14, 2, 2);
    punch(c, 20, 14, 2, 2);
    punch(c, 11, 22, 9, 1);
  });
}

/** 敏：靴（身法、命中与闪避）——靴筒 ＋ 靴头，鞋底那道掏空。 */
function agi() {
  return inkIcon((c, col) => {
    c.rect(10, 6, 8, 14, col);
    c.rect(10, 18, 13, 5, col);
    c.disk(22.5, 20.5, 3, col);
    c.disk(12, 21, 3.5, col);
  }, (c) => {
    punch(c, 10, 22, 15, 1);
    punch(c, 13, 8, 1, 8);
  });
}

/** 智：毛笔（内功伤害、内力与异常强度）。 */
function int() {
  return inkIcon((c, col) => {
    for (let k = 0; k < 12; k++) c.rect(21 - k, 4 + k, 3, 2, col);   // 笔杆
    c.rect(9, 16, 5, 5, col);                                        // 笔斗
    for (let k = 0; k < 6; k++) {
      c.hLine(9 - Math.round(k * 0.7), 21 + k, Math.max(1, 5 - k), col);  // 笔锋
    }
  }, (c) => {
    for (let k = 0; k < 11; k++) punch(c, 22 - k, 5 + k, 1, 1);
  });
}

/** 运：骰（暴击、异常触发率与掉落机缘）——三点那面。 */
function luk() {
  return inkIcon((c, col) => {
    c.rect(9, 7, 14, 18, col);
    c.rect(7, 9, 18, 14, col);
  }, (c) => {
    punch(c, 11, 11, 2, 2);
    punch(c, 15, 15, 2, 2);
    punch(c, 19, 19, 2, 2);
  });
}

/** 悟性：灯（招式槽上限与熟练度成长）——"悟"用一盏灯说，比画个"悟"字像素画能认。 */
function wu() {
  return inkIcon((c, col) => {
    c.rect(12, 4, 8, 2, col);
    c.rect(10, 6, 12, 14, col);
    c.rect(12, 20, 8, 2, col);
    c.rect(15, 22, 2, 5, col);
  }, (c) => {
    for (const x of [13, 15, 17, 19]) punch(c, x, 8, 1, 10);
  });
}

/** 根骨：竹（内功槽上限与内力上限）——竿上两处节掏空，一提"根骨"就往这儿想。 */
function gen() {
  return inkIcon((c, col) => {
    c.rect(12, 3, 5, 26, col);
    c.ellipse(22, 8, 5, 2.5, col);
    c.ellipse(23, 14, 4, 2, col);
  }, (c) => {
    punch(c, 11, 10, 7, 1);
    punch(c, 11, 19, 7, 1);
  });
}

/**
 * 相对 `assets/` 的路径 → 画布。**文件名 = `attribute_def.attr_id`，一位不能差**
 * （这张表没有 `icon` 列，界面直接按行 id 找图；写错＝那行"静静地没有图标"）。
 */
function buildAll() {
  return {
    "icons/attr/str.png": str(),
    "icons/attr/con.png": con(),
    "icons/attr/agi.png": agi(),
    "icons/attr/int.png": int(),
    "icons/attr/luk.png": luk(),
    "icons/attr/wu.png": wu(),
    "icons/attr/gen.png": gen(),
  };
}

module.exports = { buildAll };
