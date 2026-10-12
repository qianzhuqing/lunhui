using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 战斗结算面板（22 §1.6 第八版，2026-10-12）：压暗底上的一块整屏分区面板，自上而下六块——
/// 战果标题／经验（含逐人升级行）／铜钱／掉落格／武学结算／「确定」。
///
/// 从 `BattleScreenSettle.cs` 拆出来的一份 partial：那份算账与攒文案（`lines` 是**战报**，全量），
/// 这份把结果摆成六块。**逐人那行只写「升级」**（口径①）——经验是队伍池、属性靠加点，不照搬
/// 八方旅人「每人自动涨属性」；行文本与 `LevelService.describe` **同源**（「Lv a → b（+N 点）」
/// 这个格式不在别处再写一遍）。
///
/// 版式预算（口径②）：面板按区铺、每区不限行，**面板自身高 ≤ 设计高**（还要让开贴底那枚「确定」），
/// 真超出由滚动区**整块滚**；放不下的掉落照旧进战报（`SETTLE_DROP_ITEMS` 是面板里那两列的格数上限）。
/// 零收获（败北／平局／撤退，口径③）：经验／铜钱／掉落／升级**四块整块不建**，只剩标题＋说明＋「确定」。
/// </summary>
public partial class BattleScreen
{
    /// <summary>面板宽：设计画布 1152 的两侧各留 136，面板居中</summary>
    public const int SETTLE_PANEL_WIDTH = 880;

    /// <summary>面板最少也有这么高（只有标题＋一段说明时别压成一条）</summary>
    private const int SETTLE_PANEL_MIN_HEIGHT = 200;

    /// <summary>面板内边距：内容宽 ＝ 面板宽 − 2×它（同时也是背板的 content margin）</summary>
    private const int SETTLE_PANEL_PAD = 20;

    /// <summary>贴底那枚「确定」占掉的带高（场景里 `ResultConfirm` 的 `offset_top = -48`）</summary>
    private const double SETTLE_CONFIRM_OFFSET = 48.0;

    private Control _settle_root;
    private ColorRect _settle_dim;
    private Panel _settle_panel;
    private ScrollContainer _settle_scroll;
    private VBoxContainer _settle_column;

    /// <summary>
    /// 面板绑一次（`SettleRoot` 那棵树在场景里；六块内容是每次结算重建的）。
    /// 宽是定的、高随内容收，见 `_fit_settle_panel()`。
    /// </summary>
    private void _bind_settle_panel()
    {
        _settle_root = _require_node("SettleRoot") as Control;
        _settle_dim = _require_node("SettleRoot/SettleDim") as ColorRect;
        _settle_panel = _require_node("SettleRoot/SettlePanel") as Panel;
        _settle_scroll = _require_node("SettleRoot/SettlePanel/SettleScroll") as ScrollContainer;
        _settle_column = _require_node("SettleRoot/SettlePanel/SettleScroll/SettleColumn") as VBoxContainer;
        if (_settle_dim != null)
        {
            // 压暗底那一档色只在主题里（决策 375：色值不写第二份）
            _settle_dim.Color = UiKit.color("settle_dim");
        }

        if (_settle_panel != null)
        {
            _settle_panel.AddThemeStyleboxOverride("panel", _settle_panel_style());
            _settle_panel.OffsetLeft = -SETTLE_PANEL_WIDTH / 2.0f;
            _settle_panel.OffsetRight = SETTLE_PANEL_WIDTH / 2.0f;
        }

        if (_settle_scroll != null)
        {
            // 面板不是 Container（`Panel`），子节点得自己贴进去；内边距只有 `SETTLE_PANEL_PAD` 一处
            _settle_scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _settle_scroll.OffsetLeft = SETTLE_PANEL_PAD;
            _settle_scroll.OffsetTop = SETTLE_PANEL_PAD;
            _settle_scroll.OffsetRight = -SETTLE_PANEL_PAD;
            _settle_scroll.OffsetBottom = -SETTLE_PANEL_PAD;
        }

        if (_settle_root != null)
        {
            _settle_root.Visible = false;
        }

        _fit_settle_panel();
    }

    /// <summary>
    /// 面板背板：暗底 ＋ 一像素金边。**不用宣纸那件九宫格**——结算面板是战斗这一屏的东西，
    /// 底色系跟着战场走（22 §1.5：全游戏唯一压暗的一屏），金色标题也才压得住。
    /// </summary>
    private static StyleBoxFlat _settle_panel_style()
    {
        StyleBoxFlat style = _stage_box(UiKit.color("settle_panel"), 2, 1);
        style.BorderColor = UiKit.color("gold");
        return style;
    }

