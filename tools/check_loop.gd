## 跨场景闭环自检：在真引擎里跑**真实的场景切换**（共 11 段），逐条验证衔接处。
##
## 为什么单独有这一条：用例大多注入 `handler` 拦下切场景（不然会把测试的树换掉），
## 于是「切过去之后落点对不对、会话清没清、会不会立刻被弹回去」这类问题用例看不见。
## 2026-10-02~03 连着挖出的四个真 bug 全在这一层：后山密道跨图走不通、进清风驿被立刻弹回、
## 出小地图又被立刻送回、驿站传送把回程落点记成驿站。
## 2026-10-03 又加了一段「酒葫芦 → 隐藏 Boss → 回小地图」：这条链的**交接**（触发器的 encounter
## 过 GameSession 到战斗场景、打完回图）以前只有两半用例，跨场景这一步没人跑过。
##
## 用法：`tools\check_loop.bat`（已接进 `tools\run_all_checks.bat`）。
## 通过标记 `LOOPCHECK: OK`（run_bat 按标记判定，不看退出码）。
extends SceneTree

const WORLD := "res://scenes/world_run.tscn"
const LOCAL_RUN := "res://scenes/local_run.tscn"
const MENU := "res://scenes/main_menu.tscn"
## 创建角色界面（0.17.0）：新建游戏先过这里，「确认创建」才落盘
const CREATION := "res://scenes/creation_screen.tscn"


