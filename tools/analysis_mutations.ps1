<#
变异探针（只读·手动工具，不进验收）：把数据改坏，看 **validate_tables.ps1** 抓不抓得住。

为什么单独有一半：验收里其实有两道网——`table_validator.gd`（构建期，见 analysis_mutations.gd）
与 `validate_tables.ps1`。两边管的不是同一批东西：PS1 管编码/列数/主键/引用/枚举/价格方向/
武器槽一致性/文档比对这些。所以只跑一半会把「另一道网已经管住」误判成"没人管"。

做法：拷一份表与文档到临时目录（**绝不碰真文件**），在副本上逐个改坏，
`validate_tables.ps1 -Root <临时目录>` 跑一遍，比较「错误/警告条数」与基线——
一模一样就是没抓住。退出码 0 = 全部抓住。

用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\analysis_mutations.ps1
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$probe = Join-Path ([IO.Path]::GetTempPath()) ('lunhui_mut_' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$pristine = Join-Path $probe 'pristine'
$work = Join-Path $probe 'work'

function Initialize-Probe {
    New-Item -ItemType Directory -Path (Join-Path $pristine 'data') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $pristine 'docs\design') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $pristine 'docs\dev') -Force | Out-Null
    Copy-Item (Join-Path $root 'data\tables') (Join-Path $pristine 'data\tables') -Recurse
    Copy-Item (Join-Path $root 'docs\design\*') (Join-Path $pristine 'docs\design')
    Copy-Item (Join-Path $root 'docs\dev\模块对接表.csv') (Join-Path $pristine 'docs\dev')
    # **贴图目录也要带上**：验收里有一条「`map_region.icon` 指向的贴图必须存在」（5.21g），
    # 探针里没有 assets 的话那条会对每一行都报红——基线本来就红，改坏它反而看不出变化
    # （探针的判定是「错误/警告条数有没有变」）。三类资源里现在只有地标图标被规则查过。
    $iconsSrc = Join-Path $root 'assets\sprites\icons'
    if (Test-Path -LiteralPath $iconsSrc) {
        $iconsDst = Join-Path $pristine 'assets\sprites\icons'
        New-Item -ItemType Directory -Path $iconsDst -Force | Out-Null
        Copy-Item (Join-Path $iconsSrc '*') $iconsDst
    }
}

function Reset-Work {
    if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    Copy-Item (Join-Path $pristine 'data') (Join-Path $work 'data') -Recurse
    Copy-Item (Join-Path $pristine 'docs') (Join-Path $work 'docs') -Recurse
    $iconsSrc = Join-Path $pristine 'assets'
    if (Test-Path -LiteralPath $iconsSrc) {
        Copy-Item $iconsSrc (Join-Path $work 'assets') -Recurse
    }
}

function Get-CsvLines {
    param([string]$Name)
    return [IO.File]::ReadAllLines((Join-Path $work "data\tables\$Name"))
}

function Save-CsvLines {
    param([string]$Name, $Lines)
    [IO.File]::WriteAllLines((Join-Path $work "data\tables\$Name"), $Lines, (New-Object Text.UTF8Encoding($true)))
}

function Invoke-Check {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\validate_tables.ps1') -Root $work 2>&1
    $err = 0; $warn = 0
    $msgs = New-Object System.Collections.Generic.List[string]
    foreach ($line in $out) {
        $text = [string]$line
        if ($text -match '^\s*错误\s+(\d+)\s*条') { $err = [int]$Matches[1] }
        if ($text -match '^\s*警告\s+(\d+)\s*条') { $warn = [int]$Matches[1] }
        if ($text -match '^\s*x\s+(.*)$') { $msgs.Add($Matches[1]) }
        elseif ($text -match '^\s*!\s+(.*)$') { $msgs.Add($Matches[1]) }
    }
    return [pscustomobject]@{ err = $err; warn = $warn; msgs = $msgs }
}

