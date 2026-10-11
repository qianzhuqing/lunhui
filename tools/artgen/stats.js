// 派生数值图标（32×32）：`icons/stat/<stat_id>.png`，29 张。
//
// 设计口径（交接单_美术 §九 四／§十；`15 §4.3`）：按 `stat_def` 的**行 id** 交图、**不进 `icon` 列**，
// 摆在**角色面板的属性页**（战斗属性按 `stat_def` 逐行摆 ＝ 29 行，别再按分组表少摆几张——
// 那正是旧界面漏掉毒抗／火抗／流血抗的原因，见 `待策划确认.md` §界面口径）。出图即显示，不出也不报错。
//
// 29 张要在一张表里互相认得出，所以**按语法分组**、不靠颜色：
//
//   · **盾族 10 张**（防御／抗性／格挡）：同一个盾形，靠**盾里掏出来的记号**分辨——
//     素盾＝外功防御、环＝内功防御、裂＝破架势、横杠＝格挡、下箭头＝格挡减伤、内圈＝减伤、
//     断环＝内伤抗、毒泡＝毒抗、火＝火抗、血滴＝流血抗。**四张抗性的记号直接借用异常那族**
//     （中毒是三粒泡、灼伤是火、流血是滴、内伤是断环），玩家在战斗里认过一遍就能对上。
//   · **资源族**：心＝气血、气团＝内力，加一道上箭头＝"每回合回复"。
//   · **率族**：准星＝命中、斜影＝闪避、星芒＝暴击、双星芒＝暴击伤害、箭穿盾＝穿透。
//   · **槽／成长族**：四格＝招式槽、叠圆＝内功容量、卷轴＋上箭头＝熟练度成长、双斜杠＋加号＝段数。
//
// 记号一律是**实心墨块 ＋ 掏空的细节**（`pictos.js::inkIcon`）：贴宣纸面板，掏空＝露纸色，
// 不用另配一套浅色，也就不会跟着面板色一起漂。

"use strict";

const { HOLE, punch, inkIcon, shield, arrow, ring, brokenRing, drop, flame, spark } = require("./pictos");

/** 盾里掏一道**下箭头**（格挡减伤那种"把伤害压下去"的意思）。 */
function punchDownArrow(c, cx, top, len) {
  punch(c, Math.round(cx) - 1, top, 2, len);
  for (let k = 0; k < 4; k++) {
    const w = 8 - k * 2;
    punch(c, Math.round(cx - w / 2), top + len + k, w, 1);
  }
}

/** 盾里掏一道**折线裂**（破架势）。 */
function punchCrack(c) {
  const pts = [[15, 8], [13, 11], [16, 14], [12, 17], [15, 20], [13, 23]];
  for (const [x, y] of pts) punch(c, x, y, 2, 2);
}

/** 盾里掏**三粒泡**（毒抗——与异常那张中毒同一个记号）。 */
function punchBubbles(c) {
  c.disk(12.5, 12.5, 2.2, HOLE);
  c.disk(16.5, 15.5, 2.6, HOLE);
  c.disk(18, 20, 1.8, HOLE);
}

// ---------------------------------------------------------------- 攻击

/** 外功攻击：拳（与七维那张"力"同源）＋ 两道冲击短笔——同源是故意的：力就驱动外功。 */
function atkPhys() {
  return inkIcon((c, col) => {
    c.disk(19.5, 16, 4, col);
    c.rect(14, 11, 12, 10, col);
    for (const x of [15, 18, 21] ) c.disk(x, 11, 2.6, col);
    c.rect(17, 19, 7, 6, col);
    c.rect(5, 13, 4, 2, col);          // 冲击短笔（与拳留 4px：挨近了描边会连成一块）
    c.rect(4, 19, 5, 2, col);
  }, (c) => {
    for (const x of [17, 20]) punch(c, x, 10, 1, 8);
    punch(c, 15, 18, 2, 4);
  });
}

