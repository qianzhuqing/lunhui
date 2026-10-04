## 存档 v1 → v2 迁移与 v2 往返。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const MenuControllerScript := preload("res://src/core/menu_controller.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")

const TEST_DIR := "res://.logs/test_migration"
const SLOTS := 3

## 每个版本「新增了什么字段」（照 `game_state.gd` 头部那份迁移清单）。
## 构造某一档老档 = 拿当前存档改 `version`，再把**那一档之后才出现的字段**删掉。
## 以后每加一个版本，只要往这张表补一行，`_check_every_legacy_version` 就自动多覆盖一档——
## 不会再出现「升了 v12、却没人试过 v11 老档」这种事。
const ADDED_IN := {
	2: ["inventory", "char_allocations", "party_exp"],
	3: ["skill_mastery"],
	4: ["char_learned", "char_loadouts"],
	5: [],                      # v5 只给装备实例加了 affixes 字段，单独处理
	6: ["shop_state"],
	7: ["dungeon_records", "pity"],
	8: ["revealed_nodes"],
	9: ["event_checks", "flags"],
	10: ["first_kills"],
	11: ["char_hp"],
	12: ["field_buffs"],
	13: ["world_pos"],
	14: ["talent_picks", "custom_templates"],
	15: ["npc_favor"],
}


func suite_name() -> String:
	return "存档迁移 v1 → 当前版本"


func run() -> void:
	var db = get_db()
	# 存档结构版本写死绝对值：升版本必须同时写迁移与 `ADDED_IN`（漏一边就有档读不回来）
	check_eq(GameStateScript.VERSION, 15,
		"存档结构版本是 15（v15 = NPC 好感 npc_favor，设计 19）")
	_clean_dir(TEST_DIR)
	_check_direct_migration(db)
	_check_store_migration(db)
	_check_v2_round_trip(db)
	_check_v3_migration(db)
	_check_v4_migration(db)
	_check_v9_migration(db)
	_check_v10_migration(db)
	_check_v12_field_buffs(db)
	_check_v13_world_pos(db)
	_check_v14_creation(db)
	_check_every_legacy_version(db)
	_check_retired_characters(db)
	_check_corrupt(db)
	_check_type_mismatched_fields(db)


## 一份 0.4.x 时代的存档：只有队伍、等级、难度与时间戳，没有背包。
func _v1_dict() -> Dictionary:
	return {
		"version": 1,
		"char_ids": ["scholar_fallen"],
		"char_levels": {"scholar_fallen": 3},
		"difficulty_id": "hard",
		"chapter_id": "chapter_01",
		"slot": 0,
		"created_unix": 111,
		"saved_unix": 222,
	}


func _check_direct_migration(db) -> void:
	var state = GameStateScript.from_dict(_v1_dict(), db)
	check_not_null(state, "v1 存档能读进来")
	if state == null:
		return
	check_eq(state.version, GameStateScript.VERSION, "读入后版本升到当前版本")
	check_eq(state.migrated_from, 1, "记下迁移来源")
	check_eq(state.level_of("scholar_fallen"), 3, "等级保留")
	check_eq(state.difficulty_id, "hard", "难度保留")
	check_eq(state.created_unix, 111, "创建时间保留")
	check_eq(state.saved_unix, 222, "保存时间保留")
	check_eq(state.inventory.equipment_count(), 2, "自动补发模板的初始装备（0.10.2 起两件）")
	check_eq(state.inventory.equipped_instances(db, "scholar_fallen").size(), 2, "补发的两件都穿上了")
	check_eq(state.inventory.money, 0, "新迁移的存档铜钱为 0")
	check_eq(state.available_points(db, "scholar_fallen"), 15, "3 级累计 15 点可分配（0.13.0 每级 5 点）")

	# 没有 db 时仍能升级版本，只是补不了装备
	var bare = GameStateScript.from_dict(_v1_dict())
	check_eq(bare.version, GameStateScript.VERSION, "没有 db 也能升版本")
	check_eq(bare.inventory.equipment_count(), 0, "没有 db 时不补装备")


