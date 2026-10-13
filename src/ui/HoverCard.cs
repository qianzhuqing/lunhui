using Godot;

namespace Lunhui.Ui;

/// <summary>
/// 悬停小卡（UI 清单 §二-12）：鼠标停在格子／槽位／行上 **0.2 秒**后，在它**上方**浮出一张
/// **236px** 宽的小卡；移开**立即**收；**不挡点击**（整棵子树 `MouseFilter = Ignore`）。
///
/// **为什么单开一份**：同一张卡要挂到五六个地方（行囊格子、装备七槽、招式格／心法格、战斗属性行…）。
/// 各屏自己拼一张，就会出现「这一屏四行、那一屏三行」「这屏 0.3 秒、那屏 0.1 秒」这类
/// **不会报错的漂移**——而清单里那四类卡的字段顺序是定死的。所以卡的**形状**
/// （延迟／宽度／摆位／要不要挡点击／五个格子）只有这里一处定义，
/// 各屏只负责把**内容**（`HoverCardText` 的四个 builder）喂进来。
///
/// 摆位写成纯函数 `place()`：它不碰节点，用例可以直接量——真窗口里把鼠标挪到屏幕顶上的格子
/// 才能看出翻没翻，而那种验法在 headless 链里跑不了。
///
/// **不进 `OverlayStack`**：小卡不是浮层（不压栈、不吃 `Esc`、不夺焦点），它就是屏内一个
/// 画在最后面的 `Control`——盖在内容之上，鼠标事件照旧穿过去。
/// </summary>
public partial class HoverCard : PanelContainer
{
    /// <summary>浮出延迟（清单 §二-12：0.2 秒）。</summary>
    public const float DELAY_SECONDS = 0.2f;

    /// <summary>卡片宽（清单 §二-12：236px，画布内坐标）。</summary>
    public const int CARD_WIDTH = 236;

    /// <summary>贴边留的那点空（别让卡贴着画布边缘）。</summary>
    public const int EDGE_MARGIN = 4;

    /// <summary>卡与它指着的那一格之间的缝。</summary>
    public const int GAP = 6;

    /// <summary>正文行数（行2／行3／行4）——标题与脚注另算，四类卡都是这三行。</summary>
    public const int ROW_COUNT = 3;

    private Label _title;
    private Label[] _rows;
    private Label _foot;
    private Timer _delay;

    /// <summary>卡现在指着谁（`MouseExited` 要认人：挪到另一格时先收这一张）。</summary>
    private Control _target;

    /// <summary>正在等那 0.2 秒的那一格的内容（每次浮出都现算一遍，见 `watch`）。</summary>
    private System.Func<string[]> _pending_rows;

    /// <summary>
    /// 在 `host` 下造一张小卡（默认藏着）。**一屏一张就够**——所有格子共用它，
    /// 免得每格一个小卡节点、每格一个计时器。
    /// </summary>
    public static HoverCard make(Node host, string node_name)
    {
        var card = new HoverCard
        {
            Name = string.IsNullOrEmpty(node_name) ? "HoverCard" : node_name,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            // 盖在内容之上：同画布内按树序画，它挂在最后 ＋ 一个高 z，两重都吃住
            ZIndex = 16,
            CustomMinimumSize = new Vector2(CARD_WIDTH, 0),
        };
        card.AddThemeStyleboxOverride("panel", UiKit.panel_style());
        card._build();
        host.AddChild(card);
        return card;
    }

    /// <summary>
    /// 让某一格挂上小卡：停 **0.2 秒** 才浮、移开**立刻**收。
    /// `rows` 给的是**当前**内容（每次浮出都现算一遍——占格／熟练／装备一变，小卡就得跟着变，
    /// 清单 §二-12 那句「改占格后小卡立刻跟着变」）。
    /// </summary>
    public void watch(Control target, System.Func<string[]> rows)
    {
        if (target == null)
        {
            return;
        }

        target.MouseEntered += () => _arm(target, rows);
        target.MouseExited += () =>
        {
            // 只有正指着它的那一张才收——否则在两格之间划过去，会把刚浮出来的新卡一起收掉
            if (_target == target)
            {
                hide_now();
            }
        };
    }

    /// <summary>
    /// 立刻浮出（不等那 0.2 秒）。计时器到点走的就是这里；用例也走这里——
    /// headless 里没有真鼠标，靠 `MouseEntered` 验不了「0.2 秒之后浮出来」。
    /// </summary>
    public void show_now(Control target, string[] rows)
    {
        if (target == null || !GodotObject.IsInstanceValid(target))
        {
            return;
        }

        _target = target;
        set_rows(rows);
        Visible = true;
        _place_over(target);
    }