func _initialize() -> void:
	var lines := PackedStringArray()
	var ok := true
	var db = load("res://src/core/table_db.gd").new()
	db.load_all()
	var session = root.get_node_or_null("GameSession")
	var state = load("res://src/core/game_state.gd").new_game(db, "normal")
	session.set_state(state)
	session.world_position = Vector2.ZERO
	for char_id: String in state.char_ids:
		state.append_level(char_id, 19)
	# 只留目标明雷：追击型／巡逻型会在回图那一瞬间发现玩家并立刻开第二场（那是它们该有的行为），
	# 会把「回图落点」这件事盖住，这里先只留一只。
	# 0.8.1：大地图明雷默认关着（`feature_toggle.overworld_roaming_enemy=0`），
	# 这一段验的正是明雷机制本身 —— 所以在闭环里**把开关打开**（设计原文：
	# "开关打开时，闭环里的「明雷战斗（胜／败两种结局）」那一整段照旧能跑"）。
	_keep_only(db, session, "sp_lp_wolf_01")
	# 0.8.1：大地图明雷默认是关的（`feature_toggle.overworld_roaming_enemy=0`），
	# 而闭环的 ③／③b 验的正是明雷机制本身 —— 在这里**打开开关**（设计原文：
	# "开关打开时，闭环里的「明雷战斗（胜／败两种结局）」那一整段照旧能跑"）。
	# 放会话上：大地图每次重建场景都从会话读，控制器上的字段活不过重建（踩过）。
	session.roaming_enabled_override = 1

	# ---------- ① 大地图 → 小地图 ----------
	change_scene_to_file(WORLD)
	await _frames(3)
	var world = current_scene
	if world == null or not world.has_method("enter_local_map"):
		print("LOOPCHECK: FAIL 大地图没起来")
		quit(1)
		return
	var portal: Vector2 = world.player.global_position
	var region: Resource = db.get_row("map_region", "n_heifengzhai")
	portal = Vector2(float(region.pos_x), float(region.pos_y))
	# 0.32.0：**开局只有城镇与驿站亮着**，落雁坡（野外）要走到跟前才揭开；而黑风寨的入口条件
	# 正是 `discover_luoyanpo`——所以先按真实走位把主路走一趟（别绕过入口条件去开那扇门）。
	var slope: Node2D = world.world.get_node_or_null("Markers/Node_n_luoyanpo")
	if slope != null:
		world.player.global_position = slope.global_position
		await _frames(2)
	# 地标名字：已揭开的才有（未揭开写出来就是剧透）——走完这一趟是清风驿／驿站／落雁坡／黑风寨四处
	var named := 0
	for label in world.find_children("NodeLabel_*", "Label", true, false):
		if label.visible:
			named += 1
	# 模拟真实走位：先站在圈外（入口武装），再走进圈里。直接摆在入口上会被武装条挡住——
	# 那条正是防「回图落点压在入口上又立刻进图」的。
	world.player.global_position = portal + Vector2(200, 0)
	await _frames(2)
	world.player.global_position = portal
	await _frames(4)
	var local = current_scene
	var in_local: bool = local != null and local.has_method("current_room_id")
	ok = ok and in_local
	lines.append("大地图→小地图：切过去了=%s（图=%s 宝箱=%d 触发=%d 地标名=%d）" % [
		in_local, str(local.scene_id) if in_local else "-",
		local.chests.size() if in_local else -1, local.triggers.size() if in_local else -1, named,
	])
	ok = ok and named >= 4
	if not in_local:
		print("LOOPCHECK: FAIL 进不了小地图")
		quit(1)
		return

	# ---------- ② 小地图 → 回大地图（原位置，且不会被立刻送回） ----------
	var entry_spot: Vector2 = session.world_position
	local.leave_to_overworld()
	await _frames(4)
	var back = current_scene
	var back_is_world: bool = back != null and back.has_method("enter_local_map")
	var back_pos: Vector2 = back.player.global_position if back_is_world else Vector2.INF
	var cleared_state: bool = not session.local_maps.has("scene_heifengzhai")
	var back_ok: bool = back_is_world and back_pos.distance_to(entry_spot) <= 1.0 and cleared_state
	ok = ok and back_ok
	lines.append("小地图→大地图：回去了=%s 落点=原位置=%s 会话已清=%s" % [
		back_is_world, back_is_world and back_pos.distance_to(entry_spot) <= 1.0, cleared_state,
	])
	# 站在入口上再跑几帧也不该被自动送回小地图（这就是那个死循环）
	await _frames(6)
	var still_world: bool = current_scene != null and current_scene.name == "WorldRun"
	ok = ok and still_world
	lines.append("站回入口再跑 6 帧：还留在大地图=%s" % still_world)

	# ---------- ③ 明雷战斗 → 回大地图（胜负两种结局都验） ----------
	ok = ok and await _battle_round(session, "sp_lp_wolf_01", lines)
	# ③b 同一条明雷**再打一次**：把队伍打残（1 级 + 1 点气血），逼出"没赢"的结局 ----------
	# （它不算新的段标记：属于 ③ 那一"段"——同一只明雷、胜负两种结局都验。）
	# 为什么单开一段：③ 那句"胜负两种结局都验"以前是**假的**——循环把全队 +19 级，
	# `winner == "ally"` 恒成立，`if winner != "ally"` 那条分支（明雷不清 + 人被推离接触圈）
	# **写了但从没跑过**（见框架说明决策 206）。
	# 顺便给这一场准备「最近到过的城镇」与一条战斗外增益：8.0 的战败处理要回城镇、清增益、回满气血。
	session.note_shelter("scene_qingfengyi", "清风驿")
	session.add_field_buff("buff_meditated", 10)
	ok = ok and await _battle_round(session, "sp_lp_wolf_01", lines, true)

	# ---------- ④ 驿站传送 → 进图 → 从目的地回图 ----------
	ok = ok and await _waypoint_round(db, state, session, lines)

	# ---------- ⑤ 房间战斗 → 回小地图 ----------
	ok = ok and await _room_battle_round(state, lines)

	# ---------- ⑥ 副本巡游：开箱 → 完成度 → 可扫荡 ----------
	ok = ok and _dungeon_round(state, lines)

	# ---------- ⑦ 存档 → 读档（真文件，走 SaveStore 的实际读写路径） ----------
	ok = ok and _save_round(state, db, lines)

	# ---------- ⑧ 主菜单新建游戏 →（第二局）小镇：进店／打坐／手动存档读档 ----------
	ok = ok and await _menu_and_town_round(db, lines)

	# ---------- ⑨ 酒葫芦 → 隐藏 Boss → 回小地图（跨场景交接） ----------
	# 这一段跑在 ⑧ 新建的那一局上。为什么单独有这一段：这条链以前只有**两半**用例
	# （test_local_map 验「触发器建出正确的 encounter」、test_battle_ui 验「结算把紫名 Boss 记下来」，
	# 而后者那场是**手工**建的 encounter）。中间「触发器的 encounter 过 GameSession 到战斗场景、
	# 打完再回图」没人跑过——而跨场景交接正是历史上最爱出错的一层。
	ok = ok and await _wine_boss_round(db, session.state, session, lines)

	# ---------- ⑩ 招募链：读告示板 → 出图 → 回来在客栈入队 → 存档往返仍是 2 人 ----------
	# 为什么单独有这一段：招募是**跨场景 + 存档**的行为——条件旗标在**这一次**进图点亮、
	# 人在**下一次**进图入队。两半用例各验一半（test_local_map 验「客栈按 E 入队」、
	# test_recruit 验服务层），中间「出图再回来」这一步没人跑过，而跨场景正是最爱出错的一层。
	ok = ok and await _recruit_round(db, session, lines)

	# ---------- ⑪ 宝箱守卫：荒村屠夫挡箱 → 文取（给草药汤）→ 开箱 → 存档往返不复活 ----------
	ok = ok and await _guard_round(db, session, lines)

	# ---------- ⑫ 幕五对质 → 账册 → 终局三选一（跨场景：战斗场景写、小地图读） ----------
	# 为什么单独有这一段：这条链的**交接面**是「战斗结算（写 `last_battle`）→ 回到小地图
	# （`setup()` 里按它摆出择念）」。两半用例各验一半（test_battle_ui 验首杀发账册、
	# test_local_map 验摆面板），中间那一跳没人跑过——而"跨场景交接"正是历史上最爱出错的一层
	# （决策 48／49／50 的四个真 bug 全在这一层）。
	ok = ok and await _ledger_round(db, session, lines)

	for line: String in lines:
		# 行首留 ASCII 标记：run_bat 用 findstr 抓 `LOOP`（中文模式 findstr 抓不到）
		print("LOOP " + line)
	if ok:
		print("LOOPCHECK: OK")
		quit(0)
	else:
		printerr("LOOPCHECK: FAIL")
		quit(1)


