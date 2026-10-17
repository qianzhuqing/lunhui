using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// 「道具使用 · 选人」弹窗（UI 清单 §二-3）——行囊页点「使用」打开，「取消」／`Esc` **回到行囊**
/// （不是退出整个面板：那一层由 `_UnhandledInput` 分开处理）。
///
/// 四块照清单摆：标题栏（`使用 · 小还丹` ＋ `持有 5`）／目标行（队伍每人一行：头像／名字／`LvN`／
/// 气血条／`气血 94 / 180`／状态词）／效果预览（效果／气血／消耗三行，随选中目标变）／
/// 「使用」＋「取消」。
///
/// **「使用」现在是置灰的，而且这不是偷懒**：`item_base` 还没有效果数值列（Q33），
/// 设计把效果写在 `desc` 那一句中文里（例：「非战斗恢复气血120」）。战斗里那六指令的「道具」
/// 用的是同一口径（`BattleSimulatorActions.battle_item_options()`）——**不解析中文、不自己编数值**，
/// 只如实说一句「效果数值还没配」。两处都等同一列，所以两处要一起改。
///
/// 目标行的「空位不可选」＝**根本不摆那一行**（队伍 3 人就 3 行）：摆一行灰的"空位"只会让玩家
/// 去点它、然后不知道为什么点不动。
/// </summary>
public partial class CharacterScreen
{
    /// <summary>目标行高（`Button` 不给子节点排版，行里的东西靠锚点铺满，所以高度得写死一个）。</summary>
    private const int USE_ROW_HEIGHT = 44;

    private Control _use_modal;
    private string _use_item_id = "";
    private string _use_target = "";

    /// <summary>弹窗开着没有——`Esc` 要先关它，别把整个面板一起退了。</summary>
    public bool use_modal_open() => _use_modal != null && _use_modal.Visible;

    /// <summary>打开弹窗（`item_id` 应为 `use_context=field` 的那一类；别的走不进这里）。</summary>
    public void open_use_item(string item_id)
    {
        if (item_id.Length == 0 || Db == null)
        {
            return;
        }

        _use_item_id = item_id;
        _use_target = _default_use_target();
        _use_modal ??= _build_use_modal();
        _refresh_use_modal();
        _use_modal.Visible = true;
        if (_use_modal.FindChild("UseCancel", true, false) is Button cancel)
        {
            cancel.GrabFocus();
        }
    }

    public void close_use_item()
    {
        if (_use_modal != null)
        {
            _use_modal.Visible = false;
        }
    }

    /// <summary>点一行＝选中目标（清单 §二-3：预览随选中目标变）。</summary>
    public void select_use_target(string char_id)
    {
        _use_target = char_id;
        _refresh_use_modal();
    }

    /// <summary>
    /// `使用` 为什么点不动（空串＝能点）。**Q33 那三列落表之后**（2026-10-13），判据就是那三列：
    /// 全空＝这件还没配效果，**如实说一句**（以前是常年置灰写「效果数值还没接」，现在只有真没配的才置灰）。
    /// </summary>
    private string _use_block_reason()
    {
        var row = Db.get_row("item_base", _use_item_id) as ItemBaseRow;
        if (row == null)
        {
            return "表里查不到这件东西";
        }

        return row.heal_hp <= 0 && row.heal_qi <= 0 && row.clear_status.Length == 0
            ? "这件道具还没配效果（效果数值三列都是空的）"
            : "";
    }

    /// <summary>真用一件（战斗外）：效果落地在 `ItemUse` 那一处，界面只播报结果。</summary>
    private void _apply_use_item()
    {
        if (_use_target.Length == 0)
        {
            return;
        }

        Godot.Collections.Dictionary result =
            ItemUse.use_field(Db, current_state(), _use_target, _use_item_id);
        if (!_b(result, "ok"))
        {
            _set_status(_s(result, "error"));
            return;
        }

        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant line_variant in result["lines"].AsGodotArray())
        {
            parts.Add(line_variant.AsString());
        }

