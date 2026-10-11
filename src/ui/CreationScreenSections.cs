using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// `CreationScreen` 的各区段渲染与汇总（从 `CreationScreen.cs` 拆出来的 partial）：
/// 出身卡／七维与武器／天赋／起名／确认汇总，外加右侧立绘刷新。
///
/// 这里**不查表算数**——出身卡那几样（七维／武器名／起始武学名）由 `CreationService.origins()`
/// 备好，汇总里的武器名与武学名也走 `CreationService` 那两个 helper，与面板同一份取名口径。
/// </summary>
public partial class CreationScreen
{
    // ------------------------------------------------------------------ 各区段

    private void _build_origin_section()
    {
        Godot.Collections.Array cards = CreationService.origins((TableDb)db);
        _body.AddChild(_section_label("选出身（模板）"));
        foreach (Variant card_variant in cards)
        {
            Godot.Collections.Dictionary card = card_variant.AsGodotDictionary();
            bool picked = _s(spec, "origin_id") == _s(card, "origin_id");
            // 一张卡 = 按钮（点它选中）＋ 下面一行信息（七维条形图／武器／起始武学）。
            // 设计 13 §二／§六 要的就是这两行：**只给一句文案没法做数值选择**，
            // 而条形图比读七个数字快（五个出身一眼可比）。
            var box = new VBoxContainer { Name = "OriginCard_" + _s(card, "origin_id") };
            box.AddThemeConstantOverride("separation", 2);
            var button = new Button
            {
                Name = "Origin_" + _s(card, "origin_id"),
                Text = $"{(picked ? "● " : "○ ")}{_s(card, "name_cn")}（{_s(card, "playstyle")}）"
                    + $"——{_s(card, "tagline")}",
                CustomMinimumSize = new Vector2(740, 40),
            };
            string captured = _s(card, "origin_id");
            button.Pressed += () => pick_origin(captured);
            box.AddChild(button);
            var detail = new Label
            {
                Name = "OriginDetail_" + _s(card, "origin_id"),
                Text = _origin_card_detail(card),
            };
            detail.AddThemeFontSizeOverride("font_size", 12);
            box.AddChild(detail);
            _body.AddChild(box);
        }
    }

    /// <summary>
    /// 出身卡的第二行：**七维条形图 ＋ 武器 ＋ 起始武学**（设计 13 §二／§六）。
    /// 条形图与 NPC 面板的好感条同一套写法（`█`／`░` 各五格，按 15 分满刻度）——
    /// 字形来自已经落地的像素字体，不额外要图。
    /// </summary>
    private string _origin_card_detail(Godot.Collections.Dictionary card)
    {
        Godot.Collections.Dictionary attrs = card.ContainsKey("attrs")
            ? card["attrs"].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        var parts = new System.Collections.Generic.List<string>();
        foreach (string attr_id in ATTR_ORDER)
        {
            int value = attrs.ContainsKey(attr_id) ? attrs[attr_id].AsInt32() : 0;
            parts.Add($"{_attr_name(attr_id)} {_attr_bar(value)}{value}");
        }

        var skills = new System.Collections.Generic.List<string>();
        Godot.Collections.Array skill_names = card.ContainsKey("skill_names")
            ? card["skill_names"].AsGodotArray()
            : new Godot.Collections.Array();
        foreach (Variant name_cn in skill_names)
        {
            skills.Add(name_cn.AsString());
        }

        return $"{string.Join("　", parts)}　｜　{_s(card, "weapon_name")}：{string.Join("、", skills)}";
    }

    /// <summary>七维条形图：五格满格 = `create_attr_max`（15）</summary>
    private static string _attr_bar(int value)
    {
        double scaled = (double)value / 15.0 * 5.0;
        int filled = System.Math.Clamp(
            (int)System.Math.Round(scaled, System.MidpointRounding.AwayFromZero), 0, 5);
        return new string('█', filled) + new string('░', 5 - filled);
    }

