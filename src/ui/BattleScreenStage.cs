using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的**舞台**（partial，守 800 行硬上限）：版式常量、战场底、站位、镜头层、
/// Boss 长血条与行动顺序条。
///
/// 版式照 `docs/design/22_战斗表现.md` §1.1／§1.3：**整屏 1152×648、无壳**，
/// 只有三层——远景敌人（3×）／地面／近景我方（4×）。坐标取自策划那份可编辑示意源
/// （路径见 `docs/dev/交接/小策划.md` 追加 19），换版式只改这一份常量。
///
/// **镜头＝`StageRoot` 一个节点的 `position`／`scale` 补间**（22 §1.4）：不引相机节点、
/// 不引 3D、不写 shader。顺序条**挂在屏上、不进 `StageRoot`**（§1.1 那条注），
/// 头顶条／状态图标／预兆签／相性行**挂 `StageRoot`**（它们得跟着单位动）。
/// </summary>
public partial class BattleScreen
{
    // ------------------------------------------------------------------ 版式（1152×648 的设计坐标）

    /// <summary>我方一排四个站位（x）宽 4×（128）：近景、站画面的下缘。</summary>
    private static readonly int[] ALLY_X = { 64, 208, 352, 496 };
    private const int ALLY_Y = 458;
    private const int ALLY_W = 128;

    /// <summary>敌方三列（x）与三排（y）：排越靠上越远——这就是纵深。</summary>
    private static readonly int[] FOE_COL = { 330, 530, 730 };
    private static readonly int[] FOE_ROW_Y = { 132, 232, 332 };
    private static readonly int[] FOE_ROW_DX = { 40, 20, 0 };
    private const int FOE_W = 96;
    private const int FOE_Y = 302;

    /// <summary>Boss 站位：一个大位 ＋ 两侧各一个随从。</summary>
    private const int BOSS_X = 442;
    private const int BOSS_Y = 140;
    private const int BOSS_W = 176;

    /// <summary>单位头顶正上方 8px（22 §1.2.1）。</summary>
    private const int PLATE_GAP = 8;

    /// <summary>示意源的设计画布。窗口用 `aspect=expand` 时实际画布会更大，按这个尺寸居中。</summary>
    private const int DESIGN_W = 1152;
    private const int DESIGN_H = 648;

    /// <summary>顺序条：整条总宽 ≤40px（22 §1.1 第五版——必须让开我方第一个人的头顶条）。</summary>
    private const int TURN_BAR_WIDTH = 40;

    /// <summary>架势刻度的格值兜底（真值在 `combat_const.poise_grid`；缺行才用这个并报一次错）。</summary>
    private const int POISE_GRID_FALLBACK = 10;

    // ------------------------------------------------------------------ 舞台底与镜头

