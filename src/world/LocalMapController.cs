using Godot;
using Godot.Collections;
using Lunhui.Audio;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.World;

/// <summary>
/// 小地图控制器（C# 版，对位原 `src/world/local_map_controller.gd`）。城镇／副本通用。
///
/// 职责：
///   1. 实例化 `scenes/maps/&lt;scene_id&gt;.tscn`（地编资产，只读不动）
///   2. 生成玩家（入口房间）与房间敌人（`Enemies/&lt;room_id&gt;/Team_&lt;team_id&gt;`）
///   3. 生成宝箱（`Chest_&lt;drop_group&gt;`）与隐藏触发点（`Trigger_&lt;id&gt;`）
///   4. 交互（E）：开宝箱、触发隐藏内容、走 `Exit_&lt;scene_id&gt;` 回大地图
///   5. 房间敌人接触 → Encounter → 战斗；打回来的清怪状态存在会话里
///
/// 会话状态（`GameSession.local_maps[scene_id]`）在小地图内持续，**回大地图就整片刷新**
/// ——与 02_地图与明雷.md 的刷新规则一致。
/// </summary>
public partial class LocalMapController : Node2D
{
    private const string CHARACTER_SCENE = "res://scenes/character_screen.tscn";
    private const string NPC_SCENE = "res://scenes/npc_panel.tscn";
    private const string BATTLE_SCENE = "res://scenes/battle_screen.tscn";
    private const string SHOP_SCENE = "res://scenes/shop_screen.tscn";
    private const string CULTIVATE_SCENE = "res://scenes/cultivate_screen.tscn";
    private const string CLUE_SCENE = "res://scenes/clue_screen.tscn";
    private const string DUNGEON_SCENE = "res://scenes/dungeon_screen.tscn";
    private const string OVERWORLD_RUN = "res://scenes/world_run.tscn";
    private const string LOCAL_RUN = "res://scenes/local_run.tscn";

    private const string COPY_GUARD_SCRIPT = "res://src/ui/CopyGuard.cs";
    private const string OVERLAY_STACK_SCRIPT = "res://src/ui/OverlayStack.cs";

    public const float EXIT_DISTANCE = 28.0f;
    /// <summary>条件地表层的名字（设计 16 §3.2 的第 5 层，0.32.0 新增）。地编摆这一层，代码按条件整层显隐。</summary>
    public const string CONDITIONAL_LAYER = "Conditional";
    /// <summary>玩家位于遮挡瓦片（TileSet 的 `covering`）下方时，`Overlay` 层压到这个透明度。</summary>
    public const float OVERLAY_COVER_ALPHA = 0.35f;
    /// <summary>站多近才能跟 NPC 说话（格 = 32px，这里约 1 格）。</summary>
    public const float NPC_DISTANCE = 30.0f;
    /// <summary>NPC 的**占位称呼**：07 §8.2 把镇上的非功能角色写成「平民 NPC／若干路人」。</summary>
    public const string NPC_SPEAKER = "路人";
    /// <summary>站多近才能按 E 进店（格 = 32px，这里约 1.5 格）。</summary>
    public const float BUILDING_DISTANCE = 48.0f;
    /// <summary>事件判定位点的交互距离（站在草丛／石碑旁边按 E）。</summary>
    public const float EVENT_DISTANCE = 40.0f;
    /// <summary>
    /// 出口要「先走出去一次」才武装：出生点可能就压在出口位点上（清风驿的城门就是这么摆的），
    /// 不定这条的话一进图就被判定成走到出口，立刻弹回大地图（真踩过）。
    /// </summary>
    public const float EXIT_ARM_MARGIN = 8.0f;
    /// <summary>
    /// 刚进场/刚回图的**接触宽限期**（秒）：败北回图时落点就在敌人身上，推开之后还可能被墙顶回
    /// 接触圈——那也会当场又开一场，玩家等于被锁在败仗里。宽限期内忽略接触事件。
    /// </summary>
    public const float CONTACT_GRACE = 0.5f;

    /// <summary>
    /// 设施的显示名（这几个在 07 文档里是「无表信息点」，所以名字写在这里）：
    /// 场景里它们和建筑一样是**看不见的 Marker2D**，不标出来玩家不知道哪栋是客栈。
    /// 建筑名从 `building_def.name_cn` 取（表里有）。
    /// </summary>
    public static readonly Dictionary FACILITY_LABELS = new()
    {
        { "facility_inn", "客栈" },
        { "facility_pawnshop", "当铺" },
        { "facility_bounty_board", "悬赏板" },
        // 0.31.0：书铺（陆文昭「本命机遇·书箱底」的落点，07 §4.1 新增的无表信息点）。
        { "facility_bookshop", "书铺" },
    };

    /// <summary>
    /// 引导第一步的旗标（09 §3.1「去清风驿的告示板看看」）。表里 `guide_step.condition` 写的就是这个名字，
    /// 这里只负责在玩家真的读了告示板时点亮它——**引导推进本身归 `GuideService`**。
    /// </summary>
    public const string FLAG_BOARD_READ = "flag_board_read";

    /// <summary>
    /// 打赢某支队伍之后、回到图上要摆出来的那段对话（20 号 §七 的终局难题）。value 是 `dialogue_node.node_id`
    /// ——「还没选过」「对质打过了」这些**条件写在表里**，代码只做「上一场打赢的是哪支队伍 → 翻哪张对话」。
    /// </summary>
    public static readonly Dictionary TEAM_WIN_DIALOGUES = new() { { "team_boss", "dl_ledger_choice" } };

    /// <summary>
    /// 判定**通过**之后要接着说的那句话（20 号 §四 幕四：地牢那次判定过了，铁栏后的人才开口）。
    /// </summary>
    public static readonly Dictionary EVENT_DIALOGUES = new() { { "ev_shen_rescue", "dl_shen_cell" } };

    /// <summary>`BUILDING_DISTANCE` 的取值口（原来给 GDScript 侧的 `verify_maps.gd` 用——它读不到 `const`；
    /// 那份校验器 2026-10-11 已搬 C#、改成直接读常量，**这个口暂无调用方**，留着无害）。</summary>
    public static float building_distance() => BUILDING_DISTANCE;

    /// <summary>`FACILITY_LABELS` 的取值口（理由见上一条）。</summary>
    public static Dictionary facility_labels() => FACILITY_LABELS;

    [Export] public Callable battle_switch_handler;
    [Export] public Callable return_handler;
    /// <summary>跨图捷径（挖通 → 直通后山地牢）要重进小地图场景；自检／用例用它拦下切场景。</summary>
    [Export] public Callable portal_switch_handler;
    [Export] public Variant state_override;
    /// <summary>
    /// 存档设施（用例可注入临时目录；正常流程用 `SaveStore.default_dir()`）。
    /// **故意收成 `GodotObject`**：GDScript 用例会注入只有 `save_slot(slot, state)` 的桩
    /// （`_FailingStore`）来验「写不进去时不静默」，而桩不可能是 C# 的 `SaveStore`——
    /// 参数写死类型会在注入那一刻抛 `InvalidCastException`（与 `SaveService.store` 同一条理由）。
    /// </summary>
    [Export] public GodotObject save_store_override = null;

    public TableDb db = null;
    [Export] public string scene_id = "";
    [Export] public Node2D world = null;
    [Export] public PlayerController player = null;
    [Export] public Camera2D camera = null;
    [Export] public Array teams = new();
    [Export] public Array chests = new();
    [Export] public Array triggers = new();
    /// <summary>城镇 NPC 站位（`Characters/npc_slot_0N`）：只有位置，内容等对话表。</summary>
    [Export] public Array npcs = new();
    /// <summary>商店是覆盖在当前场景上的界面（不切场景，所以玩家位置与清怪状态都不丢）。</summary>
    [Export] public Node shop_panel = null;
    /// <summary>打坐界面（同上，客栈用）。</summary>
    [Export] public Node cultivate_panel = null;
    /// <summary>线索本（K 打开：本图的隐藏内容与事件判定 + 线索来源）。</summary>
    [Export] public Node clue_panel = null;
    /// <summary>完成度界面（M 打开：四项完成度 + 楼层与扫荡按钮）。</summary>
    [Export] public Node dungeon_panel = null;
    [Export] public Node npc_panel = null;
    [Export] public GodotObject _save_service = null;

    public Label _status;
    /// <summary>开局引导 HUD（设计 09 §3.1）：HUD 第四行常驻「当前目标」。</summary>
    public Label _guide_label = null;
    /// <summary>
    /// 宝箱守卫（设计 09 §一）：玩家已经听过谁的「要价」（guard_id → true）。
    /// 只放会话里：第一次按 E 只是让他开口，第二次才付账——这条确认链不需要跨存档记住。
    /// </summary>
    private Dictionary _guard_demands = new();
    /// <summary>战斗外增益 HUD（08）：城镇／副本里也要看得见（与大地图共用同一份文案）。</summary>
    public Label _field_label = null;
    private float _field_refresh_timer = 0.0f;
    /// <summary>副本完成度 HUD（只有 has_completion 的图显示）。</summary>
    public Label _progress_label = null;
    private CanvasLayer _hud = null;
    /// <summary>事件判定位点：{check_id, node, position}。</summary>
    [Export] public Array events = new();
    /// <summary>观察点位点：{point_id, node, position}（设计 20 §3.2；只在这张图里收）。</summary>
    [Export] public Array flavor_points = new();
    public EventCheckService _event_service = null;
    /// <summary>浮层栈（设计 18.1）：商店／打坐／角色／线索本／完成度都压在它上面。</summary>
    private GodotObject _overlays = null;
    private bool _was_sneaking = false;
    private GodotObject _dungeon = null;
    /// <summary>遮挡层（`Overlay`）与它当前是不是压在玩家身上（0.32.0：石隙死路的岩檐）。</summary>
    private TileMapLayer _overlay_layer = null;
    public bool _overlay_covering = false;
    /// <summary>这一层的 TileSet 认不认 `covering` 自定义数据（老资产没有这一层 → 不做半透明，不报错）。</summary>
    private bool _covering_supported = false;
    /// <summary>条件地表层（`Conditional`）：拿到藏宝图才显示石隙那条细径。</summary>
    public TileMapLayer _conditional_layer = null;
    private string _last_room_id = "";
    private Vector2 _exit_position = Vector2.Zero;
    private bool _has_exit = false;
    /// <summary>出口是否已经武装（出生点可能就在出口上，先走出去一次才算数）。</summary>
    private bool _exit_armed = false;
    /// <summary>接触宽限期的剩余秒数（见 `CONTACT_GRACE`）。</summary>
    public float _contact_grace = 0.0f;

    private Variant _opening_choice_node = DialogueService.opening_node();

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--local-selftest"))
        {
            Callable.From(_run_local_selftest).CallDeferred();
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
        GodotObject session_node = _session_node();
        if (session_node != null && session_node.Get("state").AsGodotObject() == null)
        {
            // 自检或直接进小地图时没有会话状态：临时开一局（掉落会落在这份临时存档上）
            GodotObject fresh = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
            session_node.Call("set_state", fresh);
        }

        scene_id = session_node != null ? session_node.Get("pending_local_scene").AsString() : "";
        if (scene_id.Length == 0)
        {
            // 自检默认挑副本（城镇没有房间敌人/宝箱/触发），正常流程由大地图写 pending_local_scene
            scene_id = _has_user_arg("--local-selftest") ? "scene_heifengzhai" : "scene_qingfengyi";
        }

        var packed = GD.Load<PackedScene>($"res://scenes/maps/{scene_id}.tscn");
        if (packed == null)
        {
            GD.PushError($"[LocalMap] 没有场景 {scene_id}");
            return;
        }

        world = packed.Instantiate<Node2D>();
        AddChild(world);
        var ysort = world.GetNodeOrNull<Node2D>("YSort");
        if (ysort != null)
        {
            ysort.YSortEnabled = true;
        }

        _prepare_conditional_layer();
        _prepare_overlay_cover();
        _spawn_player();
        _spawn_teams();
        _spawn_chests();
        _spawn_triggers();
        _collect_npc_slots();
        _build_place_labels();
        _collect_events();
        _collect_flavor_points();
        // 战败处理（08）要「回最近到过的出生点或城镇」——**进城镇时在这里登记**。
        _note_shelter_if_town(session_node);
        _build_status_label();
        _refresh_status();
        _build_progress_label();
        _build_guide_label();
        // 进图就把「条件早就满足」的同伴收进来（例如旗标是在别的图上点亮的）；**城镇除外**。
        if (!_is_town())
        {
            _join_pending_recruits(false);
        }

        // 剧情节点（0.22.0）：条件旗标已点亮 + 到了这张图 → 发武学；`chapter_end` 节点同时置章节完成条件。
        _claim_story_nodes();
        // 剧情抉择（20 §七）：上一场打赢大寨主之后，终局难题就在这张图上等人回答。
        _open_pending_choice_dialogue();
        // 备货旗标这类「口径写在表里、但没人去置」的条件，进图时算一次
        GuideService.refresh_derived_flags(db, current_state());
        if (_clear_team_overlap())
        {
            _contact_grace = CONTACT_GRACE;
        }
    }

    // ---------------------------------------------------------------- 地表分层（0.32.0）

    /// <summary>
    /// 条件地表（设计 16 §3.2 第 5 层 `Conditional`）：**整层**显隐，不是逐格擦。
    /// 判据与大地图**共用一份**（`WorldMapService.conditional_layer_rule`），这里只是把「本图的地标」
    /// 缩成母地标那一个（`map_local.parent_node`）。
    /// 返回 {apply, items}：apply=false 表示这层没有条件可依（保持地编摆的样子，不擅自隐藏）。
    /// </summary>
    public Dictionary conditional_layer_rule()
    {
        var node_ids = new System.Collections.Generic.List<string>();
        Resource local_row = db != null ? db.get_row("map_local", scene_id) : null;
        if (local_row != null && local_row.Get("parent_node").AsString().Length > 0)
        {
            node_ids.Add(local_row.Get("parent_node").AsString());
        }

        return WorldMapService.conditional_layer_rule(db, node_ids.ToArray());
    }

    /// <summary>这层现在该不该显示（按上一条的规则问背包）。</summary>
    public bool conditional_layer_visible()
    {
        var node_ids = new System.Collections.Generic.List<string>();
        Resource local_row = db != null ? db.get_row("map_local", scene_id) : null;
        if (local_row != null && local_row.Get("parent_node").AsString().Length > 0)
        {
            node_ids.Add(local_row.Get("parent_node").AsString());
        }

        return WorldMapService.conditional_layer_visible(db, current_state(), node_ids.ToArray());
    }

    private void _prepare_conditional_layer()
    {
        _conditional_layer = world.GetNodeOrNull<TileMapLayer>(CONDITIONAL_LAYER);
        _apply_conditional_layer();
    }

    /// <summary>
    /// 把条件地表的显隐刷成当前状态。**每帧便宜**（一次背包查询），随掉图／用图立刻跟上：
    /// 藏宝图可能是在这张图里开箱拿到的，不能等下次进图才显示。
    /// </summary>
    /// <summary>用例／冒烟读它验「这一层真的会显隐」（私有成员对外不可见，所以放开）。</summary>
    public void _apply_conditional_layer()
    {
        if (_conditional_layer == null)
        {
            return;
        }

        _conditional_layer.Visible = conditional_layer_visible();
    }

    private void _prepare_overlay_cover()
    {
        _overlay_layer = world.GetNodeOrNull<TileMapLayer>("Overlay");
        if (_overlay_layer == null || _overlay_layer.TileSet == null)
        {
            return;
        }

        _covering_supported = tile_set_has_covering(_overlay_layer.TileSet);
        _refresh_overlay_cover();
    }