    private void _build_custom_section()
    {
        Godot.Collections.Dictionary rules = CreationService.attr_rules((TableDb)db);
        _body.AddChild(_section_label(
            $"分七维（总和 {_i(rules, "total")}，单项 {_i(rules, "min")}~{_i(rules, "max")}）"));
        Godot.Collections.Dictionary attrs = spec.ContainsKey("attrs")
            ? spec["attrs"].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        foreach (string attr_id in ATTR_ORDER)
        {
            if (!attrs.ContainsKey(attr_id))
            {
                attrs[attr_id] = _i(rules, "min");
            }
        }

        spec["attrs"] = attrs;
        foreach (string attr_id in ATTR_ORDER)
        {
            var row = new HBoxContainer { Name = "Attr_" + attr_id };
            row.AddChild(new Label
            {
                Text = $"{_attr_name(attr_id)}：{attrs[attr_id].AsInt32()}",
                CustomMinimumSize = new Vector2(200, 0),
            });
            foreach (int delta in new[] { -1, 1 })
            {
                var button = new Button
                {
                    Name = (delta < 0 ? "Minus" : "Plus") + attr_id,
                    Text = delta < 0 ? "-1" : "+1",
                    CustomMinimumSize = new Vector2(64, 36),
                };
                string captured_attr = attr_id;
                int captured_delta = delta;
                button.Pressed += () => nudge_attr(captured_attr, captured_delta);
                row.AddChild(button);
            }

            _body.AddChild(row);
        }

        int sum = 0;
        foreach (string attr_id in ATTR_ORDER)
        {
            sum += attrs[attr_id].AsInt32();
        }

        _body.AddChild(_section_label($"已分配 {sum} / {_i(rules, "total")}"));

        _body.AddChild(_section_label("选武器"));
        foreach (Variant weapon_variant in CreationService.weapons((TableDb)db))
        {
            Godot.Collections.Dictionary weapon = weapon_variant.AsGodotDictionary();
            bool picked = _s(spec, "weapon_type") == _s(weapon, "weapon_type");
            var button = new Button
            {
                Name = "Weapon_" + _s(weapon, "weapon_type"),
                Text = (picked ? "● " : "○ ") + _s(weapon, "name_cn"),
                CustomMinimumSize = new Vector2(180, 36),
            };
            string captured = _s(weapon, "weapon_type");
            button.Pressed += () => pick_weapon(captured);
            _body.AddChild(button);
        }

        string weapon_type = _s(spec, "weapon_type");
        if (weapon_type.Length > 0)
        {
            _body.AddChild(_section_label("选起始武学（★1 本系）"));
            foreach (Variant option_variant in CreationService.start_skill_options((TableDb)db, weapon_type))
            {
                Godot.Collections.Dictionary option = option_variant.AsGodotDictionary();
                bool picked = _s(spec, "start_skill_id") == _s(option, "skill_id");
                var button = new Button
                {
                    Name = "StartSkill_" + _s(option, "skill_id"),
                    Text = (picked ? "● " : "○ ") + _s(option, "name_cn"),
                    CustomMinimumSize = new Vector2(300, 36),
                };
                string captured = _s(option, "skill_id");
                button.Pressed += () => pick_start_skill(captured);
                _body.AddChild(button);
            }
        }
    }

    private void _build_talent_section()
    {
        string[] picks = _picks();
        TableDb table_db = (TableDb)db;
        _body.AddChild(_section_label(
            $"选天赋（还剩 {TalentService.remaining_points(table_db, picks)} / {TalentService.budget(table_db)} 点）"));
        foreach (Variant card_variant in TalentService.cards(table_db))
        {
            Godot.Collections.Dictionary card = card_variant.AsGodotDictionary();
            string talent_id = _s(card, "talent_id");
            bool picked = System.Array.IndexOf(picks, talent_id) >= 0;
            var button = new Button
            {
                Name = "Talent_" + talent_id,
                Text = $"{(picked ? "● " : "○ ")}{_s(card, "name_cn")}（{_i(card, "cost")} 点）"
                    + $"——{_s(card, "desc")}",
                CustomMinimumSize = new Vector2(740, 36),
            };
            string captured = talent_id;
            button.Pressed += () => toggle_talent(captured);
            _body.AddChild(button);
        }
    }

    private void _build_name_section()
    {
        _body.AddChild(_section_label("起名"));
        var edit = new LineEdit
        {
            Name = "NameEdit",
            Text = _s(spec, "name_cn"),
            CustomMinimumSize = new Vector2(320, 36),
        };
        edit.TextChanged += text => spec["name_cn"] = text;
        _body.AddChild(edit);
    }

