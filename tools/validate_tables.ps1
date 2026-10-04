<#
.SYNOPSIS
    校验 data/tables 下配置表的结构、引用与文档同步。

.DESCRIPTION
    检查项：
      1. 编码     —— 每个 CSV 必须带 UTF-8 BOM
      2. 列数     —— 每行字段数必须与表头一致
      3. 主键     —— 主键列不得重复、不得为空
      4. 引用     —— 跨表 id 必须存在于目标表
      5. 孤立     —— 未被引用的队伍／掉落组／房间
      6. 数值     —— 比率在 0~1、区间 min<=max
      7. 文档同步 —— 表清单与 docs/design/06_配置表说明.md 双向一致

    用法：
        powershell -ExecutionPolicy Bypass -File tools\validate_tables.ps1

    退出码：0 全部通过；1 存在问题。
#>
[CmdletBinding()]
param(
    [string]$Root = ''
)

$ErrorActionPreference = 'Stop'

# PowerShell 5.1 在 param 默认值里取不到 $PSScriptRoot，改到脚本体内解析。
if ([string]::IsNullOrEmpty($Root)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $Root = Split-Path -Parent $scriptDir
}
$tableDir = Join-Path $Root 'data/tables'
$docPath  = Join-Path $Root 'docs/design/06_配置表说明.md'

if (-not (Test-Path -LiteralPath $tableDir)) { throw "找不到配置表目录: $tableDir" }

$errors   = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

function Add-Error   { param([string]$m) $errors.Add($m) }
function Add-Warning { param([string]$m) $warnings.Add($m) }

# 文档比对专用的**覆盖门限**：5.26／5.29／5.30 这几项都是「按文档表格的排版解析出行，再跟数据逐格比」。
# 文档一改版式（加一列、换表头、全角竖线、多一行说明），解析就变成 0 行，比对循环一行都不跑——
# 报出来的是「不一致 0 处」，**和真比过一模一样**（2026-10-03 发现，见框架说明决策 150）。
# 所以这类比对跑完必须核一次覆盖：0 行 = 这项检查根本没做 = 报错。
# 「量不出来」不等于「量过了」，和版式预算／进程泄漏那两条是同一条纪律。
function Assert-DocCoverage {
    param([string]$Label, [int]$Compared, [int]$Available = 0)
    if ($Compared -gt 0) { return }
    $hint = if ($Available -gt 0) { "（数据里有 $Available 行可比）" } else { "" }
    Add-Error "[文档比对] $Label 一行都没解析出来$hint——文档表格格式变了，这项比对等于没做"
}

# ---------------------------------------------------------------- 读取工具

function Split-CsvLine {
    param([string]$Line)
    $fields  = New-Object System.Collections.Generic.List[string]
    $sb      = New-Object System.Text.StringBuilder
    $inQuote = $false
    for ($i = 0; $i -lt $Line.Length; $i++) {
        $ch = $Line[$i]
        if ($inQuote) {
            if ($ch -eq '"') {
                if ($i + 1 -lt $Line.Length -and $Line[$i + 1] -eq '"') { [void]$sb.Append('"'); $i++ }
                else { $inQuote = $false }
            }
            else { [void]$sb.Append($ch) }
        }
        else {
            if ($ch -eq '"') { $inQuote = $true }
            elseif ($ch -eq ',') { [void]$fields.Add($sb.ToString()); [void]$sb.Clear() }
            else { [void]$sb.Append($ch) }
        }
    }
    [void]$fields.Add($sb.ToString())
    return ,$fields.ToArray()
}

function Get-RawLines {
    param([string]$Path)
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    return ($text -split "`r`n|`n") | Where-Object { $_.Trim() -ne '' }
}

function Has-Bom {
    param([string]$Path)
    $b = [System.IO.File]::ReadAllBytes($Path)
    return ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
}

function Get-Table {
    param([string]$Name)
    $p = Join-Path $tableDir $Name
    if (-not (Test-Path -LiteralPath $p)) { return @() }
    return @(Import-Csv -LiteralPath $p -Encoding UTF8)
}

function Get-Col {
    param($Rows, [string]$Column)
    return @($Rows | ForEach-Object { $_.$Column } | Where-Object { $_ -ne '' -and $null -ne $_ })
}

# ---------------------------------------------------------------- 1. 编码与列数

$csvFiles = Get-ChildItem -LiteralPath $tableDir -Filter *.csv | Sort-Object Name
if ($csvFiles.Count -eq 0) { throw "配置表目录为空: $tableDir" }

foreach ($f in $csvFiles) {
    if (-not (Has-Bom $f.FullName)) { Add-Error "[编码] $($f.Name) 缺少 UTF-8 BOM，Excel 打开可能乱码" }

    $lines = @(Get-RawLines $f.FullName)
    if ($lines.Count -eq 0) { Add-Error "[结构] $($f.Name) 是空文件"; continue }

    $header = Split-CsvLine $lines[0]
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $fieldCount = (Split-CsvLine $lines[$i]).Count
        if ($fieldCount -ne $header.Count) {
            Add-Error "[列数] $($f.Name) 第 $($i + 1) 行有 $fieldCount 列，表头为 $($header.Count) 列"
        }
    }
    if ($header.Count -ne ($header | Select-Object -Unique).Count) {
        Add-Error "[列数] $($f.Name) 表头存在重复字段名"
    }
}

# ---------------------------------------------------------------- 2. 主键唯一

$primaryKeys = @{
    'attribute_def.csv'   = 'attr_id'
    'stat_def.csv'        = 'stat_id'
    'curve_def.csv'       = 'curve_id'
    'attr_to_stat.csv'    = 'id'
    'level_growth.csv'    = 'level'
    'damage_type.csv'     = 'type_id'
    'status_effect.csv'   = 'status_id'
    'skill_base.csv'      = 'skill_id'
    'rarity_def.csv'      = 'rarity_id'
    'equip_base.csv'      = 'equip_id'
    'affix_pool.csv'      = 'affix_id'
    'item_base.csv'       = 'item_id'
    'enemy_base.csv'      = 'enemy_id'
    'enemy_team.csv'      = 'team_id'
    'drop_table.csv'      = 'drop_row_id'
    'difficulty_config.csv' = 'difficulty_id'
    'map_region.csv'      = 'node_id'
    'map_local.csv'       = 'scene_id'
    'roaming_spawn.csv'   = 'spawn_id'
    'dungeon_room.csv'    = 'room_id'
    'hidden_trigger.csv'  = 'trigger_id'
    'combat_const.csv'    = 'const_id'
    'weapon_type_def.csv' = 'weapon_type'
    'event_skill_def.csv' = 'skill_id'
    'event_check.csv'     = 'check_id'
    'character_base.csv'  = 'char_id'
    'equip_slot_def.csv'  = 'slot_id'
    'building_def.csv'    = 'building_id'
    'skill_star_def.csv'  = 'star'
    'growth_const.csv'    = 'const_id'
    'skill_active.csv'    = 'skill_id'
    'skill_passive.csv'   = 'skill_id'
    'buff_def.csv'        = 'buff_id'
    'buff_grant.csv'      = 'grant_id'
    'set_def.csv'         = 'set_id'
    'feature_toggle.csv'  = 'toggle_id'
    'guide_step.csv'      = 'step_id'
    'recruit_def.csv'     = 'char_id'
    'npc_guard.csv'       = 'guard_id'
    'ui_text.csv'         = 'text_id'
    'talent_def.csv'      = 'talent_id'
    'chapter_def.csv'     = 'chapter_id'
    'story_node.csv'      = 'node_id'
    'npc_def.csv'         = 'npc_id'
    'npc_offer.csv'       = 'offer_id'
    'npc_quest.csv'       = 'quest_id'
    'world_event.csv'     = 'event_id'
}

foreach ($name in $primaryKeys.Keys) {
    $key = $primaryKeys[$name]
    $rows = Get-Table $name
    if ($rows.Count -eq 0) { continue }
    if (-not ($rows[0].PSObject.Properties.Name -contains $key)) {
        Add-Error "[主键] $name 缺少主键列 $key"
        continue
    }
    $vals = @($rows | ForEach-Object { $_.$key })
    $dups = $vals | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name }
    foreach ($d in $dups) { Add-Error "[主键] $name 重复: $d" }
    foreach ($v in $vals) { if ($v -eq '' -or $null -eq $v) { Add-Error "[主键] $name 存在空主键" } }
}

# ---------------------------------------------------------------- 3. 载入全部表

$Tables = @{}
foreach ($f in $csvFiles) { $Tables[$f.Name] = Get-Table $f.Name }

# ---------------------------------------------------------------- 4. 单值引用

$simpleRefs = @(
    @{ From = 'attr_to_stat.csv';   Field = 'attr_id';   To = 'attribute_def.csv'; Target = 'attr_id' }
    @{ From = 'attr_to_stat.csv';   Field = 'stat_id';   To = 'stat_def.csv';      Target = 'stat_id' }
    @{ From = 'attr_to_stat.csv';   Field = 'curve';     To = 'curve_def.csv';     Target = 'curve_id' }
    @{ From = 'status_effect.csv';  Field = 'damage_type'; To = 'damage_type.csv'; Target = 'type_id' }
    @{ From = 'status_effect.csv';  Field = 'trigger_stat'; To = 'attribute_def.csv'; Target = 'attr_id' }
    @{ From = 'status_effect.csv';  Field = 'power_stat'; To = 'attribute_def.csv'; Target = 'attr_id' }
    @{ From = 'skill_base.csv';     Field = 'damage_type'; To = 'damage_type.csv'; Target = 'type_id' }
    @{ From = 'skill_base.csv';     Field = 'status_id';  To = 'status_effect.csv'; Target = 'status_id' }
    @{ From = 'equip_base.csv';     Field = 'rarity';    To = 'rarity_def.csv';    Target = 'rarity_id' }
    @{ From = 'affix_pool.csv';     Field = 'min_rarity'; To = 'rarity_def.csv';   Target = 'rarity_id' }
    @{ From = 'enemy_base.csv';     Field = 'drop_group'; To = 'drop_table.csv';   Target = 'drop_group' }
    @{ From = 'roaming_spawn.csv';  Field = 'region_id'; To = 'map_region.csv';   Target = 'node_id' }
    @{ From = 'roaming_spawn.csv';  Field = 'team_id';   To = 'enemy_team.csv';   Target = 'team_id' }
    @{ From = 'map_local.csv';      Field = 'parent_node'; To = 'map_region.csv'; Target = 'node_id' }
    @{ From = 'map_region.csv';     Field = 'enter_scene'; To = 'map_local.csv';  Target = 'scene_id' }
    @{ From = 'dungeon_room.csv';   Field = 'enemy_team'; To = 'enemy_team.csv';  Target = 'team_id' }
    @{ From = 'dungeon_room.csv';   Field = 'chest_id';  To = 'drop_table.csv';    Target = 'drop_group' }
    @{ From = 'dungeon_room.csv';   Field = 'hidden_trigger'; To = 'hidden_trigger.csv'; Target = 'trigger_id' }
    @{ From = 'dungeon_room.csv';   Field = 'scene_id';  To = 'map_local.csv';     Target = 'scene_id' }
    @{ From = 'hidden_trigger.csv'; Field = 'room_id';   To = 'dungeon_room.csv'; Target = 'room_id' }
    @{ From = 'hidden_trigger.csv'; Field = 'scene_id';  To = 'map_local.csv';     Target = 'scene_id' }
    @{ From = 'hidden_trigger.csv'; Field = 'required_item'; To = 'item_base.csv'; Target = 'item_id' }
    @{ From = 'difficulty_drop_rate.csv'; Field = 'difficulty_id'; To = 'difficulty_config.csv'; Target = 'difficulty_id' }
    @{ From = 'difficulty_drop_rate.csv'; Field = 'rarity_id'; To = 'rarity_def.csv'; Target = 'rarity_id' }
    @{ From = 'equip_base.csv';      Field = 'weapon_type'; To = 'weapon_type_def.csv'; Target = 'weapon_type' }
    @{ From = 'event_skill_def.csv'; Field = 'related_attr'; To = 'attribute_def.csv';  Target = 'attr_id' }
    @{ From = 'event_check.csv';     Field = 'region_id';   To = 'map_region.csv';      Target = 'node_id' }
    @{ From = 'event_check.csv';     Field = 'scene_id';    To = 'map_local.csv';       Target = 'scene_id' }
    @{ From = 'event_check.csv';     Field = 'room_id';     To = 'dungeon_room.csv';    Target = 'room_id' }
    @{ From = 'character_base.csv';  Field = 'weapon_type'; To = 'weapon_type_def.csv'; Target = 'weapon_type' }
    @{ From = 'character_base_skill.csv'; Field = 'char_id';  To = 'character_base.csv';  Target = 'char_id' }
    @{ From = 'character_base_skill.csv'; Field = 'skill_id'; To = 'event_skill_def.csv'; Target = 'skill_id' }
    @{ From = 'enemy_skill.csv';    Field = 'enemy_id';   To = 'enemy_base.csv';      Target = 'enemy_id' }
    @{ From = 'enemy_skill.csv';    Field = 'skill_id';   To = 'skill_base.csv';      Target = 'skill_id' }
    @{ From = 'enemy_equip.csv';    Field = 'enemy_id';   To = 'enemy_base.csv';      Target = 'enemy_id' }
    @{ From = 'enemy_equip.csv';    Field = 'equip_id';   To = 'equip_base.csv';      Target = 'equip_id' }
    @{ From = 'npc_def.csv';        Field = 'spar_team_id'; To = 'enemy_team.csv';   Target = 'team_id' }
# npc_def.place_id 可以是小地图场景或大地图区域，单独在 5.21 里查
    # `npc_favor`／`npc_quest` 的 npc_id 可以是 NPC 或同伴，单独在 5.21e 里查
    @{ From = 'npc_offer.csv';      Field = 'npc_id';    To = 'npc_def.csv';         Target = 'npc_id' }
    @{ From = 'enemy_equip.csv';    Field = 'slot_id';    To = 'equip_slot_def.csv';  Target = 'slot_id' }
    @{ From = 'talent_effect.csv';  Field = 'talent_id';  To = 'talent_def.csv';      Target = 'talent_id' }
    @{ From = 'story_node.csv';      Field = 'chapter_id'; To = 'chapter_def.csv';    Target = 'chapter_id' }
    # story_node.place_id 与 npc_def.place_id 同一套：**小地图或大地图节点**都算数
    # （落雁坡没有自己的小地图，本命机遇要在那儿触发），所以单独在 5.21 里查
    # 抉择增益的四列（0.29.1）：属性点层引 attribute_def、固定值层引 stat_def
    @{ From = 'story_node.csv';      Field = 'bonus_attr_id'; To = 'attribute_def.csv'; Target = 'attr_id' }
    @{ From = 'story_node.csv';      Field = 'bonus_stat_id'; To = 'stat_def.csv';      Target = 'stat_id' }
    # 观察点（0.29.1）：三列各自指向真东西
    @{ From = 'flavor_point.csv';    Field = 'scene_id';   To = 'map_local.csv';      Target = 'scene_id' }
    @{ From = 'flavor_point.csv';    Field = 'room_id';    To = 'dungeon_room.csv';   Target = 'room_id' }
    @{ From = 'flavor_point.csv';    Field = 'region_id';  To = 'map_region.csv';     Target = 'node_id' }
    # 对话容器（0.31.0）：跳转目标只能是对话节点（说话人／给的东西是"两处任一命中"，单独查）
    @{ From = 'dialogue_node.csv';   Field = 'next_node_id'; To = 'dialogue_node.csv'; Target = 'node_id' }
    @{ From = 'dialogue_option.csv'; Field = 'node_id';      To = 'dialogue_node.csv'; Target = 'node_id' }
    @{ From = 'dialogue_option.csv'; Field = 'next_node_id'; To = 'dialogue_node.csv'; Target = 'node_id' }
    @{ From = 'guide_step.csv';      Field = 'chapter_id'; To = 'chapter_def.csv';    Target = 'chapter_id' }
    @{ From = 'chapter_def.csv';     Field = 'next_chapter_id'; To = 'chapter_def.csv'; Target = 'chapter_id' }
    @{ From = 'equip_base.csv';     Field = 'slot';        To = 'equip_slot_def.csv';  Target = 'slot_id' }
    @{ From = 'shop_stock.csv';     Field = 'shop_id';     To = 'building_def.csv';    Target = 'stock_group' }
    @{ From = 'skill_active.csv';      Field = 'skill_id'; To = 'skill_base.csv';     Target = 'skill_id' }
    @{ From = 'skill_passive.csv';     Field = 'skill_id'; To = 'skill_base.csv';     Target = 'skill_id' }
    @{ From = 'skill_passive_stat.csv'; Field = 'skill_id'; To = 'skill_base.csv';    Target = 'skill_id' }
    @{ From = 'skill_base.csv';        Field = 'learn_req_attr'; To = 'attribute_def.csv'; Target = 'attr_id' }
    @{ From = 'skill_base.csv';        Field = 'star';     To = 'skill_star_def.csv'; Target = 'star' }
    @{ From = 'buff_stat.csv';         Field = 'buff_id';  To = 'buff_def.csv';       Target = 'buff_id' }
    @{ From = 'buff_grant.csv';        Field = 'buff_id';  To = 'buff_def.csv';       Target = 'buff_id' }
    @{ From = 'set_member.csv';        Field = 'set_id';   To = 'set_def.csv';        Target = 'set_id' }
    @{ From = 'set_bonus.csv';         Field = 'set_id';   To = 'set_def.csv';        Target = 'set_id' }
    @{ From = 'set_bonus.csv';         Field = 'buff_id';  To = 'buff_def.csv';       Target = 'buff_id' }
)

foreach ($r in $simpleRefs) {
    $targetSet = @{}
    foreach ($v in (Get-Col $Tables[$r.To] $r.Target)) { $targetSet[$v] = $true }
    foreach ($row in $Tables[$r.From]) {
        $v = $row.($r.Field)
        if ($v -eq '' -or $null -eq $v) { continue }
        if (-not $targetSet.ContainsKey($v)) {
            Add-Error "[引用] $($r.From) 的 $($r.Field)='$v' 在 $($r.To) 中不存在"
        }
    }
}

# ---------------------------------------------------------------- 5. 复合引用

$validElements = @('external', 'internal', 'odd')
# 槽位清单来自 equip_slot_def，不再写死，否则增删槽位必然漏改校验器
$validSlots    = @(Get-Col $Tables['equip_slot_def.csv'] 'slot_id')

