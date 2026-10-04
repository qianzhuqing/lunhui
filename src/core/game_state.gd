## 一局游戏的状态快照。
##
## 只存「以后能重建现场」的最小数据：队伍、难度、章节、时间戳。
## 存档格式里不放 Resource 引用，全部是 JSON 能表达的类型，方便以后做版本迁移。
class_name GameState
extends RefCounted

const InventoryScript := preload("res://src/core/inventory.gd")
const SkillLoadoutScript := preload("res://src/core/skill_loadout.gd")
const PityTrackerScript := preload("res://src/core/pity_tracker.gd")
## 章节显示名从表里取（0.22.0 起 `chapter_def` 才是章节的唯一出处）
const ChapterServiceScript := preload("res://src/core/chapter_service.gd")

## 存档结构版本，格式变了要升这个号并写迁移。
## v1 → v2：加入背包与装备（inventory）、加点（char_allocations）与队伍经验池（party_exp）。
## v2 → v3：加入武学熟练度（skill_mastery）。
## v3 → v4：加入已学武学（char_learned）与装配（char_loadouts）。
## v4 → v5：装备实例带上随机词条（inventory 里的 affixes，缺省为空）。
## v5 → v6：加入商店交易状态（shop_state：累计买入次数与回购列表）。
## v6 → v7：加入副本完成度记录（dungeon_records）与抽卡保底计数（pity）。
## v7 → v8：加入大地图揭开记录（revealed_nodes，走出来的地图要留着）。
## v8 → v9：加入事件判定记录（event_checks）与剧情旗标（flags）。
## v9 → v10：加入首杀掉落记录（first_kills）。首杀奖励是「确定的惊喜」，
##           只能领一次，所以必须进存档——留在会话里会让玩家退出重进刷首杀。
##           老档（v9 及更早）没有这份记录，一律当作「还没打过首杀」。
## v10 → v11：加入战斗外气血（char_hp）。05 文档的医馆治疗要按「缺失气血」收费，
##            没有这份记录就只能算价钱、治不了。老档（v10 及更早）没有记录 = 全队满血。
## v11 → v12：加入战斗外增益（field_buffs）。08 的 `scope=field` 是**按现实时间**计时的
##            （打坐余韵 10 分钟、饱食 5 分钟），只留在会话里的话「退出重进」就等于白打坐；
##            存的是到期时间戳（unix 秒），所以离线那段时间照样算。老档（v11 及更早）= 没有增益。
## v12 → v13：加入大地图坐标（world_pos）。设计 09 §二：读档一律回大地图，落点用这个坐标；
##            小地图／副本内的位置**故意不存**（那部分进度已各自持久化，存坐标只会让玩家
##            读档后卡在一个已经清空的房间里）。老档（v12 及更早）= 没有记录，用大地图默认出生点。
## v13 → v14：加入创建角色（0.16.0／0.17.0）的两份记录——
##            `talent_picks`（谁选了哪几个天赋）与 `custom_templates`（「不使用模板」那条路
##            玩家自己分的七维／武器／起始武学）。后者必须进存档：模板平时从 `character_base`
##            读，自建角色不在任何 CSV 里，读档时要重新注入（`TableDb.inject_row`）。
##            老档（v13 及更早）= 没有天赋、没有自建角色。
## v14 → v15：加入 NPC 好感度（`npc_favor`：npc_id → 当前好感）。设计 19 的赠礼／切磋／偷窃／兑换
##            都会改它；不进存档的话「送完礼退出重进」等于白送。任务进度不用新字段——它记在旗标里。
const VERSION := 15

## 开局队伍人数上限。
##
## 设计上队伍规模是 3–4 人（docs/design/00_总览.md），0.4.0 起 character_base 只有一个
## 角色模板，实际队伍先按表里的行数来；上限留着等模板扩充。
const MAX_PARTY := 4

const DEFAULT_CHAPTER := "chapter_01"

var version: int = VERSION
var char_ids: PackedStringArray = PackedStringArray()
## char_id → 等级
var char_levels: Dictionary = {}
var difficulty_id: String = "normal"
var chapter_id: String = DEFAULT_CHAPTER
## 所在存档槽（0 表示还没落盘）
var slot: int = 0
var created_unix: int = 0
var saved_unix: int = 0

