// 招式图标·方底版（32×32）：参考《大江湖之苍龙与白鸟》（Steam 1407450）的图标语法重做。
//
// 为什么另起一支而不是改 `skills.js`：那一支是"半透明墨圆 ＋ 单色剪影"，已经交付并在用；
// 这一支是"**方格底 ＋ 描边 ＋ 多阶明暗**"，两者要能并排比对（用户 2026-10-11 拍板：
// **底板用方格子、先做 32×32**）。等 65 张全过一遍、用户点头，再把 `skills.js` 那一支退役。
//
// 从参考图学到的三条（写在交接单里，这里只做实现）：
//   ① 方格底：最外 1px 墨（合 A2「图标自带一圈墨色描边」）＋ 内 1px 米色细边 ＋ 深底，
//      顶上提亮一行、底下压暗一行，方块才有厚度；
//   ② 本体是**带描边的 3–4 阶明暗**：受光在左上、背光在右下，两层交界处**抖动**——
//      这是"质感"的来源，单色剪影永远做不出来；
//   ③ **区分度靠形体**：同门同色之间换姿态（斜出／上挑／竖劈／横扫）＋ 一处与招式名对应的势，
//      不靠贴小记号、也不靠换颜色。
//
// 所以这一支的骨架是「**构图（mask）与上色（paint）分开**」：
// 每个招式只管往透明 mask 上画剪影，`paint()` 统一按左上受光上 3–4 阶 ＋ 加墨描边。
// 换个角度、换个姿势＝只改 mask，明暗规则一个字不改（与 `skills.js` 那条"气环档位是骨架"同一思路）。

"use strict";

const path = require("path");
const { Canvas, hex, shade, mix, encodePNG } = require("./pixel");
const { SCHOOL_COLORS } = require("./skills");

const S = 32;
const INK = "#0E1113";      // 描边：比方格最外那圈还深，压在深底上才立得住
const BEVEL = "#C8BC96";    // 方格内框的米色细边（参考图那圈）
const HOLE = [0, 0, 0, 0];
const MASK = [255, 255, 255, 255];   // 剪影只占 alpha，颜色取白（`Canvas.set` 要的是数组不是色名）

/** 方格子：最外 1px 墨、内 1px 米、里面学派底色 ＋ 上下各一行提亮／压暗。 */
function tileFrame(colors) {
  const c = new Canvas(S, S);
  c.fill(HOLE);
  c.rect(0, 0, S, S, hex(INK));
  c.frame(1, 1, S - 2, S - 2, hex(BEVEL));
  c.rect(2, 2, S - 4, S - 4, hex(colors.plate));
  c.hLine(2, 2, S - 4, hex(shade(colors.plate, 0.12)));
  c.vLine(2, 2, S - 4, hex(shade(colors.plate, 0.06)));
  c.hLine(2, S - 3, S - 4, hex(shade(colors.plate, -0.35)));
  return c;
}

/**
 * 学派色 → 上色五阶。**星阶只改明度**（`skills.js` 那条 A15 的同一个口径：
 * 招式的星阶不进图标、不加角标，靠"同一色相往上提亮"来读），所以 ★1 偏暗、★5 接近 light。
 */
function palette(school, star = 1) {
  const s = SCHOOL_COLORS[school];
  const body = mix(s.main, s.light, 0.22 + star * 0.12);
  return {
    dark: shade(body, -0.42),
    body,
    lit: s.light,
    hi: shade(s.light, 0.35),
    faint: mix(body, s.plate, 0.35),     // 气劲／水纹这种"轻"的笔画
  };
}

// ---------------------------------------------------------------- 构图工具（都画在透明 mask 上）

/** 斜直线（角度按数学惯例：0°＝右、90°＝上），`w` 像素粗。 */
function stroke(c, cx, cy, deg, len, w, color) {
  const rad = (deg * Math.PI) / 180;
  const dx = Math.cos(rad), dy = -Math.sin(rad);
  const nx = -dy, ny = dx;
  for (let t = 0; t <= len; t += 0.25) {
    for (let k = 0; k < w; k++) {
      const off = k - (w - 1) / 2;
      c.set(Math.round(cx + dx * t + nx * off), Math.round(cy + dy * t + ny * off), color);
    }
  }
}

/** 弧线（用于出手弧、音波、回旋）。 */
function arc(c, cx, cy, r, a0, a1, color, step = 3) {
  for (let a = a0; a <= a1; a += step) {
    const rad = (a * Math.PI) / 180;
    c.set(Math.round(cx + Math.cos(rad) * r), Math.round(cy - Math.sin(rad) * r), color);
  }
}

/**
 * 一根"沿轴加宽"的笔：`(ox,oy)` 起点、`(dx,dy)` 轴、`(nx,ny)` 法线，`t` 从 `t0` 到 `t1`，
 * 每个位置画法线方向 `±half` 宽的一条。
 */
function bar(c, ox, oy, dx, dy, nx, ny, t0, t1, w, color) {
  for (let t = t0; t <= t1; t += 0.2) {
    const cx = ox + dx * t, cy = oy + dy * t;
    for (let k = 0; k < w; k++) {
      const off = k - (w - 1) / 2;
      c.set(Math.round(cx + nx * off), Math.round(cy + ny * off), color);
    }
  }
}

