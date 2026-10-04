## 设备设置的落盘（音量 ＋ 自动战斗开关）。
##
## **为什么不放进存档**：音量是「这台机器／这个玩家」的设置，不该跟着存档槽走
## （换个存档玩，音量跟着变会很怪）。所以它落在**存档目录旁边**的 `settings.cfg`——
## 目录解析与存档共用一处（`SaveStore.default_dir()`，支持 `--save-dir=<路径>` 覆盖），
## 沙箱与自检里也能指到可写目录。
##
## **写不进去不许挡住游戏**（与存档同一条纪律）：`save_volume()` 把 {ok,error} 交给调用方决定怎么说；
## 读不到／读坏了退回默认音量 1.0——**绝不悄悄变成 0**（"静音"是最容易看起来正常的坏结果）。
class_name SettingsStore
extends RefCounted

const SaveStoreScript := preload("res://src/core/save_store.gd")

const FILE_NAME := "settings.cfg"
const SECTION := "audio"
const KEY_VOLUME := "volume"
const DEFAULT_VOLUME := 1.0
## 自动战斗开关（设计 11 §四）：默认**关**——自动是省事不是夺权。
const SECTION_BATTLE := "battle"
const KEY_AUTO_BATTLE := "auto_battle"
const DEFAULT_AUTO_BATTLE := false

var base_dir: String


func _init(dir_path: String = "") -> void:
	base_dir = dir_path if not dir_path.is_empty() else SaveStoreScript.default_dir()


func file_path() -> String:
	return "%s/%s" % [base_dir, FILE_NAME]


## 读音量。返回 {ok, volume, error}
func load_volume() -> Dictionary:
	if not FileAccess.file_exists(file_path()):
		return {"ok": true, "volume": DEFAULT_VOLUME, "error": ""}
	var config := ConfigFile.new()
	var error := config.load(file_path())
	if error != OK:
		return {"ok": false, "volume": DEFAULT_VOLUME, "error": "设置文件读不了（错误码 %d）" % error}
	var value = config.get_value(SECTION, KEY_VOLUME, DEFAULT_VOLUME)
	if typeof(value) != TYPE_FLOAT and typeof(value) != TYPE_INT:
		return {"ok": false, "volume": DEFAULT_VOLUME, "error": "音量不是数字：%s" % str(value)}
	return {"ok": true, "volume": clampf(float(value), 0.0, 1.0), "error": ""}


## 写音量。返回 {ok, error}
func save_volume(value: float) -> Dictionary:
	return _write(SECTION, KEY_VOLUME, clampf(value, 0.0, 1.0))


## 读自动战斗开关。返回 {ok, enabled, error}
##
## 与音量同一条纪律：**读不到／读坏退回默认（关）**，不因为设置文件坏了就替玩家开自动。
func load_auto_battle() -> Dictionary:
	if not FileAccess.file_exists(file_path()):
		return {"ok": true, "enabled": DEFAULT_AUTO_BATTLE, "error": ""}
	var config := ConfigFile.new()
	var error := config.load(file_path())
	if error != OK:
		return {"ok": false, "enabled": DEFAULT_AUTO_BATTLE, "error": "设置文件读不了（错误码 %d）" % error}
	var value = config.get_value(SECTION_BATTLE, KEY_AUTO_BATTLE, DEFAULT_AUTO_BATTLE)
	if typeof(value) != TYPE_BOOL and typeof(value) != TYPE_INT:
		return {"ok": false, "enabled": DEFAULT_AUTO_BATTLE, "error": "自动战斗开关不是布尔：%s" % str(value)}
	return {"ok": true, "enabled": bool(value), "error": ""}


## 写自动战斗开关。返回 {ok, error}
func save_auto_battle(enabled: bool) -> Dictionary:
	return _write(SECTION_BATTLE, KEY_AUTO_BATTLE, enabled)


## 落盘一个键——**先读回整份配置再写**。
##
## 以前每种设置各自 `ConfigFile.new()` 后整份保存：文件里只有那一次写进去的节，
## 于是「按一下音量」会把自动战斗开关冲掉（反过来也一样）。设置一多这条就会真丢东西，
## 所以落盘只留这一处。
func _write(section: String, key: String, value: Variant) -> Dictionary:
	var error := DirAccess.make_dir_recursive_absolute(base_dir)
	if error != OK and error != ERR_ALREADY_EXISTS:
		return {"ok": false, "error": "建不了设置目录：%s（错误码 %d）" % [base_dir, error]}
	var config := ConfigFile.new()
	if FileAccess.file_exists(file_path()):
		# 读坏了也不能把别的设置清零：退回空配置，只写这一个键
		config.load(file_path())
	config.set_value(section, key, value)
	error = config.save(file_path())
	if error != OK:
		return {"ok": false, "error": "设置写不进去：%s（错误码 %d）" % [file_path(), error]}
	return {"ok": true, "error": ""}
