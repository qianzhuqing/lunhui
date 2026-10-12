using Godot;
using Lunhui.Audio;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的结算（胜／败／撤／平局）——从 `BattleScreen.cs` 拆出来的 partial
/// （那份要守 800 行硬上限）。
///
/// 第八版起结算是一块**整屏分区面板**（六块的摆法在 `BattleScreenSettlePanel.cs`）：这里把战果算清
/// （`lines` 是**战报**、全量、顺序即优先级），并组装面板要的那份结构化内容；
/// 落账一律走 `BattleReward.apply()` 那条唯一口径，这里只负责把结果写成人话。
/// </summary>
public partial class BattleScreen
{
    public void _settle()
    {
        if (settled)
        {
            return;
        }

        settled = true;
        auto_enabled = false;
        string winner = sim.Call("winner").AsString();
        // 结算音：胜／败各一条（07 §8.4）。逃跑与平局的音效位设计没给，先不出声。
        if (winner == _winner_ally())
        {
            Sfx.play("victory");
        }
        else if (winner == _winner_enemy())
        {
            Sfx.play("defeat");
        }

        // 战斗结束清掉**战斗内** buff（设计 08：`battle` 作用域随战斗结束清除；`field` 战斗外增益
        // 由会话层按现实分钟管，不在这一步里动）。放在结算最前面：掉落与经验不受残留 buff 影响。
        foreach (Godot.Collections.Array group in new[] { allies, enemies })
        {
            foreach (Variant actor in group)
            {
                actor.AsGodotObject()?.Call("clear_battle_buffs");
            }
        }

        Node session_node = _session_node();
        var lines = new Godot.Collections.Array();
        // 面板上的另外两块（说明／武学）与收获四块分开走：`applied` 直接喂给面板（口径③：零收获时不建）。
        var notes = new Godot.Collections.Array();
        var martial = new Godot.Collections.Array();
        Godot.Collections.Dictionary applied = new();
        if (winner == _winner_ally())
        {
            var drops = new Godot.Collections.Array();
            // 保底计数从存档读、打完写回（设计：保底跨战斗、跨难度继承）
            PityTracker pity = state != null
                ? state.Call("pity_tracker").As<PityTracker>()
                : PityTracker.create();
            DropResolver resolver = DropResolver.create(Db, RngService.create(rng_seed), pity);
            double bonus = DropResolver.party_drop_bonus(allies);
            foreach (Variant enemy_variant in enemies)
            {
                GodotObject enemy = enemy_variant.AsGodotObject();
                if (enemy.Call("is_alive").AsBool())
                {
                    continue;
                }

                // 练习战（09 §3.3）：**掉落、首杀一律不算**——它的收益只有封顶的经验与熟练度，
                // 否则「打木桩刷装备/刷首杀」就把上山的意义抹掉了。
                if (Enc.practice)
                {
                    continue;
                }

                // 首杀记录进存档（GameState.first_kills）：同一掉落组只领一次，
                // 退出重进也不会再出首杀固定掉落。
                string drop_group = enemy.Get("drop_group").AsString();
                bool first_kill = false;
                if (state != null && drop_group.Length > 0)
                {
                    first_kill = state.Call("mark_first_kill", drop_group).AsBool();
                }

                if (drop_group.Length > 0)
                {
                    drops.AddRange(resolver.roll_group(
                        drop_group,
                        Enc.difficulty_id,
                        new Godot.Collections.Dictionary
                        {
                            { "first_kill", first_kill }, { "drop_rate_bonus", bonus },
                        }));
                }

                // 装备掉落：**身上穿的，就是能掉的**（设计 10 §五；0.14.0 起 `drop_table` 不再列装备）。
                // 首杀那一下由 `roll_enemy_equipment` 保一件（武器优先）。
                string enemy_id = enemy.Get("source_id").AsString();
                if (enemy_id.Length == 0)
                {
                    enemy_id = enemy.Get("actor_id").AsString().Split('#')[0];
                }

                drops.AddRange(resolver.roll_enemy_equipment(enemy_id, first_kill));
            }

            // 剧情物（20 号 §七 的账册）：**第一次**打赢这支队伍才进掉落清单，之后不再重复发。
            string story_item = _team_win_item_id();
            if (story_item.Length > 0 && !_owns_item(story_item))
            {
                drops.Add(new Godot.Collections.Dictionary { { "item_id", story_item }, { "qty", 1 } });
            }

            Godot.Collections.Dictionary result = sim.Call("result").AsGodotDictionary();
            if (Enc.practice)
            {
                // 经验封顶（`growth_const.dummy_xp_cap_level`）＋没有铜钱：改在结算前就地削，
                // 这样 `BattleReward` 那条唯一入账口径不用为练习战开分支。
                Godot.Collections.Dictionary rewards = result.ContainsKey("rewards")
                    ? result["rewards"].AsGodotDictionary()
                    : new Godot.Collections.Dictionary();
                result["rewards"] = new Godot.Collections.Dictionary
                {
                    { "exp", PracticeService.capped_exp(Db, state, _i(rewards, "exp")) }, { "money", 0 },
                };
            }

            applied = BattleReward.apply(
                Db, state, result, drops, AffixRoller.create(Db, RngService.create(rng_seed)));
            if (state != null)
            {
                state.Call("store_pity", pity);
            }

            // 战报照旧记全量（优先级：战果 → 掉落 → 升级 → 领悟／未习得 → 熟练度 → 暂缓规则）。
            // 面板那边把经验与铜钱拆成两块（第八版），这一行不动——它是 `last_battle.summary` 的来源。
            lines.Add($"胜利！经验 +{_i(applied, "exp")}　铜钱 +{_i(applied, "money")}");
            string drops_detail = _describe_drops(applied);
            lines.Add($"掉落：{(drops_detail.Length > 0 ? drops_detail : "无")}");

            if (_s(applied, "level_up_text").Length > 0)
            {
                lines.Add($"升级：{_s(applied, "level_up_text")}");
            }

            // 切磋（设计 19 §2.2）：赢了给这位 NPC 加好感。
            // 只在这一条路上加——切磋是「主动选的战斗」，输了不扣好感（19 §2.2 只写了赢的收益）。
            if (Enc.spar_npc.Length > 0)
            {
                Godot.Collections.Dictionary spar = NpcService.win_spar(Db, state, Enc.spar_npc);
                if (_b(spar, "ok"))
                {
                    lines.Add(_s(spar, "text"));
                    notes.Add(_s(spar, "text"));
                }
            }

            // 击败领悟：skill_base 里 source_type=drop 且 source_id 指向本场敌人的武学
            var defeated_ids = new System.Collections.Generic.List<string>();
            foreach (Variant enemy_variant in enemies)
            {
                GodotObject enemy = enemy_variant.AsGodotObject();
                if (enemy.Call("is_alive").AsBool())
                {
                    continue;
                }

                // 练习战不教武学：木桩不是「击败了谁」
                if (Enc.practice)
                {
                    continue;
                }

                string source = enemy.Get("source_id").AsString();
                if (source.Length == 0)
                {
                    source = enemy.Get("actor_id").AsString().Split('#')[0];
                }

                if (source.Length > 0 && !defeated_ids.Contains(source))
                {
                    defeated_ids.Add(source);
                }
            }

            Godot.Collections.Array granted =
                SkillGrant.grant_from_defeated(Db, state, defeated_ids.ToArray());
            foreach (string learned_line in SkillGrant.summarize(granted))
            {
                lines.Add(learned_line);
                martial.Add(learned_line);
            }
            // 熟练度：这一场真正施放过的招式，每次施放 +mastery_combat_gain
            // （排在卡片前三行之外，进战报——它是慢成长细节，掉落与领悟更要紧）
            MasteryService mastery = MasteryService.create(Db, state);
            foreach (Variant ally_variant in allies)
            {
                string ally_id = _prop_str(ally_variant, "actor_id");
                Godot.Collections.Dictionary usage = sim.Call("skill_uses", ally_id).AsGodotDictionary();
                if (usage.Count == 0)
                {
                    continue;
                }

                // 练习战的熟练度按 `growth_const.dummy_mastery_cap` 封顶（只压涨、不压低）
                int mastery_cap = Enc.practice ? PracticeService.mastery_cap(Db) : 0;
                Godot.Collections.Array mastered = mastery.apply_combat_usage(ally_id, usage, mastery_cap);
                if (mastered.Count > 0)
                {
                    string mastery_line = $"熟练度：{mastery.describe_usage(mastered)}";
                    lines.Add(mastery_line);
                    martial.Add(mastery_line);
                }
            }

            if (Enc.practice)
            {
                string practice_note = $"练习战：经验只算到 {PracticeService.cap_level(Db)} 级、"
                    + $"熟练度只涨到 {PracticeService.mastery_cap(Db)} 级，无掉落与首杀";
                lines.Add(practice_note);
                notes.Add(practice_note);
            }

            if (Enc.pending_rules.Length > 0)
            {
                string pending_note = $"暂缓规则：{string.Join("；", Enc.pending_rules)}";
                lines.Add(pending_note);
                notes.Add(pending_note);
            }

            // 练习战不写「清怪／房间」记录：它不是副本进度，也不进完成度
            if (!Enc.practice)
            {
                _mark_spawn_cleared(session_node);
                _apply_team_win_flags();
            }
        }
        else if (winner == _winner_flee())
        {
            // 主动撤退：不算败北，也不结算经验／铜钱／掉落；明雷留在原地（设计 02：明雷看得见、可以绕开）
            // 口径③：零收获 → 面板上只剩标题 ＋ 说明 ＋「确定」
            string flee_note = "撤退：脱离了战斗（没有经验与掉落）";
            lines.Add(flee_note);
            notes.Add(flee_note);
        }
        else if (winner == _winner_draw())
        {
            // 回合打满（MAX_ROUNDS=50）：谁也没赢。设计没写平局的后果，先按「零收获、明雷不清」处理，
            // 但**不许写「败北」**——那是假话（玩家没输，只是没打完）。口径记进交接表等设计定。
            string draw_note = "平局：回合打满了，双方都没有收获（明雷不会被清掉）";
            lines.Add(draw_note);
            notes.Add(draw_note);
        }
        else
        {
            // 切磋落败是一个**例外**（Q88 之后策划定的口径，2026-10-04）：**不传送、原地站着**——
            // 陪练把人送回城很怪。气血回满与清战斗外增益照旧走败北结算（见 `_handle_defeat` 的
            // `stay_put`），只是不设回程，也不写「被送回」。好感本来就 +0（19 §2.2 输了不罚）。
            if (Enc.spar_npc.Length > 0)
            {
                string spar_loss = "切磋落败，无好感（对方是自己人，不结怨）";
                lines.Add(spar_loss);
                notes.Add(spar_loss);
            }
            else
            {
                // 战败处理（08 已定，0.8.0 口径）：全队回到**最近到过的出生点／城镇**、气血回满、
                // 战斗外增益全部清除；不扣铜钱不掉装备。平局不走这里（见上一分支）。
                string shelter = session_node != null ? session_node.Get("last_shelter_name").AsString() : "";
                string defeat_note = $"败北：全队被送回{(shelter.Length > 0 ? $"「{shelter}」" : "大地图")}，"
                    + "气血已回满、战斗外增益已清除";
                string no_loot_note = "这一场没有收获，明雷不会被清掉";
                lines.Add(defeat_note);
                lines.Add(no_loot_note);
                notes.Add(defeat_note);
                notes.Add(no_loot_note);
            }
        }

        if (session_node != null)
        {
            session_node.Set("last_battle", new Godot.Collections.Dictionary
            {
                { "winner", sim.Call("winner") },
                { "rounds", sim.Call("rounds_played") },
                { "team", Enc.team_name },
                // 队伍 **id**（`team` 是中文名）：小地图靠它认「上一场打完的是哪支队伍」，
                // 再决定回图之后要不要摆出那一段对话（20 号 §七 的终局难题）。
                { "team_id", Enc.team_id },
                { "contact", Enc.contact_label() },
                { "summary", _join_variants(lines, "　") },
                // 击杀方式（毒杀判定用）：隐藏内容里的 trig_poison_kill 靠它判「是不是毒杀的」
                { "kill_styles", sim.Call("kill_styles") },
            });
            session_node.Set("pending_encounter", default(Variant));
        }

        // 战斗外气血（v11）：不管是赢、撤还是败，都把这场的剩余气血写回存档
        _write_back_hp();
        // 战败处理要在写回气血**之后**做：`heal_all()` 清掉 char_hp 记录 = 全队满血，
        // 否则会被上面那份「打完剩 1 点」的记录盖回去。
        if (winner == _winner_enemy())
        {
            _handle_defeat(session_node, Enc.spar_npc.Length > 0);
        }

        // 打完就自动存档（设计 02：副本内自动存档，退出重进不掉本层进度）
        Godot.Collections.Dictionary save_result = save_service().save("战斗结算", true);
        // **自动存档失败要在战报里说一句**：不然玩家以为这一场白打的进度都存住了，其实只在本局里。
        // 失败不打断结算（AGENTS：存档写入失败不允许挡住开局），但绝不能静默。
        if (!_b(save_result, "ok"))
        {
            string save_note =
                $"自动存档失败：{_s(save_result, "error", "未知原因")}（这一场的结果只在本局里）";
            _append_log(new Godot.Collections.Array { save_note });
            // 面板上也要说一句：这一屏是玩家唯一会看的结算，战报不打开就看不见
            notes.Add(save_note);
        }

        // 结算面板（22 §1.6 第八版）：六块自上而下——战果标题／经验（含逐人升级行）／铜钱／掉落／武学／「确定」。
        // 「确定」是场景里那枚 `ResultConfirm`（第七版），不归面板建。
        _refresh_settle_panel(new Godot.Collections.Dictionary
        {
            { "title", _settle_title(winner) },
            { "harvest", winner == _winner_ally() },
            { "exp", _i(applied, "exp") },
            { "money", _i(applied, "money") },
            { "levels", _arr_of(applied, "level_ups") },
            { "drops", _settle_drop_cells(applied) },
            // 掉落总件数：面板只摆得下 SETTLE_DROP_ITEMS 格，多出来的进战报，面板上要留一句「还有几件」
            { "drops_total", _drop_cells(applied).Count },
            { "martial", martial },
            { "notes", notes },
        });
        // 结算全文进战报：第八版不再只塞"放不下的那几行"，`lines` 本来就是全量（掉落明细／领悟／熟练度
        // 都还翻得到，`last_battle.summary` 也读这份）。
        _append_log(lines);
        _refresh();
        _refresh_buttons();
    }