    /// <summary>
    /// 确认前的汇总（设计 13 §六「确认页汇总全部选择——让玩家在进游戏前再核对一次」）。
    ///
    /// 一次把所有选择写成几行：出身／七维／武器／起始武学／天赋／名字。**与面板同一份取名口径**
    /// （武器名与武学名都走 `CreationService` 那两个 helper），不在这里另查一遍表。
    /// </summary>
    private void _build_summary_section()
    {
        _body.AddChild(_section_label("确认一遍"));
        var label = new Label
        {
            Name = "SummaryLabel",
            Text = summary_text(),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(740, 0),
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        _body.AddChild(label);
    }

    /// <summary>汇总文案（自检与用例读它：名字固定叫 `SummaryLabel`）</summary>
    public string summary_text()
    {
        var lines = new System.Collections.Generic.List<string>();
        Godot.Collections.Dictionary spec_now = _current_spec();
        TableDb table_db = (TableDb)db;
        if (_s(spec_now, "route", ROUTE_ORIGIN) == ROUTE_ORIGIN)
        {
            Resource card = table_db.get_row("origin_def", _s(spec_now, "origin_id"));
            var char_row = card != null
                ? table_db.get_row("character_base", card.Get("char_id")) as CharacterBaseRow
                : null;
            lines.Add($"出身：{(card != null ? card.Get("name_cn").AsString() : "还没选")}"
                + $"（{(card != null ? card.Get("playstyle").AsString() : "—")}）");
            if (char_row != null)
            {
                Godot.Collections.Dictionary attrs = char_row.initial_attrs();
                var parts = new System.Collections.Generic.List<string>();
                foreach (string attr_id in ATTR_ORDER)
                {
                    parts.Add($"{_attr_name(attr_id)} {(attrs.ContainsKey(attr_id) ? attrs[attr_id].AsInt32() : 0)}");
                }

                lines.Add($"七维（模板给的）：{string.Join("　", parts)}");
                var skill_names = new System.Collections.Generic.List<string>();
                foreach (string skill_id in char_row.skill_ids())
                {
                    skill_names.Add(CreationService.skill_name_of(table_db, skill_id));
                }

                lines.Add($"武器：{CreationService.weapon_name_of(table_db, char_row.weapon_type)}"
                    + $"　起始武学：{string.Join("、", skill_names)}");
            }
        }
        else
        {
            Godot.Collections.Dictionary attrs = spec_now.ContainsKey("attrs")
                ? spec_now["attrs"].AsGodotDictionary()
                : new Godot.Collections.Dictionary();
            var parts = new System.Collections.Generic.List<string>();
            foreach (string attr_id in ATTR_ORDER)
            {
                parts.Add($"{_attr_name(attr_id)} {(attrs.ContainsKey(attr_id) ? attrs[attr_id].AsInt32() : 0)}");
            }

            lines.Add($"七维（自己分的）：{string.Join("　", parts)}");
            string weapon_type = _s(spec_now, "weapon_type");
            string skill_id = _s(spec_now, "start_skill_id");
            lines.Add($"武器：{(weapon_type.Length > 0 ? CreationService.weapon_name_of(table_db, weapon_type) : "还没选")}"
                + $"　起始武学：{(skill_id.Length > 0 ? CreationService.skill_name_of(table_db, skill_id) : "还没选")}");
        }

        var talent_names = new System.Collections.Generic.List<string>();
        Godot.Collections.Array talents = spec_now.ContainsKey("talents")
            ? spec_now["talents"].AsGodotArray()
            : new Godot.Collections.Array();
        foreach (Variant talent_id in talents)
        {
            Resource row = table_db.get_row("talent_def", talent_id.AsString());
            talent_names.Add(row != null ? row.Get("name_cn").AsString() : talent_id.AsString());
        }

        string[] picks = _picks();
        lines.Add($"天赋：{(talent_names.Count > 0 ? string.Join("、", talent_names) : "还没选")}"
            + $"（{TalentService.cost_of(table_db, picks)}/{TalentService.budget(table_db)} 点）");
        string name_cn = _s(spec_now, "name_cn");
        lines.Add($"名字：{(name_cn.Trim().Length > 0 ? name_cn : "还没填")}");
        return string.Join("\n", lines);
    }

    /// <summary>收集当前选择 → 交给校验／创建的 spec（**每次刷新都从控件与 spec 现算**）</summary>
    private Godot.Collections.Dictionary _current_spec()
    {
        Godot.Collections.Dictionary out_spec = (Godot.Collections.Dictionary)spec.Duplicate(true);
        // 名字**以控件为准**：这一格是玩家直接改的，程序里改 `text` 不会发 `text_changed`，
        // 只信 spec 的话就会「界面上写着一个名字、建出来的角色是另一个」（自检里就踩到过）。
        LineEdit name_edit = FindChild("NameEdit", true, false) as LineEdit;
        if (name_edit != null)
        {
            out_spec["name_cn"] = name_edit.Text;
        }

        if (_s(out_spec, "route", ROUTE_ORIGIN) == ROUTE_ORIGIN)
        {
            out_spec.Remove("attrs");
            out_spec.Remove("weapon_type");
            out_spec.Remove("start_skill_id");
        }
        else
        {
            out_spec["attrs"] = out_spec.ContainsKey("attrs")
                ? out_spec["attrs"].AsGodotDictionary()
                : new Godot.Collections.Dictionary();
        }

        return out_spec;
    }

    // ------------------------------------------------------------------ 立绘

    /// <summary>
    /// 右侧立绘：跟着**当前选择**走——模板路看出身卡指向的 char_id，
    /// 自建路还没有立绘（角色还没生成），显示占位底。
    /// </summary>
    private void _refresh_portrait()
    {
        if (_portrait == null)
        {
            return;
        }

        string char_id = "";
        if (route == ROUTE_ORIGIN)
        {
            Resource card = ((TableDb)db).get_row("origin_def", _s(spec, "origin_id"));
            if (card != null)
            {
                char_id = card.Get("char_id").AsString();
            }
        }

        string path = $"{PORTRAIT_DIR}portrait_{char_id}.png";
        if (char_id.Length == 0 || !ResourceLoader.Exists(path))
        {
            _portrait.Texture = null;
            _portrait.TooltipText = PORTRAIT_PLACEHOLDER;
            return;
        }

        _portrait.TooltipText = "";
        _portrait.Texture = GD.Load<Texture2D>(path);
    }

    public string portrait_path()
    {
        Resource card = ((TableDb)db).get_row("origin_def", _s(spec, "origin_id"));
        return $"{PORTRAIT_DIR}portrait_"
            + $"{(card != null ? card.Get("char_id").AsString() : "")}.png";
    }
}
