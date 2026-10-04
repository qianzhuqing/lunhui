## 运行期表库：只读 data/generated/*.tres，绝不读 CSV。
##
## CSV 是设计师的唯一数值源，构建期由 tools/build_tables.gd 转成资源，
## 运行期所有系统都通过 TableDb 取数，方便校验与替换。
class_name TableDb
extends RefCounted

const Registry := preload("res://src/data/table_registry.gd")

## 表名 → TableResource
var tables: Dictionary = {}

## 加载错误，空数组表示全部加载成功。
var errors: PackedStringArray = PackedStringArray()


## 加载全部生成资源，返回是否全部成功。
func load_all(dir_path: String = "res://data/generated") -> bool:
	tables.clear()
	errors = PackedStringArray()
	for table_name: String in Registry.TABLES:
		var path := "%s/%s.tres" % [dir_path, table_name]
		if not ResourceLoader.exists(path):
			errors.append("缺少生成资源 %s，请先运行 tools/build_tables.gd 生成配置表" % path)
			continue
		var resource: Resource = load(path)
		if resource == null or resource.get("rows") == null:
			errors.append("生成资源结构不对：%s" % path)
			continue
		tables[table_name] = resource
	_warn_if_generated_is_stale(dir_path)
	return errors.is_empty()


## 数据新鲜度：CSV 比生成的 `.tres` 新，就说明"改完表没重建，现在读的是旧数据"。
##
## 由来（2026-10-03）：观测工具的脚本**不重建配置表**，直接读 `data/generated`——
## 改完 CSV 跑它，量到的是上一版数据，而它照样打印一张像模像样的表（见框架说明决策 167）。
## 那次的错误结论被写进了两份文档和给策划的问题里，所以这里加一道**加载时的提醒**：
## 只 `push_warning` 不报错（导出包里没有 CSV，正常跑也不该被打断；时间戳读不到就不猜）。
func _warn_if_generated_is_stale(dir_path: String) -> void:
	var csv_dir: String = Registry.TABLES_DIR
	if not DirAccess.dir_exists_absolute(csv_dir):
		return     # 导出包：CSV 不在包里，跳过
	var newest_csv := 0
	for file_name: String in DirAccess.get_files_at(csv_dir):
		if file_name.ends_with(".csv"):
			newest_csv = maxi(newest_csv, int(FileAccess.get_modified_time(csv_dir.path_join(file_name))))
	if newest_csv <= 0:
		return
	var oldest_generated := 0
	for table_name: String in Registry.TABLES:
		var path := "%s/%s.tres" % [dir_path, table_name]
		if not ResourceLoader.exists(path):
			continue
		var stamp := int(FileAccess.get_modified_time(path))
		if stamp <= 0:
			return     # 时间戳不可靠（某些平台／打包），不猜
		oldest_generated = stamp if oldest_generated == 0 else mini(oldest_generated, stamp)
	if oldest_generated > 0 and newest_csv > oldest_generated:
		push_warning(
			"[TableDb] data/generated 里有比 CSV 更旧的文件：你改完表没重建，现在读的是**旧数据**——"
			+ "先跑 tools/build_tables.gd（或 run_tests.bat／run_all_checks.bat）"
		)


func has_table(table_name: String) -> bool:
	return tables.has(table_name)


func rows(table_name: String) -> Array:
	var resource: Resource = tables.get(table_name)
	if resource == null:
		return []
	return resource.get("rows")


## 按主键取行，取不到返回 null。
func get_row(table_name: String, row_id: Variant) -> Resource:
	var resource: Resource = tables.get(table_name)
	if resource == null:
		return null
	return resource.get_row(row_id)


## 按主键取行，取不到时报错并返回 null（用在「配置缺失就该立刻炸」的地方）。
func require_row(table_name: String, row_id: Variant) -> Resource:
	var row := get_row(table_name, row_id)
	if row == null:
		push_error("[TableDb] 找不到配置：%s[%s]" % [table_name, row_id])
	return row


## 按某列的值筛行（用于 drop_group 这类非主键分组）。
func rows_where(table_name: String, column: String, value: Variant) -> Array:
	var resource: Resource = tables.get(table_name)
	if resource == null:
		return []
	return resource.rows_where(column, value)


## 往表里注入一行运行期的行（见 `TableResource.inject_row` 的说明）。
## 只影响内存：CSV 与 `data/generated` 一个字都不动。
func inject_row(table_name: String, row: Resource) -> void:
	var resource: Resource = tables.get(table_name)
	if resource == null:
		push_error("[TableDb] 没有这张表，注入失败：%s" % table_name)
		return
	resource.inject_row(row)


## 某列出现过的全部取值（用于引用校验）。
func column_values(table_name: String, column: String) -> Dictionary:
	var out: Dictionary = {}
	for row: Resource in rows(table_name):
		out[str(row.get(column))] = true
	return out


func has_value(table_name: String, column: String, value: Variant) -> bool:
	for row: Resource in rows(table_name):
		if row.get(column) == value:
			return true
	return false


## 表名 → 行数，用于引导场景打印摘要。
func summary() -> Dictionary:
	var out: Dictionary = {}
	for table_name: String in Registry.TABLES:
		out[table_name] = rows(table_name).size()
	return out


## 显示名兜底：给玩家看的文案一律走这里，**不要把表里的英文 id 甩到界面上**。
##
## 起因（2026-10-03 换对象审计）：文案里查出成片的 id 泄露——「打开宝箱：item_health_pill ×2、
## eq_sword_01#1」「需要 item_pickaxe」「要用 poison 击杀目标」「挖通！直接通到「hf3_dungeon」」。
## 根源不是某处写错，而是每个界面各自拿 id 当名字用；所以把「id → 中文名」收在一处。
##
## `row_id` 可以是装备实例 id（`基础id#序号`），会自动剥掉 `#序号`。
## 查不到就原样返回 id——查不到说明数据或调用写错了，**不要在这里编个假名字**。
const DISPLAY_NAME_SOURCES := [
	["item_base", "name_cn"],
	["equip_base", "name_cn"],
	["skill_base", "name_cn"],
	["enemy_base", "name_cn"],
	["character_base", "name_cn"],
	["building_def", "name_cn"],
	["hidden_trigger", "name_cn"],
	["map_region", "name_cn"],
	["map_local", "name_cn"],
	["dungeon_room", "room_name"],
	["status_effect", "name_cn"],
	["damage_type", "name_cn"],
	["rarity_def", "name_cn"],
	["difficulty_config", "name_cn"],
	["attribute_def", "name_cn"],
	["weapon_type_def", "name_cn"],
	["equip_slot_def", "name_cn"],
]


func display_name(row_id: String) -> String:
	if row_id.is_empty():
		return ""
	var bare := row_id.split("#", false)[0]
	for source: Array in DISPLAY_NAME_SOURCES:
		var row: Resource = get_row(str(source[0]), bare)
		if row != null:
			return str(row.get(str(source[1])))
	return row_id
