## 变异探针（只读·手动工具，**不进验收**）：把数据改坏，看**构建期校验器**抓不抓得住。
##
## 为什么需要它：`table_validator.gd` 里几十条规则是几十轮里一条条加出来的，而「规则写了但抓不住」
## 和「规则没问题」在验收里长得一模一样（都是绿）。前两轮靠临时脚本扫，扫出过 rarities 区间、
## 经验曲线下降、兜底常量分家等真洞（见 docs/dev/框架说明.md 决策 152／154／155）。
## 这个工具把那套临时脚本固定下来：`tools\run_mutation_sweep.bat` 一起跑它和 PS1 那一半。
##
## 做法：`TableDb` 载入真表 → **在内存里复制一份改坏**（**绝不碰 CSV／.tres**）→ 调 `validate()`
## → 看有没有报出这条错。报错文案对不上也算「没抓住」，并原样打出来便于判断是规则漏了还是 case 写错了。
##
## 用法：
##   Godot --headless --path . --log-file .logs\mutation.log --script res://tools/analysis_mutations.gd
## 退出码：0 = 每个 case 都被抓住；1 = 有 case 没被抓住（或 case 本身失效）。
extends SceneTree

const TableDbScript := preload("res://src/core/table_db.gd")
const TableValidatorScript := preload("res://src/core/table_validator.gd")
## 用例里的列名写成 **CSV 里的列名**（和策划看到的表一致），由 registry 映射到行类属性——
## 有些列两者不同名（`enemy_base.csv` 的 `exp` 在行类里叫 `exp_reward`），不映射就会误报 [INVALID]。
const RegistryScript := preload("res://src/data/table_registry.gd")

