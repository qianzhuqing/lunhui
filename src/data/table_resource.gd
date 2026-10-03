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


## 按某列的值收集行（用于 drop_group 这类非主键分组）。
func rows_where(column: String, value: Variant) -> Array:
	var out: Array = []
	for row: Resource in rows:
		if row.get(column) == value:
			out.append(row)
	return out
