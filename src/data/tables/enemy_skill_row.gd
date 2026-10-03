## enemy_skill.csv 行：一个敌人的一条招式（0.10.0 新增，见 06「敌人招式：谁会哪些招」）。
##
## 主键是 `enemy_id + skill_id`；`sort_order` 是出招偏好顺序（战斗里自动选招还会按期望伤害再挑一遍）。
## 这张表取代了原来写死在 `EnemyFactory` 里的 `AI_SKILL_MAP` / `ENEMY_SKILL_OVERRIDE`
## （见框架说明决策 246／Q48：设计给了表，就改成配表驱动）。
extends "res://src/data/table_row.gd"

@export var enemy_id: String = ""
@export var skill_id: String = ""
@export var sort_order: int = 1
## 给人看的说明：这招是干什么的（如「被近身时的自卫」）
@export var note: String = ""
