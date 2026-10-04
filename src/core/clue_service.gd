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
