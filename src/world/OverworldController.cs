using Godot;
using Godot.Collections;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.World;

/// <summary>
/// 大地图控制器（C# 版，对位原 `src/world/overworld_controller.gd`）。
///
/// 职责：
///   1. 实例化 `scenes/maps/overworld.tscn`（地编资产，只读不动）
///   2. 按 `roaming_spawn` 在 `Spawn_&lt;id&gt;` 位点生成明雷
///   3. 生成玩家、相机跟随、按 `map_region` 决定起始位置
///   4. 明雷接触 → 组 Encounter → 切到战斗场景
///   5. 战斗回来时应用「已清明雷」状态（含重生计时）
/// </summary>
public partial class OverworldController : Node2D
{
    private const string OVERWORLD_SCENE = "res://scenes/maps/overworld.tscn";
    private const string BATTLE_SCENE = "res://scenes/battle_screen.tscn";
    private const string LOCAL_RUN = "res://scenes/local_run.tscn";
    private const string PLACEHOLDER_SCENE = "res://scenes/placeholder_game.tscn";
    private const string CHARACTER_SCENE = "res://scenes/character_screen.tscn";
    private const string NPC_SCENE = "res://scenes/npc_panel.tscn";
    private const string WAYPOINT_SCENE = "res://scenes/waypoint_screen.tscn";
    private const string CLUE_SCENE = "res://scenes/clue_screen.tscn";
    private const string SHOP_SCENE = "res://scenes/shop_screen.tscn";

    // `CopyGuard`／`OverlayStack` 已是 C#（`src/ui/`），拿不到强类型的还是 `Call(...)`，
    // 只是类型参数从 `GDScript` 换成 `Script`——`GDScript` 去 `Load` 一个 `.cs` 会类型不匹配。
    private const string COPY_GUARD_SCRIPT = "res://src/ui/CopyGuard.cs";
    private const string OVERLAY_STACK_SCRIPT = "res://src/ui/OverlayStack.cs";

    /// <summary>站多近才能按 E 跟人说话（与 `LocalMapController` 同一个数）。</summary>
    public const float NPC_DISTANCE = 30.0f;
    /// <summary>没配行的人顶着的称呼（与 `LocalMapController` 同一套）。</summary>
    public const string NPC_SPEAKER = "路人";
    /// <summary>走进这个距离就算发现地标（设计 02：走进去才揭开）。</summary>
    public const float DISCOVER_DISTANCE = 96.0f;
    /// <summary>走进这个距离就进小地图。</summary>
    public const float PORTAL_DISTANCE = 26.0f;
    /// <summary>
    /// 入口要「先走出圈外一次」才武装：从小地图回大地图时落点就在入口位点上
    /// （设计 02：「返回大地图原位置」），不设这条会出现「进图 → 走到出口 → 又被立刻送回图」。
    /// </summary>
    public const float PORTAL_ARM_MARGIN = 8.0f;
    /// <summary>驿站交互距离（走到驿站图标旁边按 E）。</summary>
    public const float POST_DISTANCE = 40.0f;
    /// <summary>事件判定位点的交互距离。</summary>
    public const float EVENT_DISTANCE = 40.0f;
    /// <summary>地标图标（07 §8.1：id 配在 `map_region.icon`，资产在 assets/sprites/icons/）。</summary>
    private const string ICON_DIR = "res://assets/sprites/icons/";
    /// <summary>图标比标记点高一点，免得压住玩家/明雷。</summary>
    private static readonly Vector2 ICON_OFFSET = new(0, -26);
    /// <summary>玩家离地标多近才点亮高亮（像素；一格 32px，两格出头）。</summary>
    public const float HIGHLIGHT_DISTANCE = 70.0f;
    /// <summary>
    /// 走到某个**区域地标**多近才算「到了那儿」——剧情招募里 `join_scene` 指向区域节点的那几位
    /// 用这个半径判定。取值与地标高亮同一个（脚下 70px），因为那是全项目唯一一处
    /// 「你就在这个地标旁」的既有口径；要不要改成范围／触发点由设计定（Q51）。
    /// </summary>
    public const float REGION_JOIN_DISTANCE = HIGHLIGHT_DISTANCE;
    /// <summary>
    /// 相机默认倍数（设计 0.31.2「大地图扩容」）：一屏约 16×9 格，走起来才有"路程"。
    /// **写在代码里、不写进场景**：大地图正由地编按 2048×1536 重建，场景一换这份设置会被覆盖掉。
    /// </summary>
    public const float CAMERA_ZOOM = 2.0f;
    /// <summary>起始地点：新游戏从清风驿出发。</summary>
    private const string START_NODE = "n_qingfengyi";

    // GDScript 读不到 C# 的 `const`，测试与别处要用的这几个各配一个静态取值口
    public static float portal_distance() => PORTAL_DISTANCE;
    public static float portal_arm_margin() => PORTAL_ARM_MARGIN;
    public static float discover_distance() => DISCOVER_DISTANCE;
    public static float highlight_distance() => HIGHLIGHT_DISTANCE;
    public static float region_join_distance() => REGION_JOIN_DISTANCE;

    // ---------------------------------------------------------------- 测试注入点

    /// <summary>切战斗的接管点：用例用它把遭遇收走、不真换场景。</summary>
    [Export] public Callable battle_switch_handler;
    /// <summary>切场景的接管点：用例用它避免真换场景（顺便能断言切到哪张图）。</summary>
    [Export] public Callable scene_change_handler;
    [Export] public Variant state_override;
    /// <summary>存档设施（用例可注入临时目录）。</summary>
    [Export] public GodotObject save_store_override = null;

    public TableDb db = null;
    [Export] public Node2D world = null;
    [Export] public PlayerController player = null;
    [Export] public Camera2D camera = null;
    public Array enemies = new();
    public Label _status = null;
    /// <summary>战斗外增益 HUD（08）：大地图上也必须看得见打坐余韵／饱食还剩几分钟。</summary>
    public Label _field_label = null;
    private double _field_refresh_timer = 0.0;
    public Label _map_label = null;
    /// <summary>开局引导 HUD（设计 09 §3.1）：大地图上也常驻一行「当前目标」。</summary>
    public Label _guide_label = null;
    /// <summary>随机事件的提示行（设计 19 §四：只在事件存活期间出现）。</summary>
    public Label _event_label = null;
    /// <summary>当前活着的随机事件（一行 world_event；空 = 没有）。</summary>
    public Resource _event_row = null;
    public Node2D _event_marker = null;
    public double _event_timer = 0.0;
    private double _event_cooldown = 0.0;
    /// <summary>上一次领剧情节点时的区域 id（`_track_region_for_story()` 用它判断「走进新区域了没有」）。</summary>
    private string _last_claimed_region = "";
    private RngService _event_rng = null;
    private CanvasLayer _hud = null;
    private Array _portals = new();
    /// <summary>入口是否已武装（刚回图时人可能正踩在入口上，先离开圈外一次才算数）。</summary>
    public bool _portal_armed = false;
    /// <summary>驿站界面（覆盖在当前场景上的界面，关掉就 queue_free）。</summary>
    public Node waypoint_panel = null;
    /// <summary>线索本（K 打开：野外事件按地标分组）。</summary>
    public Node clue_panel = null;
    /// <summary>行商货架（随机事件就地开张；与线索本同一套浮层，出栈即关）。</summary>
    public Node shop_panel = null;
    /// <summary>浮层栈（设计 18.1）：线索本／驿站／角色面板都压在它上面。</summary>
    private GodotObject _overlays = null;
    public SaveService _save_service = null;
    /// <summary>地图揭开与驿站规则的唯一来源（状态写在存档里）。</summary>
    public WorldMapService world_map = null;
    /// <summary>事件判定位点：{check_id, node, position}。</summary>
    public Array events = new();
    /// <summary>观察点位点：{point_id, node, position}（设计 20 §3.2）。</summary>
    public Array flavor_points = new();
    /// <summary>大地图上的 NPC 站位（`Characters/npc_slot_0N`）。</summary>
    public Array npcs = new();
    /// <summary>NPC 交往面板（覆盖在当前场景上的接口，关掉就 queue_free）。</summary>
    public Node npc_panel = null;
    /// <summary>地标图标：node_id → Sprite2D（见 `_build_node_icons`）。</summary>
    public Dictionary _node_icons = new();
    /// <summary>条件地表层（`Conditional`，0.32.0）：大地图上那条「藏宝图上的细径」。</summary>
    private TileMapLayer _conditional_layer = null;
    /// <summary>「这个地标的贴图两张都找不到」已经报过没（只报一次，别每帧刷屏）。</summary>
    private Dictionary _icon_missing_warned = new();
    /// <summary>地标名字（node_id → Label，见 `_build_node_labels`）。</summary>
    public Dictionary _node_labels = new();
    /// <summary>指路牌文案（node_id → Label，见 `_build_signposts`）。</summary>
    private Dictionary _signpost_labels = new();
    /// <summary>脚下的地标高亮（叠在最近的地标上）。</summary>
    private Sprite2D _highlight = null;
    private EventCheckService _event_service = null;
    private bool _was_sneaking = false;

    public override void _Ready()
    {
        // 这个场景自检验的正是**明雷机制**（生成／接触／巡逻／追击／回图落点）。
        // 0.8.1 起大地图明雷默认关着，所以自检跑之前先把会话级开关打开；
        // 开关本身的两种状态由 `_run_world_selftest()` 里的两条断言各自钉一遍。
        if (_has_user_arg("--world-selftest"))
        {
            GodotObject session_node = session();
            if (session_node != null)
            {
                session_node.Set("roaming_enabled_override", 1);
            }
        }

        setup();
        if (_has_user_arg("--world-selftest"))
        {
            Callable.From(_run_world_selftest).CallDeferred();
        }
    }

    public void setup()
    {
        if (world != null)
        {
            return;
        }

        InputSetup.ensure();
        db = _resolve_db();
        var packed = GD.Load<PackedScene>(OVERWORLD_SCENE);
        world = packed.Instantiate<Node2D>();
        AddChild(world);
        var ysort = world.GetNodeOrNull<Node2D>("YSort");
        if (ysort != null)
        {
            ysort.YSortEnabled = true;
        }

        _spawn_player();
        _spawn_enemies();
        _conditional_layer = world.GetNodeOrNull<TileMapLayer>("Conditional");
        _apply_conditional_layer();
        _build_node_icons();
        _build_node_labels();
        _build_signposts();
        _collect_portals();
        _collect_events();
        _collect_flavor_points();
        _collect_npc_slots();
        world_map = WorldMapService.create(db, current_state());
        world_map.apply_initial_reveals();
        _build_status_label();
        _refresh_status();
        _build_map_label();
        _build_guide_label();
        _build_event_label();
        _check_region_recruits();
        // 剧情节点与派生引导旗标（0.22.0）：大地图上也有「不限地点」的节点（药王谷那条），
        // 以及靠升级／买东西才会变真的备货条件。
        GuideService.refresh_derived_flags(db, current_state());
        _claim_story_nodes();
        _apply_fog();
        _apply_conditional_layer();
        // 放在最后：状态栏提示与揭雾都不该被「刚脱身」这一步盖掉
        _push_player_out_of_contact();
    }

    /// <summary>
    /// 领取大地图上能领的剧情节点（**`place_id` 留空的**＋**指向当前区域节点的**那些**）。
    /// 返回领到的节点，供状态栏播报。
    ///
    /// **为什么传当前区域 id 而不是空串**（2026-10-04 修）：`StoryService.place_matches` 对**空串**的口径是
    /// 「**只**匹配 `place_id` 留空的节点」——所以 21 §九 那条把地点写成**区域节点**的
    /// （林铁山的「镖车暗格」在落雁坡，`opp_gang.place_id = n_luoyanpo`）**在游戏里永远领不到**。
    /// `place_matches` 对「区域 id」是超集：`place_id` 留空的那些仍然会命中（`want.is_empty()` 先判）✓。
    /// </summary>
    public Array _claim_story_nodes()
    {
        GodotObject state = current_state();
        if (state == null || db == null)
        {
            return new Array();
        }

        string place = current_region_id();
        _last_claimed_region = place;
        Array claimed = StoryService.claim_for(db, state, place);
        if (claimed.Count == 0)
        {
            return new Array();
        }

        var names = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in claimed)
        {
            Dictionary entry = entry_variant.AsGodotDictionary();
            names.Add(_s(entry, "text_cn", _s(entry, "node_id", "")));
        }

