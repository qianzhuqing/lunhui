using Godot;
using Lunhui.Core;

namespace Lunhui.Audio;

/// <summary>
/// 音效播放入口（事件名 → 音频文件的**唯一出处**）。C# 版，对位原 `src/audio/sfx.gd`。
///
/// 各处只写 `Sfx.play("hit")`，不写音频路径：音频路径是**给音效侧的替换契约**——
/// 现在放的是 `tools/audio/make_placeholders.py` 程序化合成的占位音，音效侧到位后
/// **按同名文件替换**即可，代码一行都不用改（同「贴图缺失才退回色块」的口径）。
///
/// 播放层不是自己写的：直接复用 MIT 的 `godot_sound_manager`
/// （`addons/sound_manager/`，v2.6.2，出处与许可见 `docs/dev/第三方代码.md`「第五轮」）。
/// 它按需挂到场景树 root 上，**不给 `project.godot` 加 autoload**——这样 `--script`
/// （自检）模式与真窗口两条路都不需要额外初始化；没有主循环时只记账、不上声（不报错）。
///
/// 事件名取自设计 07 §8.4 的「特效与音效位」，不是开发侧新编的内容：
/// 命中／暴击（伤害跳字那一档）、宝箱开启、火盆点燃、战斗胜负。
///
/// **成员名保持 snake_case**：迁移期是 GDScript `SfxScript.play(...)` 这样点名字调的（调用点
/// 已随迁移全搬到 C#），改名仍要连调用点一起扫——与行类同一套理由
/// （见 `src/data/TableRow.cs` 的说明）。
/// </summary>
public partial class Sfx : RefCounted
{
    private const string SoundManagerPath = "res://addons/sound_manager/sound_manager.gd";

    public static readonly Godot.Collections.Dictionary EVENTS = new()
    {
        { "hit", "res://assets/audio/sfx/sfx_hit.wav" },
        { "crit", "res://assets/audio/sfx/sfx_crit.wav" },
        { "victory", "res://assets/audio/sfx/sfx_victory.wav" },
        { "defeat", "res://assets/audio/sfx/sfx_defeat.wav" },
        { "chest", "res://assets/audio/sfx/sfx_chest.wav" },
        { "brazier", "res://assets/audio/sfx/sfx_brazier.wav" },
    };

    /// <summary>记账上限：只给用例看「这一小段里放没放过某个事件」，不是无限增长的历史。</summary>
    public const int LOG_LIMIT = 64;

    /// <summary>音效走的总线（与 `default_bus_layout.tres` 和播放层的候选名单一致）</summary>
    public const string SOUND_BUS = "Sounds";

    /// <summary>音量档位：枢纽页那个按钮按一下轮换一档（0 = 静音）</summary>
    private static readonly double[] VOLUME_STEPS = { 0.0, 0.25, 0.5, 0.75, 1.0 };

    public const double DEFAULT_VOLUME = 1.0;

    /// <summary>静音的 dB：`linear_to_db(0)` 算出来是 -inf，明确写 -80 dB 更好收拾</summary>
    public const double MUTE_DB = -80.0;

    private static Godot.Collections.Array _played = new();
    private static Node _manager;
    private static double _volume = DEFAULT_VOLUME;
    private static bool _volume_loaded;

    // GDScript 读不到 C# 的 `const`／静态 `readonly`（施工单坑⑩），这两个是给用例的取值口。
    public static Godot.Collections.Dictionary events() => EVENTS;

    public static string sound_bus() => SOUND_BUS;

    /// <summary>放一个音效事件。没登记过的事件名**出声**（事件名写错时静默就成了哑炮）。</summary>
    public static void play(string event_id)
    {
        if (!EVENTS.ContainsKey(event_id))
        {
            GD.PushError($"[Sfx] 没有登记过的音效事件：'{event_id}'（要加事件只改本文件的 EVENTS）");
            return;
        }

        _played.Add(event_id);
        if (_played.Count > LOG_LIMIT)
        {
            // `Godot.Collections.Array.Slice()` 返回的是 `Variant[]`、不是 `Array`，这里自己收口
            var kept = new Godot.Collections.Array();
            for (int i = _played.Count - LOG_LIMIT; i < _played.Count; i++)
            {
                kept.Add(_played[i]);
            }

            _played = kept;
        }

        AudioStream stream = _stream_for(event_id);
        if (stream == null)
        {
            return;
        }

        Node manager = _manager_node();
        if (manager == null)
        {
            return;
        }

        manager.Call("play_sound", stream);
    }

    /// <summary>这一小段里放过哪些事件（用例读它；真玩的时候没人读）</summary>
    public static string[] played_events()
    {
        var out_list = new System.Collections.Generic.List<string>();
        foreach (Variant item in _played)
        {
            out_list.Add(item.AsString());
        }

        return out_list.ToArray();
    }

    public static bool has_played(string event_id) => _played.Contains(event_id);

    public static void clear_log() => _played = new Godot.Collections.Array();

    /// <summary>当前音量（0..1）。第一次问的时候从设置文件读一次；读不到就用默认值。</summary>
    public static double volume()
    {
        _ensure_volume_loaded();
        return _volume;
    }

    /// <summary>
    /// 改音量：当场生效（写总线），并把设置落盘。
    /// 写不进去**不挡游戏**——本次运行内照常生效，只记一条警告（与存档同一条纪律）。
    /// </summary>
    public static Godot.Collections.Dictionary set_volume(double value, bool persist = true)
    {
        _ensure_volume_loaded();
        _volume = Mathf.Clamp(value, 0.0, 1.0);
        _apply_volume();
        if (!persist)
        {
            return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" } };
        }

        var store = new SettingsStore();
        Godot.Collections.Dictionary result = store.save_volume(_volume);
        if (!result["ok"].AsBool())
        {
            GD.PushWarning($"[Sfx] 音量设置没落盘：{result["error"].AsString()}（本次运行内仍然生效）");
        }

        return result;
    }

