using Godot;

namespace Lunhui.World;

/// <summary>
/// 副本里的宝箱（C# 版，对位原 `src/world/chest.gd`）。
///
/// 位点是地编放好的 `Chest_&lt;drop_group&gt;`；内容由 `drop_table` 决定，开启后把掉落交给
/// `BattleReward` 落账，并在小地图会话里记成「已开」。
/// </summary>
public partial class Chest : Area2D
{
    [Signal]
    public delegate void OpenedEventHandler(string chest_id, string summary);

    public const float CONTACT_DISTANCE = 26.0f;

    /// <summary>宝箱三档（07：宝箱开启「三档」，掉落组也分铜/银/金）。</summary>
    public static readonly string[] TIER_ORDER = { "gold", "silver", "copper" };

    public static readonly Godot.Collections.Dictionary TIER_COLORS = new()
    {
        { "copper", Color.FromHtml("c07a45") },
        { "silver", Color.FromHtml("c9ced6") },
        { "gold", Color.FromHtml("e8c04a") },
    };

    public static readonly Godot.Collections.Dictionary TIER_LABELS = new()
    {
        { "copper", "铜箱" },
        { "silver", "银箱" },
        { "gold", "金箱" },
    };

    public static readonly Godot.Collections.Dictionary TIER_SCALES = new()
    {
        { "copper", 0.85 },
        { "silver", 1.0 },
        { "gold", 1.2 },
    };

    public static readonly Color OPENED_COLOR = Color.FromHtml("6f6656");

    /// <summary>宝箱贴图：assets/sprites/props/chest_&lt;tier&gt;[_open].png（07 §8.3）。</summary>
    public const string SPRITE_DIR = "res://assets/sprites/props/";

    [Export] public string chest_id = "";
    [Export] public bool opened_already = false;
    [Export] public Node2D player = null;
    /// <summary>档位：copper / silver / gold（从掉落组名解析）。</summary>
    [Export] public string tier = "copper";

    public void setup(string group_id, Node2D player_node, bool already_open = false)
    {
        chest_id = group_id;
        player = player_node;
        opened_already = already_open;
        tier = resolve_tier(group_id);
        CollisionLayer = 0;
        CollisionMask = 0;
        Monitoring = false;
        _build_placeholder();
    }

    /// <summary>从掉落组名（drop_chest_copper / _silver / _gold）解析档位；认不出来按铜箱。</summary>
    public static string resolve_tier(string group_id)
    {
        foreach (string candidate in TIER_ORDER)
        {
            if (group_id.Contains(candidate))
            {
                return candidate;
            }
        }

        // 认不出来时**要出声**：要么是命名写错了，要么是设计加了新档（那要同时补贴图/颜色/名字）。
        GD.PushError($"[Chest] 认不出宝箱档位：'{group_id}'（命名要含 {string.Join(", ", TIER_ORDER)} 之一）");
        return "copper";
    }

    public string tier_id() => tier;

    public Color tier_color() =>
        TIER_COLORS.ContainsKey(tier) ? TIER_COLORS[tier].AsColor() : TIER_COLORS["copper"].AsColor();

    private void _build_placeholder()
    {
        var shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = 12.0f } };
        AddChild(shape);

        float scale_factor = TIER_SCALES.ContainsKey(tier) ? (float)TIER_SCALES[tier].AsDouble() : 1.0f;
        // 贴图优先：地编给了三档与对应的 _open 图就用它；找不到才退回「按档位上色」的占位方块
        string texture_path = $"{SPRITE_DIR}chest_{tier}{(opened_already ? "_open" : "")}.png";
        if (ResourceLoader.Exists(texture_path))
        {
            AddChild(new Sprite2D
            {
                Name = "Body",
                Texture = GD.Load<Texture2D>(texture_path),
                Position = new Vector2(0, -7),
                Scale = new Vector2(scale_factor, scale_factor),
            });
        }
        else
        {
            AddChild(new Polygon2D
            {
                Name = "Body",
                Color = opened_already ? OPENED_COLOR : tier_color(),
                Scale = new Vector2(scale_factor, scale_factor),
                Polygon = new Vector2[]
                {
                    new(-10, -14), new(10, -14), new(10, 0), new(-10, 0),
                },
            });
        }

        var label = new Label
        {
            Name = "State",
            Text = opened_already
                ? "已开"
                : (TIER_LABELS.ContainsKey(tier) ? TIER_LABELS[tier].AsString() : "宝箱"),
            Position = new Vector2(-14, -32),
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        AddChild(label);
    }

    public void mark_opened()
    {
        opened_already = true;
        var body = GetNodeOrNull<Node>("Body");
        if (body is Sprite2D sprite)
        {
            string open_path = $"{SPRITE_DIR}chest_{tier}_open.png";
            if (ResourceLoader.Exists(open_path))
            {
                sprite.Texture = GD.Load<Texture2D>(open_path);
            }
        }
        else if (body is Polygon2D polygon)
        {
            polygon.Color = OPENED_COLOR;
        }

        var label = GetNodeOrNull<Label>("State");
        if (label != null)
        {
            label.Text = "已开";
        }
    }

    /// <summary>当前用的是哪张贴图（用例读它）。</summary>
    public string body_texture_path()
    {
        var body = GetNodeOrNull<Node>("Body");
        if (body is Sprite2D sprite && sprite.Texture != null)
        {
            return sprite.Texture.ResourcePath;
        }

        return "";
    }

    /// <summary>玩家在范围内且没开过才能交互。</summary>
    public bool can_interact() =>
        !opened_already && player != null
        && GlobalPosition.DistanceTo(player.GlobalPosition) <= CONTACT_DISTANCE;
}
