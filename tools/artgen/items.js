// 物品图标（32×32）。
//
// 路径照 15 §六：`icons/item/<item_id>.png`，**文件名 = `item_base.item_id`**。
// 注意这条目录现在与大地图图标（`assets/sprites/icons/`）并存：
// 后者是 07 §8.1 定的、代码已经写死 `ICON_DIR` 的那一套；两者别混。
//
// 接线情况：`item_base.icon` 现在**有界面读了**——角色页／行囊页走 `src/ui/icon_paths.gd` 的
// `IconPaths.item()`（有图才摆、没图不留空位，见框架说明决策 309／312）。
// 装备图标（`equips.js`）与这一批**同框**，所以两边的画法口径要一起看：都是透明底 ＋ 墨线勾边。

"use strict";

const { Canvas, hex, mix, shade, noise } = require("./pixel");

const S = 32;
const INK = "#1E2224";

/**
 * 藏宝图：一卷旧图纸 + 朱红系带，露出一角画着山形与朱印。
 * 它是「偷来的唯一入口」，所以要比普通纸卷更像「有人藏过的东西」。
 */
function itemTreasureMap() {
  const c = new Canvas(S, S);
  // 卷身（左低右高的斜卷）
  for (let i = 0; i < 20; i++) {
    const x = 5 + i;
    const y = 20 - Math.round(i * 0.45);
    c.rect(x, y, 1, 12, hex(i < 12 ? "#C8B48A" : "#B0A078"));
  }
  c.rect(4, 20, 2, 12, hex("#8A7A55"));
  c.rect(24, 10, 2, 12, hex("#8A7A55"));
  // 纸面（摊开的一角）
  c.rect(6, 12, 20, 10, hex("#D8D2C4"));
  c.rect(6, 12, 20, 1, hex("#B0AA9C"));
  c.rect(6, 21, 20, 1, hex("#B0AA9C"));
  // 图上画的：两笔山形 + 一条河
  for (let i = 0; i < 5; i++) {
    c.set(9 + i, 19 - i, hex("#4A5258"));
    c.set(13 - i, 19 - i, hex("#4A5258"));
    c.set(17 + i, 18 - i, hex("#4A5258"));
    c.set(21 - i, 18 - i, hex("#4A5258"));
  }
  c.hLine(8, 19, 16, hex("#5A6E7A"));
  // 朱印：藏宝图这类东西的标记
  c.rect(21, 14, 4, 4, hex("#A83A2E"));
  c.set(22, 15, hex("#D8D2C4"));
  c.set(23, 16, hex("#D8D2C4"));
  // 系带
  c.rect(4, 23, 22, 2, hex("#8A3A2E"));
  c.rect(12, 20, 2, 6, hex("#A83A2E"));
  c.edge();
  return c;
}

/**
 * 遗篇残卷：石隙迷窟终点那部 ★5 内功的秘籍。
 * 与普通书卷的区别用**幽蓝**（16 §4.8 的石台光）＋撕口表达——它是前朝遗物。
 */
function itemScrollYipian() {
  const c = new Canvas(S, S);
  // 竹简／卷轴主体：右上角是撕口
  c.rect(6, 6, 20, 20, hex("#B0A078"));
  c.rect(7, 7, 18, 18, hex("#C8B48A"));
  // 竹简的竖条
  for (let x = 9; x < 25; x += 4) c.vLine(x, 7, 18, hex("#A89070"));
  // 撕口：右下角缺一块（残卷）
  for (let y = 17; y < 26; y++) {
    const cut = 26 - (y - 17) * 1.4;
    for (let x = Math.round(cut); x < 26; x++) c.set(x, y, [0, 0, 0, 0]);
  }
  for (let i = 0; i < 6; i++) c.set(24 - i, 16 + Math.round(i * 0.5), hex("#8A7A55"));
  // 前朝的字：三行朱笔（不是墨黑——它被朱批过）
  for (const y of [10, 13, 16]) {
    for (let x = 9; x < 20; x += 3) c.hLine(x, y, 2, hex("#8A3A2E"));
  }
  // 幽蓝的光：与石隙石台同一色，一眼知道它出自哪儿
  for (const [x, y] of [[8, 8], [24, 8], [8, 24]]) {
    c.set(x, y, hex("#3E5A7A"));
    c.set(x + 1, y, hex("#5E7EA8"));
  }
  c.edge();
  return c;
}