## 背包与装备（Inventory）
var inventory
## char_id → {attr_id: 点数}
var char_allocations: Dictionary = {}
## char_id → [talent_id]（0.16.0 的天赋；创建时选，之后不改）
var talent_picks: Dictionary = {}
## char_id → 自建角色的模板字段（0.17.0「不使用模板」那条路）。
## 正常角色不在这里——他们读 `character_base`。读档时由 `apply_custom_templates()` 注入回 db。
var custom_templates: Dictionary = {}
## npc_id → 当前好感（设计 19；初始值在 npc_favor.csv 的 initial_favor）
var npc_favor: Dictionary = {}
## 队伍共享经验池：经验分配与升级规则未定，先记账不分人
var party_exp: int = 0
## char_id → {skill_id: 熟练度等级}（0~mastery_max）
var skill_mastery: Dictionary = {}
## char_id → [skill_id]（已学会的武学，图鉴按去重计数）
var char_learned: Dictionary = {}
## char_id → {"active": [skill_id], "passive": [skill_id]}（当前装配）
var char_loadouts: Dictionary = {}
## building_id → {"purchased": {item_id: 累计买入次数}, "buyback": [{item_id, qty, price}]}
var shop_state: Dictionary = {}
## scene_id → {"chests": [...], "triggers": [...], "rooms": [...], "rooms_entered": [...], "bosses": [...]}
## 副本完成度是**永久记录**（会话里那份会随「回大地图整片刷新」清掉，这里不清）
var dungeon_records: Dictionary = {}
## 抽卡保底计数（PityTracker.to_dict()），跨难度继承
var pity: Dictionary = {}
## 已经揭开（点亮）的大地图节点 id
var revealed_nodes: PackedStringArray = PackedStringArray()
## check_id → "done" / "failed"（事件判定结果，once_only 的点位靠它判重）
var event_checks: Dictionary = {}
## 剧情旗标（事件奖励里 event_id 这类，跨场景读）
var flags: Dictionary = {}
## drop_group → true：已经拿过首杀掉落奖励的掉落组（永久记录，跨难度/周目继承）
var first_kills: Dictionary = {}
## char_id → 当前气血（战斗外气血，v11 起进存档）。**没有记录 = 满血**，
## 这样新档与老档都不用特意填满值；治疗就是 clear() 掉记录。
var char_hp: Dictionary = {}
## 战斗外增益（v12 起进存档）：每条 {buff_id, source_id, stacks, expires_at}，
## `expires_at` 是 unix 秒——按现实分钟计时，所以离线那段时间照样算。
## 会话层（`GameSession.add_field_buff/active_field_buffs/clear_field_buffs`）是唯一读写口。
var field_buffs: Array = []
## 大地图坐标（v13 起进存档）：{"x": float, "y": float}。**空字典 = 没有记录**
## （新档或 v12 及更早的老档），读档时用大地图默认出生点。
## 会话层（`GameSession.set_world_position`）是唯一写入口，别在这里直接赋值。
var world_pos: Dictionary = {}
## 运行时字段：从旧版本迁移过来时记下原版本号（0 表示本来就是当前版本），不写进存档
var migrated_from: int = 0
## 运行时字段：读档时剔掉了几个「表里已经下架」的角色（0 表示没有），不写进存档
var pruned_characters: int = 0
## 运行时字段：读档时按当前 `equip_slot_def` 退回了多少件「穿不上」的装备（0 表示没有），不写进存档
var reclaimed_equipped: int = 0


## 新开一局：队伍取 `recruit_def` 里 `is_initial=1` 的角色（0.10.0 起开局只有书生一人），
## 等级取各自 `character_base.start_level`。
static func new_game(
	db,
	difficulty_id: String = "normal",
	char_ids: PackedStringArray = PackedStringArray(),
	slot: int = 0
) -> GameState:
	var state := GameState.new()
	state.difficulty_id = difficulty_id
	state.slot = slot
	var now := int(Time.get_unix_time_from_system())
	state.created_unix = now
	state.saved_unix = now
	var ids := char_ids
	if ids.is_empty():
		ids = default_party_ids_from_recruit(db)
	for char_id: String in ids:
		var row: Resource = db.get_row("character_base", char_id)
		if row == null:
			push_error("[GameState] character_base 缺少角色 %s" % char_id)
			continue
		state.char_ids.append(char_id)
		state.char_levels[char_id] = maxi(1, int(row.start_level))
	state.inventory = InventoryScript.new()
	state.seed_starting_equipment(db)
	state.seed_starting_skills(db)
	return state


## 按角色模板的 start_equip_ids 建出实例并穿上（新建游戏与 v1 老档迁移共用）。
func seed_starting_equipment(db) -> void:
	if inventory == null:
		inventory = InventoryScript.new()
	for char_id: String in char_ids:
		seed_character_equipment(db, char_id)


## 只给**一位**角色发起始装备并穿上（`seed_starting_equipment` 与剧情招募共用一处实现）。
## 招募时必须走这个单人口：多人口会把已经在队里的角色**再发一遍**装备。
func seed_character_equipment(db, char_id: String) -> void:
	if inventory == null:
		inventory = InventoryScript.new()
	var row: Resource = db.get_row("character_base", char_id)
	if row == null:
		return
	for equip_id: String in row.equip_ids():
		var instance_id: String = inventory.add_equipment(db, equip_id)
		if instance_id.is_empty():
			continue
		var equipped: Dictionary = inventory.equip(
			db, char_id, level_of(char_id), str(row.weapon_type), instance_id
		)
		if not equipped["ok"]:
			push_error("[GameState] 初始装备穿戴失败：%s" % equipped["error"])


# ------------------------------------------------------------------ 加点

func allocations_of(char_id: String) -> Dictionary:
	return Dictionary(char_allocations.get(char_id, {}))


# ------------------------------------------------------------------ 武学熟练度

## 按角色模板的起始武学铺「已学 + 装配」（新建游戏与老档迁移共用）。
##
## 注意：只认模板 `start_skill_ids`，不把 `skill_base` 里所有 `source_type=start` 的行都发出去——
## 设计侧那张角色卡写死了书生的「起始武学＝玄微剑法·起手」（玄微心法·引气与连环腿虽然标着
## 开局自带／可选，但没进模板），这条差异记在对接表的 skill_system 备注里等设计确认。
func seed_starting_skills(db) -> void:
	for char_id: String in char_ids:
		seed_character_skills(db, char_id)


## 只给**一位**角色发「起始武学 + 装配」（与上面同理：招募走单人口，别重发）。
func seed_character_skills(db, char_id: String) -> void:
	var row: Resource = db.get_row("character_base", char_id)
	if row == null:
		return
	for skill_id: String in row.skill_ids():
		if db.get_row("skill_base", skill_id) == null:
			push_error("[GameState] 起始武学不存在：%s" % skill_id)
			continue
		learn_skill(char_id, skill_id)
	SkillLoadoutScript.new(db, self, char_id).fill_from_learned()


