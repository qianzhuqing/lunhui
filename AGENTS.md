# 约定摘要

《轮回》是一个 Godot 4.7（GDScript，Mobile 渲染器）的武侠回合制 RPG。
策划文档在 `docs/design/`，工程说明在 `docs/dev/框架说明.md`。
**`docs/dev/设计实现对照.md`** 是「设计写了什么 → 代码在哪」的反向清单（找漏项用，与正查的
`docs/dev/模块对接表.csv` 互补）；策划改需求后顺手核对相关行。

> **本文件只放「仓库级约定 + 入口」。分系统的细则放在各自目录的 `AGENTS.md` 里**，
> 你改到那块代码时它们自动生效——先看下面这张表，别在这里再抄一份、也别在别处另立一份：
>
> | 要改的地方 | 细则是哪一份 |
> |---|---|
> | `src/core/`：状态与存档、成长、队伍与剧情、物品与装备、武学、战斗管线、副本 | `src/core/AGENTS.md` |
> | `src/world/`：大地图与揭雾、小地图落点与出口、明雷、输入映射、地图资产 | `src/world/AGENTS.md` |
> | `src/ui/`：场景骨架、版式预算、面板背板、玩家可见文案、HUD 按键提示 | `src/ui/AGENTS.md` |
> | `src/audio/`：音效事件、音量与设置落盘 | `src/audio/AGENTS.md` |
> | `tests/`：断言纪律、夹具、确定性、插调用点 | `tests/AGENTS.md` |
> | `tools/`：脚本编码/行尾、两道校验网、变异探针、单场景自检、总验收链路 | `tools/AGENTS.md` |
> | `data/`：CSV 唯一源、枚举三处、上限口径 | `data/AGENTS.md` |

## 常用命令

> 想快速了解现状（能玩到什么、缺什么），先看 `docs/dev/当前状态.md` 这一页看板。

```bat
tools\run_tests.bat        rem 生成配置表 → 刷新类缓存 → 跑自检，退出码 0 = 全绿
tools\run_all_checks.bat   rem 一条命令跑完「自检 + 表校验 + 配表审计 + 地图验收（07 §9）+ 跨场景闭环 + 11 个真实场景自检」；步数看它末尾的 passed steps
tools\run_mutation_sweep.bat rem 变异探针（手动·只读）：故意改坏数据，看两道校验网抓不抓得住；退出码 0 = 每条都被抓住
tools\run_mutation_sweep.bat code rem 再跑第三块「代码常量」（每个 case 一次完整自检，约 10 分钟）：改坏了手感常量有没有测试会红
```

> `code` 那一块的清单在 `tools\analysis_mutations_code.ps1`。**有意不钉**的常量写进那里的
> `$intentionallyUnpinned`（附理由，且每个 key 必须对应一个真实 case，否则当场 `[INVALID]`），
> 别直接删 case 了事——删了就没记录，下次会当新洞再追一遍。新增「决定事件发不发」的常量时，
> 先补一条绝对值断言，再加一条 case。

**带窗口的真实渲染冒烟**（headless 抓不到渲染路径的问题：贴图导入／着色器／字体；改了界面、
贴图或字体之后手动跑一次。**不接进验收**——验收要能在无图形环境跑）：

```bat
tools\run_windowed_smoke.bat
```

它依次跑**全部 11 个**场景自检（真窗口、D3D12 + Forward Mobile；与 `run_all_checks.bat` 同一份清单，
2026-10-03 从"三个最重的场景"扩到全量——另外 8 个面板此前从没在真窗口里渲染过，见框架说明决策 200），逐个打印 `[OK]/[FAIL]`，
末尾 `WINDOWED SMOKE: PASSED`、退出码 0 = 全绿，日志落在 `.logs\<name>_windowed_smoke.log`。
判定与 headless 那条链共用一套（`SELF-TEST: OK` 标记 ＋ `check_log_errors.bat` 的运行期错误门限），
只是不加 `--headless`、日志名带 `_windowed` 后缀——**以前这三个命令要人手工抄三行，抄错一个参数
就会静默跑成别的场景**，现在一条命令搞定（见 `docs/dev/框架说明.md` 决策 146／149）。
2026-10-03 实测 **11 个场景在真窗口下全 OK**（`passed steps: 11`，一趟约 20 秒），
日志里只有一条与图形无关的系统证书报错。
排查单个场景时：给 `tools\check_scene.bat` 设上 `CHECK_SCENE_WINDOWED=1` 跑同一条调用即可。

