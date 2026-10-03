## 开局引导（设计 09 §3.1）：HUD 上常驻一行「当前目标」，条件满足就自动推进。
##
## 数据源 `guide_step.csv`（4 步）。每行的 `condition` 就是**「这一步成为当前目标」的判据**：
## 取值是剧情旗标（`GameState.flags`），第一步用哨兵 `start`（开局即满足，表里没有对应的旗标）。
##
## 口径：**当前步 = 条件已满足的最后一行**——旗标一个个点亮，目标就一行行往前走。
## 最后一步（「出城上山」）的条件满足后停在最后一步：设计表只有 4 步，没有再往后的目标。
## 条件本身由谁点亮是各系统的事（悬赏板发 `flag_board_read`、招募发 `flag_ch_ci_joined`…），
## 这里只读旗标，不自己发明触发点。
class_name GuideService
extends RefCounted

## 第一步的哨兵：设计表里写的是「开局」，没有旗标可查。
const START_CONDITION := "start"


## 按 `sort_order` 排好的引导步骤（表里的顺序就是玩家看到的顺序）。
static func steps(db) -> Array:
	var rows: Array = db.rows("guide_step").duplicate()
	rows.sort_custom(func(a, b) -> bool: return int(a.sort_order) < int(b.sort_order))
	return rows


static func condition_met(state, condition: String) -> bool:
	if condition == START_CONDITION:
		return true
	if state == null:
		return false
	return state.has_flag(condition)


## 当前目标行；表为空（或没配 `start`）时返回 `{}`。
static func current(db, state) -> Dictionary:
	var result: Dictionary = {}
	for row: Resource in steps(db):
		if condition_met(state, str(row.condition)):
			result = {
				"step_id": str(row.step_id),
				"index": int(row.sort_order),
				"text_cn": str(row.text_cn),
				"condition": str(row.condition),
			}
	return result


## HUD 那一行的文案：`当前目标：…（2/4）`；没配引导表时返回空串（HUD 不占位）。
static func hud_text(db, state) -> String:
	var row: Dictionary = current(db, state)
	if row.is_empty():
		return ""
	return "当前目标：%s（%d/%d）" % [
		str(row["text_cn"]), int(row["index"]), steps(db).size(),
	]
