// 武学图标：★4 内功一档（0.31.0 的「本命机遇」五部）。
//
// 15 §4.3 的构图纪律：**同一类别内构图取齐**（内功类都**正面居中**），
// 否则在武学列表里一眼就看出是拼的。这五部同档（★4）同类（内功）——
// 要求原话是「它们**彼此**要像一套，别各画一个风格」。
//
// 所以这里先定一套**内功语法**，五张只是换「色 ＋ 中心母题」：
//   底：与其它图标同一块压暗圆底（列表里排一行时不打架）
//   中：一圈「气环」（同粗细、同半径）——这一笔是「我们是同一套」的信号
//   心：正面居中的母题（剑意／掌劲／毒／影／药），每部一个
//   底：丹田上的一点「息」（同一个位置、同一个形状）
//
// **文件名 = `skill_base.skill_id`，一位不能差**（15 §六：程序按 id 找图）。
// 第一版按 `pf_<school>_NN` 的序号规律猜了名字（`pf_xuanwei_06` 等），而那 5 部
// **本命内功**在表里根本不带序号（`pf_xuanwei_zhbai`），于是图静静地不显示——
// 2026-10-04 由 `test_map_assets._check_icon_file_names` 抓出来，本文件同步改名。
// 教训写在这儿：`pf_<school>_NN` 只对「门派主线的第 N 部」成立，
// **本命／机遇那类额外条目一律带名字后缀**，不能再按序号推。

"use strict";

const { Canvas, hex, mix, shade, noise } = require("./pixel");

const S = 32;
const INK = "#1E2224";

/** 内功图标底板：一圈压暗（与前两批图标同一块），保证列表里底色一致。 */
function plate(tone = "#1E2428", alpha = 0.68) {
  const c = new Canvas(S, S);
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      const d = Math.hypot(x - 15.5, y - 15.5);
      if (d <= 15) c.blend(x, y, hex(tone), alpha * (d <= 13 ? 1 : 1 - (d - 13) / 2));
    }
  }
  return c;
}

/** 气环：五张共用的一笔（半径、粗细、断口位置全一致）。 */
function qiRing(c, color, inner) {
  for (let a = 0; a < 360; a += 3) {
    if (a > 200 && a < 250) continue; // 一个统一的断口，作为「同一套」的签名
    const rad = (a * Math.PI) / 180;
    const x = 15.5 + Math.cos(rad) * (inner ? 9.5 : 10.5);
    const y = 15.5 + Math.sin(rad) * (inner ? 9.5 : 10.5);
    c.set(x, y, hex(inner ? shade(color, 0.2) : color));
  }
}

/** 丹田那一点「息」：五张同一个位置、同一个形状。 */
function breathMark(c, color) {
  c.rect(14, 25, 4, 1, hex(color));
  c.rect(15, 23, 2, 2, hex(shade(color, 0.25)));
}

function finish(c, accent) {
  c.outline(hex(INK));
  const src = c.clone();
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      if (src.get(x, y)[3] < 200) continue;
      if (src.get(x + 1, y)[3] < 100 || src.get(x, y + 1)[3] < 100) {
        c.set(x, y, hex(mix(accent, INK, 0.3)));
      }
    }
  }
  return c;
}

// ---------------------------------------------------------------- 五部内功

/** 玄微心法·知白（书生）：**剑意 ＋ 白气**。知白守黑——整张以白为主。 */
function xuanweiZhibai() {
  const c = plate("#1A2026", 0.7);
  qiRing(c, "#7A8A9A", false);
  qiRing(c, "#C6D0D8", true);
  // 居中的剑：剑尖朝上，刃上一道高光
  c.rect(15, 6, 2, 16, hex("#D8E0E8"));
  c.rect(15, 6, 1, 16, hex("#F0F6FA"));
  c.rect(13, 20, 6, 2, hex("#8A94A0"));
  c.rect(14, 22, 4, 3, hex("#5E6A70"));
  c.set(16, 4, hex("#F0F6FA"));
  breathMark(c, "#B0BAC2");
  return finish(c, "#C6D0D8");
}

/** 沉沙心法·不还（镖局）：**掌劲**。一掌推出、劲力外散（赭石／砂黄）。 */
function chenshaBuhuan() {
  const c = plate("#221E18", 0.7);
  qiRing(c, "#8A6A46", false);
  qiRing(c, "#C8A24A", true);
  // 掌心（正面居中）：掌心三点 + 四道劲线
  c.ellipse(15.5, 15.5, 5, 5, hex("#A8763A"));
  c.ellipse(15.5, 15.5, 3, 3, hex("#C8A24A"));
  c.set(14, 14, hex("#E0C070"));
  c.set(17, 17, hex("#7A5A28"));
  for (const [dx, dy] of [[0, -1], [1, 0], [0, 1], [-1, 0]]) {
    for (let k = 6; k < 9; k++) c.set(15.5 + dx * k, 15.5 + dy * k, hex("#C8A24A"));
  }
  breathMark(c, "#C8A24A");
  return finish(c, "#C8A24A");
}

/** 五毒心法·还毒（药王谷）：**毒**。绿（15 §二：绿色同理＝毒）。 */
function wuduHuandu() {
  const c = plate("#18201A", 0.7);
  qiRing(c, "#4A7A46", false);
  qiRing(c, "#6E9E5E", true);
  // 一滴毒 + 上升的毒气
  c.ellipse(15.5, 18, 4, 5, hex("#4A7A46"));
  c.rect(13, 16, 5, 4, hex("#4A7A46"));
  c.ellipse(14, 16.5, 2, 2, hex("#6E9E5E"));
  c.set(15, 14, hex("#8ABE72"));
  for (const [x, y] of [[13, 10], [16, 8], [18, 11]]) c.rect(x, y, 2, 1, hex("#8ABE72"));
  breathMark(c, "#6E9E5E");
  return finish(c, "#6E9E5E");
}

