## 策划对接守卫。
##
## 把「策划增加或修改需求后，计划与验证必须跟着动」这条规则变成硬门槛：
##   1. `docs/design/CHANGELOG.md` 的当前设计版本，必须与已动工模块的 design_version 一致
##   2. 已动工（进行中／已完成／需返工）的模块必须在 `docs/dev/验证清单.csv` 登记验证用例
##   3. 验证用例文件必须真实存在，且 verified_design_version 必须等于当前设计版本
##   4. `tests/` 下的用例文件（基础设施除外）必须被某个模块引用，不许有孤儿验证
##
## 红了怎么办：先读 CHANGELOG 看改了什么，评估要不要返工；然后更新代码、补或改用例，
## 最后同步 `docs/dev/模块对接表.csv`（design_version／status）与 `docs/dev/验证清单.csv`。
extends "res://tests/test_case.gd"

const CHANGELOG_PATH := "res://docs/design/CHANGELOG.md"
const TableValidatorScript := preload("res://src/core/table_validator.gd")
const TableRegistryScript := preload("res://src/data/table_registry.gd")
const CONTRACT_PATH := "res://docs/dev/模块对接表.csv"
const VERIFY_PATH := "res://docs/dev/验证清单.csv"
const TESTS_DIR := "res://tests"
const STATUS_PATH := "res://docs/dev/当前状态.md"
const QUESTIONS_PATH := "res://docs/dev/待策划确认.md"
const CONTRAST_PATH := "res://docs/dev/设计实现对照.md"
const HANDOVER_PATH := "res://docs/design/交接单.md"
const ART_SPEC_PATH := "res://docs/design/15_美术风格需求.md"
const ART_HANDOVER_PATH := "res://docs/design/交接单_美术.md"

## 需要「计划和验证跟上」的状态。
## 「需返工」故意不在其中：它本来就被标成落后于当前设计版本，是待处理状态，
## 不能因为版本对不上就再报一次；但它的验证用例仍然要登记（返工要有验收目标）。
const STARTED_STATUSES := ["进行中", "已完成"]
const MENTIONED_STATUSES := ["进行中", "已完成", "需返工"]

## 基础设施用例：不属于某个策划模块，不参与孤儿检查。
## `test_layout_budget.gd` 是"门限的裁判自己也要有反例"（见该文件头与框架说明决策 189）——
## 它盯的是 `LayoutBudget` 本身，不对应任何一条策划需求。
const INFRASTRUCTURE_TESTS := [
	"test_case.gd", "test_handshake.gd", "test_layout_budget.gd",
	# 拆分出来的八族（2026-10-04）：都注册在 tools/run_tests.gd 的 TEST_SCRIPTS 里，
	# 各自是一道门的检查；它们不对应某一个策划模块，所以走基础设施白名单。
	"test_handshake_code.gd", "test_handshake_contract.gd", "test_handshake_doccounts.gd", "test_handshake_docs.gd", "test_handshake_flags.gd", "test_handshake_numbers.gd", "test_handshake_repo.gd", "test_handshake_wiring.gd",
]

## 只把文本文件读进语料。`docs/dev/images/` 下有 PNG 截图，当文本读会刷一屏
## 「Unicode parsing error」并把二进制垃圾塞进语料（踩过：9 条告警，全是截图）。
const TEXT_EXTENSIONS := ["gd", "tscn", "tres", "godot", "csv", "md", "json", "bat", "ps1", "txt"]

## 采样表那条门限要拿 growth_const 的公式重算，所以直接读 GrowthCalculator（不手抄公式）
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")
## 「框架说明」概述里那两个存档数字（当前版本 vN／`to_dict()` 字段数）要拿代码现算
const GameStateScript := preload("res://src/core/game_state.gd")
## 套装档位的可达性是「有没有人能拿到」，与 SkillGrant 认的来源是同一个真相
const SkillGrantScript := preload("res://src/core/skill_grant.gd")