func _check_store_migration(db) -> void:
	var store = SaveStoreScript.new(TEST_DIR, SLOTS)
	store.ensure_dir()
	var file := FileAccess.open(store.slot_path(1), FileAccess.WRITE)
	file.store_string(JSON.stringify(_v1_dict()))
	file.close()

	var loaded: Dictionary = store.load_slot(1, db)
	check_true(loaded["ok"], "v1 存档能读出来")
	check_eq(loaded["state"].migrated_from, 1, "状态标记需要迁移")

	# 走菜单的读档流程：迁移后立刻写回
	var controller = MenuControllerScript.new(db, store)
	var result: Dictionary = controller.load_game(1)
	check_true(result["ok"], "菜单读档成功")
	check_true(str(result["message"]).contains("升级"), "提示已升级存档：%s" % result["message"])

	var raw: Variant = JSON.parse_string(FileAccess.get_file_as_string(store.slot_path(1)))
	check_true(GameStateScript.is_valid_dict(raw), "写回后仍是合法存档")
	check_eq(int(Dictionary(raw).get("version", 0)), GameStateScript.VERSION, "文件里的版本变成当前版本")

	var again: Dictionary = store.load_slot(1, db)
	check_eq(again["state"].migrated_from, 0, "第二次读取不需要迁移")
	check_eq(again["state"].inventory.equipment_count(), 2, "写回后的存档带着装备（两件）")
	check_eq(again["state"].level_of("scholar_fallen"), 3, "写回后等级没丢")


func _check_v2_round_trip(db) -> void:
	# 这组用例验的是**迁移机制**（v2 老档里的钱／材料／装备／加点能不能原样回来），
	# 与"表里有几个模板"无关；而装备件数会随模板的起始装备变化（1 个模板 = 1 件）。
	# 所以把 roster 钉成**单模板夹具**，让这条用例专测机制（框架说明决策 161）。
	var roster = table_with_n_chars(1)
	var state = GameStateScript.new_game(roster, "normal")
	var char_id := str(state.char_ids[0])
	state.inventory.money = 123
	state.inventory.add_item(roster, "item_herb", 4)
	state.spend_point(roster, char_id, "con")

	var store = SaveStoreScript.new(TEST_DIR, SLOTS)
	var saved: Dictionary = store.save_slot(2, state)
	check_true(saved["ok"], "v2 存档写入成功")
	var loaded: Dictionary = store.load_slot(2, roster)
	check_true(loaded["ok"], "v2 存档读出成功")
	var back = loaded["state"]
	check_eq(back.migrated_from, 0, "v2 不需要迁移")
	check_eq(back.inventory.money, 123, "铜钱往返一致")
	check_eq(back.inventory.count("item_herb"), 4, "材料往返一致")
	check_eq(back.inventory.equipment_count(), 2, "装备往返一致（两件）")
	check_eq(back.available_points(db, char_id), 4, "加点往返一致（1 级 5 点，已用 1 点）")
	check_eq(int(back.allocations_of(char_id).get("con", 0)), 1, "加点内容往返一致")


## v3（有熟练度、没有已学与装配）老档：读进来要补出「已学 + 装配」，并且带上熟练度里那份武学
func _check_v3_migration(db) -> void:
	# 同 `_check_v2_round_trip`：验的是"从 v3 补出已学与装配"这套机制，
	# 图鉴条数会随模板数变化 → 钉成单模板夹具（框架说明决策 161）。
	var roster = table_with_n_chars(1)
	var fresh = GameStateScript.new_game(roster, "normal")
	var data: Dictionary = fresh.to_dict()
	data["version"] = 3
	data.erase("char_learned")
	data.erase("char_loadouts")
	data["skill_mastery"] = {"scholar_fallen": {"sk_wudu_01": 4}}

	var state = GameStateScript.from_dict(data, roster)
	check_not_null(state, "v3 存档能读进来")
	if state == null:
		return
	check_eq(state.migrated_from, 3, "记下从 v3 迁移")
	check_eq(state.version, GameStateScript.VERSION, "版本升到当前")
	check_true(state.is_learned("scholar_fallen", "sk_xuanwei_01"), "模板起始武学补进已学")
	check_true(state.is_learned("scholar_fallen", "sk_wudu_01"), "熟练度里出现过的武学也算已学")
	check_eq(state.mastery_of("scholar_fallen", "sk_wudu_01"), 4, "熟练度本身保留")
	var loadout: Dictionary = state.loadout_of("scholar_fallen")
	check_gt(float(PackedStringArray(loadout["active"]).size()), 0.0, "迁移时按容量铺好装配")
	check_eq(state.collected_skill_count(), 2, "图鉴按去重计数：起始武学 + 熟练度里的那部")

	# 再写回一遍：v4 往返幂等
	var back = GameStateScript.from_dict(state.to_dict(), roster)
	check_eq(back.migrated_from, 0, "v4 写回后不再需要迁移")
	check_eq(
		PackedStringArray(back.loadout_of("scholar_fallen")["active"]),
		PackedStringArray(loadout["active"]),
		"装配往返一致",
	)


