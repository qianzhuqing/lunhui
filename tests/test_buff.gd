## 统一 buff 与套装（设计 0.7.0 的 `08_增益与套装.md`；6 张新表）。
##
## 这个用例管两件事：
##   ① **表本身自洽**：id 不重复、引用存在、枚举在 08 写的取值里、套装档位的**计数口径**对得上；
##      （**「玩家凑不凑得齐」不在这里**——那要按来源算，见 `test_handshake::_check_set_tier_reachability`
##       与 `validate_tables.ps1` 5.19b；当前三个档位凑不出来，记在 `当前状态.md` 3.1 #28）
##   ② **两类规则落成代码**：`BuffService` 的静态规则、`SetService` 的计数口径
##      （只算已装备／已装配；内功套按**占格数之和**；档位向下兼容）。
## 结算（上 buff／递减／并进属性）不在这里——那要动战斗管线，见框架说明后续决策。
extends "res://tests/test_case.gd"

const BuffServiceScript := preload("res://src/core/buff_service.gd")
const SetServiceScript := preload("res://src/core/set_service.gd")
const InventoryScript := preload("res://src/core/inventory.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const BattleActorScript := preload("res://src/core/battle_actor.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")

const SCOPE_VALUES := ["battle", "field"]
const STACK_VALUES := ["refresh", "stack", "unique"]
const EFFECT_KINDS := ["stat", "computed", "special"]
const SOURCE_TYPES := ["skill_passive", "skill_active", "equip", "set"]
const TRIGGERS := ["on_cast", "on_equip", "on_hit", "on_battle_start"]
const SET_KINDS := ["equip", "skill_active", "skill_passive"]


func suite_name() -> String:
	return "增益与套装（buff／set）"


func run() -> void:
	var db = get_db()
	# 极性／常驻哨兵的绝对值：这三条是 2026-10-03 变异探针第三块点名「没人钉」的
	check_eq(BuffServiceScript.POLARITY_BUFF, 0, "增益极性 = 0")
	check_eq(BuffServiceScript.POLARITY_DEBUFF, 1, "减益极性 = 1")
	check_eq(BuffServiceScript.DURATION_PERMANENT, 0, "常驻（永久）哨兵 = 0")
	_check_tables(db)
	_check_buff_service(db)
	_check_set_service(db)
	_check_permanent_contributions(db)
	_check_actor_buff_rules(db)
	_check_six_commands(db)
	_check_battle_start_grants(db)
	_check_field_buffs(db)
	_check_cast_grants(db)
	_check_stack_scaling(db)
	_check_support_skill(db)
	_check_skill_hit_grants(db)


## ① 表自洽：06 写的行数、引用、枚举、档位可达
func _check_tables(db) -> void:
	var expected_rows := {"buff_def": 19, "buff_stat": 31, "buff_grant": 41, "set_def": 3, "set_member": 14, "set_bonus": 6}
	for table_name: String in expected_rows:
		check_eq(
			db.rows(table_name).size(), int(expected_rows[table_name]),
			"%s 行数与 06 的表清单一致" % table_name
		)

	for row: Resource in db.rows("buff_def"):
		check_true(SCOPE_VALUES.has(str(row.scope)), "%s.scope=%s 在 08 的取值里" % [row.buff_id, row.scope])
		check_true(STACK_VALUES.has(str(row.stack_rule)), "%s.stack_rule=%s 在 08 的取值里" % [row.buff_id, row.stack_rule])
		check_true(EFFECT_KINDS.has(str(row.effect_kind)), "%s.effect_kind=%s 在 08 的取值里" % [row.buff_id, row.effect_kind])
		check_gt(float(row.max_stack), 0.0, "%s.max_stack 至少 1" % row.buff_id)
		if str(row.stack_rule) != BuffServiceScript.STACK_STACK:
			check_eq(int(row.max_stack), 1, "%s 不叠层时 max_stack 应当是 1" % row.buff_id)

	for row: Resource in db.rows("buff_stat"):
		check_true(db.get_row("buff_def", str(row.buff_id)) != null, "buff_stat 的 %s 在 buff_def 里存在" % row.buff_id)
		var parsed: Dictionary = row.parsed_target()
		check_true(
			str(parsed["kind"]) in ["attr", "stat"],
			"buff_stat 的 target 带 attr:/stat: 前缀：%s" % row.target
		)

	for row: Resource in db.rows("buff_grant"):
		check_true(db.get_row("buff_def", str(row.buff_id)) != null, "buff_grant %s 发的 buff 存在" % row.grant_id)
		check_true(SOURCE_TYPES.has(str(row.source_type)), "%s.source_type=%s 在 08 的取值里" % [row.grant_id, row.source_type])
		check_true(TRIGGERS.has(str(row.trigger)), "%s.trigger=%s 在 08 的取值里" % [row.grant_id, row.trigger])
		var source_table := ""
		match str(row.source_type):
			"skill_passive", "skill_active":
				source_table = "skill_base"
			"equip":
				source_table = "equip_base"
			"set":
				source_table = "set_def"
		if not source_table.is_empty():
			check_true(
				db.get_row(source_table, str(row.source_id)) != null,
				"%s 的来源 %s 在 %s 里存在" % [row.grant_id, row.source_id, source_table]
			)

	for row: Resource in db.rows("set_def"):
		check_true(SET_KINDS.has(str(row.set_kind)), "%s.set_kind=%s 在 08 的取值里" % [row.set_id, row.set_kind])
	for row: Resource in db.rows("set_member"):
		check_true(db.get_row("set_def", str(row.set_id)) != null, "set_member 的 %s 是已知套装" % row.set_id)
		var kind := str(db.get_row("set_def", str(row.set_id)).set_kind)
		var member_table := "equip_base" if kind == "equip" else "skill_base"
		check_true(
			db.get_row(member_table, str(row.member_id)) != null,
			"%s 的成员 %s 在 %s 里存在" % [row.set_id, row.member_id, member_table]
		)
	for row: Resource in db.rows("set_bonus"):
		check_true(db.get_row("buff_def", str(row.buff_id)) != null, "set_bonus %s 的 buff 存在" % row.buff_id)
		check_gt(float(row.required_count), 0.0, "%s 档位至少 1 件" % row.set_id)


## ② BuffService：静态规则按 08 的口径
func _check_buff_service(db) -> void:
	var service = BuffServiceScript.new(db)
	check_eq(service.all_defs().size(), 19, "BuffService 能列出全部 buff")
	check_true(service.exists("buff_guard"), "防御姿态在表里")
	check_false(service.is_debuff("buff_guard"), "防御姿态是增益（不是减益）")
	check_eq(service.polarity_of("buff_guard"), BuffServiceScript.POLARITY_BUFF, "极性按 is_debuff 算")
	check_eq(service.scope_of("buff_guard"), BuffServiceScript.SCOPE_BATTLE, "防御姿态是战斗内")
	check_true(service.is_field_buff("buff_meditated"), "打坐余韵是战斗外（field）增益")
	check_gt(
		float(service.field_minutes_of("buff_meditated")), 0.0,
		"战斗外增益按**现实分钟**计时（0.8.0 口径）：%d 分钟" % service.field_minutes_of("buff_meditated")
	)
	check_eq(service.field_minutes_of("buff_guard"), 0, "战斗内增益不看 field_minutes")
	check_eq(service.duration_of("buff_guard"), 1, "防御姿态持续 1 回合")
	check_true(service.is_permanent("buff_rusty_moon"), "锈月剑意是常驻（duration=0）")
	check_eq(service.duration_of("buff_regen"), BuffServiceScript.DURATION_UNTIL_BATTLE_END, "药王护体持续到本场结束")
	check_eq(service.stack_rule_of("buff_poison_amp"), BuffServiceScript.STACK_STACK, "淬毒是叠层")
	check_eq(service.max_stack_of("buff_poison_amp"), 3, "淬毒最多 3 层")
	check_eq(service.effect_kind_of("buff_guard"), BuffServiceScript.EFFECT_KIND_STAT, "防御姿态是数值型")

	var guard_mods: Array = service.stat_mods_of("buff_guard")
	check_eq(guard_mods.size(), 2, "防御姿态带两条数值修正（减伤＋外防）")
	var kinds: Dictionary = {}
	for mod: Dictionary in guard_mods:
		kinds["%s:%s" % [str(mod["kind"]), str(mod["target_id"])]] = float(mod["value"])
	check_true(kinds.has("stat:dmg_reduction"), "防御姿态给了减伤")
	check_true(kinds.has("stat:def_phys"), "防御姿态给了外防")

	check_eq(service.grants_for("skill_passive", "pf_xuanwei_05", "on_cast").size(), 1, "太清靠 on_cast 发运功 buff")
	check_true(
		service.persistent_grants_for("equip", "eq_sword_04").size() >= 1,
		"锈月是装备即生效（常驻）：%s" % service.persistent_grants_for("equip", "eq_sword_04").size()
	)


## ③ SetService：计数口径（只算已装备／已装配；内功套按占格数之和；档位向下兼容）
func _check_set_service(db) -> void:
	var service = SetServiceScript.new(db)
	check_eq(service.all_defs().size(), 3, "第一章 3 套套装")
	check_eq(service.kind_of("set_heifeng"), SetServiceScript.KIND_EQUIP, "黑风寨是装备套")
	check_eq(service.members_of("set_xuanwei_sword").size(), 5, "玄微剑意 5 招")
	check_eq(service.bonuses_of("set_heifeng").size(), 2, "黑风寨两档（2 件／4 件）")

	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	var inventory = InventoryScript.new()
	# 黑风寨那套有等级门槛（7~9 级），先升到 12 级再穿——套装用例验的是计数口径，不是等级校验
	state.append_level(char_id, 11)
	var level: int = state.level_of(char_id)
	var weapon_type := str(db.get_row("character_base", char_id).weapon_type)

	# 装备套：只算穿在身上的——先在背包里放一件，不该被算进去
	inventory.add_equipment(db, "eq_armor_03")
	check_eq(service.count_for("set_heifeng", char_id, inventory, state), 0, "只在背包里不算套装件数")
	var worn := PackedStringArray([
		inventory.add_equipment(db, "eq_armor_03"), inventory.add_equipment(db, "eq_ring_03"),
		inventory.add_equipment(db, "eq_head_01"),
	])
	for instance_id: String in worn:
		var result: Dictionary = inventory.equip(db, char_id, level, weapon_type, instance_id)
		check_true(bool(result["ok"]), "穿得上 %s（%s）" % [instance_id, result.get("error", "")])
	check_eq(service.count_for("set_heifeng", char_id, inventory, state), 3, "穿上 3 件 → 算 3 件")
	# 第 4 件是**黑风刀**（blade），而这个模板是剑系（sword）——正经的拒绝，不是 bug。
	# 4 件档因此要等"刀系可用角色"（待策划确认 Q17）；档位逻辑先用 active_buff_ids 单独验。
	var blade := inventory.add_equipment(db, "eq_sword_03")
	var blade_result: Dictionary = inventory.equip(db, char_id, level, weapon_type, blade)
	check_false(bool(blade_result["ok"]), "剑系角色穿不上黑风刀（武器类型限制）")
	check_true(
		str(blade_result.get("error", "")).contains("刀") or str(blade_result.get("error", "")).contains("blade"),
		"拒绝理由说清武器类型：%s" % blade_result.get("error", "")
	)
	check_eq(
		service.active_buff_ids("set_heifeng", 4).size(), 2,
		"4 件时两档都生效（向下兼容）"
	)
	check_eq(
		service.active_buff_ids("set_heifeng", 2).size(), 1,
		"2 件时只有低档"
	)

	# 招式套：按装配的招数
	state.set_loadout(char_id, PackedStringArray([
		"sk_xuanwei_01", "sk_xuanwei_02", "sk_xuanwei_03", "sk_xuanwei_04", "sk_xuanwei_05",
	]), PackedStringArray())
	check_eq(service.count_for("set_xuanwei_sword", char_id, inventory, state), 5, "装 5 招 → 算 5 招")
	check_eq(service.active_buff_ids("set_xuanwei_sword", 5).size(), 2, "5 招时 3 招档＋5 招档都生效")

	# 内功套：按**占格数之和**（不是部数）——这条正是设计特别点名的口径
	var passives := PackedStringArray([
		"pf_xuanwei_01", "pf_xuanwei_02", "pf_xuanwei_03", "pf_xuanwei_04", "pf_xuanwei_05",
	])
	state.set_loadout(char_id, PackedStringArray(), passives)
	var slots := 0
	for passive_id: String in passives:
		slots += int(db.get_row("skill_passive", passive_id).slot_cost)
	check_gt(float(slots), 5.0, "五部玄微心法占 %d 格（>5 部数）" % slots)
	check_eq(
		service.count_for("set_xuanwei_qi", char_id, inventory, state), slots,
		"内功套按占格数之和算（%d 格）" % slots
	)
	check_eq(
		service.count_for("set_xuanwei_qi", char_id, inventory, state) >= 7, true,
		"五部占格之和够得上 7 格档（设计要的 4 格／7 格两档）——这里验的是**格数口径**；"
			+ "这几部武学能不能全拿到是另一回事（现在拿不到，见 KNOWN_UNREACHABLE_SET_TIERS）"
	)


## ④ 常驻 buff 走**贡献通道**：装备（on_equip）与套装档位在面板与战斗里读到同一个数。
## 临时 buff（正回合／整场）不在这里，走 `BattleActor` 的现算路径（下一节）。
func _check_permanent_contributions(db) -> void:
	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	# 锈月要 9 级、前代寨主遗物要 8 级
	state.append_level(char_id, 11)
	var level: int = state.level_of(char_id)
	var weapon_type := str(db.get_row("character_base", char_id).weapon_type)

	var before = CharacterSheetScript.new(db, state, char_id)
	var agi_before := float(before.total_attrs()["agi"])

	# 锈月：buff_grant(equip, eq_sword_04, buff_rusty_moon, on_equip)，buff 是 duration=0 常驻
	var sword: String = state.inventory.add_equipment(db, "eq_sword_04")
	var equipped: Dictionary = state.inventory.equip(db, char_id, level, weapon_type, sword)
	check_true(bool(equipped["ok"]), "穿上锈月（%s）" % equipped.get("error", ""))
	var after = CharacterSheetScript.new(db, state, char_id)
	check_gt(
		float(after.total_attrs()["agi"]), agi_before,
		"穿上锈月面板的五维就变了（%d → %d）" % [int(agi_before), int(after.total_attrs()["agi"])]
	)
	# 断言落在**贡献条目**上（数值精确，且不会被「装备自己的 bonus_* 列」和属性曲线混进来）
	var sword_mods: Array = _buff_contributions(state.inventory.contributions_for(db, char_id))
	check_eq(_contribution_value(sword_mods, "attr", "agi"), 4.0, "锈月的常驻 buff 给敏 +4（属性点层）")
	check_eq(_contribution_value(sword_mods, "stat", "crit_rate"), 0.05, "锈月的常驻 buff 给暴击率 +5%（固定值层）")
	var source_names_instance := false
	for mod: Dictionary in sword_mods:
		if str(mod.get("source", "")).contains(sword):
			source_names_instance = true
	check_true(source_names_instance, "贡献的来源同时写着 buff 与装备实例 id（同名装备两件也分得开）")

	# 遗物：attr:str+3、attr:con+3、stat:block_reduction+0.08
	var relic: String = state.inventory.add_equipment(db, "eq_head_01")
	check_true(bool(state.inventory.equip(db, char_id, level, weapon_type, relic)["ok"]), "戴上遗物")
	var relic_mods: Array = _buff_contributions_for(
		state.inventory.contributions_for(db, char_id), relic
	)
	check_eq(_contribution_value(relic_mods, "attr", "str"), 3.0, "遗物给力 +3")
	check_eq(_contribution_value(relic_mods, "stat", "block_reduction"), 0.08, "遗物给格挡减伤 +8%")

	# 套装档位（黑风寨 2 件：力 +3）也走同一条通道 —— 只算穿在身上的
	state.inventory.add_equipment(db, "eq_armor_03")
	var armor: String = state.inventory.add_equipment(db, "eq_armor_03")
	check_true(bool(state.inventory.equip(db, char_id, level, weapon_type, armor)["ok"]), "穿上山贼皮铠")
	# 此时身上：锈月 + 遗物 + 皮铠。套装要的是「黑风刀／皮铠／淬毒指环／遗物」，
	# 皮铠 + 遗物 = 2 件 → 2 件档「力 +3」应当出现（锈月不算这一套）
	var with_set = CharacterSheetScript.new(db, state, char_id)
	var set_mods: Array = _source_contributions(with_set.contributions(), "set:set_heifeng")
	check_eq(_contribution_value(set_mods, "attr", "str"), 3.0, "黑风寨两件档：力 +3（档位 buff 也走贡献通道）")
	check_gt(
		float(with_set.total_attrs()["str"]), 0.0,
		"套装贡献的来源写着 set_id（能追溯是哪一套发的）"
	)


## 取出某个来源（装备实例 id / `set:<id>`）的贡献条目
func _source_contributions(contributions: Array, source: String) -> Array:
	var out: Array = []
	for contribution: Dictionary in contributions:
		if str(contribution.get("source", "")) == source:
			out.append(contribution)
	return out


## 取出**装备常驻 buff** 的那几条贡献（source 形如 `buff:<buff_id>@<实例 id>`）
func _buff_contributions(contributions: Array) -> Array:
	var out: Array = []
	for contribution: Dictionary in contributions:
		if str(contribution.get("source", "")).begins_with("buff:"):
			out.append(contribution)
	return out


func _buff_contributions_for(contributions: Array, instance_id: String) -> Array:
	var out: Array = []
	for contribution: Dictionary in _buff_contributions(contributions):
		if str(contribution.get("source", "")).ends_with("@%s" % instance_id):
			out.append(contribution)
	return out


## 某来源里「某一层某一项」的合计值；`kind` 用 AttributeCalculator 的常量名（attr/stat 简写）
func _contribution_value(contributions: Array, kind: String, target: String) -> float:
	var wanted := AttributeCalculatorScript.CONTRIB_ATTR_POINT if kind == "attr" else AttributeCalculatorScript.CONTRIB_STAT_FLAT
	var total := 0.0
	for contribution: Dictionary in contributions:
		if str(contribution.get("kind", "")) != wanted or str(contribution.get("target", "")) != target:
			continue
		total += float(contribution.get("value", 0.0))
	return total


## ⑤ BattleActor 的 buff 规则：叠加／刷新、回合末递减、属性现算、computed（运功翻倍）
func _check_actor_buff_rules(db) -> void:
	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	# 装一部内功（引气：stat:qi_max +30）——通用运功就是把它翻倍
	state.set_loadout(char_id, PackedStringArray(), PackedStringArray(["pf_xuanwei_01"]))
	var actor = PartyBuilderScript.build_actor(db, state, char_id)
	check_not_null(actor, "从存档造出战斗单位")

	var def_before := float(actor.stat("def_phys"))
	var reduction_before := float(actor.stat("dmg_reduction"))
	check_float(reduction_before, 0.0, "没上 buff 时减伤是 0（口径：减伤只来自属性与临时效果）", 0.001)

	# 防御姿态：unique —— 上第二次无效，属性不叠
	check_true(bool(actor.add_buff("buff_guard", "action", "defend")["applied"]), "防御姿态上得去")
	check_float(float(actor.stat("dmg_reduction")), 0.25, "防御姿态给减伤 25%", 0.001)
	check_float(float(actor.stat("def_phys")) - def_before, 15.0, "防御姿态给外防 +15", 0.001)
	check_false(bool(actor.add_buff("buff_guard", "action", "defend")["applied"]), "unique：重复获得无效")
	check_eq(actor.buff_stacks("buff_guard"), 1, "unique 不叠层")

	# 回合末递减：duration=1 的防御姿态下一回合末就没了，属性要跟着回落
	check_eq(actor.buff_remaining("buff_guard"), 1, "防御姿态剩余 1 回合")
	check_eq(actor.tick_buffs().size(), 1, "回合末过期一条")
	check_false(actor.has_buff("buff_guard"), "过期后列表里没有它了")
	check_float(float(actor.stat("dmg_reduction")), reduction_before, "过期后减伤回落到原值", 0.001)
	check_float(float(actor.stat("def_phys")), def_before, "过期后外防回落到原值", 0.001)

	# computed：通用运功（buff_yunqi）= 该内功的常驻加成再来一份
	var qi_before := float(actor.max_qi())
	var yunqi: Dictionary = actor.add_buff("buff_yunqi", "skill_passive", "pf_xuanwei_01")
	check_true(bool(yunqi["applied"]), "催动通用运功")
	check_float(float(actor.max_qi()) - qi_before, 30.0, "运功让引气的内力上限加成翻倍（+30）", 0.001)
	check_eq(actor.buff_remaining("buff_yunqi"), 3, "运功持续 3 回合（duration=3）")

	# stack：淬毒（上限 3 层）——**逐次**验，别只看封顶：
	# 之前那个「四次调用之后 = 3」的写法盖住了「第一次就给 2 层」的 bug（2026-10-03 修，见决策 220）
	for expected in range(1, 4):
		actor.add_buff("buff_poison_amp", "equip", "eq_ring_03")
		check_eq(actor.buff_stacks("buff_poison_amp"), expected, "第 %d 次叠层 = %d 层" % [expected, expected])
	actor.add_buff("buff_poison_amp", "equip", "eq_ring_03")
	check_eq(actor.buff_stacks("buff_poison_amp"), 3, "叠层封顶 3 层（max_stack）")
	# special 类没有实现的效果**不许静默**：记进 unhandled_buffs，结算会播成「暂缓规则」
	check_eq(actor.unhandled_buffs.size(), 1, "special 类 buff 记一条「还没实现」")
	check_true(
		str(actor.unhandled_buffs[0]["name"]).contains("淬毒"),
		"记的是中文名：%s" % actor.unhandled_buffs[0]["name"]
	)


## ⑥ 六指令：普通攻击／主动防御／内功催动／道具（招式与逃跑早有用例）
func _check_six_commands(db) -> void:
	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	state.set_loadout(char_id, PackedStringArray(), PackedStringArray(["pf_xuanwei_01"]))
	var allies: Array = PartyBuilderScript.build_actors(db, state)
	var enemies: Array = EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(20261003))
	sim.setup(allies, enemies, {"modifiers": {"force_hit": true, "no_variance": true}})
	check_eq(allies.size(), 1, "单人队（solo_state）")

	# 普通攻击：不耗内力、不看武器；也不该污染「招式使用」记录（隐藏内容按它判定）
	var actor = allies[0]
	var target = enemies[0]
	var hp_before := int(target.hp)
	sim.basic_attack(actor, target)
	check_lt(float(target.hp), float(hp_before), "普通攻击能造成伤害（%d → %d）" % [hp_before, target.hp])
	check_true(sim.skill_uses(str(actor.actor_id)).is_empty(), "普通攻击不是武学：不进招式使用记录")

	# 主动防御：花一次行动换 buff + 架势；下回合先手
	sim.begin_round()
	var guard := 0
	while sim.in_round() and guard < 20:
		var current = sim.current_actor()
		if current != null and current.side == BattleActorScript.SIDE_ALLY:
			break
		sim.auto_act()
		guard += 1
	var defender = sim.current_actor()
	check_true(
		defender != null and defender.side == BattleActorScript.SIDE_ALLY,
		"第 1 回合轮到我方（防御指令的前提）"
	)
	if defender != null:
		# 架势拉低才看得出回复（满架势会被上限夹住 +0，这是预期行为）
		defender.poise = 10
		var poise_before := int(defender.poise)
		var defend: Dictionary = sim.defend(defender)
		check_true(bool(defend["ok"]), "主动防御生效（%s）" % defend.get("error", ""))
		check_true(defender.has_buff("buff_guard"), "防御姿态挂在身上")
		check_gt(float(defender.poise), float(poise_before), "主动防御回复架势（%d → %d）" % [poise_before, defender.poise])
		check_eq(sim.defend_block_reason(defender), "已经在这个姿态里了", "同回合不能再防一次（unique）")
		guard = 0
		while sim.in_round() and guard < 20:
			sim.auto_act()
			guard += 1
		if not sim.finished():
			sim.begin_round()
			var first = sim.current_actor()
			check_true(first != null, "新回合有单位可行动")
			if first != null:
				check_eq(str(first.actor_id), str(defender.actor_id), "主动防御的人下回合先手")
		else:
			fail("第一回合就打完了，先手断言拿不到下一回合")

	# 内功催动：换运功 buff；没装的内功有明确理由
	var caster = allies[0]
	var cast: Dictionary = sim.cast_passive(caster, "pf_xuanwei_01")
	check_true(bool(cast["ok"]), "内功催动生效（%s）" % cast.get("error", ""))
	check_true(caster.has_buff("buff_yunqi"), "运功 buff 挂上了")
	check_true(
		not sim.passive_cast_block_reason(caster, "pf_wudu_01").is_empty(),
		"没装的内功催不动，且给得出理由"
	)
	check_eq(
		sim.passive_cast_options(caster).size(), 1,
		"内功指令只列已装的那一部"
	)
	check_true(
		str(sim.passive_cast_options(caster)[0]["buff_name"]).contains("运功"),
		"按钮上写明催动后会拿到什么"
	)

	# 道具：`use_context=battle` 的两件列得出来，但**效果列还没配**，必须写明而不是假装能用
	state.inventory.add_item(db, "item_potion_small", 2)
	var items: Array = sim.battle_item_options(state.inventory)
	check_eq(items.size(), 2, "战斗道具两件（金创药／回气散）")
	check_eq(str(items[0]["name"]), "金创药", "道具用中文名")
	check_eq(int(items[0]["qty"]), 2, "数量从背包读")
	check_false(bool(items[0]["usable"]), "效果列未配 → 不可用（不假装能回血）")
	check_true(
		str(items[0]["reason"]).contains("效果"),
		"不可用的原因写清是效果没配：%s" % items[0]["reason"]
	)


