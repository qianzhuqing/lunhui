using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 角色与行囊界面的**行囊那一半**：行囊页（格子／详情／丢弃）＋ 秘籍研读——从
/// `TestCharacterUi.cs` 拆出来的 partial（那一份 2026-10-12 顶到了 800 行硬上限，
/// **只挪位置不改行为**）。夹具与选人／装备／武学那些检查仍在 `TestCharacterUi.cs`。
/// </summary>
public partial class TestCharacterUi
{
    private void _CheckBagTab(TableDb db, CharacterScreen screen, GodotObject state)
    {
        screen.select_tab(UiNav.char_tab_of(UiNav.BAG));
        Inventory inventory = _InventoryOf(state);
        inventory.add_item(db, "item_herb", 2);
        inventory.add_item(db, "item_wine_gourd", 1);
        screen.refresh();

        // 格子网格：一格一件，堆叠数压在右下角那个小徽标上（清单 §二-2）
        CheckNotNull(screen.FindChild("ItemCellitem_herb", true, false), "网格里有草药那一格");
        var herb_badge = screen.FindChild("ItemCountitem_herb", true, false) as Label;
        CheckNotNull(herb_badge, "草药有堆叠数徽标");
        if (herb_badge != null)
        {
            CheckEq(herb_badge.Text, "2", "徽标写的就是堆叠数量");
        }

        CheckNull(
            screen.FindChild("ItemCountitem_wine_gourd", true, false),
            "只有 1 件时不摆堆叠数（清单 §二-2 那一行）");

        // 动作收在右侧详情里：先选中那一格，再看主／次两个按钮
        //（旧版是每一行自己带一份按钮，同一件事会有两个入口）
        screen.select_bag_entry("item:item_herb");
        CheckEq(
            (screen.FindChild("BagUseButton", true, false) as Button)?.Text, "查看",
            "没有 use_context 的材料，主动作是「查看」");
        var drop_herb = screen.FindChild("BagDropButton", true, false) as Button;
        CheckNotNull(drop_herb, "材料可以丢弃");

        screen.select_bag_entry("item:item_wine_gourd");
        var drop_key = screen.FindChild("BagDropButton", true, false) as Button;
        CheckNotNull(drop_key, "钥匙道具也在网格里");
        if (drop_key != null)
        {
            CheckTrue(drop_key.Disabled, "钥匙道具的丢弃按钮被禁用");
        }

        var drop_reason = screen.FindChild("BagDropReason", true, false) as Label;
        CheckTrue(
            drop_reason != null && drop_reason.Text.Contains("钥匙"),
            "置灰的丢弃按钮下面写明原因");

        Godot.Collections.Dictionary refused = screen.drop_item("item_wine_gourd");
        CheckFalse(refused["ok"].AsBool(), "钥匙道具丢弃被拒");
        CheckTrue(screen.status_text().Contains("钥匙"), "状态栏说明拒绝原因");

        screen.select_bag_entry("item:item_herb");
        drop_herb = screen.FindChild("BagDropButton", true, false) as Button;
        if (drop_herb != null)
        {
            drop_herb.EmitSignal(Button.SignalName.Pressed);
            CheckEq(inventory.count("item_herb"), 1, "丢弃材料数量减一");
        }

        // 分类筛选（点左边那一列的标签＝换分类，网格里只剩这一档）
        screen.set_filter("material");
        CheckNotNull(screen.FindChild("ItemCellitem_herb", true, false), "材料分类下能看到草药");
        CheckTrue(
            screen.FindChild("ItemCellitem_wine_gourd", true, false) == null, "钥匙被筛掉");

        // 「钥匙」按**设计语义**筛（is_key_item），不是按 item_type=key：
        // 05 把铁镐列进钥匙道具，而它的 item_type 是 tool——以前它哪个分类都进不去
        inventory.add_item(db, "item_pickaxe", 1);
        screen.refresh();
        screen.set_filter("key");
        CheckNotNull(
            screen.FindChild("ItemCellitem_wine_gourd", true, false), "钥匙分类下有酒葫芦");
        CheckNotNull(
            screen.FindChild("ItemCellitem_pickaxe", true, false),
            "钥匙分类下有铁镐（item_type=tool，但它是钥匙道具）");
        CheckTrue(screen.FindChild("ItemCellitem_herb", true, false) == null, "材料不在钥匙分类里");
        screen.set_filter("all");
    }

