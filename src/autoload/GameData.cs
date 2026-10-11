using Godot;
using Lunhui.Core;

namespace Lunhui.Autoload;

/// <summary>
/// 全局表库单例（C# 版，对位原 `src/autoload/game_data.gd`，70 行）。
///
/// 启动时加载 `data/generated/*.tres` 并跑一遍交叉校验。
/// 它在 `project.godot` 的 `[autoload]` 里按**路径**注册（`GameData="*res://src/autoload/GameData.cs"`），
/// 所以类名不必也不能加 `class_name`——autoload 名与全局类名同名会冲突（原 `.gd` 的注释同理）。
///
/// **跨语言面**：`db`／`errors`／`ready_ok`／`verbose` 四个成员都有 GDScript 在读
/// （`tools/_shot.gd`、`src/bootstrap` 的旧版），所以全部 `[Export]`——
/// C# 的成员不加 `[Export]` 对脚本不可见（迁移期最常踩的一条）。
/// </summary>
public partial class GameData : Node
{
    /// <summary>生成资源目录。</summary>
    public const string DIRECTORY = "res://data/generated";

    /// <summary>
    /// 表库。类型收成 `GodotObject`（跨语言口）：GDScript 侧读 `game_data.db` 只看得到对象本身，
    /// 真正要表的代码自己 `as TableDb`；类内部走下面的 `Db` 强类型视图。
    /// </summary>
    [Export] public GodotObject db = null;

    /// <summary>最近一次校验的报错（空＝通过）。GDScript 侧读 `.size()`，所以是 `string[]`。</summary>
    [Export] public string[] errors = System.Array.Empty<string>();

    /// <summary>最近一次 `reload()` 是否通过。</summary>
    [Export] public bool ready_ok = false;

    /// <summary>是否把加载结果打到控制台（用例静音用）。</summary>
    [Export] public bool verbose = true;

    /// <summary>类内部的强类型视图；对外仍旧只暴露 `db`。</summary>
    private TableDb Db => db as TableDb;

    /// <summary>
    /// 先把全部 C# 脚本钉住（见 <see cref="ScriptKeepAlive"/>），再加载表。
    /// 顺序无所谓，但放这儿是因为它是**所有场景与工具链都会先经过的唯一入口**——
    /// 少钉一次，反复 new 同一批类的长任务（平衡观测）就会崩在引擎的竞态上。
    /// </summary>
    public override void _Ready()
    {
        ScriptKeepAlive.hold_all();
        reload();
    }

    /// <summary>
    /// 退出前手动回收一次托管对象——见 `GcShutdown` 的说明：不做的话，攒够多 C# 对象的链
    /// （跨场景闭环）会打一屏 `Leaked unsafe reference` 再崩在终结器里，退出码非零、验收假红。
    /// </summary>
    public override void _ExitTree() => GcShutdown.run();

    /// <summary>重新加载全部表并校验。返回是否通过。</summary>
    public bool reload()
    {
        var fresh = new TableDb();
        fresh.load_all_from(DIRECTORY);
        db = fresh;
        errors = TableValidator.validate(fresh);
        ready_ok = errors.Length == 0;
        if (verbose)
        {
            if (ready_ok)
            {
                GD.Print($"[GameData] 配置表就绪：{fresh.tables.Count} 张表");
            }
            else
            {
                GD.Print(
                    $"[GameData] 配置表未就绪，{errors.Length} 条问题（运行 tools/run_tests.bat 或检查 data/generated/）");
            }
        }

        return ready_ok;
    }

    public Resource get_row(string table_name, Variant row_id)
        => Db == null ? null : Db.get_row(table_name, row_id);

    public Resource require_row(string table_name, Variant row_id)
    {
        if (Db == null)
        {
            GD.PushError("[GameData] 表库尚未加载");
            return null;
        }

        return Db.require_row(table_name, row_id);
    }

    public Godot.Collections.Array rows(string table_name)
        => Db == null ? new Godot.Collections.Array() : Db.rows(table_name);

    /// <summary>重新跑一遍校验，返回报错列表（空表示通过）。</summary>
    public string[] validate()
        => Db == null ? new[] { "表库尚未加载，请先调用 reload()" } : TableValidator.validate(Db);
}