/**
 * 剑的四段比例（`ref_glyph.png` 那把握手式量出来的口径，改这里就等于改全套剑的姿态）。
 * 数字是"垂直的宽度 ／ 沿轴的长度"，单位都是像素（**垂直宽度就是这个宽度本身**）：
 *   `pommel` 柄头实心方 3.0×1.2 ／ `grip` 柄 **1.8×3.0** ／ `guard` 护手 **11×1.4** ／
 *   `blade` 刃宽 **3.1** ／ `tip` 收尖长度 **2.6**（Why 见 `SWORD.tip` 上头那段）
 *
 * 全套数字都是照参考图那把**垂直量**出来的比值缩到"全长 26px"的（参考：宽 8、刃长 38、
 * 护手跨度 24、护手厚 5.7、柄 15）：
 *
 * | 量什么 | 参考图比值 | 全长 26 时该是多少 | 现在的取值 |
 * |---|---|---|---|
 * | 刃宽 : 剑全长 | 8 : 59 ＝ 0.136 | 3.5px 垂直 | `blade: 3.1`（格子上 **3px／行**，见下面那条 ⚠️） |
 * | 护手跨度 : 刃宽 | 27 : 8 ＝ **3.4** | 11.9px 垂直 | `guard: 11`（格子上 11px） |
 * | 护手厚 : 刃宽 | 5 : 8 ＝ 0.63 | ≈ 2.2px | `guard` 第二维 1.4（格子上 2px） |
 * | 柄 : 剑全长 | 15 : 59 ＝ 0.25 | 6.5px | `pommel 1.2 ＋ grip 3.0` ＝ 4.2 |
 *
 * **三条都是"读不读得出是剑"的关键**（2026-10-11 用户打回三次之后才凑齐，别再动）：
 *   ① **护手要"又长又薄"**：跨度 ≈ 刃宽的 3.4 倍、厚度只有 2px。护手小／厚了整把就成"匕首"；
 *   ② **刃要细长**：刃宽 ≈ 全长的 13%。**这一条我一开始判反了**——当时把"刃细"当成"像针"
 *      一路加粗到 4.6（每行 5–7px），结果用户一眼："现在样子不是剑的样子"。
 *      参考图那把在 32px 尺度上刃只有 **2.8px 垂直**；
 *   ③ **直的那一段要占刃长八成以上，角度只留在尖上**（用户原话："只有剑尖有角度，
 *      80% 以上的剑身是直的"）——tip 2.6 约 13%。
 *
 * ⚠️ **起手点必须落在"半格"上，让剑轴正好穿过一格的中心**（`CANON_SWORD` 用的是 `x0 = 13.5`
 * → 轴在 `x = 13`）。这是**能不能出 1px 剑尖**的开关：轴在格心时，垂直宽度只会取**奇数**
 * （1／3／5…），所以 `blade 3.1 → 3px`、`grip 1.8 → 1px`、`pommel 3.0 → 3px`，
 * 收尖收到最后剩**正中那 1px**——用户要的"对称的三角形"才是真的。
 * 起手点给整数（轴落在两格之间）时宽度只能取偶数（2／4／6…），收尖最少剩 2px 平帽。
 * 两版并排比对过：`tip_variants.png`（A 偶宽 4px 刃／B 尖短／C 奇宽）与
 * `left_variants.png`（用户 2026-10-11 拍板"剑位置靠左、剑尖尖锐一些"时四选一的那张）。
 */
const SWORD = {
  pommel: [3.0, 1.2],
  grip: [1.8, 3.0],    // 奇宽档里的"比刃细一档"：给 2.4 会与刃并成同一个 3px 宽
  guard: [11, 1.4],    // 不另加钝头：护手本身就是一根横杠（参考图 厚度:跨度 ≈ 1:4）
  blade: 3.1,
  /**
   * 收尖长度（沿轴像素）。**判据是"收得完"**：90° 上相邻两行在轴上正好差 1px，奇数档
   * 每行跳 2px（3→1），所以 `tip` 只要 ≥ 1.5 就够收到那 1px 顶点；给 2.6 是让
   * **顶点立两行**——只立一行时放大看是个"点在一根细棍上"，两行才读成一个尖。
   * 给大了（3.1 那种）会多出一行 1px 的细脖子，反而像被拉长的针。
   */
  tip: 2.6,
};

/**
 * 剑。`(x0,y0)` 在**柄头那一端**，`deg` 指刃尖方向，`len` 是柄头到刃尖的总长。
 *
 * 形状是照参考图**量出来的比例**重画的，三处与前两版不同、都是"读不读得出是剑"的关键，
 * 别再退回去：
 *   ① **刃尖左右对称收**（每级两侧各收 1px）。**别再做单边斜切**：用户 2026-10-11 一眼
 *      就看出来"剑尖为什么不对称"。能不能收成**正中那 1px** 全靠起手点——必须落在半格上
 *      （见 `SWORD` 上头那条 ⚠️）：轴落在格心时宽度只取奇数，收尖收到正中 1px；
 *      整数起手点（轴在两格之间）时宽度只取偶数，最少剩 2px 平帽。
 *   ② **刃要够长**（见 `SWORD` 上头那段）：**直的那一段占 80% 以上，角度只留在尖上**
 *      （用户 2026-10-11 的原话："只有剑尖有角度，80% 以上的剑身是直的"），
 *      所以收尖长度 `tip` 不许超过刃长的两成。
 *   ③ **柄头是一块实心方、不是横杠**——与护手同为横杠时会撞脸，读成"双十字"。
 * 其余按参考图：刃是**等宽条**（不是两头收的梭子），柄明显比刃细。
 */
function sword(c, x0, y0, deg, len, color, o = SWORD) {
  const rad = (deg * Math.PI) / 180;
  const dx = Math.cos(rad), dy = -Math.sin(rad);
  const nx = -dy, ny = dx;
  const [pw, pl] = o.pommel;             // [垂直宽, 沿轴长]
  const [gw, gl] = o.grip;
  const [cw, ch] = o.guard;
  const g0 = pl + gl;                    // 护手起点（离柄头端的距离）
  const b0 = g0 + ch;                    // 刃根
  /**
   * 半宽曲线：`u` 是"离柄头端多远"（沿轴，像素），返回该处的**垂直半宽**。
   *
   * 三处形状问题都在这一版的换法里一起解决了——旧版是"沿轴走、每处按法线落 `w` 个像素"
   * （`bar()`），两个毛病：① 斜摆时法线每走 1px 在两个轴上各只动 0.707，取整后相邻偏移并格，
   * 同一把剑 45° 比 90° 细一圈（用户"怎么做着做着剑的大小不一样了"），为此要乘
   * `1/max(|nx|,|ny|)` 硬补，补完 45° 的护手又胖到出格；② **刃尖是逐步`round`出来的**，
   * 收到最后几级时左右不对称、还落下一截 4px 宽的平头（用户 2026-10-11："靠近剑尖那里
   * 变形了"）。现在改成**按连续形状逐像素判定**：把像素中心投影到 `(u, v)`，落在半宽曲线里
   * 就点亮。宽度从此是"垂直像素"这一个口径，**哪个角度画都一个粗细**，尖也自然收成一个点。
   */
  const halfBody = (u) => {
    if (u < pl) return pw / 2;
    if (u < g0) return gw / 2;
    if (u < b0) return cw / 2;
    return o.blade / 2;
  };
  // 沿"一行"走时 `u` 随 `v` 涨的比例：45° 是 1（行是斜的）、90° 是 0（行与轴垂直）。
  // 用来把像素换算到**它所在那一行与轴的交点**——收尖必须按这个量，理由见下面。
  const cot = Math.abs(dy) < 1e-6 ? 0 : dx / -dy;
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      // 像素中心 (x+0.5, y+0.5) 投影到轴坐标：`u` 沿轴、`v` 垂直
      const px = x + 0.5 - x0, py = y + 0.5 - y0;
      const u = px * dx + py * dy;
      if (u < 0) continue;
      const v = px * nx + py * ny;
      /**
       * ⚠️ **收尖要按 `uRow`（本行与轴的交点），不能按像素自己的 `u`**——这是个只斜着摆才会
       * 露出来的坑（用户 2026-10-11 第二次退："靠近剑尖那里变形了"）：45° 上同一行的像素
       * `u` 和 `v` 是一起涨的（`x` 每 +1，`u`、`v` 各 +0.707），拿 `u` 去收，收掉的永远是
       * **离轴更远的那一侧**，一路收下来剩的是一截**偏在一遍的竖墙**（实测那版剑尖是
       * "`x=22` 竖着四格 ＋ 各一行越来越宽"）。换成 `uRow` 之后，同一行的像素 `uRow` 相同，
       * 收尖就是"一行比一行窄、且始终关于轴对称"——7→5→5→3→1 那样收成一个点。
       * 90° 摆时 `cot = 0`，两者本来就相等，这一条不影响直着摆的剑。
       */
      const uRow = u - v * cot;
      if (uRow > len) continue;
      const w =
        uRow <= len - o.tip
          ? halfBody(u)
          : (o.blade / 2) * (1 - (uRow - (len - o.tip)) / o.tip);   // 对称收尖
      // ⚠️ **要的是"对称"就不许再 round**：判定用连续值，镜像像素的 `|v|` 必然相等，收尖、护手
      // 两端、刃缘都天然左右对齐。（45° 的轴是 `x+y = x0+y0−1`、90° 是 `x = x0−0.5`，
      // 两者都把格心映到格心——**前提是起手点是整数**，所以起手点别挪到半格上。）
      if (Math.abs(v) <= w) c.set(x, y, color);
    }
  }
}

