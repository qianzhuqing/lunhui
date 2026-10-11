using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace Lunhui.Tests;

/// <summary>
/// 行类搬迁门限（P2）。**清单不在这里手抄**：`TableRegistry.cs` 的 `ROW_SCRIPTS` 是唯一源，
/// 值以 `.cs` 结尾的就是"已经搬成 C#"的表——搬一张、这里自动多盯一张，不会漏也不会漂。
///
/// 判据**一律看生成物**，不读属性表——原因是被真事故教出来的：同一次 `get_property_list()`，
/// GDScript 侧给 C# 行类成员打的是标志位 2048，C# 侧 `GetPropertyList()` 拿到的是 4102（不含 2048），
/// 按 2048 过滤在 C# 里一条都读不到。拿标志位写门限等于把"构建脚本能不能写进这一格"
/// 押在绑定的实现细节上，所以改成验 `data/generated/&lt;表&gt;.tres` 里真有这一格、
/// 且引用的就是这份行类。
///
/// 它盯四件事：registry 指到的行类文件存在（拼错路径＝构建直接失败）、
/// CSV 列名与行类成员名逐字相同（对不上就是静默丢一格）、行类真被构建脚本用上了、
/// 生成物里引用的就是这份行类。`.tres` 是构建产物、不在版本库里，所以
/// `run_tests_cs.bat` 会先建表再跑这条用例。
/// </summary>
public partial class TestRows : TestCase
{
    /// <summary>行类已搬成 C# 的表数下限——**搬完一批就把这条抬上去**（贴着实测值，别调松）。</summary>
    private const int PortedFloor = 64;

    /// <summary>
    /// CSV 列名 → 行类成员名，只登记两者不一致的表（与 `TableRegistry.cs` 的 `ALIAS_TRIPLES` 同一份口径）：
    /// `attr_to_stat` 的主键列叫 `id`，而行类里 `id` 是基类字段名、改叫 `row_id`；
    /// `enemy_base` 的 `exp` 与全局函数 `exp()` 撞名，改叫 `exp_reward`。
    /// 两边不一致时这里会红——那是有意的，漂移了必须有人看见。
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> ColumnAliases = new()
    {
        ["attr_to_stat"] = new() { ["id"] = "row_id" },
        ["enemy_base"] = new() { ["exp"] = "exp_reward" },
    };

    /// <summary>
    /// `ROW_SCRIPTS` 的一条：表名 → 行类路径。
    /// 两种写法都认——旧 GDScript 字典是 `"表": "路径"`，C# 集合初始化器是 `{ "表", "路径" }`
    /// （2026-10-10 注册表搬成 C# 时这条差点静默失效：正则只认冒号，`ported` 变成 0 张）。
    /// </summary>
    private static readonly Regex RowScriptEntry = new(
        "\"([a-z0-9_]+)\"\\s*(?::|,)\\s*\"(res://src/data/tables/[A-Za-z0-9_]+\\.(?:gd|cs))\"",
        RegexOptions.Compiled);

    /// <summary>行类里声明的属性：`public 类型 名字 { get; set; }`（只认属性，不认类声明）。</summary>
    private static readonly Regex MemberDeclaration = new(
        "public\\s+[A-Za-z0-9_\\.\\[\\]<>]+\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\{\\s*get",
        RegexOptions.Compiled);

    public override string SuiteName() => "行类搬迁门限";

