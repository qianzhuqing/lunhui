## enemy_team.csv 行：敌人小队配置（明雷队伍）。
extends "res://src/data/table_row.gd"

@export var team_id: String = ""
@export var name_cn: String = ""
## 成员，格式 "enemy_id:count;enemy_id:count"
@export var members: String = ""
@export var threat_tag: String = "green"
@export var team_buff: String = ""
@export var note: String = ""


## 解析 members，返回 [{"enemy_id": String, "count": int}, ...]。
func parsed_members() -> Array:
	var out: Array = []
	for part: String in members.split(";", false):
		var trimmed := part.strip_edges()
		if trimmed.is_empty():
			continue
		var pieces := trimmed.split(":", false)
		var enemy_id := pieces[0].strip_edges()
		var count := 1
		if pieces.size() > 1:
			count = int(pieces[1].strip_edges().to_float())
		out.append({"enemy_id": enemy_id, "count": count})
	return out