## 剧情招募（设计 09 §3.2）：把**一位**角色加进队伍。
##
## 只处理这一位：等级取模板 `start_level`，按模板发起始装备与起始武学并铺好装配
## （09 的「待确认」里定了「入队即送装备」；要改成不送，把模板的 `start_equip_ids` 清空即可）。
## 已在队里直接返回 `{ok: false}`，**不重复发装备**——重复调用（每次按 E 都会查一遍）是常态。
func add_character(db, char_id: String) -> Dictionary:
	if char_ids.has(char_id):
		return {"ok": false, "error": "已经在队伍里", "char_id": char_id}
	var row: Resource = db.get_row("character_base", char_id)
	if row == null:
		push_error("[GameState] 招募失败：character_base 里没有角色 %s" % char_id)
		return {"ok": false, "error": "这个角色不在配置里（数据错，已记进日志）", "char_id": char_id}
	char_ids.append(char_id)
	char_levels[char_id] = maxi(1, int(row.start_level))
	if inventory == null:
		inventory = InventoryScript.new()
	seed_character_equipment(db, char_id)
	seed_character_skills(db, char_id)
	return {"ok": true, "error": "", "char_id": char_id}


## 读档时剔掉 `character_base` 里已经下架的角色。
##
## 角色模板改过几轮（早期是 ch_gang／ch_du／ch_ci／ch_qi 四个），老档里留着这些 id
## 只会让队伍变空——**空队伍会让每场战斗瞬间判负**，玩家还看不出为什么。
## 等级取被剔角色的最高值转给接手的新角色，身上的装备退回背包（实例本身不删）。
func _prune_unknown_characters(db) -> void:
	if db == null or char_ids.is_empty():
		return
	var kept := PackedStringArray()
	var dropped := PackedStringArray()
	var best_level := 0
	for char_id: String in char_ids:
		best_level = maxi(best_level, int(char_levels.get(char_id, 1)))
		if db.get_row("character_base", char_id) != null:
			kept.append(char_id)
		else:
			dropped.append(char_id)
	if dropped.is_empty():
		return
	for char_id: String in dropped:
		_strip_character(char_id)
	char_ids = kept
	pruned_characters = dropped.size()
	if char_ids.is_empty():
		var rows: Array = db.rows("character_base")
		if rows.is_empty():
			return
		var row: Resource = rows[0]
		var new_id := str(row.char_id)
		char_ids.append(new_id)
		char_levels[new_id] = maxi(1, maxi(best_level, int(row.start_level)))
		seed_starting_skills(db)
		if inventory != null:
			seed_starting_equipment(db)


## 把一个角色从所有按 char_id 索引的记录里摘掉；他身上穿的东西**退回背包**（实例不删）
func _strip_character(char_id: String) -> void:
	if inventory != null:
		inventory.equipped.erase(char_id)
	char_levels.erase(char_id)
	char_allocations.erase(char_id)
	skill_mastery.erase(char_id)
	char_learned.erase(char_id)
	char_loadouts.erase(char_id)
	char_hp.erase(char_id)


## 熟练度表里出现过的武学也算已学（v3 老档的熟练度是唯一的学习记录）
func learn_mastered_skills() -> void:
	for char_id: String in skill_mastery:
		for skill_id: String in masteries_of(str(char_id)):
			learn_skill(str(char_id), skill_id)


