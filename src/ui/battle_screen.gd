## 战斗界面。
##
## 逐回合播放 `BattleSimulator`：可以「下一回合」手动推进，也可以自动战斗（1x/2x/4x 调速）。
## 打完之后结算掉落与经验（`BattleReward`），把明雷标成已清，再回大地图。
##
## 自检：`-- --battle-selftest`，自动打完并按退出码报告。
extends Control

const TableDbScript := preload("res://src/core/table_db.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const BattleActorScript := preload("res://src/core/battle_actor.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const BattleRewardScript := preload("res://src/core/battle_reward.gd")
const DropResolverScript := preload("res://src/core/drop_resolver.gd")
const PityTrackerScript := preload("res://src/core/pity_tracker.gd")
const EncounterScript := preload("res://src/core/encounter.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const SkillGrantScript := preload("res://src/core/skill_grant.gd")
const SettingsStoreScript := preload("res://src/core/settings_store.gd")
const NpcServiceScript := preload("res://src/core/npc_service.gd")
const AffixRollerScript := preload("res://src/core/affix_roller.gd")
const MasteryServiceScript := preload("res://src/core/mastery_service.gd")
const PracticeServiceScript := preload("res://src/core/practice_service.gd")
## 「打赢某支队伍 → 置哪枚旗标」：设计 20 §十一 里靠战斗收尾的那两个旗标来源。
## 表里还没有对应列（只给了口径），已记 `待策划确认.md` Q69；给列就挪进表、这里改成查表。
const TEAM_WIN_FLAGS := {
	"team_butcher": "flag_huangcun_done",
	"team_poison_hand": "flag_poison_hall",
	## 幕五「聚义厅对质」的进入条件（20 号 §十一：`flag_heifeng_confront` = 战大寨主后）
	"team_boss": "flag_heifeng_confront",
}
## 打赢某支队伍时随掉落一起到手、且**只领一次**的剧情物（20 号 §七：账册在幕五「对质」之后到手）。
##
## 为什么挂在掉落清单里：`BattleReward` 那条唯一入账口径会把 `drops` 里的条目写进「掉落：…」那一行，
## 玩家看得见「账册」是怎么来的，而**结算卡片的行数是钉住的**（多一行就顶出设计分辨率），
## 所以不另起一行战报。为什么按「背包里有没有」判唯一：账册是钥匙道具——不能丢（`is_key_item=1`）、
## 也不在任何货架上（卖不掉），所以「已持有」＝「领过了」，不必再记一份首杀簿。
const TEAM_WIN_ITEMS := {"team_boss": "item_bd_ledger"}
const SaveServiceScript := preload("res://src/core/save_service.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")
## 敌人剪影的路径与「有图才摆」（15 §六）——路径只在那一个文件里拼
const IconPathsScript := preload("res://src/ui/icon_paths.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const OverlayStackScript := preload("res://src/ui/overlay_stack.gd")
## 浮层（设计 14 §二：**战斗中按 Tab 开角色面板属于「浮层盖场景层」**）
const CHARACTER_SCENE := "res://scenes/character_screen.tscn"
const CLUE_SCENE := "res://scenes/clue_screen.tscn"
const DUNGEON_SCENE := "res://scenes/dungeon_screen.tscn"
const SfxScript := preload("res://src/audio/sfx.gd")

const WORLD_SCENE := "res://scenes/world_run.tscn"
const LOCAL_SCENE := "res://scenes/local_run.tscn"
const AUTO_INTERVAL := 0.8
const SPEED_STEPS := [1.0, 2.0, 4.0]
## 自检固定种子（与 tests/test_battle_ui.gd 用同一个）：命中／暴击／掉落都是掷出来的，
## 不固定时同一份代码会时红时绿——2026-10-03 实测过一次：`--battle-selftest` 的
## 「六指令」那行因为普攻落空而随机变红。
const SELFTEST_SEED := 20261003
## 结算卡片最多几行：多一行就顶出设计分辨率（版式预算靠 --battle-selftest 守着），
## 放不下的进战报。优先级：战果 → 掉落 → 升级 → 领悟 → 熟练度 → 暂缓规则。
const SETTLE_CARD_LINES := 3
## 结算卡片上「掉落」那一行最多列几件、最多多少字。
## 起因：卡片行数固定三行，但**每行都会换行**——章节 Boss 多件掉落 + 带词条装备时整页会顶到 663 > 648
## （`--battle-selftest` 的「长文案」一档量出来的）。超出的部分进战报，与「放不下的行进战报」同一口径。
const SETTLE_DROP_ITEMS := 3
const SETTLE_DROP_MAX_CHARS := 48

var state_override = null
## 遭遇覆盖（用例注入指定敌人队伍；正常游玩从 GameSession.pending_encounter 取）
var encounter_override = null
## 战斗修正覆盖（用例注入 force_* 开关；正常游玩为空）
var modifiers_override: Dictionary = {}
## 存档设施（用例可注入临时目录）
var save_store_override = null
## 设置存储（自动战斗开关）；自检注入临时目录
var settings_override = null
var return_handler := Callable()
## 随机种子：-1 = 随机（正常游玩）；自检与用例传固定值，让命中／掉落可复现
var rng_seed: int = -1

var db
var state
var encounter = null
var allies: Array = []
var enemies: Array = []
var sim = null

var settled := false
var auto_enabled := false
## 玩家点选的敌人（空 = 自动打血量最低的）
var selected_target_id: String = ""
var speed_index := 0
var auto_timer := 0.0

var _headline: Label
var _allies_box: VBoxContainer
var _enemies_box: VBoxContainer
var _round_label: Label
var _action_label: Label
var _log: RichTextLabel
var _skill_row: HBoxContainer
var _buff_panel: HFlowContainer
var _basic_button: Button
var _inner_button: Button
var _item_button: Button
var _defend_button: Button
## 招式行当前显示哪一组：skills（招式）／inner（内功列表）／items（道具列表）
var _panel_mode := "skills"
## 指令类按钮的公共开场日志（开新回合、推进到轮到我方）
var _command_lines: Array = []
var _result_label: Label
var _status: Label
var _next_button: Button
var _auto_button: Button
var _speed_button: Button
var _strategy_button: Button
var _flee_button: Button
var _return_button: Button
## 场景里的节点只绑定一次（setup() 可能被外部再调一次）
var _ui_bound := false
## 浮层栈（设计 18.1／14 §二）：战斗界面是**场景层**，角色面板／线索本／完成度压在上面
var _overlays = null
## 伤害跳字的浮层（不受卡片容器排版影响）
var _float_layer_node: Control = null


func _ready() -> void:
	# 种子要在 `setup()` 之前定：模拟器是在 setup 里用 rng_seed 造的
	if _has_user_arg("--battle-selftest"):
		rng_seed = SELFTEST_SEED
	setup()
	if _has_user_arg("--battle-selftest"):
		call_deferred("_run_battle_selftest")


# ------------------------------------------------------------------ 浮层（全局快捷键）

func overlays():
	if _overlays == null:
		_overlays = OverlayStackScript.new()
	return _overlays


## 全局快捷键（设计 14 §八）：**战斗中也能开角色／行囊／线索本／完成度**——
## 它们是「浮层盖场景层」，与地图上的行为一致；`Esc` 弹一层（战斗指令不受影响）。
func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		close_top_overlay()
		return
	if event.is_action_pressed("open_character"):
		open_overlay(CHARACTER_SCENE, "CharacterPanel", 0)
		return
	if event.is_action_pressed("open_bag"):
		open_overlay(CHARACTER_SCENE, "CharacterPanel", 2)
		return
	if event.is_action_pressed("show_clues"):
		open_overlay(CLUE_SCENE, "CluePanel", 0)
		return
	if event.is_action_pressed("show_progress"):
		open_overlay(DUNGEON_SCENE, "DungeonPanel", 0)


## 开一个浮层（同一个场景重复按 → 弹回它并刷新，不叠第二份）。
func open_overlay(scene_path: String, node_name: String, tab_index: int = 0) -> Dictionary:
	var existing := find_child(node_name, true, false)
	if existing != null:
		for closed_id: String in overlays().push(node_name):
			_free_overlay(closed_id)
		existing.state_override = state
		existing.refresh()
		if existing.has_method("select_tab"):
			existing.select_tab(tab_index)
		return {"ok": true, "reopened": true}
	var panel = load(scene_path).instantiate()
	if panel == null:
		return {"ok": false, "error": "浮层加载失败：%s" % scene_path}
	panel.name = node_name
	panel.state_override = state
	if panel.has_method("setup"):
		pass
	if node_name == "CluePanel":
		panel.scope = "region"
		panel.scene_id = str(encounter.source_scene)
	elif node_name == "DungeonPanel":
		panel.scene_id = str(encounter.source_scene)
	# 角色界面用的是 `back_handler`，其余三个面板用 `return_handler`（各自既有的口径）
	if node_name == "CharacterPanel":
		panel.back_handler = func() -> void: _close_overlay(node_name)
	else:
		panel.return_handler = func() -> void: _close_overlay(node_name)
	add_child(panel)
	panel.setup()
	if panel.has_method("select_tab"):
		panel.select_tab(tab_index)
	overlays().push(node_name)
	return {"ok": true, "reopened": false}


func close_top_overlay() -> bool:
	var top_id: String = overlays().pop()
	if top_id.is_empty():
		return false
	_free_overlay(top_id)
	return true


func _close_overlay(id: String) -> void:
	for extra: String in overlays().remove(id):
		_free_overlay(extra)
	_free_overlay(id)


func _free_overlay(id: String) -> void:
	var panel := find_child(id, true, false)
	if panel != null:
		panel.queue_free()


func open_overlay_count() -> int:
	return overlays().depth()


func setup() -> void:
	if sim != null:
		return
	db = _resolve_db()
	state = current_state()
	var session_node := _session_node()
	if state == null:
		# 自检或直接开战斗界面时没有会话状态：临时开一局（奖励会落在这份临时存档上）
		state = GameStateScript.new_game(db, "normal")
		if session_node != null:
			session_node.set_state(state)
	encounter = session_node.pending_encounter if session_node != null else null
	if encounter_override != null:
		encounter = encounter_override
	if encounter == null:
		encounter = _fallback_encounter()
	# 战斗外增益（08）：会话里还活着的带进战场，进战斗后维持整场
	var field_buffs: Array = session_node.active_field_buffs() if session_node != null else []
	allies = PartyBuilderScript.build_actors(db, state, field_buffs)
	enemies = _build_enemies(encounter)
	sim = BattleSimulatorScript.new(db, RngServiceScript.new(rng_seed))
	sim.setup(allies, enemies, {"encounter": encounter, "modifiers": modifiers_override.duplicate()})
	_bind_ui()
	# 自动战斗持久开关（设计 11 §四）：设置里开着，**进战斗就自动打**。
	# 玩家点任意指令立刻接管（`take_over()`）；这里只负责"进来就按设置走"。
	if bool(settings().load_auto_battle().get("enabled", false)):
		auto_enabled = true
		_auto_advance()
	_refresh()


## 设置存储：自检注入 `settings_override`（临时目录），正常运行落默认存档目录旁。
var _settings_store = null


func settings():
	if settings_override != null:
		return settings_override
	if _settings_store == null:
		_settings_store = SettingsStoreScript.new()
	return _settings_store


func current_state():
	if state_override != null:
		return state_override
	var session_node := _session_node()
	return session_node.state if session_node != null else null


## 存档设施：注入优先，否则用默认目录。与其他两个控制器同一套写法
## （保留一个实例，用例/自检也能查 `last_reason`，看清「什么时候存的」）。
var _save_service = null


func save_service():
	if _save_service == null:
		var store = save_store_override
		if store == null:
			store = SaveServiceScript.make_default()
		_save_service = SaveServiceScript.new(store, state)
	return _save_service


func status_text() -> String:
	return _status.text if _status != null else ""


func round_text() -> String:
	return _round_label.text if _round_label != null else ""


func finished() -> bool:
	return sim != null and sim.finished()


func auto_running() -> bool:
	return auto_enabled


func result_label_text() -> String:
	return _result_label.text if _result_label != null else ""


# ------------------------------------------------------------------ 操作

## 「玩家点任意指令立刻接管」（设计 11 §四的准话）。
##
## 自动战斗是省事不是夺权：只要玩家动一下任何指令，本场就退出自动，
## 让他把接下来的回合握在自己手里。所有手选指令与目标切换都要先过这里。
func take_over() -> bool:
	if not auto_enabled:
		return false
	auto_enabled = false
	_refresh_buttons()
	return true


func press_next_round() -> Array:
	if sim == null or sim.finished():
		return []
	# 语义：推进到「轮到我方行动」为止（敌方自动跑），这样才能手选招式；
	# 「自动战斗」才是一次打到底。
	var lines: Array = []
	if not sim.in_round():
		sim.begin_round()
		lines.append("—— 第 %d 回合 ——" % sim.rounds_played())
		lines.append_array(_intent_lines())
	lines.append_array(_advance_until_ally())
	# 自动战斗：轮到我方时**也要自己出招**。
	# 以前这里只推回合，而 `_advance_until_ally()` 碰到我方就停——自动战斗会在第一个
	# 玩家回合永远卡住（`--battle-selftest` 的注释「曾经把自检挂死」说的就是这件事，
	# 但游戏里的「自动战斗」按钮一直踩这个坑：只有敌方回合会自动跑）。
	# 我方出招走 `sim.auto_act()`，和敌方同一条路：策略（保守／均衡／全力）在 `pick_skill` 里生效。
	if auto_enabled and sim.in_round() and not sim.finished():
		var actor = sim.current_actor()
		if actor != null and actor.side == BattleActorScript.SIDE_ALLY:
			lines.append_array(sim.auto_act())
			lines.append_array(_advance_until_ally())
	var events: Array = sim.take_events()
	_append_log(lines)
	_refresh()
	# 顺序很重要：先重建卡片，再挂跳字/反馈——反过来会被 _refresh() 清掉
	_spawn_floats(events)
	_lunge_attackers(events)
	if sim.finished():
		_settle()
	return lines


## 手选招式：轮到我方时点招式按钮，打血量最低的敌人
func press_skill(skill_id: String) -> Dictionary:
	if sim == null or sim.finished():
		return {"ok": false, "error": "战斗已结束"}
	take_over()
	var lines: Array = []
	if not sim.in_round():
		sim.begin_round()
		lines.append("—— 第 %d 回合 ——" % sim.rounds_played())
		lines.append_array(_intent_lines())
	lines.append_array(_advance_until_ally())
	var actor = sim.current_actor()
	if actor == null or actor.side != BattleActorScript.SIDE_ALLY:
		_append_log(lines)
		_refresh()
		return {"ok": false, "error": "现在不是我方行动"}
	var chosen: Resource = null
	for active: Resource in sim.available_skills(actor):
		if str(active.skill_id) == skill_id:
			chosen = active
			break
	if chosen == null:
		_set_status("这一招现在用不了（内力不足或武器不符）")
		return {"ok": false, "error": "招式不可用"}
	var target = _current_target()
	if target == null:
		return {"ok": false, "error": "没有可打的目标"}
	lines.append_array(sim.act(actor, chosen, target))
	var events: Array = sim.take_events()
	_append_log(lines)
	if sim.finished():
		_settle()
	_refresh()
	_spawn_floats(events)
	_lunge_attackers(events)
	return {"ok": true, "skill_id": skill_id, "target": target.actor_id}


## 拆招（设计 04 核心循环）：花掉这次行动读破某个敌人的预兆招式 —— 那一招伤害减半，
## 自己反涨架势。和出招互斥（都要花掉这一次行动）。
func press_parry(enemy_id: String) -> Dictionary:
	if sim == null or sim.finished():
		return {"ok": false, "error": "战斗已结束"}
	take_over()
	var lines: Array = []
	if not sim.in_round():
		sim.begin_round()
		lines.append("—— 第 %d 回合 ——" % sim.rounds_played())
		lines.append_array(_intent_lines())
	lines.append_array(_advance_until_ally())
	var actor = sim.current_actor()
	if actor == null or actor.side != BattleActorScript.SIDE_ALLY:
		_append_log(lines)
		_refresh()
		return {"ok": false, "error": "现在不是我方行动"}
	var target = null
	for enemy in enemies:
		if str(enemy.actor_id) == enemy_id:
			target = enemy
			break
	var result: Dictionary = sim.parry(actor, target)
	lines.append_array(Array(result.get("lines", [])))
	_append_log(lines)
	if bool(result["ok"]):
		_set_status("拆招：%s 读破 %s 的招（这一招伤害减半），架势 +%d" % [
			actor.display_name, target.display_name, int(result.get("poise_gain", 0)),
		])
	else:
		_set_status("拆不了招：%s" % str(result["error"]))
	if sim.finished():
		_settle()
	_refresh()
	return result


## 新回合的预兆播报（敌方出招前有一回合预兆，玩家据此决定出招还是拆招）
func _intent_lines() -> Array:
	var out: Array = []
	for line: String in sim.intent_lines():
		out.append("预兆：%s" % line)
	return out


## 敌方预兆**横带**（设计 14 §四 要点一：「预兆必须显眼——藏在角落等于这套机制没做，
## 它要占一整条横带」）。
##
## 这一条以前只贴在敌人卡片里、再往日志里写一行，玩家得在两处找；现在 `Action` 这个
## 整宽标签就是预兆带：一条一句，把这一回合**所有活着的敌人**要出的招并排写出来。
## 日志仍照常记（回看用），但**不再用它顶这一行**——两处显示同一件事没有必要。
func _refresh_intent_band() -> void:
	if _action_label == null:
		return
	if sim == null or sim.finished():
		_action_label.text = ""
		return
	var lines: PackedStringArray = sim.intent_lines()
	if lines.is_empty():
		_action_label.text = "敌方预兆：这一回合没有敌人要出招"
		return
	_action_label.text = "敌方预兆：%s" % "　｜　".join(lines)


## 六指令里那几条「花一次行动」的按钮共用这个开场：必要时开新回合、推进到轮到我方。
## 不是我方行动就把日志补上并返回 null（调用方只管报错，不再各写一遍开场）。
func _begin_round_for_command():
	_command_lines = []
	if sim == null or sim.finished():
		_set_status("战斗已结束")
		return null
	# 推进到轮到我方：可能这一回合正好在敌方行动后跑完——那就把回合末结算跑掉、开新回合继续
	# （最多 4 轮兜底，正常情况下一次就够）
	var guard := 0
	while guard < 4:
		guard += 1
		if not sim.in_round():
			sim.begin_round()
			if sim.finished():
				break
			_command_lines.append("—— 第 %d 回合 ——" % sim.rounds_played())
			_command_lines.append_array(_intent_lines())
		_command_lines.append_array(_advance_until_ally())
		var actor = sim.current_actor()
		if actor != null and actor.side == BattleActorScript.SIDE_ALLY:
			return actor
		if sim.finished() or actor != null:
			break
	_append_log(_command_lines)
	_refresh()
	_set_status("战斗已结束" if sim.finished() else "现在不是我方行动")
	return null


## 普通攻击（六指令之一）：不耗内力、不看武器与招式装配，打当前目标
func press_basic_attack() -> Dictionary:
	take_over()
	var actor = _begin_round_for_command()
	if actor == null:
		return {"ok": false, "error": "现在不是我方行动"}
	var target = _current_target()
	if target == null:
		return {"ok": false, "error": "没有可打的目标"}
	var lines: Array = _command_lines.duplicate()
	lines.append_array(sim.basic_attack(actor, target))
	var events: Array = sim.take_events()
	_append_log(lines)
	_set_status("%s 普通攻击 %s" % [actor.display_name, target.display_name])
	if sim.finished():
		_settle()
	_refresh()
	_spawn_floats(events)
	_lunge_attackers(events)
	return {"ok": true, "target": str(target.actor_id)}


## 主动防御（六指令之一）：花一次行动换一个防御姿态 buff（减伤、架势、下回合先手）
func press_defend() -> Dictionary:
	take_over()
	var actor = _begin_round_for_command()
	if actor == null:
		return {"ok": false, "error": "现在不是我方行动"}
	var result: Dictionary = sim.defend(actor)
	var lines: Array = _command_lines.duplicate()
	lines.append_array(Array(result.get("lines", [])))
	_append_log(lines)
	if bool(result["ok"]):
		_set_status("%s 进入防御姿态（本回合减伤、下回合先手，架势 +%d）" % [
			actor.display_name, int(result.get("poise_gain", 0)),
		])
	else:
		_set_status("防不了：%s" % str(result["error"]))
	if sim.finished():
		_settle()
	_refresh()
	return result


## 内功指令：把招式行切成「已装内功」列表（再点一次切回招式）
func press_inner_list() -> Dictionary:
	take_over()
	_panel_mode = "skills" if _panel_mode == "inner" else "inner"
	_refresh_skills()
	return {"ok": true, "mode": _panel_mode}


## 道具指令：把招式行切成「战斗中可用的道具」列表（效果列未配，条目会写明原因）
func press_item_list() -> Dictionary:
	take_over()
	_panel_mode = "skills" if _panel_mode == "items" else "items"
	_refresh_skills()
	return {"ok": true, "mode": _panel_mode}


## 催动某部内功：花一次行动换它的运功 buff（buff_grant trigger=on_cast）
func press_cast_passive(skill_id: String) -> Dictionary:
	take_over()
	var actor = _begin_round_for_command()
	if actor == null:
		return {"ok": false, "error": "现在不是我方行动"}
	var result: Dictionary = sim.cast_passive(actor, skill_id)
	var lines: Array = _command_lines.duplicate()
	lines.append_array(Array(result.get("lines", [])))
	_append_log(lines)
	if bool(result["ok"]):
		_set_status("%s 催动内功（运功 buff 已生效）" % actor.display_name)
	else:
		_set_status("催不动：%s" % str(result["error"]))
	_panel_mode = "skills"
	if sim.finished():
		_settle()
	_refresh()
	return result


## 当前轮到我方行动的人（没有就返回 null）
func _current_ally_actor():
	if sim == null or sim.finished() or not sim.in_round():
		return null
	var actor = sim.current_actor()
	if actor != null and actor.side == BattleActorScript.SIDE_ALLY:
		return actor
	return null


## 敌方自动跑，直到轮到我方（或本回合结束）
func _advance_until_ally() -> Array:
	var lines: Array = []
	var guard := 0
	while sim.in_round() and guard < 20:
		var actor = sim.current_actor()
		if actor == null or actor.side == BattleActorScript.SIDE_ALLY:
			break
		lines.append_array(sim.auto_act())
		guard += 1
	return lines


## 选中的敌人还活着就用它，否则退回「血量最低」
func _current_target():
	if not selected_target_id.is_empty():
		for enemy in enemies:
			if enemy.actor_id == selected_target_id and enemy.is_alive():
				return enemy
	return _lowest_hp_enemy()


func _lowest_hp_enemy():
	var best = null
	for enemy in enemies:
		if not enemy.is_alive():
			continue
		if best == null or enemy.hp < best.hp:
			best = enemy
	return best


## 点敌人卡片上的按钮选目标
func select_target(actor_id: String) -> void:
	take_over()
	selected_target_id = actor_id if selected_target_id != actor_id else ""
	var target = _current_target()
	_set_status("目标：%s" % (target.display_name if target != null else "无"))
	_refresh()


## 伤害跳字：读引擎事件，在目标卡片上飘一个数字（暴击金色、破绽红色）
func _spawn_floats(events: Array) -> void:
	# 多段招式一次会来好几条事件：受击反馈每个目标只放一次，不然几段缩放补间会互相打架
	var reacted := {}
	for event: Dictionary in events:
		var damage := int(event.get("damage", 0))
		if damage <= 0:
			continue
		var target_id := str(event["target"])
		var row := _find_actor_row(target_id)
		if row == null:
			continue
		if not reacted.has(target_id):
			reacted[target_id] = true
			_hit_feedback(row, event)
			# 音效位见设计 07 §8.4「伤害跳字」那一档：直伤按暴击/普通分两条，
			# 持续伤害（DoT）不走这一条——设计没给它的音效位，等音效侧定。
			if str(event.get("kind", "")) != "dot":
				SfxScript.play("crit" if bool(event.get("is_crit", false)) else "hit")
		var label := Label.new()
		# 段序号 0 保持旧名字（用例与脚本还在按 Float_<id> 找），后续段加后缀避免重名被引擎改名
		var hit_index := int(event.get("hit_index", 0))
		label.name = "Float_%s" % str(event["target"]) if hit_index == 0 else "Float_%s_%d" % [str(event["target"]), hit_index]
		label.text = "-%d" % damage
		# 游戏内浮字也吃两档（设计 15 §4.4，2026-10-04 Q82）：跟标题档 24——
		# 16 ＝ 12×1.33，像素字发虚，正是本轮取消 16 档要消灭的那种非整数倍。
		label.add_theme_font_size_override("font_size", 24)
		var color := Color("ffd24a") if bool(event.get("is_crit", false)) else Color(1, 1, 1, 0.95)
		if bool(event.get("was_broken", false)):
			color = Color("eb5757")
		# 持续伤害跳字用状态自己的颜色（中毒绿、灼伤橙、流血红、内伤紫），一眼能和直伤区分
		if str(event.get("kind", "")) == "dot":
			color = _dot_color(str(event.get("status_id", "")))
		label.add_theme_color_override("font_color", color)
		label.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.8))
		label.add_theme_constant_override("outline_size", 4)
		# 多段招式一次会产生好几个跳字：按段序号错开，不然数字叠在一起看不清
		# 跳字必须挂在**不受容器排版管**的浮层上：挂在卡片里的话，VBoxContainer 每次排版
		# 都会把它按到卡片左边（position 被顶掉），半个数字还会被滚动区裁掉——踩过。
		var layer := _float_layer()
		label.position = (row as Control).global_position - global_position \
			+ Vector2(210 + 26 * hit_index, 4 - 10 * hit_index)
		layer.add_child(label)
		# 卡片靠右时上面那个 +210 会把数字推出屏幕（满招式那档内容宽 1134，离 1152 只剩 18px），
		# 所以按浮层尺寸夹一下：宁可压回卡片上，也别让玩家看不见这一击打了多少。
		# `reset_size()` 让 Label 立刻按文字算出尺寸；浮层还没排版（headless／刚建）时 bounds 为 0，
		# `clamp_into` 会原样返回，不影响用例里对位置的断言。
		label.reset_size()
		label.position = LayoutBudgetScript.clamp_into(layer.size, label.position, label.size)
		var tween := create_tween()
		tween.set_parallel(true)
		tween.tween_property(label, "position:y", label.position.y - 26.0 - 6.0 * hit_index, 0.7)
		tween.tween_property(label, "modulate:a", 0.0, 0.7)
		tween.chain().tween_callback(label.queue_free)


## 跳字浮层：铺满整屏的普通 Control（不是容器），子节点位置由我们自己说了算，且在卡片之上
func _float_layer() -> Control:
	if _float_layer_node != null and is_instance_valid(_float_layer_node):
		_float_layer_node.move_to_front()
		return _float_layer_node
	_float_layer_node = Control.new()
	_float_layer_node.name = "FloatLayer"
	_float_layer_node.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_float_layer_node.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_float_layer_node)
	return _float_layer_node