/**
 * 账册：第一章暗线的核心道具（十年前白鹭渡沉银案的真账）。
 *
 * 设计要求是**「旧、普通、不像宝贝」**——玩家第一眼看不出它重要。
 * 所以刻意不画成书册的端庄样：封皮磨白、边角起毛、夹着半张纸条，
 * 连朱印都不给它（宝贝才盖印，这只是一本被人翻烂的账）。
 */
function itemBdLedger() {
  const c = new Canvas(S, S);
  // 封皮：压暗的旧蓝布，四角磨白
  c.rect(4, 6, 24, 21, hex("#3E4650"));
  c.rect(5, 7, 22, 19, hex("#4A545E"));
  c.hLine(5, 7, 22, hex("#5A646E"));
  c.hLine(5, 25, 22, hex("#2E363E"));
  // 装订线：线装书的针脚，缝得歪
  for (let y = 9; y < 25; y += 4) {
    c.hLine(4, y, 4, hex("#8A7A55"));
    c.set(3, y + 1, hex("#6E6248"));
  }
  // 磨白的角落（旧而不破得夸张）
  for (const [x, y] of [[5, 8], [25, 8], [5, 24], [25, 24]]) {
    c.rect(x - 1, y - 1, 3, 2, hex("#B0AA9C"));
  }
  // 起毛的边角：右下缺一小口
  for (let i = 0; i < 4; i++) c.set(26 - i, 26, [0, 0, 0, 0]);
  c.set(26, 25, [0, 0, 0, 0]);
  // 夹着的半张纸条（露在外面一截，暗示有人查过）
  c.rect(18, 3, 9, 5, hex("#D8D2C4"));
  c.hLine(18, 3, 9, hex("#B0AA9C"));
  c.hLine(19, 5, 6, hex("#8A8578"));
  c.hLine(19, 7, 4, hex("#8A8578"));
  // 封皮上的题签（褪色的字，不是朱印）
  c.rect(9, 12, 12, 7, hex("#C8C0AE"));
  c.hLine(10, 14, 10, hex("#8A8578"));
  c.hLine(10, 16, 7, hex("#8A8578"));
  c.edge();
  return c;
}

// ---------------------------------------------------------------- 0.32.0 补齐的 16 张
//
// 行囊里要**一眼分得出这是什么**，所以这批按四类各给一套剪影：
//   货币（一堆钱）／材料（矿、草、皮——都是"一堆东西"）／
//   药与酒（瓶、碗、坛、葫芦——各有各的容器）／剧情道具（锈剑、号衣、腰牌、残页、毒酒）。
// 颜色照 15 §二：**绿＝毒**（毒酒、五毒残页都是绿），朱红只给朱印与伤药的标。

/** 铜钱：一撮散钱（后面两枚压着、前面一枚立着）。货币不能画成"一枚"，那是饰品。 */
function itemMoney() {
  const c = new Canvas(S, S);
  const cu = "#B08A4A";
  const cuHi = "#E8C878";
  const cuDk = "#775524";
  const hole = "#2E2A26";
  // 三枚**必须摆开、不许叠**：叠起来勾边会把它们连成一块金疙瘩（第一版就是这样，
  // 32×32 上读成"个金摆件"）。留 2–3 px 缝，勾边才有地方落。
  const coin = (cx, cy) => {
    c.ellipse(cx, cy, 6, 5, hex(cuDk));
    c.ellipse(cx, cy - 1, 5, 4, hex(cu));
    c.ellipse(cx - 1, cy - 1, 3, 2, hex(cuHi));
    c.rect(cx - 2, cy - 2, 4, 3, hex(hole));
  };
  coin(8, 24);
  coin(24, 24);
  coin(16, 13);
  c.edge();
  return c;
}

