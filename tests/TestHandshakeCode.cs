using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 代码契约（preload／单一定义／存档字段）——"策划对接守卫"的一族
/// （拆自 tests/test_handshake.gd，2026-10-04；2026-10-11 搬 C#）。
/// 只换语言、不改行为：常量与共用夹具在 `TestHandshake`（base）；
/// 第二半（仓库约定／单一出处／只被测试调用的接口／随机种子／存档字段）在
/// `TestHandshakeCodeGuards.cs`（同一个类的 `partial`，只为了不撞 800 行硬上限）。
/// </summary>
public partial class TestHandshakeCode : TestHandshake
{
    public override string SuiteName() => "代码契约（preload／单一定义／存档字段）";

    public override void Run()
    {
        CheckSceneNodeContracts();
        CheckFrameworkSaveClaims();
        CheckNoAbsoluteAutoloadPaths();
        CheckTestsUseSeededRng();
        CheckProjectEntryPoint();
        CheckRepoConventions();
        CheckSingleSourceOfTruth();
        CheckNoNewTestOnlyApi();
        CheckBattleTestsAreSeeded();
        CheckSaveFieldsAreSerialized();
    }

    /// <summary>
    /// 9. 界面骨架的节点契约：代码里 `_require_node("A/B/C")` 要求的节点，在对应 `.tscn` 里必须真的存在。
    /// 运行期场景自检也会发现少节点，但那是「跑起来才知道」；改节点名时最容易只改一边。
    ///
    /// **场景 → 脚本这一头不许按文件名猜**：C# 面板叫 `ClueScreen.cs`、场景叫 `clue_screen.tscn`，
    /// 直接读场景自己的 `ExtResource` 指向的 `src/ui` 脚本——`.gd`／`.cs` 一视同仁。
    /// 一个界面拆成几份 `partial class` 之后节点名散在各分片里（`battle_screen` 的 20 处
    /// `_require_node` 全在 `BattleScreenView.cs`），所以按类名把同类的其它 `.cs` 分片一起收进来，
    /// 别让"拆文件"变成"少检查"。
    /// </summary>
    private void CheckSceneNodeContracts()
    {
        var callRegex = new Regex("_require_node\\(\"([^\"]+)\"\\)");
        var scriptRegex = new Regex("path=\"res://(src/ui/[^\"]+\\.(?:gd|cs))\"");
        var csClassRegex = new Regex("partial\\s+class\\s+([A-Za-z_][A-Za-z0-9_]*)");
        var checkedCount = 0;
        foreach (var scenePath in CollectFiles(new[] { "scenes" }))
        {
            if (!scenePath.EndsWith(".tscn", StringComparison.Ordinal))
            {
                continue;
            }

            var scene = FileAccess.GetFileAsString($"res://{scenePath}");
            var scriptMatch = scriptRegex.Match(scene);
            if (!scriptMatch.Success)
            {
                continue;
            }

            var scriptPath = scriptMatch.Groups[1].Value;
            var sources = new List<string> { FileAccess.GetFileAsString($"res://{scriptPath}") };
            if (scriptPath.EndsWith(".cs", StringComparison.Ordinal) && sources[0].Length != 0)
            {
                var headMatch = csClassRegex.Match(sources[0]);
                if (headMatch.Success)
                {
                    var memberRegex = new Regex($"partial\\s+class\\s+{headMatch.Groups[1].Value}\\b");
                    sources.AddRange(CollectPartialCs(scriptPath, memberRegex));
                }
            }

            foreach (var source in sources)
            {
                if (source.Length == 0)
                {
                    continue;
                }

                foreach (Match found in callRegex.Matches(source))
                {
                    var nodePath = found.Groups[1].Value;
                    // 不能用 `System.IO.Path.GetDirectoryName`：Windows 上它把 `/` 换成分隔符 `\`，
                    // 拼出来的 `parent="Panel\Margin\Column"` 在 `.tscn` 里永远找不到（GDScript 的
                    // `get_base_dir()` 返回的是 `/` 分隔）。这里按 `/` 自己切，口径与老链一致。
                    var slashIndex = nodePath.LastIndexOf('/');
                    var leaf = slashIndex < 0 ? nodePath : nodePath.Substring(slashIndex + 1);
                    var parent = slashIndex < 0 ? "" : nodePath.Substring(0, slashIndex);
                    if (parent.Length == 0)
                    {
                        parent = ".";
                    }

                    checkedCount += 1;
                    var hit = false;
                    foreach (var line in scene.Split('\n'))
                    {
                        if (line.StartsWith($"[node name=\"{leaf}\"", StringComparison.Ordinal)
                            && line.Contains($"parent=\"{parent}\"", StringComparison.Ordinal))
                        {
                            hit = true;
                            break;
                        }
                    }

                    CheckTrue(
                        hit,
                        $"{System.IO.Path.GetFileName(scriptPath)} 的 _require_node(\"{nodePath}\") 在 {scenePath} 里找不到对应节点（期望 parent={parent}）");
                }
            }
        }

        CheckGt(checkedCount, 30.0, $"界面节点契约覆盖到足够多的节点（{checkedCount} 个）");
    }

