## 线索本（03_副本_黑风寨.md：「线索必须能被找到，否则就是猜谜。每条隐藏至少给两个线索来源」）。
##
## 数据全在表里：`hidden_trigger.clue_source` 与 `event_check.clue_source` 都是分号分隔的线索来源
## （行类的 `clues()` 解析）。这里把它们整理成玩家能读的列表，并带上「做过没」的状态：
##   隐藏内容看存档 `dungeon_records.triggers`
##   事件判定看存档 `event_checks`
##   大地图上的事件按 `region_id` 归到地标名下（玩家在大地图按 K 就能翻线索本）
class_name ClueService
extends RefCounted

## 「这条线索我确实听人说过」的旗标由 `WorldEventService` 写（0.28.0 Q64 的 hint 类效果）
const WorldEventServiceScript := preload("res://src/core/world_event_service.gd")
const GuideServiceScript := preload("res://src/core/guide_service.gd")
const NpcServiceScript := preload("res://src/core/npc_service.gd")

## 纪事（线索本）的五类分页（设计 0.32.0「纪事＝线索本扩成五类」，逐条见 UI 落地清单 §二-10）。
##
## **顺序就是页签顺序**；「隐藏」那一页跟着打开时的视角（小地图／大地图）走，另外四类是全局的。
## 这里只认**现成的表**：主线＝引导步骤、支线＝NPC 委托、传闻＝随机事件的 hint ＋ 观察点碎句、
## 角色＝招募表与 NPC 表、隐藏＝隐藏内容与事件判定。**不为这五页新开表、不加列。**
const SECTION_ORDER := ["main", "side", "rumor", "cast", "hidden"]
const SECTION_TITLES := {
	"main": "主线",
	"side": "支线",
	"rumor": "传闻",
	"cast": "角色",
	"hidden": "隐藏",
}
## 「这条碎句我读到过」的旗标前缀（`local_map_controller`／`overworld_controller` 读到观察点时置）
const OBSERVED_PREFIX := "flag_obs_"
## 传闻的来源之一：随机事件里 `effect_kind=hint` 那一类（另一类是观察点碎句）。
## 注意是 `effect_kind` 不是 `kind`——`kind` 是事件的门类（caravan／omen…），
## 「驿站风声」那条的 `kind` 写的是 `omen`，只有 `effect_kind` 才是 `hint`。
const HINT_KIND := "hint"

var db
var state


func _init(table_db, game_state) -> void:
	db = table_db
	state = game_state


## 一张小地图的线索：隐藏内容 + 事件判定
func clues_for_scene(scene_id: String) -> Array:
	var out: Array = []
	var done_triggers: Array = state.dungeon_record(scene_id).get("triggers", []) if state != null else []
	for row: Resource in db.rows("hidden_trigger"):
		if str(row.scene_id) != scene_id:
			continue
		out.append({
			"kind": "hidden",
			"id": str(row.trigger_id),
			"name": str(row.name_cn),
			"clues": Array(row.clues()),
			"note": str(row.note),
			"done": done_triggers.has(str(row.trigger_id)),
			"hint_known": state.has_flag(WorldEventServiceScript.hint_flag(str(row.trigger_id))) if state != null else false,
			"unsupported": str(row.trigger_type) == "sequence",
			"condition": str(row.required_condition),
		})
	for row: Resource in db.rows("event_check"):
		if str(row.scene_id) != scene_id:
			continue
		out.append(_event_entry(row))
	return out


## 大地图按地标分组的线索（`event_check.region_id`）
func clues_for_region(region_id: String) -> Array:
	var out: Array = []
	for row: Resource in db.rows("event_check"):
		if str(row.region_id) != region_id:
			continue
		var entry := _event_entry(row)
		entry["region_name"] = node_name(region_id)
		out.append(entry)
	return out


## 全部大地图线索（按地标名分组显示用）
func region_clues() -> Array:
	var out: Array = []
	for row: Resource in db.rows("map_region"):
		out.append_array(clues_for_region(str(row.node_id)))
	return out


# ---------------------------------------------------------------- 纪事：五类分页

