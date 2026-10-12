using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// `CharacterScreen` 的写操作与几个查询小口——从 `CharacterScreen.cs` 拆出来的 partial
/// （那份要守 800 行硬上限）。
///
/// 每个写操作都是「转发给 `CharacterSheet`／`Inventory` → 把结果写成一句状态 → 重建页签」，
/// 面板自己不记第二份账。
/// </summary>
public partial class CharacterScreen
{
    /// <summary>加一点，返回 `{ok, error, remaining}`</summary>
    public Godot.Collections.Dictionary press_plus(string attr_id)
    {
        CharacterSheet panel = sheet();
        if (panel == null)
        {
            _set_status("没有可操作的角色");
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", "没有可操作的角色" }, { "remaining", 0 },
            };
        }

        Godot.Collections.Dictionary result = panel.spend_point(attr_id);
        _set_status(_b(result, "ok") ? $"加点成功：{panel.attribute_name(attr_id)} +1" : _s(result, "error"));
        refresh();
        return result;
    }

    /// <summary>选中某个槽位，列出可换的装备</summary>
    public void press_slot(string slot_id, int index)
    {
        _selected_slot = $"{slot_id}|{index}";
        // 换了槽位，上一件候选就不是「这个槽位的候选」了——选中态一起清掉
        _selected_candidate = "";
        _set_status("选择要装上的装备");
        refresh();
    }

    /// <summary>
    /// 在「可换」列表里选中一件候选（清单 §二-5：点一行＝选中候选）。**只改选中态**——
    /// 真穿上去是那一行／详情里那枚「换上」按钮（`equip_instance`），这样玩家可以先看
    /// 「换上之后」与详情再决定。选中的那件不在列表里了（被穿上／卖掉／换人）时会留空。
    /// </summary>
    public void press_candidate(string instance_id)
    {
        _selected_candidate = instance_id;
        refresh();
    }

    /// <summary>把背包里的装备穿上</summary>
    public Godot.Collections.Dictionary equip_instance(string instance_id)
    {
        GodotObject st = current_state();
        CharacterSheet panel = sheet();
        if (st == null || panel == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有可操作的角色" } };
        }

        Inventory inventory = st.Get("inventory").As<Inventory>();
        Godot.Collections.Dictionary result = inventory.equip(
            Db, panel.char_id, panel.level, panel.weapon_type_id(), instance_id);
        _set_status(_b(result, "ok") ? $"{_item_name(instance_id)} 已装上" : _s(result, "error"));
        refresh();
        return result;
    }

    /// <summary>卸下某格装备</summary>
    public Godot.Collections.Dictionary unequip(string slot_id, int index)
    {
        GodotObject st = current_state();
        CharacterSheet panel = sheet();
        if (st == null || panel == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有可操作的角色" } };
        }

        Inventory inventory = st.Get("inventory").As<Inventory>();
        Godot.Collections.Dictionary result = inventory.unequip(panel.char_id, slot_id, index);
        _set_status(_b(result, "ok") ? $"{_item_name(_s(result, "instance_id"))} 已卸下" : _s(result, "error"));
        refresh();
        return result;
    }

    public void set_filter(string kind)
    {
        _filter = kind;
        refresh();
    }

    /// <summary>
    /// 这一行物品属于当前筛选吗。
    ///
    /// 「钥匙」这一类按设计 05 的**语义**判：钥匙道具是隐藏内容的载体，也就是 `is_key_item`——
    /// 不是 `item_type == "key"`。05 的钥匙道具清单里就有铁镐，而它的 `item_type` 是 `tool`，
    /// 以前它哪个分类都进不去，玩家只能在「全部」里翻（数据侧要不要把 item_type 并进 key 已记在缺口表）。
    /// </summary>
    private bool _row_matches(string kind, ItemBaseRow row)
    {
        if (kind == "all")
        {
            return true;
        }

        return kind == "key" ? row.is_key_item : row.item_type == kind;
    }

    /// <summary>装／卸一部武学（招式进招式槽，内功占容量格）</summary>
    public Godot.Collections.Dictionary toggle_skill(string skill_id)
    {
        CharacterSheet panel = sheet();
        if (panel == null)
        {
            _set_status("没有可操作的角色");
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", "没有可操作的角色" }, { "equipped", false },
            };
        }

        Godot.Collections.Dictionary result = panel.loadout().toggle(skill_id);
        string name = _skill_name(skill_id);
        if (_b(result, "ok"))
        {
            _set_status($"已{(_b(result, "equipped") ? "装配" : "卸下")}「{name}」");
        }
        else
        {
            _set_status($"「{name}」{_s(result, "error")}");
        }

        refresh();
        return result;
    }

    private string _skill_name(string skill_id)
    {
        var row = Db.get_row("skill_base", skill_id) as SkillBaseRow;
        return row != null ? row.name_cn : skill_id;
    }

    /// <summary>研读秘籍（item 来源的一次性武学）。门槛不够或已经学过都不消耗。</summary>
    public Godot.Collections.Dictionary study_item(string item_id)
    {
        GodotObject st = current_state();
        if (st == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有会话状态" } };
        }

        Godot.Collections.Dictionary result = SkillGrant.study(Db, st, item_id);
        if (_b(result, "ok"))
        {
            _set_status($"研读《{_item_name(item_id)}》：{_learned_names(result)}");
        }
        else
        {
            _set_status($"《{_item_name(item_id)}》{_s(result, "error")}");
        }

        refresh();
        return result;
    }

    /// <summary>装备实例 id（`eq_sword_01#1`）也走这里：`TableDb.display_name` 会剥掉 #序号</summary>
    private string _item_name(string item_id) => Db.display_name(item_id);

    private static string _learned_names(Godot.Collections.Dictionary result)
    {
        var names = new System.Collections.Generic.List<string>();
        Godot.Collections.Array entries = result.ContainsKey("entries")
            ? result["entries"].AsGodotArray()
            : new Godot.Collections.Array();
        foreach (Variant entry_value in entries)
        {
            Godot.Collections.Dictionary entry = entry_value.AsGodotDictionary();
            string[] learned = entry.ContainsKey("learned")
                ? entry["learned"].AsStringArray()
                : System.Array.Empty<string>();
            if (learned.Length > 0)
            {
                names.Add($"学会「{_s(entry, "name")}」");
            }
        }

        if (names.Count == 0)
        {
            return "没有新学会的武学";
        }

        string text = string.Join("、", names);
        if (_s(result, "error").Length > 0)
        {
            text += $"（{_s(result, "error")}）";
        }

        return text;
    }

    /// <summary>丢弃物品（钥匙道具会被拒绝）</summary>
    public Godot.Collections.Dictionary drop_item(string item_id)
    {
        GodotObject st = current_state();
        if (st == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有会话状态" } };
        }

        Inventory inventory = st.Get("inventory").As<Inventory>();
        Godot.Collections.Dictionary result = inventory.remove_item(Db, item_id, 1);
        _set_status(_b(result, "ok") ? "已丢弃 1 个" : _s(result, "error"));
        refresh();
        return result;
    }

    private string _rarity_color(string rarity_id)
    {
        Resource row = Db.get_row("rarity_def", rarity_id);
        return row != null ? row.Get("color").AsString() : "#FFFFFF";
    }

    private void _set_status(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
        }
    }
}
