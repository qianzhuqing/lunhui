using Godot;
using Lunhui.Audio;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 战斗界面。C# 版，对位原 `src/ui/battle_screen.gd`（2073 行）。
///
/// 逐回合播放 `BattleSimulator`：可以「下一回合」手动推进，也可以自动战斗（1x/2x/4x 调速）。
/// 打完之后结算掉落与经验（`BattleReward`），把明雷标成已清，再回大地图。
///
/// 自检：`-- --battle-selftest`，自动打完并按退出码报告。
///
/// 为了守 800 行硬上限，这个面板拆成七份 partial（原 `.gd` 2073 行 × 1.4 ≈ 2900）：
/// 本文件＝骨架／常量／环境／浮层／造敌人，`BattleScreenCommands.cs`＝六指令与回合推进，
/// `BattleScreenFloats.cs`＝跳字与受击／出手表现＋自动战斗与撤退，
/// `BattleScreenSettle.cs`＝结算（掉落／经验／旗标／写回气血／败北），
/// `BattleScreenView.cs`＝界面绑定与招式行／增益条，`BattleScreenActors.cs`＝单位卡片与日志，
/// `BattleScreenSelfTest.cs`＝整段自检（棘轮表盯的是**自检那一份**）。
///
/// **跨语言面**：`party_builder` 已是 C#（`src/core/PartyBuilder.cs`）——直调静态口，
/// 不再需要 `GD.Load<GDScript>(…)`；
/// `game_state`／`battle_actor`／`npc_service` 已是 C#，直调
/// （阵营常量直接读 `BattleActor.SIDE_*`，不再走 `GetScriptConstantMap()`）；
/// `Sfx`（`src/audio/Sfx.cs`）已是 C#，直调 `Sfx.play(...)`；
/// 其余（`TableDb`／`Encounter`／`EnemyFactory`／`DropResolver`／`BattleReward`／`RngService`／
/// `PityTracker`／`AffixRoller`／`MasteryService`／`PracticeService`／`SkillGrant`／`SaveService`／
/// `SettingsStore`／`IconPaths`／`CopyGuard`／`LayoutBudget`）都已是 C#，直调。
///
/// **给用例直接读写的字段**（`tests/TestBattleUi.cs` 直接读写）：`state_override`／
/// `encounter_override`／`modifiers_override`／`save_store_override`／`return_handler`／`rng_seed`／
/// `db`／`state`／`encounter`／`allies`／`enemies`／`sim`／`settled`／`selected_target_id`／
/// `_enemies_box`／`_log` 一律 `[Export]`；`db` 与 `encounter` 是**自定义 RefCounted 类**，
/// 导出类型写成 `GodotObject` 最稳（类内部走强类型视图 `Db`／`Enc`）。
/// </summary>
public partial class BattleScreen : Control
{
	// ------------------------------------------------------------------ 跨语言：还是 GDScript 的类

	/// <summary>阵营常量的唯一出处是 `BattleActor`（2026-10-11 起是 C#，直接引用）。</summary>
	private static int _side_ally() => BattleActor.SIDE_ALLY;

	private static int _side_enemy() => BattleActor.SIDE_ENEMY;

	/// <summary>胜负与策略常量的唯一出处是 `BattleSimulator`（2026-10-11 起是 C#，直接引用）。</summary>
	private static string _winner_ally() => BattleSimulator.winner_ally();

	private static string _winner_enemy() => BattleSimulator.winner_enemy();

	private static string _winner_flee() => BattleSimulator.winner_flee();

	private static string _winner_draw() => BattleSimulator.winner_draw();

	private static string _strategy_all_out() => BattleSimulator.strategy_all_out();

	// ------------------------------------------------------------------ 场景与常量