## 一页的内容：`{id, title, subtitle, entries}`；每条 `entries` 是 `{id, name, text, done}`。
##
## 文案只在这里拼——面板负责摆，不负责编话（同 `describe()` 的口径）。
func section(section_id: String, scope: String = "scene", scene_id: String = "") -> Dictionary:
	match section_id:
		"main":
			return _main_section()
		"side":
			return _side_section()
		"rumor":
			return _rumor_section()
		"cast":
			return _cast_section()
		_:
			return _hidden_section(scope, scene_id)


func _section_entry(id: String, name: String, text: String, done: bool) -> Dictionary:
	return {"id": id, "name": name, "text": text, "done": done}


## 主线：`guide_step` 按 `sort_order` 排，当前步＝条件已满足的最后一行（与 `GuideService` 同一口径）。
## 顶部写百分比（已了步数／总步数），每步写状态词——**旗标名一个都不许露**。
func _main_section() -> Dictionary:
	var rows: Array = GuideServiceScript.steps(db)
	var current := int(GuideServiceScript.current(db, state).get("index", 0))
	var done_steps := maxi(0, current - 1)
	var percent := 0
	if not rows.is_empty():
		percent = int(round(100.0 * float(done_steps) / float(rows.size())))
	var entries: Array = []
	for row: Resource in rows:
		var index := int(row.sort_order)
		var status := "未接"
		if index < current:
			status = "已了"
		elif index == current:
			status = "进行中"
		var lines := PackedStringArray()
		lines.append("%s　［%s］" % [str(row.text_cn), status])
		var spec := str(row.condition_spec)
		if not spec.is_empty():
			lines.append("　%s" % spec)
		entries.append(_section_entry(str(row.step_id), str(row.text_cn), "\n".join(lines), status == "已了"))
	return {
		"id": "main",
		"title": "纪事　（主线 · %s）" % _chapter_name(rows),
		"subtitle": "章节进度 %d%%　（已了 %d／共 %d 步）" % [percent, done_steps, rows.size()],
		"entries": entries,
	}


## 支线：`npc_quest` 逐条摆。状态词按 `NpcService` 那套判——已了／待交（条件已达成）／待接（前置没齐）。
func _side_section() -> Dictionary:
	var rows: Array = db.rows("npc_quest").duplicate()
	rows.sort_custom(func(a: Resource, b: Resource) -> bool:
		if str(a.npc_id) == str(b.npc_id):
			return int(a.sort_order) < int(b.sort_order)
		return str(a.npc_id) < str(b.npc_id))
	var entries: Array = []
	var done_count := 0
	for row: Resource in rows:
		var quest_id := str(row.quest_id)
		var npc_id := str(row.npc_id)
		var done: bool = NpcServiceScript.quest_done(state, quest_id)
		var status := "待接"
		if done:
			status = "已了"
			done_count += 1
		else:
			var available: Resource = NpcServiceScript.available_quest(db, state, npc_id)
			if available != null and str(available.quest_id) == quest_id:
				status = "待交"
		var lines := PackedStringArray()
		lines.append("%s　［%s］" % [str(row.text_cn), status])
		lines.append("　委托人：%s　%s" % [_npc_name(npc_id), _requirement_text(str(row.requirement))])
		var reward := _reward_text(row)
		if not reward.is_empty():
			lines.append("　奖励：%s" % reward)
		entries.append(_section_entry(quest_id, str(row.text_cn), "\n".join(lines), done))
	return {
		"id": "side",
		"title": "纪事　（支线 · 委托）",
		"subtitle": "共 %d 条委托　已了 %d 条　（每条写清了委托人与奖励）" % [rows.size(), done_count],
		"entries": entries,
	}


