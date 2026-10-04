## 创建角色（设计 13，0.17.0）：两条路——「使用模板」与「不使用模板」。
##
##   使用模板：选出身（`origin_def` 的 5 张卡，直接复用 `character_base` 的 5 行）→ 选天赋 → 起名
##   不使用模板：分七维（总和 `create_attr_total`、单项 `create_attr_min`~`create_attr_max`）
##               → 选武器 → 选本系 ★1 起始武学 → 选天赋 → 起名
##
## 两条路**给的东西完全等价**（设计 13），所以这里只有「模板从哪来」一处差别：
## 模板路线直接引用现成行；自建路线现造一行、注入 `character_base`、并把这份 spec 存进存档
## （`GameState.custom_templates`）——不然读档时按 char_id 查不到模板。
##
## 两条纪律：
##   ① **创建没走完不写存档**（设计 13 的准话）——本服务只造 `GameState`，落盘由调用方在确认后做；
##   ② **选了谁当主角，谁就不作为同伴出现**——不用额外记账：`RecruitService` 只收不在队里的人，
##      主角已经在 `char_ids` 里，招募自然跳过。
class_name CreationService
extends RefCounted

const GameStateScript := preload("res://src/core/game_state.gd")
const TalentServiceScript := preload("res://src/core/talent_service.gd")

## 自建角色的固定 id：一局只有一个主角，进存档时也按它认
const CUSTOM_CHAR_ID := "char_custom"
## 自建角色的起始护甲：与 5 个模板一样给凡品布衣
const CUSTOM_ARMOR_ID := "eq_armor_01"


## 出身卡（创建界面直接渲染这个）。`playstyle`／`tagline` 就是 origin_def 的展示列。
static func origins(db) -> Array:
	var out: Array = []
	if db == null:
		return out
	for row: Resource in db.rows("origin_def"):
		var card := {
			"origin_id": str(row.origin_id),
			"char_id": str(row.char_id),
			"name_cn": str(row.name_cn),
			# 默认名（设计 21 §七 第 5 条）：卡片第二行与"名字"预填都用它
			"default_name_cn": default_name_of(db, str(row.origin_id)),
			"playstyle": str(row.playstyle),
			"tagline": str(row.tagline),
			"sort_order": int(row.sort_order),
			"desc": str(row.desc),
		}
		# 设计 13 §二：**卡片必须显示七维与起始武学**（"不能只给一句文案——玩家是在做数值选择"），
		# §六 还要求七维用**条形图**（"五个出身一眼可比，比读数字快"）。所以在服务层就把这几样
		# 备好，界面只负责渲染——**别让界面自己去 character_base 里翻**（同一份换算只写一处）。
		var char_row: Resource = db.get_row("character_base", str(row.char_id))
		if char_row != null:
			card["attrs"] = char_row.initial_attrs()
			card["weapon_type"] = str(char_row.weapon_type)
			card["weapon_name"] = weapon_name_of(db, str(char_row.weapon_type))
			var skill_names := PackedStringArray()
			for skill_id: String in char_row.skill_ids():
				skill_names.append(skill_name_of(db, skill_id))
			card["skill_names"] = skill_names
		out.append(card)
	out.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		return int(a["sort_order"]) < int(b["sort_order"]))
	return out


## 武器类型的中文名（15 §六 的表里那列；查不到就退回 id，界面照实显示）
static func weapon_name_of(db, weapon_type: String) -> String:
	var row: Resource = db.get_row("weapon_type_def", weapon_type) if db != null else null
	return str(row.name_cn) if row != null else weapon_type


## 出身卡的**默认名**（设计 21 §七 第 5 条，决策 323）：优先 `origin_def.default_name_cn`
## 那一列——因为 `name_cn` 是**卡的标题**（书生那张写的是「家道失落的书生」这种**类名**，
## 而真名是**陆文昭**），预填进"名字"那一格会给主角起个类名。
## 空值退回 `name_cn`（老数据／没配的卡不会崩）。**"默认名怎么来"只有这一处**——
## `MenuController._with_defaults()` 与创建界面的预填都调它。
static func default_name_of(db, origin_id: String) -> String:
	var card: Resource = db.get_row("origin_def", origin_id) if db != null else null
	if card == null:
		return ""
	var real := str(card.default_name_cn).strip_edges()
	return real if not real.is_empty() else str(card.name_cn)


## 武学的中文名（出身卡的「起始武学」那一项用它）
static func skill_name_of(db, skill_id: String) -> String:
	var row: Resource = db.get_row("skill_base", skill_id) if db != null else null
	return str(row.name_cn) if row != null else skill_id