/** 青骡心法·影（绿林）：**影**。一把短刃 + 身后的残影。 */
function qingluoYing() {
  const c = plate("#161A1E", 0.74);
  qiRing(c, "#3E4A56", false);
  qiRing(c, "#5E6E7A", true);
  // 短刃：右手握、斜向（刃朝右上）。先画残影（往左下错开的暗副本），再画本体
  const dagger = (ox, oy, body, edge, hilt) => {
    for (let i = 0; i < 11; i++) {
      const x = 12 + i + ox;
      const y = 18 - i + oy;
      c.set(x, y, hex(body));
      c.set(x, y + 1, hex(body));
      c.set(x + 1, y, hex(edge));
    }
    c.rect(10 + ox, 16 + oy, 5, 2, hex(hilt)); // 护手
    c.rect(9 + ox, 18 + oy, 3, 4, hex(hilt));  // 握柄
    c.set(8 + ox, 21 + oy, hex(edge));         // 柄头
  };
  dagger(-3, 2, "#2E3842", "#3E4A56", "#333B42"); // 残影
  dagger(0, 0, "#8A94A0", "#C6D0D8", "#4A3A2C");  // 本体
  c.set(22, 7, hex("#F0F6FA"));                    // 刃尖高光
  breathMark(c, "#5E6E7A");
  return finish(c, "#5E6E7A");
}

/** 调息诀·回春（游医）：**药 ＋ 息**。一叶草药与一圈回气（青绿／宣纸白）。 */
function tiaoxiHuichun() {
  const c = plate("#182220", 0.7);
  qiRing(c, "#3E6E5A", false);
  qiRing(c, "#5E9E7E", true);
  // 一片叶子要「是叶子」：整片填肉 + 中脉 + 叶尖 + 叶柄，别拿细线凑
  for (let y = 7; y <= 23; y++) {
    const t = (y - 7) / 16;
    const half = Math.round(Math.sin(t * Math.PI) * 6);
    c.rect(16 - half, y, half * 2, 1, hex("#4E8A62"));
    c.set(16 - half, y, hex("#3E6E5A"));
    c.set(16 + half - 1, y, hex("#3E6E5A"));
  }
  for (let y = 8; y <= 22; y++) c.set(15, y, hex("#2E5A46")); // 中脉
  for (let i = 0; i < 4; i++) c.set(15 - 2 - i, 12 + i, hex("#3E6E5A"));
  c.rect(13, 6, 7, 1, hex("#8ABE9E"));                        // 叶尖的高光
  c.rect(15, 24, 2, 3, hex("#4E8A62"));                       // 叶柄
  c.set(14, 10, hex("#8ABE9E"));
  breathMark(c, "#8ABE9E");
  return finish(c, "#5E9E7E");
}

/**
 * 相对 `assets/` 的路径 → 画布。
 * id 按 `pf_<school>_NN` 的既有规律推：玄微／沉沙／五毒那三个 school 表里已有，
 * `qingluo`／`tiaoxi` 是我按门派推的（表里还没有这两个 school）。
 */
function buildAll() {
  const out = {
    // 手绘的 5 部本命内功（21 号 §九）——保留原样，它们是这一套的样板
    "icons/skill/pf_xuanwei_zhbai.png": xuanweiZhibai(),
    "icons/skill/pf_chensha_buhuan.png": chenshaBuhuan(),
    "icons/skill/pf_wudu_huandu.png": wuduHuandu(),
    "icons/skill/pf_qingluo_ying.png": qingluoYing(),
    "icons/skill/pf_tiaoxi_huichun.png": tiaoxiHuichun(),
  };
  // 另 25 部按「学派色 ＋ 星阶气环 ＋ 母题」的同一套语法生成
  for (const [id, school, star, draw] of MOTIFS) {
    out[`icons/skill/${id}.png`] = innerIcon(school, star, draw);
  }
  // 招式 35 张：内功是「正面居中」，招式是「斜 45°」——两类共用同一块压暗圆底与同一支勾边笔
  for (const [id, school, star, shape, mark] of ACTIVES) {
    out[`icons/skill/${id}.png`] = activeIcon(id, school, star, shape, mark);
  }
  return out;
}

// ---------------------------------------------------------------- 内功 30 张：学派色 ＋ 气环档位

/**
 * 12 个学派的色（15 §二：朱红只给血／灯笼／旗帜／敌意，绿色＝毒，金色＝精英）。
 * `yipian` 用**遗篇幽蓝**（与 16 §4.8 石隙石台同一色）——那一部内功本来就从石隙拿，
 * 同色是「一眼看出它出自哪」的线索，不是随手挑的。
 */
