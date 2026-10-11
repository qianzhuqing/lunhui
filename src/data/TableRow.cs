using Godot;

namespace Lunhui.Data;

/// <summary>
/// 配置表行基类（C# 版，对位 `src/data/table_row.gd`）。
///
/// 每张 CSV 对应一个行类，行类的 `[Export]` 成员与 CSV 列一一对应，
/// 类型由行类声明，构建脚本据此把字符串转换成 int/float/bool/string。
/// 主键由构建脚本写入 `id`（多主键用 "|" 连接），行类里不再声明 id 字段。
///
/// **成员名保持 snake_case，与 CSV 列名逐字相同**——这是迁移期刻意的选择，不是疏忽：
/// `src/data/TableRegistry.cs` 的 `COLUMN_ALIASES` 按"属性名 ＝ 列名"折算，
/// 而还没搬走的 GDScript（`src/core`、`src/world`、`src/ui` 与全部用例）里到处是
/// `row.char_id` 这种取值。名字改成 PascalCase 就得同时扫全仓调用点，
/// 一搬一动之间会留下大量"半新半旧"的中间态。等 P5 全部搬完、GDScript 清零以后，
/// 要不要整体改回 PascalCase 再单独议（那时是纯机械改名，有 C# 侧用例兜底）。
/// </summary>
public partial class TableRow : Resource
{
    /// <summary>主键值。单主键为列值本身，复合主键按 `table_registry.PRIMARY_KEYS` 的顺序用 "|" 连接。</summary>
    [Export]
    public string id { get; set; } = "";
}
