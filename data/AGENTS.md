# 配置表约定（`data/`）

适用：`data/tables/*.csv`、`data/generated/`，以及任何"改表"的工作。
仓库级约定与命令见根 `AGENTS.md`；数值怎么进代码看 `src/core/AGENTS.md`；
脚本与门限看 `tools/AGENTS.md`。

- **CSV 是唯一数值源**：`data/tables/*.csv` 由设计师维护，运行期只读 `data/generated/*.tres`（生成物不进 Git）。
  **改完 CSV 必须重建**（`tools\build_tables.gd`／`run_tests.bat`／`run_all_checks.bat` 都会重建）——
  不重建就等于在读上一版数据，而输出看不出区别（真踩过：观测工具没重建，量出错误结论并写进了文档，
  见框架说明决策 167／168）。现在 `TableDb` 加载时会自己提醒："CSV 比 data/generated 新"。
- 不修改既有 CSV 的已有列；新增列要同步行类与校验规则。
- **改枚举要三处一起改**：`docs/design/06_配置表说明.md`（数据字典）／`src/core/table_validator.gd` 的 `ENUMS`／
  `tools/validate_tables.ps1` 的枚举与引用检查。`tests/test_handshake.gd::_check_doc_enums_match_code`
  会把「06 里写的枚举」与「代码 ENUMS／表内实际取值」对一遍——曾经 `event_check.reward_type`
  文档写 7 种、代码只剩 4 种，导致「配一条 equip 奖励」被自己的构建期校验器拒掉（见框架说明决策 87）。
  文档里那几处**已知过期**的写在 `DOC_ENUM_KNOWN_STALE` 并注明理由，改完删一行。
- 装备／词条／内功给**派生数值**的加成**单条超过 `stat_def.max_value`** 时，运行期会静默夹掉
  （`AttributeCalculator` 夹最终值、`DamageResolver._stat_cap` 取同一个上限）：写 0.8 的减伤实际只有 0.6，
  描述却写 0.8。§5.31 会把这类值报成警告并打印覆盖（现在「比了 150 条」）。
  口径：**只比上限**——加成写 0 是「这条不给加成」的正常写法，`max_value` 空或 0 表示无上限（见决策 151）。
- **表里说「无上限」，代码却有兜底上限 —— 必须让表写上上限**：`stat_def.max_value` 留空 = 0 = 无上限
  （`has_max()`／`curve_evaluator`／校验器三处都是这个口径），而 `DamageResolver._stat_cap` 在没上限时
  会静默换用兜底常数（穿透/格挡 0.75、格挡减伤 0.8、减伤 0.6）。所以要动这四个比率的上限，
  得同时想清楚管线；`REQUIRED_CAPPED_STATS` 每次构建都会点名（决策 155）。
  反过来，`combat_const` 那 5 个「缺行兜底」**故意不做一致性断言**——它们只在缺行时用到，
  加了等于让策划调数值就得改代码。
