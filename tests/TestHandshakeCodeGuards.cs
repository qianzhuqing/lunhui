using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 代码契约那一族的第二半（仓库约定／单一定义／只被测试调用的接口／随机种子／存档字段）。
/// 与 `TestHandshakeCode.cs` 是同一个类的两份 `partial`——拆开只为不撞 800 行硬上限，
/// 注册进 `tools/RunTestsCs.cs` 的是主文件那一份。
/// </summary>
public partial class TestHandshakeCode
{
    /// <summary>
    /// 43. 用例里不许出现「没给种子的随机源」：写了无参构造，断言就会**时红时绿**——
    /// 红起来查不出原因，绿起来也不算数。要用固定种子。
    /// **两种语言都扫**（用例搬 C# 之后只扫 `.gd` 会让这条网静默空掉）。
    /// 名单（`UnsafeRngPatterns`）里同时留着新旧两种写法：旧写法已经调不通了，
    /// 但留着才不会让这条门限「悄悄少管一种形状」。
    /// </summary>
    private void CheckTestsUseSeededRng()
    {
        var dir = DirAccess.Open("res://tests");
        if (dir == null)
        {
            Fail("打不开 tests/ 目录，无法检查随机源");
            return;
        }

        var scanned = 0;
        foreach (var fileName in dir.GetFiles())
        {
            if (!fileName.EndsWith(".gd", StringComparison.Ordinal) && !fileName.EndsWith(".cs", StringComparison.Ordinal))
            {
                continue;
            }

            var source = FileAccess.GetFileAsString($"res://tests/{fileName}");
            scanned += 1;
            foreach (var raw in source.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                // **先抹掉字符串字面量再搜**：否则名单里那几条模式常量会把门限自己判红
                // （第一次跑就是那样，三条红全指着自己）。调用不可能写在字符串里。
                var code = StripStringLiterals(raw);
                foreach (var pattern in UnsafeRngPatterns)
                {
                    if (code.Contains(pattern, StringComparison.Ordinal))
                    {
                        Fail($"{fileName} 里有没给种子的随机源 `{pattern}`：断言会时红时绿。改成 `RngService.create(SEED)`");
                    }
                }
            }
        }

        CheckGt(scanned, 30.0, $"扫到足够多的用例文件（{scanned} 个）");
    }

    /// <summary>去掉一行里的字符串字面量（含简单的 `\"` 转义），只留代码部分。</summary>
    private static string StripStringLiterals(string line)
    {
        var outBuilder = new System.Text.StringBuilder();
        var inString = false;
        var index = 0;
        while (index < line.Length)
        {
            var ch = line[index];
            if (inString)
            {
                if (ch == '\\' && index + 1 < line.Length)
                {
                    index += 2;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }
            }
            else if (ch == '"')
            {
                inString = true;
            }
            else
            {
                outBuilder.Append(ch);
            }

            index += 1;
        }

        return outBuilder.ToString();
    }
    /// <summary>
    /// 13. 工程入口与 autoload：`project.godot` 里这两处是**文档写明的不变量**，以前却没有任何门限在盯。
    /// 危险在于：把 `run/main_scene` 改成 bootstrap／占位场景，**所有用例照样绿**（场景自检都是按路径显式
    /// 启动的），而玩家双击游戏会进到诊断场景。
    /// </summary>
    private void CheckProjectEntryPoint()
    {
        var text = FileAccess.GetFileAsString("res://project.godot");
        CheckTrue(
            text.Contains("run/main_scene=\"res://scenes/main_menu.tscn\"", StringComparison.Ordinal),
            "主场景是启动菜单（bootstrap 是诊断场景，不是入口）");
        CheckTrue(text.Contains("GameData=\"*res://src/autoload/GameData.cs\"", StringComparison.Ordinal), "GameData 是 autoload");
        CheckTrue(text.Contains("GameSession=\"*res://src/autoload/GameSession.cs\"", StringComparison.Ordinal), "GameSession 是 autoload");
        CheckTrue(FileAccess.FileExists("res://src/autoload/GameData.cs"), "GameData 脚本存在");
        CheckTrue(FileAccess.FileExists("res://src/autoload/GameSession.cs"), "GameSession 脚本存在");
        CheckTrue(
            text.Contains("renderer/rendering_method=\"mobile\"", StringComparison.Ordinal),
            "渲染器仍是 mobile（换渲染器是正式变更：改这里要同时更新 AGENTS 与本条门限）");
    }

