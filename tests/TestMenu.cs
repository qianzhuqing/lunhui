using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 启动菜单：控制器决策 ＋ 真实场景冒烟（设计 09）。
///
/// **2026-10-11（P4 第二十九批）起这份就是唯一一份**：旧链 `tests/test_menu.gd` 已并入本文件后退场
/// ——这一批补进当初「要场景树」的那一节（实例化 `main_menu.tscn`／`creation_screen.tscn`／
/// `placeholder_game.tscn`，点真实按钮走「新建 → 落盘 → 列表出现 → 读取 → 会话状态就位」），
/// 外加「读档按当前配表收口并写回」那一节。
/// 决策层盯五组：新建／读取／退出的主流程、空配置表、存档写不进去、损坏档、槽位全满 → 覆盖最旧。
/// </summary>
public partial class TestMenu : TestCase
{
    private const string TestDir = "res://.logs/test_menu_cs";
    private const int Slots = 3;
    private const string MenuScene = "res://scenes/main_menu.tscn";
    private const string PlaceholderScene = "res://scenes/placeholder_game.tscn";
    private const string CreationScene = "res://scenes/creation_screen.tscn";

    /// <summary>`MainMenu.cs` 的 `OVERWORLD_SCENE` 是 C# 的 `const`（不进脚本 API，坑⑩），这边留一份。</summary>
    private const string OverworldScene = "res://scenes/world_run.tscn";

    public override string SuiteName() => "启动菜单：新建 / 读取 / 退出";

    public override void Run()
    {
        // 游戏里默认 6 个存档槽；用例夹具自己只开 3 格（`Slots`），所以常量本身要单独钉住
        // （2026-10-03 变异探针：`DEFAULT_SLOT_COUNT 6→3` 没被抓住）。
        CheckEq(SaveStore.default_slot_count(), 6, "默认存档槽位数 = 6（用例夹具另有 Slots）");
        _CleanDir(TestDir);
        _CheckControllerFlow();
        _CheckControllerWithoutTables();
        _CheckUnwritableStore();
        _CheckCorruptSlotLoad();
        _CheckOverwriteOldestSlot();
        _CheckStaleEquippedWriteback();
        _CheckSceneFlow();
    }

