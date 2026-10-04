// 极简 ZIP 读取器（只读，够用就行）。
//
// 为什么自己写：这台机器上没有 unzip／7z，而 `pixel.js` 已经证明了
// 「Node 内置 zlib + 手写容器格式」这条路走得通——字体包只需要取出里面几个成员，
// 为这点事引一个第三方 zip 库不值得。
// 支持 stored（0）与 deflate（8）两种压缩方式，够解压 GitHub Release 的产物。

"use strict";

const zlib = require("zlib");

/** 找出中央目录，返回 [{name, size, data()}]。 */
function entries(buffer) {
  // 中央目录结束记录：签名 0x06054b50，从尾部往前找（注释长度不定）
  let eocd = -1;
  for (let i = buffer.length - 22; i >= 0 && i > buffer.length - 22 - 65536; i--) {
    if (buffer.readUInt32LE(i) === 0x06054b50) {
      eocd = i;
      break;
    }
  }
  if (eocd < 0) throw new Error("不是 ZIP：找不到中央目录");
  const count = buffer.readUInt16LE(eocd + 10);
  let offset = buffer.readUInt32LE(eocd + 16);

  const out = [];
  for (let i = 0; i < count; i++) {
    if (buffer.readUInt32LE(offset) !== 0x02014b50) throw new Error("中央目录项签名不对");
    const method = buffer.readUInt16LE(offset + 10);
    const compressedSize = buffer.readUInt32LE(offset + 20);
    const size = buffer.readUInt32LE(offset + 24);
    const nameLength = buffer.readUInt16LE(offset + 28);
    const extraLength = buffer.readUInt16LE(offset + 30);
    const commentLength = buffer.readUInt16LE(offset + 32);
    const localOffset = buffer.readUInt32LE(offset + 42);
    const name = buffer.toString("utf8", offset + 46, offset + 46 + nameLength);
    offset += 46 + nameLength + extraLength + commentLength;
    if (name.endsWith("/")) continue;

    // 本地头：签名 0x04034b50，长度字段可能和中央目录不同（取决于是否用数据描述符）
    if (buffer.readUInt32LE(localOffset) !== 0x04034b50) throw new Error(`本地头签名不对：${name}`);
    const localNameLength = buffer.readUInt16LE(localOffset + 26);
    const localExtraLength = buffer.readUInt16LE(localOffset + 28);
    const start = localOffset + 30 + localNameLength + localExtraLength;
    const raw = buffer.subarray(start, start + compressedSize);
    out.push({
      name,
      size,
      data: () => (method === 0 ? raw : zlib.inflateRawSync(raw)),
    });
  }
  return out;
}

module.exports = { entries };
