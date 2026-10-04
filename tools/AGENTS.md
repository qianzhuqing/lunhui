# 工具与门限约定（`tools/`）

适用：`tools/` 下的构建脚本、校验脚本、自检开关，以及任何"跑一次验收"的工作。
仓库级约定与命令见根 `AGENTS.md`；配置表本身看 `data/AGENTS.md`；用例断言看 `tests/AGENTS.md`。

## 跑引擎的纪律

- 所有 headless 调用必须带 `--log-file <可写路径>`，否则 `user://logs` 创建失败会让 Godot 以信号 11 崩溃。
- 自检成败以日志中的 `SELFCHECK: OK` 标记为准（退出码只作辅助）。
- **引擎日志里出现 `SCRIPT ERROR` 就是失败**（`tools\check_log_errors.bat`，尽数接上）：
  GDScript 运行期错误会掐断命中的那个函数——它后面的语句、以及调用方剩下的检查**全都静默不跑**，
  而标记与退出码可能照样是绿的。看到运行期错误先修它，别当噪声过滤掉（决策 147／148）。
  另外有两类**不带 `SCRIPT ERROR` 前缀**的引擎级报错也在名单里（实测出来的签名，不是照猜）：
  `String formatting error`（`%` 格式化遇到不认识的格式符——它**不掐断函数**，所以断言照跑、
  只是消息坏了，最容易被当噪声）与 `into a TypedArray of type`（往类型化数组塞错类型）。
  名单是按证据加的：见到新签名就补一条，**但别一刀切禁掉所有 `ERROR:`**——好几处 `push_error`
  是故意的（反面例子：`DamageResolver` 拒绝 `dmg_reflect` 时就是要报）。

## 脚本文件的编码/行尾

- `tools/*.bat` 必须保持纯 ASCII：cmd.exe 用本地代码页解析，非 ASCII 会变成乱码命令。
  新增 `.bat` 一律 **CRLF 行尾**（LF-only 时 `call :label` 之类的标签查找会出问题）。
  （这两条 + `.ps1` 的 BOM 规则已经**写了门限**：`tests/test_handshake.gd::_check_repo_conventions`，
  按原始字节验，失败会点出字节位置。`run_tests.bat` 当年就是 LF-only，被这条门限抓出来的。）
- `tools/*.ps1` 带中文的话必须存成 **UTF-8 带 BOM**：Windows PowerShell 5.1 读无 BOM 的 .ps1 会按 ANSI 解，
  中文被拆坏后连语法都会报错（`audit_table_usage.ps1` 踩过，报的是莫名其妙的「缺少 }」）。

## 两道校验网

- `tools\audit_table_usage.ps1`：查「配了但 src/ 里一个字都没提」的列与常数（筛子，不是证明；行为保证在用例里）。
  它会把每行的 `@export var` 定义行去掉再用**词边界**匹配（否则每个列名都能在行类里找到自己，
  第一版因此永远报「0 死列」）；另有一份**写明理由的白名单**（如 `curve_def.formula` 是给人看的公式说明），
  白名单**外**的新死列才会让审计红——红的时候要么接上、要么把理由写进白名单。
- `tools\validate_tables.ps1` 里**按设计文档表格排版解析**的那几项比对（5.26 伤害类型／相克、5.29 职业方向、
  5.30 判定刻度）带一条**覆盖门限**：解析到 **0 行就报错**——文档改版式会让检查静默变成「0 处不一致」，
  和真比过一模一样（见框架说明决策 150）。它打印的 `比了 7/7 行` 就是覆盖率：**分母是数据，分子是比过的**，
  分子变小先去看文档表格格式；不一致本身仍是警告（改哪边由设计定）。

## 变异探针