# 5.1 招式系别（系别已随 0.6.0 挪到 skill_active）
foreach ($row in $Tables['skill_active.csv']) {
    if ($row.element -ne '' -and $validElements -notcontains $row.element) {
        Add-Error "[枚举] skill_active.csv $($row.skill_id).element='$($row.element)' 不是合法系别"
    }
}
# 5.2 装备槽位
foreach ($row in $Tables['equip_base.csv']) {
    if ($validSlots -notcontains $row.slot) {
        Add-Error "[枚举] equip_base.csv $($row.equip_id).slot='$($row.slot)' 不是合法槽位"
    }
    if ($row.element -ne '' -and $validElements -notcontains $row.element) {
        Add-Error "[枚举] equip_base.csv $($row.equip_id).element='$($row.element)' 不是合法系别"
    }
}
# 5.3 词条 target 前缀与槽位列表
foreach ($row in $Tables['affix_pool.csv']) {
    if ($row.target.StartsWith('attr:')) {
        $targetId = $row.target.Substring(5)
        if ((Get-Col $Tables['attribute_def.csv'] 'attr_id') -notcontains $targetId) {
            Add-Error "[引用] affix_pool.csv $($row.affix_id).target='$targetId' 不是合法属性"
        }
    }
    elseif ($row.target.StartsWith('stat:')) {
        $targetId = $row.target.Substring(5)
        if ((Get-Col $Tables['stat_def.csv'] 'stat_id') -notcontains $targetId) {
            Add-Error "[引用] affix_pool.csv $($row.affix_id).target='$targetId' 不是合法派生数值"
        }
    }
    else { Add-Error "[格式] affix_pool.csv $($row.affix_id).target 缺少 attr:/stat: 前缀" }

    foreach ($s in ($row.allow_slots -split '\|')) {
        if ($s -ne '' -and $validSlots -notcontains $s) {
            Add-Error "[枚举] affix_pool.csv $($row.affix_id).allow_slots 含非法槽位 '$s'"
        }
    }
}
# 5.4 队伍成员
$enemyIds = @{}
foreach ($v in (Get-Col $Tables['enemy_base.csv'] 'enemy_id')) { $enemyIds[$v] = $true }
foreach ($row in $Tables['enemy_team.csv']) {
    foreach ($m in ($row.members -split ';')) {
        if ($m -eq '') { continue }
        $parts = $m -split ':'
        if ($parts.Count -ne 2 -or $parts[1] -notmatch '^\d+$') {
            Add-Error "[格式] enemy_team.csv $($row.team_id).members 项 '$m' 应为 id:数量"
            continue
        }
        if (-not $enemyIds.ContainsKey($parts[0])) {
            Add-Error "[引用] enemy_team.csv $($row.team_id).members 引用了不存在的敌人 '$($parts[0])'"
        }
    }
}
# 5.5 掉落目标物品
$validItemIds = @{}
foreach ($v in (Get-Col $Tables['item_base.csv'] 'item_id'))      { $validItemIds[$v] = $true }
foreach ($v in (Get-Col $Tables['equip_base.csv'] 'equip_id'))    { $validItemIds[$v] = $true }
foreach ($row in $Tables['drop_table.csv']) {
    if (-not $validItemIds.ContainsKey($row.item_id)) {
        Add-Error "[引用] drop_table.csv $($row.drop_row_id).item_id='$($row.item_id)' 不存在"
    }
}
# 5.6 房间出口
$roomIds = @{}
foreach ($v in (Get-Col $Tables['dungeon_room.csv'] 'room_id')) { $roomIds[$v] = $true }
foreach ($row in $Tables['dungeon_room.csv']) {
    foreach ($e in ($row.exit_rooms -split '\|')) {
        if ($e -ne '' -and -not $roomIds.ContainsKey($e)) {
            Add-Error "[引用] dungeon_room.csv $($row.room_id).exit_rooms 指向不存在的房间 '$e'"
        }
    }
}
# 5.7 隐藏触发奖励
$equipIds = @{}
foreach ($v in (Get-Col $Tables['equip_base.csv'] 'equip_id')) { $equipIds[$v] = $true }
$itemIds = @{}
foreach ($v in (Get-Col $Tables['item_base.csv'] 'item_id')) { $itemIds[$v] = $true }
foreach ($row in $Tables['hidden_trigger.csv']) {
    switch ($row.reward_type) {
        'boss'      { if (-not $enemyIds.ContainsKey($row.reward_id))        { Add-Error "[引用] hidden_trigger.csv $($row.trigger_id) 奖励敌人 '$($row.reward_id)' 不存在" } }
        'equip'     { if (-not $equipIds.ContainsKey($row.reward_id))        { Add-Error "[引用] hidden_trigger.csv $($row.trigger_id) 奖励装备 '$($row.reward_id)' 不存在" } }
        'skillbook' { if (-not $itemIds.ContainsKey($row.reward_id))         { Add-Error "[引用] hidden_trigger.csv $($row.trigger_id) 奖励秘籍 '$($row.reward_id)' 不存在" } }
        'room'      { if (-not $roomIds.ContainsKey($row.reward_id))         { Add-Error "[引用] hidden_trigger.csv $($row.trigger_id) 奖励房间 '$($row.reward_id)' 不存在" } }
        'event'     { }
        default     { Add-Error "[枚举] hidden_trigger.csv $($row.trigger_id).reward_type='$($row.reward_type)' 不合法" }
    }
}

# 5.8 事件判定的判定来源（attr: / skill: 前缀）
foreach ($row in $Tables['event_check.csv']) {
    if ($row.check_source.StartsWith('attr:')) {
        $srcRef = $row.check_source.Substring(5)
        if ((Get-Col $Tables['attribute_def.csv'] 'attr_id') -notcontains $srcRef) {
            Add-Error "[引用] event_check.csv $($row.check_id).check_source='$srcRef' 不是合法属性"
        }
    }
    elseif ($row.check_source.StartsWith('skill:')) {
        $srcRef = $row.check_source.Substring(6)
        if ((Get-Col $Tables['event_skill_def.csv'] 'skill_id') -notcontains $srcRef) {
            Add-Error "[引用] event_check.csv $($row.check_id).check_source='$srcRef' 不是合法非战斗技能"
        }
    }
    else {
        Add-Error "[格式] event_check.csv $($row.check_id).check_source 缺少 attr:/skill: 前缀"
    }
    if (@('hard', 'soft') -notcontains $row.check_type) {
        Add-Error "[枚举] event_check.csv $($row.check_id).check_type='$($row.check_type)' 应为 hard 或 soft"
    }
    # 奖励引用
    switch ($row.reward_type) {
        'item'      { if (-not $itemIds.ContainsKey($row.reward_id))  { Add-Error "[引用] event_check.csv $($row.check_id) 奖励物品 '$($row.reward_id)' 不存在" } }
        'equip'     { if (-not $equipIds.ContainsKey($row.reward_id)) { Add-Error "[引用] event_check.csv $($row.check_id) 奖励装备 '$($row.reward_id)' 不存在" } }
        'skillbook' { if (-not $itemIds.ContainsKey($row.reward_id))  { Add-Error "[引用] event_check.csv $($row.check_id) 奖励秘籍 '$($row.reward_id)' 不存在" } }
        'room'      { if (-not $roomIds.ContainsKey($row.reward_id))  { Add-Error "[引用] event_check.csv $($row.check_id) 奖励房间 '$($row.reward_id)' 不存在" } }
        'boss'      { if (-not $enemyIds.ContainsKey($row.reward_id)) { Add-Error "[引用] event_check.csv $($row.check_id) 奖励敌人 '$($row.reward_id)' 不存在" } }
        'event'     { }
        'none'      { }
        default     { Add-Error "[枚举] event_check.csv $($row.check_id).reward_type='$($row.reward_type)' 不合法" }
    }
}

# 5.9 角色模板：属性合计、起始武学与装备
foreach ($row in $Tables['character_base.csv']) {
    $attrSum = 0
    $allInt  = $true
    foreach ($attrCol in @('initial_str', 'initial_con', 'initial_agi', 'initial_int', 'initial_luk', 'initial_wu', 'initial_gen')) {
        $attrVal = 0
        if ([int]::TryParse($row.$attrCol, [ref]$attrVal)) { $attrSum += $attrVal } else { $allInt = $false }
    }
    if (-not $allInt) {
        Add-Error "[数值] character_base.csv $($row.char_id) 属性列存在非整数"
    }
    else {
        $declaredTotal = 0
        if ([int]::TryParse($row.attr_total, [ref]$declaredTotal)) {
            if ($attrSum -ne $declaredTotal) {
                Add-Error "[数值] character_base.csv $($row.char_id) 五项属性合计 $attrSum，与 attr_total $declaredTotal 不符"
            }
        }
        else { Add-Error "[数值] character_base.csv $($row.char_id).attr_total 不是整数" }
    }
    foreach ($skillRef in ($row.start_skill_ids -split '[;|]')) {
        if ($skillRef -ne '' -and (Get-Col $Tables['skill_base.csv'] 'skill_id') -notcontains $skillRef) {
            Add-Error "[引用] character_base.csv $($row.char_id).start_skill_ids 引用了不存在的招式 '$skillRef'"
        }
    }
    foreach ($equipRef in ($row.start_equip_ids -split '[;|]')) {
        if ($equipRef -ne '' -and (Get-Col $Tables['equip_base.csv'] 'equip_id') -notcontains $equipRef) {
            Add-Error "[引用] character_base.csv $($row.char_id).start_equip_ids 引用了不存在的装备 '$equipRef'"
        }
    }
}

# 5.10 每个模板必须给出全部非战斗技能的初始等级
$allEventSkills = @(Get-Col $Tables['event_skill_def.csv'] 'skill_id')
foreach ($row in $Tables['character_base.csv']) {
    $covered = @($Tables['character_base_skill.csv'] | Where-Object { $_.char_id -eq $row.char_id } | ForEach-Object { $_.skill_id })
    foreach ($eventSkill in $allEventSkills) {
        if ($covered -notcontains $eventSkill) {
            Add-Error "[完整] 角色 $($row.char_id) 缺少非战斗技能 $eventSkill 的初始等级"
        }
    }
    foreach ($skillRow in @($Tables['character_base_skill.csv'] | Where-Object { $_.char_id -eq $row.char_id })) {
        $lvl = -1
        if ([int]::TryParse($skillRow.level, [ref]$lvl)) {
            if ($lvl -lt 0 -or $lvl -gt 10) {
                Add-Error "[数值] character_base_skill.csv $($row.char_id)/$($skillRow.skill_id) 等级 $lvl 超出 0~10"
            }
        }
        else { Add-Error "[数值] character_base_skill.csv $($row.char_id)/$($skillRow.skill_id) 等级不是整数" }
    }
}

# 5.11 战斗常数：管线依赖的常数必须全部存在
foreach ($constId in @('crit_base_mult', 'def_const', 'def_level_coeff', 'variance_min', 'variance_max', 'backstab_poise_reduce', 'surprise_damage_bonus', 'dot_execute_threshold')) {
    if ((Get-Col $Tables['combat_const.csv'] 'const_id') -notcontains $constId) {
        Add-Error "[完整] combat_const.csv 缺少伤害管线依赖的常数 $constId"
    }
}

# 5.12 01_角色系统.md 里的「备选模板数据」：虽未启用，同样不能烂
$rosterDocPath = Join-Path $Root 'docs/design/01_角色系统.md'
if (Test-Path -LiteralPath $rosterDocPath) {
    $rosterText  = Get-Content -LiteralPath $rosterDocPath -Encoding UTF8 -Raw
    $rosterMatch = [regex]::Match($rosterText, '###\s*备选模板数据（未启用）(.*?)(?=\r?\n###\s)', 'Singleline')
    if ($rosterMatch.Success) {
        $rosterBlocks = @([regex]::Matches($rosterMatch.Groups[1].Value, '(?s)```\r?\n(.*?)\r?\n```') |
            ForEach-Object { $_.Groups[1].Value })
        if ($rosterBlocks.Count -ge 2) {
            $altCharIds = @()
            foreach ($line in @($rosterBlocks[0] -split "\r?\n" | Where-Object { $_.Trim() -ne '' })) {
                $cells = $line -split ','
                if ($cells.Count -ne 16) {
                    Add-Error "[备选模板] 字段数 $($cells.Count)，应为 16：$line"
                    continue
                }
                $altCharIds += $cells[0]
                $attrSum = 0
                $allInt  = $true
                foreach ($colIndex in 4..10) {
                    $attrVal = 0
                    if ([int]::TryParse($cells[$colIndex], [ref]$attrVal)) { $attrSum += $attrVal } else { $allInt = $false }
                }
                $declaredTotal = 0
                if (-not [int]::TryParse($cells[11], [ref]$declaredTotal)) {
                    Add-Error "[备选模板] $($cells[0]).attr_total 不是整数"
                }
                elseif (-not $allInt) { Add-Error "[备选模板] $($cells[0]) 属性列存在非整数" }
                elseif ($attrSum -ne $declaredTotal) {
                    Add-Error "[备选模板] $($cells[0]) 属性合计 $attrSum 与 attr_total $declaredTotal 不符"
                }
                if ((Get-Col $Tables['weapon_type_def.csv'] 'weapon_type') -notcontains $cells[3]) {
                    Add-Error "[备选模板] $($cells[0]).weapon_type='$($cells[3])' 不存在"
                }
                foreach ($skillRef in ($cells[13] -split '[;|]')) {
                    if ($skillRef -ne '' -and (Get-Col $Tables['skill_base.csv'] 'skill_id') -notcontains $skillRef) {
                        Add-Error "[备选模板] $($cells[0]).start_skill_ids 引用了不存在的招式 '$skillRef'"
                    }
                }
                foreach ($equipRef in ($cells[14] -split '[;|]')) {
                    if ($equipRef -ne '' -and (Get-Col $Tables['equip_base.csv'] 'equip_id') -notcontains $equipRef) {
                        Add-Error "[备选模板] $($cells[0]).start_equip_ids 引用了不存在的装备 '$equipRef'"
                    }
                }
            }

            $altCovered = @{}
            foreach ($line in @($rosterBlocks[1] -split "\r?\n" | Where-Object { $_.Trim() -ne '' })) {
                $cells = $line -split ','
                if ($cells.Count -ne 3) {
                    Add-Error "[备选模板] 技能行字段数 $($cells.Count)，应为 3：$line"
                    continue
                }
                if ($altCharIds -notcontains $cells[0]) { Add-Error "[备选模板] 技能行引用了未定义的 char_id '$($cells[0])'" }
                if ($allEventSkills -notcontains $cells[1]) { Add-Error "[备选模板] 技能行引用了未定义的非战斗技能 '$($cells[1])'" }
                if (-not $altCovered.ContainsKey($cells[0])) { $altCovered[$cells[0]] = @() }
                $altCovered[$cells[0]] += $cells[1]
                $altLevel = -1
                if ([int]::TryParse($cells[2], [ref]$altLevel)) {
                    if ($altLevel -lt 0 -or $altLevel -gt 10) { Add-Error "[备选模板] $($cells[0])/$($cells[1]) 等级 $altLevel 超出 0~10" }
                }
                else { Add-Error "[备选模板] $($cells[0])/$($cells[1]) 等级不是整数" }
            }
            foreach ($charRef in $altCharIds) {
                foreach ($eventSkill in $allEventSkills) {
                    if (-not $altCovered.ContainsKey($charRef) -or $altCovered[$charRef] -notcontains $eventSkill) {
                        Add-Error "[备选模板] 角色 $charRef 缺少非战斗技能 $eventSkill 的初始等级"
                    }
                }
            }
            Write-Output ("  备选模板 : {0} 个" -f $altCharIds.Count)
        }
        else {
            Add-Warning '[备选模板] 01_角色系统.md 的备选模板小节里没找到两段数据块'
        }
    }
}

# 5.13 装备槽位与武器类型
$weaponTypes    = @(Get-Col $Tables['weapon_type_def.csv'] 'weapon_type')
$slotWeaponAllow = @{}
foreach ($row in $Tables['equip_slot_def.csv']) {
    $allowedTypes = @()
    foreach ($weaponType in ($row.allow_weapon_types -split '\|')) {
        if ($weaponType -eq '') { continue }
        if ($weaponTypes -notcontains $weaponType) {
            Add-Error "[引用] equip_slot_def.csv $($row.slot_id).allow_weapon_types 含未定义的武器类型 '$weaponType'"
        }
        $allowedTypes += $weaponType
    }
    $slotWeaponAllow[$row.slot_id] = $allowedTypes

    $maxEquip = 0
    if ([int]::TryParse($row.max_equip, [ref]$maxEquip)) {
        if ($maxEquip -lt 1) { Add-Error "[数值] equip_slot_def.csv $($row.slot_id).max_equip 必须大于 0" }
    }
    else { Add-Error "[数值] equip_slot_def.csv $($row.slot_id).max_equip 不是整数" }
}
foreach ($row in $Tables['equip_base.csv']) {
    if (-not $slotWeaponAllow.ContainsKey($row.slot)) { continue }  # 槽位不存在已由引用检查报过
    $allowedTypes = $slotWeaponAllow[$row.slot]
    if ($allowedTypes.Count -gt 0) {
        if ($row.weapon_type -eq '') {
            Add-Error "[完整] equip_base.csv $($row.equip_id) 位于武器槽 $($row.slot)，但没有填 weapon_type"
        }
        elseif ($allowedTypes -notcontains $row.weapon_type) {
            Add-Error "[引用] equip_base.csv $($row.equip_id).weapon_type='$($row.weapon_type)' 不被槽位 $($row.slot) 允许"
        }
    }
    elseif ($row.weapon_type -ne '') {
        Add-Error "[完整] equip_base.csv $($row.equip_id) 位于非武器槽 $($row.slot)，却填了 weapon_type"
    }
}

# 5.14 商店：价格方向与货架完整性
$stockGroupCount = @{}
foreach ($row in $Tables['shop_stock.csv']) {
    if (-not $stockGroupCount.ContainsKey($row.shop_id)) { $stockGroupCount[$row.shop_id] = 0 }
    $stockGroupCount[$row.shop_id]++

    if (-not $validItemIds.ContainsKey($row.item_id)) {
        Add-Error "[引用] shop_stock.csv $($row.shop_id)/$($row.item_id) 指向不存在的物品或装备"
    }

    $buyPrice = 0
    $sellPrice = 0
    $hasBuy  = [int]::TryParse($row.buy_price, [ref]$buyPrice)
    $hasSell = [int]::TryParse($row.sell_price, [ref]$sellPrice)

    if (-not $hasBuy -and -not $hasSell) {
        Add-Error "[完整] shop_stock.csv $($row.shop_id)/$($row.item_id) 买入价与卖出价都为空"
    }
    if ($hasBuy -and $buyPrice -le 0) {
        Add-Error "[数值] shop_stock.csv $($row.shop_id)/$($row.item_id) 买入价必须大于 0"
    }
    if ($hasBuy -and $hasSell -and $buyPrice -le $sellPrice) {
        Add-Error "[数值] shop_stock.csv $($row.shop_id)/$($row.item_id) 买入价 $buyPrice 未大于卖出价 $sellPrice，玩家可以刷钱"
    }
}
foreach ($row in $Tables['building_def.csv']) {
    if ($row.stock_group -ne '' -and -not $stockGroupCount.ContainsKey($row.stock_group)) {
        Add-Error "[引用] building_def.csv $($row.building_id).stock_group='$($row.stock_group)' 没有任何对应的货架行"
    }
}