## 31. 03 的「隐藏内容」7 行表要与 `hidden_trigger` 对齐（名称与类型都对得上）。
##
## 由来（2026-10-03）：这张表列的是"第一章七种钥匙"，而**七类里有一类（顺序／三火盆）还没做**——
## 也就是说这张表天然容易和代码/数据脱节。此前只有 `validate_tables` 的**警告**在报"线索条数"
## 那条（5.20），**名称与类型没人核**：doc 把某条的名称写错、或把类型从"击杀方式"改成"行为"，谁都不会发现。
##
## 类型词是中文（道具／空间／顺序／行为／击杀方式／完整度／携带物），枚举在代码里，
## 所以这里手写一张 7 条的映射（每一对的枚举都取自 `table_validator` 的 `trigger_type` 允许值）。
## 数量也核：03 说七个层次，数据里就该有 7 行（多一个少一个都点出来）。
const HIDDEN_TYPE_WORDS := {
	"道具": "item", "空间": "space", "顺序": "sequence", "行为": "behavior",
	"击杀方式": "kill_style", "完整度": "completion", "携带物": "carry",
}
## 29. `GameState` 里**声明的每个字段**都必须进 `to_dict()`（或写进白名单并说明理由）。
##
## 由来（2026-10-03）：存档字段是"加了就得存"的东西，而**忘了加进 to_dict() 不会报任何错**——
## 那个字段每次读档都静默回到默认值（玩家表现是"我明明买了/学了，怎么没了"）。
## 升版本的纪律（`ADDED_IN` 那张表）靠人记，这条门限是机器兜底：
## 扫 `src/core/game_state.gd` 的 `var x` 声明与 `to_dict()` 里的 `"x":` 键，缺一个就红。
## 白名单是**不持久化**的三个"读档整理结果"（只报给菜单看，不回写）。
## `Inventory` 同样列出（它有自己的一份 to_dict/from_dict，同一个坑）。
##
## 2026-10-03 补两处：
##   ① **反向**：`to_dict()` 写进去的每个键，都必须在文件里被读过——"存了却回不来"是同一个
##      玩家症状的另一半（实测两个文件现在都是 0 个孤儿键）。
##   ② 取键的范围从"文件后半段"收成 **`to_dict()` 的函数体**：以前是从 `func to_dict` 一直截到
##      文件结尾再到里面找 `"键":`，于是"某个字段其实没写、但它的名字恰好在后面某个字面量里出现过"
##      就能蒙混过去。现在按花括号配平取出函数体，两个方向都只认这一段。
const SAVE_SERIALIZATION_TARGETS := [
	{
		"path": "res://src/core/game_state.gd",
		"exempt": ["migrated_from", "pruned_characters", "reclaimed_equipped"],
	},
	{"path": "res://src/core/inventory.gd", "exempt": []},
]
## 28. 数据字典（06）必须**提到**每张表的每个列。
##
## 由来（2026-10-03）：06 的「表清单行数」与「枚举」都有门限，但**列**一直没有——
## 而它正是策划查"这个字段叫什么、什么意思"的地方。实测确实漏了三处**功能性**列：
## `character_base` 的 `weapon_type`／`initial_wu`／`initial_gen`（0.6.0 加的两维）／`attr_total`／`start_equip_ids`、
## `equip_base` 的 `weapon_type`、`level_growth` 的 `base_def_qi`（见框架说明决策 172）。
##
## 判定**从宽**：只要求列名以 `` `列名` `` 的形式出现在该表那一节里（不查类型、不查说明文字对不对）——
## 这一节本来就有组合行（`` | `qty_min` / `qty_max` | int | … | ``）和分组行（`` | `bonus_…` … | ``）两种写法，
## 这条门限拦的是"字典里根本没提这个列"。
## 白名单只放**给人看的说明列**（不驱动逻辑、字典不逐列登记）；白名单外的新列必须登记。
const DOC_DICTIONARY_PROSE_COLUMNS := ["desc", "note", "name_cn", "icon", "display_color"]
## 36. 「只打印一个 OK 标记」的验证脚本也要有检查条数下限（删掉几条检查没人会红）。
##
## 由来（2026-10-03）：单元自检有 `MIN_ASSERTIONS` 这条棘轮（删断言当场红），
## 而**场景自检与两个验收脚本一条都没有**——偏偏它们是「版式预算（`LAYOUT`／`CONTENT`）、
## 真实节点接线、装配真的进战斗、跨场景交接、地图位点可达」这类**只有把真场景跑起来才量得到**
## 的东西的唯一覆盖。把某行 `ok = ok and …`（或某个 `_problems.append(…)`）删掉，
## `SELF-TEST: OK`／`LOOPCHECK: OK`／`MAPCHECK: OK` 照样打印，验收照样 16/16。
##
## 记的是**当前条数**（棘轮，不是目标）：删一条就红，加了新检查就往上调——
## 和 `MIN_ASSERTIONS` 一样，它证明不了"检查有效"，只保证**没人能悄悄把检查删掉**。
## 运行期错误导致的假绿不归它管：那条由 `tools\check_log_errors.bat` 扫 `SCRIPT ERROR` 兜着。
## 每条都写明"这个 pattern 代表什么"；`SELF-TEST: %s` 那句会被扫到，
## **新加一个带自检的场景必须在表里登记一行**（数量对账会拦）。
## 余量上限（同 `MIN_ASSERTIONS` 的 `SLACK_LIMIT`）：实际条数比棘轮表高出这个数就红，
## 提示"加了检查就把 floor 贴上去"——不然那张表会慢慢退化成摆设（2026-10-04 实测漂了 1～11 条）。
const VERIFICATION_FLOOR_SLACK := 2
const VERIFICATION_CHECK_FLOORS := {
	"src/ui/battle_screen.gd": {"pattern": "ok = ok and", "floor": 22, "why": "场景自检的聚合断言"},
	"src/ui/shop_screen.gd": {"pattern": "ok = ok and", "floor": 12, "why": "场景自检的聚合断言"},
	"src/ui/waypoint_screen.gd": {"pattern": "ok = ok and", "floor": 11, "why": "场景自检的聚合断言"},
	"src/ui/dungeon_screen.gd": {"pattern": "ok = ok and", "floor": 11, "why": "场景自检的聚合断言"},
	"src/ui/character_screen.gd": {"pattern": "ok = ok and", "floor": 13, "why": "场景自检的聚合断言"},
	"src/ui/cultivate_screen.gd": {"pattern": "ok = ok and", "floor": 11, "why": "场景自检的聚合断言"},
	"src/ui/clue_screen.gd": {"pattern": "ok = ok and", "floor": 8, "why": "场景自检的聚合断言"},
	"src/ui/main_menu.gd": {"pattern": "ok = ok and", "floor": 7, "why": "场景自检的聚合断言"},
	"src/world/overworld_controller.gd": {"pattern": "ok = ok and", "floor": 17, "why": "场景自检的聚合断言（2026-10-04 决策 337 加了「观察点可见标记」一条）"},
	"src/world/local_map_controller.gd": {"pattern": "ok = ok and", "floor": 14, "why": "场景自检的聚合断言（2026-10-04 决策 337 加了「观察点可见标记」一条）"},
	"src/bootstrap.gd": {"pattern": "ok = ok and", "floor": 6, "why": "配置表诊断场景自检的聚合断言"},
	"src/ui/creation_screen.gd": {"pattern": "ok = ok and", "floor": 15, "why": "创建角色场景自检的聚合断言"},
	"src/ui/npc_panel.gd": {"pattern": "ok = ok and", "floor": 20, "why": "NPC 面板自检的聚合断言"},
	"tools/check_loop.gd": {"pattern": "ok = ok and", "floor": 15, "why": "跨场景闭环每段一个聚合断言（③ 现在有胜／败两次）"},
	"tools/mapgen/verify_maps.gd": {"pattern": "_problems.append(", "floor": 39, "why": "每个地图检查点都必须能报错（2026-10-04 决策 332 给「按 id 绑的 NPC 位点」加了 2 条：id 不存在／人不在本图）"},
}
## 58. 设计文档里「某张表 N 行／N 件／N 个」的声称，抽成一张小表逐条对账。
##
## 由来（2026-10-04）：350／351 各修一处之后，把**同一手法**（能算的数就现算，绝不手抄）
## 扩大到别的设计文档，一次又抓到**四处**——而且全在地编／美术照着排期的那两份上：
##
##   · 15 §4.2 补「NPC 头像 **数量 7**（`npc_def`）」——0.29.0 加了沈雁回，`npc_def` 已经 **8** 行
##     （15 §4.3 与美术交接单早就是 8，只有这一格没改）；
##   · 07 §8.5「`equip_base` 目前没有 `icon` 列，**24 件**装备」——实际 **27** 行；
##   · 07 §8.2 标题「按 `enemy_base.csv` **14 行**」——练功木桩／石隙猎犬／游方弟子 加进来后是 **17** 行；
##   · 20 号 §十二「`dialogue_node`／`dialogue_option`（各 **1／3** 行）」——已经长到 **8／16**。
##
## 老问题一模一样：**表在长，数字留在原地**；而这四处写的是"要画多少张""有几个角色"，
## 是排期量。**没有一条门限看得见**（185 量看板、350 量交接单、351 量图标类目）。
##
## 口径：每条写死「文档 + 正则（只抓数，表名写在 pairs 里）+ 说明」；正则**抓不到也红**
## （措辞改了要连着改这条门限，别让它悄悄失效）。**带日期的叙事行不算**——行里出现
## 「当时／以前／当年」就跳过（同 `_looks_historical`，20 号那句「当时刚建骨架（各 1／3 行）」
## 记的是 0.29.0 的状态，不该拿现状口径去量）。
const ROW_COUNT_CLAIMS := [
	{
		"path": "res://docs/design/15_美术风格需求.md",
		"re": "\\|\\s*数量\\s*\\|\\s*(\\d+)\\s*（\\s*`npc_def`",
		"pairs": [[1, "npc_def"]],
		"why": "15 §4.2 补「NPC 头像 数量 N（`npc_def`）」",
	},
	{
		"path": "res://docs/design/07_地图资源需求.md",
		"re": "`equip_base`[^\\n]{0,40}?，\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*件装备",
		"pairs": [[1, "equip_base"]],
		"why": "07 §8.5「`equip_base` …N 件装备」",
	},
	{
		"path": "res://docs/design/07_地图资源需求.md",
		"re": "按\\s*`enemy_base\\.csv`\\s*(\\d+)\\s*行",
		"pairs": [[1, "enemy_base"]],
		"why": "07 §8.2 标题「按 `enemy_base.csv` N 行」",
	},
	{
		"path": "res://docs/design/20_第一章剧情.md",
		"re": "各\\s*\\*{0,2}(\\d+)／(\\d+)\\*{0,2}\\s*行",
		"pairs": [[1, "dialogue_node"], [2, "dialogue_option"]],
		"why": "20 号 §十二「`dialogue_node`／`dialogue_option` 各 N／M 行」",
	},
	{
		"path": "res://docs/design/交接单_美术.md",
		"re": "NPC 头像\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*张",
		"pairs": [[1, "npc_def"]],
		"why": "美术交接单第 10 行「NPC 头像 N 张」",
	},
]
## 61. 设计文档里点名的「表.列／表.行」必须真在表里——**改列名最容易把这种指针写死**。
##
## 由来（2026-10-04）：0.14.0 把 `npc_guard.peace_condition` 拆成三列，那时设计文档里
## 那些**带点号**的引用就成了指向不存在的东西的指针（读的人会去找一个没有的列）。
## 这一轮做「同名列互盖」扫查时顺手把它量了：`docs/design/*.md`（CHANGELOG 除外——那是带日期的
## 叙事）里带点号的引用共 **135 处**，**134 处**是真列名或真行值，只剩一处是**计划中还没落的行**
## （20 号 §九 写的 `npc_quest.nq_yan_01`）。
##
## 口径：只认 `\b<表名>.<标识符>` 这种**带点号**的写法。设计写"将来才有的列"本来就不该带点
## （写「给 `event_check` 加一列 `hp_cost`」，别写 `event_check.hp_cost`），所以这条不会误伤计划。
## `csv` 后缀（`item_base.csv`）不算引用；计划中的行写进 `PENDING_DOC_REFS`，**双向维护**。
const PENDING_DOC_REFS := [
	## 20 号 §九 那张支线表要新增的行（表还没落，见 `待策划确认.md` Q72／Q73）
	"npc_quest.nq_yan_01",
]
## 8. `src/` 下的脚本与 `scenes/` 下的场景都必须有人引用（和「孤儿用例」「孤儿设计文档」同一类）。
##
## 判定「被引用」的三种方式：路径字符串出现在别处（`preload`／`load`／场景的 ext_resource／
## autoload 配置）、或者它的 `class_name` 在别的文件里被用到。
## 现在 91 个脚本 + `scenes/` 下 18 个场景全部有引用（外加根目录那 1 个白名单空场景，见 8b）；
## 以后谁写了脚本忘了接上，这里会当场红。
## 真要留一个「暂时没人用」的东西，把它加进 INFRASTRUCTURE_SCRIPTS 并写明理由。
const INFRASTRUCTURE_SCRIPTS := []
## 8b. 项目**根目录**下的 `.tscn` 也要有主。
##
## 这条是**用 MCP 列场景时撞出来的**：MCP 的 `list_scenes` 报 19 个场景，而 `scenes/` 下只有 18 个——
## 多出来的是根目录的 `node_2d.tscn`（一个空 `Node2D`，103 字节，谁都不引用）。
## 原因是上面的孤儿扫描只收 `scenes/`：**只要有人把场景存到项目根，它就能永远躲过检查**。
## 而用 MCP 的 `add_node` + `save_scene` 联调时，默认落点恰好就是 `res://node_2d.tscn`。
##
## 继承来的那个空场景不是本轮写的，删它要动 Git 里已有的文件，所以先登记在白名单里等确认；
## 白名单自身也会烂（文件被删了名单还留着），所以顺手断言它仍然存在。
const ROOT_SCENE_WHITELIST := ["node_2d.tscn"]
## 43. 用例里不许出现「没给种子的随机源」。
##
## `_check_battle_tests_are_seeded` 只管"造战斗界面"那 12 行窗口；别处（掉落、事件判定、词条、
## 明雷刷新……）要是写了 `RngServiceScript.new()`（无参 = 随机种子）或 `RandomNumberGenerator.new()`，
## 断言就会**时红时绿**——红起来查不出原因，绿起来也不算数（这类"噪声当门限"的坑，
## 项目在别处已经写过：`PERF` 为什么不做门限）。
## 检查口径：`tests/` 下这三个无参构造**一个都不许有**；要用固定种子（`RngServiceScript.new(SEED)`）。
## 真需要引擎自带 RNG 的极少数情况，就在下一行显式 `seed = …`，并把理由写进这里的白名单。
const UNSAFE_RNG_PATTERNS := ["RngServiceScript.new()", "RngService.new()", "RandomNumberGenerator.new()"]
## 40. 「现状类文档」里不许写死总命令的步数。
##
## 由来（2026-10-03）：`框架说明` 的工具清单里一边写着"docs 里不再写死步数，免得加一步就漏改一处"
## （还举了真漏过的例子），**同一段的上方却写着「当前 16」**；`地图搭建说明.md` 里也还留着
## 「15 步里的「map acceptance」」——而实际已经是 17 步。这两处都是"写下来那天是真的、之后没人改"。
## 政策已经写在文档里了，缺的是**有人盯着**：这条门限扫"现状类文档"（见下面的清单）里的
## `数字+步`，发现就红，并告诉怎么改（写成"看末尾 `passed steps`"）。
##
## 为什么 `框架说明.md` 不在清单里：它是**带日期的叙事**（"总命令 14 步 → 15 步""占一步（16 步）"），
## 那些句子记的是"那天发生了什么"，拿现状口径去量会假红（同 183 的 Q 范围、185 的看板数字）。
## 同理 `平衡观测.md`／`性能观测.md` 是快照报告，也不收。
const CURRENT_STATE_DOCS := [
	"AGENTS.md",
	"README.md",
	"docs/dev/当前状态.md",
	"docs/dev/地图搭建说明.md",
]
## 14. AGENTS.md 里那几条**仓库级硬约定**也要有门限，不然「约定」只是愿望。
##
## ① `src/` 不许直接读 `res://data/tables/`：CSV 是**构建期输入**，运行期只读 `data/generated/*.tres`
##    （直接读 CSV 会绕过校验、导出后也可能根本不在包里）。只认 `res://data/tables` 这个前缀，
##    `res://src/data/tables/...`（行类路径）不算——两者只差一个 `src/`。
## ② `tools/*.bat` 必须纯 ASCII + CRLF：cmd 用本地代码页解析，非 ASCII 变乱码；
##    LF-only 时 `call :label` 之类的标签查找会失效（这条真踩过）。
## ③ `tools/*.ps1` 只要含非 ASCII（中文注释）就必须带 UTF-8 BOM：PowerShell 5.1 会按 ANSI 读，
##    中文被拆坏后连语法都报错（`audit_table_usage.ps1` 踩过）。
##
## 白名单只有一处：`src/data/table_registry.gd` 里的 `TABLES_DIR := "res://data/tables"` ——
## **构建管线**（`tools/build_tables.gd`）就是靠它去读 CSV 的，那是它该干的事。
const CSV_DIR_WHITELIST := ["src/data/table_registry.gd"]
## 15. 06_配置表说明.md 里写的枚举，必须与代码/数据对得上。
##
## 为什么值得一条门限：06 是**策划配置时的依据**——照文档填一个代码不接受的值，构建期会直接报
## 「不是合法取值」，来回一趟才知道该填什么。这条门限诞生当天就抓到一个真坑：
## `event_check.reward_type` 文档写 7 种，代码 `ENUMS` 只剩 4 种（none／item／room／event），
## 于是「给判定配一条 equip 奖励」——06 允许、PS1 允许、`EventCheckService` 也实现了的写法——
## 会被自己的构建期校验器拒掉（见框架说明决策 87）。
##
## 解析两种写法（06 里都用）：① 表格行 `| \`列\` | 类型 | \`a\` / \`b\` … |`（值全是反引号字面量、
## 至少 2 个，单值多是「引用某张表」）；② 说明行 `… \`列\` 取值：\`a\` / \`b\` …`。
## 比三处：代码 `TableValidator.ENUMS`；主键列比表里的实际取值；`equip_base.slot` 因为**故意不写死**，
## 比 `equip_slot_def.slot_id` 的实际取值。
## 已知「文档过期」的几处写在 `DOC_ENUM_KNOWN_STALE` 里并写明理由，改一处删一行。
## **2026-10-03：四处都改完了（06 按代码与数据订正），白名单已清空**——现在文档与代码必须逐项一致，
## 再出现不一致会当场红（见框架说明决策 171）。这个空字典保留是因为下面的门限要读它的结构。
## 2026-10-03：这四处**都改完了**（06 按代码与数据订正），白名单清空——现在文档与代码必须逐项一致，
## 再出现不一致会当场红。留着这个空字典是因为门限的代码要读它（结构保留，条目归零）。
const DOC_ENUM_KNOWN_STALE := {}
## 16. 「同一事实只许有一处定义」——只收那些**漂移了不会报错、只会静默丢东西**的常量。
##
## 由头：`LOCKED_SCENES` 曾在两个文件里各写一份（注释还写着「同一口径」），谁改一边都不会红；
## 贡献 kind 字符串（attr_point／stat_flat）原先在 4 个文件里写死，而**写错一个字母就会被
## `AttributeCalculator` 的 `!=` 判断静默跳过**——面板上少一截加成，没有任何报错。
##
## 故意**不**收场景路径常量（`PLACEHOLDER_SCENE` 等 9 处）：路径写错会当场加载失败，是响的，
## 不值得为它加门限、更不值得为了门限去打包一堆 const。
const SINGLE_SOURCE_RULES := [
	{
		"label": "const LOCKED_SCENES",
		"owner": "src/core/world_map_service.gd",
		"also": [],
		"why": "哪些小地图本章不开放：大地图控制器必须引用它，不能各写一份（漂移了不会报错）",
	},
	{
		"label": "\"attr_point\"",
		"owner": "src/core/attribute_calculator.gd",
		"also": ["src/core/table_validator.gd"],
		"why": "贡献 kind 写错会被计算器静默跳过；table_validator 那处是 affix_pool.value_kind 的枚举（另一个轴）",
	},
	{
		"label": "\"stat_flat\"",
		"owner": "src/core/attribute_calculator.gd",
		"also": [],
		"why": "同上：贡献 kind 写错会被静默跳过",
	},
]
## 20. 「写了但游戏路径从不调用」的接口清单：钉成门限，只许少不许悄悄多。
##
## 由来：框架说明「API 使用审计」那一节记过一次人工审计——图鉴奖励就是栽在这里的
## （`codex_bonus()` 早写好了，全项目只有用例在调，游戏里收集再多也不涨属性）。
## 那次全项目扫出 22 个「只被测试调用」的接口并逐个看过；后来又加了不少代码，
## 所以这里把它做成**常驻门限**：每个 `func` 的名字在 `src/`＋`scenes/`＋`project.godot` 里
## 出现 ≤1 次（只有定义那行）＝游戏路径从不调用；出现在下面的白名单里才算通过。
## `_` 开头的一律跳过（引擎回调与私有 helper，与那次审计同一口径）。
## **新加的这种函数**会当场红：要么接进游戏路径，要么把名字与理由写进白名单（有意识的行为）。
const TEST_ONLY_API_ALLOWED := {
	# —— 0.7.0 数据层：buff／套装的服务类先落地并有用例钉着，**结算与六指令随后接** ——
	# （等 BattleActor/BattleSimulator/battle_screen 真正读它们时，把这些条目逐条删掉。）
	"polarity_of": "0.7.0 buff 数据层：结算未接（见 08 与框架说明决策 210）",
	"is_field_buff": "0.7.0 buff 数据层：战斗外增益的 HUD 显示未接",
	"grants_of_buff": "0.7.0 buff 数据层：发放时机未接",
	# —— 查询接口：游戏逻辑不需要，测试/界面按需读 ——
	"has_state": "查询", "has_status": "查询", "hp_ratio": "查询", "kill_style_of": "查询",
	"checks_for_region": "查询", "shop_state_of": "查询", "has_first_kill": "查询",
	"first_kill_count": "查询", "get_seed": "查询", "has_table": "查询",
	"has_value": "查询", "column_of": "查询", "get_id": "查询", "has_cap": "查询", "has_service": "查询",
	"buff_stacks": "查询", "buff_remaining": "查询",
	"exits": "查询", "is_unlimited": "查询", "focus_attrs": "查询", "round_text": "查询",
	"auto_running": "查询", "slot_button": "查询", "tier_id": "查询", "body_texture_path": "查询",
	"brazier_texture_path": "查询：用例验火盆用的是地编交付的那两张「灭／燃」贴图（07 §8.3）",
	"is_defined": "查询", "node_icon": "查询", "highlight_visible": "查询", "map_progress_text": "查询",
	"fog_cells": "查询", "facing_quadrant": "查询", "is_sneaking": "查询", "has_elite_glow": "查询",
	"weapon_type_name_of": "查询", "team_drop_groups": "查询",
	"threat_tags": "查询", "skills_from_enemy": "查询", "resolve_db": "查询",
	# —— 音效：播放记录只给用例看（游戏路径不需要知道"刚才放过什么"）——
	"played_events": "音效播放记录：用例读",
	"has_played": "同上",
	"clear_log": "同上（用例之间清记录）",
	"shutdown": "音效播放层收尾：--script 模式下没人回收挂在 root 上的节点（由 tools/run_tests.gd 调）",
	# —— 等设计补效果列/流程：接口留着，游戏里还没有调用方 ——
	"is_field_use": "等 item_base 效果列（05：两种回血道具要分开，使用未做）",
	"is_battle_use": "同上",
	"clear_dispellable": "等「驱散」来源（设计只标了可驱散，没给驱散技能）",
	"set_pity": "保底计数由存档读写，游戏路径不需要外部塞 —— 留给测试",
	"roll_group_many": "多组连掉的便利接口，游戏路径逐组调 roll_group",
	"set_strategy": "界面只用轮换（cycle_strategy）；直接设值留给测试",
	# —— 存档/工具接口：设计或流程上还没有调用方 ——
	"delete_slot": "设计文档里没有「删除存档」这一条",
	"has_any_save": "菜单逐个槽位检查，没用到这个汇总查询",
}
## 65. 「枚举三处同改」的第三处（`tools/validate_tables.ps1`）也得能看见——`ENUMS` 里每个枚举列，
## PS1 至少要提到那一列。
##
## 由来（2026-10-04）：`data/AGENTS.md` 写着「改枚举要三处一起改：06 数据字典／`table_validator.ENUMS`／
## PS1」，而 `ENUMS` 的 41 个枚举列里**有 7 列的列名在整个 PS1 里一次都没出现过**——也就是说
## **设计侧那道网根本不查它们**：`building_def.building_type`／`drop_table.roll_type`／
## `item_base.use_context`／`map_local.scene_type`／`map_region.node_type`／`talent_def.category`／
## `weapon_type_def.default_element`。后果不静默（构建期那道网会红），但**设计侧自己跑的时候看不见**，
## 而他们交接口径恰恰是「这条每次都是绿的」。这一轮把那 7 条补成了真检查。
##
## 口径：**只要求列名在 PS1 里出现**。这是条**名字级棘轮**，不是证明——所以规矩写在这里：
## 新加枚举列时，**要么给 PS1 补一条检查，要么写进 `PS1_ENUM_EXEMPT` 并说明为什么这边不查**。
const PS1_ENUM_EXEMPT := {
	# 今天没有豁免。示例：某列只有引擎 API 能算（PS1 是纯文本校验、没有引擎），就写进这里并说明。
}
## 「被要求的旗标」必须有人能点亮——把 Q51 那一族断链变成机器门限。
##
## 由来（三次都是人工踩出来的）：落雁坡那枚 flag_luoyanpo_met／毒堂那枚 flag_poison_hall／
## 荒村那枚 flag_huangcun_done 一度**一个来源都没有**，于是三位同伴永远入不了队；
## flag_qiutu_saved 也一样（钱大夫的委托永远交不了）。这类断链**表里看不出来**：
## 「要求」写在一处、「来源」在另一处或根本不存在，两边单独看都是合法的。
##
## 要求来自七处：recruit_def.join_condition／guide_step.condition／
## story_node.trigger_condition／chapter_def.complete_condition／
## dialogue_node.condition／dialogue_option.condition／npc_quest.requirement，
## 条件语言按 & 拆开后取 flag_*。
##
## 来源三类：
##   ① 表里——对话选项的 set_flag、event_check／hidden_trigger 的 reward_type=event 的
##      reward_id、以及 story_node(kind=chapter_end) 会置**本章**的 complete_condition；
##   ② 代码里——set_flag("flag…") 那种字面量，以及「const NAME := "flag…" 且 set_flag(NAME)
##      真的被调用过」的常量形式（GuideService 那几枚就是）；
##   ③ 格式化写法——set_flag("flag_%s_joined" % …) 这种当**模式**匹配（招募链）。
##
## **已知缺口**写在 KNOWN_FLAG_GAPS：补上就要把那行删掉，否则门限会红
## （「缺口清单过期」和「新断链」一样要有人管，与 PENDING_COST_CHECKS 同一套做法）。
const KNOWN_FLAG_GAPS := [
	## 落雁坡旧镖车位点还没摆（地编的活，见 20 号 §十二）
	"flag_luoyanpo_met",
	## 伪装混入（号衣＋腰牌）整套还没做——设计侧的 Q7 还开着
	"flag_bd_uniform",
]
## 20 号 §九 的十条支线表 ↔ npc_quest：**好感那一列必须对得上**。
##
## 为什么单挑这一列：它是那张表里**唯一能被机器读的数值**（＋20 那种）；
## 「奖励」那一列是散文（「草药汤 ＋ 旧信（燕小七线）」），机器比不了——散文那半由设计侧自己看。
##
## 2026-10-04 第一次跑就抓到两处对不上：陈氏 20→**25**、孙掌柜 10→**20**（且漏了草药汤），
## 都按**设计那张表**改了数据——设计文档是较新的口径，数据是早先落的。
##
## 例外写在 SIDEQUEST_FORCE_SKIP：**张贵**那条按设计是「图只可偷」（走 of_zhang_treasure），
## 本来就没有 npc_quest 行；招募那四条归 recruit_def，也不在这张表里。
const SIDEQUEST_FORCE_SKIP := ["张贵", "燕小七", "林铁山", "白清和", "苏九娘"]
## 51. 套装的**每一档**必须真的凑得出来——按「已经发得出来的来源」算。
##
## 由来（2026-10-04）：5.19（PS1 那条可达性审计）只查「单件／单部有没有来源」，查不出**组合**。
## `set_xuanwei_sword` 的「三招／五招」要 3／5 部玄微剑法，而 `sk_xuanwei_02／03／04` 三部全是
## `source_type=npc`（门派对话没实现）——**拿得到的只有起手与点星＝2 部**；
## `set_xuanwei_qi` 的「七格」要 7 点占格，而**拿得到的**三部只占 6 格（1＋2＋3，另两部也是 npc）。
## 表里配了、`buff_def` 也配了、代码也认，**玩家把能拿的全拿了也亮不起来**——
## 这类「这一档是死内容」以前没有任何地方会报：单件都是"有来源"的，只是凑不齐。
##
## 口径（与 `tools/validate_tables.ps1` 5.19b **同一条规则**，改一处要改两处）：
##   equip         来源按 5.19 那几路（掉落／货架／起始／事件与隐藏奖励／敌人身上／NPC 兑换／代码点名），
##                 并且**按槽位**算上限——同槽位最多 `equip_slot_def.max_equip` 件（戒指两枚）
##   skill_active   招式数 = 来源已实现的成员数（`SkillGrant.IMPLEMENTED_SOURCES` 是唯一真相）
##   skill_passive  格数 = 来源已实现的成员 `slot_cost` 之和
##
## 已知缺口写在 KNOWN_UNREACHABLE_SET_TIERS（**双向**维护：补上了就把那行删掉，
## 否则门限会红——「缺口清单过期」和「新断链」一样要有人管，与 KNOWN_FLAG_GAPS 同一套做法）。
const KNOWN_UNREACHABLE_SET_TIERS := [
	## 三招／五招：02／03／04 三部都是 source_type=npc（门派对话没实现，`待策划确认.md` Q3）
	"set_xuanwei_sword|3",
	"set_xuanwei_sword|5",
	## 七格：拿得到的只有引气（1 格，开局）＋周天（2 格，剧情）＋太清（3 格，剧情）＝6 格
	"set_xuanwei_qi|7",
]
## 53. **划掉的条目（`~~…~~`）正文里不许再留下「还没做」的字样。**
##
## 由来（2026-10-04，决策 338）：A3（精英 `marker_elite_red` 贴图）**图在 0.14.0 就交付、代码在决策 304
## 也接了线**（`roaming_enemy._make_elite_glow()` 按 `elite_marker` 的 id 拼路径加载），可两份清单里
## A3 那行**标题划掉了、正文还写着「代码侧仍是程序化光晕，换图属开发侧接线——需要时提一句就换」**——
## 读它的人会把一件做完的事当成待办。**「划掉」与「正文说没做」自相矛盾，而没有任何门限会红。**
##
## 口径：四份现状／清单文档里凡是**行内有划线**的，先把划线片段（`~~…~~`）摘掉再查「待办字样」——
## 旧措辞**划掉就放过**（那是明确撤回），留在正文里才算矛盾。词表写死在这里，加词要连着改这条注释。
const STRUCK_PENDING_WORDS := ["开发侧接线", "要开发侧", "需要时提一句", "还没接", "未接线", "仍是程序化"]
## 66. 单个 `.gd` 不许长成上帝类：**硬上限 800 行**（2026-10-04 用户拍板）。
##
## 依据在根 `AGENTS.md`「硬性约束」；存量超标清单与建议拆分轴在 `docs/dev/代码拆分清单.md`。
##
## 由来：`tests/`／`src/world/`／`src/ui/` 里长出一批 800～3800 行的文件，一个脚本扛好几类职责。
## 「一个脚本只担一类职责」**没有量得到的东西盯着**就只是口号——每个文件都是在"顺手再加一点"里
## 滚大的，从没人在某一次提交前停下来判断过"该拆了"（这次一次量出 15 个，最大的 3843 行）。
##
## **白名单就是那份清单本身**，不在代码里另抄一份：超限的文件必须能在清单「现状」表里找到一行，
## 登记了却已经拆到 800 行以下的也要红（提醒划掉那一行）。这样"同一份事实只许有一处定义"，
## 也不会出现"清单说 15 个、代码里写着 17 个"这种两边互相打脸的漂移。
##
## 行数口径与清单里量行数的那条命令一致（`Get-Content .Count`：**末尾换行不算一行**）。
const FILE_SIZE_LIMIT := 800
const FILE_SIZE_LIST_PATH := "res://docs/dev/代码拆分清单.md"




