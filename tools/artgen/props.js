// 地图上会被代码读到的道具贴图（`assets/sprites/props/`）与角色占位。
//
// 这些名字是**代码认的**（`chest.gd` / `trigger_point.gd` / 场景里的 npc 占位），
// 所以只能同名替换、不能改名：
//   chest_<copper|silver|gold>[_open].png  ← `Chest.SPRITE_DIR`
//   brazier_<off|on>.png                   ← `TriggerPoint.BRAZIER_*`
//   npc_placeholder.png                    ← 清风驿等场景里的 NPC 占位

"use strict";

const { Canvas, hex, mix, shade } = require("./pixel");
const { tileChest, THEMES } = require("./tiles");

const S = 32;
const INK = "#1E2224";

/** 火盆：青铜盆 + 炭。灭＝冷灰，燃＝暖橙并带一圈外溢的光。 */
function brazier(lit) {
  const c = new Canvas(S, S);
  const bronze = lit ? "#8A6A46" : "#5E4A38";
  const bronzeLight = lit ? "#A8875C" : "#6E5A46";
  if (lit) {
    // 外溢的光：先铺一圈低透明度的暖色，火盆才有「照亮周围」的感觉
    for (let y = 0; y < S; y++) {
      for (let x = 0; x < S; x++) {
        const d = Math.hypot(x - 15.5, y - 14.5);
        if (d <= 15) c.blend(x, y, hex("#D96A28"), Math.pow(1 - d / 15, 2) * 0.5);
      }
    }
  }
  // 三足
  for (const [x, y] of [[8, 24], [15, 26], [22, 24]]) c.rect(x, y, 2, 5, hex(INK));
  // 盆身
  c.ellipse(16, 18, 12, 9, hex(bronze));
  c.ellipse(16, 17, 11, 8, hex(bronzeLight));
  c.ellipse(16, 20, 10, 6, hex(bronze));
  // 盆口
  c.ellipse(16, 14, 10, 4, hex(shade(bronze, -0.25)));
  c.ellipse(16, 14, 8, 3, hex(lit ? "#3A2418" : "#2A2622"));
  // 炭
  for (let i = 0; i < 7; i++) {
    const x = 9 + ((i * 5) % 15);
    const y = 13 + ((i * 3) % 4);
    c.rect(x, y, 2, 2, hex(lit ? "#B8481C" : "#3E3A36"));
    if (lit) c.set(x, y, hex("#E8A33D"));
  }
  if (lit) {
    // 火苗：三簇高低不一
    for (const [x, h] of [[13, 7], [16, 10], [19, 6]]) {
      for (let k = 0; k < h; k++) {
        const w = Math.max(1, Math.round((h - k) / 3));
        c.hLine(x - w + 1, 12 - k, w * 2 - 1, hex(k > h - 3 ? "#E8A33D" : "#F0C060"));
      }
      c.set(x, 12 - h, hex("#F8E8B0"));
    }
  }
  c.outline(hex(INK));
  return c;
}

/** NPC 占位：人形剪影。15 §4.6 明确「初期不能用纯空白，统一用人形剪影」。 */
function npcPlaceholder() {
  const c = new Canvas(S, S);
  const robe = "#4A5257";
  const robeLight = "#5E6A70";
  const skin = "#C8A886";
  // 影子
  c.ellipse(16, 29, 9, 3, [0, 0, 0, 70]);
  // 下摆
  for (let y = 20; y < 30; y++) {
    const w = 6 + Math.round((y - 20) * 0.7);
    c.rect(16 - w, y, w * 2, 1, hex(robe));
    c.set(16 - w, y, hex(robeLight));
  }
  // 身子
  c.rect(9, 12, 14, 10, hex(robe));
  c.rect(9, 12, 4, 10, hex(robeLight));
  // 头
  c.rect(11, 4, 10, 8, hex(robe));
  c.rect(12, 6, 8, 6, hex(skin));
  c.rect(12, 6, 8, 2, hex(robe));
  // 腰带（暖色提亮——可交互的人在低饱和里要跳出来）
  c.rect(9, 19, 14, 2, hex("#A8763A"));
  c.outline(hex(INK));
  return c;
}