## 撞一只明雷 → 真打一场 → 回大地图 → 按结局检查「明雷清没清 + 人落在哪」
func _battle_round(session, spawn_id: String, lines: PackedStringArray, crippled: bool = false) -> bool:
	var saved_levels: Dictionary = {}
	var saved_hp: Dictionary = {}
	if crippled:
		# 复活它（③ 赢了之后它被记成"已清"），再重进大地图让明雷按会话重建。
		session.cleared_spawns.erase(spawn_id)
		change_scene_to_file(WORLD)
		await _frames(4)
		# 打残：等级 1 + 气血 1。等级只能加不能减（`append_level`），所以这里直接写 `char_levels`——
		# 这是诊断脚本，跟上面直接摆 `world_position` 一个性质；打完**原样还原**，别影响后面几段。
		for char_id: String in session.state.char_ids:
			saved_levels[char_id] = session.state.level_of(char_id)
			saved_hp[char_id] = session.state.current_hp_of(char_id)
			session.state.char_levels[char_id] = 1
			session.state.set_current_hp(char_id, 1)
	var world = current_scene
	var enemy = null
	for each in world.enemies:
		if each.spawn_id == spawn_id:
			enemy = each
	if enemy == null:
		lines.append("%s：地图上找不到这只明雷" % spawn_id)
		return false
	var spot: Vector2 = enemy.global_position
	world.player.global_position = spot
	await _frames(8)
	var battle = current_scene
	if battle == null or not battle.has_method("press_auto"):
		lines.append("%s：没进战斗场景" % spawn_id)
		return false
	var result := _auto_battle(battle)   # press_return 之后对象会被释放，结果先取出来
	var winner := str(result["winner"])
	var rounds := int(result["rounds"])
	battle.press_return()
	await _frames(8)
	var back = current_scene
	var defeat_line := ""
	# 战败处理（08 已定）：全队回到**最近到过的城镇**、气血回满、战斗外增益清空。
	# 这一局前面进过清风驿（`note_shelter`），所以回程应当是小地图里的那座城镇——
	# 验完再离开城镇回大地图，后面几段才接得上。
	if crippled and winner != "ally":
		var shelter := str(session.last_shelter_scene)
		var shelter_name := str(session.last_shelter_name)
		var town_return: bool = (
			back != null and back.has_method("current_room_id") and str(back.scene_id) == shelter
		)
		var healed := true
		for char_id: String in session.state.char_ids:
			if session.state.is_wounded(char_id):
				healed = false
		var field_cleared: bool = session.active_field_buffs().is_empty()
		var defeat_ok: bool = town_return and healed and field_cleared
		defeat_line = "战败回城：回%s=%s（%s）气血回满=%s 战斗外增益已清=%s" % [
			shelter, town_return, shelter_name, healed, field_cleared,
		]
		if not defeat_ok:
			lines.append(defeat_line)
			return false
		if back != null and back.has_method("leave_to_overworld"):
			back.leave_to_overworld()
			await _frames(6)
			back = current_scene
	if back == null or back.name != "WorldRun":
		var dbg_spawn := ""
		if session.pending_encounter != null:
			dbg_spawn = str(session.pending_encounter.spawn_id)
		lines.append("%s：回程不是大地图（当前 %s；pending_return=%s pending_local=%s 已清记录=%d 待处理遭遇=%s）" % [
			spawn_id, back.name if back != null else "无",
			str(session.pending_return_scene), str(session.pending_local_scene),
			session.cleared_spawns.size(), dbg_spawn,
		])
		return false
	var cleared: bool = session.cleared_spawns.has(spawn_id)
	var dist: float = back.player.global_position.distance_to(spot)
	var alive := false
	for each in back.enemies:
		if each.spawn_id == spawn_id and not each.defeated and each.visible:
			alive = true
	lines.append("明雷战斗（%s）：胜者=%s 回合=%d → 明雷已清=%s 明雷还在=%s 回图距接触点=%.1f" % [
		spawn_id, winner, rounds, cleared, alive, dist,
	])
	if not defeat_line.is_empty():
		lines.append(defeat_line)
	for char_id: String in saved_levels:
		session.state.char_levels[char_id] = saved_levels[char_id]
		session.state.set_current_hp(char_id, saved_hp[char_id])
	if crippled:
		# 这一段存在的意义就是"没赢"：真赢了说明打残没生效（或这只明雷太弱），当场说清，
		# 否则它会变成第二次"赢"、这条路径仍然是没跑过。
		if winner == "ally":
			lines.append("%s：打残后仍然赢了——这一段没验到败北路径" % spawn_id)
			return false
	if winner == "ally":
		# 赢了：明雷记清、人就落在接触点（不用被推开）
		return cleared and not alive and dist <= 40.0
	# 没赢（败北/撤退）：明雷不清，人被推离接触圈（否则回图瞬间再被抓）
	return (not cleared) and alive and dist > 22.0