# 5.15 武学：种类、配套表完整性、关卡门槛与获取途径
$skillRows = @($Tables['skill_base.csv'])
$activeSkillIds  = @($Tables['skill_active.csv']  | ForEach-Object { $_.skill_id })
$passiveSkillIds = @($Tables['skill_passive.csv'] | ForEach-Object { $_.skill_id })
$passiveStatBySkill = @{}
foreach ($row in $Tables['skill_passive_stat.csv']) {
    if (-not $passiveStatBySkill.ContainsKey($row.skill_id)) { $passiveStatBySkill[$row.skill_id] = @() }
    $passiveStatBySkill[$row.skill_id] += $row
    if ($row.target.StartsWith('attr:')) {
        $t = $row.target.Substring(5)
        if ((Get-Col $Tables['attribute_def.csv'] 'attr_id') -notcontains $t) {
            Add-Error "[引用] skill_passive_stat.csv $($row.skill_id).target='$t' 不是合法属性"
        }
    }
    elseif ($row.target.StartsWith('stat:')) {
        $t = $row.target.Substring(5)
        if ((Get-Col $Tables['stat_def.csv'] 'stat_id') -notcontains $t) {
            Add-Error "[引用] skill_passive_stat.csv $($row.skill_id).target='$t' 不是合法派生数值"
        }
    }
    else { Add-Error "[格式] skill_passive_stat.csv $($row.skill_id).target 缺少 attr:/stat: 前缀" }
}

$starDefs = @(Get-Col $Tables['skill_star_def.csv'] 'star')
$enemyIdSet = @(Get-Col $Tables['enemy_base.csv'] 'enemy_id')
$triggerIdSet = @(Get-Col $Tables['hidden_trigger.csv'] 'trigger_id')
$checkIdSet = @(Get-Col $Tables['event_check.csv'] 'check_id')
$buildingIdSet = @(Get-Col $Tables['building_def.csv'] 'building_id')

foreach ($row in $skillRows) {
    $sid = $row.skill_id
    if (@('active', 'passive') -notcontains $row.skill_kind) {
        Add-Error "[枚举] skill_base.csv $sid.skill_kind='$($row.skill_kind)' 应为 active 或 passive"
        continue
    }
    if ($starDefs -notcontains $row.star) {
        Add-Error "[引用] skill_base.csv $sid.star='$($row.star)' 未在 skill_star_def 中定义"
    }

    # 每种武学必须有配套的明细行，且只能有一种
    if ($row.skill_kind -eq 'active') {
        if ($activeSkillIds -notcontains $sid) { Add-Error "[完整] skill_base.csv $sid 是招式，但 skill_active.csv 里没有对应行" }
        if ($passiveSkillIds -contains $sid) { Add-Error "[冲突] $sid 同时出现在 skill_active 和 skill_passive" }
        if ($row.weapon_type -eq '') { Add-Error "[完整] skill_base.csv $sid 是招式，必须标 weapon_type" }
        elseif ($row.weapon_type -ne 'any' -and (Get-Col $Tables['weapon_type_def.csv'] 'weapon_type') -notcontains $row.weapon_type) {
            Add-Error "[引用] skill_base.csv $sid.weapon_type='$($row.weapon_type)' 不存在"
        }
    }
    else {
        if ($passiveSkillIds -notcontains $sid) { Add-Error "[完整] skill_base.csv $sid 是内功，但 skill_passive.csv 里没有对应行" }
        if ($activeSkillIds -contains $sid) { Add-Error "[冲突] $sid 同时出现在 skill_active 和 skill_passive" }
        if ($row.weapon_type -ne '') { Add-Error "[完整] skill_base.csv $sid 是内功，不应填 weapon_type" }
        if (-not $passiveStatBySkill.ContainsKey($sid)) {
            Add-Error "[完整] skill_base.csv $sid 是内功，但 skill_passive_stat.csv 里一条加成都没有"
        }
    }

    # 获取途径
    switch ($row.source_type) {
        'drop'   { if ($enemyIdSet -notcontains $row.source_id)    { Add-Error "[引用] skill_base.csv $sid.source_id='$($row.source_id)' 不是已知敌人" } }
        'shop'   { if ($buildingIdSet -notcontains $row.source_id) { Add-Error "[引用] skill_base.csv $sid.source_id='$($row.source_id)' 不是已知建筑" } }
        'hidden' {
            if (($triggerIdSet -notcontains $row.source_id) -and ($checkIdSet -notcontains $row.source_id)) {
                Add-Error "[引用] skill_base.csv $sid.source_id='$($row.source_id)' 既不是 hidden_trigger 也不是 event_check"
            }
        }
        'story'  {
            if ((Get-Col $Tables['story_node.csv'] 'node_id') -notcontains $row.source_id) {
                Add-Error "[引用] skill_base.csv $sid.source_id='$($row.source_id)' 不是 story_node 里的节点"
            }
        }
        # 出身本命机遇（设计 21 §九，0.31.0）：与 story 同一条发放通道，也要指到真实节点
        'origin' {
            if ((Get-Col $Tables['story_node.csv'] 'node_id') -notcontains $row.source_id) {
                Add-Error "[引用] skill_base.csv $sid.source_type=origin 的 source_id='$($row.source_id)' 不是 story_node 里的节点"
            }
        }
        { $_ -in @('start', 'npc', 'item') } { }
        default { Add-Error "[枚举] skill_base.csv $sid.source_type='$($row.source_type)' 不合法" }
    }
}

# story_node：kind 枚举 ＋ 「choice 必须带增益」（设计 20 §八，与构建期校验器同一套口径）
$storyKindEnum = @('chapter_end', 'faction', 'choice', 'origin_gift')
foreach ($row in $Tables['story_node.csv']) {
    if ($storyKindEnum -notcontains [string]$row.kind) {
        Add-Error "[枚举] story_node.csv $($row.node_id).kind='$($row.kind)' 不是 $($storyKindEnum -join '／')"
    }
    if ([string]$row.kind -eq 'choice' -and [string]$row.bonus_attr_id -eq '' -and [string]$row.bonus_stat_id -eq '') {
        Add-Error "[完整] story_node.csv $($row.node_id) 是 kind=choice，但四列增益全空——玩家选完什么也得不到"
    }
}
Write-Output ("  story_node : {0} 行逐行查了 kind 枚举与「choice 必须带增益」" -f @($Tables['story_node.csv']).Count)

# 内功占格：1~3，且与星级对应（★1-2→1 / ★3-4→2 / ★5→3）
$starOfSkill = @{}
foreach ($row in $skillRows) {
    $starValue = 0
    if ([int]::TryParse($row.star, [ref]$starValue)) { $starOfSkill[$row.skill_id] = $starValue }
}
$passiveCapacityMax = 0
foreach ($row in $Tables['growth_const.csv']) {
    if ($row.const_id -eq 'passive_cap_max') { [void][int]::TryParse($row.value, [ref]$passiveCapacityMax) }
}
$totalPassiveCost = 0
foreach ($row in $Tables['skill_passive.csv']) {
    $slotCost = 0
    if (-not [int]::TryParse($row.slot_cost, [ref]$slotCost)) {
        Add-Error "[数值] skill_passive.csv $($row.skill_id).slot_cost 不是整数"
        continue
    }
    if ($slotCost -lt 1 -or $slotCost -gt 3) {
        Add-Error "[数值] skill_passive.csv $($row.skill_id).slot_cost=$slotCost 应在 1~3"
        continue
    }
    $totalPassiveCost += $slotCost
    if ($starOfSkill.ContainsKey($row.skill_id)) {
        $star = $starOfSkill[$row.skill_id]
        $expectedCost = 1
        if ($star -ge 5) { $expectedCost = 3 } elseif ($star -ge 3) { $expectedCost = 2 }
        if ($slotCost -ne $expectedCost) {
            Add-Warning "[平衡] $($row.skill_id) 是 ★$star 却占 $slotCost 格，与「★1-2→1 / ★3-4→2 / ★5→3」不符"
        }
    }
}
if ($passiveCapacityMax -gt 0 -and $totalPassiveCost -le $passiveCapacityMax) {
    Add-Warning "[平衡] 全部内功占格合计 $totalPassiveCost，不超过容量上限 $passiveCapacityMax，容量机制形同虚设"
}

# 成长常数：槽位公式依赖的常数必须齐全
foreach ($constId in @('active_slot_base', 'active_slot_lv_div', 'active_slot_attr_div', 'active_slot_cap',
                       'passive_cap_base', 'passive_cap_lv_div', 'passive_cap_attr_div', 'passive_cap_max',
                       'mastery_max', 'mastery_combat_gain', 'cultivate_cost_growth',
                       'codex_step', 'codex_bonus', 'codex_cap')) {
    if ((Get-Col $Tables['growth_const.csv'] 'const_id') -notcontains $constId) {
        Add-Error "[完整] growth_const.csv 缺少成长系统依赖的常数 $constId"
    }
}

# 5.16 掉落表：与设计 05 的措辞对齐（只报警告——表是策划维护的，开发侧只负责把矛盾摆出来）
#
# 05_装备与掉落.md 写得很具体：章节 Boss「首杀必掉固定珍品武器」、保底「建议 N 值：珍品 20 次／
# 奇珍 60 次／传世 150 次」。这些机械可查，而**第一次跑就查出一条对不上**：
# 大寨主首杀槽 dr_boss_02 的备注写着「首杀固定珍品武器」，它指向的黑风刀 eq_sword_03 却是**良品**
# （而且整张 equip_base 里一把珍品武器都没有）。同类的还有 dr_elite_03 备注「珍品20次保底」→ 青锋剑 良品。
# 之所以只报警告：改法只有「补一件珍品武器」或「把备注改掉」，两条都是策划的内容决定，
# 开发侧不替策划改内容（同 5.15 的「容量形同虚设」那条）。
$equipRowById = @{}
foreach ($r in $Tables['equip_base.csv']) { $equipRowById[$r.equip_id] = $r }
$itemRowById = @{}
foreach ($r in $Tables['item_base.csv']) { $itemRowById[$r.item_id] = $r }
$rarityRank = @{}
$rarityLabel = @{}
$rarityIndex = 0
foreach ($r in $Tables['rarity_def.csv']) {
    $rarityRank[$r.rarity_id] = $rarityIndex
    $rarityLabel[$r.rarity_id] = $r.name_cn
    $rarityIndex++
}
$pityFloor = @{ 'rare' = 20; 'epic' = 60; 'legend' = 150 }
$dropSlotsChecked = 0
$pitySlotsChecked = 0
foreach ($row in $Tables['drop_table.csv']) {
    $dropSlotsChecked++
    $rarity = ''
    if ($equipRowById.ContainsKey($row.item_id))    { $rarity = $equipRowById[$row.item_id].rarity }
    elseif ($itemRowById.ContainsKey($row.item_id)) { $rarity = $itemRowById[$row.item_id].rarity }
    if ($rarity -eq '' -or -not $rarityRank.ContainsKey($rarity)) { continue }

    # ① 首杀必掉槽：设计 05 说章节 Boss 首杀给「固定珍品武器」→ 至少珍品，且必须必掉
    if ($row.first_kill_only -eq '1') {
        if ($rarityRank[$rarity] -lt $rarityRank['rare']) {
            Add-Warning "[掉落] $($row.drop_row_id) 是首杀必掉槽，但 $($row.item_id) 是$($rarityLabel[$rarity])；设计 05 写的是「章节 Boss 首杀必掉固定珍品武器」"
        }
        $rate = 1.0
        if (-not [double]::TryParse([string]$row.base_rate, [ref]$rate) -or $rate -lt 1.0) {
            Add-Warning "[掉落] $($row.drop_row_id) 标了 first_kill_only=1，但 base_rate=$($row.base_rate) 不是必掉"
        }
    }

    # ② 备注里自报的稀有度必须与物品实际稀有度一致（备注是最容易忘改的一处）
    foreach ($key in @($rarityLabel.Keys)) {
        $label = [string]$rarityLabel[$key]
        if ($label -ne '' -and ([string]$row.note).Contains($label) -and $key -ne $rarity) {
            Add-Warning "[掉落] $($row.drop_row_id) 备注写「$label」，但 $($row.item_id) 实际是$($rarityLabel[$rarity])"
        }
    }

    # ③ 保底次数不低于设计建议值
    $pity = 0
    if ([int]::TryParse([string]$row.pity_count, [ref]$pity) -and $pity -gt 0) {
        $pitySlotsChecked++
        if ($pityFloor.ContainsKey($rarity) -and $pity -lt $pityFloor[$rarity]) {
            Add-Warning "[掉落] $($row.drop_row_id) 的 $($rarityLabel[$rarity]) 保底 $pity 次，低于设计建议的 $($pityFloor[$rarity]) 次"
        }
    }
}
Write-Output ("  掉落表对齐 : {0} 槽（其中保底 {1} 槽）已按设计 05 的措辞核对" -f $dropSlotsChecked, $pitySlotsChecked)

# 5.17 01_角色系统.md 的三条设计纪律，在表侧的表现（只报警告：纪律是设计写的，数据将来不符要提醒）
#
# 01 原文：「减伤率不给属性。`dmg_reduction` 只能来自装备和临时效果，上限 60%」
#        「穿透率上限压在 75% 以下」
# 这两条以前只活在文档里——`attr_to_stat.csv` 里今天没有 `dmg_reduction`，但没人拦着以后加一行，
# 而加了以后**没有任何地方会红**（`stat_def.dmg_reduction` 照样有上限，看着一切正常）。
# 所以把它们变成每次验收都会跑的核对：属性映射里不许出现减伤类派生值；穿透与减伤的上限不许超过设计写的值。
$attrMappedStats = @(Get-Col $Tables['attr_to_stat.csv'] 'stat_id')
$attrOnlyForbidden = @('dmg_reduction', 'block_reduction')
foreach ($forbidden in $attrOnlyForbidden) {
    if ($attrMappedStats -contains $forbidden) {
        Add-Warning "[纪律] attr_to_stat.csv 给了 '$forbidden'——01 写明「减伤率不给属性，只能来自装备和临时效果」，请策划确认"
    }
}
$statDefById = @{}
foreach ($r in $Tables['stat_def.csv']) { $statDefById[$r.stat_id] = $r }
$capRules = @(
    @{ Stat = 'pen_rate';        Max = 0.75; Why = '01：穿透率上限压在 75% 以下' },
    @{ Stat = 'dmg_reduction';   Max = 0.6;  Why = '01：减伤率上限 60%' }
)
$capChecked = 0
foreach ($rule in $capRules) {
    if (-not $statDefById.ContainsKey($rule.Stat)) {
        Add-Warning "[纪律] stat_def.csv 里没有 $($rule.Stat)，$($rule.Why) 这条无从落实"
        continue
    }
    $row = $statDefById[$rule.Stat]
    $maxValue = -1.0
    if (-not [double]::TryParse([string]$row.max_value, [ref]$maxValue) -or $maxValue -le 0) {
        Add-Warning "[纪律] stat_def.csv $($rule.Stat) 没有上限值；$($rule.Why)（0 视为无上限）"
        continue
    }
    $capChecked++
    if ($maxValue -gt $rule.Max) {
        Add-Warning "[纪律] stat_def.csv $($rule.Stat).max_value=$maxValue 超过 $($rule.Why)"
    }
}
Write-Output ("  设计纪律 : attr_to_stat {0} 行无减伤类映射；穿透/减伤上限 {1} 项已核对" -f $attrMappedStats.Count, $capChecked)

# 5.18 钥匙道具必须有「使用点」（只报警告：这是内容决定，得策划定）
#
# `is_key_item=1` 只保证「不能丢」（05 文档：钥匙道具是隐藏内容的载体）。可要是没有任何地方认它，
# 玩家就只能拿着两件废物——而且因为不能丢，它们还永久占着背包。
# 逐个反查使用点：`hidden_trigger.required_item`／`required_condition` 文本／`hidden_trigger.reward_id`／
# `event_check.reward_id`／`skill_base(source_type=item)`（秘籍研读即消耗）／
# **对话条件里的 `item:<id>`**（2026-10-04 加：账册那件钥匙道具就是靠终局难题的
# 「必须拿着账册才问」当使用点——两个通道都是「判定认它」，不是「发得出来就算」）。
# **第一次跑就抓到 3 件**：黑风寨号衣／黑风寨腰牌（03 的第三条上山路线「伪装混入」没实现）
# 与醉里乾坤残卷（没有任何 skill_base 行指向它，研读学不到东西）——都记进当前状态等策划。
$usedKeyItems = @{}
$itemIdSet = @(Get-Col $Tables['item_base.csv'] 'item_id')
foreach ($row in $Tables['hidden_trigger.csv']) {
    $req = [string]$row.required_item
    if ($req -ne '') { $usedKeyItems[$req] = $true }
    $condition = [string]$row.required_condition
    foreach ($id in $itemIdSet) {
        if ($condition.Contains($id)) { $usedKeyItems[$id] = $true }
    }
    $reward = [string]$row.reward_id
    if ($reward -ne '') { $usedKeyItems[$reward] = $true }
}
foreach ($row in $Tables['event_check.csv']) {
    $reward = [string]$row.reward_id
    if ($reward -ne '') { $usedKeyItems[$reward] = $true }
}
# 对话条件：`item:<id>` / `item:<id>:<数量>`（条件语言里由 GuideService 解析）
foreach ($table in @('dialogue_node.csv', 'dialogue_option.csv')) {
    foreach ($row in $Tables[$table]) {
        $condition = [string]$row.condition
        foreach ($id in $itemIdSet) {
            if ($condition.Contains("item:$id")) { $usedKeyItems[$id] = $true }
        }
    }
}
$readableSkillbooks = @{}
foreach ($row in $Tables['skill_base.csv']) {
    if ($row.source_type -eq 'item' -and [string]$row.source_id -ne '') {
        $readableSkillbooks[[string]$row.source_id] = $true
    }
}
$keyItemCount = 0
foreach ($row in $Tables['item_base.csv']) {
    if ($row.is_key_item -ne '1') { continue }
    $keyItemCount++
    if ($readableSkillbooks.ContainsKey($row.item_id)) { continue }   # 秘籍：研读即消耗
    if ($usedKeyItems.ContainsKey($row.item_id)) { continue }
    if ($row.item_type -eq 'skillbook') {
        Add-Warning "[孤立] 钥匙道具 $($row.item_id)（$($row.name_cn)）是秘籍，但没有 skill_base 行的 source_id 指向它——研读学不到东西"
    }
    else {
        Add-Warning "[孤立] 钥匙道具 $($row.item_id)（$($row.name_cn)）没有任何使用点：掉落发得出来，但没有任何判定或触发认它（又不能丢，只能占背包）"
    }
}
Write-Output ("  钥匙道具 : {0} 件逐件反查了使用点" -f $keyItemCount)

