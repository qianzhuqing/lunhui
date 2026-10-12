using System;
using System.Collections.Generic;
using Godot;

namespace Lunhui.Tests;

/// <summary>
/// 策划对接守卫（C# 版，对位 `tests/test_handshake.gd`）。
///
/// 把「策划增加或修改需求后，计划与验证必须跟着动」这条规则变成硬门槛：
///   1. `docs/design/CHANGELOG.md` 的当前设计版本，必须与已动工模块的 design_version 一致
///   2. 已动工（进行中／已完成／需返工）的模块必须在 `docs/dev/验证清单.csv` 登记验证用例
///   3. 验证用例文件必须真实存在，且 verified_design_version 必须等于当前设计版本
///   4. `tests/` 下的用例文件（基础设施除外）必须被某个模块引用，不许有孤儿验证
///
/// 红了怎么办：先读 CHANGELOG 看改了什么、评估要不要返工；然后改代码、补或改用例，
/// 最后同步 `docs/dev/模块对接表.csv`（design_version／status）与 `docs/dev/验证清单.csv`。
///
/// **搬法**：常量与共用夹具留在本类（`TestHandshake.cs`／`TestHandshakeConsts.cs`／
/// `TestHandshakeFloors.cs`／`TestHandshakeHelpers.cs` 四份 partial），八族各占一份；
/// 只换语言、不改行为（`_check_*` 的判定逐条对位）。原 `.gd` 里那些大段历史叙事记在
/// `docs/dev/decisions/`，这里按「要点 ＋ 决策编号」压缩，判定依据一条没少。
/// </summary>
public abstract partial class TestHandshake : TestCase
{
    protected const string ChangelogPath = "res://docs/design/CHANGELOG.md";
    protected const string ContractPath = "res://docs/dev/模块对接表.csv";
    protected const string VerifyPath = "res://docs/dev/验证清单.csv";
    protected const string TestsDir = "res://tests";
    protected const string StatusPath = "res://docs/dev/当前状态.md";
    protected const string QuestionsPath = "res://docs/dev/待策划确认.md";
    protected const string ContrastPath = "res://docs/dev/设计实现对照.md";
    protected const string HandoverPath = "res://docs/design/交接单.md";
    protected const string ArtSpecPath = "res://docs/design/15_美术风格需求.md";
    protected const string ArtHandoverPath = "res://docs/design/交接单_美术.md";

    /// <summary>
    /// 需要「计划和验证跟上」的状态。「需返工」故意不在其中：它本来就被标成落后于当前设计版本，
    /// 是待处理状态，不能因为版本对不上就再报一次；但它的验证用例仍然要登记（返工要有验收目标）。
    /// </summary>
    protected static readonly string[] StartedStatuses = { "进行中", "已完成" };

    protected static readonly string[] MentionedStatuses = { "进行中", "已完成", "需返工" };

    /// <summary>
    /// 基础设施用例：不属于某个策划模块，不参与孤儿检查（`TestLayoutBudget.cs` 盯的是
    /// `LayoutBudget` 自己，见决策 189）。
    ///
    /// 2026-10-11（第 48 批）：孤儿检查从「只扫 `.gd`」扩到「只扫 `.cs`」——用例全搬 C# 之后
    /// 若还只扫 `.gd`，这条门限就成了摆设。扩面之后，「不是独立用例」的几种文件
    /// （基类、拆开的 `partial`、探针类）都要在这里登记理由。
    /// </summary>
    protected static readonly string[] InfrastructureTests =
    {
        "TestCase.cs", "TestHandshake.cs", "TestHandshakeFloors.cs", "TestHandshakeHelpers.cs",
        "TestLayoutBudget.cs",
        // 八族（2026-10-04 拆自 test_handshake.gd，2026-10-11 起逐族搬 C#）：各是一道门，
        // 不对应某一个策划模块，所以走基础设施白名单。**没搬过来的族仍列在 .gd 那份白名单里**，
        // 搬一族就把它那一行从那边的 	est_handshake.gd 挪到这儿（两边不重不漏）。
        "TestHandshakeCode.cs", "TestHandshakeCodeGuards.cs", "TestHandshakeContract.cs",
        "TestHandshakeDoccounts.cs", "TestHandshakeDocs.cs", "TestHandshakeDocsTables.cs",
        "TestHandshakeFlags.cs", "TestHandshakeNumbers.cs", "TestHandshakeRepo.cs",
        "TestHandshakeRepoFiles.cs", "TestHandshakeWiring.cs",
        // 骨架自检与「行类」门限：同样不对应某一个策划模块。
        "HarnessSelfTest.cs", "TestRows.cs",
        // 拆开的 `partial`（注册进运行器的是主文件）与探针类——不是独立用例。
        "TestBattleUiCommands.cs", "TestBattleUiSettles.cs", "TestMapAssetsAssets.cs",
        "TestCharacterBagUi.cs",
        "TestSaveMigrationBase.cs", "TestTablePipelineBase.cs",
        "FailingProbe.cs", "FailingSaveStore.cs", "UnwritableSaveStore.cs",
    };

