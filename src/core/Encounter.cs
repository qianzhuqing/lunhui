using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 一次明雷遭遇（C# 版，对位原 `src/core/encounter.gd`）。
///
/// 把 docs/design/02_地图与明雷.md 的「接触结果」表落成数据：接触方式决定战斗开局。
///   正面撞上        正常开战，按身法排序
///   从背后接触      我方全体先手一回合；敌方架势初始值 -30%
///   背后接触沉睡敌人 奇袭：首回合伤害 +50%，敌方无防备
///   进追击警戒圈被发现 敌方先手
///   被追击抓到      敌方先手，且首回合无法逃跑
///
/// 架势、拆招、逃跑都已实现；`pending_rules` 现在只剩「奇袭时敌方首回合无防备」这一条——
/// 设计原文只写了这六个字（02_地图与明雷.md 的接触结果表），没说「无防备」到底改什么
/// （降格挡率？架势清零？还是不吃防御？），所以这里如实记下来、不替设计编效果。
///
/// **成员名保持 snake_case**：还没搬走的 GDScript（`src/core`／`src/world`／`src/ui` 与用例）
/// 里到处是 `encounter.team_name` 这种取值，Godot 的 C# 脚本按成员名暴露属性。
/// </summary>
public partial class Encounter : RefCounted
{
    /// <summary>
    /// 阵营常量**只有一处出处**：`BattleActor.SIDE_*`（这里以前自己又写了一份 0/1——同一套值两处定义，
    /// 两边漂移了不会报错）。`first_side` 与模拟器比的是同一批数字，所以它们必须是同一个事实。
    ///
    /// 2026-10-11：`battle_actor` 已搬成 C#，这里**直接引用它的那两个常量**——
    /// 过渡期那份「`GD.Load&lt;GDScript&gt;` ＋ 读常量表」的写法连同它一起删掉。
    /// </summary>
    private static readonly int SIDE_ALLY = BattleActor.SIDE_ALLY;
    private static readonly int SIDE_ENEMY = BattleActor.SIDE_ENEMY;

    public const string CONTACT_FRONT = "front";
    public const string CONTACT_BACK = "back";
    public const string CONTACT_SLEEP = "ambush_sleep";
    public const string CONTACT_SPOTTED = "spotted";
    public const string CONTACT_CAUGHT = "caught";

    // ---------------------------------------------------------------- 过渡期取值口
    //
    // GDScript 读不到 C# 的 `const`（`Invalid access to property or key`，与
    // `BuffService.POLARITY_*` 同一个坑），而调用面现在直接读 `EncounterScript.CONTACT_FRONT`
    // 这类名字。每个对外常量配一个同名小写静态方法，调用方走 `EncounterScript.contact_front()`；
    // P5 把 GDScript 清零后把它连着调用方一起删掉。

    public static int side_ally() => SIDE_ALLY;
    public static int side_enemy() => SIDE_ENEMY;
    public static string contact_front() => CONTACT_FRONT;
    public static string contact_back() => CONTACT_BACK;
    public static string contact_sleep() => CONTACT_SLEEP;
    public static string contact_spotted() => CONTACT_SPOTTED;
    public static string contact_caught() => CONTACT_CAUGHT;

    // ---------------------------------------------------------------- 一场遭遇

    [Export]
    public string spawn_id { get; set; } = "";

    [Export]
    public string team_id { get; set; } = "";

    [Export]
    public string team_name { get; set; } = "";

    [Export]
    public string region_id { get; set; } = "";

    [Export]
    public string threat_tag { get; set; } = "green";

    [Export]
    public bool is_elite { get; set; }

    [Export]
    public string difficulty_id { get; set; } = "normal";

    /// <summary>这场遭遇来自哪张图：overworld 或某个 scene_id。</summary>
    [Export]
    public string source_scene { get; set; } = "overworld";

    /// <summary>来源标识：明雷用 spawn_id，房间敌人用 room_id。</summary>
    [Export]
    public string source_key { get; set; } = "";

