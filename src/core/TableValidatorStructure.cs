using System.Collections.Generic;
using Godot;
using Lunhui.Data;

namespace Lunhui.Core;

/// <summary>
/// 校验器的「表结构」那一族（C# 版，对位原 `src/core/table_validator.gd` 的同名函数）：
/// 主键／枚举／引用／区间／关系，以及**代码里硬编码的那几份 id 名单与表的一致性**。
///
/// 纯 C# 静态类（不派生 GodotObject）：只被 `TableValidator.validate()` 调用。
/// </summary>
internal static class TableValidatorStructure
{
    /// <summary>锁定场景清单的唯一真相在 `WorldMapService`（这里只读）。</summary>

    /// <summary>
    /// 隐藏触发在哪个 trigger_type 下该用哪个 condition 键（消费方是
    /// `local_map_controller._condition_value()`／`_condition_text()`）。
    /// 键必须**一字不差**：拼错的后果不对称——`behavior` 会当成条件已满足（白送奖励）、
    /// `kill_style` 会永远触发不了、`completion` 会静默用默认阈值。
    /// </summary>
    private static readonly Dictionary<string, string> TRIGGER_CONDITION_KEYS = new()
    {
        ["sequence"] = "sequence",
        ["behavior"] = "flag_stealth_full",
        ["kill_style"] = "kill_with",
        ["completion"] = "chest_open_rate",
    };

    /// <summary>这几种触发类型靠别的列判条件（道具／工具／带着东西），不该再写 required_condition。</summary>
    private static readonly string[] TRIGGER_TYPES_WITHOUT_CONDITION = { "item", "space", "carry" };

    /// <summary>
    /// 主键：列不能空／非 0，`id` 必须与主键列算出来的一致，不许重复，
    /// 而且 `index` 与 `rows` 必须对得上（手改 `.tres` 或删行没重建索引时逐行点名）。
    /// </summary>
    internal static void CheckPrimaryKeys(TableDb db, List<string> errors)
    {
        foreach (string table_name in TableRegistry.tables())
        {
            if (!db.tables.ContainsKey(table_name))
            {
                continue;
            }

            var resource = db.tables[table_name].As<TableResource>();
            List<Resource> rows = TableValidator.Rows(db, table_name);
            Godot.Collections.Dictionary index = resource.index;
            var seen = new HashSet<string>();
            foreach (Resource row in rows)
            {
                var values = new Godot.Collections.Dictionary();
                var complete = true;
                foreach (string key in PrimaryKeysOf(table_name))
                {
                    Variant value = row.Get(TableRegistry.property_of(table_name, key));
                    values[key] = value;
                    if (value.VariantType == Variant.Type.String && value.AsString().Trim().Length == 0)
                    {
                        errors.Add($"{table_name} 存在空主键列 {key} 的行");
                        complete = false;
                    }
                    else if (value.VariantType == Variant.Type.Int && value.AsInt64() == 0)
                    {
                        errors.Add($"{table_name} 存在主键列 {key} = 0 的行");
                        complete = false;
                    }
                }

                if (!complete)
                {
                    continue;
                }

                string computed = TableRegistry.build_id(table_name, values);
                if (computed != TableValidator.RowId(row))
                {
                    errors.Add(
                        $"{table_name}[{TableValidator.RowId(row)}] 的 id 与主键列不一致（按列算应为 '{computed}'）"
                    );
                }

                if (!seen.Add(computed))
                {
                    errors.Add($"{table_name} 主键重复：'{computed}'");
                }

                int position = index.ContainsKey(TableValidator.RowId(row))
                    ? index[TableValidator.RowId(row)].AsInt32()
                    : -1;
                // 位置必须先夹在 rows 的范围内才能取——`rows[position]` 越界会当场报运行期错误、
                // **把整段主键检查掐断**（决策 147）。越界本身就是「index 与 rows 不一致」，
                // 照报一条然后继续查后面的行／表。
                if (position < 0 || position >= rows.Count || !ReferenceEquals(rows[position], row))
                {
                    errors.Add(
                        $"{table_name} 的 index 与 rows 不一致：{TableValidator.RowId(row)}"
                        + $"（index 记位置 {position}，实际 {rows.Count} 行）"
                    );
                }
            }
        }
    }