/** 铁矿石：一块糙石头 ＋ 断面上的三处金属反光（"这石头里有铁"）。 */
function itemIron() {
  const c = new Canvas(S, S);
  const rock = "#5E6A70";
  const rockHi = "#8A94A0";
  const rockDk = "#3E464C";
  c.ellipse(15.5, 18, 11, 9, hex(rock));
  c.ellipse(13, 15, 6, 4, hex(rockHi));
  c.ellipse(19, 22, 7, 5, hex(rockDk));
  c.rect(15, 24, 10, 1, hex(rockDk));
  for (const [x, y] of [[10, 18], [16, 13], [21, 20]]) {   // 金属反光
    c.set(x, y, hex("#E0E8F0"));
    c.set(x + 1, y + 1, hex("#A8B4BE"));
  }
  c.set(13, 12, hex("#F0F6FA"));
  c.edge();
  return c;
}

/** 草药：一束晒干的药草，麻绳扎着（材料类共用"一捆"的剪影）。 */
function itemHerb() {
  const c = new Canvas(S, S);
  const leaf = "#4E8A62";
  const leafHi = "#8ABE9E";
  const leafDk = "#2E5A46";
  const stem = "#8A7A55";
  // 叶在上、茎在下、**麻绳扎在腰上**。第一版把麻绳画成整条底边 → 整张读成
  // "一根柱子上顶着一丛草"（像棵树）；第二版五根细竖笔 → 读成一把刷子。
  // 第三版换成**三片两头尖的叶子**：叶形是"草药"唯一不用教的剪影。
  const leafAt = (cx, cy, dx, dy, len) => {
    for (let k = 0; k < len; k++) {
      const t = k / (len - 1);
      const half = Math.max(1, Math.round(Math.sin(t * Math.PI) * 3));
      const x = Math.round(cx + dx * k);
      const y = Math.round(cy + dy * k);
      c.hLine(x - half, y, half * 2 + 1, hex(k > len * 0.72 ? leafHi : leaf));
      c.set(x - half, y, hex(leafDk));
    }
  };
  leafAt(16, 18, 0, -1, 13);
  leafAt(15, 18, -0.5, -1, 12);
  leafAt(17, 18, 0.5, -1, 12);
  for (let i = 0; i < 4; i++) {            // 四支切齐的茎
    const x = 14 + i * 2;
    c.vLine(x, 19, 9, hex(i % 2 ? stem : leafDk));
  }
  c.rect(11, 17, 11, 3, hex("#8A6A46"));   // 麻绳
  c.hLine(11, 17, 11, hex("#B08A5A"));
  c.set(21, 18, hex("#6E5236"));
  c.edge();
  return c;
}

/** 兽皮：摊开的一张皮（四条腿 ＋ 尾巴），不是"一块布"。 */
function itemPelt() {
  const c = new Canvas(S, S);
  const fur = "#8A6A46";
  const furHi = "#B08A5A";
  const furDk = "#57402C";
  c.rect(7, 8, 18, 17, hex(fur));          // 皮身
  c.rect(7, 8, 18, 1, hex(furHi));
  c.rect(7, 24, 18, 1, hex(furDk));
  for (const [x, y] of [[5, 6], [22, 6], [5, 21], [22, 21]]) {   // 四条腿
    c.rect(x, y, 4, 5, hex(fur));
    c.hLine(x, y, 4, hex(furHi));
    c.set(x, y + 4, hex(furDk));
  }
  c.rect(14, 3, 4, 5, hex(fur));           // 头颈那一截
  c.hLine(14, 3, 4, hex(furHi));
  c.rect(15, 24, 2, 5, hex(furDk));        // 尾巴
  for (let i = 0; i < 5; i++) c.set(9 + i * 3, 14 + (i % 2) * 3, hex(furDk));   // 毛的斑纹
  c.edge();
  return c;
}

/**
 * 药瓶：金创药与回气散共用一支瓶（换色与标）。
 * **两者同框**，靠颜色分开：金创药＝素白瓶＋朱印（治伤），回气散＝青瓶＋三道气弧（内力）。
 */