自检失败时先看 `.logs\tests.log`。Godot 可执行文件路径可用环境变量 `GODOT_BIN` 覆盖。
`run_all_checks.bat` 那一串设计口径（为什么拆两个子脚本、`--quit-after` 兜底、日志错误门限、
「跑完不许留下引擎进程」）见 `tools/AGENTS.md` 的总验收链路一节。

## 硬性约束

下面是**跨目录**的约定；分系统的规矩在各自目录的 `AGENTS.md`（见开头那张表）。

- **版本库状态与入库方式**：仓库 HEAD 目前**只跟踪骨架**（编辑器配置、`addons/godot-mcp/`、
  `icon.svg(.import)`、`node_2d.tscn`、`project.godot`、`sync_to_git.bat`）——
  `src/ tests/ tools/ docs/ data/ scenes/ assets/ AGENTS.md` **都还没入册**；
  入库靠根目录的 `sync_to_git.bat`（`git add .` → commit → push 到 `github.com/qianzhuqing/lunhui`）。
  两条纪律：① **绝不要在这个仓库跑 `git clean -fd`／`git reset --hard`**——**未跟踪的就是整个游戏**，
  一条 `clean` 就能把它删光（清理只能精确删自己刚建的文件）；② `.gitignore` 的忽略面与
  `.gitattributes` 里 `*.bat` 的行尾有门限盯着（`_check_git_repo_contract`），别绕开。
  **怎么判断现在是哪种状态**（别信日期，信命令）：`git ls-files | wc -l` 只有十几个就是"还没同步"；
  几百个（`src/ docs/ …` 都在列）就是已同步——同步过之后上面"只跟踪骨架"那句当历史看，
  **两条纪律与两条门限照旧**。
- **新加的公共接口要真的接进游戏路径**：`tests/test_handshake.gd::_check_no_new_test_only_api` 会扫
  「定义了但 `src/`＋`scenes/` 里没人调用」的函数（`_` 开头的跳过），只在白名单 `TEST_ONLY_API_ALLOWED`
  里才放行。真出了这类坑：图鉴奖励 `codex_bonus()` 写好却只有用例在调，游戏里收集再多也不涨属性。
  要么接上，要么把名字与理由写进白名单。
- **同一事实只许有一处定义**（漂移了不报错的那种）：`LOCKED_SCENES` 归 `WorldMapService`、
  贡献 kind 字符串（`attr_point`／`stat_flat`）归 `AttributeCalculator`，别在别处再写字面量。
  `tests/test_handshake.gd::_check_single_source_of_truth()` 按 `SINGLE_SOURCE_RULES` 盯着；
  新发现这类重复（**写错只会静默丢东西**）就加一条规则。场景路径那种写错就加载失败的不收（见决策 88）。
- **公式不进表**：表只配系数、上限、曲线枚举，算法写在 `src/core/`。
- 跨文件类型引用用 `preload` 常量，不要依赖 `.godot` 的全局类名缓存。
- 不要改 `addons/godot-mcp/`（编辑器插件）。`addons/sound_manager/` 是复用的第三方 MIT 库，
  它的规矩见 `src/audio/AGENTS.md`。
- 主场景是 `scenes/main_menu.tscn`（启动菜单）；`scenes/bootstrap.tscn` 是配置表诊断场景，不是入口。
- `GameData`（配置表）与 `GameSession`（当前这一局的 GameState）是 autoload；取它们不要用
  `get_node("/root/X")` 绝对路径，`--script` 模式下会失败，统一走 `Engine.get_main_loop()` 的 root 相对查找
  （见 `src/ui/main_menu.gd` 的 `_session_node`）。
