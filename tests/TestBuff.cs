using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 增益与套装（一·表与静态规则）：六张表的自洽（行数 ＋ **逐行**枚举与外键）、
/// `BuffService` 的静态规则、`SetService` 的计数口径里不依赖背包的那半边。
///
/// **落地那半**（装备／套装贡献进面板与战斗、`BattleActor` 的叠加与递减、六指令、进场 buff、
/// 战斗外计时、招式自带增益、叠层放大）在 `tests/TestBuffIntegration.cs`——那几节要
/// `Inventory`／`GameState`／`BattleSimulator`，2026-10-11 起它们都是 C# 了，所以也搬了过来。
/// **GDScript 那份 `tests/test_buff.gd` 已退场**（P4 第三十八批），两半合起来覆盖它的十二节。
///
/// 这里额外钉住 `BuffService` 的**常量取值口与绝对值**：旧链走
/// `BuffServiceScript.polarity_buff()` 这类过渡取值口，而变异探针第三块改 `.cs` 不重编
/// （那三条 case 已登记进 `$intentionallyUnpinned`），所以这一层的绝对值断言是那三条的替代网。
/// </summary>
public partial class TestBuff : TestCase
{
    public override string SuiteName() => "增益与套装（C# 侧）";

    public override void Run()
    {
        TableDb db = GetDb();
        _check_vocabulary();
        _check_tables(db);
        _check_table_rows(db);
        _check_buff_rules(db);
        _check_stat_mods(db);
        _check_grants(db);
        _check_sets(db);
    }

    /// <summary>08 的固定词表：绝对值 + 过渡取值口（GDScript 读不到 C# 的 const）。</summary>
    private void _check_vocabulary()
    {
        CheckEq(BuffService.polarity_buff(), 0, "增益极性 = 0");
        CheckEq(BuffService.polarity_debuff(), 1, "减益极性 = 1");
        CheckEq(BuffService.duration_permanent(), 0, "常驻（永久）哨兵 = 0");
        CheckEq(BuffService.duration_until_battle_end(), -1, "整场哨兵 = -1");
        CheckEq(BuffService.scope_battle(), "battle", "作用域 battle");
        CheckEq(BuffService.scope_field(), "field", "作用域 field");
        CheckEq(BuffService.stack_refresh(), "refresh", "叠加规则 refresh");
        CheckEq(BuffService.stack_stack(), "stack", "叠加规则 stack");
        CheckEq(BuffService.stack_unique(), "unique", "叠加规则 unique");
        CheckEq(BuffService.effect_kind_stat(), "stat", "效果类型 stat");
        CheckEq(BuffService.effect_kind_computed(), "computed", "效果类型 computed");
        CheckEq(BuffService.effect_kind_special(), "special", "效果类型 special");
        CheckEq(SetService.kind_equip(), "equip", "套装计数口径 equip");
        CheckEq(SetService.kind_active(), "skill_active", "套装计数口径 skill_active");
        CheckEq(SetService.kind_passive(), "skill_passive", "套装计数口径 skill_passive");
    }

    /// <summary>六张表的行数与 06 的清单一致（行数被砍会让下面的规则断言静默变少）。</summary>
    private void _check_tables(TableDb db)
    {
        CheckEq(db.rows("buff_def").Count, 19, "buff_def 19 条");
        CheckEq(db.rows("buff_stat").Count, 32, "buff_stat 32 条");
        CheckEq(db.rows("buff_grant").Count, 41, "buff_grant 41 条");
        CheckEq(db.rows("set_def").Count, 3, "set_def 3 套");
        CheckEq(db.rows("set_member").Count, 14, "set_member 14 行");
        CheckEq(db.rows("set_bonus").Count, 6, "set_bonus 6 档");
    }

