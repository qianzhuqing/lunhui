## 地图资源自检：场景里的 Marker 与配置表双向比对。
##
## 这是 07_地图资源需求.md 第二节承诺的那条自检——地编的场景进仓库后由开发侧卡住：
##   * 表里有、地图没有 → 少放（或名字写错）
##   * 地图有、表里没有 → 多放（或名字写错）
##   * 放错房间/区域 → 归属不对
##
## 关键实现细节：**Marker 用列表存，不能按名字去重**。同一个 id 会在不同房间重复出现
## （两个 `Chest_drop_chest_silver`、两处 `Trigger_trig_wine`），去重会少算，
## 所以每一项都要按「名字 + 所在房间」两段来核对。
extends "res://tests/test_case.gd"

## 遮挡层判据（`covering` 自定义数据）只有一处出处：控制器那两个静态函数
const LocalMapControllerScript := preload("res://src/world/local_map_controller.gd")

const OVERWORLD := "res://scenes/maps/overworld.tscn"
const PARENT_REGION := "jiangnan_east"
const ALL_SCENES := [
	OVERWORLD,
	"res://scenes/maps/scene_qingfengyi.tscn",
	"res://scenes/maps/scene_cave.tscn",
	"res://scenes/maps/scene_heifengzhai.tscn",
	"res://scenes/maps/scene_huangcun.tscn",
	"res://scenes/maps/scene_ferry_locked.tscn",
	"res://scenes/maps/scene_shixi.tscn",
]

## Team_ / Exit_ 必须也在白名单里：`_collect` 只收白名单前缀的节点，
## 少了这两个，地编就算把位点摆好，`_check_pending_requirements` 也永远找不到它们。
const MARKER_PREFIXES := [
	"Node_", "Portal_", "Room_", "Chest_", "Spawn_", "Event_", "Trigger_", "Patrol_",
	"Team_", "Exit_", "Observe_", "Sign_",
]

## 2026-10-02 审计后补进 07_地图资源需求.md 的两条约定，地编已交付：
## `Enemies/<room_id>/Team_<team_id>`（7 个）与 `Exit_<scene_id>`（4 个，渡口不开放不做）。
## 开关关掉后这两条就是硬失败，少一个位点自检就会红。
const PENDING_ENEMY_MARKERS := false
const PENDING_EXIT_MARKERS := false


func suite_name() -> String:
	return "地图资源与表 id 比对"


func run() -> void:
	var db = get_db()
	if not FileAccess.file_exists(OVERWORLD):
		fail("没有大地图：%s" % OVERWORLD)
		return
	_check_overworld(db)
	_check_local_scenes(db)
	_check_flavor_points(db, _load_all_markers())
	_check_events_exist(db, _load_all_markers())
	_check_pending_requirements(db)
	_check_theme_tiles()
	_check_region_icons(db)
	_check_icon_file_names(db)
	_check_equip_icon_file_names(db)
	_check_signposts(db)
	_check_map_layers()


