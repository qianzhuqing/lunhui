## 隐藏内容触发点。
##
## 位点是地编放好的 `Trigger_<trigger_id>`；触发类型与条件见 `hidden_trigger.csv`。
## 已实现五类：
##   item   —— 背包里有指定钥匙道具（酒葫芦 / 毒酒）
##   space  —— 背包里有指定工具，直接打开捷径（铁镐挖通 → 传送到目标房间）
##   carry  —— 带着某件东西进某房间（锈剑共鸣）
##   completion —— 本图宝箱全开（完成度统计就在会话状态里）
##   kill_style —— 按「上一场战斗怎么杀的」判定（毒杀毒手 → 掉独门秘籍）
##   behavior  —— 全程潜行不惊动守卫（潜行入寨；被撞上就算失败）
##   sequence  —— 顺序谜题（三火盆：按 1-3-2 依次点燃）。
##
## 顺序谜题的位点命名约定（07「地图资源需求」里写明）：三个火盆分别是
## `Trigger_trig_brazier_1`／`_2`／`_3`——**同一个 `hidden_trigger` 行、三个编号位点**。
## 控制器负责解析编号（`sequence_index`），本类只记住它。
extends Area2D

signal activated(trigger_id: String, summary: String)

const CONTACT_DISTANCE := 30.0

## 火盆（顺序谜题）用地图侧交付的**两态贴图**：灭／燃。
## 设计 07 §8.3 的可交互物件里写着「火盆（灭／燃）」，§8.4 的音效／特效位里也有「火盆点燃」；
## 贴图早就进了项目（`assets/sprites/props/brazier_off.png` / `_on.png`），但**代码一次都没用过**——
## 那时火盆只是个紫色多边形，玩家看不出自己点着了几个。其余触发类型的专用贴图还没交付，
## 仍走「按状态配色」的占位多边形。
const PROP_DIR := "res://assets/sprites/props/"
const BRAZIER_OFF := PROP_DIR + "brazier_off.png"
const BRAZIER_ON := PROP_DIR + "brazier_on.png"

const SUPPORTED_TYPES := ["item", "space", "carry", "completion", "kill_style", "behavior", "sequence"]

var trigger_id: String = ""
var row: Resource = null
var used := false
var player: Node2D = null
## 火盆专用：这一格现在是不是点着的（其余类型恒 false）
var lit := false
## sequence 类专用：这个位点是第几个火盆（1／2／3）；其余类型是 0。
## 由控制器从位点名 `..._<n>` 里解析出来——表里只有一行谜题，区分靠地图上的编号。
var sequence_index: int = 0
## 取显示名用（`requirement_text` 要把 `item_pickaxe` 说成「铁镐」）；
## 直接 new 出来自用的用例可以不传，这时退回原样显示 id
var db = null


func setup(
	trigger_row: Resource, player_node: Node2D, already_used: bool = false, table_db = null,
	seq_index: int = 0
) -> void:
	row = trigger_row
	trigger_id = str(trigger_row.trigger_id)
	player = player_node
	used = already_used
	lit = already_used and _is_brazier()   # 进图时这条隐藏已经做过 → 火盆应该是燃的
	db = table_db
	sequence_index = seq_index
	collision_layer = 0
	collision_mask = 0
	monitoring = false
	_build_placeholder()