    /// <summary>
    /// **逐行**自洽（对位 `test_buff.gd::_check_tables` 的循环那半，2026-10-11 P4 第三十八批补过来）：
    /// 枚举列必须在 08 写的取值里、`max_stack` 至少 1（不叠层的必须是 1）、每一行的外键都要真的存在。
    ///
    /// 这些不是"文档抄一遍"——枚举写错一个词、引用写错一个 id，效果都是**静默丢失**
    /// （`BuffService` 拿到空串按兜底值走），所以只能在这里逐行点。
    /// </summary>
    private void _check_table_rows(TableDb db)
    {
        string[] scopeValues = { "battle", "field" };
        string[] stackValues = { "refresh", "stack", "unique" };
        string[] effectKinds = { "stat", "computed", "special" };
        string[] sourceTypes = { "skill_passive", "skill_active", "equip", "set" };
        string[] triggers = { "on_cast", "on_equip", "on_hit", "on_battle_start" };
        string[] setKinds = { "equip", "skill_active", "skill_passive" };

        foreach (Variant entry in db.rows("buff_def"))
        {
            var row = (BuffDefRow)entry.AsGodotObject();
            CheckTrue(
                System.Array.IndexOf(scopeValues, row.scope) >= 0,
                $"{row.buff_id}.scope={row.scope} 在 08 的取值里");
            CheckTrue(
                System.Array.IndexOf(stackValues, row.stack_rule) >= 0,
                $"{row.buff_id}.stack_rule={row.stack_rule} 在 08 的取值里");
            CheckTrue(
                System.Array.IndexOf(effectKinds, row.effect_kind) >= 0,
                $"{row.buff_id}.effect_kind={row.effect_kind} 在 08 的取值里");
            CheckGt(row.max_stack, 0.0, $"{row.buff_id}.max_stack 至少 1");
            if (row.stack_rule != BuffService.stack_stack())
            {
                CheckEq(row.max_stack, 1, $"{row.buff_id} 不叠层时 max_stack 应当是 1");
            }
        }

        foreach (Variant entry in db.rows("buff_stat"))
        {
            var row = (BuffStatRow)entry.AsGodotObject();
            CheckTrue(db.get_row("buff_def", row.buff_id) != null, $"buff_stat 的 {row.buff_id} 在 buff_def 里存在");
            string kind = row.parsed_target()["kind"].AsString();
            CheckTrue(
                kind == "attr" || kind == "stat",
                $"buff_stat 的 target 带 attr:/stat: 前缀：{row.target}");
        }

        foreach (Variant entry in db.rows("buff_grant"))
        {
            var row = (BuffGrantRow)entry.AsGodotObject();
            CheckTrue(db.get_row("buff_def", row.buff_id) != null, $"buff_grant {row.grant_id} 发的 buff 存在");
            CheckTrue(
                System.Array.IndexOf(sourceTypes, row.source_type) >= 0,
                $"{row.grant_id}.source_type={row.source_type} 在 08 的取值里");
            CheckTrue(
                System.Array.IndexOf(triggers, row.trigger) >= 0,
                $"{row.grant_id}.trigger={row.trigger} 在 08 的取值里");
            string sourceTable = row.source_type switch
            {
                "skill_passive" or "skill_active" => "skill_base",
                "equip" => "equip_base",
                "set" => "set_def",
                _ => "",
            };
            if (sourceTable.Length > 0)
            {
                CheckTrue(
                    db.get_row(sourceTable, row.source_id) != null,
                    $"{row.grant_id} 的来源 {row.source_id} 在 {sourceTable} 里存在");
            }
        }

        foreach (Variant entry in db.rows("set_def"))
        {
            var row = (SetDefRow)entry.AsGodotObject();
            CheckTrue(
                System.Array.IndexOf(setKinds, row.set_kind) >= 0,
                $"{row.set_id}.set_kind={row.set_kind} 在 08 的取值里");
        }

        foreach (Variant entry in db.rows("set_member"))
        {
            var row = (SetMemberRow)entry.AsGodotObject();
            CheckTrue(db.get_row("set_def", row.set_id) != null, $"set_member 的 {row.set_id} 是已知套装");
            var def = (SetDefRow)db.get_row("set_def", row.set_id);
            string memberTable = def.set_kind == "equip" ? "equip_base" : "skill_base";
            CheckTrue(
                db.get_row(memberTable, row.member_id) != null,
                $"{row.set_id} 的成员 {row.member_id} 在 {memberTable} 里存在");
        }

        foreach (Variant entry in db.rows("set_bonus"))
        {
            var row = (SetBonusRow)entry.AsGodotObject();
            CheckTrue(db.get_row("buff_def", row.buff_id) != null, $"set_bonus {row.buff_id} 的 buff 存在");
            CheckGt(row.required_count, 0.0, $"{row.set_id} 档位至少 1 件");
        }
    }

