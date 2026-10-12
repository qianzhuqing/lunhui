using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// bCharacterScreenb 四个页签的重建（角色／装备／行囊／武学）——从 bCharacterScreen.csb 拆出来的 partial
/// （那份要守 800 行硬上限）。
///
/// **装备页那一块 2026-10-12 又拆出去一份**（`CharacterScreenEquip.cs`：七槽视图／换上之后／
/// 详情／可换四块）。
///
/// 这一份只干一件事：按 bCharacterSheetb／bInventoryb 给的数据**摆控件**。
/// 名字（bSlotButtonweapon_0b 这种）就是用例与自检的接口，别改。
/// </summary>
public partial class CharacterScreen
{
    // ------------------------------------------------------------------ 角色页

    private void _rebuild_header()
    {
        GodotObject st = current_state();
        Inventory inventory = st?.Get("inventory").As<Inventory>();
        if (st == null || inventory == null)
        {
            _header.Text = "没有会话状态";
            if (_money_label != null)
            {
                _money_label.Text = "";
            }

            return;
        }

        // 队伍经验池 + 离下一级还差多少（升级规则见 src/core/LevelService.cs）
        LevelService levels = LevelService.create(Db, st);
        string exp_text = $"队伍经验 {st.Get("party_exp").AsInt32()}";
        string[] ids = st.Get("char_ids").AsStringArray();
        if (ids.Length > 0)
        {
            Godot.Collections.Dictionary progress = levels.exp_progress(ids[0]);
            exp_text += _b(progress, "maxed") ? "（已满级）" : $"（下一级还需 {_i(progress, "need")}）";
        }

        // 天赋（设计 12／14 §三条共性要求：「角色面板显示剩余属性点与天赋点」）：
        // 天赋**创建时就定死了**（之后不能改），所以这里写「已选的那几个 + 花了多少点」，
        // **不写「剩余」**——那会让玩家以为还能加。属性点的「未分配」在下面五维表头那一行。
        string talent_text = "天赋：无（创建时未选）";
        if (_selected_char.Length > 0)
        {
            string[] picks = TalentService.picked_of(st, _selected_char);
            var names = new System.Collections.Generic.List<string>();
            foreach (string talent_id in picks)
            {
                var row = Db.get_row("talent_def", talent_id) as TalentDefRow;
                names.Add(row != null ? row.name_cn : talent_id);
            }

            if (picks.Length > 0)
            {
                talent_text = $"天赋：{string.Join("、", names)}"
                    + $"（{TalentService.cost_of(Db, picks)}/{TalentService.budget(Db)} 点）";
            }
        }

        // 铜钱改挂**顶栏右边**（示意图把常驻数字都放在那一行）；这一行留经验／难度／天赋
        _header.Text = $"{exp_text}　难度 {st.Call("difficulty_name", Db).AsString()}　{talent_text}";
        if (_money_label != null)
        {
            _money_label.Text = $"铜钱 {inventory.money}";
        }
    }

