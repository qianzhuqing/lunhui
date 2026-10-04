// 《轮回》墨色江湖瓦片集：按主题画 32×32 packed 图集。
//
// 图集坐标**必须**与 `tools/mapgen/map_kit.gd` 里的 T_* 常量一一对应——
// 这样换美术就只是换一张 PNG（地图脚本、场景、碰撞数据一行都不用动），
// 也就是 15_美术风格需求 §六「按同名替换，不改代码」那条约定。
//
// 风格口径来自 15_美术风格需求（低饱和、光源统一自左上、朱红只给血／灯笼／旗帜／敌意）
// 与 16_区域美术设定（每个区域一套主色 + 材质语言，相邻区域不许撞）。

"use strict";

const { Canvas, hex, mix, shade, noise, rnd, encodePNG } = require("./pixel");

const TILE = 32;
const ATLAS_COLS = 12;
const ATLAS_ROWS = 11;

// ---------------------------------------------------------------- 调色板

/** 全部主题共用一份「键」，各主题只覆盖自己不同的色值。 */
const BASE = {
  ground: "#6E6A45", groundDark: "#5A5838", groundLight: "#7E7A52",
  soil: "#8A6A46", soilDark: "#6B5136", soilLight: "#9E7C54",
  stone: "#7A7570", stoneDark: "#5E5A55", stoneLight: "#918C85",
  floor: "#8A8272", floorDark: "#6E675A", floorLight: "#9E9584",
  rock: "#4E5A5E", rockDark: "#333B3E", rockLight: "#63706F",
  pine: "#3E5A46", pineDark: "#2A3E30", pineLight: "#4E6E56",
  leaf: "#4A6248", leafDark: "#33472F", leafLight: "#5E7A58",
  autumn: "#A87A3A", autumnDark: "#7A5A28", autumnLight: "#C8A24A",
  trunk: "#4A3A2C", trunkDark: "#2E241A", trunkLight: "#5E4A38",
  roofA: "#5B6B70", roofADark: "#3F4B4F", roofALight: "#78888D",
  roofB: "#8A6A46", roofBDark: "#5E4830", roofBLight: "#A8875C",
  wall: "#D8D2C4", wallDark: "#B0AA9C", wallShade: "#948E80",
  wallAlt: "#6B5236", wallAltDark: "#4A3A2C", wallAltLight: "#836546",
  wood: "#6B5236", woodDark: "#4A3A2C", woodLight: "#8A6A46",
  metal: "#7A8288", metalDark: "#565E64", metalLight: "#9EA6AC",
  copper: "#A8763A", copperLight: "#C89A5A",
  silver: "#9EA6AC", silverLight: "#C4CCD2",
  gold: "#C8A24A", goldLight: "#E0C070",
  lamp: "#E8A33D", lampDark: "#B87A22",
  red: "#A83A2E", redDark: "#7A2A20",
  water: "#4A6472", waterDark: "#334A56",
  ink: "#1E2224",
};

function palette(overrides) {
  return Object.assign({}, BASE, overrides);
}

/**
 * 五套主题（07 §8.6）。每套给一组**区域主色 + 材质语言**（16 §二、§五）：
 * `groundStyle` 决定地表怎么画，`wallStyle` 决定构筑物的材质，
 * `roofA/roofB` 决定两套屋顶（民居用 A、官式／山寨用 B）。
 */
const THEMES = {
  // 大地图：落雁坡一带的江南野地。开阔秋坡 + 松林，唯一允许出现「暖色小镇」的地图。
  jiangnan_wild: {
    label: "大地图·江南野地（落雁坡）",
    groundStyle: "grass",
    wallStyle: "plaster",
    floorStyle: "paving",
    detail: "dry",
    pal: palette({
      ground: "#6E6A45", groundDark: "#575535", groundLight: "#827E54",
      soil: "#8A6A46", soilDark: "#6B5136", soilLight: "#A08053",
      roofA: "#5B6B70", roofADark: "#3F4B4F", roofALight: "#78888D",
      roofB: "#4E5E64", roofBDark: "#354246", roofBLight: "#68787E",
    }),
  },
  // 清风驿：全图唯一的暖色安全区。青瓦白墙 + 木构 + 灯火，石板与夯土铺地。
  town: {
    label: "清风驿",
    groundStyle: "plain",
    wallStyle: "plaster",
    floorStyle: "paving",
    detail: "dust",
    pal: palette({
      ground: "#A89060", groundDark: "#8A7349", groundLight: "#BCA574",
      soil: "#A89060", soilDark: "#8A7349", soilLight: "#BCA574",
      floor: "#7A7570", floorDark: "#5E5A55", floorLight: "#918C85",
      stone: "#7A7570", stoneDark: "#5E5A55", stoneLight: "#918C85",
      lamp: "#E8A33D",
    }),
  },
  // 黑风寨：粗原木 + 毛石石基，军事化、少装饰，整体压暗靠火把提亮。
  heifengzhai: {
    label: "黑风寨",
    groundStyle: "earth",
    wallStyle: "masonry",
    floorStyle: "soil",
    detail: "grim",
    pal: palette({
      ground: "#5A4A3A", groundDark: "#453627", groundLight: "#6B5844",
      soil: "#5A4636", soilDark: "#423226", soilLight: "#6E5844",
      floor: "#5E5446", floorDark: "#443C32", floorLight: "#6E6456",
      stone: "#6E6A63", stoneDark: "#4E4A45", stoneLight: "#85807A",
      wall: "#6E6A63", wallDark: "#4E4A45", wallShade: "#5A554F",
      roofA: "#4A3A2C", roofADark: "#2E241A", roofALight: "#634C38",
      roofB: "#6B5236", roofBDark: "#4A3A2C", roofBLight: "#836546",
      lamp: "#D96A28", lampDark: "#A04818",
      leaf: "#3A4436", leafDark: "#2A322A", leafLight: "#4A5644",
      pine: "#33452F", pineDark: "#243020", pineLight: "#435739",
    }),
  },
  // 塌陷山洞：逼仄的青灰岩壁 + 一线天，无任何构筑物。
  cave: {
    label: "塌陷山洞",
    groundStyle: "rock",
    wallStyle: "masonry",
    floorStyle: "gravel",
    detail: "wet",
    pal: palette({
      ground: "#3E4A4E", groundDark: "#2E383C", groundLight: "#4E5A5E",
      soil: "#4A4238", soilDark: "#332E28", soilLight: "#5A5044",
      floor: "#454039", floorDark: "#332F2A", floorLight: "#565046",
      stone: "#4E5A5E", stoneDark: "#333B3E", stoneLight: "#63706F",
      wall: "#4E5A5E", wallDark: "#333B3E", wallShade: "#3E4A4E",
      roofA: "#3E4A4E", roofADark: "#2A3438", roofALight: "#4E5A5E",
      roofB: "#4A4238", roofBDark: "#332E28", roofBLight: "#5A5044",
      leaf: "#46543F", leafDark: "#333E2E", leafLight: "#56644D",
      pine: "#46543F", pineDark: "#333E2E", pineLight: "#56644D",
      autumn: "#5A5A44", autumnDark: "#3E3E2E", autumnLight: "#6E6A52",
    }),
  },
  // 荒村 / 废弃渡口：焦黑断墙 + 枯树 + 无人，冷、暗、荒废。
  village: {
    label: "荒村／废弃渡口",
    groundStyle: "ash",
    wallStyle: "plaster",
    floorStyle: "soil",
    detail: "ruin",
    pal: palette({
      ground: "#6E6448", groundDark: "#57503A", groundLight: "#80754F",
      soil: "#5A5040", soilDark: "#443C30", soilLight: "#6E6450",
      stone: "#7A7570", stoneDark: "#5E5A55", stoneLight: "#918C85",
      floor: "#6E6448", floorDark: "#57503A", floorLight: "#80754F",
      wall: "#8A8478", wallDark: "#68625A", wallShade: "#524E48",
      roofA: "#5E5A55", roofADark: "#42403C", roofALight: "#78736C",
      roofB: "#6B5236", roofBDark: "#4A3A2C", roofBLight: "#836546",
      leaf: "#6E6440", leafDark: "#4E4830", leafLight: "#857A50",
      pine: "#3A4436", pineDark: "#2A322A", pineLight: "#4A5644",
      autumn: "#8A7A55", autumnDark: "#5E5440", autumnLight: "#A89460",
      water: "#5A6E7A", waterDark: "#3E5058",
    }),
  },
  // 石隙迷窟：窄岩缝里的一线天光（16 §4.8）。与塌陷山洞同为岩洞，
  // 靠**岩壁形态**（笔直有裂缝 vs 破碎砌块）与**光**（一线天光 vs 几乎无光）区分。
  shixi: {
    label: "石隙迷窟",
    groundStyle: "rock",
    wallStyle: "crack",
    floorStyle: "gravel",
    detail: "wet",
    pal: palette({
      ground: "#3E464C", groundDark: "#2E363C", groundLight: "#505A60",
      soil: "#5A4A3C", soilDark: "#443830", soilLight: "#6E5A48",
      floor: "#3A4248", floorDark: "#2A3238", floorLight: "#4A545C",
      stone: "#4A5258", stoneDark: "#343C42", stoneLight: "#5E686E",
      wall: "#4A5258", wallDark: "#343C42", wallShade: "#3E464C",
      // 前朝石台与裂缝渗水共用一点幽蓝，是全区唯一的冷亮色
      water: "#3E5A7A", waterDark: "#2E445C",
      leaf: "#4A5A44", leafDark: "#36422F", leafLight: "#5C6E52",
      pine: "#4A5A44", pineDark: "#36422F", pineLight: "#5C6E52",
      autumn: "#6E5A48", autumnDark: "#4A3C30", autumnLight: "#8A7258",
    }),
  },
};

