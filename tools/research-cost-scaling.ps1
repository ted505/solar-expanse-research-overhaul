param(
    [string]$ResearchYaml = "C:\Program Files (x86)\Steam\steamapps\common\Solar Expanse\BepInEx\plugins\Teddit\dump\research.yaml",
    [string]$OutDir = ".\research-scaling-output",
    [double]$BaseResearchPointPerMonth = 100.0,
    [double]$PivotMonths = 120.0,
    [double]$Exponent = 1.5,
    [double]$MaxMultiplier = 30.0,
    [string]$TargetResearchId = "research_sc_nike",
    [switch]$WriteOverrideYaml
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-ResearchEntry {
    param([string]$Id, [string]$Name)

    [pscustomobject]@{
        Id = $Id
        Name = $Name
        WorkHours = 0.0
        Stage = $null
        SubStage = $null
        ResearchType = ""
        ResearchSubType = ""
        Requirements = [System.Collections.Generic.List[string]]::new()
    }
}

function Parse-ResearchYaml {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Research YAML not found: $Path"
    }

    $entries = [ordered]@{}
    $lines = Get-Content -LiteralPath $Path
    $currentName = ""
    $entry = $null
    $inRequirements = $false

    foreach ($line in $lines) {
        if ($line -match '^#\s*(.*?)\s*\(ResearchDefinition\)') {
            $currentName = $Matches[1].Trim()
            continue
        }

        if ($line -match '^([A-Za-z0-9_]+):\s*$') {
            if ($entry -ne $null) {
                $entries[$entry.Id] = $entry
            }

            $entry = New-ResearchEntry -Id $Matches[1] -Name $currentName
            $inRequirements = $false
            continue
        }

        if ($entry -eq $null) {
            continue
        }

        if ($line -match '^\s+workHourToComplete:\s*(\S+)') {
            $entry.WorkHours = [double]$Matches[1]
            $inRequirements = $false
            continue
        }

        if ($line -match '^\s+researchType:\s*(\S+)') {
            $entry.ResearchType = $Matches[1]
            $inRequirements = $false
            continue
        }

        if ($line -match '^\s+researchSubType:\s*(\S+)') {
            $entry.ResearchSubType = $Matches[1]
            $inRequirements = $false
            continue
        }

        if ($line -match '^\s+stage:\s*(-?\d+)') {
            $entry.Stage = [int]$Matches[1]
            $inRequirements = $false
            continue
        }

        if ($line -match '^\s+subStage:\s*(-?\d+)') {
            $entry.SubStage = [int]$Matches[1]
            $inRequirements = $false
            continue
        }

        if ($line -match '^\s+requirementsResearch:\s*$') {
            $inRequirements = $true
            continue
        }

        if ($inRequirements -and $line -match '^\s+-\s+([A-Za-z0-9_]+)\s*$') {
            $entry.Requirements.Add($Matches[1]) | Out-Null
            continue
        }

        if ($line -match '^\s+\S') {
            $inRequirements = $false
        }
    }

    if ($entry -ne $null) {
        $entries[$entry.Id] = $entry
    }

    return $entries
}

function Get-PrerequisiteClosure {
    param(
        [hashtable]$Entries,
        [string]$ResearchId,
        [System.Collections.Generic.HashSet[string]]$Visiting = $null
    )

    if (-not $Entries.Contains($ResearchId)) {
        return [System.Collections.Generic.HashSet[string]]::new()
    }

    if ($Visiting -eq $null) {
        $Visiting = [System.Collections.Generic.HashSet[string]]::new()
    }

    if ($Visiting.Contains($ResearchId)) {
        throw "Cycle detected while reading prerequisites at $ResearchId"
    }

    $Visiting.Add($ResearchId) | Out-Null
    $closure = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($req in $Entries[$ResearchId].Requirements) {
        if (-not $Entries.Contains($req)) {
            continue
        }

        $closure.Add($req) | Out-Null
        $childClosure = Get-PrerequisiteClosure -Entries $Entries -ResearchId $req -Visiting $Visiting
        foreach ($child in $childClosure) {
            $closure.Add($child) | Out-Null
        }
    }

    $Visiting.Remove($ResearchId) | Out-Null
    return $closure
}

function Get-ScaledCost {
    param(
        [double]$VanillaHours,
        [double]$DepthMonths,
        [double]$Pivot,
        [double]$Power,
        [double]$Cap
    )

    if ($Pivot -le 0) {
        throw "PivotMonths must be greater than 0."
    }

    $multiplier = [Math]::Pow(1.0 + ($DepthMonths / $Pivot), $Power)
    if ($Cap -gt 0) {
        $multiplier = [Math]::Min($multiplier, $Cap)
    }

    [pscustomobject]@{
        Multiplier = $multiplier
        ScaledHours = $VanillaHours * $multiplier
    }
}