## 自建路线的七维规则（`growth_const` 的三个创建常数）。
static func attr_rules(db) -> Dictionary:
	var total := 0
	var low := 0
	var high := 0
	if db != null:
		for row: Resource in db.rows("growth_const"):
			match str(row.const_id):
				"create_attr_total": total = int(row.value)
				"create_attr_min": low = int(row.value)
				"create_attr_max": high = int(row.value)
	return {"total": total, "min": low, "max": high}


## 武器类型选项（`weapon_type_def` 的全部行）。
static func weapons(db) -> Array:
	var out: Array = []
	if db == null:
		return out
	for row: Resource in db.rows("weapon_type_def"):
		out.append({
			"weapon_type": str(row.weapon_type),
			"name_cn": str(row.name_cn),
			"desc": str(row.desc) if row.get("desc") != null else "",
		})
	return out


## 某武器能当起手的那几部武学：**★1 且本系（`any`／留空也算通用）**（设计 13）。
static func start_skill_options(db, weapon_type: String) -> Array:
	var out: Array = []
	if db == null:
		return out
	for row: Resource in db.rows("skill_base"):
		if int(row.star) != 1 or str(row.source_type) != "start":
			continue
		var want := str(row.weapon_type)
		if not (want == weapon_type or want == "any" or want.is_empty()):
			continue
		out.append({"skill_id": str(row.skill_id), "name_cn": str(row.name_cn), "weapon_type": want})
	out.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return a["skill_id"] < b["skill_id"])
	return out


## 自建角色的起始装备（设计 13：**不由玩家选**，按武器自动给）——
## 该武器的凡品一件 ＋ 与模板同款的布衣。找不到对应武器时只给布衣（不编数据）。
static func default_equipment(db, weapon_type: String) -> PackedStringArray:
	var out := PackedStringArray()
	if db != null and not weapon_type.is_empty():
		for row: Resource in db.rows("equip_base"):
			if str(row.slot) == "weapon" and str(row.weapon_type) == weapon_type \
					and str(row.rarity) == "common":
				out.append(str(row.equip_id))
				break
	out.append(CUSTOM_ARMOR_ID)
	return out


## 校验一份创建方案。返回错误列表（空 = 通过）。
##
## `spec`：
##   {"route": "origin"|"custom", "origin_id", "char_id", "name_cn",
##    "attrs": {七个 attr_id}, "weapon_type", "start_skill_id", "talents": [talent_id],
##    "difficulty_id"}
static func validate(db, spec: Dictionary) -> PackedStringArray:
	var errors := PackedStringArray()
	if db == null:
		errors.append("没有配置表")
		return errors
	var route := str(spec.get("route", "origin"))
	var char_id := str(spec.get("char_id", ""))
	if route == "origin":
		var origin_id := str(spec.get("origin_id", ""))
		var card: Resource = db.get_row("origin_def", origin_id)
		if card == null:
			# 还没选（空 id）是**人话**；选了但查不到才是数据错——只有后者才点名 id。
			# 以前两种情况都走同一句，于是刚进创建界面时状态栏写着「出身不存在：」（空 id）。
			errors.append("还没选出身" if origin_id.is_empty() else "出身不存在：%s" % origin_id)
		else:
			char_id = str(card.char_id)
	else:
		char_id = CUSTOM_CHAR_ID
		var rules := attr_rules(db)
		var attrs: Dictionary = spec.get("attrs", {})
		var total := 0
		for attr_row: Resource in db.rows("attribute_def"):
			var attr_id := str(attr_row.attr_id)
			if not attrs.has(attr_id):
				errors.append("七维少了 %s" % attr_id)
				continue
			var value := int(attrs[attr_id])
			total += value
			if value < int(rules["min"]) or value > int(rules["max"]):
				errors.append("%s 必须在 %d~%d 之间（现在是 %d）"
					% [str(attr_row.name_cn), int(rules["min"]), int(rules["max"]), value])
		if total != int(rules["total"]):
			errors.append("七维总和必须正好 %d（现在是 %d）" % [int(rules["total"]), total])
		var weapon := str(spec.get("weapon_type", ""))
		if db.get_row("weapon_type_def", weapon) == null:
			errors.append("武器类型不存在：%s" % weapon)
		var skill_id := str(spec.get("start_skill_id", ""))
		var allowed := false
		for option: Dictionary in start_skill_options(db, weapon):
			if str(option["skill_id"]) == skill_id:
				allowed = true
		if not allowed:
			errors.append("起始武学 %s 不是「★1 且本系」的那几部" % skill_id)
	if char_id.is_empty():
		errors.append("没有确定用哪个角色模板")
	if str(spec.get("name_cn", "")).strip_edges().is_empty():
		errors.append("还没起名")
	# 天赋：id 存在 + 总花费不超预算
	var picks := PackedStringArray()
	for talent_id: Variant in Array(spec.get("talents", [])):
		picks.append(str(talent_id))
	if not picks.is_empty():
		var running := PackedStringArray()
		for talent_id: String in picks:
			var check: Dictionary = TalentServiceScript.can_pick(db, running, talent_id)
			if not bool(check["ok"]):
				errors.append(str(check["error"]))
			else:
				running.append(talent_id)
	return errors