// ---------------------------------------------------------------- 其余兵器（刀／枪／掌／腿／破气弧／箭）
//
// 与剑**同一套落位**：竖摆、轴在 `x = 12`（`CANON_SWORD` 的 `x0 = 12.5` 落成的那条格中线）、
// 纵向占满 `y = 3–28`（26 格长）。同一族的图标换兵器不换摆法——用户 2026-10-11 定的口径
// "武器的位置、方向、大小保持一致"对五种兵器一起生效。
//
// **每一支的区分点在哪**（别只换颜色，那是用户打回过一次的错）：
//   剑＝对称三角尖；刀＝**单刃**（刀身宽一档、尖斜切到刀背一侧）；枪＝**1px 长杆 ＋ 叶形头 ＋
//   缨**；掌＝三根分开的指＋掌＋腕（有指缝才读得出是手）；腿＝小腿＋朝右的靴；破气弧＝
//   通用招（破军式），**不画任何兵器**，只有一道弧；箭＝细杆＋三角镞＋尾羽。

/**
 * 刀（单刃）。**形体照参考图的"刀"类型剪影改的**（`ref_ss23.png` 那格，2026-10-11）：
 *   ① **刀身往尖那头变宽**（柄侧 3px → 尖侧 6px）：参考图那把是**宽头刀**（砍刀那一路），
 *      不是等宽的柳叶刀——这一条是它与剑最大的差别，别再画成等宽条子；
 *   ② **尖是斜切的"削头"**，最尖的一点落在**刃侧**（右），往刀背（左）斜着退，退到最后
 *      留一个小**缺角**（参考图那把的尖上就有个明显的台阶）；
 *   ③ 刀格比剑的护手**短一截、偏向刃侧**（单刃刀的护手本来就不对称）；
 *   ④ 柄头是**外撇的方块**（参考图那把的下端是张开的，不是方头方脑的一小块）。
 * 参考图那格是斜 45° 的，这里是竖摆——**照搬的是形体特征，不是角度**（角度用户已经拍过）。
 */
function saber(c, col) {
  c.rect(11, 28, 4, 1, col);          // 柄头（外撇：比柄宽两格）
  c.vLine(12, 25, 3, col);            // 柄
  c.hLine(9, 23, 8, col);             // 刀格（偏刃侧，跨度 8）
  c.hLine(9, 24, 8, col);
  // 刀身：**刀背（左）是一条直线，只有刃侧（右）往外鼓**——两边一起鼓就成"铲子"了
  c.rect(10, 8, 3, 15, col);          // 刀背那一列（x=10，从 y=8 到 y=22 笔直）
  c.vLine(12, 18, 5, col);            // 柄侧最窄（x=10–12，3px）
  c.vLine(13, 13, 6, col);            // 中段 4px
  c.vLine(14, 9, 5, col);             // 上段 5px
  c.set(15, 7, col); c.set(15, 8, col);      // 近尖最宽 6px（宽头刀）
  c.hLine(12, 6, 4, col);             // 削头：一层层往刀背退
  c.hLine(12, 5, 3, col);
  c.hLine(13, 4, 2, col);
  c.set(14, 3, col);                  // 尖落在刃侧（参考图那把的尖就是偏刃侧的）
}

/**
 * 枪。**形体照参考图的"长兵"类型剪影改的**（同一格表，2026-10-11）：参考图那支的三个特征
 * 是——**细长杆**（整支最细的地方只有 1px）＋**枪镦外撇成喇叭口**（下端明显张开）＋
 * **头下那一团点状的缨**（参考图用点阵画缨，不是实心块）。这三点都照搬：
 * 头是叶形（最宽 5px）、缨是三对小点、镦是 5px 的喇叭口。
 */
function spear(c, col) {
  c.rect(10, 28, 5, 1, col);          // 枪镦（喇叭口：5px）
  c.rect(11, 26, 3, 2, col);
  c.set(9, 27, col); c.set(15, 27, col);     // 镦上的两个小倒钩（参考图那支的尾端是张开的）
  c.vLine(12, 11, 15, col);           // 杆（1px，最细的一段）
  c.rect(11, 9, 3, 2, col);           // 骹（头与杆的接环）
  c.rect(10, 5, 5, 2, col);           // 枪头最宽处（叶腹）
  c.rect(11, 4, 3, 1, col);
  c.rect(11, 7, 3, 2, col);
  c.set(12, 3, col);                  // 尖
  // 缨：点阵（参考图那团缨就是一堆点），左右各三颗，离杆子两格
  for (const [x, y] of [[9, 10], [8, 11], [9, 12], [15, 10], [16, 11], [15, 12]]) c.set(x, y, col);
}

/**
 * 掌。**形体照参考图的"掌"类型剪影改的**（同一格表，2026-10-11）：参考图那只手是
 * **四指张开（1px 指、1px 缝）＋ 掌是块圆的 ＋ 拇指明显伸在掌外**，指缝在剪影上一眼看得见。
 * ⚠️ 两点别再犯：**指不能等长**（等长的三根会被读成叉子/耙子，第一版就是那样），
 * **四根指之间的缝必须留**（缝没了就成一整块"手掌形的板"）。
 * 手是拳门 11 张的共用母题，改这里等于改那 11 张。
 */