## 某个状态的跳字颜色（表里 damage_type.display_color）
func _dot_color(status_id: String) -> Color:
	var status_row: Resource = db.get_row("status_effect", status_id) if not status_id.is_empty() else null
	if status_row == null:
		return Color("7fd35a")
	var type_row: Resource = db.get_row("damage_type", str(status_row.damage_type))
	return Color(str(type_row.display_color)) if type_row != null else Color("7fd35a")


## 受击反馈：命中闪一下（破绽再抖一下），濒死/阵亡由卡片颜色体现
func _hit_feedback(row: Node, event: Dictionary) -> void:
	if not (row is Control):
		return
	var item := row as Control
	item.pivot_offset = _card_pivot(item)
	# 受击分帧：先「挨了一下」被压扁再弹回来，配合红光；破绽那一下再补一段横向抖动
	item.modulate = Color(1.6, 0.8, 0.8, 1.0)
	item.scale = Vector2(0.94, 1.04)
	var flash := create_tween()
	flash.set_trans(Tween.TRANS_QUAD)
	flash.tween_property(item, "scale", Vector2.ONE, 0.16)
	flash.parallel().tween_property(item, "modulate", Color(1, 1, 1, 1), 0.18)
	if bool(event.get("was_broken", false)):
		var base: Vector2 = item.position
		var shake := create_tween()
		for offset in [Vector2(4, 0), Vector2(-4, 0), Vector2(3, 0), Vector2(0, 0)]:
			shake.tween_property(item, "position", base + offset, 0.05)


