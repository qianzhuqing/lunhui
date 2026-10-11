using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace Lunhui.Tests;

/// <summary>
/// 策划对接（对接表／验证清单／版本对齐）——"策划对接守卫"的一族
/// （拆自 tests/test_handshake.gd，2026-10-04；2026-10-11 由 .gd 搬 C#）。
///
/// 拆分的规矩：**只挪位置、不改行为**。常量与共用夹具留在 `TestHandshake`（base），
/// 本族只放自己这一类的检查，逐条搬过来。加检查就写在对应族里；跨族要用某个函数先看它
/// 是不是该挪进 base。
/// </summary>
public partial class TestHandshakeContract : TestHandshake
{
    public override string SuiteName() => "策划对接（对接表／验证清单／版本对齐）";

    public override void Run()
    {
        var version = CurrentDesignVersion();
        CheckNe(version, "", "CHANGELOG.md 里应能找到「当前设计版本」");

        var contract = ReadCsv(ContractPath);
        var verifyRows = ReadCsv(VerifyPath);
        CheckGt(contract.Count, 0.0, "对接表应能解析出模块行");
        CheckGt(verifyRows.Count, 0.0, "验证清单应能解析出模块行");
        if (version.Length == 0 || contract.Count == 0 || verifyRows.Count == 0)
        {
            return;
        }

        var verifyByModule = new Dictionary<string, CsvRow>();
        foreach (var row in verifyRows)
        {
            verifyByModule[row.Get("module_id")] = row;
        }

        CheckDesignVersion(version, contract);
        CheckVerifyCoverage(version, contract, verifyByModule);
        CheckContractLinks(contract, verifyRows);
        CheckNoDuplicateModules(contract, verifyRows);
        CheckNoOrphanTests(verifyRows);
        CheckContractAssets(contract);
        CheckNoOrphanDesignDocs(contract);
        CheckVerifyTestsAreExecuted(verifyRows);
    }
    /// <summary>
    /// 10. 验证清单登记的用例必须真的在**执行列表**里。「文件存在」与「有人引用」前两条都查过了，
    /// 但**登记了却没进执行列表**的用例会永远不跑——文件在、也被引用了，那两个门限都抓不到它。
    ///
    /// **执行列表只有一份**：`tools/RunTestsCs.cs` 的 `Suites`（GDScript 那条链随 P4 收口退休，
    /// 2026-10-11）。迁移期两种语言各有一份清单，漏读一份会把另一边的用例判成「登记了却从不运行」
    /// ——2026-10-10 搬 attr_system 那批踩过；现在只剩 C# 这一份。
    /// </summary>
    private void CheckVerifyTestsAreExecuted(List<CsvRow> verifyRows)
    {
        var executed = new List<string>();
        var regex = new Regex("res://(tests/[^\"]+\\.(?:gd|cs))");
        var runner = FileAccess.GetFileAsString("res://tools/RunTestsCs.cs");
        foreach (Match found in regex.Matches(runner))
        {
            var path = found.Groups[1].Value;
            if (!executed.Contains(path))
            {
                executed.Add(path);
            }
        }

        CheckGt(executed.Count, 20.0, $"能读出执行列表（{executed.Count} 个用例）");
        foreach (var row in verifyRows)
        {
            var moduleId = row.Get("module_id");
            foreach (var path in Split(row.Get("verify_paths")))
            {
                if (!path.StartsWith("tests/", StringComparison.Ordinal))
                {
                    continue; // 只查 tests/ 下的用例；引擎级脚本（check_loop 等）另有入口与判定
                }

                CheckTrue(
                    executed.Contains(path),
                    $"{moduleId} 登记的用例 {path} 不在执行列表里（登记了却从不运行）");
            }
        }
    }

    /// <summary>
    /// 6. 对接表登记的 `code_paths` 与 `data_tables` 必须真实存在。
    /// 这两列是写给设计侧「去哪找」的——写错了比不写更糟（他们会照着找不到的东西来问，
    /// 或者以为某个模块早就动工了）。以前只校验了验证用例存在，这两列从来没查过。
    /// </summary>
    private void CheckContractAssets(List<CsvRow> contract)
    {
        foreach (var row in contract)
        {
            var moduleId = row.Get("module_id");
            foreach (var path in Split(row.Get("code_paths")))
            {
                CheckTrue(
                    FileAccess.FileExists($"res://{path}"),
                    $"{moduleId} 的 code_paths 引用了不存在的文件：{path}");
            }

            foreach (var table in Split(row.Get("data_tables")))
            {
                var fileName = table.EndsWith(".csv", StringComparison.Ordinal) ? table : $"{table}.csv";
                CheckTrue(
                    FileAccess.FileExists($"res://data/tables/{fileName}"),
                    $"{moduleId} 的 data_tables 引用了不存在的表：{table}");
            }

            foreach (var doc in Split(row.Get("design_docs")))
            {
                CheckTrue(
                    FileAccess.FileExists($"res://docs/design/{doc}"),
                    $"{moduleId} 的 design_docs 引用了不存在的文档：{doc}");
            }
        }
    }
    /// <summary>
    /// 7. 反向：`docs/design/` 下的每篇设计文档（README／CHANGELOG 除外）都应被某个模块引用。
    /// 一篇没人引用的设计文档，等于「没人负责的设计」——要么漏登记模块，要么文档该并进别处。
    /// </summary>
    private void CheckNoOrphanDesignDocs(List<CsvRow> contract)
    {
        var referenced = new HashSet<string>();
        foreach (var row in contract)
        {
            foreach (var doc in Split(row.Get("design_docs")))
            {
                referenced.Add(doc);
            }
        }

        var skip = new[] { "README.md", "CHANGELOG.md" };
        foreach (var fileName in DirAccess.GetFilesAt("res://docs/design"))
        {
            if (!fileName.EndsWith(".md", StringComparison.Ordinal) || Array.IndexOf(skip, fileName) >= 0)
            {
                continue;
            }

            CheckTrue(
                referenced.Contains(fileName),
                $"设计文档 {fileName} 没有被任何模块的 design_docs 引用（漏登记模块？）");
        }
    }

