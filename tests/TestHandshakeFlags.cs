using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 旗标与可达性——"策划对接守卫"的一族（拆自 tests/test_handshake.gd，2026-10-04；2026-10-11 搬 C#）。
/// 只换语言、不改行为：常量与共用夹具在 `TestHandshake`（base），本族只放自己这一类的检查。
/// </summary>
public partial class TestHandshakeFlags : TestHandshake
{
    public override string SuiteName() => "旗标与可达性";

    public override void Run()
    {
        CheckFlagReachability();
        CheckSidequestFavors();
        CheckSetTierReachability();
    }

    /// <summary>被要求的旗标必须有人能点亮；缺口清单**双向**维护。</summary>
    private void CheckFlagReachability()
    {
        var db = GetDb();
        var required = new List<string>();
        var rules = new (string Table, string Column)[]
        {
            ("recruit_def", "join_condition"),
            ("guide_step", "condition"),
            ("story_node", "trigger_condition"),
            ("chapter_def", "complete_condition"),
            ("dialogue_node", "condition"),
            ("dialogue_option", "condition"),
            ("npc_quest", "requirement"),
        };
        foreach (var (table, column) in rules)
        {
            CollectRequiredFlags(db, table, column, required);
        }

        CheckGt(required.Count, 3.0, $"扫到足够多「被要求的旗标」（{required.Count} 枚）");

        var produced = new List<string>();
        foreach (Variant entry in db.rows("dialogue_option"))
        {
            var row = entry.AsGodotObject();
            foreach (var flagId in DialogueOptionRow.parse_set_flags(row.Get("set_flag").AsString()))
            {
                NoteFlag(produced, flagId);
            }
        }

        foreach (var tableName in new[] { "event_check", "hidden_trigger" })
        {
            foreach (Variant entry in db.rows(tableName))
            {
                var row = entry.AsGodotObject();
                if (row.Get("reward_type").AsString() == "event")
                {
                    NoteFlag(produced, row.Get("reward_id").AsString());
                }
            }
        }

        foreach (Variant entry in db.rows("story_node"))
        {
            var row = entry.AsGodotObject();
            if (row.Get("kind").AsString() != "chapter_end")
            {
                continue;
            }

            var chapter = db.get_row("chapter_def", row.Get("chapter_id").AsString());
            if (chapter != null)
            {
                NoteFlag(produced, chapter.Get("complete_condition").AsString());
            }
        }

        CollectCodeProducers(produced);

        var missing = new List<string>();
        foreach (var flagId in required)
        {
            if (!FlagProduced(produced, flagId) && Array.IndexOf(KnownFlagGaps, flagId) < 0)
            {
                missing.Add(flagId);
            }
        }

        CheckEq(
            missing.Count,
            0,
            $"每一枚被要求的旗标都有人能点亮（没人管的：{string.Join("、", missing)}）——"
            + "要么补来源、要么写进 KnownFlagGaps 并写明等谁");

        // 缺口清单**双向**维护：补上了就要把那一行删掉（过期清单 = 下一个人照着它白找）
        var stale = new List<string>();
        foreach (var flagId in KnownFlagGaps)
        {
            if (FlagProduced(produced, flagId))
            {
                stale.Add(flagId);
            }
        }

        CheckEq(stale.Count, 0, $"已知缺口清单里这些已经有来源了，请删掉对应行：{string.Join("、", stale)}");
    }
    /// <summary>条件语言里被**要求**的旗标（按 & 拆；只看 flag_*，origin: / item: 那些不是旗标）。</summary>
    private static void CollectRequiredFlags(TableDb db, string tableName, string column, List<string> outList)
    {
        if (!db.has_table(tableName))
        {
            return;
        }

        foreach (Variant entry in db.rows(tableName))
        {
            var row = entry.AsGodotObject();
            // 条件语言的三种连接符都要拆：`&` 全部满足、`|` 任一满足、`!` 取反。
            // `!flag_x` 里被**要求**的仍然是 `flag_x`（要求它"没被点亮"也是要求），照收。
            foreach (var group in row.Get(column).AsString().Replace("|", "&").Split('&'))
            {
                var token = group.Trim();
                while (token.StartsWith("!", StringComparison.Ordinal))
                {
                    token = token.Substring(1).Trim();
                }

                if (token.StartsWith("flag_", StringComparison.Ordinal))
                {
                    NoteFlag(outList, token);
                }
            }
        }
    }

