// 大地图地标图标 + 明雷表现贴图（32×32，透明底）。
//
// 口径：
//  - 07 §8.1 给了 7 个 id，代码按 `map_region.icon` 找 `assets/sprites/icons/<id>.png`，
//    未解锁时找 `<id>_dim.png`；`icon_highlight.png` 叠在离玩家最近的地标上。
//  - 16 §3.5 定了各自的剪影：城镇＝屋舍剪影（最大最亮）、副本＝寨门／塔楼、
//    驿站＝旗杆／马桩；兴趣点不给问号，靠地形异常本身说话，所以只做「看得出是什么」的剪影。
//  - 15 §二：朱红只给血／灯笼／旗帜／敌意，所以旗与灯笼是唯一用朱红的地方。

"use strict";

const { Canvas, hex, mix, shade, noise, encodePNG } = require("./pixel");

const S = 32;
const INK = "#1E2224";
const PAPER = "#D8D2C4";
const LAMP = "#E8A33D";
const RED = "#A83A2E";
const JADE = "#3E6E8A";
const GOLD = "#C8A24A";

/** 底线：先铺一层压暗的圆底，剪影在花地图上才跳得出来（不铺底会跟地形糊在一起）。 */
function badge(c, { tone = INK, alpha = 0.62, r = 14 } = {}) {
  const cx = (c.w - 1) / 2;
  const cy = (c.h - 1) / 2;
  for (let y = 0; y < c.h; y++) {
    for (let x = 0; x < c.w; x++) {
      const d = Math.hypot(x - cx, y - cy);
      if (d <= r + 1) {
        const a = d <= r - 1 ? alpha : alpha * (1 - (d - (r - 1)) / 2);
        c.blend(x, y, hex(tone), a);
      }
    }
  }
}

/** 图标统一的外框：墨黑描边 + 内侧一道极细的亮边（低饱和底子上靠它立住）。 */
function rim(c, color) {
  c.outline(hex(INK));
  const src = c.clone();
  for (let y = 0; y < c.h; y++) {
    for (let x = 0; x < c.w; x++) {
      const p = src.get(x, y);
      if (p[3] < 200) continue;
      const outside = [
        src.get(x + 2, y), src.get(x - 2, y), src.get(x, y + 2), src.get(x, y - 2),
      ].some((q) => q[3] < 100);
      if (!outside) continue;
      if (src.get(x + 1, y)[3] < 100 || src.get(x, y + 1)[3] < 100) {
        c.set(x, y, hex(mix(color, INK, 0.35)));
      }
    }
  }
}

// ---------------------------------------------------------------- 逐个地标

/**
 * 清风驿：城镇地标。**48×48**（16 §3.5：城镇是最大最显眼的一类，
 * 地形占 3×3 瓦片、图标 48×48；副本与驿站才 32×32）。
 *
 * 构图上有两条自己的规矩：
 *  ① 剪影是「一片屋舍」不是「一栋房子」——主屋居中、两座偏屋分开，
 *     这样共用的 `icon_highlight`（32×32 的金环，代码写死一份）正好套在主屋上。
 *  ② 内容整体上移、最下面 8px 留空：图标的世界坐标是 marker + (0,-26)，
 *     而地标名字的 Label 从 (0,-10) 开始画——不留空的话字会压在屋子上。
 */
