## 浮层栈（设计 18.1，0.18.1）：纯逻辑——谁被弹、谁被挤，全在返回值里。
##
## 界面那一侧的接线（商店 → 角色 → Esc 回商店）在
## `--local-selftest` 的「浮层栈」一段里验（真实场景、真实面板）。
extends "res://tests/test_case.gd"

const OverlayStackScript := preload("res://src/ui/overlay_stack.gd")


func suite_name() -> String:
	return "浮层栈（压栈 / 弹回 / 栈深上限）"


func run() -> void:
	var stack = OverlayStackScript.new()
	check_eq(OverlayStackScript.LIMIT, 3, "栈深上限是 3（设计 18.1 定死）")
	check_true(stack.is_empty(), "开局栈是空的")
	check_eq(stack.pop(), "", "空栈弹一层返回空串（调用方据此做场景层的事）")

	# 压栈：底 → 顶（`_join` 把栈打成 "底|…|顶" 好读）
	check_true(stack.push("shop").is_empty(), "压第一个不挤掉任何人")
	stack.push("character")
	stack.push("clue")
	check_eq(stack.depth(), 3, "压了三层")
	check_eq(stack.top(), "clue", "栈顶是最后压进去的那个")
	check_eq(_join(stack), "shop|character|clue", "顺序就是压进来的顺序")

	# 已达上限再压 → **挤掉栈底**
	var squeezed: Array = stack.push("dungeon")
	check_eq(squeezed.size(), 1, "超上限时挤掉一个（栈底）")
	check_eq(str(squeezed[0]), "shop", "挤掉的正是栈底那个")
	check_eq(stack.depth(), 3, "挤完仍然只有三层")
	check_false(stack.has("shop"), "被挤掉的已经不在栈里")
	check_eq(_join(stack), "character|clue|dungeon", "栈底被挤掉后，原来的第二层成了栈底")

	# 目标已在栈里 → **弹回到它**，不重复压（A→B→A 就是回到 A）
	var closed: Array = stack.push("clue")
	check_eq(closed.size(), 1, "弹回到 clue：把它上面的 dungeon 弹掉")
	check_eq(str(closed[0]), "dungeon", "被弹掉的是压在它上面的那个")
	check_eq(stack.top(), "clue", "弹回之后它就是栈顶")
	check_eq(_join(stack), "character|clue", "没有把 clue 再压一遍（栈深 2）")

	# 弹一层
	check_eq(stack.pop(), "clue", "弹一层拿到栈顶那个")
	check_eq(stack.depth(), 1, "弹掉一层")
	check_eq(stack.top(), "character", "新的栈顶是原本压在它下面的")

	# 程序化关闭（界面自己的「返回」按钮）：它上面的也要跟着走
	stack.push("shop")
	stack.push("clue")
	check_eq(_join(stack), "character|shop|clue", "「角色」在最底下，上面压着商店与线索本")
	var removed: Array = stack.remove("shop")
	check_eq(removed.size(), 1, "关掉中间那层时，压在它上面的一起走")
	check_eq(str(removed[0]), "clue", "被带走的是压在它上面的线索本")
	check_false(stack.has("shop"), "中间那层自己也没了")
	check_eq(_join(stack), "character", "只剩栈底那个")

	# clear（切场景时用）
	stack.clear()          # 先把上一段留下的栈底清掉，免得数错
	stack.push("a")
	stack.push("b")
	check_eq(stack.clear().size(), 2, "clear 返回原本栈里的全部")
	check_true(stack.is_empty(), "clear 之后栈是空的")


func _join(stack) -> String:
	var parts := PackedStringArray()
	for id: String in stack.ids():
		parts.append(id)
	return "|".join(parts)