// ---------------------------------------------------------------- 地表

/** 底色 + 脏感。所有地面瓦片都整格不透明（地面层不允许透出画布底色）。 */
function groundBase(pal, seed) {
  const c = new Canvas(TILE, TILE);
  c.fill(hex(pal.ground));
  c.speckle(0, 0, TILE, TILE, hex(pal.groundDark), 0.07, seed);
  c.speckle(0, 0, TILE, TILE, hex(pal.groundLight), 0.05, seed + 91);
  return c;
}

function tileGrass(pal, kind, seed) {
  const c = groundBase(pal, seed);
  const detail = pal.__detail || "dry";
  if (kind === "tuft") {
    if (detail === "wet") {
      // 洞窟里长的是苔，不是草
      for (let i = 0; i < 3; i++) {
        const x = 5 + Math.floor(rnd(seed, i) * 22);
        const y = 6 + Math.floor(rnd(seed, i + 10) * 20);
        c.ellipse(x, y, 4, 3, hex(pal.leafDark));
        c.ellipse(x, y - 1, 3, 2, hex(pal.leaf));
      }
    } else {
      const blade = detail === "grim" || detail === "ruin" ? pal.trunkLight : pal.groundDark;
      for (let i = 0; i < 3; i++) {
        const x = 4 + Math.floor(rnd(seed, i) * 24);
        const y = 6 + Math.floor(rnd(seed, i + 10) * 22);
        const h = 2 + Math.floor(rnd(seed, i + 20) * 2);
        for (let k = 0; k < h; k++) {
          c.set(x, y - k, hex(blade));
          c.set(x + 1, y - k - 1, hex(blade));
          if (k === h - 1) c.set(x + 1, y - k, hex(pal.groundLight));
        }
      }
    }
  } else if (kind === "flower") {
    if (detail === "wet" || detail === "grim" || detail === "ruin") {
      // 荒废／阴湿处：不种花，改撒碎石
      for (let i = 0; i < 3; i++) {
        const x = 5 + Math.floor(rnd(seed, i + 3) * 22);
        const y = 8 + Math.floor(rnd(seed, i + 13) * 18);
        c.rect(x, y, 3, 2, hex(pal.stone));
        c.hLine(x, y, 3, hex(pal.stoneLight));
      }
    } else {
      for (let i = 0; i < 2; i++) {
        const x = 6 + Math.floor(rnd(seed, i + 3) * 20);
        const y = 8 + Math.floor(rnd(seed, i + 13) * 16);
        c.rect(x, y, 2, 2, hex(pal.autumnLight));
        c.set(x + 2, y + 1, hex(pal.autumn));
        c.set(x - 1, y + 1, hex(pal.autumn));
        c.set(x, y - 1, hex(pal.autumn));
        c.set(x + 1, y + 2, hex(pal.groundDark));
      }
    }
  } else if (kind === "pebble") {
    for (let i = 0; i < 3; i++) {
      const x = 4 + Math.floor(rnd(seed, i + 5) * 22);
      const y = 6 + Math.floor(rnd(seed, i + 15) * 20);
      c.rect(x, y, 3, 2, hex(pal.stone));
      c.hLine(x, y, 3, hex(pal.stoneLight));
      c.hLine(x, y + 2, 3, hex(pal.groundDark));
    }
  }
  return c;
}

/**
 * 土路／空地：把「这一格哪几条边接草」做成 mask，用起伏的边界切出草地。
 * mask 的 n/s/w/e = 那一侧**有草地**（也就是土路的边缘）。
 */
function tileDirt(pal, mask, seed) {
  const c = new Canvas(TILE, TILE);
  const grass = [];
  for (let y = 0; y < TILE; y++) grass.push(new Array(TILE).fill(false));
  const depth = (i, salt) => 4 + Math.round(noise(i, salt * 17, seed) * 3);

  if (mask.n) {
    for (let x = 0; x < TILE; x++) {
      const d = depth(x, 1);
      for (let y = 0; y < d; y++) grass[y][x] = true;
    }
  }
  if (mask.s) {
    for (let x = 0; x < TILE; x++) {
      const d = depth(x, 2);
      for (let y = TILE - d; y < TILE; y++) grass[y][x] = true;
    }
  }
  if (mask.w) {
    for (let y = 0; y < TILE; y++) {
      const d = depth(y, 3);
      for (let x = 0; x < d; x++) grass[y][x] = true;
    }
  }
  if (mask.e) {
    for (let y = 0; y < TILE; y++) {
      const d = depth(y, 4);
      for (let x = TILE - d; x < TILE; x++) grass[y][x] = true;
    }
  }

  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      c.set(x, y, hex(grass[y][x] ? pal.ground : pal.soil));
    }
  }
  c.speckle(0, 0, TILE, TILE, hex(pal.soilDark), 0.07, seed + 7);
  c.speckle(0, 0, TILE, TILE, hex(pal.soilLight), 0.05, seed + 17);
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      if (grass[y][x]) c.speckle(x, y, 1, 1, hex(pal.groundDark), 0.08, seed + 27);
    }
  }

  // 接缝：土这一侧压一条暗线，草这一侧压一条更暗的草，边界才不糊。
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      const g = grass[y][x];
      const neighbour =
        (y > 0 && grass[y - 1][x] !== g) ||
        (y < TILE - 1 && grass[y + 1][x] !== g) ||
        (x > 0 && grass[y][x - 1] !== g) ||
        (x < TILE - 1 && grass[y][x + 1] !== g);
      if (!neighbour) continue;
      if (g) c.set(x, y, hex(pal.groundDark));
      else c.set(x, y, hex(pal.soilDark));
    }
  }
  return c;
}

/**
 * 室内地面。`style` 跟着主题走：砌石副本铺石板、山洞是碎石湿土、荒村是夯土，
 * 三种地面的质感差得远，玩家才能在洞里／村里一眼分清自己在哪。
 */
function tileFloor(pal, withTop, seed, style) {
  const c = new Canvas(TILE, TILE);
  c.fill(hex(pal.floor));
  c.speckle(0, 0, TILE, TILE, hex(pal.floorDark), 0.06, seed + 3);
  c.speckle(0, 0, TILE, TILE, hex(pal.floorLight), 0.04, seed + 11);
  if (style === "gravel") {
    // 湿土 + 碎石：没有缝，靠石子密度说话
    for (let i = 0; i < 14; i++) {
      const x = 1 + Math.floor(rnd(seed + i, i) * 29);
      const y = 1 + Math.floor(rnd(seed + i, i + 50) * 29);
      c.rect(x, y, 2, 1, hex(pal.floorLight));
      c.set(x, y + 1, hex(pal.floorDark));
    }
    c.speckle(0, 0, TILE, TILE, hex(pal.floorDark), 0.09, seed + 61);
  } else if (style === "soil") {
    // 夯土：只留一道极淡的夯层线 + 一两道裂纹。线一密就成了「砖地」，一整片房间会发花。
    c.hLine(0, 16, TILE, hex(mix(pal.floorDark, pal.floor, 0.45)));
    for (let i = 0; i < 2; i++) {
      const x = 3 + Math.floor(rnd(seed + i, i + 70) * 24);
      const y = 2 + Math.floor(rnd(seed + i, i + 80) * 24);
      for (let k = 0; k < 3; k++) c.set(x + k - 1, y + k, hex(shade(pal.floorDark, -0.15)));
    }
    c.speckle(0, 0, TILE, TILE, hex(pal.floorLight), 0.05, seed + 81);
  } else {
    // 石板：缝压得很淡（缝太黑就变成砖墙了，整间屋子会像砖地），只偶尔补一块略深的板
    const seam = hex(mix(pal.floorDark, pal.floor, 0.4));
    for (let y = 0; y < TILE; y += 16) c.hLine(0, y, TILE, seam);
    for (let x = 0; x < TILE; x += 16) c.vLine(x, 0, TILE, seam);
    c.speckle(0, 0, TILE, TILE, hex(pal.floorDark), 0.03, seed + 71);
  }
  if (withTop) {
    c.rect(0, 0, TILE, 7, hex(pal.floorDark));
    c.hLine(0, 7, TILE, hex(pal.floorLight));
    c.hLine(0, 0, TILE, hex(shade(pal.floorDark, -0.25)));
  }
  return c;
}

// ---------------------------------------------------------------- 植被

