## 大地图：明雷生成、行为、接触判定、遭遇交接到战斗、打完回图后的清怪状态。
extends "res://tests/test_case.gd"

const OVERWORLD_RUN := "res://scenes/world_run.tscn"
const BATTLE_SCENE := "res://scenes/battle_screen.tscn"
const GameStateScript := preload("res://src/core/game_state.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const RoamingEnemyScript := preload("res://src/world/roaming_enemy.gd")
const OverworldControllerScript := preload("res://src/world/overworld_controller.gd")

const SAVE_TEST_DIR := "res://.logs/test_save_timing/run_world"


func suite_name() -> String:
	return "大地图与明雷"


func run() -> void:
	if scene_tree == null:
		fail("没有注入场景树")
		return
	var db = get_db()
	var state = solo_state(db)
	# 0.8.1：大地图明雷默认**关着**（`feature_toggle.overworld_roaming_enemy=0`）。
	# 这个用例验的是明雷机制本身，所以先把会话级开关打开（跑完在 _check_roaming_toggle 里还原）。
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	if session_node != null:
		session_node.roaming_enabled_override = 1
	var controller = load(OVERWORLD_RUN).instantiate()
	controller.state_override = state
	var captures: Array = []
	controller.battle_switch_handler = func(encounter) -> void: captures.append(encounter)
	scene_tree.root.add_child(controller)
	controller.setup()

	_check_spawn(db, controller)
	_check_contact(controller, captures)
	_check_neutral(controller, captures)
	_check_sneak(controller, captures)
	_check_world_map(controller, state)
	_check_node_icons(controller)
	_check_save_on_enter(controller, state)
	_check_loop(db, controller, captures, state)
	_check_portal_arm(db, controller, state)
	_check_locked_portal(db, controller, state)
	_check_enter_position_rule(db, controller)
	_check_cleared(db, state)
	_check_respawn(db, controller, state)
	_check_respawn_under_player(db, controller)
	_check_retreat_push_out(db, state)
	_check_pinned_interaction_distances()
	_check_roaming_toggle(db, state)
	_check_field_buff_hud(controller)
	# 区域招募（09 §3.2 的落雁坡那条路）：用**独立的一份存档与大地图实例**跑，
	# 免得把上面那份共用 state（队伍人数会被它改）带偏
	_check_region_recruit(db)
	scene_tree.root.remove_child(controller)
	controller.free()


## 走到 `Markers/Node_n_luoyanpo` 跟前 → 把林铁山收进来（设计 09 §3.2 的区域招募）。
##
## 这一段以前只有服务层用例（`test_recruit` 验 `pending_for_region`／`join_all_for_region`，
## 外加半径常量的绝对值断言）——**控制器里「走过去就加入」那一小段没人跑过**，而它正是
## 「条款齐了、接入点漏了」最容易出事的地方（同族问题见 `框架说明.md` 决策 256）。
func _check_region_recruit(db) -> void:
	var state = solo_state(db)
	var char_id := "ch_gang"          # 落雁坡那位（`recruit_def.join_scene = n_luoyanpo`）
	var row: Resource = db.get_row("recruit_def", char_id)
	check_not_null(row, "表里有林铁山那一行")
	if row == null:
		return
	check_eq(str(row.join_scene), "n_luoyanpo", "他的加入点写在区域节点上（不是小地图）")
	state.set_flag(str(row.join_condition))
	var before_party: int = state.party_size()
	var map = load(OVERWORLD_RUN).instantiate()
	map.state_override = state
	scene_tree.root.add_child(map)
	map.setup()
	var node: Node2D = map.world.get_node_or_null("Markers/Node_n_luoyanpo")
	check_not_null(node, "大地图上有落雁坡地标")
	# 出生点在清风驿，离落雁坡很远：进图那一下不该已经收人
	check_eq(state.party_size(), before_party, "刚进图（人在清风驿）还没收到人")
	if node != null:
		map.player.global_position = node.global_position
		var joined: Array = map._check_region_recruits()
		check_eq(joined.size(), 1, "走到落雁坡地标就收到一位同伴")
		check_eq(state.party_size(), before_party + 1, "队伍多了一个人")
		check_true(state.char_ids.has(char_id), "林铁山进了队伍名单")
		check_true(state.has_flag("flag_%s_joined" % char_id), "入队点亮 flag_%s_joined" % char_id)
		# 走开再走回来不该重复收（大地图每帧都会调这个检查）
		map.player.global_position = node.global_position + Vector2(400, 0)
		map._check_region_recruits()
		map.player.global_position = node.global_position
		var again: Array = map._check_region_recruits()
		check_eq(again.size(), 0, "已经入队的人不会再收一次")
		check_eq(state.party_size(), before_party + 1, "队伍人数没变")
	scene_tree.root.remove_child(map)
	map.free()


## 战斗外增益 HUD（08）：大地图上也必须看得见还剩几分钟（与城镇共用 `FieldBuffHud` 的文案）
func _check_field_buff_hud(controller) -> void:
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	check_not_null(session_node, "用例能拿到会话")
	if session_node == null:
		return
	session_node.clear_field_buffs()
	session_node.add_field_buff("buff_meditated", 10)
	controller._refresh_field_buffs()
	var label: Label = controller.find_child("FieldBuffs", true, false)
	check_not_null(label, "大地图 HUD 上有战斗外增益那一行")
	if label != null:
		check_true(label.text.contains("打坐余韵"), "写清是哪个增益：%s" % label.text)
		check_true(label.text.contains("剩 10 分钟"), "写清还剩几分钟：%s" % label.text)
	# 同一条 buff 再来一次 = 刷新，不叠一行（08：`buff_meditated` 的 stack_rule=refresh）
	session_node.add_field_buff("buff_meditated", 10)
	check_eq(session_node.active_field_buffs().size(), 1, "重复获得只刷新，不变成两条")
	session_node.clear_field_buffs()
	controller._refresh_field_buffs()
	if label != null:
		check_eq(label.text, "", "清掉之后这一行是空的")


## 0.8.1 的大地图明雷开关：默认（读表）关着；开关注 1 时 17 个位点照旧全部生成。
##
## 设计原文："开关打开时，闭环里的「明雷战斗（胜／败两种结局）」那一整段照旧能跑，
## 所以两条路线都被测试覆盖着，不会出现「关了半年再打开发现已经烂了」。"
func _check_roaming_toggle(db, state) -> void:
	var session_node = scene_tree.root.get_node_or_null("GameSession")

	# ① 会话覆盖 = 0：一张大地图上不该有明雷
	if session_node != null:
		session_node.roaming_enabled_override = 0
	var off = load(OVERWORLD_RUN).instantiate()
	off.state_override = state
	scene_tree.root.add_child(off)
	off.setup()
	check_eq(off.enemies.size(), 0, "开关 0：大地图不生成明雷")
	check_false(off.roaming_enabled(), "开关 0：roaming_enabled() 为假")
	scene_tree.root.remove_child(off)
	off.free()

	# ② 会话覆盖 = 1：17 个位点照旧全部生成（机制没被删，只是默认关）
	if session_node != null:
		session_node.roaming_enabled_override = 1
	var on = load(OVERWORLD_RUN).instantiate()
	on.state_override = state
	scene_tree.root.add_child(on)
	on.setup()
	check_eq(
		on.enemies.size(), db.rows("roaming_spawn").size(),
		"开关 1：%d 个明雷位点照旧全部生成" % db.rows("roaming_spawn").size()
	)
	check_true(on.roaming_enabled(), "开关 1：roaming_enabled() 为真")
	scene_tree.root.remove_child(on)
	on.free()

	# ③ 恢复"读表"：默认值就是 design 定的 0
	if session_node != null:
		session_node.roaming_enabled_override = -1
	var by_table = load(OVERWORLD_RUN).instantiate()
	by_table.state_override = state
	scene_tree.root.add_child(by_table)
	by_table.setup()
	check_false(by_table.roaming_enabled(), "不覆盖时读表：默认 0（大地图不出现明雷）")
	check_eq(
		int(db.get_row("feature_toggle", "overworld_roaming_enemy").value), 0,
		"表里 overworld_roaming_enemy 的默认值就是 0"
	)
	scene_tree.root.remove_child(by_table)
	by_table.free()


## 大地图三个「交互距离」都是开发侧定的（表里没有这些列），而且各自钉着一条既有规则：
## 变异探针实测：`PORTAL_ARM_MARGIN` 8→1、`DISCOVER_DISTANCE` 96→48、`PORTAL_DISTANCE` 26→60
## 一整套自检**全都不红**（决策 154）。其中 `PORTAL_ARM_MARGIN` 尤其要守——决策 48／75 那一串
## 「出小地图立刻被送回」的修复就靠它：降到 0 附近等于出生点又压在出口上，老 bug 会悄悄回来。
## 按 `test_poise` 里敌人那两行的同一条纪律，把数值本身钉住。
func _check_pinned_interaction_distances() -> void:
	check_float(OverworldControllerScript.PORTAL_DISTANCE, 26.0, "门户/驿站交互距离 26px")
	check_float(
		OverworldControllerScript.PORTAL_ARM_MARGIN, 8.0,
		"出图武装边距 8px（决策 48：出生点压在出口上时『先走出去一次』才武装）"
	)
	check_float(
		OverworldControllerScript.DISCOVER_DISTANCE, 96.0,
		"地标揭开距离 96px（设计 02：走进去才揭开）"
	)


## 本章不开放的小地图（废弃渡口）：走过去要**被挡住**，而不是进图。
##
## 这份清单 2026-10-03 起只有一处定义（`WorldMapService.LOCKED_SCENES`），大地图控制器引用它——
## 以前两个文件各写一份、注释写着「同一口径」，但没有任何门限盯着它们一致：
## 谁改一边都不会红，玩家那边却会出现「地图不让进、驿站还列着」这种前后不一致（决策 88）。
func _check_locked_portal(db, controller, state) -> void:
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	session_node.pending_local_scene = ""
	var switched: Array = []
	controller.scene_change_handler = func(path: String) -> void: switched.append(path)
	var ferry: Node2D = controller.world.get_node_or_null("Markers/Portal_scene_ferry_locked")
	check_not_null(ferry, "大地图上有通往废弃渡口的入口位点")
	if ferry == null:
		return
	# 先站远处让入口武装（PORTAL_ARM_MARGIN），再走进圈里
	controller.player.global_position = ferry.position + Vector2(400, 0)
	controller._check_portal()
	controller.player.global_position = ferry.position
	controller._check_portal()
	check_eq(switched.size(), 0, "本章不开放的图：不会切场景")
	check_true(
		str(controller._status.text).contains("本章不可前往"),
		"状态栏说明为什么进不去：%s" % str(controller._status.text),
	)
	check_eq(str(session_node.pending_local_scene), "", "也没把「待进入的小地图」写进会话")


## 进图时的**回程落点**规则（设计 02「返回大地图原位置」的两种情形）：
##   走进去 → 原位置 = 你当时站的地方；
##   驿站传送 → 原位置 = **目的地的地标**，不然从荒村出来会被一路拽回落雁坡的驿站
##   （那是「传送之前站的地方」，与小地图「挖通」捷径同一口径）。
func _check_enter_position_rule(db, controller) -> void:
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	var switched: Array = []
	controller.scene_change_handler = func(path: String) -> void: switched.append(path)

	var walk_spot := Vector2(700, 300)
	controller.player.global_position = walk_spot
	controller.enter_local_map("scene_heifengzhai")
	check_eq(switched.size(), 1, "走进去：切场景被触发")
	check_eq(session_node.world_position, walk_spot, "走进去：回程落点 = 你站的位置")
	check_eq(session_node.pending_local_scene, "scene_heifengzhai", "走进去：写清进哪张图")
	# 设计 09 §二：这个落点要跟着存档走（v13），否则退出重进就回到默认出生点了
	check_eq(controller.current_state().world_position(), walk_spot, "走进去：落点同步进存档的 world_pos（v13）")

	var huangcun: Resource = db.get_row("map_region", "n_huangcun")
	var expect := Vector2(float(huangcun.pos_x), float(huangcun.pos_y))
	controller.enter_local_map("scene_huangcun", true)
	check_eq(switched.size(), 2, "传送：也切场景")
	check_eq(session_node.world_position, expect, "驿站传送：回程落点 = 目的地地标（不拽回驿站）")
	check_eq(session_node.pending_local_scene, "scene_huangcun", "传送：写清进哪张图")

	# 收尾：别把注入的 handler 留给后面的用例
	controller.scene_change_handler = Callable()
	session_node.pending_local_scene = ""


## 入口武装：人正站在入口位点上时**不**自动进图；先走出圈外、再走近才进。
##
## 以前 `_check_portal()` 每帧只看距离，而从小地图回大地图时人正好站在入口位点上
## （设计 02 说「返回大地图原位置」，而原位置就是走进入口的那一步）——
## 于是「出图 → 立刻又被送回图」，玩家永远回不到大地图（真踩过）。
func _check_portal_arm(db, controller, state) -> void:
	var local_row: Resource = db.get_row("map_local", "scene_qingfengyi")
	var region: Resource = db.get_row("map_region", str(local_row.parent_node))
	var portal_pos := Vector2(float(region.pos_x), float(region.pos_y))
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	var switched: Array = []
	controller.scene_change_handler = func(path: String) -> void: switched.append(path)
	controller._portal_armed = false
	session_node.pending_local_scene = ""   # 前面的用例可能留过待进入的小地图，先清干净

	# ① 刚回图，人就站在入口上 → 不进图
	controller.player.global_position = portal_pos
	controller._check_portal()
	check_eq(switched.size(), 0, "刚落在大地图入口上时不会自动进图（不然出图就被送回去）")
	check_true(str(session_node.pending_local_scene).is_empty(), "也没写待进入的小地图")

	# ② 走出圈外 → 入口武装
	controller.player.global_position = portal_pos + Vector2(200, 0)
	controller._check_portal()
	check_eq(switched.size(), 0, "走开的过程不进图")

	# ③ 再走回来 → 这次要进图，并写清进哪张
	controller.player.global_position = portal_pos
	controller._check_portal()
	check_eq(switched.size(), 1, "走出圈外再走近才进图")
	check_eq(session_node.pending_local_scene, "scene_qingfengyi", "写清进哪张小地图")

	# 收尾：别把注入的 handler 和状态留给后面的用例
	controller.scene_change_handler = Callable()
	session_node.pending_local_scene = ""
	controller._portal_armed = true


## 地标图标（设计 02「已探索的地标显示在地图上」+ 07 §8.1 的 7 个图标 id）：
## 已揭开画正常图标、没揭开不画（不剧透）、本章去不了的用暗版、脚下最近的高亮。
func _check_node_icons(controller) -> void:
	var city: Sprite2D = controller.node_icon("n_qingfengyi")
	check_not_null(city, "清风驿有图标节点")
	if city != null:
		check_true(city.visible, "已揭开的清风驿图标可见")
		check_true(
			str(city.texture.resource_path).contains("icon_town"),
			"用的是表里配的 icon_town：%s" % str(city.texture.resource_path),
		)
	# 荒村要 proximity_5 才揭开，此刻应该还是暗的（黑风寨不行：落雁坡默认已揭开，
	# discover_luoyanpo 的链式规则会把它一起揭开——这条口径本身是对的）
	var hidden_icon: Sprite2D = controller.node_icon("n_huangcun")
	check_not_null(hidden_icon, "荒村有图标节点")
	if hidden_icon != null:
		check_false(hidden_icon.visible, "还没揭开的地标不画图标（不然是剧透）")
	check_true(controller.node_icon("n_missing") == null, "表里没有的地标没有图标")
	# 地标名字：规则与图标一致（已揭开才有名字，未揭开不写——写出来就是剧透）
	var city_label: Label = controller.find_child("NodeLabel_n_qingfengyi", true, false)
	check_not_null(city_label, "清风驿有名字标签")
	if city_label != null:
		check_eq(city_label.text, "清风驿", "名字取自 map_region.name_cn")
		check_true(city_label.visible, "已揭开的地标名字可见")
	var hidden_label: Label = controller.find_child("NodeLabel_n_huangcun", true, false)
	check_not_null(hidden_label, "荒村也有名字节点")
	if hidden_label != null:
		check_false(hidden_label.visible, "还没揭开的地标不写名字")

	# 走到荒村跟前揭开：图标当场出现，而且是正常版（它没被锁）
	var ruin: Node2D = controller.world.get_node_or_null("Markers/Node_n_huangcun")
	check_not_null(ruin, "地图上有荒村位点")
	if ruin != null:
		controller.player.global_position = ruin.global_position + Vector2(0, 40)
		controller._check_reveals()
		var ruin_label: Label = controller.find_child("NodeLabel_n_huangcun", true, false)
		if ruin_label != null:
			check_true(ruin_label.visible, "揭开后名字跟着出现")
			check_eq(ruin_label.text, "荒村", "名字是荒村")
		var ruin_icon: Sprite2D = controller.node_icon("n_huangcun")
		if ruin_icon != null:
			check_true(ruin_icon.visible, "揭开后荒村图标出现")
			check_true(
				str(ruin_icon.texture.resource_path).contains("icon_ruin")
					and not str(ruin_icon.texture.resource_path).contains("_dim"),
				"没锁的地标用正常版图标：%s" % str(ruin_icon.texture.resource_path),
			)

	# 走到废弃渡口跟前：它是本章去不了的，图标要用暗版
	var ferry: Node2D = controller.world.get_node_or_null("Markers/Node_n_ferry_abandoned")
	check_not_null(ferry, "地图上有废弃渡口位点")
	if ferry != null:
		controller.player.global_position = ferry.global_position + Vector2(0, 40)
		controller._check_reveals()
		var ferry_icon: Sprite2D = controller.node_icon("n_ferry_abandoned")
		check_not_null(ferry_icon, "废弃渡口有图标节点")
		if ferry_icon != null:
			check_true(ferry_icon.visible, "揭开后图标出现")
			check_true(
				str(ferry_icon.texture.resource_path).contains("_dim"),
				"本章去不了的地标用暗版图标：%s" % str(ferry_icon.texture.resource_path),
			)

	# 高亮：站到清风驿旁边才亮，站远了不亮
	var city_marker: Node2D = controller.world.get_node_or_null("Markers/Node_n_qingfengyi")
	check_not_null(city_marker, "地图上有清风驿地标位点")
	if city_marker != null:
		controller.player.global_position = city_marker.global_position
		controller._update_highlight()
		check_true(controller.highlight_visible(), "站在地标旁边有高亮")
		controller.player.global_position = city_marker.global_position + Vector2(400, 400)
		controller._update_highlight()
		check_false(controller.highlight_visible(), "走远了高亮消失")


## 设计 02：大地图在「切换小地图时」自动存档
func _check_save_on_enter(controller, state) -> void:
	var store = SaveStoreScript.new(SAVE_TEST_DIR, 3)
	store.ensure_dir()
	state.slot = 3
	controller.save_store_override = store
	controller._save_service = null
	var changes: Array = []
	controller.scene_change_handler = func(path: String) -> void: changes.append(path)
	controller.enter_local_map("scene_qingfengyi")
	check_eq(changes.size(), 1, "进入小地图会切场景")
	check_true(str(changes[0]).contains("local_run"), "切的是小地图场景：%s" % str(changes[0]))
	check_eq(controller.save_service().last_reason, "切换小地图", "切图前自动存档")
	check_true(controller.save_service().last_auto, "标记为自动存档")
	check_true(store.slot_exists(3), "自动存档真的落盘了")


## 潜行：移速 60% 之外，**被发现判定也要打折**（combat_const.sneak_detect_reduce = 0.4）
func _check_sneak(controller, captures: Array) -> void:
	var wolf = _enemy(controller, "sp_lp_lonewolf")
	check_not_null(wolf, "大地图上有追击型明雷（独目狼）")
	if wolf == null:
		return
	check_eq(str(wolf.behavior), "chase", "独目狼是追击型")
	check_gt(float(wolf.sneak_detect_reduce), 0.0, "控制层把潜行折扣注入了明雷")
	var full_radius: float = wolf.alert_radius * 32.0
	# 站到「警戒圈内、潜行后的判断圈外」的位置
	controller.player.global_position = wolf.global_position + Vector2(full_radius * 0.8, 0)
	check_gt(float(wolf.effective_alert_radius()), float(full_radius * 0.8), "潜行时有效半径缩小")
	controller.player.sneaking = true
	var before := captures.size()
	wolf.reset_latch()
	wolf._check_contact()
	check_eq(captures.size(), before, "潜行时站在警戒圈边缘不会被发现")
	controller.player.sneaking = false
	wolf.reset_latch()
	wolf._check_contact()
	check_eq(captures.size(), before + 1, "不潜行时同样位置就会被发现")
	if captures.size() > before:
		check_eq(str(captures[before].contact), "spotted", "被发现是 spotted（潜行失败进入追击流程）")
	# 潜行提示
	controller.player.sneaking = true
	controller._track_sneak()
	check_true(str(controller._status.text).contains("潜行中"), "状态栏提示潜行：%s" % controller._status.text)
	controller.player.sneaking = false
	controller._track_sneak()
	check_false(str(controller._status.text).contains("潜行中"), "退出潜行后提示恢复")


## 揭雾与驿站：走近地标自动揭开并写存档；走到驿站按 E 能开面板、传送与改难度
func _check_world_map(controller, state) -> void:
	check_not_null(controller.world_map, "大地图有揭开服务")
	check_true(controller.map_progress_text().contains("地图"), "HUD 显示已探索进度：%s" % controller.map_progress_text())
	check_true(controller.world_map.is_revealed("n_luoyanpo"), "落雁坡一开始就可见")
	# 揭雾在画面上要看得见：已经揭开的地标周围，雾格被擦掉
	var fog_before: int = controller.fog_cells()
	check_gt(float(fog_before), 0.0, "地图上有雾层（%d 格）" % fog_before)
	check_lt(
		float(fog_before), 3267.0,
		"初始揭开的地标周围已经擦掉一部分雾（剩 %d 格）" % fog_before,
	)

	# 走到塌陷山洞跟前 → 揭开（proximity_3）
	var cave: Node2D = controller.world.get_node_or_null("Markers/Node_n_cave_collapse")
	check_not_null(cave, "地图上有塌陷山洞位点")
	if cave != null:
		check_false(controller.world_map.is_revealed("n_cave_collapse"), "走近之前山洞是暗的")
		controller.player.global_position = cave.global_position + Vector2(0, 40)
		controller._check_reveals()
		check_true(controller.world_map.is_revealed("n_cave_collapse"), "走近后山洞揭开")
		check_true(state.is_node_revealed("n_cave_collapse"), "揭开状态写进存档")
		check_lt(float(controller.fog_cells()), float(fog_before), "揭开后雾又少了一些")
		check_true(controller.map_progress_text().contains("地图"), "HUD 仍然正常：%s" % controller.map_progress_text())

	# 驿站：站过去按 E
	var post: Node2D = controller.world.get_node_or_null("Markers/Node_n_post_station")
	controller.player.global_position = post.global_position
	check_true(controller.post_station_near(), "站到驿站旁边能认出来")
	var opened: Dictionary = controller.open_waypoint()
	check_true(bool(opened["ok"]), "按 E 打开驿站面板：%s" % opened.get("error", ""))
	check_not_null(controller.waypoint_panel, "驿站面板挂上了")
	if controller.waypoint_panel != null:
		var panel = controller.waypoint_panel
		check_true(panel.progress_text().contains("地图"), "面板显示探索进度：%s" % panel.progress_text())
		# 传送：把落点交给测试自己的回调，避免真去切场景
		var travelled: Array = []
		panel.travel_handler = func(scene_id: String, name: String) -> void: travelled.append("%s|%s" % [scene_id, name])
		var result: Dictionary = panel.press_travel("n_qingfengyi")
		check_true(bool(result["ok"]), "面板能传送到清风驿：%s" % result.get("error", ""))
		check_eq(travelled.size(), 1, "传送回调被调用一次：%s" % str(travelled))
		if not travelled.is_empty():
			check_eq(str(travelled[0]), "scene_qingfengyi|清风驿", "落点与名字都对")
		# 难度：普通已解锁、困难要先打大寨主
		check_true(bool(panel.press_difficulty("normal")["ok"]), "普通难度能切")
		var hard: Dictionary = panel.press_difficulty("hard")
		check_false(bool(hard["ok"]), "困难还没解锁")
		check_true(str(hard["error"]).contains("大寨主"), "写明差哪个 Boss：%s" % hard["error"])
	controller.close_waypoint()
	check_true(controller.waypoint_panel == null, "关掉后引用清空")

	# 事件判定：翻墙（敏 9 ≥ 9 过）、推门（力 5 < 8 失败）、追足迹（生存 1 < 3 失败）
	check_eq(controller.events.size(), 7, "大地图有 7 个事件判定位点：%s" % str(controller.events.size()))
	var wall: Node2D = controller.world.get_node_or_null("Markers/Event_ev_climb_wall")
	check_not_null(wall, "地图上有翻墙判定位点")
	if wall != null:
		controller.player.global_position = wall.global_position
		check_eq(controller.event_near_player(), "ev_climb_wall", "站到寨墙旁边认得出这条判定")
		var climbed: Dictionary = controller.resolve_event("ev_climb_wall")
		check_true(bool(climbed["success"]), "敏 9 够翻墙：%s" % climbed["text"])
		check_eq(state.event_check_result("ev_climb_wall"), "done", "翻墙结果写存档")
	var gate: Node2D = controller.world.get_node_or_null("Markers/Event_ev_force_gate")
	check_not_null(gate, "地图上有推门判定位点")
	if gate != null:
		controller.player.global_position = gate.global_position
		var pushed: Dictionary = controller.resolve_event("ev_force_gate")
		check_false(bool(pushed["success"]), "力 5 推不开木闩")
		check_true(str(pushed["text"]).contains("推不开"), "失败文案：%s" % pushed["text"])


func _enemy(controller, spawn_id: String):
	for enemy in controller.enemies:
		if enemy.spawn_id == spawn_id:
			return enemy
	return null


func _check_spawn(db, controller) -> void:
	check_eq(controller.enemies.size(), 17, "roaming_spawn 的 17 个明雷都生成")
	check_not_null(controller.player, "玩家在场")
	check_not_null(controller.camera, "相机在场")
	check_eq(controller.camera.limit_right, 1024, "相机沿用地图边界")

	var patrol = _enemy(controller, "sp_lp_patrol_01")
	check_not_null(patrol, "巡逻明雷在场")
	if patrol != null:
		check_eq(patrol.behavior, "patrol", "行为取自表")
		check_not_null(patrol.patrol_path, "巡逻明雷挂上了 Path2D")
	var wolf = _enemy(controller, "sp_lp_wolf_01")
	check_eq(wolf.behavior, "wander", "野狼是游荡")
	check_not_null(wolf.get_node_or_null("Body"), "明雷有占位外观")
	var sleeper = _enemy(controller, "sp_hc_sleeper")
	check_eq(sleeper.behavior, "sleep", "醉汉是沉睡")
	var lone = _enemy(controller, "sp_lp_lonewolf")
	check_true(bool(lone.is_elite), "独目狼是精英")
	check_float(lone.alert_radius, 6.0, "警戒半径取自表")
	check_not_null(lone.get_node_or_null("AlertRing"), "有警戒圈的可见范围")
	# 「看得见的状态」要和配表对得上：威胁色环（设计 02：玩家一眼能规划路线，靠的就是这个颜色）、
	# 精英个头、沉睡的半透明。这些是玩家判断「打不打得过 / 能不能绕开」的唯一依据，配错就是坑。
	var lone_body: Polygon2D = lone.get_node_or_null("Body")
	check_eq(
		lone_body.color, RoamingEnemyScript.THREAT_COLORS["red"],
		"威胁色环/身体颜色跟着队伍 threat_tag 走（独目狼是红）",
	)
	var wolf_body: Polygon2D = wolf.get_node_or_null("Body")
	check_eq(
		wolf_body.color, RoamingEnemyScript.THREAT_COLORS["green"],
		"野狼是绿（明显弱于我方）",
	)
	check_float(lone_body.polygon[0].x, -13.0, "精英占位块比普通明雷大（13 vs 10）")
	check_float(wolf_body.polygon[0].x, -10.0, "普通明雷块是 10")
	check_lt(sleeper.modulate.a, 1.0, "沉睡的明雷是半透明（可绕开的视觉信号）")
	check_float(wolf.modulate.a, 1.0, "非沉睡的明雷不透明")
	check_null(wolf.get_node_or_null("AlertRing"), "没有警戒半径的明雷不画色环")
	# 四种威胁色都要有对应配色，不然表里写个新 tag 会静默变成白色
	for tag: String in ["green", "yellow", "red", "purple"]:
		check_true(
			RoamingEnemyScript.THREAT_COLORS.has(tag),
			"威胁色 %s 有配色（设计 02 的绿/黄/红/紫）" % tag,
		)
	# 精英发光（设计 07）：精英挂光晕、普通明雷不挂
	check_true(lone.has_elite_glow(), "精英明雷挂上了发光标识")
	check_not_null(lone.get_node_or_null("EliteGlow"), "发光节点叫 EliteGlow（编辑器里能直接看到）")
	check_eq(lone.elite_marker_id(), "marker_elite_red", "地编的精英贴图 id 仍在表里（等美术到位换贴图）")
	check_false(wolf.has_elite_glow(), "普通明雷不发光")
	check_eq(wolf.elite_marker_id(), "", "普通明雷没有精英贴图 id")


func _check_contact(controller, captures: Array) -> void:
	# 正面贴上野狼 → front
	var wolf = _enemy(controller, "sp_lp_wolf_01")
	controller.player.global_position = wolf.global_position + Vector2(0, 8)
	wolf.reset_latch()
	wolf._check_contact()
	check_eq(captures.size(), 1, "贴上明雷会触发遭遇")
	if captures.size() >= 1:
		check_eq(captures[0].contact, "front", "从正面贴上算正面遭遇")
		check_eq(captures[0].team_id, "team_wolf_pack", "遭遇带上队伍")

	# 从上方贴近沉睡的醉汉 → 奇袭
	var sleeper = _enemy(controller, "sp_hc_sleeper")
	controller.player.global_position = sleeper.global_position + Vector2(0, -8)
	sleeper.reset_latch()
	sleeper._check_contact()
	check_eq(captures.size(), 2, "第二次接触也会触发")
	if captures.size() >= 2:
		check_eq(captures[1].contact, "ambush_sleep", "背后贴沉睡敌人算奇袭")


func _check_neutral(controller, captures: Array) -> void:
	var herbalist = _enemy(controller, "sp_lp_herbalist")
	if herbalist == null:
		fail("中立明雷没生成")
		return
	var before := captures.size()
	controller.player.global_position = herbalist.global_position + Vector2(0, 6)
	herbalist.reset_latch()
	herbalist._check_contact()
	check_eq(captures.size(), before, "中立明雷（采药人）贴身也不开战")


## 完整闭环：遭遇 → 战斗 → 奖励落账 → 明雷被清
func _check_loop(db, controller, captures: Array, state) -> void:
	if captures.is_empty():
		fail("没有可用的遭遇")
		return
	var encounter = captures[0]
	var session_node = controller.session()
	check_not_null(session_node, "GameSession 在场")
	session_node.pending_encounter = encounter
	var exp_before := int(state.party_exp)

	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	# 固定种子：这场战斗会写经验与「明雷已清」，随机种子会让后面的断言时红时绿
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	check_eq(battle.enemies.size(), 3, "战斗按队伍配置出 3 只野狼")
	var guard := 0
	while not battle.finished() and guard < 120:
		var actor = battle.sim.current_actor() if battle.sim.in_round() else null
		if actor != null and battle.allies.has(actor):
			var skills: Array = battle.sim.available_skills(actor)
			if skills.is_empty():
				battle.press_next_round()
			else:
				battle.press_skill(str(skills[0].skill_id))
		else:
			battle.press_next_round()
		guard += 1
	check_true(battle.finished(), "战斗会结束")
	check_true(battle.status_text() != "" or battle.result_label_text() != "", "结束时有结果文案")
	check_gt(float(int(state.party_exp)), float(exp_before), "经验落进队伍池")
	# 战斗外气血（v11）：打完这一场，剩余气血要写回存档（下一场带着伤上）
	check_gt(
		float(state.current_hp_of(str(battle.allies[0].actor_id))), 0.0,
		"打完把剩余气血写回存档（%d）" % state.current_hp_of(str(battle.allies[0].actor_id)),
	)
	check_true(session_node.cleared_spawns.has(encounter.spawn_id), "打完把明雷标成已清")
	check_true(str(battle.result_label_text()).contains("胜利"), "胜利文案：%s" % battle.result_label_text())
	scene_tree.root.remove_child(battle)
	battle.free()


## 撤退（或败北）后回到大地图：上次的接触点在明雷身边，落点必须被推开，
## 否则同一只明雷会瞬间再抓一次，"跑得掉"就是空话。
func _check_retreat_push_out(db, state) -> void:
	var probe = load(OVERWORLD_RUN).instantiate()
	probe.state_override = state
	scene_tree.root.add_child(probe)
	probe.setup()
	var live = null
	for enemy in probe.enemies:
		if not enemy.defeated:
			live = enemy
			break
	check_not_null(live, "地图上有还没清的明雷")
	if live == null:
		scene_tree.root.remove_child(probe)
		probe.free()
		return
	var spot: Vector2 = live.global_position
	var spawn_id := str(live.spawn_id)
	scene_tree.root.remove_child(probe)
	probe.free()

	# 把「上次遭遇点」摆到明雷身上，重开一张大地图
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	if session_node != null:
		session_node.world_position = spot
	var again = load(OVERWORLD_RUN).instantiate()
	again.state_override = state
	scene_tree.root.add_child(again)
	again.setup()
	var nearest := 100000.0
	for enemy in again.enemies:
		if enemy.defeated:
			continue
		nearest = minf(nearest, again.player.global_position.distance_to(enemy.global_position))
	check_gt(nearest, 22.0, "落点在明雷身上时会被推开（最近 %.1f px）" % nearest)
	check_true(str(again._status.text).contains("脱身"), "状态栏说明为什么退开：%s" % again._status.text)
	# 推开之后不该还存着上次那个落点，否则每次回图都从明雷身上开始
	if session_node != null:
		session_node.world_position = Vector2.ZERO
	scene_tree.root.remove_child(again)
	again.free()
	check_ne(spawn_id, "", "拿到的是一只真实存在的明雷")


## 刷新：`respawn_sec` 到期**回原位**（设计 02「刷新」）。两条路径都要成立：
##   ① 留在图上待够时间 → 自己回来（倒计时在 `_physics_process` 里）；
##   ② 期间回过大地图 → 重建时按绝对时间戳补出来（过期就当没清过）。
## 以前 `mark_defeated()` 无条件 `set_physics_process(false)`，把①的倒计时一起关掉了——
## 只有换图重建才刷得出来，「在图上等够时间」永远不成立。
func _check_respawn(db, controller, state) -> void:
	var enemy = _enemy(controller, "sp_lp_wolf_01")
	check_not_null(enemy, "有用来验刷新的明雷")
	if enemy == null:
		return
	var home: Vector2 = enemy.home
	enemy.global_position = home + Vector2(60, 0)   # 先挪开：回来必须是**原位**，不是被打倒的地方
	enemy.mark_defeated(2)
	check_true(enemy.defeated, "打完先进入已清状态")
	check_false(enemy.visible, "已清的明雷不可见")
	check_true(enemy.is_physics_processing(), "已清但还没到点：倒计时要继续跑（关掉物理处理就永远不刷新）")
	# 正好推 2 秒（respawn_left 2.0 → 1.0 → 0.0）就停：**别多推一帧**。
	# 复活那一帧函数里 `return` 了，多推的那一帧会跑 AI 的 `move_and_slide()`——
	# 手动调 `_physics_process` 时不在引擎的物理步内，会报 `body->get_space() is null`（踩过）。
	# 本条用例验的是刷新计时，不验走位（走位由别的用例覆盖）。
	for i in range(2):
		enemy._physics_process(1.0)
	check_false(enemy.defeated, "留在图上待够 respawn_sec，明雷自己回来")
	check_true(enemy.visible, "回来的明雷可见")
	check_lt(enemy.global_position.distance_to(home), 1.0, "回来站在原位")

	# respawn_sec=0（不刷新）的明雷不会自己回来
	enemy.mark_defeated(0)
	check_false(enemy.is_physics_processing(), "respawn_sec=0 表示不刷新，连倒计时都不跑")
	for i in range(3):
		enemy._physics_process(1.0)
	check_true(enemy.defeated, "respawn_sec=0 表示不刷新，等多久都不回来")

	# ② 跨图重建：已清时间戳过期 → 当成没清过，明雷在原位出现
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	session_node.cleared_spawns["sp_lp_wolf_02"] = int(Time.get_unix_time_from_system()) - 1
	var again = load(OVERWORLD_RUN).instantiate()
	again.state_override = state
	scene_tree.root.add_child(again)
	again.setup()
	var back = _enemy(again, "sp_lp_wolf_02")
	check_not_null(back, "重建的大地图里有这只明雷")
	if back != null:
		check_false(back.defeated, "已清时间戳过期 → 重建时它回来了")
		check_lt(back.global_position.distance_to(back.home), 1.0, "重建后也在原位")
	# 到期的记录要被**删掉**，别留着：留着的话 `cleared_spawns.has(id)` 还是真，
	# 而明雷其实已经站在场上了——「已清」和「在场上」两种状态同时成立，读它的人就被骗了。
	check_false(
		session_node.cleared_spawns.has("sp_lp_wolf_02"),
		"过期的「已清」记录被清掉了（不再同时是「已清」和「在场上」）",
	)
	scene_tree.root.remove_child(again)
	again.free()


## 到期那一瞬间玩家正站在位点上：不能「凭空出现在脚下、同一帧就开战」——
## 得先走出接触圈才恢复判定（与地图出口「先走出去一次」同一套做法）。
## 起因与当年「败北后被同一队立刻再抓」同类：明雷回来了，玩家却连看都没看见。
func _check_respawn_under_player(db, controller) -> void:
	var enemy = _enemy(controller, "sp_lp_wolf_01")
	check_not_null(enemy, "有用来验重生站位的明雷")
	if enemy == null:
		return
	var captured: Array = []
	enemy.encountered.connect(func(_spawn_id: String, contact: String) -> void: captured.append(contact))
	enemy.player = controller.player
	enemy.mark_defeated(1)
	controller.player.global_position = enemy.home      # 玩家就站在位点上等它回来
	enemy._physics_process(1.0)                         # 倒计时到 → 重生（这一帧里 return，不跑 AI）
	check_false(enemy.defeated, "明雷按时间回来了")
	check_false(bool(enemy._contact_armed), "重生时先不武装接触判定")
	enemy._check_contact()
	check_eq(captured.size(), 0, "站在重生点上不会当场开战")

	# 走出接触圈再回来：恢复正常判定
	controller.player.global_position = enemy.home + Vector2(500, 0)
	enemy._check_contact()
	check_true(bool(enemy._contact_armed), "走出接触圈后恢复武装")
	controller.player.global_position = enemy.home
	enemy._check_contact()
	check_eq(captured.size(), 1, "走出去一次之后再撞上才会开战")

	# 反向风险：别让「先不武装」变成「永远不武装」——玩家离得远（最常见的情况）时，
	# 重生后第一帧就该恢复武装，这只明雷照常能开战（不然它就成了地图上一块摆设）。
	captured.clear()
	enemy.mark_defeated(1)
	controller.player.global_position = enemy.home + Vector2(600, 0)
	enemy._physics_process(1.0)
	check_false(enemy.defeated, "第二次按时间回来")
	enemy._check_contact()
	check_true(bool(enemy._contact_armed), "玩家离得远时，重生后立刻恢复武装")
	controller.player.global_position = enemy.home
	enemy._check_contact()
	check_eq(captured.size(), 1, "离得远重生后照常能开战（不是摆设）")


## 已清的明雷在新的大地图实例里不再出现（respawn_sec 之前）
func _check_cleared(db, state) -> void:
	var controller = load(OVERWORLD_RUN).instantiate()
	controller.state_override = state
	scene_tree.root.add_child(controller)
	controller.setup()
	var cleared_count := 0
	for enemy in controller.enemies:
		if enemy.defeated:
			cleared_count += 1
	check_gt(float(cleared_count), 0.0, "回到大地图后已清的明雷是隐藏状态")
	scene_tree.root.remove_child(controller)
	controller.free()