## 卡片缩放要绕中心：size 还没排版出来时退回一个按卡片宽高的估计值（320×40 的一半）
func _card_pivot(control: Control) -> Vector2:
	if control.size.x > 1.0 and control.size.y > 1.0:
		return control.size * 0.5
	return Vector2(160, 20)


func _find_actor_row(actor_id: String) -> Node:
	for box in [_allies_box, _enemies_box]:
		var row: Node = box.get_node_or_null("Actor_%s" % actor_id)
		if row != null:
			return row
	return null


func press_auto() -> void:
	if sim == null or sim.finished():
		return
	auto_enabled = true
	_auto_advance()


func press_stop_auto() -> void:
	auto_enabled = false
	_refresh_buttons()


func toggle_speed() -> float:
	speed_index = (speed_index + 1) % SPEED_STEPS.size()
	_refresh_buttons()
	return speed()


func speed() -> float:
	return float(SPEED_STEPS[speed_index])


## 自动战斗策略（设计 04：保守／均衡／全力）。只影响我方自动战斗，手动出招不受影响。
func press_strategy() -> String:
	if sim == null:
		return ""
	var strategy_id: String = sim.cycle_strategy()
	_set_status("自动战斗策略：%s" % sim.strategy_label())
	_refresh_buttons()
	return strategy_id


func strategy_text() -> String:
	return _strategy_button.text if _strategy_button != null else ""


func press_flee() -> Dictionary:
	take_over()
	if sim == null or sim.finished():
		return {"ok": false, "error": "战斗已结束"}
	var lines: Array = []
	if not sim.in_round():
		sim.begin_round()
		lines.append("—— 第 %d 回合 ——" % sim.rounds_played())
		lines.append_array(_intent_lines())
	lines.append_array(_advance_until_ally())
	var actor = sim.current_actor()
	if actor == null or actor.side != BattleActorScript.SIDE_ALLY:
		_append_log(lines)
		_refresh()
		return {"ok": false, "error": "现在不是我方行动"}
	var result: Dictionary = sim.flee(actor)
	lines.append_array(Array(result.get("lines", [])))
	_append_log(lines)
	if not bool(result["ok"]):
		_set_status("撤不了：%s" % str(result["error"]))
	elif bool(result["escaped"]):
		_set_status("撤退成功：脱离战斗（没有经验与掉落，明雷还在）")
	else:
		_set_status("撤退失败：被缠住了，白费一次行动（成功率 %.0f%%）" % (float(result["chance"]) * 100.0))
	if sim.finished():
		_settle()
	_refresh()
	return result


func press_return() -> void:
	if return_handler.is_valid():
		return_handler.call()
		return
	var tree := _tree()
	if tree != null:
		tree.change_scene_to_file(_return_scene())


## 切磋的镜像对手（Q88 拍板 ①）：把这位同伴**照他自己**造一个战斗单位，翻到敌方阵营。
##
## 用的是与他上场时**同一个** `PartyBuilder.build_actor()`——等级、加点、装备、内功、招式全同，
## 所以「赢的是他自己的镜像」这句话是真的，而不是另配一套数值。
##
## 两处刻意的收口：① **满血**（`refill()`）——同伴带伤不该让这一场变得更好打；
## ② **不给经验、钱、掉落**（同伴不是敌人，表里没有他的 `exp_reward`；设计 19 §2.2 里切磋的
## 收益就是好感那一条）。这两条若与设计口径不合，改这里一处即可。
func _build_mirror_enemy(char_id: String):
	var actor = PartyBuilderScript.build_actor(db, state, char_id)
	if actor == null:
		push_error("[battle] 切磋镜像造不出来（character_base 里没有这个同伴）")
		return null
	actor.side = BattleActorScript.SIDE_ENEMY
	actor.actor_id = "mirror_%s" % char_id
	actor.reward_exp = 0
	actor.reward_money = 0
	actor.drop_group = ""
	actor.tags["faction"] = "companion"
	actor.refill()
	return actor


## 打完回哪：大地图或来时的那个小地图
func _return_scene() -> String:
	var session_node := _session_node()
	if session_node != null and not str(session_node.pending_return_scene).is_empty():
		return str(session_node.pending_return_scene)
	return WORLD_SCENE


## 敌人在遭遇里可能带着 members（房间队伍、隐藏 Boss），否则按 team_id 建
func _build_enemies(encounter) -> Array:
	var factory = EnemyFactoryScript.new(db)
	# 同伴的切磋没有表里的队伍：对手是**他自己**（Q88 拍板 ①）——按人物现造一个镜像
	if not str(encounter.mirror_char).is_empty():
		var mirror = _build_mirror_enemy(str(encounter.mirror_char))
		return [mirror] if mirror != null else []
	var members := str(encounter.members)
	if members.is_empty():
		return factory.create_team(encounter.team_id, encounter.difficulty_id)
	var out: Array = []
	for part: String in members.split(";", false):
		var pieces := part.split(":", false)
		if pieces.is_empty():
			continue
		var enemy_id := pieces[0].strip_edges()
		var count := int(pieces[1]) if pieces.size() > 1 else 1
		for index in range(maxi(1, count)):
			var actor = factory.create(enemy_id, encounter.difficulty_id)
			if actor == null:
				continue
			actor.actor_id = "%s#%d" % [enemy_id, index + 1]
			out.append(actor)
	return out


func _process(delta: float) -> void:
	if not auto_enabled or sim == null or sim.finished():
		return
	auto_timer -= delta * speed()
	if auto_timer <= 0.0:
		auto_timer = AUTO_INTERVAL
		press_next_round()


func _auto_advance() -> void:
	auto_timer = 0.0


# ------------------------------------------------------------------ 结算

