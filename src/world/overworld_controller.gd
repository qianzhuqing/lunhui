## 大地图控制器：把地编的场景 + 附表数据跑起来。
##
## 职责：
##   1. 实例化 `scenes/maps/overworld.tscn`（地编资产，只读不动）
##   2. 按 `roaming_spawn` 在 `Spawn_<id>` 位点生成明雷
##   3. 生成玩家、相机跟随、按 `map_region` 决定起始位置
##   4. 明雷接触 → 组 Encounter → 切到战斗场景
##   5. 战斗回来时应用「已清明雷」状态（含重生计时）
extends Node2D

const OVERWORLD_SCENE := "res://scenes/maps/overworld.tscn"
const BATTLE_SCENE := "res://scenes/battle_screen.tscn"
const LOCAL_RUN := "res://scenes/local_run.tscn"
const PLACEHOLDER_SCENE := "res://scenes/placeholder_game.tscn"
## 战斗外增益 HUD：与城镇共用同一份文案（`FieldBuffHud`），别在两处各写一句话
const FieldBuffHudScript := preload("res://src/world/field_buff_hud.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const GuideServiceScript := preload("res://src/core/guide_service.gd")
const StoryServiceScript := preload("res://src/core/story_service.gd")
const ChapterServiceScript := preload("res://src/core/chapter_service.gd")
const OverlayStackScript := preload("res://src/ui/overlay_stack.gd")
const CHARACTER_SCENE := "res://scenes/character_screen.tscn"
const WorldEventServiceScript := preload("res://src/core/world_event_service.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const RecruitServiceScript := preload("res://src/core/recruit_service.gd")
## NPC 交往与对话（设计 19／20）：大地图上的人 —— `npc_def.place_id` 允许写**区域节点**
## （`NpcService.npcs_at()` 本来就两种都认），可这条交互以前只在小地图里接，
## 于是「落雁坡的采药人老周」在大地图上按 E 什么也不发生。
const NpcServiceScript := preload("res://src/core/npc_service.gd")
const NpcPanelMode := preload("res://src/ui/npc_panel.gd")
const NPC_SCENE := "res://scenes/npc_panel.tscn"
## 站多近才能按 E 跟人说话（与 `local_map_controller` 同一个数）
const NPC_DISTANCE := 30.0
## 没配行的人顶着的称呼（与 `local_map_controller` 同一套）
const NPC_SPEAKER := "路人"
## 走进这个距离就进小地图／算发现地标
const PORTAL_DISTANCE := 26.0
const DISCOVER_DISTANCE := 96.0
## 驿站交互距离（走到驿站图标旁边按 E）
const POST_DISTANCE := 40.0
## 事件判定位点的交互距离
const EVENT_DISTANCE := 40.0
## 本章不开放的地点
## 本章不开放的小地图：**清单只有一处**（`WorldMapService.LOCKED_SCENES`），这里引用它。
## 以前两个文件各写一份、注释写着「同一口径」，但没有任何门限盯着它们一致——
## 谁改一边都不会红，玩家那边却会出现「地图不让进、驿站却还列着」这种前后不一致
## （见框架说明决策 88，以及 `test_handshake` 的「同一事实只许一处」门限）。
## 入口要「先走出圈外一次」才武装：从小地图回大地图时落点就在入口位点上
## （设计 02：「返回大地图原位置」，而原位置就是走进入口的那一步），
## 不设这条就会出现「进图 → 走到出口 → 又被立刻送回图」，玩家永远回不到大地图（真踩过）。
const PORTAL_ARM_MARGIN := 8.0
## 地标图标（07 §8.1：id 配在 `map_region.icon`，资产在 assets/sprites/icons/）
const ICON_DIR := "res://assets/sprites/icons/"
## 图标比标记点高一点，免得压住玩家/明雷
const ICON_OFFSET := Vector2(0, -26)
## 玩家离地标多近才点亮高亮（像素；一格 32px，两格出头）
const HIGHLIGHT_DISTANCE := 70.0
## 走到某个**区域地标**多近才算「到了那儿」——剧情招募里 `join_scene` 指向区域节点的那几位
## （现例：林铁山在落雁坡）用这个半径判定。取值与地标高亮同一个（脚下 70px），
## 因为那是全项目唯一一处「你就在这个地标旁」的既有口径；要不要改成范围／触发点由设计定（Q51）。
const REGION_JOIN_DISTANCE := HIGHLIGHT_DISTANCE

const PlayerControllerScript := preload("res://src/world/player_controller.gd")
const RoamingEnemyScript := preload("res://src/world/roaming_enemy.gd")
const EncounterScript := preload("res://src/core/encounter.gd")
const InputSetupScript := preload("res://src/world/input_setup.gd")
const TableDbScript := preload("res://src/core/table_db.gd")
const WorldMapServiceScript := preload("res://src/core/world_map_service.gd")
const WAYPOINT_SCENE := "res://scenes/waypoint_screen.tscn"
const EventCheckServiceScript := preload("res://src/core/event_check_service.gd")
const CLUE_SCENE := "res://scenes/clue_screen.tscn"
## 行商货架（Q64：`world_event.we_caravan` 就地开张，不是常驻建筑）
const SHOP_SCENE := "res://scenes/shop_screen.tscn"
const ShopServiceScript := preload("res://src/core/shop_service.gd")
## 观察点（设计 20 §3.2）：最薄的一张表，一个位点一句话
const FlavorPointScript := preload("res://src/data/tables/flavor_point_row.gd")
## 观察点的可见标记（设计 15 §一「可交互物暖色提亮」）：视觉只有一处出处
const FlavorMarkerScript := preload("res://src/world/flavor_marker.gd")
const SaveServiceScript := preload("res://src/core/save_service.gd")

## 起始地点：新游戏从清风驿出发
const START_NODE := "n_qingfengyi"

## 测试注入点
var battle_switch_handler := Callable()
## 切场景的接管点：用例用它避免真换场景（顺便能断言切到哪张图）
var scene_change_handler := Callable()
var state_override = null
## 存档设施（用例可注入临时目录）
var save_store_override = null

var db
var world: Node2D = null
var player = null
var camera: Camera2D = null
var enemies: Array = []
var _status: Label = null
## 战斗外增益 HUD（08）：大地图上也必须看得见打坐余韵／饱食还剩几分钟
var _field_label: Label = null
var _field_refresh_timer := 0.0
var _map_label: Label = null
## 开局引导 HUD（设计 09 §3.1）：大地图上也常驻一行「当前目标」
var _guide_label: Label = null
## 随机事件的提示行（设计 19 §四：只在事件存活期间出现）
var _event_label: Label = null
## 当前活着的随机事件（一行 world_event；空 = 没有）
var _event_row: Resource = null
var _event_marker: Node2D = null
var _event_timer := 0.0
var _event_cooldown := 0.0
## 上一次领剧情节点时的区域 id（`_track_region_for_story()` 用它判断「走进新区域了没有」）
var _last_claimed_region := ""
var _event_rng = null
var _hud: CanvasLayer = null
var _portals: Array = []
## 入口是否已武装（刚回图时人可能正踩在入口上，先离开圈外一次才算数）
var _portal_armed := false
## 驿站界面（覆盖在当前场景上的界面，关掉就 queue_free）
var waypoint_panel: Node = null
## 线索本（K 打开：野外事件按地标分组）
var clue_panel: Node = null
## 行商货架（随机事件就地开张；与线索本同一套浮层，出栈即关）
var shop_panel: Node = null
## 浮层栈（设计 18.1）：线索本／驿站／角色面板都压在它上面
var _overlays = null
var _save_service = null
## 地图揭开与驿站规则的唯一来源（状态写在存档里）
var world_map
## 事件判定位点：{check_id, node, position}
var events: Array = []
## 观察点位点：{point_id, node, position}（设计 20 §3.2；大地图这边收 `region_id` 那几条）
var flavor_points: Array = []
## 大地图上的 NPC 站位（`Characters/npc_slot_0N`）
var npcs: Array = []
## NPC 交往面板（覆盖在当前场景上的接口，关掉就 queue_free）
var npc_panel: Node = null
## 地标图标：node_id → Sprite2D（见 _build_node_icons）
var _node_icons: Dictionary = {}
## 条件地表层（`Conditional`，0.32.0）：大地图上那条「藏宝图上的细径」
var _conditional_layer: TileMapLayer = null
## 「这个地标的贴图两张都找不到」已经报过没（只报一次，别每帧刷屏）
var _icon_missing_warned: Dictionary = {}
## 地标名字（node_id → Label，见 `_build_node_labels`）
var _node_labels: Dictionary = {}
## 相机默认倍数（设计 0.31.2「大地图扩容」）：一屏约 16×9 格，走起来才有"路程"。
## **写在代码里、不写进场景**：大地图正由地编按 2048×1536 重建，场景一换这份设置会被覆盖掉。
const CAMERA_ZOOM := 2.0
## 指路牌文案（node_id → Label，见 `_build_signposts`）
var _signpost_labels: Dictionary = {}
## 脚下的地标高亮（叠在最近的地标上）
var _highlight: Sprite2D = null
var _event_service = null
var _was_sneaking := false


func _ready() -> void:
	# 这个场景自检验的正是**明雷机制**（生成／接触／巡逻／追击／回图落点）。
	# 0.8.1 起大地图明雷默认关着，所以自检跑之前先把会话级开关打开；
	# 开关本身的两种状态由 `_run_world_selftest()` 里的两条断言各自钉一遍。
	if _has_user_arg("--world-selftest"):
		var session_node = session()
		if session_node != null:
			session_node.roaming_enabled_override = 1
	setup()
	if _has_user_arg("--world-selftest"):
		call_deferred("_run_world_selftest")


func setup() -> void:
	if world != null:
		return
	InputSetupScript.ensure()
	db = _resolve_db()
	var packed: PackedScene = load(OVERWORLD_SCENE)
	world = packed.instantiate()
	add_child(world)
	var ysort: Node2D = world.get_node_or_null("YSort")
	if ysort != null:
		ysort.y_sort_enabled = true
	_spawn_player()
	_spawn_enemies()
	_conditional_layer = world.get_node_or_null("Conditional") as TileMapLayer
	_apply_conditional_layer()
	_build_node_icons()
	_build_node_labels()
	_build_signposts()
	_collect_portals()
	_collect_events()
	_collect_flavor_points()
	_collect_npc_slots()
	world_map = WorldMapServiceScript.new(db, current_state())
	world_map.apply_initial_reveals()
	_build_status_label()
	_refresh_status()
	_build_map_label()
	_build_guide_label()
	_build_event_label()
	_check_region_recruits()
	# 剧情节点与派生引导旗标（0.22.0）：大地图上也有「不限地点」的节点（药王谷那条），
	# 以及靠升级／买东西才会变真的备货条件。
	GuideServiceScript.refresh_derived_flags(db, current_state())
	_claim_story_nodes()
	_apply_fog()
	_apply_conditional_layer()
	# 放在最后：状态栏提示与揭雾都不该被「刚脱身」这一步盖掉
	_push_player_out_of_contact()


## 领取大地图上能领的剧情节点（**`place_id` 留空的**＋**指向当前区域节点的**那些）。
## 返回领到的节点，供状态栏播报。
##
## **为什么传当前区域 id 而不是空串**（2026-10-04 修）：`StoryService.place_matches` 对**空串**的口径是
## 「**只**匹配 `place_id` 留空的节点」——所以 21 §九 那条把地点写成**区域节点**的
## （林铁山的「镖车暗格」在落雁坡，`opp_gang.place_id = n_luoyanpo`）**在游戏里永远领不到**
## （《沉沙心法·不还》成了拿不到的死内容）。服务层与用例一直是对的（它们显式传 `n_luoyanpo`），
## 错的是这个调用点：**「用例过的路」和「游戏走的路」不是同一条**（决策 345）。
##
## `place_matches` 对「区域 id」是超集：`place_id` 留空的那些仍然会命中（`want.is_empty()` 先判）✓。
func _claim_story_nodes() -> Array:
	var state = current_state()
	if state == null or db == null:
		return []
	var place := current_region_id()
	_last_claimed_region = place
	var claimed: Array = StoryServiceScript.claim_for(db, state, place)
	if claimed.is_empty():
		return []
	var names := PackedStringArray()
	for entry: Dictionary in claimed:
		names.append(str(entry.get("text_cn", entry.get("node_id", ""))))
	var advance: Dictionary = ChapterServiceScript.try_advance(db, state)
	if bool(advance.get("advanced", false)):
		names.append("章节推进：%s" % str(advance.get("name", "")))
	if not str(advance.get("text", "")).is_empty():
		names.append(str(advance.get("text")))
	if _status != null and not names.is_empty():
		_status.text = "；".join(names)
	_refresh_guide()
	return claimed


## 走进新区域时再领一次：玩家是**走**过去的，而 `_claim_story_nodes()` 只在场景载入时跑过一遍。
## 与 `_check_region_recruits()` 同一套口径——走到地标跟前就算「到了」。
func _track_region_for_story() -> void:
	if player == null or db == null:
		return
	if _last_claimed_region == current_region_id():
		return
	_claim_story_nodes()


func current_state():
	if state_override != null:
		return state_override
	var session := _session_node("GameSession")
	return session.state if session != null else null


func session() -> Node:
	return _session_node("GameSession")


# ------------------------------------------------------------------ 生成

func _spawn_player() -> void:
	player = PlayerControllerScript.new()
	player.name = "Player"
	var spawn_position := _start_position()
	player.position = spawn_position
	_add_to_world(ysort_node(), player)
	player.z_index = 1

	camera = world.get_node_or_null("Camera")
	if camera != null:
		camera.global_position = player.global_position
		# 2× 缩放（设计 0.31.2）：写在这里而不是场景里，理由见 CAMERA_ZOOM 的注释
		camera.zoom = Vector2(CAMERA_ZOOM, CAMERA_ZOOM)


func _start_position() -> Vector2:
	var session_node = session()
	if session_node != null and session_node.world_position != Vector2.ZERO:
		return session_node.world_position
	var marker: Node2D = world.get_node_or_null("Markers/Node_%s" % START_NODE)
	if marker != null:
		return marker.position
	return Vector2(512, 384)


## 回到大地图时别站在明雷身上。
##
## 上一场没赢（败北或**撤退**）时，`world_position` 就是当时的接触点，也就是明雷身边；
## 场景重建后明雷的 `_encounter_latched` 归零，不推开就会瞬间再抓一次——「跑得掉」就成了一句空话。
## 只在真的重叠时推开（沿玩家与敌人都连线上往外推），正常落点不受影响。
func _push_player_out_of_contact() -> void:
	if player == null:
		return
	var margin := RoamingEnemyScript.CONTACT_DISTANCE + 30.0
	for enemy in enemies:
		if enemy == null or enemy.defeated or not enemy.visible:
			continue
		var away: Vector2 = player.global_position - enemy.global_position
		if away.length() > RoamingEnemyScript.CONTACT_DISTANCE:
			continue
		if away.length() < 0.01:
			away = Vector2.DOWN
		player.global_position = enemy.global_position + away.normalized() * margin
		# 敌方的接触闩也清一下，免得它把「刚才那一下」当成已经触发过
		enemy.reset_latch()
		_set_status("刚从遭遇里脱身，先退开几步")


## 大地图明雷开关（0.8.1）：`feature_toggle.overworld_roaming_enemy`，默认 0 = 大地图不出现明雷。
## 会话级覆盖（用例／调试）在 `GameSession.roaming_enabled_override`——场景重建也丢不掉。
func roaming_enabled() -> bool:
	var session_node = session()
	if session_node != null and int(session_node.roaming_enabled_override) >= 0:
		return int(session_node.roaming_enabled_override) == 1
	var row: Resource = db.get_row("feature_toggle", "overworld_roaming_enemy")
	if row == null:
		return true     # 表里没这行就当"开着"：别让老数据静默变成一张空地图
	return int(row.value) == 1


## 重建大地图明雷（开关改变后调用；闭环用例靠它把开关打开再跑明雷那几段）。
## 幂等：先清掉现有的，再按会话状态重新生成，最后把玩家从接触圈里推开。
func rebuild_roaming_enemies() -> void:
	for enemy in enemies:
		if enemy != null and is_instance_valid(enemy):
			enemy.queue_free()
	enemies.clear()
	_spawn_enemies()
	_push_player_out_of_contact()


func _spawn_enemies() -> void:
	var session_node = session()
	var now := int(Time.get_unix_time_from_system())
	# 0.8.1：大地图明雷由 `feature_toggle.overworld_roaming_enemy` 控制（默认 0 = 不出现）。
	# 机制本身一行代码都没删——置 1 就照旧（闭环用例会打开它跑胜／败两种结局）。
	if not roaming_enabled():
		return
	for row: Resource in db.rows("roaming_spawn"):
		var spawn_id := str(row.spawn_id)
		var enemy = RoamingEnemyScript.new()
		enemy.name = "Spawn_%s" % spawn_id
		var marker: Node2D = world.get_node_or_null("Markers/Spawn_%s" % spawn_id)
		if marker == null:
			push_error("[Overworld] 地图里没有位点 Markers/Spawn_%s" % spawn_id)
			continue
		enemy.position = marker.position
		var team: Resource = db.get_row("enemy_team", str(row.team_id))
		var path: Path2D = world.get_node_or_null("Markers/Patrol_%s" % str(row.patrol_path_id))
		enemy.setup(db, row, team, player, path)
		enemy.sneak_detect_reduce = sneak_detect_reduce()
		enemy.encountered.connect(_on_encountered)
		_add_to_world(ysort_node(), enemy)
		enemies.append(enemy)
		# 战斗回来时应用「已清」状态
		if session_node != null and session_node.cleared_spawns.has(spawn_id):
			var until := int(session_node.cleared_spawns[spawn_id])
			if until < 0:
				enemy.mark_defeated(0)
			elif until > now:
				enemy.mark_defeated(until - now)
			else:
				# 冷却到了：**把记录删掉**再让它出现——留着的话 `cleared_spawns.has(id)` 还是真
				# （「已清」与「已在场上」两种状态会同时成立，读它的人就被骗了）。
				session_node.cleared_spawns.erase(spawn_id)


func _add_to_world(parent: Node, node: Node) -> void:
	if parent != null:
		parent.add_child(node)
	else:
		world.add_child(node)


func ysort_node() -> Node2D:
	return world.get_node_or_null("YSort")


## 地标图标：设计 02「已探索的地标显示在地图上」+ 07 §8.1 的 7 个图标 id。
## 状态口径（开发侧定，已记进交接表）：
##   已揭开 → 正常图标；**未揭开 → 不画**（02 说未探索用云雾盖着，不该提前剧透）；
##   已揭开但本章去不了（`LOCKED_SCENES`）→ `_dim` 暗版（看得见、去不了）；
##   玩家脚下最近的已揭开地标 → 叠一层 `icon_highlight` 高亮。
func _build_node_icons() -> void:
	for row: Resource in db.rows("map_region"):
		var node_id := str(row.node_id)
		var marker: Node2D = world.get_node_or_null("Markers/Node_%s" % node_id)
		if marker == null:
			continue
		# 地编在地图上留了一份 `Markers/Node_<id>/icon`（**摆位参考**，给地图预览用）：
		# 它默认可见、又不认揭雾，于是开局会把黑风寨／落雁坡／石隙的图标直接亮在图上——
		# 与设计 0.32.0「没探索的地方完全不存在（没有图标、没有地名）」直接冲突。
		# 运行期一律收掉那一份，图标只由下面这套按揭雾状态控制的 sprite 画。
		var preview_icon: Node = marker.get_node_or_null("icon")
		if preview_icon is CanvasItem:
			(preview_icon as CanvasItem).visible = false
		var sprite := Sprite2D.new()
		sprite.name = "Icon_%s" % node_id
		sprite.position = marker.position + ICON_OFFSET
		sprite.visible = false
		_add_to_world(ysort_node(), sprite)
		_node_icons[node_id] = sprite
	# 高亮叠在最后：和图标同层，但画在它们上面
	var highlight_file := "%sicon_highlight.png" % ICON_DIR
	if ResourceLoader.exists(highlight_file):
		_highlight = Sprite2D.new()
		_highlight.name = "NodeHighlight"
		_highlight.texture = load(highlight_file)
		_highlight.visible = false
		_add_to_world(ysort_node(), _highlight)
	refresh_node_icons()


## 条件地表层（`Conditional`）：整层显隐，判据在 `WorldMapService.conditional_layer_rule`——
## 这张图上那些地标里「持有类解锁」的，东西到手就显示（石隙细径绑 `item_treasure_map`）。
## 每帧调一次很便宜（一次背包查询），因为藏宝图可能是在大地图上偷到／拿到的。
func _apply_conditional_layer() -> void:
	if _conditional_layer == null:
		return
	_conditional_layer.visible = WorldMapServiceScript.conditional_layer_visible(
		db, current_state(), _landmark_node_ids()
	)


## 这张图上真的摆了位点的地标（条件地表层看的是它们的 `unlock_condition`）
func _landmark_node_ids() -> PackedStringArray:
	var out := PackedStringArray()
	if world == null:
		return out
	for row: Resource in db.rows("map_region"):
		if world.get_node_or_null("Markers/Node_%s" % str(row.node_id)) != null:
			out.append(str(row.node_id))
	return out


## 按揭雾/锁定状态更新图标（揭开一个新地标后要能立刻看到）
func refresh_node_icons() -> void:
	if world_map == null:
		return
	for row: Resource in db.rows("map_region"):
		var node_id := str(row.node_id)
		var sprite: Sprite2D = _node_icons.get(node_id)
		if sprite == null:
			continue
		var revealed: bool = world_map.is_revealed(node_id)
		var locked: bool = WorldMapServiceScript.LOCKED_SCENES.has(str(row.enter_scene))
		var icon_id := str(row.icon)
		if icon_id.is_empty() or not revealed:
			sprite.visible = false
			continue
		var file := "%s%s%s.png" % [ICON_DIR, icon_id, "_dim" if locked else ""]
		if not ResourceLoader.exists(file):
			# 暗版缺失就退回正常版：少一层状态，但不至于什么都不画
			file = "%s%s.png" % [ICON_DIR, icon_id]
		if ResourceLoader.exists(file):
			sprite.texture = load(file)
			sprite.visible = true
		else:
			# **两版都没有**：这个地标会"什么都不画"——玩家只会觉得这里本来就没图标。
			# 出声（每个地标只报一次）：数据里 icon 写错、或美术的图没进仓库，都该看得见。
			if not _icon_missing_warned.has(node_id):
				_icon_missing_warned[node_id] = true
				push_error("[Overworld] 地标 %s 的图标 %s 两张贴图都不存在（%s）" % [node_id, icon_id, ICON_DIR])
	_update_highlight()
	refresh_node_labels()


## 地标名字：图标之外再写一行字（`map_region.name_cn`），玩家不用挨个走进去才知道那是哪。
## 规则与图标一致：**没揭开的没有名字**（提前把地名写出来就是剧透）；
## 本章去不了的（锁定）用暗色字，和 `_dim` 图标一个口径。
##
## **2026-10-04（0.31.2）解耦**：名字**只看"揭没揭开"**，不再看 `map_region.icon` 有没有值。
## 以前这两件事绑在一起（`if str(row.icon).is_empty(): continue`），于是设计侧按 16 §3.5
## 「兴趣点不给图标」清空那四个 `icon` 时，**它们的名字会跟着一起消失**——两条本来无关的规则被一个条件拴住了。
func _build_node_labels() -> void:
	for row: Resource in db.rows("map_region"):
		var node_id := str(row.node_id)
		var marker: Node2D = world.get_node_or_null("Markers/Node_%s" % node_id)
		if marker == null:
			continue
		var label := Label.new()
		label.name = "NodeLabel_%s" % node_id
		label.text = str(row.name_cn)
		label.visible = false
		label.custom_minimum_size = Vector2(140, 0)
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.position = marker.position + ICON_OFFSET + Vector2(-70, 16)
		label.add_theme_font_size_override("font_size", 12)
		label.add_theme_color_override("font_color", Color(1, 1, 1, 0.92))
		label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.75))
		_add_to_world(ysort_node(), label)
		_node_labels[node_id] = label
	refresh_node_labels()