const SCHOOL_COLORS = {
  xuanwei: { main: "#7A8A9A", light: "#C6D0D8", plate: "#1A2026" },
  chensha: { main: "#8A6A46", light: "#C8A24A", plate: "#221E18" },
  wudu: { main: "#4A7A46", light: "#8ABE72", plate: "#18201A" },
  // 黑风的 `main` 第一版是深褐木 #6E5236——在 #1E1A16 的底上**几乎看不见**（褐 on 褐）。
  // 16 §4.3 给黑风寨的强调色本来就是**火把橙** #D96A28，改用它当 light，一组三张立刻分得开。
  heifeng: { main: "#A8875C", light: "#D96A28", plate: "#1E1A16" },
  tieqiang: { main: "#7A8A98", light: "#A8B8C4", plate: "#1A2024" },
  yaowang: { main: "#4E8A6E", light: "#8ABE9E", plate: "#182220" },
  taixu: { main: "#6E8A9A", light: "#B6C8D4", plate: "#1A2226" },
  luohan: { main: "#A87A38", light: "#D8B25A", plate: "#201C14" },
  zuidj: { main: "#8A5A28", light: "#D8A24A", plate: "#201A12" },
  yipian: { main: "#4E6E92", light: "#7E9EC8", plate: "#181E28" },
  qingluo: { main: "#3E4A56", light: "#8A9AA8", plate: "#161A1E" },
  tiaoxi: { main: "#3E6E5A", light: "#8ABE9E", plate: "#182220" },
  // 招式才用到的三支。15 §二 那条「朱红只给血／灯笼／旗帜／敌意、绿色＝毒」
  // 决定了它们只能这么取：烈火＝火把橙那一族（16 §4.3），不是朱红；通用＝石黄灰；
  // 冷箭是黑风寨的敌人招，沿用黑风那套色。
  liehuo: { main: "#A8432A", light: "#E8823A", plate: "#221612" },
  common: { main: "#7A7364", light: "#C8BFA8", plate: "#1E1E1C" },
  bandit: { main: "#A8875C", light: "#D96A28", plate: "#1E1A16" },
};

/** 与 `qiRing` 同一支笔，但半径可调——**星阶只改环的圈数与半径，不改画法**。 */
function qiRingAt(c, color, radius) {
  for (let a = 0; a < 360; a += 3) {
    if (a > 200 && a < 250) continue; // 与已有 5 张同一个断口
    const rad = (a * Math.PI) / 180;
    c.set(15.5 + Math.cos(rad) * radius, 15.5 + Math.sin(rad) * radius, hex(color));
  }
}

/**
 * 气环档位：**★1–2 一圈、★3–4 两圈、★5 三圈**。
 * 这条是全类别统一的「骨架」，所以已交付的 5 张（都是 ★4）正好落在「两圈」上——
 * 新补的 25 张照这条对齐，**不是每张各画一个风格**（15 §4.3「同一类别内构图取齐」）。
 */
function rings(c, colors, star) {
  // 外环取 main→light 的中间值：深色学派的 main 直接画环会糊在底上
  qiRingAt(c, mix(colors.main, colors.light, 0.35), 10.5);
  if (star >= 3) qiRingAt(c, colors.light, 9.5);
  if (star >= 5) qiRingAt(c, shade(colors.light, 0.25), 8.5);
}

/** 母题统一画在正中这块 14×14 里，别越界压到气环上。 */
function motif(c, draw, colors, star) {
  const ctx = {
    c, main: colors.main, light: colors.light,
    px: (x, y, col) => c.set(16 + x, 16 + y, hex(col || ctx.main)),
    rect: (x, y, w, h, col) => c.rect(16 + x, 16 + y, w, h, hex(col || ctx.main)),
    line: (x, y, w, col) => c.hLine(16 + x, 16 + y, w, hex(col || ctx.main)),
    vline: (x, y, h, col) => c.vLine(16 + x, 16 + y, h, hex(col || ctx.main)),
    disc: (x, y, r, col) => c.ellipse(16 + x, 16 + y, r, r, hex(col || ctx.main)),
    ring: (x, y, r, col) => c.ellipse(16 + x, 16 + y, r, r, hex(col || ctx.light)),
  };
  draw(ctx, star);
}

// ---------------------------------------------------------------- 25 个母题
// 每个学派一条「物产感」：玄微＝气与剑、五毒＝毒与虫、黑风＝蛮与骨、沉沙＝土石、
// 铁枪＝枪与旗、药王＝丹草、太虚＝涟漪、罗汉＝甲、醉里＝葫芦云、遗篇＝古纹。

