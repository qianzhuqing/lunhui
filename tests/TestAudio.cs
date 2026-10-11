using Godot;
using Lunhui.Audio;

namespace Lunhui.Tests;

/// <summary>
/// 音效：事件名 ↔ 文件、播放层与总线——对位 `tests/test_audio.gd`（2026-10-11 P4 第九批搬 C#）。
///
/// 播放层是**复用**来的 MIT 项目 `godot_sound_manager`（`addons/sound_manager/`，出处见
/// `docs/dev/第三方代码.md`「第五轮」）；音频文件是脚本程序化合成的占位音。这条用例守的是"两边别漂"：
///   ① 每个事件指向的文件必须存在且真的能当音频加载；
///   ② 磁盘上的音频文件必须每个都有事件在放（不然就是放不响的孤儿素材）；
///   ③ 没登记的事件名**不许静默**（写错事件名要出声，不是哑炮）；
///   ④ 总线布局里得有播放层认的「Sounds」——落到 Master 就说明"接上了"是假的。
/// 真实路径上的播放（开箱／火盆／命中／胜负）钉在小地图与战斗界面那两份用例里，这里只管契约与映射。
/// </summary>
public partial class TestAudio : TestCase
{
    private const string SoundDir = "res://assets/audio/sfx";
    private const string BusLayoutPath = "res://default_bus_layout.tres";
    private const string PlayerLicense = "res://addons/sound_manager/LICENSE";

    /// <summary>复用的播放层（`addons/` 下的 GDScript，迁移期只能从 C# 侧 load 出来 `New`）。</summary>
    private const string SoundEffectsPath = "res://addons/sound_manager/sound_effects.gd";

    public override string SuiteName() => "音效事件与总线";

    public override void Run()
    {
        _check_events_have_files();
        _check_no_orphan_files();
        _check_unknown_event_is_loud();
        _check_bus_layout();
        _check_reused_player_license();
    }

    private void _check_events_have_files()
    {
        var checked_count = 0;
        Godot.Collections.Dictionary events = Sfx.events();
        foreach (Variant event_variant in Sfx.event_ids())
        {
            string event_id = event_variant.AsString();
            string path = events[event_id].AsString();
            checked_count += 1;
            CheckTrue(FileAccess.FileExists(path), $"事件 {event_id} 指向的文件在：{path}");
            CheckTrue(GD.Load(path) is AudioStream, $"事件 {event_id} 的文件能当音频加载：{path}");
        }

        CheckGt(checked_count, 3.0, $"登记了足够多的音效事件（{checked_count} 个）");
    }

    private void _check_no_orphan_files()
    {
        DirAccess dir = DirAccess.Open(SoundDir);
        CheckNotNull(dir, $"音频目录在：{SoundDir}（缺了就重跑 tools/audio/make_placeholders.py）");
        if (dir == null)
        {
            return;
        }

        var known = new Godot.Collections.Dictionary();
        Godot.Collections.Dictionary events = Sfx.events();
        foreach (Variant event_variant in Sfx.event_ids())
        {
            string event_id = event_variant.AsString();
            known[events[event_id].AsString()] = event_id;
        }

        var scanned = 0;
        foreach (var file_name in dir.GetFiles())
        {
            if (!file_name.EndsWith(".wav"))
            {
                continue;
            }

            scanned += 1;
            string full = $"{SoundDir}/{file_name}";
            CheckTrue(known.ContainsKey(full), $"磁盘上的 {file_name} 有事件在放它（没人放就该删掉或补事件）");
        }

        CheckEq(scanned, known.Count, $"音频文件数与事件数一一对应（{scanned} 个文件）");
    }

    private void _check_unknown_event_is_loud()
    {
        Sfx.clear_log();
        // 这一行会 push_error（引擎日志门限盯着它）——所以它同时验了"不静默"这一条
        Sfx.play("no_such_event");
        CheckEq(Sfx.played_events().Length, 0, "没登记的事件名不进播放记录（不静默当哑炮）");

        Sfx.clear_log();
        Sfx.play("hit");
        CheckTrue(Sfx.has_played("hit"), "登记过的事件能放");
        CheckEq(Sfx.played_events().Length, 1, "放一次记一次");

        Sfx.clear_log();
        CheckEq(Sfx.played_events().Length, 0, "clear_log 能把记录清空（用例之间不互相污染）");
    }

    private void _check_bus_layout()
    {
        // 最强的一条：**启动时**项目就已经把我们的布局装上了（Godot 默认读 res://default_bus_layout.tres）。
        // 只断言"文件能加载"是不够的——文件在、但没人读，播放层照样会静默落回 Master。
        CheckGt(
            AudioServer.GetBusIndex("Sounds"),
            0.0,
            $"启动时就有「Sounds」总线（实际 {AudioServer.GetBusIndex("Sounds")}）");
        AudioBusLayout layout = GD.Load<AudioBusLayout>(BusLayoutPath);
        CheckTrue(layout != null, $"总线布局资源能加载：{BusLayoutPath}");
        if (layout == null)
        {
            return;
        }

        AudioServer.SetBusLayout(layout);
        var names = new Godot.Collections.Array();
        for (var index = 0; index < AudioServer.BusCount; index++)
        {
            names.Add(AudioServer.GetBusName(index));
        }

        CheckTrue(names.Contains("Sounds"), $"总线布局里有播放层认的「Sounds」（实际 {string.Join(", ", names)}）");
        // 播放层自己怎么挑总线：`abstract_audio_player_pool.get_possible_bus()` 按候选名找，
        // 一个都找不到会**静默落回 Master**——所以这里拿真播放层问一次，而不是只看布局文件。
        // 候选总线那一参在 GDScript 里声明成 `PackedStringArray`：从 GDScript 侧传 `["Sounds","SFX"]`
        // 会被自动转过来，从 C# 侧照做同样传一个字符串数组的 `Array`（值语义那份见坑⑧）。
        var possible_busses = new Godot.Collections.Array { "Sounds", "SFX" };
        var pool = GD.Load<GDScript>(SoundEffectsPath).New(possible_busses, 1).AsGodotObject();
        CheckEq(
            pool.Get("bus").AsString(),
            "Sounds",
            "播放层解析到的默认总线是 Sounds（落到 Master 说明布局没生效）");
        pool.Free();
    }

    private void _check_reused_player_license()
    {
        CheckTrue(FileAccess.FileExists(PlayerLicense), $"复用来的播放层带着许可证：{PlayerLicense}");
        string text = FileAccess.GetFileAsString(PlayerLicense);
        CheckTrue(text.Contains("MIT License"), "许可证确实是 MIT（第三方代码只收 MIT／CC0 这类能商用的）");
    }
}