## name／table／id／column／value／expect（expect 是报错里必须出现的片段；留空＝只要这张表报错就算抓住）。
## 想一次改多列（比如「把一件良品装的加成全清零」）就把 `column` 换成 `columns`（字符串数组，一起赋值）。
## 注意：case 写错时工具会报 `[INVALID]`，不会装作"抓住了"——这是刻意的（量不出来 ≠ 量过了）。
const CASES := [
	{"name": "装备等级门槛为负", "table": "equip_base", "id": "eq_sword_01", "column": "level_req", "value": -1, "expect": "level_req"},
	{"name": "稀有度非法", "table": "equip_base", "id": "eq_sword_01", "column": "rarity", "value": "legendary", "expect": "rarity"},
	{"name": "槽位非法", "table": "equip_base", "id": "eq_sword_01", "column": "slot", "value": "bogus_slot", "expect": "slot"},
	{"name": "良品装备一条加成都没有", "table": "equip_base", "id": "eq_ring_03", "expect": "一条加成都没有",
		"columns": ["attr_str", "attr_con", "attr_agi", "attr_int", "attr_luk", "bonus_atk_phys", "bonus_atk_qi",
			"bonus_def_phys", "bonus_hp_max", "bonus_speed", "bonus_crit_rate", "bonus_pen_rate",
			"bonus_block_rate", "bonus_block_reduction", "bonus_dmg_reduction"], "value": 0},
	{"name": "词条条数区间倒挂", "table": "rarity_def", "id": "common", "column": "affix_min", "value": 9, "expect": "区间"},
	{"name": "经验门槛突然变便宜", "table": "level_growth", "id": "6", "column": "exp_to_next", "value": 1, "expect": "exp_to_next"},
	{"name": "非封顶级经验门槛为 0", "table": "level_growth", "id": "4", "column": "exp_to_next", "value": 0, "expect": "封顶"},
	{"name": "加点低于上一级", "table": "level_growth", "id": "6", "column": "upgrade_points", "value": 1, "expect": "upgrade_points"},
	{"name": "招式段数为 0", "table": "skill_active", "id": "sk_xuanwei_01", "column": "hit_count", "value": 0, "expect": "hit"},
	{"name": "物品堆叠上限为 0", "table": "item_base", "id": "item_iron", "column": "stack_max", "value": 0, "expect": "stack_max"},
	{"name": "物品类型非法", "table": "item_base", "id": "item_iron", "column": "item_type", "value": "bogus", "expect": "item_type"},
	{"name": "管线比率没有上限", "table": "stat_def", "id": "pen_rate", "column": "max_value", "value": 0, "expect": "硬上限"},
	{"name": "内伤抗性没写上限", "table": "stat_def", "id": "res_internal", "column": "max_value", "value": 0, "expect": "硬上限"},
	{"name": "槽位可佩戴数量为 0", "table": "equip_slot_def", "id": "weapon", "column": "max_equip", "value": 0, "expect": "max_equip"},
	{"name": "异常状态规则写错", "table": "status_effect", "id": "poison", "column": "extra_rule", "value": "rule_bogus", "expect": "extra_rule"},
	{"name": "掉落数量区间倒挂", "table": "drop_table", "id": "dr_boss_01", "column": "qty_min", "value": 9999, "expect": "区间"},
	{"name": "掉落指向不存在的装备", "table": "drop_table", "id": "dr_boss_01", "column": "item_id", "value": "eq_bogus", "expect": "eq_bogus"},
	{"name": "词条目标不存在", "table": "affix_pool", "id": "af_str_01", "column": "target", "value": "attr:bogus", "expect": "bogus"},
	{"name": "副本楼层为 0", "table": "dungeon_room", "id": "hf1_yard", "column": "floor", "value": 0, "expect": "floor"},
	{"name": "判定来源缺前缀", "table": "event_check", "id": "ev_gamble", "column": "check_source", "value": "luk", "expect": "check_source"},
	{"name": "敌人血量下限", "table": "enemy_base", "id": "en_wolf", "column": "hp_base", "value": 0, "expect": "hp_base"},
	# 设计 10 §二 连带规则①的**容量**那半边（2026-10-04 补的规则）：屠夫身上那部内功占格 2 → 3，
	# 3＋1＝4 格 > 它 8 级／根骨 8 的容量 3——构建期必须点名它
	{"name": "敌人内功超编（占格被写大）", "table": "skill_passive", "id": "pf_chensha_02", "column": "slot_cost", "value": 3, "expect": "内功超编"},
	# 装备图标列（A14，0.31.1）：`icon` 必须等于自己的 equip_id——写错不会报错，只会画出别人家的图标
	{"name": "装备图标列指向别人", "table": "equip_base", "id": "eq_sword_02", "column": "icon", "value": "eq_sword_01", "expect": "icon"},
	{"name": "内功被动加成为 0", "table": "skill_passive_stat", "id": "pf_xuanwei_01|stat:qi_max", "column": "value", "value": 0, "expect": "白填"},
	{"name": "困难档比普通还弱", "table": "difficulty_config", "id": "hard", "column": "enemy_atk_mul", "value": 0.5, "expect": "难度越高数值反而越小"},
	{"name": "难度解锁短语写错", "table": "difficulty_config", "id": "nightmare", "column": "unlock_condition", "value": "通关第一章并击败醉刀客（误）", "expect": "永远解锁不了"},
	{"name": "成长常数被改名（代码依赖它）", "table": "growth_const", "id": "mastery_max", "column": "const_id", "value": "mastery_max_typo", "expect": "缺少代码依赖的常数"},
	{"name": "起始装备与模板武器不符", "table": "character_base", "id": "scholar_fallen", "column": "start_equip_ids", "value": "eq_fist_01", "expect": "开局装不上"},
	{"name": "打坐费用底数为负", "table": "growth_const", "id": "cultivate_cost_growth", "column": "value", "value": -1.6, "expect": "小于可用下限"},
	{"name": "招式槽除数被写成 0", "table": "growth_const", "id": "active_slot_lv_div", "column": "value", "value": 0, "expect": "小于可用下限"},
	# —— 下面这批是「按序号挑行」的广覆盖用例：只要求这张表报错，用来找**还不存在的规则** ——
	{"name": "招式内力消耗为负", "table": "skill_active", "index": 1, "column": "qi_cost", "value": -5, "expect": "qi_cost"},
	{"name": "招式冷却为负", "table": "skill_active", "index": 1, "column": "cooldown", "value": -3, "expect": "cooldown"},
	{"name": "内功占格为 0", "table": "skill_passive", "index": 1, "column": "slot_cost", "value": 0, "expect": "slot_cost"},
	{"name": "内功占格超过 3", "table": "skill_passive", "index": 1, "column": "slot_cost", "value": 9, "expect": "slot_cost"},
	{"name": "星级成长系数为负", "table": "skill_star_def", "index": 1, "column": "mastery_gain", "value": -0.5, "expect": "mastery_gain"},
	{"name": "状态层数上限为 0", "table": "status_effect", "index": 1, "column": "max_stack", "value": 0, "expect": "max_stack"},
	{"name": "状态持续 0 回合", "table": "status_effect", "index": 1, "column": "duration", "value": 0, "expect": "duration"},
	{"name": "状态触发率超过 1", "table": "status_effect", "index": 1, "column": "base_chance", "value": 1.5, "expect": "base_chance"},
	{"name": "槽位排序为 0", "table": "equip_slot_def", "index": 1, "column": "sort_order", "value": 0, "expect": "sort_order"},
	{"name": "词条权重为 0", "table": "affix_pool", "index": 1, "column": "weight", "value": 0, "expect": "weight"},
	{"name": "物品售价为负", "table": "item_base", "index": 1, "column": "sell_price", "value": -5, "expect": "sell_price"},
	{"name": "货架限量为负", "table": "shop_stock", "index": 1, "column": "stock_limit", "value": -1, "expect": "stock_limit"},
	{"name": "敌人铜钱为负", "table": "enemy_base", "index": 1, "column": "money", "value": -10, "expect": "money"},
	# —— 增益与套装（08）：这 16 条盯的是 2026-10-03 才补进**构建期**那一侧的规则（决策 218）——
	# 补之前它们只有 PS1 会报，也就是「跑验收才发现」；现在每一条都在构建期当场报出来。
	{"name": "buff 作用域非法", "table": "buff_def", "id": "buff_guard", "column": "scope", "value": "bogus", "expect": "scope"},
	{"name": "buff 叠加规则非法", "table": "buff_def", "id": "buff_guard", "column": "stack_rule", "value": "bogus", "expect": "stack_rule"},
	{"name": "buff 效果类型非法", "table": "buff_def", "id": "buff_guard", "column": "effect_kind", "value": "bogus", "expect": "effect_kind"},
	{"name": "数值型 buff 一条加成都没有", "table": "buff_def", "id": "buff_yunqi", "column": "effect_kind", "value": "stat", "expect": "一条加成都没有"},
	{"name": "可叠层 buff 的 max_stack 只有 1", "table": "buff_def", "id": "buff_poison_amp", "column": "max_stack", "value": 1, "expect": "max_stack"},
	{"name": "buff 持续写成 -2", "table": "buff_def", "id": "buff_guard", "column": "duration", "value": -2, "expect": "duration"},
	{"name": "buff 修正缺 attr:/stat: 前缀", "table": "buff_stat", "id": "buff_guard|stat:dmg_reduction", "column": "target", "value": "dmg_reduction", "expect": "前缀"},
	# expect 要写**我自己那条规则的话**：改 target 会连带改掉复合主键，主键一致性检查也会报一条含有
	# `stat:bogus_stat` 的错——只写 `bogus_stat` 的话会被那条蒙混过关（第一版就是这样，读打印才发现）
	{"name": "buff 修正指向不存在的派生数值", "table": "buff_stat", "id": "buff_guard|stat:dmg_reduction", "column": "target", "value": "stat:bogus_stat", "expect": "引用的派生数值不存在"},
	{"name": "buff 发放来源非法", "table": "buff_grant", "id": "gr_pf_xuanwei_01", "column": "source_type", "value": "bogus", "expect": "source_type"},
	{"name": "buff 发放的来源 id 不存在", "table": "buff_grant", "id": "gr_pf_xuanwei_01", "column": "source_id", "value": "pf_bogus", "expect": "pf_bogus"},
	{"name": "某部内功没有 on_cast（内功指令点它没反应）", "table": "buff_grant", "id": "gr_pf_xuanwei_05", "column": "source_id", "value": "pf_xuanwei_04", "expect": "pf_xuanwei_05"},
	{"name": "套装类别非法", "table": "set_def", "id": "set_heifeng", "column": "set_kind", "value": "bogus", "expect": "set_kind"},
	{"name": "套装成员类型不符", "table": "set_member", "id": "set_heifeng|eq_sword_03", "column": "member_id", "value": "sk_xuanwei_01", "expect": "不是合法的 equip"},
	{"name": "套装档位写成 0", "table": "set_bonus", "id": "set_heifeng|2", "column": "required_count", "value": 0, "expect": "正整数"},
	{"name": "套装档位永远凑不齐", "table": "set_bonus", "id": "set_xuanwei_sword|3", "column": "required_count", "value": 99, "expect": "凑不齐"},
	{"name": "全局开关取值不是 0/1", "table": "feature_toggle", "id": "overworld_roaming_enemy", "column": "value", "value": 2, "expect": "value"},
	# —— 非战斗技能（01 的「每个模板给出全部非战斗技能初始等级」，2026-10-03 才补进构建期）——
	{"name": "角色漏配一项非战斗技能", "table": "character_base_skill", "id": "scholar_fallen|wenxue", "column": "skill_id", "value": "qimen", "expect": "缺少非战斗技能"},
	{"name": "非战斗技能等级超过上限", "table": "character_base_skill", "id": "scholar_fallen|wenxue", "column": "level", "value": 99, "expect": "超过这部非战斗技能的上限"},
	# —— buff_grant 的「这一行会不会触发」：写完时机的表必须真的被代码读（2026-10-03 补的规则）——
	{"name": "内功挂了一个没人读的时机", "table": "buff_grant", "id": "gr_pf_xuanwei_01", "column": "trigger", "value": "on_hit", "expect": "永远不会触发"},
	# 单列改就够：把来源改成 `event`，而 `event` 这一档代码里没有任何触发点（组合表里没有它）
	{"name": "event 来源的 buff（代码里还没有这个时机）", "table": "buff_grant", "id": "gr_pf_xuanwei_01", "column": "source_type", "value": "event", "expect": "永远不会触发"},
	# 招式配了代码不支持的伤害类型 → 那招永远没有按钮（2026-10-03 的 DoT 事故就是这一类的反面：
	# 当时是「代码过滤把 dot 当不支持」，而数据侧完全合法——这条规则能把它当场抓出来）
	{"name": "招式配了代码不支持的伤害类型", "table": "skill_active", "id": "sk_xuanwei_01", "column": "damage_type", "value": "dmg_reflect", "expect": "永远没有按钮"},
]
# 探针里**有意没有**的几类（写清楚，免得下轮又当洞，见框架说明决策 157）：
#   · `skill_active.power_ratio = 0`、`difficulty_drop_rate.rate_multiplier = 0`：由 **PS1 那一半**管
#     （5.21 招式效果警告 / 难度倍率检查），所以它们的 case 在 analysis_mutations.ps1 里。
#   · `enemy_base.exp = 0`、`roaming_spawn.alert_radius = 0`：0 是否能算"合法的无害敌人/被动位点"
#     属于设计口径，开发侧不编规则，只登记。
#   · `rarity_def.drop_weight = 0`：这一列**运行时没人读**（已登记在待设计清单里），改动没有效果。
#   · `growth_const.value = 0`：不同常数 0 的含义不同，写不出通用规则（缺常数由另一条规则管）。


