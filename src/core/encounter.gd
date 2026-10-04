## 一次明雷遭遇。
##
## 把 docs/design/02_地图与明雷.md 的「接触结果」表落成数据：接触方式决定战斗开局。
##   正面撞上        正常开战，按身法排序
##   从背后接触      我方全体先手一回合；敌方架势初始值 -30%
##   背后接触沉睡敌人 奇袭：首回合伤害 +50%，敌方无防备
##   进追击警戒圈被发现 敌方先手
##   被追击抓到      敌方先手，且首回合无法逃跑
##
## 架势、拆招、逃跑都已实现；`pending_rules` 现在只剩「奇袭时敌方首回合无防备」这一条——
## 设计原文只写了这六个字（02_地图与明雷.md 的接触结果表），没说「无防备」到底改什么
## （降格挡率？架势清零？还是不吃防御？），所以这里如实记下来、不替设计编效果。
class_name Encounter
extends RefCounted

## 阵营常量**只有一处出处**：`BattleActor.SIDE_*`（这里以前自己又写了一份 0/1——同一套值两处定义，
## 两边漂移了不会报错）。`first_side` 与模拟器比的是同一批数字，所以它们必须是同一个事实。
const BattleActorScript := preload("res://src/core/battle_actor.gd")
const SIDE_ALLY := BattleActorScript.SIDE_ALLY
const SIDE_ENEMY := BattleActorScript.SIDE_ENEMY

const CONTACT_FRONT := "front"
const CONTACT_BACK := "back"
const CONTACT_SLEEP := "ambush_sleep"
const CONTACT_SPOTTED := "spotted"
const CONTACT_CAUGHT := "caught"

const CONTACT_LABELS := {
	CONTACT_FRONT: "正面遭遇",
	CONTACT_BACK: "背后偷袭",
	CONTACT_SLEEP: "奇袭",
	CONTACT_SPOTTED: "被发现了",
	CONTACT_CAUGHT: "被追上",
}

var spawn_id: String = ""
var team_id: String = ""
var team_name: String = ""
var region_id: String = ""
var threat_tag: String = "green"
var is_elite: bool = false
var difficulty_id: String = "normal"
## 这场遭遇来自哪张图：overworld 或某个 scene_id
var source_scene: String = "overworld"
## 来源标识：明雷用 spawn_id，房间敌人用 room_id
var source_key: String = ""
## 队伍成员串（"en_wolf:3"）；房间队伍与隐藏 Boss 靠它建战斗单位
var members: String = ""
var contact: String = CONTACT_FRONT
## 首回合强制先手方；-1 表示按身法自然排序
var first_side: int = -1
## 首回合增伤（奇袭用，取 combat_const.surprise_damage_bonus）
var surprise_bonus: float = 0.0
## 设计写了、但代码还没法照做的规则说明（目前只剩奇袭的「敌方首回合无防备」，等设计定效果）
var pending_rules: PackedStringArray = PackedStringArray()

## 练习战（设计 09 §3.3 的城镇木桩）：**收益封顶的开关**——
## 结算时按它跳过掉落／铜钱／首杀／击败领悟，并把经验与熟练度卡在 `growth_const.dummy_*`。
## 组这场遭遇的地方是 `PracticeService.build_encounter()`。
var practice: bool = false
## 切磋（设计 19 §2.2）：赢了要给这位 NPC 加好感，所以遭遇里记下他是谁。
## 记在这里而不是会话里：结算发生在战斗场景，那时会话里的临时标记早没了（切过场景）。
## 空串 = 普通遭遇。组这场遭遇的地方是 `local_map_controller._start_spar()`。
var spar_npc: String = ""
## 切磋的**对手是这个人自己**（Q88 拍板 ①，2026-10-04）：同伴没有 `enemy_team` 行，
## 战斗层按这位同伴的 `character_base` ＋当前等级与配装现造一个镜像（不新增表行、不加列）。
## 空串 = 不是镜像切磋（普通队伍走 `team_id`）。组这场遭遇的地方同上。
var mirror_char: String = ""
## 背袭时敌方架势初始值降低比例（0 表示不降；数值取 combat_const.backstab_poise_reduce）
var enemy_poise_reduce: float = 0.0


## 由明雷刷新点 + 队伍 + 接触方式构造一次遭遇
static func build(db, spawn_row, team_row, contact: String, difficulty_id: String = "normal") -> Encounter:
	var encounter := Encounter.new()
	# 表行（Resource）与字典都支持；Dictionary.get 不接受默认值，所以统一走 _row_get
	encounter.spawn_id = str(_row_get(spawn_row, "spawn_id", ""))
	encounter.region_id = str(_row_get(spawn_row, "region_id", ""))
	encounter.team_id = str(_row_get(spawn_row, "team_id", ""))
	encounter.team_name = str(_row_get(team_row, "name_cn", encounter.team_id))
	encounter.threat_tag = str(_row_get(team_row, "threat_tag", "green"))
	encounter.is_elite = bool(_row_get(spawn_row, "is_elite", false))
	encounter.source_scene = str(_row_get(spawn_row, "source_scene", "overworld"))
	encounter.source_key = str(_row_get(spawn_row, "source_key", encounter.spawn_id))
	encounter.members = str(_row_get(team_row, "members", ""))
	encounter.difficulty_id = difficulty_id
	encounter.contact = contact
	encounter._apply_contact(db)
	return encounter


## 表行与字典通用取值
static func _row_get(row, key: String, fallback: Variant) -> Variant:
	if row == null:
		return fallback
	var value: Variant = row.get(key)
	return fallback if value == null else value


func _apply_contact(db) -> void:
	var consts := _combat_consts(db)
	match contact:
		CONTACT_BACK:
			first_side = SIDE_ALLY
			enemy_poise_reduce = float(consts.get("backstab_poise_reduce", 0.3))
		CONTACT_SLEEP:
			first_side = SIDE_ALLY
			surprise_bonus = float(consts.get("surprise_damage_bonus", 0.5))
			pending_rules.append("敌方首回合「无防备」（设计未定它的具体效果，暂按原样开打）")
		CONTACT_SPOTTED:
			first_side = SIDE_ENEMY
		CONTACT_CAUGHT:
			first_side = SIDE_ENEMY
			# 首回合不能逃这条已经由 BattleSimulator 真判了（flee_block_reason），不再记暂缓
		_:
			first_side = -1


func _combat_consts(db) -> Dictionary:
	var out: Dictionary = {}
	if db == null:
		return out
	for row: Resource in db.rows("combat_const"):
		out[str(row.const_id)] = float(row.value)
	return out


func contact_label() -> String:
	return str(CONTACT_LABELS.get(contact, contact))


## 遇敌提示文案（战斗开场用）
func headline() -> String:
	var suffix := "（精英）" if is_elite else ""
	return "%s%s　%s" % [contact_label(), suffix, team_name]


## 首回合要传给伤害管线的修正（奇袭增伤只给先手方）
func round_modifiers(round_index: int, actor_side: int) -> Dictionary:
	var mods: Dictionary = {}
	if round_index == 1 and surprise_bonus > 0.0 and actor_side == first_side:
		mods["dmg_up"] = surprise_bonus
	return mods


func summary() -> Dictionary:
	return {
		"spawn_id": spawn_id,
		"team_id": team_id,
		"team_name": team_name,
		"contact": contact,
		"contact_label": contact_label(),
		"first_side": first_side,
		"surprise_bonus": surprise_bonus,
		"difficulty": difficulty_id,
		"threat_tag": threat_tag,
		"is_elite": is_elite,
		"source_scene": source_scene,
		"source_key": source_key,
		"members": members,
	}