    /// <summary>一个 buff 的静态规则：极性／作用域／持续／叠加／效果类型，缺行时各有兜底。</summary>
    private void _check_buff_rules(TableDb db)
    {
        var buffs = BuffService.create(db);
        CheckEq(buffs.all_defs().Count, 19, "BuffService 能列出全部 buff");
        CheckTrue(buffs.exists("buff_guard"), "防御姿态在表里");
        CheckFalse(buffs.exists("buff_bogus"), "表里没有的 buff 判不存在");
        CheckNotNull(buffs.def_of("buff_guard"), "def_of 拿得到那一行");
        CheckNull(buffs.def_of("buff_bogus"), "没有的行返回 null（调用方靠它兜底）");
        CheckEq(buffs.def_of("buff_guard").buff_id, "buff_guard", "拿到的就是那一条");

        // 现在 19 条 buff 全是增益（is_debuff 全 0）——设计还没给减益行，所以只能验这一侧
        CheckFalse(buffs.is_debuff("buff_guard"), "防御姿态是增益（不是减益）");
        CheckEq(buffs.polarity_of("buff_guard"), BuffService.polarity_buff(), "极性按 is_debuff 算");
        CheckEq(buffs.polarity_of("buff_bogus"), BuffService.polarity_buff(), "没有的行按增益兜底");

        CheckEq(buffs.scope_of("buff_guard"), BuffService.scope_battle(), "防御姿态是战斗内");
        CheckEq(buffs.scope_of("buff_meditated"), BuffService.scope_field(), "打坐余韵是战斗外");
        CheckEq(buffs.scope_of("buff_bogus"), "", "没有的行作用域为空");
        CheckTrue(buffs.is_field_buff("buff_meditated"), "打坐余韵是 field");
        CheckFalse(buffs.is_field_buff("buff_guard"), "防御姿态不是 field");
        CheckEq(buffs.field_minutes_of("buff_meditated"), 10, "战斗外增益 10 分钟（读表）");
        CheckEq(buffs.field_minutes_of("buff_guard"), 0, "战斗内增益不看 field_minutes");
        CheckEq(buffs.field_minutes_of("buff_bogus"), 0, "没有的行按 0 分钟");

        CheckEq(buffs.duration_of("buff_guard"), 1, "防御姿态持续 1 回合");
        CheckEq(buffs.duration_of("buff_regen"), BuffService.duration_until_battle_end(), "药王护体持续到本场结束");
        CheckEq(buffs.duration_of("buff_bogus"), 0, "没有的行按常驻兜底");
        CheckTrue(buffs.is_permanent("buff_rusty_moon"), "锈月剑意是常驻（duration=0）");
        CheckTrue(buffs.is_permanent("buff_meditated"), "战斗外增益也是 duration=0（常驻哨兵）");
        CheckFalse(buffs.is_permanent("buff_guard"), "1 回合的姿态不是常驻");

        CheckEq(buffs.stack_rule_of("buff_poison_amp"), BuffService.stack_stack(), "淬毒是叠层");
        CheckEq(buffs.stack_rule_of("buff_guard"), BuffService.stack_unique(), "防御姿态是唯一");
        CheckEq(buffs.stack_rule_of("buff_yunqi"), BuffService.stack_refresh(), "运功是刷新");
        CheckEq(buffs.stack_rule_of("buff_bogus"), "", "没有的行叠加规则为空");
        CheckEq(buffs.max_stack_of("buff_poison_amp"), 3, "淬毒最多 3 层");
        CheckEq(buffs.max_stack_of("buff_guard"), 1, "不叠层的上限是 1");
        CheckEq(buffs.max_stack_of("buff_bogus"), 1, "没有的行按 1 层兜底");

        CheckEq(buffs.effect_kind_of("buff_guard"), BuffService.effect_kind_stat(), "防御姿态是数值型");
        CheckEq(buffs.effect_kind_of("buff_yunqi"), BuffService.effect_kind_computed(), "运功是算出来的");
        CheckEq(buffs.effect_kind_of("buff_poison_amp"), BuffService.effect_kind_special(), "淬毒是特判");
        CheckEq(buffs.effect_kind_of("buff_bogus"), "", "没有的行效果类型为空");
    }

