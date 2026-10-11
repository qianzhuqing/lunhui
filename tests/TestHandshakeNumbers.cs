using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;
using Lunhui.Data;

namespace Lunhui.Tests;

/// <summary>
/// 文档里写死的数字与计数——"策划对接守卫"的一族
/// （拆自 tests/test_handshake.gd，2026-10-04；2026-10-11 搬 C#）。
/// 只换语言、不改行为：常量与共用夹具在 `TestHandshake`（base）。
/// </summary>
public partial class TestHandshakeNumbers : TestHandshake
{
    public override string SuiteName() => "文档里写死的数字与计数";

    public override void Run()
    {
        CheckNoHardcodedStepCounts();
        CheckLoopSegmentCount();
        CheckSlotSampleTableMatchesFormula();
        CheckSpecialEffectCountClaim();
        CheckDashboardNumbers();
        CheckHandoverSheetNumbers();
        CheckContrastAuditNumbers();
        CheckVerificationCheckFloors();
    }

    /// <summary>数一段文本里出现了几处字面量（对位 GDScript 的 `String.count`，不走正则）。</summary>
    private static int CountOccurrences(string text, string pattern)
    {
        if (pattern.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count += 1;
            index += pattern.Length;
        }

        return count;
    }

    /// <summary>
    /// 30. 01 的「槽位采样点」表必须与 `growth_const` 的公式算出来的一致。
    ///
    /// 由来（2026-10-03）：那张表是**手算**的，而它就在公式下方当例子用——实测两格是错的：
    /// 「20 级专注悟性 30」的内功容量写 4（公式 2+floor(20/6)+floor(5/10)=5）、
    /// 「20 级专注根骨 25」的招式槽写 6（悟性仍是基础 10 → 2+4+floor(10/8)=7）。
    /// `test_growth` 只钉住了"公式算出来是 5"，没有钉住表本身。现在按表里每一行
    /// （等级 ＋ 写明的悟／根，没写就用首个模板的基础资质）重算一遍，与表里那两个数逐个比——
    /// **设计改公式（growth_const）或改采样点都会立刻反映出来**。
    /// </summary>
    private void CheckSlotSampleTableMatchesFormula()
    {
        var doc = FileAccess.GetFileAsString("res://docs/design/01_角色系统.md");
        CheckFalse(doc.Length == 0, "读得到 01_角色系统.md");
        var db = GetDb();
        var tables = db.rows("character_base");
        if (tables.Count == 0)
        {
            CheckTrue(false, "character_base 至少要有一个模板（采样表的默认资质取自它）");
            return;
        }

        var baseWu = (int)tables[0].AsGodotObject().Get("initial_wu").AsInt64();
        var baseGen = (int)tables[0].AsGodotObject().Get("initial_gen").AsInt64();
        var growth = GrowthCalculator.create(db);
        var levelRegex = new Regex("(\\d+)\\s*级");
        var wuRegex = new Regex("悟(?:性)?\\s*(\\d+)");
        var genRegex = new Regex("根(?:骨)?\\s*(\\d+)");
        var digits = new Regex("(\\d+)");
        var rows = 0;
        foreach (var raw in doc.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal) || !line.Contains('级'))
            {
                continue;
            }

            var cells = line.Split('|');
            if (cells.Length < 5)
            {
                continue;
            }

            var label = cells[1];
            var levelHit = levelRegex.Match(label);
            if (!levelHit.Success)
            {
                continue;
            }

            var slotHit = digits.Match(cells[2].Replace("*", ""));
            var capHit = digits.Match(cells[3].Replace("*", ""));
            if (!slotHit.Success || !capHit.Success)
            {
                continue; // 不是「场景 | 招式槽 | 内功容量」那种行
            }

