## 大地图随机事件（设计 19 §四；0.28.0 Q64 把八行的 `effect_id` 填齐之后的接线）。
##
## 五类效果分两层验：
## - `gift`／`hint`／`check` 在**服务层**就结算完（发物品、记线索、走判定）；
## - `trade`／`spar` 只交出 `scene_action` 请求——开货架与切战斗要挂在当前场景的节点上，
##   由 `overworld_controller._apply_world_event_effect()` 动手（那一层在场景自检里验）。
##
## 这一批用例的另一半作用是把「八行 effect_id 都填过」钉住：空一行就会有人拿到一句
## 「效果还没接上」的占位文案，而那正是 0.28.0 要消掉的东西。
extends "res://tests/test_case.gd"

const WorldEventServiceScript := preload("res://src/core/world_event_service.gd")
const ShopServiceScript := preload("res://src/core/shop_service.gd")
const ClueServiceScript := preload("res://src/core/clue_service.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")

## 软判定要掷骰，用例靠固定种子复现
const SEED := 20261004


func suite_name() -> String:
	return "大地图随机事件"


func run() -> void:
	var db = get_db()
	_check_wired_rows(db)
	_check_gift(db)
	_check_hint(db)
	_check_check_effect(db)
	_check_scene_actions(db)
	_check_unwired(db)


## 八条事件的 effect_kind／effect_id 与 0.28.0 的 Q64 表逐条一致
func _check_wired_rows(db) -> void:
	var expected := {
		"we_caravan": ["trade", "shop_caravan"],
		"we_disciple": ["spar", "team_wanderer_disciple"],
		"we_wanderer": ["hint", "trig_stealth_clear"],
		"we_hermit": ["gift", "eq_sword_04"],
		"we_lost_item": ["gift", "item_herb"],
		"we_patrol": ["check", "ev_patrol_check"],
		"we_rumor": ["hint", "trig_wine"],
		"we_dog": ["gift", "item_pelt"],
	}
	var rows := 0
	for row: Resource in db.rows("world_event"):
		rows += 1
		var event_id := str(row.event_id)
		check_true(
			not str(row.effect_id).is_empty(),
			"%s 的效果有指向（0.28.0 之前八行全空）" % event_id,
		)
		check_true(
			expected.has(event_id), "%s 在用例登记的八条里" % event_id,
		)
		if not expected.has(event_id):
			continue
		var want: Array = expected[event_id]
		check_eq(str(row.effect_kind), str(want[0]), "%s 的效果类型" % event_id)
		check_eq(str(row.effect_id), str(want[1]), "%s 的效果指向" % event_id)
	check_eq(rows, 8, "大地图随机事件 8 条")


## 赠礼：`we_hermit` 给良品剑「锈月」——它原先**没有任何来源**（0.28.0 才补上）
func _check_gift(db) -> void:
	var state = solo_state(db)
	# 开局本来就穿着模板给的初始装备，所以按**增量**断言，不写死总数
	var before: int = state.inventory.equipment.size()
	var result: Dictionary = WorldEventServiceScript.trigger(db, state, "we_hermit", RngServiceScript.new(SEED))
	var effect: Dictionary = result.get("effect", {})
	check_true(bool(effect.get("applied", false)), "隐士奇遇的赠礼真的发出来了")
	check_eq(str(effect.get("kind", "")), "gift", "它是赠礼类")
	check_eq(str(effect.get("id", "")), "eq_sword_04", "给的是锈月")
	check_eq(state.inventory.equipment.size(), before + 1, "背包里多出 1 件装备实例")
	check_true(
		str(effect.get("scene_action", "")).is_empty(), "赠礼不需要场景层动手",
	)
	# 一次性事件（repeatable=0）触发过就记账
	check_true(
		state.has_flag(WorldEventServiceScript.done_flag("we_hermit")),
		"一次性事件触发后记了账",
	)

	# 材料同理（`we_dog` 的兽皮），顺带验非装备走堆叠
	var item_state = solo_state(db)
	WorldEventServiceScript.trigger(db, item_state, "we_dog", RngServiceScript.new(SEED))
	check_eq(item_state.inventory.count("item_pelt"), 1, "野狗拦路给到 1 张兽皮")


