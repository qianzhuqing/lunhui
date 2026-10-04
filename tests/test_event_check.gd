## 事件判定（`event_check.csv` + 非战斗技能）。
##
## 判定值来自 CharacterSheet：`attr:x` 取裸属性、`skill:y` = 技能等级 + floor(裸属性/5)；
## `hard` 达标必过、不足做不到；`soft` 达标必过、**不足掷骰**（01 的公式 `0.5 + 差值 × 0.1`，
## 夹 5%~95%），软判定失败不惩罚。奖励：道具 / 装备 / 指向某地 / 剧情旗标。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const EventCheckServiceScript := preload("res://src/core/event_check_service.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const TableValidatorScript := preload("res://src/core/table_validator.gd")

const CHAR_ID := "scholar_fallen"
## 软判定要掷骰，用例靠固定种子复现
const SEED := 20261003


func suite_name() -> String:
	return "事件判定"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	var service = EventCheckServiceScript.new(db, state, RngServiceScript.new(SEED))
	_check_table(db, service)
	_check_judgement(db, state, service)
	_check_soft_roll(db)
	_check_rewards(db, state, service)
	_check_once_only(db, state, service)
	_check_save(db, state)
	_check_reward_types(db)
	_check_gamble_money(db)
	_check_marker_ownership(db)


## 14 条判定的来源都要能解析，难度都要是正数（0.28.0 加了 `ev_patrol_check`，
## 0.29.0 又加了 `ev_shen_rescue`——地牢里救沈雁回，打通 Q61 的那枚旗标）
func _check_table(db, service) -> void:
	var rows: Array = []
	for row: Resource in db.rows("event_check"):
		rows.append(row)
	check_eq(rows.size(), 14, "表里有 14 条事件判定（0.29.0 加了 `ev_shen_rescue`）")
	for row: Resource in rows:
		var check_id := str(row.check_id)
		var source := str(row.check_source)
		check_true(
			source.begins_with("attr:") or source.begins_with("skill:"),
			"%s 的判定来源格式对：%s" % [check_id, source],
		)
		check_gt(float(row.difficulty), 0.0, "%s 的难度是正数" % check_id)
		var best: Dictionary = service.best_check_value(source)
		check_gt(float(best["value"]), -900.0, "%s 的判定值算得出来（%s）" % [check_id, source])
	check_eq(service.checks_for_scene("scene_heifengzhai").size(), 5, "黑风寨 5 条（0.29.0 加了地牢救人）")
	check_eq(service.checks_for_region("n_luoyanpo").size(), 3, "落雁坡 3 条")


## 位点归属：赌局摆在小地图里，判定行却只有 region_id
##
## 07 文档把赌局摆在清风驿·客栈，但 `ev_gamble` 的 `scene_id` 是空的、只写了 `region_id=n_qingfengyi`。
## 小地图收位点以前只认 `scene_id`，于是镇上那个 `Event_ev_gamble` **永远没人接**；
## 地图验收那边还专门写了 `check_id != "ev_gamble"` 的例外把它从大地图里排除掉——
## 两边都知道它特殊，却没人把它接上。现在判据是「scene_id 命中 **或** region_id 是这张图的父节点」，
## 再**与**「这张图上真有 `Event_<check_id>` 位点」取交集（见 `local_map_controller._collect_events`）。
func _check_marker_ownership(db) -> void:
	var service = EventCheckServiceScript.new(db, solo_state(db), RngServiceScript.new(SEED))
	check_eq(service.parent_node_of("scene_qingfengyi"), "n_qingfengyi", "清风驿挂在 n_qingfengyi 下")
	check_true(service.check_allowed_in_scene("ev_gamble", "scene_qingfengyi"), "赌局允许出现在清风驿小地图里")
	check_false(service.check_allowed_in_scene("ev_gamble", "scene_heifengzhai"), "但不允许出现在黑风寨")
	check_true(service.check_allowed_in_scene("ev_shed_trap", "scene_heifengzhai"), "柴房机关（scene_id 命中）允许")
	check_true(
		service.check_allowed_in_scene("ev_climb_wall", "scene_heifengzhai"),
		"翻寨墙的 region 与黑风寨相符，所以「允许」——**能不能接上还要看图上有位点**",
	)
	check_false(service.check_allowed_in_scene("没这条判定", "scene_qingfengyi"), "查不到的判定返回 false")