# ---------------------------------------------------------------- 用例
# 每条都是「本该被 PS1 抓住」的改坏方式；catch 里的工作目录就是 $work 的副本。
$cases = [ordered]@{
    '缺 UTF-8 BOM' = {
        $p = Join-Path $work 'data\tables\item_base.csv'
        $bytes = [IO.File]::ReadAllBytes($p)
        [IO.File]::WriteAllBytes($p, [byte[]]$bytes[3..($bytes.Length - 1)])
    }
    '某行列数不一致' = {
        $l = Get-CsvLines 'item_base.csv'
        $l += (($l[1]) + ',多出来的一列')
        Save-CsvLines 'item_base.csv' $l
    }
    '主键重复' = {
        $l = Get-CsvLines 'item_base.csv'
        $l += $l[1]
        Save-CsvLines 'item_base.csv' $l
    }
    '引用不存在（装备稀有度）' = {
        $l = Get-CsvLines 'equip_base.csv'
        $c = $l[1] -split ','
        $c[3] = 'bogus_rarity'
        $l[1] = $c -join ','
        Save-CsvLines 'equip_base.csv' $l
    }
    '枚举非法（物品类型）' = {
        $l = Get-CsvLines 'item_base.csv'
        $h = $l[0] -split ','
        $ti = [array]::IndexOf($h, 'item_type')
        $c = $l[1] -split ','
        $c[$ti] = 'bogus_type'
        $l[1] = $c -join ','
        Save-CsvLines 'item_base.csv' $l
    }
    '商店买入价 <= 卖出价' = {
        $l = Get-CsvLines 'shop_stock.csv'
        $h = $l[0] -split ','
        $bi = [array]::IndexOf($h, 'buy_price')
        $si = [array]::IndexOf($h, 'sell_price')
        $c = $l[1] -split ','
        $c[$bi] = '1'; $c[$si] = '5'
        $l[1] = $c -join ','
        Save-CsvLines 'shop_stock.csv' $l
    }
    '武器槽缺 weapon_type' = {
        $l = Get-CsvLines 'equip_base.csv'
        $h = $l[0] -split ','
        $wi = [array]::IndexOf($h, 'weapon_type')
        $c = $l[1] -split ','
        $c[$wi] = ''
        $l[1] = $c -join ','
        Save-CsvLines 'equip_base.csv' $l
    }
    '隐藏奖励指向不存在的东西' = {
        $l = Get-CsvLines 'hidden_trigger.csv'
        $h = $l[0] -split ','
        $ri = [array]::IndexOf($h, 'reward_id')
        $c = $l[1] -split ','
        if ($c[$ri] -eq '') { $c[$ri] = 'bogus_reward' } else { $c[$ri] = 'bogus_reward' }
        $l[1] = $c -join ','
        Save-CsvLines 'hidden_trigger.csv' $l
    }
    '房间出口指向不存在的房间' = {
        $l = Get-CsvLines 'dungeon_room.csv'
        $h = $l[0] -split ','
        $ei = [array]::IndexOf($h, 'exit_rooms')
        $c = $l[1] -split ','
        $c[$ei] = 'hf_bogus_room'
        $l[1] = $c -join ','
        Save-CsvLines 'dungeon_room.csv' $l
    }
    '文档比对覆盖归零（改坏 04 的伤害类型表）' = {
        $p = Join-Path $work 'docs\design\04_战斗与伤害.md'
        $t = [IO.File]::ReadAllText($p)
        $t = $t -replace '(?m)^\| (dmg_|dot_)', '|| $1'
        [IO.File]::WriteAllText($p, $t, (New-Object Text.UTF8Encoding($true)))
    }
    # 下面两条是「构建期不报、只有 PS1 管」的样例：写在这里，顺便证明两道网的分工是真的。
    '招式倍率为 0（5.21 招式效果）' = {
        $l = Get-CsvLines 'skill_active.csv'
        $h = $l[0] -split ','
        $pi = [array]::IndexOf($h, 'power_ratio')
        $c = $l[1] -split ','
        $c[$pi] = '0'
        $l[1] = $c -join ','
        Save-CsvLines 'skill_active.csv' $l
    }
    '难度掉落倍率为 0' = {
        $l = Get-CsvLines 'difficulty_drop_rate.csv'
        $h = $l[0] -split ','
        $ri = [array]::IndexOf($h, 'rate_multiplier')
        $c = $l[1] -split ','
        $c[$ri] = '0'
        $l[1] = $c -join ','
        Save-CsvLines 'difficulty_drop_rate.csv' $l
    }
    # 0.31.0 起的三张新表／新规则，各留一条「故意改坏」：证明这两道网真的盖到它们。
    '对话选项指向不存在的节点' = {
        $l = Get-CsvLines 'dialogue_option.csv'
        $h = $l[0] -split ','
        $ni = [array]::IndexOf($h, 'node_id')
        $c = $l[1] -split ','
        $c[$ni] = 'bogus_node'
        $l[1] = $c -join ','
        Save-CsvLines 'dialogue_option.csv' $l
    }
    '对话选项的心性旗标形状非法' = {
        $l = Get-CsvLines 'dialogue_option.csv'
        $h = $l[0] -split ','
        $fi = [array]::IndexOf($h, 'set_flag')
        $c = $l[1] -split ','
        $c[$fi] = 'heartz_yi'
        $l[1] = $c -join ','
        Save-CsvLines 'dialogue_option.csv' $l
    }
    '观察点既没有小地图也没有区域' = {
        $l = Get-CsvLines 'flavor_point.csv'
        $h = $l[0] -split ','
        $si = [array]::IndexOf($h, 'scene_id')
        $ri = [array]::IndexOf($h, 'region_id')
        $c = $l[1] -split ','
        $c[$si] = ''; $c[$ri] = ''
        $l[1] = $c -join ','
        Save-CsvLines 'flavor_point.csv' $l
    }
    '抉择节点四列增益全空' = {
        $l = Get-CsvLines 'story_node.csv'
        $h = $l[0] -split ','
        $ki = [array]::IndexOf($h, 'kind')
        $ai = [array]::IndexOf($h, 'bonus_attr_id')
        $si = [array]::IndexOf($h, 'bonus_stat_id')
        for ($i = 1; $i -lt $l.Count; $i++) {
            $c = $l[$i] -split ','
            if ($c[$ki] -ne 'choice') { continue }
            $c[$ai] = ''; $c[$si] = ''
            $l[$i] = $c -join ','
            break
        }
        Save-CsvLines 'story_node.csv' $l
    }
    '好感挂到不存在的人身上' = {
        $l = Get-CsvLines 'npc_favor.csv'
        $h = $l[0] -split ','
        $ni = [array]::IndexOf($h, 'npc_id')
        $c = $l[1] -split ','
        $c[$ni] = 'bogus_person'
        $l[1] = $c -join ','
        Save-CsvLines 'npc_favor.csv' $l
    }
    '委托奖励指向不存在的物品' = {
        $l = Get-CsvLines 'npc_quest.csv'
        $h = $l[0] -split ','
        $ri = [array]::IndexOf($h, 'reward_item_ids')
        $c = $l[1] -split ','
        $c[$ri] = 'bogus_reward'
        $l[1] = $c -join ','
        Save-CsvLines 'npc_quest.csv' $l
    }
    '地标图标指向不存在的贴图' = {
        $l = Get-CsvLines 'map_region.csv'
        $h = $l[0] -split ','
        $ii = [array]::IndexOf($h, 'icon')
        $c = $l[1] -split ','
        $c[$ii] = 'icon_bogus'
        $l[1] = $c -join ','
        Save-CsvLines 'map_region.csv' $l
    }
    '套装档位凑不出来（把上限抬到拿不到）' = {
        # 5.19b：黑风套最多穿 4 件（武器／身体／戒指／肩部各一），把档位抬到 5 就该报出来
        $l = Get-CsvLines 'set_bonus.csv'
        $h = $l[0] -split ','
        $si = [array]::IndexOf($h, 'set_id')
        $ci = [array]::IndexOf($h, 'required_count')
        for ($i = 1; $i -lt $l.Count; $i++) {
            $c = $l[$i] -split ','
            if ($c[$si] -ne 'set_heifeng' -or $c[$ci] -ne '4') { continue }
            $c[$ci] = '5'
            $l[$i] = $c -join ','
            break
        }
        Save-CsvLines 'set_bonus.csv' $l
    }
}

