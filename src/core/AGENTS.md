# 核心逻辑约定（`src/core/`）

适用：`src/core/` 下的纯逻辑（`RefCounted`/`Resource`，无节点，可在 headless 下端到端跑）。
仓库级约定与命令见根 `AGENTS.md`；改表看 `data/AGENTS.md`；界面看 `src/ui/AGENTS.md`；
世界层看 `src/world/AGENTS.md`；写用例看 `tests/AGENTS.md`。

## 状态与存档

- 存档默认写 `user://saves`，测试与受限环境用 `--save-dir=<路径>` 覆盖；存档写入失败不允许挡住开局。
- 存档结构当前是 **v13**（多存档槽 + 背包 `inventory`（装备实例带随机词条 `affixes`）+ 加点 `char_allocations`
  + 队伍经验 `party_exp` + 武学熟练度 `skill_mastery` + 已学武学 `char_learned` + 装配 `char_loadouts`
  + 商店交易状态 `shop_state` + 副本完成度 `dungeon_records` + 抽卡保底 `pity`
  + 大地图揭开 `revealed_nodes` + 事件判定 `event_checks`/`flags` + 首杀记录 `first_kills` + 战斗外气血 `char_hp`
  + **战斗外增益 `field_buffs`**（v12：存到期时间戳，按现实分钟计时，离线那段时间照样算）
  + **大地图坐标 `world_pos`**（v13：读档一律回大地图、落点用它；小地图与副本内的位置**故意不存**））。
  改存档字段必须升 `GameState.VERSION` 并写迁移；v1~v12 老档会自动补齐缺失部分（老档没有首杀记录一律当作「还没打过首杀」、
  没有气血记录一律当作满血、没有增益记录一律当作「没打坐」、没有坐标记录一律用大地图默认出生点），
  `MenuController.load_game` 负责写回。
  **读档落点也在这里定**：`MenuController.load_game` 的结果带 `start_scene = START_OVERWORLD`（视图据此切
  `scenes/world_run.tscn`），新建游戏仍进占位枢纽页（`START_HUB`）。
  **新加的字段必须同时写进 `to_dict()`**（漏了不会报错，只是读档静默丢）——
  `tests/test_handshake.gd::_check_save_fields_are_serialized` 会扫源文件兜底（`Inventory` 同理），
  确实不持久化的（如读档整理结果）要加进那儿的白名单并写明理由（见框架说明决策 173）。
  **字段类型写错的老档也要容错**（JSON 合法但 `inventory` 是字符串之类）：要么判损坏、要么读成缺省，**不许运行期报错**——`test_save_migration._check_type_mismatched_fields` 用六种写法钉着（决策 248）。
  **升版本时还要做一件事**：往 `tests/test_save_migration.gd` 的 `ADDED_IN` 补一行（写清新版本加了哪些字段）——
  那条用例会拿当前存档逐档「删掉那一档之后才有的字段再读」，v1~v11 每一档都验一遍；
  不补的话新字段的迁移就没人试过（这条纪律是 2026-10-03 补的，见 `docs/dev/框架说明.md` 决策 78）。
- **存档时机按设计 02**：城镇手动存（F5／枢纽按钮）、大地图切小图自动存、副本内清房间/开箱/触发/事件/扫荡自动存；
  统一走 `src/core/save_service.gd`，读档只在主菜单。用例注入 `save_store_override` 到临时目录，别写 `user://`。
- **首杀奖励只领一次**：首杀记录的唯一落点是 `GameState.first_kills`（v10 起进存档），
  战斗结算前调 `GameState.mark_first_kill(drop_group)`，返回 true 才算首杀；
  `first_kill_only=1` 的掉落槽只在首杀那次掷，扫荡固定 `first_kill=false`。
  别把首杀记录放回 `GameSession`——那是会话内存，退出重进就能刷。
- **战斗外气血只在 `GameState.char_hp` 里**（v11 起进存档，**没有记录 = 满血**，治疗就是 `heal_all()` 清记录）：
  战斗结算末尾 `battle_screen._write_back_hp()` 写回（胜/败/撤都写，**下限 1 点**，免得全队 0 血卡死）；
  **但败北还要多走一步**（0.8.1，08 已定）：`_handle_defeat()` 在写回**之后**跑
  `heal_all()`（清记录 = 满血）＋清战斗外增益＋把回程指向「最近到过的城镇」
  （登记点在小地图控制器 `setup()`；没到过城镇就退回大地图）。顺序写反＝满血被「剩 1 点」盖回去，
  用例 `_check_defeat_settle` 与闭环里那一场败北都盯着这条。见框架说明决策 213。
  `PartyBuilder.build_actor` 按它带着伤进下一场；医馆治疗走 `ShopService.heal_preview()/heal_party()`
  （费用 = 全队缺失气血 × `building_def.service_price`，设计 05）。改这条口径要同时动这四处，别只改一处。

