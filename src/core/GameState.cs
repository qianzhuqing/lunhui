using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 一局游戏的状态快照（C# 版，对位原 `src/core/game_state.gd`，1060 行；拆成五份 `partial`）：
/// 本文件＝常量／字段／开局／队伍与角色；`GameStateSkills.cs`＝加点与武学；
/// `GameStateText.cs`＝等级、坐标与给玩家看的文案；`GameStateStorage.cs`＝存档读写与迁移；
/// `GameStateWorld.cs`＝商店、事件、旗标、首杀、气血、揭开、副本、保底、战斗外增益。
///
/// 只存「以后能重建现场」的最小数据：队伍、难度、章节、时间戳。
/// 存档格式里不放 Resource 引用，全部是 JSON 能表达的类型，方便以后做版本迁移。
///
/// **迁移期的两条跨语言口径**：
///   1. 所有字段 **`[Export] public` 且名字保持 snake_case**——GDScript 侧（20 份用例、
///      几个面板的自检、`tools/check_loop.gd`）直接读写 `state.char_levels[…]` 这种写法，一行都不改。
///   2. 常量 GDScript 读不到（坑⑩），`MAX_PARTY`／`DEFAULT_CHAPTER` 各配一个小写静态取值口；
///      `VERSION` 那条**不能叫 `version()`**（字段 `version` 已占这个名字，同名的静态方法编不过，
///      同坑 65），所以叫 **`save_version()`**——旧调用点 `GameStateScript.VERSION` 要改成它。
/// </summary>
public partial class GameState : RefCounted
{
    /// <summary>
    /// 存档结构版本，格式变了要升这个号并写迁移。
    /// v1 → v2：加入背包与装备（inventory）、加点（char_allocations）与队伍经验池（party_exp）。
    /// v2 → v3：加入武学熟练度（skill_mastery）。
    /// v3 → v4：加入已学武学（char_learned）与装配（char_loadouts）。
    /// v4 → v5：装备实例带上随机词条（inventory 里的 affixes，缺省为空）。
    /// v5 → v6：加入商店交易状态（shop_state：累计买入次数与回购列表）。
    /// v6 → v7：加入副本完成度记录（dungeon_records）与抽卡保底计数（pity）。
    /// v7 → v8：加入大地图揭开记录（revealed_nodes，走出来的地图要留着）。
    /// v8 → v9：加入事件判定记录（event_checks）与剧情旗标（flags）。
    /// v9 → v10：加入首杀掉落记录（first_kills）。首杀奖励是「确定的惊喜」，只能领一次，
    ///           所以必须进存档——留在会话里会让玩家退出重进刷首杀。老档＝还没打过首杀。
    /// v10 → v11：加入战斗外气血（char_hp）。05 文档的医馆治疗要按「缺失气血」收费，
    ///            没有这份记录就只能算价钱、治不了。老档＝全队满血。
    /// v11 → v12：加入战斗外增益（field_buffs）。08 的 `scope=field` 是**按现实时间**计时的，
    ///            只留在会话里的话「退出重进」就等于白打坐；存的是到期时间戳（unix 秒）。
    /// v12 → v13：加入大地图坐标（world_pos）。设计 09 §二：读档一律回大地图，落点用这个坐标；
    ///            小地图／副本内的位置**故意不存**（那部分进度已各自持久化）。
    /// v13 → v14：加入创建角色（0.16.0／0.17.0）的两份记录——`talent_picks` 与
    ///            `custom_templates`（后者必须进存档：自建角色不在任何 CSV 里）。
    /// v14 → v15：加入 NPC 好感度（`npc_favor`）。设计 19 的赠礼／切磋／偷窃／兑换都会改它。
    /// </summary>
    public const int VERSION = 15;

    /// <summary>
    /// 开局队伍人数上限。
    /// 设计上队伍规模是 3–4 人（docs/design/00_总览.md），0.4.0 起 character_base 只有一个
    /// 角色模板，实际队伍先按表里的行数来；上限留着等模板扩充。
    /// </summary>
    public const int MAX_PARTY = 4;

    public const string DEFAULT_CHAPTER = "chapter_01";

    /// <summary>过渡取值口：GDScript 读不到 C# 的 `const`（坑⑩）。**不叫 `version()`**——字段占了那个名字。</summary>
    public static int save_version() => VERSION;