function mYinQi(x) { for (let i = 0; i < 3; i++) { const dx = -4 + i * 4; for (let k = 0; k < 7; k++) x.px(dx + (k % 2), -5 + k, k < 2 ? x.light : x.main); } }
function mNingShen(x) { x.line(-4, 0, 8, x.main); for (let i = 0; i < 5; i++) x.px(0, -5 + i, i === 2 ? x.light : x.main); x.disc(0, 2, 1, x.light); }
function mShouYi(x) { x.ring(0, 0, 5); x.ring(0, 0, 3); x.disc(0, 0, 1, x.light); }
function mZhouTian(x) { x.ring(0, 0, 5); for (const a of [20, 110, 200, 290]) { const r = (a * Math.PI) / 180; for (let k = 0; k < 2; k++) x.px(Math.round(Math.cos(r) * (4 + k)), Math.round(Math.sin(r) * (4 + k)), x.light); } }
function mTaiQing(x) { for (let y = -7; y <= 2; y++) { x.px(-3, y, x.main); x.px(3, y, x.main); } x.disc(0, -3, 2, x.light); x.line(-2, 4, 5, x.main); }
function mYinDu(x) { for (const [dx, dy, r] of [[-3, -4, 2], [2, -2, 3], [-1, 2, 2]]) x.disc(dx, dy, r, x.main); for (let i = 0; i < 4; i++) x.px(-4 + i * 3, -7, x.light); }
function mFuJi(x) { x.disc(0, 0, 5, x.main); for (let i = 0; i < 7; i++) x.px(-4 + ((i * 5) % 9), -4 + ((i * 3) % 9), x.plate || "#101610"); x.disc(-2, -2, 2, x.light); }
function mShiMai(x) { x.vline(0, -6, 13, x.main); for (const [dx, dy, h] of [[-3, -3, 4], [3, -3, 4], [-4, 2, 4], [4, 2, 4]]) x.vline(dx, dy, h, x.light); x.line(-6, 0, 13, x.main); }
function mWanGu(x) { for (const [dx, dy] of [[-4, -3], [1, -5], [3, 1], [-2, 3]]) { x.disc(dx, dy, 2, x.main); x.px(dx - 3, dy, x.light); x.px(dx + 3, dy, x.light); } }
function mManLi(x) { x.disc(0, 0, 5, x.main); for (let i = 0; i < 4; i++) x.vline(-3 + i * 2, -4, 3, x.light); x.line(-4, 3, 9, x.light); }
function mTieGu(x) { x.rect(-5, -2, 11, 4, x.main); x.disc(-5, 0, 2, x.main); x.disc(5, 0, 2, x.main); x.line(-3, -3, 7, x.light); x.px(-2, 0, x.light); }
function mHanYong(x) { for (let i = 0; i < 10; i++) x.px(-5 + i, 5 - i, x.light); for (let i = 0; i < 10; i++) x.px(-5 + i, 6 - i, x.main); x.line(3, -6, 3, x.light); x.px(5, -4, x.light); }
function mHouTu(x) { for (const [dy, w] of [[4, 6], [1, 5], [-2, 4]]) { x.line(-w, dy, w * 2, x.main); x.px(-w, dy, x.light); } x.line(-1, -5, 3, x.light); }
function mPanShi(x) { x.rect(-5, -4, 11, 9, x.main); x.rect(-5, -4, 11, 2, x.light); x.line(-3, 3, 7, x.plate || "#161310"); x.px(-4, -3, x.light); }
function mKaiBei(x) { x.rect(-5, -5, 11, 11, x.main); x.rect(-5, -5, 11, 2, x.light); for (let i = 0; i < 6; i++) x.px(0, -5 + i * 2, x.plate || "#161310"); x.px(-1, 0, x.plate || "#161310"); }
function mChangQu(x) { x.vline(0, -6, 12, x.main); for (const [dy, w] of [[-6, 1], [-5, 2], [-4, 3], [-3, 4]]) x.line(-w, dy, w * 2 + 1, x.light); for (let i = 0; i < 3; i++) x.line(-6, 4 + i * 2, 12, x.main); }
function mDingJun(x) { x.vline(-4, -6, 13, x.main); for (const [dy, w] of [[-6, 9], [-5, 9], [-4, 6], [-3, 4]]) x.line(-4, dy, w, x.light); x.disc(3, 3, 2, x.main); }
function mYangSheng(x) { x.disc(0, 1, 4, x.main); x.disc(0, 1, 2, x.light); x.ring(0, -3, 3); }
function mXuMing(x) { x.vline(0, -6, 13, x.main); x.line(-4, -1, 9, x.main); x.line(-3, 3, 7, x.main); x.disc(0, -5, 2, x.light); }
function mBaiCao(x) { for (const [dx, h] of [[-4, 7], [0, 10], [4, 6]]) { x.vline(dx, 4 - h, h, x.main); x.px(dx - 1, 4 - h + 2, x.light); x.px(dx + 1, 4 - h + 3, x.light); } x.line(-5, 5, 11, x.main); }
function mJingXin(x) { for (const r of [6, 4, 2]) x.ring(0, 0, r); x.disc(0, 0, 1, x.light); }
function mTieBuShan(x) { for (let row = 0; row < 3; row++) for (let col = 0; col < 3; col++) { const dx = -4 + col * 4 + (row % 2 ? 2 : 0); x.rect(dx, -5 + row * 4, 3, 3, x.main); x.px(dx, -5 + row * 4, x.light); } }
function mZuiYi(x) { x.disc(0, 2, 3, x.main); x.disc(0, -2, 2, x.main); x.line(-1, -5, 3, x.main); x.disc(0, 2, 1, x.light); x.ring(0, -2, 3); }
function mWangYou(x) { x.disc(-3, 0, 3, x.main); x.disc(1, -2, 3, x.main); x.disc(4, 1, 2, x.main); x.line(-4, 3, 10, x.main); x.line(-1, -4, 4, x.light); }
function mGuiYuan(x) { x.rect(-5, -5, 11, 11, x.main); x.rect(-3, -3, 7, 7, x.plate || "#101620"); x.rect(-1, -1, 3, 3, x.light); x.line(-5, -5, 11, x.light); }

