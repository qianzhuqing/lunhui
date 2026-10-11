using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 仓库与文件约定那一族的第二半（文档引用／枚举列／控制字节／文件体积／字号两档）。
/// 与 `TestHandshakeRepo.cs` 是同一个类的两份 `partial`——拆开只为不撞 800 行硬上限，
/// 注册进 `tools/RunTestsCs.cs` 的是主文件那一份。
/// </summary>
public partial class TestHandshakeRepo
{
    /// <summary>UI 字号只许这两档（设计 15 §4.4）；`0` 表示"这一处不覆写"，单独放行。</summary>
    private static readonly int[] FontSizeTiers = { 12, 24 };

    private static readonly string[] FontSizeScanDirs = { "scenes", "src" };

    private const string FontSizeTheme = "res://assets/ui/theme_lunhui.tres";

    /// <summary>
    /// 15b 的豁免表：**已退休的工具路径**。GDScript → C# 迁移里有些工具整体搬到了 C# 侧、
    /// 老的 `.gd` 已删，但 `docs/dev/decisions/**` 那类**历史叙事**仍按当时的名字提到它们
    /// （"那时是跑 `run_tests.gd` 的"）。那些引用不是坏链接，所以按名字放行。
    ///
    /// **放行不等于不管**：下面的循环会反向钉住「这些路径确实已经不在 `tools/`」——
    /// 哪天有人把老路径又建回来（比如恢复旧链），这里会红，提醒把名字从表里摘掉。
    /// 表里只放**真退休**的名字，别拿它当坏链接的遮羞布。
    /// </summary>
    private static readonly string[] RetiredToolPaths =
    {
        "run_tests.gd", "build_tables.gd", "analysis_balance.gd", "analysis_perf.gd",
        "analysis_mutations.gd", "_shot.gd", "artgen/font_preview.gd",
    };
    /// <summary>
    /// 12. 开发文档里引用的 `tools/…` 文件必须真的存在。文档里的命令过期，后来的人会照着敲一条
    /// 不存在的命令——`check_maps` 接进总命令那次就差点留下「手动跑一次」这种尸体说法。
    /// 只查 `docs/dev/`（设计文档是策划写的，可能提到将来的工具，不该拿这条误伤）。
    /// 注意这条门限**会读它自己的说明**：文档里要举例（哪怕是反例）请写成 `tools/<假名字>.bat`
    /// 这种不构成真实路径的形态，否则门限自己就红了（真踩过，见框架说明决策 57）。
    ///
    /// **后缀表含 `.tscn`**（2026-10-11 补）：C# 工具进不了 `--script`，入口都是场景
    /// （`run_tests_cs.tscn`／`build_tables.tscn`／`check_loop_cs.tscn`…），漏掉 `.tscn`
    /// 这些**最主要的入口**就没人验了。
    /// </summary>
    private void CheckDocToolReferences()
    {
        // 反向钉住：退休路径**不该**还在，留在表里说明是坏链接的遮羞布
        foreach (var retired in RetiredToolPaths)
        {
            CheckFalse(
                FileAccess.FileExists($"res://tools/{retired}"),
                $"已退休的工具路径 tools/{retired} 又在仓库里出现了——若是有意恢复，请从 RetiredToolPaths 摘掉");
        }

        var regex = new Regex("tools[\\\\/]([A-Za-z0-9_\\.\\\\/]+\\.(?:bat|ps1|gd|py|tscn))");
        var checkedCount = 0;
        foreach (var path in CollectFiles(new[] { "docs/dev" }))
        {
            if (!path.EndsWith(".md", StringComparison.Ordinal))
            {
                continue;
            }

            var text = FileAccess.GetFileAsString($"res://{path}");
            foreach (Match found in regex.Matches(text))
            {
                var rel = found.Groups[1].Value.Replace("\\", "/");
                if (Array.IndexOf(RetiredToolPaths, rel) >= 0)
                {
                    continue; // 历史文档照提老名字，不是坏链接
                }

                checkedCount += 1;
                CheckTrue(FileAccess.FileExists($"res://tools/{rel}"), $"{System.IO.Path.GetFileName(path)} 引用了不存在的工具：tools/{rel}");
            }
        }

        CheckGt(checkedCount, 5.0, $"开发文档里引用到足够多的工具（{checkedCount} 处）");
    }