    /// <summary>
    /// 同一张表里 module_id 只能出现一次。重复行会被后面那行**悄悄盖掉**（两边都按 module_id 建索引），
    /// 结果就是一边写着「已做」、一边写着「未做」而自检完全看不出来。
    /// </summary>
    private void CheckNoDuplicateModules(List<CsvRow> contract, List<CsvRow> verifyRows)
    {
        var pairs = new (string Name, List<CsvRow> Rows)[] { ("模块对接表", contract), ("验证清单", verifyRows) };
        foreach (var (name, rows) in pairs)
        {
            var seen = new HashSet<string>();
            foreach (var row in rows)
            {
                var moduleId = row.Get("module_id");
                if (moduleId.Length == 0)
                {
                    continue;
                }

                CheckFalse(seen.Contains(moduleId), $"{name} 里 {moduleId} 重复出现（后者会盖掉前者）");
                seen.Add(moduleId);
            }
        }
    }

    /// <summary>1. 已动工的模块必须对齐当前设计版本。</summary>
    private void CheckDesignVersion(string version, List<CsvRow> contract)
    {
        foreach (var row in contract)
        {
            var status = row.Get("status");
            if (Array.IndexOf(StartedStatuses, status) < 0)
            {
                continue;
            }

            var moduleId = row.Get("module_id");
            var aligned = row.Get("design_version");
            if (aligned != version)
            {
                Fail(
                    $"{moduleId}（{status}）对齐的设计版本是 {aligned}，当前是 {version}："
                    + "需求变过了，先读 CHANGELOG 评估返工，再更新代码与验证");
            }
        }
    }
    /// <summary>2/3. 已动工的模块必须有验证用例，用例存在且对齐当前设计版本。</summary>
    private void CheckVerifyCoverage(string version, List<CsvRow> contract, Dictionary<string, CsvRow> verifyByModule)
    {
        foreach (var row in contract)
        {
            var status = row.Get("status");
            if (Array.IndexOf(MentionedStatuses, status) < 0)
            {
                continue;
            }

            var moduleId = row.Get("module_id");
            if (!verifyByModule.TryGetValue(moduleId, out var verifyRow))
            {
                Fail($"{moduleId} 已动工，但没在 docs/dev/验证清单.csv 登记验证用例");
                continue;
            }

            var paths = Split(verifyRow.Get("verify_paths"));
            CheckGt(paths.Count, 0.0, $"{moduleId} 的 verify_paths 不能为空");
            foreach (var path in paths)
            {
                CheckTrue(FileAccess.FileExists($"res://{path}"), $"{moduleId} 引用的用例不存在：{path}");
            }

            var verified = verifyRow.Get("verified_design_version");
            // 需返工的模块本来就要补验证，不要求它已经对齐
            if (status != "需返工" && verified != version)
            {
                Fail(
                    $"{moduleId} 的验证对齐版本是 {verified}，当前是 {version}："
                    + $"需求变过，必须补或改用例（{string.Join(", ", paths)}）后更新 verified_design_version");
            }
        }
    }

    /// <summary>4. 验证清单不能引用对接表里不存在的模块。</summary>
    private void CheckContractLinks(List<CsvRow> contract, List<CsvRow> verifyRows)
    {
        var known = new HashSet<string>();
        foreach (var row in contract)
        {
            known.Add(row.Get("module_id"));
        }

        foreach (var row in verifyRows)
        {
            var moduleId = row.Get("module_id");
            CheckTrue(known.Contains(moduleId), $"验证清单里的 module_id '{moduleId}' 不在对接表中");
        }
    }
    /// <summary>
    /// 5. `tests/` 下不许有没人引用的用例文件。
    ///
    /// 2026-10-11（第 48 批）：扫描面从 `.gd` 换成 `.cs`（用例已全搬 C#，只扫 `.gd` 就是空转）。
    /// 扩面的代价是「不是独立用例」的那几种文件（基类、拆开的 `partial`、探针类、不对应策划模块的
    /// 骨架自检）都得进 `InfrastructureTests`——但换来的是**新写的孤儿用例当场红**，
    /// 这跟原来那条门限守的是同一件事。
    /// </summary>
    private void CheckNoOrphanTests(List<CsvRow> verifyRows)
    {
        var referenced = new HashSet<string>();
        foreach (var row in verifyRows)
        {
            foreach (var path in Split(row.Get("verify_paths")))
            {
                referenced.Add(path);
            }
        }

        foreach (var fileName in DirAccess.GetFilesAt(TestsDir))
        {
            if (!fileName.EndsWith(".cs", StringComparison.Ordinal)
                || Array.IndexOf(InfrastructureTests, fileName) >= 0)
            {
                continue;
            }

            var path = $"tests/{fileName}";
            CheckTrue(referenced.Contains(path), $"用例 {path} 没有被任何模块的 verify_paths 引用（孤儿验证）");
        }

        // 基础设施白名单也要对得上文件：名字写错／文件删了都不该悄悄留着
        foreach (var fileName in InfrastructureTests)
        {
            CheckTrue(
                FileAccess.FileExists($"{TestsDir}/{fileName}"),
                $"InfrastructureTests 里的 {fileName} 不存在（名字写错或文件删了？）");
        }
    }
}
