using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// **武学页**（UI 清单 §二-6）——从 `CharacterScreenPages.cs` 拆出来的 partial（2026-10-12）。
///
/// 拆的原因：这一页要「顶部切人 ＋ 招式格 ＋ 选中那一招的详情 ＋ 心法占格 ＋ 内力参数 ＋ 被动 ＋
/// 套装效果」七块，摆回那一份会顶破 800 行硬上限（那份要守 `docs/dev/代码拆分清单.md`）。
///
/// 排版照策划示意图第 7 屏那三块：左＝招式格 ＋ 选中那一招的详情｜中＝心法占格 ＋ 内力｜
/// 右＝被动 ＋ 套装效果。
///
/// 这一份只干一件事：按 `CharacterSheet`（已学武学／占格／派生数值）、`SetService`（套装进度）
/// 给的数据摆控件。**数值一个都不在这里算**——「战斗初始气」那种表里没有的量如实写「开场即满」
/// （`BattleActor.refill()` 开场把气回满），不编一个数字出来充数。
/// 名字（`SkillCell&lt;id&gt;`／`LoadoutButton&lt;id&gt;` 这种）就是用例与自检的接口，别改。
/// </summary>
public partial class CharacterScreen
{
    /// <summary>招式格那一栏的宽度。窄了「熟练 12/10　倍率 1.40　内力 8　段数 1」那一行会折行
    /// （真窗口里量过：350 刚好放下这一串，再窄就折）。</summary>
    private const int SKILL_GRID_WIDTH = 350;

    /// <summary>中栏／右栏那两块（内力参数、被动、套装）的宽度。</summary>
    private const int SKILL_SIDE_WIDTH = 300;

    private void _rebuild_skills()
    {
        _clear(_skill_box);
        GodotObject st = current_state();
        CharacterSheet panel = sheet();
        if (st == null || panel == null || !panel.valid())
        {
            _skill_box.AddChild(_make_label("SkillEmpty", "没有会话状态"));
            return;
        }

        // 选中的那一招／那一部内功要是已经不在「已学」列表里了（换了人、这一局没学过），
        // 选中态一起清掉——不然详情栏会继续对着一招玩家再也看不到的东西算。
        if (_selected_skill.Length > 0)
        {
            var still_there = false;
            foreach (Variant row_value in panel.skill_rows())
            {
                still_there = still_there
                    || _s(row_value.AsGodotDictionary(), "skill_id") == _selected_skill;
            }

            if (!still_there)
            {
                _selected_skill = "";
            }
        }

        // 顶部队伍页签（清单 §二-6：「这一页最需要切人」）：名字 ＋ 等级 ＋ 已学招数，点头换人
        _skill_box.AddChild(_skill_member_row(st));

        Godot.Collections.Dictionary summary = panel.slot_summary();
        _skill_box.AddChild(_section(
            "SkillSection", "SkillHeader", "武学",
            $"招式 {_i(summary, "active_used")}/{_i(summary, "active_slots")}"
                + $"　内功 {_i(summary, "passive_used")}/{_i(summary, "passive_capacity")} 格"));

        var columns = new HBoxContainer { Name = "SkillColumns" };
        columns.AddThemeConstantOverride("separation", 12);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _skill_box.AddChild(columns);
        columns.AddChild(_skill_active_column(panel));
        columns.AddChild(_skill_mana_column(panel));
        columns.AddChild(_skill_info_column(panel, st));
    }

