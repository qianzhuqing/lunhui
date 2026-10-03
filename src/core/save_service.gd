## 存档时机（02_地图与明雷.md「存档规则」）。
##
## | 地点 | 规则 | 落在哪 |
## |---|---|---|
## | 城镇 | 存档点，可自由存 | 小地图里按 F5（`save_game`）手动存；占位场景有「保存游戏」按钮 |
## | 大地图 | 切换小地图时自动存 | `enter_local_map()` 进入前存一次 |
## | 副本内 | 自动存，退出重进不掉本层进度 | 清房间（战斗结算）、开宝箱、触发点位、扫荡、事件判定后各存一次 |
##
## 「读」留在主菜单（存档槽列表已经在那儿），游戏内只做保存，免得两处各写一套槽位逻辑。
class_name SaveService
extends RefCounted

const SaveStoreScript := preload("res://src/core/save_store.gd")

var store
var state
## 最近一次保存的结果，界面拿它播提示
var last_ok := false
var last_reason := ""
var last_auto := false
var last_error := ""


func _init(save_store, game_state) -> void:
	store = save_store
	state = game_state


## 能不能存：有会话状态、有存档槽、目录可写
func can_save() -> Dictionary:
	if state == null:
		return {"ok": false, "error": "没有会话状态"}
	if int(state.slot) <= 0:
		return {"ok": false, "error": "这一局还没有存档槽（从主菜单新建或读档才有）"}
	return {"ok": true, "error": ""}


## 保存一次。automatic=true 表示自动存档（提示文案不同）。
## 返回 {ok, error, slot, reason, automatic}
func save(reason: String, automatic: bool = false) -> Dictionary:
	var ready := can_save()
	if not bool(ready["ok"]):
		last_ok = false
		last_error = str(ready["error"])
		return {"ok": false, "error": str(ready["error"]), "slot": 0, "reason": reason, "automatic": automatic}
	var result: Dictionary = store.save_slot(int(state.slot), state)
	last_ok = bool(result["ok"])
	last_error = str(result["error"])
	last_reason = reason
	last_auto = automatic
	return {
		"ok": last_ok, "error": last_error, "slot": int(state.slot),
		"reason": reason, "automatic": automatic,
	}


## 状态栏用的一句话：成功写「已自动保存（清房间）」，失败写原因
func describe() -> String:
	if not last_ok:
		return "存档失败：%s" % last_error
	return "%s保存成功（%s）" % ["已自动" if last_auto else "已", last_reason]


static func make_default():
	return SaveStoreScript.new(SaveStoreScript.default_dir())