/** 针叶树：三段三角 + 树干。透明底，压在土路上也不穿帮。 */
function tilePine(pal, seed) {
  const c = new Canvas(TILE, TILE);
  c.rect(14, 22, 4, 9, hex(pal.trunkDark));
  c.rect(14, 22, 2, 9, hex(pal.trunk));
  const bands = [
    [2, 10, 13],
    [10, 9, 11],
    [17, 7, 9],
  ];
  for (const [y0, h, half] of bands) {
    for (let y = 0; y < h; y++) {
      const w = Math.round(half * (1 - y / h)) + 1;
      for (let x = 16 - w; x <= 15 + w; x++) {
        c.set(x, y0 + y, hex(x < 16 ? pal.pine : pal.pineDark));
      }
      c.set(16 - w, y0 + y, hex(pal.pineDark));
      if (y === 0) c.hLine(16 - w + 1, y0 + y, w, hex(pal.pineLight));
    }
  }
  addTreeRim(c, pal, seed);
  return c;
}

/** 阔叶／秋色树：圆形树冠 + 树干。 */
function tileTree(pal, autumn, seed) {
  const c = new Canvas(TILE, TILE);
  c.rect(14, 21, 5, 10, hex(pal.trunkDark));
  c.rect(14, 21, 3, 10, hex(pal.trunk));
  const leaf = autumn ? pal.autumn : pal.leaf;
  const dark = autumn ? pal.autumnDark : pal.leafDark;
  const light = autumn ? pal.autumnLight : pal.leafLight;
  c.ellipse(16, 14, 13, 12, hex(dark));
  c.ellipse(15, 13, 11, 10, hex(leaf));
  c.ellipse(12, 10, 6, 5, hex(light));
  for (let i = 0; i < 26; i++) {
    const x = 4 + Math.floor(rnd(seed + i, i) * 24);
    const y = 3 + Math.floor(rnd(seed + i, i + 40) * 20);
    c.set(x, y, hex(rnd(seed + i, i + 80) > 0.5 ? dark : light));
  }
  addTreeRim(c, pal);
  return c;
}

/** 小树：给「点景」用，棵小、不压满格。 */
function tileSmallTree(pal, autumn, seed) {
  const c = new Canvas(TILE, TILE);
  c.rect(15, 23, 3, 7, hex(pal.trunkDark));
  const leaf = autumn ? pal.autumn : pal.leaf;
  const dark = autumn ? pal.autumnDark : pal.leafDark;
  const light = autumn ? pal.autumnLight : pal.leafLight;
  c.ellipse(16, 17, 9, 8, hex(dark));
  c.ellipse(15, 16, 7, 6, hex(leaf));
  c.ellipse(13, 14, 3, 2, hex(light));
  addTreeRim(c, pal, seed);
  return c;
}

function tileBush(pal, seed) {
  const c = new Canvas(TILE, TILE);
  c.ellipse(11, 19, 8, 7, hex(pal.leafDark));
  c.ellipse(21, 17, 8, 7, hex(pal.leafDark));
  c.ellipse(11, 18, 6, 5, hex(pal.leaf));
  c.ellipse(21, 16, 6, 5, hex(pal.leaf));
  c.ellipse(9, 16, 3, 2, hex(pal.leafLight));
  c.ellipse(19, 14, 2, 2, hex(pal.leafLight));
  addTreeRim(c, pal, seed);
  return c;
}

/** 密林：整格铺满树冠，用来封边（16 §3「看起来像山林而不是空气墙」）。 */
function tileForest(pal, autumn, seed) {
  const c = new Canvas(TILE, TILE);
  const leaf = autumn ? pal.autumn : pal.leaf;
  const dark = autumn ? pal.autumnDark : pal.leafDark;
  const light = autumn ? pal.autumnLight : pal.leafLight;
  // 两棵并排的树 + 三层树冠，边缘相接但树形还认得出——封边要「密」而不是「一坨」。
  c.rect(0, 12, TILE, 16, hex(dark));
  for (const [x, y, rx, ry] of [[9, 15, 10, 11], [23, 17, 9, 10]]) {
    c.ellipse(x, y, rx, ry, hex(dark));
    c.ellipse(x - 1, y - 1, rx - 2, ry - 2, hex(leaf));
    c.ellipse(x - 3, y - 4, Math.max(2, rx - 5), Math.max(2, ry - 6), hex(light));
  }
  c.rect(6, 25, 4, 7, hex(pal.trunkDark));
  c.rect(22, 25, 4, 7, hex(pal.trunkDark));
  c.rect(6, 25, 2, 7, hex(pal.trunk));
  c.rect(22, 25, 2, 7, hex(pal.trunk));
  for (let i = 0; i < 40; i++) {
    const x = Math.floor(rnd(seed + i, i) * TILE);
    const y = Math.floor(rnd(seed + i, i + 30) * TILE);
    if (c.get(x, y)[3] > 0 && rnd(seed + i, i + 60) > 0.55) {
      c.set(x, y, hex(rnd(seed + i, i + 90) > 0.6 ? light : dark));
    }
  }
  return c;
}

function tileMushroom(pal, seed) {
  const c = new Canvas(TILE, TILE);
  for (const [x, y, r] of [[11, 18, 7], [22, 24, 5]]) {
    c.rect(x - 1, y, 3, 8, hex(pal.wall));
    c.vLine(x - 1, y, 8, hex(pal.wallShade));
    c.ellipse(x, y - 1, r, Math.max(3, r - 3), hex(pal.autumnDark));
    c.ellipse(x, y - 2, r - 1, Math.max(2, r - 4), hex(pal.autumn));
    c.ellipse(x - 1, y - 3, Math.max(1, r - 4), Math.max(1, r - 6), hex(pal.autumnLight));
  }
  return c;
}

/** 树冠的暗侧压边：光源自左上，所以只压右下沉边（不做整圈描边，见 15 §一）。 */
function addTreeRim(c, pal) {
  const src = c.clone();
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      if (src.get(x, y)[3] === 0) continue;
      const below = y + 1 < TILE ? src.get(x, y + 1) : [0, 0, 0, 0];
      const right = x + 1 < TILE ? src.get(x + 1, y) : [0, 0, 0, 0];
      if (below[3] === 0 || right[3] === 0) {
        if (src.get(x, y)[3] > 0) c.set(x, y, hex(mix(pal.ink, pal.leafDark, 0.45)));
      }
    }
  }
}

// ---------------------------------------------------------------- 构筑物

/** 屋顶瓦面：横向瓦垄 + 错缝。ridge=true 时是屋顶最上排（带正脊）。 */
function tileRoof(pal, variant, ridge) {
  const c = new Canvas(TILE, TILE);
  const base = variant === "A" ? pal.roofA : pal.roofB;
  const dark = variant === "A" ? pal.roofADark : pal.roofBDark;
  const light = variant === "A" ? pal.roofALight : pal.roofBLight;
  c.fill(hex(base));
  if (ridge) {
    // 正脊：一条压暗的脊线，顶上再走一道高光，屋顶的「最高处」必须在图上读得出来。
    c.rect(0, 0, TILE, 7, hex(dark));
    c.hLine(0, 0, TILE, hex(light));
    c.hLine(0, 1, TILE, hex(light));
    c.hLine(0, 6, TILE, hex(shade(dark, -0.35)));
  }
  // 瓦垄：每 8×6 一片瓦，顶边高光、两侧与下沿压暗——跟砌砖（16×8 的大块）一眼分得开。
  const startY = ridge ? 7 : 0;
  for (let cy = startY; cy < TILE; cy += 6) {
    const offset = (Math.floor(cy / 6) % 2) * 4;
    for (let sx = -8; sx < TILE; sx += 8) {
      const x = sx + offset;
      c.hLine(x + 1, cy, 6, hex(light));
      c.set(x, cy + 1, hex(dark));
      c.set(x + 7, cy + 1, hex(dark));
      c.vLine(x, cy + 2, 3, hex(dark));
      c.vLine(x + 7, cy + 2, 3, hex(dark));
      c.set(x, cy + 5, hex(dark));
      c.set(x + 7, cy + 5, hex(dark));
      c.hLine(x + 2, cy + 5, 4, hex(dark));
      c.set(x + 3, cy + 5, hex(base));
      c.set(x + 4, cy + 5, hex(base));
    }
  }
  return c;
}

/** 山墙／坡角：对角收边的屋顶端头，透明底（`tileRoof` 的补角）。 */
function tileGable(pal, variant) {
  const c = new Canvas(TILE, TILE);
  const base = variant === "A" ? pal.roofA : pal.roofB;
  const dark = variant === "A" ? pal.roofADark : pal.roofBDark;
  const light = variant === "A" ? pal.roofALight : pal.roofBLight;
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      const edge = 12;
      if (x + y >= edge) {
        const d = x + y - edge;
        c.set(x, y, hex(d === 0 || d === 1 ? dark : d < 4 ? base : d < 8 ? light : base));
      }
    }
  }
  return c;
}

