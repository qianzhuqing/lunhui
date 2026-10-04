## 章节推进（设计 18 §3.1）：`chapter_def` 是「第一章从哪开始、到哪结束、下一章是谁」的唯一出处。
##
## 为什么要有它：`GameState.chapter_id` 原来是**硬编码常量**（`DEFAULT_CHAPTER := "chapter_01"`），
## 枢纽页那行「章节：第 1 章」是个假象——既没有推进逻辑，也没有「第一章结束」这个实体
## （设计侧端到端梳理的断点 5）。现在判定口径在表里，代码只做匹配。
class_name ChapterService
extends RefCounted

const GuideServiceScript := preload("res://src/core/guide_service.gd")


## 当前章节行。存档里的 id 认不出来时退回「开局那一章」（`entry_condition=start`）——
## 老档或空值不该让整局没有章节。
static func row_of(db, state) -> Resource:
	if db == null or state == null:
		return null
	var row: Resource = db.get_row("chapter_def", str(state.chapter_id))
	if row != null:
		return row
	return starting_row(db)


static func starting_row(db) -> Resource:
	if db == null:
		return null
	for row: Resource in db.rows("chapter_def"):
		if str(row.entry_condition) == "start":
			return row
	return null


## 章节显示名（表里的 `name_cn`）。取不到时给空串——**绝不把 id 甩给玩家**。
static func label_of(db, state) -> String:
	var row := row_of(db, state)
	return str(row.name_cn) if row != null else ""


## 完成条件满足就推进到下一章。返回 {advanced, from, to, name}
##
## 只推进**一格**：一次调用换一章，避免"链式跳章"把中间的剧情吞掉。
static func try_advance(db, state) -> Dictionary:
	var row := row_of(db, state)
	var empty := {"advanced": false, "from": "", "to": "", "name": "", "text": ""}
	if row == null:
		return empty
	var from := str(row.chapter_id)
	var done_text := str(row.complete_text_cn)
	if not GuideServiceScript.condition_met(state, str(row.complete_condition)):
		return {"advanced": false, "from": from, "to": "", "name": str(row.name_cn), "text": ""}
	var next := str(row.next_chapter_id)
	if next.is_empty():
		# 最后一章：完成了也停在原地（设计把第二章当占位，第二章没有下一章）
		return {"advanced": false, "from": from, "to": "", "name": str(row.name_cn), "text": done_text}
	var next_row: Resource = db.get_row("chapter_def", next)
	if next_row == null:
		push_error("[ChapterService] chapter_def 里没有下一章 %s（%s 的 next_chapter_id 写错了）" % [next, from])
		return {"advanced": false, "from": from, "to": "", "name": str(row.name_cn), "text": ""}
	state.chapter_id = next
	# `text` 是表里的 `complete_text_cn`（「第一章告一段落」）——**给玩家看的那一句**，
	# 由场景层播报；空着说明这一章没配完成文案。
	return {"advanced": true, "from": from, "to": next, "name": str(next_row.name_cn), "text": done_text}