        Dictionary advance = ChapterService.try_advance(db, state);
        if (_b(advance, "advanced", false))
        {
            names.Add($"章节推进：{_s(advance, "name", "")}");
        }

        string advance_text = _s(advance, "text", "");
        if (advance_text.Length > 0)
        {
            names.Add(advance_text);
        }

        if (_status != null && names.Count > 0)
        {
            _status.Text = string.Join("；", names);
        }

        _refresh_guide();
        return claimed;
    }

    /// <summary>
    /// 走进新区域时再领一次：玩家是**走**过去的，而 `_claim_story_nodes()` 只在场景载入时跑过一遍。
    /// 与 `_check_region_recruits()` 同一套口径——走到地标跟前就算「到了」。
    /// </summary>
    public void _track_region_for_story()
    {
        if (player == null || db == null)
        {
            return;
        }

        if (_last_claimed_region == current_region_id())
        {
            return;
        }

        _claim_story_nodes();
    }

    public GodotObject current_state()
    {
        if (state_override.VariantType != Variant.Type.Nil)
        {
            return state_override.AsGodotObject();
        }

        GodotObject session_node = _session_node("GameSession");
        return session_node?.Get("state").AsGodotObject();
    }

    public GodotObject session() => _session_node("GameSession");

    // ------------------------------------------------------------------ 生成

    private void _spawn_player()
    {
        player = new PlayerController();
        player.Name = "Player";
        Vector2 spawn_position = _start_position();
        player.Position = spawn_position;
        _add_to_world(ysort_node(), player);
        player.ZIndex = 1;

        camera = world.GetNodeOrNull<Camera2D>("Camera");
        if (camera != null)
        {
            camera.GlobalPosition = player.GlobalPosition;
            // 2× 缩放（设计 0.31.2）：写在这里而不是场景里，理由见 CAMERA_ZOOM 的注释
            camera.Zoom = new Vector2(CAMERA_ZOOM, CAMERA_ZOOM);
        }
    }

    private Vector2 _start_position()
    {
        GodotObject session_node = session();
        if (session_node != null)
        {
            Vector2 stored = session_node.Get("world_position").AsVector2();
            if (stored != Vector2.Zero)
            {
                return stored;
            }
        }

        var marker = world.GetNodeOrNull<Node2D>($"Markers/Node_{START_NODE}");
        return marker != null ? marker.Position : new Vector2(512, 384);
    }

    /// <summary>
    /// 回到大地图时别站在明雷身上。
    ///
    /// 上一场没赢（败北或**撤退**）时，`world_position` 就是当时的接触点，也就是明雷身边；
    /// 场景重建后明雷的 `_encounter_latched` 归零，不推开就会瞬间再抓一次——「跑得掉」就成了一句空话。
    /// 只在真的重叠时推开（沿玩家与敌人都连线上往外推），正常落点不受影响。
    /// </summary>
    public void _push_player_out_of_contact()
    {
        if (player == null)
        {
            return;
        }

        float margin = RoamingEnemy.contact_distance() + 30.0f;
        foreach (Variant enemy_variant in enemies)
        {
            RoamingEnemy enemy = enemy_variant.AsGodotObject() as RoamingEnemy;
            if (enemy == null || enemy.defeated || !enemy.Visible)
            {
                continue;
            }

            Vector2 away = player.GlobalPosition - enemy.GlobalPosition;
            if (away.Length() > RoamingEnemy.contact_distance())
            {
                continue;
            }

            if (away.Length() < 0.01f)
            {
                away = Vector2.Down;
            }

            player.GlobalPosition = enemy.GlobalPosition + away.Normalized() * margin;
            // 敌方的接触闩也清一下，免得它把「刚才那一下」当成已经触发过
            enemy.reset_latch();
            _set_status("刚从遭遇里脱身，先退开几步");
        }
    }

    /// <summary>
    /// 大地图明雷开关（0.8.1）：`feature_toggle.overworld_roaming_enemy`，默认 0 = 大地图不出现明雷。
    /// 会话级覆盖（用例／调试）在 `GameSession.roaming_enabled_override`——场景重建也丢不掉。
    /// </summary>
    public bool roaming_enabled()
    {
        GodotObject session_node = session();
        if (session_node != null)
        {
            int override_value = session_node.Get("roaming_enabled_override").AsInt32();
            if (override_value >= 0)
            {
                return override_value == 1;
            }
        }

        Resource row = db.get_row("feature_toggle", "overworld_roaming_enemy");
        // 表里没这行就当"开着"：别让老数据静默变成一张空地图
        return row == null || row.Get("value").AsInt32() == 1;
    }

    /// <summary>
    /// 重建大地图明雷（开关改变后调用；闭环用例靠它把开关打开再跑明雷那几段）。
    /// 幂等：先清掉现有的，再按会话状态重新生成，最后把玩家从接触圈里推开。
    /// </summary>
    public void rebuild_roaming_enemies()
    {
        foreach (Variant enemy_variant in enemies)
        {
            GodotObject enemy = enemy_variant.AsGodotObject();
            if (enemy != null && GodotObject.IsInstanceValid(enemy))
            {
                ((Node)enemy).QueueFree();
            }
        }

        enemies.Clear();
        _spawn_enemies();
        _push_player_out_of_contact();
    }

    private void _spawn_enemies()
    {
        GodotObject session_node = session();
        long now = (long)Time.GetUnixTimeFromSystem();
        // 0.8.1：大地图明雷由 `feature_toggle.overworld_roaming_enemy` 控制（默认 0 = 不出现）。
        // 机制本身一行代码都没删——置 1 就照旧（闭环用例会打开它跑胜／败两种结局）。
        if (!roaming_enabled())
        {
            return;
        }

        foreach (Variant row_variant in db.rows("roaming_spawn"))
        {
            Resource row = row_variant.As<Resource>();
            string spawn_id = row.Get("spawn_id").AsString();
            var enemy = new RoamingEnemy();
            enemy.Name = $"Spawn_{spawn_id}";
            var marker = world.GetNodeOrNull<Node2D>($"Markers/Spawn_{spawn_id}");
            if (marker == null)
            {
                GD.PushError($"[Overworld] 地图里没有位点 Markers/Spawn_{spawn_id}");
                continue;
            }

            enemy.Position = marker.Position;
            Resource team = db.get_row("enemy_team", row.Get("team_id").AsString());
            var path = world.GetNodeOrNull<Path2D>($"Markers/Patrol_{row.Get("patrol_path_id").AsString()}");
            enemy.setup(db, row, team, player, path);
            enemy.sneak_detect_reduce = sneak_detect_reduce();
            // C# 的信号名**不转 snake_case**：暴露出来叫 `Encountered`
            enemy.Encountered += _on_encountered;
            _add_to_world(ysort_node(), enemy);
            enemies.Add(enemy);
            // 战斗回来时应用「已清」状态
            if (session_node != null)
            {
                Dictionary cleared = session_node.Get("cleared_spawns").AsGodotDictionary();
                if (cleared.ContainsKey(spawn_id))
                {
                    long until = cleared[spawn_id].AsInt64();
                    if (until < 0)
                    {
                        enemy.mark_defeated(0);
                    }
                    else if (until > now)
                    {
                        enemy.mark_defeated((int)(until - now));
                    }
                    else
                    {
                        // 冷却到了：**把记录删掉**再让它出现——留着的话 `cleared_spawns.has(id)` 还是真
                        // （「已清」与「已在场上」两种状态会同时成立，读它的人就被骗了）。
                        cleared.Remove(spawn_id);
                    }
                }
            }
        }
    }

    private void _add_to_world(Node parent, Node node)
    {
        if (parent != null)
        {
            parent.AddChild(node);
        }
        else
        {
            world.AddChild(node);
        }
    }

    private Node2D ysort_node() => world.GetNodeOrNull<Node2D>("YSort");

    /// <summary>
    /// 地标图标：设计 02「已探索的地标显示在地图上」+ 07 §8.1 的 7 个图标 id。
    /// 状态口径（开发侧定）：已揭开 → 正常图标；**未揭开 → 不画**（不剧透）；
    /// 已揭开但本章去不了（`LOCKED_SCENES`）→ `_dim` 暗版；脚下最近的已揭开地标 → 高亮。
    /// </summary>
    private void _build_node_icons()
    {
        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            var marker = world.GetNodeOrNull<Node2D>($"Markers/Node_{node_id}");
            if (marker == null)
            {
                continue;
            }

            // 地编在地图上留了一份 `Markers/Node_<id>/icon`（**摆位参考**，给地图预览用）：
            // 它默认可见、又不认揭雾，于是开局会把黑风寨／落雁坡／石隙的图标直接亮在图上——
            // 与设计 0.32.0「没探索的地方完全不存在」直接冲突。运行期一律收掉那一份。
            if (marker.GetNodeOrNull<Node>("icon") is CanvasItem preview_icon)
            {
                preview_icon.Visible = false;
            }

            var sprite = new Sprite2D
            {
                Name = $"Icon_{node_id}",
                Position = marker.Position + ICON_OFFSET,
                Visible = false,
            };
            _add_to_world(ysort_node(), sprite);
            _node_icons[node_id] = sprite;
        }

        // 高亮叠在最后：和图标同层，但画在它们上面
        string highlight_file = $"{ICON_DIR}icon_highlight.png";
        if (ResourceLoader.Exists(highlight_file))
        {
            _highlight = new Sprite2D
            {
                Name = "NodeHighlight",
                Texture = GD.Load<Texture2D>(highlight_file),
                Visible = false,
            };
            _add_to_world(ysort_node(), _highlight);
        }

        refresh_node_icons();
    }

    /// <summary>
    /// 条件地表层（`Conditional`）：整层显隐，判据在 `WorldMapService.conditional_layer_rule`——
    /// 这张图上那些地标里「持有类解锁」的，东西到手就显示（石隙细径绑 `item_treasure_map`）。
    /// </summary>
    public void _apply_conditional_layer()
    {
        if (_conditional_layer == null)
        {
            return;
        }

        _conditional_layer.Visible =
            WorldMapService.conditional_layer_visible(db, current_state(), _landmark_node_ids());
    }

    /// <summary>这张图上真的摆了位点的地标（条件地表层看的是它们的 `unlock_condition`）。</summary>
    public string[] _landmark_node_ids()
    {
        var out_list = new System.Collections.Generic.List<string>();
        if (world == null)
        {
            return out_list.ToArray();
        }

        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            if (world.GetNodeOrNull<Node2D>($"Markers/Node_{row.Get("node_id").AsString()}") != null)
            {
                out_list.Add(row.Get("node_id").AsString());
            }
        }

        return out_list.ToArray();
    }

    /// <summary>按揭雾/锁定状态更新图标（揭开一个新地标后要能立刻看到）。</summary>
    public void refresh_node_icons()
    {
        if (world_map == null)
        {
            return;
        }

        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            Sprite2D sprite = _icon_of(node_id);
            if (sprite == null)
            {
                continue;
            }

            bool revealed = world_map.is_revealed(node_id);
            bool locked = _is_locked_scene(row.Get("enter_scene").AsString());
            string icon_id = row.Get("icon").AsString();
            if (icon_id.Length == 0 || !revealed)
            {
                sprite.Visible = false;
                continue;
            }

            string file = $"{ICON_DIR}{icon_id}{(locked ? "_dim" : "")}.png";
            if (!ResourceLoader.Exists(file))
            {
                // 暗版缺失就退回正常版：少一层状态，但不至于什么都不画
                file = $"{ICON_DIR}{icon_id}.png";
            }

            if (ResourceLoader.Exists(file))
            {
                sprite.Texture = GD.Load<Texture2D>(file);
                sprite.Visible = true;
            }
            else if (!_icon_missing_warned.ContainsKey(node_id))
            {
                // **两版都没有**：这个地标会"什么都不画"——玩家只会觉得这里本来就没图标。
                // 出声（每个地标只报一次）：数据里 icon 写错、或美术的图没进仓库，都该看得见。
                _icon_missing_warned[node_id] = true;
                GD.PushError($"[Overworld] 地标 {node_id} 的图标 {icon_id} 两张贴图都不存在（{ICON_DIR}）");
            }
        }

        _update_highlight();
        refresh_node_labels();
    }

    /// <summary>
    /// 地标名字：图标之外再写一行字（`map_region.name_cn`），玩家不用挨个走进去才知道那是哪。
    /// 规则与图标一致：**没揭开的没有名字**（提前把地名写出来就是剧透）；本章去不了的用暗色字。
    ///
    /// **2026-10-04（0.31.2）解耦**：名字**只看"揭没揭开"**，不再看 `map_region.icon` 有没有值。
    /// </summary>
    private void _build_node_labels()
    {
        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            var marker = world.GetNodeOrNull<Node2D>($"Markers/Node_{node_id}");
            if (marker == null)
            {
                continue;
            }

            var label = new Label
            {
                Name = $"NodeLabel_{node_id}",
                Text = row.Get("name_cn").AsString(),
                Visible = false,
                CustomMinimumSize = new Vector2(140, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = marker.Position + ICON_OFFSET + new Vector2(-70, 16),
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.92f));
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
            _add_to_world(ysort_node(), label);
            _node_labels[node_id] = label;
        }

        refresh_node_labels();
    }

    public void refresh_node_labels()
    {
        if (world_map == null)
        {
            return;
        }

        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            Label label = _label_of(node_id);
            if (label == null)
            {
                continue;
            }

            // 名字的条件**只有一条**：揭没揭开（与 `icon` 解耦，见 `_build_node_labels` 的注释）
            if (!world_map.is_revealed(node_id))
            {
                label.Visible = false;
                continue;
            }

            label.Text = row.Get("name_cn").AsString();
            label.Visible = true;
            // 本章去不了的：字压暗（和 _dim 图标同一口径，不加额外文字免得挡住地图）
            label.Modulate = _is_locked_scene(row.Get("enter_scene").AsString())
                ? new Color(0.62f, 0.62f, 0.62f)
                : Colors.White;
        }

        // 指路牌同理：跟着「这个地标揭没揭开」走（没揭开就不知道通往哪儿）
        foreach (Variant key in _signpost_labels.Keys)
        {
            string node_id = key.AsString();
            var label = _signpost_labels[node_id].AsGodotObject() as Label;
            if (label != null)
            {
                label.Visible = world_map.is_revealed(node_id);
            }
        }
    }

    /// <summary>
    /// 指路牌（设计 0.31.2）：`map_region.signpost_cn` 非空的行，代码在**地编摆的 `Markers/Sign_&lt;node_id&gt;`**
    /// 位点旁边把那句话摆出来。**文案只在表里**——表里空 = 不摆。没摆位点时静默跳过。
    /// </summary>
    private void _build_signposts()
    {
        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            string text = row.Get("signpost_cn").AsString().StripEdges();
            if (text.Length == 0)
            {
                continue;
            }

            string node_id = row.Get("node_id").AsString();
            var marker = world.GetNodeOrNull<Node2D>($"Markers/Sign_{node_id}");
            if (marker == null)
            {
                continue;
            }

            var label = new Label
            {
                Name = $"SignLabel_{node_id}",
                Text = text,
                Visible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = marker.Position + new Vector2(-70, -20),
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.92f));
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
            _add_to_world(ysort_node(), label);
            _signpost_labels[node_id] = label;
        }

        refresh_node_labels();
    }

    /// <summary>高亮：离玩家最近的**已揭开**地标叠一层 icon_highlight。</summary>
    public void _update_highlight()
    {
        if (_highlight == null || !GodotObject.IsInstanceValid(_highlight))
        {
            return;
        }

        if (player == null)
        {
            _highlight.Visible = false;
            return;
        }

        Sprite2D best = null;
        float best_distance = float.PositiveInfinity;
        foreach (Variant key in _node_icons.Keys)
        {
            var sprite = _node_icons[key].AsGodotObject() as Sprite2D;
            if (sprite == null || !sprite.Visible)
            {
                continue;
            }

            float distance = sprite.GlobalPosition.DistanceTo(player.GlobalPosition);
            if (distance < best_distance)
            {
                best_distance = distance;
                best = sprite;
            }
        }

        // 只有凑得够近才算「就在这个地标旁」，免得全图都亮着
        bool shown = best != null && best_distance <= HIGHLIGHT_DISTANCE;
        _highlight.Visible = shown;
        if (shown)
        {
            _highlight.GlobalPosition = best.GlobalPosition;
        }
    }

    /// <summary>地标图标（测试与截图读它）。</summary>
    public Sprite2D node_icon(string node_id) => _icon_of(node_id);

    /// <summary>脚下的高亮现在亮不亮（用例读它）。</summary>
    public bool highlight_visible() =>
        _highlight != null && GodotObject.IsInstanceValid(_highlight) && _highlight.Visible;

    private void _build_status_label()
    {
        _status = new Label
        {
            Name = "Status",
            Position = new Vector2(12, 8),
        };
        _status.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.9f));
        _status.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _status.AddThemeConstantOverride("shadow_offset_x", 1);
        _status.AddThemeConstantOverride("shadow_offset_y", 1);
        _hud_layer().AddChild(_status);
        // 第三行（状态栏 y=8、揭雾进度 y=34 各占一行）——放 30 会和 MapProgress 叠在一起
        _field_label = FieldBuffHud.build_label(new Vector2(12, 56), 12);
        _hud_layer().AddChild(_field_label);
    }

    private void _refresh_status()
    {
        if (_status == null)
        {
            return;
        }

        string region = _region_name();
        // 线索本 K 必须写出来：设计 03 要求「线索必须能被找到」，藏着一个键等于没有
        _status.Text = $"{region}　移动 WASD／方向键　潜行 Shift　交互 E　看人 Q　角色 Tab　行囊 I　线索 K　返回 Esc";
        _refresh_field_buffs();
    }

    /// <summary>战斗外增益（08）：剩余分钟随时间走，所以按秒刷一次；没有增益时这行是空的。</summary>
    public void _refresh_field_buffs()
    {
        if (_field_label == null)
        {
            return;
        }

        GodotObject session_node = session();
        Array rows = session_node != null
            ? session_node.Call("active_field_buffs", -1).AsGodotArray()
            : new Array();
        _field_label.Text = FieldBuffHud.text_of(db, rows);
    }

    /// <summary>大地图「揭雾」HUD：已探索几个地标、还剩哪些没走到。</summary>
    private void _build_map_label()
    {
        _map_label = new Label
        {
            Name = "MapProgress",
            Position = new Vector2(12, 34),
        };
        _map_label.AddThemeColorOverride("font_color", new Color("8ab4f8"));
        _map_label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _map_label.AddThemeConstantOverride("shadow_offset_x", 1);
        _map_label.AddThemeConstantOverride("shadow_offset_y", 1);
        _hud_layer().AddChild(_map_label);
        _refresh_map_label();
    }

    /// <summary>开局引导（设计 09 §3.1）：常驻一行「当前目标」（状态栏 8／揭雾 34／增益 56 之后的第四行）。</summary>
    private void _build_guide_label()
    {
        _guide_label = new Label
        {
            Name = "Guide",
            Position = new Vector2(12, 80),
        };
        _guide_label.AddThemeColorOverride("font_color", new Color("8fe3ff"));
        _guide_label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _guide_label.AddThemeConstantOverride("shadow_offset_x", 1);
        _guide_label.AddThemeConstantOverride("shadow_offset_y", 1);
        _hud_layer().AddChild(_guide_label);
        _refresh_guide();
    }

    /// <summary>
    /// 随机事件的提示行（设计 19 §四／0.25.1）：**只在事件存活期间出现**，
    /// 文案取 `world_event.prompt_text_cn`（模糊的方向性提示，例「远处有车马声」）。
    /// </summary>
    private void _build_event_label()
    {
        _event_label = new Label
        {
            Name = "WorldEventPrompt",
            Position = new Vector2(12, 104),
        };
        _event_label.AddThemeColorOverride("font_color", new Color("ffd479"));
        _event_label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _event_label.AddThemeConstantOverride("shadow_offset_x", 1);
        _event_label.AddThemeConstantOverride("shadow_offset_y", 1);
        _hud_layer().AddChild(_event_label);
        _refresh_event_prompt();
    }

    private void _refresh_guide()
    {
        if (_guide_label == null)
        {
            return;
        }

        _guide_label.Text = GuideService.hud_text(db, current_state());
    }

    /// <summary>自检／用例读这一行的文案。</summary>
    public string guide_text() => _guide_label != null ? _guide_label.Text : "";

    /// <summary>
    /// 剧情招募（09 §3.2）里 `join_scene` 指向**区域地标**的那几位：走到跟前就入队。
    /// 数据能表达的粒度是「区域」，所以这里按「玩家与 `Markers/Node_&lt;node_id&gt;` 的距离」判定。
    /// </summary>
    public Array _check_region_recruits()
    {
        if (player == null || world_map == null)
        {
            return new Array();
        }

        var joined = new Array();
        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            var marker = world.GetNodeOrNull<Node2D>($"Markers/Node_{node_id}");
            if (marker == null)
            {
                continue;
            }

            if (marker.Position.DistanceTo(player.Position) > REGION_JOIN_DISTANCE)
            {
                continue;
            }

            Array results = RecruitService.join_all_for_region(db, current_state(), node_id);
            if (results.Count == 0)
            {
                continue;
            }

            foreach (Variant entry in results)
            {
                joined.Add(entry);
            }
        }

        if (joined.Count == 0)
        {
            return joined;
        }

        var names = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in joined)
        {
            names.Add(_s(entry_variant.AsGodotDictionary(), "name_cn", ""));
        }

        _refresh_guide();
        // 同小地图那一处：存档失败时把结果并进同一行，别把「谁加入了队伍」盖掉
        Dictionary saved = autosave("剧情招募");
        string text = $"{string.Join("、", names)} 加入了队伍";
        if (!_b(saved, "ok", false))
        {
            text += $"　（自动存档失败：{_s(saved, "error", "未知原因")}，这段进度只在本局里）";
        }

        _set_status(text);
        return joined;
    }

    /// <summary>HUD 必须挂在 CanvasLayer 上：挂在世界节点上的话会跟着相机跑出屏幕。</summary>
    private CanvasLayer _hud_layer()
    {
        if (_hud != null && GodotObject.IsInstanceValid(_hud))
        {
            return _hud;
        }

        _hud = new CanvasLayer { Name = "HUD" };
        AddChild(_hud);
        return _hud;
    }

    private void _refresh_map_label()
    {
        if (_map_label == null || world_map == null)
        {
            return;
        }

        _map_label.Text = world_map.progress_text();
    }

    public string map_progress_text() => _map_label != null ? _map_label.Text : "";

    private string _region_name()
    {
        Resource row = db.get_row("map_region", START_NODE);
        if (row == null)
        {
            return "";
        }

        // **大区名来自表**（`map_region.region_name_cn`）：以前这里写的是代码兜底串，
        // 违反「文案在表里」。表里还没填时退回节点名——**不编中文**。
        string from_table = row.Get("region_name_cn").AsString().StripEdges();
        return from_table.Length > 0 ? from_table : row.Get("name_cn").AsString();
    }

    // ------------------------------------------------------------------ 帧循环

    public override void _Process(double delta)
    {
        if (camera != null && player != null)
        {
            camera.GlobalPosition = player.GlobalPosition;
        }

        if (player == null)
        {
            return;
        }

        _check_reveals();
        _check_portal();
        // 条件地表（石隙那条细径）跟背包走：藏宝图可能是在这张图上偷到／拿到的
        _apply_conditional_layer();
        _track_sneak();
        _update_highlight();
        // 剧情招募里那几位 `join_scene` 指向区域地标的：走到地标跟前就算「遇上了」
        _check_region_recruits();
        // 剧情节点同理：**走进新区域时再领一次**
        _track_region_for_story();
        // 战斗外增益的剩余分钟按现实时间走：每秒刷一次就够
        _field_refresh_timer -= delta;
        if (_field_refresh_timer <= 0.0)
        {
            _field_refresh_timer = 1.0;
            _refresh_field_buffs();
        }

        // 随机事件（设计 19 §四）：每帧推进存活计时／接触判定（很轻，只有一两个判断）
        _update_world_event(delta);
    }

    // ---------------------------------------------------------------- 随机事件

    /// <summary>
    /// 随机事件的生命周期（设计 19 §四 ＋ 0.25.1 的提示两条规则）：
    /// 冷却到点 → 在当前区域按权重抽一条 → 在玩家附近放一个**临时淡色光点** ＋ HUD 一行模糊提示
    /// → 走上去触发／超时或走远消失。**同一时刻最多一条**。
    /// </summary>
    private void _update_world_event(double delta)
    {
        if (_event_cooldown > 0.0)
        {
            _event_cooldown = System.Math.Max(0.0, _event_cooldown - delta);
        }

        if (_event_row == null)
        {
            // 冷却好了、附近又没别的交互位点时才考虑刷一个（别在事件判定/驿站门口刷）
            if (_event_cooldown <= 0.0 && player != null && event_near_player().Length == 0
                && !post_station_near())
            {
                _try_spawn_world_event();
            }

            return;
        }

        _event_timer -= delta;
        bool gone = _event_timer <= 0.0;
        if (_event_marker != null && GodotObject.IsInstanceValid(_event_marker) && player != null)
        {
            float distance = player.Position.DistanceTo(_event_marker.Position);
            if (distance <= WorldEventService.trigger_distance())
            {
                _trigger_world_event();
                return;
            }

            if (distance > WorldEventService.despawn_distance())
            {
                gone = true;
            }
        }

        if (gone)
        {
            _despawn_world_event();
        }
    }

    /// <summary>当前区域（事件池按区域给：商队走官道、弟子在野外）。</summary>
    public string current_region_id()
    {
        if (player == null || world == null)
        {
            return "";
        }

        string best = "";
        float best_distance = 1e9f;
        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            var marker = world.GetNodeOrNull<Node2D>($"Markers/Node_{row.Get("node_id").AsString()}");
            if (marker == null)
            {
                continue;
            }

            float distance = player.Position.DistanceTo(marker.Position);
            if (distance < best_distance)
            {
                best_distance = distance;
                best = row.Get("node_id").AsString();
            }
        }

        return best;
    }

    private bool _try_spawn_world_event()
    {
        string region_id = current_region_id();
        if (region_id.Length == 0)
        {
            return false;
        }

        GodotObject state = current_state();
        if (WorldEventService.eligible(db, state, region_id).Count == 0)
        {
            _event_cooldown = WorldEventService.cooldown();
            return false;
        }

        _event_rng ??= RngService.create_random();
        Resource row = WorldEventService.pick(db, state, region_id, _event_rng.Call("randf").AsDouble());
        if (row == null)
        {
            return false;
        }

        _event_row = row;
        _event_timer = WorldEventService.lifetime();
        _place_event_marker();
        _refresh_event_prompt();
        return true;
    }

    /// <summary>临时标记：比地标小、淡色、会随事件消失（0.25.1 的「一明一暗两处提示」里明的那一处）。</summary>
    private void _place_event_marker()
    {
        if (world == null || player == null)
        {
            return;
        }

        double span = WorldEventService.spawn_max() - WorldEventService.spawn_min();
        double angle = _event_rng != null ? _event_rng.Call("randf").AsDouble() * Mathf.Tau : 0.0;
        double distance = WorldEventService.spawn_min() + span * 0.5;
        Vector2 spot = player.Position
            + new Vector2((float)System.Math.Cos(angle), (float)System.Math.Sin(angle)) * (float)distance;
        var marker = new Polygon2D
        {
            Name = "WorldEventMark",
            // 淡色光点：菱形，不抢地标的视线
            Polygon = new[]
            {
                new Vector2(0, -7), new Vector2(7, 0), new Vector2(0, 7), new Vector2(-7, 0),
            },
            Color = new Color(1.0f, 0.83f, 0.47f, 0.55f),
            Position = spot,
            ZIndex = -1,
        };
        _add_to_world(ysort_node(), marker);
        _event_marker = marker;
    }

    private void _refresh_event_prompt()
    {
        if (_event_label == null)
        {
            return;
        }

        if (_event_row == null)
        {
            _event_label.Text = "";
            return;
        }

        // HUD 只说模糊的那一句（「远处有车马声」），不说「商队」——保留探索感
        _event_label.Text = $"（附近有动静）{_event_row.Get("prompt_text_cn").AsString()}";
    }

    private void _despawn_world_event()
    {
        if (_event_marker != null && GodotObject.IsInstanceValid(_event_marker))
        {
            _event_marker.QueueFree();
        }

        _event_marker = null;
        _event_row = null;
        _event_timer = 0.0;
        _event_cooldown = WorldEventService.cooldown();
        _refresh_event_prompt();
    }

    /// <summary>
    /// 走到光点上 → 触发。效果分两半：能纯逻辑结算的在服务层就结了；要挂在场景上的由
    /// `_apply_world_event_effect()` 动手——**只有它真的没配上时**才补一句「效果还没接上」。
    /// </summary>
    private Dictionary _trigger_world_event()
    {
        if (_event_row == null)
        {
            return new Dictionary { { "ok", false }, { "error", "没有活着的事件" } };
        }

        string event_id = _event_row.Get("event_id").AsString();
        Dictionary result = WorldEventService.trigger(db, current_state(), event_id, null);
        Dictionary effect = result["effect"].AsGodotDictionary();
        string text = result["text"].AsString();
        Dictionary acted = _apply_world_event_effect(event_id, effect);
        if (_b(acted, "ok", true))
        {
            string effect_text = _s(effect, "text", "");
            if (effect_text.Length > 0)
            {
                text += $"　{effect_text}";
            }

            string acted_text = _s(acted, "text", "");
            if (acted_text.Length > 0)
            {
                text += $"（{acted_text}）";
            }
        }
        else if (_s(acted, "error", "").Length > 0)
        {
            text += $"（{_s(acted, "error", "")}）";
        }

        bool wired = _b(effect, "applied", false)
            || _s(effect, "scene_action", "").Length > 0
            || _s(effect, "text", "").Length > 0;
        if (!wired && _s(effect, "note", "").Length > 0)
        {
            // 效果那半没配时**如实说**，但**不把开发用的说明甩给玩家**（CopyGuard 当场会点名）。
            text += "（这条事件的效果还没接上，先记下这段经过）";
        }

        _set_status(text);
        _despawn_world_event();
        result["scene_effect"] = acted;
        return result;
    }

    /// <summary>
    /// 事件效果的**场景那一半**（0.28.0 Q64）。`trade` 就地开行商货架、`spar` 与 Boss 走开战通道。
    /// 返回 {ok, text, error}——`text` 是补在事件文案后面的**玩家可见**短句。
    /// </summary>
    private Dictionary _apply_world_event_effect(string event_id, Dictionary effect)
    {
        string action = _s(effect, "scene_action", "");
        switch (action)
        {
            case "open_shop":
            {
                string building_id = ShopService.building_for_shop_group(db, _s(effect, "id", ""));
                if (building_id.Length == 0)
                {
                    return new Dictionary { { "ok", false }, { "text", "" }, { "error", "这批货暂时支不起摊子" } };
                }

                Dictionary opened = open_shop(building_id);
                if (!_b(opened, "ok", false))
                {
                    return new Dictionary { { "ok", false }, { "text", "" }, { "error", "货担没能摆开" } };
                }

                return new Dictionary { { "ok", true }, { "text", "就地摆开了货担" }, { "error", "" } };
            }
            case "start_battle":
            {
                string source_key = $"world_event_{event_id}";
                string enemy_id = _s(effect, "battle_enemy_id", "");
                if (enemy_id.Length > 0)
                {
                    _start_boss_encounter(enemy_id, source_key);
                    return new Dictionary { { "ok", true }, { "text", "来者不善" }, { "error", "" } };
                }

                Dictionary started = _start_team_battle(_s(effect, "id", ""), source_key);
                if (!_b(started, "ok", false))
                {
                    return new Dictionary { { "ok", false }, { "text", "" }, { "error", "对方没接这场切磋" } };
                }

                return new Dictionary { { "ok", true }, { "text", "两边摆开了架势" }, { "error", "" } };
            }
            default:
                return new Dictionary { { "ok", true }, { "text", "" }, { "error", "" } };
        }
    }

    public string world_event_active_id() =>
        _event_row != null ? _event_row.Get("event_id").AsString() : "";

    /// <summary>探针用：直接放一个事件（用例固定内容，避免依赖随机）。</summary>
    public bool force_world_event(string event_id, Vector2 offset = default)
    {
        if (offset == default)
        {
            offset = new Vector2(64, 0);
        }

        Resource row = db.get_row("world_event", event_id);
        if (row == null || player == null)
        {
            return false;
        }

        _event_row = row;
        _event_timer = WorldEventService.lifetime();
        _event_cooldown = 0.0;
        if (_event_marker != null && GodotObject.IsInstanceValid(_event_marker))
        {
            _event_marker.QueueFree();
        }

        var marker = new Polygon2D
        {
            Name = "WorldEventMark",
            Polygon = new[]
            {
                new Vector2(0, -7), new Vector2(7, 0), new Vector2(0, 7), new Vector2(-7, 0),
            },
            Color = new Color(1.0f, 0.83f, 0.47f, 0.55f),
            Position = player.Position + offset,
        };
        _add_to_world(ysort_node(), marker);
        _event_marker = marker;
        _refresh_event_prompt();
        return true;
    }

    /// <summary>揭雾：proximity_N 走进去自动揭开、discover_X 在 X 揭开后跟着揭开，结果写进存档。</summary>
    public void _check_reveals()
    {
        if (world_map == null)
        {
            return;
        }

        var revealed = new Godot.Collections.Array();
        foreach (string node_id in world_map.apply_proximity_reveals(player.Position, _node_positions()))
        {
            revealed.Add(node_id);
        }

        // 持有类（`item_<id>`）：拿到藏宝图这种就立刻揭开，不必等走到刷新点旁边
        foreach (string node_id in world_map.apply_held_reveals())
        {
            revealed.Add(node_id);
        }

        if (revealed.Count == 0)
        {
            return;
        }

        _refresh_map_label();
        var names = new System.Collections.Generic.List<string>();
        foreach (Variant node_id_variant in revealed)
        {
            string node_id = node_id_variant.AsString();
            names.Add(world_map.node_name(node_id));
            _clear_fog_around(node_id);
        }

        // 揭开后地标图标要跟着出现（不然玩家走了半天地图上还是空的）
        refresh_node_icons();
        _set_status($"揭开新区域：{string.Join("、", names)}");
    }

    /// <summary>
    /// 把已经揭开的地标周围的雾擦掉（地编在 overworld.tscn 里放了 `Fog` 图层）。
    /// 半径取该节点的 proximity 门槛（没有就用默认 6 格），这样「走进去才揭开」在画面上真的看得见。
    /// </summary>
    private void _apply_fog()
    {
        if (world_map == null)
        {
            return;
        }

        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            if (world_map.is_revealed(node_id))
            {
                _clear_fog_around(node_id);
            }
        }
    }

    private void _clear_fog_around(string node_id, int default_radius = 6)
    {
        var fog = world.GetNodeOrNull<TileMapLayer>("Fog");
        if (fog == null)
        {
            return;
        }

        var marker = world.GetNodeOrNull<Node2D>($"Markers/Node_{node_id}");
        if (marker == null)
        {
            return;
        }

        int radius = default_radius;
        Resource row = db.get_row("map_region", node_id);
        if (row != null)
        {
            string condition = row.Get("unlock_condition").AsString();
            if (condition.StartsWith("proximity_"))
            {
                radius = Mathf.Max(default_radius, (int)condition.Substring("proximity_".Length).ToFloat());
            }
        }

        Vector2I center = fog.LocalToMap(fog.ToLocal(marker.GlobalPosition));
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx * dx + dy * dy > radius * radius)
                {
                    continue;
                }

                fog.EraseCell(new Vector2I(center.X + dx, center.Y + dy));
            }
        }
    }

    /// <summary>还剩多少格雾（自检与用例看这个数，能证明雾真的被擦掉了）。</summary>
    public int fog_cells()
    {
        var fog = world.GetNodeOrNull<TileMapLayer>("Fog");
        return fog != null ? fog.GetUsedCells().Count : 0;
    }

    /// <summary>所有 Node_&lt;node_id&gt; 位点的位置（揭雾的距离判定用）。</summary>
    private Dictionary _node_positions()
    {
        var out_dict = new Dictionary();
        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            var marker = world.GetNodeOrNull<Node2D>($"Markers/Node_{node_id}");
            if (marker != null)
            {
                out_dict[node_id] = marker.Position;
            }
        }

        return out_dict;
    }

    /// <summary>潜行时「被发现」判定打折的比例（`combat_const.sneak_detect_reduce`）。</summary>
    public float sneak_detect_reduce()
    {
        Resource row = db.get_row("combat_const", "sneak_detect_reduce");
        return row != null ? (float)row.Get("value").AsDouble() : 0.4f;
    }

    /// <summary>潜行状态变了就给一句提示（玩家得知道按住 Shift 到底有没有用）。</summary>
    public void _track_sneak()
    {
        if (player == null)
        {
            return;
        }

        bool sneaking = player.sneaking;
        if (sneaking == _was_sneaking)
        {
            return;
        }

        _was_sneaking = sneaking;
        if (sneaking)
        {
            _set_status($"潜行中：移速 60%，被发现判定 ×{1.0 - sneak_detect_reduce():F2}");
        }
        else
        {
            _refresh_status();
        }
    }

    /// <summary>收集大地图上的事件判定位点（`Event_&lt;check_id&gt;`，按 region_id 归属）。</summary>
    private void _collect_events()
    {
        events = new Array();
        if (world == null)
        {
            return;
        }

        var seen = new Dictionary();
        foreach (Variant row_variant in db.rows("event_check"))
        {
            Resource row = row_variant.As<Resource>();
            string check_id = row.Get("check_id").AsString();
            if (seen.ContainsKey(check_id))
            {
                continue;
            }

            var marker = world.GetNodeOrNull<Node2D>($"Markers/Event_{check_id}");
            if (marker == null)
            {
                continue;
            }

            seen[check_id] = true;
            events.Add(new Dictionary
            {
                { "check_id", check_id }, { "node", marker }, { "position", marker.GlobalPosition },
            });
        }
    }

    public EventCheckService event_service() =>
        _event_service ??= EventCheckService.create(db, current_state(), null);

    /// <summary>玩家身边的事件位点。</summary>
    public string event_near_player()
    {
        if (player == null)
        {
            return "";
        }

        string best = "";
        float best_distance = EVENT_DISTANCE;
        foreach (Variant entry_variant in events)
        {
            Dictionary entry = entry_variant.AsGodotDictionary();
            float distance = player.Position.DistanceTo(entry["position"].AsVector2());
            if (distance <= best_distance)
            {
                best = entry["check_id"].AsString();
                best_distance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// 收集大地图上的观察点（`Observe_&lt;point_id&gt;`，表里填 `region_id` 的那几条）。
    /// 与判定位点一样挂在 `Markers/` 下；小地图那几条由 `LocalMapController` 收。
    /// </summary>
    private void _collect_flavor_points()
    {
        flavor_points = new Array();
        if (world == null)
        {
            return;
        }

        foreach (Variant row_variant in db.rows("flavor_point"))
        {
            Resource row = row_variant.As<Resource>();
            if (row.Get("region_id").AsString().Length == 0)
            {
                continue;
            }

            string point_id = row.Get("point_id").AsString();
            var marker = world.GetNodeOrNull<Node2D>($"Markers/{FlavorPointRow.marker_name_of(point_id)}");
            if (marker == null)
            {
                continue;
            }

            // **看得见**：观察点本身只是一根 Marker2D，挂一枚小暖色菱形（决策 337）。
            // 幂等——重复 setup() 不会挂第二枚。
            if (marker.GetNodeOrNull<Node>(FlavorMarker.node_name()) == null)
            {
                marker.AddChild(new FlavorMarker());
            }

            flavor_points.Add(new Dictionary
            {
                { "point_id", point_id }, { "node", marker }, { "position", marker.GlobalPosition },
            });
        }
    }

    /// <summary>玩家身边的观察点（最近的那个 point_id；没有就空串）。</summary>
    public string flavor_near_player()
    {
        if (player == null)
        {
            return "";
        }

        string best = "";
        float best_distance = EVENT_DISTANCE;
        foreach (Variant entry_variant in flavor_points)
        {
            Dictionary entry = entry_variant.AsGodotDictionary();
            float distance = player.Position.DistanceTo(entry["position"].AsVector2());
            if (distance <= best_distance)
            {
                best = entry["point_id"].AsString();
                best_distance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// 收大地图上的 NPC 站位（`Characters/npc_slot_0N` 或按 id 绑的 `npc_&lt;npc_id&gt;`，
    /// 与地编摆位点的命名一致；两种都收，见 `框架说明.md` 决策 332）。
    /// </summary>
    private void _collect_npc_slots()
    {
        npcs = new Array();
        Node box = world != null ? world.GetNodeOrNull<Node>("Characters") : null;
        if (box == null)
        {
            return;
        }

        string prefix = _npc_slot_prefix();
        foreach (Node child in box.GetChildren())
        {
            if (child is Node2D && child.Name.ToString().StartsWith(prefix))
            {
                npcs.Add(child);
            }
        }
    }

    /// <summary>玩家身边的 NPC 位点（最近的那个；没有就 null）。</summary>
    public Node2D npc_near_player()
    {
        if (player == null)
        {
            return null;
        }

        Node2D best = null;
        float best_distance = NPC_DISTANCE;
        foreach (Variant slot_variant in npcs)
        {
            var slot = slot_variant.AsGodotObject() as Node2D;
            if (!instance_valid(slot))
            {
                continue;
            }

            float distance = slot.GlobalPosition.DistanceTo(player.Position);
            if (distance <= best_distance)
            {
                best = slot;
                best_distance = distance;
            }
        }

        return best;
    }

    public bool instance_valid(GodotObject node) => node != null && GodotObject.IsInstanceValid(node);

    /// <summary>
    /// 位点 → 人：**两种命名都认**，判定只有一处（`NpcService.npc_for_slot`）。
    /// 区域取「最近的地标」（`current_region_id()`，与随机事件同一份判定）。
    /// </summary>
    private string _npc_for_slot(string slot_id) =>
        NpcService.npc_for_slot(db, slot_id, current_region_id());

    /// <summary>
    /// 按 E 跟人说话：开交往面板（设计 19 §三）。内容还没到的人**不静默**——
    /// `npc_panel` 会如实写「这个人还没有配 npc_def 行」。
    /// </summary>
    public Dictionary _talk_to_npc(Node2D slot)
    {
        string npc_id = _npc_for_slot(slot.Name.ToString());
        if (npc_id.Length == 0)
        {
            // 与 `LocalMapController` 同一句话：**表名不进玩家可见文案**（决策 329）。
            GD.PushError(
                $"[overworld] NPC 站位 {slot.Name} 按编号顺序取不到 npc_def 行"
                + $"（本区域只有 {NpcService.npcs_at(db, current_region_id()).Count} 个人）");
            const string text = "眼下没什么可说的（这个人还没配台词）";
            _set_status($"{NPC_SPEAKER}：{text}");
            return new Dictionary
            {
                { "ok", true }, { "npc", slot.Name.ToString() }, { "speaker", NPC_SPEAKER }, { "text", text },
            };
        }

        return open_npc(npc_id);
    }

    /// <summary>开 NPC 交往面板：`mode` 与设计 0.28.0 的 Q62 一致（E 进交互菜单、Q 只读看信息）。</summary>
    public Dictionary open_npc(string npc_id, string mode = "interact")
    {
        if (_panel_open(npc_panel))
        {
            _push_overlay("npc");
            npc_panel.Set("state_override", current_state());
            npc_panel.Set("npc_id", npc_id);
            npc_panel.Set("mode", mode);
            npc_panel.Call("refresh");
            return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", true } };
        }

        var panel = GD.Load<PackedScene>(NPC_SCENE).Instantiate();
        if (panel == null)
        {
            return new Dictionary { { "ok", false }, { "error", "NPC 面板加载失败" } };
        }

        panel.Name = "NpcPanel";
        panel.Set("state_override", current_state());
        panel.Set("npc_id", npc_id);
        panel.Set("mode", mode);
        panel.Set("return_handler", Callable.From(close_npc));
        // 同伴的交往入口现在在角色面板（Q67 拍板 ④），而角色面板在大地图上也能开——
        // 所以大地图这一侧同样要把切磋接上，否则那一按会落到「这里不能切磋」。
        panel.Set("spar_handler", Callable.From<string, string>((who, team) => _start_spar(who, team)));
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        npc_panel = panel;
        _push_overlay("npc");
        return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", false } };
    }

    public void close_npc() => _close_overlay("npc");

    /// <summary>
    /// 切磋（与 `LocalMapController._start_spar` 同一套口径）：空队伍 id = **打他自己的镜像**
    /// （同伴，Q88 拍板 ①）；有队伍 id 就走表里那支队伍。
    /// </summary>
    private Dictionary _start_spar(string npc_id, string team_id)
    {
        Resource team = team_id.Length > 0 ? db.get_row("enemy_team", team_id) : null;
        if (team_id.Length > 0 && team == null)
        {
            GD.PushError($"[overworld] 切磋队伍不存在：enemy_team 缺少 {team_id}（npc {npc_id}）");
            _set_status("切磋对手的配置对不上（数据错，已记进日志）");
            return new Dictionary { { "ok", false }, { "error", "no_team" } };
        }

        var data = new Dictionary
        {
            { "spawn_id", $"spar_{npc_id}" },
            { "source_scene", current_region_id() },
            { "source_key", $"spar_{npc_id}" },
            { "team_id", team_id },
            { "is_elite", false },
        };
        if (team_id.Length == 0)
        {
            data["mirror_char"] = npc_id;
        }

        Encounter encounter = Encounter.build(
            db, data, team, Encounter.contact_front(), current_state().Get("difficulty_id").AsString());
        if (team_id.Length == 0)
        {
            Resource person = NpcService.person_of(db, current_state(), npc_id);
            string mirror_name = person != null ? person.Get("name_cn").AsString() : npc_id;
            encounter.team_name = $"{mirror_name}的镜像";
        }

        encounter.spar_npc = npc_id;
        close_npc();
        if (_callable_valid(battle_switch_handler))
        {
            battle_switch_handler.Call(encounter);
        }
        else
        {
            _change_scene(BATTLE_SCENE);
        }

        return new Dictionary { { "ok", true }, { "team_id", team_id }, { "npc_id", npc_id } };
    }

    /// <summary>
    /// 看一个观察点：只出一句碎句——不发奖励、不锁任何路（设计 20 §3.2）。
    /// **0.32.0 补**：读过顺手记一枚 `flag_obs_&lt;point_id&gt;`（幕二「免战」选项的前置来源）。
    /// </summary>
    public Dictionary read_flavor_point(string point_id)
    {
        Resource row = db.get_row("flavor_point", point_id);
        if (row == null)
        {
            return new Dictionary { { "ok", false }, { "error", "没有这个观察点" } };
        }

        string text = row.Get("text_cn").AsString();
        _set_status(text);
        GodotObject state = current_state();
        if (state != null)
        {
            state.Call("set_flag", $"flag_obs_{point_id}", true);
            autosave("观察点");
        }

        return new Dictionary { { "ok", true }, { "point_id", point_id }, { "text", text } };
    }

    /// <summary>走一次事件判定：判定 → 奖励／失败说明；「指向某地」的奖励会顺带揭开地标。</summary>
    public Dictionary resolve_event(string check_id)
    {
        Dictionary result = event_service().resolve(check_id);
        _set_status(result["text"].AsString());
        _refresh_map_label();
        Dictionary reward = result["reward"].AsGodotDictionary();
        if (result["success"].AsBool() && _b(reward, "start_battle", false))
        {
            // `reward_type=boss`（08／06）：判定通过 → Boss 现身开战。
            _start_boss_encounter(_s(reward, "id", ""), check_id);
        }

        return result;
    }

    /// <summary>
    /// 事件触发的团队战（Q64 的「切磋」）。与明雷那次同一口径，只是没有刷新点行——
    /// 队伍直接从 `enemy_team` 取，`source_key` 记成事件名。`is_elite=false`：切磋不是精英战。
    /// </summary>
    private Dictionary _start_team_battle(string team_id, string source_key)
    {
        Resource team = db.get_row("enemy_team", team_id);
        if (team == null)
        {
            GD.PushError($"[Overworld] 事件的队伍不在表里：{team_id}");
            return new Dictionary { { "ok", false }, { "error", "no_team" } };
        }

        GodotObject state = current_state();
        string difficulty = state != null ? state.Get("difficulty_id").AsString() : "normal";
        Encounter encounter = Encounter.build(db, new Dictionary
        {
            { "spawn_id", source_key },
            { "source_scene", "overworld" },
            { "source_key", source_key },
            { "team_id", team_id },
            { "is_elite", false },
        }, team, Encounter.contact_front(), difficulty);
        GodotObject session_node = session();
        if (session_node != null)
        {
            session_node.Set("pending_encounter", encounter);
            session_node.Call(
                "set_world_position", player != null ? player.Position : Vector2.Zero, current_state());
        }

        _set_status(encounter.headline());
        if (_callable_valid(battle_switch_handler))
        {
            battle_switch_handler.Call(encounter);
            return new Dictionary { { "ok", true }, { "team_id", team_id }, { "error", "" } };
        }

        // 正式游玩时没有人接管：直接切战斗场景
        _change_scene(BATTLE_SCENE);
        return new Dictionary { { "ok", true }, { "team_id", team_id }, { "error", "" } };
    }

    /// <summary>
    /// 事件判定触发的 Boss 战：单人一支队，构造与「小地图隐藏 Boss」同一口径
    /// （`team_hidden_&lt;enemy&gt;` / contact=front / 难度取当前存档）。
    /// </summary>
    private void _start_boss_encounter(string enemy_id, string source_key)
    {
        Resource enemy = db.get_row("enemy_base", enemy_id);
        if (enemy == null)
        {
            GD.PushError($"[Overworld] Boss 奖励指向的敌人不存在：{enemy_id}");
            return;
        }

        GodotObject state = current_state();
        string difficulty = state != null ? state.Get("difficulty_id").AsString() : "normal";
        Encounter encounter = Encounter.build(db, new Dictionary
        {
            { "spawn_id", source_key },
            { "source_scene", "overworld" },
            { "source_key", source_key },
            { "team_id", $"team_hidden_{enemy_id}" },
            { "is_elite", true },
        }, new Dictionary
        {
            { "name_cn", enemy.Get("name_cn").AsString() },
            { "threat_tag", enemy.Get("threat_tag").AsString() },
            { "members", $"{enemy_id}:1" },
        }, Encounter.contact_front(), difficulty);
        GodotObject session_node = session();
        if (session_node != null)
        {
            session_node.Set("pending_encounter", encounter);
            session_node.Call(
                "set_world_position", player != null ? player.Position : Vector2.Zero, current_state());
        }

        _set_status(encounter.headline());
        if (_callable_valid(battle_switch_handler))
        {
            battle_switch_handler.Call(encounter);
            return;
        }

        // 以前这里**没有兜底**：正式游玩时没人接管 `battle_switch_handler`，于是
        // 「判定过了 → Boss 现身」只会改一行状态文字，战斗永远不开。
        _change_scene(BATTLE_SCENE);
    }

    /// <summary>收集大地图上的入口位点（Portal_&lt;scene_id&gt;），以及进入门槛。</summary>
    private void _collect_portals()
    {
        _portals = new Array();
        foreach (Variant row_variant in db.rows("map_local"))
        {
            Resource row = row_variant.As<Resource>();
            string scene_id = row.Get("scene_id").AsString();
            var marker = world.GetNodeOrNull<Node2D>($"Markers/Portal_{scene_id}");
            if (marker == null)
            {
                continue;
            }

            Resource region = db.get_row("map_region", row.Get("parent_node").AsString());
            _portals.Add(new Dictionary
            {
                { "scene_id", scene_id },
                { "name", row.Get("name_cn").AsString() },
                { "position", marker.Position },
                { "condition", region != null ? region.Get("unlock_condition").AsString() : "default" },
            });
        }
    }

    public void _check_portal()
    {
        // 刚回图（或刚开局）时人可能正站在入口位点上——那时候不进图，先离开圈外再武装。
        if (!_portal_armed)
        {
            float nearest = 1.0e9f;
            foreach (Variant portal_variant in _portals)
            {
                Dictionary portal = portal_variant.AsGodotDictionary();
                nearest = Mathf.Min(nearest, player.Position.DistanceTo(portal["position"].AsVector2()));
            }

            if (nearest > PORTAL_DISTANCE + PORTAL_ARM_MARGIN)
            {
                _portal_armed = true;
            }

            return;
        }

        foreach (Variant portal_variant in _portals)
        {
            Dictionary portal = portal_variant.AsGodotDictionary();
            if (player.Position.DistanceTo(portal["position"].AsVector2()) > PORTAL_DISTANCE)
            {
                continue;
            }

            if (_is_locked_scene(portal["scene_id"].AsString()))
            {
                _set_status($"{portal["name"].AsString()}：本章不可前往");
                return;
            }

            if (!_portal_unlocked(portal))
            {
                _set_status($"{portal["name"].AsString()}：还没发现这里（先在落雁坡转转）");
                return;
            }

            enter_local_map(portal["scene_id"].AsString());
            return;
        }
    }

    private bool _portal_unlocked(Dictionary portal)
    {
        string condition = portal["condition"].AsString();
        if (condition.Length == 0 || condition == "default")
        {
            return true;
        }

        // discover_luoyanpo → 先在落雁坡露过面；proximity_* 只影响地图揭开
        if (condition.StartsWith("discover_"))
        {
            return world_map != null
                && world_map.discovery_target_revealed(condition.Substring("discover_".Length));
        }

        return true;
    }

    /// <summary>
    /// from_teleport=true 表示这次是**驿站传送**来的，不是走进去的。
    /// 区别在「返回大地图原位置」（设计 02）怎么理解：走进去 → 原位置 = 你当时站的地方；
    /// 传送过去 → 原位置应当是**目的地的地标**，否则从荒村出来会被一路拽回落雁坡的驿站。
    /// </summary>
    /// <summary>
    /// 进小地图。**故意重载两个**：C# 的默认参数不会进 Godot 的方法签名，
    /// GDScript 那边 `call("enter_local_map", "scene_x")` 只给一个实参时会被判成
    /// 「Invalid call. Nonexistent function」——`tools/check_loop.gd` 那条真闭环当场红
    /// （2026-10-11 搬 C# 时踩到，GDScript 的 `= false` 没有这条限制）。
    /// </summary>
    public void enter_local_map(string scene_id) => enter_local_map(scene_id, false);

    public void enter_local_map(string scene_id, bool from_teleport)
    {
        GodotObject session_node = session();
        if (session_node != null)
        {
            session_node.Set("pending_local_scene", scene_id);
            // 从大地图进图一律走入口：清掉上一轮的落点与战斗前位置
            session_node.Set("pending_local_room", "");
            session_node.Set("local_position", Vector2.Zero);
            session_node.Set("local_position_scene", "");
            if (player != null)
            {
                session_node.Call(
                    "set_world_position",
                    from_teleport ? _landmark_position(scene_id) : player.Position,
                    current_state());
            }
        }

        // 设计 02：大地图在「切换小地图时」自动存档
        autosave("切换小地图");
        _change_scene(LOCAL_RUN);
    }

    /// <summary>某张小地图在大地图上的地标坐标（`map_local.parent_node` → `map_region.pos_*`）。</summary>
    private Vector2 _landmark_position(string scene_id)
    {
        Resource local_row = db.get_row("map_local", scene_id);
        if (local_row == null)
        {
            return Vector2.Zero;
        }

        Resource region = db.get_row("map_region", local_row.Get("parent_node").AsString());
        if (region == null)
        {
            return Vector2.Zero;
        }

        return new Vector2(
            (float)region.Get("pos_x").AsDouble(), (float)region.Get("pos_y").AsDouble());
    }

    /// <summary>存档设施：注入优先，否则用默认目录。</summary>
    public SaveService save_service()
    {
        if (_save_service == null)
        {
            GodotObject store = save_store_override ?? SaveService.make_default();
            _save_service = SaveService.create(store, current_state());
        }

        return _save_service;
    }

    /// <summary>自动存档（大地图切小地图时、战斗结算后由战斗界面调用）。</summary>
    public Dictionary autosave(string reason)
    {
        Dictionary result = save_service().save(reason, true);
        // 失败要出声：不打断游玩，但别让玩家以为存上了。
        if (!_b(result, "ok", false))
        {
            _set_status($"自动存档失败：{_s(result, "error", "未知原因")}（这一段的进度只在本局里）");
        }

        return result;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 浮层栈（设计 18.1，与 `LocalMapController` 同一套口径）：
        // **全局快捷键在任何浮层里都可用**，Esc 一次只弹一层，栈空才回枢纽页。
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (close_top_overlay())
            {
                return;
            }

            _change_scene(PLACEHOLDER_SCENE);
            return;
        }

        if (@event.IsActionPressed("open_character"))
        {
            open_character_overlay(0);
            return;
        }

        if (@event.IsActionPressed("open_bag"))
        {
            // 下标走 `UiNav.char_tab_of`，别写字面量（行囊 2026-10-11 从第 3 页挪到第 4 页）
            open_character_overlay(UiNav.char_tab_of(UiNav.BAG));
            return;
        }

        if (@event.IsActionPressed("show_clues"))
        {
            open_clues();
            return;
        }

        // 看 NPC 信息（设计 0.28.0 的 Q62，与 `LocalMapController` 同一套）：Q 只读、E 才交互
        if (@event.IsActionPressed("npc_info"))
        {
            Node2D who = npc_near_player();
            // 与 `LocalMapController` 同一套：**失败也要出声**
            if (who == null)
            {
                _set_status("这附近没有可以看的人（Q 是看人，E 才是搭话）");
                return;
            }

            string info_id = _npc_for_slot(who.Name.ToString());
            if (info_id.Length == 0)
            {
                _set_status($"这个人还没配信息（位点 {who.Name} 没绑到表里）");
                return;
            }

            open_npc(info_id, "info");
            return;
        }

        if (overlays().Call("depth").AsInt32() > 0)
        {
            return;
        }

        if (@event.IsActionPressed("interact"))
        {
            string check_id = event_near_player();
            if (check_id.Length > 0)
            {
                resolve_event(check_id);
            }
            else if (post_station_near())
            {
                open_waypoint();
            }
            else if (npc_near_player() != null)
            {
                _talk_to_npc(npc_near_player());
            }
            else if (flavor_near_player().Length > 0)
            {
                read_flavor_point(flavor_near_player());
            }
            else
            {
                _set_status("这里暂时没什么可交互的（走进地标可以进小地图）");
            }
        }
    }

    // ------------------------------------------------------------------ 浮层栈

    public GodotObject overlays() => _overlays ??= _overlay_stack_script().Call("new").AsGodotObject();

    private Array _push_overlay(string id)
    {
        Array closed = overlays().Call("push", id).AsGodotArray();
        foreach (Variant closed_id in closed)
        {
            _free_overlay(closed_id.AsString());
        }

        return closed;
    }

    public bool close_top_overlay()
    {
        string top_id = overlays().Call("pop").AsString();
        if (top_id.Length == 0)
        {
            return false;
        }

        _free_overlay(top_id);
        return true;
    }

    /// <summary>
    /// 底栏七入口的跳转（`docs/dev/UI示意落地清单.md` §一）：面板内部能消化的（行囊／武学／角色／装备）
    /// 在面板那一侧就切了页签，落到这儿的是**跨浮层**的三个——地图（回场景）、纪事、系统。
    /// 「系统」那一屏（存读档＋开关）还没做，入口在底栏上是置灰的（`UiNav.disabled_reason`）；
    /// 真点到也不做事——**不摆一个点不动的空壳界面**。
    /// </summary>
    public void nav_to(string id)
    {
        if (UiNav.is_back(id))
        {
            // 「地图」＝离开这一摞浮层，回到场景本身。压了几层就弹几层。
            while (overlays().Call("depth").AsInt32() > 0)
            {
                if (!close_top_overlay())
                {
                    break;
                }
            }

            return;
        }

        if (id == UiNav.CLUE)
        {
            open_clues();
        }
    }

    private void _close_overlay(string id)
    {
        foreach (Variant extra in overlays().Call("remove", id).AsGodotArray())
        {
            _free_overlay(extra.AsString());
        }

        _free_overlay(id);
        // 与 `LocalMapController._close_overlay` 同一条口径（决策 347）：浮层里的动作可能刚刚满足
        // 某条引导口径，关浮层时重算一次，别等下一次换图。
        GuideService.refresh_derived_flags(db, current_state());
        _refresh_guide();
    }

    private void _free_overlay(string id)
    {
        switch (id)
        {
            case "clue":
                _free_panel(ref clue_panel);
                break;
            case "waypoint":
                _free_panel(ref waypoint_panel);
                break;
            case "shop":
                _free_panel(ref shop_panel);
                break;
            case "npc":
                _free_panel(ref npc_panel);
                break;
            case "character":
                Node panel = FindChild("CharacterPanel", true, false);
                panel?.QueueFree();
                break;
        }
    }

    private static void _free_panel(ref Node panel)
    {
        if (panel != null && GodotObject.IsInstanceValid(panel))
        {
            panel.QueueFree();
        }

        panel = null;
    }

    private static bool _panel_open(Node panel) => panel != null && GodotObject.IsInstanceValid(panel);

    /// <summary>角色与行囊（Tab／I）：**浮层**，与大地图同屏——切场景会把驿站那一层丢掉。</summary>
    public Dictionary open_character_overlay(int tab_index = 0)
    {
        if (overlays().Call("has", "character").AsBool())
        {
            _push_overlay("character");
            Node existing = FindChild("CharacterPanel", true, false);
            if (existing != null)
            {
                existing.Set("state_override", current_state());
                existing.Call("refresh");
                existing.Call("select_tab", tab_index);
            }

            return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", true } };
        }

        var panel = GD.Load<PackedScene>(CHARACTER_SCENE).Instantiate();
        if (panel == null)
        {
            return new Dictionary { { "ok", false }, { "error", "角色界面场景加载失败" } };
        }

        panel.Name = "CharacterPanel";
        panel.Set("state_override", current_state());
        panel.Set("back_handler", Callable.From(() => _close_overlay("character")));
        // Q67 拍板 ④：同伴的交往入口在角色面板——这里把「谁」接到既有那条 `open_npc()`
        panel.Set("npc_open_handler", Callable.From<string>(npc_id => open_npc(npc_id)));
        // 底栏七入口（UI 清单 §一）：跨浮层的那三个回到这里（地图／纪事／系统），
        // 四个页签入口由面板自己切。地名借 HUD 那一份（同一处口径，别在面板里再算一次）。
        panel.Set("nav_handler", Callable.From<string>(nav_id => nav_to(nav_id)));
        panel.Set("place_label", _region_name());
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        panel.Call("select_tab", tab_index);
        _push_overlay("character");
        return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", false } };
    }

    /// <summary>开线索本：野外事件按地标分组（在大地图上按 K）。</summary>
    public Dictionary open_clues()
    {
        if (_panel_open(clue_panel))
        {
            _push_overlay("clue");
            clue_panel.Set("state_override", current_state());
            clue_panel.Call("refresh");
            return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", true } };
        }

        var panel = GD.Load<PackedScene>(CLUE_SCENE).Instantiate();
        if (panel == null)
        {
            return new Dictionary { { "ok", false }, { "error", "线索本场景加载失败" } };
        }

        panel.Name = "CluePanel";
        panel.Set("state_override", current_state());
        panel.Set("scope", "region");
        panel.Set("return_handler", Callable.From(close_clues));
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        clue_panel = panel;
        _push_overlay("clue");
        _set_status("线索本：五类都在里面；野外的可交互点按地标列在「隐藏」那一页（Esc 或点「离开」出来）");
        return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", false } };
    }

    public void close_clues() => _close_overlay("clue");

    /// <summary>
    /// 开货架：设计 19 §四的「货商车队」在野外就地开张（`bld_caravan` 没有地图位点）。
    /// 节点名与「谁卖什么」全走 `building_def`／`shop_stock`，这里只负责把界面盖上来。
    /// </summary>
    public Dictionary open_shop(string building_id)
    {
        ShopService service = ShopService.create(db, current_state());
        if (service.building(building_id) == null)
        {
            return new Dictionary
            {
                { "ok", false }, { "error", "这个货架不在表里" }, { "building_id", building_id },
            };
        }

        if (_panel_open(shop_panel))
        {
            _push_overlay("shop");
            shop_panel.Set("state_override", current_state());
            shop_panel.Call("open_building", building_id);
            return new Dictionary
            {
                { "ok", true }, { "error", "" }, { "building_id", building_id }, { "reopened", true },
            };
        }

        var panel = GD.Load<PackedScene>(SHOP_SCENE).Instantiate();
        if (panel == null)
        {
            return new Dictionary
            {
                { "ok", false }, { "error", "商店场景加载失败" }, { "building_id", building_id },
            };
        }

        panel.Name = "ShopPanel";
        panel.Set("state_override", current_state());
        panel.Set("building_id", building_id);
        panel.Set("return_handler", Callable.From(close_shop));
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        shop_panel = panel;
        _push_overlay("shop");
        const string welcome = "行商货担：货比城镇贵、还限量（Esc 或点「离开」出来）";
        panel.Call("show_message", welcome);
        _set_status(welcome);
        return new Dictionary
        {
            { "ok", true }, { "error", "" }, { "building_id", building_id }, { "reopened", false },
        };
    }

    public void close_shop()
    {
        _close_overlay("shop");
        _set_status("行商收起货担，继续赶路");
    }

    /// <summary>身边是不是驿站（`Markers/Node_n_post_station`）。</summary>
    public bool post_station_near()
    {
        if (world == null || player == null)
        {
            return false;
        }

        var marker = world.GetNodeOrNull<Node2D>("Markers/Node_n_post_station");
        return marker != null && player.Position.DistanceTo(marker.Position) <= POST_DISTANCE;
    }

    /// <summary>开驿站界面：传送目标与难度切换都在这一个面板里。</summary>
    public Dictionary open_waypoint()
    {
        if (_panel_open(waypoint_panel))
        {
            _push_overlay("waypoint");
            waypoint_panel.Set("state_override", current_state());
            waypoint_panel.Call("refresh");
            return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", true } };
        }

        var panel = GD.Load<PackedScene>(WAYPOINT_SCENE).Instantiate();
        if (panel == null)
        {
            return new Dictionary { { "ok", false }, { "error", "驿站场景加载失败" } };
        }

        panel.Name = "WaypointPanel";
        panel.Set("state_override", current_state());
        panel.Set("current_scene_id", "");
        panel.Set("return_handler", Callable.From(close_waypoint));
        panel.Set("travel_handler", Callable.From<string, string>((scene_id, _name) =>
        {
            close_waypoint();
            // 传送：回程落点记目的地地标，别把人拽回驿站
            enter_local_map(scene_id, true);
        }));
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        waypoint_panel = panel;
        _push_overlay("waypoint");
        const string welcome = "驿站：可以传送到已探索的地标，也能在这里改难度（Esc 或点「离开」出来）";
        panel.Call("show_message", welcome);
        _set_status(welcome);
        return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", false } };
    }

    public void close_waypoint()
    {
        _close_overlay("waypoint");
        _set_status("离开驿站");
    }

    // ------------------------------------------------------------------ 遭遇

    private void _on_encountered(string spawn_id, string contact)
    {
        Resource row = db.get_row("roaming_spawn", spawn_id);
        if (row == null)
        {
            return;
        }

        Resource team = db.get_row("enemy_team", row.Get("team_id").AsString());
        GodotObject state = current_state();
        string difficulty = state != null ? state.Get("difficulty_id").AsString() : "normal";
        Encounter encounter = Encounter.build(db, row, team, contact, difficulty);
        GodotObject session_node = session();
        if (session_node != null)
        {
            session_node.Set("pending_encounter", encounter);
            session_node.Call(
                "set_world_position", player != null ? player.Position : Vector2.Zero, current_state());
        }

        _set_status(encounter.headline());
        if (_callable_valid(battle_switch_handler))
        {
            battle_switch_handler.Call(encounter);
            return;
        }

        _change_scene(BATTLE_SCENE);
    }

    private void _set_status(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
        }
    }

    // ------------------------------------------------------------------ 环境

    private TableDb _resolve_db()
    {
        GodotObject game_data = _session_node("GameData");
        if (game_data != null)
        {
            var db_variant = game_data.Get("db");
            if (db_variant.VariantType != Variant.Type.Nil)
            {
                var existing = db_variant.AsGodotObject() as TableDb;
                if (existing != null && existing.tables.Count > 0)
                {
                    return existing;
                }
            }
        }

        var table_db = new TableDb();
        table_db.load_all();
        return table_db;
    }

    private GodotObject _session_node(string node_name)
    {
        SceneTree tree = _tree();
        return tree?.Root.GetNodeOrNull(node_name);
    }

    private SceneTree _tree()
    {
        // 不在场景树里时 get_tree() 会打一条 ERROR
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

    private void _change_scene(string path)
    {
        // 用例可以接管切场景（否则会把测试的场景树真的换掉）
        if (_callable_valid(scene_change_handler))
        {
            scene_change_handler.Call(path);
            return;
        }

        _tree()?.ChangeSceneToFile(path);
    }

    private static bool _has_user_arg(string flag) => System.Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;

    // ------------------------------------------------------------------ 跨语言取值口

    private static Script _copy_guard_script_cache = null;
    private static Script _overlay_stack_script_cache = null;

    private static Script _copy_guard_script() =>
        _copy_guard_script_cache ??= GD.Load<Script>(COPY_GUARD_SCRIPT);

    private static Script _overlay_stack_script() =>
        _overlay_stack_script_cache ??= GD.Load<Script>(OVERLAY_STACK_SCRIPT);

    private static string _npc_slot_prefix() => NpcService.SLOT_PREFIX;

    /// <summary>
    /// `WorldMapService.LOCKED_SCENES` 的唯一出处（决策 88：同一事实只许一处定义）——
    /// 这份 `.gd` 已搬成 `src/core/WorldMapService.cs`，取值口也跟着从「读常量表」换成静态方法。
    /// </summary>
    private static string[] _locked_scenes()
    {
        return WorldMapService.locked_scenes();
    }

    private static bool _is_locked_scene(string scene_id) =>
        System.Array.IndexOf(_locked_scenes(), scene_id) >= 0;

    /// <summary>
    /// 这个 Callable 有没有真的接上（空的 ＝ 没人接，走兜底路径）。
    /// **不能照抄 GDScript 那套 `Target != null &amp;&amp; !Method.IsEmpty`**：`Callable.From(…)` 造出来的
    /// 委托型 Callable，`Target` 是 `null`、`Method` 取值还会抛 `NullReferenceException`。
    /// </summary>
    private static bool _callable_valid(Callable callable) =>
        callable.Delegate != null || (callable.Target != null && !callable.Method.IsEmpty);

    private Sprite2D _icon_of(string node_id) =>
        _node_icons.ContainsKey(node_id)
            ? _node_icons[node_id].AsGodotObject() as Sprite2D
            : null;

    private Label _label_of(string node_id) =>
        _node_labels.ContainsKey(node_id)
            ? _node_labels[node_id].AsGodotObject() as Label
            : null;

    private static string _s(Dictionary dict, string key, string fallback) =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static bool _b(Dictionary dict, string key, bool fallback) =>
        dict.ContainsKey(key) ? dict[key].AsBool() : fallback;

    // ------------------------------------------------------------------ 自检

    /// <summary>真实场景自检：生成 → 撞明雷 → 触发遭遇（不切场景）。</summary>
    private void _run_world_selftest()
    {
        var ok = true;
        var lines = new System.Collections.Generic.List<string>();
        ok = ok && enemies.Count == 17 && player != null && camera != null;
        lines.Add($"生成：明雷 {enemies.Count} 个，玩家与相机就位={player != null && camera != null}");
        // 0.8.1 的明雷开关：两种状态都验一遍（默认关、置 1 照旧），
        // 免得"关了半年再打开发现已经烂了"。
        GodotObject session_for_toggle = session();
        if (session_for_toggle != null)
        {
            session_for_toggle.Set("roaming_enabled_override", 0);
            rebuild_roaming_enemies();
            ok = ok && enemies.Count == 0;
            lines.Add($"开关 0：明雷 {enemies.Count} 个（应为 0）");
            session_for_toggle.Set("roaming_enabled_override", 1);
            rebuild_roaming_enemies();
            ok = ok && enemies.Count == 17;
            lines.Add($"开关 1：明雷 {enemies.Count} 个（应为 17）");
        }

        var captured = new Array();
        battle_switch_handler = Callable.From<Variant>(encounter => captured.Add(encounter));
        RoamingEnemy wolf = null;
        foreach (Variant enemy_variant in enemies)
        {
            var enemy = enemy_variant.AsGodotObject() as RoamingEnemy;
            if (enemy != null && enemy.spawn_id == "sp_lp_wolf_01")
            {
                wolf = enemy;
            }
        }

        if (wolf == null)
        {
            ok = false;
            lines.Add("找不到 sp_lp_wolf_01");
        }
        else
        {
            // 朝向决定「正面／背后」，而游荡型的朝向每帧都在变 →
            // 必须按它当前的朝向摆位，否则自检会随机变成背袭（这里曾经 50% 概率红）。
            Vector2 facing = wolf.facing.Length() > 0.001f ? wolf.facing.Normalized() : Vector2.Down;
            player.GlobalPosition = wolf.GlobalPosition + facing * 8.0f;
            wolf.reset_latch();
            wolf._check_contact();
            var hit = captured.Count == 1
                && captured[0].AsGodotObject() is Encounter first
                && first.contact == "front";
            ok = ok && hit;
            lines.Add($"撞明雷 ok={hit}（{(captured.Count > 0 ? ((Encounter)captured[0].AsGodotObject()).headline() : "无")}）");

            // 再绕到它背后 → 背袭（同一支明雷重置后重触发）
            player.GlobalPosition = wolf.GlobalPosition - facing * 8.0f;
            wolf.reset_latch();
            wolf._check_contact();
            var back = captured.Count == 2 && ((Encounter)captured[1].AsGodotObject()).contact == "back";
            ok = ok && back;
            lines.Add($"绕背偷袭 ok={back}（{(captured.Count > 1 ? ((Encounter)captured[1].AsGodotObject()).headline() : "无")}）");
        }

        // 背后贴沉睡的醉汉 → 奇袭
        RoamingEnemy sleeper = null;
        foreach (Variant enemy_variant in enemies)
        {
            var enemy = enemy_variant.AsGodotObject() as RoamingEnemy;
            if (enemy != null && enemy.spawn_id == "sp_hc_sleeper")
            {
                sleeper = enemy;
            }
        }

        if (sleeper != null)
        {
            player.GlobalPosition = sleeper.GlobalPosition + new Vector2(0, -8);
            sleeper.reset_latch();
            sleeper._check_contact();
            var ambush = captured.Count == 3 && ((Encounter)captured[2].AsGodotObject()).contact == "ambush_sleep";
            ok = ok && ambush;
            lines.Add($"背袭沉睡 ok={ambush}");
        }

        GodotObject session_node = session();
        if (session_node != null && captured.Count > 0)
        {
            session_node.Set("pending_encounter", captured[0]);
            ok = ok && session_node.Get("pending_encounter").AsGodotObject() != null;
            lines.Add("遭遇已交给 GameSession 待处理");
        }

        // 战斗外增益 HUD（08）：大地图上看得见打坐余韵还剩几分钟；清掉后这一行为空
        if (session_node != null)
        {
            session_node.Call("clear_field_buffs");
            session_node.Call("add_field_buff", "buff_meditated", 10, "", 1);
            _refresh_field_buffs();
            var hud_ok = _field_label != null
                && _field_label.Text.Contains("打坐余韵")
                && _field_label.Text.Contains("剩 10 分钟");
            ok = ok && hud_ok;
            lines.Add($"战斗外增益 HUD ok={hud_ok}（{(_field_label != null ? _field_label.Text : "-")}）");
            // 别和上面两行叠字：状态栏 y=8、揭雾进度 y=34，这一行必须在它们下面且不同 y
            var no_overlap = _field_label != null && _field_label.Position.Y > _status.Position.Y
                && (_map_label == null || _field_label.Position.Y != _map_label.Position.Y);
            ok = ok && no_overlap;
            lines.Add($"增益行不与状态栏／揭雾进度叠字={no_overlap}（y={(_field_label != null ? _field_label.Position.Y : -1.0f):F0}）");
            session_node.Call("clear_field_buffs");
            _refresh_field_buffs();
            var cleared_ok = _field_label != null && _field_label.Text.Length == 0;
            ok = ok && cleared_ok;
            lines.Add($"清掉后 HUD 为空={cleared_ok}");
        }

        // 开局引导 HUD（09 §3.1）：大地图上也要常驻「当前目标」，并且与上面三行错开
        _refresh_guide();
        string guide_line = guide_text();
        var guide_ok = guide_line.Contains("当前目标");
        ok = ok && guide_ok;
        lines.Add($"引导 HUD ok={guide_ok}（{guide_line}）");
        var guide_no_overlap = _guide_label != null && _guide_label.Position.Y > _status.Position.Y
            && (_map_label == null || _guide_label.Position.Y > _map_label.Position.Y)
            && (_field_label == null || _guide_label.Position.Y > _field_label.Position.Y);
        ok = ok && guide_no_overlap;
        lines.Add($"引导行不与上面三行叠字={guide_no_overlap}（y={(_guide_label != null ? _guide_label.Position.Y : -1.0f):F0}）");

        // 大地图上的 NPC（设计 19：`npc_def.place_id` 允许写区域节点）——老周就在落雁坡
        var npc_ok = true;
        npc_ok = npc_ok && npcs.Count >= 1;
        if (npcs.Count > 0)
        {
            var slot = npcs[0].AsGodotObject() as Node2D;
            player.Position = slot.GlobalPosition;
            npc_ok = npc_ok && npc_near_player() == slot;
            npc_ok = npc_ok && current_region_id() == "n_luoyanpo";
            string npc_id = _npc_for_slot(slot.Name.ToString());
            npc_ok = npc_ok && npc_id == "npc_caiyao";
            Dictionary opened = _talk_to_npc(slot);
            npc_ok = npc_ok && _b(opened, "ok", false) && npc_panel != null;
            close_npc();
            npc_ok = npc_ok && npc_panel == null;
            lines.Add($"大地图 NPC（落雁坡 {slot.Name} 可交互）={npc_ok}");
        }
        else
        {
            lines.Add("大地图 NPC：一个位点都没有（应为 1 个）");
        }

        ok = ok && npc_ok;

        var stack_ok = true;
        open_waypoint();
        stack_ok = stack_ok && overlays().Call("depth").AsInt32() == 1 && waypoint_panel != null;
        open_character_overlay(0);
        stack_ok = stack_ok && overlays().Call("depth").AsInt32() == 2 && waypoint_panel != null;
        open_character_overlay(UiNav.char_tab_of(UiNav.BAG));
        Node char_panel = FindChild("CharacterPanel", true, false);
        stack_ok = stack_ok && overlays().Call("depth").AsInt32() == 2 && char_panel != null
            && char_panel.Call("current_tab").AsInt32() == UiNav.char_tab_of(UiNav.BAG);
        stack_ok = stack_ok && close_top_overlay() && overlays().Call("depth").AsInt32() == 1 && waypoint_panel != null;
        stack_ok = stack_ok && close_top_overlay() && overlays().Call("is_empty").AsBool() && waypoint_panel == null;
        ok = ok && stack_ok;
        lines.Add($"浮层栈（驿站→角色→Esc 回驿站→Esc 关驿站）={stack_ok}");

        // 随机事件（设计 19 §四／0.25.1）：强制放一个 → HUD 出模糊提示 → 超时/走远消失
        // → 再放一个走上去触发 → 一次性事件记账。
        var event_ok = true;
        event_ok = event_ok && force_world_event("we_lost_item", new Vector2(64, 0));
        event_ok = event_ok && world_event_active_id() == "we_lost_item";
        event_ok = event_ok && _event_label != null && _event_label.Text.Contains("路边");
        lines.Add($"随机事件提示（HUD 模糊提示、不说事件名）={event_ok}（{(_event_label != null ? _event_label.Text : "-")}）");
        _event_timer = 0.01;
        _update_world_event(0.1);
        event_ok = event_ok && world_event_active_id().Length == 0;
        event_ok = event_ok && _event_label != null && _event_label.Text.Length == 0;
        lines.Add($"超时后光点与提示一起消失={event_ok}");
        // 触发用一条**一次性**事件（`repeatable=0`）：这样能顺带验「发过就记账」。
        force_world_event("we_hermit", new Vector2(8, 0));
        _update_world_event(0.1);
        event_ok = event_ok && world_event_active_id().Length == 0;
        event_ok = event_ok && _status.Text.Contains("老人");
        event_ok = event_ok && current_state().Call("has_flag", WorldEventService.done_flag("we_hermit")).AsBool();
        lines.Add($"走上去触发（一次性记账）={event_ok}（{_status.Text}）");

        // 事件效果的**场景那一半**（0.28.0 Q64）：开货架要真盖上来、切磋要真把遭遇交给场景层。
        force_world_event("we_caravan", new Vector2(8, 0));
        _update_world_event(0.1);
        event_ok = event_ok && overlays().Call("has", "shop").AsBool() && shop_panel != null;
        event_ok = event_ok && _status.Text.Contains("货担");
        lines.Add($"货商车队就地开张（{_status.Text}）={overlays().Call("has", "shop").AsBool() && shop_panel != null}");
        close_shop();
        event_ok = event_ok && !overlays().Call("has", "shop").AsBool() && shop_panel == null;
        int before_spar = captured.Count;
        force_world_event("we_disciple", new Vector2(8, 0));
        _update_world_event(0.1);
        var spar_ok = captured.Count == before_spar + 1;
        if (spar_ok)
        {
            var spar_encounter = (Encounter)captured[before_spar].AsGodotObject();
            spar_ok = spar_encounter.team_id == "team_wanderer_disciple";
            spar_ok = spar_ok && !spar_encounter.is_elite;
        }

        event_ok = event_ok && spar_ok;
        lines.Add($"门派弟子历练把遭遇交给场景层={spar_ok}");
        ok = ok && event_ok;

        // 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
        string[] copy_hits = _copy_guard_script().Call("id_tokens", this).AsStringArray();
        ok = ok && copy_hits.Length == 0;
        lines.Add(_copy_guard_script().Call("ascii_line", this).AsString());
        if (copy_hits.Length > 0)
        {
            lines.Add($"COPY 命中：{string.Join("；", copy_hits)}");
        }

        // 观察点**看得见**（设计 15 §一，决策 337）：大地图那几条也要有那枚标记。
        var highlight_ok = flavor_points.Count > 0;
        foreach (Variant point_variant in flavor_points)
        {
            Dictionary point = point_variant.AsGodotDictionary();
            var node = point["node"].AsGodotObject() as Node2D;
            GodotObject mark = node.GetNodeOrNull(FlavorMarker.node_name());
            highlight_ok = highlight_ok && FlavorMarker.is_visible_highlight(mark);
        }

        ok = ok && highlight_ok;
        lines.Add($"观察点可见标记 {flavor_points.Count} 个 ok={highlight_ok}");

        // 地名常驻（设计 11 §三「地标一旦揭开，地名常驻」＋14 §六「大区名常驻 HUD 左上角」）。
        if (world_map != null)
        {
            world_map.apply_initial_reveals();
            // 自检夹具是「先建控制器、后塞 `state_override`」，于是 `world_map` 还绑在**旧状态**上——
            // 不重建的话它永远是「0 个揭开」，这条断言也就永远量不到东西。
            if (current_state() != null)
            {
                world_map = WorldMapService.create(db, current_state());
                world_map.apply_initial_reveals();
                world_map.state.Call("reveal_node", START_NODE);
            }

            refresh_node_labels();
        }

        var revealed_with_name = false;
        foreach (Variant key in _node_labels.Keys)
        {
            string node_id = key.AsString();
            if (world_map != null && world_map.is_revealed(node_id)
                && _label_of(node_id) is { Visible: true })
            {
                revealed_with_name = true;
                break;
            }
        }

        // **名字与 `icon` 解耦**（0.31.2）：只要那张图上有 `Node_<id>` 位点，就该有它的名字 Label。
        var labelable = 0;
        foreach (Variant row_variant in db.rows("map_region"))
        {
            Resource row = row_variant.As<Resource>();
            if (world.GetNodeOrNull<Node2D>($"Markers/Node_{row.Get("node_id").AsString()}") != null)
            {
                labelable += 1;
            }
        }

        var name_ok = revealed_with_name && _node_labels.Count == labelable;
        Resource region_row = db.get_row("map_region", START_NODE);
        string want_region = region_row != null ? region_row.Get("region_name_cn").AsString().StripEdges() : "";
        name_ok = name_ok && want_region.Length > 0 && _region_name() == want_region;
        ok = ok && name_ok;
        lines.Add(
            $"地名常驻（labels={_node_labels.Count}／该有名字的位点 {labelable} 个／已揭开 "
            + $"{(world_map != null ? world_map.revealed_count() : -1)} 个／可见={revealed_with_name}／"
            + $"HUD 大区名「{want_region}」来自表={want_region.Length > 0 && _region_name() == want_region}）={name_ok}");

        foreach (string line in lines)
        {
            GD.Print("  " + line);
        }

        GD.Print($"WORLD SELF-TEST: {(ok ? "OK" : "FAILED")}");
        _tree()?.Quit(ok ? 0 : 1);
    }
}
