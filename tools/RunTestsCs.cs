using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Lunhui.Tests;

namespace Lunhui.Tools;

/// <summary>
/// 自检入口（C# 版，对位 `tools/run_tests.gd`）：逐条跑用例、汇总退出码。
///
/// 用法（`tools/run_tests_cs.bat` 就是这么调的）：
///   godot --headless --path . --log-file .logs/tests_cs.log \
///         --quit-after 2000 res://tools/run_tests_cs.tscn
/// 退出码 0 = 全部通过，1 = 有失败。
///
/// **为什么是场景而不是 `--script`**：Godot 的 `--script` 只认 GDScript，
/// C# 侧只能用"一个挂在场景里的节点，跑完 `GetTree().Quit(code)`"这个形状
/// （见 `docs/dev/CSharp迁移计划.md` 第三节第 1 条）。
///
/// 这里保留 GDScript 版的两道网，它们都是被真事故逼出来的，不是摆设：
///   * **假绿门限**：按文件比"写了几处断言"与"实际跑了几条"，少了就是有条件分支／空循环没走到。
///   * **断言数下限 `MinAssertions` ＋ 余量上限 `SlackLimit`**：防"删掉一整块断言"与"下限松成摆设"
///     （2026-10-03 出现过 952 条的窟窿）。
/// </summary>
public partial class RunTestsCs : Node
{
    /// <summary>用例清单：路径（读源码数静态断言点用）＋ 造用例的工厂。</summary>
    private static readonly (string Path, Func<TestCase> Factory)[] Suites =
    {
        ("res://tests/HarnessSelfTest.cs", () => new HarnessSelfTest()),
        // 「策划对接守卫」八族（2026-10-11 P4 第四十八批起搬 C#）：排在**最前面**——它们扫的是
        // 仓库文件与共享表库，而 `TestCreation` 会往共享表库里注入一行自建角色模板，跑到后面再比
        // 就可能多出一行。**八族已全部搬过来**（2026-10-11 P4 第五十三批收口）。
        ("res://tests/TestHandshakeContract.cs", () => new TestHandshakeContract()),
        ("res://tests/TestHandshakeWiring.cs", () => new TestHandshakeWiring()),
        ("res://tests/TestHandshakeFlags.cs", () => new TestHandshakeFlags()),
        ("res://tests/TestHandshakeNumbers.cs", () => new TestHandshakeNumbers()),
        ("res://tests/TestHandshakeDoccounts.cs", () => new TestHandshakeDoccounts()),
        ("res://tests/TestHandshakeCode.cs", () => new TestHandshakeCode()),
        ("res://tests/TestHandshakeRepo.cs", () => new TestHandshakeRepo()),
        ("res://tests/TestHandshakeDocs.cs", () => new TestHandshakeDocs()),
        // **这两份必须排在前面**：它们读的是**共享表库**（`GameData` 那一个实例），
        // 断言「每张表的行数与 `data/tables/*.csv` 一致」。后面的 `TestCreation` 会往共享表库
        // 注入一行自建角色模板（`creation_service` 那条路），跑到最后再来比就会多出一行
        // （2026-10-11 实测：6 行 vs CSV 5 行，连带 5 条校验错误）。GDScript 那条链里
        // `test_table_pipeline.gd` 本来也是 `TEST_SCRIPTS` 的第一个——这里对齐同样的次序。
        ("res://tests/TestTablePipeline.cs", () => new TestTablePipeline()),
        ("res://tests/TestTablePipelineRules.cs", () => new TestTablePipelineRules()),
        ("res://tests/TestRows.cs", () => new TestRows()),
        ("res://tests/TestAttribute.cs", () => new TestAttribute()),
        ("res://tests/TestGrowth.cs", () => new TestGrowth()),
        ("res://tests/TestAffix.cs", () => new TestAffix()),
        ("res://tests/TestTalent.cs", () => new TestTalent()),
        ("res://tests/TestLevel.cs", () => new TestLevel()),
        ("res://tests/TestMastery.cs", () => new TestMastery()),
        ("res://tests/TestBuff.cs", () => new TestBuff()),
        ("res://tests/TestBuffIntegration.cs", () => new TestBuffIntegration()),
        ("res://tests/TestStory.cs", () => new TestStory()),
        ("res://tests/TestPity.cs", () => new TestPity()),
        ("res://tests/TestStatusRules.cs", () => new TestStatusRules()),
        ("res://tests/TestDamage.cs", () => new TestDamage()),
        ("res://tests/TestEncounter.cs", () => new TestEncounter()),
        ("res://tests/TestPractice.cs", () => new TestPractice()),
        ("res://tests/TestSaveStore.cs", () => new TestSaveStore()),
        ("res://tests/TestSettings.cs", () => new TestSettings()),
        ("res://tests/TestBattleReward.cs", () => new TestBattleReward()),
        ("res://tests/TestDropResolver.cs", () => new TestDropResolver()),
        ("res://tests/TestDialogue.cs", () => new TestDialogue()),
        ("res://tests/TestDialogueNpc.cs", () => new TestDialogueNpc()),
        ("res://tests/TestBattle.cs", () => new TestBattle()),
        ("res://tests/TestCharacterUi.cs", () => new TestCharacterUi()),
        ("res://tests/TestCopyAudit.cs", () => new TestCopyAudit()),
        ("res://tests/TestBattleUi.cs", () => new TestBattleUi()),
        ("res://tests/TestRecruit.cs", () => new TestRecruit()),
        ("res://tests/TestWorldEvent.cs", () => new TestWorldEvent()),
        ("res://tests/TestGuard.cs", () => new TestGuard()),
        ("res://tests/TestMenu.cs", () => new TestMenu()),
        ("res://tests/TestGuide.cs", () => new TestGuide()),
        ("res://tests/TestSkillGrant.cs", () => new TestSkillGrant()),
        ("res://tests/TestEnemyTemplate.cs", () => new TestEnemyTemplate()),
        ("res://tests/TestCreation.cs", () => new TestCreation()),
        ("res://tests/TestDungeon.cs", () => new TestDungeon()),
        ("res://tests/TestSkillLoadout.cs", () => new TestSkillLoadout()),
        ("res://tests/TestInventory.cs", () => new TestInventory()),
        ("res://tests/TestCharacterSheet.cs", () => new TestCharacterSheet()),
        ("res://tests/TestLocalMap.cs", () => new TestLocalMap()),
        ("res://tests/TestOverworld.cs", () => new TestOverworld()),
        ("res://tests/TestLayoutBudget.cs", () => new TestLayoutBudget()),
        ("res://tests/TestOverlayStack.cs", () => new TestOverlayStack()),
        ("res://tests/TestCooldown.cs", () => new TestCooldown()),
        ("res://tests/TestMultihit.cs", () => new TestMultihit()),
        ("res://tests/TestPoise.cs", () => new TestPoise()),
        ("res://tests/TestClue.cs", () => new TestClue()),
        ("res://tests/TestDrop.cs", () => new TestDrop()),
        ("res://tests/TestSaveTiming.cs", () => new TestSaveTiming()),
        ("res://tests/TestGuideSteps.cs", () => new TestGuideSteps()),
        ("res://tests/TestEncounterRules.cs", () => new TestEncounterRules()),
        ("res://tests/TestAudio.cs", () => new TestAudio()),
        ("res://tests/TestBattleSession.cs", () => new TestBattleSession()),
        ("res://tests/TestBattleFuzz.cs", () => new TestBattleFuzz()),
        ("res://tests/TestLevelFlow.cs", () => new TestLevelFlow()),
        ("res://tests/TestDungeonUi.cs", () => new TestDungeonUi()),
        ("res://tests/TestWorldEventWiring.cs", () => new TestWorldEventWiring()),
        ("res://tests/TestGrowthRules.cs", () => new TestGrowthRules()),
        ("res://tests/TestSettingsRuntime.cs", () => new TestSettingsRuntime()),
        ("res://tests/TestSkill.cs", () => new TestSkill()),
        ("res://tests/TestShop.cs", () => new TestShop()),
        ("res://tests/TestEndToEnd.cs", () => new TestEndToEnd()),
        ("res://tests/TestSaveMigration.cs", () => new TestSaveMigration()),
        ("res://tests/TestSaveMigrationVersions.cs", () => new TestSaveMigrationVersions()),
        ("res://tests/TestSaveMigrationRobust.cs", () => new TestSaveMigrationRobust()),
        ("res://tests/TestEventCheck.cs", () => new TestEventCheck()),
        ("res://tests/TestMapAssets.cs", () => new TestMapAssets()),
    };

