## 天赋的 `rule:` 效果（设计 12 §五：「`rule:` 是给那些表达不成数值的东西用的……
## 新增一个 `rule:` 目标 = 改一处代码 + 加一行表」）。
##
## 为什么单开一个用例：这些效果一行挂在 `talent_effect` 上，**没有任何东西会报错**——
## 玩家花满 5 个点选了天赋、面板上的 `attr:`／`stat:` 也真的涨了，但「首回合先手」
## 「判定 +1」「图鉴翻倍」「买东西便宜一成」这些**全都没发生**。
## 2026-10-04 之前只有 `start_money` 有人读，其余九条全是死数据（本轮接上八条）。
##
## 这里逐条钉「选了天赋 → 那个系统真的变了」，并且钉**不该受影响的地方没变**
## （例：三寸不烂之舌只加文学，医术判定不动）。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const EventCheckServiceScript := preload("res://src/core/event_check_service.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const TalentServiceScript := preload("res://src/core/talent_service.gd")
const ShopServiceScript := preload("res://src/core/shop_service.gd")
const BattleActorScript := preload("res://src/core/battle_actor.gd")
const DamageResolverScript := preload("res://src/core/damage_resolver.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")

const CHAR_ID := "scholar_fallen"
const SEED := 20261004


func suite_name() -> String:
	return "天赋规则效果"


func run() -> void:
	var db = get_db()
	_check_table_is_consumed(db)
	_check_check_bonus(db)
	_check_check_difficulty(db)
	_check_codex_multiplier(db)
	_check_shop_discount(db)
	_check_first_strike(db)
	_check_skill_damage(db)


## `rule:` 的每一行都要有人在读——**这条是防「又添一条没人读的规则」的**。
## 还没接的四条写在 `UNCONSUMED` 里（各自等设计或等实现，见 `待策划确认.md` Q75）。
const UNCONSUMED := {
	"mastery_gain": "熟练度成长 +30% 要小数进度（熟练度是 0~10 的整数，+30% 落不到整点上），要设计定口径",
	"bonus_skill": "天生剑骨给的是「额外一部剑法」——哪一部设计没点名",
	"defeat_heal_bonus": "九命的「败北恢复更高」：现在败北本来就是回满气血，这条得先定语义",
}


func _check_table_is_consumed(db) -> void:
	var corpus := ""
	for path: String in ["src/core/talent_service.gd", "src/core/character_sheet.gd",
			"src/core/event_check_service.gd", "src/core/shop_service.gd",
			"src/core/battle_simulator.gd", "src/core/damage_resolver.gd",
			"src/core/skill_loadout.gd", "src/core/creation_service.gd"]:
		corpus += FileAccess.get_file_as_string("res://" + path)
	var ids: Dictionary = {}
	for row: Resource in db.rows("talent_effect"):
		var target := str(row.target)
		if target.begins_with("rule:"):
			ids[target.substr("rule:".length())] = true
	check_gt(float(ids.size()), 5.0, "表里有足够多的 rule: 效果（%d 条）" % ids.size())
	var unread := PackedStringArray()
	for rule_id: String in ids.keys():
		if UNCONSUMED.has(rule_id):
			continue
		if rule_id == "event_check_bonus":
			continue    # 通用加成与「只加某一门」都是按前缀拼出来的，见 TalentService.check_bonus
		if rule_id.begins_with("event_check_bonus_"):
			continue    # `event_check_bonus_<技能>` 由同一个模板拼，语料里当然找不到字面量
		if not corpus.contains(rule_id):
			unread.append(rule_id)
	check_eq(
		unread.size(), 0,
		"每条 rule: 都有人在读（没人读的：%s）——要么接上，要么写进 UNCONSUMED 并说明等谁"
			% "、".join(unread)
	)


