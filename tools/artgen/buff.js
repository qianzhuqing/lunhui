// 增益图标（32×32）：`icons/buff/<buff_def.icon>.png`，19 张。
//
// 三条口径都来自表与设计，改之前先回表确认：
//
//   · **文件名 = `buff_def.icon` 的值，不是 `buff_id`**——19 行里 13 行是缩写
//     （`buff_set_heifeng_2` → `buff_set_hf2`）。游戏侧 `battle_screen._make_effect_chip()`
//     取的是 `icon` 列、空才退回行 id（`IconPaths.buff()`），门限 `test_map_assets` 同一口径。
//   · **颜色只走「增益蓝」**（08 §181「增益蓝、减益红」）：表里 19 行 `is_debuff` 全是 0，
//     第一章的「减益红」由 `status_effect`（异常 4 张，走 `icons/status/`）承担，
//     所以这一批**一张红的都不出**（小策划 2026-10-04 拍板）。
//   · 骨架与异常同一套：**亮色圆底 ＋ 深记号**（`badge.js`）——两者并排在同一条 chip 列表里。
//
// 19 张要在同一条 chip 列表里互相认得出，所以按**母题分四族**、都留在蓝系里
// （靠记号形状拉开，不靠色相）：
//
//   钢蓝 `STEEL`  防御／护体：防御姿态、药王护体、寨主遗威
//   青碧 `QI`     内功运功形态（同一部内功的"运功"形态都归这里）：运功、太清境、蛊毒附手、
//                 续命诀、忘忧
//   靛蓝 `INDIGO` 剑意与套装档位：锈月剑意、黑风两件／四件、剑意三招／五招、心法四格／七格
//   青蓝 `CYAN`   生活与杂项：淬毒、醉步、打坐余韵、饱食
//
// 套装那 6 张**不画件数角标**——A15 定的是「图标不承担星级，数量由界面文字说」，
// 所以两件／四件、三招／五招靠**母题的满与更满**区分（风两笔／四笔、单剑／交叉双剑）。

"use strict";

const { hex } = require("./pixel");
const { badge } = require("./badge");

const STEEL = "#3A6FA8", STEEL_LIGHT = "#8FBCE4";
const QI = "#3A7C94", QI_LIGHT = "#86C8D8";
const INDIGO = "#3E5E9E", INDIGO_LIGHT = "#94AEDE";
const CYAN = "#45788E", CYAN_LIGHT = "#93C4D4";

/** 防御姿态：盾。盾面走中脊 ＋ 上缘两道**亮线**——深记号里只有亮线能看出结构（同灼伤内焰那一手）。 */
function buffGuard() {
  return badge(STEEL, STEEL_LIGHT, (c, d, l) => {
    c.hLine(10, 8, 11, d);                          // 盾顶
    for (let k = 1; k < 8; k++) c.hLine(10, 8 + k, 11, d);
    for (let k = 0; k < 8; k++) {                   // 下缘收尖
      const w = Math.max(1, Math.round(11 * (1 - (k + 1) / 9)));
      c.hLine(Math.round(15.5 - w / 2), 16 + k, w, d);
    }
    c.rect(15, 11, 2, 6, l);                        // 中脊
    for (const [x, y] of [[12, 16], [13, 17], [14, 18], [17, 18], [18, 17], [19, 16]]) {
      c.set(x, y, l);                               // 一道 V：盾牌上的纹章，别用横线（横线会读成钥匙孔）
    }
  });
}

/** 运功：一圈**带断口的气环** ＋ 丹田一点——与武学内功那套同一个语汇（气环＝内功），
 *  只是这边是"亮底上的深记号"，所以只取环与点两笔。 */
function buffYunqi() {
  return badge(QI, QI_LIGHT, (c, d) => {
    for (let a = 0; a < 360; a += 3) {
      if (a > 200 && a < 250) continue;             // 断口：与武学内功同一个方位
      const r = (a * Math.PI) / 180;
      c.set(15.5 + Math.cos(r) * 8.5, 15.5 + Math.sin(r) * 8.5, d);
      c.set(15.5 + Math.cos(r) * 7.3, 15.5 + Math.sin(r) * 7.3, d);
    }
    c.disk(15.5, 15.5, 2.5, d);
  });
}