    /// <summary>数值修正与贡献列表：条数 = 修正条数 × 层数，层数按乘法放大。</summary>
    private void _check_stat_mods(TableDb db)
    {
        var buffs = BuffService.create(db);
        Godot.Collections.Array guard_mods = buffs.stat_mods_of("buff_guard");
        CheckEq(guard_mods.Count, 2, "防御姿态带两条数值修正（减伤＋外防）");
        CheckEq(buffs.stat_mods_of("buff_guard").Count, 2, "再问一次走缓存，结果一样");
        CheckEq(buffs.stat_mods_of("buff_bogus").Count, 0, "没有修正的 buff 是空表");
        CheckEq(buffs.stat_mods_of("buff_rusty_moon").Count, 2, "锈月两条（属性点＋固定值）");

        var by_kind = new Godot.Collections.Dictionary();
        foreach (Variant entry in guard_mods)
        {
            Godot.Collections.Dictionary mod = entry.AsGodotDictionary();
            by_kind[$"{mod["kind"].AsString()}:{mod["target_id"].AsString()}"] = mod["value"];
        }

        CheckTrue(by_kind.ContainsKey("stat:dmg_reduction"), "防御姿态给了减伤");
        CheckTrue(by_kind.ContainsKey("stat:def_phys"), "防御姿态给了外防");
        CheckEq(by_kind["stat:dmg_reduction"].AsDouble(), 0.25, "减伤 25%");
        CheckEq(by_kind["stat:def_phys"].AsDouble(), 15.0, "外防 +15");

        // 贡献通道：source 留空退回 `buff:<id>`，给了就用给的
        Godot.Collections.Array one = buffs.contributions_of("buff_guard", "", 1);
        CheckEq(one.Count, 2, "一条修正一份贡献");
        CheckEq(one[0].AsGodotDictionary()["source"].AsString(), "buff:buff_guard", "source 留空退回 buff:<id>");
        CheckEq(_contrib_value(one, AttributeCalculator.CONTRIB_STAT_FLAT, "dmg_reduction"), 0.25, "减伤进了固定值层");
        CheckEq(_contrib_value(one, AttributeCalculator.CONTRIB_STAT_FLAT, "def_phys"), 15.0, "外防进了固定值层");
        Godot.Collections.Array stacked = buffs.contributions_of("buff_guard", "test", 3);
        CheckEq(stacked.Count, 6, "3 层 → 每条修正各来三份");
        CheckEq(_contrib_value(stacked, AttributeCalculator.CONTRIB_STAT_FLAT, "def_phys"), 45.0, "叠层按乘法放大");
        CheckEq(stacked[0].AsGodotDictionary()["source"].AsString(), "test", "给了 source 就照它写");

        Godot.Collections.Array rusty = buffs.contributions_of("buff_rusty_moon", "", 1);
        CheckEq(_contrib_value(rusty, AttributeCalculator.CONTRIB_ATTR_POINT, "agi"), 4.0, "attr:* 走属性点层");
        CheckEq(_contrib_value(rusty, AttributeCalculator.CONTRIB_STAT_FLAT, "crit_rate"), 0.05, "stat:* 走固定值层");
        CheckEq(buffs.contributions_of("buff_bogus", "", 1).Count, 0, "没有修正就没有贡献");
    }

    /// <summary>发放表：谁发它、什么时机发、哪些算常驻。</summary>
    private void _check_grants(TableDb db)
    {
        var buffs = BuffService.create(db);
        Godot.Collections.Array taiping = buffs.grants_for("skill_passive", "pf_xuanwei_05", "on_cast");
        CheckEq(taiping.Count, 1, "太清靠 on_cast 发运功 buff");
        CheckEq((taiping[0].AsGodotObject() as BuffGrantRow).buff_id, "buff_taiping", "发的是太清境");
        CheckEq(buffs.grants_for("skill_passive", "pf_xuanwei_05", "on_hit").Count, 0, "时机不对就不发");
        CheckEq(buffs.grants_for("skill_passive", "pf_bogus", "on_cast").Count, 0, "没有的来源不发");

        CheckEq(buffs.persistent_grants_for("equip", "eq_sword_04").Count, 1, "锈月是装备即生效（常驻）");
        CheckEq(buffs.persistent_grants_for("equip", "eq_ring_03").Count, 0, "淬毒是命中触发，不算常驻");
        CheckEq(buffs.grants_for("equip", "eq_ring_03", "").Count, 1, "不过按来源查还是看得到它");
        CheckEq(buffs.persistent_grants_for("equip", "eq_bogus").Count, 0, "没有的来源没有常驻发放");

        CheckEq(buffs.grants_of_buff("buff_taiping", "").Count, 1, "按 buff 反查谁发它");
        CheckEq(buffs.grants_of_buff("buff_taiping", "on_hit").Count, 0, "按 buff + 时机过滤要能滤掉");
        CheckEq(buffs.grants_of_buff("buff_bogus", "").Count, 0, "没人发的 buff 是空表");

        CheckTrue(buffs.has_cast_grant("sk_drunk_zuibu"), "醉步是增益招式（有 on_cast 发放）");
        CheckFalse(buffs.has_cast_grant("sk_xuanwei_01"), "普通招式没有 on_cast 发放");
        CheckFalse(buffs.has_cast_grant(""), "空 id 直接判否");
    }

