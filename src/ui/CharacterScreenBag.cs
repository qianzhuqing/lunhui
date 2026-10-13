using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// 角色面板的**行囊页**（从 `CharacterScreenPages.cs` 拆出来的 partial：那一份正卡在
/// 800 行硬上限上，行囊这次按示意图重做，正好整页搬出来）。
///
/// 版式照 `docs/dev/UI示意落地清单.md` §二-2（示意图第 2 屏）：**三列**——左边分类标签
/// （六个，每个带计数）、中间格子网格（六列方格，堆叠数压在右下角）、右边详情（大图标／
/// 名称／类型 · 稀有度／效果行／描述／主次两个按钮），窗壳下沿一条说明行。
///
/// 换成「分类 ＋ 格子 ＋ 详情」不是排版偏好：旧版是一长串「一行一件」，材料行、秘籍行、装备行
/// 各带一份自己的按钮，同一件事因此有两个入口（行内的「丢弃」与别处的「丢弃」）。
/// 这套版式要表达的是**同一件东西只出现一处、动作也只有一处**——所以道具与装备实例混在
/// 同一个网格里（按分类筛），动作全部收到右侧详情。
/// </summary>
public partial class CharacterScreen
{
    private const int BAG_COLS = 6;
    /// <summary>格子边长。92 会让四行（示意图那一屏要看见的量）比页签里能用的高度高出一截，
    /// 于是**一进来就带滚动条**、第四行被切掉半截；84 正好四行全见（4×84 ＋ 3×9 的间距）。</summary>
    private const int BAG_CELL = 84;

    /// <summary>网格里的一格：道具（`item_id` 非空）或装备实例（`instance_id` 非空）。
    /// 两者共用同一个网格与同一块详情区，所以只在数据侧分家；节点名分家
    /// （`ItemCell*`／`EquipCell*`）只是为了让用例点得到。</summary>
    private sealed class BagEntry
    {
        public string key = "";
        public string item_id = "";
        public string instance_id = "";
        public string base_id = "";
        public string slot_id = "";
        public string name = "";
        public string icon_path = "";
        public string rarity = "";
        public int count = 1;

        public bool is_equip() => instance_id.Length > 0;
    }

    /// <summary>
    /// 选中行囊里的一条（写法见 `BagEntry.key`：「item:道具 id」／「equip:实例 id」）。
    /// 格子按钮与用例都走这里，别直接改私有字段。
    /// </summary>
    public void select_bag_entry(string entry_key)
    {
        _bag_selected = entry_key;
        refresh();
    }

    private void _rebuild_bag()
    {
        // 格子马上要被整批换掉，先把手上的小卡收了——不然它会继续指着已经不在树上的那一格
        _bag_hover?.hide_now();
        _clear(_bag_box);
        GodotObject st = current_state();
        Inventory inventory = st?.Get("inventory").As<Inventory>();
        if (st == null || inventory == null)
        {
            _bag_box.AddChild(_make_label("Empty", "没有会话状态"));
            return;
        }

        var entries = _bag_entries(inventory);
        BagEntry selected = null;
        foreach (BagEntry entry in entries)
        {
            if (entry.key == _bag_selected)
            {
                selected = entry;
            }
        }

        // 选中的那条被筛掉了就把选中态一起清掉——否则右边详情会继续显示一件已经看不见的东西
        if (selected == null && _bag_selected.Length > 0)
        {
            _bag_selected = "";
        }

        // 标题条右边那截常驻字：示意图写的是「18 / 40」（容量），而配表里**没有容量这一列**
        // （已记 `待策划确认.md` Q94），所以先摆两个算得准的数，不编一个上限出来。
        VBoxContainer body = UiChrome.window(
            _bag_box, "行囊",
            $"道具 {inventory.item_ids().Length} 种 · 装备 {inventory.equipment_count()} 件",
            "BagWindow");
        // 窗体要**撑满页签**：示意图里那是个 24px 边距、上贴标题下贴底栏的整块浮窗，
        // 不撑的话它只按内容高，底下留一大截空白（`window()` 返回的是内容列，壳按名字找）。
        if (_bag_box.FindChild("BagWindow", true, false) is Control shell)
        {
            shell.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        }

        var columns = new HBoxContainer { Name = "BagColumns" };
        columns.AddThemeConstantOverride("separation", 12);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        columns.AddChild(_bag_cats(inventory));
        columns.AddChild(_bag_grid(entries));
        columns.AddChild(_bag_detail(selected));
        body.AddChild(columns);

        UiChrome.window_foot(
            body,
            selected == null ? "未选" : $"已选 1 件 · {selected.name}",
            "↑↓ 选择　Enter 使用　Esc 关闭",
            "BagFoot");
    }

