// 无依赖的像素画布 + PNG 编解码（Node 内置 zlib）。
//
// 为什么不引第三方库：这台机器上没有 pip／Pillow，联网也关着；而像素画的
// 每一笔都要能复现，手写 PNG 编码器（IHDR/IDAT/IEND + zlib）反而是最稳的一条路。
// 只写不读：生成器从不回读 PNG，取色一律走调色板常量。

"use strict";

const zlib = require("zlib");

// ---------------------------------------------------------------- 颜色

function hex(color) {
  // 允许把已经算好的 [r,g,b,a] 直接喂回来，省得每处都区分「色名」和「色值」。
  if (Array.isArray(color)) return [color[0], color[1], color[2], color.length > 3 ? color[3] : 255];
  let s = color.trim();
  if (s[0] === "#") s = s.slice(1);
  if (s.length === 3) s = s[0] + s[0] + s[1] + s[1] + s[2] + s[2];
  const v = parseInt(s, 16);
  return [(v >> 16) & 255, (v >> 8) & 255, v & 255, 255];
}

function rgba(r, g, b, a) {
  return [r, g, b, a === undefined ? 255 : a];
}

/** 往白／黑方向推色相不变的明度。t>0 提亮，t<0 压暗。 */
function shade(color, t) {
  const c = hex(color);
  const target = t >= 0 ? 255 : 0;
  const k = Math.abs(t);
  return [
    Math.round(c[0] + (target - c[0]) * k),
    Math.round(c[1] + (target - c[1]) * k),
    Math.round(c[2] + (target - c[2]) * k),
    c[3],
  ];
}

/** 两色插值，t=0 取 a，t=1 取 b。 */
function mix(a, b, t) {
  const ca = hex(a);
  const cb = hex(b);
  return [
    Math.round(ca[0] + (cb[0] - ca[0]) * t),
    Math.round(ca[1] + (cb[1] - ca[1]) * t),
    Math.round(ca[2] + (cb[2] - ca[2]) * t),
    Math.round(ca[3] + (cb[3] - ca[3]) * t),
  ];
}

/** 去饱和（只留明度），给「未解锁／变暗」态用。 */
function desaturate(color, k) {
  const c = hex(color);
  const l = Math.round(0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]);
  return [
    Math.round(c[0] + (l - c[0]) * k),
    Math.round(c[1] + (l - c[1]) * k),
    Math.round(c[2] + (l - c[2]) * k),
    c[3],
  ];
}

function withAlpha(color, a) {
  const c = hex(color);
  return [c[0], c[1], c[2], Math.round(a * 255)];
}

// ---------------------------------------------------------------- 画布

class Canvas {
  constructor(w, h) {
    this.w = w;
    this.h = h;
    this.data = new Uint8Array(w * h * 4);
  }

  idx(x, y) {
    return (y * this.w + x) * 4;
  }

  /** 直接覆盖（含 alpha）。 */
  set(x, y, color) {
    x = Math.round(x);
    y = Math.round(y);
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return;
    const i = this.idx(x, y);
    this.data[i] = color[0];
    this.data[i + 1] = color[1];
    this.data[i + 2] = color[2];
    this.data[i + 3] = color.length > 3 ? color[3] : 255;
  }

  /** 带 alpha 混合的落笔。 */
  blend(x, y, color, alpha) {
    x = Math.round(x);
    y = Math.round(y);
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return;
    const a = (color.length > 3 ? color[3] / 255 : 1) * (alpha === undefined ? 1 : alpha);
    const i = this.idx(x, y);
    const dstA = this.data[i + 3] / 255;
    const outA = a + dstA * (1 - a);
    if (outA <= 0) return;
    for (let c = 0; c < 3; c++) {
      this.data[i + c] = Math.round(
        (color[c] * a + this.data[i + c] * dstA * (1 - a)) / outA,
      );
    }
    this.data[i + 3] = Math.round(outA * 255);
  }

  get(x, y) {
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return [0, 0, 0, 0];
    const i = this.idx(x, y);
    return [this.data[i], this.data[i + 1], this.data[i + 2], this.data[i + 3]];
  }

  fill(color) {
    this.rect(0, 0, this.w, this.h, color);
  }

  rect(x, y, w, h, color) {
    for (let j = 0; j < h; j++) {
      for (let i = 0; i < w; i++) this.set(x + i, y + j, color);
    }
  }

  rectBlend(x, y, w, h, color, alpha) {
    for (let j = 0; j < h; j++) {
      for (let i = 0; i < w; i++) this.blend(x + i, y + j, color, alpha);
    }
  }

  /** 只画边框（w/h 含线宽）。 */
  frame(x, y, w, h, color) {
    this.rect(x, y, w, 1, color);
    this.rect(x, y + h - 1, w, 1, color);
    this.rect(x, y, 1, h, color);
    this.rect(x + w - 1, y, 1, h, color);
  }

  hLine(x, y, w, color) {
    this.rect(x, y, w, 1, color);
  }

  vLine(x, y, h, color) {
    this.rect(x, y, 1, h, color);
  }

  /** 实心椭圆（像素块状，不是抗锯齿圆）。 */
  ellipse(cx, cy, rx, ry, color) {
    for (let y = Math.floor(cy - ry); y <= Math.ceil(cy + ry); y++) {
      for (let x = Math.floor(cx - rx); x <= Math.ceil(cx + rx); x++) {
        const dx = (x - cx) / rx;
        const dy = (y - cy) / ry;
        if (dx * dx + dy * dy <= 1.02) this.set(x, y, color);
      }
    }
  }

  disk(cx, cy, r, color) {
    this.ellipse(cx, cy, r, r, color);
  }