    public override void Run()
    {
        var registry = FileAccess.GetFileAsString("res://src/data/TableRegistry.cs");
        CheckFalse(registry.Length == 0, "读得到 src/data/TableRegistry.cs");

        var ported = new List<(string Table, string ScriptPath)>();
        foreach (Match match in RowScriptEntry.Matches(registry))
        {
            var table = match.Groups[1].Value;
            var scriptPath = match.Groups[2].Value;
            CheckTrue(
                FileAccess.FileExists(scriptPath),
                $"TableRegistry.cs 的 ROW_SCRIPTS[\"{table}\"] 指向的 {scriptPath} 不存在（路径拼错了？）");

            if (scriptPath.EndsWith(".cs"))
            {
                ported.Add((table, scriptPath));
            }
        }

        CheckTrue(
            ported.Count >= PortedFloor,
            $"行类已搬成 C# 的表有 {ported.Count} 张，少于下限 {PortedFloor}——"
                + "是不是有表的 ROW_SCRIPTS 被改回 .gd 了？（搬完新一批就把这条下限抬上去）");

        foreach (var (table, scriptPath) in ported)
        {
            var csvPath = $"res://data/tables/{table}.csv";
            CheckTrue(FileAccess.FileExists(csvPath), $"{csvPath} 存在");
            var header = ReadHeader(csvPath);

            var source = FileAccess.GetFileAsString(scriptPath);
            CheckTrue(source.Length > 0, $"读得到行类源码 {scriptPath}");

            // 列名 → 成员名：构建脚本就是这么按名字写值的（对不上时 build_tables 直接判失败、
            // 整条链红在第一步，所以这里读源码即可；**不能**去 .tres 里找那一格——
            // `ResourceSaver` 会省掉"等于默认值"的属性，那批列在生成物里本来就没有行）。
            var declared = DeclaredMembers(source);
            foreach (var column in header)
            {
                var member = MemberOf(table, column);
                CheckTrue(
                    declared.Contains(member),
                    $"{scriptPath} 里没有成员 '{member}'（CSV 列 '{column}'）——"
                        + "构建脚本按列名写值，对不上就整条链红在 build_tables");
            }

            CheckEq(declared.Count, header.Count, $"{scriptPath} 的成员个数与 {table}.csv 的列数一致");
            CheckEq(CountExports(source), header.Count, $"{scriptPath} 的 [Export] 个数与 {table}.csv 的列数一致");

            var tresPath = $"res://data/generated/{table}.tres";
            CheckTrue(FileAccess.FileExists(tresPath), $"{tresPath} 存在（生成物没建？先跑一次 build_tables）");
            var tres = FileAccess.GetFileAsString(tresPath);
            CheckTrue(
                tres.Contains($"path=\"{scriptPath}\""),
                $"{tresPath} 里的行类不是 {scriptPath}——构建脚本还在用旧行类（或 .tres 是旧的）");
        }
    }

    /// <summary>读 CSV 表头的第一行（`data/tables/*.csv` 的列名都是 ASCII 标识符，不用带引号的解析）。</summary>
    private static List<string> ReadHeader(string path)
    {
        var firstLine = FileAccess.GetFileAsString(path).Split('\n')[0].Trim();
        if (firstLine.Length > 0 && firstLine[0] == '\uFEFF')
        {
            // BOM：FileAccess 读出来可能带（构建脚本自己剥过一次）。
            firstLine = firstLine.Substring(1);
        }

        var columns = new List<string>();
        foreach (var cell in firstLine.Split(','))
        {
            columns.Add(cell.Trim());
        }

        return columns;
    }

    /// <summary>行类里声明的属性名（`TableRow.cs` 的基类字段 `id` 不在这份文件内）。</summary>
    private static List<string> DeclaredMembers(string source)
    {
        var names = new List<string>();
        foreach (Match match in MemberDeclaration.Matches(source))
        {
            names.Add(match.Groups[1].Value);
        }

        return names;
    }

    /// <summary>数行类里声明了几处 `[Export]`——基类主键 `id` 在 `TableRow.cs` 里，不在这份文件内。</summary>
    private static int CountExports(string source)
    {
        var count = 0;
        var at = source.IndexOf("[Export]", System.StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = source.IndexOf("[Export]", at + 1, System.StringComparison.Ordinal);
        }

        return count;
    }

    private static string MemberOf(string table, string column)
    {
        if (ColumnAliases.TryGetValue(table, out var aliases) && aliases.TryGetValue(column, out var member))
        {
            return member;
        }

        return column;
    }
}