    /// <summary>过渡取值口（`tests/test_recruit.gd` 读队伍上限）。</summary>
    public static int max_party() => MAX_PARTY;

    /// <summary>过渡取值口（`chapter_label` 的兜底口径）。</summary>
    public static string default_chapter() => DEFAULT_CHAPTER;

    /// <summary>
    /// 字段一律写成**自动属性**（`{ get; set; }`）：`tests/test_handshake_code.gd` 的存档字段门限
    /// 按「`[Export] public 类型 名字 {`」认字段（`Inventory.cs`／`TableDb.cs` 同一形状），
    /// 写成裸字段 `public int x = 0;` 它一个都扫不到——那条门限是"字段必须进 to_dict()"的兜底。
    /// </summary>
    [Export]
    public int version { get; set; } = VERSION;

    [Export]
    public Godot.Collections.Array char_ids { get; set; } = new();

    /// <summary>char_id → 等级。</summary>
    [Export]
    public Godot.Collections.Dictionary char_levels { get; set; } = new();

    [Export]
    public string difficulty_id { get; set; } = "normal";

    [Export]
    public string chapter_id { get; set; } = DEFAULT_CHAPTER;

    /// <summary>所在存档槽（0 表示还没落盘）。</summary>
    [Export]
    public int slot { get; set; }

    [Export]
    public int created_unix { get; set; }

    [Export]
    public int saved_unix { get; set; }

    /// <summary>背包与装备（`Inventory`，已是 C#）。GDScript 侧直接读 `state.inventory.money`。</summary>
    [Export]
    public GodotObject inventory { get; set; }

    /// <summary>char_id → {attr_id: 点数}。</summary>
    [Export]
    public Godot.Collections.Dictionary char_allocations { get; set; } = new();

    /// <summary>char_id → [talent_id]（v14 起）。</summary>
    [Export]
    public Godot.Collections.Dictionary talent_picks { get; set; } = new();

    /// <summary>char_id → 自建角色模板（v14 起；读档时重新注入 db）。</summary>
    [Export]
    public Godot.Collections.Dictionary custom_templates { get; set; } = new();

    /// <summary>npc_id → 当前好感（v15 起）。</summary>
    [Export]
    public Godot.Collections.Dictionary npc_favor { get; set; } = new();

    [Export]
    public int party_exp { get; set; }

    /// <summary>char_id → {skill_id: 熟练度}。</summary>
    [Export]
    public Godot.Collections.Dictionary skill_mastery { get; set; } = new();

    /// <summary>char_id → [skill_id]。</summary>
    [Export]
    public Godot.Collections.Dictionary char_learned { get; set; } = new();

    /// <summary>char_id → {active: [], passive: []}。</summary>
    [Export]
    public Godot.Collections.Dictionary char_loadouts { get; set; } = new();

    /// <summary>building_id → {purchased: {item_id: n}, buyback: []}。</summary>
    [Export]
    public Godot.Collections.Dictionary shop_state { get; set; } = new();

    /// <summary>scene_id → {chests/triggers/rooms/rooms_entered/bosses: [key]}。</summary>
    [Export]
    public Godot.Collections.Dictionary dungeon_records { get; set; } = new();

    /// <summary>保底计数器（`PityTracker.to_dict()`）。</summary>
    [Export]
    public Godot.Collections.Dictionary pity { get; set; } = new();

    /// <summary>已揭开的大地图节点。</summary>
    [Export]
    public Godot.Collections.Array revealed_nodes { get; set; } = new();

    /// <summary>check_id → done／failed。</summary>
    [Export]
    public Godot.Collections.Dictionary event_checks { get; set; } = new();

    /// <summary>flag_id → bool。</summary>
    [Export]
    public Godot.Collections.Dictionary flags { get; set; } = new();

    /// <summary>drop_group → true（首杀记录）。</summary>
    [Export]
    public Godot.Collections.Dictionary first_kills { get; set; } = new();

    /// <summary>char_id → 战斗外剩余气血（**没有记录＝满血**）。</summary>
    [Export]
    public Godot.Collections.Dictionary char_hp { get; set; } = new();

    /// <summary>战斗外增益：每条 {buff_id, source_id, stacks, expires_at}。</summary>
    [Export]
    public Godot.Collections.Array field_buffs { get; set; } = new();

    /// <summary>存档里的大地图坐标 {x, y}（v13 起；空 = 用默认出生点）。</summary>
    [Export]
    public Godot.Collections.Dictionary world_pos { get; set; } = new();