## v4（装备实例还没有词条字段）老档：读进来默认没有词条，版本升到 v5
func _check_v4_migration(db) -> void:
	# 装备件数 = 各模板起始装备之和（1 个模板 = 1 件）→ 钉成单模板夹具，专测 v4→v5 的词条补齐
	# （框架说明决策 161）。
	var roster = table_with_n_chars(1)
	var fresh = GameStateScript.new_game(roster, "normal")
	var data: Dictionary = fresh.to_dict()
	data["version"] = 4
	# 把装备实例里的 affixes 拿掉，模拟 v4 存档的形状
	var equipment: Dictionary = data["inventory"]["equipment"]
	for instance_id: String in equipment:
		Dictionary(equipment[instance_id]).erase("affixes")

	var state = GameStateScript.from_dict(data, roster)
	check_not_null(state, "v4 存档能读进来")
	if state == null:
		return
	check_eq(state.migrated_from, 4, "记下从 v4 迁移")
	check_eq(state.version, GameStateScript.VERSION, "版本升到当前")
	var instance_id := str(state.inventory.equipment_ids()[0])
	check_true(state.inventory.affixes_of(instance_id).is_empty(), "v4 的装备没有词条（不是空词条对象）")
	check_eq(state.inventory.equipment_count(), 2, "装备本体还在（两件）")


## 老档里已经下架的角色（早期是 ch_gang／ch_du／ch_ci／ch_qi 四个，现在只有一个模板）：
## 读档要把它们剔掉、换成一个能玩的角色，等级取最高值，装备退回背包。
## 不清的话队伍是空的——每场战斗瞬间判负，玩家还看不出为什么。
func _check_retired_characters(db) -> void:
	var legacy := {
		"version": 4,
		"char_ids": ["ch_gang", "ch_du", "ch_ci", "ch_qi"],
		"char_levels": {"ch_gang": 9, "ch_du": 4, "ch_ci": 4, "ch_qi": 4},
		"difficulty_id": "normal",
		"chapter_id": "chapter_01",
		"slot": 1,
		"created_unix": 11,
		"saved_unix": 22,
		"inventory": {
			"stacks": {"item_herb": 3},
			"equipment": {"eq_sword_01#9": {"base_id": "eq_sword_01", "affixes": []}},
			"equipped": {"ch_gang": {"weapon": ["eq_sword_01#9"]}},
			"next_uid": 10,
			"money": 77,
		},
	}
	# 这条用例的剧本是「老档里那 4 个 char_id 已经不在表里」。而 01 的备选模板里正好有同名
	# `ch_gang`／`ch_du`／`ch_ci`／`ch_qi`——一旦把它们启用，这些角色就**不是下架角色**了，
	# 剧本不再成立。所以把 roster 钉成单模板夹具（只剩 `scholar_fallen`），剧本才稳定
	# （框架说明决策 161）。
	var roster = table_with_n_chars(1)
	var state = GameStateScript.from_dict(legacy, roster)
	check_not_null(state, "带下架角色的老档能读进来")
	if state == null:
		return
	# 接手几个角色由**表里现有模板数**决定（上限 MAX_PARTY）：1 个模板时 1 个、4 个时 4 个。
	# 写死"1 个"会把设计状态钉进用例（框架说明决策 161）。
	check_eq(state.pruned_characters, 4, "记下剔掉了 4 个下架角色")
	check_eq(Array(state.char_ids).size(), 1, "换来 1 个表里还在的角色")
	var new_id := str(state.char_ids[0])
	check_eq(new_id, "scholar_fallen", "接手的角色取自 character_base")
	check_eq(state.level_of(new_id), 9, "等级取被剔角色的最高值，进度不倒退")
	check_eq(state.inventory.count("item_herb"), 3, "背包材料没丢")
	check_eq(state.inventory.money, 77, "铜钱没丢")
	check_true(state.inventory.equipment_count() >= 1, "老档那件装备还在（下架角色身上的也退回背包）")
	check_true(
		Dictionary(state.inventory.equipped_by("eq_sword_01#9")).is_empty(),
		"装备不再挂在下架角色身上（退回背包了）",
	)
	# 接手角色要能真的进战斗：有招、有气血、能造出战斗单位
	var actor = PartyBuilderScript.build_actor(db, state, new_id)
	check_not_null(actor, "接手角色能造出战斗单位")
	if actor != null:
		check_gt(float(actor.max_hp()), 0.0, "有气血上限")
		check_gt(float(actor.skills.size()), 0.0, "有起手招式")
	check_eq(state.version, GameStateScript.VERSION, "版本升到当前")
	# 写回一遍：下架角色不再出现
	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_eq(back.pruned_characters, 0, "写回后再读没有可剔的角色")
	check_eq(str(back.char_ids[0]), "scholar_fallen", "写回后队伍稳定")


