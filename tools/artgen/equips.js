// 装备图标（32×32）：`icons/equip/<equip_id>.png`，**文件名 = `equip_base.equip_id`**（15 §六）。
//
// 这一批和 `items.js` 的物品图标**同框**（角色页的装备栏与行囊页），所以沿用物品那套语言：
// **透明底 ＋ 一件带墨线勾边的"东西"**——不套武学图标那块压暗圆底。圆底是"招式／心法"
// 那一类的签名（配合气环、45° 轴读成一套），装备套上反而会和武学行打架。
//
// 15 §4.3 的「同一类别内构图取齐」在这批里落成两条：
//
// | 类别 | 张数 | 构图 |
// |---|---|---|
// | **武器** | 9 | **斜 45°**（与招式图标同一根轴：左下 → 右上） |
// | **其余**（衣／甲／裤／护肩／腰带／戒指／佩饰） | 18 | **正面居中** |
//
// 颜色一律**按材料走**（铁／青锋／锈／乌木／白蜡杆／皮／布／铜／玉／金）——装备不像武学那样
// 按学派染色：行囊里"这把是铁的那个是玉的"本身就是信息。
// **稀有度不画进图标**（同 15 §4.3「星阶不画进图标」那条）：层级由列表 UI 的文字与颜色承担，
// 画进图标就会和别的图标打架。所以 `锈月`／`遗物`／`淬毒指环` 这些的区别来自**造型与材料**
// （锈迹／鎏金／毒光），不是"边框更花"。

"use strict";

const { Canvas, hex } = require("./pixel");

const S = 32;
const INK = "#1E2224";

/** 材料色板：hi＝受光面（刃口／高光），body＝本体，dark＝背光面。 */
const MAT = {
  iron: { hi: "#E0E8F0", body: "#8A94A0", dark: "#5E6A70" },
  steel: { hi: "#E8F4FC", body: "#7A98B0", dark: "#456479" },
  rust: { hi: "#D8B896", body: "#8A6A52", dark: "#553F30" },
  ebony: { hi: "#7A6E62", body: "#453C34", dark: "#2A241F" },
  waxwood: { hi: "#F0E4C4", body: "#C8BC9A", dark: "#94845F" },
  leather: { hi: "#B08A5A", body: "#8A6A46", dark: "#57402C" },
  cloth: { hi: "#E4DED2", body: "#B0AA9C", dark: "#837C6E" },
  copper: { hi: "#E8C878", body: "#B08A4A", dark: "#775524" },
  jade: { hi: "#B0E0C0", body: "#6E9E7E", dark: "#3C6A56" },
  gold: { hi: "#F4DC98", body: "#C8A24A", dark: "#8A6A28" },
};

/** 45° 轴上第 i 个点：i=0 在左下，越往上越靠右上（与招式图标同一根轴）。 */
function axis(x0, y0, i) {
  return [x0 + i, y0 - i];
}

// ---------------------------------------------------------------- 武器 9：斜 45°

/**
 * 一把剑：直刃 ＋ 护手 ＋ 柄 ＋（可选）柄头流苏／刃上的月痕。
 * 五把刀剑共用这一段——**骨架一样、只换材料与附件**，这正是 15 §4.3 要的「同类别取齐」。
 */