    /// <summary>
    /// 会被「对接守卫」当文本读进来的扩展名。`docs/dev/images/` 下有 PNG 截图，当文本读会刷一屏
    /// 「Unicode parsing error」并把二进制垃圾塞进语料。
    /// **C# 迁移起要带上 `cs`**：不带的话，搬进 `src/` 的 C# 文件对下面一整族门限等于不存在——
    /// 不是"少查一点"，是**门限悄悄失效**（2026-10-10 搬行类时白名单报"找不到定义"就是这个原因）。
    /// </summary>
    protected static readonly string[] TextExtensions =
        { "gd", "cs", "tscn", "tres", "godot", "csv", "md", "json", "bat", "ps1", "txt" };

    /// <summary>
    /// 31. 03 的「隐藏内容」7 行表要与 `hidden_trigger` 对齐（名称与类型都对得上）。
    /// 这张表列的是"第一章七种钥匙"，天然容易和代码/数据脱节——此前只有条数警告（5.20），
    /// **名称与类型没人核**。类型词是中文、枚举在代码里，所以手写这张 7 条映射
    /// （枚举取自 `TableValidator` 的 `trigger_type`）；数量也核：03 说七个层次，数据里就该有 7 行。
    /// </summary>
    protected static readonly Dictionary<string, string> HiddenTypeWords = new()
    {
        { "道具", "item" },
        { "空间", "space" },
        { "顺序", "sequence" },
        { "行为", "behavior" },
        { "击杀方式", "kill_style" },
        { "完整度", "completion" },
        { "携带物", "carry" },
    };

    /// <summary>`to_dict()`／`from_dict()` 双向键检查的一个目标文件。</summary>
    protected sealed class SaveSerializationTarget
    {
        public string Path;
        public string[] Exempt;
        public bool CSharp;
        public string[] Also = Array.Empty<string>();
    }

    /// <summary>
    /// 29. `GameState` 里**声明的每个字段**都必须进 `to_dict()`（或写进白名单并说明理由）。
    /// 由来：存档字段是"加了就得存"的东西，而**忘了加不会报任何错**——那个字段每次读档都静默回到
    /// 默认值（玩家表现是"我明明买了/学了，怎么没了"）。升版本的纪律靠人记，这条门限是机器兜底：
    /// 扫字段声明与 `to_dict()` 里的键，缺一个就红。白名单是**不持久化**的三个"读档整理结果"。
    /// `Inventory` 同样列出（它有自己的一份 to_dict/from_dict，同一个坑）。
    ///
    /// 2026-10-03 补两处：① **反向**：`to_dict()` 写进去的每个键都必须在文件里被读过——
    /// "存了却回不来"是同一个玩家症状的另一半；② 取键的范围收成 **`to_dict()` 的函数体**
    /// （按花括号配平），免得"某个字段其实没写、但名字恰好在后面某个字面量里出现过"蒙混过去。
    /// </summary>
    protected static readonly SaveSerializationTarget[] SaveSerializationTargets =
    {
        new()
        {
            Path = "res://src/core/GameState.cs",
            Exempt = new[] { "migrated_from", "pruned_characters", "reclaimed_equipped" },
            CSharp = true,
            // 2026-10-11：`GameState` 由 `.gd` 搬成 C# 并拆成五份 partial——字段在 `GameState.cs`，
            // `to_dict()`／`from_dict()`／`_read_*` 在 `GameStateStorage.cs`，其余记录进
            // `GameStateWorld.cs`／`GameStateSkills.cs`／`GameStateText.cs`。两边的扫描面都要收
            // （收窄了这条门限会静默退化）。
            Also = new[]
            {
                "res://src/core/GameStateStorage.cs",
                "res://src/core/GameStateWorld.cs",
                "res://src/core/GameStateSkills.cs",
                "res://src/core/GameStateText.cs",
            },
        },
        new()
        {
            Path = "res://src/core/Inventory.cs",
            Exempt = Array.Empty<string>(),
            CSharp = true,
        },
    };

