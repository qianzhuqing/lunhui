using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// `ShopScreen` 的**买入页与底部操作条**（UI 清单 §二-9）——从 `ShopScreen.cs` 拆出来的 partial
/// （那份要守 800 行硬上限，这边只放「货架行 ＋ 数量选择器 ＋ 合计 ＋ 确认购买」）。
///
/// 价钱与判定全在 `src/core/ShopService.cs`：这边只按 `can_buy()` 的返回（`total`／`short`／`error`）
/// 摆字，**不自己算第二份价钱**——改价、改库存规则时面板跟着变，不会只改一半。
/// </summary>
public partial class ShopScreen
{
    /// <summary>
    /// 底部那条「数量选择器 ＋ 合计 ＋ 确认购买」（清单 §二-9）：`−`／`＋` 到上限停、
    /// 中段显示当前数量、右侧写「合计 N 文」；买不起时**确认购买置灰并把差多少写出来**
    /// （不隐藏、不让人点了才挨报错）。数量那截四个页签共用（卖出也按这个数量卖）。
    /// </summary>
    private void _refresh_bar()
    {
        int current = quantity();
        if (_qty_label != null)
        {
            _qty_label.Text = $"×{current}";
        }

        if (_qty_minus != null)
        {
            _qty_minus.Disabled = current <= 1;
        }

        if (_qty_plus != null)
        {
            _qty_plus.Disabled = current >= qty_max();
        }

        bool is_buy = tab == "buy";
        if (_total_label != null)
        {
            _total_label.Visible = is_buy;
        }

        if (_confirm_button != null)
        {
            _confirm_button.Visible = is_buy;
        }

        if (!is_buy || _total_label == null || _confirm_button == null)
        {
            return;
        }

        if (selected_item.Length == 0)
        {
            _total_label.Text = "合计 0 文（先在货架上点一行）";
            _confirm_button.Disabled = true;
            _confirm_button.Text = "确认购买";
            _confirm_button.TooltipText = "先在货架上点一行";
            return;
        }

        Godot.Collections.Dictionary check = shop.can_buy(building_id, selected_item, current);
        int total = _i(check, "total");
        int shortfall = _i(check, "short");
        _total_label.Text = shortfall > 0
            ? $"合计 {total} 文　还差 {shortfall} 文"
            : $"合计 {total} 文";
        _confirm_button.Disabled = !_b(check, "ok");
        _confirm_button.Text = "确认购买";
        _confirm_button.TooltipText = _b(check, "ok")
            ? $"花 {total} 文买 {shop.item_name(selected_item)} ×{current}"
            : _s(check, "error");
    }

    private void _rebuild_buy()
    {
        Godot.Collections.Array rows = shop.stock_rows(building_id);
        if (rows.Count == 0)
        {
            _list.AddChild(_make_label("Empty", "这家店没有货架"));
            return;
        }

        int shown = 0;
        foreach (Variant row_variant in rows)
        {
            GodotObject row = row_variant.AsGodotObject();
            string item_id = row.Get("item_id").AsString();
            if (row.Get("buy_price").AsInt32() <= 0)
            {
                continue;
            }

            shown += 1;
            _list.AddChild(_build_buy_row(row, item_id));
        }

        if (shown == 0)
        {
            _list.AddChild(_make_label("Empty", "这家店没有货架"));
        }
    }