    /// <summary>
    /// 战场底（22 §1.5／15 §4.4）：墨渐变压暗 ＋ 远景山脊 ＋ 地平线。
    /// **全程序画**（美术 B6 那张舞台图一交就换），所以这一屏是全游戏唯一压暗的一屏。
    /// </summary>
    private void _build_backdrop()
    {
        _stage_root ??= GetNodeOrNull<Node2D>("StageRoot");
        if (_stage_root == null || _stage_root.GetNodeOrNull("Backdrop") != null)
        {
            return;
        }

        Vector2 world = _stage_size();
        var gradient = new Gradient();
        gradient.Offsets = new[] { 0.0f, 0.52f, 1.0f };
        gradient.Colors = new[]
        {
            UiKit.color("stage_top"), UiKit.color("stage_mid"), UiKit.color("stage_bottom"),
        };
        var texture = new GradientTexture2D
        {
            Gradient = gradient,
            FillFrom = new Vector2(0, 0),
            FillTo = new Vector2(0, 1),
            Width = (int)world.X,
            Height = (int)world.Y,
        };
        var backdrop = new TextureRect
        {
            Name = "Backdrop",
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = world,
        };
        _stage_root.AddChild(backdrop);

        // 山脊：示意源里那条 14 点的折线，按画布比例算出来（换了窗口尺寸也跟着走）
        var ridge = new Polygon2D
        {
            Name = "Ridge",
            Color = UiKit.color("stage_ridge"),
        };
        float left = world.X * -0.02f;
        float width = world.X * 1.04f;
        float top = world.Y * 0.24f;
        float height = world.Y * 0.30f;
        var points = new System.Collections.Generic.List<Vector2>();
        foreach ((float px, float py) in RIDGE_SHAPE)
        {
            points.Add(new Vector2(left + (px * width), top + (py * height)));
        }

        ridge.Polygon = points.ToArray();
        _stage_root.AddChild(ridge);

        var horizon = new ColorRect
        {
            Name = "Horizon",
            Color = new Color(1, 1, 1, 0.06f),
            Position = new Vector2(0, world.Y * 0.56f),
            Size = new Vector2(world.X, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _stage_root.AddChild(horizon);
    }

    /// <summary>示意源里那条山脊折线（按 x／y 占画布的比例）。</summary>
    private static readonly (float x, float y)[] RIDGE_SHAPE =
    {
        (0f, 1f), (0f, 0.62f), (0.07f, 0.40f), (0.15f, 0.58f), (0.24f, 0.26f), (0.33f, 0.54f),
        (0.41f, 0.34f), (0.52f, 0.60f), (0.60f, 0.30f), (0.70f, 0.56f), (0.79f, 0.38f),
        (0.88f, 0.58f), (1f, 0.42f), (1f, 1f),
    };

    /// <summary>实际画布尺寸（`aspect=expand` 下与 1152×648 不一定相等）。</summary>
    private Vector2 _stage_size()
    {
        Vector2 size = GetViewportRect().Size;
        return size.X < 100.0f || size.Y < 100.0f ? new Vector2(DESIGN_W, DESIGN_H) : size;
    }

    /// <summary>把设计坐标平移到实际画布（等比居中）。</summary>
    private Vector2 _design_offset() => (_stage_size() - new Vector2(DESIGN_W, DESIGN_H)) * 0.5f;

    /// <summary>
    /// 镜头归位（22 §1.4：开回合 `scale 1.0`、偏移 0）。
    /// 补间只动 `StageRoot` 一个节点——验收也只量它。
    /// </summary>
    private void _stage_reset(bool animate)
    {
        if (_stage_root == null)
        {
            return;
        }

        if (!animate)
        {
            _stage_root.Scale = Vector2.One;
            _stage_root.Position = Vector2.Zero;
            return;
        }

        Tween tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic);
        tween.SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_stage_root, "scale", Vector2.One, 0.30);
        tween.Parallel().TweenProperty(_stage_root, "position", Vector2.Zero, 0.30);
    }