func _settle() -> void:
	if settled:
		return
	settled = true
	auto_enabled = false
	# 结算音：胜／败各一条（07 §8.4）。逃跑与平局的音效位设计没给，先不出声。
	if sim.winner() == BattleSimulatorScript.WINNER_ALLY:
		SfxScript.play("victory")
	elif sim.winner() == BattleSimulatorScript.WINNER_ENEMY:
		SfxScript.play("defeat")
	# 战斗结束清掉**战斗内** buff（设计 08：`battle` 作用域随战斗结束清除；`field` 战斗外增益
	# 由会话层按现实分钟管，不在这一步里动）。放在结算最前面：掉落与经验不受残留 buff 影响。
	for actor in allies + enemies:
		actor.clear_battle_buffs()
	var session_node := _session_node()
	var lines := PackedStringArray()
	if sim.winner() == BattleSimulatorScript.WINNER_ALLY:
		var drops: Array = []
		# 保底计数从存档读、打完写回（设计：保底跨战斗、跨难度继承）
		var pity = PityTrackerScript.new()
		if state != null:
			pity = state.pity_tracker()
		var resolver = DropResolverScript.new(db, RngServiceScript.new(rng_seed), pity)
		var bonus: float = DropResolverScript.party_drop_bonus(allies)
		for enemy in enemies:
			if enemy.is_alive():
				continue
			# 练习战（09 §3.3）：**掉落、首杀一律不算**——它的收益只有封顶的经验与熟练度，
			# 否则「打木桩刷装备/刷首杀」就把上山的意义抹掉了。
			if bool(encounter.practice):
				continue
			# 首杀记录进存档（GameState.first_kills）：同一掉落组只领一次，
			# 退出重进也不会再出首杀固定掉落。
			var drop_group := str(enemy.drop_group)
			var first_kill := false
			if state != null and not drop_group.is_empty():
				first_kill = state.mark_first_kill(drop_group)
			if not drop_group.is_empty():
				drops.append_array(resolver.roll_group(
					drop_group, encounter.difficulty_id,
					{"first_kill": first_kill, "drop_rate_bonus": bonus}
				))
			# 装备掉落：**身上穿的，就是能掉的**（设计 10 §五；0.14.0 起 `drop_table` 不再列装备）。
			# 首杀那一下由 `roll_enemy_equipment` 保一件（武器优先）。
			var enemy_id := str(enemy.source_id)
			if enemy_id.is_empty():
				enemy_id = str(enemy.actor_id).split("#")[0]
			drops.append_array(resolver.roll_enemy_equipment(enemy_id, first_kill))
		# 剧情物（20 号 §七 的账册）：**第一次**打赢这支队伍才进掉落清单，之后不再重复发。
		var story_item := _team_win_item_id()
		if not story_item.is_empty() and not _owns_item(story_item):
			drops.append({"item_id": story_item, "qty": 1})
		var result: Dictionary = sim.result()
		if bool(encounter.practice):
			# 经验封顶（`growth_const.dummy_xp_cap_level`）＋没有铜钱：改在结算前就地削，
			# 这样 `BattleReward` 那条唯一入账口径不用为练习战开分支。
			result["rewards"] = {
				"exp": PracticeServiceScript.capped_exp(db, state, int(result.get("rewards", {}).get("exp", 0))),
				"money": 0,
			}
		var applied: Dictionary = BattleRewardScript.apply(
			db, state, result, drops,
			AffixRollerScript.new(db, RngServiceScript.new(rng_seed))
		)
		if state != null:
			state.store_pity(pity)
		# 件数并进掉落明细：结算面板多一行就会顶出版式预算（自检会红），一行说清更省地方
		lines.append("胜利！经验 +%d　铜钱 +%d" % [int(applied["exp"]), int(applied["money"])])
		var drops_detail := _describe_drops(applied)
		var drops_card := _describe_drops_card(applied)
		lines.append("掉落：%s" % (drops_card if not drops_card.is_empty() else "无"))
		if drops_card != drops_detail:
			# 完整清单进战报：卡片那一行只列得下 SETTLE_DROP_ITEMS 件（见常量注释里的 663 > 648）
			lines.append("掉落明细：%s" % drops_detail)
		if not str(applied.get("level_up_text", "")).is_empty():
			lines.append("升级：%s" % str(applied["level_up_text"]))
		# 切磋（设计 19 §2.2）：赢了给这位 NPC 加好感。
		# 只在这一条路上加——切磋是「主动选的战斗」，输了不扣好感（19 §2.2 只写了赢的收益）。
		if not str(encounter.spar_npc).is_empty():
			var spar: Dictionary = NpcServiceScript.win_spar(db, state, str(encounter.spar_npc))
			if bool(spar.get("ok", false)):
				lines.append(str(spar.get("text", "")))
		# 击败领悟：skill_base 里 source_type=drop 且 source_id 指向本场敌人的武学
		var defeated_ids := PackedStringArray()
		for enemy in enemies:
			if enemy.is_alive():
				continue
			# 练习战不教武学：木桩不是「击败了谁」
			if bool(encounter.practice):
				continue
			var source := str(enemy.source_id)
			if source.is_empty():
				source = str(enemy.actor_id).split("#")[0]
			if not source.is_empty() and not defeated_ids.has(source):
				defeated_ids.append(source)
		var granted: Array = SkillGrantScript.grant_from_defeated(db, state, defeated_ids)
		lines.append_array(SkillGrantScript.summarize(granted))
		# 熟练度：这一场真正施放过的招式，每次施放 +mastery_combat_gain
		# （排在卡片前三行之外，进战报——它是慢成长细节，掉落与领悟更要紧）
		var mastery = MasteryServiceScript.new(db, state)
		for ally in allies:
			var usage: Dictionary = sim.skill_uses(str(ally.actor_id))
			if usage.is_empty():
				continue
			# 练习战的熟练度按 `growth_const.dummy_mastery_cap` 封顶（只压涨、不压低）
			var mastery_cap := PracticeServiceScript.mastery_cap(db) if bool(encounter.practice) else 0
			var mastered: Array = mastery.apply_combat_usage(str(ally.actor_id), usage, mastery_cap)
			if not mastered.is_empty():
				lines.append("熟练度：%s" % mastery.describe_usage(mastered))
		if bool(encounter.practice):
			lines.append("练习战：经验只算到 %d 级、熟练度只涨到 %d 级，无掉落与首杀" % [
				PracticeServiceScript.cap_level(db), PracticeServiceScript.mastery_cap(db),
			])
		if not encounter.pending_rules.is_empty():
			lines.append("暂缓规则：" + "；".join(encounter.pending_rules))
		# 练习战不写「清怪／房间」记录：它不是副本进度，也不进完成度
		if not bool(encounter.practice):
			_mark_spawn_cleared(session_node)
			_apply_team_win_flags()
	elif sim.winner() == BattleSimulatorScript.WINNER_FLEE:
		# 主动撤退：不算败北，也不结算经验／铜钱／掉落；明雷留在原地（设计 02：明雷看得见、可以绕开）
		lines.append("撤退：脱离了战斗（没有经验与掉落）")
	elif sim.winner() == BattleSimulatorScript.WINNER_DRAW:
		# 回合打满（MAX_ROUNDS=50）：谁也没赢。设计没写平局的后果，先按「零收获、明雷不清」处理，
		# 但**不许写「败北」**——那是假话（玩家没输，只是没打完）。口径记进交接表等设计定。
		lines.append("平局：回合打满了，双方都没有收获（明雷不会被清掉）")
	else:
		# 切磋落败是一个**例外**（Q88 之后策划定的口径，2026-10-04）：**不传送、原地站着**——
		# 陪练把人送回城很怪。气血回满与清战斗外增益照旧走败北结算（见 `_handle_defeat` 的
		# `stay_put`），只是不设回程，也不写「被送回」。好感本来就 +0（19 §2.2 输了不罚）。
		if not str(encounter.spar_npc).is_empty():
			lines.append("切磋落败，无好感（对方是自己人，不结怨）")
		else:
			# 战败处理（08 已定，0.8.0 口径）：全队回到**最近到过的出生点／城镇**、气血回满、
			# 战斗外增益全部清除；不扣铜钱不掉装备。平局不走这里（见上一分支）。
			var shelter := ""
			if session_node != null:
				shelter = str(session_node.last_shelter_name)
			lines.append("败北：全队被送回%s，气血已回满、战斗外增益已清除" % (
				"「%s」" % shelter if not shelter.is_empty() else "大地图"
			))
			lines.append("这一场没有收获，明雷不会被清掉")
	if session_node != null:
		session_node.last_battle = {
			"winner": sim.winner(),
			"rounds": sim.rounds_played(),
			"team": encounter.team_name,
			# 队伍 **id**（`team` 是中文名）：小地图靠它认「上一场打完的是哪支队伍」，
			# 再决定回图之后要不要摆出那一段对话（20 号 §七 的终局难题）。
			"team_id": str(encounter.team_id),
			"contact": encounter.contact_label(),
			"summary": "　".join(lines),
			# 击杀方式（毒杀判定用）：隐藏内容里的 trig_poison_kill 靠它判「是不是毒杀的」
			"kill_styles": sim.kill_styles(),
		}
		session_node.pending_encounter = null
	# 战斗外气血（v11）：不管是赢、撤还是败，都把这场的剩余气血写回存档
	_write_back_hp()
	# 战败处理要在写回气血**之后**做：`heal_all()` 清掉 char_hp 记录 = 全队满血，
	# 否则会被上面那份「打完剩 1 点」的记录盖回去。
	if sim.winner() == BattleSimulatorScript.WINNER_ENEMY:
		_handle_defeat(session_node, not str(encounter.spar_npc).is_empty())
	# 打完就自动存档（设计 02：副本内自动存档，退出重进不掉本层进度）
	var save_result: Dictionary = save_service().save("战斗结算", true)
	# **自动存档失败要在战报里说一句**：不然玩家以为这一场白打的进度都存住了，其实只在本局里。
	# 失败不打断结算（AGENTS：存档写入失败不允许挡住开局），但绝不能静默。
	if not bool(save_result.get("ok", false)):
		_append_log(["自动存档失败：%s（这一场的结果只在本局里）" % str(save_result.get("error", "未知原因"))])
	# 结算卡片最多三行（版式预算有限：多一行就顶出 648，自检会红），其余行进战报。
	# 优先级：战果 → 掉落 → 升级 → 领悟 → 熟练度 → 暂缓规则（顺序本身有断言钉着）。
	var card := lines.slice(0, mini(SETTLE_CARD_LINES, lines.size()))
	var rest := lines.slice(mini(SETTLE_CARD_LINES, lines.size()))
	if not rest.is_empty():
		_append_log(rest)
	_result_label.text = "\n".join(card)
	_refresh()
	_refresh_buttons()


## 把这一场打完的剩余气血写回存档（05 文档的医馆治疗要按「缺失气血」收费，得先有这个数）。
## 下限 1：倒下的角色回图时留 1 点，免得全队 0 血把存档卡死（设计没定败北代价，口径已记进交接表）。
func _write_back_hp() -> void:
	if state == null:
		return
	for ally in allies:
		var char_id := str(ally.actor_id)
		# 用例会把 actor_id 改成 scholar_1 之类，只有真角色 id 才写回
		if char_id.is_empty() or not state.char_ids.has(char_id):
			continue
		state.set_current_hp(char_id, maxi(1, ally.hp))


## 战败处理（08：回最近到过的出生点／城镇 ＋ 气血回满 ＋ 战斗外增益全部清除；
## 不扣铜钱、不掉装备——第一章不做死亡惩罚）。
##
## 只有真的败北才走这里：**平局留在原地**（设计原文），撤退也不回城。
## 回到哪张图交给会话：`pending_local_scene` + `pending_return_scene` 是既有的回程通道，
## 小地图控制器与大地图都读它们，所以这里不需要另开一条传送实现。
##
## `stay_put=true` 是**切磋**那一条（Q88 之后策划定的口径）：气血回满与清战斗外增益照旧，
## 但**不设回程**——陪练把人送回城很怪。
func _handle_defeat(session_node, stay_put: bool = false) -> void:
	if state != null:
		state.heal_all()
	if session_node == null:
		return
	session_node.clear_field_buffs()
	if stay_put:
		return
	var shelter := str(session_node.last_shelter_scene)
	if not shelter.is_empty():
		session_node.pending_local_scene = shelter
		session_node.pending_return_scene = LOCAL_SCENE


func _mark_spawn_cleared(session_node) -> void:
	if session_node == null:
		return
	if str(encounter.source_scene) == "overworld":
		var row: Resource = db.get_row("roaming_spawn", encounter.source_key)
		var respawn := int(row.respawn_sec) if row != null else 0
		# 0 = 不刷新（设计：精英长 CD 或不刷新；第一章普通明雷 30~60 秒）
		session_node.cleared_spawns[encounter.source_key] = (
			-1 if respawn <= 0 else int(Time.get_unix_time_from_system()) + respawn
		)
		return
	# 小地图房间：记在本张图的会话状态里（回大地图整片刷新）
	var scene_id := str(encounter.source_scene)
	if not session_node.local_maps.has(scene_id):
		session_node.local_maps[scene_id] = {"cleared": [], "chests": [], "triggers": []}
	var record: Array = session_node.local_maps[scene_id].get("cleared", [])
	if not record.has(encounter.source_key):
		record.append(encounter.source_key)
	session_node.local_maps[scene_id]["cleared"] = record
	# 完成度与「已通关层」是永久记录：打过的房间、被击败的紫名 Boss 都写进存档
	if state != null:
		state.record_dungeon(scene_id, "rooms", str(encounter.source_key))
		for enemy in enemies:
			if enemy.is_alive():
				continue
			if str(enemy.tags.get("threat_tag", "")) == "purple":
				state.record_dungeon(scene_id, "bosses", str(enemy.source_id))


## 打赢某支队伍 → 置旗标（设计 20 §十一 那三个「没有来源」的旗标里，靠战斗收尾的两个）。
##
## 荒村废屋的屠夫 → `flag_huangcun_done`（白清和招募的条件）；毒堂的毒手 → `flag_poison_hall`
## （苏九娘招募的条件）。**暂时写在这个常量里**：表里还没有「打赢某队置旗标」这一列
## （设计 20 §十一 只给了口径），已记 `待策划确认.md` Q69——设计给了列（例如
## `enemy_team.win_flag`）就挪进表，这一处跟着改成查表。
##
## 不往战报里加文案：旗标的可见反馈是**回到那张图时同伴入队**（`RecruitService`），
## 在这儿再编一句叙事反而会和设计后面的台词打架。
func _apply_team_win_flags() -> void:
	var team_id := str(encounter.team_id)
	if state == null or not TEAM_WIN_FLAGS.has(team_id):
		return
	state.set_flag(str(TEAM_WIN_FLAGS[team_id]))