    /// <summary>
    /// 28. 数据字典（06）必须**提到**每张表的每个列。06 的表清单行数与枚举行都有门限，**列**一直没有——
    /// 而它正是策划查"这个字段叫什么、什么意思"的地方（实测漏过 `character_base.weapon_type` 等功能性列，
    /// 见决策 172）。判定**从宽**：只要求列名以 `` `列名` `` 的形式出现在该表那一节里。
    /// 白名单只放**给人看的说明列**（不驱动逻辑、字典不逐列登记）。
    /// </summary>
    protected static readonly string[] DocDictionaryProseColumns =
        { "desc", "note", "name_cn", "icon", "display_color" };

    /// <summary>
    /// 36. 「只打印一个 OK 标记」的验证脚本也要有检查条数下限（删掉几条检查没人会红）。
    ///
    /// 场景自检与两个验收脚本此前一条下限都没有——偏偏它们是「版式预算、真实节点接线、装配真的进战斗、
    /// 跨场景交接、地图位点可达」这类**只有把真场景跑起来才量得到**的东西的唯一覆盖。
    /// `VERIFICATION_CHECK_FLOORS` 记的是**当前条数**（棘轮，不是目标）：删一条就红，加检查就往上调——
    /// 它证明不了"检查有效"，只保证**没人能悄悄把检查删掉**。余量上限同 `MIN_ASSERTIONS`：
    /// 实际条数比棘轮表高出 `VerificationFloorSlack` 就红，提示"加了检查就把 floor 贴上去"。
    /// </summary>
    protected const int VerificationFloorSlack = 2;

    /// <summary>`VerificationCheckFloors` 的一行：模式 ＋ 条数下限 ＋ 理由。</summary>
    protected sealed class CheckFloor
    {
        public string Pattern;
        public int Floor;
        public string Why;
    }

    /// <summary>一条「设计文档声称某张表 N 行」的对账项。</summary>
    protected sealed class RowCountClaim
    {
        public string Path;
        public string Re;
        public (int Index, string Table)[] Pairs;
        public string Why;
    }

    /// <summary>
    /// 58. 设计文档里「某张表 N 行／N 件／N 个」的声称，抽成一张小表逐条对账。
    /// 由来（2026-10-04）：能算的数就现算、绝不手抄；一次抓到四处（15 §4.2 头像数、07 §8.5 装备件数、
    /// 07 §8.2 敌人行数、20 号 §十二 对话行数）——全在地编／美术照着排期的那两份上。
    /// 口径：正则**抓不到也红**（措辞改了要连着改这条门限，别让它悄悄失效）；
    /// **带日期的叙事行不算**——行里出现「当时／以前／当年」就跳过（同 `LooksHistorical`）。
    /// </summary>
    protected static readonly RowCountClaim[] RowCountClaims =
    {
        new()
        {
            Path = "res://docs/design/15_美术风格需求.md",
            Re = "\\|\\s*数量\\s*\\|\\s*(\\d+)\\s*（\\s*`npc_def`",
            Pairs = new[] { (1, "npc_def") },
            Why = "15 §4.2 补「NPC 头像 数量 N（`npc_def`）」",
        },
        new()
        {
            Path = "res://docs/design/07_地图资源需求.md",
            Re = "`equip_base`[^\\n]{0,40}?，\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*件装备",
            Pairs = new[] { (1, "equip_base") },
            Why = "07 §8.5「`equip_base` …N 件装备」",
        },
        new()
        {
            Path = "res://docs/design/07_地图资源需求.md",
            Re = "按\\s*`enemy_base\\.csv`\\s*(\\d+)\\s*行",
            Pairs = new[] { (1, "enemy_base") },
            Why = "07 §8.2 标题「按 `enemy_base.csv` N 行」",
        },
        new()
        {
            Path = "res://docs/design/20_第一章剧情.md",
            Re = "各\\s*\\*{0,2}(\\d+)／(\\d+)\\*{0,2}\\s*行",
            Pairs = new[] { (1, "dialogue_node"), (2, "dialogue_option") },
            Why = "20 号 §十二「`dialogue_node`／`dialogue_option` 各 N／M 行」",
        },
        new()
        {
            Path = "res://docs/design/交接单_美术.md",
            Re = "NPC 头像\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*张",
            Pairs = new[] { (1, "npc_def") },
            Why = "美术交接单第 10 行「NPC 头像 N 张」",
        },
    };