    /// <summary>队伍成员串（"en_wolf:3"）；房间队伍与隐藏 Boss 靠它建战斗单位。</summary>
    [Export]
    public string members { get; set; } = "";

    [Export]
    public string contact { get; set; } = CONTACT_FRONT;

    /// <summary>首回合强制先手方；-1 表示按身法自然排序。</summary>
    [Export]
    public int first_side { get; set; } = -1;

    /// <summary>首回合增伤（奇袭用，取 combat_const.surprise_damage_bonus）。</summary>
    [Export]
    public double surprise_bonus { get; set; }

    private readonly System.Collections.Generic.List<string> _pending_rules = new();

    /// <summary>设计写了、但代码还没法照做的规则说明（目前只剩奇袭的「敌方首回合无防备」，等设计定效果）。</summary>
    [Export]
    public string[] pending_rules
    {
        get => _pending_rules.ToArray();
        set
        {
            _pending_rules.Clear();
            if (value != null)
            {
                _pending_rules.AddRange(value);
            }
        }
    }

    /// <summary>
    /// 练习战（设计 09 §3.3 的城镇木桩）：**收益封顶的开关**——结算时按它跳过掉落／铜钱／首杀／
    /// 击败领悟，并把经验与熟练度卡在 `growth_const.dummy_*`。组这场遭遇的地方是 `PracticeService.build_encounter()`。
    /// </summary>
    [Export]
    public bool practice { get; set; }

    /// <summary>
    /// 切磋（设计 19 §2.2）：赢了要给这位 NPC 加好感，所以遭遇里记下他是谁。
    /// 记在这里而不是会话里：结算发生在战斗场景，那时会话里的临时标记早没了（切过场景）。
    /// 空串 = 普通遭遇。组这场遭遇的地方是 `local_map_controller._start_spar()`。
    /// </summary>
    [Export]
    public string spar_npc { get; set; } = "";

    /// <summary>
    /// 切磋的**对手是这个人自己**（Q88 拍板 ①，2026-10-04）：同伴没有 `enemy_team` 行，
    /// 战斗层按这位同伴的 `character_base` ＋当前等级与配装现造一个镜像（不新增表行、不加列）。
    /// 空串 = 不是镜像切磋（普通队伍走 `team_id`）。组这场遭遇的地方同上。
    /// </summary>
    [Export]
    public string mirror_char { get; set; } = "";

    /// <summary>背袭时敌方架势初始值降低比例（0 表示不降；数值取 combat_const.backstab_poise_reduce）。</summary>
    [Export]
    public double enemy_poise_reduce { get; set; }

    // ---------------------------------------------------------------- 构造

    /// <summary>由明雷刷新点 + 队伍 + 接触方式构造一次遭遇。</summary>
    public static Encounter build(
        TableDb db, Variant spawn_row, Variant team_row, string contact, string difficulty_id)
    {
        var encounter = new Encounter();
        // 表行（Resource）与字典都支持；Dictionary.get 不接受默认值，所以统一走 RowGet
        encounter.spawn_id = Str(RowGet(spawn_row, "spawn_id", ""));
        encounter.region_id = Str(RowGet(spawn_row, "region_id", ""));
        encounter.team_id = Str(RowGet(spawn_row, "team_id", ""));
        encounter.team_name = Str(RowGet(team_row, "name_cn", encounter.team_id));
        encounter.threat_tag = Str(RowGet(team_row, "threat_tag", "green"));
        encounter.is_elite = RowGet(spawn_row, "is_elite", false).AsBool();
        encounter.source_scene = Str(RowGet(spawn_row, "source_scene", "overworld"));
        encounter.source_key = Str(RowGet(spawn_row, "source_key", encounter.spawn_id));
        encounter.members = Str(RowGet(team_row, "members", ""));
        encounter.difficulty_id = difficulty_id;
        encounter.contact = contact;
        encounter.ApplyContact(db);
        return encounter;
    }