# 5.19 「拿不到的内容」反查（只报警告）：装备、道具、武学都要有**已实现的**获取来源
#
# 这条是把当初手做的 B0 审计（靠它找出「毒酒没有来源」「6 件良品装没有来源」「22 部武学来源没实现」）
# 变成每次验收都会跑的机器。来源只认**代码真的发得出来**的那几处：
#   掉落表 / 货架 / 角色模板的起始装备 / 事件判定奖励 / 隐藏触发奖励 / 武学已实现的三类来源。
# **第一次跑抓到的正好是三组已知缺口**；前两组（毒酒、6 件良品装）**2026-10-04 复核时已经消失**——
# 毒酒在货架上、那 6 件走的是「敌人穿什么就掉什么」。**教训**：这份清单只认它知道的通道，
# 通道变了（装备来源从 `drop_table` 挪到 `enemy_equip`）而扫描没跟上，它就会把**能拿到的东西**
# 报成"永远拿不到"——假红跟漏报一样有害（策划会照着它去改本来没问题的数据）。
$obtainable = @{}
foreach ($row in $Tables['drop_table.csv'])  { if ([string]$row.item_id -ne '')     { $obtainable[[string]$row.item_id] = $true } }
foreach ($row in $Tables['shop_stock.csv'])  { if ([string]$row.item_id -ne '')     { $obtainable[[string]$row.item_id] = $true } }
foreach ($row in $Tables['event_check.csv']) { if ([string]$row.reward_id -ne '')   { $obtainable[[string]$row.reward_id] = $true } }
foreach ($row in $Tables['hidden_trigger.csv']) { if ([string]$row.reward_id -ne '') { $obtainable[[string]$row.reward_id] = $true } }
# 大地图随机事件的赠礼（0.28.0 Q64 填齐了 effect_id）：`we_hermit` 的良品剑「锈月」
# 原先**只有这一条来源**，漏了它这条扫描就会一直报「玩家永远拿不到」。
# 只收 `effect_kind=gift`——`trade` 的 effect_id 是货架组、`spar` 是队伍，都不是物品。
foreach ($row in $Tables['world_event.csv']) {
    if ([string]$row.effect_kind -eq 'gift' -and [string]$row.effect_id -ne '') { $obtainable[[string]$row.effect_id] = $true }
}
foreach ($row in $Tables['character_base.csv']) {
    foreach ($equip_id in ([string]$row.start_equip_ids -split '\|')) {
        if ($equip_id -ne '') { $obtainable[$equip_id] = $true }
    }
}
# 5.19 补两路**已经落地的**来源（2026-10-04）——不补它们，这条审计就会把能拿到的东西
# 报成「玩家永远拿不到」，而这份清单是给策划看的（假红会让人去改本来没问题的数据）：
#   ① `enemy_equip`：**敌人穿什么就掉什么**（0.14.0 起装备来源从 `drop_table` 挪到了这里；
#      黑风刀、乌木拳套、皮甲、牛皮腰带、皮护肩、皮护腿、淬毒指环都是这么来的）
#   ② `npc_offer`：NPC 的**兑换与偷窃**（铁匠印记、药王符、猎户披肩、藏宝图）
#   ③ 代码里点名的 id（`src` 扫一遍）：剧情物是代码发的——账册由 `battle_screen.TEAM_WIN_ITEMS`
#      在打赢大寨主那一下发。口径与 `test_handshake` 的「旗标有没有人提」一致。
$srcText = ''
$srcDirForAudit = Join-Path $root 'src'
if (Test-Path -LiteralPath $srcDirForAudit) {
    foreach ($f in (Get-ChildItem -LiteralPath $srcDirForAudit -Recurse -File -Filter *.gd)) {
        $srcText += [IO.File]::ReadAllText($f.FullName) + "`n"
    }
}
foreach ($row in $Tables['enemy_equip.csv']) { if ([string]$row.equip_id -ne '') { $obtainable[[string]$row.equip_id] = $true } }
foreach ($row in $Tables['npc_offer.csv'])  { if ([string]$row.item_id  -ne '') { $obtainable[[string]$row.item_id]  = $true } }
$unreachableEquip = 0
foreach ($row in $Tables['equip_base.csv']) {
    if ($obtainable.ContainsKey($row.equip_id) -or $srcText.Contains($row.equip_id)) { continue }
    $unreachableEquip++
    Add-Warning "[可达] 装备 $($row.equip_id)（$($row.name_cn)）没有任何获取来源：不在掉落表／货架／起始装备／事件与隐藏奖励里，玩家永远拿不到"
}
$unreachableItem = 0
foreach ($row in $Tables['item_base.csv']) {
    if ($row.item_type -eq 'currency') { continue }     # 铜钱是货币，不走「发一件物品」那套
    if ($obtainable.ContainsKey($row.item_id) -or $srcText.Contains($row.item_id)) { continue }
    $unreachableItem++
    Add-Warning "[可达] 道具 $($row.item_id)（$($row.name_cn)）没有任何获取来源：不在掉落表／货架／事件与隐藏奖励／NPC 兑换偷窃／代码发放里，玩家永远拿不到"
}
# 武学来源：已实现的是 drop／hidden／item（研读）／start（模板起始）／**story（剧情节点）**；
# 只剩 npc（门派传授）没实现——`shop` 自 2026-10-04（Q3）起**没有数据再用**：那 5 部武学改成
# `item` 秘籍（四家店各上架一本）走已实现的研读通道。这里仍把 shop 留在待办扫描里当**安全网**
# （将来谁又落一行 `source_type=shop` 就会重新出现在这条告警里）。
# 按来源类型汇总成一条（逐条刷屏没意义，缺的是那两套机制本身）。
#
# 为什么把 story 从"没实现"里拿掉（2026-10-04）：0.22.0 起 `StoryService.claim_for()` 就按
# `skill_base.source_type=story` ＋ `source_id=剧情节点` 发武学，`chapter1_end`／`xuanwei`／
# `yaowang` 三个节点的条件（`flag_shen_rescued`／`flag_board_read`／`flag_poison_hall`）也都有人点亮
# ——那 6 部**拿得到**。以前这条警告把它们算成"玩家拿不到"，是扫描没跟上版本；
# 构建期另有一条「story 的 source_id 必须指向真实剧情节点」替这里兜着。
$skillSourceCount = @{}
foreach ($row in $Tables['skill_base.csv']) {
    $kind = [string]$row.source_type
    if (-not $skillSourceCount.ContainsKey($kind)) { $skillSourceCount[$kind] = 0 }
    $skillSourceCount[$kind] = $skillSourceCount[$kind] + 1
}
$pendingSkillSources = @()
foreach ($kind in @('npc', 'shop')) {
    if ($skillSourceCount.ContainsKey($kind)) {
        $pendingSkillSources += ("{0} {1}" -f $kind, $skillSourceCount[$kind])
    }
}
if ($pendingSkillSources.Count -gt 0) {
    # 等什么**按实际待办生成**：别把已经落地的通道（shop → item 秘籍）挂在告警里，
    # 读的人会照着一条不存在的待办去查（2026-10-04 Q3 落地时点出来的）。
    $pendingReasons = @()
    if ($skillSourceCount.ContainsKey('npc')) { $pendingReasons += '门派对话（`source_id` 指的是门派／地点）' }
    if ($skillSourceCount.ContainsKey('shop')) { $pendingReasons += '「买武学」的价格列' }
    Add-Warning "[可达] 武学来源还没实现的类别：$($pendingSkillSources -join '／')——分别等 $($pendingReasons -join '与')，这几类的武学玩家现在拿不到"
}
# NPC 的「切磋」是**两个数据凑齐**才出现的交互：npc_favor.spar_favor（赢了加多少好感）
# ＋ npc_def.spar_team_id（打哪支队伍）。只填前者不填后者，面板**不会显示切磋按钮**
# （`_add_spar_section` 直接 return），玩家看不到这个交互——而面板自检从不走切磋，两边都绿。
# 2026-10-04 玩家报「NPC 面板缺少切磋」就是这么来的（spar_favor 配了 9 个人，队伍列全空）。
$sparMissing = @()
foreach ($row in $Tables['npc_favor.csv']) {
    if ([int]$row.spar_favor -le 0) { continue }     # ≤0 = 设计上不切磋（黄村／囚徒／沈雁回），不要求队伍
    $npcId = [string]$row.npc_id
    $def = $Tables['npc_def.csv'] | Where-Object { [string]$_.npc_id -eq $npcId } | Select-Object -First 1
    if ($null -eq $def) { continue }                  # 同伴没有 npc_def 行（走 character_base 那条），不在这里管
    if ([string]::IsNullOrWhiteSpace([string]$def.spar_team_id)) { $sparMissing += $npcId }
}
if ($sparMissing.Count -gt 0) {
    Add-Warning "[交互] 这些 NPC 配了 spar_favor（切磋赢了加好感）却没配 npc_def.spar_team_id——面板不会显示「切磋」按钮，玩家看不到这个交互（$($sparMissing -join '、')）"
}
# source_type=start 的武学必须真的挂在某个角色模板的 start_skill_ids 上，否则也是拿不到
$startSkills = @{}
foreach ($row in $Tables['character_base.csv']) {
    foreach ($skill_id in ([string]$row.start_skill_ids -split '\|')) {
        if ($skill_id -ne '') { $startSkills[$skill_id] = $true }
    }
}
$orphanStartSkills = @()
foreach ($row in $Tables['skill_base.csv']) {
    if ($row.source_type -eq 'start' -and -not $startSkills.ContainsKey($row.skill_id)) {
        $orphanStartSkills += [string]$row.skill_id
    }
}
if ($orphanStartSkills.Count -gt 0) {
    Add-Warning "[可达] 这几部武学标着 source_type=start，但没有任何角色模板的 start_skill_ids 带上它们：$($orphanStartSkills -join '／')"
}
Write-Output ("  可达性 : 装备 {0} / 道具 {1} / 武学 {2} 部逐条反查了获取来源（拿不到：装备 {3} / 道具 {4} / 起始武学 {5}）" -f `
    @($Tables['equip_base.csv']).Count, @($Tables['item_base.csv']).Count, @($Tables['skill_base.csv']).Count, `
    $unreachableEquip, $unreachableItem, $orphanStartSkills.Count)

# 5.19b 套装的**每一档**要真的凑得出来（只报警告）
#
# 由来（2026-10-04）：5.19 只查「单件／单部有没有来源」，查不出**组合**——
#   · `set_xuanwei_sword` 的「三招／五招」要 3／5 部玄微剑法，而其中 02／03／04 都是
#     `source_type=npc`（门派对话没实现），**拿得到的只有起手（开局）与点星（巡逻头目掉落）＝ 2 部**；
#   · `set_xuanwei_qi` 的「七格」要 7 点占格，**拿得到的只有引气（1）＋周天（2）＋太清（3）＝ 6 格**
#     （另两部也是 npc）。
# 也就是说：表里配了、`buff_def` 也配了、代码也认，**玩家把能拿的全拿了也亮不起来**——
# 这类「这一档是死内容」以前没有任何地方会报（单件审计看不出来）。
#
# 口径（只算**已经发得出来**的成员）：
#   equip         来源同 5.19（掉落／货架／起始／事件与隐藏奖励／敌人身上／NPC 兑换／代码点名），
#                 并**按槽位**算上限——同槽位最多 `equip_slot_def.max_equip` 件（戒指是 2）
#   skill_active   招式数 = 来源已实现的成员数（`SkillGrant.IMPLEMENTED_SOURCES` 那几类）
#   skill_passive  格数 = 来源已实现的成员 `slot_cost` 之和
#
# **口径的边界（2026-10-04 补）**：这里只算**数据侧**「有没有来源」——**地图侧缺位点它看不见**。
# 真事：`eq_head_01`（前代寨主遗物，黑风套四件档的一员）的唯一来源是三火盆密室 `trig_brazier`，
# 而那三个编号位点还没摆（`tools\check_maps.bat` 的「待补」块每次点名）——按地图算，黑风套的
# 四件档**同样拿不到**，可这里仍然报「凑得齐」。**两边都要看**：本脚本管来源，`check_maps` 管位点。
$skillReachable = @{}
foreach ($row in $Tables['skill_base.csv']) {
    $sid = [string]$row.skill_id
    $kindOfSource = [string]$row.source_type
    if ($kindOfSource -in @('drop', 'hidden', 'item', 'story', 'origin', 'start')) { $skillReachable[$sid] = $true }
    else { $skillReachable[$sid] = $false }     # 其余（npc 门派传授／shop 买武学）还没实现——shop 目前无数据使用
}
$passiveCost = @{}
foreach ($row in $Tables['skill_passive.csv']) { $passiveCost[[string]$row.skill_id] = [int]$row.slot_cost }
$slotCap = @{}
foreach ($row in $Tables['equip_slot_def.csv']) { $slotCap[[string]$row.slot_id] = [int]$row.max_equip }
$equipSlot = @{}
foreach ($row in $Tables['equip_base.csv']) { $equipSlot[[string]$row.equip_id] = [string]$row.slot }
$deadSetTiers = 0
$setTierCount = 0
foreach ($def in $Tables['set_def.csv']) {
    $setId = [string]$def.set_id
    $setKind = [string]$def.set_kind
    $setMembers = @()
    foreach ($m in $Tables['set_member.csv']) { if ([string]$m.set_id -eq $setId) { $setMembers += [string]$m.member_id } }
    if ($setMembers.Count -eq 0) { continue }
    $reachCap = 0
    if ($setKind -eq 'equip') {
        $perSlot = @{}
        foreach ($mid in $setMembers) {
            if (-not $obtainable.ContainsKey($mid) -and -not $srcText.Contains($mid)) { continue }
            $slot = ''
            if ($equipSlot.ContainsKey($mid)) { $slot = $equipSlot[$mid] }
            if (-not $perSlot.ContainsKey($slot)) { $perSlot[$slot] = 0 }
            $perSlot[$slot] = $perSlot[$slot] + 1
        }
        foreach ($slot in $perSlot.Keys) {
            $cap = 1
            if ($slot -ne '' -and $slotCap.ContainsKey($slot)) { $cap = $slotCap[$slot] }
            $reachCap += [Math]::Min($perSlot[$slot], $cap)
        }
    }
    elseif ($setKind -eq 'skill_active') {
        foreach ($mid in $setMembers) { if ($skillReachable.ContainsKey($mid) -and $skillReachable[$mid]) { $reachCap++ } }
    }
    elseif ($setKind -eq 'skill_passive') {
        foreach ($mid in $setMembers) {
            if ($skillReachable.ContainsKey($mid) -and $skillReachable[$mid] -and $passiveCost.ContainsKey($mid)) {
                $reachCap += $passiveCost[$mid]
            }
        }
    }
    foreach ($tier in $Tables['set_bonus.csv']) {
        if ([string]$tier.set_id -ne $setId) { continue }
        $setTierCount++
        $need = [int]$tier.required_count
        if ($reachCap -ge $need) { continue }
        $deadSetTiers++
        $unit = '件'
        if ($setKind -eq 'skill_active') { $unit = '招' }
        elseif ($setKind -eq 'skill_passive') { $unit = '格' }
        Add-Warning "[套装] $setId（$($def.name_cn)）的「$need $unit」档位**现在凑不出来**：已实现的来源加起来最多 $reachCap——玩家把能拿的全拿了也亮不起来（等设计补来源，见 `待策划确认.md` Q3）"
    }
}
Write-Output ("  套装档位 : {0} 个套装的 {1} 档逐档按「已实现的来源」算可达上限（凑不出来：{2} 档）" -f `
    @($Tables['set_def.csv']).Count, $setTierCount, $deadSetTiers)

# 5.20 隐藏内容的线索来源条数（只报警告）
#
# 03_副本_黑风寨.md：「**每条隐藏至少两个线索来源**、线索本可查」——原文是硬要求，理由是
# 「线索必须能被找到，否则就是猜谜」。以前没人数过，**第一次跑就抓到一条**：`trig_chest_all`
# （宝箱全开→隐藏门）只有一条 `clue_source`，而且那一条是「副本完成度界面」这个 UI 面板，
# 不是世界里的线索。只数 `hidden_trigger`：这条纪律出自 03 的隐藏内容章节。
$thinClueTriggers = 0
foreach ($row in $Tables['hidden_trigger.csv']) {
    $clues = @([string]$row.clue_source -split ';' | Where-Object { $_.Trim() -ne '' })
    if ($clues.Count -ge 2) { continue }
    $thinClueTriggers++
    Add-Warning "[线索] 隐藏内容 $($row.trigger_id)（$($row.name_cn)）只有 $($clues.Count) 条线索来源（设计 03：每条隐藏至少两个）——现在写的是「$($row.clue_source)」"
}
Write-Output ("  线索来源 : {0} 条隐藏内容逐条数了 clue_source（不足两条：{1}）" -f `
    @($Tables['hidden_trigger.csv']).Count, $thinClueTriggers)