	/// <summary>浮层（设计 14 §二：**战斗中按 Tab 开角色面板属于「浮层盖场景层」**）</summary>
	private const string CHARACTER_SCENE = "res://scenes/character_screen.tscn";
	private const string CLUE_SCENE = "res://scenes/clue_screen.tscn";
	private const string DUNGEON_SCENE = "res://scenes/dungeon_screen.tscn";

	private const string WORLD_SCENE = "res://scenes/world_run.tscn";
	private const string LOCAL_SCENE = "res://scenes/local_run.tscn";
	public const double AUTO_INTERVAL = 0.8;

	/// <summary>长按战场多久算「长按」（22 §1.6：按住 0.4 秒呼出暂停条）</summary>
	public const double PAUSE_HOLD_SECONDS = 0.4;
	private static readonly double[] SPEED_STEPS = { 1.0, 2.0, 4.0 };

	/// <summary>
	/// 自检固定种子（与 tests/TestBattleUi.cs 用同一个）：命中／暴击／掉落都是掷出来的，
	/// 不固定时同一份代码会时红时绿——2026-10-03 实测过一次：`--battle-selftest` 的
	/// 「六指令」那行因为普攻落空而随机变红。
	/// </summary>
	public const int SELFTEST_SEED = 20261003;

	/// <summary>
	/// 结算面板的掉落块最多摆几格（**两列往外铺**，第八版口径：「不再压成一件」）。
	///
	/// 旧版「三行卡片 ＋ 48 字」那套硬挤压随第八版面一起退休（口径②：面板按区铺、每区不限行）；
	/// 这个格数上限是「实在放不下的仍进战报」那条的落点——`_describe_drops()` 那一行始终是全量。
	/// </summary>
	public const int SETTLE_DROP_ITEMS = 8;

	// GDScript 读不到 C# 的 `const`（坑⑩），这几个取值口是给用例钉绝对值的（现由 `tests/TestBattleUi.cs` 用）。
	public static double auto_interval() => AUTO_INTERVAL;
	public static int selftest_seed() => SELFTEST_SEED;
	public static int settle_drop_items() => SETTLE_DROP_ITEMS;

	// `TEAM_WIN_FLAGS` 的取值口：GDScript 读不到 C# 的「静态 readonly」（坑⑩），
	// 而 `tests/test_recruit.gd` 要拿它验「两支队伍的旗标来源都在」。
	public static Godot.Collections.Dictionary team_win_flags() => TEAM_WIN_FLAGS;

	/// <summary>
	/// 「打赢某支队伍 → 置哪枚旗标」：设计 20 §十一 里靠战斗收尾的那两个旗标来源。
	/// 表里还没有对应列（只给了口径），已记 `待策划确认.md` Q69；给列就挪进表、这里改成查表。
	/// </summary>
	public static readonly Godot.Collections.Dictionary TEAM_WIN_FLAGS = new()
	{
		{ "team_butcher", "flag_huangcun_done" },
		{ "team_poison_hand", "flag_poison_hall" },
		// 幕五「聚义厅对质」的进入条件（20 号 §十一：`flag_heifeng_confront` = 战大寨主后）
		{ "team_boss", "flag_heifeng_confront" },
	};

	/// <summary>
	/// 打赢某支队伍时随掉落一起到手、且**只领一次**的剧情物（20 号 §七：账册在幕五「对质」之后到手）。
	///
	/// 为什么挂在掉落清单里：`BattleReward` 那条唯一入账口径会把 `drops` 里的条目写进「掉落：…」那一行，
	/// 玩家看得见「账册」是怎么来的，而**结算卡片的行数是钉住的**（多一行就顶出设计分辨率），
	/// 所以不另起一行战报。为什么按「背包里有没有」判唯一：账册是钥匙道具——不能丢（`is_key_item=1`）、
	/// 也不在任何货架上（卖不掉），所以「已持有」＝「领过了」，不必再记一份首杀簿。
	/// </summary>
	public static readonly Godot.Collections.Dictionary TEAM_WIN_ITEMS = new() { { "team_boss", "item_bd_ledger" } };

