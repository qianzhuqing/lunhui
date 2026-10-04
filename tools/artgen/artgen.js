// 墨色江湖美术生成器：一条命令把瓦片集／图标／道具／区域地图预览落到仓库里。
//
//   node tools/artgen/artgen.js            rem 只出贴图（瓦片集 + 图标 + 道具）
//   node tools/artgen/artgen.js --maps     rem 顺带出 docs/dev/images 下的区域地图预览
//   node tools/artgen/artgen.js --out DIR  rem 落到别处（改图时先看效果，别动仓库）
//
// 为什么是「生成」而不是「导入素材」：这台机器没有 Godot、没有 Python、也没有联网，
// 而像素画的每一笔都要求可复现、可回改。生成器本身就是美术的源文件——
// 改一处颜色或一个瓦片，重跑一次，五套图集与所有图标一起跟上，不会出现
// 「手改了一张 PNG、下一批忘了改」的漂移。
//
// 换真美术的做法：把同名 PNG 覆盖掉即可，场景与代码一行不改（15 §六）。

"use strict";

const fs = require("fs");
const path = require("path");
const { encodePNG } = require("./pixel");
const { THEMES, buildAtlas, buildFogSeamless } = require("./tiles");
const { buildAll: buildIcons } = require("./icons");
const { buildAll: buildProps } = require("./props");
const { buildAll: buildActors } = require("./actors");
const { buildAll: buildItems } = require("./items");
const { buildAll: buildEquips } = require("./equips");
const { buildAll: buildUi } = require("./ui");
const { buildAll: buildSkills } = require("./skills");
const { buildAll: buildStatus } = require("./status");
const { buildAll: buildBuff } = require("./buff");
const { buildAll: buildPortraits } = require("./portraits");

const REPO = path.resolve(__dirname, "..", "..");

const args = process.argv.slice(2);
const withMaps = args.includes("--maps");
const outIndex = args.indexOf("--out");
const REPO_OUT = outIndex >= 0 ? path.resolve(args[outIndex + 1]) : REPO;

/**
 * 区域地图预览：一个区域一张，直接贴进 docs/dev/地图搭建说明.md。
 * 黑风寨三层堆在同一张场景图里，所以按格裁成三张——与文档里原来那三张图一一对位。
 */
const MAP_IMAGES = [
  { file: "overworld.tscn", out: "墨色江湖_大地图.png", scale: 2 },
  // 开局真实状态：Fog 层只让 reveal_on_map = 1 的三处露出来
  { file: "overworld.tscn", out: "墨色江湖_大地图_开局迷雾.png", scale: 2, withFog: true },
  { file: "scene_qingfengyi.tscn", out: "墨色江湖_区域_清风驿.png", scale: 2 },
  // 三层在场景里是纵向堆叠的：房间行落在 y 2–29／31–51／58–82（见 build_heifengzhai.gd 的 Rect2i）
  { file: "scene_heifengzhai.tscn", out: "墨色江湖_区域_黑风寨一层.png", scale: 2, cells: [0, 0, 40, 30] },
  { file: "scene_heifengzhai.tscn", out: "墨色江湖_区域_黑风寨二层.png", scale: 2, cells: [0, 30, 40, 30] },
  { file: "scene_heifengzhai.tscn", out: "墨色江湖_区域_黑风寨三层.png", scale: 2, cells: [0, 54, 40, 30] },
  { file: "scene_cave.tscn", out: "墨色江湖_区域_塌陷山洞.png", scale: 2 },
  { file: "scene_huangcun.tscn", out: "墨色江湖_区域_荒村.png", scale: 2 },
  { file: "scene_ferry_locked.tscn", out: "墨色江湖_区域_废弃渡口.png", scale: 2 },
];

let written = 0;
let bytes = 0;

function write(relative, buffer) {
  const target = path.join(REPO_OUT, relative);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, buffer);
  written += 1;
  bytes += buffer.length;
  return target;
}

function writeCanvas(relative, canvas) {
  return write(relative, encodePNG(canvas));
}

// 瓦片集：一套主题一张图集，图集坐标与 map_kit.gd 的 T_* 常量一一对应
for (const theme of Object.keys(THEMES)) {
  writeCanvas(`assets/tilesets/ink_jianghu/${theme}/tilemap_packed.png`, buildAtlas(theme));
}
// 迷雾：可遮罩的云雾贴图（07 §11 第 5 条点名要的那张）
writeCanvas("assets/tilesets/ink_jianghu/fog_tile.png", buildFogSeamless());

// 大地图图标（文件名必须与 map_region.icon 一致，灰态是 <id>_dim）
for (const [name, canvas] of Object.entries(buildIcons())) {
  writeCanvas(`assets/sprites/icons/${name}`, canvas);
}

// 地图上会被代码读到的道具与 NPC 占位（同名替换）
for (const [name, canvas] of Object.entries(buildProps())) {
  writeCanvas(`assets/sprites/props/${name}`, canvas);
}

// NPC 头像与敌人剪影（路径由 actors.js 给出，都是相对 assets/ 的）
for (const [relative, canvas] of Object.entries(buildActors())) {
  writeCanvas(`assets/${relative}`, canvas);
}

// 物品图标（15 §六：assets/icons/item/<item_id>.png）
for (const [relative, canvas] of Object.entries(buildItems())) {
  writeCanvas(`assets/${relative}`, canvas);
}

// 装备图标（15 §六：assets/icons/equip/<equip_id>.png）——与物品图标同框，语言也同一套
for (const [relative, canvas] of Object.entries(buildEquips())) {
  writeCanvas(`assets/${relative}`, canvas);
}

