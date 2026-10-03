## 启动菜单的决策逻辑。
##
## 与场景节点分开：这里只决定「该做什么」，退出与切场景由视图执行，
## 所以整套菜单流程能在 headless 下被用例覆盖（见 tests/test_menu.gd）。
class_name MenuController
extends RefCounted

const GameStateScript := preload("res://src/core/game_state.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")

const ACTION_PLAY := "play"
const ACTION_QUIT := "quit"
const ACTION_NONE := "none"

## 进游戏后落在哪一页（设计 09 §二）。
## **读档一律回大地图**（不管存档时人在哪，落点用存档里的 `world_pos`）；
## 新建游戏仍从占位枢纽页起步——那里有「进入大地图」的入口与音量开关。
const START_HUB := "hub"
const START_OVERWORLD := "overworld"

var slots: Array = []
var message: String = ""

var _db
var _store: SaveStore
var _party_ids: PackedStringArray
var _difficulty_id: String


func _init(
	db,
	store: SaveStore,
	party_ids: PackedStringArray = PackedStringArray(),
	difficulty_id: String = "normal"
) -> void:
	_db = db
	_store = store
	_party_ids = party_ids
	_difficulty_id = difficulty_id
	refresh()


func store() -> SaveStore:
	return _store


## 重新读一遍存档列表（返回主菜单或删档后调用）。
func refresh() -> void:
	slots = _store.list_slots(_db)
	message = ""


func has_saves() -> bool:
	for entry: Dictionary in slots:
		if entry["exists"]:
			return true
	return false


func save_count() -> int:
	var total := 0
	for entry: Dictionary in slots:
		if entry["exists"]:
			total += 1
	return total


## 读取存档是否可用（有档才能点）。
func can_load() -> bool:
	return has_saves()


## 新建游戏是否可用（配置表就绪才能开局）。
func can_start_new_game() -> bool:
	return _db != null and not _db.tables.is_empty()


## 新建游戏：挑槽位 → 造状态 → 立刻落盘（新游戏自动存档）。
## 返回 {ok, action, slot, state, overwrite, message}
func new_game() -> Dictionary:
	if not can_start_new_game():
		return _result(false, ACTION_NONE, 0, null, false, "配置表未就绪，先运行 tools\\run_tests.bat 生成配置表")
	var placement: Dictionary = _store.slot_for_new_game()
	if not placement["ok"]:
		return _result(false, ACTION_NONE, 0, null, false, str(placement["error"]))
	var slot := int(placement["slot"])
	var state = GameStateScript.new_game(_db, _difficulty_id, _party_ids, slot)
	if state == null or state.party_size() == 0:
		return _result(false, ACTION_NONE, slot, null, false, "开局队伍为空，检查 character_base.csv")
	var save_result: Dictionary = _store.save_slot(slot, state)
	var notice := ""
	if not save_result["ok"]:
		notice = "（自动存档失败：%s，本次进度不会保留）" % save_result["error"]
	elif bool(placement["overwrite"]):
		notice = "（存档位已满，已覆盖最旧的存档）"
	var out := _result(true, ACTION_PLAY, slot, state, bool(placement["overwrite"]), notice)
	out["start_scene"] = START_HUB
	return out


## 读取存档：载入指定槽。返回 {ok, action, slot, state, message}
func load_game(slot: int) -> Dictionary:
	var result: Dictionary = _store.load_slot(slot, _db)
	if not result["ok"]:
		return _result(false, ACTION_NONE, slot, null, false, str(result["error"]))
	var state = result["state"]
	var notice := "已读取第 %d 格存档" % slot
	if state.migrated_from > 0 or state.pruned_characters > 0 or state.reclaimed_equipped > 0:
		# 旧版本存档、档里有已下架的角色、或者穿着当前配表装不下的装备：整理完立刻写回，玩家无感
		var saved: Dictionary = _store.save_slot(slot, state)
		if saved["ok"]:
			if state.migrated_from > 0:
				notice = "已读取第 %d 格存档（已从 v%d 升级到 v%d）" % [slot, state.migrated_from, state.version]
			elif state.pruned_characters > 0:
				notice = "已读取第 %d 格存档（已清理 %d 个已下架角色）" % [slot, state.pruned_characters]
			else:
				notice = "已读取第 %d 格存档（已按当前配表退回 %d 件穿不上的装备）" % [slot, state.reclaimed_equipped]
		else:
			notice = "已读取第 %d 格存档（迁移写回失败：%s）" % [slot, saved["error"]]
	var out := _result(true, ACTION_PLAY, slot, state, false, notice)
	# 设计 09 §二：读档一律回大地图（落点用存档里的 world_pos，没有就用默认出生点）
	out["start_scene"] = START_OVERWORLD
	return out


## 退出游戏：只回一个动作，真正退出交给视图（headless 用例不会真的退）。
func quit_game() -> Dictionary:
	return _result(true, ACTION_QUIT, 0, null, false, "")


## 给 UI 用的槽位显示文案。
func slot_labels() -> PackedStringArray:
	var out := PackedStringArray()
	for entry: Dictionary in slots:
		out.append("第 %d 格　%s" % [entry["slot"], entry["label"]])
	return out


func _result(
	ok: bool,
	action: String,
	slot: int,
	state,
	overwrite: bool,
	notice: String
) -> Dictionary:
	if not notice.is_empty():
		message = notice
	return {
		"ok": ok,
		"action": action,
		"slot": slot,
		"state": state,
		"overwrite": overwrite,
		"message": notice,
	}
