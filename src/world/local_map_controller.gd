## 小地图控制器（城镇／副本通用）。
##
## 职责：
##   1. 实例化 `scenes/maps/<scene_id>.tscn`（地编资产，只读不动）
##   2. 生成玩家（入口房间）与房间敌人（`Enemies/<room_id>/Team_<team_id>`）
##   3. 生成宝箱（`Chest_<drop_group>`）与隐藏触发点（`Trigger_<id>`）
##   4. 交互（E）：开宝箱、触发隐藏内容、走 `Exit_<scene_id>` 回大地图
##   5. 房间敌人接触 → Encounter → 战斗；打回来的清怪状态存在会话里
##
## 会话状态（`GameSession.local_maps[scene_id]`）在小地图内持续，**回大地图就整片刷新**
## ——与 02_地图与明雷.md 的刷新规则一致。
extends Node2D

const PlayerControllerScript := preload("res://src/world/player_controller.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
## 条件地表看「母地标是不是持有类解锁」，判据与大地图共用一份（`WorldMapService.held_item_id`）
const WorldMapServiceScript := preload("res://src/core/world_map_service.gd")
const RoamingEnemyScript := preload("res://src/world/roaming_enemy.gd")
const ChestScript := preload("res://src/world/chest.gd")
const TriggerPointScript := preload("res://src/world/trigger_point.gd")
const EncounterScript := preload("res://src/core/encounter.gd")
const BattleRewardScript := preload("res://src/core/battle_reward.gd")
const AffixRollerScript := preload("res://src/core/affix_roller.gd")
const SkillGrantScript := preload("res://src/core/skill_grant.gd")
const DungeonServiceScript := preload("res://src/core/dungeon_service.gd")
const EventCheckServiceScript := preload("res://src/core/event_check_service.gd")
const DropResolverScript := preload("res://src/core/drop_resolver.gd")
const PityTrackerScript := preload("res://src/core/pity_tracker.gd")
const InputSetupScript := preload("res://src/world/input_setup.gd")
const TableDbScript := preload("res://src/core/table_db.gd")
## 战斗外增益 HUD：与大地图共用同一份文案（`FieldBuffHud`）
const FieldBuffHudScript := preload("res://src/world/field_buff_hud.gd")
const GuideServiceScript := preload("res://src/core/guide_service.gd")
const StoryServiceScript := preload("res://src/core/story_service.gd")
const ChapterServiceScript := preload("res://src/core/chapter_service.gd")
const OverlayStackScript := preload("res://src/ui/overlay_stack.gd")
const NpcServiceScript := preload("res://src/core/npc_service.gd")
## NPC 面板的两种模式（设计 0.28.0 的 Q62）：E = 交互、Q = 只读信息
const NpcPanelMode := preload("res://src/ui/npc_panel.gd")
const CHARACTER_SCENE := "res://scenes/character_screen.tscn"
const NPC_SCENE := "res://scenes/npc_panel.tscn"
const RecruitServiceScript := preload("res://src/core/recruit_service.gd")
## 观察点（设计 20 §3.2）：最薄的一张表，一个位点一句话
const FlavorPointScript := preload("res://src/data/tables/flavor_point_row.gd")
## 观察点的可见标记（设计 15 §一「可交互物暖色提亮」）：视觉只有一处出处
const FlavorMarkerScript := preload("res://src/world/flavor_marker.gd")
## 对话容器（设计 20 §十一）：客栈那一次招募就是燕小七的幕一对话
const DialogueServiceScript := preload("res://src/core/dialogue_service.gd")
const GuardServiceScript := preload("res://src/core/guard_service.gd")
const PracticeServiceScript := preload("res://src/core/practice_service.gd")
const SfxScript := preload("res://src/audio/sfx.gd")

const BATTLE_SCENE := "res://scenes/battle_screen.tscn"
const SHOP_SCENE := "res://scenes/shop_screen.tscn"
const CULTIVATE_SCENE := "res://scenes/cultivate_screen.tscn"
const CLUE_SCENE := "res://scenes/clue_screen.tscn"
const DUNGEON_SCENE := "res://scenes/dungeon_screen.tscn"
const SaveServiceScript := preload("res://src/core/save_service.gd")
const OVERWORLD_RUN := "res://scenes/world_run.tscn"
const EXIT_DISTANCE := 28.0

## 条件地表层的名字（设计 16 §3.2 的第 5 层，0.32.0 新增）。地编摆这一层，代码按条件整层显隐。
const CONDITIONAL_LAYER := "Conditional"
## 玩家位于遮挡瓦片（TileSet 的 `covering`）下方时，`Overlay` 层压到这个透明度
## （设计 07 §节点结构「玩家走到下面时半透明」／16 §3.2）。
const OVERLAY_COVER_ALPHA := 0.35
## 站多近才能跟 NPC 说话（格 = 32px，这里约 1 格）
const NPC_DISTANCE := 30.0
## NPC 的**占位称呼**：07 §8.2 把镇上的非功能角色写成「平民 NPC／若干路人」，
## 真正的名字与台词等 `dialogue_tree.csv`（那之前不自己编身份）
const NPC_SPEAKER := "路人"
## 站多近才能按 E 进店（格 = 32px，这里约 1.5 格）
const BUILDING_DISTANCE := 48.0
## 事件判定位点的交互距离（站在草丛／石碑旁边按 E）
const EVENT_DISTANCE := 40.0
## 本小地图自己的场景路径（「挖通」这类跨图捷径要重进它，好让新落点生效）
const LOCAL_RUN := "res://scenes/local_run.tscn"
## 出口要「先走出去一次」才武装：出生点可能就压在出口位点上（清风驿的城门就是这么摆的），
## 不定这条的话一进图就被判定成走到出口，立刻弹回大地图（真踩过）
const EXIT_ARM_MARGIN := 8.0
## 刚进场/刚回图的**接触宽限期**（秒）：败北回图时落点就在敌人身上，
## 推开之后还可能被墙顶回接触圈——那也会当场又开一场，玩家等于被锁在败仗里。
## 宽限期内忽略接触事件（敌人自己的 `_encounter_latched` 会挡住重复触发，所以忽略一次就够）。
const CONTACT_GRACE := 0.5
## 设施的显示名（这几个在 07 文档里是「无表信息点」，所以名字写在这里）：
## 场景里它们和建筑一样是**看不见的 Marker2D**，不标出来玩家不知道哪栋是客栈。
## 建筑名从 `building_def.name_cn` 取（表里有）。
const FACILITY_LABELS := {
	"facility_inn": "客栈",
	"facility_pawnshop": "当铺",
	"facility_bounty_board": "悬赏板",
	# 0.31.0：书铺（陆文昭「本命机遇·书箱底」的落点，07 §4.1 新增的无表信息点）。
	"facility_bookshop": "书铺",
}
## 引导第一步的旗标（09 §3.1「去清风驿的告示板看看」）。表里 `guide_step.condition` 写的就是这个名字，
## 这里只负责在玩家真的读了告示板时点亮它——**引导推进本身归 `GuideService`**。
const FLAG_BOARD_READ := "flag_board_read"

## 打赢某支队伍之后、回到图上要摆出来的那段对话（20 号 §七 的终局难题：打完大寨主，
## 账册到手，沈雁回问你打算怎么办）。value 是 `dialogue_node.node_id`——
## 「还没选过」「对质打过了」这些**条件写在表里**（`dialogue_node.condition`），
## 代码只做「上一场打赢的是哪支队伍 → 翻哪张对话」这一层换算。
##
## 与 `battle_screen.TEAM_WIN_FLAGS` 同一套触发点（都是「打赢某队」的后果），
## 但那两张表分在各自的场景层：旗标是战斗结算的事，对话要等回到图上才摆得出来。
const TEAM_WIN_DIALOGUES := {"team_boss": "dl_ledger_choice"}

## 判定**通过**之后要接着说的那句话（20 号 §四 幕四：地牢那次判定过了，铁栏后的人才开口）。
## value 是 `dialogue_node.node_id`——「现在该不该说」写在表的 `condition` 里
## （她一开口就是「你是来领那三十两的？」，打过大寨主之后换问账册那件事）。
const EVENT_DIALOGUES := {"ev_shen_rescue": "dl_shen_cell"}

## 序幕·择念（设计 20 号 §3.1）：读告示板那一下的**第一个决定**。
##
## 设计 0.29.1 v2 第 4 条把它定在「悬赏板交互里，与引子同框、零新增位点」——
## 「玩家的第一个决定要和他的第一个动作在一起」。台词与三个选项都在表里，
## 这里只负责什么时候摆出来；**条件写在表里**（三个心性旗标一个都没点亮过），
## 选过之后同一条条件不再成立，所以不会反复问。
##
## 节点 id 的唯一出处是 `DialogueService.OPENING_NODE`（`choose()` 要在同一处顺带置
## `flag_open_*`，见那里的注释），这里只引用，不另立一份。
const OPENING_CHOICE_NODE := DialogueServiceScript.OPENING_NODE

var battle_switch_handler := Callable()
var return_handler := Callable()
## 跨图捷径（挖通 → 直通后山地牢）要重进小地图场景；自检／用例用它拦下切场景
var portal_switch_handler := Callable()
var state_override = null
## 存档设施（用例可注入临时目录；正常流程用 SaveStore.default_dir()）
var save_store_override = null

var db
var scene_id: String = ""
var world: Node2D = null
var player = null
var camera: Camera2D = null
var teams: Array = []
var chests: Array = []
var triggers: Array = []
## 城镇 NPC 站位（`Characters/npc_slot_0N`）：只有位置，内容等对话表
var npcs: Array = []
## 商店是覆盖在当前场景上的界面（不切场景，所以玩家位置与清怪状态都不丢）
var shop_panel: Node = null
## 打坐界面（同上，客栈用）
var cultivate_panel: Node = null
## 线索本（K 打开：本图的隐藏内容与事件判定 + 线索来源）
var clue_panel: Node = null
## 完成度界面（M 打开：四项完成度 + 楼层与扫荡按钮）
var dungeon_panel: Node = null
var npc_panel: Node = null
var _save_service = null

var _status: Label
## 开局引导 HUD（设计 09 §3.1）：HUD 第四行常驻「当前目标」
var _guide_label: Label = null
## 宝箱守卫（设计 09 §一）：玩家已经听过谁的「要价」（guard_id → true）。
## 只放会话里：第一次按 E 只是让他开口，第二次才付账——这条确认链不需要跨存档记住。
var _guard_demands: Dictionary = {}
## 战斗外增益 HUD（08）：城镇／副本里也要看得见（与大地图共用同一份文案）
var _field_label: Label = null
var _field_refresh_timer := 0.0
## 副本完成度 HUD（只有 has_completion 的图显示）
var _progress_label: Label
var _hud: CanvasLayer = null
## 事件判定位点：{check_id, node, position}
var events: Array = []
## 观察点位点：{point_id, node, position}（设计 20 §3.2；只在这张图里收）
var flavor_points: Array = []
var _event_service = null
## 浮层栈（设计 18.1）：商店／打坐／角色／线索本／完成度都压在它上面
var _overlays = null
var _was_sneaking := false
var _dungeon
## 遮挡层（`Overlay`）与它当前是不是压在玩家身上（0.32.0：石隙死路的岩檐）
var _overlay_layer: TileMapLayer = null
var _overlay_covering := false
## 这一层的 TileSet 认不认 `covering` 自定义数据（老资产没有这一层 → 不做半透明，不报错）
var _covering_supported := false
## 条件地表层（`Conditional`）：拿到藏宝图才显示石隙那条细径
var _conditional_layer: TileMapLayer = null
var _last_room_id := ""
var _exit_position := Vector2.ZERO
var _has_exit := false
## 出口是否已经武装（出生点可能就在出口上，先走出去一次才算数）
var _exit_armed := false
## 接触宽限期的剩余秒数（见 `CONTACT_GRACE`）
var _contact_grace := 0.0


func _ready() -> void:
	setup()
	if _has_user_arg("--local-selftest"):
		call_deferred("_run_local_selftest")


func setup() -> void:
	if world != null:
		return
	InputSetupScript.ensure()
	db = _resolve_db()
	var session_node := _session_node()
	if session_node != null and session_node.state == null:
		# 自检或直接进小地图时没有会话状态：临时开一局（掉落会落在这份临时存档上）
		session_node.set_state(load("res://src/core/game_state.gd").new_game(db, "normal"))
	scene_id = str(session_node.pending_local_scene) if session_node != null else ""
	if scene_id.is_empty():
		# 自检默认挑副本（城镇没有房间敌人/宝箱/触发），正常流程由大地图写 pending_local_scene
		scene_id = "scene_heifengzhai" if _has_user_arg("--local-selftest") else "scene_qingfengyi"
	var packed: PackedScene = load("res://scenes/maps/%s.tscn" % scene_id)
	if packed == null:
		push_error("[LocalMap] 没有场景 %s" % scene_id)
		return
	world = packed.instantiate()
	add_child(world)
	var ysort: Node2D = world.get_node_or_null("YSort")
	if ysort != null:
		ysort.y_sort_enabled = true
	_prepare_conditional_layer()
	_prepare_overlay_cover()
	_spawn_player()
	_spawn_teams()
	_spawn_chests()
	_spawn_triggers()
	_collect_npc_slots()
	_build_place_labels()
	_collect_events()
	_collect_flavor_points()
	# 战败处理（08）要「回最近到过的出生点或城镇」——**进城镇时在这里登记**。
	# 只有这一处：大地图进图与驿站传送都走 `pending_local_scene` → 这条 setup，不会漏记也不会重复。
	_note_shelter_if_town(session_node)
	_build_status_label()
	_refresh_status()
	_build_progress_label()
	_build_guide_label()
	# 进图就把「条件早就满足」的同伴收进来（例如旗标是在别的图上点亮的）；
	# **城镇除外**：设计 09 §3.2 说城镇里那次是「在客栈对话」，所以城镇的入队只认客栈那一次交互
	# （见 `interact()` 的 facility_inn 分支）——进图自动收人会跳过玩家该走的那一步。
	if not _is_town():
		_join_pending_recruits(false)
	# 剧情节点（0.22.0）：条件旗标已点亮 + 到了这张图 → 发武学；
	# `chapter_end` 节点同时置章节完成条件。**进图与换房间都要判**（人是走进去的）。
	_claim_story_nodes()
	# 剧情抉择（20 §七）：上一场打赢大寨主之后，终局难题就在这张图上等人回答。
	# 条件（没选过 ＋ 对质打过了）全在表的 `dialogue_node.condition` 里，这里只负责摆出来。
	_open_pending_choice_dialogue()
	# 备货旗标这类「口径写在表里、但没人去置」的条件，进图时算一次
	GuideServiceScript.refresh_derived_flags(db, current_state())
	if _clear_team_overlap():
		_contact_grace = CONTACT_GRACE


# ---------------------------------------------------------------- 地表分层（0.32.0）

## 条件地表（设计 16 §3.2 第 5 层 `Conditional`）：**整层**显隐，不是逐格擦。
##
## 唯一在用的地方是**大地图**上那条「落雁坡西 → 石隙迷窟」的碎石细径（22 格，藏在藏宝图里）。
## 小地图这边留着同一套接口：判据与大地图**共用一份**
## （`WorldMapService.conditional_layer_rule`），这里只是把「本图的地标」缩成母地标那一个
## （`map_local.parent_node`）——谁都不许再抄一份条件语言。
##
## 返回 {apply, items}：apply=false 表示这层没有条件可依（保持地编摆的样子，不擅自隐藏）。
func conditional_layer_rule() -> Dictionary:
	var node_ids := PackedStringArray()
	var local_row: Resource = db.get_row("map_local", scene_id) if db != null else null
	if local_row != null and not str(local_row.parent_node).is_empty():
		node_ids.append(str(local_row.parent_node))
	return WorldMapServiceScript.conditional_layer_rule(db, node_ids)


## 这层现在该不该显示（按上一条的规则问背包）
func conditional_layer_visible() -> bool:
	var node_ids := PackedStringArray()
	var local_row: Resource = db.get_row("map_local", scene_id) if db != null else null
	if local_row != null and not str(local_row.parent_node).is_empty():
		node_ids.append(str(local_row.parent_node))
	return WorldMapServiceScript.conditional_layer_visible(db, current_state(), node_ids)


func _prepare_conditional_layer() -> void:
	_conditional_layer = world.get_node_or_null(CONDITIONAL_LAYER) as TileMapLayer
	_apply_conditional_layer()


## 把条件地表的显隐刷成当前状态。**每帧便宜**（一次背包查询），随掉图／用图立刻跟上：
## 藏宝图可能是在这张图里开箱拿到的，不能等下次进图才显示。
func _apply_conditional_layer() -> void:
	if _conditional_layer == null:
		return
	_conditional_layer.visible = conditional_layer_visible()


func _prepare_overlay_cover() -> void:
	_overlay_layer = world.get_node_or_null("Overlay") as TileMapLayer
	if _overlay_layer == null or _overlay_layer.tile_set == null:
		return
	_covering_supported = tile_set_has_covering(_overlay_layer.tile_set)
	_refresh_overlay_cover()


## TileSet 里有没有 `covering` 这一层自定义数据（由 `tools/mapgen/map_kit.gd` 生成）。
## 老资产没有它时静默退化：不做半透明，也不报错——「量不出来 ≠ 塞不下」那套口径同样适用。
static func tile_set_has_covering(tile_set: TileSet) -> bool:
	if tile_set == null:
		return false
	for index in tile_set.get_custom_data_layers_count():
		if tile_set.get_custom_data_layer_name(index) == "covering":
			return true
	return false


## 玩家（脚下一格 ＋ 头顶半格）有没有被「遮挡类」`Overlay` 瓦片盖住。
## 静态：用例可以直接喂一层验判据，不必先造出「地编还没交付的那种图」。
static func overlay_covering_at(layer: TileMapLayer, position: Vector2) -> bool:
	if layer == null or not tile_set_has_covering(layer.tile_set):
		return false
	for probe: Vector2 in [Vector2.ZERO, Vector2(0, -16)]:
		var cell: Vector2i = layer.local_to_map(layer.to_local(position + probe))
		var data: TileData = layer.get_cell_tile_data(cell)
		if data != null and bool(data.get_custom_data("covering")):
			return true
	return false


## 玩家走到遮挡瓦片下面 → `Overlay` 整层半透明（走开恢复）。
## 为什么整层而不是只削那一格：TileMapLayer 没有逐格透明度，而岩檐本来就只压在死路内段，
## 整层压暗在观感上正是「钻到岩檐下面，眼前让开」；**不是**把瓦片删掉（那是 Conditional 的事）。
func _refresh_overlay_cover() -> void:
	if _overlay_layer == null or not _covering_supported:
		return
	var under := player != null and overlay_covering_at(_overlay_layer, player.global_position)
	if under == _overlay_covering:
		return
	_overlay_covering = under
	_overlay_layer.modulate.a = OVERLAY_COVER_ALPHA if under else 1.0


## 领取这张图上能领的剧情节点（设计 18 §3.1）。返回领到的节点名，供状态栏播报。
func _claim_story_nodes() -> Array:
	var state = current_state()
	if state == null or db == null:
		return []
	var claimed: Array = StoryServiceScript.claim_for(db, state, scene_id)
	if claimed.is_empty():
		return []
	var names := PackedStringArray()
	for entry: Dictionary in claimed:
		names.append(str(entry.get("text_cn", entry.get("node_id", ""))))
	# 章节完成条件满足 → 推进章节（`chapter_def`，设计 18 §3.1）
	var advance: Dictionary = ChapterServiceScript.try_advance(db, state)
	if bool(advance.get("advanced", false)):
		names.append("章节推进：%s" % str(advance.get("name", "")))
	if not str(advance.get("text", "")).is_empty():
		names.append(str(advance.get("text")))
	if _status != null and not names.is_empty():
		_status.text = "；".join(names)
	_refresh_guide()
	return claimed


## 判定通过之后接的那段对话（配在 `EVENT_DIALOGUES` 里的判定才有）。
##
## 摆一段「旁白／自白」式的对话（**没有「这个人」**）：序幕择念（20 §3.1）用它——
## 数据仍是 `dialogue_node`／`dialogue_option`，只是面板按 `MODE_STORY` 渲染，
## 不摆头像、好感条与交往段（那一段是给 NPC 用的）。
func _open_story_dialogue(node_id: String) -> bool:
	var state = current_state()
	if db == null or state == null:
		return false
	var node: Resource = DialogueServiceScript.node_of(db, node_id)
	if node == null:
		push_error("[LocalMap] 要摆的旁白 %s 不在表里" % node_id)
		return false
	if not DialogueServiceScript.condition_ok(state, str(node.condition)):
		return false
	open_npc(str(node.speaker_id), NpcPanelMode.MODE_STORY, node_id)
	return true


## 判定通过之后接的那段对话（配在 `EVENT_DIALOGUES` 里的判定才有）。
##
## 为什么挂在判定后面而不是另摆一个 NPC 位点：设计 20 号 §四 幕四写的就是
## 「铁栏后的人**听过你的话之后**才回头」——台词接在同一个位点的判定之后，
## 而「她现在说哪一句」仍然由表的 `condition` 决定（本文件不替她编话）。
func _open_event_dialogue(check_id: String) -> bool:
	if db == null or not EVENT_DIALOGUES.has(check_id):
		return false
	var node_id := str(EVENT_DIALOGUES[check_id])
	var node: Resource = DialogueServiceScript.node_of(db, node_id)
	if node == null:
		push_error("[LocalMap] 判定 %s 之后要说的对话 %s 不在表里" % [check_id, node_id])
		return false
	var state = current_state()
	if state == null or not DialogueServiceScript.condition_ok(state, str(node.condition)):
		return false
	var speaker := str(node.speaker_id)
	if speaker.is_empty():
		push_error("[LocalMap] 对话 %s 没写说话人" % node_id)
		return false
	open_npc(speaker, NpcPanelMode.MODE_INTERACT, node_id)
	return true


## 上一场打赢的队伍有没有「回图就该摆出来的那段对话」（20 §七 的终局难题）。
##
## 触发链：`battle_screen._settle()` 把这一场的 `team_id` 写进 `GameSession.last_battle`
## → 回到小地图时这里翻 `TEAM_WIN_DIALOGUES` → 取那张 `dialogue_node` →
## **条件是表里写的**（`flag_heifeng_confront&!flag_ledger_*`）：没打过对质不会弹，
## 已经选过也不会再弹（选完那条条件就不成立了）。对话没配、说话人认不出来都**不静默**：出声到日志。
func _open_pending_choice_dialogue() -> bool:
	var session_node := _session_node()
	if session_node == null or db == null:
		return false
	var state = current_state()
	if state == null:
		return false
	# 候选从哪来分两种情况：
	#   ① 会话里认得出「上一场打赢的是哪支队」→ **只**摆那一支队配的那张对话；
	#   ② 认不出（读档／重开一局，`last_battle` 是空的）→ 把配置里所有候选都问一遍。
	# ② 不是可有可无：主菜单读档进来时 `last_battle` 已经不在会话里，而**那本账还在背包里**
	# ——只认①的话，玩家一读档就再也回答不了那道题，三样永久增益永远拿不到。
	var candidates := PackedStringArray()
	var last: Variant = session_node.last_battle
	var known_team := str(Dictionary(last).get("team_id", "")) if last is Dictionary else ""
	if known_team.is_empty():
		for key: String in TEAM_WIN_DIALOGUES.keys():
			candidates.append(str(TEAM_WIN_DIALOGUES[key]))
	elif TEAM_WIN_DIALOGUES.has(known_team):
		candidates.append(str(TEAM_WIN_DIALOGUES[known_team]))
	for node_id: String in candidates:
		var node: Resource = DialogueServiceScript.node_of(db, node_id)
		if node == null:
			push_error("[LocalMap] 要摆的对话 %s 不在表里" % node_id)
			continue
		if not DialogueServiceScript.condition_ok(state, str(node.condition)):
			continue
		var speaker := str(node.speaker_id)
		if speaker.is_empty():
			push_error("[LocalMap] 对话 %s 没写说话人" % node_id)
			continue
		# 有意**不记「摆过了」**：条件在表里——没选过就还会摆出来（玩家关掉面板走了，
		# 下次回到这张图它照样在等人回答）；选过之后同一条件不成立，自然就不再弹。
		open_npc(speaker, NpcPanelMode.MODE_INTERACT, node_id)
		return true
	return false


func current_state():
	if state_override != null:
		return state_override
	var session_node := _session_node()
	return session_node.state if session_node != null else null


## 进的是城镇（`map_local.scene_type=town`）就把它记成「最近的出生点／城镇」。
## 设计 08：败北后全队回到**最近到过的**出生点或城镇；本作第一章只有清风驿一个城镇。
func _note_shelter_if_town(session_node) -> void:
	if session_node == null:
		return
	var row: Resource = db.get_row("map_local", scene_id)
	if row == null or str(row.scene_type) != "town":
		return
	session_node.note_shelter(scene_id, str(row.name_cn))


## 这张图是不是城镇（`map_local.scene_type=town`）——剧情招募与战败回城都用这一条判。
func _is_town() -> bool:
	var row: Resource = db.get_row("map_local", scene_id)
	return row != null and str(row.scene_type) == "town"


func local_state() -> Dictionary:
	var session_node := _session_node()
	if session_node == null:
		return {}
	if not session_node.local_maps.has(scene_id):
		session_node.local_maps[scene_id] = {"cleared": [], "chests": [], "triggers": []}
	return session_node.local_maps[scene_id]


# ------------------------------------------------------------------ 生成

func _spawn_player() -> void:
	player = PlayerControllerScript.new()
	player.name = "Player"
	player.position = _entrance_position()
	_add(ysort_node(), player)
	player.z_index = 1
	camera = world.get_node_or_null("Camera")
	if camera != null:
		camera.global_position = player.global_position


## 落点：① 捷径指定的房间（挖通） ② 打完回图时的原位置 ③ 该图的入口房间 ④ 出口位点
##
## ② 以前是写进 `local_position` 就没再读过的——副本里打完一场会被弹回入口，
## 现在读回来（并核对 scene，免得跨图错用）。
func _entrance_position() -> Vector2:
	var session_node := _session_node()
	# ① 捷径落点：「铁镐挖通 → 直达后山地牢」会把 pending_local_room 写成 hf3_dungeon
	if session_node != null:
		var wanted := str(session_node.pending_local_room)
		if not wanted.is_empty():
			session_node.pending_local_room = ""
			if _room_belongs_here(wanted):
				var point: Variant = _room_spawn_point(wanted)
				if point != null:
					return point
			else:
				push_error("[LocalMap] %s 不属于本图 %s，落点请求被忽略" % [wanted, scene_id])
	# ② 打完回图：接着站在战斗前的位置（必须排在出生点前面，不然这条永远轮不到）
	if session_node != null and str(session_node.local_position_scene) == scene_id:
		var back: Vector2 = session_node.local_position
		session_node.local_position = Vector2.ZERO
		session_node.local_position_scene = ""
		if back != Vector2.ZERO:
			return back
	# ③ 地编放的出生点（`Characters/player_spawn`，metadata role=player）——资产里的第一真相
	var authored: Node2D = world.get_node_or_null("Characters/player_spawn")
	if authored != null:
		return authored.position
	# ④ 入口房间
	var entrance := ""
	for row: Resource in db.rows("dungeon_room"):
		if str(row.scene_id) == scene_id and str(row.room_type) == "entrance":
			entrance = str(row.room_id)
			break
	if not entrance.is_empty():
		var point: Variant = _room_spawn_point(entrance)
		if point != null:
			return point
		var room: Node2D = world.get_node_or_null("Rooms/Room_%s" % entrance)
		if room != null:
			return room.position
	# ⑤ 出口位点（地图既没有出生点标记也没有入口房间时兜底）
	var exit: Node2D = world.get_node_or_null("Markers/Exit_%s" % scene_id)
	if exit != null:
		return exit.position
	return Vector2(512, 384)


## 这个房间 id 是不是本图的（`dungeon_room.scene_id`）
func _room_belongs_here(room_id: String) -> bool:
	var row: Resource = db.get_row("dungeon_room", room_id)
	return row != null and str(row.scene_id) == scene_id


## 建筑与设施的**可见标识**：场景里 `Markers/Buildings/*` 与 `Markers/Facilities/*` 都是
## 看不见的 Marker2D，玩家看不出哪栋是杂货铺、哪栋是铁匠铺，只能挨个走过去按 E 试
## （城镇没有店招美术）。名字这类「运营信息」由代码补上——和触发点／敌人用 Label 标名字
## 是同一套占位约定；地编以后给了店招贴图，把这里换成贴图即可。
## 建筑名取自 `building_def.name_cn`（表），设施名在本文件的 `FACILITY_LABELS`。
func _build_place_labels() -> void:
	for group: String in ["Markers/Buildings", "Markers/Facilities"]:
		var box := world.get_node_or_null(group) if world != null else null
		if box == null:
			continue
		for child in box.get_children():
			if not (child is Node2D):
				continue
			var place_id := str(child.name)
			var text := ""
			if group.ends_with("Buildings"):
				var row: Resource = db.get_row("building_def", place_id)
				if row != null:
					text = str(row.name_cn)
			else:
				text = str(FACILITY_LABELS.get(place_id, ""))
			if text.is_empty():
				continue
			var label := Label.new()
			label.name = "PlaceLabel_%s" % place_id
			# 店名本身就说明「这儿能进」——HUD 已经写了「交互 E」，这里不再重复
			label.text = text
			label.position = Vector2(-28, -48)
			label.add_theme_font_size_override("font_size", 12)
			label.add_theme_color_override("font_color", Color(1, 1, 1, 0.92))
			label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.75))
			(child as Node2D).add_child(label)
	# 出口：走到 `Exit_<scene_id>` 上就回大地图（Esc 也能走），但那是个看不见的坐标——
	# 标一个「出口」出来，玩家才知道「门口」在哪。位置可能挂在房间子节点下，所以递归找。
	for marker: Node2D in _collect_exit_markers():
		var exit_label := Label.new()
		exit_label.name = "PlaceLabel_Exit"
		exit_label.text = "出口"
		exit_label.position = Vector2(-18, -40)
		exit_label.add_theme_font_size_override("font_size", 12)
		exit_label.add_theme_color_override("font_color", Color(0.98, 0.9, 0.6, 0.95))
		exit_label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.75))
		marker.add_child(exit_label)