## v11 → v12：战斗外增益进存档。老档（没有 `field_buffs`）读进来就是「没有增益」，
## 新档往返要**连到期时间戳一起保留**（按现实时间计时，离线那段时间照样算）。
func _check_v12_field_buffs(db) -> void:
	var session = load("res://src/autoload/game_session.gd").new()
	var state = GameStateScript.new_game(db, "normal")
	session.set_state(state)
	var now := int(Time.get_unix_time_from_system())
	check_true(bool(session.add_field_buff("buff_meditated", 10)["ok"]), "打坐余韵挂到存档上")
	check_eq(state.field_buffs.size(), 1, "增益落在 GameState（v12 起可持久化），不是只在会话里")

	# 老档：把 v12 之后才有的字段删掉再读 → 当作没有增益
	var legacy: Dictionary = state.to_dict()
	legacy["version"] = 11
	legacy.erase("field_buffs")
	var old = GameStateScript.from_dict(legacy, db)
	check_not_null(old, "v11 老档能读进来")
	if old == null:
		return
	check_eq(old.migrated_from, 11, "记下从 v11 迁移")
	check_eq(old.field_buffs.size(), 0, "老档没有战斗外增益（当作没打坐）")

	# 往返：带到期时间戳一起回来
	legacy = state.to_dict()
	var back = GameStateScript.from_dict(legacy, db)
	check_eq(back.field_buffs.size(), 1, "v12 往返保留增益")
	check_eq(str(back.field_buffs[0]["buff_id"]), "buff_meditated", "保留的是哪条增益")
	check_gt(float(back.field_buffs[0]["expires_at"]), float(now), "到期时间戳是**绝对时间**（离线也照走）")

	# 坏数据（缺 buff_id / 到期时间）直接丢掉，不读进一条「永远不过期」的增益
	legacy["field_buffs"] = [
		{"buff_id": "", "expires_at": now + 600},
		{"buff_id": "buff_meal"},
		{"buff_id": "buff_meal", "expires_at": now + 600, "stacks": 2},
	]
	var filtered = GameStateScript.from_dict(legacy, db)
	check_eq(filtered.field_buffs.size(), 1, "坏条目被丢掉，只留合法的")
	check_eq(str(filtered.field_buffs[0]["buff_id"]), "buff_meal", "留下的那条是对的")
	session.free()