    /// <summary>
    /// 断言数下限：用例中途报错会让后面的断言静悄悄地不跑，退出码却仍是 0——这条把那类假绿变成红。
    /// **贴着实测值留 10 条余量**：2026-10-10 首次跑通时 C# 侧只有骨架自检（13 条断言），
    /// 于是 13 − 10 ＝ 3；同一天接上行类搬迁门限（tests/TestRows.cs）后是 24 − 10 ＝ 14。
    /// core 第十二批（对话／招募／大地图随机事件）接上 `TestDialogue`／`TestRecruit`／`TestWorldEvent`
    /// 之后实测 2037，于是 2037 − 10 ＝ 2027；第十三批（宝箱守卫 `TestGuard`）之后实测 2078，
    /// 于是 2078 − 10 ＝ 2068；第十四批（启动菜单 `TestMenu`）之后实测 2135，于是 2135 − 10 ＝ 2125；
    /// 第十五批（开局引导 `TestGuide`）之后实测 2202，于是 2202 − 10 ＝ 2192。
    /// core 第十六批（武学来源 `SkillGrant`）之后实测 2262，于是 2262 − 10 ＝ 2252。
    /// core 第十七批（敌人工厂 `EnemyFactory`）之后实测 2302，于是 2302 − 10 ＝ 2292。
    /// core 第十八批（创建角色 `CreationService`）之后实测 2363，于是 2363 − 10 ＝ 2353。
    /// core 第十九批（副本完成度与扫荡 `DungeonService`）之后实测 2403，于是 2403 − 10 ＝ 2393。
    /// core 第二十批（武学装配 `SkillLoadout`）之后实测 2476，于是 2476 − 10 ＝ 2466。
    /// core 第二十一批（背包与装备 `Inventory`）之后实测 2565，于是 2565 − 10 ＝ 2555。
    /// core 第二十二批（角色面板 `CharacterSheet`）之后实测 2649，于是 2649 − 10 ＝ 2639。
    /// P3 第二批（小地图控制器 `LocalMapController` ＋ 用例 `TestLocalMap` 一起搬 C#）之后实测 3132，
    /// 于是 3132 − 10 ＝ 3122（这一批搬进来 483 条断言：479 条来自原 `test_local_map.gd`，
    /// 4 条是搬 C# 时补的——`_check_no_orphan_code` 之类按文件逐条算的那几条）。
    /// P3 第三批（大地图控制器 `OverworldController` ＋ 用例 `TestOverworld` 一起搬 C#）之后实测 3364，
    /// 于是 3364 − 10 ＝ 3354（这一批搬进来 232 条断言，原 `test_overworld.gd` 的对位实现）。
    /// P3 第四批（`src/ui` 五个叶子库 ＋ 两个用例一起搬 C#）之后实测 3412，
    /// 于是 3412 − 10 ＝ 3402（这一批搬进来 48 条断言：LayoutBudget 22／OverlayStack 26）。
    /// P4 第一批（`tests/test_cooldown.gd` → `tests/TestCooldown.cs`）之后实测 3433，
    /// 于是 3433 − 10 ＝ 3423（这一批搬进来 21 条断言，原用例的对位实现）。
    /// 同批给骨架补了一条「C# 用例也要用固定种子」的扫描（GDScript 那条只扫 `.gd`）⇒ 实测 3434，
    /// 于是 3434 − 10 ＝ 3424。
    /// P4 第二批（`tests/test_multihit.gd` → `tests/TestMultihit.cs`）之后实测 3454，
    /// 于是 3454 − 10 ＝ 3444（这一批搬进来 20 条断言）。
    /// P4 第三批（`tests/test_poise.gd` → `tests/TestPoise.cs`）之后实测 3491，
    /// 于是 3491 − 10 ＝ 3481（这一批搬进来 37 条断言）。
    /// P4 第四批（`tests/test_clue.gd` → `tests/TestClue.cs`）之后实测 3583，
    /// 于是 3583 − 10 ＝ 3573（这一批搬进来 92 条断言——与旧链那份的「断言 92／静态 48」逐条对齐）。
    /// P4 第五批（`tests/test_drop.gd` → `tests/TestDrop.cs`）之后实测 3620，
    /// 于是 3620 − 10 ＝ 3610（这一批搬进来 37 条断言）。
    /// P4 第六批（`tests/test_save_timing.gd` → `tests/TestSaveTiming.cs`）之后实测 3643，
    /// 于是 3643 − 10 ＝ 3633（这一批搬进来 23 条断言）。
    /// P4 第七批（`tests/test_guide.gd` → `tests/TestGuideSteps.cs`）之后实测 3684，
    /// 于是 3684 − 10 ＝ 3674（这一批搬进来 41 条断言；静态 18 处，多出来的是按表循环点旗标那几条）。
    /// P4 第八批（`tests/test_encounter.gd` → `tests/TestEncounterRules.cs`）之后实测 3707，
    /// 于是 3707 − 10 ＝ 3697（这一批搬进来 23 条断言）。
    /// P4 第九批（`tests/test_audio.gd` → `tests/TestAudio.cs`）之后实测 3738，
    /// 于是 3738 − 10 ＝ 3728（这一批搬进来 31 条断言；静态 16 处，多出来的是按事件／文件循环那几条）。
    /// P4 第十批（`tests/test_battle_session.gd` → `tests/TestBattleSession.cs`）之后实测 3756，
    /// 于是 3756 − 10 ＝ 3746（这一批搬进来 18 条断言；静态 10 处，多出来的是逐回合那 10 条）。
    /// P4 第十一批（`tests/test_battle_fuzz.gd` → `tests/TestBattleFuzz.cs`）之后实测 3761，
    /// 于是 3761 − 10 ＝ 3751（这一批搬进来 5 条断言）。
    /// P4 第十二批（`tests/test_level.gd` → `tests/TestLevelFlow.cs`）之后实测 3818，
    /// 于是 3818 − 10 ＝ 3808（这一批搬进来 57 条断言；静态 39 处，多出来的是逐级递增那 19 条）。
    /// P4 第十三批（`tests/test_dungeon_ui.gd` → `tests/TestDungeonUi.cs`）之后实测 3850，
    /// 于是 3850 − 10 ＝ 3840（这一批搬进来 32 条断言）。
    /// P4 第十四批（`tests/test_world_event.gd` → `tests/TestWorldEventWiring.cs`）之后实测 3915，
    /// 于是 3915 − 10 ＝ 3905（这一批搬进来 65 条断言；静态 37 处，多出来的是按八行逐条比那 28 条）。
    /// P4 第十五批（`tests/test_growth.gd` → `tests/TestGrowthRules.cs`）之后实测 3978，
    /// 于是 3978 − 10 ＝ 3968（这一批搬进来 63 条断言；静态 54 处，多出来的是图鉴七项那几条）。
    /// P4 第十六批（`tests/test_settings.gd` → `tests/TestSettingsRuntime.cs`）之后实测 4021，
    /// 于是 4021 − 10 ＝ 4011（这一批搬进来 43 条断言）。
    /// P4 第十七批（`tests/test_guard.gd` 退场，覆盖已由 `tests/TestGuard.cs` 兜住）**不动这一行**。
    /// P4 第十八批（`tests/test_skill.gd` → `tests/TestSkill.cs`）之后实测 4163，
    /// 于是 4163 − 10 ＝ 4153（这一批搬进来 142 条断言；静态 42 处，多出来的是按武学／敌人／招式逐条循环那几条）。
    /// P4 第十九批（`tests/test_mastery.gd` 并入 `tests/TestMastery.cs` 后退场）之后实测 4200，
    /// 于是 4200 − 10 ＝ 4190（这一批补进来 37 条断言：熟练度倍率＋存档往返／多人界面／整套预算）。
    /// P4 第二十批（`tests/test_practice.gd` 并入 `tests/TestPractice.cs` 后退场）之后实测 4215，
    /// 于是 4215 − 10 ＝ 4205（这一批并进来 25 条断言：练习战熟练度封顶／内存夹具遭遇／木桩不还手）。
    /// P4 第二十一批（`tests/test_talent.gd` 并入 `tests/TestTalent.cs` 后退场）之后实测 4243，
    /// 于是 4243 − 10 ＝ 4233（这一批并进来 28 条断言：rule: 效果逐条——判定加成／门槛／图鉴翻倍／
    /// 买价折扣／首回合先手／招式伤害，外加「每条 rule: 都有人在读」那道防死数据的门限）。
    /// P4 第二十二批（`tests/test_enemy_template.gd` **直接退场**：C# 侧已是超集）之后实测不变 4243
    /// （退场不加断言），基线维持 4233。
    /// P4 第二十三批（`tests/test_affix.gd` 并入 `tests/TestAffix.cs` 后退场）之后实测 4307，
    /// 于是 4307 − 10 ＝ 4297（这一批并进来 64 条断言：词条真的进属性合成／存档往返／掉落接词条／
    /// 武器系别覆盖招式系别／凡品条数边界／五件武器的槽位与稀有度门槛／「加一档稀有度」夹具）。
    /// P4 第二十四批（`tests/test_save_store.gd` 并入 `tests/TestSaveStore.cs` 后退场）之后实测 4330，
    /// 于是 4330 − 10 ＝ 4320（这一批并进来 23 条断言：开局队伍照 recruit_def／落盘 JSON 合法性与版本号／
    /// 全字段往返 27 项 diff 与比较器自查／三个可用档按时间选最旧／存档体积量级）。
    /// P4 第三十四批（`tests/test_end_to_end.gd` 整体搬进**新建**的 `tests/TestEndToEnd.cs` 后退场）之后
    /// 实测 6222，于是 6222 − 10 ＝ 6212（这一批新搬进来 60 条断言：表校验＋组队＋模拟一场明雷＋
    /// 奖励入账与掉落种类／带伤进下一场与医馆按缺失真治／装备与内功加成穿过 PartyBuilder 到战斗单位／
    /// 奖励落账后存档往返／多人队伍的默认队伍来源与按人记的等级加点／判定「谁行谁上」／
    /// 经验池先补等级最低的／医馆按全队缺失求和／多角色存档往返）。
    /// P4 第三十五批（`tests/test_save_migration.gd` 整体搬进**新建**的 `tests/TestSaveMigration.cs`
    /// 后退场）之后实测 6585，于是 6585 − 10 ＝ 6575（这一批新搬进来 363 条断言：v1 老档直读与走存档槽
    /// 再走菜单读档的写回／v2 的钱与材料与装备与加点往返／v3 补出「已学＋装配」／v4 装备实例没有 affixes
    /// ／v9 首杀为空／v10 气血按满血／v11 战斗外增益与到期时间戳随档走（坏条目丢掉）／v12 缺 `world_pos`
    /// 退默认出生点（写坏的坐标也不许读进来）／v13 天赋与自建角色模板重新注入 `character_base`／
    /// **v1…v14 逐档**造老档全读一遍（版本号是权威）／下架角色替换与等级继承／坏档判定／
    /// 六种字段类型写错只许判损坏或取缺省、不许运行期炸）。
    /// P4 第三十六批（`tests/test_event_check.gd` 整体搬进**新建**的 `tests/TestEventCheck.cs` 后退场）
    /// 之后实测 6857，于是 6857 − 10 ＝ 6847（这一批新搬进来 272 条断言：表里 14 条判定的来源格式与
    /// 难度为正与判定值算得出来／判定值取自面板同一套（裸属性）与达标临界值／hard 不足不掷骰没有概率／
    /// soft 公式与 5%~95% 上下限／同种子可复现且换种子两边都出现（59 颗种子各两条）／掷中发奖励掷不中
    /// 不惩罚且旗标各钉一条／奖励三类（道具进包／房间顺带揭开地标／剧情旗标）与失败文案／
    /// once_only 做过不能再做而失败可重试／结果与旗标进存档（含 v8 迁移）／equip 真发实例＋
    /// skillbook 与 boss 各有落点＋非法 reward_type 与四种坏 id 都显式报错且**不漏表内 id**／
    /// 赌局赢的是钱而不是背包里一行「铜钱」／位点归属（scene_id 命中或 region_id 相符）。
    /// P4 第三十七批（`tests/test_table_pipeline.gd` 整体搬进**新建**的 `tests/TestTablePipeline*.cs`
    /// 后退场）之后实测 7153，于是 7153 − 10 ＝ 7143（这一批新搬进来 296 条断言：正例 241 条——
    /// 表库能加载／表数与注册表一致／真实配置表零校验错误／每张表的行数与 CSV 逐张一致／
    /// 抽查列别名与复合主键与多值列（attr_to_stat 的 id→row_id、element_counter 与
    /// difficulty_drop_rate 的复合主键、enemy_base 的 exp→exp_reward、enemy_team 的 members 解析、
    /// character_base／weapon_type_def／combat_const／event_check／equip_slot_def／shop_stock／
    /// building_def／item_base 的抽查值）／`data/tables` 与 `table_registry.TABLES` **双向**对齐；
    /// 负例 55 条——写坏引用与主键重复与五维对不上与事件判定缺前缀／index 与 rows 错位（两张表都要
    /// 被点名、且逐行报）／词条条数区间倒挂／经验门槛反向与白送一级／比率没写上限／起手招式与武器
    /// 对不上／难度阶梯倒挂／解锁短语没登记／缺代码依赖的常数／开局装不上（武器类型与等级两条）／
    /// 成长常数低于可用下限（两条各报一条）／五条扫描区间（0 层状态、0 回合持续、>100% 触发率、
    /// 负售价、负铜钱）／非法槽位与卖价倒挂与 use_context 非法与武器槽缺类型／extra_rule 拼错与
    /// 条件键拼错与非法击杀方式与 ai_template 拼错／hit_count=0（改回 1 段要**恢复零错误**）／
    /// 负 poise_damage／start_level 两条越界／负 status_chance_mul／负经验与负加点／drop_group 里
    /// slot 重复／level_growth 缺号／三种词条目标错（正好各报一条）／升级会变弱／装备等级需求超上限／
    /// 敌人负防御与超额抗性／难度倍率非正／非白板装备全零加成／内功加成白填／第 0 层与缺第 2 层／
    /// **校验器自己的规则表自检**。拆法同第 35 批：整份 900 行上下撞 800 行硬上限，拆成
    /// 「抽象基类 `TestTablePipelineBase.cs`（不注册）＋ 正例 `TestTablePipeline.cs` ＋
    /// 负例 `TestTablePipelineRules.cs`」；两族**注册在 `Suites` 最前面**，因为读的是共享表库，
    /// 而后面的 `TestCreation` 会往里注入自建角色模板，跑到最后比行数就会多出一行。）
    /// P4 第三十八批（`tests/test_buff.gd` 十二节整体搬 C#：表逐行自洽并进 `tests/TestBuff.cs`，
    /// 落地那半**新建** `tests/TestBuffIntegration.cs`）之后实测 7635，于是 7635 − 10 ＝ 7625
    /// （这一批新搬进来 482 条断言：`TestBuff.cs` 补的六张表**逐行**自洽 365 条——枚举取值、
    /// `max_stack` 规则、每一行的外键真的存在；`TestBuffIntegration.cs` 117 条——套装计数口径
    /// （只算已装备／已装配、内功套按占格数之和、档位向下兼容、武器类型拒绝的理由）、
    /// 常驻 buff 走贡献通道进面板（锈月／遗物／套装两件档，来源写着 buff@实例 id 与 set_id）、
    /// `BattleActor` 的叠加与刷新与回合末递减与 computed 运功与 special 记「暂缓规则」、
    /// 六指令（普攻不进招式使用记录／主动防御回架势且下回合先手／内功催动与拒绝理由／
    /// 战斗道具列得出但写明效果未配）、进场 buff 整场有效且发完清空、战斗外增益按现实分钟计时
    /// 并带进战场且战斗结束不清、招式自带的 `on_cast`／`on_hit` 发放（叠层类一次行动只 +1、
    /// 写错 buff 时不静默）、叠层按层数放大（一层 +5／三层封顶）、增益招式醉步
    /// （两个查询口径一致、施放不打敌人、放完进冷却、自动战斗不会自己放它）。
    /// 表自洽与两个服务的**静态**规则本来就在 `tests/TestBuff.cs`（那一份原来只有静态半边），
    /// 现在两边合起来覆盖旧链全部十二节。）
    /// P4 第四十三批（`tests/test_battle_ui.gd` 整体搬进**新建**的 `tests/TestBattleUi.cs`，
    /// 另拆两份 partial：`tests/TestBattleUiSettles.cs`（结算那几支）／
    /// `tests/TestBattleUiCommands.cs`（指令按钮那一支））之后实测 8339，
    /// 于是 8339 − 10 ＝ 8329（这一批新搬进来 **267** 条断言，与旧链那份「断言 267／静态 260」
    /// 逐条对得上——一个用例一条不落：四道闸门常量／骨架与场景节点／异常状态行与跳字落点夹取／
    /// 三种资源条与临界·破防贴图／结算卡片三行契约／逃跑三态与现算成功率／败北·平局·练习战
    /// 三支零收获／自动存档失败不静默／首杀只给一次（含落盘读回）／两个 Boss 真打完才解锁绝境／
    /// 打赢某支队伍置旗标与账册只发一次／招式按钮置灰写明原因／增益招式不打人／六指令与
    /// 合并的增益减益面板（含 ▼／×N／∞／减益在前）／预兆与拆招／自动战斗自己打到底＋
    /// 持久开关与「点指令立刻接管」／敌人剪影「有图才摆」。拆分只为不撞 800 行硬上限——
    /// 假绿门限按**注册的那一份**数静态点（105 处），而 `Checks` 是三份合计，所以比它多是对的。）
    /// P4 第四十二批（`tests/test_copy_audit.gd` 九节整体搬进**新建**的 `tests/TestCopyAudit.cs`）之后
    /// 实测 8072，于是 8072 − 10 ＝ 8062（这一批搬进来 62 条断言：`src/` 下不再出现四句过时文案
    /// （含注释）、`display_name` 六种 id 形态与「查不到就原样返回」、HUD 五键提示条要点全
    /// （含 C# 多行拼接续行）、启动菜单与枢纽页按钮文案逐字＋`_check_no_id_tokens` 递归扫
    /// Label／Button 文字里不许出现表内 id、存档里下架角色／未知难度时标签与状态行不漏 id、
    /// 未接结算的伤害类型报中文名而不是 `dot_poison` 且可用时不给原因、奇袭暂缓规则口径写
    /// 「设计未定」而不是甩锅架势系统、拿 `data/tables` 的表名当词表扫 `src/` 字符串字面量
    /// （三条排除＋白名单双向维护）。白名单 `TableNameInTextAllowed` 的键改成**文件名**，
    /// 与 `_AllowedFile` 的口径一致；`text.UnicodeAt(i)` 在 C# 侧没有，改成 `text[i]`。）
    /// P4 第四十一批（`tests/test_character_ui.gd` 十二节整体搬进**新建**的 `tests/TestCharacterUi.cs`）之后
    /// 实测 8010，于是 8010 − 10 ＝ 8000（这一批新搬进来 135 条断言：页签三条与默认停哪页／
    /// 头部铜钱与经验／五维加号写进存档且未分配点数减少／悟性·根骨那两行置灰并写明「资质」与
    /// 「不可加点」／五维行撑满整行（旧链量 `get_line_count()` 是**空转**的，见施工单「二点九七」）／
    /// 武学装配与卸下的往返与状态栏提示、装不上的拳法置灰、增益招式与「暂未生效」不甩内部 id／
    /// 装备槽卸下与装回、候选标题写中文槽位名、不匹配武器类型置灰、状态栏不漏 `eq_` 与实例序号／
    /// 背包堆叠数量与钥匙拒丢的理由、按 `is_key_item` 筛（铁镐是 tool 但算钥匙）、秘籍门槛与研读／
    /// 图标路径按 id 拼、没图不占位（拿"文件在不在"当期望值，不写死某张还没出的图）／
    /// 大背包（22 种道具 ＋ 30 件实例）的存档往返与版式／4 人满员与长名字的成员行宽度。）
    /// P4 第四十批（`tests/test_battle.gd` 十四节整体搬进**新建**的 `tests/TestBattle.cs`）之后实测 7875，
    /// 于是 7875 − 10 ＝ 7865（这一批新搬进来 113 条断言：阵营与攻击／防御／拆招／破绽／逃跑六条
    /// **兜底常量**的绝对值、同速稳定次序、难度三档按 `enemy_hp_mul`／`enemy_atk_mul`／`enemy_def_mul`
    /// 现算（含「越难越厚越疼」的单调性）、同种子日志逐字一致、默认回合上限打满 50 回合判平局、
    /// 预兆三条与拆招（伤害减半、反涨架势钉死 6、花掉这次行动、同一敌人一回合只能拆一次）、
    /// `stat:extra_hit` 段数 +1 且普攻不吃、内功催动按表扣 6 气与气不够的拒绝理由、
    /// 逃跑四段公式（含顶到 95%／25% 两端）与被追上第一回合撤不了、气血正好打空与「负伤害不是治疗」、
    /// 三档策略各挑各的且不影响敌人、`target_type=self` 打自己（手选与自动两条路）。）
    /// P4 第三十九批（`tests/test_dialogue.gd` 十五节整体搬 C#：容器那一侧的十一节并进
    /// `tests/TestDialogue.cs`，「人」那一侧的三节**新建** `tests/TestDialogueNpc.cs`）之后实测 7762，
    /// 于是 7762 − 10 ＝ 7752（这一批新搬进来 127 条断言：`TestDialogue.cs` —— 陈氏／幕四地牢／
    /// 序幕择念／幕二林铁山四条路／幕一客栈／终局账册六处台词与选项逐字核对（含交心那句不许被
    /// 任何选项指到、账册三选一不叠加）、条件语言 `&`／`|`／`!`／`item:`、效果三样（旗标／好感／
    /// 给东西）与负好感地板钉绝对值、`set_flag` 分号多值（单值行为不变、带 `heart_*` 照旧补
    /// `flag_open_*`、校验器点名认不出的那一段）、前置没满足时选项不出现且硬选报「前置」；
    /// `TestDialogueNpc.cs` —— 老周挂在大地图区域节点上而清风驿不串台、同伴没有 `npc_def` 行但
    /// `person_of` 按同一形状合成一行、好感行里赠送 5／四位同伴各自的切磋数／不可偷、
    /// 孙掌柜那条额外给燕小七好感 +10。）
    /// P4 第三十三批（`tests/test_world_map.gd` 七节服务层口径并进 `tests/TestOverworld.cs` 后退场）之后
    /// 实测 6162，于是 6162 − 10 ＝ 6152（这一批并进来 64 条断言：开局点亮哪两处与幂等／持有类揭开
    /// （藏宝图→石隙迷窟）／`proximity_N` 的 3 格与 5 格门槛连同「正好卡在门槛上」的 off-by-one／
    /// 落雁坡走近后链式带出黑风寨／0.32.0 两条表侧规则（发行数据逐行点名 ＋ 改坏了构建期真会红）／
    /// 传送目的地的取舍与两条拒绝理由／难度解锁条件与切难度与列表里只有一个「当前」／
    /// 揭开状态与难度进存档（含 v7 老档迁移））。
    /// P4 第三十二批（`tests/test_status.gd` 十一节整体并进 `tests/TestStatusRules.cs` 后退场）之后实测 6098，
    /// 于是 6098 − 10 ＝ 6088（这一批并进来 66 条断言：触发几率与抗性打折／叠层与刷新时长／快照制／
    /// DoT 逐类型的防御口径／灼伤削防与内伤减内力回复（含改表读表两条）／未实现规则报进暂缓规则／
    /// 可驱散／整场战斗流程与斩杀死因／敌人抗性（含内伤抗性上限读表）／玩家抗性走同一条派生数值／
    /// 回合末回复的封顶与死人两种边界）。
    /// P4 第三十一批（`tests/test_shop.gd` 整体搬进**新建**的 `tests/TestShop.cs` 后退场）之后实测 6032，
    /// 于是 6032 − 10 ＝ 6022（这一批新搬进来 251 条断言：五家货架按表与升序/买卖两个价／买入与「差一文」
    /// 边界／钥匙与穿着中与未上架的三条拒收／回购价与上限 20 与词条原样回来／回购「部分入包」三态／
    /// 一次买 5 件出 5 个独立实例与 overflow 只按入包数量收钱／一次卖光不留 ×0 空行／
    /// 限量货架服务层与界面层／医馆多人分支 4 行预览与全队回满／治疗费公式与缺钱被拒／
    /// 交易状态进存档与 v5 老档迁移）。
    /// P4 第四十四批（`tools/mapgen/map_kit.gd` 整体搬进**新建**的 `tools/mapgen/MapKit.cs`
    /// ＋`MapKitPaint.cs`＋`MapKitExport.cs`，`tests/test_map_assets.gd` 整体搬进**新建**的
    /// `tests/TestMapAssets.cs`＋`TestMapAssetsAssets.cs` 之后实测 8717，于是 8717 − 10 ＝ 8707
    /// （这一批新搬进来 378 条断言——大地图与小地图的节点口径、房间与触发与观察点、事件判定、
    /// 敌人位点与回程出口、四类工具方法、地表分层、六套主题 TileSet 逐格碰撞与 covering、三类图标
    /// 文件名对表、指路牌。**比旧链「断言 379」少 1 条**：旧链那条是 `check_false(kit == null,
    /// "读得到地图工具箱 map_kit.gd")`——工具箱搬成编译器里的静态类之后，「加载失败」这条路
    /// 根本不存在，所以这条断言没有对应物。拆两份 `TestMapAssets*` 只为不撞 800 行硬上限，
    /// 注册进 `Suites` 的是 `TestMapAssets.cs` 那一份。）
    /// P2 起每搬一个模块都要把这一行跟着抬——**别把基线调松**。
    /// </summary>
    /// P4 第四十八批（`tests/test_handshake_contract.gd`／`test_handshake_wiring.gd`／`test_handshake_flags.gd`
    /// 三族搬进**新建**的 `tests/TestHandshakeContract.cs`／`TestHandshakeWiring.cs`／`TestHandshakeFlags.cs`，
    /// 基类与共用夹具在 `tests/TestHandshake.cs` ＋ `TestHandshakeFloors.cs` ＋ `TestHandshakeHelpers.cs`）之后
    /// 实测 9604，于是 9604 − 10 ＝ 9594（这一批新搬进来 887 条断言：对接 729／接线 133／旗标 25；
    /// 那时剩下五族还在 `tools/run_tests.gd` 那条链上）。别把基线调松。
    /// P4 第四十九批（`tests/test_handshake_numbers.gd` → **新建** `tests/TestHandshakeNumbers.cs`）之后
    /// 实测 9737，于是 9737 − 10 ＝ 9727（这一批新搬进来 133 条断言：01 采样表对公式／闭环段数／
    /// 特殊效果条数／看板与交接单的表行数／对照表的判定与武学比／场景自检棘轮与个数声称）。
    /// P4 第五十批（`tests/test_handshake_doccounts.gd` → **新建** `tests/TestHandshakeDoccounts.cs`）之后
    /// 实测 9826，于是 9826 − 10 ＝ 9816（这一批新搬进来 89 条断言：接触手感三个百分比对文档现读／
    /// 对照表 ✅ 行的符号在代码语料里找得到／15 §4.3 与美术交接单的图标数对表行数／设计文档里的
    /// 「某张表 N 行」逐条对账／带点号的「表.列」引用双向维护／「待补充的表」不许已存在）。
    /// P4 第五十一批（`tests/test_handshake_code.gd` → **新建** `tests/TestHandshakeCode.cs`
    /// ＋ `TestHandshakeCodeGuards.cs`）之后实测 10179，于是 10179 − 10 ＝ 10169（这一批新搬进来
    /// 353 条断言：界面节点契约逐处对 `.tscn`／框架说明两个存档数字／绝对路径取 autoload／
    /// 跨文件调用必须 preload／用例不许无种子随机源／工程入口与 autoload／`src` 不许碰
    /// `data/tables`＋`tools` 的 bat/ps1 编码审计／单一定义规则逐条／「只被测试调用」的接口
    /// 逐名对白名单／造战斗界面必须固定种子／`to_dict()` 字段双向对账）。
    /// P4 第五十二批（`tests/test_handshake_repo.gd` → **新建** `tests/TestHandshakeRepo.cs`
    /// ＋ `TestHandshakeRepoFiles.cs`）之后实测 11120，于是 11120 − 10 ＝ 11110（这一批新搬进来
    /// 935 条断言：每个 `.uid`／`.import` 都得有源文件（光这两项就 160+ 条）、孤儿脚本与场景、
    /// 根目录场景白名单两向、`data/tables` 不许有导入产物、`.gitignore` 与 `.gitattributes` 契约、
    /// 开发文档引到的工具必须存在、枚举列要在 `validate_tables.ps1` 里出现、文本文件不许有控制字符、
    /// 文档里 TAB 紧跟字母、单文件 800 行硬上限两向 ＋ 抬头数字、UI 字号只许两档）。
    /// P4 第五十三批（`tests/test_handshake_docs.gd` → **新建** `tests/TestHandshakeDocs.cs`
    /// ＋ `TestHandshakeDocsTables.cs`）之后实测 11677，于是 11677 − 10 ＝ 11667（这一批新搬进来
    /// 557 条断言：06 的枚举逐列与代码／数据三方对账、01／05 的六张「写死的表」逐格比、
    /// 07 的逐位点明雷表逐格比、05 的商店货架与四家店口径、代码里的战斗常数与字面量查表、
    /// 数据字典覆盖每一列、副本文档的隐藏内容表、商店页签与场景按钮一一对应）。
    /// P4 收口（2026-10-11）：八族全搬到 C# 侧、旧 GD 链退休——`tools/run_tests.gd` 与
    /// `tests/test_handshake.gd` 已删，`tools/run_tests.bat` 改成转发到 `tools/run_tests_cs.bat`
    /// （顺手把 `tests/TestHandshakeContract.cs` 那份「执行列表」只留 `RunTestsCs.cs` 一份，
    /// `tests/TestHandshakeRepoFiles.cs` 给 79 处 `tools/run_tests.gd` 文档指针开了
    /// `RetiredToolPaths` 豁免、改成反向钉住「它确实已不在」）。于是实测 11614，MinAssertions
    /// 11667 → **11604**（少 63 条＝豁免掉的那批文档引用）。
    /// P4 第五十五批（2026-10-11）：`tools/build_tables.gd` 整体搬成 `tools/BuildTables.cs` ＋
    /// `tools/build_tables.tscn`（五个调用面跟着从 `--script …gd` 换成场景入口），文档门限的
    /// 后缀表补上 `.tscn`（C# 工具的入口都是场景，漏掉它等于最主要的一类入口没人验），
    /// `RetiredToolPaths` 加 `build_tables.gd`。于是实测 11632，MinAssertions 11604 → **11622**。
    /// （2026-10-11 又吃一次决策 57 的亏：这两段说明里把那条**不存在的** `run_tests.tscn`
    /// 带 `tools/` 前缀写了进去，门限连反例都当真路径验，当场红两条——写反例别带前缀。）
    /// P4 第五十六批（2026-10-11）：`analysis_balance`／`analysis_perf`／`tests/test_case.gd` 三份整体
    /// 搬 C#（`tools/AnalysisBalance.cs`／`AnalysisPerf.cs`／`tests/TestCase.cs` 本来就有一份夹具），
    /// `RetiredToolPaths` 加 `analysis_balance.gd`／`analysis_perf.gd`。于是实测 **11617**，
    /// MinAssertions 11622 → **11607**（少 15 条＝这两条豁免掉的历史文档引用：`analysis_balance.gd` 8 处
    /// ＋ `analysis_perf.gd` 9 处，再补回 `RetiredToolPaths` 多出来的 2 条反向钉住；这批**没有**用例被删）。
    /// P4 第五十六批 · 收尾（2026-10-11）：`analysis_mutations`／`_shot`／`artgen/font_preview` 三份工具
    /// 与 `tests/test_case.gd` 一并退休——`tools/AnalysisMutations.cs`／`Screenshot.cs`／`artgen/FontPreview.cs`
    /// 顶上来，`RetiredToolPaths` 再加 `analysis_mutations.gd`／`_shot.gd`／`artgen/font_preview.gd`
    /// （`docs/dev` 里剩下的 5 处旧指针全在 `交接/小程序.md` 的历史叙事里）。于是实测 **11608**，
    /// MinAssertions 11607 → **11598**（total−10；这批**没有**用例被删，差值全是两条门限口径的涨落）。
    /// P5 收尾（2026-10-11）：`src/` 下 `.gd` 归零之后 `CheckPreloadForGlobalClassCalls` 成了空转
    /// （三个断言恒真），连同根 `AGENTS.md` 那条 `preload` 约定一起退场；同批把 `CheckNoNewTestOnlyApi`
    /// 的 C# 正则补上 `public static`（以前静态口在这道门里整个隐形），浮出来的 56 条过渡期取值口
    /// 逐条写进白名单。于是实测 **11605**，MinAssertions 11598 → **11595**（total−10；
    /// 少的 5 条＝删掉的那 3 条 ＋ 探针第三块清豁免后文档口径的两处涨落）。
    /// 战斗界面第五版（2026-10-12，P13）：顺序条定案 ＋ 卡片壳删掉之后，用例从「卡片上的行」
    /// 改指向**挂在人身上的挂件**（头顶条／状态图标／预兆签），断言条数随之上涨。于是实测 **11707**，
    /// MinAssertions 11595 → **11697**（total−10；这批**没有**用例被删，差值全是改指 + 新加的口径）。
    /// 战斗界面第六版（2026-10-12，P15）：删掉「▶ 推进」＋流程自动推进 ＋ 结算卡那枚「确定」＝返回。
    /// 用例改指（▶ → ResultConfirm）并新增 `_CheckAutoAdvance`（没有推进钮／轮到我方 tick 停下／
    /// 暂停条开着 tick 不推进／收起即继续）。于是实测 **11746**，MinAssertions 11697 → **11736**（total−10）。
    /// 战斗界面第八版（2026-10-12，P17）：三行小卡片 → 整屏结算面板（六块：标题／经验＋逐人升级行／
    /// 铜钱／掉落两列／武学／「确定」）。用例改指 `_CheckSettleCardContract` → `_CheckSettlePanelContract`
    /// （五块都在／升级每人一行且与 `LevelService.describe` 同源／面板自身高 ≤648／口径③四块不出现），
    /// 场景自检那两档改指 `_check_settle_panel`。于是实测 **11781**，MinAssertions 11736 → **11771**（total−10）。
/// 第八版收口（2026-10-12 三轮）：结算那一帧其余挂件整组收起（回合标签／状态播报／Boss 长血条／战报面板，
/// 只留「战报」那枚入口钮）＋掉落格文案不再被挤成一字一行；`TestBattleUiSettles` 补 6 条钉住它。
/// 于是实测 **11787**，MinAssertions 11771 → **11777**（total−10）。
/// 同日 P18（口径④）再 +7：结算帧只剩三样（`_check_settle_frame`）＋战报钮盖在面板之上＋跳字层收起
/// ＋「还有 N 件放不下」那一行。于是实测 **11794**，MinAssertions 11777 → **11784**（total−10）。
/// 角色页排版（2026-10-12 第 4 屏）：`TestCharacterUi` 补「值列只放数值／来源列不换行／
/// 来源写法是「+N 装备」或「—」／资质行的来源列写招式槽·心法格」那一族。
/// 于是实测 **11803**，MinAssertions 11784 → **11793**（total−10）。
/// 装备页（2026-10-12 UI 清单 §二-5）：`TestCharacterUi` 补「三栏／七槽／空槽写可装／装备需求写 Lv／
/// 词条区＋洗词条置灰／点候选只选中／换上之后预演值＝真穿上值」那一族，
/// `TestHandshake` 顺带把「已接进游戏路径」的 `weapon_type_name_of` 从 TEST_ONLY_API_ALLOWED 划掉。
/// 于是实测 **11821**，MinAssertions 11793 → **11811**（total−10）。
/// 装备需求那一问的回执（2026-10-12 小策划 P19 ②）：`TestCharacterUi` 补「武器槽需求行写 `Lv N（剑）`／
/// 已满足／类型不匹配时写「本角色只能用剑」且不漏表内 id」那三条（装备页那一簇随文件拆分搬进
/// `tests/TestCharacterEquipUi.cs`）。于是实测 **11828**，MinAssertions 11811 → **11818**（total−10）。
private const int MinAssertions = 11818;

