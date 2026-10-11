using Godot;

namespace Lunhui.World;

/// <summary>
/// 大地图上的玩家角色（C# 版，对位原 `src/world/player_controller.gd`）。
///
/// 占位美术：`Polygon2D` 画的方块 + 一个朝向小方块（背袭要靠朝向判断，所以朝向必须看得见）。
/// 碰撞只吃「阻挡层」（瓦片物理层的位 2），与明雷的接触交给明雷的接触判定。
/// </summary>
public partial class PlayerController : CharacterBody2D
{
    /// <summary>
    /// 走路速度（像素/秒）。**2026-10-04（0.31.2 大地图扩容）从 180 下调回 128**：
    /// 相机改 2× 之后"观感变快"，设计侧给的判据是「**清风驿走到驿站不短于约 4 秒**」——
    /// 按直线（路程的下界）算 514.8 / 128 ≈ **4.02 秒**。这个判据由
    /// `tests/TestOverworld.cs::_CheckWalkSpeedCriterion` 每次验收重算。
    /// </summary>
    public const float WALK_SPEED = 128.0f;

    public const float SNEAK_RATIO = 0.6f;
    public const int BLOCKING_LAYER_BIT = 2;

    /// <summary>`WALK_SPEED` 的取值口（GDScript 侧用；P5 清零 GDScript 后收回成字段）。</summary>
    public static float walk_speed() => WALK_SPEED;

    /// <summary>`SNEAK_RATIO` 的取值口（GDScript 侧用；同上）。</summary>
    public static float sneak_ratio() => SNEAK_RATIO;

    [Export] public Vector2 facing = Vector2.Down;
    [Export] public bool sneaking = false;
    [Export] public bool can_move = true;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = BLOCKING_LAYER_BIT;
        if (GetChildCount() == 0)
        {
            _build_placeholder();
        }
    }

    private void _build_placeholder()
    {
        var shape = new CollisionShape2D();
        shape.Shape = new CircleShape2D { Radius = 9.0f };
        shape.Position = new Vector2(0, -6);
        AddChild(shape);

        var body = new Polygon2D
        {
            Name = "Body",
            Color = Color.FromHtml("6fa8dc"),
            Polygon = new Vector2[]
            {
                new(-8, -22), new(8, -22), new(8, 0), new(-8, 0),
            },
        };
        AddChild(body);

        var nose = new Polygon2D
        {
            Name = "Facing",
            Color = Color.FromHtml("f6d55c"),
            Polygon = new Vector2[]
            {
                new(-3, -3), new(3, -3), new(3, 3), new(-3, 3),
            },
        };
        AddChild(nose);
        _update_facing_marker();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!can_move)
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        Vector2 input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        sneaking = Input.IsActionPressed("sneak");
        float speed = WALK_SPEED * (sneaking ? SNEAK_RATIO : 1.0f);
        Velocity = input * speed;
        if (input.Length() > 0.1f)
        {
            facing = input.Normalized();
            _update_facing_marker();
        }

        MoveAndSlide();
        Color modulate = Modulate;
        modulate.A = sneaking ? 0.6f : 1.0f;
        Modulate = modulate;
    }

    private void _update_facing_marker()
    {
        var nose = GetNodeOrNull<Node2D>("Facing");
        if (nose == null)
        {
            return;
        }

        nose.Position = facing_limit(facing) * 14.0f;
    }

    /// <summary>朝向取整成四方向（背袭判定用）。</summary>
    public Vector2 facing_limit(Vector2 direction)
    {
        if (Mathf.Abs(direction.X) > Mathf.Abs(direction.Y))
        {
            return new Vector2(Mathf.Sign(direction.X), 0);
        }

        return new Vector2(0, Mathf.Sign(direction.Y));
    }

    public Vector2 facing_quadrant() => facing_limit(facing);

    public bool is_sneaking() => sneaking;
}