    /// <summary>
    /// 把这一场打完的剩余气血写回存档（05 文档的医馆治疗要按「缺失气血」收费，得先有这个数）。
    /// 下限 1：倒下的角色回图时留 1 点，免得全队 0 血把存档卡死（设计没定败北代价，口径已记进交接表）。
    /// </summary>
    public void _write_back_hp()
    {
        if (state == null)
        {
            return;
        }

        string[] char_ids = state.Get("char_ids").AsStringArray();
        foreach (Variant ally_variant in allies)
        {
            string char_id = _prop_str(ally_variant, "actor_id");
            // 用例会把 actor_id 改成 scholar_1 之类，只有真角色 id 才写回
            if (char_id.Length == 0 || System.Array.IndexOf(char_ids, char_id) < 0)
            {
                continue;
            }

            state.Call("set_current_hp", char_id, System.Math.Max(1, _prop_int(ally_variant, "hp")));
        }
    }

    /// <summary>
    /// 战败处理（08：回最近到过的出生点／城镇 ＋ 气血回满 ＋ 战斗外增益全部清除；
    /// 不扣铜钱、不掉装备——第一章不做死亡惩罚）。
    ///
    /// 只有真的败北才走这里：**平局留在原地**（设计原文），撤退也不回城。
    /// 回到哪张图交给会话：`pending_local_scene` + `pending_return_scene` 是既有的回程通道，
    /// 小地图控制器与大地图都读它们，所以这里不需要另开一条传送实现。
    ///
    /// `stay_put=true` 是**切磋**那一条（Q88 之后策划定的口径）：气血回满与清战斗外增益照旧，
    /// 但**不设回程**——陪练把人送回城很怪。
    /// </summary>
    private void _handle_defeat(Node session_node, bool stay_put = false)
    {
        state?.Call("heal_all");
        if (session_node == null)
        {
            return;
        }

        session_node.Call("clear_field_buffs");
        if (stay_put)
        {
            return;
        }

        string shelter = session_node.Get("last_shelter_scene").AsString();
        if (shelter.Length > 0)
        {
            session_node.Set("pending_local_scene", shelter);
            session_node.Set("pending_return_scene", LOCAL_SCENE);
        }
    }

