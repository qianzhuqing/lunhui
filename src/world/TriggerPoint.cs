using Godot;
using Lunhui.Core;

namespace Lunhui.World;

/// <summary>
/// 隐藏内容触发点（C# 版，对位原 `src/world/trigger_point.gd`）。
///
/// 位点是地编放好的 `Trigger_&lt;trigger_id&gt;`；触发类型与条件见 `hidden_trigger.csv`。
/// 已实现七类：item / space / carry / completion / kill_style / behavior / sequence。
/// </summary>
public partial class TriggerPoint : Area2D
{
    [Signal]
    public delegate void ActivatedEventHandler(string trigger_id, string summary);

    public const float CONTACT_DISTANCE = 30.0f;

    /// <summary>火盆（顺序谜题）用地图侧交付的两态贴图：灭／燃。</summary>
    public const string PROP_DIR = "res://assets/sprites/props/";
    public const string BRAZIER_OFF = PROP_DIR + "brazier_off.png";
    public const string BRAZIER_ON = PROP_DIR + "brazier_on.png";

    public static readonly string[] SUPPORTED_TYPES =
        { "item", "space", "carry", "completion", "kill_style", "behavior", "sequence" };

    [Export] public string trigger_id = "";
    [Export] public Resource row = null;
    [Export] public bool used = false;
    [Export] public Node2D player = null;
    /// <summary>火盆专用：这一格现在是不是点着的（其余类型恒 false）。</summary>
    [Export] public bool lit = false;
    /// <summary>sequence 类专用：这个位点是第几个火盆（1／2／3）；其余类型是 0。</summary>
    [Export] public int sequence_index = 0;
    /// <summary>取显示名用（`requirement_text` 要把 `item_pickaxe` 说成「铁镐」）。</summary>
    public TableDb db = null;

    public void setup(
        Resource trigger_row, Node2D player_node, bool already_used = false,
        TableDb table_db = null, int seq_index = 0
    )
    {
        row = trigger_row;
        trigger_id = trigger_row.Get("trigger_id").AsString();
        player = player_node;
        used = already_used;
        lit = already_used && _is_brazier();   // 进图时这条隐藏已经做过 → 火盆应该是燃的
        db = table_db;
        sequence_index = seq_index;
        CollisionLayer = 0;
        CollisionMask = 0;
        Monitoring = false;
        _build_placeholder();
    }

    private void _build_placeholder()
    {
        var shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = 14.0f } };
        AddChild(shape);

        if (_is_brazier() && ResourceLoader.Exists(_brazier_sprite_path()))
        {
            AddChild(new Sprite2D
            {
                Name = "Body",
                Texture = GD.Load<Texture2D>(_brazier_sprite_path()),
                Position = new Vector2(0, -8),
            });
        }
        else
        {
            // 贴图没到位（或不是火盆）就退回原来的占位多边形：不然这间屋子会空一格
            AddChild(new Polygon2D
            {
                Name = "Body",
                Color = _polygon_color(),
                Polygon = new Vector2[]
                {
                    new(0, -14), new(12, 0), new(0, 14), new(-12, 0),
                },
            });
        }

        var label = new Label
        {
            Name = "Name",
            Text = row.Get("name_cn").AsString(),
            Position = new Vector2(-26, -34),
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("color", new Color(1, 1, 1, 0.85f));
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        AddChild(label);
    }

    private bool _is_brazier() => row != null && row.Get("trigger_type").AsString() == "sequence";

    private string _brazier_sprite_path() => lit ? BRAZIER_ON : BRAZIER_OFF;

    /// <summary>占位多边形的配色（与旧的三个取值逐字一致，用例在按颜色断言）。</summary>
    private Color _polygon_color()
    {
        if (lit)
        {
            return Color.FromHtml("e8a24a");     // 燃：暖橙
        }

        if (used)
        {
            return Color.FromHtml("3f3f3f");
        }

        return supported() ? Color.FromHtml("b07cc6") : Color.FromHtml("5a5a5a");
    }

    /// <summary>
    /// 点燃／熄灭这一格的视觉（顺序谜题用）：控制器按次序判定结果调它。
    /// 贴图缺了就退回颜色区分，不静默。
    /// </summary>
    public void set_lit(bool value)
    {
        lit = value;
        var body = GetNodeOrNull<Node>("Body");
        if (body is Sprite2D sprite)
        {
            string path = _brazier_sprite_path();
            if (ResourceLoader.Exists(path))
            {
                sprite.Texture = GD.Load<Texture2D>(path);
            }

            return;
        }

        if (body is Polygon2D polygon)
        {
            polygon.Color = _polygon_color();
        }
    }

    /// <summary>这一格现在显示的是哪张占位贴图（用例读它）。</summary>
    public string brazier_texture_path()
    {
        var body = GetNodeOrNull<Node>("Body");
        if (body is Sprite2D sprite && sprite.Texture != null)
        {
            return sprite.Texture.ResourcePath;
        }

        return "";
    }

    public bool supported() =>
        row != null && System.Array.IndexOf(SUPPORTED_TYPES, row.Get("trigger_type").AsString()) >= 0;

    /// <summary>说明这一类为什么还没实现（回报用）。七类现在都实现了，正常情况下返回空串。</summary>
    public string unsupported_reason() => "";

    public bool can_interact()
    {
        if (used || player == null || row == null)
        {
            return false;
        }

        if (!supported())
        {
            return false;
        }

        return GlobalPosition.DistanceTo(player.GlobalPosition) <= CONTACT_DISTANCE;
    }

    public string requirement_text()
    {
        string trigger_type = row.Get("trigger_type").AsString();
        // 顺序谜题把「该按什么次序点」写给玩家（这条在表里，不是代码编的）
        if (trigger_type == "sequence")
        {
            string order = _condition_value(row.Get("required_condition").AsString(), "sequence");
            if (order.Length == 0)
            {
                return "按序点燃";
            }

            return $"按 {order.Replace("-", " → ")} 次序点燃";
        }

        string item_id = row.Get("required_item").AsString();
        if (item_id.Length == 0)
        {
            return "无需求";
        }

        if (db != null)
        {
            return $"需要 {db.display_name(item_id)}";
        }

        return $"需要 {item_id}";
    }

    public void mark_used()
    {
        used = true;
        var body = GetNodeOrNull<Node>("Body");
        if (body == null)
        {
            return;
        }

        if (body is Sprite2D)
        {
            // 火盆点着了就一直是燃的（贴图本身表达了「已完成」；压成灰反而像灭了）
            set_lit(true);
            return;
        }

        ((Polygon2D)body).Color = _polygon_color();
    }

    /// <summary>
    /// 从 `required_condition`（形如 `sequence=1-3-2`）里取一个键；取不到返回空串。
    /// 与控制器里的 `_condition_text` 同一口径（表里只有一处写这个条件，这里只读不写）。
    /// </summary>
    public static string _condition_value(string condition, string key)
    {
        foreach (string part in condition.Split(";", System.StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = part.Split("=", System.StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 2 && pieces[0].StripEdges() == key)
            {
                return pieces[1].StripEdges();
            }
        }

        return "";
    }
}