## 驿站传送 → 进图 → 从目的地回图：回程落点 = **目的地地标**（不是驿站），且不会被立刻送回
##
## 主菜单新建游戏 → 小镇：这一段把「整局游戏的头尾」也串上——主菜单真按钮新建、落盘，
## 然后进清风驿走一遍进店／打坐／手动存档／读档一致。前面六段都是「已有存档」的路径，
## 这一段从**没有存档**开始。
func _menu_and_town_round(db, lines: PackedStringArray) -> bool:
	var store = load("res://src/core/save_store.gd").new("res://.logs/loopcheck/saves", 3)
	store.ensure_dir()
	for slot in range(1, 4):
		store.delete_slot(slot)
	change_scene_to_file(MENU)
	await _frames(3)
	var menu = current_scene
	if menu == null or not menu.has_method("press_new_game"):
		lines.append("主菜单没起来")
		return false
	menu.store_override = store
	menu.controller = null          # 让 setup 用注入的 store 重建
	menu.setup()
	var created: Dictionary = menu.press_new_game()
	# 0.17.0 起「新建游戏」先过**创建角色界面**（创建没走完不写存档），
	# 所以这里要接着把创建界面走完——落盘发生在「确认创建」那一刻。
	var went_creation: bool = menu.pending_scene == menu.PLACEHOLDER_SCENE
	change_scene_to_file(CREATION)
	await _frames(3)
	var creation = current_scene
	var creation_ok := creation != null and creation.has_method("confirm")
	if creation_ok:
		creation.store_override = store
		creation.setup()
		creation.pick_origin("ori_scholar")
		var confirmed: Dictionary = creation.confirm()
		creation_ok = bool(confirmed.get("ok", false))
	var session = root.get_node_or_null("GameSession")
	var state = session.state if session != null else null
	var new_ok: bool = bool(created.get("ok", false)) and went_creation and creation_ok \
		and state != null and store.slot_exists(1)
	lines.append("主菜单新建游戏（经创建界面）：ok=%s 落盘=%s" % [
		creation_ok, store.slot_exists(1),
	])
	if not new_ok:
		return false

	# 进清风驿（城镇）：进店 → 打坐 → 手动存档 → 读档一致
	session.pending_local_scene = "scene_qingfengyi"
	session.pending_local_room = ""
	session.local_position_scene = ""
	change_scene_to_file(LOCAL_RUN)
	await _frames(6)
	var town = current_scene
	if town == null or not town.has_method("can_save_here"):
		lines.append("进不了清风驿")
		return false
	town.save_store_override = store
	town._save_service = null
	var grocery: Node2D = town.world.get_node_or_null("Markers/Buildings/bld_grocery")
	var shop_ok := false
	if grocery != null:
		town.player.global_position = grocery.global_position
		var opened: Dictionary = town.interact()
		shop_ok = bool(opened.get("ok", false)) and town.shop_panel != null
		town.close_shop()
	var inn: Node2D = town.world.get_node_or_null("Markers/Facilities/facility_inn")
	var inn_ok := false
	var trained := false
	var meditated := false
	if inn != null:
		# 打坐要花钱，而新档是 0 文——诊断脚本直接给一笔（和直接摆 world_position 一个性质）
		state.inventory.money = maxi(int(state.inventory.money), 500)
		town.player.global_position = inn.global_position
		var inn_opened: Dictionary = town.interact()
		inn_ok = bool(inn_opened.get("ok", false)) and town.cultivate_panel != null
		# **真打坐一次**（不只是开面板）：这样才验到「打坐 → 战斗外增益进存档」（v12）
		var train_button: Button = town.cultivate_panel.find_child("TrainButton*", true, false)
		if train_button != null and not train_button.disabled:
			train_button.emit_signal("pressed")
			trained = true
			meditated = state.field_buffs.size() > 0
		town.close_cultivate()
	var saved: Dictionary = town.press_save()
	var loaded: Dictionary = store.load_slot(int(state.slot), db)
	var same_money := bool(loaded.get("ok", false)) and loaded["state"] != null \
		and int(loaded["state"].inventory.money) == int(state.inventory.money)
	# 战斗外增益（v12）：打坐余韵要跟着手动存档一起进档、读回来还在
	var field_saved: bool = bool(loaded.get("ok", false)) and loaded["state"] != null \
		and meditated and loaded["state"].field_buffs.size() == state.field_buffs.size()
	# 赌局位点：判定行只填了 region_id，位点却摆在小地图里——真按一次 E，确认它接上了。
	# 这条以前是**接不上**的（小地图收位点只认 scene_id），而这轮同时修了「赢钱当成背包物品」。
	var gamble_ok := false
	var gamble_text := ""
	var gamble: Node2D = town.world.get_node_or_null("Markers/Event_ev_gamble")
	if gamble != null:
		town.player.global_position = gamble.global_position
		var near := str(town.event_near_player())
		var rolled: Dictionary = town.resolve_event(near) if near == "ev_gamble" else {}
		gamble_text = str(rolled.get("text", ""))
		# 只钉两件真事：**位点接得上**（能按出文案）、**奖励没有落进背包**（钱的唯一口径是钱包）。
		# 原来还要求文案里有「掷骰」——那只在**软判定不足**时才出现，而队伍人数一变（1 → 4），
		# 判定值够高就直接成功、不再掷骰，这条断言于是跟"表里有几个角色"绑在一起了
		# （2026-10-03 接 4 人队探针时踩到，见框架说明决策 161）。
		gamble_ok = near == "ev_gamble" and not gamble_text.is_empty() \
			and int(state.inventory.count("item_money")) == 0
	# 城镇里的可见标识：4 家店 + 3 个设施 + 出口（场景里都是看不见的 Marker，全靠代码补名字）
	var place_labels: int = town.find_children("PlaceLabel_*", "Label", true, false).size()
	# 「余韵进档」印的是**读回来的存档里还有没有这条增益**（`field_saved`），不是内存里那个标记——
	# 第一版印错成 `meditated`，于是把 `to_dict` 里的 field_buffs 删掉时这行还显示 true（检查本身是红的，只有文案骗人）
	lines.append("清风驿：进店=%s 打坐面板=%s 打坐=%s（余韵进档=%s）手动存档=%s 读档一致=%s 可见标识=%d 赌局=%s（%s）" % [
		shop_ok, inn_ok, meditated, field_saved, saved.get("ok", false), same_money, place_labels, gamble_ok, gamble_text,
	])
	return shop_ok and inn_ok and trained and meditated and field_saved \
		and bool(saved.get("ok", false)) and same_money \
		and place_labels >= 8 and gamble_ok