func _collect_exit_markers() -> Array:
	var out: Array = []
	var markers := world.get_node_or_null("Markers") if world != null else null
	if markers != null:
		_collect_exit_markers_in(markers, out)
	return out


func _collect_exit_markers_in(node: Node, out: Array) -> void:
	for child in node.get_children():
		if child is Node2D and str(child.name).begins_with("Exit_"):
			out.append(child)
		_collect_exit_markers_in(child, out)


## 房间里的落点：**bounds 矩形中心**，找不到才退回房间节点位置。
## 房间节点位置是矩形左上角（地编的约定），站在角上会被墙碰撞推出去，
## `current_room_id()` 也就认不到这个房间——完成度里的「进过隐藏房间」会漏计。
## 找不到房间返回 null（调用方自己决定退回哪）。
func _room_spawn_point(room_id: String) -> Variant:
	var room: Node2D = world.get_node_or_null("Rooms/Room_%s" % room_id)
	if room == null:
		return null
	var bounds := room.get_node_or_null("bounds") as Area2D
	if bounds != null:
		var rect := _bounds_rect(bounds)
		if rect.size != Vector2.ZERO:
			return rect.get_center()
	return room.position


## 房间敌人：按 dungeon_room.enemy_team + Enemies/<room_id>/Team_<team_id> 位点生成
func _spawn_teams() -> void:
	var cleared: Array = local_state().get("cleared", [])
	for row: Resource in db.rows("dungeon_room"):
		if str(row.scene_id) != scene_id:
			continue
		var team_id := str(row.enemy_team)
		if team_id.is_empty():
			continue
		var room_id := str(row.room_id)
		var marker: Node2D = world.get_node_or_null("Enemies/%s/Team_%s" % [room_id, team_id])
		if marker == null:
			push_error("[LocalMap] %s 缺少敌人位点 %s/%s" % [scene_id, room_id, team_id])
			continue
		var team: Resource = db.get_row("enemy_team", team_id)
		var room_type := str(row.room_type)
		var enemy = RoamingEnemyScript.new()
		enemy.name = "RoomTeam_%s" % room_id
		enemy.position = marker.position
		enemy.setup(db, {
			"spawn_id": room_id,
			"source_scene": scene_id,
			"source_key": room_id,
			"team_id": team_id,
			"behavior": "idle",          # 副本内不追击，原地驻守
			"alert_radius": 0.0,
			"chase_speed": 0.0,
			"respawn_sec": 0,
			"is_elite": room_type == "elite" or room_type == "boss",
		}, team, player, null)
		enemy.sneak_detect_reduce = sneak_detect_reduce()
		enemy.encountered.connect(_on_encountered)
		_add(ysort_node(), enemy)
		teams.append(enemy)
		if cleared.has(room_id):
			enemy.mark_defeated(0)


