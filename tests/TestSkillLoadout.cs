using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 武学：修习门槛、招式槽、内功占格、装配与卸下、存档往返（C# 版，对位原 `tests/test_skill_loadout.gd`）。
///
/// 规则出处设计 01：招式槽 2+等级/5+悟性/8（上限 9）、内功容量 2+等级/6+根骨/10（上限 7）、
/// 内功按星级占 1~3 格、高星武学要属性门槛。
/// </summary>
public partial class TestSkillLoadout : TestCase
{
    private const string CharId = "scholar_fallen";

    public override string SuiteName() => "武学装配";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        SkillLoadout loadout = SkillLoadout.create(db, state, CharId);

        _CheckStarting(db, state, loadout);
        _CheckLearnRequirement(db, state);
        _CheckWeaponRequirement(db, state);
        _CheckActiveSlots(db, state);
        _CheckPassiveCapacity(db, state);
        _CheckPassiveBonus(db, state);
        _CheckRoundTrip(db, state);
        _CheckNoSelfLoop();
        _CheckBasicSlot(db, state);
    }

    /// <summary>
    /// 普攻位（设计 04 §武学页三块玩家配置，2026-10-13）：**只装已学的免费招**
    /// （`qi_cost = 0` 且 `cooldown = 0`）、**与招式位互斥**、空＝系统普攻；进存档、
    /// 而且**装上之后战斗里那一下真的换成它**（吃段数、进记录）。
    /// </summary>
    private void _CheckBasicSlot(TableDb db, GodotObject state)
    {
        SkillLoadout loadout = SkillLoadout.create(db, state, CharId);
        CheckEq(loadout.basic_id(), "", "开局普攻位是空的（照旧用系统普攻）");
        CheckTrue(loadout.can_set_basic("sk_xuanwei_01")["ok"].AsBool(), "免费招能装上（起手式）");
        CheckFalse(
            loadout.can_set_basic("sk_xuanwei_02")["ok"].AsBool(),
            "要内力的招装不上（普攻位就是「替换普攻」那一档）");
        CheckTrue(
            loadout.can_set_basic("sk_xuanwei_02")["error"].AsString().Contains("免费"),
            "装不上时把原因写出来");

        // 互斥：**先把起手式装进招式位**，再想装普攻位就该被拦住（两边不可能同时占着）
        if (!_Has(loadout.active_ids(), "sk_xuanwei_01") && loadout.can_equip("sk_xuanwei_01")["ok"].AsBool())
        {
            loadout.toggle("sk_xuanwei_01");
        }

        CheckFalse(
            loadout.can_set_basic("sk_xuanwei_01")["ok"].AsBool(),
            "已经在招式位上的招装不进普攻位（互斥）");
        CheckTrue(
            loadout.can_set_basic("sk_xuanwei_01")["error"].AsString().Contains("招式位"),
            "拦下来时说清是「招式位」那一半");
        if (_Has(loadout.active_ids(), "sk_xuanwei_01"))
        {
            loadout.toggle("sk_xuanwei_01");   // 先从招式位卸下
        }

        CheckTrue(loadout.set_basic("sk_xuanwei_01")["ok"].AsBool(), "卸下招式位之后装到普攻位");
        CheckEq(loadout.basic_id(), "sk_xuanwei_01", "普攻位记下来了");
        CheckFalse(
            loadout.can_equip("sk_xuanwei_01")["ok"].AsBool(),
            "互斥的另一半：已经在普攻位上，就装不进招式位");

        // 进存档（v16 的 `char_loadouts.basic`）＋ 读回来还在
        GameState back = GameState.from_dict(((GameState)state).to_dict(), db);
        CheckEq(
            back.loadout_of(CharId)["basic"].AsString(), "sk_xuanwei_01",
            "普攻位跟着存档走（v16 新字段）");
        CheckEq(
            SkillLoadout.create(db, back, CharId).basic_id(), "sk_xuanwei_01",
            "读档后普攻位还在");

        // 战斗里那一下真换成它：日志点的是这一招的名字，而且**计入招式使用记录**
        BattleActor actor = BattleActor.from_character(
            db, db.get_row("character_base", CharId), 3,
            new Godot.Collections.Dictionary(), new Godot.Collections.Array());
        actor.actor_id = CharId;
        actor.basic_skill_id = "sk_xuanwei_01";
        actor.skills = new[] { "sk_xuanwei_01" };
        BattleActor foe = BattleActor.from_character(
            db, db.get_row("character_base", CharId), 3,
            new Godot.Collections.Dictionary(), new Godot.Collections.Array());
        foe.actor_id = "foe";
        foe.side = BattleActor.side_enemy();
        foe.hp = foe.max_hp();
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(7));
        Godot.Collections.Array lines = sim.basic_attack(actor, foe);
        string text = string.Join("｜", lines);
        CheckTrue(
            text.Contains(db.display_name("sk_xuanwei_01")),
            $"装上之后普攻打的就是那一招（战报点名）：{text}");
        CheckEq(
            loadout.set_basic("")["ok"].AsBool(), true, "卸下普攻位（空串＝回系统普攻）");
        CheckEq(SkillLoadout.create(db, state, CharId).basic_id(), "", "卸下之后是空的");
    }

    /// <summary>开局：已学 = 模板的起始武学，装配已经铺好，槽位按公式算。</summary>
    private void _CheckStarting(TableDb db, GodotObject state, SkillLoadout loadout)
    {
        CheckTrue(loadout.valid(), "装配对象有效");
        CheckTrue(state.Call("is_learned", CharId, "sk_xuanwei_01").AsBool(), "模板起始武学算已学");
        CheckFalse(state.Call("is_learned", CharId, "sk_xuanwei_05").AsBool(), "没学过的武学不算已学");
        CheckEq(loadout.learned_ids().Length, 1, "开局只会一部（角色卡的起始武学）");
        CheckTrue(_Has(loadout.active_ids(), "sk_xuanwei_01"), "起始武学默认装在招式槽里");
        CheckTrue(loadout.passive_ids().Length == 0, "开局没有内功");
        CheckEq(loadout.active_slots(), 3, "1 级书生招式槽 3（2 + 0 + 悟10/8）");
        CheckEq(loadout.passive_capacity(), 2, "1 级书生内功容量 2（2 + 0 + 根5/10）");
        CheckEq(state.Call("collected_skill_count").AsInt32(), 1, "图鉴计数按已学去重");
    }

    /// <summary>修习门槛：★3 招式要悟性 12，1 级书生只有 10 → 学不了；加到 12 就能学。</summary>
    private void _CheckLearnRequirement(TableDb db, GodotObject state)
    {
        SkillLoadout loadout = SkillLoadout.create(db, state, CharId);
        Godot.Collections.Dictionary requirement = loadout.requirement_of("sk_xuanwei_05");
        CheckFalse(requirement["ok"].AsBool(), "悟性不足时门槛不过");
        CheckEq(requirement["attr"].AsString(), "wu", "门槛属性是悟性");
        CheckEq(requirement["value"].AsInt32(), 12, "门槛值取表里的 12");
        CheckTrue(
            requirement["reason"].AsString().Contains("悟性"),
            $"原因里写清要什么属性：{requirement["reason"].AsString()}");

        Godot.Collections.Dictionary refused = loadout.learn("sk_xuanwei_05");
        CheckFalse(refused["ok"].AsBool(), "门槛不足时学不会");
        CheckTrue(
            refused["error"].AsString().Contains("修习门槛"),
            $"给出修习门槛的提示：{refused["error"].AsString()}");
        CheckFalse(state.Call("is_learned", CharId, "sk_xuanwei_05").AsBool(), "被拒之后没写进已学");

        // 悟性是**资质**（设计 0.13.0：不能加点），这里用「复制模板行、直接给资质」的夹具，
        // 而不是往这份**共用存档**里塞 10 部武学——那会把图鉴奖励一起带给后面的用例。
        TableDb raised_db = TableWithTalents(12, 5);
        GodotObject raised_state = SoloState(raised_db);
        SkillLoadout raised = SkillLoadout.create(raised_db, raised_state, CharId);
        CheckEq(raised.attrs()["wu"].AsInt32(), 12, "夹具把悟性给到 12（对应图鉴奖励那条通道）");
        CheckTrue(raised.requirement_of("sk_xuanwei_05")["ok"].AsBool(), "悟性 12 后门槛过了");
        Godot.Collections.Dictionary learned = raised.learn("sk_xuanwei_05");
        CheckTrue(learned["ok"].AsBool() && learned["new"].AsBool(), "过了门槛就能学会");
        CheckTrue(
            raised_state.Call("is_learned", CharId, "sk_xuanwei_05").AsBool(), "学会后写进已学");
        Godot.Collections.Dictionary again = raised.learn("sk_xuanwei_05");
        CheckTrue(again["ok"].AsBool() && !again["new"].AsBool(), "重复学不算失败也不算新学");
    }

    /// <summary>武器类型：拿剑时装不了拳法；卸下剑连剑招也装不上，但通用招式任何时候都能装。</summary>
    private void _CheckWeaponRequirement(TableDb db, GodotObject state)
    {
        SkillLoadout loadout = SkillLoadout.create(db, state, CharId);
        loadout.learn("sk_chensha_01");
        loadout.learn("sk_common_01");

        Godot.Collections.Dictionary fist = loadout.can_equip("sk_chensha_01");
        CheckFalse(fist["ok"].AsBool(), "拿剑时拳法装不上");
        CheckTrue(
            fist["error"].AsString().Contains("武器不符"),
            $"说明是武器不符：{fist["error"].AsString()}");

        CheckTrue(loadout.can_equip("sk_common_01")["ok"].AsBool(), "通用招式任何武器都能装");

        // 卸下武器：剑招装不上，通用招式照装
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        string instance_id = inventory.Call("equipped_instances", db, CharId).AsStringArray()[0];
        inventory.Call("unequip", CharId, "weapon", 0);
        SkillLoadout bare = SkillLoadout.create(db, state, CharId);
        CheckTrue(bare.equipped_weapon_type().Length == 0, "卸下后身上没武器");
        CheckFalse(bare.can_equip("sk_xuanwei_02")["ok"].AsBool(), "空手装不了剑招");
        CheckTrue(bare.can_equip("sk_common_01")["ok"].AsBool(), "空手也能装通用招式");
        // 装回去，后面的用例还要用剑
        inventory.Call("equip", db, CharId, 1, "sword", instance_id);
    }

    /// <summary>招式槽上限：装满 3 个之后第 4 个被拒；卸一个又能装。</summary>
    private void _CheckActiveSlots(TableDb db, GodotObject state)
    {
        SkillLoadout loadout = SkillLoadout.create(db, state, CharId);
        foreach (string skill_id in new[] { "sk_xuanwei_02", "sk_xuanwei_03", "sk_xuanwei_04", "sk_common_02" })
        {
            loadout.learn(skill_id);
        }

        // 先清空装配，再按槽位一个个装
        foreach (string skill_id in loadout.active_ids())
        {
            loadout.unequip(skill_id);
        }

        CheckEq(loadout.active_used(), 0, "清空后招式槽为空");

        int filled = 0;
        foreach (string skill_id in new[] { "sk_xuanwei_01", "sk_xuanwei_02", "sk_xuanwei_03" })
        {
            if (loadout.equip(skill_id)["ok"].AsBool())
            {
                filled += 1;
            }
        }

        CheckEq(filled, 3, "前 3 个都装上了");
        CheckEq(loadout.active_used(), loadout.active_slots(), "招式槽正好装满");

        // 第 4 个（通用、武器不限）应该被槽位拦住
        string fourth = "";
        foreach (string skill_id in loadout.learned_ids())
        {
            if (_Has(loadout.active_ids(), skill_id))
            {
                continue;
            }

            if (db.get_row("skill_base", skill_id) is not SkillBaseRow row || !row.is_active())
            {
                continue;
            }

            Godot.Collections.Dictionary gate = loadout.can_equip(skill_id);
            if (gate["ok"].AsBool() || gate["error"].AsString().Contains("招式槽已满"))
            {
                fourth = skill_id;
                break;
            }
        }

        CheckNe(fourth, "", "找到一个可比较的第 4 个招式");
        if (fourth.Length == 0)
        {
            return;
        }

        Godot.Collections.Dictionary overflow = loadout.can_equip(fourth);
        CheckFalse(overflow["ok"].AsBool(), "招式槽满了装不上第 4 个");
        CheckTrue(
            overflow["error"].AsString().Contains("招式槽已满"),
            $"说明是槽位满：{overflow["error"].AsString()}");

        CheckTrue(loadout.unequip("sk_xuanwei_01")["ok"].AsBool(), "能卸下招式");
        CheckEq(loadout.active_used(), 2, "卸下后少一个");
        CheckTrue(loadout.equip(fourth)["ok"].AsBool(), "腾出槽位后第 4 个能装上");
        CheckTrue(loadout.unequip(fourth)["ok"].AsBool(), "收尾：把它卸掉");
    }

    /// <summary>内功占格：容量 2 格，装 2 部★1 就满；★2 内功要根骨 8。</summary>
    private void _CheckPassiveCapacity(TableDb db, GodotObject state)
    {
        SkillLoadout loadout = SkillLoadout.create(db, state, CharId);
        loadout.learn("pf_xuanwei_01");
        loadout.learn("pf_wudu_01");
        CheckEq(loadout.passive_slot_cost("pf_xuanwei_01"), 1, "★1 内功占 1 格");
        CheckEq(loadout.passive_capacity(), 2, "容量 2 格");

        CheckTrue(loadout.equip("pf_xuanwei_01")["ok"].AsBool(), "第 1 部内功装得上");
        CheckEq(loadout.passive_used(), 1, "占 1 格");
        CheckTrue(loadout.equip("pf_wudu_01")["ok"].AsBool(), "第 2 部内功装得上");
        CheckEq(loadout.passive_used(), 2, "占满 2 格");

        loadout.learn("pf_yaowang_01");
        Godot.Collections.Dictionary overflow = loadout.can_equip("pf_yaowang_01");
        CheckFalse(overflow["ok"].AsBool(), "容量满了装不上第 3 部");
        CheckTrue(
            overflow["error"].AsString().Contains("内功容量不足"),
            $"说明是容量不足：{overflow["error"].AsString()}");

        Godot.Collections.Dictionary summary = loadout.slot_summary();
        CheckEq(summary["passive_used"].AsInt32(), 2, "面板上的内功占格数正确");
        CheckEq(summary["passive_capacity"].AsInt32(), 2, "面板上的内功容量正确");

        // ★2 内功要根骨 8：书生只有 5，门槛不过
        CheckTrue(state.Call("is_learned", CharId, "pf_xuanwei_01").AsBool(), "内功也能学会");
        Godot.Collections.Dictionary gated = loadout.requirement_of("pf_xuanwei_03");
        CheckFalse(gated["ok"].AsBool(), "根骨不够时 ★2 内功门槛不过");
        CheckEq(gated["attr"].AsString(), "gen", "★2 内功门槛属性是根骨");
        CheckEq(gated["value"].AsInt32(), 8, "门槛值取星级表默认值 8");
    }

    /// <summary>内功「装备即生效」：加了内力上限，卸下就还原；判定值不受装配影响。</summary>
    private void _CheckPassiveBonus(TableDb db, GodotObject state)
    {
        CharacterSheet sheet = _Sheet(db, state);
        double before = sheet.stat_value("qi_max");
        sheet.loadout().unequip("pf_xuanwei_01");
        double without = _Sheet(db, state).stat_value("qi_max");
        sheet.loadout().equip("pf_xuanwei_01");
        double with_passive = _Sheet(db, state).stat_value("qi_max");
        CheckFloat(without, before - 30.0, "卸下内功后内力上限掉 30");
        CheckFloat(with_passive, before, "装回内功后内力上限还原");

        // 内功给的是派生数值不是五维：判定值（只用裸属性）不该变
        Godot.Collections.Dictionary naked_before = sheet.naked_attrs();
        sheet.loadout().unequip("pf_xuanwei_01");
        Godot.Collections.Dictionary naked_after = _Sheet(db, state).naked_attrs();
        _CheckSameDict(naked_after, naked_before, "内功加成不进裸属性（判定值不受装配影响）");
        sheet.loadout().equip("pf_xuanwei_01");
    }

    /// <summary>存档往返：已学与装配都要留下；玩家自己卸空的槽不许被迁移逻辑悄悄填上。</summary>
    private void _CheckRoundTrip(TableDb db, GodotObject state)
    {
        SkillLoadout loadout = SkillLoadout.create(db, state, CharId);
        foreach (string skill_id in loadout.active_ids())
        {
            loadout.unequip(skill_id);
        }

        CheckEq(_SheetActive(state), 0, "把招式槽清空（模拟玩家主动卸下）");

        Godot.Collections.Dictionary data = state.Call("to_dict").AsGodotDictionary();
        GodotObject back = GameState.from_dict(data, db);
        CheckNotNull(back, "装配状态能读回来");
        if (back == null)
        {
            return;
        }

        CheckEq(back.Get("migrated_from").AsInt32(), 0, "同版本往返不需要迁移");
        CheckTrue(back.Call("is_learned", CharId, "sk_xuanwei_01").AsBool(), "已学列表往返一致");
        CheckTrue(back.Call("is_learned", CharId, "pf_xuanwei_01").AsBool(), "内功的已学也往返一致");
        CheckEq(
            back.Call("collected_skill_count").AsInt32(),
            state.Call("collected_skill_count").AsInt32(),
            "图鉴计数往返一致");
        CheckEq(_SheetActive(back), 0, "玩家卸空的招式槽不会被迁移重新填上");
        CheckTrue(
            _Has(_StringsOf(back.Call("loadout_of", CharId).AsGodotDictionary()["passive"]), "pf_xuanwei_01"),
            "内功装配往返一致");
        // `state.version` 就是当前版本（`var version: int = VERSION`），对位原断言里的常量比较
        CheckEq(back.Get("version").AsInt32(), state.Get("version").AsInt32(), "写回的版本是当前版本");
    }

    /// <summary>
    /// 内功加成不许反过来影响槽位与门槛：★5 内功（玄微心法·太清）自己给悟性 +5，而悟性正是招式槽的入参。
    /// 要是槽位按「含内功」的属性算，装哪部内功就变成了顺序游戏（决策 22 明确禁止）。
    /// </summary>
    private void _CheckNoSelfLoop()
    {
        TableDb db = TableWithTalents(19, 25);
        GodotObject state = SoloState(db);
        state.Get("char_levels").AsGodotDictionary()[CharId] = 20;
        // 夹具给的是根骨 25（够 ★5 内功门槛与容量）与悟性 19（卡在 /8 的边界上，+5 就会多一格）

        SkillLoadout loadout = SkillLoadout.create(db, state, CharId);
        CheckEq(loadout.passive_capacity(), 7, "20 级根骨 25：内功容量满格 7");
        int slots_before = loadout.active_slots();
        int wu_before = _Sheet(db, state).total_attrs()["wu"].AsInt32();
        CheckEq(slots_before, 2 + 4 + wu_before / 8, $"招式槽 = 2 + 20/5 + 悟性/8（按当前悟性 {wu_before}）");

        Godot.Collections.Dictionary learned = loadout.learn("pf_xuanwei_05");
        CheckTrue(learned["ok"].AsBool(), $"根骨 25 够 ★5 内功的修习门槛：{learned["error"].AsString()}");
        int cost = loadout.passive_slot_cost("pf_xuanwei_05");
        int used_before = loadout.passive_used();
        Godot.Collections.Dictionary equipped = loadout.equip("pf_xuanwei_05");
        CheckTrue(equipped["ok"].AsBool(), $"★5 内功（占 {cost} 格）装得上：{equipped["error"].AsString()}");
        CheckEq(loadout.passive_used(), used_before + cost, "占格数按 slot_cost 增加");

        CharacterSheet sheet = _Sheet(db, state);
        int wu_after = sheet.total_attrs()["wu"].AsInt32();
        CheckEq(wu_after, wu_before + 5, "内功的悟性 +5 确实进了五维");
        CheckEq(
            wu_after / 8,
            wu_before / 8 + 1,
            $"这个 +5 正好跨过 /8 的整数边界（悟性 {wu_before} → {wu_after}），所以下一条断言是有意义的");
        CheckEq(
            sheet.loadout().active_slots(),
            slots_before,
            "但招式槽不跟着涨（不含内功）");
        CheckEq(
            sheet.stats()["slot_active"].AsInt32(),
            slots_before,
            "面板显示的招式槽与判定口径一致（不是 per-面板一个数 per-判定另一个数）");
        CheckEq(
            sheet.stats()["passive_capacity"].AsInt32(),
            7,
            "面板显示的内功容量也是 7");
    }

    private static CharacterSheet _Sheet(TableDb db, GodotObject state) =>
        new(db, state, CharId);

    private static int _SheetActive(GodotObject state) =>
        _StringsOf(state.Call("loadout_of", CharId).AsGodotDictionary()["active"]).Length;

    /// <summary>
    /// `PackedStringArray` 与 `Array` 两种形状都收（`loadout_of` 给前者，设施里可能塞后者，坑68）。
    /// </summary>
    private static string[] _StringsOf(Variant value)
    {
        if (value.VariantType == Variant.Type.PackedStringArray)
        {
            return value.AsStringArray();
        }

        var out_values = new System.Collections.Generic.List<string>();
        foreach (Variant entry in value.AsGodotArray())
        {
            out_values.Add(entry.AsString());
        }

        return out_values.ToArray();
    }

    /// <summary>
    /// 字典逐键比：C# 的 `Godot.Collections.Dictionary.Equals` 是按 native 引用，
    /// 两个内容相同的字典直接 `CheckEq` 会判假（GDScript 那份是按值比）。
    /// </summary>
    private void _CheckSameDict(
        Godot.Collections.Dictionary actual, Godot.Collections.Dictionary expected, string message)
    {
        bool same = actual.Count == expected.Count;
        if (same)
        {
            foreach (Variant key in expected.Keys)
            {
                if (!actual.ContainsKey(key) || !actual[key].Equals(expected[key]))
                {
                    same = false;
                    break;
                }
            }
        }

        CheckTrue(same, message);
    }

    private static bool _Has(string[] values, string wanted) =>
        System.Array.IndexOf(values, wanted) >= 0;
}
