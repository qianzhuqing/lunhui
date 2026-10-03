## 音效：事件名 ↔ 文件、播放层与总线。
##
## 播放层是**复用**来的 MIT 项目 `godot_sound_manager`（`addons/sound_manager/`，出处见
## `docs/dev/第三方代码.md`「第五轮」）；音频文件是 `tools/audio/make_placeholders.py`
## 程序化合成的占位音。这条用例守的是"两边别漂"：
##   ① 每个事件指向的文件必须存在且真的能当音频加载；
##   ② 磁盘上的音频文件必须每个都有事件在放（不然就是放不响的孤儿素材）；
##   ③ 没登记的事件名**不许静默**（写错事件名要出声，不是哑炮）；
##   ④ 总线布局里得有播放层认的「Sounds」——落到 Master 就说明"接上了"是假的。
## 真实路径上的播放（开箱／火盆／命中／胜负）钉在 `test_local_map` 与 `test_battle_ui` 里，
## 这里只管契约与映射。
extends "res://tests/test_case.gd"

const SfxScript := preload("res://src/audio/sfx.gd")
const SoundEffectsScript := preload("res://addons/sound_manager/sound_effects.gd")

const SOUND_DIR := "res://assets/audio/sfx"
const BUS_LAYOUT := "res://default_bus_layout.tres"
const PLAYER_LICENSE := "res://addons/sound_manager/LICENSE"


func suite_name() -> String:
	return "音效事件与总线"


func run() -> void:
	_check_events_have_files()
	_check_no_orphan_files()
	_check_unknown_event_is_loud()
	_check_bus_layout()
	_check_reused_player_license()


func _check_events_have_files() -> void:
	var checked := 0
	for event_id: String in SfxScript.event_ids():
		var path: String = SfxScript.EVENTS[event_id]
		checked += 1
		check_true(FileAccess.file_exists(path), "事件 %s 指向的文件在：%s" % [event_id, path])
		check_true(load(path) is AudioStream, "事件 %s 的文件能当音频加载：%s" % [event_id, path])
	check_gt(float(checked), 3.0, "登记了足够多的音效事件（%d 个）" % checked)


func _check_no_orphan_files() -> void:
	var dir := DirAccess.open(SOUND_DIR)
	check_not_null(dir, "音频目录在：%s（缺了就重跑 tools/audio/make_placeholders.py）" % SOUND_DIR)
	if dir == null:
		return
	var known := {}
	for event_id: String in SfxScript.event_ids():
		known[str(SfxScript.EVENTS[event_id])] = event_id
	var scanned := 0
	for file_name: String in dir.get_files():
		if not file_name.ends_with(".wav"):
			continue
		scanned += 1
		var full := "%s/%s" % [SOUND_DIR, file_name]
		check_true(known.has(full), "磁盘上的 %s 有事件在放它（没人放就该删掉或补事件）" % file_name)
	check_eq(scanned, known.size(), "音频文件数与事件数一一对应（%d 个文件）" % scanned)


func _check_unknown_event_is_loud() -> void:
	SfxScript.clear_log()
	# 这一行会 push_error（引擎日志门限盯着它）——所以它同时验了"不静默"这一条
	SfxScript.play("no_such_event")
	check_eq(SfxScript.played_events().size(), 0, "没登记的事件名不进播放记录（不静默当哑炮）")

	SfxScript.clear_log()
	SfxScript.play("hit")
	check_true(SfxScript.has_played("hit"), "登记过的事件能放")
	check_eq(SfxScript.played_events().size(), 1, "放一次记一次")

	SfxScript.clear_log()
	check_eq(SfxScript.played_events().size(), 0, "clear_log 能把记录清空（用例之间不互相污染）")


func _check_bus_layout() -> void:
	# 最强的一条：**启动时**项目就已经把我们的布局装上了（Godot 默认读 res://default_bus_layout.tres）。
	# 只断言"文件能加载"是不够的——文件在、但没人读，播放层照样会静默落回 Master。
	check_gt(float(AudioServer.get_bus_index("Sounds")), 0.0, "启动时就有「Sounds」总线（实际 %d）" % AudioServer.get_bus_index("Sounds"))
	var layout = load(BUS_LAYOUT)
	check_true(layout is AudioBusLayout, "总线布局资源能加载：%s" % BUS_LAYOUT)
	if not (layout is AudioBusLayout):
		return
	AudioServer.set_bus_layout(layout)
	var names := PackedStringArray()
	for index in range(AudioServer.bus_count):
		names.append(AudioServer.get_bus_name(index))
	check_true(names.has("Sounds"), "总线布局里有播放层认的「Sounds」（实际 %s）" % ", ".join(names))
	# 播放层自己怎么挑总线：`abstract_audio_player_pool.get_possible_bus()` 按候选名找，
	# 一个都找不到会**静默落回 Master**——所以这里拿真播放层问一次，而不是只看布局文件。
	var pool = SoundEffectsScript.new(["Sounds", "SFX"], 1)
	check_eq(str(pool.bus), "Sounds", "播放层解析到的默认总线是 Sounds（落到 Master 说明布局没生效）")
	pool.free()


func _check_reused_player_license() -> void:
	check_true(FileAccess.file_exists(PLAYER_LICENSE), "复用来的播放层带着许可证：%s" % PLAYER_LICENSE)
	var text := FileAccess.get_file_as_string(PLAYER_LICENSE)
	check_true(text.contains("MIT License"), "许可证确实是 MIT（第三方代码只收 MIT／CC0 这类能商用的）")