func _spawn_chests() -> void:
	var opened: Array = local_state().get("chests", [])
	for row: Resource in db.rows("dungeon_room"):
		if str(row.scene_id) != scene_id:
			continue
		var group_id := str(row.chest_id)
		if group_id.is_empty():
			continue
		var key := "%s|%s" % [row.room_id, group_id]
		var marker: Node2D = world.get_node_or_null("Markers/%s/Chest_%s" % [row.room_id, group_id])
		if marker == null:
			push_error("[LocalMap] %s 缺少宝箱位点 %s/%s" % [scene_id, row.room_id, group_id])
			continue
		var chest = ChestScript.new()
		chest.name = "Chest_%s" % key
		chest.position = marker.position
		chest.setup(group_id, player, opened.has(key))
		_add(ysort_node(), chest)
		chests.append(chest)
		chest.set_meta("key", key)


func _spawn_triggers() -> void:
	var used: Array = local_state().get("triggers", [])
	# 按**地图里的位点**生成：同一个触发可能有多个位点（酒葫芦在牢房深处与后山地牢各一处）
	var markers := _find_all_trigger_markers()
	for marker: Node2D in markers:
		# 位点名 → (触发 id, 顺序编号)：顺序谜题三个编号位点共用一个 `hidden_trigger` 行
		# （`Trigger_trig_brazier_1/2/3`），其余类型就是「点名的 id」。
		var parsed := _parse_trigger_marker(str(marker.name))
		var trigger_id := str(parsed["trigger_id"])
		var row: Resource = db.get_row("hidden_trigger", trigger_id)
		if row == null or str(row.scene_id) != scene_id:
			continue
		var point = TriggerPointScript.new()
		point.name = "Trigger_%s_%d" % [trigger_id, triggers.size()]
		point.position = marker.position
		# 一次性触发点（once_only=1）看**永久记录**：离图再回来不该能重复拿奖励，
		# 其余点位只按这次进图的会话状态判（回大地图整片刷新）
		var already := used.has(trigger_id)
		if bool(row.once_only):
			var state = current_state()
			if state != null and Array(state.dungeon_record(scene_id).get("triggers", [])).has(trigger_id):
				already = true
		point.setup(row, player, already, db, int(parsed["index"]))
		_add(ysort_node(), point)
		triggers.append(point)


## 位点名 → `{trigger_id, index}`。
##
## 约定（写进 07「地图资源需求」）：顺序谜题的三个位点叫 `Trigger_trig_brazier_1/2/3`，
## 表里只有 `trig_brazier` 一行；这里先把整串当 id 查表，查不到再剥掉末尾的 `_<数字>` 重试。
## 既不影响现有的「一个触发一个位点」（含同名多位点，如酒葫芦两处），又给顺序谜题留了编号位。
func _parse_trigger_marker(marker_name: String) -> Dictionary:
	var raw := marker_name.substr("Trigger_".length())
	if db.get_row("hidden_trigger", raw) != null:
		return {"trigger_id": raw, "index": 0}
	var cut := raw.rfind("_")
	if cut > 0:
		var suffix := raw.substr(cut + 1)
		var head := raw.substr(0, cut)
		if suffix.is_valid_int() and db.get_row("hidden_trigger", head) != null:
			return {"trigger_id": head, "index": int(suffix)}
	return {"trigger_id": raw, "index": 0}


## 存档设施：注入优先，否则用默认目录
func save_service():
	if _save_service == null:
		var store = save_store_override
		if store == null:
			store = SaveServiceScript.make_default()
		_save_service = SaveServiceScript.new(store, current_state())
	return _save_service


## 自动存档（副本内的进度点调用）。返回 {ok, ...}
func autosave(reason: String) -> Dictionary:
	var result: Dictionary = save_service().save(reason, true)
	# **自动存档失败要出声**：不然玩家以为「自动存档」把进度保住了，实际上这一段只在本局里。
	# 失败不打断游玩（AGENTS：存档写入失败不允许挡住开局），但状态栏必须如实说一句。
	if not bool(result.get("ok", false)):
		_set_status("%s　｜　自动存档失败：%s（这一段的进度只在本局里）" % [
			_status.text if _status != null and not str(_status.text).is_empty() else reason,
			str(result.get("error", "未知原因")),
		])
	return result


## 手动存档：只有城镇是存档点（设计 02「城镇：存档点，可自由存读」）
func can_save_here() -> bool:
	var row: Resource = db.get_row("map_local", scene_id)
	return row != null and str(row.scene_type) == "town"


func press_save() -> Dictionary:
	if not can_save_here():
		_set_status("这里不能手动存档：城镇才是存档点（副本里的进度会自动保存）")
		return {"ok": false, "error": "not_a_town"}
	var result: Dictionary = save_service().save("城镇存档点")
	_set_status(save_service().describe())
	return result


## 潜行时「被发现」判定打折的比例（combat_const.sneak_detect_reduce）
func sneak_detect_reduce() -> float:
	var row: Resource = db.get_row("combat_const", "sneak_detect_reduce")
	return float(row.value) if row != null else 0.4


## 潜行状态变了就给一句提示
func _track_sneak() -> void:
	if player == null:
		return
	var sneaking := bool(player.get("sneaking"))
	if sneaking == _was_sneaking:
		return
	_was_sneaking = sneaking
	if sneaking:
		_set_status("潜行中：移速 60%%，被发现判定 ×%.2f（被守卫撞上就算潜行失败）" % (1.0 - sneak_detect_reduce()))
	else:
		_refresh_status()


## 收集事件判定位点（`Event_<check_id>`，可能挂在房间子节点下）
func _collect_events() -> void:
	events = []
	if world == null:
		return
	for check: Resource in event_service().checks_for_scene(scene_id):
		var check_id := str(check.check_id)
		for node in _find_markers(world.get_node_or_null("Markers"), "Event_%s" % check_id):
			events.append({"check_id": check_id, "node": node, "position": node.global_position})
	# 再按**地图上真的摆了哪些位点**补一遍：表里只填了 region_id（`scene_id` 空）的判定，
	# 只要位点在这张图里就该能按 E——07 文档把赌局摆在清风驿·客栈，而表里 `ev_gamble` 只有 region_id，
	# 以前这条路只认 `scene_id`，于是镇上那个位点永远没人接（地图验收还专门为它开了个例外）。
	# 判据交给 `check_allowed_in_scene()`：scene_id 命中，或者 region_id 正好是这张图的父节点。
	for node in _find_all_marker_nodes(world.get_node_or_null("Markers"), "Event_"):
		var marker_check_id := str(node.name).trim_prefix("Event_")
		if marker_check_id.is_empty() or _has_event(marker_check_id):
			continue
		if not event_service().check_allowed_in_scene(marker_check_id, scene_id):
			continue
		events.append({"check_id": marker_check_id, "node": node, "position": node.global_position})


## 这张图的 events 里已经有这条判定了吗（避免同一个位点被两种来源各收一次）
func _has_event(check_id: String) -> bool:
	for entry: Dictionary in events:
		if str(entry["check_id"]) == check_id:
			return true
	return false


## 递归找所有名字以 prefix 开头的 Marker（返回节点本身，名字里带的是 check_id）
func _find_all_marker_nodes(root: Node, prefix: String) -> Array:
	var out: Array = []
	if root == null:
		return out
	for child in root.get_children():
		if child is Node2D and str(child.name).begins_with(prefix):
			out.append(child)
		out.append_array(_find_all_marker_nodes(child, prefix))
	return out


## 递归找某个名字的 Marker（大地图与小地图都可能有嵌套）
func _find_markers(root: Node, node_name: String) -> Array:
	var out: Array = []
	if root == null:
		return out
	for child in root.get_children():
		if str(child.name) == node_name:
			out.append(child)
		out.append_array(_find_markers(child, node_name))
	return out


func event_service():
	if _event_service == null:
		_event_service = EventCheckServiceScript.new(db, current_state())
	return _event_service


## 玩家身边的事件位点（最近的那个 check_id）
func event_near_player() -> String:
	if player == null:
		return ""
	var best := ""
	var best_distance := EVENT_DISTANCE
	for entry: Dictionary in events:
		var distance: float = player.global_position.distance_to(entry["position"])
		if distance <= best_distance:
			best = str(entry["check_id"])
			best_distance = distance
	return best


## 收集观察点位点（`Observe_<point_id>`，与判定位点一样可能挂在房间下）。
##
## 只收**这张图**的行（`scene_id` 命中）；`region_id` 那几条在大地图上，由
## `overworld_controller` 收——同一行不会两边都生效（表里二选一）。
func _collect_flavor_points() -> void:
	flavor_points = []
	if world == null:
		return
	for row: Resource in db.rows("flavor_point"):
		if str(row.scene_id) != scene_id:
			continue
		var point_id := str(row.point_id)
		for node in _find_markers(
			world.get_node_or_null("Markers"), FlavorPointScript.marker_name_of(point_id)
		):
			# **看得见**：观察点本身只是一根 Marker2D，挂一枚小暖色菱形（决策 337）。
			# 幂等——重复 setup() 不会挂第二枚（名字撞了 Godot 会自动改名，用例按名字找会出错）。
			if node.get_node_or_null(FlavorMarkerScript.NODE_NAME) == null:
				node.add_child(FlavorMarkerScript.new())
			flavor_points.append({"point_id": point_id, "node": node, "position": node.global_position})


## 玩家身边的观察点（最近的那个 point_id；没有就空串）
func flavor_near_player() -> String:
	if player == null:
		return ""
	var best := ""
	var best_distance := EVENT_DISTANCE
	for entry: Dictionary in flavor_points:
		var distance: float = player.global_position.distance_to(entry["position"])
		if distance <= best_distance:
			best = str(entry["point_id"])
			best_distance = distance
	return best


## 看一个观察点：**只出一句碎句**——不发奖励、不锁任何路（设计 20 §3.2），也不进线索本与副本完成度。
##
## **0.32.0 补**：读过顺手记一枚 `flag_obs_<point_id>`——幕二那条「免战」选项的前置
## 就写 `flag_obs_ob_luoyanpo_cart_01`（「看过的观察点」由此能被别的判定引用）。
## 之前全项目 0 处 `flag_obs_*`，那条前置是死的。旗标名按 `point_id` 拼，不逐条抄。
func read_flavor_point(point_id: String) -> Dictionary:
	var row: Resource = db.get_row("flavor_point", point_id)
	if row == null:
		return {"ok": false, "error": "没有这个观察点"}
	var text := str(row.text_cn)
	_set_status(text)
	var state = current_state()
	if state != null:
		state.set_flag("flag_obs_%s" % point_id)
		autosave("观察点")
	return {"ok": true, "point_id": point_id, "text": text}


## 走一次事件判定（E 交互）：判定 → 奖励／失败说明，结果写存档
func resolve_event(check_id: String) -> Dictionary:
	var result: Dictionary = event_service().resolve(check_id)
	_set_status(str(result["text"]))
	autosave("事件判定")
	var reward: Dictionary = result.get("reward", {})
	if bool(result["success"]) and str(reward.get("type", "")) == "room":
		# 小地图里的「找到通往某房间的路」直接把人带过去
		var room: Node2D = world.get_node_or_null("Rooms/Room_%s" % str(reward.get("id", "")))
		if room != null and player != null:
			player.global_position = room.position
			_set_status("%s（已带你过去）" % str(result["text"]))
	elif bool(result["success"]) and bool(reward.get("start_battle", false)):
		# `reward_type=boss`：判定通过 → 当场开战。语义与本文件的 `_trigger_item`（隐藏 Boss）一致，
		# 所以共用同一个建队函数；`pending_return_scene` 等回程信息由 `_start_battle` 一并写好。
		var boss_encounter = _boss_encounter(str(reward.get("id", "")), check_id)
		if boss_encounter != null:
			_start_battle(boss_encounter)
	elif bool(result["success"]):
		# 判定过了接一段对话（设计 20 §四 幕四）：配在 `EVENT_DIALOGUES` 里的才有，
		# 开不开得出还得看表里那条 `condition`（同一个判定位点在不同阶段说不同的话）。
		_open_event_dialogue(check_id)
	return result


## 找出场景里所有 Trigger_ 位点（含挂在各房间下的）
func _find_all_trigger_markers() -> Array:
	var markers: Node2D = world.get_node_or_null("Markers")
	if markers == null:
		return []
	var out: Array = []
	_collect_trigger_markers(markers, out)
	return out


func _collect_trigger_markers(node: Node, out: Array) -> void:
	for child in node.get_children():
		if child is Node2D and str(child.name).begins_with("Trigger_"):
			out.append(child)
		_collect_trigger_markers(child, out)


## 收集城镇 NPC 站位（`Characters/npc_slot_0N`）。地编交付的是 Marker2D + 一张占位贴图；
## 代码这边只记引用：交互入口是 `npc_near_player()`，摆放校验入口是
## `verify_maps._check_npc_slots()`（命名 / 没卡墙 / 从出生点走得到）。
##
## 命名**两种都收**（07 §九 第 12 条）：占位 `npc_slot_0N` 与按 id 绑的 `npc_<npc_id>`
## ——地编改名之后这里不用跟着改（见 `框架说明.md` 决策 332）。
func _collect_npc_slots() -> void:
	npcs = []
	var box: Node = world.get_node_or_null("Characters") if world != null else null
	if box == null:
		return
	for child in box.get_children():
		if child is Node2D and str(child.name).begins_with(NpcServiceScript.SLOT_PREFIX):
			npcs.append(child)


func ysort_node() -> Node2D:
	return world.get_node_or_null("YSort")


func _add(parent: Node, node: Node) -> void:
	if parent != null:
		parent.add_child(node)
	else:
		world.add_child(node)


# ------------------------------------------------------------------ 交互

func _unhandled_input(event: InputEvent) -> void:
	# 浮层栈（设计 18.1）：**全局快捷键在任何浮层里都可用**——
	# 所以这里先认快捷键与 Esc，只有它们都不吃这一下时，才轮到场景层的交互。
	if event.is_action_pressed("ui_cancel"):
		# Esc 弹一层；栈空才回场景（回大地图）
		if close_top_overlay():
			return
		leave_to_overworld()
		return
	if event.is_action_pressed("open_character"):
		open_character_overlay(0)
		return
	if event.is_action_pressed("open_bag"):
		open_character_overlay(2)
		return
	if event.is_action_pressed("show_progress"):
		open_dungeon_panel()
		return
	if event.is_action_pressed("show_clues"):
		open_clues()
		return
	# 看 NPC 信息（设计 0.28.0 的 Q62）：**E 是交互菜单，Q 才是只读信息**——
	# 「我只是想看看这人是谁」不该经过一次会改变世界状态的交互。
	if event.is_action_pressed("npc_info"):
		var who := npc_near_player()
		# **失败也要出声**（2026-10-04 实测反馈：玩家按 Q 觉得"没反应"）——
		# 以前这两条失败路径都是静默 `return`：附近没人、或这个位点还没绑到 `npc_def` 行。
		if who == null:
			_set_status("这附近没有可以看的人（Q 是看人，E 才是搭话）")
			return
		var info_id := _npc_for_slot(str(who.name))
		if info_id.is_empty():
			_set_status("这个人还没配信息（位点 %s 没绑到表里）" % str(who.name))
			return
		open_npc(info_id, NpcPanelMode.MODE_INFO)
		return
	# 下面这些是**场景层**的：有浮层开着就不许透传（E 不该在商店里再触发一次交互）
	if overlays().depth() > 0:
		return
	if event.is_action_pressed("interact"):
		interact()
	elif event.is_action_pressed("sweep"):
		sweep_floor(-1)
	elif event.is_action_pressed("save_game"):
		press_save()


# ------------------------------------------------------------------ 浮层栈

func overlays():
	if _overlays == null:
		_overlays = OverlayStackScript.new()
	return _overlays


