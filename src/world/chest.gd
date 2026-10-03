## 副本里的宝箱。
##
## 位点是地编放好的 `Chest_<drop_group>`；内容由 `drop_table` 决定，
## 开启后把掉落交给 `BattleReward` 落账，并在小地图会话里记成「已开」。
extends Area2D

signal opened(chest_id: String, summary: String)

const CONTACT_DISTANCE := 26.0

## 宝箱三档（设计 07：宝箱开启「三档」；07 的宝箱表也分铜/银/金三种掉落组）。
## 段落颜色与个头都由档位决定，玩家不用开就知道值不值得绕路。
const TIER_ORDER := ["gold", "silver", "copper"]
const TIER_COLORS := {
	"copper": Color("c07a45"),
	"silver": Color("c9ced6"),
	"gold": Color("e8c04a"),
}
const TIER_LABELS := {"copper": "铜箱", "silver": "银箱", "gold": "金箱"}
const TIER_SCALES := {"copper": 0.85, "silver": 1.0, "gold": 1.2}
const OPENED_COLOR := Color("6f6656")
## 宝箱贴图（地编已交付三档 × 开/未开，07 §8.3）：assets/sprites/props/chest_<tier>[_open].png
const SPRITE_DIR := "res://assets/sprites/props/"

var chest_id: String = ""
var opened_already := false
var player: Node2D = null
## 档位：copper / silver / gold（从掉落组名解析）
var tier: String = "copper"


func setup(group_id: String, player_node: Node2D, already_open: bool = false) -> void:
	chest_id = group_id
	player = player_node
	opened_already = already_open
	tier = resolve_tier(group_id)
	collision_layer = 0
	collision_mask = 0
	monitoring = false
	_build_placeholder()


## 从掉落组名（drop_chest_copper / _silver / _gold）解析档位；认不出来按铜箱
static func resolve_tier(group_id: String) -> String:
	for candidate: String in TIER_ORDER:
		if group_id.contains(candidate):
			return candidate
	# 认不出来时**要出声**：要么是命名写错了，要么是设计加了新档（那要同时补贴图/颜色/名字）。
	# 静默按铜箱处理会画出「档位不对的箱子」，而玩家只会觉得掉的东西跟箱子不搭。
	push_error("[Chest] 认不出宝箱档位：'%s'（命名要含 %s 之一）" % [group_id, ", ".join(TIER_ORDER)])
	return "copper"


func tier_id() -> String:
	return tier


func tier_color() -> Color:
	return TIER_COLORS.get(tier, TIER_COLORS["copper"])


func _build_placeholder() -> void:
	var shape := CollisionShape2D.new()
	var circle := CircleShape2D.new()
	circle.radius = 12.0
	shape.shape = circle
	add_child(shape)

	var scale_factor: float = float(TIER_SCALES.get(tier, 1.0))
	# 贴图优先：地编给了 chest_copper/silver/gold 与对应的 _open 图，就用它；
	# 找不到贴图才退回「按档位上色」的占位方块（不然一条贴图路径写错就什么都看不见）
	var texture_path := "%schest_%s%s.png" % [SPRITE_DIR, tier, "_open" if opened_already else ""]
	if ResourceLoader.exists(texture_path):
		var sprite := Sprite2D.new()
		sprite.name = "Body"
		sprite.texture = load(texture_path)
		sprite.position = Vector2(0, -7)
		sprite.scale = Vector2(scale_factor, scale_factor)
		add_child(sprite)
	else:
		var fallback := Polygon2D.new()
		fallback.name = "Body"
		fallback.color = tier_color() if not opened_already else OPENED_COLOR
		fallback.scale = Vector2(scale_factor, scale_factor)
		fallback.polygon = PackedVector2Array([
			Vector2(-10, -14), Vector2(10, -14), Vector2(10, 0), Vector2(-10, 0),
		])
		add_child(fallback)

	var label := Label.new()
	label.name = "State"
	label.text = "已开" if opened_already else str(TIER_LABELS.get(tier, "宝箱"))
	label.position = Vector2(-14, -32)
	label.add_theme_font_size_override("font_size", 11)
	label.add_theme_color_override("font_color", Color(1, 1, 1, 0.85))
	add_child(label)


func mark_opened() -> void:
	opened_already = true
	var body := get_node_or_null("Body")
	if body != null:
		var open_path := "%schest_%s_open.png" % [SPRITE_DIR, tier]
		if ResourceLoader.exists(open_path):
			(body as Sprite2D).texture = load(open_path)
		else:
			body.color = OPENED_COLOR
	var label := get_node_or_null("State")
	if label != null:
		label.text = "已开"


## 当前用的是哪张贴图（用例读它）
func body_texture_path() -> String:
	var body := get_node_or_null("Body")
	if body is Sprite2D and (body as Sprite2D).texture != null:
		return (body as Sprite2D).texture.resource_path
	return ""


## 玩家在范围内且没开过才能交互
func can_interact() -> bool:
	return not opened_already and player != null and global_position.distance_to(player.global_position) <= CONTACT_DISTANCE