    /// <summary>
    /// TileSet 里有没有 `covering` 这一层自定义数据（由 `tools/mapgen/MapKit.cs` 生成）。
    /// 老资产没有它时静默退化：不做半透明，也不报错。
    /// </summary>
    public static bool tile_set_has_covering(TileSet tile_set)
    {
        if (tile_set == null)
        {
            return false;
        }

        for (int index = 0; index < tile_set.GetCustomDataLayersCount(); index++)
        {
            if (tile_set.GetCustomDataLayerName(index) == "covering")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 玩家（脚下一格 ＋ 头顶半格）有没有被「遮挡类」`Overlay` 瓦片盖住。
    /// 静态：用例可以直接喂一层验判据，不必先造出「地编还没交付的那种图」。
    /// </summary>
    public static bool overlay_covering_at(TileMapLayer layer, Vector2 position)
    {
        if (layer == null || !tile_set_has_covering(layer.TileSet))
        {
            return false;
        }

        foreach (Vector2 probe in new[] { Vector2.Zero, new Vector2(0, -16) })
        {
            Vector2I cell = layer.LocalToMap(layer.ToLocal(position + probe));
            TileData data = layer.GetCellTileData(cell);
            if (data != null && data.GetCustomData("covering").AsBool())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 玩家走到遮挡瓦片下面 → `Overlay` 整层半透明（走开恢复）。
    /// 为什么整层而不是只削那一格：TileMapLayer 没有逐格透明度。
    /// </summary>
    public void _refresh_overlay_cover()
    {
        if (_overlay_layer == null || !_covering_supported)
        {
            return;
        }

        bool under = player != null && overlay_covering_at(_overlay_layer, player.GlobalPosition);
        if (under == _overlay_covering)
        {
            return;
        }

        _overlay_covering = under;
        Color modulate = _overlay_layer.Modulate;
        modulate.A = under ? OVERLAY_COVER_ALPHA : 1.0f;
        _overlay_layer.Modulate = modulate;
    }

    /// <summary>领取这张图上能领的剧情节点（设计 18 §3.1）。返回领到的节点名，供状态栏播报。</summary>
    private Array _claim_story_nodes()
    {
        GodotObject state = current_state();
        if (state == null || db == null)
        {
            return new Array();
        }

        Array claimed = StoryService.claim_for(db, state, scene_id);
        if (claimed.Count == 0)
        {
            return new Array();
        }

        var names = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in claimed)
        {
            Dictionary entry = entry_variant.AsGodotDictionary();
            names.Add(entry.ContainsKey("text_cn")
                ? entry["text_cn"].AsString()
                : (entry.ContainsKey("node_id") ? entry["node_id"].AsString() : ""));
        }

        // 章节完成条件满足 → 推进章节（`chapter_def`，设计 18 §3.1）
        Dictionary advance = ChapterService.try_advance(db, state);
        if (advance.ContainsKey("advanced") && advance["advanced"].AsBool())
        {
            names.Add($"章节推进：{(advance.ContainsKey("name") ? advance["name"].AsString() : "")}");
        }

        if (advance.ContainsKey("text") && advance["text"].AsString().Length > 0)
        {
            names.Add(advance["text"].AsString());
        }

        if (_status != null && names.Count > 0)
        {
            _status.Text = string.Join("；", names);
        }

        _refresh_guide();
        return claimed;
    }

    /// <summary>
    /// 摆一段「旁白／自白」式的对话（**没有「这个人」**）：序幕择念（20 §3.1）用它——
    /// 数据仍是 `dialogue_node`／`dialogue_option`，只是面板按 `MODE_STORY` 渲染。
    /// </summary>
    private bool _open_story_dialogue(string node_id)
    {
        GodotObject state = current_state();
        if (db == null || state == null)
        {
            return false;
        }

        Resource node = DialogueService.node_of(db, node_id);
        if (node == null)
        {
            GD.PushError($"[LocalMap] 要摆的旁白 {node_id} 不在表里");
            return false;
        }

        if (!DialogueService.condition_ok(state, node.Get("condition").AsString()))
        {
            return false;
        }

        open_npc(node.Get("speaker_id").AsString(), "story", node_id);
        return true;
    }

    /// <summary>
    /// 判定通过之后接的那段对话（配在 `EVENT_DIALOGUES` 里的判定才有）。
    /// 「她现在说哪一句」仍然由表的 `condition` 决定（本文件不替她编话）。
    /// </summary>
    private bool _open_event_dialogue(string check_id)
    {
        if (db == null || !EVENT_DIALOGUES.ContainsKey(check_id))
        {
            return false;
        }

        string node_id = EVENT_DIALOGUES[check_id].AsString();
        Resource node = DialogueService.node_of(db, node_id);
        if (node == null)
        {
            GD.PushError($"[LocalMap] 判定 {check_id} 之后要说的对话 {node_id} 不在表里");
            return false;
        }

        GodotObject state = current_state();
        if (state == null || !DialogueService.condition_ok(state, node.Get("condition").AsString()))
        {
            return false;
        }

        string speaker = node.Get("speaker_id").AsString();
        if (speaker.Length == 0)
        {
            GD.PushError($"[LocalMap] 对话 {node_id} 没写说话人");
            return false;
        }

        open_npc(speaker, "interact", node_id);
        return true;
    }

    /// <summary>
    /// 上一场打赢的队伍有没有「回图就该摆出来的那段对话」（20 §七 的终局难题）。
    /// 触发链：`battle_screen._settle()` 把这一场的 `team_id` 写进 `GameSession.last_battle`
    /// → 回到小地图时这里翻 `TEAM_WIN_DIALOGUES` → 取那张 `dialogue_node`。
    /// </summary>
    public bool _open_pending_choice_dialogue()
    {
        GodotObject session_node = _session_node();
        if (session_node == null || db == null)
        {
            return false;
        }

        GodotObject state = current_state();
        if (state == null)
        {
            return false;
        }

        // 候选从哪来分两种情况：① 认得出「上一场打赢的是哪支队」→ **只**摆那一支队配的那张对话；
        // ② 认不出（读档／重开一局）→ 把配置里所有候选都问一遍。② 不是可有可无：读档进来时
        // `last_battle` 已经不在会话里，而**那本账还在背包里**——只认①的话，玩家一读档就再也回答不了。
        Variant last = session_node.Get("last_battle");
        string known_team = last.VariantType == Variant.Type.Dictionary
            ? (last.AsGodotDictionary().ContainsKey("team_id") ? last.AsGodotDictionary()["team_id"].AsString() : "")
            : "";
        var candidates = new System.Collections.Generic.List<string>();
        if (known_team.Length == 0)
        {
            foreach (Variant value in TEAM_WIN_DIALOGUES.Values)
            {
                candidates.Add(value.AsString());
            }
        }
        else if (TEAM_WIN_DIALOGUES.ContainsKey(known_team))
        {
            candidates.Add(TEAM_WIN_DIALOGUES[known_team].AsString());
        }

        foreach (string node_id in candidates)
        {
            Resource node = DialogueService.node_of(db, node_id);
            if (node == null)
            {
                GD.PushError($"[LocalMap] 要摆的对话 {node_id} 不在表里");
                continue;
            }

            if (!DialogueService.condition_ok(state, node.Get("condition").AsString()))
            {
                continue;
            }

            string speaker = node.Get("speaker_id").AsString();
            if (speaker.Length == 0)
            {
                GD.PushError($"[LocalMap] 对话 {node_id} 没写说话人");
                continue;
            }

            // 有意**不记「摆过了」**：条件在表里——没选过就还会摆出来；选过之后同一条件不成立，自然不再弹。
            open_npc(speaker, "interact", node_id);
            return true;
        }

        return false;
    }

    public GodotObject current_state()
    {
        if (state_override.VariantType != Variant.Type.Nil)
        {
            return state_override.AsGodotObject();
        }

        GodotObject session_node = _session_node();
        return session_node != null ? session_node.Get("state").AsGodotObject() : null;
    }

    /// <summary>`GameState` 还是 GDScript，跨语言只能走 `Call`（P5 搬完直接调静态方法）。</summary>
    private static Dictionary _dungeon_record(GodotObject state, string scene_id)
        => state.Call("dungeon_record", scene_id).AsGodotDictionary();

    /// <summary>
    /// Godot 4.7 的 C# `Callable` 没有 `IsValid()`／`IsNull()`（反射确认过公开面只有
    /// `Target`／`Method`／`Delegate`／`Call`），所以按「目标 ＋ 方法名」判（同 `SkillLoadout`）。
    /// </summary>
    /// <summary>
    /// 这个 Callable 有没有真的接上（空的 ＝ 没人接，走兜底路径）。
    ///
    /// **不能照抄 GDScript 那套 `Target != null &amp;&amp; !Method.IsEmpty`**：`Callable.From(…)` 造出来的
    /// 委托型 Callable，`Target` 是 `null`、`Method` 取值还会抛 `NullReferenceException`
    /// （2026-10-11 实测）——照抄的写法短路成 false，于是**注入进来的切场景回调永远不触发**，
    /// 而且是静默的（用例只看到「回调没被调用」，看不出是这一处判据的锅）。
    /// 委托型看 `Delegate`；GDScript 传来的 Callable 才走老口径。
    /// </summary>
    private static bool _callable_valid(Callable callable) =>
        callable.Delegate != null
        || (callable.Target != null && !callable.Method.IsEmpty);

    /// <summary>进的是城镇（`map_local.scene_type=town`）就把它记成「最近的出生点／城镇」。</summary>
    private void _note_shelter_if_town(GodotObject session_node)
    {
        if (session_node == null)
        {
            return;
        }

        Resource row = db.get_row("map_local", scene_id);
        if (row == null || row.Get("scene_type").AsString() != "town")
        {
            return;
        }

        session_node.Call("note_shelter", scene_id, row.Get("name_cn").AsString());
    }

    /// <summary>这张图是不是城镇（`map_local.scene_type=town`）——剧情招募与战败回城都用这一条判。</summary>
    private bool _is_town()
    {
        Resource row = db.get_row("map_local", scene_id);
        return row != null && row.Get("scene_type").AsString() == "town";
    }

    public Dictionary local_state()
    {
        GodotObject session_node = _session_node();
        if (session_node == null)
        {
            return new Dictionary();
        }

        Dictionary maps = session_node.Get("local_maps").AsGodotDictionary();
        if (!maps.ContainsKey(scene_id))
        {
            maps[scene_id] = new Dictionary
            {
                { "cleared", new Array() },
                { "chests", new Array() },
                { "triggers", new Array() },
            };
        }

        return maps[scene_id].AsGodotDictionary();
    }

    // ------------------------------------------------------------------ 生成

    private void _spawn_player()
    {
        player = new PlayerController { Name = "Player", Position = _entrance_position() };
        _add(ysort_node(), player);
        player.ZIndex = 1;
        camera = world.GetNodeOrNull<Camera2D>("Camera");
        if (camera != null)
        {
            camera.GlobalPosition = player.GlobalPosition;
        }
    }

    /// <summary>
    /// 落点：① 捷径指定的房间（挖通） ② 打完回图时的原位置 ③ 该图的入口房间 ④ 出口位点
    ///
    /// ② 以前是写进 `local_position` 就没再读过的——副本里打完一场会被弹回入口，
    /// 现在读回来（并核对 scene，免得跨图错用）。
    /// </summary>
    private Vector2 _entrance_position()
    {
        GodotObject session_node = _session_node();
        // ① 捷径落点：「铁镐挖通 → 直达后山地牢」会把 pending_local_room 写成 hf3_dungeon
        if (session_node != null)
        {
            string wanted = session_node.Get("pending_local_room").AsString();
            if (wanted.Length > 0)
            {
                session_node.Set("pending_local_room", "");
                if (_room_belongs_here(wanted))
                {
                    Variant point = _room_spawn_point(wanted);
                    if (point.VariantType != Variant.Type.Nil)
                    {
                        return point.AsVector2();
                    }
                }
                else
                {
                    GD.PushError($"[LocalMap] {wanted} 不属于本图 {scene_id}，落点请求被忽略");
                }
            }
        }

        // ② 打完回图：接着站在战斗前的位置（必须排在出生点前面，不然这条永远轮不到）
        if (session_node != null && session_node.Get("local_position_scene").AsString() == scene_id)
        {
            Vector2 back = session_node.Get("local_position").AsVector2();
            session_node.Set("local_position", Vector2.Zero);
            session_node.Set("local_position_scene", "");
            if (back != Vector2.Zero)
            {
                return back;
            }
        }

        // ③ 地编放的出生点（`Characters/player_spawn`，metadata role=player）——资产里的第一真相
        Node2D authored = world.GetNodeOrNull<Node2D>("Characters/player_spawn");
        if (authored != null)
        {
            return authored.Position;
        }

        // ④ 入口房间
        string entrance = "";
        foreach (Variant row_variant in db.rows("dungeon_room"))
        {
            Resource row = row_variant.As<Resource>();
            if (row.Get("scene_id").AsString() == scene_id && row.Get("room_type").AsString() == "entrance")
            {
                entrance = row.Get("room_id").AsString();
                break;
            }
        }

        if (entrance.Length > 0)
        {
            Variant point = _room_spawn_point(entrance);
            if (point.VariantType != Variant.Type.Nil)
            {
                return point.AsVector2();
            }

            Node2D room = world.GetNodeOrNull<Node2D>($"Rooms/Room_{entrance}");
            if (room != null)
            {
                return room.Position;
            }
        }

        // ⑤ 出口位点（地图既没有出生点标记也没有入口房间时兜底）
        Node2D exit = world.GetNodeOrNull<Node2D>($"Markers/Exit_{scene_id}");
        if (exit != null)
        {
            return exit.Position;
        }

        return new Vector2(512, 384);
    }

    /// <summary>这个房间 id 是不是本图的（`dungeon_room.scene_id`）。</summary>
    private bool _room_belongs_here(string room_id)
    {
        Resource row = db.get_row("dungeon_room", room_id);
        return row != null && row.Get("scene_id").AsString() == scene_id;
    }

    /// <summary>
    /// 建筑与设施的**可见标识**：场景里 `Markers/Buildings/*` 与 `Markers/Facilities/*` 都是看不见的
    /// Marker2D，玩家看不出哪栋是杂货铺。名字这类「运营信息」由代码补上——和触发点／敌人用 Label
    /// 标名字是同一套占位约定。建筑名取自 `building_def.name_cn`（表），设施名取自 `FACILITY_LABELS`。
    /// </summary>
    private void _build_place_labels()
    {
        foreach (string group in new[] { "Markers/Buildings", "Markers/Facilities" })
        {
            Node box = world != null ? world.GetNodeOrNull(group) : null;
            if (box == null)
            {
                continue;
            }

            foreach (Node child in box.GetChildren())
            {
                if (child is not Node2D node2d)
                {
                    continue;
                }

                string place_id = child.Name;
                string text = "";
                if (group.EndsWith("Buildings"))
                {
                    Resource row = db.get_row("building_def", place_id);
                    if (row != null)
                    {
                        text = row.Get("name_cn").AsString();
                    }
                }
                else
                {
                    Dictionary labels = facility_labels();
                    text = labels.ContainsKey(place_id) ? labels[place_id].AsString() : "";
                }

                if (text.Length == 0)
                {
                    continue;
                }

                var label = new Label { Name = $"PlaceLabel_{place_id}", Text = text, Position = new Vector2(-28, -48) };
                label.AddThemeFontSizeOverride("font_size", 12);
                label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.92f));
                label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
                node2d.AddChild(label);
            }
        }

        // 出口：走到 `Exit_<scene_id>` 上就回大地图（Esc 也能走），但那是个看不见的坐标——
        // 标一个「出口」出来，玩家才知道「门口」在哪。位置可能挂在房间子节点下，所以递归找。
        foreach (Node2D marker in _collect_exit_markers())
        {
            var exit_label = new Label { Name = "PlaceLabel_Exit", Text = "出口", Position = new Vector2(-18, -40) };
            exit_label.AddThemeFontSizeOverride("font_size", 12);
            exit_label.AddThemeColorOverride("font_color", new Color(0.98f, 0.9f, 0.6f, 0.95f));
            exit_label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
            marker.AddChild(exit_label);
        }
    }

    private Array _collect_exit_markers()
    {
        var result = new Array();
        Node markers = world != null ? world.GetNodeOrNull("Markers") : null;
        if (markers != null)
        {
            _collect_exit_markers_in(markers, result);
        }

        return result;
    }

    private void _collect_exit_markers_in(Node node, Array result)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Node2D && child.Name.ToString().StartsWith("Exit_"))
            {
                result.Add(child);
            }

