using System;
using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 数据管线用例的公共夹具与公共断言口（对位 `tests/test_table_pipeline.gd`——2026-10-11
/// P4 第三十七批整体搬过来）。
///
/// **为什么拆成「基类 ＋ 两族」**：整份搬完是 900 行上下，撞 `tests/test_handshake_repo.gd::_check_file_size`
/// 的 800 行硬上限，于是照 `test_save_migration.gd`（第 35 批）与 `test_handshake.gd`（八族）的先例拆：
/// `TestTablePipeline.cs` 管**正例**（生成资源 ↔ CSV ↔ 注册表三项对齐）、
/// `TestTablePipelineRules.cs` 管**负例**（把一格改坏，校验必须报出来）。两族各自注册进
/// `tools/RunTestsCs.cs` 的 `Suites`；**这一份是抽象基类，不注册**（假绿门限按注册路径逐文件比
/// 「静态断言点 vs 实际执行条数」，工具留在基类里不会让任何一族的账失衡）。
///
/// **坏数据一律只改内存副本**：表资源是 Godot 按路径缓存的（两个 `TableDb.load_all()` 实例可能拿到
/// 同一份 `TableResource`，行对象也可能是同一批），所以 `Broken()` 先深拷贝整表、再逐行
/// `Duplicate(true)`，改完写回**私有表库**的 `tables`。直接改行会污染别的用例
/// （照 `TestCase.TableWithTalents` 的形状，见施工单坑53）。
/// </summary>
public abstract partial class TestTablePipelineBase : TestCase
{
    /// <summary>一份**新**的表库，等价于旧链的 `TableDbScript.new()` ＋ `load_all()`。</summary>
    protected static TableDb Stub()
    {
        var db = new TableDb();
        db.load_all();
        return db;
    }

    /// <summary>
    /// 把 `tableName` 深拷贝出来、按 `edit` 改坏行，再写回一份**私有**表库里。
    /// `edit` 拿到的是新数组（元素是逐行深拷贝过的行对象），改它不会碰到别人手上的那一份。
    /// </summary>
    protected static TableDb Broken(string tableName, Action<Godot.Collections.Array> edit)
    {
        var db = new TableDb();
        db.load_all();
        var table = (TableResource)db.tables[tableName].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            rows.Add(((Resource)entry.AsGodotObject()).Duplicate(true));
        }