/** 太清境：阴阳鱼（两片半鱼 ＋ 一对鱼眼）。留白的下半鱼靠**底色露出**，不另上色。 */
function buffTaiping() {
  return badge(QI, QI_LIGHT, (c, d) => {
    const cx = 15.5, cy = 15.5, R = 8.5, r = R / 2;
    for (let y = Math.round(cy - R); y <= Math.round(cy + R); y++) {
      for (let x = Math.round(cx - R); x <= Math.round(cx + R); x++) {
        const dx = x - cx, dy = y - cy;
        if (dx * dx + dy * dy > R * R) continue;
        const topLobe = dx * dx + (dy + r) ** 2 <= r * r;   // 上小圆：暗
        const botLobe = dx * dx + (dy - r) ** 2 <= r * r;   // 下小圆：亮
        let dark = dx >= 0;
        if (topLobe) dark = true;
        if (botLobe) dark = false;
        if (dark) c.set(x, y, d);
      }
    }
    c.disk(cx, cy - r, 1.6, hex(QI));               // 上鱼眼（暗鱼里的底色点）
    c.disk(cx, cy + r, 1.6, d);                     // 下鱼眼
  });
}

/** 蛊毒附手：一只**蛊虫**（椭圆身 ＋ 三对足 ＋ 一对触须）。
 *  **不画毒滴**：毒滴是异常（中毒）那一族的记号，这张是"给药手附了蛊"，用虫把两者分开。 */
function buffVenom() {
  return badge(QI, QI_LIGHT, (c, d) => {
    c.ellipse(15.5, 15, 4, 6.5, d);                 // 身
    c.set(15, 9, d);                                // 头
    c.set(16, 9, d);
    for (const y of [12, 15, 18]) {                 // 三对足：往外下方伸
      c.set(11, y, d); c.set(10, y + 1, d);
      c.set(20, y, d); c.set(21, y + 1, d);
    }
    c.set(13, 7, d); c.set(12, 6, d);               // 触须
    c.set(18, 7, d); c.set(19, 6, d);
    c.rect(15, 12, 2, 6, hex(QI));                  // 背中线露底色，否则是一坨黑橄榄
  });
}

/** 续命诀：心（两瓣 ＋ 下尖）。这一族里的**身体母题**，与葫芦（药王护体）分开。 */
function buffLife() {
  return badge(QI, QI_LIGHT, (c, d, l) => {
    c.disk(11.5, 12.5, 4.5, d);
    c.disk(19.5, 12.5, 4.5, d);
    for (let k = 0; k < 12; k++) {                  // 收成下尖
      const w = Math.max(1, Math.round(17 * (1 - k / 12)));
      c.hLine(Math.round(15.5 - w / 2), 14 + k, w, d);
    }
    c.disk(12, 11, 1.6, l);
  });
}

/** 忘忧：**酒坛**（封口的布 ＋ 短颈 ＋ 鼓腹 ＋ 一道亮箍）。
 *  与药王护体的葫芦分开画：葫芦是两个球带腰，坛子是单腹带颈，摆一起不会混。 */
function buffDrunk() {
  return badge(QI, QI_LIGHT, (c, d, l) => {
    c.hLine(13, 6, 7, d);                           // 坛口
    c.hLine(12, 7, 9, d);
    c.rect(14, 8, 5, 3, d);                         // 颈
    c.ellipse(15.5, 17, 8, 7.5, d);                 // 腹
    c.disk(12, 14, 2, l);                           // 受光
    c.hLine(10, 19, 4, l);                          // 亮箍：把腹分成两段，否则是个黑球
  });
}

/** 锈月剑意：一弯月 ＋ 一柄斜剑 ＋ 两点锈斑（锈斑用底色点掉，08 只给蓝红两条路，不另开色）。 */
function buffRusty() {
  return badge(INDIGO, INDIGO_LIGHT, (c, d) => {
    c.disk(13.5, 13.5, 7.5, d);                     // 月
    c.disk(17.5, 11, 6.8, hex(INDIGO));             // 挖掉一口 → 弯月开口朝右下
    for (let k = 0; k < 9; k++) {                   // 斜剑：右下角一段短刃
      c.set(15 + k, 24 - k, d);
      c.set(16 + k, 24 - k, d);
    }
    c.set(24, 14, d);                               // 剑尖
    c.set(17, 18, hex(INDIGO)); c.set(19, 16, hex(INDIGO));  // 锈斑
  });
}

/** 寨主遗威：一杆**令旗**（旗杆 ＋ 右飘的旗面 ＋ 旗尾的燕尾豁口）。 */
function buffChief() {
  return badge(STEEL, STEEL_LIGHT, (c, d, l) => {
    c.rect(10, 6, 2, 20, d);                        // 杆：1px 的线在 16px 下会断，加粗到 2px
    for (let k = 0; k < 10; k++) {                  // 旗面：中间最宽
      const w = Math.round(9 * Math.sin(((k + 0.5) / 10) * Math.PI) * 0.85 + 2);
      c.hLine(12, 7 + k, w, d);
    }
    c.rect(19, 10, 3, 2, hex(STEEL));               // 燕尾豁口
    c.rect(20, 14, 2, 2, hex(STEEL));
    c.hLine(13, 12, 4, l);                          // 一点亮
  });
}