## 压栈并关掉「被挤出栈」的那些浮层（弹回已有的 / 挤掉栈底）。
func _push_overlay(id: String) -> Array:
	var closed: Array = overlays().push(id)
	for closed_id: String in closed:
		_free_overlay(closed_id)
	return closed


## Esc：弹掉栈顶那一层。栈空返回 false（由调用方做场景层的事）。
func close_top_overlay() -> bool:
	var top_id: String = overlays().pop()
	if top_id.is_empty():
		return false
	_free_overlay(top_id)
	return true


## 程序化关一个浮层（界面自己的「返回」按钮走这里）。
func _close_overlay(id: String) -> void:
	# 关掉它时压在它上面的也要跟着走，否则栈里会留下已经不在屏幕上的幽灵
	for extra: String in overlays().remove(id):
		_free_overlay(extra)
	_free_overlay(id)
	# **关掉浮层后重算一次引导**（2026-10-04，决策 347）：浮层里的动作会改状态——最典型的是
	# 「在店里买齐回血道具」：`flag_supplies_ready` 的口径是「等级 ≥ 5 ＋ 背包里有消耗品」，
	# 而它原本只在**换图／换房间**时算。玩家正站在药铺里把药买齐，HUD 却还停在「把家伙和药备齐」，
	# 要出镇再进来才推进。挂在关浮层这一下，正好落在「刚做完那件事」的时刻。
	GuideServiceScript.refresh_derived_flags(db, current_state())
	_refresh_guide()


## 按 id 释放界面节点。**只碰界面**，不碰栈——栈的账在调用方那几行里。
func _free_overlay(id: String) -> void:
	match id:
		"shop":
			if _panel_open(shop_panel):
				shop_panel.queue_free()
			shop_panel = null
		"cultivate":
			if _panel_open(cultivate_panel):
				cultivate_panel.queue_free()
			cultivate_panel = null
		"clue":
			if _panel_open(clue_panel):
				clue_panel.queue_free()
			clue_panel = null
		"dungeon":
			if _panel_open(dungeon_panel):
				dungeon_panel.queue_free()
			dungeon_panel = null
		"character":
			var panel := _overlay_node("character")
			if panel != null:
				panel.queue_free()
		"npc":
			if _panel_open(npc_panel):
				npc_panel.queue_free()
			npc_panel = null
		_:
			pass


## 浮层节点（角色面板不在专用字段里，按节点名找）。
func _overlay_node(id: String) -> Node:
	match id:
		"character":
			return find_child("CharacterPanel", true, false)
		_:
			return null


## 角色与行囊（设计 18.1：`Tab` 开角色、`I` 直达行囊页签）。
##
## 它本身是**浮层**（压在场景上），不再是切场景——这样「商店 → 角色 → Esc 回商店」
## 这条链才成立（切场景会把商店那一层丢掉）。
func open_character_overlay(tab_index: int = 0) -> Dictionary:
	if overlays().has("character"):
		# 已在栈里 → 弹回它 + **重读状态**（设计 18.1：不复用上次快照）
		_push_overlay("character")
		var existing := _overlay_node("character")
		if existing != null:
			existing.state_override = current_state()
			existing.refresh()
			existing.select_tab(tab_index)
		return {"ok": true, "error": "", "reopened": true}
	var panel = load(CHARACTER_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "角色界面场景加载失败"}
	panel.name = "CharacterPanel"
	panel.state_override = current_state()
	panel.back_handler = func() -> void: _close_overlay("character")
	add_child(panel)
	panel.setup()
	panel.select_tab(tab_index)
	_push_overlay("character")
	return {"ok": true, "error": "", "reopened": false}


## 开完成度界面（M）：四项完成度 + 楼层列表，已通关的层可以在界面里直接扫荡
func open_dungeon_panel() -> Dictionary:
	if not has_completion():
		_set_status("这里不是副本（城镇没有完成度）")
		return {"ok": false, "error": "not_dungeon"}
	if _panel_open(dungeon_panel):
		# 已在栈里 → 弹回它（关掉压在它上面的）并按设计重读状态
		_push_overlay("dungeon")
		dungeon_panel.state_override = current_state()
		dungeon_panel.refresh()
		return {"ok": true, "error": "", "reopened": true}
	var panel = load(DUNGEON_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "完成度场景加载失败"}
	panel.name = "DungeonPanel"
	panel.state_override = current_state()
	panel.scene_id = scene_id
	panel.return_handler = func() -> void: close_dungeon_panel()
	# 扫荡还是走小地图控制器（它管会话状态、玩家位置与掉落入包）
	panel.sweep_handler = func(floor_number: int) -> Dictionary: return sweep_floor(floor_number)
	add_child(panel)
	panel.setup()
	dungeon_panel = panel
	_push_overlay("dungeon")
	return {"ok": true, "error": "", "reopened": false}


func close_dungeon_panel() -> void:
	_close_overlay("dungeon")


## 开线索本：本图的隐藏内容与事件判定，每条都带线索来源与完成状态
func open_clues() -> Dictionary:
	if _panel_open(clue_panel):
		_push_overlay("clue")
		clue_panel.state_override = current_state()
		clue_panel.refresh()
		return {"ok": true, "error": "", "reopened": true}
	var panel = load(CLUE_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "线索本场景加载失败"}
	panel.name = "CluePanel"
	panel.state_override = current_state()
	panel.scope = "scene"
	panel.scene_id = scene_id
	panel.return_handler = func() -> void: close_clues()
	add_child(panel)
	panel.setup()
	clue_panel = panel
	_push_overlay("clue")
	return {"ok": true, "error": "", "reopened": false}


func close_clues() -> void:
	_close_overlay("clue")


## 扫荡已通关的层。floor_number 传 -1 表示「玩家所在层，拿不到就挑第一个可扫荡的层」。
## 设计：已通关层可扫荡，只结算掉落（不给经验）。
func sweep_floor(floor_number: int = -1) -> Dictionary:
	if not has_completion():
		_set_status("这里不是副本，不能扫荡")
		return {"ok": false, "error": "not_dungeon"}
	var service = dungeon_service()
	var target := floor_number
	if target < 0:
		var room_id := current_room_id()
		var row: Resource = _room_row(room_id)
		if row != null:
			target = int(row.floor)
		var sweepable: Array = service.sweepable_floors(scene_id)
		if not sweepable.has(target):
			# 站在没通关的层上：退而用第一个能扫的层
			target = int(sweepable[0]) if not sweepable.is_empty() else -1
	if target < 0:
		_set_status("还没有通关的层可以扫荡（先把这一层的房间清干净）")
		return {"ok": false, "error": "no_sweepable_floor"}
	var result: Dictionary = service.sweep(scene_id, target)
	if bool(result["ok"]):
		var applied: Dictionary = result["applied"]
		_set_status("扫荡第 %d 层：经验不加（仅掉落）　%s　物品 %d 种　装备 %d 件" % [
			target, str(result["summary"]),
			Array(applied["items"]).size(), Array(applied["equipment"]).size(),
		])
	else:
		_set_status("扫荡失败：%s" % str(result["error"]))
	if bool(result["ok"]):
		autosave("扫荡第 %d 层" % target)
	return result


## 交互：宝箱 > 触发点 > 事件判定 > 店铺 > 无
func interact() -> Dictionary:
	# 剧情招募（09 §3.2）在**最前面**：条件旗标满足且人在这张图时，按 E 先把同伴收进来——
	# 它常常正是上一次交互（比如悬赏板发 `flag_board_read`）的结果，所以必须排在别的交互之前。
	# 城镇走「客栈那一次」：设计原文是「看过告示板后，在清风驿客栈对话」，所以城镇不在任意 E 上收人。
	if not _is_town():
		var recruited := _join_pending_recruits(true)
		if not recruited.is_empty():
			return {"ok": true, "recruited": recruited}
	for chest in chests:
		if chest.can_interact():
			return _try_open_chest(chest)
	for point in triggers:
		if point.can_interact():
			return _activate_trigger(point)
	var check_id := event_near_player()
	if not check_id.is_empty():
		return resolve_event(check_id)
	var building_id := building_near_player()
	if not building_id.is_empty():
		# service 类建筑各有各的入口：医馆的治疗在店铺界面里；木桩（09 §3.3）直接进练习战
		if PracticeServiceScript.is_practice_building(db, building_id):
			return start_practice(building_id)
		return open_shop(building_id)
	var facility := facility_near_player()
	if facility == "facility_inn":
		# 设计 09 §3.2：城镇里的同伴是在**客栈**这一次交互上入队的（现例：燕小七）。
		var joined_at_inn := _join_pending_recruits(true)
		if not joined_at_inn.is_empty():
			# 设计 20 §四 幕一「客栈里的绿林客」：这一次对话就是她的招募对话，
			# 入队之后把对话摆出来（台词与选项都在表里；没配对话的人只报入队）。
			var speaker := _first_joined_with_dialogue(joined_at_inn)
			if not speaker.is_empty():
				open_npc(speaker)
			return {"ok": true, "recruited": joined_at_inn}
		return open_cultivate()
	# 当铺与悬赏板在 07 文档里是「无表的信息源」：按 E 读一段对话/告示，不开功能界面。
	# 悬赏委托本身按 06 文档已移出初步开发阶段（quest_board.csv 不做），所以这里只给线索。
	if facility == "facility_pawnshop":
		return _read_facility_notice(facility, "当铺老板", "寨里关着个疯子，别去招惹。")
	if facility == "facility_bounty_board":
		# 09 §3.1 的第一步就是「去清风驿的告示板看看」：清风驿里能读的告示就是这块悬赏板，
		# 读它＝看过告示板 → 点亮引导旗标（下一步是「到客栈找燕小七」）。
		var state = current_state()
		if state != null and not state.has_flag(FLAG_BOARD_READ):
			state.set_flag(FLAG_BOARD_READ)
		_refresh_guide()
		var posted: Dictionary = _read_facility_notice(
			facility, "悬赏板", "沈家小姐在驿外被掳，黑风寨脱不了干系。告示只指了方向，没有坐标。"
		)
		# 序幕·择念（20 §3.1／0.29.1 v2 第 4 条）：**与引子同框**——第一次读到告示，
		# 主角心里那个问题就摆出来（条件在表里，选过就不再问）。
		if _open_story_dialogue(OPENING_CHOICE_NODE):
			posted["story"] = OPENING_CHOICE_NODE
		return posted
	if facility == "facility_bookshop":
		# 07 §4.1（0.31.0 加）：书铺是**无表信息点**，21 §九 陆文昭的「本命机遇·书箱底」落在这里。
		# 台词只描述设计原文点名的三样陈设（书架／账桌／抄书的纸笔）＋那只书箱，不编新剧情——
		# 机遇本身由 `story_node(opp_scholar)` 按「地点 ＋ 出身」发，不靠这一按。
		var notice: Dictionary = _read_facility_notice(
			facility, "书铺掌柜", "架上的旧书摞到房梁，抄书的纸笔摊了一桌；书架底下还塞着只旧书箱。"
		)
		# **这一按顺手再判一次剧情节点**：机遇的条件是「看过悬赏板 ＋ 主角是书生」，而那条旗标
		# 就是在**同一个城镇里**读悬赏板时点亮的——只在进图／换房间时判，玩家读完板走到书铺会扑空，
		# 得先出镇再进来才发（21 §九 的机遇地点写的就是「清风驿·书铺」）。判在书铺这一按上，
		# 拿到手的那一刻正好站在它写的地方（决策 345 的续：把「领取点」钉到设计写的那件事上）。
		_claim_story_nodes()
		return notice
	if not facility.is_empty():
		# 07 §4.1 的无表设施都在上面各有一支（客栈／当铺／悬赏板／书铺）；认不出来的位点 = 地图／数据错。
		# **别把位点名（facility_xxx）印给玩家**：出声到日志，界面上说人话。
		push_error("[LocalMap] 认不出的设施位点：%s（07 只定义了 %s）" % [
			facility, "、".join(FACILITY_LABELS.keys())])
		_set_status("这里还没有可交互的内容（数据错，已记进日志）")
		return {"ok": false, "error": "facility_unsupported", "facility": facility}
	# NPC 站位放在**最后**：站在店门口的路人不该把「进店」抢掉（店铺/设施优先）
	var npc := npc_near_player()
	if npc != null:
		return _talk_to_npc(npc)
	# 观察点排在 NPC 之后：它是「看一眼」的物，不该抢掉「跟人说话」。
	var point_id := flavor_near_player()
	if not point_id.is_empty():
		return read_flavor_point(point_id)
	# 别说「走 Exit 位点」——那是条看不见的坐标；玩家能按的键写清楚
	_set_status("这里没有可交互的东西（按 Esc 回大地图）")
	return {"ok": false, "error": "无可交互目标"}


## 无表设施的信息点：把设计文档里的那两句对话/告示落到状态栏。
## 这次刚入队的人里，谁配了对话（设计 20 §四 的幕一就是「客栈里那一次」）。
## 没有就返回空串——调用方只报入队，不硬凑一段对话。
func _first_joined_with_dialogue(results: Array) -> String:
	for entry: Dictionary in results:
		if not bool(entry.get("ok", false)):
			continue
		var char_id := str(entry.get("char_id", ""))
		if DialogueServiceScript.has_dialogue(db, char_id):
			return char_id
	return ""


## 无表设施的信息点：把设计文档里的那两句对话/告示落到状态栏。
func _read_facility_notice(facility_id: String, speaker: String, text: String) -> Dictionary:
	_set_status("%s：%s" % [speaker, text])
	return {"ok": true, "facility": facility_id, "speaker": speaker, "text": text}


## NPC 站位交互：内容还没到，但**不静默**——玩家按了 E 要有一句如实的回应。
## 等 `dialogue_tree.csv` 到了，把这里换成「按 NPC id 取台词／对话树」，调用方（`interact()`）不用动。
func _talk_to_npc(slot: Node2D) -> Dictionary:
	var slot_id := str(slot.name)
	# 设计 19：城镇 NPC 从「按 E 一句占位台词」升级成**可以交往的人**。
	# 位点与人的对应：**按 id 绑的 `npc_<npc_id>` 优先，占位命名 `npc_slot_0N` 按编号顺序**
	# （判定只有一处：`NpcService.npc_for_slot`）。07 §九 第 12 条那句「等 NPC／对话表出来后再按 id
	# 绑定」的前置条件已经满足——地编改名当天代码不用动（`框架说明.md` 决策 332）。
	var npc_id := _npc_for_slot(slot_id)
	if npc_id.is_empty():
		# **别把表名甩给玩家**：这句以前写「（这个人还没有配 npc_def 行）」，而 `npc_def` 正是
		# `CopyGuard` 盯的表内 id 形态（AGENTS 硬规矩）。表名只进日志，玩家看一句人话（决策 329）。
		push_error("[local_map] NPC 站位 %s 绑不到人（按 id 认不出来／按编号顺序越界；本场景只有 %d 个人）"
				% [slot_id, NpcServiceScript.npcs_at(db, scene_id).size()])
		var text := "眼下没什么可说的（这个人还没配台词）"
		_set_status("%s：%s" % [NPC_SPEAKER, text])
		return {"ok": true, "npc": slot_id, "speaker": NPC_SPEAKER, "text": text}
	var opened := open_npc(npc_id)
	var def: Resource = NpcServiceScript.def_of(db, npc_id)
	return {
		"ok": bool(opened.get("ok", false)), "npc": slot_id, "npc_id": npc_id,
		"speaker": str(def.name_cn) if def != null else npc_id,
		"text": str(def.greet_text_cn) if def != null else "",
	}


## 位点 → 人：**两种命名都认**，判定只有一处（`NpcService.npc_for_slot`）——
## 按 id 绑的 `npc_<npc_id>` 优先（认不出来就**不绑**，不退回按顺序），
## 占位命名 `npc_slot_0N` 按编号顺序对上本场景 `npc_def` 的行序。
func _npc_for_slot(slot_id: String) -> String:
	return NpcServiceScript.npc_for_slot(db, slot_id, scene_id)


