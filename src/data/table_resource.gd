## 一张配置表的容器资源，由 tools/build_tables.gd 生成到 data/generated/<table>.tres。
##
## 运行期只读它，不读 CSV：CSV 是设计师的唯一数值源，资源是代码的唯一读取入口。
class_name TableResource
extends Resource

## 表名，与 data/tables/<table>.csv 同名。
@export var table_name: String = ""

## 该表的主键列名（复合主键按顺序排列），用于构建 id。
@export var primary_keys: PackedStringArray = PackedStringArray()

## 行数据。元素是 table_registry.ROW_SCRIPTS 指定的行类实例。
@export var rows: Array = []

## 主键 → rows 下标。
@export var index: Dictionary = {}


## 按主键取行，取不到返回 null。
func get_row(row_id: Variant) -> Resource:
	var position: int = index.get(str(row_id), -1)
	if position < 0 or position >= rows.size():
		return null
	return rows[position]


## 全部主键，顺序与 rows 一致。
func ids() -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in rows:
		out.append(row.get("id"))
	return out


## 往表里塞一行**运行期**的行（不进 CSV、不进 data/generated）。
##
## 目前只有一处用途：创建角色的「不使用模板」路线——玩家自己分的七维不在任何 CSV 里，
## 得有一行 `character_base` 供 `get_row()` 查（装备、武学、面板全按 char_id 取模板）。
## 这一行随存档走（`GameState.custom_templates`），读档时重新注入。
## 主键重复时覆盖旧行（读档重复注入要幂等）。
func inject_row(row: Resource) -> void:
	var row_id := str(row.get("id"))
	var existing: int = index.get(row_id, -1)
	if existing >= 0 and existing < rows.size():
		rows[existing] = row
		return
	rows.append(row)
	index[row_id] = rows.size() - 1


## 按某列的值收集行（用于 drop_group 这类非主键分组）。
func rows_where(column: String, value: Variant) -> Array:
	var out: Array = []
	for row: Resource in rows:
		if row.get(column) == value:
			out.append(row)
	return out