## 判定值上的加成：见多识广（所有判定 +1）／三寸不烂之舌（文学 +2）／悬壶（医术 +2）
func _check_check_bonus(db) -> void:
	var state = solo_state(db)
	var plain = CharacterSheetScript.new(db, state, CHAR_ID)
	var wenxue := int(plain.event_check_value("skill:wenxue")["value"])
	var yishu := int(plain.event_check_value("skill:yishu")["value"])
	var attr_check := int(plain.event_check_value("attr:int")["value"])

	state.talent_picks[CHAR_ID] = ["tal_jian_duo"]
	var broad = CharacterSheetScript.new(db, state, CHAR_ID)
	check_eq(
		int(broad.event_check_value("skill:wenxue")["value"]), wenxue + 1,
		"见多识广：所有判定 +1（文学）",
	)
	check_eq(
		int(broad.event_check_value("skill:yishu")["value"]), yishu + 1,
		"见多识广：所有判定 +1（医术也算）",
	)
	check_eq(
		int(broad.event_check_value("attr:int")["value"]), attr_check + 1,
		"见多识广：属性判定同样 +1",
	)

	state.talent_picks[CHAR_ID] = ["tal_san_cun"]
	var wen_holder = CharacterSheetScript.new(db, state, CHAR_ID)
	check_eq(
		int(wen_holder.event_check_value("skill:wenxue")["value"]), wenxue + 2,
		"三寸不烂之舌：文学 +2",
	)
	check_eq(
		int(wen_holder.event_check_value("skill:yishu")["value"]), yishu,
		"它**只**加文学，医术不动",
	)


## 江湖百晓生：判定门槛 −2（设计 12 §六「让不想打的人也能推内容」）
func _check_check_difficulty(db) -> void:
	var state = solo_state(db)
	var service = EventCheckServiceScript.new(db, state, RngServiceScript.new(SEED))
	var row: Resource = db.get_row("event_check", "ev_shen_rescue")
	check_eq(service.effective_difficulty(row), int(row.difficulty), "没天赋时门槛就是表里那个数")
	state.talent_picks[CHAR_ID] = ["tal_bai_xiao"]
	var discounted = EventCheckServiceScript.new(db, state, RngServiceScript.new(SEED))
	check_eq(
		discounted.effective_difficulty(row), maxi(0, int(row.difficulty) - 2),
		"江湖百晓生：门槛 −2",
	)
	# 门槛压到 0 就不再往下压（负数会让硬判定变成永远能做，那是另一套语义）
	var hard: Resource = db.get_row("event_check", "ev_shed_trap")
	for i in range(3):
		state.talent_picks[CHAR_ID] = ["tal_bai_xiao"]
	check_eq(
		EventCheckServiceScript.new(db, state, RngServiceScript.new(SEED)).effective_difficulty(hard),
		maxi(0, int(hard.difficulty) - 2),
		"门槛只减到 0，不写负数",
	)


## 藏书癖：图鉴奖励翻倍（设计 12 §六「把收集癖变成正经 build」）
func _check_codex_multiplier(db) -> void:
	var state = solo_state(db)
	# 收集 5 部武学 = 一档奖励（`growth_const.codex_step`）
	var learned := 0
	for row: Resource in db.rows("skill_base"):
		if learned >= 5:
			break
		if state.learn_skill(CHAR_ID, str(row.skill_id)):
			learned += 1
	check_eq(learned, 5, "先学会 5 部武学（夹具）")
	var plain = CharacterSheetScript.new(db, state, CHAR_ID)
	var before: Dictionary = plain.codex_bonus()
	check_eq(int(before["times"]), 1, "5 部 = 一档")
	check_eq(int(before["per_tier"]), 1, "没天赋时每档七项各 +1")
	# **先把这个数取下来**：`CharacterSheet` 持有的是同一份 state，后面改了天赋它就跟着变
	var wu_plain := int(plain.naked_attrs().get("wu", 0))

	state.talent_picks[CHAR_ID] = ["tal_cang_shu"]
	var doubled = CharacterSheetScript.new(db, state, CHAR_ID)
	var after: Dictionary = doubled.codex_bonus()
	check_eq(int(after["per_tier"]), 2, "藏书癖：每档翻倍（+2）")
	check_eq(int(after["bonus"]), 2, "这一档合计 +2")
	# 翻倍必须真的进属性合成（不然只是面板上的字）
	var wu_doubled := int(doubled.naked_attrs().get("wu", 0))
	check_eq(wu_doubled - wu_plain, 1, "翻倍真的进属性（悟性多涨 1：%d → %d）" % [wu_plain, wu_doubled])
	# 门槛那一侧也要用同一个倍数（面板够、门槛不够是踩过的坑）
	check_true(
		TalentServiceScript.rule_value(db, state, CHAR_ID, "codex_bonus_multiplier", 0.0) == 2.0,
		"表里的值是倍数（2 = 翻倍），不是增量",
	)