/** id → 学派 / 星阶 / 母题。表里 `pf_*` 一共 30 行，这里 25 行（另 5 行是手绘的本命内功）。 */
const MOTIFS = [
  ["pf_xuanwei_01", "xuanwei", 1, mYinQi],
  ["pf_xuanwei_02", "xuanwei", 1, mNingShen],
  ["pf_xuanwei_03", "xuanwei", 2, mShouYi],
  ["pf_xuanwei_04", "xuanwei", 3, mZhouTian],
  ["pf_xuanwei_05", "xuanwei", 5, mTaiQing],
  ["pf_wudu_01", "wudu", 1, mYinDu],
  ["pf_wudu_02", "wudu", 2, mFuJi],
  ["pf_wudu_03", "wudu", 3, mShiMai],
  ["pf_wudu_04", "wudu", 4, mWanGu],
  ["pf_heifeng_01", "heifeng", 1, mManLi],
  ["pf_heifeng_02", "heifeng", 2, mTieGu],
  ["pf_heifeng_03", "heifeng", 3, mHanYong],
  ["pf_chensha_01", "chensha", 2, mHouTu],
  ["pf_chensha_02", "chensha", 3, mPanShi],
  ["pf_chensha_03", "chensha", 4, mKaiBei],
  ["pf_tieqiang_01", "tieqiang", 2, mChangQu],
  ["pf_tieqiang_02", "tieqiang", 3, mDingJun],
  ["pf_yaowang_01", "yaowang", 1, mYangSheng],
  ["pf_yaowang_02", "yaowang", 3, mXuMing],
  ["pf_yaowang_03", "yaowang", 4, mBaiCao],
  ["pf_taixu_01", "taixu", 2, mJingXin],
  ["pf_luohan_01", "luohan", 3, mTieBuShan],
  ["pf_drunk_01", "zuidj", 4, mZuiYi],
  ["pf_drunk_02", "zuidj", 5, mWangYou],
  ["pf_yipian_01", "yipian", 5, mGuiYuan],
];

/** 按表生成一张内功图标：底板 ＋ 气环（按星阶）＋ 母题 ＋ 丹田一点「息」。 */
function innerIcon(school, star, draw) {
  const colors = SCHOOL_COLORS[school];
  const c = plate(colors.plate, 0.7);
  rings(c, colors, star);
  motif(c, draw, colors, star);
  breathMark(c, colors.light);
  return finish(c, colors.light);
}

// ---------------------------------------------------------------- 招式 35 张：斜 45° 的兵器剪影
//
// 15 §4.3 的同一句纪律换到另一半上：**武器类都斜 45°**（内功是正面居中）。
// 所以这 35 张共用一套**骨架**——同一块压暗圆底、**同一条左下→右上的 45° 轴**、
// **同一个方向的动作残影**、同一支勾边笔（`finish`）；每张只换三样：
// **兵器剪影**（按 `weapon_type` 分五种：剑／刀／枪／掌／杂）＋ **学派色** ＋ **星阶明度**。
//
// 骨架写在 `activeIcon()`／`trail45()` 里：残影**不是每张各画一个**，而是同一段代码沿着
// 同一根轴画出来的三道尾迹。**"看起来像一套"来自这段代码，不来自每张对齐**——
// 这是 README 那两条规矩（同类别的骨架必须一模一样）在招式这边的落法。
//
// 星阶**只改明度**（`starLight`）：15 §4.3 / A15 定的「星阶不画进图标」——
// 同门 ★1→★5 越亮，边框与角标留给列表 UI（32×32 里再塞角标就糊了）。

/** 星阶 → 明度：★1 取学派 main 与 light 之间偏暗处，★5 落在提亮过的 light 上。 */
function starLight(colors, star) {
  const top = shade(colors.light, 0.22);
  return mix(colors.main, top, Math.min(1, 0.34 + star * 0.13));
}

/**
 * 一张招式图标只有五个色：edge（刃口／受光面）、body（本体）、dark（背光面）、
 * grip（柄／腕带）、hi（高光点）。**残影那一遍整体压向底板色**，所以它读起来是"影子"而不是"第二把兵器"。
 */
function activePalette(colors, star, ghost) {
  const light = starLight(colors, star);
  if (ghost) {
    // 残影只有三档明度（近亮远暗），不画分面的刃口与背光——它是"尾迹"不是第二把兵器。
    return {
      edge: mix(colors.main, colors.plate, 0.36),
      body: mix(colors.main, colors.plate, 0.52),
      dark: mix(colors.main, colors.plate, 0.66),
      grip: mix(colors.main, colors.plate, 0.52),
      hi: mix(colors.main, colors.plate, 0.36),
    };
  }
  return {
    edge: light,
    body: mix(colors.main, light, 0.42),
    dark: mix(colors.main, colors.plate, 0.28),
    grip: mix(colors.main, colors.plate, 0.14),
    hi: shade(light, 0.3),
  };
}

/**
 * 动作残影：**3 道与轴平行的短痕**，落在兵器的下右侧（挥出去留下的尾迹），越远越淡。
 *
 * 为什么不是"整把兵器的半透明副本"：副本会把**剑柄**一起错开，而剑柄本来就是全图最靠
 * 左下的一团，一错就伸出底板——第一版画出来是"图标旁边掉了块暗疙瘩"，不像动作。
 * 尾迹只借用**轴的走向**，不借用兵器的形状，所以剑／刀／枪／掌都能共用这同一段。
 * 3 道的长度（8／6／4）与间距（2／3／4）全 35 张同一个值：它是"同一套"的信号之一。
 * 间距要**贴着刃**（垂直于轴只错开 2～6 px）——拉开成一个扇形就变成一撮刮痕，不是尾迹。
 */
function trail45(c, p) {
  const spans = [[2, 8], [3, 6], [4, 4]];
  const tones = [p.edge, p.body, p.dark];
  spans.forEach(([gap, len], k) => {
    for (let i = 0; i < len; i++) {
      c.set(12 + i + gap, 19 - i + gap, hex(tones[k]));
    }
  });
}

