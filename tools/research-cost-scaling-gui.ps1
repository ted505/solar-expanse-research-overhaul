Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$scriptPath = Join-Path $PSScriptRoot "research-cost-scaling.ps1"
$defaultResearchYaml = "C:\Program Files (x86)\Steam\steamapps\common\Solar Expanse\BepInEx\plugins\Teddit\dump\research.yaml"
$defaultTedditResearchFacilities = "C:\Program Files (x86)\Steam\steamapps\common\Solar Expanse\BepInEx\plugins\Teddit\mods\ResearchFacilities"

function Add-Label {
    param(
        [System.Windows.Forms.Control]$Parent,
        [string]$Text,
        [int]$X,
        [int]$Y,
        [int]$W = 180
    )

    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.Location = New-Object System.Drawing.Point($X, $Y)
    $label.Size = New-Object System.Drawing.Size($W, 22)
    $Parent.Controls.Add($label)
    return $label
}

function Add-TextBox {
    param(
        [System.Windows.Forms.Control]$Parent,
        [string]$Text,
        [int]$X,
        [int]$Y,
        [int]$W = 180
    )

    $box = New-Object System.Windows.Forms.TextBox
    $box.Text = $Text
    $box.Location = New-Object System.Drawing.Point($X, $Y)
    $box.Size = New-Object System.Drawing.Size($W, 22)
    $Parent.Controls.Add($box)
    return $box
}

function Add-Button {
    param(
        [System.Windows.Forms.Control]$Parent,
        [string]$Text,
        [int]$X,
        [int]$Y,
        [int]$W = 120
    )

    $button = New-Object System.Windows.Forms.Button
    $button.Text = $Text
    $button.Location = New-Object System.Drawing.Point($X, $Y)
    $button.Size = New-Object System.Drawing.Size($W, 28)
    $Parent.Controls.Add($button)
    return $button
}

function Get-Number {
    param([System.Windows.Forms.TextBox]$Box, [string]$Name)

    $value = 0.0
    if (-not [double]::TryParse($Box.Text, [ref]$value)) {
        throw "$Name must be a number."
    }
    return $value
}

function Invoke-ScalingScript {
    param([switch]$WriteOverride)

    $pivot = Get-Number $pivotBox "Pivot months"
    $exponent = Get-Number $exponentBox "Exponent"
    $maxMultiplier = Get-Number $maxMultiplierBox "Max multiplier"
    $base = Get-Number $baseBox "Base RP/month"
    $target = $targetBox.Text.Trim()
    $researchYaml = $researchYamlBox.Text.Trim()
    $outDir = $outDirBox.Text.Trim()

    if ([string]::IsNullOrWhiteSpace($target)) {
        throw "Target research ID is required."
    }

    if ([string]::IsNullOrWhiteSpace($outDir)) {
        throw "Output directory is required."
    }

    $args = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $scriptPath,
        "-ResearchYaml", $researchYaml,
        "-OutDir", $outDir,
        "-BaseResearchPointPerMonth", $base.ToString([Globalization.CultureInfo]::InvariantCulture),
        "-PivotMonths", $pivot.ToString([Globalization.CultureInfo]::InvariantCulture),
        "-Exponent", $exponent.ToString([Globalization.CultureInfo]::InvariantCulture),
        "-MaxMultiplier", $maxMultiplier.ToString([Globalization.CultureInfo]::InvariantCulture),
        "-TargetResearchId", $target
    )

    if ($WriteOverride) {
        $args += "-WriteOverrideYaml"
    }

    $quotedArgs = ($args | ForEach-Object {
        $arg = [string]$_
        if ($arg -match '[\s"]') {
            '"' + $arg.Replace('"', '\"') + '"'
        } else {
            $arg
        }
    }) -join " "

    $process = Start-Process -FilePath "powershell" -ArgumentList $quotedArgs -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Scaling script failed with exit code $($process.ExitCode)."
    }

    return [pscustomobject]@{
        TargetQueueCsv = Join-Path $outDir "$target-queue.csv"
        FullReportCsv = Join-Path $outDir "research-cost-scaling-report.csv"
        OverrideYaml = Join-Path $outDir "research-cost-overrides.yaml"
    }
}

function Update-Preview {
    try {
        $paths = Invoke-ScalingScript
        if (-not (Test-Path -LiteralPath $paths.TargetQueueCsv)) {
            throw "Target queue CSV was not generated: $($paths.TargetQueueCsv)"
        }

        $rows = @(Import-Csv -LiteralPath $paths.TargetQueueCsv)
        $grid.DataSource = $rows

        $vanilla = ($rows | Measure-Object -Property VanillaRpMonths -Sum).Sum
        $scaled = ($rows | Measure-Object -Property ScaledRpMonths -Sum).Sum
        $base = Get-Number $baseBox "Base RP/month"
        $summaryLabel.Text = "Target queue: $([Math]::Round($vanilla / $base, 1)) base months vanilla -> $([Math]::Round($scaled / $base, 1)) base months scaled. Full report: $($paths.FullReportCsv)"
        $statusLabel.Text = "Preview updated."
    } catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Preview failed", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        $statusLabel.Text = "Preview failed."
    }
}