func refresh_node_labels() -> void:
	if world_map == null:
		return
	for row: Resource in db.rows("map_region"):
		var node_id := str(row.node_id)
		var label: Label = _node_labels.get(node_id)
		if label == null:
			continue
		# 名字的条件**只有一条**：揭没揭开（与 `icon` 解耦，见 `_build_node_labels` 的注释）
		if not world_map.is_revealed(node_id):
			label.visible = false
			continue
		label.text = str(row.name_cn)
		label.visible = true
		# 本章去不了的：字压暗（和 _dim 图标同一口径，不加额外文字免得挡住地图）
		label.modulate = Color(0.62, 0.62, 0.62) if WorldMapServiceScript.LOCKED_SCENES.has(str(row.enter_scene)) else Color.WHITE
	# 指路牌同理：跟着「这个地标揭没揭开」走（没揭开就不知道通往哪儿）
	for node_id: String in _signpost_labels.keys():
		(_signpost_labels[node_id] as Label).visible = world_map.is_revealed(node_id)


## 指路牌（设计 0.31.2）：`map_region.signpost_cn` 非空的行，代码在**地编摆的 `Markers/Sign_<node_id>`**
## 位点旁边把那句话摆出来。**文案只在表里**——不许在代码里拼「→ 多少里」这类句子；
## 表里空 = 不摆（今天 8 行都空着，等地编写文案）。没摆位点时静默跳过，
## 「有文案却没有牌子」由 `tests/test_map_assets.gd` 点名。
func _build_signposts() -> void:
	for row: Resource in db.rows("map_region"):
		var text := str(row.signpost_cn).strip_edges()
		if text.is_empty():
			continue
		var node_id := str(row.node_id)
		var marker: Node2D = world.get_node_or_null("Markers/Sign_%s" % node_id)
		if marker == null:
			continue
		var label := Label.new()
		label.name = "SignLabel_%s" % node_id
		label.text = text
		label.visible = false
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.position = marker.position + Vector2(-70, -20)
		label.add_theme_font_size_override("font_size", 12)
		label.add_theme_color_override("font_color", Color(1, 1, 1, 0.92))
		label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.75))
		_add_to_world(ysort_node(), label)
		_signpost_labels[node_id] = label
	refresh_node_labels()