## 取出 `func to_dict()` 的**函数体**：从它后面的第一个 `{` 起，按花括号配平到归零。
##
## 为什么要精确到函数体（而不是"从 to_dict 截到文件结尾"）：那条门限是双向量键的，
## 用"后半段"当范围会让"没写进去的字段"被后面某个同名字面量蒙混过关；
## 也因为要问"写进去的键有没有人读"，而**读档代码就在同一个文件的后半段**，
## 拿"文件后半段"当范围等于把答案也算进问题里。
func _to_dict_body(source: String) -> String:
	var start := source.find("func to_dict")
	if start < 0:
		return ""
	var brace := source.find("{", start)
	if brace < 0:
		return ""
	var depth := 0
	for index in range(brace, source.length()):
		var ch := source[index]
		if ch == "{":
			depth += 1
		elif ch == "}":
			depth -= 1
			if depth == 0:
				return source.substr(brace, index - brace + 1)
	return ""




## `token` 是不是这张表的**列名**（CSV 表头）或**任何一行的值**（宽松口径：宁可漏一个笔误，
## 也不要因为设计文档里写了某个非主键的取值而假红——例：`drop_table.drop_chest_silver` 是"掉落组"的值，
## 不是那一行的主键）。
func _table_has_token(db, table_name: String, token: String) -> bool:
	if db.get_row(table_name, token) != null:
		return true
	var header := FileAccess.get_file_as_string("res://data/tables/%s.csv" % table_name).split("\n")
	if not header.is_empty():
		for column: String in header[0].split(","):
			if column.strip_edges().trim_prefix("\uFEFF") == token:
				return true
	for row: Resource in db.rows(table_name):
		for prop: Dictionary in row.get_property_list():
			if int(prop.get("usage", 0)) & PROPERTY_USAGE_SCRIPT_VARIABLE == 0:
				continue
			if str(row.get(str(prop.get("name")))) == token:
				return true
	return false