        table.rows = rows;
        db.tables[tableName] = table;
        edit(rows);
        return db;
    }

    /// <summary>`rows[index]` 取成行类（对位旧链的 `copy.rows[i]` ＋ 直接改属性）。</summary>
    protected static T Row<T>(Godot.Collections.Array rows, int index) where T : class =>
        rows[index].AsGodotObject() as T;

    /// <summary>按条件找第一行，找不到返回 null（对位旧链的 `for row in rows: if …: … break`）。</summary>
    protected static T First<T>(Godot.Collections.Array rows, Func<T, bool> match) where T : class
    {
        foreach (Variant entry in rows)
        {
            if (entry.AsGodotObject() is T row && match(row))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>按条件删行（倒着走，别让下标跟着挪）。</summary>
    protected static void RemoveRows<T>(Godot.Collections.Array rows, Func<T, bool> match) where T : class
    {
        for (int index = rows.Count - 1; index >= 0; index--)
        {
            if (rows[index].AsGodotObject() is T row && match(row))
            {
                rows.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// 删掉第一行但**不重建 index**：造出「index 里记的位置比实际行数大」的越界条件
    /// （对位旧链的 `_drop_first_row_keep_index`）。
    /// </summary>
    protected static void DropFirstRowKeepIndex(TableDb db, string tableName)
    {
        var resource = (TableResource)db.tables[tableName].As<TableResource>().Duplicate(true);
        var kept = new Godot.Collections.Array();
        for (int index = 0; index < resource.rows.Count; index++)
        {
            if (index == 0)
            {
                continue;
            }

            kept.Add(((Resource)resource.rows[index].AsGodotObject()).Duplicate(true));
        }

        resource.rows = kept;
        db.tables[tableName] = resource;
    }

    /// <summary>拼一条可读的清单（对位旧链里的 `str(array)`）。</summary>
    protected static string Join(System.Collections.Generic.IEnumerable<string> items) =>
        string.Join("|", items);

    /// <summary>`Godot.Collections.Array` 版的 `Join`（元素逐项 `ToString()`）。</summary>
    protected static string Join(Godot.Collections.Array items)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant item in items)
        {
            parts.Add(item.ToString());
        }

        return string.Join("|", parts);
    }

    /// <summary>校验报出的错里，有没有哪一条**同时**含 `needles` 里的全部关键词。</summary>
    protected static bool HasAll(string[] errors, params string[] needles) =>
        System.Array.Exists(
            errors, message => System.Array.TrueForAll(needles, needle => message.Contains(needle)));

    /// <summary>校验报出的错里，含 `needles`（全部关键词）的**条数**——有几处要的是「正好 N 条」。</summary>
    protected static int CountWith(string[] errors, params string[] needles)
    {
        int count = 0;
        foreach (string message in errors)
        {
            if (System.Array.TrueForAll(needles, needle => message.Contains(needle)))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 把某一格改坏，要求校验报出的错里含 `needle`（对位旧链的 `_check_one_cell_caught`）。
    /// 列名走 `TableRegistry.property_of`——旧链这里用的是 **CSV 列名**，别名表得先过一遍。
    /// </summary>
    protected void CheckOneCellCaught(string tableName, string rowId, string column, int value, string needle) =>
        CheckOneCellCaught(tableName, rowId, column, Variant.From(value), $"{value}", needle);

    /// <summary>`double` 版（`base_chance` 这类比率列）。</summary>
    protected void CheckOneCellCaught(string tableName, string rowId, string column, double value, string needle) =>
        CheckOneCellCaught(tableName, rowId, column, Variant.From(value), $"{value}", needle);

    private void CheckOneCellCaught(
        string tableName, string rowId, string column, Variant value, string shown, string needle)
    {
        string property = TableRegistry.property_of(tableName, column);
        TableDb broken = Broken(tableName, rows =>
        {
            foreach (Variant entry in rows)
            {
                GodotObject row = entry.AsGodotObject();
                if (row.Get("id").AsString() == rowId)
                {
                    row.Set(property, value);
                }
            }
        });
        CheckTrue(
            HasAll(TableValidator.validate(broken), needle),
            $"{tableName}[{rowId}].{column} = {shown} 应被校验抓到（找 {needle}）");
    }

    /// <summary>
    /// **校验器自己的规则表自检**：`REFERENCES` / `RANGES` / `RELATIONS` / `ENUMS` 里点到的
    /// 表名与列名必须真实存在。这些规则是几十轮里一条条加出来的，而**列名写错很危险**：
    /// `row.get(错列)` 返回 null，`float(null)` 会当场报运行期错误、**让整个校验中途断掉**
    /// （后面的规则一条都不再跑——那才是真正可怕的地方，探针实测过）。
    /// 这条门限把「规则写错」变成当场红；校验器本身也加了守卫。
    /// </summary>
    protected void CheckValidatorRuleTargets(TableDb db)
    {
        if (!db.load_all())
        {
            Fail("规则自检：生成表加载失败");
            return;
        }

        var problems = new System.Collections.Generic.List<string>();
        foreach (Variant entry in TableValidator.references())
        {
            Godot.Collections.Dictionary rule = entry.AsGodotDictionary();
            string table = rule["table"].AsString();
            CheckRuleColumn(db, table, rule["column"].AsString(), problems, "REFERENCES");
            string target = rule.ContainsKey("target") ? rule["target"].AsString() : "";
            if (target.Length == 0)
            {
                continue;
            }

            string targetColumn = rule.ContainsKey("target_column") ? rule["target_column"].AsString() : "";
            if (targetColumn.Length == 0)
            {
                // 没写目标列时，校验器按目标表的**第一个主键列**取值（见 `_reference_values`）
                Godot.Collections.Dictionary primary = TableRegistry.primary_keys();
                var keys = primary.ContainsKey(target)
                    ? primary[target].AsGodotArray()
                    : new Godot.Collections.Array();
                targetColumn = keys.Count > 0 ? keys[0].AsString() : "";
            }

            CheckRuleColumn(db, target, targetColumn, problems, "REFERENCES 目标");
        }

        foreach (Variant entry in TableValidator.ranges())
        {
            Godot.Collections.Dictionary rule = entry.AsGodotDictionary();
            CheckRuleColumn(db, rule["table"].AsString(), rule["column"].AsString(), problems, "RANGES");
        }

        foreach (Variant entry in TableValidator.relations())
        {
            Godot.Collections.Dictionary rule = entry.AsGodotDictionary();
            string table = rule["table"].AsString();
            CheckRuleColumn(db, table, rule["left"].AsString(), problems, "RELATIONS");
            CheckRuleColumn(db, table, rule["right"].AsString(), problems, "RELATIONS");
        }

        Godot.Collections.Dictionary enums = TableValidator.enums();
        foreach (Variant tableKey in enums.Keys)
        {
            Godot.Collections.Dictionary columns = enums[tableKey].AsGodotDictionary();
            foreach (Variant columnKey in columns.Keys)
            {
                CheckRuleColumn(db, tableKey.AsString(), columnKey.AsString(), problems, "ENUMS");
            }
        }

        CheckEq(problems.Count, 0, $"校验器规则里点到的表/列都要真实存在：{Join(problems)}");
    }

    private static void CheckRuleColumn(
        TableDb db, string table, string column, System.Collections.Generic.List<string> problems, string where)
    {
        if (!db.has_table(table))
        {
            problems.Add($"{where}：没有表 {table}");
            return;
        }

        if (column.Length == 0)
        {
            return;
        }

        Godot.Collections.Array rows = db.rows(table);
        if (rows.Count == 0)
        {
            return;
        }

        // CSV 列名 → 行类属性名（别名表在 `TableRegistry` 里），再对照真实的属性列表
        string property = TableRegistry.property_of(table, column);
        foreach (Variant entry in rows[0].AsGodotObject().GetPropertyList())
        {
            if (entry.AsGodotDictionary()["name"].AsString() == property)
            {
                return;
            }
        }

        problems.Add($"{where}：{table} 没有列 {column}");
    }
}

