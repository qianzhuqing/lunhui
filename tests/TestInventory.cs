using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 背包与装备：堆叠、实例、穿戴规则、贡献列表、序列化（C# 版，对位原 `tests/test_inventory.gd`）。
/// </summary>
public partial class TestInventory : TestCase
{

    public override string SuiteName() => "背包与装备";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        // 抬到 10 级：让等级需求不挡后面几组用例（等级限制本身单独验）
        string char_id = state.Get("char_ids").AsStringArray()[0];
        state.Get("char_levels").AsGodotDictionary()[char_id] = 10;
        Inventory inventory = state.Get("inventory").As<Inventory>();
        _CheckStartingEquipment(db, state, inventory);
        _CheckStacks(db, inventory);
        _CheckEquipRules(db, state, inventory);
        _CheckSlotCapacity(db, state, inventory);
        _CheckContributions(db, state);
        _CheckSlotTableChange(db);
        _CheckPerCharacterIsolation();
        _CheckRoundTrip(db, state, inventory);
        _CheckEquipInvariants(db, state, inventory);
    }

    private void _CheckStartingEquipment(TableDb db, GodotObject state, Inventory inventory)
    {
        string char_id = state.Get("char_ids").AsStringArray()[0];
        string[] equipped = inventory.equipped_instances(db, char_id);
        CheckGt(equipped.Length, 0.0, "新建游戏会按模板穿上初始装备");
        Godot.Collections.Dictionary owner = inventory.equipped_by(equipped[0]);
        CheckEq(owner.ContainsKey("slot_id") ? owner["slot_id"].AsString() : "", "weapon", "初始装备穿在武器槽");
        // 0.10.2：`start_equip_ids` 由单件改成两件（武器 ＋ `eq_armor_01` 布衣）
        CheckEq(equipped.Length, 2, "新建游戏穿上**两件**初始装备（武器＋上衣）");
        if (equipped.Length > 1)
        {
            Godot.Collections.Dictionary armor_owner = inventory.equipped_by(equipped[1]);
            CheckEq(
                armor_owner.ContainsKey("slot_id") ? armor_owner["slot_id"].AsString() : "",
                "body",
                "第二件穿在身上（布衣）");
        }

        Godot.Collections.Dictionary slots = inventory.equipment_slots(db, char_id);
        CheckEq(slots.Count, 7, "槽位视图覆盖 7 类槽位");
        CheckEq(slots["ring"].AsGodotArray().Count, 2, "戒指槽有两个格子");
        // 初始装备占掉 weapon 与 body 两格，所以「空槽」用下装（7 类槽位里没有「头」）：
        // 槽位清单以 `equip_slot_def` 为准——武器／身体／腰带／肩部／下装／戒指／项链
        CheckEq(slots["legs"].AsGodotArray()[0].AsString(), "", "没装备的槽位是空串");
    }

    private void _CheckStacks(TableDb db, Inventory inventory)
    {
        Godot.Collections.Dictionary added = inventory.add_item(db, "item_iron", 5);
        CheckTrue(added["ok"].AsBool(), "放入材料成功");
        CheckEq(inventory.count("item_iron"), 5, "数量累加");

        // stack_max = 99：再加 100 只进 94，其余在 overflow 里报出来
        Godot.Collections.Dictionary overflow = inventory.add_item(db, "item_iron", 100);
        CheckEq(overflow["added"].AsInt32(), 94, "超出 stack_max 的部分被截断");
        CheckEq(overflow["overflow"].AsInt32(), 6, "截断数量报在 overflow");
        CheckEq(inventory.count("item_iron"), 99, "堆叠上限生效");

        Godot.Collections.Dictionary removed = inventory.remove_item(db, "item_iron", 9);
        CheckTrue(removed["ok"].AsBool(), "丢弃材料成功");
        CheckEq(inventory.count("item_iron"), 90, "丢弃后数量正确");

        // 钥匙道具不能丢
        inventory.add_item(db, "item_wine_gourd", 1);
        Godot.Collections.Dictionary refused = inventory.remove_item(db, "item_wine_gourd", 1);
        CheckFalse(refused["ok"].AsBool(), "钥匙道具不能丢弃");
        CheckTrue(
            refused["error"].AsString().Contains("钥匙"), "拒绝原因说明是钥匙道具");
        CheckEq(inventory.count("item_wine_gourd"), 1, "被拒后数量没变");

        // 装备不能用 add_item 塞
        Godot.Collections.Dictionary wrong = inventory.add_item(db, "eq_sword_01", 1);
        CheckFalse(wrong["ok"].AsBool(), "装备要走 add_equipment");
    }

    private void _CheckEquipRules(TableDb db, GodotObject state, Inventory inventory)
    {
        string char_id = state.Get("char_ids").AsStringArray()[0];
        int level = state.Call("level_of", char_id).AsInt32();
        string weapon_type = ((CharacterBaseRow)db.get_row("character_base", char_id)).weapon_type;

        // 武器类型不匹配（书生用剑，铁拳套是拳）
        string fist = inventory.add_equipment(db, "eq_fist_01", new Godot.Collections.Array());
        Godot.Collections.Dictionary fist_check = inventory.can_equip(db, char_id, level, weapon_type, fist);
        CheckFalse(fist_check["ok"].AsBool(), "拳套装不到用剑的角色身上");
        CheckTrue(fist_check["error"].AsString().Contains("拳"), "拒绝原因说明武器类型");

        // 等级不足（锈月要 10 级）
        string legendary = inventory.add_equipment(db, "eq_sword_04", new Godot.Collections.Array());
        Godot.Collections.Dictionary level_check =
            inventory.can_equip(db, char_id, 1, weapon_type, legendary);
        CheckFalse(level_check["ok"].AsBool(), "等级不足的装备不能穿");
        CheckTrue(level_check["error"].AsString().Contains("级"), "拒绝原因说明等级需求");
        CheckTrue(
            inventory.can_equip(db, char_id, 10, weapon_type, legendary)["ok"].AsBool(),
            "等级够了就能穿");

        // 卸下 → 外功下降；装回 → 恢复
        string sword = inventory.equipped_instances(db, char_id)[0];
        double atk_before = _AtkOf(db, state);
        Godot.Collections.Dictionary removed = inventory.unequip(char_id, "weapon", 0);
        CheckTrue(removed["ok"].AsBool(), "卸下武器成功");
        CheckEq(removed["instance_id"].AsString(), sword, "卸下的正是那把武器");
        double atk_without = _AtkOf(db, state);
        CheckLt(atk_without, atk_before, "卸下武器后外功下降");
        Godot.Collections.Dictionary again = inventory.equip(db, char_id, level, weapon_type, sword);
        CheckTrue(again["ok"].AsBool(), "重新装上武器成功");
        CheckFloat(_AtkOf(db, state), atk_before, "装回后外功恢复");

        // 已装备的实例不能直接删
        Godot.Collections.Dictionary delete_equipped = inventory.remove_equipment(sword);
        CheckFalse(delete_equipped["ok"].AsBool(), "已装备的实例不能直接删掉");
        Godot.Collections.Dictionary delete_idle = inventory.remove_equipment(fist);
        CheckTrue(delete_idle["ok"].AsBool(), "没装备的实例可以删掉");
    }

    private void _CheckSlotCapacity(TableDb db, GodotObject state, Inventory inventory)
    {
        string char_id = state.Get("char_ids").AsStringArray()[0];
        int level = state.Call("level_of", char_id).AsInt32();
        string weapon_type = ((CharacterBaseRow)db.get_row("character_base", char_id)).weapon_type;
        string ring_a = inventory.add_equipment(db, "eq_ring_01", new Godot.Collections.Array());
        string ring_b = inventory.add_equipment(db, "eq_ring_02", new Godot.Collections.Array());
        string ring_c = inventory.add_equipment(db, "eq_ring_03", new Godot.Collections.Array());
        CheckTrue(
            inventory.equip(db, char_id, level, weapon_type, ring_a)["ok"].AsBool(), "第一枚戒指装上");
        CheckTrue(
            inventory.equip(db, char_id, level, weapon_type, ring_b)["ok"].AsBool(), "第二枚戒指装上");
        Godot.Collections.Array ring_slots = inventory.equipment_slots(db, char_id)["ring"].AsGodotArray();
        CheckEq(ring_slots[0].AsString(), ring_a, "第一格是戒指 A");
        CheckEq(ring_slots[1].AsString(), ring_b, "第二格是戒指 B");
        Godot.Collections.Dictionary third = inventory.equip(db, char_id, level, weapon_type, ring_c);
        CheckTrue(third["ok"].AsBool(), "第三枚戒指会替换第一格");
        CheckEq(third["replaced"].AsString(), ring_a, "被替换的是第一格的戒指");
        CheckEq(
            inventory.equipment_slots(db, char_id)["ring"].AsGodotArray()[0].AsString(),
            ring_c,
            "第一格换成戒指 C");
    }

    private void _CheckContributions(TableDb db, GodotObject state)
    {
        string char_id = state.Get("char_ids").AsStringArray()[0];
        int level = state.Call("level_of", char_id).AsInt32();
        string weapon_type = ((CharacterBaseRow)db.get_row("character_base", char_id)).weapon_type;
        Inventory inventory = state.Get("inventory").As<Inventory>();
        // 青锋剑：敏 +2（属性点层）＋ 外功 +14（固定值层）
        string sword = inventory.add_equipment(db, "eq_sword_02", new Godot.Collections.Array());
        inventory.equip(db, char_id, level, weapon_type, sword);
        Godot.Collections.Array contributions = inventory.contributions_for(db, char_id);
        bool has_attr = false;
        bool has_flat = false;
        foreach (Variant entry in contributions)
        {
            Godot.Collections.Dictionary item = entry.AsGodotDictionary();
            if (item["kind"].AsString() == "attr_point"
                && item["target"].AsString() == "agi"
                && item["value"].AsInt32() == 2)
            {
                has_attr = true;
            }

            if (item["kind"].AsString() == "stat_flat"
                && item["target"].AsString() == "atk_phys"
                && item["value"].AsSingle() == 14.0f)
            {
                has_flat = true;
            }
        }

        CheckTrue(has_attr, "装备的属性点加成走 attr_point 层");
        CheckTrue(has_flat, "装备的派生数值加成走 stat_flat 层");
    }

    /// <summary>
    /// 设计改了槽位表（删掉一类槽位／把 max_equip 改小）之后，存档里多出来的那些穿戴记录怎么办。
    /// 这条必须守住：**配表认不出的记录不算加成，读档时退回背包**（实例本体不删）。
    /// </summary>
    private void _CheckSlotTableChange(TableDb db)
    {
        GodotObject state = SoloState(db);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        state.Get("char_levels").AsGodotDictionary()[char_id] = 10;
        string weapon_type = ((CharacterBaseRow)db.get_row("character_base", char_id)).weapon_type;
        Inventory inventory = state.Get("inventory").As<Inventory>();
        string ring_a = inventory.add_equipment(db, "eq_ring_01", new Godot.Collections.Array());
        string ring_b = inventory.add_equipment(db, "eq_ring_02", new Godot.Collections.Array());
        CheckTrue(
            inventory.equip(db, char_id, 10, weapon_type, ring_a)["ok"].AsBool(),
            "旧配表下第 1 枚戒指装上");
        CheckTrue(
            inventory.equip(db, char_id, 10, weapon_type, ring_b)["ok"].AsBool(),
            "旧配表下第 2 枚戒指装上");
        CheckEq(
            inventory.equipped_instances(db, char_id).Length, 4,
            "旧配表下身上是武器＋上衣＋两枚戒指");

        // ① 容量改小：戒指从 2 格改成 1 格
        TableDb narrow = _SlotTableWith(
            db, new Godot.Collections.Dictionary { { "ring", 1 } }, System.Array.Empty<string>());
        CheckEq(
            inventory.equipment_slots(narrow, char_id)["ring"].AsGodotArray().Count, 1,
            "新配表只显示 1 个戒指格");
        CheckEq(
            inventory.equipped_instances(narrow, char_id).Length, 3,
            "新配表下只认武器＋上衣＋第 1 枚戒指");
        CheckEq(
            _SourcesOf(inventory.contributions_for(narrow, char_id), new[] { ring_b }), 0,
            "装不下的那枚不再加属性");
        string[] reclaimed = inventory.reclaim_stranded_equipped(narrow, char_id);
        CheckEq(reclaimed.Length, 1, "装不下的那枚被退回背包");
        CheckEq(reclaimed[0], ring_b, "退回的正是第 2 枚（保留界面显示的第 1 格）");
        CheckFalse(inventory.is_equipped(ring_b), "退回后它不再算穿着");
        CheckTrue(inventory.has_equipment(ring_b), "实例本体留在背包里（没被删掉）");
        CheckEq(
            inventory.equipped_instances(narrow, char_id).Length, 3,
            "退回后身上是武器＋上衣＋第 1 枚戒指");

        // ② 整类槽位被删（例：设计把 ring 从 equip_slot_def 里拿掉）
        TableDb cut = _SlotTableWith(db, new Godot.Collections.Dictionary(), new[] { "ring" });
        CheckFalse(inventory.equipment_slots(cut, char_id).ContainsKey("ring"), "新配表里没有戒指槽了");
        CheckEq(
            inventory.equipped_instances(cut, char_id).Length, 2, "删槽位后身上只剩武器＋上衣");
        CheckEq(
            _SourcesOf(inventory.contributions_for(cut, char_id), new[] { ring_a, ring_b }), 0,
            "删槽位后戒指不再加属性");
        string[] cut_reclaimed = inventory.reclaim_stranded_equipped(cut, char_id);
        CheckEq(cut_reclaimed.Length, 1, "还在身上的那枚也被退回");
        CheckFalse(inventory.is_equipped(ring_a), "退回后戒指不算穿着");
        CheckTrue(inventory.has_equipment(ring_a), "戒指实例还在背包里");

        // ③ 真走一次读档：`GameState.from_dict` 会按当前配表把装不下的记录退回背包
        GodotObject fresh = SoloState(db);
        fresh.Get("char_levels").AsGodotDictionary()[char_id] = 10;
        Inventory fresh_inventory = fresh.Get("inventory").As<Inventory>();
        string r1 = fresh_inventory.add_equipment(db, "eq_ring_01", new Godot.Collections.Array());
        string r2 = fresh_inventory.add_equipment(db, "eq_ring_02", new Godot.Collections.Array());
        fresh_inventory.equip(db, char_id, 10, weapon_type, r1);
        fresh_inventory.equip(db, char_id, 10, weapon_type, r2);
        GodotObject back = GameState.from_dict(fresh.Call("to_dict").AsGodotDictionary(), narrow);
        CheckNotNull(back, "换了槽位表的存档仍能读入");
        if (back != null)
        {
            Inventory back_inventory = back.Get("inventory").As<Inventory>();
            CheckEq(
                back_inventory.equipped_instances(narrow, char_id).Length, 3,
                "读档后只认武器＋上衣＋第 1 枚戒指");
            CheckFalse(back_inventory.is_equipped(r2), "读档时第 2 枚被退回背包（不再偷偷加属性）");
            CheckTrue(back_inventory.has_equipment(r2), "退回的实例还在背包里");
            CheckEq(back.Get("reclaimed_equipped").AsInt32(), 1, "读档记下按配表退回了 1 件");
        }

        // ④ 实例序号不许复用（含存档往返）：`next_uid` 必须随存档走
        string temp = inventory.add_equipment(db, "eq_ring_01", new Godot.Collections.Array());
        CheckTrue(inventory.remove_equipment(temp)["ok"].AsBool(), "临时那件装备能删掉");
        Inventory reloaded = Inventory.from_dict(inventory.to_dict());
        string[] existing = reloaded.equipment_ids();
        string again = reloaded.add_equipment(db, "eq_ring_01", new Godot.Collections.Array());
        CheckNe(again, temp, "读档后再新增不会复用刚删掉的序号");
        CheckFalse(_Has(existing, again), "新实例 id 不和现有任何一件撞号（撞上就是静默覆盖）");
        CheckFalse(reloaded.has_equipment(temp), "旧实例确实不在了");
        CheckTrue(reloaded.has_equipment(again), "新实例在背包里");
    }

    /// <summary>
    /// 装备与加点是**按人**的：给 A 换武器／加点，B 的属性与身上装备一点都不能动。
    /// 跨人串号（把一个人的加成算到另一个人头上）是极难发现的错。
    /// </summary>
    private void _CheckPerCharacterIsolation()
    {
        TableDb wide = TableWithNChars(4);
        GodotObject state = PartyState(wide, 4);
        string[] ids = state.Get("char_ids").AsStringArray();
        CheckEq(ids.Length, 4, "造了 4 人队伍");
        string a = ids[0];
        string b = ids[1];
        string weapon_type = ((CharacterBaseRow)wide.get_row("character_base", a)).weapon_type;
        double atk_a_before = _AtkOfChar(wide, state, a);
        double atk_b_before = _AtkOfChar(wide, state, b);
        Inventory inventory = state.Get("inventory").As<Inventory>();
        string b_equipped_before = _Join(inventory.equipped_instances(wide, b));
        string b_weapon_before =
            inventory.equipment_slots(wide, b)["weapon"].AsGodotArray()[0].AsString();

        state.Call("append_level", a, 4); // 青锋剑要 5 级
        string sword = inventory.add_equipment(wide, "eq_sword_02", new Godot.Collections.Array());
        Godot.Collections.Dictionary equipped = inventory.equip(
            wide, a, state.Call("level_of", a).AsInt32(), weapon_type, sword);
        CheckTrue(equipped["ok"].AsBool(), $"A 换上青锋剑：{equipped["error"].AsString()}");
        CheckGt(_AtkOfChar(wide, state, a), atk_a_before, "A 的外功涨了");
        CheckEq(_AtkOfChar(wide, state, b), atk_b_before, "B 的外功一点没动");
        CheckEq(_Join(inventory.equipped_instances(wide, b)), b_equipped_before, "B 身上的装备没被换过");
        CheckEq(
            inventory.equipment_slots(wide, b)["weapon"].AsGodotArray()[0].AsString(),
            b_weapon_before,
            "B 的武器槽还是原来那件（A 换的是自己的）");

        state.Call("spend_point", wide, a, "str");
        CheckEq(_Allocation(state, a, "str"), 1, "A 的加点记在 A 头上");
        CheckEq(_Allocation(state, b, "str"), 0, "B 的加点还是 0");
        CheckEq(_AtkOfChar(wide, state, b), atk_b_before, "给 A 加点之后 B 的外功仍不变");
    }

    private void _CheckRoundTrip(TableDb db, GodotObject state, Inventory inventory)
    {
        inventory.money = 777;
        inventory.add_item(db, "item_herb", 3);
        Inventory restored = Inventory.from_dict(inventory.to_dict());
        CheckEq(restored.money, 777, "铜钱读回一致");
        CheckEq(restored.count("item_herb"), 3, "堆叠读回一致");
        CheckEq(restored.equipment_count(), inventory.equipment_count(), "装备数量读回一致");
        string char_id = state.Get("char_ids").AsStringArray()[0];
        CheckEq(
            inventory.equipment_slots(db, char_id).ToString(),
            restored.equipment_slots(db, char_id).ToString(),
            "穿戴映射读回一致");
        // 读档后再捡一件装备**不能撞上已有的实例 id**
        string[] pending = inventory.equipment_ids();
        string fresh_id = restored.add_equipment(db, "eq_sword_01", new Godot.Collections.Array());
        CheckFalse(fresh_id.Length == 0, "读档后还能造装备实例");
        CheckFalse(_Has(pending, fresh_id), $"新实例 id 不与存档里已有的撞车：{fresh_id}");
        CheckEq(
            restored.equipment_count(), inventory.equipment_count() + 1,
            "新实例是**进背包**（+1），不是覆盖旧的那件");
    }

    /// <summary>
    /// 穿戴的不变量：确定性小模糊测试（**不用随机数**——步数取模就够了）。
    /// 每一步都查五件事：① 槽是定长的；② 没有悬空 id；③ 同一个实例不会同时穿在两处；
    /// ④ 每格用量不超过容量；⑤ `next_uid` 必须大于所有已存在的序号。
    /// 另外：**被拒绝的那一次不许改动任何东西**（拿 `to_dict()` 前后对拍）。
    /// </summary>
    private void _CheckEquipInvariants(TableDb db, GodotObject state, Inventory inventory)
    {
        string char_id = state.Get("char_ids").AsStringArray()[0];
        var capacities = new Godot.Collections.Dictionary();
        foreach (Variant entry in db.rows("equip_slot_def"))
        {
            if (entry.AsGodotObject() is EquipSlotDefRow row)
            {
                capacities[row.slot_id] = row.max_equip;
            }
        }

        CheckGt(capacities.Count, 3.0, $"读到足够多的槽位定义（{capacities.Count} 个）");
        var slot_ids = new System.Collections.Generic.List<string>();
        foreach (Variant key in capacities.Keys)
        {
            slot_ids.Add(key.AsString());
        }

        var pool = new System.Collections.Generic.List<string>();
        foreach (Variant entry in db.rows("equip_base"))
        {
            if (entry.AsGodotObject() is EquipBaseRow row)
            {
                pool.Add(row.equip_id);
            }
        }

        CheckGt(pool.Count, 5.0, $"读到足够多的装备模板（{pool.Count} 件）");

        var problems = new System.Collections.Generic.List<string>();
        int added = 0;
        int equipped_ok = 0;
        int unequipped_ok = 0;
        int refused = 0;
        for (int step = 1; step <= 160; step++)
        {
            switch (step % 4)
            {
                case 0:
                {
                    string base_id = pool[(step * 7) % pool.Count];
                    if (inventory.add_equipment(db, base_id, new Godot.Collections.Array()).Length > 0)
                    {
                        added += 1;
                    }

                    break;
                }

                case 1:
                {
                    string[] ids = inventory.equipment_ids();
                    if (ids.Length == 0)
                    {
                        continue;
                    }

                    string wanted = ids[(step * 5) % ids.Length];
                    string before = inventory.to_dict().ToString();
                    Godot.Collections.Dictionary result =
                        inventory.equip(db, char_id, 20, "sword", wanted);
                    if (result["ok"].AsBool())
                    {
                        equipped_ok += 1;
                    }
                    else
                    {
                        refused += 1;
                        if (inventory.to_dict().ToString() != before)
                        {
                            problems.Add(
                                $"第 {step} 步：穿戴被拒（{result["error"].AsString()}）却改动了背包");
                        }
                    }

                    break;
                }

                case 2:
                {
                    string slot_id = slot_ids[(step * 3) % slot_ids.Count];
                    Godot.Collections.Array entries =
                        inventory.equipment_slots(db, char_id)[slot_id].AsGodotArray();
                    string before = inventory.to_dict().ToString();
                    Godot.Collections.Dictionary result = inventory.unequip(
                        char_id, slot_id, (step * 11) % System.Math.Max(1, entries.Count));
                    if (result["ok"].AsBool())
                    {
                        unequipped_ok += 1;
                    }
                    else
                    {
                        refused += 1;
                        if (inventory.to_dict().ToString() != before)
                        {
                            problems.Add(
                                $"第 {step} 步：卸下被拒（{result["error"].AsString()}）却改动了背包");
                        }
                    }

                    break;
                }

                default:
                {
                    string[] ids = inventory.equipment_ids();
                    if (ids.Length == 0)
                    {
                        continue;
                    }

                    // 穿在身上的会被拒（"先卸下装备再处理"）——这条拒绝也必须不改动任何东西
                    string victim = ids[(step * 13) % ids.Length];
                    string before = inventory.to_dict().ToString();
                    Godot.Collections.Dictionary result = inventory.remove_equipment(victim);
                    if (!result["ok"].AsBool())
                    {
                        refused += 1;
                        if (inventory.to_dict().ToString() != before)
                        {
                            problems.Add($"第 {step} 步：丢弃被拒却改动了背包");
                        }
                    }

                    break;
                }
            }

            problems.AddRange(_EquipStateProblems(db, inventory, char_id, capacities, step));
        }

        CheckGt(added, 0.0, $"模糊测试真的造过装备（{added} 件）");
        CheckGt(equipped_ok, 0.0, $"真的穿上过（{equipped_ok} 次）");
        CheckGt(unequipped_ok, 0.0, $"真的卸下过（{unequipped_ok} 次）");
        CheckGt(refused, 0.0, $"也碰到过被拒绝的操作（{refused} 次）——那条不变量才算真验过");
        CheckEq(problems.Count, 0, $"160 步里没有一步破坏穿戴的不变量：{string.Join("；", problems)}");
    }

    /// <summary>当前背包／穿戴的五条不变量，返回所有违规描述（空 = 全过）。</summary>
    private System.Collections.Generic.List<string> _EquipStateProblems(
        TableDb db,
        Inventory inventory,
        string char_id,
        Godot.Collections.Dictionary capacities,
        int step)
    {
        var problems = new System.Collections.Generic.List<string>();
        Godot.Collections.Dictionary view = inventory.equipment_slots(db, char_id);
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (Variant slot_key in view.Keys)
        {
            string slot_id = slot_key.AsString();
            Godot.Collections.Array entries = view[slot_key].AsGodotArray();
            int capacity = capacities.ContainsKey(slot_id) ? capacities[slot_id].AsInt32() : 1;
            if (entries.Count != capacity)
            {
                problems.Add($"第 {step} 步：{slot_id} 槽不是定长（{entries.Count} ≠ {capacity}）");
            }

            int used = 0;
            foreach (Variant entry in entries)
            {
                string instance_id = entry.AsString();
                if (instance_id.Length == 0)
                {
                    continue;
                }

                used += 1;
                if (!inventory.has_equipment(instance_id))
                {
                    problems.Add($"第 {step} 步：{slot_id} 槽挂着不存在的实例 {instance_id}（悬空 id）");
                }

                if (!seen.Add(instance_id))
                {
                    problems.Add($"第 {step} 步：实例 {instance_id} 同时穿在两处");
                }
            }

            if (used > capacity)
            {
                problems.Add($"第 {step} 步：{slot_id} 槽超容量（{used} > {capacity}）");
            }
        }

        // `next_uid` 必须大于所有已存在的序号，否则下一件装备就会撞上旧的那件
        int max_uid = 0;
        foreach (string instance_id in inventory.equipment_ids())
        {
            int cut = instance_id.LastIndexOf('#');
            if (cut < 0)
            {
                continue;
            }

            if (int.TryParse(instance_id.Substring(cut + 1), out int uid))
            {
                max_uid = System.Math.Max(max_uid, uid);
            }
        }

        if (max_uid >= inventory.next_uid)
        {
            problems.Add(
                $"第 {step} 步：next_uid={inventory.next_uid} 不大于已有的最大序号 {max_uid}（下一件会撞车）");
        }

        return problems;
    }

    /// <summary>
    /// 复制一份表库并按参数改 `equip_slot_def`：`overrides` 改容量、`remove` 删整类槽位。
    /// **只改内存副本，不碰策划的 CSV。**
    /// </summary>
    private static TableDb _SlotTableWith(
        TableDb db, Godot.Collections.Dictionary overrides, string[] remove)
    {
        var custom = new TableDb();
        custom.load_all();
        var table = (TableResource)custom.tables["equip_slot_def"].As<TableResource>().Duplicate(true);
        var kept = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            var row = (EquipSlotDefRow)((EquipSlotDefRow)entry.AsGodotObject()).Duplicate(true);
            string slot_id = row.slot_id;
            if (System.Array.IndexOf(remove, slot_id) >= 0)
            {
                continue;
            }

            if (overrides.ContainsKey(slot_id))
            {
                row.max_equip = overrides[slot_id].AsInt32();
            }

            kept.Add(row);
        }

        table.rows = kept;
        custom.tables["equip_slot_def"] = table;
        return custom;
    }

    /// <summary>贡献列表里来自这几件实例的条数（用来验「装不下的那件不再加属性」）。</summary>
    private static int _SourcesOf(Godot.Collections.Array contributions, string[] instance_ids)
    {
        int total = 0;
        foreach (Variant entry in contributions)
        {
            Godot.Collections.Dictionary item = entry.AsGodotDictionary();
            string source = item.ContainsKey("source") ? item["source"].AsString() : "";
            if (System.Array.IndexOf(instance_ids, source) >= 0)
            {
                total += 1;
            }
        }

        return total;
    }

    private double _AtkOf(TableDb db, GodotObject state) =>
        _AtkOfChar(db, state, state.Get("char_ids").AsStringArray()[0]);

    private double _AtkOfChar(TableDb db, GodotObject state, string char_id) =>
        new CharacterSheet(db, state, char_id).stat_value("atk_phys");

    private static string _Join(string[] values) => string.Join("|", values);

    /// <summary>读某角色的加点：GDScript 的 `dict["str"]` 缺键返回 null，C# 索引器会抛，故显式兜底。</summary>
    private static int _Allocation(GodotObject state, string char_id, string attr_id)
    {
        Godot.Collections.Dictionary alloc =
            state.Call("allocations_of", char_id).AsGodotDictionary();
        return alloc.ContainsKey(attr_id) ? alloc[attr_id].AsInt32() : 0;
    }

    private static bool _Has(string[] values, string wanted) =>
        System.Array.IndexOf(values, wanted) >= 0;
}