    /// <summary>
    /// 轮换到下一档音量（枢纽页的按钮用它）：0 → 25% → 50% → 75% → 100% → 0。
    /// 设置文件里写了非标准值时，取「比它大的第一档」，到顶回到静音。
    /// </summary>
    public static double cycle_volume(bool persist = true)
    {
        double current = volume();
        double next = VOLUME_STEPS[0];
        foreach (double step in VOLUME_STEPS)
        {
            if (step > current + 0.001)
            {
                next = step;
                break;
            }
        }

        set_volume(next, persist);
        return next;
    }

    /// <summary>给界面看的文案（0 说「静音」，不说「0%」）</summary>
    public static string volume_text()
    {
        double current = volume();
        if (current <= 0.0)
        {
            return "音量：静音";
        }

        int percent = (int)System.Math.Round(current * 100.0, System.MidpointRounding.AwayFromZero);
        return $"音量：{percent}%";
    }

    /// <summary>
    /// 把当前音量写到总线上。
    ///
    /// 播放层在的话让它自己设（复用它那套）；但**静音要自己写**：播放层的
    /// `set_sound_volume(0)` 会算出 `-inf` dB，不如明确写 -80。
    /// </summary>
    private static void _apply_volume()
    {
        if (_manager != null && GodotObject.IsInstanceValid(_manager) && _volume > 0.0)
        {
            _manager.Call("set_sound_volume", _volume);
            return;
        }

        int bus = AudioServer.GetBusIndex(SOUND_BUS);
        if (bus < 0)
        {
            return;
        }

        AudioServer.SetBusVolumeDb(bus, (float)(_volume <= 0.0 ? MUTE_DB : Mathf.LinearToDb(_volume)));
    }

    private static void _ensure_volume_loaded()
    {
        if (_volume_loaded)
        {
            return;
        }

        _volume_loaded = true;
        var store = new SettingsStore();
        Godot.Collections.Dictionary result = store.load_volume();
        _volume = Mathf.Clamp(result["volume"].AsDouble(), 0.0, 1.0);
        _apply_volume();
    }

    /// <summary>
    /// 把挂上去的播放层摘掉并释放（**只给自检收尾用**）。
    ///
    /// 为什么需要它：`--script` 模式下没人替我们回收挂在场景树 root 上的节点，跑完不退
    /// 就是一串 `ObjectDB instances were leaked at exit`（实测 31 个）。真玩的时候播放层
    /// 跟进程同寿，不需要这一步；`tools/RunTestsCs.cs` 在所有用例跑完后调它。
    /// </summary>
    public static void shutdown()
    {
        if (_manager != null && GodotObject.IsInstanceValid(_manager))
        {
            Node parent = _manager.GetParent();
            parent?.RemoveChild(_manager);
            _manager.Free();
        }

        _manager = null;
        _played = new Godot.Collections.Array();
        _volume = DEFAULT_VOLUME;
        _volume_loaded = false;
    }

    /// <summary>事件名列表（用例拿它跟磁盘上的文件名互相核对，防止「加了文件没人放」/「放了文件不存在」）</summary>
    public static Godot.Collections.Array event_ids()
    {
        var out_list = new Godot.Collections.Array();
        foreach (Variant key in EVENTS.Keys)
        {
            out_list.Add(key);
        }

        return out_list;
    }

    private static AudioStream _stream_for(string event_id)
    {
        string path = EVENTS[event_id].AsString();
        // 不做自己的缓存：`load()` 本来就命中引擎的资源缓存，而**静态字段会在退出时
        // 留住这些资源**（实测会报 "N resources still in use at exit"）——退出干净比省一次查表值钱。
        AudioStream stream = GD.Load<AudioStream>(path);
        if (stream == null)
        {
            GD.PushError($"[Sfx] 音频加载失败：{path}（缺文件就重跑 tools/audio/make_placeholders.py）");
        }

        return stream;
    }

    private static Node _manager_node()
    {
        if (_manager != null && GodotObject.IsInstanceValid(_manager))
        {
            return _manager;
        }

        // 别人（比如以后真把编辑器插件的 autoload 打开）先注册的话，用那一份，别造第二个
        if (Engine.HasSingleton("SoundManager") && Engine.GetSingleton("SoundManager") is Node existing
            && GodotObject.IsInstanceValid(existing))
        {
            _manager = existing;
            return _manager;
        }

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            return null;
        }

        Node root = tree.Root;
        if (root == null)
        {
            return null;
        }

        var script = GD.Load<GDScript>(SoundManagerPath);
        var node = script.New().As<Node>();
        root.AddChild(node);
        // 播放层的 `_init` 会把自己注册成引擎单例；单例是引擎持有的引用，退出时会报
        // "ObjectDB instances were leaked"（实测 12 个）。节点已经挂在场景树 root 上、
        // 生命周期由场景树管，所以这里把单例登记撤掉——两件事都保留，退出才干净。
        if (Engine.HasSingleton("SoundManager"))
        {
            Engine.UnregisterSingleton("SoundManager");
        }

        _manager = node;
        // 建出来的第一件事：把当前音量套上（不然玩家在枢纽里设过的音量会被"新播放层"重置成 100%）
        _apply_volume();
        return _manager;
    }
}