    internal static void CheckEnums(TableDb db, List<string> errors)
    {
        foreach (KeyValuePair<string, Dictionary<string, string[]>> table in TableValidator.ENUMS)
        {
            foreach (KeyValuePair<string, string[]> column in table.Value)
            {
                foreach (Resource row in TableValidator.Rows(db, table.Key))
                {
                    string value = TableValidator.Str(row, column.Key);
                    if (System.Array.IndexOf(column.Value, value) < 0)
                    {
                        errors.Add(
                            $"{table.Key}[{TableValidator.RowId(row)}].{column.Key} = '{value}' "
                            + $"不是合法取值（允许：{TableValidator.Joined(column.Value)}）"
                        );
                    }
                }
            }
        }
    }

    /// <summary>
    /// 异常状态的特殊规则：表里写了代码不认的 `extra_rule`，效果就会静默丢失（决策 59）。
    /// 「设计已声明、代码还没实现」的规则允许留着（运行期会播成暂缓规则）；**写错的 id** 仍然当场报错。
    /// </summary>
    internal static void CheckStatusRules(TableDb db, List<string> errors)
    {
        // 2026-10-11：`battle_actor` 已搬成 C#，两份名单**直接读它的公开静态字段**
        // （原先是 `TableValidator.ScriptConstants(BATTLE_ACTOR_SCRIPT)` 读 `.gd` 的常量表）。
        List<string> known = new(BattleActor.KNOWN_STATUS_RULES);
        Godot.Collections.Dictionary declared = BattleActor.DECLARED_STATUS_RULES;
        var declared_keys = new List<string>();
        foreach (Variant key in declared.Keys)
        {
            declared_keys.Add(key.AsString());
        }

        foreach (Resource row in TableValidator.Rows(db, "status_effect"))
        {
            string rule = TableValidator.Trimmed(row, "extra_rule");
            if (known.Contains(rule) || declared.ContainsKey(rule))
            {
                continue;
            }

            errors.Add(
                $"status_effect[{TableValidator.RowId(row)}].extra_rule = '{rule}' "
                + "既不是代码认得的规则（battle_actor.KNOWN_STATUS_RULES："
                + $"{TableValidator.Joined(known)}），也不是设计已声明的待实现规则"
                + $"（DECLARED_STATUS_RULES：{TableValidator.Joined(declared_keys)}）"
            );
        }
    }

    /// <summary>
    /// 隐藏触发的 `required_condition` 必须用对键：键写错的后果不对称（见 TRIGGER_CONDITION_KEYS 注释）。
    /// </summary>
    internal static void CheckTriggerConditions(TableDb db, List<string> errors)
    {
        // 击杀方式 = 状态 id + 「正面击杀」；从表里取，不手抄一份枚举
        var kill_styles = new List<string> { "normal" };
        foreach (Resource row in TableValidator.Rows(db, "status_effect"))
        {
            kill_styles.Add(TableValidator.Str(row, "status_id"));
        }

        foreach (Resource row in TableValidator.Rows(db, "hidden_trigger"))
        {
            string trigger_type = TableValidator.Str(row, "trigger_type");
            string condition = TableValidator.Trimmed(row, "required_condition");
            if (System.Array.IndexOf(TRIGGER_TYPES_WITHOUT_CONDITION, trigger_type) >= 0)
            {
                if (condition.Length != 0)
                {
                    errors.Add(
                        $"hidden_trigger[{TableValidator.RowId(row)}]（{trigger_type}）"
                        + $"不该有 required_condition，但写了 '{condition}'"
                    );
                }

                continue;
            }

            if (!TRIGGER_CONDITION_KEYS.TryGetValue(trigger_type, out string want_key))
            {
                continue; // 没登记的 trigger_type 由枚举校验负责
            }

            if (condition.Length == 0)
            {
                errors.Add(
                    $"hidden_trigger[{TableValidator.RowId(row)}]（{trigger_type}）"
                    + $"缺少 required_condition（应为 {want_key}=…）"
                );
                continue;
            }

            foreach (string segment in condition.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pieces = segment.Split('=', System.StringSplitOptions.RemoveEmptyEntries);
                if (pieces.Length != 2)
                {
                    errors.Add(
                        $"hidden_trigger[{TableValidator.RowId(row)}].required_condition "
                        + $"段 '{segment.Trim()}' 不是 key=value"
                    );
                    continue;
                }

                string key = pieces[0].Trim();
                string value = pieces[1].Trim();
                if (key != want_key)
                {
                    errors.Add(
                        $"hidden_trigger[{TableValidator.RowId(row)}]（{trigger_type}）的条件键是 '{key}'，"
                        + $"只认 '{want_key}'（拼错会静默改变判定）"
                    );
                    continue;
                }

                CheckTriggerConditionValue(TableValidator.RowId(row), trigger_type, value, kill_styles, errors);
            }
        }
    }