## 成长

- **经验升级只在 `LevelService` 里**：`level_growth.exp_to_next` 是逐级门槛，付得起就升（可跨级）、
  20 级封顶；升级的加点与基础属性自动跟上（`available_points` + `AttributeCalculator`）。
  经验进队伍池 `party_exp`，**谁升级扣一份**（多人时先补等级最低的）——这条分配口径是开发侧定的，等设计确认。
- **加点只能加「五维」，资质不能加**（设计 0.13.0）：`attribute_def.allocatable=1` 的才可加点
  （力体敏智运），`=0` 的是**资质**（悟性／根骨）——创建时定死，只能靠图鉴奖励与特定内功提升。
  唯一判定出处是 `GameState.allocation_block_reason()`，加点入口与角色面板的加号按钮都问它；
  每级给 `level_growth.upgrade_points` 点（0.13.0 起是 5）。
- 槽位上限、熟练度倍率、打坐费用、图鉴奖励都是「公式在代码（`src/core/growth_calculator.gd`）、常数在
  `growth_const.csv`」；改数值只改表，别把系数写进代码。

## 队伍与剧情

- **开局队伍来自 `recruit_def`**（设计 09 §3.2，0.10.0）：`is_initial=1` 的才是初始成员
  （现在只有书生一人），其余按 `join_condition`／`join_scene` 在剧情里加入。
  取队伍一律走 `GameState.default_party_ids_from_recruit(db)`；`default_party_ids(db)`（按
  `character_base` 顺序取前 `MAX_PARTY` 行）只留给老用例与夹具。**用例要多人队伍就显式点名**
  （`tests/test_case.gd` 的 `party_state(db, N)`），别再指望 `new_game()` 自动给 4 人。
- **入队只有一条路**：`RecruitService` 判定 + `GameState.add_character()` 落地（等级／起始装备／
  起始武学／装配一次发齐）。触发粒度：**城镇只认客栈那一次交互**（设计 09 §3.2 的原文）、
  副本／野外按场景（`join_scene` 的粒度就是场景）、大地图区域按走到 `Markers/Node_<node_id>`
  70px 内（与地标高亮同半径）。入队点亮 `flag_<char_id>_joined`（`guide_step` 拿它当下一步条件）；
  已在队里时返回 false 且**不重发装备**（每次按 E 都会查一遍，这是常态路径）。
- **引导只有一处**：`src/core/guide_service.gd`（读 `guide_step`，当前步 = 条件已满足的最后一行，
  第一步用 `start` 哨兵）。HUD 那一行在两个控制器里各加一个 Label（**y=80**：状态栏 8／揭雾或完成度
  34／战斗外增益 56 之后的第四行，自检有不叠字断言），文案由 `hud_text()` 生成，**别在界面里另拼一套**。
  谁点亮旗标是各系统的事：`flag_board_read` 由清风驿的悬赏板发，`flag_ch_ci_joined` 由入队发。
- **宝箱守卫只有一处**：`src/core/guard_service.gd`（`npc_guard` 那间房 → 文取条件／武取队伍）。
  规则：**没有守卫的箱子照旧直接开**；有守卫且没解决时，**第一次按 E 只是让他开口**
  （说明他要什么＋另一条路，不扣东西），第二次才付账。武取**不用按键**——`fight_team` 与
  `dungeon_room.enemy_team` 是同一条，打赢房里那场仗就解锁。解决状态两条各写各的：
  武取＝`dungeon_records[scene].rooms`、文取＝旗标 `flag_guard_<guard_id>_peace`。
  给玩家看的文案里**不许出现内部编号**。**0.14.0 起文取是三列**
  （`peace_item`／`peace_check_source`／`peace_check_value`，**可叠加、任一满足即过**，06 的口径）：
  判定路走 `EventCheckService.best_check_value`（与事件判定同一算法），过门槛就放行且**不消耗道具**——
  控制器**优先走不花东西的那条**，别改成「先收道具」。