    /// <summary>
    /// 61. 设计文档里点名的「表.列／表.行」必须真在表里——**改列名最容易把这种指针写死**
    /// （0.14.0 拆 `npc_guard.peace_condition` 时，带点号的引用就成了指向不存在的东西的指针）。
    /// 只认 `\b<表名>.<标识符>` 这种**带点号**的写法；`csv` 后缀不算引用；
    /// 计划中的行写进这里，**双向维护**。
    /// </summary>
    protected static readonly string[] PendingDocRefs =
    {
        // 20 号 §九 那张支线表要新增的行（表还没落，见 `待策划确认.md` Q72／Q73）
        "npc_quest.nq_yan_01",
    };

    /// <summary>
    /// 8. `src/` 下的脚本与 `scenes/` 下的场景都必须有人引用（和「孤儿用例」「孤儿设计文档」同一类）：
    /// 路径字符串出现在别处，或它的 `class_name` 在别的文件里被用到。
    /// 真要留一个「暂时没人用」的东西，把它加进本表并写明理由。
    /// </summary>
    protected static readonly string[] InfrastructureScripts = Array.Empty<string>();

    /// <summary>
    /// 8b. 项目**根目录**下的 `.tscn` 也要有主——孤儿扫描只收 `scenes/`，于是**只要有人把场景存到
    /// 项目根，它就能永远躲过检查**（MCP 的 `add_node`＋`save_scene` 默认落点恰好是 `res://node_2d.tscn`）。
    /// 白名单自身也会烂（文件被删了名单还留着），所以顺手断言它仍然存在。
    /// </summary>
    protected static readonly string[] RootSceneWhitelist = { "node_2d.tscn" };

    /// <summary>
    /// 43. 用例里不许出现「没给种子的随机源」：写了无参构造，断言就会**时红时绿**——
    /// 红起来查不出原因，绿起来也不算数。要用固定种子（`RngService.create(seed)`）。
    /// **`RngService` 搬成 C# 之后**无参那一版改叫 `create_random()`，所以名单同时留着新旧两种写法——
    /// 旧 `.new()` 已经调不通了，但留着才不会让这条门限"悄悄少管一种形状"。
    /// </summary>
    protected static readonly string[] UnsafeRngPatterns =
    {
        "RngServiceScript.create_random()", "RngService.create_random()",
        "RngServiceScript.new()", "RngService.new()", "RandomNumberGenerator.new()",
    };

    /// <summary>
    /// 40. 「现状类文档」里不许写死总命令的步数（一边写着"docs 里不再写死步数"，同一段上方却写着
    /// 「当前 16」，而实际已经是 17 步）。`框架说明.md` 不在清单里：它是**带日期的叙事**，
    /// 拿现状口径去量会假红；同理 `平衡观测.md`／`性能观测.md` 是快照报告，也不收。
    /// </summary>
    protected static readonly string[] CurrentStateDocs =
    {
        "AGENTS.md",
        "README.md",
        "docs/dev/当前状态.md",
        "docs/dev/地图搭建说明.md",
    };

    /// <summary>
    /// 14. 仓库级硬约定也要有门限：① `src/` 不许直接读 `res://data/tables/`（CSV 是**构建期输入**）；
    /// ② `tools/*.bat` 必须纯 ASCII ＋ CRLF；③ `tools/*.ps1` 含非 ASCII 就必须带 UTF-8 BOM。
    /// 白名单只有一处：`src/data/TableRegistry.cs` 的 `TABLES_DIR`——构建管线就是靠它去读 CSV 的。
    /// </summary>
    protected static readonly string[] CsvDirWhitelist = { "src/data/TableRegistry.cs" };

    /// <summary>
    /// 15. 06_配置表说明.md 里写的枚举必须与代码/数据对得上（诞生当天就抓到 `event_check.reward_type`
    /// 文档写 7 种、代码只剩 4 种）。比三处：代码 `TableValidator.ENUMS`、主键列的实际取值、
    /// 以及 `equip_base.slot` 对 `equip_slot_def.slot_id`。
    /// 2026-10-03 四处都改完了（06 按代码与数据订正），白名单已清空——这个空字典保留是因为门限要读它的结构。
    /// </summary>
    protected static readonly Dictionary<string, string> DocEnumKnownStale = new();