/** 墙体：按主题的材质语言分白墙／砌石／木构三种。 */
function tileWall(pal, style, seed) {
  const c = new Canvas(TILE, TILE);
  c.fill(hex(pal.wall));
  if (style === "masonry") {
    for (let y = 0; y < TILE; y += 8) {
      const offset = (y / 8) % 2 === 0 ? 0 : 8;
      c.hLine(0, y, TILE, hex(pal.wallDark));
      c.hLine(0, y + 1, TILE, hex(pal.wallShade));
      for (let x = offset; x < TILE; x += 16) c.vLine(x, y, 8, hex(pal.wallDark));
    }
    for (let y = 0; y < TILE; y += 8) {
      for (let x = 0; x < TILE; x += 16) {
        const ox = ((y / 8) % 2 === 0 ? 0 : 8) + x;
        const blk = ((x / 16) + y / 8) % 2 === 0 ? pal.wall : pal.wallDark;
        c.rect(ox + 1, y + 2, 14, 5, hex(blk));
        c.hLine(ox + 1, y + 2, 14, hex(pal.wall));
      }
    }
  } else if (style === "wood") {
    for (let x = 0; x < TILE; x += 8) {
      c.rect(x, 0, 7, TILE, hex(pal.wallAlt));
      c.vLine(x, 0, TILE, hex(pal.wallAltDark));
      c.vLine(x + 7, 0, TILE, hex(pal.wallAltLight));
    }
    c.hLine(0, 0, TILE, hex(pal.wallAltLight));
    c.hLine(0, TILE - 1, TILE, hex(pal.wallAltDark));
  } else {
    c.hLine(0, 0, TILE, hex(pal.wall));
    c.hLine(0, 1, TILE, hex(shade(pal.wall, 0.08)));
    c.hLine(0, TILE - 1, TILE, hex(pal.wallDark));
    c.hLine(0, TILE - 2, TILE, hex(pal.wallShade));
    c.speckle(0, 0, TILE, TILE, hex(pal.wallShade), 0.05, seed + 5);
    for (let y = 15; y < 18; y++) c.hLine(0, y, TILE, hex(pal.wallDark));
  }
  return c;
}

/** 窗：白墙上开窗，窗心里点一盏灯（清风驿的暖黄全靠这一格）。 */
function tileWindow(pal, style) {
  const c = tileWall(pal, style, 21);
  c.rect(8, 7, 16, 17, hex(pal.woodDark));
  c.rect(10, 9, 12, 13, hex(pal.lampDark));
  c.rect(11, 11, 10, 9, hex(pal.lamp));
  c.dither(11, 11, 10, 9, pal.lamp, pal.lampDark, 0.35, 7);
  c.vLine(15, 9, 13, hex(pal.woodDark));
  c.hLine(10, 15, 12, hex(pal.woodDark));
  c.rect(8, 7, 16, 1, hex(pal.wood));
  return c;
}

/** 门：木门 + 朱红门环（朱红只用在这里和旗帜／灯笼／血，见 15 §二）。 */
function tileDoor(pal, style) {
  const c = tileWall(pal, style, 31);
  c.rect(8, 5, 16, 27, hex(pal.woodDark));
  c.rect(10, 7, 12, 25, hex(pal.wood));
  for (let x = 11; x < 22; x += 4) c.vLine(x, 7, 25, hex(pal.woodDark));
  c.rect(13, 9, 6, 10, hex(pal.woodLight));
  c.rect(13, 21, 6, 9, hex(pal.woodLight));
  c.hLine(8, 5, 16, hex(shade(pal.woodDark, -0.2)));
  c.rect(18, 18, 3, 3, hex(pal.redDark));
  c.rect(18, 18, 2, 2, hex(pal.red));
  return c;
}

/**
 * 主墙体（自动砌墙那 800+ 格全靠它）。两套画法按主题走：
 *  - `masonry`：山寨／塌陷山洞——**砌石**，有错缝的灰缝。
 *  - `crack`：石隙迷窟——**笔直的岩面 + 竖向裂缝**，没有砌缝。
 * 16 §5 要求这两个岩洞不能撞脸，差别就落在这张瓦片上：
 * 「塌出来的」是碎块拼的，「劈出来的」是一整面被裂开的岩。
 */
function tileBattlement(pal, seed) {
  if (pal.__rockFace === "crack") return tileCrackWall(pal, seed);
  const c = tileWall(pal, "masonry", seed);
  c.hLine(0, 0, TILE, hex(pal.stoneLight));
  c.hLine(0, 1, TILE, hex(pal.stone));
  c.hLine(0, TILE - 1, TILE, hex(shade(pal.wallDark, -0.3)));
  c.rect(2, 10, 12, 10, hex(pal.wall));
  c.rect(18, 10, 12, 10, hex(pal.wallDark));
  c.rect(2, 22, 12, 8, hex(pal.wallDark));
  c.rect(18, 22, 12, 8, hex(pal.wall));
  c.speckle(0, 0, TILE, TILE, hex(pal.wallShade), 0.06, seed + 13);
  return c;
}

/** 石隙迷窟的岩面：整面青灰岩，自上而下贯通的竖裂缝，只有极少的横层理。 */
function tileCrackWall(pal, seed) {
  const c = new Canvas(TILE, TILE);
  c.fill(hex(pal.wall));
  // 竖向的岩面明暗：左亮右暗（光源自左上，与全项目一致）
  for (let x = 0; x < TILE; x++) {
    const t = x / (TILE - 1);
    c.rect(x, 0, 1, TILE, hex(mix(pal.wall, pal.wallDark, t * 0.55)));
  }
  c.hLine(0, 0, TILE, hex(pal.stoneLight));
  c.hLine(0, 1, TILE, hex(mix(pal.stoneLight, pal.wall, 0.5)));
  c.hLine(0, TILE - 1, TILE, hex(shade(pal.wallDark, -0.35)));
  // 贯通裂缝：一条主缝 + 一条支缝，缝里压暗、缝边留一线亮（高光落在缝口）
  const main = 9 + Math.round(noise(seed, 1, 77) * 4);
  for (let y = 0; y < TILE; y++) {
    const jog = noise(Math.floor(y / 6), seed, 31) > 0.5 ? 1 : 0;
    c.set(main + jog, y, hex(shade(pal.wallDark, -0.45)));
    c.set(main + jog + 1, y, hex(shade(pal.wallDark, -0.15)));
    c.set(main + jog - 1, y, hex(mix(pal.wall, pal.stoneLight, 0.35)));
  }
  const second = 23 + Math.round(noise(seed, 2, 91) * 3);
  for (let y = 2; y < TILE - 3; y++) {
    if (noise(y, seed, 53) > 0.75) continue;
    c.set(second, y, hex(shade(pal.wallDark, -0.3)));
    c.set(second + 1, y, hex(mix(pal.wall, pal.stoneLight, 0.25)));
  }
  // 层理：只两条，且断断续续——砌缝那种规整感一出来就又变成山寨了
  for (const y of [8, 22]) {
    for (let x = 0; x < TILE; x++) {
      if (noise(x, y, seed) > 0.4) c.set(x, y, hex(mix(pal.wallDark, pal.wall, 0.45)));
    }
  }
  c.speckle(0, 0, TILE, TILE, hex(pal.wallDark), 0.05, seed + 13);
  c.speckle(0, 0, TILE, TILE, hex(pal.stoneLight), 0.03, seed + 29);
  return c;
}

/** 墙角／寨墙顶：压亮左上、压暗右下，给墙线一个明确的「转折点」。 */
function tileWallCorner(pal, seed) {
  const c = tileBattlement(pal, seed + 3);
  c.rect(0, 0, TILE, 3, hex(pal.stoneLight));
  c.rect(0, 0, 3, TILE, hex(pal.stoneLight));
  c.rect(TILE - 3, 0, 3, TILE, hex(shade(pal.wallDark, -0.4)));
  c.rect(0, TILE - 3, TILE, 3, hex(shade(pal.wallDark, -0.4)));
  return c;
}

/** 券门：石拱洞里透出一线暗，山洞／聚义厅的门都用它。 */
function tileArch(pal, seed) {
  // 石隙的「门」不是石拱，是**劈开的窄缝**：一竖条开口 + 两侧笔直岩面
  if (pal.__rockFace === "crack") return tileCrackGap(pal, seed);
  const c = tileBattlement(pal, seed + 9);
  c.rect(6, 12, 20, 20, hex(shade(pal.ink, 0.05)));
  c.ellipse(16, 13, 10, 8, hex(shade(pal.ink, 0.05)));
  for (let i = 0; i < 14; i++) {
    const x = 6 + Math.floor(rnd(seed + i, i) * 20);
    c.set(x, 12 + Math.floor(rnd(seed + i, i + 20) * 18), hex(shade(pal.ink, 0.18)));
  }
  c.frame(6, 12, 20, 20, hex(pal.stoneLight));
  return c;
}