    /// <summary>当前分类下有哪些格子：道具（按 `_filter` 筛）＋ 装备实例（只在「全部」与
    /// 「装备」两档里露面）。**与 `_bag_category_count` 同一套口径，改一处要连着看另一处。**</summary>
    private System.Collections.Generic.List<BagEntry> _bag_entries(Inventory inventory)
    {
        var entries = new System.Collections.Generic.List<BagEntry>();
        foreach (string item_id in inventory.item_ids())
        {
            var row = Db.get_row("item_base", item_id) as ItemBaseRow;
            // 铜钱不占格子：它挂在顶栏右边那一截常驻数字上
            if (row == null || row.item_type == "currency" || !_row_matches(_filter, row))
            {
                continue;
            }

            entries.Add(new BagEntry
            {
                key = $"item:{item_id}",
                item_id = item_id,
                name = row.name_cn,
                icon_path = ItemText.item_icon_path(Db, item_id),
                rarity = row.rarity,
                count = inventory.count(item_id),
            });
        }

        if (_filter != "all" && _filter != "equip")
        {
            return entries;
        }

        foreach (string instance_id in inventory.equipment_ids())
        {
            string base_id = inventory.base_of(instance_id);
            var base_row = Db.get_row("equip_base", base_id) as EquipBaseRow;
            if (base_row == null)
            {
                continue;
            }

            entries.Add(new BagEntry
            {
                key = $"equip:{instance_id}",
                instance_id = instance_id,
                base_id = base_id,
                slot_id = base_row.slot,
                name = base_row.name_cn,
                icon_path = equip_icon_path(base_id),
                rarity = base_row.rarity,
            });
        }

        return entries;
    }

    /// <summary>分类标签上的计数（示意图里每个标签右边那个数）。装备实例只在「全部」与
    /// 「装备」两档里算——与 `_bag_entries` 同一口径。</summary>
    private int _bag_category_count(Inventory inventory, string kind)
    {
        int n = 0;
        foreach (string item_id in inventory.item_ids())
        {
            var row = Db.get_row("item_base", item_id) as ItemBaseRow;
            if (row != null && row.item_type != "currency" && _row_matches(kind, row))
            {
                n += 1;
            }
        }

        return kind == "all" || kind == "equip" ? n + inventory.equipment_count() : n;
    }

