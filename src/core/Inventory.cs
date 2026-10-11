using Godot;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 背包与装备（C# 版，对位原 `src/core/inventory.gd`）。
///
/// 纯逻辑 + 可序列化：物品堆叠、装备实例、穿戴映射、铜钱。
/// 不依赖场景树，也不依赖 GameState，所以能被面板、战斗与存档共用。
///
/// 装备实例 id 形如 `eq_sword_01#3`（基础 id + 序号），序号存在存档里，读档后不会重复。
/// 槽位容量取自 `equip_slot_def.max_equip`（戒指 2），槽位数组定长，空位存空串。
///
/// **背包没有自己的版本号**：存档结构的唯一迁移闸门是 `GameState.VERSION`，
/// `from_dict` 对缺字段一律取默认值（v1 那种没有 `affixes` 的装备条目照样读得进来）。
///
/// **跨语言面**：`stacks`／`equipment`／`equipped`／`money`／`next_uid` 是 GDScript 直接读写
/// 的字段（`state.inventory.money = 1000`、`state.inventory.stacks.erase(...)`），
/// 所以必须是 `[Export]` 的公开属性（坑⑩／`MenuController.slots` 同形）。
/// </summary>
public partial class Inventory : RefCounted
{
    /// <summary>装备实例 id 的分隔符（`eq_sword_01#3`）。</summary>
    public const string UID_SEPARATOR = "#";

    /// <summary>item_id → 数量。</summary>
    [Export]
    public Godot.Collections.Dictionary stacks { get; set; } = new();

    /// <summary>instance_id → `{base_id, affixes:[{affix_id, target, value_kind, value}]}`。</summary>
    [Export]
    public Godot.Collections.Dictionary equipment { get; set; } = new();

    /// <summary>char_id → `{slot_id: [instance_id]}`，定长数组，空位为 ""。</summary>
    [Export]
    public Godot.Collections.Dictionary equipped { get; set; } = new();

    [Export]
    public int money { get; set; }

    /// <summary>下一个装备实例序号。</summary>
    [Export]
    public int next_uid { get; set; } = 1;

    // ------------------------------------------------------------------ 物品堆叠

    public int count(string item_id) =>
        stacks.ContainsKey(item_id) ? stacks[item_id].AsInt32() : 0;

    public bool has(string item_id, int qty) => count(item_id) >= qty;

    public string[] item_ids()
    {
        var out_ids = new System.Collections.Generic.List<string>();
        foreach (Variant key in stacks.Keys)
        {
            string item_id = key.AsString();
            if (stacks[key].AsInt32() > 0)
            {
                out_ids.Add(item_id);
            }
        }

        out_ids.Sort(System.StringComparer.Ordinal);
        return out_ids.ToArray();
    }

    /// <summary>
    /// 放入物品。超过 stack_max 的部分会被截断并在 overflow 里报出来。
    /// 返回 `{ok, added, overflow, error}`。
    /// </summary>
    public Godot.Collections.Dictionary add_item(TableDb db, string item_id, int qty)
    {
        if (qty <= 0)
        {
            return _add_failure(0, 0, "数量必须大于 0");
        }

        if (db.get_row("equip_base", item_id) != null)
        {
            return _add_failure(0, 0, $"{item_id} 是装备，要用 add_equipment");
        }

        if (db.get_row("item_base", item_id) is not ItemBaseRow)
        {
            GD.PushError($"[Inventory] item_base 里没有 {item_id}");
            return _add_failure(0, 0, "这件东西不在配置里（数据错，已记进日志）");
        }

        int current = count(item_id);
        int added = System.Math.Min(qty, room_for(db, item_id));
        int overflow = qty - added;
        stacks[item_id] = current + added;
        return new Godot.Collections.Dictionary
        {
            { "ok", added > 0 },
            { "added", added },
            { "overflow", overflow },
            { "error", added > 0 ? "" : "背包已满" },
        };
    }

    /// <summary>
    /// 这一堆还能放下几个（`stack_max - 现有`）；表里没有这个物品返回 0。
    /// **`add_item` 与回购的「部分成交」都用它**：同一件事只算一次，免得两边对不上。
    /// </summary>
    public int room_for(TableDb db, string item_id)
    {
        if (db.get_row("item_base", item_id) is not ItemBaseRow row)
        {
            return 0;
        }

        return System.Math.Max(0, System.Math.Max(1, row.stack_max) - count(item_id));
    }