    /// <summary>
    /// 顶部队伍页签：每人写「名字　Lv5 · 6 招」，点头换人，当前那个用「▶」标出来。
    ///
    /// 「几招」＝这个人**已学**的武学条数（`skill_rows()` 数出来的，与下面两张表同一份数据）。
    /// 用 Flow 容器而不是 HBox：人多／名字长的时候**换行**，比被屏幕右边裁掉强。
    /// </summary>
    private Control _skill_member_row(GodotObject st)
    {
        var row = new HFlowContainer { Name = "SkillMembers" };
        row.AddThemeConstantOverride("h_separation", 6);
        row.AddThemeConstantOverride("v_separation", 4);
        string[] ids = st.Get("char_ids").AsStringArray();
        foreach (string char_id in ids)
        {
            string who = char_id;
            bool selected = who == _selected_char;
            CharacterSheet other = CharacterSheet.create(Db, st, who);
            int learned = other != null ? other.skill_rows().Count : 0;
            row.AddChild(_make_button(
                $"SkillMemberButton{who}",
                $"{(selected ? "▶ " : "")}{st.Call("char_name", Db, who).AsString()}"
                    + $"　Lv{st.Call("level_of", who).AsInt32()} · {learned} 招",
                () => select_char(who)));
        }

        // 空位也占一格（照角色页／装备页的口径）；置灰——它是缺口提示，不是按钮
        for (int empty_index = ids.Length; empty_index < _max_party(); empty_index++)
        {
            int index = empty_index;
            row.AddChild(_make_button($"SkillMemberEmpty{index}", "空位", () => { }, true));
        }

        return row;
    }

    /// <summary>
    /// 左栏：招式格区（已学的招式）＋ 选中那一招的详情（清单 §二-6：消耗内力／伤害倍率／段数；
    /// 附加效果写成一句白话）。
    ///
    /// 占用数按清单的原话写「`6 / 12 槽（悟性 14）`」——**悟性要写出来**，玩家才知道这个上限是哪来的
    /// （悟性管招式槽，见 `attribute_def` 那一行）。
    /// </summary>
    private VBoxContainer _skill_active_column(CharacterSheet panel)
    {
        var col = new VBoxContainer
        {
            Name = "SkillGridColumn",
            CustomMinimumSize = new Vector2(SKILL_GRID_WIDTH, 0),
        };
        col.AddThemeConstantOverride("separation", 6);
        Godot.Collections.Dictionary summary = panel.slot_summary();
        col.AddChild(_section(
            "ActiveSection", "ActiveHeader", "招式格",
            $"{_i(summary, "active_used")} / {_i(summary, "active_slots")} 槽"
                + $"（悟性 {_i(panel.total_attrs(), "wu")}）"));
        foreach (Variant row_value in panel.skill_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            if (_s(row, "kind") == "active")
            {
                col.AddChild(_skill_cell(row));
            }
        }

        col.AddChild(_skill_detail(panel));
        return col;
    }

    /// <summary>
    /// 中栏：心法占格区（已学的内功）＋ 内力参数（清单 §二-6「内力上限／战斗初始气 ＋ 每回合回气」）。
    ///
    /// **气没有「起始值」这一列**：`BattleActor.refill()` 造人时就把气回满，所以这里如实写「开场即满」。
    /// 编一个数字出来的话，公式一改面板就开始说谎，而且那是第二处算法。
    /// 剩的空位写「剩 N 格」（清单原话）——空位是缺口提示，不是按钮。
    /// </summary>
    private VBoxContainer _skill_mana_column(CharacterSheet panel)
    {
        var col = new VBoxContainer
        {
            Name = "SkillPassiveColumn",
            CustomMinimumSize = new Vector2(SKILL_SIDE_WIDTH, 0),
        };
        col.AddThemeConstantOverride("separation", 6);
        Godot.Collections.Dictionary summary = panel.slot_summary();
        int used = _i(summary, "passive_used");
        int capacity = _i(summary, "passive_capacity");
        int gen = _i(panel.total_attrs(), "gen");
        col.AddChild(_section(
            "PassiveSection", "PassiveHeader", "心法占格",
            $"{used} / {capacity} 格（根骨 {gen}）"));
        foreach (Variant row_value in panel.skill_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            if (_s(row, "kind") != "active")
            {
                col.AddChild(_skill_cell(row));
            }
        }

        col.AddChild(capacity > used
            ? _make_label($"PassiveFree{capacity - used}", $"剩 {capacity - used} 格")
            : _make_label("PassiveFull", $"心法格已满（{used} / {capacity}）"));

        col.AddChild(_section("ManaSection", "ManaHeader", "内力"));
        col.AddChild(_kv_row(
            "ManaMaxRow", "内力上限",
            panel.format_stat_value("qi_max", panel.stat_value("qi_max")), $"根骨 {gen}"));
        col.AddChild(_kv_row(
            "ManaInitRow", "战斗初始气", "开场即满",
            $"每回合回复 {panel.format_stat_value("qi_regen", panel.stat_value("qi_regen"))}"));
        return col;
    }

