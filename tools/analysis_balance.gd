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
	"team_heifeng_elite", "team_boss",
]
## 落雁坡明雷一次全清的收益按这个区算（报告里的「一次全清」）。
const SWEEP_REGION := "n_luoyanpo"


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
	_dump_team_rewards(db)
	_dump_level_gates(db)
	_dump_sweep_reward(db)
	_dump_enemy_attr_floor(db)
	# 收尾标记 + 主动退出。**放在最后一段之后**：它的意思是「整张表都打完了」。
	# 为什么必须有个标记：光靠 `--quit-after` 的退出码分不出"跑到最后"和"半路被运行期错误掐断"，
	# 而这个脚本打的**是一张表**——半截表和完整表长得一模一样，读的人只会以为"就这么多"。
	# 包装脚本 `run_balance_analysis.bat` 用 findstr 找这一行，找不到就判红（决策 147／148／187）。
	print("BALANCE SNAPSHOT: OK")
	quit(0)


## 胜率／平均回合／平均剩余气血：等级 × 敌人队伍。
func _dump_win_rates(
	db, game_state_script, party_builder_script,
	enemy_factory_script, sim_script, rng_script
) -> void:
	# 队伍用测试夹具 `party_state(db, N)` **显式点名**：0.10.0 起 `new_game()` 只给
	# `recruit_def` 的初始成员（开局一人），拿它当「4 人队」会静默量成单人
	# （性能工具踩过同一个坑，见框架说明决策 261）。
	var helper = load("res://tests/test_case.gd").new()
	for party_size: int in PARTY_SIZES:
		var sample = helper.party_state(db, party_size)
		var names := PackedStringArray()
		for char_id: String in sample.char_ids:
			var row: Resource = db.get_row("character_base", char_id)
			names.append(str(row.name_cn) if row != null else char_id)
		print("---- 胜率（%d 人队：%s；每档 %d 个固定种子） ----" % [party_size, "、".join(names), TRIES])
		print("等级\t队伍\t敌人等级\t胜\t负\t打不完\t平均回合\t平均剩余气血%")
		for level: int in LEVELS:
			for team_id: String in TEAMS:
				var team: Resource = db.get_row("enemy_team", team_id)
				if team == null:
					continue
				var enemy_levels := PackedStringArray()
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
					var allies: Array = party_builder_script.build_actors(db, state)
					var enemies: Array = enemy_factory_script.new(db).create_team(team_id, "normal")
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


