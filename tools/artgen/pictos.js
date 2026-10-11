// 面板图标的**共用笔法**：七维 `icons/attr/`、派生 `icons/stat/`、空槽 `icons/slot/`
// 三族共用（32×32、透明底、墨色记号 ＋ 一圈 `pixel.EDGE`）。
//
// 为什么合在一起：这三族**摆在同一张角色面板上、尺寸一样、底色一样**，记号必须出自同一套笔法——
// 各写各的就会出现"七维那批粗、派生那批细"，并排一眼看穿（15 §4.3「同一类别内构图取齐」）。
//
// 与 `badge.js` 那套**不是一回事**：异常／增益是战斗界面那条 chip，需要**圆底**先在深色面板上抢眼；
// 这三族贴在浅色面板的表格行里，**只留一个记号**，靠 `edge()` 那一圈墨线在宣纸上立住
// （15 §4.4 浅底连带①＋交接单 A2「三族自带那圈描边」）。

"use strict";

const { Canvas, hex } = require("./pixel");

const S = 32;
const INK = "#322E26";     // 与 `pixel.EDGE` 同色：这三族的记号就是正文墨色
const FAINT = "#A89A7E";   // 面板描边那一档的赭石——只有空槽那种"这里没东西"才用它
const HOLE = [0, 0, 0, 0];

/** 掏空一块（透明）。细节一律"掏"出来，见 `inkIcon` 的注释。 */
function punch(c, x, y, w = 1, h = 1) {
  for (let j = 0; j < h; j++) {
    for (let i = 0; i < w; i++) c.set(x + i, y + j, HOLE);
  }
}

/**
 * 面板图标的统一造法：**先画实心记号 → 描边 → 再掏细节**。
 * 顺序不能反——`edge()` 会给每块不透明像素的邻居刷一圈墨，2px 的细节被刷一圈就糊死了
 * （`badge.js` 那句「记号画在勾边之后」是同一个坑）。
 * 细节一律**掏空**、不另上一色：三族贴在宣纸面板上，掏空＝露出纸色，天然就对；
 * 另上一色反而要跟着面板色一起维护。
 */
function inkIcon(base, detail) {
  const c = new Canvas(S, S);
  base(c, hex(INK));
  c.edge();
  if (detail) detail(c, hex(FAINT));
  return c;
}

/**
 * 盾：上直下收尖，防御／抗性／格挡那一族（9 张）都从这一笔起手。
 * 认它们靠**盾里掏出来的记号**（环／裂／横杠／下箭头／属性记号），不靠换盾形。
 */
function shield(c, col, cx = 15.5, top = 6, w = 17, h = 20) {
  const left = Math.round(cx - w / 2);
  const straight = Math.round(h * 0.45);
  c.rect(left, top, w, straight, col);
  const taper = h - straight;
  for (let k = 1; k <= taper; k++) {
    const t = k / (taper + 1);
    const ww = Math.max(2, Math.round(w * (1 - t * t)));
    c.hLine(Math.round(cx - ww / 2), top + straight + k - 1, ww, col);
  }
}

/** 箭：一根杆 ＋ 一个三角头。`dir` 取 "up"／"down"／"right"。 */
function arrow(c, col, x, y, len, dir = "up", shaft = 2, head = 4) {
  if (dir === "up" || dir === "down") {
    const s = dir === "up" ? -1 : 1;
    c.rect(x, y, shaft, len, col);
    for (let k = 0; k <= head; k++) {
      const w = Math.max(1, (head - k) * 2 - 1);
      c.hLine(Math.round(x + shaft / 2 - w / 2), y + s * (len + k), w, col);
    }
  } else {
    c.rect(x, y, len, shaft, col);
    for (let k = 0; k <= head; k++) {
      const h = Math.max(1, (head - k) * 2 - 1);
      c.vLine(x + len + k, Math.round(y + shaft / 2 - h / 2), h, col);
    }
  }
}

/** 粗圆环（气环／准星那种）：按极角撒点，厚度 `t`。 */
function ring(c, col, cx, cy, r, t = 2) {
  for (let a = 0; a < 360; a += 2) {
    const rad = (a * Math.PI) / 180;
    for (let k = 0; k < t; k++) {
      c.set(cx + Math.cos(rad) * (r + k), cy + Math.sin(rad) * (r + k), col);
    }
  }
}

/** 断环：环上留一个豁口（"内力被截断"那一族的记号，与异常的内伤同一套语义）。 */
function brokenRing(c, col, cx, cy, r, gapFrom, gapTo, t = 2) {
  for (let a = 0; a < 360; a += 2) {
    if (a > gapFrom && a < gapTo) continue;
    const rad = (a * Math.PI) / 180;
    for (let k = 0; k < t; k++) {
      c.set(cx + Math.cos(rad) * (r + k), cy + Math.sin(rad) * (r + k), col);
    }
  }
}

/** 一滴：圆肚 ＋ 上尖（毒／血那一族都用它，靠盾上／盾外来分）。 */
function drop(c, col, cx, cy, r) {
  c.disk(cx, cy, r, col);
  for (let k = 0; k < r * 2; k++) {
    const w = Math.max(1, Math.round(k * 0.8));
    c.hLine(Math.round(cx - w / 2), Math.round(cy - r - k * 0.9), w, col);
  }
}

/** 火：外焰下宽上尖 ＋ 两侧舔边（与异常那张灼伤同一个语汇，这里更小）。 */
function flame(c, col, cx, baseY, h) {
  for (let k = 0; k < h; k++) {
    const w = Math.max(1, Math.round((1 - k / h) * 8));
    c.hLine(Math.round(cx - w / 2), baseY - k, w, col);
  }
  c.set(Math.round(cx - 5), baseY - Math.round(h * 0.45), col);
  c.set(Math.round(cx + 5), baseY - Math.round(h * 0.6), col);
}

/** 星芒：四角爆（暴击那一族的记号）。 */
function spark(c, col, cx, cy, r) {
  for (let k = -r; k <= r; k++) {
    const w = Math.max(1, Math.round((1 - Math.abs(k) / r) * 2.2));
    c.hLine(Math.round(cx - w / 2), cy + k, w, col);
    c.vLine(cx + k, Math.round(cy - w / 2), w, col);
  }
  c.disk(cx, cy, 1.6, col);
}

module.exports = { S, INK, FAINT, HOLE, punch, inkIcon, shield, arrow, ring, brokenRing, drop, flame, spark };
