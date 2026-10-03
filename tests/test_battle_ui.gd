## 战斗界面：真实场景、真实按钮。
extends "res://tests/test_case.gd"

const BATTLE_SCENE := "res://scenes/battle_screen.tscn"
const GameStateScript := preload("res://src/core/game_state.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const EncounterScript := preload("res://src/core/encounter.gd")
const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleScreenScript := preload("res://src/ui/battle_screen.gd")
const WorldMapServiceScript := preload("res://src/core/world_map_service.gd")
const SfxScript := preload("res://src/audio/sfx.gd")
const PracticeServiceScript := preload("res://src/core/practice_service.gd")

## 测试用的「写不进去」的存档设施：只实现 SaveService 会用到的那几个方法。
## 用途：验「自动存档失败**不静默**」——玩家得在战报里看到一句，而不是以为进度存住了。
class FailingStore extends RefCounted:
	func ensure_dir() -> void:
		pass

	func save_slot(_slot: int, _state) -> Dictionary:
		return {"ok": false, "error": "测试注入：盘写不进去"}

	func slot_path(_slot: int) -> String:
		return ""


func suite_name() -> String:
	return "战斗界面"


func run() -> void:
	if scene_tree == null:
		fail("没有注入场景树")
		return
	var db = get_db()
	# 战斗界面的手感／文案闸门常量（变异探针点名「没人钉」的四条）——写死绝对值
	check_float(BattleScreenScript.AUTO_INTERVAL, 0.8, "自动战斗的推进间隔 0.8 秒", 0.0001)
	check_eq(BattleScreenScript.SELFTEST_SEED, 20261003, "战斗自检的固定种子（决策 236：不固定就会时红时绿）")
	check_eq(BattleScreenScript.SETTLE_DROP_ITEMS, 3, "结算卡片最多列 3 件掉落")
	check_eq(BattleScreenScript.SETTLE_DROP_MAX_CHARS, 48, "结算卡片掉落行最多 48 字")
	var state = solo_state(db)
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	# 打完自动存档：给个槽位与临时目录，验证结算真的落盘
	var store = SaveStoreScript.new("res://.logs/test_save_timing/run_battle", 3)
	store.ensure_dir()
	state.slot = 1
	battle.save_store_override = store
	# 固定种子：命中／闪避是掷点决定的，不固定的话「出招后血量下降」这类断言会随机红
	battle.rng_seed = 20261003
	var returns: Array = []
	battle.return_handler = func() -> void: returns.append(true)
	var settle_reason := ""
	scene_tree.root.add_child(battle)
	battle.setup()

	_check_layout(battle)
	_check_rounds(battle)
	_check_status_ui(battle)
	_check_resource_bars(battle)
	_check_finish(battle, returns)
	# 结算时自动存档的原因要在 free 之前取出来（对象释放后就问不到了）
	settle_reason = str(battle.save_service().last_reason)

	scene_tree.root.remove_child(battle)
	battle.free()
	_check_first_kill_once(db)
	_check_boss_kills_unlock_nightmare(db, store)
	_check_auto_battle(db)
	_check_parry_ui(db)
	_check_flee_ui(db)
	_check_defeat_settle(db)
	_check_practice_settle(db)
	_check_draw_settle(db)
	_check_skill_buttons_ui(db)
	_check_support_skill_button(db)
	_check_command_ui(db)
	_check_settle_card_contract(db)
	_check_autosave_failure(db)
	check_true(store.slot_exists(1), "战斗结算后自动落盘")
	check_eq(settle_reason, "战斗结算", "记下存档原因（设计 02：副本内自动存）")


## 自动存档失败**不静默**（2026-10-03 补）：失败不打断结算（AGENTS：不许挡住开局/结算），
## 但战报里要有一句「自动存档失败：…（这一场的结果只在本局里）」——
## 玩家的运行环境真有可能写不进 `user://saves`（本工程的沙箱就出过 `user://` 写失败的事故）。
func _check_autosave_failure(db) -> void:
	var state = solo_state(db)
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.save_store_override = FailingStore.new()
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	check_not_null(battle.save_service(), "存档设施建得起来（注入的是写不进去的那个）")
	for ally in battle.allies:
		ally.hp = 0
	battle._settle()
	var log_text := str(battle._log.get_parsed_text())
	check_true(log_text.contains("自动存档失败"), "战报里如实写「自动存档失败」：%s" % log_text)
	check_true(log_text.contains("只在本局里"), "并说明后果：这一段的进度没存上")
	check_true(battle.settled, "失败也不打断结算（卡片与按钮照常收尾）")
	scene_tree.root.remove_child(battle)
	battle.free()


func _check_layout(battle) -> void:
	check_gt(float(battle.allies.size()), 0.0, "我方有战斗单位")
	check_gt(float(battle.enemies.size()), 0.0, "敌方有战斗单位")
	var headline: Label = battle.find_child("Headline", true, false)
	check_not_null(headline, "有开场文案")
	if headline != null:
		check_true(headline.text.contains("遭遇") or headline.text.contains("偷袭"), "文案说明接触方式：%s" % headline.text)
	check_not_null(battle.find_child("Log", true, false), "有战斗日志")
	var flee: Button = battle.find_child("FleeButton", true, false)
	check_not_null(flee, "有逃跑按钮")
	if flee != null:
		check_true(flee.disabled, "开打前（还没轮到我方）逃跑置灰：%s" % flee.tooltip_text)
	var actor_label: Label = battle.find_child("Name_%s" % battle.allies[0].actor_id, true, false)
	check_not_null(actor_label, "我方面板列出战斗单位")
	if actor_label != null:
		check_true(actor_label.text.contains("气血"), "显示气血：%s" % actor_label.text)
	_check_scene_layout(battle)


## 界面骨架必须是 scenes/battle_screen.tscn 里的真实节点（本轮从代码搭建改成 .tscn），
## 卡片行仍然按战斗状态动态生成，但要挂在场景里那两个列表节点下。
## （「整页塞得进设计分辨率」由真实场景自检 --battle-selftest 断言：那里才跑过帧、版式算得出来）
func _check_scene_layout(battle) -> void:
	check_not_null(battle.get_node_or_null("Margin/Column/Headline"), "Headline 是场景节点")
	check_not_null(battle.get_node_or_null("Margin/Column/Buttons/NextRoundButton"), "按钮是场景节点")
	var enemy_list_path := "Margin/Column/Front/TeamEnemies/EnemyScroll/ListEnemies"
	check_not_null(battle.get_node_or_null(enemy_list_path), "敌方列表是场景节点")
	var scroll: ScrollContainer = battle.get_node("Margin/Column/Front/TeamEnemies/EnemyScroll")
	var enemy_list: Node = battle.get_node(enemy_list_path)
	check_gt(scroll.custom_minimum_size.y, 0.0, "敌方列表留了滚动区高度")
	var pick: Button = battle.find_child("TargetButton_%s" % battle.enemies[0].actor_id, true, false)
	check_not_null(pick, "敌人卡片有选目标按钮")
	if pick != null:
		check_true(enemy_list.is_ancestor_of(pick), "动态卡片挂在场景的敌方列表下")


## 异常状态在战斗界面上的表现：卡片上有状态行、持续伤害跳字用状态颜色
func _check_status_ui(battle) -> void:
	var target = battle.enemies[0]
	target.add_status("poison", "test", 7.0, 5, 3, "", true)
	battle._refresh()
	var label: Label = battle.find_child("Status_%s" % target.actor_id, true, false)
	check_not_null(label, "卡片上有异常状态行")
	if label != null:
		check_true(label.text.contains("中毒"), "状态行写清是中毒：%s" % label.text)
		check_true(label.text.contains("×1"), "状态行写清层数：%s" % label.text)
		check_eq(label.get_theme_color("font_color"), Color("7fd35a"), "中毒用伤害类型的颜色")

	# 持续伤害跳字：颜色跟着状态走
	battle._spawn_floats([{
		"kind": "dot", "target": target.actor_id, "damage": 12, "status_id": "poison",
	}])
	var float_label: Label = battle.find_child("Float_%s" % target.actor_id, true, false)
	check_not_null(float_label, "持续伤害也有跳字")
	if float_label != null:
		check_eq(float_label.get_theme_color("font_color"), Color("7fd35a"), "毒伤跳字用中毒色")
		float_label.queue_free()
	target.statuses = []
	battle._refresh()

	# 多段跳字要错开位置，不然两段数字叠在一起
	battle._spawn_floats([
		{"kind": "hit", "target": target.actor_id, "damage": 10, "hit_index": 0, "hit_count": 2},
		{"kind": "hit", "target": target.actor_id, "damage": 12, "hit_index": 1, "hit_count": 2},
	])
	var first: Label = battle.find_child("Float_%s" % target.actor_id, true, false)
	var floats: Array = []
	# 跳字挂在 FloatLayer 上（不受卡片容器排版），所以要满场景找
	for node in battle.find_children("Float_*", "Label", true, false):
		# 前一段用例 queue_free 过的还在树上（要等帧末才真的走），别数进来
		if node.is_queued_for_deletion():
			continue
		floats.append(node)
	check_eq(floats.size(), 2, "两段各有一个跳字")
	if floats.size() == 2:
		check_ne(
			(floats[0] as Label).position.x, (floats[1] as Label).position.x,
			"第二段跳字横向错开：%s vs %s" % [floats[0].position, floats[1].position],
		)
		# 跳字必须挂在**不受容器排版**的浮层上：以前挂在卡片里，VBoxContainer 每次排版
		# 都把它按到卡片左边、半个数字被滚动区裁掉（测试全绿但画面是坏的）。
		# 版式要跑过帧才算得出来，所以这里钉结构、真实位置交给 --battle-selftest 与截图。
		check_eq(str(floats[0].get_parent().name), "FloatLayer", "跳字挂在浮层上")
		var card: Control = battle.find_child("Actor_%s" % target.actor_id, true, false)
		check_not_null(card, "目标卡片在")
		if card != null:
			check_false(card.is_ancestor_of(floats[0]), "跳字不再挂在卡片里（那样会被容器顶到左边）")
	# 浮层有尺寸时，落点必须被**夹进浮层**——这是**护栏测试**，不是复现线上问题：
	# 真窗口实测（`--battle-selftest` 带窗口跑，决策 199）跳字落点是 x≈610（敌卡）／232（我方卡），
	# **今天不会出界**；但落点是"卡片位置 + 210 + 26×段数"这种写死的算术，没人拦着它以后变。
	# **headless 里卡片没有真实坐标**（容器还没排版）→ 不夹取也照样"在界内"，那是假绿；
	# 所以这里手动把卡片摆到右边缘，让落点**明确越界**再验（写完先反向验证过一次才发现，见决策 199）。
	var layer: Control = battle.find_child("FloatLayer", true, false)
	var clamp_card: Control = battle.find_child("Actor_%s" % target.actor_id, true, false)
	if layer != null and clamp_card != null:
		var saved_card_pos := clamp_card.position
		layer.size = Vector2(1152, 648)
		clamp_card.position = Vector2(1100, 120)
		# 用 hit_index=4 起个不重名的（前几段跳字还挂在树上等帧末，同名会被引擎改名）；
		# 4 也是**表里真有的**最大段序号（`skill_active.hit_count` 最大 5 → 序号 0..4），
		# 所以这条夹具对应「五段招式打在右列敌人身上」，不是造一个数据到不了的场景。
		battle._spawn_floats([{"kind": "hit", "target": target.actor_id, "damage": 9, "hit_index": 4}])
		var placed: Label = battle.find_child("Float_%s_4" % target.actor_id, true, false)
		check_not_null(placed, "夹取用的跳字建出来了")
		if placed != null:
			check_true(
				placed.position.x >= 0.0 and placed.position.x + placed.size.x <= 1152.5,
				"卡片贴右边缘时跳字仍被夹在界内（x=%.0f 宽=%.0f）" % [placed.position.x, placed.size.x],
			)
			placed.queue_free()
		clamp_card.position = saved_card_pos
	for node in floats:
		node.queue_free()

	# 阵亡：卡片压暗 + 名字标「（倒）」
	target.hp = 0
	battle._refresh()
	var dead_label: Label = battle.find_child("Name_%s" % target.actor_id, true, false)
	check_not_null(dead_label, "阵亡单位还在名单上")
	if dead_label != null:
		check_true(dead_label.text.contains("倒"), "阵亡标「（倒）」：%s" % dead_label.text)
		var row: Node = battle.find_child("Actor_%s" % target.actor_id, true, false)
		check_not_null(row, "阵亡单位卡片还在")
		if row != null:
			check_lt(float(row.modulate.a), 0.6, "阵亡卡片压暗（alpha %.2f）" % row.modulate.a)
	# 出手动作：出手者的卡片会闪一下暖光（挂在容器里不能动位置，容器会顶掉补间）
	var attacker_row: Node = battle.find_child("Actor_%s" % battle.allies[0].actor_id, true, false)
	check_not_null(attacker_row, "出手者的卡片在（不然出手动画那几条会静默跳过）")
	if attacker_row != null:
		battle._lunge_attackers([{
			"kind": "hit", "source": str(battle.allies[0].actor_id), "target": target.actor_id, "damage": 5,
		}])
		check_gt(float(attacker_row.modulate.r), 1.0, "出手者闪暖光（r=%.2f）" % attacker_row.modulate.r)
		# 分帧：第一帧是「蓄势」——先压一下再探身出去（补间跑起来之前就能看到）
		check_lt(float(attacker_row.scale.x), 1.0, "出手第一帧是蓄势压一下（scale=%.2f）" % attacker_row.scale.x)
		check_gt(attacker_row.pivot_offset.x, 0.0, "缩放绕卡片中心，不是从左上角涨")
	# 受击分帧：被打的那一方先被压扁再弹回（用还活着的 2 号敌人，别借刚做阵亡断言的那个）
	var victim = battle.enemies[1]
	var victim_row: Node = battle.find_child("Actor_%s" % victim.actor_id, true, false)
	check_not_null(victim_row, "受击者的卡片在")
	if victim_row != null:
		battle._spawn_floats([{
			"kind": "hit", "source": str(battle.allies[0].actor_id), "target": victim.actor_id, "damage": 7,
		}])
		check_lt(float(victim_row.scale.x), 1.0, "受击第一帧被压扁（scale=%.2f）" % victim_row.scale.x)
		check_gt(float(victim_row.modulate.r), 1.0, "受击也闪一下")
		for node in battle._enemies_box.get_children():
			for child in node.get_children():
				if child is Label and str(child.name).begins_with("Float_"):
					child.queue_free()
	# 濒死提示：血量 ≤25% 时名字转红并标「危急」
	battle.allies[0].hp = maxi(1, int(float(battle.allies[0].max_hp()) * 0.2))
	battle._refresh()
	var hurt: Label = battle.find_child("Name_%s" % battle.allies[0].actor_id, true, false)
	check_not_null(hurt, "我方卡片还在")
	if hurt != null:
		check_true(hurt.text.contains("危急"), "濒死标注：%s" % hurt.text)

	# 结算的掉落明细：物品与装备都写名字（装备带词条）
	var fake_instance: String = battle.state.inventory.add_equipment(
		battle.db, "eq_sword_01",
		[{"affix_id": "af_str_01", "target": "attr:str", "value_kind": "attr_point", "value": 3.0}],
	)
	var described: String = battle._describe_drops({
		"items": [{"item_id": "item_herb", "qty": 2}],
		"equipment": [fake_instance],
	})
	check_true(described.contains("草药 ×2"), "掉落明细写物品名与数量：%s" % described)
	check_true(described.contains("铁剑"), "掉落明细写装备名：%s" % described)
	check_true(described.contains("力 +3"), "掉落明细带词条：%s" % described)


func _check_rounds(battle) -> void:
	check_eq(battle.round_text(), "第 1 回合　进行中", "开局停在第 1 回合")
	battle.press_next_round()
	check_eq(int(battle.sim.rounds_played()), 1, "点下一回合推进一回合")
	battle.toggle_speed()
	check_float(battle.speed(), 2.0, "速度切到 2x")
	battle.toggle_speed()
	check_float(battle.speed(), 4.0, "速度切到 4x")
	battle.toggle_speed()
	check_float(battle.speed(), 1.0, "速度回到 1x")
	# 自动战斗策略：按钮轮换并把当前策略写在按钮上（只影响我方自动战斗）
	check_true(battle.strategy_text().contains("均衡"), "策略按钮默认写均衡：%s" % battle.strategy_text())
	check_eq(battle.press_strategy(), "all_out", "轮换到全力")
	check_true(battle.strategy_text().contains("全力"), "按钮跟上：%s" % battle.strategy_text())
	battle.press_strategy()
	check_true(battle.strategy_text().contains("保守"), "再轮换到保守：%s" % battle.strategy_text())
	battle.press_strategy()
	check_true(battle.strategy_text().contains("均衡"), "轮换回均衡（不影响后面的断言）")
	# 逃跑的完整行为另有专门的用例（_check_flee_ui）；这里只确认按钮状态与原因一致，
	# 不能真按下去——按下去可能直接结束战斗，后面的出招断言就没得测了
	var flee: Button = battle.find_child("FleeButton", true, false)
	check_not_null(flee, "有逃跑按钮")
	if flee != null:
		check_false(flee.text.contains("未实现"), "逃跑按钮不再是占位文案：%s" % flee.text)
		check_false(flee.disabled, "轮到我方时逃跑可按（原因：%s）" % flee.tooltip_text)
	battle.press_auto()
	check_true(battle.auto_running(), "点了自动战斗会进入自动状态")
	battle.press_stop_auto()
	check_false(battle.auto_running(), "可以停下自动战斗")

	# 点敌人选目标：选第二个敌人后，出招应该打它而不是"血量最低"
	var second = battle.enemies[1]
	var pick: Button = battle.find_child("TargetButton_%s" % second.actor_id, true, false)
	check_not_null(pick, "敌人卡片上有选目标的按钮")
	if pick != null:
		var hp_before: int = _enemy_hp_total(battle)
		pick.emit_signal("pressed")
		check_eq(battle.selected_target_id, second.actor_id, "选中状态记在界面上")
		var actor = battle.sim.current_actor() if battle.sim.in_round() else null
		if actor == null or not battle.allies.has(actor):
			battle.press_next_round()
			actor = battle.sim.current_actor() if battle.sim.in_round() else null
		if actor != null and battle.allies.has(actor):
			var skills: Array = battle.sim.available_skills(actor)
			if not skills.is_empty():
				var second_hp_before: int = second.hp
				battle.press_skill(str(skills[0].skill_id))
				check_lt(float(second.hp), float(second_hp_before), "出招打的是选中的敌人")
				check_lt(float(_enemy_hp_total(battle)), float(hp_before), "总血量也下降了")
				check_not_null(
					battle.find_child("Float_%s" % second.actor_id, true, false),
					"命中后目标卡片上有伤害跳字"
				)


func _check_finish(battle, returns: Array) -> void:
	var guard := 0
	SfxScript.clear_log()
	while not battle.finished() and guard < 120:
		# 「下一回合」现在只推进到我方行动，所以轮到我方时要出招
		var actor = battle.sim.current_actor() if battle.sim.in_round() else null
		if actor != null and battle.allies.has(actor):
			var skills: Array = battle.sim.available_skills(actor)
			if skills.is_empty():
				battle.press_next_round()
			else:
				var before_hp := _enemy_hp_total(battle)
				var result: Dictionary = battle.press_skill(str(skills[0].skill_id))
				check_true(bool(result["ok"]), "手选招式能出招：%s" % result)
				check_lt(float(_enemy_hp_total(battle)), float(before_hp), "出招后敌人总血量下降")
		else:
			battle.press_next_round()
		guard += 1


	check_true(battle.finished(), "战斗能打完")
	# 音效（设计 07 §8.4）：这一场真打完了 → 命中那一刻必须请求过音效
	check_true(SfxScript.has_played("hit"), "命中会请求音效（伤害跳字那一刻）")
	check_true(battle.result_label_text() != "", "打完显示结果")
	# 熟练度：打完这一场，真正施放过的招式要涨熟练度并写回存档
	var char_id := str(battle.allies[0].actor_id)
	var learned_ids = state_mastery_ids(battle)
	check_gt(
		float(state_mastery_of(battle, char_id, "sk_xuanwei_01")), 0.0,
		"打完把熟练度写回存档（%s）" % str(learned_ids),
	)
	check_true(battle.result_label_text().contains("熟练度"), "结算里播报熟练度：%s" % battle.result_label_text())
	var next: Button = battle.find_child("NextRoundButton", true, false)
	check_not_null(next, "有「下一回合」按钮")
	if next != null:
		check_true(next.disabled, "打完后不能再点下一回合")
	var back: Button = battle.find_child("ReturnButton", true, false)
	check_not_null(back, "有返回大地图按钮")
	if back != null:
		back.emit_signal("pressed")
		check_eq(returns.size(), 1, "返回按钮触发回程")


func _enemy_hp_total(battle) -> int:
	var total := 0
	for enemy in battle.enemies:
		total += maxi(0, enemy.hp)
	return total


## 结算卡片契约：最多三行，按「战果 → 掉落 → 升级 → 领悟 → 熟练度 → 暂缓规则」取前三行；
## 放不下的**必须进战报**，不能被丢掉（卡片位置有限，但信息不能丢）。
func _check_settle_card_contract(db) -> void:
	var state = solo_state(db)
	# 攒够升好几级的经验，让「升级」这一行真的出现，卡片才会被塞满
	state.party_exp = 400
	var store = SaveStoreScript.new("res://.logs/test_save_timing/settle_card", 3)
	store.ensure_dir()
	var settle: Dictionary = _settle_boss(db, state, store)
	var card := str(settle["card"])
	var log_text := str(settle["log"])
	var lines: Array = settle["lines"]
	check_eq(
		lines.size(), BattleScreenScript.SETTLE_CARD_LINES,
		"结算卡片行数 = 设定上限（%d）" % BattleScreenScript.SETTLE_CARD_LINES,
	)
	check_true(str(lines[0]).contains("经验"), "第一行是战果：%s" % str(lines[0]))
	check_true(str(lines[1]).contains("掉落"), "第二行是掉落：%s" % str(lines[1]))
	check_true(str(lines[2]).contains("升级"), "第三行是升级：%s" % str(lines[2]))
	# 武学那一段排在升级之后，卡片放不下 → 必须出现在战报里，且不该挤进卡片。
	# 文字形态有两种：学得会写「领悟：…」，全被修习门槛挡住写「未习得：…」（低等级打 BOSS 就是后者）。
	var skill_line_shown := log_text.contains("领悟") or log_text.contains("未习得")
	check_true(skill_line_shown, "卡片放不下的武学一行进了战报：%s" % log_text)
	check_false(
		card.contains("领悟") or card.contains("未习得"),
		"卡片里不会挤进第四项：%s" % card,
	)


## 逃跑在真实界面上的三态：开打前不可按、轮到我方可按、按下后（强制成功）撤退收场且不结算奖励。
func _check_flee_ui(db) -> void:
	var state = solo_state(db)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/flee_ui", 3)
	store.ensure_dir()
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	# 逃跑是掷点判定的，界面用例靠 force_flee 把结局钉死
	battle.modifiers_override = {"force_flee": true}
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	var flee: Button = battle.find_child("FleeButton", true, false)
	check_not_null(flee, "有逃跑按钮")
	if flee == null:
		scene_tree.root.remove_child(battle)
		battle.free()
		return
	check_false(flee.text.contains("未实现"), "逃跑按钮不再是「未实现」占位：%s" % flee.text)
	check_true(flee.disabled, "还没轮到我方时逃跑置灰：%s" % flee.tooltip_text)
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	var spawn_key := str(battle.encounter.source_key)
	var cleared_before: bool = session_node != null and session_node.cleared_spawns.has(spawn_key)
	var money_before: int = state.inventory.money

	battle.press_next_round()
	check_false(flee.disabled, "轮到我方时逃跑可按（原因：%s）" % flee.tooltip_text)
	# 能撤时 tooltip 要写出**现算的**成功率（和判定面板同一口径；写死数字的话设计改公式就假了）
	var flee_actor = battle.sim.current_actor()
	var expected_pct := int(round(battle.sim.flee_chance(flee_actor) * 100.0))
	check_true(
		flee.tooltip_text.contains("%d%%" % expected_pct),
		"可撤退时 tooltip 写出成功率（期望 %d%%，实际「%s」）" % [expected_pct, flee.tooltip_text],
	)
	var result: Dictionary = battle.press_flee()
	check_true(bool(result.get("ok", false)), "按下逃跑能判定：%s" % str(result))
	check_true(bool(result.get("escaped", false)), "force_flee 下撤退成功")
	check_eq(battle.sim.winner(), "flee", "战斗以撤退收场")
	check_true(battle.result_label_text().contains("撤退"), "结算卡片写撤退：%s" % battle.result_label_text())
	check_eq(state.inventory.money, money_before, "撤退不结算铜钱")
	check_eq(state.party_exp, 0, "撤退不给经验")
	check_true(flee.disabled, "撤退后按钮置灰")
	var cleared_after: bool = session_node != null and session_node.cleared_spawns.has(spawn_key)
	check_false(cleared_after and not cleared_before, "撤退不会把明雷标成已清（回来还能打）")
	scene_tree.root.remove_child(battle)
	battle.free()


## 败北结算是**整条分支从来没被跑过**（现有用例只打过胜利／撤退／拆招／自动战斗）：
## 把败北误判成胜利、败北时顺手发奖励、或者顺序写反，以前一条断言都不会响。
## 这条真的把全队按到 0 血 → 走真场景的 `_settle()`，钉住设计口径：
## 败北零收获（不发经验／铜钱／掉落、不记首杀、不清明雷）＋**战败处理**（08 已定）：
## 全队回到最近到过的出生点／城镇、气血回满、战斗外增益全部清除；没到过城镇就退回大地图。
func _check_defeat_settle(db) -> void:
	var state = solo_state(db)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/defeat", 3)
	store.ensure_dir()
	state.slot = 1
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	var session_node := scene_tree.root.get_node_or_null("GameSession")
	# 带一个战斗外增益进战斗：败北要把它清掉（08 的败北处理第 ④ 条）
	check_not_null(session_node, "用例能拿到会话（战败处理写在会话里）")
	if session_node != null:
		session_node.clear_field_buffs()
		session_node.add_field_buff("buff_meditated", 10)
		check_eq(session_node.active_field_buffs().size(), 1, "开打前带着一条战斗外增益")

	var money_before := int(state.inventory.money)
	var exp_before := int(state.party_exp)
	var equips_before := int(state.inventory.equipment_count())
	var items_before := str(state.inventory.item_ids())
	for ally in battle.allies:
		ally.hp = 0
	var defeated: bool = battle.sim.winner() == "enemy"
	SfxScript.clear_log()
	battle._settle()

	check_true(defeated, "全队倒下时胜负判定是败北（实际 %s）" % battle.sim.winner())
	check_true(battle.settled, "败北也走结算（不会卡在半场：按钮与自动存档都要收尾）")
	check_true(SfxScript.has_played("defeat"), "败北结算会请求「败北」音效（07 §8.4）")
	var card: String = battle.result_label_text()
	check_true(card.contains("败北"), "结算卡片写清败北：%s" % card)
	check_false(card.contains("胜利"), "败北卡片不许出现「胜利」：%s" % card)
	check_eq(int(state.inventory.money), money_before, "败北不发铜钱")
	check_eq(int(state.party_exp), exp_before, "败北不给经验")
	check_eq(int(state.inventory.equipment_count()), equips_before, "败北不掉装备（没有奖励入账）")
	check_eq(str(state.inventory.item_ids()), items_before, "败北不掉材料／消耗品")
	check_eq(int(state.first_kill_count()), 0, "败北不记首杀（首杀只认打赢那一次）")
	for char_id: String in state.char_ids:
		check_true(
			int(state.current_hp_of(char_id)) < 0 and not state.is_wounded(char_id),
			"败北后气血回满（没有 char_hp 记录 = 满血；%s 读到 %d）" % [char_id, int(state.current_hp_of(char_id))]
		)
	if session_node != null:
		check_eq(session_node.active_field_buffs().size(), 0, "败北清掉全部战斗外增益（08 第 ④ 条）")
		# 这一局从没进过城镇 → 退回大地图（「最近到过的出生点或城镇」为空时的兜底）
		check_eq(str(session_node.last_shelter_scene), "", "这一局没到过城镇（用例前提）")
		check_eq(
			str(session_node.pending_return_scene), "res://scenes/world_run.tscn",
			"没到过城镇 → 败北退回大地图"
		)
	check_eq(str(battle.save_service().last_reason), "战斗结算", "败北也自动存档（设计 02：副本内自动存）")

	scene_tree.root.remove_child(battle)
	battle.free()


## 平局：回合打满（MAX_ROUNDS=50）时谁也没赢。这条分支以前**没有**——`_settle()` 只有
## 胜利／撤退／else，于是平局掉进 else、告诉玩家「败北」（假话），而 `last_battle.winner`
## 记的却是 "draw"。红名精英线现在常打到上限，这条挺现实。
## 练习战结算（设计 09 §3.3）：**没有掉落／铜钱／首杀／领悟**，经验按 `dummy_xp_cap_level`
## 封顶、熟练度按 `dummy_mastery_cap` 封顶——否则木桩就成了刷级/刷装备的永动机。
func _check_practice_settle(db) -> void:
	var state = solo_state(db)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/practice", 3)
	store.ensure_dir()
	state.slot = 1
	var char_id: String = state.char_ids[0]
	# 把角色抬到木桩的上限等级：这时练习战应当**一点经验都不给**
	state.char_levels[char_id] = PracticeServiceScript.cap_level(db)
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	battle.encounter.practice = true          # ← 这一局是练习战

	var money_before := int(state.inventory.money)
	var exp_before := int(state.party_exp)
	var equips_before := int(state.inventory.equipment_count())
	var items_before := str(state.inventory.item_ids())
	# 熟练度封顶：先把这一招练到练习战的上限（3 级），这一场再施放一次也不该涨到 4
	state.set_mastery(char_id, "sk_xuanwei_01", PracticeServiceScript.mastery_cap(db))
	var reference_skill: Resource = db.get_row("skill_active", "sk_xuanwei_01")
	battle.sim.act(battle.allies[0], reference_skill, battle.enemies[0])
	for enemy in battle.enemies:
		enemy.hp = 0
	var won: bool = battle.sim.winner() == "ally"
	battle._settle()

	check_true(won, "把木桩打空算我方胜利（实际 %s）" % battle.sim.winner())
	var card: String = battle.result_label_text()
	check_true(card.contains("练习战"), "结算卡片写清这是练习战：%s" % card)
	check_eq(int(state.inventory.money), money_before, "练习战不发铜钱")
	check_eq(int(state.party_exp), exp_before, "到顶之后练习战一点经验都不给")
	check_eq(int(state.inventory.equipment_count()), equips_before, "练习战不掉装备")
	check_eq(str(state.inventory.item_ids()), items_before, "练习战不掉材料")
	check_eq(int(state.first_kill_count()), 0, "练习战不记首杀")
	check_eq(
		state.mastery_of(char_id, "sk_xuanwei_01"), PracticeServiceScript.mastery_cap(db),
		"练习战里熟练度卡在上限（已经 3 级，再施放一次也不涨）"
	)
	var record: Dictionary = state.dungeon_record(battle.encounter.source_scene)
	check_false(Array(record.get("rooms", [])).has(str(battle.encounter.source_key)), "练习战不进副本完成度")
	scene_tree.root.remove_child(battle)
	battle.free()


func _check_draw_settle(db) -> void:
	var state = solo_state(db)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/draw", 3)
	store.ensure_dir()
	state.slot = 1
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	var money_before := int(state.inventory.money)
	var exp_before := int(state.party_exp)
	var items_before := str(state.inventory.item_ids())
	var equips_before := int(state.inventory.equipment_count())
	# 回合上限压到 1、双方血量拉高：谁也打不死谁，必然打满判平局
	battle.sim._options["max_rounds"] = 1
	for actor in battle.allies + battle.enemies:
		actor.hp = 9999
	battle.press_auto()
	var ticks := 0
	while not battle.sim.finished() and ticks < 80:
		battle._process(0.8)
		ticks += 1
	check_true(battle.sim.finished(), "有限次 tick 内打满回合上限（用了 %d 次）" % ticks)
	check_eq(battle.sim.winner(), "draw", "双方都活着打满上限 → 平局")
	var card: String = battle.result_label_text()
	check_true(card.contains("平局"), "结算卡片写「平局」：%s" % card)
	check_false(card.contains("败北"), "平局不许写成「败北」（玩家没输）：%s" % card)
	check_false(card.contains("胜利"), "平局也不是胜利：%s" % card)
	check_eq(int(state.inventory.money), money_before, "平局不发铜钱")
	check_eq(int(state.party_exp), exp_before, "平局不给经验")
	check_eq(int(state.inventory.equipment_count()), equips_before, "平局不掉装备")
	check_eq(str(state.inventory.item_ids()), items_before, "平局不掉材料")
	check_eq(int(state.first_kill_count()), 0, "平局不记首杀")
	check_eq(str(battle.save_service().last_reason), "战斗结算", "平局也自动存档（设计 02）")
	scene_tree.root.remove_child(battle)
	battle.free()


## 战斗界面的招式按钮：能用的可点、不能用的**置灰并在按钮上写原因**。
## 以前没有任何用例找过 `SkillButton_*`（grep 全项目为零）——把 `disabled` 写死成 false、
## 或把原因从按钮文案里删掉，自检都不会红（体检表 `skill_options` 里标了不可用，但那是另一层）。
## 这类「体检表说不行、按钮却照旧能点」的洞本项目出现过（逃跑／买入按钮都是「按钮态跟着原因走」）。
func _check_skill_buttons_ui(db) -> void:
	var state = solo_state(db)
	var char_id := str(state.char_ids[0])
	# 装配两部招式：起手式（qi_cost=0）＋进步（要 8 点内力），两种按钮态都能看到
	state.set_loadout(
		char_id,
		PackedStringArray(["sk_xuanwei_01", "sk_xuanwei_02"]),
		PackedStringArray(state.loadout_of(char_id)["passive"]),
	)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/skill_buttons", 3)
	store.ensure_dir()
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	battle.press_next_round()      # 推进到我方行动，招式行才会建出来
	var actor = battle.sim.current_actor()
	check_true(
		actor != null and actor.side == BattleActorScript.SIDE_ALLY,
		"轮到我方，招式行已建出来",
	)
	var qi_button: Button = battle.find_child("SkillButton_sk_xuanwei_02", true, false)
	var free_button: Button = battle.find_child("SkillButton_sk_xuanwei_01", true, false)
	check_not_null(qi_button, "耗内力那部招式有按钮")
	check_not_null(free_button, "不耗内力那部招式有按钮")
	if free_button != null:
		check_false(free_button.disabled, "不耗内力的招式可点：%s" % free_button.text)
	# 内力清零：耗内力的那部要置灰、并在按钮上写「内力不足」
	actor.qi = 0
	battle._refresh()
	var qi_button_zero: Button = battle.find_child("SkillButton_sk_xuanwei_02", true, false)
	check_not_null(qi_button_zero, "内力清零后按钮还在（不是整行消失）")
	if qi_button_zero != null:
		check_true(qi_button_zero.disabled, "内力不足时置灰")
		check_true(qi_button_zero.text.contains("内力不足"), "按钮上写明原因：%s" % qi_button_zero.text)
	var refused: Dictionary = battle.press_skill("sk_xuanwei_02")
	check_false(bool(refused["ok"]), "绕过置灰硬按也要被拒：%s" % str(refused.get("error", "")))
	scene_tree.root.remove_child(battle)
	battle.free()


## 增益招式在真实战斗界面上：按钮上写「增益」（不是「倍率 0.0」），按下去给自己上 buff、不打敌人。
## 用的就是发行数据里那条醉里乾坤·醉步（以前它连按钮都没有，见框架说明决策 221）。
func _check_support_skill_button(db) -> void:
	var state = solo_state(db)
	var char_id := str(state.char_ids[0])
	state.learn_skill(char_id, "sk_drunk_zuibu")
	state.set_loadout(
		char_id, PackedStringArray(["sk_xuanwei_01", "sk_drunk_zuibu"]),
		PackedStringArray(state.loadout_of(char_id)["passive"]),
	)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/support_skill", 3)
	store.ensure_dir()
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	battle.press_next_round()
	var actor = battle.sim.current_actor()
	check_true(actor != null and actor.side == BattleActorScript.SIDE_ALLY, "轮到我方")
	var button: Button = battle.find_child("SkillButton_sk_drunk_zuibu", true, false)
	check_not_null(button, "增益招式有按钮（以前没有）")
	if button != null:
		check_false(button.disabled, "能按（原因：%s）" % button.tooltip_text)
		check_true(button.text.contains("增益"), "按钮上写明是增益：%s" % button.text)
		check_false(button.text.contains("倍率 0.0"), "不再显示「倍率 0.0」：%s" % button.text)
		var enemy_hp := int(battle.enemies[0].hp)
		button.emit_signal("pressed")
		check_true(battle.allies[0].has_buff("buff_zuibu"), "按下后自己拿到忘忧")
		check_eq(int(battle.enemies[0].hp), enemy_hp, "没打敌人（增益招式不走伤害管线）")
	scene_tree.root.remove_child(battle)
	battle.free()


## 六指令在真实界面上的表现：普通攻击／内功／道具／主动防御四个按钮，加上增益减益面板。
## （招式与逃跑分别由 `_check_skill_buttons_ui` 与 `_check_flee_ui` 盖住。）
func _check_command_ui(db) -> void:
	var state = solo_state(db)
	var char_id := str(state.char_ids[0])
	# 装一部内功：内功指令才有东西可列（引气：运功＝它的内力上限加成翻倍）
	state.set_loadout(
		char_id, PackedStringArray(state.loadout_of(char_id)["active"]),
		PackedStringArray(["pf_xuanwei_01"]),
	)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/command_ui", 3)
	store.ensure_dir()
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	battle.press_next_round()
	var actor = battle.sim.current_actor()
	check_true(
		actor != null and actor.side == BattleActorScript.SIDE_ALLY,
		"轮到我方，六指令有行动对象",
	)

	for node_name: String in ["BasicAttackButton", "InnerButton", "ItemButton", "DefendButton"]:
		check_not_null(battle.find_child(node_name, true, false), "指令按钮 %s 在场景里" % node_name)

	# 道具指令：把 `use_context=battle` 的道具列出来，并如实写「效果列未配」（不假装能用）
	battle.press_item_list()
	var potion: Button = battle.find_child("ItemButton_item_potion_small", true, false)
	check_not_null(potion, "道具指令列出战斗道具（金创药）")
	if potion != null:
		check_true(potion.disabled, "效果列未配 → 道具置灰")
		check_true(potion.text.contains("效果"), "按钮上写明原因：%s" % potion.text)
	battle.press_item_list()      # 切回招式

	# 内功指令：切出已装内功的按钮 → 催动 → 拿到运功 buff
	battle.press_inner_list()
	var inner_skill: Button = battle.find_child("InnerButton_pf_xuanwei_01", true, false)
	check_not_null(inner_skill, "内功指令列出已装内功")
	if inner_skill != null:
		check_true(
			inner_skill.text.contains("内力消耗列未配"),
			"按钮上如实写「内力消耗列未配」：%s" % inner_skill.text
		)
		inner_skill.emit_signal("pressed")
	check_true(battle.allies[0].has_buff("buff_yunqi"), "催动内功后拿到运功 buff")
	var yunqi_chip: Button = battle.find_child("Buff_%s_buff_yunqi" % battle.allies[0].actor_id, true, false)
	check_not_null(yunqi_chip, "增益面板列出运功")
	if yunqi_chip != null:
		check_true(yunqi_chip.text.contains("增"), "增益用「增」字标极性：%s" % yunqi_chip.text)
		check_true(yunqi_chip.text.contains("▲"), "增益还带形状（色盲可辨，不只看红蓝）：%s" % yunqi_chip.text)
		check_true(yunqi_chip.text.contains("3"), "标出剩余回合数：%s" % yunqi_chip.text)

	# 主动防御：上 buff 后面板上能悬浮看到它给了什么
	var defend: Button = battle.find_child("DefendButton", true, false)
	check_not_null(defend, "有主动防御按钮")
	if defend != null:
		check_false(defend.disabled, "能按（原因：%s）" % defend.tooltip_text)
		defend.emit_signal("pressed")
	check_true(battle.allies[0].has_buff("buff_guard"), "防御按钮上了防御姿态")
	var chip: Button = battle.find_child("Buff_%s_buff_guard" % battle.allies[0].actor_id, true, false)
	check_not_null(chip, "增益减益面板出现防御姿态")
	if chip != null:
		check_true(chip.text.contains("防御姿态"), "条目写清名字：%s" % chip.text)
		check_true(
			chip.tooltip_text.contains("外防") or chip.tooltip_text.contains("减伤"),
			"悬浮说明给数值：%s" % chip.tooltip_text,
		)

	# 普通攻击：按下就掉血，而且不写进招式使用记录
	var victim = battle.enemies[0]
	var hp_before := int(victim.hp)
	var basic: Button = battle.find_child("BasicAttackButton", true, false)
	check_not_null(basic, "有普通攻击按钮")
	if basic != null:
		basic.emit_signal("pressed")
	check_lt(float(victim.hp), float(hp_before), "普通攻击按钮真的打出了伤害（%d → %d）" % [hp_before, victim.hp])
	check_true(
		battle.sim.skill_uses(str(battle.allies[0].actor_id)).is_empty(),
		"普通攻击不算武学（隐藏内容的招式使用判定不该被它污染）"
	)

	# 08 的 UI 要求还没钉住的几条：**合并展示**（异常状态也进同一条列表）、
	# 减益形状、层数 ×N、常驻 ∞、以及固定排序（减益在前）
	var label_ally := str(battle.allies[0].actor_id)
	var label_enemy := str(battle.enemies[0].actor_id)
	battle.allies[0].add_buff("buff_rusty_moon", "equip", "eq_sword_04")   # duration=0 → 常驻
	# 异常状态（DoT）：叠两层，才能验「可叠层的显示 ×N」
	battle.enemies[0].add_status("poison", "test", 5.0, 5, 3, "", true)
	battle.enemies[0].add_status("poison", "test", 5.0, 5, 3, "", true)
	battle._refresh()
	var rusty: Button = battle.find_child("Buff_%s_buff_rusty_moon" % label_ally, true, false)
	check_not_null(rusty, "常驻 buff 也进面板")
	if rusty != null:
		check_true(rusty.text.contains("∞"), "常驻显示 ∞（不是 0 回合）：%s" % rusty.text)
	var poison: Button = battle.find_child("Buff_%s_poison" % label_enemy, true, false)
	check_not_null(poison, "异常状态进同一条合并列表（status_effect 也在面板上）")
	if poison != null:
		check_true(poison.text.contains("▼") and poison.text.contains("减"), "减益用 ▼／减 标极性：%s" % poison.text)
		check_true(poison.text.contains("中毒"), "写中文名而不是 status_id：%s" % poison.text)
		check_true(poison.text.contains("×2"), "标出层数：%s" % poison.text)
		check_true(poison.text.contains("敌"), "合并列表里写清挂在谁身上：%s" % poison.text)
		check_true(poison.tooltip_text.contains("减益"), "悬浮说明给极性：%s" % poison.tooltip_text)
	# 排序：减益在前（玩家先看到危机），同类按剩余回合升序——这里比两个条目的先后
	var panel: HFlowContainer = battle.find_child("BuffPanel", true, false)
	check_not_null(panel, "增益减益面板是场景节点")
	if panel != null and poison != null and rusty != null:
		check_lt(
			float(poison.get_index()), float(rusty.get_index()),
			"减益排在增益前面（%d < %d）" % [poison.get_index(), rusty.get_index()]
		)
	scene_tree.root.remove_child(battle)
	battle.free()


## 预兆与拆招在真实界面上的表现：敌人卡片有预兆行与拆招按钮，按下去播报并改预兆行。
func _check_parry_ui(db) -> void:
	var state = solo_state(db)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/parry_ui", 3)
	store.ensure_dir()
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	battle.press_next_round()

	# 架势拉低一点才看得出「反涨」（满架势时被上限夹住 +0 是预期行为）
	battle.allies[0].poise = 10
	battle._refresh()
	var actor = battle.sim.current_actor()
	check_true(
		actor != null and actor.side == BattleActorScript.SIDE_ALLY,
		"第 1 回合预兆已给，轮到我方行动",
	)
	var enemy = battle.enemies[0]
	var intent: Label = battle.find_child("Intent_%s" % enemy.actor_id, true, false)
	check_not_null(intent, "敌人卡片上有预兆行")
	if intent != null:
		check_true(intent.text.contains("预兆："), "预兆行写清出什么招：%s" % intent.text)
	var parry: Button = battle.find_child("ParryButton_%s" % enemy.actor_id, true, false)
	check_not_null(parry, "敌人卡片上有拆招按钮")
	if parry != null:
		check_false(parry.disabled, "轮到我方时拆招按钮可用（原因：%s）" % parry.tooltip_text)
		# 能拆时把**效果**说清楚（三条都是已实现的行为，别让玩家靠试）
		check_true(parry.tooltip_text.contains("减半"), "能拆时说明「伤害减半」：%s" % parry.tooltip_text)
		check_true(parry.tooltip_text.contains("架势"), "能拆时说明「反涨架势」：%s" % parry.tooltip_text)
		var poise_before: int = battle.allies[0].poise
		parry.emit_signal("pressed")
		check_true(battle.status_text().contains("拆招"), "状态栏播报拆招：%s" % battle.status_text())
		check_gt(
			float(battle.allies[0].poise), float(poise_before),
			"拆招后我方架势反涨（%d → %d）" % [poise_before, battle.allies[0].poise],
		)
		var after: Label = battle.find_child("Intent_%s" % enemy.actor_id, true, false)
		check_not_null(after, "拆招后预兆行还在")
		if after != null:
			check_true(after.text.contains("已被拆招"), "预兆行标出已被拆招：%s" % after.text)
	scene_tree.root.remove_child(battle)
	battle.free()


## 首杀漏洞回归（真实战斗场景）：Boss 的首杀固定珍品武器只能领一次。
## 三种战斗资源都要在界面上看得见（设计 04：气血／内力／架势），且读数跟着单位走。
##
## 以前没有任何用例找过 `HpBar_*`／`QiBar_*`／`PoiseBar_*`——删掉或改名这两条资源条，
## 用例照样全绿（「三种战斗资源」那条在对照表里也标着 ✅）。
func _check_resource_bars(battle) -> void:
	for actor in [battle.allies[0], battle.enemies[0]]:
		var actor_id := str(actor.actor_id)
		check_not_null(battle.find_child("HpBar_%s" % actor_id, true, false), "有气血条（%s）" % actor_id)
		check_not_null(battle.find_child("QiBar_%s" % actor_id, true, false), "有内力条（%s）" % actor_id)
		check_not_null(battle.find_child("PoiseBar_%s" % actor_id, true, false), "有架势条（%s）" % actor_id)

	var ally = battle.allies[0]
	var ally_id := str(ally.actor_id)
	var qi: ProgressBar = battle.find_child("QiBar_%s" % ally_id, true, false)
	var poise: ProgressBar = battle.find_child("PoiseBar_%s" % ally_id, true, false)
	check_eq(int(qi.max_value), maxi(1, ally.max_qi()), "内力条上限 = 内力上限")
	check_eq(int(qi.value), int(ally.qi), "内力条读数 = 当前内力")
	check_eq(int(poise.max_value), maxi(1, ally.max_poise()), "架势条上限 = 架势上限")
	check_eq(int(poise.value), int(ally.poise), "架势条读数 = 当前架势")

	# 界面要真的跟着状态刷新：掉血之后重建的行里，气血条是新值（_refresh 是重建行，不是就地改）
	ally.take_damage(5)
	battle._refresh()
	var hp_after: ProgressBar = battle.find_child("HpBar_%s" % ally_id, true, false)
	check_not_null(hp_after, "刷新后气血条还在")
	check_eq(int(hp_after.value), int(ally.hp), "掉血后气血条跟着变（%d）" % ally.hp)
	check_eq(int(hp_after.max_value), maxi(1, ally.max_hp()), "气血条上限 = 气血上限")


## 自动战斗：按下去要能**自己打到底**。
##
## 曾经的实现是「`_process` 里反复 `press_next_round()`」，而 `press_next_round()` 只推进到
## 「轮到我方」就停（那是给手选招式用的）——所以自动战斗只有敌方回合会自动跑，
## 轮到玩家就永远卡住（`--battle-selftest` 的注释里写着「曾经把自检挂死」，但游戏里的
## 自动战斗按钮一直踩这个坑）。这条用例按固定种子把它钉死：有限次 tick 内必须打完并获胜。
func _check_auto_battle(db) -> void:
	var state = solo_state(db)
	# 等级拉满：这里验的是「自动战斗能不能自己推进」，不是平衡
	for char_id: String in state.char_ids:
		state.append_level(char_id, 19)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/auto_battle", 3)
	store.ensure_dir()
	state.slot = 1
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()

	battle.press_auto()
	check_true(battle.auto_running(), "按下自动战斗后进入自动状态")
	SfxScript.clear_log()
	var ticks := 0
	while not battle.sim.finished() and ticks < 200:
		# AUTO_INTERVAL = 0.8：一次 tick 至少够触发一次出手
		battle._process(0.8)
		ticks += 1
	check_true(battle.sim.finished(), "自动战斗能自己打到结束（用了 %d 次 tick）" % ticks)
	check_eq(battle.sim.winner(), "ally", "自动战斗打赢了弱队（现实现在 %s）" % battle.sim.winner())
	check_gt(float(battle.sim.rounds_played()), 1.0, "真的打了多个回合（%d）" % battle.sim.rounds_played())
	check_false(battle.auto_running(), "打完后自动状态自己关掉")
	check_true(str(battle.result_label_text()).contains("胜利"), "打完有结算卡片：%s" % battle.result_label_text())
	check_true(SfxScript.has_played("victory"), "胜利结算会请求「胜利」音效（07 §8.4）")
	scene_tree.root.remove_child(battle)
	battle.free()


## 以前首杀记录放在 GameSession 会话里，退出重进（= 读档）就能重复领；
## 这条走「真场景 → 真 _settle()」的路径，钉住「记录写进 GameState 并随存档走」。
func _check_first_kill_once(db) -> void:
	var state = solo_state(db)
	var store = SaveStoreScript.new("res://.logs/test_save_timing/first_kill", 3)
	store.ensure_dir()
	var before := _count_instances(state, "eq_sword_03")
	_settle_boss(db, state, store)
	var after_first := _count_instances(state, "eq_sword_03")
	check_eq(after_first - before, 1, "第一次击败 Boss 拿到首杀固定珍品武器")
	check_true(state.has_first_kill("drop_bd_boss"), "首杀记录写进 GameState（不再留在会话内存）")

	_settle_boss(db, state, store)
	check_eq(_count_instances(state, "eq_sword_03"), after_first, "同一存档再打一次不再给首杀武器")

	# 退出重进 = 真的落盘再读回，然后用读回来的状态再打一场
	var saved: Dictionary = store.save_slot(1, state)
	check_true(bool(saved["ok"]), "首杀后的存档能落盘：%s" % str(saved))
	var loaded: Dictionary = store.load_slot(1, db)
	var reloaded = loaded["state"]
	check_true(reloaded != null and reloaded.has_first_kill("drop_bd_boss"), "读档后首杀记录仍在")
	_settle_boss(db, reloaded, store)
	check_eq(_count_instances(reloaded, "eq_sword_03"), after_first, "退出重进后仍然不给首杀武器")


## 用黑风寨大寨主打一场（真场景、真结算）：直接把敌人打死再调 _settle()。
func _settle_boss(db, state, store) -> Dictionary:
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.encounter_override = _boss_encounter()
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	for enemy in battle.enemies:
		enemy.hp = 0
	battle._settle()
	var out := {
		"card": battle.result_label_text(),
		"log": battle._log.get_parsed_text(),
		"lines": battle.result_label_text().split("\n", false),
	}
	scene_tree.root.remove_child(battle)
	battle.free()
	return out


func _boss_encounter():
	var encounter = EncounterScript.new()
	encounter.team_name = "黑风寨大寨主"
	encounter.members = "en_bd_boss:1"
	encounter.difficulty_id = "normal"
	encounter.source_scene = "dungeon"
	encounter.source_key = "test_boss_room"
	return encounter


## 真打两场紫名 Boss（大寨主 → 醉刀客）→ 都写进副本记录 →「绝境」难度解锁。
##
## 这条链以前只有**白盒**覆盖：`test_world_map` 与 `test_dungeon` 都是直接
## `state.record_dungeon(scene, "bosses", "en_hidden_drunk")` 把 Boss 塞进去，
## 而「打赢一场隐藏 Boss 会不会被记成 Boss」这一段没人验——它恰恰是绝境解锁的唯一入口。
## 醉刀客那场按 `local_map_controller._trigger_item` 的真实形状建 encounter（source_scene=场景、
## source_key=trig_wine、单只队伍），所以这条用例走的是和游戏里一样的结算路径。
func _check_boss_kills_unlock_nightmare(db, store) -> void:
	var state = solo_state(db)
	state.slot = 1
	var world_map = WorldMapServiceScript.new(db, state)
	check_false(bool(world_map.can_switch_difficulty("nightmare")["ok"]), "新档：绝境锁着")
	check_false(bool(world_map.can_switch_difficulty("hard")["ok"]), "新档：困难也锁着（要通关第一章）")

	_settle_named_encounter(db, state, store, "黑风寨大寨主", "en_bd_boss:1", "hf3_boss")
	var after_boss: Array = Array(state.dungeon_record("scene_heifengzhai").get("bosses", []))
	check_true(after_boss.has("en_bd_boss"), "打赢大寨主被记成 Boss：%s" % str(after_boss))
	check_true(bool(world_map.can_switch_difficulty("hard")["ok"]), "通关第一章后困难解锁")
	check_false(bool(world_map.can_switch_difficulty("nightmare")["ok"]), "只有大寨主时绝境还锁着")

	_settle_named_encounter(db, state, store, "醉刀客", "en_hidden_drunk:1", "trig_wine")
	var after_all: Array = Array(state.dungeon_record("scene_heifengzhai").get("bosses", []))
	check_true(after_all.has("en_hidden_drunk"), "打赢醉刀客被记成 Boss：%s" % str(after_all))
	check_true(bool(world_map.can_switch_difficulty("nightmare")["ok"]), "两个 Boss 都真打完：绝境解锁")


## 建一场「小地图里打完的」战斗、把敌人打死、走真结算。成员串与 `enemy_team` 同格式。
func _settle_named_encounter(db, state, store, team_name: String, members: String, source_key: String) -> void:
	var encounter = EncounterScript.new()
	encounter.team_name = team_name
	encounter.members = members
	encounter.difficulty_id = str(state.difficulty_id)
	encounter.source_scene = "scene_heifengzhai"
	encounter.source_key = source_key
	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	battle.encounter_override = encounter
	battle.save_store_override = store
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	for enemy in battle.enemies:
		enemy.hp = 0
	battle._settle()
	scene_tree.root.remove_child(battle)
	battle.free()


func _count_instances(state, base_id: String) -> int:
	var total := 0
	for instance_id: String in state.inventory.equipment_ids():
		if state.inventory.base_of(instance_id) == base_id:
			total += 1
	return total


func state_mastery_of(battle, char_id: String, skill_id: String) -> int:
	return int(battle.state.mastery_of(char_id, skill_id))


func state_mastery_ids(battle) -> Array:
	return battle.state.masteries_of(str(battle.allies[0].actor_id))