    private void _rebuild_character()
    {
        GodotObject st = current_state();
        // 立绘跟着「角色」页走（装备／背包页各有自己的排版）：先把它从上一个父级摘下来，
        // 免得被下面的 b_clear()b 连带释放——bqueue_freeb 之后这个节点就再也摆不回去了。
        if (_portrait != null && _portrait.GetParent() != null)
        {
            _portrait.GetParent().RemoveChild(_portrait);
        }

        _clear(_char_box);
        if (st == null)
        {
            _char_box.AddChild(_make_label("Empty", "没有会话状态：请回主菜单新建或读取存档"));
            return;
        }

        // 顶部队伍页签（14 §四）：名字 ＋ 等级，点头换人，当前那个用「▶」标出来。
        // 用 Flow 容器而不是 HBox：名字长／人多的时候**换行**，比被屏幕右边裁掉强
        // （4 人满员 + 长名字那条版式用例量的就是这一行）。
        var member_row = new HFlowContainer { Name = "Members" };
        member_row.AddThemeConstantOverride("h_separation", 6);
        member_row.AddThemeConstantOverride("v_separation", 4);
        _char_box.AddChild(member_row);
        string[] ids = st.Get("char_ids").AsStringArray();
        foreach (string char_id in ids)
        {
            string who = char_id;
            bool selected = who == _selected_char;
            member_row.AddChild(_make_button(
                $"MemberButton{who}",
                $"{(selected ? "▶ " : "")}{st.Call("char_name", Db, who).AsString()}"
                    + $"　Lv{st.Call("level_of", who).AsInt32()}",
                () => select_char(who)));
        }

        // 空位也占一格（清单 §二-4：空位写「空位」）；置灰——它是缺口提示，不是按钮
        for (int empty_index = ids.Length; empty_index < _max_party(); empty_index++)
        {
            int index = empty_index;
            member_row.AddChild(_make_button($"EmptySlot{index}", "空位", () => { }, true));
        }

        // Q67 拍板 ④：选中的是**同伴**时，这一按开交往屏——同伴不站位，这里是唯一入口
        // （偷窃对同伴不适用，交往屏自己不摆那一段）。主角不摆；战斗里没人接手，也不摆。
        string social_id = _social_target();
        if (social_id.Length > 0)
        {
            string who = social_id;
            _char_box.AddChild(_make_button(
                "SocialButton",
                $"与 {st.Call("char_name", Db, who).AsString()} 交往",
                () => { open_social(); }));
        }

        CharacterSheet panel = sheet();
        if (panel == null || !panel.valid())
        {
            _char_box.AddChild(_make_label("InvalidChar", $"角色模板缺失：{_selected_char}"));
            return;
        }

        // 三栏（14 §四 ＋ UI 清单 §二-4）：左＝立绘／装备摘要｜中＝七维＋称号＋永久增益｜
        // 右＝战斗属性＋非战斗技能。三栏都只读——要改在「装备」页与下面那段武学里改。
        var detail = new HBoxContainer { Name = "DetailBody" };
        detail.AddThemeConstantOverride("separation", 12);
        _char_box.AddChild(detail);
        VBoxContainer left = _make_column(detail, "LeftColumn", "立绘 · 装备摘要", 320);
        VBoxContainer middle = _make_column(detail, "MidColumn", "七维 · 心性 · 增益");
        VBoxContainer right = _make_column(detail, "RightColumn", "战斗属性 · 非战斗技能");
        _build_character_left(left, panel);
        _build_character_middle(middle, panel, st);
        _build_character_right(right, panel);

        // 武学（学 ＋ 装）在 2026-10-11 搬进了它自己的「武学」页（清单 §二-6 那一屏），
        // 见 `_rebuild_skills()`——留在这里的话，底栏那个「武学」入口就没有落点。
    }

    /// <summary>
    /// 「武学」页（清单 §二-6）：招式槽与内功格分开摆，顶部写占用。
    /// 数值全部来自 `CharacterSheet.skill_rows()`／`slot_summary()`，界面不自己算。
    /// </summary>
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

        Godot.Collections.Dictionary summary = panel.slot_summary();
        _skill_box.AddChild(_section(
            "SkillSection", "SkillHeader", "武学",
            $"招式 {_i(summary, "active_used")}/{_i(summary, "active_slots")}"
                + $"　内功 {_i(summary, "passive_used")}/{_i(summary, "passive_capacity")} 格"));

