using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 角色与行囊界面：真实场景、真实按钮。对位 `tests/test_character_ui.gd`
/// （2026-10-11 P4 第四十一批整体搬 C#）。
///
/// 这一份**不查数据、只查界面**：把 `character_screen.tscn` 挂进真场景树，按真按钮、读真文案，
/// 钉「加号没点数要置灰而不是点了才报错」「候选标题写中文槽位名而不是内部 id」
/// 「钥匙道具丢弃被拒且写明原因」「没图的行走文字、有图的才摆图标」这些**玩家看得见**的口径。
/// 数据那一半在 `tests/TestCharacterSheet.cs`。
///
/// **两份 partial**（都是顶到 800 行硬上限后按职责拆出去的，只挪位置不改行为）：
/// `TestCharacterBagUi.cs`（行囊页 ＋ 秘籍研读，2026-10-12）与 `TestCharacterEquipUi.cs`（装备页 §二-5，同日）。
/// </summary>
public partial class TestCharacterUi : TestCase
{
    private const string UiScene = "res://scenes/character_screen.tscn";
    private const string CharId = "scholar_fallen";

    public override string SuiteName() => "角色与行囊界面";

    public override void Run()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，界面用例无法进行");
            return;
        }

        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        CharacterScreen screen = _MakeScreen(state);
        var back_calls = new Godot.Collections.Array();
        screen.back_handler = Callable.From(() => back_calls.Add(true));
        Tree.Root.AddChild(screen);
        screen.setup();

        _CheckTabs(screen);
        _CheckCharacterTab(db, screen, state);
        _CheckSkillLoadout(screen, state);
        _CheckEquipmentTab(db, screen, state);
        _CheckBagTab(db, screen, state);
        _CheckIcons(db, screen, state);
        _CheckSkillbook(db, screen, state);
        _CheckBack(screen, back_calls);

        _DropScreen(screen);

        _CheckPartyStress(db);
        _CheckBigBag(db);
        _CheckNoPointsUi(db);
    }

    /// <summary>
    /// 图标（15 §六 `icons/&lt;类&gt;/&lt;id&gt;.png`）：**有图才摆**，没图的行走文字——
    /// 这一条把「按 id 找图」这件事钉在真界面上（美术那 19 张道具图标只到了 3 张）。
    /// </summary>
    private void _CheckIcons(TableDb db, CharacterScreen screen, GodotObject state)
    {
        // 路径拼接只有一处（`IconPaths`），先钉它
        CheckEq(
            IconPaths.item("item_bd_ledger"), "res://assets/icons/item/item_bd_ledger.png",
            "道具图标路径按 id 拼（15 §六）");
        CheckEq(
            IconPaths.skill("pf_xuanwei_05"), "res://assets/icons/skill/pf_xuanwei_05.png",
            "武学图标路径按 id 拼");

        // 装备图标（设计 15 §六＋A14，0.31.1）：路径**优先 `equip_base.icon`、空则退回 `equip_id`**。
        // 拿真表的值算期望，不写死 id——美术／设计改名也不会假红。
        var sword = (EquipBaseRow)db.get_row("equip_base", "eq_sword_01");
        CheckNotNull(sword, "表里有 eq_sword_01");
        if (sword != null)
        {
            string sword_icon = sword.icon.StripEdges();
            if (sword_icon.Length == 0)
            {
                sword_icon = "eq_sword_01";
            }

            CheckEq(
                screen.equip_icon_path("eq_sword_01"), IconPaths.equip(sword_icon),
                $"装备图标按 `equip_base.icon` 拼路径（{sword_icon}）");
        }

        CheckEq(
            screen.equip_icon_path("eq_bogus_000"), "res://assets/icons/equip/eq_bogus_000.png",
            "查不到那件装备时也照 id 拼路径（不留空位、更不编名字）");

        // 单开一份状态与面板：往背包里放「有图的那件（账册）」与「没图的那件（铁矿石）」，
        // 免得受前面那些用例动过的背包影响（行是按存档现建的）
        GodotObject bag_state = SoloState(db);
        _InventoryOf(bag_state).add_item(db, "item_bd_ledger", 1);
        _InventoryOf(bag_state).add_item(db, "item_iron", 5);
        CharacterScreen bag = _MakeScreen(bag_state);
        Tree.Root.AddChild(bag);
        bag.setup();
        bag.select_tab(UiNav.char_tab_of(UiNav.BAG));
        Node row = bag.FindChild("ItemCellitem_bd_ledger", true, false);
        CheckNotNull(row, "行囊网格里有账册那一格");
        if (row != null)
        {
            CheckNotNull(
                row.FindChild("ItemIconitem_bd_ledger", true, false),
                "有图的道具行前面挂着图标（账册）");
        }

        // 「没图的行不摆图标」这条要写成**有图 ⟺ 有图标节点**，别写死"某张现在还没出图"——
        // 美术一交付它就假红。这里拿 item_iron 的实际文件在不在当期望值。
        Node plain_row = bag.FindChild("ItemCellitem_iron", true, false);
        CheckNotNull(plain_row, "行囊网格里有铁矿石那一格");
        if (plain_row != null)
        {
            Node iron_icon = plain_row.FindChild("ItemIconitem_iron", true, false);
            bool iron_has_file = ResourceLoader.Exists(IconPaths.item("item_iron"));
            CheckEq(
                iron_icon != null, iron_has_file,
                $"道具行的图标跟文件同步（{IconPaths.item("item_iron")} 在吗：{iron_has_file}）");
        }

        _DropScreen(bag);

        // 「取不到图」必须是**正常状态**（美术只交付一部分，`make_icon` 返回 null、调用方不留空位）。
        // **不要拿"某个真实 id 现在还没出图"当例子**——那张图一交付，这条断言自己就红
        // （2026-10-04 实测踩到：原来写死 `pf_xuanwei_05`）。改用一个**永远不该存在**的 id，
        // 并把"它确实不存在"这条前提也钉住。
        string missing_path = IconPaths.skill("sk_zzz_not_delivered_000");
        CheckFalse(ResourceLoader.Exists(missing_path), $"这条用例的前提：{missing_path} 确实不存在");
        CheckEq(IconPaths.load_icon(missing_path), null, "没有这张图时 load_icon 返回 null（不刷错误）");
        CheckNull(IconPaths.make_icon(missing_path, "Icon"), "没图时 make_icon 返回 null（调用方据此不留位置）");
    }

    /// <summary>五维点加完之后：加号按钮要**置灰**（不是「能点、点了才报错」），标题写明剩 0。</summary>
    private void _CheckNoPointsUi(TableDb db)
    {
        GodotObject state = SoloState(db);
        CharacterScreen screen = _MakeScreen(state);
        Tree.Root.AddChild(screen);
        screen.setup();
        int guard = 0;
        while (screen.sheet().available_points() > 0 && guard < 200)
        {
            screen.press_plus("str");
            guard += 1;
        }

        CheckEq(screen.sheet().available_points(), 0, $"把五维点加完（用了 {guard} 次）");
        var plus = screen.FindChild("PlusButtonstr", true, false) as Button;
        CheckNotNull(plus, "加号按钮还在");
        if (plus != null)
        {
            CheckTrue(plus.Disabled, "没点数了，加号按钮置灰（不是点了才报错）");
        }

        var header = screen.FindChild("AttrHeader", true, false) as Label;
        CheckNotNull(header, "七维标题还在");
        if (header != null)
        {
            // 文案按 UI 清单 §二-4：「顶部写「可加点 N 点」」（14 §四 的「剩余配额」）
            CheckTrue(header.Text.Contains("可加点 0 点"), $"标题写明剩 0：{header.Text}");
        }

        Godot.Collections.Dictionary refused = screen.press_plus("str");
        CheckFalse(refused["ok"].AsBool(), "就算绕过置灰硬调也拒绝");
        _DropScreen(screen);
    }

    /// <summary>
    /// 大背包：22 种道具各堆满 ＋ 30 件装备实例时，背包页还列得出来、版式还塞得进、存档还往返得回来。
    /// 发行数据里 `item_base` 有 23 行，这种局面靠正常游玩很难自然到——列表是按存档动态生成的，
    /// 东西一多就可能出岔子（重名节点、排序、版式）。
    /// </summary>
    private void _CheckBigBag(TableDb db)
    {
        GodotObject state = SoloState(db);
        Inventory inventory = _InventoryOf(state);
        inventory.money = 123456;
        int equip_before = inventory.equipment_count();
        int filled = 0;
        foreach (Variant entry in db.rows("item_base"))
        {
            var row = (ItemBaseRow)entry.AsGodotObject();
            if (row.item_type == "currency")
            {
                continue;
            }

            if (inventory.add_item(db, row.item_id, Mathf.Max(1, row.stack_max))["ok"].AsBool())
            {
                filled += 1;
            }
        }

        CheckEq(
            filled, 22,
            "23 行道具里除「铜钱」外都放进背包（各堆满；0.26.0 加藏宝图与遗篇残卷，0.31.0 加账册，2026-10-04 加 4 本秘籍）");

        var instances = new Godot.Collections.Array();
        foreach (string base_id in new[] { "eq_sword_01", "eq_fist_01", "eq_ring_01", "eq_head_01", "eq_armor_01" })
        {
            for (int n = 0; n < 6; n++)
            {
                instances.Add(inventory.add_equipment(db, base_id, new Godot.Collections.Array()));
            }
        }

        CheckEq(instances.Count, 30, "造了 30 件装备实例");
        CheckFalse(instances.Contains(""), "30 件实例都造出来了（装备 id 写错时 add_equipment 会返回空串）");
        CheckEq(inventory.equipment_count(), equip_before + 30, "装备实例数对得上");
        var unique = new Godot.Collections.Dictionary();
        foreach (string instance_id in inventory.equipment_ids())
        {
            unique[instance_id] = true;
        }

        CheckEq(unique.Count, inventory.equipment_count(), "30 件实例的 id 没有重号");

        // 存档往返：几十件东西一个都不能丢
        GameState back = GameState.from_dict(((GameState)state).to_dict(), db);
        CheckNotNull(back, "大背包能读回来");
        if (back != null)
        {
            var back_bag = (Inventory)back.inventory;
            CheckEq(back_bag.equipment_count(), inventory.equipment_count(), "装备数量往返一致");
            CheckEq(back_bag.count("item_iron"), inventory.count("item_iron"), "堆叠数量往返一致");
            CheckEq(
                string.Join(",", back_bag.item_ids()),
                string.Join(",", inventory.item_ids()),
                "道具清单往返一致");
            CheckEq(back_bag.money, 123456, "铜钱往返一致");
        }

        // 界面：行囊页真的把每一件都摆成格子了，且整页仍塞得进设计分辨率
        CharacterScreen screen = _MakeScreen(state);
        Tree.Root.AddChild(screen);
        screen.setup();
        screen.select_tab(UiNav.char_tab_of(UiNav.BAG));
        int item_rows = 0;
        foreach (Node node in screen.FindChildren("ItemCell*", "Button", true, false))
        {
            item_rows += 1;
        }

        CheckEq(item_rows, 22, "行囊网格列出 22 种道具");
        int equip_rows = 0;
        foreach (Node node in screen.FindChildren("EquipCell*", "Button", true, false))
        {
            equip_rows += 1;
        }

        CheckEq(equip_rows, inventory.equipment_count(), "装备实例也在同一个网格里（一件一格）");
        CheckTrue(LayoutBudget.fits(screen), "大背包下整页仍塞进设计分辨率");
        _DropScreen(screen);
    }

    private void _CheckTabs(CharacterScreen screen)
    {
        var tabs = screen.FindChild("Tabs", true, false) as TabContainer;
        CheckNotNull(tabs, "有页签容器");
        if (tabs == null)
        {
            return;
        }

        // 2026-10-11 起是四个，**顺序与名字照 `14 §四`**：属性／装备／武学／行囊
        //（武学从「属性」页底部搬成自己一页；行囊按 Q91 挪到第 4 页）
        CheckEq(tabs.GetTabCount(), 4, "四个页签（属性／装备／武学／行囊）");
        CheckEq(tabs.GetTabTitle(0), "属性", "第一页是属性（14 §四 那张表就是这么列的）");
        CheckEq(tabs.GetTabTitle(1), "装备", "第二页是装备");
        CheckEq(tabs.GetTabTitle(2), "武学", "第三页是武学");
        CheckEq(tabs.GetTabTitle(3), "行囊", "第四页是行囊（Q91：行囊＝角色面板第 4 个页签）");
        CheckEq(screen.current_tab(), 0, "默认停在属性页");
        CheckEq(
            UiNav.char_tab_of(UiNav.BAG), 3,
            "底栏「行囊」入口映射到第 4 页——与上面那串顺序是同一件事的两个写法");
        screen.select_tab(UiNav.char_tab_of(UiNav.BAG));
        CheckEq(screen.current_tab(), 3, "能切到行囊页");
        screen.select_tab(0);
    }

    private void _CheckCharacterTab(TableDb db, CharacterScreen screen, GodotObject state)
    {
        var header = screen.FindChild("Header", true, false) as Label;
        CheckNotNull(header, "有头部信息");
        if (header != null)
        {
            CheckTrue(header.Text.Contains("经验"), "头部显示队伍经验");
        }

        // 铜钱 2026-10-11 起挂在**顶栏右边**（示意图把常驻数字都摆那一行）
        CheckTrue(_MoneyText(screen).Contains("铜钱"), $"顶栏显示铜钱：{_MoneyText(screen)}");

        var member = screen.FindChild($"MemberButton{CharId}", true, false) as Button;
        CheckNotNull(member, "成员列表里有当前角色");
        foreach (string attr_id in new[] { "str", "con", "agi", "int", "luk" })
        {
            CheckNotNull(screen.FindChild($"PlusButton{attr_id}", true, false), $"{attr_id} 有加点按钮");
        }

        // 设计 0.13.0：悟性／根骨是资质——按钮仍在（玩家看得到这一项），但置灰并写明原因
        foreach (string talent_id in new[] { "wu", "gen" })
        {
            var talent_plus = screen.FindChild($"PlusButton{talent_id}", true, false) as Button;
            CheckNotNull(talent_plus, $"{talent_id} 的加号还在（置灰而不是藏起来）");
            if (talent_plus != null)
            {
                CheckTrue(talent_plus.Disabled, $"{talent_id} 是资质，加号置灰");
                CheckTrue(
                    talent_plus.TooltipText.Contains("资质"),
                    $"{talent_id} 的悬浮说明写明是资质：{talent_plus.TooltipText}");
            }

            var talent_label = screen.FindChild($"AttrLabel{talent_id}", true, false) as Label;
            CheckTrue(
                talent_label != null && talent_label.Text.Contains("不可加点"),
                $"{talent_id} 那一行写明不可加点");
            // 资质项「管着什么」（招式槽／心法格）2026-10-12 起写在**加成来源那一列**里
            // （清单 §二-4 的五列：图标／字／值／加成来源／＋）
            var talent_source = screen.FindChild($"AttrSource{talent_id}", true, false) as Label;
            CheckTrue(
                talent_source != null
                && talent_source.Text.Contains(talent_id == "wu" ? "招式槽" : "心法"),
                $"{talent_id} 的来源列写清它管着多少格：{talent_source?.Text}");
        }

        CharacterSheet sheet = screen.sheet();
        int before = sheet.available_points();
        var plus = screen.FindChild("PlusButtonstr", true, false) as Button;
        plus?.EmitSignal(Button.SignalName.Pressed);
        CheckEq(
            ((GameState)state).allocations_of(CharId)["str"].AsInt32(), 1, "点加号会写进存档");
        CheckEq(screen.sheet().available_points(), before - 1, "未分配点数减少");
        CheckTrue(screen.status_text().Contains("加点"), "状态栏提示加点成功");

        var event_label = screen.FindChild("EventSkillwenxue", true, false) as Label;
        CheckNotNull(event_label, "非战斗技能行存在");
        if (event_label != null)
        {
            CheckTrue(event_label.Text.Contains("判定"), "显示判定值");
        }

        CheckTrue(
            (screen.FindChild("EventSkillHeader", true, false) as Label).Text.Contains("不含装备"),
            "标注判定不含装备");

        // 战斗外气血（v11）：面板要看得到当前气血；五维行不能被自动换行压成竖排
        var hp_line = screen.FindChild("CurrentHp", true, false) as Label;
        CheckNotNull(hp_line, "战斗属性上面有当前气血行");
        if (hp_line != null)
        {
            CheckTrue(hp_line.Text.Contains("当前气血"), $"写清是当前气血：{hp_line.Text}");
        }

        var attr_label = screen.FindChild("AttrLabelstr", true, false) as Label;
        CheckNotNull(attr_label, "五维行存在");
        if (attr_label != null)
        {
            // 五维行不能被自动换行压成竖排（这一条真踩过：面板上「力 5（裸 5 ＋ 装备 0）」
            // 被竖着排下来）。旧链量的是 `get_line_count()` —— 但那条链的视口是 0×0、
            // 容器从没排过版，量出来**恒为 1**，也就是说它一直是空转的；C# 链上量到的是
            // 「宽度 1、换行 3 行」这种没排版时的假象。所以这里改钉**那条修复本身**：
            // 这一行必须撑满整行（版式是否真的塞得下，另有下面 `LayoutBudget.fits` 那条量）。
            CheckEq(
                (int)attr_label.SizeFlagsHorizontal, (int)Control.SizeFlags.ExpandFill,
                "五维行撑满整行（自动换行会把最小宽度压到一个字，HBox 于是把它挤成竖排）");
            // 加成来源 2026-10-12 起**单独一列**（清单 §二-4）：名字后面不再拖那串「（裸 X ＋ 装备 Y）」
            CheckFalse(attr_label.Text.Contains("裸"), $"加成来源移到独立列：{attr_label.Text}");
        }

        // 七维五列固定列：值／加成来源各占一列，来源列不换行（列宽按整行文字算）
        var attr_value = screen.FindChild("AttrValuestr", true, false) as Label;
        var attr_source = screen.FindChild("AttrSourcestr", true, false) as Label;
        CheckNotNull(attr_value, "五维行有独立的数值列");
        CheckNotNull(attr_source, "五维行有独立的加成来源列");
        if (attr_source != null)
        {
            CheckEq(
                (int)attr_source.AutowrapMode, (int)TextServer.AutowrapMode.Off,
                "加成来源列不换行（换行会把那一列的最小宽度压到一个字）");
            CheckTrue(
                attr_source.Text == "—" || attr_source.Text.StartsWith("+"),
                $"来源列写「+N 装备」或「—」：{attr_source.Text}");
        }

        if (attr_value != null)
        {
            // 值那一列只放数值本身（「（裸 X ＋ 装备 Y）」那截已经搬去来源列了）——
            // 两边都写数值的话，玩家会以为两列数的是两回事
            CheckTrue(
                int.TryParse(attr_value.Text, out int parsed_value) && parsed_value > 0,
                $"值那一列只放数值：{attr_value.Text}");
        }

        // 图鉴奖励：面板要写清「已收集几部 → 现在 +几、再收几部再 +1」
        var codex_line = screen.FindChild("CodexReward", true, false) as Label;
        CheckNotNull(codex_line, "五维下面有图鉴奖励行");
        if (codex_line != null)
        {
            CheckTrue(codex_line.Text.Contains("图鉴奖励"), $"写清是图鉴奖励：{codex_line.Text}");
            CheckTrue(
                codex_line.Text.Contains("已收集 1 部"),
                $"开局已收集 1 部（起始武学）：{codex_line.Text}");
        }
    }

    /// <summary>武学装配：面板上的「装配／卸下」按钮写回存档，卸下后战斗就没这一招了。</summary>
    private void _CheckSkillLoadout(CharacterScreen screen, GodotObject state)
    {
        // 武学现在是第 3 个页签（下标 2）——行囊挪到第 4 页之后它往前挪了一位
        screen.select_tab(UiNav.char_tab_of(UiNav.SKILL));
        var skill_section = screen.FindChild("SkillSection", true, false) as Control;
        CheckNotNull(skill_section, "有武学小节");
        if (skill_section != null)
        {
            // 标题那一行只写「武学」；招式槽／内功格那两个占用数在小节头右边的次要字里
            // （示意图的 `.lu-panel-h` ＋ `.lu-dim`——一个 12px 的字号档里，层级只能靠颜色与位置分）
            var title = skill_section.FindChild("SkillHeader", true, false) as Label;
            var hint = skill_section.FindChild("SectionHint", true, false) as Label;
            CheckTrue(title != null && title.Text.Contains("武学"), $"武学小节有标题：{title?.Text}");
            CheckTrue(hint != null && hint.Text.Contains("招式"), $"小节头写清招式槽：{hint?.Text}");
            CheckTrue(hint != null && hint.Text.Contains("内功"), $"小节头写清内功容量：{hint?.Text}");
        }

        const string skill_id = "sk_xuanwei_01";
        var button = screen.FindChild($"LoadoutButton{skill_id}", true, false) as Button;
        CheckNotNull(button, "起始武学有装配按钮");
        if (button == null)
        {
            return;
        }

        CheckTrue(button.Text == "卸下", $"已装配的显示「卸下」：{button.Text}");
        button.EmitSignal(Button.SignalName.Pressed);
        CheckEq(
            ((GameState)state).loadout_of(CharId)["active"].AsStringArray().Length, 0,
            "卸下后招式槽空了");
        CheckTrue(screen.status_text().Contains("卸下"), $"状态栏提示卸下：{screen.status_text()}");

        var again = screen.FindChild($"LoadoutButton{skill_id}", true, false) as Button;
        CheckNotNull(again, "卸下后按钮还在");
        if (again != null)
        {
            CheckTrue(again.Text == "装配", $"卸下后按钮变成「装配」：{again.Text}");
            again.EmitSignal(Button.SignalName.Pressed);
        }

        CheckEq(
            ((GameState)state).loadout_of(CharId)["active"].AsStringArray().Length, 1,
            "再点一下就装回去");
        CheckTrue(screen.status_text().Contains("装配"), $"状态栏提示装配：{screen.status_text()}");

        // 装不上的武学（武器不符／门槛不足）按钮要置灰
        var game_state = (GameState)state;
        if (!game_state.is_learned(CharId, "sk_wudu_01"))
        {
            game_state.learn_skill(CharId, "sk_wudu_01");
        }

        screen.refresh();
        var fist = screen.FindChild("LoadoutButtonsk_wudu_01", true, false) as Button;
        CheckNotNull(fist, "拳法出现在武学列表里");
        if (fist != null)
        {
            CheckTrue(fist.Disabled, "剑客装不了拳法，按钮置灰");
        }

        // 醉里乾坤·醉步（整行 0、target_type=self，但表里配了 on_cast 发放）：它是**增益招式**，
        // 战斗里能用；面板要写清这一点（2026-10-03 起；在那之前写的是「用不出来」，见决策 221）。
        if (!game_state.is_learned(CharId, "sk_drunk_zuibu"))
        {
            game_state.learn_skill(CharId, "sk_drunk_zuibu");
        }

        screen.refresh();
        var empty_label = screen.FindChild("Skillsk_drunk_zuibu", true, false) as Label;
        CheckNotNull(empty_label, "醉步出现在武学列表里");
        if (empty_label != null)
        {
            CheckTrue(
                empty_label.Text.Contains("增益招式"),
                $"面板写清它是增益招式（施放给自己上 buff）：{empty_label.Text}");
        }

        // 内功的「特殊效果」设计还没给效果表：面板要如实标出来，而且**不能把 id 原样显示**出来
        if (!game_state.is_learned(CharId, "pf_drunk_02"))
        {
            game_state.learn_skill(CharId, "pf_drunk_02");
        }

        screen.refresh();
        var effect_label = screen.FindChild("Skillpf_drunk_02", true, false) as Label;
        CheckNotNull(effect_label, "忘忧出现在武学列表里");
        if (effect_label != null)
        {
            CheckTrue(
                effect_label.Text.Contains("暂未生效"),
                $"面板写清那条特殊效果暂未生效：{effect_label.Text}");
            CheckFalse(
                effect_label.Text.Contains("effect_"),
                $"不把表里的效果 id 原样甩给玩家：{effect_label.Text}");
        }
    }

    private void _CheckBack(CharacterScreen screen, Godot.Collections.Array back_calls)
    {
        // 2026-10-11 起「返回」不再是一个孤零零的按钮：**底栏最左边那个「地图」入口**就是它
        // （UI 清单 §一：底栏贴屏幕底、`Esc` 提示在底部说明行，示意图那一屏没有返回按钮）。
        // 没接控制器时（用例这条路）`nav_to(map)` 就是回退——所以这里按下去应当触发 `back_handler`。
        var back = screen.FindChild(UiNav.node_name_of(UiNav.MAP), true, false) as Button;
        CheckNotNull(back, "底栏有「地图」入口（＝离开这一层）");
        if (back != null)
        {
            back.EmitSignal(Button.SignalName.Pressed);
            CheckEq(back_calls.Count, 1, "底栏「地图」入口触发回退");
        }
    }

    /// <summary>顶栏右边那截常驻数字（铜钱）——2026-10-11 起它不再待在页面头那一行。</summary>
    private static string _MoneyText(CharacterScreen screen)
    {
        var top_bar = screen.FindChild("TopBar", true, false) as Control;
        var money = top_bar?.FindChild("Right", true, false) as Label;
        return money != null ? money.Text : "";
    }

    /// <summary>页签自己与每一页的最小尺寸（版式超预算时用来看是哪一页撑的）。</summary>
    private static string _PageMins(CharacterScreen screen)
    {
        var tabs = screen.FindChild("Tabs", true, false) as TabContainer;
        if (tabs == null)
        {
            return "缺";
        }

        var parts = new System.Collections.Generic.List<string>();
        Vector2 tabs_min = tabs.GetCombinedMinimumSize();
        parts.Add($"Tabs={tabs_min.X:0}x{tabs_min.Y:0}");
        foreach (Node child in tabs.GetChildren())
        {
            var control = child as Control;
            if (control != null)
            {
                Vector2 min = control.GetCombinedMinimumSize();
                parts.Add($"{child.Name}={min.X:0}x{min.Y:0}");
            }
        }

        return string.Join("／", parts);
    }

    /// <summary>
    /// 4 人满员 ＋ 长名字 ＋ 满级 ＋ 六位数铜钱的版式绊线。
    ///
    /// 现表只有 1 个角色模板，而设计要 3–4 人——也就是说**多人下的界面从来没被量过**：
    /// 成员列表每多一人就多一行按钮、长名字会撑宽左侧栏，而「塞不下」在界面代码里一声不吭
    /// （只会被屏幕裁掉）。这里复制模板行造 4 人，把「整页仍塞进 1152×648」钉死，
    /// 等策划补行时不会第一次就踩雷。
    /// </summary>
    private void _CheckPartyStress(TableDb db)
    {
        TableDb wide = TableWithNChars(4, 2, true);
        GodotObject state = PartyState(wide, 4);
        var game_state = (GameState)state;
        foreach (Variant entry in game_state.char_ids)
        {
            game_state.char_levels[entry.AsString()] = 20;
        }

        _InventoryOf(state).money = 999999;
        game_state.party_exp = 9876543;

        CharacterScreen screen = _MakeScreen(state);
        Tree.Root.AddChild(screen);
        screen.setup();

        int members = 0;
        foreach (Node node in screen.FindChildren("MemberButton*", "Button", true, false))
        {
            members += 1;
        }

        CheckEq(members, 4, "4 人队伍在成员列表里各有按钮（多人界面分支以前没跑过）");
        var header = screen.FindChild("Header", true, false) as Label;
        CheckNotNull(header, "满员时头部信息还在");
        if (header != null)
        {
            CheckTrue(header.Text.Contains("经验"), $"满员时头部仍然写队伍经验：{header.Text}");
        }

        CheckTrue(
            _MoneyText(screen).Contains("999999"),
            $"六位数铜钱照常显示（顶栏）：{_MoneyText(screen)}");

        bool laid_out = LayoutBudget.fits(screen);
        if (!laid_out)
        {
            // 失败时把「整屏那一列的直接子节点」各块的最小尺寸一并打出来：整屏那行只给一个总数，
            // 看不出是哪一块撑破的（2026-10-11 就是靠它一眼认出「页面头那个自动换行标签在宽度
            // 还没有的时候，会按一个字一行去算高度」）。
            var parts = new System.Collections.Generic.List<string>();
            Control column = LayoutBudget.measure_target(screen);
            if (column != null)
            {
                foreach (Node child in column.GetChildren())
                {
                    var control = child as Control;
                    if (control != null)
                    {
                        Vector2 min = control.GetCombinedMinimumSize();
                        parts.Add($"{child.Name}={min.X:0}x{min.Y:0}");
                    }
                }
            }

            Fail("4 人满员 + 长名字下版式超预算：" + LayoutBudget.ascii_line(screen)
                + "　分块：" + string.Join("／", parts)
                + "　页：" + _PageMins(screen));
        }

        CheckTrue(laid_out, "4 人满员 + 长名字仍塞进设计分辨率");

        // 成员行是 Flow 容器（2026-10-04 第 4 屏排版时从 HBox 换过来的）：装不下就**换行**，
        // 而滚动区的横向滚动是关着的——HBox 时代行宽超过设计宽度，后面的成员就被裁掉。
        // 这里量成员行自己的最小宽度——**不用等帧就准**。
        // 外层那条 `fits()` 量不到它：`get_combined_minimum_size()` 到 ScrollContainer 就断了。
        var members_node = screen.FindChild("Members", true, false) as Control;
        CheckNotNull(members_node, "有成员行（HBox）");
        CheckTrue(members_node is HFlowContainer, "成员行是 Flow 容器：塞不下会换行，不会被右边裁掉");
        double members_width = members_node != null ? members_node.GetCombinedMinimumSize().X : -1.0;
        CheckLt(
            members_width, LayoutBudget.design_width(),
            $"4 人 + 长名字的成员行不超设计宽度（{members_width:0} px）");
        screen.select_tab(UiNav.char_tab_of(UiNav.BAG));
        CheckEq(screen.current_tab(), UiNav.char_tab_of(UiNav.BAG), "满员下也能切到行囊页");
        CheckNotNull(screen.sheet(), "满员下选中的角色仍有数据面板");
        _DropScreen(screen);
    }

    private static CharacterScreen _MakeScreen(GodotObject state)
    {
        var screen = GD.Load<PackedScene>(UiScene).Instantiate() as CharacterScreen;
        screen.state_override = state;
        return screen;
    }

    private void _DropScreen(Node screen)
    {
        Tree.Root.RemoveChild(screen);
        screen.Free();
    }

    private static Inventory _InventoryOf(GodotObject state) => state.Get("inventory").As<Inventory>();
}