    /// <summary>余量上限：实际 − 下限超过它，说明下限松了（这条网在退化成摆设）。</summary>
    private const int SlackLimit = 100;

    /// <summary>真有正当例外（断言写在条件分支里）才登记，并写明理由；空着是常态。</summary>
    private static readonly string[] SkippedCheckWhitelist = Array.Empty<string>();

    /// <summary>
    /// 数"断言调用点"：要求方法名紧跟左括号，避免把说明文字算进去。
    /// **故意不算 `Fail(`**：那是失败路径的回报口，不执行才说明数据是对的。
    /// 注意要把 `NotNull` 排在 `Null` 前面，否则 `CheckNotNull(` 会被截成 `CheckNull(`。
    /// </summary>
    private const string CheckCallPattern = @"Check(?:True|False|Eq|Ne|Float|InRange|Gt|Lt|NotNull|Null)\(";

    private static readonly Regex CheckCallRegex = new(CheckCallPattern, RegexOptions.Compiled);

    public override void _Ready()
    {
        // **不能在这一下直接跑**：`_Ready` 正处在「根节点正在装配子节点」的窗口里，
        // 用例往 `Tree.Root` 挂真场景（`local_run.tscn` 那几支）会报
        // 「Parent node is busy setting up children, add_child() failed」——**挂不上去**，
        // 而用例照样往下跑（静默少测一大批）。延到本帧空闲再跑，与 GDScript 那条链
        // （`--script` 在主循环之前跑）等价。2026-10-11 小地图用例实测。
        Callable.From(() => GetTree().Quit(RunAll())).CallDeferred();
    }