## 开 NPC 交往面板（设计 19 §三）：信息在最上面，交互项在下面。
## `mode`：E 打开的是交互菜单、Q 打开的是只读信息（设计 0.28.0 的 Q62）。
## `entry_node_id`：**直接从某一句说起**（剧情抉择用，见 `_open_pending_choice_dialogue`）——
## 空值走 `entry_node` 的默认入口（按 `sort_order` 取第一条条件满足的台词）。
func open_npc(npc_id: String, mode: String = NpcPanelMode.MODE_INTERACT,
		entry_node_id: String = "") -> Dictionary:
	if _panel_open(npc_panel):
		_push_overlay("npc")
		npc_panel.state_override = current_state()
		npc_panel.npc_id = npc_id
		npc_panel.mode = mode
		npc_panel.dialogue_node_id = entry_node_id
		npc_panel.refresh()
		return {"ok": true, "error": "", "reopened": true}
	var panel = load(NPC_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "NPC 面板加载失败"}
	panel.name = "NpcPanel"
	panel.state_override = current_state()
	panel.npc_id = npc_id
	panel.mode = mode
	panel.dialogue_node_id = entry_node_id
	panel.kill_styles = _last_kill_styles()
	panel.return_handler = func() -> void: close_npc()
	panel.spar_handler = func(who: String, team: String) -> void: _start_spar(who, team)
	add_child(panel)
	panel.setup()
	npc_panel = panel
	_push_overlay("npc")
	return {"ok": true, "error": "", "reopened": false}


func close_npc() -> void:
	_close_overlay("npc")


## 切磋：组一场「和这个人打」的遭遇交给战斗场景；赢了由战斗结算加好感
## （`encounter.spar_npc` 就是干这个的——切过场景之后会话里的临时标记早没了）。
func _start_spar(npc_id: String, team_id: String) -> Dictionary:
	var team: Resource = db.get_row("enemy_team", team_id)
	if team == null:
		# 数据错：id 只进日志，玩家看一句人话（AGENTS：玩家可见文案不许出现表内 id，见决策 242／329）
		push_error("[local_map] 切磋队伍不存在：enemy_team 缺少 %s（npc %s 的 spar_team_id）" % [team_id, npc_id])
		_set_status("切磋对手的配置对不上（数据错，已记进日志）")
		return {"ok": false, "error": "no_team"}
	var encounter = EncounterScript.build(db, {
		"spawn_id": "spar_%s" % npc_id,
		"source_scene": scene_id,
		"source_key": "spar_%s" % npc_id,   # 不写房间名：切磋不进副本完成度
		"team_id": team_id,
		"is_elite": false,
	}, team, EncounterScript.CONTACT_FRONT, str(current_state().difficulty_id))
	encounter.spar_npc = npc_id
	close_npc()
	if battle_switch_handler.is_valid():
		battle_switch_handler.call(encounter)
	else:
		_set_status("这里不能切磋（没有战斗入口）")
		return {"ok": false, "error": "no_handler"}
	return {"ok": true, "team_id": team_id, "npc_id": npc_id}


## 剧情招募（设计 09 §3.2）：`recruit_def` 里条件旗标已点亮、人在这张图、又还没入队的同伴 → 入队。
##
## 三条入口：**进图时**（`announce=false`，旗标早就点亮了）、**按 E 时**（`announce=true`）、
## 以及城镇里**客栈那一次交互**。城镇只走客栈那一条——设计 09 §3.2 的原文是
## 「看过告示板后，在清风驿客栈对话」，在任意位置按 E 就收人会跳过玩家该走的那一步。
## 口径说明：数据能表达的粒度是「场景」（`recruit_def.join_scene`），所以判定按场景走；
## 「具体在哪一间房／哪一个位点」没有对应的列，已登记 `待策划确认.md` Q51。
func _join_pending_recruits(announce: bool) -> Array:
	var results: Array = RecruitServiceScript.join_all_for_scene(db, current_state(), scene_id)
	if results.is_empty():
		return results
	var names := PackedStringArray()
	var notes := PackedStringArray()
	var rewards := PackedStringArray()
	for entry: Dictionary in results:
		names.append(str(entry["name_cn"]))
		if not str(entry["note"]).is_empty():
			notes.append(str(entry["note"]))
		# 入队奖励（设计 20 号 §九：四条招募支线各写「入队 ＋ 某物」＋ 好感）——
		# 玩家得看见自己拿到了什么，不然「入队给的东西」等于没发生。
		for reward: String in Array(entry.get("rewards", [])):
			rewards.append(reward)
	_refresh_guide()
	if not announce:
		return results
	var suffix := "（%s）" % "；".join(notes) if not notes.is_empty() else ""
	if not rewards.is_empty():
		suffix += "　得到：%s" % "、".join(rewards)
	# 先存档再写状态：`autosave` 失败时会把状态栏写成「自动存档失败」，
	# 而「谁加入了队伍」是玩家更该看到的那条——所以把存档结果并进同一行，别让它被盖掉。
	var saved: Dictionary = autosave("剧情招募")
	var text := "%s 加入了队伍%s" % ["、".join(names), suffix]
	if not bool(saved.get("ok", false)):
		text += "　（自动存档失败：%s，这段进度只在本局里）" % str(saved.get("error", "未知原因"))
	_set_status(text)
	return results


## 玩家身边最近的建筑 Marker（`Markers/Buildings/<building_def.building_id>`）
func building_near_player() -> String:
	return _marker_near_player("Markers/Buildings", func(id: String) -> bool:
		return db.get_row("building_def", id) != null
	)


## 玩家身边最近的设施 Marker（`Markers/Facilities/<facility_id>`），如客栈 facility_inn
func facility_near_player() -> String:
	return _marker_near_player("Markers/Facilities", func(_id: String) -> bool: return true)


## 玩家身边最近的 NPC 站位（`Characters/npc_slot_0N`，地编交付的占位）。
##
## 07 §12 写着「城镇 NPC 位置先用 `Characters/npc_slot_0N` 占位即可；等 NPC／对话表
## （`dialogue_tree.csv`）出来后再按 NPC id 绑定」——**表还没到，所以这里只接「站位 + 交互」**：
## 走到跟前按 E 有一句如实的回应，`verify_maps` 那边校验它站在能走到的地方。
## 台词与身份等表到了以后往 `_talk_to_npc()` 这一处塞（NPC 的唯一入口）。
func npc_near_player() -> Node2D:
	if player == null:
		return null
	var best: Node2D = null
	var best_distance := NPC_DISTANCE
	for slot in npcs:
		if not is_instance_valid(slot):
			continue
		var distance: float = (slot as Node2D).global_position.distance_to(player.global_position)
		if distance <= best_distance:
			best = slot
			best_distance = distance
	return best


## 在某个 Markers/<组> 下找离玩家最近的位点（名字即 id），filter 用来筛掉不认识的 id
func _marker_near_player(group_path: String, filter: Callable) -> String:
	var box := world.get_node_or_null(group_path) if world != null else null
	if box == null or player == null:
		return ""
	var best := ""
	var best_distance := BUILDING_DISTANCE
	for child in box.get_children():
		if not (child is Node2D):
			continue
		var marker_id := str(child.name)
		if not bool(filter.call(marker_id)):
			continue
		var distance: float = (child as Node2D).global_position.distance_to(player.global_position)
		if distance <= best_distance:
			best = marker_id
			best_distance = distance
	return best


## 开店：把界面盖在当前场景上（不切场景，回来时位置与状态都还在）
func open_shop(building_id: String) -> Dictionary:
	if _panel_open(shop_panel):
		shop_panel.open_building(building_id)
		return {"ok": true, "error": "", "building_id": building_id, "reopened": true}
	var panel = load(SHOP_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "商店场景加载失败", "building_id": building_id}
	panel.name = "ShopPanel"
	panel.state_override = current_state()
	panel.building_id = building_id
	panel.return_handler = func() -> void: close_shop()
	add_child(panel)
	panel.setup()
	shop_panel = panel
	_push_overlay("shop")
	var welcome := "进店：%s（买入／卖出／回购，Esc 或点「离开」出来）" % panel.building_name()
	panel.show_message(welcome)
	_set_status(welcome)
	return {"ok": true, "error": "", "building_id": building_id, "reopened": false}


func close_shop() -> void:
	_close_overlay("shop")
	_set_status("离开店铺")


## 打坐（客栈）：{ok, error, facility}
func open_cultivate() -> Dictionary:
	if _panel_open(cultivate_panel):
		_push_overlay("cultivate")
		cultivate_panel.state_override = current_state()
		cultivate_panel.refresh()
		return {"ok": true, "error": "", "reopened": true}
	var panel = load(CULTIVATE_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "打坐场景加载失败"}
	panel.name = "CultivatePanel"
	panel.state_override = current_state()
	panel.return_handler = func() -> void: close_cultivate()
	add_child(panel)
	panel.setup()
	cultivate_panel = panel
	_push_overlay("cultivate")
	var welcome := "客栈打坐：一次 +1 熟练度，费用随熟练度递增（Esc 或点「离开」出来）"
	panel.show_message(welcome)
	_set_status(welcome)
	return {"ok": true, "error": "", "reopened": false}


func close_cultivate() -> void:
	_close_overlay("cultivate")
	_set_status("离开客栈")


func _panel_open(panel: Node) -> bool:
	return panel != null and is_instance_valid(panel)


## 城镇木桩练习战（设计 09 §3.3）：按 E 直接进一场「木桩不还手」的战斗。
##
## 两条前置都写在界面上、不静默：
##   · **练到顶**（`growth_const.dummy_xp_cap_level`）→ 显示 `ui_text.dummy_cap_reached`，不开战
##   · **数据没到**（设计侧还没给木桩的 `enemy_team`／`enemy_base` 两行）→ 日志点名缺哪一行，
##     界面上如实说「还没准备好」（`待策划确认.md` Q52）
## 真开打时 `encounter.practice = true`，结算那边按它把掉落／铜钱／首杀／领悟全部拿掉、
## 经验与熟练度按 `dummy_*` 封顶（见 `battle_screen._settle`）。
func start_practice(building_id: String) -> Dictionary:
	var state = current_state()
	# **到顶先判**：这条提示不依赖木桩的战斗数据（数据没到也该告诉玩家「练不出东西了」）
	if not PracticeServiceScript.xp_allowed(db, state):
		_set_status(PracticeServiceScript.cap_notice(db))
		return {"ok": false, "error": "练到顶了", "capped": true, "building_id": building_id}
	var ready: Dictionary = PracticeServiceScript.data_ready(db)
	if not bool(ready["ok"]):
		var why := str(ready["error"])
		push_error("[LocalMap] 木桩练习战缺数据：%s" % why)
		_set_status("木桩还没准备好（缺一条配置，已记进日志）")
		return {"ok": false, "error": why, "building_id": building_id}
	var encounter = PracticeServiceScript.build_encounter(db, scene_id, _difficulty())
	_start_battle(encounter)
	_set_status("木桩练习：打它练到 %d 级（招式熟练度到 %d 级）" % [
		PracticeServiceScript.cap_level(db), PracticeServiceScript.mastery_cap(db),
	])
	return {"ok": true, "practice": true, "encounter": encounter, "building_id": building_id}


## 开箱前先看这间房有没有守卫（设计 09 §一）：**没有守卫照旧直接开**，
## 有守卫且没解决就走守卫流程（文取付账／武取＝打赢房里那场仗）。
func _try_open_chest(chest) -> Dictionary:
	var room_id := GuardServiceScript.room_of_chest_key(str(chest.get_meta("key", "")))
	var guard: Resource = GuardServiceScript.guard_for_room(db, room_id)
	if guard == null or GuardServiceScript.is_resolved(db, current_state(), scene_id, room_id):
		return _open_chest(chest)
	return _approach_guard(room_id, guard)


## 守卫流程：**第一次按 E 只是让他开口**（说清他要什么、另一条路是什么），
## 第二次按 E 才付账——免费的东西不该在「以为要开箱」的那一下被拿走。
##
## 武取不用按键：房里那支队伍（`dungeon_room.enemy_team`，与 `npc_guard.fight_team` 同一条）
## 就站在那儿，走上去开打；赢了把房间记进 `dungeon_records`，`is_resolved()` 自然为真。
func _approach_guard(room_id: String, guard: Resource) -> Dictionary:
	var state = current_state()
	var guard_id := str(guard.guard_id)
	if not _guard_demands.has(guard_id):
		_guard_demands[guard_id] = true
		_set_status(GuardServiceScript.demand_text(db, state, guard))
		return {"ok": false, "error": "守卫挡着宝箱", "guard": guard_id, "stage": "demand", "room_id": room_id}
	var paid := {"ok": false, "error": ""}
	# 文取可能有多条路同时可用（0.14.0：三字段可叠加）。**优先走不花东西的那条**——
	# 判定过了就没必要再收走玩家的草药汤（设计只说「满足任意一条即算过」，没说必须付道具）。
	var chosen: Dictionary = {}
	for option: Dictionary in GuardServiceScript.peace_options(db, state, guard):
		if not bool(option["ok"]):
			continue
		if chosen.is_empty():
			chosen = option
			continue
		if str(chosen["kind"]) == "item" and str(option["kind"]) != "item":
			chosen = option
	if not chosen.is_empty():
		paid = GuardServiceScript.pay_peace(db, state, guard, str(chosen["kind"]), str(chosen["id"]))
	if bool(paid["ok"]):
		_guard_demands.erase(guard_id)
		_set_status("给了 %s：%s" % [str(guard.npc_name_cn), str(paid["text"])])
		autosave("宝箱守卫（文取）")
		return {
			"ok": true, "guard": guard_id, "stage": "peace",
			"text": str(paid["text"]), "room_id": room_id,
		}
	var why := str(paid["error"])
	if why.is_empty():
		why = "身上没有他要的东西，先按上面说的准备一下，或者直接动手"
	_set_status("%s还挡着：%s" % [str(guard.npc_name_cn), why])
	return {"ok": false, "error": why, "guard": guard_id, "stage": "refused", "room_id": room_id}


func _open_chest(chest) -> Dictionary:
	var state = current_state()
	if state == null or state.inventory == null:
		_set_status("没有会话状态，开不了宝箱")
		return {"ok": false, "error": "没有会话状态"}
	# 保底计数走**存档里那一份**（与战斗结算、扫荡同一个口径：唯一读写口是
	# `GameState.pity_tracker()`／`store_pity()`）。这里以前 `new` 了一个临时计数器，
	# 开箱的尝试记完就丢——今天宝箱组还没有保底槽（`pity_count` 全是 0）所以看不出来，
	# 但等设计给宝箱配保底的那天，那个槽会**永远不出货**（每次开箱都从 0 开始数）。
	var pity = state.pity_tracker()
	var resolver = DropResolverScript.new(db, _rng(), pity)
	var drops: Array = resolver.roll_group(chest.chest_id, _difficulty(), {"drop_rate_bonus": DropResolverScript.party_drop_bonus(_party())})
	state.store_pity(pity)
	# 宝箱出的装备同样要滚词条：显式传一个随机源，免得每次现开（也方便以后固定种子复用）
	var applied: Dictionary = BattleRewardScript.grant(
		db, state, 0, 0, drops, AffixRollerScript.new(db, _rng())
	)
	chest.mark_opened()
	# 音效位见设计 07 §8.4「宝箱开启」；事件名→文件的唯一出处是 Sfx.EVENTS
	SfxScript.play("chest")
	var key := str(chest.get_meta("key", ""))
	if not key.is_empty():
		var record: Array = local_state().get("chests", [])
		if not record.has(key):
			record.append(key)
		local_state()["chests"] = record
		# 完成度是永久记录（会话那份回大地图就清）
		if state.record_dungeon(scene_id, "chests", key):
			_refresh_progress()
			autosave("开宝箱")
	var summary := _drops_summary(applied, drops)
	_set_status("打开宝箱：%s" % summary)
	return {"ok": true, "drops": drops, "applied": applied, "summary": summary}


