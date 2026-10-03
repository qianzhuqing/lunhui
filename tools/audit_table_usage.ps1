# 配表使用情况审计：找「配了但代码里一个字都没提」的列与常数。
#
# 用途：策划问「这列到底有没有用」、或者加完功能想确认没有死配置时跑一下。
# 注意：它只查**名字有没有出现在 src/ 里**，不保证读得对——所以它是筛子，不是证明。
# 真正的行为保证在 tests/（用例）与 tools/validate_tables.ps1（结构校验）。
#
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit_table_usage.ps1

$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot ".."
# 语料里要去掉 `@export var xxx` 这些**定义行**：每个列名都会出现在它自己的行类里，
# 留着它们会让「有没有人读这一列」永远为真——这个工具第一版就是这样，报出来的「0 死列」没有依据。
$srcLines = New-Object System.Collections.Generic.List[string]
foreach ($file in Get-ChildItem (Join-Path $root "src") -Recurse -Include *.gd) {
    foreach ($line in (Get-Content $file.FullName)) {
        if ($line -match '^\s*@export\s+var\s') { continue }
        $srcLines.Add($line)
    }
}
$src = $srcLines -join "`n"

# 已知「故意不被代码读」的列：每条都要写清为什么留着。不在这里的新增死列才会让审计失败。
# （按名字审计的极限：icon／note／desc 这类同名列会被别的表用到，所以它们在名单里也不影响判断。）
$knownUnused = @{
    "curve_def:formula"         = "曲线公式的说明文字；公式在代码、表只放枚举（AGENTS 的硬性约束）"
    "enemy_team:team_buff"      = "全空、06 也没登记、代码没人读——语义待设计（待策划确认 Q44：全队开场 buff 还是删列）"
    "equip_base:special_effect" = "效果 id 有了但效果定义表还没建，等设计补表"
    "item_base:icon"            = "道具图标资产还没有，等美术（界面先用文字+稀有度色）"
    "status_effect:icon"        = "状态图标资产还没有，等美术"
    "map_local:room_count"      = "表侧写 18、实际房间表 19，已记在 07 待确认第 4 条（以 19 为准）"
    "map_local:has_combat"      = "代码从房间/队伍结构推出来，列只是给人看的元信息"
    "map_local:save_allowed"    = "五张图全是 1，但手动存档只在城镇——语义待设计明确（见交接表）"
    "map_region:parent_region"  = "区域层级还没用上（等第二章多区）"
    "map_region:respawn_group"  = "明雷按单点 respawn_sec 刷新，分组刷新没做（设计没给规则）"
    "rarity_def:drop_weight"    = "掉落表直接点名物品，稀有度权重抽取还没用上（等随机稀有度掉落）"
    # 0.10.0 那批新表的列：**宝箱守卫（09 一）与剧情招募（09 3.2）已经接上**，
    # npc_guard 的三列（npc_name_cn／peace_note／fight_note）与 recruit_def:join_note
    # 都已由 `guard_service.gd`／`recruit_service.gd` 真读——那四条白名单按约定删掉了。
    # 只剩木桩那两条常数（等设计给 enemy_base 行，见 Q52）。
}

# 常数这一侧同理：木桩练习战（09 3.3）没做，两条上限先记在理由里。
# 接上「城镇木桩」之后把这两行删掉。
$knownUnusedConstants = @{
    "growth_const:dummy_xp_cap_level" = "木桩练习战（09 3.3）未做——到这一级后木桩不再给经验"
    "growth_const:dummy_mastery_cap"  = "木桩练习战（09 3.3）未做——参与木桩战的招式熟练度只涨到这一级"
}

$unusedColumns = @()
foreach ($file in Get-ChildItem (Join-Path $root "data\tables") -Filter *.csv) {
    $header = Get-Content $file.FullName -TotalCount 1
    foreach ($column in ($header -split ",")) {
        $name = $column.Trim().TrimStart([char]0xFEFF)
        if ($name -eq "") { continue }
        # 词边界匹配，不用子串：`icon_id` / `ICON_DIR` 这类标识符会把 `icon` 这一列假判成「有人读」
        $key = "{0}:{1}" -f $file.BaseName, $name
        if ($src -notmatch ("\b" + [regex]::Escape($name) + "\b")) {
            if (-not $knownUnused.ContainsKey($key)) {
                $unusedColumns += $key
            }
        }
    }
}

$unusedConstants = @()
foreach ($table in @("combat_const", "growth_const")) {
    $path = Join-Path $root ("data\tables\{0}.csv" -f $table)
    if (-not (Test-Path $path)) { continue }
    foreach ($row in (Import-Csv $path)) {
        $id = $row.const_id
        if ($id -eq "") { continue }
        if ($src -notmatch ("\b" + [regex]::Escape($id) + "\b")) {
            $key = "{0}:{1}" -f $table, $id
            if (-not $knownUnusedConstants.ContainsKey($key)) {
                $unusedConstants += $key
            }
        }
    }
}

Write-Output ("未被 src/ 提到的列: {0}" -f $unusedColumns.Count)
foreach ($entry in $unusedColumns) { Write-Output ("  " + $entry) }
Write-Output ("未被 src/ 提到的常数: {0}" -f $unusedConstants.Count)
foreach ($entry in $unusedConstants) { Write-Output ("  " + $entry) }

if ($unusedColumns.Count -gt 0 -or $unusedConstants.Count -gt 0) {
    Write-Output "AUDIT: 有没被读到的配置（要么接上，要么在交接表里写明为什么留着）"
    exit 1
}
Write-Output "AUDIT: PASSED（没有死配置）"
