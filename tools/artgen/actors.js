// 角色侧的贴图：**NPC 头像（9 张）与敌人剪影**。
//
// **这是头像的唯一出处。** 曾经还有一份 `avatars.js` 画着其中 7 张（更早的简版），
// 而入口是**先写 actors、后写 avatars**——于是那 7 张被静默覆盖成简版、只有
// 沈雁回（只在 actors 里）留在这一版，**同一排头像里两种画风**。2026-10-04 已删掉
// `avatars.js`：一份东西两处定义，漂了不报错，正是这个仓库最防的那种坑。
//
// 路径不是照 15 §六 的表抄的，而是**照代码实际加载的路径**：
//   `src/ui/npc_panel.gd` 读的是 `res://assets/sprites/avatars/avatar_<npc_id>.png`
//   （设计文档里写的是 `assets/sprites/npc/`，与工程不一致——出图要按工程，见决策 276）。
// 因此这里的文件名 = `npc_def.npc_id`，一位不能差。
//
// 规格：头像**原生 32×32、头肩构图**（15 §4.2 补）；显示时 2× 到 64×64，
// 所以 32×32 是硬约束，不能顺手按 64 画。

"use strict";

const { Canvas, hex, mix, shade, noise } = require("./pixel");

const S = 32;
const INK = "#1E2224";

/** 头像底：一块比面板更暗的圆角背板 + 1px 石灰边（14 号：头像与名字／称号同排）。 */
function frame() {
  const c = new Canvas(S, S);
  for (let y = 1; y < S - 1; y++) {
    for (let x = 1; x < S - 1; x++) {
      const corner =
        (x < 3 || x > S - 4) && (y < 3 || y > S - 4);
      if (corner) continue;
      c.set(x, y, hex("#22262A"));
    }
  }
  c.frame(1, 1, S - 2, S - 2, hex("#6E6A63"));
  return c;
}

/** 肩与领：头肩头像的下半身只画到锁骨，领口是区分身份的第二个信息位。 */
function shoulders(c, { cloth, clothDark, collar, lapel = null }) {
  for (let y = 25; y < S - 1; y++) {
    const w = 7 + (y - 25) * 2;
    c.rect(16 - w, y, w * 2, 1, hex(cloth));
  }
  c.hLine(9, 25, 14, hex(shade(cloth, 0.12)));
  c.hLine(9, 30, 14, hex(clothDark));
  // 交领：两笔斜线，右压左（汉服右衽）
  for (let i = 0; i < 5; i++) {
    c.set(16 - 1 - i, 25 + i, hex(collar));
    c.set(16 + 1 + i, 25 + i, hex(collar));
  }
  if (lapel) {
    for (let i = 0; i < 4; i++) c.set(16 - 2 - i, 26 + i, hex(lapel));
  }
}

