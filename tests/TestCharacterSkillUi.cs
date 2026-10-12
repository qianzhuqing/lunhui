using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 角色面板**武学页**那一半（UI 清单 §二-6）——从 `TestCharacterUi.cs` 拆出来的 partial（2026-10-12）。
///
/// 钉的是这一页的**排版与口径**（数值怎么算是 `TestCharacterSheet.cs` 的事）：
/// 三栏（招式格／心法占格／被动＋套装）、顶部切人页签、占用数写「N / M 槽（悟性 X）」、
/// 点一格＝只选中、详情按清单写消耗内力／伤害倍率／段数、招式的附加效果写成一句白话、
/// 招式槽满了「装备到快捷键」置灰并把原因写出来、被动与套装段照表读，**整页不出现表内 id**。
/// </summary>
public partial class TestCharacterUi
{
    private void _CheckSkillPage(TableDb db, CharacterScreen screen, GodotObject state)
    {
        screen.select_tab(UiNav.char_tab_of(UiNav.SKILL));
        CharacterSheet panel = screen.sheet();
        CheckEq(screen.current_tab(), UiNav.char_tab_of(UiNav.SKILL), "切到武学页");
        CheckTrue(screen.FindChild("SkillGridColumn", true, false) != null, "武学页有招式格那一栏");
        CheckTrue(screen.FindChild("SkillPassiveColumn", true, false) != null, "武学页有心法占格那一栏");
        CheckTrue(screen.FindChild("SkillInfoColumn", true, false) != null, "武学页有被动／套装那一栏");

        // 顶部队伍页签（清单 §二-6：这一页最需要切人）：每人写等级与已学招数
        var member = screen.FindChild($"SkillMemberButton{CharId}", true, false) as Button;
        CheckNotNull(member, "武学页顶部有队伍页签");
        if (member != null)
        {
            CheckTrue(
                member.Text.Contains("Lv") && member.Text.Contains("招"),
                $"页签写等级与已学招数：{member.Text}");
        }

        // 招式格区：占用数按清单的原话写「N / M 槽（悟性 X）」——悟性那一项要写出来
        Godot.Collections.Dictionary summary = panel.slot_summary();
        // 占用数写在小节头右边的次要字里（`_section` 的 `SectionHint`，示意图的 `.lu-panel-h` ＋ `.lu-dim`）
        Node active_section = screen.FindChild("ActiveSection", true, false);
        CheckNotNull(active_section, "招式格区有标题");
        var active_hint = active_section?.FindChild("SectionHint", true, false) as Label;
        if (active_hint != null)
        {
            string expect = $"{summary["active_used"].AsInt32()} / {summary["active_slots"].AsInt32()}"
                + $" 槽（悟性 {panel.total_attrs()["wu"].AsInt32()}）";
            CheckEq(active_hint.Text, expect, "招式格区顶部写占用数与悟性");
        }

        // 心法占格区：「N / M 格（根骨 X）」＋ 剩下的空位写「剩 N 格」
        int passive_used = summary["passive_used"].AsInt32();
        int passive_capacity = summary["passive_capacity"].AsInt32();
        Node passive_section = screen.FindChild("PassiveSection", true, false);
        CheckNotNull(passive_section, "心法占格区有标题");
        var passive_hint = passive_section?.FindChild("SectionHint", true, false) as Label;
        if (passive_hint != null)
        {
            CheckEq(
                passive_hint.Text,
                $"{passive_used} / {passive_capacity} 格（根骨 {panel.total_attrs()["gen"].AsInt32()}）",
                "心法占格区顶部写占用格数与根骨");
        }

        CheckTrue(
            screen.FindChild($"PassiveFree{passive_capacity - passive_used}", true, false) != null,
            $"剩下的空位写出来（剩 {passive_capacity - passive_used} 格）");

        // 一格＝名字／星级（清单 §二-6），摘要那一行还写着内力
        const string skill_id = "sk_xuanwei_01";
        var skill_base = db.get_row("skill_base", skill_id) as SkillBaseRow;
        var cell = screen.FindChild($"SkillCell{skill_id}", true, false) as Button;
        CheckNotNull(cell, "招式格里有一格");
        if (cell != null && skill_base != null)
        {
            CheckTrue(
                cell.Text.Contains(skill_base.name_cn) && cell.Text.Contains("★"),
                $"一格写名字与星级：{cell.Text}");
        }

        var summary_label = screen.FindChild($"Skill{skill_id}", true, false) as Label;
        CheckTrue(
            summary_label != null && summary_label.Text.Contains("内力"),
            $"那一行的摘要写着耗气：{(summary_label != null ? summary_label.Text : "缺")}");

        // 点一格＝选中（清单 §二-6），详情写消耗内力／伤害倍率／段数、附加效果写成一句白话
        var active_row = db.get_row("skill_active", skill_id) as SkillActiveRow;
        CheckNotNull(active_row, "表里有这一招");
        if (cell == null || active_row == null || skill_base == null)
        {
            return;
        }

        cell.EmitSignal(Button.SignalName.Pressed);
        var detail_header = screen.FindChild("SkillDetailHeader", true, false) as Label;
        CheckEq(detail_header?.Text, skill_base.name_cn, "点一格后详情栏写出它的名字");
        var cost = screen.FindChild("SkillDetailCostRowValue", true, false) as Label;
        CheckEq(cost?.Text, active_row.qi_cost.ToString(), "详情写消耗内力");

        // 系别：读 `element_def.name_cn`（外功／内功／奇诡）——**那一行不许写 external 这类 id**
        var element_def = db.get_row("element_def", active_row.element) as ElementDefRow;
        CheckNotNull(element_def, "element_def 有这一系的维表行");
        var element = screen.FindChild("SkillDetailElementRowValue", true, false) as Label;
        CheckEq(element?.Text, element_def != null ? element_def.name_cn : "", "详情写系别的中文名");
        CheckTrue(
            element != null && element.Text.Length > 0 && !element.Text.Contains(active_row.element),
            $"系别那一行不写表内 id：{(element != null ? element.Text : "缺")}");

        // 结算：类型名 ＋ 吃哪门防御 ＋ 可暴击／可闪避（白话，按 `damage_type` 那三列拼）
        var damage_type = db.get_row("damage_type", active_row.damage_type) as DamageTypeRow;
        var settle = screen.FindChild("SkillDetailSettleRowValue", true, false) as Label;
        CheckTrue(
            settle != null && damage_type != null
                && settle.Text.Contains(damage_type.name_cn)
                && settle.Text.Contains(damage_type.use_def_phys ? "吃外功防御" : "无视防御"),
            $"结算写成白话：{(settle != null ? settle.Text : "缺")}");

        // 段数是**这个角色手里**的段数（`hit_count ＋ extra_hit`，与战斗侧同一算法），不是表里的裸值
        int expect_hits = BattleSimulator.effective_hits_for(
            active_row.skill_id, active_row.hit_count, panel.stat_value("extra_hit"));
        var power = screen.FindChild("SkillDetailPowerRowValue", true, false) as Label;
        CheckEq(
            power?.Text,
            ((int)System.Math.Round(active_row.power_ratio * 100.0)).ToString() + "%"
                + (expect_hits >= 2 ? "／段" : ""),
            "详情写伤害倍率（段数 ≥2 时缀「／段」）");
        var hits = screen.FindChild("SkillDetailHitRowValue", true, false) as Label;
        CheckEq(hits?.Text, expect_hits.ToString(), "详情写招式段数（按这个角色算）");
        var poise = screen.FindChild("SkillDetailPoiseRowValue", true, false) as Label;
        CheckEq(poise?.Text, active_row.poise_damage.ToString(), "详情写破架势");
        var target = screen.FindChild("SkillDetailTargetRowValue", true, false) as Label;
        CheckTrue(
            target != null && target.Text.Length > 0 && !target.Text.Contains(active_row.target_type),
            $"目标写成中文、不写 table 里的枚举：{(target != null ? target.Text : "缺")}");

        // 附加效果那句白话：表里配了 `status_id` 就写状态名，没配写「无附加效果」——
        // **两种都不许出现表内 id**（文案审计盯着）
        string expect_status = "无附加效果";
        if (active_row.status_id.Length > 0
            && db.get_row("status_effect", active_row.status_id) is StatusEffectRow status_row)
        {
            expect_status = $"附带异常：{status_row.name_cn}";
        }

        var status = screen.FindChild("SkillDetailStatusRow", true, false) as Label;
        CheckEq(status?.Text, expect_status, "附加效果写成一句白话");

        // 「装备到快捷键」那一枚就摆在详情里；卸下（遗忘）在装着的招上是可点的
        CheckNotNull(
            screen.FindChild($"SkillEquipButton{skill_id}", true, false), "详情里有「装备到快捷键」");
        var forget = screen.FindChild($"SkillForgetButton{skill_id}", true, false) as Button;
        CheckNotNull(forget, "详情里有「遗忘」");
        if (forget != null)
        {
            CheckFalse(forget.Disabled, "装着的招「遗忘」可点");
        }

        // 心法：装一部内功 → 占格区多一行、点它出「占几格」的详情
        var game_state = (GameState)state;
        if (!game_state.is_learned(CharId, "pf_xuanwei_01"))
        {
            game_state.learn_skill(CharId, "pf_xuanwei_01");
        }

        screen.toggle_skill("pf_xuanwei_01");
        screen.refresh();
        var passive_cell = screen.FindChild("SkillCellpf_xuanwei_01", true, false) as Button;
        CheckNotNull(passive_cell, "心法占格区摆出了刚装上的那部内功");
        if (passive_cell != null)
        {
            passive_cell.EmitSignal(Button.SignalName.Pressed);
            var slot_row = screen.FindChild("SkillDetailSlotRowValue", true, false) as Label;
            var passive_row = db.get_row("skill_passive", "pf_xuanwei_01") as SkillPassiveRow;
            CheckEq(
                slot_row?.Text, passive_row != null ? $"{passive_row.slot_cost} 格" : "",
                "点一格内功写出它占几格");
        }

        // 被动段：装着的内功要在这一栏有名字＋来源（清单 §二-6「每行：名 ＋ 来源 ＋ 效果」）
        CheckNotNull(
            screen.FindChild("PassiveListRowpf_xuanwei_01", true, false), "被动段列出装着的内功");
        var passive_source = screen.FindChild("PassiveListRowpf_xuanwei_01Source", true, false) as Label;
        CheckTrue(
            passive_source != null && passive_source.Text.Contains("内功"),
            $"被动那一行写出来源：{(passive_source != null ? passive_source.Text : "缺")}");

        // 招式槽满了：「装备到快捷键」置灰并写原因（清单 §二-6 那一行的置灰原因）
        foreach (string probe in new[] { "sk_xuanwei_02", "sk_xuanwei_03", "sk_xuanwei_04" })
        {
            if (!game_state.is_learned(CharId, probe))
            {
                game_state.learn_skill(CharId, probe);
            }
        }

        foreach (string probe in new[] { "sk_xuanwei_02", "sk_xuanwei_03" })
        {
            if (screen.sheet().loadout().active_used() < screen.sheet().loadout().active_slots())
            {
                screen.toggle_skill(probe);
            }
        }

        CheckEq(
            screen.sheet().loadout().active_used(), screen.sheet().loadout().active_slots(),
            "招式槽被填满了（这条用例的前提）");
        screen.select_skill("sk_xuanwei_04");
        var full_button = screen.FindChild("SkillEquipButtonsk_xuanwei_04", true, false) as Button;
        CheckNotNull(full_button, "选中那一招有「装备到快捷键」");
        if (full_button != null)
        {
            CheckTrue(full_button.Disabled, "招式槽已满，那一枚置灰");
            CheckTrue(
                full_button.TooltipText.Contains("招式槽已满"),
                $"置灰原因写出来：{full_button.TooltipText}");
        }

        // 套装效果：进度与档位都按 SetService 的真实算法写，达成的档位同时出现在被动段
        SetService sets = SetService.create(db);
        int count = sets.count_for("set_xuanwei_sword", CharId, _InventoryOf(state), state);
        var top = 0;
        foreach (Variant tier_value in sets.bonuses_of("set_xuanwei_sword"))
        {
            top = System.Math.Max(top, tier_value.As<SetBonusRow>().required_count);
        }

        var set_count = screen.FindChild("SkillSetCountset_xuanwei_sword", true, false) as Label;
        CheckEq(set_count?.Text, $"{count} / {top} · 招式", "套装写「N / M · 招式」");
        var tier_state = screen.FindChild("SkillSetStateset_xuanwei_sword_3", true, false) as Label;
        CheckEq(tier_state?.Text, count >= 3 ? "已激活" : "未达", "档位达成与否写「已激活／未达」");
        CheckNotNull(
            screen.FindChild("PassiveListRowset_xuanwei_sword_3", true, false),
            "已达成的套装档位同时出现在被动段");

        // 整页（只到武学那一页）不许出现表内 id（决策 244）
        Node skill_box = screen.FindChild("Box武学", true, false);
        CheckNotNull(skill_box, "找得到武学那一页");
        string[] copy_hits =
            skill_box != null ? CopyGuard.id_tokens(skill_box) : System.Array.Empty<string>();
        CheckEq(
            copy_hits.Length, 0,
            $"武学页不出现表内 id：{(copy_hits.Length > 0 ? string.Join("；", copy_hits) : "无")}");
    }
}