    /// <summary>找出同一个 `partial class` 的其它 `.cs` 分片（本文件之外），用来把节点契约收全。</summary>
    private static List<string> CollectPartialCs(string scriptPath, Regex memberRegex)
    {
        var outList = new List<string>();
        foreach (var otherPath in CollectFiles(new[] { "src/ui" }))
        {
            if (!otherPath.EndsWith(".cs", StringComparison.Ordinal) || otherPath == scriptPath)
            {
                continue;
            }

            if (memberRegex.Match(FileAccess.GetFileAsString($"res://{otherPath}")).Success)
            {
                outList.Add(FileAccess.GetFileAsString($"res://{otherPath}"));
            }
        }

        return outList;
    }
    /// <summary>
    /// 62. `框架说明` 概述里那两个**存档数字**必须与代码一致：当前版本 vN 与 `to_dict()` 字段数。
    ///
    /// 由来（2026-10-04）：那一节写着「v1 → v10 迁移」「当前版本 **v11**」「字段数（现在是 **22**）」，
    /// 而代码里是 **v15**、字段数 **27**——同一段还照着 v11 列了一遍「每版加了什么」，于是
    /// v12～v15 四版在**入口文档**里根本不存在。那一段不是"带日期的叙事"，是入口概述。
    /// 口径：两句**各扫一处**、都必须扫到（措辞改了要连着改这条门限）。
    /// </summary>
    private void CheckFrameworkSaveClaims()
    {
        var db = new TableDb();
        db.load_all();
        var doc = FileAccess.GetFileAsString("res://docs/dev/框架说明.md");
        CheckFalse(doc.Length == 0, "读得到 docs/dev/框架说明.md");
        var versionRe = new Regex("当前版本 \\*\\*v(\\d+)\\*\\*");
        var versionHit = versionRe.Match(doc);
        CheckTrue(versionHit.Success, "框架说明概述里写了「当前版本 **vN**」（措辞改了要连着改这条门限）");
        if (versionHit.Success)
        {
            CheckEq(
                int.Parse(versionHit.Groups[1].Value),
                GameState.save_version(),
                $"框架说明写过期了：它说当前存档版本是 v{versionHit.Groups[1].Value}，代码里是 v{GameState.save_version()}");
        }

        var sizeRe = new Regex("字段数（现在是 \\*{0,2}(\\d+)\\*{0,2}）");
        var sizeHit = sizeRe.Match(doc);
        CheckTrue(sizeHit.Success, "框架说明概述里写了「字段数（现在是 N）」（措辞改了要连着改这条门限）");
        if (sizeHit.Success)
        {
            var fresh = GameState.new_game(db, "normal", Array.Empty<string>(), 0);
            var actual = fresh.to_dict().Count;
            CheckEq(
                int.Parse(sizeHit.Groups[1].Value),
                actual,
                $"框架说明写过期了：它说 `to_dict()` 有 {sizeHit.Groups[1].Value} 个字段，实际是 {actual}（也在 `test_save_store` 里钉着）");
        }
    }

    /// <summary>
    /// 63. 取 autoload 不许走 `get_node("/root/X")` 绝对路径——`--script` 模式下那样会失败。
    ///
    /// 这是 `AGENTS.md` 的硬性约定，可**全靠人记**；一旦写错**受影响的是工具链本身**
    /// （`run_tests.gd`、变异探针、平衡／性能分析全是用 `--script` 起的）。更糟的是
    /// "静默取不到 → 那块逻辑没生效"，而验收还是绿的。
    /// 口径：**去掉行内注释后**不许出现 `get_node("/root`——`main_menu.gd` 的注释里正是这条规矩的出处，
    /// 别误伤它。同时要求正规写法 `Engine.get_main_loop()` 至少还有几处（否则说明门限量错了对象）。
    /// **迁移期**：`src/bootstrap.gd` 搬 C# 之后 `.gd` 侧的正规写法掉到 0，所以扫描面**两种语言一起收**
    /// （C# 是 `Engine.GetMainLoop()`，绝对路径是 `GetNode("/root…")`）。
    /// </summary>
    private void CheckNoAbsoluteAutoloadPaths()
    {
        var hits = new List<string>();
        var sanctioned = 0;
        var scriptsTotal = 0;
        foreach (var path in CollectFiles(new[] { "src" }))
        {
            // 迁移期 `src/` 里的 `.gd` 会一批批换成 `.cs`（P5 才清零），所以「扫描健康度」
            // 盯的是 `_collect_files()` 还找得到多少脚本（`.gd`＋`.cs`），不再钉死 `.gd` 数量。
            var isCs = path.EndsWith(".cs", StringComparison.Ordinal);
            if (!isCs && !path.EndsWith(".gd", StringComparison.Ordinal))
            {
                continue;
            }

            scriptsTotal += 1;
            var needles = isCs
                ? new[] { "GetNode(\"/root", "GetNodeOrNull(\"/root" }
                : new[] { "get_node(\"/root", "get_node_or_null(\"/root" };
            var lineNo = 0;
            foreach (var raw in FileAccess.GetFileAsString($"res://{path}").Split('\n'))
            {
                lineNo += 1;
                var code = raw.Split('#')[0];
                if (code.Contains("Engine.get_main_loop", StringComparison.Ordinal)
                    || code.Contains("Engine.GetMainLoop", StringComparison.Ordinal))
                {
                    sanctioned += 1;
                }

                foreach (var needle in needles)
                {
                    if (code.Contains(needle, StringComparison.Ordinal))
                    {
                        hits.Add($"{System.IO.Path.GetFileName(path)}:{lineNo}「{raw.Trim()}」");
                    }
                }
            }
        }

        CheckGt(scriptsTotal, 50.0, $"扫到足够多的脚本（.gd＋.cs 共 {scriptsTotal} 个）");
        // `.gd` 清零（P5）之后这条门限连同整份 GDScript 用例链一起退场，现在只要求「还扫得到」。
        CheckGt(sanctioned, 10.0, $"`src/` 里还在用正规写法 Engine.get_main_loop／GetMainLoop（{sanctioned} 处）");
        CheckEq(
            hits.Count,
            0,
            $"这些地方用绝对路径取 autoload（`--script` 模式会失败，写成 Engine.get_main_loop() 再相对取）：{string.Join("；", hits)}");
    }
}
