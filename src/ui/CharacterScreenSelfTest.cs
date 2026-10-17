using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// `CharacterScreen` 的真实场景自检（`-- --character-selftest`）。从 `CharacterScreen.cs` 拆出来的
/// partial（那份要守 800 行硬上限）；棘轮表盯的是**本文件**，见 `tests/TestHandshakeFloors.cs` 的
/// `VerificationCheckFloors`（2026-10-11 从 `character_screen.gd` 搬 C#，同一批 29 条原样搬过来）。
///
/// 这套剧本走「切页签 → 加点 → 穿脱装备 → 武学装配 → 研读秘籍 → 丢钥匙道具 → 造大背包量版式」，
/// 每一步都要有说法（成功或「被拒绝」），不许静默。
/// </summary>
public partial class CharacterScreen
{
    // ------------------------------------------------------------------ 自检

    private async void _run_character_selftest()
    {
        SceneTree tree = _tree();
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        GodotObject st = current_state();
        if (st == null)
        {
            // 自检**钉成单人队**：这套剧本是"选中的人不够门槛 → 拒绝且不消耗，够门槛才学会并消耗一本"。
            // 而研读是**全队**行为（`SkillGrant` 让队里每个够门槛的人都学会，够的人≥1 就消耗残页）——
            // 用 4 人队跑时，苏九娘（智 14）会先满足条件，于是"应被拒绝"的那一次直接成功、残页被吃掉，
            // 剧本不再成立（2026-10-03 接 4 人队探针时踩到，见框架说明决策 163）。
            st = GameState.new_game((TableDb)_db, "normal", new[] { "scholar_fallen" }, 0);
            state_override = st;
            _selected_char = st.Get("char_ids").AsStringArray()[0];
            refresh();
        }

        // 1. 切页签（2026-10-11 起是四个：属性／装备／武学／行囊——武学从角色页底部搬成自己一页，
        // 行囊按 Q91 挪到第 4 页，下标见 `UiNav.char_tab_of`）
        select_tab(1);
        bool equip_tab = current_tab() == 1;
        select_tab(UiNav.char_tab_of(UiNav.SKILL));
        bool skill_tab = current_tab() == UiNav.char_tab_of(UiNav.SKILL);
        select_tab(UiNav.char_tab_of(UiNav.BAG));
        bool bag_tab = current_tab() == UiNav.char_tab_of(UiNav.BAG);
        select_tab(0);
        ok = ok && equip_tab && bag_tab && skill_tab;
        lines.Add($"切页签 ok={_bool(equip_tab && bag_tab && skill_tab)}");

        // 2. 加点
        CharacterSheet panel = sheet();
        int points_before = panel.available_points();
        int str_before = _i(panel.total_attrs(), "str");
        Godot.Collections.Dictionary allocated = press_plus("str");
        panel = sheet();
        int str_after = _i(panel.total_attrs(), "str");
        bool allocate_ok = _b(allocated, "ok")
            && str_after == str_before + 1
            && panel.available_points() == points_before - 1;
        ok = ok && allocate_ok;
        lines.Add($"加点 ok={_bool(allocate_ok)}（力 {str_before} → {str_after}，剩余 {panel.available_points()}）");

        // 3. 穿脱装备：先卸下武器，再装回去（外功会掉下去又回来）
        Godot.Collections.Dictionary weapon_row = new();
        foreach (Variant row_value in panel.equipped_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            if (_s(row, "slot_id") == "weapon" && !_b(row, "empty"))
            {
                weapon_row = row;
                break;
            }
        }

        bool equip_ok = false;
        if (weapon_row.Count > 0)
        {
            string instance_id = _s(weapon_row, "instance_id");
            double atk_equipped = panel.stat_value("atk_phys");
            Godot.Collections.Dictionary removed = unequip("weapon", _i(weapon_row, "index"));
            double atk_removed = sheet().stat_value("atk_phys");
            Godot.Collections.Dictionary equipped = equip_instance(instance_id);
            double atk_again = sheet().stat_value("atk_phys");
            equip_ok = _b(removed, "ok")
                && _b(equipped, "ok")
                && atk_removed < atk_equipped
                && Mathf.IsEqualApprox(atk_again, atk_equipped);
            lines.Add(
                $"穿脱装备 ok={_bool(equip_ok)}（外功 {atk_equipped:F0} → {atk_removed:F0} → {atk_again:F0}）");
        }
        else
        {
            lines.Add("穿脱装备 ok=false（武器槽是空的）");
        }

        ok = ok && equip_ok;

        // 3b. 装备页（UI 清单 §二-5）：三栏（七槽 ＋ 换上之后｜详情｜可换）。
        // 「换上之后」走 `CharacterSheet.preview_swap_stats` 真重算（在背包副本上真穿一遍），
        // 不是拿表里的加成手工加减——所以这里钉「预演值 ＝ 真穿上去之后的值」。
        select_tab(1);
        bool equip_columns_ok = FindChild("EquipSlotsColumn", true, false) != null
            && FindChild("EquipDetailColumn", true, false) != null
            && FindChild("EquipCandidateColumn", true, false) != null;
        ok = ok && equip_columns_ok;
        lines.Add($"装备页三栏（七槽／详情／可换）={_bool(equip_columns_ok)}");

        var slot_ids = new System.Collections.Generic.List<string>();
        foreach (Variant slot_value in Db.rows("equip_slot_def"))
        {
            slot_ids.Add(slot_value.As<EquipSlotDefRow>().slot_id);
        }

        bool slot_rows_ok = true;
        foreach (string slot_id in slot_ids)
        {
            slot_rows_ok = slot_rows_ok && FindChild($"SlotButton{slot_id}_0", true, false) != null;
        }

        ok = ok && slot_rows_ok;
        lines.Add($"七槽视图（{string.Join("／", slot_ids)}）={_bool(slot_rows_ok)}");

        // 选中武器槽 → 详情栏写出装备名／装备需求／词条区；洗词条 Q6 待定先摆着置灰
        press_slot("weapon", 0);
        Label equip_detail_header = FindChild("EquipDetailHeader", true, false) as Label;
        Label equip_req_value = FindChild("EquipReqRowValue", true, false) as Label;
        Label equip_req_note = FindChild("EquipReqRowNote", true, false) as Label;
        Button reroll_button = FindChild("RerollAffixButton", true, false) as Button;
        bool detail_ok = equip_detail_header != null && equip_detail_header.Text.Length > 0
            && equip_req_value != null
            // P19 ②：武器槽的两条真门槛都正面写出来——`Lv N（剑）`，等级够了写「已满足」
            && equip_req_value.Text.StartsWith("Lv ", System.StringComparison.Ordinal)
            && equip_req_value.Text.Contains("（", System.StringComparison.Ordinal)
            && equip_req_note != null && equip_req_note.Text == "已满足"
            && FindChild("AffixText", true, false) != null
            && reroll_button != null && reroll_button.Disabled;
        ok = ok && detail_ok;
        lines.Add($"装备详情（{(equip_detail_header != null ? equip_detail_header.Text : "缺")}"
            + $" · {(equip_req_value != null ? equip_req_value.Text : "缺")}）={_bool(detail_ok)}");

        // 卸下 → 背包里出现候选 → 选中它 → 「换上之后」真重算（外功升回去）
        string spare_weapon = "";
        foreach (Variant row_value in sheet().equipped_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            if (_s(row, "slot_id") == "weapon" && !_b(row, "empty"))
            {
                spare_weapon = _s(row, "instance_id");
                break;
            }
        }

        bool swap_ok = false;
        if (spare_weapon.Length > 0)
        {
            double atk_on = sheet().stat_value("atk_phys");
            unequip("weapon", 0);
            double atk_off = sheet().stat_value("atk_phys");
            press_slot("weapon", 0);
            Button candidate_row = FindChild($"CandidateRow{spare_weapon}", true, false) as Button;
            if (candidate_row != null)
            {
                candidate_row.EmitSignal(Button.SignalName.Pressed);
                Godot.Collections.Dictionary after =
                    sheet().preview_swap_stats("weapon", 0, spare_weapon);
                double atk_after = after.ContainsKey("atk_phys") ? after["atk_phys"].AsDouble() : -1.0;
                swap_ok = atk_off < atk_on
                    && System.Math.Abs(atk_after - atk_on) < 0.0001
                    && FindChild("SwapRowatk_phys", true, false) != null;
                lines.Add($"换上之后（外功 {atk_on:F0} → {atk_off:F0} → 预演 {atk_after:F0}）={_bool(swap_ok)}");
            }

            // 还原：把武器装回去，后面几步的剧本按原状态走
            equip_instance(spare_weapon);
        }
        else
        {
            lines.Add("换上之后 ok=false（武器槽是空的）");
        }

        ok = ok && swap_ok;
        select_tab(0);

        // 4. 武学装配：卸下招式 → 槽位腾出来 → 再装回去（战斗用的是装配，不是模板）
        string[] active_ids = sheet().loadout().active_ids();
        string first_active = active_ids.Length > 0 ? active_ids[0] : "";
        bool loadout_ok = false;
        if (first_active.Length == 0)
        {
            lines.Add("武学装配 ok=false（招式槽是空的）");
        }
        else
        {
            int used_before = sheet().loadout().active_used();
            Godot.Collections.Dictionary removed = toggle_skill(first_active);
            int used_after = sheet().loadout().active_used();
            Godot.Collections.Dictionary back = toggle_skill(first_active);
            int used_back = sheet().loadout().active_used();
            loadout_ok = _b(removed, "ok") && !_b(removed, "equipped")
                && used_after == used_before - 1
                && _b(back, "ok") && _b(back, "equipped")
                && used_back == used_before;
            lines.Add($"武学装配 ok={_bool(loadout_ok)}（{first_active}：招式槽 {used_before} → {used_after} → {used_back}）");
        }

        ok = ok && loadout_ok;

        // 4b. 武学页（UI 清单 §二-6）：三栏 ＋ 顶部切人 ＋「N / M 槽（悟性 X）」＋ 槽满置灰并写原因。
        // 这一页的数值一个都不在界面里算（`slot_summary()`／`total_attrs()` 给什么就写什么），
        // 所以这里拿同一对接口反着读回来比。
        select_tab(UiNav.char_tab_of(UiNav.SKILL));
        CharacterSheet skill_panel = sheet();
        Godot.Collections.Dictionary slots = skill_panel.slot_summary();
        bool skill_page_ok = FindChild("SkillGridColumn", true, false) != null
            && FindChild("SkillPassiveColumn", true, false) != null
            && FindChild("SkillInfoColumn", true, false) != null
            && FindChild($"SkillMemberButton{skill_panel.char_id}", true, false) != null;
        ok = ok && skill_page_ok;
        lines.Add($"武学页三栏＋顶部切人 ok={_bool(skill_page_ok)}");
        var slots_hint = (FindChild("ActiveSection", true, false) as Node)?.FindChild(
            "SectionHint", true, false) as Label;
        string slots_text = $"{_i(slots, "active_used")} / {_i(slots, "active_slots")} 槽"
            + $"（悟性 {_i(skill_panel.total_attrs(), "wu")}）";
        bool slots_ok = slots_hint != null && slots_hint.Text == slots_text;
        ok = ok && slots_ok;
        lines.Add($"招式格占用数「{slots_text}」={_bool(slots_ok)}");
        // 把招式槽填满，看「装备到快捷键」那一枚置灰且原因写在悬浮里（清单 §二-6 那一行的口径）
        foreach (string probe in new[] { "sk_xuanwei_02", "sk_xuanwei_03", "sk_xuanwei_04" })
        {
            if (!st.Call("is_learned", skill_panel.char_id, probe).AsBool())
            {
                st.Call("learn_skill", skill_panel.char_id, probe);
            }
        }

        foreach (string probe in new[] { "sk_xuanwei_02", "sk_xuanwei_03" })
        {
            if (sheet().loadout().active_used() < sheet().loadout().active_slots())
            {
                toggle_skill(probe);
            }
        }

        select_skill("sk_xuanwei_04");
        Button full_equip = FindChild("SkillEquipButtonsk_xuanwei_04", true, false) as Button;
        bool slot_full_ok = sheet().loadout().active_used() == sheet().loadout().active_slots()
            && full_equip != null && full_equip.Disabled
            && full_equip.TooltipText.Contains("招式槽已满");
        ok = ok && slot_full_ok;
        lines.Add($"招式槽满了置灰并写原因={_bool(slot_full_ok)}");

        // 5. 秘籍研读：门槛不够不消耗，学会才消耗一本
        Inventory inventory = st.Get("inventory").As<Inventory>();
        string char_id = st.Get("char_ids").AsStringArray()[0];
        inventory.add_item(Db, "item_scroll_wudu", 1);
        int scrolls_before = inventory.count("item_scroll_wudu");
        Godot.Collections.Dictionary refused_study = study_item("item_scroll_wudu");
        bool study_ok = !_b(refused_study, "ok")
            && inventory.count("item_scroll_wudu") == scrolls_before;
        while (_i(sheet().total_attrs(), "int") < 12 && sheet().available_points() > 0)
        {
            press_plus("int");
        }

        Godot.Collections.Dictionary studied = study_item("item_scroll_wudu");
        study_ok = study_ok && _b(studied, "ok")
            && st.Call("is_learned", char_id, "sk_wudu_03").AsBool()
            && inventory.count("item_scroll_wudu") == 0;
        ok = ok && study_ok;
        lines.Add($"秘籍研读 ok={_bool(study_ok)}（智 {_i(sheet().total_attrs(), "int")}：门槛不足不消耗，学会后消耗一本）");

        // 6. 丢钥匙道具必须被拒
        inventory.add_item(Db, "item_wine_gourd", 1);
        refresh();
        Godot.Collections.Dictionary dropped = drop_item("item_wine_gourd");
        bool drop_ok = !_b(dropped, "ok") && _s(dropped, "error").Contains("钥匙");
        ok = ok && drop_ok;
        lines.Add($"钥匙不可丢弃 ok={_bool(drop_ok)}（{_s(dropped, "error")}）");

        // 大背包：把 15 种道具各堆满、再塞一批装备实例，然后才量版式——
        // 背包列表是按存档动态生成的，东西一多就可能顶破版式（发行数据里 item_base 只有 16 行，
        // 「几十件东西」这种局面靠正常游玩很难自然到，专门造出来量一次）。
        int filled = 0;
        foreach (Variant item_value in Db.rows("item_base"))
        {
            ItemBaseRow item_row = item_value.As<ItemBaseRow>();
            if (item_row.item_type == "currency")
            {
                continue;
            }

            Godot.Collections.Dictionary added = inventory.add_item(
                Db, item_row.item_id, System.Math.Max(1, item_row.stack_max));
            if (_b(added, "ok"))
            {
                filled += 1;
            }
        }

        foreach (string base_id in new[] { "eq_sword_01", "eq_ring_01", "eq_head_01", "eq_armor_01" })
        {
            for (int n = 0; n < 5; n++)
            {
                inventory.add_equipment(Db, base_id, new Godot.Collections.Array());
            }
        }

        refresh();
        lines.Add($"大背包：道具 {filled} 种堆满 + 装备 {inventory.equipment_count()} 件");

        // 版式预算：整页最小高度要塞得进设计分辨率（塞不下时内容被屏幕底部裁掉，代码一声不吭）
        ok = ok && LayoutBudget.fits(this);
        lines.Add(LayoutBudget.ascii_line(this));
        ok = ok && LayoutBudget.has_opaque_backdrop(this);
        lines.Add(LayoutBudget.ascii_backdrop_line(this));
        // 新增的「道具使用 · 选人」弹窗（清单 §二-3）也是浮层，同样要过版式预算——**在开着的状态下量**
        // （弹窗懒建：不开连节点都没有）。量它那块面板的最小尺寸有没有顶破 1152×648，两方向都要过；
        // 量完关掉，别影响后面几条。
        string field_item = _first_field_item_id();
        bool use_modal_ok = true;
        if (field_item.Length > 0)
        {
            open_use_item(field_item);
            var use_window = FindChild("UseWindow", true, false) as Control;
            Vector2 use_min = use_window != null ? use_window.GetCombinedMinimumSize() : Vector2.Zero;
            use_modal_ok = use_modal_open() && use_window != null
                && use_min.X <= 1152 && use_min.Y <= 648;
            lines.Add($"道具使用弹窗 {use_min.X:F0}×{use_min.Y:F0}（限 1152×648）");
            close_use_item();
        }

        ok = ok && use_modal_ok;
        lines.Add($"道具使用弹窗〔开得起来 ＋ 版式塞得进〕={_bool(use_modal_ok)}");
        // 顶部「剩余配额」那一行（设计 14 §三条共性要求）：铜钱／经验／难度 ＋ **天赋**
        // （属性点的「未分配」在五维表头那一行）。天赋写「已选 + 花了几点」而不是「剩余」——
        // 它创建时就定死了，写「剩余」会让玩家以为还能加。
        // 铜钱 2026-10-11 起挂在**顶栏右边**（示意图把常驻数字都摆那一行），页面头留经验／难度／天赋
        Label money_label = (FindChild("TopBar", true, false)?.FindChild("Right", true, false)) as Label;
        bool quota_ok = _header.Text.Contains("天赋：") && _header.Text.Contains("队伍经验")
            && money_label != null && money_label.Text.Contains("铜钱");
        ok = ok && quota_ok;
        lines.Add($"顶部配额行（顶栏铜钱 ＋ 经验／难度／天赋）={_bool(quota_ok)}"
            + $"（{(money_label != null ? money_label.Text : "缺")}｜{_header.Text}）");
        // 永久增益那一行（设计 20 §八 第 1 条：「界面要能看见」）：没做过抉择也要如实写「无」，
        // 不能留白——这条通道在面板上必须有位置。
        Label bonus_label = FindChild("PermanentBonus", true, false) as Label;
        bool bonus_ok = bonus_label != null && bonus_label.Text.Contains("永久增益：");
        ok = ok && bonus_ok;
        lines.Add($"永久增益行={_bool(bonus_ok)}（{(bonus_label != null ? bonus_label.Text : "缺")}）");
        // 立绘位（设计 0.19.1／14）：左侧 320×480、按 char_id 取图；缺图不留空白框
        TextureRect portrait = FindChild("Portrait", true, false) as TextureRect;
        bool portrait_ok = portrait != null
            && portrait.CustomMinimumSize == new Vector2(320, 480)
            && portrait.Texture != null;
        ok = ok && portrait_ok;
        lines.Add($"立绘位 320×480 且占位图已加载={_bool(portrait_ok)}（{portrait_path()}）");
        // —— 第 4 屏（UI 清单 §二-4）：三栏排版 ＋ 装备摘要 ＋ 资质行 ＋ 底部提示 ——
        Control detail = FindChild("DetailBody", true, false) as Control;
        var column_names = new System.Collections.Generic.List<string>();
        if (detail != null)
        {
            foreach (Node child in detail.GetChildren())
            {
                column_names.Add(child.Name.ToString());
            }
        }

        bool columns_ok = detail != null && column_names.Count == 3
            && column_names.Contains("LeftColumn") && column_names.Contains("MidColumn")
            && column_names.Contains("RightColumn");
        ok = ok && columns_ok;
        lines.Add($"角色页三栏＝{_bool(columns_ok)}（{string.Join("／", column_names)}）");
        // 立绘移进了左栏（以前是「面板左边固定一块」，装备／背包页也一起显示）
        bool portrait_in_left = _portrait != null && _portrait.GetParent() != null
            && _portrait.GetParent().Name.ToString() == "LeftColumn";
        ok = ok && portrait_in_left;
        lines.Add($"立绘在左栏里={_bool(portrait_in_left)}");
        // 装备摘要两行（清单写的是「兵器／护具／站位」三行，站位规则还没定，先两行）
        Label weapon_line = FindChild("EquipSummaryweapon", true, false) as Label;
        Label armor_line = FindChild("EquipSummarybody", true, false) as Label;
        // 槽位名照表里的 `name_cn` 读（清单写的「兵器」在表里叫「武器」）——别在这儿抄第二份名字
        EquipSlotDefRow weapon_slot = Db.get_row("equip_slot_def", "weapon") as EquipSlotDefRow;
        EquipSlotDefRow armor_slot = Db.get_row("equip_slot_def", "body") as EquipSlotDefRow;
        bool equip_summary_ok = weapon_line != null && armor_line != null
            && weapon_slot != null && armor_slot != null
            && weapon_line.Text.Contains(weapon_slot.name_cn)
            && armor_line.Text.Contains(armor_slot.name_cn);
        ok = ok && equip_summary_ok;
        lines.Add($"装备摘要两行={_bool(equip_summary_ok)}（{(weapon_line != null ? weapon_line.Text : "缺")}）");
        // 顶部「可加点 N 点」（14 §四 的「剩余配额」）
        Label attr_header = FindChild("AttrHeader", true, false) as Label;
        bool points_ok = attr_header != null && attr_header.Text.Contains("可加点");
        ok = ok && points_ok;
        lines.Add($"七维表头写「可加点 N 点」={_bool(points_ok)}（{(attr_header != null ? attr_header.Text : "缺")}）");
        // 资质行（清单 §二-4）：悟性／根骨那一行要把「管着什么」写出来——招式槽／心法格在
        // 「加成来源」那一列（`AttrSource<id>`），「不可加点」跟着属性名（加号是置灰的，得说清理由）
        Label wu_line = FindChild("AttrLabelwu", true, false) as Label;
        Label gen_line = FindChild("AttrLabelgen", true, false) as Label;
        Label wu_source = FindChild("AttrSourcewu", true, false) as Label;
        Label gen_source = FindChild("AttrSourcegen", true, false) as Label;
        bool talent_rows_ok = wu_line != null && gen_line != null && wu_source != null && gen_source != null
            && wu_line.Text.Contains("不可加点") && gen_line.Text.Contains("不可加点")
            && wu_source.Text.Contains("招式槽") && gen_source.Text.Contains("心法");
        ok = ok && talent_rows_ok;
        lines.Add($"资质行写出招式槽／心法格={_bool(talent_rows_ok)}"
            + $"（{(wu_source != null ? wu_source.Text : "缺")}／{(gen_source != null ? gen_source.Text : "缺")}）");
        // 七维固定列（清单 §二-4）：图标／字／值／加成来源／＋ 五列——以前「（裸 X ＋ 装备 Y）」
        // 跟名字挤在同一个标签里，窄栏下会换行；现在来源单独一列且**不换行**（换行会把列压塌）
        var attr_grid = FindChild("AttrGrid", true, false) as GridContainer;
        Label str_name = FindChild("AttrLabelstr", true, false) as Label;
        Label str_value = FindChild("AttrValuestr", true, false) as Label;
        Label str_source = FindChild("AttrSourcestr", true, false) as Label;
        bool attr_columns_ok = attr_grid != null && attr_grid.Columns == 5
            && str_name != null && str_value != null && str_source != null
            && !str_name.Text.Contains("裸")
            && str_source.AutowrapMode == TextServer.AutowrapMode.Off;
        ok = ok && attr_columns_ok;
        lines.Add($"七维五列（图标／字／值／来源／＋）={_bool(attr_columns_ok)}"
            + $"（{(str_name != null ? str_name.Text : "缺")}｜{(str_value != null ? str_value.Text : "缺")}"
            + $"｜{(str_source != null ? str_source.Text : "缺")}）");
        // 右栏：战斗属性逐项一行（行数＝表里那几项）
        var stat_rows = FindChildren("StatLabel*", "Label", true, false);
        bool stat_ok = stat_rows.Count == Db.rows("stat_def").Count;
        ok = ok && stat_ok;
        lines.Add($"战斗属性逐项一行（{stat_rows.Count} 行）={_bool(stat_ok)}");
        // 属性／派生数值图标（15 §六 命名表：按行 id 走）：**有图才摆**——
        // 断言的是「图标节点在不在 ⟺ 图在不在」，美术出图那天不用回来改用例。
        bool attr_icon_ok = true;
        foreach (Variant attr_value in Db.rows("attribute_def"))
        {
            string attr_id = attr_value.As<AttributeDefRow>().attr_id;
            if (ResourceLoader.Exists(IconPaths.attr(attr_id))
                    != (FindChild($"AttrIcon{attr_id}", true, false) != null))
            {
                attr_icon_ok = false;
            }
        }

        ok = ok && attr_icon_ok;
        lines.Add($"七维图标「有图才摆」={_bool(attr_icon_ok)}");
        bool stat_icon_ok = true;
        foreach (Variant stat_value in Db.rows("stat_def"))
        {
            string stat_id = stat_value.As<StatDefRow>().stat_id;
            if (ResourceLoader.Exists(IconPaths.stat(stat_id))
                    != (FindChild($"StatIcon{stat_id}", true, false) != null))
            {
                stat_icon_ok = false;
            }
        }

        ok = ok && stat_icon_ok;
        lines.Add($"派生数值图标「有图才摆」={_bool(stat_icon_ok)}");
        // 底部提示（14 §四）
        Label hint = FindChild("TabHint", true, false) as Label;
        bool hint_ok = hint != null && hint.Text.Contains("Tab");
        ok = ok && hint_ok;
        lines.Add($"底部提示={_bool(hint_ok)}（{(hint != null ? hint.Text : "缺")}）");
        // 队伍页签：当前项高亮（▶）＋空位占格（清单 §二-4）
        string leader = st.Get("char_ids").AsStringArray()[0];
        Button leader_button = FindChild($"MemberButton{leader}", true, false) as Button;
        var empty_slots = FindChildren("EmptySlot*", "Button", true, false);
        bool member_tabs_ok = leader_button != null && leader_button.Text.StartsWith("▶", System.StringComparison.Ordinal)
            && empty_slots.Count == _max_party() - st.Get("char_ids").AsStringArray().Length
            && (empty_slots.Count == 0 || ((Button)empty_slots[0]).Disabled);
        ok = ok && member_tabs_ok;
        lines.Add($"队伍页签：当前项带「▶」＋空位 {empty_slots.Count} 格={_bool(member_tabs_ok)}");
        // 底栏七入口（UI 清单 §一 ＋ §四-2 要的那条断言）：七个都在、**当前所在那一项高亮**、
        // 点一个真的切页签、禁用的那一个必须写清原因。
        var nav_buttons = FindChildren("Nav*", "Button", true, false);
        Button nav_current = FindChild(UiNav.node_name_of(current_entry()), true, false) as Button;
        Button nav_other = FindChild(UiNav.node_name_of(UiNav.CLUE), true, false) as Button;
        bool nav_styles = nav_current != null && nav_other != null
            && nav_current.GetThemeStylebox("normal") is StyleBoxFlat on_style
            && nav_other.GetThemeStylebox("normal") is StyleBoxFlat off_style
            && on_style.BgColor != off_style.BgColor;
        int nav_disabled = 0;
        bool nav_reason_ok = true;
        foreach (Node nav_node in nav_buttons)
        {
            if (nav_node is Button nav_button && nav_button.Disabled)
            {
                nav_disabled += 1;
                nav_reason_ok = nav_reason_ok && nav_button.TooltipText.Length > 0;
            }
        }

        nav_to(UiNav.BAG);
        bool nav_jump = current_tab() == UiNav.char_tab_of(UiNav.BAG);
        nav_to(UiNav.CHAR);
        bool nav_ok = nav_buttons.Count == UiNav.entries().Count && nav_styles && nav_jump && nav_reason_ok;
        ok = ok && nav_ok;
        lines.Add($"底栏七入口={_bool(nav_ok)}（{nav_buttons.Count} 个入口、当前项高亮={_bool(nav_styles)}"
            + $"、点行囊真的切页签={_bool(nav_jump)}、禁用 {nav_disabled} 个都有理由={_bool(nav_reason_ok)}）");
        // 页签内容宽度：**外层版式量不到滚动区里的内容**（`get_combined_minimum_size()` 到 ScrollContainer
        // 就断了，量到的其实是 `_tabs.custom_minimum_size` 那个写死的常量），而横向滚动是关着的——
        // 内容一宽就被裁掉、代码一声不吭。
        // 量法有个坑：TabContainer 只让**当前页**可见，容器又会跳过不可见的子树，所以必须
        // **逐页切过去 + 等一帧**再量；否则量到的是 0，那是「量不出来」，不是「塞得下」。
        for (int index = 0; index < _tabs.GetTabCount(); index++)
        {
            select_tab(index);
            await ToSignal(_tree(), SceneTree.SignalName.ProcessFrame);
            // 逐页报一下「这一页内容的最小尺寸」——整屏那行 LAYOUT 只给一个总数，
            // 顶破预算时看不出是哪一页撑的（TabContainer 取的是各页里最大的那个）。
            Control page = _tabs.GetChild(index) as Control;
            Vector2 page_min = page != null ? page.GetCombinedMinimumSize() : Vector2.Zero;
            lines.Add($"PAGE[{_tabs.GetTabTitle(index)}] min={page_min.X:0}x{page_min.Y:0}");
        }

        ok = ok && LayoutBudget.content_fits(this);
        lines.Add(LayoutBudget.ascii_content_line(this));
        // 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
        string[] copy_hits = CopyGuard.id_tokens(this);
        ok = ok && copy_hits.Length == 0;
        lines.Add(CopyGuard.ascii_line(this));
        if (copy_hits.Length > 0)
        {
            lines.Add($"COPY 命中：{string.Join("；", copy_hits)}");
        }

        // —— 心性称号（Q74① 拍板）——
        // 「没到章节末整行不显示」：先确认现在**没有**这一行，再点亮「序幕那一档 ＋ 账册已选」两枚旗标，
        // 确认它出现、且名字逐字取自 `ui_text`（代码不抄第二份名字）。
        bool title_gone = FindChild("StoryTitle", true, false) == null;
        ok = ok && title_gone;
        lines.Add($"章节末之前不显示心性称号行={_bool(title_gone)}");
        st.Call("set_flag", "flag_open_yi", true);
        st.Call("set_flag", "flag_ledger_public", true);
        refresh();
        Label title_label = FindChild("StoryTitle", true, false) as Label;
        UiTextRow title_row = Db.get_row("ui_text", "title_yi") as UiTextRow;
        bool title_ok = title_label != null && title_row != null
            && title_label.Text == $"心性称号：{title_row.text_cn}";
        ok = ok && title_ok;
        lines.Add($"心性称号行（义档）={_bool(title_ok)}（{(title_label != null ? title_label.Text : "缺")}）");
        // 幕结旁白与称号**同一条件**：两边的条件串必须一字一致，否则会「旁白出来了、称号还没出来」
        // （或反过来）。条件串只在 `StoryService.TITLE_CONDITION_FORMAT` 写一次，这里对着表读回来比。
        bool same_condition = true;
        foreach (string suffix in StoryService.title_suffixes())
        {
            DialogueNodeRow end_row = Db.get_row("dialogue_node", $"dl_end_{suffix}") as DialogueNodeRow;
            if (end_row == null || end_row.condition != StoryService.title_condition_format().Replace("%s", suffix))
            {
                same_condition = false;
            }
        }

        ok = ok && same_condition;
        lines.Add($"称号与幕结旁白同一条件={_bool(same_condition)}");

        // —— 同伴的交往入口（Q67 拍板 ④）——
        // 同伴不站位，这是入队之后**唯一**的交往入口：角色面板选中他 → 摆出一按 → 按下把 id 交给
        // 场景控制器（控制器那边走 `open_npc()`）。主角不摆这一按。
        var social_got = new System.Collections.Generic.List<string>();
        npc_open_handler = Callable.From((string who) => social_got.Add(who));
        select_char(leader);
        bool leader_ok = FindChild("SocialButton", true, false) == null;
        ok = ok && leader_ok;
        lines.Add($"主角不摆交往那一按={_bool(leader_ok)}");
        string companion = "";
        string[] party_ids = st.Get("char_ids").AsStringArray();
        foreach (Variant row_value in Db.rows("recruit_def"))
        {
            string cid = row_value.As<RecruitDefRow>().char_id;
            if (cid != leader && System.Array.IndexOf(party_ids, cid) < 0)
            {
                companion = cid;
                break;
            }
        }

        Godot.Collections.Dictionary joined = companion.Length > 0
            ? RecruitService.join(Db, st, companion)
            : new Godot.Collections.Dictionary { { "ok", false }, { "char_id", "" }, { "error", "没有可入队的同伴" } };
        select_char(companion);
        Button social_btn = FindChild("SocialButton", true, false) as Button;
        bool social_ok = _b(joined, "ok") && social_btn != null
            && social_btn.Text == $"与 {st.Call("char_name", _db, companion).AsString()} 交往";
        if (social_btn != null)
        {
            social_btn.EmitSignal(Button.SignalName.Pressed);
        }

        social_ok = social_ok && social_got.Count == 1 && social_got[0] == companion;
        ok = ok && social_ok;
        lines.Add($"同伴交往那一按（{companion} → 交给控制器 {string.Join("、", social_got)}）={_bool(social_ok)}");
        // 新摆出来的这一行也过一遍文案守卫（表内 id 不许出现在玩家可见文案里）
        string[] later_hits = CopyGuard.id_tokens(this);
        ok = ok && later_hits.Length == 0;
        lines.Add($"称号与交往文案守卫={_bool(later_hits.Length == 0)}");

        foreach (string line in lines)
        {
            GD.Print("  " + line);
        }

        GD.Print($"CHARACTER SELF-TEST: {(ok ? "OK" : "FAILED")}");
        if (tree != null)
        {
            tree.Quit(ok ? 0 : 1);
        }
    }

    /// <summary>
    /// 表里第一件 `use_context=field` 的药（清单 §二-3 那一屏的对象）；没有就给空串——
    /// 那种情况下「弹窗开得起来」那条按过算（没有可开的东西不是这一屏的错）。
    /// </summary>
    private string _first_field_item_id()
    {
        foreach (Variant row_value in Db.rows("item_base"))
        {
            var row = row_value.AsGodotObject() as ItemBaseRow;
            if (row != null && row.use_context == "field")
            {
                return row.item_id;
            }
        }

        return "";
    }
}