/** 岩缝开口：石隙迷窟的通道口。真岔路与死路的开口**用同一张**（16 §4.8 的死路口径）。 */
function tileCrackGap(pal, seed) {
  const c = tileCrackWall(pal, seed + 9);
  // 开口：像被硬劈开的一道缝，**缝里几乎全黑**——这是石隙「看不出深浅」的那一招：
  // 玩家从缝口望进去读不出里面有多深，才可能被骗着走进去（见交接单 §六 第 1 条）。
  for (let y = 4; y < TILE - 2; y++) {
    const t = (y - 4) / (TILE - 6);
    const half = 5 + Math.round(t * 2);
    c.rect(16 - half, y, half * 2, 1, hex(shade(pal.ink, 0.06)));
  }
  c.rect(10, 3, 12, 3, hex(shade(pal.ink, 0.06)));
  // 缝口两侧的高光：光从上方来，缝口是整张图最亮的地方
  for (let y = 4; y < TILE - 2; y++) {
    const t = (y - 4) / (TILE - 6);
    const half = 5 + Math.round(t * 2);
    c.set(16 - half - 1, y, hex(pal.stoneLight));
    c.set(16 + half, y, hex(mix(pal.stoneLight, pal.wall, 0.5)));
  }
  return c;
}

// ---------------------------------------------------------------- 物件

// ---------------------------------------------------------------- 大地图的山（16 §3.5「成片、有厚度」）

/**
 * 山体：一套九宫格（照土路那套的用法：`mask` 说「这一格哪几条边还是山」）。
 *
 * 为什么不是「孤零零一块山石」：16 §3.5 新加的那句是**山脉本身要成片、有厚度**——
 * 用户的试玩反馈正是「洞穴之类的希望融入到山体中，而不是孤零零放个图标」。
 * 所以山必须是一**片**：上沿山脊（受光）、下沿坡脚（压到地面上）、左右有明暗缘、中间岩面。
 * 相机 2×（一屏约 16×9 格），一层山至少要铺 2–3 屏才像山。
 * 光源统一自左上（15 §一）：**左上受光、右下压暗并在地面上落影**。
 */
function tileMountain(pal, mask, seed) {
  const c = new Canvas(TILE, TILE);
  const rock = pal.rock || "#4E5A5E";
  const rockDark = pal.rockDark || "#333B3E";
  const rockLight = pal.rockLight || "#63706F";
  // **纵向明暗**是「厚度」的主要来源：上面受光、越往下越暗。
  // 第一版整片同一个色 → 铺到地图上就是一面灰墙（2× 相机下更明显）。
  for (let y = 0; y < TILE; y++) {
    const t = y / (TILE - 1);
    const base = mix(rock, rockDark, 0.15 + t * 0.55);
    c.hLine(0, y, TILE, hex(base));
  }
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      const n = noise(x, y, seed);
      if (n > 0.88) c.set(x, y, hex(rockDark));
      else if (n < 0.08) c.set(x, y, hex(rockLight));
    }
  }
  // 沟壑：自左上往右下走的暗沟（2px 暗 ＋ 左缘 1px 亮），是"山面"的骨架
  for (let i = 0; i < 3; i++) {
    const x0 = -4 + i * 13 + Math.round(noise(i, seed, 3) * 5);
    for (let y = 0; y < TILE; y++) {
      const x = x0 + Math.round(y * 0.42);
      if (noise(y, i, seed) < 0.24) continue; // 断断续续，别做成直线
      if (x >= 0 && x < TILE) c.set(x, y, hex(shade(rockDark, -0.35)));
      if (x + 1 >= 0 && x + 1 < TILE) c.set(x + 1, y, hex(rockDark));
      if (x - 1 >= 0 && x - 1 < TILE) c.set(x - 1, y, hex(mix(rockLight, rock, 0.4)));
    }
  }
  if (mask.n) {
    // 山脊：不等高的棱线（直的一横会立刻暴露"这是瓦片"）
    for (let x = 0; x < TILE; x++) {
      const h = 2 + Math.round(noise(x, seed, 31) * 3);
      for (let y = 0; y < h; y++) c.set(x, y, hex(rockLight));
      c.set(x, h, hex(rock));
      c.set(x, h + 1, hex(rockDark));
    }
    for (let i = 0; i < 2; i++) {
      const x = 5 + i * 14 + Math.round(noise(i, seed, 7) * 4);
      c.rect(x + 1, 3, 2, 5, hex(pal.trunkDark));
      for (let k = 0; k < 3; k++) {
        const w = 4 - k;
        c.hLine(x + 2 - w, 1 + k * 2, w * 2 + 1, hex(k === 0 ? pal.pineDark : pal.pine));
      }
    }
  }
  if (mask.s) {
    for (let i = 0; i < 9; i++) {
      const x = 1 + Math.floor(noise(i, seed, 11) * 28);
      const y = 20 + Math.floor(noise(i, seed, 13) * 6);
      c.rect(x, y, 3, 2, hex(rockLight));
      c.hLine(x, y + 2, 3, hex(rockDark));
    }
    c.rect(0, 27, TILE, 5, hex(shade(rockDark, -0.42)));
    c.hLine(0, 27, TILE, hex(rockDark));
  }
  if (mask.w) {
    for (let y = 0; y < TILE; y++) {
      const w = 1 + Math.round(noise(y, seed, 37) * 2);
      for (let x = 0; x < w; x++) c.set(x, y, hex(rockLight));
      c.set(w, y, hex(mix(rockLight, rock, 0.5)));
    }
  }
  if (mask.e) {
    c.rect(TILE - 2, 0, 2, TILE, hex(rockDark));
    for (let y = 2; y < TILE - 2; y++) {
      if (noise(y, seed, 17) > 0.4) c.set(TILE - 1, y, hex(shade(rockDark, -0.4)));
    }
  }
  c.outline(hex(pal.ink));
  return c;
}

/** 山峰：山体内部偶尔竖一座尖顶（两档高度），打断"一整片墙"的规整感。 */
function tileMountainPeak(pal, seed, tall) {
  const c = tileMountain(pal, { n: true }, seed + 5);
  const h = tall ? 20 : 14;
  const top = 16 - h;
  for (let i = 0; i < h; i++) {
    const w = Math.round(((i + 2) / h) * 13);
    // 左面受光、右面压暗：这一条就把"尖顶"立起来了
    for (let x = 16 - w; x <= 16 + w; x++) {
      c.set(x, top + i, hex(x < 16 ? (pal.rockLight || "#63706F") : (pal.rockDark || "#333B3E")));
    }
    c.set(16 - w, top + i, hex("#7E8A88"));
    c.set(16 + w, top + i, hex(shade(pal.rockDark || "#333B3E", -0.3)));
  }
  c.rect(15, top - 4, 3, 5, hex(pal.trunkDark));
  for (let k = 0; k < 4; k++) {
    const w = 4 - k;
    c.hLine(16 - w, top - 6 + k * 2, w * 2 + 1, hex(k < 2 ? pal.pine : pal.pineDark));
  }
  c.outline(hex(pal.ink));
  return c;
}

/**
 * 塌陷山洞的洞口：**山体上的一个凹陷**（16 §3.5：不是摆在地上的洞图标）。
 * 洞口上檐是塌下来的碎石（"塌陷"两个字就在这儿），脚下散着落石。
 */
function tileCaveRecess(pal, seed) {
  const c = tileMountain(pal, { n: true }, seed + 3);
  for (let y = 8; y < 30; y++) {
    const t = (y - 8) / 22;
    const half = Math.round(11 * (1 - t * 0.35));
    const x0 = 16 - half;
    for (let x = x0; x < x0 + half * 2; x++) {
      const edge = Math.min(x - x0, x0 + half * 2 - 1 - x) / Math.max(1, half);
      c.set(x, y, hex(edge < 0.22 ? shade(pal.ink, 0.14) : shade(pal.ink, 0.02)));
    }
  }
  for (let i = 0; i < 7; i++) {
    const x = 5 + i * 3;
    const y = 6 + Math.round(noise(i, seed, 19) * 3);
    c.rect(x, y, 3, 2, hex(pal.rockLight || "#63706F"));
    c.hLine(x, y + 2, 3, hex(shade(pal.rockDark || "#333B3E", -0.3)));
  }
  for (const [x, y] of [[6, 26], [23, 27], [15, 29]]) {
    c.rect(x, y, 4, 3, hex(pal.rockDark || "#333B3E"));
    c.hLine(x, y, 4, hex(pal.rockLight || "#63706F"));
  }
  c.outline(hex(pal.ink));
  return c;
}

/**
 * 石隙迷窟的入口：山体上的一道**笔直窄缝**（16 §4.8 的「劈出来的」，与塌陷山洞刻意相反）。
 * 缝顶留一线亮（＝一线天光），缝身笔直、两侧岩面完整。
 */
function tileCrackMouth(pal, seed) {
  const c = tileMountain(pal, { n: true }, seed + 7);
  for (let y = 6; y < 30; y++) {
    const jog = noise(Math.floor(y / 5), seed, 23) > 0.5 ? 1 : 0;
    c.rect(16 + jog - 2, y, 4, 1, hex(shade(pal.ink, 0.05)));
    c.set(16 + jog - 3, y, hex(pal.rockLight || "#63706F"));
    c.set(16 + jog + 2, y, hex(shade(pal.rockDark || "#333B3E", -0.2)));
  }
  for (let y = 3; y < 11; y++) {
    c.set(16, y, hex("#8EA8CC"));
    c.set(17, y, hex("#C6D8EE"));
  }
  c.outline(hex(pal.ink));
  return c;
}

