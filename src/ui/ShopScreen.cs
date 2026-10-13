using System.Globalization;
using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// 商店界面。C# 版，对位原 `src/ui/shop_screen.gd`（576 行）。
///
/// 骨架在 `scenes/shop_screen.tscn` 里（节点名即接口，改版式改场景，代码只做绑定），
/// 规则与记账全在 `src/core/ShopService.cs`，这里只负责显示与转发。
///
/// 入口：清风驿（`scene_qingfengyi`）里走到 `Markers/Buildings/&lt;building_id&gt;` 旁边按 E。
/// 自检：`-- --shop-selftest`，买 → 卖 → 回购 → 切页签走一遍并按退出码报告。
///
/// **跨语言面**：`ShopService`／`GameState`／`Inventory`／`AffixRoller`／`TableDb` 全都已是 C#，直调。
/// 回调（`return_handler`）收 `Callable`（P5 起 GDScript 已清零，不再需要跨语言桥）。
/// </summary>
public partial class ShopScreen : CanvasLayer
{
    /// <summary>买卖数量的硬上限（限量商品再按「还剩几个」往下夹，见 `qty_max()`）</summary>
    public const int QTY_HARD_MAX = 99;

    /// <summary>
    /// 页签：key ↔ 场景里 `Tab&lt;后缀&gt;` 按钮名 ↔ 显示名（三处必须对齐，
    /// 见 `tests/test_handshake_docs.gd` 第 33 条——那条门限按 `{ "key", "后缀", "名称" }` 逐行解析，
    /// 别把中间那格删掉）。
    /// </summary>
    private static readonly string[][] TABS =
    {
        new[] { "buy", "Buy", "买入" },
        new[] { "sell", "Sell", "卖出" },
        new[] { "buyback", "Buyback", "回购" },
        new[] { "service", "Service", "服务" },
    };

    [Export] public Variant state_override = default;

    /// <summary>用例注入的表库（要在真场景里跑「限定货架」这种发行数据用不到的配置）</summary>
    [Export] public Variant db_override = default;

    /// <summary>返回回调（P5 起 GDScript 已清零，直接用 `Callable`）。</summary>
    [Export] public Callable return_handler;

    /// <summary>当前店（building_def.building_id）。调用方 `Set("building_id", …)`。</summary>
    [Export] public string building_id = "bld_smith";

    /// <summary>打开界面时停在哪个页签</summary>
    [Export] public string tab = "buy";

    /// <summary>当前买卖数量（清单 §二-9：选择器中段显示它；`−`／`＋` 改它）</summary>
    [Export] public int qty = 1;

    /// <summary>买入页选中的那一行（清单 §二-9「点一行＝选中」）；空＝还没选</summary>
    [Export] public string selected_item = "";

    private GodotObject db;
    private GodotObject state;
    private ShopService shop;

