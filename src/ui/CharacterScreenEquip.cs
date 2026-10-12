using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// **装备页**（UI 清单 §二-5）——从 `CharacterScreenPages.cs` 拆出来的 partial（2026-10-12）。
///
/// 拆的原因：装备页要「七槽视图 ＋ 换上之后 ＋ 详情（属性／词条／套装）＋ 可换」四块，
/// 与另外三个页签的常规排版摆在一个文件里会顶破 800 行硬上限（那份要守 `docs/dev/代码拆分清单.md`）。
///
/// 这一份只干一件事：按 `CharacterSheet`（装备／词条）、`SetService`（套装进度）给的数据摆控件，
/// 一套数值只算一遍（「换上之后」与候选行的差值都走 `CharacterSheet.preview_swap_stats()`）。
/// 名字（`SlotButtonweapon_0`／`EquipButton&lt;实例 id&gt;` 这种）就是用例与自检的接口，别改。
/// </summary>
public partial class CharacterScreen
{
    /// <summary>「换上之后」最多写几行（清单 §二-5 画的是三行；变化多时取变化最大的那几项）。</summary>
    private const int SWAP_ROWS = 3;

    private void _rebuild_equipment()
    {
        _clear(_equip_box);
        GodotObject st = current_state();
        CharacterSheet panel = sheet();
        if (st == null || panel == null)
        {
            _equip_box.AddChild(_make_label("Empty", "没有会话状态"));
            return;
        }

        // 选中的那件候选要是已经不在背包里（刚穿上、卖掉了、换了人），选中态一起清掉——
        // 不然详情／「换上之后」会继续对着一件玩家再也看不到的东西算。
        if (_selected_candidate.Length > 0)
        {
            var still_there = false;
            foreach (Variant row_value in panel.equipped_rows())
            {
                still_there = still_there || _s(row_value.AsGodotDictionary(), "instance_id") == _selected_candidate;
            }

            foreach (Variant candidate_value in _candidate_rows(_target_slot()))
            {
                still_there = still_there
                    || _s(candidate_value.AsGodotDictionary(), "instance_id") == _selected_candidate;
            }

            if (!still_there)
            {
                _selected_candidate = "";
            }
        }

        // 三栏（清单 §二-5 那张表是按「左栏／右栏」写的）：左＝七个槽位 ＋ 换上之后｜
        // 中＝选中那件装备的详情｜右＝可换。心法不是装备槽，走武学页的占格。
        var columns = new HBoxContainer { Name = "EquipColumns" };
        columns.AddThemeConstantOverride("separation", 12);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _equip_box.AddChild(columns);
        columns.AddChild(_equip_slot_column(panel));
        columns.AddChild(_equip_detail_column(panel, st));
        columns.AddChild(_equip_candidate_column(panel));
    }