    /// <summary>「同一事实只许有一处定义」的一条规则。</summary>
    protected sealed class SingleSourceRule
    {
        public string Label;
        public string Owner;
        public string[] Also = Array.Empty<string>();
        public string Why;
    }

    /// <summary>
    /// 16. 「同一事实只许有一处定义」——只收那些**漂移了不会报错、只会静默丢东西**的常量。
    /// `LOCKED_SCENES` 曾在两个文件里各写一份；贡献 kind 字符串（attr_point／stat_flat）原先在 4 个文件里写死，
    /// 而**写错一个字母就会被 `AttributeCalculator` 的 `!=` 判断静默跳过**——面板上少一截加成，没有任何报错。
    /// 故意**不**收场景路径常量：路径写错会当场加载失败，是响的。
    /// </summary>
    protected static readonly SingleSourceRule[] SingleSourceRules =
    {
        new()
        {
            // 2026-10-11（第三十一批）：这份 `.gd` 搬成 `WorldMapService.cs` 之后，GDScript 的
            // `const LOCKED_SCENES := [...]` 那行没了——量的是**C# 里那行声明本身**
            // （标签换了、量的东西没变：改了列表这条就红，正好逼着回来改这里）。
            Label = "LOCKED_SCENES = { \"scene_ferry_locked\" }",
            Owner = "src/core/WorldMapService.cs",
            Why = "哪些小地图本章不开放：大地图控制器必须引用它，不能各写一份（漂移了不会报错）",
        },
        new()
        {
            Label = "\"attr_point\"",
            Owner = "src/core/AttributeCalculator.cs",
            // 两处：词表（ENUMS 里的 affix_pool.value_kind）＋规则里的 `value_kind != "attr_point"` 判断
            Also = new[] { "src/core/TableValidator.cs", "src/core/TableValidatorActors.cs" },
            Why = "贡献 kind 写错会被计算器静默跳过；table_validator 那处是 affix_pool.value_kind 的枚举（另一个轴）",
        },
        new()
        {
            Label = "\"stat_flat\"",
            Owner = "src/core/AttributeCalculator.cs",
            Why = "同上：贡献 kind 写错会被静默跳过",
        },
    };

