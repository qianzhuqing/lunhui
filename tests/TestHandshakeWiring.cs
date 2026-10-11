using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace Lunhui.Tests;

/// <summary>
/// 接线（自检开关／闭环／冒烟／问题追踪）——"策划对接守卫"的一族
/// （拆自 tests/test_handshake.gd，2026-10-04；2026-10-11 搬 C#）。
/// 只换语言、不改行为：常量与共用夹具在 `TestHandshake`（base）。
/// </summary>
public partial class TestHandshakeWiring : TestHandshake
{
    public override string SuiteName() => "接线（自检开关／闭环／冒烟／问题追踪）";

    public override void Run()
    {
        CheckSceneSelftestsAreWired();
        CheckWindowedSmokeMatchesRunner();
        CheckEverySceneIsReachable();
        CheckFrameworkDocToc();
        CheckDesignerQuestionsCoverGaps();
        CheckBlockersAreTracked();
        CheckQuestionNumbering();
        CheckBlockerCitationsAreLive();
    }

    /// <summary>对照表里那四行图例不算数据行（按前缀跳过）。</summary>
    private static readonly string[] BlockerLegend =
    {
        "| 🟡 部分 |", "| ⛔ 未实现·等设计 |", "| ⛔ 未实现·等地编 |", "| ✅ 已完成 |",
    };

    /// <summary>
    /// 26. 「设计实现对照」里每条「部分／未实现」的行，都必须写清**卡在哪里**。
    ///
    /// 由来：这份对照表是**第二份独立维护的缺口清单**，交叉核对时发现有 5 条缺口只活在这份表里。
    /// 允许的写法是 `Q数字`／`决策 数字`／`当前状态.md`／`待策划确认.md`＋Q 编号／`07 待补`／
    /// 「可选」。第一版只认「`当前状态.md` 紧跟 #N」，结果 13 行假红——真实写法中间还夹着字。
    /// **门限要照着现实写，不是照着理想写。**
    /// </summary>
    private void CheckBlockersAreTracked()
    {
        var doc = FileAccess.GetFileAsString(ContrastPath);
        CheckFalse(doc.Length == 0, "读得到 设计实现对照.md");
        // 注意 `待策划确认` 后面**必须**跟一个 Q 编号才算数——只写文件名等于没指出是哪一条。
        var trace = new Regex("Q\\d+|决策\\s*\\d+|当前状态\\.md|待策划确认[^\\n]{0,8}Q\\d+|07 待补|缺口\\s*#\\d+|可选");
        var checkedLines = 0;
        foreach (var raw in doc.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.Contains("⛔") && !line.Contains("🟡"))
            {
                continue;
            }

            var isLegend = false;
            foreach (var mark in BlockerLegend)
            {
                if (line.StartsWith(mark, StringComparison.Ordinal))
                {
                    isLegend = true;
                }
            }

            if (isLegend)
            {
                continue;
            }

            checkedLines += 1;
            CheckTrue(
                trace.IsMatch(line),
                $"设计实现对照的 ⛔／🟡 行没写清卡在哪：{line.Substring(0, Math.Min(36, line.Length))}"
                + "（补 Q 编号／决策编号／当前状态.md／07 待补，或写「可选」）");
        }