function palm(c, col) {
  c.vLine(10, 4, 3, col);             // 无名指
  c.vLine(12, 3, 4, col);             // 中指（最高）
  c.vLine(14, 4, 3, col);             // 食指
  c.vLine(16, 6, 3, col);             // 小指（最短、也最靠外——四指是扇开的）
  c.rect(9, 7, 8, 7, col);            // 掌：8×7 的一块（参考图那只手掌大而圆）
  c.rect(7, 10, 2, 4, col);           // 拇指（伸出掌外两格）
  c.rect(10, 14, 6, 3, col);          // 掌根
  c.rect(11, 17, 4, 4, col);          // 腕
  c.rect(10, 21, 6, 3, col);          // 腕带
  c.rect(10, 24, 6, 5, col);          // 前臂／袖
}

/** 腿（连环腿）：小腿 ＋ 靴。**靴头朝右**——这条腿是横着踢出去的。 */
function boot(c, col) {
  c.rect(11, 4, 3, 17, col);          // 小腿
  c.rect(10, 21, 3, 2, col);          // 踝
  c.rect(9, 23, 9, 3, col);           // 靴
  c.rect(9, 26, 9, 2, col);           // 靴底
}

/** 破气弧（破军式）：通用招，不属于任何兵器门——所以只有一道弧。 */
function crescent(c, col) {
  for (let y = 3; y <= 28; y++) {
    const t = (y - 3) / 25;
    const x = 9 + Math.round(Math.sin(t * Math.PI) * 8);
    c.set(x, y, col);
    c.set(x + 1, y, col);
  }
}

/** 箭（冷箭）：细杆 ＋ 三角镞 ＋ 尾羽。 */
function arrow(c, col) {
  c.vLine(12, 7, 17, col);            // 杆
  c.rect(11, 4, 3, 3, col);           // 镞
  c.set(12, 3, col);                  // 镞尖
  for (const [x, y] of [[10, 25], [11, 26], [10, 27], [11, 28],
                        [14, 25], [13, 26], [14, 27], [13, 28]]) c.set(x, y, col);   // 尾羽
}

// ---------------------------------------------------------------- 母题小签（"势"）
//
// 一律画在**右侧竖条**（`x = 18–27, y = 4–22`），1px 笔画。**加一条硬门限**（第十轮那次的
// 教训）：1px 笔画上色后视觉宽度是 3px，所以**同一张里两道平行笔画要隔 ≥4px**，
// 否则一放大就是一片网点。所以每张最多画三道、而且尽量让它们纵横错开。
//
// 同门之间靠这枚小签区分（用户 2026-10-11 那句"同样的颜色图案太像了"就是冲这个来的）；
// **跨门可以复用形**（颜色已经隔开了），同门之内不许复用。
const MARKS = {
  // —— 醉里乾坤（剑门，暖褐）
  step: (c, col) => { c.rect(19, 5, 2, 2, col); c.rect(23, 10, 2, 2, col); c.rect(19, 15, 2, 2, col); },      // 醉步：三个踩歪的脚印
  drop: (c, col) => { c.vLine(24, 5, 4, col); c.rect(22, 9, 5, 4, col); },                                     // 酒中刀：一滴酒
  turn: (c, col) => { c.vLine(25, 7, 8, col); c.hLine(22, 6, 3, col); c.set(21, 7, col); },                    // 颠倒：一道回勾
  // —— 沉沙掌（拳门，赭石）
  push: (c, col) => { c.hLine(19, 7, 5, col); c.hLine(20, 12, 6, col); c.hLine(21, 17, 6, col); },             // 推山：三道劲线逐级后错
  crack: (c, col) => { c.rect(19, 5, 3, 3, col); c.rect(24, 14, 3, 3, col);
                       c.set(22, 9, col); c.set(23, 11, col); c.set(22, 13, col); },                           // 裂石：两块石＋裂纹
  river: (c, col) => { c.hLine(20, 8, 6, col); c.set(26, 7, col); c.hLine(21, 15, 6, col); c.set(19, 16, col); }, // 翻江：两道反向浪头
  feather: (c, col) => { stroke(c, 23, 5, 245, 10, 1, col); c.set(26, 9, col); c.set(25, 11, col); c.set(27, 13, col); }, // 没羽：一根羽＋两道羽枝
  // —— 五毒（拳门，绿）
  scatter: (c, col) => { c.rect(19, 6, 2, 2, col); c.rect(24, 10, 2, 2, col); c.rect(21, 15, 2, 2, col); },    // 散手：散开的毒点
  haze: (c, col) => { c.hLine(20, 6, 3, col); c.hLine(22, 7, 4, col);
                      c.hLine(20, 13, 3, col); c.hLine(22, 14, 4, col); },                                    // 瘴：两道错开的毒雾
  bone: (c, col) => { c.vLine(24, 6, 8, col); c.rect(22, 4, 5, 2, col); c.rect(22, 14, 5, 2, col); },          // 蚀骨：一根骨
  blood: (c, col) => { c.rect(19, 5, 2, 2, col); c.rect(23, 10, 2, 2, col); c.rect(20, 15, 1, 2, col); },       // 化血：血点
  // —— 黑风刀法（刀门，火把橙）
  slash: (c, col) => { stroke(c, 20, 16, 45, 6, 1, col); stroke(c, 21, 13, -45, 6, 1, col); stroke(c, 25, 6, 45, 4, 1, col); }, // 乱劈：三道乱斜
  mountain: (c, col) => { c.vLine(20, 12, 6, col); c.vLine(23, 8, 10, col); c.vLine(26, 12, 6, col);
                          c.hLine(19, 18, 9, col); },                                                          // 劈山：一座山
  cutwind: (c, col) => { arc(c, 24, 7, 3, 110, 250, col); arc(c, 24, 19, 3, 110, 250, col);
                         stroke(c, 19, 20, 45, 11, 1, col); },                                             // 斩风：两道风团被一道斜斩分开
  sweep: (c, col) => { c.hLine(19, 13, 8, col); c.set(27, 12, col); c.set(27, 14, col); },                    // 横扫：一道长横扫
  rain: (c, col) => { stroke(c, 20, 7, -70, 4, 1, col); stroke(c, 23, 12, -70, 4, 1, col);
                      stroke(c, 21, 17, -70, 4, 1, col); },                                                // 夜雨：三滴斜雨
  // —— 铁枪诀（枪门，青灰）
  thrust: (c, col) => { c.hLine(20, 13, 8, col); c.set(19, 12, col); c.set(19, 13, col); c.set(19, 14, col); }, // 直刺：一根平刺的枪
  turnback: (c, col) => { c.vLine(25, 6, 6, col); c.hLine(21, 11, 5, col); c.set(20, 10, col); c.set(20, 12, col); }, // 回马：回头的枪
  pierce: (c, col) => { c.frame(20, 9, 5, 5, col); stroke(c, 19, 5, -45, 13, 1, col); },                      // 穿心：一环被一枪斜穿（横着穿会读成风扇）
  banner: (c, col) => { c.vLine(23, 5, 13, col); c.rect(24, 5, 4, 2, col);
                        c.rect(24, 8, 3, 2, col); c.set(24, 10, col); },                                    // 定军：一面残旗
  // —— 烈火掌（拳门，火把橙）
  flame1: (c, col) => { c.rect(21, 10, 4, 5, col); c.rect(22, 7, 2, 3, col); c.set(22, 6, col); },            // 引火：一簇火苗
  flame3: (c, col) => { c.rect(19, 12, 2, 3, col); c.set(19, 11, col); c.rect(23, 8, 2, 3, col);
                        c.set(23, 7, col); c.rect(26, 14, 2, 3, col); c.set(26, 13, col); },                 // 燎原：三簇火
  flameHeart: (c, col) => { c.frame(20, 9, 6, 6, col); c.rect(22, 11, 2, 3, col); c.set(22, 10, col); },      // 焚心：火在环里
  // —— 通用／敌人
  chain: (c, col) => { c.frame(19, 6, 5, 5, col); c.frame(23, 11, 5, 5, col); },                              // 连环腿：两环相扣
  breakLine: (c, col) => { c.hLine(22, 13, 4, col); c.set(26, 12, col); c.set(27, 14, col); },                // 破军式：一道被撕开的阵线
  spark: (c, col) => { c.vLine(23, 10, 5, col); c.hLine(20, 13, 7, col);
                       c.set(21, 11, col); c.set(25, 11, col); c.set(21, 15, col); c.set(25, 15, col); },   // 冷箭：一道冷星
};