            rows += 1;
            var level = int.Parse(levelHit.Groups[1].Value);
            var wuHit = wuRegex.Match(label);
            var genHit = genRegex.Match(label);
            var wu = wuHit.Success ? int.Parse(wuHit.Groups[1].Value) : baseWu;
            var gen = genHit.Success ? int.Parse(genHit.Groups[1].Value) : baseGen;
            var attrs = new Godot.Collections.Dictionary { { "wu", wu }, { "gen", gen } };
            CheckEq(
                int.Parse(slotHit.Groups[1].Value),
                growth.active_slots(level, attrs),
                $"01 采样表「{label.Trim()}」的招式槽与公式一致（悟 {wu}／根 {gen}）");
            CheckEq(
                int.Parse(capHit.Groups[1].Value),
                growth.passive_capacity(level, attrs),
                $"01 采样表「{label.Trim()}」的内功容量与公式一致（悟 {wu}／根 {gen}）");
        }

        CheckGt(rows, 3.0, $"01 的槽位采样表至少解析出 4 行（{rows} 行）");
    }
    /// <summary>
    /// 40. 「现状类文档」里不许写死总命令的步数（政策已写在文档里，缺的是**有人盯着**）。
    /// 这条门限扫 `CurrentStateDocs` 里的 `数字+步`，发现就红，并告诉怎么改（写成"看末尾 `passed steps`"）。
    /// </summary>
    private void CheckNoHardcodedStepCounts()
    {
        var stepRe = new Regex("\\d+\\s*步");
        var scanned = 0;
        foreach (var path in CurrentStateDocs)
        {
            var text = FileAccess.GetFileAsString($"res://{path}");
            CheckFalse(text.Length == 0, $"读得到 {path}");
            scanned += 1;
            foreach (Match hit in stepRe.Matches(text))
            {
                Fail(
                    $"{path} 里写死了总命令的步数（「{hit.Value}」）：加一步它就变成假话——"
                    + "改成「看末尾 `passed steps`」");
            }
        }

        CheckEq(scanned, CurrentStateDocs.Length, $"现状类文档都读到了（{scanned} 份）");
    }

    /// <summary>
    /// 48. `check_loop` 的「段数」：代码里的段标记是唯一出处，两份文档不许各写一个数。
    ///
    /// 由来（2026-10-03）：看板与工具表都写着"**10 段**真引擎"，而 `check_loop.gd` 里只有 **9 个**
    /// 段标记；更糟的是 **⑥ 那行注释是复制粘贴错的**——它写着"存档 → 读档"，下面调的却是副本巡游。
    /// 数字对不上、注释说错，两处都没有东西会红。
    /// **叙述里引用旧值时别用粗体**：这条门限把粗体的 `**N 段**` 当成"声称当前段数"。
    ///
    /// 2026-10-11（第四十七批）：闭环自检搬成 C#（`tools/CheckLoopRunner.cs`）——段标记从
    /// `# ---------- ①` 变成 `// ---------- ①`，扫描面与模式一起换；只认带圈号的那一行
    /// （同文件里还有 `// ------ 段` 这种分区线，别把它们数成段）。
    /// </summary>
    private void CheckLoopSegmentCount()
    {
        var loop = FileAccess.GetFileAsString("res://tools/CheckLoopRunner.cs");
        CheckFalse(loop.Length == 0, "读得到 CheckLoopRunner.cs");
        var segments = Regex.Matches(loop, "(?m)^\\s*// -{5,} [①②③④⑤⑥⑦⑧⑨⑩⑪⑫]").Count;
        CheckGt(segments, 5.0, $"check_loop 扫到足够多的段标记（{segments} 段）");

        var claimRe = new Regex("\\*\\*(\\d+)\\s*段");
        var claims = 0;
        foreach (var path in new[] { StatusPath, "res://docs/dev/框架说明.md" })
        {
            var text = FileAccess.GetFileAsString(path);
            foreach (Match hit in claimRe.Matches(text))
            {
                claims += 1;
                CheckEq(
                    int.Parse(hit.Groups[1].Value),
                    segments,
                    $"{System.IO.Path.GetFileName(path)} 写「{hit.Value}」，而 check_loop 里有 {segments} 个段标记");
            }
        }

        CheckGt(claims, 0.0, $"至少在一份文档里写明了闭环段数（扫到 {claims} 处）");
    }

    /// <summary>
    /// 48. 看板里写死的「特殊效果 id 共 N 条」必须等于数据里的真实条数。
    ///
    /// 由来（2026-10-04）：这条一直写「共 8 条：装备 4 件＋内功 4 部」，而验收里的 `[效果]` 警告
    /// **每次都现数**——实际是装备 6 ＋ 内功 5 ＝ **11**。**表格在长，填数字的地方不长**：
    /// 这类"文档引用一个能算出来的数"最容易被漏，而它偏偏写在给策划看的那一行上。
    /// 口径：数字从**数据**现算；措辞被改掉也要红——**别让声称悄悄消失**。
    /// </summary>
    private void CheckSpecialEffectCountClaim()
    {
        var db = new TableDb();
        db.load_all();
        var equipRows = 0;
        foreach (Variant entry in db.rows("equip_base"))
        {
            if (entry.AsGodotObject().Get("special_effect").AsString().Trim().Length > 0)
            {
                equipRows += 1;
            }
        }

        var passiveRows = 0;
        foreach (Variant entry in db.rows("skill_passive"))
        {
            if (entry.AsGodotObject().Get("passive_effect").AsString().Trim().Length > 0)
            {
                passiveRows += 1;
            }
        }

        var actual = equipRows + passiveRows;
        CheckGt(actual, 5.0, $"数据里填了效果 id 的条数（装备 {equipRows} ＋ 内功 {passiveRows} ＝ {actual}）");

        var status = FileAccess.GetFileAsString(StatusPath);
        var claimRe = new Regex("「特殊效果 id」全是悬空的\\*{0,2}（\\*{0,2}共 (\\d+) 条");
        var hit = claimRe.Match(status);
        CheckTrue(hit.Success, "看板里那行写了「特殊效果 id 全是悬空的（共 N 条）」（措辞改了也要连着改这条门限）");
        if (hit.Success)
        {
            CheckEq(
                int.Parse(hit.Groups[1].Value),
                actual,
                $"看板写「共 {hit.Groups[1].Value} 条」，数据里是 {actual} 条（装备 {equipRows} ＋ 内功 {passiveRows}）"
                + "——行数变了就同步这一句");
        }

        // 同一句话在「设计实现对照」里还有一份（`B2` 那张「等设计补」表写着「共 N 条悬空 id」）——
        // 它一直停在 8，复核订正到 11 之后两边要一起走，否则接手的人会照旧数错（决策 318／319 的续）。
        var contrast = FileAccess.GetFileAsString(ContrastPath);
        CheckFalse(contrast.Length == 0, $"读得到 {ContrastPath}");
        var contrastHit = new Regex("共\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*条悬空 id").Match(contrast);
        CheckTrue(contrastHit.Success, "对照表里写了「共 N 条悬空 id」（措辞改了也要连着改这条门限）");
        if (contrastHit.Success)
        {
            CheckEq(
                int.Parse(contrastHit.Groups[1].Value),
                actual,
                $"对照表写「共 {contrastHit.Groups[1].Value} 条悬空 id」，数据里是 {actual} 条"
                + $"（装备 {equipRows} ＋ 内功 {passiveRows}）");
        }
    }
    /// <summary>
    /// 35. 「当前状态」看板与「设计实现对照」里写死的表数／行数，必须与真表一致。
    ///
    /// 由来（2026-10-03）：那一行写着「37 张表 / 566 行」——**同一行里还写着"条数以运行输出为准，
    /// 别在这里写死"，而它自己就是写死的**。183 治的是 Q 编号、182 治的是代码里的 id 清单，
    /// 这是同一家族的第三处：**文档里的动态数字**。
    /// 2026-10-04 扩到第二份文档（`设计实现对照.md` 那句一直停在「37 张表」，而它偏偏是接手的
    /// 人拿来问"还缺什么"的那份清单，头一句就写着过期的规模，比不写更糟）。
    /// 数字从 `TableDb`（验收前刚由构建期重建过）现算，与 build_tables 打印的「N 张表 / M 行」同源。
    /// </summary>
    private void CheckDashboardNumbers()
    {
        var db = new TableDb();
        db.load_all();
        CheckEq(db.errors.Length, 0, $"TableDb 能载全（{string.Join("; ", db.errors)}）");
        var tableCount = db.tables.Count;
        CheckGt(tableCount, 20.0, $"载到足够多的表（{tableCount} 张）");
        var rowCount = 0;
        foreach (Variant key in db.tables.Keys)
        {
            var table = db.tables[key].As<TableResource>();
            rowCount += table?.rows.Count ?? 0;
        }

        var claimRe = new Regex("(\\d+)\\s*张表\\s*/\\s*(\\d+)\\s*行");
        foreach (var path in new[] { StatusPath, ContrastPath })
        {
            CheckTableCountClaim(path, claimRe, tableCount, rowCount);
        }

        CheckCharacterBaseRowClaim(db);
    }

    /// <summary>「N 张表 / M 行」这句在两份文档里各要出现一次，且数字都要等于实载。</summary>
    private void CheckTableCountClaim(string path, Regex claimRe, int tableCount, int rowCount)
    {
        var text = FileAccess.GetFileAsString(path);
        CheckFalse(text.Length == 0, $"读得到 {path}");
        var hits = claimRe.Matches(text);
        CheckGt(hits.Count, 0.0, $"{path} 里写明了「N 张表 / M 行」（扫到 {hits.Count} 处）");
        foreach (Match hit in hits)
        {
            CheckEq(
                int.Parse(hit.Groups[1].Value),
                tableCount,
                $"{path} 写的表数 {hit.Groups[1].Value} 与实载 {tableCount} 张不符");
            CheckEq(
                int.Parse(hit.Groups[2].Value),
                rowCount,
                $"{path} 写的行数 {hit.Groups[2].Value} 与实载 {rowCount} 行不符（加／删表行后要同步这句）");
        }
    }

    /// <summary>
    /// 「设计实现对照」里还有一处写死表行数的地方（01 那格的 `character_base` N 行）。
    /// 它一直写着 1，而 0.9.0 起那张表就是 5 行——同一家族、同一手法量。
    /// </summary>
    private void CheckCharacterBaseRowClaim(TableDb db)
    {
        var contrast = FileAccess.GetFileAsString(ContrastPath);
        CheckFalse(contrast.Length == 0, $"读得到 {ContrastPath}");
        var hit = new Regex("`character_base`\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*行").Match(contrast);
        CheckTrue(hit.Success, "对照表里写了「`character_base` N 行」（措辞改了也要连着改这条门限）");
        if (!hit.Success)
        {
            return;
        }

        var actual = db.rows("character_base").Count;
        CheckEq(
            int.Parse(hit.Groups[1].Value),
            actual,
            $"对照表写 character_base {hit.Groups[1].Value} 行，数据里是 {actual} 行");
    }

    /// <summary>
    /// 56. 设计侧「交接单」里的表数与逐表行数，必须与真表一致。
    ///
    /// 由来（2026-10-04）：`docs/design/交接单.md` 是设计交付的**汇总页**，开头就写着
    /// 「程序从这里看就够了」——可它 §一 那句一直停在 **0.23.0 的「54 张表 / 848 行」**，
    /// 现实是 **64 张 / 1023 行**。**读它的人照 §一 去核对会白找**。
    /// 口径：只扫**计数**写法——「N 张表 / M 行」与 `` `表名`（N 行… ``；数字从 `TableDb` 现算。
    /// 「＋1 行」「3 处」这类**改动量**不在此列（那是当时的 diff，不是现状）。
    /// **一段都扫不到也要红**：别让声称悄悄消失。这一份**归设计侧**，可以换措辞——但只要写了，就必须是真的。
    /// </summary>
    private void CheckHandoverSheetNumbers()
    {
        var text = FileAccess.GetFileAsString(HandoverPath);
        CheckFalse(text.Length == 0, $"读得到 {HandoverPath}");
        if (text.Length == 0)
        {
            return;
        }

        var db = new TableDb();
        db.load_all();
        CheckEq(db.errors.Length, 0, $"TableDb 能载全（{string.Join("; ", db.errors)}）");
        var tableCount = db.tables.Count;
        var rowCount = 0;
        foreach (Variant key in db.tables.Keys)
        {
            var table = db.tables[key].As<TableResource>();
            rowCount += table?.rows.Count ?? 0;
        }

        var scanned = 0;
        foreach (Match hit in new Regex("(\\d+)\\s*张表\\s*/\\s*(\\d+)\\s*行").Matches(text))
        {
            scanned += 1;
            CheckEq(
                int.Parse(hit.Groups[1].Value),
                tableCount,
                $"交接单写的表数 {hit.Groups[1].Value} 与实载 {tableCount} 张不符（加／删表后要同步这一句）");
            CheckEq(
                int.Parse(hit.Groups[2].Value),
                rowCount,
                $"交接单写的行数 {hit.Groups[2].Value} 与实载 {rowCount} 行不符（加／删表行后要同步这一句）");
        }

        foreach (Match hit in new Regex("`([a-z][a-z0-9_]*)`\\s*（\\s*(\\d+)\\s*行").Matches(text))
        {
            var name = hit.Groups[1].Value;
            if (!db.has_table(name))
            {
                Fail($"交接单把 `{name}` 当表名写着行数，但 data/tables 里没有这张表");
                continue;
            }

            var actual = db.rows(name).Count;
            scanned += 1;
            CheckEq(
                int.Parse(hit.Groups[2].Value),
                actual,
                $"交接单写 `{name}` 有 {hit.Groups[2].Value} 行，数据里是 {actual} 行");
        }

        CheckGt(scanned, 0.0, "交接单里扫到了计数声称（一句都没有的话，这条门限就白设了）");
    }
    /// <summary>
    /// 52. 「设计实现对照」里两处**能从数据算出来的**数字：判定条数、武学来源未实现的比例。
    ///
    /// 由来（2026-10-04，决策 336）：B0 那节写着「16 / **52** 部武学」——`skill_base` 早就是 65 行；
    /// 「判定不要卡在主线必经路上」那行写着「**12** 条判定」，实际有 14 条。
    /// 口径：只扫**现状文档**（`设计实现对照.md`），并跳过**历史叙事**的行（含「以前／当时／当年」）——
    /// 那些是回放，拿现状口径去量它会假红（同决策 183 对 `框架说明.md` 的处理）。
    /// </summary>
    private void CheckContrastAuditNumbers()
    {
        var db = GetDb();
        var checkRows = db.rows("event_check").Count;
        var skillTotal = db.rows("skill_base").Count;
        var skillPending = 0;
        foreach (Variant entry in db.rows("skill_base"))
        {
            var source = entry.AsGodotObject().Get("source_type").AsString();
            if (source == "npc" || source == "shop")
            {
                skillPending += 1;
            }
        }

        CheckGt(checkRows, 10.0, $"载到足够多的判定（{checkRows} 条）");
        CheckGt(skillTotal, 40.0, $"载到足够多的武学（{skillTotal} 部）");
        var text = FileAccess.GetFileAsString(ContrastPath);
        CheckFalse(text.Length == 0, $"读得到 {ContrastPath}");

        var claimRe = new Regex("(\\d+)\\s*条判定");
        var ratioRe = new Regex("(\\d+)\\s*/\\s*(\\d+)\\s*部武学");
        var checkClaims = 0;
        var ratioClaims = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            foreach (Match hit in claimRe.Matches(line))
            {
                // **历史叙事不拿现状口径去量**：同一行里这个数**前面**已经写了「以前／当时／当年」
                // 就跳过（表格行常常前半句是现状、后半句是回放，整行跳过会把现状那半也漏掉）。
                if (LooksHistorical(line.Substring(0, hit.Index)))
                {
                    continue;
                }

                checkClaims += 1;
                CheckEq(
                    int.Parse(hit.Groups[1].Value),
                    checkRows,
                    $"对照表写「{hit.Groups[1].Value} 条判定」，event_check 里是 {checkRows} 条——数变了就同步这一句");
            }

            foreach (Match hit in ratioRe.Matches(line))
            {
                if (LooksHistorical(line.Substring(0, hit.Index)))
                {
                    continue;
                }

                ratioClaims += 1;
                CheckEq(
                    int.Parse(hit.Groups[1].Value),
                    skillPending,
                    $"对照表写「{hit.Groups[1].Value} / … 部武学」，而 `source_type` 还是 npc／shop 的是"
                    + $" {skillPending} 部（分子变了要跟着改）");
                CheckEq(
                    int.Parse(hit.Groups[2].Value),
                    skillTotal,
                    $"对照表写「… / {hit.Groups[2].Value} 部武学」，而 `skill_base` 有 {skillTotal} 行（分母变了要跟着改）");
            }
        }

        CheckGt(checkClaims, 0.0, "对照表里写了「N 条判定」（措辞改了也要连着改这条门限）");
        CheckGt(ratioClaims, 0.0, "对照表里写了「N / M 部武学」（措辞改了也要连着改这条门限）");
    }
    /// <summary>
    /// 36. 场景自检的「检查条数棘轮」：带自检的场景个数、每个场景的检查条数与下限、文档里写的个数，
    /// 三样互相对账（删检查没人会红是当年真踩过的坑；余量太大同样是坑，见 `VerificationFloorSlack`）。
    ///
    /// 另有一条：文档里写的 `N 个真实场景自检` 也得对得上——场景自检加一个，那两句就变成假话，
    /// 而它们写在**交接文档**和**一页看板**上，最容易被当真（README 也收进来：它是仓库的门面）。
    /// </summary>
    private void CheckVerificationCheckFloors()
    {
        var selftestScenes = new List<string>();
        foreach (var path in CollectFiles(new[] { "src" }))
        {
            // C# 迁移期：场景自检从 `.gd` 搬到 `.cs` 时这一条也得跟着认——`src` 下这两个后缀都在
            // `TextExtensions` 里，别让「搬一个自检场景」把这条**计数**门限打成假红。
            if (!path.EndsWith(".gd", StringComparison.Ordinal) && !path.EndsWith(".cs", StringComparison.Ordinal))
            {
                continue;
            }

            var source = FileAccess.GetFileAsString($"res://{path}");
            // GDScript 写 `print("… SELF-TEST: %s" % …)`；C# 是插值字符串，留的是 `SELF-TEST: {`。
            if (source.Contains("SELF-TEST: %s", StringComparison.Ordinal)
                || source.Contains("SELF-TEST: {", StringComparison.Ordinal))
            {
                selftestScenes.Add(path);
            }
        }

        CheckEq(
            selftestScenes.Count,
            13,
            $"带场景自检的文件数没变（新加一个就要在棘轮表里登记一行）：{string.Join(", ", selftestScenes)}");
        foreach (var path in selftestScenes)
        {
            CheckTrue(
                VerificationCheckFloors.ContainsKey(path),
                $"{path} 有场景自检，但棘轮表里没有它的检查条数下限");
        }

        foreach (var (path, spec) in VerificationCheckFloors)
        {
            var text = FileAccess.GetFileAsString($"res://{path}");
            CheckFalse(text.Length == 0, $"读得到 {path}");
            // 用**字面量计数**，不走正则：pattern 里有 `(` 这类字符时，正则写错（少个反斜杠）
            // 会静默数出 0 条，反而把门限变成假红／假绿（第一次就踩了）。
            var count = CountOccurrences(text, spec.Pattern);
            CheckTrue(
                count >= spec.Floor,
                $"{path} 的检查只剩 {count} 条（下限 {spec.Floor}，数的是「{spec.Pattern}」= {spec.Why}）"
                + "——少的哪条？删检查要连着改棘轮表并写明理由");
            // **余量也不能太大**（2026-10-04 补）：原先只查「不低于下限」，于是"加了检查却忘了
            // 更新棘轮表"会让下限慢慢变成摆设——实测 15 个文件全部漂了 1～11 条。
            // 口径同 `MinAssertions` 的余量上限：**加了检查就把它贴上去**，而不是把上限抬高。
            CheckTrue(
                count <= spec.Floor + VerificationFloorSlack,
                $"{path} 的检查涨到 {count} 条、棘轮表还写着 {spec.Floor}——把 floor 贴到 {count}"
                + $"（余量上限 {VerificationFloorSlack}）");
        }

        var countRe = new Regex("(\\d+)\\s*个真实场景自检");
        var docsChecked = 0;
        foreach (var path in new[] { "AGENTS.md", "docs/dev/当前状态.md", "README.md" })
        {
            var text = FileAccess.GetFileAsString($"res://{path}");
            foreach (Match hit in countRe.Matches(text))
            {
                docsChecked += 1;
                CheckEq(
                    int.Parse(hit.Groups[1].Value),
                    selftestScenes.Count,
                    $"{path} 里的「{hit.Value}」与实际自检场景数（{selftestScenes.Count} 个）不符——加了自检场景就要同步这句");
            }
        }

        CheckGt(docsChecked, 0.0, $"至少在一份文档里写明了真实场景自检的个数（扫到 {docsChecked} 处）");
    }
}