        // 装／卸都要能按段找得到：招式在前、内功在后（`skill_rows()` 就是按这个顺序给的）
        foreach (Variant row_value in panel.skill_rows())
        {
            _skill_box.AddChild(_make_skill_row(row_value.AsGodotDictionary()));
        }
    }

    /// <summary>
    /// 三栏里的一栏。立绘那一栏要确定宽度（320），另外两栏平分剩下的地方。
    /// 每一栏＝一块卡片（示意图第 4 屏就是三个 blu-panelb）：外面是九宫格背板＋标题条，
    /// **返回的是卡片里的内容列**（名字仍是 bLeftColumnb 这些——自检按名字找它）。
    /// </summary>
    private VBoxContainer _make_column(Node parent, string node_name, string title, int min_width = 0)
    {
        // 卡片外壳与它的内容列**同名**（挂在不同父级下，不冲突）：自检按 bDetailBodyb 的
        // 直接子节点找「三栏」，b_portraitb 的父级也要叫 bLeftColumnb——两头都认这个名字。
        VBoxContainer box = UiKit.add_card(parent, title, "", node_name);
        box.Name = node_name;
        box.AddThemeConstantOverride("separation", 4);
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // 卡片外壳（bBodyb 的祖父）才是霍在 HBox 里的那一格，宽度要它来撑
        var card = box.GetParent().GetParent() as Control;
        if (card != null)
        {
            card.Name = node_name;
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (min_width > 0)
            {
                card.CustomMinimumSize = new Vector2(min_width, 0);
            }
        }

        return box;
    }

    /// <summary>
    /// 一段的标题条（示意图的 `.lu-panel-h`：12px 墨色标题 ＋ 右边一截次要字 ＋ 下面一道细线）。
    /// 15 §4.4 只有 12／24 两档字号，**层级靠颜色与位置分**——小节头一律这一种长相，
    /// 别再写 18／20 那种「不大不小」的小标题（像素字按 1.5 倍画就是糊的）。
    ///
    /// 两个名字都要留着：`section_name` 是这一段的节点名，`pin_name` 是**用例当接口找的
    /// 那个标题 Label 的名字**——用例按节点名找段、按标题名读那行字。
    /// </summary>
    private static Control _section(string section_name, string pin_name, string title, string hint = "")
    {
        Control head = UiChrome.section_head(title, hint, section_name);
        if (head.FindChild("SectionTitle", true, false) is Label title_label)
        {
            title_label.Name = pin_name;
        }

        return head;
    }

    /// <summary>
    /// 固定列里的一个小格子：**不换行**（列宽就是按整行文字量出来的；允许换行会把那一列的
    /// 最小宽度压到一个字，窄栏下整列就塌了——七维行踩过这个坑）。
    /// 颜色只从 `UiKit/colors/*` 取，别在这儿再写一遍色值。
    /// </summary>
    private static Label _cell_label(
        string node_name, string text, string color_name, int min_width, HorizontalAlignment align)
    {
        var label = new Label
        {
            Name = node_name,
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.Off,
            HorizontalAlignment = align,
        };
        label.AddThemeColorOverride("font_color", UiKit.color(color_name));
        if (min_width > 0)
        {
            label.CustomMinimumSize = new Vector2(min_width, 0);
        }

        return label;
    }

    /// <summary>左栏：立绘 ＋ 名字／定位 ＋ 装备摘要（只读，要改去「装备」页）</summary>
    private void _build_character_left(VBoxContainer box, CharacterSheet panel)
    {
        if (_portrait != null)
        {
            box.AddChild(_portrait);
        }

        // 名字是这一屏的**大标题**（示意图 `.lu-hero-name` 是 26px；两档字号里取 24——
        // 20 那种「不大不小」的档位在像素字下是 1.666 倍，画出来是糊的）
        box.AddChild(_make_label("Template", $"{panel.display_name()}　Lv{panel.level}", 24));
        string sub = $"{panel.role_tag()} · {panel.weapon_type_name()}";
        string desc = panel.template_desc();
        if (desc.Length > 0)
        {
            sub += $"　{desc}";
        }

        box.AddChild(_make_label("TemplateDesc", sub));
        box.AddChild(_section("EquipSummarySection", "EquipSummaryHeader", "装备摘要"));
        // 清单 §二-4 原本写的是「兵器／护具／站位」三行，**「站位」那行已按策划 2026-10-04 的拍板删掉**
        // （前/后排规则还没定，bbattle_actorb 里记着这条缺口）——装备摘要只摆有数据的槽位行。
        // 槽位 id 取自 bequip_slot_defb：武器是 bweaponb、护具是 bbodyb（不是 barmorb）。
        foreach (string slot_id in new[] { "weapon", "body" })
        {
            box.AddChild(_make_label($"EquipSummary{slot_id}", _equip_summary_line(panel, slot_id)));
        }

        box.AddChild(_make_button(
            "EquipTabButton", "换装、词条、套装另开「装备」页", () => select_tab(1)));
    }

    /// <summary>一件已穿装备的一行摘要：「兵器：铁枪　外功攻击+12」；空槽写「护具：空」</summary>
    private string _equip_summary_line(CharacterSheet panel, string slot_id)
    {
        Resource slot_row = Db.get_row("equip_slot_def", slot_id);
        string slot_name = slot_row != null ? slot_row.Get("name_cn").AsString() : slot_id;
        foreach (Variant row_value in panel.equipped_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            if (_s(row, "slot_id") != slot_id || _b(row, "empty"))
            {
                continue;
            }

            return $"{slot_name}：{_s(row, "name")}　{_s(row, "summary")}";
        }

        return $"{slot_name}：空";
    }

    /// <summary>
    /// 中栏：七维（5 项可加点 ＋ 2 项资质，资质那一行顺带写清它管着多少格）
    /// ＋ 图鉴奖励 ＋ 永久增益 ＋ 心性称号
    ///
    /// 七维行按清单 §二-4 摆成**五列固定列**：图标／字／值／加成来源／＋。
    /// 以前是「力 5（裸 5 ＋ 装备 0）」一整串塞在一个标签里——窄栏下那串括号会换行，
    /// 把一行撑成两行、还跟下一行对不齐。现在来源单独一列（`AttrSource<id>`，不换行）。
    /// </summary>
    private void _build_character_middle(VBoxContainer box, CharacterSheet panel, GodotObject st)
    {
        box.AddChild(_section("AttrSection", "AttrHeader", $"七维 · 可加点 {panel.available_points()} 点"));
        Godot.Collections.Dictionary total = panel.total_attrs();
        Godot.Collections.Dictionary naked = panel.naked_attrs();
        Godot.Collections.Dictionary summary = panel.slot_summary();
        // 五列用 GridContainer 而不是每行一个 HBox：列宽由**所有行**一起定，两行之间才对得齐
        // （每行自己排的话，「+2 装备」和「招式槽 12」会让两行的值列差开几个像素）。
        var grid = new GridContainer { Name = "AttrGrid", Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(grid);
        foreach (Variant attr_value in Db.rows("attribute_def"))
        {
            var attr_row = attr_value.As<AttributeDefRow>();
            string attr_id = attr_row.attr_id;
            int total_value = _i(total, attr_id);
            int naked_value = _i(naked, attr_id);
            // 悟性／根骨是资质（设计 0.13.0）：加号置灰，并在行里写清为什么不能加
            string blocked_reason = panel.allocation_block_reason(attr_id);
            bool is_talent = blocked_reason.Length > 0;
            // 属性图标：bicons/attr/<attr_id>.pngb（15 §六；没出图就不占位）
            TextureRect attr_icon = IconPaths.make_icon(IconPaths.attr(attr_id), $"AttrIcon{attr_id}");
            // 第 1 列：图标。没出图的那一项摆一个**空格子**——列的宽度由别的行定，
            // 所以这一行不会因为缺图而错位，也别给玩家留一个空的占位框。
            grid.AddChild(attr_icon ?? (Control)new Control { Name = $"AttrIconNone{attr_id}" });
            // 第 2 列：字。资质那一项把「不可加点」也跟着写出来（加号是置灰的，得说清为什么）
            Label attr_label = _make_label(
                $"AttrLabel{attr_id}",
                is_talent ? $"{attr_row.name_cn}　资质，不可加点" : attr_row.name_cn);
            // 自动换行会把标签的最小宽度压到一个字，那一列于是缩到最窄——这一列要撑住
            // （这一条踩过：面板上「力 5（裸 5 ＋ 装备 0）」被挤成竖排）
            attr_label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.AddChild(attr_label);
            // 第 3 列：值（朱红、右对齐——两三位数不会让这一列跟着晃）
            grid.AddChild(_cell_label(
                $"AttrValue{attr_id}", total_value.ToString(), "cinnabar_deep", 36,
                HorizontalAlignment.Right));
            // 第 4 列：加成来源。可加点项写装备给的那几点（没有就写「—」）；
            // 资质项这一列写它**管着什么**（清单 §二-4：省得玩家猜）
            string source_text;
            if (attr_id == "wu")
            {
                source_text = $"招式槽 {_i(summary, "active_slots")}";
            }
            else if (attr_id == "gen")
            {
                source_text = $"心法 {_i(summary, "passive_capacity")} 格";
            }
            else
            {
                source_text = total_value != naked_value ? $"+{total_value - naked_value} 装备" : "—";
            }

            grid.AddChild(_cell_label(
                $"AttrSource{attr_id}", source_text, is_talent ? "ink_off" : "jade", 76,
                HorizontalAlignment.Left));
            // 第 5 列：加点钮（资质置灰，原因进悬浮说明）
            Button plus = _make_button(
                $"PlusButton{attr_id}", "+", () => { press_plus(attr_id); },
                panel.available_points() <= 0 || is_talent);
            if (is_talent)
            {
                plus.TooltipText = blocked_reason;
            }

            plus.CustomMinimumSize = new Vector2(36, 0);
            grid.AddChild(plus);
        }

        // 图鉴奖励（设计 01「收集本身也是成长」）：进度与当前加成摆出来，玩家才知道集图鉴有用
        Godot.Collections.Dictionary codex = panel.codex_bonus();
        int collected = panel.collected_skill_count();
        int step = System.Math.Max(1, _i(codex, "step", 5));
        int remaining = (step - collected % step) % step;
        box.AddChild(_make_label(
            "CodexReward",
            $"图鉴奖励：已收集 {collected} 部武学 → 七项各 +{_i(codex, "bonus")}"
                + $"（每 {step} 部 +1，上限 {_i(codex, "cap", 10)} 次"
                + $"{(remaining == 0 ? "" : $"，再收 {remaining} 部再 +1")}）"));
        // 永久增益（设计 20 §八 第 1 条：「跟存档、跨章保留、**界面要能看见**」）：
        // 账册三选一给的是**资质与派生上限**，它们已经算进上面的七维与右栏的战斗属性里，
        // 但玩家看不出来"为什么多了这 2 点"——所以在这儿写明是哪一条、加了多少。
        string[] bonus_labels = StoryService.bonus_labels(Db, st, _selected_char);
        box.AddChild(_make_label(
            "PermanentBonus",
            $"永久增益：{(bonus_labels.Length > 0 ? string.Join("、", bonus_labels) : "无（账册三选一之后见）")}"));
        // 心性称号（Q74① 拍板 ＋ 20 §四：「落在永久增益那一行的**下面**」）：名字取自 bui_textb，
        // 条件与幕结旁白同一条件——**没到章节末整行不显示**（不是写「无」，是不摆这一行）。
        string title_text = StoryService.title_of(Db, st);
        if (title_text.Length > 0)
        {
            box.AddChild(_make_label("StoryTitle", $"心性称号：{title_text}"));
        }
    }

    /// <summary>右栏：战斗属性（按表里的顺序逐项一行）＋ 非战斗技能</summary>
    private void _build_character_right(VBoxContainer box, CharacterSheet panel)
    {
        // 小节头右边的次要字写「几项」——「照表摆」这件事玩家看得见（示意图那一行也是这么写的）
        box.AddChild(_section(
            "StatSection", "StatHeader", "战斗属性", $"{Db.rows("stat_def").Count} 项"));
        // 战斗外气血（v11 起进存档）：面板上要看得见，不然玩家不知道为什么要去医馆
        int current_hp = panel.current_hp();
        int max_hp = panel.max_hp();
        box.AddChild(_make_label(
            "CurrentHp",
            $"当前气血 {current_hp} / {max_hp}{(current_hp >= max_hp ? "" : "　（可到医馆花钱治疗）")}"));
        // 一条气血条（15 §4.4：状态条高 12、颜色是信息色）——光有数字看不出「还剩多少」，
        // 而这是玩家进战斗前最想知道的一件事。
        box.AddChild(UiChrome.stat_bar(
            "hp", max_hp > 0 ? (float)current_hp / max_hp : 0f, 200, "HpBar"));
        // 三列（图标／名／值），跟七维那五列同一个道理：值单独一列、右对齐，
        // 「外功攻击 26」这种整串一个标签的写法两行之间对不齐（清单 §二-4：每行图标／名／值）
        var stat_grid = new GridContainer { Name = "StatGrid", Columns = 3 };
        stat_grid.AddThemeConstantOverride("h_separation", 8);
        stat_grid.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(stat_grid);
        foreach (Variant stat_value in Db.rows("stat_def"))
        {
            var stat_row = stat_value.As<StatDefRow>();
            string stat_id = stat_row.stat_id;
            // 派生数值图标：bicons/stat/<stat_id>.pngb（15 §六；没出图就摆一个空格子，
            // 列宽由别的行定，这一行不会因此错位）
            TextureRect stat_icon = IconPaths.make_icon(IconPaths.stat(stat_id), $"StatIcon{stat_id}");
            stat_grid.AddChild(stat_icon ?? (Control)new Control { Name = $"StatIconNone{stat_id}" });
            Label stat_label = _make_label($"StatLabel{stat_id}", panel.stat_name(stat_id));
            stat_label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            stat_grid.AddChild(stat_label);
            stat_grid.AddChild(_cell_label(
                $"StatValue{stat_id}", panel.stat_label(stat_id), "cinnabar_deep", 56,
                HorizontalAlignment.Right));
        }

        // 非战斗技能与判定值（判定不含装备）
        box.AddChild(_section(
            "EventSkillSection", "EventSkillHeader", "非战斗技能（判定值不含装备加成）"));
        foreach (Variant row_value in panel.event_skill_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            box.AddChild(_make_label(
                $"EventSkill{_s(row, "skill_id")}",
                $"{_s(row, "name")} {_i(row, "level")}/{_i(row, "max_level")}"
                    + $"　判定 {_i(row, "check_value")}（{_s(row, "related_attr_name")}）"));
        }
    }

    /// <summary>
    /// 一行武学：名字／星级色 + 数值摘要 + 装配（卸下）按钮。
    /// 装不下的按钮置灰，原因写在旁边——玩家不用猜为什么点不动。
    /// </summary>
    private Control _make_skill_row(Godot.Collections.Dictionary row)
    {
        string skill_id = _s(row, "skill_id");
        bool equipped = _b(row, "equipped");
        bool is_active = _s(row, "kind") == "active";
        string kind_text = is_active ? "招式" : $"内功 {_i(row, "slot_cost")} 格";
        string detail;
        if (is_active)
        {
            detail = $"倍率 {_f2(_f(row, "power_ratio"))}　内力 {_i(row, "qi_cost")}";
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
            // 内功填了「特殊效果 id」但设计还没给效果表（4 部高星内功都是这样）——如实写一句，
            // 不显示 id（界面不许出现表内英文 id，文案审计盯着）。
            if (_b(row, "special_effect_pending"))
            {
                detail += "　—　带一条特殊效果，暂未生效（设计未给效果表）";
            }
        }

        // 修习门槛（属性不够时写出来，玩家知道该练哪一项）
        string req_reason = _s(row, "learn_req_reason");
        if (req_reason.Length > 0)
        {
            detail += $"　门槛 {req_reason}";
        }

        string equip_error = _s(row, "equip_error");
        string suffix = equipped ? "　▶ 已装配" : $"　{equip_error}";
        var line = new HBoxContainer { Name = $"SkillRow{skill_id}" };
        line.AddThemeConstantOverride("separation", 6);
        // 武学图标（15 §六：bassets/icons/skill/<skill_id>.pngb）——**有图才摆**，
        // 美术那 65 张只到了一部分，没图的行照旧从文字开始（不是错，别刷引擎错误）
        TextureRect skill_icon = IconPaths.make_icon(IconPaths.skill(skill_id), $"SkillIcon{skill_id}");
        if (skill_icon != null)
        {
            line.AddChild(skill_icon);
        }

        Label label = _make_label(
            $"Skill{skill_id}",
            $"{_s(row, "star_name")}★　{_s(row, "name")}　{kind_text}"
                + $"　熟练 {_i(row, "mastery")}/{_i(row, "mastery_max")}"
                + $"（×{_f2(_f(row, "mastery_multiplier"))}）　{detail}{suffix}",
            0,
            _s(row, "star_color"));
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(label);
        line.AddChild(_make_button(
            $"LoadoutButton{skill_id}",
            equipped ? "卸下" : "装配",
            () => { toggle_skill(skill_id); },
            !equipped && equip_error.Length > 0));
        return line;
    }

    // ------------------------------------------------------------------ 装备页

    /// <summary>
    /// 装备图标的路径：**优先 bequip_base.iconb，空则退回 bequip_idb**——与道具行／武学行同一套优先级
    /// （设计 15 §六＋A14，0.31.1），所以美术按哪个名字交都能对上；找不到图时 bmake_icon()b 返回 null、
    /// 调用方不留空位（"还没出图"不是数据错）。
    /// </summary>
    public string equip_icon_path(string base_id)
    {
        Resource row = Db != null ? Db.get_row("equip_base", base_id) : null;
        string icon_id = row != null ? row.Get("icon").AsString().StripEdges() : "";
        if (icon_id.Length == 0)
        {
            icon_id = base_id;
        }

        return IconPaths.equip(icon_id);
    }

    /// <summary>某个槽位可用的背包装备：b{instance_id, name, rarity, color, summary, ok, error}b</summary>
    private Godot.Collections.Array _candidate_rows(string slot_id)
    {
        var rows = new Godot.Collections.Array();
        GodotObject st = current_state();
        CharacterSheet panel = sheet();
        if (st == null || panel == null)
        {
            return rows;
        }

        Inventory inventory = st.Get("inventory").As<Inventory>();
        foreach (string instance_id in inventory.equipment_ids())
        {
            var base_row = Db.get_row("equip_base", inventory.base_of(instance_id)) as EquipBaseRow;
            if (base_row == null || base_row.slot != slot_id || inventory.is_equipped(instance_id))
            {
                continue;
            }

            Godot.Collections.Dictionary check = inventory.can_equip(
                Db, panel.char_id, panel.level, panel.weapon_type_id(), instance_id);
            rows.Add(new Godot.Collections.Dictionary
            {
                { "instance_id", instance_id },
                { "base_id", base_row.equip_id }, // 装备取图要用（优先 icon、空则退回它）
                { "name", base_row.name_cn },
                { "rarity", panel.rarity_name(base_row.rarity) },
                { "color", panel.rarity_color(base_row.rarity) },
                { "summary", panel.equipment_summary(base_row) },
                { "affix_summary", panel.affix_summary_of(instance_id) },
                { "ok", _b(check, "ok") },
                { "error", _s(check, "error") },
            });
        }

        return rows;
    }

}