## 这一场打赢之后该发的剧情物 id（没配就是空）
func _team_win_item_id() -> String:
	if encounter == null:
		return ""
	return str(TEAM_WIN_ITEMS.get(str(encounter.team_id), ""))


## 玩家手里有没有这件东西——`TEAM_WIN_ITEMS` 用它判「领过了没有」
func _owns_item(item_id: String) -> bool:
	return state != null and state.inventory != null and state.inventory.has(item_id)


# ------------------------------------------------------------------ 界面

## 界面骨架在 scenes/battle_screen.tscn 里（真实节点树，编辑器/MCP 可直接调版式与皮肤），
## 这里只做查找与接线。卡牌行、跳字仍是运行时生成的动态内容。
func _bind_ui() -> void:
	if _ui_bound:
		return
	_ui_bound = true
	set_anchors_preset(Control.PRESET_FULL_RECT)
	_headline = _require_node("Margin/Column/Headline") as Label
	_allies_box = _require_node("Margin/Column/Front/TeamAllies/AllyScroll/ListAllies") as VBoxContainer
	_enemies_box = _require_node("Margin/Column/Front/TeamEnemies/EnemyScroll/ListEnemies") as VBoxContainer
	_round_label = _require_node("Margin/Column/Round") as Label
	_action_label = _require_node("Margin/Column/Action") as Label
	_log = _require_node("Margin/Column/Log") as RichTextLabel
	_skill_row = _require_node("Margin/Column/SkillRow") as HBoxContainer
	_buff_panel = _require_node("Margin/Column/BuffPanel") as HFlowContainer
	_basic_button = _require_node("Margin/Column/CommandRow/BasicAttackButton") as Button
	_inner_button = _require_node("Margin/Column/CommandRow/InnerButton") as Button
	_item_button = _require_node("Margin/Column/CommandRow/ItemButton") as Button
	_defend_button = _require_node("Margin/Column/CommandRow/DefendButton") as Button
	_result_label = _require_node("Margin/Column/Result") as Label
	_status = _require_node("Margin/Column/Status") as Label
	_next_button = _require_node("Margin/Column/Buttons/NextRoundButton") as Button
	_auto_button = _require_node("Margin/Column/Buttons/AutoButton") as Button
	_speed_button = _require_node("Margin/Column/Buttons/SpeedButton") as Button
	_strategy_button = _require_node("Margin/Column/Buttons/StrategyButton") as Button
	_flee_button = _require_node("Margin/Column/Buttons/FleeButton") as Button
	_return_button = _require_node("Margin/Column/Buttons/ReturnButton") as Button
	_next_button.pressed.connect(press_next_round)
	_auto_button.pressed.connect(press_auto)
	_speed_button.pressed.connect(toggle_speed)
	_strategy_button.pressed.connect(press_strategy)
	_flee_button.pressed.connect(press_flee)
	_return_button.pressed.connect(press_return)
	_basic_button.pressed.connect(press_basic_attack)
	_inner_button.pressed.connect(press_inner_list)
	_item_button.pressed.connect(press_item_list)
	_defend_button.pressed.connect(press_defend)
	# 六指令的说明写在 meta 上（`_refresh_buttons()` 每次会按当前状态覆盖 tooltip）
	_basic_button.set_meta("hint", "六指令之一：不耗内力、不看武器，打当前目标")
	_inner_button.set_meta("hint", "六指令之一：查看已装内功，催动一次换运功 buff")
	_item_button.set_meta("hint", "六指令之一：战斗中可用的道具")
	_defend_button.set_meta("hint", "六指令之一：本回合减伤、架势回复、下回合先手")
	_flee_button.disabled = true
	_headline.text = encounter.headline()
	_log.clear()
	_action_label.text = ""
	_result_label.text = ""
	_status.text = ""


## 场景里少一个节点就应该炸得很响（而不是界面上静悄悄缺一块）
func _require_node(path: String) -> Node:
	var node := get_node_or_null(path)
	if node == null:
		push_error("battle_screen.tscn 缺少节点：%s" % path)
		assert(false, "battle_screen.tscn 缺少节点：%s" % path)
	return node


func _make_button(node_name: String, text: String, pressed: Callable, disabled: bool = false) -> Button:
	var button := Button.new()
	button.name = node_name
	button.text = text
	button.disabled = disabled
	button.pressed.connect(pressed)
	return button


func _refresh() -> void:
	_clear(_allies_box)
	_clear(_enemies_box)
	for actor in allies:
		_allies_box.add_child(_make_actor_row(actor))
	for actor in enemies:
		_enemies_box.add_child(_make_actor_row(actor))
	_round_label.text = "第 %d 回合　%s" % [
		maxi(sim.rounds_played(), 1),
		"已结束" if sim.finished() else "进行中",
	]
	_refresh_buttons()
	_refresh_skills()
	_refresh_buffs()
	_refresh_intent_band()


## 这一行按当前模式显示：招式（默认）／已装内功／战斗道具
func _refresh_skills() -> void:
	if _skill_row == null:
		return
	_clear(_skill_row)
	if sim == null or sim.finished():
		return
	if not sim.in_round():
		# 不在这里偷偷开回合（会让「下一回合」的计数对不上）：等玩家点「下一回合」或直接点招式
		var idle := Label.new()
		idle.name = "SkillHint"
		idle.text = "点「下一回合」开打，或直接点招式先手出招"
		_skill_row.add_child(idle)
		return
	var actor = sim.current_actor()
	if actor == null:
		return
	if _panel_mode == "inner" and actor.side == BattleActorScript.SIDE_ALLY:
		_add_inner_buttons(actor)
		return
	if _panel_mode == "items":
		_add_item_buttons()
		return
	var hint := Label.new()
	hint.name = "SkillHint"
	if actor.side == BattleActorScript.SIDE_ALLY:
		hint.text = "%s 行动：" % actor.display_name
	else:
		hint.text = "敌方行动中（点「下一回合」自动推进）"
	_skill_row.add_child(hint)
	if actor.side != BattleActorScript.SIDE_ALLY:
		return
	# 用 skill_options：冷却中的招式也要列出来（灰着写清还差几回合），不然玩家以为招式没了
	for option: Dictionary in sim.skill_options(actor):
		var skill_id := str(option["skill_id"])
		# 增益招式没有倍率与段数（它施放一次给自己上 buff），按钮上就直说「增益」
		var text := ""
		if bool(option.get("support", false)):
			text = "%s（内力 %d｜增益）" % [str(option["name"]), int(option["qi_cost"])]
		else:
			text = "%s（内力 %d｜倍率 %.1f%s）" % [
				str(option["name"]), int(option["qi_cost"]), float(option["power_ratio"]),
				"" if int(option["hit_count"]) <= 1 else "｜%d 段" % int(option["hit_count"]),
			]
		if not bool(option["ok"]):
			text += "　— %s" % str(option["reason"])
		_skill_row.add_child(_make_button(
			"SkillButton_%s" % skill_id, text, func() -> void: press_skill(skill_id),
			not bool(option["ok"]),
		))


## 内功列表（「内功」指令）：每部已装内功一个按钮，点一次催动。
## 内力消耗：`skill_passive` 没有 qi_cost 列——按 0 收并把这条缺口写在按钮上，不假装有数。
func _add_inner_buttons(actor) -> void:
	var options: Array = sim.passive_cast_options(actor)
	if options.is_empty():
		var hint := Label.new()
		hint.name = "InnerHint"
		hint.text = "没装内功（在角色面板装配）"
		_skill_row.add_child(hint)
		return
	for option: Dictionary in options:
		var skill_id := str(option["skill_id"])
		var buff_name := str(option.get("buff_name", ""))
		var text := "%s（占 %d 格" % [str(option["name"]), int(option.get("slot_cost", 0))]
		if not buff_name.is_empty():
			text += "｜催动→%s" % buff_name
		text += "｜内力消耗列未配）"
		if not bool(option["ok"]):
			text += "　— %s" % str(option["reason"])
		_skill_row.add_child(_make_button(
			"InnerButton_%s" % skill_id, text, func() -> void: press_cast_passive(skill_id),
			not bool(option["ok"]),
		))


## 道具列表（「道具」指令）：`use_context=battle` 的道具，数量与「为什么用不了」都写清楚
func _add_item_buttons() -> void:
	var inventory = null
	var state_node = current_state()
	if state_node != null:
		inventory = state_node.inventory
	var options: Array = sim.battle_item_options(inventory)
	if options.is_empty():
		var hint := Label.new()
		hint.name = "ItemHint"
		hint.text = "没有战斗道具"
		_skill_row.add_child(hint)
		return
	for option: Dictionary in options:
		var item_id := str(option["item_id"])
		var text := "%s ×%d（%s）　— %s" % [
			str(option["name"]), int(option.get("qty", 0)), str(option.get("desc", "")),
			str(option["reason"]),
		]
		_skill_row.add_child(_make_button(
			"ItemButton_%s" % item_id, text, func() -> void: press_item_list(), true,
		))


## 增益减益面板（08 UI 要求）：`status_effect` 与 `buff_def` **合并成一条列表**，
## 减益在前、按剩余回合升序；常驻／整场显示 ∞，可叠层的显示 ×N；悬浮给名称与效果。
func _refresh_buffs() -> void:
	if _buff_panel == null:
		return
	_clear(_buff_panel)
	# 先把**所有单位**的条目合成一条列表再排一次：08 的排序是全局的
	# （减益在前、同类按剩余回合升序）——各排各的会让敌方的减益被我方增益压到下面。
	var rows: Array = []
	for actor in allies + enemies:
		for row: Dictionary in actor.effect_rows(db):
			var entry: Dictionary = row.duplicate()
			entry["actor_id"] = str(actor.actor_id)
			entry["actor_name"] = str(actor.display_name)
			entry["side_mark"] = "我" if actor.side == BattleActorScript.SIDE_ALLY else "敌"
			rows.append(entry)
	for row: Dictionary in BattleActorScript.sort_effect_rows(rows):
		_buff_panel.add_child(_make_effect_chip(row))


func _make_effect_chip(row: Dictionary) -> Button:
	var chip := Button.new()
	chip.name = "Buff_%s_%s" % [row.get("actor_id", "?"), row.get("entry_id", row.get("buff_id", "?"))]
	chip.flat = true
	chip.focus_mode = Control.FOCUS_NONE
	chip.add_theme_font_size_override("font_size", 12)
	var is_debuff := bool(row.get("is_debuff", false))
	var remaining := int(row.get("remaining", 0))
	var time_text := "∞" if (bool(row.get("permanent", false)) or remaining < 0) else str(remaining)
	var stacks := int(row.get("stacks", 1))
	# 图标（15 §4.3 的「增益减益 19／异常状态 4」）：**优先表里的 `icon`，空则退回行 id**
	# ——与 `item_base.icon` 同一套优先级，所以美术按哪个名字交都能对上。
	# **有图才摆**：一张都还没交付时 `icon` 留 null，卡片尺寸与以前一模一样（不占空位）。
	var icon_id := str(row.get("icon", "")).strip_edges()
	if icon_id.is_empty():
		icon_id = str(row.get("entry_id", row.get("buff_id", "")))
	var icon_path := (
		IconPathsScript.status(icon_id)
		if str(row.get("kind", "")) == "status"
		else IconPathsScript.buff(icon_id)
	)
	chip.set_meta("icon_path", icon_path)
	var icon_texture := IconPathsScript.load_icon(icon_path)
	if icon_texture != null:
		chip.icon = icon_texture
	# 极性用「形状 + 字」两重编码（08 要求色盲可辨：光靠红蓝不够）
	# 名字要带上是谁的（合并列表里不写归属，玩家不知道这条 buff 挂在谁身上）
	chip.text = "%s %s%s·%s %s%s" % [
		str(row.get("side_mark", "")), "▼" if is_debuff else "▲", "减" if is_debuff else "增",
		str(row["name"]), time_text,
		"" if stacks <= 1 else " ×%d" % stacks,
	]
	# 极性配色（08：增益蓝、减益红）——另配「增／减」字标，色盲也能分辨
	chip.add_theme_color_override("font_color", Color("#e57373") if is_debuff else Color("#64b5f6"))
	chip.tooltip_text = "%s·%s（%s，剩余 %s）%s" % [
		str(row.get("actor_name", "")), str(row["name"]),
		"减益" if is_debuff else "增益", time_text, str(row.get("desc", "")),
	]
	return chip