    /// <summary>
    /// 左栏：七槽视图（`equip_slot_def` 那七行，戒指两格 → 八行）＋「换上之后」那一组数值。
    ///
    /// 空槽也占位：槽名照表读，写「空 · 可装」，占位框走 `icons/slot/slot_&lt;slot_id&gt;.png`
    /// （清单 §二-5 明说这一族**不在** `icons/equip/` 里）——**有图才摆**。
    /// </summary>
    private VBoxContainer _equip_slot_column(CharacterSheet panel)
    {
        var col = new VBoxContainer
        {
            Name = "EquipSlotsColumn",
            CustomMinimumSize = new Vector2(340, 0),
        };
        col.AddThemeConstantOverride("separation", 6);
        col.AddChild(_section("SlotSection", "SlotHeader", "七个槽位", "点一行选要换的槽位"));
        foreach (Variant row_value in panel.equipped_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            string slot_id = _s(row, "slot_id");
            int index = _i(row, "index");
            bool empty = _b(row, "empty");
            EquipBaseRow base_row = _base_of_row(row);
            int capacity = ((EquipSlotDefRow)Db.get_row("equip_slot_def", slot_id))?.max_equip ?? 1;
            string slot_label = capacity <= 1 ? _s(row, "slot_name") : $"{_s(row, "slot_name")}{index + 1}";
            TextureRect icon = IconPaths.make_icon(
                empty ? IconPaths.slot(slot_id) : equip_icon_path(base_row != null ? base_row.equip_id : ""),
                $"EquipSlotIcon{slot_id}_{index}");
            // 一行两截：名字 ＋「槽名 · 品阶」。**属性摘要不摆进来**——这一栏到手约 300px，
            // 串上摘要（「体+1、外功防御+4、气血上限+…」）会被 Button 裁掉半句（真窗口截图看得很清楚）；
            // 摘要留给悬浮说明与中间那栏的详情（`EquipAttr*`／`EquipBonus*` 逐条摆着）。
            string name_text = empty ? "空" : _s(row, "name");
            string meta_text = empty
                ? $"{slot_label} · 可装"
                : $"{slot_label} · {panel.rarity_name(base_row.rarity)}";
            string tooltip = empty
                ? $"{slot_label}：空着，点它看看背包里有什么能装"
                : $"{name_text}　{meta_text}　{_s(row, "summary")}";
            Button button = _make_button(
                $"SlotButton{slot_id}_{index}",
                $"{name_text}　{meta_text}",
                () => press_slot(slot_id, index));
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.TooltipText = tooltip;
            if (!empty && base_row != null)
            {
                button.AddThemeColorOverride("font_color", new Color(panel.rarity_color(base_row.rarity)));
            }

            var line = new HBoxContainer { Name = $"SlotRow{slot_id}_{index}" };
            line.AddThemeConstantOverride("separation", 6);
            if (icon != null)
            {
                line.AddChild(icon);
            }

            line.AddChild(button);
            col.AddChild(line);
        }

        col.AddChild(_section("SwapSection", "SwapHeader", "换上之后", "选中候选后即时重算"));
        foreach (Control line in _swap_preview_lines(panel))
        {
            col.AddChild(line);
        }

        return col;
    }

    /// <summary>
    /// 「换上之后」那一组：把**选中的候选**换上去重算一遍（`CharacterSheet.preview_swap_stats`，
    /// 与面板同一个 `AttributeCalculator`），只写真的会变的派生数值（最多 `SWAP_ROWS` 行，
    /// 按变化量绝对值从大到小）。一个都不变就直说「属性不变」——**不编数字**。
    /// </summary>
    private System.Collections.Generic.List<Control> _swap_preview_lines(CharacterSheet panel)
    {
        var lines = new System.Collections.Generic.List<Control>();
        if (_selected_slot.Length == 0)
        {
            lines.Add(_make_label("SwapHint", "先点上面一个槽位，再看背包里能换的装备。"));
            return lines;
        }

        if (_selected_candidate.Length == 0)
        {
            lines.Add(_make_label("SwapHint", "在右边点一件候选，这里写出换上之后会变成多少。"));
            return lines;
        }

        Godot.Collections.Dictionary after = panel.preview_swap_stats(
            _target_slot(), _selected_slot_index(), _selected_candidate);
        if (after.Count == 0)
        {
            lines.Add(_make_label("SwapHint", "这件现在装不上——先看右边那行的置灰原因。"));
            return lines;
        }

        var deltas = _stat_deltas(panel, after);
        if (deltas.Count == 0)
        {
            lines.Add(_make_label("SwapNone", "换上之后属性不变。"));
            return lines;
        }

        var shown = 0;
        foreach (var (stat_id, stat_name, before, now_value) in deltas)
        {
            if (shown >= SWAP_ROWS)
            {
                break;
            }

            double delta = now_value - before;
            var line = new HBoxContainer { Name = $"SwapRow{stat_id}" };
            line.AddThemeConstantOverride("separation", 8);
            Label label = _make_label($"SwapLabel{stat_id}", stat_name);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(label);
            line.AddChild(_cell_label(
                $"SwapValue{stat_id}",
                $"{panel.format_stat_value(stat_id, before)} → {panel.format_stat_value(stat_id, now_value)}",
                "ink", 76, HorizontalAlignment.Right));
            line.AddChild(_cell_label(
                $"SwapDelta{stat_id}",
                $"{(delta > 0 ? "+" : "−")}{panel.format_stat_value(stat_id, System.Math.Abs(delta))}",
                delta > 0 ? "jade" : "cinnabar_deep", 56, HorizontalAlignment.Right));
            lines.Add(line);
            shown += 1;
        }

        return lines;
    }