## 高亮：离玩家最近的**已揭开**地标叠一层 icon_highlight（告诉玩家「你在这个地标附近」）
func _update_highlight() -> void:
	if _highlight == null or not is_instance_valid(_highlight):
		return
	if player == null:
		_highlight.visible = false
		return
	var best: Sprite2D = null
	var best_distance := INF
	for node_id: String in _node_icons:
		var sprite: Sprite2D = _node_icons[node_id]
		if sprite == null or not sprite.visible:
			continue
		var distance: float = sprite.global_position.distance_to(player.global_position)
		if distance < best_distance:
			best_distance = distance
			best = sprite
	# 只有凑得够近才算「就在这个地标旁」，免得全图都亮着
	var shown: bool = best != null and best_distance <= HIGHLIGHT_DISTANCE
	_highlight.visible = shown
	if shown:
		_highlight.global_position = best.global_position


## 地标图标（测试与截图读它）
func node_icon(node_id: String) -> Sprite2D:
	return _node_icons.get(node_id)


## 脚下的高亮现在亮不亮（用例读它）
func highlight_visible() -> bool:
	return _highlight != null and is_instance_valid(_highlight) and _highlight.visible


func _build_status_label() -> void:
	_status = Label.new()
	_status.name = "Status"
	_status.position = Vector2(12, 8)
	_status.add_theme_color_override("font_color", Color(1, 1, 1, 0.9))
	_status.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	_status.add_theme_constant_override("shadow_offset_x", 1)
	_status.add_theme_constant_override("shadow_offset_y", 1)
	_hud_layer().add_child(_status)
	# 第三行（状态栏 y=8、揭雾进度 y=34 各占一行）——放 30 会和 MapProgress 叠在一起
	_field_label = FieldBuffHudScript.build_label(Vector2(12, 56))
	_hud_layer().add_child(_field_label)


func _refresh_status() -> void:
	if _status == null:
		return
	var region := _region_name()
	# 线索本 K 必须写出来：设计 03 要求「线索必须能被找到」，藏着一个键等于没有
	_status.text = "%s　移动 WASD／方向键　潜行 Shift　交互 E　看人 Q　角色 Tab　行囊 I　线索 K　返回 Esc" % region
	_refresh_field_buffs()


## 战斗外增益（08）：剩余分钟随时间走，所以按秒刷一次；没有增益时这行是空的（不占视觉）
func _refresh_field_buffs() -> void:
	if _field_label == null:
		return
	var session_node = session()
	var rows: Array = session_node.active_field_buffs() if session_node != null else []
	_field_label.text = FieldBuffHudScript.text_of(db, rows)


## 大地图「揭雾」HUD：已探索几个地标、还剩哪些没走到
func _build_map_label() -> void:
	_map_label = Label.new()
	_map_label.name = "MapProgress"
	_map_label.position = Vector2(12, 34)
	_map_label.add_theme_color_override("font_color", Color("8ab4f8"))
	_map_label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	_map_label.add_theme_constant_override("shadow_offset_x", 1)
	_map_label.add_theme_constant_override("shadow_offset_y", 1)
	_hud_layer().add_child(_map_label)
	_refresh_map_label()


## 开局引导（设计 09 §3.1）：大地图上也常驻一行「当前目标」（状态栏 8／揭雾 34／增益 56 之后的第四行）
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


## 随机事件的提示行（设计 19 §四／0.25.1）：**只在事件存活期间出现**，
## 文案取 `world_event.prompt_text_cn`（模糊的方向性提示，例「远处有车马声」）——
## HUD 不说「商队」只说「车马声」，保留探索感。
func _build_event_label() -> void:
	_event_label = Label.new()
	_event_label.name = "WorldEventPrompt"
	_event_label.position = Vector2(12, 104)
	_event_label.add_theme_color_override("font_color", Color("ffd479"))
	_event_label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	_event_label.add_theme_constant_override("shadow_offset_x", 1)
	_event_label.add_theme_constant_override("shadow_offset_y", 1)
	_hud_layer().add_child(_event_label)
	_refresh_event_prompt()


func _refresh_guide() -> void:
	if _guide_label == null:
		return
	_guide_label.text = GuideServiceScript.hud_text(db, current_state())


## 自检／用例读这一行的文案
func guide_text() -> String:
	return _guide_label.text if _guide_label != null else ""


## 剧情招募（09 §3.2）里 `join_scene` 指向**区域地标**的那几位：走到跟前就入队。
## 数据能表达的粒度是「区域」，所以这里按「玩家与 `Markers/Node_<node_id>` 的距离」判定；
## 要不要改成范围触发（而不是踩到地标）由设计定，已登记 `待策划确认.md` Q51。
func _check_region_recruits() -> Array:
	if player == null or world_map == null:
		return []
	var joined: Array = []
	for row: Resource in db.rows("map_region"):
		var node_id := str(row.node_id)
		var marker: Node2D = world.get_node_or_null("Markers/Node_%s" % node_id)
		if marker == null:
			continue
		if marker.position.distance_to(player.position) > REGION_JOIN_DISTANCE:
			continue
		var results: Array = RecruitServiceScript.join_all_for_region(db, current_state(), node_id)
		if results.is_empty():
			continue
		joined.append_array(results)
	if joined.is_empty():
		return joined
	var names := PackedStringArray()
	for entry: Dictionary in joined:
		names.append(str(entry["name_cn"]))
	_refresh_guide()
	# 同小地图那一处：存档失败时把结果并进同一行，别把「谁加入了队伍」盖掉
	var saved: Dictionary = autosave("剧情招募")
	var text := "%s 加入了队伍" % "、".join(names)
	if not bool(saved.get("ok", false)):
		text += "　（自动存档失败：%s，这段进度只在本局里）" % str(saved.get("error", "未知原因"))
	_set_status(text)
	return joined


## HUD 必须挂在 CanvasLayer 上：挂在世界节点上的话会跟着相机跑出屏幕（踩过）
func _hud_layer() -> CanvasLayer:
	if _hud != null and is_instance_valid(_hud):
		return _hud
	_hud = CanvasLayer.new()
	_hud.name = "HUD"
	add_child(_hud)
	return _hud