    /// <summary>
    /// 面板自身高的上限：设计高减掉贴底那枚「确定」占掉的一带。
    ///
    /// 面板是**居中**的，所以「到「确定」上缘」的可用高度要按两倍算（上下一半各占一半）——
    /// 不管内容多少，面板都不会压在自己的出口键上。量不到那枚钮的矩形（还没排版）就按场景里
    /// 那个 `offset_top` 算。
    ///
    /// 还要**再夹一道设计高**（§八 的验收就是「面板自身高 ≤ 648」）：自检与用例跑在
    /// 各不相同的窗口尺寸下（`_stage_size()` 跟着视口走），不夹这一道，换个高一点的视口
    /// 面板就高过设计画布了。
    /// </summary>
    private double _settle_max_height()
    {
        double world_h = _stage_size().Y;
        double confirm_top = world_h - SETTLE_CONFIRM_OFFSET;
        if (_result_confirm != null && IsInstanceValid(_result_confirm))
        {
            double measured = _result_confirm.GetGlobalRect().Position.Y;
            if (measured > 0.0)
            {
                confirm_top = measured;
            }
        }

        // ① 到贴底那枚「确定」的两倍距离（面板居中，撑到那儿就压住它了）
        double by_confirm = 2.0 * (confirm_top - (world_h / 2.0));
        // ② 设计高减掉那一带（视口比设计画布高也不许越线）
        double by_design = LayoutBudget.design_height() - SETTLE_CONFIRM_OFFSET;
        double limit = System.Math.Min(world_h, System.Math.Min(by_confirm, by_design));
        return System.Math.Max(SETTLE_PANEL_MIN_HEIGHT, limit);
    }

    /// <summary>面板高度收到内容高（不够就顶到上限；真超了由 `SettleScroll` 整块滚）。</summary>
    private void _fit_settle_panel()
    {
        if (_settle_panel == null || _settle_column == null || !IsInstanceValid(_settle_panel))
        {
            return;
        }

        double need = _settle_column.GetCombinedMinimumSize().Y + (SETTLE_PANEL_PAD * 2.0);
        double height = Mathf.Clamp(need, SETTLE_PANEL_MIN_HEIGHT, _settle_max_height());
        _settle_panel.OffsetTop = (float)(-height / 2.0);
        _settle_panel.OffsetBottom = (float)(height / 2.0);
    }

    /// <summary>
    /// 把结算结果摆成六块。`view` 的键：`title`／`harvest`／`exp`／`money`／`levels`（升级条目，每条
    /// 就 `LevelService.apply_available_levels()` 里那一项）／`drops`（格子：`icon` ＋ `text`）／
    /// `martial`（领悟／未习得／熟练度的行文本）／`notes`（说明与附注）。
    ///
    /// **整块重建**：这一屏打完就不再变，重建比增量刷新少一整类「上一场残留」的 bug。
    /// </summary>
    public void _refresh_settle_panel(Godot.Collections.Dictionary view)
    {
        if (_settle_column == null)
        {
            return;
        }

        _clear(_settle_column);
        _settle_column.CustomMinimumSize =
            new Vector2(SETTLE_PANEL_WIDTH - (SETTLE_PANEL_PAD * 2), 0);
        _add_settle_title(_s(view, "title"));
        if (_b(view, "harvest"))
        {
            // 口径③：零收获时这四块**整块不建**（不是建了再藏——藏起来的东西迟早会被别处数进去）
            _add_settle_exp(view);
            _add_settle_money(view);
            _add_settle_drops(view);
        }

        _add_settle_martial(view);
        _add_settle_notes(view);
        _fit_settle_panel();
        // 自动换行的 Label 要等排完版才有宽度，这一趟量到的内容高会偏小——排完版再收一次
        Callable.From(_fit_settle_panel).CallDeferred();
    }

    /// <summary>① 战果标题：居中大字 ＋ 上下各一条细金线（胜利／败北／平局／撤退）</summary>
    private void _add_settle_title(string title)
    {
        var box = new VBoxContainer { Name = "SettleTitleBox" };
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(_settle_rule("SettleRuleTop"));
        Label label = _settle_label("SettleTitle", title, 24, "gold");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(label);
        box.AddChild(_settle_rule("SettleRuleBottom"));
        _settle_column.AddChild(box);
    }

    /// <summary>
    /// ② 经验块：一行 `经验 +N`，**升级的人每人再占一行**（口径①：逐人那行只写升级、不写逐人经验）。
    /// 行文本 ＝ `▲ ` ＋ `LevelService.describe(单条)`——等级与加点的写法只有一个出处。
    /// </summary>
    private void _add_settle_exp(Godot.Collections.Dictionary view)
    {
        var box = new VBoxContainer { Name = "SettleExp" };
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(_settle_label("SettleExpRow", $"经验 +{_i(view, "exp")}", 12, "stage_text"));
        LevelService levels = LevelService.create(Db, state);
        int index = 0;
        foreach (Variant entry_variant in _arr_of(view, "levels"))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            var row = (Label)_settle_label(
                $"SettleLevelRow{index}",
                $"▲ {levels.describe(new Godot.Collections.Array { entry })}",
                12,
                "gold");
            // 用例靠它验「与 describe 同源」：行里那一项就是记账用的那一项，不是重算出来的
            row.SetMeta("level_up", entry);
            box.AddChild(row);
            index += 1;
        }