    /// <summary>
    /// 当前数值 → 预演数值的差：只留真的变了的那几项，按变化量绝对值从大到小。
    /// 基准只取一次 `stats()`（每个派生数值各调一次 `stat_value()` 会把整条公式算二十九遍）。
    /// </summary>
    private System.Collections.Generic.List<(string stat_id, string stat_name, double before, double after)>
        _stat_deltas(CharacterSheet panel, Godot.Collections.Dictionary after)
    {
        Godot.Collections.Dictionary before = panel.stats();
        var out_list = new System.Collections.Generic.List<(string, string, double, double)>();
        foreach (Variant stat_value in Db.rows("stat_def"))
        {
            string stat_id = stat_value.As<StatDefRow>().stat_id;
            double was = before.ContainsKey(stat_id) ? before[stat_id].AsDouble() : 0.0;
            double now = after.ContainsKey(stat_id) ? after[stat_id].AsDouble() : 0.0;
            if (System.Math.Abs(now - was) > 0.0000001)
            {
                out_list.Add((stat_id, panel.stat_name(stat_id), was, now));
            }
        }

        out_list.Sort((a, b) =>
            System.Math.Abs(b.Item4 - b.Item3).CompareTo(System.Math.Abs(a.Item4 - a.Item3)));
        return out_list;
    }

    /// <summary>候选那一行的差值：「+6 外功攻击」这种最显著的一项——**真重算一遍**，不是拿表里的加成猜。</summary>
    private string _swap_delta_text(CharacterSheet panel, string slot_id, int index, string instance_id)
    {
        Godot.Collections.Dictionary after = panel.preview_swap_stats(slot_id, index, instance_id);
        if (after.Count == 0)
        {
            return "装不上";
        }

        var deltas = _stat_deltas(panel, after);
        if (deltas.Count == 0)
        {
            return "属性不变";
        }

        var (stat_id, stat_name, before, now_value) = deltas[0];
        double delta = now_value - before;
        return $"{(delta > 0 ? "+" : "−")}{panel.format_stat_value(stat_id, System.Math.Abs(delta))} {stat_name}";
    }