/** 内功攻击：掌印（五指分开的手印）——外功用拳、内功用掌，一只手说清两条路。 */
function atkQi() {
  return inkIcon((c, col) => {
    c.disk(15.5, 19, 6, col);                       // 掌
    for (const x of [8, 11.5, 15, 18.5, 22]) c.rect(x, 5, 2.4, 11, col);
  }, (c) => {
    for (const x of [10, 13.5, 17, 20.5]) punch(c, x, 8, 1, 9);   // 指缝
    punch(c, 12, 18, 7, 1);
  });
}

// ---------------------------------------------------------------- 防御／抗性（盾族）

/** 外功防御：素盾。 */
function defPhys() {
  return inkIcon((c, col) => shield(c, col));
}

/** 内功防御：盾里一道气环。 */
function defQi() {
  return inkIcon((c, col) => shield(c, col), (c) => ring(c, HOLE, 15.5, 15, 5.5, 2));
}

/** 破架势：盾上一道裂。 */
function poiseBreak() {
  return inkIcon((c, col) => shield(c, col), (c) => punchCrack(c));
}

/** 格挡率：盾里一道横杠（"架住"那一下）。 */
function blockRate() {
  return inkIcon((c, col) => shield(c, col), (c) => punch(c, 11, 14, 9, 2));
}

/** 格挡减伤率：盾里的下箭头。 */
function blockReduction() {
  return inkIcon((c, col) => shield(c, col), (c) => punchDownArrow(c, 15.5, 11, 5));
}

/** 减伤率：**重盾**（盾里再套一圈内圈）——终局乘区那张，画得比格挡更"厚"。 */
function dmgReduction() {
  return inkIcon((c, col) => shield(c, col), (c) => ring(c, HOLE, 15.5, 15, 6.5, 2));
}

/** 内伤抗性：盾里的断环（与异常"内伤"同一个记号）。 */
function resInternal() {
  return inkIcon((c, col) => shield(c, col), (c) => brokenRing(c, HOLE, 15.5, 14.5, 5.5, 40, 110, 2));
}

/** 毒抗：盾里三粒泡（与异常"中毒"同一个记号）。 */
function resPoison() {
  return inkIcon((c, col) => shield(c, col), (c) => punchBubbles(c));
}

/** 火抗：盾里一簇火（与异常"灼伤"同一个记号）。 */
function resBurn() {
  return inkIcon((c, col) => shield(c, col), (c) => flame(c, HOLE, 15.5, 22, 12));
}

/** 流血抗：盾里一滴血（与异常"流血"同一个记号）。 */
function resBleed() {
  return inkIcon((c, col) => shield(c, col), (c) => {
    drop(c, HOLE, 15.5, 15, 3.6);
    punch(c, 13, 21, 3, 1);
    punch(c, 17, 21, 3, 1);
  });
}

// ---------------------------------------------------------------- 资源

/** 气血上限：心（实心，不加箭头）。 */
function hpMax() {
  return inkIcon((c, col) => {
    c.disk(11.5, 12.5, 5, col);
    c.disk(20.5, 12.5, 5, col);
    for (let k = 0; k < 13; k++) {
      const w = Math.max(1, Math.round(19 * (1 - k / 13)));
      c.hLine(Math.round(16 - w / 2), 14 + k, w, col);
    }
  });
}

/** 内力上限：气团（实心圆 ＋ 掏一道旋）。 */
function qiMax() {
  return inkIcon((c, col) => c.disk(15.5, 15.5, 9.5, col), (c) => {
    for (let a = -70; a < 210; a += 3) {
      const rad = (a * Math.PI) / 180;
      for (let k = 0; k < 2; k++) {
        c.set(15.5 + Math.cos(rad) * (6 + k), 15.5 + Math.sin(rad) * (6 + k), HOLE);
      }
    }
    c.disk(15.5, 15.5, 2.2, HOLE);
  });
}