/** 只出剪影，不上色也不套底部——用来单独判"形体读不读得出"（明暗会盖掉形体的问题）。 */
function maskOf(draw) {
  const m = new Canvas(S, S);
  draw(m, MASK);
  return m;
}

// ---------------------------------------------------------------- 上色：剪影 → 描边 ＋ 三阶明暗 ＋ 抖动

/**
 * 把 mask 画成"有体积"的图标：受光在左上（`(x-1,y-1)` 在形体之外＝这一条是亮边），
 * 背光在右下，中间用**半阶**把两档接上；外面再包一圈墨。
 *
 * 分档是**按"离形体边缘几步"**来的，每档都是**实心块**：
 * 亮边 1px → 半阶 1px → 中间平色 → 半阶 1px → 暗边 1px。
 *
 * 前一版这里用的是**棋盘抖动**过渡，在 5px 宽的刃上等于整个刃都在抖——形体被抖成了一团毛
 * （用户 2026-10-11"没有剑的形状"有**一半的原因在颜色、不在构图**；四种上色并排比过，
 * 抖的那版最难认）。参考图那把剑本身也是一整块平色，抖动只落在**大色块的边缘**；
 * 32px 的图标没有"大色块"，所以这里**一格抖动都不要**。细笔画（气劲／水纹）另算。
 */
function paint(mask, pal) {
  const out = new Canvas(S, S);
  const solid = (x, y) => x >= 0 && y >= 0 && x < S && y < S && mask.get(x, y)[3] > 0;
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      if (!solid(x, y)) {
        // 描边：只给"贴着形体的空位"上墨（外面那圈方格底留着）
        if (solid(x - 1, y) || solid(x + 1, y) || solid(x, y - 1) || solid(x, y + 1)) {
          out.set(x, y, hex(INK));
        }
        continue;
      }
      let color = pal.body;
      if (!solid(x - 1, y - 1)) color = pal.lit;                        // 亮边
      else if (!solid(x + 1, y + 1)) color = pal.dark;                  // 暗边
      else if (!solid(x - 2, y - 2)) color = mix(pal.lit, pal.body, 0.5);   // 亮侧半阶
      else if (!solid(x + 2, y + 2)) color = mix(pal.body, pal.dark, 0.5);  // 暗侧半阶
      out.set(x, y, hex(color));
    }
  }
  return out;
}

// ---------------------------------------------------------------- 九个样例（玄微剑法 8 ＋ 一指）

/**
 * 每个招式一条：`[id, 学派, 星阶, 画剪影的函数]`。
 * **姿态本身承担区分度**（用户 2026-10-11 的意见：同色之间太像）——
 * 起手斜出、撩云上挑、穿云竖劈、回风带旋、点星指天、分水斜下、落雁下劈、长歌移步，
 * 再加一处与招式名对应的"势"。
 */
/**
 * 五元组：`[id, 学派, 星阶, 画剑的函数, 画"势"的函数]`。**剑与"势"必须分开写**——
 * 两张 mask 分头画、`checkSample()` 量它们的间距，粘在一起就当场报错。
 *
 * 为什么拆开（用户 2026-10-11）："'势'和剑连在一起，保证剑和 icon 中其他效果有间隔。"
 * 连在一起之后那些笔画就不是"势"了，是**长在剑上的赘肉**——看起来像剑断了、或者多了个怪东西。
 * 位置一律先看空位图再落笔：`node tools\artgen\tile.js --gaps` 会把每张图的间距打出来。
 *
 * ⚠️ **全套共用同一把剑**（用户 2026-10-11 拍板："剑的位置、方向、大小都保持一致，其他的
 * '势'等放在右边或下边或右下"）。所以九张之间**只差"势"**——剑一律 `CANON_SWORD`：
 * 斜向右 45°、柄头 `(5,22)`、全长 26，**一个字都不许改**。理由有两条：
 *   ① 45° 与 90° 是栅格化上唯二"每一步宽度都一样"的角度，其余角度刃边一宽一窄，
 *      会把"剑身要直"抵消掉（上一轮已定：只留 45°）；
 *   ② 同一支武学列表里剑忽大忽小、忽左忽右，看着就像没做完（用户原话："怎么做着做着剑的
 *      大小不一样了"）——那一半是我把 45° 的宽度忘了按角度补，见 `sword()` 里的 `k`。
 * **2026-10-11 深夜改竖着摆了**（用户："直着看着对了，斜着 45° 显示的就不是三角形了"）：
 * 45° 上剑尖只能是个"一边竖直的直角三角"（几何决定的，见 `sword()` 里 `uRow` 那段），
 * **立起来（90°）剑尖才是屏幕上真正的对称三角**——用户最终就是按这条拍的。柄头 `(16,29)`、
 * 全长 26 → 剑尖在 `y=4`、护手横在 `y=23–24`（`x=10–21`）。于是"势"的落点变成两处：
 *   **右侧那一竖条**（`x ≥ 20`，`y 4–22`——剑身左缘 `x=14` 给了 6px 余量）与
 *   **护手下面那一角**（`x ≥ 24`，`y 25–28`；贴着护手右端 `x=21` 会只剩 3px，起手从 24 开始）。
 * 用户的原话是"'势'等放在右边或下边或右下"，竖摆之后正好还是这三处，不用改口径。
 */