    /// <summary>决策层：不碰场景树也要能覆盖全部分支。</summary>
    private void _CheckControllerFlow()
    {
        // 这一份用例会 `new_game({})` ——即 `CreationService.build` 的「改名的主角」那条路，
        // 会往 `character_base` 注入一行书生模板（`TableDb.inject_row`）。必须用私有表库，
        // 否则同进程里后跑的 `TestCharacterSheet` 读到的书生模板已是「陆文昭」（见 `TestCase.FreshDb`）。
        TableDb db = FreshDb();
        SaveStore store = SaveStore.create(TestDir, Slots);
        store.ensure_dir();
        MenuController controller =
            MenuController.create(db, store, System.Array.Empty<string>(), "normal");
        CheckTrue(controller.can_start_new_game(), "配置表就绪时可以新建游戏");
        CheckFalse(controller.can_load(), "没有存档时读取不可用");
        CheckFalse(controller.has_saves(), "没有存档时 has_saves 为假");

        Godot.Collections.Dictionary created = controller.new_game(new Godot.Collections.Dictionary());
        CheckTrue(created["ok"].AsBool(), "新建游戏应成功");
        CheckEq(created["action"].AsString(), MenuController.ACTION_PLAY, "新建后动作是进入游戏");
        CheckEq(created["slot"].AsInt32(), 1, "新游戏落在第一个空槽");
        // 0.10.0 起开局队伍由 `recruit_def.is_initial` 决定（设计 09 §3.2：开局只有书生一人，
        // 同伴按 join_condition／join_scene 在剧情里加入）。
        var initialIds = new System.Collections.Generic.List<string>();
        foreach (Variant entry in db.rows("recruit_def"))
        {
            var row = (RecruitDefRow)entry.AsGodotObject();
            if (row.is_initial)
            {
                initialIds.Add(row.char_id);
            }
        }
        CheckGt(initialIds.Count, 0, $"recruit_def 里有初始成员（{initialIds.Count} 人）");
        GodotObject state = created["state"].AsGodotObject();
        CheckEq(state.Call("party_size").AsInt32(), initialIds.Count, "新游戏按 recruit_def 的初始成员组队");
        CheckEq(
            string.Join(",", state.Get("char_ids").AsStringArray()),
            string.Join(",", initialIds),
            "开局队伍与 recruit_def 的初始成员逐人一致（顺序也照表）");
        CheckEq(created["start_scene"].AsString(), MenuController.START_HUB, "新建游戏进占位枢纽页");
        CheckTrue(store.slot_exists(1), "新建游戏会立刻自动存档");
        // 空方案走 `_with_defaults`：默认用第一张出身卡，名字预填**真名**而不是卡标题
        // （设计 21 §七 第 5 条：书生那张卡叫「家道失落的书生」，真名是陆文昭）
        CheckEq(
            state.Call("char_name", db, initialIds[0]).AsString(), "陆文昭",
            "没给名字时默认用出身卡的真名");

        controller.refresh();
        CheckTrue(controller.can_load(), "有了存档后读取可用");
        CheckEq(controller.save_count(), 1, "存档计数为 1");
        string[] labels = controller.slot_labels();
        CheckTrue(labels[0].Contains("第 1 格"), "槽位文案带格号");
        CheckTrue(labels[0].Contains("Lv"), "槽位文案带队伍等级");

        Godot.Collections.Dictionary second = controller.new_game(new Godot.Collections.Dictionary());
        CheckEq(second["slot"].AsInt32(), 2, "第二次新建落在下一个空槽");
        controller.refresh();
        CheckEq(controller.save_count(), 2, "两份存档");

        Godot.Collections.Dictionary loaded = controller.load_game(1);
        CheckTrue(loaded["ok"].AsBool(), "读取得回来");
        CheckEq(loaded["action"].AsString(), MenuController.ACTION_PLAY, "读取后动作是进入游戏");
        GodotObject loadedState = loaded["state"].AsGodotObject();
        CheckEq(loadedState.Get("slot").AsInt32(), 1, "读回的是第 1 格");
        CheckEq(
            string.Join(",", loadedState.Get("char_ids").AsStringArray()),
            string.Join(",", state.Get("char_ids").AsStringArray()),
            "读回的队伍与建档时一致");
        // 设计 09 §二：读档一律回大地图，落点用存档里的 world_pos（v13）
        CheckEq(loaded["start_scene"].AsString(), MenuController.START_OVERWORLD, "读档一律回大地图");
        state.Call("set_world_pos", new Vector2(300.0f, 400.0f));
        store.save_slot(1, state);
        Godot.Collections.Dictionary reloaded = controller.load_game(1);
        CheckEq(
            reloaded["state"].AsGodotObject().Call("world_position").AsVector2(),
            new Vector2(300.0f, 400.0f),
            "读档把大地图坐标一起带回来");

        Godot.Collections.Dictionary missing = controller.load_game(99);
        CheckFalse(missing["ok"].AsBool(), "读不存在的槽位应失败");
        CheckTrue(missing["message"].AsString().Contains("没有存档"), "失败文案说清原因");

        Godot.Collections.Dictionary quitResult = controller.quit_game();
        CheckEq(quitResult["action"].AsString(), MenuController.ACTION_QUIT, "退出动作");
    }

    private void _CheckControllerWithoutTables()
    {
        var emptyDb = new TableDb();
        // 单独一个干净目录：主流程那份目录里已经有存档，用**空表**去列槽会去解析它们
        // （`describe_slot` → `GameState.from_dict`），白白往日志里喷「没有这张表」。
        string dirPath = TestDir + "/empty_db";
        _CleanDir(dirPath);
        SaveStore store = SaveStore.create(dirPath, Slots);
        MenuController controller =
            MenuController.create(emptyDb, store, System.Array.Empty<string>(), "normal");
        CheckFalse(controller.can_start_new_game(), "没有配置表时不能新建游戏");
        Godot.Collections.Dictionary result = controller.new_game(new Godot.Collections.Dictionary());
        CheckFalse(result["ok"].AsBool(), "没有配置表时新建应失败");
        CheckTrue(result["message"].AsString().Contains("配置表"), "失败文案指向配置表");
    }