func _make_actor_row(actor) -> Control:
	var row := VBoxContainer.new()
	row.name = "Actor_%s" % actor.actor_id
	row.add_theme_constant_override("separation", 2)
	# 名字与「选为目标」同一行：卡片矮一点，多几个敌人也不用滚
	var head := HBoxContainer.new()
	head.name = "Head_%s" % actor.actor_id
	head.add_theme_constant_override("separation", 6)
	row.add_child(head)
	# 敌人剪影（15 六：`assets/sprites/<faction>/<enemy_id>.png`）——**有图才摆**。
	# 美术先出了两条做对照（山寨喽啰／别派弟子），其余还没出；我方那一侧不放
	# （角色的形象在角色面板的立绘里，战斗卡片只列数值，免得卡片变高）。
	if actor.side == BattleActorScript.SIDE_ENEMY:
		var sprite_path := IconPathsScript.actor(
			str(actor.tags.get("faction", "")), str(actor.source_id)
		)
		var sprite := IconPathsScript.make_icon(sprite_path, "Sprite_%s" % actor.actor_id)
		if sprite != null:
			head.add_child(sprite)
	var label := Label.new()
	label.name = "Name_%s" % actor.actor_id
	var is_target: bool = actor.side == BattleActorScript.SIDE_ENEMY and str(actor.actor_id) == selected_target_id
	label.text = "%s%s　Lv%d　气血 %d/%d　内力 %d" % [
		"▶ " if is_target else "",
		actor.display_name, actor.level, actor.hp, actor.max_hp(), actor.qi,
	]
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	# 阵亡：卡片压暗并标「（倒）」，还带一点点倒下的位移（跳字与血条都跟着暗下去）
	var dead: bool = not actor.is_alive()
	if dead:
		label.text = "%s　（倒）" % label.text
		row.modulate = Color(1, 1, 1, 0.45)
		row.position.x = -6.0
		var fall := create_tween()
		fall.tween_property(row, "position:x", 0.0, 0.25)
	if is_target:
		label.add_theme_color_override("font_color", Color("ffd24a"))
	if dead:
		label.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
	elif actor.side == BattleActorScript.SIDE_ALLY and actor.hp * 4 <= maxi(1, actor.max_hp()):
		# 濒死提示：血量 ≤25% 时名字转红，玩家一眼看到要救
		label.add_theme_color_override("font_color", Color("eb5757"))
		label.text += "　（危急）"
	head.add_child(label)
	if actor.side == BattleActorScript.SIDE_ENEMY and actor.is_alive():
		var pick := Button.new()
		pick.name = "TargetButton_%s" % actor.actor_id
		pick.text = "▶ 已选中" if is_target else "选为目标"
		pick.custom_minimum_size = Vector2(88, 24)
		pick.pressed.connect(func() -> void: select_target(actor.actor_id))
		head.add_child(pick)
		# 拆招按钮：预兆已给，玩家在本回合自己的行动里可以「花一次行动读破它」
		var parry := Button.new()
		parry.name = "ParryButton_%s" % actor.actor_id
		parry.text = "已拆招" if sim.is_parried(str(actor.actor_id)) else "拆招"
		parry.custom_minimum_size = Vector2(72, 24)
		var ally = _current_ally_actor()
		var reason: String = sim.parry_block_reason(ally, actor)
		parry.disabled = not reason.is_empty()
		if parry.disabled:
			parry.tooltip_text = reason
		elif not sim.is_parried(str(actor.actor_id)):
			# 能拆的时候把**效果**写清楚——三条都是已实现的行为（`PARRY_DAMAGE_MULT` 减半、
			# `PARRY_POISE_GAIN` 反涨、花掉这次行动），跟逃跑那个成功率提示同一类：
			# 信息就在代码里，别让玩家靠试。
			parry.tooltip_text = "读破这一招：它这次伤害减半，自己反涨架势（要花掉这次行动）"
		parry.pressed.connect(func() -> void: press_parry(str(actor.actor_id)))
		head.add_child(parry)
	# 预兆行：敌人这一回合要出什么招，界面上要先看得见（设计 04 的核心循环）
	if actor.side == BattleActorScript.SIDE_ENEMY and actor.is_alive():
		var intent_id: String = sim.intent_of(str(actor.actor_id))
		if not intent_id.is_empty():
			var intent := Label.new()
			intent.name = "Intent_%s" % actor.actor_id
			if sim.is_parried(str(actor.actor_id)):
				intent.text = "预兆：%s（已被拆招，伤害减半）" % sim.skill_display_name(intent_id)
				intent.add_theme_color_override("font_color", Color("9a9a9a"))
			else:
				intent.text = "预兆：%s" % sim.skill_display_name(intent_id)
				intent.add_theme_color_override("font_color", Color("c9a0ff"))
			intent.add_theme_font_size_override("font_size", 12)
			row.add_child(intent)
	var bar := ProgressBar.new()
	bar.name = "HpBar_%s" % actor.actor_id
	bar.max_value = maxf(1.0, float(actor.max_hp()))
	bar.value = float(actor.hp)
	bar.custom_minimum_size = Vector2(320, 10)
	bar.show_percentage = false
	row.add_child(bar)
	# 内力条与架势条：手选招式要看内力，破绽机制要看架势
	var bars := HBoxContainer.new()
	bars.name = "Bars_%s" % actor.actor_id
	bars.add_theme_constant_override("separation", 6)
	row.add_child(bars)
	bars.add_child(_make_bar("QiBar_%s" % actor.actor_id, float(actor.qi), float(maxi(1, actor.max_qi())), Color("5aa9e6"), 150))
	var poise_bar := _make_bar(
		"PoiseBar_%s" % actor.actor_id, float(actor.poise), float(maxi(1, actor.max_poise())),
		Color("e0a13c") if not actor.is_broken() else Color("eb5757"), 160
	)
	bars.add_child(poise_bar)
	_decorate_poise_bar(poise_bar, actor)
	if actor.is_broken():
		var mark := Label.new()
		mark.name = "BrokenMark_%s" % actor.actor_id
		mark.text = "破绽！"
		mark.add_theme_color_override("font_color", Color("eb5757"))
		bars.add_child(mark)
	# 我方单位详情（设计 14 §四：左侧那一栏 = 气血／内力／架势 ＋ **武器与已装内功**）。
	# 前三条已经在上面（名字里的气血／内力 ＋ 两根条 ＋ 架势条），缺的是这一行。
	if actor.side == BattleActorScript.SIDE_ALLY:
		var gear := Label.new()
		gear.name = "Gear_%s" % actor.actor_id
		gear.text = _gear_text(actor)
		gear.add_theme_font_size_override("font_size", 12)
		gear.add_theme_color_override("font_color", Color(0.78, 0.83, 0.9))
		row.add_child(gear)
	# 异常状态：中毒/灼伤/流血/内伤，颜色取 damage_type.display_color
	var status_text := _status_text(actor)
	if not status_text.is_empty():
		var status_label := Label.new()
		status_label.name = "Status_%s" % actor.actor_id
		status_label.text = status_text
		status_label.add_theme_color_override("font_color", _status_color(actor))
		bars.add_child(status_label)
	return row


## 「武器与已装内功」那一行。两样都从**存档 + 表**里查（战斗单位身上只带武器类型与系别，
## 不带物品 id）；查不到就如实写「空手」／「未装内功」，不留空白。
func _gear_text(actor) -> String:
	var char_id := str(actor.actor_id)
	var weapon = PartyBuilderScript.equipped_weapon(db, state, char_id)
	var weapon_name := str(weapon.name_cn) if weapon != null else "空手"
	var names := PackedStringArray()
	for skill_id: String in actor.passives:
		var row: Resource = db.get_row("skill_base", skill_id)
		names.append(str(row.name_cn) if row != null else skill_id)
	var inner := "未装内功" if names.is_empty() else "、".join(names)
	return "武器：%s　内功：%s" % [weapon_name, inner]


## 一行状态摘要：「中毒×3（2回合）、灼伤×1」；没有状态返回空串
func _status_text(actor) -> String:
	if not actor.has_method("status_rows"):
		return ""
	var parts := PackedStringArray()
	for row: Dictionary in actor.status_rows(db):
		parts.append("%s×%d（%d回合）" % [str(row["name"]), int(row["stacks"]), int(row["remaining"])])
	return "、".join(parts)


## 状态行用第一条状态的伤害类型颜色（DoT 的颜色在 damage_type.display_color 里）
func _status_color(actor) -> Color:
	var rows: Array = actor.status_rows(db) if actor.has_method("status_rows") else []
	if rows.is_empty():
		return Color(1, 1, 1)
	return Color(str(rows[0]["color"]))


func _make_bar(node_name: String, value: float, maximum: float, color: Color, width: int) -> ProgressBar:
	var bar := ProgressBar.new()
	bar.name = node_name
	bar.max_value = maxf(1.0, maximum)
	bar.value = value
	bar.custom_minimum_size = Vector2(width, 8)
	bar.show_percentage = false
	var style := StyleBoxFlat.new()
	style.bg_color = color
	bar.add_theme_stylebox_override("fill", style)
	return bar


## 架势条的「即将破防」（设计 15 §4.5：这是核心循环的爽点，不能只是一条会变短的色块）。
##
## 分工（美术在 `tools/artgen/ui.js` 里写死的那句话）：**条的颜色仍由这里按色值表刷**，
## 两张贴图只管「加什么」——架势低于临界线叠 `poise_critical.png`（裂纹），打空那一刻
## 换成 `poise_break.png`（横条压亮 ＋ 四道炸开的短光）。
##
## 贴图原生 32×12（15 §4.4 的状态条高度），比这根 8px 的条高 2px，所以上下各溢 2px；
## **不影响版式**：子 Control 不计入父级最小尺寸，`满招式`那档 636/648 的预算照旧。
## 临界线是开发侧定的**表现**阈值（设计只写了"要有临界表现"、没给数值），
## 与判定无关，因此不进变异探针的手感常量清单。
const POISE_CRITICAL_TEXTURE := "res://assets/ui/battle/poise_critical.png"
const POISE_BREAK_TEXTURE := "res://assets/ui/battle/poise_break.png"
const POISE_CRITICAL_RATIO := 0.3


func _decorate_poise_bar(bar: ProgressBar, actor) -> void:
	var file := ""
	if actor.is_broken():
		file = POISE_BREAK_TEXTURE
	elif float(actor.poise) / maxf(1.0, float(actor.max_poise())) <= POISE_CRITICAL_RATIO:
		file = POISE_CRITICAL_TEXTURE
	if file.is_empty() or not ResourceLoader.exists(file):
		return     # 美术还没交图时不摆这一层（有图才加，与敌人剪影同一口径）
	var overlay := TextureRect.new()
	overlay.name = "PoiseFx_%s" % str(actor.actor_id)
	overlay.texture = load(file)
	overlay.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	overlay.stretch_mode = TextureRect.STRETCH_SCALE
	overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.offset_top = -2.0
	overlay.offset_bottom = 2.0
	bar.add_child(overlay)


func _clear(box: Node) -> void:
	for child in box.get_children():
		box.remove_child(child)
		child.queue_free()


func _append_log(lines: Array) -> void:
	for line: String in lines:
		var text := str(line)
		_log.append_text("%s\n" % _colored_line(text))
		# 预兆带由 `_refresh_intent_band()` 单独维护——日志不再往那儿写字


## 日志配色：回合头蓝色、暴击金色、未命中灰色、其余常规
func _colored_line(text: String) -> String:
	if text.begins_with("——"):
		return "[color=#8ab4f8]%s[/color]" % text
	if text.contains("暴击"):
		return "[color=#ffd24a]%s[/color]" % text
	if text.contains("破绽") or text.contains("架势被打"):
		return "[color=#eb5757]%s[/color]" % text
	if text.contains("未命中") or text.contains("跳过") or text.contains("失败"):
		return "[color=#9aa0a6]%s[/color]" % text
	var head_end := text.find(" ")
	if head_end > 0 and text.begins_with("R"):
		return "[color=#9aa0a6]%s[/color]%s" % [text.substr(0, head_end), text.substr(head_end)]
	return text