## 按方案造出一局（**不落盘**——落盘由调用方在玩家按「确认」之后做）。
## 返回 {ok, state, char_id, error}
static func build(db, spec: Dictionary) -> Dictionary:
	var errors := validate(db, spec)
	if not errors.is_empty():
		return {"ok": false, "state": null, "char_id": "", "error": "；".join(errors)}
	var route := str(spec.get("route", "origin"))
	var char_id := ""
	var custom_spec: Dictionary = {}
	if route == "origin":
		var card: Resource = db.get_row("origin_def", str(spec.get("origin_id", "")))
		char_id = str(card.char_id)
		# 「主角可改」＋默认名（设计 21 §七 第 5 条，决策 323）：
		# `GameState.char_name()` 读的是**表里那一行**，所以玩家改过的名字（以及"默认名＝真名"）
		# 必须像自建角色那样**注入一行并存进存档**——否则界面上改的名字谁也不认，
		# 面板会一直显示卡的标题（书生的卡标题是「家道失落的书生」）。
		# 其余字段照模板抄一遍，只有 `name_cn` 换掉；`custom_templates` 是 v14 就有的字段
		# （自建角色那条路在用），所以**不动存档版本**。
		var char_row: Resource = db.get_row("character_base", char_id)
		var wanted_name := str(spec.get("name_cn", "")).strip_edges()
		if char_row != null and not wanted_name.is_empty() and wanted_name != str(char_row.name_cn):
			custom_spec = {
				"name_cn": wanted_name,
				"role_tag": str(char_row.role_tag),
				"weapon_type": str(char_row.weapon_type),
				"attrs": char_row.initial_attrs(),
				"start_level": int(char_row.start_level),
				"start_skill_ids": str(char_row.start_skill_ids),
				"start_equip_ids": str(char_row.start_equip_ids),
				"desc": str(char_row.desc),
			}
	else:
		char_id = CUSTOM_CHAR_ID
		custom_spec = {
			"name_cn": str(spec.get("name_cn", "无名")),
			"role_tag": "自建",
			"weapon_type": str(spec.get("weapon_type", "")),
			"attrs": Dictionary(spec.get("attrs", {})).duplicate(),
			"start_level": 1,
			"start_skill_ids": str(spec.get("start_skill_id", "")),
			"start_equip_ids": ";".join(default_equipment(db, str(spec.get("weapon_type", "")))),
			"desc": "自建角色（不使用模板）",
		}

	# 自建角色要**先注入模板**再开新局：`new_game` 是按 char_id 读 character_base 的，
	# 模板没在表里 = 这一行被跳过 = 开局队伍为空。注入是内存行为，CSV 不动。
	if not custom_spec.is_empty():
		var probe := GameStateScript.new()
		probe.custom_templates[char_id] = custom_spec.duplicate(true)
		probe.apply_custom_templates(db)
	var state = GameStateScript.new_game(
		db, str(spec.get("difficulty_id", "normal")), PackedStringArray([char_id]), 0
	)
	if custom_spec.is_empty():
		state.talent_picks[char_id] = Array(spec.get("talents", [])).duplicate()
	else:
		state.custom_templates[char_id] = custom_spec.duplicate(true)
		state.apply_custom_templates(db)
		state.talent_picks[char_id] = Array(spec.get("talents", [])).duplicate()
	# 天赋里「纯增益」的那类：起始铜钱（设计 12：纯增益只加资源与规则）
	var money := int(TalentServiceScript.rule_value(db, state, char_id, "start_money", 0.0))
	if money != 0:
		state.inventory.money += money
	return {"ok": true, "state": state, "char_id": char_id, "error": ""}
