using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 存档迁移那三份用例的公共基类（常量、老档夹具与取值小工具）。
///
/// 拆法照 `tests/test_handshake.gd` 那一套：**只挪位置、不改行为**——常量与共用夹具留基类，
/// 各份只放自己那一族的检查。起因是这份用例从 `tests/test_save_migration.gd`（598 行）
/// 整体搬成 C# 之后是 889 行、撞了 800 行硬上限（见 `docs/dev/代码拆分清单.md`）。
/// 按被测面拆成三份 suite：① `TestSaveMigration` v1–v4 与存档槽／菜单写回；
/// ② `TestSaveMigrationVersions` v9–v14 与逐档覆盖；③ `TestSaveMigrationRobust` 坏档与下架角色。
///
/// **拆成多份 suite 而不是同一份 partial**：`tools/RunTestsCs.cs` 的「静态断言点 vs 实际执行条数」
/// 是按 `Suites` 里的路径逐文件算的，partial 的另一半不注册就没人量；三族各自注册，那道网照旧生效
/// （`test_handshake.gd` 拆八族时同一条理由）。
/// </summary>
public abstract partial class TestSaveMigrationBase : TestCase
{
    protected const string TestDir = "res://.logs/test_migration_cs";
    protected const int Slots = 3;

    /// <summary>
    /// 每个版本「新增了什么字段」（照 `GameState` 头部那份迁移清单）。
    /// 构造某一档老档 = 拿当前存档改 `version`，再把**那一档之后才出现的字段**删掉。
    /// v5 只给装备实例加了 `affixes`，没有顶层字段，单独处理。
    /// </summary>
    protected static readonly (int Version, string[] Fields)[] AddedIn =
    {
        (2, new[] { "inventory", "char_allocations", "party_exp" }),
        (3, new[] { "skill_mastery" }),
        (4, new[] { "char_learned", "char_loadouts" }),
        (5, System.Array.Empty<string>()),
        (6, new[] { "shop_state" }),
        (7, new[] { "dungeon_records", "pity" }),
        (8, new[] { "revealed_nodes" }),
        (9, new[] { "event_checks", "flags" }),
        (10, new[] { "first_kills" }),
        (11, new[] { "char_hp" }),
        (12, new[] { "field_buffs" }),
        (13, new[] { "world_pos" }),
        (14, new[] { "talent_picks", "custom_templates" }),
        (15, new[] { "npc_favor" }),
    };

    /// <summary>一份 0.4.x 时代的存档：只有队伍、等级、难度与时间戳，没有背包。</summary>
    protected static Godot.Collections.Dictionary _V1Dict() => new()
    {
        { "version", 1 },
        { "char_ids", new Godot.Collections.Array { "scholar_fallen" } },
        { "char_levels", new Godot.Collections.Dictionary { { "scholar_fallen", 3 } } },
        { "difficulty_id", "hard" },
        { "chapter_id", "chapter_01" },
        { "slot", 0 },
        { "created_unix", 111 },
        { "saved_unix", 222 },
    };

    protected static Inventory _InventoryOf(GameState state) => (Inventory)state.inventory;

    protected static Godot.Collections.Dictionary _Row(Godot.Collections.Array array, int index) =>
        array[index].AsGodotDictionary();

    protected static string _Text(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    protected static int _I(Godot.Collections.Dictionary dict, string key, int fallback) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;

    /// <summary>`Dictionary` 没有带默认值的取法，自己包一层（缺字段当作空数组）。</summary>
    protected static Godot.Collections.Array _ArrayOf(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsGodotArray() : new Godot.Collections.Array();

    protected static string _Join(string[] ids) => string.Join(",", ids);

    protected static void WriteText(string path, string text)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(text);
    }

    protected static void _CleanDir(string path)
    {
        if (DirAccess.DirExistsAbsolute(path))
        {
            DirAccess dir = DirAccess.Open(path);
            if (dir != null)
            {
                foreach (string fileName in dir.GetFiles())
                {
                    dir.Remove(fileName);
                }
            }
        }

        DirAccess.MakeDirRecursiveAbsolute(path);
    }
}