func _initialize() -> void:
	print("=== 变异探针：构建期校验器 ===")
	var missed: Array[String] = []
	var invalid: Array[String] = []
	for case: Dictionary in CASES:
		var verdict := _run_case(case)
		if verdict == "invalid":
			invalid.append(str(case["name"]))
		elif verdict == "missed":
			missed.append(str(case["name"]))
	print("MUTATIONS: %d 个 case，漏 %d，case 失效 %d" % [CASES.size(), missed.size(), invalid.size()])
	if not missed.is_empty():
		print("MUTATIONS MISSED: ", ", ".join(missed))
	if not invalid.is_empty():
		print("MUTATIONS INVALID: ", ", ".join(invalid))
	# ASCII 判定行给 findstr 用（中文在 cmd 控制台代码页下匹配不到）
	print("MUTATIONS: OK" if (missed.is_empty() and invalid.is_empty()) else "MUTATIONS: FAILED")
	quit(1 if (not missed.is_empty() or not invalid.is_empty()) else 0)


## 返回 "caught" / "missed" / "invalid"。
func _run_case(case: Dictionary) -> String:
	var db = TableDbScript.new()
	db.load_all()
	if not db.errors.is_empty():
		print("  [SKIP] 表加载不了：%s" % db.errors)
		return "invalid"
	var table_name := str(case["table"])
	# 用例必须写明"该报哪条错"：只要求"这张表报错"的话，**别的**错也能让它蒙混过关
	# （2026-10-03 把 13 个空 expect 的用例逐个补上，并在这里强制）。
	if str(case.get("expect", "")).is_empty():
		print("  [INVALID] 用例 %s 没写 expect（该报哪条错）" % case["name"])
		return "invalid"
	var resource: Resource = db.tables.get(table_name)
	if resource == null:
		print("  [INVALID] 没有表 %s" % table_name)
		return "invalid"
	var copy: Resource = resource.duplicate(true)
	# 行可以按 id 找（稳），也可以按序号找（省事，用于 id 是复合主键、懒得拼的表）
	var target: Resource = null
	if case.has("index"):
		var index := int(case["index"])
		if index >= 0 and index < copy.rows.size():
			target = copy.rows[index]
	else:
		target = _find_row(copy, str(case["id"]))
	if target == null:
		print("  [INVALID] %s 里找不到目标行（id=%s index=%s）" % [table_name, str(case.get("id", "")), str(case.get("index", ""))])
		return "invalid"
	var columns: Array = case.get("columns", [str(case.get("column", ""))])
	var mapped: Array = []
	for column: String in columns:
		var property := RegistryScript.property_of(table_name, column)
		if not _has_property(target, property):
			print("  [INVALID] %s 没有列 %s" % [table_name, column])
			return "invalid"
		mapped.append(property)
		target.set(property, case["value"])
	var column_list := ", ".join(PackedStringArray(columns))
	db.tables[table_name] = copy

	var expect := str(case.get("expect", ""))
	var errors := TableValidatorScript.validate(db)
	var caught := false
	var matched := ""
	for message: String in errors:
		if expect.is_empty():
			if message.contains(table_name) and matched.is_empty():
				caught = true
				matched = message
		elif message.contains(expect) and matched.is_empty():
			caught = true
			matched = message
	if caught:
		# 把**匹配到的那条错**打出来：用途是核对"抓对了没有"。
		# `expect` 留空的用例只要求"这张表报错"，可能被**别的**错蒙混过关——
		# 打印出来才能一眼看出是不是同一条（2026-10-03 起，空 expect 的用例要逐个补上）。
		print("  [OK]   %s ← %s" % [case["name"], matched.substr(0, 60)])
		return "caught"
	print("  *** 没抓住 *** %s（%s：%s，总错误 %d）" % [case["name"], table_name, column_list, errors.size()])
	return "missed"


## 主键列拼出的 id 直接存在行上（`row.id`），不用再猜它由哪几列拼成。
func _find_row(table_resource: Resource, row_id: String) -> Resource:
	for row: Resource in table_resource.rows:
		if str(row.id) == row_id:
			return row
	return null


func _has_property(row: Resource, column: String) -> bool:
	for property: Dictionary in row.get_property_list():
		if str(property["name"]) == column:
			return true
	return false