    /// <summary>
    /// 道具使用弹窗（UI 清单 §二-3）：行囊里点「使用」＝**开弹窗**（不是当场用掉）；
    /// 目标行队伍每人一行（**空位不摆**）、预览随选中目标变、「使用」置灰并把原因写在按钮下面、
    /// 「取消」与 `Esc` 都只回行囊（不扣物品、不退整个面板）。
    ///
    /// 两味 `use_context=field` 的药是这一屏的对象；「使用」现在恒置灰，因为 `item_base`
    /// 还没有效果数值列（Q33）——**与战斗里那六指令同一口径**，不解析中文、不自己编数。
    /// </summary>
    private void _CheckUseItemModal(TableDb db, CharacterScreen screen, GodotObject state)
    {
        screen.select_tab(UiNav.char_tab_of(UiNav.BAG));
        screen.set_filter("all");
        Inventory inventory = _InventoryOf(state);
        inventory.add_item(db, "item_med_01", 2);
        screen.refresh();
        screen.select_bag_entry("item:item_med_01");

        var use = screen.FindChild("BagUseButton", true, false) as Button;
        CheckNotNull(use, "战斗外能用的药有「使用」");
        CheckNull(screen.FindChild("UseModal", true, false), "弹窗是懒建的：没点之前连节点都没有");
        if (use == null)
        {
            return;
        }

        CheckEq(use.Text, "使用", "动词是「使用」");
        CheckFalse(use.Disabled, "field 那类药点得动——置灰的是弹窗里那一枚（数值列还没配）");
        use.EmitSignal(Button.SignalName.Pressed);
        CheckTrue(screen.use_modal_open(), "点「使用」＝开弹窗");
        CheckEq(inventory.count("item_med_01"), 2, "开弹窗不扣物品");
        var modal = screen.FindChild("UseModal", true, false) as Control;
        CheckNotNull(modal, "弹窗建出来了");
        if (modal == null)
        {
            return;
        }

        var med = db.get_row("item_base", "item_med_01") as ItemBaseRow;
        CheckNotNull(med, "表里有两味战斗外的药");
        if (med != null)
        {
            CheckEq(
                (screen.FindChild("UseTitle", true, false) as Label)?.Text, $"使用 · {med.name_cn}",
                "标题栏写「使用 · 名字」");
        }

        CheckEq(
            (screen.FindChild("UseHeld", true, false) as Label)?.Text, "持有 2", "标题栏右边写持有数");

        // 目标行：队伍几人摆几行，**空位不摆**（摆一行灰的"空位"只会让玩家去点它）
        int members = state.Get("char_ids").AsStringArray().Length;
        var rows = screen.FindChildren("UseTargetRow*", "Button", true, false);
        CheckEq(rows.Count, members, $"队伍 {members} 人就摆 {members} 行");
        CheckTrue(
            ((screen.FindChild($"UseTargetName{CharId}", true, false) as Label)?.Text ?? "").StartsWith("▶"),
            "单人队默认就选中他（那一行有 ▶）");

        CheckTrue(
            ((screen.FindChild("UsePreviewEffect", true, false) as Label)?.Text ?? "").Contains("效果"),
            "预览有「效果」那一行（数值列没配就摆表里那句文字）");
        CheckTrue(
            ((screen.FindChild("UsePreviewCost", true, false) as Label)?.Text ?? "").Contains("剩 1"),
            "消耗那一行写用掉之后还剩几件");

        // 气血满＝预览写明「浪费」（清单 §二-3 那一格）
        var sheet = screen.sheet();
        ((GameState)state).char_hp[CharId] = sheet.max_hp();
        screen.select_use_target(CharId);
        CheckTrue(
            ((screen.FindChild("UsePreviewHp", true, false) as Label)?.Text ?? "").Contains("浪费"),
            "气血已满就写明再用是浪费");

        // 不满：读得出当前值，但**算不出到多少就直说**（不编数字）
        ((GameState)state).char_hp[CharId] = 5;
        screen.select_use_target(CharId);
        string hp_line = (screen.FindChild("UsePreviewHp", true, false) as Label)?.Text ?? "";
        CheckTrue(hp_line.Contains("5"), $"气血那一行读当前值：{hp_line}");
        // Q33 三列落表之后（2026-10-13），这一行是**算得出来**的：清单要的「气血 94 → 180（+86，上限截断）」，
        // 而且与真扣下去同一处口径（`ItemUse.preview_field`）——不是界面自己算的第二个数
        CheckTrue(
            hp_line.Contains("→") && hp_line.Contains("+120"),
            $"预览写出「气血 5 → 125（+120）」这种结果：{hp_line}");
        CheckEq(
            (screen.FindChild($"UseTargetState{CharId}", true, false) as Label)?.Text, "—",
            "没满的目标状态词是「—」（战斗外没有状态模型）");

        var confirm = screen.FindChild("UseConfirm", true, false) as Button;
        CheckNotNull(confirm, "弹窗里有「使用」");
        if (confirm != null)
        {
            // Q33 三列落表之后这件**有**效果（`heal_hp = 120`），所以「使用」点得动
            CheckFalse(confirm.Disabled, "配了效果的道具，「使用」点得动");
        }

        CheckEq(
            (screen.FindChild("UseBlockReason", true, false) as Label)?.Text, "",
            "配了效果就不再写「还没接」那一句（只有真没配的才置灰并说明）");

        var cancel = screen.FindChild("UseCancel", true, false) as Button;
        CheckNotNull(cancel, "弹窗里有「取消」");
        cancel?.EmitSignal(Button.SignalName.Pressed);
        CheckFalse(screen.use_modal_open(), "「取消」关掉弹窗");
        CheckEq(inventory.count("item_med_01"), 2, "取消不扣物品");

        // 真用一口：气血从 5 回到 125（**上限截断**、与预览同一个数），并**扣掉一件**
        ((GameState)state).char_hp[CharId] = 5;
        use.EmitSignal(Button.SignalName.Pressed);
        CheckTrue(screen.use_modal_open(), "再开一次");
        var use_confirm = screen.FindChild("UseConfirm", true, false) as Button;
        use_confirm?.EmitSignal(Button.SignalName.Pressed);
        CheckEq(((GameState)state).char_hp[CharId].AsInt32(), 125, "「使用」真回了 120 气血（5 → 125）");
        CheckEq(inventory.count("item_med_01"), 1, "用掉一件");
        CheckTrue(screen.status_text().Contains("气血"), $"底部说明播报这一口的结果：{screen.status_text()}");
        CheckFalse(screen.use_modal_open(), "用完之后弹窗收掉（别留一张指旧数据的卡）");

        // `Esc`：弹窗开着时只关它（清单 §二-3「关闭＝回到行囊」），关着时照旧退面板
        var backs = new Godot.Collections.Array();
        screen.back_handler = Callable.From(() => backs.Add(true));
        use.EmitSignal(Button.SignalName.Pressed);
        screen._UnhandledInput(new InputEventAction { Action = "ui_cancel", Pressed = true });
        CheckFalse(screen.use_modal_open(), "`Esc` 先关弹窗");
        CheckEq(backs.Count, 0, "关弹窗那一下**不**退面板");
        screen._UnhandledInput(new InputEventAction { Action = "ui_cancel", Pressed = true });
        CheckEq(backs.Count, 1, "弹窗关着时 `Esc` 照旧退面板（那一层没被挡住）");
        screen.set_filter("all");
    }

