## 观察点的**可见标记**（设计 15 §一：「可交互物要在低饱和背景里明显跳出来——暖色提亮」）。
##
## 观察点在地图上只是一根光秃秃的 `Marker2D`：不加这个标记，玩家只会从旁边走过去，
## 而它们正是设计 20 §3.2 要的「探索感」来源（`框架说明.md` 决策 277 就记着「这条要开发侧接」）。
##
## **为什么单独一个文件**：小地图与大地图**各自**收观察点（同一行只在一侧生效），两边要挂同一个视觉
## ——颜色／大小／节点名只在这里写一次，别各画一套。
##
## 美术给贴图之前先用**程序化的暖色菱形**：与「精英明雷贴图缺失退回程序化光晕」（决策 304）同一套做法，
## 素材到了再按同名替换，代码不用动。
extends Node2D

## 暖色（15 §一的「暖色提亮」）：橙黄系 + 半透明填充——在低饱和地形上跳出来，又不跟宝箱抢戏
const WARM := Color(1.0, 0.76, 0.38, 0.92)
## 菱形半径（像素）：格子 32px，半径 6 ≈ 三分之一格
const HALF := 6.0
## 填充透明度（描边用 `WARM` 本身）
const FILL_ALPHA := 0.35
## 节点名：用例与场景自检按它找（改名要连着改 `tests/`）
const NODE_NAME := "FlavorHighlight"


func _init() -> void:
	name = NODE_NAME


func _draw() -> void:
	var outline := PackedVector2Array([
		Vector2(0, -HALF), Vector2(HALF, 0), Vector2(0, HALF), Vector2(-HALF, 0),
	])
	draw_colored_polygon(outline, Color(WARM.r, WARM.g, WARM.b, FILL_ALPHA))
	var loop := outline.duplicate()
	loop.append(outline[0])
	draw_polyline(loop, WARM, 1.0)


## 「看得见吗」的判据——用例与自检用它，**别让谁把视觉改没了还没人知道**
## （headless 抓不到渲染，所以判据落在「节点在、可见、颜色是暖色且不透明」这三点上）。
static func is_visible_highlight(node) -> bool:
	if node == null or not (node is Node2D):
		return false
	return node.visible and (node as Node2D).modulate.a > 0.5 and WARM.a > 0.5