/** 头：脸、耳、眉眼、嘴。`hairStyle` 决定剪影，是 32×32 上最省事也最有效的区分手段。 */
function head(c, {
  skin = "#C8A886", hair = "#2E2622", hairLight = "#463A32",
  hairStyle = "topknot", beard = null, brow = "#3A2E26", mouth = "#8A6A5A",
}) {
  // 脖子
  c.rect(13, 22, 6, 4, hex(shade(skin, -0.22)));
  // 耳
  c.rect(8, 15, 2, 4, hex(shade(skin, -0.12)));
  c.rect(22, 15, 2, 4, hex(shade(skin, -0.12)));
  // 脸
  c.ellipse(15.5, 16, 7, 8, hex(skin));
  c.ellipse(15.5, 15, 6, 6, hex(shade(skin, 0.08)));
  c.rect(9, 12, 14, 6, hex(skin));
  // 眉与眼
  for (const x of [12, 18]) {
    c.hLine(x, 14, 3, hex(brow));
    c.rect(x + 1, 16, 2, 2, hex(INK));
    c.set(x + 1, 16, hex("#3A3430"));
  }
  // 嘴
  c.hLine(14, 20, 4, hex(mouth));
  // 发／巾／冠：只画上半圈，剪影区别最大
  if (hairStyle === "bald") {
    c.ellipse(15.5, 12, 7, 4, hex(skin));
  } else if (hairStyle === "topknot") {
    c.ellipse(15.5, 12, 7, 5, hex(hair));
    c.rect(13, 5, 5, 4, hex(hair));
    c.rect(14, 3, 3, 3, hex(hairLight));
  } else if (hairStyle === "bun") {
    c.ellipse(15.5, 12, 7, 5, hex(hair));
    c.ellipse(15.5, 7, 3, 2, hex(hair));
  } else if (hairStyle === "band") {
    c.ellipse(15.5, 12, 7, 5, hex(hair));
    c.rect(9, 10, 14, 2, hex("#8A8578"));
    c.rect(21, 12, 4, 2, hex("#A83A2E"));
  } else if (hairStyle === "straw") {
    c.ellipse(15.5, 9, 11, 3, hex("#A89060"));
    c.ellipse(15.5, 11, 9, 2, hex("#8A7349"));
    c.rect(15, 4, 2, 4, hex("#8A7349"));
  } else if (hairStyle === "cloth") {
    c.ellipse(15.5, 11, 8, 5, hex(hair));
    c.rect(8, 12, 16, 3, hex(hair));
    c.rect(8, 15, 2, 4, hex(shade(hair, -0.2)));
    c.rect(22, 15, 2, 4, hex(shade(hair, -0.2)));
  } else if (hairStyle === "messy") {
    c.ellipse(15.5, 12, 7, 5, hex(hair));
    for (const [x, y] of [[9, 8], [12, 5], [16, 4], [20, 6], [23, 9]]) c.rect(x, y, 2, 3, hex(hair));
    c.set(10, 6, hex(hairLight));
  }
  if (beard === "full") {
    c.ellipse(15.5, 22, 6, 3, hex(hair));
    c.rect(13, 21, 6, 3, hex(hair));
  } else if (beard === "goatee") {
    c.rect(15, 22, 3, 4, hex(hair));
    c.set(15, 25, hex(hairLight));
  } else if (beard === "stubble") {
    c.speckle(11, 19, 10, 4, hex(hair), 0.22, 7);
  }
}

// ---------------------------------------------------------------- 7 个 NPC

/** 王铁·清风驿铁匠：壮、络腮胡、皮围裙、肩上搭着锤。 */
function npcWangTie() {
  const c = frame();
  shoulders(c, { cloth: "#5A4636", clothDark: "#3E3026", collar: "#8A6A46" });
  head(c, { hairStyle: "band", beard: "full", hair: "#2E2622" });
  // 身份道具：铁锤（他手上有三十年铁锈）
  c.rect(25, 25, 3, 6, hex("#8A6A46"));
  c.rect(23, 22, 7, 4, hex("#565E64"));
  c.hLine(23, 22, 7, hex("#9EA6AC"));
  c.edge();
  return c;
}

/** 张贵·酒楼掌柜：圆脸、笑、布巾、暖色褂子（消息灵通的那种油滑）。 */
function npcZhangGui() {
  const c = frame();
  shoulders(c, { cloth: "#7A6242", clothDark: "#5A4830", collar: "#C8A24A" });
  head(c, { hairStyle: "band", hair: "#3A2E26", mouth: "#6B4A3A" });
  // 眯眼笑（把眼睛压成一条线）
  c.hLine(12, 16, 3, hex("#3A3430"));
  c.hLine(18, 16, 3, hex("#3A3430"));
  c.rect(25, 26, 4, 5, hex("#8A6A46"));
  c.edge();
  return c;
}

/** 钱大夫·医馆大夫：长须、方巾、素袍（脾气古怪的三件套）。 */
function npcQianDafu() {
  const c = frame();
  shoulders(c, { cloth: "#5E6A70", clothDark: "#44505A", collar: "#B0AA9C" });
  head(c, { hairStyle: "cloth", hair: "#5A5E62", beard: "goatee", skin: "#C0A284" });
  c.rect(25, 24, 3, 7, hex("#8A8578"));
  c.edge();
  return c;
}

/** 孙掌柜·客栈掌柜：中年、头巾、嘴上不饶人（眉压低）。 */
function npcSunZhanggui() {
  const c = frame();
  shoulders(c, { cloth: "#4A5A62", clothDark: "#35444C", collar: "#8A8578" });
  head(c, { hairStyle: "band", hair: "#2E2622", beard: "stubble" });
  c.hLine(11, 13, 4, hex("#2E2622"));
  c.hLine(17, 13, 4, hex("#2E2622"));
  c.rect(25, 26, 4, 5, hex("#6B5236"));
  c.edge();
  return c;
}

