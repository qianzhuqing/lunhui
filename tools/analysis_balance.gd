## 平衡观测（只读分析，不是门限）：给策划看「现在的数值打起来是什么样」。
##
## 用法：
##   tools\run_balance_analysis.bat
##
## 它**不改任何数值、不写任何存档**，只跑战斗模拟并把原始数据打到 stdout，
## 由人工整理进 `docs/dev/平衡观测.md`。之所以不做成 `run_all_checks` 的一步：
## 它输出的是一张表，不是「通过/不通过」；而且策划改数值时这张表本来就该变，
## 把它做成门限只会让正常的数据调整变成假红。
##
## 口径（和报告里写的一致，改了这里也要改报告）：
##   两档队伍：① **1 人＝真实开局**（0.10.0 起队伍由 `recruit_def.is_initial` 决定，开局只有书生）
##             ② **4 人＝设计目标队伍**（`character_base` 前 4 行，与 `MAX_PARTY` 一致）
##   ＋起始装备、普通难度、满血开局、等级直接设成表里的等级，每档用 20 个固定种子各打一场。
extends SceneTree

const SEED_BASE := 20261003
const TRIES := 20
const LEVELS := [1, 3, 5, 8, 10, 12, 15]
## 队伍规模：1 = 真实开局（招募驱动，只有书生）；4 = 设计目标队伍（01 的角色卡，取前 4 行）
const PARTY_SIZES := [1, 4]
const TEAMS := [
	"team_wolf_pack", "team_boar_pair", "team_bandit_patrol", "team_lone_wolf",
	"team_gate_sentry", "team_elite_blade", "team_poison_hand", "team_boss_guards",
	"team_heifeng_elite", "team_boss", "team_butcher", "team_shixi_hound",
	# 隐藏 Boss（酔刀客）**没有 `enemy_team` 行**：小地图是拿 `team_hidden_<enemy_id>`
	# 这个合成 id 现拉一支单人队（`local_map_controller._boss_encounter`）。平衡表要看得见它
	# ——它是三条 ★5 内功路线之一，设计 10 §七 明写「必须可胜，10 级险胜」。
	"team_hidden_en_hidden_drunk",
]
## 合成队伍的前缀（见 TEAMS 里那条注释）
const HIDDEN_TEAM_PREFIX := "team_hidden_"
## 落雁坡明雷一次全清的收益按这个区算（报告里的「一次全清」）。
const SWEEP_REGION := "n_luoyanpo"


## 天赋 id → 中文名（表里的名字，报告头直接用，别在打印里出现 id）
func _talent_names(db, talents: Array) -> PackedStringArray:
	var out := PackedStringArray()
	for talent_id: Variant in talents:
		var row: Resource = db.get_row("talent_def", str(talent_id))
		out.append(str(row.name_cn) if row != null else str(talent_id))
	return out

## 「含天赋」那一档用的**代表 build**（2026-10-04 补，共 5 点、全是**已经接上**的效果）：
##   天生武胆（3 点：招式伤害 +15%、内伤抗性 −20%）＋ 眼疾手快（2 点：暴击率 +5%、命中 +5%）
## 为什么用这一套：① 与武器类型无关（剑／拳／刀／枪的队伍都能吃）；② 两条效果都已实现；
## ③ 它**不是最优解**，只是一套"玩家真可能选、而且偏战斗向"的 build——设计要换一套，
## 改这一个常量即可（`待策划确认.md` Q76 问的就是"这套代表 build 行不行"）。
const REPRESENTATIVE_TALENTS := ["tal_dan_shi", "tal_yan_ji"]