func _refresh_buttons() -> void:
	var done: bool = sim != null and sim.finished()
	_next_button.disabled = done
	_auto_button.disabled = done
	_auto_button.text = "自动战斗中…" if auto_enabled and not done else "自动战斗"
	_speed_button.text = "速度 %dx" % int(speed())
	# 策略按钮：把当前策略写在按钮上，玩家不用记
	_strategy_button.text = "策略：%s" % (sim.strategy_label() if sim != null else "均衡")
	_strategy_button.tooltip_text = "自动战斗策略（保守省内力／均衡期望伤害／全力单发最高倍率），点一下轮换"
	_strategy_button.disabled = done
	# 六指令里的四条（招式那一行与逃跑各自管自己）：战斗没结束就能按——
	# 没在回合里点一下会自己开回合（与「直接点招式先手出招」同一套行为）
	var actor = _current_ally_actor()
	var command_disabled := done or sim == null
	for button: Button in [_basic_button, _inner_button, _item_button, _defend_button]:
		if button == null:
			continue
		button.disabled = command_disabled
		button.tooltip_text = str(button.get_meta("hint", ""))
	if _defend_button != null and not command_disabled:
		var defend_reason: String = sim.defend_block_reason(actor) if actor != null else ""
		if not defend_reason.is_empty():
			_defend_button.disabled = true
			_defend_button.tooltip_text = defend_reason
	# 逃跑：轮到我方且不在「被追上第一回合」才可按；不能按时把原因写给玩家
	_flee_button.disabled = true
	if done or sim == null:
		_flee_button.tooltip_text = ""
		return
	var reason: String = sim.flee_block_reason(_current_ally_actor())
	_flee_button.disabled = not reason.is_empty()
	# 能撤的时候把**成功率**写进 tooltip：soft 判定面板早就如实显示成功率，逃跑是同一类掷点
	# （`FLEE_BASE_CHANCE + 0.5×身法差/身法和`，夹 25%~95%，见 `flee_chance`），
	# 没道理让玩家摸黑——而且它是**现算的**，设计改了公式这里自动跟着变。
	_flee_button.tooltip_text = reason if not reason.is_empty() \
		else "掷点判定：成功率 %d%%（身法差越大越高）" % int(round(sim.flee_chance(_current_ally_actor()) * 100.0))


func _set_status(text: String) -> void:
	if _status != null:
		_status.text = text


# ------------------------------------------------------------------ 环境

func _fallback_encounter():
	# 没有待处理遭遇时（例如自检直接开战斗界面），用野狼群走一遍
	# （`sp_lp_wolf_01` 这个写死的 id 由 `test_handshake` 的「字面量查表」门限盯着，
	#   表里改掉了它会在自检里当场点名，而不是运行时在这行静默炸掉。）
	var row: Resource = db.get_row("roaming_spawn", "sp_lp_wolf_01")
	var team: Resource = db.get_row("enemy_team", str(row.team_id))
	return EncounterScript.build(db, row, team, EncounterScript.CONTACT_FRONT, "normal")


func _resolve_db():
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var table_db = TableDbScript.new()
	table_db.load_all()
	return table_db


func _session_node(node_name: String = "GameSession") -> Node:
	var tree := _tree()
	if tree == null:
		return null
	return tree.root.get_node_or_null(node_name)


func _tree() -> SceneTree:
	# 不在场景树里时 get_tree() 会打一条 ERROR（--script 模式用例直接 new 节点就会踩到）
	if is_inside_tree():
		var tree := get_tree()
		if tree != null:
			return tree
	return Engine.get_main_loop() as SceneTree


func _has_user_arg(flag: String) -> bool:
	return OS.get_cmdline_user_args().has(flag)


## 自检用：全场血量总和，用来判断「推进是否真的产生了变化」
func _hp_total(actors: Array) -> int:
	var total := 0
	for actor in actors:
		total += maxi(0, int(actor.hp))
	return total


# ------------------------------------------------------------------ 自检

