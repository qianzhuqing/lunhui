using Godot;
using Lunhui.Autoload;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 增益与套装（二·落地那半）：常驻 buff 走贡献通道进面板、`BattleActor` 的叠加／刷新与回合末递减、
/// 六指令（普攻／主动防御／内功催动／道具）、进场 buff、战斗外计时、招式自带的 `on_cast`／`on_hit` 发放、
/// 叠层按层数放大。
///
/// 对位 `tests/test_buff.gd` 的后九节（2026-10-11 P4 第三十八批整体搬过来）。表自洽与两个服务的
/// **静态**规则那两节在 `tests/TestBuff.cs` —— 那一份原来只覆盖了静态半边，注释里写着"要
/// `Inventory`／`GameState`／`BattleSimulator`，它们都还在 GDScript 里"；这三块 2026-10-11 已经全搬成
/// C# 了，所以这半边现在也搬得动。**两份各注册一次**，断言的账分别落在各自那份上。
///
/// 夹具照 `_check_cast_grants` 那几节的老办法：**新建一份私有表库**，把 `buff_grant`／`buff_stat`
/// 复制出来再追加几行（发行数据里跑不到的分支靠它验代码半边），绝不碰共享表库。
/// </summary>
public partial class TestBuffIntegration : TestCase
{
    public override string SuiteName() => "增益与套装（落地那半）";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckSetCounts(db);
        _CheckPermanentContributions(db);
        _CheckActorBuffRules(db);
        _CheckSixCommands(db);
        _CheckBattleStartGrants(db);
        _CheckFieldBuffs(db);
        _CheckCastGrants();
        _CheckStackScaling();
        _CheckSupportSkill(db);
        _CheckSkillHitGrants();
    }

    /// <summary>
    /// 套装的**计数口径**：只算已装备／已装配；内功套按**占格数之和**（不是部数）；档位向下兼容。
    /// （静态那半——套数、成员数、档位数——在 `TestBuff.cs`。）
    /// </summary>
    private void _CheckSetCounts(TableDb db)
    {
        SetService sets = SetService.create(db);
        var state = (GameState)SoloState(db);
        string char_id = state.char_ids[0].AsString();
        var inventory = new Inventory();
        // 黑风寨那套有等级门槛（7~9 级），先升到 12 级再穿——套装用例验的是计数口径，不是等级校验
        state.append_level(char_id, 11);
        int level = state.level_of(char_id);
        string weapon_type = ((CharacterBaseRow)db.get_row("character_base", char_id)).weapon_type;

        // 装备套：只算穿在身上的——先在背包里放一件，不该被算进去
        inventory.add_equipment(db, "eq_armor_03", new Godot.Collections.Array());
        CheckEq(sets.count_for("set_heifeng", char_id, inventory, state), 0, "只在背包里不算套装件数");
        string[] worn =
        {
            inventory.add_equipment(db, "eq_armor_03", new Godot.Collections.Array()),
            inventory.add_equipment(db, "eq_ring_03", new Godot.Collections.Array()),
            inventory.add_equipment(db, "eq_head_01", new Godot.Collections.Array()),
        };
        foreach (string instance_id in worn)
        {
            Godot.Collections.Dictionary result = inventory.equip(db, char_id, level, weapon_type, instance_id);
            CheckTrue(result["ok"].AsBool(), $"穿得上 {instance_id}（{_S(result, "error")}）");
        }

        CheckEq(sets.count_for("set_heifeng", char_id, inventory, state), 3, "穿上 3 件 → 算 3 件");

        // 第 4 件是**黑风刀**（blade），而这个模板是剑系（sword）——正经的拒绝，不是 bug。
        // 4 件档因此要等"刀系可用角色"（待策划确认 Q17）；档位逻辑先用 active_buff_ids 单独验。
        string blade = inventory.add_equipment(db, "eq_sword_03", new Godot.Collections.Array());
        Godot.Collections.Dictionary bladeResult = inventory.equip(db, char_id, level, weapon_type, blade);
        CheckFalse(bladeResult["ok"].AsBool(), "剑系角色穿不上黑风刀（武器类型限制）");
        string bladeError = _S(bladeResult, "error");
        CheckTrue(
            bladeError.Contains("刀") || bladeError.Contains("blade"),
            $"拒绝理由说清武器类型：{bladeError}");
        CheckEq(sets.active_buff_ids("set_heifeng", 4).Length, 2, "4 件时两档都生效（向下兼容）");
        CheckEq(sets.active_buff_ids("set_heifeng", 2).Length, 1, "2 件时只有低档");

        // 招式套：按装配的招数
        state.set_loadout(
            char_id,
            new[] { "sk_xuanwei_01", "sk_xuanwei_02", "sk_xuanwei_03", "sk_xuanwei_04", "sk_xuanwei_05" },
            System.Array.Empty<string>());
        CheckEq(sets.count_for("set_xuanwei_sword", char_id, inventory, state), 5, "装 5 招 → 算 5 招");
        CheckEq(sets.active_buff_ids("set_xuanwei_sword", 5).Length, 2, "5 招时 3 招档＋5 招档都生效");

        // 内功套：按**占格数之和**（不是部数）——这条正是设计特别点名的口径
        string[] passives =
        {
            "pf_xuanwei_01", "pf_xuanwei_02", "pf_xuanwei_03", "pf_xuanwei_04", "pf_xuanwei_05",
        };
        state.set_loadout(char_id, System.Array.Empty<string>(), passives);
        var slots = 0;
        foreach (string passive_id in passives)
        {
            slots += ((SkillPassiveRow)db.get_row("skill_passive", passive_id)).slot_cost;
        }

        CheckGt(slots, 5.0, $"五部玄微心法占 {slots} 格（>5 部数）");
        CheckEq(
            sets.count_for("set_xuanwei_qi", char_id, inventory, state), slots,
            $"内功套按占格数之和算（{slots} 格）");
        CheckTrue(
            sets.count_for("set_xuanwei_qi", char_id, inventory, state) >= 7,
            "五部占格之和够得上 7 格档（设计要的 4 格／7 格两档）——这里验的是**格数口径**；"
                + "这几部武学能不能全拿到是另一回事（现在拿不到，见 KNOWN_UNREACHABLE_SET_TIERS）");
    }

    /// <summary>
    /// 常驻 buff 走**贡献通道**：装备（`on_equip`）与套装档位在面板里读到同一个数。
    /// 临时 buff（正回合／整场）不在这里，走 `BattleActor` 的现算路径（下一节）。
    /// </summary>
    private void _CheckPermanentContributions(TableDb db)
    {
        var state = (GameState)SoloState(db);
        string char_id = state.char_ids[0].AsString();
        // 锈月要 9 级、前代寨主遗物要 8 级
        state.append_level(char_id, 11);
        int level = state.level_of(char_id);
        string weapon_type = ((CharacterBaseRow)db.get_row("character_base", char_id)).weapon_type;
        Inventory inventory = state.inventory as Inventory;

        CharacterSheet before = CharacterSheet.create(db, state, char_id);
        double agi_before = before.total_attrs()["agi"].AsDouble();

        // 锈月：buff_grant(equip, eq_sword_04, buff_rusty_moon, on_equip)，buff 是 duration=0 常驻
        string sword = inventory.add_equipment(db, "eq_sword_04", new Godot.Collections.Array());
        Godot.Collections.Dictionary equipped = inventory.equip(db, char_id, level, weapon_type, sword);
        CheckTrue(equipped["ok"].AsBool(), $"穿上锈月（{_S(equipped, "error")}）");
        CharacterSheet after = CharacterSheet.create(db, state, char_id);
        CheckGt(
            after.total_attrs()["agi"].AsDouble(), agi_before,
            $"穿上锈月面板的五维就变了（{agi_before} → {after.total_attrs()["agi"].AsDouble()}）");

        // 断言落在**贡献条目**上（数值精确，且不会被「装备自己的 bonus_* 列」和属性曲线混进来）
        Godot.Collections.Array swordMods = _BuffContributions(inventory.contributions_for(db, char_id));
        CheckEq(_ContributionValue(swordMods, "attr", "agi"), 4.0, "锈月的常驻 buff 给敏 +4（属性点层）");
        CheckEq(_ContributionValue(swordMods, "stat", "crit_rate"), 0.05, "锈月的常驻 buff 给暴击率 +5%（固定值层）");
        var sourceNamesInstance = false;
        foreach (Variant entry in swordMods)
        {
            if (entry.AsGodotDictionary()["source"].AsString().Contains(sword))
            {
                sourceNamesInstance = true;
            }
        }

        CheckTrue(sourceNamesInstance, "贡献的来源同时写着 buff 与装备实例 id（同名装备两件也分得开）");

        // 遗物：attr:str+3、attr:con+3、stat:block_reduction+0.08
        string relic = inventory.add_equipment(db, "eq_head_01", new Godot.Collections.Array());
        CheckTrue(
            inventory.equip(db, char_id, level, weapon_type, relic)["ok"].AsBool(), "戴上遗物");
        Godot.Collections.Array relicMods = _BuffContributionsFor(
            inventory.contributions_for(db, char_id), relic);
        CheckEq(_ContributionValue(relicMods, "attr", "str"), 3.0, "遗物给力 +3");
        CheckEq(_ContributionValue(relicMods, "stat", "block_reduction"), 0.08, "遗物给格挡减伤 +8%");

        // 套装档位（黑风寨 2 件：力 +3）也走同一条通道 —— 只算穿在身上的
        inventory.add_equipment(db, "eq_armor_03", new Godot.Collections.Array());
        string armor = inventory.add_equipment(db, "eq_armor_03", new Godot.Collections.Array());
        CheckTrue(inventory.equip(db, char_id, level, weapon_type, armor)["ok"].AsBool(), "穿上山贼皮铠");
        // 此时身上：锈月 + 遗物 + 皮铠。套装要的是「黑风刀／皮铠／淬毒指环／遗物」，
        // 皮铠 + 遗物 = 2 件 → 2 件档「力 +3」应当出现（锈月不算这一套）
        CharacterSheet with_set = CharacterSheet.create(db, state, char_id);
        Godot.Collections.Array setMods = _SourceContributions(with_set.contributions(), "set:set_heifeng");
        CheckEq(_ContributionValue(setMods, "attr", "str"), 3.0, "黑风寨两件档：力 +3（档位 buff 也走贡献通道）");
        CheckGt(
            with_set.total_attrs()["str"].AsDouble(), 0.0,
            "套装贡献的来源写着 set_id（能追溯是哪一套发的）");
    }

    /// <summary>
    /// `BattleActor` 的 buff 规则：叠加／刷新、回合末递减、属性现算、`computed`（运功翻倍）、
    /// `special` 类没实现的效果**不许静默**（记进 `unhandled_buffs`）。
    /// </summary>
    private void _CheckActorBuffRules(TableDb db)
    {
        var state = (GameState)SoloState(db);
        string char_id = state.char_ids[0].AsString();
        // 装一部内功（引气：stat:qi_max +30）——通用运功就是把它翻倍
        state.set_loadout(char_id, System.Array.Empty<string>(), new[] { "pf_xuanwei_01" });
        BattleActor actor = PartyBuilder.build_actor(db, state, char_id);
        CheckNotNull(actor, "从存档造出战斗单位");

        double def_before = actor.stat("def_phys");
        double reduction_before = actor.stat("dmg_reduction");
        CheckFloat(reduction_before, 0.0, "没上 buff 时减伤是 0（口径：减伤只来自属性与临时效果）", 0.001);

        // 防御姿态：unique —— 上第二次无效，属性不叠
        CheckTrue(actor.add_buff("buff_guard", "action", "defend")["applied"].AsBool(), "防御姿态上得去");
        CheckFloat(actor.stat("dmg_reduction"), 0.25, "防御姿态给减伤 25%", 0.001);
        CheckFloat(actor.stat("def_phys") - def_before, 15.0, "防御姿态给外防 +15", 0.001);
        CheckFalse(actor.add_buff("buff_guard", "action", "defend")["applied"].AsBool(), "unique：重复获得无效");
        CheckEq(actor.buff_stacks("buff_guard"), 1, "unique 不叠层");

        // 回合末递减：duration=1 的防御姿态下一回合末就没了，属性要跟着回落
        CheckEq(actor.buff_remaining("buff_guard"), 1, "防御姿态剩余 1 回合");
        CheckEq(actor.tick_buffs().Count, 1, "回合末过期一条");
        CheckFalse(actor.has_buff("buff_guard"), "过期后列表里没有它了");
        CheckFloat(actor.stat("dmg_reduction"), reduction_before, "过期后减伤回落到原值", 0.001);
        CheckFloat(actor.stat("def_phys"), def_before, "过期后外防回落到原值", 0.001);

        // computed：通用运功（buff_yunqi）= 该内功的常驻加成再来一份
        double qi_before = actor.max_qi();
        Godot.Collections.Dictionary yunqi = actor.add_buff("buff_yunqi", "skill_passive", "pf_xuanwei_01");
        CheckTrue(yunqi["applied"].AsBool(), "催动通用运功");
        CheckFloat(actor.max_qi() - qi_before, 30.0, "运功让引气的内力上限加成翻倍（+30）", 0.001);
        CheckEq(actor.buff_remaining("buff_yunqi"), 3, "运功持续 3 回合（duration=3）");

        // stack：淬毒（上限 3 层）——**逐次**验，别只看封顶：
        // 之前那个「四次调用之后 = 3」的写法盖住了「第一次就给 2 层」的 bug（2026-10-03 修，见决策 220）
        for (var expected = 1; expected <= 3; expected++)
        {
            actor.add_buff("buff_poison_amp", "equip", "eq_ring_03");
            CheckEq(actor.buff_stacks("buff_poison_amp"), expected, $"第 {expected} 次叠层 = {expected} 层");
        }

        actor.add_buff("buff_poison_amp", "equip", "eq_ring_03");
        CheckEq(actor.buff_stacks("buff_poison_amp"), 3, "叠层封顶 3 层（max_stack）");
        // special 类没有实现的效果**不许静默**：记进 unhandled_buffs，结算会播成「暂缓规则」
        CheckEq(actor.unhandled_buffs.Count, 1, "special 类 buff 记一条「还没实现」");
        CheckTrue(
            actor.unhandled_buffs[0].AsGodotDictionary()["name"].AsString().Contains("淬毒"),
            $"记的是中文名：{actor.unhandled_buffs[0].AsGodotDictionary()["name"].AsString()}");
    }

    /// <summary>六指令：普通攻击／主动防御／内功催动／道具（招式与逃跑早有用例）。</summary>
    private void _CheckSixCommands(TableDb db)
    {
        var state = (GameState)SoloState(db);
        string char_id = state.char_ids[0].AsString();
        state.set_loadout(char_id, System.Array.Empty<string>(), new[] { "pf_xuanwei_01" });
        Godot.Collections.Array allies = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        Godot.Collections.Array enemies = EnemyFactory.create(db).create_team("team_wolf_pack", "normal");
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(20261003));
        sim.setup(allies, enemies, _Modifiers());
        CheckEq(allies.Count, 1, "单人队（solo_state）");

        // 普通攻击：不耗内力、不看武器；也不该污染「招式使用」记录（隐藏内容按它判定）
        BattleActor actor = allies[0].As<BattleActor>();
        BattleActor target = enemies[0].As<BattleActor>();
        int hp_before = target.hp;
        sim.basic_attack(actor, target);
        CheckLt(target.hp, hp_before, $"普通攻击能造成伤害（{hp_before} → {target.hp}）");
        CheckEq(sim.skill_uses(actor.actor_id).Count, 0, "普通攻击不是武学：不进招式使用记录");

        // 主动防御：花一次行动换 buff + 架势；下回合先手
        sim.begin_round();
        var guard = 0;
        while (sim.in_round() && guard < 20)
        {
            BattleActor current = sim.current_actor() as BattleActor;
            if (current != null && current.side == BattleActor.side_ally())
            {
                break;
            }

            sim.auto_act();
            guard++;
        }

        BattleActor defender = sim.current_actor() as BattleActor;
        CheckTrue(
            defender != null && defender.side == BattleActor.side_ally(),
            "第 1 回合轮到我方（防御指令的前提）");
        if (defender != null)
        {
            // 架势拉低才看得出回复（满架势会被上限夹住 +0，这是预期行为）
            defender.poise = 10;
            int poise_before = defender.poise;
            Godot.Collections.Dictionary defend = sim.defend(defender);
            CheckTrue(defend["ok"].AsBool(), $"主动防御生效（{_S(defend, "error")}）");
            CheckTrue(defender.has_buff("buff_guard"), "防御姿态挂在身上");
            CheckGt(defender.poise, poise_before, $"主动防御回复架势（{poise_before} → {defender.poise}）");
            CheckEq(sim.defend_block_reason(defender), "已经在这个姿态里了", "同回合不能再防一次（unique）");
            guard = 0;
            while (sim.in_round() && guard < 20)
            {
                sim.auto_act();
                guard++;
            }

            if (!sim.finished())
            {
                sim.begin_round();
                BattleActor first = sim.current_actor() as BattleActor;
                CheckTrue(first != null, "新回合有单位可行动");
                if (first != null)
                {
                    CheckEq(first.actor_id, defender.actor_id, "主动防御的人下回合先手");
                }
            }
            else
            {
                Fail("第一回合就打完了，先手断言拿不到下一回合");
            }
        }

        // 内功催动：换运功 buff；没装的内功有明确理由
        BattleActor caster = allies[0].As<BattleActor>();
        Godot.Collections.Dictionary cast = sim.cast_passive(caster, "pf_xuanwei_01");
        CheckTrue(cast["ok"].AsBool(), $"内功催动生效（{_S(cast, "error")}）");
        CheckTrue(caster.has_buff("buff_yunqi"), "运功 buff 挂上了");
        CheckTrue(
            sim.passive_cast_block_reason(caster, "pf_wudu_01").Length > 0,
            "没装的内功催不动，且给得出理由");
        CheckEq(sim.passive_cast_options(caster).Count, 1, "内功指令只列已装的那一部");
        CheckTrue(
            sim.passive_cast_options(caster)[0].AsGodotDictionary()["buff_name"].AsString().Contains("运功"),
            "按钮上写明催动后会拿到什么");

        // 道具：`use_context=battle` 的两件列得出来，但**效果列还没配**，必须写明而不是假装能用
        (state.inventory as Inventory).add_item(db, "item_potion_small", 2);
        Godot.Collections.Array items = sim.battle_item_options(state.inventory as Inventory);
        CheckEq(items.Count, 2, "战斗道具两件（金创药／回气散）");
        CheckEq(items[0].AsGodotDictionary()["name"].AsString(), "金创药", "道具用中文名");
        CheckEq(items[0].AsGodotDictionary()["qty"].AsInt32(), 2, "数量从背包读");
        CheckFalse(items[0].AsGodotDictionary()["usable"].AsBool(), "效果列未配 → 不可用（不假装能回血）");
        CheckTrue(
            items[0].AsGodotDictionary()["reason"].AsString().Contains("效果"),
            $"不可用的原因写清是效果没配：{items[0].AsGodotDictionary()["reason"].AsString()}");
    }

    /// <summary>进场 buff（`on_battle_start`，整场有效）在 setup 时就挂上：药王玉佩给气血回复 +3。</summary>
    private void _CheckBattleStartGrants(TableDb db)
    {
        var state = (GameState)SoloState(db);
        string char_id = state.char_ids[0].AsString();
        state.append_level(char_id, 6); // 玉佩要 6 级
        int level = state.level_of(char_id);
        string weapon_type = ((CharacterBaseRow)db.get_row("character_base", char_id)).weapon_type;
        Inventory inventory = state.inventory as Inventory;
        string jade = inventory.add_equipment(db, "eq_acc_02", new Godot.Collections.Array());
        CheckTrue(inventory.equip(db, char_id, level, weapon_type, jade)["ok"].AsBool(), "戴上药王玉佩");
        Godot.Collections.Array allies = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        BattleActor ally = allies[0].As<BattleActor>();
        double regen_before = ally.stat("hp_regen");
        Godot.Collections.Array enemies = EnemyFactory.create(db).create_team("team_wolf_pack", "normal");
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(20261003));
        sim.setup(allies, enemies);
        CheckTrue(ally.has_buff("buff_regen"), "玉佩的整场 buff 开打就挂上");
        CheckEq(ally.buff_remaining("buff_regen"), -1, "整场有效（remaining = -1，不按回合递减）");
        CheckFloat(ally.stat("hp_regen") - regen_before, 3.0, "玉佩给气血回复 +3", 0.001);
        CheckEq(ally.pending_grants.Count, 0, "进场 buff 发完就清空（不会每回合重复发）");
    }

    /// <summary>
    /// 战斗外增益（08：`scope=field`，按**现实分钟**计时；进战斗维持整场）。
    ///
    /// 会话层负责计时（`GameSession.add_field_buff／active_field_buffs／clear_field_buffs`），
    /// `PartyBuilder` 只负责把还活着的那几条带进战场——所以两边各验一段。
    /// </summary>
    private void _CheckFieldBuffs(TableDb db)
    {
        var session = new GameSession();
        var now = (int)Time.GetUnixTimeFromSystem();

        CheckEq(session.active_field_buffs(now).Count, 0, "一开始没有战斗外增益");
        Godot.Collections.Dictionary applied = session.add_field_buff("buff_meditated", 10, "", 1);
        CheckTrue(applied["ok"].AsBool(), "打坐余韵挂上了（10 分钟）");
        Godot.Collections.Array rows = session.active_field_buffs(now);
        CheckEq(rows.Count, 1, "现在有一条生效的");
        CheckEq(rows[0].AsGodotDictionary()["remaining_sec"].AsInt32(), 600, "剩余秒数 = 10 分钟（按现实时间算）");

        // 重复获得 = 刷新（buff_meditated 的 stack_rule=refresh），不是两条
        session.add_field_buff("buff_meditated", 10, "", 1);
        CheckEq(session.active_field_buffs(now).Count, 1, "重复获得只刷新，不叠成两条");
        // 到点即失效：用注入的 now，不用真等 10 分钟
        CheckEq(session.active_field_buffs(now + 601).Count, 0, "600 秒之后即失效");
        CheckEq(session.field_buffs.Count, 0, "过期的会被清掉（不是留在列表里装样子）");

        session.add_field_buff("buff_meditated", 10, "", 1);
        CheckEq(session.clear_field_buffs(), 1, "clear（败北要用）返回清掉几条");
        CheckEq(session.active_field_buffs(now).Count, 0, "清完就一条不剩");

        // 带进战斗：PartyBuilder 把它当**进场 buff**发，进战斗后维持整场（不按回合递减）
        session.add_field_buff("buff_meditated", 10, "", 1);
        var state = (GameState)SoloState(db);
        Godot.Collections.Array allies = PartyBuilder.build_actors(db, state, session.active_field_buffs(-1));
        Godot.Collections.Array enemies = EnemyFactory.create(db).create_team("team_wolf_pack", "normal");
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(20261003));
        BattleActor ally = allies[0].As<BattleActor>();
        double qi_regen_before = ally.stat("qi_regen");
        double hp_regen_before = ally.stat("hp_regen");
        sim.setup(allies, enemies);
        CheckTrue(ally.has_buff("buff_meditated"), "战斗外增益带进了战场");
        CheckEq(ally.buff_remaining("buff_meditated"), 0, "整场有效（field 在战斗里不按回合递减）");
        CheckFloat(ally.stat("qi_regen") - qi_regen_before, 1.5, "打坐余韵给内力回复 +1.5", 0.001);
        CheckFloat(
            ally.stat("hp_regen") - hp_regen_before, 0.0,
            "饱食那条不在身上（只挂了打坐余韵）：气血回复不该变", 0.001);
        // 战斗结束清战斗内 buff，但**field 要留着**（它归会话管，还没到期）
        ally.clear_battle_buffs();
        CheckTrue(ally.has_buff("buff_meditated"), "战斗结束不会清掉战斗外增益（会话还在管它）");
        session.Free();
    }

    /// <summary>
    /// 招式自带的增益（`skill_active` 的 `on_cast` 发放）——「招式的增益效果＝施放时发的 buff」。
    ///
    /// 表里目前只有一条 `gr_sk_drunk_zuibu`（醉步，招式本身还没配出伤害、战斗里用不出来），
    /// 所以这条路在发行数据里跑不到——用**新建一份表库 + 加三条夹具发放**来验代码半边：
    /// 施放一次只发一次（不看打到几个人）、同一招的多条发放都生效、buff 写错时**不静默**。
    /// </summary>
    private void _CheckCastGrants()
    {
        TableDb local_db = _WithExtraRows(
            "buff_grant",
            _GrantRow("gr_test_cast_guard", "skill_active", "sk_xuanwei_01", "buff_guard"),
            _GrantRow("gr_test_cast_poison", "skill_active", "sk_xuanwei_01", "buff_poison_amp"),
            _GrantRow("gr_test_cast_bogus", "skill_active", "sk_xuanwei_02", "buff_bogus"));

        var state = (GameState)SoloState(local_db);
        string char_id = state.char_ids[0].AsString();
        state.set_loadout(char_id, new[] { "sk_xuanwei_01", "sk_xuanwei_02" }, System.Array.Empty<string>());
        Godot.Collections.Array allies = PartyBuilder.build_actors(local_db, state, new Godot.Collections.Array());
        Godot.Collections.Array enemies = EnemyFactory.create(local_db).create_team("team_wolf_pack", "normal");
        BattleSimulator sim = BattleSimulator.create(local_db, RngService.create(20261003));
        sim.setup(allies, enemies, _Modifiers());
        BattleActor ally = allies[0].As<BattleActor>();
        Resource skill = local_db.get_row("skill_active", "sk_xuanwei_01");
        CheckNotNull(skill, "夹具招式在表里");
        CheckTrue(ally.add_buff("buff_guard", "action", "defend")["applied"].AsBool(), "先上一个别的来源的防御姿态");
        ally.buffs = new Godot.Collections.Array(); // 清干净：下面要验「招式自己发的那一条」

        // ① 施放一次：两条发放都生效；叠层类只 +1（**一次行动只发一次**，不是每个目标各发一次）
        Godot.Collections.Array lines = sim.act(ally, skill, enemies[0].As<BattleActor>());
        CheckTrue(ally.has_buff("buff_guard"), "招式自带的增益挂上了（on_cast）");
        CheckEq(ally.buff_stacks("buff_poison_amp"), 1, "一次行动只发一次（叠层类 +1，不按目标数累加）");
        CheckTrue(_Text(lines).Contains("附带"), $"战报里写明这一招附带什么：{_Text(lines)}");

        // ② 再施放一次：叠层类到 2（叠层规则照旧生效）
        sim.act(ally, skill, enemies[0].As<BattleActor>());
        CheckEq(ally.buff_stacks("buff_poison_amp"), 2, "再施放一次叠第二层");

        // ③ 发放指向不存在的 buff：**不静默**，战报里写清原因（这是唯一线索）
        Godot.Collections.Array bogus = sim.act(
            ally, local_db.get_row("skill_active", "sk_xuanwei_02"), enemies[0].As<BattleActor>());
        CheckTrue(_Text(bogus).Contains("没上上去"), $"buff 写错时战报如实说：{_Text(bogus)}");
        CheckTrue(_Text(bogus).Contains("buff_bogus"), $"连是哪个 buff 都写出来（定位用）：{_Text(bogus)}");

        // ④ 敌人也能接 buff（以前 EnemyFactory 不传 db，敌人的 add_buff 直接失败）
        BattleActor wolf = (BattleActor)EnemyFactory.create(local_db).create_enemy("en_wolf", "normal");
        CheckNotNull(wolf.buff_service(), "敌人接上配置表（能收 buff）");
        CheckTrue(wolf.add_buff("buff_guard", "test", "unit")["applied"].AsBool(), "敌人的 buff 真的上得去");
    }

    /// <summary>
    /// 叠层要**真的按层数放大效果**，命中触发要**按段各发一次**。
    ///
    /// 这两条都是 08 写死的口径（「stack 叠层」「招式的增益效果＝命中/施放时发的 buff」），
    /// 而发行数据里唯一可叠层的 buff（淬毒）是 `special`——**没有数值修正行**，所以
    /// 「层数 × 数值」这条乘法在真数据里根本走不到。这里用夹具（给淬毒补一条 `stat:atk_phys +5`、
    /// 手动挂 `on_hit` 发放）把两条口径都钉住；上一轮那个「第一次就给 2 层」的 bug 就是靠这类
    /// 「逐次断言」抓到的（决策 220）。
    /// </summary>
    private void _CheckStackScaling()
    {
        TableDb local_db = _WithExtraRows("buff_stat", _StatRow("buff_poison_amp", "stat:atk_phys", 5.0));

        var state = (GameState)SoloState(local_db);
        string char_id = state.char_ids[0].AsString();
        BattleActor actor = PartyBuilder.build_actor(local_db, state, char_id);
        double atk_before = actor.stat("atk_phys");
        CheckTrue(actor.add_buff("buff_poison_amp", "equip", "eq_ring_03")["applied"].AsBool(), "挂第一层");
        CheckFloat(actor.stat("atk_phys") - atk_before, 5.0, "一层 = +5（层数 × 数值）", 0.001);
        actor.add_buff("buff_poison_amp", "equip", "eq_ring_03");
        CheckFloat(actor.stat("atk_phys") - atk_before, 10.0, "两层 = +10（乘法真的按层数走）", 0.001);
        actor.add_buff("buff_poison_amp", "equip", "eq_ring_03");
        actor.add_buff("buff_poison_amp", "equip", "eq_ring_03");
        CheckEq(actor.buff_stacks("buff_poison_amp"), 3, "封顶 3 层");
        CheckFloat(actor.stat("atk_phys") - atk_before, 15.0, "封顶后不再涨（3 层 = +15）", 0.001);

        // 命中触发：**按命中段数各发一次**（`sk_spear_04` 是 2 段单体）
        state.set_loadout(char_id, new[] { "sk_spear_04" }, System.Array.Empty<string>());
        Godot.Collections.Array allies = PartyBuilder.build_actors(local_db, state, new Godot.Collections.Array());
        BattleActor lab_ally = allies[0].As<BattleActor>();
        lab_ally.qi = 999;
        // 手动挂「命中叠淬毒」：真数据里这条来自淬毒指环（本用例换了武器，所以自己挂）
        lab_ally.on_hit_grants = new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary
            {
                { "buff_id", "buff_poison_amp" },
                { "source_type", "equip" },
                { "source_id", "eq_ring_03" },
                { "stacks", 1 },
            },
        };
        BattleActor boss = (BattleActor)EnemyFactory.create(local_db).create_enemy("en_bd_boss", "normal");
        BattleSimulator sim = BattleSimulator.create(local_db, RngService.create(20261003));
        sim.setup(allies, new Godot.Collections.Array { boss }, _Modifiers());
        sim.act(lab_ally, local_db.get_row("skill_active", "sk_spear_04"), boss);
        CheckEq(lab_ally.buff_stacks("buff_poison_amp"), 2, "2 段招式 → 命中触发叠 2 层（按段，不是按行动）");
    }

    /// <summary>
    /// 增益招式：醉里乾坤·醉步（`skill_active` 整行 0、`target_type=self`，但表里配了 `on_cast` 发放）。
    ///
    /// 这条用的就是**发行数据**（不是夹具）：玩家能从酒葫芦那条隐藏内容里学会它，在那之前它在战斗里
    /// 既没有按钮也用不出来（角色面板会写「用不出来」）。现在的口径：**没有伤害但有 `on_cast` 发放
    /// 的招式＝增益招式**，施放一次给自己上 buff、不走伤害管线（见框架说明决策 221）。
    /// </summary>
    private void _CheckSupportSkill(TableDb db)
    {
        var state = (GameState)SoloState(db);
        string char_id = state.char_ids[0].AsString();
        state.learn_skill(char_id, "sk_drunk_zuibu");
        state.set_loadout(char_id, new[] { "sk_xuanwei_01", "sk_drunk_zuibu" }, System.Array.Empty<string>());
        Godot.Collections.Array allies = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        Godot.Collections.Array enemies = EnemyFactory.create(db).create_team("team_wolf_pack", "normal");
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(20261003));
        sim.setup(allies, enemies, _Modifiers());
        BattleActor ally = allies[0].As<BattleActor>();
        // 内力给满（别超过上限：上 buff 时属性会整份重算，超出上限的部分会被夹掉——那是预期行为）
        ally.qi = ally.max_qi();
        int qi_before = ally.qi;

        // ① 界面能看到它、且写明是「增益」（不是「倍率 0.0」）
        var options = new System.Collections.Generic.Dictionary<string, Godot.Collections.Dictionary>();
        foreach (Variant entry in sim.skill_options(ally))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            options[option["skill_id"].AsString()] = option;
        }

        CheckTrue(options.ContainsKey("sk_drunk_zuibu"), "醉步出现在可点列表里（以前连按钮都没有）");
        if (options.ContainsKey("sk_drunk_zuibu"))
        {
            Godot.Collections.Dictionary zuibu_option = options["sk_drunk_zuibu"];
            CheckTrue(zuibu_option["support"].AsBool(), "标记为增益招式");
            CheckTrue(zuibu_option["ok"].AsBool(), $"现在能点：{_S(zuibu_option, "reason")}");
        }

        CheckTrue(options.ContainsKey("sk_xuanwei_01"), "对照：普攻招式照旧在列表里");

        // ①b **两个查询口径必须一致**：`available_skills`（能用的）与 `skill_options`（体检表 ok=true）
        // 是同一件事的两种问法，界面按前者置灰、按后者列按钮——一边漏了只有真路径能看出来。
        // （这次就是这么发现的：`available_skills` 里忘了给增益招式放行，按钮列得出来、按下去却说「用不了」。）
        var usable_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in sim.available_skills(ally))
        {
            usable_ids.Add(((SkillActiveRow)entry.AsGodotObject()).skill_id);
        }

        var ok_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in sim.skill_options(ally))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            if (option["ok"].AsBool())
            {
                ok_ids.Add(option["skill_id"].AsString());
            }
        }

        CheckEq(
            string.Join("|", usable_ids), string.Join("|", ok_ids),
            "available_skills 与 skill_options(ok) 口径一致");

        // ② 施放：给自己上「忘忧」，敌人**一点血都不掉**（不走伤害管线）
        Resource zuibu = db.get_row("skill_active", "sk_drunk_zuibu");
        double dodge_before = ally.stat("dodge_rate");
        int enemy_hp_before = enemies[0].As<BattleActor>().hp;
        Godot.Collections.Array lines = sim.act(ally, zuibu, enemies[0].As<BattleActor>());
        CheckTrue(ally.has_buff("buff_zuibu"), "施放后自己拿到忘忧");
        CheckFloat(ally.stat("dodge_rate") - dodge_before, 0.20, "忘忧给闪避 +20%", 0.001);
        CheckEq(enemies[0].As<BattleActor>().hp, enemy_hp_before, "增益招式不打敌人（不走伤害管线）");
        CheckTrue(_Text(lines).Contains("增益"), $"战报里写明这是增益：{_Text(lines)}");
        CheckEq(ally.qi, qi_before, "醉步内力消耗是 0（表里配的），放完内力不变");
        CheckGt(sim.cooldown_left(ally.actor_id, "sk_drunk_zuibu"), 0.0, "放完进入冷却（表里 cooldown=3）");

        // ③ 自动战斗**不主动用**增益招式（它只挑打伤害的招）——同一套列表里，选招仍然挑得出普攻招式
        Resource picked = sim.pick_skill(ally);
        CheckNotNull(picked, "自动战斗仍能挑到攻击招式");
        if (picked != null)
        {
            CheckNe(picked.Get("skill_id").AsString(), "sk_drunk_zuibu", "自动战斗不会自己去放醉步");
        }
    }

    /// <summary>
    /// 命中触发也能来自**招式自己**（`buff_grant(source_type=skill_active, trigger=on_hit)`）。
    ///
    /// 08 的口径是「招式的增益效果＝命中/施放时发的 buff」：`on_cast` 上一轮接了，
    /// **命中这一半 2026-10-03 才接**（在那之前只有装备能配 `on_hit`，招式写了也是静默不发）。
    /// 发行数据里还没有哪条招式配 `on_hit`，所以用夹具；`sk_spear_04` 是 2 段单体 → 一次行动叠 2 层。
    /// </summary>
    private void _CheckSkillHitGrants()
    {
        TableDb local_db = _WithExtraRows(
            "buff_grant",
            _GrantRow("gr_test_hit_poison", "skill_active", "sk_spear_04", "buff_poison_amp", "on_hit"));

        var state = (GameState)SoloState(local_db);
        string char_id = state.char_ids[0].AsString();
        state.set_loadout(char_id, new[] { "sk_spear_04" }, System.Array.Empty<string>());
        Godot.Collections.Array allies = PartyBuilder.build_actors(local_db, state, new Godot.Collections.Array());
        BattleActor ally = allies[0].As<BattleActor>();
        ally.qi = ally.max_qi();
        // 确保装备那半不掺和：这条验的是**招式自带**的命中触发
        ally.on_hit_grants = new Godot.Collections.Array();
        BattleActor boss = (BattleActor)EnemyFactory.create(local_db).create_enemy("en_bd_boss", "normal");
        BattleSimulator sim = BattleSimulator.create(local_db, RngService.create(20261003));
        sim.setup(allies, new Godot.Collections.Array { boss }, _Modifiers());
        CheckEq(ally.buff_stacks("buff_poison_amp"), 0, "开打前没有淬毒层");
        Godot.Collections.Array lines = sim.act(ally, local_db.get_row("skill_active", "sk_spear_04"), boss);
        CheckEq(ally.buff_stacks("buff_poison_amp"), 2, "招式自带的命中触发：2 段 → 2 层");
        CheckFalse(_Text(lines).Contains("没上上去"), $"没有「buff 没上上去」的报错：{_Text(lines)}");
    }

    /// <summary>
    /// 新建一份**私有**表库，把 `tableName` 复制出来再追加几行夹具（对位旧链的
    /// `local_db.tables[表].duplicate(true)` ＋ `rows.append(...)`）。
    ///
    /// **为什么是"复制整表 + 重建 rows 数组"而不是直接 `rows.Add`**：表资源是 Godot 按路径缓存的，
    /// `duplicate(true)` 之后 `rows` 这个数组仍可能与共享表共用同一份；重建一个新数组再写回去，
    /// 就绝不会把夹具行漏进别的用例（照 `TestCase.TableWithTalents` 的形状，见施工单坑53）。
    /// </summary>
    private static TableDb _WithExtraRows(string tableName, params Resource[] extra)
    {
        var db = new TableDb();
        db.load_all();
        var table = (TableResource)db.tables[tableName].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            rows.Add(entry);
        }

        foreach (Resource row in extra)
        {
            rows.Add(row);
        }

        table.rows = rows;
        db.tables[tableName] = table;
        return db;
    }

    /// <summary>造一条 `buff_stat` 夹具行。</summary>
    private static Resource _StatRow(string buff_id, string target, double value)
    {
        var row = new BuffStatRow { buff_id = buff_id, target = target, value = value };
        return row;
    }

    /// <summary>造一条 `buff_grant` 夹具行（只填逻辑需要的列；`id` 留空不影响 `grants_for`）。</summary>
    private static Resource _GrantRow(
        string grant_id, string source_type, string source_id, string buff_id,
        string trigger = "on_cast", int stacks = 1)
    {
        var row = new BuffGrantRow
        {
            grant_id = grant_id,
            source_type = source_type,
            source_id = source_id,
            buff_id = buff_id,
            trigger = trigger,
            stacks = stacks,
        };
        return row;
    }

    /// <summary>取某个来源（装备实例 id / `set:<id>`）的贡献条目。</summary>
    private static Godot.Collections.Array _SourceContributions(
        Godot.Collections.Array contributions, string source)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry in contributions)
        {
            if (entry.AsGodotDictionary()["source"].AsString() == source)
            {
                out_rows.Add(entry);
            }
        }

        return out_rows;
    }

    /// <summary>取**装备常驻 buff** 的那几条贡献（`source` 形如 `buff:<buff_id>@<实例 id>`）。</summary>
    private static Godot.Collections.Array _BuffContributions(Godot.Collections.Array contributions)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry in contributions)
        {
            if (entry.AsGodotDictionary()["source"].AsString().StartsWith("buff:"))
            {
                out_rows.Add(entry);
            }
        }

        return out_rows;
    }

    private static Godot.Collections.Array _BuffContributionsFor(
        Godot.Collections.Array contributions, string instance_id)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry in _BuffContributions(contributions))
        {
            if (entry.AsGodotDictionary()["source"].AsString().EndsWith($"@{instance_id}"))
            {
                out_rows.Add(entry);
            }
        }

        return out_rows;
    }

    /// <summary>某来源里「某一层某一项」的合计值；`kind` 用 `AttributeCalculator` 的常量（attr/stat 简写）。</summary>
    private static double _ContributionValue(
        Godot.Collections.Array contributions, string kind, string target)
    {
        string wanted = kind == "attr"
            ? AttributeCalculator.CONTRIB_ATTR_POINT
            : AttributeCalculator.CONTRIB_STAT_FLAT;
        var total = 0.0;
        foreach (Variant entry in contributions)
        {
            Godot.Collections.Dictionary item = entry.AsGodotDictionary();
            if (item["kind"].AsString() != wanted || item["target"].AsString() != target)
            {
                continue;
            }

            total += item["value"].AsDouble();
        }

        return total;
    }

    /// <summary>战斗修正夹具：必中 ＋ 无浮动（同种子可复现）。</summary>
    private static Godot.Collections.Dictionary _Modifiers() => new()
    {
        {
            "modifiers",
            new Godot.Collections.Dictionary { { "force_hit", true }, { "no_variance", true } }
        },
    };

    /// <summary>把战报（Array）拼成一段可查询的文本（对位旧链的 `str(lines)`）。</summary>
    private static string _Text(Godot.Collections.Array lines)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant entry in lines)
        {
            parts.Add(entry.ToString());
        }

        return string.Join("|", parts);
    }

    private static string _S(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";
}