function iconTown() {
  const size = 48;
  const c = new Canvas(size, size);
  badge(c, { tone: "#2A2620", alpha: 0.74, r: 21 });
  const roof = (cx, y, half, h, base, dark, light) => {
    for (let i = 0; i < h; i++) {
      const w = half + Math.round((i / h) * half * 0.35);
      const row = y + i;
      c.rect(cx - w, row, w * 2 + 1, 1, hex(i < 2 ? light : base));
      if (i % 3 === 2) c.hLine(cx - w, row, w * 2 + 1, hex(dark));
      for (let x = cx - w; x <= cx + w; x += 5) c.set(x, row, hex(dark));
      c.set(cx - w, row, hex(light));
      c.set(cx + w, row, hex(dark));
    }
    c.hLine(cx - half, y + h, half * 2 + 1, hex(shade(dark, -0.25)));
  };
  // 屋舍：主屋 + 两座偏屋。墙用宣纸白（清风驿是全图唯一的暖色块）
  const house = (walkX, walkY, walkW, roofY, roofHalf, roofH, roofBase) => {
    c.rect(walkX, walkY, walkW, 12, hex(PAPER));
    c.hLine(walkX, walkY, walkW, hex(shade(PAPER, 0.22)));
    c.hLine(walkX, walkY + 11, walkW, hex("#8A8578"));
    roof(walkX + Math.floor(walkW / 2), roofY, roofHalf, roofH, roofBase, "#3F4B4F", "#78888D");
  };
  house(4, 22, 10, 17, 7, 5, "#5B6B70"); // 左偏屋
  house(34, 22, 10, 17, 7, 5, "#5B6B70"); // 右偏屋
  house(16, 20, 16, 11, 11, 8, "#5B6B70"); // 主屋
  // 主屋的窗与门（窗里点灯，那是「安全区」的全部信号）
  for (const x of [19, 27]) {
    c.rect(x, 23, 6, 6, hex("#4A3A2C"));
    c.rect(x + 1, 24, 4, 4, hex(LAMP));
    c.set(x + 2, 25, hex(shade(LAMP, 0.35)));
  }
  c.rect(22, 24, 5, 8, hex("#4A3A2C"));
  c.rect(23, 25, 3, 7, hex(RED));
  c.set(25, 28, hex("#E8D8B0"));
  // 院墙 + 门洞（城镇的轮廓靠一圈院墙兜住，不是只贴一栋房子）
  c.rect(6, 33, 36, 4, hex("#B0AA9C"));
  c.hLine(6, 33, 36, hex(PAPER));
  c.hLine(6, 36, 36, hex("#948E80"));
  c.rect(21, 32, 7, 6, hex("#4A3A2C"));
  c.rect(22, 32, 5, 5, hex("#3A2E22"));
  // 两盏灯笼：朱红在 15 §二里只许给血／灯笼／旗帜／敌意
  for (const x of [15, 32]) {
    c.rect(x, 30, 1, 4, hex("#4A3A2C"));
    c.rect(x - 1, 34, 3, 4, hex(RED));
    c.set(x, 35, hex(LAMP));
  }
  // 地面：夯土 + 一块石板空地，让「镇子」落在地上而不是浮着
  c.ellipse(24, 39, 20, 5, hex("#8A7349"));
  c.ellipse(24, 38, 18, 4, hex("#A89060"));
  c.ellipse(24, 38, 7, 2, hex("#7A7570"));
  rim(c, PAPER);
  return c;
}

/** 驿站：旗杆 + 马桩。旗用朱红，玩家在地图上找的就是这面旗。 */
function iconPost() {
  const c = new Canvas(S, S);
  badge(c, { tone: "#2A2620", alpha: 0.62 });
  // 旗杆
  c.rect(12, 4, 2, 24, hex("#5E4A38"));
  c.rect(12, 4, 1, 24, hex("#8A6A46"));
  c.rect(6, 7, 8, 9, hex(RED));
  c.rect(6, 7, 8, 1, hex(shade(RED, 0.25)));
  c.rect(10, 7, 4, 9, hex(shade(RED, -0.2)));
  c.rect(6, 15, 8, 1, hex(shade(RED, -0.3)));
  // 马桩（两根 + 一根横木）
  for (const x of [21, 26]) c.rect(x, 14, 2, 13, hex("#6B5236"));
  c.rect(20, 14, 9, 2, hex("#8A6A46"));
  c.rect(20, 14, 9, 1, hex("#A08053"));
  // 拴马绳
  c.rect(22, 17, 6, 1, hex("#A08053"));
  rim(c, GOLD);
  return c;
}

/** 落雁坡：开阔的枯黄草坡 + 青松，明雷区（不做危险标记，靠地形说）。 */
function iconWild() {
  const c = new Canvas(S, S);
  badge(c, { tone: "#2A2E22", alpha: 0.6 });
  c.ellipse(16, 22, 13, 7, hex("#8A7A45"));
  c.ellipse(16, 21, 12, 6, hex(GOLD));
  c.ellipse(15, 20, 10, 4, hex(shade(GOLD, 0.22)));
  for (let i = 0; i < 5; i++) {
    const x = 5 + i * 5;
    c.vLine(x, 18 + (i % 2) * 3, 4, hex("#8A7A45"));
  }
  // 青松：三段三角
  c.rect(16, 16, 2, 6, hex("#4A3A2C"));
  for (let i = 0; i < 3; i++) {
    const y = 8 + i * 4;
    const w = 5 + i * 2;
    for (let k = 0; k < 4; k++) {
      const ww = Math.round(w * (1 - k / 4)) + 1;
      c.hLine(17 - ww, y + k, ww * 2 + 1, hex("#3E5A46"));
    }
    c.hLine(17 - w, y, w, hex("#4E6E56"));
  }
  rim(c, GOLD);
  return c;
}