    private Control _bag_cats(Inventory inventory)
    {
        var box = new VBoxContainer
        {
            Name = "BagCats",
            CustomMinimumSize = new Vector2(124, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        box.AddThemeConstantOverride("separation", 6);
        foreach (string[] entry in FILTERS)
        {
            string key = entry[0];
            bool on = _filter == key;
            var button = new Button
            {
                Name = $"FilterButton{key}",
                // 名字与计数写进 `Text`：`Button` 的最小尺寸只按自己的 `Text` 算、**不含子节点**
                // ——用子 Label 拼会挤成一排小方块（底栏那条踩过一次）。
                Text = $"{entry[1]}　{_bag_category_count(inventory, key)}",
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(124, 34),
            };
            button.AddThemeStyleboxOverride("normal", UiChrome.chip_style(on));
            button.AddThemeStyleboxOverride("hover", UiChrome.chip_style(true));
            button.AddThemeStyleboxOverride("pressed", UiChrome.chip_style(true));
            button.AddThemeStyleboxOverride("focus", UiChrome.chip_style(on));
            button.AddThemeColorOverride("font_color", UiKit.color(on ? "on_text" : "ink_dim"));
            button.AddThemeColorOverride("font_hover_color", UiKit.color("on_text"));
            button.AddThemeColorOverride("font_pressed_color", UiKit.color("on_text"));
            string wanted = key;
            button.Pressed += () => set_filter(wanted);
            box.AddChild(button);
        }

        return box;
    }

    private Control _bag_grid(System.Collections.Generic.List<BagEntry> entries)
    {
        var scroll = new ScrollContainer
        {
            Name = "BagGridScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var grid = new GridContainer { Name = "BagGrid", Columns = BAG_COLS };
        grid.AddThemeConstantOverride("h_separation", 9);
        grid.AddThemeConstantOverride("v_separation", 9);
        scroll.AddChild(grid);
        foreach (BagEntry entry in entries)
        {
            grid.AddChild(_bag_cell(entry));
        }

        // 空格子也要铺（示意图里网格永远是满的）：**「还剩几格」是这套版式的一半信息**，
        // 所以东西少的时候也铺满三行，不做「有几件就摆几格」。
        // 三行是量出来的：页签里给网格的地方约 347px，四行（4×84 ＋ 3×9 ＝ 363）会**一进来
        // 就带滚动条**、最后一行被切掉半截。
        int rows = (entries.Count + BAG_COLS - 1) / BAG_COLS;
        int want = Mathf.Max(3, rows) * BAG_COLS;
        for (int i = entries.Count; i < want; i++)
        {
            var pad = new PanelContainer
            {
                Name = $"CellEmpty{i}",
                CustomMinimumSize = new Vector2(BAG_CELL, BAG_CELL),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            pad.AddThemeStyleboxOverride("panel", UiChrome.cell_style(false, true));
            grid.AddChild(pad);
        }

        return scroll;
    }

    private Button _bag_cell(BagEntry entry)
    {
        string ident = entry.is_equip() ? entry.instance_id : entry.item_id;
        bool on = entry.key == _bag_selected;
        var button = new Button
        {
            Name = (entry.is_equip() ? "EquipCell" : "ItemCell") + ident,
            CustomMinimumSize = new Vector2(BAG_CELL, BAG_CELL),
            // **原生 tooltip 退休**（清单 §二-2 ＋ §二-12：格子悬停＝自绘小卡）：两套一起摆会
            // 叠着弹——原生那套的延时不可控、也塞不下四行，位置还压在卡上面。卡见 `_bag_hover_rows()`。
        };
        button.AddThemeStyleboxOverride("normal", UiChrome.cell_style(on, false));
        button.AddThemeStyleboxOverride("hover", UiChrome.cell_style(true, false));
        button.AddThemeStyleboxOverride("pressed", UiChrome.cell_style(true, false));
        button.AddThemeStyleboxOverride("focus", UiChrome.cell_style(on, false));
        string key = entry.key;
        button.Pressed += () => select_bag_entry(key);
        // 悬停出小卡（清单 §二-2 ＋ §二-12）：一屏共用一张，停 0.2 秒才浮、移开立刻收。
        // 内容走 `_bag_hover_rows()` 现算——堆叠数／词条／稀有度变了，卡跟着变。
        _hover().watch(button, () => _bag_hover_rows(entry));

        // 图／字居中：`Button` **不给子节点排版**（它不是容器），所以这里靠锚点铺满再居中。
        // 子节点一律 `MouseFilter = Ignore`，否则它会吃掉格子自己的点击。
        var fill = new CenterContainer { Name = "CellFill", MouseFilter = Control.MouseFilterEnum.Ignore };
        fill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(fill);
        fill.AddChild(_bag_face(
            entry.icon_path, entry.name,
            (entry.is_equip() ? "EquipIcon" : "ItemIcon") + ident, $"ItemGlyph{ident}"));

        // 堆叠数压在右下角；**只有 1 件时不显示数字**（清单 §二-2 那一行）
        if (entry.count > 1)
        {
            var badge = new Label
            {
                Name = $"ItemCount{ident}",
                Text = entry.count.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorLeft = 1f,
                AnchorTop = 1f,
                AnchorRight = 1f,
                AnchorBottom = 1f,
                OffsetLeft = -26,
                OffsetTop = -18,
                OffsetRight = 0,
                OffsetBottom = 0,
            };
            badge.AddThemeColorOverride("font_color", UiKit.color("on_text"));
            badge.AddThemeStyleboxOverride("normal", UiKit.flat(UiKit.color("cinnabar")));
            button.AddChild(badge);
        }

        return button;
    }

    /// <summary>格子里／详情框里那张脸：**有图才摆图**，没图退回名字的头一个字
    /// ——格子里什么都不摆比摆一个字更糟（玩家会以为那一格是空的）。</summary>
    private static Control _bag_face(string icon_path, string name, string icon_node, string glyph_node)
    {
        TextureRect icon = IconPaths.make_icon(icon_path, icon_node);
        if (icon != null)
        {
            return icon;
        }

        var glyph = new Label { Name = glyph_node, Text = _first_char(name) };
        glyph.AddThemeFontSizeOverride("font_size", UiChrome.TITLE_SIZE);
        glyph.AddThemeColorOverride("font_color", UiKit.color("ink_dim"));
        return glyph;
    }

    private Control _bag_detail(BagEntry selected)
    {
        var scroll = new ScrollContainer
        {
            Name = "BagDetailScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(300, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var panel = UiKit.make_panel("BagDetail");
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(panel);
        var column = new VBoxContainer
        {
            Name = "BagDetailColumn",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        if (selected == null)
        {
            column.AddChild(_make_label("BagDetailHint", "在左边点一格，看这件东西", 0, _hex("ink_off")));
            return scroll;
        }

        GodotObject st = current_state();
        CharacterSheet sheet_panel = sheet();
        string action = _bag_action_word(selected);
        string blocked = _bag_action_blocker(st, selected);

        var top = new HBoxContainer { Name = "BagDetailTop" };
        top.AddThemeConstantOverride("separation", 12);
        column.AddChild(top);

        var frame = UiKit.make_panel("BagDetailIcon");
        frame.CustomMinimumSize = new Vector2(76, 76);
        frame.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var center = new CenterContainer();
        frame.AddChild(center);
        center.AddChild(_bag_face(selected.icon_path, selected.name, "BagDetailIconTexture", "BagDetailGlyph"));
        top.AddChild(frame);

        var head = new VBoxContainer
        {
            Name = "BagDetailHead",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        head.AddThemeConstantOverride("separation", 2);
        head.AddChild(_make_label("BagDetailName", selected.name, UiChrome.TITLE_SIZE));
        head.AddChild(_make_label("BagDetailKind", _bag_kind_text(selected, sheet_panel), 0, _hex("ink_dim")));
        top.AddChild(head);

        column.AddChild(_make_label(
            "BagDetailEffect", _bag_effect_text(selected, sheet_panel), 0, _hex("cinnabar_deep")));
        column.AddChild(_make_label("BagDetailDesc", _bag_desc_text(selected), 0, _hex("ink_dim")));

        var actions = new HBoxContainer { Name = "BagDetailActions" };
        actions.AddThemeConstantOverride("separation", 8);
        column.AddChild(actions);

        string item_id = selected.item_id;
        string instance_id = selected.instance_id;
        bool is_equip = selected.is_equip();
        actions.AddChild(_make_button("BagUseButton", action, () =>
        {
            if (is_equip)
            {
                equip_instance(instance_id);
            }
            else
            {
                study_item(item_id);
            }
        }, blocked.Length > 0));

        // 次按钮只有道具有「丢弃」——装备的卸下在「装备」页那一套槽位上做，不在这儿重复一个入口
        bool key_item = _bag_is_key_item(selected);
        if (!is_equip)
        {
            actions.AddChild(_make_button("BagDropButton", "丢弃", () => { drop_item(item_id); }, key_item));
        }

        if (blocked.Length > 0)
        {
            column.AddChild(_make_label("BagUseReason", blocked, 0, _hex("ink_off")));
        }

        if (key_item)
        {
            column.AddChild(_make_label(
                "BagDropReason", "钥匙道具不能丢——它是那条线索的凭据", 0, _hex("ink_off")));
        }

        return scroll;
    }

    /// <summary>主按钮上的动词（清单 §二-2：由 `use_context` 决定 使用／研读／装备／查看）。</summary>
    private string _bag_action_word(BagEntry entry)
    {
        if (entry.is_equip())
        {
            return "装备";
        }

        var row = Db.get_row("item_base", entry.item_id) as ItemBaseRow;
        if (row == null)
        {
            return "查看";
        }

        // 秘籍是「研读」：它自己是一个动作（残页集齐才能读），与丹药的「使用」不是一回事
        if (row.item_type == "skillbook")
        {
            return "研读";
        }

        return row.use_context.Length > 0 ? "使用" : "查看";
    }

    /// <summary>主按钮为什么点不了（空串 ＝ 能点）。**不能点的按钮一律把原因写出来**——
    /// 清单每一屏都有「置灰时写原因」那一列。原因写在按钮下面那一行，别塞进按钮文字：文字一长
    /// 会把详情区撑变形。</summary>
    private string _bag_action_blocker(GodotObject st, BagEntry entry)
    {
        Inventory inventory = st?.Get("inventory").As<Inventory>();
        if (inventory == null)
        {
            return "没有会话状态";
        }

        if (entry.is_equip())
        {
            if (inventory.equipped_by(entry.instance_id).Count > 0)
            {
                return "已经穿在某个人身上了";
            }

            CharacterSheet sheet_panel = sheet();
            if (sheet_panel == null)
            {
                return "没有可操作的角色";
            }

            Godot.Collections.Dictionary check = inventory.can_equip(
                Db, sheet_panel.char_id, sheet_panel.level, sheet_panel.weapon_type_id(), entry.instance_id);
            return _b(check, "ok") ? "" : _s(check, "error");
        }

        var row = Db.get_row("item_base", entry.item_id) as ItemBaseRow;
        if (row == null)
        {
            return "表里查不到这件东西";
        }

        if (row.item_type == "skillbook")
        {
            Godot.Collections.Dictionary gate = SkillGrant.study_gate(Db, st, entry.item_id);
            return _b(gate, "ok") ? "" : _s(gate, "error");
        }

        // 丹药这类：`item_base` 还没有效果数值列（Q33），先置灰并把话说清楚，别让玩家点了没反应
        return row.use_context.Length > 0
            ? "这件道具的效果数值还没接，先别用（效果待补）"
            : "这件暂时没有可用的动作，先看描述";
    }

    private bool _bag_is_key_item(BagEntry entry)
    {
        if (entry.is_equip())
        {
            return false;
        }

        return Db.get_row("item_base", entry.item_id) is ItemBaseRow row && row.is_key_item;
    }

    private string _bag_kind_text(BagEntry entry, CharacterSheet sheet_panel)
    {
        string rarity = sheet_panel != null ? sheet_panel.rarity_name(entry.rarity) : entry.rarity;
        if (entry.is_equip())
        {
            Resource slot_row = Db.get_row("equip_slot_def", entry.slot_id);
            string slot_name = slot_row != null ? slot_row.Get("name_cn").AsString() : entry.slot_id;
            return $"{slot_name} · {rarity}";
        }

        return $"{_bag_type_text(entry, sheet_panel)} · {rarity}";
    }

    /// <summary>
    /// 「类型」那一截（详情栏的「类型 · 稀有度」与悬停小卡的「`类型 · 部位 · 品阶`」共用一处）：
    /// **道具走 `item_type` 的中文名**（`ItemText`，界面不印英文枚举）、**装备走武器类型名**
    /// （剑／拳／刀…，读 `weapon_type_def.name_cn`；非武器为空，调用方跳过那一截）。
    /// </summary>
    private string _bag_type_text(BagEntry entry, CharacterSheet sheet_panel)
    {
        if (entry.is_equip())
        {
            var base_row = Db.get_row("equip_base", entry.base_id) as EquipBaseRow;
            return base_row != null && sheet_panel != null ? sheet_panel.weapon_type_name_of(base_row) : "";
        }

        var row = Db.get_row("item_base", entry.item_id) as ItemBaseRow;
        return ItemText.type_name(row != null ? row.item_type : "");
    }

    /// <summary>
    /// 这一格悬停要说的五格字（清单 §二-12）——**与右侧详情栏同一处数据**，不另算一套：
    /// 稀有度／效果行／描述／词条都走详情栏用的那几个私有函数，小卡只是换个排法。
    /// </summary>
    private string[] _bag_hover_rows(BagEntry entry)
    {
        CharacterSheet sheet_panel = sheet();
        string rarity = sheet_panel != null ? sheet_panel.rarity_name(entry.rarity) : entry.rarity;
        string type_text = _bag_type_text(entry, sheet_panel);
        if (entry.is_equip())
        {
            var base_row = Db.get_row("equip_base", entry.base_id) as EquipBaseRow;
            Resource slot_row = Db.get_row("equip_slot_def", entry.slot_id);
            string slot_name = slot_row != null ? slot_row.Get("name_cn").AsString() : "";
            return HoverCardText.equip_card(
                entry.name, type_text, slot_name, rarity,
                sheet_panel != null ? sheet_panel.equipment_summary(base_row) : "",
                sheet_panel != null ? sheet_panel.affix_summary_of(entry.instance_id) : "");
        }

        return HoverCardText.item_card(
            entry.name, type_text, rarity, entry.count,
            _bag_effect_text(entry, sheet_panel), _bag_desc_text(entry),
            // 脚注写**真话**：这一屏左键是选中，动作在右侧详情那两个按钮上
            // （清单原文的「左键使用 · 右键丢弃」与现状不符，已记进交接待策划回话）
            "左键选中 · 右侧按钮使用／丢弃");
    }

    /// <summary>行囊这一屏共用的那张悬停小卡（第一次用到时才建，挂在本屏根节点上）。</summary>
    private HoverCard _hover() => _bag_hover ??= HoverCard.make(this, "BagHoverCard");

    private HoverCard _bag_hover;

    /// <summary>效果行：**没有数值列就直说「效果待补」**——印一个编出来的数字比留白更糟
    /// （`item_base` 的效果数值列是 `待策划确认.md` Q33）。装备的效果行走词条。</summary>
    private string _bag_effect_text(BagEntry entry, CharacterSheet sheet_panel)
    {
        if (entry.is_equip())
        {
            string affix = sheet_panel != null ? sheet_panel.affix_summary_of(entry.instance_id) : "";
            return affix.Length > 0 ? $"词条：{affix}" : "无词条";
        }

        return "效果待补（配表还没有效果数值列）";
    }

    private string _bag_desc_text(BagEntry entry)
    {
        if (entry.is_equip())
        {
            var base_row = Db.get_row("equip_base", entry.base_id) as EquipBaseRow;
            return base_row != null ? base_row.desc : "";
        }

        var row = Db.get_row("item_base", entry.item_id) as ItemBaseRow;
        return row != null ? row.desc : "";
    }

    /// <summary>`_make_label` 的颜色参数是字符串（`new Color(str)`），而色值只在主题里有一份
    /// ——这里补一个「主题色名 → 十六进制」的转写，别在界面里散写 `#` 字面量。</summary>
    private static string _hex(string color_name) => UiKit.color(color_name).ToHtml(false);

    private static string _first_char(string text) => text.Length > 0 ? text.Substring(0, 1) : "？";
}