    /// <summary>
    /// 20. 「写了但游戏路径从不调用」的接口清单：钉成门限，只许少不许悄悄多。
    /// 由来：图鉴奖励就是栽在这里的（`codex_bonus()` 早写好了，全项目只有用例在调，收集再多也不涨属性）。
    /// 判定：函数名在 `src/`＋`scenes/`＋`project.godot` 里出现 ≤1 次（只有定义那行）＝游戏路径从不调用；
    /// 出现在白名单里才算通过。`_` 开头的一律跳过。
    /// </summary>
    protected static readonly Dictionary<string, string> TestOnlyApiAllowed = new()
    {
        // —— 0.7.0 数据层：buff／套装的服务类先落地并有用例钉着，**结算与六指令随后接** ——
        { "polarity_of", "0.7.0 buff 数据层：结算未接（见 08 与决策 210）" },
        { "is_field_buff", "0.7.0 buff 数据层：战斗外增益的 HUD 显示未接" },
        { "grants_of_buff", "0.7.0 buff 数据层：发放时机未接" },
        // —— 查询接口：游戏逻辑不需要，测试/界面按需读 ——
        // `has_state` 2026-10-11 随 `src/autoload/GameSession.cs` 搬来 C#，**这条留着不动**：
        // 这道门的 C# 正则认得出 `public bool has_state()`（只认不出 `public static`），删了当场红（实测）。
        { "has_state", "查询" },
        { "has_status", "查询" },
        { "hp_ratio", "查询" },
        { "kill_style_of", "查询" },
        { "checks_for_region", "查询" },
        { "shop_state_of", "查询" },
        { "has_first_kill", "查询" },
        { "first_kill_count", "查询" },
        { "get_seed", "查询" },
        { "has_table", "查询" },
        // 2026-10-11：`get_id` 随行类全量搬完被删（行基类 `TableRow.cs` 的主键是属性 `id`），
        // 这条按老规矩删掉。**P5 若给 `TableRow.cs` 补 `get_id()` 取值口，要把这条加回来**。
        { "has_value", "查询" },
        { "has_cap", "查询" },
        { "has_service", "查询" },
        { "buff_stacks", "查询" },
        { "buff_remaining", "查询" },
        { "exits", "查询" },
        { "is_unlimited", "查询" },
        { "focus_attrs", "查询" },
        { "round_text", "查询" },
        { "auto_running", "查询" },
        { "slot_button", "查询" },
        { "tier_id", "查询" },
        { "body_texture_path", "查询" },
        { "brazier_texture_path", "查询：用例验火盆用的是地编交付的那两张「灭／燃」贴图（07 §8.3）" },
        // 2026-10-11（P5）：C# 正则补上了 `public static`，下面这几条**当年因为「定义看不见」被删掉的
        // 白名单**全部加回来（那几处注释里都写着「P5 补正则时加回来」，这里就是那一步）；
        // 同一批还多出三条以前从没进过白名单、但同样「只有测试在调」的静态口。
        { "is_defined", "查询：用例验输入映射里注册了哪些动作（`InputSetup`）" },
        { "skills_from_enemy", "便利查询：游戏路径走 `grant_from_source(db, state, \"drop\", …)`，这条只给用例" },
        { "played_events", "音效自检读它：这一小段里放过哪些事件（真玩的时候没人读）" },
        { "has_played", "同上（音效自检的查询口）" },
        { "clear_log", "同上（音效自检每段前清一次）" },
        { "shutdown", "自检收尾把播放层从 root 上摘掉（`tools/RunTestsCs.cs` 在调；`tools/` 不在语料里）" },
        { "column_of", "构建工具 `tools/BuildTables.cs` 在调（`tools/` 不在语料里，所以语料里只算 1 次）" },
        { "default_dir_name", "过渡期取值口：用例读 `DEFAULT_DIR`（C# 的 `const` 跨语言读不到）" },
        { "parry_damage_mult_default", "绝对值断言的取值口（变异探针那条「补一条绝对值断言」的落点）" },
        { "node_icon", "查询" },
        { "highlight_visible", "查询" },
        { "map_progress_text", "查询" },
        { "fog_cells", "查询" },
        { "facing_quadrant", "查询" },
        { "is_sneaking", "查询" },
        { "has_elite_glow", "查询" },
        { "team_drop_groups", "查询" },
        { "threat_tags", "查询" },
        { "resolve_db", "查询" },
        // —— 等设计补效果列/流程：接口留着，游戏里还没有调用方 ——
        { "is_field_use", "等 item_base 效果列（05：两种回血道具要分开，使用未做）" },
        { "is_battle_use", "同上" },
        { "clear_dispellable", "等「驱散」来源（设计只标了可驱散，没给驱散技能）" },
        { "set_pity", "保底计数由存档读写，游戏路径不需要外部塞 —— 留给测试" },
        { "roll_group_many", "多组连掉的便利接口，游戏路径逐组调 roll_group" },
        { "set_strategy", "界面只用轮换（cycle_strategy）；直接设值留给测试" },
        // —— 存档/工具接口：设计或流程上还没有调用方 ——
        { "delete_slot", "设计文档里没有「删除存档」这一条" },
        { "has_any_save", "菜单逐个槽位检查，没用到这个汇总查询" },
        // —— 2026-10-11（P5）：正则补上 `public static` 之后浮出来的**过渡期取值口**（56 条） ——
        // 这批在原 GDScript 链里本来就是可见的（`static func` 当年扫得到），是「GDScript 读不到
        // C# 的 `const`／枚举」才包出来的静态口；用例靠它们钉绝对值（也是变异探针「补一条绝对值断言」
        // 的落点）。**GDScript 清零后这层包装已经没必要了**——C# 用例可以直接读 `public const`，
        // 哪天顺手收掉时就连同这里一起删（门限的反向检查会提醒：接口没了、白名单还在就红）。
        // 战斗常量
        { "flee_base_chance", "取值口：逃跑基础成功率（原 const；用例钉绝对值）" },
        { "flee_min_chance", "取值口：逃跑成功率下限" },
        { "flee_max_chance", "取值口：逃跑成功率上限" },
        { "poise_break_damage_bonus", "取值口：破绽窗口增伤" },
        { "basic_attack_id", "取值口：普攻那招的 id" },
        { "basic_attack_power", "取值口：普攻倍率" },
        { "basic_attack_poise", "取值口：普攻破架势值" },
        { "defend_buff_id", "取值口：主动防御挂的那个 buff id" },
        { "strategy_balanced", "取值口：自动战斗策略枚举" },
        { "strategy_conservative", "取值口：同上" },
        { "strategy_order", "取值口：三档策略的轮换顺序" },
        { "default_crit_dmg", "取值口：暴击伤害兜底值（表里没有时的兜底）" },
        { "default_volume", "取值口：默认音量（设置读坏时的兜底）" },
        { "default_auto_battle", "取值口：默认自动战斗开关" },
        { "implemented_sources", "取值口：已实现的武学来源清单（用例按设计逐条核对）" },
        // buff／套装枚举
        { "polarity_buff", "取值口：buff 极性枚举（增益）" },
        { "polarity_debuff", "取值口：buff 极性枚举（减益）" },
        { "scope_battle", "取值口：作用域枚举（战斗内）" },
        { "scope_field", "取值口：作用域枚举（战斗外）" },
        { "stack_refresh", "取值口：叠加规则枚举（刷新）" },
        { "duration_permanent", "取值口：持续枚举（常驻）" },
        { "duration_until_battle_end", "取值口：持续枚举（整场）" },
        { "effect_kind_stat", "取值口：效果类别枚举（改数值）" },
        { "kind_active", "取值口：套装类别枚举（招式套）" },
        { "kind_passive", "取值口：套装类别枚举（内功套）" },
        // 引导／菜单／存档／商店
        { "start_condition", "取值口：引导入口条件（原 const）" },
        { "origin_prefix", "取值口：出身类引导的 id 前缀" },
        { "item_prefix", "取值口：物品类引导的 id 前缀" },
        { "supplies_flag", "取值口：补给旗标名" },
        { "supplies_level", "取值口：补给门槛等级" },
        { "arrived_heifengzhai_flag", "取值口：到黑风寨那个旗标名" },
        { "heifengzhai_scene", "取值口：黑风寨场景 id" },
        { "start_hub", "取值口：新建游戏落到哪个场景" },
        { "default_chapter", "取值口：默认章节 id" },
        { "custom_char_id", "取值口：自建角色的 id" },
        { "buyback_limit", "取值口：回购列表上限" },
        // 界面版式／结算
        { "auto_interval", "取值口：自动战斗推进间隔" },
        { "selftest_seed", "取值口：战斗自检固定种子（决策 236）" },
        { "settle_drop_items", "取值口：结算面板的掉落块最多摆几格（两列往外铺）" },
        { "team_win_flags", "取值口：队伍胜利旗标表（用例钉三支队伍的心结旗标）" },
        { "backdrop_min_alpha", "取值口：背板最低不透明度" },
        // 世界层交互距离
        { "portal_distance", "取值口：门户交互半径" },
        { "portal_arm_margin", "取值口：出图武装边距（决策 48）" },
        { "discover_distance", "取值口：地标揭开距离" },
        { "highlight_distance", "取值口：地标高亮半径（与区域招募同值）" },
        { "region_join_distance", "取值口：区域招募判定半径" },
        { "walk_speed", "取值口：走路速度" },
        { "sneak_ratio", "取值口：潜行移速比" },
        { "threat_colors", "取值口：明雷威胁色表（用例按设计 02 核对）" },
        // 数据层与音效
        { "row_scripts", "取值口：行类脚本表（构建工具与用例读；`tools/` 不在语料里）" },
        { "tables_dir", "取值口：CSV 目录常量" },
        { "output_dir", "取值口：生成物目录常量" },
        { "sound_bus", "取值口：音量总线名（设置页与用例读）" },
    };

