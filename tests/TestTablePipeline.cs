using System;
using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 数据管线（一·正例）：生成资源能加载、行数与 CSV 一致、抽查值对得上、
/// `table_registry.TABLES` 与 `data/tables/` 双向对齐。
/// （对位 `tests/test_table_pipeline.gd` 的前三节，2026-10-11 P4 第三十七批整体搬过来。）
///
/// 负例（"表写错了会怎样"）在 `tests/TestTablePipelineRules.cs`；两族共用 `TestTablePipelineBase`。
/// </summary>
public partial class TestTablePipeline : TestTablePipelineBase
{
    public override string SuiteName() => "数据管线与校验";

    public override void Run()
    {
        TableDb db = GetDb();
        CheckTrue(db.errors.Length == 0, $"data/generated 应能全部加载：{Join(db.errors)}");
        CheckEq(db.tables.Count, TableRegistry.tables().Length, "表数量与注册表一致");
        CheckEq(TableValidator.validate(db).Length, 0, "真实配置表应零校验错误");

        _CheckRowCounts(db);
        _CheckSpotValues(db);
        _CheckRegistryMatchesCsvDir();
    }

    /// <summary>
    /// `data/tables/` 里的每张 CSV 都必须登记进 `table_registry.TABLES`，反过来也必须存在。
    ///
    /// 由来（2026-10-03）：`tools\validate_tables.ps1` 第 9 节**确实**比对了「CSV 文件 ↔ 注册表」，
    /// 但**只算 Warning**——而验收里那一串警告被明确标成"给策划的清单、不挡验收"。
    /// 于是留了这么一条静默路径：策划按 06 把新表（`dialogue_tree.csv` 这类"第一章制作期"的表）
    /// 放进 `data/tables/`，**验收全绿、游戏运行期却根本不加载它**（`TableDb.load_all` 只认注册表），
    /// 要等到某个功能"怎么不生效"才会被翻出来——**开发侧的账不该混在给策划的警告里**，所以升成硬门限。
    ///
    /// 反方向（注册了却没有 CSV）本来就会让构建期直接失败，这里也一并钉住，报错更早更直白。
    /// 只扫一层：`data/tables/` 下出现子目录会当场点名——藏在子目录里的表同样读不到，
    /// 要么移平，要么把这条门限改成递归（**别让它悄悄漏**）。
    /// </summary>
    private void _CheckRegistryMatchesCsvDir()
    {
        string dir = TableRegistry.tables_dir();
        var registered = new System.Collections.Generic.HashSet<string>();
        foreach (string tableName in TableRegistry.tables())
        {
            registered.Add(tableName);
        }

        var onDisk = new System.Collections.Generic.List<string>();
        foreach (string fileName in DirAccess.GetFilesAt(dir))
        {
            if (!fileName.ToLower().EndsWith(".csv"))
            {
                continue;
            }

            onDisk.Add(fileName.GetBaseName());
        }

        CheckGt(onDisk.Count, 20.0, $"data/tables 下扫到足够多的 CSV（{onDisk.Count} 张）");
        foreach (string tableName in onDisk)
        {
            CheckTrue(
                registered.Contains(tableName),
                $"{dir}/{tableName}.csv 没登记进 table_registry.TABLES：构建会跳过它，游戏运行期读不到这张表");
        }

        foreach (string tableName in registered)
        {
            CheckTrue(
                onDisk.Contains(tableName),
                $"table_registry 登记了 '{tableName}'，但 {dir}/{tableName}.csv 不存在（构建期会直接失败）");
        }

        foreach (string sub in DirAccess.GetDirectoriesAt(dir))
        {
            CheckTrue(
                false,
                $"{dir}/{sub} 是子目录：这条门限只扫一层，藏在子目录里的表会被漏掉（移平，或把门限改成递归）");
        }
    }