## v12 → v13：大地图坐标进存档（设计 09 §二：读档一律回大地图，落点用这个坐标）。
## 老档（没有 `world_pos`）读进来 = 没有记录（大地图退回默认出生点）；
## 新档往返要把坐标原样带回来——会话层写进去的那一份必须真的落进存档。
func _check_v13_world_pos(db) -> void:
	var session = load("res://src/autoload/game_session.gd").new()
	var state = GameStateScript.new_game(db, "normal")
	session.set_state(state)
	var spot := Vector2(412.5, 1337.25)
	session.set_world_position(spot)
	check_eq(state.world_position(), spot, "会话层写坐标时顺手同步进存档（v13 起）")

	# 新档往返：坐标原样回来，而且读档后会话字段也照它摆人
	var data: Dictionary = state.to_dict()
	check_true(data.has("world_pos"), "to_dict 里带上了 world_pos")
	var back = GameStateScript.from_dict(data, db)
	check_not_null(back, "带坐标的档能读回来")
	if back == null:
		session.free()
		return
	check_eq(back.world_position(), spot, "v13 往返保留大地图坐标")
	var reloaded_session = load("res://src/autoload/game_session.gd").new()
	reloaded_session.set_state(back)
	check_eq(reloaded_session.world_position, spot, "读档后会话里的大地图落点就是存档里那个坐标")
	reloaded_session.free()

	# 老档：v12 及更早没有这条记录 → 当作「没有坐标」，用默认出生点
	var legacy: Dictionary = data.duplicate(true)
	legacy["version"] = 12
	legacy.erase("world_pos")
	var old = GameStateScript.from_dict(legacy, db)
	check_not_null(old, "v12 老档能读进来")
	if old != null:
		check_eq(old.migrated_from, 12, "记下从 v12 迁移")
		check_eq(old.world_position(), Vector2.ZERO, "老档没有坐标记录（退回默认出生点）")

	# 写坏的坐标（x 是字符串）宁可丢掉，也不能读进一个 NaN 把玩家扔出地图
	var broken: Dictionary = data.duplicate(true)
	broken["world_pos"] = {"x": "左", "y": 3}
	var salvaged = GameStateScript.from_dict(broken, db)
	check_eq(salvaged.world_position(), Vector2.ZERO, "写坏的坐标被丢掉，不当成一条记录")
	session.free()


## v13 → v14：天赋与自建角色模板进存档（设计 12／13）。
##
## 两条都要：① 老档（没有这两份记录）读进来 = 没天赋、没自建角色，且版本升到 14；
## ② 新档往返：天赋列表原样回来；**自建角色的模板要重新注入 db**，
## 否则按 char_id 取 `character_base` 会查不到，装备与武学全落空。
func _check_v14_creation(db) -> void:
	var fresh = GameStateScript.new_game(db, "normal")
	var char_id := str(fresh.char_ids[0])
	fresh.talent_picks[char_id] = ["tal_tie_shen"]
	var data: Dictionary = fresh.to_dict()
	var back = GameStateScript.from_dict(data, db)
	check_eq(Array(back.talent_picks.get(char_id, [])).size(), 1, "天赋随存档往返")
	check_eq(str(Array(back.talent_picks.get(char_id, []))[0]), "tal_tie_shen", "天赋 id 原样")

	# 老档（v13）：把这两份记录删掉再读 → 空，版本升到 14
	var legacy: Dictionary = data.duplicate(true)
	legacy["version"] = 13
	legacy.erase("talent_picks")
	legacy.erase("custom_templates")
	var old = GameStateScript.from_dict(legacy, db)
	check_true(old.talent_picks.is_empty(), "v13 老档没有天赋记录（不是坏档）")
	check_true(old.custom_templates.is_empty(), "v13 老档没有自建角色")
	check_eq(old.version, GameStateScript.VERSION, "老档读进来后升到当前版本")

	# 自建角色：模板进存档 → 读档时重新注入 character_base，按 char_id 查得到
	var custom: Dictionary = data.duplicate(true)
	custom["version"] = 14
	custom["char_ids"] = ["char_custom"]
	custom["char_levels"] = {"char_custom": 1}
	custom["talent_picks"] = {}
	custom["custom_templates"] = {"char_custom": {
		"name_cn": "自建书生", "role_tag": "自建", "weapon_type": "sword",
		"attrs": {"str": 8, "con": 8, "agi": 8, "int": 9, "luk": 5, "wu": 6, "gen": 5},
		"start_level": 1, "start_skill_ids": "sk_xuanwei_01", "start_equip_ids": "eq_sword_01",
	}}
	var restored = GameStateScript.from_dict(custom, db)
	check_not_null(restored, "自建角色的档读得回来")
	if restored != null:
		check_true(restored.char_ids.has("char_custom"), "自建角色留在队伍里（没被当成下架角色清掉）")
		var row: Resource = db.get_row("character_base", "char_custom")
		check_not_null(row, "读档时把自建模板重新注入 character_base")
		if row != null:
			check_eq(int(row.initial_con), 8, "自建模板的七维从存档恢复")
			check_eq(str(row.weapon_type), "sword", "武器类型也恢复")


