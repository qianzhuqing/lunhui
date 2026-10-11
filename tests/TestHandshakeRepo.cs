using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 仓库与文件约定——"策划对接守卫"的一族
/// （拆自 tests/test_handshake.gd，2026-10-04；2026-10-11 搬 C#）。
/// 只换语言、不改行为：常量与共用夹具在 `TestHandshake`（base）；
/// 第二半（文档引用／枚举列／控制字节／文件体积／字号两档）在 `TestHandshakeRepoFiles.cs`
/// （同一个类的 `partial`，只为了不撞 800 行硬上限）。
/// </summary>
public partial class TestHandshakeRepo : TestHandshake
{
    public override string SuiteName() => "仓库与文件约定";

    public override void Run()
    {
        CheckNoOrphanCode();
        CheckPs1MentionsEnumColumns();
        CheckDocToolReferences();
        CheckNoDanglingUids();
        CheckNoOrphanImportFiles();
        CheckGitRepoContract();
        CheckTablesDirIsNotImported();
        CheckEngineScriptsScanLogs();
        CheckNoStrayControlBytes();
        CheckNoEscapeEatenTabs();
        CheckFileSize();
        CheckFontSizeTiers();
    }

    /// <summary>
    /// 37. 凡是自己起引擎的 `tools/*.bat`，都必须扫引擎日志错误。
    ///
    /// `check_log_errors.bat` 是专治"运行期错误掐断函数、标记与退出码照样绿"那条的（决策 147／148），
    /// 几个**步骤包装**都接了它；但两个**观测工具**没接——`run_balance_analysis.bat`／
    /// `run_perf_analysis.bat` 自己起引擎跑分析脚本，而脚本只打一张表、既不收尾也不判通过：
    /// **一次运行期错误就能让"半截表"看起来像完整结果**（决策 167／168 那条路就是这么走出来的）。
    ///
    /// 判定：`tools/*.bat` 里出现**行首就是 `"%GODOT_BIN%"` 的调用**（真的起引擎）才收；
    /// 只定义变量、只做存在性检查的不算。
    /// </summary>
    private void CheckEngineScriptsScanLogs()
    {
        var invokeRe = new Regex("(?m)^\\s*\"%GODOT_BIN%\"");
        var checkedCount = 0;
        foreach (var fileName in DirAccess.GetFilesAt("res://tools"))
        {
            if (!fileName.EndsWith(".bat", StringComparison.Ordinal) || fileName == "check_log_errors.bat")
            {
                continue;
            }

            var text = FileAccess.GetFileAsString($"res://tools/{fileName}");
            if (!invokeRe.IsMatch(text))
            {
                continue;
            }

            checkedCount += 1;
            CheckTrue(
                text.Contains("check_log_errors.bat", StringComparison.Ordinal),
                $"{fileName} 自己起引擎却不扫引擎日志——运行期错误会让它把半截结果当完整结果（补一条 check_log_errors.bat）");
        }

        CheckGt(checkedCount, 5.0, $"扫到足够多自己起引擎的 tools/*.bat（{checkedCount} 个）");
    }

    /// <summary>
    /// 8. `src/` 下的脚本与 `scenes/` 下的场景都必须有人引用（和「孤儿用例」「孤儿设计文档」同一类）。
    /// 判定「被引用」的三种方式：路径字符串出现在别处、或它的 `class_name` 在别的文件里被用到。
    /// 真要留一个「暂时没人用」的东西，把它加进 `InfrastructureScripts` 并写明理由。
    /// </summary>
    private void CheckNoOrphanCode()
    {
        var corpusBuilder = new System.Text.StringBuilder();
        var files = CollectFiles(new[] { "src", "scenes", "tests", "docs", "tools" });
        files.Add("project.godot");
        foreach (var path in files)
        {
            var text = FileAccess.GetFileAsString($"res://{path}");
            if (text.Length != 0)
            {
                corpusBuilder.Append(text);
            }
        }

        var corpus = corpusBuilder.ToString();
        foreach (var path in CollectFiles(new[] { "src" }))
        {
            if (!path.EndsWith(".gd", StringComparison.Ordinal) || Array.IndexOf(InfrastructureScripts, path) >= 0)
            {
                continue;
            }

            CheckTrue(HasReference(corpus, path), $"孤儿脚本：{path} 没有被任何地方引用（写完忘了接上？还是该删了？）");
        }

        foreach (var path in CollectFiles(new[] { "scenes" }))
        {
            if (!path.EndsWith(".tscn", StringComparison.Ordinal))
            {
                continue;
            }

            CheckTrue(HasReference(corpus, path), $"孤儿场景：{path} 没有被任何地方引用（写完忘了接上？还是该删了？）");
        }

        CheckNoOrphanRootScenes(corpus);
    }