        _set_status(parts.Count > 0 ? string.Join("　", parts) : "这一口没什么可回的");
        // 用完之后整屏重算（`refresh()` 自己会把弹窗收掉、行囊里的数量也跟着变）——
        // 与「悬停小卡指着的东西被这一轮算掉」同一条口径：**别留着一张指旧数据的卡**。
        refresh();
    }

    /// <summary>默认挑「第一个气血没满的人」——这一屏九成是进来补气血的；都满就挑第一个。</summary>
    private string _default_use_target()
    {
        string[] ids = _party_ids();
        foreach (string char_id in ids)
        {
            CharacterSheet panel = CharacterSheet.create(Db, current_state(), char_id);
            if (panel != null && panel.current_hp() < panel.max_hp())
            {
                return char_id;
            }
        }

        return ids.Length > 0 ? ids[0] : "";
    }

    private string[] _party_ids()
    {
        GodotObject st = current_state();
        return st != null ? st.Get("char_ids").AsStringArray() : System.Array.Empty<string>();
    }

    private Control _build_use_modal()
    {
        var modal = new Control
        {
            Name = "UseModal",
            Visible = false,
            // 弹窗底要**吃掉**鼠标：不然点在压暗底上会穿到行囊页去，选中态在背后乱跳
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        modal.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect
        {
            Name = "UseDim",
            Color = UiKit.color("backdrop"),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        modal.AddChild(dim);

        var center = new CenterContainer { Name = "UseCenter", MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        modal.AddChild(center);
        PanelContainer window = UiKit.make_panel("UseWindow");
        window.CustomMinimumSize = new Vector2(560, 0);
        center.AddChild(window);

        var column = new VBoxContainer { Name = "UseColumn" };
        column.AddThemeConstantOverride("separation", 8);
        window.AddChild(column);

        var head = new HBoxContainer { Name = "UseHead" };
        head.AddThemeConstantOverride("separation", 8);
        Label title = _make_label("UseTitle", "");
        // 这几行**关掉自动换行**：它们的文字都是本文件里定死的短句（标题、持有数、置灰原因），
        // 而自动换行的 Label 最小高是按「1 个字形宽折行」算的——留着它，面板的最小高会被顶到
        // 屏幕外面去（实测标题那一行就能报出 280）。会变长的那几行（目标行、预览）在滚动区里。
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        title.AddThemeColorOverride("font_color", UiKit.color("ink"));
        head.AddChild(title);
        Label held = _make_label("UseHeld", "");
        held.AutowrapMode = TextServer.AutowrapMode.Off;
        held.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        held.HorizontalAlignment = HorizontalAlignment.Right;
        held.AddThemeColorOverride("font_color", UiKit.color("ink_dim"));
        head.AddChild(held);
        column.AddChild(head);

        var targets = new VBoxContainer { Name = "UseTargets" };
        targets.AddThemeConstantOverride("separation", 4);
        var preview = new VBoxContainer { Name = "UsePreview" };
        preview.AddThemeConstantOverride("separation", 2);
        preview.AddChild(_make_label("UsePreviewEffect", ""));
        preview.AddChild(_make_label("UsePreviewHp", ""));
        preview.AddChild(_make_label("UsePreviewCost", ""));

        // **目标行与预览要收进一个定高的滚动区**：这几行都是 `AutowrapMode=WordSmart` 的 Label，
        // 而自动换行的 Label **最小宽只有 1 个字形**、最小高却是「按 1px 宽折行」算出来的
        // （实测三行预览就报 870px）——直接放进面板，面板的最小高会被顶到 1700 多，
        // 居中容器照着这个数摆，窗口就长到屏幕外面去了。滚动区把它挡在里面（本项目一贯做法：
        // 会随内容变长的那一段都走滚动区），队伍再多人也只是内部滚动。
        var scroll = new ScrollContainer
        {
            Name = "UseScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(520, 200),
        };
        var body = new VBoxContainer { Name = "UseBody" };
        body.AddThemeConstantOverride("separation", 8);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        body.AddChild(targets);
        body.AddChild(preview);
        scroll.AddChild(body);
        column.AddChild(scroll);

        var actions = new HBoxContainer { Name = "UseActions" };
        actions.AddThemeConstantOverride("separation", 8);
        Button confirm = _make_button("UseConfirm", "使用", () => { _apply_use_item(); }, true);
        actions.AddChild(confirm);
        actions.AddChild(_make_button("UseCancel", "取消", () => { close_use_item(); }));
        column.AddChild(actions);

        Label reason = _make_label("UseBlockReason", "");
        reason.AutowrapMode = TextServer.AutowrapMode.Off;
        reason.AddThemeColorOverride("font_color", UiKit.color("ink_off"));
        column.AddChild(reason);

        AddChild(modal);
        return modal;
    }

    private void _refresh_use_modal()
    {
        if (_use_modal == null)
        {
            return;
        }

        if (Db.get_row("item_base", _use_item_id) is not ItemBaseRow row)
        {
            close_use_item();
            return;
        }

        GodotObject st = current_state();
        Inventory inventory = st != null ? st.Get("inventory").As<Inventory>() : null;
        int held = inventory != null ? inventory.count(_use_item_id) : 0;
        if (_use_modal.FindChild("UseTitle", true, false) is Label title)
        {
            title.Text = $"使用 · {row.name_cn}";
        }

        if (_use_modal.FindChild("UseHeld", true, false) is Label held_label)
        {
            held_label.Text = $"持有 {held}";
        }

        _refresh_use_targets();

        CharacterSheet target = _use_target.Length > 0
            ? CharacterSheet.create(Db, st, _use_target)
            : null;
        if (_use_modal.FindChild("UsePreviewEffect", true, false) is Label effect)
        {
            // 数值列没配，就把表里那一句效果说明如实摆出来（不自作主张去解析它）
            effect.Text = $"效果　{(row.desc.Length > 0 ? row.desc : "这件没写效果")}";
        }

        if (_use_modal.FindChild("UsePreviewHp", true, false) is Label hp_line)
        {
            if (target == null)
            {
                hp_line.Text = "气血　先点上面一行选人";
            }
            else
            {
                int hp = target.current_hp();
                int max_hp = target.max_hp();
                // Q33 三列落表之后（2026-10-13），这一行终于是清单要的那句
                // 「气血 94 → 180（+86，上限截断）」——**与真扣下去同一处口径**（`ItemUse.preview_field`）
                Godot.Collections.Dictionary preview =
                    ItemUse.preview_field(Db, st, _use_target, row, hp, max_hp);
                int after = preview["hp_after"].AsInt32();
                int healed = preview["healed"].AsInt32();
                hp_line.Text = $"气血　{hp} / {max_hp}"
                    + (row.heal_hp > 0
                        ? $"　→　{after}（+{healed}"
                            + (after < hp + row.heal_hp ? $"，上限截断）" : "）")
                        : "　·　这件不回气血")
                    + (hp >= max_hp && row.heal_hp > 0 ? "　·　已满，这一口浪费了" : "");
            }
        }

        if (_use_modal.FindChild("UsePreviewCost", true, false) is Label cost)
        {
            cost.Text = $"消耗　{row.name_cn} ×1（剩 {System.Math.Max(0, held - 1)}）";
        }

        if (_use_modal.FindChild("UseConfirm", true, false) is Button confirm)
        {
            // 能不能按是**每刷新一次现算**的（Q33 三列落表之后：配了效果就能按、
            // 真没配的那几件才置灰）——以前是建的时候写死 `true`，一置灰就是一辈子
            string block = _use_block_reason();
            confirm.Disabled = block.Length > 0;
            confirm.TooltipText = block;
        }

        if (_use_modal.FindChild("UseBlockReason", true, false) is Label reason)
        {
            reason.Text = _use_block_reason();
        }
    }

    private void _refresh_use_targets()
    {
        if (_use_modal.FindChild("UseTargets", true, false) is not VBoxContainer targets)
        {
            return;
        }

        _clear(targets);
        foreach (string char_id in _party_ids())
        {
            CharacterSheet panel = CharacterSheet.create(Db, current_state(), char_id);
            if (panel != null)
            {
                targets.AddChild(_use_target_row(panel, char_id));
            }
        }
    }

    private Control _use_target_row(CharacterSheet panel, string char_id)
    {
        bool on = char_id == _use_target;
        int hp = panel.current_hp();
        int max_hp = panel.max_hp();
        bool full = hp >= max_hp;
        var row = new Button
        {
            Name = $"UseTargetRow{char_id}",
            Text = "",
            CustomMinimumSize = new Vector2(520, USE_ROW_HEIGHT),
        };
        row.AddThemeStyleboxOverride("normal", UiChrome.cell_style(on, false));
        row.AddThemeStyleboxOverride("hover", UiChrome.cell_style(true, false));
        row.AddThemeStyleboxOverride("pressed", UiChrome.cell_style(true, false));
        row.AddThemeStyleboxOverride("focus", UiChrome.cell_style(on, false));
        string captured = char_id;
        row.Pressed += () => select_use_target(captured);

        // `Button` **不给子节点排版**（它不是容器）：行里的东西靠锚点铺满再摆，子节点一律不吃鼠标
        // ——与行囊格子同一套做法（那里踩过一次：子节点把格子的点击吃掉了）
        var fill = new HBoxContainer
        {
            Name = "UseTargetFill" + char_id,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fill.AddThemeConstantOverride("separation", 8);
        row.AddChild(fill);

        string portrait = $"res://assets/sprites/portraits/portrait_{char_id}.png";
        TextureRect face = IconPaths.make_icon(portrait, $"UseTargetFace{char_id}");
        if (face != null)
        {
            face.CustomMinimumSize = new Vector2(USE_ROW_HEIGHT - 8, USE_ROW_HEIGHT - 8);
            fill.AddChild(face);
        }

        Label name = _make_label(
            $"UseTargetName{char_id}", $"{(on ? "▶ " : "")}{panel.display_name()}");
        name.AddThemeColorOverride("font_color", UiKit.color(on ? "cinnabar_deep" : "ink"));
        fill.AddChild(name);
        fill.AddChild(_make_label($"UseTargetLevel{char_id}", $"Lv{panel.level}"));
        Control bar = UiChrome.stat_bar(
            "hp", max_hp > 0 ? (float)hp / max_hp : 0f, 120, $"UseTargetHpBar{char_id}");
        bar.MouseFilter = Control.MouseFilterEnum.Ignore;
        fill.AddChild(bar);
        fill.AddChild(_make_label($"UseTargetHp{char_id}", $"气血 {hp} / {max_hp}"));
        Label state = _make_label($"UseTargetState{char_id}", full ? "已满" : "—");
        state.AddThemeColorOverride("font_color", UiKit.color(full ? "ink_off" : "ink_dim"));
        fill.AddChild(state);
        return row;
    }
}