## 驿站传送 → 进图 → 从目的地回图：回程落点 = **目的地地标**（不是驿站），且不会被立刻送回
##
## 副本巡游（接在第 ⑤ 段之后，人还在黑风寨里）：开箱 → 完成度计数 → 「可扫荡」提示。
## **注意**：这里是把玩家摆到箱子旁按 E（等于「走到了」），验的是**开箱→完成度→扫荡**这条接线；
## 火盆密室与门后暗格那两箱在真实玩法里还进不去（等三火盆位点），不是这条要管的事。
func _dungeon_round(state, lines: PackedStringArray) -> bool:
	var map = current_scene
	if map == null or not map.has_method("progress_text"):
		lines.append("副本巡游：当前不在小地图")
		return false
	var before := int(map.dungeon_service().progress("scene_heifengzhai")["percent"])
	var opened := 0
	for chest in map.chests:
		if bool(chest.opened_already):
			continue
		map.player.global_position = chest.global_position
		if bool(map.interact().get("ok", false)):
			opened += 1
	var snapshot: Dictionary = map.dungeon_service().progress("scene_heifengzhai")
	var chests: Dictionary = snapshot["chests"]
	var chest_ok := int(chests["done"]) == int(chests["total"]) and int(chests["total"]) == 4
	var sweep_ok: bool = map.progress_text().contains("可扫荡")
	var rose := int(snapshot["percent"]) > before
	# 顺带带上「我在第几层哪间房」——副本是三层同一张图，HUD 该报这个
	var floor_text := str(map._floor_label())
	lines.append("副本巡游：所在=%s 开箱 %d 个 → 完成度 %d%%→%d%%（宝箱 %d/%d）可扫荡=%s" % [
		floor_text, opened, before, int(snapshot["percent"]),
		int(chests["done"]), int(chests["total"]), sweep_ok,
	])
	return opened >= 4 and chest_ok and sweep_ok and rose and not floor_text.is_empty()


## 驿站传送 → 进图 → 从目的地回图：回程落点 = **目的地地标**（不是驿站），且不会被立刻送回
func _waypoint_round(db, state, session, lines: PackedStringArray) -> bool:
	var world = current_scene
	if world == null or not world.has_method("open_waypoint"):
		lines.append("驿站传送：当前不在大地图")
		return false
	var post: Node2D = world.world.get_node_or_null("Markers/Node_n_post_station")
	if post == null:
		lines.append("驿站传送：地图上没有驿站位点")
		return false
	state.reveal_node("n_huangcun")   # 没揭开的地标不进传送列表
	world.player.global_position = post.global_position
	var opened: Dictionary = world.open_waypoint()
	if not bool(opened.get("ok", false)) or world.waypoint_panel == null:
		lines.append("驿站传送：面板没打开（%s）" % str(opened.get("error", "")))
		return false
	var panel = world.waypoint_panel
	panel.press_travel("n_huangcun")   # 走真实的 travel_handler → enter_local_map(..., true)
	await _frames(8)
	var local = current_scene
	var target_ok: bool = local != null and local.has_method("current_room_id") \
		and str(local.scene_id) == "scene_huangcun"
	var region: Resource = db.get_row("map_region", "n_huangcun")
	var expect := Vector2(float(region.pos_x), float(region.pos_y))
	var spot_ok: bool = session.world_position.distance_to(expect) <= 1.0
	lines.append("驿站传送（→荒村）：切过去了=%s 回程落点=目的地地标=%s" % [target_ok, spot_ok])
	if not (target_ok and spot_ok):
		return false
	# 从荒村出来：站在荒村门口（不是被拽回落雁坡的驿站），而且不会又被立刻送回
	local.leave_to_overworld()
	await _frames(8)
	var back = current_scene
	var back_ok: bool = back != null and back.name == "WorldRun"
	var dist := -1.0
	if back_ok:
		dist = back.player.global_position.distance_to(expect)
		back_ok = dist <= 1.0
	lines.append("驿站传送回程：回到大地图=%s 距荒村地标=%.1f" % [back_ok, dist])
	return back_ok


## 房间战斗 → 回小地图：回的是**战斗前的位置**（不是入口），那间房按胜负记成已清／未清
func _room_battle_round(state, lines: PackedStringArray) -> bool:
	var world = current_scene
	if world == null or not world.has_method("enter_local_map"):
		lines.append("房间战斗：当前不在大地图")
		return false
	world.enter_local_map("scene_heifengzhai")
	await _frames(6)
	var local = current_scene
	if local == null or not local.has_method("current_room_id") or local.teams.is_empty():
		lines.append("房间战斗：没进小地图或没有房间队伍")
		return false
	var team = local.teams[0]
	var room_id := str(team.spawn_id)
	var spot: Vector2 = team.global_position
	local.player.global_position = spot
	await _frames(6)
	var battle = current_scene
	if battle == null or not battle.has_method("press_auto"):
		lines.append("房间战斗：没进战斗场景")
		return false
	var result := _auto_battle(battle)
	battle.press_return()
	await _frames(8)
	var back = current_scene
	if back == null or not back.has_method("current_room_id"):
		lines.append("房间战斗：回程不是小地图（当前 %s）" % (back.name if back != null else "无"))
		return false
	var cleared: Array = Array(state.dungeon_record("scene_heifengzhai").get("rooms", []))
	var dist: float = back.player.global_position.distance_to(spot)
	var winner := str(result["winner"])
	lines.append("房间战斗（%s）：胜者=%s 回合=%d → 回图=%s 距战斗前位置=%.1f 房间已清=%s" % [
		room_id, winner, int(result["rounds"]), str(back.scene_id), dist, cleared.has(room_id),
	])
	if str(back.scene_id) != "scene_heifengzhai":
		return false
	if winner == "ally":
		# 赢了：房间记清，人接着站在打之前的位置（不是被弹回入口）
		return cleared.has(room_id) and dist <= 40.0
	# 没赢：房间不清，人被推离接触圈
	return (not cleared.has(room_id)) and dist > 22.0


