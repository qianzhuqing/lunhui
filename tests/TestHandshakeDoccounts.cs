using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.World;

namespace Lunhui.Tests;

/// <summary>
/// 文档计数与引用（含美术需求对账）——"策划对接守卫"的一族
/// （拆自 tests/test_handshake.gd，2026-10-04；2026-10-11 搬 C#）。
/// 只换语言、不改行为：常量与共用夹具在 `TestHandshake`（base）。
/// </summary>
public partial class TestHandshakeDoccounts : TestHandshake
{
    public override string SuiteName() => "文档计数与引用（含美术需求对账）";

    public override void Run()
    {
        CheckDesignDocsPathsExist();
        CheckAssetQuestionsMatchStatus();
        CheckCompletionRowsSymbolsExist();
        CheckDesignDocTableCount();
        CheckCombatFeelMatchesDesign();
        CheckArtIconCounts();
        CheckDesignDocRowCounts();
        CheckDesignDocRefs();
        CheckMissingTableClaims();
        CheckStruckEntriesAreClosed();
    }

    /// <summary>
    /// 44. 策划文档里带目录前缀的路径，必须指向**现在真的存在**的东西。
    ///
    /// 由来（2026-10-03）：`docs/dev` 那侧早有一条"开发文档里引到的工具必须存在"，但**只认
    /// `docs/dev` 和 `tools/`**。而策划文档里也在写"去哪找"（`scenes/maps/overworld.tscn`、
    /// `data/tables/character_base.csv`……），这些指针**没有任何门限**——文件一改名/一挪，
    /// 设计侧照着找不到，而验收照样全绿。
    ///
    /// **为什么只收 `docs/design`**：`框架说明.md` 是**带日期的叙事**，里面有意引用一堆"不存在的路径"
    /// 当反面例子——拿现状口径去量它会假红。策划文档要写"将来才建的东西"时，**别带目录前缀**。
    /// **归档不看**（2026-10-10 加）：`docs/design/archive/` 是 47 个版本的历史叙事，记的是**当年**的路径，
    /// 文件一搬迁就必然过时——那不是"指针写错"，是"历史不能改"。
    /// </summary>
    private void CheckDesignDocsPathsExist()
    {
        var pathRe = new Regex(
            "(?:src|scenes|tools|data|assets|tests)/[A-Za-z0-9_\\-/\\.]+\\.(?:gd|tscn|tres|csv|bat|ps1|py|png|json)");
        var checkedCount = 0;
        foreach (var path in CollectFiles(new[] { "docs/design" }))
        {
            if (!path.EndsWith(".md", StringComparison.Ordinal))
            {
                continue;
            }

            if (path.Contains("/archive/", StringComparison.Ordinal))
            {
                continue;
            }

            var text = FileAccess.GetFileAsString($"res://{path}");
            foreach (Match hit in pathRe.Matches(text))
            {
                checkedCount += 1;
                var target = hit.Value;
                CheckTrue(
                    FileAccess.FileExists($"res://{target}"),
                    $"{System.IO.Path.GetFileName(path)} 里写的 {target} 不存在——"
                    + "带目录前缀的应当指向**现在**存在的东西（要写「将来才建」的，别带目录前缀）");
            }
        }

        CheckGt(checkedCount, 5.0, $"策划文档里扫到足够多的路径引用（{checkedCount} 处）");
    }

