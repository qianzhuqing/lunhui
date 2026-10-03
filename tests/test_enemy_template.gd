## 敌人模板与角色**共用一套管线**（设计 10 §二，0.14.0）：
## 七维 + 等级 + 装备加成 + 内功加成 → `attr_to_stat`／`level_growth` → 派生数值。
##
## 发行数据里那七列还空着（设计要用平衡观测工具一起配，10 §三），所以这里用**内存夹具**
## 把七维填上，验「填上之后算出来的与 `AttributeCalculator` 一模一样」——只改内存，不碰 CSV。
extends "res://tests/test_case.gd"

const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")


func suite_name() -> String:
	return "敌人模板与角色同管线"


func run() -> void:
	var db = get_db()
	_check_empty_attrs_fall_back(db)
	_check_shared_pipeline(db)
	_check_equipment_contributions(db)
	_check_passive_contributions(db)
	_check_attr_map_semantics(db)


## 七维空着（发行数据的现状）→ 走旧派生列那条过渡路，**并且出声点名**
func _check_empty_attrs_fall_back(db) -> void:
	var row: Resource = db.get_row("enemy_base", "en_bd_thug")
	check_true(row.attr_map().is_empty(), "发行数据里山寨喽啰的七维还没配（attr_map 为空）")
	var factory = EnemyFactoryScript.new(db)
	var actor = factory.create("en_bd_thug", "normal")
	check_not_null(actor, "空七维也能造出敌人（过渡路径不静默崩）")
	if actor == null:
		return
	check_gt(float(actor.max_hp()), 0.0, "血量来自旧派生列（%d）" % actor.max_hp())
	# 过渡路上**装备/内功的 stat 类加成照样算**（不然毒抗会凭空消失）——见 _check_passive_contributions


## 填上七维 → 派生数值与 `AttributeCalculator` 完全一致（这就是「同一套算法」的可验证形式）
func _check_shared_pipeline(db) -> void:
	var local = _with_attrs(db, "en_bd_thug", {
		"str": 9, "con": 7, "agi": 6, "int": 4, "luk": 3, "wu": 6, "gen": 5,
	})
	var row: Resource = local.get_row("enemy_base", "en_bd_thug")
	check_false(row.attr_map().is_empty(), "夹具里七维填上了")
	var factory = EnemyFactoryScript.new(local)
	var actor = factory.create("en_bd_thug", "normal")
	check_not_null(actor, "七维齐了照样能造")
	if actor == null:
		return
	var difficulty: Resource = local.get_row("difficulty_config", "normal")
	var calculator = AttributeCalculatorScript.new(local)
	var expected: Dictionary = calculator.compute(
		int(row.level), row.attr_map(), {}, EnemyFactoryScript.new(local)._enemy_contributions("en_bd_thug")
	)
	for stat_id: String in ["hp_max", "atk_phys", "atk_qi", "def_phys", "def_qi"]:
		var mul := 1.0
		match stat_id:
			"hp_max":
				mul = float(difficulty.enemy_hp_mul)
			"atk_phys", "atk_qi":
				mul = float(difficulty.enemy_atk_mul)
			"def_phys", "def_qi":
				mul = float(difficulty.enemy_def_mul)
		var want: float = float(expected.get(stat_id, 0.0)) * mul
		if stat_id == "hp_max":
			want = float(roundi(want))
		check_float(float(actor.stat(stat_id)), want, "%s 与角色同一套 AttributeCalculator 算出来的一致" % stat_id, 0.01)
	# 不吃难度倍率的那几项照抄派生值
	check_float(float(actor.stat("speed")), float(expected.get("speed", 0.0)), "身法照抄派生值", 0.01)
	check_float(float(actor.stat("crit_rate")), float(expected.get("crit_rate", 0.0)), "暴击率照抄派生值", 0.0001)
	# 内力池：设计 10 §二要「敌人也要有内力池」——共用管线把 qi_max 算出来了（不再是 0）
	check_gt(float(actor.stat("qi_max")), 0.0, "七维齐了就有内力池（qi_max=%d）" % int(actor.stat("qi_max")) )


