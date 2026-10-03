## 从存档构造战斗单位。
##
## 统一入口的意义：等级、加点、装备加成都在这里算进去，
## 所以「穿上装备」「加了点」一定影响战斗，而不只是面板上好看。
class_name PartyBuilder
extends RefCounted

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
const BuffServiceScript := preload("res://src/core/buff_service.gd")


static func build_actor(db, state, char_id: String, field_buffs: Array = []):
	var sheet = CharacterSheetScript.new(db, state, char_id)
	if not sheet.valid():
		push_error("[PartyBuilder] character_base 里没有角色 %s" % char_id)
		return null
	var actor = BattleActorScript.from_character(
		db,
		sheet.template,
		sheet.level,
		sheet.allocations(),
		sheet.contributions()
	)
	if actor == null:
		return null
	# 战斗里能用哪些招，由**装配**决定，不再是模板里有什么就全会
	actor.skills = sheet.loadout().battle_skill_ids()
	# 兜底：从没铺过装配的老档（已学为空）仍然按模板起始武学打，免得整场无招可出
	if actor.skills.is_empty() and sheet.loadout().learned_ids().is_empty():
		actor.skills = sheet.template.skill_ids()
	if state != null:
		for skill_id: String in state.masteries_of(char_id):
			actor.skill_mastery[skill_id] = state.mastery_of(char_id, skill_id)
	# 战斗外气血（v11）：带着上一场的伤进这一场；没有记录就是满血
	var saved_hp: int = state.current_hp_of(char_id)
	if saved_hp >= 0:
		actor.hp = clampi(saved_hp, 1, actor.max_hp())
	# 招式绑定武器类型：用身上那把武器的类型做限制（没武器就只能用通用招式）
	var weapon: Resource = equipped_weapon(db, state, char_id)
	actor.tags["weapon_type"] = str(weapon.weapon_type) if weapon != null else ""
	# 武器系别决定实际系别（05 文档：具体系别以每件装备自己的 element 为准）
	actor.tags["weapon_element"] = str(weapon.element) if weapon != null else ""
	# 内功指令要用「装了哪几部」；临时 buff 的属性重算也在这里把输入交给它
	if sheet.loadout() != null:
		actor.passives = sheet.loadout().passive_ids()
	actor.setup_recompute(
		db, AttributeCalculatorScript.new(db), sheet.level,
		sheet.base_attrs(), sheet.allocations(), sheet.contributions()
	)
	# 进场 buff：装备的 on_equip／on_battle_start 里**非常驻**的那些（常驻的已进贡献列表，
	# 再发一次就重复计算了）；战斗外增益（`field`）也走这里带进战斗。
	actor.pending_grants = equip_grants(db, state, char_id, ["on_equip", "on_battle_start"])
	# 战斗外增益（08）：会话里还活着的带进战场，**进战斗后维持整场**（不按回合递减）
	for entry: Dictionary in field_buffs:
		actor.pending_grants.append({
			"buff_id": str(entry.get("buff_id", "")), "source_type": "field",
			"source_id": str(entry.get("source_id", "")), "stacks": maxi(1, int(entry.get("stacks", 1))),
		})
	# 命中时触发的装备特效（例：淬毒指环命中叠一层淬毒）——由模拟器在命中后发
	actor.on_hit_grants = equip_grants(db, state, char_id, ["on_hit"])
	return actor


## 某角色身上装备发出的 buff 条目（带 `trigger`，由调用方按时机施加）。
##
## 只收**非常驻**的：常驻 buff（duration=0，装备 on_equip 与套装档位）在 `contributions()`
## 里就算进属性了，再发一次会重复。这里收的是「整场有效（-1）」与将来的战斗外增益。
static func equip_grants(db, state, char_id: String, triggers: Array) -> Array:
	var out: Array = []
	if state == null or state.inventory == null:
		return out
	var service = BuffServiceScript.new(db)
	for instance_id: String in state.inventory.equipped_instances(db, char_id):
		var base_id: String = state.inventory.base_of(instance_id)
		for trigger: String in triggers:
			for grant: Resource in service.grants_for("equip", base_id, trigger):
				var buff_id := str(grant.buff_id)
				if service.is_permanent(buff_id):
					continue
				out.append({
					"buff_id": buff_id, "source_type": "equip", "trigger": trigger,
					"source_id": base_id, "stacks": maxi(1, int(grant.stacks)),
				})
	return out


## 角色当前装备的武器本体（equip_base 行）；没装备返回 null。
static func equipped_weapon(db, state, char_id: String):
	if state == null or state.inventory == null:
		return null
	for instance_id: String in state.inventory.equipped_instances(db, char_id):
		var base: Resource = db.get_row("equip_base", state.inventory.base_of(instance_id))
		if base != null and str(base.slot) == "weapon":
			return base
	return null


## 角色当前装备的武器类型；没装备返回空串。
static func equipped_weapon_type(db, state, char_id: String) -> String:
	var weapon: Resource = equipped_weapon(db, state, char_id)
	return str(weapon.weapon_type) if weapon != null else ""


static func build_actors(db, state, field_buffs: Array = []) -> Array:
	var out: Array = []
	if state == null:
		return out
	for char_id: String in state.char_ids:
		var actor = build_actor(db, state, char_id, field_buffs)
		if actor != null:
			out.append(actor)
	return out