## v10（没有战斗外气血）老档：读进来当作全队满血，版本升到 v11。
func _check_v10_migration(db) -> void:
	var fresh = GameStateScript.new_game(db, "normal")
	var char_id := str(fresh.char_ids[0])
	fresh.set_current_hp(char_id, 7)
	var data: Dictionary = fresh.to_dict()
	data["version"] = 10
	data.erase("char_hp")

	var state = GameStateScript.from_dict(data, db)
	check_not_null(state, "v10 存档能读进来")
	if state == null:
		return
	check_eq(state.migrated_from, 10, "记下从 v10 迁移")
	check_eq(state.version, GameStateScript.VERSION, "版本升到当前")
	check_eq(state.current_hp_of(char_id), -1, "v10 老档没有气血记录")
	check_false(state.is_wounded(char_id), "没有记录就是满血")
	# 迁移后照常能记伤
	state.set_current_hp(char_id, 12)
	check_eq(state.current_hp_of(char_id), 12, "迁移后能记战斗外气血")
	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_eq(back.migrated_from, 0, "v11 写回后不再需要迁移")
	check_eq(back.current_hp_of(char_id), 12, "气血往返一致")


## v9（没有首杀记录）老档：读进来首杀记录为空（当作没打过），版本升到 v10。
func _check_v9_migration(db) -> void:
	var fresh = GameStateScript.new_game(db, "normal")
	var data: Dictionary = fresh.to_dict()
	data["version"] = 9
	data.erase("first_kills")

	var state = GameStateScript.from_dict(data, db)
	check_not_null(state, "v9 存档能读进来")
	if state == null:
		return
	check_eq(state.migrated_from, 9, "记下从 v9 迁移")
	check_eq(state.version, GameStateScript.VERSION, "版本升到当前")
	check_eq(state.first_kill_count(), 0, "v9 老档没有首杀记录")
	check_false(state.has_first_kill("drop_bd_boss"), "v9 老档视作没打过首杀")
	check_true(state.mark_first_kill("drop_bd_boss"), "迁移后能正常记首杀")

	# 写回一遍：v10 往返幂等
	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_eq(back.migrated_from, 0, "v10 写回后不再需要迁移")
	check_true(back.has_first_kill("drop_bd_boss"), "首杀记录往返一致")