    /// <summary>
    /// 8b. 项目**根目录**下的 `.tscn` 也要有主——孤儿扫描只收 `scenes/`，于是**只要有人把场景存到
    /// 项目根，它就能永远躲过检查**（用编辑器 `save_scene` 时默认落点恰好是 `res://node_2d.tscn`）。
    /// 白名单自身也会烂，所以顺手断言它仍然存在；**反方向**也要查：白名单是"暂时没人引用的草稿"，
    /// 一旦被引用就该退场。
    /// </summary>
    private void CheckNoOrphanRootScenes(string corpus)
    {
        foreach (var fileName in DirAccess.GetFilesAt("res://"))
        {
            if (!fileName.EndsWith(".tscn", StringComparison.Ordinal) || Array.IndexOf(RootSceneWhitelist, fileName) >= 0)
            {
                continue;
            }

            CheckTrue(HasReference(corpus, fileName), $"根目录下的场景 {fileName} 没有被任何地方引用（编辑器草稿？还是该删了？）");
        }

        foreach (var fileName in RootSceneWhitelist)
        {
            CheckTrue(FileAccess.FileExists($"res://{fileName}"), $"根场景白名单里的 {fileName} 已经不存在了，把这条白名单删掉");
            // **判定要用纯代码语料**：上面那份 corpus 故意把 docs/tools 也算作"引用"（宽松），
            // 而 `node_2d.tscn` 恰恰在框架说明与 README_MCP 里被反复提到——拿宽语料判会当场假红
            // （第一版踩过，见框架说明决策 178）。
            var codeCorpusBuilder = new System.Text.StringBuilder(FileAccess.GetFileAsString("res://project.godot"));
            foreach (var path in CollectFiles(new[] { "src", "scenes" }))
            {
                codeCorpusBuilder.Append(FileAccess.GetFileAsString($"res://{path}"));
            }

            CheckFalse(HasReference(codeCorpusBuilder.ToString(), fileName), $"根场景白名单里的 {fileName} 已经被引用了，把这条白名单删掉");
        }
    }
    /// <summary>
    /// 8c. `.uid` 文件不许是孤儿。
    ///
    /// Godot 4.4+ 给每个脚本生成 `<名字>.gd.uid`，而**删掉或改名源文件时它不会跟着消失**：
    /// 那对孤儿探针（`__orphan_probe.gd` ＋ `__orphan_probe.tscn`）删干净了，但它的 `.uid` 留了下来——
    /// 142 个 `.uid` 里就这一个对不上，而且**没有任何检查会红**。残骸会骗后来的人「这个脚本还在」。
    /// 注意 `CollectFiles()` 按扩展名过滤、收不到 `.uid`，这里自己递归（跳过 `.godot/` 与隐藏目录）。
    /// </summary>
    private void CheckNoDanglingUids()
    {
        var uids = new List<string>();
        CollectFilesWithSuffix("", ".uid", uids);
        CheckGt(uids.Count, 100.0, $"扫到足够多的 .uid 文件（{uids.Count} 个）");
        foreach (var path in uids)
        {
            var target = path.Substring(0, path.Length - 4);
            CheckTrue(FileAccess.FileExists($"res://{target}"), $"{path} 没有对应的源文件（改名或删文件时留下的残骸？）");
        }
    }

    /// <summary>
    /// 8d. `.import` 残骸：导入元数据还在，源文件没了。
    ///
    /// 由来（2026-10-03）：`data/tables/__probe_unregistered.csv.import` 是反向验证时建临时 CSV 留下的
    /// ——Godot 会给每个被导入的文件写 `<源文件>.import`，而**源文件删掉它不会跟着消失**
    /// （和上面 `.uid` 残骸同一个坑）。残骸会骗后来的人"这个文件还在"。
    /// </summary>
    private void CheckNoOrphanImportFiles()
    {
        var metas = new List<string>();
        CollectFilesWithSuffix("", ".import", metas);
        CheckGt(metas.Count, 20.0, $"扫到足够多的 .import 文件（{metas.Count} 个）");
        foreach (var path in metas)
        {
            var target = path.Substring(0, path.Length - 7);
            CheckTrue(
                FileAccess.FileExists($"res://{target}"),
                $"{path} 没有对应的源文件（删文件／改名时留下的残骸——把它删掉，或把源文件找回来）");
        }
    }