/** 气血回复：小心 ＋ 上箭头。 */
function hpRegen() {
  return inkIcon((c, col) => {
    c.disk(10.5, 17.5, 4, col);
    c.disk(17.5, 17.5, 4, col);
    for (let k = 0; k < 10; k++) {
      const w = Math.max(1, Math.round(15 * (1 - k / 10)));
      c.hLine(Math.round(14 - w / 2), 19 + k, w, col);
    }
    arrow(c, col, 24, 12, 9, "up", 2, 4);
  });
}

/** 内力回复：小气团 ＋ 上箭头。 */
function qiRegen() {
  return inkIcon((c, col) => {
    c.disk(11, 18, 7, col);
    arrow(c, col, 22, 13, 9, "up", 2, 4);
  }, (c) => {
    for (let a = -70; a < 210; a += 4) {
      const rad = (a * Math.PI) / 180;
      c.set(11 + Math.cos(rad) * 4, 18 + Math.sin(rad) * 4, HOLE);
    }
  });
}

// ---------------------------------------------------------------- 身法与率

/** 身法：三道速度线 ＋ 一个右箭头（比"画个靴子"更贴"出手顺序"这件事）。 */
function speed() {
  return inkIcon((c, col) => {
    c.rect(6, 11, 12, 3, col);
    c.rect(6, 16, 15, 3, col);
    c.rect(6, 21, 10, 3, col);
    arrow(c, col, 18, 16, 6, "right", 3, 4);
  });
}

/** 穿透率：**一支箭把盾射穿**——直接在盾上掏出一个箭形，比并排画两件东西清楚。 */
function penRate() {
  return inkIcon((c, col) => shield(c, col), (c) => {
    punch(c, 8, 14, 10, 2);            // 杆
    for (let k = 0; k < 4; k++) punch(c, 18 + k, 15 - k, 1, 2 * (k + 1) - 1);   // 箭头（朝右）
  });
}

/** 命中率：准星（环 ＋ 四刻度 ＋ 掏空的中心）。 */
function hitRate() {
  return inkIcon((c, col) => {
    ring(c, col, 15.5, 15.5, 7, 2.5);
    c.rect(14.5, 3, 2, 4, col);
    c.rect(14.5, 25, 2, 4, col);
    c.rect(3, 14.5, 4, 2, col);
    c.rect(25, 14.5, 4, 2, col);
  });
}

/** 闪避率：斜影（一个人侧身让开——本体 ＋ 两道残影）。 */
function dodgeRate() {
  return inkIcon((c, col) => {
    for (let k = 0; k < 14; k++) {     // 本体：一条斜着的楔
      c.rect(13 + k, 8 + k, 5 - Math.round(k / 4), 2, col);
    }
    c.rect(4, 10, 6, 2, col);          // 残影（与本体留 3px）
    c.rect(4, 16, 6, 2, col);
  });
}

/** 暴击率：星芒。 */
function critRate() {
  return inkIcon((c, col) => spark(c, col, 15.5, 15.5, 11));
}

/** 暴击伤害：大小两枚星芒（"这一下更重"）。 */
function critDmg() {
  return inkIcon((c, col) => {
    spark(c, col, 12, 19, 8);
    spark(c, col, 22, 10, 5);
  });
}

/** 异常强度：毒滴 ＋ 上箭头（"每层更疼"）。 */
function debuffPower() {
  return inkIcon((c, col) => {
    drop(c, col, 11, 17, 4.5);
    arrow(c, col, 22, 22, 10, "up", 2, 4);
  });
}

/** 异常触发率：三粒泡 ＋ 一道环（泡＝什么异常，环＝"上不上得去"）。 */
function debuffChance() {
  return inkIcon((c, col) => {
    ring(c, col, 15.5, 15.5, 8.5, 2.5);
  }, (c) => punchBubbles(c));
}

// ---------------------------------------------------------------- 机缘与槽位

