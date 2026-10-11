// 空槽占位（32×32）：`icons/slot/slot_<slot_id>.png`，7 张。
//
// 设计口径（交接单_美术 §九 四／§十 第 2 条）：**一个槽一张空框，里面只放部位标识，
// 不带任何装备的形**——画成一把剑／一条腰带，玩家就会以为那个槽里已经有东西了。
// 所以里面那笔一律是**身体部位**（手／躯干／腰／肩／腿／指／颈），不是任何一件装备的剪影。
//
// 两条落点：① 文件名只跟 `equip_slot_def.slot_id` 走（中文名照表读，别自抄一套）；
// ② **不能放进 `icons/equip/`**——那个目录有硬门限「每张 png 都必须是 `equip_base` 的 id」，
// 空槽不是装备，塞进去自检当场红（2026-10-04 命名一度定成 `icons/equip/slot_*`，就是被它逼回来的）。
//
// 画法上这族与另两族不同：**框先描边、记号不描**。框是"这里有个位置"，记号是"哪个部位"——
// 记号若也走 `edge()`，2px 的笔画会被那一圈墨糊成 4px，塞进 22px 的框里就成一坨。

"use strict";

const { Canvas, hex } = require("./pixel");
const { S, INK, FAINT, punch } = require("./pictos");

/** 空框 ＋ 部位记号。`mark(canvas, color)` 只画记号，画在描边之后。 */
function slotIcon(mark) {
  const c = new Canvas(S, S);
  c.frame(3, 3, 26, 26, hex(FAINT));   // 空框（1px 赭石）
  c.edge();                            // 框外再走一圈墨：宣纸上"这儿有个框"才立得住
  mark(c, hex(INK));
  return c;
}

/** 武器槽：一只**手**（握持的位置）。张开五指的轮廓，不是任何兵器。 */
function slotWeapon() {
  return slotIcon((c, col) => {
    c.disk(15.5, 18, 5, col);                       // 掌
    for (const x of [11, 14, 17, 20]) c.rect(x, 10, 2, 7, col);   // 四指
    c.disk(10, 17, 2.5, col);                       // 拇指
  });
}

/** 身体槽：**躯干**（肩宽腰收）。 */
function slotBody() {
  return slotIcon((c, col) => {
    c.rect(12, 7, 7, 4, col);                       // 颈
    c.rect(8, 11, 15, 4, col);                      // 肩
    for (let y = 15; y <= 24; y++) {
      const half = Math.max(4, Math.round(7 - (y - 15) * 0.3));
      c.hLine(Math.round(15.5 - half), y, half * 2, col);
    }
  });
}

/** 腰带槽：**躯干收腰那一段**（肩宽 → 腰细得硬 → 胯再放），腰上再掏一小道折痕。
 *  两版前车：①「躯干 ＋ 掏一道通长的腰缝」＝**上下两块砖**，缝断了轮廓；
 *  ②「温和的收腰」＝**花瓶**，收得不够狠就读成器皿。所以这一段要收得干脆、还是方的。 */
function slotBelt() {
  return slotIcon((c, col) => {
    c.rect(12, 5, 7, 4, col);                       // 颈
    c.rect(8, 9, 15, 4, col);                       // 肩（有肩才读得出"这是个人"）
    const half = [6, 6, 3, 3, 3, 3, 3, 5, 5, 6, 6, 6, 5];   // 硬收腰（第 3–7 行）再放胯
    half.forEach((h, k) => c.hLine(Math.round(15.5 - h), 13 + k, h * 2, col));
    punch(c, 14, 17, 4, 1);                         // 折痕，不是腰带：短、且只掏一道
  });
}

/** 肩部槽：**两肩**——左右各一道从颈侧往外的斜肩线，中间那根颈柱。
 *  画过一版"单边一道圆肩弧"，摆出来是把**镰刀**：肩是两边一起看的，只画一边认不出来。 */
function slotShoulder() {
  return slotIcon((c, col) => {
    c.rect(14, 9, 4, 6, col);                       // 颈柱（落在两肩的缺口里）
    for (let k = 0; k < 6; k++) {
      c.rect(10 - k, 12 + k, 3, 3, col);            // 左肩：往外、往下
      c.rect(19 + k, 12 + k, 3, 3, col);            // 右肩（镜像）
    }
  });
}

/** 下装槽：**两条腿**（连胯）。 */
function slotLegs() {
  return slotIcon((c, col) => {
    c.rect(10, 8, 12, 4, col);                      // 胯
    c.rect(11, 12, 4, 13, col);
    c.rect(17, 12, 4, 13, col);
  });
}

/** 戒指槽：**并起的食指与中指**（＋ 指根），不是戒指圈。
 *  画过一版"一根竖直的指头"，摆出来是**感叹号**——一根光棍读不出"指"，
 *  手指得有指根、还得有第二根做参照。 */
function slotRing() {
  return slotIcon((c, col) => {
    c.rect(10, 19, 12, 6, col);                     // 指根（掌的一小段）
    c.rect(10, 8, 4, 11, col);                      // 食指
    c.rect(17, 9, 4, 10, col);                      // 中指（比食指短一行）
    punch(c, 10, 14, 1, 2);                         // 指节：**只在侧面啃一道**，
    punch(c, 17, 15, 1, 2);                         // 掏通一条会把一根手指切成两截（上一版就这么废的）
    punch(c, 10, 8, 1, 1);                          // 倒角，指尖是个圆头
    punch(c, 20, 9, 1, 1);
  });
}

/** 项链槽：**颈**——头 ＋ **拉长的颈柱** ＋ 斜肩。读"颈"全靠中间那段细长：
 *  躯干那族（`slotBody`）的颈只有 4 行、和肩连成一块；这里拉到 7 行，一眼落在脖子上。
 *  这一张试了六版，全是"只画骨头不画人"或"多画的东西抢了主角"：U 形弧（两撇在上、一弧在下）＝**笑脸**；
 *  一竖 ＋ 两撇向下斜开＝**帐篷**；平肩＋圆头＝**台灯**；不画头（只留长颈＋口）＝**搋子**。
 *  留下这版是"有个头的半身像"——严格说偏**棋子**，但它是唯一还在读"人"的形状；
 *  这条槽位上永远挨着「项链」两个字，宁可形状朴素也别让人看成别的部位。 */
function slotNecklace() {
  return slotIcon((c, col) => {
    c.disk(15.5, 9, 4, col);                        // 头（下巴那一段，不画五官）
    c.rect(14, 12, 4, 7, col);                      // 颈柱：故意拉长（躯干那族的颈只有 4 行）
    const half = [4, 5, 6, 7, 7, 7];                // 斜肩：往外压**六行就收住**——
    half.forEach((h, k) => c.hLine(Math.round(15.5 - h), 19 + k, h * 2, col));
                                                    // 再往下摊开就成"兵"的锥形底座了
  });
}

/**
 * 相对 `assets/` 的路径 → 画布。**文件名 = `slot_<equip_slot_def.slot_id>`，一位不能差**
 * （表里没有 `icon` 列，界面按 slot_id 拼路径；写错＝空槽那张框静静地不显示）。
 */
function buildAll() {
  return {
    "icons/slot/slot_weapon.png": slotWeapon(),
    "icons/slot/slot_body.png": slotBody(),
    "icons/slot/slot_belt.png": slotBelt(),
    "icons/slot/slot_shoulder.png": slotShoulder(),
    "icons/slot/slot_legs.png": slotLegs(),
    "icons/slot/slot_ring.png": slotRing(),
    "icons/slot/slot_necklace.png": slotNecklace(),
  };
}

module.exports = { buildAll };
