using System.Collections.Generic;
using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Tools.Mapgen;
using Lunhui.World;

namespace Lunhui.Tests;

/// <summary>
/// `TestMapAssets` 的**资产与环境**那一半：地表分层（挡路只许画在 Decor／`covering` 数据层／
/// `Conditional` 层）、主题 TileSet 的 solid 逐格核对、三类图标文件名对表、指路牌位点。
/// 拆文件只为不撞 800 行硬上限（同 `TestBattleUi` 的三份拆法）——**假绿门限按注册的那一份
/// 数静态断言点**，两份合起来才是实际执行条数。
/// </summary>
public partial class TestMapAssets
{
    /// <summary>地表分层（设计 16 §3.2，0.32.0 的 `Conditional` 与「岩檐半透明」落地后的两件事）：
    ///
    /// ① **挡路只许画在 `Decor`**：Ground／Overlay／Conditional／Fog 的「挡路」列全是「否」。
    ///    （Ground 那次是「副本里整张地板都是碰撞盒」，人一步推不动，见决策 269。）
    /// ② **遮挡类瓦片要能被代码认出来**：`Overlay` 上的岩檐在 TileSet 里标 `covering=true`
    ///    （这份清单的唯一出处是 `tools/mapgen/MapKit.cs::TILES_COVERING`，运行期只读生成结果），
    ///    代码据此在玩家走到它下面时把整层压到 `OVERLAY_COVER_ALPHA`。</summary>
    private void _CheckMapLayers()
    {
        var coveringScenes = new List<string>();
        var coveringPaths = new List<string>();
        var conditionalScenes = new List<string>();
        foreach (string path in AllScenes)
        {
            var packed = GD.Load<PackedScene>(path);
            CheckNotNull(packed, $"场景能加载：{path}");
            if (packed == null)
            {
                continue;
            }

            var root = packed.Instantiate();
            TileMapLayer overlay = null;
            foreach (Node child in root.GetChildren())
            {
                if (child is not TileMapLayer layer)
                {
                    continue;
                }

                if (layer.Name == "Overlay")
                {
                    overlay = layer;
                }

                if (layer.Name == "Decor")
                {
                    continue;
                }

                // ① 除 Decor 外，一层都不许有带碰撞的瓦片
                int solid = 0;
                string firstSolid = "";
                foreach (Vector2I cell in layer.GetUsedCells())
                {
                    var data = layer.GetCellTileData(cell);
                    if (data == null || data.GetCollisionPolygonsCount(0) == 0)
                    {
                        continue;
                    }

                    solid++;
                    if (firstSolid.Length == 0)
                    {
                        firstSolid = cell.ToString();
                    }
                }

                CheckEq(
                    solid, 0,
                    $"{path.GetFile()} 的 {layer.Name} 层有 {solid} 格带碰撞（第一格 {firstSolid}）"
                        + "——挡路只许画在 Decor 层");
            }

            // ② 条件地表（`Conditional`）：地编交付后在这里数一下格数，没交付不算失败
            var conditional = root.GetNodeOrNull<TileMapLayer>("Conditional");
            if (conditional != null)
            {
                conditionalScenes.Add(
                    $"{path.GetFile()}({conditional.GetUsedCells().Count} 格)");
            }

            // ③ 遮挡类瓦片：图集认 `covering`，而且真的标在「盖住玩家」的那几格上
            CheckNotNull(overlay, $"{path.GetFile()} 有 Overlay 层");
            if (overlay != null)
            {
                CheckTrue(
                    LocalMapController.tile_set_has_covering(overlay.TileSet),
                    $"{path.GetFile()} 的 Overlay 图集带 `covering` 数据层（半透明遮挡靠它认格子）");
                var coveringCell = new Vector2I(-1, -1);
                var plainCell = new Vector2I(-1, -1);
                int blocking = 0;
                foreach (Vector2I cell in overlay.GetUsedCells())
                {
                    var data = overlay.GetCellTileData(cell);
                    bool covers = data != null && data.GetCustomData("covering").AsBool();
                    if (covers)
                    {
                        if (coveringCell.X < 0)
                        {
                            coveringCell = cell;
                        }

                        if (data.GetCollisionPolygonsCount(0) > 0)
                        {
                            blocking++;
                        }
                    }
                    else if (plainCell.X < 0)
                    {
                        plainCell = cell;
                    }
                }

                CheckEq(blocking, 0, $"{path.GetFile()}：遮挡瓦片不许挡路（玩家要能走进去）");
                if (coveringCell.X >= 0)
                {
                    coveringScenes.Add(path.GetFile());
                    coveringPaths.Add(path);
                }
            }

            root.Free();
        }

        // 判据本身（放在收集之后：遮挡瓦片全没了的话，下面这个循环一次都不跑，那条 `CheckGt`
        // 会当场红、[假绿] 门限也会点名——两层都指着"这批资产不见了"，而不是静默通过）
        CheckGt(
            coveringScenes.Count, 0.0,
            $"至少一张图有遮挡瓦片（0.32.0：石隙死路的岩檐压在 Overlay 上；现在：{Join(coveringScenes)}）");
        foreach (string path in coveringPaths)
        {
            var packed = GD.Load<PackedScene>(path);
            var root = packed.Instantiate();
            var overlay = root.GetNodeOrNull<TileMapLayer>("Overlay");
            var coveringCell = new Vector2I(-1, -1);
            var plainCell = new Vector2I(-1, -1);
            foreach (Vector2I cell in overlay.GetUsedCells())
            {
                var data = overlay.GetCellTileData(cell);
                if (data != null && data.GetCustomData("covering").AsBool())
                {
                    if (coveringCell.X < 0)
                    {
                        coveringCell = cell;
                    }
                }
                else if (plainCell.X < 0)
                {
                    plainCell = cell;
                }
            }

            // 站在遮挡格上 → 被盖住；站在普通 Overlay 格上 → 不算被盖住
            CheckTrue(
                LocalMapController.overlay_covering_at(
                    overlay, overlay.ToGlobal(overlay.MapToLocal(coveringCell))),
                $"{path.GetFile()}：站在遮挡格 {coveringCell} 上判为「被盖住」");
            CheckFalse(
                LocalMapController.overlay_covering_at(
                    overlay, overlay.ToGlobal(overlay.MapToLocal(plainCell))),
                $"{path.GetFile()}：站在普通地表格 {plainCell} 上不算被盖住");
            root.Free();
        }

        GD.Print(
            $"  [地图资产] 地表分层：有遮挡瓦片的图 {Join(coveringScenes)}；有 Conditional 层的图 "
                + (conditionalScenes.Count == 0
                    ? "（一个都没有，等地编交付石隙细径 22 格）"
                    : Join(conditionalScenes)));
    }