## 酒葫芦 → 隐藏 Boss（醉刀客）→ 回小地图：真跨场景走一遍，顺带验回图落点是战斗前站的位置。
##
## 用例只验了这条链的两半（触发器建 encounter／结算记紫名 Boss），中间那段交接没人跑过。
func _wine_boss_round(db, state, session, lines: PackedStringArray) -> bool:
	if state == null:
		lines.append("隐藏Boss：没有会话状态")
		return false
	session.pending_local_scene = "scene_heifengzhai"
	session.pending_local_room = "hf1_deep"
	session.local_position_scene = ""
	state.inventory.add_item(db, "item_wine_gourd", 1)
	change_scene_to_file(LOCAL_RUN)
	await _frames(6)
	var map = current_scene
	if map == null or not map.has_method("current_room_id"):
		lines.append("隐藏Boss：进不了黑风寨")
		return false
	var trig = null
	for point in map.triggers:
		if str(point.trigger_id) == "trig_wine":
			trig = point
	if trig == null:
		lines.append("隐藏Boss：图里没有酒葫芦触发点")
		return false
	# 站到触发点上按 E（真交互）→ 控制器把 encounter 交给 GameSession 并切到战斗场景
	map.player.global_position = trig.global_position
	var spot_room := str(map.current_room_id())
	map.interact()
	await _frames(8)
	var battle = current_scene
	if battle == null or not battle.has_method("press_return") or battle.encounter == null:
		lines.append("隐藏Boss：按 E 没进战斗")
		return false
	var enc = battle.encounter
	var carried: bool = str(enc.source_scene) == "scene_heifengzhai" and str(enc.source_key) == "trig_wine"
	# 把敌人打死再走真结算（和用例同一手法：验的是结算与记录，不是打得赢打不赢）
	for enemy in battle.enemies:
		enemy.hp = 0
	battle._settle()
	var bosses: Array = Array(state.dungeon_record("scene_heifengzhai").get("bosses", []))
	var recorded: bool = bosses.has("en_hidden_drunk")
	battle.press_return()
	await _frames(8)
	var back = current_scene
	var back_ok: bool = back != null and back.has_method("current_room_id")
	var back_room := str(back.current_room_id()) if back_ok else "-"
	var kept: bool = back_ok \
		and Array(state.dungeon_record("scene_heifengzhai").get("bosses", [])).has("en_hidden_drunk")
	lines.append("隐藏Boss（酒葫芦→醉刀客）：交接=%s（source_scene=%s key=%s）记成Boss=%s 回图=%s 落点房间=%s（战前 %s）记录仍在=%s" % [
		carried, str(enc.source_scene), str(enc.source_key), recorded, back_ok, back_room, spot_room, kept,
	])
	return carried and recorded and back_ok and kept and back_room == spot_room


## ⑩ 招募链（设计 09 §3.2）：读告示板点亮条件 → **出图** → 再回来在**客栈**入队 → 存档往返仍 2 人。
##
## 这一段专门跑「跨场景」那一步：条件旗标是上一次进图点的，人是下一次进图收的
## （数据里 `recruit_def.join_scene=scene_qingfengyi`、条件 `flag_board_read`）。
func _recruit_round(db, session, lines: PackedStringArray) -> bool:
	var guide = load("res://src/core/guide_service.gd")
	var state = session.state
	if state == null:
		lines.append("招募链：没有会话状态")
		return false
	var before_party: int = state.party_size()
	var state_before_board: bool = state.has_flag("flag_board_read")

	# 第一次进清风驿：读悬赏板（引导第一步的目标、也是招募的条件）
	session.pending_local_scene = "scene_qingfengyi"
	session.pending_local_room = ""
	session.local_position_scene = ""
	change_scene_to_file(LOCAL_RUN)
	await _frames(6)
	var town = current_scene
	if town == null or not town.has_method("interact"):
		lines.append("招募链：进不了清风驿")
		return false
	var board: Node2D = town.world.get_node_or_null("Markers/Facilities/facility_bounty_board")
	var board_ok := false
	var guide_text := ""
	## 序幕·择念（20 §3.1）：同一按摆出来、**真选一条**（面板走旁白模式、心性真的进档）。
	## 这里走的是**真场景 + 真按键**：`test_local_map` 那一条是同一个控制器上的直调，
	## 而这一段验的是"跨场景之后它还认不认账"（第二次进图不该再问）。
	var opening_ok := false
	var opening_once := false
	if board != null:
		town.player.global_position = board.global_position
		var posted: Dictionary = town.interact()
		board_ok = bool(posted.get("ok", false)) and state.has_flag("flag_board_read")
		guide_text = guide.hud_text(db, state)
		opening_ok = str(posted.get("story", "")) == "dl_opening_choice" \
			and town.npc_panel != null and str(town.npc_panel.mode) == "story"
		if opening_ok:
			var picked: Dictionary = town.npc_panel.choose_dialogue("opt_open_li")
			opening_ok = bool(picked.get("ok", false)) and state.has_flag("heart_li")
			town.close_npc()

	# 出图（回大地图），再进来——「条件在上一次进图点亮」这件事只有跨场景才验得到
	town.leave_to_overworld()
	await _frames(4)
	var back_to_world: bool = current_scene != null and current_scene.has_method("enter_local_map")
	session.pending_local_scene = "scene_qingfengyi"
	change_scene_to_file(LOCAL_RUN)
	await _frames(6)
	var town2 = current_scene
	var joined := false
	var refused_elsewhere := false
	if town2 != null and town2.has_method("interact"):
		# 先回悬赏板再按一次：心性已经定过（选过一条），这句**不该再弹**
		var board2: Node2D = town2.world.get_node_or_null("Markers/Facilities/facility_bounty_board")
		if board2 != null:
			town2.player.global_position = board2.global_position
			var posted_again: Dictionary = town2.interact()
			opening_once = not posted_again.has("story")
			town2.close_npc()
		# 先站在**当铺**按一次 E：城镇只认客栈，这一步不该收人（09 §3.2 的口径）
		var pawn: Node2D = town2.world.get_node_or_null("Markers/Facilities/facility_pawnshop")
		if pawn != null:
			town2.player.global_position = pawn.global_position
			town2.interact()
			refused_elsewhere = state.party_size() == before_party
		var inn: Node2D = town2.world.get_node_or_null("Markers/Facilities/facility_inn")
		if inn != null:
			town2.player.global_position = inn.global_position
			var at_inn: Dictionary = town2.interact()
			joined = bool(at_inn.get("ok", false)) and Array(at_inn.get("recruited", [])).size() >= 1

	# 存档往返：入队结果（队伍 + 旗标）要跟着存档走
	var store = load("res://src/core/save_store.gd").new("res://.logs/loopcheck/saves", 3)
	var saved: Dictionary = store.save_slot(2, state)
	var loaded: Dictionary = store.load_slot(2, db)
	var persisted: bool = bool(loaded.get("ok", false)) and loaded["state"] != null \
		and loaded["state"].party_size() == state.party_size() \
		and loaded["state"].has_flag("flag_ch_ci_joined")
	lines.append("招募链：读告示板=%s 引导「%s」｜序幕择念=%s（再进图不再问=%s）｜出图回大地图=%s｜当铺不收人=%s｜客栈入队=%s（%d→%d 人）｜存档往返=%s" % [
		board_ok and not state_before_board, guide_text, opening_ok, opening_once, back_to_world,
		refused_elsewhere, joined, before_party, state.party_size(), persisted,
	])
	# 分母**不写死**：引导步数会随设计扩表（0.22.0 由 4 步扩到 6 步）。
	# 写死 2/4 的那版在扩表当天就假红——这里改成按表算，分母错了照样抓得住。
	var guide_second := "2/%d" % guide.steps(db).size()
	return board_ok and not state_before_board and guide_text.contains(guide_second) \
		and opening_ok and opening_once and back_to_world and refused_elsewhere and joined and persisted


