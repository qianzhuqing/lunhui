using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `ShopScreen` 的真实场景自检（`-- --shop-selftest`）。从 `ShopScreen.cs` 拆出来——
/// 那边已经贴到 800 行硬上限，而这一整段只被 `_Ready` 在带标志时调一次，与界面逻辑无关。
/// 自检条数的棘轮表盯着**本文件**（见 `tests/TestHandshakeFloors.cs` 的 `VerificationCheckFloors`）。
/// </summary>
public partial class ShopScreen
{
    // ------------------------------------------------------------------ 自检

    /// <summary>真实场景自检：换页签 → 买 → 卖 → 回购，账单要对得上</summary>
    private async void _run_shop_selftest()
    {
        SceneTree tree = _tree();
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        open_building("bld_smith");
        _inventory().money = 1000;
        refresh();
        lines.Add($"店：{building_name()}　铜钱 {shop.money()}");

        // 1. 四个页签都切一遍
        foreach (string[] entry in TABS)
        {
            select_tab(entry[0]);
        }

        select_tab("buy");
        ok = ok && tab == "buy";
        lines.Add($"页签切换 ok={_bool(tab == "buy")}");

        // 数量选择器 ＋ 合计 ＋ 确认购买（UI 清单 §二-9）：点一行＝选中、选择器到上限停、
        // 合计＝单价×数量、**买不起不隐藏而是把「还差 N」写出来**、确认购买置灰。
        select_buy_item("eq_sword_01");
        Button confirm = FindChild("ConfirmBuy", true, false) as Button;
        Label total_label = FindChild("TotalLabel", true, false) as Label;
        Label qty_label = FindChild("QtyLabel", true, false) as Label;
        int unit = shop.buy_price(building_id, "eq_sword_01");
        bool bar_ok = selected_item == "eq_sword_01" && confirm != null && !confirm.Disabled
            && total_label != null && total_label.Text == $"合计 {unit} 文"
            && qty_label != null && qty_label.Text == "×1";
        ok = ok && bar_ok;
        lines.Add($"点一行＝选中·合计＝单价×数量={_bool(bar_ok)}（{total_label?.Text}）");
        press_qty(1);
        press_qty(1);
        bool step_ok = quantity() == 3 && total_label.Text == $"合计 {unit * 3} 文";
        ok = ok && step_ok;
        lines.Add($"数量 ＋2 步到 3、合计跟着翻={_bool(step_ok)}（{qty_label.Text}／{total_label.Text}）");
        int keep_money = shop.money();
        int need = unit * quantity();
        _inventory().money = 1;
        refresh();
        Label poor_row = FindChild("BuyLabeleq_sword_01", true, false) as Label;
        bool poor_ok = poor_row != null && poor_row.Text.Contains($"还差 {need - 1}")
            && total_label.Text.Contains($"还差 {need - 1}") && confirm.Disabled;
        ok = ok && poor_ok;
        lines.Add($"买不起：不隐藏·写清还差多少·确认购买置灰={_bool(poor_ok)}（{poor_row?.Text}）");
        _inventory().money = keep_money;
        press_qty(-1);
        press_qty(-1);
        bool floor_ok = quantity() == 1 && press_qty(-1) == 1;
        ok = ok && floor_ok;
        lines.Add($"数量到 1 就停（不为 0／不绕圈）={_bool(floor_ok)}");
        // 真按那枚「确认购买」（不是直接调 `press_buy`）：按钮接线也一起过
        int before_button = shop.money();
        confirm.EmitSignal(Button.SignalName.Pressed);
        bool press_ok = shop.money() == before_button - unit;
        ok = ok && press_ok;
        lines.Add($"按「确认购买」真扣钱={_bool(press_ok)}（{before_button} → {shop.money()}）");

        // 2. 买入白板剑
        int money_before = shop.money();
        Godot.Collections.Dictionary bought = press_buy("eq_sword_01");
        ok = ok && _b(bought, "ok") && shop.money() == money_before - _i(bought, "spent");
        string bought_instance = "";
        Inventory inv = _inventory();
        foreach (string instance_id in inv.equipment_ids())
        {
            if (inv.base_of(instance_id) == "eq_sword_01")
            {
                bought_instance = instance_id;
            }
        }

        lines.Add($"买入 ok={_bool(_b(bought, "ok"))}（花 {_i(bought, "spent")} 文，余 {shop.money()}）");

        // 3. 卖回去（白板剑在铁匠铺的货架上）
        int earned = 0;
        if (bought_instance.Length > 0)
        {
            Godot.Collections.Dictionary sold = press_sell(bought_instance);
            ok = ok && _b(sold, "ok");
            earned = _i(sold, "earned");
            lines.Add($"卖出 ok={_bool(_b(sold, "ok"))}（收 {earned} 文）");
        }
        else
        {
            ok = false;
            lines.Add("卖出 ok=false（没买到剑）");
        }

        // 4. 回购列表买回来
        select_tab("buyback");
        int back_index = shop.buyback_rows(building_id).Count - 1;
        Godot.Collections.Dictionary back = press_buyback(back_index);
        ok = ok && _b(back, "ok") && _i(back, "cost") == earned;
        lines.Add($"回购 ok={_bool(_b(back, "ok"))}（花 {_i(back, "cost")} 文，应等于卖出价 {earned}）");

        // 5. 治疗页：先打伤一名队员，再按「治疗」看它真的回血（战斗外气血 v11 起进存档）
        open_building("bld_clinic");
        select_tab("service");
        string char_id = state.Get("char_ids").AsStringArray()[0];
        Godot.Collections.Dictionary preview = shop.heal_preview(building_id);
        int cap = preview["rows"].AsGodotArray()[0].AsGodotDictionary()["max_hp"].AsInt32();
        state.Call("set_current_hp", char_id, cap - 30);
        inv.money = System.Math.Max(inv.money, 200);
        select_tab("service");
        Button heal_button = FindChild("HealButton", true, false) as Button;
        ok = ok && heal_button != null && !heal_button.Disabled;
        Godot.Collections.Dictionary healed = press_heal();
        ok = ok && _b(healed, "ok") && _i(healed, "healed") == 30 && _i(healed, "cost") == 60;
        ok = ok && !state.Call("is_wounded", char_id).AsBool();
        lines.Add(
            $"治疗页 ok={_bool(_b(healed, "ok"))}（缺 30 点 → 花 {_i(healed, "cost")} 文，"
            + $"回 {_i(healed, "healed")} 点，治完满血={_bool(!state.Call("is_wounded", char_id).AsBool())}）");
        // 钱不够时「治疗」按钮要置灰、行下写明还差多少（不是「能点、点了才报错」）
        state.Call("set_current_hp", char_id, cap - 30);
        inv.money = 59;
        select_tab("service");
        Button poor_heal = FindChild("HealButton", true, false) as Button;
        Label poor_note = FindChild("HealNote", true, false) as Label;
        bool poor_ui = poor_heal != null && poor_heal.Disabled
            && poor_note != null && poor_note.Text.Contains("铜钱不够");
        ok = ok && poor_ui;
        lines.Add($"缺钱时治疗按钮置灰且写明原因={_bool(poor_ui)}（{(poor_note != null ? poor_note.Text : "-")}）");
        inv.money = System.Math.Max(inv.money, 200);
        select_tab("service");
        // 版式预算：整页最小高度要塞得进设计分辨率，塞不下时内容会被屏幕底部裁掉而代码一声不吭
        ok = ok && LayoutBudget.fits(this);
        lines.Add(LayoutBudget.ascii_line(this));
        ok = ok && LayoutBudget.has_opaque_backdrop(this);
        lines.Add(LayoutBudget.ascii_backdrop_line(this));
        // 滚动区内容宽度：横滚是关着的，清单一行宽了就被裁掉——而上面那行 LAYOUT 只量到面板外壳
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

        foreach (string line in lines)
        {
            GD.Print("  " + line);
        }

        GD.Print($"SHOP SELF-TEST: {(ok ? "OK" : "FAILED")}");
        if (tree != null)
        {
            tree.Quit(ok ? 0 : 1);
        }
    }
}
