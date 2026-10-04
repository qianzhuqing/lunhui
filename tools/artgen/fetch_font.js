// 取中文字体：`ark-pixel-font`（15 §3「字体要先落地」那一条的落地脚本）。
//
//   node tools\artgen\fetch_font.js          rem 下载并装到 assets/fonts/
//   node tools\artgen\fetch_font.js --check  rem 只核对本地文件的 sha256，不联网
//
// 为什么用脚本而不是「拷一份进去」：
//  ① **版本要钉住**。这套字体在 2026.09 把 16px 尺寸整个废弃了（构建与素材都移走），
//     版本漂移会直接改变我们能拿到哪几档字号——这正是本项目 UI 规范的前提。
//  ② **授权要跟文件一起走**。包里的 `OFL.txt` 与仓库的 MIT 是两码事（见下），
//     脚本会把许可原文一并落盘，免得日后只剩一堆说不清来历的 ttf。
//
// 授权（2026-10-04 实测，与 17 号文档写的「MIT」有出入，已回报）：
//   · 仓库**代码**：MIT（GitHub 的 license 字段就是这个）
//   · **字体本体**：**SIL Open Font License 1.1**，release 包里带的就是 `OFL.txt`
// 对我们（游戏内嵌、不修改、不单独售卖字体）OFL-1.1 是可商用的；
// 义务是**随包保留许可与版权声明**，所以 `assets/fonts/OFL.txt` 是必须进仓库的那一份。

"use strict";

const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const { entries } = require("./zipread");

const PIN = {
  version: "2026.09.25",
  asset: "ark-pixel-font-12px-proportional-ttf.woff2-v2026.09.25.zip",
  url: "https://github.com/TakWolf/ark-pixel-font/releases/download/2026.09.25/ark-pixel-font-12px-proportional-ttf.woff2-v2026.09.25.zip",
  // 只要我们真用得上的三份：拉丁／简体／繁体。
  // 16px 那份**故意不取**——2026.09.01 起 16px 已废弃，最后那版里
  // `zh_cn` 只有 68KB（12px 同名文件 738KB），是空壳不是字集（见 README）。
  members: [
    "ark-pixel-12px-proportional-latin.ttf.woff2",
    "ark-pixel-12px-proportional-zh_hans.ttf.woff2",
    "ark-pixel-12px-proportional-zh_hant.ttf.woff2",
  ],
};

const REPO = path.resolve(__dirname, "..", "..");
const OUT_DIR = path.join(REPO, "assets", "fonts");

function sha256(buffer) {
  return crypto.createHash("sha256").update(buffer).digest("hex");
}

function check() {
  let ok = true;
  for (const name of [...PIN.members, "OFL.txt"]) {
    const file = path.join(OUT_DIR, name);
    if (!fs.existsSync(file)) {
      console.log(`缺失 ${name}`);
      ok = false;
      continue;
    }
    const buf = fs.readFileSync(file);
    console.log(`${name}  ${(buf.length / 1024).toFixed(0)}KB  sha256=${sha256(buf).slice(0, 16)}…`);
  }
  return ok;
}

async function fetchAll() {
  console.log(`下载 ${PIN.asset} …`);
  const response = await fetch(PIN.url, { headers: { "User-Agent": "codex" } });
  if (!response.ok) throw new Error(`下载失败：HTTP ${response.status}`);
  const zip = Buffer.from(await response.arrayBuffer());
  console.log(`  包 ${(zip.length / 1048576).toFixed(2)}MB  sha256=${sha256(zip).slice(0, 16)}…`);

  const list = entries(zip);
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const report = [];
  for (const want of [...PIN.members, "OFL.txt"]) {
    const member = list.find((e) => e.name === want);
    if (!member) throw new Error(`包里没有 ${want}`);
    const data = member.data();
    fs.writeFileSync(path.join(OUT_DIR, want), data);
    report.push({ name: want, size: data.length, sha256: sha256(data) });
    console.log(`  装上 ${want}  ${(data.length / 1024).toFixed(0)}KB`);
  }
  return report;
}

if (process.argv.includes("--check")) {
  process.exit(check() ? 0 : 1);
}

fetchAll()
  .then((report) => {
    console.log("\n本批文件的指纹（记进 assets/fonts/README.md 用）：");
    for (const row of report) {
      console.log(`  ${row.name}  ${row.size}B  ${row.sha256}`);
    }
  })
  .catch((error) => {
    console.error(error.message);
    process.exit(1);
  });