## 赌局：赢的是**钱**，不是背包里一行「铜钱」。
##
## 以前 `_grant` 的 item 分支直接 `add_item`，于是 `item_money` 被当成可堆叠物品进了背包，
## `inventory.money` 一文不涨——玩家赢了赌局却买不了任何东西。
func _check_gamble_money(db) -> void:
	var won := false
	var lost := false
	for candidate in range(1, 200):
		var state = solo_state(db)
		var before: int = int(state.inventory.money)
		var service = EventCheckServiceScript.new(db, state, RngServiceScript.new(candidate))
		var result: Dictionary = service.resolve("ev_gamble")
		if bool(result["success"]) and not won:
			won = true
			check_eq(str(result["reward"]["type"]), "money", "赌局赢的是钱（种子 %d）" % candidate)
			check_gt(
				float(state.inventory.money), float(before),
				"铜钱真的涨了：%d → %d" % [before, int(state.inventory.money)],
			)
			check_eq(state.inventory.count("item_money"), 0, "背包里不该多出一行「铜钱」")
			check_true(str(result["text"]).contains("文钱"), "文案说清拿到多少钱：%s" % result["text"])
		elif not bool(result["success"]) and not lost:
			lost = true
			check_eq(int(state.inventory.money), before, "赌输不掉钱（软判定失败不惩罚）")
			check_eq(state.inventory.count("item_money"), 0, "输了也不会给钱")
		if won and lost:
			break
	check_true(won, "在 200 颗种子里能找到赌赢那次")
	check_true(lost, "也有赌输的时候")


## 1 级书生的判定值：奇门 3、文学 5、医术 3、毒术 2、生存 1；敏 9、力 5
func _check_judgement(db, state, service) -> void:
	var sheet = CharacterSheetScript.new(db, state, CHAR_ID)
	check_eq(int(sheet.event_check_value("skill:qimen")["value"]), 3, "奇门判定值 3（1 + 10/5）")
	check_true(bool(sheet.event_check_value("skill:qimen")["naked"]), "判定值用裸属性（不含装备）")
	check_eq(int(sheet.event_check_value("skill:wenxue")["value"]), 5, "文学判定值 5（3 + 2）")
	check_eq(int(sheet.event_check_value("skill:dusu")["value"]), 2, "毒术判定值 2（0 + 2）")
	check_eq(int(sheet.event_check_value("attr:agi")["value"]), 9, "敏 9 直接当判定值")

	# 恰好达标与差一点
	var pass_case: Dictionary = service.judge("ev_shed_trap")     # skill:qimen 3
	check_true(bool(pass_case["ok"]) and bool(pass_case["success"]), "奇门 3 ≥ 3：识破柴房机关")
	check_eq(int(pass_case["difficulty"]), 3, "难度取自表")
	check_eq(int(pass_case["value"]), 3, "判定值取自面板同一套")
	var fail_case: Dictionary = service.judge("ev_force_gate")    # attr:str 5 vs 8
	check_true(bool(fail_case["ok"]) and not bool(fail_case["success"]), "力 5 < 8：推不开木闩")
	var wall: Dictionary = service.judge("ev_climb_wall")         # attr:agi 9 vs 9
	check_true(bool(wall["success"]), "敏 9 ≥ 9：翻得过寨墙（临界值算过）")
	var dusk: Dictionary = service.judge("ev_night_watch")        # attr:con 5 vs 7
	check_false(bool(dusk["success"]), "体 5 < 7：确定性结果是不足（soft 才去掷骰）")