- **要判断「一条规则到底有没有在管」，用变异探针**（2026-10-03 的做法，见框架说明决策 152）：
  在一份临时副本上把数据**故意改坏**，`validate_tables.ps1 -Root <临时目录>` 只跑校验——
  「错误/警告条数与基线一模一样」＝这条改动没被抓住。构建期的规则（`table_validator.gd`）点不到
  临时目录，要用内存副本喂（复制 `db.tables[...]`、只改内存，**不碰 CSV／.tres**）。
  两个口径：① **PS1 只是两道网里的一道**，别把「PS1 没抓到」当成「没人管」；
  ② 补完新规则后要**再跑一遍同一批改动**，确认每条都被抓住（只写"已加规则"不算数）。
  同一个办法也用来查**代码常量**：备份字节 → 只改字面量 → 跑 `run_tests.bat` → 恢复并核对哈希。
  两轮扫下来（决策 153／154）最值钱的两句教训：**测试一直传覆盖参数，默认值就成了没人走的路**
  （`simulate(…, {"max_rounds": 3})` 跑了很久，默认的 50 一次没走过——改成 10 也不红）；
  **写了没人读的版本号／字段要删掉或接上**（`Inventory.VERSION` 全项目没人读，已删）。
  另外：**「决定事件发不发」的距离要钉值，「纯移动/表现」的速度与半径不钉**（这条边界在决策 154）。

## 单个场景自检

得自己显式指定场景跑（主场景是启动菜单，自检开关在各场景里），退出码 0 = 通过：

```bat
godot --headless --path . --log-file .logs\character_smoke.log res://scenes/character_screen.tscn -- --character-selftest
godot --headless --path . --log-file .logs\world_smoke.log  res://scenes/world_run.tscn     -- --world-selftest
godot --headless --path . --log-file .logs\battle_smoke.log res://scenes/battle_screen.tscn -- --battle-selftest
godot --headless --path . --log-file .logs\local_smoke.log  res://scenes/local_run.tscn     -- --local-selftest
godot --headless --path . --log-file .logs\shop_smoke.log   res://scenes/shop_screen.tscn   -- --shop-selftest
godot --headless --path . --log-file .logs\cultivate_smoke.log res://scenes/cultivate_screen.tscn -- --cultivate-selftest
godot --headless --path . --log-file .logs\waypoint_smoke.log  res://scenes/waypoint_screen.tscn  -- --waypoint-selftest
godot --headless --path . --log-file .logs\clue_smoke.log      res://scenes/clue_screen.tscn      -- --clue-selftest
godot --headless --path . --log-file .logs\dungeon_smoke.log   res://scenes/dungeon_screen.tscn   -- --dungeon-selftest
```

启动菜单自检要显式给一个可写存档目录（否则 `user://saves` 写不进去会被判失败）：

```bat
godot --headless --path . --log-file .logs\menu_smoke.log res://scenes/main_menu.tscn -- --menu-selftest --save-dir=res://.logs/menu_selftest
```

- **新加的场景自检开关必须同时接进 `tools\run_all_checks.bat`**：代码里支持的 `--*-selftest` 与
  总命令里调用的两集合由 `test_handshake` 双向比对（「写了自检却没人跑」和「登记了却不执行」
  是同一类洞，见 `docs/dev/框架说明.md` 决策 56）。

## 总验收链路（`tools\run_all_checks.bat`）

它是给交接用的**唯一验收命令**：逐步打印 `[OK]/[FAIL]`，末尾汇总，退出码 0 = 全绿。
它由 `tools\check_command.bat`（跑一条命令）与 `tools\check_scene.bat`（跑一个场景自检）拼起来——
拆成子脚本是因为在本脚本里写 `call :label` 会报「找不到批处理标签」（cmd 的怪癖，踩过）。
两个辅助脚本都跑一次就退出，场景自检带 `--quit-after 900` 兜底：万一有人把自检开关写错，
场景会自己退出并因为缺少 `SELF-TEST: OK` 标记而判红，**不会挂死**（也踩过，挂死过一次）。
**`--script` 的三步（自检／地图验收／跨场景闭环）同样带 `--quit-after` 兜底**：运行期错误发生在
`_initialize` 里时 `quit()` 根本跑不到，进程会一直转（真挂过，见框架说明决策 148），
默认上限 `CHECK_SCRIPT_MAX_FRAMES=2000`，可用环境变量覆盖。
每一步跑完还会调 `tools\check_log_errors.bat` 扫引擎日志：里面有 `SCRIPT ERROR` 就判红——
它能抓住「退出码 0、`SELFCHECK: OK` 也打印了，但出错那一行之后的代码根本没跑」这种假绿
（反向验证过：把一条越界塞进 `run_tests.gd` 的 `quit()` 之后，两道旧判定都说绿，只有它报红）。