## 注意：下面几个 helper 的参数**故意不写类型**——`TableDb` 是 `RefCounted`，
## 写 `db: Resource` 会当场报「不是 Resource 的子类」并把进程挂在主循环里（本脚本踩过）。
## 这个脚本是「观察工具」而不是门限，宁可少写类型注解，也不要因为注解写错就出来一份空报告。
func _initialize() -> void:
	var db = load("res://src/core/table_db.gd").new()
	db.load_all()
	var GameStateScript := load("res://src/core/game_state.gd")
	var PartyBuilderScript := load("res://src/core/party_builder.gd")
	var EnemyFactoryScript := load("res://src/core/enemy_factory.gd")
	var SimScript := load("res://src/core/battle_simulator.gd")
	var RngScript := load("res://src/core/rng_service.gd")

	_dump_win_rates(db, GameStateScript, PartyBuilderScript, EnemyFactoryScript, SimScript, RngScript)
	_dump_win_rates(db, GameStateScript, PartyBuilderScript, EnemyFactoryScript, SimScript, RngScript, true)
	# 第三档（2026-10-04）：**含加点 ＋ 一套代表天赋**——玩家开局一定会花那 5 点，
	# 前两档量的都是"没选天赋"的队伍。build 见 REPRESENTATIVE_TALENTS 的注释。
	_dump_win_rates(
		db, GameStateScript, PartyBuilderScript, EnemyFactoryScript, SimScript, RngScript,
		true, REPRESENTATIVE_TALENTS
	)
	_dump_team_rewards(db)
	_dump_level_gates(db)
	_dump_sweep_reward(db)
	_dump_enemy_attrs(db, EnemyFactoryScript)
	# 收尾标记 + 主动退出。**放在最后一段之后**：它的意思是「整张表都打完了」。
	# 为什么必须有个标记：光靠 `--quit-after` 的退出码分不出"跑到最后"和"半路被运行期错误掐断"，
	# 而这个脚本打的**是一张表**——半截表和完整表长得一模一样，读的人只会以为"就这么多"。
	# 包装脚本 `run_balance_analysis.bat` 用 findstr 找这一行，找不到就判红（决策 147／148／187）。
	print("BALANCE SNAPSHOT: OK")
	quit(0)


## 胜率／平均回合／平均剩余气血：等级 × 敌人队伍。
##
## `with_points` 是**队伍模型**这一维（2026-10-04 补）：
##   false = 只把等级设过去、**升级点数一分不花**（老口径，留着做对照）；
##   true  = **把点数花掉**的真实队伍（口径见 `_spend_all_points`）。
## 为什么必须补这一档：`level_growth` 的基础列很平，裸队从 1 级到 12 级只涨约 40%，
## 于是设计 10 §七 的目标曲线（「精英 5～7 级有压力／8 级稳过」「大寨主 8 级险胜、10 级稳过」）
## **在裸队口径下数学上不可同时成立**——1 级都能过的敌人，10 级不可能只是险胜。
## 设计说的是**玩到那一步的队伍**，不是「把等级设成 10、点数没花」的队伍。
func _dump_win_rates(
	db, game_state_script, party_builder_script,
	enemy_factory_script, sim_script, rng_script,
	with_points: bool = false,
	talents: Array = []
) -> void:
	# 队伍用测试夹具 `party_state(db, N)` **显式点名**：0.10.0 起 `new_game()` 只给
	# `recruit_def` 的初始成员（开局一人），拿它当「4 人队」会静默量成单人
	# （性能工具踩过同一个坑，见框架说明决策 261）。
	var helper = load("res://tests/test_case.gd").new()
	for party_size: int in PARTY_SIZES:
		if with_points and party_size < 4:
			continue   # 单人真实开局本来就没有点数可花（1 级 0 点），不必重复出表
		var sample = helper.party_state(db, party_size)
		var names := PackedStringArray()
		for char_id: String in sample.char_ids:
			var row: Resource = db.get_row("character_base", char_id)
			names.append(str(row.name_cn) if row != null else char_id)
		print("---- 胜率（%d 人队：%s%s；每档 %d 个固定种子） ----" % [
			party_size, "、".join(names),
			(
				"；**含加点 ＋ 代表天赋 %s**" % "＋".join(_talent_names(db, talents))
				if with_points and not talents.is_empty()
				else ("；**含加点**" if with_points else "；不含加点（只设等级）")
			),
			TRIES,
		])
		print("等级\t队伍\t敌人等级\t胜\t负\t打不完\t平均回合\t平均剩余气血%")
		for level: int in LEVELS:
			for team_id: String in TEAMS:
				var team: Resource = db.get_row("enemy_team", team_id)
				if team == null and not team_id.begins_with(HIDDEN_TEAM_PREFIX):
					continue
				var enemy_levels := PackedStringArray()
				if team == null:
					var solo: Resource = db.get_row("enemy_base", team_id.trim_prefix(HIDDEN_TEAM_PREFIX))
					if solo == null:
						continue
					enemy_levels.append(str(int(solo.level)))
				else:
					for member: Dictionary in team.parsed_members():
						var enemy: Resource = db.get_row("enemy_base", str(member.get("enemy_id", "")))
						if enemy != null:
							enemy_levels.append(str(int(enemy.level)))
				var wins := 0
				var losses := 0
				var draws := 0
				var rounds_total := 0
				var hp_ratio_total := 0.0
				for i in range(TRIES):
					var state = helper.party_state(db, party_size)
					for char_id: String in state.char_ids:
						state.char_levels[char_id] = level
						state.char_hp.erase(char_id)
					if with_points:
						_spend_all_points(db, state)
					# 天赋：同一套代表 build 发给队里每个人（天赋是**每个角色**各选各的）
					if not talents.is_empty():
						for char_id: String in state.char_ids:
							state.talent_picks[char_id] = talents.duplicate()
					var allies: Array = party_builder_script.build_actors(db, state)
					var enemies: Array = _team_enemies(db, enemy_factory_script, team_id)
					var sim = sim_script.new(db, rng_script.new(SEED_BASE + i))
					var result: Dictionary = sim.simulate(allies, enemies)
					var winner := str(result["winner"])
					if winner == "ally":
						wins += 1
					elif winner == "enemy":
						losses += 1
					else:
						draws += 1
					rounds_total += int(result["rounds"])
					var hp_left := 0.0
					var hp_max := 0.0
					for ally in allies:
						hp_left += maxf(0.0, float(ally.hp))
						hp_max += float(ally.max_hp())
					hp_ratio_total += 0.0 if hp_max <= 0.0 else hp_left / hp_max * 100.0
				print("%d\t%s\t%s\t%d\t%d\t%d\t%.1f\t%.0f" % [
					level, team_id, ",".join(enemy_levels), wins, losses, draws,
					float(rounds_total) / float(TRIES), hp_ratio_total / float(TRIES),
				])