	// ------------------------------------------------------------------ 注入点与状态（GDScript 读写 → [Export]）

	[Export] public Variant state_override = default;

	/// <summary>遭遇覆盖（用例注入指定敌人队伍；正常游玩从 GameSession.pending_encounter 取）</summary>
	[Export] public Variant encounter_override = default;

	/// <summary>战斗修正覆盖（用例注入 force_* 开关；正常游玩为空）</summary>
	[Export] public Godot.Collections.Dictionary modifiers_override = new();

	/// <summary>存档设施（用例可注入临时目录）</summary>
	[Export] public GodotObject save_store_override;

	/// <summary>设置存储（自动战斗开关）；自检注入临时目录</summary>
	[Export] public GodotObject settings_override;

	[Export] public Callable return_handler;

	/// <summary>随机种子：-1 = 随机（正常游玩）；自检与用例传固定值，让命中／掉落可复现</summary>
	[Export] public int rng_seed = -1;

	[Export] public GodotObject db;
	[Export] public GodotObject state;
	[Export] public GodotObject encounter;
	[Export] public Godot.Collections.Array allies = new();
	[Export] public Godot.Collections.Array enemies = new();
	[Export] public GodotObject sim;

	[Export] public bool settled;
	[Export] public bool auto_enabled;

	/// <summary>玩家点选的敌人（空 = 自动打血量最低的）</summary>
	[Export] public string selected_target_id = "";
	[Export] public int speed_index;
	[Export] public double auto_timer;

	/// <summary>`db` 的强类型视图（表库本来就是 C#，这里只是把 `GodotObject` 收窄一次）。</summary>
	private TableDb Db => db as TableDb;

	/// <summary>`encounter` 的强类型视图（同上：类内部一律走它，导出字段只负责给 GDScript 看）。</summary>
	private Encounter Enc => encounter as Encounter;

	/// <summary>镜头层（22 §1.4）：战场上所有单位（含头顶条／状态图标／预兆签／相性行）都挂在它下面。</summary>
	private Node2D _stage_root;

	/// <summary>战场上的单位（`actor_id` → 节点）：跳字／出手／选人按它定位。</summary>
	private readonly System.Collections.Generic.Dictionary<string, Control> _units_by_id = new();

	/// <summary>整屏之后「敌方列表」就是 `StageRoot` 本身——用例按名字找战场单位要走这里。</summary>
	[Export] public Node _enemies_box;

	private Label _round_label;
	[Export] public RichTextLabel _log;
	private GridContainer _skill_row;
	private Button _basic_button;
	private Button _skill_button;
	private Button _inner_button;
	private Button _item_button;
	private Button _defend_button;

	/// <summary>展开浮层显示哪一组：空＝收起（指令行常驻）／skills／inner／items（22 §1.6：三者互斥）</summary>
	private string _panel_mode = "";

	/// <summary>暂停条开着吗（22 §1.6：与指令行／展开浮层三者互斥，**呼出式、不常驻**）</summary>
	private bool _pause_open;

	/// <summary>长按战场的手势：按下起算，够 `PAUSE_HOLD_SECONDS` 就呼出暂停条（第六版起只有这一种手势）</summary>
	private double _hold_time;
	private bool _holding;

	/// <summary>指令类按钮的公共开场日志（开新回合、推进到轮到我方）</summary>
	private Godot.Collections.Array _command_lines = new();

	/// <summary>结算面板那一颗「确定」的焦点抢过一次没有（抢第二次会把焦点从别处拽回来）</summary>
	private bool _settle_focus_taken;
	/// <summary>结算那一帧的挂件收拾（收起战报、把「战报」钮提到压暗底之上）只做一次</summary>
	private bool _settle_hud_applied;