/** 淬毒：**一刃 ＋ 刃下挂一滴**——"把刀淬进毒里"。
 *  与中毒（异常那族）分开：中毒是三粒往上飘的泡，这里是刃下的单滴。 */
function buffPoison() {
  return badge(CYAN, CYAN_LIGHT, (c, d, l) => {
    for (let k = 0; k < 11; k++) {                  // 刃：45° 斜上
      c.set(10 + k, 19 - k, d);
      c.set(11 + k, 19 - k, d);
      c.set(10 + k, 18 - k, d);
    }
    c.hLine(8, 21, 4, d);                           // 柄
    c.disk(17.5, 21.5, 2.6, d);                     // 滴
    c.set(17, 18, d); c.set(18, 18, d);             // 滴尖
    c.disk(16.5, 21, 1.1, l);
  });
}

/** 药王护体：**葫芦**（木塞 ＋ 短颈 ＋ 上小下大两球）——药王的招牌。 */
function buffRegen() {
  return badge(STEEL, STEEL_LIGHT, (c, d, l) => {
    c.hLine(12, 6, 8, d);                           // 塞
    c.rect(14, 7, 4, 3, d);                         // 颈
    c.disk(15.5, 12, 3.8, d);                       // 上球
    c.disk(15.5, 20, 6.2, d);                       // 下球
    // 腰：两侧露底色，把两球分开——不然合成一个黑团（第一版就是这样）
    c.rect(9, 14, 3, 3, hex(STEEL));
    c.rect(19, 14, 3, 3, hex(STEEL));
    c.disk(12.5, 18.5, 2.2, l);                     // 受光
  });
}

/** 醉步：**两只斜着的脚印**（脚掌椭圆 ＋ 三个前趾），一前一后——"步"用脚说最省。 */
function buffZuibu() {
  return badge(CYAN, CYAN_LIGHT, (c, d, l) => {
    // 一只脚 = 脚掌椭圆 ＋ 上面一枚扁的脚趾椭圆（中间留 1px 断口）。
    // 试过"一排三粒小趾"，16px 下三粒连成一根横杠、整只脚读成两团黑，所以改成两截式。
    c.ellipse(11.5, 19.5, 2.8, 3.4, d);             // 后脚掌
    c.ellipse(11, 14.5, 3.2, 1.6, d);               // 后脚趾
    c.ellipse(19.5, 12.5, 2.8, 3.4, d);             // 前脚掌
    c.ellipse(20, 7.5, 3.2, 1.6, d);                // 前脚趾
    c.disk(11, 18, 1.2, l);
  });
}

/** 一道「风」：左端平的横笔、右端往上卷一小钩——**卷钩是"风"的辨识点**，
 *  两笔平直的横线会被读成"二"。 */
function gust(c, d, x, y, len) {
  c.hLine(x, y, len, d);
  c.hLine(x, y - 1, len - 1, d);
  c.set(x + len, y - 2, d);
  c.set(x + len, y - 3, d);
  c.set(x + len - 1, y - 4, d);
}

/** 黑风·两件：**两道风**（上短下长）。 */
function buffSetHf2() {
  return badge(INDIGO, INDIGO_LIGHT, (c, d) => {
    gust(c, d, 10, 13, 7);
    gust(c, d, 9, 19, 11);
  });
}

/** 黑风·四件：**四道风**——同一个母题画满，比两件那两张更盛（件数由界面文字说，A15）。 */
function buffSetHf4() {
  return badge(INDIGO, INDIGO_LIGHT, (c, d) => {
    gust(c, d, 12, 9, 6);
    gust(c, d, 9, 14, 11);
    gust(c, d, 12, 19, 7);
    gust(c, d, 10, 23, 10);
  });
}

/** 剑意·三招：一柄**斜剑**（剑身 ＋ 剑格 ＋ 剑首）。 */
function buffSetXs3() {
  return badge(INDIGO, INDIGO_LIGHT, (c, d, l) => {
    for (let k = 0; k < 15; k++) {                  // 剑身
      c.set(9 + k, 22 - k, d);
      c.set(10 + k, 22 - k, d);
    }
    c.hLine(8, 20, 4, d);                           // 剑格
    c.set(7, 23, d);                                // 剑首
    c.set(7, 24, d);
    c.hLine(12, 18, 3, l);                          // 剑脊一点亮
  });
}

/** 剑意·五招：**交叉双剑**——同一母题更满。 */
function buffSetXs5() {
  return badge(INDIGO, INDIGO_LIGHT, (c, d, l) => {
    for (let k = 0; k < 13; k++) {
      c.set(9 + k, 22 - k, d);                      // 左下一柄
      c.set(10 + k, 22 - k, d);
      c.set(20 - k, 22 - k, d);                     // 右下一柄（交叉）
      c.set(19 - k, 22 - k, d);
    }
    c.set(8, 23, d); c.set(21, 23, d);              // 两个剑首
    c.hLine(13, 18, 4, l);                          // 交叉处的亮
  });
}

