# 音效层约定（`src/audio/`）

适用：`src/audio/` 与任何"播一个音效／调音量"的工作。
仓库级约定与命令见根 `AGENTS.md`。

- **音效只在 `src/audio/sfx.gd` 里播**：事件名 → 文件路径的唯一出处是 `Sfx.EVENTS`
  （现行 6 个事件取自设计 07 §8.4 的音效位：命中／暴击／胜利／败北／宝箱／火盆）；
  各处只写 `Sfx.play("hit")`，不要自己 `load()` 音频、也不要直接调播放层。
  音频现在是 `tools/audio/make_placeholders.py` 合成的**占位音**，音效侧**按同名文件替换**即可，代码不用改。
  `addons/sound_manager/` 是**复用的第三方 MIT 代码**（v2.6.2，出处见 `docs/dev/第三方代码.md`）：
  不改它、不带它的编辑器插件、不删它的 `LICENSE`。加事件＝改 `Sfx.EVENTS` ＋ 补一条占位音
  （`test_audio` 会查"事件↔文件一一对应"，漏一边就红）。
  **音量只走 `Sfx.set_volume/cycle_volume`**（开关在枢纽页；静音写 -80 dB 而不是 `linear_to_db(0)` 的 `-inf`），
  设置落盘在**存档目录旁的 `settings.cfg`**（`SettingsStore`，目录解析与存档共用一处）——
  **读坏退回默认音量、写不进去不挡使用**，别把这两条改掉（`tests/test_settings.gd` 钉着）。