    /// <summary>
    /// 一格武学：图标 ＋「★ 名字」那一按（点它＝选中，详情跟着换）＋ 一行数值摘要 ＋ 装／卸。
    ///
    /// 那一按上的颜色照 `skill_star_def.color` 走（星级色），选中那一格换朱红——一眼看得出在看哪一招。
    /// 数值摘要那一行是**旧排版留下来的**（用例按 `Skill&lt;id&gt;` 读它），措辞一字没改。
    /// </summary>
    private Control _skill_cell(Godot.Collections.Dictionary row)
    {
        string skill_id = _s(row, "skill_id");
        bool is_active = _s(row, "kind") == "active";
        bool equipped = _b(row, "equipped");
        string equip_error = _s(row, "equip_error");
        var cell = new VBoxContainer
        {
            Name = is_active ? $"SkillRow{skill_id}" : $"PassiveRow{skill_id}",
        };
        cell.AddThemeConstantOverride("separation", 2);

        var top = new HBoxContainer { Name = $"SkillTop{skill_id}" };
        top.AddThemeConstantOverride("separation", 6);
        // 武学图标（15 §六：`assets/icons/skill/<skill_id>.png`）——**有图才摆**，没图的行照旧从文字开始
        TextureRect icon = IconPaths.make_icon(IconPaths.skill(skill_id), $"SkillIcon{skill_id}");
        if (icon != null)
        {
            top.AddChild(icon);
        }

        // 点一格只改选中态（真装／卸是右边那枚——玩家可以先看详情再决定）
        Button pick = _make_button(
            $"SkillCell{skill_id}",
            $"{_s(row, "star_name")}★　{_s(row, "name")}",
            () => select_skill(skill_id));
        pick.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // 熟练度倍率挪进悬浮说明：那一行在 350px 的格里放不下「（×1.05）」，折行之后很难看，
        // 而「练了有没有用」这一问在这一行里已经由「熟练 12/10」答了一半。
        pick.TooltipText = $"{_s(row, "desc")}　熟练 {_i(row, "mastery")}/{_i(row, "mastery_max")}"
            + $"（本招倍率 ×{_f2(_f(row, "mastery_multiplier"))}）";
        pick.AddThemeColorOverride(
            "font_color",
            UiKit.color(_selected_skill == skill_id ? "cinnabar_deep" : "ink"));
        top.AddChild(pick);
        top.AddChild(_make_button(
            $"LoadoutButton{skill_id}",
            equipped ? "卸下" : "装配",
            () => { toggle_skill(skill_id); },
            !equipped && equip_error.Length > 0));
        cell.AddChild(top);
        Label summary = _make_label($"Skill{skill_id}", _skill_summary_text(row));
        summary.AddThemeColorOverride("font_color", new Color(_s(row, "star_color", "#FFFFFF")));
        cell.AddChild(summary);
        return cell;
    }