## 取一支队伍的打手：普通队伍查 `enemy_team`；`team_hidden_<enemy_id>` 按单人现拉
## （与 `local_map_controller._boss_encounter` 同一个约定）。
## 把「1 级到当前等级」的升级点数**全花掉**（真实玩家不会攒着不用）。
##
## 口径（开发侧定的，写在唯一的这一处）：**一半进体质（con，保命）、一半进伤害属性**——
## 伤害属性看角色**起始武学的系别**：`external`（外功）→ 力 `str`；其余（内伤／毒／火…）→ 智 `int`。
## 只投 `attribute_def.allocatable=1` 的那五项（悟性／根骨是资质，本来就投不了——
## 具体由 `GameState.spend_point` 自己拦，这里不重写规则）。
func _spend_all_points(db, state) -> void:
	for char_id: String in state.char_ids:
		var row: Resource = db.get_row("character_base", char_id)
		if row == null:
			continue
		var damage_attr := "str"
		for skill_id: String in row.skill_ids():
			var active: Resource = db.get_row("skill_active", skill_id)
			if active != null:
				damage_attr = "str" if str(active.element) == "external" else "int"
				break
		var guard := 0
		while state.available_points(db, char_id) > 0 and guard < 1000:
			guard += 1
			var target := "con" if state.available_points(db, char_id) % 2 == 0 else damage_attr
			if not bool(state.spend_point(db, char_id, target).get("ok", false)):
				break


func _team_enemies(db, enemy_factory_script, team_id: String) -> Array:
	var factory = enemy_factory_script.new(db)
	if db.get_row("enemy_team", team_id) != null:
		return factory.create_team(team_id, "normal")
	var enemy_id := team_id.trim_prefix(HIDDEN_TEAM_PREFIX)
	if enemy_id == team_id:
		return []
	var actor = factory.create(enemy_id, "normal")
	return [actor] if actor != null else []


## 每支队伍的经验／铜钱（按 members 展开，普通难度无加成）。
func _dump_team_rewards(db) -> void:
	print("---- 每支队伍收益（普通难度） ----")
	print("队伍\t敌人等级\t经验\t铜钱")
	for team_id: String in TEAMS:
		var team: Resource = db.get_row("enemy_team", team_id)
		if team == null:
			continue
		var exp_sum := 0
		var money_sum := 0
		var levels := PackedStringArray()
		for member: Dictionary in team.parsed_members():
			var enemy: Resource = db.get_row("enemy_base", str(member.get("enemy_id", "")))
			if enemy == null:
				continue
			var count := maxi(1, int(member.get("count", 1)))
			levels.append(str(int(enemy.level)))
			exp_sum += int(enemy.exp_reward) * count
			money_sum += int(enemy.money) * count
		print("%s\t%s\t%d\t%d" % [team_id, ",".join(levels), exp_sum, money_sum])


## 升级门槛：`level_growth.exp_to_next` 逐级累加（每行是「本级 → 下一级」）。
func _dump_level_gates(db) -> void:
	print("---- 升级门槛（累计经验） ----")
	var total := 0
	var rows: Array = db.rows("level_growth")
	rows.sort_custom(func(a: Resource, b: Resource) -> bool: return int(a.level) < int(b.level))
	for row: Resource in rows:
		total += int(row.exp_to_next)
		if int(row.level) <= 15:
			print("到 %d 级累计 %d" % [int(row.level), total])