## 线索：`we_rumor` 给醉刀客的那条线索记进线索本（不再只是「看一眼就走的空气」）
func _check_hint(db) -> void:
	var state = solo_state(db)
	var clue_service = ClueServiceScript.new(db, state)
	var before := _wine_entry(clue_service)
	check_false(bool(before.get("hint_known", false)), "触发之前这条线索还没「听说」过")

	var result: Dictionary = WorldEventServiceScript.trigger(db, state, "we_rumor", RngServiceScript.new(SEED))
	var effect: Dictionary = result.get("effect", {})
	check_true(bool(effect.get("applied", false)), "驿站风声给到了信息")
	check_eq(str(effect.get("id", "")), "trig_wine", "指向的是醉刀客那条隐藏内容")
	check_true(
		state.has_flag(WorldEventServiceScript.hint_flag("trig_wine")),
		"线索旗标写进了存档",
	)
	var after := _wine_entry(clue_service)
	check_true(bool(after.get("hint_known", false)), "线索本这时标成「已听说」")
	check_true(
		clue_service.describe(after).contains("已从传闻中听说"),
		"线索本那一行写明了来源：%s" % clue_service.describe(after),
	)


func _wine_entry(clue_service) -> Dictionary:
	for entry: Dictionary in clue_service.clues_for_scene("scene_heifengzhai"):
		if str(entry.get("id", "")) == "trig_wine":
			return entry
	return {}


## 判定：`we_patrol` 走**直接结算**，不要求玩家走到官道那个固定关卡（Q64 两个入口共用一行）
func _check_check_effect(db) -> void:
	var state = solo_state(db)
	var result: Dictionary = WorldEventServiceScript.trigger(db, state, "we_patrol", RngServiceScript.new(SEED))
	var effect: Dictionary = result.get("effect", {})
	check_true(bool(effect.get("applied", false)), "镇抚司巡查当场判定了")
	check_eq(str(effect.get("kind", "")), "check", "它是判定类")
	check_eq(str(effect.get("id", "")), "ev_patrol_check", "走的是共享的那一行判定")
	check_true(not str(effect.get("text", "")).is_empty(), "判定结果有给玩家看的文案")
	check_true(
		state.event_check_result("ev_patrol_check") in ["done", "failed"],
		"判定结果写进了存档（%s）" % state.event_check_result("ev_patrol_check"),
	)
	# 这一行判定在设计里**没有小地图位点**，所以它的入口只能是随机事件
	check_true(
		str(db.get_row("event_check", "ev_patrol_check").scene_id).is_empty(),
		"ev_patrol_check 本来就不挂小地图",
	)


## 开货架与开战：服务层只交请求，id 必须是**能落到表里的**那种（货架组要能找回建筑）
func _check_scene_actions(db) -> void:
	var state = solo_state(db)
	var trade: Dictionary = WorldEventServiceScript.trigger(db, state, "we_caravan", RngServiceScript.new(SEED)).get("effect", {})
	check_eq(str(trade.get("scene_action", "")), "open_shop", "货商车队交出「开货架」请求")
	check_eq(str(trade.get("id", "")), "shop_caravan", "指向的是货架组")
	check_eq(
		ShopServiceScript.building_for_shop_group(db, "shop_caravan"), "bld_caravan",
		"货架组能找回那个临时建筑",
	)
	check_true(
		not ShopServiceScript.new(db, state).stock_rows("bld_caravan").is_empty(),
		"行商的货架不是空的",
	)

	var spar: Dictionary = WorldEventServiceScript.trigger(db, state, "we_disciple", RngServiceScript.new(SEED)).get("effect", {})
	check_eq(str(spar.get("scene_action", "")), "start_battle", "门派弟子交出「开战」请求")
	check_eq(str(spar.get("id", "")), "team_wanderer_disciple", "打的是那支历练队伍")
	var team: Resource = db.get_row("enemy_team", "team_wanderer_disciple")
	check_true(team != null, "那支队伍在表里")
	if team != null:
		check_eq(str(team.members), "en_wanderer_disciple:2", "两人都是别派弟子")
	check_eq(
		str(db.get_row("enemy_base", "en_wanderer_disciple").drop_group), "",
		"别派弟子不掉东西（切磋不是抢东西）",
	)


## 反面：`effect_id` 空着、或效果类型认不出时，**显式说话**，不假装发过东西
func _check_unwired(db) -> void:
	var state = solo_state(db)
	var blank: Resource = db.get_row("world_event", "we_dog").duplicate()
	blank.effect_id = ""
	var blank_effect: Dictionary = WorldEventServiceScript._apply_effect(db, state, blank)
	check_false(bool(blank_effect.get("applied", false)), "没配效果时不假装发过东西")
	check_true(
		str(blank_effect.get("note", "")).contains("还没配"),
		"并且明说没配：%s" % str(blank_effect.get("note", "")),
	)

	var odd: Resource = db.get_row("world_event", "we_dog").duplicate()
	odd.effect_kind = "teleport"
	var odd_effect: Dictionary = WorldEventServiceScript._apply_effect(db, state, odd)
	check_false(bool(odd_effect.get("applied", false)), "没见过的效果类型不发东西")
	check_true(
		str(odd_effect.get("note", "")).contains("没见过的效果类型"),
		"并且点名报错：%s" % str(odd_effect.get("note", "")),
	)