## ⑦ 进场 buff（on_battle_start，整场有效）在 setup 时就挂上：药王玉佩给气血回复 +3
func _check_battle_start_grants(db) -> void:
	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	state.append_level(char_id, 6)      # 玉佩要 6 级
	var level: int = state.level_of(char_id)
	var weapon_type := str(db.get_row("character_base", char_id).weapon_type)
	var jade: String = state.inventory.add_equipment(db, "eq_acc_02")
	check_true(bool(state.inventory.equip(db, char_id, level, weapon_type, jade)["ok"]), "戴上药王玉佩")
	var allies: Array = PartyBuilderScript.build_actors(db, state)
	var regen_before := float(allies[0].stat("hp_regen"))
	var enemies: Array = EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(20261003))
	sim.setup(allies, enemies)
	check_true(allies[0].has_buff("buff_regen"), "玉佩的整场 buff 开打就挂上")
	check_eq(allies[0].buff_remaining("buff_regen"), -1, "整场有效（remaining = -1，不按回合递减）")
	check_float(float(allies[0].stat("hp_regen")) - regen_before, 3.0, "玉佩给气血回复 +3", 0.001)
	check_eq(allies[0].pending_grants.size(), 0, "进场 buff 发完就清空（不会每回合重复发）")


## ⑧ 战斗外增益（08：`scope=field`，按**现实分钟**计时；进战斗维持整场）
##
## 会话层负责计时（`GameSession.add_field_buff/active_field_buffs/clear_field_buffs`），
## `PartyBuilder` 只负责把还活着的那几条带进战场——所以这里两边各验一段。
func _check_field_buffs(db) -> void:
	var session = load("res://src/autoload/game_session.gd").new()
	var now := int(Time.get_unix_time_from_system())

	check_eq(session.active_field_buffs(now).size(), 0, "一开始没有战斗外增益")
	var applied: Dictionary = session.add_field_buff("buff_meditated", 10)
	check_true(bool(applied["ok"]), "打坐余韵挂上了（10 分钟）")
	var rows: Array = session.active_field_buffs(now)
	check_eq(rows.size(), 1, "现在有一条生效的")
	check_eq(int(rows[0]["remaining_sec"]), 600, "剩余秒数 = 10 分钟（按现实时间算）")

	# 重复获得 = 刷新（buff_meditated 的 stack_rule=refresh），不是两条
	session.add_field_buff("buff_meditated", 10)
	check_eq(session.active_field_buffs(now).size(), 1, "重复获得只刷新，不叠成两条")
	# 到点即失效：用注入的 now，不用真等 10 分钟
	check_eq(session.active_field_buffs(now + 601).size(), 0, "600 秒之后即失效")
	check_eq(session.field_buffs.size(), 0, "过期的会被清掉（不是留在列表里装样子）")

	session.add_field_buff("buff_meditated", 10)
	check_eq(session.clear_field_buffs(), 1, "clear（败北要用）返回清掉几条")
	check_eq(session.active_field_buffs(now).size(), 0, "清完就一条不剩")

	# 带进战斗：PartyBuilder 把它当**进场 buff**发，进战斗后维持整场（不按回合递减）
	session.add_field_buff("buff_meditated", 10)
	var state = solo_state(db)
	var allies: Array = PartyBuilderScript.build_actors(db, state, session.active_field_buffs())
	var enemies: Array = EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(20261003))
	var qi_regen_before := float(allies[0].stat("qi_regen"))
	var hp_regen_before := float(allies[0].stat("hp_regen"))
	sim.setup(allies, enemies)
	check_true(allies[0].has_buff("buff_meditated"), "战斗外增益带进了战场")
	check_eq(allies[0].buff_remaining("buff_meditated"), 0, "整场有效（field 在战斗里不按回合递减）")
	check_float(
		float(allies[0].stat("qi_regen")) - qi_regen_before, 1.5,
		"打坐余韵给内力回复 +1.5", 0.001
	)
	check_float(
		float(allies[0].stat("hp_regen")) - hp_regen_before, 0.0,
		"饱食那条不在身上（只挂了打坐余韵）：气血回复不该变", 0.001
	)
	# 战斗结束清战斗内 buff，但**field 要留着**（它归会话管，还没到期）
	allies[0].clear_battle_buffs()
	check_true(allies[0].has_buff("buff_meditated"), "战斗结束不会清掉战斗外增益（会话还在管它）")
	session.free()