function vial(body, dark, cap, mark) {
  const c = new Canvas(S, S);
  c.rect(12, 5, 8, 3, hex(cap));           // 木塞
  c.rect(11, 8, 10, 2, hex("#6E6248"));
  for (let y = 10; y <= 26; y++) {         // 瓶身：上窄下宽
    const w = 8 + Math.round((y - 10) * 0.34);
    const x = 16 - Math.floor(w / 2);
    c.hLine(x, y, w, hex(body));
    c.set(x, y, hex("#F0ECE2"));
    c.set(x + w - 1, y, hex(dark));
  }
  if (mark === "wound") {                  // 朱印：伤药
    c.rect(13, 14, 6, 6, hex("#A83A2E"));
    c.set(14, 15, hex(body));
    c.set(16, 17, hex(body));
  } else {                                 // 三道气弧：回气
    for (let i = 0; i < 3; i++) c.hLine(13 + i, 17 + i * 2, 6 - i, hex("#3E6E5A"));
  }
  c.hLine(9, 26, 14, hex(dark));
  c.edge();
  return c;
}

/** 金创药：素白瓷瓶 ＋ 朱印。 */
function itemPotionSmall() {
  return vial("#D8D2C4", "#B0AA9C", "#8A6A46", "wound");
}

/** 回气散：青瓷瓶 ＋ 三道气弧（与金创药同框，靠颜色分开）。 */
function itemPotionQi() {
  return vial("#C0D4D4", "#6E8A9A", "#3E5A7A", "qi");
}

/** 草药汤：一碗汤（碗 ＋ 绿汤面 ＋ 三缕热气）。 */
function itemMed01() {
  const c = new Canvas(S, S);
  const bowl = "#B0AA9C";
  const bowlDk = "#837C6E";
  c.ellipse(16, 23, 11, 4, hex(bowlDk));   // 碗底
  c.rect(6, 17, 20, 6, hex(bowl));
  c.hLine(6, 17, 20, hex("#E4DED2"));
  c.hLine(6, 22, 20, hex(bowlDk));
  c.ellipse(16, 17, 10, 3, hex(bowlDk));
  c.ellipse(16, 17, 9, 2, hex("#4E8A62")); // 汤面：绿（草药）
  c.set(13, 17, hex("#8ABE9E"));
  c.edge();
  // 热气**画在勾边之后**：勾边会把孤立的浅色像素包成一圈墨，三缕热气就成了三个黑点
  // （第一版就是这么糊的）。冒完热气不再有勾边，它们才像一缕一缕的。
  for (const x of [12, 16, 20]) {
    c.vLine(x, 11, 3, hex("#C8C2B4"));
    c.set(x + 1, 9, hex("#B0AA9C"));
  }
  return c;
}

/** 药酒：一个封着布的酒坛（坛口的布与麻绳是"药酒"与"毒酒"共用的坛形）。 */
function itemMed02() {
  const c = new Canvas(S, S);
  const jar = "#7A5B3E";
  const jarHi = "#A0764A";
  const jarDk = "#4E3A28";
  c.ellipse(16, 18, 10, 9, hex(jar));      // 坛身
  c.ellipse(13, 15, 5, 4, hex(jarHi));
  c.ellipse(19, 22, 6, 4, hex(jarDk));
  c.rect(12, 7, 8, 4, hex(jarDk));         // 坛口
  c.rect(11, 9, 10, 2, hex("#B0AA9C"));    // 封布
  c.hLine(11, 9, 10, hex("#D8D2C4"));
  c.hLine(11, 14, 17, hex("#8A6A46"));     // 麻绳
  c.hLine(11, 15, 17, hex("#6E5236"));
  c.rect(12, 20, 8, 7, hex("#C8C0AE"));    // 坛上的题签
  c.hLine(13, 22, 6, hex("#8A8578"));
  c.hLine(13, 24, 4, hex("#8A8578"));
  c.edge();
  return c;
}

/** 酒葫芦：两截葫芦 ＋ 塞子 ＋ 系绳。与"药酒坛"分开——**葫芦是随身物、坛是家里存的**。 */
function itemWineGourd() {
  const c = new Canvas(S, S);
  const g = "#C89A5B";
  const gHi = "#E0BE82";
  const gDk = "#8A6A38";
  c.ellipse(16, 21, 9, 8, hex(g));         // 下截
  c.ellipse(13, 18, 4, 4, hex(gHi));
  c.ellipse(16, 10, 6, 6, hex(g));         // 上截
  c.ellipse(14, 8, 3, 3, hex(gHi));
  c.rect(13, 11, 6, 6, hex(gDk));          // 腰：收进 5–6 px 才像葫芦，9 px 就是一坨
  c.rect(12, 13, 8, 2, hex(gDk));
  c.rect(13, 9, 6, 5, hex(g));
  c.rect(14, 4, 4, 3, hex("#8A6A46"));     // 塞子
  c.hLine(14, 4, 4, hex("#B08A5A"));
  c.set(21, 11, hex(gDk));                 // 系绳
  c.set(22, 13, hex(gDk));
  c.set(11, 23, hex(gDk));
  c.edge();
  return c;
}

