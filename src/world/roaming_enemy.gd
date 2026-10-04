## 大地图上的明雷小队。
##
## 行为来自 roaming_spawn.behavior（idle／patrol／wander／chase／sleep），
## 数值来自表：alert_radius（格）、chase_speed（格/秒）、respawn_sec、is_elite。
## 接触玩家时发 `encountered(spawn_id, contact)`，由大地图控制器决定进不进战斗。
##
## 接触方式按 docs/design/02_地图与明雷.md：
##   正面 → front；从背后 → back；背后且沉睡 → ambush_sleep；
##   追击型进警戒圈被发现 → spotted；被追到 → caught。
## （驻守/巡逻型的警戒圈只让他们转头发现你，不额外惩罚——表里没给这条规则，按「正常遭遇」处理。）
extends CharacterBody2D

signal encountered(spawn_id: String, contact: String)

const EncounterScript := preload("res://src/core/encounter.gd")

const TILE := 32.0
const PATROL_SPEED := 64.0        # 2 格/秒
const WANDER_SPEED := 48.0        # 1.5 格/秒
const CONTACT_DISTANCE := 22.0
## 重生时如果玩家正站在位点上，要**先走出这个圈**才恢复接触判定（留点余量，免得在边界上抖）
const CONTACT_REARM_DISTANCE := CONTACT_DISTANCE * 1.6
const THREAT_COLORS := {
	"green": Color("6fcf97"),
	"yellow": Color("f2c94c"),
	"red": Color("eb5757"),
	"purple": Color("bb6bd9"),
}

var spawn_id: String = ""
## 明雷用表行（Resource），房间敌人用字典——所以这里不写死类型
var row = null
var team_row: Resource = null
var behavior: String = "idle"
var alert_radius: float = 0.0
var chase_speed: float = 0.0
var respawn_sec: int = 0
var is_elite: bool = false
## 潜行时「被发现」判定打折的比例（combat_const.sneak_detect_reduce，由控制层注入）
var sneak_detect_reduce: float = 0.0

var home: Vector2 = Vector2.ZERO
var player: Node2D = null
var patrol_path: Path2D = null

var facing := Vector2.DOWN
var patrol_progress := 0.0
var patrol_direction := 1.0
var wander_target := Vector2.ZERO
var wander_timer := 0.0
var chasing := false
var defeated := false
var respawn_left := 0.0
var _encounter_latched := false
## 接触判定「武装」与否：重生那一刻如果玩家就站在位点上，先不武装——
## 否则明雷凭空出现在脚下、同一帧就开战，玩家连看都没看见（和地图出口「先走出去一次」同一套做法）。
var _contact_armed := true
## 精英标识（每帧做一点点呼吸感的明暗，证明「发光」是活的）。
## 类型是 Node2D 而不是 Polygon2D：美术交付贴图后换成 Sprite2D，程序化八边形只作兜底。
var _elite_glow: Node2D = null
var _glow_time := 0.0

## 精英贴图目录。`roaming_spawn.elite_marker` 存的是**贴图 id**（如 `marker_elite_red`），
## 路径按 id 拼——表里换一张图只改一处（与 `map_region.icon`、`item_base.icon` 同一套约定）。
const ELITE_MARKER_DIR := "res://assets/sprites/icons/"


## spawn_row 既可以是 roaming_spawn 的行，也可以是房间敌人用的字典（房间没用明雷表）
func setup(table_db, spawn_row, team, player_node: Node2D, path_node: Path2D) -> void:
	spawn_id = str(_row_get(spawn_row, "spawn_id", ""))
	row = spawn_row
	team_row = team
	behavior = str(_row_get(spawn_row, "behavior", "idle"))
	alert_radius = float(_row_get(spawn_row, "alert_radius", 0.0))
	chase_speed = float(_row_get(spawn_row, "chase_speed", 0.0))
	respawn_sec = int(_row_get(spawn_row, "respawn_sec", 0))
	is_elite = bool(_row_get(spawn_row, "is_elite", false))
	player = player_node
	patrol_path = path_node
	home = position
	wander_target = position

	collision_layer = 0
	collision_mask = 2          # 同样被墙挡住
	_build_placeholder()
	if behavior == "sleep":
		modulate = Color(1, 1, 1, 0.75)


## 表行与字典通用取值
static func _row_get(row, key: String, fallback: Variant) -> Variant:
	if row == null:
		return fallback
	var value: Variant = row.get(key)
	return fallback if value == null else value