/** 老周·采药人：斗笠、白须、背篓带（常年在山里跑）。 */
function npcCaiyao() {
  const c = frame();
  shoulders(c, { cloth: "#5A6048", clothDark: "#42462F", collar: "#8A7A55" });
  head(c, { hairStyle: "straw", hair: "#C8C0B0", beard: "full", skin: "#BE9E7E" });
  // 背篓的绳
  for (let i = 0; i < 6; i++) c.set(9 + i, 24 + i, hex("#8A6A46"));
  c.edge();
  return c;
}

/** 陈氏·荒村遗孀：女性、头巾压得很低、面色灰（唯一活下来的人）。 */
function npcHuangcun() {
  const c = frame();
  shoulders(c, { cloth: "#6E6858", clothDark: "#524C40", collar: "#8A8578", lapel: "#7A7468" });
  head(c, { hairStyle: "bald", skin: "#B99C84", brow: "#3A322C", mouth: "#7A5A50" });
  // 头巾：从头顶一直垂到肩，两侧各一片——女性的剪影靠这个，不靠配色
  c.ellipse(15.5, 11, 9, 6, hex("#4A4238"));
  c.rect(6, 12, 20, 4, hex("#4A4238"));
  c.rect(6, 15, 3, 10, hex("#3E3830"));
  c.rect(23, 15, 3, 10, hex("#3E3830"));
  c.rect(15, 5, 3, 3, hex("#5A5248"));
  // 低垂的视线：眼画成半阖
  c.hLine(12, 17, 3, hex("#3E3830"));
  c.hLine(18, 17, 3, hex("#3E3830"));
  c.edge();
  return c;
}

/** 阿福·被囚村民：乱发、消瘦、破衣（饿了三天）。 */
function npcQiutu() {
  const c = frame();
  shoulders(c, { cloth: "#6A6252", clothDark: "#4A453A", collar: "#8A8578" });
  head(c, { hairStyle: "messy", hair: "#3A3028", skin: "#BC9C7C", beard: "stubble", mouth: "#8A5A50" });
  // 破口：肩上撕开一道
  c.hLine(20, 27, 3, hex("#2E2A24"));
  c.set(21, 28, hex("#2E2A24"));
  c.edge();
  return c;
}

/**
 * 沈雁回·前漕运司书吏之女（0.29.0 新增，第 8 张）。
 *
 * 人设要求是「不是等人来救的弱女子，也不是女侠」——落笔全在**眼睛和嘴**上：
 * 眉平、眼窄而直视（眼睛毒），嘴压平不笑（性子冷）；
 * 造型按「账房先生的女儿」走：素色直领、头发梳得一丝不乱、一根素簪，没有任何侠气装饰。
 * 与陈氏（头巾垂肩、眼半阖、面色灰）刻意分开：一个是被碾过的，一个是不肯被碾的。
 */
function npcShenYanhui() {
  const c = frame();
  shoulders(c, { cloth: "#4A4E56", clothDark: "#343840", collar: "#B0AA9C" });
  head(c, {
    hairStyle: "bun", hair: "#24262A", hairLight: "#3A3E44",
    skin: "#D0B294", brow: "#2E2A26", mouth: "#8A5A50",
  });
  // 眼：窄、平、直视（不是半阖也不是圆眼）
  for (const x of [11, 17]) {
    c.rect(x + 1, 16, 3, 1, hex("#26282C"));
    c.set(x + 1, 16, hex("#14161A"));
    c.set(x + 3, 16, hex("#14161A"));
    c.hLine(x, 14, 4, hex("#2E2A26"));
  }
  // 嘴：压平（嘴角不上翘）
  c.hLine(14, 20, 5, hex("#7A4E46"));
  // 素簪：一根，横着插在发髻上
  c.hLine(11, 8, 10, hex("#A89060"));
  c.set(10, 8, hex("#C8A24A"));
  // 领口压得整齐（她的衣着比谁都利落）
  c.hLine(13, 25, 7, hex("#C8C2B4"));
  c.edge();
  return c;
}

