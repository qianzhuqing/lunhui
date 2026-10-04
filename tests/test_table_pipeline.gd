## 数据管线：生成资源与 CSV 一致、主键/引用校验能抓错。
extends "res://tests/test_case.gd"

const Registry := preload("res://src/data/table_registry.gd")
const TableValidatorScript := preload("res://src/core/table_validator.gd")


func suite_name() -> String:
	return "数据管线与校验"


func run() -> void:
	var db = get_db()
	check_true(db.errors.is_empty(), "data/generated 应能全部加载：%s" % db.errors)
	check_eq(db.tables.size(), Registry.TABLES.size(), "表数量与注册表一致")
	check_eq(TableValidatorScript.validate(db).size(), 0, "真实配置表应零校验错误")

	_check_row_counts(db)
	_check_spot_values(db)
	_check_registry_matches_csv_dir()
	_check_broken_reference_is_caught(db)
	_check_shop_and_slot_negatives()
	_check_stale_index_is_caught()
	_check_growth_and_rarity_negatives()


## `data/tables/` 里的每张 CSV 都必须登记进 `table_registry.TABLES`，反过来也必须存在。
##
## 由来（2026-10-03）：`tools\validate_tables.ps1` 第 9 节**确实**比对了「CSV 文件 ↔ 注册表」，
## 但**只算 Warning**——而验收里那一串警告被明确标成"给策划的清单、不挡验收"。
## 于是留了这么一条静默路径：策划按 06 把新表（`dialogue_tree.csv` 这类"第一章制作期"的表）
## 放进 `data/tables/`，**验收全绿、游戏运行期却根本不加载它**（`TableDb.load_all` 只认注册表），
## 要等到某个功能"怎么不生效"才会被翻出来——**开发侧的账不该混在给策划的警告里**，所以升成硬门限。
##
## 反方向（注册了却没有 CSV）本来就会让构建期直接失败，这里也一并钉住，报错更早更直白。
## 只扫一层：`data/tables/` 下出现子目录会当场点名——藏在子目录里的表同样读不到，
## 要么移平，要么把这条门限改成递归（**别让它悄悄漏**）。
func _check_registry_matches_csv_dir() -> void:
	var dir: String = Registry.TABLES_DIR
	var registered: Dictionary = {}
	for table_name: String in Registry.TABLES:
		registered[table_name] = true
	var on_disk := PackedStringArray()
	for file_name: String in DirAccess.get_files_at(dir):
		if not file_name.to_lower().ends_with(".csv"):
			continue
		on_disk.append(file_name.get_basename())
	check_gt(float(on_disk.size()), 20.0, "data/tables 下扫到足够多的 CSV（%d 张）" % on_disk.size())
	for table_name: String in on_disk:
		check_true(
			registered.has(table_name),
			"%s/%s.csv 没登记进 table_registry.TABLES：构建会跳过它，游戏运行期读不到这张表"
				% [dir, table_name]
		)
	for table_name: String in registered.keys():
		check_true(
			on_disk.has(table_name),
			"table_registry 登记了 '%s'，但 %s/%s.csv 不存在（构建期会直接失败）"
				% [table_name, dir, table_name]
		)
	for sub: String in DirAccess.get_directories_at(dir):
		check_true(
			false,
			"%s/%s 是子目录：这条门限只扫一层，藏在子目录里的表会被漏掉（移平，或把门限改成递归）"
				% [dir, sub]
		)


## 生成资源的行数必须与 CSV 行数一致（表头一行不算）。
func _check_row_counts(db) -> void:
	for table_name: String in Registry.TABLES:
		var csv_path := "%s/%s.csv" % [Registry.TABLES_DIR, table_name]
		var file := FileAccess.open(csv_path, FileAccess.READ)
		if file == null:
			fail("读不到 CSV %s" % csv_path)
			continue
		var lines := 0
		while not file.eof_reached():
			var line := file.get_csv_line()
			if line.size() == 1 and line[0].strip_edges().is_empty():
				continue
			lines += 1
		file.close()
		var expected := lines - 1
		check_eq(db.rows(table_name).size(), maxi(expected, 0), "%s 行数与 CSV 一致" % table_name)