## 软判定：不足可掷骰（01 的公式）、失败不惩罚；硬判定不足则确定性地做不到。
func _check_soft_roll(db) -> void:
	var service = EventCheckServiceScript.new(db, solo_state(db), RngServiceScript.new(SEED))

	# 公式与上下限
	check_eq(service.soft_chance(5, 7), 0.3, "差值 -2 → 0.5 - 0.2 = 30%")
	check_eq(service.soft_chance(6, 7), 0.4, "差值 -1 → 40%")
	check_eq(service.soft_chance(7, 7), 0.5, "差值 0 → 50%（达标时直接过，这里只验公式）")
	check_eq(service.soft_chance(0, 10), 0.05, "差得太多夹到 5% 下限")
	check_eq(service.soft_chance(20, 10), 0.95, "高太多夹到 95% 上限")

	# 硬判定不足：不掷骰、没有概率
	var gate: Dictionary = service.judge("ev_force_gate")   # hard，力 5 < 8
	check_false(bool(gate["success"]), "硬判定不足：失败")
	check_false(bool(gate["roll_needed"]), "硬判定不足不掷骰")
	check_eq(float(gate["chance"]), 0.0, "硬判定不足没有概率")

	# 软判定不足：给出概率，judge 本身不掷骰（可以重复调）
	var dusk: Dictionary = service.judge("ev_night_watch")  # soft，体 5 < 7
	check_false(bool(dusk["success"]), "软判定不足：确定性结果仍是不足")
	check_true(bool(dusk["roll_needed"]), "软判定不足要掷骰")
	check_eq(float(dusk["chance"]), 0.3, "守夜成功率 30%")
	check_true(str(dusk["text"]).contains("掷骰 30%"), "文案写出概率：%s" % dusk["text"])
	var dusk_again: Dictionary = service.judge("ev_night_watch")
	check_eq(float(dusk_again["chance"]), float(dusk["chance"]), "judge 不掷骰，重复调用结果一致")

	# 掷骰真的会翻面：同一颗种子可复现，换种子两边都出现（否则就是「永远失败」）
	var outcomes := {"win": 0, "lose": 0}
	for candidate in range(1, 60):
		var one = EventCheckServiceScript.new(
			db, solo_state(db), RngServiceScript.new(candidate)
		).resolve("ev_night_watch")
		check_true(bool(one.get("rolled", false)), "软判定不足真的掷了骰（种子 %d）" % candidate)
		check_true(str(one["text"]).contains("掷骰 →"), "文案写出掷骰结果：%s" % one["text"])
		if bool(one["success"]):
			outcomes["win"] += 1
		else:
			outcomes["lose"] += 1
	check_gt(float(outcomes["win"]), 0.0, "换种子有掷中的时候（不是永远失败）")
	check_gt(float(outcomes["lose"]), 0.0, "也有掷不中的时候（不是永远成功）")
	# 同一颗种子跑两次必须一模一样
	var repeat_a = EventCheckServiceScript.new(
		db, solo_state(db), RngServiceScript.new(SEED)
	).resolve("ev_night_watch")
	var repeat_b = EventCheckServiceScript.new(
		db, solo_state(db), RngServiceScript.new(SEED)
	).resolve("ev_night_watch")
	check_eq(bool(repeat_a["success"]), bool(repeat_b["success"]), "同种子同结果（可复现）")

	# 掷中要发奖励、掷不中不发且不惩罚（拿 ev_cell_heal 当样本：soft，医术 3 < 4 = 40%）
	var won := false
	var lost := false
	for candidate in range(1, 200):
		var probe_state = solo_state(db)
		var probe = EventCheckServiceScript.new(db, probe_state, RngServiceScript.new(candidate))
		var result: Dictionary = probe.resolve("ev_cell_heal")
		if bool(result["success"]) and not won:
			won = true
			check_eq(probe_state.inventory.count("item_potion_small"), 1, "掷中：奖励进背包（种子 %d）" % candidate)
			check_eq(str(probe_state.event_check_result("ev_cell_heal")), "done", "掷中记 done")
			# 救出阿福 → `flag_qiutu_saved`（钱大夫的委托等的就是它）。
			# 以前这枚旗标**全项目没有任何来源**，那条委托永远交不了（20 号 §九 #6）。
			check_true(probe_state.has_flag("flag_qiutu_saved"), "掷中＝救出来了，记下 flag_qiutu_saved")
		elif not bool(result["success"]) and not lost:
			lost = true
			check_eq(probe_state.inventory.count("item_potion_small"), 0, "掷不中：不发东西（失败不惩罚）")
			check_eq(str(probe_state.event_check_result("ev_cell_heal")), "failed", "掷不中记 failed")
			check_true(str(result["text"]).contains("拿不到额外药品"), "失败文案用 fail_note：%s" % result["text"])
			check_false(probe_state.has_flag("flag_qiutu_saved"), "没救成就不记账")
		if won and lost:
			break
	check_true(won, "软判定掷中过")
	check_true(lost, "软判定也掷不中过")


