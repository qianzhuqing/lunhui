using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 道具的**效果落地**（`item_base` 的 `heal_hp`／`heal_qi`／`clear_status` 三列，Q33 2026-10-13）。
///
/// 这三列到之前，效果只写在 `desc` 那一句中文里（「非战斗恢复气血120」），代码**不解析中文、
/// 也不自己编数**——所以旅囊里的「使用」一直置灰、战斗里那六指令的「道具」也一样。
/// 现在数值有列了，这里就是**唯一的读取端**：战斗外用（行囊的「道具使用 · 选人」弹窗）与
/// 战斗内用两条路都走它，免得出现「屏上写回 120、实际回 60」那种**不会报错**的错。
///
/// **不解状态不做通配**：`clear_status` 填的是一行 `status_effect.status_id`，空＝不解——
/// 设计明写「一个值一个意思」，没有"全解"。
/// </summary>
public static class ItemUse
{
    /// <summary>
    /// 战斗外用一件（`use_context = field`）。返回
    /// `{ok, error, hp_before, hp_after, healed, cleared, lines}`。
    /// **扣物品只在真的有用之后**（三列全空、或不是 field 那两类，一件都不扣）。
    /// </summary>
    public static Godot.Collections.Dictionary use_field(
        TableDb db, GodotObject state, string char_id, string item_id)
    {
        var fail = new Godot.Collections.Dictionary { { "ok", false }, { "error", "" } };
        if (db == null || state == null || char_id.Length == 0)
        {
            fail["error"] = "没有会话状态";
            return fail;
        }

        if (db.get_row("item_base", item_id) is not ItemBaseRow row)
        {
            fail["error"] = "表里查不到这件东西";
            return fail;
        }

        Inventory inventory = state.Get("inventory").As<Inventory>();
        if (inventory == null || inventory.count(item_id) <= 0)
        {
            fail["error"] = $"背包里没有{row.name_cn}了";
            return fail;
        }

        if (row.use_context != "field")
        {
            fail["error"] = row.use_context.Length > 0
                ? "这件要在战斗里用（非战斗用不上）"
                : "这件没有可用的动作";
            return fail;
        }

        // 效果数值列全空＝**如实说一句**，别扣了东西什么都没发生（那正是"点了没反应"）。
        // `clear_status` 那一条**在战斗外用不上**（战斗内的异常出了战斗就清，用户 2026-10-13 口径），
        // 所以外场只看回气血／回内力两列。
        if (row.heal_hp <= 0 && row.heal_qi <= 0)
        {
            fail["error"] = row.clear_status.Length > 0
                ? "这件是战斗里解状态用的（非战斗用不上——战斗内的异常出了战斗就清）"
                : "这件道具还没配效果（战斗外能用的两列都是空的）";
            return fail;
        }

        CharacterSheet sheet = CharacterSheet.create(db, state, char_id);
        if (sheet == null)
        {
            fail["error"] = "没有这个角色";
            return fail;
        }

        var lines = new Godot.Collections.Array();
        int before_hp = sheet.current_hp();
        int max_hp = sheet.max_hp();
        int after_hp = before_hp;
        if (row.heal_hp > 0)
        {
            after_hp = System.Math.Min(max_hp, before_hp + row.heal_hp);
            if (after_hp > before_hp)
            {
                state.Call("set_current_hp", char_id, after_hp);
            }

            lines.Add(after_hp > before_hp
                ? $"气血 {before_hp} → {after_hp}（+{after_hp - before_hp}）"
                : "气血是满的，这一口没回上去");
        }

        // 内力：**战斗外没有这一层**（气是每场战斗的，开场即满）——如实说一句，别假装回了
        if (row.heal_qi > 0)
        {
            lines.Add($"内力 {row.heal_qi} 点在战斗外用不上（内力每场开场即满）");
        }

        inventory.consume(item_id, 1);
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "error", "" },
            { "hp_before", before_hp },
            { "hp_after", after_hp },
            { "healed", System.Math.Max(0, after_hp - before_hp) },
            { "lines", lines },
        };
    }

    /// <summary>
    /// 战斗外的**目标预览**：这一口会回多少（气血按上限截断）、会解掉哪一条。
    /// 与 `use_field()` 同一处口径——弹窗上写的就是真扣下去的结果（清单 §二-3 那一行）。
    /// 返回 `{hp_before, hp_after, healed, clear_status, clear_name, clear_needed}`。
    /// </summary>
    public static Godot.Collections.Dictionary preview_field(
        TableDb db, GodotObject state, string char_id, ItemBaseRow row, int current_hp, int max_hp)
    {
        int after_hp = row != null && row.heal_hp > 0
            ? System.Math.Min(max_hp, current_hp + row.heal_hp)
            : current_hp;
        return new Godot.Collections.Dictionary
        {
            { "hp_before", current_hp },
            { "hp_after", after_hp },
            { "healed", System.Math.Max(0, after_hp - current_hp) },
        };
    }
}
