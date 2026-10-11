using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.World;

/// <summary>
/// 大地图上的明雷小队（C# 版，对位原 `src/world/roaming_enemy.gd`）。
///
/// 行为来自 `roaming_spawn.behavior`（idle／patrol／wander／chase／sleep），数值来自表。
/// 接触玩家时发 `encountered(spawn_id, contact)`，由控制器决定进不进战斗。
/// 接触方式按 `docs/design/02_地图与明雷.md`：正面 → front；从背后 → back；背后且沉睡 → ambush_sleep；
/// 追击型进警戒圈被发现 → spotted；被追到 → caught。
/// </summary>
public partial class RoamingEnemy : CharacterBody2D
{
    [Signal]
    public delegate void EncounteredEventHandler(string spawn_id, string contact);

    public const float TILE = 32.0f;
    public const float PATROL_SPEED = 64.0f;        // 2 格/秒
    public const float WANDER_SPEED = 48.0f;        // 1.5 格/秒
    public const float CONTACT_DISTANCE = 22.0f;
    /// <summary>重生时如果玩家正站在位点上，要先走出这个圈才恢复接触判定（留点余量，免得在边界上抖）。</summary>
    public const float CONTACT_REARM_DISTANCE = CONTACT_DISTANCE * 1.6f;

    public static readonly Godot.Collections.Dictionary THREAT_COLORS = new()
    {
        { "green", Color.FromHtml("6fcf97") },
        { "yellow", Color.FromHtml("f2c94c") },
        { "red", Color.FromHtml("eb5757") },
        { "purple", Color.FromHtml("bb6bd9") },
    };

    /// <summary>精英贴图目录；`roaming_spawn.elite_marker` 存的是贴图 id（如 `marker_elite_red`）。</summary>
    public const string ELITE_MARKER_DIR = "res://assets/sprites/icons/";

    /// <summary>`CONTACT_DISTANCE` 的取值口（GDScript 侧用；P5 清零 GDScript 后收回成字段）。</summary>
    public static float contact_distance() => CONTACT_DISTANCE;

    /// <summary>`THREAT_COLORS` 的取值口（GDScript 侧用；同上）。</summary>
    public static Godot.Collections.Dictionary threat_colors() => THREAT_COLORS;

    [Export] public string spawn_id = "";
    /// <summary>明雷用表行（Resource），房间敌人用字典——所以这里不写死类型。</summary>
    [Export] public Variant row;
    [Export] public Resource team_row = null;
    [Export] public string behavior = "idle";
    [Export] public float alert_radius = 0.0f;
    [Export] public float chase_speed = 0.0f;
    [Export] public int respawn_sec = 0;
    [Export] public bool is_elite = false;
    /// <summary>潜行时「被发现」判定打折的比例（`combat_const.sneak_detect_reduce`，由控制层注入）。</summary>
    [Export] public float sneak_detect_reduce = 0.0f;

    [Export] public Vector2 home = Vector2.Zero;
    [Export] public Node2D player = null;
    [Export] public Path2D patrol_path = null;

    [Export] public Vector2 facing = Vector2.Down;
    [Export] public float patrol_progress = 0.0f;
    [Export] public float patrol_direction = 1.0f;
    [Export] public Vector2 wander_target = Vector2.Zero;
    [Export] public float wander_timer = 0.0f;
    [Export] public bool chasing = false;
    [Export] public bool defeated = false;
    [Export] public float respawn_left = 0.0f;

    private bool _encounter_latched = false;
    /// <summary>
    /// 接触判定「武装」与否：重生那一刻如果玩家就站在位点上，先不武装——否则明雷凭空出现在脚下、
    /// 同一帧就开战（和地图出口「先走出去一次」同一套做法）。
    /// </summary>
    public bool _contact_armed = true;
    /// <summary>精英标识（每帧做一点点呼吸感的明暗，证明「发光」是活的）。</summary>
    private Node2D _elite_glow = null;
    private float _glow_time = 0.0f;

    /// <summary>spawn_row 既可以是 roaming_spawn 的行，也可以是房间敌人用的字典。</summary>
    public void setup(TableDb table_db, Variant spawn_row, Resource team, Node2D player_node, Path2D path_node)
    {
        spawn_id = _row_get(spawn_row, "spawn_id", "").AsString();
        row = spawn_row;
        team_row = team;
        behavior = _row_get(spawn_row, "behavior", "idle").AsString();
        alert_radius = (float)_row_get(spawn_row, "alert_radius", 0.0).AsDouble();
        chase_speed = (float)_row_get(spawn_row, "chase_speed", 0.0).AsDouble();
        respawn_sec = _row_get(spawn_row, "respawn_sec", 0).AsInt32();
        is_elite = _row_get(spawn_row, "is_elite", false).AsBool();
        player = player_node;
        patrol_path = path_node;
        home = Position;
        wander_target = Position;

        CollisionLayer = 0;
        CollisionMask = 2;          // 同样被墙挡住
        _build_placeholder();
        if (behavior == "sleep")
        {
            Modulate = new Color(1, 1, 1, 0.75f);
        }
    }