    /// <summary>存档写不进去也不能挡住玩：进游戏照走，只是提示本次进度不保留。</summary>
    private void _CheckUnwritableStore()
    {
        TableDb db = FreshDb();
        UnwritableSaveStore store = UnwritableSaveStore.create(TestDir, Slots);
        MenuController controller =
            MenuController.create(db, store, System.Array.Empty<string>(), "normal");
        Godot.Collections.Dictionary result = controller.new_game(new Godot.Collections.Dictionary());
        CheckTrue(result["ok"].AsBool(), "存档目录不可写时仍能开局");
        CheckEq(result["action"].AsString(), MenuController.ACTION_PLAY, "照样进入游戏");
        CheckTrue(result["message"].AsString().Contains("自动存档失败"), "提示自动存档失败");
        CheckNotNull(result["state"].AsGodotObject(), "内存里的会话状态有效");
    }

    /// <summary>
    /// 选到一个**损坏的存档**：菜单不许崩，要给得出原因、不给出半成品状态，并且仍然停在菜单（不进游戏）。
    /// </summary>
    private void _CheckCorruptSlotLoad()
    {
        TableDb db = FreshDb();
        SaveStore store = SaveStore.create(TestDir + "/corrupt", Slots);
        store.ensure_dir();
        using (FileAccess file = FileAccess.Open(store.slot_path(1), FileAccess.ModeFlags.Write))
        {
            file.StoreString("{ 这不是合法 JSON");
        }
        MenuController controller =
            MenuController.create(db, store, System.Array.Empty<string>(), "normal");
        CheckTrue(controller.has_saves(), "损坏的档也算「有存档」（列表里看得到）");
        CheckTrue(controller.can_load(), "损坏的档也能点「读取」（点了要给原因，不是灰掉）");
        CheckTrue(controller.slot_labels()[0].Contains("损坏"), "槽位文案照实写「损坏」");

        Godot.Collections.Dictionary loaded = controller.load_game(1);
        CheckFalse(loaded["ok"].AsBool(), "损坏档读不进来");
        CheckEq(loaded["action"].AsString(), MenuController.ACTION_NONE, "动作是「什么都不做」（不切场景）");
        CheckNull(loaded["state"].AsGodotObject(), "不给出半成品状态");
        CheckTrue(loaded["message"].AsString().Contains("损坏"), "给得出原因");
        CheckTrue(controller.can_start_new_game(), "读档失败后仍能新建游戏（菜单没被卡住）");
    }

    /// <summary>
    /// 三个槽全满时「新建游戏」：覆盖**最旧**那一格，并且提示玩家（不问就覆盖，必须说清楚）。
    /// 以前只验过「有空槽就放空槽」；「全满 → 覆盖最旧」这条分支正是老玩家常常撞上的那条。
    /// </summary>
    private void _CheckOverwriteOldestSlot()
    {
        TableDb db = FreshDb();
        string dirPath = TestDir + "/full";
        _CleanDir(dirPath);      // 上一轮跑剩的文件会让「空槽」变成「覆盖」，先把目录清干净
        SaveStore store = SaveStore.create(dirPath, Slots);
        store.ensure_dir();
        string[] partyIds = GameState.default_party_ids(db);
        MenuController controller = MenuController.create(db, store, partyIds, "normal");
        for (int index = 0; index < Slots; index++)
        {
            Godot.Collections.Dictionary made = controller.new_game(new Godot.Collections.Dictionary());
            CheckTrue(made["ok"].AsBool(), $"第 {index + 1} 次新建成功");
            CheckFalse(made["overwrite"].AsBool(), "新档落在空槽（没有覆盖）");
        }
        controller.refresh();
        CheckEq(controller.save_count(), Slots, "三格都满了");

        // 把第 2 格改成「最旧」：直接改文件里的 saved_unix（save_slot 会用当前时间覆盖它）
        string path = store.slot_path(2);
        var data = Json.ParseString(FileAccess.GetFileAsString(path)).AsGodotDictionary();
        data["saved_unix"] = 100;
        using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            file.StoreString(Json.Stringify(data));
        }
        int slot1Before = store.describe_slot(1, null)["saved_unix"].AsInt32();
        int slot3Before = store.describe_slot(3, null)["saved_unix"].AsInt32();