        CheckGt(checkedLines, 15.0, $"数到了足够多的 ⛔／🟡 行（{checkedLines} 行）");
    }
    /// <summary>
    /// 34. 「待策划确认」的 Q 编号必须自洽，而且三份「现状文档」里写的范围要对得上。
    ///
    /// 三条一起钉：① 编号不许重（两条问题共用一个编号 = 引用它必然指错一边）；② 1..最大编号里缺的
    /// 那些，必须与「跳号登记」那行**一一对应**（`Q28` 是结案移除、编号空着不挪；谁把一条问题
    /// **悄悄删掉**，这里当场红）；③ 现状文档里写的 `Q<起>–<止>`，止必须等于真实最大编号。
    /// `框架说明.md` **不在其中**：那里是带日期的叙事（"那天补到哪"），拿现状口径去量它会假红。
    /// </summary>
    private void CheckQuestionNumbering()
    {
        var questions = FileAccess.GetFileAsString(QuestionsPath);
        CheckFalse(questions.Length == 0, "读得到 待策划确认.md");
        if (questions.Length == 0)
        {
            return;
        }

        var rowRe = new Regex("(?m)^\\|\\s*Q(\\d+)\\s*\\|");
        var seen = new HashSet<int>();
        var dupes = new List<string>();
        var maxQ = 0;
        var count = 0;
        foreach (Match hit in rowRe.Matches(questions))
        {
            var number = int.Parse(hit.Groups[1].Value);
            count += 1;
            maxQ = Math.Max(maxQ, number);
            if (!seen.Add(number))
            {
                dupes.Add($"Q{number}");
            }
        }

        CheckGt(maxQ, 20.0, $"Q 清单数到了足够多的编号（{count} 条，最大 Q{maxQ}）");
        CheckEq(dupes.Count, 0, $"同一编号被两条问题共用（引用它会指错）：{string.Join(", ", dupes)}");

        // ② 跳号必须登记：登记行写成「**跳号登记**（…）：Q28。」（机器只认这行里的 Q 编号）
        var declared = new HashSet<int>();
        var markerRe = new Regex("跳号登记[^\\n]*");
        var numberRe = new Regex("Q(\\d+)");
        foreach (var raw in questions.Split('\n'))
        {
            var marker = markerRe.Match(raw);
            if (!marker.Success)
            {
                continue;
            }

            foreach (Match hit in numberRe.Matches(marker.Value))
            {
                declared.Add(int.Parse(hit.Groups[1].Value));
            }
        }

        var missing = new List<string>();
        for (var number = 1; number <= maxQ; number++)
        {
            if (!seen.Contains(number) && !declared.Contains(number))
            {
                missing.Add($"Q{number}");
            }
        }

        CheckEq(missing.Count, 0, $"这些编号既没有对应的问题、也不在「跳号登记」里（被悄悄删了？）：{string.Join(", ", missing)}");
        foreach (var number in declared)
        {
            CheckTrue(!seen.Contains(number), $"跳号登记里的 Q{number} 又回来了——把那条登记删掉");
        }

        // ③ 现状文档里的范围必须止于真实最大编号
        var rangeRe = new Regex("Q(\\d+)\\s*[–—-]\\s*Q?(\\d+)");
        var ranged = 0;
        foreach (var path in new[] { StatusPath, QuestionsPath, HandoverPath })
        {
            var text = FileAccess.GetFileAsString(path);
            foreach (Match hit in rangeRe.Matches(text))
            {
                ranged += 1;
                CheckEq(
                    int.Parse(hit.Groups[2].Value),
                    maxQ,
                    $"{System.IO.Path.GetFileName(path)} 里写的 Q 范围「{hit.Value}」止于 Q{hit.Groups[2].Value}，"
                    + $"而 Q 清单最大是 Q{maxQ}——清单长了，这句没跟上");
            }
        }

        CheckGt(ranged, 0.0, $"至少在一份现状文档里写明了当前 Q 范围（扫到 {ranged} 处）");
    }
    /// <summary>
    /// 25. 「待策划确认」那份 Q 清单必须真的覆盖「当前状态」里的每一条缺口。
    ///
    /// 由来：Q 清单是第一版手工压缩出来的，交叉核对时发现**漏了 15 条**——包括最严重的那条
    /// （`character_base` 只有 1 行 → 第一章红色精英线全档 0 胜）。**一份漏了大半的待办清单比没有清单更糟**：
    /// 策划照着它逐条回完，以为清空了，其实还差一半。所以把覆盖做成门限：3.1／3.2 里每一行的编号
    /// 都必须在 Q 清单里被引用到（3.1 引用成 `#N`，3.2 引用成 `3.2 #N`——两节编号会重合，所以分开认）。
    /// 新加一条缺口时同时补一条 Q，是有意识的行为。
    /// </summary>
    private void CheckDesignerQuestionsCoverGaps()
    {
        var status = FileAccess.GetFileAsString(StatusPath);
        var questions = FileAccess.GetFileAsString(QuestionsPath);
        CheckFalse(status.Length == 0, "读得到 当前状态.md");
        CheckFalse(questions.Length == 0, "读得到 待策划确认.md");
        var rowRegex = new Regex("^\\|\\s*(\\d+)\\s*\\|\\s*\\*\\*");
        var section = "";
        var dataRows = 0;
        var ruleRows = 0;
        foreach (var raw in status.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("### 3.1", StringComparison.Ordinal))
            {
                section = "3.1";
                continue;
            }

            if (line.StartsWith("### 3.2", StringComparison.Ordinal))
            {
                section = "3.2";
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                section = "";
                continue;
            }

            if (section.Length == 0)
            {
                continue;
            }

            var hit = rowRegex.Match(line);
            if (!hit.Success)
            {
                continue;
            }

            var number = int.Parse(hit.Groups[1].Value);
            // `#1` 不能拿 `Contains` 判——它会被 `#17` 蒙混过去，所以用带边界的正则。
            var needle = section == "3.2"
                ? $"3\\.2\\s*#{number}(?!\\d)"
                : $"(^|[^\\d.])#{number}(?!\\d)";
            var regex = new Regex(needle);
            if (section == "3.1")
            {
                dataRows += 1;
            }
            else
            {
                ruleRows += 1;
            }

            CheckTrue(
                regex.IsMatch(questions),
                $"当前状态 {section} #{number} 没有进「待策划确认」——补一条 Q，或把那条缺口从表里移走");
        }

        CheckGt(dataRows, 15.0, $"3.1 数到了足够多的缺口（{dataRows} 条）");
        CheckGt(ruleRows, 3.0, $"3.2 数到了足够多的缺口（{ruleRows} 条）");
    }

    /// <summary>
    /// 47. `框架说明.md` 的目录必须与实际的 `##` 小节**一一对应（含顺序）**。
    ///
    /// 由来：这个文件已经 3600+ 行、13 个小节、200+ 条编号决策，**却没有目录**——接手的人每次都靠 grep 找。
    /// 补了目录之后缺的是"别让它烂"：新增/改名一个小节而忘了改目录，读的人就会照着目录找不到那一节，
    /// 而**没有任何东西会红**。判定口径：目录节里的 `N. 标题` 行，必须与全文 `## 标题`（去掉目录自己）
    /// **逐条逐序相等**；数量不同、标题不同、顺序不同都点名。
    /// </summary>
    private void CheckFrameworkDocToc()
    {
        const string path = "res://docs/dev/框架说明.md";
        var doc = FileAccess.GetFileAsString(path);
        CheckFalse(doc.Length == 0, "读得到 框架说明.md");

        var headings = new List<string>();
        var toc = new List<string>();
        var tocRe = new Regex("^(\\d+)\\.\\s+(.+)$");
        var inToc = false;
        foreach (var raw in doc.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var title = line.Substring(3).Trim();
                if (title == "目录")
                {
                    inToc = true;
                    continue;
                }

                inToc = false;
                headings.Add(title);
                continue;
            }

            if (!inToc)
            {
                continue;
            }

            var hit = tocRe.Match(line);
            if (!hit.Success)
            {
                continue;
            }

            var entry = hit.Groups[2].Value.Trim();
            // 目录里允许写"← 备注"这类尾巴，比对时只取标题本体
            var arrow = entry.IndexOf('←');
            if (arrow >= 0)
            {
                entry = entry.Substring(0, arrow).Trim();
            }

            toc.Add(entry);
        }

        CheckGt(headings.Count, 8.0, $"扫到足够多的小节（{headings.Count} 个）");
        CheckGt(toc.Count, 8.0, $"目录里扫到足够多条目（{toc.Count} 条）");

        var problems = new List<string>();
        if (toc.Count != headings.Count)
        {
            problems.Add($"目录有 {toc.Count} 条、实际有 {headings.Count} 个小节");
        }

        for (var index = 0; index < Math.Min(toc.Count, headings.Count); index++)
        {
            if (toc[index] != headings[index])
            {
                problems.Add($"第 {index + 1} 条：目录写「{toc[index]}」，实际是「{headings[index]}」");
            }
        }

        for (var index = Math.Min(toc.Count, headings.Count); index < headings.Count; index++)
        {
            problems.Add($"缺登记：{headings[index]}");
        }

        var sample = new List<string>();
        for (var index = 0; index < Math.Min(4, problems.Count); index++)
        {
            sample.Add(problems[index]);
        }

        CheckEq(
            problems.Count,
            0,
            $"框架说明的目录与真实小节对不上（{problems.Count} 处：{string.Join("；", sample)}）——新增/改名小节要顺手改目录");
    }
    /// <summary>
    /// 39. `scenes/` 下每个场景都必须有一步"真的跑得到它"。
    ///
    /// 由来：`bootstrap.tscn` 被 `AGENTS.md` 当成"配置表诊断场景"，却**没有任何一步跑过它**（决策 192 才补上）。
    /// 而现有的孤儿门限只问"有没有人**提到**这个路径"——`AGENTS.md` 里写一句也算提到。
    /// 这里的判据换成可执行的：要么出现在 `run_all_checks.bat` 的真场景自检里，要么被某个用例加载。
    /// </summary>
    private void CheckEverySceneIsReachable()
    {
        var runner = FileAccess.GetFileAsString("res://tools/run_all_checks.bat");
        CheckFalse(runner.Length == 0, "读得到总命令脚本");
        var corpus = "";
        foreach (var path in CollectFiles(new[] { "tests" }))
        {
            corpus += FileAccess.GetFileAsString($"res://{path}");
        }

        var checkedScenes = 0;
        foreach (var path in CollectFiles(new[] { "scenes" }))
        {
            if (!path.EndsWith(".tscn", StringComparison.Ordinal))
            {
                continue;
            }

            checkedScenes += 1;
            var resPath = $"res://{path}";
            if (runner.Contains(resPath, StringComparison.Ordinal))
            {
                continue; // 有真场景自检（自带 `SELF-TEST` 标记与退出码）
            }

            CheckTrue(
                corpus.Contains(resPath, StringComparison.Ordinal),
                $"{resPath} 既不在总命令的真场景自检里、也没有任何用例加载它——新场景得接上（决策 192 的由来）");
        }

        CheckGt(checkedScenes, 15.0, $"扫到足够多的场景（{checkedScenes} 个）");
    }

    /// <summary>
    /// 代码里支持 `--xxx-selftest` 的每个脚本，都要在 `tools/run_all_checks.bat` 里被调用一次；
    /// 反过来 runner 里写的开关也必须真的有实现——写错开关名时场景会照常启动、不退出，
    /// 只因为没有 `SELF-TEST: OK` 而判红，从 harness 层面看不出是「开关写错了」还是「场景真坏了」。
    /// </summary>
    private void CheckSceneSelftestsAreWired()
    {
        var codeFlags = CollectFlags(new[] { "src" });
        var runnerFlags = CollectFlags(new[] { "tools/run_all_checks.bat" });
        CheckGt(codeFlags.Count, 5.0, $"扫到足够多的场景自检开关（{codeFlags.Count} 个）");
        CheckGt(runnerFlags.Count, 5.0, $"总命令里调用了足够多的自检（{runnerFlags.Count} 个）");
        foreach (var flag in codeFlags)
        {
            CheckTrue(
                runnerFlags.Contains(flag),
                $"{flag} 场景支持 {flag}，但 tools/run_all_checks.bat 没调用它（写了自检却没人跑）");
        }

        foreach (var flag in runnerFlags)
        {
            CheckTrue(
                codeFlags.Contains(flag),
                $"tools/run_all_checks.bat 调用了 {flag}，但没有哪个脚本实现它（开关名写错了？）");
        }
    }

    /// <summary>
    /// 「真窗口冒烟」那一份清单必须与总命令**逐条一致**——两份 .bat 各抄了一份场景表，而它们谁也不看谁：
    /// 新加一个场景自检只补了 `run_all_checks.bat`，冒烟就会**静默少跑一个**，而两边都还是"全 OK"。
    /// 这正是决策 200 想防的事，却一直靠人抄两遍。
    /// </summary>
    private void CheckWindowedSmokeMatchesRunner()
    {
        var runner = CollectSceneCalls("tools/run_all_checks.bat");
        var smoke = CollectSceneCalls("tools/run_windowed_smoke.bat");
        CheckGt(runner.Count, 5.0, $"总命令里扫到足够多的场景自检（{runner.Count} 条）");
        CheckGt(smoke.Count, 5.0, $"真窗口冒烟里扫到足够多的场景自检（{smoke.Count} 条）");
        var missing = new List<string>();
        foreach (var entry in runner)
        {
            if (!smoke.Contains(entry))
            {
                missing.Add(entry);
            }
        }

        CheckEq(
            missing.Count,
            0,
            "这些场景自检在 tools/run_all_checks.bat 里有、但 tools/run_windowed_smoke.bat 里没有"
            + "（新加自检要两份都补，否则冒烟静默少跑一个、两边还都是绿的）："
            + string.Join("、", missing));
        var extra = new List<string>();
        foreach (var entry in smoke)
        {
            if (!runner.Contains(entry))
            {
                extra.Add(entry);
            }
        }

        CheckEq(extra.Count, 0, $"这些场景自检只在真窗口冒烟里（总命令漏了？）：{string.Join("、", extra)}");

        // 两个清单里的场景路径都必须真的存在（改名只改了一边时，这条会先响）
        var badPaths = new List<string>();
        var all = new List<string>(runner);
        all.AddRange(smoke);
        foreach (var entry in all)
        {
            var scene = entry.Split('|')[1];
            if (!FileAccess.FileExists(scene) && !badPaths.Contains(scene))
            {
                badPaths.Add(scene);
            }
        }

        CheckEq(badPaths.Count, 0, $"两份清单里指向不存在的场景：{string.Join("、", badPaths)}");
    }
    /// <summary>
    /// 从一份 .bat 里扫出 `check_scene.bat "名字" "场景" "开关"` 的三元组（拼成 `名字|场景|开关`）。
    /// **按行扫并跳过 `rem`／`::` 注释行**——第一版是全文本正则，把注释掉的那一行也数了进去，
    /// 于是"注释掉一个场景自检"这条最容易被忽略的改法**照样是绿的**（反向验证时当场发现）。
    /// </summary>
    private static List<string> CollectSceneCalls(string path)
    {
        var regex = new Regex("check_scene\\.bat\"\\s+\"([^\"]+)\"\\s+\"([^\"]+)\"\\s+\"([^\"]+)\"");
        var found = new List<string>();
        var text = FileAccess.GetFileAsString($"res://{path}");
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("rem ", StringComparison.Ordinal)
                || line.StartsWith("rem\t", StringComparison.Ordinal)
                || line.StartsWith("::", StringComparison.Ordinal))
            {
                continue;
            }

            var hit = regex.Match(line);
            if (!hit.Success)
            {
                continue;
            }

            found.Add($"{hit.Groups[1].Value}|{hit.Groups[2].Value}|{hit.Groups[3].Value}");
        }

        return found;
    }

    /// <summary>
    /// 47. 对照表里当卡点引用的 **Q 编号必须是"还活着的问题"**。
    ///
    /// 由来：Q1（毒酒从哪来）与 Q2（6 件良品装从哪来）都被设计后续补上了来源，我把它们划掉、
    /// 登记成跳号；可 `设计实现对照.md` 里**当时还有行拿它们当卡点**——那意味着接手的人会照着一条
    /// 已经解决的问题白找一遍（和 185 的"过期的数字"、`KnownFlagGaps` 的"过期缺口清单"是同一家族：
    /// **清单本身也会过期，而过期清单比不写更糟**）。
    ///
    /// 口径：只看 ⛔／🟡 的行（图例那四行不算），扫里面的 `Q\d+`；被划掉的 Q 写成 `| ~~Q1~~ |`，
    /// 行首正则 `^\| Q1(?!\d)` 自然匹配不到 → 就算"不是活问题"。
    /// </summary>
    private void CheckBlockerCitationsAreLive()
    {
        var doc = FileAccess.GetFileAsString(ContrastPath);
        var questions = FileAccess.GetFileAsString(QuestionsPath);
        CheckFalse(doc.Length == 0 && questions.Length == 0, "读得到对照表与问题清单");
        var qRe = new Regex("Q(\\d+)");
        var cited = 0;
        var stale = new List<string>();
        foreach (var raw in doc.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.Contains("⛔") && !line.Contains("🟡"))
            {
                continue;
            }

            var isLegend = false;
            foreach (var mark in BlockerLegend)
            {
                if (line.StartsWith(mark, StringComparison.Ordinal))
                {
                    isLegend = true;
                }
            }

            if (isLegend)
            {
                continue;
            }

            foreach (Match hit in qRe.Matches(line))
            {
                var qid = int.Parse(hit.Groups[1].Value);
                cited += 1;
                var liveRe = new Regex($"(?m)^\\| Q{qid}(?!\\d)");
                if (!liveRe.IsMatch(questions) && !stale.Contains($"Q{qid}"))
                {
                    stale.Add($"Q{qid}");
                }
            }
        }

        CheckGt(cited, 10.0, $"对照表的 ⛔／🟡 行里扫到足够多的 Q 引用（{cited} 处）");
        CheckEq(stale.Count, 0, $"对照表里这些 Q 已经不是活问题了（被回答／划掉就更新那一行）：{string.Join("、", stale)}");
    }
}