function Generate-Override {
    try {
        $paths = Invoke-ScalingScript -WriteOverride
        $statusLabel.Text = "Generated override: $($paths.OverrideYaml)"

        if ($installCheck.Checked) {
            $targetDir = $tedditModBox.Text.Trim()
            if ([string]::IsNullOrWhiteSpace($targetDir)) {
                throw "Teddit mod folder is required when install is checked."
            }
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
            Copy-Item -LiteralPath $paths.OverrideYaml -Destination (Join-Path $targetDir "research.yaml") -Force
            $statusLabel.Text = "Generated and installed: $(Join-Path $targetDir "research.yaml")"
        }

        Update-Preview
    } catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Generate failed", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        $statusLabel.Text = "Generate failed."
    }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Solar Expanse Research Cost Scaling"
$form.Size = New-Object System.Drawing.Size(1120, 760)
$form.StartPosition = "CenterScreen"

$font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Font = $font

Add-Label $form "Research dump YAML" 12 16 150 | Out-Null
$researchYamlBox = Add-TextBox $form $defaultResearchYaml 180 14 740
$browseResearchButton = Add-Button $form "Browse..." 930 12 90

Add-Label $form "Output directory" 12 48 150 | Out-Null
$outDirBox = Add-TextBox $form (Join-Path $repoRoot "research-scaling-output\gui") 180 46 740
$browseOutButton = Add-Button $form "Browse..." 930 44 90

Add-Label $form "Target research ID" 12 84 150 | Out-Null
$targetBox = Add-TextBox $form "research_sc_nike" 180 82 220

Add-Label $form "Base RP/month" 430 84 110 | Out-Null
$baseBox = Add-TextBox $form "100" 540 82 80

Add-Label $form "Pivot months" 12 120 150 | Out-Null
$pivotBox = Add-TextBox $form "8000" 180 118 100

Add-Label $form "Exponent" 310 120 80 | Out-Null
$exponentBox = Add-TextBox $form "1.25" 390 118 80

Add-Label $form "Max multiplier" 500 120 110 | Out-Null
$maxMultiplierBox = Add-TextBox $form "30" 610 118 80

$installCheck = New-Object System.Windows.Forms.CheckBox
$installCheck.Text = "Install generated override as Teddit ResearchFacilities\research.yaml"
$installCheck.Checked = $true
$installCheck.Location = New-Object System.Drawing.Point(12, 158)
$installCheck.Size = New-Object System.Drawing.Size(420, 24)
$form.Controls.Add($installCheck)

$tedditModBox = Add-TextBox $form $defaultTedditResearchFacilities 430 156 490
$browseTedditButton = Add-Button $form "Browse..." 930 154 90

$previewButton = Add-Button $form "Preview" 12 196 110
$generateButton = Add-Button $form "Generate Files" 132 196 130

$summaryLabel = New-Object System.Windows.Forms.Label
$summaryLabel.Text = "Preview not run yet."
$summaryLabel.Location = New-Object System.Drawing.Point(280, 200)
$summaryLabel.Size = New-Object System.Drawing.Size(800, 40)
$form.Controls.Add($summaryLabel)

$grid = New-Object System.Windows.Forms.DataGridView
$grid.Location = New-Object System.Drawing.Point(12, 248)
$grid.Size = New-Object System.Drawing.Size(1076, 420)
$grid.ReadOnly = $true
$grid.AllowUserToAddRows = $false
$grid.AllowUserToDeleteRows = $false
$grid.AutoSizeColumnsMode = "DisplayedCells"
$form.Controls.Add($grid)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = "Ready."
$statusLabel.Location = New-Object System.Drawing.Point(12, 682)
$statusLabel.Size = New-Object System.Drawing.Size(1076, 28)
$form.Controls.Add($statusLabel)

$browseResearchButton.Add_Click({
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Filter = "YAML files (*.yaml)|*.yaml|All files (*.*)|*.*"
    $dialog.FileName = $researchYamlBox.Text
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $researchYamlBox.Text = $dialog.FileName
    }
})

$browseOutButton.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.SelectedPath = $outDirBox.Text
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $outDirBox.Text = $dialog.SelectedPath
    }
})

$browseTedditButton.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.SelectedPath = $tedditModBox.Text
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $tedditModBox.Text = $dialog.SelectedPath
    }
})

$previewButton.Add_Click({ Update-Preview })
$generateButton.Add_Click({ Generate-Override })

[void]$form.ShowDialog()