/** 铁镐：斜柄 ＋ 弯头（镐头是一横，不是刀剑的斜刃）。 */
function itemPickaxe() {
  const c = new Canvas(S, S);
  const wood = "#8A6A46";
  const woodHi = "#B08A5A";
  const iron = "#8A94A0";
  for (let i = 0; i < 17; i++) {           // 柄：左下 → 右上
    const x = 7 + i;
    const y = 27 - i;
    c.set(x, y, hex(woodHi));
    c.set(x, y + 1, hex(wood));
    c.set(x, y + 2, hex("#57402C"));
  }
  // 镐头：**一根横铁、两头往下弯成尖**（只画一弯会读成钩子/镰刀——第一版就是这样）
  for (let i = 0; i < 13; i++) {
    const x = 18 + i;
    const dip = i < 3 ? 3 - i : (i > 9 ? i - 9 : 0);
    c.vLine(x, 6 + dip, 5, hex(iron));
    c.set(x, 6 + dip, hex("#E0E8F0"));
    c.set(x, 9 + dip, hex("#5E6A70"));
    c.set(x, 10 + dip, hex("#5E6A70"));
  }
  c.rect(23, 8, 3, 3, hex("#6E7A82"));     // 装柄的孔
  c.edge();
  return c;
}

/** 锈剑：**没有护手、没有鞘、刃上有缺口与锈斑**——和装备栏那把"锈月"要一眼分开。 */
function itemRustySword() {
  const c = new Canvas(S, S);
  const rust = "#8A6A52";
  const rustHi = "#B08A6A";
  const rustDk = "#553F30";
  // 断刃：只留十步，末端是**掰断的茬口**（不是剑尖）——"锈剑"是捡来的破烂，不是兵器
  for (let i = 0; i < 10; i++) {
    const x = 11 + i;
    const y = 22 - i;
    c.set(x, y, hex("#D8C0A0"));           // 刃口：还反着一点光（"锈而未烂"）
    c.set(x, y + 1, hex(rustHi));
    c.set(x, y + 2, hex(rust));
    c.set(x, y + 3, hex(rustDk));
  }
  for (let k = 0; k < 4; k++) c.set(21 + k, 12 + (k % 2), hex(rustDk));
  c.set(23, 13, hex(rustHi));
  // 护手：**垂直横过轴的 9 px**。这一笔是"这是剑"的关键——缺了它整张读成一根棍
  for (let k = -4; k <= 4; k++) {
    c.set(11 + k, 22 + k, hex("#6E6A63"));
    c.set(11 + k, 23 + k, hex("#4A4E52"));
  }
  c.set(7, 18, hex("#9EA6AC"));
  // 缠布条的柄：从护手往左下
  for (let k = 1; k <= 6; k++) {
    c.rect(10 - k, 22 + k, 2, 2, hex(k % 2 ? "#6E6248" : "#8A7A55"));
  }
  c.set(4, 28, hex("#57402C"));            // 柄头
  c.edge();
  return c;
}

/** 黑风寨号衣：**叠好的一件**（不是穿着的）＋ 一道黑风寨的号带。伪装道具要像"从哪儿顺来的"。 */
function itemBdUniform() {
  const c = new Canvas(S, S);
  const cloth = "#4A3A2C";
  const clothHi = "#6E5236";
  const clothDk = "#2E2622";
  for (let i = 0; i < 4; i++) {            // 叠着的四层
    const y = 9 + i * 5;
    c.rect(6, y, 20, 5, hex(i % 2 ? clothHi : cloth));
    c.hLine(6, y, 20, hex("#8A6A46"));
    c.hLine(6, y + 4, 20, hex(clothDk));
    c.set(6, y, [0, 0, 0, 0]);
    c.set(25, y + 4, [0, 0, 0, 0]);
  }
  c.rect(4, 12, 24, 3, hex("#8A3A2E"));    // 号带：扎在叠好的衣服上
  c.hLine(4, 12, 24, hex("#A83A2E"));
  c.set(20, 11, hex("#D96A28"));           // 号带上的火把橙记号（黑风寨那一套色）
  c.set(23, 11, hex("#D96A28"));
  c.edge();
  return c;
}