## 奖励三类：道具进包、指向某地（顺带揭开地标）、剧情旗标
func _check_rewards(db, state, service) -> void:
	# 道具：救治采药人（医术 3 ≥ 2）
	var heal: Dictionary = service.resolve("ev_herbalist_help")
	check_true(bool(heal["success"]), "医术判定成功：%s" % heal["text"])
	check_eq(state.inventory.count("item_potion_small"), 1, "奖励的金创药进了背包")
	check_eq(str(state.event_check_result("ev_herbalist_help")), "done", "结果写进存档")

	# 剧情旗标：劝退强盗（文学 5 ≥ 4）
	var parley: Dictionary = service.resolve("ev_bandit_parley")
	check_true(bool(parley["success"]), "劝退成功：%s" % parley["text"])
	check_true(state.has_flag("event_parley"), "记下 event_parley 旗标")

	# 碑文（文学 5 ≥ 3）给锈剑
	var epitaph: Dictionary = service.resolve("ev_grave_epitaph")
	check_true(bool(epitaph["success"]), "读碑成功：%s" % epitaph["text"])
	check_eq(state.inventory.count("item_rusty_sword"), 1, "拿到锈剑")

	# 失败：转述代价、不写 done
	var gate: Dictionary = service.resolve("ev_force_gate")
	check_false(bool(gate["success"]), "推门失败")
	check_true(str(gate["text"]).contains("推不开粗木闩"), "失败文案用表里的 fail_note：%s" % gate["text"])
	check_eq(str(state.event_check_result("ev_force_gate")), "failed", "失败也记下来（但不是 done）")

	# 「指向某地」类：这条现在判定不达标（生存 1 < 3），走白盒验一次奖励落点
	var row: Resource = db.get_row("event_check", "ev_wild_track")
	check_not_null(row, "有追足迹这条")
	if row != null:
		var granted: Dictionary = service._grant(row)
		check_eq(str(granted["type"]), "room", "奖励类型是「去某个房间」")
		check_eq(str(granted.get("landmark", "")), "n_cave_collapse", "顺带把塌陷山洞标到地图上")
		check_true(state.is_node_revealed("n_cave_collapse"), "地标真的揭开了")
		check_true(str(granted["text"]).contains("塌方深处"), "文案写清去哪：%s" % granted["text"])


## once_only：成功过就不能再做；失败还能再试
func _check_once_only(db, state, service) -> void:
	state.record_event_check("ev_wild_track", "done")
	var again: Dictionary = service.resolve("ev_wild_track")
	check_false(bool(again["ok"]), "once_only 的点位做过就不能再做")
	check_true(bool(again.get("already", false)), "结果里标着「已经做过」")
	check_true(str(again["text"]).contains("已经做过"), "文案说明原因：%s" % again["text"])

	# 失败过一次的还能重试（失败不算 once_only 的「做过」）
	state.record_event_check("ev_force_gate", "failed")
	var retry: Dictionary = service.judge("ev_force_gate")
	check_true(bool(retry["ok"]), "失败过的还能再判一次")