    /// <summary>
    /// 14. 仓库级硬约定也要有门限：① `src/` 不许直接读 `res://data/tables/`（CSV 是**构建期输入**）；
    /// ② `tools/*.bat` 必须纯 ASCII ＋ CRLF；③ `tools/*.ps1` 含非 ASCII 就必须带 UTF-8 BOM。
    /// 白名单只有一处：`TableRegistry` 的 `TABLES_DIR`——构建管线就是靠它去读 CSV 的；
    /// 白名单自己也会烂（文件被删／改名），所以顺手断言它仍然存在。
    /// </summary>
    private void CheckRepoConventions()
    {
        var srcFiles = CollectFiles(new[] { "src" });
        CheckGt(srcFiles.Count, 20.0, $"能扫到 src 下的文件（{srcFiles.Count} 个）");
        foreach (var path in srcFiles)
        {
            var text = FileAccess.GetFileAsString($"res://{path}");
            if (Array.IndexOf(CsvDirWhitelist, path) < 0)
            {
                CheckFalse(
                    text.Contains("res://data/tables", StringComparison.Ordinal),
                    $"{path} 提到了 res://data/tables（运行期只读 data/generated；构建期白名单见 CSV_DIR_WHITELIST）");
            }
        }

        foreach (var path in CsvDirWhitelist)
        {
            CheckTrue(FileAccess.FileExists($"res://{path}"), $"CSV 目录白名单里的 {path} 不存在了");
        }

        var batCount = 0;
        var ps1Count = 0;
        foreach (var fileName in DirAccess.GetFilesAt("res://tools"))
        {
            if (fileName.EndsWith(".bat", StringComparison.Ordinal))
            {
                batCount += 1;
                AuditBat($"tools/{fileName}");
            }
            else if (fileName.EndsWith(".ps1", StringComparison.Ordinal))
            {
                ps1Count += 1;
                AuditPs1($"tools/{fileName}");
            }
        }

        CheckGt(batCount, 3.0, $"扫到足够多的 .bat（{batCount} 个）");
        CheckGt(ps1Count, 0.0, $"扫到 .ps1（{ps1Count} 个）");
    }