func _refresh_map_label() -> void:
	if _map_label == null or world_map == null:
		return
	_map_label.text = world_map.progress_text()


func map_progress_text() -> String:
	return _map_label.text if _map_label != null else ""


func _region_name() -> String:
	var row: Resource = db.get_row("map_region", START_NODE)
	if row == null:
		return ""
	# **大区名来自表**（`map_region.region_name_cn`，设计 11 §三／14 §六，0.31.1）——
	# 以前这里写的是代码兜底串 `else "江南道·东部"`，违反「文案在表里」（见 `待策划确认.md` 实机反馈 ③）。
	var from_table := str(row.region_name_cn).strip_edges()
	if not from_table.is_empty():
		return from_table
	# 表里还没填时退回节点名：**不编中文**（宁可显示节点名，也不在代码里造一个地区名）
	return str(row.name_cn)


# ------------------------------------------------------------------ 帧循环

func _process(_delta: float) -> void:
	if camera != null and player != null:
		camera.global_position = player.global_position
	if player == null:
		return
	_check_reveals()
	_check_portal()
	# 条件地表（石隙那条细径）跟背包走：藏宝图可能是在这张图上偷到／拿到的
	_apply_conditional_layer()
	_track_sneak()
	_update_highlight()
	# 剧情招募里那几位 `join_scene` 指向区域地标的（现例：林铁山在落雁坡）：
	# 走到地标跟前就算「遇上了」，和脚下的高亮用同一个半径。
	_check_region_recruits()
	# 剧情节点同理：**走进新区域时再领一次**（21 §九 那条区域上的本命机遇就靠它，决策 345）
	_track_region_for_story()
	# 战斗外增益的剩余分钟按现实时间走：每秒刷一次就够（不必每帧重算文案）
	_field_refresh_timer -= _delta
	if _field_refresh_timer <= 0.0:
		_field_refresh_timer = 1.0
		_refresh_field_buffs()
	# 随机事件（设计 19 §四）：每帧推进存活计时／接触判定（很轻，只有一两个判断）
	_update_world_event(_delta)


# ---------------------------------------------------------------- 随机事件

## 随机事件的生命周期（设计 19 §四 ＋ 0.25.1 的提示两条规则）：
## 冷却到点 → 在当前区域按权重抽一条 → 在玩家附近放一个**临时淡色光点** ＋ HUD 一行模糊提示
## → 走上去触发／超时或走远消失。**同一时刻最多一条**（取最近的），
## 「错过了」是玩家的选择而不是系统的疏忽。
func _update_world_event(delta: float) -> void:
	if _event_cooldown > 0.0:
		_event_cooldown = maxf(0.0, _event_cooldown - delta)
	if _event_row == null:
		# 冷却好了、附近又没别的交互位点时才考虑刷一个（别在事件判定/驿站门口刷）
		if _event_cooldown <= 0.0 and player != null and event_near_player().is_empty() \
				and not post_station_near():
			_try_spawn_world_event()
		return
	_event_timer -= delta
	var gone := _event_timer <= 0.0
	if _event_marker != null and is_instance_valid(_event_marker) and player != null:
		var distance: float = player.position.distance_to(_event_marker.position)
		if distance <= WorldEventServiceScript.TRIGGER_DISTANCE:
			_trigger_world_event()
			return
		if distance > WorldEventServiceScript.DESPAWN_DISTANCE:
			gone = true
	if gone:
		_despawn_world_event()


## 当前区域（事件池按区域给：商队走官道、弟子在野外）。
func current_region_id() -> String:
	if player == null or world == null:
		return ""
	var best := ""
	var best_distance := 1e9
	for row: Resource in db.rows("map_region"):
		var marker: Node2D = world.get_node_or_null("Markers/Node_%s" % str(row.node_id))
		if marker == null:
			continue
		var distance: float = player.position.distance_to(marker.position)
		if distance < best_distance:
			best_distance = distance
			best = str(row.node_id)
	return best


func _try_spawn_world_event() -> bool:
	var region_id := current_region_id()
	if region_id.is_empty():
		return false
	if WorldEventServiceScript.eligible(db, current_state(), region_id).is_empty():
		_event_cooldown = WorldEventServiceScript.COOLDOWN
		return false
	if _event_rng == null:
		_event_rng = RngServiceScript.new()
	var row: Resource = WorldEventServiceScript.pick(
		db, current_state(), region_id, float(_event_rng.randf())
	)
	if row == null:
		return false
	_event_row = row
	_event_timer = WorldEventServiceScript.LIFETIME
	_place_event_marker()
	_refresh_event_prompt()
	return true


## 临时标记：比地标小、淡色、会随事件消失（0.25.1 的「一明一暗两处提示」里明的那一处）。
func _place_event_marker() -> void:
	if world == null or player == null:
		return
	var span := WorldEventServiceScript.SPAWN_MAX - WorldEventServiceScript.SPAWN_MIN
	var angle: float = float(_event_rng.randf()) * TAU if _event_rng != null else 0.0
	var distance: float = WorldEventServiceScript.SPAWN_MIN + span * 0.5
	var spot: Vector2 = player.position + Vector2(cos(angle), sin(angle)) * distance
	var marker := Polygon2D.new()
	marker.name = "WorldEventMark"
	# 淡色光点：菱形，不抢地标的视线
	marker.polygon = PackedVector2Array([
		Vector2(0, -7), Vector2(7, 0), Vector2(0, 7), Vector2(-7, 0),
	])
	marker.color = Color(1.0, 0.83, 0.47, 0.55)
	marker.position = spot
	marker.z_index = -1
	_add_to_world(ysort_node(), marker)
	_event_marker = marker


func _refresh_event_prompt() -> void:
	if _event_label == null:
		return
	if _event_row == null:
		_event_label.text = ""
		return
	# HUD 只说模糊的那一句（「远处有车马声」），不说「商队」——保留探索感
	_event_label.text = "（附近有动静）%s" % str(_event_row.prompt_text_cn)


func _despawn_world_event() -> void:
	if _event_marker != null and is_instance_valid(_event_marker):
		_event_marker.queue_free()
	_event_marker = null
	_event_row = null
	_event_timer = 0.0
	_event_cooldown = WorldEventServiceScript.COOLDOWN
	_refresh_event_prompt()


## 走到光点上 → 触发。一次性事件记账在 `WorldEventService` 里。
##
## 效果分两半：能纯逻辑结算的（物品／线索／判定）在服务层就结了；要挂在场景上的
## （开货架、切战斗）由 `_apply_world_event_effect()` 动手——**只有它真的没配上时**，
## 才补一句「效果还没接上」。
func _trigger_world_event() -> Dictionary:
	if _event_row == null:
		return {"ok": false, "error": "没有活着的事件"}
	var event_id := str(_event_row.event_id)
	var result: Dictionary = WorldEventServiceScript.trigger(db, current_state(), event_id)
	var effect: Dictionary = result.get("effect", {})
	var text := str(result.get("text", ""))
	var acted: Dictionary = _apply_world_event_effect(event_id, effect)
	if bool(acted.get("ok", true)):
		var effect_text := str(effect.get("text", ""))
		if not effect_text.is_empty():
			text += "　%s" % effect_text
		var acted_text := str(acted.get("text", ""))
		if not acted_text.is_empty():
			text += "（%s）" % acted_text
	elif not str(acted.get("error", "")).is_empty():
		text += "（%s）" % str(acted["error"])
	var wired: bool = (
		bool(effect.get("applied", false))
		or not str(effect.get("scene_action", "")).is_empty()
		or not str(effect.get("text", "")).is_empty()
	)
	if not wired and not str(effect.get("note", "")).is_empty():
		# 效果那半没配时**如实说**，但**不把开发用的说明（含表名/列名）甩给玩家**——
		# 玩家可见文案不许出现表内 id（CopyGuard 当场会点名）。细节留在 `effect.note` 里给日志与用例。
		text += "（这条事件的效果还没接上，先记下这段经过）"
	_set_status(text)
	_despawn_world_event()
	result["scene_effect"] = acted
	return result


## 事件效果的**场景那一半**（0.28.0 Q64）。
##
## `trade` 就地开行商货架（`effect_id` 是**货架组**，靠 `ShopService` 找对应的店）；
## `spar` 与判定奖励的 Boss 走既有的两条开战通道。返回 {ok, text, error}——
## `text` 是补在事件文案后面的**玩家可见**短句，`error` 只在真的做不成时非空。
func _apply_world_event_effect(event_id: String, effect: Dictionary) -> Dictionary:
	var action := str(effect.get("scene_action", ""))
	match action:
		"open_shop":
			var building_id: String = ShopServiceScript.building_for_shop_group(db, str(effect.get("id", "")))
			if building_id.is_empty():
				return {"ok": false, "text": "", "error": "这批货暂时支不起摊子"}
			var opened: Dictionary = open_shop(building_id)
			if not bool(opened.get("ok", false)):
				return {"ok": false, "text": "", "error": "货担没能摆开"}
			return {"ok": true, "text": "就地摆开了货担", "error": ""}
		"start_battle":
			var source_key := "world_event_%s" % event_id
			var enemy_id := str(effect.get("battle_enemy_id", ""))
			if not enemy_id.is_empty():
				_start_boss_encounter(enemy_id, source_key)
				return {"ok": true, "text": "来者不善", "error": ""}
			var started: Dictionary = _start_team_battle(str(effect.get("id", "")), source_key)
			if not bool(started.get("ok", false)):
				return {"ok": false, "text": "", "error": "对方没接这场切磋"}
			return {"ok": true, "text": "两边摆开了架势", "error": ""}
		_:
			return {"ok": true, "text": "", "error": ""}


func world_event_active_id() -> String:
	return str(_event_row.event_id) if _event_row != null else ""


## 探针用：直接放一个事件（用例固定内容，避免依赖随机）
func force_world_event(event_id: String, offset: Vector2 = Vector2(64, 0)) -> bool:
	var row: Resource = db.get_row("world_event", event_id)
	if row == null or player == null:
		return false
	_event_row = row
	_event_timer = WorldEventServiceScript.LIFETIME
	_event_cooldown = 0.0
	if _event_marker != null and is_instance_valid(_event_marker):
		_event_marker.queue_free()
	var marker := Polygon2D.new()
	marker.name = "WorldEventMark"
	marker.polygon = PackedVector2Array([
		Vector2(0, -7), Vector2(7, 0), Vector2(0, 7), Vector2(-7, 0),
	])
	marker.color = Color(1.0, 0.83, 0.47, 0.55)
	marker.position = player.position + offset
	_add_to_world(ysort_node(), marker)
	_event_marker = marker
	_refresh_event_prompt()
	return true


