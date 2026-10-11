using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `CreationScreen` 的真实场景自检（`-- --creation-selftest`）：模板路 → 自建路 → 非法方案被拒
/// → 天赋点数上限。从 `CreationScreen.cs` 拆出来的 partial（那边要守 800 行硬上限）。
/// 自检条数的棘轮表盯着**本文件**（见 `tests/TestHandshakeFloors.cs` 的 `VerificationCheckFloors`）。
/// </summary>
public partial class CreationScreen
{
    // ------------------------------------------------------------------ 自检

    private void _run_creation_selftest()
    {
        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        SaveStore store = SaveStore.create("res://.logs/creation_selftest", SaveStore.default_slot_count());
        // 上一轮留下的存档会让「创建成功」这条断言变成时红时绿——先清干净
        for (int slot = 1; slot <= store.slot_count; slot++)
        {
            DirAccess.RemoveAbsolute(store.slot_path(slot));
        }

        store_override = store;
        var switched = new Godot.Collections.Array();
        scene_switch_handler = Callable.From((string path) => switched.Add(path));

        // ① 模板路：选出身 → 确认
        select_route(ROUTE_ORIGIN);
        Godot.Collections.Array cards = CreationService.origins((TableDb)db);
        ok = ok && cards.Count > 0;
        lines.Add($"出身卡 {cards.Count} 张（{_s(cards[0].AsGodotDictionary(), "name_cn")}…）");
        pick_origin(_s(cards[0].AsGodotDictionary(), "origin_id"));
        // 立绘位（设计 0.19.1）：右侧 320×480，选了出身就有占位图（美术出图后同名替换）
        TextureRect portrait = FindChild("Portrait", true, false) as TextureRect;
        bool portrait_ok = portrait != null
            && portrait.CustomMinimumSize == new Vector2(320, 480)
            && portrait.Texture != null;
        ok = ok && portrait_ok;
        lines.Add($"立绘位 320×480 且跟着出身走={_bool(portrait_ok)}（{portrait_path()}）");
        // 默认名（设计 21 §七 第 5 条）：名字那一格预填的是**真名**（不是卡的标题）
        LineEdit name_edit = FindChild("NameEdit", true, false) as LineEdit;
        string default_name = CreationService.default_name_of(
            (TableDb)db, _s(cards[0].AsGodotDictionary(), "origin_id"));
        bool prefill_ok = name_edit != null && default_name.Length > 0 && name_edit.Text == default_name;
        ok = ok && prefill_ok;
        lines.Add($"名字格预填默认名「{default_name}」={_bool(prefill_ok)}");
        // 「主角可改」：改成别的名字 → 创建后**从存档里读回来的是改过的那个**
        const string typed_name = "王二";
        if (name_edit != null)
        {
            name_edit.Text = typed_name;
        }

        Godot.Collections.Dictionary created = confirm();
        ok = ok && _b(created, "ok");
        lines.Add($"模板路创建 ok={_bool(_b(created, "ok"))}（槽 {_i(created, "slot")}）");
        ok = ok && switched.Count == 1 && switched[0].AsString() == HUB_SCENE;
        Godot.Collections.Dictionary loaded = store.load_slot(_i(created, "slot", 1), (TableDb)db);
        string saved_name = "";
        GodotObject loaded_state = _b(loaded, "ok") ? loaded["state"].AsGodotObject() : null;
        if (loaded_state != null)
        {
            string[] loaded_ids = loaded_state.Get("char_ids").AsStringArray();
            if (loaded_ids.Length > 0)
            {
                saved_name = loaded_state.Call("char_name", db, loaded_ids[0]).AsString();
            }
        }

        bool name_saved = saved_name == typed_name;
        ok = ok && name_saved;
        lines.Add($"改名「{typed_name}」写进存档={_bool(name_saved)}（读回来：{saved_name}）");

        // ② 自建路：分满七维 → 选武器 → 选武学 → 确认
        switched.Clear();
        select_route(ROUTE_CUSTOM);
        Godot.Collections.Dictionary rules = CreationService.attr_rules((TableDb)db);
        Godot.Collections.Dictionary attrs = new();
        int left = _i(rules, "total") - _i(rules, "min") * ATTR_ORDER.Length;
        foreach (string attr_id in ATTR_ORDER)
        {
            attrs[attr_id] = _i(rules, "min");
        }

        // 按「每个属性最多加到 create_attr_max」把余量分完（一次加满 str 会越过单项上限）
        int headroom = _i(rules, "max") - _i(rules, "min");
        foreach (string attr_id in ATTR_ORDER)
        {
            if (left <= 0)
            {
                break;
            }

            int add = System.Math.Min(left, headroom);
            attrs[attr_id] = attrs[attr_id].AsInt32() + add;
            left -= add;
        }

        spec["attrs"] = attrs;
        Godot.Collections.Array weapons_list = CreationService.weapons((TableDb)db);
        ok = ok && weapons_list.Count > 0;
        pick_weapon(_s(weapons_list[0].AsGodotDictionary(), "weapon_type"));
        Godot.Collections.Array options =
            CreationService.start_skill_options((TableDb)db, _s(spec, "weapon_type"));
        ok = ok && options.Count > 0;
        if (options.Count > 0)
        {
            pick_start_skill(_s(options[0].AsGodotDictionary(), "skill_id"));
        }

        Godot.Collections.Dictionary custom = confirm();
        ok = ok && _b(custom, "ok");
        lines.Add($"自建路创建 ok={_bool(_b(custom, "ok"))}"
            + $"（{_s(custom, "error", $"七维总和 {_i(rules, "total")}")}）");
        ok = ok && switched.Count == 1 && switched[0].AsString() == HUB_SCENE;

        // ③ 非法方案必须被挡住（七维总和不对）
        switched.Clear();
        Godot.Collections.Dictionary bad = (Godot.Collections.Dictionary)attrs.Duplicate(true);
        bad["str"] = bad["str"].AsInt32() + 1;
        spec["attrs"] = bad;
        Godot.Collections.Dictionary refused = confirm();
        ok = ok && !_b(refused, "ok") && switched.Count == 0;
        lines.Add($"七维总和不对时被拒={_bool(!_b(refused, "ok"))}");

        // ④ 天赋点数上限：把 24 个天赋全选一遍，花完点之后必须拒绝
        select_route(ROUTE_ORIGIN);
        pick_origin(_s(cards[0].AsGodotDictionary(), "origin_id"));
        int spent = 0;
        foreach (Variant card_variant in TalentService.cards((TableDb)db))
        {
            int before = TalentService.remaining_points((TableDb)db, _picks());
            toggle_talent(_s(card_variant.AsGodotDictionary(), "talent_id"));
            int after = TalentService.remaining_points((TableDb)db, _picks());
            if (after < before)
            {
                spent += before - after;
            }
        }

        ok = ok && spent <= TalentService.budget((TableDb)db);
        lines.Add($"天赋共花 {spent} 点（上限 {TalentService.budget((TableDb)db)}），没超");
        ok = ok && TalentService.remaining_points((TableDb)db, _picks()) >= 0;

        // 文案守卫：整页不许出现表内 id 形态
        string[] copy_hits = CopyGuard.id_tokens(this);
        ok = ok && copy_hits.Length == 0;
        lines.Add(CopyGuard.ascii_line(this));
        bool budget_ok = LayoutBudget.fits(this);
        ok = ok && budget_ok;
        lines.Add(LayoutBudget.ascii_line(this));

        foreach (string line in lines)
        {
            GD.Print("  " + line);
        }

        GD.Print($"CREATION SELF-TEST: {(ok ? "OK" : "FAILED")}");
        SceneTree tree = _tree();
        if (tree != null)
        {
            tree.Quit(ok ? 0 : 1);
        }
    }
}
