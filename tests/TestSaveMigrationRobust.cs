using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 存档迁移第三族：老档里已经下架的角色要换成能玩的人、坏档要判成损坏、
/// **字段类型**写错的档不许在运行期炸。
/// （对位 `tests/test_save_migration.gd` 的对应几节，2026-10-11 P4 第三十五批整体搬过来；
/// 公共基类与拆分理由见 `TestSaveMigrationBase`。）
/// </summary>
public partial class TestSaveMigrationRobust : TestSaveMigrationBase
{
    public override string SuiteName() => "存档迁移的坏档与下架角色";

    public override void Run()
    {
        TableDb db = GetDb();
        _CleanDir(TestDir);
        _CheckRetiredCharacters(db);
        _CheckCorrupt(db);
        _CheckTypeMismatchedFields(db);
    }

    /// <summary>
    /// 老档里已经下架的角色（早期是 ch_gang／ch_du／ch_ci／ch_qi 四个，现在只有一个模板）：
    /// 读档要把它们剔掉、换成一个能玩的角色，等级取最高值，装备退回背包。
    /// 不清的话队伍是空的——每场战斗瞬间判负，玩家还看不出为什么。
    /// </summary>
    private void _CheckRetiredCharacters(TableDb db)
    {
        var legacy = new Godot.Collections.Dictionary
        {
            { "version", 4 },
            { "char_ids", new Godot.Collections.Array { "ch_gang", "ch_du", "ch_ci", "ch_qi" } },
            {
                "char_levels",
                new Godot.Collections.Dictionary
                {
                    { "ch_gang", 9 }, { "ch_du", 4 }, { "ch_ci", 4 }, { "ch_qi", 4 },
                }
            },
            { "difficulty_id", "normal" },
            { "chapter_id", "chapter_01" },
            { "slot", 1 },
            { "created_unix", 11 },
            { "saved_unix", 22 },
            {
                "inventory",
                new Godot.Collections.Dictionary
                {
                    { "stacks", new Godot.Collections.Dictionary { { "item_herb", 3 } } },
                    {
                        "equipment",
                        new Godot.Collections.Dictionary
                        {
                            {
                                "eq_sword_01#9",
                                new Godot.Collections.Dictionary
                                {
                                    { "base_id", "eq_sword_01" },
                                    { "affixes", new Godot.Collections.Array() },
                                }
                            },
                        }
                    },
                    {
                        "equipped",
                        new Godot.Collections.Dictionary
                        {
                            {
                                "ch_gang",
                                new Godot.Collections.Dictionary
                                {
                                    { "weapon", new Godot.Collections.Array { "eq_sword_01#9" } },
                                }
                            },
                        }
                    },
                    { "next_uid", 10 },
                    { "money", 77 },
                }
            },
        };

        // 这条用例的剧本是「老档里那 4 个 char_id 已经不在表里」。而 01 的备选模板里正好有同名
        // `ch_gang`／`ch_du`／`ch_ci`／`ch_qi`——一旦把它们启用，这些角色就**不是下架角色**了，
        // 剧本不再成立。所以把 roster 钉成单模板夹具（框架说明决策 161）。
        TableDb roster = TableWithNChars(1);
        GameState state = GameState.from_dict(legacy, roster);
        CheckNotNull(state, "带下架角色的老档能读进来");
        if (state == null)
        {
            return;
        }

        // 接手几个角色由**表里现有模板数**决定（上限 MAX_PARTY）：1 个模板时 1 个、4 个时 4 个。
        // 写死"1 个"会把设计状态钉进用例（框架说明决策 161）。
        CheckEq(state.pruned_characters, 4, "记下剔掉了 4 个下架角色");
        CheckEq(state.char_ids.Count, 1, "换来 1 个表里还在的角色");
        string newId = state.char_ids[0].AsString();
        CheckEq(newId, "scholar_fallen", "接手的角色取自 character_base");
        CheckEq(state.level_of(newId), 9, "等级取被剔角色的最高值，进度不倒退");
        Inventory inventory = _InventoryOf(state);
        CheckEq(inventory.count("item_herb"), 3, "背包材料没丢");
        CheckEq(inventory.money, 77, "铜钱没丢");
        CheckTrue(inventory.equipment_count() >= 1, "老档那件装备还在（下架角色身上的也退回背包）");
        CheckTrue(
            inventory.equipped_by("eq_sword_01#9").Count == 0,
            "装备不再挂在下架角色身上（退回背包了）");

        // 接手角色要能真的进战斗：有招、有气血、能造出战斗单位
        BattleActor actor = PartyBuilder.build_actor(db, state, newId);
        CheckNotNull(actor, "接手角色能造出战斗单位");
        if (actor != null)
        {
            CheckGt(actor.max_hp(), 0.0, "有气血上限");
            CheckGt(actor.skills.Length, 0.0, "有起手招式");
        }

        CheckEq(state.version, GameState.save_version(), "版本升到当前");
        // 写回一遍：下架角色不再出现
        GameState back = GameState.from_dict(state.to_dict(), db);
        CheckEq(back.pruned_characters, 0, "写回后再读没有可剔的角色");
        CheckEq(back.char_ids[0].AsString(), "scholar_fallen", "写回后队伍稳定");
    }