- **改跨场景衔接（大地图↔小地图、战斗↔回程、驿站传送）或存读档之后必须跑 `tools\check_loop.bat`**：
  它在真引擎里跑真实场景切换（大地图→小地图／回大地图、明雷战斗→回大地图、房间战斗→回小地图）
  外加一次**真文件存读档指纹比对**。用例里大多数切场景都被 `handler` 拦下了，所以那一层只有它能看见
  （决策 48／49／50 的四个真 bug 全在这一层）。入口同样有「先走出去一次才武装」的规矩。

## 策划改了需求怎么办（强制流程）

策划每改一次需求，开发侧的**计划和验证都要跟着动**，这条由 `tests/test_handshake.gd` 把关：

1. 读 `docs/design/CHANGELOG.md`，看当前设计版本和影响模块。
2. 在 `docs/dev/模块对接表.csv` 找到自己负责的模块，评估要不要返工。
3. 改代码 → **补或改 `tests/` 里的用例**（新增能力必须有新断言，改动的数值必须有对应校验）。
4. 回填 `docs/dev/验证清单.csv`：`verify_paths` 写清用例文件，`verified_design_version` 改成当前设计版本。
   **新写的用例必须同时加进 `tools/run_tests.gd` 的 `TEST_SCRIPTS`**——只登记不执行会被
   `test_handshake` 的新门限抓出来（「登记了却从不运行」），那是以前两个门限都盖不住的方向。
5. 同步对接表的 `design_version`（对齐当前版本）与 `status`。
6. 跑 `tools\run_tests.bat` 与 `tools\validate_tables.ps1`，两个都必须绿。
7. **做完了某个系统，顺手把它从 `框架说明` 的「扩展点」表里挪走**（挪进「已经做完、别当成待做的」，
   或把那一行改成"真正剩下的部分"）。那张表的用途是"告诉接手的人还没做什么"，**挂着一个已经做完的
   条目比不写更糟**——接手的人会照着它白找一遍。它已经漂过两次：先是「装备词条生成／装备与心法加成／
   DoT 与异常状态」（2026-10-03 晚重写时删掉），再是「战斗表现打磨」（那一行说"连续帧攻击动画、
   阵亡淡出、伤害数字层级待做"，其实三样都在 `battle_screen` 里做完很久了，见决策 198）。

只要「模块已动工」而设计版本或验证版本没跟上，自检就会失败并打印要改哪一项。
另外自检还会**反查对接表自己**：`code_paths`／`data_tables`／`design_docs` 三列指向的文件必须真实存在，
`docs/design/` 下也不许有没人引用的设计文档（除 README／CHANGELOG）——那三列是给设计侧「去哪找」用的，
写错比不写更糟。
还有死代码检查：`src/` 下的脚本与 `scenes/` 下的场景都必须有人引用（路径字符串或 `class_name` 被用到）。
要留「暂时没人用」的东西，加进 `tests/test_handshake.gd` 的 `INFRASTRUCTURE_SCRIPTS` 并写明理由。

## 结构

| 目录 | 内容 |
|---|---|
| `src/core/` | 纯逻辑（`RefCounted`/`Resource`，无节点），可在 headless 下端到端跑 |
| `src/data/` | 行类与表注册表（CSV 列 ↔ 行类字段的唯一映射） |
| `src/audio/` | 音效播放层（复用 MIT 的 `godot_sound_manager`；事件名→文件只在 `Sfx.EVENTS` 一处） |
| `src/autoload/game_data.gd` | `GameData` 单例：加载表 + 校验 |
| `tests/` | 自检用例，继承 `tests/test_case.gd` |
| `tools/` | 构建与自检脚本 |

## 加功能的路子

配表（`data/tables`）→ 行类（`src/data/tables`）→ 注册表登记 → 校验规则（`src/core/table_validator.gd`）
→ 纯逻辑类（`src/core`）→ 用例（`tests/`）→ `docs/dev/框架说明.md` 补一段。

## 代码风格

标识符英文 `snake_case`（类名 `PascalCase`），注释与日志中文，注释写「为什么」而不是「做了什么」。