/**
 * 底板就是图标的边界：**落在圆外的像素在游戏里没有任何东西托着**（面板底色直接透出来）。
 * 第一版那块"暗疙瘩"有一半就落在圆外，所以这里立一条**当场断言**，而不是出图后再剪一刀——
 * 剪是静默的，等于把越界藏起来；断言会在生成器这一层就叫人停手。
 * （现在 65 张都刚好收在圆内：最靠边的是剑柄头那一片，连勾边一起离边还有 1 px。）
 */
function assertInsidePlate(c, id) {
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      if (c.get(x, y)[3] > 0 && Math.hypot(x - 15.5, y - 15.5) > 15) {
        throw new Error(`${id}：有像素落在底板圆外（${x},${y}）——图形越界，收到圆内再出图`);
      }
    }
  }
  return c;
}

// ---- 五支剪影。**每支只画本体**（从 `(10,21)` 往右上走到 `(22,9)` 那条轴上），
// ---- 残影与勾边由 `activeIcon()`／`finish()` 统一加。画法共用，所以加一支兵器只加一个函数。

/** 剑：直刃（刃口一条亮线在上侧）＋ 护手 ＋ 柄。 */
function sword45(c, p, ox, oy) {
  for (let i = 0; i < 13; i++) {
    const x = 10 + i + ox;
    const y = 21 - i + oy;
    c.set(x, y, hex(p.edge));
    c.set(x, y + 1, hex(p.body));
    c.set(x, y + 2, hex(p.dark));
  }
  c.rect(8 + ox, 19 + oy, 5, 3, hex(p.dark));   // 护手：横过轴
  c.rect(7 + ox, 21 + oy, 3, 5, hex(p.grip));   // 柄
  c.set(6 + ox, 26 + oy, hex(p.edge));          // 柄头
  c.set(22 + ox, 9 + oy, hex(p.hi));            // 刃尖高光
}

/** 刀：**背厚刃薄**——背光面多一道，越往尖越厚，与剑的直刃一刀就能分开。 */
function saber45(c, p, ox, oy) {
  for (let i = 0; i < 12; i++) {
    const x = 10 + i + ox;
    const y = 21 - i + oy;
    c.set(x, y, hex(p.edge));
    c.set(x, y + 1, hex(p.body));
    c.set(x, y + 2, hex(p.dark));
    if (i > 2) c.set(x, y + 3, hex(p.dark));
  }
  c.rect(8 + ox, 19 + oy, 5, 3, hex(p.dark));
  c.rect(7 + ox, 21 + oy, 3, 5, hex(p.grip));
  c.set(6 + ox, 26 + oy, hex(p.edge));
  c.set(21 + ox, 10 + oy, hex(p.hi));
}

/** 枪：杆细一号、比剑长，靠**叶尖 ＋ 缨**认——缨用本学派色（枪缨本可朱红，但 15 §二 只把朱红给血／旗帜）。 */
function spear45(c, p, ox, oy) {
  for (let i = 0; i < 12; i++) {
    const x = 10 + i + ox;
    const y = 21 - i + oy;
    c.set(x, y, hex(p.body));
    c.set(x, y + 1, hex(p.dark));
  }
  for (let i = 0; i < 4; i++) {                 // 叶尖：三排收成一个尖
    const x = 21 + i + ox;
    const y = 10 - i + oy;
    c.set(x, y, hex(p.edge));
    c.set(x, y + 1, hex(p.body));
    c.set(x, y + 2, hex(p.dark));
  }
  c.set(25 + ox, 7 + oy, hex(p.hi));
  c.rect(10 + ox, 21 + oy, 3, 2, hex(p.grip));  // 杆尾的握把
  c.set(23 + ox, 13 + oy, hex(p.edge));         // 缨
  c.set(24 + ox, 14 + oy, hex(p.edge));
  c.set(22 + ox, 15 + oy, hex(p.edge));
}

/** 掌／拳：没有兵器，就用**前臂 ＋ 掌**走同一条轴——十一张拳脚招全走这一支。 */
function palm45(c, p, ox, oy) {
  for (let i = 0; i < 7; i++) {
    const x = 10 + i + ox;
    const y = 21 - i + oy;
    c.set(x, y, hex(p.body));
    c.set(x, y + 1, hex(p.body));
    c.set(x, y + 2, hex(p.dark));
  }
  c.ellipse(19 + ox, 12 + oy, 3, 3, hex(p.body));   // 掌
  c.ellipse(18 + ox, 11 + oy, 2, 2, hex(p.edge));   // 掌心受光
  for (const [dx, dy] of [[3, -1], [3, -3], [1, -4]]) {
    c.set(19 + dx + ox, 12 + dy + oy, hex(p.edge)); // 指
  }
  c.set(21 + ox, 8 + oy, hex(p.hi));
  c.rect(11 + ox, 20 + oy, 2, 2, hex(p.grip));      // 腕带
}

/** 腿（连环腿）：小腿 ＋ 靴，靴头那两块是它与掌的区分点。 */
function boot45(c, p, ox, oy) {
  for (let i = 0; i < 8; i++) {
    const x = 10 + i + ox;
    const y = 21 - i + oy;
    c.set(x, y, hex(p.body));
    c.set(x, y + 1, hex(p.body));
    c.set(x, y + 2, hex(p.dark));
  }
  c.rect(17 + ox, 11 + oy, 6, 4, hex(p.body));      // 靴
  c.rect(17 + ox, 11 + oy, 6, 1, hex(p.edge));
  c.rect(22 + ox, 11 + oy, 2, 4, hex(p.dark));      // 靴头
  c.set(21 + ox, 10 + oy, hex(p.hi));
}

