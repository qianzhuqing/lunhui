using Godot;
using Lunhui.Audio;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的伤害跳字与受击／出手表现，外加自动战斗与撤退——从 `BattleScreen.cs`
/// 拆出来的 partial（那份要守 800 行硬上限）。
///
/// 一条铁律：**别动卡片在容器里的 `position`**（容器下次排版会把补间顶掉），
/// 所以缩放／抖动都走 `scale`／`modulate`，跳字则挂在不受排版管的浮层上。
/// </summary>
public partial class BattleScreen
{
    /// <summary>伤害跳字：读引擎事件，在目标卡片上飘一个数字（暴击金色、破绽红色）</summary>
    public void _spawn_floats(Godot.Collections.Array events)
    {
        // 多段招式一次会来好几条事件：受击反馈每个目标只放一次，不然几段缩放补间会互相打架
        var reacted = new Godot.Collections.Dictionary();
        foreach (Variant event_variant in events)
        {
            Godot.Collections.Dictionary @event = event_variant.AsGodotDictionary();
            int damage = _i(@event, "damage");
            if (damage <= 0)
            {
                continue;
            }

            string target_id = _s(@event, "target");
            Node row = _find_actor_row(target_id);
            if (row == null)
            {
                continue;
            }

            if (!reacted.ContainsKey(target_id))
            {
                reacted[target_id] = true;
                _hit_feedback(row, @event);
                // 音效位见设计 07 §8.4「伤害跳字」那一档：直伤按暴击/普通分两条，
                // 持续伤害（DoT）不走这一条——设计没给它的音效位，等音效侧定。
                if (_s(@event, "kind") != "dot")
                {
                    Sfx.play(_b(@event, "is_crit") ? "crit" : "hit");
                }
            }

            var label = new Label();
            // 段序号 0 保持旧名字（用例与脚本还在按 Float_<id> 找），后续段加后缀避免重名被引擎改名
            int hit_index = _i(@event, "hit_index");
            label.Name = hit_index == 0 ? $"Float_{target_id}" : $"Float_{target_id}_{hit_index}";
            label.Text = $"-{damage}";
            // 游戏内浮字也吃两档（设计 15 §4.4，2026-10-04 Q82）：跟标题档 24——
            // 16 ＝ 12×1.33，像素字发虚，正是本轮取消 16 档要消灭的那种非整数倍。
            label.AddThemeFontSizeOverride("font_size", 24);
            Color color = _b(@event, "is_crit") ? new Color("ffd24a") : new Color(1, 1, 1, 0.95f);
            if (_b(@event, "was_broken"))
            {
                color = new Color("eb5757");
            }

            // 持续伤害跳字用状态自己的颜色（中毒绿、灼伤橙、流血红、内伤紫），一眼能和直伤区分
            if (_s(@event, "kind") == "dot")
            {
                color = _dot_color(_s(@event, "status_id"));
            }

            label.AddThemeColorOverride("font_color", color);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
            label.AddThemeConstantOverride("outline_size", 4);
            // 多段招式一次会产生好几个跳字：按段序号错开，不然数字叠在一起看不清
            // 跳字必须挂在**不受容器排版管**的浮层上：挂在卡片里的话，VBoxContainer 每次排版
            // 都会把它按到卡片左边（position 被顶掉），半个数字还会被滚动区裁掉——踩过。
            Control layer = _float_layer();
            label.Position = ((Control)row).GlobalPosition - GlobalPosition
                + new Vector2(210 + (26 * hit_index), 4 - (10 * hit_index));
            layer.AddChild(label);
            // 卡片靠右时上面那个 +210 会把数字推出屏幕（满招式那档内容宽 1134，离 1152 只剩 18px），
            // 所以按浮层尺寸夹一下：宁可压回卡片上，也别让玩家看不见这一击打了多少。
            // `reset_size()` 让 Label 立刻按文字算出尺寸；浮层还没排版（headless／刚建）时 bounds 为 0，
            // `clamp_into` 会原样返回，不影响用例里对位置的断言。
            label.ResetSize();
            label.Position = LayoutBudget.clamp_into(layer.Size, label.Position, label.Size);
            Tween tween = CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(
                label, "position:y", label.Position.Y - 26.0 - (6.0 * hit_index), 0.7);
            tween.TweenProperty(label, "modulate:a", 0.0, 0.7);
            tween.Chain().TweenCallback(Callable.From(label.QueueFree));
        }
    }

    /// <summary>跳字浮层：铺满整屏的普通 Control（不是容器），子节点位置由我们自己说了算，且在卡片之上</summary>
    private Control _float_layer()
    {
        if (_float_layer_node != null && IsInstanceValid(_float_layer_node))
        {
            _float_layer_node.MoveToFront();
            return _float_layer_node;
        }

        _float_layer_node = new Control
        {
            Name = "FloatLayer",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _float_layer_node.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_float_layer_node);
        return _float_layer_node;
    }

