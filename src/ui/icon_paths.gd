## 图标路径的**唯一出处**（设计 15 §六「命名规范」）：
##
## | 类别 | 路径 | 例 |
## |---|---|---|
## | 武学图标 | `assets/icons/skill/<skill_id>.png` | `sk_xuanwei_01.png` |
## | 装备图标 | `assets/icons/equip/<equip_id>.png` | `eq_sword_01.png` |
## | 物品图标 | `assets/icons/item/<item_id>.png` | `item_iron.png` |
## | 增益图标 | `assets/icons/buff/<buff_id>.png` | `buff_guard.png` |
## | 角色／敌人立绘 | `assets/sprites/<faction>/<id>.png` | `sprites/bandit/en_bd_thug.png` |
##
## 为什么单开一份：这几条路径以前散在各处（大地图标记在 `overworld_controller.ICON_DIR`、
## 精英标识在 `roaming_enemy.ELITE_MARKER_DIR`），再加「武学／道具」两处就是**四个副本**——
## 换目录时漏一处就是「有些图忽然不显示了」这种没人会红的毛病。
##
## **没图是常态**（美术约 160 个图标只交付了一小部分）：所以这里提供
## `load_icon()` / `make_icon()`——**有图才摆，没图返回 null**，调用方不要自己 `load()`
## （加载失败会刷一条引擎错误，而"这张图还没出"不是错误）。
class_name IconPaths
extends RefCounted

const ROOT := "res://assets/icons/"
const SKILL_DIR := ROOT + "skill/"
const EQUIP_DIR := ROOT + "equip/"
const ITEM_DIR := ROOT + "item/"
const BUFF_DIR := ROOT + "buff/"
## 异常状态图标（15 §4.3 的「异常状态 4」）。**目录约定先由开发侧定在 `icons/status/`**：
## `status_effect.icon` 的值就是文件名（如 `status_poison.png`），空则退回行 id——
## 与 `item_base.icon`／`buff_def.icon` 同一套优先级。这条路径待设计侧在交接单里确认（`待策划确认.md` Q80）。
const STATUS_DIR := ROOT + "status/"
## 七维图标（15 §六 命名表，2026-10-04 设计拍）：**`attribute_def` 没有 `icon` 列**，
## 一律按行 id 走（`icons/attr/<attr_id>.png`）；派生数值那批同理（`icons/stat/<stat_id>.png`）。
const ATTR_DIR := ROOT + "attr/"
const STAT_DIR := ROOT + "stat/"
## 角色与敌人的**剪影／立绘**不在 `icons/` 下，而在 `assets/sprites/<faction>/`（15 §六）
const SPRITES_DIR := "res://assets/sprites/"

## 图标显示尺寸：15 §六 定的是原生 32×32（与地编的贴图同规格）
const SIZE := 32


static func skill(skill_id: String) -> String:
	return SKILL_DIR + skill_id.strip_edges() + ".png" if not skill_id.strip_edges().is_empty() else ""


static func equip(equip_id: String) -> String:
	return EQUIP_DIR + equip_id.strip_edges() + ".png" if not equip_id.strip_edges().is_empty() else ""


static func item(item_id: String) -> String:
	return ITEM_DIR + item_id.strip_edges() + ".png" if not item_id.strip_edges().is_empty() else ""


static func buff(buff_id: String) -> String:
	return BUFF_DIR + buff_id.strip_edges() + ".png" if not buff_id.strip_edges().is_empty() else ""


static func status(status_id: String) -> String:
	return STATUS_DIR + status_id.strip_edges() + ".png" if not status_id.strip_edges().is_empty() else ""


static func attr(attr_id: String) -> String:
	return ATTR_DIR + attr_id.strip_edges() + ".png" if not attr_id.strip_edges().is_empty() else ""


static func stat(stat_id: String) -> String:
	return STAT_DIR + stat_id.strip_edges() + ".png" if not stat_id.strip_edges().is_empty() else ""


## 角色／敌人的剪影：`assets/sprites/<faction>/<enemy_id>.png`（15 §六）。
## `faction` 就是 `enemy_base.faction`（bandit／neutral／beast／rogue／hidden／training）。
static func actor(faction: String, actor_id: String) -> String:
	var who := actor_id.strip_edges()
	var side := faction.strip_edges()
	if who.is_empty() or side.is_empty():
		return ""
	return SPRITES_DIR + side + "/" + who + ".png"


## 有这张图就返回它，没有就返回 null（**不报错**——还没出图不是数据错）
static func load_icon(path: String) -> Texture2D:
	if path.is_empty() or not ResourceLoader.exists(path):
		return null
	return ResourceLoader.load(path) as Texture2D


## 按路径摆一个图标：没图返回 null，调用方据此决定「这一行要不要留位置」
static func make_icon(path: String, node_name: String = "Icon") -> TextureRect:
	var texture := load_icon(path)
	if texture == null:
		return null
	var rect := TextureRect.new()
	rect.name = node_name
	rect.texture = texture
	rect.custom_minimum_size = Vector2(SIZE, SIZE)
	rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	rect.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	return rect