	private Label _status;
	private Button _result_confirm;
	private Button _auto_button;
	private Button _speed_button;
	private Button _strategy_button;
	private Button _flee_button;
	private Button _log_button;
	private Button _report_button;
	private PanelContainer _log_panel;
	private PanelContainer _layer;
	private HBoxContainer _command_row;
	private HBoxContainer _pause_bar;
	private Control _boss_bar;
	private Label _boss_name;
	private Label _boss_phase;
	private Label _boss_poise_label;
	private HBoxContainer _boss_aff;
	private HBoxContainer _boss_seg;
	private HBoxContainer _boss_poise;
	private VBoxContainer _turn_bar;

	/// <summary>指令行上方那一行播报的淡出补间（22 §1.6：3 秒后收）</summary>
	private Tween _status_tween;

	/// <summary>场景里的节点只绑定一次（setup() 可能被外部再调一次）</summary>
	private bool _ui_bound;

	/// <summary>浮层栈（设计 18.1／14 §二）：战斗界面是**场景层**，角色面板／线索本／完成度压在上面</summary>
	private GodotObject _overlays;

	/// <summary>伤害跳字的浮层（不受卡片容器排版影响）</summary>
	private Control _float_layer_node;

	private SettingsStore _settings_store;
	private SaveService _save_service;

	public override void _Ready()
	{
		// 种子要在 `setup()` 之前定：模拟器是在 setup 里用 rng_seed 造的
		if (_has_user_arg("--battle-selftest"))
		{
			rng_seed = SELFTEST_SEED;
		}

		setup();
		if (_has_user_arg("--battle-selftest"))
		{
			Callable.From(_run_battle_selftest).CallDeferred();
		}
	}

	// ------------------------------------------------------------------ 浮层（全局快捷键）

	public GodotObject overlays() => _overlays ??= new OverlayStack();

	/// <summary>
	/// 全局快捷键（设计 14 §八）：**战斗中也能开角色／行囊／线索本／完成度**——
	/// 它们是「浮层盖场景层」，与地图上的行为一致；`Esc` 弹一层（战斗指令不受影响）。
	/// </summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			// Esc 两层语义（14 §八 ＋ 22 §1.6）：**有浮层先弹浮层**（战斗中开角色面板那一层），
			// 没有浮层才轮到暂停条——不然浮层盖着的时候 Esc 会去开合底下那一条。
			if (!close_top_overlay())
			{
				toggle_pause();
			}