    /// <summary>
    /// 15c. `tools/validate_tables.ps1` 必须提到每一个「枚举列」——「枚举三处同改」的第三处。
    ///
    /// **去掉注释**再找：第一版是全文匹配，于是"把列名写在注释里"就能把这条门限糊过去——
    /// 反向验证（删掉 `weapon_type_def.default_element` 那条真检查）时当场发现：只剩我自己那句
    /// "这一轮补了哪 7 列" 的注释还留着，于是它照样绿。注释里提一嘴不算查过。
    /// </summary>
    private void CheckPs1MentionsEnumColumns()
    {
        var ps1 = FileAccess.GetFileAsString("res://tools/validate_tables.ps1");
        CheckFalse(ps1.Length == 0, "读得到 tools/validate_tables.ps1");
        var codeBuilder = new System.Text.StringBuilder();
        foreach (var raw in ps1.Split('\n'))
        {
            codeBuilder.Append(raw.Split('#')[0]).Append('\n');
        }

        var code = codeBuilder.ToString();
        var enums = TableValidator.enums();
        CheckGt(enums.Count, 10.0, $"ENUMS 里读到足够多的表（{enums.Count} 张）");
        var checkedCount = 0;
        var missing = new List<string>();
        foreach (var tableKey in enums.Keys)
        {
            var tableName = tableKey.AsString();
            var columns = enums[tableKey].AsGodotDictionary();
            foreach (var columnKey in columns.Keys)
            {
                var column = columnKey.AsString();
                checkedCount += 1;
                var key = $"{tableName}.{column}";
                if (Ps1EnumExempt.ContainsKey(key))
                {
                    continue;
                }

                if (!code.Contains(column, StringComparison.Ordinal))
                {
                    missing.Add(key);
                }
            }
        }

        CheckGt(checkedCount, 30.0, $"扫到足够多的枚举列（{checkedCount} 个）");
        CheckEq(
            missing.Count,
            0,
            "这些枚举列在 `TableValidator.ENUMS` 里有、但 `tools/validate_tables.ps1` 里一个字都没提"
            + $"（「枚举三处同改」的第三处漏了，设计侧跑网时看不见）：{string.Join("、", missing)}");
    }
    /// <summary>
    /// 54. **文本文件里不许有控制字符**——`反引号 + 字母` 被当成转义吃掉，留下的就是这些字节。
    ///
    /// 由来（2026-10-04，决策 339）：`待策划确认.md` 里躺着 **17 个控制字节**，全是这个坑：
    /// PowerShell 的双引号串里 `` `e ``＝ESC（0x1B）、`` `f ``＝换页（0x0C）、`` `a ``＝响铃（0x07），
    /// 而它**连前一个反引号一起**吃掉了：`event_check` 变成 `<ESC>vent_check`、读出来就是
    /// 「`ote 写的是…」这种残句，markdown 的代码片段还会**跨词配对**、整段渲染错位——
    /// 而**任何门限都不看字节**，它就这么躺了好几轮。
    ///
    /// 口径：`docs/`／`src/`／`data/`／`tools/`／`tests/`／`scenes/` 下的文本文件（按扩展名）逐字节扫，
    /// 除 `\t`／`\n`／`\r` 外不许出现任何 `< 0x20` 的字节。`addons/` 是第三方、`.logs/` 是运行产物，都不扫。
    /// </summary>
    private void CheckNoStrayControlBytes()
    {
        var files = CollectFiles(new[] { "docs", "src", "data", "tools", "tests", "scenes" });
        CheckGt(files.Count, 200.0, $"扫到足够多的文本文件（{files.Count} 个）");
        var hits = new List<string>();
        foreach (var path in files)
        {
            var bytes = FileAccess.GetFileAsBytes($"res://{path}");
            var found = new Dictionary<int, int>();
            foreach (var one in bytes)
            {
                var value = (int)one;
                if (value < 0x20 && value != 0x09 && value != 0x0A && value != 0x0D)
                {
                    found[value] = found.GetValueOrDefault(value) + 1;
                }
            }

            if (found.Count == 0)
            {
                continue;
            }

            var parts = new List<string>();
            foreach (var value in found.Keys)
            {
                parts.Add($"0x{value:X2}×{found[value]}");
            }

            hits.Add($"{path}（{string.Join("、", parts)}）");
        }

        CheckEq(
            hits.Count,
            0,
            "这些文本文件里有控制字符——多半是「反引号 ＋ 字母」被当成转义吃掉了（PowerShell 里反引号 e＝ESC／"
            + "反引号 f＝换页／反引号 a＝响铃；连反引号一起没了，读出来就是残句）：" + string.Join("；", hits));
    }