    /// <summary>某个状态的跳字颜色（表里 damage_type.display_color）</summary>
    private Color _dot_color(string status_id)
    {
        Resource status_row = status_id.Length > 0 ? Db.get_row("status_effect", status_id) : null;
        if (status_row == null)
        {
            return new Color("7fd35a");
        }

        Resource type_row = Db.get_row("damage_type", status_row.Get("damage_type").AsString());
        return type_row != null ? new Color(type_row.Get("display_color").AsString()) : new Color("7fd35a");
    }

    /// <summary>受击反馈：命中闪一下（破绽再抖一下），濒死/阵亡由卡片颜色体现</summary>
    private void _hit_feedback(Node row, Godot.Collections.Dictionary @event)
    {
        if (row is not Control item)
        {
            return;
        }

        item.PivotOffset = _card_pivot(item);
        // 受击分帧：先「挨了一下」被压扁再弹回来，配合红光；破绽那一下再补一段横向抖动
        item.Modulate = new Color(1.6f, 0.8f, 0.8f, 1.0f);
        item.Scale = new Vector2(0.94f, 1.04f);
        Tween flash = CreateTween();
        flash.SetTrans(Tween.TransitionType.Quad);
        flash.TweenProperty(item, "scale", Vector2.One, 0.16);
        flash.Parallel().TweenProperty(item, "modulate", new Color(1, 1, 1, 1), 0.18);
        if (_b(@event, "was_broken"))
        {
            Vector2 base_position = item.Position;
            Tween shake = CreateTween();
            foreach (Vector2 offset in new[]
                     {
                         new Vector2(4, 0), new Vector2(-4, 0), new Vector2(3, 0), Vector2.Zero,
                     })
            {
                shake.TweenProperty(item, "position", base_position + offset, 0.05);
            }
        }
    }

    /// <summary>卡片缩放要绕中心：size 还没排版出来时退回一个按卡片宽高的估计值（320×40 的一半）</summary>
    private static Vector2 _card_pivot(Control control) =>
        control.Size.X > 1.0 && control.Size.Y > 1.0 ? control.Size * 0.5f : new Vector2(160, 20);

    private Node _find_actor_row(string actor_id)
    {
        foreach (Node box in new Node[] { _allies_box, _enemies_box })
        {
            Node row = box?.GetNodeOrNull($"Actor_{actor_id}");
            if (row != null)
            {
                return row;
            }
        }

        return null;
    }

    public void press_auto()
    {
        if (sim == null || _sim_finished())
        {
            return;
        }

        auto_enabled = true;
        _auto_advance();
    }

    public void press_stop_auto()
    {
        auto_enabled = false;
        _refresh_buttons();
    }

    public double toggle_speed()
    {
        speed_index = (speed_index + 1) % SPEED_STEPS.Length;
        _refresh_buttons();
        return speed();
    }

    public double speed() => SPEED_STEPS[speed_index];

    /// <summary>自动战斗策略（设计 04：保守／均衡／全力）。只影响我方自动战斗，手动出招不受影响。</summary>
    public string press_strategy()
    {
        if (sim == null)
        {
            return "";
        }

        string strategy_id = sim.Call("cycle_strategy").AsString();
        _set_status($"自动战斗策略：{sim.Call("strategy_label").AsString()}");
        _refresh_buttons();
        return strategy_id;
    }

    public string strategy_text() => _strategy_button != null ? _strategy_button.Text : "";

    public Godot.Collections.Dictionary press_flee()
    {
        take_over();
        if (sim == null || _sim_finished())
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "战斗已结束" } };
        }

        var lines = new Godot.Collections.Array();
        if (!sim.Call("in_round").AsBool())
        {
            sim.Call("begin_round");
            lines.Add($"—— 第 {sim.Call("rounds_played").AsInt32()} 回合 ——");
            lines.AddRange(_intent_lines());
        }

        lines.AddRange(_advance_until_ally());
        GodotObject actor = sim.Call("current_actor").AsGodotObject();
        if (actor == null || actor.Get("side").AsInt32() != _side_ally())
        {
            _append_log(lines);
            _refresh();
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "现在不是我方行动" } };
        }

        Godot.Collections.Dictionary result = sim.Call("flee", actor).AsGodotDictionary();
        lines.AddRange(_v(result, "lines").AsGodotArray());
        _append_log(lines);
        if (!_b(result, "ok"))
        {
            _set_status($"撤不了：{_s(result, "error")}");
        }
        else if (_b(result, "escaped"))
        {
            _set_status("撤退成功：脱离战斗（没有经验与掉落，明雷还在）");
        }
        else
        {
            _set_status($"撤退失败：被缠住了，白费一次行动（成功率 {_f(result, "chance") * 100.0:F0}%）");
        }

        if (_sim_finished())
        {
            _settle();
        }

        _refresh();
        return result;
    }

    public void press_return()
    {
        if (_callable_valid(return_handler))
        {
            _invoke_callback(return_handler);
            return;
        }

        _tree()?.ChangeSceneToFile(_return_scene());
    }
}
