<#
变异探针·第三块（只读·手动工具，**慢**，不进验收）：改**代码里的常量字面量**，跑一整套自检，
看有没有测试会红——不红就说明这个值**没人钉**，改了没人会发现。

为什么值得单独有一块：153／154 两轮就是靠它抓出 11 个没人钉的手感常量（破绽增伤、拆招反涨、
逃跑上限、玩家架势基础值、认输线、四个交互距离…），其中最隐蔽的一条是
`test_poise` 拿**被测常量本身**算期望值（算式级同义反复）。数据侧的探针抓不到这类问题。

做法（每个 case）：
  ① 把原文件按字节备份到 `.logs\mutation_code_backup\`（**中断了也能手工还原**）；
  ② 只替换那一处 `const NAME := 值` 的字面量；
  ③ 跑 `res://tools/run_tests.gd`，看退出码与 `SELFCHECK: OK` 标记；
  ④ 无论结果如何都在 finally 里从内存恢复，并核对 SHA256。

为什么逐个跑：每个 case 都要重新解析源码，没法在同一个引擎进程里做完。
粗算一条 10~15 秒，**现在 79 条、实测约 19 分钟**（2026-10-04 实测：
13:40:30 起、13:59:48 收）——所以它是**手动、按需**跑的工具。

2026-10-04 补的两条护栏（那天真踩了）：**Godot 编辑器开着的时候会短暂 mmap 一批 `.gd`**，
此时写那一笔会抛「a file with a user-mapped section open」，最坏的情况是「文件改了、恢复不了」。
现在：① 动手前 `Test-Writable` 探一下，写不进去就记成 `[UNMEASURED]` 并让整轮**判失败**
（量不出来 ≠ 量过了）；② 改动与恢复都走 `Write-Bytes-Robust`（重试 + 临时文件覆盖兜底），
真恢复不了就**立刻停**并打印 `Copy-Item` 还原命令——那一份备份在 `.logs\mutation_code_backup\`。

用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\analysis_mutations_code.ps1
退出码：0 = 每个常量都被钉住；1 = 有常量改了没人发现（把结果写进用例或记进文档的"有意不钉"）。
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$godot = if ($env:GODOT_BIN) { $env:GODOT_BIN } else { 'F:\Godot_v4.7.1-stable_win64.exe\Godot_v4.7.1-stable_win64_console.exe' }
if (-not (Test-Path -LiteralPath $godot)) { throw "找不到 Godot：$godot（可用 GODOT_BIN 覆盖）" }

$logDir = Join-Path $root '.logs'
$backupDir = Join-Path $logDir 'mutation_code_backup'
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

# file / find / replace。find 一律写成完整的 `const NAME := 值`，避免误伤同名的其它行。
$cases = @(
    @{ Name = 'POISE_BREAK_DAMAGE_BONUS 0.5->0.7'; File = 'src\core\battle_simulator.gd'; Find = 'const POISE_BREAK_DAMAGE_BONUS := 0.5'; Repl = 'const POISE_BREAK_DAMAGE_BONUS := 0.7' },
    @{ Name = 'PARRY_DAMAGE_MULT 0.5->0.7'; File = 'src\core\battle_simulator.gd'; Find = 'const PARRY_DAMAGE_MULT := 0.5'; Repl = 'const PARRY_DAMAGE_MULT := 0.7' },
    @{ Name = 'PARRY_POISE_GAIN 0.15->0.25'; File = 'src\core\battle_simulator.gd'; Find = 'const PARRY_POISE_GAIN := 0.15'; Repl = 'const PARRY_POISE_GAIN := 0.25' },
    @{ Name = 'FLEE_BASE_CHANCE 0.5->0.3'; File = 'src\core\battle_simulator.gd'; Find = 'const FLEE_BASE_CHANCE := 0.5'; Repl = 'const FLEE_BASE_CHANCE := 0.3' },
    @{ Name = 'FLEE_MIN_CHANCE 0.25->0.4'; File = 'src\core\battle_simulator.gd'; Find = 'const FLEE_MIN_CHANCE := 0.25'; Repl = 'const FLEE_MIN_CHANCE := 0.4' },
    @{ Name = 'FLEE_MAX_CHANCE 0.95->0.85'; File = 'src\core\battle_simulator.gd'; Find = 'const FLEE_MAX_CHANCE := 0.95'; Repl = 'const FLEE_MAX_CHANCE := 0.85' },
    @{ Name = 'MAX_ROUNDS 50->10'; File = 'src\core\battle_simulator.gd'; Find = 'const MAX_ROUNDS := 50'; Repl = 'const MAX_ROUNDS := 10' },
    @{ Name = 'PLAYER_POISE_BASE 40->60'; File = 'src\core\battle_actor.gd'; Find = 'const PLAYER_POISE_BASE := 40.0'; Repl = 'const PLAYER_POISE_BASE := 60.0' },
    @{ Name = 'PLAYER_POISE_PER_LEVEL 8->12'; File = 'src\core\battle_actor.gd'; Find = 'const PLAYER_POISE_PER_LEVEL := 8.0'; Repl = 'const PLAYER_POISE_PER_LEVEL := 12.0' },
    @{ Name = 'BURN_DEF_DOWN_PER_STACK 0.05->0.15'; File = 'src\core\battle_actor.gd'; Find = 'const BURN_DEF_DOWN_PER_STACK := 0.05'; Repl = 'const BURN_DEF_DOWN_PER_STACK := 0.15' },
    @{ Name = 'INTERNAL_QI_REGEN_FACTOR 0.5->0.8'; File = 'src\core\battle_actor.gd'; Find = 'const INTERNAL_QI_REGEN_FACTOR := 0.5'; Repl = 'const INTERNAL_QI_REGEN_FACTOR := 0.8' },
    @{ Name = 'SNEAK_RATIO 0.6->0.5'; File = 'src\world\player_controller.gd'; Find = 'const SNEAK_RATIO := 0.6'; Repl = 'const SNEAK_RATIO := 0.5' },
    @{ Name = 'SOFT_BASE 0.5->0.7'; File = 'src\core\event_check_service.gd'; Find = 'const SOFT_BASE := 0.5'; Repl = 'const SOFT_BASE := 0.7' },
    @{ Name = 'SOFT_PER_POINT 0.1->0.2'; File = 'src\core\event_check_service.gd'; Find = 'const SOFT_PER_POINT := 0.1'; Repl = 'const SOFT_PER_POINT := 0.2' },
    @{ Name = 'SOFT_MIN 0.05->0.2'; File = 'src\core\event_check_service.gd'; Find = 'const SOFT_MIN := 0.05'; Repl = 'const SOFT_MIN := 0.2' },
    @{ Name = 'HIT_CHANCE_MIN 0.05->0.15'; File = 'src\core\damage_resolver.gd'; Find = 'const HIT_CHANCE_MIN := 0.05'; Repl = 'const HIT_CHANCE_MIN := 0.15' },
    @{ Name = '明雷接触距离 22->60'; File = 'src\world\roaming_enemy.gd'; Find = 'const CONTACT_DISTANCE := 22.0'; Repl = 'const CONTACT_DISTANCE := 60.0' },
    @{ Name = '门户交互距离 26->60'; File = 'src\world\overworld_controller.gd'; Find = 'const PORTAL_DISTANCE := 26.0'; Repl = 'const PORTAL_DISTANCE := 60.0' },
    @{ Name = '地标揭开距离 96->48'; File = 'src\world\overworld_controller.gd'; Find = 'const DISCOVER_DISTANCE := 96.0'; Repl = 'const DISCOVER_DISTANCE := 48.0' },
    @{ Name = '出图武装边距 8->1'; File = 'src\world\overworld_controller.gd'; Find = 'const PORTAL_ARM_MARGIN := 8.0'; Repl = 'const PORTAL_ARM_MARGIN := 1.0' }
    # 2026-10-03 补：这不是"手感值"而是**功能清单**——打错一个字母，那扇门就静默不锁了
    # （构建期 `_check_locked_scenes_exist` 会点名"锁了个不存在的场景"，见框架说明决策 182）。
    @{ Name = 'LOCKED_SCENES 打错一个字母'; File = 'src\core\world_map_service.gd'; Find = 'const LOCKED_SCENES := ["scene_ferry_locked"]'; Repl = 'const LOCKED_SCENES := ["scene_ferry_lockd"]' },
    # 2026-10-03 补（音频）：事件表指向不存在的文件——契约测试（test_audio）要能抓住「文件没了／改名打错」
    @{ Name = '音效事件指向不存在的文件'; File = 'src\audio\sfx.gd'; Find = '"hit": "res://assets/audio/sfx/sfx_hit.wav"'; Repl = '"hit": "res://assets/audio/sfx/sfx_hit_missing.wav"' },
    # 2026-10-03 补：原来只扫「手感值」，这一轮把「设计写死的值」与「门限阈值」也扫一遍
    #   （阈值类的意义：**放松门限**和改坏手感一样会静默——没人会红）；
    #   注意：DamageResolver 那几个 *_DEFAULT（combat_const 缺行时的兜底）**故意不扫**，
    #   见框架说明决策 155 的口径。
    @{ Name = 'MAX_PARTY 4->5'; File = 'src\core\game_state.gd'; Find = 'const MAX_PARTY := 4'; Repl = 'const MAX_PARTY := 5' },
    @{ Name = 'BACKDROP_MIN_ALPHA 0.9->0.5'; File = 'src\ui\layout_budget.gd'; Find = 'const BACKDROP_MIN_ALPHA := 0.9'; Repl = 'const BACKDROP_MIN_ALPHA := 0.5' },
    @{ Name = 'DESIGN_WIDTH 1152->1280'; File = 'src\ui\layout_budget.gd'; Find = 'const DESIGN_WIDTH := 1152'; Repl = 'const DESIGN_WIDTH := 1280' },
    @{ Name = 'SOFT_MAX 0.95->1.0'; File = 'src\core\event_check_service.gd'; Find = 'const SOFT_MAX := 0.95'; Repl = 'const SOFT_MAX := 1.0' },
    @{ Name = 'HIT_CHANCE_MAX 0.99->1.0'; File = 'src\core\damage_resolver.gd'; Find = 'const HIT_CHANCE_MAX := 0.99'; Repl = 'const HIT_CHANCE_MAX := 1.0' },
    @{ Name = 'BUYBACK_LIMIT 20->5'; File = 'src\core\shop_service.gd'; Find = 'const BUYBACK_LIMIT := 20'; Repl = 'const BUYBACK_LIMIT := 5' },
    @{ Name = 'DEFAULT_SLOT_COUNT 6->3'; File = 'src\core\save_store.gd'; Find = 'const DEFAULT_SLOT_COUNT := 6'; Repl = 'const DEFAULT_SLOT_COUNT := 3' },
    @{ Name = 'EVENT_SKILL_ATTR_DIVISOR 5->4'; File = 'src\core\character_sheet.gd'; Find = 'const EVENT_SKILL_ATTR_DIVISOR := 5'; Repl = 'const EVENT_SKILL_ATTR_DIVISOR := 4' },
    @{ Name = 'SETTLE_CARD_LINES 3->4'; File = 'src\ui\battle_screen.gd'; Find = 'const SETTLE_CARD_LINES := 3'; Repl = 'const SETTLE_CARD_LINES := 4' },
    # 2026-10-04 补：好感的**地板**。它决定「扣好感能不能真的扣成负数」（`npc_favor` 里陈氏初始 −10），
    #   放松/收紧它都会静默改变偷窃失手的代价手感——`test_dialogue` 钉了绝对值。
    @{ Name = 'FAVOR_MIN -10->-20'; File = 'src\core\npc_service.gd'; Find = 'const FAVOR_MIN := -10'; Repl = 'const FAVOR_MIN := -20' },
    # 2026-10-04 补：四张「决定内容发不发」的常量表（tools/AGENTS.md 的规矩：绝对值断言 ＋ case 两条都要）。
    #   改坏了都必须有用例红——不然「玩家该拿到的东西」会在没有任何提示的情况下消失。
    @{ Name = 'TEAM_WIN_FLAGS 缺一条'; File = 'src\ui\battle_screen.gd'; Find = '"team_butcher": "flag_huangcun_done"'; Repl = '"team_butcher": "flag_huangcun_done_x"' },
    @{ Name = 'SUCCESS_FLAGS 缺一条'; File = 'src\core\event_check_service.gd'; Find = 'const SUCCESS_FLAGS := {"ev_cell_heal": "flag_qiutu_saved"}'; Repl = 'const SUCCESS_FLAGS := {"ev_cell_heal": "flag_qiutu_saved_x"}' },
    @{ Name = 'JOIN_REWARDS 少一件东西'; File = 'src\core\recruit_service.gd'; Find = '"ch_ci": {"items": ["item_potion_qi"], "favor": 20},'; Repl = '"ch_ci": {"items": [], "favor": 20},' },
    @{ Name = 'EXTRA_FAVOR 数值改 0'; File = 'src\core\npc_service.gd'; Find = '"nq_sun_01": [{"npc_id": "ch_ci", "value": 10}],'; Repl = '"nq_sun_01": [{"npc_id": "ch_ci", "value": 0}],' },
    # 2026-10-04 补：终局难题（账册三选一）那两张「决定内容发不发」的表，规矩同上。
    #   账册发不出去 → 那三条对话选项连出现的机会都没有（条件里有 `item:item_bd_ledger`）；
    #   回图不摆题 → 三个 `flag_ledger_*` 又变回没有来源的悬空旗标。两条都必须有用例红。
    @{ Name = 'TEAM_WIN_ITEMS 改成别的物品'; File = 'src\ui\battle_screen.gd'; Find = 'const TEAM_WIN_ITEMS := {"team_boss": "item_bd_ledger"}'; Repl = 'const TEAM_WIN_ITEMS := {"team_boss": "item_herb"}' },
    @{ Name = 'TEAM_WIN_DIALOGUES 缺一条'; File = 'src\world\local_map_controller.gd'; Find = 'const TEAM_WIN_DIALOGUES := {"team_boss": "dl_ledger_choice"}'; Repl = 'const TEAM_WIN_DIALOGUES := {"team_butcher": "dl_ledger_choice"}' },
    # `EVENT_DIALOGUES` 同一条规矩：判定过了接不接那段对话，只有这张表说了算。
    #   把它挂到别的判定上 → 地牢那次判定通过之后玩家什么也听不到（用例在 test_local_map）。
    @{ Name = 'EVENT_DIALOGUES 改成别的判定'; File = 'src\world\local_map_controller.gd'; Find = 'const EVENT_DIALOGUES := {"ev_shen_rescue": "dl_shen_cell"}'; Repl = 'const EVENT_DIALOGUES := {"ev_shed_trap": "dl_shen_cell"}' },
    # 序幕择念（20 §3.1）也挂在一张常量上：改坏了 → 读告示板不再问「你为什么要接这桩事」。
    # 2026-10-04：那条常量搬到 `DialogueService.OPENING_NODE`（控制器改引用它，见 0.32.0 的
    # `flag_open_*`——同一处要按节点 id 判「这是不是序幕那一问」），case 跟着挪到新文件。
    @{ Name = 'OPENING_NODE 改成别的节点'; File = 'src\core\dialogue_service.gd'; Find = 'const OPENING_NODE := "dl_opening_choice"'; Repl = 'const OPENING_NODE := "dl_open_li"' },
    # ---- 2026-10-03 第二次全扫的产物（已收口）：被抓住的留在表里当回归； ----
    # ---- 有意不钉的移进 `$intentionallyUnpinned`（附理由），不再参与跑测。 ----
    @{ Name = 'SIDE_ALLY 0->1'; File = 'src\core\battle_actor.gd'; Find = 'const SIDE_ALLY := 0'; Repl = 'const SIDE_ALLY := 1' },
    @{ Name = 'SIDE_ENEMY 1->2'; File = 'src\core\battle_actor.gd'; Find = 'const SIDE_ENEMY := 1'; Repl = 'const SIDE_ENEMY := 2' },
    @{ Name = 'POISE_REGEN_DEFAULT 0.1->0.12'; File = 'src\core\battle_actor.gd'; Find = 'const POISE_REGEN_DEFAULT := 0.1'; Repl = 'const POISE_REGEN_DEFAULT := 0.12' },
    @{ Name = 'POISE_REGEN_BOSS 0.3->0.36'; File = 'src\core\battle_actor.gd'; Find = 'const POISE_REGEN_BOSS := 0.3'; Repl = 'const POISE_REGEN_BOSS := 0.36' },
    @{ Name = 'BASIC_ATTACK_POWER 1.0->1.2'; File = 'src\core\battle_simulator.gd'; Find = 'const BASIC_ATTACK_POWER := 1.0'; Repl = 'const BASIC_ATTACK_POWER := 1.2' },
    @{ Name = 'BASIC_ATTACK_POISE 8->9'; File = 'src\core\battle_simulator.gd'; Find = 'const BASIC_ATTACK_POISE := 8'; Repl = 'const BASIC_ATTACK_POISE := 9' },
    @{ Name = 'DEFEND_POISE_GAIN 0.15->0.18'; File = 'src\core\battle_simulator.gd'; Find = 'const DEFEND_POISE_GAIN := 0.15'; Repl = 'const DEFEND_POISE_GAIN := 0.18' },
    @{ Name = 'POLARITY_BUFF 0->1'; File = 'src\core\buff_service.gd'; Find = 'const POLARITY_BUFF := 0'; Repl = 'const POLARITY_BUFF := 1' },
    @{ Name = 'POLARITY_DEBUFF 1->2'; File = 'src\core\buff_service.gd'; Find = 'const POLARITY_DEBUFF := 1'; Repl = 'const POLARITY_DEBUFF := 2' },
    @{ Name = 'DURATION_PERMANENT 0->1'; File = 'src\core\buff_service.gd'; Find = 'const DURATION_PERMANENT := 0'; Repl = 'const DURATION_PERMANENT := 1' },
    @{ Name = 'DEFAULT_CRIT_DMG 0.5->0.6'; File = 'src\core\damage_resolver.gd'; Find = 'const DEFAULT_CRIT_DMG := 0.5'; Repl = 'const DEFAULT_CRIT_DMG := 0.6' },
    # encounter.gd 的 SIDE_* 现在直接取自 BattleActor（单一出处），那两条 case 随之删除——
    # 它们已经不存在于源码里，留着只会报「case 失效」。
    # 存档版本：改坏了必须有用例红（`test_save_migration` 那套按版本分支）。
    # 这条以前写死在 13，版本升到 15 之后 `Find` 就找不到了——探针会把它报成
    # `case 失效`（不是"没人钉"），所以升版本时要顺手改这一行。
    @{ Name = 'VERSION 15->14'; File = 'src\core\game_state.gd'; Find = 'const VERSION := 15'; Repl = 'const VERSION := 14' },
    @{ Name = 'VERSION 1->2'; File = 'src\core\pity_tracker.gd'; Find = 'const VERSION := 1'; Repl = 'const VERSION := 2' },
    @{ Name = 'TILE 32.0->38.4'; File = 'src\core\world_map_service.gd'; Find = 'const TILE := 32.0'; Repl = 'const TILE := 38.4' },
    @{ Name = 'AUTO_INTERVAL 0.8->0.96'; File = 'src\ui\battle_screen.gd'; Find = 'const AUTO_INTERVAL := 0.8'; Repl = 'const AUTO_INTERVAL := 0.96' },
    @{ Name = 'SELFTEST_SEED 20261003->20261004'; File = 'src\ui\battle_screen.gd'; Find = 'const SELFTEST_SEED := 20261003'; Repl = 'const SELFTEST_SEED := 20261004' },
    @{ Name = 'SETTLE_DROP_ITEMS 3->4'; File = 'src\ui\battle_screen.gd'; Find = 'const SETTLE_DROP_ITEMS := 3'; Repl = 'const SETTLE_DROP_ITEMS := 4' },
    @{ Name = 'SETTLE_DROP_MAX_CHARS 48->49'; File = 'src\ui\battle_screen.gd'; Find = 'const SETTLE_DROP_MAX_CHARS := 48'; Repl = 'const SETTLE_DROP_MAX_CHARS := 49' },
    @{ Name = 'SWEEP_ANIM_SECONDS 0.6->0.72'; File = 'src\ui\dungeon_screen.gd'; Find = 'const SWEEP_ANIM_SECONDS := 0.6'; Repl = 'const SWEEP_ANIM_SECONDS := 0.72' },
    @{ Name = 'DESIGN_HEIGHT 648->649'; File = 'src\ui\layout_budget.gd'; Find = 'const DESIGN_HEIGHT := 648'; Repl = 'const DESIGN_HEIGHT := 649' },
    @{ Name = 'CONTACT_DISTANCE 26.0->31.2'; File = 'src\world\chest.gd'; Find = 'const CONTACT_DISTANCE := 26.0'; Repl = 'const CONTACT_DISTANCE := 31.2' },
    @{ Name = 'EXIT_DISTANCE 28.0->33.6'; File = 'src\world\local_map_controller.gd'; Find = 'const EXIT_DISTANCE := 28.0'; Repl = 'const EXIT_DISTANCE := 33.6' },
    @{ Name = 'NPC_DISTANCE 30.0->36'; File = 'src\world\local_map_controller.gd'; Find = 'const NPC_DISTANCE := 30.0'; Repl = 'const NPC_DISTANCE := 36' },
    @{ Name = 'BUILDING_DISTANCE 48.0->57.6'; File = 'src\world\local_map_controller.gd'; Find = 'const BUILDING_DISTANCE := 48.0'; Repl = 'const BUILDING_DISTANCE := 57.6' },
    @{ Name = 'EVENT_DISTANCE 40.0->48'; File = 'src\world\local_map_controller.gd'; Find = 'const EVENT_DISTANCE := 40.0'; Repl = 'const EVENT_DISTANCE := 48' },
    @{ Name = 'EXIT_ARM_MARGIN 8.0->9.6'; File = 'src\world\local_map_controller.gd'; Find = 'const EXIT_ARM_MARGIN := 8.0'; Repl = 'const EXIT_ARM_MARGIN := 9.6' },
    @{ Name = 'CONTACT_GRACE 0.5->0.6'; File = 'src\world\local_map_controller.gd'; Find = 'const CONTACT_GRACE := 0.5'; Repl = 'const CONTACT_GRACE := 0.6' },
    @{ Name = 'POST_DISTANCE 40.0->48'; File = 'src\world\overworld_controller.gd'; Find = 'const POST_DISTANCE := 40.0'; Repl = 'const POST_DISTANCE := 48' },
    @{ Name = 'EVENT_DISTANCE 40.0->48'; File = 'src\world\overworld_controller.gd'; Find = 'const EVENT_DISTANCE := 40.0'; Repl = 'const EVENT_DISTANCE := 48' },
    @{ Name = 'HIGHLIGHT_DISTANCE 70.0->84'; File = 'src\world\overworld_controller.gd'; Find = 'const HIGHLIGHT_DISTANCE := 70.0'; Repl = 'const HIGHLIGHT_DISTANCE := 84' },
    @{ Name = 'WALK_SPEED 180.0->216.0'; File = 'src\world\player_controller.gd'; Find = 'const WALK_SPEED := 180.0'; Repl = 'const WALK_SPEED := 216.0' },
    @{ Name = 'BLOCKING_LAYER_BIT 2->3'; File = 'src\world\player_controller.gd'; Find = 'const BLOCKING_LAYER_BIT := 2'; Repl = 'const BLOCKING_LAYER_BIT := 3' },
    @{ Name = 'TILE 32.0->38.4'; File = 'src\world\roaming_enemy.gd'; Find = 'const TILE := 32.0'; Repl = 'const TILE := 38.4' },
    @{ Name = 'CONTACT_DISTANCE 30.0->36'; File = 'src\world\trigger_point.gd'; Find = 'const CONTACT_DISTANCE := 30.0'; Repl = 'const CONTACT_DISTANCE := 36' },
    # 2026-10-03 深夜：0.9.0／0.10.0 新系统的常量也进探针（三条都已有绝对值断言，
    # 见 test_guide／test_local_map／test_recruit）——它们决定「引导推不推进」「谁在哪儿入队」
    @{ Name = 'START_CONDITION 改字'; File = 'src\core\guide_service.gd'; Find = 'const START_CONDITION := "start"'; Repl = 'const START_CONDITION := "begin"' },
    @{ Name = 'FLAG_BOARD_READ 改字'; File = 'src\world\local_map_controller.gd'; Find = 'const FLAG_BOARD_READ := "flag_board_read"'; Repl = 'const FLAG_BOARD_READ := "flag_board_read_x"' },
    @{ Name = 'REGION_JOIN_DISTANCE 70->140'; File = 'src\world\overworld_controller.gd'; Find = 'const REGION_JOIN_DISTANCE := HIGHLIGHT_DISTANCE'; Repl = 'const REGION_JOIN_DISTANCE := HIGHLIGHT_DISTANCE * 2.0' },
    # 宝箱守卫解决的旗标名（改字 → 文取白付：箱子永远解不开；真场景用例断言了字面量）
    @{ Name = 'peace_flag 改字'; File = 'src\core\guard_service.gd'; Find = 'return "flag_guard_%s_peace" % guard_id'; Repl = 'return "flag_guard_%s_piece" % guard_id' }
    # 木桩练习战（09 §3.3）：入口识别的 service_id 改字 → 按 E 不再进练习战（test_practice 钉了字面量）
    @{ Name = 'dummy service_id 改字'; File = 'src\core\practice_service.gd'; Find = 'const SERVICE_ID := "dummy_training"'; Repl = 'const SERVICE_ID := "dummy_trainning"' }
)

# 有意**不钉**的常量：跑一次完整的收口（2026-10-03 第二次全扫）之后，把「改了没人发现但不是缺陷」
# 的条目移到这里，附上理由。它们**不再参与跑测**（省时间），但条目本身留档，免得下次再当新洞追。
# 规矩（框架说明决策 154）：**决定事件发不发**的距离要钉值；**纯移动／表现**的速度与半径不钉。
$intentionallyUnpinned = @{
    'CONTACT_DISTANCE 26.0->31.2' = '宝箱的交互半径（走路手感）：用例把玩家摆在箱子上（距离 0），任何 ≥ 26 的半径都等价'
    'CONTACT_DISTANCE 30.0->36'   = '触发点的交互半径，同上'
    'EXIT_DISTANCE 28.0->33.6'    = '出口的触发半径：出口还有「先走出去一次才武装」那套逻辑顶着，半径大小只影响手感'
    'NPC_DISTANCE 30.0->36'       = 'NPC 对话半径（纯交互手感）'
    'BUILDING_DISTANCE 48.0->57.6' = '店铺交互半径（同上）'
    'EVENT_DISTANCE 40.0->48'     = '判定位点半径：用例把玩家正好摆在位点上，机制（按 E 才触发）已有断言，半径值只影响手感'
    'POST_DISTANCE 40.0->48'      = '驿站交互半径（同上）'
    'EXIT_ARM_MARGIN 8.0->9.6'    = '「先走出去一次」的边距：默认值小于接触距离，改大只会让武装更晚，不影响机制'
    'CONTACT_GRACE 0.5->0.6'      = '败北后的接触宽限期（秒）：用例不依赖具体秒数，只验「宽限期内不再开战」'
    'WALK_SPEED 180.0->216.0'     = '走路速度（纯手感，决策 154 明确不钉；0.15.0 由 128 提到 180）'
    'BLOCKING_LAYER_BIT 2->3'     = '物理层位：地图与玩家两边用同一个常量，改了自洽，不产生行为差异'
    'TILE 32.0->38.4'             = '像素↔格子换算（地图工具侧），改了只是画得更粗，逻辑仍自洽'
}

## 改一处字节。返回 'applied' / 'not-found' / 'locked' 三态——
## `not-found` 是 case 写错了（要报 [INVALID]），`locked` 是文件此刻写不进去
## （编辑器在扫；要记成「未测」，**不能**混进 [INVALID]，更不能装作跑过了）。
function Replace-Bytes {
    param([string]$Path, [string]$Find, [string]$Repl)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $f = [Text.Encoding]::UTF8.GetBytes($Find)
    $r = [Text.Encoding]::UTF8.GetBytes($Repl)
    $idx = -1
    for ($i = 0; $i -le $bytes.Length - $f.Length; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $f.Length; $j++) { if ($bytes[$i + $j] -ne $f[$j]) { $ok = $false; break } }
        if ($ok) { $idx = $i; break }
    }
    if ($idx -lt 0) { return 'not-found' }
    $out = New-Object System.Collections.Generic.List[byte]
    if ($idx -gt 0) { $out.AddRange([byte[]]$bytes[0..($idx - 1)]) }
    $out.AddRange($r)
    if ($idx + $f.Length -le $bytes.Length - 1) { $out.AddRange([byte[]]$bytes[($idx + $f.Length)..($bytes.Length - 1)]) }
    if (-not (Write-Bytes-Robust -Path $Path -Bytes $out.ToArray())) { return 'locked' }
    return 'applied'
}

# 用 .NET 直接算哈希，不用 `Get-FileHash`：从 cmd 里拉起的 PowerShell 可能取不到
# Microsoft.PowerShell.Utility（实测 `Get-FileHash : 无法识别`），而恢复校验是这套工具的安全底线。
function Get-Sha256 {
    param([string]$Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($Path)))
    }
    finally { $sha.Dispose() }
}

# 这个文件现在能不能写？
#
# 由来（2026-10-04 实测）：**Godot 编辑器开着的时候会把一批 `.gd` mmap 住**，此时写它会抛
# 「The requested operation cannot be performed on a file with a user-mapped section open」。
# 那天探针正是在 `finally` 恢复时撞上这条错——**脚本当场中断，文件留在改坏的状态**
# （好在 `Copy-Item` 那份备份还在，哈希核对确认没污染）。所以现在两条：
#   ① 动手前先测能不能写；不能写就**整条跳过并记成「未测」**（不许静默当通过——量不出来 ≠ 量过了）；
#   ② 恢复走 `Restore-File`：重试 + 兜底拷贝 + 事后核对哈希，真不行就**立刻停**并打印还原命令。
function Test-Writable {
    param([string]$Path)
    try {
        $fs = [IO.File]::Open($Path, 'Open', 'ReadWrite', 'None')
        $fs.Close()
        return $true
    }
    catch { return $false }
}

function Restore-File {
    param([string]$Path, [byte[]]$Bytes, [string]$BackupPath)
    if (Write-Bytes-Robust -Path $Path -Bytes $Bytes) { return $true }
    Write-Output ("  [FAIL] 恢复失败，文件还是改坏的那版：{0}" -f $Path)
    Write-Output ("         原版备份：{0}" -f $BackupPath)
    Write-Output ("         手工还原：Copy-Item -LiteralPath '{0}' -Destination '{1}' -Force" -f $BackupPath, $Path)
    return $false
}

# 写字节：先重试直写，再退到「写临时文件 + 覆盖改名」。
#
# 为什么要重试：Godot 编辑器**在扫描／导入时会短暂 mmap 一批 `.gd`**，写会抛
# 「a file with a user-mapped section open」。2026-10-04 实测连探针自己都撞了两回
# （一次在恢复、一次在改文件那一笔）——重试几百毫秒基本就过去了。
function Write-Bytes-Robust {
    param([string]$Path, [byte[]]$Bytes)
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            [IO.File]::WriteAllBytes($Path, $Bytes)
            return $true
        }
        catch { Start-Sleep -Milliseconds (200 * $attempt) }
    }
    try {
        # 兜底：先写临时文件再整个覆盖（有些占用只挡直接写句柄，挡不住改名覆盖）
        $tmp = "$Path.mutation_restore_tmp"
        [IO.File]::WriteAllBytes($tmp, $Bytes)
        Move-Item -LiteralPath $tmp -Destination $Path -Force
        return $true
    }
    catch { return $false }
}

# 跑一次引擎并拿到退出码。**不要**用 `& $godot ... 2>&1`：Godot 往 stderr 写的那行
# 「Failed to read the root certificate store.」会被 PowerShell 包成 NativeCommandError，
# 在 $ErrorActionPreference='Stop' 下直接中断整个脚本（踩过两次）。
# 也**不要**用 `Start-Process`：本机的环境块里同时有 `Path` 与 `PATH`，它会抛
# 「Item has already been added. Key in dictionary: 'Path'」（踩过一次）。
# 交给 cmd 去重定向两个流最稳，退出码原样回传。
function Invoke-Godot {
    param([string[]]$Arguments, [string]$OutFile, [string]$ErrFile)
    $argLine = ($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $line = '"' + $godot + '" ' + $argLine + ' > "' + $OutFile + '" 2> "' + $ErrFile + '"'
    & cmd /c $line
    return $LASTEXITCODE
}

# 先把表建一次，免得 .tres 是旧的
$null = Invoke-Godot -Arguments @('--headless', '--path', $root, '--log-file', (Join-Path $logDir 'mutation_code_build.log'), '--quit-after', '2000', '--script', 'res://tools/build_tables.gd') `
    -OutFile (Join-Path $logDir 'mutation_code_build_stdout.log') -ErrFile (Join-Path $logDir 'mutation_code_build_stderr.log')

