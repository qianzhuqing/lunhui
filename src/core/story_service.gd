## 剧情节点（设计 18 §3.1）：`story_node` 是「条件 → 效果」的落地处。
##
## 它补的是设计侧端到端梳理里最要命的两个断点（18 号文档的 4／6）：
## **6 部 `source_type=story` 的武学原来一部都没有落地处**——`skill_base.source_id`
## 指向的 `chapter1_end`／`xuanwei`／`yaowang` 在表里根本不存在。
#
## 形状与招募链（`RecruitService`）完全一样：**条件与效果都在表里**，
## 代码只做「地点匹配 + 条件旗标已点亮 + 还没领过」三件事，
## **谁点亮条件旗标是各系统的事**（事件判定、隐藏触发、战斗结算……）。
##
## 完成记账用一枚派生旗标 `story_done_<node_id>`（名字的唯一出处是本文件的 `done_flag()`）——
## 不新开存档字段：旗标本来就可序列化，`EventCheckService` 的 `reward_type=event`
## 早就把「旗标＝一次性的记账」这套用顺了。
class_name StoryService
extends RefCounted

const GuideServiceScript := preload("res://src/core/guide_service.gd")
const SkillGrantScript := preload("res://src/core/skill_grant.gd")


static func done_flag(node_id: String) -> String:
	return "story_done_%s" % node_id


## 抉择的**永久增益**（设计 20 §八，0.29.1）：`kind=choice` 的节点只要**条件满足**就一直在生效
## ——条件就是那枚抉择旗标，所以"选完立刻生效"，不必再跑回触发点去领。
##
## 目标是**主角**（`state.char_ids[0]`）：抉择是主角做的，而这三条增益改的是**资质**
## （悟性／根骨）与派生上限——发给全队会变成"四个人的资质一起涨"，那是另一回事。
##
## 贡献形状与图鉴奖励完全一样（`attr_point`／`stat_flat`），**不另起一路属性合成**；
## 「选过没选过」记在旗标里，**不新增字段、不动存档版本**（设计 §十三 第 1 条）。
static func permanent_contributions(db, state, char_id: String) -> Array:
	var out: Array = []
	if db == null or state == null or char_id.is_empty():
		return out
	if state.char_ids.is_empty() or char_id != str(state.char_ids[0]):
		return out
	for row: Resource in db.rows("story_node"):
		if str(row.kind) != "choice":
			continue
		if not GuideServiceScript.condition_met(state, str(row.trigger_condition)):
			continue
		out.append_array(row.bonus_contributions())
	return out


## 生效中的抉择增益，翻译成**给人看的一条**（角色面板用）：`["悟性 +2", "内力上限 +20"]`。
##
## 名字从 `attribute_def.name_cn`／`stat_def.name_cn` 取，**面板不自己翻译 id**——
## 写死一份中文名，表一改就静默显示成 id（和"dot_poison 泄露"同一类）。
static func bonus_labels(db, state, char_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	if db == null or state == null or char_id.is_empty():
		return out
	if state.char_ids.is_empty() or char_id != str(state.char_ids[0]):
		return out
	for row: Resource in db.rows("story_node"):
		if str(row.kind) != "choice":
			continue
		if not GuideServiceScript.condition_met(state, str(row.trigger_condition)):
			continue
		if not str(row.bonus_attr_id).is_empty() and int(row.bonus_attr_value) != 0:
			var attr: Resource = db.get_row("attribute_def", str(row.bonus_attr_id))
			out.append("%s +%d" % [
				str(attr.name_cn) if attr != null else str(row.bonus_attr_id), int(row.bonus_attr_value),
			])
		if not str(row.bonus_stat_id).is_empty() and float(row.bonus_stat_value) != 0.0:
			var stat: Resource = db.get_row("stat_def", str(row.bonus_stat_id))
			var value := float(row.bonus_stat_value)
			out.append("%s %s" % [
				str(stat.name_cn) if stat != null else str(row.bonus_stat_id),
				("+%d%%" % int(round(value * 100.0))) if stat != null and int(stat.is_percent) == 1 \
					else "+%d" % int(round(value)),
			])
	return out


## 地点匹配：`place_id` 命中这张图（或它的父区域节点）。
## **`place_id` 留空 = 不限地点**（药王谷那条就是在哪儿都能领）。
static func place_matches(db, row: Resource, place_id: String) -> bool:
	var want := str(row.place_id)
	if want.is_empty():
		return true
	if place_id.is_empty():
		return false
	if want == place_id:
		return true
	var local: Resource = db.get_row("map_local", place_id)
	return local != null and str(local.parent_node) == want


## 这个地点上还能领的剧情节点（条件满足、还没领过、地点对得上）。
static func pending_for(db, state, place_id: String) -> Array:
	var out: Array = []
	if db == null or state == null:
		return out
	for row: Resource in db.rows("story_node"):
		var node_id := str(row.node_id)
		if state.has_flag(done_flag(node_id)):
			continue
		if not place_matches(db, row, place_id):
			continue
		if not GuideServiceScript.condition_met(state, str(row.trigger_condition)):
			continue
		out.append(row)
	return out


## 在某地点领取剧情节点：发武学；`kind=chapter_end` 的节点**同时置上本章的完成条件**。
##
## 返回 [{node_id, text_cn, granted, chapter_done}]；`granted` 的形状见 `SkillGrant`。
static func claim_for(db, state, place_id: String) -> Array:
	var out: Array = []
	for row: Resource in pending_for(db, state, place_id):
		var node_id := str(row.node_id)
		# 发放通道按 kind 分：`origin_gift`（出身本命机遇）走 `origin`，其余走 `story`。
		# 两条通道的 `source_id` 都是 `story_node.node_id`，只是条件不同。
		var source_type := "origin" if str(row.kind) == "origin_gift" else "story"
		var granted: Array = SkillGrantScript.grant_from_source(db, state, source_type, node_id)
		var chapter_done := false
		if str(row.kind) == "chapter_end":
			var chapter: Resource = db.get_row("chapter_def", str(row.chapter_id))
			if chapter != null and not str(chapter.complete_condition).is_empty():
				state.set_flag(str(chapter.complete_condition))
				chapter_done = true
		state.set_flag(done_flag(node_id))
		out.append({
			"node_id": node_id, "text_cn": str(row.text_cn),
			"granted": granted, "chapter_done": chapter_done,
		})
	return out