        Godot.Collections.Dictionary again = controller.new_game(new Godot.Collections.Dictionary());
        CheckTrue(again["ok"].AsBool(), "全满时新建仍然成功");
        CheckEq(again["slot"].AsInt32(), 2, "覆盖的是最旧那一格（第 2 格）");
        CheckTrue(again["overwrite"].AsBool(), "结果里标着 overwrite=true");
        CheckTrue(again["message"].AsString().Contains("已覆盖"), "提示写明覆盖了最旧存档");
        CheckGt(store.describe_slot(2, null)["saved_unix"].AsInt32(), 100, "第 2 格已经换成了新档");
        CheckEq(store.describe_slot(1, null)["saved_unix"].AsInt32(), slot1Before, "第 1 格没被动过");
        CheckEq(store.describe_slot(3, null)["saved_unix"].AsInt32(), slot3Before, "第 3 格没被动过");
    }

    /// <summary>
    /// 读档时按当前配表收口：档里穿着「配表装不下」的装备时，读进来退回背包**并且整理完写回存档**。
    /// 起因：槽位是配表驱动的（设计可以删槽位或把 `max_equip` 改小），而穿戴记录留在存档里——
    /// 不写回的话每次读档都要重收一遍，档里一直留着旧记录（决策 101）。
    /// </summary>
    private void _CheckStaleEquippedWriteback()
    {
        TableDb db = FreshDb();
        string dirPath = TestDir + "/stale_equip";
        _CleanDir(dirPath);
        SaveStore store = SaveStore.create(dirPath, Slots);
        store.ensure_dir();
        MenuController controller = MenuController.create(db, store, System.Array.Empty<string>(), "normal");
        Godot.Collections.Dictionary created = controller.new_game(new Godot.Collections.Dictionary());
        CheckTrue(created["ok"].AsBool(), "先把一份新档写进去");
        GodotObject state = created["state"].AsGodotObject();
        string char_id = state.Get("char_ids").AsStringArray()[0];
        var inventory = (Inventory)state.Get("inventory").AsGodotObject();
        string r1 = inventory.add_equipment(db, "eq_ring_01", new Godot.Collections.Array());
        string r2 = inventory.add_equipment(db, "eq_ring_02", new Godot.Collections.Array());
        string r3 = inventory.add_equipment(db, "eq_ring_03", new Godot.Collections.Array());
        // 手工造一份「旧配表允许 3 枚戒指」的档：戒指槽塞 3 格（当前 max_equip=2）
        Godot.Collections.Dictionary slots = inventory.equipped.ContainsKey(char_id)
            ? inventory.equipped[char_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        slots["ring"] = new Godot.Collections.Array { r1, r2, r3 };
        inventory.equipped[char_id] = slots;
        CheckTrue(store.save_slot(1, state)["ok"].AsBool(), "把这档「旧配表」写进去");

        Godot.Collections.Dictionary loaded = controller.load_game(1);
        CheckTrue(loaded["ok"].AsBool(), "读档成功");
        GodotObject loaded_state = loaded["state"].AsGodotObject();
        CheckEq(loaded_state.Get("reclaimed_equipped").AsInt32(), 1, "读档时发现 1 件穿不上的装备");
        CheckTrue(
            loaded["message"].AsString().Contains("退回"),
            $"提示写明按配表退回了装备：{loaded["message"].AsString()}");
        var loaded_inventory = (Inventory)loaded_state.Get("inventory").AsGodotObject();
        CheckFalse(loaded_inventory.is_equipped(r3), "第 3 枚不再算穿着");
        CheckTrue(loaded_inventory.has_equipment(r3), "第 3 枚回到背包（实例没丢）");

        // 整理完要写回：再读一次不该还有可退的（不然每次读档都在重收，档里一直留着旧记录）
        Godot.Collections.Dictionary again = controller.load_game(1);
        CheckEq(
            again["state"].AsGodotObject().Get("reclaimed_equipped").AsInt32(),
            0,
            "写回后再读没有可退的（整理真的落盘了）");
    }

    /// <summary>
    /// 表现层：实例化真实场景、点真实按钮走一遍
    /// 「点新建 → 只切创建界面（不落盘）→ 走完创建 → 落盘 ＋ 进枢纽页 → 回菜单读取 → 会话状态就位」。
    /// </summary>
    private void _CheckSceneFlow()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，UI 冒烟无法进行");
            return;
        }

        _CleanDir(TestDir);
        SaveStore store = SaveStore.create(TestDir, Slots);
        store.ensure_dir();

        var menu = GD.Load<PackedScene>(MenuScene).Instantiate() as MainMenu;
        CheckNotNull(menu, "菜单场景挂得上 MainMenu");
        if (menu == null)
        {
            return;
        }

        menu.store_override = store;
        var switched = new System.Collections.Generic.List<Variant>();
        menu.scene_switch_handler = Callable.From<Variant>(value => switched.Add(value));
        var quit_called = new System.Collections.Generic.List<bool>();
        menu.quit_handler = Callable.From(() => quit_called.Add(true));
        Tree.Root.AddChild(menu);
        menu.setup();

        CheckTrue(menu.status_text().Contains("暂无存档"), "空存档时状态栏给提示");
        var new_button = menu.FindChild("NewGameButton", true, false) as Button;
        var load_button = menu.FindChild("LoadGameButton", true, false) as Button;
        var quit_button = menu.FindChild("QuitButton", true, false) as Button;
        CheckNotNull(new_button, "有「新建游戏」按钮");
        CheckNotNull(load_button, "有「读取存档」按钮");
        CheckNotNull(quit_button, "有「退出游戏」按钮");
        if (new_button == null || load_button == null || quit_button == null)
        {
            menu.Free();
            return;
        }

        CheckEq(new_button.Text, "新建游戏", "新建按钮文案");
        CheckEq(load_button.Text, "读取存档", "读取按钮文案");
        CheckEq(quit_button.Text, "退出游戏", "退出按钮文案");
        CheckTrue(load_button.Disabled, "没有存档时读取按钮置灰");
        CheckTrue(menu.slot_button(1).Disabled, "空槽按钮不可点");

        // 点「新建游戏」：**只切到创建界面**（设计 13：创建没走完不写存档）
        new_button.EmitSignal("pressed");
        CheckEq(switched.Count, 1, "点新建游戏切到创建界面");
        CheckEq(switched[0].AsString(), CreationScene, "切的是创建角色场景");
        CheckFalse(store.slot_exists(1), "创建没走完**不写存档**");

        // 走创建界面：真实场景、真实按钮 → 确认 → 落盘 + 进枢纽页
        var creation = GD.Load<PackedScene>(CreationScene).Instantiate() as CreationScreen;
        CheckNotNull(creation, "创建场景挂得上 CreationScreen");
        if (creation == null)
        {
            Tree.Root.RemoveChild(menu);
            menu.Free();
            return;
        }

        creation.store_override = store;
        var creation_switched = new System.Collections.Generic.List<string>();
        creation.scene_switch_handler =
            Callable.From<Variant>(value => creation_switched.Add(value.AsString()));
        // 确认那一下会走 `CreationService.build` 的「改过名字」那条路，往 autoload 的
        // `GameData.db` 注入一行自建模板（`TableDb.inject_row` 只影响本实例，而这里用的正是
        // autoload 那一份）——**它会影响同进程后面跑的角色面板与战斗用例**（2026-10-11 实测：
        // 书生的显示名变成「陆文昭」，`TestCharacterSheet` 与 `TestBattleSession` 当场红）。
        // 所以先记下原表，验完还原。
        GodotObject game_db = Tree.Root.GetNodeOrNull<Node>("GameData")?.Get("db").AsGodotObject();
        Godot.Collections.Dictionary tables =
            game_db != null ? game_db.Get("tables").AsGodotDictionary() : null;
        Variant saved_character_base = tables != null && tables.ContainsKey("character_base")
            ? tables["character_base"]
            : default;
        Tree.Root.AddChild(creation);
        creation.setup();
        var origin_button = creation.FindChild("Origin_ori_scholar", true, false) as Button;
        CheckNotNull(origin_button, "创建界面有出身卡按钮（ori_scholar）");
        origin_button?.EmitSignal("pressed");
        var confirm_button = creation.FindChild("ConfirmButton", true, false) as Button;
        CheckNotNull(confirm_button, "创建界面有「确认创建」按钮");
        CheckTrue(confirm_button != null && !confirm_button.Disabled, "选了出身就能确认（不是禁用态）");
        confirm_button?.EmitSignal("pressed");
        CheckTrue(store.slot_exists(1), "确认创建后落盘");
        CheckEq(creation_switched.Count, 1, "确认后进游戏");
        CheckEq(creation_switched[0], PlaceholderScene, "新建游戏落在占位枢纽页");
        Tree.Root.RemoveChild(creation);
        creation.Free();

        Node session = Tree.Root.GetNodeOrNull<Node>("GameSession");
        CheckNotNull(session, "GameSession 单例在场");
        CheckTrue(session != null && session.Get("state").AsGodotObject() != null, "会话状态被写入 GameSession");

        // 回菜单重来一次，验证「读取存档」真的能载入
        menu._refresh();
        menu.press_load_game();
        CheckFalse(load_button.Disabled, "有存档后读取按钮可用");
        Button slot_button = menu.slot_button(1);
        CheckNotNull(slot_button, "槽位按钮已生成");
        if (slot_button != null)
        {
            CheckFalse(slot_button.Disabled, "有档的槽位按钮可点");
            slot_button.EmitSignal("pressed");
            CheckEq(switched.Count, 2, "读取后再次进入游戏");
            CheckEq(menu.pending_scene, OverworldScene, "读档落在世界地图（设计 09 §二）");
            if (switched.Count >= 2)
            {
                CheckEq(switched[1].AsGodotObject().Get("slot").AsInt32(), 1, "载入的是第 1 格");
            }
        }

        quit_button.EmitSignal("pressed");
        CheckEq(quit_called.Count, 1, "退出按钮触发退出回调");

        // 占位游戏场景：显示当前会话状态，并能退回菜单
        var placeholder = GD.Load<PackedScene>(PlaceholderScene).Instantiate() as PlaceholderGame;
        CheckNotNull(placeholder, "占位场景挂得上 PlaceholderGame");
        if (placeholder != null)
        {
            Tree.Root.AddChild(placeholder);
            placeholder.setup();
            CheckGt(placeholder.state_lines().Length, 0, "占位场景能取到会话状态");
            CheckTrue(placeholder.state_lines()[2].Contains("队伍"), "占位场景展示队伍");
            var back = placeholder.FindChild("BackButton", true, false) as Button;
            CheckNotNull(back, "占位场景有返回按钮");
            var back_calls = new System.Collections.Generic.List<bool>();
            placeholder.back_handler = Callable.From(() => back_calls.Add(true));
            back?.EmitSignal("pressed");
            CheckEq(back_calls.Count, 1, "返回按钮触发回退");
            Tree.Root.RemoveChild(placeholder);
            placeholder.Free();
        }

        Tree.Root.RemoveChild(menu);
        menu.Free();
        session?.Call("clear");

        // **还原要在最后**：确认那一下会注入自建模板，**读档那一下还会再注入一次**
        // （`GameState.from_dict` 的 `_apply_custom_templates` 会把 `custom_templates` 里那行写回 db），
        // 两次都落在 autoload 的 `GameData.db` 上。不还原的话，同进程后面跑的角色面板
        // （`CharacterSheet.display_name()` 读的就是模板行）与战斗用例会读到「陆文昭」。
        if (tables != null)
        {
            tables["character_base"] = saved_character_base;
        }
    }

    private static void _CleanDir(string path)
    {
        if (DirAccess.DirExistsAbsolute(path))
        {
            using DirAccess dir = DirAccess.Open(path);
            if (dir != null)
            {
                foreach (string fileName in dir.GetFiles())
                {
                    dir.Remove(fileName);
                }
            }
        }
        DirAccess.MakeDirRecursiveAbsolute(path);
    }
}