    private Label _title;
    private Label _money;
    private VBoxContainer _list;
    private Label _status;
    private Button _qty_minus;
    private Button _qty_plus;
    private Label _qty_label;
    private Label _total_label;
    private Button _confirm_button;
    private Button _return_button;
    private HBoxContainer _tabs_box;
    private bool _bound;

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--shop-selftest"))
        {
            Callable.From(_run_shop_selftest).CallDeferred();
        }
    }

    public void setup()
    {
        if (shop != null)
        {
            return;
        }

        db = _resolve_db();
        state = current_state();
        Node session_node = _session_node();
        if (state == null)
        {
            state = GameState.new_game((TableDb)db, "normal", System.Array.Empty<string>(), 0);
            if (session_node != null)
            {
                session_node.Call("set_state", state);
            }
        }

        shop = ShopService.create((TableDb)db, state);
        _bind_ui();
        refresh();
    }

    public GodotObject current_state()
    {
        if (state_override.VariantType != Variant.Type.Nil)
        {
            return state_override.AsGodotObject();
        }

        return _session_node()?.Get("state").AsGodotObject();
    }

    /// <summary>打开某家店（入口在 local_map_controller 里按 Marker 名传过来）</summary>
    public void open_building(string id)
    {
        building_id = id;
        tab = "buy";
        if (shop != null)
        {
            refresh();
        }
    }

    public string building_name() =>
        shop != null ? shop.building_name(building_id) : building_id;

    public string status_text() => _status != null ? _status.Text : "";

    /// <summary>给界面塞一句话（开店时用：「进店：铁匠铺」这类提示）</summary>
    public void show_message(string text) => _set_status(text);

    public string money_text() => _money != null ? _money.Text : "";

    public int quantity() => _clamped_qty();

    /// <summary>
    /// 当前这一件商品最多能选到几个：限量商品就是**还剩几个**（到上限停，别让玩家选一个买不到的
    /// 数量再挨一句报错），没限量／还没选中商品就是硬上限 `QTY_HARD_MAX`。
    /// </summary>
    public int qty_max()
    {
        if (shop == null)
        {
            return QTY_HARD_MAX;
        }

        if (tab == "buy" && selected_item.Length > 0)
        {
            int left = shop.remaining(building_id, selected_item);
            if (left >= 0)
            {
                return System.Math.Clamp(left, 1, QTY_HARD_MAX);
            }
        }

        return QTY_HARD_MAX;
    }

    private int _clamped_qty() => System.Math.Clamp(qty, 1, qty_max());

    // ------------------------------------------------------------------ 界面

    /// <summary>骨架在场景里，这里只查找与接线；商品行是按存档状态动态生成的</summary>
    private void _bind_ui()
    {
        if (_bound)
        {
            return;
        }

        _bound = true;
        _title = _require_node("Panel/Margin/Column/Title") as Label;
        _money = _require_node("Panel/Margin/Column/Money") as Label;
        _tabs_box = _require_node("Panel/Margin/Column/Tabs") as HBoxContainer;
        _list = _require_node("Panel/Margin/Column/Body/List") as VBoxContainer;
        _status = _require_node("Panel/Margin/Column/Status") as Label;
        _qty_minus = _require_node("Panel/Margin/Column/Buttons/QtyMinus") as Button;
        _qty_label = _require_node("Panel/Margin/Column/Buttons/QtyLabel") as Label;
        _qty_plus = _require_node("Panel/Margin/Column/Buttons/QtyPlus") as Button;
        _total_label = _require_node("Panel/Margin/Column/Buttons/TotalLabel") as Label;
        _confirm_button = _require_node("Panel/Margin/Column/Buttons/ConfirmBuy") as Button;
        _return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button;
        if (_qty_minus != null)
        {
            _qty_minus.Pressed += () => press_qty(-1);
        }

        if (_qty_plus != null)
        {
            _qty_plus.Pressed += () => press_qty(1);
        }

        if (_confirm_button != null)
        {
            _confirm_button.Pressed += () => press_buy(selected_item);
        }

        if (_return_button != null)
        {
            _return_button.Pressed += press_return;
        }

        foreach (string[] entry in TABS)
        {
            Button button = _tabs_box?.GetNodeOrNull($"Tab{entry[1]}") as Button;
            if (button != null)
            {
                string captured = entry[0];
                button.Pressed += () => select_tab(captured);
            }
        }

        if (_status != null)
        {
            _status.Text = "";
        }
    }

    /// <summary>场景里少一个节点就报出来（别让界面静悄悄缺一块）</summary>
    private Node _require_node(string path)
    {
        Node node = GetNodeOrNull(path);
        if (node == null)
        {
            GD.PushError($"shop_screen.tscn 缺少节点：{path}");
        }

        return node;
    }

    public void select_tab(string key)
    {
        tab = key;
        refresh();
    }

    /// <summary>数量选择器：`delta` 是 ＋1（`＋`）或 −1（`−`）；**到上下限就停**（不绕圈）。</summary>
    public int press_qty(int delta = 1)
    {
        qty = System.Math.Clamp(qty + delta, 1, qty_max());
        refresh();
        return quantity();
    }

    /// <summary>买入页点一行＝选中（清单 §二-9）：数量回到 1——上一件商品选到 10 个，
    /// 默默套到刚点开的另一件货上会让人误买。</summary>
    public void select_buy_item(string item_id)
    {
        selected_item = item_id;
        qty = 1;
        refresh();
    }

    public void press_return()
    {
        if (_callable_valid(return_handler))
        {
            _invoke_callback(return_handler);
            return;
        }

        QueueFree();
    }

    private void _set_status(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
        }
    }

    public void refresh()
    {
        if (shop == null || _list == null)
        {
            return;
        }

        _title.Text = $"{building_name()}（{_building_desc()}）";
        // 标题栏只留店名与持有铜钱（清单 §二-9）；买卖数量搬到选择器那一行去
        _money.Text = $"铜钱 {shop.money()}";
        _refresh_tabs();
        // 商品被买光／存档换了之后选中行可能已经不存在了——留着它会让「确认购买」对着空气算钱
        if (selected_item.Length > 0 && tab == "buy" && !_stock_has(selected_item))
        {
            selected_item = "";
        }

        _clear(_list);
        switch (tab)
        {
            case "sell":
                _rebuild_sell();
                break;
            case "buyback":
                _rebuild_buyback();
                break;
            case "service":
                _rebuild_service();
                break;
            default:
                _rebuild_buy();
                break;
        }

        _refresh_bar();
    }

    private string _building_desc()
    {
        Resource row = shop.building(building_id);
        return row != null ? row.Get("desc").AsString() : "";
    }

    private void _refresh_tabs()
    {
        foreach (string[] entry in TABS)
        {
            Button button = _tabs_box?.GetNodeOrNull($"Tab{entry[1]}") as Button;
            if (button != null)
            {
                button.Text = (tab == entry[0] ? "▶ " : "") + entry[2];
            }
        }
    }

    private void _rebuild_sell()
    {
        int shown = 0;
        Inventory inv = _inventory();
        TableDb table_db = (TableDb)db;
        // 材料与道具
        foreach (string item_id in inv.item_ids())
        {
            Resource row = table_db.get_row("item_base", item_id);
            if (row == null)
            {
                continue;
            }

            int unit = shop.sell_price(building_id, item_id);
            if (unit <= 0)
            {
                continue;
            }

            shown += 1;
            int count = inv.count(item_id);
            int sell_qty = System.Math.Min(quantity(), count);
            _list.AddChild(_make_sell_row(
                item_id,
                $"{row.Get("name_cn").AsString()} ×{count}　卖 {unit} 文/个",
                $"卖 {sell_qty} 个（+{unit * sell_qty} 文）",
                row.Get("is_key_item").AsBool(),
                item_id));
        }

        // 未穿戴的装备实例
        foreach (string instance_id in inv.equipment_ids())
        {
            if (inv.is_equipped(instance_id))
            {
                continue;
            }

            string base_id = inv.base_of(instance_id);
            int unit = shop.sell_price(building_id, base_id);
            if (unit <= 0)
            {
                continue;
            }

            shown += 1;
            Resource base_row = table_db.get_row("equip_base", base_id);
            string affix = _affix_summary(instance_id);
            _list.AddChild(_make_sell_row(
                instance_id,
                $"{base_row.Get("name_cn").AsString()}（{base_row.Get("rarity").AsString()}）{affix}　卖 {unit} 文",
                $"卖出（+{unit} 文）",
                false,
                instance_id));
        }

        if (shown == 0)
        {
            _list.AddChild(_make_label("Empty", "背包里没有这家店收的东西"));
        }
    }

    private Control _make_sell_row(string sell_id, string text, string button_text, bool disabled, string node_key)
    {
        var line = new HBoxContainer { Name = "SellRow" + node_key };
        line.AddThemeConstantOverride("separation", 8);
        Label label = _make_label("SellLabel" + node_key, text);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(label);
        string captured = sell_id;
        line.AddChild(_make_button(
            "SellButton" + node_key, button_text, () => press_sell(captured), disabled));
        return line;
    }

    private void _rebuild_buyback()
    {
        Godot.Collections.Array rows = shop.buyback_rows(building_id);
        if (rows.Count == 0)
        {
            _list.AddChild(_make_label("Empty", "还没有卖过东西给这家店"));
            return;
        }

        for (int index = 0; index < rows.Count; index++)
        {
            Godot.Collections.Dictionary entry = rows[index].AsGodotDictionary();
            var line = new HBoxContainer { Name = $"BuybackRow{index}" };
            line.AddThemeConstantOverride("separation", 8);
            int cost = _i(entry, "price") * System.Math.Max(1, _i(entry, "qty", 1));
            string text = $"{shop.item_name(_s(entry, "item_id"))}"
                + $" ×{_i(entry, "qty", 1)}　买回 {cost} 文";
            Godot.Collections.Array affixes = entry.ContainsKey("affixes")
                ? entry["affixes"].AsGodotArray()
                : new Godot.Collections.Array();
            if (affixes.Count > 0)
            {
                text += $"　词条：{AffixRoller.summarize((TableDb)db, affixes)}";
            }

            Label label = _make_label($"BuybackLabel{index}", text);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(label);
            int captured = index;
            line.AddChild(_make_button(
                $"BuybackButton{index}", $"买回（{cost} 文）", () => press_buyback(captured)));
            _list.AddChild(line);
        }
    }

    private void _rebuild_service()
    {
        if (!shop.has_heal(building_id))
        {
            _list.AddChild(_make_label("Empty", "这家店没有附加服务"));
            return;
        }

        var line = new HBoxContainer { Name = "HealRow" };
        line.AddThemeConstantOverride("separation", 8);
        Godot.Collections.Dictionary preview = shop.heal_preview(building_id);
        int missing = _i(preview, "missing");
        int cost = _i(preview, "cost");
        Label heal_label = _make_label(
            "HealLabel",
            $"治疗：每点气血 {_num(shop.heal_price(building_id))} 文｜"
            + $"全队缺 {missing} 点，要 {cost} 文（费用 = 缺失气血 × 单价）");
        heal_label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(heal_label);
        // 战斗外气血 v11 起进存档：缺多少、要多少钱都算得出来，钱够就能按
        line.AddChild(_make_button(
            "HealButton", "治疗", () => press_heal(), missing <= 0 || shop.money() < cost));
        _list.AddChild(line);
        Godot.Collections.Array rows = preview.ContainsKey("rows")
            ? preview["rows"].AsGodotArray()
            : new Godot.Collections.Array();
        foreach (Variant row_variant in rows)
        {
            Godot.Collections.Dictionary row = row_variant.AsGodotDictionary();
            int row_missing = _i(row, "missing");
            _list.AddChild(_make_label(
                $"Heal_{_s(row, "char_id")}",
                $"{_s(row, "name")}　气血 {_i(row, "hp")} / {_i(row, "max_hp")}"
                + (row_missing <= 0 ? "" : $"　缺 {row_missing}")));
        }

        if (missing <= 0)
        {
            _list.AddChild(_make_label("HealNote", "全队气血是满的。"));
        }
        else if (shop.money() < cost)
        {
            _list.AddChild(_make_label("HealNote", $"铜钱不够：要 {cost} 文。"));
        }
    }

    private static void _clear(Node box)
    {
        foreach (Node child in box.GetChildren())
        {
            // 先摘再 queue_free：名字要立刻可复用（同 cultivate_screen 的注释）
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Label _make_label(string node_name, string text, int font_size = 0, string color = "")
    {
        var label = new Label
        {
            Name = node_name,
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        if (font_size > 0)
        {
            label.AddThemeFontSizeOverride("font_size", font_size);
        }

        if (color.Length > 0)
        {
            label.AddThemeColorOverride("font_color", new Color(color));
        }

        return label;
    }

    private static Button _make_button(
        string node_name, string text, System.Action pressed, bool disabled = false)
    {
        var button = new Button
        {
            Name = node_name,
            Text = text,
            Disabled = disabled,
        };
        button.Pressed += pressed;
        return button;
    }

    private string _item_color(string item_id)
    {
        TableDb table_db = (TableDb)db;
        Resource equip = table_db.get_row("equip_base", item_id);
        if (equip != null)
        {
            Resource rarity = table_db.get_row("rarity_def", equip.Get("rarity"));
            return rarity != null ? rarity.Get("color").AsString() : "";
        }

        Resource item = table_db.get_row("item_base", item_id);
        if (item != null)
        {
            Resource item_rarity = table_db.get_row("rarity_def", item.Get("rarity"));
            return item_rarity != null ? item_rarity.Get("color").AsString() : "";
        }

        return "";
    }

    private string _affix_summary(string instance_id) =>
        AffixRoller.summarize((TableDb)db, _inventory().affixes_of(instance_id));

    // ------------------------------------------------------------------ 操作

    public Godot.Collections.Dictionary press_buy(string item_id)
    {
        Godot.Collections.Dictionary result = shop.buy(building_id, item_id, quantity());
        _set_status(_b(result, "ok")
            ? $"买入 {shop.item_name(item_id)} ×{quantity()}，花掉 {_i(result, "spent")} 文"
            : _s(result, "error"));
        refresh();
        return result;
    }

    public Godot.Collections.Dictionary press_sell(string sell_id)
    {
        Inventory inv = _inventory();
        int qty = inv.has_equipment(sell_id) ? 1 : System.Math.Min(quantity(), inv.count(sell_id));
        Godot.Collections.Dictionary result = shop.sell(building_id, sell_id, System.Math.Max(1, qty));
        _set_status(_b(result, "ok")
            ? $"卖出，收入 {_i(result, "earned")} 文"
            : _s(result, "error"));
        refresh();
        return result;
    }

    public Godot.Collections.Dictionary press_buyback(int index)
    {
        Godot.Collections.Dictionary result = shop.buy_back(building_id, index);
        if (!_b(result, "ok"))
        {
            _set_status(_s(result, "error"));
        }
        else
        {
            // 背包快满时只买回一部分：写清买到几个、剩下的还在列表里（别让玩家以为全买回来了）
            string text = $"买回 {shop.item_name(_s(result, "item_id"))}"
                + $" ×{_i(result, "received", 1)}，花掉 {_i(result, "cost")} 文";
            if (_b(result, "partial"))
            {
                text += "（背包放不下，剩下的还留在回购列表）";
            }

            _set_status(text);
        }

        refresh();
        return result;
    }

    public Godot.Collections.Dictionary press_heal()
    {
        Godot.Collections.Dictionary result = shop.heal_party(building_id);
        if (_b(result, "ok"))
        {
            _set_status($"治疗完成：回 {_i(result, "healed")} 点气血，花 {_i(result, "cost")} 文");
        }
        else
        {
            _set_status(_s(result, "error"));
        }

        refresh();
        return result;
    }

    // ------------------------------------------------------------------ 环境

    private GodotObject _resolve_db()
    {
        if (db_override.VariantType != Variant.Type.Nil)
        {
            return db_override.AsGodotObject();
        }

        GodotObject game_data = _session_node("GameData");
        GodotObject game_db = game_data?.Get("db").AsGodotObject();
        if (game_db != null && game_db.Get("tables").AsGodotDictionary().Count > 0)
        {
            return game_db;
        }

        var table_db = new TableDb();
        table_db.load_all();
        return table_db;
    }

    private Node _session_node(string node_name = "GameSession") => _tree()?.Root.GetNodeOrNull(node_name);

    /// <summary>不在场景树里时 `get_tree()` 会打一条 ERROR（`--script` 模式用例直接 new 节点就会踩到）。</summary>
    private SceneTree _tree()
    {
        if (IsInsideTree())
        {
            SceneTree tree = GetTree();
            if (tree != null)
            {
                return tree;
            }
        }

        return Engine.GetMainLoop() as SceneTree;
    }

    private static bool _has_user_arg(string flag) => System.Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;

    private Inventory _inventory() => state.Get("inventory").As<Inventory>();

    // ------------------------------------------------------------------ 小工具

    /// <summary>这个回调接上没有（空 Callable 与「根本没接」都算没接）。</summary>
    private static bool _callable_valid(Callable callable) =>
        callable.Delegate != null || (callable.Target != null && !callable.Method.IsEmpty);

    /// <summary>调这个回调（没接就不动）；返回值原样给出来，`sweep_handler` 那类要读它。</summary>
    private static Variant _invoke_callback(Callable callable, params Variant[] args)
    {
        if (!_callable_valid(callable))
        {
            return default;
        }

        return callable.Call(args);
    }

    /// <summary>对位 GDScript 的 `str(float)`：整数就写整数（显式走不变文化）。</summary>
    private static string _num(double value) =>
        value == System.Math.Floor(value) && !double.IsInfinity(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static int _i(Godot.Collections.Dictionary dict, string key, int fallback = 0) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static string _bool(bool value) => value ? "true" : "false";
}