func _check_spot_values(db) -> void:
	var attrs: Dictionary = db.column_values("attribute_def", "attr_id")
	for attr_id in ["str", "con", "agi", "int", "luk"]:
		check_true(attrs.has(attr_id), "attribute_def 含 %s" % attr_id)

	# BOM 剥离 + 列别名：attr_to_stat 的 CSV 列 id 映射到行类 row_id
	var attr_row = db.get_row("attr_to_stat", 1001)
	check_not_null(attr_row, "attr_to_stat 主键 1001 存在")
	if attr_row != null:
		check_eq(attr_row.row_id, 1001, "列别名 id → row_id 生效")
		check_eq(attr_row.attr_id, "str", "1001 的来源属性是 str")

	# 复合主键：element_counter / difficulty_drop_rate
	var counter = db.get_row("element_counter", "external|internal")
	check_not_null(counter, "element_counter 复合主键 external|internal 存在")
	if counter != null:
		check_float(counter.multiplier, 0.8, "外功打内功被克 −20%")
	var legend_nightmare = db.get_row("difficulty_drop_rate", "nightmare|legend")
	check_not_null(legend_nightmare, "difficulty_drop_rate 复合主键 nightmare|legend 存在")
	if legend_nightmare != null:
		check_float(legend_nightmare.rate_multiplier, 6.67, "绝境下传世掉率 ×6.67")

	# 列别名：enemy_base.exp → exp_reward
	var wolf = db.get_row("enemy_base", "en_wolf")
	check_not_null(wolf, "enemy_base 主键 en_wolf 存在")
	if wolf != null:
		check_eq(wolf.exp_reward, 12, "列别名 exp → exp_reward 生效")
		check_eq(wolf.drop_group, "drop_wolf", "野狼引用掉落组 drop_wolf")

	# 多值列解析
	var patrol = db.get_row("enemy_team", "team_bandit_patrol")
	check_not_null(patrol, "enemy_team 主键 team_bandit_patrol 存在")
	if patrol != null:
		var members: Array = patrol.parsed_members()
		check_eq(members.size(), 2, "山贼巡山队有两个成员条目")
		check_eq(int(members[0]["count"]), 2, "第一个成员数量为 2")

	# 0.4.0 新增：角色模板、武器类型、战斗常数、事件判定、非战斗技能
	check_eq(db.rows("event_skill_def").size(), 5, "五项目非战斗技能")
	var scholar = db.get_row("character_base", "scholar_fallen")
	check_not_null(scholar, "角色模板 scholar_fallen 存在")
	if scholar != null:
		check_eq(scholar.attr_sum(), scholar.attr_total, "五维之和等于 attr_total")
		check_eq(scholar.weapon_type, "sword", "模板用剑")
		check_gt(float(scholar.equip_ids().size()), 0.0, "模板带初始装备")
	var blade = db.get_row("weapon_type_def", "blade")
	check_not_null(blade, "武器类型 blade 存在")
	if blade != null:
		check_eq(str(blade.focus_attrs()), str(PackedStringArray(["str"])), "刀主堆力量")
	var def_const = db.get_row("combat_const", "def_const")
	check_not_null(def_const, "combat_const 里有 def_const")
	if def_const != null:
		check_float(def_const.value, 60.0, "防御常数 60")
	var event_row = db.get_row("event_check", "ev_shed_trap")
	check_not_null(event_row, "事件判定 ev_shed_trap 存在")
	if event_row != null:
		var parsed_source: Dictionary = event_row.parsed_source()
		check_eq(parsed_source["kind"], "skill", "事件判定来源是技能")
		check_eq(parsed_source["target_id"], "qimen", "柴房机关用奇门判定")
	# 每个角色模板给 5 项非战斗技能的初始等级：行数随**模板数**走，不写死 5
	# （0.4.0 起模板 1 行、设计要 3–4 行；写死就等于把设计状态钉进用例，见框架说明决策 161）。
	# `db` 在这里是无类型的参数，`db.rows(...).size()` 推断不出类型 → 用 int() 包一层
	var skill_row_count := int(db.rows("character_base_skill").size())
	var template_count := int(db.rows("character_base").size())
	check_eq(skill_row_count, template_count * 5, "每个角色模板 5 项非战斗技能（%d 模板 × 5 = %d，实际 %d）"
		% [template_count, template_count * 5, skill_row_count])

	# 0.5.0：装备槽位与商店
	check_eq(db.rows("equip_slot_def").size(), 7, "七类装备槽位")
	var ring = db.get_row("equip_slot_def", "ring")
	check_not_null(ring, "戒指槽存在")
	if ring != null:
		check_eq(int(ring.max_equip), 2, "戒指可戴两枚")
	var weapon_slot = db.get_row("equip_slot_def", "weapon")
	check_not_null(weapon_slot, "武器槽存在")
	if weapon_slot != null:
		check_eq(weapon_slot.allowed_weapon_types().size(), 4, "武器槽允许四种武器类型")
	check_true(db.get_row("weapon_type_def", "staff") == null, "棍已从武器类型里移除")
	check_true(db.get_row("equip_base", "eq_head_01").slot == "shoulder", "前代寨主遗物改挂肩部")
	check_eq(db.rows("shop_stock").size(), 26, "货架 26 行（0.23.0 毒酒 +1；0.28.0 行商货架 +4；2026-10-04 秘籍 +4）")
	var grocery = db.get_row("building_def", "bld_grocery")
	check_not_null(grocery, "杂货铺存在")
	if grocery != null:
		check_eq(grocery.stock_group, "shop_grocery", "杂货铺挂 shop_grocery 货架")
	var med = db.get_row("item_base", "item_med_01")
	check_not_null(med, "非战斗回血道具存在")
	if med != null:
		check_true(med.is_field_use(), "草药汤是非战斗回血")
	var potion = db.get_row("item_base", "item_potion_small")
	check_not_null(potion, "战斗回血道具存在")
	if potion != null:
		check_true(potion.is_battle_use(), "金创药是战斗中回血")


## 校验器规则表的自检（见调用处注释）
func _check_validator_rule_targets(db) -> void:
	if not db.load_all():
		fail("规则自检：生成表加载失败")
		return
	var problems: Array = []
	for rule: Dictionary in TableValidatorScript.REFERENCES:
		var table := str(rule["table"])
		_check_rule_column(db, table, str(rule["column"]), problems, "REFERENCES")
		var target := str(rule.get("target", ""))
		if target.is_empty():
			continue
		var target_column := str(rule.get("target_column", ""))
		if target_column.is_empty():
			# 没写目标列时，校验器按目标表的**第一个主键列**取值（见 `_reference_values`）
			var primary: Array = Registry.PRIMARY_KEYS.get(target, [])
			target_column = str(primary[0]) if not primary.is_empty() else ""
		_check_rule_column(db, target, target_column, problems, "REFERENCES 目标")
	for rule: Dictionary in TableValidatorScript.RANGES:
		_check_rule_column(db, str(rule["table"]), str(rule["column"]), problems, "RANGES")
	for rule: Dictionary in TableValidatorScript.RELATIONS:
		_check_rule_column(db, str(rule["table"]), str(rule["left"]), problems, "RELATIONS")
		_check_rule_column(db, str(rule["table"]), str(rule["right"]), problems, "RELATIONS")
	for table: String in TableValidatorScript.ENUMS:
		for column: String in TableValidatorScript.ENUMS[table]:
			_check_rule_column(db, table, str(column), problems, "ENUMS")
	check_eq(problems.size(), 0, "校验器规则里点到的表/列都要真实存在：%s" % str(problems))