    private static void NoteFlag(List<string> outList, string flagId)
    {
        var token = flagId.Trim();
        if (token.StartsWith("flag_", StringComparison.Ordinal) && !outList.Contains(token))
        {
            outList.Add(token);
        }
    }

    /// <summary>
    /// 代码里的生产者。**故意算「提到过」而不是做数据流**：读不了 AST，能做的是「这个字面量在代码里
    /// 出现过」；`TEAM_WIN_FLAGS` 那种**装在常量表里**的写法只有这样才认得出来。
    /// 代价是「只被读、从没被 set」的旗标会漏网——那类由「谁要求它」那一侧自己的用例管
    /// （`test_event_check` 就钉了「掷中＝救出来了」，反向验证过：把来源改字那条会红）。
    ///
    /// 另有一条**已知的假阴性**：`src/` 里把旗标写在**自检函数**里也算数（例：`npc_panel` 的自检
    /// 手动置过 `flag_qiutu_saved`）。
    /// </summary>
    private static void CollectCodeProducers(List<string> outList)
    {
        var literalRe = new Regex("[\"'](flag_[a-zA-Z0-9_%]+)[\"']");
        // C# 的插值写法（`$"flag_{char_id}_joined"`）抓不到上面那条字面量正则（`{` 不在字符类里、
        // 洞里的表达式还带引号）。单开一条把插值洞折成 `%s`：第 1／2 组是固定的前后缀。
        var csInterpRe = new Regex("\\$@?\"(flag_[^\"{]*)\\{[^}]*\\}([^\"}]*)\"");
        foreach (var path in CollectFiles(new[] { "src" }))
        {
            var text = FileAccess.GetFileAsString($"res://{path}");
            if (text.Length == 0)
            {
                continue;
            }

            foreach (Match hit in literalRe.Matches(text))
            {
                NoteLiteral(outList, hit.Groups[1].Value);
            }

            if (path.EndsWith(".cs", StringComparison.Ordinal))
            {
                foreach (Match hit in csInterpRe.Matches(text))
                {
                    NoteLiteral(outList, $"{hit.Groups[1].Value}%s{hit.Groups[2].Value}");
                }
            }
        }
    }

    /// <summary>带 % 的字面量（如 flag_%s_joined）当模式存下来，比对时按前后缀匹配。</summary>
    private static void NoteLiteral(List<string> outList, string literal)
    {
        var token = literal.Trim();
        if (token.Length == 0 || outList.Contains(token))
        {
            return;
        }

        if (token.Contains('%'))
        {
            outList.Add(token);
            return;
        }

        NoteFlag(outList, token);
    }