## 地表分层（设计 16 §3.2，0.32.0 的 `Conditional` 与「岩檐半透明」落地后的两件事）：
##
## ① **挡路只许画在 `Decor`**：Ground／Overlay／Conditional／Fog 的「挡路」列全是「否」。
##    （Ground 那次是「副本里整张地板都是碰撞盒」，人一步推不动，见决策 269。）
## ② **遮挡类瓦片要能被代码认出来**：`Overlay` 上的岩檐在 TileSet 里标 `covering=true`
##    （这份清单的唯一出处是 `tools/mapgen/map_kit.gd::TILES_COVERING`，运行期只读生成结果），
##    代码据此在玩家走到它下面时把整层压到 `OVERLAY_COVER_ALPHA`。
##
## 两条都读**生成出来的资产**：地图侧的门限（`verify_maps`）查同一份，任何一侧漏了都会变成
## 「走着走着被顶住」或「钻进岩檐视线还挡着」——两种玩家都看不出来是数据错。
func _check_map_layers() -> void:
	var covering_scenes := PackedStringArray()
	var covering_paths := PackedStringArray()
	var conditional_scenes := PackedStringArray()
	for path: String in ALL_SCENES:
		var packed: PackedScene = load(path)
		check_not_null(packed, "场景能加载：%s" % path)
		if packed == null:
			continue
		var root := packed.instantiate()
		var overlay: TileMapLayer = null
		for child in root.get_children():
			var layer := child as TileMapLayer
			if layer == null:
				continue
			if layer.name == "Overlay":
				overlay = layer
			if layer.name == "Decor":
				continue
			# ① 除 Decor 外，一层都不许有带碰撞的瓦片
			var solid := 0
			var first_solid := ""
			for cell: Vector2i in layer.get_used_cells():
				var data: TileData = layer.get_cell_tile_data(cell)
				if data == null or data.get_collision_polygons_count(0) == 0:
					continue
				solid += 1
				if first_solid.is_empty():
					first_solid = str(cell)
			check_eq(
				solid, 0,
				"%s 的 %s 层有 %d 格带碰撞（第一格 %s）——挡路只许画在 Decor 层" % [
					path.get_file(), layer.name, solid, first_solid,
				]
			)
		# ② 条件地表（`Conditional`）：地编交付后在这里数一下格数，没交付不算失败
		var conditional := root.get_node_or_null("Conditional") as TileMapLayer
		if conditional != null:
			conditional_scenes.append("%s(%d 格)" % [path.get_file(), conditional.get_used_cells().size()])
		# ③ 遮挡类瓦片：图集认 `covering`，而且真的标在「盖住玩家」的那几格上
		check_not_null(overlay, "%s 有 Overlay 层" % path.get_file())
		if overlay != null:
			check_true(
				LocalMapControllerScript.tile_set_has_covering(overlay.tile_set),
				"%s 的 Overlay 图集带 `covering` 数据层（半透明遮挡靠它认格子）" % path.get_file(),
			)
			var covering_cell := Vector2i(-1, -1)
			var plain_cell := Vector2i(-1, -1)
			var blocking := 0
			for cell: Vector2i in overlay.get_used_cells():
				var data: TileData = overlay.get_cell_tile_data(cell)
				var covers: bool = data != null and bool(data.get_custom_data("covering"))
				if covers:
					if covering_cell.x < 0:
						covering_cell = cell
					if data.get_collision_polygons_count(0) > 0:
						blocking += 1
				elif plain_cell.x < 0:
					plain_cell = cell
			check_eq(blocking, 0, "%s：遮挡瓦片不许挡路（玩家要能走进去）" % path.get_file())
			if covering_cell.x >= 0:
				covering_scenes.append(path.get_file())
				covering_paths.append(path)
		root.free()

	# 判据本身（放在收集之后：遮挡瓦片全没了的话，下面这个循环一次都不跑，那条 `check_gt`
	# 会当场红、[假绿] 门限也会点名——两层都指着"这批资产不见了"，而不是静默通过）
	check_gt(
		float(covering_scenes.size()), 0.0,
		"至少一张图有遮挡瓦片（0.32.0：石隙死路的岩檐压在 Overlay 上；现在：%s）"
			% "、".join(covering_scenes),
	)
	for path: String in covering_paths:
		var packed: PackedScene = load(path)
		var root := packed.instantiate()
		var overlay: TileMapLayer = root.get_node_or_null("Overlay") as TileMapLayer
		var covering_cell := Vector2i(-1, -1)
		var plain_cell := Vector2i(-1, -1)
		for cell: Vector2i in overlay.get_used_cells():
			var data: TileData = overlay.get_cell_tile_data(cell)
			if data != null and bool(data.get_custom_data("covering")):
				if covering_cell.x < 0:
					covering_cell = cell
			elif plain_cell.x < 0:
				plain_cell = cell
		# 站在遮挡格上 → 被盖住；站在普通 Overlay 格上 → 不算被盖住
		check_true(
			LocalMapControllerScript.overlay_covering_at(
				overlay, overlay.to_global(overlay.map_to_local(covering_cell))
			),
			"%s：站在遮挡格 %s 上判为「被盖住」" % [path.get_file(), str(covering_cell)],
		)
		check_false(
			LocalMapControllerScript.overlay_covering_at(
				overlay, overlay.to_global(overlay.map_to_local(plain_cell))
			),
			"%s：站在普通地表格 %s 上不算被盖住" % [path.get_file(), str(plain_cell)],
		)
		root.free()
	print("  [地图资产] 地表分层：有遮挡瓦片的图 %s；有 Conditional 层的图 %s" % [
		"、".join(covering_scenes),
		"（一个都没有，等地编交付石隙细径 22 格）" if conditional_scenes.is_empty() else "、".join(conditional_scenes),
	])


