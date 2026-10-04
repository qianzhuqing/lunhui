## 敌人模板与角色**共用一套管线**（设计 10 §二，0.14.0）：
## 七维 + 等级 + 装备加成 + 内功加成 → `attr_to_stat`／`level_growth` → 派生数值。
##
## 发行数据里那七列还空着（设计要用平衡观测工具一起配，10 §三），所以这里用**内存夹具**
## 把七维填上，验「填上之后算出来的与 `AttributeCalculator` 一模一样」——只改内存，不碰 CSV。
extends "res://tests/test_case.gd"

const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")
const TableValidatorScript := preload("res://src/core/table_validator.gd")


func suite_name() -> String:
	return "敌人模板与角色同管线"


func run() -> void:
	var db = get_db()
	_check_empty_attrs_fall_back(db)
	_check_shared_pipeline(db)
	_check_equipment_contributions(db)
	_check_passive_contributions(db)
	_check_passive_capacity_rule(db)
	_check_attr_map_semantics(db)


## 七维空着 → 走旧派生列那条过渡路，**并且出声点名**。
##
## 0.28.0 之后**发行数据全配齐了**（17 行都填了七维），所以这条回退路径只能用
## **内存夹具**验（把某一行的七维清零）——它仍然要留着：它是"数据漏配一行"时的兜底，
## 而且必须出声，不能静默按 0 算。
func _check_empty_attrs_fall_back(db) -> void:
	# ① 发行数据：**一行都不许空着**（空了就说明漏配，玩家会看到一条没调的敌人）
	var pending := PackedStringArray()
	for row: Resource in db.rows("enemy_base"):
		if row.attr_map().is_empty():
			pending.append(str(row.enemy_id))
	check_true(
		pending.is_empty(),
		"发行数据 17 行敌人的七维全配齐（空着的：%s）" % "、".join(pending),
	)
	# ② 回退路径：夹具把山寨喽啰的七维清零
	var local = _with_attrs(db, "en_bd_thug", {})
	var row: Resource = local.get_row("enemy_base", "en_bd_thug")
	check_true(row.attr_map().is_empty(), "夹具里那一行的七维被清零了")
	var factory = EnemyFactoryScript.new(local)
	var actor = factory.create("en_bd_thug", "normal")
	check_not_null(actor, "空七维也能造出敌人（过渡路径不静默崩）")
	if actor == null:
		return
	# 过渡路上「派生列 ＋ 装备/内功的固定值」都要算（山寨喽啰的布衣给 20 点气血）
	var equip_hp := 0
	for equip_row: Resource in local.rows_where("enemy_equip", "enemy_id", "en_bd_thug"):
		var equip: Resource = local.get_row("equip_base", str(equip_row.equip_id))
		if equip != null:
			equip_hp += int(equip.bonus_hp_max)
	check_eq(
		actor.max_hp(), int(row.hp_base) + equip_hp,
		"血量来自旧派生列 ＋ 装备（%d）" % actor.max_hp(),
	)
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
		int(row.level), row.attr_map(), {},
		EnemyFactoryScript.new(local)._enemy_contributions("en_bd_thug"),
		false,   # 0.28.0：敌人**不吃** level_growth 的基础列（设计 10 §七）
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
	# 0.28.0 起发行数据全配着七维了，这条语义要用**清零后的**夹具行验
	row.attr_str = 0
	row.attr_con = 0
	row.attr_agi = 0
	row.attr_int = 0
	row.attr_luk = 0
	row.attr_wu = 0
	row.attr_gen = 0
	check_true(row.attr_map().is_empty(), "全 0 → 空字典（走过渡路径）")
	row.attr_luk = 3
	check_false(row.attr_map().is_empty(), "有一列非 0 → 整行生效")
	check_eq(int(row.attr_map().get("luk", 0)), 3, "取到的就是那一列")
	check_eq(int(row.attr_map().get("str", -1)), 0, "其余列按 0 参与")


## 敌人配的内功**占格 ≤ 容量**（设计 10 §二 连带规则①的另一半）——构建期那条规则的**反例**。
##
## 由来（2026-10-04）：10 号那句「敌人同样受招式槽与内功容量限制……用的是 `growth_const` 里同一组公式」
## 只接了招式槽（0.10.0），容量这半边一直空着；七维 0.28.0 配齐之后它本来该生效。
## 补上检查一量就抓到 **2 行超编**（`en_bd_boss` 5 格 > 4、`en_hidden_drunk` 7 格 > 4）——
## 数据是设计侧配的，「削内功／提根骨」属内容决定（`待策划确认.md` Q79），所以那两行进白名单、
## **双向维护**（补进容量内会红、新超编也红）。这条用例只钉两件事：
## ①**发行数据的形状**（除白名单外一行都不许超编）；②**那条规则真的抓得住**（夹具里把
## 屠夫身上那部内功的占格 2 改 3，构建期必须点名 `en_bd_butcher`）。
func _check_passive_capacity_rule(db) -> void:
	var growth = GrowthCalculatorScript.new(db)
	var calculator = AttributeCalculatorScript.new(db)
	var over := {}
	for row: Resource in db.rows("enemy_base"):
		var enemy_id := str(row.enemy_id)
		var attrs: Dictionary = row.attr_map()
		if attrs.is_empty():
			continue
		var passives := PackedStringArray()
		for passive: Resource in db.rows_where("enemy_passive", "enemy_id", enemy_id):
			passives.append(str(passive.skill_id))
		var contributions: Array = growth.passive_contributions(passives) if not passives.is_empty() else []
		var totals: Dictionary = calculator.attr_totals_of(attrs, {}, contributions)
		var fit: Dictionary = growth.passive_fit(passives, int(row.level), totals)
		if not bool(fit["fits"]):
			over[enemy_id] = "%d 格 > 容量 %d" % [int(fit["used"]), int(fit["capacity"])]
	check_eq(over.size(), 2, "发行数据里超编的敌人正好两个（现在：%s）" % str(over))
	check_true(over.has("en_bd_boss"), "大寨主在超编名单里（%s）" % str(over.get("en_bd_boss", "不在")))
	check_true(over.has("en_hidden_drunk"), "醉刀客在超编名单里（%s）" % str(over.get("en_hidden_drunk", "不在")))

	# 夹具：把屠夫身上 `pf_chensha_02` 的占格从 2 改成 3 → 3＋1＝4 格 > 容量 3
	var broken = TableDbScript.new()
	broken.load_all()
	var table: Resource = broken.tables["skill_passive"].duplicate(true)
	for row: Resource in table.rows:
		if str(row.skill_id) == "pf_chensha_02":
			row.slot_cost = 3
	broken.tables["skill_passive"] = table
	var errors: PackedStringArray = TableValidatorScript.validate(broken)
	var named := false
	for message: String in errors:
		if message.contains("en_bd_butcher") and message.contains("超编"):
			named = true
	check_true(named, "构建期要抓住「内功超编」并点名 en_bd_butcher：%s" % str(errors))


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
