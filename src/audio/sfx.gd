## 音效播放入口（事件名 → 音频文件的**唯一出处**）。
##
## 各处只写 `Sfx.play("hit")`，不写音频路径：音频路径是**给音效侧的替换契约**——
## 现在放的是 `tools/audio/make_placeholders.py` 程序化合成的占位音，音效侧到位后
## **按同名文件替换**即可，代码一行都不用改（同「贴图缺失才退回色块」的口径）。
##
## 播放层不是自己写的：直接复用 MIT 的 `godot_sound_manager`
## （`addons/sound_manager/`，v2.6.2，出处与许可见 `docs/dev/第三方代码.md`「第五轮」）。
## 它按需挂到场景树 root 上，**不给 `project.godot` 加 autoload**——这样 `--script`
## （自检）模式与真窗口两条路都不需要额外初始化；没有主循环时只记账、不上声（不报错）。
##
## 事件名取自设计 07 §8.4 的「特效与音效位」，不是开发侧新编的内容：
## 命中／暴击（伤害跳字那一档）、宝箱开启、火盆点燃、战斗胜负。
extends RefCounted

const SoundManagerScript := preload("res://addons/sound_manager/sound_manager.gd")
const SettingsStoreScript := preload("res://src/core/settings_store.gd")

const EVENTS := {
	"hit": "res://assets/audio/sfx/sfx_hit.wav",
	"crit": "res://assets/audio/sfx/sfx_crit.wav",
	"victory": "res://assets/audio/sfx/sfx_victory.wav",
	"defeat": "res://assets/audio/sfx/sfx_defeat.wav",
	"chest": "res://assets/audio/sfx/sfx_chest.wav",
	"brazier": "res://assets/audio/sfx/sfx_brazier.wav",
}

## 记账上限：只给用例看「这一小段里放没放过某个事件」，不是无限增长的历史。
const LOG_LIMIT := 64
## 音效走的总线（与 `default_bus_layout.tres` 和播放层的候选名单一致）
const SOUND_BUS := "Sounds"
## 音量档位：枢纽页那个按钮按一下轮换一档（0 = 静音）
const VOLUME_STEPS := [0.0, 0.25, 0.5, 0.75, 1.0]
const DEFAULT_VOLUME := 1.0
## 静音的 dB：`linear_to_db(0)` 算出来是 -inf，明确写 -80 dB 更好收拾
const MUTE_DB := -80.0

static var _played: PackedStringArray = PackedStringArray()
static var _manager: Node = null
static var _volume := DEFAULT_VOLUME
static var _volume_loaded := false


## 放一个音效事件。没登记过的事件名**出声**（事件名写错时静默就成了哑炮）。
static func play(event_id: String) -> void:
	if not EVENTS.has(event_id):
		push_error("[Sfx] 没有登记过的音效事件：'%s'（要加事件只改本文件的 EVENTS）" % event_id)
		return
	_played.append(event_id)
	if _played.size() > LOG_LIMIT:
		_played = _played.slice(_played.size() - LOG_LIMIT)
	var stream := _stream_for(event_id)
	if stream == null:
		return
	var manager := _manager_node()
	if manager == null:
		return
	manager.play_sound(stream)


## 这一小段里放过哪些事件（用例读它；真玩的时候没人读）
static func played_events() -> PackedStringArray:
	return _played.duplicate()


static func has_played(event_id: String) -> bool:
	return _played.has(event_id)


static func clear_log() -> void:
	_played = PackedStringArray()


## 当前音量（0..1）。第一次问的时候从设置文件读一次；读不到就用默认值。
static func volume() -> float:
	_ensure_volume_loaded()
	return _volume


## 改音量：当场生效（写总线），并把设置落盘。
## 写不进去**不挡游戏**——本次运行内照常生效，只记一条警告（与存档同一条纪律）。
static func set_volume(value: float, persist: bool = true) -> Dictionary:
	_ensure_volume_loaded()
	_volume = clampf(value, 0.0, 1.0)
	_apply_volume()
	if not persist:
		return {"ok": true, "error": ""}
	var store = SettingsStoreScript.new()
	var result: Dictionary = store.save_volume(_volume)
	if not bool(result["ok"]):
		push_warning("[Sfx] 音量设置没落盘：%s（本次运行内仍然生效）" % str(result["error"]))
	return result