			return;
		}

		if (@event.IsActionPressed("open_character"))
		{
			open_overlay(CHARACTER_SCENE, "CharacterPanel", 0);
			return;
		}

		if (@event.IsActionPressed("open_bag"))
		{
			open_overlay(CHARACTER_SCENE, "CharacterPanel", 2);
			return;
		}

		if (@event.IsActionPressed("show_clues"))
		{
			open_overlay(CLUE_SCENE, "CluePanel", 0);
			return;
		}

		if (@event.IsActionPressed("show_progress"))
		{
			open_overlay(DUNGEON_SCENE, "DungeonPanel", 0);
		}
	}

	/// <summary>开一个浮层（同一个场景重复按 → 弹回它并刷新，不叠第二份）。</summary>
	public Godot.Collections.Dictionary open_overlay(string scene_path, string node_name, int tab_index = 0)
	{
		Node existing = FindChild(node_name, true, false);
		if (existing != null)
		{
			foreach (Variant closed in overlays().Call("push", node_name).AsGodotArray())
			{
				_free_overlay(closed.AsString());
			}

			existing.Set("state_override", state);
			existing.Call("refresh");
			if (existing.HasMethod("select_tab"))
			{
				existing.Call("select_tab", tab_index);
			}

			return new Godot.Collections.Dictionary { { "ok", true }, { "reopened", true } };
		}

		Node panel = GD.Load<PackedScene>(scene_path).Instantiate();
		if (panel == null)
		{
			return new Godot.Collections.Dictionary
			{
				{ "ok", false }, { "error", $"浮层加载失败：{scene_path}" },
			};
		}

		panel.Name = node_name;
		panel.Set("state_override", state);
		if (node_name == "CluePanel")
		{
			panel.Set("scope", "region");
			panel.Set("scene_id", Enc?.source_scene ?? "");
		}
		else if (node_name == "DungeonPanel")
		{
			panel.Set("scene_id", Enc?.source_scene ?? "");
		}

		// 角色界面用的是 `back_handler`，其余两个面板用 `return_handler`（各自既有的口径）
		if (node_name == "CharacterPanel")
		{
			string closing = node_name;
			panel.Set("back_handler", Callable.From(() => _close_overlay(closing)));
		}
		else
		{
			string closing = node_name;
			panel.Set("return_handler", Callable.From(() => _close_overlay(closing)));
		}

		AddChild(panel);
		panel.Call("setup");
		if (panel.HasMethod("select_tab"))
		{
			panel.Call("select_tab", tab_index);
		}

		overlays().Call("push", node_name);
		return new Godot.Collections.Dictionary { { "ok", true }, { "reopened", false } };
	}

	public bool close_top_overlay()
	{
		string top_id = overlays().Call("pop").AsString();
		if (top_id.Length == 0)
		{
			return false;
		}

		_free_overlay(top_id);
		return true;
	}

	private void _close_overlay(string id)
	{
		foreach (Variant extra in overlays().Call("remove", id).AsGodotArray())
		{
			_free_overlay(extra.AsString());
		}

		_free_overlay(id);
	}

	private void _free_overlay(string id)
	{
		Node panel = FindChild(id, true, false);
		panel?.QueueFree();
	}

	public int open_overlay_count() => overlays().Call("depth").AsInt32();

	// ------------------------------------------------------------------ 装配

	public void setup()
	{
		if (sim != null)
		{
			return;
		}

		db = _resolve_db();
		state = current_state();
		Node session_node = _session_node();
		if (state == null)
		{
			// 自检或直接开战斗界面时没有会话状态：临时开一局（奖励会落在这份临时存档上）
			state = GameState.new_game((TableDb)db, "normal", System.Array.Empty<string>(), 0);
			session_node?.Call("set_state", state);
		}

		encounter = (session_node != null ? session_node.Get("pending_encounter") : default).AsGodotObject();
		if (encounter_override.VariantType != Variant.Type.Nil)
		{
			encounter = encounter_override.AsGodotObject();
		}

		encounter ??= _fallback_encounter();
		// 战斗外增益（08）：会话里还活着的带进战场，进战斗后维持整场
		Godot.Collections.Array field_buffs = session_node != null
			? session_node.Call("active_field_buffs", -1).AsGodotArray()
			: new Godot.Collections.Array();
		allies = PartyBuilder.build_actors((TableDb)db, state, field_buffs);
		enemies = _build_enemies(Enc);
		sim = BattleSimulator.create((TableDb)db, RngService.create(rng_seed));
		sim.Call("setup", allies, enemies, new Godot.Collections.Dictionary
		{
			{ "encounter", encounter },
			{ "modifiers", modifiers_override.Duplicate() },
		});
		_bind_ui();
		// 自动战斗持久开关（设计 11 §四）：设置里开着，**进战斗就自动打**。
		// 玩家点任意指令立刻接管（`take_over()`）；这里只负责"进来就按设置走"。
		if (_b(settings().load_auto_battle(), "enabled"))
		{
			auto_enabled = true;
			_auto_advance(); // 0 ＝ 这一拍就开始打
		}
		else
		{
			// 第六版（流程自动推进）也**进场先留一拍**（`AUTO_INTERVAL`）再开打：
			// 一是玩家先看清场上站了谁，二是开场别让敌方抢在第一帧动手——
			// 跨场景闭环那几段（`CheckLoopRunner` 验结算与记录、不是验打得赢打不赢）也靠这一拍。
			auto_timer = AUTO_INTERVAL;
		}

		_refresh();
	}

	/// <summary>设置存储：自检注入 `settings_override`（临时目录），正常运行落默认存档目录旁。</summary>
	public SettingsStore settings() =>
		_settings_store ??= settings_override != null ? settings_override as SettingsStore : new SettingsStore();

	public GodotObject current_state()
	{
		if (state_override.VariantType != Variant.Type.Nil)
		{
			return state_override.AsGodotObject();
		}

		return _session_node()?.Get("state").AsGodotObject();
	}

	/// <summary>
	/// 存档设施：注入优先，否则用默认目录。与其他两个控制器同一套写法
	/// （保留一个实例，用例/自检也能查 `last_reason`，看清「什么时候存的」）。
	/// </summary>
	public SaveService save_service()
	{
		if (_save_service != null)
		{
			return _save_service;
		}

		GodotObject store = save_store_override ?? SaveService.make_default();
		_save_service = SaveService.create(store, state);
		return _save_service;
	}

	public string status_text() => _status != null ? _status.Text : "";

	public string round_text() => _round_label != null ? _round_label.Text : "";

	public bool finished() => sim != null && _sim_finished();

	public bool auto_running() => auto_enabled;

	/// <summary>
	/// 结算面板上**看得见的字**（自上而下一行行拼起来）：第八版起面板替掉了那个三行卡片 Label，
	/// 这个口子的含义跟着变成"面板上写了什么"。**直接走节点树**，不另存一份副本——副本会与实际显示漂开。
	/// </summary>
	public string result_label_text()
	{
		if (_settle_column == null)
		{
			return "";
		}

		var parts = new System.Collections.Generic.List<string>();
		_collect_text(_settle_column, parts);
		return string.Join("\n", parts);
	}

	/// <summary>把一棵子树里的 Label 文本按顺序收起来（`result_label_text()` 用）</summary>
	private static void _collect_text(Node node, System.Collections.Generic.List<string> parts)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is Label label && label.Text.Length > 0)
			{
				parts.Add(label.Text);
			}

			_collect_text(child, parts);
		}
	}

	// ------------------------------------------------------------------ 环境

	private Encounter _fallback_encounter()
	{
		// 没有待处理遭遇时（例如自检直接开战斗界面），用野狼群走一遍
		// （`sp_lp_wolf_01` 这个写死的 id 由 `test_handshake` 的「字面量查表」门限盯着，
		//   表里改掉了它会在自检里当场点名，而不是运行时在这行静默炸掉。）
		Resource row = Db.get_row("roaming_spawn", "sp_lp_wolf_01");
		Resource team = Db.get_row("enemy_team", row.Get("team_id").AsString());
		return Encounter.build(Db, row, team, Encounter.contact_front(), "normal");
	}

	private TableDb _resolve_db()
	{
		GodotObject game_data = _session_node("GameData");
		GodotObject game_db = game_data?.Get("db").AsGodotObject();
		if (game_db != null && game_db.Get("tables").AsGodotDictionary().Count > 0)
		{
			return game_db as TableDb;
		}

		var table_db = new TableDb();
		table_db.load_all();
		return table_db;
	}

	private Node _session_node(string node_name = "GameSession") => _tree()?.Root.GetNodeOrNull(node_name);

	/// <summary>不在场景树里时 `get_tree()` 会打一条 ERROR（`--script` 模式用例直接 new 节点就会踩到）。</summary>
	private SceneTree _tree()
	{
		if (IsInsideTree())
		{
			SceneTree tree = GetTree();
			if (tree != null)
			{
				return tree;
			}
		}

		return Engine.GetMainLoop() as SceneTree;
	}

	private static bool _has_user_arg(string flag) => System.Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;

	/// <summary>自检用：全场血量总和，用来判断「推进是否真的产生了变化」</summary>
	private static int _hp_total(Godot.Collections.Array actors)
	{
		int total = 0;
		foreach (Variant actor_value in actors)
		{
			total += System.Math.Max(0, _prop_int(actor_value, "hp"));
		}

		return total;
	}

	// ------------------------------------------------------------------ 造敌人

	/// <summary>
	/// 切磋的镜像对手（Q88 拍板 ①）：把这位同伴**照他自己**造一个战斗单位，翻到敌方阵营。
	///
	/// 用的是与他上场时**同一个** `PartyBuilder.build_actor()`——等级、加点、装备、内功、招式全同，
	/// 所以「赢的是他自己的镜像」这句话是真的，而不是另配一套数值。
	///
	/// 两处刻意的收口：① **满血**（`refill()`）——同伴带伤不该让这一场变得更好打；
	/// ② **不给经验、钱、掉落**（同伴不是敌人，表里没有他的 `exp_reward`；设计 19 §2.2 里切磋的
	/// 收益就是好感那一条）。这两条若与设计口径不合，改这里一处即可。
	/// </summary>
	public BattleActor _build_mirror_enemy(string char_id)
	{
		BattleActor actor = PartyBuilder.build_actor((TableDb)db, state, char_id);
		if (actor == null)
		{
			GD.PushError("[battle] 切磋镜像造不出来（character_base 里没有这个同伴）");
			return null;
		}

		actor.side = _side_enemy();
		actor.actor_id = $"mirror_{char_id}";
		actor.reward_exp = 0;
		actor.reward_money = 0;
		actor.drop_group = "";
		actor.tags["faction"] = "companion";
		actor.refill();
		return actor;
	}

	/// <summary>打完回哪：大地图或来时的那个小地图</summary>
	private string _return_scene()
	{
		Node session_node = _session_node();
		if (session_node != null)
		{
			string pending = session_node.Get("pending_return_scene").AsString();
			if (pending.Length > 0)
			{
				return pending;
			}
		}

		return WORLD_SCENE;
	}

	/// <summary>敌人在遭遇里可能带着 members（房间队伍、隐藏 Boss），否则按 team_id 建</summary>
	private Godot.Collections.Array _build_enemies(Encounter enc)
	{
		EnemyFactory factory = EnemyFactory.create(Db);
		// 同伴的切磋没有表里的队伍：对手是**他自己**（Q88 拍板 ①）——按人物现造一个镜像
		if (enc.mirror_char.Length > 0)
		{
			GodotObject mirror = _build_mirror_enemy(enc.mirror_char);
			var one = new Godot.Collections.Array();
			if (mirror != null)
			{
				one.Add(mirror);
			}

			return one;
		}

		if (enc.members.Length == 0)
		{
			return factory.create_team(enc.team_id, enc.difficulty_id);
		}

		var out_actors = new Godot.Collections.Array();
		foreach (string part in enc.members.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
		{
			string[] pieces = part.Split(':', System.StringSplitOptions.RemoveEmptyEntries);
			if (pieces.Length == 0)
			{
				continue;
			}

			string enemy_id = pieces[0].Trim();
			int count = pieces.Length > 1 ? pieces[1].ToInt() : 1;
			for (int index = 0; index < System.Math.Max(1, count); index++)
			{
				GodotObject actor = factory.create_enemy(enemy_id, enc.difficulty_id);
				if (actor == null)
				{
					continue;
				}

				actor.Set("actor_id", $"{enemy_id}#{index + 1}");
				out_actors.Add(actor);
			}
		}

		return out_actors;
	}

	/// <summary>
	/// 流程自动推进（22 §1.6 第六版）：**没有手动推进入口**——一条指令结算完就走到下一个出手的人；
	/// 玩家只在**轮到自己的人出手**时下指令，其余（敌人行动、回合末结算、DoT 跳字、演出）自己往下走。
	/// 口径注 ③：**暂停条呼出＝流程停住**（条开着时 tick 不推进），收起即继续。
	/// 自动战斗（`auto_enabled`）时连我方的招也自己出——那一层在 `press_next_round()` 里面。
	/// </summary>
	public override void _Process(double delta)
	{
		_tick_hold(delta);
		if (sim == null || _sim_finished())
		{
			return;
		}

		// 口径注 ③：不然玩家正调速度／翻战报，回合就自己推过去了。
		if (_pause_open)
		{
			return;
		}

		// 轮到我方就**停住等指令**（自动战斗才替他出招）：`press_next_round()` 在这儿本来就是空转，
		// 提前 return 只是不白跑计时器，顺便把节拍压回满值——玩家一出手，下一步隔一拍再走。
		if (!auto_enabled && _current_ally_actor() != null)
		{
			auto_timer = AUTO_INTERVAL;
			return;
		}

		auto_timer -= delta * speed();
		if (auto_timer <= 0.0)
		{
			auto_timer = AUTO_INTERVAL;
			press_next_round();
		}
	}

	private void _auto_advance() => auto_timer = 0.0;

	// ------------------------------------------------------------------ 小工具

	private bool _sim_finished() => sim == null || sim.Call("finished").AsBool();

	/// <summary>这个回调接上没有（空 Callable 与「根本没接」都算没接）。</summary>
	private static bool _callable_valid(Callable callable) =>
		callable.Delegate != null || (callable.Target != null && !callable.Method.IsEmpty);

	/// <summary>调这个回调（没接就不动）；返回值原样给出来，`sweep_handler` 那类要读它。</summary>
	private static Variant _invoke_callback(Callable callable, params Variant[] args)
	{
		if (!_callable_valid(callable))
		{
			return default;
		}

		return callable.Call(args);
	}

	/// <summary>对位 GDScript 的 `dict.get(key, fallback)`（读不存在的键在 C# 里会抛，坑 88）。</summary>
	private static Variant _v(Godot.Collections.Dictionary dict, string key) =>
		dict.ContainsKey(key) ? dict[key] : default;

	/// <summary>`dict.get(key, [])`：键不在或不是数组时给一个空数组（C# 侧 `AsGodotArray()` 不给兜底）。</summary>
	private static Godot.Collections.Array _arr_of(Godot.Collections.Dictionary dict, string key) =>
		dict.ContainsKey(key) && dict[key].VariantType == Variant.Type.Array
			? dict[key].AsGodotArray()
			: new Godot.Collections.Array();

	private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
		dict.ContainsKey(key) ? dict[key].AsString() : fallback;

	private static bool _b(Godot.Collections.Dictionary dict, string key) =>
		dict.ContainsKey(key) && dict[key].AsBool();

	private static int _i(Godot.Collections.Dictionary dict, string key, int fallback = 0) =>
		dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;

	private static double _f(Godot.Collections.Dictionary dict, string key, double fallback = 0.0) =>
		dict.ContainsKey(key) ? dict[key].AsDouble() : fallback;

	private static string _bool(bool value) => value ? "true" : "false";

	/// <summary>
	/// 按名字读一个对象的属性（战斗单位现在是 C# 的 `BattleActor`，技能行／表行也是 C# 的
	/// `[Export]` 属性——两种都按名字取得动，所以这一组助手是通用的）。
	/// </summary>
	private static Variant _prop(Variant target, string property) =>
		target.AsGodotObject()?.Get(property) ?? default;

	private static string _prop_str(Variant target, string property) => _prop(target, property).AsString();

	private static int _prop_int(Variant target, string property) => _prop(target, property).AsInt32();

	private static double _prop_float(Variant target, string property) => _prop(target, property).AsDouble();

	private static bool _actor_alive(Variant actor) =>
		actor.AsGodotObject()?.Call("is_alive").AsBool() ?? false;
}