## 装备图标的文件名必须是 `equip_base` 里的 id（15 §六：`assets/icons/equip/<equip_id>.png`）。
##
## 与武学那一条同源（那条抓到过 5 张旧编号）：**代码按 id 找图，名字不对就是"图静静地不显示"**。
## 区别是这条**允许一张都没有**——A14（0.31.1）才把 `equip_base.icon` 补上，27 张图还在画；
## 所以口径是"目录里有几张就查几张"，目录不存在也不算错。
## （27 张 2026-10-04 已交齐；**口径不变**——写错名字多出一张照样红，这条正是为那一天留的。）
func _check_equip_icon_file_names(db) -> void:
	var dir := DirAccess.open("res://assets/icons/equip")
	if dir == null:
		return     # 还没这个目录（美术还没出图）——不算错
	var unknown := PackedStringArray()
	for file: String in dir.get_files():
		if not file.ends_with(".png"):
			continue
		var equip_id := file.trim_suffix(".png")
		if db.get_row("equip_base", equip_id) == null:
			unknown.append(equip_id)
	check_eq(
		unknown.size(), 0,
		"每张装备图标的名字都是 equip_base 里的 id（认不出的：%s）" % "、".join(unknown)
	)


## 指路牌（设计 0.31.2）：`map_region.signpost_cn` **非空**的行，大地图那张图上必须有
## `Markers/Sign_<node_id>` 位点——**文案在表里、牌子在地图上**，两半缺一就是"玩家看不到那句话"。
## 空 = 不摆（今天 8 行都空着，等地编写文案／摆位点）。
func _check_signposts(db) -> void:
	var scene := FileAccess.get_file_as_string(OVERWORLD)
	check_false(scene.is_empty(), "读得到大地图场景")
	var planned := PackedStringArray()
	var missing := PackedStringArray()
	for row: Resource in db.rows("map_region"):
		if str(row.signpost_cn).strip_edges().is_empty():
			continue
		var node_id := str(row.node_id)
		planned.append(node_id)
		if not scene.contains("Sign_%s" % node_id):
			missing.append(node_id)
	check_eq(
		missing.size(), 0,
		"这些地标在表里写了指路牌文案，但大地图上没有 `Markers/Sign_<node_id>` 位点（地编还没摆？）：%s"
			% "、".join(missing)
	)
	# 非空校验：一条都没有说明这批还没开工——不算错，但把数字打出来，免得"0 条"被当成通过
	print("  [地图资产] 指路牌：表里写了文案 %d 条%s" % [
		planned.size(), "" if planned.is_empty() else "（%s）" % "、".join(planned),
	])


## 美术交付的图标文件**名字必须是表里的 id**（15 §六：`icons/<类>/<id>.png`）。
##
## 为什么要有这条：图按别的名字交上来，代码这边"按 id 找图"就永远找不到——
## 结果不是报错，而是**那张图静静地不显示**（玩家只会觉得"这块没做"）。
## 2026-10-04 实测就抓到 5 张：`pf_xuanwei_06`／`pf_wudu_05`／`pf_chensha_04`／
## `pf_qingluo_01`／`pf_tiaoxi_01` —— 它们是**21 号 §九 那 5 部出身本命内功（★4）**的图标，
## 而表里的 id 是 `pf_xuanwei_zhbai`（知白）／`pf_wudu_huandu`（还毒）／`pf_chensha_buhuan`（不还）／
## `pf_qingluo_ying`（影）／`pf_tiaoxi_huichun`（回春）。
##
## **教训（2026-10-04 收尾时补）**：`pf_<school>_NN` 只对「门派主线的第 N 部」成立，
## **本命／机遇那类额外条目一律带名字后缀**（`pf_xuanwei_zhbai`），不能再按序号推。
## 当时这 5 张挂过一张「旧名 → 该改成的 id」的白名单等美术改名，**美术改完（同一天）
## 白名单就删掉了**——门限本身要求双向维护：挂着过期白名单照样红。
## 反向验证：改名后故意留一行白名单 → 报
## 「这几张旧编号的图已经改了名字，把白名单里的行删掉：pf_xuanwei_06」。


