using System.Collections.Generic;
using Godot;
using Lunhui.Core;
using Lunhui.Data;

namespace Lunhui.Tools;

/// <summary>
/// 构建期配置表转换（C# 版，对位 `tools/build_tables.gd`）：`data/tables/*.csv` → `data/generated/*.tres`。
///
/// 用法（headless；`--log-file` 不能省，否则 `user://logs` 写失败会直接崩）：
///   godot --headless --path . --log-file .logs/build.log res://tools/build_tables.tscn
///
/// 列的类型来自行类的 `[Export]` 声明，所以「CSV 列 ↔ 行类属性」对不上会当场报错；
/// 交叉校验规则在 `src/core/TableValidator.cs`（构建期与运行期共用同一份）。
///
/// **为什么是场景而不是 `--script`**：C# 进不了 `--script` 入口（那条路只认 GDScript），
/// 所以照 `tools/RunTestsCs.cs` 的形状——`_Ready` 里延后一帧跑、跑完 `Quit(code)`。
/// </summary>
public partial class BuildTables : Node
{
    public override void _Ready()
    {
        // 与 RunTestsCs 同一个理由：_Ready 正处在根节点装配子节点的窗口里，延后一帧更稳。
        Callable.From(() => GetTree().Quit(Run())).CallDeferred();
    }

    private int Run()
    {
        var errors = new List<string>();
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(TableRegistry.output_dir()));

        var db = new TableDb();
        var totalRows = 0;
        foreach (string tableName in TableRegistry.tables())
        {
            TableResource table = BuildTable(tableName, errors);
            if (table == null)
            {
                continue;
            }

            db.tables[tableName] = table;
            totalRows += table.rows.Count;
            string path = $"{TableRegistry.output_dir()}/{tableName}.tres";
            Error saveError = ResourceSaver.Save(table, path);
            if (saveError != Error.Ok)
            {
                errors.Add($"保存 {path} 失败（错误码 {(int)saveError}）");
            }
        }

        errors.AddRange(TableValidator.validate(db));

        if (errors.Count > 0)
        {
            GD.PrintErr($"=== 配置表构建失败，共 {errors.Count} 条问题 ===");
            foreach (string message in errors)
            {
                GD.PrintErr("  - " + message);
            }

            return 1;
        }