    /// <summary>
    /// 49. 策划文档里写的「共 N 张 CSV」必须等于真实的表数。
    ///
    /// 由来（2026-10-03）：`00_总览.md` 结尾写着「共 **22** 张 CSV」，而 `06` 写的是 37、现实也是 37——
    /// **同一套策划文档自己就对不上**。口径：只扫 `N 张 CSV` 这种**关于表数**的声称
    /// （`07` 里那些"地图 1 张/5 张"不算），数从注册表现算；`CHANGELOG.md` 不看——它是带日期的叙事。
    /// </summary>
    private void CheckDesignDocTableCount()
    {
        var expected = TableRegistry.tables().Length;
        CheckGt(expected, 10.0, $"注册表里扫到足够多的表（{expected} 张）");
        var claimRe = new Regex("(\\d+)\\s*张\\s*CSV");
        var claims = 0;
        foreach (var path in CollectFiles(new[] { "docs/design" }))
        {
            if (!path.EndsWith(".md", StringComparison.Ordinal) || path.EndsWith("CHANGELOG.md", StringComparison.Ordinal))
            {
                continue;
            }

            var text = FileAccess.GetFileAsString($"res://{path}");
            foreach (Match hit in claimRe.Matches(text))
            {
                claims += 1;
                CheckEq(
                    int.Parse(hit.Groups[1].Value),
                    expected,
                    $"{System.IO.Path.GetFileName(path)} 写「{hit.Value}」，而 data/tables 里实有 {expected} 张（以注册表为准）");
            }
        }

        CheckGt(claims, 0.0, $"至少在一份策划文档里写明了表数（扫到 {claims} 处）");
    }
    /// <summary>
    /// 45. 「地编／美术」那两份清单必须对得上：A 清单的行数 = `当前状态` 3.3 的行数，
    /// 抬头写明的"N 条（A1–Am）"也要与真实条数、最大编号一致。
    ///
    /// 由来（2026-10-03）：3.3 节有 **5** 条给地编／美术的缺口，而 A 清单只有 **4** 条、两份文档的抬头
    /// 还都写着"4 条（A1–A4）"——第 5 条**只活在详细表里**，从没进过"一句话就能回"的清单。
    /// 这与 Q 清单漏项（决策 158／159）、Q 范围写旧（183）是同一个家族。所以把三处一起核。
    /// </summary>
    private void CheckAssetQuestionsMatchStatus()
    {
        var questions = FileAccess.GetFileAsString(QuestionsPath);
        var status = FileAccess.GetFileAsString(StatusPath);
        CheckFalse(questions.Length == 0 && status.Length == 0, "读得到两份现状文档");

        // ① A 清单：行数与最大编号
        var aNumbers = new List<int>();
        foreach (Match hit in new Regex("(?m)^\\|\\s*A(\\d+)\\s*\\|").Matches(questions))
        {
            aNumbers.Add(int.Parse(hit.Groups[1].Value));
        }

        CheckGt(aNumbers.Count, 2.0, $"A 清单扫到足够多的条目（{aNumbers.Count} 条）");
        var aMax = 0;
        foreach (var number in aNumbers)
        {
            aMax = Math.Max(aMax, number);
        }

        // ② `当前状态` 3.3 节的行数（从 `### 3.3` 数到下一个标题）
        var sectionRows = 0;
        var inSection = false;
        var rowRe = new Regex("^\\|\\s*(\\d+)\\s*\\|");
        foreach (var raw in status.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("### 3.3", StringComparison.Ordinal))
            {
                inSection = true;
                continue;
            }

            if (inSection && (line.StartsWith("### ", StringComparison.Ordinal) || line.StartsWith("## ", StringComparison.Ordinal)))
            {
                inSection = false;
                continue;
            }

            if (inSection && rowRe.IsMatch(line))
            {
                sectionRows += 1;
            }
        }

        CheckEq(
            sectionRows,
            aNumbers.Count,
            $"「当前状态」3.3 有 {sectionRows} 条给地编／美术的缺口，而「待策划确认」的 A 清单只有 {aNumbers.Count} 条"
            + "——漏的那条要补进去");

        // ③ 抬头写的"N 条（A1–Am）"：条数与最大编号都要对
        var claimRe = new Regex("地编/美术\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*条（A1[–-]A?(\\d+)）");
        var claims = 0;
        foreach (var path in new[] { QuestionsPath, StatusPath })
        {
            var text = FileAccess.GetFileAsString(path);
            foreach (Match hit in claimRe.Matches(text))
            {
                claims += 1;
                CheckEq(
                    int.Parse(hit.Groups[1].Value),
                    aNumbers.Count,
                    $"{System.IO.Path.GetFileName(path)} 抬头写「{hit.Value}」，而 A 清单实有 {aNumbers.Count} 条");
                CheckEq(
                    int.Parse(hit.Groups[2].Value),
                    aMax,
                    $"{System.IO.Path.GetFileName(path)} 抬头写「{hit.Value}」，而 A 清单最大编号是 A{aMax}");
            }
        }