    /// <summary>
    /// 把镜头推向出手的人（22 §1.4：选中指令微推 1.02×、出手推到 1.12×）。
    /// 只量节点、不量截图——所以这里不碰任何贴图。
    /// </summary>
    private void _stage_focus(string actor_id, double scale)
    {
        if (_stage_root == null || _units_by_id.Count == 0
            || !_units_by_id.TryGetValue(actor_id, out Control unit))
        {
            return;
        }

        Vector2 world = _stage_size();
        Vector2 target = unit.Position + (unit.Size * 0.5f);
        // `Node2D` 没有 `PivotOffset`（那是 `Control` 的），缩放锚点靠位置补偿：
        // 想让 `target` 这一个点钉在原地，就把整层挪 `target * (1 - scale)`。
        Vector2 offset = target * (1.0f - (float)scale);
        Tween tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic);
        tween.SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_stage_root, "scale", new Vector2((float)scale, (float)scale), 0.15);
        tween.Parallel().TweenProperty(_stage_root, "position", offset, 0.15);
    }

    // ------------------------------------------------------------------ 站位

    /// <summary>
    /// 单位摆位（22 §1.3 的三层纵深）：我方一排、敌方按人数取一排／两排／三排，Boss 战走大位。
    /// 返回「远→近」排好序的清单——脚底线越靠下越晚画，近的才压得住远的。
    /// </summary>
    private System.Collections.Generic.List<(GodotObject actor, Vector2 position, int size)> _placements()
    {
        var items = new System.Collections.Generic.List<(GodotObject, Vector2, int)>();
        Vector2 offset = _design_offset();
        int count = enemies.Count;
        bool boss_battle = _boss_actor() != null;
        for (int index = 0; index < count; index++)
        {
            GodotObject actor = enemies[index].AsGodotObject();
            if (boss_battle && actor == _boss_actor())
            {
                items.Add((actor, offset + new Vector2(BOSS_X, BOSS_Y), BOSS_W));
                continue;
            }

            if (boss_battle)
            {
                // Boss 战：随从退到两翼（示意源里那一版）
                int escort = index == 0 ? 0 : 2;
                items.Add((actor, offset + new Vector2(FOE_COL[escort] - 48, FOE_Y), FOE_W));
                continue;
            }

            int rows = count <= 3 ? 1 : (count <= 6 ? 2 : 3);
            int row = index / 3;
            int column = index % 3;
            if (rows == 1)
            {
                items.Add((actor, offset + new Vector2(_foe_single_x(index, count), FOE_Y), FOE_W));
                continue;
            }

            int slot = row + (3 - rows);
            items.Add((actor, offset + new Vector2(
                FOE_COL[column] + FOE_ROW_DX[slot] - (FOE_W / 2), FOE_ROW_Y[slot]), FOE_W));
        }

        for (int index = 0; index < allies.Count; index++)
        {
            items.Add((
                allies[index].AsGodotObject(),
                offset + new Vector2(ALLY_X[System.Math.Min(index, ALLY_X.Length - 1)], ALLY_Y),
                ALLY_W));
        }

        items.Sort((left, right) =>
            (left.Item2.Y + left.Item3).CompareTo(right.Item2.Y + right.Item3));
        return items;
    }

    /// <summary>一排列队时敌人站哪一列：1 只居中、2 只分两翼、3 只铺满三列（示意源的口径）。</summary>
    private static int _foe_single_x(int index, int count)
    {
        int column = count switch
        {
            1 => 1,
            2 => index == 0 ? 0 : 2,
            _ => System.Math.Min(index, 2),
        };
        return FOE_COL[column] - (FOE_W / 2);
    }

    // ------------------------------------------------------------------ 程序画的条（22 §1.2.1）

    /// <summary>
    /// 一条状态条：底槽 ＋ 填充 ＋ **1px 描边**。暗底上不描边就是黑底黑条（15 §4.4）。
    /// `node_name` 挂在里面的 `ProgressBar` 上——那是用例与自检的接口（`HpBar_&lt;id&gt;` 等），
    /// 描边只能当兄弟节点画在它上面，否则会被填充盖掉。
    /// </summary>
    private static Control _status_bar(
        string node_name, double value, double maximum,
        string color_key, int width, int height, string rim_key)
    {
        var wrap = new Control
        {
            Name = $"{node_name}Wrap",
            CustomMinimumSize = new Vector2(width, height),
        };
        var bar = new ProgressBar
        {
            Name = node_name,
            MinValue = 0,
            MaxValue = System.Math.Max(1.0, maximum),
            Value = System.Math.Max(0.0, value),
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(width, height),
        };
        bar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bar.AddThemeStyleboxOverride("background", _stage_box(UiKit.color("track"), 0, 0));
        bar.AddThemeStyleboxOverride("fill", _stage_box(UiKit.color(color_key), 0, 0));
        wrap.AddChild(bar);
        var rim = new Panel { Name = $"{node_name}Rim", MouseFilter = Control.MouseFilterEnum.Ignore };
        rim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        rim.AddThemeStyleboxOverride("panel", _frame_box(UiKit.color(rim_key), 1));
        wrap.AddChild(rim);
        return wrap;
    }

    /// <summary>纯色块（圆角 1）：条底槽、刻度格的填充都用它。</summary>
    private static StyleBoxFlat _stage_box(Color color, int radius, int border)
    {
        var style = new StyleBoxFlat { BgColor = color };
        style.CornerRadiusTopLeft = radius;
        style.CornerRadiusTopRight = radius;
        style.CornerRadiusBottomLeft = radius;
        style.CornerRadiusBottomRight = radius;
        style.SetBorderWidthAll(border);
        style.BorderColor = color;
        return style;
    }

    /// <summary>只有一个边框的框（底透明）：头顶条那一圈、刻度格都用它。</summary>
    private static StyleBoxFlat _frame_box(Color color, int border)
    {
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = color };
        style.SetBorderWidthAll(border);
        style.CornerRadiusTopLeft = 1;
        style.CornerRadiusTopRight = 1;
        style.CornerRadiusBottomLeft = 1;
        style.CornerRadiusBottomRight = 1;
        return style;
    }

    /// <summary>
    /// 架势刻度（22 §1.2.1，八方的「护盾点数」）：**亮格数 ＝ 还差几下打空**。
    /// 格值固定（`combat_const.poise_grid`，设计 22 号点名落表），不足一格按比例点亮末格，
    /// 格间 1px 缝、格宽 ≥8px（远景敌人屏上才 56px 宽，格小了数不清）。
    /// 破绽（架势归零）时整条转朱红空心——那一拍还有「破绽」字标与顿帧。
    /// </summary>
    private Control _poise_ticks(GodotObject actor, int width, string rim_key)
    {
        int grid = _poise_grid();
        int max_poise = System.Math.Max(1, actor.Call("max_poise").AsInt32());
        int poise = System.Math.Max(0, actor.Get("poise").AsInt32());
        bool broken = actor.Call("is_broken").AsBool();
        int wanted = (int)System.Math.Ceiling(max_poise / (double)grid);
        int room = System.Math.Max(1, (width + 1) / 9);
        int cells = System.Math.Clamp(wanted, 1, room);
        int cell_width = System.Math.Max(8, (width - (cells - 1)) / cells);
        int lit = poise <= 0
            ? 0
            : System.Math.Clamp((int)System.Math.Ceiling(poise / (double)grid), 1, cells);
        double partial = lit == 0 ? 0.0 : System.Math.Clamp((poise - ((lit - 1) * grid)) / (double)grid, 0.0, 1.0);

        var row = new HBoxContainer
        {
            Name = $"PoiseBar_{actor.Get("actor_id").AsString()}",
            CustomMinimumSize = new Vector2(width, 5),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", 1);
        row.SetMeta("grid", grid);
        row.SetMeta("cells", cells);
        row.SetMeta("lit", lit);
        row.SetMeta("partial", partial);
        for (int index = 0; index < cells; index++)
        {
            var cell = new Control
            {
                Name = $"Tick{index}",
                CustomMinimumSize = new Vector2(cell_width, 5),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            var frame = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            frame.AddThemeStyleboxOverride(
                "panel", _frame_box(
                    broken ? UiKit.color("cinnabar") : new Color(UiKit.color("gold"), 0.35f), 1));
            cell.AddChild(frame);
            double ratio = index < lit - 1 ? 1.0 : (index == lit - 1 ? partial : 0.0);
            if (ratio > 0.0)
            {
                var fill = new ColorRect
                {
                    Name = "Fill",
                    Color = broken ? UiKit.color("cinnabar") : UiKit.color("gold"),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                fill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                fill.AnchorRight = (float)ratio;
                fill.OffsetRight = -1.0f;
                fill.OffsetLeft = 1.0f;
                fill.OffsetTop = 1.0f;
                fill.OffsetBottom = -1.0f;
                cell.AddChild(fill);
            }

            row.AddChild(cell);
        }

        return row;
    }

    /// <summary>架势刻度的格值：`combat_const.poise_grid`（22 §1.2.1 点名落表；公式不进表）。</summary>
    private int _poise_grid()
    {
        Resource row = Db?.get_row("combat_const", "poise_grid");
        return row != null
            ? System.Math.Max(1, (int)row.Get("value").AsDouble())
            : POISE_GRID_FALLBACK;
    }

    /// <summary>站位影（15 §4.4：椭圆 24×8、黑 α.35）：交代纵深，敌我脚下各一。</summary>
    private static Control _shadow(int width)
    {
        var shadow = new Panel
        {
            Name = "Shadow",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(width, 8),
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.35f),
        };
        style.CornerRadiusTopLeft = 4;
        style.CornerRadiusTopRight = 4;
        style.CornerRadiusBottomLeft = 4;
        style.CornerRadiusBottomRight = 4;
        shadow.AddThemeStyleboxOverride("panel", style);
        return shadow;
    }

    // ------------------------------------------------------------------ Boss 长血条（22 §1.2.2）

    /// <summary>Boss 战里的那个 Boss（`ai_template=ai_boss`）；没有就是普通遭遇。</summary>
    private GodotObject _boss_actor()
    {
        foreach (Variant enemy_variant in enemies)
        {
            GodotObject enemy = enemy_variant.AsGodotObject();
            if (enemy.Get("tags").AsGodotDictionary().ContainsKey("ai_template")
                && enemy.Get("tags").AsGodotDictionary()["ai_template"].AsString() == "ai_boss")
            {
                return enemy;
            }
        }

        return null;
    }

    /// <summary>
    /// 贴顶长血条：**全宽、高 40**，左＝名字（24px）＋阶段，中＝每 10% 一格的分段条，
    /// 下沿＝架势细条；破绽窗口整条转红。**非 Boss 战里它必须不存在**（22 §1.2.2／§八）。
    /// </summary>
    private void _refresh_boss_bar()
    {
        if (_boss_bar == null)
        {
            return;
        }

        GodotObject boss = _boss_actor();
        _boss_bar.Visible = boss != null;
        if (boss == null)
        {
            return;
        }

        int hp = boss.Get("hp").AsInt32();
        int max_hp = System.Math.Max(1, boss.Call("max_hp").AsInt32());
        double ratio = System.Math.Clamp(hp / (double)max_hp, 0.0, 1.0);
        bool broken = boss.Call("is_broken").AsBool();
        if (_boss_name != null)
        {
            _boss_name.Text = boss.Get("display_name").AsString();
        }

        if (_boss_phase != null)
        {
            string phase = "第 2 阶段";
            Resource row = Db?.get_row("enemy_base", boss.Get("source_id").AsString());
            if (row != null && row.Get("phases").AsString().Length > 0)
            {
                phase = row.Get("phases").AsString();
            }

            _boss_phase.Text = phase;
        }

        if (_boss_poise_label != null)
        {
            int lit = _poise_lit(boss);
            _boss_poise_label.Text = broken
                ? "架势刻度 0 / 10 · 破绽窗口"
                : $"架势刻度 {lit} / 10　{hp} / {max_hp}";
            _boss_poise_label.AddThemeColorOverride(
                "font_color", broken ? UiKit.color("cinnabar") : UiKit.color("stage_dim"));
        }

        _refresh_boss_affinity(boss);
        _clear(_boss_seg);
        _clear(_boss_poise);
        int segments = 10;
        int lit_segments = (int)System.Math.Ceiling(ratio * segments);
        for (int index = 0; index < segments; index++)
        {
            var segment = new Panel { Name = $"BossSeg{index}", MouseFilter = Control.MouseFilterEnum.Ignore };
            segment.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            segment.CustomMinimumSize = new Vector2(0, 10);
            segment.AddThemeStyleboxOverride(
                "panel", _stage_box(
                    index < lit_segments
                        ? (broken ? UiKit.color("cinnabar") : UiKit.color("hp"))
                        : UiKit.color("track"),
                    1, 1));
            _boss_seg.AddChild(segment);
        }

        _add_poise_ticks_into(_boss_poise, boss, broken, segments);
    }

    /// <summary>Boss 那条架势细条同样是刻度格（22 §1.2.1：Boss 长血条下沿那条「同样刻度化」）。</summary>
    private void _add_poise_ticks_into(HBoxContainer into, GodotObject actor, bool broken, int segments)
    {
        int lit = _poise_lit(actor);
        for (int index = 0; index < segments; index++)
        {
            var tick = new Panel
            {
                Name = $"BossPoise{index}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 4),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            tick.AddThemeStyleboxOverride(
                "panel", _stage_box(
                    index < lit
                        ? (broken ? UiKit.color("cinnabar") : UiKit.color("gold"))
                        : UiKit.color("track"),
                    1, 1));
            into.AddChild(tick);
        }
    }

    /// <summary>还差几下打空（Boss 长血条上那一行与刻度格同一个数）。</summary>
    private int _poise_lit(GodotObject actor)
    {
        int grid = _poise_grid();
        int poise = System.Math.Max(0, actor.Get("poise").AsInt32());
        int max_poise = System.Math.Max(1, actor.Call("max_poise").AsInt32());
        int cells = System.Math.Clamp(
            (int)System.Math.Ceiling(max_poise / (double)grid), 1, 10);
        return poise <= 0
            ? 0
            : System.Math.Clamp((int)System.Math.Ceiling(poise / (double)grid), 1, cells);
    }

    /// <summary>
    /// 相性行（22 §1.2.5）：敌人「打它该用什么」＋异常抗＋内力抗，≤3 枚、**只给敌人**。
    /// 数据全读现成的：`element_counter.csv` 的系别环 ＋ 敌人装的内功给的异常抗 ＋ `res_internal`。
    /// </summary>
    private void _refresh_boss_affinity(GodotObject boss)
    {
        _clear(_boss_aff);
        foreach (string text in _affinity_marks(boss))
        {
            _boss_aff.AddChild(_affinity_chip(text));
        }
    }

    /// <summary>相性行那几枚文字签：克／抗／内力。最多三枚（22 §1.2.5）。</summary>
    private string[] _affinity_marks(GodotObject actor)
    {
        var marks = new System.Collections.Generic.List<(string text, bool counter)>();
        Godot.Collections.Dictionary tags = actor.Get("tags").AsGodotDictionary();
        string element = tags.ContainsKey("element") ? tags["element"].AsString() : "external";
        if (Db != null)
        {
            foreach (Variant row_variant in Db.rows("element_counter"))
            {
                Resource row = row_variant.AsGodotObject() as Resource;
                if (row == null || row.Get("defend_element").AsString() != element
                    || System.Math.Abs(row.Get("multiplier").AsDouble() - 1.3) > 0.001)
                {
                    continue;
                }

                marks.Add(($"克 {_element_name(row.Get("attack_element").AsString())}", true));
                break;
            }
        }

        foreach (string passive in actor.Get("passives").AsStringArray())
        {
            Resource row = Db?.get_row("skill_base", passive);
            if (row == null)
            {
                continue;
            }

            string name = row.Get("name_cn").AsString();
            if (name.Contains("毒"))
            {
                marks.Add(("抗 毒", false));
            }
            else if (name.Contains("烧") || name.Contains("炎"))
            {
                marks.Add(("抗 烧", false));
            }
        }

        Resource enemy_row = Db?.get_row("enemy_base", actor.Get("source_id").AsString());
        double res_internal = enemy_row != null ? enemy_row.Get("res_internal").AsDouble() : 0.0;
        if (res_internal > 0.0)
        {
            marks.Add(($"内力 −{System.Math.Round(res_internal * 100.0)}%", false));
        }

        var out_marks = new System.Collections.Generic.List<string>();
        foreach ((string text, bool _) in marks)
        {
            if (out_marks.Count >= 3)
            {
                break;
            }

            out_marks.Add(text);
        }

        return out_marks.ToArray();
    }

    /// <summary>系别 id → 玩家看得懂的字（表里的 `name_cn`，取不到就退回 id）。</summary>
    private string _element_name(string element_id)
    {
        Resource row = Db?.get_row("element_def", element_id);
        return row != null ? row.Get("name_cn").AsString() : element_id;
    }

    /// <summary>相性行的一枚文字签（克＝枯黄、抗＝冷灰，15 §4.4）。</summary>
    private static Control _affinity_chip(string text)
    {
        var label = new Label
        {
            Name = $"Aff_{text}",
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride(
            "font_color", text.StartsWith("克", System.StringComparison.Ordinal)
                ? UiKit.color("gold")
                : UiKit.color("rim_foe"));
        return label;
    }
}