function swordIcon(opts) {
  const {
    mat, guard = "copper", grip = "leather",
    x0 = 9, y0 = 23, len = 17, sleeve = 3,
    tassel = false, moon = false, notch = false,
  } = opts;
  const c = new Canvas(S, S);
  const m = MAT[mat];
  const g = MAT[guard];
  const h = MAT[grip];
  for (let i = 0; i < len; i++) {
    const [x, y] = axis(x0, y0, i);
    c.set(x, y, hex(m.hi));
    c.set(x, y + 1, hex(m.body));
    if (sleeve >= 3) c.set(x, y + 2, hex(m.dark));
  }
  const [tx, ty] = axis(x0, y0, len - 1);
  c.set(tx + 1, ty - 1, hex(m.hi));                 // 剑尖
  if (notch) {                                      // 卷刃：锈月／柴刀那类的"用过"
    const [nx, ny] = axis(x0, y0, Math.floor(len * 0.45));
    c.set(nx, ny, hex(m.dark));
  }
  // 护手与柄要**比刃窄**：第一版护手 6×3、柄 3×5，摆上去比 3 px 宽的刃还粗，
  // 整体读成"一把刷子"。收成 5×2 ＋ 3×4 之后剑身才是主角。
  c.rect(x0 - 1, y0 - 2, 5, 2, hex(g.dark));
  c.hLine(x0 - 1, y0 - 2, 5, hex(g.body));
  c.rect(x0 - 2, y0, 3, 4, hex(h.body));            // 柄
  c.set(x0 - 2, y0 + 3, hex(h.dark));
  c.set(x0 - 3, y0 + 4, hex(g.body));               // 柄头
  if (tassel) {                                     // 流苏：本命／精工那把的记号
    c.set(x0 - 3, y0 + 6, hex("#A83A2E"));
    c.set(x0 - 2, y0 + 7, hex("#A83A2E"));
    c.set(x0 - 4, y0 + 7, hex("#8A2A22"));
  }
  if (moon) {                                       // 锈月：刃上一点冷月痕（不画成"logo"）
    c.set(x0 + 11, y0 - 11, hex("#C8D4D8"));
    c.set(x0 + 12, y0 - 12, hex("#E8F0F4"));
    c.set(x0 + 11, y0 - 13, hex("#C8D4D8"));
    c.set(x0 + 10, y0 - 12, hex("#8A98A0"));
  }
  c.outline(hex(INK));
  return c;
}

/** 刀：**背厚刃薄**——背光面多一道，越往尖越厚，与剑的直刃一眼分开（同招式那边的语法）。 */
function saberIcon(opts) {
  const { mat, grip = "leather", x0 = 9, y0 = 23, len = 16, tassel = false } = opts;
  const c = new Canvas(S, S);
  const m = MAT[mat];
  const h = MAT[grip];
  for (let i = 0; i < len; i++) {
    const [x, y] = axis(x0, y0, i);
    c.set(x, y, hex(m.hi));
    c.set(x, y + 1, hex(m.body));
    c.set(x, y + 2, hex(m.dark));
    if (i > 2) c.set(x, y + 3, hex(m.dark));
  }
  const [tx, ty] = axis(x0, y0, len - 1);
  c.set(tx + 1, ty, hex(m.hi));
  c.rect(x0 - 1, y0 - 2, 5, 2, hex(MAT.iron.dark));
  c.rect(x0 - 2, y0, 3, 4, hex(h.body));
  c.set(x0 - 3, y0 + 4, hex(MAT.iron.body));
  if (tassel) {
    c.set(x0 - 3, y0 + 6, hex("#A83A2E"));
    c.set(x0 - 2, y0 + 7, hex("#A83A2E"));
  }
  c.outline(hex(INK));
  return c;
}

/** 枪：杆细一号、比刀剑长，靠**叶尖**认（与招式那边的枪同一支笔）。 */
function spearIcon(opts) {
  const { shaft = "waxwood", tip = "iron", x0 = 8, y0 = 24, len = 17 } = opts;
  const c = new Canvas(S, S);
  const s = MAT[shaft];
  const t = MAT[tip];
  for (let i = 0; i < len - 4; i++) {
    const [x, y] = axis(x0, y0, i);
    c.set(x, y, hex(s.body));
    c.set(x, y + 1, hex(s.dark));
  }
  for (let i = 0; i < 4; i++) {
    const [x, y] = axis(x0, y0, len - 4 + i);
    c.set(x, y, hex(t.hi));
    c.set(x, y + 1, hex(t.body));
    c.set(x, y + 2, hex(t.dark));
  }
  const [ex, ey] = axis(x0, y0, len - 1);
  c.set(ex + 1, ey - 1, hex(t.hi));
  const [bx, by] = axis(x0, y0, 0);
  c.rect(bx, by, 3, 2, hex(s.dark));                // 杆尾
  c.outline(hex(INK));
  return c;
}

