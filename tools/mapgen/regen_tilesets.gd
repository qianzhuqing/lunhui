## 一次性工具：只重建各主题的 TileSet（`assets/tilesets/*/*_tileset.tres`），不动地图场景。
##
## 用途：`map_kit` 给 TileSet 加了新的自定义数据层（0.32.0 的 `covering`＝会不会盖住玩家）之后，
## 仓库里旧的 `.tres` 上没有这一层——必须重存一次才带上。地编下次跑 `build_*` 也会带上，
## 这个脚本只是让「加一层数据」这件事不必等整张地图重建。
##
## 用法：
##   godot --headless --path . --log-file .logs\regen_tilesets.log --script res://tools/mapgen/regen_tilesets.gd
extends SceneTree

const MapKit := preload("res://tools/mapgen/map_kit.gd")


func _initialize() -> void:
	var failed := PackedStringArray()
	for theme: String in MapKit.THEMES:
		var tile_set: TileSet = MapKit.save_theme_tile_set(theme)
		if tile_set == null:
			failed.append(theme)
			continue
		var covering := tile_set.get_custom_data_layers_count() > 1
		print("[regen] %s → %s（covering 数据层 %s）" % [
			theme, MapKit.theme_tileset_path(theme), "有" if covering else "缺",
		])
	if failed.is_empty():
		print("REGEN: OK")
		quit(0)
		return
	print("REGEN: FAILED（%s）" % "、".join(failed))
	quit(1)