    /// <summary>
    /// 中栏：选中那件装备的详情——品阶／装备需求／说明 ＋ 属性行 ＋ 词条区 ＋ 套装归属 ＋ 动作钮。
    /// 「选中那件」＝右栏点过的候选优先，没有就是左栏选中槽位里穿着的那件。
    /// </summary>
    private VBoxContainer _equip_detail_column(CharacterSheet panel, GodotObject st)
    {
        var col = new VBoxContainer { Name = "EquipDetailColumn" };
        col.AddThemeConstantOverride("separation", 6);
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Godot.Collections.Dictionary subject = _detail_subject(panel);
        if (subject.Count == 0)
        {
            col.AddChild(_section("EquipDetailSection", "EquipDetailHeader", "装备详情"));
            col.AddChild(_make_label("EquipDetailHint", "点左边一个槽位、或在右边点一件候选，这里写出它的详情。"));
            return col;
        }

        string instance_id = _s(subject, "instance_id");
        EquipBaseRow base_row = _base_of_row(subject);
        string weapon_name = base_row != null ? panel.weapon_type_name_of(base_row) : "";
        col.AddChild(_section(
            "EquipDetailSection", "EquipDetailHeader", _s(subject, "name"),
            // 副字只写武器类型：品阶已经在下面 `EquipRarityRow` 单占一行，两处都写就成了同一句话说两遍
            weapon_name));
        if (base_row != null)
        {
            col.AddChild(_kv_row("EquipRarityRow", "品阶", panel.rarity_name(base_row.rarity), ""));
        }

        // 装备需求：表里只有等级门槛（`equip_base.level_req`）＋武器类型那一关，
        // **没有属性门槛这一列**（示意图画的是「力 8 已满足」，那条口径表里没有）
        int level_req = base_row != null ? base_row.level_req : _i(subject, "level_req");
        col.AddChild(_kv_row(
            "EquipReqRow", "装备需求", $"Lv {level_req}",
            level_req <= panel.level ? "已满足" : $"差 {level_req - panel.level} 级"));
        if (base_row != null && base_row.desc.Length > 0)
        {
            col.AddChild(_kv_row("EquipDescRow", "说明", base_row.desc, ""));
        }

        col.AddChild(_section("EquipStatSection", "EquipStatHeader", "属性"));
        foreach (Control row in _equip_stat_rows(panel, base_row))
        {
            col.AddChild(row);
        }

        // 词条区（清单 §二-5：「词条文字 ＋ 凡品词条 · 可洗（Q6 待定）」，洗词条那枚先摆着但置灰）
        Inventory inventory = st.Get("inventory").As<Inventory>();
        int affix_count = instance_id.Length > 0 && inventory != null
            ? inventory.affixes_of(instance_id).Count
            : 0;
        string affix_text = _s(subject, "affix_summary");
        col.AddChild(_section("AffixSection", "AffixHeader", "词条", $"{affix_count} 条"));
        col.AddChild(_make_label("AffixText", affix_text.Length > 0 ? affix_text : "无词条"));
        Button reroll = _make_button("RerollAffixButton", "洗词条", () => { }, true);
        reroll.TooltipText = "Q6 待定：洗词条的规则还没定，先摆着";
        col.AddChild(reroll);
        col.AddChild(_make_label(
            "AffixHint",
            $"{(base_row != null ? panel.rarity_name(base_row.rarity) : "")}词条 · 可洗（Q6 待定）"));

        // 套装归属（设计 08）：这件属于哪个装备套装、进度到哪一档
        col.AddChild(_section("SetSection", "SetHeader", "套装归属"));
        foreach (Control row in _set_rows(panel, st, base_row))
        {
            col.AddChild(row);
        }

        var actions = new HBoxContainer { Name = "EquipActions" };
        actions.AddThemeConstantOverride("separation", 8);
        bool is_candidate = _selected_candidate.Length > 0 && instance_id == _selected_candidate;
        if (is_candidate)
        {
            actions.AddChild(_make_button(
                $"EquipConfirmButton{instance_id}", "换上", () => { equip_instance(instance_id); }));
        }

        string slot_id = _s(subject, "slot_id");
        int slot_index = _i(subject, "index");
        if (!_b(subject, "empty") && slot_id.Length > 0)
        {
            actions.AddChild(_make_button(
                $"UnequipButton{slot_id}_{slot_index}", "卸下", () => { unequip(slot_id, slot_index); }));
        }

        col.AddChild(actions);
        return col;
    }

    /// <summary>详情区对着的那一件：右栏点过的候选优先，否则是左栏选中槽位里穿着的那件。</summary>
    private Godot.Collections.Dictionary _detail_subject(CharacterSheet panel)
    {
        if (_selected_candidate.Length > 0)
        {
            foreach (Variant row_value in panel.equipped_rows())
            {
                Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
                if (_s(row, "instance_id") == _selected_candidate)
                {
                    return row;
                }
            }

            foreach (Variant candidate_value in _candidate_rows(_target_slot()))
            {
                Godot.Collections.Dictionary candidate = candidate_value.AsGodotDictionary();
                if (_s(candidate, "instance_id") == _selected_candidate)
                {
                    return candidate;
                }
            }

            return new Godot.Collections.Dictionary();
        }

        if (_selected_slot.Length == 0)
        {
            return new Godot.Collections.Dictionary();
        }

        foreach (Variant row_value in panel.equipped_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            if ($"{_s(row, "slot_id")}|{_i(row, "index")}" == _selected_slot)
            {
                return row;
            }
        }

        return new Godot.Collections.Dictionary();
    }

    /// <summary>选中槽位的 id（没选就空串）。</summary>
    private string _target_slot() => _selected_slot.Length > 0 ? _selected_slot.Split("|")[0] : "";

    /// <summary>选中槽位的中文名（照表读，别在界面里再抄一份中文）。</summary>
    private string _target_slot_name()
    {
        string slot_id = _target_slot();
        if (slot_id.Length == 0)
        {
            return "";
        }

        Resource row = Db.get_row("equip_slot_def", slot_id);
        return row != null ? row.Get("name_cn").AsString() : slot_id;
    }

