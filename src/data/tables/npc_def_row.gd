## npc_def.csv 行：可以交往的 NPC（设计 19 §2.1，0.25 交付）。
##
## 本章的 NPC 从「按 E 触发固定结果」升级成**可以交往的人**：
## 身份／位置／称号／基本信息／切磋队伍都在这一行里。
## 好感度规则在 `npc_favor`、兑换与偷窃在 `npc_offer`、专属任务在 `npc_quest`。
extends "res://src/data/table_row.gd"

@export var npc_id: String = ""
@export var name_cn: String = ""
## 称号（「清风驿铁匠」这种），信息面板第一行显示
@export var title_cn: String = ""
## 所在位置：`map_local.scene_id`（城镇／副本）或 `map_region.node_id`（大地图区域，如 n_luoyanpo）
## ——列名是 `place_id`（设计 19 的写法，与 `story_node.place_id` 同一口径：地点可以是两者之一）
@export var place_id: String = ""
## NPC 等级（切磋与偷窃难度都按它算）
@export var level: int = 1
@export var faction: String = "civilian"
## 切磋队伍（引用 enemy_team.team_id）；留空 = 不能切磋
@export var spar_team_id: String = ""
@export var greet_text_cn: String = ""
## 基本信息（信息面板正文）：**要有人味**——刀疤、脾气、旧识，不然 NPC 就是自动售货机
@export var info_text_cn: String = ""
