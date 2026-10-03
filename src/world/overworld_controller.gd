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
const RecruitServiceScript := preload("res://src/core/recruit_service.gd")
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
var _hud: CanvasLayer = null
var _portals: Array = []
## 入口是否已武装（刚回图时人可能正踩在入口上，先离开圈外一次才算数）
var _portal_armed := false
## 驿站界面（覆盖在当前场景上的界面，关掉就 queue_free）
var waypoint_panel: Node = null
## 线索本（K 打开：野外事件按地标分组）
var clue_panel: Node = null
var _save_service = null
## 地图揭开与驿站规则的唯一来源（状态写在存档里）
var world_map
## 事件判定位点：{check_id, node, position}
var events: Array = []
## 地标图标：node_id → Sprite2D（见 _build_node_icons）
var _node_icons: Dictionary = {}
## 地标名字（node_id → Label，见 `_build_node_labels`）
var _node_labels: Dictionary = {}
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
	_build_node_icons()
	_build_node_labels()
	_collect_portals()
	_collect_events()
	world_map = WorldMapServiceScript.new(db, current_state())
	world_map.apply_initial_reveals()
	_build_status_label()
	_refresh_status()
	_build_map_label()
	_build_guide_label()
	_check_region_recruits()
	_apply_fog()
	# 放在最后：状态栏提示与揭雾都不该被「刚脱身」这一步盖掉
	_push_player_out_of_contact()


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
	_update_highlight()
	refresh_node_labels()


## 地标名字：图标之外再写一行字（`map_region.name_cn`），玩家不用挨个走进去才知道那是哪。
## 规则与图标一致：**没揭开的没有名字**（提前把地名写出来就是剧透）；
## 本章去不了的（锁定）用暗色字，和 `_dim` 图标一个口径。
func _build_node_labels() -> void:
	for row: Resource in db.rows("map_region"):
		var node_id := str(row.node_id)
		if str(row.icon).is_empty():
			continue
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
		if not world_map.is_revealed(node_id) or str(row.icon).is_empty():
			label.visible = false
			continue
		label.text = str(row.name_cn)
		label.visible = true
		# 本章去不了的：字压暗（和 _dim 图标同一口径，不加额外文字免得挡住地图）
		label.modulate = Color(0.62, 0.62, 0.62) if WorldMapServiceScript.LOCKED_SCENES.has(str(row.enter_scene)) else Color.WHITE


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
	_status.text = "%s　移动 WASD／方向键　潜行 Shift　交互 E　角色 Tab　线索 K　返回 Esc" % region
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
	return str(row.name_cn) if row != null else "江南道·东部"


# ------------------------------------------------------------------ 帧循环

func _process(_delta: float) -> void:
	if camera != null and player != null:
		camera.global_position = player.global_position
	if player == null:
		return
	_check_reveals()
	_check_portal()
	_track_sneak()
	_update_highlight()
	# 剧情招募里那几位 `join_scene` 指向区域地标的（现例：林铁山在落雁坡）：
	# 走到地标跟前就算「遇上了」，和脚下的高亮用同一个半径。
	_check_region_recruits()
	# 战斗外增益的剩余分钟按现实时间走：每秒刷一次就够（不必每帧重算文案）
	_field_refresh_timer -= _delta
	if _field_refresh_timer <= 0.0:
		_field_refresh_timer = 1.0
		_refresh_field_buffs()


## 揭雾：proximity_N 走进去自动揭开、discover_X 在 X 揭开后跟着揭开，结果写进存档
func _check_reveals() -> void:
	if world_map == null:
		return
	var revealed: PackedStringArray = world_map.apply_proximity_reveals(player.position, _node_positions())
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
	# 驿站界面开着的时候输入归界面（E 不再触发交互，Esc 先关界面）
	if (waypoint_panel != null and is_instance_valid(waypoint_panel)) or (clue_panel != null and is_instance_valid(clue_panel)):
		return
	if event.is_action_pressed("open_character"):
		var tree := _tree()
		if tree != null:
			_change_scene("res://scenes/character_screen.tscn")
	elif event.is_action_pressed("ui_cancel"):
		_change_scene(PLACEHOLDER_SCENE)
	elif event.is_action_pressed("interact"):
		var check_id := event_near_player()
		if not check_id.is_empty():
			resolve_event(check_id)
		elif post_station_near():
			open_waypoint()
		else:
			_set_status("这里暂时没什么可交互的（走进地标可以进小地图）")
	elif event.is_action_pressed("show_clues"):
		open_clues()


## 开线索本：野外事件按地标分组（在大地图上按 K）
func open_clues() -> Dictionary:
	if clue_panel != null and is_instance_valid(clue_panel):
		clue_panel.refresh()
		return {"ok": true, "error": "", "reopened": true}
	var panel = load(CLUE_SCENE).instantiate()
	if panel == null:
		return {"ok": false, "error": "线索本场景加载失败"}
	panel.name = "CluePanel"
	panel.state_override = current_state()
	panel.scope = "region"
	panel.return_handler = func() -> void: close_clues()
	add_child(panel)
	panel.setup()
	clue_panel = panel
	_set_status("线索本：野外的可交互点按地标列在这里（Esc 或点「离开」出来）")
	return {"ok": true, "error": "", "reopened": false}


func close_clues() -> void:
	if clue_panel != null and is_instance_valid(clue_panel):
		clue_panel.queue_free()
	clue_panel = null


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
	add_child(panel)
	panel.setup()
	waypoint_panel = panel
	var welcome := "驿站：可以传送到已探索的地标，也能在这里改难度（Esc 或点「离开」出来）"
	panel.show_message(welcome)
	_set_status(welcome)
	return {"ok": true, "error": "", "reopened": false}


func close_waypoint() -> void:
	if waypoint_panel != null and is_instance_valid(waypoint_panel):
		waypoint_panel.queue_free()
	waypoint_panel = null
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

	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	var copy_hits: PackedStringArray = CopyGuardScript.id_tokens(self)
	ok = ok and copy_hits.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	if not copy_hits.is_empty():
		lines.append("COPY 命中：%s" % "；".join(copy_hits))
	for line: String in lines:
		print("  " + line)
	print("WORLD SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	var tree := _tree()
	if tree != null:
		tree.quit(0 if ok else 1)