## 揭雾：proximity_N 走进去自动揭开、discover_X 在 X 揭开后跟着揭开，结果写进存档
func _check_reveals() -> void:
	if world_map == null:
		return
	var revealed: PackedStringArray = world_map.apply_proximity_reveals(Vector2(player.position), _node_positions())
	# 持有类（`item_<id>`）：拿到藏宝图这种就立刻揭开，不必等走到刷新点旁边
	revealed.append_array(world_map.apply_held_reveals())
	if revealed.is_empty():
		return
	_refresh_map_label()
	var names := PackedStringArray()
	for node_id: String in revealed:
		names.append(world_map.node_name(node_id))
		_clear_fog_around(node_id)
	# 揭开后地标图标要跟着出现（不然玩家走了半天地图上还是空的）
	refresh_node_icons()
	_set_status("揭开新区域：%s" % "、".join(names))


## 把已经揭开的地标周围的雾擦掉（地编在 overworld.tscn 里放了 `Fog` 图层）。
## 半径取该节点的 proximity 门槛（没有就用默认 6 格），这样「走进去才揭开」在画面上真的看得见。
func _apply_fog() -> void:
	if world_map == null:
		return
	for row: Resource in db.rows("map_region"):
		var node_id := str(row.node_id)
		if world_map.is_revealed(node_id):
			_clear_fog_around(node_id)


func _clear_fog_around(node_id: String, default_radius: int = 6) -> void:
	var fog: TileMapLayer = world.get_node_or_null("Fog")
	if fog == null:
		return
	var marker: Node2D = world.get_node_or_null("Markers/Node_%s" % node_id)
	if marker == null:
		return
	var radius := default_radius
	var row: Resource = db.get_row("map_region", node_id)
	if row != null:
		var condition := str(row.unlock_condition)
		if condition.begins_with("proximity_"):
			radius = maxi(default_radius, int(condition.substr("proximity_".length()).to_float()))
	var center := fog.local_to_map(fog.to_local(marker.global_position))
	for dx in range(-radius, radius + 1):
		for dy in range(-radius, radius + 1):
			if dx * dx + dy * dy > radius * radius:
				continue
			fog.erase_cell(Vector2i(center.x + dx, center.y + dy))


## 还剩多少格雾（自检与用例看这个数，能证明雾真的被擦掉了）
func fog_cells() -> int:
	var fog: TileMapLayer = world.get_node_or_null("Fog")
	return fog.get_used_cells().size() if fog != null else 0


## 所有 Node_<node_id> 位点的位置（揭雾的距离判定用）
func _node_positions() -> Dictionary:
	var out := {}
	for row: Resource in db.rows("map_region"):
		var node_id := str(row.node_id)
		var marker: Node2D = world.get_node_or_null("Markers/Node_%s" % node_id)
		if marker != null:
			out[node_id] = marker.position
	return out


## 潜行时「被发现」判定打折的比例（combat_const.sneak_detect_reduce）
func sneak_detect_reduce() -> float:
	var row: Resource = db.get_row("combat_const", "sneak_detect_reduce")
	return float(row.value) if row != null else 0.4


## 潜行状态变了就给一句提示（玩家得知道按住 Shift 到底有没有用）
func _track_sneak() -> void:
	if player == null:
		return
	var sneaking := bool(player.get("sneaking"))
	if sneaking == _was_sneaking:
		return
	_was_sneaking = sneaking
	if sneaking:
		_set_status("潜行中：移速 60%%，被发现判定 ×%.2f" % (1.0 - sneak_detect_reduce()))
	else:
		_refresh_status()


## 收集大地图上的事件判定位点（`Event_<check_id>`，按 region_id 归属）
func _collect_events() -> void:
	events = []
	if world == null:
		return
	var seen := {}
	for row: Resource in db.rows("event_check"):
		var check_id := str(row.check_id)
		if seen.has(check_id):
			continue
		var marker: Node2D = world.get_node_or_null("Markers/Event_%s" % check_id)
		if marker == null:
			continue
		seen[check_id] = true
		events.append({"check_id": check_id, "node": marker, "position": marker.global_position})


func event_service():
	if _event_service == null:
		_event_service = EventCheckServiceScript.new(db, current_state())
	return _event_service


## 玩家身边的事件位点
func event_near_player() -> String:
	if player == null:
		return ""
	var best := ""
	var best_distance := EVENT_DISTANCE
	for entry: Dictionary in events:
		var distance: float = player.position.distance_to(entry["position"])
		if distance <= best_distance:
			best = str(entry["check_id"])
			best_distance = distance
	return best


## 收集大地图上的观察点（`Observe_<point_id>`，表里填 `region_id` 的那几条）。
## 与判定位点一样挂在 `Markers/` 下；小地图那几条由 `local_map_controller` 收，
## 同一行不会两边都生效（表里 scene_id／region_id 二选一）。
func _collect_flavor_points() -> void:
	flavor_points = []
	if world == null:
		return
	for row: Resource in db.rows("flavor_point"):
		if str(row.region_id).is_empty():
			continue
		var point_id := str(row.point_id)
		var marker: Node2D = world.get_node_or_null(
			"Markers/%s" % FlavorPointScript.marker_name_of(point_id)
		)
		if marker == null:
			continue
		# **看得见**：观察点本身只是一根 Marker2D，挂一枚小暖色菱形（决策 337）。
		# 幂等——重复 setup() 不会挂第二枚（名字撞了 Godot 会自动改名，用例按名字找会出错）。
		if marker.get_node_or_null(FlavorMarkerScript.NODE_NAME) == null:
			marker.add_child(FlavorMarkerScript.new())
		flavor_points.append({"point_id": point_id, "node": marker, "position": marker.global_position})


## 玩家身边的观察点（最近的那个 point_id；没有就空串）
func flavor_near_player() -> String:
	if player == null:
		return ""
	var best := ""
	var best_distance := EVENT_DISTANCE
	for entry: Dictionary in flavor_points:
		var distance: float = player.position.distance_to(entry["position"])
		if distance <= best_distance:
			best = str(entry["point_id"])
			best_distance = distance
	return best


## 收大地图上的 NPC 站位（`Characters/npc_slot_0N` 或按 id 绑的 `npc_<npc_id>`，
## 与地编摆位点的命名一致；两种都收，见 `框架说明.md` 决策 332）。
func _collect_npc_slots() -> void:
	npcs = []
	var box: Node = world.get_node_or_null("Characters") if world != null else null
	if box == null:
		return
	for child in box.get_children():
		if child is Node2D and str(child.name).begins_with(NpcServiceScript.SLOT_PREFIX):
			npcs.append(child)


## 玩家身边的 NPC 位点（最近的那个；没有就 null）
func npc_near_player() -> Node2D:
	if player == null:
		return null
	var best: Node2D = null
	var best_distance := NPC_DISTANCE
	for slot in npcs:
		if not instance_valid(slot):
			continue
		var distance: float = (slot as Node2D).global_position.distance_to(player.position)
		if distance <= best_distance:
			best = slot
			best_distance = distance
	return best


func instance_valid(node) -> bool:
	return node != null and is_instance_valid(node)


## 位点 → 人：**两种命名都认**，判定只有一处（`NpcService.npc_for_slot`）——
## 按 id 绑的 `npc_<npc_id>` 优先，占位命名 `npc_slot_0N` 按**编号顺序**对上**当前区域**的 `npc_def` 顺序。
##
## 区域取「最近的地标」（`current_region_id()`，与随机事件同一份判定）——
## 老周的 `place_id` 就是 `n_luoyanpo`，与 `NpcService.npcs_at()` 的两套地点口径能对上。
func _npc_for_slot(slot_id: String) -> String:
	return NpcServiceScript.npc_for_slot(db, slot_id, current_region_id())


## 按 E 跟人说话：开交往面板（设计 19 §三）。内容还没到的人**不静默**——
## `npc_panel` 会如实写「这个人还没有配 npc_def 行」。
func _talk_to_npc(slot: Node2D) -> Dictionary:
	var npc_id := _npc_for_slot(str(slot.name))
	if npc_id.is_empty():
		# 与 `local_map_controller` 同一句话：**表名不进玩家可见文案**（决策 329）。
		push_error("[overworld] NPC 站位 %s 按编号顺序取不到 npc_def 行（本区域只有 %d 个人）"
				% [str(slot.name), NpcServiceScript.npcs_at(db, current_region_id()).size()])
		var text := "眼下没什么可说的（这个人还没配台词）"
		_set_status("%s：%s" % [NPC_SPEAKER, text])
		return {"ok": true, "npc": str(slot.name), "speaker": NPC_SPEAKER, "text": text}
	return open_npc(npc_id)


## 开 NPC 交往面板：`mode` 与设计 0.28.0 的 Q62 一致（E 进交互菜单、Q 只读看信息）
func open_npc(npc_id: String, mode: String = NpcPanelMode.MODE_INTERACT) -> Dictionary:
	if npc_panel != null and is_instance_valid(npc_panel):
		_push_overlay("npc")
		npc_panel.state_override = current_state()
		npc_panel.npc_id = npc_id
		npc_panel.mode = mode
		npc_panel.refresh()
		return {"ok": true, "error": "", "reopened": true}
	var panel = load(NPC_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "NPC 面板加载失败"}
	panel.name = "NpcPanel"
	panel.state_override = current_state()
	panel.npc_id = npc_id
	panel.mode = mode
	panel.return_handler = func() -> void: close_npc()
	# 同伴的交往入口现在在角色面板（Q67 拍板 ④），而角色面板在大地图上也能开——
	# 所以大地图这一侧同样要把切磋接上，否则那一按会落到「这里不能切磋」。
	panel.spar_handler = func(who: String, team: String) -> void: _start_spar(who, team)
	OverlayStackScript.mount(self, panel)
	panel.setup()
	npc_panel = panel
	_push_overlay("npc")
	return {"ok": true, "error": "", "reopened": false}


func close_npc() -> void:
	_close_overlay("npc")


## 切磋（与 `local_map_controller._start_spar` 同一套口径）：空队伍 id = **打他自己的镜像**
## （同伴，Q88 拍板 ①）；有队伍 id 就走表里那支队伍。
func _start_spar(npc_id: String, team_id: String) -> Dictionary:
	var team: Resource = db.get_row("enemy_team", team_id) if not team_id.is_empty() else null
	if not team_id.is_empty() and team == null:
		push_error("[overworld] 切磋队伍不存在：enemy_team 缺少 %s（npc %s）" % [team_id, npc_id])
		_set_status("切磋对手的配置对不上（数据错，已记进日志）")
		return {"ok": false, "error": "no_team"}
	var data := {
		"spawn_id": "spar_%s" % npc_id,
		"source_scene": current_region_id(),
		"source_key": "spar_%s" % npc_id,
		"team_id": team_id,
		"is_elite": false,
	}
	if team_id.is_empty():
		data["mirror_char"] = npc_id
	var encounter = EncounterScript.build(
		db, data, team, EncounterScript.CONTACT_FRONT, str(current_state().difficulty_id))
	if team_id.is_empty():
		var person: Resource = NpcServiceScript.person_of(db, current_state(), npc_id)
		encounter.team_name = "%s的镜像" % (str(person.name_cn) if person != null else npc_id)
	encounter.spar_npc = npc_id
	close_npc()
	if battle_switch_handler.is_valid():
		battle_switch_handler.call(encounter)
	else:
		_change_scene(BATTLE_SCENE)
	return {"ok": true, "team_id": team_id, "npc_id": npc_id}