    /// <summary>生成资源的行数必须与 CSV 行数一致（表头一行不算）。</summary>
    private void _CheckRowCounts(TableDb db)
    {
        foreach (string tableName in TableRegistry.tables())
        {
            string csvPath = $"{TableRegistry.tables_dir()}/{tableName}.csv";
            FileAccess file = FileAccess.Open(csvPath, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                Fail($"读不到 CSV {csvPath}");
                continue;
            }

            int lines = 0;
            while (!file.EofReached())
            {
                string[] line = file.GetCsvLine();
                if (line.Length == 1 && line[0].Trim().Length == 0)
                {
                    continue;
                }

                lines++;
            }

            file.Close();
            int expected = lines - 1;
            CheckEq(db.rows(tableName).Count, Math.Max(expected, 0), $"{tableName} 行数与 CSV 一致");
        }
    }

    /// <summary>
    /// 抽查几种"列映射写错了就会静默取默认值"的地方：BOM 剥离、列别名、复合主键、多值列解析，
    /// 外加上线较晚的几张表（角色模板、武器类型、战斗常数、事件判定、装备槽位、商店、道具用途）。
    /// </summary>
    private void _CheckSpotValues(TableDb db)
    {
        Godot.Collections.Dictionary attrs = db.column_values("attribute_def", "attr_id");
        foreach (string attrId in new[] { "str", "con", "agi", "int", "luk" })
        {
            CheckTrue(attrs.ContainsKey(attrId), $"attribute_def 含 {attrId}");
        }

        // BOM 剥离 + 列别名：attr_to_stat 的 CSV 列 id 映射到行类 row_id
        Resource attrRow = db.get_row("attr_to_stat", 1001);
        CheckNotNull(attrRow, "attr_to_stat 主键 1001 存在");
        if (attrRow != null)
        {
            var row = (AttrToStatRow)attrRow;
            CheckEq(row.row_id, 1001, "列别名 id → row_id 生效");
            CheckEq(row.attr_id, "str", "1001 的来源属性是 str");
        }

        // 复合主键：element_counter / difficulty_drop_rate
        Resource counter = db.get_row("element_counter", "external|internal");
        CheckNotNull(counter, "element_counter 复合主键 external|internal 存在");
        if (counter != null)
        {
            CheckFloat(((ElementCounterRow)counter).multiplier, 0.8, "外功打内功被克 −20%");
        }

        Resource legendNightmare = db.get_row("difficulty_drop_rate", "nightmare|legend");
        CheckNotNull(legendNightmare, "difficulty_drop_rate 复合主键 nightmare|legend 存在");
        if (legendNightmare != null)
        {
            CheckFloat(((DifficultyDropRateRow)legendNightmare).rate_multiplier, 6.67, "绝境下传世掉率 ×6.67");
        }

        // 列别名：enemy_base.exp → exp_reward
        Resource wolf = db.get_row("enemy_base", "en_wolf");
        CheckNotNull(wolf, "enemy_base 主键 en_wolf 存在");
        if (wolf != null)
        {
            var row = (EnemyBaseRow)wolf;
            CheckEq(row.exp_reward, 12, "列别名 exp → exp_reward 生效");
            CheckEq(row.drop_group, "drop_wolf", "野狼引用掉落组 drop_wolf");
        }

        // 多值列解析
        Resource patrol = db.get_row("enemy_team", "team_bandit_patrol");
        CheckNotNull(patrol, "enemy_team 主键 team_bandit_patrol 存在");
        if (patrol != null)
        {
            Godot.Collections.Array members = ((EnemyTeamRow)patrol).parsed_members();
            CheckEq(members.Count, 2, "山贼巡山队有两个成员条目");
            CheckEq(members[0].AsGodotDictionary()["count"].AsInt32(), 2, "第一个成员数量为 2");
        }

        // 0.4.0 新增：角色模板、武器类型、战斗常数、事件判定、非战斗技能
        CheckEq(db.rows("event_skill_def").Count, 5, "五项目非战斗技能");
        Resource scholar = db.get_row("character_base", "scholar_fallen");
        CheckNotNull(scholar, "角色模板 scholar_fallen 存在");
        if (scholar != null)
        {
            var row = (CharacterBaseRow)scholar;
            CheckEq(row.attr_sum(), row.attr_total, "五维之和等于 attr_total");
            CheckEq(row.weapon_type, "sword", "模板用剑");
            CheckGt(row.equip_ids().Length, 0.0, "模板带初始装备");
        }

        Resource blade = db.get_row("weapon_type_def", "blade");
        CheckNotNull(blade, "武器类型 blade 存在");
        if (blade != null)
        {
            CheckEq(Join(((WeaponTypeDefRow)blade).focus_attrs()), "str", "刀主堆力量");
        }

        Resource defConst = db.get_row("combat_const", "def_const");
        CheckNotNull(defConst, "combat_const 里有 def_const");
        if (defConst != null)
        {
            CheckFloat(((CombatConstRow)defConst).value, 60.0, "防御常数 60");
        }

        Resource eventRow = db.get_row("event_check", "ev_shed_trap");
        CheckNotNull(eventRow, "事件判定 ev_shed_trap 存在");
        if (eventRow != null)
        {
            Godot.Collections.Dictionary parsedSource = ((EventCheckRow)eventRow).parsed_source();
            CheckEq(parsedSource["kind"].AsString(), "skill", "事件判定来源是技能");
            CheckEq(parsedSource["target_id"].AsString(), "qimen", "柴房机关用奇门判定");
        }

        // 每个角色模板给 5 项非战斗技能的初始等级：行数随**模板数**走，不写死 5
        // （0.4.0 起模板 1 行、设计要 3–4 行；写死就等于把设计状态钉进用例，见框架说明决策 161）。
        int skillRowCount = db.rows("character_base_skill").Count;
        int templateCount = db.rows("character_base").Count;
        CheckEq(
            skillRowCount, templateCount * 5,
            $"每个角色模板 5 项非战斗技能（{templateCount} 模板 × 5 = {templateCount * 5}，实际 {skillRowCount}）");

        // 0.5.0：装备槽位与商店
        CheckEq(db.rows("equip_slot_def").Count, 7, "七类装备槽位");
        Resource ring = db.get_row("equip_slot_def", "ring");
        CheckNotNull(ring, "戒指槽存在");
        if (ring != null)
        {
            CheckEq(((EquipSlotDefRow)ring).max_equip, 2, "戒指可戴两枚");
        }

        Resource weaponSlot = db.get_row("equip_slot_def", "weapon");
        CheckNotNull(weaponSlot, "武器槽存在");
        if (weaponSlot != null)
        {
            CheckEq(((EquipSlotDefRow)weaponSlot).allowed_weapon_types().Length, 4, "武器槽允许四种武器类型");
        }

        CheckNull(db.get_row("weapon_type_def", "staff"), "棍已从武器类型里移除");
        CheckEq(
            ((EquipBaseRow)db.get_row("equip_base", "eq_head_01")).slot, "shoulder", "前代寨主遗物改挂肩部");
        CheckEq(
            db.rows("shop_stock").Count, 26,
            "货架 26 行（0.23.0 毒酒 +1；0.28.0 行商货架 +4；2026-10-04 秘籍 +4）");

        Resource grocery = db.get_row("building_def", "bld_grocery");
        CheckNotNull(grocery, "杂货铺存在");
        if (grocery != null)
        {
            CheckEq(((BuildingDefRow)grocery).stock_group, "shop_grocery", "杂货铺挂 shop_grocery 货架");
        }

        Resource med = db.get_row("item_base", "item_med_01");
        CheckNotNull(med, "非战斗回血道具存在");
        if (med != null)
        {
            CheckTrue(((ItemBaseRow)med).is_field_use(), "草药汤是非战斗回血");
        }

        Resource potion = db.get_row("item_base", "item_potion_small");
        CheckNotNull(potion, "战斗回血道具存在");
        if (potion != null)
        {
            CheckTrue(((ItemBaseRow)potion).is_battle_use(), "金创药是战斗中回血");
        }
    }
}