        CheckGt(claims, 0.0, $"至少在一份文档里写明了 A 清单的条数（扫到 {claims} 处）");
    }

    /// <summary>
    /// 53. **划掉的条目（`~~…~~`）正文里不许再留下「还没做」的字样**（决策 338：A3 那行标题划掉了、
    /// 正文还写着「仍是程序化光晕，换图属开发侧接线」——读它的人会把一件做完的事当成待办）。
    /// 口径：行内有划线的先把划线片段摘掉再查「待办字样」——旧措辞**划掉就放过**（那是明确撤回）。
    /// </summary>
    private void CheckStruckEntriesAreClosed()
    {
        var struckRe = new Regex("~~[^~]*~~");
        var checkedCount = 0;
        var hits = new List<string>();
        foreach (var fileName in new[] { "待策划确认.md", "地图搭建说明.md", "设计实现对照.md", "当前状态.md" })
        {
            var path = $"res://docs/dev/{fileName}";
            var text = FileAccess.GetFileAsString(path);
            CheckFalse(text.Length == 0, $"读得到 {path}");
            if (text.Length == 0)
            {
                continue;
            }

            var lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo += 1;
                if (!raw.Contains("~~", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedCount += 1;
                var body = struckRe.Replace(raw, "");
                var trimmed = body.Trim();
                foreach (var word in StruckPendingWords)
                {
                    if (body.Contains(word, StringComparison.Ordinal))
                    {
                        hits.Add($"{fileName}:{lineNo}「{word}」…{trimmed.Substring(0, Math.Min(50, trimmed.Length))}");
                        break;
                    }
                }
            }
        }

        CheckGt(checkedCount, 5.0, $"扫到足够多的划线条目（{checkedCount} 行）");
        CheckEq(
            hits.Count,
            0,
            "这些「划掉了」的条目正文还在说要开发侧做（自相矛盾：要么把正文改成已完成，要么把旧措辞也划掉）："
            + string.Join("；", hits));
    }

    /// <summary>
    /// 46. `设计实现对照.md` 里 ✅ 行点名的代码符号必须**在代码里真的找得到**。
    ///
    /// 由来（2026-10-03）：那张表是给设计侧看的"✅ = 代码里有实现、且有断言钉着"，54 行 ✅ 里点了
    /// 94 个反引号符号。符号一旦被改名或删掉，这一行就变成**没人能核**的假话——而验收不会红。
    ///
    /// 口径：① 语料**只收代码**（`src/scenes/tests/tools` + `project.godot`）；② 反引号里带点的按
    /// `.` 拆开**逐段**找（文档习惯写 `类.成员`，代码里是 `XxxScript.progress` 这种别名）；
    /// ③ **只看"状态列整个是 ✅"的行**（最后那一格整个以 ✅ 开头才算"这行声称已完成"）。
    /// </summary>
    private void CheckCompletionRowsSymbolsExist()
    {
        var doc = FileAccess.GetFileAsString("res://docs/dev/设计实现对照.md");
        CheckFalse(doc.Length == 0, "读得到 设计实现对照.md");
        var corpusBuilder = new System.Text.StringBuilder();
        corpusBuilder.Append(FileAccess.GetFileAsString("res://project.godot"));
        foreach (var path in CollectFiles(new[] { "src", "scenes", "tests", "tools" }))
        {
            corpusBuilder.Append(FileAccess.GetFileAsString($"res://{path}"));
        }

        CheckGt(corpusBuilder.Length, 100000.0, $"代码语料收得够多（{corpusBuilder.Length} 字符）");
        var corpus = corpusBuilder.ToString();

        var tokenRe = new Regex("`([A-Za-z_][A-Za-z0-9_\\.]*)`");
        var missing = new List<string>();
        var scanned = 0;
        foreach (var raw in doc.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal) || !line.Contains("✅", StringComparison.Ordinal)
                || line.Contains("✅ 已完成", StringComparison.Ordinal))
            {
                continue;
            }

            // 状态列是这一行的**最后一格**：只有它整个以 ✅ 开头，才算"这行声称已完成"。
            var cells = line.Split('|');
            var status = "";
            for (var index = cells.Length - 1; index >= 0; index--)
            {
                if (cells[index].Trim().Length != 0)
                {
                    status = cells[index].Trim();
                    break;
                }
            }

            if (!status.StartsWith("✅", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match hit in tokenRe.Matches(line))
            {
                var token = hit.Groups[1].Value;
                scanned += 1;
                foreach (var part in token.Split('.'))
                {
                    if (!corpus.Contains(part, StringComparison.Ordinal))
                    {
                        missing.Add(token);
                        break;
                    }
                }
            }
        }

        CheckGt(scanned, 50.0, $"✅ 行里扫到足够多的代码符号（{scanned} 个）");
        var sample = new List<string>();
        for (var index = 0; index < Math.Min(6, missing.Count); index++)
        {
            sample.Add(missing[index]);
        }

        CheckEq(
            missing.Count,
            0,
            $"设计实现对照的 ✅ 行点了代码里找不到的符号（{missing.Count} 个，例如 {string.Join(", ", sample)}）：那行要么改符号名，要么如实降级");
    }
    /// <summary>
    /// 19. 02 里的**接触手感数值**：文档正文写了三个百分比，代码/表里各有一份，别让它们悄悄分家。
    ///
    /// 背袭「敌方架势初始值降低 30%」→ `combat_const.backstab_poise_reduce`；
    /// 奇袭「首回合伤害 +50%」→ `combat_const.surprise_damage_bonus`；
    /// 潜行「移速降至 60%」→ `PlayerController.sneak_ratio()`。
    /// 数字**从文档里现读**（不在代码里再抄一遍），所以策划改文档而忘了改表/代码时这里会红。
    /// </summary>
    private void CheckCombatFeelMatchesDesign()
    {
        var doc = FileAccess.GetFileAsString("res://docs/design/02_地图与明雷.md");
        var db = GetDb();
        var want = new Dictionary<string, double>();
        var patterns = new (string Pattern, string Key)[]
        {
            ("降低\\s*(\\d+)%", "backstab"),
            ("首回合伤害\\s*\\+(\\d+)%", "surprise"),
            ("移速降至\\s*(\\d+)%", "sneak"),
        };
        foreach (var (pattern, key) in patterns)
        {
            var found = new Regex(pattern).Match(doc);
            CheckTrue(found.Success, $"02 里找得到「{pattern}」这个数值");
            if (found.Success)
            {
                want[key] = double.Parse(found.Groups[1].Value);
            }
        }

        var backstab = db.get_row("combat_const", "backstab_poise_reduce");
        CheckNotNull(backstab, "combat_const 里有背袭架势削减");
        if (backstab != null)
        {
            CheckFloat(
                backstab.Get("value").AsDouble() * 100.0,
                want.GetValueOrDefault("backstab", -1.0),
                "背袭架势削减与 02 一致（%）",
                0.0001);
        }

        var surprise = db.get_row("combat_const", "surprise_damage_bonus");
        CheckNotNull(surprise, "combat_const 里有奇袭增伤");
        if (surprise != null)
        {
            CheckFloat(
                surprise.Get("value").AsDouble() * 100.0,
                want.GetValueOrDefault("surprise", -1.0),
                "奇袭首回合增伤与 02 一致（%）",
                0.0001);
        }

        CheckFloat(
            PlayerController.sneak_ratio() * 100.0,
            want.GetValueOrDefault("sneak", -1.0),
            "潜行移速比与 02 一致（%）",
            0.0001);
    }
    /// <summary>
    /// 57. 美术清单里的图标数（15 §4.3 与《交接单（美术）》）必须与表行数一致。
    ///
    /// 由来（2026-10-04）：美术交接单写着「图标 160 个（武学 65／装备 27／物品 19／增益 19／异常 4／
    /// 派生 28／地图标记 8）」——**这两组数自己就对不上**（七个类目合计 170），15 §4.3 的标题也还停在
    /// 「约 160 个」。图标数是**排期量**：每加一部武学／一件装备它就变，而没有任何门限会红。
    ///
    /// 口径两条：① 15 §4.3 那张表逐行读「数量」与「来源表」（表名写在反引号里）——数量必须等于那张
    /// 表的行数，标题括号里的总数必须等于各行之和；② 美术交接单里「N 个（…）」的括号内成分之和必须
    /// 等于它写出来的总数（括号外的解释文字不算）。两条都不许「一处都没扫到」就算过。
    /// </summary>
    private void CheckArtIconCounts()
    {
        var db = new TableDb();
        db.load_all();
        CheckEq(db.errors.Length, 0, $"TableDb 能载全（{string.Join("; ", db.errors)}）");
        var spec = FileAccess.GetFileAsString(ArtSpecPath);
        CheckFalse(spec.Length == 0, $"读得到 {ArtSpecPath}");
        var headRe = new Regex("###\\s*4\\.3\\s*图标（(\\d+)\\s*个）");
        var head = headRe.Match(spec);
        CheckTrue(head.Success, "15 §4.3 的标题里写了图标总数（措辞改了要连着改这条门限）");
        var body = spec;
        if (head.Success)
        {
            body = spec.Substring(head.Index + head.Length);
            var nextHead = body.IndexOf("\n### ", StringComparison.Ordinal);
            if (nextHead > 0)
            {
                body = body.Substring(0, nextHead);
            }
        }

        // ① 逐类目：数量 == 来源表的行数，总数 == 各行之和
        var rowRe = new Regex("^\\|\\s*([^|]+?)\\s*\\|\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*\\|\\s*`([a-z][a-z0-9_]*)`");
        var counted = 0;
        var total = 0;
        foreach (var raw in body.Split('\n'))
        {
            var hit = rowRe.Match(raw);
            if (!hit.Success)
            {
                continue;
            }

            var label = hit.Groups[1].Value.Replace("*", "").Trim();
            var claimed = int.Parse(hit.Groups[2].Value);
            var tableName = hit.Groups[3].Value;
            counted += 1;
            total += claimed;
            if (!db.has_table(tableName))
            {
                Fail($"15 §4.3 拿 `{tableName}` 当来源表，但 data/tables 里没有这张表");
                continue;
            }

            var actual = db.rows(tableName).Count;
            CheckEq(
                claimed,
                actual,
                $"15 §4.3 写「{label}」这一档有 {claimed} 个图标，而 `{tableName}` 有 {actual} 行——表长了就要连着算一遍图标量");
        }

        CheckGt(counted, 5.0, $"15 §4.3 扫到了足够多的类目（{counted} 行）");
        if (head.Success)
        {
            CheckEq(
                int.Parse(head.Groups[1].Value),
                total,
                $"15 §4.3 标题写 {head.Groups[1].Value} 个图标，而表里各档合计 {total} 个");
        }

        // ② 美术交接单：「N 个（各档…）」的总数 == 括号内之和
        var sheet = FileAccess.GetFileAsString(ArtHandoverPath);
        CheckFalse(sheet.Length == 0, $"读得到 {ArtHandoverPath}");
        var bundleRe = new Regex("(\\d+)\\s*个\\*{0,2}\\s*（([^）]*)）");
        var numRe = new Regex("\\d+");
        var bundles = 0;
        foreach (Match hit in bundleRe.Matches(sheet))
        {
            var parts = numRe.Matches(hit.Groups[2].Value);
            if (parts.Count < 3)
            {
                continue; // 「（原生 32×32，头肩）」这种不是成分清单
            }

            bundles += 1;
            var innerTotal = 0;
            foreach (Match part in parts)
            {
                innerTotal += int.Parse(part.Value);
            }

            CheckEq(
                int.Parse(hit.Groups[1].Value),
                innerTotal,
                $"美术交接单写「{hit.Value.Replace("*", "")}」，括号里各成分合计 {innerTotal}——两组数自己就打起来了");
        }

        CheckGt(bundles, 0.0, "美术交接单里扫到了「N 个（成分…）」的计数声称");
    }
    /// <summary>
    /// 58. 设计文档里「某张表 N 行／N 件／N 个」的声称，逐条对账（见基类 `RowCountClaims`）。
    /// 口径：正则**抓不到也红**；**带日期的叙事行不算**（`LooksHistorical` 命中就跳过）。
    /// </summary>
    private void CheckDesignDocRowCounts()
    {
        var db = new TableDb();
        db.load_all();
        CheckEq(db.errors.Length, 0, $"TableDb 能载全（{string.Join("; ", db.errors)}）");
        foreach (var spec in RowCountClaims)
        {
            var path = spec.Path;
            var text = FileAccess.GetFileAsString(path);
            CheckFalse(text.Length == 0, $"读得到 {path}");
            if (text.Length == 0)
            {
                continue;
            }

            var re = new Regex(spec.Re);
            var found = 0;
            foreach (var raw in text.Split('\n'))
            {
                if (LooksHistorical(raw))
                {
                    continue;
                }

                foreach (Match hit in re.Matches(raw))
                {
                    found += 1;
                    foreach (var pair in spec.Pairs)
                    {
                        var claimed = int.Parse(hit.Groups[pair.Index].Value);
                        var tableName = pair.Table;
                        var actual = db.rows(tableName).Count;
                        CheckEq(
                            claimed,
                            actual,
                            $"{System.IO.Path.GetFileName(path)}：{spec.Why} 写了 {claimed}，而 `{tableName}` 有 {actual} 行——表长到哪儿，这句就要跟着到哪儿");
                    }
                }
            }

            CheckGt(found, 0.0, $"{System.IO.Path.GetFileName(path)} 抓不到「{spec.Why}」——改措辞要连着改这条门限");
        }
    }
    /// <summary>
    /// 61. 设计文档里点名的「表.列／表.行」必须真在表里——**改列名最容易把这种指针写死**。
    /// 只认 `\b<表名>.<标识符>` 这种**带点号**的写法；`csv` 后缀不算引用；计划中的行写进
    /// `PendingDocRefs`，**双向维护**（真落了就要红，提示删掉那一条）。
    /// </summary>
    private void CheckDesignDocRefs()
    {
        var db = new TableDb();
        db.load_all();
        CheckEq(db.errors.Length, 0, $"TableDb 能载全（{string.Join("; ", db.errors)}）");
        var refRe = new Regex("\\b([a-z][a-z0-9_]*)\\.([A-Za-z][A-Za-z0-9_]*)");
        var checkedCount = 0;
        var missing = new List<string>();
        var pendingSeen = new List<string>();
        foreach (var path in CollectFiles(new[] { "docs/design" }))
        {
            if (!path.EndsWith(".md", StringComparison.Ordinal) || path.EndsWith("CHANGELOG.md", StringComparison.Ordinal))
            {
                continue;
            }

            var text = FileAccess.GetFileAsString($"res://{path}");
            foreach (Match hit in refRe.Matches(text))
            {
                var tableName = hit.Groups[1].Value;
                var token = hit.Groups[2].Value;
                if (token == "csv" || !db.has_table(tableName))
                {
                    continue;
                }

                checkedCount += 1;
                var key = $"{tableName}.{token}";
                if (Array.IndexOf(PendingDocRefs, key) >= 0)
                {
                    pendingSeen.Add(key);
                    continue;
                }

                if (TableHasToken(db, tableName, token))
                {
                    continue;
                }

                missing.Add($"{System.IO.Path.GetFileName(path)} 里的 {key}");
            }
        }

        CheckGt(checkedCount, 50.0, $"设计文档里扫到足够多的「表.列／表.行」引用（{checkedCount} 处）");
        CheckEq(
            missing.Count,
            0,
            $"这些引用既不是列名也不是任何一行的值（列被改名了？还是写错了？）：{string.Join("；", missing)}"
            + "——计划中才有的行列进 PENDING_DOC_REFS 并写明等谁；将来才有的列**不要带点号**写");

        // 计划清单双向维护：行真的落了之后，这里要红，提示删掉那一条
        foreach (var key in PendingDocRefs)
        {
            if (!pendingSeen.Contains(key))
            {
                CheckTrue(false, $"PENDING_DOC_REFS 里的 {key} 已经没有引用了，把这一条删掉");
                continue;
            }

            var parts = key.Split('.');
            CheckFalse(
                parts.Length == 2 && TableHasToken(db, parts[0], parts[1]),
                $"PENDING_DOC_REFS 里的 {key} 已经落表了，把这一条删掉（清单过期 = 下一个人照着它白找）");
        }
    }
    /// <summary>
    /// 「设计实现对照」那张「待补充的表（…）」列在括号里的表名，**一个都不许已经在 `data/tables` 里**。
    ///
    /// 由来（2026-10-04）：那一格写着六个表名、却说五个，而且 `dialogue_node`／`dialogue_option`
    /// 0.31.0 就落表了——**读它的人会去找一张已经存在的表**。口径：只认那一格**括号里**的表名
    /// （说明里可以自由提别的表），所以不会误伤"提到某张现有的表"的行。
    /// </summary>
    private void CheckMissingTableClaims()
    {
        var contrast = FileAccess.GetFileAsString(ContrastPath);
        CheckFalse(contrast.Length == 0, $"读得到 {ContrastPath}");
        var marker = "待补充的表（";
        var slot = contrast.IndexOf(marker, StringComparison.Ordinal);
        CheckGt(slot, 0.0, "对照表里那张「待补充的表（…）」还在（改标题要连着改这条门限）");
        if (slot < 0)
        {
            return;
        }

        var tail = contrast.Substring(slot + marker.Length);
        var close = tail.IndexOf("）", StringComparison.Ordinal);
        CheckGt(close, 0.0, "「待补充的表（…）」后面有右括号收口");
        if (close < 0)
        {
            return;
        }

        var listed = tail.Substring(0, close);
        var db = new TableDb();
        db.load_all();
        var tokenRe = new Regex("`([a-z][a-z0-9_]*)(?:\\.csv)?`");
        var stale = new List<string>();
        foreach (Match hit in tokenRe.Matches(listed))
        {
            var name = hit.Groups[1].Value;
            if (db.has_table(name))
            {
                stale.Add($"{name}（表已存在）");
            }
        }

        CheckEq(
            stale.Count,
            0,
            $"对照表把已经建好的表列进「待补充的表」了（读的人会去找一张已经存在的表）：{string.Join(", ", stale)}");
    }
}
