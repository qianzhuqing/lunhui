using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 战斗结算入账（C# 版，对位 `tests/test_level.gd`／`test_dungeon.gd`／`test_affix.gd`
/// 里围着 `BattleReward` 的那几节，以及 `test_end_to_end` 的入账那一段）。
///
/// 这一层是**奖励变成玩家资产的唯一口径**，所以除了正路（钱进钱袋、装备建实例、其余堆叠），
/// 还要钉住三件容易悄悄坏的事：
///   ① 铜钱**不许**变成背包里一行物品（赌局那个真 bug 的正脸）；
///   ② 背包满时算「结算成功但没捡起来」，不把整场判成失败；
///   ③ **没有背包的会话状态**要能现建一个——`Inventory` 还是 GDScript，
///      这条同时钉住 `Call("new")` 在 C# 侧走得通（P5 之前唯一一处这样的调用）。
/// </summary>
public partial class TestBattleReward : TestCase
{
    private const int Seed = 24680;

    public override string SuiteName() => "战斗结算入账（C# 侧）";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckMoneyGoesToPurse(db);
        _CheckEquipmentInstance(db);
        _CheckStackAndOverflow(db);
        _CheckBadInputs(db);
        _CheckExpAndLevelUp(db);
        _CheckMissingInventoryIsCreated(db);
        _CheckApplyReadsRewards(db);
    }

    /// <summary>铜钱进 `inventory.money`，**不进**背包格子。</summary>
    private void _CheckMoneyGoesToPurse(TableDb db)
    {
        GodotObject state = SoloState(db);
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        int money_before = inventory.Get("money").AsInt32();

        Godot.Collections.Dictionary granted = BattleReward.grant_item(db, state, "item_money", 7, null);
        CheckTrue(granted["ok"].AsBool(), "铜钱入账成功");
        CheckEq(granted["kind"].AsString(), "money", "铜钱走「钱袋」那一支");
        CheckEq(granted["money"].AsInt32(), 7, "入账数额写进结果");
        CheckEq(inventory.Get("money").AsInt32(), money_before + 7, "钱真的加到了 inventory.money");
        CheckEq(
            inventory.Call("count", "item_money").AsInt32(), 0,
            "铜钱**不该**变成背包里一行物品（赌局赢钱那个 bug 的正脸）");
    }

    /// <summary>装备发的是实例（带词条），不是「一件 eq_xxx」。</summary>
    private void _CheckEquipmentInstance(TableDb db)
    {
        GodotObject state = SoloState(db);
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        var roller = AffixRoller.create(db, RngService.create(Seed));
        // 开局身上本来就穿着两件（起始装备），所以比的是**增量**，不是绝对值
        int equipment_before = inventory.Call("equipment_count").AsInt32();

        Godot.Collections.Dictionary granted = BattleReward.grant_item(db, state, "eq_sword_03", 2, roller);
        CheckEq(granted["kind"].AsString(), "equip", "装备走「建实例」那一支");
        CheckTrue(granted["ok"].AsBool(), "两件装备都发得出去");
        CheckEq(granted["qty"].AsInt32(), 2, "数量如实回报");
        CheckEq(granted["instance_ids"].AsGodotArray().Count, 2, "两件装备两个实例");
        CheckEq(
            inventory.Call("equipment_count").AsInt32(), equipment_before + 2,
            "背包里多出来两件（开局那两件起始装备不算）");

        string instance_id = granted["instance_ids"].AsGodotArray()[0].AsString();
        CheckEq(inventory.Call("base_of", instance_id).AsString(), "eq_sword_03", "实例认得自己是什么装备");
        var equip = db.get_row("equip_base", "eq_sword_03") as EquipBaseRow;
        CheckNotNull(equip, "配表里有 eq_sword_03");
        var rarity = db.get_row("rarity_def", equip!.rarity) as RarityDefRow;
        CheckNotNull(rarity, "它的稀有度在 rarity_def 里有行");
        int affix_count = inventory.Call("affixes_of", instance_id).AsGodotArray().Count;
        CheckTrue(
            affix_count >= rarity!.affix_min && affix_count <= rarity.affix_max,
            $"词条条数落在稀有度区间 [{rarity.affix_min}, {rarity.affix_max}]（实际 {affix_count}）");
    }

    /// <summary>背包满：溢出单独记账，结算照旧算成功。</summary>
    private void _CheckStackAndOverflow(TableDb db)
    {
        GodotObject state = SoloState(db);
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        // 草药堆叠上限就是 99
        inventory.Call("add_item", db, "item_herb", 99);
        int money_before = inventory.Get("money").AsInt32();

        Godot.Collections.Dictionary applied = BattleReward.grant(
            db, state, 10, 5, _Drops(("item_herb", 5)), null);
        CheckTrue(applied["ok"].AsBool(), "背包满不算结算失败");
        CheckEq(applied["errors"].AsGodotArray().Count, 0, "errors 是空的（这不是错误，是没捡起来）");
        CheckEq(applied["overflow"].AsGodotArray().Count, 1, "溢出单独记一条");
        Godot.Collections.Dictionary entry = applied["overflow"].AsGodotArray()[0].AsGodotDictionary();
        CheckEq(entry["item_id"].AsString(), "item_herb", "溢出的是草药");
        CheckEq(entry["qty"].AsInt32(), 5, "溢出数量写的是没捡起来的量");
        CheckEq(state.Get("party_exp").AsInt32(), 10, "经验照常入账");
        CheckEq(inventory.Get("money").AsInt32(), money_before + 5, "铜钱照常入账");
        CheckEq(inventory.Call("count", "item_herb").AsInt32(), 99, "背包没被塞爆");

        // 部分入包：还剩 2 格、奖励 5 个 → 进去 2、溢出 3
        GodotObject partial_state = SoloState(db);
        GodotObject partial_inventory = partial_state.Get("inventory").AsGodotObject();
        partial_inventory.Call("add_item", db, "item_herb", 97);
        Godot.Collections.Dictionary partial = BattleReward.grant(
            db, partial_state, 0, 0, _Drops(("item_herb", 5)), null);
        CheckTrue(partial["ok"].AsBool(), "部分入包也不算结算失败");
        CheckEq(partial["items"].AsGodotArray().Count, 1, "入账清单里记了这一条");
        Godot.Collections.Dictionary item_entry = partial["items"].AsGodotArray()[0].AsGodotDictionary();
        CheckEq(item_entry["qty"].AsInt32(), 2, "真正进去 2 个");
        CheckEq(item_entry["overflow"].AsInt32(), 3, "这一条自己记着溢出 3");
        CheckEq(partial_inventory.Call("count", "item_herb").AsInt32(), 99, "另一局的背包装的是它自己的 99 个");
    }

    /// <summary>发不出去的三种情形都要如实报错，不静默。</summary>
    private void _CheckBadInputs(TableDb db)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Dictionary no_id = BattleReward.grant_item(db, state, "", 1, null);
        CheckFalse(no_id["ok"].AsBool(), "空 id 拒收");
        CheckTrue(no_id["error"].AsString().Contains("id"), "理由说清是「没有奖励物品 id」");

        Godot.Collections.Dictionary unknown = BattleReward.grant_item(db, state, "item_does_not_exist", 1, null);
        CheckFalse(unknown["ok"].AsBool(), "配表里没有的东西发不出去");
        CheckTrue(unknown["error"].AsString().Length > 0, "失败必须带理由");

        Godot.Collections.Dictionary no_state = BattleReward.grant(
            null, null, 5, 5, new Godot.Collections.Array(), null);
        CheckFalse(no_state["ok"].AsBool(), "没有会话状态时如实失败");
        CheckEq(no_state["errors"].AsGodotArray().Count, 1, "失败原因记在 errors 里");
    }

    /// <summary>经验进队伍池，付得起的等级立刻换出来。</summary>
    private void _CheckExpAndLevelUp(TableDb db)
    {
        GodotObject state = SoloState(db);
        CheckEq(state.Call("level_of", SoloCharId).AsInt32(), 1, "新档 1 级");
        Godot.Collections.Dictionary applied = BattleReward.grant(
            db, state, 5000, 0, new Godot.Collections.Array(), null);
        CheckEq(applied["exp"].AsInt32(), 5000, "经验如实回报");
        CheckGt(applied["level_ups"].AsGodotArray().Count, 0.0, "付得起的等级被换出来了");
        CheckTrue(applied["level_up_text"].AsString().Length > 0, "升级文案给了一句");
        CheckGt(state.Call("level_of", SoloCharId).AsInt32(), 1.0, "等级真的涨了");
        CheckGt(state.Get("party_exp").AsInt32(), 0.0, "队伍池里还剩余额");

        // 负数当 0：不给负经验、也不倒扣钱
        GodotObject other = SoloState(db);
        Godot.Collections.Dictionary negative = BattleReward.grant(
            db, other, -50, -50, new Godot.Collections.Array(), null);
        CheckEq(negative["exp"].AsInt32(), 0, "负经验按 0");
        CheckEq(negative["money"].AsInt32(), 0, "负铜钱按 0");
        CheckEq(other.Get("party_exp").AsInt32(), 0, "没往队伍池里塞负数");
    }

    /// <summary>没有背包的会话状态：现建一个，别把奖励丢了。</summary>
    private void _CheckMissingInventoryIsCreated(TableDb db)
    {
        GodotObject state = SoloState(db);
        state.Set("inventory", default(Variant));
        CheckNull(state.Get("inventory").AsGodotObject(), "先把背包摘掉");

        // 注意：现建背包这一步在 `grant()` 里（`grant_item()` 照 GDScript 的口径要求背包已存在）
        Godot.Collections.Dictionary granted = BattleReward.grant(
            db, state, 0, 0, _Drops(("item_herb", 1)), null);
        CheckTrue(granted["ok"].AsBool(), "没有背包时现建一个再发");
        GodotObject rebuilt = state.Get("inventory").AsGodotObject();
        CheckNotNull(rebuilt, "背包被建出来并写回状态");
        int rebuilt_count = rebuilt == null ? -1 : rebuilt.Call("count", "item_herb").AsInt32();
        CheckEq(rebuilt_count, 1, "东西真的落进新背包了");
    }

    /// <summary>`apply` 从 `simulate()` 的结果里读奖励，缺字段按 0。</summary>
    private void _CheckApplyReadsRewards(TableDb db)
    {
        GodotObject state = SoloState(db);
        var result = new Godot.Collections.Dictionary
        {
            {
                "rewards", new Godot.Collections.Dictionary
                {
                    { "exp", 12 },
                    { "money", 3 },
                }
            },
        };
        Godot.Collections.Dictionary applied = BattleReward.apply(
            db, state, result, new Godot.Collections.Array(), null);
        CheckEq(applied["exp"].AsInt32(), 12, "apply 从 result.rewards 里读经验");
        CheckEq(applied["money"].AsInt32(), 3, "apply 从 result.rewards 里读铜钱");
        CheckEq(state.Get("party_exp").AsInt32(), 12, "读出来的经验真的入账了");

        Godot.Collections.Dictionary empty = BattleReward.apply(
            db, state, new Godot.Collections.Dictionary(), new Godot.Collections.Array(), null);
        CheckEq(empty["exp"].AsInt32(), 0, "没有 rewards 字段时经验按 0");
        CheckEq(empty["money"].AsInt32(), 0, "没有 rewards 字段时铜钱按 0");
        CheckTrue(empty["ok"].AsBool(), "空结算也算成功（只是什么都没发）");
    }

    private static Godot.Collections.Array _Drops(params (string ItemId, int Qty)[] entries)
    {
        var drops = new Godot.Collections.Array();
        foreach ((string item_id, int qty) in entries)
        {
            drops.Add(new Godot.Collections.Dictionary
            {
                { "item_id", item_id },
                { "qty", qty },
            });
        }
        return drops;
    }
}