func _check_rule_column(db, table: String, column: String, problems: Array, where: String) -> void:
	if not db.has_table(table):
		problems.append("%s：没有表 %s" % [where, table])
		return
	if column.is_empty():
		return
	var rows: Array = db.rows(table)
	if rows.is_empty():
		return
	# CSV 列名 → 行类属性名（`Registry.COLUMN_ALIASES`），再对照真实的属性列表
	var property := Registry.property_of(table, column)
	for entry: Dictionary in (rows[0] as Object).get_property_list():
		if str(entry["name"]) == property:
			return
	problems.append("%s：%s 没有列 %s" % [where, table, column])


## 故意写坏一条引用，校验必须报错（保证校验不是摆设）。
func _check_broken_reference_is_caught(db) -> void:
	var broken = TableDbScript.new()
	broken.load_all()
	var original = broken.tables.get("attr_to_stat")
	check_not_null(original, "attr_to_stat 已加载")
	if original == null:
		return
	var copy: Resource = original.duplicate(true)
	copy.rows[0].attr_id = "not_an_attribute"
	broken.tables["attr_to_stat"] = copy
	var errors: PackedStringArray = TableValidatorScript.validate(broken)
	check_gt(float(errors.size()), 0.0, "写坏引用后校验应报错")

	var broken_pk = TableDbScript.new()
	broken_pk.load_all()
	var pk_copy: Resource = broken_pk.tables["enemy_base"].duplicate(true)
	pk_copy.rows[1].enemy_id = pk_copy.rows[0].enemy_id
	broken_pk.tables["enemy_base"] = pk_copy
	check_gt(float(TableValidatorScript.validate(broken_pk).size()), 0.0, "主键重复应被校验抓到")

	# 属性总数与五维对不上，必须报错
	var broken_attr = TableDbScript.new()
	broken_attr.load_all()
	var attr_copy: Resource = broken_attr.tables["character_base"].duplicate(true)
	attr_copy.rows[0].attr_total = 99
	broken_attr.tables["character_base"] = attr_copy
	check_gt(float(TableValidatorScript.validate(broken_attr).size()), 0.0, "attr_total 与五维对不上应被校验抓到")

	# 事件判定来源写错前缀，必须报错
	var broken_event = TableDbScript.new()
	broken_event.load_all()
	var event_copy: Resource = broken_event.tables["event_check"].duplicate(true)
	event_copy.rows[0].check_source = "qimen"
	broken_event.tables["event_check"] = event_copy
	check_gt(float(TableValidatorScript.validate(broken_event).size()), 0.0, "事件判定缺前缀应被校验抓到")


## 生成资源的 `index` 与 `rows` 对不上（有人手改 .tres、或行被删过没重建索引）时，
## 校验必须逐行点出来**并且不中断**——`rows[position]` 越界会当场报运行期错误、把整段主键检查掐断，
## 排在这张表后面的表一条都不再查（以前 `level_growth` 一旦坏，后面 24 张表的主键检查全部静默跳过；
## 见框架说明决策 147）。所以这里同时弄坏两张表，要求两张都被点名。
func _check_stale_index_is_caught() -> void:
	var broken = TableDbScript.new()
	broken.load_all()
	_drop_first_row_keep_index(broken, "level_growth")
	_drop_first_row_keep_index(broken, "item_base")
	var named: Array = []
	var stale_count := 0
	for message: String in TableValidatorScript.validate(broken):
		if not message.contains("index 与 rows 不一致"):
			continue
		stale_count += 1
		var table_name := message.split(" ")[0]
		if not named.has(table_name):
			named.append(table_name)
	check_true(named.has("level_growth"), "level_growth 的 index 错位应被点名（实报：%s）" % str(named))
	check_true(named.has("item_base"),
		"排在其后的 item_base 也要被查到——越界曾把整段主键检查掐断（实报：%s）" % str(named))
	# 两张表各删一行、index 未重建：每张表被删行之后的每一行都错位，条数必然 > 2
	check_gt(float(stale_count), 2.0, "错位要逐行点出，不能只报一条（实报 %d 条）" % stale_count)


## 删掉第一行但**不重建 index**：造出「index 里记的位置比实际行数大」的越界条件。
func _drop_first_row_keep_index(db, table_name: String) -> void:
	var resource: Resource = db.tables[table_name].duplicate(true)
	var kept: Array = []
	for i in range(resource.rows.size()):
		if i == 0:
			continue
		kept.append(resource.rows[i])
	resource.rows = kept
	db.tables[table_name] = resource