  /** 以 4×4 Bayer 阈值做两色抖动填充，像素画里最常用的渐层手法。 */
  dither(x, y, w, h, colorA, colorB, level, seed) {
    const bayer = [
      [0, 8, 2, 10],
      [12, 4, 14, 6],
      [3, 11, 1, 9],
      [15, 7, 13, 5],
    ];
    for (let j = 0; j < h; j++) {
      for (let i = 0; i < w; i++) {
        const b = (bayer[(y + j) % 4][(x + i) % 4] + 0.5) / 16;
        const n = noise(x + i, y + j, seed);
        const t = level * 0.75 + n * 0.25;
        this.set(x + i, y + j, b < t ? colorB : colorA);
      }
    }
  }

  /** 撒点：在指定区域随机替换成另一种颜色（像素画的「脏感」）。 */
  speckle(x, y, w, h, color, chance, seed, alpha) {
    for (let j = 0; j < h; j++) {
      for (let i = 0; i < w; i++) {
        if (noise(x + i, y + j, seed) < chance) {
          if (alpha === undefined) this.set(x + i, y + j, color);
          else this.blend(x + i, y + j, color, alpha);
        }
      }
    }
  }

  /** 把另一块画布贴进来（按 alpha 混合）。 */
  blit(other, ox, oy) {
    for (let y = 0; y < other.h; y++) {
      for (let x = 0; x < other.w; x++) {
        const p = other.get(x, y);
        if (p[3] === 0) continue;
        if (p[3] === 255) this.set(ox + x, oy + y, p);
        else this.blend(ox + x, oy + y, p, p[3] / 255);
      }
    }
  }

  /** 把整张画布按 2×2 放大（整数倍缩放，像素风的硬要求）。 */
  scaled(factor) {
    const out = new Canvas(this.w * factor, this.h * factor);
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        const p = this.get(x, y);
        for (let j = 0; j < factor; j++) {
          for (let i = 0; i < factor; i++) out.set(x * factor + i, y * factor + j, p);
        }
      }
    }
    return out;
  }

  /** 换整个画布的不透明度。 */
  faded(k) {
    const out = this.clone();
    for (let i = 3; i < out.data.length; i += 4) {
      out.data[i] = Math.round(out.data[i] * k);
    }
    return out;
  }

  /** 去饱和 + 压暗：未解锁／禁用态。 */
  muted(satK, alphaK) {
    const out = this.clone();
    for (let i = 0; i < out.data.length; i += 4) {
      const c = desaturate(
        `#${[out.data[i], out.data[i + 1], out.data[i + 2]]
          .map((v) => v.toString(16).padStart(2, "0"))
          .join("")}`,
        satK,
      );
      out.data[i] = c[0];
      out.data[i + 1] = c[1];
      out.data[i + 2] = c[2];
      out.data[i + 3] = Math.round(out.data[i + 3] * alphaK);
    }
    return out;
  }

  /**
   * 给不透明像素团外缘描一圈（墨黑系轮廓）。
   * **画布外面不算相邻**——与画布边缘相接的像素（栅栏的横杆、密林的树冠）是给
   * 邻居瓦片续用的，描边会在每格边界画出一圈方框，整张地图都会变成「格子纸」。
   */
  outline(color) {
    const src = this.clone();
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        if (src.get(x, y)[3] !== 0) continue;
        let touches = false;
        for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
          const nx = x + dx;
          const ny = y + dy;
          if (nx < 0 || ny < 0 || nx >= this.w || ny >= this.h) continue;
          if (src.get(nx, ny)[3] > 128) touches = true;
        }
        if (touches) this.set(x, y, color);
      }
    }
  }

  /** 把画布外缘一圈变成透明：地面瓦片接缝用的柔化。 */
  clearEdge() {
    for (let x = 0; x < this.w; x++) {
      this.set(x, 0, [0, 0, 0, 0]);
      this.set(x, this.h - 1, [0, 0, 0, 0]);
    }
    for (let y = 0; y < this.h; y++) {
      this.set(0, y, [0, 0, 0, 0]);
      this.set(this.w - 1, y, [0, 0, 0, 0]);
    }
  }

  clone() {
    const out = new Canvas(this.w, this.h);
    out.data.set(this.data);
    return out;
  }
}

// ---------------------------------------------------------------- 噪声

/** 确定性哈希 → [0,1)。同一坐标永远同一个值，生成结果可复现。 */
function noise(x, y, seed) {
  let h = (x * 374761393 + y * 668265263 + (seed || 0) * 1442695041) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/** 小工具：按种子取「第 n 个」伪随机数，写循环时比维护 rng 状态清爽。 */
function rnd(seed, n) {
  return noise(seed * 7919 + n * 104729, (n * 31) ^ seed, 12345);
}

function pick(list, seed, n) {
  return list[Math.floor(rnd(seed, n) * list.length) % list.length];
}

// ---------------------------------------------------------------- PNG

const CRC_TABLE = (() => {
  const table = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c;
  }
  return table;
})();

function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 255] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length, 0);
  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body), 0);
  return Buffer.concat([len, body, crc]);
}

function encodePNG(canvas) {
  const { w, h, data } = canvas;
  const stride = w * 4;
  const raw = Buffer.alloc((stride + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (stride + 1)] = 0; // filter type 0（None）——像素画没有渐变，压缩率够用
    Buffer.from(data.buffer, data.byteOffset + y * stride, stride).copy(
      raw,
      y * (stride + 1) + 1,
    );
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // RGBA
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", zlib.deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

module.exports = {
  Canvas,
  hex,
  rgba,
  shade,
  mix,
  desaturate,
  withAlpha,
  noise,
  rnd,
  pick,
  encodePNG,
};