    /// <summary>「名 ＋ 值 ＋ 备注」一行（品阶／装备需求／说明这种键值对）。</summary>
    private static HBoxContainer _kv_row(string node_name, string key, string value, string note)
    {
        var row = new HBoxContainer { Name = node_name };
        row.AddThemeConstantOverride("separation", 8);
        Label key_label = new Label { Name = $"{node_name}Key", Text = key };
        key_label.AddThemeColorOverride("font_color", UiKit.color("ink_dim"));
        Label value_label = new Label { Name = $"{node_name}Value", Text = value };
        value_label.AddThemeColorOverride("font_color", UiKit.color("ink"));
        value_label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(key_label);
        row.AddChild(value_label);
        if (note.Length > 0)
        {
            Label note_label = new Label { Name = $"{node_name}Note", Text = note };
            note_label.AddThemeColorOverride("font_color", UiKit.color("jade"));
            row.AddChild(note_label);
        }

        return row;
    }

    /// <summary>这件装备的属性行（`equip_base` 的 `attr_*` 与 `bonus_*` 两段，顺序照表）。</summary>
    private System.Collections.Generic.List<Control> _equip_stat_rows(CharacterSheet panel, EquipBaseRow base_row)
    {
        var rows = new System.Collections.Generic.List<Control>();
        if (base_row == null)
        {
            return rows;
        }

        foreach (Variant attr_key in base_row.attr_bonuses().Keys)
        {
            int value = base_row.attr_bonuses()[attr_key].AsInt32();
            if (value != 0)
            {
                rows.Add(_kv_row(
                    $"EquipAttr{attr_key.AsString()}", panel.attribute_name(attr_key.AsString()),
                    $"+{value}", ""));
            }
        }

        foreach (Variant stat_key in base_row.stat_bonuses().Keys)
        {
            double value = base_row.stat_bonuses()[stat_key].AsDouble();
            if (value != 0.0)
            {
                rows.Add(_kv_row(
                    $"EquipBonus{stat_key.AsString()}", panel.stat_name(stat_key.AsString()),
                    $"+{panel.format_stat_value(stat_key.AsString(), value)}", ""));
            }
        }

        if (rows.Count == 0)
        {
            rows.Add(_make_label("EquipStatNone", "白板，没有附加属性"));
        }

        return rows;
    }

    /// <summary>
    /// 套装归属：这件装备属于的**装备套装**（`set_def.set_kind = equip`）以及每一档的进度。
    /// 「N / M」＝身上穿着几件成员（`SetService.count_for`，与真判定的算法同一处）／这一档要几件；
    /// 到了就写「已激活」，没到写「未达」——档位文案照 `set_bonus.desc` 读，界面不自己编。
    /// </summary>
    private System.Collections.Generic.List<Control> _set_rows(
        CharacterSheet panel, GodotObject st, EquipBaseRow base_row)
    {
        var rows = new System.Collections.Generic.List<Control>();
        if (base_row == null)
        {
            return rows;
        }

        Inventory inventory = st.Get("inventory").As<Inventory>();
        SetService sets = SetService.create(Db);
        var matched = false;
        foreach (Variant def_value in sets.all_defs())
        {
            var def = def_value.As<SetDefRow>();
            if (def.set_kind != SetService.KIND_EQUIP
                || System.Array.IndexOf(sets.members_of(def.set_id), base_row.equip_id) < 0)
            {
                continue;
            }

            matched = true;
            int count = sets.count_for(def.set_id, panel.char_id, inventory, st);
            foreach (Variant bonus_value in sets.bonuses_of(def.set_id))
            {
                var bonus = bonus_value.As<SetBonusRow>();
                bool active = bonus.required_count <= count;
                rows.Add(_kv_row(
                    $"SetRow{def.set_id}_{bonus.required_count}",
                    $"{def.name_cn} {count} / {bonus.required_count}",
                    bonus.desc,
                    active ? "已激活" : "未达"));
            }
        }

        if (!matched)
        {
            rows.Add(_make_label("SetNone", "不属于任何装备套装。"));
        }

        return rows;
    }