/** 拳套：一只手握成拳、沿 45° 撑出去——两根指节的棱是它的辨识点。 */
function fistIcon(opts) {
  const { mat, strap = "leather" } = opts;
  const c = new Canvas(S, S);
  const m = MAT[mat];
  const st = MAT[strap];
  for (let i = 0; i < 8; i++) {                     // 前臂
    const [x, y] = axis(8, 24, i);
    c.set(x, y, hex(m.body));
    c.set(x, y + 1, hex(m.body));
    c.set(x, y + 2, hex(m.dark));
  }
  c.rect(17, 11, 7, 7, hex(m.body));                // 拳
  c.rect(17, 11, 7, 1, hex(m.hi));
  c.rect(17, 17, 7, 1, hex(m.dark));
  c.rect(23, 11, 1, 7, hex(m.dark));
  for (const y of [13, 15]) c.hLine(18, y, 5, hex(m.dark));   // 指节棱
  for (const y of [13, 15]) c.set(21, y, hex(MAT.iron.body));  // 拳面上的铁钉
  c.set(19, 12, hex(MAT.iron.hi));
  c.rect(15, 18, 6, 2, hex(st.body));               // 腕带
  c.set(20, 12, hex(m.hi));
  c.outline(hex(INK));
  return c;
}

// ---------------------------------------------------------------- 防具 12：正面居中

/** 布衣／皮甲／山贼皮铠共用一件"上衣"的骨架，只换材料与面上的东西。 */
function coatIcon(opts) {
  const { mat, trim = "cloth", lamellar = false, stitches = false } = opts;
  const c = new Canvas(S, S);
  const m = MAT[mat];
  const t = MAT[trim];
  // 身：**肩宽腰窄**的梯形。第一版是等宽 12×18 方块加两条一样的袖，
  // 摆上去读成一个"十"字（不像衣服）；收出腰线以后剪影才立得住。
  for (let y = 8; y <= 25; y++) {
    const w = 15 - Math.round((y - 8) * 0.34);
    const x = 16 - Math.floor(w / 2);
    c.hLine(x, y, w, hex(m.body));
    c.set(x, y, hex(m.hi));
    c.set(x + w - 1, y, hex(m.dark));
  }
  for (const side of [-1, 1]) {                     // 两段短袖：从肩往外下斜
    for (let i = 0; i < 5; i++) {
      const x = 16 + side * (8 + Math.floor(i / 2));
      const y = 9 + i;
      c.set(x, y, hex(m.body));
      c.set(x + side, y, hex(m.dark));
    }
  }
  c.rect(12, 5, 8, 3, hex(t.body));                 // 领口
  c.rect(15, 8, 2, 5, hex(m.dark));                 // 前襟
  c.hLine(9, 20, 14, hex(t.dark));                  // 束腰
  c.rect(9, 25, 14, 1, hex(m.dark));                // 下摆
  if (lamellar) {                                   // 皮铠：三排甲片 ＋ 铁钉
    for (let row = 0; row < 3; row++) {
      for (let col = 0; col < 4; col++) {
        const x = 11 + col * 3;
        const y = 9 + row * 3;
        c.rect(x, y, 3, 2, hex(m.dark));
        c.set(x, y, hex(m.hi));
        c.set(x + 1, y + 1, hex(MAT.iron.body));
      }
    }
  } else if (stitches) {                            // 皮甲：缝线
    for (let y = 10; y < 24; y += 3) {
      c.set(11, y, hex(t.hi));
      c.set(21, y, hex(t.hi));
    }
  } else {                                          // 布衣：一道补丁
    c.rect(18, 17, 4, 3, hex(t.dark));
    c.set(19, 18, hex(t.hi));
  }
  c.outline(hex(INK));
  return c;
}

/** 裤／护腿：两条并列。布裤是软的（有褶），皮护腿是硬的（有绑带）。 */
function legsIcon(opts) {
  const { mat, strap = "leather", greaves = false, trim = "cloth" } = opts;
  const c = new Canvas(S, S);
  const m = MAT[mat];
  const st = MAT[strap];
  c.rect(8, 5, 16, 3, hex(MAT[trim].body));         // 腰头
  for (const x of [9, 18]) {
    c.rect(x, 8, 5, 18, hex(m.body));
    c.rect(x, 8, 1, 18, hex(m.hi));
    c.rect(x + 4, 8, 1, 18, hex(m.dark));
    c.rect(x, 25, 5, 1, hex(m.dark));
  }
  c.rect(14, 8, 4, 6, hex(m.dark));                 // 裆
  if (greaves) {                                    // 皮护腿：三道绑带 ＋ 护膝
    for (let i = 0; i < 3; i++) {
      c.hLine(9, 12 + i * 5, 5, hex(st.body));
      c.hLine(18, 12 + i * 5, 5, hex(st.body));
    }
    c.rect(10, 9, 3, 2, hex(MAT.iron.body));
    c.rect(19, 9, 3, 2, hex(MAT.iron.body));
  } else {                                          // 布裤：褶
    for (const y of [12, 17, 22]) {
      c.set(11, y, hex(m.dark));
      c.set(21, y, hex(m.dark));
    }
  }
  c.outline(hex(INK));
  return c;
}

