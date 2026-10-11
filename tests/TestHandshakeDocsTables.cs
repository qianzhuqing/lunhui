using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;
using Lunhui.Data;

namespace Lunhui.Tests;

/// <summary>
/// 文档 ↔ 配置表逐格对账那一族的第二半（商店三件／副本文档／数据字典／代码里的字面量）。
/// 与 `TestHandshakeDocs.cs` 是同一个类的两份 `partial`——拆开只为不撞 800 行硬上限，
/// 注册进 `tools/RunTestsCs.cs` 的是主文件那一份。
/// </summary>
public partial class TestHandshakeDocs
{
    /// <summary>
    /// 32. 05 的「四家店」表要与 `building_def` 对齐（店名 + 附加服务 + 行数）。
    ///
    /// 由来（2026-10-03）：这张表此前**只覆盖了一半**——`CheckShopShelvesMatchDesign` 按建筑 id 硬编码了
    /// "这家店只准卖什么"，但**店的集合本身没人核**：设计加第五家店、改店名、或给某家店加一项服务，
    /// 数据不跟上也不会有任何提示。
    /// 判定：按 05 那张表的每一行取店名 → 到 `building_def` 里按 `name_cn` 找 → 核「附加服务」那一格
    /// （`—` 表示没有服务，其余词要求 `service_id` 非空）；最后核行数一致。
    ///
    /// 收口（2026-10-03）：`building_def` 里还有**不是店**的建筑（`bld_dummy` 木桩，
    /// `building_type=service`）——它既不进 05 那张店表，也不该算进"店的行数"。两边口径统一成
    /// 「`building_type=shop` 的建筑」。
    /// </summary>
    private void CheckShopTableMatchesBuildings()
    {
        var doc = FileAccess.GetFileAsString("res://docs/design/05_装备与掉落.md");
        CheckFalse(doc.Length == 0, "读得到 05_装备与掉落.md");
        var db = GetDb();
        var byName = new Dictionary<string, GodotObject>();
        var shopCount = 0;
        foreach (Variant rowVariant in db.rows("building_def"))
        {
            var row = rowVariant.AsGodotObject();
            if (row.Get("building_type").AsString() != "shop")
            {
                continue;
            }

            shopCount += 1;
            byName[row.Get("name_cn").AsString()] = row;
        }

        var rows = 0;
        // 只认「隐藏内容式」的 3 列行：| 建筑 | 卖什么 | 附加服务 |，避免把别的表也算进来
        var start = doc.IndexOf("| 建筑 | 卖什么 | 附加服务 |", StringComparison.Ordinal);
        if (start < 0)
        {
            Fail("05 里找不到「四家店」那张表（表头变了？）");
            return;
        }

        var body = doc.Substring(start);
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                break; // 表格结束
            }

            if (!line.StartsWith("|", StringComparison.Ordinal))
            {
                break;
            }

            var cells = line.Split('|');
            if (cells.Length < 5)
            {
                continue;
            }

            var buildingName = cells[1].Trim();
            if (buildingName == "建筑" || buildingName.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            var serviceText = cells[3].Trim();
            rows += 1;
            if (!byName.ContainsKey(buildingName))
            {
                Fail($"05 的商店表里有「{buildingName}」，但 building_def 里没有同名的店（building_type=shop）");
                continue;
            }

            var serviceId = byName[buildingName].Get("service_id").AsString();
            var docSaysService = serviceText.Length != 0 && serviceText != "—";
            var dataHasService = serviceId.Length != 0;
            CheckTrue(
                docSaysService == dataHasService,
                $"05 说「{buildingName}」的附加服务是「{serviceText}」（{(docSaysService ? "有服务" : "无服务")}），"
                + $"building_def 里 service_id='{serviceId}'（{(dataHasService ? "有服务" : "无服务")}）");
        }