func _build_placeholder() -> void:
	var shape := CollisionShape2D.new()
	var circle := CircleShape2D.new()
	circle.radius = 10.0
	shape.shape = circle
	add_child(shape)

	var color: Color = THREAT_COLORS.get(str(team_row.threat_tag) if team_row != null else "green", Color.WHITE)

	# 精英发光（设计 07：发光精英外观）。光晕先加、身体后加，这样光晕在身体**后面**；
	# 不用 z_index = -1：那会被地图的地面图层盖掉（试过，画面上什么也看不见）。
	if is_elite:
		var glow := _make_elite_glow()
		glow.name = "EliteGlow"
		add_child(glow)
		_elite_glow = glow

	var body := Polygon2D.new()
	body.name = "Body"
	var size := 13.0 if is_elite else 10.0
	body.color = color
	body.polygon = PackedVector2Array([
		Vector2(-size, -size * 2), Vector2(size, -size * 2), Vector2(size, 0), Vector2(-size, 0),
	])
	add_child(body)

	# 威胁色环：玩家一眼能看出打不打得过（绿/黄/红/紫）
	if alert_radius > 0.0:
		var ring := Polygon2D.new()
		ring.name = "AlertRing"
		ring.color = Color(color.r, color.g, color.b, 0.18)
		var points := PackedVector2Array()
		var radius := alert_radius * TILE
		for index in range(32):
			var angle := TAU * float(index) / 32.0
			points.append(Vector2(cos(angle), sin(angle)) * radius + Vector2(0, -10))
		ring.polygon = points
		add_child(ring)

	var nose := Polygon2D.new()
	nose.name = "Facing"
	nose.color = Color(0, 0, 0, 0.55)
	nose.polygon = PackedVector2Array([
		Vector2(-3, -3), Vector2(3, -3), Vector2(3, 3), Vector2(-3, 3),
	])
	nose.position = facing * 15.0 + Vector2(0, -10)
	add_child(nose)


## 精英标识用哪张图：`elite_marker` 是贴图 id，按同一套约定拼路径（空 id = 不拼）。
static func elite_marker_texture_path(marker_id: String) -> String:
	if marker_id.strip_edges().is_empty():
		return ""
	return ELITE_MARKER_DIR + marker_id.strip_edges() + ".png"


## 贴图到位就用贴图（2026-10-04：美术已交付 `marker_elite_red`，32×32 的环）；
## **贴图缺失才退回程序化八边形**——资产没到不该变成「精英看不出是精英」，这条兜底原来就在，
## 换成贴图之后留着（新主题换图、表里写错 id 时，玩家至少还看得见一个光晕）。
func _make_elite_glow() -> Node2D:
	var path := elite_marker_texture_path(elite_marker_id())
	if not path.is_empty() and ResourceLoader.exists(path):
		var sprite := Sprite2D.new()
		sprite.texture = ResourceLoader.load(path) as Texture2D
		# 身体的占位块是 26×26（y 从 -26 到 0），贴图 32×32 → 居中套住它
		sprite.position = Vector2(0.0, -13.0)
		return sprite
	var fallback := Polygon2D.new()
	fallback.color = Color(1.0, 0.84, 0.35, 0.5)
	var gx := 13.0 + 7.0
	var cut := gx * 0.45
	# 八边形而不是矩形：连兜底也要看得出是「光晕」，不是给方块描了个边
	fallback.polygon = PackedVector2Array([
		Vector2(-gx + cut, -gx * 2 - 2), Vector2(gx - cut, -gx * 2 - 2),
		Vector2(gx, -gx * 2 - 2 + cut), Vector2(gx, 6 - cut),
		Vector2(gx - cut, 6), Vector2(-gx + cut, 6),
		Vector2(-gx, 6 - cut), Vector2(-gx, -gx * 2 - 2 + cut),
	])
	return fallback


func _physics_process(delta: float) -> void:
	if defeated:
		# 只给「有刷新时间」的倒计时（见 `mark_defeated`：-1 是不刷新）
		if respawn_left > 0.0:
			respawn_left -= delta
			if respawn_left <= 0.0:
				_respawn()
		return
	_tick_elite_glow(delta)
	match behavior:
		"patrol":
			_process_patrol(delta)
		"wander":
			_process_wander(delta)
		"chase":
			_process_chase(delta)
		_:
			velocity = Vector2.ZERO
			move_and_slide()
	if player != null:
		_check_contact()


func _process_patrol(delta: float) -> void:
	if patrol_path == null:
		velocity = Vector2.ZERO
		move_and_slide()
		return
	var length := patrol_path.curve.get_baked_length()
	if length <= 0.0:
		return
	patrol_progress += patrol_direction * PATROL_SPEED * delta
	if patrol_progress >= length:
		patrol_progress = length
		patrol_direction = -1.0
	elif patrol_progress <= 0.0:
		patrol_progress = 0.0
		patrol_direction = 1.0
	var target: Vector2 = patrol_path.to_global(patrol_path.curve.sample_baked(patrol_progress))
	var step := target - global_position
	velocity = step.normalized() * PATROL_SPEED if step.length() > 1.0 else Vector2.ZERO
	if velocity.length() > 0.1:
		facing = velocity.normalized()
	move_and_slide()