    /// <summary>一格下面那行摘要（措辞与旧排版一字不差——用例与自检按它读「增益招式」「暂未生效」）。</summary>
    private string _skill_summary_text(Godot.Collections.Dictionary row)
    {
        bool is_active = _s(row, "kind") == "active";
        string detail;
        if (is_active)
        {
            detail = $"倍率 {_f2(_f(row, "power_ratio"))}　内力 {_i(row, "qi_cost")}"
                + $"　段数 {_i(row, "hit_count")}";
            string status_note = _s(row, "status_note");
            if (status_note.Length > 0)
            {
                detail += $"　{status_note}";
            }

            // 效果没配的招式（战斗里没有按钮、用不出来）如实写一句：玩家能装它占一个招式槽，
            // 不写清楚他就只会觉得「点了没反应」。
            string battle_note = _s(row, "battle_note");
            if (battle_note.Length > 0)
            {
                detail += $"　—　{battle_note}";
            }
        }
        else
        {
            detail = _s(row, "bonus_summary");
            // 内功填了「特殊效果 id」但设计还没给效果表——如实写一句，不显示 id（文案审计盯着）
            if (_b(row, "special_effect_pending"))
            {
                detail += "　—　带一条特殊效果，暂未生效（设计未给效果表）";
            }
        }

        string req_reason = _s(row, "learn_req_reason");
        if (req_reason.Length > 0)
        {
            detail += $"　门槛 {req_reason}";
        }

        string kind_text = is_active ? "招式" : $"内功 {_i(row, "slot_cost")} 格";
        // 装没装不用写在这儿——右边那枚按钮就写着「卸下／装配」；**装不上的原因要写**，
        // 玩家才知道为什么那一枚点不动。
        string suffix = _b(row, "equipped") ? "" : $"　{_s(row, "equip_error")}";
        return $"{kind_text}　熟练 {_i(row, "mastery")}/{_i(row, "mastery_max")}　{detail}{suffix}";
    }

    /// <summary>
    /// 选中那一招／那一部内功的详情（清单 §二-6：「只读（数值从表里来）」）。
    /// 没选中时写一句指路，不摆一排空行。
    /// </summary>
    private Control _skill_detail(CharacterSheet panel)
    {
        var col = new VBoxContainer { Name = "SkillDetailColumn" };
        col.AddThemeConstantOverride("separation", 4);
        Godot.Collections.Dictionary row = _selected_skill_row(panel);
        if (row.Count == 0)
        {
            col.AddChild(_section("SkillDetailSection", "SkillDetailHeader", "招式详情", "点上面一格"));
            col.AddChild(_make_label(
                "SkillDetailHint",
                "点上面任意一格，这里写出它的消耗内力／伤害倍率／段数与附加效果。"));
            return col;
        }

        bool is_active = _s(row, "kind") == "active";
        string skill_id = _s(row, "skill_id");
        string equipped = _b(row, "equipped") ? " · ▶ 已装配" : "";
        col.AddChild(_section(
            "SkillDetailSection", "SkillDetailHeader", _s(row, "name"),
            $"{_s(row, "star_name")}★ · {(is_active ? "招式" : $"内功 {_i(row, "slot_cost")} 格")}"
                + equipped));
        if (is_active)
        {
            col.AddChild(_kv_row("SkillDetailCostRow", "消耗内力", _i(row, "qi_cost").ToString(), ""));
            col.AddChild(_kv_row("SkillDetailPowerRow", "伤害倍率", _percent(_f(row, "power_ratio")), ""));
            col.AddChild(_kv_row("SkillDetailHitRow", "招式段数", _i(row, "hit_count").ToString(), ""));
        }
        else
        {
            col.AddChild(_kv_row("SkillDetailSlotRow", "占用格数", $"{_i(row, "slot_cost")} 格", ""));
            col.AddChild(_kv_row("SkillDetailBonusRow", "常驻加成", _s(row, "bonus_summary"), ""));
        }

        // 附加效果写成一句白话（`status_note` 就是那一句——界面不再转译一次，id 也不上界面）
        string status_note = _s(row, "status_note");
        Label status = _make_label(
            "SkillDetailStatusRow", status_note.Length > 0 ? status_note : "无附加效果");
        status.AddThemeColorOverride(
            "font_color", UiKit.color(status_note.Length > 0 ? "cinnabar_deep" : "ink_off"));
        col.AddChild(status);

        string req_reason = _s(row, "learn_req_reason");
        if (req_reason.Length > 0)
        {
            col.AddChild(_kv_row("SkillDetailReqRow", "修习门槛", req_reason, ""));
        }

        col.AddChild(_make_label("SkillDetailDescRow", _s(row, "desc")));

        // 「装备到快捷键」／「遗忘」（清单那一行）：装备那一枚**装不上就置灰并写原因**，原因就是
        // `can_equip` 给的那一句（槽满了会写「招式槽已满（12/12）」）——全文摆在悬浮说明里，行上放不下。
        // ★5 绝学「遗忘」要二次确认那条在清单里是 ⏳（口径待策划定），先按普通卸下走。
        string equip_error = _s(row, "equip_error");
        bool already = _b(row, "equipped");
        var actions = new HBoxContainer { Name = "SkillActions" };
        actions.AddThemeConstantOverride("separation", 8);
        Button equip = _make_button(
            $"SkillEquipButton{skill_id}",
            already ? "已装备" : (is_active ? "装备到快捷键" : "装配"),
            () => { toggle_skill(skill_id); },
            already || equip_error.Length > 0);
        if (equip_error.Length > 0)
        {
            equip.TooltipText = equip_error;
        }
        else if (already)
        {
            equip.TooltipText = "这一部已经装配着了";
        }

        actions.AddChild(equip);
        Button forget = _make_button(
            $"SkillForgetButton{skill_id}", "遗忘", () => { toggle_skill(skill_id); }, !already);
        if (!already)
        {
            forget.TooltipText = "这一部没装着，没什么可遗忘的";
        }

        actions.AddChild(forget);
        col.AddChild(actions);
        return col;
    }