Initialize-Probe
Reset-Work
$base = Invoke-Check
$baseSet = @{}
foreach ($m in $base.msgs) { $baseSet[$m] = $true }
Write-Output ("基线：错误 {0} / 警告 {1}" -f $base.err, $base.warn)

$missed = @()
foreach ($name in $cases.Keys) {
    Reset-Work
    & $cases[$name]
    $r = Invoke-Check
    if ($r.err -eq $base.err -and $r.warn -eq $base.warn) {
        $missed += $name
        Write-Output ("  *** 没抓住 *** {0}（错误 {1} / 警告 {2}）" -f $name, $r.err, $r.warn)
    }
    else {
        # 把**新出现的**那条报错打出来：光比条数只能说明"有变化"，打出原文才能核对"抓的是不是这条规则"
        $newest = ''
        foreach ($m in $r.msgs) { if (-not $baseSet.ContainsKey($m)) { $newest = $m; break } }
        if ($newest.Length -gt 64) { $newest = $newest.Substring(0, 64) }
        Write-Output ("  [OK]   {0}（错误 {1} / 警告 {2}）← {3}" -f $name, $r.err, $r.warn, $newest)
    }
}

Remove-Item -LiteralPath $probe -Recurse -Force
Write-Output ("PS1 变异探针：{0} 个 case，漏 {1}" -f $cases.Count, $missed.Count)
if ($missed.Count -gt 0) {
    Write-Output ("PS1 MUTATIONS MISSED: " + ($missed -join ', '))
    Write-Output 'PS1 MUTATIONS: FAILED'
    exit 1
}
Write-Output 'PS1 MUTATIONS: OK'
exit 0