            _collect_exit_markers_in(child, result);
        }
    }

    /// <summary>
    /// 房间里的落点：**bounds 矩形中心**，找不到才退回房间节点位置。
    /// 房间节点位置是矩形左上角（地编的约定），站在角上会被墙碰撞推出去，`current_room_id()` 也就认不到
    /// 这个房间——完成度里的「进过隐藏房间」会漏计。找不到房间返回 null（调用方自己决定退回哪）。
    /// </summary>
    private Variant _room_spawn_point(string room_id)
    {
        Node2D room = world.GetNodeOrNull<Node2D>($"Rooms/Room_{room_id}");
        if (room == null)
        {
            return default;
        }

        var bounds = room.GetNodeOrNull<Area2D>("bounds");
        if (bounds != null)
        {
            Rect2 rect = _bounds_rect(bounds);
            if (rect.Size != Vector2.Zero)
            {
                return rect.GetCenter();
            }
        }

        return room.Position;
    }

    /// <summary>房间敌人：按 dungeon_room.enemy_team + Enemies/&lt;room_id&gt;/Team_&lt;team_id&gt; 位点生成。</summary>
    private void _spawn_teams()
    {
        Array cleared = local_state().ContainsKey("cleared")
            ? local_state()["cleared"].AsGodotArray()
            : new Array();
        foreach (Variant row_variant in db.rows("dungeon_room"))
        {
            Resource row = row_variant.As<Resource>();
            if (row.Get("scene_id").AsString() != scene_id)
            {
                continue;
            }

            string team_id = row.Get("enemy_team").AsString();
            if (team_id.Length == 0)
            {
                continue;
            }

            string room_id = row.Get("room_id").AsString();
            Node2D marker = world.GetNodeOrNull<Node2D>($"Enemies/{room_id}/Team_{team_id}");
            if (marker == null)
            {
                GD.PushError($"[LocalMap] {scene_id} 缺少敌人位点 {room_id}/{team_id}");
                continue;
            }

            Resource team = db.get_row("enemy_team", team_id);
            string room_type = row.Get("room_type").AsString();
            var enemy = new RoamingEnemy { Name = $"RoomTeam_{room_id}", Position = marker.Position };
            enemy.setup(db, new Dictionary
            {
                { "spawn_id", room_id },
                { "source_scene", scene_id },
                { "source_key", room_id },
                { "team_id", team_id },
                { "behavior", "idle" },          // 副本内不追击，原地驻守
                { "alert_radius", 0.0 },
                { "chase_speed", 0.0 },
                { "respawn_sec", 0 },
                { "is_elite", room_type == "elite" || room_type == "boss" },
            }, team, player, null);
            enemy.sneak_detect_reduce = sneak_detect_reduce();
            // C# 的信号名**不转 snake_case**：`[Signal] delegate void EncounteredEventHandler(...)`
            // 暴露出来就叫 `Encountered`（2026-10-11 实测 `geo_signal_list()`）。
            enemy.Encountered += _on_encountered;
            _add(ysort_node(), enemy);
            teams.Add(enemy);
            if (cleared.Contains(room_id))
            {
                enemy.mark_defeated(0);
            }
        }
    }

    private void _spawn_chests()
    {
        Array opened = local_state()["chests"].AsGodotArray();
        foreach (Variant row_variant in db.rows("dungeon_room"))
        {
            Resource row = row_variant.As<Resource>();
            if (row.Get("scene_id").AsString() != scene_id)
            {
                continue;
            }

            string group_id = row.Get("chest_id").AsString();
            if (group_id.Length == 0)
            {
                continue;
            }

            string key = $"{row.Get("room_id").AsString()}|{group_id}";
            Node2D marker = world.GetNodeOrNull<Node2D>(
                $"Markers/{row.Get("room_id").AsString()}/Chest_{group_id}");
            if (marker == null)
            {
                GD.PushError($"[LocalMap] {scene_id} 缺少宝箱位点 {row.Get("room_id").AsString()}/{group_id}");
                continue;
            }

            var chest = new Chest { Name = $"Chest_{key}", Position = marker.Position };
            chest.setup(group_id, player, opened.Contains(key));
            _add(ysort_node(), chest);
            chests.Add(chest);
            chest.SetMeta("key", key);
        }
    }

    private void _spawn_triggers()
    {
        Array used = local_state()["triggers"].AsGodotArray();
        // 按**地图里的位点**生成：同一个触发可能有多个位点（酒葫芦在牢房深处与后山地牢各一处）
        Array markers = _find_all_trigger_markers();
        foreach (Variant marker_variant in markers)
        {
            var marker = marker_variant.As<Node2D>();
            // 位点名 → (触发 id, 顺序编号)：顺序谜题三个编号位点共用一个 `hidden_trigger` 行。
            Dictionary parsed = _parse_trigger_marker(marker.Name.ToString());
            string trigger_id = parsed["trigger_id"].AsString();
            Resource row = db.get_row("hidden_trigger", trigger_id);
            if (row == null || row.Get("scene_id").AsString() != scene_id)
            {
                continue;
            }

            var point = new TriggerPoint { Name = $"Trigger_{trigger_id}_{triggers.Count}", Position = marker.Position };
            // 一次性触发点（once_only=1）看**永久记录**：离图再回来不该能重复拿奖励，
            // 其余点位只按这次进图的会话状态判（回大地图整片刷新）
            bool already = used.Contains(trigger_id);
            if (row.Get("once_only").AsBool())
            {
                GodotObject state = current_state();
                if (state != null && _dungeon_record(state, scene_id).ContainsKey("triggers")
                    && _dungeon_record(state, scene_id)["triggers"].AsGodotArray().Contains(trigger_id))
                {
                    already = true;
                }
            }

            point.setup(row, player, already, db, parsed["index"].AsInt32());
            _add(ysort_node(), point);
            triggers.Add(point);
        }
    }

    /// <summary>
    /// 位点名 → `{trigger_id, index}`。
    ///
    /// 约定（写进 07「地图资源需求」）：顺序谜题的三个位点叫 `Trigger_trig_brazier_1/2/3`，
    /// 表里只有 `trig_brazier` 一行；这里先把整串当 id 查表，查不到再剥掉末尾的 `_&lt;数字&gt;` 重试。
    /// </summary>
    private Dictionary _parse_trigger_marker(string marker_name)
    {
        string raw = marker_name.Substring("Trigger_".Length);
        if (db.get_row("hidden_trigger", raw) != null)
        {
            return new Dictionary { { "trigger_id", raw }, { "index", 0 } };
        }

        int cut = raw.LastIndexOf('_');
        if (cut > 0)
        {
            string suffix = raw.Substring(cut + 1);
            string head = raw.Substring(0, cut);
            if (suffix.IsValidInt() && db.get_row("hidden_trigger", head) != null)
            {
                return new Dictionary { { "trigger_id", head }, { "index", suffix.ToInt() } };
            }
        }

        return new Dictionary { { "trigger_id", raw }, { "index", 0 } };
    }

    /// <summary>存档设施：注入优先，否则用默认目录。</summary>
    public SaveService save_service()
    {
        if (_save_service == null)
        {
            GodotObject store = save_store_override ?? SaveService.make_default();
            _save_service = SaveService.create(store, current_state());
        }

        return (SaveService)_save_service;
    }

    /// <summary>自动存档（副本内的进度点调用）。返回 {ok, ...}。</summary>
    public Dictionary autosave(string reason)
    {
        Dictionary result = save_service().save(reason, true);
        // **自动存档失败要出声**：不然玩家以为「自动存档」把进度保住了，实际上这一段只在本局里。
        // 失败不打断游玩（AGENTS：存档写入失败不允许挡住开局），但状态栏必须如实说一句。
        if (!result.ContainsKey("ok") || !result["ok"].AsBool())
        {
            string head = _status != null && _status.Text.Length > 0 ? _status.Text : reason;
            string error = result.ContainsKey("error") ? result["error"].AsString() : "未知原因";
            _set_status($"{head}　｜　自动存档失败：{error}（这一段的进度只在本局里）");
        }

        return result;
    }

    /// <summary>手动存档：只有城镇是存档点（设计 02「城镇：存档点，可自由存读」）。</summary>
    public bool can_save_here()
    {
        Resource row = db.get_row("map_local", scene_id);
        return row != null && row.Get("scene_type").AsString() == "town";
    }

    public Dictionary press_save()
    {
        if (!can_save_here())
        {
            _set_status("这里不能手动存档：城镇才是存档点（副本里的进度会自动保存）");
            return new Dictionary { { "ok", false }, { "error", "not_a_town" } };
        }

        Dictionary result = save_service().save("城镇存档点", false);
        _set_status(save_service().describe());
        return result;
    }

    /// <summary>潜行时「被发现」判定打折的比例（combat_const.sneak_detect_reduce）。</summary>
    public float sneak_detect_reduce()
    {
        Resource row = db.get_row("combat_const", "sneak_detect_reduce");
        return row != null ? (float)row.Get("value").AsDouble() : 0.4f;
    }

    /// <summary>潜行状态变了就给一句提示。</summary>
    private void _track_sneak()
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
            _set_status($"潜行中：移速 60%，被发现判定 ×{1.0f - sneak_detect_reduce():F2}（被守卫撞上就算潜行失败）");
        }
        else
        {
            _refresh_status();
        }
    }

    /// <summary>收集事件判定位点（`Event_&lt;check_id&gt;`，可能挂在房间子节点下）。</summary>
    private void _collect_events()
    {
        events = new Array();
        if (world == null)
        {
            return;
        }

        foreach (Variant check_variant in event_service().checks_for_scene(scene_id))
        {
            Resource check = check_variant.As<Resource>();
            string check_id = check.Get("check_id").AsString();
            foreach (Variant node_variant in _find_markers(world.GetNodeOrNull("Markers"), $"Event_{check_id}"))
            {
                var node = node_variant.As<Node2D>();
                events.Add(new Dictionary
                {
                    { "check_id", check_id },
                    { "node", node },
                    { "position", node.GlobalPosition },
                });
            }
        }

        // 再按**地图上真的摆了哪些位点**补一遍：表里只填了 region_id（`scene_id` 空）的判定，
        // 只要位点在这张图里就该能按 E——07 文档把赌局摆在清风驿·客栈，而表里 `ev_gamble` 只有 region_id。
        foreach (Variant node_variant in _find_all_marker_nodes(world.GetNodeOrNull("Markers"), "Event_"))
        {
            var node = node_variant.As<Node2D>();
            string marker_check_id = node.Name.ToString().TrimPrefix("Event_");
            if (marker_check_id.Length == 0 || _has_event(marker_check_id))
            {
                continue;
            }

            if (!event_service().check_allowed_in_scene(marker_check_id, scene_id))
            {
                continue;
            }

            events.Add(new Dictionary
            {
                { "check_id", marker_check_id },
                { "node", node },
                { "position", node.GlobalPosition },
            });
        }
    }

    /// <summary>这张图的 events 里已经有这条判定了吗（避免同一个位点被两种来源各收一次）。</summary>
    private bool _has_event(string check_id)
    {
        foreach (Variant entry_variant in events)
        {
            if (entry_variant.AsGodotDictionary()["check_id"].AsString() == check_id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>递归找所有名字以 prefix 开头的 Marker（返回节点本身，名字里带的是 check_id）。</summary>
    private Array _find_all_marker_nodes(Node root, string prefix)
    {
        var result = new Array();
        if (root == null)
        {
            return result;
        }

        foreach (Node child in root.GetChildren())
        {
            if (child is Node2D && child.Name.ToString().StartsWith(prefix))
            {
                result.Add(child);
            }

            foreach (Variant nested in _find_all_marker_nodes(child, prefix))
            {
                result.Add(nested);
            }
        }

        return result;
    }

    /// <summary>递归找某个名字的 Marker（大地图与小地图都可能有嵌套）。</summary>
    private Array _find_markers(Node root, string node_name)
    {
        var result = new Array();
        if (root == null)
        {
            return result;
        }

        foreach (Node child in root.GetChildren())
        {
            if (child.Name.ToString() == node_name)
            {
                result.Add(child);
            }

            foreach (Variant nested in _find_markers(child, node_name))
            {
                result.Add(nested);
            }
        }

        return result;
    }

    public EventCheckService event_service() =>
        _event_service ??= EventCheckService.create(db, current_state(), null);

    /// <summary>玩家身边的事件位点（最近的那个 check_id）。</summary>
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
            float distance = player.GlobalPosition.DistanceTo(entry["position"].AsVector2());
            if (distance <= best_distance)
            {
                best = entry["check_id"].AsString();
                best_distance = distance;
            }
        }

        return best;
    }

    /// <summary>开店：把界面盖在当前场景上（不切场景，回来时位置与状态都还在）。</summary>
    public Dictionary open_shop(string building_id)
    {
        if (_panel_open(shop_panel))
        {
            shop_panel.Call("open_building", building_id);
            return new Dictionary
            {
                { "ok", true }, { "error", "" }, { "building_id", building_id }, { "reopened", true },
            };
        }

        var panel = GD.Load<PackedScene>(SHOP_SCENE).Instantiate();
        if (panel == null)
        {
            return new Dictionary { { "ok", false }, { "error", "商店场景加载失败" }, { "building_id", building_id } };
        }

        panel.Name = "ShopPanel";
        panel.Set("state_override", current_state());
        panel.Set("building_id", building_id);
        panel.Set("return_handler", Callable.From(close_shop));
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        shop_panel = panel;
        _push_overlay("shop");
        string welcome = $"进店：{panel.Call("building_name").AsString()}（买入／卖出／回购，Esc 或点「离开」出来）";
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
        _set_status("离开店铺");
    }

    /// <summary>打坐（客栈）：{ok, error, facility}。</summary>
    public Dictionary open_cultivate()
    {
        if (_panel_open(cultivate_panel))
        {
            _push_overlay("cultivate");
            cultivate_panel.Set("state_override", current_state());
            cultivate_panel.Call("refresh");
            return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", true } };
        }

        var panel = GD.Load<PackedScene>(CULTIVATE_SCENE).Instantiate();
        if (panel == null)
        {
            return new Dictionary { { "ok", false }, { "error", "打坐场景加载失败" } };
        }

        panel.Name = "CultivatePanel";
        panel.Set("state_override", current_state());
        panel.Set("return_handler", Callable.From(close_cultivate));
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        cultivate_panel = panel;
        _push_overlay("cultivate");
        const string welcome = "客栈打坐：一次 +1 熟练度，费用随熟练度递增（Esc 或点「离开」出来）";
        panel.Call("show_message", welcome);
        _set_status(welcome);
        return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", false } };
    }

    public void close_cultivate()
    {
        _close_overlay("cultivate");
        _set_status("离开客栈");
    }

    private bool _panel_open(Node panel) => panel != null && IsInstanceValid(panel);

    /// <summary>
    /// 城镇木桩练习战（设计 09 §3.3）：按 E 直接进一场「木桩不还手」的战斗。
    /// 两条前置都写在界面上、不静默：**练到顶** → 显示 cap_notice，不开战；**数据没到** → 日志点名缺哪一行。
    /// </summary>
    public Dictionary start_practice(string building_id)
    {
        GodotObject state = current_state();
        // **到顶先判**：这条提示不依赖木桩的战斗数据（数据没到也该告诉玩家「练不出东西了」）
        if (!PracticeService.xp_allowed(db, state))
        {
            _set_status(PracticeService.cap_notice(db));
            return new Dictionary
            {
                { "ok", false }, { "error", "练到顶了" }, { "capped", true }, { "building_id", building_id },
            };
        }

        Dictionary ready = PracticeService.data_ready(db);
        if (!ready["ok"].AsBool())
        {
            string why = ready["error"].AsString();
            GD.PushError($"[LocalMap] 木桩练习战缺数据：{why}");
            _set_status("木桩还没准备好（缺一条配置，已记进日志）");
            return new Dictionary { { "ok", false }, { "error", why }, { "building_id", building_id } };
        }

        Encounter encounter = PracticeService.build_encounter(db, scene_id, _difficulty());
        _start_battle(encounter);
        _set_status($"木桩练习：打它练到 {PracticeService.cap_level(db)} 级"
            + $"（招式熟练度到 {PracticeService.mastery_cap(db)} 级）");
        return new Dictionary
        {
            { "ok", true }, { "practice", true }, { "encounter", encounter }, { "building_id", building_id },
        };
    }

    /// <summary>
    /// 开箱前先看这间房有没有守卫（设计 09 §一）：**没有守卫照旧直接开**，
    /// 有守卫且没解决就走守卫流程（文取付账／武取＝打赢房里那场仗）。
    /// </summary>
    private Dictionary _try_open_chest(Chest chest)
    {
        string room_id = GuardService.room_of_chest_key(_chest_key(chest));
        Resource guard = GuardService.guard_for_room(db, room_id);
        if (guard == null || GuardService.is_resolved(db, current_state(), scene_id, room_id))
        {
            return _open_chest(chest);
        }

        return _approach_guard(room_id, guard);
    }

    private static string _chest_key(Chest chest)
    {
        Variant key = chest.GetMeta("key");
        return key.VariantType == Variant.Type.Nil ? "" : key.AsString();
    }

    /// <summary>
    /// 守卫流程：**第一次按 E 只是让他开口**，第二次按 E 才付账——免费的东西不该在「以为要开箱」的
    /// 那一下被拿走。武取不用按键：房里那支队伍就站在那儿，走上去开打。
    /// </summary>
    private Dictionary _approach_guard(string room_id, Resource guard)
    {
        GodotObject state = current_state();
        string guard_id = guard.Get("guard_id").AsString();
        if (!_guard_demands.ContainsKey(guard_id))
        {
            _guard_demands[guard_id] = true;
            _set_status(GuardService.demand_text(db, state, guard));
            return new Dictionary
            {
                { "ok", false }, { "error", "守卫挡着宝箱" }, { "guard", guard_id },
                { "stage", "demand" }, { "room_id", room_id },
            };
        }

        Dictionary paid = new() { { "ok", false }, { "error", "" } };
        // 文取可能有多条路同时可用（0.14.0：三字段可叠加）。**优先走不花东西的那条**。
        Dictionary chosen = new();
        foreach (Variant option_variant in GuardService.peace_options(db, state, guard))
        {
            Dictionary option = option_variant.AsGodotDictionary();
            if (!option["ok"].AsBool())
            {
                continue;
            }

            if (chosen.Count == 0)
            {
                chosen = option;
                continue;
            }

            if (chosen["kind"].AsString() == "item" && option["kind"].AsString() != "item")
            {
                chosen = option;
            }
        }

        if (chosen.Count > 0)
        {
            paid = GuardService.pay_peace(db, state, guard, chosen["kind"].AsString(), chosen["id"].AsString());
        }

        if (paid["ok"].AsBool())
        {
            _guard_demands.Remove(guard_id);
            _set_status($"给了 {guard.Get("npc_name_cn").AsString()}：{paid["text"].AsString()}");
            autosave("宝箱守卫（文取）");
            return new Dictionary
            {
                { "ok", true }, { "guard", guard_id }, { "stage", "peace" },
                { "text", paid["text"].AsString() }, { "room_id", room_id },
            };
        }

        string why = paid["error"].AsString();
        if (why.Length == 0)
        {
            why = "身上没有他要的东西，先按上面说的准备一下，或者直接动手";
        }

        _set_status($"{guard.Get("npc_name_cn").AsString()}还挡着：{why}");
        return new Dictionary
        {
            { "ok", false }, { "error", why }, { "guard", guard_id }, { "stage", "refused" }, { "room_id", room_id },
        };
    }

    public Dictionary _open_chest(Chest chest)
    {
        GodotObject state = current_state();
        var inventory = state?.Get("inventory").AsGodotObject() as Inventory;
        if (state == null || inventory == null)
        {
            _set_status("没有会话状态，开不了宝箱");
            return new Dictionary { { "ok", false }, { "error", "没有会话状态" } };
        }

        // 保底计数走**存档里那一份**（与战斗结算、扫荡同一个口径：唯一读写口是
        // `GameState.pity_tracker()`／`store_pity()`）。这里以前 `new` 了一个临时计数器，开箱的尝试记完就丢。
        GodotObject pity = state.Call("pity_tracker").AsGodotObject();
        DropResolver resolver = DropResolver.create(db, _rng(), (PityTracker)pity);
        Array drops = resolver.roll_group(chest.chest_id, _difficulty(), new Dictionary
        {
            { "drop_rate_bonus", DropResolver.party_drop_bonus(_party()) },
        });
        state.Call("store_pity", pity);
        // 宝箱出的装备同样要滚词条：显式传一个随机源，免得每次现开（也方便以后固定种子复用）
        Dictionary applied = BattleReward.grant(db, state, 0, 0, drops, AffixRoller.create(db, _rng()));
        chest.mark_opened();
        // 音效位见设计 07 §8.4「宝箱开启」；事件名→文件的唯一出处是 Sfx.EVENTS
        Sfx.play("chest");
        string key = _chest_key(chest);
        if (key.Length > 0)
        {
            Dictionary local = local_state();
            Array record = local["chests"].AsGodotArray();
            if (!record.Contains(key))
            {
                record.Add(key);
            }

            local["chests"] = record;
            // 完成度是永久记录（会话那份回大地图就清）
            if (state.Call("record_dungeon", scene_id, "chests", key).AsBool())
            {
                _refresh_progress();
                autosave("开宝箱");
            }
        }

        string summary = _drops_summary(applied, drops);
        _set_status($"打开宝箱：{summary}");
        return new Dictionary
        {
            { "ok", true }, { "drops", drops }, { "applied", applied }, { "summary", summary },
        };
    }

    public Dictionary _activate_trigger(TriggerPoint point)
    {
        GodotObject state = current_state();
        Resource row = point.row;
        string required = row.Get("required_item").AsString();
        string trigger_type = row.Get("trigger_type").AsString();
        if (!point.supported())
        {
            _set_status($"{row.Get("name_cn").AsString()}：暂时做不了（{point.unsupported_reason()}）");
            return new Dictionary { { "ok", false }, { "error", "unsupported" } };
        }

        // 同一个触发可能有**多个位点**（酒葫芦在「牢房深处」与「后山地牢」各一处）。这一趟已经触发过的，
        // 另一个位点不该再给一次。
        Array already_triggered = local_state()["triggers"].AsGodotArray();
        if (already_triggered.Contains(row.Get("trigger_id").AsString()))
        {
            _close_trigger_points(row.Get("trigger_id").AsString());
            _set_status($"{row.Get("name_cn").AsString()}：这一趟已经触发过了");
            return new Dictionary { { "ok", false }, { "error", "already_used" } };
        }

        if (required.Length > 0 && (state == null
            || !((Inventory)state.Get("inventory").AsGodotObject()).has(required, 1)))
        {
            _set_status($"{row.Get("name_cn").AsString()}：{point.requirement_text()}");
            return new Dictionary { { "ok", false }, { "error", "missing_item" } };
        }

        // 条件类（宝箱全开／毒杀）先判条件，不满足就不算触发过
        string blocked = _condition_block(row);
        if (blocked.Length > 0)
        {
            _set_status($"{row.Get("name_cn").AsString()}：{blocked}");
            return new Dictionary { { "ok", false }, { "error", "condition_unmet" } };
        }

        string summary = trigger_type switch
        {
            "item" => _trigger_item(row),
            "space" => _trigger_space(row),
            "carry" => _trigger_carry(row),
            "completion" => _trigger_completion(row),
            "kill_style" => _trigger_kill_style(row),
            "behavior" => _trigger_behavior(row),
            "sequence" => "",
            _ => "",
        };
        if (trigger_type == "sequence")
        {
            // 顺序谜题：没点完**不算触发过**（不 mark_used、不记档、不发奖励），所以这里单独收口。
            Dictionary step = _advance_sequence(row, point);
            if (!step["ok"].AsBool())
            {
                _set_status($"{row.Get("name_cn").AsString()}：{step["error"].AsString()}");
                return new Dictionary
                {
                    { "ok", false }, { "error", "sequence_step" }, { "summary", step["error"].AsString() },
                };
            }

            if (!step["complete"].AsBool())
            {
                _set_status($"{row.Get("name_cn").AsString()}：{step["summary"].AsString()}");
                return new Dictionary
                {
                    { "ok", true }, { "complete", false }, { "summary", step["summary"].AsString() },
                    { "trigger_id", point.trigger_id }, { "granted", new Array() },
                };
            }

            summary = step["summary"].AsString();
        }

        point.mark_used();
        // 同一行的其余位点一起收口：不然玩家走到第二个位点还能再领一次（隐藏 Boss 会再打一遍）
        _close_trigger_points(row.Get("trigger_id").AsString());
        Array used = local_state()["triggers"].AsGodotArray();
        if (!used.Contains(point.trigger_id))
        {
            used.Add(point.trigger_id);
        }

        local_state()["triggers"] = used;
        if (state.Call("record_dungeon", scene_id, "triggers", point.trigger_id).AsBool())
        {
            _refresh_progress();
            autosave("触发隐藏内容");
        }

        // 隐藏内容里配的武学（source_type=hidden，source_id 就是这个触发点）
        Array granted = SkillGrant.grant_from_source(db, state, "hidden", point.trigger_id);
        string[] grant_lines = SkillGrant.summarize(granted);
        string full = summary;
        if (grant_lines.Length > 0)
        {
            full = $"{summary}　{string.Join("；", grant_lines)}";
        }

        _set_status($"{row.Get("name_cn").AsString()}：{full}");
        return new Dictionary
        {
            { "ok", true }, { "complete", true }, { "summary", full },
            { "trigger_id", point.trigger_id }, { "granted", granted },
        };
    }

    /// <summary>
    /// 顺序谜题（三火盆）：按 `hidden_trigger.sequence`（例 `1-3-2`）逐次点**对**才成立。
    /// 三个位点的编号来自地图命名 `Trigger_trig_brazier_1/2/3`（07 资源需求里写明）。
    /// 返回 {ok, complete, summary, error}；**没点完不算触发过**。
    /// </summary>
    private Dictionary _advance_sequence(Resource row, TriggerPoint point)
    {
        Array expected = _sequence_steps(row);
        if (expected.Count == 0)
        {
            // 数据错：列名只进日志（AGENTS：玩家可见文案不许出现表内 id）
            GD.PushError($"[LocalMap] hidden_trigger {row.Get("trigger_id").AsString()} 没写 sequence");
            return new Dictionary
            {
                { "ok", false }, { "complete", false },
                { "error", "这条谜题的次序没配（数据错，已记进日志）" },
            };
        }

        int index = point.sequence_index;
        if (index <= 0)
        {
            return new Dictionary
            {
                { "ok", false }, { "complete", false },
                { "error", "这个火盆没有编号（位点要按 _1／_2／_3 命名）" },
            };
        }

        string trigger_id = row.Get("trigger_id").AsString();
        Array progress = _sequence_progress(trigger_id);
        if (progress.Count >= expected.Count)
        {
            progress = new Array();      // 兜底：记录比次序长（不该发生），重来
        }

        // 已经做过的谜题**连第二遍都不接**：`can_interact()` 在玩家路径上已经拦住了，这里再收一道口。
        GodotObject state_for_record = current_state();
        if (state_for_record != null
            && _dungeon_record(state_for_record, scene_id).ContainsKey("triggers")
            && _dungeon_record(state_for_record, scene_id)["triggers"].AsGodotArray().Contains(trigger_id))
        {
            foreach (Variant other_variant in triggers)
            {
                var other = other_variant.As<TriggerPoint>();
                if (other.row.Get("trigger_id").AsString() == trigger_id)
                {
                    other.mark_used();
                }
            }

            return new Dictionary { { "ok", false }, { "complete", false }, { "error", "这条谜题已经做过了" } };
        }

        if (index != expected[progress.Count].AsInt32())
        {
            _set_sequence_progress(trigger_id, new Array());
            _reset_brazier_lights(trigger_id);
            return new Dictionary
            {
                { "ok", false }, { "complete", false },
                { "error", $"次序不对（应当先点第 {expected[0].AsInt32()} 个），进度已重置" },
            };
        }

        progress.Add(index);
        _set_sequence_progress(trigger_id, progress);
        point.set_lit(true);
        // 每点对一格 = 一个火盆被点燃（设计 07 §8.4「火盆点燃」）
        Sfx.play("brazier");
        if (progress.Count < expected.Count)
        {
            return new Dictionary
            {
                { "ok", true }, { "complete", false },
                { "summary", $"点对了第 {index} 个（{progress.Count}/{expected.Count}）" },
            };
        }

        // 凑齐次序 → 发奖励（这条谜题在表里配的是「前代寨主遗物」），并清掉进度
        _set_sequence_progress(trigger_id, new Array());
        // 同一行的**其余位点一起收口**：只把最后点的那一格标成已用的话，另外两格还写着「能点」——
        // 玩家站在原地再按一遍 1-3-2 就能再领一次。这条以前真的能重复刷「前代寨主遗物」。
        _close_trigger_points(trigger_id);
        return new Dictionary
        {
            { "ok", true }, { "complete", true }, { "summary", _grant_trigger_reward(row, "三个火盆依次亮起") },
        };
    }

    /// <summary>
    /// 把同一个触发行的**所有位点**一起收口（标记为已用）。一行两位点的例子：`trig_wine`（酒葫芦）。
    /// </summary>
    private void _close_trigger_points(string trigger_id)
    {
        foreach (Variant other_variant in triggers)
        {
            var other = other_variant.As<TriggerPoint>();
            if (other.row.Get("trigger_id").AsString() == trigger_id)
            {
                other.mark_used();
            }
        }
    }

    /// <summary>点错时把同一行的火盆**全部熄掉**：进度重置了、视觉也要跟着回灭。</summary>
    private void _reset_brazier_lights(string trigger_id)
    {
        foreach (Variant other_variant in triggers)
        {
            var other = other_variant.As<TriggerPoint>();
            if (other.row.Get("trigger_id").AsString() == trigger_id)
            {
                other.set_lit(false);
            }
        }
    }

    /// <summary>`required_condition` 里的 `sequence=1-3-2` → [1, 3, 2]。</summary>
    private Array _sequence_steps(Resource row)
    {
        var result = new Array();
        string condition = _condition_text(row.Get("required_condition").AsString(), "sequence");
        foreach (string piece in condition.Split("-", System.StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = piece.StripEdges();
            if (trimmed.IsValidInt())
            {
                result.Add(trimmed.ToInt());
            }
        }

        return result;
    }

    public Array _sequence_progress(string trigger_id)
    {
        Dictionary table = local_state().ContainsKey("sequence")
            ? local_state()["sequence"].AsGodotDictionary()
            : new Dictionary();
        return table.ContainsKey(trigger_id) ? table[trigger_id].AsGodotArray() : new Array();
    }

    public void _set_sequence_progress(string trigger_id, Array steps)
    {
        Dictionary table = local_state().ContainsKey("sequence")
            ? local_state()["sequence"].AsGodotDictionary()
            : new Dictionary();
        table[trigger_id] = steps.Duplicate();
        local_state()["sequence"] = table;
    }

    /// <summary>条件判定：返回空串表示满足，否则是一句给玩家看的原因。</summary>
    public string _condition_block(Resource row)
    {
        string condition = row.Get("required_condition").AsString();
        switch (row.Get("trigger_type").AsString())
        {
            case "completion":
            {
                // chest_open_rate=1.0：**前寨（第 1 层）**的宝箱全开（设计 03 第六条的原文）
                float want_rate = _condition_value(condition, "chest_open_rate", 1.0f);
                Array gate = _completion_gate_chests(row);
                int total = gate.Count;
                int opened = 0;
                foreach (Variant chest_variant in gate)
                {
                    if (chest_variant.As<Chest>().opened_already)
                    {
                        opened++;
                    }
                }

                float rate = total <= 0 ? 1.0f : (float)opened / total;
                if (rate + 0.0001f < want_rate)
                {
                    return $"还差 {Mathf.Max(0, total - opened)} 个前寨宝箱没开（{opened}/{total}）";
                }

                break;
            }
            case "kill_style":
            {
                // kill_with=poison：上一场要有人死于中毒
                string want_style = _condition_text(condition, "kill_with");
                Dictionary styles = _last_kill_styles();
                if (!_has_kill_style(styles, want_style))
                {
                    return $"要用「{_style_name(want_style)}」击杀目标（上一场的死因：{_styles_label(styles)}）";
                }

                break;
            }
            case "behavior":
                // flag_stealth_full=1：全程潜行不惊动守卫（被任何一个房间的守卫撞上就算失败）
                if (_condition_text(condition, "flag_stealth_full") == "1")
                {
                    Dictionary local = local_state();
                    if (local.ContainsKey("stealth_broken") && local["stealth_broken"].AsBool())
                    {
                        return "潜行已经失败过（被守卫撞上了，出去再进来重新潜）";
                    }
                }

                break;
        }

        return "";
    }

    /// <summary>
    /// 「宝箱全开」这条隐藏内容的计数范围：本图**第 1 层（前寨）**、且**不在隐藏门后面**的宝箱。
    /// 为什么不能直接数 `chests.size()`：① 隐藏门后面的暗格银箱要等门开了才拿得到——算进去永远凑不齐；
    /// ② 三层宝库的金箱不在前寨（设计 03 第六条写的是「**前寨**宝箱全部开启」）。
    /// </summary>
    public Array _completion_gate_chests(Resource row)
    {
        string behind_door = row.Get("reward_id").AsString();
        var result = new Array();
        foreach (Variant chest_variant in chests)
        {
            var chest = chest_variant.As<Chest>();
            string key = _chest_key(chest);
            string room_id = key.Length > 0 ? key.Split("|", System.StringSplitOptions.RemoveEmptyEntries)[0] : "";
            if (room_id.Length == 0 || room_id == behind_door)
            {
                continue;
            }

            Resource room = db.get_row("dungeon_room", room_id);
            if (room != null && room.Get("floor").AsInt32() == 1)
            {
                result.Add(chest);
            }
        }

        return result;
    }

    /// <summary>从 "key=value" 里取数值。</summary>
    private static float _condition_value(string condition, string key, float fallback)
    {
        foreach (string part in condition.Split(";", System.StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = part.Split("=", System.StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 2 && pieces[0].StripEdges() == key)
            {
                return (float)pieces[1].ToFloat();
            }
        }

        return fallback;
    }

    private static string _condition_text(string condition, string key)
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

    /// <summary>上一场战斗的击杀方式（battle_screen 写在 GameSession.last_battle 里）。</summary>
    private Dictionary _last_kill_styles()
    {
        GodotObject session_node = _session_node();
        if (session_node == null)
        {
            return new Dictionary();
        }

        Variant last = session_node.Get("last_battle");
        if (last.VariantType == Variant.Type.Dictionary)
        {
            Variant styles = last.AsGodotDictionary().ContainsKey("kill_styles")
                ? last.AsGodotDictionary()["kill_styles"]
                : default;
            if (styles.VariantType == Variant.Type.Dictionary)
            {
                return styles.AsGodotDictionary();
            }
        }

        return new Dictionary();
    }

    private static bool _has_kill_style(Dictionary styles, string want)
    {
        if (want.Length == 0)
        {
            return false;
        }

        foreach (Variant actor_id in styles.Keys)
        {
            if (styles[actor_id].AsString() == want)
            {
                return true;
            }
        }

        return false;
    }

    private string _styles_label(Dictionary styles)
    {
        if (styles.Count == 0)
        {
            return "没有战斗记录";
        }

        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant actor_id in styles.Keys)
        {
            parts.Add($"{_actor_label(actor_id.AsString())}（{_style_name(styles[actor_id].AsString())}）");
        }

        return string.Join("、", parts);
    }

    /// <summary>
    /// 击杀方式的中文名：`kill_with=poison` 这类条件值**不许直接给玩家看**。
    /// 名字的**唯一出处是表**（`status_effect.name_cn`）；`normal`（正面击杀）是唯一代码侧的名字。
    /// </summary>
    public string _style_name(string style)
    {
        if (style == "normal")
        {
            return "正面击杀";
        }

        Resource row = db.get_row("status_effect", style);
        return row != null ? row.Get("name_cn").AsString() : style;
    }

    /// <summary>战报里的击杀者署名：char_id → 角色名、敌 id → 敌人名，都查不到才回 id。</summary>
    private string _actor_label(string actor_id)
    {
        string bare = actor_id.Split("#", System.StringSplitOptions.RemoveEmptyEntries)[0];
        GodotObject state = current_state();
        if (state != null)
        {
            string name_cn = state.Call("char_name", db, bare).AsString();
            if (name_cn != bare)
            {
                return name_cn;
            }
        }

        return db.display_name(bare);
    }

    /// <summary>completion 类：宝箱全开 → 隐藏门浮现（room 奖励会把玩家带进 `hf1_secret`）。</summary>
    private string _trigger_completion(Resource row) => _grant_trigger_reward(row, "墙上浮现一道暗门");

    /// <summary>kill_style 类：按击杀方式给奖励（毒杀毒手 → 掉落独门秘籍）。</summary>
    private string _trigger_kill_style(Resource row)
    {
        GodotObject state = current_state();
        if (state == null || state.Get("inventory").AsGodotObject() == null)
        {
            return "没有会话状态，拿不到奖励";
        }

        string style = _condition_text(row.Get("required_condition").AsString(), "kill_with");
        return _grant_trigger_reward(row, $"达成「{_style_name(style)}」");
    }

    /// <summary>behavior 类：全程潜行不惊动守卫 → 额外剧情（奖励走同一套发放）。</summary>
    private string _trigger_behavior(Resource row)
    {
        GodotObject state = current_state();
        if (state == null || state.Get("inventory").AsGodotObject() == null)
        {
            return "没有会话状态，拿不到奖励";
        }

        return _grant_trigger_reward(row, "潜行入寨成功");
    }

    /// <summary>触发点的奖励发放（item／skillbook／equip／event／room 都走这里）。</summary>
    public string _grant_trigger_reward(Resource row, string prefix)
    {
        GodotObject state = current_state();
        string reward_type = row.Get("reward_type").AsString();
        string reward_id = row.Get("reward_id").AsString();
        switch (reward_type)
        {
            case "item":
            case "skillbook":
            {
                // 走与战斗／宝箱／事件判定同一套入账口径（货币进钱、装备建实例、其余堆叠）：
                // 以前这里直接 add_item，配一条「奖励 500 文」就会把铜钱当成一件背包物品发出去。
                Dictionary granted = BattleReward.grant_item(db, state, reward_id, 1, null);
                if (!granted["ok"].AsBool())
                {
                    return $"想给 {item_name(reward_id)}，但{granted["error"].AsString()}";
                }

                if (granted["kind"].AsString() == "money")
                {
                    return $"{prefix}，得到 {granted["qty"].AsInt32()} 文钱";
                }

                return $"{prefix}，得到 {item_name(reward_id)} ×{granted["qty"].AsInt32()}";
            }
            case "equip":
            {
                string instance_id = ((Inventory)state.Get("inventory").AsGodotObject())
                    .add_equipment(db, reward_id, new Array());
                if (instance_id.Length == 0)
                {
                    return $"想给 {item_name(reward_id)}，但造不出实例";
                }

                return $"{prefix}，得到装备 {item_name(reward_id)}";
            }
            case "event":
                // 剧情旗标是内部记账（event_stealth_reward 这种），玩家看前缀那句话就够了
                state.Call("set_flag", reward_id, true);
                return $"{prefix}，记下了这段经过";
            case "room":
            {
                Node2D target = world.GetNodeOrNull<Node2D>($"Rooms/Room_{reward_id}");
                if (target != null && player != null)
                {
                    player.GlobalPosition = target.Position;
                    return $"{prefix}，被带到「{db.display_name(reward_id)}」";
                }

                // 房间不在这张图（= 数据错）：id 只进日志，别印给玩家
                GD.PushError($"[LocalMap] 触发奖励指向的房间不在这张图：{reward_id}（当前 {scene_id}）");
                return $"{prefix}（目标房间不在本图）";
            }
        }

        // 走到这儿说明表里写了一个校验器不认识的 reward_type——是**数据错**，不是「功能没做」。
        GD.PushError($"[LocalMap] 触发奖励的 reward_type 不认识：{reward_type}（trigger={row.Get("trigger_id").AsString()}）");
        return $"{prefix}（数据错：未知的奖励类型，已记进日志）";
    }

    public string item_name(string item_id) => db.display_name(item_id);

    /// <summary>item 类：给东西触发隐藏 Boss（酒葫芦 → 醉刀客）。</summary>
    private string _trigger_item(Resource row)
    {
        if (row.Get("reward_type").AsString() != "boss")
        {
            GD.PushError($"[LocalMap] item 触发的奖励类型配错了：{row.Get("trigger_id").AsString()}"
                + $" 写成了 {row.Get("reward_type").AsString()}（item 触发只认 boss）");
            return "数据错：这个触发点的奖励类型配错了（item 触发只认 boss，已记进日志）";
        }

        string enemy_id = row.Get("reward_id").AsString();
        Resource enemy = db.get_row("enemy_base", enemy_id);
        if (enemy == null)
        {
            GD.PushError($"[LocalMap] 隐藏 Boss 奖励指向的敌人不存在：{enemy_id}");
            return "数据错：奖励指向的隐藏 Boss 不在敌人表里（已记进日志）";
        }

        _start_battle(_boss_encounter(enemy_id, row.Get("trigger_id").AsString()));
        return $"隐藏 Boss {enemy.Get("name_cn").AsString()} 出现！";
    }

    /// <summary>
    /// 「单人 Boss 队」的 Encounter（隐藏 Boss 与「判定通过后 Boss 现身」共用一份构造）。
    /// `source_key` 是业务侧的来源标识：隐藏触发用 `trigger_id`，事件判定用 `check_id`。
    /// </summary>
    private Encounter _boss_encounter(string enemy_id, string source_key)
    {
        Resource enemy = db.get_row("enemy_base", enemy_id);
        if (enemy == null)
        {
            GD.PushError($"[LocalMap] Boss 奖励指向的敌人不存在：{enemy_id}");
            return null;
        }

        return Encounter.build(db, new Dictionary
        {
            { "spawn_id", source_key },
            { "source_scene", scene_id },
            { "source_key", source_key },
            { "team_id", $"team_hidden_{enemy_id}" },
            { "is_elite", true },
        }, new Dictionary
        {
            { "name_cn", enemy.Get("name_cn").AsString() },
            { "threat_tag", enemy.Get("threat_tag").AsString() },
            { "members", $"{enemy_id}:1" },
        }, Encounter.contact_front(), _difficulty());
    }

    /// <summary>space 类：用工具打开捷径（铁镐挖通 → 传送到目标房间）。</summary>
    public string _trigger_space(Resource row)
    {
        string room_id = row.Get("reward_id").AsString();
        Node2D room = world.GetNodeOrNull<Node2D>($"Rooms/Room_{room_id}");
        if (room != null)
        {
            // 目标房间在同一张图：直接挪过去（例：图内隐藏门）
            if (player != null)
            {
                Variant point = _room_spawn_point(room_id);
                player.GlobalPosition = point.VariantType == Variant.Type.Nil ? room.Position : point.AsVector2();
            }

            return $"挖通！直接通到「{db.display_name(room_id)}」";
        }

        // 目标房间不在这张图——设计 07 给这种捷径留了 `Portal_<scene_id>` 位点。
        // 以前这里只会回一句「找不到目标房间」，等于后山密道根本走不通。
        string target_scene = _scene_of_room(room_id);
        if (target_scene.Length == 0)
        {
            GD.PushError($"[LocalMap] 捷径奖励指向的房间不在 dungeon_room 表里：{room_id}");
            return "数据错：目标房间不在副本房间表里（已记进日志）";
        }

        Node2D portal = world.GetNodeOrNull<Node2D>($"Markers/Portal_{target_scene}");
        if (portal == null)
        {
            // 玩家看不懂 `Portal_scene_heifengzhai` 这种 id；位点名只进日志（那才是地编要看的）
            GD.PushError($"[LocalMap] 缺 Portal_{target_scene} 位点：从 {scene_id} 挖通后进不去 {room_id}");
            return $"挖通了，但从这里进不去「{db.display_name(room_id)}」——本图缺通往那边的入口位点（等地编补）";
        }

        GodotObject session_node = _session_node();
        if (session_node == null)
        {
            return $"挖通了，但没有会话状态，进不去「{db.display_name(room_id)}」";
        }

        // 重进小地图场景，让新落点生效（pending_local_scene + pending_local_room 就是为此写的）
        session_node.Set("pending_local_scene", target_scene);
        session_node.Set("pending_local_room", room_id);
        _remember_world_position(session_node);
        string text = $"挖通！直通「{db.display_name(room_id)}」";
        _set_status(text);
        if (_callable_valid(portal_switch_handler))
        {
            portal_switch_handler.Call(target_scene, room_id);
        }
        else
        {
            _change_scene(LOCAL_RUN);
        }

        return text;
    }

    /// <summary>房间属于哪张小地图（跨图捷径要据此换图）。</summary>
    private string _scene_of_room(string room_id)
    {
        Resource row = db.get_row("dungeon_room", room_id);
        return row != null ? row.Get("scene_id").AsString() : "";
    }

    /// <summary>
    /// 进场／打完回图时，站位可能正好压在还没清的敌人身上（逃跑时房间不会标记已清），
    /// 先推离接触圈，否则一进图就再被同一队抓一次。返回是否真的推过。
    /// </summary>
    private bool _clear_team_overlap()
    {
        if (player == null)
        {
            return false;
        }

        bool pushed = false;
        // 推开距离要够：只推 `CONTACT_DISTANCE + 6` 时，落点常常在墙里，物理把玩家顶回去就又进接触圈。
        float margin = RoamingEnemy.contact_distance() + 30.0f;
        foreach (Variant enemy_variant in teams)
        {
            var enemy = enemy_variant.As<RoamingEnemy>();
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
            enemy.reset_latch();
            _set_status("刚从遭遇里脱身，先退开几步");
            pushed = true;
        }

        return pushed;
    }

    /// <summary>
    /// carry 类：带着某件东西进来（锈剑共鸣）。设计 03 写的是「解锁一剑式」，但 `skill_base` 里还没有
    /// 对应行，所以现在只有一段演出——这是设计侧的数据缺口，等设计补表后 grant 通道自动生效。
    /// </summary>
    private string _trigger_carry(Resource row) =>
        $"剑气共鸣：{db.display_name(row.Get("trigger_id").AsString())} 震了一下"
        + "——对应的武学还没落表，先记一段缘（等设计补）";

    // ------------------------------------------------------------------ 战斗与出口

    public void _on_encountered(string room_id, string contact)
    {
        // 刚回图的宽限期（见 CONTACT_GRACE）：落点被墙顶回接触圈也不该当场又开一场
        if (_contact_grace > 0.0f)
        {
            return;
        }

        Resource row = _room_row(room_id);
        if (row == null)
        {
            return;
        }

        // 被守卫撞上 = 潜行失败（「潜行入寨」这类行为条件靠它判定）
        local_state()["stealth_broken"] = true;
        string team_id = row.Get("enemy_team").AsString();
        Resource team = db.get_row("enemy_team", team_id);
        Encounter encounter = Encounter.build(db, new Dictionary
        {
            { "spawn_id", room_id },
            { "source_scene", scene_id },
            { "source_key", room_id },
            { "team_id", team_id },
            { "is_elite", row.Get("room_type").AsString() == "elite" || row.Get("room_type").AsString() == "boss" },
        }, team, contact, _difficulty());
        _start_battle(encounter);
    }

    private void _start_battle(Encounter encounter)
    {
        GodotObject session_node = _session_node();
        if (session_node != null)
        {
            session_node.Set("pending_encounter", encounter);
            session_node.Set("pending_return_scene", LOCAL_RUN);
            session_node.Set("pending_local_scene", scene_id);
            if (player != null)
            {
                session_node.Set("local_position", player.Position);
                session_node.Set("local_position_scene", scene_id);
            }
        }

        _set_status(encounter.headline());
        if (_callable_valid(battle_switch_handler))
        {
            battle_switch_handler.Call(encounter);
            return;
        }

        _change_scene(BATTLE_SCENE);
    }

    public void leave_to_overworld()
    {
        // 回大地图 = 整片刷新：清掉这张小地图的会话状态
        GodotObject session_node = _session_node();
        if (session_node != null)
        {
            Dictionary maps = session_node.Get("local_maps").AsGodotDictionary();
            maps.Remove(scene_id);
            session_node.Set("pending_local_scene", "");
            session_node.Set("pending_local_room", "");
            // 回大地图就整片刷新：位置也不再保留（下次进图从入口开始）
            session_node.Set("local_position", Vector2.Zero);
            session_node.Set("local_position_scene", "");
        }

        // **不要**把回程落点改写成地标坐标：设计 02 是「返回大地图原位置」。
        if (_callable_valid(return_handler))
        {
            return_handler.Call();
            return;
        }

        _change_scene(OVERWORLD_RUN);
    }

    private void _remember_world_position(GodotObject session_node)
    {
        Resource local_row = db.get_row("map_local", scene_id);
        string node_id = local_row != null ? local_row.Get("parent_node").AsString() : "";
        Resource region = db.get_row("map_region", node_id);
        if (region != null)
        {
            // 走会话层的写入口：会话内存与存档里的 world_pos（v13）一起更新
            session_node.Call("set_world_position",
                new Vector2((float)region.Get("pos_x").AsDouble(), (float)region.Get("pos_y").AsDouble()),
                current_state());
        }
    }

    public override void _Process(double delta)
    {
        _contact_grace = Mathf.Max(0.0f, _contact_grace - (float)delta);
        _field_refresh_timer -= (float)delta;
        if (_field_refresh_timer <= 0.0f)
        {
            _field_refresh_timer = 1.0f;
            _refresh_field_buffs();
            // 备货这类派生旗标一秒算一次：升级／买东西／打完仗回来都会让条件变真
            GuideService.refresh_derived_flags(db, current_state());
        }

        if (camera != null && player != null)
        {
            camera.GlobalPosition = player.GlobalPosition;
        }

        // 条件地表与遮挡层：跟背包／走位走（两种都可能在这张图里当场变化——开箱拿到藏宝图、钻进岩檐）
        _apply_conditional_layer();
        _refresh_overlay_cover();
        _track_room();
        _track_sneak();
        if (!_has_exit && player != null)
        {
            Node2D exit_marker = world.GetNodeOrNull<Node2D>($"Markers/Exit_{scene_id}");
            if (exit_marker != null)
            {
                _exit_position = exit_marker.Position;
                _has_exit = true;
            }
        }

        if (_has_exit && player != null)
        {
            float exit_distance = player.Position.DistanceTo(_exit_position);
            if (!_exit_armed)
            {
                // 出生点就压在出口上是合法摆法（清风驿的城门）：先走出去一次，出口才开始算数
                if (exit_distance > EXIT_DISTANCE + EXIT_ARM_MARGIN)
                {
                    _exit_armed = true;
                }
            }
            else if (exit_distance <= EXIT_DISTANCE)
            {
                leave_to_overworld();
            }
        }
    }

    /// <summary>记录玩家进过哪些房间（完成度里的「隐藏房间进入数」用它，永久写进存档）。</summary>
    public void _track_room()
    {
        if (player == null)
        {
            return;
        }

        string room_id = current_room_id();
        if (room_id.Length == 0 || room_id == _last_room_id)
        {
            return;
        }

        _last_room_id = room_id;
        // 换房间就把操作提示刷回来：「潜行中…」「刚从遭遇里脱身…」这类临时文案不该一直盖着交互提示。
        _refresh_status();
        // 走进房间时把「只能在场景里判」的引导旗标点亮（0.22.0：首次进入黑风寨前寨）
        GuideService.note_room_entered(db, current_state(), scene_id, room_id);
        // 剧情节点也可能挂在"走进某个房间"上（条件旗标是别的系统点的）
        _claim_story_nodes();
        if (!has_completion())
        {
            return;
        }

        GodotObject state = current_state();
        if (state == null)
        {
            return;
        }

        Resource row = _room_row(room_id);
        // 表里没有 room_type=hidden：隐藏房间就是 branch_group = hidden（与 DungeonService 同一口径）
        if (row != null && row.Get("branch_group").AsString() == "hidden")
        {
            if (state.Call("record_dungeon", scene_id, "rooms_entered", room_id).AsBool())
            {
                _refresh_progress();
            }
        }
    }

    /// <summary>玩家当前所在的房间：按 Rooms/&lt;room_id&gt;/bounds 的矩形判位置。</summary>
    public string current_room_id()
    {
        if (world == null || player == null)
        {
            return "";
        }

        Node box = world.GetNodeOrNull("Rooms");
        if (box == null)
        {
            return "";
        }

        foreach (Node child in box.GetChildren())
        {
            if (child is not Node2D node2d || !child.Name.ToString().StartsWith("Room_"))
            {
                continue;
            }

            var bounds = node2d.GetNodeOrNull<Area2D>("bounds");
            if (bounds == null)
            {
                continue;
            }

            Rect2 rect = _bounds_rect(bounds);
            if (rect.HasPoint(player.GlobalPosition))
            {
                return child.Name.ToString().Substring("Room_".Length);
            }
        }

        return "";
    }

    public Rect2 _bounds_rect(Area2D bounds)
    {
        foreach (Node child in bounds.GetChildren())
        {
            if (child is not CollisionShape2D shape_node || shape_node.Shape == null)
            {
                continue;
            }

            if (shape_node.Shape is not RectangleShape2D rect_shape)
            {
                continue;
            }

            Vector2 center = shape_node.GlobalPosition;
            return new Rect2(center - rect_shape.Size * 0.5f, rect_shape.Size);
        }

        return new Rect2();
    }

    // ------------------------------------------------------------------ 工具

    private Resource _room_row(string room_id)
    {
        foreach (Variant row_variant in db.rows("dungeon_room"))
        {
            Resource row = row_variant.As<Resource>();
            if (row.Get("room_id").AsString() == room_id)
            {
                return row;
            }
        }

        return null;
    }

    private Array _party()
    {
        GodotObject state = current_state();
        if (state == null)
        {
            return new Array();
        }

        return PartyBuilder.build_actors(db, state, new Array());
    }

    private string _difficulty()
    {
        GodotObject state = current_state();
        return state != null ? state.Get("difficulty_id").AsString() : "normal";
    }

    private RngService _rng() => RngService.create_random();

    public string _drops_summary(Dictionary applied, Array drops)
    {
        if (drops.Count == 0)
        {
            return "空的";
        }

        var parts = new System.Collections.Generic.List<string>();
        if (applied.ContainsKey("items"))
        {
            foreach (Variant entry_variant in applied["items"].AsGodotArray())
            {
                Dictionary entry = entry_variant.AsGodotDictionary();
                parts.Add($"{item_name(entry["item_id"].AsString())} ×{entry["qty"].AsInt32()}");
            }
        }

        if (applied.ContainsKey("equipment"))
        {
            foreach (Variant instance_id in applied["equipment"].AsGodotArray())
            {
                parts.Add(item_name(instance_id.AsString()));
            }
        }

        if (applied.ContainsKey("money") && applied["money"].AsInt32() > 0)
        {
            parts.Add($"铜钱 {applied["money"].AsInt32()}");
        }

        // 背包满时如实写「没捡起」：以前这里不看 overflow，满包开箱会显示「空的」，
        // 玩家以为箱子里没东西（其实是放不下）——玩家可见的文案不许说假话。
        var lost = new System.Collections.Generic.List<string>();
        if (applied.ContainsKey("overflow"))
        {
            foreach (Variant entry_variant in applied["overflow"].AsGodotArray())
            {
                Dictionary entry = entry_variant.AsGodotDictionary();
                lost.Add($"{item_name(entry["item_id"].AsString())} ×{entry["qty"].AsInt32()}");
            }
        }

        if (lost.Count > 0)
        {
            parts.Add($"{string.Join("、", lost)} 没捡起（背包满）");
        }

        if (parts.Count == 0)
        {
            return "背包放不下，什么都没捡起";
        }

        return string.Join("、", parts);
    }

    private void _build_status_label()
    {
        _status = new Label { Name = "Status", Position = new Vector2(12, 8) };
        _status.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.92f));
        _status.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _status.AddThemeConstantOverride("shadow_offset_x", 1);
        _status.AddThemeConstantOverride("shadow_offset_y", 1);
        _hud_layer().AddChild(_status);
        // 第三行（状态栏 y=8、完成度进度 y=34 各占一行）——放 30 会和 Progress 叠在一起
        _field_label = FieldBuffHud.build_label(new Vector2(12, 56), 12);
        _hud_layer().AddChild(_field_label);
    }

    /// <summary>
    /// 开局引导（设计 09 §3.1）：HUD 常驻一行「当前目标」，旗标点亮就自动推进。
    /// 位置排在状态栏 8／完成度 34／增益 56 之后的第四行（80），自检里有一条不叠字的断言盯着。
    /// </summary>
    private void _build_guide_label()
    {
        _guide_label = new Label { Name = "Guide", Position = new Vector2(12, 80) };
        _guide_label.AddThemeColorOverride("font_color", Color.FromHtml("8fe3ff"));
        _guide_label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _guide_label.AddThemeConstantOverride("shadow_offset_x", 1);
        _guide_label.AddThemeConstantOverride("shadow_offset_y", 1);
        _hud_layer().AddChild(_guide_label);
        _refresh_guide();
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
    /// 引导 HUD 现在**应该**显示的那句（表里「条件已满足的最靠后一行」的文案）。自检拿它当期望值
    /// ——这样步号与分母都跟着表走，扩表不会再假红。
    /// </summary>
    private string _expected_guide_text()
    {
        GodotObject state = current_state();
        string text = "";
        foreach (Variant row_variant in GuideService.steps(db))
        {
            Resource row = row_variant.As<Resource>();
            if (GuideService.condition_met(state, row.Get("condition").AsString()))
            {
                text = row.Get("text_cn").AsString();
            }
        }

        return text;
    }

    /// <summary>从 HUD 那行里取步号（`当前目标：…（3/6）` → 3）。取不到给 -1。</summary>
    private static int _guide_step_index(string line)
    {
        int start = line.LastIndexOf('（');
        if (start < 0)
        {
            return -1;
        }

        string tail = line.Substring(start + 1);
        int slash = tail.IndexOf('/');
        if (slash <= 0)
        {
            return -1;
        }

        string digits = tail.Substring(0, slash);
        return digits.IsValidInt() ? digits.ToInt() : -1;
    }

    /// <summary>战斗外增益（08）：城镇／副本里也看得见，剩余分钟随时间走（每秒刷一次够用）。</summary>
    public void _refresh_field_buffs()
    {
        if (_field_label == null)
        {
            return;
        }

        GodotObject session_node = _session_node();
        // -1 = 用当前时间（C# 的默认参数对 `Call` 不生效，时钟要显式传）
        Array rows = session_node != null ? session_node.Call("active_field_buffs", -1).AsGodotArray() : new Array();
        _field_label.Text = FieldBuffHud.text_of(db, rows);
    }

    /// <summary>HUD 必须挂在 CanvasLayer 上：挂在世界节点上的话会跟着相机跑出屏幕。</summary>
    private CanvasLayer _hud_layer()
    {
        if (_hud != null && IsInstanceValid(_hud))
        {
            return _hud;
        }

        _hud = new CanvasLayer { Name = "HUD" };
        AddChild(_hud);
        return _hud;
    }

    public void _refresh_status()
    {
        if (_status == null)
        {
            return;
        }

        Resource row = db.get_row("map_local", scene_id);
        // 线索 K 与存档 F5 都要写出来：设计 03 要求「线索必须能被找到」；手动存档按设计 02 只在城镇生效。
        string floor_text = _floor_label();
        string hint = $"{(row != null ? row.Get("name_cn").AsString() : scene_id)}"
            + $"{(floor_text.Length > 0 ? "　·　" : "")}{floor_text}"
            + "　交互 E　看人 Q　角色 Tab　行囊 I　线索 K　存档 F5（城镇）　回大地图 Esc";
        // 推荐等级（map_local.level_range）：进图先知道这儿打不打得过，和明雷威胁色是一个用途
        if (row != null && row.Get("level_range").AsString().Length > 0)
        {
            hint += $"　推荐等级 {row.Get("level_range").AsString()}";
        }

        if (has_completion())
        {
            hint += "　完成度 M　扫荡 J";
        }

        _status.Text = hint;
        _refresh_field_buffs();
    }

    /// <summary>「第 N 层 房间名」——副本是三层同一张图 19 间房，不给这个玩家不知道自己走到哪了。</summary>
    public string _floor_label()
    {
        string room_id = current_room_id();
        if (room_id.Length == 0)
        {
            return "";
        }

        Resource row = db.get_row("dungeon_room", room_id);
        if (row == null)
        {
            return "";
        }

        string room_name = row.Get("room_name").AsString();
        if (room_name.Length == 0)
        {
            room_name = room_id;
        }

        return $"第 {row.Get("floor").AsInt32()} 层 {room_name}";
    }

    /// <summary>这张图要不要显示完成度（map_local.has_completion）。</summary>
    public bool has_completion()
    {
        Resource row = db.get_row("map_local", scene_id);
        return row != null && row.Get("has_completion").AsBool();
    }

    public GodotObject dungeon_service()
    {
        _dungeon ??= DungeonService.create(db, current_state(), _rng());
        return _dungeon;
    }

    private void _build_progress_label()
    {
        if (!has_completion())
        {
            return;
        }

        _progress_label = new Label { Name = "Progress", Position = new Vector2(12, 34) };
        _progress_label.AddThemeColorOverride("font_color", Color.FromHtml("ffd24a"));
        _progress_label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _progress_label.AddThemeConstantOverride("shadow_offset_x", 1);
        _progress_label.AddThemeConstantOverride("shadow_offset_y", 1);
        _hud_layer().AddChild(_progress_label);
        _refresh_progress();
    }

    /// <summary>完成度 HUD 文案（含「本层可扫荡」提示）。</summary>
    public string progress_text() => _progress_label != null ? _progress_label.Text : "";

    public void _refresh_progress()
    {
        if (_progress_label == null)
        {
            return;
        }

        string text = dungeon_service().Call("progress_text", scene_id).AsString();
        Array sweepable = dungeon_service().Call("sweepable_floors", scene_id).AsGodotArray();
        if (sweepable.Count > 0)
        {
            var floors_text = new System.Collections.Generic.List<string>();
            foreach (Variant floor in sweepable)
            {
                floors_text.Add($"第 {floor.AsInt32()} 层");
            }

            text += $"　｜　可扫荡：{string.Join("、", floors_text)}（按 J）";
        }

        _progress_label.Text = text;
    }

    private void _set_status(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
        }
    }

    private TableDb _resolve_db()
    {
        GodotObject game_data = _session_node("GameData");
        if (game_data != null && game_data.Get("db").AsGodotObject() is TableDb data_db
            && data_db.tables.Count > 0)
        {
            return data_db;
        }

        var table_db = new TableDb();
        table_db.load_all();
        return table_db;
    }

    private GodotObject _session_node(string node_name = "GameSession")
    {
        SceneTree tree = _tree();
        if (tree == null)
        {
            return null;
        }

        return tree.Root.GetNodeOrNull(node_name);
    }

    private SceneTree _tree()
    {
        // 不在场景树里时 get_tree() 会打一条 ERROR（用例直接 new 节点就会踩到）
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

    private void _change_scene(string path) => _tree()?.ChangeSceneToFile(path);

    private static bool _has_user_arg(string flag) => System.Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;

    // ------------------------------------------------------------------ 跨语言取值口

    // `copy_guard`／`overlay_stack` 已是 C#（`src/ui/CopyGuard.cs`／`OverlayStack.cs`），
    // 所以这两个缓存是 `Script` 而不是 `GDScript`——用 `GDScript` 去 `Load` 一个 `.cs` 会类型不匹配。
    private static Script _copy_guard_script_cache = null;
    private static Script _overlay_stack_script_cache = null;

    public static Script CopyGuardScript() =>
        _copy_guard_script_cache ??= GD.Load<Script>(COPY_GUARD_SCRIPT);

    private static Script _overlay_stack_script() =>
        _overlay_stack_script_cache ??= GD.Load<Script>(OVERLAY_STACK_SCRIPT);

    private static string _npc_slot_prefix() => NpcService.SLOT_PREFIX;

    // ------------------------------------------------------------------ 自检

    private async void _run_local_selftest()
    {
        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        ok = ok && world != null && player != null;
        lines.Add($"{scene_id}：房间敌人 {teams.Count} / 宝箱 {chests.Count} / 隐藏触发 {triggers.Count}");

        // 真按键走一段（2026-10-04 补）。这一条以前**没有**，于是「地板瓦片自带碰撞盒 → 一进副本就卡死」
        // 这种事故一路绿灯：下面每个检查都是把玩家**瞬移**过去的，从头到尾没有一帧真的用过输入＋物理。
        // 四个方向各按住 15 个物理帧：**朝该方向的位移**必须真的发生。
        if (player != null)
        {
            bool in_tree = player.IsInsideTree();
            Vector2 spawn = player.GlobalPosition;
            bool walk_ok = in_tree;
            var report = new System.Collections.Generic.List<string>();
            (string action, Vector2 direction)[] probes =
            {
                ("move_right", new Vector2(1, 0)),
                ("move_left", new Vector2(-1, 0)),
                ("move_down", new Vector2(0, 1)),
                ("move_up", new Vector2(0, -1)),
            };
            foreach ((string action, Vector2 direction) in probes)
            {
                player.GlobalPosition = spawn;
                Input.ActionPress(action);
                for (int frame = 0; frame < 15; frame++)
                {
                    await ToSignal(_tree(), SceneTree.SignalName.PhysicsFrame);
                }

                Input.ActionRelease(action);
                float advance = (player.GlobalPosition - spawn).Dot(direction);
                report.Add($"{action.TrimPrefix("move_")} {advance:+0.0;-0.0} px");
                if (advance < 15.0f)
                {
                    walk_ok = false;
                }
            }

            player.GlobalPosition = spawn;
            ok = ok && walk_ok;
            lines.Add($"四方向都能走（各 15 物理帧，朝该方向的位移：{string.Join("；", report)}，在树内={in_tree}）={walk_ok}");
        }

        // 撞房间敌人
        var captured = new Array();
        battle_switch_handler = Callable.From<Variant>(encounter => captured.Add(encounter));
        if (teams.Count > 0)
        {
            var enemy = teams[0].As<RoamingEnemy>();
            player.GlobalPosition = enemy.GlobalPosition + new Vector2(0, 8);
            enemy.reset_latch();
            enemy._check_contact();
            bool hit = captured.Count == 1;
            ok = ok && hit;
            lines.Add($"房间遭遇 ok={hit}（{(captured.Count > 0 ? captured[0].As<Encounter>().headline() : "无")}）");
        }

        // 开宝箱
        if (chests.Count > 0)
        {
            var first_chest = chests[0].As<Chest>();
            player.GlobalPosition = first_chest.GlobalPosition;
            Dictionary opened = interact();
            ok = ok && opened.ContainsKey("ok") && opened["ok"].AsBool();
            lines.Add($"开宝箱 ok={(opened.ContainsKey("ok") && opened["ok"].AsBool())}"
                + $"（{(opened.ContainsKey("summary") ? opened["summary"].AsString() : "")}）");
        }

        // 找一条支持的触发试试（缺道具时应当给出需求提示）
        foreach (Variant point_variant in triggers)
        {
            var point = point_variant.As<TriggerPoint>();
            if (!point.supported())
            {
                continue;
            }

            player.GlobalPosition = point.GlobalPosition;
            Dictionary result = interact();
            lines.Add($"触发 {point.trigger_id} ok={(result.ContainsKey("ok") && result["ok"].AsBool())}（{_status.Text}）");
            break;
        }

        // 战斗外增益 HUD（08）：城镇／副本里也看得见；到点（用注入的 now）这一行就空掉
        GodotObject session_for_hud = _session_node();
        if (session_for_hud != null)
        {
            session_for_hud.Call("clear_field_buffs");
            session_for_hud.Call("add_field_buff", "buff_meditated", 10, "", 1);
            _refresh_field_buffs();
            bool hud_ok = _field_label != null
                && _field_label.Text.Contains("打坐余韵")
                && _field_label.Text.Contains("剩 10 分钟");
            ok = ok && hud_ok;
            lines.Add($"战斗外增益 HUD ok={hud_ok}（{(_field_label != null ? _field_label.Text : "-")}）");
            // 别和上面两行叠字：状态栏 y=8、完成度进度 y=34
            bool no_overlap = _field_label != null && _field_label.Position.Y > _status.Position.Y
                && (_progress_label == null || Mathf.Abs(_field_label.Position.Y - _progress_label.Position.Y) > 0.01f);
            ok = ok && no_overlap;
            lines.Add($"增益行不与状态栏／完成度叠字={no_overlap}（y={(_field_label != null ? _field_label.Position.Y : -1.0f):0}）");
            session_for_hud.Call("active_field_buffs", (int)Time.GetUnixTimeFromSystem() + 11 * 60);
            _refresh_field_buffs();
            bool expired_ok = _field_label != null && _field_label.Text.Length == 0;
            ok = ok && expired_ok;
            lines.Add($"过期后 HUD 为空={expired_ok}");

            // 战败回城要用「最近到过的城镇」：这张图是小地图，只有城镇会登记
            Resource row_here = db.get_row("map_local", scene_id);
            if (row_here != null && row_here.Get("scene_type").AsString() == "town")
            {
                bool shelter_ok = session_for_hud.Get("last_shelter_scene").AsString() == scene_id;
                ok = ok && shelter_ok;
                lines.Add($"城镇已登记为战败回城点={shelter_ok}（{session_for_hud.Get("last_shelter_name").AsString()}）");
            }

            // 开局引导 HUD（09 §3.1）：这一行必须与表里「条件已满足的最靠后一行」**一致**。
            // **不写死步号**：步数会随设计扩表，而且副本自检一进来就把玩家摆在寨门 → 「进寨」旗标已点亮。
            _refresh_guide();
            string guide_line = guide_text();
            string expect_line = _expected_guide_text();
            bool guide_ok = guide_line.Contains("当前目标") && guide_line.Contains(expect_line);
            ok = ok && guide_ok;
            lines.Add($"引导 HUD ok={guide_ok}（{guide_line}）");
            bool guide_no_overlap = _guide_label != null && _guide_label.Position.Y > _status.Position.Y
                && (_progress_label == null || _guide_label.Position.Y > _progress_label.Position.Y)
                && (_field_label == null || _guide_label.Position.Y > _field_label.Position.Y);
            ok = ok && guide_no_overlap;
            lines.Add($"引导行不与上面三行叠字={guide_no_overlap}（y={(_guide_label != null ? _guide_label.Position.Y : -1.0f):0}）");
            // 旗标点亮后这一行要跟着推进（09 §3.1「完成自动推进」）
            current_state().Call("set_flag", FLAG_BOARD_READ, true);
            _refresh_guide();
            string guide_after = guide_text();
            // 推进的判据：① 行的内容仍与表一致 ② 步号**不倒退**（旗标只会往前走）
            bool advanced = guide_after.Contains(_expected_guide_text())
                && _guide_step_index(guide_after) >= _guide_step_index(guide_line);
            ok = ok && advanced;
            lines.Add($"引导会自动推进（点亮告示板旗标）={advanced}（{guide_after}）");
        }

        // 浮层栈（设计 18.1）：商店 → 角色（Tab）→ Esc 回商店 → Esc 关商店。
        bool stack_ok = true;
        open_shop("bld_grocery");
        bool shop_alive = _panel_open(shop_panel);
        stack_ok = stack_ok && overlays().Call("depth").AsInt32() == 1 && shop_alive;
        open_character_overlay(0);
        stack_ok = stack_ok && overlays().Call("depth").AsInt32() == 2 && _panel_open(shop_panel);
        // 已在栈里 → 弹回它（不重复压），并切到指定页签
        open_character_overlay(UiNav.char_tab_of(UiNav.BAG));
        Node char_panel = _overlay_node("character");
        stack_ok = stack_ok && overlays().Call("depth").AsInt32() == 2 && char_panel != null
            && char_panel.Call("current_tab").AsInt32() == UiNav.char_tab_of(UiNav.BAG);
        stack_ok = stack_ok && close_top_overlay() && overlays().Call("depth").AsInt32() == 1
            && _panel_open(shop_panel);
        stack_ok = stack_ok && close_top_overlay() && overlays().Call("is_empty").AsBool()
            && !_panel_open(shop_panel);
        ok = ok && stack_ok;
        lines.Add($"浮层栈（商店→角色→Esc 回商店→Esc 关商店）={stack_ok}");

        // 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
        var copy_hits = CopyGuardScript().Call("id_tokens", this).AsStringArray();
        ok = ok && copy_hits.Length == 0;
        lines.Add(CopyGuardScript().Call("ascii_line", this).AsString());
        if (copy_hits.Length > 0)
        {
            lines.Add($"COPY 命中：{string.Join("；", copy_hits)}");
        }

        // 观察点**看得见**（设计 15 §一「可交互物暖色提亮」，决策 337）：本图的每一条都要有那枚标记。
        bool highlight_ok = flavor_points.Count > 0;
        foreach (Variant point_variant in flavor_points)
        {
            var node = point_variant.AsGodotDictionary()["node"].As<Node2D>();
            GodotObject mark = node.GetNodeOrNull(FlavorMarker.node_name());
            highlight_ok = highlight_ok && FlavorMarker.is_visible_highlight(mark);
        }

        ok = ok && highlight_ok;
        lines.Add($"观察点可见标记 {flavor_points.Count} 个 ok={highlight_ok}");

        // NPC：**按 Q 真的把信息面板压进浮层栈**，而且**失败也要出声**。
        // 这里走**真处理器**（合成一个 `npc_info` 动作喂 `_UnhandledInput`），不是直接调 `open_npc()`。
        if (npcs.Count > 0)
        {
            bool npc_ok = true;
            var probe = npcs[0].As<Node2D>();
            Vector2 camera_before = player.GlobalPosition;
            player.GlobalPosition = probe.GlobalPosition;
            var info_event = new InputEventAction { Action = "npc_info", Pressed = true };
            _UnhandledInput(info_event);
            string slot_id = probe.Name.ToString();
            bool bound = _npc_for_slot(slot_id).Length > 0;
            npc_ok = npc_ok && (!bound || overlays().Call("depth").AsInt32() > 0);
            if (bound)
            {
                npc_ok = npc_ok && _panel_open(npc_panel);
                close_top_overlay();
            }

            // 失败路径：走远一点再按 Q —— 必须给一句可见提示（不许静默 return）
            player.GlobalPosition = camera_before;
            _set_status("");
            var far_event = new InputEventAction { Action = "npc_info", Pressed = true };
            _UnhandledInput(far_event);
            npc_ok = npc_ok && !(_status == null || _status.Text.Length == 0);
            ok = ok && npc_ok;
            lines.Add($"按 Q 看人（位点 {slot_id} 绑到人={bound}；走远后提示「{(_status != null ? _status.Text : "")}」）={npc_ok}");
        }

        foreach (string line in lines)
        {
            GD.Print("  " + line);
        }

        GD.Print($"LOCAL SELF-TEST: {(ok ? "OK" : "FAILED")}");
        _tree()?.Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// 收集观察点位点（`Observe_&lt;point_id&gt;`，与判定位点一样可能挂在房间下）。
    /// 只收**这张图**的行（`scene_id` 命中）；`region_id` 那几条在大地图上，由 `OverworldController` 收。
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
            if (row.Get("scene_id").AsString() != scene_id)
            {
                continue;
            }

            string point_id = row.Get("point_id").AsString();
            foreach (Variant node_variant in _find_markers(
                world.GetNodeOrNull("Markers"), FlavorPointRow.marker_name_of(point_id)))
            {
                var node = node_variant.As<Node2D>();
                // **看得见**：观察点本身只是一根 Marker2D，挂一枚小暖色菱形（决策 337）。
                // 幂等——重复 setup() 不会挂第二枚。
                if (node.GetNodeOrNull(FlavorMarker.node_name()) == null)
                {
                    node.AddChild(new FlavorMarker());
                }

                flavor_points.Add(new Dictionary
                {
                    { "point_id", point_id },
                    { "node", node },
                    { "position", node.GlobalPosition },
                });
            }
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
            float distance = player.GlobalPosition.DistanceTo(entry["position"].AsVector2());
            if (distance <= best_distance)
            {
                best = entry["point_id"].AsString();
                best_distance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// 看一个观察点：**只出一句碎句**——不发奖励、不锁任何路（设计 20 §3.2），也不进线索本与副本完成度。
    /// **0.32.0 补**：读过顺手记一枚 `flag_obs_&lt;point_id&gt;`。
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

    /// <summary>走一次事件判定（E 交互）：判定 → 奖励／失败说明，结果写存档。</summary>
    public Dictionary resolve_event(string check_id)
    {
        Dictionary result = event_service().resolve(check_id);
        _set_status(result["text"].AsString());
        autosave("事件判定");
        Dictionary reward = result.ContainsKey("reward") ? result["reward"].AsGodotDictionary() : new Dictionary();
        if (result["success"].AsBool() && reward.ContainsKey("type") && reward["type"].AsString() == "room")
        {
            // 小地图里的「找到通往某房间的路」直接把人带过去
            Node2D room = world.GetNodeOrNull<Node2D>(
                $"Rooms/Room_{(reward.ContainsKey("id") ? reward["id"].AsString() : "")}");
            if (room != null && player != null)
            {
                player.GlobalPosition = room.Position;
                _set_status($"{result["text"].AsString()}（已带你过去）");
            }
        }
        else if (result["success"].AsBool() && reward.ContainsKey("start_battle") && reward["start_battle"].AsBool())
        {
            // `reward_type=boss`：判定通过 → 当场开战。语义与本文件的 `_trigger_item`（隐藏 Boss）一致。
            Encounter boss_encounter = _boss_encounter(
                reward.ContainsKey("id") ? reward["id"].AsString() : "", check_id);
            if (boss_encounter != null)
            {
                _start_battle(boss_encounter);
            }
        }
        else if (result["success"].AsBool())
        {
            // 判定过了接一段对话（设计 20 §四 幕四）：配在 `EVENT_DIALOGUES` 里的才有。
            _open_event_dialogue(check_id);
        }

        return result;
    }

    /// <summary>找出场景里所有 Trigger_ 位点（含挂在各房间下的）。</summary>
    private Array _find_all_trigger_markers()
    {
        var markers = world.GetNodeOrNull<Node2D>("Markers");
        if (markers == null)
        {
            return new Array();
        }

        var result = new Array();
        _collect_trigger_markers(markers, result);
        return result;
    }

    private void _collect_trigger_markers(Node node, Array result)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Node2D && child.Name.ToString().StartsWith("Trigger_"))
            {
                result.Add(child);
            }

            _collect_trigger_markers(child, result);
        }
    }

    /// <summary>
    /// 收集城镇 NPC 站位（`Characters/npc_slot_0N`）。命名**两种都收**（07 §九 第 12 条）：
    /// 占位 `npc_slot_0N` 与按 id 绑的 `npc_&lt;npc_id&gt;`。
    /// </summary>
    private void _collect_npc_slots()
    {
        npcs = new Array();
        Node box = world != null ? world.GetNodeOrNull("Characters") : null;
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

    public Node2D ysort_node() => world.GetNodeOrNull<Node2D>("YSort");

    private void _add(Node parent, Node node)
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

    // ------------------------------------------------------------------ 交互

    public override void _UnhandledInput(InputEvent @event)
    {
        // 浮层栈（设计 18.1）：**全局快捷键在任何浮层里都可用**——所以这里先认快捷键与 Esc，
        // 只有它们都不吃这一下时，才轮到场景层的交互。
        if (@event.IsActionPressed("ui_cancel"))
        {
            // Esc 弹一层；栈空才回场景（回大地图）
            if (close_top_overlay())
            {
                return;
            }

            leave_to_overworld();
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

        if (@event.IsActionPressed("show_progress"))
        {
            open_dungeon_panel();
            return;
        }

        if (@event.IsActionPressed("show_clues"))
        {
            open_clues();
            return;
        }

        // 看 NPC 信息（设计 0.28.0 的 Q62）：**E 是交互菜单，Q 才是只读信息**。
        if (@event.IsActionPressed("npc_info"))
        {
            Node2D who = npc_near_player();
            // **失败也要出声**（2026-10-04 实测反馈：玩家按 Q 觉得"没反应"）。
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

        // 下面这些是**场景层**的：有浮层开着就不许透传（E 不该在商店里再触发一次交互）
        if (overlays().Call("depth").AsInt32() > 0)
        {
            return;
        }

        if (@event.IsActionPressed("interact"))
        {
            interact();
        }
        else if (@event.IsActionPressed("sweep"))
        {
            sweep_floor(-1);
        }
        else if (@event.IsActionPressed("save_game"))
        {
            press_save();
        }
    }

    // ------------------------------------------------------------------ 浮层栈

    public GodotObject overlays()
    {
        _overlays ??= _overlay_stack_script().Call("new").AsGodotObject();
        return _overlays;
    }

    /// <summary>压栈并关掉「被挤出栈」的那些浮层（弹回已有的 / 挤掉栈底）。</summary>
    private Array _push_overlay(string id)
    {
        Array closed = overlays().Call("push", id).AsGodotArray();
        foreach (Variant closed_variant in closed)
        {
            _free_overlay(closed_variant.AsString());
        }

        return closed;
    }

    /// <summary>Esc：弹掉栈顶那一层。栈空返回 false（由调用方做场景层的事）。</summary>
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
    /// 底栏七入口的跳转（`docs/dev/UI示意落地清单.md` §一）：四个页签入口在角色面板那一侧就切了，
    /// 落到这儿的是**跨浮层**的三个——地图（离开这一摞浮层，回场景）、纪事、系统（那一屏还没做，
    /// 底栏上是置灰的，见 `UiNav.disabled_reason`）。
    /// </summary>
    public void nav_to(string id)
    {
        if (UiNav.is_back(id))
        {
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

    /// <summary>程序化关一个浮层（界面自己的「返回」按钮走这里）。</summary>
    private void _close_overlay(string id)
    {
        // 关掉它时压在它上面的也要跟着走，否则栈里会留下已经不在屏幕上的幽灵
        foreach (Variant extra_variant in overlays().Call("remove", id).AsGodotArray())
        {
            _free_overlay(extra_variant.AsString());
        }

        _free_overlay(id);
        // **关掉浮层后重算一次引导**（2026-10-04，决策 347）：浮层里的动作会改状态——最典型的是
        // 「在店里买齐回血道具」。挂在关浮层这一下，正好落在「刚做完那件事」的时刻。
        GuideService.refresh_derived_flags(db, current_state());
        _refresh_guide();
    }

    /// <summary>按 id 释放界面节点。**只碰界面**，不碰栈——栈的账在调用方那几行里。</summary>
    private void _free_overlay(string id)
    {
        switch (id)
        {
            case "shop":
                if (_panel_open(shop_panel))
                {
                    shop_panel.QueueFree();
                }

                shop_panel = null;
                break;
            case "cultivate":
                if (_panel_open(cultivate_panel))
                {
                    cultivate_panel.QueueFree();
                }

                cultivate_panel = null;
                break;
            case "clue":
                if (_panel_open(clue_panel))
                {
                    clue_panel.QueueFree();
                }

                clue_panel = null;
                break;
            case "dungeon":
                if (_panel_open(dungeon_panel))
                {
                    dungeon_panel.QueueFree();
                }

                dungeon_panel = null;
                break;
            case "character":
                Node panel = _overlay_node("character");
                panel?.QueueFree();
                break;
            case "npc":
                if (_panel_open(npc_panel))
                {
                    npc_panel.QueueFree();
                }

                npc_panel = null;
                break;
        }
    }

    /// <summary>浮层节点（角色面板不在专用字段里，按节点名找）。</summary>
    private Node _overlay_node(string id) =>
        id == "character" ? FindChild("CharacterPanel", true, false) : null;

    /// <summary>
    /// 角色与行囊（设计 18.1：`Tab` 开角色、`I` 直达行囊页签）。
    /// 它本身是**浮层**（压在场景上），不再是切场景。
    /// </summary>
    public Dictionary open_character_overlay(int tab_index = 0)
    {
        if (overlays().Call("has", "character").AsBool())
        {
            // 已在栈里 → 弹回它 + **重读状态**（设计 18.1：不复用上次快照）
            _push_overlay("character");
            Node existing = _overlay_node("character");
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
        // Q67 拍板 ④：角色面板点同伴 → 开交往屏。走的就是场景层**现成那条** `open_npc()`。
        panel.Set("npc_open_handler", Callable.From<string>(npc_id => open_npc(npc_id)));
        // 底栏七入口（UI 清单 §一）：四个页签入口面板自己切，跨浮层的三个回到这里；
        // 地名走小地图现成那条「第 N 层 房间名」（HUD 用的是同一处口径）。
        panel.Set("nav_handler", Callable.From<string>(nav_id => nav_to(nav_id)));
        panel.Set("place_label", _floor_label());
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        panel.Call("select_tab", tab_index);
        _push_overlay("character");
        return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", false } };
    }

    /// <summary>开完成度界面（M）：四项完成度 + 楼层列表，已通关的层可以在界面里直接扫荡。</summary>
    public Dictionary open_dungeon_panel()
    {
        if (!has_completion())
        {
            _set_status("这里不是副本（城镇没有完成度）");
            return new Dictionary { { "ok", false }, { "error", "not_dungeon" } };
        }

        if (_panel_open(dungeon_panel))
        {
            // 已在栈里 → 弹回它（关掉压在它上面的）并按设计重读状态
            _push_overlay("dungeon");
            dungeon_panel.Set("state_override", current_state());
            dungeon_panel.Call("refresh");
            return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", true } };
        }

        var panel = GD.Load<PackedScene>(DUNGEON_SCENE).Instantiate();
        if (panel == null)
        {
            return new Dictionary { { "ok", false }, { "error", "完成度场景加载失败" } };
        }

        panel.Name = "DungeonPanel";
        panel.Set("state_override", current_state());
        panel.Set("scene_id", scene_id);
        panel.Set("return_handler", Callable.From(close_dungeon_panel));
        // 扫荡还是走小地图控制器（它管会话状态、玩家位置与掉落入包）
        panel.Set("sweep_handler", Callable.From<int, Dictionary>(floor_number => sweep_floor(floor_number)));
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        dungeon_panel = panel;
        _push_overlay("dungeon");
        return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", false } };
    }

    public void close_dungeon_panel() => _close_overlay("dungeon");

    /// <summary>开线索本：本图的隐藏内容与事件判定，每条都带线索来源与完成状态。</summary>
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
        panel.Set("scope", "scene");
        panel.Set("scene_id", scene_id);
        panel.Set("return_handler", Callable.From(close_clues));
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        clue_panel = panel;
        _push_overlay("clue");
        return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", false } };
    }

    public void close_clues() => _close_overlay("clue");

    /// <summary>
    /// 扫荡已通关的层。floor_number 传 -1 表示「玩家所在层，拿不到就挑第一个可扫荡的层」。
    /// 设计：已通关层可扫荡，只结算掉落（不给经验）。
    /// </summary>
    public Dictionary sweep_floor(int floor_number = -1)
    {
        if (!has_completion())
        {
            _set_status("这里不是副本，不能扫荡");
            return new Dictionary { { "ok", false }, { "error", "not_dungeon" } };
        }

        GodotObject service = dungeon_service();
        int target = floor_number;
        if (target < 0)
        {
            string room_id = current_room_id();
            Resource row = _room_row(room_id);
            if (row != null)
            {
                target = row.Get("floor").AsInt32();
            }

            Array sweepable = service.Call("sweepable_floors", scene_id).AsGodotArray();
            if (!sweepable.Contains(target))
            {
                // 站在没通关的层上：退而用第一个能扫的层
                target = sweepable.Count > 0 ? sweepable[0].AsInt32() : -1;
            }
        }

        if (target < 0)
        {
            _set_status("还没有通关的层可以扫荡（先把这一层的房间清干净）");
            return new Dictionary { { "ok", false }, { "error", "no_sweepable_floor" } };
        }

        Dictionary result = service.Call("sweep", scene_id, target).AsGodotDictionary();
        if (result["ok"].AsBool())
        {
            Dictionary applied = result["applied"].AsGodotDictionary();
            _set_status($"扫荡第 {target} 层：经验不加（仅掉落）　{result["summary"].AsString()}　"
                + $"物品 {applied["items"].AsGodotArray().Count} 种　装备 {applied["equipment"].AsGodotArray().Count} 件");
        }
        else
        {
            _set_status($"扫荡失败：{result["error"].AsString()}");
        }

        if (result["ok"].AsBool())
        {
            autosave($"扫荡第 {target} 层");
        }

        return result;
    }

    /// <summary>交互：宝箱 &gt; 触发点 &gt; 事件判定 &gt; 店铺 &gt; 无。</summary>
    public Dictionary interact()
    {
        // 剧情招募（09 §3.2）在**最前面**：条件旗标满足且人在这张图时，按 E 先把同伴收进来。
        // 城镇走「客栈那一次」：设计原文是「看过告示板后，在清风驿客栈对话」，所以城镇不在任意 E 上收人。
        if (!_is_town())
        {
            Array recruited = _join_pending_recruits(true);
            if (recruited.Count > 0)
            {
                return new Dictionary { { "ok", true }, { "recruited", recruited } };
            }
        }

        foreach (Variant chest_variant in chests)
        {
            var chest = chest_variant.As<Chest>();
            if (chest.can_interact())
            {
                return _try_open_chest(chest);
            }
        }

        foreach (Variant point_variant in triggers)
        {
            var point = point_variant.As<TriggerPoint>();
            if (point.can_interact())
            {
                return _activate_trigger(point);
            }
        }

        string check_id = event_near_player();
        if (check_id.Length > 0)
        {
            return resolve_event(check_id);
        }

        string building_id = building_near_player();
        if (building_id.Length > 0)
        {
            // service 类建筑各有各的入口：医馆的治疗在店铺界面里；木桩（09 §3.3）直接进练习战
            if (PracticeService.is_practice_building(db, building_id))
            {
                return start_practice(building_id);
            }

            return open_shop(building_id);
        }

        string facility = facility_near_player();
        if (facility == "facility_inn")
        {
            // 设计 09 §3.2：城镇里的同伴是在**客栈**这一次交互上入队的（现例：燕小七）。
            Array joined_at_inn = _join_pending_recruits(true);
            if (joined_at_inn.Count > 0)
            {
                // 设计 20 §四 幕一「客栈里的绿林客」：这一次对话就是她的招募对话。
                string speaker = _first_joined_with_dialogue(joined_at_inn);
                if (speaker.Length > 0)
                {
                    open_npc(speaker);
                }

                return new Dictionary { { "ok", true }, { "recruited", joined_at_inn } };
            }

            return open_cultivate();
        }

        // 当铺与悬赏板在 07 文档里是「无表的信息源」：按 E 读一段对话/告示，不开功能界面。
        if (facility == "facility_pawnshop")
        {
            return _read_facility_notice(facility, "当铺老板", "寨里关着个疯子，别去招惹。");
        }

        if (facility == "facility_bounty_board")
        {
            // 09 §3.1 的第一步就是「去清风驿的告示板看看」：读它＝看过告示板 → 点亮引导旗标。
            GodotObject state = current_state();
            if (state != null && !state.Call("has_flag", FLAG_BOARD_READ).AsBool())
            {
                state.Call("set_flag", FLAG_BOARD_READ, true);
            }

            _refresh_guide();
            Dictionary posted = _read_facility_notice(
                facility, "悬赏板", "沈家小姐在驿外被掳，黑风寨脱不了干系。告示只指了方向，没有坐标。");
            // 序幕·择念（20 §3.1／0.29.1 v2 第 4 条）：**与引子同框**。
            if (_open_story_dialogue(_opening_choice_node.AsString()))
            {
                posted["story"] = _opening_choice_node;
            }

            return posted;
        }

        if (facility == "facility_bookshop")
        {
            // 07 §4.1（0.31.0 加）：书铺是**无表信息点**，21 §九 陆文昭的「本命机遇·书箱底」落在这里。
            Dictionary notice = _read_facility_notice(
                facility, "书铺掌柜", "架上的旧书摞到房梁，抄书的纸笔摊了一桌；书架底下还塞着只旧书箱。");
            // **这一按顺手再判一次剧情节点**：机遇的条件是「看过悬赏板 ＋ 主角是书生」，
            // 而那条旗标就是在**同一个城镇里**读悬赏板时点亮的。
            _claim_story_nodes();
            return notice;
        }

        if (facility.Length > 0)
        {
            // 07 §4.1 的无表设施都在上面各有一支；认不出来的位点 = 地图／数据错。
            // **别把位点名（facility_xxx）印给玩家**：出声到日志，界面上说人话。
            var known = new System.Collections.Generic.List<string>();
            foreach (Variant key in facility_labels().Keys)
            {
                known.Add(key.AsString());
            }

            GD.PushError($"[LocalMap] 认不出的设施位点：{facility}（07 只定义了 {string.Join("、", known)}）");
            _set_status("这里还没有可交互的内容（数据错，已记进日志）");
            return new Dictionary { { "ok", false }, { "error", "facility_unsupported" }, { "facility", facility } };
        }

        // NPC 站位放在**最后**：站在店门口的路人不该把「进店」抢掉（店铺/设施优先）
        Node2D npc = npc_near_player();
        if (npc != null)
        {
            return _talk_to_npc(npc);
        }

        // 观察点排在 NPC 之后：它是「看一眼」的物，不该抢掉「跟人说话」。
        string point_id = flavor_near_player();
        if (point_id.Length > 0)
        {
            return read_flavor_point(point_id);
        }

        // 别说「走 Exit 位点」——那是条看不见的坐标；玩家能按的键写清楚
        _set_status("这里没有可交互的东西（按 Esc 回大地图）");
        return new Dictionary { { "ok", false }, { "error", "无可交互目标" } };
    }

    /// <summary>这次刚入队的人里，谁配了对话（设计 20 §四 的幕一就是「客栈里那一次」）。</summary>
    private string _first_joined_with_dialogue(Array results)
    {
        foreach (Variant entry_variant in results)
        {
            Dictionary entry = entry_variant.AsGodotDictionary();
            if (!entry.ContainsKey("ok") || !entry["ok"].AsBool())
            {
                continue;
            }

            string char_id = entry.ContainsKey("char_id") ? entry["char_id"].AsString() : "";
            if (DialogueService.has_dialogue(db, char_id))
            {
                return char_id;
            }
        }

        return "";
    }

    /// <summary>无表设施的信息点：把设计文档里的那两句对话/告示落到状态栏。</summary>
    private Dictionary _read_facility_notice(string facility_id, string speaker, string text)
    {
        _set_status($"{speaker}：{text}");
        return new Dictionary
        {
            { "ok", true }, { "facility", facility_id }, { "speaker", speaker }, { "text", text },
        };
    }

    /// <summary>
    /// NPC 站位交互：内容还没到，但**不静默**——玩家按了 E 要有一句如实的回应。
    /// 位点与人的对应：**按 id 绑的 `npc_&lt;npc_id&gt;` 优先，占位命名 `npc_slot_0N` 按编号顺序**
    /// （判定只有一处：`NpcService.npc_for_slot`）。
    /// </summary>
    public Dictionary _talk_to_npc(Node2D slot)
    {
        string slot_id = slot.Name.ToString();
        string npc_id = _npc_for_slot(slot_id);
        if (npc_id.Length == 0)
        {
            // **别把表名甩给玩家**：表名只进日志，玩家看一句人话（决策 329）。
            GD.PushError($"[local_map] NPC 站位 {slot_id} 绑不到人（按 id 认不出来／按编号顺序越界；"
                + $"本场景只有 {npcs_at_count()} 个人）");
            const string text = "眼下没什么可说的（这个人还没配台词）";
            _set_status($"{NPC_SPEAKER}：{text}");
            return new Dictionary
            {
                { "ok", true }, { "npc", slot_id }, { "speaker", NPC_SPEAKER }, { "text", text },
            };
        }

        Dictionary opened = open_npc(npc_id);
        Resource def = NpcService.def_of(db, npc_id);
        return new Dictionary
        {
            { "ok", opened.ContainsKey("ok") && opened["ok"].AsBool() },
            { "npc", slot_id },
            { "npc_id", npc_id },
            { "speaker", def != null ? def.Get("name_cn").AsString() : npc_id },
            { "text", def != null ? def.Get("greet_text_cn").AsString() : "" },
        };
    }

    private int npcs_at_count() => NpcService.npcs_at(db, scene_id).Count;

    /// <summary>
    /// 位点 → 人：**两种命名都认**，判定只有一处（`NpcService.npc_for_slot`）。
    /// </summary>
    private string _npc_for_slot(string slot_id) =>
        NpcService.npc_for_slot(db, slot_id, scene_id);

    /// <summary>
    /// 开 NPC 交往面板（设计 19 §三）：信息在最上面，交互项在下面。
    /// `mode`：E 打开的是交互菜单、Q 打开的是只读信息（设计 0.28.0 的 Q62）。
    /// `entry_node_id`：**直接从某一句说起**（剧情抉择用）。
    /// </summary>
    public Dictionary open_npc(string npc_id, string mode = "interact", string entry_node_id = "")
    {
        if (_panel_open(npc_panel))
        {
            _push_overlay("npc");
            npc_panel.Set("state_override", current_state());
            npc_panel.Set("npc_id", npc_id);
            npc_panel.Set("mode", mode);
            npc_panel.Set("dialogue_node_id", entry_node_id);
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
        panel.Set("dialogue_node_id", entry_node_id);
        panel.Set("kill_styles", _last_kill_styles());
        panel.Set("return_handler", Callable.From(close_npc));
        panel.Set("spar_handler", Callable.From<string, string>((who, team) => _start_spar(who, team)));
        _overlay_stack_script().Call("mount", this, panel);
        panel.Call("setup");
        npc_panel = panel;
        _push_overlay("npc");
        return new Dictionary { { "ok", true }, { "error", "" }, { "reopened", false } };
    }

    public void close_npc() => _close_overlay("npc");

    /// <summary>
    /// 切磋：组一场「和这个人打」的遭遇交给战斗场景；赢了由战斗结算加好感。
    /// </summary>
    private Dictionary _start_spar(string npc_id, string team_id)
    {
        // **空队伍 id 是有意的**：同伴的切磋打的是「他自己的镜像」（Q88 拍板 ①）。
        Resource team = team_id.Length > 0 ? db.get_row("enemy_team", team_id) : null;
        if (team_id.Length > 0 && team == null)
        {
            // 数据错：id 只进日志，玩家看一句人话（AGENTS：玩家可见文案不许出现表内 id）
            GD.PushError($"[local_map] 切磋队伍不存在：enemy_team 缺少 {team_id}（npc {npc_id} 的 spar_team_id）");
            _set_status("切磋对手的配置对不上（数据错，已记进日志）");
            return new Dictionary { { "ok", false }, { "error", "no_team" } };
        }

        var data = new Dictionary
        {
            { "spawn_id", $"spar_{npc_id}" },
            { "source_scene", scene_id },
            { "source_key", $"spar_{npc_id}" },   // 不写房间名：切磋不进副本完成度
            { "team_id", team_id },
            { "is_elite", false },
        };
        if (team_id.Length == 0)
        {
            data["mirror_char"] = npc_id;
        }

        Encounter encounter = Encounter.build(
            db, data, team, Encounter.contact_front(), _difficulty());
        if (team_id.Length == 0)
        {
            // 战报抬头要有个名字（`Encounter.headline()` 直接用 team_name）；镜像就是那位同伴自己
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
            _set_status("这里不能切磋（没有战斗入口）");
            return new Dictionary { { "ok", false }, { "error", "no_handler" } };
        }

        return new Dictionary { { "ok", true }, { "team_id", team_id }, { "npc_id", npc_id } };
    }

    /// <summary>
    /// 剧情招募（设计 09 §3.2）：`recruit_def` 里条件旗标已点亮、人在这张图、又还没入队的同伴 → 入队。
    /// 三条入口：**进图时**（announce=false）、**按 E 时**（announce=true）、以及城镇里**客栈那一次交互**。
    /// </summary>
    private Array _join_pending_recruits(bool announce)
    {
        Array results = RecruitService.join_all_for_scene(db, current_state(), scene_id);
        if (results.Count == 0)
        {
            return results;
        }

        var names = new System.Collections.Generic.List<string>();
        var notes = new System.Collections.Generic.List<string>();
        var rewards = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in results)
        {
            Dictionary entry = entry_variant.AsGodotDictionary();
            names.Add(entry["name_cn"].AsString());
            if (entry.ContainsKey("note") && entry["note"].AsString().Length > 0)
            {
                notes.Add(entry["note"].AsString());
            }

            // 入队奖励（设计 20 号 §九：四条招募支线各写「入队 ＋ 某物」＋ 好感）——玩家得看见自己拿到了什么。
            if (entry.ContainsKey("rewards"))
            {
                foreach (Variant reward_variant in entry["rewards"].AsGodotArray())
                {
                    rewards.Add(reward_variant.AsString());
                }
            }
        }

        _refresh_guide();
        if (!announce)
        {
            return results;
        }

        string suffix = notes.Count > 0 ? $"（{string.Join("；", notes)}）" : "";
        if (rewards.Count > 0)
        {
            suffix += $"　得到：{string.Join("、", rewards)}";
        }

        // 先存档再写状态：`autosave` 失败时会把状态栏写成「自动存档失败」，
        // 而「谁加入了队伍」是玩家更该看到的那条——所以把存档结果并进同一行，别让它被盖掉。
        Dictionary saved = autosave("剧情招募");
        string text = $"{string.Join("、", names)} 加入了队伍{suffix}";
        if (!saved.ContainsKey("ok") || !saved["ok"].AsBool())
        {
            text += $"　（自动存档失败：{(saved.ContainsKey("error") ? saved["error"].AsString() : "未知原因")}，这段进度只在本局里）";
        }

        _set_status(text);
        return results;
    }

    /// <summary>玩家身边最近的建筑 Marker（`Markers/Buildings/&lt;building_def.building_id&gt;`）。</summary>
    public string building_near_player() =>
        _marker_near_player("Markers/Buildings", id => db.get_row("building_def", id) != null);

    /// <summary>玩家身边最近的设施 Marker（`Markers/Facilities/&lt;facility_id&gt;`），如客栈 facility_inn。</summary>
    public string facility_near_player() => _marker_near_player("Markers/Facilities", _ => true);

    /// <summary>玩家身边最近的 NPC 站位（`Characters/npc_slot_0N`，地编交付的占位）。</summary>
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
            var slot = slot_variant.As<Node2D>();
            if (!IsInstanceValid(slot))
            {
                continue;
            }

            float distance = slot.GlobalPosition.DistanceTo(player.GlobalPosition);
            if (distance <= best_distance)
            {
                best = slot;
                best_distance = distance;
            }
        }

        return best;
    }

    /// <summary>在某个 Markers/&lt;组&gt; 下找离玩家最近的位点（名字即 id），filter 用来筛掉不认识的 id。</summary>
    private string _marker_near_player(string group_path, System.Func<string, bool> filter)
    {
        Node box = world != null ? world.GetNodeOrNull(group_path) : null;
        if (box == null || player == null)
        {
            return "";
        }

        string best = "";
        // 取值口与常量同源（GDScript 读不到 C# 的 const，这里保留方法口径）
        float best_distance = building_distance();
        foreach (Node child in box.GetChildren())
        {
            if (child is not Node2D node2d)
            {
                continue;
            }

            string marker_id = child.Name.ToString();
            if (!filter(marker_id))
            {
                continue;
            }

            float distance = node2d.GlobalPosition.DistanceTo(player.GlobalPosition);
            if (distance <= best_distance)
            {
                best = marker_id;
                best_distance = distance;
            }
        }

        return best;
    }
}