func _check_icon_file_names(db) -> void:
	var checked := 0
	var unknown := PackedStringArray()
	var dir := DirAccess.open("res://assets/icons/skill")
	check_not_null(dir, "读得到武学图标目录")
	if dir == null:
		return
	for file: String in dir.get_files():
		if not file.ends_with(".png"):
			continue
		var skill_id := file.trim_suffix(".png")
		checked += 1
		if db.get_row("skill_base", skill_id) != null:
			continue
		unknown.append(skill_id)
	check_gt(float(checked), 0.0, "武学图标目录里有图（%d 张）" % checked)
	check_eq(
		unknown.size(), 0,
		"每张武学图标的名字都是 skill_base 里的 id（认不出的：%s）" % "、".join(unknown),
	)
	# 异常状态（15 §4.3 的「异常状态 4」）与增益（19）：**同一套门限，以前这两条缺着**。
	# 2026-10-04 美术交异常 4 张时发现：`icons/status/` 一张图都没有门限管——
	# 文件名和 `status_effect.icon` 对不上时不会有任何东西红（图摆上去不显示，或者摆错张）。
	# 表侧来源是 `icon` 列（**空则退回行 id**，与 `IconPathsScript` 的优先级同一口径），
	# 不是行的主键：现例 `poison` 行的 icon 就是 `status_poison`。
	_check_icon_dir_matches_table(db, "res://assets/icons/status", "status_effect", "异常状态")
	_check_icon_dir_matches_table(db, "res://assets/icons/buff", "buff_def", "增益")


## 一类图标目录里的**文件名**必须都能在对应表里认出来（目录还没交图 = 跳过，不算错，
## 与装备那条同口径）。「认出来」= 命中任一行的 `icon` 列（非空时）或行的 id——代码就是这么
## 解析的（`icon_paths.gd` 的「优先 icon，空则退回行 id」），门限跟着它走，别自创第二套口径。
func _check_icon_dir_matches_table(db, dir_path: String, table: String, label: String) -> void:
	var dir := DirAccess.open(dir_path)
	if dir == null:
		return
	var allowed := {}
	for row: Resource in db.rows(table):
		var icon_id := str(row.icon).strip_edges()
		allowed[icon_id if not icon_id.is_empty() else str(row.id)] = true
	var checked := 0
	var unknown := PackedStringArray()
	for file: String in dir.get_files():
		if not file.ends_with(".png"):
			continue
		var file_id := file.trim_suffix(".png")
		checked += 1
		if not allowed.has(file_id):
			unknown.append(file_id)
	check_gt(float(checked), 0.0, "%s图标目录里有图（%d 张）" % [label, checked])
	check_eq(
		unknown.size(), 0,
		"每张%s图标的名字都要能在 %s 里认出来（icon 列或行 id；认不出的：%s）"
			% [label, table, "、".join(unknown)],
	)


## 地标图标的贴图必须真的在（`map_region.icon` → `assets/sprites/icons/<icon>.png`）。
##
## 缺文件时大地图上那个地标**什么都不画**，玩家只会觉得「这里没图标」——不会觉得是数据错。
## 所以这条不能只写在 PS1 那道网里：headless 自检也要有（两道网各查一遍是这套的规矩）。
##
## 0.32.0 起还要反过来看一眼：**没配图标的行只许是兴趣点**（16 §3.5：洞口／屋舍群／水岸
## 靠地形认）。以前这里数的是「有图标的行数 > 5」——兴趣点一清图标那条就会红，
## 而且它只能证明「有几行配了」，证不了「该配的行都配了」。
func _check_region_icons(db) -> void:
	var missing := PackedStringArray()
	var iconless := PackedStringArray()
	var checked := 0
	for row: Resource in db.rows("map_region"):
		var icon_id := str(row.icon)
		if icon_id.is_empty():
			if str(row.node_type) != "poi":
				iconless.append("%s(%s)" % [str(row.node_id), str(row.node_type)])
			continue
		checked += 1
		if not FileAccess.file_exists("res://assets/sprites/icons/%s.png" % icon_id):
			missing.append("%s→%s" % [str(row.node_id), icon_id])
	check_eq(
		missing.size(), 0,
		"每个地标的图标贴图都在（缺：%s）" % "、".join(missing),
	)
	check_eq(
		iconless.size(), 0,
		"只有兴趣点可以不配图标，这些行没配（地标在图上会「什么都不画」）：%s" % "、".join(iconless),
	)
	check_gt(float(checked), 0.0, "扫到地标图标（%d 个：每行都查过文件在不在）" % checked)


