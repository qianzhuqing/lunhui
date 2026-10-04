## origin_def.csv 行：创建角色「使用模板」那一步的出身卡。
##
## 0.17.0 新增（设计 13）：5 张卡**直接复用 `character_base` 现有 5 行**——
## 出身卡只补「展示信息」（玩法标签／一句话定位／排序），
## 七维／武器／起始武学与装备仍然只有 `character_base` 一处定义，别在这里再抄一份。
extends "res://src/data/table_row.gd"

@export var origin_id: String = ""
## 引用 character_base.char_id —— 这张卡用的是哪一行模板
@export var char_id: String = ""
@export var name_cn: String = ""
## 创建时**预填进"名字"那一格的真名**（设计 21 §七 第 5 条／决策 323）。
##
## 为什么不能直接用 `name_cn`：那列是**卡的标题**——林铁山／苏九娘那几位恰好是本人名字，
## 而书生那张写的是「家道失落的书生」这种**类名**，预填进去就会给主角起个类名。
## 空值时的兜底是退回 `name_cn`（老档／没配的卡不会崩）。
@export var default_name_cn: String = ""
## 玩法标签（探索型／刚猛型／毒攻型／刺客型／内功型）
@export var playstyle: String = ""
## 一句话定位，创建界面上直接显示
@export var tagline: String = ""
@export var sort_order: int = 0
@export var desc: String = ""
