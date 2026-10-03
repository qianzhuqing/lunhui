## enemy_base.csv 行：敌人模板。
##
## 设计 10 §二（0.14.0）：敌人和角色**共用一套模板**——七维 + 等级 → `attr_to_stat`／`level_growth`
## → 派生数值，同一套算法、分开存数据（`character_base` / `enemy_base`）。
## 表里那七列 `attr_*` **填了**就走共用管线（`EnemyFactory._stats_from_attrs`）；**空着**
## （设计还没用平衡工具配数值）就退回下面那些旧的派生列，并出声点名——「先改表结构与工厂
## 跑通空数据，再一起配数值」就是这条（10 §三）。
extends "res://src/data/table_row.gd"

@export var enemy_id: String = ""
@export var name_cn: String = ""
## beast / neutral / bandit / rogue / hidden
@export var faction: String = ""
@export var level: int = 1
## 七维（与 `character_base` 同名同义）：填了就走共用管线，空着退回派生列
@export var attr_str: int = 0
@export var attr_con: int = 0
@export var attr_agi: int = 0
@export var attr_int: int = 0
@export var attr_luk: int = 0
@export var attr_wu: int = 0
@export var attr_gen: int = 0
## 旧派生列（过渡期回退用；七维配齐后由设计侧删除，见 10 §三）
@export var hp_base: int = 1
@export var atk_phys: float = 0.0
@export var atk_qi: float = 0.0
@export var def_phys: float = 0.0
@export var def_qi: float = 0.0
@export var speed: float = 0.0
@export var hit_rate: float = 0.0
@export var dodge_rate: float = 0.0
@export var crit_rate: float = 0.0
@export var poise: int = 0
## 内伤抗性：这一列**保留**（设计只取消了毒/火/流血三条特权列——三种元素抗性改为靠
## `enemy_passive` 装配内功获得，与角色同规则；内伤抗性角色侧本来就有 `stat_def.res_internal`）
@export var res_internal: float = 0.0
## 引用 drop_table.drop_group
@export var drop_group: String = ""
## CSV 列名为 exp，与 GDScript 全局函数 exp() 重名，故改名
@export var exp_reward: int = 0
@export var money: int = 0
## green / yellow / red / purple
@export var threat_tag: String = "green"
@export var ai_template: String = ""
@export var desc: String = ""


## 七维（`attr_str`…`attr_gen`）的取值表；**七列全空/全 0 时返回空字典**——
## 调用方（`EnemyFactory`）据此判断「这一行走共用管线还是退回派生列」。
func attr_map() -> Dictionary:
	var out := {
		"str": attr_str, "con": attr_con, "agi": attr_agi, "int": attr_int,
		"luk": attr_luk, "wu": attr_wu, "gen": attr_gen,
	}
	for key: String in out:
		if int(out[key]) != 0:
			return out
	return {}


## 旧派生列那套抗性快照（**只剩内伤**；毒/火/流血改由内功提供，见 `enemy_passive`）。
func resistances() -> Dictionary:
	return {
		"internal": res_internal,
	}