/**
 * 全套招式共用的一把剑：位置／方向／大小都是定值，改这里等于全套一起改。
 * **`x0 = 12.5` 是半格**（剑轴落在 `x = 12`，即整格中心）——这是全篇唯一能出 1px 剑尖的
 * 开关，理由与比对图见 `SWORD` 上头那条 ⚠️。
 * 位置是用户两轮推出来的：第十一轮"剑位置靠左"落到 `13.5`（左移 2.5px，见
 * `left_variants.png` 那张四选一），第十三轮"武器再向左一点点"再左一格 → **`12.5`**：
 * 刃占 `x=11–13`、护手占 `x=7–17`、左边留 4 格底色，右边空出 10 格给"势"。
 * ⚠️ **只能按 1px（半格）挪**——挪到整数（`12`）就把剑轴从格心挪回格缝，`blade` 会当场
 * 从 3px 胖回 4px、1px 剑尖同时消失。再往左也一样：`11.5` 可行，`11` 不行。
 */
const CANON_SWORD = (c, col) => sword(c, 12.5, 29, 90, 26, col);

/**
 * "势"的落点也**跟着剑一起定死**（竖摆后把剪影打成 ASCII 量出来的，`_map` 那一步）。
 * 剑在 `x0 = 12.5`：刃占 `x=11–13, y=5–22`、护手占 `x=7–17, y=23–24`、柄 `x=12`、
 * 柄头 `x=11–13, y=28`。所以能落笔的是**两块**（用户口径"右边或下边或右下"）：
 *
 * | 区域 | 可用范围 | 边界怎么来的 |
 * |---|---|---|
 * | 右侧竖条 | `x = 18–27, y = 4–22` | 左＝刃右缘 14 ＋ 切比雪夫 3；右＝描边落在 28，离色板边还有 1 格 |
 * | 护手右下 | `x = 22–26, y = 24–28` | 左＝护手右端 18 ＋ 3（`y≥24` 时护手还在） |
 *
 * 四条纪律：
 *   ① **形状要有 6px 以上**，不然 1x 下就是几个看不清的浮点（上一版"势"全挤在 `y ≥ 27` 的
 *      边线上、又短又小，看着像图破了）；
 *   ② **别贴边**：`buildOne()` 会把 `x>29|y>29` 清掉，画到那儿等于自己剪自己；
 *      描边画在形体外一圈，所以**形体的最远格是 `x=27`／`y=22`**（描边落在 28，仍压在色板上；
 *      色板是 `x=2–29`，再往外那圈米色细边在 `x=30`，`buildOne()` 从 30 起清空）；
 *   ③ **每一处都要能说出一句"这是什么"**——认不出来就等于噪音；
 *   ④ **"上色宽度"这一关**（第十轮修第 4／6／8 张时攒出来的）：`paint()` 给每道 1px 笔画
 *      包一圈墨描边，**视觉宽度＝3px**，所以**平行笔画中心距 ≥ 4px、同心弧半径差 ≥ 4.5px**，
 *      比这更密就并成一片网点（第 4 张给 5／7、第 8 张给 4／6／8 时就是那个下场）。
 *      右侧竖条只有 9px 宽，**最多并排两道**互不相连的平行笔画。
 */