## ⑫ 幕五对质 → 账册 → 终局三选一：**真打一场大寨主**、真回图、真选一条。
##
## 交接面：战斗结算把这一场的队伍 id 写进会话 → 回到小地图时 `setup()` 按 `TEAM_WIN_DIALOGUES`
## 翻出 `dl_ledger_choice` 摆成面板。所以这一段要看四件事：
##   ① 首杀打赢大寨主 → **账册真的进背包**（`TEAM_WIN_ITEMS`）；
##   ② 回图时面板**自动摆出来**、说的就是那条（条件：对质打过 ＋ 账册在手 ＋ 一条都没选过）；
##   ③ 选一条 → 旗标落地（这里选「呈官」），三样永久增益由此拿得到；
##   ④ 存档往返仍认账（这条决定第二章开场关系，不能只活在本局里）。
func _ledger_round(db, session, lines: PackedStringArray) -> bool:
	var state = session.state
	if state == null:
		lines.append("终局三选一：没有会话状态")
		return false
	session.pending_local_scene = "scene_heifengzhai"
	session.pending_local_room = "hf3_boss"
	session.local_position_scene = ""
	change_scene_to_file(LOCAL_RUN)
	await _frames(6)
	var map = current_scene
	if map == null or not map.has_method("current_room_id"):
		lines.append("终局三选一：进不了黑风寨")
		return false
	# 大寨主那支队伍（房间敌人是接触开战，与明雷同一套）
	var boss = null
	for candidate in map.teams:
		if str(candidate.spawn_id) == "team_boss" or str(candidate.team_row.team_id) == "team_boss":
			boss = candidate
			break
	if boss == null:
		lines.append("终局三选一：图里没有大寨主那支队伍")
		return false
	map.player.global_position = boss.global_position
	await _frames(8)
	var battle = current_scene
	if battle == null or not battle.has_method("press_return") or battle.encounter == null:
		lines.append("终局三选一：接触没进战斗")
		return false
	var team_id := str(battle.encounter.team_id)
	# 打死再走真结算（验的是结算与交接，不是打得赢打不赢）
	for enemy in battle.enemies:
		enemy.hp = 0
	battle._settle()
	var got_ledger: bool = state.inventory.has("item_bd_ledger")
	battle.press_return()
	await _frames(8)
	var back = current_scene
	var back_ok: bool = back != null and back.has_method("current_room_id")
	var panel = back.npc_panel if back_ok else null
	var panel_ok: bool = panel != null and str(panel.dialogue_node_id) == "dl_ledger_choice"
	var flag_before: bool = state.has_flag("flag_ledger_public")
	var chosen_ok := false
	if panel_ok:
		var picked: Dictionary = panel.choose_dialogue("opt_ledger_public")
		chosen_ok = bool(picked.get("ok", false)) and state.has_flag("flag_ledger_public")
		back.close_npc()
	# 存档往返：这条旗标决定第二章开场，必须跟着存档走
	var store = load("res://src/core/save_store.gd").new("res://.logs/loopcheck/saves", 3)
	store.save_slot(2, state)
	var loaded: Dictionary = store.load_slot(2, db)
	var persisted: bool = bool(loaded.get("ok", false)) and loaded["state"] != null \
		and loaded["state"].has_flag("flag_ledger_public") \
		and loaded["state"].inventory.has("item_bd_ledger")
	lines.append("终局三选一（打赢大寨主→账册→三选一）：队伍=%s 拿到账册=%s 回图=%s 摆出面板=%s 选「呈官」=%s（选前=%s）存档往返=%s" % [
		team_id, got_ledger, back_ok, panel_ok, chosen_ok, flag_before, persisted,
	])
	return team_id == "team_boss" and got_ledger and back_ok and panel_ok \
		and chosen_ok and not flag_before and persisted