/**
 * 路牌：木牌 ＋ 箭头（0.31.2 新增，主路四个节点各一块）。
 *
 * 「**朱红小字**」这条得说明白：32×32 里**写不下真的汉字**（那是引擎字体渲染的活，
 * 见 15 §4.6 与 17 号那条字体线）。所以这张贴图负责**木牌本体 ＋ 朱红箭头 ＋ 几笔字痕**，
 * 真正的地名／里程由 Label 叠在牌子上——地标名字本来就已经是这么做的（`NodeLabel_*`）。
 */
function tileSignpost(pal) {
  const c = new Canvas(TILE, TILE);
  c.rect(14, 12, 4, 19, hex(pal.woodDark));
  c.rect(14, 12, 2, 19, hex(pal.wood));
  c.rect(3, 5, 26, 13, hex(pal.woodDark));
  c.rect(4, 6, 24, 11, hex(pal.woodLight));
  c.hLine(4, 6, 24, hex(mix(pal.woodLight, "#FFFFFF", 0.18)));
  c.hLine(4, 16, 24, hex(pal.woodDark));
  for (let i = 0; i < 8; i++) c.rect(7 + i, 11, 1, 2, hex(pal.red));
  for (let i = 0; i < 4; i++) {
    c.set(15 - i, 9 + i, hex(pal.red));
    c.set(15 - i, 14 - i, hex(pal.red));
  }
  for (const [x, y] of [[20, 8], [23, 10], [20, 12], [24, 13]]) c.hLine(x, y, 2, hex(pal.redDark));
  c.outline(hex(pal.ink));
  return c;
}


// ---------------------------------------------------------------- 特殊（15 §4.1 的「特殊」一类）

/** 深水：整格不透明。水岸与渡口都要它，否则「灰蓝水汽」那套材质无处落地。 */
function tileWaterDeep(pal, seed) {
  const c = new Canvas(TILE, TILE);
  c.fill(hex(pal.waterDark));
  c.speckle(0, 0, TILE, TILE, hex(shade(pal.waterDark, -0.25)), 0.08, seed + 4);
  for (let y = 3; y < TILE; y += 7) {
    const off = (y % 14 === 3 ? 0 : 5);
    for (let x = off; x < TILE; x += 11) {
      c.hLine(x, y, 4, hex(pal.water));
      c.set(x + 4, y, hex(shade(pal.water, 0.15)));
    }
  }
  return c;
}

/** 浅水：能看见水底，靠近岸边的地方更亮。 */
function tileWaterShallow(pal, seed) {
  const c = new Canvas(TILE, TILE);
  c.fill(hex(pal.water));
  c.speckle(0, 0, TILE, TILE, hex(shade(pal.water, 0.18)), 0.09, seed + 6);
  c.speckle(0, 0, TILE, TILE, hex(pal.waterDark), 0.04, seed + 16);
  for (let i = 0; i < 6; i++) {
    const x = 2 + Math.floor(rnd(seed + i, i) * 26);
    const y = 3 + Math.floor(rnd(seed + i, i + 30) * 26);
    c.hLine(x, y, 3, hex(shade(pal.water, 0.35)));
  }
  for (let i = 0; i < 3; i++) {
    const x = 4 + Math.floor(rnd(seed + i, i + 60) * 24);
    const y = 4 + Math.floor(rnd(seed + i, i + 70) * 24);
    c.rect(x, y, 2, 1, hex(pal.soilDark));
  }
  return c;
}

/** 水岸：一格之内从水过渡到泥，边界做成起伏的（直边会像贴图放错）。 */
function tileWaterShore(pal, seed) {
  const c = new Canvas(TILE, TILE);
  const water = [];
  for (let x = 0; x < TILE; x++) {
    const d = 14 + Math.round(noise(x, 3, seed) * 5);
    for (let y = 0; y < TILE; y++) water[y * TILE + x] = y < d;
  }
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      c.set(x, y, hex(water[y * TILE + x] ? pal.water : pal.soil));
    }
  }
  c.speckle(0, 14, TILE, 18, hex(pal.soilDark), 0.08, seed + 8);
  for (let x = 0; x < TILE; x++) {
    for (let y = 0; y < TILE; y++) {
      const isWater = water[y * TILE + x];
      const neighbour =
        (y > 0 && water[(y - 1) * TILE + x] !== isWater) ||
        (y < TILE - 1 && water[(y + 1) * TILE + x] !== isWater);
      if (!neighbour) continue;
      c.set(x, y, hex(isWater ? shade(pal.water, 0.3) : pal.soilLight));
    }
  }
  return c;
}

/** 芦苇：水岸的植被。透明底，压在浅水上就是芦苇荡。 */
function tileReeds(pal, seed) {
  const c = new Canvas(TILE, TILE);
  for (let i = 0; i < 7; i++) {
    const x = 3 + i * 4 + Math.floor(rnd(seed + i, i) * 2);
    const h = 14 + Math.floor(rnd(seed + i, i + 10) * 10);
    const y0 = TILE - 2 - h;
    for (let y = y0; y < TILE - 1; y++) {
      c.set(x, y, hex(y > y0 + 6 ? pal.leafDark : pal.leaf));
    }
    c.ellipse(x, y0 + 3, 1, 3, hex(pal.autumn));
    c.set(x + 1, y0 + 2, hex(pal.autumnLight));
  }
  return c;
}

/** 脚印：压在路上的表现层瓦片（Overlay 层用），不改地形只留痕。 */
function tileFootprint(pal, seed) {
  const c = new Canvas(TILE, TILE);
  for (let i = 0; i < 4; i++) {
    const x = 5 + Math.floor(rnd(seed + i, i) * 20);
    const y = 4 + i * 7 + Math.floor(rnd(seed + i, i + 5) * 3);
    c.rect(x, y, 2, 3, hex(shade(pal.soil, -0.35)));
    c.rect(x + 3, y + 3, 2, 3, hex(shade(pal.soil, -0.35)));
  }
  return c;
}

/** 血迹：朱红在这里是**唯一合法**的用法之一（15 §二：血／灯笼／旗帜／敌意）。 */
function tileBlood(pal, seed) {
  const c = new Canvas(TILE, TILE);
  for (const [x, y, r] of [[12, 14, 5], [19, 20, 3], [9, 22, 2], [22, 10, 2]]) {
    c.ellipse(x, y, r, r - 1, hex(pal.redDark));
    c.ellipse(x, y - 1, r - 1, r - 2, hex(pal.red));
  }
  for (let i = 0; i < 10; i++) {
    const x = 4 + Math.floor(rnd(seed + i, i) * 24);
    const y = 4 + Math.floor(rnd(seed + i, i + 20) * 24);
    c.set(x, y, hex(pal.redDark));
  }
  return c;
}

// ---------------------------------------------------------------- 石隙迷窟专用

/**
 * 一线天光：石隙迷窟的视觉签名（16 §4.8）。压在 Overlay 层上的一竖条冷光，
 * 半透明——它**给地形打光**而不是盖住地形；与塌陷山洞「几乎无光」正好相反。
 */
function tileSkySlit(pal, seed) {
  const c = new Canvas(TILE, TILE);
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      const dx = Math.abs(x - 15.5) / 15.5;
      const a = Math.pow(1 - dx, 2.2) * 0.78;
      if (a <= 0.02) continue;
      // 越靠上越亮（光从头顶的缝隙里下来），到底部散开
      const dy = y / (TILE - 1);
      c.blend(x, y, hex(mix("#8EA8CC", "#D6E4F6", 1 - dy)), a * (1 - dy * 0.35));
    }
  }
  for (let i = 0; i < 30; i++) {
    const x = 10 + Math.floor(noise(i, seed, 61) * 12);
    const y = Math.floor(noise(i, seed, 71) * TILE);
    c.blend(x, y, hex("#F0F6FE"), 0.65);
  }
  return c;
}

/** 前朝石台：整个区域唯一的人工痕迹（16 §4.8）。石面 + 刻纹 + 幽蓝的残卷光。 */
function tileShrinePlatform(pal) {
  const c = new Canvas(TILE, TILE);
  c.rect(3, 6, 26, 20, hex(pal.stoneDark));
  c.rect(4, 7, 24, 17, hex(pal.stoneLight));
  c.rect(5, 8, 22, 15, hex(pal.stone));
  // 刻纹：回字边（用断线，别做成规整花边——它已经放了几百年）
  for (const inset of [3, 6]) {
    c.frame(5 + inset, 8 + inset, 22 - inset * 2, 15 - inset * 2, hex(pal.stoneDark));
  }
  // 残卷：一小卷 + 幽蓝的光
  c.rect(13, 12, 7, 6, hex("#3E5A7A"));
  c.rect(13, 12, 7, 2, hex("#5E7EA8"));
  c.rect(14, 15, 5, 1, hex("#2E445C"));
  for (let y = 9; y < 23; y++) {
    for (let x = 8; x < 25; x++) {
      const d = Math.hypot(x - 16, y - 15);
      if (d > 3 && d < 10) c.blend(x, y, hex("#5E7EA8"), (1 - d / 10) * 0.22);
    }
  }
  c.outline(hex(pal.ink));
  return c;
}