## 主题 TileSet 的碰撞必须与 `MapKit.THEMES` 的 solid 清单**逐格一致**，而且地板永远不许挡路。
##
## 2026-10-04 玩家报的「副本里无法行走」就出在这条不变量上：`TILES_STONE` 一份清单同时当
## 「要收进图集的瓦片」和「挡路的瓦片」用，地板瓦片（1,9）被写成实心格——副本／洞穴的
## Ground 层整片都是碰撞盒，玩家横着走不动（纵向那点位移是被挤出来的假自由）。
## 地图验收与场景自检当时都是绿的：前者只看 Decor、后者全靠瞬移（见 `框架说明.md` 决策 269）。
func _check_theme_tiles() -> void:
	var kit = load("res://tools/mapgen/map_kit.gd")
	check_false(kit == null, "读得到地图工具箱 map_kit.gd")
	if kit == null:
		return
	for theme: String in kit.THEMES:
		var path: String = kit.theme_tileset_path(theme)
		var tile_set: TileSet = load(path)
		check_true(tile_set != null, "主题 %s 的 TileSet 能加载（%s）" % [theme, path])
		if tile_set == null:
			continue
		var solid: Array = kit.THEMES[theme]["solid"]
		var source: TileSetAtlasSource = tile_set.get_source(kit.SOURCE_ID) as TileSetAtlasSource
		check_true(source != null, "主题 %s 的图集源存在" % theme)
		if source == null:
			continue
		for tile: Vector2i in kit.THEMES[theme]["tiles"]:
			if not source.has_tile(tile):
				continue
			var data := source.get_tile_data(tile, 0)
			var has_collision: bool = data.get_collision_polygons_count(0) > 0
			check_eq(has_collision, solid.has(tile),
				"主题 %s 瓦片 %s：碰撞盒与 solid 清单一致" % [theme, str(tile)])
		for floor_tile: Vector2i in [kit.T_FLOOR, kit.T_FLOOR_TOP]:
			check_false(solid.has(floor_tile),
				"主题 %s 的地板 %s 不许挡路（solid 清单只收墙）" % [theme, str(floor_tile)])


## 新加的两条约定：敌人位点与回程出口
func _check_pending_requirements(db) -> void:
	var pending := PackedStringArray()
	var cache: Dictionary = {}

	# 1. 房间内的固定敌人：Enemies/<room_id>/Team_<team_id>
	for row: Resource in db.rows("dungeon_room"):
		var team_id := str(row.enemy_team)
		if team_id.is_empty():
			continue
		var scene_id := str(row.scene_id)
		var markers := _scene_markers(scene_id, cache)
		var room_id := str(row.room_id)
		var wanted := "Team_%s" % team_id
		var found := false
		for marker: Dictionary in markers:
			if str(marker["name"]) == wanted and str(marker["path"]).contains("Enemies/%s/" % room_id):
				found = true
		if not found:
			pending.append("敌人位点 Enemies/%s/%s" % [room_id, wanted])

	# 2. 小地图的回程出口：Exit_<scene_id>
	for row: Resource in db.rows("map_local"):
		var scene_id := str(row.scene_id)
		if scene_id == "scene_ferry_locked":
			continue  # 本章不可进入
		var exit_name := "Exit_%s" % scene_id
		var markers := _scene_markers(scene_id, cache)
		if not _has_marker(markers, exit_name):
			pending.append("回程出口 %s" % exit_name)
			continue
		var entrance := _entrance_room(db, scene_id)
		if not entrance.is_empty() and not _has_in_room(markers, exit_name, entrance):
			pending.append("回程出口 %s 应放在入口房间 %s" % [exit_name, entrance])

	if pending.is_empty():
		return
	if PENDING_ENEMY_MARKERS or PENDING_EXIT_MARKERS:
		print("  [地图自检·待补] %d 项约定尚未交付：%s" % [pending.size(), "、".join(pending)])
		return
	for item: String in pending:
		fail("%s 未交付（见 07_地图资源需求.md）" % item)