    /// <summary>
    /// 货架一行（清单 §二-9：图标／名／`类型 · 效果`／单价／持有 M／限量 ＋ **点一行＝选中**）。
    /// 买不起**不隐藏**：行里直接写「还差 N 文」——钱不够时玩家第一眼要知道差多少，而不是点下去挨一句报错。
    /// </summary>
    private Control _build_buy_row(GodotObject row, string item_id)
    {
        int unit = row.Get("buy_price").AsInt32();
        var line = new HBoxContainer { Name = "BuyRow" + item_id };
        line.AddThemeConstantOverride("separation", 8);
        bool is_equip = ((TableDb)db).get_row("equip_base", item_id) != null;
        string icon_path = is_equip
            ? ItemText.equip_icon_path((TableDb)db, item_id)
            : ItemText.item_icon_path((TableDb)db, item_id);
        TextureRect icon = IconPaths.make_icon(icon_path, "BuyIcon" + item_id);
        if (icon != null)
        {
            line.AddChild(icon);
        }

        string text = $"{shop.item_name(item_id)}　{_stock_kind_text(item_id)}"
            + $"　{unit} 文/个　持有 {_inventory().count(item_id)}";
        int left = shop.remaining(building_id, item_id);
        if (left >= 0)
        {
            text += $"　（限量：还剩 {left}）";
        }

        Godot.Collections.Dictionary check = shop.can_buy(building_id, item_id, quantity());
        int shortfall = _i(check, "short");
        if (shortfall > 0)
        {
            text += $"　还差 {shortfall} 文";
        }
        else if (!_b(check, "ok"))
        {
            text += $"　— {_s(check, "error")}";
        }

        if (item_id == selected_item)
        {
            text = "▶ " + text;
        }

        Label label = _make_label("BuyLabel" + item_id, text, 0, _item_color(item_id));
        // 不设 EXPAND：中文在 HBox 里的最小宽度只有一个字，会被挤成一列（踩过）
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(label);
        string captured = item_id;
        line.AddChild(_make_button(
            "BuySelect" + item_id,
            item_id == selected_item ? "已选" : "选中",
            () => select_buy_item(captured)));
        // 悬停＝物品／装备小卡（清单 §二-9 ＋ §二-12）：这一行的字里已经有类型与价，
        // 小卡补的是「这件东西是什么」（部位／品阶／属性行／说明）。**挂整行**而不是挂那个 Label
        // ——行里还有图标与「选中」按钮，挂 Label 的话鼠标一挪到图标上卡就没了。
        _hover().watch(line, () => _shop_hover_rows(item_id, unit));
        return line;
    }

    /// <summary>
    /// 货架一行悬停要说的字（清单 §二-12 的物品卡／装备卡）。尾截写**单价与持有**
    /// ——货架上不写「剩 N」（那会被读成"货架还剩 N 件"，限量明明是另一行在报）。
    /// 数据走 `HoverCardText.of_base()`：与 NPC 面板那三处同一套读法，不各查一遍三张表。
    /// </summary>
    private string[] _shop_hover_rows(string item_id, int unit)
    {
        return HoverCardText.of_base(
            (TableDb)db, item_id, $"单价 {unit} 文 · 持有 {_inventory().count(item_id)}",
            "左键选中 · 底部「确认购买」");
    }

    /// <summary>这一屏共用的那张悬停小卡（第一次用到时才建，挂在本屏根节点上）。</summary>
    private HoverCard _hover() => _hover_card ??= HoverCard.make(this, "HoverCard");

    private HoverCard _hover_card;

    /// <summary>货架行里那截「类型 · 效果」：装备按部位 ＋ 品阶，道具按类型 ＋ 说明。</summary>
    private string _stock_kind_text(string item_id)
    {
        TableDb table_db = (TableDb)db;
        Resource equip = table_db.get_row("equip_base", item_id);
        if (equip != null)
        {
            Resource slot_row = table_db.get_row("equip_slot_def", equip.Get("slot").AsString());
            string slot_name = slot_row != null
                ? slot_row.Get("name_cn").AsString()
                : equip.Get("slot").AsString();
            Resource rarity_row = table_db.get_row("rarity_def", equip.Get("rarity").AsString());
            string rarity_name = rarity_row != null
                ? rarity_row.Get("name_cn").AsString()
                : equip.Get("rarity").AsString();
            // 装备的「效果」是词条：店里卖的只有白板，所以这里写部位 · 品阶（与行囊页同一套读法）
            return $"{slot_name} · {rarity_name}";
        }

        var item = table_db.get_row("item_base", item_id) as ItemBaseRow;
        if (item == null)
        {
            return "";
        }

        // `item_base` 还没有效果数值列（Q33）——如实写「效果待补」，编一个数出来更糟
        string effect = item.desc.Length > 0 ? item.desc : "效果待补";
        return $"{ItemText.type_name(item.item_type)} · {effect}";
    }

    /// <summary>这家店的货架上有没有这件东西（选中行被买光／换了店之后要清掉，别对着空气报价）。</summary>
    private bool _stock_has(string item_id)
    {
        foreach (Variant row_variant in shop.stock_rows(building_id))
        {
            if (row_variant.AsGodotObject().Get("item_id").AsString() == item_id)
            {
                return true;
            }
        }

        return false;
    }
}