    /// <summary>运行时字段：读档时版本比当前低就从哪个版本升上来（0 表示没迁移），不写进存档。</summary>
    [Export]
    public int migrated_from { get; set; }

    /// <summary>运行时字段：读档时剔掉了几个「表里已经下架」的角色（0 表示没有），不写进存档。</summary>
    [Export]
    public int pruned_characters { get; set; }

    /// <summary>运行时字段：读档时按当前 `equip_slot_def` 退回了多少件「穿不上」的装备（0 表示没有），不写进存档。</summary>
    [Export]
    public int reclaimed_equipped { get; set; }

    /// <summary>背包的强类型视图（字段本身是跨语言口，收 `GodotObject`）。</summary>
    private Inventory Inv => inventory as Inventory;

    /// <summary>
    /// 新开一局：队伍取 `recruit_def` 里 `is_initial=1` 的角色（0.10.0 起开局只有书生一人），
    /// 等级取各自 `character_base.start_level`。
    /// **四个参数一个都不能省**（C# 的默认参数对 GDScript 不生效，坑⑲）。
    /// </summary>
    public static GameState new_game(TableDb db, string difficulty_id, string[] char_ids, int slot)
    {
        var state = new GameState();
        state.difficulty_id = difficulty_id;
        state.slot = slot;
        int now = (int)Time.GetUnixTimeFromSystem();
        state.created_unix = now;
        state.saved_unix = now;
        string[] ids = char_ids;
        if (ids == null || ids.Length == 0)
        {
            ids = default_party_ids_from_recruit(db);
        }

        foreach (string char_id in ids)
        {
            Resource row = db.get_row("character_base", char_id);
            if (row == null)
            {
                GD.PushError($"[GameState] character_base 缺少角色 {char_id}");
                continue;
            }

            state.char_ids.Add(char_id);
            state.char_levels[char_id] = Mathf.Max(1, row.Get("start_level").AsInt32());
        }

        state.inventory = new Inventory();
        state.seed_starting_equipment(db);
        state.seed_starting_skills(db);
        return state;
    }

    /// <summary>按角色模板的 start_equip_ids 建出实例并穿上（新建游戏与 v1 老档迁移共用）。</summary>
    public void seed_starting_equipment(TableDb db)
    {
        inventory ??= new Inventory();
        foreach (string char_id in _strings(char_ids))
        {
            seed_character_equipment(db, char_id);
        }
    }

    /// <summary>
    /// 只给**一位**角色发起始装备并穿上（`seed_starting_equipment` 与剧情招募共用一处实现）。
    /// 招募时必须走这个单人口：多人口会把已经在队里的角色**再发一遍**装备。
    /// </summary>
    public void seed_character_equipment(TableDb db, string char_id)
    {
        inventory ??= new Inventory();
        var row = db.get_row("character_base", char_id) as CharacterBaseRow;
        if (row == null)
        {
            return;
        }

        foreach (string equip_id in row.equip_ids())
        {
            string instance_id = Inv.add_equipment(db, equip_id, new Godot.Collections.Array());
            if (string.IsNullOrEmpty(instance_id))
            {
                continue;
            }

            Godot.Collections.Dictionary equipped = Inv.equip(
                db, char_id, level_of(char_id), row.weapon_type, instance_id);
            if (!equipped["ok"].AsBool())
            {
                GD.PushError($"[GameState] 初始装备穿戴失败：{equipped["error"].AsString()}");
            }
        }
    }

    /// <summary>
    /// 按角色模板的起始武学铺「已学 + 装配」（新建游戏与老档迁移共用）。
    ///
    /// 注意：只认模板 `start_skill_ids`，不把 `skill_base` 里所有 `source_type=start` 的行都发出去——
    /// 设计侧那张角色卡写死了书生的「起始武学＝玄微剑法·起手」（玄微心法·引气与连环腿虽然标着
    /// 开局自带／可选，但没进模板），这条差异记在对接表的 skill_system 备注里等设计确认。
    /// </summary>
    public void seed_starting_skills(TableDb db)
    {
        foreach (string char_id in _strings(char_ids))
        {
            seed_character_skills(db, char_id);
        }
    }