/** 护肩：正面居中一块肩甲。布护肩软（有褶）、皮护肩硬（有钉子）、猎户披肩带毛边。 */
function shoulderIcon(opts) {
  const { mat, trim = "leather", fur = false, gold = false, boss = false } = opts;
  const c = new Canvas(S, S);
  const m = MAT[mat];
  const t = MAT[trim];
  if (fur) {                                        // 猎户披肩：一圈毛
    for (let x = 6; x <= 25; x++) {
      const h = 4 + ((x * 7) % 3);
      c.rect(x, 8 - (h - 4), 1, h + 3, hex(x % 2 ? m.dark : m.body));
    }
    c.rect(6, 10, 20, 1, hex(m.hi));
  }
  c.ellipse(15.5, 14, 8, 6, hex(m.body));           // 肩甲本体
  c.ellipse(14, 12, 5, 3, hex(m.hi));
  c.rect(9, 18, 14, 6, hex(m.body));                // 垂下来的护片
  c.hLine(9, 18, 14, hex(m.hi));
  c.hLine(9, 23, 14, hex(m.dark));
  if (gold) {                                       // 遗物：鎏金边 ＋ 兽面
    c.hLine(9, 19, 14, hex(MAT.gold.body));
    c.hLine(9, 22, 14, hex(MAT.gold.dark));
    c.rect(13, 12, 6, 5, hex(MAT.gold.body));
    c.rect(14, 13, 4, 3, hex(MAT.ebony.body));
    c.set(14, 14, hex(MAT.gold.hi));
    c.set(17, 14, hex(MAT.gold.hi));
  } else if (boss) {
    c.rect(13, 13, 5, 4, hex(MAT.iron.body));
    c.set(14, 14, hex(MAT.iron.hi));
  } else {
    for (const x of [10, 15, 20]) c.set(x, 20, hex(t.hi));   // 铆钉／针脚
  }
  c.outline(hex(INK));
  return c;
}

/** 腰带：一条横带 ＋（布）打结／（皮）铜扣。 */
function beltIcon(opts) {
  const { mat, buckle = "copper", knot = false } = opts;
  const c = new Canvas(S, S);
  const m = MAT[mat];
  const b = MAT[buckle];
  c.rect(4, 13, 24, 6, hex(m.body));
  c.hLine(4, 13, 24, hex(m.hi));
  c.hLine(4, 18, 24, hex(m.dark));
  c.set(4, 13, [0, 0, 0, 0]);
  c.set(27, 18, [0, 0, 0, 0]);
  if (knot) {                                       // 布腰带：打的结 ＋ 两条垂头
    c.rect(13, 11, 6, 3, hex(m.body));
    c.rect(14, 19, 2, 6, hex(m.body));
    c.rect(17, 19, 2, 6, hex(m.dark));
    c.set(14, 11, hex(m.hi));
  } else {                                          // 牛皮腰带：方铜扣 ＋ 扣眼
    c.rect(12, 11, 8, 10, hex(b.body));
    c.rect(13, 12, 6, 8, hex(m.dark));
    c.rect(13, 12, 6, 1, hex(b.hi));
    c.set(15, 15, hex(b.hi));
    for (const x of [22, 25]) c.set(x, 16, hex(m.dark));
  }
  c.outline(hex(INK));
  return c;
}

// ---------------------------------------------------------------- 饰品 7：正面居中

