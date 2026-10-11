using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;
using Lunhui.Data;

namespace Lunhui.Tests;

/// <summary>
/// 文档 ↔ 配置表逐格对账（含商店三件）——"策划对接守卫"的一族
/// （拆自 tests/test_handshake.gd，2026-10-04；2026-10-11 搬 C#）。
/// 只换语言、不改行为：常量与共用夹具在 `TestHandshake`（base）；
/// 第二半（商店／副本／数据字典／字面量查表）在 `TestHandshakeDocsTables.cs`
/// （同一个类的 `partial`，只为了不撞 800 行硬上限）。
/// </summary>
public partial class TestHandshakeDocs : TestHandshake
{
    public override string SuiteName() => "文档 ↔ 配置表逐格对账（含商店三件）";

    public override void Run()
    {
        CheckDocEnumsMatchCode();
        CheckDocTablesMatchData();
        CheckShopShelvesMatchDesign();
        CheckCodeConstantsExistInTables();
        CheckCodeRowLiteralsExist();
        CheckDataDictionaryCoversColumns();
        CheckHiddenContentTableMatchesData();
        CheckShopTableMatchesBuildings();
        CheckShopTabsHaveButtons();
    }

    /// <summary>
    /// 33. 商店的页签列表与场景里的按钮必须一一对应。
    ///
    /// 由来（2026-10-03）：商店面板的页签是**代码里的** `TABS`，每个页签对应场景里一个 `Tab<后缀>`
    /// 按钮——**取不到就静默跳过**，于是"TABS 里加了第五个页签、场景却没加按钮"的结果是：
    /// **玩家点不到那个页签，而自检全绿**（自检走的是 `select_tab(key)` 直调，不需要按钮存在）。
    /// 反过来，场景里多一个 `Tab*` 按钮也没人管（点下去什么都不做）。
    ///
    /// 面板由 `.gd` 搬 C# 之后 `TABS` 改成每行 `{ "key", "场景按钮后缀", "显示名" }` 的三元组，
    /// 本条跟着读 `ShopScreen.cs`、在同一次解析里把 key 与后缀一起取出来。
    /// </summary>
    private void CheckShopTabsHaveButtons()
    {
        var source = FileAccess.GetFileAsString("res://src/ui/ShopScreen.cs");
        var scene = FileAccess.GetFileAsString("res://scenes/shop_screen.tscn");
        CheckFalse(source.Length == 0, "读得到 ShopScreen.cs");
        CheckFalse(scene.Length == 0, "读得到 shop_screen.tscn");

        // ① TABS 的 (key, 场景按钮后缀)：C# 里每行一对 `{ "key", "Suffix", "名称" }`
        var tabsStart = source.IndexOf("TABS =", StringComparison.Ordinal);
        CheckGt(tabsStart, 0.0, "ShopScreen.cs 里有 TABS 表");
        if (tabsStart < 0)
        {
            return;
        }

        var tabsEnd = source.IndexOf("};", tabsStart, StringComparison.Ordinal);
        var tabsBody = source.Substring(tabsStart, tabsEnd - tabsStart);
        var tabRegex = new Regex("\\{\\s*\"([a-z_]+)\"\\s*,\\s*\"([A-Za-z]+)\"");
        var keys = new List<string>();
        var suffixes = new Dictionary<string, string>();
        foreach (Match hit in tabRegex.Matches(tabsBody))
        {
            keys.Add(hit.Groups[1].Value);
            suffixes[hit.Groups[1].Value] = hit.Groups[2].Value;
        }

        CheckGt(keys.Count, 3.0, $"TABS 解析出足够多的页签（{keys.Count} 个）");

        // ② 场景里的 Tab* 按钮
        var buttonRegex = new Regex("\\[node name=\"(Tab[A-Za-z]*)\" type=\"Button\"");
        var sceneButtons = new HashSet<string>();
        foreach (Match hit in buttonRegex.Matches(scene))
        {
            sceneButtons.Add(hit.Groups[1].Value);
        }

        foreach (var key in keys)
        {
            var suffix = suffixes.GetValueOrDefault(key, "");
            CheckFalse(suffix.Length == 0, $"TABS 的页签 {key} 没有场景按钮后缀（按钮名拼不出来）");
            if (suffix.Length == 0)
            {
                continue;
            }

            CheckTrue(
                sceneButtons.Contains("Tab" + suffix),
                $"TABS 里的页签 {key} 需要场景里有 Tab{suffix} 按钮（没有按钮＝玩家点不到，而自检直调 select_tab 照样绿）");
            sceneButtons.Remove("Tab" + suffix);
        }

        foreach (var left in sceneButtons)
        {
            CheckTrue(false, $"场景里的 {left} 按钮在 TABS 里没有对应页签（点了什么都不做）");
        }
    }
    /// <summary>
    /// 15. 06_配置表说明.md 里写的枚举必须与代码/数据对得上（诞生当天就抓到
    /// `event_check.reward_type` 文档写 7 种、代码只剩 4 种）。
    /// 比三处：代码 `TableValidator.ENUMS`、单主键表的主键列实际取值（复合主键的每一半是 join 键，
    /// 文档写的是语义不是取值集合，只比单主键）。已知「文档过期」的几处写在 `DocEnumKnownStale` 里
    /// 并写明理由——**白名单自己也会烂**：文档里那行不在了就把白名单一起删。
    /// </summary>
    private void CheckDocEnumsMatchCode()
    {
        var doc = FileAccess.GetFileAsString("res://docs/design/06_配置表说明.md");
        CheckFalse(doc.Length == 0, "读得到 06_配置表说明.md");
        var db = GetDb();
        var enums = TableValidator.enums();
        var primaryKeys = TableRegistry.primary_keys();
        var rowRegex = new Regex("^\\|\\s*`([a-z_0-9]+)`\\s*\\|[^|]*\\|(.+)\\|\\s*$");
        var proseRegex = new Regex("`([a-z_0-9]+)`\\s*取值：(.+)$");
        var valueRegex = new Regex("`([A-Za-z_0-9:]+)`");
        var table = "";
        var docEnums = new Dictionary<string, List<string>>();
        foreach (var raw in doc.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("### ", StringComparison.Ordinal) && line.EndsWith(".csv", StringComparison.Ordinal))
            {
                table = line.Substring(4).Trim();
                table = table.Substring(0, table.Length - 4);
                continue;
            }

            if (table.Length == 0)
            {
                continue;
            }

            string keyColumn;
            string body;
            var rowHit = rowRegex.Match(line);
            if (rowHit.Success)
            {
                keyColumn = rowHit.Groups[1].Value;
                body = rowHit.Groups[2].Value;
            }
            else
            {
                var proseHit = proseRegex.Match(line);
                if (!proseHit.Success)
                {
                    continue;
                }

                keyColumn = proseHit.Groups[1].Value;
                body = proseHit.Groups[2].Value;
            }

            var values = new List<string>();
            var looksLikePrefix = false;
            foreach (Match found in valueRegex.Matches(body))
            {
                var value = found.Groups[1].Value;
                // `sk_`／`pf_`／`attr:` 这类是**前缀**，不是枚举值（文档里用来说明命名或引用形式）
                if (value.EndsWith("_", StringComparison.Ordinal) || value.EndsWith(":", StringComparison.Ordinal))
                {
                    looksLikePrefix = true;
                }

                if (value.Length != 0 && !values.Contains(value))
                {
                    values.Add(value);
                }
            }

            if (values.Count < 2 || looksLikePrefix)
            {
                continue; // 单值多是「引用某张表／可以留空」这类说明，不是枚举
            }

            docEnums[$"{table}.{keyColumn}"] = values;
        }