    private int RunAll()
    {
        GD.Print("=== 自检开始（C# 侧） ===");
        var totalChecks = 0;
        var failures = new List<string>();
        var skippedUsed = new HashSet<string>();

        foreach (var (path, factory) in Suites)
        {
            if (!FileAccess.FileExists(path))
            {
                failures.Add($"{path} 不存在（用例清单写错了？）");
                continue;
            }

            TestCase testCase;
            try
            {
                testCase = factory();
            }
            catch (Exception error)
            {
                failures.Add($"{path} 无法实例化：{error.Message}");
                continue;
            }

            testCase.Tree = GetTree();
            try
            {
                testCase.Run();
            }
            catch (Exception error)
            {
                // 在 GDScript 里这类错误是"掐断函数、后面静悄悄不跑"；C# 会抛出来，
                // 这里转成一条失败，比让它掀掉整条自检更接近原来那道网。
                failures.Add($"[{testCase.SuiteName()}] 运行期异常：{error.Message}");
                // 只写 message 会把定位成本全推给下一步（世界事件那批踩过：NullReference 不说是哪一行），
                // 所以把完整异常（含堆栈）也打一份到日志。
                GD.PrintErr($"[运行期异常] {testCase.SuiteName()}：{error}");
            }

            totalChecks += testCase.Checks;
            var staticSites = CountCheckSites(FileAccess.GetFileAsString(path));
            GD.Print($"--- {testCase.SuiteName()} ---（断言 {testCase.Checks}／静态 {staticSites}）");
            if (testCase.Checks < staticSites && !Array.Exists(SkippedCheckWhitelist, item => item == path))
            {
                failures.Add(
                    $"[假绿] {path} 写了 {staticSites} 处断言，只跑了 {testCase.Checks} 条：有条件分支或空循环没走到。"
                        + "补断言，或把它写进 SkippedCheckWhitelist 并说明理由");
            }
            else if (testCase.Checks < staticSites)
            {
                skippedUsed.Add(path);
            }

            foreach (var message in testCase.Failures)
            {
                failures.Add($"[{testCase.SuiteName()}] {message}");
            }
        }

        // 白名单要双向维护：条目对应的用例已经不需要豁免（实际执行数追平静态断言点），
        // 那条豁免就该退场——留着会让"以后再漏跑断言"没人发现。
        foreach (var path in SkippedCheckWhitelist)
        {
            if (!FileAccess.FileExists(path))
            {
                failures.Add($"[白名单过期] SkippedCheckWhitelist 里的 {path} 不存在了，把这条删掉");
            }
            else if (!skippedUsed.Contains(path))
            {
                failures.Add(
                    $"[白名单过期] SkippedCheckWhitelist 里的 {path} 已经不需要豁免了"
                        + "（实际执行数不再少于静态断言点），把这条删掉");
            }
        }

        if (failures.Count == 0)
        {
            if (totalChecks < MinAssertions)
            {
                failures.Add(
                    $"断言数 {totalChecks} 少于基线 {MinAssertions}：有用例中途报错退出了，或用例／数据表行数被删减？"
                        + "（确认无误就把 tools/RunTestsCs.cs 的 MinAssertions 改到新值——别把基线调松）");
            }
            else if (totalChecks - MinAssertions >= SlackLimit)
            {
                failures.Add(
                    $"断言基线松了：实际 {totalChecks}、基线 {MinAssertions}，余量 {totalChecks - MinAssertions}"
                        + $" 已到上限 {SlackLimit}——把它贴到 {totalChecks - 10}（total−10）。"
                        + "余量太大就等于没有下限：删掉一大块断言也不会红");
            }
        }

        if (failures.Count == 0)
        {
            GD.Print($"=== 自检通过：{totalChecks} 条断言 ===");
            // run_tests_cs.bat 以这行标记为准（中途报错就不会打印它，自然判失败）
            GD.Print($"SELFCHECK: OK ({totalChecks} assertions)");
            return 0;
        }

        GD.PrintErr($"=== 自检失败：{totalChecks} 条断言中有 {failures.Count} 条不通过 ===");
        GD.PrintErr($"SELFCHECK: FAILED ({failures.Count} failures)");
        foreach (var message in failures)
        {
            GD.PrintErr("[FAIL] " + message);
        }

        return 1;
    }

    /// <summary>数一个用例文件里写了多少处断言调用（注释行不算——那条规则照搬 GDScript 版）。</summary>
    private static int CountCheckSites(string source)
    {
        var total = 0;
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith("*", StringComparison.Ordinal)
                || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            total += CheckCallRegex.Matches(line).Count;
        }

        return total;
    }
}
