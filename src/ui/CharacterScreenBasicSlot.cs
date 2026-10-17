using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `CharacterScreen` 武学页那一屏的**「普通攻击」配置区**（设计 04 §武学页三块玩家配置，2026-10-13）
/// ——从 `CharacterScreenSkill.cs` 拆出来的 partial（那一份顶到了 800 行硬上限，
/// 这一块按职责整块搬出来，**只挪位置不改行为**）：固定 1 格、空＝系统普攻、
/// 装上已学的免费招之后那一招就是"普攻"（吃 `extra_hit`、计入使用记录、受武器类型限制）。
/// </summary>
public partial class CharacterScreen
{
    /// <summary>
    /// 「普通攻击」配置区（设计 04 §武学页三块玩家配置，2026-10-13）：**固定 1 格**。
    /// 空着＝照旧用系统普攻（`__basic_attack__`，不落表）；装上**已学的免费招**
    /// （`qi_cost = 0` 且 `cooldown = 0`）之后，那一招就是"普攻"：吃 `extra_hit`、计入使用记录、
    /// 受武器类型限制，而且与招式位互斥。
    ///
    /// 候选只列**免费招**——这不是过滤掉信息，而是这个格子的定义（设计原文就是那一档）；
    /// 装不上的每一枚都**把原因写在按钮里**（武器不符／已在招式位），与别处的口径一致。
    /// </summary>
    private Control _basic_attack_slot(CharacterSheet panel)
    {
        var box = new VBoxContainer { Name = "BasicBox" };
        box.AddThemeConstantOverride("separation", 4);
        SkillLoadout loadout = panel.loadout();
        string current = loadout != null ? loadout.basic_id() : "";
        box.AddChild(_section(
            "BasicSection", "BasicHeader", "普通攻击", current.Length > 0 ? "固定 1 格 · 已装" : "固定 1 格 · 空"));

        string current_text = current.Length > 0
            ? $"当前：{_item_name(current)}（装上之后吃段数加成、计入招式使用记录）"
            : "当前：空 —— 照旧用系统普攻（不看武器、不吃段数、不进记录）";
        box.AddChild(_make_label("BasicCurrent", current_text));
        Button clear = _make_button(
            "BasicClear", "卸下（回到系统普攻）",
            () => { _set_basic_attack(""); }, current.Length == 0);
        if (current.Length == 0)
        {
            clear.TooltipText = "本来就是空的（系统普攻）";
        }

        box.AddChild(clear);

        var candidates = new HBoxContainer { Name = "BasicCandidates" };
        candidates.AddThemeConstantOverride("separation", 6);
        foreach (Variant row_value in panel.skill_rows())
        {
            Godot.Collections.Dictionary row = row_value.AsGodotDictionary();
            if (_s(row, "kind") != "active" || _i(row, "qi_cost") != 0 || _i(row, "cooldown") != 0)
            {
                continue;
            }

            string skill_id = _s(row, "skill_id");
            Godot.Collections.Dictionary gate = loadout != null
                ? loadout.can_set_basic(skill_id)
                : new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有可操作的角色" } };
            string reason = _s(gate, "error");
            Button pick = _make_button(
                $"BasicPick{skill_id}",
                skill_id == current ? "已装" : $"装 {_s(row, "name")}",
                () => { _set_basic_attack(skill_id); },
                skill_id == current || !_b(gate, "ok"));
            if (skill_id != current && reason.Length > 0)
            {
                pick.TooltipText = reason;
            }

            candidates.AddChild(pick);
        }

        box.AddChild(candidates);
        return box;
    }

    /// <summary>装修／卸下普攻位（空串＝卸下）；装不上就把原因播报出来。</summary>
    private void _set_basic_attack(string skill_id)
    {
        CharacterSheet panel = sheet();
        SkillLoadout loadout = panel?.loadout();
        if (loadout == null)
        {
            return;
        }

        Godot.Collections.Dictionary result = loadout.set_basic(skill_id);
        _set_status(_b(result, "ok")
            ? (skill_id.Length > 0
                ? $"普通攻击换成「{_item_name(skill_id)}」"
                : "普通攻击回到系统普攻")
            : _s(result, "error"));
        refresh();
    }

    /// <summary>
}