    /// <summary>套装的计数口径与档位向下兼容（`count_for` 要真背包，留在 GDScript 侧跑）。</summary>
    private void _check_sets(TableDb db)
    {
        var sets = SetService.create(db);
        CheckEq(sets.all_defs().Count, 3, "第一章 3 套套装");
        CheckTrue(sets.exists("set_heifeng"), "黑风寨在表里");
        CheckFalse(sets.exists("set_bogus"), "没有的套装判不存在");
        CheckEq(sets.kind_of("set_heifeng"), SetService.kind_equip(), "黑风寨是装备套");
        CheckEq(sets.kind_of("set_xuanwei_sword"), SetService.kind_active(), "玄微剑意是招式套");
        CheckEq(sets.kind_of("set_xuanwei_qi"), SetService.kind_passive(), "玄微心法是内功套");
        CheckEq(sets.kind_of("set_bogus"), "", "没有的套装类型为空");

        CheckEq(sets.members_of("set_heifeng").Length, 4, "黑风寨 4 件成员");
        CheckEq(sets.members_of("set_xuanwei_sword").Length, 5, "玄微剑意 5 招");
        CheckEq(sets.members_of("set_xuanwei_qi").Length, 5, "玄微心法 5 部");
        CheckEq(sets.members_of("set_bogus").Length, 0, "没有的套装没有成员");

        Godot.Collections.Array heifeng_bonuses = sets.bonuses_of("set_heifeng");
        CheckEq(heifeng_bonuses.Count, 2, "黑风寨两档（2 件／4 件）");
        CheckEq((heifeng_bonuses[0].AsGodotObject() as SetBonusRow).required_count, 2, "档位按 required_count 升序：第一条是 2 件");
        CheckEq((heifeng_bonuses[1].AsGodotObject() as SetBonusRow).required_count, 4, "第二条是 4 件");
        CheckEq(sets.bonuses_of("set_xuanwei_qi").Count, 2, "心法两档（4 格／7 格）");
        CheckEq(sets.bonuses_of("set_bogus").Count, 0, "没有的套装没有档位");

        CheckEq(sets.active_bonuses("set_heifeng", 1).Count, 0, "1 件哪一档都不够");
        CheckEq(sets.active_bonuses("set_heifeng", 2).Count, 1, "2 件只有低档");
        CheckEq(sets.active_bonuses("set_heifeng", 4).Count, 2, "4 件时两档都生效（向下兼容）");
        CheckEq(sets.active_buff_ids("set_heifeng", 2).Length, 1, "2 件时一个 buff");
        CheckEq(sets.active_buff_ids("set_heifeng", 4).Length, 2, "4 件时两个 buff");
        CheckEq(sets.active_buff_ids("set_heifeng", 99).Length, 2, "超出最高档也不再涨");
        CheckEq(sets.active_buff_ids("set_xuanwei_sword", 5).Length, 2, "5 招时 3 招档＋5 招档都生效");
        CheckEq(sets.active_buff_ids("set_xuanwei_qi", 7).Length, 2, "7 格时两档都生效");
        CheckEq(sets.active_buff_ids("set_xuanwei_qi", 4)[0], "buff_set_xuanwei_qi_4", "低档在前（升序）");
    }

    /// <summary>某个「层／项」在贡献列表里的合计值。</summary>
    private static double _contrib_value(
        Godot.Collections.Array contributions, string kind, string target)
    {
        var total = 0.0;
        foreach (Variant entry in contributions)
        {
            Godot.Collections.Dictionary item = entry.AsGodotDictionary();
            if (item["kind"].AsString() != kind || item["target"].AsString() != target)
            {
                continue;
            }

            total += item["value"].AsDouble();
        }

        return total;
    }
}