    /// <summary>
    /// 16. 「同一事实只许有一处定义」——只收那些**漂移了不会报错、只会静默丢东西**的常量
    /// （见基类 `SingleSourceRules`：`LOCKED_SCENES` 曾在两个文件里各写一份；贡献 kind 字符串
    /// 写错一个字母会被 `AttributeCalculator` 的 `!=` 静默跳过）。**两种语言都算**：搬成 C# 之后
    /// 定义处就在 `.cs` 里，只扫 `.gd` 会让规则整个失效。
    /// </summary>
    private void CheckSingleSourceOfTruth()
    {
        var files = CollectFiles(new[] { "src" });
        CheckGt(files.Count, 20.0, $"能扫到 src 下的文件（{files.Count} 个）");
        foreach (var rule in SingleSourceRules)
        {
            var label = rule.Label;
            var owner = rule.Owner;
            var allowed = new List<string> { owner };
            allowed.AddRange(rule.Also);
            var hits = new List<string>();
            foreach (var path in files)
            {
                if (!path.EndsWith(".gd", StringComparison.Ordinal) && !path.EndsWith(".cs", StringComparison.Ordinal))
                {
                    continue;
                }

                if (FileAccess.GetFileAsString($"res://{path}").Contains(label, StringComparison.Ordinal))
                {
                    hits.Add(path);
                }
            }

            CheckGt(hits.Count, 0.0, $"「{label}」至少出现在一处（规则不是空的）");
            CheckTrue(hits.Contains(owner), $"「{label}」仍在定义处 {owner}");
            foreach (var path in hits)
            {
                CheckTrue(
                    allowed.Contains(path),
                    $"「{label}」在 {path} 里又写了一份（定义处是 {owner}）：{rule.Why}");
            }

            CheckTrue(FileAccess.FileExists($"res://{owner}"), $"规则里的定义处 {owner} 不存在了");
            foreach (var path in rule.Also)
            {
                CheckTrue(FileAccess.FileExists($"res://{path}"), $"白名单里的 {path} 不存在了，把这条删掉");
            }
        }
    }
    /// <summary>
    /// 10. 新加的公共接口要真的接进游戏路径——「定义了但 `src/`＋`scenes/` 里没人调用」的函数会被这里抓住
    /// （`_` 开头的跳过），只在白名单 `TestOnlyApiAllowed` 里才放行。
    /// 真出过这类坑：图鉴奖励写好却只有用例在调，游戏里收集再多也不涨属性。
    /// **两种语言的定义都算**：`.cs` 认 `public 类型 方法名(`——不跟上这一条，搬走的接口会在这道门限里
    /// 凭空消失，而新写进 `.cs` 的「游戏路径从不调用」的接口又完全不被拦。
    /// 白名单**两向维护**：接口删了／改名了要红，一旦被接进游戏路径也要红（留着会让"以后再断掉调用"没人发现）。
    /// </summary>
    private void CheckNoNewTestOnlyApi()
    {
        var src = CollectFiles(new[] { "src" });
        CheckGt(src.Count, 20.0, $"能扫到 src 下的文件（{src.Count} 个）");
        var corpusBuilder = new System.Text.StringBuilder();
        var files = CollectFiles(new[] { "src" });
        files.AddRange(CollectFiles(new[] { "scenes" }));
        files.Add("project.godot");
        foreach (var path in files)
        {
            corpusBuilder.Append(FileAccess.GetFileAsString($"res://{path}"));
        }

        var corpus = corpusBuilder.ToString();
        var defRegex = new Regex("(?m)^(?:static\\s+)?func\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\(");
        // `(?:static\s+)?` 是 2026-10-11（P5）补的：以前只认 `public <类型> 名字(`，于是
        // **`public static bool foo()` 在这道门里整个隐形**——真写了个「游戏路径从不调用」的静态接口
        // 也拦不住（搬 C# 时一批 `public static` 的取值口／查询口就是这么溜过去的）。
        // 补上之后当场报出 9 条存量，逐条核过再写进白名单（见 `TestOnlyApiAllowed`）。
        var csDefRegex = new Regex("(?m)^\\s*public\\s+(?:static\\s+)?[A-Za-z0-9_\\.\\[\\]<>]+\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\(");

        // 「游戏路径里有调用」的判据是「这个名字在语料里不止出现一次」。GDScript 侧数的是 PCRE2 的
        // `\b`——**ASCII** 词边界（汉字不算词字符）；.NET 默认的 `\b` 把汉字也算进 `\w`，
        // 挨着汉字的标识符会数不到，跟老链对不上。所以这里按同一口径来：把语料里的 ASCII 标识符
        // 切开数一遍，一次扫描代替「每个名字扫全篇」。
        var identifierCounts = new Dictionary<string, int>();
        foreach (Match token in Regex.Matches(corpus, "[A-Za-z_][A-Za-z0-9_]*"))
        {
            var key = token.Value;
            identifierCounts[key] = identifierCounts.GetValueOrDefault(key) + 1;
        }

        var foundNames = new List<string>();
        var definedNames = new List<string>();
        var unexpected = new List<string>();
        foreach (var path in src)
        {
            if (!path.EndsWith(".gd", StringComparison.Ordinal) && !path.EndsWith(".cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = FileAccess.GetFileAsString($"res://{path}");
            var scanner = path.EndsWith(".cs", StringComparison.Ordinal) ? csDefRegex : defRegex;
            foreach (Match found in scanner.Matches(text))
            {
                var name = found.Groups[1].Value;
                if (name.StartsWith("_", StringComparison.Ordinal))
                {
                    continue;
                }

                definedNames.Add(name);
                if (identifierCounts.GetValueOrDefault(name) > 1)
                {
                    continue; // 游戏路径里有调用，正常
                }

                foundNames.Add(name);
                if (!TestOnlyApiAllowed.ContainsKey(name))
                {
                    unexpected.Add($"{name}（{path}）");
                }
            }
        }

        CheckGt(foundNames.Count, 20.0, $"扫到足够多的「只被测试调用」接口（{foundNames.Count} 个）");
        CheckEq(
            unexpected.Count,
            0,
            $"新增了「游戏路径从不调用」的接口：{string.Join(", ", unexpected)} —— 接进游戏路径，或把名字与理由写进 TEST_ONLY_API_ALLOWED");

        // 白名单自己也会烂：接口删了／改名了就把那行一起删。判据是"这个接口在 src/ 里还有没有定义"——
        // 按上面那份**两语言都算**的清单比，别再去字符串里找 `func 名字(`（`.cs` 定义会漏）。
        foreach (var name in TestOnlyApiAllowed.Keys)
        {
            if (!definedNames.Contains(name))
            {
                Fail($"白名单里的 {name} 在 src/ 里已经找不到定义了，把这条删掉（理由：{TestOnlyApiAllowed[name]}）");
            }
            else if (!foundNames.Contains(name))
            {
                // 反方向：白名单里记的是"游戏路径从不调用"的接口，一旦它**被接进游戏**了，这条就不再需要。
                Fail($"TEST_ONLY_API_ALLOWED 里的 {name} 已经接进游戏路径了（现在数得到调用方），把这条删掉");
            }
        }

        GD.Print($"  · 只被测试调用的接口：{foundNames.Count} 个（都在白名单里）");
    }
    /// <summary>
    /// 13b. 用例里造的**战斗界面**必须固定随机种子（和上一条同一类洞，但这条按"造战斗界面"的位置找）。
    ///
    /// 起因（2026-10-03 实打实抓到的一次 flaky）：`test_skill_grant` 的真实战斗没设 `rng_seed`，
    /// 而 `BattleScreen.rng_seed` 默认是 -1（见负数就 `randomize()`）——掉落件数是掷出来的，于是
    /// 「结算卡片里有没有那一行」时红时绿。所以把它变成门限：造战斗界面的地方，**往下 12 行内**
    /// 必须出现 `rng_seed`。
    ///
    /// 2026-10-11：战斗用例整批搬去 C# 之后本链 `.gd` 侧一处不剩，于是**同时扫两种语言**
    /// （`.cs` 认 `.Instantiate()` ＋ 同一行提到 `battle_screen`／`BattleScene`），并把「扫到了几处」
    /// 的下界贴到实测值，免得它随着搬家静默缩成一格空网。
    /// </summary>
    private void CheckBattleTestsAreSeeded()
    {
        var dir = DirAccess.Open("res://tests");
        if (dir == null)
        {
            Fail("打不开 tests/ 目录，无法检查战斗用例的随机种子");
            return;
        }

        var scanned = 0;
        foreach (var fileName in dir.GetFiles())
        {
            var isGd = fileName.EndsWith(".gd", StringComparison.Ordinal);
            if (!isGd && !fileName.EndsWith(".cs", StringComparison.Ordinal))
            {
                continue;
            }

            // 这条门限自己的模式串就在下面两个文件里，别把自己数进去。
            if (fileName == "test_handshake_code.gd" || fileName == "TestHandshakeCodeGuards.cs"
                || fileName == "TestHandshakeCode.cs")
            {
                continue;
            }

            var lines = FileAccess.GetFileAsString($"res://tests/{fileName}").Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var isSite = false;
                if (isGd)
                {
                    isSite = line.Contains("BATTLE_SCENE).instantiate()", StringComparison.Ordinal);
                }
                else if (line.Contains(".Instantiate()", StringComparison.Ordinal)
                    && (line.Contains("battle_screen", StringComparison.Ordinal)
                        || line.Contains("BattleScene", StringComparison.Ordinal)))
                {
                    isSite = true;
                }

                if (!isSite)
                {
                    continue;
                }

                scanned += 1;
                var windowBuilder = new System.Text.StringBuilder();
                for (var offset = index; offset < Math.Min(index + 12, lines.Length); offset++)
                {
                    windowBuilder.Append(lines[offset]).Append('\n');
                }

                CheckTrue(
                    windowBuilder.ToString().Contains("rng_seed", StringComparison.Ordinal),
                    $"{fileName} 第 {index + 1} 行造了战斗界面但没固定 rng_seed（掉落/暴击随机 → 断言时红时绿）");
            }
        }

        CheckGt(scanned, 2.0, $"确实扫到了战斗界面用例（{scanned} 处）");
    }

    /// <summary>
    /// 65. 存档字段必须进 `to_dict()`，`to_dict()` 写进去的键也必须有人在读——两个方向都堵上。
    ///
    /// 由来（2026-10-03）：`GameState` 上十几个字段漏进 `to_dict()` 是**静默丢档**；2026-10-03 又补了
    /// 反向（"存了却回不来"是同一个玩家症状的另一半），并把取键的范围收成 `to_dict()` 的**函数体**
    /// （按花括号配平），免得"某个字段其实没写、但名字恰好在后面某个字面量里出现过"蒙混过去。
    ///
    /// **扫描面必须连 `also` 那几份 `partial` 一起收**：`GameState` 拆成五份之后字段与读档代码散在
    /// 同类的其它文件里，收窄了这道门限就会静默退化（施工单「二点七」那一类坑）。
    /// </summary>
    private void CheckSaveFieldsAreSerialized()
    {
        foreach (var target in SaveSerializationTargets)
        {
            var path = target.Path;
            var exempt = target.Exempt;
            var csharp = target.CSharp;
            var source = FileAccess.GetFileAsString(path);
            var extraSources = new List<string>();
            foreach (var extra in target.Also)
            {
                var extraSource = FileAccess.GetFileAsString(extra);
                CheckFalse(extraSource.Length == 0, $"读得到 {extra}");
                extraSources.Add(extraSource);
            }

            CheckFalse(source.Length == 0, $"读得到 {path}");
            if (source.Length == 0)
            {
                continue;
            }

            // 扫描面 = 主文件 ＋ `also` 那几份 partial（`to_dict()` 与字段可能不在同一份里）。
            var scanBuilder = new System.Text.StringBuilder(source);
            foreach (var extraSource in extraSources)
            {
                scanBuilder.Append('\n').Append(extraSource);
            }

            var scan = scanBuilder.ToString();
            var toDictStart = scan.IndexOf("to_dict", StringComparison.Ordinal);
            CheckGt(toDictStart, 0.0, $"{path} 里有 to_dict()");
            if (toDictStart < 0)
            {
                continue;
            }

            var toDictBody = csharp ? ToDictBodyCs(scan) : ToDictBody(scan);
            CheckFalse(toDictBody.Length == 0, $"{System.IO.Path.GetFileName(path)} 的 to_dict() 函数体能取出来（花括号配平）");
            if (toDictBody.Length == 0)
            {
                continue;
            }

            var declared = new List<string>();
            var keyRegex = new Regex("\"([a-z_0-9]+)\"\\s*,");
            if (csharp)
            {
                // C#：字段是 `public X 名字 { get; set; }`（`[Export]` 写在上一行），键写成 `{ "名字", 值 }`。
                var fieldRegex = new Regex("(?m)^\\s*(?:\\[Export\\]\\s*)?public\\s+[A-Za-z0-9_.<>\\[\\]]+\\s+([a-z_0-9]+)\\s*\\{");
                var candidates = new List<string> { source };
                candidates.AddRange(extraSources);
                foreach (var candidate in candidates)
                {
                    foreach (Match hit in fieldRegex.Matches(candidate))
                    {
                        declared.Add(hit.Groups[1].Value);
                    }
                }
            }
            else
            {
                var varRegex = new Regex("(?m)^var\\s+([a-z_0-9]+)\\s*[:=]");
                foreach (Match hit in varRegex.Matches(source))
                {
                    declared.Add(hit.Groups[1].Value);
                }

                keyRegex = new Regex("\"([a-z_0-9]+)\"\\s*:");
            }

            var written = new HashSet<string>();
            foreach (Match hit in keyRegex.Matches(toDictBody))
            {
                written.Add(hit.Groups[1].Value);
            }

            CheckGt(declared.Count, 3.0, $"{System.IO.Path.GetFileName(path)} 扫到足够多的字段（{declared.Count} 个）");
            CheckGt(written.Count, 3.0, $"{System.IO.Path.GetFileName(path)} 的 to_dict() 里扫到足够多的键（{written.Count} 个）");
            foreach (var name in declared)
            {
                if (Array.IndexOf(exempt, name) >= 0)
                {
                    continue;
                }

                CheckTrue(
                    written.Contains(name),
                    $"{System.IO.Path.GetFileName(path)} 的字段 '{name}' 没写进 to_dict()：读档会静默丢（要么补上，要么加进白名单说明理由）");
            }

            // 反向：写进去的键，读档时得有人认——否则存了也回不来，而玩家看到的只是"东西没了"。
            var bodyIndex = scan.IndexOf(toDictBody, StringComparison.Ordinal);
            var outside = scan.Substring(0, bodyIndex) + scan.Substring(bodyIndex + toDictBody.Length);
            var orphans = new List<string>();
            foreach (var key in written)
            {
                if (!outside.Contains($"\"{key}\"", StringComparison.Ordinal))
                {
                    orphans.Add(key);
                }
            }

            CheckEq(
                orphans.Count,
                0,
                $"{System.IO.Path.GetFileName(path)} 的 to_dict() 写了 {string.Join(", ", orphans)}，但文件里再没人提这个键——读档时没人认它（存了却回不来）：补读档分支，或把键删掉");
        }
    }
}
