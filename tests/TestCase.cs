using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 轻量断言基类（C# 版，对位 `tests/test_case.gd`）。
///
/// 不引入第三方框架：断言自行累计失败，由 <c>tools/RunTestsCs.cs</c> 汇总后决定退出码。
/// 与 GDScript 版的两处差异，都是语言差异逼出来的、不是口味问题：
///   1. GDScript 的 `check_eq` 拿 Variant 比，`1 == 1.0` 判等；C# 侧参数是 `object`
///      （装箱），所以 `ValueEquals` 里先把数值按 double 比，别让 1 与 1.0 变成不等。
///   2. `TestCase` 直接 `new` 出来跑，不经过 `load().new()`——C# 里同一程序集内不需要反射。
///
/// **夹具两边各一份**：GDScript 用例挂在 `tests/test_case.gd`，搬过来的 C# 用例用这里的
/// `SoloState`／`PartyState`／`TableWithTalents`／`TableWithNChars`（逐个用例搬到这里时按需补）。
/// 两套是**同一份口径的两份实现**——改一边要同时改另一边（见 `docs/dev/CSharp迁移计划.md`）。
/// </summary>
public partial class TestCase : RefCounted
{
    private int _checks;
    private readonly System.Collections.Generic.List<string> _failures = new();

    /// <summary>
    /// 需要真实场景树的用例（UI 冒烟）由运行器注入——对位 GDScript 的 `scene_tree`。
    /// **不叫 `SceneTree`**：那会和类型名同名，C# 里能编过但读起来容易误判（Color Color 问题）。
    /// </summary>
    public SceneTree Tree { get; set; }

    /// <summary>本用例累计的断言数（含通过的那些）。</summary>
    public int Checks => _checks;

    /// <summary>本用例记下的失败消息。</summary>
    public System.Collections.Generic.IReadOnlyList<string> Failures => _failures;

    private TableDb _db;

    /// <summary>
    /// 共享一份表库（对位 GDScript 的 `get_db()`）：资源是只读的，多个用例共用一个实例没问题。
    /// 优先复用 `GameData` autoload 里已经加载好的那一份（运行器是场景入口，autoload 早就 `_ready` 过了）；
    /// autoload 不在时自己加载一次。
    /// </summary>
    public TableDb GetDb()
    {
        if (_db == null)
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var gameData = tree?.Root?.GetNodeOrNull<Node>("GameData");
            _db = gameData?.Get("db").As<TableDb>();
            if (_db == null)
            {
                _db = new TableDb();
                _db.load_all();
            }
        }

