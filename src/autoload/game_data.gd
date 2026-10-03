## 全局表库单例。
##
## 启动时加载 data/generated/*.tres 并跑一遍交叉校验。
## 注意：本脚本故意不声明 class_name —— autoload 名与全局类名同名会冲突。
extends Node

const TableDbScript := preload("res://src/core/table_db.gd")
const TableValidatorScript := preload("res://src/core/table_validator.gd")

const DIRECTORY := "res://data/generated"

signal reloaded(ok: bool, errors: PackedStringArray)

var db
var errors: PackedStringArray = PackedStringArray()
var ready_ok := false
var verbose := true


func _ready() -> void:
	reload()


## 重新加载全部表并校验。返回是否通过。
func reload() -> bool:
	db = TableDbScript.new()
	db.load_all(DIRECTORY)
	errors = TableValidatorScript.validate(db)
	ready_ok = errors.is_empty()
	if verbose:
		if ready_ok:
			print("[GameData] 配置表就绪：%d 张表" % db.tables.size())
		else:
			print("[GameData] 配置表未就绪，%d 条问题（运行 tools/run_tests.bat 或检查 data/generated/）" % errors.size())
	reloaded.emit(ready_ok, errors)
	return ready_ok


func get_row(table_name: String, row_id: Variant) -> Resource:
	if db == null:
		return null
	return db.get_row(table_name, row_id)


func require_row(table_name: String, row_id: Variant) -> Resource:
	if db == null:
		push_error("[GameData] 表库尚未加载")
		return null
	return db.require_row(table_name, row_id)


func rows(table_name: String) -> Array:
	if db == null:
		return []
	return db.rows(table_name)


## 重新跑一遍校验，返回报错列表（空表示通过）。
func validate() -> PackedStringArray:
	if db == null:
		return PackedStringArray(["表库尚未加载，请先调用 reload()"])
	return TableValidatorScript.validate(db)