/** 碎石堆：石隙里的落石（挡住走位的装饰）。 */
function tileRubble(pal, seed) {
  const c = new Canvas(TILE, TILE);
  const rocks = [
    [8, 20, 7, 5], [19, 22, 8, 6], [13, 14, 6, 4], [24, 13, 5, 4],
  ];
  for (const [x, y, rx, ry] of rocks) {
    c.ellipse(x, y, rx, ry, hex(pal.stoneDark));
    c.ellipse(x - 1, y - 1, rx - 1, ry - 1, hex(pal.stone));
    c.ellipse(x - 2, y - 2, Math.max(1, rx - 3), Math.max(1, ry - 3), hex(pal.stoneLight));
  }
  c.speckle(0, 16, TILE, 16, hex(pal.stoneDark), 0.12, seed + 5);
  c.outline(hex(pal.ink));
  return c;
}

/**
 * 岩檐（Overlay 遮挡）：压在岩缝内段上的一块暗岩。
 *
 * 这一张是 16 §4.8「死路看起来能走」的**唯一技术出路**：俯视视角下玩家本来能一眼看完
 * 整条岔缝，除非缝的内段被盖住、走进去才揭开。07 §节点结构里 `Overlay` 的定位就是
 * 「遮挡层（树冠、屋檐），玩家走到下面时半透明」——石隙的岩檐就是岩版的屋檐。
 */
function tileRockEave(pal, seed) {
  const c = new Canvas(TILE, TILE);
  c.rect(0, 0, TILE, TILE, [26, 30, 32, 186]);
  for (let x = 0; x < TILE; x++) {
    const d = 2 + Math.round(noise(x, seed, 17) * 3);
    for (let y = 0; y < d; y++) c.blend(x, y, hex(pal.stoneDark), 0.5);
    c.set(x, d, hex(mix(pal.stoneDark, pal.ink, 0.5)));
  }
  c.speckle(0, 4, TILE, TILE - 4, hex(shade(pal.ink, 0.08)), 0.08, seed + 3, 0.5);
  return c;
}



function propBase(seed) {
  return new Canvas(TILE, TILE);
}

function tileFence(pal, kind) {
  const c = propBase(1);
  if (kind === "h") {
    for (const y of [9, 20]) {
      c.rect(0, y, TILE, 3, hex(pal.woodDark));
      c.hLine(0, y, TILE, hex(pal.woodLight));
    }
  } else if (kind === "v") {
    for (const x of [9, 20]) {
      c.rect(x, 0, 3, TILE, hex(pal.woodDark));
      c.vLine(x, 0, TILE, hex(pal.woodLight));
    }
  } else {
    c.rect(12, 0, 8, TILE, hex(pal.woodDark));
    c.rect(12, 0, 4, TILE, hex(pal.wood));
    c.vLine(12, 0, TILE, hex(pal.woodLight));
    c.hLine(12, 0, 8, hex(pal.woodLight));
  }
  c.outline(hex(pal.ink));
  return c;
}

function tileWell(pal, seed) {
  const c = propBase(2);
  c.disk(16, 18, 14, hex(pal.stoneDark));
  c.disk(15, 17, 13, hex(pal.stone));
  c.disk(15, 16, 9, hex(pal.waterDark));
  c.disk(15, 16, 7, hex(pal.water));
  c.ellipse(12, 13, 3, 2, hex(shade(pal.water, 0.25)));
  for (let a = 0; a < 8; a++) {
    const ang = (a / 8) * Math.PI * 2;
    c.rect(15 + Math.cos(ang) * 11, 16 + Math.sin(ang) * 11, 3, 3, hex(pal.stoneLight));
  }
  c.rect(4, 6, 24, 3, hex(pal.woodDark));
  c.hLine(4, 6, 24, hex(pal.woodLight));
  c.rect(6, 3, 4, 26, hex(pal.woodDark));
  c.rect(22, 3, 4, 26, hex(pal.woodDark));
  c.outline(hex(pal.ink));
  return c;
}

function tileSign(pal) {
  const c = propBase(3);
  c.rect(15, 12, 3, 20, hex(pal.woodDark));
  c.rect(3, 4, 26, 15, hex(pal.woodDark));
  c.rect(4, 5, 24, 13, hex(pal.woodLight));
  c.frame(4, 5, 24, 13, hex(pal.wood));
  c.hLine(8, 9, 16, hex(pal.woodDark));
  c.hLine(8, 13, 12, hex(pal.woodDark));
  c.rect(20, 12, 4, 3, hex(pal.red));
  c.outline(hex(pal.ink));
  return c;
}

function tileBench(pal) {
  const c = propBase(4);
  for (const y of [10, 16, 22]) {
    c.rect(3, y, 26, 3, hex(pal.woodDark));
    c.hLine(3, y, 26, hex(pal.woodLight));
  }
  c.rect(5, 25, 4, 5, hex(pal.woodDark));
  c.rect(23, 25, 4, 5, hex(pal.woodDark));
  c.outline(hex(pal.ink));
  return c;
}

function tileCrate(pal) {
  const c = propBase(5);
  c.rect(3, 6, 26, 24, hex(pal.woodDark));
  c.rect(5, 8, 22, 20, hex(pal.wood));
  for (let i = 0; i < 22; i++) {
    c.set(5 + i, 8 + Math.round((i * 20) / 22), hex(pal.woodLight));
  }
  c.frame(5, 8, 22, 20, hex(pal.woodDark));
  c.outline(hex(pal.ink));
  return c;
}

function tileChest(pal, tier, opened) {
  const c = propBase(6);
  const metal = tier === "gold" ? pal.gold : tier === "silver" ? pal.silver : pal.copper;
  const metalLight = tier === "gold" ? pal.goldLight : tier === "silver" ? pal.silverLight : pal.copperLight;
  if (opened) {
    c.rect(4, 4, 24, 8, hex(pal.woodDark));
    c.rect(5, 3, 22, 6, hex(metal));
    c.hLine(5, 3, 22, hex(metalLight));
    c.rect(6, 12, 20, 16, hex(pal.woodDark));
    c.rect(8, 14, 16, 12, hex(pal.wood));
    c.hLine(8, 14, 16, hex(shade(pal.ink, 0.2)));
  } else {
    c.rect(4, 8, 24, 20, hex(pal.woodDark));
    c.rect(5, 6, 22, 8, hex(metal));
    c.rect(6, 14, 20, 13, hex(pal.wood));
    c.hLine(6, 14, 20, hex(pal.woodLight));
    c.rect(14, 6, 4, 21, hex(metal));
    c.rect(15, 6, 2, 21, hex(metalLight));
    c.hLine(5, 6, 22, hex(metalLight));
    c.rect(13, 18, 6, 6, hex(metalLight));
    c.rect(14, 19, 4, 4, hex(shade(pal.ink, 0.15)));
  }
  c.outline(hex(pal.ink));
  return c;
}

function tileBarrel(pal, seed) {
  const c = propBase(7);
  c.rect(6, 7, 20, 20, hex(pal.woodDark));
  c.rect(7, 8, 18, 18, hex(pal.wood));
  for (let x = 9; x < 24; x += 5) c.vLine(x, 8, 18, hex(pal.woodLight));
  c.hLine(6, 11, 20, hex(pal.metalDark));
  c.hLine(6, 22, 20, hex(pal.metalDark));
  c.ellipse(16, 8, 10, 3, hex(pal.woodDark));
  c.ellipse(16, 8, 8, 2, hex(pal.woodLight));
  c.outline(hex(pal.ink));
  return c;
}

// ---------------------------------------------------------------- 图集组装