func _collect_files_with_suffix(dir: String, suffix: String, out: PackedStringArray) -> void:
	var handle := DirAccess.open("res://" + dir)
	if handle == null:
		return
	for file_name: String in handle.get_files():
		if file_name.ends_with(suffix):
			out.append(("%s/%s" % [dir, file_name]).trim_prefix("/"))
	for sub: String in handle.get_directories():
		if sub.begins_with("."):
			continue
		_collect_files_with_suffix(("%s/%s" % [dir, sub]).trim_prefix("/"), suffix, out)




## `.bat`：纯 ASCII + 全是 CRLF（有一处裸 LF 就点出来，附字节位置方便定位）
func _audit_bat(path: String) -> void:
	var bytes := _read_raw(path)
	if bytes.is_empty():
		fail("%s 读不到内容" % path)
		return
	var bare_lf := -1
	var non_ascii := -1
	for i in range(bytes.size()):
		if bytes[i] == 10 and (i == 0 or bytes[i - 1] != 13):
			if bare_lf < 0:
				bare_lf = i
		if bytes[i] > 127 and non_ascii < 0:
			non_ascii = i
	check_eq(bare_lf, -1, "%s 第 %d 字节是裸 LF：cmd 下标签查找会失效，请转成 CRLF" % [path, bare_lf])
	check_eq(non_ascii, -1, "%s 第 %d 字节不是 ASCII：cmd 按本地代码页解析会变乱码" % [path, non_ascii])




