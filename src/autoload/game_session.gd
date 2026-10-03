## 当前这一局的会话状态。
##
## 主菜单新建或读取存档后把 GameState 放到这里，游戏场景从它取数据。
## 故意不声明 class_name（autoload 名与全局类名同名会冲突）。
extends Node

var state: GameState = null

## 待处理的明雷遭遇：大地图写好，战斗场景读取
var pending_encounter = null
## 已清明的明雷：spawn_id → 可重生时间（unix 秒；-1 表示不刷新）
var cleared_spawns: Dictionary = {}
## 大地图上的最后位置（从战斗返回时接着走）。**写它一律走 `set_world_position()`**——
## 那个方法顺手把它同步进存档（v13 的 `world_pos`），读档回大地图才落得回原处。
var world_position := Vector2.ZERO
## 大地图明雷开关的**会话级**覆盖（0.8.1：-1 = 读 `feature_toggle.overworld_roaming_enemy`；
## 0／1 = 强制）。放会话里而不是控制器上，因为大地图每次从战斗／小地图回来都会**重建场景**，
## 控制器上的字段活不过那一次重建（闭环用例踩过）。
var roaming_enabled_override := -1
## 上一场战斗的结果摘要（大地图读给提示）
var last_battle: Dictionary = {}
## 要进的小地图（大地图点 Portal 时写，小地图控制器读）
var pending_local_scene: String = ""
## 进小地图时要落在哪个房间（「铁镐挖通」这类捷径用；空 = 该图的入口房间）
var pending_local_room: String = ""
## 战斗结束后回到哪个场景（大地图或小地图）
var pending_return_scene: String = "res://scenes/world_run.tscn"
## 小地图里战斗前的位置（打完回图接着站那儿）；配 scene 一起用，防止跨图误用
var local_position := Vector2.ZERO
var local_position_scene: String = ""
## 小地图会话状态：scene_id → {cleared, chests, triggers}（回大地图整片刷新）
var local_maps: Dictionary = {}
## 最近到过的出生点／城镇（08 的战败处理要「全队回到最近到过的出生点或城镇」）。
## 由小地图控制器与驿站传送在**进入城镇**时登记；空 = 还没到过城镇，败北退回大地图。
var last_shelter_scene: String = ""
var last_shelter_name: String = ""
## 战斗外增益（08：`field` 作用域按**现实时间**计时，进战斗后维持整场）。
## 每条 {buff_id, source_id, stacks, expires_at}，`expires_at` 是 unix 秒。
## **v12 起这份数据存在 `GameState.field_buffs` 里**（退出/读档后按现实时间继续走——
## 只放会话里的话「打坐完退出重进」等于白打坐）。下面这个方法返回的是**当前该写哪儿**：
## 有存档就写存档；没有存档（用例／独立场景）才退回这份会话内存。改这份数据一律走
## `add_field_buff/active_field_buffs/clear_field_buffs` 三个口，别直接动字段。
var field_buffs: Array = []


func set_state(new_state: GameState) -> void:
	state = new_state
	# 读档带回来的大地图坐标（v13）：落到会话字段上，大地图 `_start_position()` 直接照它摆人。
	# 老档／新档没有这条记录时是 `Vector2.ZERO`，大地图把它当成「用默认出生点」。
	world_position = new_state.world_position() if new_state != null else Vector2.ZERO


## 记下大地图坐标：会话内存与存档（v13 `world_pos`）同源写一份。
## 设计 09 §二：读档一律回大地图、落点用这个坐标；小地图与副本内的位置不存。
##
## `fallback` 给「用例／自检注入 `state_override`、会话里没有真 state」的那条路径：
## 那种情况下真正会被存档写出去的是注入进来的那份状态，坐标不落进去就等于这条没接。
func set_world_position(pos: Vector2, fallback = null) -> void:
	world_position = pos
	var target = state if state != null else fallback
	if target != null:
		target.set_world_pos(pos)


func has_state() -> bool:
	return state != null


## 记下「最近到过的城镇／出生点」（08 战败处理要用的那一处）
func note_shelter(scene_id: String, name_cn: String = "") -> void:
	if scene_id.is_empty():
		return
	last_shelter_scene = scene_id
	last_shelter_name = name_cn


## 战斗外增益这份数据「现在住在哪儿」：有存档就住存档（可持久化），没有就住会话内存。
func _field_rows() -> Array:
	return state.field_buffs if state != null else field_buffs


func _set_field_rows(rows: Array) -> void:
	if state != null:
		state.field_buffs = rows
	else:
		field_buffs = rows


## 加一层战斗外增益。同 buff 重复获得 = 刷新到期时间（叠层类按 `max_stack` 累加，与 buff_def 口径一致）。
## 注意：`max_stack` 由**调用方**从表里取（`BuffService.max_stack_of`）——会话层不读表，
## 现在唯一的来源「打坐余韵」是 refresh 类，所以默认 1 就够；将来加叠层类 field buff 时记得传。
func add_field_buff(
	buff_id: String, minutes: int, source_id: String = "", max_stack: int = 1
) -> Dictionary:
	if buff_id.is_empty() or minutes <= 0:
		return {"ok": false, "error": "战斗外增益要 buff_id 与正数分钟", "buff_id": buff_id}
	var expires_at := int(Time.get_unix_time_from_system()) + minutes * 60
	var rows := _field_rows()
	for entry: Dictionary in rows:
		if str(entry["buff_id"]) != buff_id:
			continue
		entry["expires_at"] = expires_at
		entry["stacks"] = mini(maxi(1, max_stack), int(entry.get("stacks", 1)) + 1)
		return {
			"ok": true, "refreshed": true, "buff_id": buff_id,
			"minutes": minutes, "stacks": int(entry["stacks"]),
		}
	rows.append({
		"buff_id": buff_id, "source_id": source_id, "stacks": 1, "expires_at": expires_at,
	})
	_set_field_rows(rows)
	return {"ok": true, "refreshed": false, "buff_id": buff_id, "minutes": minutes, "stacks": 1}


## 现在还有效的战斗外增益（顺手清掉过期的），每条多带一个 `remaining_sec`。
## `now` 可注入：用例不用真等 10 分钟也能验过期。
func active_field_buffs(now: int = -1) -> Array:
	var clock := now if now >= 0 else int(Time.get_unix_time_from_system())
	var alive: Array = []
	var out: Array = []
	for entry: Dictionary in _field_rows():
		if int(entry.get("expires_at", 0)) <= clock:
			continue
		alive.append(entry)
		var copy := entry.duplicate(true)
		copy["remaining_sec"] = int(entry["expires_at"]) - clock
		out.append(copy)
	_set_field_rows(alive)
	return out


## 清掉全部战斗外增益（08：败北要把它们全部清除）。返回清掉几条。
func clear_field_buffs() -> int:
	var count := _field_rows().size()
	_set_field_rows([])
	return count


func clear() -> void:
	state = null
	pending_encounter = null
	cleared_spawns.clear()
	world_position = Vector2.ZERO
	last_battle = {}
	pending_local_scene = ""
	pending_local_room = ""
	pending_return_scene = "res://scenes/world_run.tscn"
	local_position = Vector2.ZERO
	local_position_scene = ""
	local_maps.clear()
	last_shelter_scene = ""
	last_shelter_name = ""
	field_buffs.clear()
