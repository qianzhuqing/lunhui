using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 一次性工具：只重建各主题的 TileSet（`assets/tilesets/*/*_tileset.tres`），不动地图场景
/// （对位旧链 `regen_tilesets.gd`，该 `.gd` 已退场）。
///
/// 用途：MapKit 给 TileSet 加了新的自定义数据层（0.32.0 的 `covering`＝会不会盖住玩家）之后，
/// 仓库里旧的 `.tres` 上没有这一层——必须重存一次才带上。地编下次跑 `build_*` 也会带上，
/// 这个脚本只是让「加一层数据」这件事不必等整张地图重建。
///
/// 用法（`--script` 只认 GDScript，C# 侧只能挂场景）：
///   godot --headless --path . --log-file .logs\regen_tilesets_cs.log ^
///         res://tools/mapgen/regen_tilesets_cs.tscn
/// </summary>
public partial class RegenTilesetsCs : Node
{
    public override void _Ready()
    {
        Callable.From(() => GetTree().Quit(Run())).CallDeferred();
    }

    private static int Run()
    {
        var failed = new System.Collections.Generic.List<string>();
        foreach (Variant themeVariant in MapKit.themes().Keys)
        {
            string theme = themeVariant.AsString();
            var tileSet = MapKit.save_theme_tile_set(theme);
            if (tileSet == null)
            {
                failed.Add(theme);
                continue;
            }

            bool covering = tileSet.GetCustomDataLayersCount() > 1;
            GD.Print($"[regen] {theme} → {MapKit.theme_tileset_path(theme)}（covering 数据层 "
                + $"{(covering ? "有" : "缺")}）");
        }

        if (failed.Count == 0)
        {
            GD.Print("REGEN: OK");
            return 0;
        }

        GD.Print($"REGEN: FAILED（{string.Join("、", failed)}）");
        return 1;
    }
}