## 两条「写错了不报错」的规则（2026-10-03 用变异探针扫出来的，见框架说明决策 152）：
## ① `rarity_def` 的条数区间写成 min>max —— `AffixRoller.affix_count()` 里有 `maxi(low, high)` 兜着，
##    所以**不会炸**，只是每件装备都只出 min 条：分布没了，界面上一点看不出来；
## ② `level_growth.exp_to_next` 突然变便宜 —— 经验池照涨、付得起就升，没人检查它合不合理。
## 同时钉住「封顶级」的例外：最高等级用 0 表示「没有下一级」，不许被误判成白送一级。
func _check_growth_and_rarity_negatives() -> void:
	var broken_rarity = TableDbScript.new()
	broken_rarity.load_all()
	var rarity_copy: Resource = broken_rarity.tables["rarity_def"].duplicate(true)
	rarity_copy.rows[0].affix_min = 9
	broken_rarity.tables["rarity_def"] = rarity_copy
	var rarity_named := false
	for message: String in TableValidatorScript.validate(broken_rarity):
		if message.contains("词条数量区间不合法"):
			rarity_named = true
	check_true(rarity_named, "词条条数区间写成 min>max 应被校验抓到")

	var broken_exp = TableDbScript.new()
	broken_exp.load_all()
	var exp_copy: Resource = broken_exp.tables["level_growth"].duplicate(true)
	exp_copy.rows[5].exp_to_next = 1          # 第 6 级，门槛反而比第 5 级低
	broken_exp.tables["level_growth"] = exp_copy
	var exp_named := false
	for message: String in TableValidatorScript.validate(broken_exp):
		if message.contains("exp_to_next") and message.contains("还低"):
			exp_named = true
	check_true(exp_named, "exp_to_next 低于上一级应被校验抓到")

	var broken_zero = TableDbScript.new()
	broken_zero.load_all()
	var zero_copy: Resource = broken_zero.tables["level_growth"].duplicate(true)
	zero_copy.rows[3].exp_to_next = 0         # 第 4 级不是封顶级
	broken_zero.tables["level_growth"] = zero_copy
	var zero_named := false
	for message: String in TableValidatorScript.validate(broken_zero):
		if message.contains("不是封顶级"):
			zero_named = true
	check_true(zero_named, "非封顶级的 exp_to_next=0（白送一级）应被校验抓到")

	# 管线依赖的四个比率「必须写上上限」：max_value 留空（= 0 = 本表约定「无上限」）时代码会
	# **静默换用兜底常数**（穿透/格挡 0.75、格挡减伤 0.8、减伤 0.6）——表与行为分家且无人报错。
	var broken_cap = TableDbScript.new()
	broken_cap.load_all()
	var cap_copy2: Resource = broken_cap.tables["stat_def"].duplicate(true)
	for row: Resource in cap_copy2.rows:
		if str(row.stat_id) == "pen_rate":
			row.max_value = 0
	broken_cap.tables["stat_def"] = cap_copy2
	var cap_named := false
	for message: String in TableValidatorScript.validate(broken_cap):
		if message.contains("pen_rate") and message.contains("硬上限"):
			cap_named = true
	check_true(cap_named, "管线用的比率没写上限（留空='无上限'）应被校验抓到")

	# 「造了一个永远出不了手的角色」：起手招式要的武器与模板武器对不上（真实案例见决策 162——
	# 01 的备选模板里 ch_gang 拿刀配拳法、ch_qi 拿拳配剑法，轮到出手时一个按钮都没有）。
	var broken_start = TableDbScript.new()
	broken_start.load_all()
	var start_copy: Resource = broken_start.tables["character_base"].duplicate(true)
	start_copy.rows[0].weapon_type = "fist"     # 书生起手是剑法，改成拳 → 用不出来
	broken_start.tables["character_base"] = start_copy
	var start_named := false
	for message: String in TableValidatorScript.validate(broken_start):
		if message.contains("一部能用的起手招式都没有"):
			start_named = true
	check_true(start_named, "起手招式与模板武器对不上应被校验抓到")

	# 难度阶梯：更高难度不许更弱（原先只查「> 0」，把困难档写成比普通还弱也没人管）
	var weak_hard = TableDbScript.new()
	weak_hard.load_all()
	var diff_copy: Resource = weak_hard.tables["difficulty_config"].duplicate(true)
	for row: Resource in diff_copy.rows:
		if str(row.id) == "hard":
			row.enemy_atk_mul = 0.5
	weak_hard.tables["difficulty_config"] = diff_copy
	var ladder_named := false
	for message: String in TableValidatorScript.validate(weak_hard):
		if message.contains("难度越高数值反而越小"):
			ladder_named = true
	check_true(ladder_named, "高难度倍率低于上一档应被校验抓到")

	# 解锁短语必须被代码认得：写错一个字的短语 = 这一档永远解锁不了
	var bad_unlock = TableDbScript.new()
	bad_unlock.load_all()
	var unlock_copy: Resource = bad_unlock.tables["difficulty_config"].duplicate(true)
	for row: Resource in unlock_copy.rows:
		if str(row.id) == "nightmare":
			row.unlock_condition = "通关第一章并击败醉刀客（误）"
	bad_unlock.tables["difficulty_config"] = unlock_copy
	var unlock_named := false
	for message: String in TableValidatorScript.validate(bad_unlock):
		if message.contains("永远解锁不了"):
			unlock_named = true
	check_true(unlock_named, "没登记的难度解锁短语应被校验抓到")

	# 代码点了名的成长常数，表里少一个就该报（缺行只会静默用兜底值）
	var missing_const = TableDbScript.new()
	missing_const.load_all()
	var const_copy: Resource = missing_const.tables["growth_const"].duplicate(true)
	for row: Resource in const_copy.rows:
		if str(row.const_id) == "mastery_max":
			row.const_id = "mastery_max_typo"
	missing_const.tables["growth_const"] = const_copy
	var const_named := false
	for message: String in TableValidatorScript.validate(missing_const):
		if message.contains("缺少代码依赖的常数 'mastery_max'"):
			const_named = true
	check_true(const_named, "growth_const 少了代码依赖的常数应被校验抓到")

	# 起始装备"装不上"：武器类型不符 / 等级门槛高于起始等级（两条都是"开局空手"的死路）
	var bad_weapon_equip = TableDbScript.new()
	bad_weapon_equip.load_all()
	var weapon_equip_copy: Resource = bad_weapon_equip.tables["character_base"].duplicate(true)
	weapon_equip_copy.rows[0].start_equip_ids = "eq_fist_01"    # 模板是剑，配拳套
	bad_weapon_equip.tables["character_base"] = weapon_equip_copy
	var weapon_equip_named := false
	for message: String in TableValidatorScript.validate(bad_weapon_equip):
		if message.contains("开局装不上"):
			weapon_equip_named = true
	check_true(weapon_equip_named, "起始装备与模板武器类型不符应被校验抓到")

	var high_level_equip = TableDbScript.new()
	high_level_equip.load_all()
	var high_equip_copy: Resource = high_level_equip.tables["equip_base"].duplicate(true)
	for row: Resource in high_equip_copy.rows:
		if str(row.id) == "eq_sword_01":
			row.level_req = 9
	high_level_equip.tables["equip_base"] = high_equip_copy
	var level_equip_named := false
	for message: String in TableValidatorScript.validate(high_level_equip):
		if message.contains("开局装不上"):
			level_equip_named = true
	check_true(level_equip_named, "起始装备等级门槛高于起始等级应被校验抓到")

	# 成长常数的「可用下限」：低于代码守卫的下限会被静默换掉（除数→1.0；费用底数负数→负费用）
	var bad_growth = TableDbScript.new()
	bad_growth.load_all()
	var growth_copy: Resource = bad_growth.tables["growth_const"].duplicate(true)
	for row: Resource in growth_copy.rows:
		if str(row.const_id) == "cultivate_cost_growth":
			row.value = -1.6
		elif str(row.const_id) == "active_slot_lv_div":
			row.value = 0
	bad_growth.tables["growth_const"] = growth_copy
	var floored := 0
	for message: String in TableValidatorScript.validate(bad_growth):
		if message.contains("小于可用下限"):
			floored += 1
	check_eq(floored, 2, "两条越界的成长常数各报一条（实报 %d 条）" % floored)

	# 五条变异探针扫出来的下限/区间（决策 157）：0 层状态、0 回合持续、>100% 触发率、
	# 负售价、负铜钱——今天两边都没有规则，改了没有任何反应。
	_check_scanned_range_rules()