- **木桩练习战只有一处**：`src/core/practice_service.gd`（入口＝`building_def.service_id == dummy_training`；
  两条上限与到顶提示全部读 `growth_const.dummy_*` / `ui_text.dummy_cap_reached`）。战斗里那句
  「这一局是练习战」写在 `Encounter.practice`，**结算（`battle_screen._settle`）按它**跳过
  掉落／铜钱／首杀／击败领悟、把经验与熟练度封顶，并且不写房间完成度。
  `MasteryService.apply_combat_usage(char_id, usage, cap)` 的 `cap` **只压涨不压低**——
  打木桩不该让练好的招式退步。**经验也要截断**（设计 0.14.0 Q54）：`capped_exp()` =
  `min(原值, exp_room())`，而 `exp_room()` = Σ 每个成员到上限所需经验 − 池里已有的——
  「单场最多补到门槛，超出丢弃」，别再写成「还有人没过就给原值」。
  木桩的两行敌人数据 0.14.0 已到位（`en_dummy_training`／`team_dummy_training`，`faction=training`）；
  还差地编在清风驿校场摆 `Markers/Buildings/bld_dummy`（A6）。
- **事件判定只在 `EventCheckService` 里**：判定值来自 `CharacterSheet.event_check_value`
  （attr 取裸值／skill 取等级+裸属性/5，**只用裸值，不含装备**）；位点是 `Event_<check_id>`，
  小地图与大地图都按 E 触发，结果与旗标写存档。两种强度按 01 原文：
  `hard` 达标必过、**不足则无法进行**（不掷骰、不扣东西）；`soft` 达标必过、不足按
  `soft_chance()`（`0.5 + 差值 × 0.1`，夹 5%~95%）掷骰，**失败不惩罚**。
  `judge()` 是纯查询（不掷骰），**掷骰只在 `resolve()` 里发生一次**；随机走构造时注入的
  `RngService`（用例传固定种子）。奖励 `item`／`equip`／`room`／`event`／`none` 已实现，
  `skillbook`／`boss` 与非法取值**显式报错**，别再往「没有奖励」的兜底里塞。

## 敌人掉落（0.14.0）

- **敌人掉落不再看 `drop_table` 的装备行**（设计 10 §五）：`drop_table` 只留材料／铜钱／秘籍，
  装备掉落走 `DropResolver.roll_enemy_equipment()` —— 从 `enemy_equip` 推（**身上穿的，就是能掉的**），
  每件按 `rarity_def.drop_weight ÷ 100` 掷一次，**首杀必掉一件（武器优先，没武器才取稀有度最高）**。
  `battle_screen._settle` 同时跑两路（掉落组 + 敌人装备），**别把装备塞回 `drop_table`**。
  （口径两条是开发侧定的：`÷100` 的读法与「首杀取武器槽」，已登记 `待策划确认.md` Q56。）
- 练功木桩是**唯一允许没有 `drop_group` 的敌人**（`faction=training`）；
  `table_validator._check_training_faction` 盯着这条口子：别的阵营漏配掉落组照样报错。
## 物品与装备

- **奖励物品入账只有一条路：`BattleReward.grant_item()`**（货币 → `inventory.money`、装备 → 建实例、
  其余 → 堆叠）。战斗、宝箱、扫荡、事件判定都调它，别自己 `inventory.add_item`——
  `ev_gamble` 就是这么把「赢钱」发成了背包里一行「铜钱」的（见 `docs/dev/框架说明.md` 决策 75）。
  已知例外一处：事件判定与隐藏内容的 `equip` 奖励直接 `add_equipment`（**不带随机词条**，
  与掉落的「按稀有度掷词条」不同）——这是等设计拍板的口径，别当成遗漏去「顺手统一」，
  见 `框架说明.md` 决策 137 与 `当前状态.md` 3.2 #6。
- **背包容量只算一处**：还能放几个走 `Inventory.room_for(db, item_id)`（`add_item` 也用它）。
  花钱拿到东西的路径**都要处理「只放进去一部分」**：只按真正入包的数量收钱，剩下的留在原处
  ——买入早就这么做了，回购漏过一次（按全价扣钱、货给一半，见框架说明决策 102）。
- **装备词条只在 `AffixRoller` 里掷**：概率/区间/槽位限定全在 `affix_pool.csv` + `rarity_def.csv`，
  掷出来的数值冻结进存档；加成走 `Inventory.contributions_for` 的贡献通道，别在装备本体上加特例。
- **穿戴记录要按配表收口**：`equipment_slots`（界面视图）与 `equipped_instances`（贡献／武器判定）
  都从 `Inventory._slot_capacities(db)` 取「配表认得的格子」——设计删槽位或把 `max_equip` 改小后，
  存档里多出来的记录由 `reclaim_stranded_equipped(db, char_id)` 退回背包（`GameState.from_dict` 读档时跑），
  别让它们变成「界面看不见、却照常加属性、还卸不下来」。实例序号 `next_uid` 必须随存档走：
  丢了会复用旧 id，`equipment[id] = …` 会**静默覆盖**背包里那件（见框架说明决策 101）。
  这类「读档时整理」的字段要跟 `pruned_characters` 一样记数（`reclaimed_equipped`），
  并接进 `MenuController.load_game` 的「整理完立刻写回」条件——否则每次读档都要重收一遍。