    /// <summary>
    /// `randf_range` 的 C# 替身：走**全局** RNG（与 GDScript 的 `randf_range` 同一个源，
    /// 免得另起一个 `RandomNumberGenerator` 让全局 `seed()` 失效）——`GD` 上没有现成的区间版。
    /// </summary>
    private static float _randf_range(float from, float to) => GD.Randf() * (to - from) + from;

    /// <summary>表行与字典通用取值。</summary>
    private static Variant _row_get(Variant row, string key, Variant fallback)
    {
        if (row.VariantType == Variant.Type.Nil)
        {
            return fallback;
        }

        if (row.VariantType == Variant.Type.Dictionary)
        {
            Godot.Collections.Dictionary dict = row.AsGodotDictionary();
            return dict.ContainsKey(key) ? dict[key] : fallback;
        }

        GodotObject obj = row.AsGodotObject();
        if (obj == null || !_has_property(obj, key))
        {
            return fallback;
        }

        Variant value = obj.Get(key);
        return value.VariantType == Variant.Type.Nil ? fallback : value;
    }

    /// <summary>
    /// 表行是否真的有这个属性——`GodotObject.Get` 撞上不存在的属性会打一条 `Invalid get index`
    /// 错误（那会被日志错误门限抓成红），所以先查属性表再取。
    /// </summary>
    private static bool _has_property(GodotObject obj, string key)
    {
        foreach (Godot.Collections.Dictionary prop in obj.GetPropertyList())
        {
            if (prop["name"].AsString() == key)
            {
                return true;
            }
        }

        return false;
    }