func learned_of(char_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	for skill_id: Variant in char_learned.get(char_id, []):
		out.append(str(skill_id))
	out.sort()
	return out


func is_learned(char_id: String, skill_id: String) -> bool:
	return Array(char_learned.get(char_id, [])).has(skill_id)


## 学会一部武学；返回是否新学（重复学不算失败，掉落会反复遇到同一个来源）
func learn_skill(char_id: String, skill_id: String) -> bool:
	if skill_id.is_empty() or is_learned(char_id, skill_id):
		return false
	var learned: Array = char_learned.get(char_id, [])
	learned.append(skill_id)
	char_learned[char_id] = learned
	return true


## 图鉴收集数：全队已学武学去重后的部数（图鉴奖励按它算，见 GrowthCalculator.codex_bonus）
func collected_skill_count() -> int:
	var seen := {}
	for char_id: String in char_learned:
		for skill_id: Variant in char_learned[char_id]:
			seen[str(skill_id)] = true
	return seen.size()


func loadout_of(char_id: String) -> Dictionary:
	var entry: Dictionary = char_loadouts.get(char_id, {})
	return {
		"active": PackedStringArray(entry.get("active", PackedStringArray())),
		"passive": PackedStringArray(entry.get("passive", PackedStringArray())),
	}


func set_loadout(char_id: String, active: PackedStringArray, passive: PackedStringArray) -> void:
	char_loadouts[char_id] = {"active": Array(active), "passive": Array(passive)}


func masteries_of(char_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	var entries: Dictionary = skill_mastery.get(char_id, {})
	for skill_id: String in entries:
		out.append(skill_id)
	out.sort()
	return out


func mastery_of(char_id: String, skill_id: String) -> int:
	var entries: Dictionary = skill_mastery.get(char_id, {})
	return int(entries.get(skill_id, 0))


## 记下熟练度（成长来源：战斗施放／打坐／秘籍，本轮只提供写入接口）
func set_mastery(char_id: String, skill_id: String, level: int, max_level: int = 10) -> int:
	var entries: Dictionary = skill_mastery.get(char_id, {})
	var clamped := clampi(level, 0, maxi(0, max_level))
	entries[skill_id] = clamped
	skill_mastery[char_id] = entries
	return clamped


## 未分配点数 = 1 级到当前等级的 upgrade_points 累计 − 已加的点数。
func available_points(db, char_id: String) -> int:
	var total := 0
	for level in range(1, maxi(1, level_of(char_id)) + 1):
		var row: Resource = db.get_row("level_growth", level)
		if row != null:
			total += int(row.upgrade_points)
	var spent := 0
	for attr_id: String in allocations_of(char_id):
		spent += int(allocations_of(char_id)[attr_id])
	return maxi(0, total - spent)


## 这个属性能不能加点；能加返回空串，不能加返回给玩家看的原因。
##
## 设计 0.13.0：七维分成**五维（力体敏智运，可加点）与资质（悟性／根骨，创建时定死）**。
## 资质只能靠图鉴奖励与特定内功提升——否则「先堆资质再堆输出」永远是唯一解。
## 这里是唯一出处：加点入口与角色面板的加号按钮都问它。
func allocation_block_reason(db, attr_id: String) -> String:
	var row: Resource = db.get_row("attribute_def", attr_id)
	if row == null:
		return "没有这个属性：%s" % attr_id
	if int(row.allocatable) != 1:
		return "%s 是不能加点的资质（只能靠图鉴奖励与内功提升）" % str(row.name_cn)
	return ""


## 加一点。返回 {ok, error, remaining}
func spend_point(db, char_id: String, attr_id: String) -> Dictionary:
	var blocked := allocation_block_reason(db, attr_id)
	if not blocked.is_empty():
		return {"ok": false, "error": blocked, "remaining": available_points(db, char_id)}
	var remaining := available_points(db, char_id)
	if remaining <= 0:
		return {"ok": false, "error": "没有可分配的点数", "remaining": 0}
	var allocations := allocations_of(char_id)
	allocations[attr_id] = int(allocations.get(attr_id, 0)) + 1
	char_allocations[char_id] = allocations
	return {"ok": true, "error": "", "remaining": remaining - 1}


## character_base 里可作为开局队伍的角色（按表的顺序，最多 MAX_PARTY 人）。
## 0.10.0 起开局队伍改由 `recruit_def.is_initial` 决定（见 `default_party_ids_from_recruit`），
## 这个方法保留给「按表顺序取前 N 行」的老用例与夹具。
static func default_party_ids(db) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in db.rows("character_base"):
		if out.size() >= MAX_PARTY:
			break
		out.append(str(row.char_id))
	return out


## 开局队伍：`recruit_def` 里 `is_initial=1` 的角色（设计 09 §3.2——开局只有书生一人，
## 同伴按 `join_condition`／`join_scene` 在剧情里陆续加入，不再取 `character_base` 的前 4 行）。
## recruit_def 不可用时退回「取前 MAX_PARTY 行」，免得配表出问题就开不了局。
static func default_party_ids_from_recruit(db) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in db.rows("recruit_def"):
		if int(row.is_initial) != 1:
			continue
		var char_id := str(row.char_id)
		if db.get_row("character_base", char_id) == null:
			push_error("[GameState] recruit_def 的初始成员不在 character_base 里：%s" % char_id)
			continue
		out.append(char_id)
	if out.is_empty():
		push_warning("[GameState] recruit_def 里没有 is_initial=1 的成员，退回按 character_base 顺序取人")
		return default_party_ids(db)
	return out


## 大地图坐标（v13）：会话层写进来的唯一入口。
func set_world_pos(pos: Vector2) -> void:
	world_pos = {"x": pos.x, "y": pos.y}


## 存档里的大地图坐标；没有记录时返回 `Vector2.ZERO`（大地图把 ZERO 当成「用默认出生点」）。
func world_position() -> Vector2:
	return Vector2(float(world_pos.get("x", 0.0)), float(world_pos.get("y", 0.0)))


func party_size() -> int:
	return char_ids.size()


func level_of(char_id: String) -> int:
	return int(char_levels.get(char_id, 1))


## 升一级（经验换算在 LevelService 里；这里只管等级本身，脚本与用例也直接调它）
func append_level(char_id: String, amount: int = 1) -> int:
	var level := maxi(1, level_of(char_id) + maxi(1, amount))
	char_levels[char_id] = level
	return level


func char_name(db, char_id: String) -> String:
	var row: Resource = db.get_row("character_base", char_id)
	if row != null:
		return str(row.name_cn)
	# 存档里还留着**已经下架的角色**（设计从 character_base 里删过角色，读档时会剔掉）：
	# 界面说人话（别再把这个 char_id 摆给玩家），日志留一条警告便于查。
	push_warning("[GameState] 存档里的角色已经不在 character_base 里：%s（读档时会剔掉）" % char_id)
	return "已下架的角色"


func difficulty_name(db) -> String:
	var row: Resource = db.get_row("difficulty_config", difficulty_id)
	if row != null:
		return str(row.name_cn)
	push_warning("[GameState] 存档里的难度不在 difficulty_config 里：%s" % difficulty_id)
	return "未知难度"


## 队伍名，取前两名角色，例如「林铁山、苏九娘」。
func party_label(db) -> String:
	var names := PackedStringArray()
	for char_id: String in char_ids:
		names.append("%s Lv%d" % [char_name(db, char_id), level_of(char_id)])
	return "、".join(names) if not names.is_empty() else "（空队伍）"


## 存档列表里显示的一行摘要。
func short_label(db) -> String:
	return "%s｜%s｜%s" % [party_label(db), difficulty_name(db), format_time(saved_unix)]


## 进入游戏后的详细摘要。
func summary_lines(db) -> PackedStringArray:
	return PackedStringArray([
		"章节：%s" % chapter_label(db),
		"难度：%s" % difficulty_name(db),
		"队伍：%s" % party_label(db),
		"存档槽：%s" % ("未落盘" if slot <= 0 else "第 %d 格" % slot),
		"开局时间：%s" % format_time(created_unix),
		"最后保存：%s" % format_time(saved_unix),
	])


## 章节显示名：`chapter_01` → 「第 1 章」。
##
## **不给玩家看 id**：这里原来写的是「第 1 章（chapter_01）」，枢纽页那行直接就把
## `chapter_01` 甩给了玩家（2026-10-03 实机截图里能看到）。
##
## 0.22.0 起**章节有了真正的表**（`chapter_def`）：显示名直接取 `name_cn`
## （「第一章·黑风寨」这种），不再从 id 里猜编号。db 没传或表里没有时退回旧口径。
func chapter_label(db = null) -> String:
	if db != null:
		var name_cn := ChapterServiceScript.label_of(db, self)
		if not name_cn.is_empty():
			return name_cn
	var suffix := chapter_id.trim_prefix("chapter_")
	if suffix.is_valid_int():
		return "第 %d 章" % int(suffix)
	return "第 1 章"


## unix 秒 → 本地时间字符串（yyyy-MM-dd HH:mm）。
static func format_time(unix: int) -> String:
	if unix <= 0:
		return "—"
	var bias := int(Time.get_time_zone_from_system().get("bias", 0))
	var parts := Time.get_datetime_dict_from_unix_time(unix + bias * 60)
	return "%04d-%02d-%02d %02d:%02d" % [
		int(parts.get("year", 0)), int(parts.get("month", 0)), int(parts.get("day", 0)),
		int(parts.get("hour", 0)), int(parts.get("minute", 0)),
	]


func touch_saved() -> void:
	saved_unix = int(Time.get_unix_time_from_system())


func to_dict() -> Dictionary:
	return {
		"version": version,
		"char_ids": Array(char_ids),
		"char_levels": char_levels.duplicate(),
		"difficulty_id": difficulty_id,
		"chapter_id": chapter_id,
		"slot": slot,
		"created_unix": created_unix,
		"saved_unix": saved_unix,
		"inventory": (inventory if inventory != null else InventoryScript.new()).to_dict(),
		"char_allocations": char_allocations.duplicate(true),
		"talent_picks": talent_picks.duplicate(true),
		"custom_templates": custom_templates.duplicate(true),
		"npc_favor": npc_favor.duplicate(),
		"party_exp": party_exp,
		"skill_mastery": skill_mastery.duplicate(true),
		"char_learned": char_learned.duplicate(true),
		"char_loadouts": char_loadouts.duplicate(true),
		"shop_state": shop_state.duplicate(true),
		"dungeon_records": dungeon_records.duplicate(true),
		"pity": pity.duplicate(true),
		"revealed_nodes": Array(revealed_nodes),
		"event_checks": event_checks.duplicate(true),
		"flags": flags.duplicate(true),
		"first_kills": first_kills.duplicate(true),
		"char_hp": char_hp.duplicate(true),
		"field_buffs": field_buffs.duplicate(true),
		"world_pos": world_pos.duplicate(true),
	}


## 结构是否像一份存档（存档文件损坏时用来判定）。
static func is_valid_dict(data: Variant) -> bool:
	if not (data is Dictionary):
		return false
	var dictionary: Dictionary = data
	if not (dictionary.get("char_ids") is Array):
		return false
	if Array(dictionary["char_ids"]).is_empty():
		return false
	if not (dictionary.get("char_levels") is Dictionary):
		return false
	if not (dictionary.get("difficulty_id") is String):
		return false
	return true


func _read_dungeon_records(data: Dictionary) -> void:
	if data.get("dungeon_records") is Dictionary:
		for scene_id: Variant in Dictionary(data["dungeon_records"]):
			var entry: Variant = data["dungeon_records"][scene_id]
			if not (entry is Dictionary):
				continue
			for kind: String in ["chests", "triggers", "rooms", "rooms_entered", "bosses"]:
				var list: Variant = Dictionary(entry).get(kind, [])
				if not (list is Array):
					continue
				for key: Variant in Array(list):
					record_dungeon(str(scene_id), kind, str(key))
	if data.get("pity") is Dictionary:
		pity = Dictionary(data["pity"]).duplicate(true)


func _read_shop_state(data: Dictionary) -> void:
	if not (data.get("shop_state") is Dictionary):
		return
	for building_id: Variant in Dictionary(data["shop_state"]):
		var entry: Variant = data["shop_state"][building_id]
		if not (entry is Dictionary):
			continue
		var key := str(building_id)
		var purchased: Variant = Dictionary(entry).get("purchased", {})
		if purchased is Dictionary:
			for item_id: Variant in Dictionary(purchased):
				record_purchase(key, str(item_id), int(Dictionary(purchased)[item_id]))
		var buyback: Variant = Dictionary(entry).get("buyback", [])
		if buyback is Array:
			for record: Variant in Array(buyback):
				if record is Dictionary:
					# 读档不设上限：上限是给「继续卖东西」用的，历史条目按原样保留
					push_buyback(key, Dictionary(record), 0)


## 老档（v1~v3）补装配：只补空着的，不覆盖新档里玩家自己卸空的槽
func _fill_empty_loadouts(db) -> void:
	for char_id: String in char_ids:
		var current: Dictionary = loadout_of(char_id)
		if not current["active"].is_empty() or not current["passive"].is_empty():
			continue
		if learned_of(char_id).is_empty():
			continue
		SkillLoadoutScript.new(db, self, char_id).fill_from_learned()


func _read_learned(data: Dictionary) -> void:
	if not (data.get("char_learned") is Dictionary):
		return
	for char_id: Variant in Dictionary(data["char_learned"]):
		var entries: Variant = data["char_learned"][char_id]
		if not (entries is Array):
			continue
		var learned: Array = char_learned.get(str(char_id), [])
		for skill_id: Variant in Array(entries):
			var id := str(skill_id)
			if not id.is_empty() and not learned.has(id):
				learned.append(id)
		char_learned[str(char_id)] = learned


func _read_loadouts(data: Dictionary) -> void:
	if not (data.get("char_loadouts") is Dictionary):
		return
	for char_id: Variant in Dictionary(data["char_loadouts"]):
		var entry: Variant = data["char_loadouts"][char_id]
		if not (entry is Dictionary):
			continue
		set_loadout(
			str(char_id),
			PackedStringArray(Dictionary(entry).get("active", PackedStringArray())),
			PackedStringArray(Dictionary(entry).get("passive", PackedStringArray()))
		)


## 商店交易状态（05_装备与掉落.md「商品与存档」）：累计买入次数与回购列表。
## 读的时候返回一份规整过的副本，不顺手往存档里塞空条目。
func shop_state_of(building_id: String) -> Dictionary:
	var entry: Dictionary = shop_state.get(building_id, {})
	return {
		"purchased": Dictionary(entry.get("purchased", {})).duplicate(),
		"buyback": Array(entry.get("buyback", [])).duplicate(true),
	}


func purchased_count(building_id: String, item_id: String) -> int:
	var entry: Dictionary = shop_state.get(building_id, {})
	var purchased: Variant = entry.get("purchased", {})
	if not (purchased is Dictionary):
		return 0
	return int(Dictionary(purchased).get(item_id, 0))


## 记一次买入，返回累计次数（限量商品的剩余量 = stock_limit − 这个数）
func record_purchase(building_id: String, item_id: String, qty: int) -> int:
	var entry := _ensure_shop_entry(building_id)
	var purchased: Dictionary = entry["purchased"]
	purchased[item_id] = int(purchased.get(item_id, 0)) + maxi(0, qty)
	entry["purchased"] = purchased
	shop_state[building_id] = entry
	return int(purchased[item_id])


func buyback_of(building_id: String) -> Array:
	var entry: Dictionary = shop_state.get(building_id, {})
	return Array(entry.get("buyback", [])).duplicate(true)


## 卖出的东西进回购列表；超过上限挤掉最早的
func push_buyback(building_id: String, record: Dictionary, limit: int = 20) -> void:
	var entry := _ensure_shop_entry(building_id)
	var list: Array = entry["buyback"]
	list.append(record.duplicate(true))
	while limit > 0 and list.size() > limit:
		list.remove_at(0)
	entry["buyback"] = list
	shop_state[building_id] = entry


func pop_buyback(building_id: String, index: int) -> void:
	var entry := _ensure_shop_entry(building_id)
	var list: Array = entry["buyback"]
	if index >= 0 and index < list.size():
		list.remove_at(index)
	entry["buyback"] = list
	shop_state[building_id] = entry


## 回购条目只买走一部分（背包放不下时）：把这条的数量改成剩下的；qty <= 0 就整条出列。
func set_buyback_qty(building_id: String, index: int, qty: int) -> void:
	var entry := _ensure_shop_entry(building_id)
	var list: Array = entry["buyback"]
	if index >= 0 and index < list.size():
		if qty <= 0:
			list.remove_at(index)
		else:
			var record: Dictionary = list[index]
			record["qty"] = qty
			list[index] = record
	entry["buyback"] = list
	shop_state[building_id] = entry


func _ensure_shop_entry(building_id: String) -> Dictionary:
	var entry: Dictionary = shop_state.get(building_id, {})
	if not (entry.get("purchased") is Dictionary):
		entry["purchased"] = {}
	if not (entry.get("buyback") is Array):
		entry["buyback"] = []
	return entry


## 事件判定：记结果（done / failed）并留旗标
func record_event_check(check_id: String, result: String) -> void:
	if check_id.is_empty():
		return
	event_checks[check_id] = result


func event_check_result(check_id: String) -> String:
	return str(event_checks.get(check_id, ""))


func set_flag(flag_id: String, value: bool = true) -> void:
	if flag_id.is_empty():
		return
	flags[flag_id] = value


func has_flag(flag_id: String) -> bool:
	return bool(flags.get(flag_id, false))


## 首杀：某个掉落组第一次被打死时记一次，之后同一组不再出首杀限定掉落。
## 返回 true 表示「这次算首杀」——战斗结算拿它去问 DropResolver。
## 记录进存档（v10 起），否则退出重进就能反复领首杀固定掉落（刷子游戏的经典漏洞）。
func mark_first_kill(drop_group: String) -> bool:
	if drop_group.is_empty():
		return false
	if first_kills.has(drop_group):
		return false
	first_kills[drop_group] = true
	return true


func has_first_kill(drop_group: String) -> bool:
	return not drop_group.is_empty() and first_kills.has(drop_group)


func first_kill_count() -> int:
	return first_kills.size()


# ------------------------------------------------------------------ 战斗外气血（v11）

## 当前气血；**没有记录返回 -1**（调用方按满血处理）。
## 不返回 0 是因为 0 在游戏里表示「倒下」，而「没记录」表示「没受过伤」。
func current_hp_of(char_id: String) -> int:
	if char_id.is_empty() or not char_hp.has(char_id):
		return -1
	return int(char_hp[char_id])


## 记一次战斗后的剩余气血。**下限 1**：倒下的角色回图时留 1 点，
## 免得「全队 0 血」把存档卡死（设计没定败北代价，这条口径记在交接表等确认）。
func set_current_hp(char_id: String, hp: int) -> void:
	if char_id.is_empty():
		return
	char_hp[char_id] = maxi(1, hp)


## 满血：直接把记录清掉（没有记录就是满血）
func heal_all() -> void:
	char_hp.clear()


func is_wounded(char_id: String) -> bool:
	return char_hp.has(char_id)


## 大地图节点揭开（02_地图与明雷.md：玩家的地图是自己走出来的）
func reveal_node(node_id: String) -> bool:
	if node_id.is_empty() or revealed_nodes.has(node_id):
		return false
	revealed_nodes.append(node_id)
	return true


func is_node_revealed(node_id: String) -> bool:
	return revealed_nodes.has(node_id)


## 副本永久记录（完成度用）。kind ∈ chests / triggers / rooms / rooms_entered / bosses
func record_dungeon(scene_id: String, kind: String, key: String) -> bool:
	if scene_id.is_empty() or key.is_empty():
		return false
	var entry: Dictionary = dungeon_records.get(scene_id, {})
	var list: Array = entry.get(kind, [])
	if list.has(key):
		return false
	list.append(key)
	entry[kind] = list
	dungeon_records[scene_id] = entry
	return true


func dungeon_record(scene_id: String) -> Dictionary:
	var entry: Dictionary = dungeon_records.get(scene_id, {})
	return {
		"chests": Array(entry.get("chests", [])).duplicate(),
		"triggers": Array(entry.get("triggers", [])).duplicate(),
		"rooms": Array(entry.get("rooms", [])).duplicate(),
		"rooms_entered": Array(entry.get("rooms_entered", [])).duplicate(),
		"bosses": Array(entry.get("bosses", [])).duplicate(),
	}


## 保底计数器：从存档读出来用，打完再存回去（保底要跨战斗、跨难度继承）
func pity_tracker():
	var tracker = PityTrackerScript.new()
	tracker.from_dict(pity)
	return tracker


func store_pity(tracker) -> void:
	if tracker == null:
		return
	pity = tracker.to_dict()


## 读一份存档。db 用于旧版本迁移（补初始装备）；不传 db 时背包会是空的。
static func from_dict(data: Dictionary, db = null) -> GameState:
	if not is_valid_dict(data):
		return null
	var state := GameState.new()
	state.version = int(data.get("version", VERSION))
	var source_version := state.version
	state.char_ids = PackedStringArray(data["char_ids"])
	state.char_levels = {}
	for key: Variant in Dictionary(data["char_levels"]):
		state.char_levels[str(key)] = int(data["char_levels"][key])
	state.difficulty_id = str(data["difficulty_id"])
	state.chapter_id = str(data.get("chapter_id", DEFAULT_CHAPTER))
	state.slot = int(data.get("slot", 0))
	state.created_unix = int(data.get("created_unix", 0))
	state.saved_unix = int(data.get("saved_unix", 0))
	if source_version >= 2:
		state.inventory = InventoryScript.from_dict(data.get("inventory", {}))
		# 配表改过槽位（删槽位／max_equip 改小）时，把存档里装不下的穿戴记录退回背包：
		# 不这么做它们会「界面看不见、却照常加属性」（见 inventory.reclaim_stranded_equipped）。
		if db != null:
			var reclaimed_total := 0
			for key: Variant in state.inventory.equipped.keys():
				var reclaimed: PackedStringArray = state.inventory.reclaim_stranded_equipped(db, str(key))
				if not reclaimed.is_empty():
					reclaimed_total += reclaimed.size()
					push_warning("[GameState] 配表装不下的穿戴记录已退回背包（%s）：%s" % [str(key), ", ".join(reclaimed)])
			state.reclaimed_equipped = reclaimed_total
		if data.get("char_allocations") is Dictionary:
			for char_id: Variant in Dictionary(data["char_allocations"]):
				var allocation: Variant = data["char_allocations"][char_id]
				if allocation is Dictionary:
					var normalized: Dictionary = {}
					for attr_id: Variant in Dictionary(allocation):
						normalized[str(attr_id)] = int(Dictionary(allocation)[attr_id])
					state.char_allocations[str(char_id)] = normalized
		state.party_exp = int(data.get("party_exp", 0))
	if source_version >= 3:
		if data.get("skill_mastery") is Dictionary:
			for char_id: Variant in Dictionary(data["skill_mastery"]):
				var entries: Variant = data["skill_mastery"][char_id]
				if not (entries is Dictionary):
					continue
				var normalized: Dictionary = {}
				for skill_id: Variant in Dictionary(entries):
					normalized[str(skill_id)] = int(Dictionary(entries)[skill_id])
				state.skill_mastery[str(char_id)] = normalized
	if source_version >= 4:
		state._read_learned(data)
		state._read_loadouts(data)
	if source_version >= 6:
		state._read_shop_state(data)
	if source_version >= 7:
		state._read_dungeon_records(data)
	if source_version >= 8:
		if data.get("revealed_nodes") is Array:
			for node_id: Variant in Array(data["revealed_nodes"]):
				state.reveal_node(str(node_id))
	if source_version >= 9:
		if data.get("event_checks") is Dictionary:
			for check_id: Variant in Dictionary(data["event_checks"]):
				state.record_event_check(str(check_id), str(Dictionary(data["event_checks"])[check_id]))
		if data.get("flags") is Dictionary:
			for flag_id: Variant in Dictionary(data["flags"]):
				state.set_flag(str(flag_id), bool(Dictionary(data["flags"])[flag_id]))
	if source_version >= 10:
		state._read_first_kills(data)
	if source_version >= 11:
		state._read_char_hp(data)
	if source_version >= 12:
		state._read_field_buffs(data)
	if source_version >= 13:
		state._read_world_pos(data)
	if source_version >= 14:
		state._read_talent_picks(data)
		# **必须在 `_prune_unknown_characters()` 之前**：自建角色不在任何 CSV 里，
		# 先注入模板，它才不会被当成「模板下架的角色」清掉。
		state._read_custom_templates(data, db)
	if source_version >= 15:
		state._read_npc_favor(data)
	# 老档补齐：v1 补背包与初始装备，v1~v3 补「已学 + 装配」
	if source_version < 2:
		state.inventory = InventoryScript.new()
		if db != null:
			state.seed_starting_equipment(db)
	if source_version < 4:
		if db != null:
			state.seed_starting_skills(db)
		state.learn_mastered_skills()
		if db != null:
			state._fill_empty_loadouts(db)
	# 模板下架的角色要在进游戏前清掉（否则队伍是空的，每场战斗瞬间判负）
	state._prune_unknown_characters(db)
	if source_version < VERSION:
		state.migrated_from = source_version
	state.version = VERSION
	return state


## 天赋（v14 起）：`char_id → [talent_id]`，只保留表里还存在的天赋。
func _read_talent_picks(data: Dictionary) -> void:
	if not (data.get("talent_picks") is Dictionary):
		return
	for char_id: Variant in Dictionary(data["talent_picks"]):
		var picks: Variant = Dictionary(data["talent_picks"])[char_id]
		if not (picks is Array):
			continue
		var out: Array = []
		for talent_id: Variant in Array(picks):
			out.append(str(talent_id))
		talent_picks[str(char_id)] = out


## 自建角色模板（v14 起）：读进内存并**立刻注入 db**，后面按 char_id 取模板的地方才查得到。
func _read_custom_templates(data: Dictionary, db = null) -> void:
	if not (data.get("custom_templates") is Dictionary):
		return
	for char_id: Variant in Dictionary(data["custom_templates"]):
		var spec: Variant = Dictionary(data["custom_templates"])[char_id]
		if spec is Dictionary:
			custom_templates[str(char_id)] = Dictionary(spec).duplicate(true)
	apply_custom_templates(db)


## NPC 好感（v15 起）：只收表里还认得的 npc_id；值归一成 int。
func _read_npc_favor(data: Dictionary) -> void:
	if not (data.get("npc_favor") is Dictionary):
		return
	for npc_id: Variant in Dictionary(data["npc_favor"]):
		npc_favor[str(npc_id)] = int(Dictionary(data["npc_favor"])[npc_id])


## 把 `custom_templates` 里的每一份自建模板注入 `character_base`（幂等：同 id 覆盖）。
##
## 创建流程（建完那一刻）与读档（`_read_custom_templates`）都调它——
## 注入是内存行为，CSV 与 `data/generated` 都不动。
func apply_custom_templates(db) -> int:
	if db == null or custom_templates.is_empty():
		return 0
	var factory = load("res://src/data/tables/character_base_row.gd")
	var applied := 0
	for char_id: Variant in custom_templates:
		var spec: Dictionary = custom_templates[char_id]
		var row = factory.new()
		row.id = str(char_id)
		row.char_id = str(char_id)
		row.name_cn = str(spec.get("name_cn", char_id))
		row.role_tag = str(spec.get("role_tag", "自建"))
		row.weapon_type = str(spec.get("weapon_type", ""))
		var attrs: Dictionary = spec.get("attrs", {})
		row.initial_str = int(attrs.get("str", 0))
		row.initial_con = int(attrs.get("con", 0))
		row.initial_agi = int(attrs.get("agi", 0))
		row.initial_int = int(attrs.get("int", 0))
		row.initial_luk = int(attrs.get("luk", 0))
		row.initial_wu = int(attrs.get("wu", 0))
		row.initial_gen = int(attrs.get("gen", 0))
		row.attr_total = row.attr_sum()
		row.start_level = maxi(1, int(spec.get("start_level", 1)))
		row.start_skill_ids = str(spec.get("start_skill_ids", ""))
		row.start_equip_ids = str(spec.get("start_equip_ids", ""))
		row.desc = str(spec.get("desc", "自建角色（不使用模板）"))
		db.inject_row("character_base", row)
		applied += 1
	return applied


## 首杀记录（v10 起）。只有 true 有意义，读的时候把任何真值归一成 true。
func _read_first_kills(data: Dictionary) -> void:
	if not (data.get("first_kills") is Dictionary):
		return
	for group_id: Variant in Dictionary(data["first_kills"]):
		if bool(Dictionary(data["first_kills"])[group_id]):
			first_kills[str(group_id)] = true


## 战斗外增益（v12 起）：只收形态合法的条目（有 buff_id 与将来的到期时间），
## 坏数据直接丢掉——比读进一条「永远不过期」的增益安全。过期的条目留着不碍事：
## `GameSession.active_field_buffs()` 一问就会顺手清掉（时钟可注入，用例不必真等）。
func _read_field_buffs(data: Dictionary) -> void:
	if not (data.get("field_buffs") is Array):
		return
	for entry: Variant in Array(data["field_buffs"]):
		if not (entry is Dictionary):
			continue
		var row: Dictionary = entry
		var buff_id := str(row.get("buff_id", ""))
		var expires_at := int(row.get("expires_at", 0))
		if buff_id.is_empty() or expires_at <= 0:
			continue
		field_buffs.append({
			"buff_id": buff_id,
			"source_id": str(row.get("source_id", "")),
			"stacks": maxi(1, int(row.get("stacks", 1))),
			"expires_at": expires_at,
		})


## 战斗外气血（v11 起）。下限 1 与 set_current_hp 同一口径，坏数据不至于把人写成 0。
func _read_char_hp(data: Dictionary) -> void:
	if not (data.get("char_hp") is Dictionary):
		return
	for char_id: Variant in Dictionary(data["char_hp"]):
		var value := int(Dictionary(data["char_hp"])[char_id])
		if value > 0:
			char_hp[str(char_id)] = maxi(1, value)


## 大地图坐标（v13 起）。只收「x、y 都是数」的条目——写坏的档宁可当作没有记录
## （退回默认出生点），也不要读进一个 NaN 把玩家扔到地图外面。
func _read_world_pos(data: Dictionary) -> void:
	var entry: Variant = data.get("world_pos", {})
	if not (entry is Dictionary):
		return
	var pair: Dictionary = entry
	if not (pair.get("x") is float or pair.get("x") is int):
		return
	if not (pair.get("y") is float or pair.get("y") is int):
		return
	var pos := Vector2(float(pair["x"]), float(pair["y"]))
	if is_nan(pos.x) or is_nan(pos.y) or is_inf(pos.x) or is_inf(pos.y):
		return
	world_pos = {"x": pos.x, "y": pos.y}