func _build_placeholder() -> void:
	var shape := CollisionShape2D.new()
	var circle := CircleShape2D.new()
	circle.radius = 14.0
	shape.shape = circle
	add_child(shape)

	if _is_brazier() and ResourceLoader.exists(_brazier_sprite_path()):
		var sprite := Sprite2D.new()
		sprite.name = "Body"
		sprite.texture = load(_brazier_sprite_path())
		sprite.position = Vector2(0, -8)
		add_child(sprite)
	else:
		# 贴图没到位（或不是火盆）就退回原来的占位多边形：不然这间屋子会空一格
		var body := Polygon2D.new()
		body.name = "Body"
		body.color = _polygon_color()
		body.polygon = PackedVector2Array([
			Vector2(0, -14), Vector2(12, 0), Vector2(0, 14), Vector2(-12, 0),
		])
		add_child(body)

	var label := Label.new()
	label.name = "Name"
	label.text = str(row.name_cn)
	label.position = Vector2(-26, -34)
	label.add_theme_font_size_override("font_size", 12)
	label.add_theme_color_override("color", Color(1, 1, 1, 0.85))
	label.add_theme_color_override("font_color", Color(1, 1, 1, 0.85))
	add_child(label)


func _is_brazier() -> bool:
	return row != null and str(row.trigger_type) == "sequence"


func _brazier_sprite_path() -> String:
	return BRAZIER_ON if lit else BRAZIER_OFF


## 占位多边形的配色（与旧的三个取值逐字一致，用例在按颜色断言）
func _polygon_color() -> Color:
	if lit:
		return Color("e8a24a")          # 燃：暖橙
	if used:
		return Color("3f3f3f")
	return Color("b07cc6") if supported() else Color("5a5a5a")


## 点燃／熄灭这一格的视觉（顺序谜题用）：控制器按次序判定结果调它。
## 贴图缺了就退回颜色区分，不静默——玩家至少能看出「点着了 vs 灭了」。
func set_lit(value: bool) -> void:
	lit = value
	var body := get_node_or_null("Body")
	if body == null:
		return
	if body is Sprite2D:
		var path := _brazier_sprite_path()
		if ResourceLoader.exists(path):
			(body as Sprite2D).texture = load(path)
		return
	(body as Polygon2D).color = _polygon_color()


## 这一格现在显示的是哪张占位贴图（用例读它，验「地编交付的图真的被用上了」）
func brazier_texture_path() -> String:
	var body := get_node_or_null("Body")
	if body is Sprite2D and (body as Sprite2D).texture != null:
		return (body as Sprite2D).texture.resource_path
	return ""


func supported() -> bool:
	return SUPPORTED_TYPES.has(str(row.trigger_type)) if row != null else false


## 说明这一类为什么还没实现（回报用）。七类现在都实现了，所以正常情况下返回空串；
## 留着这个接口，是为了下一类新触发类型落地前还能如实回报，而不是静默点不动。
func unsupported_reason() -> String:
	return ""


func can_interact() -> bool:
	if used or player == null or row == null:
		return false
	if not supported():
		return false
	return global_position.distance_to(player.global_position) <= CONTACT_DISTANCE


func requirement_text() -> String:
	# 顺序谜题把「该按什么次序点」写给玩家（这条在表里，不是代码编的）
	if str(row.trigger_type) == "sequence":
		var order := _condition_value(str(row.required_condition), "sequence")
		if order.is_empty():
			return "按序点燃"
		return "按 %s 次序点燃" % order.replace("-", " → ")
	var item_id := str(row.required_item)
	if item_id.is_empty():
		return "无需求"
	if db != null:
		return "需要 %s" % db.display_name(item_id)
	return "需要 %s" % item_id


func mark_used() -> void:
	used = true
	var body := get_node_or_null("Body")
	if body == null:
		return
	if body is Sprite2D:
		# 火盆点着了就一直是燃的（贴图本身表达了「已完成」；压成灰反而像灭了）
		set_lit(true)
		return
	body.color = _polygon_color()


## 从 `required_condition`（形如 `sequence=1-3-2`）里取一个键；取不到返回空串。
## 与控制器里的 `_condition_text` 同一口径（表里只有一处写这个条件，这里只读不写）。
static func _condition_value(condition: String, key: String) -> String:
	for part: String in condition.split(";", false):
		var pieces := part.split("=", false)
		if pieces.size() == 2 and pieces[0].strip_edges() == key:
			return pieces[1].strip_edges()
	return ""
