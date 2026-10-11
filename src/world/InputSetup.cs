using Godot;

namespace Lunhui.World;

/// <summary>
/// 运行期注册输入映射（C# 版，对位原 `src/world/input_setup.gd`）。
///
/// 不往 `project.godot` 里塞 InputEvent 的序列化文本（手写容易出错、也不好 review），
/// 需要时调一次 `ensure()` 即可；重复调用不会重复注册。
///
/// **常量跨语言**：GDScript 读不到 C# 的 `ACTIONS`（`const`／`static readonly` 不进脚本 API），
/// 所以另开一个同名小写静态方法 `actions()` 当取值口（与 `MenuController.action_play()` 同一套做法）。
/// </summary>
public partial class InputSetup : RefCounted
{
    /// <summary>动作 → 物理键位（对位 GDScript 的 `ACTIONS`）。</summary>
    public static readonly Godot.Collections.Dictionary ACTIONS = new()
    {
        { "move_up", new Godot.Collections.Array { (int)Key.W, (int)Key.Up } },
        { "move_down", new Godot.Collections.Array { (int)Key.S, (int)Key.Down } },
        { "move_left", new Godot.Collections.Array { (int)Key.A, (int)Key.Left } },
        { "move_right", new Godot.Collections.Array { (int)Key.D, (int)Key.Right } },
        { "sneak", new Godot.Collections.Array { (int)Key.Shift } },
        { "interact", new Godot.Collections.Array { (int)Key.E, (int)Key.Space } },
        { "open_character", new Godot.Collections.Array { (int)Key.Tab } },
        // 0.18.1 的全局快捷键：行囊是角色面板的第三个页签，单独一个键直达
        { "open_bag", new Godot.Collections.Array { (int)Key.I } },
        // 0.28.0（Q62）：看 NPC 信息是独立的一个键——「我只是想看看这人是谁」
        // 不该经过一次会改变世界状态的交互（E 才是交互菜单）
        { "npc_info", new Godot.Collections.Array { (int)Key.Q } },
        { "show_progress", new Godot.Collections.Array { (int)Key.M } },
        { "sweep", new Godot.Collections.Array { (int)Key.J } },
        { "show_clues", new Godot.Collections.Array { (int)Key.K } },
        { "save_game", new Godot.Collections.Array { (int)Key.F5 } },
    };

    /// <summary>`ACTIONS` 的取值口（GDScript 侧用；P5 清零 GDScript 后收回成字段）。</summary>
    public static Godot.Collections.Dictionary actions() => ACTIONS;

    public static void ensure()
    {
        foreach (Variant action_key in ACTIONS.Keys)
        {
            string action = action_key.AsString();
            if (InputMap.HasAction(action))
            {
                continue;
            }

            InputMap.AddAction(action);
            foreach (Variant keycode in ACTIONS[action].AsGodotArray())
            {
                var input_event = new InputEventKey { PhysicalKeycode = (Key)keycode.AsInt32() };
                InputMap.ActionAddEvent(action, input_event);
            }
        }
    }

    public static bool is_defined(string action) => InputMap.HasAction(action);
}