    private void _mark_spawn_cleared(Node session_node)
    {
        if (session_node == null)
        {
            return;
        }

        if (Enc.source_scene == "overworld")
        {
            Resource row = Db.get_row("roaming_spawn", Enc.source_key);
            int respawn = row != null ? row.Get("respawn_sec").AsInt32() : 0;
            // 0 = 不刷新（设计：精英长 CD 或不刷新；第一章普通明雷 30~60 秒）
            Godot.Collections.Dictionary cleared = session_node.Get("cleared_spawns").AsGodotDictionary();
            cleared[Enc.source_key] = respawn <= 0
                ? -1
                : (int)Time.GetUnixTimeFromSystem() + respawn;
            return;
        }

        // 小地图房间：记在本张图的会话状态里（回大地图整片刷新）
        string scene_id = Enc.source_scene;
        Godot.Collections.Dictionary local_maps = session_node.Get("local_maps").AsGodotDictionary();
        if (!local_maps.ContainsKey(scene_id))
        {
            local_maps[scene_id] = new Godot.Collections.Dictionary
            {
                { "cleared", new Godot.Collections.Array() },
                { "chests", new Godot.Collections.Array() },
                { "triggers", new Godot.Collections.Array() },
            };
        }

        Godot.Collections.Dictionary map_record = local_maps[scene_id].AsGodotDictionary();
        Godot.Collections.Array record = map_record.ContainsKey("cleared")
            ? map_record["cleared"].AsGodotArray()
            : new Godot.Collections.Array();
        if (!record.Contains(Enc.source_key))
        {
            record.Add(Enc.source_key);
        }

        map_record["cleared"] = record;
        // 完成度与「已通关层」是永久记录：打过的房间、被击败的紫名 Boss 都写进存档
        if (state == null)
        {
            return;
        }

        state.Call("record_dungeon", scene_id, "rooms", Enc.source_key);
        foreach (Variant enemy_variant in enemies)
        {
            GodotObject enemy = enemy_variant.AsGodotObject();
            if (enemy.Call("is_alive").AsBool())
            {
                continue;
            }

            Godot.Collections.Dictionary tags = enemy.Get("tags").AsGodotDictionary();
            if (tags.ContainsKey("threat_tag") && tags["threat_tag"].AsString() == "purple")
            {
                state.Call("record_dungeon", scene_id, "bosses", enemy.Get("source_id"));
            }
        }
    }