## 传闻：**只听、只看现成的两处来源**——① 随机事件的 `hint` 效果（听到即记下）；
## ② 观察点 `flavor_point` 读到的碎句。
##
## 「验」＝这条线索**指向的东西被真触发**：指向隐藏内容看 `dungeon_records.triggers`，
## 指向事件判定看 `event_checks`。指向关系**不新开列**——拿碎句去 `clue_source` 里逐字对，
## 对得上就是同一个东西（观察点与隐藏线索本来就是同一批文案）。
func _rumor_section() -> Dictionary:
	var entries: Array = []
	for row: Resource in db.rows("world_event"):
		if str(row.effect_kind) != HINT_KIND:
			continue
		var trigger_id := str(row.effect_id)
		if state == null or not state.has_flag(WorldEventServiceScript.hint_flag(trigger_id)):
			continue
		var target := _trigger_target(trigger_id)
		var lines := PackedStringArray()
		# 行 1 是事件名（「驿站风声」这类），下面才是**听到了什么**——
		# 策划 2026-10-04 拍：传闻行＝事件名 ＋ 听到的那句话 ＋ 已验／未验，不加前置条件栏。
		lines.append("%s　［%s］" % [str(row.name_cn), _verify_word(target)])
		var prompt := str(row.prompt_text_cn)
		if not prompt.is_empty():
			lines.append("　%s" % prompt)
		var heard := str(row.text_cn)
		if not heard.is_empty():
			lines.append("　%s" % heard)
		lines.append("　指向：%s" % str(target.get("name", "线索")))
		entries.append(_section_entry(
			str(row.event_id), str(row.name_cn), "\n".join(lines), bool(target.get("done", false)),
		))
	for row: Resource in db.rows("flavor_point"):
		var point_id := str(row.point_id)
		if state == null or not state.has_flag(OBSERVED_PREFIX + point_id):
			continue
		var target := _clue_target(str(row.text_cn))
		var lines := PackedStringArray()
		lines.append("%s　［%s］" % [_trim_sentence(str(row.text_cn)), _verify_word(target)])
		var place := _point_place(row)
		if not place.is_empty():
			lines.append("　看到于：%s" % place)
		if not str(target.get("name", "")).is_empty():
			lines.append("　指向：%s" % str(target["name"]))
		entries.append(_section_entry(
			point_id, _trim_sentence(str(row.text_cn)), "\n".join(lines), bool(target.get("done", false)),
		))
	return {
		"id": "rumor",
		"title": "纪事　（传闻）",
		"subtitle": _rumor_subtitle(entries),
		"entries": entries,
	}


## 角色：同伴（`recruit_def`，主角自己不算）＋ 关键人物（`npc_def`）。
## 同伴状态＝已入队／待入队（加入条件已达成）／未遇；关键人物＝已结识／未遇（好感账本里有没有这一号）。
func _cast_section() -> Dictionary:
	var entries: Array = []
	var player_id := str(state.char_ids[0]) if state != null and not state.char_ids.is_empty() else ""
	var joined := 0
	for row: Resource in db.rows("recruit_def"):
		var char_id := str(row.char_id)
		if char_id == player_id:
			continue
		var in_party: bool = state != null and state.char_ids.has(char_id)
		var met := GuideServiceScript.condition_met(state, str(row.join_condition))
		var status := "未遇"
		if in_party:
			status = "已入队"
			joined += 1
		elif met:
			status = "待入队"
		var lines := PackedStringArray()
		lines.append("%s　［%s］" % [_char_name(char_id), status])
		var scene_name := _scene_name(str(row.join_scene))
		if not scene_name.is_empty():
			lines.append("　在哪：%s" % scene_name)
		var favor_line := _favor_text(char_id)
		if not favor_line.is_empty():
			lines.append("　%s" % favor_line)
		var note := str(row.join_note)
		if not note.is_empty():
			lines.append("　%s" % note)
		entries.append(_section_entry(char_id, _char_name(char_id), "\n".join(lines), in_party))
	var known_count := 0
	for row: Resource in db.rows("npc_def"):
		var npc_id := str(row.npc_id)
		var known: bool = state != null and state.npc_favor.has(npc_id)
		if known:
			known_count += 1
		var status := "已结识" if known else "未遇"
		var lines := PackedStringArray()
		lines.append("%s　［%s］" % [str(row.name_cn), status])
		lines.append("　%s　%s" % [str(row.title_cn), "Lv.%d" % int(row.level)])
		var npc_favor_line := _favor_text(npc_id)
		if not npc_favor_line.is_empty():
			lines.append("　%s" % npc_favor_line)
		entries.append(_section_entry(npc_id, str(row.name_cn), "\n".join(lines), known))
	return {
		"id": "cast",
		"title": "纪事　（角色）",
		"subtitle": "同伴 %d 人（已入队 %d）　关键人物 %d 人（已结识 %d）" % [
			entries.size() - db.rows("npc_def").size(), joined, db.rows("npc_def").size(), known_count,
		],
		"entries": entries,
	}


