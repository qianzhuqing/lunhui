using Godot;

namespace Lunhui.Autoload;

/// <summary>
/// 当前这一局的会话状态（C# 版，对位原 `src/autoload/game_session.gd`，153 行）。
///
/// 主菜单新建或读取存档后把 `GameState` 放到这里，游戏场景从它取数据。
/// autoload 名（`GameSession`）与文件里的类同名，按 `project.godot` 的路径注册即可，
/// 不写 `class_name`（原 `.gd` 的注释同理）。
///
/// **成员名一律保持 snake_case 且全部 `[Export]`**：GDScript 侧（`tools/check_loop.gd`、
/// `tools/_shot.gd`、五份用例、几个面板的自检）直接读写这些名字——字段 `Get`／`Set` 得到的就是它。
/// 未搬走的 `GameState` 还是 GDScript，所以 `state` 收成 `GodotObject`、方法走 `Call`（坑 67／89）；
/// 等 `game_state` 搬完再把这两处收成强类型。
/// </summary>
public partial class GameSession : Node
{
    [Export] public GodotObject state = null;

    /// <summary>待处理的明雷遭遇：大地图写好，战斗场景读取。</summary>
    [Export] public Variant pending_encounter = default;

    /// <summary>已清明的明雷：spawn_id → 可重生时间（unix 秒；-1 表示不刷新）。</summary>
    [Export] public Godot.Collections.Dictionary cleared_spawns = new();

    /// <summary>
    /// 大地图上的最后位置（从战斗返回时接着走）。**写它一律走 `set_world_position()`**——
    /// 那个方法顺手把它同步进存档（v13 的 `world_pos`），读档回大地图才落得回原处。
    /// </summary>
    [Export] public Vector2 world_position = Vector2.Zero;

    /// <summary>
    /// 大地图明雷开关的**会话级**覆盖（0.8.1：-1 = 读 `feature_toggle.overworld_roaming_enemy`；
    /// 0／1 = 强制）。放会话里而不是控制器上，因为大地图每次从战斗／小地图回来都会**重建场景**，
    /// 控制器上的字段活不过那一次重建（闭环用例踩过）。
    /// </summary>
    [Export] public int roaming_enabled_override = -1;

    /// <summary>上一场战斗的结果摘要（大地图读给提示）。</summary>
    [Export] public Godot.Collections.Dictionary last_battle = new();

    /// <summary>要进的小地图（大地图点 Portal 时写，小地图控制器读）。</summary>
    [Export] public string pending_local_scene = "";

    /// <summary>进小地图时要落在哪个房间（「铁镐挖通」这类捷径用；空 = 该图的入口房间）。</summary>
    [Export] public string pending_local_room = "";

    /// <summary>战斗结束后回到哪个场景（大地图或小地图）。</summary>
    [Export] public string pending_return_scene = "res://scenes/world_run.tscn";

    /// <summary>小地图里战斗前的位置（打完回图接着站那儿）；配 scene 一起用，防止跨图误用。</summary>
    [Export] public Vector2 local_position = Vector2.Zero;
    [Export] public string local_position_scene = "";

    /// <summary>小地图会话状态：scene_id → {cleared, chests, triggers}（回大地图整片刷新）。</summary>
    [Export] public Godot.Collections.Dictionary local_maps = new();

    /// <summary>
    /// 最近到过的出生点／城镇（08 的战败处理要「全队回到最近到过的出生点或城镇」）。
    /// 由小地图控制器与驿站传送在**进入城镇**时登记；空 = 还没到过城镇，败北退回大地图。
    /// </summary>
    [Export] public string last_shelter_scene = "";
    [Export] public string last_shelter_name = "";

    /// <summary>
    /// 战斗外增益（08：`field` 作用域按**现实时间**计时，进战斗后维持整场）。
    /// 每条 {buff_id, source_id, stacks, expires_at}，`expires_at` 是 unix 秒。
    /// **v12 起这份数据存在 `GameState.field_buffs` 里**（退出/读档后按现实时间继续走——
    /// 只放会话里的话「打坐完退出重进」等于白打坐）。`_field_rows()` 返回的是**当前该写哪儿**：
    /// 有存档就写存档；没有存档（用例／独立场景）才退回这份会话内存。改这份数据一律走
    /// `add_field_buff/active_field_buffs/clear_field_buffs` 三个口，别直接动字段。
    /// </summary>
    [Export] public Godot.Collections.Array field_buffs = new();

    public void set_state(GodotObject new_state)
    {
        state = new_state;
        // 读档带回来的大地图坐标（v13）：落到会话字段上，大地图 `_start_position()` 直接照它摆人。
        // 老档／新档没有这条记录时是 `Vector2.ZERO`，大地图把它当成「用默认出生点」。
        world_position = new_state != null ? new_state.Call("world_position").AsVector2() : Vector2.Zero;
    }