## `.ps1`：含非 ASCII 就必须有 UTF-8 BOM
func _audit_ps1(path: String) -> void:
	var bytes := _read_raw(path)
	if bytes.is_empty():
		fail("%s 读不到内容" % path)
		return
	var has_bom := bytes.size() >= 3 and bytes[0] == 0xEF and bytes[1] == 0xBB and bytes[2] == 0xBF
	var has_non_ascii := false
	for byte in bytes:
		if byte > 127:
			has_non_ascii = true
			break
	check_true(
		not has_non_ascii or has_bom,
		"%s 含非 ASCII（中文）却没有 UTF-8 BOM：PowerShell 5.1 会按 ANSI 读坏它" % path,
	)




## 按字节读文件（编码检查要用原始字节，`get_file_as_string` 会先把编码理顺）
func _read_raw(path: String) -> PackedByteArray:
	var file := FileAccess.open("res://" + path, FileAccess.READ)
	if file == null:
		return PackedByteArray()
	return file.get_buffer(file.get_length())




## 从若干路径里扫出 `--xxx-selftest` 开关名（去重排序）。
## 路径可以是目录（递归收 .gd）也可以是单个文件——注意 `_collect_files()` 只认目录，
## 传文件进去会静默返回空（第一版就这么错，导致「runner 里 0 个开关」的假红）。
func _collect_flags(paths: Array) -> PackedStringArray:
	var regex := RegEx.new()
	regex.compile("--([a-z-]*selftest)")
	var files := PackedStringArray()
	for entry: String in paths:
		if not entry.get_extension().is_empty():
			files.append(entry)
		else:
			files.append_array(_collect_files([entry]))
	var found := PackedStringArray()
	for path: String in files:
		var text := FileAccess.get_file_as_string("res://" + path)
		for match: RegExMatch in regex.search_all(text):
			var flag := "--" + match.get_string(1)
			if not found.has(flag):
				found.append(flag)
	found.sort()
	return found




