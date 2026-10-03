## 配置表行基类。
##
## 每张 CSV 对应一个行类，行类的 @export 属性与 CSV 列一一对应，
## 类型由行类声明，构建脚本据此把字符串转换成 int/float/bool/string。
## 主键由构建脚本写入 id（多主键用 "|" 连接），行类里不再声明 id 字段。
class_name TableRow
extends Resource

## 主键值。单主键为列值本身，复合主键按 table_registry.PRIMARY_KEYS 的顺序用 "|" 连接。
@export var id: String = ""


## 主键（字符串形式）。
func get_id() -> String:
	return id
