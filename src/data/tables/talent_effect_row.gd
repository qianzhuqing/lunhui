## talent_effect.csv 行：一条天赋的效果（一个天赋可以有多条）。
##
## 0.16.0 新增（设计 12）。`target` 的前缀是**三选一**：
##   `attr:<属性>`  属性点（走 AttributeCalculator 的属性点层）
##   `stat:<派生值>` 派生数值（固定值层；百分比类在 `note` 里写明）
##   `rule:<规则名>` 规则性效果（图鉴奖励翻倍、事件判定 +1 这类表达不成数值的东西）
## **新增一个 `rule:` 目标 = 改一处代码 + 加一行表**（设计 12：不再有第三种写法）。
extends "res://src/data/table_row.gd"

@export var talent_id: String = ""
@export var target: String = ""
@export var value: float = 0.0
@export var note: String = ""
