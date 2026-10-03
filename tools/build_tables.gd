## 构建期配置表转换：data/tables/*.csv → data/generated/*.tres。
##
## 用法（headless，注意 --log-file 是必须的，否则 user://logs 写失败会崩）：
##   godot --headless --path . --log-file .logs/build.log --script res://tools/build_tables.gd
##
## 类型来自行类的 @export 声明，所以 CSV 列与行类字段对不上会立刻报错，
## 校验规则见 src/core/table_validator.gd。
extends SceneTree

const Registry := preload("res://src/data/table_registry.gd")
const TableResourceScript := preload("res://src/data/table_resource.gd")
const TableDbScript := preload("res://src/core/table_db.gd")
const TableValidatorScript := preload("res://src/core/table_validator.gd")


func _initialize() -> void:
	quit(_run())


func _run() -> int:
	var errors := PackedStringArray()
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(Registry.OUTPUT_DIR))

	var db = TableDbScript.new()
	var total_rows := 0
	for table_name: String in Registry.TABLES:
		var table: Resource = _build_table(table_name, errors)
		if table == null:
			continue
		db.tables[table_name] = table
		total_rows += table.rows.size()
		var path := "%s/%s.tres" % [Registry.OUTPUT_DIR, table_name]
		var save_error := ResourceSaver.save(table, path)
		if save_error != OK:
			errors.append("保存 %s 失败（错误码 %d）" % [path, save_error])

	for message: String in TableValidatorScript.validate(db):
		errors.append(message)

	if not errors.is_empty():
		printerr("=== 配置表构建失败，共 %d 条问题 ===" % errors.size())
		for message: String in errors:
			printerr("  - " + message)
		return 1

	print("=== 配置表构建完成：%d 张表 / %d 行 → %s ===" % [db.tables.size(), total_rows, Registry.OUTPUT_DIR])
	return 0


func _build_table(table_name: String, errors: PackedStringArray) -> Resource:
	var csv_path := "%s/%s.csv" % [Registry.TABLES_DIR, table_name]
	if not FileAccess.file_exists(csv_path):
		errors.append("缺少 CSV：%s" % csv_path)
		return null

	var table_lines := _read_csv(csv_path, errors)
	if table_lines.is_empty():
		return null
	var header: PackedStringArray = table_lines[0]
	var body: Array = table_lines.slice(1)

	var row_script_path: String = Registry.ROW_SCRIPTS[table_name]
	var row_script: Script = load(row_script_path)
	if row_script == null:
		errors.append("行类脚本无法加载：%s" % row_script_path)
		return null

	var template: Resource = row_script.new()
	var properties := _exported_properties(template)

	# CSV 列 ↔ 行类属性 双向对齐检查：任何一边多了少了都当场报错
	var column_to_property: Dictionary = {}
	for column: String in header:
		var property_name := Registry.property_of(table_name, column)
		if not properties.has(property_name):
			errors.append("%s 的 CSV 列 '%s' 在 %s 里没有对应属性" % [table_name, column, row_script_path])
			continue
		column_to_property[column] = property_name
	for property_name: String in properties:
		var column := Registry.column_of(table_name, property_name)
		if not header.has(column):
			errors.append("%s 的行类属性 '%s' 在 CSV 表头里没有对应列" % [table_name, property_name])

	var rows: Array = []
	for line_index in range(body.size()):
		var line: PackedStringArray = body[line_index]
		if line.size() != header.size():
			errors.append("%s 第 %d 行有 %d 列，表头是 %d 列" % [table_name, line_index + 2, line.size(), header.size()])
			continue
		var row: Resource = row_script.new()
		for index in range(header.size()):
			var column: String = header[index]
			var raw: String = line[index]
			var property_name: String = column_to_property.get(column, "")
			if property_name.is_empty():
				continue
			row.set(property_name, _convert(raw, int(properties[property_name])))
		# 主键用「行类里的强类型值」拼，保证与运行期校验算出来的一致
		var primary_values: Dictionary = {}
		for key: String in Registry.PRIMARY_KEYS[table_name]:
			primary_values[key] = row.get(Registry.property_of(table_name, key))
		row.id = Registry.build_id(table_name, primary_values)
		rows.append(row)

	var table: Resource = TableResourceScript.new()
	table.table_name = table_name
	table.primary_keys = PackedStringArray(Registry.PRIMARY_KEYS[table_name])
	table.rows = rows
	var index: Dictionary = {}
	for row_index in range(rows.size()):
		index[rows[row_index].id] = row_index
	table.index = index
	return table


## 读 CSV：剥 BOM、跳过空行、保留表头。
func _read_csv(path: String, errors: PackedStringArray) -> Array:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		errors.append("无法读取 %s" % path)
		return []
	var lines: Array = []
	var header := file.get_csv_line()
	if header.size() > 0 and header[0].begins_with("\ufeff"):
		header[0] = header[0].substr(1)
	lines.append(header)
	while not file.eof_reached():
		var line := file.get_csv_line()
		if line.size() == 1 and line[0].strip_edges().is_empty():
			continue
		lines.append(line)
	file.close()
	return lines


## 行类里声明的 @export 成员：{属性名: 类型}
func _exported_properties(instance: Resource) -> Dictionary:
	var out: Dictionary = {}
	for property: Dictionary in instance.get_property_list():
		if (int(property["usage"]) & PROPERTY_USAGE_SCRIPT_VARIABLE) == 0:
			continue
		var name: String = property["name"]
		if name == "id":
			# 基类主键字段，由构建脚本统一写入，不是 CSV 列
			continue
		out[name] = int(property["type"])
	return out


## CSV 字符串 → 行类字段类型。空值取类型默认值（0 / 0.0 / false / ""）。
func _convert(raw: String, type: int) -> Variant:
	var value := raw.strip_edges()
	match type:
		TYPE_STRING:
			return value
		TYPE_INT:
			return 0 if value.is_empty() else int(round(value.to_float()))
		TYPE_FLOAT:
			return 0.0 if value.is_empty() else value.to_float()
		TYPE_BOOL:
			return value == "1" or value.to_lower() == "true"
		_:
			return value