    /// <summary>选中那一部（`_selected_skill` 对应的行）；没选、或选中的那部已经不在列表里时给空字典。</summary>
    private Godot.Collections.Dictionary _selected_skill_row(CharacterSheet panel)
    {
        if (_selected_skill.Length == 0)
        {
            return new Godot.Collections.Dictionary();
        }

        foreach (Variant row_value in panel.skill_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            if (_s(row, "skill_id") == _selected_skill)
            {
                return row;
            }
        }

        return new Godot.Collections.Dictionary();
    }

    /// <summary>
    /// 右栏：被动（清单 §二-6「每行：名 ＋ 来源 ＋ 效果」）＋ 套装效果（进度点按达成／未达分色）。
    /// </summary>
    private VBoxContainer _skill_info_column(CharacterSheet panel, GodotObject st)
    {
        var col = new VBoxContainer
        {
            Name = "SkillInfoColumn",
            CustomMinimumSize = new Vector2(SKILL_SIDE_WIDTH, 0),
        };
        col.AddThemeConstantOverride("separation", 6);
        System.Collections.Generic.List<Control> passives = _skill_passive_list_rows(panel, st);
        col.AddChild(_section(
            "PassiveListSection", "PassiveListHeader", "被动",
            $"{panel.display_name()} · {passives.Count} 条"));
        if (passives.Count == 0)
        {
            col.AddChild(_make_label(
                "PassiveListNone", "还没有生效中的被动（装内功、凑套装、走剧情都会加）。"));
        }

        foreach (Control row in passives)
        {
            col.AddChild(row);
        }

        col.AddChild(_section(
            "SkillSetSection", "SkillSetHeader", "套装效果",
            $"{SetService.create(Db).all_defs().Count} 套 · 随角色切"));
        foreach (Control block in _skill_set_blocks(panel, st))
        {
            col.AddChild(block);
        }

        return col;
    }