    /// <summary>
    /// 丢掉物品。钥匙道具（is_key_item=1）不允许丢弃——它们通常是隐藏内容的载体。
    /// 返回 `{ok, removed, error}`。
    /// </summary>
    public Godot.Collections.Dictionary remove_item(TableDb db, string item_id, int qty)
    {
        if (qty <= 0)
        {
            return _remove_failure(0, "数量必须大于 0");
        }

        if (db.get_row("item_base", item_id) is not ItemBaseRow row)
        {
            // 数据错：id 只进日志（决策 330）
            GD.PushError($"[Inventory] item_base 里没有 {item_id}（丢弃）");
            return _remove_failure(0, "这件东西不在配置里（数据错，已记进日志）");
        }

        if (row.is_key_item)
        {
            return _remove_failure(0, $"{row.name_cn} 是钥匙道具，不能丢弃");
        }

        int current = count(item_id);
        if (current < qty)
        {
            return _remove_failure(0, $"数量不足（有 {current}，需要 {qty}）");
        }

        stacks[item_id] = current - qty;
        if (stacks[item_id].AsInt32() <= 0)
        {
            stacks.Remove(item_id);
        }

        return new Godot.Collections.Dictionary { { "ok", true }, { "removed", qty }, { "error", "" } };
    }

    /// <summary>
    /// 消耗物品（研读秘籍、吃药这类正当用途）。
    /// 与 `remove_item` 的区别：**消耗不看 is_key_item**——钥匙道具不能「丢」，但可以被「用掉」。
    /// 返回 `{ok, removed, error}`。
    /// </summary>
    public Godot.Collections.Dictionary consume(string item_id, int qty)
    {
        if (qty <= 0)
        {
            return _remove_failure(0, "数量必须大于 0");
        }

        int current = count(item_id);
        if (current < qty)
        {
            return _remove_failure(0, $"数量不足（有 {current}，需要 {qty}）");
        }

        stacks[item_id] = current - qty;
        if (stacks[item_id].AsInt32() <= 0)
        {
            stacks.Remove(item_id);
        }

        return new Godot.Collections.Dictionary { { "ok", true }, { "removed", qty }, { "error", "" } };
    }

    // ------------------------------------------------------------------ 装备实例

    public string[] equipment_ids()
    {
        var out_ids = new System.Collections.Generic.List<string>();
        foreach (Variant key in equipment.Keys)
        {
            out_ids.Add(key.AsString());
        }

        out_ids.Sort(System.StringComparer.Ordinal);
        return out_ids.ToArray();
    }

    public int equipment_count() => equipment.Count;

    public bool has_equipment(string instance_id) => equipment.ContainsKey(instance_id);

    /// <summary>实例对应的本体 id（equip_base.equip_id），不存在返回空串。</summary>
    public string base_of(string instance_id)
    {
        Godot.Collections.Dictionary entry = _entry_of(instance_id);
        return entry.ContainsKey("base_id") ? entry["base_id"].AsString() : "";
    }

    /// <summary>
    /// 造一个装备实例，返回 instance_id；失败返回空串。
    /// affixes 由 AffixRoller 掷出来（掉落时生成；初始装备没有词条）。
    /// </summary>
    public string add_equipment(TableDb db, string base_id, Godot.Collections.Array affixes)
    {
        if (db.get_row("equip_base", base_id) == null)
        {
            GD.PushError($"[Inventory] equip_base 里没有 {base_id}");
            return "";
        }

        string instance_id = $"{base_id}{UID_SEPARATOR}{next_uid}";
        next_uid += 1;
        equipment[instance_id] = new Godot.Collections.Dictionary
        {
            { "base_id", base_id },
            { "affixes", affixes.Duplicate(true) },
        };
        return instance_id;
    }

