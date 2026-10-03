## 运行期注册输入映射。
##
## 不往 project.godot 里塞 InputEvent 的序列化文本（手写容易出错、也不好 review），
## 需要时调一次 `ensure()` 即可；重复调用不会重复注册。
class_name InputSetup
extends RefCounted

const ACTIONS := {
	"move_up": [KEY_W, KEY_UP],
	"move_down": [KEY_S, KEY_DOWN],
	"move_left": [KEY_A, KEY_LEFT],
	"move_right": [KEY_D, KEY_RIGHT],
	"sneak": [KEY_SHIFT],
	"interact": [KEY_E, KEY_SPACE],
	"open_character": [KEY_TAB],
	"show_progress": [KEY_M],
	"sweep": [KEY_J],
	"show_clues": [KEY_K],
	"save_game": [KEY_F5],
}


static func ensure() -> void:
	for action: String in ACTIONS:
		if InputMap.has_action(action):
			continue
		InputMap.add_action(action)
		for keycode: int in ACTIONS[action]:
			var event := InputEventKey.new()
			event.physical_keycode = keycode
			InputMap.action_add_event(action, event)


static func is_defined(action: String) -> bool:
	return InputMap.has_action(action)