/** 每一格在 atlas 里的位置 → 画法。顺序无关，重复坐标后者覆盖前者。 */
const LAYOUT = [
  // 地面
  [[0, 0], (p, s) => tileGrass(p, "plain", s)],
  [[1, 0], (p, s) => tileGrass(p, "tuft", s + 1)],
  [[2, 0], (p, s) => tileGrass(p, "flower", s + 2)],
  [[7, 3], (p, s) => tileGrass(p, "pebble", s + 3)],
  // 土路九宫（n/s/w/e = 该侧接草）
  [[1, 2], (p, s) => tileDirt(p, {}, s + 10)],
  [[1, 1], (p, s) => tileDirt(p, { n: true }, s + 11)],
  [[1, 3], (p, s) => tileDirt(p, { s: true }, s + 12)],
  [[0, 2], (p, s) => tileDirt(p, { w: true }, s + 13)],
  [[2, 2], (p, s) => tileDirt(p, { e: true }, s + 14)],
  [[0, 1], (p, s) => tileDirt(p, { n: true, w: true }, s + 15)],
  [[2, 1], (p, s) => tileDirt(p, { n: true, e: true }, s + 16)],
  [[0, 3], (p, s) => tileDirt(p, { s: true, w: true }, s + 17)],
  [[2, 3], (p, s) => tileDirt(p, { s: true, e: true }, s + 18)],
  // 室内地面
  [[1, 9], (p, s) => tileFloor(p, false, s + 20, p.__floorStyle)],
  [[1, 8], (p, s) => tileFloor(p, true, s + 21, p.__floorStyle)],
  // 植被
  [[4, 0], (p, s) => tilePine(p, s + 30)],
  [[4, 1], (p, s) => tileTree(p, false, s + 31)],
  [[3, 1], (p, s) => tileTree(p, true, s + 32)],
  [[7, 2], (p, s) => tileSmallTree(p, false, s + 33)],
  [[5, 0], (p, s) => tileBush(p, s + 34)],
  [[7, 0], (p, s) => tileForest(p, false, s + 35)],
  [[10, 1], (p, s) => tileForest(p, true, s + 36)],
  [[5, 2], (p, s) => tileMushroom(p, s + 37)],
  // 建筑
  [[5, 4], (p) => tileRoof(p, "B", true)],
  [[5, 5], (p) => tileRoof(p, "B", false)],
  [[1, 4], (p) => tileRoof(p, "A", true)],
  [[1, 5], (p) => tileRoof(p, "A", false)],
  [[7, 5], (p) => tileGable(p, "A")],
  [[5, 6], (p, s) => tileWall(p, p.__style, s + 40)],
  [[4, 7], (p) => tileWindow(p, p.__style)],
  [[7, 7], (p) => tileDoor(p, p.__style)],
  [[1, 6], (p, s) => tileWall(p, "wood", s + 41)],
  [[0, 7], (p) => tileWindow(p, "wood")],
  [[1, 7], (p) => tileDoor(p, "wood")],
  [[4, 8], (p, s) => tileBattlement(p, s + 42)],
  [[3, 8], (p, s) => tileWallCorner(p, s + 43)],
  [[4, 9], (p, s) => tileArch(p, s + 44)],
  // 物件
  [[9, 3], (p) => tileFence(p, "h")],
  [[10, 3], (p) => tileFence(p, "post")],
  [[11, 3], (p) => tileFence(p, "v")],
  [[10, 7], (p, s) => tileWell(p, s + 50)],
  [[10, 6], (p) => tileSign(p)],
  [[9, 6], (p) => tileBench(p)],
  [[11, 6], (p) => tileCrate(p)],
  [[10, 8], (p) => tileChest(p, "copper", false)],
  [[11, 8], (p, s) => tileBarrel(p, s + 51)],
  // 特殊（15 §4.1）：水面／水岸／芦苇／脚印／血迹
  [[6, 0], (p, s) => tileWaterDeep(p, s + 60)],
  [[6, 1], (p, s) => tileWaterShallow(p, s + 61)],
  [[6, 2], (p, s) => tileWaterShore(p, s + 62)],
  [[6, 3], (p, s) => tileReeds(p, s + 63)],
  [[6, 4], (p, s) => tileFootprint(p, s + 64)],
  [[6, 5], (p, s) => tileBlood(p, s + 65)],
  // 石隙迷窟专用（只收进 shixi 那套主题）
  [[6, 6], (p, s) => tileSkySlit(p, s + 70)],
  [[6, 7], (p) => tileShrinePlatform(p)],
  [[6, 8], (p, s) => tileRubble(p, s + 71)],
  [[6, 9], (p, s) => tileRockEave(p, s + 72)],
  // 大地图的山（16 §3.5）：成片用的九宫格 ＋ 两档山峰 ＋ 两个洞口 ＋ 路牌
  [[2, 4], (p, s) => tileMountain(p, { n: true, w: true }, s + 80)],
  [[3, 4], (p, s) => tileMountain(p, { n: true }, s + 81)],
  [[4, 4], (p, s) => tileMountain(p, { n: true, e: true }, s + 82)],
  [[2, 5], (p, s) => tileMountain(p, { w: true }, s + 83)],
  [[3, 5], (p, s) => tileMountain(p, {}, s + 84)],
  [[4, 5], (p, s) => tileMountain(p, { e: true }, s + 85)],
  [[2, 6], (p, s) => tileMountain(p, { s: true, w: true }, s + 86)],
  [[3, 6], (p, s) => tileMountain(p, { s: true }, s + 87)],
  [[4, 6], (p, s) => tileMountain(p, { s: true, e: true }, s + 88)],
  [[0, 4], (p, s) => tileMountainPeak(p, s + 89, false)],
  [[0, 5], (p, s) => tileMountainPeak(p, s + 90, true)],
  [[0, 6], (p, s) => tileCaveRecess(p, s + 91)],
  [[7, 4], (p, s) => tileCrackMouth(p, s + 92)],
  [[7, 6], (p) => tileSignpost(p)],
  // 岩面第二变体：大片山体会把同一格铺几百次，两个变体交替才不露"瓦片格子"
  [[8, 4], (p, s) => tileMountain(p, {}, s + 93)],
];

/** 建一张主题图集。 */
function buildAtlas(themeName) {
  const theme = THEMES[themeName];
  if (!theme) throw new Error(`未知主题：${themeName}`);
  const pal = Object.assign({}, theme.pal);
  pal.__style = theme.wallStyle;
  pal.__floorStyle = theme.floorStyle || "paving";
  pal.__detail = theme.detail || "dry";
  pal.__rockFace = theme.wallStyle === "crack" ? "crack" : null;
  const atlas = new Canvas(TILE * ATLAS_COLS, TILE * ATLAS_ROWS);
  let seed = 1000;
  for (const [[cx, cy], paint] of LAYOUT) {
    seed += 7;
    atlas.blit(paint(pal, seed), cx * TILE, cy * TILE);
  }
  return atlas;
}

/**
 * 格点值噪声 + 双线性插值：像素级白噪声不行（会像电视雪花），要的是连绵的云。
 * **格点坐标按周期取模**——这样噪声本身在 32px 处闭合，平铺时不会出现「格子纸」接缝。
 */
function valueNoise(x, y, cell, seed, period) {
  const mod = period ? Math.round(period / cell) : 0;
  const wrap = (v) => (mod > 0 ? ((v % mod) + mod) % mod : v);
  const gx = Math.floor(x / cell);
  const gy = Math.floor(y / cell);
  const fx = (x / cell) - gx;
  const fy = (y / cell) - gy;
  const sx = fx * fx * (3 - 2 * fx);
  const sy = fy * fy * (3 - 2 * fy);
  const a = noise(wrap(gx), wrap(gy), seed);
  const b = noise(wrap(gx + 1), wrap(gy), seed);
  const c = noise(wrap(gx), wrap(gy + 1), seed);
  const d = noise(wrap(gx + 1), wrap(gy + 1), seed);
  return (a * (1 - sx) + b * sx) * (1 - sy) + (c * (1 - sx) + d * sx) * sy;
}

/**
 * 探索迷雾：云气。
 *
 * 16 §3.3 的两条要求合起来其实很具体——**未探索要「完全遮住」，但还要透出地形轮廓的深浅**，
 * 所以不是纯色块、也不是半透明纱：底色 alpha 接近 1，只有明度在动（树影／水面透过来的是
 * 「这里比那里暗一点」）。第一版按半透明纱画，结果整张大地图的地形全露着，雾等于没铺。
 */
function buildFog() {
  const c = new Canvas(TILE, TILE);
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      // 云面全靠**颗粒**而不是形状：单张 32px 瓦片平铺整张大地图，
      // 任何成形的东西（团块、斜纹）都会被一眼看出是「同一格重复了 32 次」，
      // 而颗粒噪声看不出周期；地形轮廓则从 alpha 里透出来。
      const coarse = valueNoise(x, y, 16, 4242, TILE) - 0.5;
      const n = 0.5 + coarse * 0.12 + (noise(x, y, 5150) - 0.5) * 0.34;
      const color = mix("#98A2AA", "#B2BCC4", n);
      c.set(x, y, [color[0], color[1], color[2], Math.round(238 + n * 14)]);
    }
  }
  return c;
}

/** 云气必须**无缝**（整张大地图平铺它）：把 3×3 拼接后取中间那格，边缘就自然卷上了。 */
function buildFogSeamless() {
  const big = new Canvas(TILE * 3, TILE * 3);
  for (let ty = 0; ty < 3; ty++) {
    for (let tx = 0; tx < 3; tx++) big.blit(buildFog(), tx * TILE, ty * TILE);
  }
  const out = new Canvas(TILE, TILE);
  out.blit(big, -TILE, -TILE);
  return out;
}

module.exports = {
  TILE,
  ATLAS_COLS,
  ATLAS_ROWS,
  THEMES,
  LAYOUT,
  BASE,
  palette,
  buildAtlas,
  buildFog,
  buildFogSeamless,
  encodePNG,
  tileChest,
};
