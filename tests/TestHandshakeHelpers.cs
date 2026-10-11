using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 「策划对接守卫」的共用夹具（C# 版，对位 `tests/test_handshake.gd` 的 helper 部分）。
/// 常量在 `TestHandshake.cs`／`TestHandshakeFloors.cs`，八族各自一份 partial。
/// </summary>
public abstract partial class TestHandshake
{
    /// <summary>一行 CSV（列名 → 单元格）。取不到的列返回空串，对位 GDScript 的 `str(row.get(列, ""))`。</summary>
    protected sealed class CsvRow
    {
        private readonly Dictionary<string, string> _cells;

        public CsvRow(Dictionary<string, string> cells) => _cells = cells;

        public string Get(string column) => _cells.TryGetValue(column, out var value) ? value : "";

        public IReadOnlyDictionary<string, string> Cells => _cells;
    }

    /// <summary>
    /// 取出 `func to_dict()` 的**函数体**：从它后面的第一个 `{` 起，按花括号配平到归零。
    ///
    /// 为什么要精确到函数体（而不是"从 to_dict 截到文件结尾"）：那条门限是双向量键的，
    /// 用"后半段"当范围会让"没写进去的字段"被后面某个同名字面量蒙混过关；也因为要问
    /// "写进去的键有没有人读"，而**读档代码就在同一个文件的后半段**，拿"文件后半段"当范围
    /// 等于把答案也算进问题里。
    /// </summary>
    protected static string ToDictBody(string source)
    {
        var start = source.IndexOf("func to_dict", StringComparison.Ordinal);
        return start < 0 ? "" : BracedFrom(source, start);
    }

    /// <summary>
    /// C# 版：声明长这样——`public Godot.Collections.Dictionary to_dict() => new() { ... }`。
    /// 没有 `func` 关键字，所以按「返回类型 + 名字 + 空括号」找签名，再从它后面配平花括号。
    /// （直接 `IndexOf("to_dict")` 会先撞上类注释里提到的那个词，取到错的函数体。）
    /// </summary>
    protected static string ToDictBodyCs(string source)
    {
        var match = Regex.Match(source, "public[ \\t]+[A-Za-z0-9_.<>\\[\\]]+[ \\t]+to_dict[ \\t]*\\([ \\t]*\\)");
        return match.Success ? BracedFrom(source, match.Index + match.Length) : "";
    }