const ACTIVES = [
  // 起手：右侧两道残影斜线（45°，一长一短，像刚挥出去的两道影子）
  ["sk_xuanwei_01", "xuanwei", 1,
    CANON_SWORD,
    (c, col) => { stroke(c, 21, 11, 45, 7, 1, col); stroke(c, 22, 19, 45, 5, 1, col); }],
  // 撩云：右边两道云勾（横线右端向上勾起，一长一短错开）
  ["sk_xuanwei_02", "xuanwei", 1,
    CANON_SWORD,
    (c, col) => { c.hLine(21, 8, 5, col); c.set(26, 7, col); c.set(26, 6, col);
                  c.hLine(22, 14, 4, col); c.set(26, 13, col); }],
  // 穿云式：右侧三道云线，越往下越长（剑从云里穿出去）
  ["sk_xuanwei_03", "xuanwei", 2,
    CANON_SWORD,
    (c, col) => { c.hLine(23, 6, 4, col); c.hLine(21, 12, 6, col); c.hLine(20, 18, 7, col); }],
  // 回风：右侧两道同心底弧，开口朝剑（绕着剑走一圈回旋）。圆心 `(26,12)`＝竖条右缘，
  // 半径 3.5／8 正好铺满 `x=18–26`。
  // **半径差 ≥ 4.5**：每道 1px 笔画上色时会被描边撑到 3px，差 2px 的两道会连成一片麻花
  // （第一版给 5／7 就是这样，16× 下一放大全是网点）。弧只取 115°–245° 这一段：
  // 取满 90°–270° 时两端贴着水平走，最右那几格会顶到格子边、被 `buildOne()` 剪掉，
  // 看着像被人削平了。
  ["sk_xuanwei_04", "xuanwei", 2,
    CANON_SWORD,
    (c, col) => { arc(c, 26, 12, 3.5, 115, 245, col); arc(c, 26, 12, 8, 115, 245, col); }],
  // 点星：右侧一颗星（四臂加四个内点，别画成加号）
  ["sk_xuanwei_05", "xuanwei", 3,
    CANON_SWORD,
    (c, col) => {
      const sx = 23, sy = 12;
      c.vLine(sx, sy - 3, 7, col); c.hLine(sx - 3, sy, 7, col);
      c.set(sx - 1, sy - 1, col); c.set(sx + 1, sy - 1, col);
      c.set(sx - 1, sy + 1, col); c.set(sx + 1, sy + 1, col);
    }],
  // 分水：护手右下一道水浪（两级台阶）＋ 一颗水珠。
  // **这里放不下第二道水纹**：护手到 y=24 就没了，往下只剩 y=24–28 五行，3px 的描边一撑，
  // 两道就并成一块砖（第一版那三条横线就是这样）。阶梯往右上走而不是左上：护手右端在
  // `x=21,y=23–24`，往左上蹭到切比雪夫距离 1px，`--gaps` 会当场拦下。
  ["sk_xuanwei_06", "xuanwei", 3,
    CANON_SWORD,
    (c, col) => { c.hLine(22, 25, 4, col); c.hLine(23, 24, 4, col); c.set(26, 21, col); }],
  // 落雁：右侧两群雁（一大一小两个 V，小的在前＝远）
  ["sk_xuanwei_07", "xuanwei", 4,
    CANON_SWORD,
    (c, col) => { c.set(23, 9, col); c.set(24, 10, col); c.set(25, 9, col);
                  c.set(21, 15, col); c.set(22, 16, col); c.set(23, 17, col);
                  c.set(24, 16, col); c.set(25, 15, col); }],
  // 长歌：三道音波，**圆心压在剑轴上**（`(13,13)` 就是剑轴中线），±45° 的一段弧，
  // 像声从刃上荡开。与第 4 张分开：那是贴着右缘的整圈"气环"，这三道是绕着刃的扇形波——
  // 同一族里换的是圆心，不是半径档位。剑往左挪 2.5px 之后右侧够宽了，才排得下第三道
  // （半径差 4px 一道，正好 18／22／26）。
  ["sk_xuanwei_08", "xuanwei", 5,
    CANON_SWORD,
    (c, col) => { arc(c, 13, 13, 6, -40, 40, col); arc(c, 13, 13, 10, -40, 40, col);
                  arc(c, 13, 13, 14, -40, 40, col); }],
  // 心法·一指：**剑与其余八张完全同一把**——`skill_base.csv` 这一行的 `weapon_type = sword`
  // （`skills.js` 那套已交付的旧图也是按 weapon_type 分的，它本来就算在"剑 12"里）。
  // ⚠️ 我上一轮按 desc 里"内功系指法"四个字给它画了只手（"不用兵器"），**那是错的**：
  // 剪影（一根细长条 ＋ 底下一块）当场被读成"一把怪剑"，用户 2026-10-11 直接问
  // "第九张的剑为什么和其他的剑不一样"。**别再看 desc 猜，看 `weapon_type` 那一列。**
  // "势"用**一枚气穴环点**（4×4 的环 ＋ 2×2 的心）：指劲点到一处，与其余八张的势都不撞。
  ["sk_xuanwei_qi_01", "xuanwei", 2,
    CANON_SWORD,
    (c, col) => { c.frame(22, 10, 4, 4, col); c.rect(23, 11, 2, 2, col); }],
  // —— 醉里乾坤（剑门）：同一个学派三张，兵器必须是同一把、只有"势"不同
  ["sk_drunk_zuibu", "zuidj", 3, CANON_SWORD, MARKS.step],
  ["sk_drunk_jiuzhongdao", "zuidj", 4, CANON_SWORD, MARKS.drop],
  ["sk_drunk_01", "zuidj", 5, CANON_SWORD, MARKS.turn],
  // —— 沉沙掌（拳门）
  ["sk_chensha_01", "chensha", 1, palm, MARKS.push],
  ["sk_chensha_02", "chensha", 2, palm, MARKS.crack],
  ["sk_chensha_03", "chensha", 3, palm, MARKS.river],
  ["sk_chensha_04", "chensha", 4, palm, MARKS.feather],
  // —— 五毒（拳门）
  ["sk_wudu_01", "wudu", 1, palm, MARKS.scatter],
  ["sk_wudu_02", "wudu", 2, palm, MARKS.haze],
  ["sk_wudu_03", "wudu", 3, palm, MARKS.bone],
  ["sk_wudu_04", "wudu", 4, palm, MARKS.blood],
  // —— 黑风刀法（刀门）
  ["sk_bandit_slash", "heifeng", 1, saber, MARKS.slash],
  ["sk_bandit_scout", "heifeng", 2, saber, MARKS.mountain],
  ["sk_boss_zhangfeng", "heifeng", 3, saber, MARKS.cutwind],
  ["sk_boss_hengsao", "heifeng", 3, saber, MARKS.sweep],
  ["sk_heifeng_01", "heifeng", 4, saber, MARKS.rain],
  // —— 铁枪诀（枪门）
  ["sk_spear_01", "tieqiang", 1, spear, MARKS.thrust],
  ["sk_spear_02", "tieqiang", 2, spear, MARKS.turnback],
  ["sk_spear_03", "tieqiang", 3, spear, MARKS.pierce],
  ["sk_spear_04", "tieqiang", 4, spear, MARKS.banner],
  // —— 烈火掌（拳门）
  ["sk_liehuo_01", "liehuo", 2, palm, MARKS.flame1],
  ["sk_liehuo_02", "liehuo", 3, palm, MARKS.flame3],
  ["sk_liehuo_03", "liehuo", 4, palm, MARKS.flameHeart],
  // —— 通用／敌人
  ["sk_common_01", "common", 1, boot, MARKS.chain],
  ["sk_common_02", "common", 3, crescent, MARKS.breakLine],
  ["sk_bandit_arrow", "bandit", 1, arrow, MARKS.spark],
];

/**
 * 玄微那 9 张＝`ACTIVES` 里 `school_id = xuanwei` 的那些——**不要在下面另立一份**：
 * 出样图、跑间距门限、对表都从 `ACTIVES` 这一处走，两份清单迟早会漂。
 */
const SAMPLE = ACTIVES.filter((r) => r[1] === "xuanwei");

/** "势"与剑之间最少留几格（切比雪夫距离；3 ＝ 中间至少有两个空像素）。 */
const MIN_GAP = 3;

/** 两张 mask 的最小间距：切比雪夫距离（斜着挨着也算 1）。任一为空返回 Infinity。 */
function gap(a, b) {
  const pa = [], pb = [];
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      if (a.get(x, y)[3] > 0) pa.push([x, y]);
      if (b.get(x, y)[3] > 0) pb.push([x, y]);
    }
  }
  if (!pa.length || !pb.length) return Infinity;
  let best = Infinity;
  for (const [x1, y1] of pa) {
    for (const [x2, y2] of pb) {
      const d = Math.max(Math.abs(x1 - x2), Math.abs(y1 - y2));
      if (d < best) best = d;
    }
  }
  return best;
}

/**
 * 逐张量"兵器 ↔ 势"的间距，**不够就抛**（不是打印警告——粘在一起是会被用户一眼看见的问题，
 * 得在出图这一环挡住）。返回 `[{id, gap}]` 供 CLI 打印。
 */
function checkList(list) {
  const rows = [];
  for (const [id, , , pose, mark] of list) {
    if (!mark) { rows.push({ id, gap: Infinity }); continue; }
    const g = gap(maskOf(pose), maskOf(mark));
    if (g < MIN_GAP) {
      throw new Error(`${id}：「势」离兵器只有 ${g}px（要求 ≥${MIN_GAP}）——挪到空位去，别贴着画`);
    }
    rows.push({ id, gap: g });
  }
  return rows;
}

/** 玄微 9 张（样图那一批）的间距。 */
function checkSample() {
  return checkList(SAMPLE);
}

/** 招式 35 张（`skill_base` 里 `sk_*` 的全部出招）的间距。 */
function checkActives() {
  return checkList(ACTIVES);
}

/**
 * 造一张：方格子 ＋ 上色后的剪影。`pose` 画剑、`mark` 画"势"，**两张分头画再合成一张**
 * （合成之后一起上色，交界处的明暗才不会露馅）。
 */