    /// <summary>
    /// 打赢某支队伍 → 置旗标（设计 20 §十一 那三个「没有来源」的旗标里，靠战斗收尾的两个）。
    ///
    /// 荒村废屋的屠夫 → `flag_huangcun_done`（白清和招募的条件）；毒堂的毒手 → `flag_poison_hall`
    /// （苏九娘招募的条件）。**暂时写在这个常量里**：表里还没有「打赢某队置旗标」这一列
    /// （设计 20 §十一 只给了口径），已记 `待策划确认.md` Q69——设计给了列（例如
    /// `enemy_team.win_flag`）就挪进表，这一处跟着改成查表。
    ///
    /// 不往战报里加文案：旗标的可见反馈是**回到那张图时同伴入队**（`RecruitService`），
    /// 在这儿再编一句叙事反而会和设计后面的台词打架。
    /// </summary>
    private void _apply_team_win_flags()
    {
        if (state == null || !TEAM_WIN_FLAGS.ContainsKey(Enc.team_id))
        {
            return;
        }

        state.Call("set_flag", TEAM_WIN_FLAGS[Enc.team_id], true);
    }

    /// <summary>这一场打赢之后该发的剧情物 id（没配就是空）</summary>
    private string _team_win_item_id() =>
        Enc == null ? "" : _s(TEAM_WIN_ITEMS, Enc.team_id);

    /// <summary>玩家手里有没有这件东西——`TEAM_WIN_ITEMS` 用它判「领过了没有」</summary>
    private bool _owns_item(string item_id)
    {
        if (state == null)
        {
            return false;
        }

        GodotObject inventory = state.Get("inventory").AsGodotObject();
        return inventory != null && inventory.Call("has", item_id, 1).AsBool();
    }

    /// <summary>对位 GDScript 的 `"分隔".join(array)`</summary>
    private static string _join_variants(Godot.Collections.Array source, string separator)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant item in source)
        {
            parts.Add(item.AsString());
        }

        return string.Join(separator, parts);
    }
}
