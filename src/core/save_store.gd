## 存档槽的读写与枚举。
##
## 这一版只做「多存档槽 + JSON 落盘」，分周目存档、全局存档、版本迁移属于
## save_system 的后续范围（见 docs/dev/框架说明.md）。
##
## 目录可注入：游戏里用 user://saves，自检里注入 res://.logs/... 这样的临时目录，
## 避免测试依赖 user:// 的写权限。
class_name SaveStore
extends RefCounted

## `GameState` 走 `preload` 常量：这个文件**全是静态调用**（`from_dict`／`is_valid_dict`／`format_time`）
## 与类型标注——以前一个 `preload` 都没有，等于"能不能读档"压在 `.godot` 的全局类名缓存上
## （AGENTS 的硬规矩就是别这么干；2026-10-04 补：那是全项目唯一一处真调用的漏网）。
const GameStateScript := preload("res://src/core/game_state.gd")

const DEFAULT_DIR := "user://saves"
const DEFAULT_SLOT_COUNT := 6
const FILE_PREFIX := "slot_"
const FILE_EXT := ".json"

var base_dir: String
var slot_count: int


## 存档目录的默认值。
##
## 优先级：命令行调试参数 `-- --save-dir=<路径>` → 项目设置 `lunhui/save_dir` → user://saves。
## 调试参数是给受限环境（沙箱、只读用户目录）跑真实场景自检用的。
static func default_dir() -> String:
	for argument: String in OS.get_cmdline_user_args():
		if argument.begins_with("--save-dir="):
			return argument.substr("--save-dir=".length())
	var configured := str(ProjectSettings.get_setting("lunhui/save_dir", ""))
	if not configured.is_empty():
		return configured
	return DEFAULT_DIR


func _init(dir_path: String = DEFAULT_DIR, slots: int = DEFAULT_SLOT_COUNT) -> void:
	base_dir = dir_path
	slot_count = maxi(1, slots)


func slot_path(slot: int) -> String:
	return "%s/%s%02d%s" % [base_dir, FILE_PREFIX, slot, FILE_EXT]


## 确保存档目录存在，返回是否可用。
func ensure_dir() -> bool:
	var error := DirAccess.make_dir_recursive_absolute(base_dir)
	return error == OK or error == ERR_ALREADY_EXISTS


func slot_exists(slot: int) -> bool:
	return FileAccess.file_exists(slot_path(slot))


## 枚举全部槽位。每项：{slot, exists, corrupt, state, saved_unix, label, error}
func list_slots(db = null) -> Array:
	var out: Array = []
	for slot in range(1, slot_count + 1):
		out.append(describe_slot(slot, db))
	return out


func describe_slot(slot: int, db = null) -> Dictionary:
	var entry := {
		"slot": slot,
		"exists": false,
		"corrupt": false,
		"state": null,
		"saved_unix": 0,
		"label": "空存档位",
		"error": "",
	}
	if not slot_exists(slot):
		return entry
	entry["exists"] = true
	var result := load_slot(slot, db)
	if not result["ok"]:
		entry["corrupt"] = true
		entry["label"] = "存档损坏"
		entry["error"] = result["error"]
		return entry
	var state: GameStateScript = result["state"]
	entry["state"] = state
	entry["saved_unix"] = state.saved_unix
	entry["label"] = state.short_label(db) if db != null else GameStateScript.format_time(state.saved_unix)
	return entry


func has_any_save() -> bool:
	for slot in range(1, slot_count + 1):
		if slot_exists(slot):
			return true
	return false


## 读一个槽。db 用于把旧版本存档迁移到当前格式（补初始装备）。返回 {ok, error, state}
func load_slot(slot: int, db = null) -> Dictionary:
	var path := slot_path(slot)
	if not FileAccess.file_exists(path):
		return {"ok": false, "error": "第 %d 格没有存档" % slot, "state": null}
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return {"ok": false, "error": "存档读不出来：%s" % path, "state": null}
	var text := file.get_as_text()
	file.close()
	var json := JSON.new()
	if json.parse(text) != OK:
		# 用 JSON 实例而不是 JSON.parse_string：不要往引擎日志里喷错误，
		# 把原因交给菜单显示给玩家。
		return {"ok": false, "error": "存档损坏：%s" % json.get_error_message(), "state": null}
	var parsed: Variant = json.data
	if not GameStateScript.is_valid_dict(parsed):
		return {"ok": false, "error": "存档格式不对或已损坏：%s" % path, "state": null}
	var state := GameStateScript.from_dict(parsed, db)
	if state == null:
		return {"ok": false, "error": "存档解析失败：%s" % path, "state": null}
	state.slot = slot
	return {"ok": true, "error": "", "state": state}


## 写一个槽。返回 {ok, error}
func save_slot(slot: int, state: GameStateScript) -> Dictionary:
	if not ensure_dir():
		return {"ok": false, "error": "存档目录建不出来，user:// 可能不可写"}
	var file := FileAccess.open(slot_path(slot), FileAccess.WRITE)
	if file == null:
		return {"ok": false, "error": "存档写不进去：%s" % slot_path(slot)}
	state.slot = slot
	state.touch_saved()
	file.store_string(JSON.stringify(state.to_dict(), "  "))
	file.close()
	return {"ok": true, "error": ""}


func delete_slot(slot: int) -> bool:
	if not slot_exists(slot):
		return false
	return DirAccess.remove_absolute(slot_path(slot)) == OK


## 第一个空槽，没有则返回 -1。
func first_free_slot() -> int:
	for slot in range(1, slot_count + 1):
		if not slot_exists(slot):
			return slot
	return -1


## 最旧的**可用**存档槽（按 saved_unix），跳过损坏的，没有则返回 -1。
func oldest_slot() -> int:
	var oldest := -1
	var oldest_time := 0
	for entry: Dictionary in list_slots():
		if not entry["exists"] or entry["corrupt"]:
			continue
		var saved_unix := int(entry["saved_unix"])
		if oldest < 0 or saved_unix < oldest_time:
			oldest = entry["slot"]
			oldest_time = saved_unix
	return oldest


## 第一个损坏的槽位（可以回收来用），没有则返回 -1。
func first_corrupt_slot() -> int:
	for entry: Dictionary in list_slots():
		if entry["exists"] and entry["corrupt"]:
			return int(entry["slot"])
	return -1


## 新游戏该写哪个槽：空槽 → 损坏槽（回收）→ 覆盖最旧的可用存档。
## 返回 {ok, slot, overwrite, error}
func slot_for_new_game() -> Dictionary:
	var free_slot := first_free_slot()
	if free_slot > 0:
		return {"ok": true, "slot": free_slot, "overwrite": false, "error": ""}
	var corrupt_slot := first_corrupt_slot()
	if corrupt_slot > 0:
		return {"ok": true, "slot": corrupt_slot, "overwrite": true, "error": ""}
	var victim := oldest_slot()
	if victim > 0:
		return {"ok": true, "slot": victim, "overwrite": true, "error": ""}
	# 兜底：目录里有东西但都读不出来
	for slot in range(1, slot_count + 1):
		if slot_exists(slot):
			return {"ok": true, "slot": slot, "overwrite": true, "error": ""}
	return {"ok": false, "slot": 0, "overwrite": false, "error": "没有可用的存档槽"}