## 逐档迁移：v1…v10 **每一档**都要能读进来。
##
## 以前只显式覆盖 v1／v3／v4／v9／v10，v2／v5／v6／v7／v8 只是「被间接碰过」——
## 中间某一档读不出来（比如那年加的字段少写一个默认值），用过老档的人一读档就空队/崩，
## 而所有用例照样绿。这里按 `ADDED_IN` 把每一档的字段删掉再读，断言：
## 版本号被记下、升到当前版本、队伍与背包都在，且「那一档该有的字段」还在、
## 「那一档之后才有的字段」确实没被读进来（版本号是权威）。
func _check_every_legacy_version(db) -> void:
	var current = GameStateScript.new_game(db, "normal")
	var char_id := str(current.char_ids[0])
	current.char_allocations[char_id] = {"str": 1}
	current.inventory.add_item(db, "item_herb", 2)
	current.party_exp = 30
	current.record_event_check("ev_shed_trap", "done")
	current.set_flag("event_parley")
	current.mark_first_kill("drop_wolf")
	current.set_current_hp(char_id, 12)
	current.record_purchase("bld_smith", "eq_sword_01", 1)
	current.record_dungeon("scene_heifengzhai", "chests", "hf1_shed")
	current.reveal_node("n_cave_collapse")
	for legacy_version in range(1, GameStateScript.VERSION):
		var data: Dictionary = current.to_dict()
		data["version"] = legacy_version
		var erased := 0
		for added_version: int in ADDED_IN:
			if added_version <= legacy_version:
				continue
			for field: String in ADDED_IN[added_version]:
				if data.has(field):
					data.erase(field)
					erased += 1
		if legacy_version < 5:
			# v5 才给装备实例加 affixes：更老的档里实例没有这个词条字段
			for instance_id: Variant in Dictionary(Dictionary(data.get("inventory", {})).get("equipment", {})):
				Dictionary(Dictionary(data["inventory"])["equipment"][instance_id]).erase("affixes")
		var migrated = GameStateScript.from_dict(data, db)
		var tag := "v%d 老档（删了 %d 个新字段）" % [legacy_version, erased]
		check_not_null(migrated, "%s 能读进来" % tag)
		if migrated == null:
			continue
		check_eq(migrated.migrated_from, legacy_version, "%s 记下从哪一档迁移" % tag)
		check_eq(migrated.version, GameStateScript.VERSION, "%s 升到当前版本" % tag)
		check_gt(float(migrated.char_ids.size()), 0.0, "%s 读进来队伍不为空" % tag)
		check_not_null(migrated.inventory, "%s 读进来有背包" % tag)
		# 后面每一条都要摸 inventory：它要是真丢了（迁移写坏），这里先停下，
		# 让失败信息停在「没背包」这一条上，别用空引用把整个用例炸掉（踩过：假绿门限才把它抓出来）
		if migrated.inventory == null:
			continue
		check_eq(migrated.difficulty_id, "normal", "%s 难度保留" % tag)
		# v2 起才有背包与经验池：v1 老档要补出背包（初始装备），经验池归零
		if legacy_version >= 2:
			check_eq(int(migrated.party_exp), 30, "%s 经验池保留" % tag)
			check_eq(migrated.inventory.count("item_herb"), 2, "%s 背包物品保留" % tag)
		else:
			check_eq(int(migrated.party_exp), 0, "%s 经验池归零（那会儿还没有）" % tag)
			check_gt(float(migrated.inventory.equipment.size()), 0.0, "%s 补出了初始装备" % tag)
		# v9 起才有事件记录；更老的档即使字典里被塞了这些字段也不该读进来
		if legacy_version >= 9:
			check_eq(str(migrated.event_check_result("ev_shed_trap")), "done", "%s 事件记录保留" % tag)
			check_true(migrated.has_flag("event_parley"), "%s 剧情旗标保留" % tag)
		else:
			check_eq(str(migrated.event_check_result("ev_shed_trap")), "", "%s 没有事件记录" % tag)
			check_false(migrated.has_flag("event_parley"), "%s 没有剧情旗标" % tag)
		if legacy_version >= 10:
			check_true(migrated.has_first_kill("drop_wolf"), "%s 首杀记录保留" % tag)
		else:
			check_false(migrated.has_first_kill("drop_wolf"), "%s 首杀记录为空" % tag)
		if legacy_version >= 11:
			check_eq(migrated.current_hp_of(char_id), 12, "%s 战斗外气血保留" % tag)
		else:
			check_eq(migrated.current_hp_of(char_id), -1, "%s 没有气血记录（=满血）" % tag)
		if legacy_version >= 6:
			check_eq(migrated.purchased_count("bld_smith", "eq_sword_01"), 1, "%s 商店买入次数保留" % tag)
		else:
			check_eq(migrated.purchased_count("bld_smith", "eq_sword_01"), 0, "%s 没有商店记录" % tag)
		if legacy_version >= 7:
			check_true(
				Array(migrated.dungeon_record("scene_heifengzhai").get("chests", [])).has("hf1_shed"),
				"%s 副本完成度保留" % tag,
			)
		else:
			check_false(
				Array(migrated.dungeon_record("scene_heifengzhai").get("chests", [])).has("hf1_shed"),
				"%s 没有副本记录" % tag,
			)
		if legacy_version >= 8:
			check_true(migrated.is_node_revealed("n_cave_collapse"), "%s 揭开的地标保留" % tag)
		else:
			check_false(migrated.is_node_revealed("n_cave_collapse"), "%s 没有揭开记录" % tag)

	# 版本号是权威：字典里塞了 v11 才有的 char_hp，但 version 说 8 → 不许读进来
	var cheating: Dictionary = current.to_dict()
	cheating["version"] = 8
	var cheated = GameStateScript.from_dict(cheating, db)
	check_not_null(cheated, "版本号说 v8、但带着 v11 字段的档也能读")
	if cheated != null:
		check_eq(cheated.current_hp_of(char_id), -1, "v8 不该认 char_hp（版本号是权威）")
		check_eq(cheated.migrated_from, 8, "还是记成从 v8 迁移")