/**
 * 裴无咎·黑风寨大寨主（0.32.0 新增，第 9 张）。
 *
 * 人设一句：**四十余岁，鬓角与下颌有旧疤，外罩漕帮旧号衣的残片；眼神不凶，是沉的**
 * ——十年前漕帮的少舵主，绰号黑风刀；他不抢、不骂、也不诉苦，只认兄弟的名字。
 *
 * 落笔四处：
 *   · **旧疤**是这张脸的识别点，画在**鬓角与下颌**两处。**用比肤色亮一档的"结痂色"，
 *     不用红**——用红就成了新伤，跟"十年前"对不上（与沈雁回靠眼睛认人同理，他靠疤）。
 *   · **漕帮旧号衣的残片**：靛蓝的号衣料子只占**半边肩**，另一边露出里面的粗布，
 *     撕口留在下摆。**他穿的不是黑风寨的衣服**——这场戏的题眼就是"他不是这寨子的人"。
 *   · **眼神不凶**：**眉用浅褐（`#7A6450`）而不是墨黑**——32×32 上"凶"几乎全由眉毛的
 *     粗细与深浅承担，加宽加深就成刀眉了（第一版就这么画歪的）。浅眉 ＋ 默认的窄眼＝沉。
 *   · **四十余岁**：两鬓掺灰 ＋ 下颌短须（不是络腮胡，那太"匪"，与王铁的络腮胡分开）。
 */
function npcPeiWujiu() {
  const c = frame();
  // 号衣的靛蓝要**比沈雁回那身素灰蓝更蓝**——两人同排出现，撞色就认不出谁是谁
  shoulders(c, { cloth: "#3A4654", clothDark: "#26303C", collar: "#8A7A66", lapel: "#54667A" });
  head(c, {
    hairStyle: "topknot", hair: "#2A2E33", hairLight: "#6E7378",
    skin: "#C2A488", beard: "goatee", brow: "#7A6450", mouth: "#7A5248",
  });
  // 旧疤：**要在暗处才看得见**——所以鬓角那道是"发际线上缺了一块"（2 px 宽，露出头皮），
  // 下颌那道压在脸的右下缘。只画一像素宽的亮线会读成"高光"，不像疤。
  const scarHi = hex(shade("#C2A488", 0.24));
  const scarLo = hex(shade("#C2A488", -0.18));
  for (let y = 13; y <= 16; y++) {
    c.set(9, y, scarHi);
    c.set(10, y, y < 15 ? scarLo : scarHi);
  }
  for (let i = 0; i < 4; i++) c.set(18 + i, 21 - i, i % 2 ? scarLo : scarHi);
  // 号衣残片：右肩还留着靛蓝那一块，下摆撕开
  c.rect(19, 26, 6, 4, hex("#54667A"));
  c.hLine(19, 26, 6, hex("#6E7E92"));
  c.set(19, 29, hex("#26303C"));
  c.set(21, 29, hex("#26303C"));
  c.edge();
  return c;
}

// ---------------------------------------------------------------- 敌人剪影

/**
 * 敌人剪影。15 §4.2：**种类区分靠剪影与配色，不靠细节**。
 * 尺寸取 32×32——与仓库里既有的 `npc_placeholder.png` 一致
 * （15 §4.2 只给主角写了 16×24，敌人的尺寸没写；若设计要 16×24，说一句即可重出）。
 */