func _scene_markers(scene_id: String, cache: Dictionary) -> Array:
	if not cache.has(scene_id):
		cache[scene_id] = _load_markers("res://scenes/maps/%s.tscn" % scene_id)
	return cache[scene_id]


## 场景里的入口房间（room_type=entrance）；城镇没有房间行，返回空串
func _entrance_room(db, scene_id: String) -> String:
	for row: Resource in db.rows("dungeon_room"):
		if str(row.scene_id) == scene_id and str(row.room_type) == "entrance":
			return str(row.room_id)
	return ""


# ------------------------------------------------------------------ 大地图

func _check_overworld(db) -> void:
	var markers := _load_markers(OVERWORLD)
	if markers.is_empty():
		fail("大地图读不出 Marker")
		return

	var nodes := _prefix_rows(db, "map_region", "node_id", "Node_", "parent_region", PARENT_REGION)
	var portals := _prefix_rows(db, "map_local", "scene_id", "Portal_", "", "")
	var spawns := _prefix_rows(db, "roaming_spawn", "spawn_id", "Spawn_", "", "")
	var patrols := PackedStringArray()
	for row: Resource in db.rows("roaming_spawn"):
		var path_id := str(row.patrol_path_id)
		if not path_id.is_empty():
			var name := "Patrol_%s" % path_id
			if not patrols.has(name):
				patrols.append(name)
	_compare(names_with(markers, "Node_"), nodes, "大地图 Node_")
	_compare(names_with(markers, "Portal_"), portals, "大地图 Portal_")
	_compare(names_with(markers, "Spawn_"), spawns, "大地图 Spawn_")
	_compare(names_with(markers, "Patrol_"), patrols, "大地图 Patrol_")
	# 判定位点只查「有没有落点」与「房间级是否落在正确房间」，
	# 因为「寨墙在野外、赌桌在客栈」这种归属表里没编码，不强求它必须在大地图上。

	# 0.28.0 的 A7 让石隙迷窟进大地图：`Node_n_shixi`（持藏宝图才出现）＋ `Portal_scene_shixi`
	check_eq(names_with(markers, "Node_").size(), 8, "大地图地标 8 处（0.28.0 加了石隙迷窟）")
	check_eq(names_with(markers, "Portal_").size(), 6, "大地图入口 6 处（0.28.0 加了石隙迷窟）")
	check_eq(names_with(markers, "Spawn_").size(), 17, "大地图明雷 17 处")
	check_eq(names_with(markers, "Patrol_").size(), 4, "巡逻线 4 条")

	# 地标坐标要跟表里一致
	var positions := _load_marker_positions(OVERWORLD)
	for row: Resource in db.rows("map_region"):
		if str(row.parent_region) != PARENT_REGION:
			continue
		var marker := "Node_%s" % row.node_id
		if not positions.has(marker):
			continue
		var expected := Vector2(float(row.pos_x), float(row.pos_y))
		var actual: Vector2 = positions[marker]
		check_lt((actual - expected).length(), 1.0, "%s 坐标与 map_region 一致" % marker)


# ------------------------------------------------------------------ 小地图与房间

func _check_local_scenes(db) -> void:
	for scene_row: Resource in db.rows("map_local"):
		var scene_id := str(scene_row.scene_id)
		var path := "res://scenes/maps/%s.tscn" % scene_id
		if not FileAccess.file_exists(path):
			fail("map_local.csv 里的 %s 没有对应场景：%s" % [scene_id, path])
			continue
		var markers := _load_markers(path)
		var rooms := PackedStringArray()
		for room_row: Resource in db.rows("dungeon_room"):
			if str(room_row.scene_id) != scene_id:
				continue
			rooms.append("Room_%s" % room_row.room_id)
			_check_room_markers(db, scene_id, room_row, markers)
		_check_triggers(db, scene_id, markers)
		_compare(names_with(markers, "Room_"), rooms, "%s Room_" % scene_id)
		# 不该有不属于本场景的 Room_
		for marker: Dictionary in markers:
			var name: String = marker["name"]
			if not name.begins_with("Room_"):
				continue
			var row: Resource = db.get_row("dungeon_room", name.substr("Room_".length()))
			if row == null or str(row.scene_id) != scene_id:
				fail("%s 里多出房间 %s（表里不属于这个场景）" % [scene_id, name])