/** 黑风寨：寨门 + 望楼 + 黑旗。要有威慑感，所以压暗、加尖顶。 */
function iconDungeon() {
  const c = new Canvas(S, S);
  badge(c, { tone: "#1A1E20", alpha: 0.72 });
  // 寨门：两墩 + 横木
  for (const x of [5, 19]) {
    c.rect(x, 12, 8, 16, hex("#4A3A2C"));
    c.rect(x, 12, 8, 1, hex("#6E6A63"));
    for (let y = 13; y < 28; y += 4) c.hLine(x, y, 8, hex("#2E241A"));
  }
  c.rect(13, 13, 6, 3, hex("#4A3A2C"));
  c.rect(13, 13, 6, 1, hex("#6E6A63"));
  // 门洞
  c.rect(11, 18, 10, 10, hex("#14181A"));
  c.ellipse(16, 18, 5, 4, hex("#14181A"));
  // 望楼尖顶
  c.rect(13, 6, 6, 8, hex("#4A3A2C"));
  for (let i = 0; i < 5; i++) c.hLine(13 - i, 6 - i + 4, 6 + i * 2, hex("#354246"));
  // 黑旗（压暗的朱红边，唯一的强调色）
  c.rect(25, 4, 1, 12, hex("#4A3A2C"));
  c.rect(26, 5, 4, 6, hex("#2E2A28"));
  c.rect(26, 10, 4, 1, hex(RED));
  rim(c, "#6E6A63");
  return c;
}

/** 塌陷山洞：青灰岩壁里的洞口。 */
function iconCave() {
  const c = new Canvas(S, S);
  badge(c, { tone: "#1E2224", alpha: 0.68 });
  c.ellipse(16, 19, 13, 10, hex("#4E5A5E"));
  c.ellipse(16, 17, 12, 8, hex("#63706F"));
  c.ellipse(16, 23, 9, 8, hex("#14181A"));
  c.ellipse(15, 20, 7, 6, hex("#1E2224"));
  // 上缘的碎石与一线天
  for (const [x, y] of [[7, 12], [24, 11], [16, 9], [11, 10]]) {
    c.rect(x, y, 3, 2, hex("#63706F"));
  }
  c.hLine(12, 6, 2, hex("#8A94A0"));
  c.hLine(18, 4, 2, hex("#8A94A0"));
  rim(c, "#63706F");
  return c;
}

/** 荒村：焦黑断墙 + 枯树。要一眼看出「这里出过事」。 */
function iconRuin() {
  const c = new Canvas(S, S);
  badge(c, { tone: "#242220", alpha: 0.66 });
  // 断墙：左高右低、中间一个豁口，顶上参差（「这里出过事」全靠这个剪影）
  const wall = (x, y, w, h) => {
    c.rect(x, y, w, h, hex("#3A3430"));
    c.hLine(x, y, w, hex("#6E6A63"));
    c.hLine(x, y + 1, w, hex("#4A4238"));
    c.hLine(x, y + h - 1, w, hex("#242220"));
    for (let i = x + 1; i < x + w - 1; i += 4) c.vLine(i, y + 2, h - 3, hex("#2E2A28"));
  };
  wall(2, 16, 10, 13);
  wall(17, 21, 11, 8);
  c.rect(12, 14, 3, 3, hex("#3A3430"));
  c.rect(21, 17, 4, 3, hex("#3A3430"));
  // 焦黑的窗洞
  c.rect(5, 20, 4, 5, hex("#12100F"));
  c.rect(19, 24, 4, 4, hex("#12100F"));
  // 枯树：主干 + 三根支杈，剪影要一眼是树
  c.rect(26, 5, 2, 17, hex("#463E32"));
  c.hLine(21, 8, 5, hex("#463E32"));
  c.hLine(28, 9, 3, hex("#463E32"));
  c.hLine(22, 12, 4, hex("#463E32"));
  c.set(20, 9, hex("#463E32"));
  c.set(31, 10, hex("#463E32"));
  rim(c, "#8A7A55");
  return c;
}

/** 废弃渡口：灰蓝水汽 + 朽木断桩 + 半沉船。看得出还能走，但本章去不了。 */
function iconFerry() {
  const c = new Canvas(S, S);
  badge(c, { tone: "#1E2A30", alpha: 0.7 });
  // 水面：灰蓝 + 几道波纹
  c.ellipse(16, 22, 14, 8, hex("#3E5058"));
  c.ellipse(16, 21, 13, 7, hex("#5A6E7A"));
  c.hLine(5, 19, 6, hex("#7A8A96"));
  c.hLine(20, 18, 7, hex("#7A8A96"));
  c.hLine(8, 25, 8, hex("#7A8A96"));
  // 半沉的破船：船体 + 断桅
  c.rect(8, 21, 15, 5, hex("#463E32"));
  c.rect(8, 21, 15, 1, hex("#6B5A46"));
  c.rect(10, 20, 11, 1, hex("#3A3430"));
  c.rect(14, 12, 2, 9, hex("#4A3A2C"));
  c.hLine(11, 15, 4, hex("#4A3A2C"));
  // 断桩：三根粗细不等，前面两根要读得出「桩」
  for (const [x, h] of [[24, 9], [28, 5]]) {
    c.rect(x, 12 + (9 - h) / 1, 3, h + 9, hex("#3A3430"));
    c.rect(x, 12 + (9 - h) / 1, 1, h + 9, hex("#6B5A46"));
  }
  c.rect(5, 16, 3, 12, hex("#3A3430"));
  c.rect(5, 16, 1, 12, hex("#6B5A46"));
  rim(c, "#7A8A96");
  return c;
}

