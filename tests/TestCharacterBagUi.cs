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