## 敌人七维配置的算术约束（只读试算，给设计定档用）。
##
## 为什么单列这一节：0.14.0 把 `enemy_base` 换成了七维模板，但 14 行的 `attr_*` 还空着。
## 七维配上去之后敌人的派生数值 = `level_growth` 的基础值 ＋ Σ `attr_to_stat`(七维) ＋ 装备/内功固定值，
## 而 `level_growth` 的基础值是按**角色**定的——于是很多敌人**光靠等级地板就已经超过现在的派生列**
## （例：2 级基础气血 108，而野狼现值只有 60）。所以「把现值原样翻译成七维」在数学上不存在：
## 要么接受敌人整体变强（再调别的），要么改敌人的 `level`，要么不动这张基础表。
## 这一节把「现值 / 地板 / 按主要派生项反解出的建议七维」一起打出来，让设计看着定，
## 开发侧不自己拍板（设计 10 §三：数值不盲配，用平衡观测工具配）。
func _dump_enemy_attr_floor(db) -> void:
	var AttributeCalculatorScript := load("res://src/core/attribute_calculator.gd")
	var EnemyFactoryScript := load("res://src/core/enemy_factory.gd")
	var factory = EnemyFactoryScript.new(db)
	var calculator = AttributeCalculatorScript.new(db)
	var difficulty: Resource = db.get_row("difficulty_config", "normal")
	print("---- 敌人七维试算：现口径 / 地板（七维全 0）/ 建议（按主要派生项反解） ----")
	print("口径：三列都含装备与内功固定值、难度按 normal(1.0)。现口径＝过渡回退 `_stats_from_derived`（派生列＋固定值）；")
	print("      地板＝七维全 0 走 `AttributeCalculator`；建议＝把「现口径 − 地板」按主要派生项反解成七维后再算一遍。")
	print("      建议七维的 wu/gen 没有对应派生项（只影响招式槽／内功容量／内力上限），先按 5。")
	print("敌人\t等级\thp 现/地/建\tatk_phys 现/地/建\tdef_phys 现/地/建\tspeed 现/地/建\thit 现/地/建\tdodge 现/地/建\tcrit 现/地/建\t建议 str/con/agi/int/luk/wu/gen\t地板已超现值项")
	var overshoot_totals: Dictionary = {}
	for row: Resource in db.rows("enemy_base"):
		var level := int(row.level)
		var contributions: Array = factory._enemy_contributions(str(row.enemy_id))
		var current: Dictionary = factory._stats_from_derived(row, difficulty)
		var floor: Dictionary = calculator.compute(level, {}, {}, contributions)
		var need_str := maxf(0.0, (float(current.get("atk_phys", 0.0)) - float(floor.get("atk_phys", 0.0))) / 2.0)
		var need_con := maxf(0.0, (float(current.get("hp_max", 0.0)) - float(floor.get("hp_max", 0.0))) / 12.0)
		var need_agi := maxf(0.0, (float(current.get("speed", 0.0)) - float(floor.get("speed", 0.0))) / 1.5)
		var need_int := maxf(0.0, (float(current.get("atk_qi", 0.0)) - float(floor.get("atk_qi", 0.0))) / 1.8)
		var need_luk := _invert_diminishing(float(current.get("crit_rate", 0.0)), 0.75, 100.0)
		var suggest := {
			"str": int(round(need_str)), "con": int(round(need_con)), "agi": int(round(need_agi)),
			"int": int(round(need_int)), "luk": int(round(need_luk)), "wu": 5, "gen": 5,
		}
		var suggested: Dictionary = calculator.compute(level, suggest, {}, contributions)
		var overshoot := PackedStringArray()
		for stat_id: String in current:
			if float(floor.get(stat_id, 0.0)) > float(current[stat_id]) + 0.001:
				overshoot.append(stat_id)
				overshoot_totals[stat_id] = int(overshoot_totals.get(stat_id, 0)) + 1
		print("%s\t%d\t%.0f/%.0f/%.0f\t%.0f/%.0f/%.0f\t%.0f/%.0f/%.0f\t%.0f/%.0f/%.0f\t%.2f/%.2f/%.2f\t%.2f/%.2f/%.2f\t%.2f/%.2f/%.2f\t%d/%d/%d/%d/%d/%d/%d\t%s" % [
			row.enemy_id, level,
			float(current.get("hp_max", 0.0)), float(floor.get("hp_max", 0.0)), float(suggested.get("hp_max", 0.0)),
			float(current.get("atk_phys", 0.0)), float(floor.get("atk_phys", 0.0)), float(suggested.get("atk_phys", 0.0)),
			float(current.get("def_phys", 0.0)), float(floor.get("def_phys", 0.0)), float(suggested.get("def_phys", 0.0)),
			float(current.get("speed", 0.0)), float(floor.get("speed", 0.0)), float(suggested.get("speed", 0.0)),
			float(current.get("hit_rate", 0.0)), float(floor.get("hit_rate", 0.0)), float(suggested.get("hit_rate", 0.0)),
			float(current.get("dodge_rate", 0.0)), float(floor.get("dodge_rate", 0.0)), float(suggested.get("dodge_rate", 0.0)),
			float(current.get("crit_rate", 0.0)), float(floor.get("crit_rate", 0.0)), float(suggested.get("crit_rate", 0.0)),
			suggest["str"], suggest["con"], suggest["agi"], suggest["int"],
			suggest["luk"], suggest["wu"], suggest["gen"],
			("无" if overshoot.is_empty() else "、".join(overshoot)),
		])
	print("地板已超现值的项统计（按敌人数）：%s" % _format_counts(overshoot_totals))
	print("注：七维配齐后敌人还会多出现在没有的项——qi_max（等级基础＋智/根骨）、破架势（力）、")
	print("    穿透率（力，diminishing）、格挡率（根骨，diminishing）、暴击伤害（运）、气血/内力回复。")
	_dump_enemy_attr_fit_without_level_base(db, calculator)