func _check_corrupt(db) -> void:
	var store = SaveStoreScript.new(TEST_DIR, SLOTS)
	var file := FileAccess.open(store.slot_path(3), FileAccess.WRITE)
	file.store_string("这不是 JSON")
	file.close()
	check_false(store.load_slot(3, db)["ok"], "坏档读不出来")
	check_true(bool(store.describe_slot(3, db)["corrupt"]), "坏档被标成损坏")


## **字段类型**写错的档（JSON 合法，`is_valid_dict` 也可能认）：要么被判成损坏，要么被容错读成缺省——
## **不许在运行期炸**。成因很现实：手改过的档、以后版本换了字段类型、外部工具生成的档。
##
## 每一条都走「加载 → 消费方」整条链（保底计数／状态行／写回再读／首杀／气血）：
## 中间任何一步抛运行期错误，它**后面的断言就不会执行**，`run_tests` 的「假绿」门限会当场点名
## ——所以这几条断言值本身不必花哨，位置才是重点。
func _check_type_mismatched_fields(db) -> void:
	var cases: Array = [
		{"name": "inventory 是字符串", "data": {"char_ids": ["scholar_fallen"], "char_levels": {"scholar_fallen": 1}, "difficulty_id": "normal", "inventory": "oops"}},
		{"name": "inventory 是数字", "data": {"char_ids": ["scholar_fallen"], "char_levels": {"scholar_fallen": 1}, "difficulty_id": "normal", "inventory": 7}},
		{"name": "char_ids 里塞数字", "data": {"char_ids": [1, 2], "char_levels": {}, "difficulty_id": "normal"}},
		{"name": "char_levels 里塞字符串", "data": {"char_ids": ["scholar_fallen"], "char_levels": {"scholar_fallen": "三"}, "difficulty_id": "normal"}},
		{"name": "pity 是数组", "data": {"char_ids": ["scholar_fallen"], "char_levels": {"scholar_fallen": 1}, "difficulty_id": "normal", "pity": [1, 2]}},
		{"name": "field_buffs 是字符串", "data": {"char_ids": ["scholar_fallen"], "char_levels": {"scholar_fallen": 1}, "difficulty_id": "normal", "field_buffs": "x"}},
	]
	for case: Dictionary in cases:
		var label := str(case["name"])
		var data: Dictionary = case["data"]
		if not GameStateScript.is_valid_dict(data):
			# 判成损坏也是一种合法处理：这里只记一条，别让它悄悄溜过去
			check_false(GameStateScript.is_valid_dict(data), "%s：类型不对时判成损坏档" % label)
			continue
		var state = GameStateScript.from_dict(data, db)
		check_not_null(state, "%s：判成有效就必须读得出来（不许崩）" % label)
		if state == null:
			continue
		check_eq(state.pity_tracker().attempts("x"), 0, "%s：保底计数读得出（新读出来是空的）" % label)
		check_true(
			"章节：" in "\n".join(state.summary_lines(db)),
			"%s：状态行读得出来（不会因为某个字段类型不对就整页空）" % label,
		)
		var again = GameStateScript.from_dict(state.to_dict(), db)
		check_not_null(again, "%s：写回再读一遍也稳" % label)
		if again != null:
			check_eq(
				again.to_dict().keys().size(), state.to_dict().keys().size(),
				"%s：往返前后字段数一致" % label,
			)
		check_eq(state.first_kill_count(), 0, "%s：首杀计数读得出（没记过就是 0）" % label)
		check_false(state.is_wounded("scholar_fallen"), "%s：没有气血记录 = 满血（不是残血）" % label)


func _clean_dir(path: String) -> void:
	if DirAccess.dir_exists_absolute(path):
		var dir := DirAccess.open(path)
		if dir != null:
			for file_name: String in dir.get_files():
				dir.remove(file_name)
	DirAccess.make_dir_recursive_absolute(path)