## 看一个观察点：只出一句碎句——不发奖励、不锁任何路（设计 20 §3.2）
##
## **0.32.0 补**：读过顺手记一枚 `flag_obs_<point_id>`。大地图上的观察点（落雁坡车辙那条）
## 正是幕二「免战」选项的前置来源——不补这条，那枚旗标全项目没有出处，选项就是死的。
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


## 走一次事件判定：判定 → 奖励／失败说明；「指向某地」的奖励会顺带揭开地标
func resolve_event(check_id: String) -> Dictionary:
	var result: Dictionary = event_service().resolve(check_id)
	_set_status(str(result["text"]))
	_refresh_map_label()
	var reward: Dictionary = result.get("reward", {})
	if bool(result["success"]) and bool(reward.get("start_battle", false)):
		# `reward_type=boss`（08／06）：判定通过 → Boss 现身开战。
		# 大地图这边与明雷遭遇共用同一套「写会话 + 切战斗场景」的收尾（见 `_on_encountered`）。
		_start_boss_encounter(str(reward.get("id", "")), check_id)
	return result


## 事件触发的团队战（Q64 的「切磋」：`we_disciple` → `team_wanderer_disciple`）。
##
## 与明雷那次（`_on_encountered`）同一口径，只是没有刷新点行——队伍直接从
## `enemy_team` 取，`source_key` 记成事件名（不进副本完成度、不参与明雷刷新）。
## `is_elite=false`：切磋不是精英战，掉落与首杀都不该按精英走。
func _start_team_battle(team_id: String, source_key: String) -> Dictionary:
	var team: Resource = db.get_row("enemy_team", team_id)
	if team == null:
		push_error("[Overworld] 事件的队伍不在表里：%s" % team_id)
		return {"ok": false, "error": "no_team"}
	var state = current_state()
	var difficulty := str(state.difficulty_id) if state != null else "normal"
	var encounter = EncounterScript.build(db, {
		"spawn_id": source_key,
		"source_scene": "overworld",
		"source_key": source_key,
		"team_id": team_id,
		"is_elite": false,
	}, team, EncounterScript.CONTACT_FRONT, difficulty)
	var session_node = session()
	if session_node != null:
		session_node.pending_encounter = encounter
		session_node.set_world_position(player.position if player != null else Vector2.ZERO, current_state())
	_set_status(encounter.headline())
	if battle_switch_handler.is_valid():
		battle_switch_handler.call(encounter)
		return {"ok": true, "team_id": team_id, "error": ""}
	# 正式游玩时没有人接管：直接切战斗场景（与明雷那条 `_on_encountered` 同一口径）
	_change_scene(BATTLE_SCENE)
	return {"ok": true, "team_id": team_id, "error": ""}


## 事件判定触发的 Boss 战：单人一支队，构造与「小地图隐藏 Boss」同一口径
## （`team_hidden_<enemy>` / contact=front / 难度取当前存档）。
func _start_boss_encounter(enemy_id: String, source_key: String) -> void:
	var enemy: Resource = db.get_row("enemy_base", enemy_id)
	if enemy == null:
		push_error("[Overworld] Boss 奖励指向的敌人不存在：%s" % enemy_id)
		return
	var state = current_state()
	var difficulty := str(state.difficulty_id) if state != null else "normal"
	var encounter = EncounterScript.build(db, {
		"spawn_id": source_key,
		"source_scene": "overworld",
		"source_key": source_key,
		"team_id": "team_hidden_%s" % enemy_id,
		"is_elite": true,
	}, {
		"name_cn": str(enemy.name_cn),
		"threat_tag": str(enemy.threat_tag),
		"members": "%s:1" % enemy_id,
	}, EncounterScript.CONTACT_FRONT, difficulty)
	var session_node = session()
	if session_node != null:
		session_node.pending_encounter = encounter
		session_node.set_world_position(player.position if player != null else Vector2.ZERO, current_state())
	_set_status(encounter.headline())
	if battle_switch_handler.is_valid():
		battle_switch_handler.call(encounter)
		return
	# 以前这里**没有兜底**：正式游玩时没人接管 `battle_switch_handler`，于是
	# 「判定过了 → Boss 现身」只会改一行状态文字，战斗永远不开（用例把 handler 接上了，
	# 所以这一层只看得到 0.28.0 接 Q64 的 `we_patrol` 时才暴露）。
	_change_scene(BATTLE_SCENE)


## 收集大地图上的入口位点（Portal_<scene_id>），以及进入门槛
func _collect_portals() -> void:
	_portals = []
	for row: Resource in db.rows("map_local"):
		var scene_id := str(row.scene_id)
		var marker: Node2D = world.get_node_or_null("Markers/Portal_%s" % scene_id)
		if marker == null:
			continue
		var region: Resource = db.get_row("map_region", str(row.parent_node))
		_portals.append({
			"scene_id": scene_id,
			"name": str(row.name_cn),
			"position": marker.position,
			"condition": str(region.unlock_condition) if region != null else "default",
		})


func _check_portal() -> void:
	# 刚回图（或刚开局）时人可能正站在入口位点上——那时候不进图，先离开圈外再武装。
	# 这条和 `PORTAL_ARM_MARGIN` 一起，专治「小地图出来立刻又被送回小地图」。
	if not _portal_armed:
		var nearest := 1.0e9
		for portal: Dictionary in _portals:
			nearest = minf(nearest, player.position.distance_to(portal["position"]))
		if nearest > PORTAL_DISTANCE + PORTAL_ARM_MARGIN:
			_portal_armed = true
		return
	for portal: Dictionary in _portals:
		if player.position.distance_to(portal["position"]) > PORTAL_DISTANCE:
			continue
		if WorldMapServiceScript.LOCKED_SCENES.has(str(portal["scene_id"])):
			_set_status("%s：本章不可前往" % portal["name"])
			return
		if not _portal_unlocked(portal):
			_set_status("%s：还没发现这里（先在落雁坡转转）" % portal["name"])
			return
		enter_local_map(str(portal["scene_id"]))
		return


func _portal_unlocked(portal: Dictionary) -> bool:
	var condition := str(portal["condition"])
	if condition.is_empty() or condition == "default":
		return true
	# discover_luoyanpo → 先在落雁坡露过面；proximity_* 只影响地图揭开
	if condition.begins_with("discover_"):
		return world_map != null and world_map.discovery_target_revealed(condition.substr("discover_".length()))
	return true


## from_teleport=true 表示这次是**驿站传送**来的，不是走进去的。
##
## 区别在「返回大地图原位置」（设计 02）怎么理解：
##   走进门 → 原位置 = 你当时站的地方（= 入口旁边）；
##   传送过去 → 原位置应当是**目的地的地标**，否则从荒村出来会被一路拽回落雁坡的驿站
##   ——那正是「传送之前站的地方」。小地图里的「挖通」捷径也是这个口径（记目的地地标）。
func enter_local_map(scene_id: String, from_teleport: bool = false) -> void:
	var session_node = session()
	if session_node != null:
		session_node.pending_local_scene = scene_id
		# 从大地图进图一律走入口：清掉上一轮的落点与战斗前位置
		session_node.pending_local_room = ""
		session_node.local_position = Vector2.ZERO
		session_node.local_position_scene = ""
		if player != null:
			session_node.set_world_position(_landmark_position(scene_id) if from_teleport else player.position, current_state())
	# 设计 02：大地图在「切换小地图时」自动存档
	autosave("切换小地图")
	_change_scene(LOCAL_RUN)


## 某张小地图在大地图上的地标坐标（`map_local.parent_node` → `map_region.pos_*`）
func _landmark_position(scene_id: String) -> Vector2:
	var local_row: Resource = db.get_row("map_local", scene_id)
	if local_row == null:
		return Vector2.ZERO
	var region: Resource = db.get_row("map_region", str(local_row.parent_node))
	if region == null:
		return Vector2.ZERO
	return Vector2(float(region.pos_x), float(region.pos_y))


## 存档设施：注入优先，否则用默认目录
func save_service():
	if _save_service == null:
		var store = save_store_override
		if store == null:
			store = SaveServiceScript.make_default()
		_save_service = SaveServiceScript.new(store, current_state())
	return _save_service


## 自动存档（大地图切小地图时、战斗结算后由战斗界面调用）
func autosave(reason: String) -> Dictionary:
	var result: Dictionary = save_service().save(reason, true)
	# 失败要出声（与 `local_map_controller.autosave` 同一口径）：不打断游玩，但别让玩家以为存上了。
	if not bool(result.get("ok", false)):
		_set_status("自动存档失败：%s（这一段的进度只在本局里）" % str(result.get("error", "未知原因")))
	return result


func _unhandled_input(event: InputEvent) -> void:
	# 浮层栈（设计 18.1，与 `local_map_controller` 同一套口径）：
	# **全局快捷键在任何浮层里都可用**，Esc 一次只弹一层，栈空才回枢纽页。
	if event.is_action_pressed("ui_cancel"):
		if close_top_overlay():
			return
		_change_scene(PLACEHOLDER_SCENE)
		return
	if event.is_action_pressed("open_character"):
		open_character_overlay(0)
		return
	if event.is_action_pressed("open_bag"):
		open_character_overlay(2)
		return
	if event.is_action_pressed("show_clues"):
		open_clues()
		return
	# 看 NPC 信息（设计 0.28.0 的 Q62，与 `local_map_controller` 同一套）：Q 只读、E 才交互
	if event.is_action_pressed("npc_info"):
		var who := npc_near_player()
		# 与 `local_map_controller` 同一套：**失败也要出声**（见那边的注释，2026-10-04 实机反馈）
		if who == null:
			_set_status("这附近没有可以看的人（Q 是看人，E 才是搭话）")
			return
		var info_id := _npc_for_slot(str(who.name))
		if info_id.is_empty():
			_set_status("这个人还没配信息（位点 %s 没绑到表里）" % str(who.name))
			return
		open_npc(info_id, NpcPanelMode.MODE_INFO)
		return
	if overlays().depth() > 0:
		return
	if event.is_action_pressed("interact"):
		var check_id := event_near_player()
		if not check_id.is_empty():
			resolve_event(check_id)
		elif post_station_near():
			open_waypoint()
		elif npc_near_player() != null:
			_talk_to_npc(npc_near_player())
		elif not flavor_near_player().is_empty():
			read_flavor_point(flavor_near_player())
		else:
			_set_status("这里暂时没什么可交互的（走进地标可以进小地图）")


# ------------------------------------------------------------------ 浮层栈

func overlays():
	if _overlays == null:
		_overlays = OverlayStackScript.new()
	return _overlays