/** 心法的「丹田格」：一个**方框**——"格"就是母题本身，不是计数角标（A15）。 */
function grid(c, d) {
  c.rect(7, 7, 17, 2, d);
  c.rect(7, 22, 17, 2, d);
  c.rect(7, 7, 2, 17, d);
  c.rect(22, 7, 2, 17, d);
}

/** 心法·四格：方框里 **2×2 四点**。 */
function buffSetXq4() {
  return badge(INDIGO, INDIGO_LIGHT, (c, d) => {
    grid(c, d);
    for (const [x, y] of [[11.5, 11.5], [19.5, 11.5], [11.5, 19.5], [19.5, 19.5]]) {
      c.disk(x, y, 2, d);
    }
  });
}

/** 心法·七格：同一个方框里 **七点**（中心一点 ＋ 六点环绕）——比四格满。 */
function buffSetXq7() {
  return badge(INDIGO, INDIGO_LIGHT, (c, d) => {
    grid(c, d);
    c.disk(15.5, 15.5, 1.8, d);
    for (let a = 0; a < 360; a += 60) {
      const r = (a * Math.PI) / 180;
      c.disk(15.5 + Math.cos(r) * 4.6, 15.5 + Math.sin(r) * 4.6, 1.7, d);
    }
  });
}

/** 打坐余韵：**盘坐的剪影**（头 ＋ 肩背 ＋ 交叠的腿）＋ 衣褶的亮线。 */
function buffMed() {
  return badge(CYAN, CYAN_LIGHT, (c, d, l) => {
    c.disk(15.5, 10.5, 3, d);                       // 头
    for (let k = 0; k < 6; k++) {                   // 肩背：上窄下宽
      const w = 5 + k * 1.4;
      c.hLine(Math.round(15.5 - w / 2), 15 + k, Math.round(w), d);
    }
    c.ellipse(15.5, 21.5, 8, 3.2, d);               // 交叠的腿
    c.hLine(12, 17, 7, l);                          // 衣褶的亮线
  });
}

/** 饱食：**一碗饭 ＋ 一副筷子**（两根斜搭在碗口上）。 */
function buffMeal() {
  return badge(CYAN, CYAN_LIGHT, (c, d, l) => {
    for (let k = 0; k < 8; k++) {                   // 碗：上宽下窄
      const w = 15 - k * 1.3;
      c.hLine(Math.round(15.5 - w / 2), 15 + k, Math.round(w), d);
    }
    c.hLine(12, 22, 7, d);                          // 碗底
    c.hLine(11, 15, 9, l);                          // 碗口那层饭的亮边
    for (let k = 0; k < 7; k++) {                   // 筷子：两根平行斜笔，搭在碗口左上方
      c.set(14 + k, 14 - Math.round(k * 0.9), d);
      c.set(17 + k, 14 - Math.round(k * 0.9), d);
    }
  });
}

/**
 * 相对 `assets/` 的路径 → 画布。
 * **文件名 = `buff_def.icon` 的值，一位不能差**——19 行里 13 行与 `buff_id` 不同名，
 * 照 id 交图的话游戏里那张就是"有行没图"（不报错，只是 chip 上少个图标）。
 */
function buildAll() {
  return {
    "icons/buff/buff_guard.png": buffGuard(),
    "icons/buff/buff_yunqi.png": buffYunqi(),
    "icons/buff/buff_taiping.png": buffTaiping(),
    "icons/buff/buff_venom.png": buffVenom(),
    "icons/buff/buff_life.png": buffLife(),
    "icons/buff/buff_drunk.png": buffDrunk(),
    "icons/buff/buff_rusty.png": buffRusty(),
    "icons/buff/buff_chief.png": buffChief(),
    "icons/buff/buff_poison.png": buffPoison(),
    "icons/buff/buff_regen.png": buffRegen(),
    "icons/buff/buff_zuibu.png": buffZuibu(),
    "icons/buff/buff_set_hf2.png": buffSetHf2(),
    "icons/buff/buff_set_hf4.png": buffSetHf4(),
    "icons/buff/buff_set_xs3.png": buffSetXs3(),
    "icons/buff/buff_set_xs5.png": buffSetXs5(),
    "icons/buff/buff_set_xq4.png": buffSetXq4(),
    "icons/buff/buff_set_xq7.png": buffSetXq7(),
    "icons/buff/buff_med.png": buffMed(),
    "icons/buff/buff_meal.png": buffMeal(),
  };
}

module.exports = { buildAll };