## 路径本身出现在别处，或它的 class_name 在别的文件里被用到
func _has_reference(corpus: String, path: String) -> bool:
	var text := FileAccess.get_file_as_string("res://" + path)
	if text.is_empty():
		return true   # 读不到就不在这里判（别的检查会报），免得误红
	var class_regex := RegEx.new()
	class_regex.compile("(?m)^class_name\\s+([A-Za-z_][A-Za-z0-9_]*)")
	var found := class_regex.search(text)
	if found != null:
		var class_name_id := found.get_string(1)
		var uses := 0
		var use_regex := RegEx.new()
		use_regex.compile("\\b%s\\b" % class_name_id)
		for match: RegExMatch in use_regex.search_all(corpus):
			uses += 1
		# 自己的定义算一次；别的文件里再用到就 > 1
		if uses > 1:
			return true
	return corpus.contains(path)




## 递归收集某几个目录下的文件（相对 res:// 的路径）
func _collect_files(dirs: Array) -> PackedStringArray:
	var out := PackedStringArray()
	for dir: String in dirs:
		_collect_dir(dir, out)
	return out




func _collect_dir(dir: String, out: PackedStringArray) -> void:
	var handle := DirAccess.open("res://" + dir)
	if handle == null:
		return
	for file_name: String in handle.get_files():
		if not TEXT_EXTENSIONS.has(file_name.get_extension().to_lower()):
			continue
		out.append("%s/%s" % [dir, file_name])
	for sub: String in handle.get_directories():
		if sub.begins_with("."):
			continue
		_collect_dir("%s/%s" % [dir, sub], out)