        CheckGt(docEnums.Count, 5.0, $"从 06 里解析出足够多的枚举（{docEnums.Count} 处）");

        var checkedCount = 0;
        foreach (var key in docEnums.Keys)
        {
            if (DocEnumKnownStale.ContainsKey(key))
            {
                continue;
            }

            var parts = key.Split('.');
            var tableName = parts[0];
            var column = parts[1];
            var docValues = docEnums[key];
            if (enums.ContainsKey(tableName) && enums[tableName].AsGodotDictionary().ContainsKey(column))
            {
                checkedCount += 1;
                CheckSameEnum(key, docValues, ToStringList(enums[tableName].AsGodotDictionary()[column]), "代码 ENUMS");
                continue;
            }

            // 只比**单主键**表的主键列
            var keyColumns = primaryKeys.ContainsKey(tableName)
                ? primaryKeys[tableName].AsGodotArray()
                : new Godot.Collections.Array();
            if (keyColumns.Count == 1 && keyColumns[0].AsString() == column)
            {
                var actual = new List<string>();
                foreach (Variant row in db.rows(tableName))
                {
                    actual.Add(row.AsGodotObject().Get(column).AsString());
                }

                checkedCount += 1;
                CheckSameEnum(key, docValues, actual, "表里的实际取值");
            }
        }