    /// <summary>秘籍研读：门槛不够按钮置灰并写原因；够门槛时点一下就学会并消耗一本。</summary>
    private void _CheckSkillbook(TableDb db, CharacterScreen screen, GodotObject state)
    {
        screen.select_tab(UiNav.char_tab_of(UiNav.BAG));
        screen.set_filter("skillbook");
        _InventoryOf(state).add_item(db, "item_scroll_wudu", 1);
        screen.refresh();
        screen.select_bag_entry("item:item_scroll_wudu");
        var button = screen.FindChild("BagUseButton", true, false) as Button;
        CheckNotNull(button, "残页有研读按钮");
        if (button == null)
        {
            return;
        }

        CheckEq(button.Text, "研读", "秘籍的主动作是「研读」（不是「使用」）");
        // 1 级书生智 10 < 12：按钮置灰，原因写在按钮下面那一行
        CheckTrue(button.Disabled, "智不够时研读按钮置灰");
        var label = screen.FindChild("BagUseReason", true, false) as Label;
        CheckNotNull(label, "置灰的研读按钮下面写了原因");
        if (label != null)
        {
            CheckTrue(label.Text.Contains("门槛"), $"写明研读不行的原因：{label.Text}");
        }

        // 点数全塞智，把它抬到 12
        while (screen.sheet().total_attrs()["int"].AsInt32() < 12 && screen.sheet().available_points() > 0)
        {
            screen.press_plus("int");
        }

        screen.refresh();
        button = screen.FindChild("BagUseButton", true, false) as Button;
        CheckFalse(button.Disabled, "智 12 后研读按钮可点");
        button.EmitSignal(Button.SignalName.Pressed);
        var game_state = (GameState)state;
        CheckTrue(game_state.is_learned(CharId, "sk_wudu_03"), "研读后学会五毒掌·蚀骨");
        CheckEq(_InventoryOf(state).count("item_scroll_wudu"), 0, "研读消耗一本");
        CheckTrue(screen.status_text().Contains("研读"), $"状态栏提示研读：{screen.status_text()}");
        screen.set_filter("all");
    }
}