$missed = @()
$invalid = @()
$unmeasured = @()
# 先自查那张「有意不钉」的表：每个 key 必须真的对应一个 case（否则 key 会悄悄烂掉，
# 变成一条谁都没在看的注释）。
$caseNames = @{}
foreach ($c in $cases) { $caseNames[$c.Name] = $true }
$orphanPinned = @($intentionallyUnpinned.Keys | Where-Object { -not $caseNames.ContainsKey($_) })
if ($orphanPinned.Count -gt 0) {
    Write-Output ("  [INVALID] 有意不钉表里有 {0} 条对不上 case：{1}" -f $orphanPinned.Count, ($orphanPinned -join ' / '))
    exit 1
}
foreach ($c in $cases) {
    if ($intentionallyUnpinned.ContainsKey($c.Name)) {
        Write-Output ("  [N/A]  {0}（有意不钉：{1}）" -f $c.Name, $intentionallyUnpinned[$c.Name])
        continue
    }
    $path = Join-Path $root $c.File
    $backupPath = Join-Path $backupDir ($c.File -replace '[\\/]', '_')
    # 写不了就别动手：动手之后恢复不了才是最坏的情况（见 Test-Writable 的注释）
    if (-not (Test-Writable $path)) {
        Write-Output ("  [UNMEASURED] {0}（{1} 现在写不进去——多半是 Godot 编辑器开着并 mmap 了它；关掉编辑器再跑）" -f $c.Name, $c.File)
        $unmeasured += $c.Name
        continue
    }
    $orig = [IO.File]::ReadAllBytes($path)
    $hashBefore = Get-Sha256 $path
    Copy-Item -LiteralPath $path -Destination $backupPath -Force
    $verdict = ''
    $mutated = $false
    $restored = $false
    try {
        switch (Replace-Bytes -Path $path -Find $c.Find -Repl $c.Repl) {
            'not-found' { $verdict = 'invalid' }
            'locked'    { $verdict = 'unmeasured' }
            'applied'   {
                $mutated = $true
                $code = Invoke-Godot -Arguments @('--headless', '--path', $root, '--log-file', (Join-Path $logDir 'mutation_code_case.log'), '--quit-after', '2000', '--script', 'res://tools/run_tests.gd') `
                    -OutFile (Join-Path $logDir 'mutation_code_stdout.log') -ErrFile (Join-Path $logDir 'mutation_code_stderr.log')
                $verdict = if ($code -eq 0) { 'missed' } else { 'caught' }
            }
        }
    }
    finally {
        # 只有真的改动了才需要恢复（'locked' 那一支文件根本没动过）
        if ($mutated) { $restored = Restore-File -Path $path -Bytes $orig -BackupPath $backupPath }
        else { $restored = $true }
    }
    # 没恢复回来就**立刻停**：树是脏的，继续跑别的 case 只会让污染更难查
    if (-not $restored) {
        Write-Output 'CODE MUTATIONS: FAILED（有一次恢复没成功，先按上面的命令还原再重跑）'
        exit 1
    }
    if ((Get-Sha256 $path) -ne $hashBefore) { $verdict = 'restore-failed' }
    switch ($verdict) {
        'caught'         { Write-Output ("  [OK]   {0}" -f $c.Name) }
        'missed'         { Write-Output ("  *** 没人钉 *** {0}" -f $c.Name); $missed += $c.Name }
        'invalid'        { Write-Output ("  [INVALID] {0}（找不到那一行，case 写错了）" -f $c.Name); $invalid += $c.Name }
        'restore-failed' { Write-Output ("  [FAIL] {0}：恢复后哈希不一致！" -f $c.Name); $invalid += $c.Name }
        'unmeasured'     {
            Write-Output ("  [UNMEASURED] {0}（{1} 这一刻写不进去——编辑器在扫／导入；关掉编辑器再跑）" -f $c.Name, $c.File)
            $unmeasured += $c.Name
        }
    }
}

Write-Output ("代码常量：{0} 个 case，没人钉 {1}，case 失效 {2}，没量到 {3}" -f $cases.Count, $missed.Count, $invalid.Count, $unmeasured.Count)
if ($unmeasured.Count -gt 0) { Write-Output ("CODE MUTATIONS UNMEASURED: " + ($unmeasured -join ', ')) }
if ($missed.Count -gt 0) { Write-Output ("CODE MUTATIONS MISSED: " + ($missed -join ', ')) }
if ($missed.Count -gt 0 -or $invalid.Count -gt 0 -or $unmeasured.Count -gt 0) {
    Write-Output 'CODE MUTATIONS: FAILED'
    exit 1
}
Write-Output 'CODE MUTATIONS: OK'
exit 0