        CheckEq(rows, shopCount, "05 的商店行数与 building_def 里 building_type=shop 的行数一致");
    }

    /// <summary>
    /// 31. 03_副本_黑风寨.md 的「隐藏内容」表与 `hidden_trigger` 对（类型词 ＋ 行数）。
    ///
    /// 03 是**黑风寨**那一章的副本文档；它正文里也写了「后山密道」（那条捷径在塌陷山洞）。
    /// 0.26.0 起隐藏内容多了一个**别的副本**（石隙迷窟的 `trig_shixi_reward`）——那不归 03 管，
    /// 所以下面比的是「03 点名的那几张图里的隐藏内容」，而不是 `hidden_trigger` 全表
    /// （全表一加副本就假红，见决策 284）。
    /// </summary>
    private void CheckHiddenContentTableMatchesData()
    {
        var doc = FileAccess.GetFileAsString("res://docs/design/03_副本_黑风寨.md");
        CheckFalse(doc.Length == 0, "读得到 03_副本_黑风寨.md");
        var db = GetDb();
        var byName = new Dictionary<string, GodotObject>();
        foreach (Variant rowVariant in db.rows("hidden_trigger"))
        {
            var row = rowVariant.AsGodotObject();
            byName[row.Get("name_cn").AsString()] = row;
        }

        var rows = 0;
        var scenesInDoc = new HashSet<string>();
        foreach (var raw in doc.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('|');
            if (cells.Length < 6)
            {
                continue;
            }

            var typeWord = cells[3].Trim();
            if (!HiddenTypeWords.ContainsKey(typeWord))
            {
                continue; // 不是「隐藏内容」那张表（类型列不在 7 个词里）
            }

            var name = cells[2].Trim();
            rows += 1;
            if (!byName.ContainsKey(name))
            {
                Fail($"03 隐藏内容表的「{name}」在 hidden_trigger 里找不到（name_cn 对不上）");
                continue;
            }

            var row = byName[name];
            scenesInDoc.Add(row.Get("scene_id").AsString());
            scenesInDoc.Add("scene_cave"); // 03 正文里的「后山密道」
            CheckEq(
                row.Get("trigger_type").AsString(),
                HiddenTypeWords[typeWord],
                $"03 说「{name}」的类型是{typeWord}，hidden_trigger 里 trigger_type={row.Get("trigger_type").AsString()}");
        }

        var expected = 0;
        foreach (Variant rowVariant in db.rows("hidden_trigger"))
        {
            if (scenesInDoc.Contains(rowVariant.AsGodotObject().Get("scene_id").AsString()))
            {
                expected += 1;
            }
        }

        CheckEq(
            rows,
            expected,
            $"03 的隐藏内容行数与「03 提到的那些图」里的 hidden_trigger 行数一致（{string.Join(", ", scenesInDoc)}）");
    }
    /// <summary>
    /// 28. 数据字典（06）必须**提到**每张表的每个列。06 的表清单行数与枚举行都有门限，**列**一直没有——
    /// 而它正是策划查"这个字段叫什么、什么意思"的地方（实测漏过 `character_base.weapon_type` 等功能性列，
    /// 见决策 172）。判定**从宽**：只要求列名以 `` `列名` `` 的形式出现在该表那一节里。
    /// 白名单只放**给人看的说明列**（不驱动逻辑、字典不逐列登记）。
    /// </summary>
    private void CheckDataDictionaryCoversColumns()
    {
        var doc = FileAccess.GetFileAsString("res://docs/design/06_配置表说明.md");
        CheckFalse(doc.Length == 0, "读得到 06_配置表说明.md");
        // 按 "### <表>.csv" 切成小节（只在本节内找列名，避免别处的同名词蒙混）
        var sections = new Dictionary<string, System.Text.StringBuilder>();
        var current = "";
        foreach (var raw in doc.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("### ", StringComparison.Ordinal) && line.EndsWith(".csv", StringComparison.Ordinal))
            {
                current = line.Substring(4).Trim();
                current = current.Substring(0, current.Length - 4);
                sections[current] = new System.Text.StringBuilder();
                continue;
            }

            if (current.Length == 0)
            {
                continue;
            }

            sections[current].Append('\n').Append(line);
        }

        var checkedCount = 0;
        foreach (var tableName in TableRegistry.tables())
        {
            if (!sections.ContainsKey(tableName))
            {
                continue; // 没写数据字典的表由「表清单」那条门限管
            }

            var body = sections[tableName].ToString();
            foreach (var column in CsvHeaderColumns(tableName))
            {
                if (Array.IndexOf(DocDictionaryProseColumns, column) >= 0)
                {
                    continue;
                }

                checkedCount += 1;
                CheckTrue(
                    body.Contains($"`{column}`", StringComparison.Ordinal),
                    $"06 的数据字典里没提 {tableName}.{column}（列名改了或新加了列，就要在字典里登记）");
            }
        }

        // 下限贴着现状（179 列）：主要防"小节没切开／表名对不上"导致这条门限**空转**。
        CheckGt(checkedCount, 150.0, $"字典覆盖检查扫到了足够多的列（{checkedCount} 列）");
    }

    private static List<string> CsvHeaderColumns(string tableName)
    {
        var outList = new List<string>();
        var path = $"{TableRegistry.tables_dir()}/{tableName}.csv";
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return outList;
        }

        var header = file.GetCsvLine();
        file.Close();
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim();
            if (i == 0 && name.StartsWith("\uFEFF", StringComparison.Ordinal))
            {
                name = name.Substring(1); // 第一格可能带 BOM（构建期也剥它）
            }

            if (name.Length != 0)
            {
                outList.Add(name);
            }
        }

        return outList;
    }

    /// <summary>
    /// 27. 代码里点了名的**战斗常数**，表里必须都有。
    ///
    /// 与构建期那条成长常数校验是一对：成长那边代码里有集中清单，所以放在校验器里；战斗这边**没有**
    /// 集中清单——名字散在 `constant("<id>", 默认值)` 的调用点上，所以只能在测试里**扫源码**把实参抓出来。
    /// 放测试里还有一个理由：导出后的包里不一定有源码，运行期不该依赖读源码。
    ///
    /// 由来（2026-10-03）：`combat_const` 缺行同样只会静默用兜底值（`DamageResolver.constant()`），
    /// 而 PS1 那份 5 个常数的清单是手抄的——代码新加一个常数、清单忘了加，缺行就又没人管。
    /// 两处调用形状：伤害管线写 `constant("x", …)`，战斗模拟器写 `_combat_const("x", …)`。
    /// </summary>
    private void CheckCodeConstantsExistInTables()
    {
        var sources = new (string Path, string Pattern)[]
        {
            ("res://src/core/DamageResolver.cs", "constant\\(\"([a-z_0-9]+)\""),
            ("res://src/core/BattleSimulatorActions.cs", "_combat_const\\(\"([a-z_0-9]+)\""),
            ("res://src/core/BattleSimulatorSettle.cs", "_combat_const\\(\"([a-z_0-9]+)\""),
        };
        var lookup = new HashSet<string>();
        foreach (Variant rowVariant in GetDb().rows("combat_const"))
        {
            lookup.Add(rowVariant.AsGodotObject().Get("const_id").AsString());
        }

        var scanned = 0;
        foreach (var (path, pattern) in sources)
        {
            var source = FileAccess.GetFileAsString(path);
            CheckFalse(source.Length == 0, $"读得到 {path}（要扫它的常数调用点）");
            foreach (Match hit in new Regex(pattern).Matches(source))
            {
                scanned += 1;
                var key = hit.Groups[1].Value;
                CheckTrue(lookup.Contains(key), $"combat_const 缺少代码依赖的常数 '{key}'（缺行会静默用兜底值）");
            }
        }

        CheckGt(scanned, 3.0, $"扫到足够多的战斗常数调用点（{scanned} 处）");
    }

    /// <summary>
    /// 14. 代码里**按字面量 id 查表**的地方（`get_row("表", "某行")`），那行必须真的在表里。
    ///
    /// 由来：`LOCKED_SCENES`（决策 88）、`enemy_base.ai_template`（决策 203）、
    /// `difficulty_config.unlock_condition` 都是「代码写死的 id 清单必须对得上表」的同类问题，
    /// 但一处一处补永远补不完。这条做成**通用门限**：扫 `src/` 里的 `get_row("表", "字面量")` 与
    /// `require_row(...)`，逐条断言那一行存在——改表时改掉了代码依赖的那一行，会在自检里当场点名
    /// （以前是运行时静默走兜底值，比如 `sneak_detect_reduce()` 缺行就默默用 0.4）。
    /// 动态拼的键（`"%s|%s" % [...]`）跳过：它们由 `RELATIONS` 那套引用校验管。
    /// 两种语言一起扫（`.gd`／`.cs`）；下面那层「扫到足够多」的下限是"这条正则还抓得住东西"的哨兵，
    /// 不是覆盖率指标。
    /// </summary>
    private void CheckCodeRowLiteralsExist()
    {
        var db = GetDb();
        var regex = new Regex("(?:get_row|require_row)\\(\\s*\"([a-z_0-9]+)\"\\s*,\\s*\"([^\"]+)\"\\s*\\)");
        var found = 0;
        foreach (var path in CollectFiles(new[] { "src" }))
        {
            if (!path.EndsWith(".gd", StringComparison.Ordinal) && !path.EndsWith(".cs", StringComparison.Ordinal))
            {
                continue;
            }

            var source = FileAccess.GetFileAsString($"res://{path}");
            foreach (Match hit in regex.Matches(source))
            {
                var tableName = hit.Groups[1].Value;
                var rowId = hit.Groups[2].Value;
                if (rowId.Contains("%", StringComparison.Ordinal))
                {
                    continue; // 动态拼接的主键（"%s|%s"），交给 RELATIONS 校验
                }

                found += 1;
                CheckTrue(
                    db.get_row(tableName, rowId) != null,
                    $"{path} 里写死的 {tableName}[{rowId}] 在表里不存在（改表时改掉了代码依赖的那一行）");
            }
        }

        CheckGt(found, 10.0, $"扫到足够多的「字面量查表」调用点（{found} 处）");
    }
    /// <summary>
    /// 18. 05 的「商店」表：**每类店该卖什么**与货架上的实物要对得上（硬门限，今天全一致）。
    ///
    /// 05_装备与掉落.md 的表：杂货铺＝材料、白板饰品／酒楼＝**非战斗**回血道具／
    /// 医馆＝**战斗中**回血道具（附加服务：花钱治疗）／铁匠铺＝白板装备。
    /// 两条回血道具不能混（05 专门写了「这是酒楼和医馆的职能分界，不能混」），所以这条门限卡的是
    /// `use_context` 与货架的对应——以前只测「买了能用」，没人管「这家店卖的东西对不对」。
    /// </summary>
    private void CheckShopShelvesMatchDesign()
    {
        var db = GetDb();
        var stockByGroup = new Dictionary<string, List<GodotObject>>();
        foreach (Variant rowVariant in db.rows("shop_stock"))
        {
            var row = rowVariant.AsGodotObject();
            var shopId = row.Get("shop_id").AsString();
            if (!stockByGroup.ContainsKey(shopId))
            {
                stockByGroup[shopId] = new List<GodotObject>();
            }

            stockByGroup[shopId].Add(row);
        }

        var checkedCount = 0;
        foreach (Variant buildingVariant in db.rows("building_def"))
        {
            var building = buildingVariant.AsGodotObject();
            var group = building.Get("stock_group").AsString();
            if (group.Length == 0)
            {
                continue;
            }

            var buildingId = building.Get("building_id").AsString();
            CheckTrue(stockByGroup.ContainsKey(group), $"{buildingId} 的货架 {group} 在 shop_stock 里有货");
            if (!stockByGroup.ContainsKey(group))
            {
                continue;
            }

            foreach (var row in stockByGroup[group])
            {
                var itemId = row.Get("item_id").AsString();
                var equip = db.get_row("equip_base", itemId);
                var item = db.get_row("item_base", itemId);
                checkedCount += 1;
                switch (buildingId)
                {
                    case "bld_grocery":
                    {
                        var ok = false;
                        if (item != null && item.Get("item_type").AsString() == "material")
                        {
                            ok = true;
                        }

                        if (equip != null && equip.Get("rarity").AsString() == "common"
                            && (equip.Get("slot").AsString() == "ring" || equip.Get("slot").AsString() == "necklace"))
                        {
                            ok = true;
                        }

                        // 0.23.0 把毒酒放进杂货铺（隐藏线「毒杀毒手」的钥匙，300 文限量 1 件）——
                        // 设计 05 的「材料＋白板饰品」是常规商品口径，钥匙道具是**故意**摆在这里的。
                        if (item != null && item.Get("item_type").AsString() == "key")
                        {
                            ok = true;
                        }

                        // 2026-10-04（Q3）：`shop` 那 5 部武学改走秘籍道具，四家店各上架一本——
                        // 秘籍是这几家店**新增的例外品类**，07 §4.1 那张建筑表已写明。
                        if (item != null && item.Get("item_type").AsString() == "skillbook")
                        {
                            ok = true;
                        }

                        CheckTrue(ok, $"杂货铺只卖材料与白板饰品（{itemId} 不符）");
                        break;
                    }

                    case "bld_tavern":
                        CheckTrue(
                            (item != null && item.Get("item_type").AsString() == "consumable"
                                && item.Get("use_context").AsString() == "field")
                            || (item != null && item.Get("item_type").AsString() == "skillbook"),
                            $"酒楼只卖非战斗回血道具（{itemId} 不符）");
                        break;

                    case "bld_clinic":
                        CheckTrue(
                            item != null && item.Get("item_type").AsString() == "consumable"
                                && item.Get("use_context").AsString() == "battle",
                            $"医馆只卖战斗中回血道具（{itemId} 不符）");
                        break;

                    case "bld_smith":
                        CheckTrue(
                            (equip != null && equip.Get("rarity").AsString() == "common")
                            || (item != null && item.Get("item_type").AsString() == "skillbook"),
                            $"铁匠铺只卖白板装备（{itemId} 不符）");
                        break;
                }
            }

            // 附加服务：05 那张表里只有医馆带「花钱治疗」
            if (buildingId == "bld_clinic")
            {
                CheckEq(building.Get("service_id").AsString(), "heal", "医馆带治疗服务");
            }
            else
            {
                CheckEq(building.Get("service_id").AsString(), "", $"{buildingId} 没有附加服务（05 的表里只有医馆有）");
            }
        }

        CheckGt(checkedCount, 10.0, $"真正比过的货架条目（{checkedCount} 条）");
    }
}