## 隐藏：隐藏内容 ＋ 事件判定，跟着视线（小地图／大地图）走——与打开线索本那两处入口同一口径。
## 小节行写清「共 N 处 · 已揭 M 处 · 未揭 K 处」；**每一处都带自己的线索来源**（03 副本那条设计：
## 「线索必须能被找到，否则就是猜谜」），所以这里列名字是有意的，不按示意稿的「只报个数」来。
func _hidden_section(scope: String, scene_id: String) -> Dictionary:
	var raw: Array = region_clues() if scope == "region" else clues_for_scene(scene_id)
	var entries: Array = []
	var done_count := 0
	for entry: Dictionary in raw:
		if bool(entry.get("done", false)):
			done_count += 1
		entries.append(_section_entry(
			str(entry["id"]), str(entry["name"]), describe(entry), bool(entry.get("done", false)),
		))
	var where := "野外（按地标）" if scope == "region" else _scene_name(scene_id)
	var head := "" if where.is_empty() else "%s　" % where
	return {
		"id": "hidden",
		"title": "纪事　（隐藏）",
		"subtitle": "%s共 %d 处　已揭 %d 处　未揭 %d 处" % [
			head, raw.size(), done_count, raw.size() - done_count,
		],
		"entries": entries,
	}


func _rumor_subtitle(entries: Array) -> String:
	var verified := 0
	for entry: Dictionary in entries:
		if bool(entry["done"]):
			verified += 1
	return "听到 %d 条　已验 %d 条　（还只是听来的，找到它指向的东西才算数）" % [entries.size(), verified]


# ---------------------------------------------------------------- 纪事：查表拼文案

## `item:item_iron:10` → 「需 铁矿石 ×10」；`flag_xxx` 这类旗标**不翻译**
## （表里没有中文名，翻出来就会把表内 id 摆到玩家眼前）。
func _requirement_text(requirement: String) -> String:
	var text := requirement.strip_edges()
	if text.is_empty():
		return "随时可接"
	var parts: PackedStringArray = text.split(":", true)
	match parts[0]:
		"item":
			if parts.size() >= 3:
				return "需 %s ×%s" % [_item_name(parts[1]), parts[2]]
			return "需带一件东西去"
		"event":
			return "需先了结一桩事"
		"kill_style":
			return "需以特定手法击杀"
		_:
			return "需先办妥前事"


## 委托奖励：好感 ＋ 东西的中文名
func _reward_text(row: Resource) -> String:
	var parts := PackedStringArray()
	var favor := int(row.reward_favor)
	if favor != 0:
		parts.append("好感 +%d" % favor)
	for item_id: String in str(row.reward_item_ids).split(";", false):
		var name := _item_name(item_id.strip_edges())
		if not name.is_empty():
			parts.append(name)
	return "、".join(parts)


## `clue_source` 里逐字对得上的那一条 → `{kind, id, scene_id, name, done}`；对不上返回 `{}`。
func _clue_target(text: String) -> Dictionary:
	var needle := text.strip_edges()
	if needle.is_empty():
		return {}
	for row: Resource in db.rows("hidden_trigger"):
		if Array(row.clues()).has(needle):
			return _trigger_target(str(row.trigger_id))
	for row: Resource in db.rows("event_check"):
		if not Array(row.clues()).has(needle):
			continue
		var check_id := str(row.check_id)
		return {
			"kind": "event",
			"id": check_id,
			"name": str(row.note),
			"done": state != null and state.event_check_result(check_id) == "done",
		}
	return {}


## 隐藏内容的「验没验」看存档的 `dungeon_records.triggers`（与 `clues_for_scene` 同一口径）
func _trigger_target(trigger_id: String) -> Dictionary:
	var row: Resource = db.get_row("hidden_trigger", trigger_id)
	if row == null:
		return {"kind": "hidden", "id": trigger_id, "name": "线索", "done": false}
	var scene_id := str(row.scene_id)
	var done_triggers: Array = state.dungeon_record(scene_id).get("triggers", []) if state != null else []
	return {
		"kind": "hidden",
		"id": trigger_id,
		"name": str(row.name_cn),
		"done": done_triggers.has(trigger_id),
	}


func _verify_word(target: Dictionary) -> String:
	if target.is_empty():
		return "已听到"
	return "已验" if bool(target.get("done", false)) else "未验"