/** 戒指：一枚正面圆环 ＋ 环上的记号（宝石／毒光／锤印）。 */
function ringIcon(opts) {
  const { mat, mark = null, band = 2.5 } = opts;
  const c = new Canvas(S, S);
  const m = MAT[mat];
  for (let a = 0; a < 360; a += 2) {
    const rad = (a * Math.PI) / 180;
    for (let k = 0; k < 3; k++) {
      const r = 8 - k * (band / 2);
      c.set(15.5 + Math.cos(rad) * r, 15.5 + Math.sin(rad) * r,
            hex(k === 0 ? m.hi : (k === 2 ? m.dark : m.body)));
    }
  }
  c.rect(13, 5, 6, 3, hex(m.body));                 // 戒面
  c.hLine(13, 5, 6, hex(m.hi));
  if (mark === "jade") {                            // 玉戒：一枚玉面
    c.rect(14, 6, 4, 2, hex(MAT.jade.body));
    c.set(14, 6, hex(MAT.jade.hi));
  } else if (mark === "poison") {                   // 淬毒指环：一点毒光
    c.rect(14, 6, 4, 2, hex(MAT.iron.dark));
    c.set(15, 6, hex("#8ABE72"));
    c.set(17, 7, hex("#4A7A46"));
  } else if (mark === "hammer") {                   // 铁匠印记：一枚锤印
    c.rect(14, 6, 4, 2, hex(MAT.iron.dark));
    c.set(15, 6, hex("#E4DED2"));
    c.set(16, 7, hex("#E4DED2"));
  } else {                                          // 铜戒：素面
    c.rect(14, 6, 4, 2, hex(m.dark));
  }
  c.outline(hex(INK));
  return c;
}

/** 铜钱串：一串钱（三枚方孔钱 ＋ 绳结）。 */
function coinString() {
  const c = new Canvas(S, S);
  const m = MAT.copper;
  c.vLine(16, 3, 8, hex("#8A8578"));                // 绳
  for (let i = 0; i < 3; i++) {
    const y = 7 + i * 8;
    c.ellipse(15.5, y + 3, 6, 6, hex(m.body));
    c.ellipse(15.5, y + 3, 5, 5, hex(m.hi));
    c.ellipse(15.5, y + 3, 4, 4, hex(m.body));
    c.rect(14, y + 2, 3, 3, hex("#3A3430"));        // 方孔
  }
  c.set(16, 3, hex("#A83A2E"));                     // 绳结
  c.set(17, 4, hex("#A83A2E"));
  c.outline(hex(INK));
  return c;
}

/** 药王玉佩：系绳 ＋ 一枚玉璧（中孔的圆），下面一小截穗。 */
function jadePendant() {
  const c = new Canvas(S, S);
  const j = MAT.jade;
  for (let i = 0; i < 7; i++) {                     // 系绳
    c.set(15 - Math.round(i / 2), 3 + i, hex("#8A8578"));
    c.set(16 + Math.round(i / 2), 3 + i, hex("#8A8578"));
  }
  c.ellipse(15.5, 17, 9, 9, hex(j.body));
  c.ellipse(15.5, 17, 8, 8, hex(j.hi));
  c.ellipse(15.5, 17, 7, 7, hex(j.body));
  c.ellipse(15.5, 16, 5, 4, hex(j.hi));
  c.ellipse(15.5, 17, 2, 2, [0, 0, 0, 0]);          // 中孔
  c.set(12, 13, hex(j.hi));
  c.set(19, 20, hex(j.dark));
  c.rect(14, 26, 2, 4, hex("#A83A2E"));             // 穗
  c.set(17, 27, hex("#8A2A22"));
  c.outline(hex(INK));
  return c;
}

/** 药王符：一张黄纸符（朱砂符头 ＋ 朱印），压着一味草。 */
function medicineTalisman() {
  const c = new Canvas(S, S);
  const paper = "#E0D6A8";
  const paperDark = "#B0A478";
  const red = "#A83A2E";
  c.rect(9, 3, 13, 26, hex(paper));
  c.rect(9, 3, 13, 1, hex("#F0E8C4"));
  c.rect(9, 28, 13, 1, hex(paperDark));
  c.vLine(9, 3, 26, hex(paperDark));
  c.vLine(21, 3, 26, hex(paperDark));
  c.set(9, 3, [0, 0, 0, 0]);                        // 上角剪成符纸的斜口
  c.set(21, 3, [0, 0, 0, 0]);
  c.set(10, 4, [0, 0, 0, 0]);
  c.set(20, 4, [0, 0, 0, 0]);
  c.hLine(11, 6, 9, hex(red));                      // 符头
  c.hLine(12, 8, 7, hex(red));
  c.vLine(15, 9, 6, hex(red));
  c.hLine(12, 11, 7, hex(red));
  c.rect(12, 17, 7, 7, hex(red));                   // 朱印
  c.set(14, 19, hex(paper));
  c.set(17, 19, hex(paper));
  c.set(15, 21, hex(paper));
  for (const [dx, dy] of [[6, 24], [23, 12], [24, 22]]) {   // 一味草：绿
    c.set(dx, dy, hex("#6E9E5E"));
    c.set(dx + 1, dy - 1, hex("#8ABE72"));
  }
  c.outline(hex(INK));
  return c;
}