$entries = Parse-ResearchYaml -Path $ResearchYaml
if ($entries.Count -eq 0) {
    throw "No research entries parsed from $ResearchYaml"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$rows = foreach ($entry in $entries.Values) {
    $closure = @(Get-PrerequisiteClosure -Entries $entries -ResearchId $entry.Id)
    $depthHours = 0.0
    foreach ($req in $closure) {
        $depthHours += $entries[$req].WorkHours
    }

    $depthMonths = $depthHours / 720.0
    $scaled = Get-ScaledCost `
        -VanillaHours $entry.WorkHours `
        -DepthMonths $depthMonths `
        -Pivot $PivotMonths `
        -Power $Exponent `
        -Cap $MaxMultiplier

    [pscustomobject]@{
        Id = $entry.Id
        Name = $entry.Name
        ResearchType = $entry.ResearchType
        ResearchSubType = $entry.ResearchSubType
        Stage = $entry.Stage
        SubStage = $entry.SubStage
        PrerequisiteCount = $closure.Count
        DepthRpMonths = [Math]::Round($depthMonths, 3)
        VanillaRpMonths = [Math]::Round($entry.WorkHours / 720.0, 3)
        Multiplier = [Math]::Round($scaled.Multiplier, 6)
        ScaledRpMonths = [Math]::Round($scaled.ScaledHours / 720.0, 3)
        VanillaHours = [Math]::Round($entry.WorkHours, 3)
        ScaledHours = [Math]::Round($scaled.ScaledHours, 3)
        VanillaMonthsAtBase = [Math]::Round(($entry.WorkHours / 720.0) / $BaseResearchPointPerMonth, 3)
        ScaledMonthsAtBase = [Math]::Round(($scaled.ScaledHours / 720.0) / $BaseResearchPointPerMonth, 3)
    }
}

$reportPath = Join-Path $OutDir "research-cost-scaling-report.csv"
$rows | Sort-Object Id | Export-Csv -NoTypeInformation -LiteralPath $reportPath

if ($WriteOverrideYaml) {
    $overridePath = Join-Path $OutDir "research-cost-overrides.yaml"
    $overrideLines = [System.Collections.Generic.List[string]]::new()
    $overrideLines.Add("# Generated research cost overrides") | Out-Null
    $overrideLines.Add("# Formula: scaled = vanilla * min((1 + depthRpMonths / $PivotMonths) ^ $Exponent, $MaxMultiplier)") | Out-Null
    $overrideLines.Add("# Depth is the vanilla RP-month total of the prerequisite closure.") | Out-Null
    $overrideLines.Add("") | Out-Null

    foreach ($row in ($rows | Sort-Object Id)) {
        $overrideLines.Add("$($row.Id):") | Out-Null
        $overrideLines.Add("  workHourToComplete: $($row.ScaledHours)") | Out-Null
    }

    Set-Content -LiteralPath $overridePath -Value $overrideLines -Encoding UTF8
}

if ($entries.Contains($TargetResearchId)) {
    $targetClosure = @(Get-PrerequisiteClosure -Entries $entries -ResearchId $TargetResearchId)
    $targetIds = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($id in $targetClosure) {
        $targetIds.Add($id) | Out-Null
    }
    $targetIds.Add($TargetResearchId) | Out-Null

    $vanillaTargetHours = 0.0
    $scaledTargetHours = 0.0
    $rowById = @{}
    foreach ($row in $rows) {
        $rowById[$row.Id] = $row
    }

    foreach ($id in $targetIds) {
        $vanillaTargetHours += $entries[$id].WorkHours
        $scaledTargetHours += $rowById[$id].ScaledHours
    }

    $targetReportPath = Join-Path $OutDir "$TargetResearchId-queue.csv"
    $targetIds |
        ForEach-Object { $rowById[$_] } |
        Sort-Object DepthRpMonths, Id |
        Export-Csv -NoTypeInformation -LiteralPath $targetReportPath

    $summary = [pscustomobject]@{
        TargetResearchId = $TargetResearchId
        TargetName = $entries[$TargetResearchId].Name
        IncludedTechCount = $targetIds.Count
        VanillaQueueRpMonths = [Math]::Round($vanillaTargetHours / 720.0, 3)
        ScaledQueueRpMonths = [Math]::Round($scaledTargetHours / 720.0, 3)
        VanillaQueueMonthsAtBase = [Math]::Round(($vanillaTargetHours / 720.0) / $BaseResearchPointPerMonth, 3)
        ScaledQueueMonthsAtBase = [Math]::Round(($scaledTargetHours / 720.0) / $BaseResearchPointPerMonth, 3)
        ReportPath = (Resolve-Path -LiteralPath $reportPath).Path
        TargetQueuePath = (Resolve-Path -LiteralPath $targetReportPath).Path
    }

    $summary | Format-List
} else {
    Write-Warning "Target research id not found: $TargetResearchId"
    Write-Output "ReportPath: $((Resolve-Path -LiteralPath $reportPath).Path)"
}