    /// <summary>右栏：可换——背包里能装到这个槽位的装备（点一行＝选中候选，右边那枚「换上」才真穿）。</summary>
    private VBoxContainer _equip_candidate_column(CharacterSheet panel)
    {
        var col = new VBoxContainer { Name = "EquipCandidateColumn" };
        col.AddThemeConstantOverride("separation", 6);
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        string target_slot = _target_slot();
        if (target_slot.Length == 0)
        {
            col.AddChild(_section("CandidateSection", "CandidateHeader", "可换", "先选一个槽位"));
            col.AddChild(_make_label("NoSlot", "点左边一个槽位，这里列出背包里能装上去的装备。"));
            col.AddChild(_make_label(
                "SlotHint", "七个槽位照配表读；心法不是装备槽，走武学页的占格。"));
            return col;
        }

        string target_name = _target_slot_name();
        Godot.Collections.Array candidates = _candidate_rows(target_slot);
        col.AddChild(_section(
            "CandidateSection", "CandidateHeader", $"可以装到「{target_name}」的装备：",
            $"行囊 {candidates.Count} 件"));
        if (candidates.Count == 0)
        {
            col.AddChild(_make_label("NoCandidate", "背包里没有适合这个槽位的装备"));
            return col;
        }

        foreach (Variant candidate_value in candidates)
        {
            col.AddChild(_candidate_row(panel, target_slot, _selected_slot_index(), candidate_value.AsGodotDictionary()));
        }

        col.AddChild(_make_label(
            "SlotHint", "七个槽位照配表读；心法不是装备槽，走武学页的占格。"));
        return col;
    }

    /// <summary>选中槽位的第几格（`武器|0` 的拆法只有这一处，别在别处再 Split 一遍）。</summary>
    private int _selected_slot_index()
    {
        if (_selected_slot.Length == 0)
        {
            return 0;
        }

        string[] parts = _selected_slot.Split("|");
        return parts.Length > 1 && int.TryParse(parts[1], out int index) ? index : 0;
    }

    /// <summary>可换列表里的一行：选中那枚（点它＝选中候选）＋「换上」。锁定的一行两枚都置灰。</summary>
    private Control _candidate_row(
        CharacterSheet panel, string slot_id, int index, Godot.Collections.Dictionary candidate)
    {
        string instance_id = _s(candidate, "instance_id");
        bool ok = _b(candidate, "ok");
        // 一行里只写「名字 · 品阶 ＋ 差值」：这一栏到手才 ~270px，把属性摘要也串进来会**被按钮裁掉**
        // （Button 不换行）。属性摘要够短时放悬浮说明，长的部分在中间的详情里逐条摆着。
        string delta = ok
            ? _swap_delta_text(panel, slot_id, index, instance_id)
            : _s(candidate, "error");
        var line = new HBoxContainer { Name = $"CandidateLine{instance_id}" };
        line.AddThemeConstantOverride("separation", 6);
        TextureRect icon = IconPaths.make_icon(
            equip_icon_path(_s(candidate, "base_id")), $"EquipIconCand{instance_id}");
        if (icon != null)
        {
            line.AddChild(icon);
        }

        Button select = _make_button(
            $"CandidateRow{instance_id}",
            $"{_s(candidate, "name")} · {_s(candidate, "rarity")}　{delta}",
            () => press_candidate(instance_id));
        select.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        select.Alignment = HorizontalAlignment.Left;
        select.AddThemeColorOverride("font_color", new Color(_s(candidate, "color")));
        select.TooltipText = instance_id == _selected_candidate
            ? $"{_s(candidate, "name")}（已选中）"
            : $"{_s(candidate, "name")}　{_s(candidate, "summary")}　{delta}";
        line.AddChild(select);
        line.AddChild(_make_button(
            $"EquipButton{instance_id}", "换上", () => { equip_instance(instance_id); }, !ok));
        return line;
    }

    /// <summary>槽位行／候选行里那件装备的本体（没有就 null）。</summary>
    private static EquipBaseRow _base_of_row(Godot.Collections.Dictionary row) =>
        row.ContainsKey("base") ? row["base"].As<EquipBaseRow>() : null;

}