    /// <summary>从 `from` 之后的第一个 `{` 起，按花括号配平到归零，返回那段（含两端花括号）。</summary>
    protected static string BracedFrom(string source, int from)
    {
        var brace = source.IndexOf('{', from);
        if (brace < 0)
        {
            return "";
        }

        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            var ch = source[index];
            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(brace, index - brace + 1);
                }
            }
        }

        return "";
    }

    /// <summary>
    /// `token` 是不是这张表的**列名**（CSV 表头）或**任何一行的值**（宽松口径：宁可漏一个笔误，
    /// 也不要因为设计文档里写了某个非主键的取值而假红——例：`drop_table.drop_chest_silver` 是
    /// "掉落组"的值，不是那一行的主键）。
    /// </summary>
    protected static bool TableHasToken(TableDb db, string tableName, string token)
    {
        if (db.get_row(tableName, token) != null)
        {
            return true;
        }

        var header = FileAccess.GetFileAsString($"res://data/tables/{tableName}.csv").Split('\n');
        if (header.Length > 0)
        {
            foreach (var column in header[0].Split(','))
            {
                var columnName = column.Trim();
                if (columnName.StartsWith("\uFEFF", StringComparison.Ordinal))
                {
                    columnName = columnName.Substring(1);
                }

                if (columnName == token)
                {
                    return true;
                }
            }
        }

        foreach (Variant entry in db.rows(tableName))
        {
            if (entry.AsGodotObject() is not Resource row)
            {
                continue;
            }

            foreach (Godot.Collections.Dictionary prop in row.GetPropertyList())
            {
                // 行类成员怎么认：GDScript 侧给 C# 行类成员打 2048（ScriptVariable），
                // C# 侧 `GetPropertyList()` 拿到的是 4102（**不含** 2048，见 tests/TestRows.cs 的说明）——
                // 照抄 2048 那条过滤会在 C# 里一条都读不到（门限静默失效）。所以认「ScriptVariable **或** Storage」，
                // 既能滤掉分组／类别这类虚拟项，又读得到行类的真实字段。
                var usage = prop["usage"].AsInt64();
                var isData = (usage & (long)PropertyUsageFlags.ScriptVariable) != 0
                    || (usage & (long)PropertyUsageFlags.Storage) != 0;
                if (!isData)
                {
                    continue;
                }

                if (row.Get(prop["name"].AsString()).AsString() == token)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>递归收集某几个目录下的文本文件（相对 res:// 的路径）。</summary>
    protected static List<string> CollectFiles(string[] dirs)
    {
        var outList = new List<string>();
        foreach (var dir in dirs)
        {
            CollectDir(dir, outList);
        }

        return outList;
    }

    private static void CollectDir(string dir, List<string> outList)
    {
        var handle = DirAccess.Open($"res://{dir}");
        if (handle == null)
        {
            return;
        }

        foreach (var fileName in handle.GetFiles())
        {
            if (Array.IndexOf(TextExtensions, System.IO.Path.GetExtension(fileName).ToLowerInvariant().TrimStart('.')) < 0)
            {
                continue;
            }

            outList.Add($"{dir}/{fileName}");
        }

        foreach (var sub in handle.GetDirectories())
        {
            if (sub.StartsWith('.'))
            {
                continue;
            }

            CollectDir($"{dir}/{sub}", outList);
        }
    }

    /// <summary>按后缀收文件（递归），路径不带前导 `/`。</summary>
    protected static void CollectFilesWithSuffix(string dir, string suffix, List<string> outList)
    {
        var handle = DirAccess.Open($"res://{dir}");
        if (handle == null)
        {
            return;
        }

        foreach (var fileName in handle.GetFiles())
        {
            if (fileName.EndsWith(suffix, StringComparison.Ordinal))
            {
                outList.Add($"{dir}/{fileName}".TrimStart('/'));
            }
        }

        foreach (var sub in handle.GetDirectories())
        {
            if (sub.StartsWith('.'))
            {
                continue;
            }

            CollectFilesWithSuffix($"{dir}/{sub}".TrimStart('/'), suffix, outList);
        }
    }

    /// <summary>路径本身出现在别处，或它的 `class_name` 在别的文件里被用到。</summary>
    protected static bool HasReference(string corpus, string path)
    {
        var text = FileAccess.GetFileAsString($"res://{path}");
        if (text.Length == 0)
        {
            return true; // 读不到就不在这里判（别的检查会报），免得误红
        }

        var found = Regex.Match(text, "(?m)^class_name\\s+([A-Za-z_][A-Za-z0-9_]*)");
        if (found.Success)
        {
            var className = found.Groups[1].Value;
            var uses = 0;
            foreach (Match _ in Regex.Matches(corpus, $"\\b{className}\\b"))
            {
                uses += 1;
            }

            // 自己的定义算一次；别的文件里再用到就 > 1
            if (uses > 1)
            {
                return true;
            }
        }

        return corpus.Contains(path, StringComparison.Ordinal);
    }

    /// <summary>按字节读文件（编码检查要用原始字节，`GetFileAsString` 会先把编码理顺）。</summary>
    protected static byte[] ReadRaw(string path)
    {
        using var file = FileAccess.Open($"res://{path}", FileAccess.ModeFlags.Read);
        return file == null ? Array.Empty<byte>() : file.GetBuffer((long)file.GetLength());
    }

    /// <summary>`.bat`：纯 ASCII ＋ 全是 CRLF（有一处裸 LF 就点出来，附字节位置方便定位）。</summary>
    protected void AuditBat(string path)
    {
        var bytes = ReadRaw(path);
        if (bytes.Length == 0)
        {
            Fail($"{path} 读不到内容");
            return;
        }

        var bareLf = -1;
        var nonAscii = -1;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 10 && (i == 0 || bytes[i - 1] != 13) && bareLf < 0)
            {
                bareLf = i;
            }

            if (bytes[i] > 127 && nonAscii < 0)
            {
                nonAscii = i;
            }
        }

        CheckEq(bareLf, -1, $"{path} 第 {bareLf} 字节是裸 LF：cmd 下标签查找会失效，请转成 CRLF");
        CheckEq(nonAscii, -1, $"{path} 第 {nonAscii} 字节不是 ASCII：cmd 按本地代码页解析会变乱码");
    }

    /// <summary>`.ps1`：含非 ASCII 就必须有 UTF-8 BOM。</summary>
    protected void AuditPs1(string path)
    {
        var bytes = ReadRaw(path);
        if (bytes.Length == 0)
        {
            Fail($"{path} 读不到内容");
            return;
        }

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var hasNonAscii = false;
        foreach (var b in bytes)
        {
            if (b > 127)
            {
                hasNonAscii = true;
                break;
            }
        }

        CheckTrue(
            !hasNonAscii || hasBom,
            $"{path} 含非 ASCII（中文）却没有 UTF-8 BOM：PowerShell 5.1 会按 ANSI 读坏它");
    }

    /// <summary>
    /// 从若干路径里扫出 `--xxx-selftest` 开关名（去重排序）。
    /// 路径可以是目录（递归收文本文件）也可以是单个文件——注意 `CollectFiles()` 只认目录，
    /// 传文件进去会静默返回空（第一版就这么错，导致「runner 里 0 个开关」的假红）。
    /// </summary>
    protected static List<string> CollectFlags(string[] paths)
    {
        var files = new List<string>();
        foreach (var entry in paths)
        {
            if (System.IO.Path.GetExtension(entry).Length > 0)
            {
                files.Add(entry);
            }
            else
            {
                files.AddRange(CollectFiles(new[] { entry }));
            }
        }

        var found = new List<string>();
        foreach (var path in files)
        {
            var text = FileAccess.GetFileAsString($"res://{path}");
            foreach (Match hit in Regex.Matches(text, "--([a-z-]*selftest)"))
            {
                var flag = $"--{hit.Groups[1].Value}";
                if (!found.Contains(flag))
                {
                    found.Add(flag);
                }
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>拆 `a|b|c`：去空白、丢掉空段（对位 GDScript 的 `split("|", false)`）。</summary>
    protected static List<string> Split(string raw)
    {
        var outList = new List<string>();
        foreach (var part in raw.Split('|'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                outList.Add(trimmed);
            }
        }

        return outList;
    }

    protected static bool LooksHistorical(string prefix) =>
        prefix.Contains("以前", StringComparison.Ordinal)
        || prefix.Contains("当时", StringComparison.Ordinal)
        || prefix.Contains("当年", StringComparison.Ordinal);

    /// <summary>读 CHANGELOG 顶部的「当前设计版本」。</summary>
    protected string CurrentDesignVersion()
    {
        using var file = FileAccess.Open(ChangelogPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            Fail($"读不到 {ChangelogPath}");
            return "";
        }

        var regex = new Regex("当前设计版本[:：]\\s*\\*{0,2}([0-9]+\\.[0-9]+\\.[0-9]+)");
        while (!file.EofReached())
        {
            var found = regex.Match(file.GetLine());
            if (found.Success)
            {
                return found.Groups[1].Value;
            }
        }

        return "";
    }

    /// <summary>读 CSV（自动剥 BOM），返回逐行的 `CsvRow`。</summary>
    protected List<CsvRow> ReadCsv(string path)
    {
        var rows = new List<CsvRow>();
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            Fail($"读不到 {path}");
            return rows;
        }

        var header = file.GetCsvLine();
        if (header.Length > 0 && header[0].StartsWith("\uFEFF", StringComparison.Ordinal))
        {
            header[0] = header[0].Substring(1);
        }

        while (!file.EofReached())
        {
            var line = file.GetCsvLine();
            if (line.Length == 1 && line[0].Trim().Length == 0)
            {
                continue;
            }

            var cells = new Dictionary<string, string>();
            for (var index = 0; index < header.Length; index++)
            {
                cells[header[index].Trim()] = index < line.Length ? line[index] : "";
            }

            rows.Add(new CsvRow(cells));
        }

        return rows;
    }
}