## 过日子：买东西便宜一成（`rule:shop_buy_price 0.10`）
func _check_shop_discount(db) -> void:
	var state = solo_state(db)
	var shop = ShopServiceScript.new(db, state)
	# `stock_row()` 要的是**建筑 id**（它自己按 `building_def.stock_group` 换算到货架组）
	var building := "bld_smith"
	var stock_group := str(db.get_row("building_def", building).stock_group)
	var item := ""
	for row: Resource in db.rows("shop_stock"):
		if str(row.shop_id) == stock_group and int(row.buy_price) > 0:
			item = str(row.item_id)
			break
	check_true(not item.is_empty(), "铁匠铺有在卖的货（夹具）")
	if item.is_empty():
		return
	var base := shop.buy_price(building, item)
	check_gt(float(base), 0.0, "原价 %d" % base)
	state.talent_picks[CHAR_ID] = ["tal_guo_ri"]
	var discounted = ShopServiceScript.new(db, state)
	check_eq(
		discounted.buy_price(building, item), maxi(1, int(floor(float(base) * 0.9))),
		"过日子：买入价便宜一成（%d → %d）" % [base, discounted.buy_price(building, item)],
	)
	check_eq(
		discounted.sell_price(building, item), shop.sell_price(building, item),
		"卖出价不受影响（改它等于把玩家的钱变少）",
	)


## 先发制人：首回合必定先手（设计 12 §六）
func _check_first_strike(db) -> void:
	var slow = _actor("ally_slow", BattleActorScript.SIDE_ALLY, 5.0, 200)
	var fast = _actor("enemy_fast", BattleActorScript.SIDE_ENEMY, 30.0, 200)
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([slow], [fast], {"modifiers": {"force_hit": true, "no_variance": true}})
	var plain_order: Array = sim.turn_order([slow], [fast])
	check_eq(str(plain_order[0].actor_id), "enemy_fast", "没天赋时身法快的先手")
	slow.talent_rules = {"first_round_priority": 1.0}
	var with_talent: Array = sim._apply_talent_first_strike(sim.turn_order([slow], [fast]), 1)
	check_eq(str(with_talent[0].actor_id), "ally_slow", "先发制人：第 1 回合他抢到最前")
	var round_two: Array = sim._apply_talent_first_strike(sim.turn_order([slow], [fast]), 2)
	check_eq(str(round_two[0].actor_id), "enemy_fast", "只在第 1 回合（后面照身法走）")


## 天生武胆：招式伤害 +15%（设计 12 §六；花满 3 点、自带内伤抗性 −20% 的代价）
func _check_skill_damage(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var attacker = _actor("attacker", BattleActorScript.SIDE_ALLY, 20.0, 100)
	var defender = _actor("defender", BattleActorScript.SIDE_ENEMY, 10.0, 300)
	attacker.level = 10
	defender.level = 5
	attacker.stats["atk_phys"] = 100.0
	attacker.stats["crit_rate"] = 0.0
	attacker.stats["hit_rate"] = 0.9
	defender.stats["def_phys"] = 60.0
	defender.stats["dodge_rate"] = 0.0
	var skill: Resource = db.get_row("skill_active", "sk_xuanwei_03")
	check_not_null(skill, "黄金算式那条招式还在（夹具）")
	if skill == null:
		return
	var mods := {"force_hit": true, "no_variance": true}
	var plain: Dictionary = resolver.resolve(attacker, defender, skill, mods)
	attacker.talent_rules = {"skill_damage": 0.15}
	var boosted: Dictionary = resolver.resolve(attacker, defender, skill, mods)
	check_float(boosted["detail"]["talent_skill_factor"], 1.15, "乘区 = 1 + 15%", 0.0001)
	check_float(plain["detail"]["talent_skill_factor"], 1.0, "没天赋时是 1.0")
	check_eq(
		int(boosted["raw_damage"]), int(floor(float(plain["raw_damage"]) * 1.15)),
		"伤害按 +15%% 算（%d → %d）" % [int(plain["raw_damage"]), int(boosted["raw_damage"])],
	)


## 手搓一个战斗单位（只要身法／气血这两样就够验顺序）
func _actor(actor_id: String, side: int, speed: float, hp: int):
	var actor = BattleActorScript.new()
	actor.actor_id = actor_id
	actor.display_name = actor_id
	actor.side = side
	actor.level = 5
	actor.base_accuracy = 0.0
	actor.stats = {
		"hp_max": float(hp), "qi_max": 100.0,
		"atk_phys": 20.0, "atk_qi": 0.0, "def_phys": 10.0, "def_qi": 0.0,
		"speed": speed, "hit_rate": 0.9, "dodge_rate": 0.0,
		"crit_rate": 0.0, "crit_dmg": 0.0,
		"pen_rate": 0.0, "block_rate": 0.0, "block_reduction": 0.0, "dmg_reduction": 0.0,
	}
	actor.refill()
	return actor