    /// <summary>
    /// 表行与字典通用取值：行是 `Resource`（按属性名 Get）、也可能是 `Dictionary`（按 key 取），
    /// 缺值时退回 `fallback`——GDScript 那版就是这么写的（`Dictionary.get` 不吃默认参数）。
    /// </summary>
    private static Variant RowGet(Variant row, string key, Variant fallback)
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
        if (obj == null)
        {
            return fallback;
        }
        Variant value = obj.Get(key);
        return value.VariantType == Variant.Type.Nil ? fallback : value;
    }

    /// <summary>`str(x)`：Nil 当空串（GDScript 的 `str(null)` 就是 `"&lt;null&gt;"`，但这里只用来读表列）。</summary>
    private static string Str(Variant value) =>
        value.VariantType == Variant.Type.Nil ? "" : value.AsString();

    private void ApplyContact(TableDb db)
    {
        Godot.Collections.Dictionary consts = CombatConsts(db);
        switch (contact)
        {
            case CONTACT_BACK:
                first_side = side_ally();
                enemy_poise_reduce = ConstValue(consts, "backstab_poise_reduce", 0.3);
                break;
            case CONTACT_SLEEP:
                first_side = side_ally();
                surprise_bonus = ConstValue(consts, "surprise_damage_bonus", 0.5);
                _pending_rules.Add("敌方首回合「无防备」（设计未定它的具体效果，暂按原样开打）");
                break;
            case CONTACT_SPOTTED:
                first_side = side_enemy();
                break;
            case CONTACT_CAUGHT:
                first_side = side_enemy();
                // 首回合不能逃这条已经由 BattleSimulator 真判了（flee_block_reason），不再记暂缓
                break;
            default:
                first_side = -1;
                break;
        }
    }

    private static Godot.Collections.Dictionary CombatConsts(TableDb db)
    {
        var out_consts = new Godot.Collections.Dictionary();
        if (db == null)
        {
            return out_consts;
        }
        foreach (Variant entry in db.rows("combat_const"))
        {
            var row = entry.AsGodotObject() as CombatConstRow;
            if (row == null)
            {
                continue;
            }
            out_consts[row.const_id] = row.value;
        }
        return out_consts;
    }

    private static double ConstValue(Godot.Collections.Dictionary consts, string key, double fallback)
    {
        // 缺行的时候**不自己编数值**？这里与 GDScript 那版口径一致：手感参数给一个兜底值，
        // 真正的「表里必须有这一行」由 validate_tables 的必备常数清单把关。
        return consts.ContainsKey(key) ? consts[key].AsDouble() : fallback;
    }

    // ---------------------------------------------------------------- 查询

    public string contact_label()
    {
        return contact switch
        {
            CONTACT_FRONT => "正面遭遇",
            CONTACT_BACK => "背后偷袭",
            CONTACT_SLEEP => "奇袭",
            CONTACT_SPOTTED => "被发现了",
            CONTACT_CAUGHT => "被追上",
            _ => contact,
        };
    }

    /// <summary>遇敌提示文案（战斗开场用）。</summary>
    public string headline()
    {
        string suffix = is_elite ? "（精英）" : "";
        return $"{contact_label()}{suffix}　{team_name}";
    }

    /// <summary>首回合要传给伤害管线的修正（奇袭增伤只给先手方）。</summary>
    public Godot.Collections.Dictionary round_modifiers(int round_index, int actor_side)
    {
        var mods = new Godot.Collections.Dictionary();
        if (round_index == 1 && surprise_bonus > 0.0 && actor_side == first_side)
        {
            mods["dmg_up"] = surprise_bonus;
        }
        return mods;
    }

    public Godot.Collections.Dictionary summary()
    {
        return new Godot.Collections.Dictionary
        {
            { "spawn_id", spawn_id },
            { "team_id", team_id },
            { "team_name", team_name },
            { "contact", contact },
            { "contact_label", contact_label() },
            { "first_side", first_side },
            { "surprise_bonus", surprise_bonus },
            { "difficulty", difficulty_id },
            { "threat_tag", threat_tag },
            { "is_elite", is_elite },
            { "source_scene", source_scene },
            { "source_key", source_key },
            { "members", members },
        };
    }
}