func _run_battle_selftest() -> void:
	# 容器的最小尺寸是延后一帧刷新的：先让界面过两帧，版式才量得准
	var tree := _tree()
	if tree != null:
		await tree.process_frame
		await tree.process_frame
	var ok := true
	var lines := PackedStringArray()
	ok = ok and allies.size() > 0 and enemies.size() > 0
	lines.append("开战：我方 %d 人 vs %s %d 人" % [allies.size(), encounter.team_name, enemies.size()])

	# 切磋镜像（Q88 拍板 ①）：遭遇里带 `mirror_char` 时，敌人**不从表里取**，而是照那个人现造
	# （与他上场时同一个 PartyBuilder：等级／加点／装备／招式全同）——这里验「一条命、敌方、
	# 满血、不给经验与钱」。**「只有同伴才走镜像」那条由 `spar_opponent()` 判**，在 NPC 面板自检里钉。
	var mirror_id := str(state.char_ids[0])
	var saved_mirror := str(encounter.mirror_char)
	encounter.mirror_char = mirror_id
	var mirrored: Array = _build_enemies(encounter)
	encounter.mirror_char = saved_mirror
	var mirror_ok: bool = mirrored.size() == 1 and mirrored[0] != null \
		and int(mirrored[0].side) == int(BattleActorScript.SIDE_ENEMY) \
		and str(mirrored[0].display_name) == state.char_name(db, mirror_id) \
		and int(mirrored[0].hp) == int(mirrored[0].max_hp()) \
		and int(mirrored[0].reward_exp) == 0 and int(mirrored[0].reward_money) == 0 \
		and not mirrored[0].skills.is_empty()
	ok = ok and mirror_ok
	lines.append("切磋镜像（照自己现造·敌方·满血·无收益）=%s" % mirror_ok)

	# 版式预算：整页要塞得进设计分辨率（曾经最小高度 711 > 648，标题被裁掉半行）
	ok = ok and LayoutBudgetScript.fits(self)
	lines.append(LayoutBudgetScript.ascii_line(self))
	# 卡片列表在只滚竖向的滚动区里：横滚关着，卡片宽了就被裁掉——而上面那行 LAYOUT 只量到外壳
	ok = ok and LayoutBudgetScript.content_fits(self)
	lines.append(LayoutBudgetScript.ascii_content_line(self))
	# 自动战斗策略按钮（设计 04：保守／均衡／全力）——按钮在、能轮换、写的和实际一致
	var strategy_ok: bool = _strategy_button != null and strategy_text().contains("均衡")
	ok = ok and strategy_ok
	var cycled: String = press_strategy()
	strategy_ok = strategy_ok and cycled == BattleSimulatorScript.STRATEGY_ALL_OUT and strategy_text().contains("全力")
	press_strategy()
	press_strategy()
	strategy_ok = strategy_ok and strategy_text().contains("均衡")
	ok = ok and strategy_ok
	lines.append("策略按钮 ok=%s（当前 %s）" % [strategy_ok, strategy_text()])

	# 敌方预兆横带（设计 14 §四要点一：预兆必须显眼、占一整条横带）：
	# 预兆是**开回合时**掷的，所以这里先确保有一个进行中的回合，再看这一行写的是什么
	# （这一行以前显示「最后一条日志」，与下面的日志区重复）。
	if not sim.in_round() and not sim.finished():
		sim.begin_round()
		_refresh()
	var band_ok: bool = _action_label != null and _action_label.text.begins_with("敌方预兆：")
	band_ok = band_ok and _action_label.text.contains("将用")
	ok = ok and band_ok
	lines.append("敌方预兆横带=%s（%s）" % [band_ok, _action_label.text if _action_label != null else "-"])
	# 我方单位详情那一行（设计 14 §四：左侧 = 气血／内力／架势 ＋ 武器与已装内功）——
	# 前三条在名字与两根条上，这一行补武器与内功。
	var gear: Label = find_child("Gear_%s" % allies[0].actor_id, true, false)
	var gear_ok: bool = gear != null and gear.text.contains("武器：") and gear.text.contains("内功：")
	ok = ok and gear_ok
	lines.append("我方详情（武器／已装内功）=%s（%s）" % [gear_ok, gear.text if gear != null else "-"])

	# 战斗里能用哪些招，必须来自「装配」而不是模板（装配改动要真的进战斗）
	var char_id := str(allies[0].actor_id)
	var equipped: PackedStringArray = state.loadout_of(char_id)["active"]
	var loadout_ok: bool = allies[0].skills == equipped
	ok = ok and loadout_ok
	lines.append("战斗招式来自装配：%s（%d 招）" % [loadout_ok, equipped.size()])
	# 反向再验一次：把装配清空后重造战斗单位，招式必须跟着空。
	# 「模板里有什么就会什么」的实现在这里会变红（模板与装配恰好相同时，上面那条比不出来）
	var saved_active: PackedStringArray = state.loadout_of(char_id)["active"]
	var saved_passive: PackedStringArray = state.loadout_of(char_id)["passive"]
	state.set_loadout(char_id, PackedStringArray(), saved_passive)
	var stripped = PartyBuilderScript.build_actor(db, state, char_id)
	var stripped_ok: bool = stripped != null and stripped.skills.is_empty()
	state.set_loadout(char_id, saved_active, saved_passive)
	ok = ok and stripped_ok
	lines.append("卸空装配后战斗单位无招：%s" % stripped_ok)

	# 六指令（设计 08）：普通攻击／内功／道具／主动防御都在这个真实界面上走一遍，
	# 增益减益面板也要**真的有条目**（不是只有一套算好的数据）。
	# 招式与逃跑由别处盖住（本自检后面就是一路用招式打到结束）。
	var commands_ok := true
	var item_options: Array = sim.battle_item_options(state.inventory)
	commands_ok = commands_ok and item_options.size() == 2
	for option: Dictionary in item_options:
		commands_ok = commands_ok and not bool(option["usable"])
	lines.append("道具指令：列出 %d 件（效果列未配，全部如实标注）" % item_options.size())
	# 内功：自检这一局的装配可能没铺内功，这里给这个单位塞一部（只影响这场自检）
	allies[0].passives = PackedStringArray(["pf_xuanwei_01"])
	var inner_options: Array = sim.passive_cast_options(allies[0])
	commands_ok = commands_ok and inner_options.size() == 1
	lines.append("内功指令：可催动 %d 部" % inner_options.size())
	# 普通攻击：真打出伤害，而且不写进招式使用记录
	var basic_target = enemies[0]
	var basic_hp := int(basic_target.hp)
	press_basic_attack()
	commands_ok = commands_ok and int(basic_target.hp) < basic_hp
	commands_ok = commands_ok and sim.skill_uses(str(allies[0].actor_id)).is_empty()
	# 主动防御：真拿到 buff，面板真有条目
	press_defend()
	commands_ok = commands_ok and allies[0].has_buff("buff_guard")
	commands_ok = commands_ok and find_child("Buff_%s_buff_guard" % allies[0].actor_id, true, false) != null
	# 内功催动：真拿到运功 buff
	var cast: Dictionary = sim.cast_passive(allies[0], str(inner_options[0]["skill_id"]))
	commands_ok = commands_ok and bool(cast["ok"]) and allies[0].has_buff("buff_yunqi")
	lines.append("六指令（普攻／内功／道具／主动防御）ok=%s" % commands_ok)
	ok = ok and commands_ok

	var before_exp := int(state.party_exp)
	press_auto()
	press_stop_auto()
	# 「下一回合」只推进到我方行动为止，所以轮到我方时必须真出招；
	# 只循环 press_next_round() 会在轮到自己的那一刻永远推不动（曾经把自检挂死）。
	var guard := 0
	var stall := 0
	var last_signature := ""
	while not sim.finished() and guard < 300:
		guard += 1
		var actor = sim.current_actor() if sim.in_round() else null
		if actor != null and allies.has(actor):
			var skills: Array = sim.available_skills(actor)
			if skills.is_empty():
				press_next_round()
			else:
				press_skill(str(skills[0].skill_id))
		else:
			press_next_round()
		var signature := "%d/%d/%d" % [sim.rounds_played(), _hp_total(allies), _hp_total(enemies)]
		stall = stall + 1 if signature == last_signature else 0
		last_signature = signature
		if stall >= 8:
			lines.append("卡死：连续 8 步没有任何变化（第 %d 步）" % guard)
			ok = false
			break
	lines.append("推进步数：%d" % guard)
	lines.append("结果：%s（%d 回合）" % [sim.winner(), sim.rounds_played()])
	lines.append(result_label_text())
	# 日志要自动跟到底：不然打到第 9 回合，日志还停在第一回合那几行
	if tree != null:
		await tree.process_frame
	var log_bar: VScrollBar = _log.get_v_scroll_bar()
	var log_tail: bool = log_bar == null or log_bar.max_value - log_bar.page <= log_bar.value + 1.0
	ok = ok and log_tail
	lines.append("日志跟随到底：%s（value %.0f，底 %.0f）" % [
		log_tail,
		log_bar.value if log_bar != null else 0.0,
		(log_bar.max_value - log_bar.page) if log_bar != null else 0.0,
	])
	# 结算之后版式还得塞得进：结算文案会变长（经验／铜钱／掉落／领悟都在这里）
	ok = ok and LayoutBudgetScript.fits(self)
	lines.append(LayoutBudgetScript.ascii_line(self, "settled"))
	# 极端长掉落文案：卡片行数固定三行，但**每一行都会换行**（`Result` 是普通 Label、不在滚动区里），
	# 一长就顶破 648（当年「711 > 648、标题被裁掉半行」就是这么坏的）。
	# 这里**走真实生成路径**（`_describe_drops_card`）造一段「多件掉落 + 带词条装备」的文案，
	# 等一帧再量——直接手写长文字会绕过截断，量到的就不是线上会出现的形态（第一版就这么写错了）。
	var many: Dictionary = {"items": [], "equipment": [], "overflow": []}
	for item_id: String in [
			"item_herb", "item_iron", "item_pelt", "item_med_01", "item_med_02", "item_potion_small", "item_potion_qi",
			"item_herb", "item_iron", "item_pelt"]:
		(many["items"] as Array).append({"item_id": item_id, "qty": 3})
	for base_id: String in ["eq_sword_01", "eq_head_01", "eq_armor_01", "eq_ring_01"]:
		(many["equipment"] as Array).append(state.inventory.add_equipment(db, base_id))
	(many["overflow"] as Array).append({"item_id": "item_iron", "qty": 2})
	(many["overflow"] as Array).append({"item_id": "item_pelt", "qty": 4})
	var card_drops: String = _describe_drops_card(many)
	ok = ok and card_drops.contains("明细见战报")
	lines.append("长文案下的卡片掉落行（%d 字）：%s" % [card_drops.length(), card_drops])
	var saved_result := _result_label.text
	_result_label.text = "胜利！经验 +1280　铜钱 +256\n掉落：%s\n升级：书生 3 → 5（+6 点）" % card_drops
	await tree.process_frame
	var long_text_ok: bool = LayoutBudgetScript.fits(self)
	ok = ok and long_text_ok
	# 自动换行的 Label 在最小尺寸里不一定算得准换行；**直接量它换了几个视觉行**更实在
	var card_lines: int = _result_label.get_line_count()
	ok = ok and card_lines <= SETTLE_CARD_LINES + 1
	lines.append("%s（卡片视觉行数 %d）" % [LayoutBudgetScript.ascii_line(self, "长文案"), card_lines])
	# 上面三次量法都可能量在「技能行恰好是空的」那一帧——而真实对局里只要轮到我方，这一行就在。
	# 2026-10-03 正是踩了这个：4 人队时多出来的 31px 全部来自这一行，1 人队却碰巧量在空帧、一路绿
	# （见框架说明决策 162）。现在补一次**最坏情况**：把技能行按上限撑满按钮，两个方向一起量。
	# 上限取自 `growth_const.active_slot_cap`（不写死 9），文案取真实招式名的长度。
	var slot_cap := 9
	var cap_row: Resource = db.get_row("growth_const", "active_slot_cap")
	if cap_row != null:
		slot_cap = maxi(1, int(float(cap_row.value)))
	var temp_buttons: Array = []
	for i in range(slot_cap):
		var temp_button := Button.new()
		temp_button.text = "玄微剑法·起手%d" % i
		_skill_row.add_child(temp_button)
		temp_buttons.append(temp_button)
	await tree.process_frame
	var full_row_ok: bool = LayoutBudgetScript.fits(self)
	ok = ok and full_row_ok
	lines.append("%s（技能行按上限 %d 个按钮撑满）" % [
		LayoutBudgetScript.ascii_line(self, "满招式"), slot_cap,
	])
	for temp_button: Node in temp_buttons:
		temp_button.queue_free()
	await tree.process_frame
	# 「带状态」那一档（2026-10-04 补）：chip 有图时按**原生 32×32** 摆（不缩放——像素图非整数倍会糊），
	# 所以带状态的那一排比纯文字版高约 12px。而自检此前**从没量到带状态的那一版**：
	# `_check_status_ui` 那些是 headless 用例、不量 LAYOUT（美术交异常图标时指出来的）。
	# 照「满招式」那档的办法：临时塞几枚**带图**的 chip，量一次外壳，再收掉。
	# 图取真实表行（`status_effect` 的 icon 列），所以美术交图/改名的效果当场就能看见。
	var temp_chips: Array = []
	for status_id: String in ["poison", "burn", "bleed", "internal"]:
		var status_row: Resource = db.get_row("status_effect", status_id)
		if status_row == null:
			continue
		temp_chips.append(_make_effect_chip({
			"kind": "status", "entry_id": status_id, "buff_id": status_id,
			"icon": str(status_row.icon), "name": str(status_row.name_cn),
			"is_debuff": true, "remaining": 3, "stacks": 2, "permanent": false,
			"side_mark": "我方",
		}))
	for chip: Node in temp_chips:
		_buff_panel.add_child(chip)
	await tree.process_frame
	var with_status_ok: bool = LayoutBudgetScript.fits(self)
	ok = ok and with_status_ok
	var icon_chips := 0
	for chip: Button in temp_chips:
		if chip.icon != null:
			icon_chips += 1
	lines.append("%s（带状态：%d 枚 chip，其中 %d 枚真的带图）" % [
		LayoutBudgetScript.ascii_line(self, "带状态"), temp_chips.size(), icon_chips,
	])
	for chip: Node in temp_chips:
		chip.queue_free()
	await tree.process_frame
	_result_label.text = saved_result
	ok = ok and LayoutBudgetScript.content_fits(self)
	lines.append(LayoutBudgetScript.ascii_content_line(self))
	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	# 浮层与全局快捷键（设计 14 §二／§八）：**战斗中 Tab 开角色面板属于「浮层盖场景层」**，
	# 再按一次是「弹回它」而不是叠第二份；Esc 一次弹一层。
	var overlay_ok := true
	overlay_ok = overlay_ok and bool(open_overlay(CHARACTER_SCENE, "CharacterPanel", 0).get("ok", false))
	overlay_ok = overlay_ok and open_overlay_count() == 1
	var char_panel := find_child("CharacterPanel", true, false)
	overlay_ok = overlay_ok and char_panel != null and char_panel.current_tab() == 0
	overlay_ok = overlay_ok and bool(open_overlay(CHARACTER_SCENE, "CharacterPanel", 2).get("reopened", false))
	overlay_ok = overlay_ok and open_overlay_count() == 1
	overlay_ok = overlay_ok and char_panel != null and char_panel.current_tab() == 2
	overlay_ok = overlay_ok and close_top_overlay() and open_overlay_count() == 0
	ok = ok and overlay_ok
	lines.append("浮层（战斗中开角色面板／再按弹回并切行囊页／Esc 弹回）=%s" % overlay_ok)

	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	var copy_hits: PackedStringArray = CopyGuardScript.id_tokens(self)
	ok = ok and copy_hits.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	if not copy_hits.is_empty():
		lines.append("COPY 命中：%s" % "；".join(copy_hits))

	if sim.winner() == BattleSimulatorScript.WINNER_ALLY:
		ok = ok and int(state.party_exp) > before_exp
		var session_node := _session_node()
		var cleared: bool = session_node != null and session_node.cleared_spawns.has(encounter.spawn_id)
		ok = ok and cleared
		lines.append("落账：经验 +%d，明雷已清=%s" % [int(state.party_exp) - before_exp, cleared])
	ok = ok and settled
	for line: String in lines:
		print("  " + line)
	print("BATTLE SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	if tree != null:
		tree.quit(0 if ok else 1)
## 出手动作：出手者的卡片闪一下暖光，让「谁在打」一眼看得出来。
## 注意**不能动子节点的 position**：卡片挂在 VBoxContainer 里，容器会重排位置把补间顶掉；
## 动 modulate　既可靠又不跟容器打架。多段招式只闪一次（按 actor 去重），命中/未命中都闪。
func _lunge_attackers(events: Array) -> void:
	var seen := {}
	for event: Dictionary in events:
		var source := str(event.get("source", ""))
		if source.is_empty() or seen.has(source):
			continue
		var kind := str(event.get("kind", ""))
		if kind != "hit" and kind != "miss":
			continue
		seen[source] = true
		var row := _find_actor_row(source)
		if row == null or not (row is Control):
			continue
		var item := row as Control
		item.pivot_offset = _card_pivot(item)
		# 出手分帧：0 蓄势（压一下）→ 1 探身（弹出）→ 2 收势（回位），同时打一层暖光。
		# 卡片在容器里，position 会被下一次排版顶掉，所以整段用 scale（不受容器排版影响）。
		item.modulate = Color(1.35, 1.2, 0.8, 1.0)
		item.scale = Vector2(0.97, 0.97)
		var tween := create_tween()
		tween.set_trans(Tween.TRANS_QUAD)
		tween.tween_property(item, "scale", Vector2(1.06, 1.06), 0.09)
		tween.tween_property(item, "scale", Vector2.ONE, 0.18)
		tween.parallel().tween_property(item, "modulate", Color(1, 1, 1, 1), 0.27)


## 结算里的掉落明细：「掉落：草药 ×2、铁剑（力+3、外功攻击+7）」
## 以前只写「物品 2 种 装备 1 件」，玩家还得去背包翻。
func _describe_drops(applied: Dictionary) -> String:
	return "、".join(_drop_parts(applied))


## 卡片上的掉落行：最多 SETTLE_DROP_ITEMS 件、最多 SETTLE_DROP_MAX_CHARS 字，其余进战报
func _describe_drops_card(applied: Dictionary) -> String:
	var parts: PackedStringArray = _drop_parts(applied)
	if parts.size() <= SETTLE_DROP_ITEMS:
		var short_text := "、".join(parts)
		return short_text if short_text.length() <= SETTLE_DROP_MAX_CHARS else short_text.substr(0, SETTLE_DROP_MAX_CHARS) + "…"
	var shown := PackedStringArray()
	for index in range(SETTLE_DROP_ITEMS):
		shown.append(parts[index])
	var text := "、".join(shown)
	if text.length() > SETTLE_DROP_MAX_CHARS:
		text = text.substr(0, SETTLE_DROP_MAX_CHARS)
	return "%s…（共 %d 件，明细见战报）" % [text, parts.size()]


## 掉落清单的每一项（卡片行与战报明细共用一份，免得两边写法漂）
func _drop_parts(applied: Dictionary) -> PackedStringArray:
	var parts := PackedStringArray()
	for entry: Dictionary in Array(applied.get("items", [])):
		var item_id := str(entry.get("item_id", ""))
		var row: Resource = db.get_row("item_base", item_id)
		parts.append("%s ×%d" % [str(row.name_cn) if row != null else item_id, int(entry.get("qty", 1))])
	for instance_id: String in Array(applied.get("equipment", [])):
		var base_id := str(state.inventory.base_of(str(instance_id)))
		var base: Resource = db.get_row("equip_base", base_id)
		var text := str(base.name_cn) if base != null else base_id
		var affix := _affix_text(str(instance_id))
		parts.append("%s（%s）" % [text, affix] if not affix.is_empty() else text)
	for entry: Dictionary in Array(applied.get("overflow", [])):
		var item: Resource = db.get_row("item_base", str(entry.get("item_id", "")))
		parts.append("%s×%d 没捡起（背包满）" % [
			str(item.name_cn) if item != null else str(entry.get("item_id", "")), int(entry.get("qty", 1)),
		])
	return parts


func _affix_text(instance_id: String) -> String:
	return AffixRollerScript.summarize(db, state.inventory.affixes_of(instance_id))