    /// <summary>
    /// 42. `data/tables/` 只放设计师要看的表：不许再长出 Godot 的导入产物。
    ///
    /// 由来（2026-10-03）：`*.csv` 的默认导入器是「CSV 翻译表」，编辑器给每张表按列生成
    /// `*.translation`、按表生成 `*.import`（实测 254 ＋ 37 个）。治法是在表目录放 `.gdignore`
    /// 让编辑器**不扫描**它（构建期用 `FileAccess` 读原始 CSV，不受影响）。
    /// 这条门限把"治好了"钉住：`.gdignore` 在、且目录里没有 `*.translation`／`*.import`。
    /// </summary>
    private void CheckTablesDirIsNotImported()
    {
        var dir = "res://data/tables";
        CheckTrue(
            FileAccess.FileExists($"{dir}/.gdignore"),
            "data/tables/.gdignore 不见了：编辑器会重新把 37 张 CSV 当翻译表导入（254 个 *.translation ＋ 37 个 *.import 会回来）");
        var leftovers = new List<string>();
        foreach (var fileName in DirAccess.GetFilesAt(dir))
        {
            if (fileName.EndsWith(".translation", StringComparison.Ordinal) || fileName.EndsWith(".import", StringComparison.Ordinal))
            {
                leftovers.Add(fileName);
            }
        }

        // 名单可能长到几百个（真回归时就是那样），只举前 5 个 + 总数，别把失败信息刷爆。
        var sample = new List<string>();
        for (var i = 0; i < Math.Min(5, leftovers.Count); i++)
        {
            sample.Add(leftovers[i]);
        }

        CheckEq(
            leftovers.Count,
            0,
            $"data/tables 里又有 Godot 的导入产物了（{leftovers.Count} 个，例如 {string.Join(", ", sample)}）：表目录只该有 CSV（见决策 196）");
    }

    /// <summary>
    /// 41. 版本库契约：`.gitignore` 的忽略面 + `.gitattributes` 的批处理行尾。
    ///
    /// 入库流程是根目录的 `sync_to_git.bat`——`git add .` 全量入库再 push，于是两件事没人盯就会出事：
    ///   ① `.gitignore` 少了 `.godot/`／`data/generated/`／`.logs/`／表目录的导入产物，生成物与日志会被
    ///      扫进仓库；
    ///   ② `.gitattributes` 原本只有 `* text=auto eol=lf`，而仓库约定是「`tools/*.bat` 必须 CRLF」
    ///      （`AuditBat` 按字节验）——两者冲突时**新克隆拿到的是 LF 的 .bat，克隆完验收直接红**，
    ///      本地看不出来（工作区文件是 CRLF、只是没被重新 checkout 过）。
    /// （`git check-attr` 那种"真要问 Git"的验证写在注释与文档里：自检跑在引擎里，没有 git。）
    /// </summary>
    private void CheckGitRepoContract()
    {
        var ignoreText = FileAccess.GetFileAsString("res://.gitignore");
        CheckFalse(ignoreText.Length == 0, "读得到 .gitignore");
        var ignored = new HashSet<string>();
        foreach (var raw in ignoreText.Split('\n'))
        {
            ignored.Add(raw.Trim());
        }

        var required = new[]
        {
            ".godot/", "data/generated/", ".logs/",
            "data/tables/*.translation", "data/tables/*.import",
        };
        foreach (var pattern in required)
        {
            CheckTrue(
                ignored.Contains(pattern),
                $".gitignore 少了 `{pattern}`：入库脚本用 `git add .` 全量收，这一条丢了它就会被提交");
        }

        var attrs = FileAccess.GetFileAsString("res://.gitattributes");
        CheckFalse(attrs.Length == 0, "读得到 .gitattributes");
        // gitattributes 是"后面的规则覆盖前面的"，所以只认**最后一条**命中 *.bat 的规则。
        var batRule = "(没有匹配 *.bat 的规则)";
        foreach (var raw in attrs.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length < 2)
            {
                continue;
            }

            var pattern = cells[0];
            if (pattern == "*.bat" || pattern.EndsWith("/*.bat", StringComparison.Ordinal))
            {
                batRule = line;
            }
        }

        CheckTrue(
            batRule.Contains("eol=crlf", StringComparison.Ordinal),
            $".gitattributes 的 `*.bat` 行尾不是 CRLF（最后一条规则：'{batRule}'）：`eol=lf` 会让新克隆拿到 LF 的 .bat，而仓库约定要求 CRLF——克隆完验收就红");
    }
}