    /// <summary>
    /// 被动段的三组：**装着的内功**（常驻加成）＋ **已达成的套装档位** ＋ **抉择的永久增益**。
    /// 来源那一段是玩家最想知道的（这一条加成是哪来的），三组都要写清楚；名字一律照表读——
    /// 套装档位的名字走 `buff_def.name_cn`，界面不自己编一个。
    /// </summary>
    private System.Collections.Generic.List<Control> _skill_passive_list_rows(
        CharacterSheet panel, GodotObject st)
    {
        var rows = new System.Collections.Generic.List<Control>();
        foreach (Variant row_value in panel.skill_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            if (_s(row, "kind") == "active" || !_b(row, "equipped"))
            {
                continue;
            }

            string skill_id = _s(row, "skill_id");
            string effect = _s(row, "bonus_summary");
            if (_b(row, "special_effect_pending"))
            {
                effect += "（另有一条特殊效果，暂未生效）";
            }

            rows.Add(_passive_row(
                $"PassiveListRow{skill_id}", _s(row, "name"),
                $"内功 · 占 {_i(row, "slot_cost")} 格", effect));
        }

        Inventory inventory = st.Get("inventory").As<Inventory>();
        SetService sets = SetService.create(Db);
        foreach (Variant def_value in sets.all_defs())
        {
            var def = def_value.As<SetDefRow>();
            int count = sets.count_for(def.set_id, panel.char_id, inventory, st);
            foreach (Variant tier_value in sets.bonuses_of(def.set_id))
            {
                var tier = tier_value.As<SetBonusRow>();
                if (tier.required_count > count)
                {
                    continue;
                }

                var buff = Db.get_row("buff_def", tier.buff_id) as BuffDefRow;
                string name = buff != null && buff.name_cn.Length > 0 ? buff.name_cn : def.name_cn;
                rows.Add(_passive_row(
                    $"PassiveListRow{def.set_id}_{tier.required_count}", name,
                    $"{def.name_cn} · {_set_kind_label(def.set_kind)}满"
                        + $" {tier.required_count} {_set_kind_unit(def.set_kind)}",
                    tier.desc));
            }
        }

        // 抉择的永久增益（设计 20 §八：跟存档、跨章保留、界面要能看见）
        string[] permanent = StoryService.bonus_labels(Db, st, panel.char_id);
        for (int index = 0; index < permanent.Length; index++)
        {
            rows.Add(_passive_row(
                $"PassiveListRowPermanent{index}", "永久增益", "账册抉择", permanent[index]));
        }

        return rows;
    }

    /// <summary>被动段的一行：名 ＋ 来源（次要色）＋ 效果（信息色，占满剩下的地方）。</summary>
    private static HBoxContainer _passive_row(string node_name, string name, string source, string effect)
    {
        var row = new HBoxContainer { Name = node_name };
        row.AddThemeConstantOverride("separation", 8);
        Label name_label = new Label { Name = $"{node_name}Name", Text = name };
        name_label.AddThemeColorOverride("font_color", UiKit.color("ink"));
        row.AddChild(name_label);
        Label source_label = new Label { Name = $"{node_name}Source", Text = source };
        source_label.AddThemeColorOverride("font_color", UiKit.color("ink_dim"));
        row.AddChild(source_label);
        Label effect_label = new Label { Name = $"{node_name}Effect", Text = effect };
        effect_label.AddThemeColorOverride("font_color", UiKit.color("jade"));
        effect_label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(effect_label);
        return row;
    }