function buildOne(id, school, star, pose, mark = null) {
  const colors = SCHOOL_COLORS[school];
  const mask = new Canvas(S, S);
  for (const part of mark ? [pose, mark] : [pose]) {
    const layer = maskOf(part);
    for (let y = 0; y < S; y++) {
      for (let x = 0; x < S; x++) if (layer.get(x, y)[3] > 0) mask.set(x, y, MASK);
    }
  }
  const body = paint(mask, palette(school, star));
  // 剪影与它那圈墨描边**一律不许出格**：格子的米色细边与最外那圈墨是"卡片"的边框，
  // 被笔画盖掉之后那张图看起来就像破了角（2026-10-11 第一版：起手式左下那道开手弧
  // 正好压在角上）。留着边界不画，比事后逐张去修便宜。
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      if (x < 2 || y < 2 || x > S - 3 || y > S - 3) body.set(x, y, HOLE);
    }
  }
  const c = tileFrame(colors);
  c.blit(body, 0, 0);
  return c;
}

function buildSample() {
  const out = {};
  for (const [id, school, star, pose, mark] of SAMPLE) out[id] = buildOne(id, school, star, pose, mark);
  return out;
}

/** 招式 35 张全量（`assets/icons/skill/` 里 `sk_*` 那 35 张的成品）。 */
function buildActives() {
  const out = {};
  for (const [id, school, star, pose, mark] of ACTIVES) out[id] = buildOne(id, school, star, pose, mark);
  return out;
}

/**
 * 出图这一环的**对表门限**：`ACTIVES` 的 35 个 id 必须与 `skill_base.csv` 里
 * `skill_kind = active` 的 35 行**逐个对上**（多一个少一个都抛）。
 * Why：`skills.js` 那条注释里的原话——"**id 一位不能差**（程序按 `skill_id` 找图）"，
 * 而这份清单是我手写的，正是最容易打错一位的地方。
 */
function checkIds(csvPath = path.join(__dirname, "../../data/tables/skill_base.csv")) {
  const fs = require("fs");
  const want = fs
    .readFileSync(csvPath, "utf8")
    .trim()
    .split(/\r?\n/)
    .slice(1)
    .map((l) => l.split(","))
    .filter((r) => r[2] === "active")
    .map((r) => r[0]);
  const got = ACTIVES.map((r) => r[0]);
  const missing = want.filter((id) => !got.includes(id));
  const extra = got.filter((id) => !want.includes(id));
  if (missing.length || extra.length) {
    throw new Error(
      `对表失败：表里 active ${want.length} 行 / ACTIVES ${got.length} 行` +
        `${missing.length ? `；缺 ${missing.join("／")}` : ""}${extra.length ? `；多 ${extra.join("／")}` : ""}`,
    );
  }
  return got.length;
}

module.exports = {
  buildSample, buildActives, buildOne, tileFrame, paint, palette, maskOf, checkIds,
  // 画法原语也导出去：`maskOf` ＋ 这几个就能在不着色的情况下单独试形体（见交接单"判形体"一节）
  sword, saber, spear, palm, boot, crescent, arrow, MARKS,
  bar, arc, stroke, gap, checkSample, checkActives, SWORD, SAMPLE, ACTIVES, MIN_GAP, INK, BEVEL,
};

// ---------------------------------------------------------------- CLI：出样（不碰仓库里的图）

if (require.main === module) {
  const { decode, at } = require("./pngread");
  const arg = (n, d) => {
    const i = process.argv.indexOf("--" + n);
    return i >= 0 ? process.argv[i + 1] : d;
  };
  const Z = parseInt(arg("zoom", "6"), 10);
  const outDir = arg("out");
  const preview = arg("preview");
  // `--all` ＝ 招式 35 张全量（会先对表、再跑间距门限）；不带就是玄微那 9 张样图。
  const all = process.argv.includes("--all");
  // 出图前先过门限：不通过直接抛，免得留下一批"粘着势"的图（`--gaps` 只跑到门限为止）
  const rows = all ? checkActives() : checkSample();
  console.log(
    `兵器 ↔ 势 间距（门限 ≥${MIN_GAP}px，${rows.length} 张）：` +
      rows.map((r) => `${r.id} ${r.gap === Infinity ? "—" : r.gap + "px"}`).join(" · "),
  );
  if (all) console.log(`对表：ACTIVES 与 skill_base 的 active 行一一对上（${checkIds()} 张）`);
  if (process.argv.includes("--gaps")) process.exit(0);
  const built = all ? buildActives() : buildSample();
  const ids = Object.keys(built);
  if (outDir) {
    const fs = require("fs");
    fs.mkdirSync(outDir, { recursive: true });
    for (const id of ids) fs.writeFileSync(path.join(outDir, id + ".png"), encodePNG(built[id]));
    console.log(`写出 ${ids.length} 张 → ${outDir}`);
  }
  if (all && process.argv.includes("--write")) {
    // 落进游戏：`assets/icons/skill/<skill_id>.png`（文件名＝表里的 `skill_id`）
    const fs = require("fs");
    const dir = path.join(__dirname, "../../assets/icons/skill");
    fs.mkdirSync(dir, { recursive: true });
    for (const id of ids) fs.writeFileSync(path.join(dir, id + ".png"), encodePNG(built[id]));
    console.log(`写出 ${ids.length} 张 → ${dir}（旧图在 git 里，可随时回滚）`);
  }
  if (all && arg("sheet")) {
    // 看板：5 列 × 7 行的整族对照图（左边第一列就是玄微那 9 张）
    const cols = 5, pad = 2, cell = S * Z;
    const rowsN = Math.ceil(ids.length / cols);
    const sheet = new Canvas(cols * (cell + pad) + pad, rowsN * (cell + pad) + pad);
    sheet.fill([0xf4, 0xec, 0xd9, 255]);
    ids.forEach((id, i) => {
      const col = i % cols, row = (i - col) / cols;
      sheet.blit(built[id].scaled(Z), pad + col * (cell + pad), pad + row * (cell + pad));
    });
    require("fs").writeFileSync(arg("sheet"), encodePNG(sheet));
    console.log(`写出整族看板 ${arg("sheet")}（${cols}×${rowsN}，${Z}×）`);
  }
  if (preview) {
    // 上排＝现在游戏里那张（旧语法），下排＝方底新版，一眼能比
    const sheet = new Canvas(ids.length * S * Z, 2 * S * Z);
    sheet.fill([0xf4, 0xec, 0xd9, 255]);
    ids.forEach((id, i) => {
      const old = decode(path.join(__dirname, "../../assets/icons/skill", id + ".png"));
      const c = new Canvas(S, S);
      for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) c.set(x, y, at(old, x, y));
      sheet.blit(c.scaled(Z), i * S * Z, 0);
      sheet.blit(built[id].scaled(Z), i * S * Z, S * Z);
    });
    require("fs").writeFileSync(preview, encodePNG(sheet));
    console.log(`写出对比图 ${preview}（上排＝旧，下排＝方底新版）`);
  }
}