## 单个房间：宝箱、隐藏触发、房间级判定要挂在 `Markers/<room_id>/` 下面
func _check_room_markers(db, scene_id: String, room_row: Resource, markers: Array) -> void:
	var room_id := str(room_row.room_id)
	var expected := PackedStringArray()
	var chest_id := str(room_row.chest_id)
	if not chest_id.is_empty():
		expected.append("Chest_%s" % chest_id)
	for event_row: Resource in db.rows("event_check"):
		if str(event_row.room_id) == room_id:
			expected.append("Event_%s" % event_row.check_id)

	for name: String in expected:
		if not _has_in_room(markers, name, room_id):
			fail("%s/%s 缺少 %s" % [scene_id, room_id, name])

	# 反向：这个房间下不该有多余的宝箱与触发
	for marker: Dictionary in markers:
		var name: String = marker["name"]
		if str(marker["room"]) != room_id or name.begins_with("Room_"):
			continue
		if name.begins_with("Chest_") and not expected.has(name):
			fail("%s/%s 多出 %s（表里没有）" % [scene_id, room_id, name])


## 隐藏触发：允许的落点是「hidden_trigger.room_id」加上「dungeon_room.hidden_trigger 指向它的那些房间」。
## 例：三火盆的交互点在火盆厅（hidden_trigger.room_id），而火盆密室只是「被它打开的房间」。
func _check_triggers(db, scene_id: String, markers: Array) -> void:
	for trigger_row: Resource in db.rows("hidden_trigger"):
		if str(trigger_row.scene_id) != scene_id:
			continue
		var trigger_id := str(trigger_row.trigger_id)
		var name := "Trigger_%s" % trigger_id
		var allowed := PackedStringArray()
		if not str(trigger_row.room_id).is_empty():
			allowed.append(str(trigger_row.room_id))
		for room_row: Resource in db.rows("dungeon_room"):
			if str(room_row.scene_id) == scene_id and str(room_row.hidden_trigger) == trigger_id:
				if not allowed.has(str(room_row.room_id)):
					allowed.append(str(room_row.room_id))
		var found := false
		for marker: Dictionary in markers:
			if str(marker["name"]) != name:
				continue
			if allowed.has(str(marker["room"])):
				found = true
			else:
				fail("%s 的 %s 放在 %s，表里允许的位置是 %s" % [
					scene_id, name, marker["room"], ", ".join(allowed),
				])
		if not found:
			fail("%s 缺少 %s（允许位置：%s）" % [scene_id, name, ", ".join(allowed)])


## 观察点（设计 20 §3.2／0.29.1）也走同一条纪律：表里有这一行，地图上就得有那个位点。
##
## 判据只按名字（`Observe_<point_id>`）：小地图与大地图各自收自己那几条
## （表里 `scene_id`／`region_id` 二选一，构建期已经卡住），所以这里只要"在**某张图**里找得到"；
## 房间级的那几条还要落在写明的房间下（与宝箱／判定同一套）。
func _check_flavor_points(db, markers: Array) -> void:
	var missing := 0
	for row: Resource in db.rows("flavor_point"):
		var name := "Observe_%s" % str(row.point_id)
		if not _has_marker(markers, name):
			missing += 1
			fail("flavor_point[%s] 的位点 %s 还没有地图交付（见 20 号 §十二）" % [row.point_id, name])
			continue
		var room_id := str(row.room_id)
		if not room_id.is_empty():
			check_true(_has_in_room(markers, name, room_id), "%s 落在 %s" % [name, room_id])
	check_eq(missing, 0, "观察点位点全部交付（缺 %d 个）" % missing)