func _push_overlay(id: String) -> Array:
	var closed: Array = overlays().push(id)
	for closed_id: String in closed:
		_free_overlay(closed_id)
	return closed


func close_top_overlay() -> bool:
	var top_id: String = overlays().pop()
	if top_id.is_empty():
		return false
	_free_overlay(top_id)
	return true


func _close_overlay(id: String) -> void:
	for extra: String in overlays().remove(id):
		_free_overlay(extra)
	_free_overlay(id)
	# 与 `local_map_controller._close_overlay` 同一条口径（决策 347）：浮层里的动作（行商买药等）
	# 可能刚刚满足某条引导口径，关浮层时重算一次，别等下一次换图。
	GuideServiceScript.refresh_derived_flags(db, current_state())
	_refresh_guide()


func _free_overlay(id: String) -> void:
	match id:
		"clue":
			if clue_panel != null and is_instance_valid(clue_panel):
				clue_panel.queue_free()
			clue_panel = null
		"waypoint":
			if waypoint_panel != null and is_instance_valid(waypoint_panel):
				waypoint_panel.queue_free()
			waypoint_panel = null
		"shop":
			if shop_panel != null and is_instance_valid(shop_panel):
				shop_panel.queue_free()
			shop_panel = null
		"npc":
			if npc_panel != null and is_instance_valid(npc_panel):
				npc_panel.queue_free()
			npc_panel = null
		"character":
			var panel := find_child("CharacterPanel", true, false)
			if panel != null:
				panel.queue_free()
		_:
			pass


## 角色与行囊（Tab／I）：**浮层**，与大地图同屏——切场景会把驿站那一层丢掉
func open_character_overlay(tab_index: int = 0) -> Dictionary:
	if overlays().has("character"):
		_push_overlay("character")   # 已在栈里 → 弹回它
		var existing := find_child("CharacterPanel", true, false)
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
	# Q67 拍板 ④：同伴的交往入口在角色面板（同伴不站位）——这里把「谁」接到既有那条 `open_npc()`
	panel.npc_open_handler = func(npc_id: String) -> void: open_npc(npc_id)
	OverlayStackScript.mount(self, panel)
	panel.setup()
	panel.select_tab(tab_index)
	_push_overlay("character")
	return {"ok": true, "error": "", "reopened": false}


## 开线索本：野外事件按地标分组（在大地图上按 K）
func open_clues() -> Dictionary:
	if clue_panel != null and is_instance_valid(clue_panel):
		_push_overlay("clue")
		clue_panel.state_override = current_state()
		clue_panel.refresh()
		return {"ok": true, "error": "", "reopened": true}
	var panel = load(CLUE_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "线索本场景加载失败"}
	panel.name = "CluePanel"
	panel.state_override = current_state()
	panel.scope = "region"
	panel.return_handler = func() -> void: close_clues()
	OverlayStackScript.mount(self, panel)
	panel.setup()
	clue_panel = panel
	_push_overlay("clue")
	_set_status("线索本：五类都在里面；野外的可交互点按地标列在「隐藏」那一页（Esc 或点「离开」出来）")
	return {"ok": true, "error": "", "reopened": false}


func close_clues() -> void:
	_close_overlay("clue")


## 开货架：设计 19 §四的「货商车队」在野外就地开张（`bld_caravan` 没有地图位点）。
## 节点名与「谁卖什么」全走 `building_def`／`shop_stock`，这里只负责把界面盖上来。
func open_shop(building_id: String) -> Dictionary:
	var service = ShopServiceScript.new(db, current_state())
	if service.building(building_id) == null:
		return {"ok": false, "error": "这个货架不在表里", "building_id": building_id}
	if shop_panel != null and is_instance_valid(shop_panel):
		_push_overlay("shop")
		shop_panel.state_override = current_state()
		shop_panel.open_building(building_id)
		return {"ok": true, "error": "", "building_id": building_id, "reopened": true}
	var panel = load(SHOP_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "商店场景加载失败", "building_id": building_id}
	panel.name = "ShopPanel"
	panel.state_override = current_state()
	panel.building_id = building_id
	panel.return_handler = func() -> void: close_shop()
	OverlayStackScript.mount(self, panel)
	panel.setup()
	shop_panel = panel
	_push_overlay("shop")
	var welcome := "行商货担：货比城镇贵、还限量（Esc 或点「离开」出来）"
	panel.show_message(welcome)
	_set_status(welcome)
	return {"ok": true, "error": "", "building_id": building_id, "reopened": false}


func close_shop() -> void:
	_close_overlay("shop")
	_set_status("行商收起货担，继续赶路")


## 身边是不是驿站（`Markers/Node_n_post_station`）
func post_station_near() -> bool:
	if world == null or player == null:
		return false
	var marker: Node2D = world.get_node_or_null("Markers/Node_n_post_station")
	if marker == null:
		return false
	return player.position.distance_to(marker.position) <= POST_DISTANCE


## 开驿站界面：传送目标与难度切换都在这一个面板里
func open_waypoint() -> Dictionary:
	if waypoint_panel != null and is_instance_valid(waypoint_panel):
		_push_overlay("waypoint")
		waypoint_panel.state_override = current_state()
		waypoint_panel.refresh()
		return {"ok": true, "error": "", "reopened": true}
	var panel = load(WAYPOINT_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "驿站场景加载失败"}
	panel.name = "WaypointPanel"
	panel.state_override = current_state()
	panel.current_scene_id = ""
	panel.return_handler = func() -> void: close_waypoint()
	panel.travel_handler = func(scene_id: String, _name: String) -> void:
		close_waypoint()
		enter_local_map(scene_id, true)   # 传送：回程落点记目的地地标，别把人拽回驿站
	OverlayStackScript.mount(self, panel)
	panel.setup()
	waypoint_panel = panel
	_push_overlay("waypoint")
	var welcome := "驿站：可以传送到已探索的地标，也能在这里改难度（Esc 或点「离开」出来）"
	panel.show_message(welcome)
	_set_status(welcome)
	return {"ok": true, "error": "", "reopened": false}


func close_waypoint() -> void:
	_close_overlay("waypoint")
	_set_status("离开驿站")


# ------------------------------------------------------------------ 遭遇

func _on_encountered(spawn_id: String, contact: String) -> void:
	var row: Resource = db.get_row("roaming_spawn", spawn_id)
	if row == null:
		return
	var team: Resource = db.get_row("enemy_team", str(row.team_id))
	var state = current_state()
	var difficulty := str(state.difficulty_id) if state != null else "normal"
	var encounter = EncounterScript.build(db, row, team, contact, difficulty)
	var session_node = session()
	if session_node != null:
		session_node.pending_encounter = encounter
		session_node.set_world_position(player.position if player != null else Vector2.ZERO, current_state())
	_set_status(encounter.headline())
	if battle_switch_handler.is_valid():
		battle_switch_handler.call(encounter)
		return
	_change_scene(BATTLE_SCENE)


func _set_status(text: String) -> void:
	if _status != null:
		_status.text = text


# ------------------------------------------------------------------ 环境

func _resolve_db():
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var table_db = TableDbScript.new()
	table_db.load_all()
	return table_db


func _session_node(node_name: String) -> Node:
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
	# 用例可以接管切场景（否则会把测试的场景树真的换掉）
	if scene_change_handler.is_valid():
		scene_change_handler.call(path)
		return
	var tree := _tree()
	if tree != null:
		tree.change_scene_to_file(path)


func _has_user_arg(flag: String) -> bool:
	return OS.get_cmdline_user_args().has(flag)


# ------------------------------------------------------------------ 自检