/** 黑风寨腰牌：一块木牌 ＋ 系绳孔 ＋ 火把橙的刻记。 */
function itemBdToken() {
  const c = new Canvas(S, S);
  const wood = "#6B5A46";
  const woodHi = "#8A7660";
  const woodDk = "#4A3E32";
  c.rect(8, 8, 16, 20, hex(wood));
  c.hLine(8, 8, 16, hex(woodHi));
  c.hLine(8, 27, 16, hex(woodDk));
  c.vLine(23, 8, 20, hex(woodDk));
  c.set(8, 8, [0, 0, 0, 0]);               // 四角削一削，不像砖头
  c.set(23, 8, [0, 0, 0, 0]);
  c.set(8, 27, [0, 0, 0, 0]);
  c.set(23, 27, [0, 0, 0, 0]);
  c.rect(14, 6, 4, 3, hex(woodDk));        // 系绳孔
  c.rect(15, 7, 2, 2, hex("#2E2622"));
  c.rect(12, 13, 8, 8, hex("#D96A28"));    // 刻记：黑风寨的火把橙
  c.rect(14, 15, 4, 4, hex(woodDk));
  c.set(13, 13, hex("#E8823A"));
  c.hLine(11, 24, 10, hex(woodDk));
  c.edge();
  return c;
}

/** 毒酒：**绿**（15 §二：绿色＝毒）。坛形与药酒同源，但封口与酒面都是绿的。 */
function itemPoisonWine() {
  const c = new Canvas(S, S);
  const jar = "#4A5A48";
  const jarHi = "#6E8A62";
  const jarDk = "#2E3C2E";
  c.ellipse(16, 18, 10, 9, hex(jar));
  c.ellipse(13, 15, 5, 4, hex(jarHi));
  c.ellipse(19, 22, 6, 4, hex(jarDk));
  c.rect(12, 6, 8, 5, hex(jarDk));
  c.rect(11, 8, 10, 2, hex("#4E8A62"));    // 封布：绿
  c.hLine(11, 8, 10, hex("#8ABE72"));
  // 坛口飘出的毒气（三缕，绿）——它与药酒最大的区别就在这一口
  for (const [x, y] of [[13, 5], [16, 3], [19, 5]]) {
    c.set(x, y, hex("#8ABE72"));
    c.set(x + 1, y - 1, hex("#A8D68C"));
  }
  c.hLine(11, 13, 17, hex("#4A3E32"));
  c.edge();
  return c;
}

/**
 * 残页／残卷两支共用一张纸的底，只换"墨色"与撕法——
 * **与已交的「遗篇残卷」要分开**：那一张是整幅竹简（第一章最高品质），这两张必须是**残**的。
 */
function tornPage(paper, paperDk, ink, tear) {
  const c = new Canvas(S, S);
  c.rect(7, 6, 18, 21, hex(paper));
  c.hLine(7, 6, 18, hex("#F0E8D0"));
  c.vLine(7, 6, 21, hex(paperDk));
  if (tear === "left") {                   // 左边撕掉一块
    for (let y = 14; y < 24; y++) {
      const cut = 7 + Math.round((y - 14) * 0.7);
      for (let x = 7; x < cut; x++) c.set(x, y, [0, 0, 0, 0]);
    }
  } else {                                 // 右边撕掉一块
    for (let y = 8; y < 19; y++) {
      const cut = 25 - Math.round((y - 8) * 0.7);
      for (let x = cut; x < 25; x++) c.set(x, y, [0, 0, 0, 0]);
    }
  }
  for (const y of [10, 13, 16, 19, 22]) {  // 字：三到五笔
    for (let x = 10; x < 21; x += 4) c.hLine(x, y, 2, hex(ink));
  }
  c.edge();
  return c;
}