## 备选口径：**敌人不吃 `level_growth` 的基础列**，强度 = 七维 → `attr_to_stat` ＋ 装备/内功固定值。
## 这一节把「按现口径反解出的七维」与「这样算出来的派生值」并排打出来，看能不能原样搬过去。
## 如果能（残差 ≤ 取整误差），那 10 §二的「敌人与角色同管线」只需去掉等级基础这一层；
## 如果不能（残差很大），说明必须由设计重新定 14 行的数值目标。
func _dump_enemy_attr_fit_without_level_base(db, calculator) -> void:
	var CurveEvaluatorScript := load("res://src/core/curve_evaluator.gd")
	var EnemyFactoryScript := load("res://src/core/enemy_factory.gd")
	var factory = EnemyFactoryScript.new(db)
	var difficulty: Resource = db.get_row("difficulty_config", "normal")
	print("---- 备选口径：敌人不吃等级基础（强度＝七维＋装备固定值），按现口径反解的七维 ----")
	print("敌人\t建议 str/con/agi/int/luk/wu/gen\t按此算出的 hp/atk_phys/atk_qi/def_phys/def_qi/speed/hit/dodge/crit\t最大残差")
	var worst := 0.0
	var worst_id := ""
	for row: Resource in db.rows("enemy_base"):
		var contributions: Array = factory._enemy_contributions(str(row.enemy_id))
		var current: Dictionary = factory._stats_from_derived(row, difficulty)
		var flats := _flat_totals(contributions)
		# 反解：把「现口径 − 装备/内功固定值」按系数除回去（七维不从等级基础起步，故没有地板问题）。
		var attrs := {
			"str": int(round(maxf(0.0, (float(current.get("atk_phys", 0.0)) - float(flats.get("atk_phys", 0.0))) / 2.0))),
			"con": int(round(maxf(0.0, (float(current.get("hp_max", 0.0)) - float(flats.get("hp_max", 0.0))) / 12.0))),
			"agi": int(round(maxf(0.0, (float(current.get("speed", 0.0)) - float(flats.get("speed", 0.0))) / 1.5))),
			"int": int(round(maxf(0.0, (float(current.get("atk_qi", 0.0)) - float(flats.get("atk_qi", 0.0))) / 1.8))),
			"luk": int(round(_invert_diminishing(
				maxf(0.0, float(current.get("crit_rate", 0.0)) - float(flats.get("crit_rate", 0.0))), 0.75, 100.0))),
			"wu": 5, "gen": 5,
		}
		var got: Dictionary = _attr_only_stats(db, CurveEvaluatorScript, calculator, attrs, contributions)
		var residuals: Dictionary = {}
		var local_worst := 0.0
		for stat_id: String in ["hp_max", "atk_phys", "atk_qi", "def_phys", "def_qi", "speed", "hit_rate", "dodge_rate", "crit_rate"]:
			var diff: float = absf(float(got.get(stat_id, 0.0)) - float(current.get(stat_id, 0.0)))
			residuals[stat_id] = diff
			local_worst = maxf(local_worst, diff)
		if local_worst > worst:
			worst = local_worst
			worst_id = str(row.enemy_id)
		print("%s\t%d/%d/%d/%d/%d/%d/%d\t%.0f/%.0f/%.0f/%.0f/%.0f/%.2f/%.2f/%.2f/%.2f\t%s" % [
			row.enemy_id, attrs["str"], attrs["con"], attrs["agi"], attrs["int"], attrs["luk"], attrs["wu"], attrs["gen"],
			float(got.get("hp_max", 0.0)), float(got.get("atk_phys", 0.0)), float(got.get("atk_qi", 0.0)),
			float(got.get("def_phys", 0.0)), float(got.get("def_qi", 0.0)), float(got.get("speed", 0.0)),
			float(got.get("hit_rate", 0.0)), float(got.get("dodge_rate", 0.0)), float(got.get("crit_rate", 0.0)),
			_format_residuals(residuals),
		])
	print("最大残差 %.2f（%s）；其余只剩取整误差（≤0.5）说明这条路能把现值原样搬过去。" % [worst, worst_id])


## 只算「七维 → attr_to_stat ＋ 装备/内功固定值」，**不含** level_growth 的基础列，也不含百分比层。
func _attr_only_stats(db, curve_evaluator_script, calculator, attrs: Dictionary, contributions: Array) -> Dictionary:
	var totals: Dictionary = {}
	for row: Resource in db.rows("attr_to_stat"):
		var value: float = curve_evaluator_script.evaluate(
			row.curve, row.rate, float(attrs.get(row.attr_id, 0)), row.cap, row.param
		)
		totals[row.stat_id] = float(totals.get(row.stat_id, 0.0)) + value
	for contribution: Dictionary in contributions:
		if str(contribution.get("kind", "")) != "stat_flat":
			continue
		var target := str(contribution.get("target", ""))
		if target.is_empty():
			continue
		totals[target] = float(totals.get(target, 0.0)) + float(contribution.get("value", 0.0))
	var out: Dictionary = {}
	for stat_id: String in totals:
		out[stat_id] = calculator.apply_stat_def(stat_id, float(totals[stat_id]))
	return out


func _flat_totals(contributions: Array) -> Dictionary:
	var totals: Dictionary = {}
	for contribution: Dictionary in contributions:
		if str(contribution.get("kind", "")) != "stat_flat":
			continue
		var target := str(contribution.get("target", ""))
		if target.is_empty():
			continue
		totals[target] = float(totals.get(target, 0.0)) + float(contribution.get("value", 0.0))
	return totals


func _format_residuals(residuals: Dictionary) -> String:
	var parts := PackedStringArray()
	for stat_id: String in residuals:
		var diff := float(residuals[stat_id])
		if diff > 0.6:
			parts.append("%s±%.1f" % [stat_id, diff])
	return "无" if parts.is_empty() else "、".join(parts)


## 反解 diminishing 曲线 `value = cap × x / (x + param)`：x = param × value / (cap − value)。
## 目标已经贴到上限时返回一个足够大的整数（曲线永远到不了 cap，只能逼近）。
func _invert_diminishing(value: float, cap: float, param: float) -> float:
	if value <= 0.0:
		return 0.0
	if value >= cap:
		return 100.0
	return param * value / (cap - value)


func _format_counts(counts: Dictionary) -> String:
	if counts.is_empty():
		return "无"
	var keys: Array = counts.keys()
	keys.sort()
	var parts := PackedStringArray()
	for key: String in keys:
		parts.append("%s×%d" % [key, int(counts[key])])
	return "、".join(parts)
