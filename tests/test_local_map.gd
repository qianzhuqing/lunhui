## 小地图与副本：房间敌人、宝箱、隐藏触发、出口与回大地图的刷新。
extends "res://tests/test_case.gd"

const LOCAL_RUN := "res://scenes/local_run.tscn"
const GameStateScript := preload("res://src/core/game_state.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const RoamingEnemyScript := preload("res://src/world/roaming_enemy.gd")
const ChestScript := preload("res://src/world/chest.gd")
const SfxScript := preload("res://src/audio/sfx.gd")
const TriggerPointScript := preload("res://src/world/trigger_point.gd")
const PracticeServiceScript := preload("res://src/core/practice_service.gd")
## NPC 位点的「位点名 → 人」判定只有一处（`npc_for_slot`），用例直接问它（决策 332）
const NpcServiceScript := preload("res://src/core/npc_service.gd")
## 观察点的可见标记（设计 15 §一「可交互物暖色提亮」，决策 337）
const FlavorMarkerScript := preload("res://src/world/flavor_marker.gd")
## 剧情节点的记账旗标名（`story_done_<node_id>`）只有一处出处
const StoryServiceScript := preload("res://src/core/story_service.gd")
## 引导口径（`flag_supplies_ready` 那类「写在表里、没人置」的旗标）只有一处出处
const GuideServiceScript := preload("res://src/core/guide_service.gd")
## 界面文案判据（表内 id 形态）与场景自检用的是同一处常量，别在用例里另写一份正则
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
## 遮挡层判据与那个透明度常量都在控制器里（唯一出处），用例直接引用它
const LocalMapControllerScript := preload("res://src/world/local_map_controller.gd")

## 写不进去的存档设施（只实现 SaveService 用到的几个方法）：验「自动存档失败不静默」
class _FailingStore extends RefCounted:
	func ensure_dir() -> void:
		pass

	func save_slot(_slot: int, _state) -> Dictionary:
		return {"ok": false, "error": "测试注入：盘写不进去"}

	func slot_path(_slot: int) -> String:
		return ""

const SAVE_TEST_DIR := "res://.logs/test_save_timing/run_map"


func suite_name() -> String:
	return "小地图与副本"


func run() -> void:
	if scene_tree == null:
		fail("没有注入场景树")
		return
	var db = get_db()
	var state = solo_state(db)
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	check_not_null(session_node, "GameSession 在场")
	session_node.set_state(state)
	session_node.pending_local_scene = "scene_heifengzhai"

	var map = load(LOCAL_RUN).instantiate()
	map.state_override = state
	# 自动存档要真的落盘才谈得上「什么时机存的」：开头就给槽位 + 临时目录。
	# （以前槽位一直是 0，前面那些自动存档全在空转，只有 `_check_save_timing` 里那几次真写了。）
	var store = SaveStoreScript.new(SAVE_TEST_DIR, 3)
	store.ensure_dir()
	state.slot = 2
	map.save_store_override = store
	var captures: Array = []
	map.battle_switch_handler = func(encounter) -> void: captures.append(encounter)
	var returns: Array = []
	map.return_handler = func() -> void: returns.append(true)
	scene_tree.root.add_child(map)
	map.setup()

	_check_spawn(map)
	_check_floor_hud(map, db)
	_check_room_encounter(map, captures)
	_check_chest(db, map, state)
	_check_chest_pity(map, state)
	_check_chest_overflow_copy(map)
	_check_trigger_money_reward(db, map, state)
	_check_triggers(map, state, captures)
	_check_sequence_puzzle(map, state)
	_check_event_boss_reward(db, map, state, captures)
	_check_kill_style_names(map, db)
	_check_flavor_points(map, state)
	_check_player_text(map, db, session_node)
	_check_condition_triggers(map, state, session_node)
	_check_progress_records(map, state)
	_check_dungeon_panel(map, state)
	_check_events(map, state)
	_check_stealth_trigger(map, state, session_node)
	_check_once_only_triggers(state, session_node)
	_check_save_timing(map, state, session_node)
	_check_exit(map, session_node, returns)
	_check_town_shop(db, state, session_node)
	_check_guide_after_shop_close(db, session_node)
	_check_town_facilities(db, state, session_node)
	_check_every_facility_is_interactable(db, session_node)
	_check_town_recruit_at_inn(db, session_node)
	_check_guarded_chest(db, session_node)
	_check_dummy_practice_entry(db, session_node)
	_check_town_shelter_and_field_hud(db, session_node)
	_check_town_gamble(db, state, session_node)
	_check_spawn_and_exit_guard(db, state, session_node)
	_check_cave_shortcut(db, state, session_node)
	_check_battle_return_position(db, state, session_node)
	_check_choice_dialogue_after_boss(db, map, state, session_node)
	_check_event_dialogue(db, map, state)
	_check_shixi_layers(db, state, session_node)
	scene_tree.root.remove_child(map)
	map.free()
	session_node.pending_local_scene = ""


## 0.32.0 的两条地表分层，在**真控制器**里各走一遍（石隙迷窟是唯一同时用到两层的图）：
##   ① `Conditional` 层（那条 22 格碎石细径）跟着 `item_treasure_map` **整层**显隐；
##   ② 玩家走到遮挡瓦片（岩檐）下面时 `Overlay` 整层半透明，走开恢复。
##
## 条件**不写死在代码里**：控制器问的是「这张图的母地标是不是持有类解锁」（`n_shixi` → 藏宝图）。
## 地编还没交付 `Conditional` 层时，这里临时挂一个空层验接线（不然那几条断言一次都跑不到）。
func _check_shixi_layers(db, _state, session_node) -> void:
	session_node.pending_local_scene = "scene_shixi"
	var shixi = load(LOCAL_RUN).instantiate()
	# 用**独立的一份存档**：藏宝图是钥匙道具（`Inventory.remove_item` 拒删），
	# 拿主存档验「没有图」那一侧会把前提带歪（参数里那份 `state` 这一段不用）。
	var shixi_state = solo_state(db)
	shixi.state_override = shixi_state
	scene_tree.root.add_child(shixi)
	shixi.setup()
	check_eq(shixi.scene_id, "scene_shixi", "进入石隙迷窟")

	# ① 条件地表的判据：按表问出来（`map_region.n_shixi.unlock_condition`）
	var rule: Dictionary = shixi.conditional_layer_rule()
	check_true(bool(rule["apply"]), "石隙的条件地表有条件可依：%s" % str(rule))
	check_eq("、".join(rule["items"]), "item_treasure_map", "条件是藏宝图（写在 map_region 里，不在代码里）")
	check_false(shixi.conditional_layer_visible(), "没图 → 细径不该显示")

	var layer: TileMapLayer = shixi.world.get_node_or_null("Conditional") as TileMapLayer
	if layer == null:
		# 地编还没铺那 22 格：临时挂一个空层，验「代码真的会去显隐这一层」
		layer = TileMapLayer.new()
		layer.name = "Conditional"
		shixi.world.add_child(layer)
		shixi._conditional_layer = layer
		print("  [小地图] 石隙还没有 Conditional 层（等地编铺 22 格细径），用临时空层验显隐接线")
	shixi._apply_conditional_layer()
	check_false(layer.visible, "没图 → 这一层被藏起来")
	var added: Dictionary = shixi_state.inventory.add_item(db, "item_treasure_map", 1)
	check_true(bool(added.get("ok", false)), "藏宝图能放进背包：%s" % str(added.get("error", "")))
	check_true(shixi.conditional_layer_visible(), "拿到图 → 细径该显示")
	shixi._apply_conditional_layer()   # 正常路径是 `_process` 里每帧刷（拿到图当场就出现）
	check_true(layer.visible, "拿到图 → 这一层真的显示出来了")
	# 再回到隐藏：藏宝图是**钥匙道具**，`Inventory.remove_item` 故意拒删（钥匙拒丢），
	# 所以换一份干净存档问同一层——它和背包是「有图才显示」的两侧。
	shixi.state_override = solo_state(db)
	check_false(shixi.conditional_layer_visible(), "换一份没有图的存档 → 细径不该显示")
	shixi._apply_conditional_layer()
	check_false(layer.visible, "图没了 → 这一层又藏回去")

	# ② 遮挡层半透明：按 TileSet 的 `covering` 找一格岩檐站上去
	var overlay: TileMapLayer = shixi.world.get_node_or_null("Overlay") as TileMapLayer
	check_not_null(overlay, "石隙有 Overlay 层")
	if overlay != null:
		var eave_cell := Vector2i(-1, -1)
		var plain_cell := Vector2i(-1, -1)
		for cell: Vector2i in overlay.get_used_cells():
			var data: TileData = overlay.get_cell_tile_data(cell)
			if data != null and bool(data.get_custom_data("covering")):
				if eave_cell.x < 0:
					eave_cell = cell
			elif plain_cell.x < 0:
				plain_cell = cell
		check_true(eave_cell.x >= 0, "石隙的 Overlay 上有遮挡瓦片（岩檐压顶）")
		if eave_cell.x >= 0:
			shixi.player.global_position = overlay.to_global(overlay.map_to_local(eave_cell))
			shixi._refresh_overlay_cover()
			check_true(shixi._overlay_covering, "站在岩檐下面判为「被盖住」")
			check_float(overlay.modulate.a, LocalMapControllerScript.OVERLAY_COVER_ALPHA,
				"被盖住时整层压到 %.2f" % LocalMapControllerScript.OVERLAY_COVER_ALPHA)
			shixi.player.global_position = overlay.to_global(overlay.map_to_local(plain_cell))
			shixi._refresh_overlay_cover()
			check_false(shixi._overlay_covering, "走开就不算被盖住")
			check_float(overlay.modulate.a, 1.0, "走开后恢复不透明")

	scene_tree.root.remove_child(shixi)
	shixi.free()
	session_node.pending_local_scene = ""


## 判定过了接一段对话（设计 20 §四 幕四）：地牢那次判定（`ev_shen_rescue`）通过之后，
## 铁栏后的人才回头说话——以前这里只置一个旗标，她一句台词都没有。
## 走的是**真路径**（`resolve_event`，按 E 那一下调的就是它），不是直接调内部函数。
func _check_event_dialogue(db, map, state) -> void:
	var cell: Node2D = map.world.get_node_or_null("Markers/hf1_cell/Event_ev_shen_rescue")
	check_not_null(cell, "地牢里有沈雁回的判定位点")
	if cell == null:
		return
	map.close_npc()
	map.player.global_position = cell.global_position
	# 书生的文学判定值是 5 ≥ 门槛 2（soft 达标必过）→ 这一条是确定性的
	var result: Dictionary = map.resolve_event("ev_shen_rescue")
	check_true(bool(result.get("success", false)), "地牢那次判定过得去：%s" % str(result.get("text", "")))
	check_true(state.has_flag("flag_shen_rescued"), "判定过了仍然置旗标（章节结束等它）")
	var panel = map.npc_panel
	check_not_null(panel, "判定过了接上她那段对话")
	if panel != null:
		check_eq(str(panel.npc_id), "npc_shen_yanhui", "说话人是沈雁回")
		check_eq(str(panel.dialogue_node_id), "dl_shen_cell", "直接接上幕四那一句")
		check_not_null(
			panel._actions.find_child("TalkOptionopt_shen_take", true, false),
			"「我带你走。」是一条真按钮",
		)
		map.close_npc()


## 终局难题（设计 20 §七）：上一场打赢大寨主 → 回到图上就把那段对话摆出来。
##
## 三层一起验：`battle_screen` 把这一场的 `team_id` 写进 `last_battle`；小地图按队伍翻
## `TEAM_WIN_DIALOGUES`；**能不能问、问过没有写在表的条件里**（三个 `!flag_ledger_*`）。
## 这条链以前整条不存在——三个账册旗标全项目没有来源，`story_node` 那三行永久增益谁也拿不到。
##
## 用的是**已经在场的那张地图**（不另起一份场景）：只在它上面临时改会话状态，
## 跑完把自己动过的旗标与 `last_battle` 还原，免得后面的用例看见一份被污染的状态。
func _check_choice_dialogue_after_boss(db, map, state, session_node) -> void:
	var had_confront: bool = state.has_flag("flag_heifeng_confront")
	var had_last: Variant = session_node.last_battle
	# ① 没打过对质 → 不摆
	state.flags.erase("flag_heifeng_confront")
	session_node.last_battle = {"winner": 0, "team_id": "team_boss"}
	check_false(map._open_pending_choice_dialogue(), "没打赢大寨主时不摆终局难题")
	check_false(
		map.npc_panel != null and str(map.npc_panel.dialogue_node_id) == "dl_ledger_choice",
		"而且没把终局难题那一句塞进面板",
	)
	# ①′ 认得出队伍、但那一队没配对话 → 也不摆（别把「上一场打的是别人」也算进来）
	session_node.last_battle = {"winner": 0, "team_id": "team_butcher"}
	check_false(map._open_pending_choice_dialogue(), "上一场打赢的是别的队伍 → 不摆终局难题")
	# ② 打赢过 + 没选过 → 摆，而且就是表里那一条
	state.set_flag("flag_heifeng_confront")
	# 账册也是条件的一部分（表里写了 `item:item_bd_ledger`）：打赢这一场时它是随掉落发的
	# （`battle_screen.TEAM_WIN_ITEMS`），这里补上，验「账册在手 → 摆出来」
	state.inventory.add_item(db, "item_bd_ledger", 1)
	session_node.last_battle = {"winner": 0, "team_id": "team_boss"}
	check_true(map._open_pending_choice_dialogue(), "打赢大寨主之后回图 → 终局难题摆出来")
	var panel = map.npc_panel
	check_not_null(panel, "摆出来的是一块 NPC 面板（对话容器挂在它上面）")
	if panel != null:
		check_eq(str(panel.npc_id), "npc_shen_yanhui", "问话的是沈雁回")
		check_eq(str(panel.dialogue_node_id), "dl_ledger_choice", "直接说的就是终局难题那一条")
		check_not_null(
			panel._actions.find_child("TalkOptionopt_ledger_public", true, false),
			"三条路之一（呈官）是一条真按钮",
		)
	# ②′ 读档／重开一局时 `last_battle` 不在会话里：条件满足就必须还问得出来，
	#     否则玩家一读档就再也回答不了那道题（那本账还在背包里，题却没了）
	map.close_npc()
	session_node.last_battle = {}
	check_true(
		map._open_pending_choice_dialogue(),
		"读档进来（认不出上一场是哪支队）也照样摆——不然那道题永远答不了",
	)
	var reloaded = map.npc_panel
	check_not_null(reloaded, "读档那一路同样摆出面板")
	if reloaded != null:
		# ③ 选一条 → 旗标落地；再回图就不摆了（条件里那三个 `!flag_ledger_*` 挡住）
		var chosen: Dictionary = reloaded.choose_dialogue("opt_ledger_public")
		check_true(bool(chosen.get("ok", false)), "按钮选得动：%s" % str(chosen.get("error", "")))
		check_true(state.has_flag("flag_ledger_public"), "选完旗标进存档")
		map.close_npc()
		check_false(map._open_pending_choice_dialogue(), "选过之后回图不再摆（三选一不叠加）")
	# 还原：后面的用例用同一份会话状态，不能留下这一场动过的旗标
	state.flags.erase("flag_ledger_public")
	# 钥匙道具不能「丢」，但这是测试夹具——直接清掉那份堆叠
	state.inventory.stacks.erase("item_bd_ledger")
	if had_confront:
		state.set_flag("flag_heifeng_confront")
	else:
		state.flags.erase("flag_heifeng_confront")
	session_node.last_battle = had_last


## 城镇店的入口：走到建筑 Marker 旁边按 E → 商店界面盖在当前场景上（不切场景）
## completion（宝箱全开）与 kill_style（毒杀）两类隐藏触发
func _check_condition_triggers(map, state, session_node) -> void:
	var chest_trigger = null
	var poison_trigger = null
	for point in map.triggers:
		if point.trigger_id == "trig_chest_all":
			chest_trigger = point
		if point.trigger_id == "trig_poison_kill":
			poison_trigger = point
	check_not_null(chest_trigger, "有「宝箱全开」触发点")
	check_not_null(poison_trigger, "有「毒杀毒手」触发点")

	if chest_trigger != null:
		# 还没开完：给出还差几个，不算触发
		map.player.global_position = chest_trigger.global_position
		var early: Dictionary = map.interact()
		check_false(bool(early["ok"]), "宝箱没开全时不能触发")
		check_true(str(map._status.text).contains("前寨宝箱"), "提示还差几个前寨宝箱：%s" % map._status.text)
		# 计数范围：只算**前寨（第 1 层）**且不在门后那间的宝箱。
		# 以前这里直接 `for chest in map.chests: _open_chest(chest)` —— 把门后面的暗格银箱也强行打开了，
		# 于是「全开」条件看着能过，真实玩法里那条条件根本凑不齐（门后那箱要等门开）。
		var gate: Array = map._completion_gate_chests(chest_trigger.row)
		check_gt(float(gate.size()), 0.0, "「宝箱全开」有可数的前寨宝箱（%d 个）" % gate.size())
		check_lt(float(gate.size()), float(map.chests.size()), "计数不包含门后那间（全图 %d 个）" % map.chests.size())
		SfxScript.clear_log()
		for chest in map.chests:
			# 真实的开门流程只需要开前寨那几个；门后那箱要进去才拿得到
			if not chest.opened_already and gate.has(chest):
				map._open_chest(chest)
		check_true(SfxScript.has_played("chest"), "开箱会请求「宝箱开启」音效（07 §8.4）")
		map.player.global_position = chest_trigger.global_position
		var opened: Dictionary = map.interact()
		check_true(bool(opened["ok"]), "前寨宝箱全开后触发成功：%s" % map._status.text)
		# 门要真的开：reward_type=room → 人被带进暗格（以前只播一句「隐藏门浮现」，门没开）
		check_eq(map.current_room_id(), "hf1_secret", "隐藏门开了、人被带进「暗格」：%s" % map._status.text)
		check_true(str(map._status.text).contains("暗格"), "文案说清去的是哪间：%s" % map._status.text)

	if poison_trigger != null:
		state.inventory.add_item(map.db, "item_poison_wine", 1)
		# 上一场是正面打死的 → 不算毒杀
		session_node.last_battle = {"kill_styles": {"en_bd_poison_hand": "normal"}}
		map.player.global_position = poison_trigger.global_position
		var wrong: Dictionary = map.interact()
		check_false(bool(wrong["ok"]), "正面击杀不算毒杀")
		check_true(str(map._status.text).contains("毒"), "提示要用毒杀：%s" % map._status.text)
		check_eq(state.inventory.count("item_scroll_wudu"), 0, "没达成条件不给秘籍")
		# 上一场是毒死的 → 给独门秘籍
		session_node.last_battle = {"kill_styles": {"en_bd_poison_hand": "poison"}}
		var poisoned: Dictionary = map.interact()
		check_true(bool(poisoned["ok"]), "毒杀后触发成功：%s" % map._status.text)
		check_eq(state.inventory.count("item_scroll_wudu"), 1, "拿到五毒秘籍残页")


## 存档时机：副本内自动存（开箱/触发/事件/扫荡），手动存只能在城镇
func _check_save_timing(map, state, session_node) -> void:
	# ① 自动存档**失败**不静默（2026-10-03 补）：注入一个写不进去的存档设施，
	#    失败不打断游玩，但状态栏要有「自动存档失败：…」这句（玩家的环境真有可能写不进 user://saves）。
	var working_store = map.save_store_override
	map.save_store_override = _FailingStore.new()
	map._save_service = null
	var failed: Dictionary = map.autosave("测试注入")
	check_false(bool(failed["ok"]), "写不进去时 autosave 返回失败")
	check_true(str(map._status.text).contains("自动存档失败"), "状态栏如实写失败：%s" % map._status.text)
	check_true(str(map._status.text).contains("只在本局里"), "并说明后果")
	map.save_store_override = working_store
	map._save_service = null

	var store = SaveStoreScript.new(SAVE_TEST_DIR, 3)
	store.ensure_dir()
	state.slot = 2
	map.save_store_override = store
	map._save_service = null     # 让控制器用新注入的 store 重建
	# 副本里不能手动存档
	var refused: Dictionary = map.press_save()
	check_false(bool(refused["ok"]), "副本里不能手动存档")
	check_true(str(map._status.text).contains("城镇才是存档点"), "说明去哪存：%s" % map._status.text)
	# 副本内的自动存档：做题字判定（成功）之后应该落盘
	var inscription: Node2D = map.world.get_node_or_null("Markers/hf2_hall/Event_ev_hall_inscription")
	check_not_null(inscription, "有题字位点")
	if inscription != null:
		map.player.global_position = inscription.global_position
		map.interact()
		check_eq(map.save_service().last_reason, "事件判定", "事件判定后自动存档")
		check_true(store.slot_exists(2), "自动存档真的落盘了")

	# 城镇是存档点：换一张图（清风驿）手动存档成功
	session_node.pending_local_scene = "scene_qingfengyi"
	var town = load(LOCAL_RUN).instantiate()
	town.state_override = state
	town.save_store_override = store
	scene_tree.root.add_child(town)
	town.setup()
	check_true(town.can_save_here(), "清风驿是存档点")
	var saved: Dictionary = town.press_save()
	check_true(bool(saved["ok"]), "城镇能手动存档：%s" % saved.get("error", ""))
	check_eq(town.save_service().last_reason, "城镇存档点", "记下存档原因")
	scene_tree.root.remove_child(town)
	town.free()
	session_node.pending_local_scene = ""


## 「潜行入寨」：behavior 类触发点（flag_stealth_full=1）
func _check_stealth_trigger(map, state, session_node) -> void:
	var stealth = null
	for point in map.triggers:
		if point.trigger_id == "trig_stealth_clear":
			stealth = point
	check_not_null(stealth, "有潜行入寨的触发点")
	if stealth == null:
		return
	check_true(stealth.supported(), "behavior 类触发点现在支持了（只剩 sequence 未做）")
	# 打过房间就会标记「潜行失败」
	var broken: bool = bool(map.local_state().get("stealth_broken", false))
	check_true(broken, "撞上房间守卫会标记潜行失败")
	map.player.global_position = stealth.global_position
	var refused: Dictionary = map.interact()
	check_false(bool(refused["ok"]), "潜行失败后不能触发潜行入寨")
	check_true(str(map._status.text).contains("潜行已经失败"), "说明原因：%s" % map._status.text)
	# 清掉失败标记（等价于出图重进）就能触发，奖励是剧情旗标
	map.local_state()["stealth_broken"] = false
	var cleared: Dictionary = map.interact()
	check_true(bool(cleared["ok"]), "全程没惊动守卫就能触发：%s" % map._status.text)
	check_true(state.has_flag("event_stealth_reward"), "奖励的剧情旗标记下了")


## once_only 的触发点看**永久记录**：离图（会话清空）再回来不该能重复拿奖励；
## 非一次性点位只按这次进图的会话状态判，回来还能再触发
func _check_once_only_triggers(state, session_node) -> void:
	state.record_dungeon("scene_heifengzhai", "triggers", "trig_rusty_sword")
	# 模拟「回大地图整片刷新」：会话里的已用触发点清空
	var session_maps: Dictionary = session_node.local_maps
	session_maps.erase("scene_heifengzhai")
	session_node.local_maps = session_maps

	var again = load(LOCAL_RUN).instantiate()
	again.state_override = state
	session_node.pending_local_scene = "scene_heifengzhai"
	scene_tree.root.add_child(again)
	again.setup()
	var rusty_used := false
	var wine_used := false
	for point in again.triggers:
		if point.trigger_id == "trig_rusty_sword":
			rusty_used = point.used
		elif point.trigger_id == "trig_wine":
			wine_used = point.used
	check_true(rusty_used, "once_only 的锈剑共鸣离图再回也不可用")
	check_false(wine_used, "非一次性点位回来还能再触发")
	scene_tree.root.remove_child(again)
	again.free()
	session_node.pending_local_scene = ""


## 完成度界面入口：副本里按 M 打开，扫荡按钮走小地图控制器的 sweep_floor
func _check_dungeon_panel(map, state) -> void:
	var opened: Dictionary = map.open_dungeon_panel()
	check_true(bool(opened["ok"]), "副本里能打开完成度界面：%s" % opened.get("error", ""))
	check_not_null(map.dungeon_panel, "完成度面板挂上了")
	if map.dungeon_panel == null:
		return
	check_eq(str(map.dungeon_panel.scene_id), "scene_heifengzhai", "面板对着当前这张图")
	check_true(map.dungeon_panel.detail_text().contains("宝箱"), "面板显示四项完成度：%s" % map.dungeon_panel.detail_text())
	# 第 1 层前面已经记成打过，界面里应该可扫荡；扫一次真的走控制器的 sweep_floor
	state.record_dungeon("scene_heifengzhai", "rooms", "hf1_yard")
	map.dungeon_panel.refresh()
	var swept: Dictionary = map.dungeon_panel.press_sweep(1)
	check_true(bool(swept["ok"]), "面板里能扫荡已通关的层：%s" % map._status.text)
	check_eq(map.save_service().last_reason, "扫荡第 1 层", "扫荡后自动存档")
	check_true(str(map.dungeon_panel.status_text()).contains("扫荡第 1 层"), "面板上播出扫荡结果：%s" % map.dungeon_panel.status_text())
	map.close_dungeon_panel()
	check_true(map.dungeon_panel == null, "关掉后引用清空")


## 事件判定位点：站过去按 E，判定值与难度比大小；结果写存档
func _check_events(map, state) -> void:
	# 0.29.0 加了 `ev_shen_rescue`（地牢里救沈雁回），地编同步摆了 `Event_ev_shen_rescue`
	check_eq(map.events.size(), 5, "黑风寨有 5 个事件判定位点：%s" % str(map.events))
	# 识破柴房机关：奇门 3 ≥ 3，恰好过
	var trap: Node2D = map.world.get_node_or_null("Markers/hf1_shed/Event_ev_shed_trap")
	check_not_null(trap, "柴房有机关位点")
	if trap != null:
		map.player.global_position = trap.global_position
		check_eq(map.event_near_player(), "ev_shed_trap", "站到石砖旁边认得出是这条判定")
		var result: Dictionary = map.interact()
		check_true(bool(result["ok"]) and bool(result["success"]), "识破机关成功：%s" % result["text"])
		check_true(str(result["text"]).contains("识破柴房机关"), "文案用表里的 note：%s" % result["text"])
		check_eq(state.event_check_result("ev_shed_trap"), "done", "结果写进存档")
	# 题字：文学 5 ≥ 5 也能解出来
	var inscription: Node2D = map.world.get_node_or_null("Markers/hf2_hall/Event_ev_hall_inscription")
	if inscription != null:
		map.player.global_position = inscription.global_position
		var solved: Dictionary = map.interact()
		check_true(bool(solved["success"]), "解出三火盆次序：%s" % solved["text"])
	# 毒酒：毒术 2 < 3 → 失败并转述代价
	var poison: Node2D = map.world.get_node_or_null("Markers/hf2_poison/Event_ev_poison_identify")
	check_not_null(poison, "毒酒辨认的判定位点在（不然下面这段会静默跳过）")
	if poison != null:
		map.player.global_position = poison.global_position
		var failed: Dictionary = map.interact()
		check_false(bool(failed["success"]), "毒术不够认不出毒酒")
		check_true(str(failed["text"]).contains("无法辨认"), "失败文案来自 fail_note：%s" % failed["text"])


## 完成度 HUD、永久记录与「已通关层扫荡」
func _check_progress_records(map, state) -> void:
	check_true(map.has_completion(), "黑风寨是带完成度的副本")
	check_true(map.progress_text().contains("完成度"), "HUD 显示完成度：%s" % map.progress_text())
	var record: Dictionary = state.dungeon_record("scene_heifengzhai")
	# 记的是**真正开过的**宝箱：前寨那两个走正常流程开了，门后的暗格银箱还没进去拿。
	# （以前这里写死 4——那是靠 `for chest in map.chests: _open_chest(chest)` 强行开的，
	#   等于把「门后那箱」也提前打开了，正好掩盖了「全开条件凑不齐」那个 bug。）
	var opened_count := 0
	for chest in map.chests:
		if chest.opened_already:
			opened_count += 1
	check_eq(Array(record["chests"]).size(), opened_count, "开过的宝箱都记进永久记录（%d 个）" % opened_count)
	check_gt(float(Array(record["triggers"]).size()), 2.0, "用过的触发点都记进永久记录")

	# 进过隐藏房间也要记（会话状态回大地图会清，完成度必须永久）
	var deep: Node2D = map.world.get_node_or_null("Rooms/Room_hf1_deep")
	check_not_null(deep, "隐藏房间 hf1_deep 在（不然完成度这段会静默跳过）")
	if deep != null:
		var bounds: Node2D = deep.get_node_or_null("bounds")
		check_not_null(bounds, "隐藏房间有 bounds 区（没它 current_room_id 认不出房间）")
		if bounds != null:
			map.player.global_position = bounds.global_position
			map._track_room()
			check_true(
				Array(state.dungeon_record("scene_heifengzhai")["rooms_entered"]).has("hf1_deep"),
				"隐藏房间进入被记录：%s" % str(state.dungeon_record("scene_heifengzhai")["rooms_entered"]),
			)

	# 第 1 层的前院还没打过 → 不能扫荡
	var early: Dictionary = map.sweep_floor(1)
	check_false(bool(early["ok"]), "没通关的层扫荡被拒：%s" % map._status.text)
	check_true(str(map._status.text).contains("还没通关"), "说明这一层没打完")
	# 记录成已打过之后就能扫荡，HUD 也会提示
	state.record_dungeon("scene_heifengzhai", "rooms", "hf1_yard")
	map._refresh_progress()
	check_true(map.progress_text().contains("可扫荡"), "HUD 提示可扫荡的层：%s" % map.progress_text())
	var swept: Dictionary = map.sweep_floor(1)
	check_true(bool(swept["ok"]), "通关后能扫荡：%s" % map._status.text)
	check_true(str(map._status.text).contains("扫荡第 1 层"), "状态栏播报扫荡结果：%s" % map._status.text)
	check_true(str(map._status.text).contains("仅掉落"), "说明扫荡只结算掉落：%s" % map._status.text)


## 战败处理（08）要用「最近到过的城镇」＋战斗外增益 HUD：
## 进城镇时登记出生点／城镇；HUD 上要看得见还剩几分钟（两个控制器共用 `FieldBuffHud` 的文案）。
func _check_town_shelter_and_field_hud(db, session_node) -> void:
	session_node.pending_local_scene = "scene_qingfengyi"
	# 先清空（前面的用例可能已经进过城镇），才能验出「这一进图真的登记了」
	session_node.last_shelter_scene = ""
	session_node.last_shelter_name = ""
	var town = load(LOCAL_RUN).instantiate()
	scene_tree.root.add_child(town)
	town.setup()
	check_eq(
		str(session_node.last_shelter_scene), "scene_qingfengyi",
		"进城镇就把它记成「最近的出生点／城镇」（战败回城要用）"
	)
	check_eq(str(session_node.last_shelter_name), "清风驿", "连中文名一起记（结算卡片要写给玩家看）")

	# 战斗外增益 HUD：注入一条 10 分钟的，标签上要写清名字与剩余分钟
	session_node.clear_field_buffs()
	session_node.add_field_buff("buff_meditated", 10)
	town._refresh_field_buffs()
	var field_label: Label = town.find_child("FieldBuffs", true, false)
	check_not_null(field_label, "城镇 HUD 上有战斗外增益那一行")
	if field_label != null:
		check_true(field_label.text.contains("打坐余韵"), "写清是哪个增益：%s" % field_label.text)
		check_true(field_label.text.contains("剩 10 分钟"), "写清还剩几分钟：%s" % field_label.text)
	# 过期（用注入的 now，不用真等 10 分钟）→ 标签变空，不显示过期的东西
	var expired: Array = session_node.active_field_buffs(
		int(Time.get_unix_time_from_system()) + 11 * 60
	)
	check_eq(expired.size(), 0, "到点即失效（按现实分钟）")
	town._refresh_field_buffs()
	if field_label != null:
		check_eq(field_label.text, "", "没有生效的增益时这一行是空的（不占视觉）")
	scene_tree.root.remove_child(town)
	town.free()


func _check_town_shop(db, state, session_node) -> void:
	session_node.pending_local_scene = "scene_qingfengyi"
	var town = load(LOCAL_RUN).instantiate()
	town.state_override = state
	scene_tree.root.add_child(town)
	town.setup()
	check_eq(town.scene_id, "scene_qingfengyi", "进入清风驿城镇")
	# 城镇没配 level_range（表里是空的）→ HUD 不该硬塞一个「推荐等级」
	check_false(
		str(town._status.text).contains("推荐等级"),
		"没配推荐等级的图不显示这一项：%s" % str(town._status.text),
	)
	check_eq(town.building_near_player(), "", "刚进镇时身边没有店")

	var smith: Node2D = town.world.get_node_or_null("Markers/Buildings/bld_smith")
	check_not_null(smith, "地图上有铁匠铺位点")
	if smith == null:
		scene_tree.root.remove_child(town)
		town.free()
		return
	town.player.global_position = smith.global_position + Vector2(0, 12)
	check_eq(town.building_near_player(), "bld_smith", "站到铁匠铺旁边能认出来")
	var opened: Dictionary = town.interact()
	check_true(bool(opened["ok"]), "按 E 进店：%s" % opened.get("error", ""))
	check_eq(str(opened["building_id"]), "bld_smith", "开的是铁匠铺")
	check_not_null(town.shop_panel, "商店界面挂上了")
	if town.shop_panel != null:
		check_eq(town.shop_panel.building_name(), "铁匠铺", "界面标题是铁匠铺")
		check_true(town.shop_panel.status_text().contains("进店"), "状态栏提示进店：%s" % town.shop_panel.status_text())
	town.close_shop()
	check_true(town.shop_panel == null, "关掉后引用清空")
	_check_town_npcs(town)
	scene_tree.root.remove_child(town)
	town.free()
	session_node.pending_local_scene = ""


## 城镇 NPC 站位（07 §9 第 12 条／§12）：地编交付的是 `Characters/npc_slot_0N`，代码这边先接
## 「站位 ＋ 交互」（台词等 `dialogue_tree.csv`）——**以前这 5 个位点一个都没人读**，
## 地图验收也不看它们（`npc_slot_` 不在 `MARKER_PREFIXES` 里），摆歪了没有任何东西会红。
func _check_town_npcs(town) -> void:
	check_eq(town.npcs.size(), 5, "镇上的 5 个 NPC 站位都被代码接上了")
	if town.npcs.is_empty():
		return
	var first: Node2D = town.npcs[0]
	# 名字来自地编：占位命名 `npc_slot_0N` 与按 id 绑的 `npc_<npc_id>` **两种都合法**（07 §九 第 12 条）
	check_true(String(first.name).begins_with("npc_"), "位点的名字来自地编（%s）" % first.name)
	check_null(town.npc_near_player(), "站在出生点时不误认身边的 NPC")
	town.player.global_position = first.global_position
	check_eq(town.npc_near_player(), first, "站到路人旁边认得出他")
	var talked: Dictionary = town.interact()
	check_true(bool(talked["ok"]), "按 E 有回应（不静默）")
	check_eq(str(talked.get("npc", "")), String(first.name), "回应里带的是这个站位的名字")
	# 设计 19（0.25.0）：城镇 NPC 从「按 E 一句占位台词」升级成**可以交往的人**——
	# 现在按 E 打开的是 NPC 面板，台词取 `npc_def.greet_text_cn`（不再写死在代码里）。
	check_false(str(talked.get("npc_id", "")).is_empty(),
		"这个站位对得上 npc_def 里的人：%s" % str(talked.get("npc_id", "")))
	var npc_row: Resource = get_db().get_row("npc_def", str(talked.get("npc_id", "")))
	check_not_null(npc_row, "站位绑到的 npc_def 行存在")
	if npc_row != null:
		check_eq(str(talked.get("speaker", "")), str(npc_row.name_cn), "说话的人就是这个人")
		check_eq(str(talked.get("text", "")), str(npc_row.greet_text_cn), "开场白取自表里的 greet_text_cn")
		check_false(str(talked.get("text", "")).contains("npc_"), "玩家可见文案不漏表内 id")
	check_not_null(town.npc_panel, "按 E 打开了 NPC 交往面板")
	town.close_npc()
	# **五个站位逐个人都过一遍「按 E 说话」**（决策 329）：第 5 个站位背后没有 `npc_def` 行，
	# 那句兜底文案以前写的是「（这个人还没有配 npc_def 行）」——`npc_def` 正是 CopyGuard 盯的
	# 表内 id 形态（AGENTS 硬规矩），而这条用例只按过第一个人，所以一直没人红。
	# 这里走 `_talk_to_npc`（`interact()` 里那一条分支本身）而不是 `interact()`：后者会先撞上
	# 店铺／设施分支（`facility_bounty_board` 还会动引导旗标），那是另一条用例的事。
	for index in town.npcs.size():
		var slot: Node2D = town.npcs[index]
		var said: Dictionary = town._talk_to_npc(slot)
		check_true(bool(said.get("ok", false)), "第 %d 个站位都能说话（不静默）" % (index + 1))
		var hits: PackedStringArray = CopyGuardScript.id_tokens(town)
		check_eq(hits.size(), 0, "第 %d 个站位说话后界面不漏表内 id：%s" % [index + 1, ", ".join(hits)])
		town.close_npc()
	# **按 id 绑**（07 §九 第 12 条）：位点改名成 `npc_<npc_id>` 之后，绑的人跟着名字走，不再靠
	# `npc_def` 的行序——行序一变，占位命名就会**静默把旁边的人认成他**（决策 332）。
	var renamed: Node2D = town.npcs[0]
	var original_name := String(renamed.name)
	renamed.name = "npc_qian_dafu"     # 钱大夫（按行序第 1 个是王铁，故意挑一个不一样的）
	var by_id: Dictionary = town._talk_to_npc(renamed)
	check_eq(str(by_id.get("npc_id", "")), "npc_qian_dafu", "按 id 绑的位点认到的是名字里那个人")
	check_eq(str(by_id.get("speaker", "")), "钱大夫", "说话的人对得上（不靠行序）")
	town.close_npc()
	renamed.name = original_name
	check_eq(
		str(town._talk_to_npc(renamed).get("npc_id", "")), "npc_wang_tie",
		"改回占位命名后仍按行序绑（老口径不退化）"
	)
	town.close_npc()
	# 认不出来的 id **不许退回按顺序**——那会把旁边的人认成他，比认不出来更糟。
	# 这里直接问判据本身（走 `_talk_to_npc` 会按设计打一条 push_error，日志里不必留这个噪声）。
	check_eq(
		NpcServiceScript.npc_for_slot(get_db(), "npc_bu_cun_zai", "scene_qingfengyi"), "",
		"认不出的 id 不绑人（不退回按顺序）"
	)
	# 优先级：NPC 分支排在店铺之后——站在店门口的路人不能把「进店」抢掉
	var smith: Node2D = town.world.get_node_or_null("Markers/Buildings/bld_smith")
	check_not_null(smith, "（前提）铁匠铺位点在")
	if smith != null:
		var home: Vector2 = first.global_position
		first.global_position = smith.global_position
		town.player.global_position = smith.global_position
		var near_shop: Dictionary = town.interact()
		check_eq(
			str(near_shop.get("building_id", "")), "bld_smith",
			"店铺优先于路人（不然站在门口的 NPC 会把进店挡掉）",
		)
		town.close_shop()
		first.global_position = home


## 赌局位点：镇上摆着 `Event_ev_gamble`，而判定行只有 `region_id`——它必须真的能被按 E 触发。
##
## 以前小地图收位点只认 `scene_id`，于是这个位点**永远接不上**（地图验收那边还专门写了
## `check_id != "ev_gamble"` 的例外把它从大地图排除掉，两边都知道它特殊、却没人接上）。
func _check_town_gamble(db, state, session_node) -> void:
	session_node.pending_local_scene = "scene_qingfengyi"
	var town = load(LOCAL_RUN).instantiate()
	town.state_override = state
	# 事件判定会触发自动存档——这里必须给临时目录，否则 headless 下写 user:// 会崩（踩过）
	var store = SaveStoreScript.new(SAVE_TEST_DIR, 3)
	store.ensure_dir()
	town.save_store_override = store
	scene_tree.root.add_child(town)
	town.setup()
	var marker: Node2D = town.world.get_node_or_null("Markers/Event_ev_gamble")
	check_not_null(marker, "镇上摆着赌局位点")
	if marker != null:
		var wired := false
		for entry: Dictionary in town.events:
			if str(entry["check_id"]) == "ev_gamble":
				wired = true
		check_true(wired, "赌局位点被接上了（本图共 %d 个判定位点）" % town.events.size())
		town.player.global_position = marker.global_position
		check_eq(town.event_near_player(), "ev_gamble", "站到赌局旁边认得出这条判定")
		var before: int = int(state.inventory.money)
		var result: Dictionary = town.resolve_event("ev_gamble")
		check_true(
			str(result["text"]).contains("掷骰"),
			"赌局是软判定：文案里带掷骰 —— %s" % str(result["text"]),
		)
		check_eq(state.inventory.count("item_money"), 0, "背包里不该多出一行「铜钱」")
		# 表里赌局的奖励是 `item_money` 且没有数量列 → 按 1 文算；金额待设计给（见当前状态缺口 #12）
		if bool(result["success"]):
			check_eq(int(state.inventory.money), before + 1, "赢了：铜钱 +1（走钱，不走背包）")
		else:
			check_eq(int(state.inventory.money), before, "输了：不掉钱（软判定失败不惩罚）")
		check_true(
			str(state.event_check_result("ev_gamble")) in ["done", "failed"],
			"结果写进存档：%s" % str(state.event_check_result("ev_gamble")),
		)
	scene_tree.root.remove_child(town)
	town.free()
	session_node.pending_local_scene = ""


func _check_spawn(map) -> void:
	check_eq(map.scene_id, "scene_heifengzhai", "进入黑风寨小地图")
	# 推荐等级来自 map_local.level_range（以前这列没人读，玩家看不到「这儿打不打得过」）
	check_true(
		str(map._status.text).contains("推荐等级 3~15"),
		"HUD 显示推荐等级：%s" % str(map._status.text),
	)
	check_eq(map.teams.size(), 6, "6 支房间敌人（前院/演武场/毒堂/聚义厅/巡逻道/大堂）")
	check_eq(map.chests.size(), 4, "4 个宝箱（柴房/火盆密室/暗格/宝库）")
	check_eq(map.triggers.size(), 7, "7 个触发位点：6 条触发里酒葫芦占两处")
	check_not_null(map.player, "玩家在场景里")


func _check_room_encounter(map, captures: Array) -> void:
	var enemy = map.teams[0]
	map.player.global_position = enemy.global_position + Vector2(0, 8)
	enemy.reset_latch()
	enemy._check_contact()
	check_eq(captures.size(), 1, "贴上房间敌人会触发遭遇")
	if captures.size() >= 1:
		var encounter = captures[0]
		check_eq(str(encounter.source_scene), "scene_heifengzhai", "遭遇记下来自哪张小地图")
		check_eq(str(encounter.source_key), "hf1_yard", "遭遇记下房间 id")
		check_eq(str(encounter.contact), "front", "正面接触")


## 宝箱的掉落也要吃「存档里那一份保底计数」——与战斗结算、扫荡同一个口径
## （唯一读写口是 `GameState.pity_tracker()`／`store_pity()`）。
##
## 今天宝箱组一个保底槽都没有（`pity_count` 全是 0），所以**只能靠夹具**验：
## 把铜箱的一条槽改成 `base_rate=0 / pity_count=2`——第二次开箱必须被保底强行顶着出货，
## 而且计数要**存回存档**。以前 `_open_chest` `new` 了一个临时 PityTracker，记完就丢：
## 每次开箱都从 0 开始数 → 那条槽**永远不出货**（今天看不出来，等设计给宝箱配保底就晚了）。
func _check_chest_pity(map, state) -> void:
	var custom = TableDbScript.new()
	custom.load_all()
	var table: Resource = custom.tables["drop_table"].duplicate(true)
	var target_row_id := ""
	var target_item := ""
	for row: Resource in table.rows:
		if str(row.drop_group) != "drop_chest_copper":
			continue
		if target_row_id.is_empty():
			target_row_id = str(row.drop_row_id)
			target_item = str(row.item_id)
			row.base_rate = 0.0
			row.pity_count = 2
		else:
			# 组里其余槽：也不掉，免得混进结果里（这条用例只看夹具那一槽）
			row.base_rate = 0.0
			row.pity_count = 0
	check_true(not target_row_id.is_empty(), "夹具找到了铜箱的一条掉落槽")
	if target_row_id.is_empty():
		return
	custom.tables["drop_table"] = table
	var chest = null
	for each in map.chests:
		if each.tier_id() == "copper":
			chest = each
	check_not_null(chest, "地图里有铜箱可以开")
	if chest == null:
		return
	var saved_db = map.db
	map.db = custom
	var key := "%s|%s" % ["drop_chest_copper", target_row_id]
	var tracker = state.pity_tracker()
	tracker.clear()
	state.store_pity(tracker)
	chest.opened_already = false
	var first: Dictionary = map._open_chest(chest)
	check_eq(Array(first["drops"]).size(), 0, "第一次开箱：rate=0 且保底没到 → 一件不掉")
	check_eq(state.pity_tracker().attempts(key), 1, "那一次尝试记进了**存档里那一份**计数（不是临时计数器）")
	chest.opened_already = false
	var second: Dictionary = map._open_chest(chest)
	check_eq(Array(second["drops"]).size(), 1, "第二次开箱：保底 2 次到点，强行出一件")
	if Array(second["drops"]).size() > 0:
		var drop: Dictionary = Array(second["drops"])[0]
		check_true(bool(drop["from_pity"]), "这一件是保底顶出来的（from_pity）")
		check_eq(str(drop["item_id"]), target_item, "出的是夹具那条槽的货")
	check_eq(state.pity_tracker().attempts(key), 0, "出货后计数清零（写回的仍是存档里那一份）")
	map.db = saved_db


func _check_chest(db, map, state) -> void:
	var chest = map.chests[0]
	# 三档外观：按掉落组给颜色与个头（设计 07「宝箱开启（三档）」）
	var tiers := {}
	for each in map.chests:
		tiers[each.tier_id()] = each
		check_true(
			["copper", "silver", "gold"].has(each.tier_id()),
			"宝箱档位认得出来：%s → %s" % [each.chest_id, each.tier_id()],
		)
	check_true(tiers.size() >= 2, "地图上不止一种宝箱（拿到 %d 档）" % tiers.size())
	if tiers.has("copper") and tiers.has("silver"):
		check_ne(
			tiers["copper"].tier_color(), tiers["silver"].tier_color(),
			"铜箱与银箱颜色不同",
		)
		# 贴图如果在了，就要用对应档位的那张（地编交付 chest_copper/silver/gold）
		var copper_sprite: String = tiers["copper"].body_texture_path()
		var silver_sprite: String = tiers["silver"].body_texture_path()
		if not copper_sprite.is_empty() and not silver_sprite.is_empty():
			check_true(copper_sprite.contains("chest_copper"), "铜箱用 chest_copper 贴图：%s" % copper_sprite)
			check_true(silver_sprite.contains("chest_silver"), "银箱用 chest_silver 贴图：%s" % silver_sprite)
			check_false(copper_sprite.contains("_open"), "没开过的箱子用未开贴图")
		check_ne(
			str(tiers["copper"].get_node("State").text), str(tiers["silver"].get_node("State").text),
			"档位写在箱子上：%s / %s" % [
				str(tiers["copper"].get_node("State").text), str(tiers["silver"].get_node("State").text),
			],
		)
	map.player.global_position = chest.global_position
	var money_before: int = int(state.inventory.money)
	var result: Dictionary = map.interact()
	check_true(bool(result["ok"]), "开宝箱成功：%s" % map._status.text)
	check_eq(map.save_service().last_reason, "开宝箱", "开宝箱后自动存档（设计 02：副本内自动存）")
	check_true(chest.opened_already, "宝箱标记为已开")
	check_eq(str(chest.get_node("State").text), "已开", "开过的箱子标「已开」")
	# 贴图：地编给了三档 × 开/未开，开过之后应该换成 _open 那张（没贴图才退回色块）
	if chest.body_texture_path().is_empty():
		check_eq(chest.get_node("Body").color, Color("6f6656"), "没有贴图时退回色的暗版")
	else:
		check_true(
			chest.body_texture_path().contains("chest_%s" % chest.tier_id())
				and chest.body_texture_path().contains("_open"),
			"开过的箱子换成对应的 _open 贴图：%s" % chest.body_texture_path(),
		)
	check_true(int(state.inventory.money) >= money_before, "铜钱只增不减")
	# 再开一次：同一个宝箱不能重复领取
	var again: Dictionary = map.interact()
	check_false(bool(again["ok"]), "同一个宝箱不能重复开")
	check_true(str(map._status.text).contains("没有可交互"), "重复交互给出提示")


## 隐藏内容的奖励也走「唯一入账口径」：配一条 `reward_type=item, reward_id=item_money`（奖励铜钱）
## 必须进 `inventory.money`，**不能**在背包里发一行「铜钱」——`ev_gamble` 当年就是这个坑。
## 表里现在没有这种行（所以这是一条防回归的绊线），用合成行直接调发放函数。
func _check_trigger_money_reward(db, map, state) -> void:
	var template: Resource = db.get_row("hidden_trigger", "trig_chest_all")
	check_not_null(template, "拿一条真实隐藏内容当模板")
	if template == null:
		return
	var row: Resource = template.duplicate(true)
	row.reward_type = "item"
	row.reward_id = "item_money"
	var money_before := int(state.inventory.money)
	var text: String = map._grant_trigger_reward(row, "测试奖励")
	check_true(int(state.inventory.money) > money_before, "铜钱奖励进钱包：%s" % text)
	check_eq(int(state.inventory.count("item_money")), 0, "背包里不该出现一行「铜钱」")
	check_true(text.contains("文钱"), "文案写「得到 N 文钱」：%s" % text)


## 背包满时的宝箱文案：`_drops_summary` 必须如实写「没捡起」。
## 以前它只看 `items` / `equipment` / `money`，不看 `overflow`——满包开箱会显示「空的」，
## 玩家以为箱子里没东西（其实是放不下）。玩家可见的文案不许说假话。
## 这里直接喂合成的 `applied`，不靠随机掉落（宝箱掉落是概率掷的，不能拿它当证据）。
func _check_chest_overflow_copy(map) -> void:
	var partial := {
		"items": [{"item_id": "item_herb", "qty": 2, "overflow": 3}],
		"equipment": [],
		"money": 0,
		"overflow": [{"item_id": "item_herb", "qty": 3, "reason": "背包已满"}],
	}
	var text: String = map._drops_summary(partial, [{"item_id": "item_herb", "qty": 5}])
	check_true(text.contains("草药 ×2"), "拿到的部分照实写：%s" % text)
	check_true(text.contains("没捡起"), "没捡起的部分也要写：%s" % text)
	check_true(text.contains("×3"), "写清没捡起几个：%s" % text)

	# 一个都没进去：不许显示「空的」（箱子不是空的，是放不下）
	var all_lost := {
		"items": [],
		"equipment": [],
		"money": 0,
		"overflow": [{"item_id": "item_herb", "qty": 5, "reason": "背包已满"}],
	}
	var lost_text: String = map._drops_summary(all_lost, [{"item_id": "item_herb", "qty": 5}])
	check_ne(lost_text, "空的", "不许说「空的」（其实是放不下）：%s" % lost_text)
	check_true(lost_text.contains("没捡起"), "写清全都没捡起：%s" % lost_text)


## 击杀方式的中文名必须**查表**（`status_effect.name_cn`）——设计新加一条状态、再拿它当 `kill_with` 时，
## 抄一份常量名单的实现会把英文 id 直接甩给玩家（和当年「dot_poison 泄露」同一类）。
## 顺带核宝箱档位：从掉落组名解析，且**认不出来要出声**（新档位要先补颜色/贴图/名字，别静默按铜箱画）。
## 观察点（设计 20 §3.2／0.29.1）：按 E 只出一句碎句——
## **不发奖励、不进副本完成度**（它是"看一眼"，不是一个事件）。
##
## **0.32.0 改动**：读过的观察点要记一枚 `flag_obs_<point_id>`——幕二那条「免战」选项的前置
## 就写 `flag_obs_ob_luoyanpo_cart_01`（「看过的观察点」由此能被别的判定引用）。
## 所以这条断言从「不置任何旗标」改成「只多这一枚」（除了它，仍然什么都不动）。
func _check_flavor_points(map, state) -> void:
	var points: Array = map.flavor_points
	check_gt(float(points.size()), 0.0, "黑风寨收了观察点位点（%d 个）" % points.size())
	if points.is_empty():
		return
	var entry: Dictionary = points[0]
	var point_id := str(entry["point_id"])
	var row: Resource = map.db.get_row("flavor_point", point_id)
	check_not_null(row, "位点认得出对应的表行（%s）" % point_id)
	if row == null:
		return
	map.player.global_position = (entry["node"] as Node2D).global_position
	check_eq(map.flavor_near_player(), point_id, "站到观察点旁边认得出它")
	var flags_before: int = state.flags.size()
	var record_before: String = str(state.dungeon_record("scene_heifengzhai"))
	var obs_flag := "flag_obs_%s" % point_id
	var had_obs: bool = state.has_flag(obs_flag)
	var result: Dictionary = map.read_flavor_point(point_id)
	check_true(bool(result.get("ok", false)), "看得到这一句")
	check_eq(str(result.get("text", "")), str(row.text_cn), "文案来自表")
	check_true(map._status.text.contains(str(row.text_cn)), "状态栏把碎句显示出来了")
	check_true(state.has_flag(obs_flag), "读过就记下 %s（幕二免战选项的前置）" % obs_flag)
	check_eq(
		state.flags.size(), flags_before + (0 if had_obs else 1),
		"除 flag_obs_<point_id> 之外不多置旗标",
	)
	check_eq(str(state.dungeon_record("scene_heifengzhai")), record_before, "也不进副本完成度")
	# **看得见**（设计 15 §一：可交互物要在低饱和背景里暖色提亮）——观察点在地图上只是一根
	# 光秃秃的 Marker2D，没有这枚标记玩家只会从旁边走过去，而它们正是 20 §3.2 要的「探索感」。
	# 逐个位点都查（不是只查第一个）：漏挂一个就等于那一句碎句玩家永远看不见。
	for point: Dictionary in points:
		var node := point["node"] as Node2D
		var mark = node.get_node_or_null(FlavorMarkerScript.NODE_NAME)
		check_not_null(mark, "观察点 %s 挂着可见标记" % str(point["point_id"]))
		if mark != null:
			check_true(
				FlavorMarkerScript.is_visible_highlight(mark),
				"标记是暖色提亮且可见（%s）" % str(point["point_id"])
			)


func _check_kill_style_names(map, db) -> void:
	var stub = load("res://src/core/table_db.gd").new()
	stub.load_all()
	var statuses: Resource = stub.tables["status_effect"].duplicate(true)
	var wound: Resource = statuses.rows[0].duplicate(true)
	wound.status_id = "wound"
	wound.id = "wound"
	wound.name_cn = "创伤"
	statuses.rows.append(wound)
	statuses.index["wound"] = statuses.rows.size() - 1
	stub.tables["status_effect"] = statuses
	map.db = stub
	check_eq(map._style_name("wound"), "创伤", "新状态拿来做 kill_with：名字来自表（不是英文 id）")
	check_eq(
		map._style_name("poison"), str(stub.get_row("status_effect", "poison").name_cn),
		"既有状态名同样来自表",
	)
	check_eq(map._style_name("normal"), "正面击杀", "normal 是代码侧的名字（它不是状态行）")
	map.db = db

	check_eq(ChestScript.resolve_tier("drop_chest_copper"), "copper", "铜箱档位从掉落组名解析")
	check_eq(ChestScript.resolve_tier("drop_chest_silver"), "silver", "银箱档位从掉落组名解析")
	check_eq(ChestScript.resolve_tier("drop_chest_gold"), "gold", "金箱档位从掉落组名解析")


## `reward_type=boss`（06 允许、2026-10-03 才接上）：判定通过 → **当场开战**。
##
## 发行数据里还没有哪条判定用 boss 奖励（12 条里用的是 none/item/event/room），所以用夹具：
## 复制表、把「识破柴房机关」那条的奖励改成 boss → 真走一遍 `resolve_event()`，
## 断言「开战请求被场景层接住」——建出来的 Encounter 会交给 `battle_switch_handler`。
func _check_event_boss_reward(db, map, state, captures: Array) -> void:
	var stub = load("res://src/core/table_db.gd").new()
	stub.load_all()
	var copy: Resource = stub.tables["event_check"].duplicate(true)
	for row: Resource in copy.rows:
		if str(row.check_id) == "ev_shed_trap":
			row.reward_type = "boss"
			row.reward_id = "en_bd_boss"
	stub.tables["event_check"] = copy
	# 事件服务的表库在第一次用时就被缓存了（前面的 `_check_events` 已经用过）——
	# 所以这里直接换掉控制器手上的 db 并清掉缓存，与 `town._save_service = null` 同一套注入手法。
	map.db = stub
	map._event_service = null

	var session_node = scene_tree.root.get_node_or_null("GameSession")
	check_not_null(session_node, "用例能拿到会话")
	var before := captures.size()
	var result: Dictionary = map.resolve_event("ev_shed_trap")
	check_true(bool(result["success"]), "判定本身成功：%s" % str(result["text"]))
	check_true(str(result["text"]).contains("现身"), "文案写「现身」：%s" % str(result["text"]))
	check_eq(captures.size(), before + 1, "场景层接住开战请求（切战斗的那条回调被调用）")
	if captures.size() > before:
		var encounter = captures[captures.size() - 1]
		check_eq(str(encounter.team_id), "team_hidden_en_bd_boss", "用的是单人 Boss 队（与小地图隐藏 Boss 同一口径）")
		check_eq(str(encounter.source_key), "ev_shed_trap", "来源记的是这条判定（结算要用它找掉落与首杀）")
	if session_node != null:
		check_not_null(session_node.pending_encounter, "遭遇写进会话（切场景后战斗场景读它）")
		check_eq(str(session_node.pending_return_scene), "res://scenes/local_run.tscn", "打完回这张小地图")
		check_eq(str(session_node.pending_local_scene), "scene_heifengzhai", "回的是黑风寨")
	map.db = db
	map._event_service = null


## 三火盆顺序谜题（03 的七类隐藏内容最后一类，2026-10-03 深夜接上）：
## 点错 → 提示次序不对 + 进度重置 + 不发奖励；按 1 → 3 → 2 点齐 → 拿到前代寨主遗物并记进副本记录。
## 位点编号来自地图命名 `Trigger_trig_brazier_1/2/3`（07 资源需求里写明）；用例直接改 `sequence_index`
## 来扮演三个火盆——地图上那三个编号位点由地编摆，摆好之前这条路靠这里钉住。
## 三火盆：地编现在只摆了 1 个位点（07 待补第 6 条要求三个编号位点 `Trigger_trig_brazier_1/2/3`），
## 而「点错重置」「每格独立的灭／燃」「完成时同一行一起收口」这些规则**只有三格都在时才验得出来**——
## 所以这里按同一套命名约定补两个内存位点当夹具（**只加在内存里，不动地图资产**，与 `test_case` 里
## 那几张「复制表、只改内存」的夹具同一个思路）。
func _brazier_fixture(map) -> Array:
	var base = null
	for each in map.triggers:
		if each.trigger_id == "trig_brazier":
			base = each
	if base == null:
		return []
	base.sequence_index = 1          # 地编把位点改名成 `Trigger_trig_brazier_1` 之后就是这个编号
	var points: Array = [base]
	for index in [2, 3]:
		var extra = TriggerPointScript.new()
		extra.name = "Trigger_trig_brazier_%d" % index
		extra.setup(base.row, map.player, false, map.db, index)
		# 摆在地图上真实那个位点旁边：这样 `can_interact()` 的距离判定也跟真地图一致
		# （不然「完成后不再可交互」那条断言会因为距离太远而永远为真——假绿）
		map.add_child(extra)
		extra.global_position = base.global_position
		map.triggers.append(extra)
		points.append(extra)
	return points


## 夹具收尾：只摘掉内存里补出来的那两个，地图里真实的那个位点要留给后面的检查
func _teardown_brazier_fixture(map, extras: Array) -> void:
	for each in extras:
		if not map.triggers.has(each):
			continue
		map.triggers.erase(each)
		map.remove_child(each)
		each.free()


func _check_sequence_puzzle(map, state) -> void:
	var points: Array = _brazier_fixture(map)
	check_eq(points.size(), 3, "三火盆位点齐了（地图里 1 个 ＋ 用例夹具 2 个）")
	if points.size() != 3:
		return
	var first = points[0]
	var second = points[1]
	var third = points[2]
	# 玩家得站在火盆跟前：`can_interact()` 里那条距离判定要跟真玩一致，
	# 不然「完成后不再可交互」会变成一条永远为真的假绿
	map.player.global_position = first.global_position
	# 地编交付的两态贴图必须真的被用上（07 §8.3「火盆（灭／燃）」）——
	# 以前火盆只是个紫色多边形，仓库里的 brazier_off/on.png 一次都没被读过
	check_eq(
		first.brazier_texture_path(), "res://assets/sprites/props/brazier_off.png",
		"火盆用「灭」贴图起手（而不是占位多边形）：%s" % first.brazier_texture_path(),
	)
	check_true(first.requirement_text().contains("1 → 3 → 2"), "需求文案把次序写给玩家：%s" % first.requirement_text())
	var equips_before := int(state.inventory.equipment_count())
	check_false(
		Array(state.dungeon_record(map.scene_id).get("triggers", [])).has("trig_brazier"),
		"这段之前没记过这条隐藏（用例前提）",
	)

	# ① 点错：先点第 2 格 → 拒绝、进度重置、不发奖励、不算触发过、视觉回灭
	map._set_sequence_progress("trig_brazier", [])
	SfxScript.clear_log()
	var wrong: Dictionary = map._activate_trigger(second)
	check_false(bool(wrong["ok"]), "次序不对要拒绝：%s" % str(wrong.get("summary", "")))
	check_false(SfxScript.has_played("brazier"), "点错不点火（没有点燃音效）")
	check_true(str(map._status.text).contains("次序不对"), "提示写清是次序问题：%s" % map._status.text)
	check_false(str(map._status.text).contains("trig_brazier"), "提示不漏表内 id：%s" % map._status.text)
	check_eq(int(state.inventory.equipment_count()), equips_before, "点错不发奖励")
	check_eq(map._sequence_progress("trig_brazier").size(), 0, "点错进度重置")
	check_false(second.used, "没点完不算触发过（点还亮着）")
	check_eq(
		second.brazier_texture_path(), "res://assets/sprites/props/brazier_off.png",
		"点错后火盆回灭（视觉与进度一起重置）",
	)

	# ② 按 1 → 3 点两格：每步都成功但不算完成，进度累加，点着的那两格各自亮起
	SfxScript.clear_log()
	var first_step: Dictionary = map._activate_trigger(first)
	check_true(bool(first_step["ok"]), "第 1 格点对了：%s" % str(first_step.get("summary", "")))
	check_eq(map._sequence_progress("trig_brazier").size(), 1, "进度累加（1）")
	check_eq(
		first.brazier_texture_path(), "res://assets/sprites/props/brazier_on.png",
		"点对的第 1 格亮起（灭 → 燃）",
	)
	check_eq(
		third.brazier_texture_path(), "res://assets/sprites/props/brazier_off.png",
		"还没点的第 3 格仍然是灭的（亮的是哪几格，玩家一眼看得清）",
	)
	var third_step: Dictionary = map._activate_trigger(third)
	check_true(bool(third_step["ok"]), "第 3 格点对了：%s" % str(third_step.get("summary", "")))
	check_false(bool(third_step.get("complete", false)), "还没点完不算完成")
	check_eq(map._sequence_progress("trig_brazier").size(), 2, "进度累加（2）")
	check_eq(
		third.brazier_texture_path(), "res://assets/sprites/props/brazier_on.png",
		"点对的第 3 格也亮起",
	)
	check_false(first.used, "中途仍然不算触发过")
	check_true(SfxScript.has_played("brazier"), "点对一格就请求「火盆点燃」音效（07 §8.4）")

	# ③ 第三步点第 2 格 → 完成：发奖励（前代寨主遗物）＋记进副本触发记录
	var done: Dictionary = map._activate_trigger(second)
	check_true(
		bool(done["ok"]) and bool(done.get("complete", false)),
		"点齐 1-3-2 完成：%s" % str(done.get("summary", "")),
	)
	check_true(str(map._status.text).contains("前代寨主遗物"), "发的是表里配的那件装备：%s" % map._status.text)
	check_eq(int(state.inventory.equipment_count()), equips_before + 1, "拿到一件装备实例")
	check_true(
		Array(state.dungeon_record(map.scene_id).get("triggers", [])).has("trig_brazier"),
		"记进副本触发记录（完成度要算它）",
	)
	check_eq(map._sequence_progress("trig_brazier").size(), 0, "完成后进度清空")

	# ④ 收口：同一行的**每一格**都要变成已用并保持燃着——只标最后点的那一格的话，
	#    玩家站在原地再按一遍 1-3-2 就能再领一件（`once_only` 只在**生成位点**时看永久记录）
	var all_used := true
	var still_interactable := false
	for each in points:
		all_used = all_used and each.used
		still_interactable = still_interactable or each.can_interact()
		check_eq(
			each.brazier_texture_path(), "res://assets/sprites/props/brazier_on.png",
			"完成后的火盆保持燃着（不是压成灰）",
		)
	check_true(all_used, "谜题完成后每个火盆都收口（以前只有最后点的那格被标成已用）")
	check_false(still_interactable, "完成后火盆不再可交互（玩家路径被 `can_interact()` 挡住）")
	var equips_done := int(state.inventory.equipment_count())
	var again: Dictionary = map._activate_trigger(first)
	check_false(bool(again["ok"]), "第二遍走底层调用也被拦住：%s" % str(again.get("error", "")))
	check_eq(int(state.inventory.equipment_count()), equips_done, "重复点不再发第二件「前代寨主遗物」")
	_teardown_brazier_fixture(map, points.slice(1))


func _check_triggers(map, state, captures: Array) -> void:
	var wine = null
	var wine_points: Array = []
	var dig = null
	for point in map.triggers:
		if point.trigger_id == "trig_wine":
			wine_points.append(point)
			if wine == null:
				wine = point
		if point.trigger_id == "trig_dig":
			dig = point
	check_not_null(wine, "酒葫芦触发点在")
	check_eq(wine_points.size(), 2, "酒葫芦有两个位点（牢房深处 ＋ 后山地牢）——「一行多位点」这件事本身要钉住")
	# 触发点的**可见状态**要和状态一致（玩家靠这个判断「能不能点」）：
	# 可交互=紫色，做不了=灰色，已用过=暗色
	check_eq(wine.get_node("Body").color, Color("b07cc6"), "可用触发点是紫色")
	var sequence_point = null
	for point in map.triggers:
		if point.trigger_id == "trig_brazier":
			sequence_point = point
	check_not_null(sequence_point, "三火盆触发位点在（缺位点未做的是「三个交互点」，不是这个点本身）")
	if sequence_point != null:
		# 2026-10-03 深夜：`sequence` 类接上了（七类隐藏内容全部实现）——细节流程见
		# `_check_sequence_puzzle`；这里只钉「它不再是被动灰色块」：
		# 火盆用的是地编交付的**两态贴图**（灭／燃），不再走那条按颜色区分的占位多边形
		check_true(sequence_point.supported(), "sequence 类现在支持了")
		# 这条跑在 `_check_sequence_puzzle` 之后，那时它已经是「燃」了——所以只钉
		# 「用的是地编交付的那两张两态贴图之一」，具体灭/燃由上面那段逐格验
		check_true(
			sequence_point.brazier_texture_path() == "res://assets/sprites/props/brazier_off.png"
				or sequence_point.brazier_texture_path() == "res://assets/sprites/props/brazier_on.png",
			"火盆位点用的是地编交付的两态贴图（不是被动灰块）：%s" % sequence_point.brazier_texture_path(),
		)
	# 缺道具 → 提示需求，不触发
	map.player.global_position = wine.global_position
	var before := captures.size()
	var missing: Dictionary = map.interact()
	check_false(bool(missing["ok"]), "没有酒葫芦时触发失败")
	check_true(str(map._status.text).contains("酒葫芦"), "提示需要哪件道具（说中文名）")
	check_false(str(map._status.text).contains("item_"), "需求提示不漏表 id")
	check_eq(captures.size(), before, "缺道具不会开战")
	# 给道具 → 触发隐藏 Boss
	state.inventory.add_item(map.db, "item_wine_gourd", 1)
	var triggered: Dictionary = map.interact()
	check_true(bool(triggered["ok"]), "有酒葫芦后触发成功：%s" % map._status.text)
	check_eq(map.save_service().last_reason, "触发隐藏内容", "触发隐藏内容后自动存档")
	check_eq(captures.size(), before + 1, "隐藏 Boss 进入战斗")
	check_eq(wine.get_node("Body").color, Color("3f3f3f"), "用过的触发点变暗色")
	if captures.size() > before:
		check_true(str(captures[before].team_name).contains("醉刀客"), "打的是醉刀客")
		check_eq(str(captures[before].members), "en_hidden_drunk:1", "隐藏 Boss 用单只队伍")
	# 隐藏点位同时是武学来源（skill_base.source_type=hidden, source_id=trig_wine）
	var granted: Array = Array(triggered.get("granted", []))
	check_eq(granted.size(), 3, "醉刀客点位带 3 部武学（醉步／酒中刀／醉意）")
	for entry: Dictionary in granted:
		check_gt(float(PackedStringArray(entry["learned"]).size()) + float(Array(entry["blocked"]).size()), 0.0,
			"%s 要么学会要么写明为什么没学会" % str(entry["name"]))
	check_true(str(triggered["summary"]).contains("修习门槛") or str(triggered["summary"]).contains("领悟"),
		"触发文案里说明武学结果：%s" % triggered["summary"])
	# 一行两位点：用掉一个之后，**另一个也不该再能用**——不然隐藏 Boss 会在同一趟里再打一遍
	# （三火盆那次是同一个洞：完成时只标「最后点的那格」，见框架说明决策 237）
	if wine_points.size() > 1:
		var second: Node2D = wine_points[1] if wine_points[0] == wine else wine_points[0]
		# 先站到它跟前：`can_interact()` 里有距离判定，站远了这条断言会变成假绿（踩过）
		map.player.global_position = second.global_position
		check_false(second.can_interact(), "同一行的第二个位点跟着一起收口（玩家点不到）")
		check_eq(second.get_node("Body").color, Color("3f3f3f"), "第二个位点视觉上也变暗了")
		var again: Dictionary = map._activate_trigger(second)
		check_false(bool(again["ok"]), "走到第二个位点不再触发：%s" % str(again.get("error", "")))
		check_eq(captures.size(), before + 1, "隐藏 Boss 不会被同一趟的第二遍再打一次")
	# 触发奖励的**数据错路径**同样不许把 id 甩给玩家（决策 242）
	var bogus_row: Resource = map.db.get_row("hidden_trigger", "trig_wine").duplicate(true)
	bogus_row.reward_type = "room"
	bogus_row.reward_id = "hf_does_not_exist"
	var bogus_text: String = map._grant_trigger_reward(bogus_row, "测试")
	check_false(bogus_text.contains("hf_does_not_exist"), "触发奖励写错房间时文案不漏 id：%s" % bogus_text)
	check_true(
		bogus_text.contains("不在本图"), "触发奖励写错房间时文案说清是什么毛病：%s" % bogus_text,
	)

	# 「铁镐挖通」的位点在**塌陷山洞**（`trig_dig.scene_id=scene_cave`），不在黑风寨里。
	# 这条以前写成 `if dig != null:`——位点不在这张图，整段断言从来没跑过（假绿）。
	# 现在这里只钉「本图确实没有」，真流程在 `_check_cave_shortcut` 里用塌陷山洞走一遍。
	check_null(dig, "黑风寨里没有 trig_dig 位点（它属于 scene_cave，见 _check_cave_shortcut）")


## 玩家可见文案：不许把表里的英文 id 甩到界面上（2026-10-03 换对象审计）
##
## 起因：小地图状态栏以前会说「打开宝箱：item_health_pill ×2、eq_sword_01#1」「需要 item_pickaxe」
## 「要用 poison 击杀目标」「挖通！直接通到「hf3_dungeon」」——全是内部 id。
## 现在一律走 `TableDb.display_name()`，这个用例把四条路径都钉住（有名字、且不漏 id）。
func _check_player_text(map, db, session_node) -> void:
	# 1) 触发点需求：酒葫芦（不是 item_wine_gourd）——用本图真有的触发点，别再靠条件跳过
	var wine = null
	for point in map.triggers:
		if point.trigger_id == "trig_wine":
			wine = point
	check_not_null(wine, "黑风寨有酒葫芦触发点")
	if wine != null:
		var need: String = wine.requirement_text()
		check_true(need.contains("酒葫芦"), "需求文案说道具名：%s" % need)
		check_false(need.contains("item_"), "需求文案不漏表 id：%s" % need)

	# 2) 开箱／扫荡的掉落摘要：说名字，不带实例序号
	var summary: String = map._drops_summary(
		{
			"items": [{"item_id": "item_wine_gourd", "qty": 2}],
			"equipment": ["eq_sword_01#1"],
			"money": 7,
		},
		[{"item_id": "item_wine_gourd"}],
	)
	check_true(summary.contains("酒葫芦"), "掉落摘要说道具名：%s" % summary)
	check_true(summary.contains("铁剑"), "掉落摘要说装备名（实例 id 剥掉 #序号）：%s" % summary)
	check_false(summary.contains("item_"), "掉落摘要不漏道具 id：%s" % summary)
	check_false(summary.contains("#"), "掉落摘要不漏装备实例序号：%s" % summary)
	check_true(summary.contains("铜钱"), "掉落摘要带铜钱：%s" % summary)

	# 3) 挖通捷径：说房间名「后山地牢」（不是 hf3_dungeon）
	var space_row: Resource = db.get_row("hidden_trigger", "trig_dig")
	var dug_text: String = str(map._trigger_space(space_row))
	check_true(dug_text.contains("后山地牢"), "挖通文案说房间名：%s" % dug_text)
	check_false(dug_text.contains("hf3_dungeon"), "挖通文案不漏房间 id：%s" % dug_text)

	# 4) kill_style 判定说明：说「中毒」不说 poison
	session_node.last_battle = {}
	var poison_row: Resource = db.get_row("hidden_trigger", "trig_poison_kill")
	var blocked: String = str(map._condition_block(poison_row))
	check_true(blocked.contains("中毒"), "毒杀判定说中文：%s" % blocked)
	check_false(blocked.contains("poison"), "毒杀判定不漏条件值：%s" % blocked)


## 出生点与出口守卫（每张小地图都过一遍）：
## HUD 要告诉玩家「我在第几层、哪间房」：黑风寨是**三层同一张图**、19 间房，
## 不给这个信息玩家只能靠自己记路。数据都在 `dungeon_room`（floor + room_name）。
func _check_floor_hud(map, db) -> void:
	var room: Node2D = map.world.get_node_or_null("Rooms/Room_hf2_yard")
	check_not_null(room, "二层聚义厅前院在")
	if room == null:
		return
	var bounds := room.get_node_or_null("bounds") as Area2D
	if bounds != null:
		map.player.global_position = map._bounds_rect(bounds).get_center()
	map._refresh_status()
	var room_row: Resource = db.get_row("dungeon_room", "hf2_yard")
	check_true(
		str(map._status.text).contains("第 2 层"),
		"HUD 报当前楼层：%s" % map._status.text,
	)
	check_true(
		str(map._status.text).contains(str(room_row.room_name)),
		"HUD 报当前房间名（%s）：%s" % [str(room_row.room_name), map._status.text],
	)


## 出生点与出口守卫（每张小地图都过一遍）：
##   ① 落点必须是地编在 `Characters/player_spawn` 里放的出生点（资产里的第一真相）；
##   ② 进图后头几帧不能被判定成「走到出口」而弹回大地图——清风驿的出生点就压在出口位点上，
##      所以出口必须「先走出去一次」才武装（`EXIT_ARM_MARGIN`），否则一进城就被弹回去。
func _check_spawn_and_exit_guard(db, state, session_node) -> void:
	var checked := 0
	for row: Resource in db.rows("map_local"):
		var sid := str(row.scene_id)
		if not ResourceLoader.exists("res://scenes/maps/%s.tscn" % sid):
			continue
		checked += 1
		session_node.pending_local_scene = sid
		session_node.pending_local_room = ""
		session_node.local_position_scene = ""
		var one = load(LOCAL_RUN).instantiate()
		one.state_override = state
		var left: Array = []
		one.return_handler = func() -> void: left.append(true)
		scene_tree.root.add_child(one)
		one.setup()
		var authored: Node2D = one.world.get_node_or_null("Characters/player_spawn")
		check_not_null(authored, "%s 有地编放的 player_spawn 位点" % sid)
		# 出口也要看得见（`Exit_<scene_id>` 是看不见的坐标，走到它上面就回大地图）
		var exit_label: Label = one.find_child("PlaceLabel_Exit", true, false)
		if sid == "scene_ferry_locked":
			# 07 文档：「渡口本章不开放可不做」——它既没有出口位点，也不该有出口标识
			check_null(exit_label, "废弃渡口本章不开放，没有出口位点（07 已注明）")
		else:
			check_not_null(exit_label, "%s 的出口有可见标识" % sid)
			if exit_label != null:
				check_eq(exit_label.text, "出口", "%s 出口标识写「出口」" % sid)
		if authored != null:
			check_lt(
				one.player.global_position.distance_to(authored.global_position), 1.0,
				"%s 从出生点进场：%s" % [sid, str(one.player.global_position)],
			)
		# 真跑几帧：出生点压在出口上的图（清风驿）也不能被弹出去
		for i in range(3):
			one._process(1.0 / 60.0)
		check_eq(left.size(), 0, "%s 进场后不会被立刻弹回大地图" % sid)
		scene_tree.root.remove_child(one)
		one.free()
	check_gt(float(checked), 3.0, "小地图都过了一遍（%d 张）" % checked)


## 后山密道：塌陷山洞里用铁镐挖通 → 直通后山地牢（跨图捷径）
##
## 设计 03/07：「在塌陷山洞用铁镐继续往里挖 → 通往山寨后山的捷径」，07 还专门要求
## 塌陷山洞放一个 `Portal_scene_heifengzhai` 位点。以前的实现只会在**本图**找目标房间，
## 而目标房间 `hf3_dungeon` 在黑风寨那张图里——挖通等于没用（而且旧用例静默跳过，看不出来）。
func _check_cave_shortcut(db, state, session_node) -> void:
	session_node.pending_local_scene = "scene_cave"
	session_node.pending_local_room = ""
	session_node.local_position_scene = ""
	session_node.local_maps.clear()
	var cave = load(LOCAL_RUN).instantiate()
	cave.state_override = state
	var switches: Array = []
	cave.portal_switch_handler = func(target_scene: String, room_id: String) -> void:
		switches.append([target_scene, room_id])
	scene_tree.root.add_child(cave)
	cave.setup()

	var dig = null
	for point in cave.triggers:
		if point.trigger_id == "trig_dig":
			dig = point
	check_not_null(dig, "塌陷山洞里有 trig_dig 位点")
	check_eq(cave.scene_id, "scene_cave", "在塌陷山洞这张图里")
	check_not_null(cave.world.get_node_or_null("Markers/Portal_scene_heifengzhai"), "有 Portal_scene_heifengzhai 位点")
	check_true(
		db.get_row("dungeon_room", "hf3_dungeon") != null
			and str(db.get_row("dungeon_room", "hf3_dungeon").scene_id) == "scene_heifengzhai",
		"目标房间 hf3_dungeon 在黑风寨那张图里（所以要跨图）",
	)
	if dig != null:
		# ① 干净档（没有铁镐）→ 只给需求提示，不切图
		#    （不能拿本用例的 state 来试：前面开过柴房宝箱，按设计 07 那口铜箱里就有铁镐）
		cave.state_override = solo_state(db)
		cave.player.global_position = dig.global_position
		var blocked: Dictionary = cave.interact()
		check_false(bool(blocked["ok"]), "没铁镐挖不动：%s" % cave._status.text)
		check_true(cave._status.text.contains("铁镐"), "提示需要铁镐（不是 item_pickaxe）：%s" % cave._status.text)
		check_false(cave._status.text.contains("item_"), "需求提示不漏表 id")
		check_eq(switches.size(), 0, "挖不动时不会切图")
		# ② 有铁镐 → 记下目标图与落点，再重进小地图
		cave.state_override = state
		state.inventory.add_item(db, "item_pickaxe", 1)
		cave.player.global_position = dig.global_position
		var dug: Dictionary = cave.interact()
		check_true(bool(dug["ok"]), "用铁镐挖通成功：%s" % cave._status.text)
		check_eq(switches.size(), 1, "挖通触发切图")
		if switches.size() == 1:
			check_eq(str(switches[0][0]), "scene_heifengzhai", "切到黑风寨那张图")
			check_eq(str(switches[0][1]), "hf3_dungeon", "落点是后山地牢")
		check_eq(session_node.pending_local_scene, "scene_heifengzhai", "pending_local_scene 写好了")
		check_eq(session_node.pending_local_room, "hf3_dungeon", "pending_local_room 写好了")
		check_true(str(cave._status.text).contains("后山地牢"), "文案说房间名：%s" % cave._status.text)
		check_false(str(cave._status.text).contains("hf3_dungeon"), "文案不漏房间 id")
	scene_tree.root.remove_child(cave)
	cave.free()

	# 模拟「重进小地图」：新开一份 local_run，玩家应该站在后山地牢而不是黑风寨入口
	var reentered = load(LOCAL_RUN).instantiate()
	reentered.state_override = state
	var again: Array = []
	reentered.portal_switch_handler = func(target_scene: String, room_id: String) -> void:
		again.append([target_scene, room_id])
	scene_tree.root.add_child(reentered)
	reentered.setup()
	check_eq(reentered.scene_id, "scene_heifengzhai", "重进的是黑风寨")
	var dungeon_room: Node2D = reentered.world.get_node_or_null("Rooms/Room_hf3_dungeon")
	check_not_null(dungeon_room, "后山地牢房间还在")
	# 落点是房间 bounds 矩形的中心（房间节点位置是矩形左上角，站在角上会被墙推出去、
	# current_room_id() 也判不到这个房间），所以这里钉「人在不在这个房间里」
	check_eq(reentered.current_room_id(), "hf3_dungeon", "挖通后重进图，人就站在后山地牢里")
	var center: Vector2 = Vector2.ZERO
	var bounds := dungeon_room.get_node_or_null("bounds") as Area2D
	if bounds != null:
		center = reentered._bounds_rect(bounds).get_center()
	check_lt(reentered.player.position.distance_to(center), 40.0, "落点在房间中心附近（允许物理把 10px 宽的人推出几像素）")
	check_eq(session_node.pending_local_room, "", "落点请求用掉就清空（不会一直粘着）")
	scene_tree.root.remove_child(reentered)
	reentered.free()


## 打完一场回小地图：接着站在原来的位置，而且不会被同一队立刻再抓一次
func _check_battle_return_position(db, state, session_node) -> void:
	session_node.pending_local_scene = "scene_heifengzhai"
	session_node.pending_local_room = ""
	session_node.local_maps.clear()
	# 挑一个有房间敌人的房间，把「战斗前位置」写在它身上（模拟逃回来的那一瞬间）
	var room_row: Resource = null
	for row: Resource in db.rows("dungeon_room"):
		if str(row.scene_id) == "scene_heifengzhai" and not str(row.enemy_team).is_empty():
			room_row = row
			break
	check_not_null(room_row, "黑风寨里有带敌人的房间")
	if room_row == null:
		return
	var second = load(LOCAL_RUN).instantiate()
	second.state_override = state
	scene_tree.root.add_child(second)
	second.setup()
	var team = null
	for enemy in second.teams:
		if enemy.spawn_id == str(room_row.room_id):
			team = enemy
	check_not_null(team, "房间敌人生成在 %s" % str(room_row.room_id))
	if team != null:
		check_false(team.defeated, "这队还没被打掉（逃跑的场景）")
		# 把「战斗前位置」正好写在这队身上：重进图应该被推离接触圈，而不是原地再被抓
		session_node.local_position = team.global_position
		session_node.local_position_scene = "scene_heifengzhai"
		var back = load(LOCAL_RUN).instantiate()
		back.state_override = state
		var reentered: Array = []
		back.battle_switch_handler = func(encounter) -> void: reentered.append(encounter)
		scene_tree.root.add_child(back)
		back.setup()
		var dist: float = back.player.position.distance_to(team.global_position)
		check_gt(
			dist, float(RoamingEnemyScript.CONTACT_DISTANCE) + 20.0,
			"回图时被推到接触圈外足够远（%.0f px；推得少会被墙顶回圈里）" % dist,
		)
		check_true(str(back._status.text).contains("退开"), "回图提示「先退开几步」：%s" % back._status.text)
		# 接触宽限期：落点被墙顶回圈里时，敌人这一下接触不该当场又开一场
		check_gt(float(back._contact_grace), 0.0, "推过就给接触宽限期")
		back._on_encountered(str(room_row.room_id), "front")
		check_eq(reentered.size(), 0, "宽限期内忽略接触（不会被锁在败仗里）")
		back._contact_grace = 0.0
		back._on_encountered(str(room_row.room_id), "front")
		check_eq(reentered.size(), 1, "宽限期过去后照常开战")
		# 位置在别处时照样接着站（不再被弹回入口）
		var hallway := Vector2(1234.0, 567.0)
		session_node.local_position = hallway
		session_node.local_position_scene = "scene_heifengzhai"
		var resume = load(LOCAL_RUN).instantiate()
		resume.state_override = state
		scene_tree.root.add_child(resume)
		resume.setup()
		check_lt(resume.player.position.distance_to(hallway), 1.0, "打完回图接着站在原处")
		scene_tree.root.remove_child(resume)
		resume.free()
		scene_tree.root.remove_child(back)
		back.free()
	scene_tree.root.remove_child(second)
	second.free()


## 城镇设施：客栈按 E 进打坐界面；当铺与悬赏板按 07 文档是无表信息源，给对话线索
func _check_town_facilities(db, state, session_node) -> void:
	session_node.pending_local_scene = "scene_qingfengyi"
	var town = load(LOCAL_RUN).instantiate()
	town.state_override = state
	scene_tree.root.add_child(town)
	town.setup()
	var inn: Node2D = town.world.get_node_or_null("Markers/Facilities/facility_inn")
	check_not_null(inn, "地图上有客栈位点")
	# 城镇没有楼层/房间，HUD 不该硬凑「第 0 层」这种东西
	town._refresh_status()
	check_false(str(town._status.text).contains("第 0 层"), "城镇不硬凑楼层：%s" % town._status.text)
	check_false(str(town._status.text).contains("·"), "城镇不显示房间段：%s" % town._status.text)
	# 城镇的店与设施必须有**看得见的名字**：场景里它们都是看不见的 Marker2D，
	# 不标名字玩家只能挨个走过去按 E 试（这是真事——巡游时我自己都分不清哪栋是哪家）。
	var shop_label: Label = town.find_child("PlaceLabel_bld_grocery", true, false)
	check_not_null(shop_label, "杂货铺有位点标识")
	if shop_label != null:
		check_eq(shop_label.text, "杂货铺", "标识写的是店名（取自 building_def.name_cn）")
	var inn_label: Label = town.find_child("PlaceLabel_facility_inn", true, false)
	check_not_null(inn_label, "客栈有位点标识")
	if inn_label != null:
		check_eq(inn_label.text, "客栈", "设施名写对")
	check_eq(
		(
			(int(town.find_child("PlaceLabel_bld_grocery", true, false) != null)
			+ int(town.find_child("PlaceLabel_bld_tavern", true, false) != null)
			+ int(town.find_child("PlaceLabel_bld_clinic", true, false) != null)
			+ int(town.find_child("PlaceLabel_bld_smith", true, false) != null))
		), 4,
		"四家店都有可见标识",
	)
	# 认不出的设施位点 = 地图／数据错：**不许把位点名印给玩家**（id 只进日志）
	var marker := Marker2D.new()
	marker.name = "facility_does_not_exist"
	var facilities: Node = town.world.get_node_or_null("Markers/Facilities")
	if facilities != null:
		facilities.add_child(marker)
		marker.global_position = town.player.global_position
		var unknown: Dictionary = town.interact()
		check_false(bool(unknown["ok"]), "认不出的设施不假装能交互")
		check_false(
			str(town._status.text).contains("facility_does_not_exist"),
			"认不出设施时也不许把位点名甩给玩家：%s" % town._status.text,
		)
		check_true(str(town._status.text).contains("数据错"), "如实说这是数据错：%s" % town._status.text)
		facilities.remove_child(marker)
		marker.free()
	if inn == null:
		scene_tree.root.remove_child(town)
		town.free()
		return
	town.player.global_position = inn.global_position + Vector2(0, 12)
	check_eq(town.facility_near_player(), "facility_inn", "站到客栈旁边能认出来")
	var opened: Dictionary = town.interact()
	check_true(bool(opened["ok"]), "按 E 进客栈：%s" % opened.get("error", ""))
	check_not_null(town.cultivate_panel, "打坐界面挂上了")
	if town.cultivate_panel != null:
		check_true(town.cultivate_panel.status_text().contains("打坐"), "状态栏提示打坐：%s" % town.cultivate_panel.status_text())
	town.close_cultivate()
	check_true(town.cultivate_panel == null, "关掉后引用清空")

	# 当铺：07 文档写它是「无表（对话线索）」，要提到「寨里关着个疯子」（醉刀客的线索来源之一）
	var pawn: Node2D = town.world.get_node_or_null("Markers/Facilities/facility_pawnshop")
	check_not_null(pawn, "地图上有当铺位点")
	if pawn != null:
		town.player.global_position = pawn.global_position + Vector2(0, 12)
		check_eq(town.facility_near_player(), "facility_pawnshop", "站到当铺旁边能认出来")
		var notice: Dictionary = town.interact()
		check_true(bool(notice["ok"]), "当铺能读到对话线索：%s" % str(notice))
		check_true(str(town._status.text).contains("疯子"), "当铺老板提到寨里关着个疯子：%s" % town._status.text)

	# 悬赏板：07 文档写它是「无表（信息源）」，第一章唯一引子——沈家小姐被掳
	var board: Node2D = town.world.get_node_or_null("Markers/Facilities/facility_bounty_board")
	check_not_null(board, "地图上有悬赏板位点")
	if board != null:
		town.player.global_position = board.global_position + Vector2(0, 12)
		check_eq(town.facility_near_player(), "facility_bounty_board", "站到悬赏板旁边能认出来")
		var posted: Dictionary = town.interact()
		check_true(bool(posted["ok"]), "悬赏板能读到告示：%s" % str(posted))
		check_true(str(town._status.text).contains("沈家小姐"), "悬赏板给出第一章引子：%s" % town._status.text)
		# 读告示板 = 点亮引导第一步的旗标（09 §3.1）：这一条把**旗标名字**钉住——
		# 控制器里的常量与 `guide_step.csv` 的 condition 是两处，改错一边引导就永远停在第 1 步。
		check_true(state.has_flag("flag_board_read"), "读悬赏板点亮 flag_board_read")
		# 序幕·择念（20 §3.1／0.29.1 v2 第 4 条）：**与引子同框**——同一按把它摆出来，
		# 而且是**旁白模式**（不含头像／好感条／交往段：那句话是主角替自己说的）。
		check_eq(str(posted.get("story", "")), "dl_opening_choice", "同一按摆出序幕择念：%s" % str(posted))
		var story_panel = town.npc_panel
		check_not_null(story_panel, "择念用的是一块面板")
		if story_panel != null:
			check_eq(str(story_panel.mode), "story", "走的是旁白模式（不是交往菜单）")
			check_eq(str(story_panel.dialogue_node_id), "dl_opening_choice", "说的就是那一问")
			check_eq(str(story_panel._favor_label.text), "", "旁白模式不摆好感条")
			# 交往段整个收起来：面板上不该出现「好感／赠送／切磋／偷窃」这些字样
			var story_words := ""
			for node: Node in story_panel._actions.get_children():
				if node is Label:
					story_words += str(node.text)
				elif node is Button:
					story_words += str(node.text)
			var has_favor_ui := false
			for word: String in ["好感", "赠送", "切磋", "偷窃"]:
				if story_words.contains(word):
					has_favor_ui = true
			check_false(has_favor_ui, "旁白模式不出现交往字样：%s" % story_words)
			check_not_null(
				story_panel._actions.find_child("TalkOptionopt_open_yi", true, false),
				"三选一之一是真按钮",
			)
			var picked: Dictionary = story_panel.choose_dialogue("opt_open_yi")
			check_true(bool(picked.get("ok", false)), "选得动：%s" % str(picked.get("error", "")))
			check_true(state.has_flag("heart_yi"), "选完心性进存档")
			town.close_npc()
			# 选过之后同一按不再弹（条件在表里）
			var again: Dictionary = town.interact()
			check_false(again.has("story"), "心性定过之后不再问（%s）" % str(again))
		state.flags.erase("heart_yi")
	scene_tree.root.remove_child(town)
	town.free()
	session_node.pending_local_scene = ""


## 政策：**每个「有名字的设施」都必须真的能交互**。
##
## `FACILITY_LABELS` 是一处清单（界面上给设施起名靠它），`interact()` 里的分支是另一处——
## 两边一错位，玩家在图上按 E 就会听到兜底那句「这里还没有可交互的内容（数据错，已记进日志）」，
## 而日志里还会真的记一条**假的数据错**。
##
## 2026-10-04 真事：0.31.0 新加的**书铺**（21 §九 陆文昭本命机遇的地点）进了 `FACILITY_LABELS`
## 与地图（地图验收也点了名），却没进 `interact()` 的分支——上面那条「认不出的设施」用例只造了一个
## 假位点，验的是「兜底那句在不在」，验不了「真位点是不是都进了分支」。所以这里改成**逐个真位点按一遍 E**。
##
## 用**独立的存档与场景实例**：客栈那一次会收人（燕小七），别把共用 state 的队伍改了
## （那会把 `_check_town_gamble` 的软判定顶成「达标必过」，本文件踩过）。
func _check_every_facility_is_interactable(db, session_node) -> void:
	var probe = solo_state(db)
	session_node.pending_local_scene = "scene_qingfengyi"
	var town = load(LOCAL_RUN).instantiate()
	town.state_override = probe
	# 事件判定／触发点会触发自动存档——headless 下写 user:// 会崩，必须给临时目录（踩过）
	var store = SaveStoreScript.new(SAVE_TEST_DIR, 3)
	store.ensure_dir()
	town.save_store_override = store
	scene_tree.root.add_child(town)
	town.setup()
	var checked := 0
	for facility_id: String in town.FACILITY_LABELS.keys():
		var label := str(town.FACILITY_LABELS[facility_id])
		var marker: Node2D = town.world.get_node_or_null("Markers/Facilities/%s" % facility_id)
		check_not_null(marker, "地图上有%s位点（%s）" % [label, facility_id])
		if marker == null:
			continue
		town.player.global_position = marker.global_position + Vector2(0, 12)
		check_eq(town.facility_near_player(), facility_id, "站到%s旁边能认出来" % label)
		if facility_id == "facility_bookshop":
			# 21 §九 给书生的本命机遇定的地点就是**清风驿·书铺**，条件是「看过悬赏板」——
			# 那条旗标在**同一个城镇里**点亮；书铺这一按会顺手再判一次剧情节点（决策 345 的续），
			# 不然玩家读完板走到书铺会扑空（得当众出镇再进来才发）。
			probe.set_flag("flag_board_read")
		var result: Dictionary = town.interact()
		checked += 1
		check_ne(
			str(result.get("error", "")), "facility_unsupported",
			"%s（%s）按 E 走的是自己的分支，不是兜底那句：%s" % [facility_id, label, str(result)]
		)
		check_true(
			bool(result.get("ok", false)),
			"%s（%s）按 E 有反应：%s" % [facility_id, label, str(result)]
		)
		if facility_id == "facility_bookshop":
			check_true(
				probe.has_flag(StoryServiceScript.done_flag("opp_scholar")),
				"书生读过悬赏板之后，站到书铺按 E 就领到本命机遇"
			)
			check_true(
				str(town._status.text).contains("知白"),
				"状态栏把《玄微心法·知白》念出来：%s" % str(town._status.text)
			)
		# 可能开了面板（客栈→打坐／招募，悬赏板→择念）：关掉再测下一个
		town.close_npc()
		town.close_cultivate()
		town.close_shop()
	check_eq(checked, 4, "四个设施位点逐个按过 E（客栈／当铺／悬赏板／书铺）")
	scene_tree.root.remove_child(town)
	town.free()
	session_node.pending_local_scene = ""


## 剧情招募在城镇里的触发点（09 §3.2）：**只有客栈**那一次交互收人。
##
## 这条口径是被本文件里那场赌局逼出来的：一开始「在清风驿任意位置按 E 就收人」，
## 结果赌局用例跑到时队伍已经从 1 人变 2 人、最高的运从 5 变 7，
## 软判定的「掷骰」分支变成「达标必过」，那条断言当场红。所以城镇必须钉到客栈。
## 用**独立的存档与场景实例**跑，免得把上面那份共用 state 的队伍改了。
func _check_town_recruit_at_inn(db, session_node) -> void:
	var state = solo_state(db)
	session_node.pending_local_scene = "scene_qingfengyi"
	var town = load(LOCAL_RUN).instantiate()
	town.state_override = state
	scene_tree.root.add_child(town)
	town.setup()
	var board: Node2D = town.world.get_node_or_null("Markers/Facilities/facility_bounty_board")
	var inn: Node2D = town.world.get_node_or_null("Markers/Facilities/facility_inn")
	check_not_null(board, "悬赏板位点（招录用例）")
	check_not_null(inn, "客栈位点（招录用例）")
	# 条件旗标先点亮（等于玩家已经看过告示板）
	state.set_flag("flag_board_read")
	var before := state.party_size()
	if board != null:
		town.player.global_position = board.global_position + Vector2(0, 12)
		town.interact()
		check_eq(state.party_size(), before, "在悬赏板按 E 不收人（城镇只认客栈）")
		# 悬赏板那一按现在还会摆出序幕择念（20 §3.1）：关掉它再走下一步，
		# 免得把「客栈收人」这一步测在别的浮层开着的情况下
		town.close_npc()
	if inn != null:
		town.player.global_position = inn.global_position + Vector2(0, 12)
		var joined: Dictionary = town.interact()
		check_eq(state.party_size(), before + 1, "在客栈按 E 才把同伴收进来")
		check_eq(Array(joined.get("recruited", [])).size(), 1, "返回里写清收的是谁")
	scene_tree.root.remove_child(town)
	town.free()
	session_node.pending_local_scene = ""


## 城镇木桩的入口（设计 09 §3.3）：按 E 走到木桩那儿 → 到顶时给一句提示、不进战斗。
##
## 这里只验**不依赖木桩战斗数据**的那半边（到顶提示 + 认得出这个建筑）——「真开打」要等设计侧
## 补 `enemy_team`／`enemy_base` 两行（Q52），那半边由 `test_practice` 用内存夹具验。
## 位点是**测试时临时挂上去的** Marker：地编还没在清风驿校场摆 `bld_dummy`
## （`验证清单` 的 map_assets 行里记着这条待补）。
func _check_dummy_practice_entry(db, session_node) -> void:
	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	session_node.pending_local_scene = "scene_qingfengyi"
	var town = load(LOCAL_RUN).instantiate()
	town.state_override = state
	scene_tree.root.add_child(town)
	town.setup()
	var buildings: Node = town.world.get_node_or_null("Markers/Buildings")
	check_not_null(buildings, "清风驿有建筑位点组（木桩入口用例）")
	if buildings == null:
		scene_tree.root.remove_child(town)
		town.free()
		session_node.pending_local_scene = ""
		return
	var marker := Marker2D.new()
	marker.name = "bld_dummy"
	buildings.add_child(marker)
	marker.global_position = town.player.global_position
	check_eq(town.building_near_player(), "bld_dummy", "站到木桩旁边认得出它")

	# ① 练到顶：给提示、不开战、不组遭遇
	# 前面的用例可能留下「待处理遭遇」——这里先清空，才能把「木桩没有组遭遇」验准
	session_node.pending_encounter = null
	state.char_levels[char_id] = PracticeServiceScript.cap_level(db)
	var capped: Dictionary = town.interact()
	check_false(bool(capped["ok"]), "到顶后按 E 不开战")
	check_true(bool(capped.get("capped", false)), "返回里写明是「到顶」")
	check_eq(str(town._status.text), PracticeServiceScript.cap_notice(db), "状态栏就是表里那句提示（不自己编）")
	check_null(session_node.pending_encounter, "到顶时没有组出遭遇")
	# ② 没到顶：**两种数据状态都要有诚实的结果**——要么真开打（且标成练习战），
	#    要么点名缺的是哪张表的行。写成这样是为了「设计补表那天不用改用例」
	#    （两边都断言会踩到自检的假绿门限：总有一边的断言跑不到）。
	state.char_levels[char_id] = 1
	var second: Dictionary = town.interact()
	var started: bool = bool(second.get("ok", false))
	check_true(started == bool(second.get("practice", false)), "开打了就一定是练习战：%s" % str(second))
	check_true(
		started or str(second.get("error", "")).contains("enemy_"),
		"没开打时必须点名缺的是哪张表的行：%s" % str(second.get("error", ""))
	)
	check_true(
		str(town._status.text).contains("还没准备好") or str(town._status.text).contains("木桩练习"),
		"状态栏是两种诚实结果之一：%s" % town._status.text
	)

	buildings.remove_child(marker)
	marker.free()
	scene_tree.root.remove_child(town)
	town.free()
	session_node.pending_local_scene = ""


## 宝箱守卫（设计 09 §一）：荒村废屋那个箱子被屠夫守着——
## 文取要一份草药汤（按两次 E：第一次让他开口、第二次付账），武取就是房里那场仗。
## 用**独立的存档与场景实例**跑，不动上面那份共用 state。
func _check_guarded_chest(db, session_node) -> void:
	var state = solo_state(db)
	session_node.pending_local_scene = "scene_huangcun"
	var map = load(LOCAL_RUN).instantiate()
	map.state_override = state
	scene_tree.root.add_child(map)
	# 站在箱子边不该顺手把屠夫那支队伍也撞开——把切战斗拦下来
	map.battle_switch_handler = func(_encounter) -> void: pass
	map.setup()
	var chest = null
	for candidate in map.chests:
		if str(candidate.get_meta("key", "")).begins_with("hc_02|"):
			chest = candidate
	check_not_null(chest, "荒村里有 hc_02 的宝箱")
	if chest == null:
		scene_tree.root.remove_child(map)
		map.free()
		session_node.pending_local_scene = ""
		return
	check_eq(str(chest.get_meta("key")), "hc_02|drop_chest_silver", "认出来的就是屠夫守的那个箱子")
	map.player.global_position = chest.global_position
	# 第一次按 E：只是让屠夫开口（不开箱、不扣东西），而且要把**两条路都摆出来**
	var first: Dictionary = map.interact()
	check_false(bool(first["ok"]), "守卫挡着时开不了箱")
	check_eq(str(first.get("guard", "")), "guard_hc_02", "返回里写清是哪位守卫")
	check_eq(str(first.get("stage", "")), "demand", "第一次是「他开口」那一阶段")
	check_true(str(map._status.text).contains("屠夫"), "状态栏说清谁挡着：%s" % map._status.text)
	check_true(str(map._status.text).contains("草药汤"), "开口文案写了道具那条路：%s" % map._status.text)
	check_true(str(map._status.text).contains("医术"), "开口文案写了判定那条路：%s" % map._status.text)
	check_false(chest.opened_already, "箱子没被打开")

	# 第二次按 E：书生的医术判定 = 3 + 智10/5 = 5 ≥ 门槛 3 → **判定路免费放行**；
	# 而且不该顺手收走背包里的草药汤（控制器优先走「不花东西」的那条，0.14.0）
	state.inventory.add_item(db, "item_med_01", 1)
	var paid: Dictionary = map.interact()
	check_true(bool(paid["ok"]), "判定过了就放行：%s" % str(paid.get("error", "")))
	check_eq(str(paid.get("stage", "")), "peace", "第二阶段是「文取成功」")
	check_eq(state.inventory.count("item_med_01"), 1, "走判定路不消耗草药汤")
	check_true(state.has_flag("flag_guard_guard_hc_02_peace"), "文取解决写进存档（旗标）")
	var opened: Dictionary = map.interact()
	check_true(bool(opened["ok"]), "让开之后按 E 就能开箱：%s" % str(opened.get("error", "")))
	check_true(chest.opened_already, "箱子这时才标成已开")

	# 武取那条路：另一份存档，直接把「房间已清」写进记录 → 箱子直接能开（不再走守卫流程）
	var fighter = solo_state(db)
	fighter.record_dungeon("scene_huangcun", "rooms", "hc_02")
	# 第一张图的会话已经把那个箱子记成「开过」了，这里要清掉本图的会话记录，
	# 否则第二张图生成的箱子一出生就是「已开」，压根没有可交互目标
	session_node.local_maps.erase("scene_huangcun")
	var second_map = load(LOCAL_RUN).instantiate()
	second_map.state_override = fighter
	scene_tree.root.add_child(second_map)
	second_map.battle_switch_handler = func(_encounter) -> void: pass
	second_map.setup()
	var second_chest = null
	for candidate in second_map.chests:
		if str(candidate.get_meta("key", "")).begins_with("hc_02|"):
			second_chest = candidate
	check_not_null(second_chest, "第二份存档里也有那个箱子")
	if second_chest != null:
		second_map.player.global_position = second_chest.global_position
		var after_fight: Dictionary = second_map.interact()
		check_true(bool(after_fight["ok"]), "打赢房间之后按 E 直接开箱：%s" % str(after_fight.get("error", "")))
	scene_tree.root.remove_child(second_map)
	second_map.free()
	scene_tree.root.remove_child(map)
	map.free()
	session_node.pending_local_scene = ""


func _check_exit(map, session_node, returns: Array) -> void:
	# 模拟「从大地图某处走进入口」：`enter_local_map()` 会把当时那一步的位置写进 world_position
	var entry_spot := Vector2(744, 392)
	session_node.world_position = entry_spot
	map.leave_to_overworld()
	check_eq(returns.size(), 1, "出口触发回大地图")
	check_false(session_node.local_maps.has("scene_heifengzhai"), "回大地图后该图状态整片刷新")
	check_eq(session_node.pending_local_scene, "", "清掉待进入的小地图")
	var local_row: Resource = map.db.get_row("map_local", "scene_heifengzhai")
	var region: Resource = map.db.get_row("map_region", str(local_row.parent_node))
	# 设计 02：「返回大地图**原位置**」。原位置 = 进图那一步记下的坐标，
	# 不是地标坐标本身——不然回程会正好压在入口位点上，被自动进图条立刻送回图（死循环）。
	check_eq(session_node.world_position, entry_spot, "回程落点是进图时记下的大地图原位置")
	check_ne(session_node.world_position, Vector2(float(region.pos_x), float(region.pos_y)), "回程落点不该被改写成地标坐标")


## 在店里把药买齐之后，HUD **当场**推进（决策 347）。
##
## 起因：`flag_supplies_ready` 的口径是「等级 ≥ 5 ＋ 背包里有消耗品」，而它原本只在**换图／换房间**时算。
## 玩家正站在药铺里把药买齐，HUD 却还停在「把家伙和药备齐」（09 §3.2 的循环就是「铁匠铺与酒楼把家伙和药备齐」），
## 要出镇再进来才推进——**同一张图里做完的事，判据却在场景边界上**（与决策 345／346 同一族）。
func _check_guide_after_shop_close(db, session_node) -> void:
	var state = solo_state(db)
	session_node.pending_local_scene = "scene_qingfengyi"
	var town = load(LOCAL_RUN).instantiate()
	town.state_override = state
	var store = SaveStoreScript.new(SAVE_TEST_DIR, 3)
	store.ensure_dir()
	town.save_store_override = store
	scene_tree.root.add_child(town)
	town.setup()
	# 练到 5 级（打副本那种状态），但背包里还没有消耗品
	state.char_levels[state.char_ids[0]] = 5
	GuideServiceScript.refresh_derived_flags(db, state)
	check_false(state.has_flag("flag_supplies_ready"), "等级够了但没药：还没备齐")
	# 在药铺里买齐 → 关掉浮层
	town.open_shop("bld_clinic")
	var bought: Dictionary = state.inventory.add_item(db, "item_potion_small", 1)
	check_true(bool(bought.get("ok", false)), "买到一份金创药：%s" % str(bought))
	town.close_shop()
	check_true(
		state.has_flag("flag_supplies_ready"),
		"关掉药铺浮层的那一下就把「备齐」算出来了（不用出镇再进来）"
	)
	# 期望值**不抄文案**：0.32.0 把第 4 步从「出城上山」改成「出城往落雁坡走，从那儿上山。」
	# （落雁坡改成 `proximity_5` 后不点名玩家会摸不着北），抄死子串的写法当场过期成假红。
	# 改成问 `GuideService`「现在该显示哪一步」——状态是在关浮层那一刻才变的，
	# label 若没跟着刷新就还是上一步的文案，这条照样红（不是同义反复）。
	var expected_hud := GuideServiceScript.hud_text(db, state)
	check_true(
		str(town._guide_label.text) == expected_hud,
		"HUD 当场换成下一步：%s（表里现在该显示的是 %s）" % [str(town._guide_label.text), expected_hud]
	)
	# 再钉一层，免得上面那条靠"两边都是空串"假绿：这一步必须真是「备齐之后」那一步。
	var supplies_text := ""
	for row: Resource in GuideServiceScript.steps(db):
		if str(row.condition) == "flag_supplies_ready":
			supplies_text = str(row.text_cn)
	check_true(
		not supplies_text.is_empty() and expected_hud.contains(supplies_text),
		"关浮层那一下推进到「备齐之后」那一步（flag_supplies_ready → 「%s」）" % supplies_text
	)
	scene_tree.root.remove_child(town)
	town.free()
	session_node.pending_local_scene = ""
