# 界面层约定（`src/ui/`）

适用：`src/ui/` 下的控制器与面板脚本，以及 `scenes/*.tscn` 的骨架。
仓库级约定与命令见根 `AGENTS.md`；核心逻辑看 `src/core/AGENTS.md`；世界层看 `src/world/AGENTS.md`。

- **界面骨架进 `.tscn`，代码只做绑定**：`scenes/battle_screen.tscn` 的节点名就是接口，
  `_bind_ui()` 只查找节点并接信号；卡片行、跳字这类「随战斗状态变」的内容仍然运行时生成。
  改版式改场景，别在代码里重建节点树；改节点名要一起改 `tests/test_battle_ui.gd` 与 `_bind_ui()`。
- **界面要过版式预算**：整页最小高度必须 ≤ 设计分辨率（1152×648），`--battle-selftest` 会断言
  （曾经 711 > 648：标题被裁掉半行，而代码一声不吭）。改字号／加行／改卡片结构后必须重跑。
  **所有面板都过这条**：量法在 `src/ui/layout_budget.gd`（`LayoutBudget.fits(self)`），
  各面板的 `--*-selftest` 都会打印一行机器可读的 `LAYOUT min_width=W min_height=H limit=1152x648 ok=true`
  （故意用 ASCII：`findstr` 按控制台代码页匹配，中文抓不到）。
  **文案也过一条同类门限**：`src/ui/copy_guard.gd`（`CopyGuard.id_tokens(self)`）扫整页控件文字里
  「表内 id 形态」（小写字母 + 下划线），每个场景自检打印 `COPY id_tokens=N ok=true`；
  界面里写了 id（`eq_sword_01`／`chapter_01`…）那一页的自检当场红——要用表里的 `name_cn`（决策 244）。
  **两个方向都要过**（宽度 2026-10-03 补：以前只量高度，横向膨胀会被屏幕右侧裁掉而没人发现）。
  **当前余量最紧的是战斗界面**：`LAYOUT[满招式]` 633（技能行按上限 9 个按钮撑满，离 648 只剩 15px，
  宽度 1134 离 1152 只剩 18px），其次才是角色面板 623（25px）——往这两处加行／加宽都要先想清楚。
  **2026-10-03 补**：六指令行与增益减益面板加进战斗界面后，靠把两侧滚动区 215→185、日志 80→64 换回了空间，
  现在 `LAYOUT[满招式]` **630**（宽度 1134）、`CONTENT` 419——**余量只剩 18px，再往这一页加行必须先挪出等量空间**。
  （这条 2026-10-03 更新过：以前写的是"最紧的是角色面板"，而战斗界面在满编＋满招式下更紧，见决策 162／166。）
  **但这条只量到面板外壳**（同日实测）：`get_combined_minimum_size()` 到 `ScrollContainer` 就断了，
  角色面板量到的 760 其实是 `_tabs.custom_minimum_size` 这个常量——滚动区的内容宽了照样报 ok，
  而横滚是关着的（内容被裁掉、代码一声不吭）。内容宽度要用 `LayoutBudget.content_fits()`，
  并且**必须在「切到那一页 + 等一帧」之后量**（TabContainer 只让当前页可见、容器会跳过不可见子树，
  不等帧会量到 0）。角色面板已这样量并打印 `CONTENT min_width=W limit=1152 ok=true`；
  **七个面板（角色／商店／打坐／驿站／线索本／完成度／战斗）都已这样量**，各自的 `--*-selftest`
  会打印一行 `CONTENT min_width=W limit=1152 ok=true`（`tools/check_scene.bat` 会把它捞出来）。
  小值（线索本 1）不是「没量到」：`autowrap` 的 Label 最小宽度只有 1 个字形（长文本换行、不会被裁），
  这条量的是**不换行的内容**——按钮、固定宽卡片、显式设过 `custom_minimum_size` 的行。
- **盖在游戏画面上的面板必须有背板**：商店／打坐／驿站／线索本／完成度／角色都是按 E／Tab／M／K
  直接盖在当前场景上打开的，`Panel` 只是个空 `Control`——不铺 `Backdrop`（`ColorRect`，alpha ≥ 0.9）
  就会让地图、NPC 和地图自己的 HUD 透上来把字糊掉（单独打开看不出来，因为背后是空窗口）。
  检查在 `LayoutBudget.has_opaque_backdrop()`，六个面板的自检都会打印
  `BACKDROP alpha=0.97 min=0.90 ok=true`。
- **玩家可见的文案不许说假话**：功能做完了就把「未接入／未实现」这类话从界面和注释里删干净——
  曾经菜单副标题写着「玩法未接入」而玩法早已接入。新增／改动任何玩家看得到的字符串（按钮、提示条、
  不可用原因、战报）后，跑一次 `tests/test_copy_audit.gd`（它扫 `src/` 里的四句禁用文案，并断言
  真实场景里的关键文案）。给玩家看的界面文案**不许出现表里的英文 id**（`dot_poison`、`eq_sword_01`…），
  要用表里的 `name_cn`；**数据错时也一样**——id 只进 `push_error` 日志，界面上说人话。
  `test_copy_audit._check_no_id_tokens()` 拿启动菜单与枢纽页的**真实标签**扫「小写字母 + 下划线」的 id 形态，
  命中就红（决策 242：枢纽页那行「章节：第 1 章（chapter_01）」就是这么被逮住的）。
- **加了按键就要在 HUD 里写出来**：控制器里 `is_action_pressed("xxx")` 用到的动作，
  其按键标签（`InputSetup.ACTIONS` 的键位，`OS.get_keycode_string()` 反推）必须出现在该文件的
  HUD 提示行里，否则 `test_copy_audit.gd::_check_hud_mentions_every_key` 会红——
  线索本按 K 是**藏了两个版本**的教训（设计 03 要求「线索必须能被找到」）。

注意要显式指定 `character_screen.tscn`：主场景是启动菜单，自检开关在角色界面里。
