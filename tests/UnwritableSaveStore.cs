using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 「存档目录只读」的桩（C# 版，对位 `tests/test_menu.gd` 里内嵌的那只 `ReadOnlyStore`）。
///
/// 只改「写」这一件事：`save_slot` 一律回失败，其余口（列槽、挑槽）转手给一个真 `SaveStore`，
/// 这样菜单控制器以后多调一个口也不会漏。用它而不是真去指一个建不出来的目录，
/// 是为了别让 `FileAccess` 往引擎日志里喷错误——那会被运行期错误门限抓成假红。
///
/// **单独一个文件**：Godot 的 C# 源生成器要求「脚本类名 ＝ 文件名」，不能塞进 `TestMenu.cs` 里当嵌套类。
/// </summary>
public partial class UnwritableSaveStore : RefCounted
{
    private SaveStore _real;

    /// <summary>`MenuController.create()` 走的是 `Call`，所以这里的方法名必须保持 snake_case。</summary>
    public static UnwritableSaveStore create(string dirPath, int slots) =>
        new() { _real = SaveStore.create(dirPath, slots) };

    public Godot.Collections.Array list_slots(TableDb db) => _real.list_slots(db);

    public Godot.Collections.Dictionary slot_for_new_game() => _real.slot_for_new_game();

    public Godot.Collections.Dictionary save_slot(int slot, GodotObject state) =>
        new() { { "ok", false }, { "error", "模拟：存档目录只读" } };
}