    /// <summary>
    /// 记下大地图坐标：会话内存与存档（v13 `world_pos`）同源写一份。
    /// 设计 09 §二：读档一律回大地图、落点用这个坐标；小地图与副本内的位置不存。
    ///
    /// `fallback` 给「用例／自检注入 `state_override`、会话里没有真 state」的那条路径：
    /// 那种情况下真正会被存档写出去的是注入进来的那份状态，坐标不落进去就等于这条没接。
    /// </summary>
    public void set_world_position(Vector2 pos, Variant fallback)
    {
        world_position = pos;
        GodotObject target = state != null ? state : fallback.AsGodotObject();
        target?.Call("set_world_pos", pos);
    }

    public bool has_state() => state != null;

    /// <summary>记下「最近到过的城镇／出生点」（08 战败处理要用的那一处）。</summary>
    public void note_shelter(string scene_id, string name_cn)
    {
        if (string.IsNullOrEmpty(scene_id))
        {
            return;
        }

        last_shelter_scene = scene_id;
        last_shelter_name = name_cn;
    }

    /// <summary>战斗外增益这份数据「现在住在哪儿」：有存档就住存档（可持久化），没有就住会话内存。</summary>
    private Godot.Collections.Array _field_rows()
        => state != null ? state.Get("field_buffs").AsGodotArray() : field_buffs;

    private void _set_field_rows(Godot.Collections.Array rows)
    {
        if (state != null)
        {
            state.Set("field_buffs", rows);
        }
        else
        {
            field_buffs = rows;
        }
    }

    /// <summary>
    /// 加一层战斗外增益。同 buff 重复获得 = 刷新到期时间（叠层类按 `max_stack` 累加，与 buff_def 口径一致）。
    /// 注意：`max_stack` 由**调用方**从表里取（`BuffService.max_stack_of`）——会话层不读表，
    /// 现在唯一的来源「打坐余韵」是 refresh 类，所以默认 1 就够；将来加叠层类 field buff 时记得传。
    /// </summary>
    public Godot.Collections.Dictionary add_field_buff(string buff_id, int minutes, string source_id, int max_stack)
    {
        if (string.IsNullOrEmpty(buff_id) || minutes <= 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", "战斗外增益要 buff_id 与正数分钟" }, { "buff_id", buff_id },
            };
        }

        int expires_at = (int)Time.GetUnixTimeFromSystem() + minutes * 60;
        Godot.Collections.Array rows = _field_rows();
        for (int i = 0; i < rows.Count; i++)
        {
            Godot.Collections.Dictionary entry = rows[i].AsGodotDictionary();
            if (entry["buff_id"].AsString() != buff_id)
            {
                continue;
            }

            entry["expires_at"] = expires_at;
            int stacks = entry.ContainsKey("stacks") ? entry["stacks"].AsInt32() : 1;
            entry["stacks"] = System.Math.Min(System.Math.Max(1, max_stack), stacks + 1);
            return new Godot.Collections.Dictionary
            {
                { "ok", true }, { "refreshed", true }, { "buff_id", buff_id },
                { "minutes", minutes }, { "stacks", entry["stacks"].AsInt32() },
            };
        }

        rows.Add(new Godot.Collections.Dictionary
        {
            { "buff_id", buff_id }, { "source_id", source_id }, { "stacks", 1 }, { "expires_at", expires_at },
        });
        _set_field_rows(rows);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "refreshed", false }, { "buff_id", buff_id }, { "minutes", minutes }, { "stacks", 1 },
        };
    }

    /// <summary>
    /// 现在还有效的战斗外增益（顺手清掉过期的），每条多带一个 `remaining_sec`。
    /// `now` 可注入：用例不用真等 10 分钟也能验过期；不注入就传 **-1**。
    /// **C# 的默认参数值对 GDScript 不生效**，所以调用点一律写满实参（坑⑲）。
    /// </summary>
    public Godot.Collections.Array active_field_buffs(int now)
    {
        int clock = now >= 0 ? now : (int)Time.GetUnixTimeFromSystem();
        var alive = new Godot.Collections.Array();
        var output = new Godot.Collections.Array();
        Godot.Collections.Array rows = _field_rows();
        for (int i = 0; i < rows.Count; i++)
        {
            Godot.Collections.Dictionary entry = rows[i].AsGodotDictionary();
            int expires_at = entry.ContainsKey("expires_at") ? entry["expires_at"].AsInt32() : 0;
            if (expires_at <= clock)
            {
                continue;
            }

            alive.Add(entry);
            Godot.Collections.Dictionary copy = entry.Duplicate(true);
            copy["remaining_sec"] = expires_at - clock;
            output.Add(copy);
        }

        _set_field_rows(alive);
        return output;
    }

    /// <summary>清掉全部战斗外增益（08：败北要把它们全部清除）。返回清掉几条。</summary>
    public int clear_field_buffs()
    {
        int count = _field_rows().Count;
        _set_field_rows(new Godot.Collections.Array());
        return count;
    }

    public void clear()
    {
        state = null;
        pending_encounter = default;
        cleared_spawns.Clear();
        world_position = Vector2.Zero;
        last_battle.Clear();
        pending_local_scene = "";
        pending_local_room = "";
        pending_return_scene = "res://scenes/world_run.tscn";
        local_position = Vector2.Zero;
        local_position_scene = "";
        local_maps.Clear();
        last_shelter_scene = "";
        last_shelter_name = "";
        field_buffs.Clear();
    }
}
