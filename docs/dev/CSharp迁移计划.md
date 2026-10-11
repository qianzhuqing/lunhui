# C# 迁移计划（小程序 · 2026-10-10 立项）

> **用户口径（2026-10-10）**：**除 `addons/` 外的全部 GDScript 都转成 C#**；
> **不做 GDScript／C# 双跑共存**；**后续开发一律用 C#**。
> 本文件是这件事的**唯一施工单**：阶段、顺序、每个模块的搬迁协议、以及"改成 C# 之后
> 哪些仓库级约定失效、换成什么"。接手的人**先读这一页**，不要从代码里猜进度。

## 〇、现状（2026-10-10 实测）

| 项 | 数 | 说明 |
|---|---|---|
| `src/` | 142 文件 / 26,621 行 | 纯逻辑＋世界层＋界面，最大 `src/world/local_map_controller.gd` 2,467 行 |
| `tests/` | 63 文件 / 20,636 行 | 9,898 条断言——**这是唯一的安全网** |
| `tools/` | 14 文件 / 4,839 行 | 配表构建、验收链、变异探针 |
| 场景 | 21 个 `.tscn`，其中 **14 个**绑 `.gd` | 绑定的 `script = ExtResource(...)` 要换成 `.cs` |
| autoload | 2 个（`GameData`／`GameSession`） | `project.godot` 的 `[autoload]` 段 |
| 现有 `.cs` | **0** | 从零起步 |

> **进度（2026-10-11 收尾后）**：除 `addons/` 外**全部源码已是 C#**——`src/`／`tests/`／`tools/`／`scenes/`
> 下 `.gd` 为 **0**（只剩 `.logs/vendor/` 的第三方快照）。P0–P4 已完成、P5 收尾进行中；
> 逐批记录见下面「三点零X」起的各节。

工具链（**已就绪**）：

- **.NET SDK 8.0.425**（`C:\Program Files\dotnet`，`dotnet --list-sdks` 已认）——2026-10-10 装。
- **Godot 4.7.1 .NET（mono）编辑器**：
  `F:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.txt`
  （原来是**标准版**，跑不了 C#；验收链靠 `GODOT_BIN` 指过来，别改机器的默认路径）。

## 一、搬迁协议（每个模块照这个走，不许跳步）

**一个"模块"= 一个 `src/` 文件 ＋ 它的用例文件 ＋ 调它的地方。**

1. 读旧 GDScript ＋ 它的用例，列出**行为清单**（不列就搬，等于凭印象重写）。
2. 落 C# 版（同名 PascalCase 类，放进 `src/` 下**同名目录**，与旧文件一一对应便于对账）。
3. **用例一起搬成 C#**（同一轮里），注册进 C# 版 `run_tests`。
4. 改调用点：场景 `ExtResource`、autoload、其它模块的引用。
5. 删掉那一对 `.gd`（连同 `.gd.uid`）。
6. 跑 **C# 侧验收** ＋ `tools\validate_tables.ps1`；两边都绿才算搬完。
7. 回填 `docs/dev/模块对接表.csv`（`code_paths` 换成 `.cs`）与 `docs/dev/验证清单.csv`。

**红线**：不许"先把 `src/` 翻完再翻 `tests/`"——那把安全网先撤了，后半个迁移全是盲改。
**配对原则**：`src/x/y.gd` ↔ `src/x/Y.cs` ↔ `tests/test_y.gd` ↔ `tests/testY.cs`，一个对不上就是漏了。

## 二、阶段（顺序不可换）

| 阶段 | 内容 | 为什么排这儿 |
|---|---|---|
| **P0 工具链** | .NET 8 SDK ＋ Godot .NET(mono) ＋ `lunhui.csproj` ＋ `project.godot` 的 `[dotnet]` | ✅ 本轮完成；没它一行 C# 都跑不起来 |
| **P1 验收骨架 C# 化** | ✅ 2026-10-10 完成（断言库 ＋ 运行器 ＋ 一条自检用例，见下节）；**场景自检钩子／`check_scene`／`check_loop` 的 C# 入口留到 P3／P4** | **先有网再搬家**：C# 版断言库先立起来，后面每搬一个模块就有一对用例能跑 |
| **P2 `src/core`** | 叶子→根：`src/data/tables/*_row`（60+ 个行类）→ `table_db`／`table_validator` → 计算器与成长 → 物品／武学／NPC 服务 → `battle_actor`／`battle_simulator`／`damage_resolver` | 纯逻辑、headless 可验，风险最低、用例最密 |
| **P3 `src/world` ＋ `src/ui`** | 场景控制器、面板、`IconPaths`、`UiKit`；顺带改 14 个 `.tscn` 的脚本绑定 | 要真窗口冒烟，放 P2 之后 |
| **P4 `tools/` ＋ 脚本链** | `build_tables.gd`／`analysis_mutations*.ps1`／`*.bat`／变异探针的备份路径 | 它按 `.gd` 路径备份与改常量，代码一换它就全失效 |
| **P5 收尾** | 删残留 `.gd`／`.gd.uid`；重跑 `run_all_checks` ＋ 窗口冒烟 ＋ 变异探针；改仓库级口径 | 见下节 |

## 二点五、P1 落地的产物（2026-10-10，已跑通）

| 文件 | 对位 | 说明 |
|---|---|---|
| `tests/TestCase.cs` | `tests/test_case.gd` | 断言库（10 种断言 ＋ `Checks`／`Failures`／`SuiteName`／`Run`）。**夹具（`solo_state`／`table_with_n_chars`…）还没搬**——它们依赖 `TableDb`，P2 搬 `src/core` 时一起挪 |
| `tests/HarnessSelfTest.cs` | 新增 | 骨架自检：13 处断言点、全部无条件执行（静态＝实际），并拿探针验"失败真的被记、且计入断言数" |
| `tests/FailingProbe.cs` | 新增 | **故意失败**的探针（4 条）。**不许进运行器的 `Suites` 清单**；文件名故意不叫 `Test*.cs` |
| `tools/RunTestsCs.cs` ＋ `tools/run_tests_cs.tscn` | `tools/run_tests.gd` | 运行器：`Suites` 清单 → 逐条跑 → 假绿门限（静态断言点 vs 实际）＋ `MinAssertions`／`SlackLimit` → `SELFCHECK: OK (N assertions)` |
| `tools/run_tests_cs.bat` | `tools/run_tests.bat` | `dotnet build` → Godot .NET headless 跑 `res://tools/run_tests_cs.tscn` → `findstr "SELFCHECK: OK"` → `check_log_errors.bat` |

2026-10-10 实测：`tools\run_tests_cs.bat` → **`SELFCHECK: OK (13 assertions)`**、退出码 0；
同时旧的 `tools\run_tests.bat` → `SELFCHECK: OK (9915 assertions)`、`validate_tables.ps1` → `PASSED`。
**两条链并存**：搬家期间谁红谁就是回归。

几条踩过的坑，接手别再踩：

1. **`--script` 只认 GDScript**：C# 侧必须是"场景 ＋ 节点"，跑完 `GetTree().Quit(code)`，
   `.bat` 里也**不能再带 `--script`**，改成把 `.tscn` 当位置参数传（`… res://tools/run_tests_cs.tscn`）。
2. **C# 要 Godot 的 .NET 版本**（本机是 `F:\Godot_v4.7.1-stable_mono_win64\…_console.txt`）：
   标准版加载 `CSharpScript` 会直接失败。`run_tests_cs.bat` 用 `GODOT_CSHARP_BIN`，
   未设时回落 `GODOT_BIN`，最后才是本机默认路径。
3. **`.bat` 的 if 块里不许出现成对的括号**：`echo … (mono) not found` 里的 `)` 会**提前闭合 if 块**，
   cmd 接着去执行 `not found:`，报的是句莫名其妙的 `not was unexpected at this Time.`。
4. **`.bat` 必须 CRLF**（`_audit_bat` 按字节验），`apply_patch` 写出来是 LF，写完要转一次。
5. `MinAssertions` 先写 0 跑通、再**贴到"总断言数 − 10"**（现在是 13−10＝3）——别把基线调松。

## 二点七、P2 行类搬迁（2026-10-10 起步，试点已通）

**试点结论（`combat_const` 一张表走完一遍）**：

1. **行类可以直接搬 C#，构建链不用先搬**。`tools\build_tables.gd` 读的是 `get_property_list()`，
   C# 的 `[Export]` 成员照样在里面；`ResourceSaver` 也能把 C# 行类写进 `data/generated/*.tres`
   （实测 `combat_const.tres` 里 `script = ExtResource(... CombatConstRow.cs)`，各格值齐全）。
2. **成员名保持 snake_case（与 CSV 列名逐字相同）**——这是过渡期的关键决定：
   `table_registry.gd` 的 `property_of`／`column_of` 按"属性名 ＝ 列名"折算，
   而还没搬走的 GDScript（`src/core`、`src/world`、`src/ui`、全部用例）到处是 `row.char_id`。
   名字一动就得全仓改调用点，留下大片半新半旧的中间态。等 P5 GDScript 清零后再单独议。
   **辅助方法同理**（`row.parse_skills()` 这种），方法名也留 snake_case。
3. **`.cs` 行类只有构建脚本"读属性表"这一条路，别拿标志位写门限**：
   同一次 `get_property_list()`，GDScript 侧给 C# 行类成员打的是 `PROPERTY_USAGE_SCRIPT_VARIABLE`(2048)，
   C# 侧 `GodotObject.GetPropertyList()` 拿到的是 **4102**（不含 2048）——2026-10-10 实测，
   按 2048 过滤在 C# 里一条都读不到。所以 `tests/TestRows.cs` 的判据**一律看生成物**
   （`.tres` 里有没有这一格、引用的是不是这份 .cs），不碰标志位。
4. **整条验收链必须切到 .NET(mono) 版 Godot**：标准版加载不了 `CSharpScript`，
   而 `.tres` 一旦引用 `.cs`，`--script` 模式（`build_tables.gd`）也会去加载它。
   2026-10-10 已把 `tools/*.bat` 的默认 `GODOT_BIN` 全部换成 mono 版，
   并新增 `tools\ensure_csharp_build.bat`（每个起引擎的 `.bat` 都先 `call` 它，
   一个 cmd 会话只编一次）。**旧链在新二进制下实测全绿**。

**每一批的验收形状**：`tools\run_tests_cs.bat`（C# 侧）＋ `tools\run_tests.bat`（旧链）＋
`tools\validate_tables.ps1` 三样都绿，才算这一批搬完。

**批次划分（按"有没有辅助方法"切开，先易后难）**：

- **A 批：纯数据行**（39 个文件，只有 `extends` ＋ `@export`）→ ✅ 2026-10-10 完成：脚本机械生成
  `PascalCase` 类名、`String→string`／`int→int`／**`float→double`**（GDScript 的 float 是 64 位，
  用 C# 的 `float` 会把表里的常数截断，而用例里到处 `check_eq(值, 字面量)`）、注释原样搬成 `//`；
  `ROW_SCRIPTS` 40 张表（含试点那张）已指向 `.cs`，对应 `.gd`／`.gd.uid` 已删。
  连带改到的三处：`tools/validate_tables.ps1`（读行类源码的门限要同时认 `.gd` 与 `.cs` 两种写法）、
  `docs/dev/模块对接表.csv` 的 `code_paths`、以及两个直接 `load("…_row.gd")` 的调用点
  （`src/core/npc_service.gd`、`tests/test_buff.gd`）。
- **B 批：带辅助方法的行**（24 个文件，1–4 个 `func`：解析技能／词条／触发器／队伍…）→
  逐个人工搬，方法名照样 snake_case，调用点不动。**已搬 8/24**（2026-10-10：
  `damage_type`／`building_def`／`buff_stat`／`attr_to_stat`／`weapon_type_def`／`stat_def`／
  `skill_passive_stat`／`enemy_team`——即 `is_dot`／`has_service`／`parsed_target`／`has_cap`／
  `focus_attrs`／`has_max`／`parsed_members` 这一组）。剩下的按"牵动面"从大到小排：
  `character_base`（技能列表）→ `equip_base`／`affix_pool`（词条）→ `enemy_base`／`skill_base`／`skill_active`
  → `story_node`／`event_check`／`dialogue_option`／`flavor_point`／`hidden_trigger`／`dungeon_room`
  → `item_base`／`shop_stock`／`drop_table`／`equip_slot_def`。

**返回值怎么翻译（B 批的通用口径，踩过才知道）**：

| GDScript | C# | 说明 |
|---|---|---|
| `-> bool` | `public bool 名字()` | 直译 |
| `-> PackedStringArray` | `public string[] 名字()` | Godot 把 `string[]` 桥成 `PackedStringArray`，`str()` 出来的样子都一样（`test_table_pipeline` 就是拿 `str()` 比的） |
| `-> Array`（元素是字典） | `public Godot.Collections.Array 名字()` | GDScript 侧 `for member: Dictionary in …` 照常 |
| `-> Dictionary` | `public Godot.Collections.Dictionary 名字()` | 值用 `Variant` 装箱；`new Dictionary { { "kind", … } }` 这种集合初始化器可用 |
| `int(x.to_float())` | `(int)x.ToFloat()` | **都是截断**，别顺手写成 `Math.Round` |

**又一处"门限悄悄失效"（比报错更危险）**：`tests/test_handshake.gd` 的 `TEXT_EXTENSIONS`
是"哪些扩展名算文本"的**唯一开关**，原来没有 `cs`——于是搬进 `src/` 的 C# 文件对
控制字节、孤儿引用、契约扫描那一整族门限**等于不存在**（症状是白名单突然报"找不到定义"）。
2026-10-10 已把 `cs` 加进去。**以后每引入一种新扩展名（.csproj／.sln 等），先看这张表**。

**B 批第二批（4 个，2026-10-10）**：`affix_pool`／`hidden_trigger`／`event_check`（纯解析类辅助方法）
＋ `dialogue_option`（带静态方法）。**行类合计 57/64**，剩 7 个：
`character_base`／`equip_base`／`enemy_base`／`skill_base`／`skill_active`／`story_node`／`flavor_point`。

**静态方法可以跨语言调（实测结论，省得再猜）**：GDScript 侧 `preload("…/XxxRow.cs").静态方法(...)`
**是通的**——用 `dialogue_option.parse_set_flags` 实测：改掉 4 个调用点的 preload 路径（换成 `.cs`）后
整条链绿、`test_dialogue` 那一族 **149 条断言全部执行**（静态断言点＝实际执行数，不是"报错掐断了函数"）。
所以 C# 行类里的 `public static` 不必拆成实例方法，`flavor_point.marker_name_of` 也能照这个形状搬。

**写 `.ps1` 要连 BOM 一起写**：`[IO.File]::WriteAllText(path, text, UTF8Encoding(false))` 会把
`tools/validate_tables.ps1` 的 BOM 抹掉——门限 `_check_repo_conventions` 当场报
"含非 ASCII（中文）却没有 UTF-8 BOM：PowerShell 5.1 会按 ANSI 读坏它"。
批量改这份文件时**用 `UTF8Encoding($true)`**（改别的 `.gd`／`.cs`／`.csv` 则要先看原文件头三个字节，
照原样写回去：`.csv` 大多带 BOM、`.gd`／`.cs` 不带）。

**A 批踩到的两个新坑（都写进门限了）**：

5. **别拿 `.tres` 里"有没有这一格"当判据**：`ResourceSaver` 会**省掉等于默认值的属性**——
   `map_local.save_allowed`、`buff_grant.note` 这些列在生成物里**本来就没有那一行**，
   首版门限因此误报了 9 条。现在改成读行类源码的成员声明（＋`[Export]` 个数），
   而"构建脚本真能按列名写进去"由 `build_tables.gd` 自己把关（列对不上它直接判失败、整条链红在第一步）。
6. **旧链的断言基线会随文件数漂**：`"每个 src 脚本都被引用"`与`"每个 .uid 都有源文件"`是**逐文件**断言的，
   换掉 40 个行类文件就少了 78 条。这是迁移带来的**有意识**增减，按规矩把 `run_tests.gd` 的
   `MIN_ASSERTIONS` 贴到 `实测 − 10`（9932 → 9854 条 → 基线 9844），**不是把下限调松**。
   每搬一批都要重贴一次。
- 收尾：`src/data/table_row.gd`＋`table_registry.gd` 的 `ROW_SCRIPTS` 全量换 `.cs` 之后，
  删掉 64 个 `.gd`／`.gd.uid`。

## 二点八、P2 进入 core（curve_evaluator ＋ attribute_calculator，2026-10-10）

行类 **64/64 全部搬完**（最后一张 `story_node` 卡在「要引 `AttributeCalculator` 的 kind 常量」，
所以它排在 `attribute_calculator` 之后、同一批补上）之后，第一块 core 是
**`src/core/curve_evaluator.gd` ＋ `src/core/attribute_calculator.gd`**（两者耦合：计算器调曲线求值），
用例 `tests/test_attribute.gd` → `tests/TestAttribute.cs` 同轮搬完。
**踩过的四类"跨语言形状"（全是探针实测，别再猜）**：

1. **带参构造在 GDScript 侧过不了静态检查**：
   `const X := preload("…/X.cs")` 再 `X.new(db)` → **解析期**报
   `Parse Error: too many arguments for "new()" call. Expected at most 0 but received 1.`
   （GDScript 解析器看不见 C# 的构造签名，一律按 0 参算。）**用 `load()` 时值是动态的，
   所以同一个调用"看着能用"——探针必须写成 `const preload` 才能看见。**
   **解法**：C# 类加一个静态工厂 `public static X create(GodotObject db) => new(db);`，
   GDScript 调用点一律 `X.create(db)`；C# 侧照旧 `new X(db)`。
2. **GDScript 读不到 C# 的 `const`**：`script.CONTRIB_ATTR_POINT` →
   `Invalid access to property or key 'CONTRIB_ATTR_POINT' on a base object of type 'CSharpScript'`。
   **解法**：过渡期在 C# 侧加静态取值口（`contrib_attr_point()`／`contrib_stat_flat()`），
   等调用方自己搬成 C# 后连它们一起删。**注意静态函数读不到实例变量**——
   `affix_roller.gd` 那两个类级 `var` 一度让它的 `static func contributions_of()` 解析失败，
   所以取值口本身也得是 `static func`。
3. **C# 的默认参数值对 GDScript 不生效**：`compute(level, attrs)` 少传实参 →
   `Invalid call. Nonexistent function 'compute'`。**解法**：GDScript 调用点把参数写全
   （`compute(..., true)`／`attr_totals_of(attrs, {}, [])`）。C#→C# 的调用照旧吃默认值。
4. **静态方法可以跨语言调**（`contrib_attr_point()`、`CurveEvaluator.evaluate`）——
   这条 0.7.0 的 `dialogue_option.parse_set_flags` 就已经证过，只是这轮又验了一次。

**顺带修掉一个"跨用例污染"（与迁移无关，但挡着验收）**：`TableDb.inject_row` 原先直接改
`load()` 出来的**共享资源缓存对象**。创建角色那条路（`CreationService.build`，玩家名字 ≠ 模板名时）
会注入一行自建模板，于是同一进程里后跑的用例读到的 `character_base` 已经是玩家的真名
（「家道失落的书生」→「陆文昭」）、行数还多一行。症状像"属性算错"，实际与属性管线无关——
2026-10-10 用「只跑 `test_menu` ＋ `test_character_sheet`」的探针钉住的。
现在注入前先 `duplicate(true)` 一份、只替换本实例的 `tables[表名]`（游戏里只有一个 `TableDb`，
行为不变；用例之间恢复隔离）。

**门限补偿（搬 core 时会反复用）**：

- `tests/test_handshake.gd::SINGLE_SOURCE_RULES` 的 owner 换成 `.cs`，
  且 `test_handshake_code.gd::_check_single_source_of_truth` 改成**两种语言都扫**——
  只扫 `.gd` 的话，owner 一搬走这条规则会整个失效（先是"至少出现在一处"报红，
  再是真正的漂移没人看）。
- `test_handshake_contract.gd::_check_verify_tests_are_executed` 原先只读
  `tools/run_tests.gd`，**C# 用例（走 `tools/RunTestsCs.cs` 的 `Suites`）会被判成
  "登记了却从不运行"**——现在两份清单都读，正则同时认 `.gd`／`.cs`。

**本轮验收**：`run_tests_cs.bat` **996 条**（基线 986）、`run_tests.bat` **9843 条**（基线 9832）、
`validate_tables.ps1` **PASSED**。

**同轮收尾**：`src/data/tables/story_node_row.gd` → `src/data/tables/StoryNodeRow.cs`
（`bonus_contributions()` 直接引 `AttributeCalculator.CONTRIB_ATTR_POINT`／`CONTRIB_STAT_FLAT`，
不再需要过渡取值口），`ROW_SCRIPTS` 指过去、`.gd`／`.gd.uid` 删掉、
`tests/TestRows.cs::PortedFloor` 抬到 **64**——**`src/data/tables/` 下已无 `.gd`**。
验收：`run_tests_cs.bat` **1014 条**、`run_tests.bat` **9842 条**、`validate_tables.ps1` **PASSED**。

## 二点九、P2 第二批：`table_registry` ＋ `table_resource` ＋ `table_db`（2026-10-10）

三张一起搬（**不能拆**：`TableDb` 要读注册表的常量，而 GDScript **读不到 C# 的 `const`／静态字段**，
先搬一半会卡在"没人读得到那张表"）。搬完 `src/core/` 下再无 `table_db.gd`；
`src/data/` 下只剩 `table_row.gd` 一个 GDScript 基类（等行类调用面清零再处理）
——**2026-10-11 已清零**：那个 15 行的 `class_name TableRow` 只是旧壳，
`TableRow.cs` 早就存在，删壳即可，见 §二点四五。

- `src/data/table_registry.gd` → `src/data/TableRegistry.cs`（`public partial class : RefCounted`；
  数据放**纯托管** `static readonly` 字段，另配 `tables()／row_scripts()／primary_keys()／tables_dir()／
  output_dir()` 五个静态取值口给过渡期的 GDScript 调——**静态字段里绝不放 Godot 集合**，
  见下面第 8 条）。
- `src/data/table_resource.gd` → `src/data/TableResource.cs`（`[Export]` 成员名保持 snake_case：
  `.tres` 里存的键逐字就是 `table_name`／`primary_keys`／`rows`／`index`）。
- `src/core/table_db.gd` → `src/core/TableDb.cs`。
- 调用面：`TableDbScript := preload("…/TableDb.cs")` 二十来处（`.new()` **不用**改——零参构造不受
  "GDScript 看不见 C# 构造签名"那条影响）；`Registry.X` → `Registry.x()`（`build_tables.gd`／
  `TableValidator.cs`／`test_table_pipeline.gd`／`test_handshake_docs.gd`／
  `test_handshake_doccounts.gd`／`analysis_mutations.gd`）。
- `data/generated/*.tres` 的 `[ext_resource]` 从 `table_resource.gd` 换成 `TableResource.cs`：
  **不要手工改**，跑一次 `build_tables.gd` 让它重生成（生成物不入库）。

**这一批新踩的三条（都写进代码注释了，接手别再试）**：

5. **C# 的 `Variant.Equals` 不是引擎里的 `Variant == Variant`**。`row.Get(col).Equals(value)`
   对两个内容相同的 String 返回 **false**（按内部结构比），于是
   `db.rows_where("enemy_passive", "enemy_id", "en_bd_boss")` 静默返回**空集**——
   症状是「敌人内功超编」那道校验整段空跑、两条白名单反过来报"已经不再超编"。
   正确写法：**借字典的键比较**（`new Dictionary { { value, true } }.ContainsKey(cell)`），
   那才是 `==` 的语义。**凡"按列值筛行／查存在"的 C# 代码一律用这个写法。**
6. **C# 默认参数值对 GDScript 不生效**（重申二点八第 3 条）：`load_all(dir = "…")` 被 GDScript 空参调用会
   报 `Invalid call. Nonexistent function`，所以拆成无参 `load_all()` ＋ 显式 `load_all_from(dir)`
   （不敢重载：Godot 的 C# 脚本注册按方法名索引）。
7. **`_check_no_new_test_only_api` 的 C# 正则只认 `public <类型> 名字(`**，
   `public static <类型> 名字(` 因为中间多一个 `static` 词**整条漏过去**——白名单里 `"column_of"`
   那条（唯一定义在注册表里，只被 `tools/build_tables.gd` 调）于是从"找不到定义"变成假红，已删掉。
   **静态 C# 方法目前是这道门限的盲区**：P5 要么补正则，要么另立一条"不许有游戏路径不调用的静态接口"。
8. **静态字段里不许放 Godot 对象**（`Godot.Collections.Dictionary`／`Array`…）。第一版把注册表的
   `ROW_SCRIPTS`／`PRIMARY_KEYS`／`COLUMN_ALIASES` 写成 `static readonly Godot.Collections.Dictionary`，
   结果：**进程退出时**才轮到 GC 回收它们，而那时引擎已经拆完，`GodotObject.Finalize()` 调 native 抛
   `Fatal error. System.Runtime.InteropServices.SEHException`——场景自检打印 `SHOP SELF-TEST: OK`、
   退出码却非零，`run_all_checks` 报 `[FAIL] scene:shop`（`shop_stdout.log` 末尾那一坨才是真凶）。
   改法：静态只存 `string[]`／`Dictionary<string,…>` 这类**纯托管**数据，Godot 集合**用完即建、
   当场交给调用方**。
9. **C# 侧 `new` 出来的 `RefCounted`／`Resource` 要在退出前手动回收一次**，否则进程会以
   `SEHException` 崩在终结器里——`tools\check_loop.bat` 就是这条的现场：32 条
   `Leaked unsafe reference to object` + 一条 `Fatal error`，**退出码非零**，而 `LOOPCHECK: OK`
   已经打出来了（`run_all_checks` 因此报 `[FAIL] cross-scene loop`，单跑、场景自检全对，
   只有"攒够多 C# 对象"的那条长链会撞上）。修法是新增 `src/core/GcShutdown.cs`
   （`Collect + WaitForPendingFinalizers + Collect`），由 autoload `GameData._exit_tree()` 调用——
   它在 native 侧还活着时跑，游戏正常退出／`--script` 工具链／场景自检三条路都覆盖（实测 32 → 0、退出码 0）。
   **根因（P5 可以顺手消掉）**：每次调用都 `new` 一个 C# `RefCounted`（`AttributeCalculator.create(db)` 最典型），
   把它做成静态方法（`compute(db, …)`）之后这类托管对象就不再堆积；那时 `GcShutdown` 只当兜底。

**同轮修掉一个 P2 早就踩坏、但没人跑到的工具**：`tools/audit_table_usage.ps1` 的语料
`Get-ChildItem src -Recurse -Include *.gd` **只扫 .gd**——行类搬成 .cs 之后它们整段隐形，
`run_all_checks` 的 `table usage audit` 报出 13 条"没人读的列"（`curve_def:curve_id`／
`buff_grant:grant_id`／`equip_base:bonus_*` 这些只出现在 .cs 里的）。改成 `*.gd,*.cs`，
并跟 `@export var` 一样**剔除 C# 的成员定义行**（`public string char_id { get; set; }`）——
不剔的话每个列名都会出现在自己的行类里，"有没有人读"永远为真（这工具第一版就栽在这）。

**同轮顺带**：`TestRows.cs` 读 `ROW_SCRIPTS` 的正则原先只认 GDScript 的 `"表": "路径"`，
搬成 C# 的 `{ "表", "路径" }` 之后 `ported` 直接变成 0 张（假红"少于下限 64"）——改成两种写法都认。
`tests/TestCase.cs::GetDb()` 的过渡写法（从 `GameData` autoload 上取 `GodotObject`）收紧成 `TableDb`
（autoload 不在时自己 `load_all()`）；`AttributeCalculator` 的表库字段从 `GodotObject + Call(...)`
跟着收紧成 `TableDb`。

**本轮验收**：`run_tests_cs.bat` **1014 条**（基线 986）、`run_tests.bat` **9840 条**（基线 9832）、
`validate_tables.ps1` **PASSED**（警告 20 条）。

## 二点一零、P2 第三批：`table_validator`（2026-10-10）

**最大的一块**：原 `src/core/table_validator.gd` 2049 行、67 个静态函数，一次搬完、**按表族拆成四份**：

| 文件 | 装什么 |
|---|---|
| `src/core/TableValidator.cs` | 词表（`ENUMS`／`REFERENCES`／`RANGES`／`RELATIONS`…）＋入口 `validate()` ＋过渡取值口 ＋共享工具 |
| `src/core/TableValidatorStructure.cs` | 主键／枚举／引用／区间／关系／「代码里硬编码的 id 名单 vs 表」 |
| `src/core/TableValidatorActors.cs` | 角色／武学／装备／敌人／掉落／事件／商店 |
| `src/core/TableValidatorWorld.cs` | 剧情／对话／NPC／引导／创建角色／增益与套装／难度／地图 |

后三份是**纯 C# 静态类**（不派生 GodotObject）——Godot 只把第一份当脚本资源，
GDScript 侧照旧 `preload("res://src/core/TableValidator.cs").validate(db)`。
`validate(TableDb db)` 的返回从 `PackedStringArray` 变成 `string[]`（桥过去就是 PackedStringArray），
六个调用面（`bootstrap.gd`／`game_data.gd`／`build_tables.gd`／`analysis_mutations.gd`／
七个用例）一个字没改。

**这一批新踩的三条**：

10. **过渡取值口对 GDScript 是必须的，而且恰好落在第 7 条那个盲区里**。GDScript
    **读不到 C# 的 `const`／静态字段**，而 `test_table_pipeline.gd`／`test_handshake_docs.gd`／
    `test_handshake_repo.gd` 要读那四张规则表 → 包成 `enums()`／`references()`／`ranges()`／
    `relations()` 四个静态方法（用完即建 Godot 集合，不在静态字段里存）。
    试过把它们写进 `TEST_ONLY_API_ALLOWED`，**当场被反向检查打回**：
    `_check_no_new_test_only_api` 的 C# 正则认不出 `public static <类型> 名字(`，
    于是"白名单里找不到定义"报红。**结论：静态方法那道盲区（第 7 条）真的会咬人，
    P5 必须补**——要么正则加 `static`，要么单立一条静态接口的门限。
    （补正则的代价已经量过：`src/**/*.cs` 里会因此**多出 13 个**"只在定义处出现"的名字，
    其中 6 个还没有白名单条目：`column_of`／`output_dir`／`row_scripts`／`tables_dir`／
    `is_battle_use`／`is_field_use`。）
11. **归档文档不能进「路径必须存在」那道门限**。`test_handshake_doccounts.gd::
    _check_design_docs_paths_exist` 扫的是 `docs/design/` 全目录，**包含 `archive/CHANGELOG.md`**——
    那里面写着 `src/core/table_validator.gd`（当年确实存在）。删掉这个 `.gd` 之后门限当场红，
    而"改历史"是不许的。已把 `/archive/` 整体排除（与 dev 侧排除 `框架说明.md` 同一条理由：
    那边是历史叙事，必然引用过时的路径）。
12. **800 行门限只扫 `.gd`**（`_check_file_size` 里那句 `if not path.ends_with(".gd"): continue`）——
    所以 `.cs` 拆不拆都不影响验收，但**这正是 P5 要补的口子**：迁移期间 `.cs` 会一路长大。
    本批按原文件的"按表族拆"建议主动拆成四份（最大一份不到 1300 行），
    并顺手把 `docs/dev/代码拆分清单.md` 里 `table_validator.gd` 那一行划掉（14 → 13 个超标）。

**跨语言只读通道**（这一批全部集中到 `TableValidator.ScriptConstants()`／`ScriptFor()` 两个口上）：
`WorldMapService.LOCKED_SCENES`／`DIFFICULTY_RULES`、`GrowthCalculator.DEFAULTS`、
`BattleActor.KNOWN_STATUS_RULES`／`DECLARED_STATUS_RULES`、`EnemyFactory.AI_TEMPLATES`、
`NpcService.PLAYER_ID`（常量表），`WorldMapService.held_item_id()`／
`BattleSimulator.is_supported_damage_type()`（静态方法），
`GrowthCalculator.new(db)`（**造 GDScript 实例要传构造参数**，`script.New(db)`）。
这些 `.gd` 一旦搬成 C#，改的就是**这一处**——注释里逐条写了。

**同步改到的地方**：`SINGLE_SOURCE_RULES` 的 `also` 从一份变**两份**
（`"attr_point"` 现在在 `TableValidator.cs` 的 ENUMS 与 `TableValidatorActors.cs` 的
`value_kind != "attr_point"` 里各出现一次）；`模块对接表.csv` 的 `table_pipeline.code_paths`；
`框架说明.md` 的模块职责表与「加一张新表」两处；`src/core/AGENTS.md`／
`enemy_factory.gd`／`damage_resolver.gd`／`talent_service.gd` 等处的散文指针改成新函数名。

**本轮验收**：`run_tests.bat` **9836 条**（基线 9832）`SELFCHECK: OK`、
`run_tests_cs.bat` **1014 条** `OK`、`validate_tables.ps1` **PASSED**（警告 20 条）、
`run_mutation_sweep.bat`（构建期＋验收两半）**64 个 cast／漏 0／cast 失效 0**——
最后这条是这次最有价值的一份证据：**故意改坏 64 处数据，规则一条都没漏抓**。

**下一步**：`src/core/` 剩余叶子——`character_sheet`／`skill_loadout`／
`inventory`／`affix_roller`／`talent_service`／`level_service`／`mastery_service`… 再到
`battle_actor`／`battle_simulator`／`damage_resolver`（搬它们时，那几处
`ScriptConstants()`／`ScriptFor()` 调用点要跟着改成静态取值口；`growth_calculator` 那两处
已在「二点一一」换完，照它抄）。

## 二点一一、core 第四批：`growth_calculator`（2026-10-10）

`src/core/growth_calculator.gd`（192 行、14 个函数）→ `src/core/GrowthCalculator.cs`
（一份，没拆——离 800 行还远）。旧 `.gd`／`.gd.uid` 已删。
**这是第一个「构建期校验器直接引用」的 core 叶子**，所以跨语言口也跟着动：

| 原来的口 | 现在 |
|---|---|
| `TableValidator.ScriptConstants("res://src/core/growth_calculator.gd")["DEFAULTS"]` | `GrowthCalculator.Defaults()`（C# 直调） |
| `TableValidator.ScriptFor(…).New(db)` → `growth.Call("passive_fit", …)` | `GrowthCalculator.create(db)` → `growth.passive_fit(…)` |
| GDScript `GrowthCalculatorScript.new(db)`（11 处） | `preload("res://src/core/GrowthCalculator.cs").create(db)` |

**这一批把「core 叶子怎么搬」定成了三条形状**（后面每份照抄，别每次重新想）：

13. **构造统一走 `create(db)` 静态工厂**：GDScript 的 `new(db)` 是**解析期**报错
    （看不见 C# 的构造签名，按 0 参处理）。这条已在 `AttributeCalculator` 上量过，
    这次在 `growth_calculator` 上复核了一遍。
14. **GDScript 调到的方法一个可选参数都不留**：C# 的默认参数值对 GDScript 不生效，
    少传实参直接 `Invalid call. Nonexistent function`。所以 GDScript 调用方一律写明全部实参
    （`codex_bonus(n, 1.0)`／`passive_contributions(ids, {})`）——**看着啰嗦，是必须的**；
    C# 侧也不留默认值，免得两个语言看到的调用形状不一致。
15. **常量表给静态取值口，且现建现用**（`Defaults()`）：GDScript 读不到 C# 的 `const`／静态字段，
    而 Godot 集合挂在 C# 静态字段上会活到进程收尾（第 8 条那个 `SEHException` 的现场）。

**顺手清掉一处死引用**：`level_service.gd` 里 preload 了 `growth_calculator.gd` 却**一次没用**
（`rg` 只有那一行）——文件一删它就从"没用的 preload"升级成"加载失败"，直接删。

**用例双跑**：新加 `tests/TestGrowth.cs`（56 条断言／47 个静态调用点，纯计算那几节）；
`tests/test_growth.gd` **留着**——它还要测 `CharacterSheet` 面板，而 `CharacterSheet`／`GameState`
都还没搬。两份并存＝哪边红哪边是回归，`验证清单.csv` 的 `growth_system.verify_paths` 两行都写上。

**本轮验收**：`run_tests.bat` **9838 条**（基线 9832）`SELFCHECK: OK`、`run_tests_cs.bat`
**1070 条**（下限 986 → **1060**）`OK`、`validate_tables.ps1` **PASSED**、
`run_mutation_sweep.bat` **PASSED**（64 cast／漏 0／失效 0——本次专门看
「敌人内功容量／招式槽」那几条规则换了取值口之后还抓不抓得住）。

## 二点一二、core 第五批：`rng_service` ＋ `affix_roller` ＋ `talent_service`（2026-10-10）

三份互不依赖的小叶子一次搬完（`src/core/RngService.cs`／`AffixRoller.cs`／`TalentService.cs`），
旧 `.gd`／`.gd.uid` 已删。**这一批的难点不在文件本身，在调用面**：

| 文件 | 调用面 |
|---|---|
| `rng_service` | **42 个文件、201 处**——`RngServiceScript.new(SEED)` → `create(SEED)`；旧的无参 `new()`
（＝随机种子）改名成 **`create_random()`**，好在调用点一眼认出来 |
| `affix_roller` | 11 处 `.new(db, rng)` → `.create(db, rng)`；四个 static（`target_of`／
`contributions_of`／`describe`／`summarize`）名字不变，只换 preload 路径 |
| `talent_service` | 全是 static，只换 preload 路径（+ 4 处 `:=` 见下） |

16. **`:=` 类型推断在 C# 调用点上会解析失败**（这一批新踩的坑）：
    `var x := TalentServiceScript.budget(db)` 与 `var n := roller.affix_count(...)` 都报
    `Parse Error: Cannot infer the type of "n" variable because the value doesn't have a set type.`
    ——C# 脚本的返回值没有静态类型可推。**修法是写死类型**（`var n: int = ...`／`var raw: float = ...`），
    不是加 `Variant(...)`。本批共 4 处：`tests/test_affix.gd`／`tests/test_creation.gd`／
    `src/core/skill_loadout.gd`／`src/core/character_sheet.gd`。
    **排查捷径**：先跑一趟 `godot --headless --editor --quit --path .`，所有 Parse Error 一次打出来，
    比"跑一轮用例→修一处→再跑一轮"（一轮 55 秒）快得多。
17. **改了 `.new()` 的形状，「没给种子的随机源」那道门限要跟着改口径**
    （`tests/test_handshake.gd::UNSAFE_RNG_PATTERNS` ＋ 消息文案 → `create(SEED)`）：
    名单里同时留新旧两种写法，并**做了反向验证**——往 `tests/test_affix.gd` 临时加一行
    `RngServiceScript.create_random()`，门限当场红（`[FAIL] … 里有没给种子的随机源`），撤掉后恢复绿。
18. **`RandomNumberGenerator.RandfRange` 在 C# 里是 `float` 入参**：端点要显式转 float 才**不改**
    既有期望值——引擎单精度构建内部本来就是 `real_t`，GDScript 经 Variant 调用时同样收窄。

**用例**：新加 `tests/TestAffix.cs`（201 条：同种子可复现、`chance` 边界、加权抽取、
稀有度排名取表序、词条数落在区间、槽位／稀有度门槛、掷词条的确定性与数值区间、贡献与文案换算）
与 `tests/TestTalent.cs`（69 条：预算／花费／剩余／`can_pick` 三种拒绝／`toggle` 三态／卡片排序）；
`tests/test_affix.gd`／`test_talent.gd`／`test_creation.gd` 留着跑整合（进存档、进掉落、进战斗）。
`验证清单.csv` 的 `char_template`／`equip_system` 两行各挂上新的 `.cs` 用例。

**本轮验收**：`run_tests.bat` **9837 条**（基线 9832）`SELFCHECK: OK`、`run_tests_cs.bat`
**1340 条**（下限 1060 → **1330**）`OK`、`validate_tables.ps1` **PASSED**、
`run_mutation_sweep.bat` **PASSED**（64 cast／漏 0／失效 0）。

## 二点一三、core 第六批：`level_service` ＋ `mastery_service`（2026-10-10）

两份都只依赖表与 `GameState`，一次搬完（`src/core/LevelService.cs`／`MasteryService.cs`），
旧 `.gd`／`.gd.uid` 已删。调用面各 4–5 处，机械换 preload 路径 ＋ `.new(` → `.create(`：
`battle_screen.gd`（战斗结算施放）／`cultivate_screen.gd`（打坐界面）／`character_screen.gd`
（面板进度）／`battle_reward.gd`（战斗入账）＋ 四条用例（`test_level`／`test_mastery`／
`test_practice`／`test_end_to_end`）。

19. **`cap` 这类「GDScript 默认参数」要显式补上**（照 `GrowthCalculator` 那三条形状的第 2 条）：
   `apply_combat_usage(char_id, usage, cap=0)` 在 C# 里**不留默认值**，所以 5 处两参调用
   全部改成三参（`…, 0`）——`test_mastery.gd` 4 处、`test_practice.gd` 1 处。
20. **`:=` 推不出 C# 返回值**（第 16 条的同一坑，这回又踩一次）：`battle_reward.gd:45`
   `var levels := LevelServiceScript.new(db, state)` 改成 `.create(...)` 后必须写 `=`（不带类型推断）。
   排查照旧先跑一遍 `godot --headless --editor --quit`，一次打全 Parse Error。
21. **C# 用例这一步能直接驱动 GDScript 的 `GameState`**：`tests/TestCase.cs` 新增夹具
   `SoloState(db)`（`GD.Load<GDScript>("res://src/core/GameState.cs").Call("new_game", db, …)`），
   再用 `state.Call("level_of"／"set_mastery"／…)` 与 `state.Get/Set("party_exp"／"inventory")` 读写
   ——**实测通了**，与 `TableValidator.World` 读世界层静态方法同一个形状。所以这一批的 C# 用例
   是**全量**覆盖（含经验池兑换等级、打坐拒绝／成功、`cap` 只压涨不压低），不是只挑纯计算。
   （更早的天赋那一批因为 `state` 只读字段，把状态相关几节留在了 GDScript 侧；`GameState` 搬成
   C# 之前，这条 `SoloState` 是后面所有状态型用例的通行证。）

**用例**：`tests/TestLevel.cs`（56 条：经验表逐级递增／`upgrade_points`／99 不升 100 升／跨级／
满级封顶只记账／`exp_progress`／`describe` 文案）＋ `tests/TestMastery.cs`（47 条：上限与星级／
费用曲线逐级上涨／未学·满级·缺钱三种拒绝／「差一文」边界／施放结算与封顶／`cap` 只压涨不压低／
结算文案）；`test_level.gd`／`test_mastery.gd`／`test_practice.gd`／`test_end_to_end.gd` 留着跑整合
（升级带动加点与基础属性、战斗入账、打坐界面多人分支、存档往返）。
`验证清单.csv` 的 `growth_system` 那一行挂上两个新 `.cs` 用例；
`模块对接表.csv` 的 `code_paths` 换 `MasteryService.cs`／`LevelService.cs`；
`框架说明.md` 的「经验与升级」指针改掉。

**本轮验收**：`run_tests.bat` **9845 条**（基线 9832 未动——GDScript 用例没增删）`SELFCHECK: OK`、
`run_tests_cs.bat` **1443 条**（下限 1330 → **1433**）`OK`、`validate_tables.ps1` **PASSED**、
`run_mutation_sweep.bat` **PASSED**（64 cast／漏 0／失效 0）。

## 二点一四、core 第七批：`buff_service` ＋ `set_service`（2026-10-10）

两份一起搬（`src/core/BuffService.cs`／`SetService.cs`），旧 `.gd`／`.gd.uid` 已删。
调用面 8 个文件、~20 处：`battle_actor`（缓存的实例）／`battle_simulator`（5 处）／
`character_sheet`（套装贡献＋面板那个「能不能用」判定）／`cultivate_screen`（打坐余韵 2 处）／
`enemy_factory`（敌人装备的常驻 buff）／`inventory`（装备贡献，2 处）／`party_builder`（进场发放）／
`test_buff`；机械换 preload 路径 ＋ `.new(` → `.create(`。

22. **这一批的真正难点是「常量跨语言」**（坑⑩的正面撞上）：GDScript 读不到 C# 的 `const`，而
    `battle_actor.gd` 读 `STACK_UNIQUE`／`STACK_STACK`／`EFFECT_KIND_SPECIAL`／`EFFECT_KIND_COMPUTED`，
    `test_buff.gd` 读 `POLARITY_*`／`DURATION_*`／`STACK_STACK`／`SCOPE_BATTLE`／`EFFECT_KIND_STAT`，
    还有 `SetServiceScript.KIND_EQUIP`。**做法照 `AttributeCalculator.contrib_attr_point()` 那套**：
    每个对外常量配一个同名小写静态方法（`BuffService.stack_unique()`／`SetService.kind_equip()`…），
    调用点跟着换；P5 把 GDScript 清零后再收回成字段。这一批共 15 个取值口。
23. **变异探针第三块（代码常量）对 `.cs` 是盲区**：`analysis_mutations_code.ps1` 每个 cast
    只把 `const NAME := 值` 改一处、然后跑 `res://tools/run_tests.gd`——**改 `.cs` 不会重编**，
    所以量不出来。那三条打 `buff_service.gd` 常量的 cast（`POLARITY_BUFF`／`POLARITY_DEBUFF`／
    `DURATION_PERMANENT`）改成指向 `src\core\BuffService.cs` 的 C# 写法，**并登记进
    `$intentionallyUnpinned`**（理由写明「改 .cs 不重编 → 量不出来」，替代网是 `tests/TestBuff.cs`
    的绝对值断言）；P4 给探针加 `.cs` 支持后把这三行豁免删掉即可。
    ——**别直接删 cast**：删了就没记录，下次会当新洞再追一遍（AGENTS 的原话）。
24. **`:=` 推不出 C# 返回值（第 16／20 条之后第三次）**：`character_sheet.gd`
    `var count := sets.count_for(...)` → 写死 `var count: int = …`。
    排查仍是先跑一遍 `godot --headless --editor --quit`，一次打全 Parse Error（这轮就这一处）。
25. **`contributions_of(buff_id, source="", stacks=1)` 的默认参数去掉**，两处调用补第三个实参：
    `character_sheet.gd`（套装档位）与 `inventory.gd`（装备常驻 buff）都传 `1`——档位／装备的 buff
    不叠层，这是刻意的。

**用例**：新加 `tests/TestBuff.cs`（112 条：15 个取值口的绝对值／六张表行数／极性·作用域·持续·
叠加·效果类型的规则与缺行兜底／数值修正与贡献通道（含 3 层乘法与 source 退回 `buff:<id>`）／
发放表（常驻筛选、按 buff 反查、`has_cast_grant`）／套装计数口径与档位向下兼容）；
`test_buff.gd` 留着跑整合（真背包的 `count_for`、贡献进面板与战斗、叠加与递减、六指令、
进场 buff、战斗外计时、招式自带增益、叠层放大）。
`验证清单.csv` 的 `buff_system`／`set_system` 两行挂上 `tests/TestBuff.cs`；
`模块对接表.csv` 的 `code_paths` 换 `BuffService.cs`／`SetService.cs`。

**本轮验收**：`run_tests.bat` **9849 条**（基线 9832 不动）`SELFCHECK: OK`、
`run_tests_cs.bat` **1555 条**（下限 1433 → **1545**）`OK`、`validate_tables.ps1` **PASSED**、
`run_mutation_sweep.bat` **PASSED**（64 cast／漏 0／失效 0）。

## 二点一五、core 第八批：`story_service` ＋ `chapter_service` ＋ `pity_tracker` ＋ `status_rules`（2026-10-10）

四份一起搬（`src/core/StoryService.cs`／`ChapterService.cs`／`PityTracker.cs`／`StatusRules.cs`），
旧 `.gd`／`.gd.uid` 已删。调用面 13 个文件、~30 处：两个控制器（`overworld_controller`／
`local_map_controller`）、两个面板（`character_screen`／`character_sheet`）、`game_state`、
战斗侧（`battle_screen`／`battle_actor`）＋ 六条用例（`test_story`／`test_dialogue`／`test_drop`／
`test_local_map`／`test_overworld`／`test_end_to_end`）；机械换 preload 路径 ＋
`.new(` → `.create(`，另有 `PityTrackerScript.VERSION` → `.version()`、
`StoryServiceScript.TITLE_SUFFIXES` → `.title_suffixes()`、`TITLE_CONDITION_FORMAT` →
`.title_condition_format()`（常量跨语言那套，见坑⑩／第 22 条）。

26. **C# 静态类调 GDScript 的静态方法**（这一批第一次正面用到）：`ChapterService`／`StoryService`
    守卫条件要读 `guide_service.gd` 的 `condition_met()`，给技能要走 `skill_grant.gd` 的
    `grant_from_source()`。做法是 `GD.Load<GDScript>("res://src/core/guide_service.gd")
    .Call("condition_met", state, condition)`——**实测通**（与 `TableValidator` 读世界层静态方法是同形），
    所以 core 里剩下的"跨语言只读"调用面照这个形状搬就行。缺点是每次 `GD.Load` 一次；这条路只在
    P3 世界层/面板层自己搬完前的过渡期存在，P5 收尾时这些 `.gd` 已清零、调用也换成 C# 直调。
27. **`Variant.type.Nil` 判"列为空"**：`StatusRules.rate()` 对应 GDScript 的 `row.get(column) == null`，
    C# 侧读表列要写 `row.Get(column).VariantType == Variant.type.Nil`（列缺省视为 0），
    别用 `== null`（`Godot.Variant` 是结构体，"空列"会拆成 `Nil` 型而不是 C# 的 `null`）。
28. **行类返回 `Godot.Collections.Array` 时累加用 `AddRange`**：`StoryNodeRow.bonus_contributions()`
    返回的是 Godot 集合，`StoryService.permanent_contributions` 里累积贡献要
    `out_contribs.AddRange(row.bonus_contributions())`（`Godot.Collections.Array` 有 `AddRange`）。
29. **`:=` 推不出 C# 返回值（第 16／20／24 条之后第四次）**：`StoryService.title_of/place_matches/
    pending_for/claim_for` 那几个从 C# 取值的调用点，GDScript 里 `var x := …` 都要写死类型。
    排查仍旧是批量跑一次 `godot --headless --editor --quit`，一次打全 Parse Error（这轮 0 处，
    因为上一批已把形状固化，`--editor --quit` 只剩证书／编辑器设置两条环境报错）。
30. **变异探针 `.cs` 盲区（第 23 条同源，这次是 `VERSION`）**：`pity_tracker.gd` 那条
    「`VERSION 1->2`」cast 改指 `src\core\PityTracker.cs` 的 C# 写法，**并登记进
    `$intentionallyUnpinned`**（理由「改 .cs 不重编 → 量不出来」，替代网是 `tests/TestPity.cs`
    的绝对值断言）；P4 给探针加 `.cs` 支持后删掉这行豁免。**别直接删 cast。**

**用例**：新加 `tests/TestStory.cs`（57 条：常量取值口绝对值／章节推进与标题授予／守卫条件三条
（好感·旗标·章节）／待办与领取通道／跨语言只读条件），`tests/TestPity.cs`（22 条：计数器形状、
`version()`／每档保底的抽取与重置），`tests/TestStatusRules.cs`（7 条：`burn_def_cut`／`qi_regen_mul`
两条新列的取值与缺行兜底）；`tests/TestCase.cs` 新增 `NewGame(db, difficulty, charIds)` 夹具
（`SoloState` 改为转调它，供"带指定角色/难度开局"的用例共用）。
旧用例 `test_story.gd`／`test_drop.gd` 等**留着跑整合**（真 GameState 推进章节、真掉落触发保底）。
`验证清单.csv` 的 `drop_system` 行加 `tests/TestPity.cs`、`guide_system` 行加 `tests/TestStory.cs`；
`模块对接表.csv` 的 `drop_system.code_paths` 换 `src/core/PityTracker.cs`；
`框架说明.md` 里 `pity_tracker.gd` 的指针改掉。

**本轮验收**：`dotnet build` 0 错 0 警、`--editor --quit` 无 Parse Error、
`run_tests_cs.bat` **1641 条**（下限 1545 → **1631**）`OK`、
`run_tests.bat` **9853 条**（基线 9832 不动）`SELFCHECK: OK`、
`validate_tables.ps1` **PASSED**、`run_mutation_sweep.bat` **PASSED**（64 cast／漏 0／失效 0）。

## 二点一六、core 第九批：`encounter` ＋ `practice_service`（2026-10-10）

两份一起搬（`src/core/Encounter.cs`／`src/core/PracticeService.cs`），旧 `.gd`／`.gd.uid` 已删。
调用面 14 个文件、~70 处：`battle_simulator`／两个控制器／`roaming_enemy`／`battle_screen` ＋
八条用例（`test_encounter`／`test_battle`／`test_battle_session`／`test_battle_ui`／`test_copy_audit`／
`test_poise`／`test_skill_grant`／`test_practice`／`test_local_map`）＋ 变异探针 ＋ 两份验收脚本。

31. **`.cs` 的成员怎么给 GDScript 看**（这一批把形状钉死）：GDScript 读写 C# 对象要**属性**
    （`[Export] public string team_name { get; set; }`，行类那一套），方法名保留 **snake_case**
    才按名字调得到；**零参 `new()` 不用改成 `create()`**（与 `TableDb` 同一条，见二点九）——
    只有带构造参数的 `new(db)` 才是解析期报错。所以 `EncounterScript.new()` 原样保留，
    而 `build(...)`／`contact_front()` 这类静态口照旧 snake_case。
32. **读别的 GDScript 的常量**：`Encounter` 的 `SIDE_ALLY/SIDE_ENEMY` 必须取自
    `BattleActor.SIDE_*`（单一出处），而那份 `.gd` 还没搬——做法是
    `GD.Load<GDScript>(path).GetScriptConstantMap()[name]`（与 `TableValidator.ScriptConstants`
    同一套）。**别在 C# 里抄一份 0/1**：那正是 决策 217 修掉的漂移。
33. **`Godot.Collections.Dictionary` 没有"带默认值的 get"**：GDScript 的 `dict.get(k)` 缺键给 null，
    C# 要自己 `ContainsKey`。`Encounter.build` 的入参既可能是表行（`Resource`）也可能是字典
    （练习战／切磋／隐藏 Boss 都丢字典），所以写了一个 `RowGet(row, key, fallback)` 把两条路收一处。
34. **门限对 `.cs` 视而不见（第三处，这次是两道）**：
    ① `tests/test_copy_audit.gd::_gd_files` 只收 `.gd`（文件里那句跳过 `TableValidator.cs` 是**死代码**，
    说明当初就想扫 `.cs` 没做完）——扫描面扩到 `.cs` 后会有 **117 条**命中，**全是**构建期校验器
    （`TableValidator*.cs`）的 error 串，所以 skip 从"写死一份"改成**按 `TableValidator` 前缀挡整族**，
    并把日志行判据补上 C# 的 `PushError`／`PushWarning`／`Printerr`／`Print(`；
    ② `tools/validate_tables.ps1` 的「代码点名的 id」那处也只扫 `*.gd`——`PracticeService.TEAM_ID`
    搬进 `.cs` 之后 `team_dummy_training` 会被报成「孤立数据」，同一个文件里补齐 `.cs`。
    **规律**：每搬一份 `.gd`，就要问一句"有哪道门限是只扫 `.gd` 的"。
35. **写回文件时把 BOM 写了两遍**（本轮真正的坑）：批量助手
    `[text.Encoding]::UTF8.GetString(bytes)` → `WriteAllText(…, UTF8Encoding($true))` 会把原 BOM 变成
    **两个** U+FEFF（解码出来的字符串里本就带着第一个）。后果分两档：`.ps1` 双 BOM **直接解析失败**
    （`Unexpected token 'param'`——报错点还指向文件第一行的 `param(`，看不出是 BOM 的问题）；
    `.csv` 双 BOM 现在不报错，但首列名会带上一个不可见字符（早晚咬人）。
    **写回要么 `WriteAllBytes`（连原 BOM 一起搬字节），要么先 `$text.Substring(1)` 把解码出来的那个
    U+FEFF 去掉再加。** 本轮写坏 5 个（4 个是本轮改的，另 1 个 `docs/design/06_配置表说明.md` 是别人早先
    留下的），已逐个还原成单 BOM。

**用例**：新加 `tests/TestEncounter.cs`（51 条：阵营／接触方式字面量、五种接触的开局规则与
`combat_const` 取值、暂缓规则、表行与字典两个来源、`team_name`/`threat_tag` 的兜底），
`tests/TestPractice.cs`（42 条：入口识别、两条上限读表、单场封顶与 `exp_room` 的 1400／100／0、
三人队伍 4200／1060、到顶提示与 `data_ready` 自洽、`build_encounter` 的标记与来源字段）。
旧用例 `test_encounter.gd`／`test_practice.gd` 等**留着跑整合**（明雷接触距离、内存夹具补齐两行真开打）。
`验证清单.csv` 的 `roaming_enemy` 加 `TestEncounter.cs`、`growth_system` 加 `TestPractice.cs`；
`模块对接表.csv` 的 `roaming_enemy`／`ui_copy` 换 `src/core/Encounter.cs`；
`设计实现对照.md` 的 `Encounter._apply_contact` 改成 `ApplyContact`；
`src/core/AGENTS.md`／`第三方代码.md` 的 `practice_service.gd` 指针改掉；
变异探针的 `dummy service_id 改字` 改指 `PracticeService.cs` 并登记 `$intentionallyUnpinned`。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests_cs.bat` **1734 条**（下限 1631 → **1724**）、
`run_tests.bat` **9854 条**（基线 9832 不动）`SELFCHECK: OK`、`validate_tables.ps1` **PASSED**、
`run_mutation_sweep.bat` **PASSED**（64 cast／漏 0／失效 0）。

## 二点一七、core 第十批：`save_store` ＋ `save_service` ＋ `settings_store`（2026-10-10）

三份一起搬：`src/core/save_store.gd`（157 行）→ **`src/core/SaveStore.cs`**、`save_service.gd`（53）→ **`SaveService.cs`**、`settings_store.gd`（75）→ **`SettingsStore.cs`**；旧 `.gd`／`.gd.uid` 已删。**这一批的坑全在「GDScript 与 C# 的类型边界」上**，都是第一次遇到：

36. **GDScript 不能 `extends` 一个 C# 脚本，也不能拿它当类型标注**。`test_menu.gd` 里那个 `class ReadOnlyStore: extends "res://src/core/SaveStore.cs"`（存档目录只读的桩）当场让**整个用例文件加载不了**：`SCRIPT ERROR: Parse Error: Could not resolve super class inheritance from "res://src/core/SaveStore.cs"`。表现是 `run_tests` 只报一句「`res://tests/test_menu.gd` 无法加载或解析失败」，**看不到真因**——真因要去 `.logs/tests.log` 里搜 `super class`；`menu_controller.gd` 里那些 `var _store: SaveStore` 标注同理，这一批也撤掉了（C# 类不进 GDScript 的类型系统，要标就得给它 `[GlobalClass]`，P5 清零前不值当）。**替代形状**：桩自己 `extends RefCounted`，只实现用例真用到的那几个口，其余口转手给一个真 store（`const Store := preload("…/SaveStore.cs")` ＋ 组合）。
37. **C# 的强类型参数会让「注入的 GDScript 桩」在调用点炸，而不是在注入点**。`SaveService.create(SaveStore store, …)` 收下 `FailingStore`／`ReadOnlyStore` 时抛 `System.InvalidCastException: Unable to cast object of type 'Godot.RefCounted' to type 'Lunhui.Core.SaveStore'`，并且**紧跟着还有一条 `Nonexistent function 'create' in base 'CSharpScript'`**——两条一起打，很容易误判成「静态方法没注册进桥」（第一眼就是这么判的）。**口径**：跨语言面收 `GodotObject`，真·C# 实例走强类型快路径、桩走 `Call` 兜底：`store is SaveStore typed ? typed.save_slot(slot, state) : store.Call("save_slot", slot, state).AsGodotDictionary()`（与「迁移期跨语言读写 `GameState` 用 `GodotObject` ＋ `Call/Get/Set`」同一条路子）。
38. **`:=` 推不出类型，第四次踩到**：`var path := store.slot_path(2)`（`store` 是变体）→ `Parse Error: Cannot infer the type of "path" variable`。**排查捷径照旧**：`godot --headless --editor --quit` 一次把全项目的 Parse Error 打出来，比一遍遍跑 52 秒的自检快得多（这一批靠它一次找齐 `new()` 参数不对 ＋ 两处 `:=`）。
39. **判据别只信自检的收尾输出**：`run_tests.bat` 失败时打的是「`.logs\tests.log` contains a GDScript runtime error -- the check that hit it stopped early」，那一条**当场停**；真正的原因（`Nonexistent function 'list_slots' in base 'RefCounted (ReadOnlyStore)'`）只在日志里。

**跨语言面**：`SaveStore` 的常量走静态取值口（`default_dir()`／`default_dir_name()`／`default_slot_count()`，P5 收回成字段）；`list_slots(db)`／`describe_slot(slot, db)`／`load_slot(slot, db)` **一律写全实参**（`list_slots(null)`）；`SaveService.make_default()` 返回的是 **`SaveStore`**（历史命名，调用方拿到它再 `create(store, state)`）；`SettingsStore` 指定目录走 `create(dir)`、零参仍 `new()`（后者是 GDScript 真零参，不算坑②那种「带参构造」）。**19 个调用点**机械改完（`SaveStoreScript.new(` → `.create(`、`SaveServiceScript.new(` → `.create(`、常量改取值口、三处 `.save("…")` → `.save("…", false)`）。变异探针 `DEFAULT_SLOT_COUNT 6→3` 改指 `src\core\SaveStore.cs` 并登记 `$intentionallyUnpinned`。`模块对接表.csv` 的 `save_system`／`audio_system` 换 `.cs`；`验证清单.csv` 两行加 `TestSaveStore.cs`／`TestSettings.cs`；`src/core/AGENTS.md`／`框架说明.md`／`设计实现对照.md` 的 `.gd` 指针改掉。

**用例**：新加 `tests/TestSaveStore.cs`（槽枚举／写盘读回／损坏档／选槽策略／`SaveService` 时机）与 `tests/TestSettings.cs`（两键互不覆盖／坏档退回默认／写不进去如实报错）。**旧的 `test_save_store.gd`／`test_settings.gd` 留着跑整合**（真场景上的枢纽按钮／读档流程）。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests_cs.bat` **1828 条**（下限 1724 → **1818**）、`run_tests.bat` **9858 条**（基线 9832 不动）`SELFCHECK: OK`、`validate_tables.ps1` **PASSED**、`run_mutation_sweep.bat` **PASSED**（64 cast／漏 0／失效 0）、`check_loop.bat` **OK**（动了存档相关代码，这条必跑）。

## 二点一八、core 第十一批：`battle_reward` ＋ `drop_resolver`（2026-10-10）

两份一起搬：`src/core/battle_reward.gd`（127 行）→ **`src/core/BattleReward.cs`**、`drop_resolver.gd`（186）→ **`src/core/DropResolver.cs`**（旧 `.gd`／`.gd.uid` 已删）。挑这两份先做是因为**同族三份小叶子都等着它们**：`dialogue_service`／`world_event_service`／`recruit_service` 的奖励发放全走 `BattleReward.grant_item()`，`dungeon_service`／两个控制器走 `DropResolver`。

调用面 **13 个 .gd 文件**：`battle_screen`／`local_map_controller`／`dungeon_service`／`dialogue_service`／`event_check_service`／`npc_service`／`recruit_service`／`world_event_service` ＋ 5 条用例。四条新坑：

40. **带默认值的参数：搬之前先 `rg` 调用点，把实参写全**（坑②的具体形态）。`roll_group(group, difficulty="normal", options={})` 在 `.gd` 里有 **5 处只传了两个实参**（`test_drop` 的 2000 次循环全在里面），`grant_item(db, state, item_id, qty=1, roller=null)` 有 **7 处只传四个**，`grant(…, roller=null)` 两处、`apply(…, roller=null)` 一处。C# 的默认值对 GDScript **不生效**（少传一个就是运行期 `Invalid call`），所以这些点一个不落地改成写全（`grant_item(db, state, item_id, 1, null)`）。**改的时候顺手检查「同一个调用形状在别的文件里还有吗」**——`preload` 路径替换与实参补全这两步都做过一遍 `rg` 复核。
41. **C# 里现建一个 GDScript 实例**：`GD.Load<GDScript>(path).Call("new")`。这是仓库里**第一次**这么做（此前跨语言只调静态方法／读写属性），实测通——`BattleReward.grant()` 那条「没有背包就现建一个 `Inventory`」的分支是它唯一的调用点，`tests/TestBattleReward.cs` 专门把状态里的 `inventory` 摘掉再验一遍，**这条断言同时就是那个调用的回归网**。
42. **「谁负责建对象」要照原口径抄，不许自己顺手补**：`grant_item()` 在背包为空时**故意**报「没有会话状态」（它只负责往里放东西），现建背包在 `grant()` 里。第一版把这两处搞反了（想着"顺手建一个更省事"），用例当场红——这类"顺手改良"会把调用方原本依赖的语义悄悄改掉。
43. **两次 C# 用例侧的教训**：① **绝对值断言会被「开局自带的东西」打脸**——`equipment_count()` 在书生身上不是 0（开局穿着起始装备），断言要写**增量**；② **`!`（null-forgiving）不是运行期保护**——`rebuilt!.Call(…)` 在 `rebuilt` 为 null 时照样抛 `NullReferenceException`，而 C# 用例链是「一个用例一个 try」，那条异常把后面 **7 条断言全吞了**，报出来的是「假绿：写了 52 处断言，只跑了 45 条」——看到这条，先怀疑自己用例里有会抛异常的分支，别急着往白名单里塞。

**跨语言面**：`DropResolver` 的实例三参构造走 `create(db, rng, pity)`（`pity` 可为 `null`）；`party_drop_bonus(allies)` 里对战斗单位按鸭子类型调 `stat("drop_rate")`（`battle_actor.gd` 还没搬）；`set_pity`／`roll_group_many` 两个**只被用例调用**的接口继续留在 `TEST_ONLY_API_ALLOWED`（C# 侧它们是非静态 public，正好落在那道门限的扫描面里）。

**用例**：新增 `tests/TestBattleReward.cs`（52 条：铜钱进钱袋**不进背包**／装备建实例带词条／背包满算「没捡起来」不算失败／配错 id 如实报错／经验进队伍池并升级／摘掉背包也能重建／`apply` 从 `result.rewards` 读）与 `tests/TestDropResolver.cs`（23 条：不存在的掉落组空表＋出声／必掉槽每次都出／概率区间／数量区间／难度只改几率／首功武器优先／**保底到点必出**（内存夹具复制一行，不碰 CSV）／机缘倍率／空队伍机缘为 0）。旧的 `test_drop.gd`／`test_affix.gd`／`test_dungeon.gd`／`test_level.gd`／`test_end_to_end.gd` 继续跑整合。

**本轮验收**：`dotnet build` 0 错 0 警、`--headless --editor --quit` 无 Parse Error、`run_tests_cs.bat` **1903 条**（下限 1818 → **1893**）、`run_tests.bat` **9866 条**（基线 9832 不动）`SELFCHECK: OK`、`validate_tables.ps1` **PASSED**、`run_mutation_sweep.bat` **PASSED**（64 cast／漏 0／失效 0）、`check_loop.bat` **OK**。

`模块对接表.csv` 的 `drop_system`／`item_system`／`ui_copy` 换 `.cs`；`验证清单.csv` 挂上两个新 `.cs`；`框架说明.md` 的「掉落掷点」那行指针改掉。

## 二点一九、core 第十二批：`dialogue_service` ＋ `recruit_service` ＋ `world_event_service`（2026-10-10）

三份同族小叶子一起搬：`src/core/dialogue_service.gd`（147 行）→ **`src/core/DialogueService.cs`**、
`recruit_service.gd`（132）→ **`src/core/RecruitService.cs`**、`world_event_service.gd`（163）→
**`src/core/WorldEventService.cs`**（旧 `.gd`／`.gd.uid` 已删）。挑它们是因为第十一批刚把
`BattleReward.grant_item()` 搬完，这三份的奖励发放与好感/线索都只差这一口；而且它们的
**调用面主要在 `.gd`**（`clue_service`／`character_screen`／`clue_screen`／`npc_panel`／
两个控制器 ＋ 3 条用例）。七条新坑：

44. **C# 的 `const` GDScript 读不到**（坑⑩的老问题，本轮又踩一次）：`DialogueService.OPENING_NODE`
   是 GDScript 侧（`local_map_controller`）要读的，所以照着 `WorldEventService.lifetime()` 的形状
   补一个 `opening_node()` 静态取值口。**每搬一份先问「有没有哪个常量是 `.gd` 在读的」**，
   `rg` 一遍常量名比事后 parse 报错便宜。
45. **`const` 不能拿 C# 静态调用的结果去赋**：`const OPENING_CHOICE_NODE := DialogueServiceScript.opening_node()`
   连 Godot 的 `const` 推不出来（`:=` 也推不出跨语言返回值的静态类型），改成 `var`。
   **取舍**：宁可让那个名字是 `var`，也别把 C# 的常量值再抄一遍到 `.gd`（那正是「单一定义」门限在盯的）。
46. **C# 的 `private` 不给 GDScript 看见**：`WorldEventService._apply_effect` 在 `.gd` 里是私有命名，
   但用例要**直接传一行改过的世界事件行**进来（`test_world_event` 的 `_check_unwired`），
   C# 侧 `_` 开头的方法 GDScript 同样调不到 → 改名 `apply_effect` 并开成 `public`。
   它不是「只给用例的接口」（`trigger()` 在游戏路径里也调它），所以没进 `TEST_ONLY_API_ALLOWED`。
47. **有两条门限只扫 `.gd`，搬走 `.gd` 就会假红**：
   ① `test_handshake_flags` 的旗标来源扫描以前只认 `"flag_x"` 这种**带引号的字面量**——
    C# 写的是插值 `$"flag_{char_id}_joined"`／`$"flag_open_{…}"`，扫不到，于是
    `flag_ch_ci_joined`／`flag_open_mou`／`flag_open_li` 三枚被报「没人点亮」。
    修法是给扫描器加一条 C# 插值正则，把 `{…}` 折成 `%s` 再按前后缀匹配（**洞里带引号也要吃掉**，
    `$"flag_open_{flag.Substring("heart_".Length)}"` 就是这种）。
    ② `test_handshake_code::_check_no_absolute_autoload_paths` 的「扫到足够多的脚本（50 个）」
    是拿 `src/**/*.gd` 数的——**迁移期这个数只会一直往下掉**，改成盯 `.gd`＋`.cs` 的总数
    （扫描健康度）＋「`.gd` 还扫得到」两条，P5 清零时连同整份 GDScript 链一起退场。
    **纪律**：每搬一份都要问一句「有哪道门限是只扫 `.gd` 的」。
48. **`Inventory.equipment` 是 `Dictionary`（实例 id → 条目），不是 `Array`**：C# 用例里
    `Get("equipment").AsGodotArray()` 拿到空数组（不报错！），于是「入队发了几件装备」这类断言
    会以 `0 == 0` 的方式**假绿**。要数就用 `inventory.equipment_count()`（它才是那件事的唯一出处）。
    同一类坑在第十一批的「绝对值断言被开局自带装备打脸」已经露过一次头。
49. **条件语言把空串当「不满足」，于是「留空＝无条件」那一层必须由调用方自己补**——
    本轮因此抓到 `world_event_service` 的一个**真 bug**（不是迁移引入的，`.gd` 版同样有）：
    `world_event.condition` 这一列发行数据**八行全空**，而 `eligible()` 直接把它塞给
    `GuideService.condition_met`（空串 → `state.has_flag("")` → false）→ **池子永远为空**、
    `pick()` 永远 `null`、大地图上一场随机事件都出不来（19 §四整节形同虚设，而且没有任何用例覆盖过）。
    修法是在服务层补一层 `ConditionOk()`：**留空 = 无条件**，其余照旧交给 `GuideService`
    （与 `DialogueService.condition_ok` 同一口径）；`TestWorldEvent.cs` 用 `pick(roll=0)`
    拿到巡查那条来钉住它，`check_loop` 的「招募链／序幕择念」也顺带跑了这条路径。
50. **C# 用例抛异常时只报 message 不够定位**：第一版 `RunTestsCs` 把 `error.Message` 收进失败列表，
    报出来只有一句「Object reference not set to an instance of an object」。
    现在额外把**完整异常（含堆栈）**打到日志（`[运行期异常] <用例名>：<exception>`），
    这一次就是靠它一眼定位到 `TestWorldEvent._CheckPool` 第 167 行的。

**跨语言面**：三份的条件语言都走 `GuideService.condition_met`、好感都走 `NpcService.add_favor`
（两者都还是 `.gd`，过渡期 `GD.Load<GDScript>().Call(...)`）；`WorldEventService` 的六个常量
（`LIFETIME`／`DESPAWN_DISTANCE`／`TRIGGER_DISTANCE`／`SPAWN_MIN`／`SPAWN_MAX`／`COOLDOWN`）
各配一个小写取值口（`lifetime()` …），P5 再收回成字段。

**用例**：新增 `tests/TestDialogue.cs`（33 条：入口排掉接续节点／条件语言决定说不说与能不能选／
三样效果落地／`flag_open_*` 补旗／负好感扣到地板／`parse_set_flags` 分号多值）、
`tests/TestRecruit.cs`（29 条：`is_initial` 不再跳过／小地图与父区域两条匹配／区域路径只认地标／
入队奖励回气散＋好感 20／重复入队被拒且不重发装备／`MAX_PARTY` 绝对值）、
`tests/TestWorldEvent.cs`（72 条：八条 effect 指向逐条一致／赠礼与材料入账／线索旗标／
判定直接结算／trade・spar 只交请求／空效果与未知类型如实说话／按权重抽与一次性退出池子／
常量取值口）。旧的 `test_dialogue.gd`／`test_recruit.gd`／`test_world_event.gd` 继续跑整合。

**本轮验收**：`dotnet build` 0 错 0 警、`--headless --editor --quit` 无 Parse Error、
`run_tests_cs.bat` **2037 条**（下限 1893 → **2027**）、`run_tests.bat` **9870 条**
（基线 9832 不动）`SELFCHECK: OK`、`validate_tables.ps1` **PASSED**、
`run_mutation_sweep.bat` **PASSED**、`check_loop.bat` **OK**。

`模块对接表.csv` 的 `map_overworld`／`recruit_system`／`npc_system` 换 `.cs`；
`验证清单.csv` 挂上三个新 `.cs`；`docs/design/21_角色背景设定.md`／`CHANGELOG.md` 里那两处
指向 `src/core/recruit_service.gd` 的路径换成 `RecruitService.cs`（「带目录前缀的必须指向现在存在的东西」）。

## 二点二零、core 第十三批：`guard_service`（2026-10-10）

`src/core/guard_service.gd`（148 行）→ **`src/core/GuardService.cs`**（旧 `.gd`／`.gd.uid` 已删）。
它是一份**纯静态**的服务（一个类没有一个实例状态），所以搬过来只有一条纪律：
每个 GDScript 调得到的静态方法都保持 `public static`，**参数一个可选值都不留**
（`is_resolved(db, state, scene_id, room_id)`／`pay_peace(db, state, row, kind, target_id)` 都写全）。

调用面 **4 个文件 ＋ 3 处旁注**：`src/world/local_map_controller.gd`（preload ＋ 8 个调用点）、
`tools/CheckLoopRunner.cs`（`load("…/GuardService.cs")` 再调 `peace_flag`）、`tests/test_guard.gd`（preload）、
`src/core/AGENTS.md` 的指针；旁注是 `tools/audit_table_usage.ps1` 的注释、
`tools/analysis_mutations_code.ps1` 的 `peace_flag 改字` cast、`docs/dev/第三方代码.md` 的路径。

## 四条新坑：

51. **`state?.Get("inventory")` 这种写法在 Godot C# 里编不过**：`Variant` 是 **struct**，
   `?.` 的结果是 `Variant?`，后面再 `.AsGodotObject()` 就没有这个方法了。要写「状态可能是
   GDScript 传进来的 null」就老老实实 `if (state != null) { … }`。编译器会当场报错，
   不算隐患，但第一版就是顺手写成 `state?.`，值得记一笔免得下次再省。
52. **字典取值前先 `ContainsKey`**：GDScript 那边到处是 `.get(key, 默认值)`，
   C# 的 `Godot.Collections.Dictionary` **没有**对应写法，`dict[key]` 缺键直接抛。
   本批两处都踩到（`removed["ok"]`、`record["rooms"]`），顺手还发现
   `Variant.AsGodotDictionary()` 对 `null`/nil **返回 null 而不是空表**——所以
   `state.Call("dungeon_record", …)` 之后要判一次 `record == null`，不然是 `NullReferenceException`。
53. **内存夹具：复制表之后还要再复制那一行，再写回 `rows[0]`**。
   `table.duplicate(true)` 在 GDScript 侧够用（用例都那么写），但 C# 侧**吃过不确定的亏**：
   表资源是 Godot 按路径缓存的（`TableDb.load_all()` 两个实例可能拿到同一份 `TableResource`），
   于是夹具改的那一行可能正是别人在用的那一行。写法固定成
   `Duplicate(true)` → 单独 `Duplicate(true)` 那一行 → 改 → `table.rows[0] = row` → `local.tables[...] = table`，
   改的永远是本用例私有的那一份。
54. **`var x := <C# 静态方法>(…)` 是能推的，不能推的只有 `const`**（把第十二批坑㊺的边界写死）：
   `test_guard.gd` 里 `var demand := GuardServiceScript.demand_text(…)` 照旧一行不动，
   parse 无错；上一批挂掉的是 `const OPENING_CHOICE_NODE := <C# 静态调用>`——
   `const` 要的是**编译期常量**，跟类型推断没关系。所以规则收成一句：
   **C# 的静态调用可以出现在 `var :=` 右边，不能出现在 `const :=` 右边。**

**用例**：新增 `tests/TestGuard.cs`（41 条：旗标名绝对值／表侧配对 `reward_group == chest_id`／
没有守卫的房间恒为可开／宝箱键拆房间那半边／物品路扣一份＋点旗标＋放行台词取 `peace_note`／
判定路门槛取表值且「刚好等于算过」、不消耗任何东西／门槛抬到 999 如实不可用／
武取＝写房间记录即解锁）；旧的 `test_guard.gd` 继续跑整合（它现在调的是 C# 服务，
顺带把跨语言那几条调用形状再验一遍）。

**本轮验收**：`dotnet build` 0 错 0 警、`--headless --editor --quit` 无 Parse Error、
`run_tests_cs.bat` **2078 条**（下限 2027 → **2068**）、`run_tests.bat` **9872 条**
（基线 9832 不动）`SELFCHECK: OK`、`validate_tables.ps1` **PASSED**、
`run_mutation_sweep.bat` **PASSED**、`check_loop.bat` **OK**（含「宝箱守卫」那一段：
开口→付账→开箱→旗标进档）。

`验证清单.csv` 的 `dungeon_system` 挂上 `tests/TestGuard.cs`；
`src/core/AGENTS.md`／`tools/audit_table_usage.ps1`／`docs/dev/第三方代码.md` 的指针改 `.cs`；
变异探针的 `peace_flag 改字` cast 改指 `.cs` 并进 `$intentionallyUnpinned`。

## 二点二一、core 第十四批：`menu_controller`（2026-10-10）

`src/core/menu_controller.gd`（157 行）→ **`src/core/MenuController.cs`**（旧 `.gd`／`.gd.uid` 已删）。
这一份是「启动菜单的决策层」——不碰场景树，新建／读档／退出三条路都以「返回一个结果字典」的形状
交给视图执行，所以整条流程能在 headless 下被用例钉住。

**跨语言面只有三处，都是老形状的复用**：
`_db` 收 `TableDb`（已是 C#）；`_store` 收 **`GodotObject`**（`test_menu.gd` 会塞一只 GDScript 的
`ReadOnlyStore` 桩进来验「存档目录不可写也要能开局」，所以全程 `Call`，
桩只需要 `list_slots`／`slot_for_new_game`／`save_slot` 三个口）；
`state` 是 GDScript 的 `GameState`，读写走 `Get`／`Set`／`Call`。
`slots`／`message` 两个**视图要读的实例字段**做成 `[Export]` 公开属性
（`main_menu.gd` 逐格读 `controller.slots`；C# 的普通公开属性不进脚本 API）。
常量给静态小写取值口（`action_play()`／`action_none()`／`action_quit()`／`start_hub()`／
`start_overworld()`），P5 清零后再连着调用方一起删。

**调用面 4 个文件**：`src/ui/main_menu.gd`（preload ＋ 建实例 ＋ 7 处常量读）、
`src/ui/creation_screen.gd`（preload ＋ `create`）、`tests/test_menu.gd`、`tests/test_save_migration.gd`；
旁注是 `src/core/AGENTS.md`（3 处，写的是类名 `MenuController.load_game`，本来就对得上，没动）
与被搬文件自己的抬头注释。

## 三条新坑：

55. **搬走一个类，`docs/dev/模块对接表.csv` 的 `code_paths` 要跟着换 `.cs`**。
   `main_menu` 那行还写着 `src/core/menu_controller.gd`，自检当场报
   「`main_menu` 的 `code_paths` 引用了不存在的文件」——`code_paths` 是给设计侧「去哪找」用的，
   它跟 `验证清单.csv` 的 `verify_paths` 是两处不同的登记，**都要改**。
56. **C# 的日志句要一行写完**：`test_copy_audit` 的「玩家可见文案夹表名」扫描
   **只豁免同一行里带 `PushError`／`push_error` 的语句**。本批把
   `GD.PushError("[MenuController] 开局队伍为空：character_base.csv …（看 recruit_def.is_initial）")`
   拆成三行写，字符串那两行不含 `PushError`，于是被判成「玩家可见文案夹表名」。
   GDScript 版是一行，所以那行本来就豁免——**搬过来时别顺手折行**。
57. **Godot 的 C# 源生成器要求「脚本类名 ＝ 文件名」**：用例里的桩（只读存档目录那只）
   **不能**在 `TestMenu.cs` 里当嵌套类或第二个类，得单独一个 `tests/UnwritableSaveStore.cs`。
   相应地，它也不进「`src/` 下的死代码／测试专用接口」两道门限的扫描面（那两道只扫 `src/`），
   摆在哪都安全。

**顺带实测通的两条跨语言形状**（下次照用）：
① GDScript 传来的 `PackedStringArray` 能直接喂 C# 的 `string[]` 形参
（`MenuController.create(db, store, default_party_ids(db), "normal")`，`main_menu` 与用例共 6 处）；
② C# 里 `store.Call("list_slots", db)` / `store.Call("save_slot", slot, state)` 调另一份 C# 脚本
（`SaveStore`）的 snake_case 方法，与调 GDScript 桩是同一套形状，都能用。

**用例**：新增 `tests/TestMenu.cs`（53 个静态点／57 条断言：默认槽位数 6 的绝对值／
新建落在第一个空槽且立刻落盘／队伍＝`recruit_def.is_initial`／默认名用出身卡真名（陆文昭）／
槽位文案带格号与等级／读档回大地图且带回 `world_pos`／读不存在的槽给原因／退出动作／
空配置表时拒绝开工且文案指向配置表／存档只读仍能进游戏只是提示不保留／损坏档不进游戏也不给半成品状态／
三格全满覆盖**最旧**那一格）。真实场景点按钮那条（`main_menu`／`creation_screen`／占位场景）
仍在 `tests/test_menu.gd` 里跑——那半边要 `SceneTree` 注入，等 P3 搬 UI 时一起走。

**本轮验收**：`dotnet build` 0 错 0 警、`--headless --editor --quit` 无 Parse Error、
`run_tests_cs.bat` **2135 条**（下限 2068 → **2125**）、`run_tests.bat` **9880 条**
（基线 9832 不动）`SELFCHECK: OK`、`validate_tables.ps1` **PASSED**、
`run_mutation_sweep.bat` **PASSED**、`check_loop.bat` **OK**
（含「主菜单新建游戏（经创建界面）」那一段：ok=true 落盘=true）。

`验证清单.csv` 的 `main_menu` 挂上 `tests/TestMenu.cs`；`模块对接表.csv` 的 `code_paths`、
`docs/dev/框架说明.md` 与 `docs/design/21_角色背景设定.md` 的指针改 `.cs`。
变异探针里**没有** `menu_controller` 相关的 cast（`ACTION_*`／`START_*` 那几条常量不被任何 cast 引用），
所以这一批不动探针。

## 二点二二、core 第十五批：`guide_service`（2026-10-10）

`src/core/guide_service.gd`（173 行）→ **`src/core/GuideService.cs`**（旧 `.gd`／`.gd.uid` 已删）。
开局引导：读 `guide_step` 算「当前目标」，并自己点亮两条**只能算出来**的旗标
（`flag_supplies_ready`／`flag_arrived_heifengzhai`）。七个常量
（`START_CONDITION`／`ORIGIN_PREFIX`／`ITEM_PREFIX`／`SUPPLIES_FLAG`／`SUPPLIES_LEVEL`／
`ARRIVED_HEIFENGZHAI_FLAG`／`HEIFENGZHAI_SCENE`）各配一个小写静态取值口。

**调用面 11 个文件**（这回比前几批都宽）：GDScript 侧 `src/core/clue_service.gd`／
`src/core/npc_service.gd`／`src/world/local_map_controller.gd`／`src/world/overworld_controller.gd`
＋ 4 份用例（`test_guide`／`test_story`／`test_dialogue`／`test_local_map`）＋ `tools/CheckLoopRunner.cs`；
**C# 侧还有 5 处「借它判条件」的只读通道**（见下）。旁注是 `src/core/AGENTS.md`、
`docs/dev/第三方代码.md`、`docs/dev/模块对接表.csv` 的 `code_paths`。

## 四条新坑：

58. **借了别处的服务，搬完要把那条 `GD.Load&lt;GDScript&gt;(…)` 通道拆掉**。本批一次拆了 **5 处**：
   `ChapterService`／`StoryService`／`DialogueService`／`RecruitService`／`WorldEventService`
   都有一条「条件判定还在 `GuideService`（GDScript）里」的跨语言只读通道，各自还留着一个
   `GUIDE_SCRIPT` 常量。搬过来之后直接 `GuideService.condition_met(state, condition)`，
   常量一并删掉——**留着不删，下一个人会以为它还依赖 GDScript**（`rg "guide_service.gd"` 一搜就有）。
   顺带把三处注释里的「还没搬走」改掉。
59. **C# 返回 `string[]`，GDScript 侧可以当 `PackedStringArray` 收**：
   `var set_now: PackedStringArray = GuideServiceScript.refresh_derived_flags(db, state)`
   **带类型标注也照样过**（`.has()`／`.is_empty()` 都在）。这是 `TableRegistry.tables()`／
   `SetService.active_buff_ids()` 那套形状的再用一次，不用为了跨语言改成 `Array`。
60. **`String.Split` 有两套口径，别混**：GDScript 的 `split(delim, allow_empty=false)` ＝
   C# 的 `Split('&', StringSplitOptions.RemoveEmptyEntries)`；`allow_empty=true`（默认）＝
   `Split(':')`。混了就会把「尾部空段＝写坏了」这条纪律悄悄放掉——`!flag_a|` 那条断言
   （`test_dialogue`／`TestGuide` 各一条）正好钉着它。另：`int("12abc")`＝12 这种 GDScript 口径
   C# 侧没有现成对应，`GuideService._to_int()` 自己实现了一遍并写明理由（`item:` 的 `:数量` 用它）。
61. **探针 cast 改指 `.cs` 之后，绝对值要在 C# 用例里补上**：`START_CONDITION 改字` 那条跟着
   `File`／`Find`／`Repl` 换成了 C# 写法并登记进 `$intentionallyUnpinned`（改 `.cs` 不重编 → 量不出来），
   所以 `TestGuide.cs` 把**七个常量**全写成绝对值断言，等于把那条网挪进用例里。

**用例**：新增 `tests/TestGuide.cs`（44 个静态点／67 条断言：七个常量绝对值／六步表结构与
`sort_order` 连续／新档停在第一步、无关旗标不推进、逐个点亮逐步推进、清掉最后一步退回上一步／
HUD 文案取表原文＋进度分母＋不出现 `flag_` 形态 id／条件语言的边界九条＋`origin:` 三条＋
`item:` 五条）。旧的 `test_guide.gd` 留着跑跨语言形状（现在调的是 C# 服务）。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests_cs.bat` **2202 条**（下限 2125 → **2192**）、
`run_tests.bat` **9892 条**（基线 9832 ⇒ **9882**，其中 6 条是本轮补的兜底常量断言）`SELFCHECK: OK`、
`validate_tables.ps1` **PASSED**、
`check_loop.bat` **OK**（第 ⑩ 段那行 HUD 文案就是 C# `hud_text()` 生成的：
「当前目标：到客栈找燕小七同行。一个人上山，是送死。（2/6）」）。

`验证清单.csv` 的 `guide_system` 挂上 `tests/TestGuide.cs`；`模块对接表.csv` 的 `code_paths`、
`src/core/AGENTS.md`、`docs/dev/第三方代码.md` 的指针改 `.cs`。

### 顺手补的洞（同轮）：变异探针「代码常量」六条 `[missed]`

这一批额外跑了手动档 `run_mutation_sweep.bat code`（2026-10-10，约 40 分钟），结果**六条过不了**，
全是同一类，跟本批的搬运无关但必须记下来：

```
CODE MUTATIONS MISSED: POISE_BREAK_DAMAGE_BONUS 0.5->0.7, PARRY_DAMAGE_MULT 0.5->0.7,
  PARRY_POISE_GAIN 0.15->0.25, FLEE_BASE_CHANCE 0.5->0.3, FLEE_MIN_CHANCE 0.25->0.4,
  FLEE_MAX_CHANCE 0.95->0.85
```

**根因**：0.32.0（Q85）把这七个战斗常数搬进 `combat_const` 表之后，`battle_simulator.gd` 里
那几份只剩「表里没这行时的兜底」（`_combat_const(id, fallback)`），而**所有用例都在有表的进程里跑**——
改坏兜底不产生任何行为差异，于是那六条 cast 从"钉住"退化成了"没人钉"。
（第七条 `DEFEND_POISE_GAIN` 没事，因为 `test_battle.gd` 早有一条直接读常量的绝对值断言。）

**处置**（照 AGENTS 的老规矩：**先补绝对值断言，再加 cast**，而不是往 `$intentionallyUnpinned` 里一塞了事）：
`tests/test_battle.gd::_check_absolute_constants` 补上六条 `check_float(...)`，兜底值写死；
顺手把 `test_poise.gd` 里那句「`combat_const` 还没这一行」的过期注脚改成现状。
**逐条复验**：把六个常量各自改坏跑一遍 `run_tests.bat`，六次都红在对应那条断言上、
文件按字节还原（SHA256 一致）⇒ 探针那六条 cast 重新可量。
代价是 GDScript 侧的断言基线要动：`tools/run_tests.gd::MIN_ASSERTIONS` 9832 ⇒ **9882**（实测 9892）。

## 二点二三、core 第十六批：`skill_grant`（2026-10-11）

`src/core/skill_grant.gd`（224 行）→ **`src/core/SkillGrant.cs`**（旧 `.gd`／`.gd.uid` 已删）。
武学「来源 → 学会」的解析：按 `skill_base.source_type`＋`source_id` 发武学
（`drop`／`hidden`／`item`／`story`／`origin` 五类已接，`npc`／`shop` 未接），带修习门槛、
结算文案与秘籍研读。`IMPLEMENTED_SOURCES` 是**普通 `string[]` 静态字段**（静态字段不许放 Godot
集合，坑⑧），另配小写取值口 `implemented_sources()`（返回副本）——GDScript 读不到 C# 常量。

**调用面**：GDScript 侧 `src/world/local_map_controller.gd`／`src/ui/battle_screen.gd`／
`src/ui/character_screen.gd` ＋ 两份用例（`test_handshake`／`test_skill_grant`）的 preload 换 `.cs`；
`tests/test_handshake_flags.gd` 的 `SkillGrantScript.IMPLEMENTED_SOURCES.has(source)` 改调
`implemented_sources().has(source)`。**C# 侧 `StoryService`** 那条
`GD.Load<GDScript>("…/skill_grant.gd").Call("grant_from_source", …)` 拆掉直调
`SkillGrant.grant_from_source(db, state, …)`，净少一条跨语言只读通道（坑58 的又一次复用）。

**用例**：新增 `tests/TestSkillGrant.cs`（54 个静态点／60 条断言：来源名单五类绝对值／
按来源查与按敌人查是同一件事／击败领悟与去重／门槛挡住不写进已学但说清原因／隐藏点位／
秘籍研读·门槛不够不消耗）。旧的 `test_skill_grant.gd` 留着跑跨语言形状（真实战斗闭环那半边要场景树）。
`tests/TestCase.cs` 新增 `TableWithTalents(wu, gen)` 内存夹具（对位 `test_case.gd::table_with_talents`）。

### 三条新坑：

62. **内存夹具复制一行 `Resource`：`Duplicate()` 返回的是 `Resource`，要显式转回具体行类**。
   `custom.tables["character_base"].AsGodotObject().Duplicate(true)` 编不过——`GodotObject`
   没有 `Duplicate`；行元素也不能 `entry.AsGodotObject()` 再转，得 `entry.As<TableRow>()` 再
   `(CharacterBaseRow)…Duplicate(true)`。坑53 的「复制表→复制行→写回」形状不变，只是类型口要收对。
63. **别把 C# 的 `string[]` 收进 `Godot.Collections.Array`**：`SkillGrant.summarize()` 返回
   `string[]`（对位 GDScript 的 `Array`），用例里写成 `Godot.Collections.Array lines = …` 直接
   CS0029；改用 `string[]`（`.Length`／`lines[0].StartsWith(...)` 都在）。这条与坑59 是同一件事
   的两面：**跨语言面认 `PackedStringArray`／`string[]`，C# 内部别绕回 `Godot.Collections.Array`**。
64. **「同一 id 传两遍去重」的用例要用两份干净存档**：`grant_from_defeated` 会**顺手把武学写进已学**，
   拿同一份 `state` 先跑 `once` 再跑 `twice`，第二次会因为「已学过」返回空、断言假红——这是用例写法问题、
   不是代码问题，第一版就是这么红的。两边各 `SoloState(db)` 一份即可。

**本轮验收**：`dotnet build` 0 错 0 警、`--editor --quit` 只剩图形无关的证书报错、
`run_tests_cs.bat` **2262 条**（下限 2192 → **2252**）、`run_tests.bat` **9892 条**（基线 9882 不动）、
`validate_tables.ps1` **PASSED**。

`验证清单.csv` 的 `skill_system` 挂上 `tests/TestSkillGrant.cs`；`src/core/AGENTS.md` 的
`skill_grant.gd` 指针改 `.cs`；`tools/validate_tables.ps1` 注释里的 `SkillGrant.IMPLEMENTED_SOURCES`
改 `implemented_sources()`。另：`tests/test_handshake.gd` 的 `TEST_ONLY_API_ALLOWED` 里
`"skills_from_enemy": "查询"` 条目已删——**如果留着，C# 正则不认 `public static`（坑⑦）**，
搬成 C# 后那道「新 API 无人调用」门限本来就扫不到它；P5 补正则时要把白名单条目（连同理由）
一起加回来。

## 二点二四、core 第十七批：`enemy_factory`（2026-10-11）

`src/core/enemy_factory.gd`（218 行）→ **`src/core/EnemyFactory.cs`**（旧 `.gd`／`.gd.uid` 已删）。
敌人实例化：`enemy_base` × `difficulty_config` → `BattleActor`；七维＋装备／内功贡献 →
`AttributeCalculator.compute(…, include_level_base: false)`，空七维退回旧派生列并出声点名；
`enemy_skill` 配表驱动招式、`enemy_team` 解析整队与掉落组。

**调用面 15 个 `.gd` 文件（~40 处）**（本批是目前最宽的一次）：`src/ui/battle_screen.gd`、
两个平衡／性能分析工具、12 份用例。三处机械改动：`preload("…enemy_factory.gd")` → `…/EnemyFactory.cs`、
`Xxx.new(db)` → `Xxx.create(db)`、**实例方法 `create(enemy_id, difficulty_id)` → `create_enemy(...)`**
（原因见坑65）。`tests/test_handshake.gd` 的 `TEST_ONLY_API_ALLOWED` 里 `team_drop_groups`／`threat_tags`
两条**保持不变**：它们本来就登记成「只被测试调用」的查询口，搬成 C# 后仍是 public、仍无游戏路径调用方。

**C# 侧**：`TableValidatorStructure` 删掉 `ENEMY_FACTORY_SCRIPT` 常量与 `ScriptConstants(...)["AI_TEMPLATES"]`
的跨语言读法，直调 `EnemyFactory.ai_templates()`（又少一条过渡通道）；`TableValidator.ScriptConstants`
本身留给 `battle_actor.gd`／`world_map_service.gd` 那两处还没搬的。
`enemy_contributions` 从 `_enemy_contributions` 开成 public（坑㊻：GDScript 读不到 C# 的 `private`，
`test_enemy_template.gd` 要直接调它）。

### 三条新坑：

65. **静态工厂不能与实例方法同名**——这条是本批最贵的一个坑。Godot 生成的 `GetGodotMethodList()`
   里两条 `create` 各占一行（静态那条带 `MethodFlags.Static`），但 GDScript 静态调用是按**名字**找的，
   实例那条会盖住静态那条，于是 `battle_screen.gd:840` 解析 `EnemyFactoryScript.create(db)` 直接报
   `Parse Error: Static function "create()" not found in base "CSharpScript"`，**整个脚本加载失败**
   （`--headless --editor --quit` 一次就打出来）。修法：静态工厂保住 `create(db)`（与其余已搬的类一致），
   实例那条改名 `create_enemy(enemy_id, difficulty_id)`。以后凡是「既要 `create(db)` 工厂、
   又要有 `create(...)` 实例方法」的类，照这条提前避让。
66. **`Godot.PackedStringArray` 在 4.7 的 C# 里不存在**：GodotSharp 4.7.1 里只有 native 的
   `godot_packed_string_array`，托管侧 packed 数组就是 `string[]`（行类的 `string[]` 导出列就是这么桥的）。
   要往 GDScript 传 PackedStringArray，直接给 `string[]`（`Variant` 隐式转）；写
   `new PackedStringArray(skills)` 会 CS0246。
67. **`Call("名字")` 只认方法，读属性要用 `Get("名字")`**：`actor.Call("reward_exp")` 不报错、
   返回 nil（`AsInt32()` 得 0），断言就成了「经验奖励按难度倍率算（实际 0，期望 29）」这种假红。
   `actor_id`／`tags`／`resistances` 是 `var` → `Get`／`Set`；`stat()`／`max_hp()`／`resistance_of()`
   是方法 → `Call`。

**用例**：新增 `tests/TestEnemyTemplate.cs`（36 个静态点／40 条断言：发行数据 17 行七维全配齐、
空七维回退血量＝派生列＋装备、七维齐了与 `AttributeCalculator` 逐项一致、内功池 > 0、
装备／内功加成进管线（柴刀↔布衣对比、五毒心法 0.15 毒抗）、内功超编白名单正好两个
＋夹具改占格后构建期点名 `en_bd_butcher`、`attr_map()` 的全 0 语义、
工厂口径（九种 AI 模板绝对值／招式按 `sort_order`／难度倍率与经验奖励／整队编号／掉落组去重／威胁标示））。
旧的 `test_enemy_template.gd` 留着跑跨语言形状（现在造的是 C# 工厂、拿的仍是 GDScript `BattleActor`）。

**本轮验收**：`dotnet build` 0 错 0 警、`--editor --quit` 无 Parse Error、
`run_tests_cs.bat` **2302 条**（下限 2252 → **2292**）、`run_tests.bat` **9894 条**
（基线 9882 ⇒ **9884**）、`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_mutation_sweep.bat` **PASSED**。

`验证清单.csv` 的 `enemy_data` 挂上 `tests/TestEnemyTemplate.cs`；`模块对接表.csv` 的 `code_paths`、
`docs/dev/框架说明.md` 的 `enemy_factory.gd` 指针改 `.cs`；`docs/design/10_敌人与技能配表.md` 那句
「现状」里的 `src/core/enemy_factory.gd` 改成不带目录前缀的写法——那道「文档引用的路径必须存在」的
门限会抓（它盯的是**现在**，历史叙述写路径会被判成过期指针）。

## 二点二五、core 第十八批：`creation_service`（2026-10-11）

`src/core/creation_service.gd`（253 行）→ **`src/core/CreationService.cs`**（旧 `.gd`／`.gd.uid` 已删）。
创建角色（设计 13）：出身卡、自建七维规则、武器与起始武学选项、`validate()`、`build()`。
`CUSTOM_CHAR_ID` 配取值口 `custom_char_id()`（GDScript 读不到 C# 常量，坑⑩）。

**调用面**：`src/ui/creation_screen.gd`（11 处）、`tests/test_creation.gd`、`tests/test_talent.gd`
（那份语料清单里的路径要跟着换，**不换的话 `start_money` 会被判成"没人读的 rule"**）；
**C# 侧 `MenuController` 拆掉三条 `GD.Load<GDScript>(…)` 通道**——`build`／`origins`／`default_name_of`
全改直调，`CREATION_SCRIPT` 常量一并删掉（这已经是本迁移里第 N 次「借了别处的服务搬完要拆通道」，坑58）。

### 三条新坑：

68. **`Array(字典.get(key, []))` 在 C# 里要自己收口径**：GDScript 的 `Array(...)` 收 `Array` 与
   `PackedStringArray`，遇上 `null` 会报错；C# 侧不能直接 `AsGodotArray()`（类型不对会抛）。
   `CreationService._array()` 只放行这两种真数组、其余（含缺键）给空表——`talents` 缺省与
   `PackedStringArray` 两种形状都要过（夹具里就传过 `PackedStringArray`）。
69. **`Godot.Collections.Dictionary` 不收 C# 的 `null`**（坑⑥ 的再一次）：`build()` 失败时
   GDScript 写的是 `{"state": null}`，C# 侧要写 `{ "state", default(Variant) }`，
   调用方 `creation["state"].AsGodotObject()` 才拿得到 null。
70. **`Godot.Collections.Dictionary` 的索引是真引用，就地改就够**：`state.Get("talent_picks")`
   拿到的字典与 `state` 内部是**同一份**，`picks[char_id] = …` 不需要再 `Set` 回去
   （`actor.Get("tags")` 那条同形）。反过来也别多写一次 `Set`，那会在读档序列化上留下两份来源。

**用例**：新增 `tests/TestCreation.cs`（57 个静态点／61 条断言：五张出身卡与七维合计／默认名＝真名／
七维规则与武器数／`validate()` 八条边界（含「还没选出身」是人话）／模板路建库＋主角不再作为同伴出现
＋改名进档与读档往返／自建路模板注入＋起始装备＋面板认得／天赋预算与 `rule:` 归位＋存档往返）。
旧 `test_creation.gd` 留着跑界面那两节（出身卡第二行、确认段汇总要真场景）。

**本轮验收**：`dotnet build` 0 错 0 警、`--editor --quit` 无 Parse Error、
`run_tests_cs.bat` **2363 条**（下限 2292 → **2353**）、`run_tests.bat` **9896 条**
（基线 9884 ⇒ **9886**）、`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_mutation_sweep.bat` **PASSED**。

`验证清单.csv` 的 `char_template` 挂上 `tests/TestCreation.cs`；`模块对接表.csv` 该行的
`code_paths` 只有三个行类、本来就没写 `creation_service.gd`，所以不动。

## 二点二六、core 第十九批：`dungeon_service`（2026-10-11）

`src/core/dungeon_service.gd`（253 行）→ **`src/core/DungeonService.cs`**（旧 `.gd`／`.gd.uid` 已删）。
副本完成度四项（宝箱／隐藏房间／隐藏 Boss／事件）与「已通关层扫荡」（只结算掉落、不给经验）。
带参构造改静态工厂 `create(db, state, rng)`；`HIDDEN_BOSS_THREAT` 只在这一个文件里用，
**不配取值口**（游戏路径里没人读它，配了反而会被"只被测试调用的接口"那道门限追）。

**调用面**：`src/world/local_map_controller.gd`、`src/ui/dungeon_screen.gd` 的 preload 换 `.cs`、
`.new(` → `.create(`；`tools/analysis_perf.gd` 那处两参 `.new(wide, state)` 补成
`.create(wide, state, null)`（坑②：GDScript 调得到的 public 静态工厂一个可选参数都不留）；
`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `test_dungeon.gd`，`tests/TestDungeon.cs` 进 C# 清单
（`tools/RunTestsCs.cs` 的 `Suites`）。

### 沿用／新踩的坑：

71. **同层房间要保持表序**：GDScript `sort_custom` 只按 `floor` 排，同层顺序不保证稳定；C# 侧改成
   「floor 升序 + 原始下标兜底」的稳定排序，`floors()` 的行序才确定（坑⑦确定性那条的同一族）。
72. **`PartyBuilder` 还是 GDScript**：C# 里借它的静态方法走
   `GD.Load<GDScript>("res://src/core/party_builder.gd").Call("build_actors", db, state, new Array())`
   ——默认参数 `field_buffs` 要显式补上空表（与 `TestDropResolver` 里那条同形）。

**用例**：新增 `tests/TestDungeon.cs`（40 个静态点／40 条断言：完成度四项分母跟着表走／
只记一半按比例算＋重复记录不叠加＋补齐到 100%／层视图与可扫荡层／未通关层拒绝扫荡＋同一层可重复扫／
完成度与保底往返存档／出口成对声明）。`BattleReward` 的溢出计数那两节已经在 `TestBattleReward.cs`
里钉过，这里不重复搬。

**本轮验收**：`dotnet build` 0 错 0 警、`--editor --quit` 无 Parse Error、
`run_tests_cs.bat` **2403 条**（下限 2353 → **2393**）、`run_tests.bat` **9843 条**
（基线 9889 ⇒ **9833**——比上轮低是因为 `test_dungeon.gd` 那批断言搬进了 C# 链，
**基线只会跟着搬动后的真实值走**）、`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_mutation_sweep.bat` **PASSED**。

`验证清单.csv` 的 `dungeon_system` 挂上 `tests/TestDungeon.cs`；`模块对接表.csv` 的
`code_paths`／`docs/dev/框架说明.md`／`src/ui/dungeon_screen.gd` 注释里的 `.gd` 指针改 `.cs`。

## 二点二七、core 第二十批：`skill_loadout`（2026-10-11）

`src/core/skill_loadout.gd`（319 行）→ **`src/core/SkillLoadout.cs`**（旧 `.gd`／`.gd.uid` 已删）。
武学「学（修习门槛）＋ 装（招式槽／内功容量）」：槽位与容量按表算、内功占格、修习门槛、
武器类型限制、装配／卸下／切换／`fill_from_learned`、战斗招式与内功贡献。带参构造改静态工厂
`create(db, state, member_id)`。

**调用面**：`character_sheet.gd`／`game_state.gd`（各一处构造 ＋ `fill_from_learned` 两处）
＋四份用例（`test_character_sheet`／`test_end_to_end`／`test_skill_grant`／`test_story` 的 preload
与 `.new(` → `.create(`；`test_talent` 那份 `rule:` 语料清单从 `.gd` 换 `.cs`）。
**C# 侧还拆了一条跨语言通道**：`SkillGrant` 原来用 `GD.Load<GDScript>(…).New(...)` 现建装配对象
（坑58「借了别处的服务搬完要拆通道」），这次改成直调 `SkillLoadout.create(...)`，
`SKILL_LOADOUT_SCRIPT` 常量删掉。

### 三条新坑：

73. **Godot 4.7 的 C# `Callable` 没有 `IsValid()`／`IsNull()`**（反射确认过公开面只有
   `target`／`Method`／`Delegate`／`Call`）：所以「面板有没有接属性提供口」只能按
   `target != null && !Method.IsEmpty` 判——默认构造的 `Callable()` 两者都空。
   `[Export] public Callable attrs_provider { get; set; }` 是给 GDScript 写的口（坑⑩／
   `MenuController.slots` 同形）。
74. **`Godot.Collections.Dictionary.Equals` 是 native 引用比，不是按值**：GDScript 那份
   `check_eq(dice_a, dice_b)` 是深比，C# 里照抄会假红。`TestSkillLoadout._CheckSameDict` 逐键比。
75. **门限会随迁移「过期」**：`test_handshake_code::_check_preload_for_global_class_calls` 原来要求
   `src/**/*.gd` 里扫到 **> 20** 个 `class_name`——搬走一个就少一个，这一批到 20 就假红了。
   改成 **> 5** 并在注释里写明「P5 全搬完这条要删」（**它量的对象整个消失了**）。

**用例**：新增 `tests/TestSkillLoadout.cs`（73 个静态点／73 条断言：开局装配与槽位公式／
★3 门槛要悟性 12＋夹具给资质后才学得会／武器类型限制（卸下武器后剑招装不上、通用照装）／
招式槽满拒第 4 个且卸一个又能装／内功占格与容量／内功装备即生效（内力上限掉 30 又还原、裸属性不变）／
清空的槽位不被迁移重填＋存档往返／★5 内功的悟性 +5 不许反过来涨招式槽）。

**本轮验收**：`dotnet build` 0 错 0 警、`--editor --quit` 无 Parse Error、
`run_tests_cs.bat` **2476 条**（下限 2393 → **2466**）、`run_tests.bat` **9772 条**
（基线 9833 ⇒ **9762**，少的是搬进 C# 链的那批断言）、`validate_tables.ps1` **PASSED**、
`check_loop.bat` **OK**、`run_mutation_sweep.bat` **PASSED**。

`验证清单.csv` 的 `skill_system` 挂上 `tests/TestSkillLoadout.cs`；`模块对接表.csv` 与
`src/core/AGENTS.md` 的 `.gd` 指针改 `.cs`。

## 二点二八、core 第二十一批：`inventory`（2026-10-11）

`src/core/inventory.gd`（395 行）→ **`src/core/Inventory.cs`**（旧 `.gd`／`.gd.uid` 已删）。
背包与装备：物品堆叠与容量、装备实例（`基础 id#序号`）、按 `equip_slot_def` 定长的穿戴映射、
装备贡献列表（属性点／派生值／常驻 buff）、`to_dict`／`from_dict`。
**形状**：`stacks`／`equipment`／`equipped`／`money`／`next_uid` 是 GDScript 直接读写的字段
（`state.inventory.money = 1000`、`state.inventory.stacks.erase(...)`），所以都是
`[Export] public`；带参构造没有，读档走静态工厂 `Inventory.from_dict(data)`。

**调用面（这一批的主要工作量）**：带默认参数的方法**一个都不留**（坑②），所以全仓库补实参——
`add_equipment(a, b)` → `add_equipment(a, b, [])` 共 **16 个文件**，`has(x)` → `has(x, 1)`
（`check_loop`／`local_map_controller`／`battle_screen`／`test_shop`／`npc_service`…）；
`game_state.gd`／`test_buff.gd` 的 preload 换 `.cs`。
**C# 侧拆掉三条跨语言通道**（坑58）：`BattleReward`／`EnemyFactory` 删 `INVENTORY_SCRIPT` 改直调
（`new Inventory()`、`Inventory.equipment_contributions(...)`），`SkillGrant` 改
`state.Get("inventory").As<Inventory>().consume(...)`。

**用例**：新增 `tests/TestInventory.cs`（89 个静态点／89 条断言：堆叠与溢出拒收、装备实例与穿戴规则、
槽位定长与改表退回、贡献列表（属性点／派生／常驻 buff）、每角色隔离、存档往返、穿戴不变量），
`tools/RunTestsCs.cs` 的 `Suites` 加它；`tools/run_tests.gd` 的 `TEST_SCRIPTS` 摘掉
`test_inventory.gd`（旧 `.gd`／`.uid` 已删）。`TestCase.cs` 顺手补了 C# 夹具
`PartyState(db, count, difficulty)`／`TableWithNChars(count, smartest, longNames)`（对位 GDScript 那份，
都是**改内存副本**的写法）。
`test_handshake` 的存档字段门限因此要能读 C#：`SAVE_SERIALIZATION_TARGETS` 给 `Inventory.cs`
加 `"csharp": true`，`_check_save_fields_are_serialized` 分语言走两套扫法（见坑78）。

### 四条新坑：

76. **C# 的 `Godot.Collections.Dictionary` 缺键索引会抛，不返回 null**：GDScript 的
   `dict["x"]` 在没这个键时给 `null`，C# 的索引器直接 `KeyNotFoundException`——
   `TestInventory` 里读「没加点角色的 `allocations_of(b)["str"]`」当场炸，
   改成 `ContainsKey` 兜底。**同族**：`Godot.Collections.Dictionary` 没有 `IsEmpty()`，用 `.Count == 0`。
   （照抄 GDScript 的取值习惯会一路踩，P2 的 world／ui 要留意。）
77. **带默认参数的方法搬成 C# 之后，调用点审计要按方法名逐个 `rg` 两遍**：这一批漏的不是业务逻辑，
   而是调用面——① **C# 内部**的 `inventory.Call("has", id)`（`GuardService`／`SkillGrant`），
   ② GDScript 里的 `state.inventory.has(x)`（4 个文件）。`Call` 与 GDScript 都不吃 C# 的默认参数，
   运行期才报 `Nonexistent function 'has' in base 'RefCounted (Inventory.cs)'`。
   纪律：搬完先 `rg -n 'Call\("方法名"' src tests tools`，**再** `rg -n '\.方法名\('`，
   两边都补完才算完；全量自检的日志错误门限会替你把漏的那条揪出来（但只在那条路径真跑到时）。
78. **`test_handshake` 的存档字段门限要跟着换语言**：它原来只认 `func to_dict` ＋ `(?m)^var x[:=]`
   ＋ `"k":`。目标文件换成 `.cs` 之后，加 `_to_dict_body_cs`（按「返回类型 ＋ `to_dict()`」定位声明、
   再配平花括号——直接 `find("to_dict")` 会先撞上类注释里的那个词）＋ C# 字段正则
   `^\s*(?:\[Export\]\s*)?public\s+[\w.<>\[\]]+\s+([a-z_0-9]+)\s*\{` ＋ 键正则 `"([a-z_0-9]+)"\s*,`。
   这类「扫源码文本」的门限每搬一个文件都要问一次「它还读得懂吗」。
79. **本机 PowerShell 7.6 的 `Hashtable.Count`／`.Keys` 会返回错东西**（实测：517 个文件的语料表
   `Count` 报 561、`@($h.Keys)[0]` 是 `Int32`，而 `GetEnumerator()` 里键值都对）——**批量统计一律用
   `GetEnumerator()`**，别信 `.Count`／`.Keys`。这条是下一段事故的救援工具。

### 顺带记一次事故与还原（2026-10-11）

上一步的批量替换脚本把 **4 个文件**里的**全小写 `s` 换成了 `t`**：
`src/ui/battle_screen.gd`（`extends`→`extende`、`res://src`→`ree://erc`）、
`src/world/local_map_controller.gd`、`tools/CheckLoopRunner.cs`、`tests/test_shop.gd`——
4 个文件的小写 `s` 计数全为 0（引擎报 `Could not resolve script`／`Unexpected identifier "extende"`）。
**还原办法**（都在临时目录里做，事后删）：① 取 `git show HEAD:<path>` 的原文；
② 对原文做同样的 `s → t`；③ 与损坏文件 `git diff --no-index`——差集就是"这一批至今的合法改动"
（只有几十行）；④ 把差集里每一行按「token 词表反查」解码（语料表 = 全仓库未被损坏的文件，
候选按出现频次排序，实测全部唯一或高置信：`conee`→`const`、`ree://erc`→`res://src`、`.ce`→`.cs`…）；
⑤ 重建 = HEAD 原文（未改动行逐字照抄）＋ 解码后的改动行。**还原后 4 个文件的字符数与损坏前完全一致**，
`--editor --quit` 无 Parse Error、GDScript 链全绿。
教训：**别用「整文件替换」的批量脚本**；非要用就先 `git stash`／先复制副本，并且**替换后立刻
`rg` 一次特征词**（`extende`／`ree://`／`conee ` 这种一眼假的东西）。

**本轮验收**：`dotnet build` 0 错 0 警、`--editor --quit` 无 Parse Error、
`run_tests_cs.bat` **2565 条**（下限 2466 → **2555**）、`run_tests.bat` **9682 条**
（基线 9762 ⇒ **9672**，少的就是搬进 C# 链的那批断言）、`validate_tables.ps1` **PASSED**、
`check_loop.bat` **OK**、`run_mutation_sweep.bat` **PASSED**（passed steps 2）。

`验证清单.csv`／`模块对接表.csv` 的 `inventory.gd`／`test_inventory.gd` 指针已换 `.cs`；
下一批是 **`character_sheet.gd`（432 行，汇总口，最后搬）**，之后进 P3 `src/world`。

## 二点二九、core 第二十二批：`character_sheet`（2026-10-11）

`src/core/character_sheet.gd`（432 行）→ **`src/core/CharacterSheet.cs`**（旧 `.gd`／`.gd.uid` 已删）。
角色面板的数据集合（这一批是 core 的**汇总口**，最后搬）：五维合成、派生数值、判定值、加点、
武学／装备／行囊视图、图鉴奖励、装备词条文案。
**形状**：`party_builder.gd` 与 `character_screen.gd` 直接读 `char_id`／`template`／`level` 三个字段，
所以是 `[Export] public`；带参构造没有，改走静态工厂 **`CharacterSheet.create(db, state, char_id)`**；
装配口 `_loadout.attrs_provider = new Callable(this, "loadout_attrs")`；
`EVENT_SKILL_ATTR_DIVISOR`／`STAT_GROUPS` 是公开常量。

**调用面（这一批的主要工作量）**：16 个 GDScript 文件机械换——`preload("…/CharacterSheet.cs")` ＋
`.new(` → `.create(`（`character_screen`／`party_builder`／`event_check_service` ＋ 13 份用例）；
4 个 C# 用例（`TestCreation`／`TestInventory`／`TestSkillGrant`／`TestSkillLoadout`）改成直用类型；
跨语言面只剩 `GameState`（走 `Get`／`Call`）。
**用例**：新增 `tests/TestCharacterSheet.cs`（84 条断言，对位原 291 行那份），
`tools/RunTestsCs.cs` 的 `Suites` 加它；`tools/run_tests.gd` 的 `TEST_SCRIPTS` 摘掉
`test_character_sheet.gd`（旧 `.gd`／`.uid` 已删）；`验证清单.csv`（2 处）／`模块对接表.csv`（5 处）
指针换 `.cs`，`test_talent.gd` 的 `rule:` 语料清单也跟着换。

### 两条新坑：

80. **`TableDb.inject_row` 只保护「别的实例」，不保护自己（跨用例污染）**：`CreationService.build`
   走「改名的模板角色」那条路会 `inject_row("character_base", row)`——`inject_row` 内部
   先把表 `Duplicate(true)` 再写回**本实例**的 `tables[名]`，所以**共用同一个 `TableDb` 的用例**
   会被先跑的用例改到（`TestMenu`／`TestCreation` 先跑，`TestCharacterSheet` 后读到的书生模板
   已是玩家真名「陆文昭」、`character_base` 还多一行）。**谁 `new_game({})`／`build(...)` 谁就得用
   `TestCase.FreshDb()`（对位 GDScript 的 `fresh_db()`）**，别用共享的 `GetDb()`。
81. **`:=` 推不出 C# 方法的返回类型（第 16／20 条之后第三次）**：`CharacterSheet` 成为 C# 对象后，
   `character_screen.gd`（6 处）与 `test_affix.gd`（2 处）里 `var x := sheet().stat_value(...)` 这类
   全 Parse Error。排查仍是先跑 `--editor --quit` 一次打全；修法是显式标注（`: float`／`: int`／
   `: bool`／`: String`）。**P3 搬 `src/world`／`src/ui` 时要成批留意**。

**本轮验收**：`dotnet build` 0 错 0 警、`--editor --quit` 无 Parse Error、
`run_tests_cs.bat` **2649 条**（下限 2555 → **2639**）、`run_tests.bat` **9598 条**
（基线 9672 ⇒ **9588**，少的就是搬进 C# 链的那批断言）、`validate_tables.ps1` **PASSED**、
`check_loop.bat` **OK**、`run_mutation_sweep.bat` **PASSED**（passed steps 2）、
`run_all_checks.bat` **18/19**（余一条 `leaked-process` 是 MCP 编辑器实例，假红）。

下一批进 **P3 `src/world`**：`local_map_controller.gd`（~2790 行）→ `overworld_controller.gd`（~1974 行）。

## 二点三零、P3 第一批：`src/world` 的 7 个叶子（2026-10-11）

进 P3 先搬**叶子**（控制器调它们，叶子先落地，两个大控制器搬起来就没有跨语言面了）：
`input_setup.gd`→**`InputSetup.cs`**、`flavor_marker.gd`→**`FlavorMarker.cs`**、
`field_buff_hud.gd`→**`FieldBuffHud.cs`**、`player_controller.gd`→**`PlayerController.cs`**、
`chest.gd`→**`Chest.cs`**、`trigger_point.gd`→**`TriggerPoint.cs`**、
`roaming_enemy.gd`→**`RoamingEnemy.cs`**（14 个旧 `.gd`／`.gd.uid` 已删）。
节点类照样是 `partial`；GDScript 直接读写的字段一律 `[Export] public`（`chest_id`／`used`／`lit`／
`sequence_index`／`row`／`facing`／`sneaking`／`can_move`／`defeated`／`is_elite`… 与 `Inventory` 同理），
`RoamingEnemy.row` 因为「明雷用表行、房间敌人用字典」是 `[Export] Variant`。

**常量跨语言**（第 22 条那套）：GDScript 读得到的常量各配一个同名小写静态方法当取值口——
`RoamingEnemy.contact_distance()`／`threat_colors()`、`FlavorMarker.node_name()`、
`PlayerController.walk_speed()`／`sneak_ratio()`、`InputSetup.actions()`；
本来就 `static` 的函数（`InputSetup.ensure`／`elite_marker_texture_path`／`Chest.resolve_tier`／
`FlavorMarker.is_visible_highlight`／`FieldBuffHud.build_label`／`text_of`）直接调。
**注意默认参数**：`FieldBuffHud.build_label(Vector2)` 在 GDScript 里补上了第二实参 `12`（坑77）。

**调用面**：两个控制器（`local_map_controller`／`overworld_controller`）＋ 五份用例
（`test_encounter`／`test_handshake_doccounts`／`test_local_map`／`test_overworld`／`test_copy_audit`）
换 `preload(...cs)` 与取值口；`验证清单.csv`／`模块对接表.csv` 的 `code_paths` 换 `.cs`；
变异探针那 7 条 cast 跟着改指 `.cs` 并登记进 `$intentionallyUnpinned`（改 .cs 不重编 → 量不出来）。

### 两条新坑：

82. **C# 的信号名不转 snake_case**：`[Signal] public delegate void EncounteredEventHandler(string a, string b)`
   在 GDScript 侧暴露成 **`Encountered`**（就是委托名去掉 `EventHandler`，**不是** `encountered`）。
   2026-10-11 实测 `get_signal_list()` 打出 `"name": "Encountered"`；照 GDScript 习惯写
   `enemy.encountered.connect(...)` 会当场红「Invalid access to property or key 'encountered'」，
   而且**整个生成循环会被这行中断**（表现为「明雷/房间敌人一个都没生成」这种莫名其妙的现象）。
   调用点写成 `enemy.Encountered.connect(...)`。**P3 后面还有别的地方要靠信号时照这条办。**
83. **`--script` 模式遇到 C# 会崩，不能拿来当探针**：`godot --headless --script res://xxx.gd` 里
   `load("res://src/.../*.cs")` 会把引擎打崩（`Program crashed with signal 11`，
   `--quit-after` 也兜不住，进程会挂着）。要问「C# 侧运行时到底长什么样」，
   用**测试链里的临时 `print`**（写进 `.logs/tests.log`）或 C# 用例，别用 `--script`。

**本轮验收**：`dotnet build` 0 错 0 警、`--editor --quit` 无 Parse Error、`run_tests_cs.bat`
**2649 条**、`run_tests.bat` **9590 条**（基线 9588 ⇒ **9580**——`src/` 下少 7 个 `.gd`，
`_check_no_orphan_code` 按文件逐条断言于是掉 8）、`validate_tables.ps1` **PASSED**、
`check_loop.bat` **OK**、`run_mutation_sweep.bat` **PASSED**（passed steps 2）、
`run_windowed_smoke.bat` **PASSED**（13/13——这一批动的是渲染路径，真窗口那条必须跑）、
`run_all_checks.bat` **18/19**（余一条 `leaked-process` 是 MCP 编辑器实例，假红）。

下一批：**`local_map_controller.gd`（2469 行）＋ `test_local_map.gd`**（这一批之后它调的全是 C# 了）。

## 二点三一、P3 第二批：小地图控制器（2026-10-11）

`src/world/local_map_controller.gd` → **`src/world/LocalMapController.cs`**（4,079 行），
`tests/test_local_map.gd` → **`tests/TestLocalMap.cs`**（2,537 行，**483 条断言**，对位原 ~40 个
`_Check*`）；旧 `.gd`／`.gd.uid` 已删，`scenes/local_run.tscn` 的 `ExtResource` 换成 `.cs`。
它是全仓库最大的文件，也是「两个大控制器」里的第一个。

**跨语言面**：只有 `GameState`（走 `Get`／`Call`，与前几批同一套）与
`DialogueService.opening_node()` 这种取值口；世界层叶子（`Chest`／`TriggerPoint`／`RoamingEnemy`／
`FieldBuffHud`／`PlayerController`／`InputSetup`／`FlavorMarker`）上一批已全 C#，所以这一批**没有**
需要新造取值口的常量。

**调用面**：`scenes/local_run.tscn`（`ExtResource`）；`tools/mapgen/VerifyMapsCs.cs`（第 46 批搬 C# 后
直接读 `BUILDING_DISTANCE`／`FACILITY_LABELS`；旧的 `building_distance()`／`facility_labels()` 取值口随
最后一个 GDScript 调用方一起退休）；
`tests/TestMapAssets.cs`（`preload` 换 `.cs`，静态方法照旧直接调）；
`模块对接表.csv`（7 处 `code_paths` ＋ 10 个模块的用例列）／`验证清单.csv`（10 处）指针换 `.cs`。

**这条门限顺手补了一个洞**：`_check_file_size`（800 行硬上限）原来**只扫 `.gd`**——C# 迁移之后
所有新代码都不挨这条网（`LocalMapController.cs` 4,079 行照样绿）。已扩成 `.gd`／`.cs`，
`docs/dev/代码拆分清单.md` 抬头 `13 → 16 个超标`（去掉 2 个 `.gd`、加进 5 个 `.cs`：
`LocalMapController.cs`／`TestLocalMap.cs`／`TableValidatorActors.cs`／`TableValidatorWorld.cs`／
`Inventory.cs`）。**这是"翻译过来的大文件"第一次被记账**，后面 P3／P4 每搬一个大 `.gd`，
它的 `.cs` 对位就得进这张清单。

### 三条新坑：

84. **`Callable.From(...)` 造的委托型 Callable：`target` 是 `null`、读 `Method` 会抛
    `NullReferenceException`**——判断"这个 Callable 有没有接东西"不能写
    `callable.target != null && !callable.Method.IsEmpty`（GDScript `is_valid()` 的直觉翻译）。
    委托型 Callable 的 `target`／`Method` 都是空的，`Method` 还会抛 NRE，于是**注入的切场景回调
    永远不触发且静默**（表现是「用例里按了 E 却什么都没发生」）。判法改成
    `callable.Delegate != null || (target != null && !Method.IsEmpty)`。`_callable_valid` 唯一一处。
85. **`RunTestsCs._Ready` 里不能直接 `AddChild`**：直接在 `_Ready` 里跑全部用例会在
   根节点「正在装配子节点」的窗口里 `AddChild` 失败（`Parent node is busy setting up children`），
   于是**用例静默少测一大批**（断言数看着还不少，红不了）。改成
    `Callable.From(() => GetTree().Quit(RunAll())).CallDeferred();` 延后一帧跑。
86. **GDScript 桩注入要求字段放宽成 `GodotObject`**：`save_store_override` 原来标 `SaveStore`，
   而 GDScript 用例塞的是 `Object` 桩 → `InvalidCastException`。与 `SaveService.store` 同一条理由，
   字段类型改成 `GodotObject`（用例要调的十个 `_` 方法也一并从 `private` 放成 `public`）。

**用例搬移的连带改动**：`tools/RunTestsCs.cs` 的 `Suites` 加 `TestLocalMap`、
`MinAssertions` `2639 → 3122`（实测 3,132 − 10）；`tools/run_tests.gd` 的 `TEST_SCRIPTS` 摘掉
`test_local_map.gd`（那 479 条现在跑在 C# 链上）；`tests/test_copy_audit.gd` 的
`_hud_hint_text` 原本只收 `string hint = "…"` 那一行，漏掉 C# 的**多行拼接续行**（`+ "…"`）——
按键标签正好全写在续行里，于是 `Tab／I／K／Q／E／F5` 六个键全报「HUD 提示里没写」；
改成把 `string hint =` 之后连续的 `+` 续行一起收（`_handled_actions` 的 `IsAction(?:Just)?Pressed`
正则上一批已补）。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests_cs.bat` **3,132 条**（下限 → **3,122**）、
`run_tests.bat` **9,111 条**（基线 9,580 ⇒ **9,101**）、`validate_tables.ps1` **PASSED**。

## 二点三二、P3 第三批：大地图控制器（2026-10-11）

`src/world/overworld_controller.gd` → **`src/world/OverworldController.cs`**（2,903 行），
`tests/test_overworld.gd` → **`tests/TestOverworld.cs`**（1,207 行，**232 条断言**，对位原 24 个
`_Check*`）；旧 `.gd`／`.gd.uid` 已删，`scenes/world_run.tscn` 的 `ExtResource` 换成 `.cs`。
这是「两个大控制器」里的第二个，搬完 `src/world/` 下**已无 `.gd`**。

**跨语言面**：GDScript 协作方（`world_map_service`／`event_check_service`／`npc_service`／
`shop_service`／`copy_guard`／`overlay_stack`）走 `GD.Load<GDScript>(路径).Call(...)`——它们要到
`src/core` 那一批才搬。`LOCKED_SCENES` 从 `world_map_service.GetScriptConstantMap()` 读、
NPC 站位前缀同理，**不在 C# 里另抄一份**（决策 88 单一来源）。常量取值口 5 个：
`portal_distance()`／`portal_arm_margin()`／`discover_distance()`／`highlight_distance()`／
`region_join_distance()`（GDScript 读不到 C# 的 `const`，与前几批同一套）。

**调用面**：`scenes/world_run.tscn`（`ExtResource`）；`tools/CheckLoopRunner.cs`；
`tests/test_recruit.gd`（`preload` 换 `.cs`，两个常量换取值口）；
`tests/test_copy_audit.gd`（控制器清单换 `.cs`）；`src/world/PlayerController.cs` 的注释指针；
`docs/dev/设计实现对照.md`（3 行符号换成 C# 写法）；`docs/dev/框架说明.md` 一行指针；
`模块对接表.csv`／`验证清单.csv`（`overworld_controller.gd`／`test_overworld.gd` → `.cs`）；
`tools/analysis_mutations_code.ps1`（7 条 cast 改指 `.cs`，并进 `$intentionallyUnpinned`——
**改 `.cs` 不重编就量不出来**，等 P4 给探针加 `.cs` 支持时删掉那 7 条豁免）。

### 五条新坑：

87. **C# 的默认参数不进 Godot 的方法签名**：`enter_local_map(scene_id, from_teleport = false)`
    在 Godot 眼里是**两个参数都要给**的。GDScript 那边照旧写
    `call("enter_local_map", "scene_x")`（GDScript 的 `= false` 没这条限制），报的是
    `Invalid call. Nonexistent function 'enter_local_map' in base 'Node2D (OverworldController.cs)'`
    ——**看着像方法不存在，其实是实参个数不对**。`tools/check_loop.bat` 在真场景里抓到。
    修法是**重载一个短签名**（`enter_local_map(scene_id) => enter_local_map(scene_id, false)`），
    别去改 GDScript 调用点（那个调用点迟早要搬走，短签名才是原 `.gd` 的语义）。
     **搬别的方法时先扫一遍自己有没有默认参数、GDScript 那边是不是少给了实参。**
     **（2026-10-11 更正，见坑 92）**：「脚本类里加个重载」这一招**不成立**——GDScript 解析
     C# 脚本类的静态调用时，一个名字出现两次就**整个名字都解析不到**。短签名只在
     **实例方法**上安全；静态方法要么改名，要么让调用点把实参给全。
88. **`Godot.Collections.Dictionary` 读不存在的键会抛**（GDScript 返回 `null`）：用例里的
    取值一律走 `ContainsKey` 或一个 `_v(dict, key)` helper，别直接 `dict[key]`。
89. **GDScript 对象的「属性」要用 `Get` 不是 `Call`**：`battle.enemies` 是 `var`（不是函数），
    `battle.Call("enemies")` 报 `Nonexistent function 'enemies' in base 'Godot.Control'`；
    `battle.Call("finished")`／`Call("status_text")` 才是对的。搬用例时逐条分清。
90. **用例里真切场景会把 C# 运行器整个摘出场景树**：`TestLocalMap` 的木桩练习战用例按 E
    真组了一场遭遇，控制器没有 handler 就走到 `_change_scene("battle_screen.tscn")`；
    在 C# 链里运行器节点**就是当前场景**，换场景把它摘掉 → 后面所有用例当场判「没有注入场景树」。
    GDScript 链（`--script`，没有 current_scene）看不出来，所以这是**上一批留下的假绿**，
    这轮加了大地图用例才暴露。修法：像真场景一样注入 `battle_switch_handler`。
    **搬用例时凡是有「按 E 开战」的分支，先确认 handler 注了没有。**
91. **C# 类要被 Godot 注册，得先有一次「它的 `.cs` 被当资源加载过」**：`CharacterSheet`（C#）内部
    `new SetService(...)`，而 GDScript 链里 `SetService.cs` 一直没被 `preload` 过 →
    随机崩在 `FATAL: Condition "script.is_null()" is true. at: tie_user_managed_to_unmanaged`
    （实测 4 跑 2 崩，崩的那次整条链死在 `test_battle_fuzz`）。这不是这一批引进的
    （`CharacterSheet` 搬 C# 那批就有），但**这一批把它修了**：`src/core/party_builder.gd`
    补一行 `const SetServiceScript := preload("res://src/core/SetService.cs")`。
    **凡是「C# 内部 new 另一个 C# 类」的连线，GDScript 侧要替那个类留一条 preload。**

**用例搬移的连带改动**：`tools/RunTestsCs.cs` 的 `Suites` 加 `TestOverworld`、
`MinAssertions` `3122 → 3354`（实测 3,364 − 10）；`tools/run_tests.gd` 的 `TEST_SCRIPTS` 摘掉
`test_overworld.gd`、`MIN_ASSERTIONS` `9101 → 8866`（实测 8,876 − 10）；
`docs/dev/代码拆分清单.md` 把两个 `.gd` 行换成 `OverworldController.cs`（2,903）／
`TestOverworld.cs`（1,207），`TestLocalMap.cs` 也跟着刷新（2,537 → 2,542）。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests_cs.bat` **3,364 条**（下限 → **3,354**）、
`run_tests.bat` **8,876 条**（基线 9,101 ⇒ **8,866**，连跑两遍都绿，见坑 91）、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、`run_windowed_smoke.bat` **13/13 PASSED**、
`run_all_checks.bat` **18/19**（余下的 `leaked-process` 是 MCP 编辑器进程的假红）。

## 二点三三、P3 第四批：`src/ui` 五个叶子库（2026-10-11）

这一批是**纯工具库**，没有场景绑定，搬完 `src/ui` 下还剩 **11 个面板 `.gd`**（下一批）。

| 新文件 | 对位旧文件 | 内容 |
|---|---|---|
| `src/ui/CopyGuard.cs`（68 行） | `copy_guard.gd` | `id_tokens(root)`／`ascii_line(root)`（全静态） |
| `src/ui/IconPaths.cs`（106 行） | `icon_paths.gd` | 7 条路径方法 ＋ `load_icon` ＋ `make_icon(path, node_name)`；`SIZE` |
| `src/ui/LayoutBudget.cs`（189 行） | `layout_budget.gd` | `fits`／`min_height`／`min_width`／`measure_target`／`ascii_line`／`ascii_line_tagged`／`has_opaque_backdrop`／`has_opaque_backdrop_min`／`backdrop_of`／`ascii_backdrop_line`／`content_min_width`／`content_fits`／`content_fits_limit`／`ascii_content_line`／`ascii_content_line_limit`／`clamp_into` ＋ 取值口 `design_width()`／`design_height()`／`backdrop_min_alpha()` |
| `src/ui/UiKit.cs`（128 行） | `ui_kit.gd` | `color(name)`／`paint_backdrop(screen)`／`add_card(parent, title, hint, node_name)` |
| `src/ui/OverlayStack.cs`（155 行） | `overlay_stack.gd` | `mount(host, panel)`／`mount_on_layer(host, panel, layer)`／`ids`／`depth`／`is_empty`／`has`／`top`／`push`／`pop`／`pop_to`／`remove`／`clear` ＋ 取值口 `limit()`／`overlay_layer()` |

**调用面**（都在 GDScript 侧，`preload` 路径换 `.cs`）：11 个还没搬的界面（`clue_screen`／
`dungeon_screen`／`character_screen`／`cultivate_screen`／`waypoint_screen`／`battle_screen`／
`creation_screen`／`shop_screen`／`npc_panel`／`main_menu`）、`src/bootstrap.gd`、
两个世界控制器（`LocalMapController`／`OverworldController`——它们的 `_copy_guard_script_cache`／
`_overlay_stack_script_cache` 由 `GDScript` 改成 **`Script`**，`GDScript` 类型去 `Load` 一个 `.cs`
会类型不匹配）、`tests/test_battle_ui.gd`／`test_character_ui.gd`、
`assets/ui/theme_lunhui.tres`（主题里那几条脚本引用）、`src/ui/AGENTS.md`。

**用例**：`tests/TestLayoutBudget.cs`（**22 条**，对位原 `test_layout_budget.gd`）＋
`tests/TestOverlayStack.cs`（**26 条**，对位原 `test_overlay_stack.gd`），两个都注册进
`RunTestsCs.Suites`；两个旧 `.gd` 用例从 `tools/run_tests.gd` 的 `TEST_SCRIPTS` 摘掉、
`test_handshake.gd` 的 `INFRASTRUCTURE_TESTS` 里 `test_layout_budget.gd` → `TestLayoutBudget.cs`。

### 一条新坑（这条是这批的主菜）：

92. **Godot C# 的脚本类不许「重载」——重载过的名字，GDScript 一个都解析不到。**
    这批最初照坑 87 的旧修法，给 `ascii_line`／`has_opaque_backdrop`／`content_fits`／
    `ascii_content_line`（`LayoutBudget`）、`make_icon`（`IconPaths`）、`add_card`（`UiKit`）、
    `mount`（`OverlayStack`）各留了两三档重载。`dotnet build` **0 错 0 警**，
    `--script` 的探针里 `get_script_method_list()` 也**两条都在**——但 GDScript 解析调用点时
    报的是：

    ```
    SCRIPT ERROR: Parse Error: Static function "ascii_line()" not found in base "CSharpScript".
    ```

    **判据极准**：报错的名字**恰好全是重载过的**；同一批没重载的 `fits`／`clamp_into`／
    `design_width` 一个都没报。受害面比想象的大：
    ① 11 个界面场景全在 `load()` 时就带着这串 Parse Error（`--script` 链里它们照样"LOADED"、
       真场景自检里 `LAYOUT`／`BACKDROP`／`CONTENT` 三行全丢）；
    ② `OverworldController`／`LocalMapController` 里那 11 处
       `_overlay_stack_script().Call("mount", this, panel)` 也**跟着失灵**
       ——`TestOverworld` 从 232 条断言掉到 78 条（"面板显示探索进度""面板能传送到清风驿"两条红）。

    **修法（按优先级）**：① 用不着的那几档直接删（`IconPaths.make_icon` 的 1 参档、
    `UiKit.add_card` 的 2／3 参档——调用点一处都没有）；② 用得着的**改名**，例如
    `ascii_line(screen, tag)` → `ascii_line_tagged`、`content_fits(screen, limit)` →
    `content_fits_limit`、`has_opaque_backdrop(screen, min_alpha)` → `has_opaque_backdrop_min`、
    `mount(host, panel, layer)` → `mount_on_layer`；③ 让 GDScript 调用点把实参给全
    （`test_character_ui.gd` 的 `make_icon(missing_path)` → `make_icon(missing_path, "Icon")`）。
    **搬 C# 时判据是「公有方法名在这个类里出现过几次」，不是「签名一样不一样」。**
    **自检怎么抓**：这批靠的是 `check_log_errors.bat`——Parse Error 一句就把那一步判红
    （否则会变成最坏的一种假绿：界面自检照打 OK，`LAYOUT`／`BACKDROP`／`CONTENT` 三行却全没了）。

### 连带改动

- `tools/analysis_mutations_code.ps1`：3 条 cast 改指 `src\ui\LayoutBudget.cs`（`Find`／`Repl`
  换成 C# 写法），并进 `$intentionallyUnpinned`——**改 `.cs` 不重编就量不出来**，
  P4 给探针加 `.cs` 支持时删掉这三条豁免。
- `tools/RunTestsCs.cs`：`Suites` 加两个用例，`MinAssertions` `3354 → 3402`（实测 3,412 − 10）。
- `tools/run_tests.gd`：`TEST_SCRIPTS` 摘掉两个旧用例，`MIN_ASSERTIONS` `8866 → 8816`
  （实测 8,826 − 10）。
- `docs/dev/验证清单.csv`：`ui_copy` 行的 `tests/test_overlay_stack.gd` → `tests/TestOverlayStack.cs`。
- `docs/dev/代码拆分清单.md`：**不用动**（这 5 个文件 68–189 行，都远在 800 行线下）。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests_cs.bat` **3,412 条**（下限 → **3,402**）、
`run_tests.bat` **8,826 条**（基线 8,866 ⇒ **8,816**）、`validate_tables.ps1` **PASSED**、
`check_loop.bat` **OK**、`run_windowed_smoke.bat` **13/13 PASSED**、
`run_all_checks.bat` **18/19**（余下的 `leaked-process` 是 MCP 编辑器进程的假红）。

## 二点三四、P3 第五批：枢纽页 `placeholder_game`（2026-10-11）

`src/ui/placeholder_game.gd`（273 行）→ **`src/ui/PlaceholderGame.cs`**（332 行），
`scenes/placeholder_game.tscn` 的 `ExtResource` 换成 `.cs`，旧 `.gd`／`.gd.uid` 已删。
这一批是 **10 个真面板之前的那一个**（它没有自检、也不在 13 个场景自检清单里），
拿它把「面板怎么搬」这条路走通：场景绑定、用例注入、跨语言回调。

**跨语言面**：`GameSession`／`GameData` 两个 autoload 还是 GDScript——读它们的
`state`／`db`／`last_battle` 要用 **`Get(名字)` 不是 `Call`**（属性不是方法，坑 67／89）；
`Sfx`（音效层）走 `GD.Load<Script>("res://src/audio/sfx.gd").Call("cycle_volume")`。
`save_store_override` 与 `_settings_override` 放宽成 **`GodotObject`** 才接得住 GDScript 桩（坑 86）。

### 一条新坑（这条比这道菜本身重要）：

93. **GDScript 的 lambda 进不了 C# 的 `Callable`——它会静默变成空 Callable。**

    C# 的 `Godot.Callable` 是**托管结构体**（反射确认：私有字段只有
    `_target`／`_method`／`_delegate`／`_trampoline` 四个），只装得下两种形状：
    「GodotObject ＋ 方法名」和「C# 委托」。而 **GDScript 的 lambda 是 `CallableCustom`**
    （GDScript 侧看得到 `get_object()` 是本脚本实例、`get_method()` 是 `<anonymous lambda>`，
    可 C# 侧 `target` 是 `null`、`Method` 是空、`Delegate` 是 `null`）。
    于是 `panel.back_handler = func(): …` 从 GDScript 赋给 C# 面板之后：
    **`Callable.Call()` 不报错、也不执行**——回调永远不触发，`_callable_valid()` 那套判据
    还会把它判成「没接」而走进兜底分支。实测现场：`test_menu` 的「返回按钮触发回退」
    期望 1、实际 0，日志里**一条 ERROR 都没有**（最坏的那种假绿）。
    存成 `Variant` 就不会丢（实测同一枚 lambda 存 Variant 后读回来 `is_null=false`）。

    **做法（过渡期的统一形状，P5 再收回）**：
    ① C# 面板上的回调属性一律写成 **`[Export] public Variant back_handler = default;`**
       （不要 `Callable`）；
    ② 新增 `src/ui/callable_invoker.gd`（28 行，**临时**）——`is_valid(handler)` 与
       `invoke(handler, args)` 两个静态方法，在 GDScript 侧把回调调起来；
    ③ C# 侧一对小工具：`_has_callback(variant)` 走 `is_valid`、`_invoke_callback(variant, …)`
       走 `invoke`（参数打包成 `Godot.Collections.Array`），返回值原样透出
       （`sweep_handler` 那类要读返回值）；
    ④ **判据不能在 C# 侧写**：`Callable.From(…)` 的委托型 `target` 是 `null`、`Method` 取值会抛，
       GDScript 的 lambda 在这侧整个是空的——两种「接上了」的形状 C# 一个都认不出来。

    **波及面比这一批大**：剩下 10 个面板的 `back_handler`／`return_handler`／`sweep_handler`／
    `scene_switch_handler`／`travel_handler`／`spar_handler`／`npc_open_handler` 全是这个形状，
    而且注入方**既有用例也有产品代码**（`battle_screen.gd` 打开 npc／角色／线索面板时就赋 lambda）
    ——所以「搬一个面板就顺手改调用点」是行不通的，必须走这条统一通道。
    **P5 GDScript 清零后**：回调属性收回 `Callable`，删 `callable_invoker.gd` 与 C# 侧那两个工具。

### 连带改动

- `docs/dev/模块对接表.csv`（2 行：`main_menu`／`ui_copy` 的 `code_paths`）、
  `docs/dev/设计实现对照.md`（自动战斗那一行的符号）。
- `docs/dev/代码拆分清单.md`：**不用动**（332 行，远在 800 行线下）。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests.bat` **8,831 条**（基线 8,816——没抬，
多出来的 5 条是新文件过文本门限）、`run_tests_cs.bat` **3,412 条**、`check_loop.bat` **OK**、
`validate_tables.ps1` **PASSED**。

## 二点三五、P3 第六批：驿站 `WaypointScreen`（2026-10-11）

`src/ui/waypoint_screen.gd`（301 行）→ **`src/ui/WaypointScreen.cs`**（约 380 行），
`scenes/waypoint_screen.tscn` 的 `ExtResource` 换过、旧 `.gd`／`.gd.uid` 已删。
**这是第一个「带自检的真正面板」**（在 13 个场景自检清单里），所以这一批把「自检也一起搬」
这条路走通了。

**跨语言面**：`world_map_service`／`game_state` 还是 GDScript——建实例
`GD.Load<GDScript>(…).Call("new", db, state)`、`Call("new_game", db, "normal")`
（与 `OverworldController`／`LocalMapController` 同一套），方法走 `Call`、属性走 `Get`／`Set`
（`session.pending_local_scene` 是属性，用 `Set`）。两个回调按坑 93 走 `Variant` ＋
`CallableInvoker`；`state_override` 同样放宽成 `Variant`（用例／控制器注入的是 GDScript 的 GameState）。

**自检搬移的三处形状差异**（都会静默红／绿错，记着）：

1. `await tree.process_frame` → `await ToSignal(tree, SceneTree.SignalName.ProcessFrame)`，
   方法签名写 **`private async void _run_waypoint_selftest()`**（与两个世界控制器同形）。
2. GDScript 的 `PackedStringArray` 在 C# 里就是 **`string[]`**（坑 66）；
   `"、".join(names)` → `string.Join("、", names)`。
3. **自检的「聚合断言条数棘轮」是按文件路径登记的**（`tests/test_handshake.gd` 的
   `VERIFICATION_CHECK_FLOORS`）：搬完要把 key 换成 `.cs`、`pattern` 从 `ok = ok and` 换成
   **`ok = ok &&`**，`floor` 照实际条数（这一批 11 条原样搬过来，仍是 11）。
   **漏改这一条的后果**：门限会一直数 `waypoint_screen.gd`（已经不存在的文件）——
   要么报缺行、要么数到 0 条直接红。

### 顺带说清一条「为什么计数会变」的事

`run_tests.bat` 的总断言数**不是常量**：`test_handshake_repo` 里有几处
「**按文件**一条断言」的循环（`for path in _collect_files(["src"])` ／ `.uid` 文件清单），
所以删一个 `.gd`／`.uid`、加一个 `.cs` 都会让总数上下浮动几条。这一批从 **8,831 → 8,825**
（`MIN_ASSERTIONS` 仍是 8,816，余量 9）。**别把这条浮动当成回归**；真掉到基线以下再照提示
贴到 `total−10`。P5 GDScript 清零时这条棘轮本来就要拆掉。

### 连带改动

- `scenes/waypoint_screen.tscn`（`ExtResource`）、`tests/test_handshake.gd`（上面第 3 条）。
- `docs/dev/模块对接表.csv`（`map_overworld` 行的 `code_paths`）。
- `docs/dev/代码拆分清单.md`：**不用动**（约 380 行，远在 800 行线下）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene waypoint` **WAYPOINT SELF-TEST: OK**、
`run_tests.bat` **8,825 条**（基线 8,816）、`run_tests_cs.bat` **3,412 条**（`TestOverworld`
那两条「面板显示探索进度／能传送到清风驿」现在驱动的是 C# 面板，条数不变）、
`check_loop.bat` **OK**、`run_windowed_smoke.bat` **13/13 PASSED**、
`run_all_checks.bat` **18/19**（余 `leaked-process` 假红）。

## 二点三六、P3 第七批：主菜单 `MainMenu`（2026-10-11）

`src/ui/main_menu.gd`（353 行）→ **`src/ui/MainMenu.cs`**（约 430 行），
`scenes/main_menu.tscn` 的 `ExtResource` 换过（**顺手把带 uid 的那行改成不带 uid**——
`.cs` 没有对应 `.uid`，留着旧 uid 是错的）、旧 `.gd`／`.gd.uid` 已删。
**这是主场景**（`project.godot` 的 `run/main_scene`），也是 `--menu-selftest` 那一条的宿主。

**跨语言面**：`src/core/game_state.gd`（`default_party_ids_from_recruit`）还是 GDScript，
走 `GD.Load<GDScript>(…).Call(…)`；`GameSession`／`GameData` 两个 autoload 也是 GDScript——
读 `state`／`db` 用 `Get(名字)`、调方法用 `Call`。`MenuController`／`SaveStore`／`TableDb` 已是 C#，
直调。两个回调（`quit_handler`／`scene_switch_handler`）按坑 93 走 `Variant` ＋ `CallableInvoker`。

### 这一批的两条表面差异（都会静默错）

1. **常量跨语言（坑⑩的正面撞上）**：`CREATION_SCENE`／`OVERWORLD_SCENE`／`PLACEHOLDER_SCENE`
   从 GDScript **读不到**，而三个地方在读：`tests/test_menu.gd:192/231`（`menu.CREATION_SCENE`／
   `menu.OVERWORLD_SCENE`）与 `tools/CheckLoopRunner.cs`（原 `check_loop.gd:287`；`menu.PLACEHOLDER_SCENE`）。
   做法照 `MenuController` 那一批：**把调用方改成各自的本地常量**（`test_menu.gd` 本来就有一份
   `PLACEHOLDER_SCENE`／`CREATION_SCENE`，只补了 `OVERWORLD_SCENE`；`check_loop.gd` 补了
   `PLACEHOLDER`），常量本身留在 C# 侧给内部用。
2. **`_` 前缀的 `public` 方法可以被 GDScript 调**（先例：`LocalMapController._floor_label()` 被
   `check_loop.gd` 调）：`tests/test_menu.gd` 直接调 `menu._refresh()` 重建槽位列表，
   所以 C# 侧必须是 **`public void _refresh()`**。**字段反过来**——GDScript 读写的字段一律要
   `[Export]`（`controller`／`pending_scene`／`store_override`；`store_override` 放宽成 `GodotObject`
   才接得住 C# 的 `SaveStore`，坑 86 的同一条）。

**自检搬移与 `WaypointScreen` 同形**：`private async void _run_menu_selftest()` ＋
两次 `await ToSignal(tree, SceneTree.SignalName.ProcessFrame)`；`string[]`／`string.Join`；
棘轮表 `tests/test_handshake.gd` 那行换成 `"src/ui/MainMenu.cs"` / `"ok = ok &&"` / floor **7**
（原 `.gd` 就是 7 条，这次贴平）。

### 一条「怎么跑」的坑（浪费了半小时，记下来）

**从 PowerShell 调 `check_scene.bat` 时，`--save-dir=res://…` 会被在 `=` 处截断**
（`cmd` 那条链把 `=` 当分隔符；`%4` 只拿到 `--save-dir`），于是自检静默拿
`user://saves` 跑——**看着像"搬完坏了"，其实是调用方式**。两种正确姿势：
`cmd.txt /c --% tools\check_scene.bat "menu" "…" "--menu-selftest" "--save-dir=res://.logs/menu_selftest"`
（`--%` 让 PowerShell 不再解析），或者干脆跑 `run_all_checks.bat`／`run_windowed_smoke.bat`。

### 连带改动

- `scenes/main_menu.tscn`（`ExtResource`，去掉 uid）、`tests/test_handshake.gd`（棘轮）。
- `tests/test_menu.gd`（`menu.CREATION_SCENE` → 本地 `CREATION_SCENE`；`menu.OVERWORLD_SCENE` → 本地 `OVERWORLD_SCENE`）、
  `tools/CheckLoopRunner.cs`（原 `check_loop.gd` 那份本地 `PLACEHOLDER` 已不再需要——第四十七批起直接读 `MainMenu.PLACEHOLDER_SCENE`）。
- `docs/dev/模块对接表.csv`（2 行：`main_menu`／`ui_copy` 的 `code_paths`）。
- `docs/dev/代码拆分清单.md`：**不用动**（约 430 行，远在 800 行线下）。
- `test_copy_audit.gd`／`test_settings.gd` 只读场景文件，没动。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene menu` **MENU SELF-TEST: OK**、
`run_tests.bat` **8,828 条**（基线 8,816）、`run_tests_cs.bat` **3,412 条**、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**（余 `leaked-process` 假红）。

## 二点三七、P3 第八批：打坐面板 `CultivateScreen`（2026-10-11）

`src/ui/cultivate_screen.gd`（381 行）→ **`src/ui/CultivateScreen.cs`**（约 455 行），
`scenes/cultivate_screen.tscn` 的 `ExtResource` 换过、旧 `.gd`／`.gd.uid` 已删。
入口是清风驿客栈（`LocalMapController.open_cultivate()`），自检是 13 个场景里的
`--cultivate-selftest`。

**跨语言面**：`src/core/game_state.gd` 还是 GDScript——`learned_of`／`char_name`／`set_mastery`
走 `Call`，`inventory.money` 走 `state.Get("inventory").AsGodotObject().Get("money")`；
打坐余韵挂在 `GameSession` 上（`add_field_buff`／`active_field_buffs`）也走 `Call`。
`MasteryService`／`GrowthCalculator`／`BuffService`／`TableDb` 已是 C#，直调。

### 这一批的三条表面差异

1. **`_check_multi_member_ui` 直接读面板的三个字段**（`tests/test_mastery.gd`）：
   `screen.db_override`／`screen.state_override`／`screen.selected_char` 要 GDScript 读得到
   （`[Export]`，前两个收成 `Variant`），而 `screen.mastery.mastery_of(…)` 读的是**服务对象本身**
   ——`mastery` 得是 `[Export] public GodotObject`，不然多人切人那条用例当场红。
2. **`_require_node` 里的 `assert(false, …)` 只能退化成 `GD.PushError`**：C# 没有 Godot 的 `assert`，
   `System.Diagnostics.Debug.Assert` 在 Godot 的 C# 运行期会弹断言框而不是抛错（更糟）。
   仓库里 `WaypointScreen.cs` 先例就是只 `PushError`——**迁移期统一按这条走**，
   场景骨架缺节点时的症状是「报错＋随后空引用」，仍然是响的。
3. **`%.2f` 要显式走不变文化**：C# 的 `$"…{v:F2}"` 跟系统区域走，这里统一用
   `value.ToString("F2", CultureInfo.InvariantCulture)`（新加的 `_f2()` 小工具）。

**连带改动**：`scenes/cultivate_screen.tscn`、`tests/test_handshake.gd` 棘轮那行
（`"src/ui/CultivateScreen.cs"` / `"ok = ok &&"` / floor **11**，与旧 `.gd` 同数）、
`docs/dev/模块对接表.csv`（`growth_system` 行的 `code_paths`）。
`docs/dev/代码拆分清单.md` 不用动（约 455 行）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene cultivate` **CULTIVATE SELF-TEST: OK**、
`run_tests.bat` **8,820 条**（基线 8,816）、`run_tests_cs.bat` **3,412 条**、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**（余 `leaked-process` 假红）。

## 二点三八、P3 第九批：副本完成度 `DungeonScreen`（2026-10-11）

`src/ui/dungeon_screen.gd`（443 行）→ **`src/ui/DungeonScreen.cs`**（约 520 行），
`scenes/dungeon_screen.tscn` 的 `ExtResource` 换过、旧 `.gd`／`.gd.uid` 已删。
入口是副本里按 M（`LocalMapController.open_dungeon_panel()`），自检是 `--dungeon-selftest`。

**跨语言面**：`src/core/game_state.gd` 还是 GDScript（`record_dungeon` 走 `Call`）；
两个回调（`return_handler`／`sweep_handler`）是 `Variant` ＋ `CallableInvoker`——**用例还会把
`sweep_handler` 显式置成空 `Callable()`** 来验「没有落点」那条分支，这正是不用 C# `Callable` 的理由。
扫荡的真实 handler 由 `LocalMapController` 用 `Callable.From<int, Dictionary>(…)` 塞进来。

### 这一批的三件事（前两件是新形状，第三件是「工具的账」）

1. **`GetChildren()` 是带类型的 `Array&lt;Node&gt;`，脚本 API 不收**
   （`error CS0029: 无法将 Array&lt;Node&gt; 隐式转换为 Array`）。对位 GDScript 的 `get_children()`
   要先转成无类型 `Godot.Collections.Array` 再返回——`floor_rows()` 就是这一处。
   **凡是「把节点列表交回给脚本」的接口都会撞到这条**，后面搬面板时会再遇到。
2. **常量扫荡动画（`SWEEP_ANIM_SECONDS`）照旧「自己数时间」**，不换成 tween：
   `tests/test_dungeon_ui.gd` 与自检都靠 `step_sweep_animation(δ)` 精确推帧，不依赖帧率。
   搬 C# 时这条形状整个保留（`_Process(delta)` 只调它）。
3. **变异探针的 cast 要跟着文件走**：`tools/analysis_mutations_code.ps1` 里
   `SWEEP_ANIM_SECONDS` 那条原本钉 `src\ui\dungeon_screen.gd` 的 `const … := 0.6`，
   文件一删 `Find` 就找不到了——探针会把它报成 `[INVALID]（cast 失效）`。
   按已有那批 `.cs` 的老规矩处理：**cast 改指 `.cs`（新写法 `public const double … = 0.6;`）
   ＋ 往 `$intentionallyUnpinned` 加一条**（理由：纯表现、用例推帧不靠秒数；改 .cs 不重编量不出来）。
   **P4 给探针加 .cs 支持后，这一条（连同原有的 3 条）一起删**。

**`MIN_ASSERTIONS` 跟着降了一次**：删掉一个 `.gd` ＋ 它的 `.uid`，
`test_handshake_repo` 那几处「按文件一条断言」的循环少 3 条 → 总数 8,820 → **8,817**，
低于旧基线 8,816 当场红。照提示贴 `total−10` → **8,803**（不是把上限调高）。

**连带改动**：`scenes/dungeon_screen.tscn`、`tests/test_handshake.gd` 棘轮那行
（`"src/ui/DungeonScreen.cs"` / `"ok = ok &&"` / floor **11**）、`tools/run_tests.gd`（基线）、
`tools/analysis_mutations_code.ps1`（cast ＋ 豁免各一条）。`模块对接表.csv` 不用动
（`dungeon_system` 行的 `code_paths` 里本来就没列这个面板）。`代码拆分清单.md` 不用动（约 520 行）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene dungeon` **DUNGEON SELF-TEST: OK**、
`run_tests.bat` **8,817 条**（基线改成 8,803）、`run_tests_cs.bat` **3,412 条**、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**（余 `leaked-process` 假红）。

## 二点三九、P3 第十批：线索本 `ClueScreen`（2026-10-11）

`src/ui/clue_screen.gd`（449 行）→ **`src/ui/ClueScreen.cs`**（670 行），
`scenes/clue_screen.tscn` 的 `ExtResource` 换过（**那一行本来就没有 uid，只有 path**）、
旧 `.gd`／`.gd.uid` 已删。入口是小地图／大地图按 K（`LocalMapController.open_clues()`／
`OverworldController.open_clues()`），自检是 13 个场景里的 `--clue-selftest`。

**跨语言面**：`src/core/clue_service.gd` **还是 GDScript**——建实例走
`GD.Load<GDScript>(…).Call("new", db, state)`，读内容走 `Call("section", id, scope, scene_id)`／
`Call("clues_for_scene", …)` 等；`src/core/game_state.gd` 还是 GDScript（`record_dungeon`／
`set_flag`／`char_ids` 走 `Call`／`Get`）；`NpcService.quest_done_flag` 也还是 GDScript 静态方法
（`_npc_script().Call("quest_done_flag", …)`），而 `WorldEventService.hint_flag`／`UiKit`／
`CopyGuard`／`LayoutBudget` 已是 C#，直调。`return_handler` 是 **`Variant`** ＋ `CallableInvoker`（坑 93）。

### 这一批的三件事（第 1、3 件是新形状）

1. **读「还是 GDScript 的服务」的 `const` 走 `GetScriptConstantMap()`**：五类页签的顺序与名字
   只许有一处定义（`ClueService.SECTION_ORDER`／`SECTION_TITLES`），面板不许抄第二份。C# 侧取
   `_clue_service_script().GetScriptConstantMap()["SECTION_ORDER"].AsGodotArray()`（`SECTION_TITLES`
   同理 `.AsGodotDictionary()`）。**方向是单向的**：C# 读 GDScript 的 `const` 可以，GDScript 读
   C# 的 `const` 不行（前面几批靠「小写静态取值口」补的就是这一头）。
2. **`PackedStringArray` 是值类型，C# 侧不能就地 `Add`**：自检「角色页」要模拟「同伴入队」，
   GDScript 写的是 `fresh.char_ids.append("ch_ci")`（属性在 GDScript 里返回同一枚数组，就地改有效）。
   照抄成 `fresh.Get("char_ids").AsGodotArray().Add(…)` 有两处错：`Get` 拿到的**不是 Godot Array**
   （`.AsGodotArray()` 给空表）、而且 PackedStringArray 本来就是值类型、改的是拷贝——那一页自检
   当场 `false`。正确形状是**取出来 → 复制加一项 → `Set` 写回**：
   `string[] ids = fresh.Get("char_ids").AsStringArray(); …; fresh.Set("char_ids", joined);`。
   （与此前「`Dictionary` 拿到的是同一份引用、就地改即可」相反——**引用型看 `Get` 是不是同一枚，
   值类型一律读改写回**。）
3. **「界面节点契约」那道门限原来只扫 `.gd`**：`tests/test_handshake_code.gd::
   _check_scene_node_contracts` 原来按**文件名**把 `src/ui/Xxx.gd` 对应到 `scenes/xxx.tscn`
   并统计 `_require_node(...)`；搬走的 C# 面板一个都不进统计（`CultivateScreen.cs`→
   `cultivate_screen.tscn` 名字都对不上），覆盖数从 32 一路掉到 27、**低于 `> 30` 的门槛当场红**。
   改成**遍历 `scenes/*.tscn`、读每张场景自己的 `ExtResource` 指向的 `src/ui` 脚本**——`.gd`／`.cs`
   一视同仁，也不再依赖文件名大小写。这一改覆盖数回到 **49**。

**另一条被印证的小形状**（不是新坑）：`[Export]` 能挂在**下划线开头**的字段上，GDScript 照样读得到
——用例读 `panel._tabs.get_tab_count()`／`panel._section_index`／`panel._subtitle.text`，
所以 C# 侧这三处必须是 `[Export] public`。

**连带改动**：`scenes/clue_screen.tscn`、`tests/test_handshake.gd` 棘轮那行
（`"src/ui/ClueScreen.cs"` / `"ok = ok &&"` / floor **16**，与旧 `.gd` 同数）、
`tests/test_handshake_code.gd`（节点契约那道门限重写）。`模块对接表.csv`／`验证清单.csv` 不用动
（`hidden_content` 行的 `code_paths` 本来就没列这个面板）。`代码拆分清单.md` 不用动（670 行）。

**断言数变化**：删一个 `.gd` ＋ 它的 `.uid`、加一个 `.cs` 会让「按文件一条」那几处少几条，
本轮 `run_tests.bat` 落到 **8,833 条**（基线 8,803 不动——节点契约那道门限重写又补回 22 条）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene clue` **CLUE SELF-TEST: OK**、
`run_tests.bat` **8,833 条**（基线 8,803）、`run_tests_cs.bat` **3,412 条**、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**（余 `leaked-process` 假红）。

## 二点四〇、P3 第十一批：商店 `ShopScreen`（2026-10-11）

`src/ui/shop_screen.gd`（576 行）→ **`src/ui/ShopScreen.cs`（696 行）＋ `src/ui/ShopScreenSelfTest.cs`（136 行）**
（`namespace Lunhui.Ui`，两个 `public partial class ShopScreen`）；`scenes/shop_screen.tscn` 的
`ExtResource` 换成 `.cs`（那行本来就没带 `uid`，直接改 path）；旧 `.gd`／`.gd.uid` 已删。

### 这一批的四件事（第 2、3 件是新形状）

1. **`Math.Min/Max/Floor` 要写全名**：C# 项目没 `using System;`（与仓库里几处 `System.Action`／
   `System.Array` 同源），`Math.X` 编译不过（`CS0103: 当前上下文中不存在名称"Math"`），
   全改成 `System.Math.X` 才绿。新面板里凡是 GDScript `mini/maxi` 都照这个形状落。

2. **`TABS` 常量被 GDScript 门限读文本——搬 C# 不能只改后缀**：`tests/test_handshake_docs.gd`
   第 33 条（`_check_shop_tabs_have_buttons`）原来 `get_file_as_string("…shop_screen.gd")`，
   再按 GDScript 字典形状 `"key": "buy"` 解析页签、按 `_tab_node_suffix` 那行解析后缀。
   面板搬 C# 后**这条门限会直接红**（读不到文件）。做法：把 `TABS` 改成每行
   `new[] { "buy", "Buy", "买入" }`（key／场景按钮后缀／显示名三元组），门限改成读
   `ShopScreen.cs`、用一条正则 `\{\s*"([a-z_]+)"\s*,\s*"([A-Za-z]+)"` 一次把 key 与后缀都取出来
   （`_tab_node_suffix` 那个独立映射函数随之取消）。**判据与行为一字未改**，只是解析目标从 `.gd` 换 `.cs`。

3. **一个类搬 C# 会「涨行数」撞 800 行硬上限**：`.gd` 576 行 → `.cs` 822 行（大括号展开），
   当场踩 `_check_file_size`（`src/ui/ShopScreen.cs 有 822 行、超过 800 行的硬上限`）。
   拆法是**同 class 的第二个 partial 文件**：把自检整段（`_run_shop_selftest`）挪到
   `src/ui/ShopScreenSelfTest.cs`，两文件各自 696／136 行。三条连带动作缺一不可：
   ① 棘轮表（`tests/test_handshake.gd` 的 `VERIFICATION_CHECK_FLOORS`）的 key 要从
   `ShopScreen.cs` 换成 **`ShopScreenSelfTest.cs`**——`ok = ok &&` 现在在那个文件里，计数门限按文件数；
   ② 「带场景自检的文件数 = 13」那条按 `src/` 里含 `SELF-TEST: {` 的文件计数，partial 拆完仍是 13，
   不用动；③ `代码拆分清单.md` **不用动**（两文件都 ≤ 800，实测超标数仍是 16、抬头那句照样对）。
   **一般规律**：面板 `.gd` 行数 ×1.4 已过 600 的，搬之前就按职责想好拆法（自检／渲染分家最省事）。

4. **`_run_shop_selftest` 抽走不影响私有成员互访**：C# 的 `partial class` 两个文件共享成员，
   `_run_shop_selftest` 照样调 `_tree()`／`_b()`／`_i()`／`_bool()` 这些留在 `ShopScreen.cs` 的私有静态助手。

**连带改动**：`scenes/shop_screen.tscn`、`docs/dev/模块对接表.csv`（`shop_system` 行 `code_paths` 的
`src/ui/shop_screen.gd` → `src/ui/ShopScreen.cs`；`验证清单.csv` 那行没列面板、不用动）、
`tests/test_handshake.gd`（棘轮那行）、`tests/test_handshake_docs.gd`（第 33 条改读 `.cs`）。

**断言数变化**：本轮 `run_tests.bat` 落到 **8,834 条**（基线 8,803；上一批 8,833，这一批 +1 来自
模块对接表 `code_paths` 换路径后那处存在性对账）。`run_tests_cs.bat` **3,412 条**（不动）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene shop` **SHOP SELF-TEST: OK**、
`run_tests.bat` **8,834 条**（基线 8,803）、`run_tests_cs.bat` **3,412 条**、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**（余 `leaked-process` 假红）。

## 二点四一、P3 第十二批：创建角色 `CreationScreen`（2026-10-11）

`src/ui/creation_screen.gd`（682 行）→ **`src/ui/CreationScreen.cs`（456 行）＋
`src/ui/CreationScreenSections.cs`（373 行）＋ `src/ui/CreationScreenSelfTest.cs`（168 行）**
（三份都是 `public partial class CreationScreen`）；`scenes/creation_screen.tscn` 的 `ExtResource`
换成 `.cs`、旧 `.gd`／`.gd.uid` 已删。这个面板**整个界面是代码搭的**（`_build_ui()` 建 `Backdrop`／
`Margin`／`Column`／`Routes`／`Body`／`Content`／`NameEdit` 那一串），所以场景文件里只有一个根节点。

### 这一批的三件事（第 1、3 件是新形状）

1. **「该按职责拆几个文件」要在动笔前用行数估出来**：`.gd` 682 行，乘 1.4 ≈ 950 行——**必然撞 800**。
   所以不是「先写一个整的、撞了再拆」（商店那批就是这么干的，白返工一次），而是**一开始就分三份**：
   主文件＝骨架／刷新／选择／提交／助手，`…Sections.cs`＝各区段渲染与汇总，`…SelfTest.cs`＝自检。
   拆轴与商店那批同源：**自检单独一份**（棘轮表 key 也跟着落在这份上），其余按「渲染 vs 逻辑」。

2. **`Pressed += confirm;` 编译不过**（`CS0407: 返回类型错误`）——`confirm()` 返回 `Dictionary`、
   而 `Button.Pressed` 是 `Action`。凡是「返回值但只当按钮回调」的方法都要包一层
   `() => confirm()`（同族的还有 `press_qty()` 那类）。`go_back()` 这类返回 `void` 的可以直挂。

3. **GDScript 通过面板读 C# 的 `const` 会红**（坑⑩ 的又一处实例）：`tests/test_menu.gd` 原先写
   `creation.HUB_SCENE`，面板搬 C# 后这个常量从脚本 API 消失——**改读用例自己的本地常量**
   （那儿本来就有 `PLACEHOLDER_SCENE`，同一个路径）。这条与「常量跨语言」那条同源：
   **C# 的 `const` 不进脚本 API，方向单向**（C# 读 GDScript 的 `const` 可以，反过来不行）。

**连带改动**：`scenes/creation_screen.tscn`、`tests/test_handshake.gd`（棘轮那行换成
`"src/ui/CreationScreenSelfTest.cs"` / `"ok = ok &&"` / floor **15**，与旧 `.gd` 同数）、
`tests/test_menu.gd`（`creation.HUB_SCENE` → 本地 `PLACEHOLDER_SCENE`）、
`docs/dev/设计实现对照.md`（13 号那两行的 `creation_screen.gd` → `CreationScreen.cs`／`CreationScreenSections.cs`）。
`模块对接表.csv`／`验证清单.csv` 不用动（都没有列这个面板的 `code_paths`）。
`代码拆分清单.md` 不用动（三份都 ≤ 800）。

**断言数变化**：`run_tests.bat` 落到 **8,837 条**（基线 8,803；上一批 8,834，这一批 +3 来自
`设计实现对照`／`模块对接表` 那几处路径对账）。`run_tests_cs.bat` **3,412 条**（不动）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene creation` **CREATION SELF-TEST: OK**、
`run_tests.bat` **8,837 条**（基线 8,803）、`run_tests_cs.bat` **3,412 条**、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**（余 `leaked-process` 假红）。

## 二点四二、P3 第十三批：NPC 交往面板 `NpcPanel`（2026-10-11）

`src/ui/npc_panel.gd`（760 行）→ **`src/ui/NpcPanel.cs`（731 行）＋ `src/ui/NpcPanelSections.cs`（179 行）
＋ `src/ui/NpcPanelSelfTest.cs`（236 行）**（三份都是 `public partial class NpcPanel`）；
`scenes/npc_panel.tscn` 的 `ExtResource` 换成 `.cs`（那行本来就没带 `uid`，直接改 path）；
旧 `.gd`／`.gd.uid` 已删。这个面板**整个界面是代码搭的**（`Backdrop`／`Margin`／`Column`／`Head`／
`Avatar`(64×64)／`Who`／`Scroll`／`Actions`／`Status`／`BackButton`），场景文件里只有一个根节点。

### 这一批的四件事（第 2、3 件是新形状）

1. **拆几个文件照上一批的估算法**：760 × 1.4 ≈ 1060 行，必然撞 800，所以一开始就分三份——
   主文件＝骨架／`refresh()`／`_refresh_story()`／谈话段／五个动作（`gift`／`spar`／`steal`／
   `redeem`／`complete_quest`）／服务层小口与助手（**731 行，贴着上限**）；
   `…Sections.cs`＝赠送／切磋／偷窃／兑换／委托五段摆控件；`…SelfTest.cs`＝整段自检
   （**棘轮表的 key 落在自检那份**：`ok = ok &&` 有 25 条，floor 贴到 25）。

2. **`Math.Round` 的默认是银行家舍入，GDScript 的 `round()` 是「.5 往远处」**：
   好感条那 10 格是 `round(好感 / 上限 × 10)`——`5/100` 这种正好落在 `.5` 上的值，
   GDScript 给 1 格、C# 默认给 0 格（差一格，自检看不出来、玩家看得出来）。
   凡是 GDScript `round()` 搬过来，一律写全
   `System.Math.Round(x, System.MidpointRounding.AwayFromZero)`。

3. **GDScript 的运算符优先级 bug 照原样搬，别"顺手修"**：自检里那句
   `ok = ok and _favor_label.text.contains("█") or _favor_label.text.contains("░")`
   按优先级等于 `(ok and 有实心块) or 有空格`——**收紧成 `ok and (A or B)` 会改变聚合口径**。
   C# 的 `&&` 比 `||` 紧，所以**一行不改就同义**；照着写、加一句注释说明"为什么看着像 bug 却不动"。

4. **读 C# 行类有两条路，本批走强类型**：行类的字段都是 `[Export] public … { get; set; }`，
   所以 `def.Get("name_cn")` 与 `def as NpcDefRow` 都通（`person_of` 合成的同伴行也是
   `NpcDefRow` 实例）。**强类型少写一处字符串就少一处漂移**，`_person_of()`／`_favor_row_of()`／
   `_item_name()` 这几处都按行类 `as` 取；真正要"什么都没有"的地方（`available_quest` 返回空）
   才用 `as` 失败即 `null` 的写法。

### 连带改动

`scenes/npc_panel.tscn`；`tests/test_handshake.gd` 的棘轮那行（`"src/ui/npc_panel.gd"` /
`"ok = ok and"` / floor 24 → **`"src/ui/NpcPanelSelfTest.cs"` / `"ok = ok &&"` / floor 25**，
旧 `.gd` 实际也是 25 条，这次贴平）；
`docs/dev/模块对接表.csv`（`npc_system` 行 `code_paths` 的 `src/ui/npc_panel.gd` → `src/ui/NpcPanel.cs`）。
`设计实现对照.md`（那里只写 `npc_panel` 不带后缀）／`验证清单.csv`（没列这个面板的 `code_paths`）／
`代码拆分清单.md`（三份都 ≤ 800）都不用动。
**`tools/CheckLoopRunner.cs`（原 `check_loop.gd`）与两份 C# 控制器一行都没改**：`mode`／`dialogue_node_id`／`npc_id`
是 `[Export] public` 字段（GDScript 读得到、`Set` 也写得进），`return_handler`／`spar_handler`
走 `Variant` ＋ `CallableInvoker`，公开方法名一个没改。

**断言数变化**：`run_tests.bat` 落到 **8,840 条**（基线 8,803；上一批 8,837，这一批 +3 来自
`模块对接表` 路径对账）。`run_tests_cs.bat` **3,412 条**（不动）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene npc` **NPC SELF-TEST: OK**（`BACKDROP alpha=0.97`）、
`run_tests.bat` **8,840 条**（基线 8,803）、`run_tests_cs.bat` **3,412 条**、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**（余 `leaked-process` 假红）。

## 二点四三、P3 第十四批：角色面板 `CharacterScreen`（2026-10-11）

`src/ui/character_screen.gd`（1363 行）→ **`src/ui/CharacterScreen.cs`（494 行）＋
`src/ui/CharacterScreenActions.cs`（222 行）＋ `src/ui/CharacterScreenPages.cs`（723 行）＋
`src/ui/CharacterScreenSelfTest.cs`（406 行）**（四份都是 `public partial class CharacterScreen`，
都 ≤ 800 行）；`scenes/character_screen.tscn` 的 `ExtResource` 换成 `.cs`（那行本来就没带 `uid`）；
旧 `.gd`／`.gd.uid` 已删。这个面板也是**整个界面用代码搭的**（`Backdrop`／`Margin`／`Column`／
`top`／`Header`／`BackButton`／`Body`／`Portrait`(320×480)／`tabs`／`Status`），场景里只有一个根节点。

### 这一批的四件事（第 1 件是拆法，第 2、4 件是新形状）

1. **拆法**（1363 × 1.4 ≈ 1900，一开始就分四份）：主文件＝骨架／对外接口／界面搭建／环境／小工具；
   `…Actions.cs`＝写操作与查询小口（加点／穿脱／装武学／研读／丢弃／筛选）；
   `…Pages.cs`＝三个页签的重建（**控件名一个不改**，`SlotButtonweapon_0` 那种名字是对外接口）；
   `…SelfTest.cs`＝整段自检（**棘轮表的 key 落在自检那份**：`ok = ok &&` 29 条，与旧 `.gd` 同数）。

2. **导出的字段要挑对类型**：用例 `test_character_ui.gd` 直接读写 `state_override`／`back_handler`／
   `_db`（拿 `screen._db` 造装备实例），所以三处都 `[Export] public`；其中 `_db` 写成
   `[Export] public GodotObject _db;`——导出系统对 `GodotObject` 一定认得出，然后加一个强类型视图
   `private TableDb Db => _db as TableDb;`。**类内部一律用 `Db`**：`Inventory.add_item()`／
   `RecruitService.join()`／`CharacterSheet.create()` 这些签名要的是 `TableDb`，
   把 `_db` 直接传进去编译不过（本批当场报 4 处 `CS1503`）。

3. **`_make_button` 收 `System.Action`，页面里一律包一层 lambda**：
   `button.Pressed += confirm;` 在 `confirm` 返回非 void（返回 `Dictionary` 的那些写操作）时是
   `CS0407`（返回值不能当 `Action`），所以页面里全部写成 `() => confirm()`；
   循环里捕获的 `slot_id`／`index` 先拷成局部变量再进 lambda（GDScript 的 `for` 变量是逐轮新绑，
   C# 的 `foreach` 变量也是逐轮新绑、但 `for` 的不是——`string sid = slot_id; int idx = index;`）。

4. **值类型上的 `?.` 是合法的**：`((EquipSlotDefRow)Db.get_row("equip_slot_def", slot_id))?.max_equip ?? 1`
   编译得过、语义也正是"没这行就给 1"（`?.` 作用在引用类型的行对象上，`max_equip` 是 `int` →
   整个表达式是 `int?`，`?? 1` 收口）。**不必**为了"值类型不能 `?.`"先拆成两行——那不是这个形状。

### 连带改动

`scenes/character_screen.tscn`；`tests/test_handshake.gd` 的棘轮那行（`"src/ui/character_screen.gd"` /
`"ok = ok and"` / floor 29 → **`"src/ui/CharacterScreenSelfTest.cs"` / `"ok = ok &&"` / floor 29**）；
`docs/dev/模块对接表.csv`（`character_ui` 行 `code_paths` 的 `src/ui/character_screen.gd` →
`src/ui/CharacterScreen.cs`）；`docs/dev/代码拆分清单.md`（`1363 | src/ui/character_screen.gd` 那行划掉，
抬头 **16 个超标 → 15 个**）；`tools/run_tests.gd` 的 `MIN_ASSERTIONS` **8803 → 8843**（见下）。
`设计实现对照.md`／`验证清单.csv` 不用动（前者只写 `character_screen` 不带后缀；后者的
`character_ui.verify_paths` 指的是 `tests/test_character_ui.gd`，那份用例仍是 GDScript、仍在旧链上）。

**第二处「门限只扫 `.gd`」**（第一处是 §二点四二 的 `SINGLE_SOURCE_RULES`）：
`tests/test_handshake_docs.gd::_check_code_row_literals_exist`——"代码按字面量查表，那一行必须真的在表里"
——原来 `if not path.ends_with(".gd"): continue`，于是**搬走一个面板就少算一处调用点**，
而它的下限是 `check_gt(found, 3.0)`：本批把 `character_screen.gd` 里那处
`get_row("equip_slot_def", "weapon")` 带走后，扫描面从 4 掉到 3，**当场红**
（`[FAIL] 扫到足够多的「字面量查表」调用点（3 处）`）。改法：`.gd` 与 `.cs` 都扫
（4 → 13 处），下限抬到 `> 10`——那一条是"正则还抓得住东西"的哨兵，不是覆盖率指标。
**这是迁移期一类系统性风险**：凡是"按文件数／调用点数下界"的门限，扫面收窄时会静默变成假门限，
而扫面**变宽**时才当场红。搬 `src/core` 剩下的 10 个时要专门盯这一类
（§二点七 那张清单里已记 `SINGLE_SOURCE_RULES`，本批再记一条）。

**断言数变化**：`run_tests.bat` **8,853 条**（基线 8,843；其中 +9 来自上面那条门限扩到 `.cs`，
−1 来自 `代码拆分清单.md` 少一行、`_check_file_size` 的逐行断言随之少一条，另外两份交接文档
各自带来 1 条随文件增删的断言）。
`run_tests_cs.bat` **3,412 条**（不动）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene character` **CHARACTER SELF-TEST: OK**
（`LAYOUT min 792×627`／`BACKDROP alpha=0.97`／`CONTENT 614`）、`run_tests.bat` **8,853 条**、
`run_tests_cs.bat` **3,412 条**、`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**（余 `leaked-process` 假红）。

## 二点四四、P3 第十五批：战斗界面 `BattleScreen`（2026-10-11）

`src/ui/battle_screen.gd`（2073 行，`src/ui` 的**最后一个**面板 `.gd`）→ **七份 `partial class`**：
`src/ui/BattleScreen.cs`（710）＋ `BattleScreenCommands.cs`（480）＋ `BattleScreenFloats.cs`（275）
＋ `BattleScreenSettle.cs`（497）＋ `BattleScreenView.cs`（337）＋ `BattleScreenActors.cs`（449）
＋ `BattleScreenSelfTest.cs`（552）（都 ≤ 800 行）；`scenes/battle_screen.tscn` 的 `ExtResource`
换成 `.cs`（那行本来就没带 `uid`）；旧 `.gd`／`.gd.uid` 已删。
拆法：主文件＝骨架／常量／环境／浮层／造敌人／小工具；`…Commands`＝六指令＋回合推进＋目标选择；
`…Floats`＝跳字／受击／出手表现＋自动战斗／速度／策略／撤退／返回；`…Settle`＝结算＋写回气血／败北／
清怪记录＋旗标；`…View`＝`_bind_ui`／`_refresh` 一族；`…Actors`＝行动者行／装备行／状态行／架势条；
`…SelfTest`＝整段自检（棘轮表的 key 落在自检那份：`ok = ok &&` **24** 条）。

### 这一批踩到的坑

1. **`Variant` 是值类型，不能 `??`**：`GodotObject?.Get(...) ?? default` 直接 `CS0019`。
2. **`Call("x").AsGodotArray()` 后面不能直接链 `.Count`**（GD0001）——必须先落到局部变量
   `Godot.Collections.Array a = …Call("x").AsGodotArray();` 再取 `.Count`。
3. **`Array.Slice()` 返回的是 `Variant[]`、不是 `Godot.Collections.Array`**：要 `Array` 就自己写
   `_slice()` 助手（本批在 `…Settle` 里加了），别指望 `.Slice()`。
4. **GDScript 读不到 C# 的 `const`／静态 `readonly`**（坑⑩）：被 GDScript 读到的常量都配一个同名小写
   静态取值口（`auto_interval()`／`selftest_seed()`／`team_win_flags()`…）；`tests/test_recruit.gd`
   原来写 `BattleScreenScript.TEAM_WIN_FLAGS.values()`，搬完必须改成 `team_win_flags().values()`。
5. `Godot.Collections.Array` **有** `AddRange<t>(IEnumerable<t>)`（实测可用），别绕。
6. **拆成多份 `partial` 之后，"按文件数下界"的门限会静默缩窄**（本批两处，见下）——
   任何"只读场景指向的那一份源码"的扫描都要按类名把同类的分片收全。

### 连带改动

`scenes/battle_screen.tscn`；`tests/test_battle_ui.gd`／`tests/test_recruit.gd` 的
`preload("…battle_screen.gd")` → `preload("…BattleScreen.cs")`（后者的 `TEAM_WIN_FLAGS` 取值改走
`team_win_flags()`）；`tests/test_handshake.gd` 的棘轮那行
（`"src/ui/battle_screen.gd"` / `"ok = ok and"` / floor 23 → `"src/ui/BattleScreenSelfTest.cs"` /
`"ok = ok &&"` / floor **24**，源文件实测 24 条，原 floor 是旧的）；
`docs/dev/模块对接表.csv`（`battle_ui` 行 `code_paths` 的 `src/ui/battle_screen.gd` →
`src/ui/BattleScreen.cs`）；`docs/dev/代码拆分清单.md`（`2073 | src/ui/battle_screen.gd` 那行划掉，
抬头 **15 个超标 → 14 个**）；`tools/run_tests.gd` 的 `MIN_ASSERTIONS` **8843 → 8852**；
`tools/analysis_mutations_code.ps1`（7 条 cast 的 `Find`/`Repl` 改指 `.cs` ＋ `$intentionallyUnpinned`
补 7 条豁免，成对）。

**第三处「门限只扫 `.gd`」**（前两处是 §二点四二 的 `SINGLE_SOURCE_RULES`、§二点四三 的
`_check_code_row_literals_exist`）：`tests/test_handshake_repo.gd::_check_font_size_tiers`
（UI 字号只有两档 12／24）原来 `if not path.ends_with(".tscn") and not path.ends_with(".gd")`，
搬走一个面板扫描面就从 10 掉到 8、`check_gt(10)` 当场红；改法：**`.tscn`／`.gd`／`.cs` 都扫**。
`.cs` 的字号写法（`AddThemeFontSizeOverride("font_size", 12)` 与 `int font_size = 0` 形参默认值）
正好吃同一条正则。

**第四处**（同批）：`tests/test_handshake_code.gd::_check_scene_node_contracts`（场景里的节点名必须
真实存在）原来只读场景 `ExtResource` 指向的**那一份**源码——`BattleScreen` 拆成七份后，
20 处 `_require_node("…")` 全在 `BattleScreenView.cs`，主文件一处没有，覆盖数 **49 → 29**、
`check_gt(30)` 当场红。改法：按主文件的 `partial class <名>` 把 `src/ui` 下**同一类的其它 `.cs`
分片一起收进来**（新助手 `_collect_partial_cs`），"拆文件"不再等于"少检查"。

### 必须重点核对、容易翻的行为差异

- `_refresh_buttons()`：`done = sim != null && _sim_finished()`（**不能**用 `_sim_finished()`——它在
  `sim == null` 时返回 true，会让「还没开局」的指令按钮被禁掉）；逃跑按钮 `Disabled = reason.Length != 0`
  （reason 是 `flee_block_reason`，非空＝不能逃）——**本批第一轮把这处写反了**（`== 0`），
  `test_battle_ui` 的「开打前置灰／轮到我方可按」两条正反都红，已改回 `!= 0`。
- `_settle`：`session_node.Set("pending_encounter", default(Variant))`；`last_battle` 的 `kill_styles`
  直接放 `sim.Call("kill_styles")`。
- `_mark_spawn_cleared`：`cleared[Enc.source_key] = respawn <= 0 ? -1 : (int)Time.GetUnixTimeFromSystem() + respawn`。
- `_describe_drops_card` 的截断、`_spawn_floats` 的 `Float_{target}_{hit}` 形状、`_make_effect_chip` 的
  文案拼法都按原 GDScript 一比一保留。

**断言数变化**：`run_tests.bat` **8,866 条**（基线 8,843）。增量主要来自两处扫描面扩到 `.cs`——
字号那处多扫进去的每一处声明各算一条，开发文档里 `tools/…` 的引用也各算一条（`_check_doc_tool_references`）；
另有棘轮一条、`代码拆分清单.md` 少一行 −1。
`run_tests_cs.bat` **3,412 条**（不动）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene battle` **BATTLE SELF-TEST: OK**
（`LAYOUT min 760×541`／`CONTENT 352`／结算与「长文案／满招式／带状态」三档全 ok）、
`run_tests.bat` **8,862 条**、`run_tests_cs.bat` **3,412 条**、`validate_tables.ps1` **PASSED**、
`check_loop.bat` **OK**、`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**
（余 `leaked-process` 假红）。

## 二点四五、P3 第十六批：音效层 `Sfx` ＋ 行基类旧壳（2026-10-11）

两份小件一起收：

- `src/audio/sfx.gd`（197 行）→ **`src/audio/Sfx.cs`**（`public partial class Sfx : RefCounted`，
  `namespace Lunhui.Audio`；**成员名保持 snake_case**——GDScript 侧是直接点名字调的），
  旧 `.gd`／`.gd.uid` 已删。**`src/audio/` 下已无 `.gd`**。
- `src/data/table_row.gd`（15 行）→ 删。它只剩一个 `class_name TableRow` 的空壳，
  `src/data/TableRow.cs` 早就是 C# 版（§二点九就搬过）——**`src/data/` 下已无 `.gd`**。

### 这一批踩到的坑

1. **`Set-Content -NoNewline` 会把整份文件的换行吞掉**（本批最贵的一次事故）：
   用它改 `tests/TestLocalMap.cs` 时，107 KB 的文件被写成**一行**（LF=0），而该文件**未入册**
   （`git status` 里是 `??`），**从 git 恢复不了**。还原办法（已验证、可复用）：从会话日志
   `~/.codex/sessions/…/<线程 id>.jsonl` 里按时间顺序**重放**对该文件的 `apply_patch`
   （`custom_tool_call` 的 `payload.input`）与 `exec_command`（`payload.arguments` 是 JSON 串）；
   `*** Add File` 的 `+` 行建底、每个 `*** Update File` 按 `@@` 分块做 before/after 替换，
   穿插的 `ReadAllText`＋`Replace`＋`WriteAllText` 也要重放。校验口径：把重建文本剥掉 `\r\n`
   后与损坏文件逐字符 `-ceq` 比，命中即对（本次 110393 vs 107852 字符、`MATCH=true`）。
   ——**教训：写代码文件一律 `apply_patch`；要批量文本替换就自己保住换行**。
2. **`AudioServer.SetBusVolumeDb` 的实参要 `(float)`**：C# 侧 `db` 是 `double`，不转就编不过。
3. **`Array.Slice()` 返回 `Variant[]` 不是 `Godot.Collections.Array`**（与 §二点四四 第 3 条同源）：
   `Sfx.play` 里要把变参收成 `Godot.Collections.Array`，自己写循环收口，别指望 `.Slice()`。
4. **GDScript 读不到 C# 的 `const`／静态 `readonly`**（坑⑩的又一次）：`EVENTS`／`SOUND_BUS`
   各配一个同名小写静态取值口 `events()`／`sound_bus()`，`test_audio.gd`／`test_settings.gd`
   的 `SfxScript.EVENTS`／`SOUND_BUS` 改调这两个口。
5. **`const` 不能拿 C# 静态调用的结果去赋**（坑 ㊺的又一次）：`PlaceholderGame.press_volume`
   里那处必须留 `double value = Sfx.cycle_volume();` 再 `return value`，写成 `const` 当场红。

### 连带改动

播放层仍是 GDScript 的 `addons/sound_manager/`（不改），`Sfx` 通过
`GD.Load<GDScript>(…).New().As<Node>()` ＋ `Call("play_sound"/"set_sound_volume")` 调它。
调用点全部改直调（不再走 `_sfx_script()`／`SFX_SCRIPT` 常量，那批常量连缓存一起删）：
`src/ui/BattleScreenSettle.cs`／`BattleScreenFloats.cs`（`Sfx.play(...)`）、
`src/ui/PlaceholderGame.cs`（`Sfx.cycle_volume()`／`Sfx.volume_text()`）、
`src/world/LocalMapController.cs`（`Sfx.play("chest"/"brazier")`，加 `using Lunhui.Audio;`）、
`tests/TestLocalMap.cs`（`Sfx.clear_log()`／`Sfx.has_played(...)`）。
GDScript 侧 `preload` 换 `res://src/audio/Sfx.cs`：`tools/run_tests.gd`、`tests/test_battle_ui.gd`、
`tests/test_audio.gd`、`tests/test_settings.gd`（两处）。
`tests/test_handshake.gd` 白名单删掉音效那四条（`played_events`／`has_played`／`clear_log`／
`shutdown`，**P5 给「只被测试调用的接口」加 `.cs` 正则时再加回来**）与 `get_id`
（行基类现在只有属性 `id`，同理 P5 再补口）。
变异探针 `tools/analysis_mutations_code.ps1` 那条「音效事件指向不存在的文件」改指
`src\audio\Sfx.cs`（`Find = '{ "hit", "res://assets/audio/sfx/sfx_hit.wav" },'`），
`$intentionallyUnpinned` 补第 45 条豁免（改 `.cs` 不重编 → 量不出来，绝对值由新用例钉）。
文档指针换 `.cs`：`docs/dev/模块对接表.csv`（`audio_system.code_paths` 2 处）、
`src/audio/AGENTS.md`、`docs/dev/框架说明.md`（2 处）、`docs/dev/第三方代码.md`、
`tools/audio/make_placeholders.py`。

**断言数变化**：`run_tests.bat` **8,862 条**（基线 8,852；比上一批低是因为旧 `.gd` 的
逐行断言随文件一起退场、其余是文档引用增减相抵）。`run_tests_cs.bat` **3,412 条**（不动）。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests.bat` **8,862 条**、
`run_tests_cs.bat` **3,412 条**、`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、
`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**
（余 `leaked-process` ＝常驻的 MCP 编辑器实例，假红）。

## 二点四六、P3 第十七批：两个 autoload ＋ 引导场景（2026-10-11）

三份一起搬（**必须同批**：`Bootstrap` 要读 `GameData`，而 `project.godot` 的 autoload 一改就是运行期入口）：

- `src/autoload/game_data.gd`（70 行）→ **`src/autoload/GameData.cs`**（`namespace Lunhui.Autoload`）；
- `src/autoload/game_session.gd`（153 行）→ **`src/autoload/GameSession.cs`**；
- `src/bootstrap.gd`（167 行）→ **`src/Bootstrap.cs`**（`namespace Lunhui`；类名＝文件名，Godot 的 C# 源生成器要求）。

`project.godot` 的两行 autoload 换成 `*res://src/autoload/GameData.cs`／`GameSession.cs`，
`scenes/bootstrap.tscn` 的 `ExtResource` 换 `.cs`，三份旧 `.gd`／`.gd.uid` 已删。
**`src/autoload/` 与 `src/` 根目录下已无 `.gd`**（`src/` 只剩 `src/core` 10 个 ＋ `src/ui/callable_invoker.gd`）。

**接口形状**：`GameSession` 的 15 个成员全部 `[Export] public` 且**名字保持 snake_case**
（GDScript 侧 `session.pending_local_scene = …` 这种写法一行都不用改）；`state`／`pending_encounter`
收成 `GodotObject`／`Variant`（`GameState` 还没搬，跨语言口先不收紧）；
`GameData` 的 `db` 同理（内部走 `private TableDb Db => db as TableDb` 强类型视图）。

### 这一批踩到的坑

1. **`Dictionary.Keys` 是 `ICollection<Variant>`，不是 `Godot.Collections.Array`**（`CS0266`）——
   照 GDScript 的 `for k in dict` 翻成 `var keys = dict.Keys; keys[i]` 编不过，直接 `foreach (Variant key in dict.Keys)`。
2. **`Variant` 上不能直接 `.Get()`／`.Call()`**：`Call(...)` 的返回值要先用 `.AsGodotObject()` 收成
   `GodotObject` 再取属性（本批 `Bootstrap` 里 `from_character` 那处报了 6 条 `CS1061`）。
3. **C# 的默认参数对 GDScript／`Call` 不生效**（坑⑲的第二次实测，本批三处）：
   `active_field_buffs(now = -1)`／`add_field_buff(buff_id, minutes, source_id = "", max_stack = 1)`／
   `set_world_position(pos, fallback = null)`——**13 处调用点全部写满实参**（`.gd` 6 处、`.cs` 7 处），
   漏一处就是 `Invalid call. Nonexistent function 'active_field_buffs'`（第一次跑就是这么红的）。
4. **`signal reloaded` 零订阅者，随搬迁退场**：`rg -n "reloaded"` 全仓只命中 `game_data.gd` 自己的
   定义与 `emit`（其余同名命中都是别的局部变量）。**要留接口就留 C# 信号，但它的名字是 `Reloaded`**
   （C# 信号名不做 snake_case 转换，坑 82）——为了让"名字悄悄变掉"这件事不发生，宁可不留。
5. **第五处「扫面收窄 → 假门限」**：`test_handshake_code::_check_no_absolute_autoload_paths` 的
   `sanctioned` 只数 `.gd` 里的 `Engine.get_main_loop` ——`bootstrap.gd` 是**最后一处**，
   它一搬走这个数掉到 0、`check_gt(0)` 当场红。改法：**两种语言一起收**
   （C# 写法 `Engine.GetMainLoop()`，绝对路径 `GetNode("/root…")`／`GetNodeOrNull("/root…")`），
   下限 0 → 10（实测 15 处）。前四处见 §二点四三／二点四四。
6. **反过来的坑：`TEST_ONLY_API_ALLOWED` 的 `has_state` 不能删**。按前几批的惯例（搬走的接口
   从白名单里划掉）我删了一次，**run_tests 当场红**：`新增了「游戏路径从不调用」的接口：has_state
   （src/autoload/GameSession.cs）`——说明**这道门的 C# 正则认得出 `public bool has_state()`**
   （只认不出 `public static`，见坑⑦）。删除动作已回退，白名单那条留着并加了一句注。

### 连带改动

`project.godot`（2 行 autoload）；`scenes/bootstrap.tscn`；`tests/test_handshake.gd`
（棘轮表 `"src/bootstrap.gd"`／`ok = ok and`／floor 6 → `"src/Bootstrap.cs"`／`ok = ok &&`／floor 6；
白名单 `has_state` 加注）；`tests/test_handshake_code.gd`（autoload 四条断言换 `.cs` ＋ 上面第 5 条）；
调用面 13 处写满实参（`tools/CheckLoopRunner.cs`、`tests/test_buff.gd`、`tests/test_battle_ui.gd`、
`tests/test_save_migration.gd`、`tests/TestOverworld.cs`、`tests/TestLocalMap.cs`、
`src/ui/BattleScreen.cs`、`src/ui/CultivateScreen.cs`、`src/world/LocalMapController.cs`、
`src/world/OverworldController.cs`）；两份用例改 `load("…/GameSession.cs")`
（`tests/test_buff.gd`／`tests/test_save_migration.gd` 共 4 处）；
`docs/dev/模块对接表.csv` 的 `main_menu`／`save_system` 两行 `code_paths` 换 `.cs`；
`AGENTS.md` 的目录表一行；六个面板的 C# 头注释里「两个 autoload 还是 GDScript」这句改掉
（`MainMenu`／`ClueScreen`／`CreationScreen`／`PlaceholderGame`／`CharacterScreen`／`CultivateScreen`）。
变异探针里**没有**这三份的 cast（`rg bootstrap|game_session` 无命中），所以没动豁免表。

**断言数变化**：`run_tests.bat` **8,862 条**（基线 8,852 不动——搬走的 `.gd` 函数不在逐行断言里，
新增的两条门限改动与文档引用增减相抵）。`run_tests_cs.bat` **3,412 条**（不动）。

**本轮验收**：`dotnet build` 0 错 0 警、`check_scene bootstrap` **BOOTSTRAP SELF-TEST: OK**、
`run_tests.bat` **8,862 条**、`run_tests_cs.bat` **3,412 条**、`validate_tables.ps1` **PASSED**、
`check_loop.bat` **OK**、`run_windowed_smoke.bat` **13/13 PASSED**、`run_all_checks.bat` **18/19**
（余 `leaked-process` ＝常驻 MCP 编辑器实例，假红）。

## 二点四七、core 第二十三批：`game_state`（拆五份，2026-10-11）

`src/core/game_state.gd`（1060 行，撞 800 行硬上限）→ 拆成**五份 partial**（同批落，`SAVE_SERIALIZATION_TARGETS`
的扫描面一起收）：

- **`src/core/GameState.cs`**（字段 ＋ 常量 ＋ `new_game` ＋ 起始装备／武学种子 ＋ `add_character`／
  `_prune_unknown_characters`／`_strip_character`）；
- `src/core/GameStateSkills.cs`（加点 ＋ 武学：`spend_point`／`learn_skill`／`set_loadout`／`set_mastery`…）；
- `src/core/GameStateText.cs`（默认队伍／世界坐标／等级／称呼／`summary_lines`／`format_time`／`touch_saved`）；
- `src/core/GameStateWorld.cs`（商店／事件与旗标／首杀／战斗外气血／揭开／副本／保底）；
- `src/core/GameStateStorage.cs`（`to_dict`／`is_valid_dict`／`from_dict` ＋ 全部 `_read_*` ＋ `apply_custom_templates`）。

**接口形状**：常量 `VERSION`／`MAX_PARTY`／`DEFAULT_CHAPTER` 各配一个小写静态取值口
（`save_version()`／`max_party()`／`default_chapter()`——`version()` 会和字段 `version` 撞名，故用 `save_version()`），
GDScript 侧读不到 C# 的 `const`／`public static` 定义，一律走取值口；**所有字段改成
`[Export] public t 名字 { get; set; }` 自动属性**（裸字段 `public t x;` 会被存档字段门限扫不到，
这是本批把 30 个字段全改自动属性的原因）。

### 这一批踩到的坑

1. **`GD.Load<GDScript>("…/GameState.cs")` 当场抛 `Unable to cast object of type 'Godot.CSharpScript'
   to type 'Godot.GDScript'`**——面板／用例／`SaveStore`／`CreationService` 原来都借这一条件（`GameState`
   还是 `.gd` 时是对的），本批**22 个 C# 文件**改成 C# 直调（`GameState.new_game(…)` 等）；`state.Call(…)`
   那种「`state` 是跨语言句柄」的写法保留，但**实参写满**。判据是真实运行（`run_tests_cs` 42 条红：
   18 个用例都是这条异常 ＋ 连带"假绿"）。
2. **C# 的默认参数对 `Call` 不生效**（坑⑲第三次实测，本批五处）：`new_game(db, difficulty, char_ids, slot)`／
   `set_flag(flag_id, value)`／`push_buyback(building_id, record, limit)`／`append_level(char_id, amount)`／
   `set_mastery(char_id, skill_id, level, max_level)`——**调用点全部写满实参**（本批 38 处 `set_flag` 批量补
   `, true`，`new_game` 一批补空队伍与 `0`）。
3. **`TEST_ONLY_API_ALLOWED` 出现一次"假红"**：`shop_state_of`／`has_first_kill`／`first_kill_count` 在
   **新旧两份文件里各有一处定义**（`game_state.gd` ＋ `GameStateWorld.cs`）时，调用方计数被定义本身凑到
   ≥2，门限误判成"已接进游戏路径"让我们删白名单；**删掉 `game_state.gd` 后再跑又反过来**（0 调用方 →
   报"从不调用"）。**口径：这三条本来就只被用例调，白名单保留**。以后"旧定义搬走"会改这道门的判据，
   搬完要**再跑一次**确认不是假信号。
4. **门限扫描面收窄（第 7 处）**：`_check_save_fields_are_serialized` 原来只在 `target["path"]` 一个文件里
   找 `to_dict()`，`GameState` 拆五份后 `to_dict()` 落在 `GameStateStorage.cs`，**当场红**。改法：新增一个
   `scan`＝主文件 ＋ `also` 几份 partial，`to_dict` 抽取与"孤儿键"反向检查都改扫 `scan`（`also` 的**字段**
   声明上一批已加，本批补 `to_dict` 这一半）。

### 连带改动

53 个文件的 `res://src/core/game_state.gd` → `GameState.cs` 批量替换（含 BOM 保持）；`tests/test_handshake.gd`
（`SAVE_SERIALIZATION_TARGETS` 那条换 `.cs` ＋ `also` 四份 ＋ 白名单回填）；`tests/test_handshake_code.gd`
（`scan` 收面 ＋ `also` 支持）；22 个 C# 调用点改直调（10 面板 ＋ `CreationService`／`SaveStore`／
`LocalMapController`／`OverworldController` ＋ 8 份用例）；`docs/dev/代码拆分清单.md`（划掉那一行，14 → 13）；
`docs/dev/模块对接表.csv` 2 处 `code_paths` 换 `.cs`；`src/core/game_state.gd` ＋ `.gd.uid` 已删。

**断言数变化**：`run_tests.bat` **8,873 条**（棘轮 8,852 不动——搬走的 `.gd` 函数不在逐行断言里）。
`run_tests_cs.bat` **3,412 条**（不动）。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests.bat` **8,873 条**、`run_tests_cs.bat` **3,412 条**、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、`run_windowed_smoke.bat` **13/13 PASSED**、
`run_all_checks.bat` **18/19**（余 `leaked-process` ＝常驻编辑器实例 PID 26892，假红）。

## 二点四八、core 第二十四批：`damage_resolver` → `DamageResolver.cs`（2026-10-11）

`src/core/damage_resolver.gd`（421 行，没撞 800 行上限）→ `src/core/DamageResolver.cs` 一份：

- **接口形状**：构造走 `public static DamageResolver create(TableDb db, GodotObject rng)`——静态工厂不能叫 `new`，
  原 `.new(db, rng)` 调用点一律改 `.create(...)`；成员名保持 snake_case
  （`resolve`／`resolve_dot`／`stat_of`／`constant`／`hit_chance`／`defense_reduction`…），
  对外方法参数**写满**（`stat_of(actor, stat_id, fallback)`／`constant(const_id, fallback)`）。
- **常量**：`HIT_CHANCE_MIN`／`HIT_CHANCE_MAX`／`DEFAULT_CRIT_DMG` 改成 `public const double`，各配一个小写静态
  取值口（`default_crit_dmg()`）——GDScript 读不到 C# 的 `const`，`test_damage.gd` 原来直接读
  `DEFAULT_CRIT_DMG`，改走取值口。`test_handshake_docs.gd` 的战斗常数扫描口径不变（`constant("id"` 仍是 5 处）。

### 这一批踩到的坑

1. **C# 的默认参数对 `Call`／跨语言调用不生效**（坑⑲第四次实测）：原签名 `resolve_dot(attacker, defender,
   skill_row, modifiers, dot_type_id = "")` 在 GDScript 里可以只传 4 个实参，C# 版必须 5 个都写满——
   `tests/test_damage.gd` 那处少传第 5 个，当场红成
   `Invalid call. Nonexistent function 'resolve_dot' in base 'RefCounted (DamageResolver.cs)'`（行号直接点出 325 行）。
   **改法是调用点补 `""`**，不是给 C# 加 `[Variant]` 兜底；`battle_simulator.gd` 那处本来就传 5 个实参。
2. **变异探针只认 `.gd`**（P4 待办）：5 条「改战斗／成长常数」的 cast 指回 `.cs` 之后，
   `--quit` 那条编译路径不会重编 C#、探针量不出来，本批先把这 5 条写进
   `tools/analysis_mutations_code.ps1` 的 `$intentionallyUnpinned`（附理由），等 P4 给探针加 `.cs`
   支持再逐条删掉。

### 连带改动

5 个文件的 `preload("res://src/core/damage_resolver.gd")` → `DamageResolver.cs` 与
`DamageResolverScript.new(` → `.create(`（`battle_simulator.gd`／`test_damage.gd`／`test_skill.gd`／
`test_status.gd`／`test_talent.gd`）；`battle_simulator.gd` 的 `stat_of(...)` 补第三参；
`test_damage.gd` 的 `DamageResolverScript.DEFAULT_CRIT_DMG` → `default_crit_dmg()`；`test_talent.gd`
的语料路径与 `test_handshake_docs.gd` 的扫描 key 换 `.cs`；`docs/dev/模块对接表.csv` 的
`damage_pipeline.code_paths` 换 `.cs`；`docs/dev/框架说明.md` 第 524 行散文指针换 `DamageResolver.cs`；
`docs/dev/设计实现对照.md` 两处散文指针换 `DamageResolver`；`tools/validate_tables.ps1` 一处注释指针换
`DamageResolver.cs`；`src/core/damage_resolver.gd` ＋ `.gd.uid` 已删。

**断言数变化**：`run_tests.bat` **8,875 条**（棘轮 8,852 不动——余量 23，没到 `SLACK_LIMIT` 100）。
`run_tests_cs.bat` **3,412 条**（不动）。

**本轮验收**：`dotnet build` 0 错 0 警、`run_tests.bat` **8,875 条**、`run_tests_cs.bat` **3,412 条**、
`validate_tables.ps1` **PASSED**、`check_loop.bat` **OK**、`run_windowed_smoke.bat` **13/13 PASSED**、
`run_all_checks.bat` **18/19**（余 `leaked-process` ＝常驻编辑器实例 PID 26892，假红）。

## 二点四九、core 第二十五批：`battle_actor` → 三份 `BattleActor*.cs` ＋ 一次「防损恢复」（2026-10-11）

**搬迁**：`src/core/battle_actor.gd`（796 行）按职责拆三份 `partial`（`namespace Lunhui.Core`、
`public partial class BattleActor : RefCounted`）：`BattleActor.cs`（常量／`[Export]` 字段／架势／资源／
`stat`／`summary`／两个工厂）、`BattleActorStatus.cs`（两个规则字典 ＋ 防御乘数／内力回复）、
`BattleActorBuffs.cs`（buff 与属性重算）。22 处调用点／文件同步改：`preload(…battle_actor.gd)` → `BattleActor.cs`、
`BattleActorScript.SIDE_ALLY` → `side_ally()`、`from_character` 补满五参。
`src/core` 剩 **7 个 `.gd`**（`battle_simulator`／`npc_service`／`clue_service`／`event_check_service`／
`shop_service`／`world_map_service`／`party_builder`）。

**两条实测结论（写死，别再试）**：
1. **实例方法的重载能被 GDScript 按元数分派**——生成的 `_ScriptMethods.generated.cs` 里有 `args.Count == N` 分支，
   `stat`／`add_buff`／`add_status` 都真跑通了。
2. **静态方法的重载不行**——`preload("…/BattleActor.cs")` 之后静态调 `from_character(db, row)` 在**解析期**就报
   `Parse Error: Static function "from_character()" not found in base "CSharpScript"`。所以静态工厂**只留一条签名**，
   调用点把实参写满（`…, level, {}, []`）。

`KNOWN_STATUS_RULES`／`DECLARED_STATUS_RULES` 用 `public static readonly`（GDScript 读不到 `const`，照旧配小写取值口）；
`[Export] public GodotObject _db { get; set; }` 的注册名就是 `_db`，GDScript 直接赋值没问题，类内 `_db as TableDb`。

**同轮事故与恢复**：这批顺带做的「散文指针替换」（把 7 份文件里的 `battle_actor.gd` 之类改写成 `BattleActor.cs`）
脚本写坏了文件——

- `src/core/BuffService.cs`／`tests/TestStatusRules.cs`／`src/core/battle_simulator.gd`／`tests/test_table_pipeline.gd`
  被**逐字符 `b`/`B` → `a`**（`public`→`pualic`、`buff`→`auff`）；判定特征就是**整份文件一个 `b`/`B` 都没有**。
- `docs/dev/设计实现对照.md` 的 Markdown 表格被拍平成空格列（`|` 全丢、每行只剩「列1  列2  列3」）。

恢复办法（记下来，下次别再慌）：

1. **有了会话记录，`.cs` 与新增文件都能逐字节还原**：`~/.codex/sessions/**/*.jsonl` 里按
   `Add File: <绝对路径>` 搜（补丁用的是**绝对路径**，且 JSON 里反斜杠是双写的），取出那次 `apply_patch` 的原文、
   按 `+` 行拼回文件；再用「原文把 `b`/`B` 换成 `a`」与原文件比对，**相等即完全复原**。
2. **`.gd` 用「词汇表 ＋ `a`→`b` 逆替换」**：词汇表取自全仓库 `.gd`/`.cs` 与所有补丁文本，
   **必须排除被污染的几份**（否则坏词本身进了词汇表，逆替换会把它当合法词放过）；修完与 `HEAD` 逐行对 diff，
   只剩已知的迁移改动就说明对了。
3. `设计实现对照.md` 按 **`HEAD` 原文 ＋ 指针改名 ＋ 几处内容更新**重建（表格结构以 `HEAD` 为准）。

**两个坑**：

- **单字母参数会与已有的 `a` 撞名**：`func(a, b)` 里的 `b` 变成 `a` 后与真 `a` 无法区分，逆替换不会碰它——
  `battle_simulator.gd` 的 `sort_custom` 闭包、`test_table_pipeline.gd` 的局部变量 `db`（48 处）都得手工或按词边界收。
- **PowerShell 里 `return @($str, $List[int])` 会被展开**（拿到的是「字符串 ＋ int 序列」，`$map[$i]` 全错），
  位置级替换要内联写，别塞进函数。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **8880 条 OK**；`run_tests_cs.bat` **3412 条 OK**；
`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点五〇、core 第二十六批：`battle_simulator` → 五份 `BattleSimulator*.cs`（2026-10-11）

**搬迁**：`src/core/battle_simulator.gd`（1524 行）按职责拆五份 `partial`（`namespace Lunhui.Core`、
`public partial class BattleSimulator : RefCounted`）：

- `BattleSimulator.cs`（703 行）——常量／取值口／字段／构造与 `create`／`setup`／回合管线
  （`begin_round`／`current_actor`／`step_round`／`finished`／`winner`／`result`）／出手顺序／BuffService 共用一份；
- `BattleSimulatorSelect.cs`（417）——可用招式与体检表、自动选招与打分、武器与伤害类型门槛、策略三件套、目标选择；
- `BattleSimulatorActions.cs`（610）——六指令（招式／普攻／内功／道具／主动防御／逃跑）＋预兆与拆招＋`_spend_action`；
- `BattleSimulatorSettle.cs`（534）——一次行动结算、命中／施放触发、异常状态、斩杀、回合末 buff 与 DoT、修正袋；
- `BattleSimulatorProgress.cs`（133）——冷却、击杀方式、招式使用次数，以及取值／切片／拼数组那组共用小助手。

**为何拆五份而不是四份**：第一版按「四份」切，主壳落到 **823 行**，撞上「单文件硬上限 800 行」——
于是把「冷却与战斗过程记录 ＋ 共用小工具」再抽一份，主壳回到 703 行。五份全部 <800，
`docs/dev/代码拆分清单.md` 划掉 `battle_simulator.gd` 那行、抬头改成「12 个超标」。

**调用面同步（~20 个文件）**：

- **C# 侧**：`src/ui/BattleScreen.cs` 删掉 `_battle_simulator_script()`／`_sim_constants()`（`GetScriptConstantMap`），
  五个常量取值口改直调 `BattleSimulator.winner_ally()` 之类，`sim` 用 `BattleSimulator.create((TableDb)db, …)` 造；
  `src/core/TableValidatorActors.cs` 的 `ScriptFor(BATTLE_SIMULATOR_SCRIPT).Call("is_supported_damage_type", …)`
  改成直调静态 `BattleSimulator.is_supported_damage_type(db, type_id)`（构建期校验与运行期过滤共用同一份名单不变）。
  `sim` 仍是 `[Export] public GodotObject`，UI 那些 `sim.Call(...)` 一行不动（**实测 C# 的 public 实例方法都能被 `Call`**，
  含返回 `Array`／`Dictionary` 的——生成的 `_ScriptMethods.generated.cs` 里带着对应的 Variant 返回类型）。
- **GDScript 侧**：15 份用例 ＋ `tools/analysis_perf.gd`／`analysis_balance.gd` 的
  `preload(…battle_simulator.gd)` → `…BattleSimulator.cs`、`BattleSimulatorScript.new(` → `.create(`、
  17 个常量读取改小写静态取值口（`MAX_ROUNDS`→`max_rounds()`、`WINNER_*`→`winner_*()`、
  `STRATEGY_*`→`strategy_*()`、`BASIC_ATTACK_*`／`PARRY_*`／`FLEE_*`／`POISE_BREAK_DAMAGE_BONUS`／`DEFEND_POISE_GAIN` 同理）。
  **`PARRY_DAMAGE_MULT` 的取值口另起名叫 `parry_damage_mult_default()`**——它和实例方法 `parry_damage_mult()` 撞名，
  而 GDScript 的静态方法不能重载（见二点四九结论 2）。
- **测试门限**：`tests/test_handshake_docs.gd` 的战斗常数扫描 key 换成
  `BattleSimulatorActions.cs`／`BattleSimulatorSettle.cs`（`_combat_const("id"` 那 7 处现在分落这两份）；
  `tests/test_talent.gd` 的 `rule:` 语料路径换成四份新 `.cs`。
- **变异探针**：`tools/analysis_mutations_code.ps1` 的 10 条战斗 cast 改指 `src\core\BattleSimulator.cs`
  （`const X := 0.5` → `public const double X = 0.5;`），并按既有口径加进 `$intentionallyUnpinned`
  （附「改 .cs 不重编 → 量不出来」的理由，等 P4 给探针加 `.cs` 支持再逐条删）。
- `src/core/battle_simulator.gd` ＋ `.gd.uid` 已删；`docs/dev/模块对接表.csv` 的
  `combat_core`／`skill_system`／`debuff_system`／`ui_copy` 四行 `code_paths` 换成五份 `.cs`；
  `docs/dev/框架说明.md` 的模块职责表那行换成 `BattleSimulator.cs`。
  **`src/core` 剩 6 个 `.gd`**（`npc_service`／`clue_service`／`event_check_service`／`shop_service`／
  `world_map_service`／`party_builder`）。

**两条实测结论（写死，别再试）**：

1. **GDScript 直接调 C# 方法的返回值不能做 `:=` 类型推断**：`var x := sim.status_chance(...)` 在**解析期**就报
   `Parse Error: Cannot infer the type of "x" variable because the value doesn't have a set type.`
   （`sim` 是 C# 的 `BattleSimulator`，GDScript 拿不到它的签名）。**改法是显式标注**（`var x: float = …`）——
   本批 `tests/test_status.gd` 两处。注意 `.Call(...)` 的结果是 `Variant`，所以原来那些 `:= sim.Call("…")` 不受影响。
2. **`[Export] public Godot.Collections.Dictionary _options { get; set; }` 能被 GDScript 直接读写**：
   `battle.sim._options["max_rounds"] = 1`（`tests/test_battle_ui.gd`）照旧生效——`BattleActor._db` 之后第二个
   「`_` 开头也能注册成属性」的实证。内部字段照旧写裸 `private`（GDScript 只读 `_options` 这一处）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **8902 条 OK**；`run_tests_cs.bat` **3412 条 OK**；
`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`；`run_all_checks.bat` **18/19**
（余 `leaked-process` ＝常驻编辑器实例 PID 26892，假红）。

## 二点五一、core 第二十七批：`npc_service` → 三份 `NpcService*.cs`（2026-10-11）

`src/core/npc_service.gd`（516 行，几乎全是 `static func`）→ **三份 `public partial class NpcService : RefCounted`
（命名空间 `Lunhui.Core`）**。合在一份会到 **863 行**、撞 800 行硬上限，所以按「常量／查表／好感 ＝ 赠礼·切磋·偷窃 ＝
兑换·专属任务」切三块（切法与 `NpcService.cs` 头部注释一致）：

| 文件 | 行数 | 内容 |
|---|---|---|
| `NpcService.cs` | 336 | 常量（含 `FAVOR_MIN`／`SLOT_PREFIX`／`PLAYER_ID`）＋ `FAVOR_TIERS` ＋两个取值口／`favor_tier_text`／静态查表（`def_of`／`join_scene_of`／`person_of`／`favor_row_of`／`npcs_at`／`npc_for_slot`）／好感（`favor_of`／`add_favor`）＋三个私有助手 |
| `NpcServiceSocial.cs` | 237 | 赠礼（`gift_preview`／`gift`）／切磋（`spar_team`／`is_companion`／`spar_opponent`／`win_spar`）／偷窃（`steal_value`／`steal_chance`／`steal`） |
| `NpcServiceQuest.cs` | 290 | 兑换（`offers_of`／`can_redeem`／`redeem`）／专属任务（`quests_of`／`requirement_met`／`quest_done`／`quest_done_flag`／`available_quest`／`complete_quest`）＋ `EXTRA_FAVOR` ＋任务侧小工具 |

**调用面同步（13 个文件）**：

- **C# 侧**：`src/ui/NpcPanel.cs`（删 `NPC_SERVICE_SCRIPT`／`_npc_script()`，11 处 `Call` 改直调）、
  `NpcPanelSections.cs`（5 处）、`NpcPanelSelfTest.cs`（8 处＋`FAVOR_MIN` 直读）、`ClueScreen.cs`（`quest_done_flag`）、
  `BattleScreen.cs`（删 `NPC_SERVICE_SCRIPT`／`_npc_service_script()`）＋`BattleScreenSettle.cs` 的 `win_spar`、
  `src/core/DialogueService.cs`（删 `NPC_SCRIPT`，`AddFavor` 直调）、`src/core/RecruitService.cs`（同上）、
  `TableValidatorWorld.cs`（`ScriptConstants(...)["PLAYER_ID"]` → `NpcService.PLAYER_ID`）、
  `src/world/OverworldController.cs`／`LocalMapController.cs`（各删 `NPC_SCRIPT`＋`_npc_script()`，4 处改直调，
  `_npc_slot_prefix()` 直接回 `NpcService.SLOT_PREFIX`）。
  **注意**：`BattleScreenSettle` 里 `db` 是 `[Export] GodotObject`，传参要写强类型视图 `Db`（`db as TableDb`），
  否则 CS1503（编译期就拦住，不是运行期）。
- **GDScript 侧**：`src/core/clue_service.gd`（`preload` 改 `.cs`＋`available_quest` 补第 4 个实参 `{}`）、
  `tests/test_dialogue.gd`（`preload` 改 `.cs`；`FAVOR_MIN`→`favor_min()`、`PLAYER_ID`→`player_id()`；
  4 处 `var x := …favor_of(…)` 改显式 `: int`；`complete_quest` 补第 4 个实参 `{}`）、
  `tests/test_recruit.gd`（`preload` 改 `.cs`）。
- **C# 用例**：`tests/TestDialogue.cs`／`TestRecruit.cs` 的 `_FavorOf` 去掉 `GD.Load<GDScript>(…).Call(...)` 直调静态；
  `tests/TestLocalMap.cs` 的 `npc_for_slot` 直调。
- **变异探针**：`FAVOR_MIN -10->-20` 改指 `src\core\NpcService.cs`（`public const int FAVOR_MIN = -10;`）、
  `EXTRA_FAVOR 数值改 0` 改指 `src\core\NpcServiceQuest.cs`，两条都按既有口径加进 `$intentionallyUnpinned`。
- `src/core/npc_service.gd` ＋ `.gd.uid` 已删；`docs/dev/模块对接表.csv` 的 `npc_system` 行 `code_paths`
  换成三份 `.cs`。**`src/core` 剩 5 个 `.gd`**（`clue_service`／`event_check_service`／`shop_service`／
  `world_map_service`／`party_builder`；另 `src/ui/callable_invoker.gd` 是 P5 才删的回调桥）。

**本批实测结论（续二点五〇，写死别再试）**：

1. **静态方法 + C# 默认参数对 GDScript 不生效**（老坑⑲）：`available_quest(db, state, npc_id)` 从 GDScript 调
   会报「实参数不够」——**C# 侧照样写 `context = null` 默认值**（C# 调用方便），**GDScript 调用点一律把实参补满**
   （`{}`）。这条不是新发现，但是第一次以「同一方法既有 C# 调用又有 GDScript 调用」的形状落地：
   **默认参数只服务 C# 侧**。
2. **`bool` 的 `%+d` 式文案写法**：`"%+d" % 0`（Godot）＝ `"+0"`，C# 的 `$"{0:+#;-#;0}"` 给的是 `"0"`——
   两者**不一样**。面板本来就有一份 `_plus(v) => v >= 0 ? $"+{v}" : $"{v}"`（`NpcPanel.cs`），
   `NpcService` 里补了同款 `_signed(v)`；**别再拿 `:+#` 去替代 `%+d`**（好感 `+0` 会在文案里少一个加号，
   而 `test_local_map`／`NpcPanelSelfTest` 都比对过这类短句）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **8909 条 OK**；`run_tests_cs.bat` **3412 条 OK**；
`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`；`run_all_checks.bat` **18/19**
（余 `leaked-process` ＝常驻编辑器实例，假红）。

## 二点五二、`clue_service.gd` → `ClueService*.cs`（第二十八批，2026-10-11）

`src/core/clue_service.gd`（515 行）→ **两份 `public partial class ClueService : RefCounted`（`namespace Lunhui.Core`）**：

| 文件 | 行数 | 内容 |
|---|---|---|
| `src/core/ClueService.cs` | ~390 | 常量 `SECTION_ORDER`／`SECTION_TITLES`（`public static readonly`，**不是 `const`**）＋`OBSERVED_PREFIX`／`HINT_KIND`＋过渡期取值口 `observed_prefix()`／`hint_kind()`＋字段 `db`／`state`＋构造 `ClueService(TableDb, GodotObject)`＋静态工厂 `create(db, state)`＋查表与分页（`clues_for_scene`／`clues_for_region`／`region_clues`／`section`／`_main_section`／`_side_section`／`_rumor_section`／`_cast_section`／`_hidden_section`／`_section_entry`／`_rumor_subtitle`／`_TargetName`／`_TargetDone`） |
| `src/core/ClueServiceText.cs` | ~290 | 查表拼文案：`_requirement_text`／`_reward_text`／`_clue_target`／`_trigger_target`／`_verify_word`／`_point_place`／`_chapter_name`／`_scene_name`／`_char_name`／`_npc_name`／`_item_name`／`_favor_text`／`_trim_sentence`／`_clues_of`／`_event_entry`＋`source_label`／`node_name`／`describe` |

**调用面同步（4 个文件）**：

- **C# 侧**：`src/ui/ClueScreen.cs`（删 `CLUE_SERVICE_SCRIPT`／`_clue_service_script_cache`／`_clue_service_script()`；
  `clues` 字段由 `GodotObject` 改 `ClueService`；`setup()` 走 `ClueService.create((TableDb)db, state)`；
  `_section_order()`／`_section_titles()` 直读 `ClueService.SECTION_ORDER`／`SECTION_TITLES`；
  `_section_text` 形参改 `ClueService`；约 15 处 `clues.Call("…")` 全改直调 `section`／`clues_for_scene`／
  `clues_for_region`／`region_clues`／`describe`）。
- **GDScript 侧**：`tests/test_clue.gd`（`preload` 改 `ClueService.cs`，`.new(db, state)` → `.create(db, state)`
  两处；**新增 `_check_observed_place`** 钉住下面的回归）、`tests/test_world_event.gd`（同样 `preload` 改 `.cs`、
  `.new(` → `.create(`）。
- **工具**：`tools/analysis_perf.gd:148` 的 `load("…/clue_service.gd").new(wide, state)` →
  `load("…/ClueService.cs").create(wide, state)`。
- `src/core/clue_service.gd` ＋ `.gd.uid` 已删；`docs/dev/框架说明.md` 的路径指到 `ClueService.cs`。
  **`src/core` 剩 4 个 `.gd`**（`event_check_service`／`shop_service`／`world_map_service`／`party_builder`；
  另 `src/ui/callable_invoker.gd` 是 P5 才删的回调桥）。

**顺手修的一处真 bug（要留痕）**：`ClueServiceText._point_place` 原来取 `room.name_cn`，
但 `dungeon_room` 的列叫 `room_name`——**房间名一直取不到**（运行期 `Invalid get index 'name_cn'`，
传闻行的「看到于：」印成空）。C# 版按真列名 `room.room_name` 取，并加回归用例：
`tests/test_clue.gd::_check_observed_place` 断言 `ob_huangcun_door_01`（挂 `scene_huangcun` / `hc_02`）
的「看到于：」里出现 `荒村 · 废屋`。

**本批实测结论（写死别再试）**：

1. **`private static` + 实例依赖会当场编译不过**：`_requirement_text`／`_reward_text` 只服务「拼文案」，
   看着像纯函数就写了 `static`，但它们要 `_item_name()`（查 `db`）——`CS0120`。**凡是要读 `db`／`state`
   的辅助方法一律不写 `static`**（`ClueServiceText` 里改成实例方法）。
2. **GDScript 直接调 C# 静态工厂要写成 `preload(...).create(...)`**（同 `NpcService` 那批的口径）：
   `preload("res://src/core/ClueService.cs")` 返回 `CSharpScript`，`.create(...)` 走静态方法、`.new(...)` 反而绕远。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **8915 条 OK**；`run_tests_cs.bat` **3412 条 OK**；
`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`；`run_all_checks.bat` **18/19**
（余 `leaked-process` ＝常驻编辑器实例，假红）。

## 二点五三、`event_check_service.gd` → `EventCheckService.cs`（第二十九批，2026-10-11）

`src/core/event_check_service.gd`（412 行）→ **一份 `public partial class EventCheckService : RefCounted`
（`namespace Lunhui.Core`）**：常量 `SOFT_BASE`／`SOFT_PER_POINT`／`SOFT_MIN`／`SOFT_MAX`（`public const double`）
＋ `PENDING_COST_CHECKS`／`SUCCESS_FLAGS`（`public static readonly`）＋ 字段 `db`／`state`／`_rng`
＋ 构造 `EventCheckService(TableDb, GodotObject, GodotObject = null)`＋ 静态工厂 `create(db, state, rng)`
＋ `check_row`／`checks_for_scene`／`parent_node_of`／`check_allowed_in_scene`／`checks_for_region`／
`best_check_value`／`soft_chance`／`effective_difficulty`／`judge`／`resolve`／`_grant`（**`public`**，
被用例白盒调）／`item_name`／`source_label`。

**调用面同步（5 个 C# 文件 ＋ 3 份 GDScript 用例）**：

- **C# 侧**：`src/world/LocalMapController.cs`（删 `EVENT_CHECK_SCRIPT`／`_event_check_script_cache`／
  `_event_check_script()`；`_event_service` 与 `event_service()` 由 `GodotObject` 改 `EventCheckService`；
  3 处 `Call` 改直调）、`src/world/OverworldController.cs`（同款，2 处 `Call` 改直调）、
  `src/core/GuardService.cs`（`check_value_of` 直调 `best_check_value`）、
  `src/core/NpcServiceSocial.cs`（`_event_check()` 返回 `EventCheckService`；`best_check_value`／`soft_chance` 直调）、
  `src/core/WorldEventService.cs`（`check` 那支直调 `resolve`）。
- **GDScript 侧**：`tests/test_event_check.gd`（`preload` 改 `.cs`，16 处 `.new(` → `.create(`）、
  `tests/test_end_to_end.gd`（`preload` 改 `.cs`，`new(wide, judge_state)` → `create(wide, judge_state, null)`）、
  `tests/test_talent.gd`（`preload` 改 `.cs`，3 处 `.new(` → `.create(`；`_check_table_is_consumed` 的
  语料清单把 `src/core/event_check_service.gd` 换成 `src/core/EventCheckService.cs`）。
- **变异探针**：5 条 cast（`SOFT_BASE`／`SOFT_PER_POINT`／`SOFT_MIN`／`SOFT_MAX`／`SUCCESS_FLAGS 缺一条`）
  改指 `src\core\EventCheckService.cs` 并按既有口径加进 `$intentionallyUnpinned`（改 `.cs` 不重编 → 量不出来）。
- `src/core/event_check_service.gd` ＋ `.gd.uid` 已删；`docs/dev/模块对接表.csv` 的 `event_skill`／`event_check`
  两行 `code_paths` 换成 `src/core/EventCheckService.cs`；`docs/dev/框架说明.md` 的路径同步改。
  **`src/core` 剩 3 个 `.gd`**（`shop_service`／`world_map_service`／`party_builder`；
  另 `src/ui/callable_invoker.gd` 是 P5 才删的回调桥）。

**本批实测结论（写死别再试）**：

1. **C# 里 `$"...{a ? "x" : "y"}..."` 编不过**（插值孔里再放字符串字面量）：先把三目结果取到局部变量
   （`string roll_word = success ? "成功" : "失败";`）再拼，`node.Get("name_cn")` 这类带引号的也同理。
2. **老坑复现**：GDScript 调 C# 静态工厂要把实参写满——`new(db, state)`（原来吃 GDScript 默认值 rng=null）
   一律改成 `create(db, state, null)`；不带默认值的 2 参构造口没必要再留一个重载。
3. **`test_handshake_repo::_check_no_orphan_code` 的断言数会随 `.gd` 文件数变**（它每个 `src/**/*.gd`
   一条 `check_true`）——所以「全量断言数」减 1 是**删掉一个 `.gd` 的正常副作用**，不是用例被跳了。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **8914 条 OK**；`run_tests_cs.bat` **3412 条 OK**；
`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`；`run_all_checks.bat` **18/19**
（余 `leaked-process` ＝常驻编辑器实例，假红）。

## 二点五四、`shop_service.gd` → `ShopService.cs`（第三十批，2026-10-11）

`src/core/shop_service.gd`（373 行）→ **一份 `public partial class ShopService : RefCounted`
（`namespace Lunhui.Core`）**：常量 `BUYBACK_LIMIT`（`public const int`）＋ 过渡期取值口
`buyback_limit()`（**C# 的 `const` GDScript 读不到**，GDScript 用例改读这个口）
＋ 字段 `db`／`state` ＋ 构造 `ShopService(TableDb, GodotObject)` ＋ 静态工厂 `create(db, state)`
＋ 建筑与货架（`building`／`building_for_shop_group`（**静态**）／`building_name`／`stock_rows`／`stock_row`）
＋ 价格（`buy_price`／`_buy_discount`／`sell_price`／`remaining`／`purchased`／`money`／`item_name`）
＋ 买入（`can_buy`／`buy`）／卖出（`can_sell`／`sell`）／回购（`buyback_rows`／`buy_back`）
＋ 治疗（`has_heal`／`heal_cost`／`heal_price`／`heal_preview`／`heal_party`）。
**`heal_preview` 暂时借 GDScript 的 `PartyBuilder.build_actor`**（`_party_builder().Call("build_actor", …)`）
——那是最小改动的一次性接口，`party_builder.gd` 搬走时要回来把它改成直调（同一批留下的欠条）。

**调用面同步（3 个 C# 文件 ＋ 4 份 GDScript 用例）**：

- **C# 侧**：`src/world/OverworldController.cs`（删 `SHOP_SCRIPT`／`_shop_script()`；`building_for_shop_group`
  与 `building` 改直调）、`src/ui/ShopScreen.cs` ＋ `src/ui/ShopScreenSelfTest.cs`（`shop` 字段由
  `GodotObject` 改 `ShopService`，全部 `Call("x", …)` 改直调）。
- **GDScript 侧**：`tests/test_shop.gd`（`preload` 改 `.cs`，`new(` → `create(`，`BUYBACK_LIMIT` → `buyback_limit()`）、
  `tests/test_end_to_end.gd`、`tests/test_world_event.gd`、`tests/test_talent.gd`
  （后一份的 `_check_table_is_consumed` 语料清单把 `src/core/shop_service.gd` 换成 `src/core/ShopService.cs`）。
- **变异探针**：`BUYBACK_LIMIT 20→5` 那条 cast 改指 `src\core\ShopService.cs`，并按既有口径加进
  `$intentionallyUnpinned`。
- `src/core/shop_service.gd` ＋ `.gd.uid` 已删；`docs/dev/模块对接表.csv` 的 `shop_system` 行
  `code_paths` 换成 `src/core/ShopService.cs`。**`src/core` 剩 2 个 `.gd`**（`world_map_service`／`party_builder`）。

**同一批顺手把两条会漂的门限改成自洽校验**（`tests/test_handshake_code.gd::_check_preload_for_global_class_calls`）：

1. 原来钉的是「`src/` 里还剩几个 GDScript 类」（`owners.size() > 1.0`），每搬走一个类就要手改一次，
   迁到只剩两个类时已经量不出东西——**改成「写了 `class_name` 的 `.gd` 文件数必须等于扫出来的 owner 数」**：
   正则失配、缓存没刷新会立刻红，且不随批次漂移。
2. 原来的「`XxxScript.` 调用处数必须 > 5」同理退役（迁移期一路掉到 0，今天 5 处全在 `party_builder.gd`），
   换成更硬的那半边：**裸写类名调用必须 0 处**（`bare_calls == 0`）。

**本批实测结论（写死别再试）**：

1. **C# 里 `$"...{expr.Get("k")}"` 编不过**——插值孔里再放带引号的字符串字面量
   （`$"{equip.Get("name_cn").AsString()} …"`）会编译失败：先取局部变量再拼（本批修了 `drop_only_name`／
   `key_name`／`stacked` 三处）。与「二点五三」的 `$"...{a ? "x" : "y"}..."` 是同一条坑。
2. **GDScript 调 C# 方法的返回值不能 `:=` 推断**（老坑复现）：`var unit := shop.sell_price(…)` 报
   `Cannot infer the type of "unit" variable`，要写成 `var unit: int = shop.sell_price(…)`
   （本批 `tests/test_shop.gd`／`tests/test_talent.gd` 各一处）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **8913 条 OK**；`run_tests_cs.bat` **3412 条 OK**；
`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`；`run_all_checks.bat` **18/19**
（余 `leaked-process` ＝常驻编辑器实例，假红）。

## 二点五五、`world_map_service.gd` → `WorldMapService.cs`（第三十一批，2026-10-11）

`src/core/world_map_service.gd`（322 行）→ **一份 `public partial class WorldMapService : RefCounted`
（`namespace Lunhui.Core`）**：常量 `TILE`（`public const double`）／`CHAPTER_SCENE`（`public const string`）
＋ `LOCKED_SCENES`（`public static readonly string[]`）＋ `DIFFICULTY_RULES`
（`private static readonly Dictionary<string, string[]>`）＋ 两个**静态取值口**
`locked_scenes()`／`difficulty_conditions()` ＋ 字段 `db`／`state` ＋ 构造 `WorldMapService(TableDb, GodotObject)`
＋ 静态工厂 `create(db, state)` ＋ 节点与揭开（`all_nodes`／`node_row`／`node_name`／`is_revealed`／
`discovery_target_revealed`／`apply_initial_reveals`／`apply_proximity_reveals`／`apply_held_reveals`／
`revealed_count`／`progress_text`）＋ 驿站（`travel_targets`／`travel`）＋ 难度（`can_switch_difficulty`／
`switch_difficulty`／`difficulty_rows`）＋ 静态 `held_item_id`／`conditional_layer_rule`／
`conditional_layer_visible`。

**这一批顺带拆掉了「迁移期跨语言只读通道」**：`TableValidator.ScriptConstants()` 与
`TableValidator.ScriptFor()`（原 `TableValidator.cs` 里那一对，注释写着「只该用来读那份名单的唯一真相」）
**最后一个用户就是这份 `.gd`**，所以两个方法随本批一起删，`src/` 里再没有「C# 去读 GDScript 常量表」这条路。
`LOCKED_SCENES`／`DIFFICULTY_RULES` 的调用点改走 `WorldMapService.locked_scenes()`／
`difficulty_conditions()`；`held_item_id()` 直接静态调。

**调用面同步（6 个 C# 文件 ＋ 4 份 GDScript 用例 ＋ 探针 ＋ 3 处文档）**：

- **C# 侧**：`src/world/OverworldController.cs`（`world_map` 字段由 `GodotObject` 改 `WorldMapService`；
  删 `WORLD_MAP_SCRIPT`／`_world_map_script_cache`／`_world_map()`；`_locked_scenes()` 改读静态口；
  15 处 `Call(...)` 改直调；`_check_reveals` 里那条「先把 `Call` 结果转 `Array`」改写为
  `foreach (string id in world_map.apply_*_reveals())`）、`src/world/LocalMapController.cs`
  （删常量与缓存，两处 `conditional_layer_*` 直调）、`src/ui/WaypointScreen.cs`（`world_map` 字段由
  `[Export] public GodotObject` 改 `public WorldMapService`——它只被 C# 读写，不留 `[Export]`；
  删常量与缓存；建实例走 `WorldMapService.create((TableDb)db, state)`；6 处 `Call` 改直调）、
  `src/core/TableValidatorStructure.cs`（`CheckLockedScenesExist` 读 `locked_scenes()`）、
  `src/core/TableValidatorWorld.cs`（`CheckMapRegionRules` 直调 `held_item_id`；
  `CheckDifficultyUnlockConditions` 读 `difficulty_conditions()` 建 `HashSet`）、
  `src/core/TableValidator.cs`（**删 `ScriptConstants()`／`ScriptFor()`**）。
- **GDScript 侧**：`tests/test_world_map.gd`（`preload` 改 `.cs`，8 处 `.new(` → `.create(`）、
  `tests/test_battle_ui.gd`（同上 1 处）、`tests/test_handshake.gd`（`SINGLE_SOURCE_RULES` 第一条：
  owner 改 `src/core/WorldMapService.cs`、标签随之改，见下面结论①）、`tests/TestOverworld.cs`
  （C# 用例：5 处 `Call("is_revealed"…)` 改直调，`conditional_layer_rule` 也从「`_gd(...).Call(...)`」
  改成静态直调）。
- **变异探针**：`LOCKED_SCENES 打错一个字母` 与 `TILE 32.0->38.4` 两条 cast 改指
  `src\core\WorldMapService.cs`，并加进 `$intentionallyUnpinned`（改 `.cs` 不重编 → 量不出来）。
- `src/core/world_map_service.gd` ＋ `.gd.uid` 已删；`docs/dev/模块对接表.csv` 的 `map_overworld` 行
  `code_paths` 换成 `src/core/WorldMapService.cs`；`docs/dev/框架说明.md`（大地图那一节）与
  `docs/dev/设计实现对照.md`（02 地图那张表）的路径/名字同步改。
  **`src/core` 剩 1 个 `.gd`**（`party_builder`，也是 `src/` 下最后一个）。

**本批实测结论（写死别再试）**：

1. **`_check_single_source_of_truth` 量的是「标签字符串在不在文件里」**：`.gd` 的
   `const LOCKED_SCENES := ["…"]` 换成 C# 的 `public static readonly string[] LOCKED_SCENES = { "…" }`
   之后，旧标签一个文件都匹配不上 → `check_gt(hits.size(), 0)` 当场红。**换成 C# 那行自己的字面量**
   （`LOCKED_SCENES = { "scene_ferry_locked" }`）：定义处与门限重新对上，改列表时还是会红。
2. **`Variant.AsGodotArray()` 读不了 PackedStringArray**（会抛）：C# 方法返回 `string[]`（＝
   PackedStringArray）时，调用侧要用 `AsStringArray()`。本批 `conditional_layer_visible` 里那条
   `foreach` 就是这么写的（`rule["items"]` 存的是 `string[]`）。
3. **别指望 GDScript 的 `Array` 自动转成 C# 的 `string[]` 参数**：`LocalMapController` 原先给
   `conditional_layer_rule` 递的是 `Godot.Collections.Array`，改直调时先建
   `System.Collections.Generic.List<string>` 再 `ToArray()`——省掉一次「能不能隐式转」的赌博。
4. **`[Export]` 字段改强类型**（`WaypointScreen.world_map`）是安全的：那个字段只有 C# 读写
   （GDScript 从来没碰过它），所以去掉 `[Export]`、直接声明成 `WorldMapService` 即可；
   真要被 GDScript 读写才必须留 `[Export]`（坑⑥）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **8912 条 OK**（比上一批少 1 条＝少一个 `.gd`，
`_check_no_orphan_code` 每个 `.gd` 一条断言）；`run_tests_cs.bat` **3412 条 OK**；
`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`；`run_all_checks.bat` **18/19**
（余 `leaked-process` ＝常驻编辑器实例，假红）。

## 二点五六、`party_builder.gd` → `PartyBuilder.cs`（第三十二批，2026-10-11）

`src/core/party_builder.gd`（122 行）→ **一份 `public partial class PartyBuilder : RefCounted`
（`namespace Lunhui.Core`）**：`build_actor(db, state, char_id)`（不带战斗外增益）／
`build_actors(db, state, field_buffs)`（按存档队伍造全员）／`equip_grants(db, state, char_id, triggers)`／
`equipped_weapon(db, state, char_id)`／`equipped_weapon_type(db, state, char_id)`
＋ 私有 `_build_actor(db, state, char_id, field_buffs)`（两边共用的那套：等级／加点／装备／内功／
招式装配／战斗外气血／进场 buff／命中触发特效）。

**这一批把「GDScript 替 C# 做注册」那五行迁就代码删了**：原 `.gd` 里那五条
`preload("res://src/core/{BattleActor,CharacterSheet,AttributeCalculator,BuffService,SetService}.cs")`
——注释写着「C# 类只有被当作 Godot 资源加载过才会注册，少了这行会随机崩在
`tie_user_managed_to_unmanaged: script.is_null()`」。搬成 C# 之后整条链都在同一个程序集里，
**删掉五行照样跑**：本轮把全量自检连跑 4 遍，那个崩溃一次都没复现（日志里唯一命中
`parse error` 的是 `test_settings` 自己那个「坏配置文件」夹具）。

**调用面同步（5 个 C# 文件 ＋ 8 份 GDScript 用例 ＋ 1 份 C# 用例 ＋ 2 个工具）**：

- **C# 侧**：`src/core/ShopService.cs`（`heal_preview` 直调 `PartyBuilder.build_actor`，
  **顺带还清「二点五四」留的那张欠条**——那批借 `_party_builder().Call(...)` 的过渡口连同
  `PARTY_BUILDER_SCRIPT` 常量与缓存一起删；顺带把 `actor.Call("max_hp")`／`actor.Get("hp")`／
  `actor.Get("display_name")` 改成成员直读）、`src/core/DungeonService.cs`（扫荡时算队伍机缘：
  `GD.Load<GDScript>(…).Call("build_actors", …)` → 直调）、`src/world/LocalMapController.cs`
  （`_party()` 直调，删常量与缓存）、`src/ui/BattleScreen.cs`（`allies` 那处直调；
  `_build_mirror_enemy` 的返回类型由 `GodotObject` 收紧成 `BattleActor`，`Set`／`Call` 改成员直写）、
  `src/ui/BattleScreenActors.cs`（`_gear_text` 直调 `equipped_weapon`）、
  `src/ui/BattleScreenSelfTest.cs`（卸空装配那条直调）。
- **GDScript 用例**：`test_affix.gd`／`test_buff.gd`／`test_battle_session.gd`／`test_battle_fuzz.gd`／
  `test_end_to_end.gd`／`test_practice.gd`／`test_save_migration.gd`／`test_status.gd`
  ——`preload` 路径改 `.cs`，`build_actors(db, state)` **实参写满**成 `build_actors(db, state, [])`
  （共 20 处；`build_actor` 三参不变）。
- **C# 用例**：`tests/TestDropResolver.cs` 的 `_CheckPartyBonus` 从「`GD.Load<GDScript>` ＋ `Call`」
  改成直调。
- **工具（P4 一起搬）**：`tools/analysis_perf.gd`／`tools/analysis_balance.gd` 的
  `load("…party_builder.gd")` 与两处 `build_actors(...)` 同步改（它们是观测工具，**不进门限**）。
- `src/core/party_builder.gd` ＋ `.gd.uid` 已删；`src/core/CharacterSheet.cs`／`src/core/DungeonService.cs`／
  `tests/TestAffix.cs` 里「PartyBuilder 还没搬」的散文指针改成现状。
  **`src/` 下只剩 `src/ui/callable_invoker.gd`**（迁移期回调桥，P5 删）——`src/core/` 与其余各目录**都已清零**。

**本批实测结论（写死别再试）**：

1. **C# 的默认参数对引擎调用确实不生效**：把生成代码导出来看一眼就是铁证——
   `build_actors` 的方法表里 `defaultArguments: null`、分派写的是 `args.Count == 3`。
   所以 GDScript 调用点**必须把实参写满**（`build_actors(db, state, [])`），与「二点四七」那条坑同源。
   **导出办法**：`dotnet build lunhui.csproj -p:EmitCompilerGeneratedFiles=true
   -p:CompilerGeneratedFilesOutputPath=obj\gen`，生成的方法表在
   `obj\gen\Godot.SourceGenerators\Godot.SourceGenerators.ScriptMethodsGenerator\`——
   **但看完必须把 `obj\gen` 整个删掉**：它就在项目根下，下一次 `dotnet build` 会把它当源码再收一次，
   当场 1892 条「类型 X 已定义同名成员」（本批踩过，删掉即恢复）。
2. **GDScript 替 C# 做的「预加载注册」可以直接删**（见上）：搬进同一程序集后不再需要，
   4 遍全量自检没复现崩溃。**别因为那句注释再把它加回来**。
3. `PartyBuilder` 的入口全是 `public static` → 「写了但游戏路径从不调用」那道门限的 C# 正则
   **看不见它们**（坑⑦），所以既不用白名单、也不会因为这批改签名而红。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **8911 条 OK**（连跑 4 遍都一样；
比上一批少 1 条＝少一个 `.gd`）；`run_tests_cs.bat` **3412 条 OK**；`validate_tables.ps1` `PASSED`；
`check_loop.bat` `OK`；`run_all_checks.bat` **18/19**（余 `leaked-process` ＝常驻编辑器实例，假红）。

## 三点一二、P4 第五十六批（`tools/` 与回调桥收尾）：**除 `addons/` 外 `.gd` 清零**（2026-10-11）

本批把 P4 的尾巴一次剪干净：`tools/` 那几份 `.gd`（`analysis_mutations`／`_shot`／
`artgen/font_preview`／`__probe_methods`）、`tests/test_case.gd`、以及 P5 名册里的
`src/ui/callable_invoker.gd` 全部退场。**收尾后 `src/`＋`tests/`＋`tools/`＋`scenes/` 下 `.gd` 为 0**
（只剩 `.logs/vendor/` 里那份第三方快照，不是源码）。

**一、先修了一个上游竞态**（它比迁移本身更像拦路虎）。`AnalysisBalance` 跑到第 8 级必崩
（退出码 `0xC000001D`，`SEHException`）。根因＝上游 godot#109711：某个 C# 类的**最后一个实例**
被 GC 时，引擎连带析构它对应的 `CSharpScript`，主线程此时 `new` 同一个类就撞
`Condition "script.is_null()" is true`。旧 GDScript 链不崩，是因为 `load()` 拿到那个 Script 后有本地引用钉着。
**对策**：新增 `src/core/ScriptKeepAlive.cs`——`GameData._Ready()` 里 `ScriptKeepAlive.hold_all()` 扫
`res://`（跳过 `.` 开头的目录）把所有 `.cs` 加载成 `CSharpScript` 存进静态表钉住（幂等）。修完连跑完成、退出码 0。

**二、`tools/NumberFormat.cs`**：Godot `%.Nf` 的口径是「对二进制真值做正确的十进制舍入、平局远离零」。
`ToString("F1")`（平局取偶）、`Math.Round(AwayFromZero)`（十进制最短表示）都会错。
正解＝`ToString("G17")` → `decimal.Parse` → `Math.Round(AwayFromZero)` → `ToString("F…")`；
30 组边界值（`0.35`／`5.55`／`10.25`…）与旧链逐字一致。

**三、三个分析工具搬 C#**：

- `analysis_balance` → `tools/AnalysisBalance.cs` ＋ `analysis_balance_cs.tscn`；与旧输出在
  「第一个 `---- 胜率` 行之后」的 428 行**逐字 0 差异**。**全流程共用一支 `EnemyFactory`** 是上面那条崩溃的必要条件之一。
- `analysis_perf` → `tools/AnalysisPerf.cs` ＋ `analysis_perf_cs.tscn`；`docs/dev/性能观测.md` 加了
  「三之五」一节重测记录（面板那栏不能读成「C# 慢 50 倍」，逐段量过）。
- `analysis_mutations` → `tools/AnalysisMutations.cs` ＋ `analysis_mutations_cs.tscn`；64 条 case 全搬，
  与旧链 `MUTATIONS: OK` 的 67 行**逐字 0 差异**。

**四、三份小工具搬 C#**：`_shot` → `tools/Screenshot.cs`（`screenshot_cs.tscn`，已实跑出 PNG）；
`artgen/font_preview` → `tools/artgen/FontPreview.cs`（**必须真窗口**，已实跑重写试排图）；
`__probe_methods` 是一次性探针，直接删。`tests/test_case.gd` 已无调用者（两个分析工具改用 `new TestCase()`），同批删除。

**五、回调桥退休（P5 的一半）**：11 份 UI 面板 ＋ `BattleScreenFloats` 的回调从「`Variant` ＋
`CallableInvoker` 跨语言桥」收紧成 `Callable`——`_has_callback(` → `_callable_valid(`，
调用直接 `callable.Call(args)`。**P5 剩下的**：变异探针 `.cs` 支持、白名单回填。

**口径变化（写死）**：文档门限 `RetiredToolPaths` 再收 `analysis_mutations.gd`／`_shot.gd`／
`artgen/font_preview.gd`（`docs/dev` 里剩的 5 处旧指针全在 `交接/小程序.md` 的历史叙事里）；
`assets/fonts/README.md` 那处活指针换成 `FontPreview.cs`。

**验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **`SELFCHECK: OK (11608 assertions)`**
（MinAssertions 11607 → 11598 ＝ total−10）；`validate_tables.ps1` `PASSED`；`run_all_checks.bat` `ALL CHECKS: PASSED`。

**六、P5 收尾（同批续做）**：

- **变异探针第三块真的跑起来了**。它其实早就改成每个 case 前先 `dotnet build`（改 `.cs` 量得到），
  欠的只是把那张「改 `.cs` 不重编 → 量不出来」的豁免表清掉——**清掉 63 条、留 14 条真·不钉**
  （决策 154 的口径：纯移动／表现／两边自洽）。全量跑的结果：**79 个 case，1 条没人钉、0 失效、0 没量到**。
  那一条是 `RoamingEnemy` 的 `TILE 32.0->38.4`（明雷的视野／游荡／追击倍率，纯手感），
  已豁免并写明理由；**揭雾门槛那份 `WorldMapService.TILE` 同一批跑出来是 [OK]**。
  顺带修了它自己的一个坑：两条 case **重名**（`TILE 32.0->38.4` 有两处），
  而探针按名字打印、豁免表也按名字做键——重名会让「哪一条被量到」说不清，现已按文件拆名；
  另给探针加了 `-Only <正则>`（只跑匹配的几条），给「补完断言再验一遍」用。
- **窗口冒烟 13/13 PASSED**；`run_all_checks.bat` **`ALL CHECKS: PASSED`（19 步）**；
  `run_tests.bat` `SELFCHECK: OK (11610 assertions)`（下限 11598）。
- **仓库级口径回填（AGENTS.md 那一批）**：根 `AGENTS.md` 首句从「GDScript」改成「C#」；
  各分目录 `AGENTS.md` 里剩下的活指针（`build_tables.gd`／`test_settings.gd`／
  `check_loop.gd`／`test_handshake_code.gd::_check_save_fields_are_serialized`／
  `test_save_migration` 的 `ADDED_IN`…）全部换成 `.cs` 侧的真名字；`docs/dev/` 的
  框架说明／设计实现对照／地图搭建说明／当前状态 里的活指针同样换过。
  **`decisions/**` 与 `交接/*_归档.md` 的历史叙事一字不动**（那是「当时是什么样」的记录）。

**七、P5 的两条过期护栏也清了（2026-10-11 收尾）**：

- `CheckPreloadForGlobalClassCalls` **删掉**：`src/` 下 `.gd` 为 0 之后它那三个断言恒真
  （扫 `.gd` 的 `class_name`，扫到 0 个）。根 `AGENTS.md` 里那条「跨文件调用必须走 `preload` 常量」
  同步改成 C# 的说法；历史（为什么要它、那条界线怎么量出来的）留在决策 369 与本文件前面的批次里。
- `CheckNoNewTestOnlyApi` 的 **C# 正则补上 `public static`**：以前只认 `public <类型> 名字(`，
  于是 `public static bool foo()` 在这道门里**整个隐形**——搬 C# 期间一批静态取值口就是这么溜过去的
  （`TestHandshake.cs` 白名单里那几处注释都写着「P5 补正则时加回来」）。补上后当场报出 **56 条**，
  逐条核过都是「过渡期取值口」（GDScript 读不到 C# 的 `const`／枚举才包的静态口），已全部登记理由。
  **留的尾巴**：GDScript 清零后这层包装已经没必要，哪天顺手收掉那 56 个静态口时，
  白名单条目会跟着门限的反向检查一起提醒删。

全量复验：`run_tests.bat` `SELFCHECK: OK (11605 assertions)`（下限 11595）、
`validate_tables.ps1` `PASSED`、`run_all_checks.bat` `ALL CHECKS: PASSED`（19 步）、
窗口冒烟 13/13、变异探针 `code` 那一块 `MUTATION SWEEP: PASSED`（3 步）。

## 三点一一、P4 第五十五批：`tools/build_tables.gd` → **新建** `tools/BuildTables.cs` ＋ `tools/build_tables.tscn`（2026-10-11）

`tools/` 剩下的 7 份 `.gd` 里先挑**风险最高**的那份——它在验收链路上，`.bat` 一改错整条链都跑不起来。

**搬了什么**：`build_tables.gd`（163 行，`extends SceneTree`）→ `BuildTables.cs`（265 行，`Node`）＋
入口场景 `build_tables.tscn`。`--script` 只认 GDScript，所以照 `RunTestsCs` 的形状：
`_Ready` 里 `Callable.From(...).CallDeferred()`，跑完 `GetTree().Quit(code)`。
五个调用面跟着换：`run_tests_cs.bat`／`run_windowed_smoke.bat`／`run_perf_analysis.bat`／
`run_balance_analysis.bat`／`analysis_mutations_code.ps1`（都是把 `--script res://tools/build_tables.gd`
换成位置参数 `res://tools/build_tables.tscn`）。

**两个新坑（都值得记）**：

1. **C# 里 `Script` 没有 `New()`**。GDScript 那版靠 `load(path).new()` 造行类实例；C# 绑定没把
   `Script` 的变参方法 `new` 暴露出来（`GDScript.New()` 有，`CSharpScript` 没有对位重载）。
   走 `script.Call("new")`——同一个 ClassDB 方法，只是动态调用。
2. **`StringExtensions.ToFloat()` 是 32 位**，而 GDScript 的 `to_float()` 是 64 位。
   真差异只有一个数：`item_money.stack_max` 的 `999999999` 被舍成 `1000000000`。
   改法 `double.TryParse(InvariantCulture)`（宽松 + 区域无关，与 `to_float()` 同口径），
   顺手修了 `TableValidatorStructure.cs` 里同款写法。

**这一批的验证手法（建议后面照抄）**：新旧两条链各生成一遍 `data/generated/*.tres`，
**先把 Godot 每次随机生成的 `sub_resource` id 归一化掉**（`Resource_[a-z0-9]+`、`id="\d+_[a-z0-9]+"`），
再逐字节比。不归一化的话 64 张「张张都不一样」，等于没有信号——第一遍就吃了这个亏。
归一化之后：63 张逐字一致，1 张差 `stack_max`，正是上面第 2 条。

**连带**：文档门限（`TestHandshakeRepoFiles.cs::CheckDocToolReferences`）后缀表补 **`.tscn`**——
C# 工具的入口都是场景，不收 `.tscn` 等于最主要的一类入口没人验；补上之后当场抓出
本节所在的旧计划里一处指着并不存在的 `run_tests.tscn` 的说法（已改成 `run_tests_cs.tscn`；**写反例别带 `tools/` 前缀**，那条门限连反例都当真路径验）。
`RetiredToolPaths` 加 `build_tables.gd`。**`docs/dev/模块对接表.csv` 的 `table_pipeline` 行
`code_paths` 必须同步**（`tools/build_tables.gd` → `tools/BuildTables.cs`）——那一列被
`CheckContractAssets` 按「文件必须存在」逐条验，不换就红。指路的字符串与注释也一起换：
`src/Bootstrap.cs`、`src/core/TableDb.cs`（两条用户可见的错误提示）、`src/data/TableResource.cs`、
`src/data/TableRegistry.cs`，以及 `框架说明.md`／`设计实现对照.md` 的活动指针。

**验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **11632 条 OK**（下限 11604 → **11622**）；
`validate_tables.ps1` `PASSED`；`run_all_checks.bat` **`ALL CHECKS: PASSED`（passed steps: 19）**。

**下一批**：~~先搬 `analysis_balance.gd`／`analysis_perf.gd`~~ **✅ `build_tables` 先办了**（见「三点一一」——
它在验收链路上，风险最高）。接着搬这两份：它们都 `load("tests/test_case.gd")` 取夹具，
C# 侧直接 `new TestCase()`（`PartyState`／`TableWithNChars`／`TableWithDuplicatedTriggers`／
`TableWithManyFloors` 都是 public）即可，搬完 `tests/test_case.gd` 才能删。
再之后：`analysis_mutations.gd`（211）／`artgen/font_preview.gd`（147）／`_shot.gd`（71）／
`__probe_methods.gd`（10）。
## 三点一零、P4 第五十四批（P4 收口）：两条链并成一条（2026-10-11）

八族手写校验网都到 C# 侧之后，把 GDScript 那条链整个退休：

1. **删 `tools/run_tests.gd`**（393 行，`TEST_SCRIPTS` 已空）＋ **删 `tests/test_handshake.gd`**
   （757 行，基类——八族搬完后已无子类）＋ 各自的 `.gd.uid`。
2. **`tools/run_tests.bat` 改成转发**：`call "%~dp0run_tests_cs.bat" %*` ＋ `exit /b %ERRORLEVEL%`。
   文件名保留——它是全仓库文档与其它 `.bat` 都在调的入口，且 `docs/dev` 里那 79 处
   `tools/run_tests.gd` 指针若没有对位文件会让「文档引到的工具必须存在」当场红。

**为什么这一步要紧（差点留下一个静默的洞）**：`run_all_checks.bat` 第 1 步调的就是
`run_tests.bat`。第 53 批把 `TEST_SCRIPTS` 清空之后，第 1 步跑的其实是**一条空链**——
`SELFCHECK: OK (0 assertions)` 照样打印，全量验收于是**一条 C# 用例都没跑**。
转发之后第 1 步真的跑 11614 条。**判据**：跑一次全量，看 `.logs\tests_cs.log` 的写入时间
是不是这一轮（旧版的 `.logs\tests.log` 是上一轮留下的，看着也"新鲜"）。
**更一般的教训**：判"某条链还在不在跑"要看**它写没写日志**，别只看退出码与 OK 标记。

**三处连带改动**：

1. `tests/TestHandshakeContract.cs::CheckVerifyTestsAreExecuted` 的「执行列表」只留
   `res://tools/RunTestsCs.cs` 一份（迁移期两份都读，第 48 批踩过漏读一份的坑）。
2. `tests/TestHandshakeRepoFiles.cs::CheckDocToolReferences` 新增 `RetiredToolPaths`：
   `docs/dev` 里 79 处 `tools/run_tests.gd` 指针（多半在 `decisions/**` 的历史叙事里）**按名字放行**，
   同时**反向钉住「这些路径确实已经不在 `tools/`」**——哪天有人把老路径建回来就红。
   这比逐处改历史文档诚实：那些引用记的是"当时怎么做的"，不是坏链接。
3. `tools/analysis_mutations_code.ps1`（手动·代码常量探针）：
   - 第 ③ 步从 `--script res://tools/run_tests.gd` 改成 **`dotnet build` ＋ 跑
     `res://tools/run_tests_cs.tscn`**，并**把默认 Godot 可执行文件从非 mono 版改成 mono 版**
     （C# 场景在非 .NET 构建里根本载不起来）；
   - 编不过记 `[UNMEASURED]`（"量不出来 ≠ 量过了"，与它原有的哲学一致），别退回去跑旧程序集——
     那会把"没人钉"误报成"钉住了"。

**顺带修的活动指针**（都指向刚删的两个文件）：根 `AGENTS.md`、`tests/AGENTS.md`、`tools/AGENTS.md`、
`data/AGENTS.md`、`src/core/AGENTS.md`、`src/ui/*SelfTest.cs` ×5 ＋ `NpcPanel.cs`、
`src/audio/Sfx.cs` ×2、`src/core/SkillGrant.cs`、`docs/dev/框架说明.md` ×5、`第三方代码.md`、
`性能观测.md`、`当前状态.md`。规矩照旧：**活文档（现状说明、AGENTS）要改，历史叙事
（`decisions/**`、本计划的三点××）留着**。

**验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **11614 条 OK**（下限 11667 → **11604**，
少 63 条＝豁免掉的那批文档引用）；`run_tests.bat` 转发的退出码透传（实测 1 与 0 都对）；
`validate_tables.ps1` `PASSED`；`run_all_checks.bat` **`ALL CHECKS: PASSED`（passed steps: 19）**、
`.logs\tests_cs.log` 是本轮写入的。

**下一批（第 55 批）**：`tests/` 只剩 `test_case.gd`（291 行夹具库）——`tools/analysis_balance.gd`／
`analysis_perf.gd` 还 `load("res://tests/test_case.gd")`，得先搬这两个工具（连同 `build_tables.gd`／
`analysis_mutations.gd`／`artgen/font_preview.gd`／`_shot.gd`／`__probe_methods.gd`，共 7 份）才能清零。
## 三点零九、P4 第五十三批：`tests/test_handshake_docs.gd` → **新建** `tests/TestHandshakeDocs.cs` ＋ `TestHandshakeDocsTables.cs`（2026-10-11）

"文档计数与引用"这一族（708 行 → 574 ＋ 467 行）。九条检查，拆成两份 `partial` 只为不撞 800 行硬上限。

**本族的看点**：
1. **版本三向对齐**：设计文档里的版本号 ＝ `CHANGELOG` 顶格版本 ＝ `模块对接表`／`验证清单` 里
   `design_version` 的最高值；任何一处落后都会红（这条是"策划改完需求下游要跟"的机器化）。
2. **计数对账**：策划文档里写死的"N 条""N 行"（`RowCountClaims`）要跟真实 CSV 行数对得上——
   `07`／`15` 那几张表随配表变化，写死的数字是最容易漂的一类。
3. **文档字典列**：`DocDictionaryProseColumns` 列的那几段散文（"本表有这些列：…"）要逐列收，
   与真实表列名两侧一致；枚举列另有 `DocEnumKnownStale`／`Ps1EnumExempt` 两张已知豁免表。
4. **路径与待办**：带目录前缀的策划文档路径要真实存在（与 `repo` 族那条工具门限同源）；
   `PendingDocRefs`／`StruckPendingWords` 盯的是"被划掉／历史化的待办不许留活钉子"——
   也就是 AGENTS.md 里那条"决定写进拍板节之后要回头把正文的『待定』改成『已定』"。
5. **存档序列化**：`SaveSerializationTargets` 列的字段要在代码里真出现（序列化目标漂了就静默丢档）。
**八族收口**：这一族搬完，`tests/` 的手写校验网**八族（contract／wiring／flags／numbers／doccounts／
code／repo／docs）全部到 C# 侧**——`tests/test_handshake.gd` 的 `INFRASTRUCTURE_TESTS` 与
`tools/run_tests.gd` 的 `TEST_SCRIPTS` 双双清空，旧 GD 链实跑 **0 条断言**。
**验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **11679 条 OK**（下限 11110 → **11667**，本族约 559 条）；
`run_tests.bat` **0 条 OK**（基线 546 → **0**）；`validate_tables.ps1` `PASSED`；
`run_all_checks.bat` **`ALL CHECKS: PASSED`（passed steps: 19）**。
**下一批（第 54 批 · P4 收口）**：~~三份 `.gd` 一起退休、`run_tests.bat` 改成转发~~ **✅ 已办**
（见「三点一零」；`test_case.gd` 因 `analysis_balance`／`analysis_perf` 还依赖它，留到第 55 批）。
## 三点零八、P4 第五十二批：`tests/test_handshake_repo.gd` → **新建** `tests/TestHandshakeRepo.cs` ＋ `TestHandshakeRepoFiles.cs`（2026-10-11）

"仓库与文件约定"这一族（597 行 → 288 ＋ 490 行）。十二条检查，拆成两份 `partial` 只为不撞 800 行硬上限。
**本族的看点**：
1. **残骸三兄弟**：`.uid`（Godot 4.4+ 给每个脚本生成，删源文件时不跟着走）、`.import`（导入元数据还在、
   源文件没了）、`data/tables` 的导入产物（靠 `.gdignore` 治好的那 254 ＋ 37 个）。三条都是"会骗后来的人
   「这东西还在」"，代价极低而没人盯。
2. **入库契约**：`sync_to_git.bat` 用 `git add .` 全量收，所以 `.gitignore` 的三条忽略面与
   `.gitattributes` 的 `*.bat eol=crlf`（最后一条命中规则才算）都得钉住——这两条本地看不出来
   （工作区文件本来就是 CRLF），**新克隆才有症状**。
3. **文本层的两种"被吃掉的转义"**：控制字符（反引号 e／f／a 变成 ESC／FF／BEL）与
   TAB 紧跟字母（反引号 t 被吃掉）——都要**按字节**扫，光看 markdown 渲染看不出来。
4. **台账两向**：800 行硬上限的清单（超了没登记要红、登记了却已拆到 ≤800 行也要红）＋抬头
   「量：N 个超标」与实测数对账；解析本身另有内存负向用例（列取错、末尾换行多数一行）。
5. **字号两档**：`scenes/*.tscn`／`src/**`（`.cs` 一起扫）／theme 里的字面量只许 12／24，
   `0` ＝不覆写放行；同样带解析的内存负向用例。
**迁移期踩到的**：删掉 `.gd` 之后，`docs/design/15_美术风格需求.md` 里那句带目录前缀的
`tests/test_handshake_repo.gd::_check_font_size_tiers` 当场被「策划文档路径必须存在」那条抓出来
（这条门限只认 `.gd`／`.bat`／`.ps1` 等后缀，`.cs` 不在名单里）——顺手改成 C# 名字。
**验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **11120 条 OK**（下限 10169 → **11110**，本族 935 条）；
`run_tests.bat` **556 条 OK**（基线 1481 → **546**，本族退出本链少 925 条）；
`validate_tables.ps1` `PASSED`；`run_all_checks.bat` **`ALL CHECKS: PASSED`（passed steps: 19）**。
**下一批（第 53 批）**：~~`tests/` 只剩 **3 `.gd`**（一族 `docs` ＋基类 `test_handshake.gd`／`test_case.gd`）~~ **✅ 第 53 批已搬完**（`docs` 一族，见「三点零九」）——只剩基类与 `tools/run_tests.gd`／`tools/run_tests.bat` 待退休（P4 收口，之后进 P5）。

## 三点零七、P4 第五十一批：`tests/test_handshake_code.gd` → **新建** `tests/TestHandshakeCode.cs` ＋ `TestHandshakeCodeGuards.cs`（2026-10-11）

"代码契约（preload／单一定义／存档字段）"这一族（602 行 → 321 ＋ 500 行）。十一条检查，
拆成两份 `partial` 只为不撞 800 行硬上限（注册进 `Suites` 的是 `TestHandshakeCode.cs`）。
**本族特有的四处**：
1. **节点契约要连同类分片一起收**：`.tscn` 的 `ExtResource` 指向哪份 `.cs` 就读哪份，再按 `partial class`
   把同类其它分片补进来（`battle_screen` 的 20 处 `_require_node` 全在 `BattleScreenView.cs`，
   只读主文件会让覆盖数 49 → 29）。
2. **两个存档数字从代码现算**：`框架说明` 的「当前版本 vN」对 `GameState.save_version()`、
   「字段数（现在是 N）」对一个全新 `GameState` 的 `to_dict()` 键数。
3. **扫面按语言分叉**：绝对路径（`get_node("/root` ↔ `GetNode("/root`）、`to_dict` 的字段与键
   （`^var x :=` ↔ `^public X x {`）、随机源、战斗界面。两种语言必须一起收，否则搬一批就静默少一批。
4. **两向维护的白名单**：`TEST_ONLY_API_ALLOWED`（接口删了要红、被接进游戏也要红）、
   `CsvDirWhitelist`、`SingleSourceRules` 的 `also`。
**两个坑（下批可能还有）**：
① `System.IO.Path.GetDirectoryName` 在 Windows 上把 `/` 换成 `\`，拼出的
`parent="Panel\Margin\Column"` 在 `.tscn` 里永远找不到（当场红 11 条）——按 `/` 自己切，
才与 GDScript 的 `get_base_dir()` 同口径。
② 「只被测试调用的接口」原来用 `\b名字\b` 数调用方：.NET 默认 `\b` 把汉字算作词字符（`\w`），
PCRE2 的 `\b` 是 ASCII 口径——挨着汉字的标识符在两边数出来不一样。改成把语料里的 ASCII 标识符
切开数一遍（一次扫描代替「每个名字扫全篇」，口径与老链一致）。
**验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **10179 条 OK**（下限 9816 → **10169**，本族 353 条）；
`run_tests.bat` **1491 条 OK**（基线 1828 → **1481**，本族退出本链少 347 条）；
`validate_tables.ps1` `PASSED`；`run_all_checks.bat` **`ALL CHECKS: PASSED`（passed steps: 19）**。
**下一批（第 52 批）**：`tests/` 剩 **4 `.gd`**——两族（`docs` 708／`repo` 597）＋基类
`test_handshake.gd`（757）＋`test_case.gd`（291）；两族搬完再把
`tools/run_tests.gd`／`run_tests.bat`／`test_case.gd` 一起退休，两条链并成一条。

## 三点零六、P4 第五十批：`tests/test_handshake_doccounts.gd` → **新建** `tests/TestHandshakeDoccounts.cs`（2026-10-11）

"文档计数与引用（含美术需求对账）"这一族（510 行 → 645 行）。搬法与第四十九批同形
（逐条对位、注释压缩、强类型常量）。**本族特有的三处**：
1. **数字从文档现读**：02 的三个百分比（背袭／奇袭／潜行）从 `02_地图与明雷.md` 正文抓，
   再与 `combat_const` 两行和 `PlayerController.sneak_ratio()` 比——策划改文档忘改表/代码就会红。
2. **符号语料只收代码**：`设计实现对照` 的 ✅ 行点名的符号去 `src/scenes/tests/tools` ＋
   `project.godot` 里找（把文档也收进来等于自证）；带点的按 `.` 拆开**逐段**找；只看**状态列整个是 ✅**
   的行（半 ✅ 半 ⛔ 的行不算）。
3. **计数声称两边都要扫到**：15 §4.3 的逐档数量、标题总数、美术交接单括号内成分之和——
   「一处都没扫到」也是红（措辞改了要连着改门限，别让它静默失效）。
**搬法差异**：`db.tables[name].get("rows")` 换 `db.rows(name).Count`；`PackedStringArray` 换 `List<string>`；
符号语料用 `StringBuilder` 拼（跟 `+=` 同结果、少一次平方级复制）；`preload(...).sneak_ratio()`
换 `PlayerController.sneak_ratio()`（新增 `using Lunhui.World;`）。
**验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **9826 条 OK**（下限 9727 → **9816**，本族 89 条）；
`run_tests.bat` **1838 条 OK**（基线 1912 → **1828**，本族退出本链少 87 条）；
`validate_tables.ps1` `PASSED`；`run_all_checks.bat` **`ALL CHECKS: PASSED`（passed steps: 19）**。
**下一批（第 51 批）**：`tests/` 剩 **5 `.gd`**——三族（`code` 602／`repo` 597／`docs` 708）
＋基类 `test_handshake.gd`（757）＋`test_case.gd`（291）；三族搬完再把
`tools/run_tests.gd`／`run_tests.bat`／`test_case.gd` 一起退休，两条链并成一条。

## 三点零五、P4 第四十九批：`tests/test_handshake_numbers.gd` → **新建** `tests/TestHandshakeNumbers.cs`（2026-10-11）

"文档里写死的数字与计数"这一族（460 行 → 536 行）。搬法与前一批同形（逐条对位、注释压缩、强类型常量）。
**本族特有的三处**：
1. **公式要现算、不手抄**：01 的槽位采样表逐行拿 `GrowthCalculator.create(db)` 的
   `active_slots`／`passive_capacity` 重算再比（`growth_const` 或采样点一改就红）。
2. **两份文档对同一个数**：闭环段数（`tools/CheckLoopRunner.cs` 里的 `// ---------- ①` 是唯一出处）、
   「特殊效果 id 共 N 条」（看板 ＋ 设计实现对照）、「N 张表 / M 行」（看板 ＋ 设计实现对照）、
   场景自检个数（AGENTS.md／当前状态.md／README.md 三份）。**一段都扫不到也红**——别让声称悄悄消失。
3. **历史叙事行要跳过**：对照表里 `N 条判定`／`N / M 部武学` 的比对先看这一行**命中位置之前**
   有没有「以前／当时／当年」（`LooksHistorical`），有就跳过——整行跳过会把"前半句是现状"那半也漏掉。
**验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **9737 条 OK**（下限 9594 → **9727**，本族 132 条）；
`run_tests.bat` **1922 条 OK**（基线 2037 → **1912**，本族退出本链少 125 条）。
**下一批**：`tests/` 还剩 **6 `.gd`**——四族（`doccounts` 510／`code` 602／`repo` 597／`docs` 708）
＋基类 `test_handshake.gd`（757）＋`test_case.gd`（291）。
## 三点零四、P4 第四十八批：`tests/` 手写校验网开始搬——基类 ＋ 三族（2026-10-11）

**背景**：`tests/` 下剩 10 份 `.gd`，其中 9 份是**手写校验网**（八族 `test_handshake_*` ＋ 基类
`test_handshake.gd`／`test_case.gd`）。它们是 `tools/run_tests.gd` 与 `tools/run_tests.bat`
**能不能退休的前提**——手写网搬完，两条链才能并成一条。本批先把**基类 ＋ 最小的三族**落地。

**本批产物（6 份新 `.cs`，全部 ≤ 800 行）**：

| 文件 | 行数 | 管什么 |
|---|---|---|
| `tests/TestHandshake.cs` | 475 | 基类：全部常量（路径／状态白名单／`InfrastructureTests`／`UnsafeRngPatterns`／
`SingleSourceRules`／`TestOnlyApiAllowed` 等）＋拆出来的小类型（`CsvRow`／`CheckFloor`／`RowCountClaim`／`SaveSerializationTarget`／`SingleSourceRule`） |
| `tests/TestHandshakeFloors.cs` | 143 | `VERIFICATION_CHECK_FLOORS` 棘轮表（17 个场景自检的 `ok = ok &&`／`_problems.Add(` 条数下限） |
| `tests/TestHandshakeHelpers.cs` | 421 | 共用夹具：`ReadCsv`／`CollectFiles`／`CollectFilesWithSuffix`／`HasReference`／`AuditBat`／`AuditPs1`／`ReadRaw`／`CollectFlags`／`Split`／`CurrentDesignVersion`／`TableHasToken`／`BracedFrom`／`ToDictBody` |
| `tests/TestHandshakeContract.cs` | 289 | 一族：对接表／验证清单／版本对齐（八条检查） |
| `tests/TestHandshakeWiring.cs` | 540 | 一族：接线（自检开关／冒烟清单／场景可达／文档目录／Q 编号／卡点引用） |
| `tests/TestHandshakeFlags.cs` | 575 | 一族：旗标与可达性（被要求的旗标都有来源／支线好感对账／套装档位可达） |

旧 `tests/test_handshake_{contract,wiring,flags}.gd` ＋ `.gd.uid` 已删；三族从 `tools/run_tests.gd`
的 `TEST_SCRIPTS` 退场，改注册在 `tools/RunTestsCs.cs` 的 `Suites` **最前面**——
它们读**共享表库**，而 `TestCreation` 会往表里注入一行自建角色模板，排到后面就可能多出一行。

**搬法（后面五族照这个来）**：
1. 判定**逐条对位、只换语言**，注释按「要点 ＋ 决策编号」压缩（全文历史叙事仍在 `docs/dev/decisions/`）。
2. 常量用 C# 类型（`sealed class` ＋ 数组／`Dictionary<string,string>`），**不用 `Godot.Collections`**、
   取数不走 `Variant`——这样门限的判定读起来是强类型的，也不受绑定细节影响。
3. 每份 ≤ 800 行；超了就按族／按职责拆 `partial`。
4. 八族在 `InfrastructureTests`（C# 那份）里逐个登记，理由是「各是一道门，不对应某一个策划模块」。

**两个坑（后面五族都会遇到）**：
1. **孤儿用例门限的扫描面要换**：`_check_no_orphan_tests` 原来只扫 `.gd`（`tests/` 里只剩 `.cs`
   之后就是空转）。C# 版改成扫 `.cs`，代价是「不是独立用例」的文件（基类／拆开的 `partial`／
   探针类／`HarnessSelfTest`／`TestRows`）都得进 `InfrastructureTests` 并写明理由；
   换来的是**新写的孤儿用例当场红**——那正是原来那条门限守的事。
   （实测：`tests/` 里 77 份 `.cs` 有 65 份已被 `验证清单.csv` 引用，剩 12 份是基类／`partial`／探针。）
2. **`_table_has_token` 的属性过滤要放宽**：GDScript 侧给 C# 行类成员打 2048（`ScriptVariable`），
   而 C# 侧 `GetPropertyList()` 拿到的是 4102（**不含** 2048，见 `tests/TestRows.cs` 的说明）——
   照抄 2048 那条过滤会让这条门限在 C# 里**一条都读不到**（静默失效）。C# 版认
   「`ScriptVariable` **或** `Storage`」。

**本批验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **9604 条 OK**（下限 8707 → **9594**；
三族各 729／133／25 条）；`run_tests.bat` **2047 条 OK**（基线 2847 → **2037**，三族退出本链少 812 条
——那边实跑 887，差额是 `_check_no_orphan_tests` 换扫 `.cs` 之后按文件逐条少算的几处）；
`validate_tables.ps1` `PASSED`；`docs/dev/验证清单.csv` 里 `set_system` 登记的
`tests/test_handshake_flags.gd` 已改指 `tests/TestHandshakeFlags.cs`。

**下一批（第 49 批）**：`tests/` 剩 **7 `.gd`**——五族（`repo` 597／`code` 602／`docs` 708／
`numbers` 460／`doccounts` 510）＋基类 `test_handshake.gd`（757）＋`test_case.gd`（291）。
五族搬完再把 `tools/run_tests.gd`／`run_tests.bat`／`test_case.gd` 一起退休，两条链并成一条。
**顺序建议**：先 `numbers`（它要读 `GrowthCalculator`／`GameState`／`SkillGrant` 的取值口），
再 `doccounts`（它扫 `tests/` 的后缀，搬完自己要去掉 `.gd` 那半），再 `code`／`repo`／`docs`。
## 三点零三、P4 第四十七批：跨场景闭环自检 `check_loop.gd` 搬 C#（2026-10-11）

| 旧链 | 新（C#） | 行数 | 管什么 |
|---|---|---|---|
| `check_loop.gd`（`tools/` 下） | `tools/CheckLoopCs.cs` | 817 → 25 | `.tscn` 入口外壳：把跑手 `CallDeferred` 挂到**根**下 |
| | `tools/CheckLoopRunner.cs` | ＋ 684 | 跑手 ＋ 编排（①–⑧ 段）＋ 小工具 |
| | `tools/CheckLoopRounds.cs` | ＋ 398 | ⑨ 隐藏 Boss／⑩ 招募链／⑪ 宝箱守卫／⑫ 终局三选一 ＋ 存档指纹 |

**入口形状的新东西（下批可能还会遇到）**：闭环每一步都要 `ChangeSceneToFile`，而换场景会把**当前场景**
释放掉。老链 `extends SceneTree`（脚本就是场景树本身，换场景伤不到它）；C# 只能挂场景，所以做成两层——
`.tscn` 根节点只当外壳，`_Ready` 里把跑手挂到 `Root` 下（**不是**当前场景的子节点），
于是换场景只释放"当前场景"那棵子树。**别试"让根节点自己跑完整闭环"**：第一步换完场景它就没了。

`tools/check_loop.bat` 那一行从 `--script`（`res://` 下的 `check_loop.gd`）换成
`res://tools/check_loop_cs.tscn`；旧 `.gd` ＋ `.gd.uid` 已删。搬完 `tools/` 只剩 **8** 份 `.gd`。

**回归口径**：新旧两版各跑一次，捞 `LOOP`／`LOOPCHECK` 那些行 `Compare-Object`——**只有一处数值差**
（战败被推离的 `回图距接触点` 47.2 ↔ 48.0；**同一份 C# 代码连跑两次也在 47.2／48.0 之间跳**，
是物理推挤的抖动，两版都满足 `> 22` 的判据），其余逐字相同。

**本批踩到的四条（下批继续用）**：

1. **布尔值的打印口径**：老链 `%s` 走 GDScript `str()`＝小写 `true`／`false`；C# 插值的
   `bool.ToString()` 是**大写** `True`／`False`——不补一手，搬完的第一次输出就会整片对不上
   （仓库既有约定：每个文件补一个 `private static string _bool(bool)`，`src/ui/` 那一批都是这么做的）。
2. **场景自检棘轮表的键是文件路径**（同「三点零二」第 2 条）：旧键（`tools/` ＋ `check_loop.gd`）要换成
   `tools/CheckLoopRunner.cs`，模式 `ok = ok and` → `ok = ok &&`，floor 仍 15。
   注意 `ok = ok && inLocal;` 那句**不能省成 `bool ok = inLocal;`**——省了正好少一条，floor 当场红。
3. **段数门限扫的是注释里的段标记**：`_check_loop_segment_count` 认 `^\s*# -{5,} `（GDScript 的注释符），
   搬 C# 后要换成 `^\s*// -{5,} [①…⑫]` 并改成读 `.cs`。**只认带圈号那行**——同文件里还有
   `// ------ 段`／`// ------ 小工具` 这种分区线，不排掉就会多数两段（门限当场红成 14）。
   不换扫描面的话它会**静默退化成 0 段**。
4. **鸭子类型的字段别钉死行类**：房间队伍的 `team_row` 是 `EnemyTeamRow`，明雷的才是 `RoamingSpawnRow`
   （老链 `candidate.team_row.team_id` 本来就是鸭子类型）。钉成 `RoamingSpawnRow` 之后
   "大寨主那支队伍"找不到，⑫ 段当场红。取法：`candidate.team_row.Get("team_id").AsString()`。

**本批验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **2857 条 OK**（基线 2868 → **2847**：
少的 19 条是 `docs/dev/` 里 19 处 `check_loop.gd` 的全路径指针改指 `.cs` 之后不再被按引用逐处记断言）；
`run_tests_cs.bat` **8717 条 OK**（下限 8707 未动）；`validate_tables.ps1` `PASSED`；
`check_maps.bat` `MAPCHECK: OK`；`check_loop.bat` `LOOPCHECK: OK`；`run_all_checks.bat`
**`ALL CHECKS: PASSED`（passed steps: 19）**。

## 三点零二、P4 第四十六批：地图验收器 `verify_maps.gd` 搬 C#（2026-10-11）

地图验收（07 第九节那 6 条：命名归属／连通性／可绕背袭／侧向通路／巡逻／性能）整体搬 C#，
**一个 `.gd` 拆成三份 `partial`**（拆的理由只有 800 行硬上限一条）：

| 旧链 | 新（C#） | 行数 | 管什么 |
|---|---|---|---|
| `verify_maps.gd`（`mapgen/` 下） | `tools/mapgen/VerifyMapsCs.cs` | 890 → 545 | 入口 ＋ 基础设施（载入场景／洪水填充／挡路判定／瓦片预算／只许 Decor 挡路／条件层） |
| | `tools/mapgen/VerifyMapsCsChecks.cs` | ＋ 698 | 命名与归属／连通性与通路 |
| | `tools/mapgen/VerifyMapsCsChecksAssets.cs` | ＋ 419 | 归属与资产（固定敌人／回程出口／NPC 位点／观察点／建筑与设施） |

`tools/check_maps.bat` 那一行从 `--script`（`res://` 下的 `mapgen/verify_maps.gd`）换成
`res://tools/mapgen/verify_maps_cs.tscn`（入口形状同「三点零一」），旧 `.gd` ＋ `.gd.uid` 已删。
搬完 `tools/mapgen/` **一个 `.gd` 都不剩**。

**回归口径（这批比地图生成器那批好判）**：新旧两版各跑一次，把 `[check]`／`MAPCHECK`／`===` 那 **27 行**
捞出来 `Compare-Object`——结果 **`IDENTICAL`**；`check_maps.bat` 复跑仍 `MAPCHECK: OK`（退出码 0）。

**本批踩到的四处（下批继续用）**：

1. **800 行上限对 `.cs` 一样管**（同「三点零一」第 1 条）：初稿 978 行被 `_check_file_size` 点名，
   按「基础设施 ↔ 逐条检查 ↔ 归属与资产」轴拆成 698＋419。
2. **场景自检棘轮表的键是文件路径**：`VERIFICATION_CHECK_FLOORS` 里那条 `"mapgen/verify_maps.gd"`
   一删就红（读不到文件＝读得到空串）。改法是**按拆出的三份各登一行**：`_problems.Add(` **10／15／18**，
   合计 43，与旧 `.gd` 的 `_problems.append(` 逐条对齐——**别只登一份，那等于把另两份的检查放空**。
3. **删 `.gd` 会让 `docs/dev` 全树指针变红**（同「三点零一」第 2 条）：本批改了 9 处
   （`框架说明.md`／`地图搭建说明.md`／`代码拆分清单.md`／`CSharp迁移计划.md`／`decisions/` 三份／
   两份交接单）。**另有一条门限扫 `docs/design` 全树**（`_check_design_docs_paths_exist`，只豁免
   `archive/`）——`07_地图资源需求.md` 那处要一起改，别以为设计文档不归程序管就不会红。
4. **取值口会变成孤儿（本轮有意不动）**：`LocalMapController.building_distance()`／`facility_labels()`
   与 `MapKit` 那串 `t_*()`／`tile_px()`，唯一调用方就是这份校验器——现在它们**没有 GDScript 调用方**了。
   留着无害（那道「只被测试调用」的门限认不出 `public static`，不会因此报警），**收回是 P5 收尾的事**。

**本批验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **8717 条 OK**（下限 8707 未动）；
`run_tests.bat` **2873 条 OK**（基线 2868 未动，余量 5 < `SLACK_LIMIT` 100）；
`validate_tables.ps1` `PASSED`；`check_maps.bat` `MAPCHECK: OK`；`check_loop.bat` `LOOPCHECK: OK`；
`run_all_checks.bat` **`ALL CHECKS: PASSED`（passed steps: 19）**。

## 三点零一、P4 第四十五批：四个地图生成器整体搬 C#（2026-10-11）

| 旧链 | 新（C#） | 行数 |
|---|---|---|
| `build_overworld.gd` | `tools/mapgen/BuildOverworldCs.cs` ＋ `BuildOverworldCsMarkers.cs` | 473 → 686 ＋ 225 |
| `build_local_maps.gd` | `tools/mapgen/BuildLocalMapsCs.cs` ＋ `BuildLocalMapsCsDungeons.cs` | 498 → 541 ＋ 375 |
| `build_heifengzhai.gd` | `tools/mapgen/BuildHeifengzhaiCs.cs` | 262 → 425 |
| `regen_tilesets.gd` | `tools/mapgen/RegenTilesetsCs.cs` | 27 → 52 |

**为什么四个一起搬**：它们都是「一屏常量表 ＋ 调 `MapKit`」，`MapKit` 上一批已经就位；拆开搬等于把
同一套 `.tscn` 入口形状写四遍。搬完 `tools/mapgen/` 只剩 `verify_maps.gd` 一个 `.gd`。

**入口形状（本批定的规矩）**：`--script` 只认 GDScript，**C# 工具只能挂场景**——每个工具一个四行
`.tscn`（`build_overworld_cs.tscn`／`build_local_maps_cs.tscn`／`build_heifengzhai_cs.tscn`／
`regen_tilesets_cs.tscn`），脚本是 `Node`，`_Ready()` 里
`Callable.From(() => GetTree().Quit(Run())).CallDeferred();`。

- **不能直接在 `_Ready()` 里跑**（会挂在「根节点正在装配子节点」那个窗口里，同 `RunTestsCs` 的注释）。
- **只加 `.cs` 不加 `.tscn` 跑不起来**——`--script res://…cs` 会静默当成不认识的脚本。
  下批搬 `verify_maps` 时 `tools/check_maps.bat` 也要跟着换这一行。
- `docs/dev/地图搭建说明.md` 的「怎么改」三条命令已换成新入口，并补了
  `call tools\ensure_csharp_build.bat` 那句提醒（改了 `.cs` 不重编，引擎加载的还是旧程序集）。

**数据形状照抄，别图省事**：常量表一律用 `Godot.Collections.Dictionary`／`Array`（**保插入序**），
不用 C# 的 `Dictionary`（不保序）——地图是逐格 `set_cell` 铺的，遍历次序一变就可能改出另一个 `.tscn`。
CSV 单元格是**字符串**（`MapKit.read_csv` 存的就是文本），取数要走 `AsString().ToFloat()`／`ToInt()`，
直接 `AsDouble()` 拿不到；同理 `nodes[id]["reveal"]` 存的是 `bool`，要用 `AsBool()`——`AsString()` 不会替你转。

**回归口径**：改前先把七张 `.tscn` 与六套 `*_tileset.tres` 拷到 `.logs/mapgen_backup_<时间戳>/`，
改完逐行比。结果是七张 `.tscn` **只差 Godot 每次重存的 `unique_id`**（逐行过滤掉那一段后 `diff=0`）；
`village_tileset.tres` 只有两处 `TileSetAtlasSource_*` 自动编号不同——**旧 `.gd` 版现在也产出同一组编号**，
磁盘上那份是更早一次存盘留下的（不是这一批改出来的）。`check_maps.bat` `MAPCHECK: OK`、
`check_loop.bat` `LOOPCHECK: OK`。

**本批验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **8717 条 OK**（下限 8707 未动）；
`run_tests.bat` **2873 条 OK**（基线 2868——比上一批少的 7 条是文档里那几条工具指针被去掉之后
`_check_doc_tool_references` 少扫到几处）；`validate_tables.ps1` `PASSED`。

**本批踩到的两条（下批继续用）**：

1. **800 行硬上限对 `.cs` 一样管**：`BuildOverworldCs.cs`（900）与 `BuildLocalMapsCs.cs`（896）
   被 `tests/test_handshake_repo.gd::_check_file_size` 当场点名，按「骨架／表现」轴拆成 `partial`
   （**只挪位置、不改行为**）才过。**一开始就按行数估、先拆再写**，别写完再回来切。
2. **删 `.gd` 会让 `docs/dev` 全树的指针变红**：`_check_doc_tool_references` 扫的是 `docs/dev/**/*.md`
   （**含 `decisions/` 与归档交接单**），命中 `tools/…gd` 就必须真实存在。本批改了 6 处文档指针
   （`decisions/决策_341-377.md` 两处、`当前状态.md` 两处、`地图搭建说明.md` 一处、`交接/小策划.md`、
   `交接/小程序_归档.md`）——**历史叙事里改指针、不改内容**：把 `tools/mapgen/` 目录前缀去掉、
   只留文件名，或指向新的 `.cs`。顺带把 `MapKit.cs` 头上那句「GDScript 侧还有五个调用方」
   改成一个（只剩 `verify_maps.gd`）——**这类注释过期比不写更坏**。

## 三点零零、P4 第四十四批：旧链 `map_kit.gd` → **新建** `tools/mapgen/MapKit*.cs`，旧链 `test_map_assets.gd` → **新建** `tests/TestMapAssets*.cs`（2026-10-11）

地图工具箱（旧链 `map_kit.gd` 876 行）整体搬成 C#，**与用例同一批**：
`tests/test_map_assets.gd` 原先 `load("…map_kit.gd")` 取主题清单，工具箱不跟着搬就当场断链。
拆法与「二点九九」同——两边都不许撞 800 行硬上限：

| 文件 | 行数 | 管什么 |
|---|---|---|
| `tools/mapgen/MapKit.cs` | 697 | 常量（86 个 `T_*`／`TILES_*`／`THEMES`／`HOUSE_*`）＋ 给**还在 GDScript 里**的五个生成器用的取值口（`tile_px()`／`source_id()`／`t_grass()`／`themes()`…，GDScript 读不到 C# 的 `const`，同坑⑩）＋ 主题 TileSet 构建与存盘 |
| `tools/mapgen/MapKitPaint.cs` | 509 | 地形算法／绘制／建筑／迷雾（对位原「地形算法／建筑／迷雾」三节） |
| `tools/mapgen/MapKitExport.cs` | 约 240 | 存盘／预览 dump／CSV 读（对位原「存盘与预览」「CSV」两节） |
| `tests/TestMapAssets.cs` | 654 | 场景 Marker ↔ 配置表双向比对、房间／触发／观察点／事件判定／敌人位点与回程出口、工具方法（**注册进 `Suites` 的就是这一份**） |
| `tests/TestMapAssetsAssets.cs` | 421 | 地表分层／六套主题 TileSet 逐格碰撞与 covering／三类图标文件名对表／指路牌（**不注册**，partial 的另一半） |

| 旧链（`map_kit.gd` ＋ `test_map_assets.gd`） | C# | 盯什么 |
|---|---|---|
| `MapKit` 的常量与几何／绘制／导出函数 | `MapKit`／`MapKitPaint`／`MapKitExport` | 逐条对位；**`hash01` 必须返回 `double`、全程 64 位长整型**（GDScript 的 `float` 是 64 位，收成 32 位会改出另一张图）——已逐格比对 1600 格全等 |
| 用例十节（断言 379／静态 32） | `TestMapAssets*`（实测 **378** 条） | 逐函数对位；**少的那 1 条**是旧链的 `check_false(kit == null, "读得到地图工具箱 map_kit.gd")`——工具箱搬成编译器里的静态类后，「加载失败」这条路不存在，没有对应物 |

**调用面**（旧链）：`build_overworld.gd`／`build_local_maps.gd`／`build_heifengzhai.gd`／
`verify_maps.gd`／`regen_tilesets.gd` 五个生成器（`preload` 换 `.cs`、`MapKit.UPPER_CASE` 换
`MapKit.lower_case()`、GDScript 侧把可选参数显式补满）；`scenes/` 与 `assets/tilesets/` 的产物
**逐字节回归**过（6 张被重生成的小地图 scenery 只差 `unique_id` 数字、7 张 TileSet `.tres` 哈希不变）。
`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉这一行用例 ＋ `MIN_ASSERTIONS` 3251 ⇒ **2868**；
`tools/RunTestsCs.cs` 的 `Suites` 加 `res://tests/TestMapAssets.cs`、`MinAssertions` 8329 ⇒ **8707**。
`docs/dev/验证清单.csv`／`代码拆分清单.md`（876 那行划掉、抬头 10 ⇒ 9）／`设计实现对照.md` 一并跟上。

**本批实测结论（写死别再试）**：

1. **GDScript 完全读不到 C# 的 `const`／静态字段**（连 `public const int`／`public static readonly` 都读不到，
   取属性返回 `null`）——**但静态方法一定能调**。所以「常量」一律开成**小写静态取值口**，这是本批给
   五个生成器补的那一层。
2. **C# 类要让 GDScript 调到静态方法，必须是 `public partial class X : RefCounted`**（要继承 Godot 类型），
   而且改完要跑一次 `--headless --editor --quit` 刷脚本缓存；纯 `public static partial class` 会报
   `Static function "xxx()" not found in base "CSharpScript"`。
3. **GDScript 调 C# 静态方法不会用 C# 的可选参数默认值**（同「二点九九」坑①）：少传就报
   `Too few arguments`，GDScript 侧要把实参补满。
4. `string.StartsWith(string)` 是**文化敏感**比较：`"building_id".StartsWith("\uFEFF")` 会返回 `true`——
   判 BOM 要用 `header[0][0] == '\ufeff'`（逐字符）。本批的生成器真被它把头列削成一格过。
5. Godot 的 `FileAccess.get_csv_line()` **已经吃掉 UTF-8 BOM**（实测首字符就是 `'b'`），不用自己再剥。
6. Godot 4.7 C# 的几处对不上：`Json.Stringify`（不是 `JSON.Stringify`）／`TileSetSource` 没有
   `GetTileData`（要 cast 成 `TileSetAtlasSource`）／`Godot.Collections.Array<T>` 用 `.Count`
   不是 `.Length`／`Substring` 没有单参重载／可选参数写 `Vector2 offset = default`（别写 `Vector2?`）。

### 顺带记一次事故与还原（2026-10-11，比上面「二点二九」那次大）

**事故**：本轮收尾时用「批量改名」脚本改 21 个文件里的 `map_kit.gd`／`test_map_assets.gd` 指针，
脚本里那句 `r=@(@('旧','新'))` 被 PowerShell 的 `@()` **展平**成一个两元素的**字符串**数组，
于是循环把「字符串的前两个字符」当成了 old／new —— 每个文件退化成一次**字符级全局替换**：
`t→e`（配 `T→e`）、`t→o`、`m→a`（配 `M→a`）。**21 个文件全中，含两个代码文件**
（`src/world/LocalMapController.cs`、`tests/test_handshake_doccounts.gd`）与五个 `tools/artgen/*.js`。

**还原办法**（与「二点二九」那套同源，但这次语料更大）：

1. **先认清是哪种替换**：每个文件的坏法都能由「原文件名／新文件名前两个字符」反推出来，
   写死进一张 `文件 -> 替换类型` 表（这是还原的前提，不然无从下手）。
2. **跟 `HEAD` 对齐的行逐字照抄 `HEAD`**：把 `HEAD` 原文按同一种替换弄坏，与坏文件做
   `difflib` 对齐——**相等的那些行直接取 `HEAD` 原文**（这一条最可靠，占了绝大多数）。
3. **剩下的按「仓库词表」反解**：语料 = 全仓库未损坏的文件 ＋ `HEAD` 全树（已删掉的旧 `.gd`
   也贡献词），词频加权、**只接受命中唯一的候选**，命中多个／一个都没有的留清单。
4. **拿编译器／用例当验证**：`dotnet build` 把 `LocalMapController.cs` 里两个猜错的词
   （`GetCustomDataLayerNames` 被我猜成 `GeoCustomDataLayerName`）当场抓出来；`run_tests.bat`
   把文档侧的引用门限全跑一遍。JS 用 `node --check`。
5. **自检口径**：修好的文件按同一种替换再弄坏一次，必须与原文件**逐字节相同**——21 个文件里
   20 个 `True`；`验证清单.csv` 唯一一处 `False`，是因为它原文里本就有一个错字（`\tests/` 少了个 `t`，
   兄弟文件 `模块对接表.csv` 同一句写的是 `tests/`），顺手订正了。

   **⚠️ 这条自检有盲区（2026-10-11 晚补记）**：它只会看「反解出来的东西再弄坏一次像不像」，
   **盖不住"把逆替换施加到了本来没坏的行上"**——那类行弄坏一次照样自洽。实测漏网：
   `docs/dev/decisions/决策_341-377.md` 有三处**原本完好的** `a`／`as` 被逆替换成了 `m`／`ms`
   （`m file with m user-mapped`、`const X := ["m","b"]`、`get_main_loop() ms SceneTree`），
   当场过了自检、两条链也全绿（那几处是散文，没有门限看得见）。事后按「孤立 ASCII 短词表」
   （全文扫 `\bm\b`／`\bms\b` 与一两个字母的独立词）才捞出来，已改回。**下次做这类还原，
   除了"弄坏一次"还要再扫一遍「短词表」**：`a`／`as`／`an`／`at`／`by`／`it`／`no`／`or`／`to`
   这种孤立词在中文散文里本来就少，多出来一个就是嫌疑。

**教训**：**别用「整文件替换」的批量脚本**；`@(...)` 会把嵌套数组展平、`$pair[0]` 落到**字符**上而不是数组元素上。
非要用就先复制副本、跑完立刻 `rg` 一遍特征词（`eools`／`eese_`／`assees`／`Godoe`／`Acoion` 这种一眼假的东西）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **8717 条 OK**（下限 → **8707**）；
`run_tests.bat` **2878 条 OK**（基线 3251 ⇒ **2868**，少的就是搬进 C# 链的那 379 条）；
`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`；五个 `tools/artgen/*.js` `node --check` 全过。

## 二点九九、P4 第四十三批：`tests/test_battle_ui.gd` → **新建** `tests/TestBattleUi.cs`（＋两份 partial）（2026-10-11）

战斗界面这一份是 `tests/` 里最大的一件（旧链 1294 行、**267 条断言**）。走**第一档：C# 侧原先没有
对应文件 ⇒ 新建**，并按旧链的顺序拆成三份 `partial`（都不撞 800 行硬上限）：

| 文件 | 行数 | 管什么 |
|---|---|---|
| `tests/TestBattleUi.cs` | 706 | 骨架：`Run()`＋四道闸门常量＋这一场的手打；布局／场景节点／异常状态与跳字／三种资源条；小工具（造战斗、算敌人总血、造跳字事件） |
| `tests/TestBattleUiSettles.cs` | 499 | 结算那几支：卡片契约／逃跑三态／败北·平局·练习战零收获／自动存档失败／首杀一次／绝境解锁／队伍胜旗标与账册 |
| `tests/TestBattleUiCommands.cs` | 438 | 指令那一支：招式按钮／增益招式／六指令与合并面板／预兆与拆招／自动战斗（含持久开关）／敌人剪影 |
| `tests/FailingSaveStore.cs` | 23 | 旧链那个**文件内部类** `FailingStore` 单开一份（见下面的坑①） |

| 旧链（`tests/test_battle_ui.gd`） | C# | 盯什么 |
|---|---|---|
| `run`（挂真场景 ＋ 注入存档设施与回程回调） | `TestBattleUi.Run` | 四道闸门常量走**取值口**（`auto_interval()` 等，GDScript 读不到 C# 的 `const`，坑⑩）＋这场打**两只**野狼（决策 287） |
| `_check_layout`／`_check_scene_layout` | 同名 `_CheckLayout`／`_CheckSceneLayout` | 开场文案、逃跑按钮置灰、日志、场景节点路径（`Margin/Column/…`）、滚动区高度、动态卡片挂在场景的敌人列表下 |
| `_check_status_ui` | `_CheckStatusUi` | 状态行「中毒 ×1」与伤害类型色；DoT 跳字同色；多段跳字横向错开且挂在 `FloatLayer` 上（不在卡片里）；**把卡片摆到右边缘**验落点夹取；阵亡压暗标「（倒）」；出手蓄势／受击压扁的分帧；濒死「危急」；掉落明细写名字与词条 |
| `_check_rounds` | `_CheckRounds` | 回合读数、调速三档、策略轮换、逃跑按钮态、自动开关、点敌人选目标后打的是它 |
| `_check_finish` | `_CheckFinish` | 脚本化手打到结束、命中音效、熟练度写回存档、结算文案、两个按钮的禁用／回程 |
| `_check_resource_bars` | `_CheckResourceBars` | 气血／内力／架势三条的**读数与上限**、刷新后跟着单位走、`poise_critical`／`poise_break` 两层临界贴图 |
| `_check_effect_icons` | `_CheckEffectIcons` | `buff_def.icon`／`status_effect.icon` 两列**真的有人在读**、空则退回行 id、「有图 ⟺ 文件在」、真战斗里那条 chip 也带表里的路径 |
| `_check_settle_card_contract` | `_CheckSettleCardContract` | 卡片三行（战果／掉落／升级），放不下的武学那行**必须进战报**且不挤进卡片 |
| `_check_flee_ui` | `_CheckFleeUi` | 开打前置灰／轮到我方可按且 tooltip 写**现算**成功率／`force_flee` 撤退成功、不结算奖励、不明雷置清 |
| `_check_defeat_settle` | `_CheckDefeatSettle` | 全队 0 血 → 败北：零收获（钱／经验／装备／材料／首杀）＋气血回满＋战斗外增益清空＋没进过城就退回大地图＋败北音效＋照旧自动存档 |
| `_check_practice_settle` | `_CheckPracticeSettle` | 练习战：卡片写清、不发光不发钱不记首杀、经验到顶就给零、**熟练度卡在上限**、不进副本完成度 |
| `_check_draw_settle` | `_CheckDrawSettle` | 打满上限 → 平局（不是「败北」也不是「胜利」）、同样零收获、照旧自动存档 |
| `_check_skill_buttons_ui` | `_CheckSkillButtonsUi` | 招式按钮能用的可点、内力清零后**置灰并写「内力不足」**、绕过置灰硬按被拒 |
| `_check_support_skill_button` | `_CheckSupportSkillButton` | 增益招式按钮写「增益」（不是「倍率 0.0」）、按下给自己上 buff、不打敌人 |
| `_check_command_ui` | `_CheckCommandUi` | 四个指令按钮在场景里；道具**置灰写明「效果」未配**；内功催动的消耗取自 `skill_passive.qi_cost`；增益减益**合并列表**（`▼减`／`▲增`／`×N`／`∞`／写清挂在谁身上／减益排前面） |
| `_check_parry_ui` | `_CheckParryUi` | 预兆行、拆招按钮的 tooltip **现算**折数与「反涨架势」、按下后状态栏播报＋架势反涨＋预兆行标「已被拆招」 |
| `_check_auto_battle` | `_CheckAutoBattle` | 自动战斗能**自己打到底**（有限次 tick 内获胜）、结束自动关掉、结算卡与胜利音效 |
| `_check_auto_battle_setting` | `_CheckAutoBattleSetting` | 设置关着不自动／开着进战斗即自动／点指令立刻接管（`take_over` 幂等） |
| `_check_first_kill_once` | `_CheckFirstKillOnce` | 首杀武器只给一次（打完再打、**落盘再读回**再打） |
| `_check_boss_kills_unlock_nightmare` | `_CheckBossKillsUnlockNightmare` | 真打两场 Boss → 记进副本记录 → 困难／绝境按顺序解锁 |
| `_check_team_win_flags` | `_CheckTeamWinFlags` | 屠夫／毒手／大寨主三场 → 三个旗标各自置、账册只发一本 |
| `_check_enemy_sprites` | `_CheckEnemySprites` | 剪影路径拼法；**有图那行挂着图、没图那行不留空位** |
| `_settle_boss`／`_boss_encounter`／`_settle_named_encounter`／`_count_instances`／`state_mastery_*`／`_enemy_hp_total` | 同名 C# 私有助手 | 夹具：造一场、把敌人打死、走真 `_settle()`、只统计入账的增量 |

**这一批踩到的四个点**（都写在这里备查）：

1. **`FailingStore` 必须单开一份顶层类**：旧链是 `.gd` 文件里的内部类，靠鸭子类型给 `SaveService` 用；
   C# 侧只有**顶层** `partial class` 的方法才会被 `Call("save_slot", …)` 认到（`SaveService` 对非
   `SaveStore` 的注入走的就是 `Call`），所以新建 `tests/FailingSaveStore.cs`（方法名保持 snake_case）。
2. **C# 链的会话是「一个进程跑完全部用例」，旧链是「一个用例一个进程」**：败北那条的前提
   「这一局没到过城镇」在 C# 链里不天然成立（`TestLocalMap` 会写 `last_shelter_scene`），
   所以显式清一次再验（等价于旧链的前提，不是放宽断言）。
3. **拆文件不破坏假绿门限**：那道门限按**注册的那一份**数静态断言点（本批 105 处），而
   `Checks` 是三份合计（267）——它只在下界方向报错，所以「实际 ≫ 静态点」是正常的。
4. **位置断言照旧链的老办法**：headless 里容器没排版，所以自己写死
   `layer.Size = new Vector2(1152, 648)`、再把卡片摆到 `(1100, 120)`，让落点**明确越界**再验夹取
   （施工单「二点九七」里记过这条：`test_battle_ui` 那类断言走的就是这个形状）。

**连带修掉三处「随搬家缩水的网」**（都是旧链 `.gd` 一删就露出来的）：

- `tests/test_handshake_code.gd::_check_battle_tests_are_seeded`——原来只扫 `.gd`（认
  `BATTLE_SCENE).instantiate()`），战斗用例搬完就只剩它自己那一处自匹配；现在**同时扫 `.cs`**
  （认 `.Instantiate()` ＋ 同一行提到 `battle_screen`／`BattleScene`），并把下界贴到实测的 3 处
  （还顺手把这条门限自己的模式串从计数里剔掉，免得它自证）。
- `docs/dev/代码拆分清单.md`——`tests/test_battle_ui.gd`（1294 行）那行划掉，抬头「量：11 个超标」
  跟着改成 **10 个**（那条门限解析的就是这张表，少一行、计数不改也会红）。
- `docs/dev/设计实现对照.md` 第 220 行那句 `tooltip_text` 改成 `TooltipText`：那条 ✅ 行的符号要在
  **代码语料**里找得到，而这个 token 原来只存在于被删掉的 `.gd` 里（C# 侧是 `TooltipText` 属性）。

**还顺手改了六处「指着老文件名」的注释**（都是活的，接手的人会照着一个不存在的文件去找：
`src/ui/BattleScreen.cs` 导语与取值口那两句、`tools/analysis_mutations_code.ps1` 里七个
battle-screen 常量 cast 的「绝对值由谁钉住」说明、`tools/audit_table_usage.ps1` 的举例、
`tools/CheckLoopRunner.cs` 两处、`docs/dev/框架说明.md` 那张「谁按名字找这些节点」的表、
`src/ui/AGENTS.md`）。

**断言账**：

- C# 侧：**新建** `tests/TestBattleUi.cs`（＋两份 partial，`--- 战斗界面 ---` 那行 267 条／静态 105，
  与旧链那份「断言 267／静态 260」逐条对得上）⇒ 本链 8072 → **8339**（基线 `MinAssertions` 8062 → **8329**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_battle_ui.gd"` 行
  （改写注释说明去处）；`tests/test_battle_ui.gd` ＋ `.gd.uid` 已删 → 旧链 **3542 → 3265 条**
  （基线 `MIN_ASSERTIONS` 3531 → **3251**；净少 277 条 ＝ 用例自身 267 条 ＋ 随文件一起少跑的
  按文件逐条门限与那三处「缩水的网」改写后少掉的下界断言，再减本节文档引用带来的 +4）。
- `docs/dev/验证清单.csv`（有 BOM）：`item_system`／`battle_ui`／`audio_system` **三行**的
  `tests/test_battle_ui.gd` 都换成 `tests/TestBattleUi.cs`（**登记列的引用要一次搜干净**——
  这次三处，改完全表搜 `test_battle_ui` 已是 0 处）。
- `docs/dev/模块对接表.csv` **这一批不用动**（那几行的 `code_paths` 早已是 `.cs`）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **8339 条 OK**（基线 8329，连跑两遍一样）；
`run_tests.bat` **3265 条 OK**（基线 3251，含本节文档引用带来的 +4）；`validate_tables.ps1` `PASSED`；
`check_loop.bat` `OK`。
三份新文件 706／499／438 行，都没撞 800 行硬上限。

## 二点九八、P4 第四十二批：`tests/test_copy_audit.gd` → **新建** `tests/TestCopyAudit.cs`（2026-10-11）

玩家可见文案审计这一份走**第一档：C# 侧原先没有对应文件 ⇒ 新建**。旧链九节一条不落搬过去
（这份用例是「换对象审计」的产物：功能一路往前走、文案留在原地，于是把这类谎话变成绊线）：

| 旧链（`tests/test_copy_audit.gd`） | C# | 盯什么 |
|---|---|---|
| `run`（挂真场景 ＋ 走真输入映射） | `TestCopyAudit.Run` | 把 `main_menu.tscn`／`placeholder_game.tscn` 挂进真场景树（`tree.Root.AddChild`）；`InputSetup` 走真按键映射；`SaveStore.create(dir, slots)` 造隔离存档目录 |
| `_check_no_banned_copy_in_source` | `_CheckNoBannedCopyInSource` | 四句已经查实是谎话的句子（「玩法未接入」「玩法还没接」「尚未接入」「架势系统未实现」）在 `src/` 下任何源码里都不许再出现——**含注释** |
| `_check_display_name_helper` | `_CheckDisplayNameHelper` | 「id → 中文名」只有 `TableDb.display_name` 一处：道具／装备／装备实例 `#序号` 剥壳／房间取 `room_name`／伤害类型；**查不到就原样返回**（不许编假名字）、空 id 返回空串 |
| `_check_hud_mentions_every_key` | `_CheckHudMentionsEveryKey` | 五个按键的提示条逐个点名（这一节最容易漂——提示条被拆成多行拼接，C# 侧要连续行一起收） |
| `_check_menu_copy` | `_CheckMenuCopy` | 启动菜单三个按钮逐字（新建／读取／退出）；副标题要点名章节与已接入玩法；再递归扫 Label／Button 文字，不许出现表内 id 形态 |
| `_check_hub_copy` | `_CheckHubCopy` | 游戏内枢纽页的提示条指向大地图（不是「尚未接入」那种）；同一道 id 形态扫描 |
| `_check_stale_save_labels` | `_CheckStaleSaveLabels` | 存档里出现配表里没有的 id（下架角色／未知难度）时，标签与状态行也不许把 id 甩给玩家 |
| `_check_unsupported_damage_reason` | `_CheckUnsupportedDamageReason` | 招式列得出来、结算没接的伤害类型要报**中文名**而不是 `dot_poison`；能用时不给原因 |
| `_check_surprise_pending_rule` | `_CheckSurprisePendingRule` | 奇袭暂缓规则的口径写「设计未定」，而不是甩锅给架势系统 |
| `_check_no_table_names_in_player_text` | `_CheckNoTableNamesInPlayerText` | 拿 `data/tables` 的表名当词表扫 `src/` 的字符串字面量（按对象扫，不再按交互路径）；三条排除 ＋ 白名单**双向维护** |

两处口径统一（都是 C# 侧才暴露的）：白名单 `TableNameInTextAllowed` 的键从**路径**改成**文件名**
（与同一份里的 `_AllowedFile` 一致，两个文件同用一个词表时不再出现两套写法）；`String` 没有
`UnicodeAt`，扫描按 `text[i]` 取字符；`Encounter.pending_rules` 是 `string[]`，取 `.Length`。

**断言账**：

- C# 侧：**新建** `tests/TestCopyAudit.cs`（`--- 玩家可见文案审计 ---` 那行 62 条／静态 47，
  与旧链那份逐条对得上）⇒ 本链 8010 → **8072**（基线 `MinAssertions` 8000 → **8062**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_copy_audit.gd"`
  行（改写注释说明去处）；`tests/test_copy_audit.gd` ＋ `.gd.uid` 已删 → 旧链 **3604 → 3541 条**
  （基线 `MIN_ASSERTIONS` 3593 → **3531**；少 63 条 ＝ 用例自身 62 条 ＋ 随文件一起少跑的按文件逐条门限 1 条）。
  本节的文档引用会让旧链再 +1 条（`3541 → 3542`，历次新节都这样），所以**最终实测是 3542**。
- `docs/dev/验证清单.csv`（有 BOM）：`ui_copy` 一行的 `tests/test_copy_audit.gd` 换成
  `tests/TestCopyAudit.cs`；同一份里还有两处指名道姓的引用——`damage_pipeline` 正文那句
  `test_copy_audit`、`ui_copy` 正文那句 `test_copy_audit::_check_no_table_names_in_player_text`
  ——也一起改成 C# 文件与方法名。**登记列的引用要一次搜干净**（这次全表三处，改完全文搜
  `test_copy_audit` 已是 0 处）。
- `docs/dev/模块对接表.csv` **这一批不用动**（`ui_copy` 那行的 `code_paths` 本来就空，注记里也没有老用例路径）。
- 还指着老文件名的**活指针**顺手改掉（不然接手的人照着去 `tests/` 找一个已经不存在的文件）：
  `docs/dev/设计实现对照.md`（2 处）、`data/AGENTS.md`（1 处，`CopyGuard` 那条）、
  `docs/dev/待策划确认_详情.md`（1 处）、`src/ui/AGENTS.md`（3 处：禁用文案那条／
  `_CheckNoIdTokens`／HUD 键位那条），以及三处 C# 源码注释（`src/ui/MainMenu.cs` 副标题那句、
  `src/ui/CopyGuard.cs` 的判据来由、`src/core/MenuController.cs` 的「表名扫描只跳同一行 PushError」）。
  `docs/dev/decisions/` 与 `docs/dev/当前状态_归档.md` 是**历史记录**（前例 `test_local_map.gd`
  也留着），不追改。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **8072 条 OK**（基线 8062，连跑两遍一样）；
`run_tests.bat` **3542 条 OK**（基线 3531）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。
新文件 660 行，没撞 800 行硬上限。

## 二点九七、P4 第四十一批：`tests/test_character_ui.gd` → **新建** `tests/TestCharacterUi.cs`（2026-10-11）

角色与行囊界面这一份走**第一档：C# 侧原先没有对应文件 ⇒ 新建**。旧链十二节一条不落搬过去
（数据那一半在 `tests/TestCharacterSheet.cs`，两份分工不变：**这一份只查界面**——把
`character_screen.tscn` 挂进真场景树、按真按钮、读真文案）：

| 旧链（`tests/test_character_ui.gd`） | C# | 盯什么 |
|---|---|---|
| `run`（挂场景 ＋ 注入 `state_override`／`back_handler`） | `TestCharacterUi.Run` | `tree.Root.AddChild` ＋ `setup()`；C# 侧 `tree` 由运行器注入（`TestCase.tree`），`back_handler` 给一个 `Callable` |
| `_check_tabs` | `_CheckTabs` | 三个页签的标题与顺序、默认停在角色页、能切到背包页 |
| `_check_character_tab` | `_CheckCharacterTab` | 头部铜钱／经验；五维加号按钮在；**悟性·根骨置灰**且悬浮写「资质」、行内写「不可加点」；点加号写进存档且未分配点数减一；状态栏提示；非战斗技能行与「不含装备」；当前气血行；**五维行撑满整行**；图鉴奖励行 |
| `_check_skill_loadout` | `_CheckSkillLoadout` | 标题写清招式槽／内功容量；卸下→槽空→再点装回（状态栏两次提示）；装不上的拳法置灰；醉步标「增益招式」；内功特殊效果标「暂未生效」且**不把 `effect_` id 甩给玩家** |
| `_check_equipment_tab` | `_CheckEquipmentTab` | 武器槽与两格戒指槽、卸下后还剩上衣、候选标题写中文槽位名、装回、状态栏**不漏 `eq_` 与 `#实例序号`**、武器类型不匹配的候选置灰 |
| `_check_bag_tab` | `_CheckBagTab` | 堆叠数量「×2」、钥匙道具丢弃按钮禁用且拒绝有理由、丢材料减一、分类筛选用 `is_key_item` 语义（**铁镐是 `item_type=tool` 但算钥匙**） |
| `_check_skillbook` | `_CheckSkillbook` | 门槛不够研读按钮置灰且行里写原因、把智抬到 12 后可点、研读学会并消耗一本 |
| `_check_icons` | `_CheckIcons` | 路径拼接只有一处（`IconPaths`）；装备图标优先 `equip_base.icon`、空则退回 id、查不到也照 id 拼；**有图 ⟺ 有图标节点**（拿"文件在不在"当期望值，不写死某张还没交付的图，同一次审计里 `pf_xuanwei_05` 就是这么炸的）；`load_icon`／`make_icon` 取不到图时返回 null 不刷错误（用**永远不该存在**的 id，并把"它确实不存在"当前提） |
| `_check_back` | `_CheckBack` | 返回按钮触发回退（注入的 handler 被调一次） |
| `_check_no_points_ui` | `_CheckNoPointsUi` | 点数加完后加号**置灰**（不是点了才报错）、标题写「可加点 0 点」、绕过置灰硬调也拒绝 |
| `_check_big_bag` | `_CheckBigBag` | 23 行道具除铜钱外各堆满（22 种）＋ 30 件装备实例；实例 id 不重号；**存档往返**一件不丢；背包页列出 22 行与每件实例；整页仍塞进设计分辨率 |
| `_check_party_stress` | `_CheckPartyStress` | 4 人满员 ＋ 长名字 ＋ 六位数铜钱；成员行是 **Flow 容器**（HBox 时代超宽会被裁掉）且行最小宽度不超设计宽度；满员下仍能切页 |

**一处「空转的断言」换成了真的**（这是这批唯一的行为改动，写在这里备案）：旧链的
`_check_character_tab` 里有一条「五维行不能被压成竖排」，量的是 `get_line_count()`。
**那条链上它一直是空转的**——`--script` 模式的视口是 0×0，容器从没排过版，量出来恒为 1。
C# 链跑在真场景里（视口 1152×1152），同一处量出来是「宽度 1、换行 3 行」这种**没排版时的假象**
（容器的排序走消息队列，而运行器把全部用例跑在同一帧里、跑完就退，队列没机会刷——
`PropagateNotification(Container.NotificationSortChildren)` 也推不动真正的 `sort_children()`）。
所以改成钉**那条修复本身**：`AttrLabelstr.SizeFlagsHorizontal == ExpandFill`
（`CharacterScreenPages` 里的注释写着这个 bug：「力 5（裸 5 ＋ 装备 0）」曾被竖着排下来）；
版式是否真的塞得下由同一份用例里的 `LayoutBudget.fits(screen)` 两条（大背包／满员）继续量。
**后话**：`test_battle_ui.gd` 那类位置断言走的是另一条老办法——自己写死
`layer.size = Vector2(1152, 648)` 再量（搬它时照办）。

**断言账**：

- C# 侧：**新建** `tests/TestCharacterUi.cs`（`--- 角色与行囊界面 ---` 那行 135 条）⇒
  本链 7875 → **8010**（基线 `MinAssertions` 7865 → **8000**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_character_ui.gd"`
  行（改写注释说明去处）；`tests/test_character_ui.gd` ＋ `.gd.uid` 已删 → 旧链 **3739 → 3604 条**
  （基线 `MIN_ASSERTIONS` 3728 → **3593**）。
- `docs/dev/验证清单.csv`：`character_ui`／`equip_system`／`skill_system` **三行**的
  `tests/test_character_ui.gd` 都换成 `tests/TestCharacterUi.cs`（BOM 已核对；先只换了 `character_ui`
  那一行，旧链当场点名 `equip_system`／`skill_system` 两行——**登记列的引用要一次搜干净**）。
- `docs/dev/模块对接表.csv` **这一批不用动**（三行的 `code_paths` 早已是 `.cs`，注记里也没有老用例路径）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **8010 条 OK**（基线 8000）；
`run_tests.bat` **3604 条 OK**（基线 3593）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。
新文件 618 行，没撞 800 行硬上限。

## 二点九六、P4 第四十批：`tests/test_battle.gd` → **新建** `tests/TestBattle.cs`（2026-10-11）

战斗序列这一份走**第一档：C# 侧原先没有对应文件 ⇒ 新建**。旧链十四节一条不落搬过去
（`TestBattleSession.cs`／`TestMultihit.cs`／`TestPoise.cs` 那一族是**别的**用例，别把这份当成它们的重写）：

| 旧链（`tests/test_battle.gd`） | C# | 盯什么 |
|---|---|---|
| `run` ＋ `_check_absolute_constants` | `TestBattle.Run`／`_CheckAbsoluteConstants` | 阵营两个常量与普攻倍率／破架势、主动防御回架势 15% 的绝对值；外加 2026-10-10 补的六条**兜底值**（拆招五成、拆招回架势 15%、破绽增伤 50%、逃跑 50%／25%／95%）——它们只在「表里没这行」时才生效，用例全在有表的进程里跑，不钉住就是变异探针点名的 `[missed]` |
| `_check_turn_order` | `_CheckTurnOrder` | 身法高的先手；同速按**稳定次序**排（我方在前） |
| `_check_party_vs_team` | `_CheckPartyVsTeam` | 野狼群 3 只、5 级两人组能赢、回合数落在 1–20、日志非空、赢完敌人全倒 |
| `_check_rewards` | `_CheckRewards` | 普通 12×3 经验与 5×3 铜钱；绝境 exp ×1.7（round 20）与 money ×1.5（round 8）——**round 而不是截断** |
| `_check_difficulty_scaling` | `_CheckDifficultyScaling` | hp／atk／def 三档难度倍率**现算**（不写死数字）＋「越难越厚越疼」的单调性；敌人走共用派生管线但**不吃** `level_growth` 基础列 |
| `_check_determinism` | `_CheckDeterminism` | 同种子的回合数与日志**逐字一致** |
| `_check_round_cap` | `_CheckRoundCap` | 压到 3 回合就收手判平局；**不传上限**时打满默认 50 回合（改常数改成 10 要红） |
| `_check_telegraph_and_parry` | `_CheckTelegraphAndParry` | 每只敌人都有预兆且文案写清出什么招；拆招那一招**伤害减半**；反涨架势钉死「上限 ×15% = 6」；拆招花掉这次行动、同一个敌人一回合只能拆一次、不是我方行动时给理由 |
| `_check_extra_hit` ＋ `_hits_in_options` | `_CheckExtraHit`／`_HitsInOptions` | `stat_def.extra_hit` 上限 2；没加成＝表里段数、加了 +1 段（招式列表如实写）；战报写「（N 段）」而**普攻不吃**；玄微剑三招档那条 `stat:extra_hit` 真的在表里 |
| `_check_passive_qi_cost` | `_CheckPassiveQiCost` | `skill_passive.qi_cost` 1 格 6／3 格 18；催动真扣（20 → 14）；气不够时挡下并把「要 6」写给玩家 |
| `_check_auto_strategies` | `_CheckAutoStrategies` | 三档策略各挑各的（起手／横扫／斩风）；默认均衡、认不出的 id 拒绝且不改现状、轮换顺序（均衡→全力→保守→均衡）；**敌人不受玩家策略影响** |
| `_check_flee` ＋ `_flee_chance_with`／`_flee_fight`／`_caught_encounter` | `_CheckFlee`／`_FleeChanceWith`／`_FleeFight`／`_CaughtEncounter` | 同身法 50%、身法高／低各往哪边走、**上下限顶到**（1000 : 1 → 95%，1 : 1000 → 25%）；撤成功＝`flee` 结局且奖励归零；撤失败继续打；被追上第一回合撤不了、过了就能撤 |
| `_check_exact_kill` | `_CheckExactKill` | 差 1 点不死、正好打空归 0、倒下后不掉负血、负伤害不当治疗 |
| `_check_self_target_skill` | `_CheckSelfTargetSkill` | `target_type=self` 打自己（决策 224）：手选路径点着敌人也打自己、自动路径同样；敌人一点血不掉；战报点名是自己。夹具＝私有表库＋复制 `skill_active` 后**自己写主键与索引**（坑：不补索引就是「行在表里、`get_row` 找不到」） |
| `_party`／`_actor`／`_names`／`_log_text` | `_Party`／`_Actor`／`_Names`／`_LogText` | 队伍由同一个 `character_base` 模板复制两人（0.4.0 起只有一个模板）；手搓战斗单位走绝对数值语义（与 `TestDamage` 那套同形） |

**两处原样保留、别顺手"修好"**：① `_check_self_target_skill` 里 `setup(..., FIXED_MODS)` 把修饰词典
放在 **options 位**上（不是 `{"modifiers": …}`），那一节本来就不吃那三条修饰；② 逃跑那一节的
`flee_chance(hero)` 只作为**取值口**，公式与夹取都在 `BattleSimulatorActions` 里。
搬的是形状，改行为要单独一批。

**断言账**：

- C# 侧：**新建** `tests/TestBattle.cs`（`--- 战斗序列与胜负 ---` 那行 113 条）⇒
  本链 7762 → **7875**（基线 `MinAssertions` 7752 → **7865**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_battle.gd"` 行
  （改写注释说明去处）；`tests/test_battle.gd` ＋ `.gd.uid` 已删 → 旧链 **3853 → 3739 条**
  （基线 `MIN_ASSERTIONS` 3842 → **3728**）。
- `docs/dev/验证清单.csv`：`skill_system`／`enemy_data`／`combat_core` **三行**的
  `tests/test_battle.gd` 都换成 `tests/TestBattle.cs`（BOM 已核对；**别误伤**同一目录下
  `test_battle_fuzz.gd`／`test_battle_ui.gd`／`test_battle_session.gd` 那几条——搬完 `rg` 一遍确认）。
- `docs/dev/模块对接表.csv` **这一批不用动**（三行的 `code_paths` 早已是 `.cs`，注记里也没有老用例路径）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **7875 条 OK**（基线 7865）；
`run_tests.bat` **3739 条 OK**（基线 3728）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。
新文件 640 行，没撞 800 行硬上限。

## 二点九五、P4 第三十九批：`tests/test_dialogue.gd` → **并入** `tests/TestDialogue.cs` ＋ **新建** `tests/TestDialogueNpc.cs`（2026-10-11）

对话与人物这一份走**第三档：C# 侧已有半份 ⇒ 补齐 ＋ 拆出另一半**（同第三十八批的 `test_buff.gd`）。
旧链十五节，按**归属**分两半：

- **容器那一侧**（`dialogue_node`／`dialogue_option` 两张表的逐条内容 ＋ 条件语言 ＋ 效果三样 ＋
  `set_flag` 多值）＝ 十一节：`_check_release_rows`／`_check_conditions`／`_check_effects`／
  `_check_set_flag_multi`／`_check_condition_ops`／`_check_mu2_lin_chain`／`_check_companion_dialogue`／
  `_check_opening_choice`／`_check_shen_cell`／`_check_ledger_choice`（外加 `run()` 的调度）。
  C# 侧 `tests/TestDialogue.cs` 原来只有**服务层契约**那半边（124 行：入口排掉接续节点／条件语言／
  三样效果／序幕补旗），这一批把**逐条台词**那半边补齐。
- **人那一侧**（`NpcService` 的 `npcs_at`／`person_of`／好感行／委托）＝ 三节：
  `_check_region_npcs`／`_check_companion_person`／`_check_quest_extra_favor`。原来没有对应文件，
  **新建** `tests/TestDialogueNpc.cs`。

| 旧链（`tests/test_dialogue.gd`） | C# | 盯什么 |
|---|---|---|
| `run`（`suite_name`／调度） | `TestDialogue.Run`／`TestDialogueNpc.Run` | 两份各注册一次（`Suites` 里 `TestDialogue` 之后紧跟 `TestDialogueNpc`），断言的账分别落在各自那份上 |
| `_check_release_rows` | `TestDialogue._CheckReleaseRows` | 陈氏那条照 20 号 §四 幕三 落的：三个选项的文案（蹲下／逼问／什么都不问）、旗标映射（义／谋／**不改心性**）、逼问扣好感 −2 |
| `_check_conditions` | `TestDialogue._CheckConditions` | 说话人入口（条件留空＝一直可谈）／没配的人如实说没有／夹具选项的前置：没满足时不出现、硬选**也选不动且说明原因**、满足后出现、给的东西真进背包 |
| `_check_effects` | `TestDialogue._CheckEffects` | 好感地板钉**绝对值**（`favor_min()=-10`、陈氏初始 −10、再扣只到地板）——放松或收紧都会静默改手感，变异探针 `code` 那块有对应 cast。（置旗标／给东西／认不出的选项那几条由 `_CheckFavorEffect`／`_CheckUnknownOption` 钉，不重复） |
| `_check_condition_ops` | `TestDialogue._CheckConditionOps` | 条件语言的 `!`（取反）／`|`（任一满足）／`&` 串接／尾部空段／自相矛盾／`item:<id>[:数量]` |
| `_check_set_flag_multi` | `TestDialogue._CheckSetFlagMulti` | 分号多值：单值行为**一字不变**（不补 `flag_open_*`）／多值两枚都落地／带 `heart_*` 照旧补 `flag_open_yi`／空白段跳过／构建期校验**点名真错的那一段** |
| `_check_mu2_lin_chain` | `TestDialogue._CheckMu2LinChain` | 幕二林铁山四条路都点亮 `flag_luoyanpo_met`／拔剑那条是分号多值／`dl_lin_heart`／`dl_lin_short` **不许被任何选项指到**（`entry_node` 会把被指的节点从入口排除） |
| `_check_companion_dialogue` | `TestDialogue._CheckCompanionDialogue` | 幕一客栈：四句选项逐字／义 +1 与好感 +5／利那条 0 −1 = **−1**（负好感允许） |
| `_check_opening_choice` | `TestDialogue._CheckOpeningChoice`（原有，本批补） | 补上：告示原文「谢银三十两」、三条旗标映射与 `next_node_id` 都指到独白、`flag_open_*` 不串档、`dl_open_yi` 独白逐字 |
| `_check_shen_cell` | `TestDialogue._CheckShenCell` | 幕四地牢：台词逐字／三个选项（义／谋／利）／「三十两」那条好感 −5 真落地 |
| `_check_ledger_choice` | `TestDialogue._CheckLedgerChoice` | 终局账册：没打过对质→只出地牢那句；打过但账册未到手→**也不问**（`item:` 挡着）；三条路各置一个旗标；选完不再问（三选一不叠加）；「清名」两条永久增益写得出 |
| `_check_region_npcs` | `TestDialogueNpc._CheckRegionNpcs` | 老周挂在大地图区域节点 `n_luoyanpo` 上（大地图按 E 也认）／清风驿上不串台 |
| `_check_companion_person` | `TestDialogueNpc._CheckCompanionPerson` | 同伴没有 `npc_def` 行，但 `person_of` 按同一形状**合成一行**（名字／称号／所在地的来源）／NPC 那边走真表行／好感行里赠送 5 与四位同伴各自的切磋数（Q88 改判成可切磋）与不可偷 |
| `_check_quest_extra_favor` | `TestDialogueNpc._CheckQuestExtraFavor` | 孙掌柜那条：委托人自己 +20（表里 `reward_favor`）＋**燕小七额外 +10**（`EXTRA_FAVOR`），结果里带给人看的短句 |

**夹具**：`TestDialogue._WithExtraOption(...)`（新建私有表库、复制整表、**重建 rows 数组**再追加夹具行，
并补 `index`——`choose()` 走 `get_row`，漏了索引就是"行在表里却选不动"）与
`TestDialogue._WithPatchedOptionFlag(...)`（深拷贝整表后只改副本那一行的 `set_flag`）。
两者对位旧链的 `duplicate(true)` ＋ `rows.append(...)`／就地改值；**重建数组**这一步是必须的
（直接 `Add` 会把夹具行漏进别的用例，见施工单坑 53）。

**断言账**：

- C# 侧：`tests/TestDialogue.cs` 原有半份 → 补齐后本族 140 条（`--- 对话容器 ---` 那行），
  **新建** `tests/TestDialogueNpc.cs` 20 条 ⇒ 本链 7635 → **7762**（基线 `MinAssertions` 7625 → **7752**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_dialogue.gd"` 行
  （改写注释说明去处）；`tests/test_dialogue.gd` ＋ `.gd.uid` 已删 → 旧链 **3998 → 3853 条**
  （基线 `MIN_ASSERTIONS` 3987 → **3842**）。
- `docs/dev/验证清单.csv`：`item_system`／`guide_system`／`npc_system` **三行**的
  `tests/test_dialogue.gd` 都换成 `tests/TestDialogue.cs|tests/TestDialogueNpc.cs`（BOM 已核对）；
  行内提到老文件的地方也标了「原 `tests/test_dialogue.gd` 已退场」。
- `docs/dev/模块对接表.csv` **这一批不用动**：`npc_system`／`guide_system`／`item_system` 三行的
  `code_paths` 早就全是 `.cs`（第 33 批那批搬 `src/core` 时换过了），注记里也没有「用例 tests/test_dialogue.gd」
  这种走向空气的指路（第三十八批 `buff_system` 那种才要跟着换）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **7762 条 OK**（基线 7752）；
`run_tests.bat` **3853 条 OK**（基线 3842）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。
两份新文件分别 605／118 行，都没撞 800 行硬上限。

## 二点九四、P4 第三十八批：`tests/test_buff.gd` → 并入 `tests/TestBuff.cs` ＋ **新建** `tests/TestBuffIntegration.cs`（2026-10-11）

增益与套装这一份走**第三档：C# 侧已有半份 ⇒ 补齐 ＋ 拆出另一半**。旧链十二节，天然分两半：

- **表与静态规则**（`run` 的三条绝对值、`_check_tables`、`_check_buff_service`、`_check_set_service`
  的静态那几条）：C# 侧原来只有这半（`tests/TestBuff.cs` 112 条），这一批把**缺的逐行自洽**补上。
- **落地那半**（常驻 buff 走贡献通道、`BattleActor` 的叠加与递减、六指令、进场 buff、战斗外计时、
  招式自带 `on_cast`／`on_hit`、叠层放大、增益招式醉步）：原来没有，**新建** `tests/TestBuffIntegration.cs`。
  `TestBuff.cs` 的类注释里当时写着"那些要 `Inventory`／`GameState`／`BattleSimulator`，它们都还在
  GDScript 里"——2026-10-11 起这三块全是 C# 了，所以这半边现在搬得动（注释已改）。

| 旧链（`tests/test_buff.gd`） | C# | 盯什么 |
|---|---|---|
| `run` 的三条 ＋ `_check_tables` 的行数 | `TestBuff._check_vocabulary`／`_check_tables` | 极性／常驻哨兵／作用域／叠加规则／效果类型的**绝对值**（变异探针第三块改 `.cs` 不重编，这三条是那三处豁免的替代网）＋六张表的行数与 06 的清单一致 |
| `_check_tables` 的循环 | `TestBuff._check_table_rows`（**新增**） | **逐行**自洽：枚举列在 08 的取值里、`max_stack` 至少 1（不叠层的必须是 1）、`buff_stat` 的 target 带 `attr:`／`stat:` 前缀且所属 buff 存在、`buff_grant` 的 buff 与来源（按 `source_type` 选表）都存在、`set_member` 的成员按套装类型落在 `equip_base`／`skill_base`、`set_bonus` 的档位至少 1 件 |
| `_check_buff_service` | `TestBuff._check_buff_rules`（原有） | 静态规则逐条（scope／duration／is_permanent／stack／max_stack／effect_kind／stat_mods／grants_for／persistent_grants） |
| `_check_set_service` 的静态几条 | `TestBuff._check_sets`（原有） | 3 套／类型／成员数／档位数与升序 |
| `_check_set_service` 的计数那半 | `TestBuffIntegration._CheckSetCounts` | 只算**穿在身上／装配上**的（背包里那件不算）／内功套按**占格数之和**（>5 部数）／档位向下兼容／剑系角色穿不上黑风刀（拒绝理由里点明武器类型） |
| `_check_permanent_contributions` | `_CheckPermanentContributions` | 常驻 buff 走**贡献通道**进面板：锈月给敏 +4（属性点层）与暴击 +5%（固定值层）且来源写着 `buff:<id>@<实例 id>`（同名两件分得开）／遗物给力 +3 与格挡减伤 +8%／套装两件档给力 +3 且来源写着 `set:<id>` |
| `_check_actor_buff_rules` | `_CheckActorBuffRules` | unique 重复获得无效且不叠层／`duration=1` 回合末递减、属性跟着回落（减伤与外防各自回原值）／computed 运功＝该内功常驻加成翻倍（+30）／stack **逐次**验 1→2→3 再封顶（决策 220 那个"第一次就给 2 层"的 bug 就是靠逐次断言抓的）／special 类记进 `unhandled_buffs` 不许静默 |
| `_check_six_commands` | `_CheckSixCommands` | 普攻造成伤害且**不进招式使用记录**／主动防御回架势、同回合不能防第二次（unique）、下回合先手／内功催动挂 `buff_yunqi`、没装的内功给得出理由、按钮写明催动后拿到什么／战斗道具列得出但**写明效果未配**（不假装能回血） |
| `_check_battle_start_grants` | `_CheckBattleStartGrants` | 玉佩的整场 buff 开打就挂上（`remaining=-1` 不递减）＋气血回复 +3＋`pending_grants` 发完清空（不会每回合重复发） |
| `_check_field_buffs` | `_CheckFieldBuffs` | 会话层按**现实分钟**计时（`remaining_sec=600`、注入 `now` 验到点即失效且真被清掉、重复获得只刷新）／`clear_field_buffs` 返回清掉几条／带进战场后不按回合递减、战斗结束**不清** field（归会话管） |
| `_check_cast_grants` | `_CheckCastGrants` | 招式自带 `on_cast` 发放：一次行动只发一次（叠层类 +1，不按目标数累加）、战报写明附带什么、**buff 写错时不静默**（连是哪个 buff 都写出来）／敌人也能收 buff（`EnemyFactory` 把 db 交给 `BattleActor`） |
| `_check_stack_scaling` | `_CheckStackScaling` | 叠层**按层数放大**（一层 +5／两层 +10／封顶 3 层 = +15，发行数据里走不到，靠夹具补一条 `stat:atk_phys`）／命中触发**按段各发一次**（2 段招式 → 2 层） |
| `_check_support_skill` | `_CheckSupportSkill` | 增益招式醉步（发行数据）：可点列表里有它且标 `support`／`available_skills` 与 `skill_options(ok)` **两个口径必须一致**／施放给自己上忘忧（闪避 +20%）而敌人一点血不掉／内力消耗 0／放完进冷却／自动战斗不会自己去放它 |
| `_check_skill_hit_grants` | `_CheckSkillHitGrants` | 命中触发也能来自**招式自己**（`skill_active + on_hit`，2026-10-03 才接）：2 段 → 2 层，且战报里没有「buff 没上上去」 |

**夹具**：`TestBuffIntegration._WithExtraRows(表, 若干夹具行)`——新建**私有**表库、复制整表、**重建 rows
数组**再追加 `buff_grant`／`buff_stat` 夹具行（对位旧链的 `duplicate(true)` ＋ `rows.append(...)`）。
重建数组这一步是必须的：`duplicate(true)` 之后 `rows` 仍可能与共享表共用同一份，直接 `Add`
会把夹具行漏进别的用例（照 `TestCase.TableWithTalents` 的形状，见施工单坑53）。

**断言账**：

- C# 侧：`tests/TestBuff.cs` 112 → **477**（补进来的逐行自洽 365 条），**新建**
  `tests/TestBuffIntegration.cs` **117** 条 ⇒ 本链 7153 → **7635**（基线 `MinAssertions` 7143 → **7625**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_buff.gd"` 行
  （改写注释说明去处）；`tests/test_buff.gd` ＋ `.gd.uid` 已删 → 旧链 **4512 → 3998 条**
  （基线 `MIN_ASSERTIONS` 4502 → **3987**）。
- `docs/dev/验证清单.csv`：`skill_system`／`combat_core`／`buff_system`／`set_system` **四行**的
  `tests/test_buff.gd` 都换成 `tests/TestBuffIntegration.cs`（BOM 已核对）；`buff_system`／`set_system`
  两行补了【2026-10-11 C# 迁移】注记。
- `docs/dev/模块对接表.csv`：`buff_system`／`set_system` 两行的注记里「用例 tests/test_buff.gd」
  跟着换掉（那份文件已经不在仓库里，留着就是一条走向空气的指路）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **7635 条 OK**（基线 7625）；
`run_tests.bat` **3998 条 OK**（基线 3987）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点九三、P4 第三十七批：`tests/test_table_pipeline.gd` → **新建** `tests/TestTablePipeline*.cs`（2026-10-11）

数据管线这一份走**第一档：C# 侧原先没有对应文件 ⇒ 新建**。旧链八节，前四节是**正例**、
后四节是**负例**——负例那半份是「表写错了会怎样」的账本，大多是「把一行深拷贝改坏一格 →
让校验器跑一遍 → 断言报错里含某关键词」，搬过来压成了 `Broken(table, edit)` ＋ `HasAll`／`CountWith`。

| 旧链（`tests/test_table_pipeline.gd`） | C# | 盯什么 |
|---|---|---|
| `run` ＋ `_check_row_counts` | `TestTablePipeline.Run`／`_CheckRowCounts` | 表库能加载（`db.errors` 为空）／表数与 `TableRegistry.tables()` 一致／`validate()` 对真实表零错误／逐张表的 `rows` 条数与 `data/tables/<表>.csv` 的行数（表头不算）一致 |
| `_check_spot_values` | `_CheckSpotValues` | 41 处抽查：五维 id 齐（str/con/agi/int/luk）／列别名 `id → row_id`／复合主键（`element_counter` 的 `external\|internal` ×0.8、`difficulty_drop_rate` 的 `nightmare\|legend` ×6.67）／列别名 `exp → exp_reward`／多值列 `enemy_team.members` 解析（2 条、第一条 `count=2`）／`character_base` 五维之和＝`attr_total` 与武器与初始装备／`weapon_type_def.blade` 主堆力量／`combat_const.def_const`＝60／`event_check.ev_shed_trap` 来源 `skill:qimen`／**每个模板 5 项非战斗技能**（随模板数走，不写死 5）／七类装备槽位（戒指两枚、武器槽四种武器类型）／棍已移除／`eq_head_01` 改挂肩部／货架 26 行／杂货铺挂 `shop_grocery`／草药汤 `field`、金创药 `battle` |
| `_check_registry_matches_csv_dir` | `_CheckRegistryMatchesCsvDir` | 双向对齐：每张 CSV 都登记进 `TABLES`、每条登记都有 CSV（构建期读不到表 = 功能静默不生效）＋**子目录当场点名**（只扫一层是故意的，藏在子目录里的表同样读不到） |
| `_check_broken_reference_is_caught` | `TestTablePipelineRules._CheckBrokenReferenceIsCaught` | 写坏引用／主键重复／五维与 `attr_total` 对不上／事件判定缺前缀，四条都要报（保证校验不是摆设） |
| `_check_stale_index_is_caught`／`_drop_first_row_keep_index` | `_CheckStaleIndexIsCaught`／基类 `DropFirstRowKeepIndex` | 同时弄坏 `level_growth` 与 `item_base`：两张都要被点名、且每张**逐行**报（条数 > 2）——证「`rows[position]` 越界不许掐断后面的表」（决策 147 那次的教训，当时后面 24 张表的主键检查全静默跳过） |
| `_check_shop_and_slot_negatives` | `_CheckShopAndSlotNegatives` | 30 条：非法槽位／卖价不低于买价／`use_context` 非法／武器槽缺 `weapon_type`／`extra_rule` 拼错（`rule_burn_def_downd`）／条件键拼错（`flag_stealth_fullx`）／非法 `kill_with`／`ai_template` 拼错（`ai_basics`）／`hit_count=0`（**改回 1 段必须恢复零错误**）／负 `poise_damage`／`start_level` 两条越界（<1、超上限）／负 `status_chance_mul`／负经验与负加点（正好两条）／`drop_group` 里 slot 重复／`level_growth` 缺 7 级／三种词条目标错（正好三条）／升级会变弱／`level_req` 超上限与低于 1 级（正好两条）／敌人负防御与超额抗性（两条）／难度倍率非正（两条）／非白板装备全零加成／内功加成白填／第 0 层与缺第 2 层 |
| `_check_growth_and_rarity_negatives`／`_check_scanned_range_rules`／`_check_one_cell_caught`／`_check_validator_rule_targets`／`_check_rule_column` | `_CheckGrowthAndRarityNegatives`／`_CheckScannedRangeRules`／基类 `CheckOneCellCaught`／基类 `CheckValidatorRuleTargets`／`CheckRuleColumn` | 17 条：词条条数区间倒挂（`min > max` 被 `maxi` 兜着，界面看不出来）／`exp_to_next` 反向与白送一级（封顶级 0 是例外）／管线用的比率没写上限（留空＝无上限 ⇒ 代码静默换兜底常数）／起手招式与模板武器对不上（决策 162）／难度阶梯倒挂／解锁短语没登记（永远解锁不了）／缺代码依赖的常数／开局装不上（武器类型与等级门槛）／成长常数低于可用下限（正好两条）／五条扫描区间（0 层状态·0 回合持续·>100% 触发率·负售价·负铜钱，决策 157）／**校验器规则表自检**（`REFERENCES`/`RANGES`/`RELATIONS`/`ENUMS` 点到的表与列必须真实存在——列名写错会让 `float(null)` 报错并**中断整段校验**，所以规则是靠列名走 `property_of` 别名表再比对属性列表，且失败只记 `problems`） |

**为什么要拆**：整份搬完 900 行上下，撞 `tests/test_handshake_repo.gd::_check_file_size` 的 800 行硬上限
（旧链那份 849 行本来也挂在 `docs/dev/代码拆分清单.md` 上），照第 35 批那套拆成
「抽象基类 `tests/TestTablePipelineBase.cs`（**不注册**，只放工具与夹具）＋ 正例 `tests/TestTablePipeline.cs`
（241 条）＋ 负例 `tests/TestTablePipelineRules.cs`（55 条）」。**不用 `partial`**：假绿门限按注册路径
逐文件算，partial 的另一半不注册就没人量。基类的公共口：`Stub()`／`Broken(table, edit)`／`Row<t>`／
`First<t>`／`RemoveRows<t>`／`DropFirstRowKeepIndex`／`Join`／`HasAll`／`CountWith`／`CheckOneCellCaught`／
`CheckValidatorRuleTargets`／`CheckRuleColumn`。**坏数据一律只改内存副本**：复制整表 → 逐行
`Duplicate(true)` → 写回**私有表库**（表资源是 Godot 按路径缓存的，直接改行会改到别人正在用的那一行，
照 `TestCase.TableWithTalents` 的形状）。

**两族注册在 `Suites` 的**最前面**（紧跟 `HarnessSelfTest`）——这不是口味**：它们读的是**共享表库**
（`GameData` 那一份），断言「每张表的行数与 CSV 一致」；后面的 `TestCreation` 会往共享表库注入一行
自建角色模板，排到最后再比就会多出一行（2026-10-11 实测：6 行 vs CSV 5 行，连带 5 条校验错误）。
GDScript 那条链里 `test_table_pipeline.gd` 本来也是 `TEST_SCRIPTS` 的第一个——这里对齐同样的次序。
**记一条通用教训**：凡「读共享表库」的用例都得排在会改共享表库的用例之前，否则症状看着像数据错。

**断言账**：

- C# 侧新建三份 ＋**296 条**（正例 241／负例 55；两族静态断言点各 49，多出来的是两张表逐张循环
  与 `_CheckSpotValues` 里那些条件分支）→ 本链 **7153 条**（基线 `RunTestsCs.cs` 的
  `MinAssertions` 6847 → **7143**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_table_pipeline.gd"` 行
  （改写注释说明去处）；`tests/test_table_pipeline.gd` ＋ `.gd.uid` 已删 → 旧链 **4806 → 4512 条**
  （用例自身那 296 条退出，按文件逐条那几条门限一进一出：少一份旧 `.gd`、多三份新 `.cs`，本节又新写了
  文档引用，净少 294；基线 `MIN_ASSERTIONS` 4796 → **4502**）。
- `docs/dev/验证清单.csv`：`table_pipeline` 行的 `tests/test_table_pipeline.gd` →
  `tests/TestTablePipeline.cs|tests/TestTablePipelineRules.cs`；`char_template` 与 `equip_system` 两行的
  同一路径 → `tests/TestTablePipeline.cs`（文件名是路径字符串，CSV 有 BOM，写完核对过前三字节）。
- `docs/dev/代码拆分清单.md`：划掉 `tests/test_table_pipeline.gd`（849 行）那一行，抬头 **12 → 11 个超标**；
  顺手按今天的实测刷新了其余行的行数（有几行留的是更早批次的旧值）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **7153 条 OK**（基线 7143）；
`run_tests.bat` **4512 条 OK**（基线 4502）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点九二、P4 第三十六批：`tests/test_event_check.gd` → **新建** `tests/TestEventCheck.cs`（2026-10-11）

事件判定这一份走**第一档：C# 侧原先没有对应文件 ⇒ 新建一份**。旧链九节：表的十四条判定逐行验来源与难度、
判定值取自面板同一套（含「恰好达标」与「差一点」）、软判定的公式与上下限、**掷骰真的会翻面**
（同种子可复现、换 59 颗种子两边都出现）、奖励三类与失败文案、`once_only` 与失败可重试、
结果与旗标进存档（含 v8 迁移）、奖励类型（`equip` 真发实例／`skillbook` 与 `boss` 各有落点／
非法取值与四种坏 id 都显式报错且**不漏表内 id**）、赌局赢的是钱不是背包里一行「铜钱」、位点归属。

| 旧链（`tests/test_event_check.gd`） | C#（`tests/TestEventCheck.cs`） | 盯什么 |
|---|---|---|
| `_check_table` | `_CheckTable` | 表里 14 条判定的来源格式（`attr:`／`skill:`）与难度为正与判定值算得出来；黑风寨 5 条／落雁坡 3 条 |
| `_check_judgement` | `_CheckJudgement` | 1 级书生：奇门 3（1＋10/5）、文学 5、毒术 2、敏 9；判定值用**裸属性**；奇门 3 ≥ 3 过、力 5 < 8 不过、敏 9 ≥ 9 过（临界值算过） |
| `_check_soft_roll` | `_CheckSoftRoll` | 公式与 5%~95% 夹取；hard 不足不掷骰且没有概率；soft 不足给概率但 `judge` 本身不掷骰（重复调一致）；59 颗种子各 2 条（真掷骰／文案带掷骰结果）＋同种子同结果；掷中发奖励记 `done` 并置 `flag_qiutu_saved`、掷不中不发且记 `failed` |
| `_check_rewards`／`_check_once_only`／`_check_save` | `_CheckRewards`／`_CheckOnceOnly`／`_CheckSave` | 道具进包／剧情旗标／碑文给锈剑；失败转述表里的 `fail_note`；`room` 奖励顺带揭开地标（白盒调 `_grant`）；做过不能再做而失败可重试；结果与旗标进存档，v8 老档读进来是空的 |
| `_check_reward_types` | `_CheckRewardTypes` | 校验器抓得到 `equip` 写错 id；`equip` 真建实例进背包；`skillbook` 与 `boss` 不再落进兜底（后者带上敌人中文名与开战请求，敌人不存在时不发请求）；`none` 不给玩家看开发说明；非法 `reward_type` 与四种坏 id 都讲人话、**不漏表内 id** |
| `_check_gamble_money`／`_check_marker_ownership` | `_CheckGambleMoney`／`_CheckMarkerOwnership` | 赌局赢的是**钱**（`inventory.money` 涨、背包不许多出 `item_money`）；位点归属（`scene_id` 命中 **或** `region_id` 相符，查不到的返回 false） |

**坏数据夹具**：这一份要在「改坏一行判定」的表库上跑（`equip`／`skillbook`／`boss`／非法类型／
坏 id／难度 99），所以新写了一个 `_StubCheckTable(edit)`——**逐行 `Duplicate(true)` 再写回 `rows`**，
照 `TestCase.TableWithTalents` 的形状：表资源是 Godot 按路径缓存的，直接改行会改到别人正在用的那一行。

**断言账**：

- C# 侧新建 `tests/TestEventCheck.cs` ＋**272 条**（静态断言点 116，逐档与掷骰那两个循环把执行数抬上去）
  → 本链 **6857 条**（基线 `RunTestsCs.cs` 的 `MinAssertions` 6575 → **6847**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_event_check.gd"` 行
  （改写注释说明去处）；`tests/test_event_check.gd` ＋ `.gd.uid` 已删 → 旧链 **5078 → 4806 条**
  （−273＝用例自身 272 条 ＋ 随文件少跑的 1 条按文件逐条门限；本节文档引用再 +1；基线
  `MIN_ASSERTIONS` 5068 → **4796**）。
- `docs/dev/验证清单.csv`：`event_skill` 与 `event_check` 两行的 `tests/test_event_check.gd` 都换掉（BOM 已核对）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **4806 条 OK**（基线 4796）；
`run_tests_cs.bat` **6857 条 OK**（基线 6847）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点九一、P4 第三十五批：`tests/test_save_migration.gd` → **新建** `tests/TestSaveMigration*.cs`（2026-10-11）

存档迁移这一份走**第一档：C# 侧原先没有对应文件 ⇒ 新建一份**。旧链那份十四节：v1 直读与「写进存档槽 →
走菜单读档 → 迁移后立刻写回」、v2 的钱／材料／装备／加点往返、v3 补出「已学＋装配」、v4 装备实例没有词条、
v9 首杀为空、v10 气血按满血、**v11→v12 战斗外增益**（含到期时间戳随档走与坏条目过滤）、
**v12→v13 大地图坐标**（含写坏的坐标丢掉）、**v13→v14 天赋与自建角色模板重新注入 `character_base`**、
**逐档覆盖 v1…v14**（版本号是权威）、下架角色替换与等级继承、坏档判定、六种字段类型写错不许运行期炸。

| 旧链（`tests/test_save_migration.gd`） | C# | 盯什么 |
|---|---|---|
| `_v1_dict`／`_check_direct_migration`／`_check_store_migration` | `TestSaveMigration._CheckDirectMigration`／`_CheckStoreMigration` | v1 老档直读（补两件起始装备、铜钱 0、3 级累计 15 点）；没有 db 也能升版本但不补装备；写进槽 1 再走 `MenuController.load_game` 触发「迁移后立刻写回」，第二次读不需要迁移 |
| `_check_v2_round_trip`／`_check_v3_migration`／`_check_v4_migration` | `TestSaveMigration._CheckV2RoundTrip`／`_CheckV3Migration`／`_CheckV4Migration` | 钱与材料与装备与加点原样回来；v3 补出已学与装配（熟练度里那部也算已学、图鉴去重计数 2）；v4 的装备实例没有词条而不是空词条对象 |
| `_check_v9_migration`／`_check_v10_migration`／`_check_v12_field_buffs`／`_check_v13_world_pos`／`_check_v14_creation` | `TestSaveMigrationVersions.*` | 首杀为空／气血按满血且迁移后能记伤／增益与**绝对**到期时间戳随档走（坏条目丢掉）／缺 `world_pos` 退默认出生点（写坏的坐标也不许读进来）／天赋往返＋自建模板按 char_id 查得到 |
| `_check_every_legacy_version` | `TestSaveMigrationVersions._CheckEveryLegacyVersion` | 按 `AddedIn` 逐档删字段再读，十四档全过；「那一档之后才有的字段」不许被读进来（版本号是权威） |
| `_check_retired_characters`／`_check_corrupt`／`_check_type_mismatched_fields` | `TestSaveMigrationRobust.*` | 下架角色换成能玩的人（等级取最高、装备退回背包、接手角色造得出战斗单位、写回后再读没有可剔的）／坏档判损坏／六种字段类型写错只许判损坏或取缺省 |

**这一批多了一件事：撞上 800 行硬上限，顺手按被测面拆成三份 suite。** 旧链 598 行整份搬完是 889 行，
`tests/test_handshake_repo.gd::_check_file_size` 当场红（新增文件不许再往「12 个超标」上加）。拆法照
`tests/test_handshake.gd` 拆八族那一套：**常量与共用夹具留基类**（`tests/TestSaveMigrationBase.cs`：`AddedIn` 表、
`_v1_dict` 夹具、`_CleanDir`／`WriteText` 与几个取值小工具），三族各自是一份 suite 并各自注册——
**没有用 `partial`**：`RunTestsCs.cs` 的「静态断言点 vs 实际执行条数」按 `Suites` 里的路径逐文件算，
partial 的另一半不注册就没人量（`test_handshake.gd` 拆族时同一条理由）。拆完 46 ＋ 258 ＋ 59 ＝ 363 条，
与拆之前一整份的 363 条**一模一样**（只挪位置、不改行为）。

**断言账**：

- C# 侧新建四份（基类 ＋ 三族）＋**363 条** → 本链 **6585 条**（基线 `RunTestsCs.cs` 的
  `MinAssertions` 6212 → **6575**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_save_migration.gd"` 行
  （改写注释说明去处）；`tests/test_save_migration.gd` ＋ `.gd.uid` 已删 → 旧链 **5434 → 5078 条**
  （−357＝用例自身 356 条 ＋ 随文件少跑的 1 条按文件逐条门限；本节文档引用再 +1；基线
  `MIN_ASSERTIONS` 5424 → **5068**）。
- `docs/dev/验证清单.csv`：`save_system` 行的 `tests/test_save_migration.gd` 换成新三份（BOM 已核对）。
- `docs/dev/代码拆分清单.md` **不用改**：拆完四份都远低于 800 行，抬头那句「量 12 个超标」与实测 12 一致
  （本批中途红过一次「实测 13 个」，就是这一份 889 行）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **5078 条 OK**（基线 5068）；
`run_tests_cs.bat` **6585 条 OK**（基线 6575）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点九〇、P4 第三十四批：`tests/test_end_to_end.gd` → **新建** `tests/TestEndToEnd.cs`（2026-10-11）

端到端这一份走**第一档：C# 侧原先没有对应文件 ⇒ 新建一份**。旧链那份是顶上一条「把表校验 → 组队 →
真模拟一场战斗 → 掷掉落」串起来跑的整合链（这条链是「一个系统一个系统都验绿、合起来却跑不通」的
唯一防线），加三节跨系统持久化；这一批把四节一条不落地搬进新文件，`SuiteName` 仍是「端到端：明雷战斗与掉落」。

| 旧链（`tests/test_end_to_end.gd`） | C#（`tests/TestEndToEnd.cs`） | 盯什么 |
|---|---|---|
| `run()` 顶上半节 | `Run()` | 表校验 0 条 ＋ 复制模板造两个 5 级角色 ＋ `team_bandit_patrol` 3 人 ＋ 模拟出胜方／经验／铜钱 ＋ 机缘倍率落在 0~0.25 ＋ 按敌 `roll_group` 收掉落且铜钱必掉 |
| `_check_wounded_persistence` | `_CheckWoundedPersistence` | 新档满血 → 打伤 40 → `PartyBuilder` 造出来的单位带伤 → `SaveStore` 往返 → 医馆治 40 点花 80 文 |
| `_check_rewards_and_persistence` | `_CheckRewardsAndPersistence` | 卸武器比外功；内功装备后 `qi_max` ＋30 **穿过 `PartyBuilder`**；`BattleReward.apply` 落账；存档往返经验／钱／装备／材料 |
| `_check_multi_character_state` | `_CheckMultiCharacterState` | 默认队伍＝`recruit_def.is_initial` ＋ 显式 4 人 ＋ 等级加点互不串 ＋ `best_check_value("skill:wenxue")` 取智最高者 ＋ 经验池**先补等级最低的** ＋ 医馆全队求 30 ＋ 多角色存档往返 |

**两处踩过的形状**：① `RecruitDefRow.is_initial` 是 **bool**（不是 int），照旧链抄成 `== 1` 编译不过；
② C# 侧那份 `TestOverworld.cs` 里的 `tile` 常量是 `float`（`Vector2` 只收 float），别照 GDScript 的 int 抄。

**断言账**：

- C# 侧新建 `tests/TestEndToEnd.cs` ＋**60 条** → 本链 **6222 条**（基线 `RunTestsCs.cs` 的
  `MinAssertions` 6152 → **6212**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_end_to_end.gd"` 行
  （改写注释说明去处）；`tests/test_end_to_end.gd` ＋ `.gd.uid` 已删 → 旧链 **5494 → 5434 条**
  （−61＝搬走的 60 条＋随文件少跑的 1 条按文件逐条门限；本节文档引用再 +1，净 −60；基线
  `MIN_ASSERTIONS` 5483 → **5424**）。
- `docs/dev/验证清单.csv`：`char_template`／`enemy_data`／`drop_system`／`save_system` 四行的
  `tests/test_end_to_end.gd` 全部改指 `tests/TestEndToEnd.cs`（4 处，BOM 已核对）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **5434 条 OK**（基线 5424）；
`run_tests_cs.bat` **6222 条 OK**（基线 6212）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八九、P4 第三十三批：`tests/test_world_map.gd` **并入** `tests/TestOverworld.cs` 后退场（2026-10-11）

大地图揭雾这一份走**第二档：C# 侧部分覆盖 ⇒ 补齐缺口再退场**。C# 侧 `TestOverworld.cs` 里早有一节
`_CheckWorldMap`，但它走的是**真场景**（雾层被擦掉、位点、驿站面板按 E、面板里的传送与难度）；
旧链那七节是**服务层**口径，两半合起来才是完整的一份，所以这一批把七节并进去，`_CheckWorldMap`
原样留着。新节自己开一份全新存档（真场景那一节会揭开山洞、还会切难度，混用同一份 `state`
会让「开局几张图亮着」这种断言看起来时红时绿）。

| 旧链（`tests/test_world_map.gd`） | C#（`tests/TestOverworld.cs`） | 盯什么 |
|---|---|---|
| `_check_initial` | `_CheckInitialReveals` | 开局只点亮城镇＋驿站（幂等）；HUD 分母是**表行数**；持有类（藏宝图→石隙迷窟） |
| `_check_proximity` | `_CheckProximityReveals` | `proximity_N` 的 3 格／5 格门槛，含「正好卡在门槛上」的 off-by-one（多 1 像素不揭） |
| `_check_discover_chain` | `_CheckDiscoverChain` | `discover_X`：落雁坡没亮时黑风寨不许自己冒出来 |
| `_check_region_rules` | `_CheckRegionRules` | 0.32.0 两条表侧规则：发行数据逐行点名 ＋ 改坏了构建期真会红 |
| `_check_travel` | `_CheckTravel` | 传送目的地的取舍（野外／未开放不列）与两条拒绝理由 |
| `_check_difficulty` | `_CheckDifficulty` | 难度解锁条件（大寨主→困难、醉刀客→绝境）、切难度与「只有一个当前」 |
| `_check_save` | `_CheckWorldMapSave` | 揭开状态与难度进存档；v7 老档（没有 `revealed_nodes`）升到 8 |

**断言账**：

- C# 侧 `tests/TestOverworld.cs` ＋**64 条**（逐节对上：15＋10＋6＋4＋10＋12＋7）→ 本链 **6162 条**
  （基线 `tools/RunTestsCs.cs` 的 `MinAssertions` 6088 → **6152**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_world_map.gd"` 行
  （改写注释说明去处）；`tests/test_world_map.gd` ＋ `.gd.uid` 已删 → 旧链 **5561 → 5493 条**
  （−68：并过去的那 64 条逐节对得上，余下 4 条是随文件一起少跑的按文件逐条门限；基线
  `MIN_ASSERTIONS` 5550 → **5483**，算上本节文档引用 +1 之后实测 5494）。
- `docs/dev/验证清单.csv`：`map_overworld` 行的 `tests/test_world_map.gd` 去掉（`tests/TestOverworld.cs`
  本来就在那一行里）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **5494 条 OK**（基线 5483）；
`run_tests_cs.bat` **6162 条 OK**（基线 6152）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八八、P4 第三十二批：`tests/test_status.gd` **并入** `tests/TestStatusRules.cs` 后退场（2026-10-11）

异常状态这一份走**第二档：C# 侧部分覆盖 ⇒ 补齐缺口再退场**。C# 侧那份 `TestStatusRules.cs` 当时
只有**一节 7 条**（`StatusRules.rate()` 的读法：表里有就按表、缺表／缺列／手搓状态退回兜底值），
而旧链有**十一节**——搬过去正好 **66 条**，合起来 73 条。

| 旧链（`tests/test_status.gd`） | C#（`tests/TestStatusRules.cs`） | 盯什么 |
|---|---|---|
| （原来就在 C#） | `_CheckStatusRateTable` | 两个数值列的读法（表／缺列／手搓状态／没表库四种情形） |
| `_check_chance` | `_CheckChance` | 触发几率 = base × 招式系数 ＋ 运换算，再乘 (1 − 抗性) |
| `_check_stacking` | `_CheckStacking` | 同源叠层封顶、刷新时长、不同来源各算一条 |
| `_check_snapshot` | `_CheckSnapshot` | 快照制：老层按施加那一刻的值跳 |
| `_check_defense_rules` | `_CheckDefenseRules` | DoT **逐类型**的防御口径（中毒无视／流血吃外防／内伤吃内防） |
| `_check_burn_and_internal` | `_CheckBurnAndInternal` | 灼伤削外防削得动直伤、内伤把内力回复压半，且两处比例**读表** |
| `_check_unhandled_rules` | `_CheckUnhandledRules` | 表里配了、代码不认的规则要记下来并播成「暂缓规则」 |
| `_check_dispellable` | `_CheckDispellable` | 毒／灼伤／流血可清，内伤清不掉 |
| `_check_battle_flow` | `_CheckBattleFlow` | 整场战斗：挂状态 → 回合末跳字 → 斩杀与死因 |
| `_check_enemy_resistances` | `_CheckEnemyResistances` | 敌人抗性（装内功才有）＋内伤抗性上限**读表** |
| `_check_player_resistances` | `_CheckPlayerResistances` | 玩家练内功拿到的抗性走**同一条**派生数值 |
| `_check_regen_caps` | `_CheckRegenCaps` | 回合末回复的封顶与「死人不回复」两种边界 |

形状上把旧链那几段「复制一份表改内存」的夹具收成两个小助手（`_StatusEffectTableWith`／
`_StatDefTableWith`），两处都照施工单坑53 的形状写：**逐行 `Duplicate(true)` 再写回**。

**断言账**：

- C# 侧 `tests/TestStatusRules.cs` 7 → **73 条**（并进来 66 条）→ 本链 **6098 条**
  （基线 `tools/RunTestsCs.cs` 的 `MinAssertions` 6022 → **6088**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_status.gd"` 行
  （改写注释说明去处）；`tests/test_status.gd` ＋ `.gd.uid` 已删 → 旧链 **5628 → 5560 条**
  （−68 ＝ 用例自身 66 条 ＋ 随文件一起少跑的按文件逐条门限 2 条；基线 `MIN_ASSERTIONS`
  5617 → **5550**，算上本节文档引用 +1 之后实测 5561）。
- `docs/dev/验证清单.csv`：三处指向旧链的路径全部换成 `tests/TestStatusRules.cs`
  （`damage_pipeline`／`hidden_content`／`debuff_system`）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **5561 条 OK**（基线 5550）；
`run_tests_cs.bat` **6098 条 OK**（基线 6088）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八七、P4 第三十一批：`tests/test_shop.gd` → **新建** `tests/TestShop.cs`（2026-10-11）

商店这一份也走**第一档：C# 侧原来根本没有对应文件**，所以是整份**新建**、十二节一条不落搬过来。
它碰的模块（`ShopService`／`Inventory`／`CharacterSheet`／`AffixRoller`／`ShopScreen`）**早就是 C#**，
旧链那份 `.gd` 本来就是「GDScript 用例打 C# 代码」——这一批只是把用例本身也翻过来。

| 旧链（`tests/test_shop.gd`） | C#（`tests/TestShop.cs`） | 盯什么 |
|---|---|---|
| `_check_stock` | `_CheckStock` | 五家货架按表（含行商 `bld_caravan`）＋升序＋买入价 > 卖出价 |
| `_check_buy` | `_CheckBuy` | 扣钱／进包／累计次数；钱不够、不卖、装备走实例且是白板；「差一文」边界 |
| `_check_sell` | `_CheckSell` | 卖出价结算；钥匙／身上穿着／没上架三条拒收；词条随实例进回购 |
| `_check_buyback` | `_CheckBuyback` | 回购价＝卖出价、上限 20 条、词条原样回来、挤掉最早的 |
| `_check_buyback_partial` | `_CheckBuybackPartial` | 回购「部分入包」：只按真放进去的收钱／剩下留在列表／放不下拒且不扣钱 |
| `_check_limited_stock` | `_CheckLimitedStock` | 限量货架（复制表只改内存）：余量 0 与无限量 −1 分得开、随存档走 |
| `_check_limited_stock_ui` | `_CheckLimitedStockUi` | 真场景：行写「限量：还剩 N」、买超时按钮置灰写原因、卖光后行还在 |
| `_check_bulk_buy` | `_CheckBulkBuy` | 一次买 5 件出 5 个独立实例；overflow 只按入包数量收钱 |
| `_check_sell_all` | `_CheckSellAll` | 一次卖光不留 ×0 空行（连内部堆叠表也查）；卖得比手上多被拒 |
| `_check_clinic_multi_member` | `_CheckClinicMultiMember` | 医馆服务页 4 人分支：逐人一行、总价求和、按一次全队回满 |
| `_check_heal` | `_CheckHeal` | 治疗费 = 缺失气血 × 单价、满血被拒、缺钱被拒、治完预览归零 |
| `_check_save` | `_CheckSave` | `shop_state` 往返一致；v5 老档读进来是空的并记下迁移 |

形状上的两处本地化：`shop_state_keys()` 改成 `_ShopStateKeys()`（键排序后拼一行字符串，便于直接比对
两份存档）；`_stock_table_with()` 去掉那个没用的 `db` 形参，直接用 `FreshDb()` 复制表
——**逐行 `Duplicate(true)` 再写回**（表资源是 Godot 按路径缓存的，直接改行会改到别人手里那份）。

**断言账**：

- C# 侧 `tests/TestShop.cs` **新建 251 条** → 本链 **6032 条**（基线 `tools/RunTestsCs.cs`
  的 `MinAssertions` 5771 → **6022**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_shop.gd"` 行
  （改写注释说明去处）；`tests/test_shop.gd` ＋ `.gd.uid` 已删 → 旧链 **5879 → 5627 条**
  （−252 ＝ 用例自身条数 ＋ 随文件一起少跑的按文件逐条门限；基线 `MIN_ASSERTIONS`
  5869 → **5617**，算上本节文档引用 +1 之后实测 5628）。
- `docs/dev/验证清单.csv`：`shop_system` 行的 `tests/test_shop.gd` 换成 `tests/TestShop.cs`。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **5628 条 OK**（基线 5617）；
`run_tests_cs.bat` **6032 条 OK**（基线 6022）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八六、P4 第三十批：`tests/test_damage.gd` → **新建** `tests/TestDamage.cs`（2026-10-11）

伤害结算这一份走**第一档：C# 侧原来根本没有对应文件**（那半边 C# 用例是空的），所以是整份**新建**、
十二节一条不落搬过来，不是「补齐缺口再退场」。旧链十二节 → C# 十二个 `_Check*`，逐节对号：

| 旧链（`tests/test_damage.gd`） | C#（`tests/TestDamage.cs`） | 盯什么 |
|---|---|---|
| `_check_golden_pipeline` | `_CheckGoldenPipeline` | 九步管线黄金数值（关浮动可手算） |
| `_check_element_counter` | `_CheckElementCounter` | 元素克制乘区 |
| `_check_crit_before_defense` | `_CheckCritBeforeDefense` | 暴击在防御**之前** |
| `_check_mastery` | `_CheckMastery` | 熟练度系数 |
| `_check_penetration` | `_CheckPenetration` | 穿透压在**有效防御**上（含「错用会让 145」的反向对照） |
| `_check_block` | `_CheckBlock` | 格挡与闪避互斥 ＋ 0.8 cap |
| `_check_damage_reduction` | `_CheckDamageReduction` | 减伤与 0.6 cap |
| `_check_modifiers` | `_CheckModifiers` | 增伤／减伤乘区 |
| `_check_variance` | `_CheckVariance` | 浮动 0.95~1.05 |
| `_check_hit_and_dodge` | `_CheckHitAndDodge` | 命中 5%~99%（玩家基准 1.0） |
| `_check_unsupported_types` | `_CheckUnsupportedTypes` | 持续伤害不结算直伤 ＋ 快照；反伤类型显式拒绝 |
| `_check_defense_curve` | `_CheckDefenseCurve` | 防御曲线 |

旧链那三个辅助函数 `_step_r`／`_attacker`／`_defender` 在 C# 侧对位 `_Step`／`_Attacker`／`_Defender`。

**断言账**：

- C# 侧 `tests/TestDamage.cs` **新建 1274 条** → 本链 **5781 条**（基线 `tools/RunTestsCs.cs`
  的 `MinAssertions` 4497 → **5771**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_damage.gd"` 行
  （改写注释说明去处）；`tests/test_damage.gd` ＋ `.gd.uid` 已删 → 旧链 **7155 → 5878 条**
  （−1277 ＝ 用例自身条数 ＋ 随文件一起少跑的按文件逐条门限，如
  `test_handshake_contract._check_no_orphan_tests` 那种「每份 `.gd` 一条」；算完文档引用 +1，
  基线 `MIN_ASSERTIONS` 7145 → **5869**）。
- `docs/dev/验证清单.csv`：`damage_pipeline` 行的 `tests/test_damage.gd` 换成 `tests/TestDamage.cs`，
  `skill_system` 行去掉 `tests/test_damage.gd`。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **5879 条 OK**（基线 5869）；
`run_tests_cs.bat` **5781 条 OK**（基线 5771）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八五、P4 第二十九批：`tests/test_menu.gd` **并入** `tests/TestMenu.cs` 后退场（2026-10-11）

启动菜单这一份走**C# 侧部分覆盖 ⇒ 补齐缺口再退场**：`tests/TestMenu.cs` 当时已经盖住了决策层五组
（新建／读取／退出主流程、空配置表、存档写不进去、损坏档、槽位全满 → 覆盖最旧），缺的是**要场景树的那一节**
（旧注释写着「等 P3 搬 UI 时一起走」——P3 早搬完了，这句已经过期）与「读档按配表收口」那一节。

**补了什么（旧链有、C# 侧当时没有的）**：

1. **`_CheckSceneFlow`**（真实场景、真实按钮）：实例化 `main_menu.tscn` → 状态栏「暂无存档」、
   三个按钮的文案与置灰态 → 点「新建游戏」**只切创建界面且不落盘**（设计 13）→ 实例化
   `creation_screen.tscn`，点出身卡、点「确认创建」→ 落盘 ＋ 进占位枢纽页 →
   `GameSession` 里就位 → 回菜单 `_refresh()` ＋ 点槽位按钮读档 → **落在世界地图**（设计 09 §二）→
   点退出触发回调 → 占位场景 `state_lines()` 展示队伍、返回按钮触发回退。
2. **`_CheckStaleEquippedWriteback`**：造一份「旧配表允许 3 枚戒指」的档（戒指槽塞 3 格，当前
   `max_equip=2`）写进槽位，读档时应当**退回 1 件并写回**——再读一次不该还有可退的（决策 101：
   不写回就会每次读档重收，档里一直留着旧记录）。

**踩到一个真坑（值得记下来）**：这一节会把自建模板**注入 autoload 的 `GameData.db`**——
`CreationService` 的确认那一下走 `TableDb.inject_row`（第一次），**读档那一下 `GameState.from_dict` 的
`_apply_custom_templates` 还会再注入一次**。注入只影响那份 db 实例，但用例读的正是它，于是同进程后面跑的
`TestCharacterSheet`（`display_name()` 读模板行）与 `TestBattleSession`（战斗日志打印角色名）当场红成
「陆文昭」。**旧 GDScript 链一直有这个污染**，只是 GD 侧没有哪条用例断言过「书生显示名是家道失落的书生」，
所以从没暴露。这一批在**方法末尾**（两次注入之后）把 `character_base` 换回原表——
那是真正的修复点：还原早了会被读档那次注入再次覆盖。

**顺手统一**：suite 名从「启动菜单：新建 / 读取 / 退出（C# 侧）」改回**「启动菜单：新建 / 读取 / 退出」**。

**断言账**：

- C# 侧 `tests/TestMenu.cs` ＋42 条 → 本链 **4507 条**（基线 `tools/RunTestsCs.cs` MinAssertions
  4455 → **4497**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_menu.gd"` 行
  （改写注释说明去处）；`tests/test_menu.gd` ＋ `.gd.uid` 已删 → 旧链 **7254 → 7155 条**
  （基线 `MIN_ASSERTIONS` 7244 → **7145**；比删文件那一轮多的 1 条是本节的文档引用计数）。
- `验证清单.csv` 的 `main_menu` 那行去掉 `tests/test_menu.gd`（`tests/TestMenu.cs` 本来就在），
  散文里两处旧链指针（`test_menu`／`tests/test_menu.gd`）改指 `TestMenu`。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **7155 条 OK**（基线 7145）；
`run_tests_cs.bat` **4507 条 OK**（基线 4497）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八四、P4 第二十八批：`tests/test_recruit.gd` **并入** `tests/TestRecruit.cs` 后退场（2026-10-11）

剧情招募这一份走**C# 侧部分覆盖 ⇒ 补齐缺口再退场**：`tests/TestRecruit.cs` 当时已经盖住了队伍上限、
小地图／区域两条匹配路径、条件闸、入队奖励（燕小七那一条）、重复入队被拒，缺的是六节
（旧链那几节当初是**按表选行**写的，C# 侧那几节写死了 `ch_ci`／`ch_gang`）。

**补了什么（旧链有、C# 侧当时没有的）**：

1. **开局队伍按 `is_initial` 现取**（不写死是哪几位），并逐人验「**初始成员不算「待加入」**」
   （否则进图那一下会再发一次入队奖励）。
2. **入队真的发齐**：等级取模板 `start_level`、起始装备都穿上（件数对得上）且背包里多了实例、
   每条起始武学都进了已学、起始招式铺进了装配、并点亮 `flag_<char_id>_joined`（引导的下一步就是等它）。
3. **区域路径按表选行**：从 `recruit_def` 里挑「非初始 ∧ `join_scene` 指向 `map_region`」的那位来验，
   换人换位点这条不该跟着红。
4. **条件语言的 `|` 在招募上真的生效**（Q83 的 `flag_a|flag_b`）：用 `FreshDb()` 复制 `recruit_def`
   表与行、把条件改成 `flag_q83_never|flag_luoyanpo_met`，再验「两枚都不亮 → 不入队」、
   「夹具点亮的是**真旗标**」、「任一满足 → 入队」。
5. **区域判定半径钉死数值**：`OverworldController.region_join_distance()` 恰 70px，且与
   `highlight_distance()` 同值（决策 154：「决定事件发不发」的距离要钉值）。
6. **四条招募链的条件与设计表一致**（断链门限，设计 20 §十一）：五行的 `join_condition` 逐条对表，
   且 `BattleScreen.team_win_flags()` 里那两个「打赢队伍 → 置旗标」的值与这里的条件**是同一串字符串**
   （改一处会红）。**不钉「必须有来源」**——落雁坡那条还等着地编的旧镖车位点。

另外把入队奖励补成两条：燕小七（回气散 ＋ 好感 ＋20 ＋ 给人看的短句）之外，
**林铁山**（好感同样 ＋20，柴刀来自起始装备而不是这里发的）。

**顺手统一**：suite 名从「剧情招募（C# 侧）」改回**「剧情招募」**；配套新增两个夹具
`_FirstRow`（按谓词挑行）／`_SceneForRow`（`join_scene` 是区域节点时找它的小地图）与
`_SatisfyCondition`（对位 GDScript 的 `test_case.gd::satisfy_condition`，逐枚点亮真旗标）。

**断言账**：

- C# 侧 `tests/TestRecruit.cs` ＋36 条 → 本链 **4465 条**（基线 `tools/RunTestsCs.cs` MinAssertions
  4419 → **4455**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_recruit.gd"` 行
  （改写注释说明去处）；`tests/test_recruit.gd` ＋ `.gd.uid` 已删 → 旧链 **7319 → 7254 条**
  （基线 `MIN_ASSERTIONS` 7309 → **7244**；比删文件那一轮多的 1 条是本节的文档引用计数）。
- `验证清单.csv` 的 `recruit_system` 那行去掉 `tests/test_recruit.gd`（`tests/TestRecruit.cs` 本来就在），
  并把散文里的三处旧链指针（`::_check_initial_member_can_join_when_not_protagonist`／
  `::_check_join_rewards`／`test_recruit::_check_or_join_condition`）改指 C# 方法名。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **7254 条 OK**（基线 7244）；
`run_tests_cs.bat` **4465 条 OK**（基线 4455）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八三、P4 第二十七批：`tests/test_skill_grant.gd` **并入** `tests/TestSkillGrant.cs` 后退场（2026-10-11）

武学来源与领悟这一份走**C# 侧部分覆盖 ⇒ 补齐缺口再退场**：`tests/TestSkillGrant.cs` 当时已经盖住了
查来源／击败领悟／门槛挡住写明原因／隐藏点位／秘籍研读那几节，缺的只有**真战斗闭环那一节**
（旧注释写着「那半边要场景树」）。

**补了什么（旧链有、C# 侧当时没有的）**：`_CheckBattleFlow` —— 造一支 12 级、点数全塞力量的队伍，
把巡山队明雷（`sp_lp_patrol_01` → `team_bandit_patrol`）塞进 `GameSession.pending_encounter`，
实例化 `scenes/battle_screen.tscn` 挂到 `tree.Root`，用**固定随机种子**打完整场，然后验：
敌人 3 人／能打完／结算卡片写「胜利」／**卡片或战报里出现「领悟」**／喽啰或弓手的武学真进了已学。

**为什么这一节现在能搬**：C# 那条链本来就有真场景树——`tools/RunTestsCs.cs` 往 `TestCase.tree`
注入 `GetTree()`，且刻意延到本帧空闲再跑（小地图那几支已在这么用）。旧注释那句「要场景树」
是 P4 早期的判断，已经过期。

**顺手统一**：suite 名从「武学来源与领悟（C# 侧）」改回**「武学来源与领悟」**。

**断言账**：

- C# 侧 `tests/TestSkillGrant.cs` ＋8 条 → 本链 **4429 条**（基线 `tools/RunTestsCs.cs` MinAssertions
  4411 → **4419**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_skill_grant.gd"` 行
  （改写注释说明去处）；`tests/test_skill_grant.gd` ＋ `.gd.uid` 已删 → 旧链 **7380 → 7319 条**
  （基线 `MIN_ASSERTIONS` 7370 → **7309**；比删文件那一轮多的 1 条是本节的文档引用计数）。
- `验证清单.csv` 的 `skill_system` 那行去掉 `tests/test_skill_grant.gd`（`tests/TestSkillGrant.cs` 本来就在）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **7319 条 OK**（基线 7309）；
`run_tests_cs.bat` **4429 条 OK**（基线 4419）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八二、P4 第二十六批：`tests/test_creation.gd` **并入** `tests/TestCreation.cs` 后退场（2026-10-11）

创建角色与天赋这一份走**C# 侧部分覆盖 ⇒ 补齐缺口再退场**：`tests/TestCreation.cs` 当时已经盖住了
出身卡与七维规则／方案校验／模板路／自建路／天赋那几节的纯逻辑，缺的只有**要真场景的那一节**。

**补了什么（旧链有、C# 侧当时没有的）**：`_CheckScreenCards` —— 实例化
`scenes/creation_screen.tscn`（挂到 `tree.Root`），读它真建出来的 Label：

1. 出身卡第二行 `OriginDetail_ori_scholar` 里要有 `█` 条形图、武器名（剑）与起始武学中文名
   （设计 13 §二／§六：这两条是「给玩家做数值选择用」的，只验「数据备齐了」不够）。
2. 选书生那张卡后，起名格 `NameEdit` 预填的是**真名**（陆文昭），不是卡标题。
3. 换选林铁山后，确认段汇总 `SummaryLabel` 要跟着变：六项（出身／七维／武器／起始武学／天赋／名字）
   都在，且带他的名字与**从表里现取**的起始武学名（不写死"应该是哪一招"）。

**顺手统一**：suite 名从「创建角色与天赋（C# 侧）」改回**「创建角色与天赋」**。

**同批的连带改动**：`docs/dev/设计实现对照.md` 里那行 ✅ 原来指的是
`test_creation._check_screen_cards`（旧链符号），`.gd` 一退场 `test_handshake_docs` 的
「✅ 行点了代码里找不到的符号」当场红——改指 `TestCreation.cs::_CheckScreenCards`。

**断言账**：

- C# 侧 `tests/TestCreation.cs` ＋16 条 → 本链 **4421 条**（基线 `tools/RunTestsCs.cs` MinAssertions
  4395 → **4411**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_creation.gd"` 行
  （改写注释说明去处）；`tests/test_creation.gd` ＋ `.gd.uid` 已删 → 旧链 **7458 → 7380 条**
  （基线 `MIN_ASSERTIONS` 7448 → **7370**；比删文件那一轮多的 1 条是本节的文档引用计数）。
- `验证清单.csv` 的 `char_template` 那行去掉 `tests/test_creation.gd`（`tests/TestCreation.cs` 本来就在），
  并把「默认名与"主角可改"」那句散文里的 `test_creation` 改指 `TestCreation`。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **7380 条 OK**（基线 7370）；
`run_tests_cs.bat` **4421 条 OK**（基线 4411）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八一、P4 第二十五批：`tests/test_story.gd` **并入** `tests/TestStory.cs` 后退场（2026-10-11）

剧情节点与章节推进这一份走**C# 侧部分覆盖 ⇒ 补齐缺口再退场**。`tests/TestStory.cs` 当时已经盖住了
旗标命名与称号条件串／地点匹配／待领名单与领取／章末发 3 部武学／章节推进那几节，缺的是当初
**还写成 GDScript 的整块整合部分**。这一批把它补进 `tests/TestStory.cs`、旧链 `tests/test_story.gd` 退场。

**补了什么（旧链有、C# 侧当时没有的）**：

1. **备货旗标**（设计 18 断点 1）：`flag_supplies_ready` 的判据是「等级 ≥ 5 且背包里有回血道具」——
   光到 5 级背包空的不算、真带上一瓶 `item_med_01` 才点亮、`refresh_derived_flags` **幂等**
   （点亮后不再重复播报）、引导 HUD 跟着走到第 4 步（分母按 `steps(db)` 算、不写死）。
2. **进寨旗标**（设计 18 断点 2）：判据是**入口房间**不是「进了这张图」——从后山密道下到
   `hf3_dungeon` 不算、别的图的入口房间不算、走进 `entrance_room_of(db, scene_heifengzhai)`
   才算，返回值要告诉调用方点亮了哪一枚。
3. **抉择增益穿过 `CharacterSheet` 真进面板**：账册三选一 `kind=choice` 三条都在且都带增益；
   旗标没点亮时零加成；点亮「清名」后**属性点层**（悟性 +2）与**固定值层**（内力上限 +20）
   都进 `naked_attrs()`／`stats()`；面板文案两条能读到；同一个抉择问别人是空的（只发主角）。
4. **剧情节点发武学的落地口径**：`chapter1_end` 发 3 部；1 级书生**够不到门槛时必须在返回里写明原因**
   （不静默）、资质到位后这 3 部**真的学会**（换「资质到位」的存档走 happy path）、剧情节点只能领一次、
   「不限地点」的门派节点（药王谷 `place_id` 留空）站在哪儿都能领。
5. **本命机遇那张表的形状**（设计 21 §九）：表里 5 条 `origin_gift` 每条发一部 ★4 内功、
   条件是 `origin:<char_id>`、门槛值 18 且门槛属性非空；书生在清风驿领得到自己那条、
   林铁山那条在落雁坡（清风驿领不到）、领过就记账不重复发。

**顺手统一**：suite 名从「剧情节点与章节推进（C# 侧）」改回**「剧情节点与章节推进」**。

**一处口径按设计砍掉**：旧链 `_check_origin_gift` 那段注释写着「领到手 = 真的学会」，其实是句空话
——`granted` 里连**被修习门槛挡下**的条目也一起返回，而 1 级林铁山力只有 12、够不到 18。
这一批只验「发的是哪一部」（`granted` 里的 `skill_id`）；**「力加到 18 之后真发到手」那条口径**
在 `tests/TestOverworld.cs::_check_region_origin_gift` 的真场景用例里钉（决策 345）。

**断言账**：

- C# 侧 `tests/TestStory.cs` ＋75 条 → 本链 **4405 条**（基线 `tools/RunTestsCs.cs` MinAssertions
  4320 → **4395**）。
- GDScript 旧链退场：`tools/run_tests.gd` 的 `TEST_SCRIPTS` 删掉 `"res://tests/test_story.gd"` 行
  （改写注释说明去处）；`tests/test_story.gd` ＋ `.gd.uid` 已删 → 旧链 **7552 → 7458 条**
  （基线 `MIN_ASSERTIONS` 7542 → **7448**；比删文件那一轮多的 1 条是本节的文档引用计数）。
- `验证清单.csv` 里 `tests/test_story.gd` 三处（`skill_loadout` 那行的 `::_check_origin_gift` 指针、
  `guide_system` 那行的路径 token 与「0.29.1 补」注释）全部改指 `tests/TestStory.cs`。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **7458 条 OK**（基线 7448）；
`run_tests_cs.bat` **4405 条 OK**（基线 4395）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点八〇、P4 第二十四批：`tests/test_save_store.gd` **并入** `tests/TestSaveStore.cs` 后退场（2026-10-11）

存档槽这一份走**C# 侧部分覆盖 ⇒ 补齐缺口再退场**：`tests/TestSaveStore.cs` 当时已经盖住了
枚举／读写／损坏兜底／选槽（回收损坏槽）／删档／存档时机，缺的是三块与「全字段」有关的东西。
这一批补进那份、旧链 `tests/test_save_store.gd` 退场。

**补了什么（旧链有、C# 侧当时没有的）**：

1. 开场队伍**按表逐人核**：`GameState.default_party_ids_from_recruit(db)` 与 `char_ids` 逐人一致
   （顺序也照 `recruit_def`），而不是只断言「有队伍」。
2. **落盘 JSON 的合法性与版本号**：读回磁盘原文 `Json.ParseString` → `GameState.is_valid_dict()`
   为真、`version == GameState.save_version()`（读档迁移靠它认老档）。
3. **全字段往返**（这一批最值钱的一块）：把 27 个存档字段**全部**设成非默认值——难度／章节／
   建号时间／等级／背包（材料＋带词条装备＋穿戴）／加点／队伍经验／熟练度／装配／已学武学／
   购买记录／回购栈／副本五类记录／保底计数／揭雾／判定结果／旗标／首杀／战斗外气血／大地图坐标
   ——落盘再读回，用自写的 `_DiffPath` 递归比较两份 `to_dict`，**零差异**；另钉 `to_dict` 字段数
   仍是 27（新增字段要一起登记）与 `world_position()` 往返一致。比较器本身还有四条自查
   （丢字段／值不同／数组长度不同／相同结构返回空），否则「零差异」那条断言会是空的。
4. **三个可用档按时间选最旧**：写三个 `saved_unix = 1000 × 槽号` 的合法档，验 `oldest_slot()`
   取 1、`slot_for_new_game()` 覆盖最旧那个、`first_corrupt_slot() == -1`。
5. **存档体积量级**：4 人 ＋ 大背包（道具堆满 ＋ 30 件装备）＋ 熟练度／加点／副本记录／首杀，
   JSON 体积必须 > 1 KB 且 < 64 KB（实测 **6.5 KB**），并且读回再写回体积变化 < 2%
   ——防「不小心把配置表或每帧日志写进存档」这类不报错的膨胀。

**顺手统一**：suite 名从「存档槽与存档时机（C# 侧）」改回**「存档槽与存档时机」**。

**同批的连带改动（重要）**：`tests/` 的 `.gd` 降到 30 份时，
`test_handshake_code.gd::_check_tests_use_seeded_rng` 那条「扫到足够多的用例文件（> 30）」当场红
——它**只扫 `.gd`**。这一批把它**扩到也扫 `.cs`**（并按 C# 写法把提示文案改成 `RngService.create(SEED)`）；
扫到的用例数回到 85，这条网不再随迁移静默缩小。这正是 P5 待办里「门限改扫 `.cs`」的第一条
（原来计划留到最后一批之前做，现在被这一步提前触发）。

**断言账**：GDScript 那条链 7621 ⇒ **7552**（旧链这一份实跑 65 条 ＋ 4 条跟着文件一起少跑的
按文件逐条门限），基线贴到 **7542**。**C# 侧：**4307 ⇒ **4330**（补进来 23 条），基线贴到 **4320**。
**这一批之后 `tests/` 是 30 `.gd`／55 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **4330 条 OK**（基线贴到 4320）；
`run_tests.bat` **7552 条 OK**（基线贴到 7542）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七九、P4 第二十三批：`tests/test_affix.gd` **并入** `tests/TestAffix.cs` 后退场（2026-10-11）

装备词条这一份同样走**C# 侧部分覆盖 ⇒ 补齐缺口再退场**：`tests/TestAffix.cs` 当时只搬了
「纯逻辑」那几节（随机源可复现、数量与区间、槽位／稀有度门槛、贡献与文案换算），
**整合部分整块没搬**——那一份自己的注释写着「不重复 GDScript 那份的整合部分：那些要
`GameState`／`BattleReward`」。现在 `GameState`（第二十三批拆五份）与 `BattleReward`（第十一批）
都已搬 C#，所以这一批把整合部分补进那份、旧链 `tests/test_affix.gd` 退场。

**补了什么（旧链有、C# 侧当时没有的）**：

1. `_check_count_edges`：凡品的条数边界——40 次掷出的集合里有 0 或 1，且**不会出 2 条**。
2. `_check_slot_pool_breadth`：拿**五件真武器**（两把剑／拳套／枪／另一把剑）各掷一遍，
   逐条验「词条允许出现在这个槽位」「同一件不重复出同一条」「这件装备达到该词条的最低稀有度」。
   原来的 C# 侧只掷了 `eq_sword_04` 一件。
3. `_check_contributions_into_attributes`：**词条真的进属性合成**——给书生穿上一件带
   `attr:str +3` 与 `stat:atk_phys +7` 的武器，读面板：五维的「力」+3、外功攻击涨了且不少于 7
   （属性点还会经 `attr_to_stat` 换算），并验词条文案给出「力 +3」「外功攻击 +7」。
4. `_check_save_round_trip`：词条跟着实例走、`GameState.from_dict(to_dict())` 之后词条原样保留、
   贡献列表往返一致（Godot 的 Array 不比内容，用自拼的可比字符串对）。
5. `_check_drop_integration`：`BattleReward.grant` 造装备实例时会滚词条——掉落的剑带的词条数
   落在 `rarity_def` 区间里、每条都在 `affix_pool`。
6. `_check_weapon_element`：**武器系别覆盖招式系别**（05 文档）——装上奇诡剑后战斗单位带
   `weapon_element=odd`，真打一场看「命中事件里的系别是武器给的奇诡」；卸下武器后回到空。
7. `_check_new_rarity_supported`：**设计加一档稀有度时排序自动跟上**（决策 230）——夹具往
   `rarity_def` 尾部加一档 `myth` ＋一条只给 myth 的词条，验 `rarity_rank("myth")==5`、
   新档刷得到、低一档（传世）刷不到（门槛没静默失效）。
   另加小工具 `_signature_of`（泛化原来的 `_signature`）／`_sources`／`_pool_has`／`_tag`。

**顺手统一**：suite 名从「随机源与装备词条（C# 侧）」改回**「随机源与装备词条」**。

**断言账**：GDScript 那条链 7920 ⇒ **7621**（旧链这一份实跑 295 条 ＋ 4 条跟着文件一起少跑的
按文件逐条门限），基线贴到 **7611**。**C# 侧：**4243 ⇒ **4307**（补进来 64 条），基线贴到 **4297**。
**这一批之后 `tests/` 是 31 `.gd`／55 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **4307 条 OK**（基线贴到 4297）；
`run_tests.bat` **7621 条 OK**（基线贴到 7611）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七八、P4 第二十二批：`tests/test_enemy_template.gd` **退场**（C# 侧已是超集，2026-10-11）

这一批没有新建文件、也没有补断言——`tests/TestEnemyTemplate.cs` 早就是旧链那一份的**超集**，
核对完直接退场。这是「二点七三」那条规矩的**第三档**（C# 侧完全覆盖 ⇒ 逐条核对后直接退场，
第一个是 `test_guard.gd`）。

**逐条对账（旧链六节 ↔ C# 同名六节）**：

| 旧链（`tests/test_enemy_template.gd`） | C#（`tests/TestEnemyTemplate.cs`） | 说明 |
|---|---|---|
| `_check_empty_attrs_fall_back` | `_CheckEmptyAttrsFallBack` | 同样两段：发行数据一行都不许空 ＋ 夹具清零走旧派生列（血量＝派生列 ＋ 装备） |
| `_check_shared_pipeline` | `_CheckSharedPipeline` | C# 侧**多三条**：困难档气血更高／经验奖励按难度倍率／难度标签进 `tags` |
| `_check_equipment_contributions` | `_CheckEquipmentContributions` | 一致（清空 vs 留着，比外功与气血） |
| `_check_passive_contributions` | `_CheckPassiveContributions` | 一致（挂 `pf_wudu_03` → 0.15 毒抗；槽位公式口径） |
| `_check_passive_capacity_rule` | `_CheckPassiveCapacityRule` | 一致（超编正好两个 ＋ 夹具把屠夫顶到超编、构建期点名） |
| `_check_attr_map_semantics` | `_CheckAttrMapSemantics` | 一致（全 0＝没配；一列非 0 即整行生效） |
| 夹具 `_with_attrs` | `_WithAttrs` ＋ `_DropsEnemyEquip` | 同形；C# 侧另多一个摘装备夹具 |
| ——（旧链没有） | `_CheckFactoryBasics` | **C# 独有**：AI 模板名单绝对值／招式按 `sort_order` 排／没招式的敌人返回空／整队成员带序号／掉落组去重／威胁标示 |

**结论**：六节一条不落，另多一节工厂口径与三条难度断言；`TestEnemyTemplate` 实跑 40 条 > 旧链 29 条，
退场不掉覆盖面（对数依据就是上表）。

**顺手统一**：suite 名从「敌人模板与角色同管线（C# 侧）」改回**「敌人模板与角色同管线」**。

**断言账**：GDScript 那条链 7953 ⇒ **7920**（这一份用例本身 29 条；其余几条是跟着文件删掉而少跑的
按文件逐条门限——孤儿用例、`.uid` 残骸、验证清单「登记了就要跑」那一类），基线贴到 **7910**。
**C# 侧不动**：仍是 **4243**／基线 **4233**（退场不加断言，suite 改名不改条数）。
**这一批之后 `tests/` 是 32 `.gd`／55 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **4243 条 OK**；`run_tests.bat` **7920 条 OK**
（基线贴到 7910）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七七、P4 第二十一批：`tests/test_talent.gd` **并入** `tests/TestTalent.cs` 后退场（2026-10-11）

天赋这一份同样是**C# 侧部分覆盖 ⇒ 补齐缺口再退场**：`tests/TestTalent.cs` 当时只搬了
「花费／挑选／开关」那四节（对位 `test_creation.gd::_check_talents` 的前半段），而 `rule:` 效果
（`test_talent.gd` 的主菜）**完全没搬**。这一批把七节搬进那份、旧链 `tests/test_talent.gd` 随后退场。

**补了什么（旧链有、C# 侧当时没有的）**：

1. `_check_table_is_consumed`：**「每条 `rule:` 都有人在读」**——扫十个源码文件拼成语料，
   逐条 `talent_effect` 里 `rule:` 之后那段去语料里找；找不到就红，除非写进 `UNCONSUMED`
   （还没接的四条：`mastery_gain`／`bonus_skill`／`defeat_heal_bonus`，各自等设计定口径）。
   这条是这一批里最值钱的一块：天赋一行挂上去、**没有任何东西会报错**，
   玩家花满 5 点选了、面板数值也涨了，而「首回合先手」这类效果可能根本没发生
   （2026-10-04 之前九条里只有 `start_money` 有人读）。
2. `_check_check_bonus`：见多识广（所有判定 +1，属性判定也算）／三寸不烂之舌（**只**加文学 +2，
   医术**不动**——"不该动的地方没动"）。
3. `_check_check_difficulty`：江湖百晓生判定门槛 −2，且**只减到 0 不写负数**。
4. `_check_codex_multiplier`：藏书癖把图鉴每档奖励翻倍，**并且真的进属性合成**（悟性多涨 1），
   外加「表里那值是倍数不是增量」。
5. `_check_shop_discount`：过日子买入价便宜一成，**卖出价不动**（改它等于把玩家的钱变少）。
6. `_check_first_strike`：先发制人只在**第 1 回合**抢到最前，后面照身法走。
7. `_check_skill_damage`：天生武胆招式伤害 ×1.15（乘区与最终伤害各钉一条，没天赋时乘区是 1.0）。
8. 夹具：`_actor`（手搓战斗单位）＋ `_set_picks`（写 `state.talent_picks`）＋ `_int`（缺键给 0）。

**顺手统一**：suite 名从「天赋花费与挑选（C# 侧）」改成**「天赋花费与规则效果」**（旧链没了，
那个后缀失去意义；范围也确实变宽了）。`UNCONSUMED` 用 `System.Collections.Generic.Dictionary`
而不是 Godot 集合——**Godot 集合挂静态字段会活到进程收尾**（施工单坑⑧，`SEHException`）。

**断言账**：GDScript 那条链 7985 ⇒ **7953**（旧链这一份实跑 31 条 ＋ 孤儿门限少扫一个 `.gd`），
基线贴到 **7943**。**C# 侧：**4215 ⇒ **4243**（补进来 28 条），基线贴到 **4233**。
**这一批之后 `tests/` 是 33 `.gd`／55 `.cs`**。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **4243 条 OK**（基线贴到 4233）；
`run_tests.bat` **7953 条 OK**（基线贴到 7943）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七六、P4 第二十批：`tests/test_practice.gd` **并入** `tests/TestPractice.cs` 后退场（2026-10-11）

木桩练习战这一份也走「二点七三／二点七五」那条规矩的**第二档**：`tests/TestPractice.cs` 当时只搬了
四节（入口识别／封顶规则／提示与就绪／`build_encounter` 的绝对值）。这一批把旧链剩下的三节与夹具
补进那份，**不克隆第二个文件**，旧链 `tests/test_practice.gd` 随后退场。

**补了什么（旧链有、C# 侧当时没有的）**：

1. `_CheckMasteryCap`：练习战里熟练度**只压涨、不压低**——5 次施放只涨到上限 3（不是 5）、
   已经练到 5 级的招式打木桩不掉回 3 级（`apply_combat_usage` 的 `cap` 只封这一次涨到哪，
   不压低已有值）；不传上限（0）时按普通战斗的 `mastery_max`（每次 +1）。这一节专门守着
   「打木桩不该惩罚练好的招式」那个容易写反的口径。
2. `_CheckFixtureEncounter`：往**内存副本**补两支行后 `data_ready().ok` 变真、
   `build_encounter` 组出的遭遇是练习战（`practice=true`／队伍 id／`source_key="practice"`／
   `source_scene` 保留）。
3. `_CheckDummyNeverAttacks`：木桩没有招式（`ai_neutral` 且 `enemy_skill` 里一条都没有）→
   打过一回合我方**一点血都没掉**，战报里写明「无可用招式」。这一节要求 `EnemyFactory`／
   `PartyBuilder`／`BattleSimulator` 三件都已搬 C#（所以拖到了这一批才并）。
4. `_with_dummy_rows` 内存夹具：C# 侧行对象**直接 `new EnemyTeamRow`／`new EnemyBaseRow` 就是干净的一份**，
   省掉旧链那个「先复制首表首行、再 `get_property_list()` 按类型逐字段清空」的 `_blank_row`
   ——`TableRow` 在 C# 侧是普通类，没有 `get_property_list()`。**`id` 仍要和主键一起设**：
   表容器按 `row.id` 建索引，只改 `team_id` 会查不到这一行。

**顺手统一**：suite 名从「木桩练习战（C# 侧）」改回**「木桩练习战」**——旧链那份没了，
「（C# 侧）」这个后缀失去意义。

**退场前的逐条对账（C# 是超集）**：C# 那份除了上面补进来的三节，另有几处旧链没有的断言——
`team_id()`／`enemy_id()` 的绝对值、`is_practice_building(null, …)`／`data_ready(null)` 的空表库分支，
以及 `_CheckBuildEncounter` 里的 `spawn_id`／`first_side`／`pending_rules`／`difficulty_id` 四条。
它们都在，所以整份退场不会掉覆盖面。

**断言账**：GDScript 那条链 8031 ⇒ **7985**（旧链这一份实跑 45 条 ＋ 孤儿门限少扫一个 `.gd`），
基线贴到 **7975**。**C# 侧：**4190 ⇒ **4215**（补进来 25 条），基线贴到 **4205**。
**这一批之后 `tests/` 是 34 `.gd`／55 `.cs`**（`.gd` 少一份，`.cs` 数量不变——又是"补齐缺口"省下来的）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **4215 条 OK**（基线贴到 4205）；
`run_tests.bat` **7985 条 OK**（基线贴到 7975）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七五、P4 第十九批：`tests/test_mastery.gd` **并入** `tests/TestMastery.cs` 后退场（2026-10-11）

熟练度这一份**没有新建 C# 文件，也没有整个照抄**：`tests/TestMastery.cs`（`MasteryService` 搬 C# 时
写的那份）当时只搬了一部分，缺的三节＋一段整合**这一批补进那份**，旧链的 `tests/test_mastery.gd` 随后退场。
这是「二点七三」那条规矩的延伸：**C# 侧已覆盖一部分时，补齐缺口再退场**（不克隆第二个文件）。

**补了什么（旧链有、C# 侧当时没有的）**：

1. `_check_multiplier`：熟练度直接决定招式倍率（0 级 ×1.0、练过更强、★1 练满 ×1.5），
   外加**熟练度进存档往返**（`GameState.from_dict(state.to_dict(), db)` 之后还是 4）。
2. `_check_multi_member_ui`：**打坐界面的多人分支**——4 人队伍各有成员按钮、切人后列表跟着换、
   **给谁练就涨谁**（A 一点不涨）。旧链那段注释写着「切人串号在单角色自检里永远看不出来」，
   所以这一节是这一批里最值钱的一块。
3. `_check_full_train_budget`：**整套**打坐的累积边界——钱刚好够从 0 连练到上限时最后一次正好花光、
   正好练了「上限」那么多次；少一文则只能练到上限前一级、余额正好是「最后一级费用 − 1」、
   最后一次被拒且一分没动。
4. `_check_combat_usage` 补上「**真打一场**看施放次数」那段（固定种子 4242 打野狼群，
   最后验「熟练度增量 = 施放次数（封顶后取上限）」）。

**顺手统一**：suite 名从「熟练度与打坐（C# 侧）」改回**「熟练度与打坐」**——旧链那份没了，
「（C# 侧）」这个后缀失去意义（两份并存的姐妹文件才需要它区分）。

**断言账**：合并后 `TestMastery` 静态 69 处、实际跑 84（补进来 37 条左右；
`_check_multi_member_ui` 与两处 `while` 循环都会多跑几条）。GDScript 那条链：
8031 ＝ 8103 − 71 − 1（旧链这一份实跑 71 条 ＋ 孤儿门限少扫一个 `.gd`），基线贴到 8021。
**C# 侧：**4200 ＝ 4163 ＋ 37，基线贴到 4190。**这一批之后 `tests/` 是 35 `.gd`／55 `.cs`**
（`.gd` 少一份，`.cs` 数量不变——这就是"补齐缺口"比"克隆"省下来的地方）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **4200 条 OK**；`run_tests.bat` **8031 条 OK**
（基线贴到 8021）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七四、P4 第十八批：`tests/test_skill.gd` → `tests/TestSkill.cs`（2026-10-11）

第十八份：`tests/test_skill.gd`（约 10.1 KB／静态 42 处）→ **`tests/TestSkill.cs`**
（suite 名「武学：总表／明细／选招」，**`TestSkill.cs` 之前没被占**，不用换名）。五节照搬：
① 表规模与「总表 ↔ 明细表」双向对应（65 部武学＝35 招式＋30 内功，每部都要有对位明细行，
外加通用招式／绑定招式／内功占格 1~3）；② 武器类型闸门（拿剑选剑法、换拳套选拳法、
空手只能通用招式；熟练度只加伤害不改优先级）；③ **「内力刚好够一招」的边界**（判定写
`qi_cost > qi` 才拒 ⇒ **刚好相等必须能用**，差一点则不可用且体检表写明「内力不足」）；
④ 敌人招式映射（毒手带上毒＋毒发、醉刀客用醉里乾坤、大寨主用黑风刀法，所有敌人的招式都要有明细）；
⑤ **DoT 招式必须能点出来**（2026-10-03 那处「五毒掌＋烈火掌六招永远没有按钮」的回归钉子）。

**这批一条形状**：那份 `_actor()` 夹具（`hp 500／atk 100／命中 0.9／base_accuracy=0`…）
**没并进 `TestCase.MeleeActor`**——两者期望值不同（这份要 `base_accuracy = 0` 与更高的攻击），
合成一个会让「改一边动另一边」。留在本文件里当私有夹具。

**断言账**：静态断言点 42、实际跑 **142**（按武学 65 部逐部查明细＋按 11 种敌人逐招查明细＋
六招 DoT 两条循环，全是"按表逐条"那种）。GDScript 那条链：
8103 ＝ 8246 − 142 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8093。**C# 侧：**4163 ＝ 4021 ＋ 142，
基线贴到 4153。**这一批之后 `tests/` 是 36 `.gd`／55 `.cs`。**

**⚠️ 本批顺带记一条网眼**：C# 那条链的「余量上限 100」**真的响了**——搬进来 142 条之后
实际 4163／基线 4011，余量 152 超限，链直接判**失败**并要求贴到 `total − 10`。
**这不是误报，是设计**：它防的是"基线长期松着、删掉一大块断言也不红"。往后每批都要把基线跟着抬。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **4163 条 OK**（搬进来 142 条）；
`run_tests.bat` **8103 条 OK**（基线贴到 8093）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七三、P4 第十七批：`tests/test_guard.gd` **退场**（不新建重复文件）（2026-10-11）

宝箱守卫这一份**没有新建 C# 文件**：`tests/TestGuard.cs`（`GuardService` 搬 C# 时写的那份）
已经把旧链那份 `tests/test_guard.gd` 的**每一节**都搬过去了，而且**是超集**。
再照前几批的样子克隆一个 `testGuardRules.cs` 只会得到两个几乎逐字相同的 300 行文件、
以后每改一次设计都要改两遍——所以这一批改成「**先逐条核对覆盖，再退场 `.gd`**」。

**核对依据（可复核）**：

1. 两边的静态断言点都是 **41 处**，且**逐类对齐**——GDScript 侧是
   `eq=9 false=9 gt=4 ne=2 not_null=3 null=1 true=13`，C# 侧是
   `eq=12 false=8 gt=4 ne=2 notnull=2 null=1 true=12`；差额三处**全都有出处**：
   · C# **多**一条 `_CheckPeaceFlagShape`（旗标名 `flag_guard_<id>_peace` 的绝对值，`eq +1`）——
     那是变异探针「改 `.cs` 不重编 → 量不出来」留下的欠条，旧链没有；
   · C# 少了旧链那条 `check_not_null(guard, "表里第一位守卫")`（改成直接取 `rows[0]`，
     前面 `CheckGt(guards.Count, 0)` 已经兜住 `notnull −1`）；
   · 边界那两条旧链写 `check_true(boundary_ok)` ＋ `check_false(above_ok)`，C# 写成
     `CheckEq(_OnlyCheckOk(...), true/false, …)`（`eq +2`、`true −1`、`false −1`）。
2. 五节内容**逐节对应**：表侧配对（`reward_group` ＝ 房里 `chest_id`）／没有守卫的房间恒可开
   ＋宝箱键拆房间那半边／物品路（不可用→扣一份＋点旗标＋解锁，失败不点旗标）／
   判定路（门槛取表值、`have >= need` 含等号、门槛抬到 999 如实不可用、判定路**不消耗东西**）／
   武取（写房间记录即解锁）。

**这一批之后 `tests/` 是 37 `.gd`／54 `.cs`**（`.gd` 少一份，`.cs` 没多）。
**断言账**：GDScript 那条链从 8291 掉到 **8246**（＝ 旧链这一份的 41 条静态点跑出来 44 条
＋孤儿门限少扫一个 `.gd`），基线贴到 8236；**C# 侧不动**，仍是 4021／基线 4011。

**本轮验收**：`run_tests.bat` **8246 条 OK**（基线贴到 8236）；`run_tests_cs.bat` **4021 条 OK**
（不变）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七二、P4 第十六批：`tests/test_settings.gd` → `tests/TestSettingsRuntime.cs`（2026-10-11）

第十六份：`tests/test_settings.gd`（约 8.8 KB／静态 43 处）→ **`tests/TestSettingsRuntime.cs`**
（suite 名「设置：音量与落盘」）。六节照搬：① 设置文件读写往返（没文件时用默认音量 1.0、
写 0.25 读回 0.25、真的落在基目录旁边）；② 读坏的文件（**报错 + 退回默认音量，不是 0＝静音**）；
③ 自动战斗开关往返，而且**两个键互不覆盖**（每个设置各自整份保存过一次，会把另一个键冲掉——
这是这条用例真正的重点）；④ **音量真的写进音效总线**（要 `Sfx` ＋ `AudioServer`：
`db_to_linear(bus_volume_db) == 0.25`、轮换档位、0 说「静音」不说 0%、静音时总线确实听不见）；
⑤ 枢纽页音量按钮走真场景（按一下 100%→静音→25%、按钮文案与反馈行跟着走、
**落盘失败也不挡使用**）；⑥ 枢纽页自动战斗按钮（文案与开关都跟着走，且真的写进设置文件）。

**类名取 `TestSettingsRuntime`：`TestSettings.cs` 已被占**（`SettingsStore` 搬 C# 时写的那份，
suite 名带「（C# 侧）」，专管落盘那四节，外加音量夹到 `[0,1]` 两条）。这一份是旧链原来那份，
**多出的是"真生效"那两节**（④⑤⑥）——那时 `Sfx`／`PlaceholderGame` 还没搬过来。

**这批两条形状**：

1. **`_settings_override` 是 `[Export] public GodotObject`**（施工单「二点三四」那套），
   C# 用例直接写字段即可；用的时候 `(SettingsStore)hub._settings_override` 拿回来。
2. **`GameSession` 那个节点用 `tree.Root.GetNodeOrNull<Node>("GameSession")?.Call("set_state", …)`**——
   与 GDScript 的 `scene_tree.root.get_node_or_null("GameSession")` 同一条路（可空时跳过）。

**断言账**：静态断言点 43、实际跑 43。GDScript 那条链：
8291 ＝ 8335 − 43 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8281。**C# 侧：**4021 ＝ 3978 ＋ 43，
基线贴到 4011。**这一批之后 `tests/` 是 38 `.gd`／54 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **4021 条 OK**（搬进来 43 条）；
`run_tests.bat` **8291 条 OK**（基线贴到 8281）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七一、P4 第十五批：`tests/test_growth.gd` → `tests/TestGrowthRules.cs`（2026-10-11）

第十五份：`tests/test_growth.gd`（约 8.5 KB／静态 54 处）→ **`tests/TestGrowthRules.cs`**
（suite 名「成长系统：槽位与熟练度」）。六节照搬：① 槽位公式（招式槽 `2+等级/5+悟/8` 上限 9、
内功容量 `2+等级/6+根/10` 上限 7，含 01 采样表那处已订正的口径）；② 熟练度倍率（★1 练满 ×1.50
到 ★5 ×1.25、超上限与负数都夹住、★5 叫「绝学」门槛 25）；③ 打坐费用（★1 一级 50、二级 80、
★5 一级 450、越练越贵）；④ 图鉴奖励（5 部一档、50 部满 10 次、封顶后不再涨、七项各 +2）；
⑤ 内功占格与**熟练度只放大 `stat:` 不放大 `attr:`**（用真实数据挑一部两种前缀都有的内功，
一条用例同时钉两边）；
⑥ **面板显示**（`CharacterSheet`：招式槽 3／容量 2／槽位并入派生数值／七维都在／
悟性与根骨**不可加点**（资质）但不能加的那两次被拒之后面板不变、加了力的那一点面板跟着涨）。

**类名取 `TestGrowthRules`：`TestGrowth.cs` 已被占**（`GrowthCalculator` 搬 C# 时写的那份，
suite 名带「（C# 侧）」，另加天赋「藏书癖」那几条倍数断言）。这一份是旧链原来那份，
**⑥ 是它多出来的一节**——那一节要 `CharacterSheet` ＋ `GameState`，而上一批搬 `GrowthCalculator`
时这两样还没搬过来，所以当时把这一节留在 GDScript 里没动（那份注释里写着"留在 GDScript 那份里"）。
**这一批把它收回来了，那份注释说的欠条算是还清。**

**断言账**：静态断言点 54、实际跑 63（图鉴七项那条 `foreach` 走 7 圈）。
GDScript 那条链：8335 ＝ 8399 − 63 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8325。
**C# 侧：**3978 ＝ 3915 ＋ 63，基线贴到 3968。**这一批之后 `tests/` 是 39 `.gd`／53 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3978 条 OK**（搬进来 63 条）；
`run_tests.bat` **8335 条 OK**（基线贴到 8325）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点七〇、P4 第十四批：`tests/test_world_event.gd` → `tests/TestWorldEventWiring.cs`（2026-10-11）

第十四份：`tests/test_world_event.gd`（约 8.4 KB／静态 37 处）→ **`tests/TestWorldEventWiring.cs`**
（suite 名「大地图随机事件」）。六块照搬：① **八条事件的 `effect_kind`／`effect_id` 与 0.28.0 的 Q64 表
逐条一致**（空一行就会有人拿到「效果还没接上」的占位文案——那正是 0.28.0 要消掉的东西）；
② 赠礼（`we_hermit` 给锈月，按**增量**断言不写死总数；`we_dog` 的兽皮走堆叠）；
③ 线索（要 `ClueService`：触发前没「听说」过、触发后线索本标成「已听说」并写明来源）；
④ 判定（`we_patrol` 直接结算、结果进存档、那一行本来就不挂小地图）；
⑤ 开货架与开战（要 `ShopService`：货架组能找回临时建筑、货架不是空的；历练队伍那条表行）；
⑥ 反面两条（`effect_id` 空着／效果类型认不出都必须显式说话）。

**类名取 `TestWorldEventWiring`：`TestWorldEvent.cs` 已被占**（`WorldEventService` 搬 C# 时写的那份，
suite 名带「（C# 侧）」，另加池子那几条：`rows_for_region`／`pick`／一次性事件出过就退出池子）。
这一份是旧链原来那份，**多出的两块是别的服务**（③ 要 `ClueService`、⑤ 要 `ShopService`——
那时候它们还在 GDScript 里）。两份都留，「八行逐条比」那 28 条在两份里都有，是同一张表的两次核对。

**这批一条形状**：`db.get_row(...)` 在 C# 侧返回的是**强类型 `Resource`**，直接
`(EnemyTeamRow)db.get_row(...)` 转就行——**别写 `.AsGodotObject() as X`**（`Resource` 上没有
`AsGodotObject()`，编译期就红，本批实测）。`Duplicate()` 出来的要再转一次同样的类型。

**断言账**：静态断言点 37、实际跑 65（八行那一段每行 4 条 ⇒ 32，加总数那条）。GDScript 那条链：
8399 ＝ 8465 − 65 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8389。**C# 侧：**3915 ＝ 3850 ＋ 65，
基线贴到 3905。**这一批之后 `tests/` 是 40 `.gd`／52 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3915 条 OK**（搬进来 65 条）；
`run_tests.bat` **8399 条 OK**（基线贴到 8389）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六九、P4 第十三批：`tests/test_dungeon_ui.gd` → `tests/TestDungeonUi.cs`（2026-10-11）

第十三份：`tests/test_dungeon_ui.gd`（约 6.1 KB／静态 32 处）→ **`tests/TestDungeonUi.cs`**
（suite 名「副本完成度界面」）。**这份要真实场景树**（形状照 `TestClue`：运行器注 `tree`、
没有就 `Fail`；`GD.Load<PackedScene>(路径).Instantiate<DungeonScreen>()` 直接拿 C# 类型后成员直读直写；
`tree.Root.AddChild`／`RemoveChild` ＋ `Free()`；节点名用 `FindChild`）。四块照搬：
① 四项完成度与百分比（**分母全部来自表**：宝箱 0/4／隐藏房间 0/5／隐藏 Boss 0/1／事件 0/2）；
② 楼层列表（黑风寨三层都在、未通关时写「已清 0／还差」并置灰扫荡；打通前院后可扫荡）；
③ 扫荡（走调用方的 handler、动画推进与逐间打勾、动画期间防连点、动画结束进度条收走、
**没通关的层仍然是灰的**、没有落点时说明原因）；
④ **长列表不截断**（把第 1 层复制成 30 层，30 个 `FloorRow` 必须全建出来）。

**这批两条形状**：

1. **夹具 `table_with_many_floors` 一起搬过来**（`tests/TestCase.cs::TableWithManyFloors`）——
   真实数据只有 3 层，撑不到"列表被截断"才看得出来（截断在界面上只表现为"少了几层"）。
   照坑53 的形状：复制表 → 逐行 `Duplicate(true)` → 写回 rows／index，**不碰策划的 CSV**。
2. **回调注入用 `Variant` 字段**：`return_handler` 写 `Callable.From(() => closes.Add(true))`；
   `sweep_handler` 写 **带返回值的** `Callable.From<int, Godot.Collections.Dictionary>(…)`；
   测「没落点」那条把字段置回 `default`（对位 GDScript 的 `Callable()`）——
   面板那边 `_has_callback` 走 `callable_invoker.gd` 的 `is_valid`，Nil 判假，
   与 GDScript 侧行为一致。

**断言账**：静态断言点 32、实际跑 32。GDScript 那条链：
8465 ＝ 8498 − 32 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8455。**C# 侧：**3850 ＝ 3818 ＋ 32，
基线贴到 3840。**这一批之后 `tests/` 是 41 `.gd`／51 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3850 条 OK**（搬进来 32 条）；
`run_tests.bat` **8465 条 OK**（基线贴到 8455）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六八、P4 第十二批：`tests/test_level.gd` → `tests/TestLevelFlow.cs`（2026-10-11）

第十二份：`tests/test_level.gd`（约 5.6 KB／静态 39 处）→ **`tests/TestLevelFlow.cs`**
（suite 名「经验与升级」）。五块照搬：① 经验表本身（1→2 要 100／2→3 要 240／19→20 要 9650／
满级 0／上限 20／每级 5 点，外加**逐级递增**那 19 条）；② 付得起才升（99 不升、100 升一级、
跨级一次到位、不够的留着）；③ **升级带动加点上限与等级成长**（要 `CharacterSheet`：
1 级 5 点→2 级 10 点→加 1 点剩 14，且基础气血真的涨了）；④ 满级只记账不消耗；
⑤ **战斗入账顺手升级**（要 `BattleReward.grant`：100 经验当场到 2 级并带上升级文案，
36 经验只记账不升）。

**类名取 `TestLevelFlow`：`TestLevel.cs` 已被占**（`LevelService` 搬 C# 时写的那份，
suite 名「经验与升级（C# 侧）」，管表口径／经验池兑换／满级封顶／面板进度）。
这一份是旧链原来那份，**多出来的两块是整合面**（③⑤ 要 `CharacterSheet`／`BattleReward`，
当时它们还在 GDScript 里，所以那一批没搬）。两份都留——同一份口径的两份实现，
日志里靠「（C# 侧）」后缀区分。

**这批一条形状**：`state` 是跨语言句柄（`GodotObject`），那几个方法用 `Call`
（`level_of`／`append_level`／`available_points`／`spend_point`），字段用 `Get`／`Set`
（`party_exp` 要 `state.Set("party_exp", 99)`，不是属性直写）。照 `TestLevel.cs` 那段抄即可。

**断言账**：静态断言点 39、实际跑 57（逐级递增那条循环走了 19 圈）。GDScript 那条链：
8498 ＝ 8556 − 57 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8488。**C# 侧：**3818 ＝ 3761 ＋ 57，
基线贴到 3808。**这一批之后 `tests/` 是 42 `.gd`／50 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3818 条 OK**（搬进来 57 条）；
`run_tests.bat` **8498 条 OK**（基线贴到 8488）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六七、P4 第十一批：`tests/test_battle_fuzz.gd` → `tests/TestBattleFuzz.cs`（2026-10-11）

第十一份：`tests/test_battle_fuzz.gd`（约 5.2 KB／静态 5 处）→ **`tests/TestBattleFuzz.cs`**
（suite 名「战斗不变量模糊测试（120 场随机对局）」）。这份是**不变量**那一类：120 组
（`seed` 1..120 × 18 支队伍 × 三档难度 × 1~4 人 × 1~15 级）全跑一遍，只问**有没有哪种组合会静默腐坏**——
气血／内力越界、伤害出现 `NaN`／`Inf`／负数、跑不完或回合数超过 `BattleSimulator.MAX_ROUNDS`、
`winner` 落到三个合法值之外；末尾还钉一条**覆盖保证**（两边都得赢过，否则这批种子退化成"同一种架"）。
固定种子保证可复现，失败信息里带 `seed=/队伍/难度`。

**这批一条真踩到的坑（值得记住）**：

- **`Variant.AsGodotDictionary()` 拿数组不报错，给的是一个空字典**：`result["log"]` 是**数组**，
  GDScript 那边写 `Array(result.get("log", []))` 看不出类型问题；照抄成 `_d(result, "log").Count == 0`
  之后 **120 场全被判成「战报是空的」**（链上 1 条 FAIL 里塞了 120 条明细，一眼能看出是同一个原因）。
  补一个 `_arr(...)`（`AsGodotArray()`）即好。**教训**：跨语言读字典字段时先想清那个值是**字典还是数组**，
  `As*` 系列不会替你把关（与坑④同族：读不存在的键会抛，读错类型却沉默）。

**断言账**：静态断言点 5、实际跑 5。GDScript 那条链：8556 ＝ 8562 − 5 − 1（孤儿门限少扫一个 `.gd`），
基线贴到 8546。**C# 侧：**3761 ＝ 3756 ＋ 5，基线贴到 3751。**这一批之后 `tests/` 是 43 `.gd`／49 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3761 条 OK**（搬进来 5 条）；
`run_tests.bat` **8556 条 OK**（基线贴到 8546）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六六、P4 第十批：`tests/test_battle_session.gd` → `tests/TestBattleSession.cs`（2026-10-11）

第十份：`tests/test_battle_session.gd`（约 5.0 KB／静态 10 处）→ **`tests/TestBattleSession.cs`**
（suite 名「分步战斗与遭遇修正」）。这份盯的是**两条路走同一场架**：`step_round()` 逐回合推
（战斗界面按回合播的那条）与 `simulate()` 一次跑完，同种子必须给出**逐字一样的日志**——
分步那条一歪，界面看到的与结算算出来的就是两场架，而且不会报错。三块照搬：
① 逐回合推进与一次跑完的胜负／回合数／**日志完全一致**（`force_hit` ＋ `no_variance`）；
② 先手方（被发现的遭遇里敌方抢在全场最前、正面遭遇时身法高的我方先手）；
③ 奇袭（首回合我方总伤害更高、`pending_rules` 里如实记着「敌方首回合无防备」）。

**这批两条形状**：

1. **数组相等在 C# 里是引用比**（施工单坑⑤再确认）：GDScript 的 `str(stepped["log"]) == str(direct["log"])`
   到 C# 侧要**先拼成一段文本再比**——`_log_text(Variant)` 按 `\n`、`_joined(Array, sep)` 按 `；`
   （`pending_rules` 那处对位 GDScript 的 `"；".join(...)`）。断言条数不变。
2. **夹具返回的是字典**（`{"sim", "allies", "enemies"}`）：跨语言句柄取回来要 `As<BattleSimulator>()`
   ／`AsGodotArray()`。`_ally_damage` 读 `damage_dealt` 时照 `BattleSimulatorProgress.cs` 末尾那套
   **键在不在先判**（`Dictionary` 没有 `Get(key, 默认值)`，坑④）。

**断言账**：静态断言点 10、实际跑 18（逐回合那条循环走了 10 圈，回合数＝18 − 8）。GDScript 那条链：
8562 ＝ 8581 − 18 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8552。**C# 侧：**3756 ＝ 3738 ＋ 18，
基线贴到 3746。**这一批之后 `tests/` 是 44 `.gd`／48 `.cs`。**

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3756 条 OK**（搬进来 18 条）；
`run_tests.bat` **8562 条 OK**（基线贴到 8552）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六五、P4 第九批：`tests/test_audio.gd` → `tests/TestAudio.cs`（2026-10-11）

第九份：`tests/test_audio.gd`（约 4.8 KB）→ **`tests/TestAudio.cs`**（suite 名「音效事件与总线」）。
四块照搬：① 每个事件指向的文件存在且能当 `AudioStream` 加载；② 磁盘上的 `.wav` 每个都有事件在放
（孤儿素材要删或补事件）；③ **没登记的事件名不许静默**（`play("no_such_event")` 不进播放记录、
登记过的能放、`clear_log` 能清空）；④ **总线布局**——启动时就有「Sounds」、布局资源能加载、
布局里有「Sounds」、**拿真播放层问一次解析到的总线**（落到 Master 就说明布局没生效）；
外加复用来的播放层带 MIT 许可证。

**这批三条形状（P4 里第一次从 C# 侧驱动 `addons/` 下的 GDScript）**：

1. **`addons/` 不是迁移对象，但可以从 C# 侧用**：`GD.Load<GDScript>(路径).New(实参…).AsGodotObject()`，
   之后照常 `Get("bus")`／`Call(...)`。`Sfx.cs` 里已经有一处这么建播放层（不带实参），这份是**带实参**的
   第一个例子。
2. **`Godot.PackedStringArray` 这个类型在 C# 侧不存在**（命名空间里没有；仓库的约定是
   **`string[]` ↔ PackedStringArray**，见坑⑧与 `CreationService` 里那段 `Variant.type.PackedStringArray`
   判断）。所以给 GDScript 的 `PackedStringArray` 形参传参时，**直接传一个装着字符串的
   `Godot.Collections.Array`**——和 GDScript 那边写 `["Sounds","SFX"]` 走的是同一条自动转换，
   实测播放层解析出的 `bus` 就是 `Sounds`。
3. **故意 `push_error` 的用例照旧**：`play("no_such_event")` 会往日志里写一行 `ERROR:`——那是它在
   证"不静默"，**不属于日志门限抓的那几类**（`SCRIPT ERROR`／`Attempt to call function`／
   `Invalid access to property`／`String formatting error`／`into a TypedArray of type`），别把它当假红。

**断言账**：静态断言点 16、实际跑 31（按事件与按文件两处循环）。GDScript 那条链：
8581 ＝ 8613 − 31 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8571。**这一批之后 `.gd` 45／`.cs` 47，
C# 侧反超**。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3738 条 OK**（搬进来 31 条）；
`run_tests.bat` **8581 条 OK**（基线贴到 8571）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六四、P4 第八批：`tests/test_encounter.gd` → `tests/TestEncounterRules.cs`（2026-10-11）

第八份：`tests/test_encounter.gd`（约 4.5 KB）→ **`tests/TestEncounterRules.cs`**（suite 名「明雷遭遇规则」）。
五块照搬：阵营常量绝对值（0／1，与 `BattleActor.SIDE_*` 同一出处）／**接触距离 22px 的绝对值**
（开发侧手感值，变异探针实测改成 60 一整套自检照样绿，所以按 `test_poise` 那条纪律把数值本身钉住）／
正面（不强制先手、无奇袭加成、无暂缓规则、文案与队伍名）／背袭（我方先手、敌方架势 −30%、
**没有额外增伤——那是奇袭**）／奇袭（首回合 +50%、只在首回合、不给敌方）／被发现与被追上（敌方先手、
「首回合不能逃」已由 `BattleSimulator` 真判、不再记暂缓）／中立队伍照样能构造遭遇（是否开战由明雷决定）。

**类名 `TestEncounterRules` 而不是 `TestEncounter`**：后者已被占（`Encounter` 搬 C# 时写的那份，
suite 名「明雷遭遇规则（C# 侧）」）。两份不重复——那一份钉 `Encounter` 的构造与字段默认值，
这一份钉**每种接触方式对开局的影响**。日志里靠「（C# 侧）」后缀区分。

**这批两条小形状**：① C# 的 `pending_rules` 是 `string[]`（GDScript 那边是 `Array`），比"空"要写
`.Length == 0`；② `round_modifiers(...)` 返回字典，GDScript 的 `check_eq(..., {})` 在 C# 里写
`.Count == 0`（不能拿字典和空表比引用）。

**断言账**：静态断言点 23、实际跑 23。GDScript 那条链：8613 ＝ 8637 − 23 − 1（孤儿门限少扫一个 `.gd`），
基线贴到 8603。**注意：`tests/` 到这一批正好是 46 `.gd`／46 `.cs` 打平**（往后每批都会把天平压向 C#）。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3707 条 OK**（搬进来 23 条）；
`run_tests.bat` **8613 条 OK**（基线贴到 8603）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六三、P4 第七批：`tests/test_guide.gd` → `tests/TestGuideSteps.cs`（2026-10-11）

第七份（当前最小的一份）：`tests/test_guide.gd`（约 3.9 KB）→ **`tests/TestGuideSteps.cs`**
（suite 名「开局引导」）。三块照搬：`guide_step` 表的形状（6 步、`sort_order` 从 1 连续、每步有文案与
条件、第一步是 `start` 哨兵、最后一步等真实旗标）／**逐步推进与退回**（新档停第一步、无关旗标不推进、
按表逐个点亮条件就一步步往前走、把最后一步的旗标清掉要退回上一步）／HUD 文案（「当前目标：」开头、
取表里的原文与进度分母、满格、**不出现 `flag_` 形态的旗标 id**）。

**这份的价值在"不写死旗标名"**：它从表里读 `condition` 再逐个点亮，设计改旗标名／加一步都不会让用例
变成"永远真"的空壳——搬的时候**这条性质要保住**（C# 里仍然是 `rows[index].condition` 驱动，
没有任何一处写死 `flag_*`）。

**类名比文件名长一点**：`TestGuideSteps.cs` 而不是 `TestGuide.cs`——**`TestGuide.cs` 已经被占了**
（`GuideService` 搬 C# 时写的那一份，suite 名「开局引导（C# 侧）」）。两份不是重复：那一份管**常量绝对值**
与条件语言（`start`／`origin:`／`item:` 那些前缀怎么解析），这一份管表形状与推进／退回／HUD 文案。
**P4 往后挑名字先看 `tests/` 下有没有同名 `.cs`**（GDScript 那边叫 `test_guide.gd` 时看不出冲突）。

**断言账**：静态断言点 18、实际跑 41（按表循环点旗标那两处各跑 5 遍）。GDScript 那条链：
8637 ＝ 8679 − 41 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8627。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3684 条 OK**（搬进来 41 条）；
`run_tests.bat` **8637 条 OK**（基线贴到 8627）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六二、P4 第六批：`tests/test_save_timing.gd` → `tests/TestSaveTiming.cs`（2026-10-11）

第六份：`tests/test_save_timing.gd`（约 4.1 KB）→ **`tests/TestSaveTiming.cs`**（suite 名「存档时机」）。
两块照搬：`SaveService` 的「没槽位不让存（说明是缺槽位、不写文件）／给槽位就落盘（写在当前槽位、
更新时间戳、记下原因、手动与自动两套文案、自动存档不换槽）」＋**存档往返真的是当前进度**
（等级／加点后的裸属性／背包／装备实例计数／队伍经验／完成度／事件判定）。临时目录仍是
`.logs/test_save_timing`，用例开头自己清场。

**这批三条形状**：

1. **`GameState` 的字段是 C# 属性，直接写**：`((GameState)state).slot = 2`、`state.party_exp = 77`、
   `state.saved_unix` 直接读——不用 `Set`／`Get`（那一层是给 GDScript 句柄的）。
2. **`Inventory.add_equipment()` 第三个参数是 `Godot.Collections.Array` 不是 `string[]`**：
   GDScript 那边写 `[]` 就完了，C# 要 `new Godot.Collections.Array()`（照 `Inventory.cs` 的签名走）。
3. **清目录／建目录走 `DirAccess`**：`DirExistsAbsolute`／`Open`／`GetFiles`／`Remove`／
   `MakeDirRecursiveAbsolute`——这一段是 C# 里第一次用，后面的存档／落盘类用例照抄。

**suite 名与已有的 `TestSaveStore.cs`（「存档槽与存档时机（C# 侧）」）区分开**：这份管的是**时机与内容**，
那一份管的是槽位列表与文件花样；名字贴近但不同，日志里能一眼分开。

**断言账**：静态断言点 23、实际跑 23（「读回来失败就提前返回」那条分支在正常数据上不走）。
GDScript 那条链：8679 ＝ 8703 − 23 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8669。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3643 条 OK**（搬进来 23 条）；
`run_tests.bat` **8679 条 OK**（基线贴到 8669）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六一、P4 第五批：`tests/test_drop.gd` → `tests/TestDrop.cs`（2026-10-11）

第五份：`tests/test_drop.gd`（约 10.6 KB）→ **`tests/TestDrop.cs`**（suite 名「掉落、首杀与保底」）。
九块照搬：保底字典版本标记＝1／必掉槽每次都出（2000 次）＋草药掉率区间＋铜钱数量区间 3~8／
**`pick_weighted` 的权重行为与空表返回 null**／难度倍率让掉落明显增多／难度**不改清单**（隐藏残卷必掉、
装备不再列在掉落表里）／首杀必掉且只保一件、重复刷按 40% 出货／**首杀记录进存档（含 v9 老档迁移）**／
保底到点必出与计数清零／**保底计数器可序列化与恢复**／保底跨难度继承／运属性掉落机缘。固定种子 90210。

**与 `tests/TestDropResolver.cs` 的分工（别当成重复用例）**：那一份是 `DropResolver` 搬 C# 时**新写**的
对位用例（必掉槽／概率区间／难度只改几率／首杀武器优先／保底到点必出／机缘倍率）；这一份是**旧链原来那份**
「掉落与保底」，多出来的是三块：**保底计数器的语义与序列化**、**首杀记录进存档（含 v9 老档迁移）**、
**权重抽取器 `pick_weighted`**。两份都要留着——它们钉的不是同一层。

**suite 名故意取成「掉落、首杀与保底」**：那一份叫「掉落与保底（C# 侧）」，两份都写「掉落与保底」的话
日志里分不清是哪条红了（suite 名只进日志、不进任何门限，所以改名没有连带成本）。

**这批的形状**：

1. **空权重表返回的不是 C# 的 `null`，是「装着 null 的 `Variant`」**：`CheckEq(object, object)` 收的是
   装箱值，直接比会判不等。要写成 `rng.pick_weighted(空表).AsGodotObject()` 再比 null
   （`Variant.AsGodotObject()` 对 null 变体返回 null）。
2. **内存夹具造"保底槽"**：复制真实 `drop_table` → 复制首行 → 改 `drop_row_id`／`base_rate=0`／
   `pity_count=20` → 写回 `rows`／`index`（发行数据里那两条带保底的行 0.14.0 已删，所以只能靠夹具）。
   提取成私有 `_db_with_pity_row(row_id, group_id, pity_count)`，两条保底用例共用。
3. 行字段是 C# 属性（`template.base_rate = 0.0`、`template.difficulty_scaled = false`），
   `local.tables["drop_table"]` 取到的是 `TableResource`，改完要**写回字典**（`local.tables[...] = table`）。

**断言账**：本用例静态断言点 35、实际跑 37。GDScript 那条链：8703 ＝ 8741 − 37 − 1（孤儿门限少扫一个 `.gd`），
基线贴到 8693。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3620 条 OK**（搬进来 37 条）；
`run_tests.bat` **8703 条 OK**（基线贴到 8693）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点六〇、P4 第四批：`tests/test_clue.gd` → `tests/TestClue.cs`（2026-10-11）

第四份：`tests/test_clue.gd`（约 10.4 KB）→ **`tests/TestClue.cs`**（`SuiteName()` 返回「线索本」）。
七块照搬：线索来源的解析口径（分号分隔、空段丢掉、去首尾空白；隐藏与事件判定两张行走同一套）／
一张图的线索（黑风寨 11 条、塌陷山洞 1 条、「缺位点」那条要明说而不是静悄悄消失）／大地图按地标归组
（9 条）／传闻行「看到于：荒村 · 废屋」（**这条是 2026-10-11 搬 C# 时发现的真 bug**：原代码取
`room.name_cn`，而表的列叫 `room_name`，房间名一直取不到、运行期报 Invalid get index）／
完成状态跟着存档走／长列表不截断（6 倍复制成 41 条）／面板场景（五类页签＋按 scope 渲染＋能关）。

**这是第一份"要场景树"的 C# 用例**，形状定下来（后面的 UI 用例照抄）：

1. 运行器把 `tree` 注进来（对位 GDScript 的 `scene_tree`）；**没有就 `Fail` ＋ `return`**，
   对位原来那个 `if scene_tree == null` 分支。
2. 装场景：`GD.Load<PackedScene>(路径).Instantiate<ClueScreen>()`——**直接 `Instantiate<t>()` 拿到 C# 类型**，
   于是 `db_override = wide`／`scope = "scene"`／`_subtitle.text` 这些都是**成员直读直写**，
   不用像 GDScript 用例那样靠 `Call`／`Get`（那一层是给跨语言句柄用的）。
   `[Export] public Variant` 的字段直接吃 C# 对象与 `Callable.From(...)`（回退回调就是这么挂的）。
3. 挂／卸：`tree.Root.AddChild(panel)` → 跑 → `tree.Root.RemoveChild(panel)` → `panel.Free()`（C# 是 `Free()`）。
   节点名查找用 `FindChild("Subtitle", true, false)`（对位 `find_child`）。

**两处"GDScript 能直接做、C# 要绕一下"**：

* **数组不能整块比**：`check_eq(titles, ["主线", …])` 在 GDScript 里比的是内容，C# 的
  `CheckEq(object, object)` 比的是引用。这里**拼成一个字符串再比**（`"主线|支线|传闻|角色|隐藏"`），
  断言条数不变（还是 1 条）——别改成逐元素断言，那会把这条用例的断言数账单改掉。
* **行类要 `as` 成具体类型**才能用 `clue_source = …`／`clues()`：`db.get_row("hidden_trigger", …) as
  HiddenTriggerRow`，复制行走 `(HiddenTriggerRow)base.Duplicate(true)`。

**顺带搬过来一个夹具**：`tests/TestCase.cs` 新增 `TableWithDuplicatedTriggers(scene_id, times)`
（对位 GDScript 的 `table_with_duplicated_triggers`）——**后面 `test_dungeon_ui.gd` 的长列表用例还要用它
的兄弟 `table_with_many_floors`**，搬那一份时照这个形状补。

**断言账**：本用例两边**逐条对齐**——旧链原本报「断言 92／静态 48」，搬过来之后 C# 侧同样是
92／48。GDScript 那条链：8741 ＝ 8834 − 92 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8731。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3583 条 OK**（搬进来 92 条）；
`run_tests.bat` **8741 条 OK**（基线贴到 8731）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点五九、P4 第三批：`tests/test_poise.gd` → `tests/TestPoise.cs`（2026-10-11）

第三份：`tests/test_poise.gd`（约 8.9 KB）→ **`tests/TestPoise.cs`**（`SuiteName()` 返回「架势与破绽」）。
七块照搬：架势池（野狼 20／大寨主 160／回复 10% vs 30%／玩家 `40+8×等级`）／打空进破绽与日志文案／
**破绽窗口挨打 ×1.5**（期望值写死 1.5，故意不写 `1 + 常量`，防"常量一改期望值跟着改"的假绿）／
窗口 1 回合后重置并封顶回复／**真战斗回合末**回复（大寨主 +30% 上限＝+48）／背袭压到 70%／
「正好打空」的边界（差 1 点不进破绽、比例拉满只压到 1）。固定种子 8080。

**这批新遇到的两类"GDScript 鸭子类型 → C# 要显式"**：

1. **`EnemyFactory.create_enemy()` 返回的是 `GodotObject`**，GDScript 那边直接 `.poise`／`.max_poise()`
   就用了；C# 必须 `as BattleActor` 之后再调（`create_team()` 返回 `Godot.Collections.Array`，
   元素同样要 `AsGodotObject() as BattleActor`）。
2. **`BattleActor.from_character()` 五个实参写全**：`from_character(db, 行, 等级, new Dictionary(), new Array())`。
   这条是坑⑤的同族——C# 的默认参数只服务 C# 侧，少写就编不过（本用例里包了一个 `_character(db, 等级)`
   小助手，七处调用都走它）。

**顺带记一个读日志的形状**：`sim.result()["log"]` 是字符串数组，GDScript 写
`"；".join(PackedStringArray(log))`；C# 侧包了一个 `_log_lines(result)` 把它转成 `string[]`
再用 `string.Join("；", …)`——**别指望 `AsStringArray()` 直接吃 `Array`**（元素是 Variant）。

**断言账**：本用例静态断言点 33、实际跑 37（背袭那两条在 `team_wolf_pack` 的 2 个敌人上各跑一遍）。
GDScript 那条链：8834 ＝ 8872 − 37 − 1（孤儿门限少扫一个 `.gd`），基线贴到 8824。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3491 条 OK**（搬进来 37 条）；
`run_tests.bat` **8834 条 OK**（基线贴到 8824）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

## 二点五八、P4 第二批：`tests/test_multihit.gd` → `tests/TestMultihit.cs`（2026-10-11）

按「小 → 大」的第二份：`tests/test_multihit.gd`（约 8 KB／20 条断言，依赖 `BattleActor`／
`BattleSimulator`／`RngService`，都已 C#）→ **`tests/TestMultihit.cs`**（`SuiteName()` 返回
「多段攻击与目标类型」）。六块照搬：夜雨 2 段 × 2.5 倍率 = 起手 1.0 的 5 倍／横扫 3 段打全体
（手选一个目标也波及全场 3 人）／破架势逐段结算（3 段扣 3 倍，数值按表的 `poise_damage` 加攻击者
`poise_break` 算）／命中事件带段序号（0 与 1、事件里记总段数）／单段回归（只出一行日志、不写段数）／
自动策略打分把段数与人数乘进来（1 人 3.9、3 人 11.7，压过斩风的 2.1）。固定种子 20261003。

**这一批顺出来的两件事**：

1. **手搓战斗单位升格成共用夹具**：`tests/TestCase.cs` 新增 `MeleeActor(id, side, overrides)`，
   与 GDScript 用例里那份 `_actor()` 对位（20 个属性键写全 → 覆盖项 → `poise_max = 100` → `refill()`）；
   `TestCooldown.cs` 里那份私有副本**已删掉改用它**。后面的战斗类用例（架势／增益／伤害管线…）**直接用这一份**，
   别再各写各的——两份实现一旦漂移，期望值会以"某一份忘了改"的形式悄悄错。
   要更大的架势上限就先建出来再改字段（`poise_max`／`poise` 都是 `[Export]`，直接写得到）。
2. **`Godot.Collections.Dictionary` 确实没有 `Get(key, 默认值)`**（坑④再确认一次）：用例里读事件
   （`event.get("kind","")` 那套）要自己包一层 `_s(dict,key,fallback)`／`_i(...)`，写法照仓库里
   `BattleSimulatorProgress.cs` 末尾那四个私有小助手。**这是编不过的硬错，不是风格问题**——
   第一遍编译就撞在这上面（5 处 `CS1061`）。

**断言账（这批开始要盯的一类）**：`test_handshake_contract._check_no_orphan_tests` 只遍历
`tests/*.gd`、**按文件逐条断言**——所以每搬走一份 `.gd`，GDScript 那条链除了少掉用例本身的断言，
还要**再少 1 条**（这批就是 8893 − 20 − 1 = **8872**）。**P5 之前必须把它（连同
`_check_verify_tests_are_executed` 那类"只认 `.gd`"的网）改成也扫 `.cs`**，否则全搬完之后这条网
会静默地什么都不查——这是"门限还在、覆盖面已经空了"的典型，比没写更糟。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests_cs.bat` **3454 条 OK**（搬进来 20 条）；
`run_tests.bat` **8872 条 OK**（基线贴到 8862）；`validate_tables.ps1` `PASSED`；`check_loop.bat` `OK`。

**❗观察到一次"退出期崩溃卡住验收链"（还没修，先记症状）**：第一次跑 C# 链时，
`SELFCHECK: OK (3454 assertions)` 已经打出来了，紧跟着日志尾巴是
`ERROR: Index p_frame = 0 is out of bounds ((int)tiles[...].animation_frames_durations.size() = 0)`
→ `CrashHandlerException: Program crashed with signal 11`，**引擎进程没退**，
于是外层 `cmd` 一直等它，整条链看着像"卡死"（其实三样都已经过了，只是没走到打印）。
处理：把那个游离的引擎进程结束掉即可（**别动常驻编辑器那个实例**），链随后自己跑完
（`validate_tables.ps1` `PASSED`、`LOOPCHECK: OK`）——**同一命令再跑一遍是干净的 10 秒**，
所以是偶发、且发生在**引擎退出清理**阶段（TileSet 动画那一块），与用例本身无关。
**别把它当成"用例红"**：判据看那行 `SELFCHECK`。若再复现，考虑给 `.bat` 里那个引擎调用加一条硬超时
（cmd 本身做不到，得换 PowerShell 包裹），否则整套验收会停在那儿等人来 kill。

## 二点五七、P4 第一批：`tests/test_cooldown.gd` → `tests/TestCooldown.cs`（2026-10-11）

**P4（用例与工具链的搬迁）开张的第一批**，选最小、最自包含的一份先把「两边链都绿」的流程跑通：
`tests/test_cooldown.gd`（120 行／21 条断言，只依赖 `BattleActor`／`BattleSimulator`／`RngService`，
三者都已是 C#）→ **`tests/TestCooldown.cs`**（`public partial class TestCooldown : TestCase`，
`namespace Lunhui.tests`，`SuiteName()` 返回「招式冷却」）。

**用例内容照搬**（21 条断言一条不多不少，两边的"静态断言点 vs 实际执行数"都是 21）：
表里 20 个招式配了冷却（点星 2／颠倒 5／起手式 0）＋放完进冷却、第 N+1 回合又能用
（2 回合 → 第 4 回合起可用；第 4 回合再放 → 第 7 回合）＋体检表把冷却写成原因且 AI 不挑冷却中的招
＋冷却按「单位 × 招式」记（别人的同一招、自己的别招都不受影响）。固定种子 20261003，
`force_hit`／`no_variance`／`no_block` 三个修饰照旧。

**这一批的形状变化（P4 的通用做法，后面的用例照这个来）**：

1. **C# 用例跑不了 `--script`**，只能「场景入口 ＋ 节点跑完 `GetTree().Quit(code)`」——
   所以 `tools/RunTestsCs.cs` 的 `Suites` 是**唯一登记处**：搬一份就在那儿加一行
   `("res://tests/X.cs", () => new X())`，同时在 `tools/run_tests.gd` 的 `TEST_SCRIPTS` 里**删掉**
   对应的 `.gd` 行。两边清单各管各的链，别只改一边。
2. **`.gd` 要连 `.gd.uid` 一起删**。
3. **验证清单跟着改**：`docs/dev/验证清单.csv` 的 `combat_core` 行 `verify_paths` 里
   `tests/test_cooldown.gd` → `tests/TestCooldown.cs`（`test_handshake_contract` 的
   `_check_verify_tests_are_executed` **两份清单都读**，改一处路径即可，不会假红）。
4. **两条链的基线各调各的**：`run_tests.gd` 的 `MIN_ASSERTIONS` 这一批**没动**（实测 8891，
   基线 8852、余量 39 < 100）；`RunTestsCs.cs` 的 `MinAssertions` 由 3412 → **3424**
   （实测 3434 − 10）。搬用例是"把断言从这条链挪到那条链"，所以 GDScript 侧的下限迟早要跟着降——
   **降到顶着下限那天就得调，不调会红在"断言数"上而不是红在真错上**。

**顺手补的一条网（不加会静默掉覆盖）**：`test_handshake_code._check_tests_use_seeded_rng` 只扫
`res://tests/*.gd`，而且 P5 要随 `run_tests.gd` 一起退场——用例一搬成 C# 就没人管"随机源有没有给种子"了。
于是往骨架自检 `tests/HarnessSelfTest.cs` 里补了同一口径的 C# 半边：扫 `res://tests/*.cs`，
发现 `RngService.create_random(`／`new RngService()`／`new RandomNumberGenerator()`
就记一条失败（扫之前**先抹掉字符串字面量**，否则模式表会把门限自己判红——GDScript 那版第一次跑就是这么红的）。
实测现有 39 份 `.cs` 里 0 处违规。

**本批实测结论（写死别再试）**：

1. **手搓战斗单位的夹具在 C# 侧这样写**（GDScript 版的 `_actor` 对位）：
   `new BattleActor { actor_id = …, display_name = …, side = BattleActor.side_ally(), level = 10,
   base_accuracy = 1.0, stats = new Godot.Collections.Dictionary { {"hp_max", 300.0}, … } }`，
   覆盖项用 `actor.stats[key] = value` 单独写，最后 `actor.poise_max = 100; actor.refill();`。
   **`stats` 那 20 个键要写全**（少一个 `stat()` 就吃兜底值，期望值会悄悄不对）。
2. **C# 侧 `CheckEq` 收 `object`**，`int`／`double` 混着传不会假红（`TestCase.ValueEquals` 里先按数值比，
   这条是刻意留的、对位 GDScript 的 `1 == 1.0`）。
3. **`db.rows("表")` 的元素是 `Variant`**：`entry.AsGodotObject() as SkillActiveRow`，
   用 `is` 判空那套照 `TestCase.PartyState()` 的写法。`db.get_row("表", id)` 返回 `Resource`，直接 `as` 成行类。
4. **写交接单／施工单会改变 GDScript 那条链的断言数**（这次实测的"8891 → 8893"就是它）：
   `test_handshake_repo._check_doc_tool_references` 对 `docs/dev/*.md` 里**每一处「`tools/` ＋ 文件名 ＋
   `.bat`／`.ps1`／`.gd`／`.py`」的引用**各记一条断言（查那个工具文件在不在）。本节正文引一次、
   交接单那一段引一次 —— 都是那个跑用例的 `.gd` ⇒ 正好多 2 条。
   **所以"实测数"要照着改完文档之后再量的那个数写**，别拿改文档之前的数——这条坑浪费过一次排查时间。

**本轮验收**：`dotnet build` 0 错 0 警；`run_tests.bat` **8893 条 OK**（上一批 8912，这一批搬走 21 条、
文档里那两处工具引用又加回 2 条；连跑两遍一致）；`run_tests_cs.bat` **3434 条 OK**；
`run_tests_cs.bat` **3434 条 OK**（多 22 条＝搬进来的 21 ＋ 骨架那条新网）；`validate_tables.ps1` `PASSED`；
`check_loop.bat` `OK`。

## 三、这些不是"翻译"（改造点，先想清楚再动）

1. **`--script` 只能跑 GDScript**：`tools\run_tests.bat`／`check_scene.bat`／`check_loop.bat` 现在
   都是 `godot --headless --script res://tools/run_tests.gd` 这种形状。C# 侧要改成**场景入口**
   （`tools/run_tests_cs.tscn` ＋ C# 节点，跑完 `GetTree().Quit(code)`），`.bat` 跟着改调用形状。
2. **`class_name` 全局类名没了**：改用命名空间 ＋ 显式 `using`；仓库那条"跨文件调用必须走 `preload`"
   的门限（`test_handshake_code` 的 `_check_preload_for_global_class_calls`）在 C# 里**没有对应物**，
   要么删、要么换成"不许反射调私有成员"这类新门限。
3. **动态写法要变强类型**：`Variant` 字典当表用、`has_method()` 鸭子类型、`@export`、信号、`await`、
   `Resource` 子类序列化——都是 C# 里最费工的地方（行类 60 多张表首当其冲）。
4. **存档 v12 兼容**：字段名／类型不许变；C# 侧要留迁移分支（老档能读）。
5. **15 个用例读 `.gd` 源码文本做门限**（`:get_file_as_string`）——C# 里要改成读 `.cs` 或改成反射断言；
   它们扫的常量名／类名口径要一起换。
6. **`addons/` 不动**：`addons/godot-mcp` 是编辑器插件、`addons/sound_manager` 是第三方 MIT 库，
   两者都保持 GDScript（`src/audio/` 通过它们的接口调用，接口名不变）。

## 四、验证与并行线程

- **迁移期 `run_tests.bat` 会阶段性变红**（`src/` 与 `tools/` 不同语言时旧链读不了）。
  过渡办法：**C# 侧新链 ＋ 旧链双跑**，每搬完一个模块两边都绿；哪边红哪边就是回归。
- 另外四条线程（小策划／小美术1／小地编／小文案）跑的是**同一条验收**——迁移期间不能让他们踩红。
  约定：**迁移只动代码，不动表与文档口径**；真要改口径（`模块对接表.code_paths`、验证清单、
  AGENTS 里的 `.gd` 约定）**改之前先通知他们**。
- 全量验收仍是"同一时刻只许一条线程跑"。

## 五、节奏与估时

- 总量 **219 文件 / 52,096 行**；按 **80–130 轮**推进（一轮 = 一个模块或一批小文件 ＋ 一轮验收）。
- 顺序按"风险从低到高"：行类（机械、量大）→ 纯逻辑 → 战斗 → 世界／界面 → 工具链。
- 每轮的产物：一对 `.cs` ＋ 它的 C# 用例 ＋ 回填过的对接表／验证清单；**不许留半截文件**。
- **P5 要顺手清掉两条「随迁移过期」的护栏**：① `test_handshake_code::_check_preload_for_global_class_calls`
  的「扫到 > 5 个全局类」——GDScript 清零后这个数必然是 0，这条连同整份 GDScript 链一起退场；
  ② `tools/run_tests.gd` 的 `MIN_ASSERTIONS`（同理，GDScript 链没了它就没意义）。
  → **两条都已完成（2026-10-11，见「三点一二」第六／七节）**。