func _activate_trigger(point) -> Dictionary:
	var state = current_state()
	var row: Resource = point.row
	var required := str(row.required_item)
	var trigger_type := str(row.trigger_type)
	if not point.supported():
		_set_status("%s：暂时做不了（%s）" % [row.name_cn, point.unsupported_reason()])
		return {"ok": false, "error": "unsupported"}
	# 同一个触发可能有**多个位点**（酒葫芦在「牢房深处」与「后山地牢」各一处）。这一趟已经触发过的，
	# 另一个位点不该再给一次：`once_only` 只在**生成位点**时看记录，管不到同一次进图里的第二遍
	# （三火盆那条洞是决策 237 修的，这里收成对**所有触发类型**都生效的一条）。
	var already_triggered: Array = local_state().get("triggers", [])
	if already_triggered.has(str(row.trigger_id)):
		_close_trigger_points(str(row.trigger_id))
		_set_status("%s：这一趟已经触发过了" % str(row.name_cn))
		return {"ok": false, "error": "already_used"}
	if not required.is_empty() and (state == null or not state.inventory.has(required)):
		_set_status("%s：%s" % [row.name_cn, point.requirement_text()])
		return {"ok": false, "error": "missing_item"}
	# 条件类（宝箱全开／毒杀）先判条件，不满足就不算触发过
	var blocked := _condition_block(row)
	if not blocked.is_empty():
		_set_status("%s：%s" % [row.name_cn, blocked])
		return {"ok": false, "error": "condition_unmet"}

	var summary := ""
	match trigger_type:
		"item":
			summary = _trigger_item(row)
		"space":
			summary = _trigger_space(row)
		"carry":
			summary = _trigger_carry(row)
		"completion":
			summary = _trigger_completion(row)
		"kill_style":
			summary = _trigger_kill_style(row)
		"behavior":
			summary = _trigger_behavior(row)
		"sequence":
			# 顺序谜题：没点完**不算触发过**（不 mark_used、不记档、不发奖励），
			# 所以这里单独收口，而不是跟着下面那段「触发成功」的公共尾巴走。
			var step := _advance_sequence(row, point)
			if not bool(step["ok"]):
				_set_status("%s：%s" % [row.name_cn, str(step["error"])])
				return {"ok": false, "error": "sequence_step", "summary": str(step["error"])}
			if not bool(step["complete"]):
				_set_status("%s：%s" % [row.name_cn, str(step["summary"])])
				return {
					"ok": true, "complete": false, "summary": str(step["summary"]),
					"trigger_id": point.trigger_id, "granted": [],
				}
			summary = str(step["summary"])
	point.mark_used()
	# 同一行的其余位点一起收口：不然玩家走到第二个位点还能再领一次（隐藏 Boss 会再打一遍）
	_close_trigger_points(str(row.trigger_id))
	var used: Array = local_state().get("triggers", [])
	if not used.has(point.trigger_id):
		used.append(point.trigger_id)
	local_state()["triggers"] = used
	if state.record_dungeon(scene_id, "triggers", point.trigger_id):
		_refresh_progress()
		autosave("触发隐藏内容")
	# 隐藏内容里配的武学（source_type=hidden，source_id 就是这个触发点）
	var granted: Array = SkillGrantScript.grant_from_source(db, state, "hidden", point.trigger_id)
	var grant_lines: PackedStringArray = SkillGrantScript.summarize(granted)
	var full := summary
	if not grant_lines.is_empty():
		full = "%s　%s" % [summary, "；".join(grant_lines)]
	_set_status("%s：%s" % [row.name_cn, full])
	return {
		"ok": true, "complete": true, "summary": full,
		"trigger_id": point.trigger_id, "granted": granted,
	}


## 顺序谜题（三火盆）：按 `hidden_trigger.sequence`（例 `1-3-2`）逐次点**对**才成立。
##
## 三个位点的编号来自地图命名 `Trigger_trig_brazier_1/2/3`（07 资源需求里写明）。
## **点错怎么办**：设计 03 只写了「按序点燃」，没说点错；开发侧先定「提示 + 进度重置」，
## 已记 `待策划确认.md` Q42——要改成「点错也保留进度」只动这一处。
##
## 返回 {ok, complete, summary, error}；**没点完不算触发过**（进度存在会话的 `local_maps` 里，
## 回大地图整片刷新，与其它会话级进度同一口径）。
func _advance_sequence(row: Resource, point) -> Dictionary:
	var expected := _sequence_steps(row)
	if expected.is_empty():
		# 数据错：列名只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
		push_error("[LocalMap] hidden_trigger %s 没写 sequence" % str(row.trigger_id))
		return {"ok": false, "complete": false, "error": "这条谜题的次序没配（数据错，已记进日志）"}
	var index := int(point.sequence_index)
	if index <= 0:
		return {"ok": false, "complete": false, "error": "这个火盆没有编号（位点要按 _1／_2／_3 命名）"}
	var trigger_id := str(row.trigger_id)
	var progress: Array = _sequence_progress(trigger_id)
	if progress.size() >= expected.size():
		progress = []      # 兜底：记录比次序长（不该发生），重来
	# 已经做过的谜题**连第二遍都不接**：`can_interact()` 在玩家路径上已经拦住了，
	# 但这里再收一道口——同一次进图里，先做完的玩家还能走到别的火盆前按 E 走这条底层调用。
	var state_for_record = current_state()
	if state_for_record != null and Array(state_for_record.dungeon_record(scene_id).get("triggers", [])).has(trigger_id):
		for other in triggers:
			if str(other.row.trigger_id) == trigger_id:
				other.mark_used()
		return {"ok": false, "complete": false, "error": "这条谜题已经做过了"}
	if index != int(expected[progress.size()]):
		_set_sequence_progress(trigger_id, [])
		_reset_brazier_lights(trigger_id)
		return {
			"ok": false, "complete": false,
			"error": "次序不对（应当先点第 %d 个），进度已重置" % int(expected[0]),
		}
	progress.append(index)
	_set_sequence_progress(trigger_id, progress)
	point.set_lit(true)
	# 每点对一格 = 一个火盆被点燃（设计 07 §8.4「火盆点燃」）
	SfxScript.play("brazier")
	if progress.size() < expected.size():
		return {
			"ok": true, "complete": false,
			"summary": "点对了第 %d 个（%d/%d）" % [index, progress.size(), expected.size()],
		}
	# 凑齐次序 → 发奖励（这条谜题在表里配的是「前代寨主遗物」），并清掉进度
	_set_sequence_progress(trigger_id, [])
	# 同一行的**其余位点一起收口**：只把最后点的那一格标成已用的话，另外两格还写着「能点」，
	# 玩家站在原地再按一遍 1-3-2 就能再领一次（`once_only` 只在**生成位点**时看永久记录，
	# 管不到同一次进图里的第二遍）。这条以前真的能重复刷「前代寨主遗物」。
	_close_trigger_points(trigger_id)
	return {"ok": true, "complete": true, "summary": _grant_trigger_reward(row, "三个火盆依次亮起")}


## 把同一个触发行的**所有位点**一起收口（标记为已用）。
##
## 一行两位点的例子：`trig_wine`（酒葫芦）在牢房深处与后山地牢各一处。只把当前这个标成已用的话，
## 玩家走到另一个位点还能再领一次——隐藏 Boss 会再打一遍、装备再发一份（三火盆那次是同一个洞）。
func _close_trigger_points(trigger_id: String) -> void:
	for other in triggers:
		if str(other.row.trigger_id) == trigger_id:
			other.mark_used()


## 点错时把同一行的火盆**全部熄掉**：进度重置了、视觉也要跟着回灭，
## 否则亮着的格数会骗人（玩家以为已经点对了两个）。
func _reset_brazier_lights(trigger_id: String) -> void:
	for other in triggers:
		if str(other.row.trigger_id) == trigger_id:
			other.set_lit(false)


## `required_condition` 里的 `sequence=1-3-2` → [1, 3, 2]
## （次序写在 `required_condition` 里，不是单独一列——表结构就这样，别去猜别的列名）
func _sequence_steps(row: Resource) -> Array:
	var out: Array = []
	for piece: String in _condition_text(str(row.required_condition), "sequence").split("-", false):
		if piece.strip_edges().is_valid_int():
			out.append(int(piece.strip_edges()))
	return out


func _sequence_progress(trigger_id: String) -> Array:
	var table: Dictionary = local_state().get("sequence", {})
	return Array(table.get(trigger_id, []))


func _set_sequence_progress(trigger_id: String, steps: Array) -> void:
	var table: Dictionary = local_state().get("sequence", {})
	table[trigger_id] = steps.duplicate()
	local_state()["sequence"] = table


## 条件判定：返回空串表示满足，否则是一句给玩家看的原因
func _condition_block(row: Resource) -> String:
	var condition := str(row.required_condition)
	match str(row.trigger_type):
		"completion":
			# chest_open_rate=1.0：**前寨（第 1 层）**的宝箱全开（设计 03 第六条的原文就是「前寨宝箱全部开启」）
			var want_rate := _condition_value(condition, "chest_open_rate", 1.0)
			var gate := _completion_gate_chests(row)
			var total: int = gate.size()
			var opened := 0
			for chest in gate:
				if bool(chest.opened_already):
					opened += 1
			var rate := 1.0 if total <= 0 else float(opened) / float(total)
			if rate + 0.0001 < want_rate:
				return "还差 %d 个前寨宝箱没开（%d/%d）" % [maxi(0, total - opened), opened, total]
		"kill_style":
			# kill_with=poison：上一场要有人死于中毒
			var want_style := _condition_text(condition, "kill_with")
			var styles := _last_kill_styles()
			if not _has_kill_style(styles, want_style):
				return "要用「%s」击杀目标（上一场的死因：%s）" % [_style_name(want_style), _styles_label(styles)]
		"behavior":
			# flag_stealth_full=1：全程潜行不惊动守卫（被任何一个房间的守卫撞上就算失败）
			if _condition_text(condition, "flag_stealth_full") == "1":
				if bool(local_state().get("stealth_broken", false)):
					return "潜行已经失败过（被守卫撞上了，出去再进来重新潜）"
	return ""


## 「宝箱全开」这条隐藏内容的计数范围：本图**第 1 层（前寨）**、且**不在隐藏门后面**的宝箱。
##
## 为什么不能直接数 `chests.size()`：
##   ① 隐藏门后面的暗格银箱要等这扇门开了才拿得到——算进去条件**永远凑不齐**（死循环）；
##   ② 三层宝库的金箱不在前寨（设计 03 第六条写的是「**前寨**宝箱全部开启」）。
## 这条范围是开发侧按设计原文定的，已记进模块对接表等设计确认。
func _completion_gate_chests(row: Resource) -> Array:
	var behind_door := str(row.reward_id)
	var out: Array = []
	for chest in chests:
		var key := str(chest.get_meta("key", ""))
		var room_id := str(key.split("|", false)[0]) if not key.is_empty() else ""
		if room_id.is_empty() or room_id == behind_door:
			continue
		var room: Resource = db.get_row("dungeon_room", room_id)
		if room != null and int(room.floor) == 1:
			out.append(chest)
	return out


## 从 "key=value" 里取数值
func _condition_value(condition: String, key: String, fallback: float) -> float:
	for part: String in condition.split(";", false):
		var pieces := part.split("=", false)
		if pieces.size() == 2 and pieces[0].strip_edges() == key:
			return float(pieces[1])
	return fallback


func _condition_text(condition: String, key: String) -> String:
	for part: String in condition.split(";", false):
		var pieces := part.split("=", false)
		if pieces.size() == 2 and pieces[0].strip_edges() == key:
			return pieces[1].strip_edges()
	return ""


## 上一场战斗的击杀方式（battle_screen 写在 GameSession.last_battle 里）
func _last_kill_styles() -> Dictionary:
	var session_node := _session_node()
	if session_node == null:
		return {}
	var last: Variant = session_node.last_battle
	if last is Dictionary:
		var styles: Variant = Dictionary(last).get("kill_styles", {})
		if styles is Dictionary:
			return Dictionary(styles)
	return {}


func _has_kill_style(styles: Dictionary, want: String) -> bool:
	if want.is_empty():
		return false
	for actor_id: String in styles:
		if str(styles[actor_id]) == want:
			return true
	return false


func _styles_label(styles: Dictionary) -> String:
	if styles.is_empty():
		return "没有战斗记录"
	var parts := PackedStringArray()
	for actor_id: String in styles:
		parts.append("%s（%s）" % [_actor_label(actor_id), _style_name(str(styles[actor_id]))])
	return "、".join(parts)


func _style_name(style: String) -> String:
	# 击杀方式的中文名：`kill_with=poison` 这类条件值**不许直接给玩家看**。
	# 名字的**唯一出处是表**（`status_effect.name_cn`）——以前这里抄了一份 5 条的常量，
	# 而校验器的「合法击杀方式」是**按表**算的（`normal` + 所有状态 id）：
	# 设计新加一条状态、再拿它当 kill_with 时，这里会回落成英文 id 直接甩给玩家（见决策 231）。
	# `normal`（正面击杀）是唯一代码侧的名字：它不是一个状态行。
	if style == "normal":
		return "正面击杀"
	var row: Resource = db.get_row("status_effect", style)
	return str(row.name_cn) if row != null else style


## 战报里的击杀者署名：char_id → 角色名、敌 id → 敌人名，都查不到才回 id
func _actor_label(actor_id: String) -> String:
	var bare := actor_id.split("#", false)[0]
	var state = current_state()
	if state != null:
		var name_cn := str(state.char_name(db, bare))
		if name_cn != bare:
			return name_cn
	return db.display_name(bare)


## completion 类：宝箱全开 → 隐藏门浮现
##
## 以前这里只返回一句台词，`reward_type=room` 的奖励**根本没发**——玩家看到「隐藏门浮现」，
## 门没开、人也进不去，暗格银箱等于不存在。现在走和其他触发同一套发放：
## room 奖励会把玩家带进 `hf1_secret`（门就真的开了）。
func _trigger_completion(row: Resource) -> String:
	# 前缀别重复触发点名字（状态栏本来就会写成「<触发点名>：<这句>」）
	return _grant_trigger_reward(row, "墙上浮现一道暗门")


## kill_style 类：按击杀方式给奖励（毒杀毒手 → 掉落独门秘籍）
func _trigger_kill_style(row: Resource) -> String:
	var state = current_state()
	if state == null or state.inventory == null:
		return "没有会话状态，拿不到奖励"
	var style := _condition_text(str(row.required_condition), "kill_with")
	return _grant_trigger_reward(row, "达成「%s」" % _style_name(style))


## behavior 类：全程潜行不惊动守卫 → 额外剧情（奖励走同一套发放）
func _trigger_behavior(row: Resource) -> String:
	var state = current_state()
	if state == null or state.inventory == null:
		return "没有会话状态，拿不到奖励"
	return _grant_trigger_reward(row, "潜行入寨成功")


## 触发点的奖励发放（item／skillbook／equip／event／room 都走这里）
func _grant_trigger_reward(row: Resource, prefix: String) -> String:
	var state = current_state()
	var reward_type := str(row.reward_type)
	var reward_id := str(row.reward_id)
	match reward_type:
		"item", "skillbook":
			# 走与战斗／宝箱／事件判定同一套入账口径（货币进钱、装备建实例、其余堆叠）：
			# 以前这里直接 add_item，配一条「奖励 500 文」（`reward_id=item_money`）就会把铜钱
			# 当成一件背包物品发出去，`inventory.money` 一文不涨——与 `ev_gamble` 那个老坑同款。
			var granted: Dictionary = BattleRewardScript.grant_item(db, state, reward_id, 1)
			if not bool(granted["ok"]):
				return "想给 %s，但%s" % [item_name(reward_id), str(granted["error"])]
			if str(granted["kind"]) == "money":
				return "%s，得到 %d 文钱" % [prefix, int(granted["qty"])]
			return "%s，得到 %s ×%d" % [prefix, item_name(reward_id), int(granted["qty"])]
		"equip":
			var instance_id: String = state.inventory.add_equipment(db, reward_id)
			if instance_id.is_empty():
				return "想给 %s，但造不出实例" % item_name(reward_id)
			return "%s，得到装备 %s" % [prefix, item_name(reward_id)]
		"event":
			# 剧情旗标是内部记账（event_stealth_reward 这种），玩家看前缀那句话就够了
			state.set_flag(reward_id)
			return "%s，记下了这段经过" % prefix
		"room":
			var target: Node2D = world.get_node_or_null("Rooms/Room_%s" % reward_id)
			if target != null and player != null:
				player.global_position = target.position
				return "%s，被带到「%s」" % [prefix, db.display_name(reward_id)]
			# 房间不在这张图（= 数据错）：id 只进日志，别印给玩家
			push_error("[LocalMap] 触发奖励指向的房间不在这张图：%s（当前 %s）" % [reward_id, scene_id])
			return "%s（目标房间不在本图）" % prefix
	# 走到这儿说明表里写了一个校验器不认识的 reward_type——是**数据错**，不是「功能没做」。
	# 文案别甩一句「未实现」，不然以后查表的人会去代码里找一个根本不存在的坑。
	push_error("[LocalMap] 触发奖励的 reward_type 不认识：%s（trigger=%s）" % [reward_type, str(row.trigger_id)])
	return "%s（数据错：未知的奖励类型，已记进日志）" % prefix