    /// <summary>
    /// 套装效果：一个套装一块——名字 ＋ **进度点**（达成的点走信息色、没到的走次要色）＋
    /// 「`2 / 5 · 招式`」，下面把每一档逐条写出来，达成的写「已激活」、没到的写「未达」。
    ///
    /// 进度走 `SetService.count_for`（与真判定同一处），档位文案照 `set_bonus.desc` 读。
    /// 分母取**最高那一档**（`2 / 5`＝凑到第五招就全开）：写「到下一档还差几」得在界面里再推一遍公式，
    /// 那正是「公式不进界面」这条要躲的事。
    /// </summary>
    private System.Collections.Generic.List<Control> _skill_set_blocks(
        CharacterSheet panel, GodotObject st)
    {
        var blocks = new System.Collections.Generic.List<Control>();
        Inventory inventory = st.Get("inventory").As<Inventory>();
        SetService sets = SetService.create(Db);
        foreach (Variant def_value in sets.all_defs())
        {
            var def = def_value.As<SetDefRow>();
            Godot.Collections.Array tiers = sets.bonuses_of(def.set_id);
            if (tiers.Count == 0)
            {
                continue;
            }

            int count = sets.count_for(def.set_id, panel.char_id, inventory, st);
            var top = 0;
            foreach (Variant tier_value in tiers)
            {
                top = System.Math.Max(top, tier_value.As<SetBonusRow>().required_count);
            }

            var block = new VBoxContainer { Name = $"SkillSetBlock{def.set_id}" };
            block.AddThemeConstantOverride("separation", 2);
            var head = new HBoxContainer { Name = $"SkillSetHead{def.set_id}" };
            head.AddThemeConstantOverride("separation", 6);
            Label name_label = new Label { Name = $"SkillSetName{def.set_id}", Text = def.name_cn };
            name_label.AddThemeColorOverride("font_color", UiKit.color("ink"));
            head.AddChild(name_label);
            var dots = new HBoxContainer { Name = $"SkillSetDots{def.set_id}" };
            dots.AddThemeConstantOverride("separation", 3);
            foreach (Variant tier_value in tiers)
            {
                var tier = tier_value.As<SetBonusRow>();
                dots.AddChild(new ColorRect
                {
                    Name = $"SkillSetDot{def.set_id}_{tier.required_count}",
                    Color = UiKit.color(tier.required_count <= count ? "jade" : "ink_dim"),
                    CustomMinimumSize = new Vector2(8, 8),
                });
            }

            head.AddChild(dots);
            Label count_label = new Label
            {
                Name = $"SkillSetCount{def.set_id}",
                Text = $"{count} / {top} · {_set_kind_label(def.set_kind)}",
                HorizontalAlignment = HorizontalAlignment.Right,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            count_label.AddThemeColorOverride("font_color", UiKit.color("ink_dim"));
            head.AddChild(count_label);
            block.AddChild(head);
            foreach (Variant tier_value in tiers)
            {
                var tier = tier_value.As<SetBonusRow>();
                bool reached = tier.required_count <= count;
                var line = new HBoxContainer { Name = $"SkillSetRow{def.set_id}_{tier.required_count}" };
                line.AddThemeConstantOverride("separation", 8);
                Label desc = _make_label($"SkillSetLine{def.set_id}_{tier.required_count}", tier.desc);
                desc.AddThemeColorOverride("font_color", UiKit.color(reached ? "ink" : "ink_off"));
                desc.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                line.AddChild(desc);
                Label state = new Label
                {
                    Name = $"SkillSetState{def.set_id}_{tier.required_count}",
                    Text = reached ? "已激活" : "未达",
                };
                state.AddThemeColorOverride("font_color", UiKit.color(reached ? "jade" : "ink_dim"));
                line.AddChild(state);
                block.AddChild(line);
            }

            blocks.Add(block);
        }

        if (blocks.Count == 0)
        {
            blocks.Add(_make_label("SkillSetNone", "配表里还没有套装。"));
        }

        return blocks;
    }

    /// <summary>套装口径的中文名（`set_def.set_kind` 那三个值——界面不许出现这几个英文 id）。</summary>
    private static string _set_kind_label(string kind) => kind switch
    {
        SetService.KIND_EQUIP => "装备",
        SetService.KIND_ACTIVE => "招式",
        SetService.KIND_PASSIVE => "内功",
        _ => "",
    };

    /// <summary>那一档要凑几个什么（装备论件、招式论招、内功论格）。</summary>
    private static string _set_kind_unit(string kind) => kind switch
    {
        SetService.KIND_EQUIP => "件",
        SetService.KIND_ACTIVE => "招",
        SetService.KIND_PASSIVE => "格",
        _ => "份",
    };

    /// <summary>倍率写成百分比（1.4 →「140%」）——小数点后最多一位，跟示意图上的写法一致。</summary>
    private static string _percent(double ratio) =>
        (ratio * 100.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