## 判定结果与旗标都进存档
func _check_save(db, state) -> void:
	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_not_null(back, "带事件记录的存档能读回来")
	if back == null:
		return
	check_eq(back.migrated_from, 0, "同版本往返不需要迁移")
	check_eq(back.event_check_result("ev_herbalist_help"), "done", "判定结果往返一致")
	check_true(back.has_flag("event_parley"), "剧情旗标往返一致")
	check_eq(back.inventory.count("item_rusty_sword"), 1, "奖励道具往返一致")

	# v8 老档没有这两项，读进来是空的
	var legacy: Dictionary = state.to_dict()
	legacy["version"] = 8
	legacy.erase("event_checks")
	legacy.erase("flags")
	var migrated = GameStateScript.from_dict(legacy, db)
	check_eq(migrated.migrated_from, 8, "记下从 v8 迁移")
	check_eq(migrated.version, GameStateScript.VERSION, "版本升到当前")
	check_eq(migrated.event_checks.size(), 0, "老档没有事件记录")


## 奖励类型：`equip` 能真发；设计 06 允许但这里做不了的 `skillbook`/`boss` 必须**显式报错**。
##
## 以前两边都没有声音：`_grant()` 对这三类落进「没有奖励」的兜底，而 `TableValidator` 只查
## `item` 与 `room` 的 id——策划写一条 `reward_type=equip` 会静默什么都不发。
func _check_reward_types(db) -> void:
	# ① 校验器补齐 id 检查：equip / skillbook / boss 写错 id 要被抓到
	var broken = TableDbScript.new()
	broken.load_all()
	var copy: Resource = broken.tables["event_check"].duplicate(true)
	for row: Resource in copy.rows:
		if str(row.check_id) == "ev_herbalist_help":
			row.reward_type = "equip"
			row.reward_id = "eq_does_not_exist"
	broken.tables["event_check"] = copy
	var errors: PackedStringArray = TableValidatorScript.validate(broken)
	check_gt(float(errors.size()), 0.0, "equip 奖励写错 id 要被校验抓到：%s" % str(errors))
	var named := false
	for message: String in errors:
		if message.contains("ev_herbalist_help"):
			named = true
	check_true(named, "报错点出是哪条判定：%s" % str(errors))

	# ② 运行时：equip 真发（建实例进背包）
	var probe_state = solo_state(db)
	var before: int = probe_state.inventory.equipment.size()
	var service = EventCheckServiceScript.new(db, probe_state, RngServiceScript.new(SEED))
	var equip_row: Resource = db.get_row("event_check", "ev_herbalist_help").duplicate(true)
	equip_row.reward_type = "equip"
	equip_row.reward_id = "eq_sword_01"
	var granted: Dictionary = service._grant(equip_row)
	check_eq(str(granted["type"]), "equip", "equip 奖励不再落进兜底")
	check_true(
		str(granted.get("instance_id", "")).begins_with("eq_sword_01"),
		"建出了装备实例：%s" % str(granted.get("instance_id", "")),
	)
	check_eq(probe_state.inventory.equipment.size(), before + 1, "装备实例进了背包")
	check_true(str(granted["text"]).contains("铁剑"), "文案用中文名：%s" % granted["text"])

	# ③ 设计 06 允许的 7 种奖励**全部有落点**（2026-10-03 补齐最后两种）：
	#    · skillbook —— 与 `item` 同一条路（给一本书，进背包后走研读流程）
	#    · boss —— 返回「开战请求」（纯逻辑服务不碰场景，由 `resolve_event` 发起战斗）
	#    写错 id 仍然显式报错，不静默。
	var skillbook_stub = TableDbScript.new()
	skillbook_stub.load_all()
	var sb_copy: Resource = skillbook_stub.tables["event_check"].duplicate(true)
	for row: Resource in sb_copy.rows:
		if str(row.check_id) == "ev_herbalist_help":
			row.reward_type = "skillbook"
			row.reward_id = "item_potion_small"
	skillbook_stub.tables["event_check"] = sb_copy
	var sb_state = solo_state(skillbook_stub)
	var sb_service = EventCheckServiceScript.new(skillbook_stub, sb_state, RngServiceScript.new(SEED))
	var sb_res: Dictionary = sb_service.resolve("ev_herbalist_help")
	check_true(bool(sb_res["success"]), "skillbook：判定本身成功")
	check_eq(str(sb_res["reward"]["type"]), "skillbook", "skillbook 不再落进兜底")
	check_eq(sb_state.inventory.count("item_potion_small"), 1, "秘籍进了背包（与 item 同一条路）")
	check_true(str(sb_res["text"]).contains("得到"), "文案写明拿到了什么：%s" % sb_res["text"])

	# boss：指向存在的敌人 → 交出开战请求（含敌人中文名）；指向不存在的敌人 → 显式报错、不发请求
	for spec: Array in [["en_bd_boss", true], ["en_does_not_exist", false]]:
		var boss_stub = TableDbScript.new()
		boss_stub.load_all()
		var boss_copy: Resource = boss_stub.tables["event_check"].duplicate(true)
		for row: Resource in boss_copy.rows:
			if str(row.check_id) == "ev_herbalist_help":
				row.reward_type = "boss"
				row.reward_id = str(spec[0])
		boss_stub.tables["event_check"] = boss_copy
		var boss_state = solo_state(boss_stub)
		var boss_service = EventCheckServiceScript.new(boss_stub, boss_state, RngServiceScript.new(SEED))
		var boss_res: Dictionary = boss_service.resolve("ev_herbalist_help")
		var reward: Dictionary = boss_res["reward"]
		check_eq(str(reward["type"]), "boss", "boss 奖励类型认出来了（%s）" % str(spec[0]))
		if bool(spec[1]):
			check_true(bool(reward.get("start_battle", false)), "交出开战请求")
			check_true(str(reward.get("enemy_name", "")).contains("大寨主"), "带上敌人中文名：%s" % str(reward.get("enemy_name", "")))
			check_true(str(boss_res["text"]).contains("现身"), "文案写「现身」：%s" % boss_res["text"])
		else:
			check_false(bool(reward.get("start_battle", false)), "敌人在表里不存在时不发开战请求")
			# **规则改了**（2026-10-03，决策 242）：id 只进日志（push_error），玩家看的是人话——
			# 原来这条断言替"把 en_does_not_exist 甩给玩家"站台
			check_false(
				str(reward.get("error", "")).contains("en_does_not_exist"),
				"错误文案不给玩家看表内 id（只进日志）：%s" % str(reward.get("error", "")),
			)
			check_true(str(reward.get("error", "")).contains("不存在"), "错误文案说人话：%s" % str(reward.get("error", "")))

	# ④ `none`（判定本身就是收益）不该再往状态栏里塞开发腔的说明
	var none_state = solo_state(db)
	var none_service = EventCheckServiceScript.new(db, none_state, RngServiceScript.new(SEED))
	var trap: Dictionary = none_service.resolve("ev_shed_trap")   # hard，奇门 3 ≥ 3，reward_type=none
	check_true(bool(trap["success"]), "识破柴房机关成功")
	check_false(str(trap["text"]).contains("场景自己处理"), "none 奖励不给玩家看开发说明：%s" % trap["text"])
	check_false(str(trap["text"]).ends_with("　"), "文案末尾不留空段：%s" % trap["text"])

	# ⑤ 非法 reward_type 也不能静默（校验器会报，运行时也要报）
	var bad_stub = TableDbScript.new()
	bad_stub.load_all()
	var bad_copy: Resource = bad_stub.tables["event_check"].duplicate(true)
	for row: Resource in bad_copy.rows:
		if str(row.check_id) == "ev_shed_trap":
			row.reward_type = "bogus"
	bad_stub.tables["event_check"] = bad_copy
	var bad_state = solo_state(bad_stub)
	var bad_service = EventCheckServiceScript.new(bad_stub, bad_state, RngServiceScript.new(SEED))
	var bad: Dictionary = bad_service.resolve("ev_shed_trap")
	check_true(str(bad["text"]).contains("不是设计允许"), "非法奖励类型要讲出来：%s" % bad["text"])
	check_false(str(bad["text"]).contains("bogus"), "非法类型也不许把配置 token 甩给玩家：%s" % bad["text"])

	# ⑦ **玩家可见文案不许出现表内 id**（AGENTS 硬规矩）：数据错时 id 只进日志。
	#    这一组把 4 条错路径逐条走一遍——坏 id（equip／room／boss）与坏 check_id。
	var leak_stub = TableDbScript.new()
	leak_stub.load_all()
	var leak_copy: Resource = leak_stub.tables["event_check"].duplicate(true)
	for row: Resource in leak_copy.rows:
		if str(row.check_id) == "ev_herbalist_help":
			row.reward_type = "equip"
			row.reward_id = "eq_does_not_exist"
		# 换一条**硬判定且必过**的来试 room（软判定会掷骰 → 后半段断言会时跑时不跑）
		if str(row.check_id) == "ev_shed_trap":
			row.reward_type = "room"
			row.reward_id = "hf_does_not_exist"
	leak_stub.tables["event_check"] = leak_copy
	var leak_state = solo_state(leak_stub)
	var leak_service = EventCheckServiceScript.new(leak_stub, leak_state, RngServiceScript.new(SEED))
	var equip_leak: Dictionary = leak_service.resolve("ev_herbalist_help")
	check_false(
		str(equip_leak["text"]).contains("eq_does_not_exist"),
		"equip 奖励写错 id：文案不漏 id（%s）" % str(equip_leak["text"]),
	)
	check_true(str(equip_leak["text"]).contains("数据错"), "equip 奖励写错 id：文案说清是数据错：%s" % str(equip_leak["text"]))
	var room_leak: Dictionary = leak_service.resolve("ev_shed_trap")
	check_false(
		str(room_leak["text"]).contains("hf_does_not_exist"),
		"room 奖励写错 id：文案不漏 id（%s）" % str(room_leak["text"]),
	)
	var missing: Dictionary = leak_service.judge("ev_does_not_exist")
	check_false(bool(missing["ok"]), "没有这条判定要如实拒绝")
	check_false(str(missing["text"]).contains("ev_does_not_exist"), "查不到的判定也不漏 id：%s" % str(missing["text"]))

	# ⑥ `fail_note` 写了「会付代价」但设计没给数值的那种，界面要如实说明（同奇袭「暂缓规则」的口径）
	var cost_stub = TableDbScript.new()
	cost_stub.load_all()
	var cost_copy: Resource = cost_stub.tables["event_check"].duplicate(true)
	for row: Resource in cost_copy.rows:
		if str(row.check_id) == "ev_shed_trap":
			row.difficulty = 99      # 让 1 级书生（奇门 3）必定失败
	cost_stub.tables["event_check"] = cost_copy
	var cost_state = solo_state(cost_stub)
	var cost_service = EventCheckServiceScript.new(cost_stub, cost_state, RngServiceScript.new(SEED))
	var trap_fail: Dictionary = cost_service.resolve("ev_shed_trap")
	check_false(bool(trap_fail["success"]), "难度 99：识破落石陷阱失败")
	check_true(str(trap_fail["text"]).contains("落石"), "转述表里的 fail_note：%s" % trap_fail["text"])
	check_true(
		str(trap_fail["text"]).contains("还没接"),
		"代价还没接要说出来，别让玩家以为真掉了血：%s" % trap_fail["text"],
	)
	# 反例：「推不开粗木闩」是状态描述，不该挂代价说明
	var plain_gate = EventCheckServiceScript.new(db, solo_state(db), RngServiceScript.new(SEED))
	var plain_text := str(plain_gate.resolve("ev_force_gate")["text"])
	check_false(plain_text.contains("还没接"), "状态类 fail_note 不挂代价说明：%s" % plain_text)