func item_name(item_id: String) -> String:
	# 装备实例 id（`eq_sword_01#1`）也走这里：TableDb.display_name 会剥掉 #序号
	return db.display_name(item_id)


## item 类：给东西触发隐藏 Boss（酒葫芦 → 醉刀客）
func _trigger_item(row: Resource) -> String:
	if str(row.reward_type) != "boss":
		push_error("[LocalMap] item 触发的奖励类型配错了：%s 写成了 %s（item 触发只认 boss）" % [
			str(row.trigger_id), str(row.reward_type)])
		return "数据错：这个触发点的奖励类型配错了（item 触发只认 boss，已记进日志）"
	var enemy_id := str(row.reward_id)
	var enemy: Resource = db.get_row("enemy_base", enemy_id)
	if enemy == null:
		push_error("[LocalMap] 隐藏 Boss 奖励指向的敌人不存在：%s" % enemy_id)
		return "数据错：奖励指向的隐藏 Boss 不在敌人表里（已记进日志）"
	_start_battle(_boss_encounter(enemy_id, str(row.trigger_id)))
	return "隐藏 Boss %s 出现！" % enemy.name_cn


## 「单人 Boss 队」的 Encounter（隐藏 Boss 与「判定通过后 Boss 现身」共用一份构造）。
## `source_key` 是业务侧的来源标识：隐藏触发用 `trigger_id`，事件判定用 `check_id`
## （战斗结算要用它找掉落组／首杀记录，所以不能让两条路各编一个）。
func _boss_encounter(enemy_id: String, source_key: String):
	var enemy: Resource = db.get_row("enemy_base", enemy_id)
	if enemy == null:
		push_error("[LocalMap] Boss 奖励指向的敌人不存在：%s" % enemy_id)
		return null
	return EncounterScript.build(db, {
		"spawn_id": source_key,
		"source_scene": scene_id,
		"source_key": source_key,
		"team_id": "team_hidden_%s" % enemy_id,
		"is_elite": true,
	}, {
		"name_cn": str(enemy.name_cn),
		"threat_tag": str(enemy.threat_tag),
		"members": "%s:1" % enemy_id,
	}, EncounterScript.CONTACT_FRONT, _difficulty())


## space 类：用工具打开捷径（铁镐挖通 → 传送到目标房间）
func _trigger_space(row: Resource) -> String:
	var room_id := str(row.reward_id)
	var room: Node2D = world.get_node_or_null("Rooms/Room_%s" % room_id)
	if room != null:
		# 目标房间在同一张图：直接挪过去（例：图内隐藏门）
		if player != null:
			var point: Variant = _room_spawn_point(room_id)
			player.global_position = room.position if point == null else point
		return "挖通！直接通到「%s」" % db.display_name(room_id)

	# 目标房间不在这张图——设计 07 给这种捷径留了 `Portal_<scene_id>` 位点
	# （「塌陷山洞 → 后山地牢」就是这条：位点在 scene_cave，房间在 scene_heifengzhai）。
	# 以前这里只会回一句「找不到目标房间」，等于后山密道根本走不通。
	var target_scene := _scene_of_room(room_id)
	if target_scene.is_empty():
		push_error("[LocalMap] 捷径奖励指向的房间不在 dungeon_room 表里：%s" % room_id)
		return "数据错：目标房间不在副本房间表里（已记进日志）"
	var portal: Node2D = world.get_node_or_null("Markers/Portal_%s" % target_scene)
	if portal == null:
		# 玩家看不懂 `Portal_scene_heifengzhai` 这种 id；位点名只进日志（那才是地编要看的）
		push_error("[LocalMap] 缺 Portal_%s 位点：从 %s 挖通后进不去 %s" % [
			target_scene, scene_id, room_id])
		return "挖通了，但从这里进不去「%s」——本图缺通往那边的入口位点（等地编补）" % db.display_name(room_id)
	var session_node := _session_node()
	if session_node == null:
		return "挖通了，但没有会话状态，进不去「%s」" % db.display_name(room_id)
	# 重进小地图场景，让新落点生效（pending_local_scene + pending_local_room 就是为此写的）
	session_node.pending_local_scene = target_scene
	session_node.pending_local_room = room_id
	_remember_world_position(session_node)
	var text := "挖通！直通「%s」" % db.display_name(room_id)
	_set_status(text)
	if portal_switch_handler.is_valid():
		portal_switch_handler.call(target_scene, room_id)
	else:
		_change_scene(LOCAL_RUN)
	return text


## 房间属于哪张小地图（跨图捷径要据此换图）
func _scene_of_room(room_id: String) -> String:
	var row: Resource = db.get_row("dungeon_room", room_id)
	return str(row.scene_id) if row != null else ""


## 进场／打完回图时，站位可能正好压在还没清的敌人身上（逃跑时房间不会标记已清），
## 先推离接触圈，否则一进图就再被同一队抓一次。口径与大地图的 `_clear_spawn_overlap` 一致。
## 返回是否真的推过（推过就要给一段 `CONTACT_GRACE` 宽限，见调用处）。
func _clear_team_overlap() -> bool:
	if player == null:
		return false
	var pushed := false
	# 推开距离要够：只推 `CONTACT_DISTANCE + 6` 时，落点常常在墙里，
	# 物理把玩家顶回去就又进接触圈——败北回图会被同一队**立刻再抓**（真引擎探针抓到）。
	# 大地图那份是 `CONTACT_DISTANCE + 30`，这里对齐同一口径。
	var margin := RoamingEnemyScript.CONTACT_DISTANCE + 30.0
	for enemy in teams:
		if enemy == null or enemy.defeated or not enemy.visible:
			continue
		var away: Vector2 = player.global_position - enemy.global_position
		if away.length() > RoamingEnemyScript.CONTACT_DISTANCE:
			continue
		if away.length() < 0.01:
			away = Vector2.DOWN
		player.global_position = enemy.global_position + away.normalized() * margin
		enemy.reset_latch()
		_set_status("刚从遭遇里脱身，先退开几步")
		pushed = true
	return pushed


## carry 类：带着某件东西进来（锈剑共鸣）。
## 设计 03 写的是「解锁一剑式」，但 skill_base 里没有 source_id=trig_rusty_sword 的武学行，
## 所以现在只有一段演出、学不到东西——这是设计侧的数据缺口（reward_id=event_sword_resonance 没有落点），
## 记在交接表的 hidden_content 备注里，等设计补 skill_base 行后这里的 grant 通道自动生效。
func _trigger_carry(row: Resource) -> String:
	# 玩家可见：不许出现表名（AGENTS 硬规矩）；这里的确是设计侧的数据缺口，如实说一句人话。
	return "剑气共鸣：%s 震了一下——对应的武学还没落表，先记一段缘（等设计补）" % db.display_name(str(row.trigger_id))


# ------------------------------------------------------------------ 战斗与出口

func _on_encountered(room_id: String, contact: String) -> void:
	# 刚回图的宽限期（见 CONTACT_GRACE）：落点被墙顶回接触圈也不该当场又开一场
	if _contact_grace > 0.0:
		return
	var row: Resource = _room_row(room_id)
	if row == null:
		return
	# 被守卫撞上 = 潜行失败（「潜行入寨」这类行为条件靠它判定）
	local_state()["stealth_broken"] = true
	var team_id := str(row.enemy_team)
	var team: Resource = db.get_row("enemy_team", team_id)
	var encounter = EncounterScript.build(db, {
		"spawn_id": room_id,
		"source_scene": scene_id,
		"source_key": room_id,
		"team_id": team_id,
		"is_elite": str(row.room_type) == "elite" or str(row.room_type) == "boss",
	}, team, contact, _difficulty())
	_start_battle(encounter)


func _start_battle(encounter) -> void:
	var session_node := _session_node()
	if session_node != null:
		session_node.pending_encounter = encounter
		session_node.pending_return_scene = "res://scenes/local_run.tscn"
		session_node.pending_local_scene = scene_id
		if player != null:
			session_node.local_position = player.position
			session_node.local_position_scene = scene_id
	_set_status(encounter.headline())
	if battle_switch_handler.is_valid():
		battle_switch_handler.call(encounter)
		return
	_change_scene(BATTLE_SCENE)


func leave_to_overworld() -> void:
	# 回大地图 = 整片刷新：清掉这张小地图的会话状态
	var session_node := _session_node()
	if session_node != null:
		session_node.local_maps.erase(scene_id)
		session_node.pending_local_scene = ""
		session_node.pending_local_room = ""
		# 回大地图就整片刷新：位置也不再保留（下次进图从入口开始）
		session_node.local_position = Vector2.ZERO
		session_node.local_position_scene = ""
		# **不要**把回程落点改写成地标坐标：设计 02 是「返回大地图原位置」，
		# 而原位置就是 `enter_local_map()` 走进去那一步记下的 `world_position`。
		# 以前这里覆盖成地标坐标（= 入口位点），配上「走进入口自动进图」就变成
		# 「出图 → 站在入口上 → 立刻又进图」，玩家永远回不到大地图（真踩过）。
		# （「挖通」这类跨图捷径会自己调 `_remember_world_position` 记新落点，不受这条影响。）
	if return_handler.is_valid():
		return_handler.call()
		return
	_change_scene(OVERWORLD_RUN)


func _remember_world_position(session_node) -> void:
	var local_row: Resource = db.get_row("map_local", scene_id)
	var node_id := str(local_row.parent_node) if local_row != null else ""
	var region: Resource = db.get_row("map_region", node_id)
	if region != null:
		# 走会话层的写入口：会话内存与存档里的 world_pos（v13）一起更新
		session_node.set_world_position(Vector2(float(region.pos_x), float(region.pos_y)), current_state())


func _process(_delta: float) -> void:
	_contact_grace = maxf(0.0, _contact_grace - _delta)
	_field_refresh_timer -= _delta
	if _field_refresh_timer <= 0.0:
		_field_refresh_timer = 1.0
		_refresh_field_buffs()
		# 备货这类派生旗标一秒算一次：升级／买东西／打完仗回来都会让条件变真
		GuideServiceScript.refresh_derived_flags(db, current_state())
	if camera != null and player != null:
		camera.global_position = player.global_position
	# 条件地表与遮挡层：跟背包／走位走（两种都可能在这张图里当场变化——开箱拿到藏宝图、钻进岩檐）
	_apply_conditional_layer()
	_refresh_overlay_cover()
	_track_room()
	_track_sneak()
	if not _has_exit and player != null:
		var exit_marker: Node2D = world.get_node_or_null("Markers/Exit_%s" % scene_id)
		if exit_marker != null:
			_exit_position = exit_marker.position
			_has_exit = true
	if _has_exit and player != null:
		var exit_distance: float = player.position.distance_to(_exit_position)
		if not _exit_armed:
			# 出生点就压在出口上是合法摆法（清风驿的城门）：先走出去一次，出口才开始算数
			if exit_distance > EXIT_DISTANCE + EXIT_ARM_MARGIN:
				_exit_armed = true
		elif exit_distance <= EXIT_DISTANCE:
			leave_to_overworld()


## 记录玩家进过哪些房间（完成度里的「隐藏房间进入数」用它，永久写进存档）
func _track_room() -> void:
	if player == null:
		return
	var room_id := current_room_id()
	if room_id.is_empty() or room_id == _last_room_id:
		return
	_last_room_id = room_id
	# 换房间就把操作提示刷回来：「潜行中…」「刚从遭遇里脱身…」这类临时文案
	# 不该一直盖着「交互 E／完成度 M／扫荡 J」——那才是玩家要一直看到的东西
	_refresh_status()
	# 走进房间时把「只能在场景里判」的引导旗标点亮（0.22.0：首次进入黑风寨前寨）
	GuideServiceScript.note_room_entered(db, current_state(), scene_id, room_id)
	# 剧情节点也可能挂在"走进某个房间"上（条件旗标是别的系统点的）
	_claim_story_nodes()
	if not has_completion():
		return
	var state = current_state()
	if state == null:
		return
	var row: Resource = _room_row(room_id)
	# 表里没有 room_type=hidden：隐藏房间就是 branch_group = hidden（与 DungeonService 同一口径）
	if row != null and str(row.branch_group) == "hidden":
		if state.record_dungeon(scene_id, "rooms_entered", room_id):
			_refresh_progress()


## 玩家当前所在的房间：按 Rooms/<room_id>/bounds 的矩形判位置
func current_room_id() -> String:
	if world == null or player == null:
		return ""
	var box := world.get_node_or_null("Rooms")
	if box == null:
		return ""
	for child in box.get_children():
		if not (child is Node2D) or not str(child.name).begins_with("Room_"):
			continue
		var bounds := (child as Node2D).get_node_or_null("bounds") as Area2D
		if bounds == null:
			continue
		var rect := _bounds_rect(bounds)
		if rect.has_point(player.global_position):
			return str(child.name).substr("Room_".length())
	return ""


func _bounds_rect(bounds: Area2D) -> Rect2:
	for child in bounds.get_children():
		var shape_node := child as CollisionShape2D
		if shape_node == null or shape_node.shape == null:
			continue
		var rect_shape := shape_node.shape as RectangleShape2D
		if rect_shape == null:
			continue
		var center := shape_node.global_position
		return Rect2(center - rect_shape.size * 0.5, rect_shape.size)
	return Rect2()


# ------------------------------------------------------------------ 工具

func _room_row(room_id: String) -> Resource:
	for row: Resource in db.rows("dungeon_room"):
		if str(row.room_id) == room_id:
			return row
	return null


func _party() -> Array:
	var state = current_state()
	if state == null:
		return []
	return load("res://src/core/party_builder.gd").build_actors(db, state)


func _difficulty() -> String:
	var state = current_state()
	return str(state.difficulty_id) if state != null else "normal"


func _rng():
	return load("res://src/core/rng_service.gd").new()


func _drops_summary(applied: Dictionary, drops: Array) -> String:
	if drops.is_empty():
		return "空的"
	var parts := PackedStringArray()
	for entry: Dictionary in applied.get("items", []):
		parts.append("%s ×%d" % [item_name(str(entry["item_id"])), int(entry["qty"])])
	for instance_id: String in applied.get("equipment", []):
		parts.append(item_name(instance_id))
	if int(applied.get("money", 0)) > 0:
		parts.append("铜钱 %d" % int(applied["money"]))
	# 背包满时如实写「没捡起」：以前这里不看 overflow，宝箱开在满包上会显示「空的」，
	# 玩家以为箱子里没东西（其实是放不下）——玩家可见的文案不许说假话。
	var lost := PackedStringArray()
	for entry: Dictionary in applied.get("overflow", []):
		lost.append("%s ×%d" % [item_name(str(entry["item_id"])), int(entry["qty"])])
	if not lost.is_empty():
		parts.append("%s 没捡起（背包满）" % "、".join(lost))
	if parts.is_empty():
		return "背包放不下，什么都没捡起"
	return "、".join(parts)


func _build_status_label() -> void:
	_status = Label.new()
	_status.name = "Status"
	_status.position = Vector2(12, 8)
	_status.add_theme_color_override("font_color", Color(1, 1, 1, 0.92))
	_status.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	_status.add_theme_constant_override("shadow_offset_x", 1)
	_status.add_theme_constant_override("shadow_offset_y", 1)
	_hud_layer().add_child(_status)
	# 第三行（状态栏 y=8、完成度进度 y=34 各占一行）——放 30 会和 Progress 叠在一起
	_field_label = FieldBuffHudScript.build_label(Vector2(12, 56))
	_hud_layer().add_child(_field_label)


## 开局引导（设计 09 §3.1）：HUD 常驻一行「当前目标」，旗标点亮就自动推进。
## 位置排在状态栏 8／完成度 34／增益 56 之后的第四行（80），自检里有一条不叠字的断言盯着。
func _build_guide_label() -> void:
	_guide_label = Label.new()
	_guide_label.name = "Guide"
	_guide_label.position = Vector2(12, 80)
	_guide_label.add_theme_color_override("font_color", Color("8fe3ff"))
	_guide_label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	_guide_label.add_theme_constant_override("shadow_offset_x", 1)
	_guide_label.add_theme_constant_override("shadow_offset_y", 1)
	_hud_layer().add_child(_guide_label)
	_refresh_guide()