    private void _CheckCorrupt(TableDb db)
    {
        SaveStore store = SaveStore.create(TestDir, Slots);
        WriteText(store.slot_path(3), "这不是 JSON");
        CheckFalse(store.load_slot(3, db)["ok"].AsBool(), "坏档读不出来");
        CheckTrue(store.describe_slot(3, db)["corrupt"].AsBool(), "坏档被标成损坏");
    }

    /// <summary>
    /// **字段类型**写错的档（JSON 合法，`is_valid_dict` 也可能认）：要么被判成损坏，
    /// 要么被容错读成缺省——**不许在运行期炸**。成因很现实：手改过的档、以后版本换了字段类型、
    /// 外部工具生成的档。
    ///
    /// 每一条都走「加载 → 消费方」整条链（保底计数／状态行／写回再读／首杀／气血）：
    /// 中间任何一步抛运行期错误，它**后面的断言就不会执行**，`run_tests` 的「假绿」门限会当场点名
    /// ——所以这几条断言值本身不必花哨，位置才是重点。
    /// </summary>
    private void _CheckTypeMismatchedFields(TableDb db)
    {
        var cases = new (string Name, Godot.Collections.Dictionary Data)[]
        {
            ("inventory 是字符串", new Godot.Collections.Dictionary
            {
                { "char_ids", new Godot.Collections.Array { "scholar_fallen" } },
                { "char_levels", new Godot.Collections.Dictionary { { "scholar_fallen", 1 } } },
                { "difficulty_id", "normal" },
                { "inventory", "oops" },
            }),
            ("inventory 是数字", new Godot.Collections.Dictionary
            {
                { "char_ids", new Godot.Collections.Array { "scholar_fallen" } },
                { "char_levels", new Godot.Collections.Dictionary { { "scholar_fallen", 1 } } },
                { "difficulty_id", "normal" },
                { "inventory", 7 },
            }),
            ("char_ids 里塞数字", new Godot.Collections.Dictionary
            {
                { "char_ids", new Godot.Collections.Array { 1, 2 } },
                { "char_levels", new Godot.Collections.Dictionary() },
                { "difficulty_id", "normal" },
            }),
            ("char_levels 里塞字符串", new Godot.Collections.Dictionary
            {
                { "char_ids", new Godot.Collections.Array { "scholar_fallen" } },
                { "char_levels", new Godot.Collections.Dictionary { { "scholar_fallen", "三" } } },
                { "difficulty_id", "normal" },
            }),
            ("pity 是数组", new Godot.Collections.Dictionary
            {
                { "char_ids", new Godot.Collections.Array { "scholar_fallen" } },
                { "char_levels", new Godot.Collections.Dictionary { { "scholar_fallen", 1 } } },
                { "difficulty_id", "normal" },
                { "pity", new Godot.Collections.Array { 1, 2 } },
            }),
            ("field_buffs 是字符串", new Godot.Collections.Dictionary
            {
                { "char_ids", new Godot.Collections.Array { "scholar_fallen" } },
                { "char_levels", new Godot.Collections.Dictionary { { "scholar_fallen", 1 } } },
                { "difficulty_id", "normal" },
                { "field_buffs", "x" },
            }),
        };

        foreach ((string label, Godot.Collections.Dictionary data) in cases)
        {
            if (!GameState.is_valid_dict(data))
            {
                // 判成损坏也是一种合法处理：这里只记一条，别让它悄悄溜过去
                CheckFalse(GameState.is_valid_dict(data), $"{label}：类型不对时判成损坏档");
                continue;
            }

            GameState state = GameState.from_dict(data, db);
            CheckNotNull(state, $"{label}：判成有效就必须读得出来（不许崩）");
            if (state == null)
            {
                continue;
            }

            CheckEq(state.pity_tracker().attempts("x"), 0, $"{label}：保底计数读得出（新读出来是空的）");
            CheckTrue(
                string.Join("\n", state.summary_lines(db)).Contains("章节："),
                $"{label}：状态行读得出来（不会因为某个字段类型不对就整页空）");
            GameState again = GameState.from_dict(state.to_dict(), db);
            CheckNotNull(again, $"{label}：写回再读一遍也稳");
            if (again != null)
            {
                CheckEq(
                    again.to_dict().Keys.Count, state.to_dict().Keys.Count,
                    $"{label}：往返前后字段数一致");
            }

            CheckEq(state.first_kill_count(), 0, $"{label}：首杀计数读得出（没记过就是 0）");
            CheckFalse(state.is_wounded("scholar_fallen"), $"{label}：没有气血记录 = 满血（不是残血）");
        }
    }
}