## ⑪ 宝箱守卫（设计 09 §一）：荒村 `hc_02` 的银箱被屠夫守着——
## 第一次按 E 只是让他开口、第二次付账（给一份草药汤）、第三次才开箱；
## 存档往返之后「文取已解决」要跟着走（下次进图屠夫不再挡）。
func _guard_round(db, session, lines: PackedStringArray) -> bool:
	var guard = load("res://src/core/guard_service.gd")
	var state = session.state
	if state == null:
		lines.append("宝箱守卫：没有会话状态")
		return false
	var guard_id := "guard_hc_02"
	var peace_already: bool = state.has_flag(guard.peace_flag(guard_id))
	state.inventory.add_item(db, "item_med_01", 1)
	session.pending_local_scene = "scene_huangcun"
	session.pending_local_room = ""
	session.local_position_scene = ""
	change_scene_to_file(LOCAL_RUN)
	await _frames(6)
	var map = current_scene
	if map == null or not map.has_method("interact"):
		lines.append("宝箱守卫：进不了荒村")
		return false
	var chest = null
	for candidate in map.chests:
		if str(candidate.get_meta("key", "")) == "hc_02|drop_chest_silver":
			chest = candidate
	if chest == null:
		lines.append("宝箱守卫：荒村里没找到 hc_02 的银箱")
		return false
	map.player.global_position = chest.global_position
	# 进图时它开没开（**必须在按 E 之前取**：下面第三次按 E 会把它标成已开）
	var was_open: bool = chest.opened_already
	var demand: Dictionary = map.interact()
	var before_pay: int = state.inventory.count("item_med_01")
	var paid: Dictionary = map.interact()
	var after_pay: int = state.inventory.count("item_med_01")
	var opened: Dictionary = map.interact()
	var demand_ok: bool = str(demand.get("stage", "")) == "demand" and not was_open
	# 0.14.0：文取拆成三列之后，书生在**判定路**（医术 5 ≥ 门槛 3）上直接达标 →
	# 控制器优先走「不花东西」的那条，所以草药汤**不该被扣**（旧断言写的是 item 路，已过期）。
	var paid_ok: bool = str(paid.get("stage", "")) == "peace" and after_pay == before_pay
	var flag_ok: bool = state.has_flag(guard.peace_flag(guard_id))
	var opened_ok: bool = bool(opened.get("ok", false)) and chest.opened_already
	# 存档往返：文取的旗标跟着走（不然下次进图屠夫又挡回去）
	var store = load("res://src/core/save_store.gd").new("res://.logs/loopcheck/saves", 3)
	store.save_slot(2, state)
	var loaded: Dictionary = store.load_slot(2, db)
	var persisted: bool = bool(loaded.get("ok", false)) and loaded["state"] != null \
		and loaded["state"].has_flag(guard.peace_flag(guard_id))
	lines.append("宝箱守卫：开口=%s 付账=%s（草药汤 %d→%d）旗标=%s 开箱=%s 旗标进档=%s（进图前已解决=%s 进图时已开=%s）" % [
		demand_ok, paid_ok, before_pay, after_pay, flag_ok, opened_ok, persisted, peace_already, was_open,
	])
	return demand_ok and paid_ok and flag_ok and opened_ok and persisted


## 存档 → 读档：真文件走一遍，比对一份指纹（气血／经验／难度／揭雾／副本记录／旗标）
func _save_round(state, db, lines: PackedStringArray) -> bool:
	var store = load("res://src/core/save_store.gd").new("res://.logs/loopcheck", 3)
	store.ensure_dir()
	state.slot = 1
	var char_id := str(state.char_ids[0])
	state.set_current_hp(char_id, 77)
	state.reveal_node("n_huangcun")
	state.record_dungeon("scene_heifengzhai", "chests", "hf1_shed|drop_chest_copper")
	state.set_flag("loopcheck_flag")
	state.party_exp = 123
	var before := _fingerprint(state)
	var saved: Dictionary = store.save_slot(1, state)
	var loaded: Dictionary = store.load_slot(1, db)
	var after := _fingerprint(loaded["state"]) if bool(loaded["ok"]) else "读档失败"
	var same := before == after
	lines.append("存档→读档：写=%s 读=%s 指纹一致=%s（%s）" % [
		saved.get("ok", false), loaded.get("ok", false), same, before,
	])
	return bool(saved["ok"]) and bool(loaded["ok"]) and same


## 存档指纹：只放「该被存下来的东西」（时间戳、槽位这些会变的项不进）
func _fingerprint(state) -> String:
	var rec: Dictionary = state.dungeon_record("scene_heifengzhai")
	return "hp=%d exp=%d diff=%s node=%s chests=%d flag=%s" % [
		state.current_hp_of(str(state.char_ids[0])), state.party_exp, state.difficulty_id,
		state.is_node_revealed("n_huangcun"), Array(rec.get("chests", [])).size(),
		state.has_flag("loopcheck_flag"),
	]


## 自动战斗打到结束（按 AUTO_INTERVAL 的节奏 tick，返回胜者／回合／次数）
func _auto_battle(battle) -> Dictionary:
	battle.press_auto()
	var ticks := 0
	while not battle.sim.finished() and ticks < 400:
		battle._process(0.8)
		ticks += 1
	return {"winner": str(battle.sim.winner()), "rounds": int(battle.sim.rounds_played()), "ticks": ticks}


func _keep_only(db, session, keep_id: String) -> void:
	session.cleared_spawns.clear()
	for row: Resource in db.rows("roaming_spawn"):
		if str(row.spawn_id) != keep_id:
			session.cleared_spawns[str(row.spawn_id)] = -1


func _frames(count: int) -> void:
	for i in range(count):
		await process_frame