    /// <summary>
    /// 59. 文档里**TAB 后面紧跟一个 ASCII 字母**＝`` `t `` 被吃掉留下的残渣（339 的视野外）。
    ///
    /// 339 把控制字节扫了一遍，但白名单里放行了 `\t`／`\n`／`\r`——缩进要用。结果**同一个坑的另一种
    /// 样子**一直躺在几份开发文档里：PowerShell 双引号串里 `` `t ``＝TAB，于是 `tools\check_maps.bat`
    /// 被吃成 `<TAB>ools\check_maps.bat`、`trigger()` 变成 `<TAB>rigger()`，反引号连同那个字母一起没了。
    ///
    /// 口径：`docs/**/*.md` 与根目录 `AGENTS.md`／`README.md` 里，TAB 后面紧跟 ASCII 字母就算错。
    /// 这条判据不漏：文档里合法的 TAB 只有整行缩进（后面跟中文、ASCII-art 的框线，或另一个 TAB）
    /// 与代码块里的对齐，**没有一种会让 TAB 紧跟一个词的首字母**；真要用 TAB 缩进代码块，改成空格就好。
    /// </summary>
    private void CheckNoEscapeEatenTabs()
    {
        var files = CollectFiles(new[] { "docs" });
        files.Add("AGENTS.md");
        files.Add("README.md");
        var tabRe = new Regex("\\t[A-Za-z]");
        var hits = new List<string>();
        var scanned = 0;
        foreach (var path in files)
        {
            if (!path.EndsWith(".md", StringComparison.Ordinal))
            {
                continue;
            }

            var text = FileAccess.GetFileAsString($"res://{path}");
            if (text.Length == 0)
            {
                continue;
            }

            scanned += 1;
            var lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo += 1;
                var hit = tabRe.Match(raw);
                if (!hit.Success)
                {
                    continue;
                }

                var at = hit.Index;
                var headStart = Math.Max(0, at - 16);
                var head = raw.Substring(headStart, Math.Min(16, raw.Length - headStart));
                var tailStart = at + 1;
                var tail = raw.Substring(tailStart, Math.Min(18, raw.Length - tailStart));
                hits.Add($"{System.IO.Path.GetFileName(path)}:{lineNo}「…{head}<TAB>{tail}…」");
            }
        }