        return _db;
    }

    /// <summary>夹具默认的「书生」id（对位 GDScript 夹具的 `SOLO_CHAR_ID`）。</summary>
    public const string SoloCharId = "scholar_fallen";

    /// <summary>
    /// 造一个「一个人」的 `GameState`（对位 GDScript 夹具 `tests/test_case.gd::solo_state`）。
    ///
    /// `GameState` 已是 C#（2026-10-11），建局直接走静态口 `GameState.new_game(…)`；
    /// 用例里拿到的还是 `GodotObject`，字段仍用 `Call`／`Get`／`Set` 读写（跨语言句柄）。
    /// </summary>
    public GodotObject SoloState(TableDb db, string difficulty = "normal") =>
        NewGame(db, difficulty, new[] { SoloCharId });

    /// <summary>
    /// 显式点名队伍的开局（对位 GDScript 夹具的 `party_state`／`GameStateScript.new_game(…, ids)`）。
    /// `charIds` 留空＝按 `recruit_def.is_initial` 的默认队伍。
    /// </summary>
    public GodotObject NewGame(TableDb db, string difficulty = "normal", string[] charIds = null) =>
        GameState.new_game(db, difficulty, charIds ?? System.Array.Empty<string>(), 0);

    /// <summary>
    /// 造一份**私有**表库（对位 GDScript 夹具 `tests/test_case.gd::fresh_db`）。
    ///
    /// 谁会往表里注入行就得用这一份：创建角色的自建模板走
    /// `CreationService` → `apply_custom_templates` → `TableDb.inject_row`，
    /// 而 `inject_row` 的写时复制只保证**别的 `TableDb` 实例**不被污染，**当前这一份**照样被改
    /// （`tables[表名]` 换成带新行的那一份）。共用 `GetDb()` 的后果是**跨用例污染**——
    /// 2026-10-11 实测：`TestCreation` 跑完之后，后跑的 `TestCharacterSheet` 读到的书生模板
    /// 已经是自建角色的名字（「家道失落的书生」→「书生」），`character_base` 还多出一行——
    /// 症状看着像属性算错，其实与属性管线无关。
    /// </summary>
    public TableDb FreshDb()
    {
        var db = new TableDb();
        db.load_all();
        return db;
    }

    /// <summary>
    /// 造一份把 `character_base` 的初始资质改掉的表库（对位 GDScript 夹具 `tests/test_case.gd::table_with_talents`）。
    /// 用途是验「资质够了之后真的学会」——**资质不可加点**（设计 0.13.0），只能靠图鉴奖励或夹具把数给到位。
    ///
    /// **按施工单坑53 的形状**：先复制表、再单独复制那一行、改完写回新数组——
    /// 表资源是 Godot 按路径缓存的（`TableDb.load_all()` 两个实例可能拿到同一份），
    /// 直接改行可能改到别人正在用的那一行。
    /// </summary>
    public TableDb TableWithTalents(int wu, int gen)
    {
        var custom = new TableDb();
        custom.load_all();
        var table = (TableResource)custom.tables["character_base"].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            var row = (CharacterBaseRow)entry.As<TableRow>().Duplicate(true);
            row.initial_wu = wu;
            row.initial_gen = gen;
            rows.Add(row);
        }
        table.rows = rows;
        custom.tables["character_base"] = table;
        return custom;
    }

    /// <summary>
    /// 造一支 N 人队伍（对位 GDScript 夹具 `tests/test_case.gd::party_state`）：
    /// 按 `character_base` 的行顺序取前 N 人（不够就取到多少算多少）。
    /// **0.10.0 起 `new_game()` 的队伍来自 `recruit_def.is_initial`**，所以多人用例必须显式点名队伍。
    /// </summary>
    public GodotObject PartyState(TableDb db, int count, string difficulty = "normal")
    {
        var ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in db.rows("character_base"))
        {
            if (ids.Count >= count)
            {
                break;
            }

            if (entry.AsGodotObject() is CharacterBaseRow row)
            {
                ids.Add(row.char_id);
            }
        }

        return NewGame(db, difficulty, ids.ToArray());
    }

    /// <summary>
    /// 造一份「把第一个角色模板复制 N 份」的表库（对位 GDScript 夹具
    /// `tests/test_case.gd::table_with_n_chars`）。非战斗技能表也跟着复制——
    /// `skill:wenxue` 的判定值 = 文学等级 + floor(智/5)，只复制模板不复制技能表的话新角色文学是 0 级。
    ///
    /// **只改内存副本，不碰策划的 CSV**（按施工单坑53 的形状：复制表 → 逐行 `Duplicate(true)` → 写回 rows/index）。
    /// </summary>
    public TableDb TableWithNChars(int count, int smartestIndex = 2, bool longNames = false)
    {
        var custom = new TableDb();
        custom.load_all();
        var table = (TableResource)custom.tables["character_base"].As<TableResource>().Duplicate(true);
        var base_row = (CharacterBaseRow)table.rows[0].As<TableRow>();
        var skill_table =
            (TableResource)custom.tables["character_base_skill"].As<TableResource>().Duplicate(true);
        var base_skills = new System.Collections.Generic.List<CharacterBaseSkillRow>();
        foreach (Variant entry in skill_table.rows)
        {
            if (entry.AsGodotObject() is CharacterBaseSkillRow skill_row
                && skill_row.char_id == base_row.char_id)
            {
                base_skills.Add(skill_row);
            }
        }

        var rows = new Godot.Collections.Array { base_row };
        var skill_rows = new Godot.Collections.Array();
        foreach (CharacterBaseSkillRow skill_row in base_skills)
        {
            skill_rows.Add(skill_row);
        }

        for (int index = 1; index < count; index++)
        {
            var row = (CharacterBaseRow)base_row.Duplicate(true);
            row.id = $"{base_row.char_id}_{index}";
            row.char_id = row.id;
            row.name_cn = longNames
                ? $"玄微剑派掌门人独孤求败·第{index}席"
                : $"{base_row.name_cn} {index}";
            if (index == smartestIndex)
            {
                row.initial_int = base_row.initial_int + 15;
            }

            rows.Add(row);
            foreach (CharacterBaseSkillRow skill_row in base_skills)
            {
                var copy = (CharacterBaseSkillRow)skill_row.Duplicate(true);
                copy.char_id = row.char_id;
                copy.id = $"{row.char_id}|{skill_row.skill_id}";
                skill_rows.Add(copy);
            }
        }

        table.rows = rows;
        table.index = new Godot.Collections.Dictionary();
        for (int index = 0; index < rows.Count; index++)
        {
            table.index[rows[index].As<TableRow>().id] = index;
        }

        skill_table.rows = skill_rows;
        skill_table.index = new Godot.Collections.Dictionary();
        for (int index = 0; index < skill_rows.Count; index++)
        {
            skill_table.index[skill_rows[index].As<TableRow>().id] = index;
        }

        custom.tables["character_base"] = table;
        custom.tables["character_base_skill"] = skill_table;
        return custom;
    }

    /// <summary>
    /// 手搓一个战斗单位（对位 GDScript 用例里的 `_actor()` 夹具）：属性写死一份便于算期望值，
    /// `overrides` 覆盖个别项，最后按上限补满血／内力。
    ///
    /// **属性那 20 个键要写全**（少一个，`stat()` 就吃兜底值，期望值会悄悄不对）；
    /// `poise_max` 默认 100，要更大就先建出来再改字段（`poise_max`／`poise` 都是 `[Export]`）。
    /// 战斗类用例（冷却／多段／架势／增益…）都吃这一份，别各写各的。
    /// </summary>
    public static BattleActor MeleeActor(
        string id, int side, Godot.Collections.Dictionary overrides = null)
    {
        var actor = new BattleActor
        {
            actor_id = id,
            display_name = id,
            side = side,
            level = 10,
            base_accuracy = 1.0,
            stats = new Godot.Collections.Dictionary
            {
                { "hp_max", 300.0 },
                { "atk_phys", 40.0 },
                { "atk_qi", 40.0 },
                { "def_phys", 0.0 },
                { "def_qi", 0.0 },
                { "speed", 10.0 },
                { "hit_rate", 0.5 },
                { "dodge_rate", 0.0 },
                { "crit_rate", 0.0 },
                { "crit_dmg", 0.0 },
                { "qi_max", 100.0 },
                { "qi_regen", 1.0 },
                { "hp_regen", 0.0 },
                { "pen_rate", 0.0 },
                { "block_rate", 0.0 },
                { "block_reduction", 0.0 },
                { "dmg_reduction", 0.0 },
                { "debuff_chance", 0.0 },
                { "debuff_power", 0.0 },
                { "poise_break", 0.0 },
            },
        };
        if (overrides != null)
        {
            foreach (Variant key in overrides.Keys)
            {
                actor.stats[key.AsString()] = overrides[key];
            }
        }

        actor.poise_max = 100;
        actor.refill();
        return actor;
    }

    /// <summary>
    /// 造一份把某张图的 `hidden_trigger` 行复制 `times` 倍的表库（对位 GDScript 夹具
    /// `tests/test_case.gd::table_with_duplicated_triggers`）。
    ///
    /// 用途是线索本的「长列表」用例：真实数据只有 8 条左右，撑不到"列表被截断"才看得出来的规模
    /// （截断在界面上只表现为"少了几条"，很难被发现）。**只改内存副本，不碰策划的 CSV**
    /// （照施工单坑53 的形状：复制表 → 逐行 `Duplicate(true)` → 写回 rows／index）。
    /// </summary>
    public TableDb TableWithDuplicatedTriggers(string scene_id, int times)
    {
        var custom = new TableDb();
        custom.load_all();
        var table = (TableResource)custom.tables["hidden_trigger"].As<TableResource>().Duplicate(true);
        var originals = new System.Collections.Generic.List<HiddenTriggerRow>();
        foreach (Variant entry in table.rows)
        {
            if (entry.AsGodotObject() is HiddenTriggerRow row && row.scene_id == scene_id)
            {
                originals.Add(row);
            }
        }

        for (var index = 1; index < times; index++)
        {
            foreach (HiddenTriggerRow base_row in originals)
            {
                var copy = (HiddenTriggerRow)base_row.Duplicate(true);
                copy.trigger_id = $"{base_row.trigger_id}_dup{index}";
                copy.name_cn = $"{base_row.name_cn}（副本{index}）";
                table.rows.Add(copy);
                table.index[copy.trigger_id] = table.rows.Count - 1;
            }
        }

        custom.tables["hidden_trigger"] = table;
        return custom;
    }

    /// <summary>
    /// 造一份把某张副本的第 1 层复制成 `floors` 层的表库（对位 GDScript 夹具
    /// `tests/test_case.gd::table_with_many_floors`）。
    ///
    /// 用途是副本完成度面板的「长楼层列表」用例：真实数据只有 3 层，撑不到"列表被截断"才看得出来
    /// （截断在界面上只表现为"少了几层"）。**只改内存副本，不碰策划的 CSV**
    /// （照施工单坑53 的形状：复制表 → 逐行 `Duplicate(true)` → 写回 rows／index）。
    /// </summary>
    public TableDb TableWithManyFloors(TableDb db, string sceneId, int floors)
    {
        var custom = new TableDb();
        custom.load_all();
        var table = (TableResource)custom.tables["dungeon_room"].As<TableResource>().Duplicate(true);
        var base_rooms = new System.Collections.Generic.List<DungeonRoomRow>();
        foreach (Variant entry in table.rows)
        {
            if (entry.AsGodotObject() is DungeonRoomRow row && row.scene_id == sceneId && row.floor == 1)
            {
                base_rooms.Add(row);
            }
        }

        for (var floorNumber = 2; floorNumber <= floors; floorNumber++)
        {
            foreach (DungeonRoomRow baseRow in base_rooms)
            {
                var copy = (DungeonRoomRow)baseRow.Duplicate(true);
                copy.room_id = $"{baseRow.room_id}_f{floorNumber}";
                copy.floor = floorNumber;
                copy.room_name = $"{baseRow.room_name}（第{floorNumber}层）";
                copy.exit_rooms = "";
                copy.hidden_trigger = "";
                copy.chest_id = "";
                table.rows.Add(copy);
                table.index[copy.room_id] = table.rows.Count - 1;
            }
        }

        custom.tables["dungeon_room"] = table;
        return custom;
    }

    /// <summary>用例名，打印用。</summary>
    public virtual string SuiteName() => "未命名用例";

    /// <summary>用例主体，子类覆写。</summary>
    public virtual void Run()
    {
    }

    /// <summary>
    /// 直接记一条失败，**不**计入断言数——对位 GDScript 的 `fail()`。
    /// 它是"失败路径的回报口"（例如「所有地图里都没落点」），不执行才说明数据是对的。
    /// </summary>
    public void Fail(string message) => _failures.Add(message);

    public void CheckTrue(bool condition, string message)
    {
        _checks++;
        if (!condition)
        {
            _failures.Add(message);
        }
    }

    public void CheckFalse(bool condition, string message) => CheckTrue(!condition, message);

    public void CheckEq(object actual, object expected, string message)
    {
        _checks++;
        if (!ValueEquals(actual, expected))
        {
            _failures.Add($"{message}（实际 {Format(actual)}，期望 {Format(expected)}）");
        }
    }

    public void CheckNe(object actual, object unexpected, string message)
    {
        _checks++;
        if (ValueEquals(actual, unexpected))
        {
            _failures.Add($"{message}（不该等于 {Format(unexpected)}）");
        }
    }

    public void CheckFloat(double actual, double expected, string message, double tolerance = 0.0001)
    {
        _checks++;
        if (System.Math.Abs(actual - expected) > tolerance)
        {
            _failures.Add($"{message}（实际 {actual}，期望 {expected}，容差 {tolerance}）");
        }
    }

    public void CheckInRange(double actual, double low, double high, string message)
    {
        _checks++;
        if (actual < low || actual > high)
        {
            _failures.Add($"{message}（实际 {actual}，期望落在 [{low}, {high}]）");
        }
    }

    public void CheckGt(double actual, double threshold, string message)
    {
        _checks++;
        if (actual <= threshold)
        {
            _failures.Add($"{message}（实际 {actual}，应大于 {threshold}）");
        }
    }

    public void CheckLt(double actual, double threshold, string message)
    {
        _checks++;
        if (actual >= threshold)
        {
            _failures.Add($"{message}（实际 {actual}，应小于 {threshold}）");
        }
    }

    public void CheckNotNull(object value, string message)
    {
        _checks++;
        if (value == null)
        {
            _failures.Add(message);
        }
    }

    /// <summary>断言应该是 null（`CheckNotNull` 的补集）。</summary>
    public void CheckNull(object value, string message)
    {
        _checks++;
        if (value != null)
        {
            _failures.Add($"{message}（实际 {Format(value)}）");
        }
    }

    private static string Format(object value) => value == null ? "null" : value.ToString();

    private static bool ValueEquals(object actual, object expected)
    {
        if (actual == null || expected == null)
        {
            return actual == null && expected == null;
        }

        // 数值按值比：GDScript 里 `1 == 1.0` 为真，装箱之后 `Equals` 会判假，得自己拉平。
        if (IsNumber(actual) && IsNumber(expected))
        {
            return System.Math.Abs(System.Convert.ToDouble(actual) - System.Convert.ToDouble(expected)) < 1e-9;
        }

        return actual.Equals(expected);
    }

    private static bool IsNumber(object value) =>
        value is int or long or short or byte or sbyte or uint or ulong or ushort or float or double or decimal;
}