    /// <summary>
    /// 65. 「枚举三处同改」的第三处（`tools/validate_tables.ps1`）也得能看见——`ENUMS` 里每个枚举列，
    /// PS1 至少要提到那一列（此前 41 列里有 7 列整个 PS1 一次都没出现过，等于设计侧那道网根本不查它们）。
    /// 口径：**只要求列名在 PS1 里出现**。这是条**名字级棘轮**，不是证明——新加枚举列时，
    /// **要么给 PS1 补一条检查，要么写进本表并说明为什么这边不查**。
    /// </summary>
    protected static readonly Dictionary<string, string> Ps1EnumExempt = new()
    {
        // 今天没有豁免。示例：某列只有引擎 API 能算（PS1 是纯文本校验、没有引擎），就写进这里并说明。
    };

    /// <summary>
    /// 「被要求的旗标」必须有人能点亮——把 Q51 那一族断链变成机器门限。
    /// 由来：落雁坡 `flag_luoyanpo_met`／毒堂 `flag_poison_hall`／荒村 `flag_huangcun_done`／
    /// 救囚徒 `flag_qiutu_saved` 都一度**一个来源都没有**，于是同伴永远入不了队、委托永远交不了。
    /// 这类断链**表里看不出来**：要求写在一处、来源在另一处或根本不存在，两边单独看都是合法的。
    /// 要求来自七处（recruit_def.join_condition／guide_step.condition／story_node.trigger_condition／
    /// chapter_def.complete_condition／dialogue_node.condition／dialogue_option.condition／
    /// npc_quest.requirement）；来源三类：表里、代码字面量、以及 `flag_%s_joined` 那种**模式**匹配。
    /// **已知缺口**写在下面：补上就要把那行删掉，否则门限会红（缺口清单过期要有人管）。
    /// </summary>
    protected static readonly string[] KnownFlagGaps =
    {
        // 伪装混入（号衣＋腰牌）整套还没做——设计侧的 Q7 还开着
        "flag_bd_uniform",
    };