/** 五毒秘籍残页：残页 ＋ 绿墨（五毒系一律绿）。 */
function itemScrollWudu() {
  const c = tornPage("#D8D2C4", "#B0AA9C", "#3E6E5A", "right");
  for (const [x, y] of [[10, 8], [20, 21], [9, 20]]) {   // 绿气渗出的点
    c.set(x, y, hex("#8ABE72"));
    c.set(x + 1, y + 1, hex("#4A7A46"));
  }
  return c;
}

/** 醉里乾坤残卷：残卷 ＋ **酒渍**（褐黄一圈），与五毒残页靠颜色分开。 */
function itemScrollDrunk() {
  const c = tornPage("#C8B48A", "#9A8A66", "#6E5236", "left");
  for (let y = 0; y < S; y++) {            // 酒渍：一块半透明的黄褐
    for (let x = 0; x < S; x++) {
      const d = Math.hypot(x - 17, y - 20);
      if (d < 7) c.blend(x, y, hex("#8A6A28"), (1 - d / 7) * 0.45);
    }
  }
  c.set(21, 12, hex("#6E5236"));           // 卷角
  c.edge();
  return c;
}

// ---------------------------------------------------------------- 四本秘籍（Q3：商店卖谱、研读学招）
//
// 这四本走的是**已经实现的 `item` 通道**（`source_type=shop` 改成"商店卖秘籍"）。出图口径三条：
//
// 1. **必须是"册"，不能像那三张纸**：`item_scroll_yipian`（整幅竹简）／`item_scroll_wudu`
//    （撕角残页）／`item_scroll_drunk`（酒渍残卷）都是散页，这四本是**线装成册**的书；
// 2. **也不许像 `item_bd_ledger`**：那本是"旧蓝布、磨白、起毛"的账册（设计要它一眼看不出重要），
//    秘籍反过来——**封皮整、题签在、有包角**，一看就是"装订过的东西"；
// 3. 四本彼此靠**学派色 ＋ 题签上的记号**分开（与武学图标同一套 `school_id` 取色，
//    见 `skills.js` 的 `SCHOOL_COLORS`）——**不是靠加花纹**。物品栏里这四本会排在一起，
//    花纹一多就糊成一摞一样的书。

/** 一册线装书：书脊 ＋ 封面 ＋ 书口（纸页）＋ 题签。`mark` 画在题签里，`band` 是封面上的横箍。 */
function bookIcon(cover, coverHi, coverDk, mark, band) {
  const c = new Canvas(S, S);
  const paper = "#D8D2C4";
  const paperDk = "#B0AA9C";
  // 书口（右侧那一叠纸页）：画在最右边，露出书是"有厚度"的
  c.rect(24, 6, 3, 20, hex(paper));
  for (let y = 8; y < 26; y += 3) c.hLine(24, y, 3, hex(paperDk));
  // 书脊：左侧一条深色
  c.rect(5, 4, 4, 24, hex(coverDk));
  c.vLine(5, 4, 24, hex(coverHi));
  for (const y of [9, 15, 21]) c.hLine(5, y, 4, hex("#3E3428"));   // 装订线
  // 封面
  c.rect(9, 4, 15, 24, hex(cover));
  c.hLine(9, 4, 15, hex(coverHi));
  c.hLine(9, 27, 15, hex(coverDk));
  if (band) {                              // 军械／官册那种横箍
    c.rect(9, 20, 15, 3, hex(coverDk));
    c.hLine(9, 20, 15, hex(coverHi));
    c.set(9, 21, hex("#3E3428"));
    c.set(23, 21, hex("#3E3428"));
  }
  // 题签：浅色的一块，四边留出封面
  c.rect(11, 8, 11, 10, hex(paper));
  c.frame(11, 8, 11, 10, hex(paperDk));
  mark(c);
  // 包角：右上／右下两块（线装书常见的铜/布包角），也是"装订过"的信号
  for (const y of [4, 25]) {
    c.rect(21, y, 3, 3, hex(coverDk));
    c.set(21, y, hex(coverHi));
  }
  c.edge();
  return c;
}