## 每条事件判定都要在地图里找到落点；有 room_id 的必须落在那个房间
##
## **例外：随机事件直接结算的那一条**（0.28.0 Q64）。`ev_patrol_check` 有两个入口，
## 其中一个（`world_event.we_patrol`）**不要求玩家走到关卡**——判定在服务层直接结算，
## 所以它没有 `Event_` 位点是设计原样，不是地编漏摆（07 只把官道那个固定关卡列成位点）。
## 判据不写死 id：**只要某条 `world_event` 的 `effect_kind=check` 指向它**，它就有入口。
func _check_events_exist(db, all_markers: Array) -> void:
	var from_events := PackedStringArray()
	for row: Resource in db.rows("world_event"):
		if str(row.effect_kind) == "check" and not str(row.effect_id).is_empty():
			from_events.append(str(row.effect_id))
	for row: Resource in db.rows("event_check"):
		var name := "Event_%s" % row.check_id
		if not _has_marker(all_markers, name):
			if not from_events.has(str(row.check_id)):
				fail("event_check[%s] 在所有地图里都没有落点" % row.check_id)
			continue
		var room_id := str(row.room_id)
		if not room_id.is_empty():
			check_true(_has_in_room(all_markers, name, room_id), "%s 落在 %s" % [name, room_id])


# ------------------------------------------------------------------ 工具

## 收集场景里所有 Marker，返回 [{name, path, room}, ...]
func _load_markers(path: String) -> Array:
	var packed: PackedScene = load(path)
	if packed == null:
		fail("场景加载失败：%s" % path)
		return []
	var root := packed.instantiate()
	var markers: Array = []
	_collect(root, "", markers)
	root.free()
	return markers


func _collect(node: Node, parent_path: String, out: Array) -> void:
	for child in node.get_children():
		var child_path := str(child.name) if parent_path.is_empty() else "%s/%s" % [parent_path, child.name]
		if _is_marker(str(child.name)):
			out.append({"name": str(child.name), "path": child_path, "room": _room_of(child_path)})
		_collect(child, child_path, out)


func _load_marker_positions(path: String) -> Dictionary:
	var packed: PackedScene = load(path)
	if packed == null:
		return {}
	var root := packed.instantiate()
	var out: Dictionary = {}
	_collect_positions(root, out)
	root.free()
	return out


func _collect_positions(node: Node, out: Dictionary) -> void:
	for child in node.get_children():
		if child is Marker2D and _is_marker(str(child.name)):
			out[str(child.name)] = (child as Marker2D).position
		_collect_positions(child, out)


## 六张地图的 Marker 并集，用于「表里的东西到底有没有落点」
func _load_all_markers() -> Array:
	var out: Array = []
	for path: String in ALL_SCENES:
		if not FileAccess.file_exists(path):
			continue
		out.append_array(_load_markers(path))
	return out


## Marker 自身路径 Markers/hf1_shed/Chest_x → hf1_shed；大地图的 Markers/Node_x 返回空
func _room_of(marker_path: String) -> String:
	var parts := marker_path.split("/")
	if parts.size() >= 3 and parts[parts.size() - 3] == "Markers":
		return parts[parts.size() - 2]
	return ""


func _is_marker(node_name: String) -> bool:
	for prefix: String in MARKER_PREFIXES:
		if node_name.begins_with(prefix):
			return true
	return false


func names_with(markers: Array, prefix: String) -> PackedStringArray:
	var out := PackedStringArray()
	for marker: Dictionary in markers:
		if str(marker["name"]).begins_with(prefix):
			out.append(str(marker["name"]))
	return out


func _has_marker(markers: Array, name: String) -> bool:
	for marker: Dictionary in markers:
		if str(marker["name"]) == name:
			return true
	return false


func _has_in_room(markers: Array, name: String, room_id: String) -> bool:
	for marker: Dictionary in markers:
		if str(marker["name"]) == name and str(marker["room"]) == room_id:
			return true
	return false


## 表里某列加上 Marker 前缀作为期望集合（可带一列过滤条件）
func _prefix_rows(
	db, table_name: String, column: String, prefix: String,
	filter_column: String, filter_value: String
) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in db.rows(table_name):
		if not filter_column.is_empty() and str(row.get(filter_column)) != filter_value:
			continue
		out.append("%s%s" % [prefix, str(row.get(column))])
	return out


func _compare(actual: PackedStringArray, expected: PackedStringArray, label: String) -> void:
	for name: String in expected:
		if not actual.has(name):
			fail("%s 缺少 %s" % [label, name])
	for name: String in actual:
		if not expected.has(name):
			fail("%s 多出 %s（表里没有）" % [label, name])