    /// <summary>
    /// 20 号 §九 的十条支线表 ↔ npc_quest：**好感那一列必须对得上**（它是那张表里唯一能被机器读的数值；
    /// 「奖励」那列是散文，机器比不了）。2026-10-04 第一次跑就抓到两处对不上（陈氏 20→25、孙掌柜 10→20），
    /// 都按**设计那张表**改了数据。
    /// 例外写在下面：**张贵**按设计是「图只可偷」（走 of_zhang_treasure），本来就没有 npc_quest 行；
    /// 招募那四条归 recruit_def，也不在这张表里。
    /// </summary>
    protected static readonly string[] SidequestForceSkip = { "张贵", "燕小七", "林铁山", "白清和", "苏九娘" };

    /// <summary>
    /// 51. 套装的**每一档**必须真的凑得出来——按「已经发得出来的来源」算（5.19 只查"单件有没有来源"，
    /// 查不出**组合**）：`set_xuanwei_sword` 的三招／五招要 3／5 部玄微剑法，而 02／03／04 三部都是
    /// `source_type=npc`（门派对话没实现），拿得到的只有起手与点星＝2 部；
    /// `set_xuanwei_qi` 的七格要 7 点占格，拿得到的只占 6 格。
    /// 口径与 `validate_tables.ps1` 5.19b 是**同一条规则**（改一处要改两处）；已知缺口写在下面
    /// （**双向**维护：补上了就把那行删掉，否则门限会红）。
    /// </summary>
    protected static readonly string[] KnownUnreachableSetTiers =
    {
        // 三招／五招：02／03／04 三部都是 source_type=npc（`待策划确认.md` Q3）
        "set_xuanwei_sword|3",
        "set_xuanwei_sword|5",
        // 七格：拿得到的只有引气（1 格，开局）＋周天（2 格，剧情）＋太清（3 格，剧情）＝6 格
        "set_xuanwei_qi|7",
    };

    /// <summary>
    /// 53. **划掉的条目（`~~…~~`）正文里不许再留下「还没做」的字样**（决策 338：A3 那行标题划掉了、
    /// 正文还写着「仍是程序化光晕，换图属开发侧接线」——读它的人会把一件做完的事当成待办）。
    /// 口径：行内有划线的先把划线片段摘掉再查「待办字样」——旧措辞**划掉就放过**（那是明确撤回），
    /// 留在正文里才算矛盾。词表写死在这里。
    /// </summary>
    protected static readonly string[] StruckPendingWords =
        { "开发侧接线", "要开发侧", "需要时提一句", "还没接", "未接线", "仍是程序化" };

    /// <summary>
    /// 66. 单个脚本不许长成上帝类：**硬上限 800 行**（2026-10-04 用户拍板）。
    /// **白名单就是 `docs/dev/代码拆分清单.md` 本身**，不在代码里另抄一份：超限的文件必须能在清单
    /// 「现状」表里找到一行，登记了却已经拆到 800 行以下的也要红（提醒划掉那一行）。
    /// 行数口径与清单里量行数的那条命令一致（`Get-Content .Count`：**末尾换行不算一行**）。
    /// </summary>
    protected const int FileSizeLimit = 800;

    protected const string FileSizeListPath = "res://docs/dev/代码拆分清单.md";
}