/** 沉沙掌谱：题签上是「土石」三横（与沉沙别的图标同一套母题语言）。 */
function itemBookChensha() {
  const ink = hex("#57402C");
  return bookIcon("#8A6A46", "#B08A5A", "#57402C", (c) => {
    c.hLine(13, 11, 7, ink);
    c.hLine(15, 14, 3, ink);
    c.hLine(12, 16, 9, ink);
  });
}

/**
 * 铁枪诀谱：题签上是一杆立枪，封面加一道**横箍**（驿站枪把式那本，像兵器谱）。
 * 它和太虚功谱原本都是蓝灰——**摆在行囊里会认混**，所以留钢灰 ＋ 横箍，
 * 太虚让给青瓷色（下面那本）。
 */
function itemBookTieqiang() {
  const ink = hex("#3E4A56");
  return bookIcon("#5E6E7A", "#8A9CA8", "#3E4A56", (c) => {
    c.vLine(16, 10, 8, ink);
    c.rect(15, 10, 3, 2, ink);
    c.set(17, 9, ink);
    c.hLine(13, 12, 7, ink);      // 枪缨那一横
  }, true);
}

/** 烈火掌谱：题签上是一簇火（16 §4.3 的火把橙那一族）。 */
function itemBookLiehuo() {
  const ink = hex("#6E2A1C");
  const flame = hex("#E8823A");
  return bookIcon("#A8432A", "#C85A32", "#6E2A1C", (c) => {
    c.vLine(16, 11, 6, ink);
    c.set(14, 13, ink);
    c.set(18, 13, ink);
    c.set(16, 10, flame);
    c.hLine(13, 16, 7, ink);
  });
}

/** 太虚功谱：题签上是三道涟漪（太虚＝气纹）。封面走**青瓷**——与铁枪的钢灰分开。 */
function itemBookTaixu() {
  const ink = hex("#3E5A5A");
  return bookIcon("#5E8A8A", "#8AB0B0", "#3E5A5A", (c) => {
    c.hLine(13, 11, 7, ink);
    c.hLine(14, 14, 5, ink);
    c.hLine(15, 16, 3, ink);
  });
}

/** 相对 `assets/` 的路径 → 画布。 */
function buildAll() {
  return {
    "icons/item/item_treasure_map.png": itemTreasureMap(),
    "icons/item/item_scroll_yipian.png": itemScrollYipian(),
    "icons/item/item_bd_ledger.png": itemBdLedger(),
    "icons/item/item_money.png": itemMoney(),
    "icons/item/item_iron.png": itemIron(),
    "icons/item/item_herb.png": itemHerb(),
    "icons/item/item_pelt.png": itemPelt(),
    "icons/item/item_potion_small.png": itemPotionSmall(),
    "icons/item/item_potion_qi.png": itemPotionQi(),
    "icons/item/item_med_01.png": itemMed01(),
    "icons/item/item_med_02.png": itemMed02(),
    "icons/item/item_wine_gourd.png": itemWineGourd(),
    "icons/item/item_pickaxe.png": itemPickaxe(),
    "icons/item/item_rusty_sword.png": itemRustySword(),
    "icons/item/item_bd_uniform.png": itemBdUniform(),
    "icons/item/item_bd_token.png": itemBdToken(),
    "icons/item/item_poison_wine.png": itemPoisonWine(),
    "icons/item/item_scroll_wudu.png": itemScrollWudu(),
    "icons/item/item_scroll_drunk.png": itemScrollDrunk(),
    "icons/item/item_book_chensha.png": itemBookChensha(),
    "icons/item/item_book_tieqiang.png": itemBookTieqiang(),
    "icons/item/item_book_liehuo.png": itemBookLiehuo(),
    "icons/item/item_book_taixu.png": itemBookTaixu(),
  };
}

module.exports = {
  buildAll, itemTreasureMap, itemScrollYipian, itemBdLedger,
  itemMoney, itemIron, itemHerb, itemPelt,
  itemPotionSmall, itemPotionQi, itemMed01, itemMed02,
  itemWineGourd, itemPickaxe, itemRustySword,
  itemBdUniform, itemBdToken, itemPoisonWine, itemScrollWudu, itemScrollDrunk,
  itemBookChensha, itemBookTieqiang, itemBookLiehuo, itemBookTaixu,
};
