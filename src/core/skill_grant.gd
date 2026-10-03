## 武学「来源 → 学会」的解析。
##
## 已实现三类来源（都按 `skill_base.source_type` + `source_id` 配表驱动）：
##   `drop`   击败指名的敌人（战斗结算调用）
##   `hidden` 触发指名的隐藏点位（小地图触发点调用）
##   `item`   研读指名的秘籍（背包里研读调用；一次性，学会就消耗一本）
## 未实现：`npc`（门派传授，要 NPC 对话）、`story`（章节节点，要剧情旗标）、`shop`（商店买武学）。
##
## 口径：学到的武学进**每个队员**的已学列表（队伍共享，图鉴按去重计数），
## 单人专属来源（门派指定传人、秘籍指定某人）未做。
class_name SkillGrant
extends RefCounted

const SkillLoadoutScript := preload("res://src/core/skill_loadout.gd")

## 已接入的来源类型（其余类型即使 source_id 撞上了也不发放）
const IMPLEMENTED_SOURCES := ["drop", "hidden", "item"]


## 某个来源带哪些武学（source_type + source_id 命中）
static func skills_from_source(db, source_type: String, source_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	if not IMPLEMENTED_SOURCES.has(source_type):
		return out
	if source_id.is_empty():
		return out
	for row: Resource in db.rows_where("skill_base", "source_id", source_id):
		if str(row.source_type) == source_type:
			out.append(str(row.skill_id))
	out.sort()
	return out


## 某个敌人身上带哪些武学（source_type=drop）
static func skills_from_enemy(db, enemy_id: String) -> PackedStringArray:
	return skills_from_source(db, "drop", enemy_id)


## 按来源授予（触发点／秘籍与战斗结算共用）。返回结构同 grant_from_defeated
static func grant_from_source(db, state, source_type: String, source_id: String) -> Array:
	return _grant_skills(db, state, skills_from_source(db, source_type, source_id))


## 战斗胜利结算：按被击败的敌人领悟武学。
## 返回 [{skill_id, name, kind, star, learned: PackedStringArray, blocked: [{char_id, name, reason}]}]
static func grant_from_defeated(db, state, enemy_ids: PackedStringArray) -> Array:
	var out: Array = []
	if db == null or state == null:
		return out
	var seen := {}
	for enemy_id: String in enemy_ids:
		for entry: Dictionary in grant_from_source(db, state, "drop", enemy_id):
			var skill_id := str(entry["skill_id"])
			if seen.has(skill_id):
				continue
			seen[skill_id] = true
			out.append(entry)
	return out


static func _grant_skills(db, state, skill_ids: PackedStringArray) -> Array:
	var out: Array = []
	if db == null or state == null:
		return out
	for skill_id: String in skill_ids:
		var entry := _grant_one(db, state, skill_id)
		if not entry.is_empty():
			out.append(entry)
	return out


## 单个武学：全队逐人过修习门槛，够的门槛才学会
static func _grant_one(db, state, skill_id: String) -> Dictionary:
	var row: Resource = db.get_row("skill_base", skill_id)
	if row == null:
		push_error("[SkillGrant] skill_base 缺少 %s" % skill_id)
		return {}
	var learned := PackedStringArray()
	var blocked: Array = []
	var already := 0
	for char_id: String in state.char_ids:
		var loadout = SkillLoadoutScript.new(db, state, char_id)
		var result: Dictionary = loadout.learn(skill_id)
		if bool(result["ok"]) and bool(result["new"]):
			learned.append(char_id)
		elif not bool(result["ok"]):
			blocked.append({
				"char_id": char_id,
				"name": state.char_name(db, char_id),
				"reason": str(result["error"]),
			})
		else:
			already += 1
	# 全队早就学过：不必每场都播报一次
	if learned.is_empty() and blocked.is_empty():
		return {}
	return {
		"skill_id": skill_id,
		"name": str(row.name_cn),
		"kind": str(row.skill_kind),
		"star": int(row.star),
		"learned": learned,
		"blocked": blocked,
		"already": already,
	}


## 结算面板上显示的一行文案
static func describe(entry: Dictionary) -> String:
	var parts := PackedStringArray()
	var learned: PackedStringArray = entry.get("learned", PackedStringArray())
	if not learned.is_empty():
		parts.append("领悟「%s」" % str(entry["name"]))
	for row: Dictionary in Array(entry.get("blocked", [])):
		parts.append("%s没能领悟「%s」：%s" % [
			str(row["name"]), str(entry["name"]), str(row["reason"]),
		])
	return "；".join(parts)


## 结算面板用的紧凑汇总：学到的合成一行、没学到的合成一行。
## 一场战斗可能一次播报好几部武学，一行一条会把结算框顶出窗口（界面上直接看不全）。
static func summarize(entries: Array) -> PackedStringArray:
	var learned := PackedStringArray()
	var blocked := PackedStringArray()
	for entry: Dictionary in entries:
		if not PackedStringArray(entry.get("learned", PackedStringArray())).is_empty():
			learned.append(str(entry["name"]))
		for row: Dictionary in Array(entry.get("blocked", [])):
			blocked.append("%s（%s）" % [str(entry["name"]), str(row["reason"])])
	var out := PackedStringArray()
	if not learned.is_empty():
		out.append("领悟：" + "、".join(learned))
	if not blocked.is_empty():
		out.append("未习得：" + "；".join(blocked))
	return out


# ------------------------------------------------------------------ 秘籍（item 来源）

## 这部秘籍现在能不能研读：{ok, error, skills}
static func can_study(db, state, item_id: String) -> Dictionary:
	if state == null or state.inventory == null:
		return {"ok": false, "error": "没有会话状态", "skills": PackedStringArray()}
	if not state.inventory.has(item_id):
		return {"ok": false, "error": "背包里没有这本秘籍", "skills": PackedStringArray()}
	var skills: PackedStringArray = skills_from_source(db, "item", item_id)
	if skills.is_empty():
		# 例：醉里乾坤残卷（钥匙道具，要「集齐」才解锁，集齐机制没做）
		return {
			"ok": false,
			"error": "这本秘籍现在没有可学的武学（来源没接或要集齐多本，集齐机制未做）",
			"skills": skills,
		}
	return {"ok": true, "error": "", "skills": skills}


## 研读一本秘籍：门槛不够或已经学过就**不消耗**（别浪费稀有残页）。
## 返回 {ok, error, entries, learned: PackedStringArray, consumed}
static func study(db, state, item_id: String) -> Dictionary:
	var check := can_study(db, state, item_id)
	if not check["ok"]:
		return {"ok": false, "error": str(check["error"]), "entries": [], "learned": PackedStringArray(), "consumed": false}
	var entries: Array = _grant_skills(db, state, PackedStringArray(check["skills"]))
	var learned := PackedStringArray()
	var blocked := PackedStringArray()
	for entry: Dictionary in entries:
		for char_id: String in PackedStringArray(entry["learned"]):
			if not learned.has(char_id):
				learned.append(char_id)
		for row: Dictionary in Array(entry["blocked"]):
			var text := "%s（%s）" % [str(entry["name"]), str(row["reason"])]
			if not blocked.has(text):
				blocked.append(text)
	if learned.is_empty():
		var reason := "这几部武学都已经学过了"
		if not blocked.is_empty():
			reason = "修习门槛不足：" + "；".join(blocked)
		return {"ok": false, "error": reason, "entries": entries, "learned": learned, "consumed": false}
	# 学会才消耗：秘籍是「一次性灌注」
	state.inventory.consume(item_id, 1)
	return {
		"ok": true,
		"error": "" if blocked.is_empty() else "部分武学没学会：" + "；".join(blocked),
		"entries": entries,
		"learned": learned,
		"consumed": true,
	}


## 界面用：这本秘籍能不能研读（含修习门槛），不能就给一条能读的原因。
## `can_study` 只看表，这里再看「队伍里有没有人够门槛」，这样按钮能正确置灰。
static func study_gate(db, state, item_id: String) -> Dictionary:
	var check := can_study(db, state, item_id)
	if not check["ok"]:
		return check
	var skills: PackedStringArray = check["skills"]
	var any_reachable := false
	var reasons := PackedStringArray()
	for skill_id: String in skills:
		for char_id: String in state.char_ids:
			var loadout = SkillLoadoutScript.new(db, state, char_id)
			var requirement: Dictionary = loadout.requirement_of(skill_id)
			if bool(requirement["ok"]):
				any_reachable = true
				break
			var reason := str(requirement["reason"])
			if not reason.is_empty() and not reasons.has(reason):
				reasons.append(reason)
		if any_reachable:
			break
	if not any_reachable:
		return {
			"ok": false,
			"error": "修习门槛不足：%s" % "、".join(reasons),
			"skills": skills,
		}
	return check