/**
 * 石隙迷窟：劈开的窄岩缝 + 一线天光（16 §4.8）。
 *
 * 与 `icon_cave`（塌陷山洞）**必须一眼分得开**：山洞是「岩壁上塌出来的一个圆洞口」，
 * 石隙是「一道从上贯到下的直缝，缝顶漏下一线光」——形态与光都相反。
 */
function iconShixi() {
  const c = new Canvas(S, S);
  badge(c, { tone: "#1E242A", alpha: 0.7 });
  // 两侧笔直的岩壁（不是圆的洞口）
  c.rect(3, 4, 9, 26, hex("#4A5258"));
  c.rect(20, 4, 9, 26, hex("#4A5258"));
  c.rect(3, 4, 9, 1, hex("#5E686E"));
  c.rect(20, 4, 9, 1, hex("#5E686E"));
  c.rect(11, 4, 1, 26, hex("#343C42"));
  c.rect(12, 4, 7, 26, hex("#242A30"));
  // 直缝的岩口：两侧各留一道亮边，缝越往深处越黑
  for (let y = 4; y < 30; y++) {
    const t = (y - 4) / 26;
    c.set(12, y, hex(mix("#5E686E", "#242A30", t)));
    c.set(19, y, hex(mix("#4A5258", "#1E242A", t)));
  }
  // 一线天光：从缝顶漏下来的一道冷光
  c.rect(14, 1, 4, 12, hex("#8EA8CC"));
  c.rect(15, 0, 2, 14, hex("#C6D8EE"));
  c.rect(15, 12, 2, 6, hex("#8EA8CC"));
  // 缝底：遗篇石台的幽蓝（全区唯一的暖…不，唯一的冷亮色）
  c.rect(13, 25, 6, 4, hex("#3E5A7A"));
  c.rect(14, 25, 4, 2, hex("#5E7EA8"));
  rim(c, "#5E686E");
  return c;
}

// ---------------------------------------------------------------- 状态叠加

/** 当前所在地高亮：叠在图标上的一圈枯黄光环。 */
function iconHighlight() {
  const c = new Canvas(S, S);
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      const d = Math.hypot(x - 15.5, y - 15.5);
      if (d < 11 || d > 15.5) continue;
      const edge = Math.min(Math.abs(d - 13.2) / 2.3, 1);
      c.blend(x, y, hex(GOLD), 0.85 * (1 - edge * edge));
    }
  }
  return c;
}

/** 精英明雷：程序化光晕的贴图版（07 §11 第 7 条点名要的那张）。 */
function markerEliteRed() {
  const c = new Canvas(S, S);
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      const d = Math.hypot(x - 15.5, y - 15.5);
      if (d > 15) continue;
      const a = Math.pow(1 - d / 15, 2.1);
      c.blend(x, y, hex(d > 10 ? RED : LAMP), a * 0.85);
    }
  }
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      const d = Math.hypot(x - 15.5, y - 15.5);
      if (Math.abs(d - 12.5) < 1.2) c.blend(x, y, hex(RED), 0.75);
    }
  }
  return c;
}

/** 一次性产出全部图标：id → 画布。 */
function buildAll() {
  const base = {
    icon_town: iconTown(),
    icon_post: iconPost(),
    icon_wild: iconWild(),
    icon_dungeon: iconDungeon(),
    icon_cave: iconCave(),
    icon_ruin: iconRuin(),
    icon_ferry: iconFerry(),
    // 第 8 个地标：15 §4.3 的「大地图标记 8」；表侧 `map_region.n_shixi.icon`
    // 现在填的是 `icon_ruin`（借了荒村的图），要换成本图才用得上。
    icon_shixi: iconShixi(),
  };
  const out = {};
  for (const [id, canvas] of Object.entries(base)) {
    out[`${id}.png`] = canvas;
    // 未解锁／不可用：去饱和 + 压到七成不透明（07 §8.1「驿站未解锁的灰色态」）
    out[`${id}_dim.png`] = canvas.muted(0.82, 0.7);
  }
  out["icon_highlight.png"] = iconHighlight();
  out["marker_elite_red.png"] = markerEliteRed();
  return out;
}

module.exports = { buildAll, encodePNG, S };