function enemySilhouette({ cloth, clothDark, trim, hat, stance, weapon, hair }) {
  const c = new Canvas(S, S);
  // 影子
  c.ellipse(16, 30, 9, 2, [0, 0, 0, 70]);
  // 腿（站姿：外八 = 匪气；并拢 = 端正）
  const legs = stance === "wide" ? [[10, 26], [19, 26]] : [[13, 26], [16, 26]];
  for (const [x, y] of legs) {
    c.rect(x, y, 3, 5, hex(clothDark));
    c.rect(x, y, 3, 1, hex(shade(clothDark, 0.14)));
  }
  // 背后那把剑**先画**（弟子）——它有一半要露在身体轮廓外面
  if (weapon === "sword_back") {
    for (let i = 0; i < 16; i++) c.set(9 + i, 7 + i, hex("#33414A"));
    for (let i = 0; i < 16; i++) c.set(10 + i, 7 + i, hex("#4A5A6A"));
    c.rect(6, 4, 6, 3, hex("#8A6A46")); // 剑柄（左上，露在肩外）
    c.rect(7, 3, 4, 1, hex("#C8A24A"));
    c.rect(23, 21, 4, 2, hex("#33414A")); // 剑鞘尖（右下）
  }
  // 身体（窄一点，剪影才立得住；32px 一格放不下 22 宽的方肩膀）
  c.rect(10, 13, 12, 13, hex(cloth));
  c.rect(10, 13, 3, 13, hex(shade(cloth, 0.16)));
  c.rect(19, 13, 3, 13, hex(shade(cloth, -0.12)));
  c.hLine(10, 25, 12, hex(clothDark));
  // 腰带（trim 是这一格唯一允许的强调色位）
  c.rect(10, 21, 12, 2, hex(trim));
  // 手臂：贴着身体画，靠明暗分开，别撑出方肩
  c.rect(8, 14, 2, 10, hex(shade(cloth, -0.18)));
  c.rect(22, 14, 2, 10, hex(shade(cloth, -0.18)));
  // 头：**先脸后发**——反过来画会变成戴头盔
  c.rect(12, 5, 8, 9, hex("#C8A886"));
  c.rect(12, 5, 8, 1, hex(hair));
  c.rect(11, 6, 1, 7, hex(hair));
  c.rect(20, 6, 1, 7, hex(hair));
  c.rect(12, 3, 8, 3, hex(hair)); // 顶发
  if (hat === "band") {
    c.rect(11, 4, 10, 2, hex(trim));
    c.rect(21, 5, 3, 1, hex(trim));
  } else if (hat === "ribbon") {
    // 束发的飘带：一撮发髻 + 脑后一条带子（不是帽檐）
    c.rect(13, 1, 6, 3, hex(hair));
    c.rect(13, 0, 6, 1, hex(trim));
    c.rect(21, 2, 3, 1, hex(trim));
    c.rect(23, 3, 2, 1, hex(trim));
  }
  // 手里的刀**最后画**（匪）：刀身压在轮廓外，一眼看出「提着家伙」
  if (weapon === "blade") {
    c.rect(24, 17, 2, 9, hex("#6E7A82"));
    c.rect(24, 17, 1, 9, hex("#9EA6AC"));
    c.rect(23, 15, 4, 2, hex("#8A6A46"));
    c.set(22, 16, hex("#8A6A46"));
  }
  c.edge();
  return c;
}

/** 山寨喽啰：深褐短打 + 头巾打结 + 手里提刀 + 外八站姿。 */
function enemyBanditThug() {
  return enemySilhouette({
    cloth: "#5A3E2A", clothDark: "#3E2A1C", trim: "#8A5A38",
    hat: "band", stance: "wide", weapon: "blade", hair: "#241C16",
  });
}

/**
 * 别派弟子：门派制服（靛青）＋束发飘带＋**背后斜背剑**＋并拢站姿。
 * 与喽啰的差别全在剪影上：细、正、武器不在手上——玩家撞见不该以为要打架。
 */
function enemyWandererDisciple() {
  return enemySilhouette({
    cloth: "#3E5266", clothDark: "#2A3A4A", trim: "#7A8A9A",
    hat: "ribbon", stance: "narrow", weapon: "sword_back", hair: "#1E2226",
  });
}

/** 一次性产出角色侧贴图：相对 `assets/` 的路径 → 画布。 */
function buildAll() {
  return {
    "sprites/avatars/avatar_npc_wang_tie.png": npcWangTie(),
    "sprites/avatars/avatar_npc_zhang_gui.png": npcZhangGui(),
    "sprites/avatars/avatar_npc_qian_dafu.png": npcQianDafu(),
    "sprites/avatars/avatar_npc_sun_zhanggui.png": npcSunZhanggui(),
    "sprites/avatars/avatar_npc_caiyao.png": npcCaiyao(),
    "sprites/avatars/avatar_npc_huangcun.png": npcHuangcun(),
    "sprites/avatars/avatar_npc_qiutu.png": npcQiutu(),
    // 下面两张的名字**以 `npc_def` 为准**（表行落地后按实际 `npc_id` 对，一位不能差）。
    "sprites/avatars/avatar_npc_shen_yanhui.png": npcShenYanhui(),
    "sprites/avatars/avatar_npc_pei_wujiu.png": npcPeiWujiu(),
    // 敌人剪影（15 §六：`sprites/<faction>/<enemy_id>.png`）
    "sprites/bandit/en_bd_thug.png": enemyBanditThug(),
    "sprites/neutral/en_wanderer_disciple.png": enemyWandererDisciple(),
  };
}

module.exports = { buildAll, frame, shoulders, head, enemySilhouette, npcPeiWujiu };