/** 箭（冷箭）：细杆 ＋ 三角镞 ＋ 尾羽。 */
function arrow45(c, p, ox, oy) {
  for (let i = 0; i < 13; i++) {
    c.set(10 + i + ox, 21 - i + oy, hex(p.body));
  }
  for (let i = 0; i < 3; i++) {
    c.set(22 + i + ox, 9 - i + oy, hex(p.edge));
    c.set(22 + i + ox, 10 - i + oy, hex(p.body));
    c.set(22 + i + ox, 11 - i + oy, hex(p.dark));
  }
  c.set(9 + ox, 21 + oy, hex(p.edge));              // 尾羽
  c.set(8 + ox, 20 + oy, hex(p.edge));
  c.set(9 + ox, 23 + oy, hex(p.edge));
  c.set(8 + ox, 24 + oy, hex(p.edge));
  c.set(11 + ox, 23 + oy, hex(p.dark));
  c.set(24 + ox, 8 + oy, hex(p.hi));
}

/** 破军式：一道张开的破气弧（通用招，不属于任何兵器门）。 */
function crescent45(c, p, ox, oy) {
  for (let i = 0; i < 14; i++) {
    const t = i / 13;
    const bulge = Math.round(Math.sin(t * Math.PI) * 3);
    const x = 9 + i + ox;
    const y = 20 - i + oy - bulge;
    c.set(x, y, hex(p.edge));
    c.set(x, y + 1, hex(p.body));
    c.set(x + 1, y, hex(p.dark));
  }
  c.set(22 + ox, 4 + oy, hex(p.hi));
  c.set(9 + ox, 19 + oy, hex(p.edge));
}

// ---- 母题小签（每张一招，2–5 px）：**同门 ★1→★5 靠它递进**，不靠边框角标。
// ---- 它们一律画在右上角那片空处（本体走的是左下→右上，右上留白正好够两三个点）。

function aWind(c, p, ox, oy) {
  for (let i = 0; i < 5; i++) c.set(19 + i + ox, 5 + oy, hex(p.edge));
  for (let i = 0; i < 4; i++) c.set(21 + i + ox, 8 + oy, hex(p.dark));
  for (let i = 0; i < 5; i++) c.set(22 + i + ox, 12 + oy, hex(p.body));
}
function aSpark(c, p, ox, oy) {
  const x = 22 + ox; const y = 7 + oy;
  c.set(x, y, hex(p.hi));
  c.set(x, y - 1, hex(p.edge)); c.set(x, y + 1, hex(p.edge));
  c.set(x - 1, y, hex(p.edge)); c.set(x + 1, y, hex(p.edge));
  c.set(x, y - 2, hex(p.body)); c.set(x, y + 2, hex(p.body));
  c.set(x - 2, y, hex(p.body)); c.set(x + 2, y, hex(p.body));
}
function aWater(c, p, ox, oy) {
  for (let i = 0; i < 6; i++) {
    c.set(19 + i + ox, 6 + oy + (i % 2), hex(i % 2 ? p.body : p.edge));
    if (i > 1) c.set(17 + i + ox, 11 + oy + (i % 2), hex(p.dark));
  }
}
function aLeaf(c, p, ox, oy) {
  for (let i = 0; i < 5; i++) c.set(21 - i + ox, 5 + i + oy, hex(p.edge));
  for (let i = 0; i < 3; i++) c.set(19 - i + ox, 4 + i + oy, hex(p.body));
  c.set(20 + ox, 6 + oy, hex(p.hi));
  c.set(18 + ox, 10 + oy, hex(p.dark));
}
function aNote(c, p, ox, oy) {
  for (const [dx, dy] of [[0, 4], [3, 1], [6, -2]]) {
    c.set(19 + dx + ox, 8 + dy + oy, hex(p.edge));
    c.set(20 + dx + ox, 8 + dy + oy, hex(p.body));
  }
}
function aPoison(c, p, ox, oy) {
  // 五毒系一律绿（15 §二「绿色＝毒」），别掺学派蓝
  const g = "#8ABE72"; const gd = "#4A7A46";
  c.ellipse(23 + ox, 8 + oy, 2, 2, hex(g));
  c.ellipse(19 + ox, 11 + oy, 1, 1, hex(gd));
  c.ellipse(21 + ox, 5 + oy, 1, 1, hex(gd));
  c.set(26 + ox, 10 + oy, hex(gd));
}
function aFlame(c, p, ox, oy) {
  // 烈火＝火把橙那一族（16 §4.3），不是朱红
  const f = "#E8823A"; const fd = "#A8432A";
  for (let i = 0; i < 5; i++) c.set(21 + ox + (i > 2 ? 1 : 0), 11 - i + oy, hex(i < 2 ? f : fd));
  c.set(19 + ox, 9 + oy, hex(fd));
  c.set(23 + ox, 10 + oy, hex(fd));
  c.set(21 + ox, 4 + oy, hex(f));
}
function aBlood(c, p, ox, oy) {
  // 15 §二：朱红只给血／灯笼／旗帜／敌意——这里是血
  const b = "#A83A2E"; const bd = "#7E2A22";
  c.set(22 + ox, 5 + oy, hex(b)); c.set(23 + ox, 6 + oy, hex(b));
  c.set(22 + ox, 7 + oy, hex(bd)); c.set(23 + ox, 8 + oy, hex(bd));
  c.set(21 + ox, 9 + oy, hex(bd));
}
function aForce(c, p, ox, oy) {
  for (let i = 0; i < 6; i++) {
    const t = Math.round((i / 5) * 3);
    c.set(18 + i + ox, 4 + t + oy, hex(p.edge));
    c.set(16 + i + ox, 9 + t + oy, hex(p.body));
  }
}
function aStone(c, p, ox, oy) {
  for (const [dx, dy] of [[2, -2], [5, 1], [1, 3]]) {
    c.rect(18 + dx + ox, 6 + dy + oy, 2, 2, hex(p.body));
    c.set(18 + dx + ox, 6 + dy + oy, hex(p.edge));
  }
}
function aWine(c, p, ox, oy) {
  for (let i = 0; i < 5; i++) c.set(20 + ox, 4 + i + oy, hex(p.edge));
  c.set(21 + ox, 9 + oy, hex(p.hi));
  c.set(19 + ox, 9 + oy, hex(p.hi));
  c.set(21 + ox, 10 + oy, hex(p.body));
  c.set(23 + ox, 5 + oy, hex(p.dark));
  c.set(17 + ox, 5 + oy, hex(p.dark));
}