func _split(raw: String) -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in raw.split("|", false):
		var trimmed := part.strip_edges()
		if not trimmed.is_empty():
			out.append(trimmed)
	return out




## 读 CHANGELOG 顶部的「当前设计版本」。
func _current_design_version() -> String:
	var file := FileAccess.open(CHANGELOG_PATH, FileAccess.READ)
	if file == null:
		fail("读不到 %s" % CHANGELOG_PATH)
		return ""
	var regex := RegEx.new()
	regex.compile("当前设计版本[:：]\\s*\\*{0,2}([0-9]+\\.[0-9]+\\.[0-9]+)")
	var version := ""
	while not file.eof_reached():
		var line := file.get_line()
		var found := regex.search(line)
		if found != null:
			version = found.get_string(1)
			break
	file.close()
	return version




## 读 CSV（自动剥 BOM），返回 [{列名: 值}, ...]。
func _read_csv(path: String) -> Array:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		fail("读不到 %s" % path)
		return []
	var header := file.get_csv_line()
	if header.size() > 0 and header[0].begins_with("\ufeff"):
		header[0] = header[0].substr(1)
	var rows: Array = []
	while not file.eof_reached():
		var line := file.get_csv_line()
		if line.size() == 1 and line[0].strip_edges().is_empty():
			continue
		var row: Dictionary = {}
		for index in range(header.size()):
			row[header[index].strip_edges()] = line[index] if index < line.size() else ""
		rows.append(row)
	file.close()
	return rows




func _looks_historical(prefix: String) -> bool:
	return prefix.contains("以前") or prefix.contains("当时") or prefix.contains("当年")