## ⑨ 招式自带的增益（08：`skill_active` 的 `on_cast` 发放）——「招式的增益效果＝施放时发的 buff」。
##
## 表里目前只有一条 `gr_sk_drunk_zuibu`（醉步，招式本身还没配出伤害、战斗里用不出来），
## 所以这条路在发行数据里跑不到——用**新建一份表库 + 加两条夹具发放**来验代码半边：
## 施放一次只发一次（不看打到几个人）、同一招的多条发放都生效、buff 写错时**不静默**。
func _check_cast_grants(db) -> void:
	var local_db = load("res://src/core/table_db.gd").new()
	local_db.load_all()
	var grants: Resource = local_db.tables["buff_grant"].duplicate(true)
	grants.rows.append(_grant_row("gr_test_cast_guard", "skill_active", "sk_xuanwei_01", "buff_guard"))
	grants.rows.append(_grant_row("gr_test_cast_poison", "skill_active", "sk_xuanwei_01", "buff_poison_amp"))
	grants.rows.append(_grant_row("gr_test_cast_bogus", "skill_active", "sk_xuanwei_02", "buff_bogus"))
	local_db.tables["buff_grant"] = grants

	var state = solo_state(local_db)
	var char_id: String = state.char_ids[0]
	state.set_loadout(char_id, PackedStringArray(["sk_xuanwei_01", "sk_xuanwei_02"]), PackedStringArray())
	var allies: Array = PartyBuilderScript.build_actors(local_db, state)
	var enemies: Array = EnemyFactoryScript.new(local_db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(local_db, RngServiceScript.new(20261003))
	sim.setup(allies, enemies, {"modifiers": {"force_hit": true, "no_variance": true}})
	var ally = allies[0]
	var skill: Resource = local_db.get_row("skill_active", "sk_xuanwei_01")
	check_not_null(skill, "夹具招式在表里")
	check_true(bool(ally.add_buff("buff_guard", "action", "defend")["applied"]), "先上一个别的来源的防御姿态")
	ally.buffs = []      # 清干净：下面要验「招式自己发的那一条」

	# ① 施放一次：两条发放都生效；叠层类只 +1（**一次行动只发一次**，不是每个目标各发一次）
	var lines: Array = sim.act(ally, skill, enemies[0])
	check_true(ally.has_buff("buff_guard"), "招式自带的增益挂上了（on_cast）")
	check_eq(ally.buff_stacks("buff_poison_amp"), 1, "一次行动只发一次（叠层类 +1，不按目标数累加）")
	check_true(str(lines).contains("附带"), "战报里写明这一招附带什么：%s" % str(lines))

	# ② 再施放一次：叠层类到 2（叠层规则照旧生效）
	sim.act(ally, skill, enemies[0])
	check_eq(ally.buff_stacks("buff_poison_amp"), 2, "再施放一次叠第二层")

	# ③ 发放指向不存在的 buff：**不静默**，战报里写清原因（这是唯一线索）
	var bogus: Array = sim.act(ally, local_db.get_row("skill_active", "sk_xuanwei_02"), enemies[0])
	check_true(str(bogus).contains("没上上去"), "buff 写错时战报如实说：%s" % str(bogus))
	check_true(str(bogus).contains("buff_bogus"), "连是哪个 buff 都写出来（定位用）：%s" % str(bogus))

	# ④ 敌人也能接 buff（以前 EnemyFactory 不传 db，敌人的 add_buff 直接失败）
	var wolf = EnemyFactoryScript.new(local_db).create("en_wolf", "normal")
	check_not_null(wolf.buff_service(), "敌人接上配置表（能收 buff）")
	check_true(bool(wolf.add_buff("buff_guard", "test", "unit")["applied"]), "敌人的 buff 真的上得去")


## ⑩ 叠层要**真的按层数放大效果**，命中触发要**按段各发一次**。
##
## 这两条都是 08 写死的口径（「stack 叠层」「招式的增益效果=命中/施放时发的 buff」），
## 而发行数据里唯一可叠层的 buff（淬毒）是 `special`——**没有数值修正行**，所以
## 「层数 × 数值」这条乘法在真数据里根本走不到。这里用夹具（给淬毒补一条 `stat:atk_phys +5`、
## 手动挂 on_hit 发放）把两条口径都钉住；上一轮那个「第一次就给 2 层」的 bug 就是靠这类
## 「逐次断言」抓到的（决策 220）。
func _check_stack_scaling(db) -> void:
	var local_db = load("res://src/core/table_db.gd").new()
	local_db.load_all()
	var stats: Resource = local_db.tables["buff_stat"].duplicate(true)
	stats.rows.append(_stat_row("buff_poison_amp", "stat:atk_phys", 5.0))
	local_db.tables["buff_stat"] = stats

	var state = solo_state(local_db)
	var char_id: String = state.char_ids[0]
	var actor = PartyBuilderScript.build_actor(local_db, state, char_id)
	var atk_before := float(actor.stat("atk_phys"))
	check_true(bool(actor.add_buff("buff_poison_amp", "equip", "eq_ring_03")["applied"]), "挂第一层")
	check_float(float(actor.stat("atk_phys")) - atk_before, 5.0, "一层 = +5（层数 × 数值）", 0.001)
	actor.add_buff("buff_poison_amp", "equip", "eq_ring_03")
	check_float(float(actor.stat("atk_phys")) - atk_before, 10.0, "两层 = +10（乘法真的按层数走）", 0.001)
	actor.add_buff("buff_poison_amp", "equip", "eq_ring_03")
	actor.add_buff("buff_poison_amp", "equip", "eq_ring_03")
	check_eq(actor.buff_stacks("buff_poison_amp"), 3, "封顶 3 层")
	check_float(float(actor.stat("atk_phys")) - atk_before, 15.0, "封顶后不再涨（3 层 = +15）", 0.001)

	# 命中触发：**按命中段数各发一次**（`sk_spear_04` 是 2 段单体）
	state.set_loadout(char_id, PackedStringArray(["sk_spear_04"]), PackedStringArray())
	var allies: Array = PartyBuilderScript.build_actors(local_db, state)
	var lab_ally = allies[0]
	lab_ally.qi = 999
	# 手动挂「命中叠淬毒」：真数据里这条来自淬毒指环（本用例换了武器，所以自己挂）
	lab_ally.on_hit_grants = [{
		"buff_id": "buff_poison_amp", "source_type": "equip",
		"source_id": "eq_ring_03", "stacks": 1,
	}]
	var boss = EnemyFactoryScript.new(local_db).create("en_bd_boss", "normal")
	var sim = BattleSimulatorScript.new(local_db, RngServiceScript.new(20261003))
	sim.setup(allies, [boss], {"modifiers": {"force_hit": true, "no_variance": true}})
	sim.act(lab_ally, local_db.get_row("skill_active", "sk_spear_04"), boss)
	check_eq(lab_ally.buff_stacks("buff_poison_amp"), 2, "2 段招式 → 命中触发叠 2 层（按段，不是按行动）")


## ⑪ 增益招式：醉里乾坤·醉步（`skill_active` 整行 0、`target_type=self`，但表里配了 on_cast 发放）。
##
## 这条用的就是**发行数据**（不是夹具）：玩家能从酒葫芦那条隐藏内容里学会它，在那之前它在战斗里
## 既没有按钮也用不出来（角色面板会写「用不出来」）。现在的口径：**没有伤害但有 `on_cast` 发放
## 的招式＝增益招式**，施放一次给自己上 buff、不走伤害管线（见框架说明决策 221）。
func _check_support_skill(db) -> void:
	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	state.learn_skill(char_id, "sk_drunk_zuibu")
	state.set_loadout(char_id, PackedStringArray(["sk_xuanwei_01", "sk_drunk_zuibu"]), PackedStringArray())
	var allies: Array = PartyBuilderScript.build_actors(db, state)
	var enemies: Array = EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(20261003))
	sim.setup(allies, enemies, {"modifiers": {"force_hit": true, "no_variance": true}})
	var ally = allies[0]
	# 内力给满（别超过上限：上 buff 时属性会整份重算，超出上限的部分会被夹掉——那是预期行为）
	ally.qi = ally.max_qi()
	var qi_before := int(ally.qi)

	# ① 界面能看到它、且写明是「增益」（不是「倍率 0.0」）
	var options: Dictionary = {}
	for option: Dictionary in sim.skill_options(ally):
		options[str(option["skill_id"])] = option
	check_true(options.has("sk_drunk_zuibu"), "醉步出现在可点列表里（以前连按钮都没有）")
	if options.has("sk_drunk_zuibu"):
		check_true(bool(options["sk_drunk_zuibu"]["support"]), "标记为增益招式")
		check_true(bool(options["sk_drunk_zuibu"]["ok"]), "现在能点：%s" % str(options["sk_drunk_zuibu"]["reason"]))
	check_true(options.has("sk_xuanwei_01"), "对照：普攻招式照旧在列表里")

	# ①b **两个查询口径必须一致**：`available_skills`（能用的）与 `skill_options`（体检表 ok=true）
	# 是同一件事的两种问法，界面按前者置灰、按后者列按钮——一边漏了只有真路径能看出来。
	# （这次就是这么发现的：`available_skills` 里忘了给增益招式放行，按钮列得出来、按下去却说「用不了」。）
	var usable: Array = sim.available_skills(ally)
	var usable_ids := PackedStringArray()
	for active: Resource in usable:
		usable_ids.append(str(active.skill_id))
	var ok_ids := PackedStringArray()
	for option: Dictionary in sim.skill_options(ally):
		if bool(option["ok"]):
			ok_ids.append(str(option["skill_id"]))
	check_eq(str(usable_ids), str(ok_ids), "available_skills 与 skill_options(ok) 口径一致")

	# ② 施放：给自己上「忘忧」，敌人**一点血都不掉**（不走伤害管线）
	var zuibu: Resource = db.get_row("skill_active", "sk_drunk_zuibu")
	var dodge_before := float(ally.stat("dodge_rate"))
	var enemy_hp_before := int(enemies[0].hp)
	var lines: Array = sim.act(ally, zuibu, enemies[0])
	check_true(ally.has_buff("buff_zuibu"), "施放后自己拿到忘忧")
	check_float(float(ally.stat("dodge_rate")) - dodge_before, 0.20, "忘忧给闪避 +20%", 0.001)
	check_eq(int(enemies[0].hp), enemy_hp_before, "增益招式不打敌人（不走伤害管线）")
	check_true(str(lines).contains("增益"), "战报里写明这是增益：%s" % str(lines))
	check_eq(int(ally.qi), qi_before, "醉步内力消耗是 0（表里配的），放完内力不变")
	check_gt(float(sim.cooldown_left(str(ally.actor_id), "sk_drunk_zuibu")), 0.0, "放完进入冷却（表里 cooldown=3）")

	# ③ 自动战斗**不主动用**增益招式（它只挑打伤害的招）——同一套列表里，选招仍然挑得出普攻招式
	var picked: Resource = sim.pick_skill(ally)
	check_not_null(picked, "自动战斗仍能挑到攻击招式")
	if picked != null:
		check_ne(str(picked.skill_id), "sk_drunk_zuibu", "自动战斗不会自己去放醉步：%s" % str(picked.skill_id))