## 变异探针扫出来的五条边界规则：每条都要能真的报错（决策 157）。
func _check_scanned_range_rules() -> void:
	_check_one_cell_caught("status_effect", "poison", "max_stack", 0, "小于下限")
	_check_one_cell_caught("status_effect", "poison", "duration", 0, "小于下限")
	_check_one_cell_caught("status_effect", "poison", "base_chance", 1.5, "大于上限")
	_check_one_cell_caught("item_base", "item_iron", "sell_price", -5, "小于下限")
	_check_one_cell_caught("enemy_base", "en_wolf", "money", -10, "小于下限")


## 把某一格改坏，要求校验报出的错里含 `needle`。
func _check_one_cell_caught(table_name: String, row_id: String, column: String, value, needle: String) -> void:
	var broken = TableDbScript.new()
	broken.load_all()
	var copy: Resource = broken.tables[table_name].duplicate(true)
	for row: Resource in copy.rows:
		if str(row.id) == row_id:
			row.set(column, value)
	broken.tables[table_name] = copy
	var named := false
	for message: String in TableValidatorScript.validate(broken):
		if message.contains(needle):
			named = true
	check_true(named, "%s[%s].%s = %s 应被校验抓到（找 %s）" % [table_name, row_id, column, str(value), needle])


## 0.5.0 新增的四条规则，每条都要能真的报错。
func _check_shop_and_slot_negatives() -> void:
	var broken_slot = TableDbScript.new()
	broken_slot.load_all()
	var equip_copy: Resource = broken_slot.tables["equip_base"].duplicate(true)
	equip_copy.rows[0].slot = "boots"
	broken_slot.tables["equip_base"] = equip_copy
	check_gt(float(TableValidatorScript.validate(broken_slot).size()), 0.0, "非法槽位应被校验抓到")

	var broken_price = TableDbScript.new()
	broken_price.load_all()
	var shop_copy: Resource = broken_price.tables["shop_stock"].duplicate(true)
	shop_copy.rows[0].sell_price = int(shop_copy.rows[0].buy_price)
	broken_price.tables["shop_stock"] = shop_copy
	check_gt(float(TableValidatorScript.validate(broken_price).size()), 0.0, "卖出价不低于买入价应被校验抓到")

	var broken_use = TableDbScript.new()
	broken_use.load_all()
	var item_copy: Resource = broken_use.tables["item_base"].duplicate(true)
	for row: Resource in item_copy.rows:
		if row.item_id == "item_med_01":
			row.use_context = "arena"
	broken_use.tables["item_base"] = item_copy
	check_gt(float(TableValidatorScript.validate(broken_use).size()), 0.0, "use_context 非法值应被校验抓到")

	var broken_weapon = TableDbScript.new()
	broken_weapon.load_all()
	var weapon_copy: Resource = broken_weapon.tables["equip_base"].duplicate(true)
	for row: Resource in weapon_copy.rows:
		if row.slot == "weapon":
			row.weapon_type = ""
			break
	broken_weapon.tables["equip_base"] = weapon_copy
	check_gt(float(TableValidatorScript.validate(broken_weapon).size()), 0.0, "武器槽缺 weapon_type 应被校验抓到")

	# 异常状态的特殊规则：写错一个字母（rule_burn_def_downd）时，效果会静默丢失，
	# 所以构建期就要拦下来——允许清单来自 battle_actor.KNOWN_STATUS_RULES，不手抄。
	var broken_rule = TableDbScript.new()
	broken_rule.load_all()
	var status_copy: Resource = broken_rule.tables["status_effect"].duplicate(true)
	for row: Resource in status_copy.rows:
		if row.status_id == "burn":
			row.extra_rule = "rule_burn_def_downd"
	broken_rule.tables["status_effect"] = status_copy
	var rule_errors: PackedStringArray = TableValidatorScript.validate(broken_rule)
	check_gt(float(rule_errors.size()), 0.0, "extra_rule 写错应被校验抓到：%s" % str(rule_errors))
	var named := false
	for message: String in rule_errors:
		if message.contains("extra_rule"):
			named = true
	check_true(named, "报错要点名 extra_rule：%s" % str(rule_errors))

	# 隐藏触发的条件键写错：后果不对称（behavior 会当成条件已满足直接发奖励），
	# 所以构建期就要拦下来。这里把 `flag_stealth_full` 拼成 `flag_stealth_fullx`。
	var broken_cond = TableDbScript.new()
	broken_cond.load_all()
	var trigger_copy: Resource = broken_cond.tables["hidden_trigger"].duplicate(true)
	for row: Resource in trigger_copy.rows:
		if row.trigger_id == "trig_stealth_clear":
			row.required_condition = "flag_stealth_fullx=1"
	broken_cond.tables["hidden_trigger"] = trigger_copy
	var cond_errors: PackedStringArray = TableValidatorScript.validate(broken_cond)
	var cond_named := false
	for message: String in cond_errors:
		if message.contains("flag_stealth_fullx"):
			cond_named = true
	check_true(cond_named, "条件键拼错应被校验抓到：%s" % str(cond_errors))

	# 击杀方式的值也要校验（写个不存在的死因 → 那条隐藏内容永远拿不到）
	var broken_style = TableDbScript.new()
	broken_style.load_all()
	var style_copy: Resource = broken_style.tables["hidden_trigger"].duplicate(true)
	for row: Resource in style_copy.rows:
		if row.trigger_id == "trig_poison_kill":
			row.required_condition = "kill_with=poisoned"
	broken_style.tables["hidden_trigger"] = style_copy
	var style_errors: PackedStringArray = TableValidatorScript.validate(broken_style)
	var style_named := false
	for message: String in style_errors:
		if message.contains("kill_with"):
			style_named = true
	check_true(style_named, "非法击杀方式应被校验抓到：%s" % str(style_errors))

	# AI 模板拼错：那个敌人会一套招都没有、每回合空过（难度静默塌掉）。
	# 允许清单是 EnemyFactory.AI_SKILL_MAP 的键（单一真相），这里把 ai_basic 拼成 ai_basics。
	var broken_ai = TableDbScript.new()
	broken_ai.load_all()
	var enemy_copy: Resource = broken_ai.tables["enemy_base"].duplicate(true)
	for row: Resource in enemy_copy.rows:
		if row.ai_template == "ai_basic":
			row.ai_template = "ai_basics"
			break
	broken_ai.tables["enemy_base"] = enemy_copy
	var ai_errors: PackedStringArray = TableValidatorScript.validate(broken_ai)
	var ai_named := false
	for message: String in ai_errors:
		if message.contains("ai_basics"):
			ai_named = true
	check_true(ai_named, "ai_template 拼错应被校验抓到：%s" % str(ai_errors))

	# 攻击招式写成 0 段：界面会给按钮（`is_attack()` 为真），但结算一段都跑不到——能用却零伤害。
	var broken_hits = TableDbScript.new()
	broken_hits.load_all()
	var active_copy: Resource = broken_hits.tables["skill_active"].duplicate(true)
	for row: Resource in active_copy.rows:
		if str(row.skill_id) == "sk_xuanwei_01":
			row.hit_count = 0
			break
	broken_hits.tables["skill_active"] = active_copy
	var hit_errors: PackedStringArray = TableValidatorScript.validate(broken_hits)
	var hit_named := false
	for message: String in hit_errors:
		if message.contains("sk_xuanwei_01") and message.contains("hit_count"):
			hit_named = true
	check_true(hit_named, "攻击招式的 hit_count=0 应被校验抓到：%s" % str(hit_errors))
	# 反过来：只把「还没配效果」的空壳（类型与倍率都是 0）留着不算错——它有别的门限盯着
	var dummy_copy: Resource = broken_hits.tables["skill_active"]
	for row: Resource in dummy_copy.rows:
		if str(row.skill_id) == "sk_xuanwei_01":
			row.hit_count = 1
			break
	check_eq(TableValidatorScript.validate(broken_hits).size(), 0, "改回 1 段后校验恢复零错误")

	# 负的 poise_damage：被 `maxi(amount, 0)` 静默吃掉，招式看着能破架势、实际一点都破不了
	var broken_poise = TableDbScript.new()
	broken_poise.load_all()
	var poise_copy: Resource = broken_poise.tables["skill_active"].duplicate(true)
	for row: Resource in poise_copy.rows:
		if str(row.skill_id) == "sk_chensha_01":
			row.poise_damage = -5
			break
	broken_poise.tables["skill_active"] = poise_copy
	var poise_named := false
	for message: String in TableValidatorScript.validate(broken_poise):
		if message.contains("poise_damage"):
			poise_named = true
	check_true(poise_named, "负的 poise_damage 应被校验抓到")

	# 起始等级越界：0 被静默当成 1 级；超过等级上限则找不到 level_growth 那一行
	var broken_level = TableDbScript.new()
	broken_level.load_all()
	var char_copy: Resource = broken_level.tables["character_base"].duplicate(true)
	char_copy.rows[0].start_level = 0
	broken_level.tables["character_base"] = char_copy
	var low_named := false
	for message: String in TableValidatorScript.validate(broken_level):
		if message.contains("start_level"):
			low_named = true
	check_true(low_named, "start_level < 1 应被校验抓到")
	char_copy.rows[0].start_level = broken_level.rows("level_growth").size() + 1
	var high_named := false
	for message: String in TableValidatorScript.validate(broken_level):
		if message.contains("超过等级上限"):
			high_named = true
	check_true(high_named, "start_level 超过等级上限应被校验抓到")

	# 负的异常几率倍率：掷点永远掷不中（招式看着能上毒、实际一次都上不了）
	var broken_status = TableDbScript.new()
	broken_status.load_all()
	var chance_copy: Resource = broken_status.tables["skill_active"].duplicate(true)
	chance_copy.rows[0].status_chance_mul = -1.0
	broken_status.tables["skill_active"] = chance_copy
	var status_named := false
	for message: String in TableValidatorScript.validate(broken_status):
		if message.contains("status_chance_mul"):
			status_named = true
	check_true(status_named, "负的 status_chance_mul 应被校验抓到")

	# 负的经验门槛 / 负的加点：前者让等级一路跳到上限，后者让面板出现「未分配点数：-3」
	var broken_growth = TableDbScript.new()
	broken_growth.load_all()
	var growth_copy: Resource = broken_growth.tables["level_growth"].duplicate(true)
	growth_copy.rows[0].exp_to_next = -1
	growth_copy.rows[0].upgrade_points = -2
	broken_growth.tables["level_growth"] = growth_copy
	var growth_named := 0
	for message: String in TableValidatorScript.validate(broken_growth):
		if message.contains("exp_to_next") or message.contains("upgrade_points"):
			growth_named += 1
	check_eq(growth_named, 2, "负的 exp_to_next 与 upgrade_points 各报一条")

	# 同一个 drop_group 里 slot 重复：`DropResolver` 按 slot 做**不稳定排序**，
	# 两个同号槽的先后就不确定了——「按 drop_group 顺序判定槽位」静默失效（保底键按行 id，不受影响，更难发现）
	var broken_slots = TableDbScript.new()
	broken_slots.load_all()
	var drop_copy: Resource = broken_slots.tables["drop_table"].duplicate(true)
	check_gt(float(drop_copy.rows.size()), 1.0, "掉落表至少两行才好做这个测试")
	drop_copy.rows[1].drop_group = str(drop_copy.rows[0].drop_group)
	drop_copy.rows[1].slot = int(drop_copy.rows[0].slot)
	broken_slots.tables["drop_table"] = drop_copy
	var slot_named := false
	for message: String in TableValidatorScript.validate(broken_slots):
		if message.contains("槽位") and message.contains("重复"):
			slot_named = true
	check_true(slot_named, "同一组里 slot 重复应被校验抓到")

	# level_growth 缺一行：升级会静默卡在缺号前一级（`get_row` 取不到 → 当成「这一级不要经验」）
	var broken_levels = TableDbScript.new()
	broken_levels.load_all()
	var growth_table: Resource = broken_levels.tables["level_growth"].duplicate(true)
	var kept: Array = []
	for row: Resource in growth_table.rows:
		if int(row.level) == 7:
			continue
		kept.append(row)
	growth_table.rows = kept
	broken_levels.tables["level_growth"] = growth_table
	var gap_named := false
	for message: String in TableValidatorScript.validate(broken_levels):
		if message.contains("缺 7 级"):
			gap_named = true
	check_true(gap_named, "level_growth 缺号应被校验抓到")

	# 词条目标写错：前缀漏写 / 目标不存在 / value_kind 与前缀对不上——
	# 三种都会让词条「看起来有用、实际一点不加」，而且没有任何报错
	var broken_affix = TableDbScript.new()
	broken_affix.load_all()
	var affix_copy: Resource = broken_affix.tables["affix_pool"].duplicate(true)
	affix_copy.rows[0].target = "str"                 # 漏前缀
	affix_copy.rows[1].target = "attr:strength"       # 目标不存在
	affix_copy.rows[2].value_kind = "flat"            # attr: 却写 flat
	broken_affix.tables["affix_pool"] = affix_copy
	var affix_errors := PackedStringArray()
	for message: String in TableValidatorScript.validate(broken_affix):
		if message.contains("affix_pool"):
			affix_errors.append(message)
	check_eq(affix_errors.size(), 3, "三种词条目标错各报一条：%s" % str(affix_errors))

	# 升级不该让人变弱：把某一级的基础气血写得比上一级低 → 校验必须点名
	var broken_base = TableDbScript.new()
	broken_base.load_all()
	var base_copy: Resource = broken_base.tables["level_growth"].duplicate(true)
	for row: Resource in base_copy.rows:
		if int(row.level) == 6:
			row.base_hp = 1.0
			break
	broken_base.tables["level_growth"] = base_copy
	var weak_named := false
	for message: String in TableValidatorScript.validate(broken_base):
		if message.contains("升级会变弱"):
			weak_named = true
	check_true(weak_named, "基础数值随等级下降应被校验抓到")

	# 装备等级需求超过上限：永远穿不上（背包里一直写「需要 N 级」，等到满级也没用）
	var broken_req = TableDbScript.new()
	broken_req.load_all()
	var req_copy: Resource = broken_req.tables["equip_base"].duplicate(true)
	var top_level: int = TableValidatorScript.max_level(broken_req)
	check_gt(float(top_level), 0.0, "等级上限算得出来（%d）" % top_level)
	req_copy.rows[0].level_req = top_level + 5
	req_copy.rows[1].level_req = 0
	broken_req.tables["equip_base"] = req_copy
	var req_errors := PackedStringArray()
	for message: String in TableValidatorScript.validate(broken_req):
		if message.contains("level_req"):
			req_errors.append(message)
	check_eq(req_errors.size(), 2, "超上限与低于 1 级各报一条：%s" % str(req_errors))

	# 敌人战斗数值：负防御会让伤害公式反向；比率超过 1 没有意义
	var broken_enemy = TableDbScript.new()
	broken_enemy.load_all()
	var stat_copy: Resource = broken_enemy.tables["enemy_base"].duplicate(true)
	stat_copy.rows[0].def_phys = -5
	# 0.14.0 起 `res_poison` 那三条特权列取消了（敌人要抗性就装内功），
	# 上限检查改用**保留下来**的 `res_internal`
	stat_copy.rows[0].res_internal = 1.5
	broken_enemy.tables["enemy_base"] = stat_copy
	var enemy_errors := PackedStringArray()
	for message: String in TableValidatorScript.validate(broken_enemy):
		if message.contains("enemy_base"):
			enemy_errors.append(message)
	check_eq(enemy_errors.size(), 2, "负防御与超额抗性各报一条：%s" % str(enemy_errors))

	# 难度倍率必须严格为正：0 倍血 = 敌人一出生就死；0 倍经验 = 这个难度白练
	var broken_diff = TableDbScript.new()
	broken_diff.load_all()
	var diff_copy: Resource = broken_diff.tables["difficulty_config"].duplicate(true)
	diff_copy.rows[0].enemy_hp_mul = 0.0
	diff_copy.rows[0].exp_mul = -1.0
	broken_diff.tables["difficulty_config"] = diff_copy
	var diff_errors := PackedStringArray()
	for message: String in TableValidatorScript.validate(broken_diff):
		if message.contains("difficulty_config"):
			diff_errors.append(message)
	check_eq(diff_errors.size(), 2, "0 倍血与负经验倍率各报一条：%s" % str(diff_errors))

	# 非白板装备全零加成 = 和「白板」没区别；内功加成的 value=0 = 白填
	var broken_bonus = TableDbScript.new()
	broken_bonus.load_all()
	var bonus_copy: Resource = broken_bonus.tables["equip_base"].duplicate(true)
	var wiped := false
	for row: Resource in bonus_copy.rows:
		if str(row.rarity) != "common":
			row.attr_str = 0
			row.attr_con = 0
			row.attr_agi = 0
			row.attr_int = 0
			row.attr_luk = 0
			row.bonus_atk_phys = 0
			row.bonus_atk_qi = 0
			row.bonus_def_phys = 0
			row.bonus_hp_max = 0
			row.bonus_speed = 0
			row.bonus_crit_rate = 0
			row.bonus_pen_rate = 0
			row.bonus_block_rate = 0
			row.bonus_block_reduction = 0
			row.bonus_dmg_reduction = 0
			wiped = true
			break
	check_true(wiped, "找到了一件非白板装备用来做这个测试")
	broken_bonus.tables["equip_base"] = bonus_copy
	var bonus_named := false
	for message: String in TableValidatorScript.validate(broken_bonus):
		if message.contains("和白板") or message.contains("一条加成都没有"):
			bonus_named = true
	check_true(bonus_named, "非白板装备全零加成应被校验抓到")

	var broken_passive = TableDbScript.new()
	broken_passive.load_all()
	var passive_copy: Resource = broken_passive.tables["skill_passive_stat"].duplicate(true)
	passive_copy.rows[0].value = 0.0
	broken_passive.tables["skill_passive_stat"] = passive_copy
	var passive_named := false
	for message: String in TableValidatorScript.validate(broken_passive):
		if message.contains("白填"):
			passive_named = true
	check_true(passive_named, "内功加成 value=0 应被校验抓到")

	# 楼层号：0 层会被面板显示成「第 0 层」；缺一层会让完成度面板少一行
	var broken_floor = TableDbScript.new()
	broken_floor.load_all()
	var floor_copy: Resource = broken_floor.tables["dungeon_room"].duplicate(true)
	for row: Resource in floor_copy.rows:
		if int(row.floor) == 1:
			row.floor = 0
			break
	broken_floor.tables["dungeon_room"] = floor_copy
	var zero_named := false
	for message: String in TableValidatorScript.validate(broken_floor):
		if message.contains("第 0 层"):
			zero_named = true
	check_true(zero_named, "floor=0 应被校验抓到")

	var broken_gap = TableDbScript.new()
	broken_gap.load_all()
	var gap_copy: Resource = broken_gap.tables["dungeon_room"].duplicate(true)
	var kept_rooms: Array = []
	for row: Resource in gap_copy.rows:
		if str(row.scene_id) == "scene_heifengzhai" and int(row.floor) == 2:
			continue
		kept_rooms.append(row)
	gap_copy.rows = kept_rooms
	broken_gap.tables["dungeon_room"] = gap_copy
	var floor_gap_named := false
	for message: String in TableValidatorScript.validate(broken_gap):
		if message.contains("缺第 2 层"):
			floor_gap_named = true
	check_true(floor_gap_named, "楼层缺号应被校验抓到")

	# **校验器自己的规则表自检**：`REFERENCES` / `RANGES` / `RELATIONS` / `ENUMS` 里点到的
	# 表名与列名必须真实存在。这些规则是几十轮里一条条加出来的，而**列名写错很危险**：
	# `row.get(错列)` 返回 null，`float(null)` 会当场报运行期错误、**让整个校验中途断掉**
	# （后面的规则一条都不再跑——那才是真正可怕的地方，探针实测过）。
	# 这条门限把「规则写错」变成当场红；校验器本身也加了守卫，见 `_check_ranges`。
	_check_validator_rule_targets(TableDbScript.new())