// ---------------------------------------------------------------- 27 张 = 表里 27 行

/** 相对 `assets/` 的路径 → 画布。**每张的键都是 `equip_base.equip_id`，一位不能差。** */
function buildAll() {
  return {
    // 武器 9（斜 45°）
    "icons/equip/eq_sword_01.png": swordIcon({ mat: "iron", guard: "iron", grip: "leather" }),
    "icons/equip/eq_sword_02.png": swordIcon({ mat: "steel", guard: "copper", grip: "ebony", tassel: true }),
    "icons/equip/eq_sword_03.png": saberIcon({ mat: "ebony", grip: "leather", tassel: true }),
    "icons/equip/eq_sword_04.png": swordIcon({ mat: "rust", guard: "iron", grip: "cloth", moon: true, notch: true }),
    "icons/equip/eq_fist_01.png": fistIcon({ mat: "iron", strap: "leather" }),
    "icons/equip/eq_fist_02.png": fistIcon({ mat: "ebony", strap: "leather" }),
    "icons/equip/eq_blade_01.png": saberIcon({ mat: "iron", grip: "waxwood", len: 12 }),
    "icons/equip/eq_spear_01.png": spearIcon({ shaft: "waxwood", tip: "iron" }),
    "icons/equip/eq_spear_02.png": spearIcon({ shaft: "iron", tip: "steel" }),
    // 防具 12（正面居中）
    "icons/equip/eq_armor_01.png": coatIcon({ mat: "cloth", trim: "cloth" }),
    "icons/equip/eq_armor_02.png": coatIcon({ mat: "leather", trim: "leather", stitches: true }),
    "icons/equip/eq_armor_03.png": coatIcon({ mat: "leather", trim: "leather", lamellar: true }),
    "icons/equip/eq_legs_01.png": legsIcon({ mat: "cloth" }),
    "icons/equip/eq_legs_02.png": legsIcon({ mat: "leather", greaves: true }),
    "icons/equip/eq_shoulder_01.png": shoulderIcon({ mat: "cloth", trim: "cloth" }),
    "icons/equip/eq_shoulder_02.png": shoulderIcon({ mat: "leather", trim: "leather", boss: true }),
    "icons/equip/eq_shoulder_03.png": shoulderIcon({ mat: "leather", trim: "leather", fur: true }),
    "icons/equip/eq_head_01.png": shoulderIcon({ mat: "ebony", trim: "gold", gold: true }),
    "icons/equip/eq_belt_01.png": beltIcon({ mat: "cloth", knot: true }),
    "icons/equip/eq_belt_02.png": beltIcon({ mat: "leather", buckle: "copper" }),
    // 饰品 7（正面居中）
    "icons/equip/eq_ring_01.png": ringIcon({ mat: "copper", mark: "plain" }),
    "icons/equip/eq_ring_02.png": ringIcon({ mat: "jade", mark: "jade" }),
    "icons/equip/eq_ring_03.png": ringIcon({ mat: "iron", mark: "poison", band: 3 }),
    "icons/equip/eq_ring_04.png": ringIcon({ mat: "iron", mark: "hammer", band: 3 }),
    "icons/equip/eq_acc_01.png": coinString(),
    "icons/equip/eq_acc_02.png": jadePendant(),
    "icons/equip/eq_acc_03.png": medicineTalisman(),
  };
}

module.exports = {
  buildAll, MAT,
  swordIcon, saberIcon, spearIcon, fistIcon,
  coatIcon, legsIcon, shoulderIcon, beltIcon,
  ringIcon, coinString, jadePendant, medicineTalisman,
};
