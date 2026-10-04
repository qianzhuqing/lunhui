## 临时截图工具（用完即删）：把某个界面在**真窗口**里渲染成 PNG，好跟示意图逐块对观感。
## 用法：godot --path . --script res://tools/_shot.gd -- <场景> <输出.png> [scope] [scene_id]
extends SceneTree

var _out := "res://.logs/ui_shot.png"
var _frames := 0
var _done := false


var _node: Node = null


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var scene_path: String = args[0] if args.size() > 0 else "res://scenes/character_screen.tscn"
	if args.size() > 1:
		_out = args[1]
	_node = load(scene_path).instantiate()
	if _node != null:
		if args.size() > 2 and "scope" in _node:
			_node.scope = args[2]
		if args.size() > 3 and "scene_id" in _node:
			_node.scene_id = args[3]
		get_root().add_child(_node)


## 开局那一帧 autoload 还没 `_ready`，所以建局要等到第二帧再做。
func _prepare() -> void:
	var session := get_root().get_node_or_null("GameSession")
	var game_data := get_root().get_node_or_null("GameData")
	if session != null and game_data != null and game_data.db != null and session.state == null:
		session.set_state(load("res://src/core/game_state.gd").new_game(
			game_data.db, "normal", PackedStringArray(["scholar_fallen"])
		))
	if _node != null and _node.has_method("setup"):
		_node.setup()
	# `setup()` 是幂等的（面板可能已经被 `_ready` 早跑过一遍、那会儿还没有局），
	# 所以这里把「选中的人」补上、再 refresh 一次，让内容真按这一局重画。
	var state = session.state if session != null else null
	if state != null and not state.char_ids.is_empty():
		if _has_prop(_node, "_selected_char") and str(_node.get("_selected_char")).is_empty():
			_node.set("_selected_char", str(state.char_ids[0]))
		if _node != null and _node.has_method("refresh"):
			_node.refresh()


## `"x" in node` 对脚本变量不可靠（踩过），改成翻属性表
func _has_prop(node: Object, prop: String) -> bool:
	if node == null:
		return false
	for entry: Dictionary in node.get_property_list():
		if str(entry["name"]) == prop:
			return true
	return false


func _process(_delta: float) -> bool:
	_frames += 1
	if _frames == 2:
		_prepare()
	if _frames < 4 or _done:
		return _done
	_done = true
	var image := get_root().get_texture().get_image()
	var error := image.save_png(ProjectSettings.globalize_path(_out))
	# 另存一张缩到 512 宽的预览——看观感用（大图不塞进上下文，AGENTS 有这条纪律）
	var small := image.duplicate()
	small.resize(512, 288, Image.INTERPOLATE_LANCZOS)
	small.save_png(ProjectSettings.globalize_path(_out.get_basename() + "_small.png"))
	print("SHOT %s ok=%s %dx%d" % [_out, error == OK, image.get_width(), image.get_height()])
	return true
