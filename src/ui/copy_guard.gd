## 界面文案守卫：扫一个界面下**所有控件文字**里「表内英文 id 形态」的 token。
##
## AGENTS 的硬规矩：给玩家看的界面文案不许出现表里的英文 id（`dot_poison`／`eq_sword_01`／
## `facility_pawnshop`…），要用表里的 `name_cn`。这条规矩原来只被 `test_copy_audit` 盯着两个界面
## （启动菜单与枢纽），而**活的那一条**（枢纽页「章节：第 1 章（chapter_01）」）就是那么漏出去的；
## 2026-10-03 把判据抽到这里，接进**每一个场景自检**（见框架说明决策 244）。
##
## 判据：小写字母打头、中间带下划线的 token（`[a-z][a-z0-9]*_[a-z0-9_]+`）。
## 中文标点、`Lv1`、`F5`、`Tab`、`Esc`、`1x/2x/4x` 都不带下划线，不会误伤；
## **扫描不看可见性**，所以 TabContainer 里没被切到的页也能扫到（容器只是跳过排版，行本身在）。
class_name CopyGuard
extends RefCounted

const ID_TOKEN_PATTERN := "[a-z][a-z0-9]*_[a-z0-9_]+"


## 这个界面里所有「像表内 id」的文字，形如 ["节点名：原文", ...]
static func id_tokens(root: Node) -> PackedStringArray:
	var out := PackedStringArray()
	var token_re := RegEx.new()
	token_re.compile(ID_TOKEN_PATTERN)
	for entry: Dictionary in _texts(root):
		var text := str(entry["text"])
		if token_re.search(text) != null:
			out.append("%s：%s" % [str(entry["node"]), text.strip_edges()])
	return out


## 机器可读的一行（自检日志里捞它，与 LAYOUT／CONTENT／BACKDROP 同一口径；刻意用 ASCII）
static func ascii_line(root: Node) -> String:
	var found := id_tokens(root)
	return "COPY id_tokens=%d ok=%s" % [found.size(), "true" if found.is_empty() else "false"]


## 递归取控件文字：Label／Button／RichTextLabel（其余控件没有玩家可见文字）
static func _texts(node: Node) -> Array:
	var out: Array = []
	for child in node.get_children():
		var text := ""
		if child is Label:
			text = (child as Label).text
		elif child is Button:
			text = (child as Button).text
		elif child is RichTextLabel:
			text = (child as RichTextLabel).text
		if not text.strip_edges().is_empty():
			out.append({"node": String(child.name), "text": text})
		out.append_array(_texts(child))
	return out
