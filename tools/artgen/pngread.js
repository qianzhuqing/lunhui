// 只读 PNG 解码（Node 内置 zlib）。
//
// 为什么单独一个文件：`pixel.js` 明写「只写不读——生成器从不回读 PNG」，
// 而「从示意图裁素材」这条路的第一步恰恰是**读**。两边分开放，
// 免得哪天顺手把解码引进生成链（那条链的确定性靠的是"每笔都是数据"，不是回读位图）。
//
// 支持范围按本项目实际用途收窄：8bit、非隔行、color type 0／2／4／6
// （`docs/dev/images/` 那批示意图实测都是 RGBA8）。够用就不写通用解码器。

"use strict";

const fs = require("fs");
const zlib = require("zlib");

const CHANNELS = { 0: 1, 2: 3, 4: 2, 6: 4 };

/** 解出一张图的 RGBA 采样：{ w, h, ch（每像素通道数）, stride, px }。 */
function decode(file) {
  const buf = fs.readFileSync(file);
  let off = 8, w = 0, h = 0, bitDepth = 0, colorType = 0, interlace = 0;
  const idat = [];
  while (off < buf.length) {
    const len = buf.readUInt32BE(off);
    const type = buf.toString("ascii", off + 4, off + 8);
    const data = buf.subarray(off + 8, off + 8 + len);
    if (type === "IHDR") {
      w = data.readUInt32BE(0);
      h = data.readUInt32BE(4);
      bitDepth = data[8];
      colorType = data[9];
      interlace = data[12];
    } else if (type === "IDAT") {
      idat.push(data);
    }
    off += 12 + len;
  }
  if (bitDepth !== 8) throw new Error(`${file}：只支持 8bit，这张是 ${bitDepth}bit`);
  if (interlace) throw new Error(`${file}：不支持隔行 PNG`);
  const ch = CHANNELS[colorType];
  if (!ch) throw new Error(`${file}：不支持 color type ${colorType}`);

  const raw = zlib.inflateSync(Buffer.concat(idat));
  const stride = w * ch;
  const px = Buffer.alloc(h * stride);
  let prev = Buffer.alloc(stride);
  for (let y = 0; y < h; y++) {
    const filter = raw[y * (stride + 1)];
    const cur = Buffer.from(raw.subarray(y * (stride + 1) + 1, y * (stride + 1) + 1 + stride));
    for (let i = 0; i < stride; i++) {
      const a = i >= ch ? cur[i - ch] : 0;
      const b = prev[i];
      const c = i >= ch ? prev[i - ch] : 0;
      if (filter === 1) cur[i] = (cur[i] + a) & 255;
      else if (filter === 2) cur[i] = (cur[i] + b) & 255;
      else if (filter === 3) cur[i] = (cur[i] + ((a + b) >> 1)) & 255;
      else if (filter === 4) {
        const p = a + b - c;
        const pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
        cur[i] = (cur[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c)) & 255;
      }
    }
    cur.copy(px, y * stride);
    prev = cur;
  }
  return { w, h, ch, stride, px };
}

/** 取 (x,y) 的 RGBA；越界返回全透明。 */
function at(img, x, y) {
  if (x < 0 || y < 0 || x >= img.w || y >= img.h) return [0, 0, 0, 0];
  const i = y * img.stride + x * img.ch;
  const r = img.px[i], g = img.px[i + 1], b = img.px[i + 2];
  const a = img.ch === 4 ? img.px[i + 3] : img.ch === 2 ? img.px[i + 1] : 255;
  return [r, g, b, a];
}

const gcd = (a, b) => (b ? gcd(b, a % b) : a);

/**
 * 判「这张示意图放大了几倍」：像素画的放大是整数倍最近邻，**所有颜色变化的边缘
 * 坐标必然落在倍数上**，所以对全部边缘坐标取最大公约数就是了。
 * 返回值 1 说明这张图不是整数倍像素画（是平滑缩放或真手绘），裁图那条路对它无效。
 */
function probeScale(img) {
  let gx = 0, gy = 0;
  for (let y = 0; y < img.h; y++) {
    for (let x = 1; x < img.w; x++) {
      const i = y * img.stride + x * img.ch;
      const j = i - img.ch;
      if (img.px[i] !== img.px[j] || img.px[i + 1] !== img.px[j + 1] || img.px[i + 2] !== img.px[j + 2]) {
        gx = gx ? gcd(gx, x) : x;
      }
    }
  }
  for (let y = 1; y < img.h; y++) {
    for (let x = 0; x < img.w; x++) {
      const i = y * img.stride + x * img.ch;
      const j = i - img.stride;
      if (img.px[i] !== img.px[j] || img.px[i + 1] !== img.px[j + 1] || img.px[i + 2] !== img.px[j + 2]) {
        gy = gy ? gcd(gy, y) : y;
      }
    }
  }
  return gcd(gx, gy);
}

module.exports = { decode, at, probeScale };