        CheckGt(scanned, 10.0, $"扫到了足够多的文档（{scanned} 份）");
        CheckEq(
            hits.Count,
            0,
            "这些行里 TAB 紧跟字母——是「反引号 ＋ 字母」被当成转义吃掉的残渣（339 的 TAB 版，"
            + $"反引号与那个字母都没了，读出来是残句）：{string.Join("；", hits)}");
    }
    /// <summary>
    /// 66. **单个文件硬上限 800 行**（"上帝类"这一类问题与语言无关，`.cs` 一样挨这条网）。
    /// 拆细的规矩：一个批次拆一个文件、**只挪位置不改行为**，拆完两条链全绿才动下一个。
    /// 白名单就是 `docs/dev/代码拆分清单.md` 本身，**两向**都要查：超了没登记要红、登记了却已拆到
    /// ≤ 800 行也要红（提醒把那行划掉，别让白名单越攒越大）；抬头那句「量：N 个超标」是最容易过期的
    /// 一句话（同「看板写死的动态数字」一个家族），也与实测数对账。
    /// </summary>
    private void CheckFileSize()
    {
        var docText = FileAccess.GetFileAsString(FileSizeListPath);
        CheckFalse(docText.Length == 0, $"读得到 {FileSizeListPath}");
        var listed = FileSizeListedPaths(docText);
        CheckGt(
            listed.Count,
            0.0,
            $"{System.IO.Path.GetFileName(FileSizeListPath)} 的「现状」表应能解析出文件名（表格式改了就要连着改这条门限）");
        CheckFileSizeParsing();
        foreach (var path in listed)
        {
            CheckTrue(
                FileAccess.FileExists($"res://{path}"),
                $"{path} 在拆分清单里，但文件不存在——路径写错等于开了一格假白名单");
        }

        var overLimit = new List<string>();
        foreach (var path in CollectFiles(new[] { "src", "tests", "tools" }))
        {
            if (!IsSizedCodeFile(path))
            {
                continue;
            }

            var lines = CountGdLines($"res://{path}");
            if (lines <= FileSizeLimit)
            {
                continue;
            }

            overLimit.Add(path);
            CheckTrue(
                listed.Contains(path),
                $"{path} 有 {lines} 行、超过 {FileSizeLimit} 行的硬上限——按职责拆细（一批只拆一个、只挪位置不改行为），"
                + "存量登记与建议拆分轴见 docs/dev/代码拆分清单.md；新增代码不许再加剧");
        }

        foreach (var path in listed)
        {
            if (!FileAccess.FileExists($"res://{path}"))
            {
                continue;
            }

            var lines = CountGdLines($"res://{path}");
            if (lines > FileSizeLimit)
            {
                continue;
            }

            CheckTrue(
                false,
                $"{path} 已经拆到 {lines} 行（≤ {FileSizeLimit}），把 `docs/dev/代码拆分清单.md` 里那一行划掉"
                + "——留着的白名单越攒越大，这条门限就慢慢成了摆设");
        }

        var headline = new Regex("量：(\\d+) 个超标").Match(docText);
        CheckTrue(headline.Success, "拆分清单抬头应写明「量：N 个超标」（措辞改了要连着改这条门限）");
        if (headline.Success)
        {
            CheckEq(
                int.Parse(headline.Groups[1].Value),
                overLimit.Count,
                $"拆分清单抬头的「{headline.Value}」与实测的超标文件数（{overLimit.Count} 个）不符");
        }
    }

    /// <summary>
    /// 从「现状」表里取**第 2 列**（文件列，反引号包起来的路径）。
    ///
    /// 只认第 2 列：第 3 列（建议拆分轴）里也写着 `.gd` 路径（如 `tests/handshake/*.gd`），
    /// 那是**目标**不是现状，一起扫进来会变成一条"文件不存在"的假红。
    /// </summary>
    private static List<string> FileSizeListedPaths(string docText)
    {
        var outList = new List<string>();
        foreach (var raw in docText.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('|');
            if (cells.Length < 4)
            {
                continue;
            }

            var name = FirstBackticked(cells[2]);
            if (IsSizedCodeFile(name))
            {
                outList.Add(name);
            }
        }

        return outList;
    }

    /// <summary>
    /// 800 行硬上限管的是"上帝类脚本"这一类问题，与语言无关——C# 迁移之后 `.cs` 一样要挨这条网。
    /// 只放行 `.gd`／`.cs`：清单第 3 列会写"拆成 `src/family/*.gd`"这种目标路径，靠后缀把它们挡在外面。
    /// </summary>
    private static bool IsSizedCodeFile(string path) =>
        path.EndsWith(".gd", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal);

    /// <summary>
    /// 内存负向用例：这条门限的两种"看着是绿的、其实没在管"的坏法都是解析写错造成的
    /// （列取错、末尾换行多数一行），所以先钉住解析本身，再去扫真文件。
    /// </summary>
    private void CheckFileSizeParsing()
    {
        var sample = string.Join("\n", new[]
        {
            "| 行数 | 文件 | 建议拆分轴 |",
            "|---|---|---|",
            "| 999 | `src/a.gd` | 按检查族拆成 `src/family/*.gd` |",
            "| 900 | `tools/b.gd` | 几何与绘制分开 |",
            "| 900 | `src/c.cs` | C# 一样要登记 |",
        });
        var parsed = FileSizeListedPaths(sample);
        CheckEq(parsed.Count, 3, "「现状」表解析出的行数（应只数文件列带 `.gd`／`.cs` 的行）");
        CheckTrue(parsed.Contains("src/a.gd"), "解析出第 2 列的反引号路径");
        CheckTrue(parsed.Contains("src/c.cs"), "`.cs` 也算现状（C# 迁移后这条网不能对 .cs 视而不见）");
        CheckFalse(parsed.Contains("src/family/*.gd"), "第 3 列（建议拆分轴）里的 `.gd` 路径不算现状");
        CheckEq(FirstBackticked("   `src/a.gd`  "), "src/a.gd", "取反引号之间的内容并去空白");
        CheckEq(FirstBackticked("没有反引号"), "", "没有反引号就取不到（宁可空，也不能猜）");

        // 末尾换行那一行的口径：两种写法都必须数成 2 行，否则台账与门限会互相打脸。
        CheckEq(CountLinesInText("a\nb\n"), 2, "末尾换行不算一行");
        CheckEq(CountLinesInText("a\nb"), 2, "没有末尾换行同样是 2 行");
        CheckEq(CountLinesInText(""), 0, "空文件 0 行");
    }

    private static string FirstBackticked(string cell)
    {
        var openAt = cell.IndexOf('`');
        if (openAt < 0)
        {
            return "";
        }

        var closeAt = cell.IndexOf('`', openAt + 1);
        if (closeAt < 0)
        {
            return "";
        }

        return cell.Substring(openAt + 1, closeAt - openAt - 1).Trim();
    }

    /// <summary>
    /// 数一个脚本的行数。`split` 会把**末尾那个换行**也切成一个空串，直接数就成了 N+1——
    /// 台账与门限差一行，两边会互相打脸，所以这里显式扣掉。
    /// </summary>
    private static int CountGdLines(string path) => CountLinesInText(FileAccess.GetFileAsString(path));

    private static int CountLinesInText(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var lines = new List<string>(text.Split('\n'));
        if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.Count;
    }
    /// <summary>
    /// 67. UI 字号**只有两档**：正文 12 ／ 标题 24（设计 15 §4.4，2026-10-04 Q82 拍板）。
    ///
    /// 由来：16（＝12×1.33）与 36 这种"非两档"的字号散在场景与代码里，像素字发虚，而且
    /// **没有任何东西盯着**——Q82 收口时一次就清出 11 处 16 ＋ 1 处 36 ＋ 1 处 14。
    /// 口径：`scenes/*.tscn` 的 `theme_override_font_sizes/font_size`、源代码里的
    /// `add_theme_font_size_override("font_size", N)` 与 `font_size: int = N` 形参默认值、
    /// 以及 theme 的 `default_font_size`／`…/font_sizes/font_size`，都只许 12／24；
    /// `0` 放行（`_make_label` 那套用它表示"这一处不覆写"）。**只认字面量**（写成变量的这条盯不住）。
    ///
    /// 搬到 C# 的面板：字号字面量在 `.cs` 里（`AddThemeFontSizeOverride("font_size", 12)` 与
    /// `int font_size = 0` 形参默认值都吃同一条正则）。不收 `.cs` 的话，搬走一个面板就少几处，
    /// 门限会**静默收窄**（搬 battle_screen：扫描面 10 → 8）。
    /// </summary>
    private void CheckFontSizeTiers()
    {
        var hits = new List<string>();
        var scanned = 0;
        foreach (var path in CollectFiles(FontSizeScanDirs))
        {
            if (!path.EndsWith(".tscn", StringComparison.Ordinal)
                && !path.EndsWith(".gd", StringComparison.Ordinal)
                && !path.EndsWith(".cs", StringComparison.Ordinal))
            {
                continue;
            }

            var lineNo = 0;
            foreach (var raw in FileAccess.GetFileAsString($"res://{path}").Split('\n'))
            {
                lineNo += 1;
                var size = FontSizeLiteral(raw);
                if (size < 0)
                {
                    continue;
                }

                scanned += 1;
                if (size != 0 && Array.IndexOf(FontSizeTiers, size) < 0)
                {
                    hits.Add($"{path}:{lineNo}（{size}）");
                }
            }
        }

        var themeText = FileAccess.GetFileAsString(FontSizeTheme);
        CheckFalse(themeText.Length == 0, "读得到主题资源");
        foreach (var raw in themeText.Split('\n'))
        {
            var size = FontSizeLiteral(raw);
            if (size < 0)
            {
                continue;
            }

            scanned += 1;
            if (Array.IndexOf(FontSizeTiers, size) < 0)
            {
                hits.Add($"{System.IO.Path.GetFileName(FontSizeTheme)}（{size}）");
            }
        }

        CheckGt(scanned, 10.0, $"扫到足够多的字号声明（{scanned} 处）");
        CheckEq(
            hits.Count,
            0,
            "UI 字号只有两档（正文 12／标题 24，设计 15 §4.4；浮字也吃两档）：这些地方不是——"
            + string.Join("、", hits));

        // 解析本身的内存负向用例：格式变了却"什么都扫不到"，这条门限会静默变成摆设。
        CheckEq(FontSizeLiteral("theme_override_font_sizes/font_size = 16"), 16, "认得出场景里的写法");
        CheckEq(FontSizeLiteral("	label.add_theme_font_size_override(\"font_size\", 14)"), 14, "认得出代码里的写法");
        CheckEq(
            FontSizeLiteral("func _make_label(node_name: String, font_size: int = 0) -> Label:"),
            0,
            "认得出形参默认值（0 = 不覆写，放行）");
        CheckEq(FontSizeLiteral("default_font_size = 12"), 12, "认得出主题里的 default_font_size");
        CheckEq(FontSizeLiteral("font_size = 20  # 行尾注释"), 20, "行尾注释不影响取值");
        CheckEq(FontSizeLiteral("var x := 1"), -1, "没有字号就给 -1（别拿 0 冒充）");
    }

    /// <summary>
    /// 从一行里取字号字面量（取不到 -1）。三种写法：`…font_size = N`／`…"font_size", N`／
    /// `font_size: int = N`。行尾注释先切掉（注释里举例的写法不该被当成真声明）。
    /// </summary>
    private static int FontSizeLiteral(string line)
    {
        var code = line.Split('#')[0];
        var hit = new Regex("font_size\\s*(?:=\\s*|\"\\s*,\\s*|:\\s*int\\s*=\\s*)(\\d+)").Match(code);
        if (!hit.Success)
        {
            return -1;
        }

        return int.Parse(hit.Groups[1].Value);
    }
}