    /// <summary>收起来（移开鼠标、面板重建、切页签都走这里）。</summary>
    public void hide_now()
    {
        _target = null;
        if (_delay != null)
        {
            _delay.Stop();
        }

        Visible = false;
    }

    /// <summary>
    /// 换内容：`rows` 是「标题 ＋ 三行 ＋ 脚注」五格（见 `HoverCardText` 的四个 builder）。
    /// **空格子整行不摆**——摆一行空文字会白占一行高，卡看着就"多了一块"。
    /// </summary>
    public void set_rows(string[] rows)
    {
        if (rows == null || rows.Length == 0)
        {
            return;
        }

        _title.Text = rows[0];
        for (int i = 0; i < ROW_COUNT; i++)
        {
            string text = rows.Length > i + 1 ? rows[i + 1] : "";
            _rows[i].Text = text;
            _rows[i].Visible = text.Length > 0;
        }

        _foot.Text = rows.Length > ROW_COUNT + 1 ? rows[ROW_COUNT + 1] : "";
        _foot.Visible = _foot.Text.Length > 0;
    }

    /// <summary>
    /// 摆位（纯函数，用例直接量）：**上方**，贴到画布上边就翻到**下方**，
    /// 左右贴边时往里夹——**横向不翻**（翻到左边会挡住玩家正在看的那一格）。
    /// </summary>
    public static Vector2 place(Rect2 target, Vector2 card_size, Vector2 canvas)
    {
        float x = Mathf.Clamp(
            target.Position.X + (target.Size.X - card_size.X) * 0.5f,
            EDGE_MARGIN,
            Mathf.Max(EDGE_MARGIN, canvas.X - card_size.X - EDGE_MARGIN));
        float above = target.Position.Y - card_size.Y - GAP;
        float y = above >= EDGE_MARGIN ? above : target.End.Y + GAP;
        y = Mathf.Clamp(y, EDGE_MARGIN, Mathf.Max(EDGE_MARGIN, canvas.Y - card_size.Y - EDGE_MARGIN));
        return new Vector2(x, y);
    }

    private void _arm(Control target, System.Func<string[]> rows)
    {
        _target = target;
        _pending_rows = rows;
        if (_delay == null)
        {
            return;
        }

        _delay.Stop();
        _delay.Start();
    }

    private void _on_delay_timeout()
    {
        if (_target == null || !GodotObject.IsInstanceValid(_target) || _pending_rows == null)
        {
            return;
        }

        show_now(_target, _pending_rows());
    }

    private void _build()
    {
        var column = new VBoxContainer { Name = "HoverCardColumn", MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        AddChild(column);

        _title = _label("HoverCardTitle", 0);
        column.AddChild(_title);
        _rows = new Label[ROW_COUNT];
        for (int i = 0; i < ROW_COUNT; i++)
        {
            _rows[i] = _label($"HoverCardRow{i + 2}", 1);
            column.AddChild(_rows[i]);
        }

        _foot = _label("HoverCardFoot", 2);
        column.AddChild(_foot);

        _delay = new Timer
        {
            Name = "HoverDelay",
            OneShot = true,
            WaitTime = DELAY_SECONDS,
            Autostart = false,
        };
        _delay.Timeout += _on_delay_timeout;
        AddChild(_delay);
    }

    /// <summary>三档字色：标题 `ink`、正文 `ink_dim`、脚注 `ink_off`（换皮肤只改这三处）。</summary>
    private Label _label(string node_name, int tier)
    {
        var label = new Label
        {
            Name = node_name,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeColorOverride(
            "font_color", UiKit.color(tier == 0 ? "ink" : (tier == 1 ? "ink_dim" : "ink_off")));
        return label;
    }

    /// <summary>
    /// 按目标格子的位置把自己摆上去。目标矩形与卡都在同一个画布层里，所以要减掉本卡父节点的原点；
    /// 高度得**等换完内容再问**——行数不一样时卡高不一样，先用旧高度摆会差几像素。
    /// </summary>
    private void _place_over(Control target)
    {
        Vector2 origin = Vector2.Zero;
        Vector2 canvas = new Vector2(1152, 648);
        if (GetParent() is Control host)
        {
            origin = host.GetGlobalRect().Position;
            canvas = host.Size;
        }
        else if (GetViewport() != null)
        {
            canvas = GetViewport().GetVisibleRect().Size;
        }

        Rect2 rect = target.GetGlobalRect();
        var local = new Rect2(rect.Position - origin, rect.Size);
        // 宽度是死的 236，高度问内容：先把宽度压到 236（换行按它算），再取组合最小高的那个 Y
        Size = new Vector2(CARD_WIDTH, Size.Y);
        Vector2 size = new Vector2(CARD_WIDTH, GetCombinedMinimumSize().Y);
        Size = size;
        Position = place(local, size, canvas);
    }
}