**场景跑的是生成物，不是 CSV**（2026-10-04 加）：所有场景自检（含真窗口冒烟）读的是
`data/generated/*.tres`，而**只有 `run_tests.bat` 那条链会重建它**（`build_tables.gd`）。
所以「改了 `data/tables/*.csv` → 直接跑场景自检／冒烟」会**拿旧数据判**（红的是幽灵、绿的也是幽灵——
有人为此白追了一轮）。处置：**`run_windowed_smoke.bat` 现在开头自己先重建一次**（多几秒，
不再靠人记）；单独跑 `check_scene.bat`／`check_maps.bat`／`check_loop.bat` 之前，
先跑一次 `run_tests.bat`（或 `build_tables`）把生成物刷到最新。
最后一步是「跑完不许留下引擎进程」：用 `Get-Process` 查（`tasklist` 在本机会话里报 Access denied），
查到就列 PID 判红，**查不动也判红**——「查不出来」不等于「没有」。（同一条纪律见版式预算：量不出来 ≠ 塞得下。）

## 同一时刻只许一条线程跑全量验收

**`tools\run_all_checks.bat` 同一时刻只允许一条线程在跑**（2026-10-04「小程序」实测）。两条会互相干扰：

- **最后一步「跑完不许留下引擎进程」会误报**：它靠 `Get-Process` 找 Godot，**别的线程正在跑场景自检时，
  那一步会红**——而且进程每两三秒换一个 PID，看着就像自己泄漏的（小程序亲眼看到过）；
- **`data/generated` 会被两边同时重建**：其中一条可能读到**不是自己刚建的那份表**，
  于是量出的结论对不上（这类「没重建／重建到一半」的坑，决策 167／168 已经踩过一次）。

**要跑全量**：先看一眼有没有人在跑（问一句最快），或者挑一个**没人跑场景自检的窗口**；
**只想验自己那点改动**：用 `tools\run_tests.bat` 或单个场景自检，**别拉全量**。

## 工具调用纪律（省的不是命令，是重发的上下文）

**每调一次工具，整个上下文都要重发一遍**——所以「调用次数」是**乘数**，不是零头。
2026-10-04 实测「小程序」某一轮 327 次调用的构成：

- **检索（`rg` / `Select-String`）：109 次，45%**；
- **试探式读**（`Get-Content … | Select-Object -Skip N -First M`）：**64 次，26%**；
- 验收（全量 ＋ 单元）：14 次，6%；
- **77% 的命令是单条**（242 次里 186 次没合并任何东西）。
- 同几个文件被**试探式读了 6–9 次**：`test_overworld.gd` 9、`overworld_controller.gd` 8、
  `local_map_controller.gd` 7、`test_handshake.gd` 6。

**四条做法**（按省下的调用数排）：

1. **一次读够**（这条最值钱）：先 `rg -n "^func |^const |^var "` 拿骨架，
   再用 **`rg -n -A 60 "^func _你要的那个"`** 把整个函数连上下文一次拿回来——
   `-A/-B/-C` 才是「一次读到位」的正手，比 `Get-Content -Skip` 一寸一寸挪强得多
   （同一个文件读 6 次＝6 次重发；实测最多的那个被读了 9 次）。
2. **合并命令**：那 77% 的单条命令就是白花的。读三个文件、看 git 状态、量行数，**串成一次调用**
   （`;` 分隔，每段前面加一句小标题，输出照样读得懂）。
3. **合并检索**：`rg -n "甲|乙|丙"` 一次问全，别为一个关键词发一次；要按目录／扩展名分就用 `-g`。
4. **验收分档 ＋ 合并补丁**：改一处 → 只跑最小的那条（单场景自检／`run_tests.bat`）；
   **全量一轮最多一次**，放收尾。同一个文件的多处改动**合并进一个 `apply_patch`**（多个 hunk 一起发）。
5. **同一条线程里读过的文件，别再读一遍**（2026-10-04 实测：`小程序` **一条线程内**
   `test_overworld.gd` 被读了 **9 次**、`overworld_controller.gd` **8 次**、
   `local_map_controller.gd` 与 `test_handshake.gd` 各 **7 次**——**全是它自己反复读**，
   不是不同线程各读一次）。它已经在你的历史里了，**重读＝纯重发**。
   要确认「有没有变」就 `git diff --stat <文件>`，要拿具体几行就 `rg -n`——
   **只有文件确实被改过、或上下文被压缩把它丢了**，才重读。