    /// <summary>只给**一位**角色发「起始武学 + 装配」（与上面同理：招募走单人口，别重发）。</summary>
    public void seed_character_skills(TableDb db, string char_id)
    {
        var row = db.get_row("character_base", char_id) as CharacterBaseRow;
        if (row == null)
        {
            return;
        }

        foreach (string skill_id in row.skill_ids())
        {
            if (db.get_row("skill_base", skill_id) == null)
            {
                GD.PushError($"[GameState] 起始武学不存在：{skill_id}");
                continue;
            }

            learn_skill(char_id, skill_id);
        }

        SkillLoadout.create(db, this, char_id).fill_from_learned();
    }

    /// <summary>
    /// 剧情招募（设计 09 §3.2）：把**一位**角色加进队伍。
    ///
    /// 只处理这一位：等级取模板 `start_level`，按模板发起始装备与起始武学并铺好装配
    /// （09 的「待确认」里定了「入队即送装备」；要改成不送，把模板的 `start_equip_ids` 清空即可）。
    /// 已在队里直接返回 `{ok: false}`，**不重复发装备**——重复调用（每次按 E 都会查一遍）是常态。
    /// </summary>
    public Godot.Collections.Dictionary add_character(TableDb db, string char_id)
    {
        if (char_ids.Contains(char_id))
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", "已经在队伍里" }, { "char_id", char_id },
            };
        }

        Resource row = db.get_row("character_base", char_id);
        if (row == null)
        {
            GD.PushError($"[GameState] 招募失败：character_base 里没有角色 {char_id}");
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", "这个角色不在配置里（数据错，已记进日志）" }, { "char_id", char_id },
            };
        }

        char_ids.Add(char_id);
        char_levels[char_id] = Mathf.Max(1, row.Get("start_level").AsInt32());
        inventory ??= new Inventory();
        seed_character_equipment(db, char_id);
        seed_character_skills(db, char_id);
        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" }, { "char_id", char_id } };
    }

    /// <summary>
    /// 读档时剔掉 `character_base` 里已经下架的角色。
    ///
    /// 角色模板改过几轮（早期是 ch_gang／ch_du／ch_ci／ch_qi 四个），老档里留着这些 id
    /// 只会让队伍变空——**空队伍会让每场战斗瞬间判负**，玩家还看不出为什么。
    /// 等级取被剔角色的最高值转给接手的新角色，身上的装备退回背包（实例本身不删）。
    /// </summary>
    private void _prune_unknown_characters(TableDb db)
    {
        if (db == null || char_ids.Count == 0)
        {
            return;
        }

        var kept = new Godot.Collections.Array();
        var dropped = new Godot.Collections.Array();
        int best_level = 0;
        foreach (string char_id in _strings(char_ids))
        {
            best_level = Mathf.Max(best_level, char_levels.ContainsKey(char_id) ? char_levels[char_id].AsInt32() : 1);
            if (db.get_row("character_base", char_id) != null)
            {
                kept.Add(char_id);
            }
            else
            {
                dropped.Add(char_id);
            }
        }

        if (dropped.Count == 0)
        {
            return;
        }

        foreach (string char_id in _strings(dropped))
        {
            _strip_character(char_id);
        }

        char_ids = kept;
        pruned_characters = dropped.Count;
        if (char_ids.Count == 0)
        {
            Godot.Collections.Array rows = db.rows("character_base");
            if (rows.Count == 0)
            {
                return;
            }

            GodotObject row = rows[0].AsGodotObject();
            string new_id = row.Get("char_id").AsString();
            char_ids.Add(new_id);
            char_levels[new_id] = Mathf.Max(1, Mathf.Max(best_level, row.Get("start_level").AsInt32()));
            seed_starting_skills(db);
            if (inventory != null)
            {
                seed_starting_equipment(db);
            }
        }
    }

    /// <summary>把一个角色从所有按 char_id 索引的记录里摘掉；他身上穿的东西**退回背包**（实例不删）。</summary>
    private void _strip_character(string char_id)
    {
        Inv?.equipped.Remove(char_id);
        char_levels.Remove(char_id);
        char_allocations.Remove(char_id);
        skill_mastery.Remove(char_id);
        char_learned.Remove(char_id);
        char_loadouts.Remove(char_id);
        char_hp.Remove(char_id);
    }

    /// <summary>把 Variant 数组收成 `string[]`（`char_ids` 这类原来是 PackedStringArray 的字段）。</summary>
    private static string[] _strings(Godot.Collections.Array values)
    {
        var out_values = new string[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            out_values[i] = values[i].AsString();
        }

        return out_values;
    }
}