// UI 贴图（15 §六：assets/ui/<panel>/）——八个浮层共用的面板底板九宫格 ＋ 状态条临界表现
for (const [relative, canvas] of Object.entries(buildUi())) {
  writeCanvas(`assets/${relative}`, canvas);
}

// 武学图标（15 §六：assets/icons/skill/<skill_id>.png）
for (const [relative, canvas] of Object.entries(buildSkills())) {
  writeCanvas(`assets/${relative}`, canvas);
}

// 异常状态图标（15 §六／Q80：assets/icons/status/<icon>.png）——战斗界面「增益减益」那排 chip
for (const [relative, canvas] of Object.entries(buildStatus())) {
  writeCanvas(`assets/${relative}`, canvas);
}

// 增益图标（08 §181／15 §4.3：assets/icons/buff/<buff_def.icon>.png）——同一条 chip 列表，
// 与异常共用 `badge.js` 那套「亮底＋深记号」骨架，颜色只走增益蓝
for (const [relative, canvas] of Object.entries(buildBuff())) {
  writeCanvas(`assets/${relative}`, canvas);
}

// 立绘占位（设计 0.19.1）：原生 80×120 的人形剪影，显示时 4 倍放大到 320×480。
// 按**角色 id** 命名：角色面板与创建界面都按 char_id 取，美术出图后同名替换。
for (const [name, canvas] of Object.entries(buildPortraits())) {
  writeCanvas(`assets/sprites/portraits/${name}`, canvas);
}

// 区域地图预览：直接读 scenes/maps/*.tscn 的瓦片数据画出来，不是另画的概念图
if (withMaps) {
  const { renderScene, cropCells } = require("./render_maps");
  for (const spec of MAP_IMAGES) {
    const scenePath = path.join(REPO, "scenes", "maps", spec.file);
    const canvas = spec.cells
      ? cropCells(scenePath, spec.cells, spec.scale)
      : (renderScene(scenePath, spec.scale, { withFog: spec.withFog }) || {}).canvas;
    if (!canvas) {
      console.error(`  跳过 ${spec.file}：读不出主题`);
      continue;
    }
    writeCanvas(`docs/dev/images/${spec.out}`, canvas);
  }
  writeCanvas("docs/dev/images/墨色江湖_图标总览.png", buildIconSheet());
  writeCanvas("docs/dev/images/墨色江湖_NPC头像与敌人剪影.png", buildActorSheet());
  writeCanvas("docs/dev/images/墨色江湖_武学图标.png", buildSkillSheet());
}

console.log(`artgen：写了 ${written} 个文件，共 ${(bytes / 1024).toFixed(1)} KB`);
console.log(`  输出根目录：${REPO_OUT}`);

/**
 * 图标／道具总览：格子太小看不出一致性，拼一张 5 倍图给美术与策划看
 * （15 §4.3「同一类别内构图取齐」这条只能并排看才知道做没做到）。
 */
function buildIconSheet() {
  const { Canvas } = require("./pixel");
  const icons = buildIcons();
  const props = buildProps();
  const ids = Object.keys(icons).filter((name) => !name.includes("_dim"));
  const dims = Object.keys(icons).filter((name) => name.includes("_dim"));
  const rows = [ids, dims, Object.keys(props).filter((n) => n.startsWith("chest")),
    Object.keys(props).filter((n) => !n.startsWith("chest"))];
  const cell = 40;
  const cols = Math.max(...rows.map((row) => row.length));
  const sheet = new Canvas(cols * cell, rows.length * cell);
  rows.forEach((row, r) => {
    row.forEach((name, c) => sheet.blit(icons[name] || props[name], c * cell + 4, r * cell + 4));
  });
  return sheet.scaled(5);
}

/**
 * 头像／剪影总览：头像要**并排看**才知道会不会互相认错（7 个 NPC 都在同一个小镇里），
 * 剪影要并排看才知道「山寨喽啰 vs 别派弟子」拉开没拉开（15 §4.2 的硬要求）。
 */
function buildActorSheet() {
  const { Canvas } = require("./pixel");
  const actors = buildActors();
  const items = buildItems();
  const avatars = Object.keys(actors).filter((k) => k.includes("avatars"));
  const enemies = Object.keys(actors).filter((k) => !k.includes("avatars"));
  const itemKeys = Object.keys(items);
  const rows = [avatars, enemies.concat(itemKeys)];
  const cell = 40;
  const cols = Math.max(...rows.map((r) => r.length));
  const sheet = new Canvas(cols * cell, rows.length * cell);
  rows.forEach((row, r) => {
    row.forEach((key, i) => {
      const canvas = actors[key] || items[key];
      sheet.blit(canvas, i * cell + 4, r * cell + 4);
    });
  });
  return sheet.scaled(5);
}

/**
 * 武学图标要**并排看**：设计要求「它们彼此要像一套，别各画一个风格」，
 * 而「像不像一套」只有摆在一起才判得出来（内功是气环／丹田息／底板那三笔，
 * 招式是 45° 轴／残影方向／勾边那三笔）。
 *
 * 65 张（内功 30 ＋ 招式 35）排一行会有 1.5 万 px 宽，所以**按 16 张一行折行**。
 */
function buildSkillSheet() {
  const { Canvas } = require("./pixel");
  const skills = buildSkills();
  const keys = Object.keys(skills);
  const cell = 40;
  const perRow = 16;
  const rows = Math.ceil(keys.length / perRow);
  const sheet = new Canvas(Math.min(keys.length, perRow) * cell, rows * cell);
  keys.forEach((k, i) => {
    sheet.blit(skills[k], (i % perRow) * cell + 4, Math.floor(i / perRow) * cell + 4);
  });
  return sheet.scaled(6);
}