    /// <summary>装备图标的文件名必须是 `equip_base` 里的 id（15 §六：`assets/icons/equip/&lt;equip_id&gt;.png`）。
    /// 与武学那一条同源（那条抓到过 5 张旧编号）：**代码按 id 找图，名字不对就是"图静静地不显示"**。
    /// 区别是这条**允许一张都没有**，口径是"目录里有几张就查几张"，目录不存在也不算错。</summary>
    private void _CheckEquipIconFileNames(TableDb db)
    {
        var dir = DirAccess.Open("res://assets/icons/equip");
        if (dir == null)
        {
            return; // 还没这个目录（美术还没出图）——不算错
        }

        var unknown = new List<string>();
        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(".png"))
            {
                continue;
            }

            string equipId = StripPng(file);
            if (db.get_row("equip_base", equipId) == null)
            {
                unknown.Add(equipId);
            }
        }

        CheckEq(unknown.Count, 0, $"每张装备图标的名字都是 equip_base 里的 id（认不出的：{Join(unknown)}）");
    }

    /// <summary>指路牌（设计 0.31.2）：`map_region.signpost_cn` **非空**的行，大地图那张图上必须有
    /// `Markers/Sign_&lt;node_id&gt;` 位点——**文案在表里、牌子在地图上**，两半缺一就是"玩家看不到那句话"。</summary>
    private void _CheckSignposts(TableDb db)
    {
        string scene = FileAccess.GetFileAsString(Overworld);
        CheckFalse(scene.Length == 0, "读得到大地图场景");
        var planned = new List<string>();
        var missing = new List<string>();
        foreach (Variant entry in db.rows("map_region"))
        {
            var row = (MapRegionRow)entry.AsGodotObject();
            if (row.signpost_cn.StripEdges().Length == 0)
            {
                continue;
            }

            planned.Add(row.node_id);
            if (!scene.Contains($"Sign_{row.node_id}"))
            {
                missing.Add(row.node_id);
            }
        }

        CheckEq(
            missing.Count, 0,
            "这些地标在表里写了指路牌文案，但大地图上没有 `Markers/Sign_<node_id>` 位点（地编还没摆？）："
                + Join(missing));
        GD.Print(
            $"  [地图资产] 指路牌：表里写了文案 {planned.Count} 条"
                + (planned.Count == 0 ? "" : $"（{Join(planned)}）"));
    }

    /// <summary>美术交付的图标文件**名字必须是表里的 id**（15 §六：`icons/&lt;类&gt;/&lt;id&gt;.png`）。
    ///
    /// 为什么要有这条：图按别的名字交上来，代码这边"按 id 找图"就永远找不到——
    /// 结果不是报错，而是**那张图静静地不显示**（玩家只会觉得"这块没做"）。
    /// 2026-10-04 实测就抓到 5 张：`pf_xuanwei_06`／`pf_wudu_05`／`pf_chensha_04`／`pf_qingluo_01`／
    /// `pf_tiaoxi_01` —— 它们是 21 号 §九 那 5 部出身本命内功（★4）的图标，
    /// 而表里的 id 是 `pf_xuanwei_zhbai`（知白）／`pf_wudu_huandu`（还毒）／`pf_chensha_buhuan`（不还）／
    /// `pf_qingluo_ying`（影）／`pf_tiaoxi_huichun`（回春）。
    ///
    /// **教训**：`pf_&lt;school&gt;_NN` 只对「门派主线的第 N 部」成立，
    /// **本命／机遇那类额外条目一律带名字后缀**（`pf_xuanwei_zhbai`），不能再按序号推。</summary>
    private void _CheckIconFileNames(TableDb db)
    {
        int checkedCount = 0;
        var unknown = new List<string>();
        var dir = DirAccess.Open("res://assets/icons/skill");
        CheckNotNull(dir, "读得到武学图标目录");
        if (dir == null)
        {
            return;
        }

        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(".png"))
            {
                continue;
            }

            string skillId = StripPng(file);
            checkedCount++;
            if (db.get_row("skill_base", skillId) != null)
            {
                continue;
            }

            unknown.Add(skillId);
        }

        CheckGt(checkedCount, 0.0, $"武学图标目录里有图（{checkedCount} 张）");
        CheckEq(unknown.Count, 0, $"每张武学图标的名字都是 skill_base 里的 id（认不出的：{Join(unknown)}）");
        // 异常状态（15 §4.3 的「异常状态 4」）与增益（19）：**同一套门限，以前这两条缺着**。
        // 表侧来源是 `icon` 列（**空则退回行 id**，与 `IconPaths` 的优先级同一口径），不是行的主键：
        // 现例 `poison` 行的 icon 就是 `status_poison`。
        _CheckIconDirMatchesTable(db, "res://assets/icons/status", "status_effect", "异常状态");
        _CheckIconDirMatchesTable(db, "res://assets/icons/buff", "buff_def", "增益");
    }

    /// <summary>一类图标目录里的**文件名**必须都能在对应表里认出来（目录还没交图 = 跳过，不算错，
    /// 与装备那条同口径）。「认出来」= 命中任一行的 `icon` 列（非空时）或行的 id——代码就是这么
    /// 解析的（`IconPaths` 的「优先 icon，空则退回行 id」），门限跟着它走，别自创第二套口径。</summary>
    private void _CheckIconDirMatchesTable(TableDb db, string dirPath, string table, string label)
    {
        var dir = DirAccess.Open(dirPath);
        if (dir == null)
        {
            return;
        }

        var allowed = new HashSet<string>();
        foreach (Variant entry in db.rows(table))
        {
            var row = entry.AsGodotObject();
            string iconId = row.Get("icon").AsString().StripEdges();
            allowed.Add(iconId.Length > 0 ? iconId : row.Get("id").AsString());
        }

        int checkedCount = 0;
        var unknown = new List<string>();
        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(".png"))
            {
                continue;
            }

            string fileId = StripPng(file);
            checkedCount++;
            if (!allowed.Contains(fileId))
            {
                unknown.Add(fileId);
            }
        }

        CheckGt(checkedCount, 0.0, $"{label}图标目录里有图（{checkedCount} 张）");
        CheckEq(
            unknown.Count, 0,
            $"每张{label}图标的名字都要能在 {table} 里认出来（icon 列或行 id；认不出的：{Join(unknown)}）");
    }

    /// <summary>地标图标的贴图必须真的在（`map_region.icon` → `assets/sprites/icons/&lt;icon&gt;.png`）。
    ///
    /// 缺文件时大地图上那个地标**什么都不画**，玩家只会觉得「这里没图标」——不会觉得是数据错。
    /// 所以这条不能只写在 PS1 那道网里：headless 自检也要有（两道网各查一遍是这套的规矩）。
    ///
    /// 0.32.0 起还要反过来看一眼：**没配图标的行只许是兴趣点**（16 §3.5：洞口／屋舍群／水岸靠地形认）。</summary>
    private void _CheckRegionIcons(TableDb db)
    {
        var missing = new List<string>();
        var iconless = new List<string>();
        int checkedCount = 0;
        foreach (Variant entry in db.rows("map_region"))
        {
            var row = (MapRegionRow)entry.AsGodotObject();
            if (row.icon.Length == 0)
            {
                if (row.node_type != "poi")
                {
                    iconless.Add($"{row.node_id}({row.node_type})");
                }

                continue;
            }

            checkedCount++;
            if (!FileAccess.FileExists($"res://assets/sprites/icons/{row.icon}.png"))
            {
                missing.Add($"{row.node_id}→{row.icon}");
            }
        }

        CheckEq(missing.Count, 0, $"每个地标的图标贴图都在（缺：{Join(missing)}）");
        CheckEq(
            iconless.Count, 0,
            $"只有兴趣点可以不配图标，这些行没配（地标在图上会「什么都不画」）：{Join(iconless)}");
        CheckGt(checkedCount, 0.0, $"扫到地标图标（{checkedCount} 个：每行都查过文件在不在）");
    }

    /// <summary>主题 TileSet 的碰撞必须与 `MapKit.THEMES` 的 solid 清单**逐格一致**，而且地板永远不许挡路。
    ///
    /// 2026-10-04 玩家报的「副本里无法行走」就出在这条不变量上：`TILES_STONE` 一份清单同时当
    /// 「要收进图集的瓦片」和「挡路的瓦片」用，地板瓦片（1,9）被写成实心格——副本／洞穴的
    /// Ground 层整片都是碰撞盒，玩家横着走不动（纵向那点位移是被挤出来的假自由）。
    /// 地图验收与场景自检当时都是绿的：前者只看 Decor、后者全靠瞬移（见 `框架说明.md` 决策 269）。</summary>
    private void _CheckThemeTiles()
    {
        foreach (Variant themeVariant in MapKit.THEMES.Keys)
        {
            string theme = themeVariant.AsString();
            string path = MapKit.theme_tileset_path(theme);
            var tileSet = GD.Load<TileSet>(path);
            CheckTrue(tileSet != null, $"主题 {theme} 的 TileSet 能加载（{path}）");
            if (tileSet == null)
            {
                continue;
            }

            var entry = MapKit.THEMES[theme].AsGodotDictionary();
            var solid = entry["solid"].AsGodotArray();
            var source = tileSet.GetSource(MapKit.SOURCE_ID) as TileSetAtlasSource;
            CheckTrue(source != null, $"主题 {theme} 的图集源存在");
            if (source == null)
            {
                continue;
            }

            foreach (Variant tileVariant in entry["tiles"].AsGodotArray())
            {
                var tile = tileVariant.AsVector2I();
                if (!source.HasTile(tile))
                {
                    continue;
                }

                var data = source.GetTileData(tile, 0);
                bool hasCollision = data.GetCollisionPolygonsCount(0) > 0;
                CheckEq(
                    hasCollision, solid.Contains(tile),
                    $"主题 {theme} 瓦片 {tile}：碰撞盒与 solid 清单一致");
            }

            foreach (var floorTile in new[] { MapKit.T_FLOOR, MapKit.T_FLOOR_TOP })
            {
                CheckFalse(
                    solid.Contains(floorTile),
                    $"主题 {theme} 的地板 {floorTile} 不许挡路（solid 清单只收墙）");
            }
        }
    }
}