- 装备加成必须通过 `Inventory.contributions_for` 走 `AttributeCalculator` 的贡献通道，战斗单位一律用
  `PartyBuilder.build_actors(db, state)` 构造，不要自己拼属性。
- **商店规则只在 `ShopService` 里**：货架/价格/限量看 `shop_stock.csv`，交易状态写存档；
  店面是 `scenes/shop_screen.tscn`（骨架在场景里、代码只绑定），入口是城镇里的建筑 Marker 按 E。

## 武学

- **武学是「学 + 装」两件事**：门槛／招式槽／内功容量全在 `src/core/skill_loadout.gd`，
  战斗里能用的招式由 `SkillLoadout.battle_skill_ids()` 决定（不要再从模板 `skill_base` 直接取招）；
  装配进存档，改槽位公式只改 `growth_const.csv`。
- **武学的来源只有三处**（都在 `src/core/skill_grant.gd`，按 `skill_base.source_type` 配表驱动）：
  击败指名敌人（`drop`）、触发指名隐藏点位（`hidden`）、研读指名秘籍（`item`，学会才消耗）。
  加新来源改这一个文件＋表，别把「谁会什么」写死在角色模板里。
- **武学是三张表**：`skill_base`（身份与门槛）、`skill_active`（招式数值）、`skill_passive`（内功明细）、
  `skill_passive_stat`（内功加成）、`skill_star_def`（星级）。伤害管线与选招都读 `skill_active`，
  不要再去 `skill_base` 找倍率或内力消耗。
- **招式要能在战斗里打出来**：`skill_active.is_attack()`（有 `damage_type` 且倍率 > 0）为假时，
  `skill_options()` 不会列它——战斗界面连按钮都没有。这类「效果未配」的招式（现例：醉里乾坤·醉步）
  要么补数值、要么给增益类招式开列；开发侧不编效果，但**必须让它不静默**：
  面板靠 `CharacterSheet` 的 `battle_usable`／`battle_note` 如实写「战斗里用不出来（效果未配）」，
  5.21 现在把「没有伤害、但有 `on_cast` 发放」的招式算作**增益招式**（能用），只有两者都没有才点名。

## 战斗管线

- **异常状态只在 `BattleActor` 上**：叠层／快照／回合末结算／灼伤削防／内伤压制内力都在那里，
  数值看 `status_effect.csv` + `damage_type.csv`；伤害管线里 DoT 走 `DamageResolver.resolve_dot()`，
  别再往九步直伤管线里塞 DoT 特例。击杀方式记在 `GameSession.last_battle.kill_styles`（隐藏内容判「毒杀」用）。
- **多段与全体在模拟器里**：`skill_active.hit_count` 每段独立掷点（`_resolve_one_hit`），
  `target_type=all_enemy` 由 `_targets_for` 展开——加新招式别再让结算忽略这两列。
- **预兆与拆招也在模拟器里**（设计 04 核心循环）：`_refresh_intents()` 每回合开始给敌人定招，
  `intent_of/intent_lines` 供界面显示预兆；敌人出手一律走 `_skill_for()` 用预兆里那一招。
  `parry(actor, target)` 花掉我方一次行动，那一招整招伤害 ×0.5、拆招者架势 +15% 上限
  （`PARRY_POISE_GAIN` 是开发侧定的数值，待设计补表列，别自己再发明一套）。
- **逃跑也在模拟器里**：`flee(actor)` 花掉一次行动掷一次判定，成功率随身法对比（`flee_chance()`，
  公式与上下限是开发侧定的，待设计补 `combat_const`）；成功以 `WINNER_FLEE` 收场——**不结算经验／铜钱／掉落、明雷不清**，
  这条别在界面或 `_settle()` 里另写一套。被追上（`CONTACT_CAUGHT`）的第一回合由 `flee_block_reason()` 挡下。

## 世界与副本

- **副本完成度与扫荡只在 `DungeonService` 里**：完成度四项与「已通关层」都从
  `dungeon_room` / `hidden_trigger` 算，记录写存档 `dungeon_records`（会话里的那份回大地图就清）；
  隐藏房间 = `branch_group=hidden`，宝箱记录键必须带房间前缀。
- **大地图揭雾与驿站只在 `WorldMapService` 里**：`map_region` 的 `reveal_on_map`／`proximity_N`／`discover_X`
  决定揭开，揭开写存档；驿站（`Node_n_post_station`）按 E 传送与切难度，难度条件是中文短语 → 用 `DIFFICULTY_RULES` 映射。
