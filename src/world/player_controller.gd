## 大地图上的玩家角色。
##
## 占位美术：`Polygon2D` 画的方块 + 一个朝向小方块（背袭要靠朝向判断，所以朝向必须看得见）。
## 移动按 07_地图资源需求.md 的口径；**0.15.0 把速度从 128 提到 180**
## （设计 11 §一：实测觉得走图太慢；约 1.4 倍，潜行比例不变）。手感常量、不进表。
## 碰撞只吃「阻挡层」（瓦片物理层的位 2），与明雷的接触交给明雷的接触判定。
extends CharacterBody2D

## 走路速度（像素/秒）。**2026-10-04（0.31.2 大地图扩容）从 180 下调回 128**：
## 相机改 2× 之后"观感变快"，设计侧给的判据是「**清风驿走到驿站不短于约 4 秒**」——
## 而地标坐标这一版也变大了（`map_region` 清风驿 350,1050 → 驿站 800,800，直线 514.8px）。
## 按**直线**（路程的下界）算：514.8 / 128 ≈ **4.02 秒** ✓；180 只有 2.86 秒 ✗。
## 这个判据由 `tests/test_overworld.gd::_check_walk_speed_criterion` 每次验收重算——
## 以后设计再动坐标，那条会红并提示重新估速。
const WALK_SPEED := 128.0
const SNEAK_RATIO := 0.6
const BLOCKING_LAYER_BIT := 2

var facing := Vector2.DOWN
var sneaking := false
var can_move := true


func _ready() -> void:
	collision_layer = 0
	collision_mask = BLOCKING_LAYER_BIT
	if get_child_count() == 0:
		_build_placeholder()


func _build_placeholder() -> void:
	var shape := CollisionShape2D.new()
	var circle := CircleShape2D.new()
	circle.radius = 9.0
	shape.shape = circle
	shape.position = Vector2(0, -6)
	add_child(shape)

	var body := Polygon2D.new()
	body.name = "Body"
	body.color = Color("6fa8dc")
	body.polygon = PackedVector2Array([
		Vector2(-8, -22), Vector2(8, -22), Vector2(8, 0), Vector2(-8, 0),
	])
	add_child(body)

	var nose := Polygon2D.new()
	nose.name = "Facing"
	nose.color = Color("f6d55c")
	nose.polygon = PackedVector2Array([
		Vector2(-3, -3), Vector2(3, -3), Vector2(3, 3), Vector2(-3, 3),
	])
	add_child(nose)
	_update_facing_marker()


func _physics_process(_delta: float) -> void:
	if not can_move:
		velocity = Vector2.ZERO
		move_and_slide()
		return
	var input := Input.get_vector("move_left", "move_right", "move_up", "move_down")
	sneaking = Input.is_action_pressed("sneak")
	var speed := WALK_SPEED * (SNEAK_RATIO if sneaking else 1.0)
	velocity = input * speed
	if input.length() > 0.1:
		facing = input.normalized()
		_update_facing_marker()
	move_and_slide()
	modulate.a = 0.6 if sneaking else 1.0


func _update_facing_marker() -> void:
	var nose := get_node_or_null("Facing")
	if nose == null:
		return
	nose.position = facing_limit(facing) * 14.0


## 朝向取整成四方向（背袭判定用）
func facing_limit(direction: Vector2) -> Vector2:
	if absf(direction.x) > absf(direction.y):
		return Vector2(signf(direction.x), 0)
	return Vector2(0, signf(direction.y))


func facing_quadrant() -> Vector2:
	return facing_limit(facing)


func is_sneaking() -> bool:
	return sneaking