    private void _build_placeholder()
    {
        var shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = 10.0f } };
        AddChild(shape);

        var team = team_row as EnemyTeamRow;
        string tag = team != null ? team.threat_tag : "green";
        Color color = THREAT_COLORS.ContainsKey(tag) ? THREAT_COLORS[tag].AsColor() : Colors.White;

        // 精英发光：光晕先加、身体后加，这样光晕在身体后面（不用 z_index=-1，那会被地面图层盖掉）
        if (is_elite)
        {
            Node2D glow = _make_elite_glow();
            glow.Name = "EliteGlow";
            AddChild(glow);
            _elite_glow = glow;
        }

        float size = is_elite ? 13.0f : 10.0f;
        AddChild(new Polygon2D
        {
            Name = "Body",
            Color = color,
            Polygon = new Vector2[]
            {
                new(-size, -size * 2), new(size, -size * 2), new(size, 0), new(-size, 0),
            },
        });

        // 威胁色环：玩家一眼能看出打不打得过（绿/黄/红/紫）
        if (alert_radius > 0.0f)
        {
            var points = new Vector2[32];
            float radius = alert_radius * TILE;
            for (int index = 0; index < 32; index++)
            {
                float angle = Mathf.Tau * index / 32.0f;
                points[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius + new Vector2(0, -10);
            }

            AddChild(new Polygon2D
            {
                Name = "AlertRing",
                Color = new Color(color.R, color.G, color.B, 0.18f),
                Polygon = points,
            });
        }

        var nose = new Polygon2D
        {
            Name = "Facing",
            Color = new Color(0, 0, 0, 0.55f),
            Polygon = new Vector2[]
            {
                new(-3, -3), new(3, -3), new(3, 3), new(-3, 3),
            },
            Position = facing * 15.0f + new Vector2(0, -10),
        };
        AddChild(nose);
    }

    /// <summary>精英标识用哪张图：`elite_marker` 是贴图 id，按同一套约定拼路径（空 id = 不拼）。</summary>
    public static string elite_marker_texture_path(string marker_id)
    {
        string trimmed = marker_id.StripEdges();
        if (trimmed.Length == 0)
        {
            return "";
        }

        return ELITE_MARKER_DIR + trimmed + ".png";
    }

    /// <summary>
    /// 贴图到位就用贴图（2026-10-04：美术已交付 `marker_elite_red`）；**贴图缺失才退回程序化八边形**
    /// ——资产没到不该变成「精英看不出是精英」。
    /// </summary>
    public Node2D _make_elite_glow()
    {
        string path = elite_marker_texture_path(elite_marker_id());
        if (path.Length > 0 && ResourceLoader.Exists(path))
        {
            return new Sprite2D
            {
                Texture = ResourceLoader.Load<Texture2D>(path),
                // 身体的占位块是 26×26（y 从 -26 到 0），贴图 32×32 → 居中套住它
                Position = new Vector2(0.0f, -13.0f),
            };
        }

        var fallback = new Polygon2D { Color = new Color(1.0f, 0.84f, 0.35f, 0.5f) };
        float gx = 13.0f + 7.0f;
        float cut = gx * 0.45f;
        // 八边形而不是矩形：连兜底也要看得出是「光晕」，不是给方块描了个边
        fallback.Polygon = new Vector2[]
        {
            new(-gx + cut, -gx * 2 - 2), new(gx - cut, -gx * 2 - 2),
            new(gx, -gx * 2 - 2 + cut), new(gx, 6 - cut),
            new(gx - cut, 6), new(-gx + cut, 6),
            new(-gx, 6 - cut), new(-gx, -gx * 2 - 2 + cut),
        };
        return fallback;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (defeated)
        {
            // 只给「有刷新时间」的倒计时（见 `mark_defeated`：-1 是不刷新）
            if (respawn_left > 0.0f)
            {
                respawn_left -= (float)delta;
                if (respawn_left <= 0.0f)
                {
                    _respawn();
                }
            }

            return;
        }

        _tick_elite_glow((float)delta);
        switch (behavior)
        {
            case "patrol":
                _process_patrol((float)delta);
                break;
            case "wander":
                _process_wander((float)delta);
                break;
            case "chase":
                _process_chase((float)delta);
                break;
            default:
                Velocity = Vector2.Zero;
                MoveAndSlide();
                break;
        }

        if (player != null)
        {
            _check_contact();
        }
    }

    private void _process_patrol(float delta)
    {
        if (patrol_path == null)
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        float length = patrol_path.Curve.GetBakedLength();
        if (length <= 0.0f)
        {
            return;
        }

        patrol_progress += patrol_direction * PATROL_SPEED * delta;
        if (patrol_progress >= length)
        {
            patrol_progress = length;
            patrol_direction = -1.0f;
        }
        else if (patrol_progress <= 0.0f)
        {
            patrol_progress = 0.0f;
            patrol_direction = 1.0f;
        }

        Vector2 target = patrol_path.ToGlobal(patrol_path.Curve.SampleBaked(patrol_progress));
        Vector2 step = target - GlobalPosition;
        Velocity = step.Length() > 1.0f ? step.Normalized() * PATROL_SPEED : Vector2.Zero;
        if (Velocity.Length() > 0.1f)
        {
            facing = Velocity.Normalized();
        }

        MoveAndSlide();
    }

    private void _process_wander(float delta)
    {
        wander_timer -= delta;
        if (wander_timer <= 0.0f)
        {
            wander_timer = _randf_range(2.0f, 4.0f);
            float angle = GD.Randf() * Mathf.Tau;
            wander_target = home
                + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _randf_range(1.0f, 3.0f) * TILE;
        }

        Vector2 step = wander_target - GlobalPosition;
        if (step.Length() > 4.0f)
        {
            Velocity = step.Normalized() * WANDER_SPEED;
            facing = Velocity.Normalized();
        }
        else
        {
            Velocity = Vector2.Zero;
        }

        MoveAndSlide();
    }

    private void _process_chase(float delta)
    {
        if (player == null)
        {
            return;
        }

        float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
        // 潜行时警戒半径缩小；一旦被发现就进入追击（设计：「潜行失败进入追击流程」）
        if (distance <= effective_alert_radius())
        {
            chasing = true;
        }

        if (chasing)
        {
            Vector2 step = player.GlobalPosition - GlobalPosition;
            if (step.Length() > 1.0f)
            {
                facing = step.Normalized();
                Velocity = step.Normalized() * Mathf.Max(chase_speed, 1.0f) * TILE;
            }
            else
            {
                Velocity = Vector2.Zero;
            }
        }
        else
        {
            Velocity = Vector2.Zero;
        }

        MoveAndSlide();
    }

    /// <summary>接触判定：贴身就算撞上；追击型在警戒圈内被发现则直接开战。</summary>
    /// <summary>接触判定的对外收口：控制器自检与用例都要能直接喂一次接触（原来是私有，GDScript 时代无所谓）。</summary>
    public void _check_contact()
    {
        float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
        if (!_contact_armed)
        {
            // 刚重生：玩家得先走出接触圈，才恢复接触判定（不然「回来了」= 立刻开战）
            if (distance > CONTACT_REARM_DISTANCE)
            {
                _contact_armed = true;
            }
            else
            {
                return;
            }
        }

        if (distance <= CONTACT_DISTANCE)
        {
            _emit_encounter(_contact_kind());
            return;
        }

        if (behavior == "chase" && distance <= effective_alert_radius())
        {
            _emit_encounter(Encounter.contact_spotted());
        }
    }

    /// <summary>
    /// 当前有效的警戒半径（像素）。潜行时按 `combat_const.sneak_detect_reduce` 打折
    /// ——设计 02：「按住潜行键进入潜行状态（移速降至 60%，被发现判定降低）」。
    /// </summary>
    public float effective_alert_radius()
    {
        float radius = alert_radius * TILE;
        if (_player_sneaking())
        {
            radius *= 1.0f - Mathf.Clamp(sneak_detect_reduce, 0.0f, 1.0f);
        }

        return radius;
    }

    private bool _player_sneaking()
    {
        if (player == null)
        {
            return false;
        }

        Variant value = player.Get("sneaking");
        return value.VariantType != Variant.Type.Nil && value.AsBool();
    }

    private string _contact_kind()
    {
        Vector2 to_player = (player.GlobalPosition - GlobalPosition).Normalized();
        float dot = facing.Normalized().Dot(to_player);
        if (behavior == "sleep")
        {
            return dot < 0.2f ? Encounter.contact_sleep() : Encounter.contact_front();
        }

        if (behavior == "chase" && chasing)
        {
            return Encounter.contact_caught();
        }

        if (dot < -0.3f)
        {
            return Encounter.contact_back();
        }

        return Encounter.contact_front();
    }

    private void _emit_encounter(string contact)
    {
        if (_encounter_latched)
        {
            return;
        }

        // 中立队伍（采药人）不主动攻击，撞上也不开战
        if (_is_neutral())
        {
            return;
        }

        _encounter_latched = true;
        EmitSignal(SignalName.Encountered, spawn_id, contact);
    }

    private bool _is_neutral()
    {
        if (team_row is not EnemyTeamRow team)
        {
            return false;
        }

        Godot.Collections.Array members = team.parsed_members();
        if (members.Count == 0)
        {
            return false;
        }

        Godot.Collections.Dictionary first = members[0].AsGodotDictionary();
        string enemy_id = first.ContainsKey("enemy_id") ? first["enemy_id"].AsString() : "";
        return enemy_id.StartsWith("en_herbalist");
    }

    /// <summary>打完这场就清掉，`respawn_sec` 秒后回来（0 = 不刷新）。</summary>
    public void mark_defeated(int respawn_seconds)
    {
        defeated = true;
        // `respawn_seconds <= 0` 用 -1 当「不刷新」的哨兵值：0 秒会被倒计时立刻满足，
        // 而设计里 0 的意思是「不刷新」（精英长 CD 或彻底不刷）。
        respawn_left = respawn_seconds > 0 ? respawn_seconds : -1.0f;
        Visible = false;
        // 不能无条件关掉物理处理：倒计时就写在 `_PhysicsProcess` 里。正数才留着跑倒计时。
        SetPhysicsProcess(respawn_seconds > 0);
    }

    private void _respawn()
    {
        defeated = false;
        _encounter_latched = false;
        // 玩家可能正站在位点上：先不武装接触判定（见 `_contact_armed`）
        _contact_armed = false;
        GlobalPosition = home;
        Visible = true;
        SetPhysicsProcess(true);
    }

    public void reset_latch()
    {
        _encounter_latched = false;
    }

    /// <summary>精英光晕的呼吸：明暗在小范围里来回，静止的发光块看起来也「活着」。</summary>
    private void _tick_elite_glow(float delta)
    {
        if (_elite_glow == null || !GodotObject.IsInstanceValid(_elite_glow))
        {
            return;
        }

        _glow_time += delta;
        Color modulate = _elite_glow.Modulate;
        modulate.A = 0.6f + 0.4f * Mathf.Sin(_glow_time * 2.6f);
        _elite_glow.Modulate = modulate;
    }

    /// <summary>用例读这个判断精英标识真的挂上了。</summary>
    public bool has_elite_glow() => _elite_glow != null && GodotObject.IsInstanceValid(_elite_glow);

    public string elite_marker_id() => _row_get(row, "elite_marker", "").AsString();
}