/**
 * 废弃渡口的**封渡木桩**（`prop_ferry_pile.png`，设计 0.32.0 后点的一件）。
 *
 * 第一章的封锁表现是「**水位 ＋ 断桥 ＋ 封渡木牌**」三样合起来：
 * 水位与断桥是**地形**（归地编铺瓦片），这一张只出**木桩那一根**——桩上钉一块木牌，
 * 牌面留朱印不写字（32×32 里塞不下「封渡」两个字；木牌 ＋ 朱印已经是"官府封的"这个意思）。
 *
 * 画法上要和「废弃渡口」的视觉签名同一套（16 §4.6：**灰蓝水汽 ＋ 朽木断桩**）：
 * 木头用朽木的灰褐、桩身要有水线（下半截泡得发黑）——
 * **水线是这张图的信息**：玩家据此看出水位涨过、这里过不去了。
 */
function ferryPile() {
  const c = new Canvas(S, S);
  const wood = "#6B5A46";
  const woodLight = "#8A7660";
  const woodDark = "#4A3E32";
  const soaked = "#3A3630";                       // 泡在水里的半截：发黑
  const waterLine = "#7A8E9A";

  // 桩：细长、略歪（埋泥里多年），桩顶削尖
  for (let y = 4; y < 30; y++) {
    const lean = Math.round((y - 4) * 0.10);
    const x = 12 + lean;
    c.vLine(x, y, 1, hex(woodLight));
    c.vLine(x + 1, y, 2, hex(wood));
    c.vLine(x + 3, y, 1, hex(woodDark));
  }
  c.hLine(14, 4, 3, hex(woodLight));              // 桩顶（削尖：比桩身窄）
  c.set(15, 3, hex(woodLight));

  // 钉在桩上的木牌：横着钉，牌面留一枚朱印（32×32 塞不下「封渡」两个字；
  // 木牌 ＋ 朱印已经表达了"官府封的"这件事）
  c.rect(13, 9, 14, 8, hex(wood));
  c.hLine(13, 9, 14, hex(woodLight));
  c.hLine(13, 16, 14, hex(woodDark));
  c.vLine(26, 9, 8, hex(woodDark));
  c.rect(20, 11, 5, 4, hex("#A83A2E"));
  c.set(21, 12, hex("#D8D2C4"));
  c.set(23, 13, hex("#D8D2C4"));
  c.set(14, 10, hex("#8A8578"));                  // 两枚钉，钉在桩上
  c.set(14, 14, hex("#8A8578"));

  // 水线以下：泡黑（**水线是这张图的信息**——玩家据此看出水位涨过）
  for (let y = 22; y < 30; y++) {
    const lean = Math.round((y - 4) * 0.10);
    const x = 12 + lean;
    c.vLine(x, y, 4, hex(soaked));
    c.set(x, y, hex("#4E5A56"));
  }
  // 水面：几笔短横线就够，别铺一整条（水面那层归地编）
  for (const [x, y, w] of [[3, 25, 6], [22, 24, 6], [5, 28, 5], [20, 27, 5]]) {
    c.hLine(x, y, w, hex(waterLine));
    c.set(x, y, hex("#A8B8C0"));
  }
  // 半沉的断桩头："这儿本来能过"
  c.rect(22, 28, 4, 2, hex(woodDark));
  c.hLine(22, 28, 4, hex(soaked));
  c.outline(hex(INK));
  return c;
}

/** 产出全部道具贴图：文件名 → 画布。 */
function buildAll() {
  const pal = THEMES.heifengzhai.pal;
  const out = {};
  for (const tier of ["copper", "silver", "gold"]) {
    out[`chest_${tier}.png`] = tileChest(pal, tier, false);
    out[`chest_${tier}_open.png`] = tileChest(pal, tier, true);
  }
  out["brazier_off.png"] = brazier(false);
  out["brazier_on.png"] = brazier(true);
  out["npc_placeholder.png"] = npcPlaceholder();
  out["prop_ferry_pile.png"] = ferryPile();
  return out;
}

module.exports = { buildAll, brazier, npcPlaceholder, ferryPile };