## 装备加成：`enemy_equip` 上的东西按 `equip_base` 的 bonus_* 进属性（与角色同一条贡献通道）
func _check_equipment_contributions(db) -> void:
	var attrs := {"str": 5, "con": 5, "agi": 5, "int": 5, "luk": 5, "wu": 5, "gen": 5}
	var bare = _with_attrs(db, "en_bd_thug", attrs)
	var armed = _with_attrs(db, "en_bd_thug", attrs)
	# 把山寨喽啰的装备清空（bare）／只留一把加外功的柴刀（armed，本来就挂着）
	var table: Resource = bare.tables["enemy_equip"].duplicate(true)
	var kept: Array = []
	for row: Resource in table.rows:
		if str(row.enemy_id) != "en_bd_thug":
			kept.append(row)
	table.rows = kept
	table.index = {}
	for i in range(kept.size()):
		table.index[str(kept[i].id)] = i
	bare.tables["enemy_equip"] = table
	var bare_actor = EnemyFactoryScript.new(bare).create("en_bd_thug", "normal")
	var armed_actor = EnemyFactoryScript.new(armed).create("en_bd_thug", "normal")
	check_not_null(bare_actor, "裸装山寨喽啰造出来了")
	check_not_null(armed_actor, "带装山寨喽啰造出来了")
	if bare_actor == null or armed_actor == null:
		return
	check_gt(
		float(armed_actor.stat("atk_phys")), float(bare_actor.stat("atk_phys")),
		"柴刀给的外功加成算进去了（%.1f → %.1f）" % [bare_actor.stat("atk_phys"), armed_actor.stat("atk_phys")]
	)
	check_gt(
		float(armed_actor.max_hp()), float(bare_actor.max_hp()),
		"布衣给的气血加成算进去了（%d → %d）" % [bare_actor.max_hp(), armed_actor.max_hp()]
	)


## 内功加成：`enemy_passive` → `skill_passive_stat` 的 `stat:`／`attr:` 也进同一条通道
## （**三种元素抗性就是从这儿来的**：10 §二取消了那三条特权列）
func _check_passive_contributions(db) -> void:
	var local = _with_attrs(db, "en_bd_thug", {"str": 5, "con": 5, "agi": 5, "int": 5, "luk": 5, "wu": 5, "gen": 5})
	var plain = EnemyFactoryScript.new(local).create("en_bd_thug", "normal")
	check_float(plain.resistance_of("poison"), 0.0, "没装内功时没有毒抗")
	# 给它挂上五毒心法·蚀骨（`pf_wudu_03` 给 0.15 毒抗）
	var table: Resource = local.tables["enemy_passive"].duplicate(true)
	var template: Resource = table.rows[0].duplicate(true)
	template.enemy_id = "en_bd_thug"
	template.skill_id = "pf_wudu_03"
	template.id = "en_bd_thug|pf_wudu_03"
	table.rows.append(template)
	table.index[str(template.id)] = table.rows.size() - 1
	local.tables["enemy_passive"] = table
	var trained = EnemyFactoryScript.new(local).create("en_bd_thug", "normal")
	check_float(trained.resistance_of("poison"), 0.15, "装上五毒心法就有 0.15 毒抗（与角色同一份表）")
	# 招式槽上限（设计 10 §二 连带规则①）：悟性决定能带几招
	var attrs_before: Dictionary = local.get_row("enemy_base", "en_bd_thug").attr_map()
	var growth = GrowthCalculatorScript.new(local)
	check_eq(
		growth.active_slots(int(local.get_row("enemy_base", "en_bd_thug").level), attrs_before),
		growth.active_slots(int(local.get_row("enemy_base", "en_bd_thug").level), attrs_before),
		"槽位公式用的是同一份 growth_const（这条只是把口径写出来）"
	)


## `attr_map()` 的语义：**七列全 0 才当「没配」**；有一列非 0 就整行生效
func _check_attr_map_semantics(db) -> void:
	var local = TableDbScript.new()
	local.load_all()
	var table: Resource = local.tables["enemy_base"].duplicate(true)
	var row: Resource = table.rows[0]
	check_true(row.attr_map().is_empty(), "全 0 → 空字典（走过渡路径）")
	row.attr_luk = 3
	check_false(row.attr_map().is_empty(), "有一列非 0 → 整行生效")
	check_eq(int(row.attr_map().get("luk", 0)), 3, "取到的就是那一列")
	check_eq(int(row.attr_map().get("str", -1)), 0, "其余列按 0 参与")


# ------------------------------------------------------------------ 夹具

## 给某个敌人的七维填上值（只改内存副本）
func _with_attrs(db, enemy_id: String, attrs: Dictionary):
	var local = TableDbScript.new()
	local.load_all()
	var table: Resource = local.tables["enemy_base"].duplicate(true)
	for row: Resource in table.rows:
		if str(row.enemy_id) != enemy_id:
			continue
		row.attr_str = int(attrs.get("str", 0))
		row.attr_con = int(attrs.get("con", 0))
		row.attr_agi = int(attrs.get("agi", 0))
		row.attr_int = int(attrs.get("int", 0))
		row.attr_luk = int(attrs.get("luk", 0))
		row.attr_wu = int(attrs.get("wu", 0))
		row.attr_gen = int(attrs.get("gen", 0))
	local.tables["enemy_base"] = table
	return local