    private static bool FlagProduced(List<string> produced, string flagId)
    {
        if (produced.Contains(flagId))
        {
            return true;
        }

        foreach (var entry in produced)
        {
            if (!entry.Contains('%'))
            {
                continue;
            }

            var parts = entry.Split("%s");
            if (parts.Length != 2)
            {
                continue;
            }

            if (flagId.StartsWith(parts[0], StringComparison.Ordinal)
                && flagId.EndsWith(parts[1], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
    /// <summary>
    /// 20 号 §九 的十条支线表 ↔ `npc_quest`：**好感那一列必须对得上**。
    /// 只扫 §九 那张表：表头是「| # | 名称 | 归属 | 类型 | 地点 | 奖励 | 好感 |」。
    /// </summary>
    private void CheckSidequestFavors()
    {
        var doc = FileAccess.GetFileAsString("res://docs/design/20_第一章剧情.md");
        CheckFalse(doc.Length == 0, "读得到 20 号剧情文档");
        var db = GetDb();

        var start = doc.IndexOf("| # | 名称 | 归属 | 类型 | 地点 | 奖励 | 好感 |", StringComparison.Ordinal);
        CheckTrue(start >= 0, "找得到 §九 的十条支线表");
        if (start < 0)
        {
            return;
        }

        var body = doc.Substring(start);
        var rows = 0;
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || !line.StartsWith("|", StringComparison.Ordinal))
            {
                break;
            }

            var cells = line.Split('|');
            if (cells.Length < 8)
            {
                continue;
            }

            var owner = cells[3].Trim();
            if (owner == "归属" || owner.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            rows += 1;
            if (Array.IndexOf(SidequestForceSkip, owner) >= 0)
            {
                continue;
            }

            var wanted = new List<long>();
            foreach (var piece in cells[7].Trim().Replace("＋", "").Split('／'))
            {
                var digits = "";
                foreach (var ch in piece)
                {
                    if (char.IsAsciiDigit(ch))
                    {
                        digits += ch;
                    }
                }

                if (digits.Length > 0)
                {
                    wanted.Add(long.Parse(digits));
                }
            }

            CheckGt(wanted.Count, 0.0, $"§九 里「{owner}」那条写了好感");
            var npcId = NpcIdForDisplayName(db, owner);
            CheckTrue(npcId.Length > 0, $"「{owner}」按中文名在 npc_def 里找得到");
            if (npcId.Length == 0)
            {
                continue;
            }

            var got = new List<long>();
            foreach (Variant entry in db.rows_where("npc_quest", "npc_id", npcId))
            {
                got.Add(entry.AsGodotObject().Get("reward_favor").AsInt64());
            }

            foreach (var value in wanted)
            {
                CheckTrue(
                    got.Contains(value),
                    $"§九 说「{owner}」这条给 +{value} 好感，他的委托好感却是 [{string.Join(", ", got)}]"
                    + "——两边对不上就有一边写错了");
            }
        }

        CheckGt(rows, 5.0, $"§九 表里解析出足够多的支线（{rows} 条）");
    }

    private static string NpcIdForDisplayName(TableDb db, string nameCn)
    {
        foreach (Variant entry in db.rows("npc_def"))
        {
            var row = entry.AsGodotObject();
            if (row.Get("name_cn").AsString() == nameCn)
            {
                return row.Get("npc_id").AsString();
            }
        }

        return "";
    }
    /// <summary>
    /// 51. 套装的**每一档**必须真的凑得出来（按「已经发得出来的来源」算）。
    /// equip 按**槽位**算上限（同槽位最多 `equip_slot_def.max_equip` 件，戒指两枚）；
    /// skill_active 招式数 ＝ 来源已实现的成员数；skill_passive 格数 ＝ `slot_cost` 之和。
    /// </summary>
    private void CheckSetTierReachability()
    {
        var db = GetDb();
        var obtainable = ObtainableIds(db);
        var reachableSkills = ReachableSkillIds(db);
        var dead = new List<string>();
        var tiers = 0;
        foreach (Variant defEntry in db.rows("set_def"))
        {
            var def = defEntry.AsGodotObject();
            var setId = def.Get("set_id").AsString();
            var kind = def.Get("set_kind").AsString();
            var members = new List<string>();
            foreach (Variant memberEntry in db.rows("set_member"))
            {
                var memberRow = memberEntry.AsGodotObject();
                if (memberRow.Get("set_id").AsString() == setId)
                {
                    members.Add(memberRow.Get("member_id").AsString());
                }
            }

            if (members.Count == 0)
            {
                continue;
            }

            var reachCap = 0;
            if (kind == "equip")
            {
                var perSlot = new Dictionary<string, int>();
                foreach (var member in members)
                {
                    if (!obtainable.Contains(member))
                    {
                        continue;
                    }

                    var equip = db.get_row("equip_base", member);
                    var slot = equip != null ? equip.Get("slot").AsString() : "";
                    perSlot[slot] = perSlot.TryGetValue(slot, out var count) ? count + 1 : 1;
                }

                foreach (var (slot, count) in perSlot)
                {
                    var limit = 1;
                    var slotRow = db.get_row("equip_slot_def", slot);
                    if (slotRow != null)
                    {
                        limit = (int)slotRow.Get("max_equip").AsInt64();
                    }

                    reachCap += Math.Min(count, limit);
                }
            }
            else if (kind == "skill_active")
            {
                foreach (var member in members)
                {
                    if (reachableSkills.Contains(member))
                    {
                        reachCap += 1;
                    }
                }
            }
            else if (kind == "skill_passive")
            {
                foreach (var member in members)
                {
                    if (!reachableSkills.Contains(member))
                    {
                        continue;
                    }

                    var passive = db.get_row("skill_passive", member);
                    if (passive != null)
                    {
                        reachCap += (int)passive.Get("slot_cost").AsInt64();
                    }
                }
            }

            foreach (Variant tierEntry in db.rows("set_bonus"))
            {
                var tier = tierEntry.AsGodotObject();
                if (tier.Get("set_id").AsString() != setId)
                {
                    continue;
                }

                tiers += 1;
                if (reachCap >= tier.Get("required_count").AsInt64())
                {
                    continue;
                }

                dead.Add($"{setId}|{tier.Get("required_count").AsInt64()}");
            }
        }

        CheckGt(tiers, 4.0, $"扫到足够多的套装档位（{tiers} 档）");
        var unexpected = new List<string>();
        foreach (var key in dead)
        {
            if (Array.IndexOf(KnownUnreachableSetTiers, key) < 0)
            {
                unexpected.Add(key);
            }
        }

        CheckEq(
            unexpected.Count,
            0,
            $"这些套装档位按已实现的来源凑不出来（要么补来源、要么写进 KnownUnreachableSetTiers 并写明等谁）："
            + $"{string.Join("、", unexpected)}");

        // 缺口清单**双向**维护：设计把来源补上之后，这里会红，提示删掉那一行
        var stale = new List<string>();
        foreach (var key in KnownUnreachableSetTiers)
        {
            if (!dead.Contains(key))
            {
                stale.Add(key);
            }
        }

        CheckEq(stale.Count, 0, $"已知缺口清单里这些档位已经凑得出来了，请删掉对应行：{string.Join("、", stale)}");
    }
    /// <summary>
    /// 「已经发得出来」的 id 集合——与 `validate_tables.ps1` 5.19 同一套通道，再加**代码里点名的 id**
    /// （剧情物由代码发，如 `battle_screen.TEAM_WIN_ITEMS` 那本账册）。
    /// </summary>
    private static HashSet<string> ObtainableIds(TableDb db)
    {
        var outSet = new HashSet<string>();
        foreach (var tableName in new[] { "drop_table", "shop_stock", "npc_offer" })
        {
            foreach (Variant entry in db.rows(tableName))
            {
                var key = entry.AsGodotObject().Get("item_id").AsString();
                if (key.Length > 0)
                {
                    outSet.Add(key);
                }
            }
        }

        foreach (Variant entry in db.rows("enemy_equip"))
        {
            outSet.Add(entry.AsGodotObject().Get("equip_id").AsString());
        }

        foreach (Variant entry in db.rows("world_event"))
        {
            // `gift` 的 effect_id 才是物品（`trade` 是货架组、`spar` 是队伍）
            var row = entry.AsGodotObject();
            if (row.Get("effect_kind").AsString() == "gift")
            {
                var effectId = row.Get("effect_id").AsString();
                if (effectId.Length > 0)
                {
                    outSet.Add(effectId);
                }
            }
        }

        foreach (var tableName in new[] { "event_check", "hidden_trigger" })
        {
            foreach (Variant entry in db.rows(tableName))
            {
                var key = entry.AsGodotObject().Get("reward_id").AsString();
                if (key.Length > 0)
                {
                    outSet.Add(key);
                }
            }
        }

        foreach (Variant entry in db.rows("character_base"))
        {
            foreach (var equipId in entry.AsGodotObject().Get("start_equip_ids").AsString().Split('|'))
            {
                outSet.Add(equipId.Trim());
            }
        }

        var equipIds = new List<string>();
        foreach (Variant entry in db.rows("equip_base"))
        {
            equipIds.Add(entry.AsGodotObject().Get("equip_id").AsString());
        }

        foreach (var path in CollectFiles(new[] { "src" }))
        {
            var text = FileAccess.GetFileAsString($"res://{path}");
            if (text.Length == 0)
            {
                continue;
            }

            foreach (var equipId in equipIds)
            {
                if (text.Contains(equipId, StringComparison.Ordinal))
                {
                    outSet.Add(equipId);
                }
            }
        }

        return outSet;
    }

    /// <summary>
    /// 来源已实现的武学 id。`SkillGrant.implemented_sources()` 是唯一真相（别在这里再列一遍），
    /// 外加 `start`——起手武学从角色模板或创建界面的「起始武学」来，**都是玩家能拿到的**。
    /// </summary>
    private static HashSet<string> ReachableSkillIds(TableDb db)
    {
        var outSet = new HashSet<string>();
        var implemented = SkillGrant.implemented_sources();
        foreach (Variant entry in db.rows("skill_base"))
        {
            var row = entry.AsGodotObject();
            var source = row.Get("source_type").AsString();
            if (source == "start" || Array.IndexOf(implemented, source) >= 0)
            {
                outSet.Add(row.Get("skill_id").AsString());
            }
        }

        return outSet;
    }
}