/** 掉落机缘：铜钱（方孔钱——与顶栏那个"铜钱"是同一个东西）。 */
function dropRate() {
  return inkIcon((c, col) => c.disk(15.5, 15.5, 9.5, col), (c) => {
    punch(c, 13, 13, 6, 6);
    ring(c, HOLE, 15.5, 15.5, 12, 2);
  });
}

/** 招式槽上限：四格。 */
function slotActive() {
  return inkIcon((c, col) => {
    for (const [x, y] of [[5, 5], [18, 5], [5, 18], [18, 18]]) c.rect(x, y, 9, 9, col);
  });
}

/** 内功容量：三枚叠圆（内功按星级占 1~3 格，所以是"能叠几个"的形状，不是格子）。 */
function passiveCapacity() {
  return inkIcon((c, col) => {
    ring(c, col, 15.5, 8, 4.5, 2.5);
    ring(c, col, 15.5, 15.5, 4.5, 2.5);
    ring(c, col, 15.5, 23, 4.5, 2.5);
  });
}

/** 熟练度成长：卷轴 ＋ 上箭头。 */
function masteryGain() {
  return inkIcon((c, col) => {
    c.rect(7, 10, 12, 15, col);
    arrow(c, col, 23, 22, 11, "up", 2, 4);
  }, (c) => {
    punch(c, 9, 13, 8, 1);
    punch(c, 9, 17, 8, 1);
    punch(c, 9, 21, 5, 1);
  });
}

/** 招式段数：两道斜杠 ＋ 一个加号（"多打一下"）。 */
function extraHit() {
  return inkIcon((c, col) => {
    for (let k = 0; k < 11; k++) { c.rect(5 + k, 18 - k, 3, 2, col); }
    for (let k = 0; k < 11; k++) { c.rect(11 + k, 18 - k, 3, 2, col); }
    c.rect(24, 6, 2, 10, col);      // 加号
    c.rect(20, 10, 10, 2, col);
  });
}

/**
 * 相对 `assets/` 的路径 → 画布。**文件名 = `stat_def.stat_id`，一位不能差**
 * （这张表没有 `icon` 列；写错＝角色面板那一行静静地没有图标，谁也不会报错）。
 */
function buildAll() {
  return {
    "icons/stat/atk_phys.png": atkPhys(),
    "icons/stat/atk_qi.png": atkQi(),
    "icons/stat/def_phys.png": defPhys(),
    "icons/stat/def_qi.png": defQi(),
    "icons/stat/hp_max.png": hpMax(),
    "icons/stat/qi_max.png": qiMax(),
    "icons/stat/hp_regen.png": hpRegen(),
    "icons/stat/qi_regen.png": qiRegen(),
    "icons/stat/speed.png": speed(),
    "icons/stat/poise_break.png": poiseBreak(),
    "icons/stat/pen_rate.png": penRate(),
    "icons/stat/hit_rate.png": hitRate(),
    "icons/stat/dodge_rate.png": dodgeRate(),
    "icons/stat/block_rate.png": blockRate(),
    "icons/stat/block_reduction.png": blockReduction(),
    "icons/stat/crit_rate.png": critRate(),
    "icons/stat/crit_dmg.png": critDmg(),
    "icons/stat/debuff_power.png": debuffPower(),
    "icons/stat/debuff_chance.png": debuffChance(),
    "icons/stat/res_internal.png": resInternal(),
    "icons/stat/dmg_reduction.png": dmgReduction(),
    "icons/stat/drop_rate.png": dropRate(),
    "icons/stat/res_poison.png": resPoison(),
    "icons/stat/res_burn.png": resBurn(),
    "icons/stat/res_bleed.png": resBleed(),
    "icons/stat/slot_active.png": slotActive(),
    "icons/stat/passive_capacity.png": passiveCapacity(),
    "icons/stat/mastery_gain.png": masteryGain(),
    "icons/stat/extra_hit.png": extraHit(),
  };
}

module.exports = { buildAll };