func _refresh_guide() -> void:
	if _guide_label == null:
		return
	_guide_label.text = GuideServiceScript.hud_text(db, current_state())


## 自检／用例读这一行的文案
func guide_text() -> String:
	return _guide_label.text if _guide_label != null else ""


## 引导 HUD 现在**应该**显示的那句（表里「条件已满足的最靠后一行」的文案）。
## 自检拿它当期望值——这样步号与分母都跟着表走，扩表不会再假红。
func _expected_guide_text() -> String:
	var state = current_state()
	var text := ""
	for row: Resource in GuideServiceScript.steps(db):
		if GuideServiceScript.condition_met(state, str(row.condition)):
			text = str(row.text_cn)
	return text


## 从 HUD 那行里取步号（`当前目标：…（3/6）` → 3）。取不到给 -1。
func _guide_step_index(line: String) -> int:
	var start := line.rfind("（")
	if start < 0:
		return -1
	var tail := line.substr(start + 1)
	var slash := tail.find("/")
	if slash <= 0:
		return -1
	var digits := tail.substr(0, slash)
	return int(digits) if digits.is_valid_int() else -1


## 战斗外增益（08）：城镇／副本里也看得见，剩余分钟随时间走（每秒刷一次够用）
func _refresh_field_buffs() -> void:
	if _field_label == null:
		return
	var session_node := _session_node()
	var rows: Array = session_node.active_field_buffs() if session_node != null else []
	_field_label.text = FieldBuffHudScript.text_of(db, rows)


## HUD 必须挂在 CanvasLayer 上：挂在世界节点上的话会跟着相机跑出屏幕
func _hud_layer() -> CanvasLayer:
	if _hud != null and is_instance_valid(_hud):
		return _hud
	_hud = CanvasLayer.new()
	_hud.name = "HUD"
	add_child(_hud)
	return _hud


func _refresh_status() -> void:
	if _status == null:
		return
	var row: Resource = db.get_row("map_local", scene_id)
	# 线索 K 与存档 F5 都要写出来：设计 03 要求「线索必须能被找到」；
	# 手动存档按设计 02 只在城镇生效，所以括号里注明——副本里按 F5 会得到「城镇才是存档点」的提示。
	var floor_text := _floor_label()
	var hint := "%s%s%s　交互 E　看人 Q　角色 Tab　行囊 I　线索 K　存档 F5（城镇）　回大地图 Esc" % [
		str(row.name_cn) if row != null else scene_id,
		"　·　" if not floor_text.is_empty() else "",
		floor_text,
	]
	# 推荐等级（map_local.level_range）：进图先知道这儿打不打得过，和明雷威胁色是一个用途
	if row != null and not str(row.level_range).is_empty():
		hint += "　推荐等级 %s" % str(row.level_range)
	if has_completion():
		hint += "　完成度 M　扫荡 J"
	_status.text = hint
	_refresh_field_buffs()


## 「第 N 层 房间名」——副本是三层同一张图 19 间房，不给这个玩家不知道自己走到哪了。
## 城镇这类没有楼层/房间的图返回空串（不硬凑）。
func _floor_label() -> String:
	var room_id := current_room_id()
	if room_id.is_empty():
		return ""
	var row: Resource = db.get_row("dungeon_room", room_id)
	if row == null:
		return ""
	var room_name := str(row.room_name)
	if room_name.is_empty():
		room_name = room_id
	return "第 %d 层 %s" % [int(row.floor), room_name]


## 这张图要不要显示完成度（map_local.has_completion）
func has_completion() -> bool:
	var row: Resource = db.get_row("map_local", scene_id)
	return row != null and bool(row.has_completion)


func dungeon_service():
	if _dungeon == null:
		_dungeon = DungeonServiceScript.new(db, current_state(), _rng())
	return _dungeon


func _build_progress_label() -> void:
	if not has_completion():
		return
	_progress_label = Label.new()
	_progress_label.name = "Progress"
	_progress_label.position = Vector2(12, 34)
	_progress_label.add_theme_color_override("font_color", Color("ffd24a"))
	_progress_label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	_progress_label.add_theme_constant_override("shadow_offset_x", 1)
	_progress_label.add_theme_constant_override("shadow_offset_y", 1)
	_hud_layer().add_child(_progress_label)
	_refresh_progress()


## 完成度 HUD 文案（含「本层可扫荡」提示）
func progress_text() -> String:
	if _progress_label == null:
		return ""
	return _progress_label.text


func _refresh_progress() -> void:
	if _progress_label == null:
		return
	var text: String = dungeon_service().progress_text(scene_id)
	var sweepable: Array = dungeon_service().sweepable_floors(scene_id)
	if not sweepable.is_empty():
		var floors_text := PackedStringArray()
		for floor: int in sweepable:
			floors_text.append("第 %d 层" % floor)
		text += "　｜　可扫荡：%s（按 J）" % "、".join(floors_text)
	_progress_label.text = text


func _set_status(text: String) -> void:
	if _status != null:
		_status.text = text


func _resolve_db():
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var table_db = TableDbScript.new()
	table_db.load_all()
	return table_db


func _session_node(node_name: String = "GameSession") -> Node:
	var tree := _tree()
	if tree == null:
		return null
	return tree.root.get_node_or_null(node_name)


func _tree() -> SceneTree:
	# 不在场景树里时 get_tree() 会打一条 ERROR（--script 模式用例直接 new 节点就会踩到）
	if is_inside_tree():
		var tree := get_tree()
		if tree != null:
			return tree
	return Engine.get_main_loop() as SceneTree


func _change_scene(path: String) -> void:
	var tree := _tree()
	if tree != null:
		tree.change_scene_to_file(path)


func _has_user_arg(flag: String) -> bool:
	return OS.get_cmdline_user_args().has(flag)


# ------------------------------------------------------------------ 自检

func _run_local_selftest() -> void:
	var ok := true
	var lines := PackedStringArray()
	ok = ok and world != null and player != null
	lines.append("%s：房间敌人 %d / 宝箱 %d / 隐藏触发 %d" % [scene_id, teams.size(), chests.size(), triggers.size()])

	# 真按键走一段（2026-10-04 补）。这一条以前**没有**，于是「地板瓦片自带碰撞盒 →
	# 一进副本就卡死」这种事故一路绿灯：下面每个检查都是把玩家**瞬移**过去的，
	# 从头到尾没有一帧真的用过输入＋物理（见 `框架说明.md` 决策 269）。
	# 四个方向各按住 15 个物理帧：**朝该方向的位移**必须真的发生——脚下是实心格、
	# 玩家没进树、动作没注册、朝向被挡住都会红（只看总位移会被"被挤出去"骗过去）。
	if player != null:
		var in_tree: bool = player.is_inside_tree()
		var spawn: Vector2 = player.global_position
		var walk_ok: bool = in_tree
		var report := PackedStringArray()
		for probe: Array in [["move_right", Vector2(1, 0)], ["move_left", Vector2(-1, 0)],
				["move_down", Vector2(0, 1)], ["move_up", Vector2(0, -1)]]:
			player.global_position = spawn
			var action := str(probe[0])
			var direction: Vector2 = probe[1]
			Input.action_press(action)
			for _frame in range(15):
				await _tree().physics_frame
			Input.action_release(action)
			var advance: float = (player.global_position - spawn).dot(direction)
			report.append("%s %+.1f px" % [action.trim_prefix("move_"), advance])
			if advance < 15.0:
				walk_ok = false
		player.global_position = spawn
		ok = ok and walk_ok
		lines.append("四方向都能走（各 15 物理帧，朝该方向的位移：%s，在树内=%s）=%s"
			% ["；".join(report), in_tree, walk_ok])

	# 撞房间敌人
	var captured: Array = []
	battle_switch_handler = func(encounter) -> void: captured.append(encounter)
	if not teams.is_empty():
		var enemy = teams[0]
		player.global_position = enemy.global_position + Vector2(0, 8)
		enemy.reset_latch()
		enemy._check_contact()
		var hit: bool = captured.size() == 1
		ok = ok and hit
		lines.append("房间遭遇 ok=%s（%s）" % [hit, captured[0].headline() if captured.size() > 0 else "无"])

	# 开宝箱
	if not chests.is_empty():
		player.global_position = chests[0].global_position
		var opened: Dictionary = interact()
		ok = ok and bool(opened.get("ok", false))
		lines.append("开宝箱 ok=%s（%s）" % [opened.get("ok", false), opened.get("summary", "")])

	# 找一条支持的触发试试（缺道具时应当给出需求提示）
	for point in triggers:
		if not point.supported():
			continue
		player.global_position = point.global_position
		var result: Dictionary = interact()
		lines.append("触发 %s ok=%s（%s）" % [point.trigger_id, result.get("ok", false), _status.text])
		break

	# 战斗外增益 HUD（08）：城镇／副本里也看得见；到点（用注入的 now）这一行就空掉
	var session_for_hud := _session_node()
	if session_for_hud != null:
		session_for_hud.clear_field_buffs()
		session_for_hud.add_field_buff("buff_meditated", 10)
		_refresh_field_buffs()
		var hud_ok: bool = (
			_field_label != null
			and _field_label.text.contains("打坐余韵")
			and _field_label.text.contains("剩 10 分钟")
		)
		ok = ok and hud_ok
		lines.append("战斗外增益 HUD ok=%s（%s）" % [hud_ok, _field_label.text if _field_label != null else "-"])
		# 别和上面两行叠字：状态栏 y=8、完成度进度 y=34
		var no_overlap: bool = (
			_field_label != null and _field_label.position.y > _status.position.y
			and (_progress_label == null or _field_label.position.y != _progress_label.position.y)
		)
		ok = ok and no_overlap
		lines.append("增益行不与状态栏／完成度叠字=%s（y=%.0f）" % [no_overlap, _field_label.position.y if _field_label != null else -1.0])
		session_for_hud.active_field_buffs(int(Time.get_unix_time_from_system()) + 11 * 60)
		_refresh_field_buffs()
		var expired_ok: bool = _field_label != null and _field_label.text.is_empty()
		ok = ok and expired_ok
		lines.append("过期后 HUD 为空=%s" % expired_ok)

		# 战败回城要用「最近到过的城镇」：这张图是小地图，只有城镇会登记
		var row_here: Resource = db.get_row("map_local", scene_id)
		if row_here != null and str(row_here.scene_type) == "town":
			var shelter_ok := str(session_for_hud.last_shelter_scene) == scene_id
			ok = ok and shelter_ok
			lines.append("城镇已登记为战败回城点=%s（%s）" % [shelter_ok, str(session_for_hud.last_shelter_name)])

		# 开局引导 HUD（09 §3.1）：这一行必须与表里「条件已满足的最靠后一行」**一致**。
		#
		# **不写死步号**：步数会随设计扩表（0.22.0 由 4 步扩到 6 步），而且副本自检
		# 一进来就把玩家摆在寨门 → 「进寨」旗标已点亮，绝对步号本来就不是 1
		# （写死 1/4 的那版在扩表当天假红过一次，见 `框架说明.md` 决策 273）。
		_refresh_guide()
		var guide_line := guide_text()
		var expect_line := _expected_guide_text()
		var guide_ok: bool = guide_line.contains("当前目标") and guide_line.contains(expect_line)
		ok = ok and guide_ok
		lines.append("引导 HUD ok=%s（%s）" % [guide_ok, guide_line])
		var guide_no_overlap: bool = (
			_guide_label != null and _guide_label.position.y > _status.position.y
			and (_progress_label == null or _guide_label.position.y > _progress_label.position.y)
			and (_field_label == null or _guide_label.position.y > _field_label.position.y)
		)
		ok = ok and guide_no_overlap
		lines.append("引导行不与上面三行叠字=%s（y=%.0f）" % [guide_no_overlap, _guide_label.position.y if _guide_label != null else -1.0])
		# 旗标点亮后这一行要跟着推进（09 §3.1「完成自动推进」）
		current_state().set_flag(FLAG_BOARD_READ)
		_refresh_guide()
		var guide_after := guide_text()
		# 推进的判据：① 行的内容仍与表一致 ② 步号**不倒退**（旗标只会往前走）
		var advanced: bool = guide_after.contains(_expected_guide_text()) \
			and _guide_step_index(guide_after) >= _guide_step_index(guide_line)
		ok = ok and advanced
		lines.append("引导会自动推进（点亮告示板旗标）=%s（%s）" % [advanced, guide_after])

	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	# 浮层栈（设计 18.1）：商店 → 角色（Tab）→ Esc 回商店 → Esc 关商店。
	# 这条链是 0.18.1 的核心——**全局快捷键在任何浮层里都可用**，而且 Esc 一次只弹一层。
	var stack_ok := true
	open_shop("bld_grocery")
	var shop_alive := _panel_open(shop_panel)
	stack_ok = stack_ok and overlays().depth() == 1 and shop_alive
	open_character_overlay(0)
	stack_ok = stack_ok and overlays().depth() == 2 and _panel_open(shop_panel)
	# 已在栈里 → 弹回它（不重复压），并切到指定页签
	open_character_overlay(2)
	var char_panel := _overlay_node("character")
	stack_ok = stack_ok and overlays().depth() == 2 and char_panel != null and char_panel.current_tab() == 2
	stack_ok = stack_ok and close_top_overlay() and overlays().depth() == 1 and _panel_open(shop_panel)
	stack_ok = stack_ok and close_top_overlay() and overlays().is_empty() and not _panel_open(shop_panel)
	ok = ok and stack_ok
	lines.append("浮层栈（商店→角色→Esc 回商店→Esc 关商店）=%s" % stack_ok)

	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	var copy_hits: PackedStringArray = CopyGuardScript.id_tokens(self)
	ok = ok and copy_hits.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	if not copy_hits.is_empty():
		lines.append("COPY 命中：%s" % "；".join(copy_hits))
	# 观察点**看得见**（设计 15 §一「可交互物暖色提亮」，决策 337）：本图的每一条都要有那枚标记。
	# 放在自检里是因为它量的是**真节点**（用例那边量的是同一条，但自检会在带窗口的冒烟里也跑一遍）。
	var highlight_ok := not flavor_points.is_empty()
	for point: Dictionary in flavor_points:
		var mark = (point["node"] as Node2D).get_node_or_null(FlavorMarkerScript.NODE_NAME)
		highlight_ok = highlight_ok and FlavorMarkerScript.is_visible_highlight(mark)
	ok = ok and highlight_ok
	lines.append("观察点可见标记 %d 个 ok=%s" % [flavor_points.size(), highlight_ok])

	# NPC：**按 Q 真的把信息面板压进浮层栈**，而且**失败也要出声**（2026-10-04 实机反馈"按 Q 没反应"）。
	# 两条路径以前都没有断言：两个 runner 一直打印 `SELF-TEST: OK`，可没人碰过这两条路。
	# 这里走**真处理器**（合成一个 `npc_info` 动作喂 `_unhandled_input`），不是直接调 `open_npc()`——
	# 坏在"按键没接上"或"位点绑不到人"时会红。
	if not npcs.is_empty():
		var npc_ok := true
		var probe = npcs[0]
		var camera_before: Vector2 = player.global_position
		player.global_position = (probe as Node2D).global_position
		var info_event := InputEventAction.new()
		info_event.action = "npc_info"
		info_event.pressed = true
		_unhandled_input(info_event)
		var slot_id := str((probe as Node2D).name)
		var bound := not _npc_for_slot(slot_id).is_empty()
		npc_ok = npc_ok and (overlays().depth() > 0 if bound else true)
		if bound:
			npc_ok = npc_ok and _panel_open(npc_panel)
			close_top_overlay()
		# 失败路径：走远一点再按 Q —— 必须给一句可见提示（不许静默 return）
		player.global_position = camera_before
		_set_status("")
		var far_event := InputEventAction.new()
		far_event.action = "npc_info"
		far_event.pressed = true
		_unhandled_input(far_event)
		npc_ok = npc_ok and not (_status == null or _status.text.is_empty())
		ok = ok and npc_ok
		lines.append("按 Q 看人（位点 %s 绑到人=%s；走远后提示「%s」）=%s"
			% [slot_id, bound, str(_status.text) if _status != null else "", npc_ok])

	for line: String in lines:
		print("  " + line)
	print("LOCAL SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	var tree := _tree()
	if tree != null:
		tree.quit(0 if ok else 1)