func _process_wander(delta: float) -> void:
	wander_timer -= delta
	if wander_timer <= 0.0:
		wander_timer = randf_range(2.0, 4.0)
		var angle := randf() * TAU
		wander_target = home + Vector2(cos(angle), sin(angle)) * randf_range(1.0, 3.0) * TILE
	var step := wander_target - global_position
	if step.length() > 4.0:
		velocity = step.normalized() * WANDER_SPEED
		facing = velocity.normalized()
	else:
		velocity = Vector2.ZERO
	move_and_slide()


func _process_chase(delta: float) -> void:
	if player == null:
		return
	var distance := global_position.distance_to(player.global_position)
	# 潜行时警戒半径缩小；一旦被发现就进入追击（设计：「潜行失败进入追击流程」）
	if distance <= effective_alert_radius():
		chasing = true
	if chasing:
		var step := player.global_position - global_position
		if step.length() > 1.0:
			facing = step.normalized()
			velocity = step.normalized() * maxf(chase_speed, 1.0) * TILE
		else:
			velocity = Vector2.ZERO
	else:
		velocity = Vector2.ZERO
	move_and_slide()


## 接触判定：贴身就算撞上；追击型在警戒圈内被发现则直接开战
func _check_contact() -> void:
	var distance := global_position.distance_to(player.global_position)
	if not _contact_armed:
		# 刚重生：玩家得先走出接触圈，才恢复接触判定（不然「回来了」= 立刻开战）
		if distance > CONTACT_REARM_DISTANCE:
			_contact_armed = true
		else:
			return
	if distance <= CONTACT_DISTANCE:
		_emit_encounter(_contact_kind())
		return
	if behavior == "chase" and distance <= effective_alert_radius():
		_emit_encounter(EncounterScript.CONTACT_SPOTTED)


## 当前有效的警戒半径（像素）。潜行时按 combat_const.sneak_detect_reduce 打折
## —— 设计 02：「按住潜行键进入潜行状态（移速降至 60%，被发现判定降低）」。
func effective_alert_radius() -> float:
	var radius := alert_radius * TILE
	if _player_sneaking():
		radius *= 1.0 - clampf(sneak_detect_reduce, 0.0, 1.0)
	return radius


func _player_sneaking() -> bool:
	if player == null:
		return false
	var value: Variant = player.get("sneaking")
	return value != null and bool(value)


func _contact_kind() -> String:
	var to_player := (player.global_position - global_position).normalized()
	var dot := facing.normalized().dot(to_player)
	if behavior == "sleep":
		return EncounterScript.CONTACT_SLEEP if dot < 0.2 else EncounterScript.CONTACT_FRONT
	if behavior == "chase" and chasing:
		return EncounterScript.CONTACT_CAUGHT
	if dot < -0.3:
		return EncounterScript.CONTACT_BACK
	return EncounterScript.CONTACT_FRONT


func _emit_encounter(contact: String) -> void:
	if _encounter_latched:
		return
	# 中立队伍（采药人）不主动攻击，撞上也不开战
	if _is_neutral():
		return
	_encounter_latched = true
	encountered.emit(spawn_id, contact)


func _is_neutral() -> bool:
	if team_row == null:
		return false
	var members: Array = team_row.parsed_members()
	if members.is_empty():
		return false
	var first: Dictionary = members[0]
	return str(first.get("enemy_id", "")).begins_with("en_herbalist")


## 打完这场就清掉，respawn_sec 秒后回来（0 = 不刷新）
func mark_defeated(respawn_seconds: int) -> void:
	defeated = true
	# `respawn_seconds <= 0` 用 -1 当「不刷新」的哨兵值：0 秒会被倒计时立刻满足，
	# 而设计里 0 的意思是「不刷新」（精英长 CD 或彻底不刷）。
	respawn_left = float(respawn_seconds) if respawn_seconds > 0 else -1.0
	visible = false
	# **不能无条件关掉物理处理**：倒计时就写在 `_physics_process` 里，
	# 关掉它等于「在图上待够 respawn_sec 也不刷新」——只有回大地图重建时按绝对时间戳补出来。
	# 正数才留着跑倒计时；不刷新的（-1）才停。
	set_physics_process(respawn_seconds > 0)


func _respawn() -> void:
	defeated = false
	_encounter_latched = false
	# 玩家可能正站在位点上：先不武装接触判定（见 `_contact_armed`）
	_contact_armed = false
	global_position = home
	visible = true
	set_physics_process(true)


func reset_latch() -> void:
	_encounter_latched = false


## 精英光晕的呼吸：明暗在小范围里来回，静止的发光块看起来也「活着」
func _tick_elite_glow(delta: float) -> void:
	if _elite_glow == null or not is_instance_valid(_elite_glow):
		return
	_glow_time += delta
	_elite_glow.modulate.a = 0.6 + 0.4 * sin(_glow_time * 2.6)


## 用例读这个判断精英标识真的挂上了
func has_elite_glow() -> bool:
	return _elite_glow != null and is_instance_valid(_elite_glow)


func elite_marker_id() -> String:
	return str(_row_get(row, "elite_marker", ""))