        CheckGt(checkedCount, 3.0, $"真正比对过的枚举列（{checkedCount} 处）");
        // 白名单自己也会烂：文档里那行不在了就把白名单一起删
        foreach (var key in DocEnumKnownStale.Keys)
        {
            CheckTrue(docEnums.ContainsKey(key), $"06 枚举白名单里的 {key} 已经不在文档里了，把这条删掉");
        }
    }

    private static List<string> ToStringList(Variant value)
    {
        var outList = new List<string>();
        if (value.VariantType == Variant.Type.Array)
        {
            foreach (Variant item in value.AsGodotArray())
            {
                outList.Add(item.AsString());
            }
        }
        else if (value.VariantType == Variant.Type.String)
        {
            outList.Add(value.AsString());
        }

        return outList;
    }

    /// <summary>两个取值集合必须一样（空串先滤掉：`ENUMS` 用 "" 表示「允许留空」，文档不会写出来）。</summary>
    private void CheckSameEnum(string key, List<string> docValues, List<string> actual, string side)
    {
        var actualSet = new HashSet<string>(actual);
        var docSet = new HashSet<string>(docValues);
        var docOnly = new List<string>();
        foreach (var value in docValues)
        {
            if (value.Length != 0 && !actualSet.Contains(value))
            {
                docOnly.Add(value);
            }
        }

        var actualOnly = new List<string>();
        foreach (var value in actual)
        {
            if (value.Length != 0 && !docSet.Contains(value))
            {
                actualOnly.Add(value);
            }
        }

        CheckTrue(
            docOnly.Count == 0 && actualOnly.Count == 0,
            $"06 的 {key} 与{side} 对不上：文档多 [{string.Join(", ", docOnly)}]，那边多 [{string.Join(", ", actualOnly)}]");
    }
    /// <summary>
    /// 17. 设计文档里「写死的表」与配置表逐格对（**硬门限**：今天全一致）。
    ///
    /// 覆盖：01 的**星级表**（熟练度成长／修习门槛／打坐费用）＋**七维属性表**＋**非战斗技能表**、
    /// 05 的**槽位表**（7 类）、**稀有度表**（词条数量区间）、**武器类型表**（4 类与中文名）。
    /// 04 的伤害类型表**故意不在这里**：它今天就有 1 格不一致（`dot_internal.use_def_qi`，缺口 #21），
    /// 改哪边要设计拍板，所以那条走 `validate_tables.ps1` 的**警告**；拍完板再挪进来当硬门限。
    /// 为什么值得一条门限：文档是策划改需求的第一落点，改了文档忘了改 CSV（或反过来）今天只有人眼能发现。
    /// </summary>
    private void CheckDocTablesMatchData()
    {
        var db = GetDb();
        var checkedCount = 0;
        var masteryMax = 10; // growth_const.mastery_max；01 的「熟练度成长 ×1.50」是指**练满**的倍率

        // ① 01 星级表：| ★N | 定位 | ×1.50 | 属性 8（或「无」） | 50 |
        var doc01 = FileAccess.GetFileAsString("res://docs/design/01_角色系统.md");
        var starRegex = new Regex("(?m)^\\|\\s*★(\\d)\\s*\\|[^|]*\\|\\s*×([0-9.]+)\\s*\\|\\s*([^|]*)\\|\\s*([0-9]+)\\s*\\|");
        var starRows = new Dictionary<int, (double Multiplier, int Req, int Cost)>();
        foreach (Match found in starRegex.Matches(doc01))
        {
            var reqText = found.Groups[3].Value.Trim();
            var req = 0;
            if (!reqText.Contains("无", StringComparison.Ordinal))
            {
                req = int.Parse(reqText.Replace("属性", "").Trim());
            }

            starRows[int.Parse(found.Groups[1].Value)] = (
                double.Parse(found.Groups[2].Value),
                req,
                int.Parse(found.Groups[4].Value));
        }

        CheckEq(starRows.Count, 5, "从 01 解析出 5 行星级表");
        foreach (Variant rowVariant in db.rows("skill_star_def"))
        {
            var row = rowVariant.AsGodotObject();
            var star = (int)row.Get("star").AsInt64();
            if (!starRows.ContainsKey(star))
            {
                continue;
            }

            checkedCount += 1;
            var docRow = starRows[star];
            CheckFloat(
                1.0 + row.Get("mastery_gain").AsDouble() * masteryMax,
                docRow.Multiplier,
                $"★{star} 练满的熟练度倍率与 01 一致",
                0.0001);
            CheckEq(row.Get("learn_req_value").AsInt64(), docRow.Req, $"★{star} 的修习门槛与 01 一致");
            CheckEq(row.Get("cultivate_cost_base").AsInt64(), docRow.Cost, $"★{star} 的打坐基础费用与 01 一致");
        }

        // ② 05 槽位表：| `slot` | 名 | 数量 | 说明 |
        var doc05 = FileAccess.GetFileAsString("res://docs/design/05_装备与掉落.md");
        var slotRegex = new Regex("(?m)^\\|\\s*`([a-z_]+)`\\s*\\|\\s*([^|]+)\\|\\s*(\\d+)\\s*\\|");
        var slotRows = new Dictionary<string, (string Name, int Max)>();
        foreach (Match found in slotRegex.Matches(doc05))
        {
            slotRows[found.Groups[1].Value] = (found.Groups[2].Value.Trim(), int.Parse(found.Groups[3].Value));
        }

        CheckEq(slotRows.Count, 7, "从 05 解析出 7 类槽位");
        foreach (Variant rowVariant in db.rows("equip_slot_def"))
        {
            var row = rowVariant.AsGodotObject();
            var slotId = row.Get("slot_id").AsString();
            CheckTrue(slotRows.ContainsKey(slotId), $"05 的槽位表里有 {slotId}");
            if (!slotRows.ContainsKey(slotId))
            {
                continue;
            }

            checkedCount += 1;
            var docSlot = slotRows[slotId];
            CheckEq(row.Get("name_cn").AsString(), docSlot.Name, $"{slotId} 的中文名与 05 一致");
            CheckEq(row.Get("max_equip").AsInt64(), docSlot.Max, $"{slotId} 的可佩戴数量与 05 一致");
        }

        // ③ 05 稀有度表：| common | 凡品 | 灰 | 0–1 |（词条数量）
        var rarityRegex = new Regex("(?m)^\\|\\s*(common|fine|rare|epic|legend)\\s*\\|[^|]*\\|[^|]*\\|\\s*([0-9]+)(?:–([0-9]+))?\\s*\\|");
        var rarityRows = new Dictionary<string, (int Min, int Max)>();
        foreach (Match found in rarityRegex.Matches(doc05))
        {
            var low = int.Parse(found.Groups[2].Value);
            var highText = found.Groups[3].Value;
            rarityRows[found.Groups[1].Value] = (low, highText.Length != 0 ? int.Parse(highText) : low);
        }

        CheckEq(rarityRows.Count, 5, "从 05 解析出 5 档稀有度");
        foreach (Variant rowVariant in db.rows("rarity_def"))
        {
            var row = rowVariant.AsGodotObject();
            var rarityId = row.Get("rarity_id").AsString();
            if (!rarityRows.ContainsKey(rarityId))
            {
                continue;
            }

            checkedCount += 1;
            var docRarity = rarityRows[rarityId];
            CheckEq(row.Get("affix_min").AsInt64(), docRarity.Min, $"{rarityId} 的词条数下限与 05 一致");
            CheckEq(row.Get("affix_max").AsInt64(), docRarity.Max, $"{rarityId} 的词条数上限与 05 一致");
        }

        // ④ 05 武器类型表：| `sword` | 剑 | 敏・智 | 招式均衡 |
        var weaponRegex = new Regex("(?m)^\\|\\s*`(sword|fist|blade|spear)`\\s*\\|\\s*([^|]+)\\|");
        var weaponRows = new Dictionary<string, string>();
        foreach (Match found in weaponRegex.Matches(doc05))
        {
            weaponRows[found.Groups[1].Value] = found.Groups[2].Value.Trim();
        }

        CheckEq(weaponRows.Count, 4, "从 05 解析出 4 种武器类型");
        foreach (Variant rowVariant in db.rows("weapon_type_def"))
        {
            var row = rowVariant.AsGodotObject();
            var weaponId = row.Get("weapon_type").AsString();
            CheckTrue(weaponRows.ContainsKey(weaponId), $"05 的武器表里有 {weaponId}");
            if (!weaponRows.ContainsKey(weaponId))
            {
                continue;
            }

            checkedCount += 1;
            CheckEq(row.Get("name_cn").AsString(), weaponRows[weaponId], $"{weaponId} 的中文名与 05 一致");
        }

        // ⑤ 01 的七维属性表：| 力 str | 主职能 | 次要 |
        var attrRegex = new Regex("(?m)^\\|\\s*([^|\\s]+)\\s+([a-z]{2,4})\\s*\\|[^|]*\\|");
        var attrRows = new Dictionary<string, string>();
        foreach (Match found in attrRegex.Matches(doc01))
        {
            attrRows[found.Groups[2].Value] = found.Groups[1].Value;
        }

        CheckEq(attrRows.Count, 7, "从 01 解析出 7 维属性");
        foreach (Variant rowVariant in db.rows("attribute_def"))
        {
            var row = rowVariant.AsGodotObject();
            var attrId = row.Get("attr_id").AsString();
            CheckTrue(attrRows.ContainsKey(attrId), $"01 的属性表里有 {attrId}");
            if (!attrRows.ContainsKey(attrId))
            {
                continue;
            }

            checkedCount += 1;
            CheckEq(row.Get("name_cn").AsString(), attrRows[attrId], $"{attrId} 的中文名与 01 一致");
        }

        // ⑥ 01 的非战斗技能表：| `yishu` | 医术 | 智 | 说明 |
        var skillRegex = new Regex("(?m)^\\|\\s*`(qimen|yishu|dusu|wenxue|shengcun)`\\s*\\|\\s*([^|]+)\\|\\s*([^|]+)\\|");
        var attrNameToId = new Dictionary<string, string>
        {
            { "力", "str" }, { "体", "con" }, { "敏", "agi" }, { "智", "int" },
            { "运", "luk" }, { "悟性", "wu" }, { "根骨", "gen" },
        };
        var skillRows = new Dictionary<string, (string Name, string Attr)>();
        foreach (Match found in skillRegex.Matches(doc01))
        {
            var attrWord = found.Groups[3].Value.Trim();
            skillRows[found.Groups[1].Value] = (
                found.Groups[2].Value.Trim(),
                attrNameToId.GetValueOrDefault(attrWord, ""));
        }

        CheckEq(skillRows.Count, 5, "从 01 解析出 5 项非战斗技能");
        foreach (Variant rowVariant in db.rows("event_skill_def"))
        {
            var row = rowVariant.AsGodotObject();
            var skillId = row.Get("skill_id").AsString();
            CheckTrue(skillRows.ContainsKey(skillId), $"01 的非战斗技能表里有 {skillId}");
            if (!skillRows.ContainsKey(skillId))
            {
                continue;
            }

            checkedCount += 1;
            var docSkill = skillRows[skillId];
            CheckEq(row.Get("name_cn").AsString(), docSkill.Name, $"{skillId} 的中文名与 01 一致");
            CheckEq(row.Get("related_attr").AsString(), docSkill.Attr, $"{skillId} 的关联属性与 01 一致");
        }

        CheckGt(checkedCount, 15.0, $"真正比过的「文档表 vs 数据」行（{checkedCount} 处）");
        CheckDocSpawnTable(db);
    }
    /// <summary>
    /// 17b. 07 的**逐位点明雷表**（`spawn_id`／队伍／行为／警戒格数／刷新秒数／精英标记）与
    /// `roaming_spawn` 对。
    ///
    /// 为什么它值得硬门限：这张表是**地编的施工图**，而且它比 02 的分布表更细——位点 id、警戒半径、
    /// 刷新秒数、精英标记一个不漏，还列了 02 漏掉的野猪群。位点集合也严格比（多一个少一个都红）。
    /// `spawn_id` 那列支持简写（`sp_lp_wolf_01/02/03`），按前缀展开。
    /// </summary>
    private void CheckDocSpawnTable(TableDb db)
    {
        var doc = FileAccess.GetFileAsString("res://docs/design/07_地图资源需求.md");
        var rowRegex = new Regex(
            "(?m)^\\|\\s*([^|]+?)\\s*\\|\\s*`([^`]+)`\\s*\\|\\s*([^|]+?)\\s*\\|\\s*([^|]+?)\\s*\\|"
            + "\\s*([^|]+?)\\s*\\|\\s*([^|]+?)\\s*\\|\\s*([^|]+?)\\s*\\|");
        var tailRegex = new Regex("(\\d+)$");
        var behaviorWord = new Dictionary<string, string>
        {
            { "游荡", "wander" }, { "巡逻", "patrol" }, { "驻守", "idle" }, { "追击", "chase" }, { "沉睡", "sleep" },
        };
        var teamNameToId = new Dictionary<string, string>();
        foreach (Variant rowVariant in db.rows("enemy_team"))
        {
            var row = rowVariant.AsGodotObject();
            teamNameToId[row.Get("name_cn").AsString()] = row.Get("team_id").AsString();
        }

        var regionNameToId = new Dictionary<string, string>();
        foreach (Variant rowVariant in db.rows("map_region"))
        {
            var row = rowVariant.AsGodotObject();
            regionNameToId[row.Get("name_cn").AsString()] = row.Get("node_id").AsString();
        }

        regionNameToId["黑风寨外围"] = "n_heifengzhai"; // 07 与 02 都用这个口语叫法
        var docSpawns = new Dictionary<string, (string Region, string Team, string Behavior, double Alert, int Respawn, int Elite)>();
        var rowsChecked = 0;
        foreach (Match found in rowRegex.Matches(doc))
        {
            var spawnCell = found.Groups[2].Value;
            if (!spawnCell.StartsWith("sp_", StringComparison.Ordinal))
            {
                continue;
            }

            var regionName = found.Groups[1].Value.Trim();
            var teamName = found.Groups[3].Value.Trim();
            var behaviorText = found.Groups[4].Value.Trim();
            var alertText = found.Groups[5].Value;
            var respawnText = found.Groups[6].Value;
            var eliteText = found.Groups[7].Value;
            if (!behaviorWord.ContainsKey(behaviorText))
            {
                continue; // 不是明雷表那几行（07 里还有别的表格也带 `sp_` 引用）
            }

            rowsChecked += 1;
            var ids = new List<string>();
            var parts = spawnCell.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var baseId = parts[0].Trim();
            var prefix = baseId;
            var tail = tailRegex.Match(baseId);
            if (tail.Success)
            {
                prefix = baseId.Substring(0, baseId.Length - tail.Groups[1].Value.Length);
            }

            foreach (var part in parts)
            {
                var piece = part.Trim();
                ids.Add(piece.Length > 2 ? piece : prefix + piece);
            }

            foreach (var spawnId in ids)
            {
                docSpawns[spawnId] = (
                    regionNameToId.GetValueOrDefault(regionName, ""),
                    teamNameToId.GetValueOrDefault(teamName, ""),
                    behaviorWord[behaviorText],
                    double.Parse(alertText.Replace("格", "").Trim()),
                    respawnText.Contains("不刷新", StringComparison.Ordinal)
                        ? 0
                        : int.Parse(respawnText.Replace("s", "").Trim()),
                    eliteText.Contains("✔", StringComparison.Ordinal) ? 1 : 0);
            }
        }

        CheckEq(rowsChecked, 13, "从 07 解析出 13 行逐位点明雷表");
        CheckEq(docSpawns.Count, 17, "07 那 13 行展开成 17 个位点");
        var actualIds = new List<string>();
        foreach (Variant rowVariant in db.rows("roaming_spawn"))
        {
            actualIds.Add(rowVariant.AsGodotObject().Get("spawn_id").AsString());
        }

        CheckEq(docSpawns.Count, actualIds.Count, "07 列出的位点数与 roaming_spawn 一致");
        foreach (Variant rowVariant in db.rows("roaming_spawn"))
        {
            var row = rowVariant.AsGodotObject();
            var spawnId = row.Get("spawn_id").AsString();
            CheckTrue(docSpawns.ContainsKey(spawnId), $"07 的逐位点表里有 {spawnId}");
            if (!docSpawns.ContainsKey(spawnId))
            {
                continue;
            }

            var docRow = docSpawns[spawnId];
            CheckEq(row.Get("region_id").AsString(), docRow.Region, $"{spawnId} 的区域与 07 一致");
            CheckEq(row.Get("team_id").AsString(), docRow.Team, $"{spawnId} 的队伍与 07 一致");
            CheckEq(row.Get("behavior").AsString(), docRow.Behavior, $"{spawnId} 的行为与 07 一致");
            CheckFloat(row.Get("alert_radius").AsDouble(), docRow.Alert, $"{spawnId} 的警戒半径与 07 一致", 0.0001);
            CheckEq(row.Get("respawn_sec").AsInt64(), docRow.Respawn, $"{spawnId} 的刷新秒数与 07 一致");
            CheckEq(row.Get("is_elite").AsInt64(), docRow.Elite, $"{spawnId} 的精英标记与 07 一致");
        }
    }
}