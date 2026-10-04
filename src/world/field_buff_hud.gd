## 战斗外增益 HUD（08 的 UI 要求：「大地图与城镇的 HUD 上，**战斗外增益**必须可见」——
## 否则玩家不知道自己带着一个还剩 2 分钟的状态走进战斗）。
##
## 大地图（`overworld_controller`）与小地图／城镇（`local_map_controller`）共用这一份文案与控件，
## 免得同一段话写两遍、两处说得不一样。
class_name FieldBuffHud
extends RefCounted

const COLOR := Color(0.85, 0.95, 1.0, 0.95)


## 一个挂在 HUD 上的小标签（位置由调用方给）。
##
## **放在第 3 行（y=56）**：大地图第 2 行是揭雾进度（`MapProgress`，y=34）、
## 小地图第 2 行是副本完成度（`Progress`，y=34）——两者都是左上角，不放第 3 行就会叠字。
## 两个控制器各有一条断言盯着「这一行不与上面两行同 y」。
static func build_label(position: Vector2, font_size: int = 12) -> Label:
	var label := Label.new()
	label.name = "FieldBuffs"
	label.position = position
	label.add_theme_font_size_override("font_size", font_size)
	label.add_theme_color_override("font_color", COLOR)
	label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	label.add_theme_constant_override("shadow_offset_x", 1)
	label.add_theme_constant_override("shadow_offset_y", 1)
	return label


## `session.active_field_buffs()` 的输出 → 一行文案；没有生效的增益就是空串（标签不占视觉）。
## 剩余时间显示到**分钟**（向上取整：还剩 10 秒就别写「0 分钟」），过期由会话层清掉。
static func text_of(db, rows: Array) -> String:
	var parts := PackedStringArray()
	for entry: Dictionary in rows:
		var buff_id := str(entry.get("buff_id", ""))
		var row: Resource = db.get_row("buff_def", buff_id) if db != null else null
		var name_cn := str(row.name_cn) if row != null else buff_id
		var minutes := int(ceil(float(entry.get("remaining_sec", 0)) / 60.0))
		var stacks := int(entry.get("stacks", 1))
		parts.append("%s 剩 %d 分钟%s" % [name_cn, minutes, "" if stacks <= 1 else " ×%d" % stacks])
	if parts.is_empty():
		return ""
	return "增益：" + "　·　".join(parts)
