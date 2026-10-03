"""Synthesise every placeholder sound effect the game plays.

    07_地图资源需求.md §8.4 列了音效位（火盆点燃／宝箱开启／命中…），框架说明的扩展点里
    也写着战斗表现只剩「逐帧贴图 + 命中音效」这类资产依赖。音效素材没到之前，这里按
    `tools/mapgen/make_placeholders.py` 的同一套办法**程序化**生成「一听就知道是占位」的短音，
    让 `src/audio/sfx.gd` 接好的事件真的有东西可放。

Usage:
    python tools/audio/make_placeholders.py

Outputs (22050 Hz / 16-bit / 单声道，每条 0.1~0.5 秒):
    assets/audio/sfx/sfx_hit.wav      命中
    assets/audio/sfx/sfx_crit.wav     暴击
    assets/audio/sfx/sfx_victory.wav  战斗胜利
    assets/audio/sfx/sfx_defeat.wav   败北
    assets/audio/sfx/sfx_chest.wav    宝箱开启
    assets/audio/sfx/sfx_brazier.wav  火盆点燃

    文件名的唯一引用点是 `src/audio/sfx.gd` 的 `EVENTS`；音效侧替换正式素材时**保持同名**即可，
    代码一行都不用改（同「贴图缺失才退回色块」的口径）。
"""

import math
import os
import random
import struct
import wave

RATE = 22050
OUT_DIR = os.path.join("assets", "audio", "sfx")


def _seconds(count):
    return count / RATE


def _samples(duration):
    return max(1, int(RATE * duration))


def _fade(i, count, attack=0.02, release=0.7):
    """线性淡入 + 线性淡出：两端不归零会在扬声器上留下"咔哒"。"""
    t = i / max(1, count - 1)
    fade_in = min(1.0, t / attack) if attack > 0 else 1.0
    fade_out = min(1.0, max(0.0, (1.0 - t) / release)) if release > 0 else 1.0
    return fade_in * fade_out


def _note(freq, duration, decay=6.0, harmonics=(1.0, 0.25, 0.1)):
    """一个带衰减的简单音（基频 + 两个泛音）——占位音只需要听得出音高。"""
    count = _samples(duration)
    out = []
    for i in range(count):
        t = i / RATE
        value = 0.0
        for index, weight in enumerate(harmonics, start=1):
            value += weight * math.sin(2 * math.pi * freq * index * t)
        out.append(value * math.exp(-decay * t) * _fade(i, count))
    return out


def _noise_burst(duration, decay=30.0, rng=None):
    rng = rng or random.Random(1)
    count = _samples(duration)
    out = []
    for i in range(count):
        t = i / RATE
        out.append(rng.uniform(-1.0, 1.0) * math.exp(-decay * t) * _fade(i, count, 0.005, 0.5))
    return out


def _mix(*tracks):
    """把若干条等长的轨道相加；长度取最长，短的按 0 补齐。"""
    length = max(len(track) for track in tracks)
    mixed = [0.0] * length
    for track in tracks:
        for i, value in enumerate(track):
            mixed[i] += value
    return mixed


def _concat(*tracks):
    out = []
    for track in tracks:
        out.extend(track)
    return out


def make_hit():
    """命中：一记闷响（低频）+ 一点噪声边缘。"""
    rng = random.Random(7)
    thump = _note(90.0, 0.12, decay=28.0, harmonics=(1.0, 0.15))
    edge = _noise_burst(0.12, decay=55.0, rng=rng)
    return _mix(thump, [value * 0.45 for value in edge])


def make_crit():
    """暴击：命中 + 一记亮一点的金属余音，和普攻区分得开。"""
    rng = random.Random(11)
    return _mix(
        make_hit(),
        [value * 0.5 for value in _note(990.0, 0.22, decay=12.0, harmonics=(1.0, 0.3))],
        [value * 0.3 for value in _note(1480.0, 0.18, decay=18.0)],
        [value * 0.25 for value in _noise_burst(0.08, decay=80.0, rng=rng)],
    )


def make_victory():
    """胜利：三个上行的短音阶。"""
    return _concat(
        _note(523.25, 0.14, decay=7.0),
        _note(659.25, 0.14, decay=6.0),
        _note(783.99, 0.26, decay=4.5),
    )


def make_defeat():
    """败北：两个下行的长音，尾音拖一点。"""
    return _concat(
        _note(392.00, 0.20, decay=5.0),
        _note(261.63, 0.42, decay=3.0, harmonics=(1.0, 0.3, 0.15)),
    )


def make_chest():
    """宝箱：先"吱"一声（低频调制的噪声），再一串细碎的光点音。"""
    rng = random.Random(23)
    creak_count = _samples(0.18)
    creak = []
    for i in range(creak_count):
        t = i / RATE
        wobble = math.sin(2 * math.pi * 24.0 * t)
        creak.append(
            rng.uniform(-1.0, 1.0) * 0.5 * (0.6 + 0.4 * wobble) * _fade(i, creak_count, 0.05, 0.6)
        )
    sparkle = _concat(
        [value * 0.5 for value in _note(1568.0, 0.09, decay=22.0)],
        [value * 0.5 for value in _note(2093.0, 0.12, decay=18.0)],
    )
    return _mix(_concat(creak, [0.0] * len(sparkle)), _concat([0.0] * creak_count, sparkle))


def make_brazier():
    """火盆点燃：一口气吹上去的"呼"声——噪声过一个截止频率先升后降的一阶低通。"""
    rng = random.Random(31)
    count = _samples(0.45)
    out = []
    previous = 0.0
    for i in range(count):
        t = i / max(1, count - 1)
        # 截止频率：0.25 → 0.65 再落回 0.2，对应"吹旺—收尾"
        cutoff = 0.25 + 0.4 * math.sin(math.pi * t)
        previous += cutoff * (rng.uniform(-1.0, 1.0) - previous)
        out.append(previous * _fade(i, count, 0.08, 0.65))
    return out


SOUNDS = {
    "sfx_hit": make_hit,
    "sfx_crit": make_crit,
    "sfx_victory": make_victory,
    "sfx_defeat": make_defeat,
    "sfx_chest": make_chest,
    "sfx_brazier": make_brazier,
}


def _normalise(samples, peak=0.85):
    loudest = max((abs(value) for value in samples), default=0.0)
    if loudest <= 0.0:
        return samples
    scale = peak / loudest
    return [max(-1.0, min(1.0, value * scale)) for value in samples]


def write_wav(path, samples):
    with wave.open(path, "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(RATE)
        frames = b"".join(
            struct.pack("<h", int(max(-1.0, min(1.0, value)) * 32767)) for value in samples
        )
        handle.writeframes(frames)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, builder in SOUNDS.items():
        samples = _normalise(builder())
        path = os.path.join(OUT_DIR, name + ".wav")
        write_wav(path, samples)
        print(f"{path}  {len(samples)} samples / {_seconds(len(samples)):.2f}s")


if __name__ == "__main__":
    main()