## 轮换到下一档音量（枢纽页的按钮用它）：0 → 25% → 50% → 75% → 100% → 0。
## 设置文件里写了非标准值时，取「比它大的第一档」，到顶回到静音。
static func cycle_volume(persist: bool = true) -> float:
	var current := volume()
	var next := float(VOLUME_STEPS[0])
	for step in VOLUME_STEPS:
		if float(step) > current + 0.001:
			next = float(step)
			break
	set_volume(next, persist)
	return next


## 给界面看的文案（0 说「静音」，不说「0%」）
static func volume_text() -> String:
	var current := volume()
	if current <= 0.0:
		return "音量：静音"
	return "音量：%d%%" % int(round(current * 100.0))


## 把当前音量写到总线上。
##
## 播放层在的话让它自己设（复用它那套）；但**静音要自己写**：播放层的
## `set_sound_volume(0)` 会算出 `-inf` dB，不如明确写 -80。
static func _apply_volume() -> void:
	if _manager != null and is_instance_valid(_manager) and _volume > 0.0:
		_manager.set_sound_volume(_volume)
		return
	var bus := AudioServer.get_bus_index(SOUND_BUS)
	if bus < 0:
		return
	AudioServer.set_bus_volume_db(bus, MUTE_DB if _volume <= 0.0 else linear_to_db(_volume))


static func _ensure_volume_loaded() -> void:
	if _volume_loaded:
		return
	_volume_loaded = true
	var store = SettingsStoreScript.new()
	var result: Dictionary = store.load_volume()
	_volume = clampf(float(result["volume"]), 0.0, 1.0)
	_apply_volume()


## 把挂上去的播放层摘掉并释放（**只给自检收尾用**）。
##
## 为什么需要它：`--script` 模式下没人替我们回收挂在场景树 root 上的节点，跑完不退
## 就是一串 `ObjectDB instances were leaked at exit`（实测 31 个）。真玩的时候播放层
## 跟进程同寿，不需要这一步；`tools/run_tests.gd` 在所有用例跑完后调它。
static func shutdown() -> void:
	if _manager != null and is_instance_valid(_manager):
		var parent := _manager.get_parent()
		if parent != null:
			parent.remove_child(_manager)
		_manager.free()
	_manager = null
	_played = PackedStringArray()
	_volume = DEFAULT_VOLUME
	_volume_loaded = false


## 事件名列表（用例拿它跟磁盘上的文件名互相核对，防止「加了文件没人放」/「放了文件不存在」）
static func event_ids() -> Array:
	return EVENTS.keys()


static func _stream_for(event_id: String) -> AudioStream:
	var path: String = EVENTS[event_id]
	# 不做自己的缓存：`load()` 本来就命中引擎的资源缓存，而**静态字段会在退出时
	# 留住这些资源**（实测会报 "N resources still in use at exit"）——退出干净比省一次查表值钱。
	var stream := load(path) as AudioStream
	if stream == null:
		push_error("[Sfx] 音频加载失败：%s（缺文件就重跑 tools/audio/make_placeholders.py）" % path)
	return stream


static func _manager_node() -> Node:
	if _manager != null and is_instance_valid(_manager):
		return _manager
	# 别人（比如以后真把编辑器插件的 autoload 打开）先注册的话，用那一份，别造第二个
	if Engine.has_singleton("SoundManager"):
		var existing = Engine.get_singleton("SoundManager")
		if existing is Node and is_instance_valid(existing):
			_manager = existing
			return _manager
	var loop := Engine.get_main_loop()
	if not (loop is SceneTree):
		return null
	var root := (loop as SceneTree).root
	if root == null:
		return null
	var node: Node = SoundManagerScript.new()
	root.add_child(node)
	# 播放层的 `_init` 会把自己注册成引擎单例；单例是引擎持有的引用，退出时会报
	# "ObjectDB instances were leaked"（实测 12 个）。节点已经挂在场景树 root 上、
	# 生命周期由场景树管，所以这里把单例登记撤掉——两件事都保留，退出才干净。
	if Engine.has_singleton("SoundManager"):
		Engine.unregister_singleton("SoundManager")
	_manager = node
	# 建出来的第一件事：把当前音量套上（不然玩家在枢纽里设过的音量会被"新播放层"重置成 100%）
	_apply_volume()
	return _manager