## 观察点是按 room 还是按 region 挂的——两种都要能说清「在哪儿看到的」
func _point_place(row: Resource) -> String:
	var region := str(row.region_id)
	if not region.is_empty():
		return node_name(region)
	var scene_name := _scene_name(str(row.scene_id))
	var room_id := str(row.room_id)
	if room_id.is_empty():
		return scene_name
	var room: Resource = db.get_row("dungeon_room", room_id)
	var room_name := str(room.name_cn) if room != null else ""
	if scene_name.is_empty():
		return room_name
	if room_name.is_empty():
		return scene_name
	return "%s · %s" % [scene_name, room_name]


func _chapter_name(rows: Array) -> String:
	if rows.is_empty():
		return "暂无章节"
	var row: Resource = db.get_row("chapter_def", str(rows[0].chapter_id))
	return str(row.name_cn) if row != null else "第一段路"


func _scene_name(scene_id: String) -> String:
	if scene_id.is_empty():
		return ""
	var row: Resource = db.get_row("map_local", scene_id)
	return str(row.name_cn) if row != null else ""


func _char_name(char_id: String) -> String:
	var row: Resource = db.get_row("character_base", char_id)
	return str(row.name_cn) if row != null else char_id


func _npc_name(npc_id: String) -> String:
	var row: Resource = db.get_row("npc_def", npc_id)
	return str(row.name_cn) if row != null else npc_id


func _item_name(item_id: String) -> String:
	var row: Resource = db.get_row("item_base", item_id)
	return str(row.name_cn) if row != null else ""


## 打过交道才有好感这一行（好感账本里没这一号人＝还没见过他，别摆一个「好感 0」）
func _favor_text(id: String) -> String:
	if state == null or not state.npc_favor.has(id):
		return ""
	return "好感 %d" % int(state.npc_favor[id])


## 碎句本来就短，摆进一行时去掉结尾的句号，读起来更像「线索」不像「段落」
func _trim_sentence(text: String) -> String:
	return text.strip_edges().trim_suffix("。")


func _event_entry(row: Resource) -> Dictionary:
	var check_id := str(row.check_id)
	var result := ""
	if state != null:
		result = state.event_check_result(check_id)
	return {
		"kind": "event",
		"id": check_id,
		"name": str(row.note),
		"clues": Array(row.clues()),
		"note": str(row.fail_note),
		"source": str(row.check_source),
		"source_label": source_label(str(row.check_source)),
		"difficulty": int(row.difficulty),
		"done": result == "done",
		"failed": result == "failed",
		"unsupported": false,
		"condition": "",
	}


## `skill:qimen` → 「奇门 3」的样式（线索本要告诉玩家拿什么去判）
func source_label(source: String) -> String:
	var parts := source.split(":", false)
	if parts.size() != 2:
		return source
	if parts[0] == "skill":
		var skill: Resource = db.get_row("event_skill_def", parts[1])
		return str(skill.name_cn) if skill != null else parts[1]
	var attr: Resource = db.get_row("attribute_def", parts[1])
	return str(attr.name_cn) if attr != null else parts[1]


func node_name(node_id: String) -> String:
	var row: Resource = db.get_row("map_region", node_id)
	return str(row.name_cn) if row != null else node_id


## 面板用的一行文案
func describe(entry: Dictionary) -> String:
	var status := "未完成"
	if bool(entry.get("done", false)):
		status = "已完成"
	elif bool(entry.get("failed", false)):
		status = "失败过"
	var lines := PackedStringArray()
	lines.append("%s（%s）" % [str(entry["name"]), status])
	var clues: Array = entry.get("clues", [])
	if not clues.is_empty():
		var suffix := "　（已从传闻中听说）" if bool(entry.get("hint_known", false)) else ""
		lines.append("　线索：%s%s" % ["；".join(PackedStringArray(clues)), suffix])
	if str(entry.get("source_label", "")).is_empty():
		if not str(entry.get("note", "")).is_empty():
			lines.append("　%s" % str(entry["note"]))
	else:
		lines.append("　判定：%s ≥ %d　失败：%s" % [
			str(entry["source_label"]), int(entry["difficulty"]), str(entry["note"]),
		])
	if bool(entry.get("unsupported", false)):
		lines.append("　（这一类还缺地图位点，暂时做不了）")
	return "\n".join(lines)