    private static void CheckTriggerConditionValue(
        string trigger_id, string trigger_type, string value, List<string> kill_styles, List<string> errors
    )
    {
        switch (trigger_type)
        {
            case "behavior":
                if (value != "0" && value != "1")
                {
                    errors.Add(
                        $"hidden_trigger[{trigger_id}].flag_stealth_full 只能是 1 或 0，实际 '{value}'"
                    );
                }

                break;
            case "kill_style":
                if (!kill_styles.Contains(value))
                {
                    errors.Add(
                        $"hidden_trigger[{trigger_id}].kill_with='{value}' 不是合法击杀方式"
                        + $"（{TableValidator.Joined(kill_styles)}）"
                    );
                }

                break;
            case "completion":
                // 别用 `ToFloat()`：C# 侧它是 32 位，999999999 那种数会被舍出入（同 BuildTables 那条坑）
                double rate = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedRate) ? parsedRate : 0.0;
                if (!(rate > 0.0 && rate <= 1.0))
                {
                    errors.Add(
                        $"hidden_trigger[{trigger_id}].chest_open_rate 要落在 (0, 1]，实际 '{value}'"
                    );
                }

                break;
            case "sequence":
                // 形如 1-3-2：数字与短横线，且至少两段（具体解法是谜题，代码只校验形态）
                string[] steps = value.Split('-', System.StringSplitOptions.RemoveEmptyEntries);
                var shape_ok = steps.Length >= 2;
                foreach (string step in steps)
                {
                    if (!step.IsValidInt())
                    {
                        shape_ok = false;
                    }
                }

                if (!shape_ok)
                {
                    errors.Add(
                        $"hidden_trigger[{trigger_id}].sequence='{value}' 形态应为 1-3-2"
                        + "（数字与短横线，至少两段）"
                    );
                }

                break;
        }
    }

    /// <summary>
    /// 敌人的 AI 模板：`enemy_base.ai_template` 必须落在 `EnemyFactory.AI_TEMPLATES` 里——
    /// 拼错（或漏填）的话那个敌人**一套招都没有**，每回合空过，难度会静默塌掉。
    /// </summary>
    internal static void CheckAiTemplates(TableDb db, List<string> errors)
    {
        // `EnemyFactory` 自 core 第十七批起是 C#（`src/core/EnemyFactory.cs`），
        // 名单的唯一真相仍然是它——这里直接调取值口，不再借 `ScriptConstants` 读常量表。
        List<string> templates = new(EnemyFactory.ai_templates());
        // 招式在 `enemy_skill.csv`（0.10.0）：先按敌人点一遍数
        var skill_counts = new Dictionary<string, int>();
        foreach (Resource row in TableValidator.Rows(db, "enemy_skill"))
        {
            string enemy_key = TableValidator.Str(row, "enemy_id");
            skill_counts.TryGetValue(enemy_key, out int count);
            skill_counts[enemy_key] = count + 1;
        }

        foreach (Resource row in TableValidator.Rows(db, "enemy_base"))
        {
            string template = TableValidator.Trimmed(row, "ai_template");
            if (!templates.Contains(template))
            {
                errors.Add(
                    $"enemy_base[{TableValidator.RowId(row)}].ai_template = '{template}' "
                    + $"不在 EnemyFactory.AI_TEMPLATES 里（{TableValidator.Joined(templates)}）"
                    + "——拼错会让它的行为走兜底"
                );
            }

            // 除 `ai_neutral`（中立·不还手，如采药人）外，每个敌人都得在 enemy_skill 里有招式，
            // 否则它在战斗里只会「无可用招式，跳过」——那是静默的哑巴敌人。
            string enemy_id = TableValidator.Str(row, "enemy_id");
            skill_counts.TryGetValue(enemy_id, out int skill_count);
            if (template != "ai_neutral" && skill_count == 0)
            {
                errors.Add(
                    $"enemy_base[{enemy_id}]（{template}）在 enemy_skill.csv 里一条招式都没有——它一招都出不了"
                );
            }
        }
    }

    /// <summary>单值／多值引用：每一段的取值都要落在目标表的取值集合里。</summary>
    internal static void CheckReferences(TableDb db, List<string> errors)
    {
        foreach (TableValidator.ReferenceRule spec in TableValidator.REFERENCES)
        {
            Godot.Collections.Dictionary values = TableValidator.TargetValues(db, spec);
            foreach (Resource row in TableValidator.Rows(db, spec.Table))
            {
                string raw = TableValidator.Trimmed(row, spec.Column);
                if (raw.Length == 0)
                {
                    if (!spec.AllowEmpty)
                    {
                        errors.Add($"{spec.Table}[{TableValidator.RowId(row)}].{spec.Column} 为空，但这是必填引用");
                    }

                    continue;
                }

                string[] tokens = string.IsNullOrEmpty(spec.Split)
                    ? new[] { raw }
                    : raw.Split(spec.Split, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                {
                    string trimmed = token.Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    if (!values.ContainsKey(trimmed))
                    {
                        errors.Add(
                            $"{spec.Table}[{TableValidator.RowId(row)}].{spec.Column} = '{trimmed}' "
                            + $"在 {spec.Target} 里不存在"
                        );
                    }
                }
            }
        }
    }

    internal static void CheckRanges(TableDb db, List<string> errors)
    {
        foreach (TableValidator.RangeRule rule in TableValidator.RANGES)
        {
            List<Resource> rows = TableValidator.Rows(db, rule.Table);
            if (rows.Count == 0)
            {
                continue;
            }

            // 规则表自己写错列名时：`row.get()` 返回 null，`float(null)` 会**直接报运行期错误、
            // 让整个校验中途断掉**——这里显式挡一下，报一条错继续往下查。
            string property = TableRegistry.property_of(rule.Table, rule.Column);
            if (!TableValidator.HasProperty(rows[0], property))
            {
                errors.Add($"校验规则里的列不存在：{rule.Table}.{rule.Column}（RANGES 写错了）");
                continue;
            }

            foreach (Resource row in rows)
            {
                double value = TableValidator.Num(row, property);
                if (rule.Min.HasValue && value < rule.Min.Value)
                {
                    errors.Add(
                        $"{rule.Table}[{TableValidator.RowId(row)}].{rule.Column} = {value} "
                        + $"小于下限 {rule.Min.Value}"
                    );
                }

                if (rule.Max.HasValue && value > rule.Max.Value)
                {
                    errors.Add(
                        $"{rule.Table}[{TableValidator.RowId(row)}].{rule.Column} = {value} "
                        + $"大于上限 {rule.Max.Value}"
                    );
                }
            }
        }
    }

    internal static void CheckRelations(TableDb db, List<string> errors)
    {
        foreach (TableValidator.RelationRule rule in TableValidator.RELATIONS)
        {
            List<Resource> rows = TableValidator.Rows(db, rule.Table);
            if (rows.Count == 0)
            {
                continue;
            }

            string left_property = TableRegistry.property_of(rule.Table, rule.Left);
            string right_property = TableRegistry.property_of(rule.Table, rule.Right);
            if (!TableValidator.HasProperty(rows[0], left_property)
                || !TableValidator.HasProperty(rows[0], right_property))
            {
                errors.Add(
                    $"校验规则里的列不存在：{rule.Table}.{rule.Left} / {rule.Right}（RELATIONS 写错了）"
                );
                continue;
            }

            foreach (Resource row in rows)
            {
                double left = TableValidator.Num(row, left_property);
                double right = TableValidator.Num(row, right_property);
                // stat_def 的 max_value 为 0 表示无上限，不参与比较
                if (rule.Table == "stat_def" && right <= 0.0)
                {
                    continue;
                }

                if (left > right)
                {
                    errors.Add(
                        $"{rule.Table}[{TableValidator.RowId(row)}] {rule.Label}不合法："
                        + $"{rule.Left}({left}) > {rule.Right}({right})"
                    );
                }
            }
        }
    }

    /// <summary>`REQUIRED_CAPPED_STATS`：管线依赖的比率不许「无上限」（留空会让代码静默用兜底常数）。</summary>
    internal static void CheckPipelineCaps(TableDb db, List<string> errors)
    {
        foreach (string stat_id in TableValidator.REQUIRED_CAPPED_STATS)
        {
            Resource row = db.get_row("stat_def", stat_id);
            if (row == null)
            {
                continue; // 缺行由引用完整性那几条报，这里只管「上限没写」
            }

            if (!row.Call("has_max").AsBool())
            {
                errors.Add(
                    $"stat_def[{stat_id}].max_value 空着（= 0，本表约定「无上限」），"
                    + "但伤害管线对这个比率有硬上限：留空会让代码静默换用兜底常数，表与实际行为分家"
                    + "——要么补一个上限，要么明确告诉开发侧「这个值要改成无上限」"
                );
            }
        }
    }

    internal static void CheckDiminishing(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "attr_to_stat"))
        {
            if (TableValidator.Str(row, "curve") != "diminishing")
            {
                continue;
            }

            if (TableValidator.Num(row, "param") <= 0.0)
            {
                errors.Add($"attr_to_stat[{TableValidator.RowId(row)}] 走递减曲线但 param 为空/非正");
            }

            if (TableValidator.Num(row, "cap") <= 0.0)
            {
                errors.Add($"attr_to_stat[{TableValidator.RowId(row)}] 走递减曲线但 cap 为空/非正");
            }
        }
    }

    /// <summary>星级定义必须是完整的 1~5。</summary>
    internal static void CheckStarDef(TableDb db, List<string> errors)
    {
        Godot.Collections.Dictionary present = db.column_values("skill_star_def", "star");
        for (var star = 1; star <= 5; star++)
        {
            if (!present.ContainsKey(star.ToString()))
            {
                errors.Add($"skill_star_def 缺少 {star} 星");
            }
        }
    }

    /// <summary>
    /// `attribute_def.allocatable` 必须是 0 或 1，而且**至少留一个可加点属性**——
    /// 否则每级 5 点无处可加，玩家会看到一个永远点不动的加点界面。
    /// </summary>
    internal static void CheckAttributeAllocatable(TableDb db, List<string> errors)
    {
        var allocatable = 0;
        foreach (Resource row in TableValidator.Rows(db, "attribute_def"))
        {
            int value = TableValidator.Int(row, "allocatable");
            if (value != 0 && value != 1)
            {
                errors.Add(
                    $"attribute_def[{TableValidator.Str(row, "attr_id")}].allocatable={value}，"
                    + "只允许 0（资质，不可加点）或 1（可加点）"
                );
                continue;
            }

            if (value == 1)
            {
                allocatable += 1;
            }
        }

        if (allocatable == 0)
        {
            errors.Add("attribute_def 里没有一个 allocatable=1 的属性：每级的自由点数将无处可加");
        }
    }

    /// <summary>
    /// 代码里那份「本章不开放的小地图」清单里的每个 scene_id 都必须是 `map_local` 里真实存在的一行。
    /// 打错一个字母 → `LOCKED_SCENES.has(...)` 永远为 false，废弃渡口**静默变成可进入**，而用例照样绿。
    /// </summary>
    internal static void CheckLockedScenesExist(TableDb db, List<string> errors)
    {
        var known = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "map_local"))
        {
            known.Add(TableValidator.Str(row, "scene_id"));
        }

        // `WorldMapService` 已搬成 C#（`src/core/WorldMapService.cs`）：名单的唯一真相还是它，
        // 取值口从「读那份 `.gd` 的常量表」换成静态方法。
        List<string> locked = new(WorldMapService.locked_scenes());
        foreach (string scene_id in locked)
        {
            if (!known.Contains(scene_id))
            {
                errors.Add(
                    $"WorldMapService.LOCKED_SCENES 里的 '{scene_id}' 在 map_local 里不存在："
                    + "锁了个不存在的场景 = 什么都没锁住"
                );
            }
        }
    }

    /// <summary>
    /// 图标列**必须是这一行自己的 id**：界面取图是「优先 `icon` 列、空则退回行 id」，
    /// 值写错了不会报错，只会画出别人家的图标。留空合法（等于退回 id）。
    /// </summary>
    internal static void CheckIconSelfReference(TableDb db, List<string> errors)
    {
        string[][] specs = { new[] { "equip_base", "equip_id" }, new[] { "item_base", "item_id" } };
        foreach (string[] spec in specs)
        {
            string table_name = spec[0];
            string id_column = spec[1];
            foreach (Resource row in TableValidator.Rows(db, table_name))
            {
                string icon = TableValidator.Trimmed(row, "icon");
                if (icon.Length == 0)
                {
                    continue;
                }

                string row_id = TableValidator.Str(row, id_column);
                if (icon != row_id)
                {
                    errors.Add(
                        $"{table_name}[{row_id}].icon='{icon}' 必须等于它自己的 {id_column}"
                        + "（界面按 icon 取图——写错不会报错，只会画出别人家的图标；空着也合法，等于退回行 id）"
                    );
                }
            }
        }
    }

    private static string[] PrimaryKeysOf(string table_name)
    {
        Godot.Collections.Dictionary primary = TableRegistry.primary_keys();
        if (!primary.ContainsKey(table_name))
        {
            return System.Array.Empty<string>();
        }

        var keys = primary[table_name].AsGodotArray();
        var out_keys = new string[keys.Count];
        for (var index = 0; index < keys.Count; index++)
        {
            out_keys[index] = keys[index].AsString();
        }

        return out_keys;
    }
}