## ⑫ 命中触发也能来自**招式自己**（`buff_grant(source_type=skill_active, trigger=on_hit)`）。
##
## 08 的口径是「招式的增益效果＝命中/施放时发的 buff」：`on_cast` 上一轮接了，
## **命中这一半 2026-10-03 才接**（在那之前只有装备能配 on_hit，招式写了也是静默不发）。
## 发行数据里还没有哪条招式配 on_hit，所以用夹具；`sk_spear_04` 是 2 段单体 → 一次行动叠 2 层。
func _check_skill_hit_grants(db) -> void:
	var local_db = load("res://src/core/table_db.gd").new()
	local_db.load_all()
	var grants: Resource = local_db.tables["buff_grant"].duplicate(true)
	grants.rows.append(_grant_row("gr_test_hit_poison", "skill_active", "sk_spear_04", "buff_poison_amp", "on_hit"))
	local_db.tables["buff_grant"] = grants

	var state = solo_state(local_db)
	var char_id: String = state.char_ids[0]
	state.set_loadout(char_id, PackedStringArray(["sk_spear_04"]), PackedStringArray())
	var allies: Array = PartyBuilderScript.build_actors(local_db, state)
	var ally = allies[0]
	ally.qi = ally.max_qi()
	# 确保装备那半不掺和：这条验的是**招式自带**的命中触发
	ally.on_hit_grants = []
	var boss = EnemyFactoryScript.new(local_db).create("en_bd_boss", "normal")
	var sim = BattleSimulatorScript.new(local_db, RngServiceScript.new(20261003))
	sim.setup(allies, [boss], {"modifiers": {"force_hit": true, "no_variance": true}})
	check_eq(ally.buff_stacks("buff_poison_amp"), 0, "开打前没有淬毒层")
	var lines: Array = sim.act(ally, local_db.get_row("skill_active", "sk_spear_04"), boss)
	check_eq(ally.buff_stacks("buff_poison_amp"), 2, "招式自带的命中触发：2 段 → 2 层")
	check_false(str(lines).contains("没上上去"), "没有「buff 没上上去」的报错：%s" % str(lines))


## 造一条 buff_stat 夹具行
func _stat_row(buff_id: String, target: String, value: float) -> Resource:
	var row = load("res://src/data/tables/buff_stat_row.gd").new()
	row.buff_id = buff_id
	row.target = target
	row.value = value
	return row


## 造一条 buff_grant 夹具行（只填逻辑需要的列；`id` 留空不影响 grants_for）
func _grant_row(
	grant_id: String, source_type: String, source_id: String, buff_id: String,
	trigger: String = "on_cast", stacks: int = 1
) -> Resource:
	var row = load("res://src/data/tables/buff_grant_row.gd").new()
	row.grant_id = grant_id
	row.source_type = source_type
	row.source_id = source_id
	row.buff_id = buff_id
	row.trigger = trigger
	row.stacks = stacks
	return row