        GD.Print($"=== 配置表构建完成：{db.tables.Count} 张表 / {totalRows} 行 → {TableRegistry.output_dir()} ===");
        return 0;
    }

    private static TableResource BuildTable(string tableName, List<string> errors)
    {
        string csvPath = $"{TableRegistry.tables_dir()}/{tableName}.csv";
        if (!FileAccess.FileExists(csvPath))
        {
            errors.Add($"缺少 CSV：{csvPath}");
            return null;
        }

        List<string[]> tableLines = ReadCsv(csvPath, errors);
        if (tableLines.Count == 0)
        {
            return null;
        }

        string[] header = tableLines[0];
        string rowScriptPath = TableRegistry.row_scripts()[tableName].AsString();
        var rowScript = GD.Load<Script>(rowScriptPath);
        if (rowScript == null)
        {
            errors.Add($"行类脚本无法加载：{rowScriptPath}");
            return null;
        }

        Resource template = InstantiateRow(rowScript);
        Dictionary<string, int> properties = ExportedProperties(template);

        // CSV 列 ↔ 行类属性 双向对齐检查：任何一边多了少了都当场报错
        var columnToProperty = new Dictionary<string, string>();
        foreach (string column in header)
        {
            string propertyName = TableRegistry.property_of(tableName, column);
            if (!properties.ContainsKey(propertyName))
            {
                errors.Add($"{tableName} 的 CSV 列 '{column}' 在 {rowScriptPath} 里没有对应属性");
                continue;
            }

            columnToProperty[column] = propertyName;
        }

        foreach (KeyValuePair<string, int> pair in properties)
        {
            string column = TableRegistry.column_of(tableName, pair.Key);
            if (System.Array.IndexOf(header, column) < 0)
            {
                errors.Add($"{tableName} 的行类属性 '{pair.Key}' 在 CSV 表头里没有对应列");
            }
        }

        // 主键列名只取一次：GDScript 那版每行都重建一遍整张 primary_keys 表，
        // 结果一样但白花 O(行数 × 表数)，这里故意提到循环外。
        string[] primaryKeys = PrimaryKeysOf(tableName);

        var rows = new Godot.Collections.Array();
        for (int lineIndex = 1; lineIndex < tableLines.Count; lineIndex++)
        {
            string[] line = tableLines[lineIndex];
            if (line.Length != header.Length)
            {
                errors.Add($"{tableName} 第 {lineIndex + 1} 行有 {line.Length} 列，表头是 {header.Length} 列");
                continue;
            }

            Resource row = InstantiateRow(rowScript);
            for (int index = 0; index < header.Length; index++)
            {
                if (!columnToProperty.TryGetValue(header[index], out string propertyName))
                {
                    continue;
                }

                row.Set(propertyName, ConvertValue(line[index], properties[propertyName]));
            }

            // 主键用「行类里的强类型值」拼，保证与运行期校验算出来的一致
            var primaryValues = new Godot.Collections.Dictionary();
            foreach (string key in primaryKeys)
            {
                primaryValues[key] = row.Get(TableRegistry.property_of(tableName, key));
            }

            row.Set("id", TableRegistry.build_id(tableName, primaryValues));
            rows.Add(row);
        }

        var table = new TableResource
        {
            table_name = tableName,
            primary_keys = primaryKeys,
            rows = rows,
        };

        var rowIndexById = new Godot.Collections.Dictionary();
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            rowIndexById[rows[rowIndex].As<Resource>().Get("id").AsString()] = rowIndex;
        }

        table.index = rowIndexById;
        return table;
    }

    /// <summary>
    /// 按脚本路径造一个行类实例。行类现在是 C#（`CSharpScript`），而 C# 绑定没把
    /// `Script` 的变参方法 `new` 暴露成 `New()`，所以走 `Call("new")` 动态调用——
    /// 与 GDScript 那版 `load(path).new()` 是同一个 ClassDB 方法。
    /// </summary>
    private static Resource InstantiateRow(Script script) => script.Call("new").As<Resource>();

    /// <summary>读 CSV：剥 BOM、跳过空行、保留表头。打不开时记一条错并返回空表。</summary>
    private static List<string[]> ReadCsv(string path, List<string> errors)
    {
        var lines = new List<string[]>();
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            errors.Add($"无法读取 {path}");
            return lines;
        }

        string[] header = file.GetCsvLine();
        if (header.Length > 0 && header[0].StartsWith("\ufeff", System.StringComparison.Ordinal))
        {
            header[0] = header[0].Substring(1);
        }

        lines.Add(header);
        while (!file.EofReached())
        {
            string[] line = file.GetCsvLine();
            if (line.Length == 1 && line[0].Trim().Length == 0)
            {
                continue;
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>行类里声明的 `[Export]` 成员：{属性名: 类型}。</summary>
    private static Dictionary<string, int> ExportedProperties(Resource instance)
    {
        var result = new Dictionary<string, int>();
        foreach (Godot.Collections.Dictionary property in instance.GetPropertyList())
        {
            if (((int)property["usage"].AsInt64() & (int)PropertyUsageFlags.ScriptVariable) == 0)
            {
                continue;
            }

            string name = property["name"].AsString();
            if (name == "id")
            {
                // 基类主键字段，由构建脚本统一写入，不是 CSV 列
                continue;
            }

            result[name] = property["type"].AsInt32();
        }

        return result;
    }

    /// <summary>CSV 字符串 → 行类字段类型。空值取类型默认值（0 / 0.0 / false / ""）。</summary>
    private static Variant ConvertValue(string raw, int type)
    {
        string value = raw.Trim();
        switch ((Variant.Type)type)
        {
            case Variant.Type.String:
                return value;
            case Variant.Type.Int:
                return (long)System.Math.Round(ParseFloat(value), System.MidpointRounding.AwayFromZero);
            case Variant.Type.Float:
                return ParseFloat(value);
            case Variant.Type.Bool:
                return value == "1" || value.ToLowerInvariant() == "true";
            default:
                return value;
        }
    }

    /// <summary>
    /// 按 GDScript `String.to_float()` 的口径解析：宽松（解析不了给 0.0）、不受区域设置影响。
    /// **别用 `StringExtensions.ToFloat`**——C# 侧它返回 **32 位** float：`item_money.stack_max`
    /// 的 `999999999` 会被舍成 `1000000000`（2026-10-11 与旧链逐字节对拍时抓到的唯一一处差异）。
    /// </summary>
    private static double ParseFloat(string value) =>
        double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double parsed
        )
            ? parsed
            : 0.0;

    /// <summary>某张表的主键列名（按声明顺序）。</summary>
    private static string[] PrimaryKeysOf(string tableName)
    {
        Godot.Collections.Array columns = TableRegistry.primary_keys()[tableName].AsGodotArray();
        var keys = new string[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            keys[i] = columns[i].AsString();
        }

        return keys;
    }
}