## 一张图上的明雷一次全清能拿多少（按队伍配置展开）。
func _dump_sweep_reward(db) -> void:
	print("---- %s 明雷一次全清（按队伍配置） ----" % SWEEP_REGION)
	var exp_sum := 0
	var money_sum := 0
	for row: Resource in db.rows("roaming_spawn"):
		if str(row.region_id) != SWEEP_REGION:
			continue
		var team: Resource = db.get_row("enemy_team", str(row.team_id))
		if team == null:
			continue
		for member: Dictionary in team.parsed_members():
			var enemy: Resource = db.get_row("enemy_base", str(member.get("enemy_id", "")))
			if enemy == null:
				continue
			exp_sum += int(enemy.exp_reward) * maxi(1, int(member.get("count", 1)))
			money_sum += int(enemy.money) * maxi(1, int(member.get("count", 1)))
	print("经验合计 %d　铜钱合计 %d" % [exp_sum, money_sum])


## 敌人七维的**正式口径**（设计 10 §七，0.28.0 答 Q57）：敌人**不吃** `level_growth` 的基础列，
## 强度 = 七维 → `attr_to_stat` ＋ 装备／内功固定值（＋百分比），难度倍率照旧最后乘。
##
## 这一节把每一行**表里真实配着的**七维与它算出来的派生数值并排打出来——它就是线上
## `EnemyFactory.create()` 走的那条路（同一个 `AttributeCalculator`，只差
## `include_level_base=false` 这一个开关），所以这里**不需要第二份算式**。
## 还空着的行会被点名：那意味着它仍在走过渡回退（`_stats_from_derived`）。
##
## 为什么不再打「地板／反解」那一套：那是 Q57 的题干（「把现值翻译成七维」），
## 设计已明确**取消这个目标**——复刻旧数字从来不是设计目标，只是开发侧自己设的自检口径。
## 现在要看的是「按定位配出来的七维打起来是什么样」，那是上面那张胜率表的活。
func _dump_enemy_attrs(db, enemy_factory_script) -> void:
	var factory = enemy_factory_script.new(db)
	print("---- 敌人七维（正式口径：不吃等级基础；强度＝七维 ＋ 装备／内功固定值） ----")
	print("敌人	等级	str/con/agi/int/luk/wu/gen	hp/atk_phys/atk_qi/def_phys/def_qi/speed	hit/dodge/crit	qi_max	破架势	drop/威胁色")
	var pending := PackedStringArray()
	for row: Resource in db.rows("enemy_base"):
		var attrs: Dictionary = row.attr_map()
		if attrs.is_empty():
			pending.append(str(row.enemy_id))
		var actor = factory.create(str(row.enemy_id), "normal")
		if actor == null:
			continue
		print("%s	%d	%d/%d/%d/%d/%d/%d/%d	%.0f/%.0f/%.0f/%.0f/%.0f/%.1f	%.2f/%.2f/%.2f	%.0f	%.0f	%s/%s" % [
			row.enemy_id, int(row.level),
			int(attrs.get("str", 0)), int(attrs.get("con", 0)), int(attrs.get("agi", 0)),
			int(attrs.get("int", 0)), int(attrs.get("luk", 0)), int(attrs.get("wu", 0)),
			int(attrs.get("gen", 0)),
			actor.stat("hp_max"), actor.stat("atk_phys"), actor.stat("atk_qi"),
			actor.stat("def_phys"), actor.stat("def_qi"), actor.stat("speed"),
			actor.stat("hit_rate"), actor.stat("dodge_rate"), actor.stat("crit_rate"),
			actor.stat("qi_max"), actor.stat("poise_break"),
			("—" if str(row.drop_group).is_empty() else str(row.drop_group)), str(row.threat_tag),
		])
	if pending.is_empty():
		print("七维配齐：全部 %d 行走正式口径（过渡回退只剩代码里的兜底，没有数据用它）" % db.rows("enemy_base").size())
	else:
		print("**还没配七维的行（仍在走过渡回退）**：%s" % "、".join(pending))
	print("注：`hit` 是敏经 diminishing 曲线给的**加成**（cap 0.95／param 80），敌我命中基准都是 1.0；")
	print("    真正决定打不打得中的是对方的 `dodge_rate`（10 §五：想让敌人「血厚防薄」用敏表达）。")
	print("    `qi_max` 由智／根骨派生（每点 6.0／4.0）——不再写死 0，配了高星招式才有内力可放。")