        _settle_column.AddChild(box);
    }

    /// <summary>③ 铜钱块：一行 `铜钱 +N`（色值跟着顶栏那个数字走）</summary>
    private void _add_settle_money(Godot.Collections.Dictionary view) =>
        _settle_column.AddChild(
            _settle_label("SettleMoneyRow", $"铜钱 +{_i(view, "money")}", 12, "topbar_money"));

    /// <summary>
    /// ④ 掉落块：图标（**有图才摆**，沿用旧兜底）＋ 名字 ＋ `×N`，**两列往外铺**（第八版不再压成一件）。
    /// 面板里只摆 `SETTLE_DROP_ITEMS` 格，多出来的进战报（与「放不下的行进战报」同一条口径）。
    /// </summary>
    private void _add_settle_drops(Godot.Collections.Dictionary view)
    {
        var box = new VBoxContainer { Name = "SettleDrops" };
        box.AddThemeConstantOverride("separation", 4);
        box.AddChild(_settle_label("SettleDropsHead", "掉落", 12, "stage_dim"));
        var grid = new GridContainer
        {
            Name = "SettleDropGrid",
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 4);
        Godot.Collections.Array cells = _arr_of(view, "drops");
        if (cells.Count == 0)
        {
            grid.AddChild(_settle_label("SettleDropNone", "无", 12, "stage_dim"));
        }

        int index = 0;
        foreach (Variant cell_variant in cells)
        {
            Godot.Collections.Dictionary cell = cell_variant.AsGodotDictionary();
            var row = new HBoxContainer
            {
                Name = $"SettleDropRow{index}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            row.AddThemeConstantOverride("separation", 6);
            TextureRect icon = IconPaths.make_icon(_s(cell, "icon"), $"SettleDropIcon{index}");
            if (icon != null)
            {
                row.AddChild(icon);
            }

            // 格子里那行字要**占满剩余宽度**：Label 默认只 Fill（不扩），在 HBoxContainer 里会被压到
            // "最长单词"那么窄，再叠上自动换行就成了一字一行（第八版实测：「草药 ×3」竖着排成四行）。
            // 扩满之后短文案一行放下，只有真长（带词条的装备）才折行。
            Label cell_text = _settle_label(
                $"SettleDropText{index}", _s(cell, "text"), 12, "stage_text");
            cell_text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(cell_text);
            grid.AddChild(row);
            index += 1;
        }

        box.AddChild(grid);
        // 放不下的那一截只进战报，面板上留一句"还有几件"——旧卡片那句「明细见战报」不能悄悄没了，
        // 不然玩家看着 8 格以为这就是全部（口径②：不许丢信息，但也不能顺手把"还有东西"这件事也丢掉）。
        int total = _i(view, "drops_total", cells.Count);
        if (total > cells.Count)
        {
            box.AddChild(_settle_label(
                "SettleDropMore", $"还有 {total - cells.Count} 件放不下（见战报）", 12, "stage_dim"));
        }

        _settle_column.AddChild(box);
    }

    /// <summary>⑤ 武学结算：领悟／未习得／熟练度，**每条一行**（原样搬，不截）</summary>
    private void _add_settle_martial(Godot.Collections.Dictionary view)
    {
        Godot.Collections.Array rows = _arr_of(view, "martial");
        if (rows.Count == 0)
        {
            return;
        }

        var box = new VBoxContainer { Name = "SettleMartial" };
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(_settle_label("SettleMartialHead", "武学", 12, "stage_dim"));
        int index = 0;
        foreach (Variant row_variant in rows)
        {
            box.AddChild(
                _settle_label($"SettleMartialRow{index}", row_variant.AsString(), 12, "stage_text"));
            index += 1;
        }

        _settle_column.AddChild(box);
    }

    /// <summary>
    /// 说明与附注（不在"收获四块"里）：败北／撤退的去向、平局的说明、练习战口径、暂缓规则、
    /// 自动存档失败。零收获时就只剩它 ＋ 标题 ＋ 「确定」（口径③）。
    /// </summary>
    private void _add_settle_notes(Godot.Collections.Dictionary view)
    {
        Godot.Collections.Array rows = _arr_of(view, "notes");
        if (rows.Count == 0)
        {
            return;
        }

        var box = new VBoxContainer { Name = "SettleNotes" };
        box.AddThemeConstantOverride("separation", 2);
        int index = 0;
        foreach (Variant row_variant in rows)
        {
            box.AddChild(
                _settle_label($"SettleNoteRow{index}", row_variant.AsString(), 12, "stage_dim"));
            index += 1;
        }

        _settle_column.AddChild(box);
    }

    /// <summary>标题上下那两条细金线（1px，和 `UiKit._header` 的细线同一个画法）</summary>
    private static ColorRect _settle_rule(string node_name) => new()
    {
        Name = node_name,
        Color = UiKit.color("gold"),
        CustomMinimumSize = new Vector2(0, 1),
    };

    /// <summary>面板里的一行字：色值只按名字从主题取（`font_size` 只有 12／24 两档）</summary>
    private static Label _settle_label(string node_name, string text, int font_size, string color_name)
    {
        var label = new Label
        {
            Name = node_name,
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeFontSizeOverride("font_size", font_size);
        label.AddThemeColorOverride("font_color", UiKit.color(color_name));
        return label;
    }

    /// <summary>① 那一行的字：胜利／撤退／平局／败北（`win` 以外的都归败北，与 `_settle()` 的分支同一套）</summary>
    private string _settle_title(string winner)
    {
        if (winner == _winner_ally())
        {
            return "胜利";
        }

        if (winner == _winner_flee())
        {
            return "撤退";
        }

        if (winner == _winner_draw())
        {
            return "平局";
        }

        return "败北";
    }

    /// <summary>
    /// 掉落清单的每一项：图标路径 ＋ 文本（名字 ×N，装备带词条，没捡起的写明原因）。**全量**——
    /// 战报那一行（`_describe_drops()`）与面板的格子都吃它，两边不会漂。
    /// 图标**有图才摆**由摆的时候判（`make_icon` 没图返回 null）。
    /// </summary>
    private Godot.Collections.Array _drop_cells(Godot.Collections.Dictionary applied)
    {
        var cells = new Godot.Collections.Array();
        foreach (Variant entry_variant in _arr_of(applied, "items"))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string item_id = _s(entry, "item_id");
            Resource row = Db.get_row("item_base", item_id);
            cells.Add(_settle_cell(
                IconPaths.item(_settle_icon_id(row, item_id)),
                $"{_settle_row_name(row, item_id)} ×{_i(entry, "qty", 1)}"));
        }

        foreach (Variant instance_variant in _arr_of(applied, "equipment"))
        {
            string instance_id = instance_variant.AsString();
            GodotObject inventory = state.Get("inventory").AsGodotObject();
            string base_id = inventory.Call("base_of", instance_id).AsString();
            Resource base_row = Db.get_row("equip_base", base_id);
            string text = _settle_row_name(base_row, base_id);
            string affix = _affix_text(instance_id);
            cells.Add(_settle_cell(
                IconPaths.equip(_settle_icon_id(base_row, base_id)),
                affix.Length > 0 ? $"{text}（{affix}）" : text));
        }

        foreach (Variant entry_variant in _arr_of(applied, "overflow"))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string item_id = _s(entry, "item_id");
            Resource row = Db.get_row("item_base", item_id);
            cells.Add(_settle_cell(
                IconPaths.item(_settle_icon_id(row, item_id)),
                $"{_settle_row_name(row, item_id)}×{_i(entry, "qty", 1)} 没捡起（背包满）"));
        }

        return cells;
    }

    /// <summary>面板（④）那两列只摆前 `SETTLE_DROP_ITEMS` 格，多出来的照旧进战报（§1.6 口径②）</summary>
    private Godot.Collections.Array _settle_drop_cells(Godot.Collections.Dictionary applied)
    {
        Godot.Collections.Array cells = _drop_cells(applied);
        if (cells.Count <= SETTLE_DROP_ITEMS)
        {
            return cells;
        }

        var head = new Godot.Collections.Array();
        for (int index = 0; index < SETTLE_DROP_ITEMS; index++)
        {
            head.Add(cells[index]);
        }

        return head;
    }

    /// <summary>图标 id：**优先行里的 `icon`，空则退回行 id**（与道具行／装备行／buff 行同一套优先级）</summary>
    private static string _settle_icon_id(Resource row, string fallback_id)
    {
        string icon_id = row != null ? row.Get("icon").AsString().StripEdges() : "";
        return icon_id.Length > 0 ? icon_id : fallback_id;
    }

    /// <summary>行上的中文名：表里没有就退回 id（**只给战报／截图看，不进玩家可见文案的 id 门限**——见 `_drop_parts` 同一口径）</summary>
    private static string _settle_row_name(Resource row, string fallback_id) =>
        row != null ? row.Get("name_cn").AsString() : fallback_id;

    private static Godot.Collections.Dictionary _settle_cell(string icon, string text) =>
        new() { { "icon", icon }, { "text", text } };
}