/**
 * `skill_base` 里 `sk_*` 那 35 行：id → 学派 / 星阶 / 剪影 / 母题小签。
 * **id 一位不能差**（同 `pf_*` 那条教训：程序按 `skill_id` 找图）。
 * 剪影按 `weapon_type`：剑 12（`sk_xuanwei_*` 9 ＋ 醉里乾坤 3）、拳 11（沉沙 4 ＋ 五毒 4 ＋ 烈火 3）、
 * 刀 5（黑风）、枪 4（铁枪）、其余 3（连环腿／破军式／冷箭）。
 */
const ACTIVES = [
  ["sk_xuanwei_01", "xuanwei", 1, sword45, aWind],
  ["sk_xuanwei_02", "xuanwei", 1, sword45, aWind],
  ["sk_xuanwei_03", "xuanwei", 2, sword45, aSpark],
  ["sk_xuanwei_04", "xuanwei", 2, sword45, aWind],
  ["sk_xuanwei_05", "xuanwei", 3, sword45, aSpark],
  ["sk_xuanwei_06", "xuanwei", 3, sword45, aWater],
  ["sk_xuanwei_07", "xuanwei", 4, sword45, aLeaf],
  ["sk_xuanwei_08", "xuanwei", 5, sword45, aNote],
  // 内功系指法（`skill_active.element=internal`），仍占招式槽，所以和别的招式同框
  ["sk_xuanwei_qi_01", "xuanwei", 2, sword45, aSpark],
  ["sk_chensha_01", "chensha", 1, palm45, aForce],
  ["sk_chensha_02", "chensha", 2, palm45, aStone],
  ["sk_chensha_03", "chensha", 3, palm45, aWater],
  ["sk_chensha_04", "chensha", 4, palm45, aForce],
  ["sk_wudu_01", "wudu", 1, palm45, aPoison],
  ["sk_wudu_02", "wudu", 2, palm45, aPoison],
  ["sk_wudu_03", "wudu", 3, palm45, aPoison],
  ["sk_wudu_04", "wudu", 4, palm45, aPoison],
  ["sk_bandit_slash", "heifeng", 1, saber45, aWind],
  ["sk_bandit_scout", "heifeng", 2, saber45, aStone],
  ["sk_boss_zhangfeng", "heifeng", 3, saber45, aWind],
  ["sk_boss_hengsao", "heifeng", 3, saber45, aForce],
  ["sk_heifeng_01", "heifeng", 4, saber45, aBlood],
  ["sk_spear_01", "tieqiang", 1, spear45, aForce],
  ["sk_spear_02", "tieqiang", 2, spear45, aWind],
  ["sk_spear_03", "tieqiang", 3, spear45, aBlood],
  ["sk_spear_04", "tieqiang", 4, spear45, aForce],
  ["sk_liehuo_01", "liehuo", 2, palm45, aFlame],
  ["sk_liehuo_02", "liehuo", 3, palm45, aFlame],
  ["sk_liehuo_03", "liehuo", 4, palm45, aFlame],
  ["sk_drunk_zuibu", "zuidj", 3, sword45, aWine],
  ["sk_drunk_jiuzhongdao", "zuidj", 4, sword45, aWine],
  ["sk_drunk_01", "zuidj", 5, sword45, aWine],
  ["sk_common_01", "common", 1, boot45, aWind],
  ["sk_common_02", "common", 3, crescent45, aForce],
  ["sk_bandit_arrow", "bandit", 1, arrow45, aSpark],
];

/** 按表生成一张招式图标：底板 ＋ 残影 ＋ 本体 ＋ 母题小签。`id` 只用于越界断言报错点名。 */
function activeIcon(id, school, star, shape, mark) {
  const colors = SCHOOL_COLORS[school];
  const c = plate(colors.plate, 0.7);
  trail45(c, activePalette(colors, star, true));
  const p = activePalette(colors, star, false);
  shape(c, p, 0, 0);
  if (mark) mark(c, p, 0, 0);
  return assertInsidePlate(finish(c, starLight(colors, star)), id);
}

module.exports = {
  buildAll, plate, qiRing, qiRingAt, rings, motif, breathMark, finish,
  SCHOOL_COLORS, MOTIFS, innerIcon, ACTIVES, activeIcon, assertInsidePlate,
};