    /// <summary>这件装备的随机词条（没有就是空数组）。</summary>
    public Godot.Collections.Array affixes_of(string instance_id)
    {
        Godot.Collections.Dictionary entry = _entry_of(instance_id);
        if (!entry.ContainsKey("affixes"))
        {
            return new Godot.Collections.Array();
        }

        Variant records = entry["affixes"];
        return records.VariantType == Variant.Type.Array
            ? records.AsGodotArray()
            : new Godot.Collections.Array();
    }

    /// <summary>删除未穿戴的实例；已穿戴的先卸下。返回 `{ok, error}`。</summary>
    public Godot.Collections.Dictionary remove_equipment(string instance_id)
    {
        if (!equipment.ContainsKey(instance_id))
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有这件装备" } };
        }

        if (equipped_by(instance_id).Count > 0)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "先卸下装备再处理" } };
        }

        equipment.Remove(instance_id);
        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" } };
    }

    /// <summary>这件装备穿在谁身上：返回 `{char_id, slot_id, index}`，没穿则空字典。</summary>
    public Godot.Collections.Dictionary equipped_by(string instance_id)
    {
        foreach (Variant char_key in equipped.Keys)
        {
            Godot.Collections.Dictionary slots = equipped[char_key].AsGodotDictionary();
            foreach (Variant slot_key in slots.Keys)
            {
                Godot.Collections.Array entries = slots[slot_key].AsGodotArray();
                for (int index = 0; index < entries.Count; index++)
                {
                    if (entries[index].AsString() == instance_id)
                    {
                        return new Godot.Collections.Dictionary
                        {
                            { "char_id", char_key.AsString() },
                            { "slot_id", slot_key.AsString() },
                            { "index", index },
                        };
                    }
                }
            }
        }

        return new Godot.Collections.Dictionary();
    }

    public bool is_equipped(string instance_id) => equipped_by(instance_id).Count > 0;

    // ------------------------------------------------------------------ 穿戴

    /// <summary>角色的槽位视图：slot_id → 定长数组（空位为 ""），按 `equip_slot_def.sort_order` 排列。</summary>
    public Godot.Collections.Dictionary equipment_slots(TableDb db, string char_id)
    {
        Godot.Collections.Dictionary slots = _slots_of(char_id);
        Godot.Collections.Dictionary capacities = _slot_capacities(db);
        var out_slots = new Godot.Collections.Dictionary();
        foreach (Variant slot_key in capacities.Keys)
        {
            string slot_id = slot_key.AsString();
            int capacity = capacities[slot_key].AsInt32();
            Godot.Collections.Array entries = slots.ContainsKey(slot_id)
                ? slots[slot_id].AsGodotArray()
                : new Godot.Collections.Array();
            var normalized = new Godot.Collections.Array();
            for (int index = 0; index < capacity; index++)
            {
                normalized.Add(index < entries.Count ? entries[index].AsString() : "");
            }

            out_slots[slot_id] = normalized;
        }

        return out_slots;
    }

    /// <summary>
    /// 当前配表认得的穿戴格子：slot_id → 容量（按 `equip_slot_def.sort_order` 排好）。
    ///
    /// **这是「配表认不认得」的唯一出处**：界面视图（`equipment_slots`）与加成／武器判定
    /// （`equipped_instances`）都从这里来，不许一边看配表、一边直接翻存档里的穿戴记录——
    /// 那正是「设计删了槽位／把 max_equip 改小，存档里那件还在偷偷加属性、界面却看不见」的成因。
    /// </summary>
    private Godot.Collections.Dictionary _slot_capacities(TableDb db)
    {
        var ordered =
            new System.Collections.Generic.List<(int SortOrder, int Index, EquipSlotDefRow Row)>();
        int index = 0;
        foreach (Variant entry in db.rows("equip_slot_def"))
        {
            if (entry.AsGodotObject() is EquipSlotDefRow slot)
            {
                ordered.Add((slot.sort_order, index, slot));
            }

            index += 1;
        }

        ordered.Sort((a, b) =>
            a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : a.Index.CompareTo(b.Index));
        var out_capacities = new Godot.Collections.Dictionary();
        foreach ((int SortOrder, int Index, EquipSlotDefRow Row) item in ordered)
        {
            string slot_id = item.Row.slot_id;
            if (slot_id.Length == 0)
            {
                continue;
            }

            out_capacities[slot_id] = System.Math.Max(1, item.Row.max_equip);
        }

        return out_capacities;
    }

    /// <summary>
    /// 把「当前配表装不下」的穿戴记录退回背包（只脱不删），返回退回来的实例 id。
    ///
    /// 什么时候会发生：设计改了 `equip_slot_def`——删掉一类槽位、或把 `max_equip` 改小（戒指 2 → 1），
    /// 而存档里还按旧配表穿着。改配表不该静默改变玩家属性，所以读档时取下来、放回背包。
    /// 一个槽位里保留靠前的格子（与界面显示的一致），其余退回；引用已不存在实例的记录直接清掉。
    /// </summary>
    public string[] reclaim_stranded_equipped(TableDb db, string char_id)
    {
        var reclaimed = new System.Collections.Generic.List<string>();
        Godot.Collections.Dictionary slots = _slots_of(char_id);
        if (slots.Count == 0)
        {
            return reclaimed.ToArray();
        }

        Godot.Collections.Dictionary capacities = _slot_capacities(db);
        bool changed = false;
        var slot_keys = new System.Collections.Generic.List<string>();
        foreach (Variant slot_key in slots.Keys)
        {
            slot_keys.Add(slot_key.AsString());
        }

        foreach (string slot_id in slot_keys)
        {
            Godot.Collections.Array entries = slots[slot_id].AsGodotArray();
            int capacity = capacities.ContainsKey(slot_id) ? capacities[slot_id].AsInt32() : 0;
            for (int index = 0; index < entries.Count; index++)
            {
                string instance_id = entries[index].AsString();
                if (instance_id.Length == 0)
                {
                    continue;
                }

                if (index < capacity && equipment.ContainsKey(instance_id))
                {
                    continue;
                }

                entries[index] = "";
                changed = true;
                if (equipment.ContainsKey(instance_id))
                {
                    reclaimed.Add(instance_id);
                }
            }

            if (entries.Count == 0 && capacity <= 0)
            {
                slots.Remove(slot_id);
                changed = true;
            }
        }

        if (changed)
        {
            equipped[char_id] = slots;
        }

        return reclaimed.ToArray();
    }

    /// <summary>
    /// 装备一件背包里的装备。校验：角色等级 ≥ level_req；武器槽只能装模板武器类型。
    /// 槽位有空位就放空位，满了替换第一格并把被替换的实例 id 放在 replaced 里。
    /// 返回 `{ok, error, slot_id, index, replaced}`。
    /// </summary>
    public Godot.Collections.Dictionary equip(
        TableDb db, string char_id, int level, string weapon_type, string instance_id)
    {
        Godot.Collections.Dictionary check = can_equip(db, char_id, level, weapon_type, instance_id);
        if (!check["ok"].AsBool())
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "error", check["error"] },
                { "slot_id", check["slot_id"] },
                { "index", -1 },
                { "replaced", "" },
            };
        }

        // 同一件装备不能同时在两处：先脱下
        Godot.Collections.Dictionary owner = equipped_by(instance_id);
        if (owner.Count > 0)
        {
            _clear_slot(owner["char_id"].AsString(), owner["slot_id"].AsString(), owner["index"].AsInt32());
        }

        string slot_id = check["slot_id"].AsString();
        int capacity = check["capacity"].AsInt32();
        Godot.Collections.Dictionary slots = _slots_of(char_id);
        Godot.Collections.Array entries = slots.ContainsKey(slot_id)
            ? slots[slot_id].AsGodotArray()
            : new Godot.Collections.Array();
        while (entries.Count < capacity)
        {
            entries.Add("");
        }

        int index = entries.IndexOf("");
        string replaced = "";
        if (index < 0)
        {
            index = 0;
            replaced = entries[index].AsString();
        }

        entries[index] = instance_id;
        slots[slot_id] = entries;
        equipped[char_id] = slots;
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "error", "" },
            { "slot_id", slot_id },
            { "index", index },
            { "replaced", replaced },
        };
    }

    /// <summary>能不能装（不改状态），给界面用来置灰与写原因。返回 `{ok, error, slot_id, capacity}`。</summary>
    public Godot.Collections.Dictionary can_equip(
        TableDb db, string char_id, int level, string weapon_type, string instance_id)
    {
        if (!equipment.ContainsKey(instance_id))
        {
            return _equip_check(false, "背包里没有这件装备", "", 0);
        }

        if (db.get_row("equip_base", base_of(instance_id)) is not EquipBaseRow equip)
        {
            return _equip_check(false, "装备本体不存在", "", 0);
        }

        if (equip.level_req > level)
        {
            return _equip_check(false, $"{equip.name_cn} 需要 {equip.level_req} 级", "", 0);
        }

        string slot_id = equip.slot;
        if (db.get_row("equip_slot_def", slot_id) is not EquipSlotDefRow slot_row)
        {
            return _equip_check(false, $"槽位 {slot_id} 未定义", "", 0);
        }

        string weapon = equip.weapon_type;
        if (slot_row.is_weapon_slot() && weapon_type.Length > 0 && weapon != weapon_type)
        {
            return _equip_check(
                false,
                $"{equip.name_cn} 是{equip.weapon_type}，本角色只能用{_weapon_type_name(db, weapon_type)}",
                slot_id,
                slot_row.max_equip);
        }

        return _equip_check(true, "", slot_id, System.Math.Max(1, slot_row.max_equip));
    }

    /// <summary>卸下某个槽位格子的装备。返回 `{ok, error, instance_id}`。</summary>
    public Godot.Collections.Dictionary unequip(string char_id, string slot_id, int index)
    {
        Godot.Collections.Dictionary slots = _slots_of(char_id);
        Godot.Collections.Array entries = slots.ContainsKey(slot_id)
            ? slots[slot_id].AsGodotArray()
            : new Godot.Collections.Array();
        if (index < 0 || index >= entries.Count || entries[index].AsString().Length == 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "error", "这一格没有装备" },
                { "instance_id", "" },
            };
        }

        string instance_id = entries[index].AsString();
        _clear_slot(char_id, slot_id, index);
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "error", "" },
            { "instance_id", instance_id },
        };
    }

    private void _clear_slot(string char_id, string slot_id, int index)
    {
        Godot.Collections.Dictionary slots = _slots_of(char_id);
        Godot.Collections.Array entries = slots.ContainsKey(slot_id)
            ? slots[slot_id].AsGodotArray()
            : new Godot.Collections.Array();
        if (index >= 0 && index < entries.Count)
        {
            entries[index] = "";
        }

        slots[slot_id] = entries;
        equipped[char_id] = slots;
    }

    // ------------------------------------------------------------------ 派生加成

    /// <summary>产出 AttributeCalculator 的贡献列表：装备的 attr_* 走属性点层，bonus_* 走固定值层。</summary>
    public Godot.Collections.Array contributions_for(TableDb db, string char_id)
    {
        var out_contributions = new Godot.Collections.Array();
        // 一个 BuffService 用完这一次调用：它内部的 grants／stat_mods 缓存按调用生效，
        // 免得每件装备各建一个实例、各扫一遍 buff_grant（性能观测里量到过这条）
        BuffService buff_service = BuffService.create(db);
        foreach (string instance_id in _equipped_instances(db, char_id))
        {
            out_contributions.AddRange(
                equipment_contributions(db, base_of(instance_id), instance_id, buff_service));
            // 随机词条走同一条贡献通道：属性点层与固定值层在 AffixRoller 里就分好了
            out_contributions.AddRange(AffixRoller.contributions_of(db, affixes_of(instance_id)));
        }

        return out_contributions;
    }

    /// <summary>
    /// **一件装备本体**的贡献：`equip_base` 的 `attr_*`（属性点层）与 `bonus_*`（固定值层）两列，
    /// 外加它自带的**常驻** buff（`buff_grant.trigger=on_equip` 且 `duration=0`；设计 08「装备即生效」）。
    ///
    /// 角色（背包里的实例）与**敌人**（`enemy_equip` 配表）共用这一处：设计 10 §五写着
    /// 「敌人属性 = 七维 + 等级 + 装备加成，与角色完全同一条管线」。
    /// `source` 只是贡献列表里的溯源标签（角色传实例 id、敌人传 `enemy_equip:&lt;装备 id&gt;`）。
    /// **四个实参写全**（坑②：可选参数对 GDScript 不生效）。
    /// </summary>
    public static Godot.Collections.Array equipment_contributions(
        TableDb db, string equip_id, string source, BuffService service)
    {
        if (db.get_row("equip_base", equip_id) is not EquipBaseRow equip)
        {
            return new Godot.Collections.Array();
        }

        string tag = source.Length > 0 ? source : equip_id;
        BuffService buff_service = service ?? BuffService.create(db);
        var out_contributions = new Godot.Collections.Array();
        Godot.Collections.Dictionary attr_bonuses = equip.attr_bonuses();
        foreach (Variant attr_key in attr_bonuses.Keys)
        {
            int value = attr_bonuses[attr_key].AsInt32();
            if (value != 0)
            {
                out_contributions.Add(new Godot.Collections.Dictionary
                {
                    { "kind", AttributeCalculator.contrib_attr_point() },
                    { "target", attr_key.AsString() },
                    { "value", value },
                    { "source", tag },
                });
            }
        }

        Godot.Collections.Dictionary stat_bonuses = equip.stat_bonuses();
        foreach (Variant stat_key in stat_bonuses.Keys)
        {
            double value = stat_bonuses[stat_key].AsDouble();
            if (value != 0.0)
            {
                out_contributions.Add(new Godot.Collections.Dictionary
                {
                    { "kind", AttributeCalculator.contrib_stat_flat() },
                    { "target", stat_key.AsString() },
                    { "value", value },
                    { "source", tag },
                });
            }
        }

        out_contributions.AddRange(_equip_buff_contributions(buff_service, equip_id, tag));
        return out_contributions;
    }

    /// <summary>
    /// 某件装备本体发的常驻 buff 的贡献。
    /// `source` 写成 `buff:&lt;buff_id&gt;@&lt;装备实例 id&gt;`：既能追溯是哪条 buff、哪一件装备发的，
    /// 又和「装备自己的 bonus_* 列」（source = 实例 id）区分开——面板与用例都能分开看。
    /// </summary>
    private static Godot.Collections.Array _equip_buff_contributions(
        BuffService service, string base_id, string instance_id)
    {
        var out_contributions = new Godot.Collections.Array();
        foreach (Variant grant_entry in service.persistent_grants_for("equip", base_id))
        {
            string buff_id = grant_entry.AsGodotObject() is BuffGrantRow grant ? grant.buff_id : "";
            // 装备的常驻 buff 不叠层，stacks 传 1
            out_contributions.AddRange(
                service.contributions_of(buff_id, $"buff:{buff_id}@{instance_id}", 1));
        }

        return out_contributions;
    }

    /// <summary>某角色身上**当前配表认得的**已穿戴实例 id（不含空位，也不含配表装不下的那份记录）。</summary>
    private string[] _equipped_instances(TableDb db, string char_id)
    {
        var out_ids = new System.Collections.Generic.List<string>();
        Godot.Collections.Dictionary slots = _slots_of(char_id);
        Godot.Collections.Dictionary capacities = _slot_capacities(db);
        foreach (Variant slot_key in capacities.Keys)
        {
            string slot_id = slot_key.AsString();
            Godot.Collections.Array entries = slots.ContainsKey(slot_id)
                ? slots[slot_id].AsGodotArray()
                : new Godot.Collections.Array();
            int count = System.Math.Min(capacities[slot_key].AsInt32(), entries.Count);
            for (int index = 0; index < count; index++)
            {
                string instance_id = entries[index].AsString();
                if (instance_id.Length > 0 && equipment.ContainsKey(instance_id))
                {
                    out_ids.Add(instance_id);
                }
            }
        }

        return out_ids.ToArray();
    }

    /// <summary>同上，给界面／战斗构造用（需要 db 才能知道槽位容量）。</summary>
    public string[] equipped_instances(TableDb db, string char_id) => _equipped_instances(db, char_id);

    private string _weapon_type_name(TableDb db, string weapon_type)
    {
        return db.get_row("weapon_type_def", weapon_type) is WeaponTypeDefRow row
            ? row.name_cn
            : weapon_type;
    }

    // ------------------------------------------------------------------ 序列化

    public Godot.Collections.Dictionary to_dict() => new()
    {
        { "stacks", stacks.Duplicate(false) },
        { "equipment", equipment.Duplicate(true) },
        { "equipped", equipped.Duplicate(true) },
        { "money", money },
        { "next_uid", next_uid },
    };

    public static Inventory from_dict(Variant data)
    {
        var inventory = new Inventory();
        if (data.VariantType != Variant.Type.Dictionary)
        {
            return inventory;
        }

        Godot.Collections.Dictionary dictionary = data.AsGodotDictionary();
        if (dictionary.ContainsKey("stacks")
            && dictionary["stacks"].VariantType == Variant.Type.Dictionary)
        {
            Godot.Collections.Dictionary raw_stacks = dictionary["stacks"].AsGodotDictionary();
            foreach (Variant item_key in raw_stacks.Keys)
            {
                inventory.stacks[item_key.AsString()] = raw_stacks[item_key].AsInt32();
            }
        }

        if (dictionary.ContainsKey("equipment")
            && dictionary["equipment"].VariantType == Variant.Type.Dictionary)
        {
            Godot.Collections.Dictionary raw_equipment = dictionary["equipment"].AsGodotDictionary();
            foreach (Variant instance_key in raw_equipment.Keys)
            {
                if (raw_equipment[instance_key].VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                Godot.Collections.Dictionary entry = raw_equipment[instance_key].AsGodotDictionary();
                Variant affixes = entry.ContainsKey("affixes")
                    ? entry["affixes"]
                    : default;
                inventory.equipment[instance_key.AsString()] = new Godot.Collections.Dictionary
                {
                    { "base_id", entry.ContainsKey("base_id") ? entry["base_id"].AsString() : "" },
                    {
                        "affixes",
                        affixes.VariantType == Variant.Type.Array
                            ? affixes.AsGodotArray().Duplicate(true)
                            : new Godot.Collections.Array()
                    },
                };
            }
        }

        if (dictionary.ContainsKey("equipped")
            && dictionary["equipped"].VariantType == Variant.Type.Dictionary)
        {
            Godot.Collections.Dictionary raw_equipped = dictionary["equipped"].AsGodotDictionary();
            foreach (Variant char_key in raw_equipped.Keys)
            {
                if (raw_equipped[char_key].VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                Godot.Collections.Dictionary slots_data = raw_equipped[char_key].AsGodotDictionary();
                var slots = new Godot.Collections.Dictionary();
                foreach (Variant slot_key in slots_data.Keys)
                {
                    if (slots_data[slot_key].VariantType != Variant.Type.Array)
                    {
                        continue;
                    }

                    var normalized = new Godot.Collections.Array();
                    foreach (Variant entry in slots_data[slot_key].AsGodotArray())
                    {
                        normalized.Add(entry.AsString());
                    }

                    slots[slot_key.AsString()] = normalized;
                }

                inventory.equipped[char_key.AsString()] = slots;
            }
        }

        inventory.money = dictionary.ContainsKey("money") ? dictionary["money"].AsInt32() : 0;
        int fallback_uid = inventory.equipment.Count + 1;
        inventory.next_uid = System.Math.Max(
            1,
            dictionary.ContainsKey("next_uid") ? dictionary["next_uid"].AsInt32() : fallback_uid);
        return inventory;
    }

    private Godot.Collections.Dictionary _slots_of(string char_id) =>
        equipped.ContainsKey(char_id)
            ? equipped[char_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();

    private Godot.Collections.Dictionary _entry_of(string instance_id) =>
        equipment.ContainsKey(instance_id)
            ? equipment[instance_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();

    private static Godot.Collections.Dictionary _add_failure(int added, int overflow, string error) =>
        new()
        {
            { "ok", false },
            { "added", added },
            { "overflow", overflow },
            { "error", error },
        };

    private static Godot.Collections.Dictionary _remove_failure(int removed, string error) =>
        new() { { "ok", false }, { "removed", removed }, { "error", error } };

    private static Godot.Collections.Dictionary _equip_check(
        bool ok, string error, string slot_id, int capacity) =>
        new()
        {
            { "ok", ok },
            { "error", error },
            { "slot_id", slot_id },
            { "capacity", capacity },
        };
}