## 真实场景自检：生成 → 撞明雷 → 触发遭遇（不切场景）
func _run_world_selftest() -> void:
	var ok := true
	var lines := PackedStringArray()
	ok = ok and enemies.size() == 17 and player != null and camera != null
	lines.append("生成：明雷 %d 个，玩家与相机就位=%s" % [enemies.size(), player != null and camera != null])
	# 0.8.1 的明雷开关：两种状态都验一遍（默认关、置 1 照旧），
	# 免得"关了半年再打开发现已经烂了"。
	var session_for_toggle = session()
	if session_for_toggle != null:
		session_for_toggle.roaming_enabled_override = 0
		rebuild_roaming_enemies()
		ok = ok and enemies.is_empty()
		lines.append("开关 0：明雷 %d 个（应为 0）" % enemies.size())
		session_for_toggle.roaming_enabled_override = 1
		rebuild_roaming_enemies()
		ok = ok and enemies.size() == 17
		lines.append("开关 1：明雷 %d 个（应为 17）" % enemies.size())

	var captured: Array = []
	battle_switch_handler = func(encounter) -> void: captured.append(encounter)
	var wolf = null
	for enemy in enemies:
		if enemy.spawn_id == "sp_lp_wolf_01":
			wolf = enemy
	if wolf == null:
		ok = false
		lines.append("找不到 sp_lp_wolf_01")
	else:
		# 朝向决定「正面／背后」，而游荡型的朝向每帧都在变 →
		# 必须按它当前的朝向摆位，否则自检会随机变成背袭（这里曾经 50% 概率红）。
		var facing: Vector2 = wolf.facing.normalized() if wolf.facing.length() > 0.001 else Vector2.DOWN
		player.global_position = wolf.global_position + facing * 8.0
		wolf.reset_latch()
		wolf._check_contact()
		var hit: bool = captured.size() == 1 and str(captured[0].contact) == "front"
		ok = ok and hit
		lines.append("撞明雷 ok=%s（%s）" % [hit, captured[0].headline() if captured.size() > 0 else "无"])

		# 再绕到它背后 → 背袭（同一支明雷重置后重触发）
		player.global_position = wolf.global_position - facing * 8.0
		wolf.reset_latch()
		wolf._check_contact()
		var back: bool = captured.size() == 2 and str(captured[1].contact) == "back"
		ok = ok and back
		lines.append("绕背偷袭 ok=%s（%s）" % [back, captured[1].headline() if captured.size() > 1 else "无"])

	# 背后贴沉睡的醉汉 → 奇袭
	var sleeper = null
	for enemy in enemies:
		if enemy.spawn_id == "sp_hc_sleeper":
			sleeper = enemy
	if sleeper != null:
		player.global_position = sleeper.global_position + Vector2(0, -8)
		sleeper.reset_latch()
		sleeper._check_contact()
		var ambush: bool = captured.size() == 3 and str(captured[2].contact) == "ambush_sleep"
		ok = ok and ambush
		lines.append("背袭沉睡 ok=%s" % ambush)

	var session_node = session()
	if session_node != null and not captured.is_empty():
		session_node.pending_encounter = captured[0]
		ok = ok and session_node.pending_encounter != null
		lines.append("遭遇已交给 GameSession 待处理")

	# 战斗外增益 HUD（08）：大地图上看得见打坐余韵还剩几分钟；清掉后这一行为空
	if session_node != null:
		session_node.clear_field_buffs()
		session_node.add_field_buff("buff_meditated", 10)
		_refresh_field_buffs()
		var hud_ok: bool = (
			_field_label != null
			and _field_label.text.contains("打坐余韵")
			and _field_label.text.contains("剩 10 分钟")
		)
		ok = ok and hud_ok
		lines.append("战斗外增益 HUD ok=%s（%s）" % [hud_ok, _field_label.text if _field_label != null else "-"])
		# 别和上面两行叠字：状态栏 y=8、揭雾进度 y=34，这一行必须在它们下面且不同 y
		var no_overlap: bool = (
			_field_label != null and _field_label.position.y > _status.position.y
			and (_map_label == null or _field_label.position.y != _map_label.position.y)
		)
		ok = ok and no_overlap
		lines.append("增益行不与状态栏／揭雾进度叠字=%s（y=%.0f）" % [no_overlap, _field_label.position.y if _field_label != null else -1.0])
		session_node.clear_field_buffs()
		_refresh_field_buffs()
		var cleared_ok: bool = _field_label != null and _field_label.text.is_empty()
		ok = ok and cleared_ok
		lines.append("清掉后 HUD 为空=%s" % cleared_ok)

	# 开局引导 HUD（09 §3.1）：大地图上也要常驻「当前目标」，并且与上面三行错开
	_refresh_guide()
	var guide_line := guide_text()
	var guide_ok: bool = guide_line.contains("当前目标")
	ok = ok and guide_ok
	lines.append("引导 HUD ok=%s（%s）" % [guide_ok, guide_line])
	var guide_no_overlap: bool = (
		_guide_label != null and _guide_label.position.y > _status.position.y
		and (_map_label == null or _guide_label.position.y > _map_label.position.y)
		and (_field_label == null or _guide_label.position.y > _field_label.position.y)
	)
	ok = ok and guide_no_overlap
	lines.append("引导行不与上面三行叠字=%s（y=%.0f）" % [guide_no_overlap, _guide_label.position.y if _guide_label != null else -1.0])

	# 浮层栈（设计 18.1）：驿站 → 角色（Tab）→ Esc 回驿站 → Esc 关驿站
	# 大地图上的 NPC（设计 19：`npc_def.place_id` 允许写区域节点）——
	# 老周就在落雁坡，按 E 应该能开他的交往面板；这条以前只在小地图里接，大地图上按 E 什么也不发生。
	var npc_ok := true
	npc_ok = npc_ok and npcs.size() >= 1
	if not npcs.is_empty():
		var slot: Node2D = npcs[0]
		player.position = slot.global_position
		npc_ok = npc_ok and npc_near_player() == slot
		npc_ok = npc_ok and current_region_id() == "n_luoyanpo"
		var npc_id := _npc_for_slot(str(slot.name))
		npc_ok = npc_ok and npc_id == "npc_caiyao"
		var opened: Dictionary = _talk_to_npc(slot)
		npc_ok = npc_ok and bool(opened.get("ok", false)) and npc_panel != null
		close_npc()
		npc_ok = npc_ok and npc_panel == null
		lines.append("大地图 NPC（落雁坡 %s 可交互）=%s" % [slot.name, npc_ok])
	else:
		lines.append("大地图 NPC：一个位点都没有（应为 1 个）")
	ok = ok and npc_ok

	var stack_ok := true
	open_waypoint()
	stack_ok = stack_ok and overlays().depth() == 1 and waypoint_panel != null
	open_character_overlay(0)
	stack_ok = stack_ok and overlays().depth() == 2 and waypoint_panel != null
	open_character_overlay(2)
	var char_panel := find_child("CharacterPanel", true, false)
	stack_ok = stack_ok and overlays().depth() == 2 and char_panel != null and char_panel.current_tab() == 2
	stack_ok = stack_ok and close_top_overlay() and overlays().depth() == 1 and waypoint_panel != null
	stack_ok = stack_ok and close_top_overlay() and overlays().is_empty() and waypoint_panel == null
	ok = ok and stack_ok
	lines.append("浮层栈（驿站→角色→Esc 回驿站→Esc 关驿站）=%s" % stack_ok)

	# 随机事件（设计 19 §四／0.25.1）：强制放一个 → HUD 出模糊提示 → 超时/走远消失
	# → 再放一个走上去触发 → 一次性事件记账。
	var event_ok := true
	event_ok = event_ok and force_world_event("we_lost_item", Vector2(64, 0))
	event_ok = event_ok and world_event_active_id() == "we_lost_item"
	event_ok = event_ok and _event_label != null and _event_label.text.contains("路边")
	lines.append("随机事件提示（HUD 模糊提示、不说事件名）=%s（%s）" % [event_ok, _event_label.text if _event_label != null else "-"])
	_event_timer = 0.01
	_update_world_event(0.1)
	event_ok = event_ok and world_event_active_id().is_empty()
	event_ok = event_ok and _event_label != null and _event_label.text.is_empty()
	lines.append("超时后光点与提示一起消失=%s" % event_ok)
	# 触发用一条**一次性**事件（`repeatable=0`）：这样能顺带验「发过就记账」。
	force_world_event("we_hermit", Vector2(8, 0))
	_update_world_event(0.1)
	event_ok = event_ok and world_event_active_id().is_empty()
	event_ok = event_ok and _status.text.contains("老人")
	event_ok = event_ok and current_state().has_flag(WorldEventServiceScript.done_flag("we_hermit"))
	lines.append("走上去触发（一次性记账）=%s（%s）" % [event_ok, _status.text])

	# 事件效果的**场景那一半**（0.28.0 Q64）：开货架要真盖上来、切磋要真把遭遇交给场景层。
	# 服务层那半（发物品／记线索／走判定）在 `tests/test_world_event.gd` 里验。
	force_world_event("we_caravan", Vector2(8, 0))
	_update_world_event(0.1)
	event_ok = event_ok and overlays().has("shop") and shop_panel != null
	event_ok = event_ok and _status.text.contains("货担")
	lines.append("货商车队就地开张（%s）=%s" % [_status.text, overlays().has("shop") and shop_panel != null])
	close_shop()
	event_ok = event_ok and not overlays().has("shop") and shop_panel == null
	var before_spar: int = captured.size()
	force_world_event("we_disciple", Vector2(8, 0))
	_update_world_event(0.1)
	var spar_ok: bool = captured.size() == before_spar + 1
	if spar_ok:
		spar_ok = str(captured[before_spar].team_id) == "team_wanderer_disciple"
		spar_ok = spar_ok and not bool(captured[before_spar].is_elite)
	event_ok = event_ok and spar_ok
	lines.append("门派弟子历练把遭遇交给场景层=%s" % spar_ok)
	ok = ok and event_ok

	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	var copy_hits: PackedStringArray = CopyGuardScript.id_tokens(self)
	ok = ok and copy_hits.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	if not copy_hits.is_empty():
		lines.append("COPY 命中：%s" % "；".join(copy_hits))
	# 观察点**看得见**（设计 15 §一「可交互物暖色提亮」，决策 337）：大地图那几条也要有那枚标记。
	var highlight_ok := not flavor_points.is_empty()
	for point: Dictionary in flavor_points:
		var mark = (point["node"] as Node2D).get_node_or_null(FlavorMarkerScript.NODE_NAME)
		highlight_ok = highlight_ok and FlavorMarkerScript.is_visible_highlight(mark)
	ok = ok and highlight_ok
	lines.append("观察点可见标记 %d 个 ok=%s" % [flavor_points.size(), highlight_ok])

	# 地名常驻（设计 11 §三「地标一旦揭开，地名常驻」＋14 §六「大区名常驻 HUD 左上角」）。
	# 2026-10-04 实机反馈「大地图地名不常驻」——而**两条都没有断言**：揭开了没字、或 HUD 那串
	# 的大区名还写着代码兜底串，都会照样 `SELF-TEST: OK`。这里把两条都钉住：
	#   ① 揭开一个地标之后，它的名字 Label **真的可见**（不是"画了但藏着"）；
	#   ② HUD 领头那个大区名**来自 `map_region.region_name_cn`**（不许是代码里编的字符串）。
	if world_map != null:
		world_map.apply_initial_reveals()     # 幂等：开局该揭开的那几个（含起始地标）
		# **断言的前提要自己铺**：地编正在按 0.31.2 重建大地图（画布换成 2048×1536，这一轮的 Marker
		# 还是旧坐标）→ "走近揭开"那条路此刻不可靠。所以这里直接揭开（`reveal_node` 就是状态层 API、
		# `WorldMapService._reveal()` 也是调它），断言的仍然是"**揭开之后名字必须看得见**"本身。
		# 注意用 **`world_map.state`** 而不是 `current_state()`：自检夹具是在控制器 `_ready` **之后**
		# 才把 `state_override` 塞进来的，两个引用不是同一个对象（2026-10-04 实测踩到：
		# 直接调 `current_state().reveal_node()` 返回 false，而 `world_map` 那边仍然是 0 个揭开）。
		# 自检夹具是「先建控制器、后塞 `state_override`」，于是 `world_map` 还绑在**旧状态**上——
		# 不重建的话它永远是「0 个揭开」，这条断言也就永远量不到东西（2026-10-04 实测踩到）。
		if current_state() != null:
			world_map = WorldMapServiceScript.new(db, current_state())
			world_map.apply_initial_reveals()
			world_map.state.reveal_node(START_NODE)
		refresh_node_labels()
	var revealed_with_name := false
	for node_id: String in _node_labels.keys():
		if world_map != null and world_map.is_revealed(node_id) and (_node_labels[node_id] as Label).visible:
			revealed_with_name = true
			break
	# **名字与 `icon` 解耦**（0.31.2）：只要那张图上有 `Node_<id>` 位点，就该有它的名字 Label——
	# 设计侧清空四个兴趣点的 `icon` 之后，这条是"名字还在"的保证（今天 8 个都有图标，所以它还不咬人）。
	var labelable := 0
	for row: Resource in db.rows("map_region"):
		if world.get_node_or_null("Markers/Node_%s" % str(row.node_id)) != null:
			labelable += 1
	var name_ok := revealed_with_name and _node_labels.size() == labelable
	var region_row: Resource = db.get_row("map_region", START_NODE)
	var want_region := str(region_row.region_name_cn).strip_edges() if region_row != null else ""
	name_ok = name_ok and not want_region.is_empty() and _region_name() == want_region
	ok = ok and name_ok
	lines.append("地名常驻（labels=%d／该有名字的位点 %d 个／已揭开 %d 个／可见=%s／HUD 大区名「%s」来自表=%s）=%s"
		% [_node_labels.size(), labelable, world_map.revealed_count() if world_map != null else -1, revealed_with_name,
			want_region, not want_region.is_empty() and _region_name() == want_region, name_ok])

	for line: String in lines:
		print("  " + line)
	print("WORLD SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	var tree := _tree()
	if tree != null:
		tree.quit(0 if ok else 1)