# 5.21 招式必须有「能用出来」的效果（只报警告）
#
# 两种都算「能用」：
#   * **伤害招式**：`skill_active.is_attack()`（有伤害类型且倍率 > 0）；
#   * **增益招式**（2026-10-03 起）：没有伤害、但 `buff_grant` 里有一条
#     `source_type=skill_active & source_id=本招 & trigger=on_cast` —— 施放一次给自己上 buff。
#     样例就是 `sk_drunk_zuibu`（醉里乾坤·醉步 → 忘忧）。**不需要新列**：增益走的是统一的 buff 通道。
# 两者都不是的招式，玩家能学会、能装上、占一个招式槽，然后什么都不会发生——那才是缺口。
$castGrantSkills = @{}
foreach ($row in $Tables['buff_grant.csv']) {
    if ($row.source_type -eq 'skill_active' -and $row.trigger -eq 'on_cast') { $castGrantSkills[$row.source_id] = $true }
}
$emptySkills = 0
foreach ($row in $Tables['skill_active.csv']) {
    $isAttack = ([string]$row.damage_type -ne '') -and ([double]$row.power_ratio -gt 0.0)
    if ($isAttack) { continue }
    if ($castGrantSkills.ContainsKey($row.skill_id)) { continue }
    $emptySkills++
    Add-Warning "[招式] $($row.skill_id) 在战斗里用不出来（既没有伤害，也没有 on_cast 的增益发放）——它占一个招式槽却不产生任何效果；若是增益类招式，在 buff_grant 里配一条 on_cast"
}
Write-Output ("  招式效果 : {0} 条招式逐条查了「能不能打出来」（用不出来：{1}）" -f `
    @($Tables['skill_active.csv']).Count, $emptySkills)

# 5.22 「特殊效果 id」有没有落点（只报警告）
#
# 两处会填效果 id：`skill_passive.passive_effect`（内功）与 `equip_base.special_effect`（装备）。
# 现在**两者都没有定义表、代码里也没有分支认它们**——填了等于一个没人读的标记：
#   内功：玄微心法·太清／五毒心法·万蛊／药王心法·续命／醉里乾坤·忘忧（都是 ★3–★5、占 2–3 格的重头货）
#   装备：锈月／前代寨主遗物／淬毒指环／药王玉佩
# 客户端这边已经把「内功那条特殊效果暂未生效」写进面板（不显示 id），这条警告负责每次验收都提醒策划：
# 要么给效果开表（或在 skill_active 那样开列），要么把这些 id 从表里去掉。
$pendingEffects = @{}
foreach ($row in $Tables['skill_passive.csv']) {
    $effect = [string]$row.passive_effect
    if ($effect -ne '') {
        if (-not $pendingEffects.ContainsKey('内功被动')) { $pendingEffects['内功被动'] = @() }
        $pendingEffects['内功被动'] += "$($row.skill_id)=$effect"
    }
}
foreach ($row in $Tables['equip_base.csv']) {
    $effect = [string]$row.special_effect
    if ($effect -ne '') {
        if (-not $pendingEffects.ContainsKey('装备特殊')) { $pendingEffects['装备特殊'] = @() }
        $pendingEffects['装备特殊'] += "$($row.equip_id)=$effect"
    }
}
$pendingEffectTotal = 0
foreach ($kind in $pendingEffects.Keys) {
    $list = $pendingEffects[$kind]
    $pendingEffectTotal += $list.Count
    Add-Warning ("[效果] {0} 有 {1} 条填了特殊效果 id 但没有定义表、代码里也没有落点：{2}——要么补效果表/列，要么把 id 去掉" -f `
        $kind, $list.Count, ($list -join '／'))
}
Write-Output ("  特殊效果 : 内功 {0} 条 + 装备 {1} 条逐条查了效果 id 有没有落点（悬空：{2}）" -f `
    @($Tables['skill_passive.csv']).Count, @($Tables['equip_base.csv']).Count, $pendingEffectTotal)

# 5.24 背包分类能不能覆盖每种 item_type（只报警告）
#
# 背包的筛选格子是固定的：全部／材料／消耗品／钥匙／残页／装备（`character_screen.FILTERS`）。
# 数据里出现这四类之外的 `item_type`，那类物品就只能靠「全部」翻。现例：`item_pickaxe` 的
# `item_type=tool`，而设计 05 把它列在「钥匙道具」里（它也确实是 `is_key_item=1`）。
# 代码侧已经把「钥匙」改成按 `is_key_item` 判（设计语义），所以铁镐现在**筛得到**；
# 这条警告留给策决定表怎么归：把 `item_type` 改成 key，或者给背包加一格「工具」。
$filterTypes = @('material', 'consumable', 'key', 'skillbook')
$uncoveredTypes = @()
foreach ($row in $Tables['item_base.csv']) {
    $t = [string]$row.item_type
    if ($t -eq 'currency' -or $t -eq 'equip') { continue }   # 铜钱是货币计数器、装备走装备页
    if (($filterTypes -notcontains $t) -and ($uncoveredTypes -notcontains $t)) { $uncoveredTypes += $t }
}
if ($uncoveredTypes.Count -gt 0) {
    Add-Warning ("[分类] item_base 里这些 item_type 没有对应的背包筛选格子：{0}——要么并进上面四类，要么给背包加一格" -f `
        ($uncoveredTypes -join '／'))
}
Write-Output ("  背包分类 : {0} 种 item_type 逐种查了筛选格子（没落格的：{1}）" -f `
    @(Get-Col $Tables['item_base.csv'] 'item_type' | Select-Object -Unique).Count, $uncoveredTypes.Count)

# 5.25 drop_table.item_type 与所指物品的实际类型一致吗（只报警告）
#
# 这一列**运行时没人读**（`drop_resolver` 只是把它塞进返回条目里，真正入账看的是 item_id：
# `BattleReward.grant_item` 先判 item_money 再查 equip_base）。也就是说表里写错了也**不会有任何反应**，
# 但读表的人会以为它有用。所以这里至少保证它**不自相矛盾**：
#   实际类型 = item_base[item_id].item_type（`money` 与 `currency` 是同一件事的两种写法，按别名处理——14 行
#   掉落钱的槽写的都是 money、物品表里叫 currency，这条口径差已记进当前状态的缺口表请设计统一）；
#   item_id 在 equip_base 里 → 视为 equip。
$itemTypeById = @{}
foreach ($r in $Tables['item_base.csv']) { $itemTypeById[[string]$r.item_id] = [string]$r.item_type }
$equipIdsForDrop = @{}
foreach ($r in $Tables['equip_base.csv']) { $equipIdsForDrop[[string]$r.equip_id] = 'equip' }
$dropTypeMismatch = 0
foreach ($row in $Tables['drop_table.csv']) {
    $itemId = [string]$row.item_id
    $actual = ''
    if ($itemTypeById.ContainsKey($itemId)) { $actual = $itemTypeById[$itemId] }
    elseif ($equipIdsForDrop.ContainsKey($itemId)) { $actual = 'equip' }
    else { continue }                      # 查不到物品的情况由 5.5 去报，这里不管
    $declared = [string]$row.item_type
    if ($declared -eq $actual) { continue }
    if ($declared -eq 'money' -and $actual -eq 'currency') { continue }   # 已知别名：掉落钱
    $dropTypeMismatch++
    Add-Warning "[掉落] $($row.drop_row_id) 的 item_type 写的是 $declared，但 $itemId 实际是 $actual（这一列运行时没人读，写错了也不会有人喊）"
}
Write-Output ("  掉落类型 : {0} 个槽逐条比了「表里写的 item_type」与物品实际类型（不一致：{1}，money／currency 按别名算一致）" -f `
    @($Tables['drop_table.csv']).Count, $dropTypeMismatch)

# 5.26 04_战斗与伤害.md 里写死的两张表，与数据对一遍（只报警告）
#
# 04 有两张「定死」的表：**伤害类型表**（7 行 × 可暴击／可闪避／吃外防／吃内防／结算时机／持续／层数上限）
# 与**系别相克矩阵**（3×3）。它们以前只活在文档里，代码与数据各一套、没人比对——
# **第一次跑就抓到一条**：`dot_internal`（内伤）文档写「吃内防 否」，数据却是 `use_def_qi=1`，
# 也就是内伤每一跳都会被目标的**内功防御**削减，而 `damage_resolver` 的注释同样写着「内伤无视防御」。
# 报**警告**不报错误：改哪一边是设计侧的内容决定（按文档把那一格改成 0 是一个字的活）。
$doc04 = Join-Path $Root 'docs/design/04_战斗与伤害.md'
if (Test-Path -LiteralPath $doc04) {
    $dmgDoc = @{}
    $matrixDoc = @{}
    $doc04Lines = Get-Content -LiteralPath $doc04 -Encoding UTF8
    foreach ($line in $doc04Lines) {
        # 伤害类型 id 有两族：`dmg_*`（直伤）与 `dot_*`（持续）——第一版只写了 `dmg_`，
        # 于是 7 行里只比了 3 行、还漏掉了下面那条真不一致（探针当场发现）
        if ($line -match '^\|\s*((?:dmg|dot)_[a-z_]+)\s*\|') {
            $cells = $line.Split('|') | ForEach-Object { $_.Trim() }
            # cells: [0]='' [1]=type_id [2]=名称 [3]=结算 [4]=可暴击 [5]=可闪避 [6]=吃外防 [7]=吃内防 [8]=结算时机 [9]=持续 [10]=层数上限
            if ($cells.Count -ge 11) {
                $dmgDoc[$cells[1]] = @{
                    'can_crit'     = ($(if ($cells[4] -eq '是') { 1 } else { 0 }))
                    'can_dodge'    = ($(if ($cells[5] -eq '是') { 1 } else { 0 }))
                    'use_def_phys' = ($(if ($cells[6] -eq '是') { 1 } else { 0 }))
                    'use_def_qi'   = ($(if ($cells[7] -eq '是') { 1 } else { 0 }))
                    'tick_timing'  = ($(if ($cells[8] -eq '回合末') { 'turn_end' } else { 'immediate' }))
                    'base_duration' = ($(if ($cells[9] -eq '—') { 0 } else { [int]$cells[9] }))
                    'max_stack'     = ($(if ($cells[10] -eq '—') { 0 } else { [int]$cells[10] }))
                }
            }
            continue
        }
        if ($line -match '^\|\s*(外功|内功|奇诡)\s*\|') {
            $cells = $line.Split('|') | ForEach-Object { $_.Trim() }
            if ($cells.Count -ge 5) {
                $atk = @{ '外功' = 'external'; '内功' = 'internal'; '奇诡' = 'odd' }[$cells[1]]
                $matrixDoc[$atk] = @([double]$cells[2], [double]$cells[3], [double]$cells[4])
            }
        }
    }
    $dmgMismatch = 0
    $dmgCompared = 0
    foreach ($row in $Tables['damage_type.csv']) {
        $tid = [string]$row.type_id
        if (-not $dmgDoc.ContainsKey($tid)) { continue }
        $dmgCompared++
        $want = $dmgDoc[$tid]
        foreach ($col in @('can_crit', 'can_dodge', 'use_def_phys', 'use_def_qi')) {
            if ([int]$row.$col -ne [int]$want[$col]) {
                $dmgMismatch++
                Add-Warning "[伤害类型] $tid.$col = $($row.$col)，但 04_战斗与伤害.md 的伤害类型表写的是 $(if ($want[$col] -eq 1) { '是' } else { '否' })（改哪边由设计定）"
            }
        }
        foreach ($col in @('tick_timing')) {
            if ([string]$row.$col -ne [string]$want[$col]) {
                $dmgMismatch++
                Add-Warning "[伤害类型] $tid.$col = $($row.$col)，但 04 那张表写的是 $($want[$col])"
            }
        }
        foreach ($col in @('base_duration', 'max_stack')) {
            if ([int]$row.$col -ne [int]$want[$col]) {
                $dmgMismatch++
                Add-Warning "[伤害类型] $tid.$col = $($row.$col)，但 04 那张表写的是 $($want[$col])"
            }
        }
    }
    $matrixMismatch = 0
    $matrixCompared = 0
    $elementOrder = @('external', 'internal', 'odd')
    foreach ($row in $Tables['element_counter.csv']) {
        $atk = [string]$row.element_atk
        $def = [string]$row.element_def
        if (-not $matrixDoc.ContainsKey($atk)) { continue }
        $colIndex = [array]::IndexOf($elementOrder, $def)
        if ($colIndex -lt 0) { continue }
        $matrixCompared++
        if ([math]::Abs([double]$row.multiplier - [double]$matrixDoc[$atk][$colIndex]) -gt 0.0001) {
            $matrixMismatch++
            Add-Warning "[相克] $atk→$def = $($row.multiplier)，但 04 的系别相克表写的是 $($matrixDoc[$atk][$colIndex])"
        }
    }
    Assert-DocCoverage -Label '04 的伤害类型表' -Compared $dmgCompared -Available @($Tables['damage_type.csv']).Count
    Assert-DocCoverage -Label '04 的系别相克表' -Compared $matrixCompared -Available @($Tables['element_counter.csv']).Count
    Write-Output ("  文档定表 : 伤害类型逐格比了 {0}/{1} 行×7 列（不一致 {2}）；系别相克 {3}/{4} 格（不一致 {5}）" -f `
        $dmgCompared, @($Tables['damage_type.csv']).Count, $dmgMismatch, `
        $matrixCompared, @($Tables['element_counter.csv']).Count, $matrixMismatch)
}

# 5.27 02_地图与明雷.md 的「第一章明雷分布」表与 roaming_spawn 对一遍（只报警告）
#
# 这张表直接决定玩家在地图上遇到什么、看到什么威胁色，可比的东西很多元：
#   区域（中文名 → `map_region.name_cn`）／明雷（×N = 该区域该队伍的**位点数量**）／
#   行为（游荡·巡逻·驻守·追击·沉睡 → `roaming_spawn.behavior`）／威胁色（绿·黄·红·紫 → `enemy_team.threat_tag`）／
#   队伍（可能是两个，用「 / 」分隔）。
# 报**警告**不报错误：不一致可能是文档没跟上（07 的逐位点表就列了野猪群），也可能是数据写错，
# 改哪边由设计定。当前抓到 3 处（见当前状态缺口 #22）。
$regionNameToId = @{}
foreach ($r in $Tables['map_region.csv']) { $regionNameToId[[string]$r.name_cn] = [string]$r.node_id }
# 02 的分布表用的是自己的口语叫法，跟 `map_region.name_cn` 不逐字相同——这里登记别名（写清出处）
$regionNameAlias = @{ '黑风寨外围' = 'n_heifengzhai' }   # 02「黑风寨外围」＝ map_region『黑风寨』
foreach ($name in $regionNameAlias.Keys) { $regionNameToId[$name] = $regionNameAlias[$name] }
$teamThreat = @{}
foreach ($r in $Tables['enemy_team.csv']) { $teamThreat[[string]$r.team_id] = [string]$r.threat_tag }
$behaviorWord = @{ '游荡' = 'wander'; '巡逻' = 'patrol'; '驻守' = 'idle'; '追击' = 'chase'; '沉睡' = 'sleep' }
$threatWord = @{ '绿' = 'green'; '黄' = 'yellow'; '红' = 'red'; '紫' = 'purple' }
$spawnsByRegionTeam = @{}
foreach ($r in $Tables['roaming_spawn.csv']) {
    $key = "$([string]$r.region_id)|$([string]$r.team_id)"
    if (-not $spawnsByRegionTeam.ContainsKey($key)) { $spawnsByRegionTeam[$key] = @() }
    $spawnsByRegionTeam[$key] += $r
}
$doc02 = Join-Path $Root 'docs/design/02_地图与明雷.md'
$spawnMismatch = 0
$docSeen = @{}
if (Test-Path -LiteralPath $doc02) {
    $currentRegion = ''
    foreach ($line in (Get-Content -LiteralPath $doc02 -Encoding UTF8)) {
        if (-not $line.StartsWith('|')) { continue }
        $cells = @($line.Split('|') | ForEach-Object { $_.Trim() })
        if ($cells.Count -lt 7) { continue }
        $teamCell = [string]$cells[5]
        if (-not $teamCell.StartsWith('team_')) { continue }   # 只认「队伍」列写的是 team_xxx 的行
        if ($cells[1] -ne '') { $currentRegion = [string]$cells[1] }
        if (-not $regionNameToId.ContainsKey($currentRegion)) {
            $spawnMismatch++
            Add-Warning "[明雷] 02 的分布表里有认不出的区域「$currentRegion」"
            continue
        }
        $regionId = $regionNameToId[$currentRegion]
        $wantCount = 1
        if ($cells[2] -match '×(\d+)') { $wantCount = [int]$Matches[1] }
        $wantBehavior = ''
        if ($behaviorWord.ContainsKey([string]$cells[3])) { $wantBehavior = $behaviorWord[[string]$cells[3]] }
        $wantThreat = ''
        if ($threatWord.ContainsKey([string]$cells[4])) { $wantThreat = $threatWord[[string]$cells[4]] }
        $teams = @($teamCell -split '/' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
        $foundCount = 0
        foreach ($team in $teams) {
            $key = "$regionId|$team"
            if ($spawnsByRegionTeam.ContainsKey($key)) {
                $foundCount += $spawnsByRegionTeam[$key].Count
                $docSeen[$key] = $true
                foreach ($spawn in $spawnsByRegionTeam[$key]) {
                    if ($wantBehavior -ne '' -and [string]$spawn.behavior -ne $wantBehavior) {
                        $spawnMismatch++
                        Add-Warning "[明雷] 02 说 $team 的行为是「$($cells[3])」，数据里 $($spawn.spawn_id) 是 $($spawn.behavior)"
                    }
                }
                if ($wantThreat -ne '' -and $teamThreat.ContainsKey($team) -and $teamThreat[$team] -ne $wantThreat) {
                    $spawnMismatch++
                    Add-Warning "[明雷] 02 说 $team 的威胁色是「$($cells[4])」，数据里 enemy_team 写的是 $($teamThreat[$team])"
                }
                elseif ($wantThreat -eq '' -and $teamThreat.ContainsKey($team)) {
                    $spawnMismatch++
                    Add-Warning "[明雷] 02 给 $team 的威胁色是「—」（中立），数据里 enemy_team 写的是 $($teamThreat[$team])——要不要配色请设计定"
                }
            }
        }
        if ($foundCount -ne $wantCount) {
            $spawnMismatch++
            Add-Warning "[明雷] 02 写「$currentRegion／$($cells[2])」= $wantCount 个位点（队伍 $($teams -join '、')），数据里是 $foundCount 个"
        }
    }
    foreach ($key in $spawnsByRegionTeam.Keys) {
        if ($docSeen.ContainsKey($key)) { continue }
        $spawnMismatch++
        Add-Warning "[明雷] 数据里有「$key」这个区域／队伍的明雷，但 02 的分布表没列（07 的逐位点表可能列了）"
    }
    Write-Output ("  明雷分布 : 02 的分布表逐行对 roaming_spawn（位点数／行为／威胁色／漏列），不一致 {0} 处" -f $spawnMismatch)
}

# 5.28 03_副本_黑风寨.md 的「各层房间流程」表与 dungeon_room 对一遍（只报警告）
#
# 三张表（第一层 8 间 / 第二层 6 间 / 第三层 4 间）给的是房间名／类型／内容／出口，可比的有：
#   ① 房间集合：03 的 18 间 ↔ 表里 19 行（多出来的 `hf1_secret` 暗格是「宝箱全开」的奖励房间，
#      03 把它写在「隐藏内容」那一节而不是逐层流程里——这条例外写在代码里并注明出处）；
#   ② 类型词 → `room_type`（入口/战斗/宝箱/陷阱/隐藏/剧情/精英/Boss）；
#   ③ 出口：按**房间名**匹配（03 用的「后寨」是层名，靠「数据房名以它开头」认到 后寨门／后寨大堂）；
#      火盆密室多一个通往暗格的出口属于 ① 那条例外。
#      **反向边不算「03 没写」**：07 §九 第 3 条要求出口对称，表里必然比 03 的「前进方向」多出反向边
#      （例 前院→寨门，出自 03 的「寨门」那行）——只要对向那一行在 03 里写了回来就放过；
#   ④ 内容里**只写了一种敌人**且带 ×N 时，N 应等于该房间队伍的总人数（写了两种就跳过，不做模糊匹配）；
#   ⑤ 出口**单向声明**汇总：`dungeon_room.exit_rooms` 里 a→b 有、b→a 没有的条数（地图上物理是双向的，
#      表侧要补成对称；verify_maps 也从地图那头报同一件事，这里让它在验收输出里也看得见）。
# 报**警告**：改文档还是改表由设计定。**2026-10-04 已把 `exit_rooms` 补成对称（单向 0 条，决策 326）**，
# 只剩 #11 那两处人数（前院 3→表里 2、毒堂 1→表里 3）等设计定夺。
$roomNameToId = @{}
$roomById = @{}
foreach ($r in $Tables['dungeon_room.csv']) {
    if ([string]$r.scene_id -ne 'scene_heifengzhai') { continue }
    $roomNameToId[[string]$r.room_name] = [string]$r.room_id
    $roomById[[string]$r.room_id] = $r
}
$roomTypeWord = @{
    '入口' = 'entrance'; '战斗' = 'battle'; '宝箱' = 'treasure'; '陷阱' = 'trap';
    '隐藏' = 'secret'; '剧情' = 'story'; '精英' = 'elite'; 'Boss' = 'boss'
}
$roomDoc = Join-Path $Root 'docs/design/03_副本_黑风寨.md'
$roomMismatch = 0
$docRooms = @{}
# ③ 的「反向边」判定要用**对向那一行**在 03 里写了什么出口；而单向扫描读到「前院」时，
# 很可能还没读到「寨门」那一行（顺序不保证），所以要先把 03 每行的出口列预扫成
# roomId → 出口文字，主循环再按顺序无关地取用（决策 326）。
$docExitText = @{}
if (Test-Path -LiteralPath $roomDoc) {
    foreach ($preLine in (Get-Content -LiteralPath $roomDoc -Encoding UTF8)) {
        if (-not $preLine.StartsWith('|')) { continue }
        $preCells = @($preLine.Split('|') | ForEach-Object { $_.Trim() })
        if ($preCells.Count -lt 6) { continue }
        if (-not $roomTypeWord.ContainsKey([string]$preCells[2])) { continue }
        if (-not $roomNameToId.ContainsKey([string]$preCells[1])) { continue }
        $docExitText[$roomNameToId[[string]$preCells[1]]] = [string]$preCells[4]
    }
}
if (Test-Path -LiteralPath $roomDoc) {
    foreach ($line in (Get-Content -LiteralPath $roomDoc -Encoding UTF8)) {
        if (-not $line.StartsWith('|')) { continue }
        $cells = @($line.Split('|') | ForEach-Object { $_.Trim() })
        if ($cells.Count -lt 6) { continue }
        $roomName = [string]$cells[1]
        $typeWord = [string]$cells[2]
        if (-not $roomTypeWord.ContainsKey($typeWord)) { continue }
        if (-not $roomNameToId.ContainsKey($roomName)) {
            $roomMismatch++
            Add-Warning "[副本] 03 的房间表里有表里查不到的房名「$roomName」"
            continue
        }
        $roomId = $roomNameToId[$roomName]
        $roomRow = $roomById[$roomId]
        $docRooms[$roomId] = $true
        if ([string]$roomRow.room_type -ne $roomTypeWord[$typeWord]) {
            $roomMismatch++
            Add-Warning "[副本] 03 说「$roomName」是$typeWord，表里 room_type=$($roomRow.room_type)"
        }
        # ③ 出口（按名匹配；空 / 「—」 / 「结束」都算没有出口）
        $exitText = [string]$cells[4]
        $wantExits = @{}
        $wantGroups = @()
        foreach ($name in ($exitText -split '、')) {
            $clean = $name.Trim()
            if ($clean -eq '' -or $clean -eq '—' -or $clean -eq '结束') { continue }
            # 03 会用**层名**当出口（「后寨」），它同时以 后寨门／后寨大堂 开头——所以一个名可能对应多个房间：
            # 这种歧义名只要求「其中任意一个在数据里」即可（第一版要求全部命中，冒出 3 条假警告）。
            $candidates = @()
            foreach ($dataName in $roomNameToId.Keys) {
                if ($dataName -eq $clean -or $dataName.StartsWith($clean)) { $candidates += $roomNameToId[$dataName] }
            }
            if ($candidates.Count -eq 0) { continue }
            if ($candidates.Count -eq 1) { $wantExits[$candidates[0]] = $true } else { $wantGroups += ,$candidates }
        }
        $actualExits = @([string]$roomRow.exit_rooms -split '\|' | Where-Object { $_ -ne '' })
        # 歧义名（层名）先把候选全展开进「文档说该能去的地方」，再双向比：
        # 顺序写反过一次（先比对、后展开），于是 后寨门 被误判成「03 没写」——同一类坑在决策 87 也踩过。
        foreach ($candidates in $wantGroups) { foreach ($candidate in $candidates) { $wantExits[$candidate] = $true } }
        foreach ($exit in $actualExits) {
            if ($exit -eq 'hf1_secret') { continue }     # ① 的例外：暗格
            if ($wantExits.ContainsKey($exit)) { continue }
            # **反向出口不算「03 没写」**：07 §九 第 3 条要求「出口对称：A 能到 B，B 也能回 A」，
            # 所以表里必然比 03 的「前进方向」多出反向边（例：前院 → 寨门，出自 03 的「寨门」那行）。
            # 只要**对向那一行**在自己那格里写了回来，这条就是那条反向边，跳过。
            # （2026-10-04：把表补成对称之后，这一条第一次跑就冒出 15 条假警告——补对称本身是对的，
            #   错的是这里按「03 没写就不许有」在比。判定改用预扫好的 $docExitText，与读取顺序无关。）
            $backText = ''
            if ($docExitText.ContainsKey($exit)) { $backText = [string]$docExitText[$exit] }
            $wantedBack = $false
            foreach ($name in ($backText -split '、')) {
                $cleanBack = $name.Trim()
                if ($cleanBack -eq '' -or $cleanBack -eq '—' -or $cleanBack -eq '结束') { continue }
                foreach ($backName in $roomNameToId.Keys) {
                    if (($backName -eq $cleanBack -or $backName.StartsWith($cleanBack)) `
                            -and $roomNameToId[$backName] -eq $roomRow.room_id) {
                        $wantedBack = $true
                    }
                }
            }
            if ($wantedBack) { continue }
            $roomMismatch++
            Add-Warning "[副本] 表里 $($roomRow.room_id) 能通往 $exit，但 03 的出口列没写（03 写的是「$exitText」）"
        }
        foreach ($candidates in $wantGroups) {
            $hit = $false
            foreach ($candidate in $candidates) {
                if ($actualExits -contains $candidate) { $hit = $true }
            }
            if (-not $hit) {
                $roomMismatch++
                Add-Warning "[副本] 03 说「$roomName」能通往「$($candidates -join '／')」中的一处，但表里 exit_rooms（$($roomRow.exit_rooms)）里一处都没有"
            }
        }
        # ④ 只写一种敌人时的 ×N 与队伍总人数
        $multipliers = [regex]::Matches([string]$cells[3], '×(\d+)')
        if ($multipliers.Count -eq 1 -and [string]$roomRow.enemy_team -ne '') {
            foreach ($team in $Tables['enemy_team.csv']) {
                if ([string]$team.team_id -ne [string]$roomRow.enemy_team) { continue }
                $total = 0
                foreach ($m in ([string]$team.members -split ';')) {
                    if ($m -eq '') { continue }
                    $parts = $m -split ':'
                    $total += $(if ($parts.Count -ge 2) { [int]$parts[1] } else { 1 })
                }
                $wantCount = [int]$multipliers[0].Groups[1].Value
                if ($total -ne $wantCount) {
                    $roomMismatch++
                    Add-Warning "[副本] 03 说「$roomName」有 $wantCount 个敌人，表里 $($roomRow.enemy_team) 一共 $total 个"
                }
            }
        }
    }
    # ① 反向：表里有、03 没列（暗格除外）
    foreach ($roomId in $roomById.Keys) {
        if ($roomId -eq 'hf1_secret') { continue }
        if ($docRooms.ContainsKey($roomId)) { continue }
        $roomMismatch++
        Add-Warning "[副本] 表里有房间 $roomId（$($roomById[$roomId].room_name)），但 03 的流程表没列"
    }
    # ⑤ 单向出口汇总
    $oneWay = 0
    foreach ($roomId in $roomById.Keys) {
        foreach ($exit in ([string]$roomById[$roomId].exit_rooms -split '\|')) {
            if ($exit -eq '' -or -not $roomById.ContainsKey($exit)) { continue }
            if (([string]$roomById[$exit].exit_rooms -split '\|') -notcontains $roomId) { $oneWay++ }
        }
    }
    if ($oneWay -gt 0) {
        Add-Warning "[副本] dungeon_room 里有 $oneWay 条出口只声明了单向（地图上物理是双向的，表侧要补对称；verify_maps 从地图那头也报同一件事）"
    }
    Write-Output ("  副本房间表 : 03 的 18 间逐间对 dungeon_room（类型／出口／单一种类的 ×N）＋单向出口汇总，不一致 {0} 处＋单向 {1} 条" -f $roomMismatch, $oneWay)
}

# 5.29 01_角色系统.md 的「后续模板的扩充分向」表里，武器必须是真有的四种（只报警告）
#
# 05 写死「只有四种：剑、拳、刀、枪」，而 01 的扩充分向表写着「药王谷弟子→棍」「江湖游医→暗器」——
# 这两种在 `weapon_type_def` 里根本没有；而同一篇 01 的「备选模板数据」用的却是
# blade／fist／sword／fist（都在四种里），**文档自己前后不一致**。
# 报警告：要么把扩充分向改成四种之一，要么先扩武器类型表（那是设计侧的内容决定）。
$weaponNames = @(Get-Col $Tables['weapon_type_def.csv'] 'name_cn')
$directionDoc = Join-Path $Root 'docs/design/01_角色系统.md'
$directionChecked = 0
$directionBad = 0
if (Test-Path -LiteralPath $directionDoc) {
    foreach ($line in (Get-Content -LiteralPath $directionDoc -Encoding UTF8)) {
        if (-not $line.StartsWith('|')) { continue }
        $cells = @($line.Split('|') | ForEach-Object { $_.Trim() })
        # 「| 镖局武夫 | 力・体 | 刀 | 力、生存 |」——第 2 格带「・」（属性倾向），第 3 格才是武器
        if ($cells.Count -lt 6) { continue }
        if ($cells[1] -eq '' -or $cells[2] -notmatch '・' -or $cells[3] -eq '') { continue }
        $directionChecked++
        if ($weaponNames -notcontains $cells[3]) {
            $directionBad++
            Add-Warning "[职业方向] 01 的扩充分向表给「$($cells[1])」配的武器是「$($cells[3])」，但 weapon_type_def 里只有 $($weaponNames -join '／')"
        }
    }
    Assert-DocCoverage -Label '01 的扩充分向表' -Compared $directionChecked
    Write-Output ("  职业方向 : 01 的扩充分向表比了 {0} 行武器（不在那四种里的：{1}）" -f $directionChecked, $directionBad)
}

# 5.30 01 的「难度刻度」：event_check.difficulty 要落在对应来源的刻度里（只报警告）
#
# 01_角色系统.md 写着「两套来源的难度刻度不同，**配置时不要混用**」：
#   `attr:` 判定值开局 5–10、后期专精可到 30+ → 难度刻度 **5 ~ 25**
#   `skill:` 判定值开局 0–5、满级约 18          → 难度刻度 **2 ~ 12**
# 越界未必是错（可能是刻意的特例），所以只提醒——12 条判定今天全部在刻度内。
$scales = @{ 'attr' = @(5.0, 25.0); 'skill' = @(2.0, 12.0) }
$scaleChecked = 0
$scaleBad = 0
foreach ($row in $Tables['event_check.csv']) {
    $parts = ([string]$row.check_source) -split ':'
    if ($parts.Count -lt 2 -or -not $scales.ContainsKey($parts[0])) { continue }
    $range = $scales[$parts[0]]
    $difficulty = [double]$row.difficulty
    $scaleChecked++
    if ($difficulty -lt $range[0] -or $difficulty -gt $range[1]) {
        $scaleBad++
        Add-Warning "[刻度] $($row.check_id) 的难度 $difficulty 不在 01 写的「$($parts[0])」刻度 $($range[0])~$($range[1]) 里（越界未必是错，请确认）"
    }
}
Assert-DocCoverage -Label '01 的难度刻度与 event_check' -Compared $scaleChecked -Available @($Tables['event_check.csv']).Count
Write-Output ("  判定刻度 : {0} 条事件判定逐条比了 01 的难度刻度（越界：{1}）" -f $scaleChecked, $scaleBad)

# 5.31 装备／词条／内功给「派生数值」的加成，**单条就超过该数值自己的上限**时，超出部分会被静默夹掉。
#
# 上限在哪一层：`AttributeCalculator` 最后按 `stat_def.min_value/max_value` 夹最终值；
# `DamageResolver` 对穿透／格挡／减伤另有一层 `_stat_cap`，取的**还是** `stat_def.max_value`
# （同一处真相）。所以「单条加成 > 上限」的那部分永远不生效：例如 `dmg_reduction` 上限 0.6，
# 某件装备写 0.8，玩家看到描述是 0.8、实际只有 0.6，而构建期与验收都不吭声。
# 三条来源一次查完：equip_base 的 `bonus_<stat>` 列、affix_pool（`stat:` 目标）的区间两端、
# skill_passive_stat（`stat:` 目标）的单值。报**警告**：可能是写错，也可能是刻意的溢出设计。
#
# 口径注意（这一版踩过）：**只比上限，不比下限**。`stat_def.min_value` 是「最终值」的下界
# （hp_max 是 1、res_internal 是 -0.5、其余多为 0），而加成写 0 是「这条不给加成」的正常写法——
# 第一版把 min 一起比，当场误报 18 条 `bonus_hp_max = 0`。`max_value` 为空/0 = 无上限，跳过。
$statCap = @{}
foreach ($s in $Tables['stat_def.csv']) {
    $cap = 0.0
    if ([string]$s.max_value -ne '') { $cap = [double]$s.max_value }
    $statCap[[string]$s.stat_id] = $cap
}
$overCap = 0
$capChecked = 0
foreach ($row in $Tables['equip_base.csv']) {
    foreach ($p in $row.PSObject.Properties) {
        if (-not $p.Name.StartsWith('bonus_')) { continue }
        $statId = $p.Name.Substring(6)
        if (-not $statCap.ContainsKey($statId)) { continue }
        $cap = [double]$statCap[$statId]
        if ($cap -le 0 -or [string]$p.Value -eq '') { continue }
        $capChecked++
        if ([double]$p.Value -gt $cap) {
            $overCap++
            Add-Warning "[加成上限] equip_base.csv $($row.equip_id).$($p.Name) = $($p.Value)，超过 stat_def.$statId 的上限 $cap——超出的部分运行期会被夹掉"
        }
    }
}
foreach ($row in $Tables['affix_pool.csv']) {
    $target = [string]$row.target
    if (-not $target.StartsWith('stat:')) { continue }
    $statId = $target.Substring(5)
    if (-not $statCap.ContainsKey($statId)) { continue }
    $cap = [double]$statCap[$statId]
    if ($cap -le 0) { continue }
    foreach ($col in @('value_min', 'value_max')) {
        if ([string]$row.$col -eq '') { continue }
        $capChecked++
        if ([double]$row.$col -gt $cap) {
            $overCap++
            Add-Warning "[加成上限] affix_pool.csv $($row.affix_id).$col = $($row.$col)，超过 stat_def.$statId 的上限 $cap——超出的部分运行期会被夹掉"
        }
    }
}
foreach ($row in $Tables['skill_passive_stat.csv']) {
    $target = [string]$row.target
    if (-not $target.StartsWith('stat:')) { continue }
    $statId = $target.Substring(5)
    if (-not $statCap.ContainsKey($statId)) { continue }
    $cap = [double]$statCap[$statId]
    if ($cap -le 0 -or [string]$row.value -eq '') { continue }
    $capChecked++
    if ([double]$row.value -gt $cap) {
        $overCap++
        Add-Warning "[加成上限] skill_passive_stat.csv $($row.skill_id)（$target）.value = $($row.value)，超过 stat_def.$statId 的上限 $cap——超出的部分运行期会被夹掉"
    }
}
Write-Output ("  加成上限 : 装备／词条／内功被动逐条比 stat_def 上限（比了 {0} 条，超出的：{1}）" -f $capChecked, $overCap)

# 5.16 增益减益与套装
$buffIds = @(Get-Col $Tables['buff_def.csv'] 'buff_id')
$buffStatByBuff = @{}
foreach ($row in $Tables['buff_stat.csv']) {
    if (-not $buffStatByBuff.ContainsKey($row.buff_id)) { $buffStatByBuff[$row.buff_id] = 0 }
    $buffStatByBuff[$row.buff_id]++
    if ($row.target.StartsWith('attr:')) {
        $t = $row.target.Substring(5)
        if ((Get-Col $Tables['attribute_def.csv'] 'attr_id') -notcontains $t) {
            Add-Error "[引用] buff_stat.csv $($row.buff_id).target='$t' 不是合法属性"
        }
    }
    elseif ($row.target.StartsWith('stat:')) {
        $t = $row.target.Substring(5)
        if ((Get-Col $Tables['stat_def.csv'] 'stat_id') -notcontains $t) {
            Add-Error "[引用] buff_stat.csv $($row.buff_id).target='$t' 不是合法派生数值"
        }
    }
    else { Add-Error "[格式] buff_stat.csv $($row.buff_id).target 缺少 attr:/stat: 前缀" }
}

foreach ($row in $Tables['buff_def.csv']) {
    if (@('0', '1') -notcontains $row.is_debuff) { Add-Error "[枚举] buff_def.csv $($row.buff_id).is_debuff='$($row.is_debuff)' 应为 0 或 1" }
    if (@('battle', 'field') -notcontains $row.scope) { Add-Error "[枚举] buff_def.csv $($row.buff_id).scope='$($row.scope)' 应为 battle 或 field" }
    if (@('refresh', 'stack', 'unique') -notcontains $row.stack_rule) { Add-Error "[枚举] buff_def.csv $($row.buff_id).stack_rule='$($row.stack_rule)' 不合法" }
    if (@('stat', 'computed', 'special') -notcontains $row.effect_kind) { Add-Error "[枚举] buff_def.csv $($row.buff_id).effect_kind='$($row.effect_kind)' 不合法" }
    # 数值型 buff 必须有加成行，否则装上等于没装
    if ($row.effect_kind -eq 'stat' -and -not $buffStatByBuff.ContainsKey($row.buff_id)) {
        Add-Error "[完整] buff_def.csv $($row.buff_id) 是数值型（effect_kind=stat），但 buff_stat.csv 里一条加成都没有"
    }
    $durValue = 0
    if (-not [int]::TryParse($row.duration, [ref]$durValue)) {
        Add-Error "[数值] buff_def.csv $($row.buff_id).duration 不是整数"
    }
    elseif ($durValue -lt 0 -and $durValue -ne -1) {
        Add-Error "[数值] buff_def.csv $($row.buff_id).duration=$durValue 只允许 -1（整场）或非负"
    }
    $stackMax = 0
    if ($row.stack_rule -eq 'stack') {
        if (-not [int]::TryParse($row.max_stack, [ref]$stackMax) -or $stackMax -lt 2) {
            Add-Error "[数值] buff_def.csv $($row.buff_id) 是可叠层 buff，max_stack 必须 ≥ 2"
        }
    }
}

# 来源 → buff：source_id 必须真实存在，且每部内功都要有运功形态
$buffSource = @{
    'skill_active'  = (Get-Col $Tables['skill_active.csv'] 'skill_id')
    'skill_passive' = (Get-Col $Tables['skill_passive.csv'] 'skill_id')
    'equip'         = (Get-Col $Tables['equip_base.csv'] 'equip_id')
    'set'           = (Get-Col $Tables['set_def.csv'] 'set_id')
    'event'         = (Get-Col $Tables['event_check.csv'] 'check_id')
    'item'          = (Get-Col $Tables['item_base.csv'] 'item_id')
}
$castablePassives = @{}
foreach ($row in $Tables['buff_grant.csv']) {
    if (-not $buffSource.ContainsKey($row.source_type)) {
        Add-Error "[枚举] buff_grant.csv $($row.grant_id).source_type='$($row.source_type)' 不合法"
    }
    elseif ($buffSource[$row.source_type] -notcontains $row.source_id) {
        Add-Error "[引用] buff_grant.csv $($row.grant_id).source_id='$($row.source_id)' 在 $($row.source_type) 里不存在"
    }
    if ($row.trigger -eq 'on_cast' -and $row.source_type -eq 'skill_passive') {
        $castablePassives[$row.source_id] = $true
    }
}
foreach ($passiveId in (Get-Col $Tables['skill_passive.csv'] 'skill_id')) {
    if (-not $castablePassives.ContainsKey($passiveId)) {
        Add-Error "[完整] 内功 $passiveId 没有 on_cast 的 buff_grant，内功指令点它要没有反应"
    }
}

# 套装：成员类型与档位
$memberCountBySet = @{}
foreach ($row in $Tables['set_member.csv']) {
    if (-not $memberCountBySet.ContainsKey($row.set_id)) { $memberCountBySet[$row.set_id] = 0 }
    $memberCountBySet[$row.set_id]++
}
# 内功套按「占格数之和」计，装备套与招式套按件数计
$reachableBySet = @{}
$slotCostBySkill = @{}
foreach ($row in $Tables['skill_passive.csv']) {
    $cost = 0
    if ([int]::TryParse($row.slot_cost, [ref]$cost)) { $slotCostBySkill[$row.skill_id] = $cost }
}
$setKindOf = @{}
foreach ($row in $Tables['set_def.csv']) { $setKindOf[$row.set_id] = $row.set_kind }
foreach ($row in $Tables['set_member.csv']) {
    if (-not $reachableBySet.ContainsKey($row.set_id)) { $reachableBySet[$row.set_id] = 0 }
    if ($setKindOf[$row.set_id] -eq 'skill_passive' -and $slotCostBySkill.ContainsKey($row.member_id)) {
        $reachableBySet[$row.set_id] += $slotCostBySkill[$row.member_id]
    }
    else {
        $reachableBySet[$row.set_id]++
    }
}
$setKindById = @{}
foreach ($row in $Tables['set_def.csv']) {
    if (@('equip', 'skill_active', 'skill_passive') -notcontains $row.set_kind) {
        Add-Error "[枚举] set_def.csv $($row.set_id).set_kind='$($row.set_kind)' 不合法"
        continue
    }
    $setKindById[$row.set_id] = $row.set_kind
}
foreach ($row in $Tables['set_member.csv']) {
    if (-not $setKindById.ContainsKey($row.set_id)) { continue }
    $expectedKind = @{ 'equip' = 'equip'; 'skill_active' = 'skill_active'; 'skill_passive' = 'skill_passive' }[$setKindById[$row.set_id]]
    if ($buffSource[$expectedKind] -notcontains $row.member_id) {
        Add-Error "[引用] set_member.csv $($row.set_id) 的成员 '$($row.member_id)' 不是合法的 $expectedKind"
    }
}
$seenBonusTiers = @{}
foreach ($row in $Tables['set_bonus.csv']) {
    $tier = 0
    if (-not [int]::TryParse($row.required_count, [ref]$tier) -or $tier -lt 1) {
        Add-Error "[数值] set_bonus.csv $($row.set_id).required_count='$($row.required_count)' 应为正整数"
        continue
    }
    $tierKey = "$($row.set_id)|$tier"
    if ($seenBonusTiers.ContainsKey($tierKey)) { Add-Error "[重复] set_bonus.csv $($row.set_id) 的档位 $tier 定义了两次" }
    $seenBonusTiers[$tierKey] = $true
    $reachable = 0
    if ($reachableBySet.ContainsKey($row.set_id)) { $reachable = $reachableBySet[$row.set_id] }
    if ($tier -gt $reachable) {
        $unit = '件'
        if ($setKindOf[$row.set_id] -eq 'skill_passive') { $unit = '格' }
        Add-Error "[数值] set_bonus.csv $($row.set_id) 要求 $tier $unit，但成员最多只到 $reachable $unit，永远凑不齐"
    }
}
foreach ($setId in $setKindById.Keys) {
    if (-not $memberCountBySet.ContainsKey($setId)) { Add-Error "[完整] 套装 $setId 一个成员都没有" }
}

# 5.17 全局开关：取值合法，且必需开关必须存在
foreach ($row in $Tables['feature_toggle.csv']) {
    if (@('0', '1') -notcontains $row.value) {
        Add-Error "[枚举] feature_toggle.csv $($row.toggle_id).value='$($row.value)' 应为 0 或 1"
    }
    if ($row.desc -notmatch '0\s*=' -or $row.desc -notmatch '1\s*=') {
        Add-Warning "[说明] feature_toggle.csv $($row.toggle_id).desc 没有把 0 与 1 的行为都写清"
    }
}
foreach ($toggleId in @('overworld_roaming_enemy')) {
    if ((Get-Col $Tables['feature_toggle.csv'] 'toggle_id') -notcontains $toggleId) {
        Add-Error "[完整] feature_toggle.csv 缺少必需的开关 $toggleId"
    }
}

# 5.18 敌人招式：非中立敌人必须至少会一招
$skillsByEnemy = @{}
foreach ($row in $Tables['enemy_skill.csv']) {
    if (-not $skillsByEnemy.ContainsKey($row.enemy_id)) { $skillsByEnemy[$row.enemy_id] = @() }
    $skillsByEnemy[$row.enemy_id] += $row.skill_id
}
foreach ($row in $Tables['enemy_base.csv']) {
    $isNeutral = ($row.ai_template -eq 'ai_neutral')
    $count = 0
    if ($skillsByEnemy.ContainsKey($row.enemy_id)) { $count = $skillsByEnemy[$row.enemy_id].Count }
    if (-not $isNeutral -and $count -eq 0) {
        Add-Error "[完整] enemy_base.csv $($row.enemy_id) 不是中立单位，但 enemy_skill.csv 里一招都没有"
    }
    if ($isNeutral -and $count -gt 0) {
        Add-Warning "[说明] enemy_base.csv $($row.enemy_id) 标为中立却配了招式，确认是否要它出手"
    }
    if ($count -eq 1) {
        Add-Warning "[平衡] enemy_base.csv $($row.enemy_id) 只会一招，预兆没有信息量、拆招无从判断"
    }
}

# 5.19 属性：可加点标记，且必须留下不能加点的资质
$allocatableCount = 0
foreach ($row in $Tables['attribute_def.csv']) {
    if (@('0', '1') -notcontains $row.allocatable) {
        Add-Error "[枚举] attribute_def.csv $($row.attr_id).allocatable='$($row.allocatable)' 应为 0 或 1"
    }
    elseif ($row.allocatable -eq '1') { $allocatableCount++ }
}
if ($allocatableCount -eq 0) {
    Add-Error "[完整] attribute_def.csv 一个可加点的属性都没有，升级点数无处可加"
}

# 5.20 敌人装备的武器必须能匹配它的招式（否则招式被 _weapon_allows 静默挡掉）
$weaponByEnemy = @{}
foreach ($row in $Tables['enemy_equip.csv']) {
    if ($row.slot_id -eq 'weapon') { $weaponByEnemy[$row.enemy_id] = $row.equip_id }
}
$equipWeaponType = @{}
foreach ($row in $Tables['equip_base.csv']) { $equipWeaponType[$row.equip_id] = $row.weapon_type }
$skillWeaponType = @{}
foreach ($row in $Tables['skill_base.csv']) { $skillWeaponType[$row.skill_id] = $row.weapon_type }
foreach ($row in $Tables['enemy_skill.csv']) {
    if (-not $weaponByEnemy.ContainsKey($row.enemy_id)) { continue }
    $equipId = $weaponByEnemy[$row.enemy_id]
    if (-not $equipWeaponType.ContainsKey($equipId)) { continue }
    $have = $equipWeaponType[$equipId]
    $need = $skillWeaponType[$row.skill_id]
    if ($need -eq 'any' -or $need -eq '') { continue }
    if ($have -ne $need) {
        Add-Error "[完整] $($row.enemy_id) 装备武器 '$have'，但招式 $($row.skill_id) 要求 '$need'——这招会被静默挡掉"
    }
}

# 5.21 NPC 位置：可以是小地图场景，也可以是大地图区域
foreach ($row in $Tables['npc_def.csv']) {
    $inLocal  = (Get-Col $Tables['map_local.csv']  'scene_id') -contains $row.place_id
    $inRegion = (Get-Col $Tables['map_region.csv'] 'node_id')  -contains $row.place_id
    if (-not $inLocal -and -not $inRegion) {
        Add-Error "[引用] npc_def.csv $($row.npc_id).place_id='$($row.place_id)' 既不是小地图也不是大地图节点"
    }
}

# 5.21b 剧情节点的触发地点：与 NPC 同一套（小地图或大地图节点；留空 = 不限地点）。
# 本命机遇里「落雁坡旧镖车」只有大地图节点，所以这里必须两种都认——
# 以前它挂在通用的单值引用表里只查 map_local，落雁坡那条会被当场判红。
foreach ($row in $Tables['story_node.csv']) {
    $place = [string]$row.place_id
    if ($place -eq '') { continue }
    $inLocal  = (Get-Col $Tables['map_local.csv']  'scene_id') -contains $place
    $inRegion = (Get-Col $Tables['map_region.csv'] 'node_id')  -contains $place
    if (-not $inLocal -and -not $inRegion) {
        Add-Error "[引用] story_node.csv $($row.node_id).place_id='$place' 既不是小地图也不是大地图节点"
    }
}

# 5.21c 对话容器（设计 20 §十一，0.31.0）：说话人与「给的东西」都是**两处任一命中**
foreach ($row in $Tables['dialogue_node.csv']) {
    $speaker = [string]$row.speaker_id
    if ($speaker -eq '') {
        Add-Error "[完整] dialogue_node.csv $($row.node_id) 没有 speaker_id：这句话没人说"
        continue
    }
    $inNpc  = (Get-Col $Tables['npc_def.csv'] 'npc_id') -contains $speaker
    $inChar = (Get-Col $Tables['character_base.csv'] 'char_id') -contains $speaker
    # `player` = **主角自己**（设计 20 §3.1 序幕择念那种「主角替自己开口」）。
    # 主角是哪一号人物要等玩家选完出身才知道，所以表里写这个记号、运行期解析（决策 307）。
    $isPlayer = ($speaker -eq 'player')
    if (-not $inNpc -and -not $inChar -and -not $isPlayer) {
        Add-Error "[引用] dialogue_node.csv $($row.node_id).speaker_id='$speaker' 既不是 npc_def 里的人也不是 character_base 里的同伴"
    }
}
foreach ($row in $Tables['dialogue_option.csv']) {
    # `set_flag` 能写多个（分号隔开：幕二「拔剑」那条一次置两个旗标，Q83）——每一段都要认得出来。
    # 拆法是 GDScript 那边（行类 `dialogue_option_row.parse_set_flags`）的镜像，改一处要连着改另一处。
    foreach ($part in ([string]$row.set_flag) -split ';') {
        $flag = $part.Trim()
        if ($flag -ne '' -and -not $flag.StartsWith('flag_') -and -not $flag.StartsWith('heart_')) {
            Add-Error "[枚举] dialogue_option.csv $($row.option_id).set_flag='$flag' 既不是 flag_* 也不是心性（heart_*；多个旗标用分号隔开）"
        }
    }
    $item = [string]$row.grant_item_id
    if ($item -eq '') { continue }
    $inItem  = (Get-Col $Tables['item_base.csv']  'item_id')  -contains $item
    $inEquip = (Get-Col $Tables['equip_base.csv'] 'equip_id') -contains $item
    if (-not $inItem -and -not $inEquip) {
        Add-Error "[引用] dialogue_option.csv $($row.option_id).grant_item_id='$item' 既不是道具也不是装备"
    }
}
Write-Output ("  对话容器 : {0} 个节点 / {1} 条选项逐条查了说话人、跳转与效果落点" -f `
    @($Tables['dialogue_node.csv']).Count, @($Tables['dialogue_option.csv']).Count)

# 5.21d 观察点（设计 20 §3.2，0.29.1）：**小地图与大地图二选一**，而且必须有一句话。
# 这条规则构建期也有——两道网各查一遍是这套的规矩（变异探针当场点过名：只有一边查时，
# 「改坏之后 PS1 抓不到」）。
foreach ($row in $Tables['flavor_point.csv']) {
    $scene  = [string]$row.scene_id
    $region = [string]$row.region_id
    if ($scene -eq '' -and $region -eq '') {
        Add-Error "[完整] flavor_point.csv $($row.point_id) 既没有 scene_id 也没有 region_id——这一句碎句玩家永远看不到"
    }
    elseif ($scene -ne '' -and $region -ne '') {
        Add-Error "[完整] flavor_point.csv $($row.point_id) 同时填了 scene_id 与 region_id——两边都会收它，位点只该有一个"
    }
    if ([string]$row.text_cn -eq '') {
        Add-Error "[完整] flavor_point.csv $($row.point_id) 没有 text_cn：观察点就是那一句话"
    }
}
Write-Output ("  观察点 : {0} 行逐条查了地点（小地图／大地图二选一）与文案" -f @($Tables['flavor_point.csv']).Count)

# 5.21e 好感与委托挂在谁身上：**NPC 或同伴**（设计 20 §十「同伴与城镇 NPC 共用一套好感规则」）。
# 兑换货架（`npc_offer`）仍只认 NPC——设计写明同伴「好感解锁的是同伴内容，不是兑换货架」。
foreach ($name in @('npc_favor.csv', 'npc_quest.csv')) {
    foreach ($row in $Tables[$name]) {
        $person = [string]$row.npc_id
        if ($person -eq '') {
            Add-Error "[完整] $name 有一行没有 npc_id"
            continue
        }
        $inNpc  = (Get-Col $Tables['npc_def.csv'] 'npc_id') -contains $person
        $inChar = (Get-Col $Tables['character_base.csv'] 'char_id') -contains $person
        if (-not $inNpc -and -not $inChar) {
            Add-Error "[引用] $name $person 既不是 npc_def 里的人也不是 character_base 里的同伴"
        }
    }
}
Write-Output ("  人 : 好感 {0} 行 / 委托 {1} 行逐条查了 npc_id（可以是同伴）" -f `
    @($Tables['npc_favor.csv']).Count, @($Tables['npc_quest.csv']).Count)

# 5.21f NPC 给的**东西**也要真的存在（写错一个字母＝玩家交完委托什么也拿不到、还没有提示）。
# 兑换货架（`npc_offer.item_id`）与委托奖励（`npc_quest.reward_item_ids`，分号分隔）都查。
foreach ($row in $Tables['npc_offer.csv']) {
    $item = [string]$row.item_id
    if ($item -eq '') {
        Add-Error "[完整] npc_offer.csv $($row.offer_id) 没有 item_id"
        continue
    }
    $inItem  = (Get-Col $Tables['item_base.csv']  'item_id')  -contains $item
    $inEquip = (Get-Col $Tables['equip_base.csv'] 'equip_id') -contains $item
    if (-not $inItem -and -not $inEquip) {
        Add-Error "[引用] npc_offer.csv $($row.offer_id) 给的 '$item' 既不是道具也不是装备"
    }
}
foreach ($row in $Tables['npc_quest.csv']) {
    foreach ($item in ([string]$row.reward_item_ids -split ';')) {
        if ($item -eq '') { continue }
        $inItem  = (Get-Col $Tables['item_base.csv']  'item_id')  -contains $item
        $inEquip = (Get-Col $Tables['equip_base.csv'] 'equip_id') -contains $item
        if (-not $inItem -and -not $inEquip) {
            Add-Error "[引用] npc_quest.csv $($row.quest_id) 奖励的 '$item' 既不是道具也不是装备"
        }
    }
}
Write-Output ("  NPC 给的东西 : 货架 {0} 行 + 委托 {1} 行的奖励逐条查了存在性" -f `
    @($Tables['npc_offer.csv']).Count, @($Tables['npc_quest.csv']).Count)

# 5.21g 地标图标：`map_region.icon` 指向 `assets/sprites/icons/<icon>.png`。
# 缺文件时**大地图上那个地标什么都不画**（`refresh_node_icons()` 里两条都找不到就隐藏），
# 玩家只会觉得"这里没有图标"，不会觉得是数据错——所以这条要在验收期就红。
$missingIcons = 0
foreach ($row in $Tables['map_region.csv']) {
    $icon = [string]$row.icon
    if ($icon -eq '') { continue }
    $iconPath = Join-Path $root ("assets\sprites\icons\" + $icon + ".png")
    if (-not (Test-Path -LiteralPath $iconPath)) {
        Add-Error "[资源] map_region.csv $($row.node_id).icon='$icon' 没有对应贴图：assets/sprites/icons/$icon.png"
        $missingIcons++
    }
}
Write-Output ("  地标图标 : {0} 行逐条查了贴图存在（缺 {1}）" -f @($Tables['map_region.csv']).Count, $missingIcons)

# ---------------------------------------------------------------- 6. 孤立数据

$usedTeams = @{}
foreach ($v in (Get-Col $Tables['roaming_spawn.csv'] 'team_id')) { $usedTeams[$v] = $true }
foreach ($v in (Get-Col $Tables['dungeon_room.csv'] 'enemy_team')) { $usedTeams[$v] = $true }
# 大地图随机事件的切磋队伍（0.28.0 Q64：`we_disciple` 指向 `team_wanderer_disciple`）——
# 它没有刷新点、也不属于任何房间，队伍由事件当场拉起来。
foreach ($row in $Tables['world_event.csv']) {
    if ([string]$row.effect_kind -eq 'spar' -and [string]$row.effect_id -ne '') { $usedTeams[[string]$row.effect_id] = $true }
}
# 代码里点名的队伍也算「有人用」：练习战那支（`PracticeService.TEAM_ID = team_dummy_training`）
# 既没有刷新点、也不属于任何房间——它是 `building_def.service_id=dummy_training` 的建筑当场拉起来的。
# 口径与 `test_handshake` 那边的「旗标有没有人提」一致：**按 id 在 src 里出现过算数**；
# 代价是只写在注释里也算（那种漏网由各自的用例管——练习战有 `test_practice`）。
# `$srcText` 在 5.19 那一段已经扫好了（那里也要用），这里直接用，别再扫一遍。
foreach ($v in (Get-Col $Tables['enemy_team.csv'] 'team_id')) {
    if (-not $usedTeams.ContainsKey($v) -and -not $srcText.Contains($v)) {
        Add-Warning "[孤立] enemy_team.csv 的 $v 没有被任何刷新点、房间或代码用到"
    }
}

$usedDropGroups = @{}
foreach ($v in (Get-Col $Tables['enemy_base.csv'] 'drop_group')) { $usedDropGroups[$v] = $true }
foreach ($v in (Get-Col $Tables['dungeon_room.csv'] 'chest_id')) { $usedDropGroups[$v] = $true }
foreach ($v in (Get-Col $Tables['drop_table.csv'] 'drop_group' | Select-Object -Unique)) {
    if (-not $usedDropGroups.ContainsKey($v)) { Add-Warning "[孤立] drop_table.csv 的掉落组 $v 没有任何敌人或宝箱使用" }
}

$entrances = @{}
foreach ($row in $Tables['dungeon_room.csv']) { if ($row.room_type -eq 'entrance') { $entrances[$row.room_id] = $true } }
$reachable = @{}
foreach ($row in $Tables['dungeon_room.csv']) {
    foreach ($e in ($row.exit_rooms -split '\|')) { if ($e -ne '') { $reachable[$e] = $true } }
}
foreach ($v in (Get-Col $Tables['hidden_trigger.csv'] 'room_id')) { $reachable[$v] = $true }
foreach ($k in $entrances.Keys) { $reachable[$k] = $true }
foreach ($v in (Get-Col $Tables['dungeon_room.csv'] 'room_id')) {
    if (-not $reachable.ContainsKey($v)) { Add-Warning "[孤立] dungeon_room.csv 的 $v 无法从任何房间到达" }
}

# ---------------------------------------------------------------- 7. 数值区间

foreach ($row in $Tables['drop_table.csv']) {
    $rate = 0.0
    if ([double]::TryParse($row.base_rate, [ref]$rate)) {
        if ($rate -lt 0 -or $rate -gt 1) { Add-Error "[数值] drop_table.csv $($row.drop_row_id).base_rate=$($row.base_rate) 应在 0~1" }
    }
    else { Add-Error "[数值] drop_table.csv $($row.drop_row_id).base_rate 不是数字" }

    $min = 0; $max = 0
    if ([int]::TryParse($row.qty_min, [ref]$min) -and [int]::TryParse($row.qty_max, [ref]$max)) {
        if ($min -gt $max) { Add-Error "[数值] drop_table.csv $($row.drop_row_id) 数量区间 $min > $max" }
    }
    else { Add-Error "[数值] drop_table.csv $($row.drop_row_id) 数量不是整数" }
}

foreach ($row in $Tables['difficulty_drop_rate.csv']) {
    $m = 0.0
    if ([double]::TryParse($row.rate_multiplier, [ref]$m)) {
        if ($m -le 0) { Add-Error "[数值] difficulty_drop_rate.csv $($row.difficulty_id)/$($row.rarity_id) 倍率必须大于 0" }
    }
    else { Add-Error "[数值] difficulty_drop_rate.csv $($row.difficulty_id)/$($row.rarity_id) 倍率不是数字" }
}

foreach ($row in $Tables['affix_pool.csv']) {
    $min = 0.0; $max = 0.0
    if ([double]::TryParse($row.value_min, [ref]$min) -and [double]::TryParse($row.value_max, [ref]$max)) {
        if ($min -gt $max) { Add-Error "[数值] affix_pool.csv $($row.affix_id) 区间 $min > $max" }
        if ($row.value_kind -eq 'rate' -and ($min -lt 0 -or $max -gt 1)) {
            Add-Error "[数值] affix_pool.csv $($row.affix_id) 是比率类，数值应在 0~1"
        }
    }
    else { Add-Error "[数值] affix_pool.csv $($row.affix_id) 区间不是数字" }
}

foreach ($row in $Tables['stat_def.csv']) {
    if ($row.min_value -ne '' -and $row.max_value -ne '') {
        $lo = 0.0; $hi = 0.0
        if ([double]::TryParse($row.min_value, [ref]$lo) -and [double]::TryParse($row.max_value, [ref]$hi)) {
            if ($lo -gt $hi) { Add-Error "[数值] stat_def.csv $($row.stat_id) min_value > max_value" }
        }
    }
}

# ---------------------------------------------------------------- 8. 文档同步

if (-not (Test-Path -LiteralPath $docPath)) {
    Add-Error "[同步] 找不到配置表说明文档: $docPath"
}
else {
    $documented = @{}
    $planned    = @{}
    foreach ($line in (Get-Content -LiteralPath $docPath -Encoding UTF8)) {
        if ($line -notmatch '^\s*\|') { continue }
        $cells = @($line.Trim().Trim('|') -split '\|')
        if ($cells.Count -lt 3) { continue }

        $cellMatch = [regex]::Match($cells[0], '^\s*`([A-Za-z0-9_]+\.csv)`\s*$')
        if (-not $cellMatch.Success) { continue }

        # 表清单的最后一列是行数，取最后一个非空单元格。
        # 不用整体正则匹配整行，是因为复合主键单元格形如 `a`+`b`，写死正则会漏。
        $countCell = ''
        for ($i = $cells.Count - 1; $i -ge 0; $i--) {
            if ($cells[$i].Trim() -ne '') { $countCell = $cells[$i].Trim(); break }
        }
        if ($countCell -match '^\d+$') {
            $documented[$cellMatch.Groups[1].Value] = [int]$countCell
        }
        else {
            # 末列不是数字 = 文档里的「待补充的表」，属于规划而非已登记。
            $planned[$cellMatch.Groups[1].Value] = $true
        }
    }

    foreach ($f in $csvFiles) {
        if (-not $documented.ContainsKey($f.Name)) {
            Add-Error "[同步] $($f.Name) 未登记在 06_配置表说明.md 的表清单中"
            continue
        }
        $actual = @($Tables[$f.Name]).Count
        if ($documented[$f.Name] -ne $actual) {
            Add-Error "[同步] $($f.Name) 行数不符：文档写 $($documented[$f.Name])，实际 $actual"
        }
    }
    foreach ($k in $documented.Keys) {
        if (-not (Test-Path -LiteralPath (Join-Path $tableDir $k))) {
            Add-Error "[同步] 06_配置表说明.md 登记了不存在的表 $k"
        }
    }
    foreach ($k in $planned.Keys) {
        if (Test-Path -LiteralPath (Join-Path $tableDir $k)) {
            Add-Error "[同步] $k 已存在于磁盘，但 06_配置表说明.md 仍把它列在「待补充的表」里，需要移入表清单并登记行数"
        }
    }
}

# ---------------------------------------------------------------- 9. 与 Godot 行类的列漂移

$registryPath = Join-Path $Root 'src/data/table_registry.gd'
$rowScriptDir = Join-Path $Root 'src/data/tables'
$driftCount   = 0

if ((Test-Path -LiteralPath $registryPath) -and (Test-Path -LiteralPath $rowScriptDir)) {
    $registryText = Get-Content -LiteralPath $registryPath -Encoding UTF8 -Raw
    $tableBlockMatch = [regex]::Match($registryText, 'const\s+TABLES\s*:=\s*\[(.*?)\]', 'Singleline')
    $registeredTables = @()
    if ($tableBlockMatch.Success) {
        $registeredTables = @([regex]::Matches($tableBlockMatch.Groups[1].Value, '"([a-z0-9_]+)"') |
            ForEach-Object { $_.Groups[1].Value })
    }

    # 列别名：CSV 列名与行类属性名不一致的少数情况（如 id→row_id、exp→exp_reward），
    # 必须套用别名后再比对，否则会把合法表报成漂移。
    $columnAliases = @{}
    $aliasBlock = [regex]::Match($registryText, 'const\s+COLUMN_ALIASES\s*:=\s*\{(.*?)\n\}', 'Singleline')
    if ($aliasBlock.Success) {
        foreach ($tableMatch in [regex]::Matches($aliasBlock.Groups[1].Value, '"([a-z0-9_]+)"\s*:\s*\{([^}]*)\}')) {
            $aliasTable = $tableMatch.Groups[1].Value
            $aliasMap = @{}
            foreach ($pairMatch in [regex]::Matches($tableMatch.Groups[2].Value, '"([^"]+)"\s*:\s*"([^"]+)"')) {
                $aliasMap[$pairMatch.Groups[1].Value] = $pairMatch.Groups[2].Value
            }
            $columnAliases[$aliasTable] = $aliasMap
        }
    }

    foreach ($tableName in $registeredTables) {
        $csvPath = Join-Path $tableDir "$tableName.csv"
        $rowPath = Join-Path $rowScriptDir "${tableName}_row.gd"

        if (-not (Test-Path -LiteralPath $csvPath)) {
            Add-Warning "[代码漂移] 注册表登记了 $tableName，但 data/tables 下没有 $tableName.csv"
            $driftCount++
            continue
        }
        if (-not (Test-Path -LiteralPath $rowPath)) {
            Add-Warning "[代码漂移] 注册表登记了 $tableName，但没有行类 ${tableName}_row.gd"
            $driftCount++
            continue
        }

        $csvColumns = Split-CsvLine (@(Get-RawLines $csvPath)[0])
        $rowText = Get-Content -LiteralPath $rowPath -Encoding UTF8 -Raw
        # 行类声明的 @export 字段；基类的 id 由构建脚本写入，不是 CSV 列
        $exportNames = @([regex]::Matches($rowText, '@export\s+var\s+([A-Za-z_][A-Za-z0-9_]*)\s*:') |
            ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -ne 'id' })

        # CSV 列 → 套用别名后应当出现的行类属性名
        $expectedProps = @()
        foreach ($column in $csvColumns) {
            if ($columnAliases.ContainsKey($tableName) -and $columnAliases[$tableName].ContainsKey($column)) {
                $expectedProps += $columnAliases[$tableName][$column]
            }
            else {
                $expectedProps += $column
            }
        }

        for ($colIndex = 0; $colIndex -lt $csvColumns.Count; $colIndex++) {
            if ($exportNames -notcontains $expectedProps[$colIndex]) {
                Add-Warning "[代码漂移] $tableName 的 CSV 列 '$($csvColumns[$colIndex])' 在 ${tableName}_row.gd 里没有对应属性，构建会失败"
                $driftCount++
            }
        }
        foreach ($propertyName in $exportNames) {
            if ($expectedProps -notcontains $propertyName) {
                Add-Warning "[代码漂移] $tableName 的行类属性 '$propertyName' 在 CSV 表头里没有对应列，构建会失败"
                $driftCount++
            }
        }
    }

    foreach ($f in $csvFiles) {
        $baseName = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
        if ($registeredTables -notcontains $baseName) {
            Add-Warning "[代码漂移] $($f.Name) 尚未登记到 table_registry.gd，游戏运行期读不到这张表"
            $driftCount++
        }
    }
}
else {
    Add-Warning '[代码漂移] 未找到 src/data/table_registry.gd 或 src/data/tables，跳过代码一致性检查'
}

# ---------------------------------------------------------------- 10. 开发对接表

$contractPath  = Join-Path $Root 'docs/dev/模块对接表.csv'
$changelogPath = Join-Path $Root 'docs/design/CHANGELOG.md'
$contractCount = 0

if (Test-Path -LiteralPath $contractPath) {
    $contract = @(Import-Csv -LiteralPath $contractPath -Encoding UTF8)
    $contractCount = $contract.Count

    $validStatuses  = @('未开始', '进行中', '已完成', '需返工', '已冻结')
    $startedStatuses = @('进行中', '已完成', '需返工')
    $designDocNames = @(Get-ChildItem -LiteralPath (Join-Path $Root 'docs/design') -Filter *.md | ForEach-Object { $_.Name })
    $tableNames     = @($csvFiles | ForEach-Object { $_.Name })

    $currentVersion = ''
    if (Test-Path -LiteralPath $changelogPath) {
        foreach ($line in (Get-Content -LiteralPath $changelogPath -Encoding UTF8)) {
            $versionMatch = [regex]::Match($line, '当前设计版本[:：]\s*\*{0,2}([0-9]+\.[0-9]+\.[0-9]+)')
            if ($versionMatch.Success) { $currentVersion = $versionMatch.Groups[1].Value; break }
        }
        if ($currentVersion -eq '') { Add-Warning '[对接] CHANGELOG.md 里找不到「当前设计版本」标记' }
    }
    else { Add-Warning '[对接] 找不到 docs/design/CHANGELOG.md' }

    foreach ($row in $contract) {
        foreach ($docRef in ($row.design_docs -split '\|')) {
            if ($docRef -eq '') { continue }
            if ($designDocNames -notcontains $docRef) {
                Add-Error "[对接] $($row.module_id).design_docs 引用了不存在的文档 '$docRef'"
            }
        }
        foreach ($tableRef in ($row.data_tables -split '\|')) {
            if ($tableRef -eq '') { continue }
            if ($tableNames -notcontains $tableRef) {
                Add-Error "[对接] $($row.module_id).data_tables 引用了不存在的表 '$tableRef'"
            }
        }
        if ($validStatuses -notcontains $row.status) {
            Add-Error "[对接] $($row.module_id).status='$($row.status)' 不是合法状态"
        }
        if ($currentVersion -ne '' -and $row.design_version -ne $currentVersion) {
            if ($startedStatuses -contains $row.status) {
                Add-Warning "[对接] $($row.module_id)（$($row.status)）对齐的设计版本为 $($row.design_version)，当前为 $currentVersion，需评估返工"
            }
        }
    }
}
else {
    Add-Warning '[对接] 找不到 docs/dev/模块对接表.csv'
}

# ---------------------------------------------------------------- 输出

# 补齐「枚举三处」里漏在 PS1 的那 7 列（2026-10-04）。
#
# 由来：`data/AGENTS.md` 写着「改枚举要三处一起改：06 数据字典／`table_validator.ENUMS`／本脚本」，
# 而这一轮逐个对了一遍——`ENUMS` 里 41 个枚举列里，有 **7 列的列名在本脚本里一次都没出现过**
# （也就是**这边根本没查**）：`building_def.building_type`／`drop_table.roll_type`／
# `item_base.use_context`／`map_local.scene_type`／`map_region.node_type`／`talent_def.category`／
# `weapon_type_def.default_element`。取值一律镜像 `table_validator.ENUMS`（那边是唯一出处，
# 与 `docs/design/06_配置表说明.md` 由 `test_handshake._check_doc_enums_match_code` 对账）。
$enumColumns = @(
    @{ Table = 'building_def.csv';    Id = 'building_id'; Column = 'building_type';   Values = @('shop', 'service') },
    @{ Table = 'drop_table.csv';      Id = 'drop_row_id'; Column = 'roll_type';      Values = @('independent', 'exclusive') },
    @{ Table = 'item_base.csv';       Id = 'item_id';     Column = 'use_context';   Values = @('', 'field', 'battle') },
    @{ Table = 'map_local.csv';       Id = 'scene_id';    Column = 'scene_type';    Values = @('town', 'dungeon', 'poi') },
    @{ Table = 'map_region.csv';      Id = 'node_id';     Column = 'node_type';     Values = @('town', 'fast_travel', 'wild', 'dungeon', 'poi') },
    @{ Table = 'talent_def.csv';      Id = 'talent_id';   Column = 'category';      Values = @('combat', 'body', 'agile', 'mind', 'social', 'fortune') },
    @{ Table = 'weapon_type_def.csv'; Id = 'weapon_type'; Column = 'default_element'; Values = @('external', 'internal', 'odd') },
    @{ Table = 'skill_active.csv';    Id = 'skill_id';    Column = 'target_type';   Values = @('single', 'self', 'all_enemy') },
    @{ Table = 'dungeon_room.csv';    Id = 'room_id';     Column = 'branch_group';  Values = @('main', 'side', 'hidden') },
    @{ Table = 'hidden_trigger.csv';  Id = 'trigger_id';  Column = 'trigger_type';  Values = @('item', 'space', 'kill_style', 'completion', 'sequence', 'behavior', 'carry') }
)
foreach ($spec in $enumColumns) {
    foreach ($row in $Tables[$spec.Table]) {
        $value = $row.($spec.Column)
        if ($spec.Values -notcontains $value) {
            Add-Error "[枚举] $($spec.Table) $($row.($spec.Id)).$($spec.Column)='$value' 不是 $(($spec.Values | Where-Object { $_ -ne '' }) -join '／')"
        }
    }
    Write-Output ("  {0} : {1} 行逐列查了 {2} 枚举" -f $spec.Table, @($Tables[$spec.Table]).Count, $spec.Column)
}

# 图标列必须等于**这一行自己的 id**（`equip_base.icon`／`item_base.icon`，15 §六＋A14，0.31.1）。
# 界面取图是「优先 icon、空则退回行 id」——写错不会报错，只会**画出别人家的图标**；
# 空着合法（等于退回 id）。构建期那条规则在 `table_validator._check_icon_self_reference`，这里是同一套。
foreach ($spec in @(
    @{ Table = 'equip_base.csv'; Id = 'equip_id' },
    @{ Table = 'item_base.csv';  Id = 'item_id' }
)) {
    foreach ($row in $Tables[$spec.Table]) {
        $icon = "$($row.icon)".Trim()
        if ($icon -eq '') { continue }
        if ($icon -ne $row.($spec.Id)) {
            Add-Error "[图标] $($spec.Table) $($row.($spec.Id)).icon='$icon' 必须等于它自己的 $($spec.Id)"
        }
    }
}

Write-Output ""
Write-Output "配置表校验"
Write-Output ("  表数量 : {0}" -f $csvFiles.Count)
Write-Output ("  总行数 : {0}" -f (($csvFiles | ForEach-Object { @($Tables[$_.Name]).Count }) | Measure-Object -Sum).Sum)
Write-Output ("  对接模块 : {0}" -f $contractCount)
Write-Output ""

if ($warnings.Count -gt 0) {
    Write-Output "警告 $($warnings.Count) 条"
    foreach ($w in $warnings) { Write-Output "  ! $w" }
    Write-Output ""
}

if ($errors.Count -gt 0) {
    Write-Output "错误 $($errors.Count) 条"
    foreach ($e in $errors) { Write-Output "  x $e" }
    Write-Output ""
    Write-Output "FAILED"
    exit 1
}

Write-Output "PASSED"
exit 0
