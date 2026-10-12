# Part of EdgeStage's validation scripts (see docs/TESTING.md). Captures the settings window pages,
# the sidebar and Expose in the light theme (and the settings pages in the dark theme), over opaque
# backdrops with synthetic windows only. Requires a Debug build. Never moves the mouse or types.
param([string]$Config = "Debug")
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\guard.ps1"
Assert-NoFullscreenApp
$root  = (Resolve-Path "$PSScriptRoot\..\..").Path
$spike = "$root\spike\ThumbnailSpike\bin\Debug\net10.0-windows\ThumbnailSpike.exe"
$app   = "$root\src\EdgeStage\bin\$Config\net10.0-windows\EdgeStage.exe"
$out   = Join-Path $root "artifacts\validation\appearance"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\*" -ErrorAction SilentlyContinue

Backup-UserSettings
$userCopies = Stop-UserEdgeStage $app
Get-Process ThumbnailSpike -ErrorAction SilentlyContinue | Stop-Process -Force
$log = Join-Path $env:LOCALAPPDATA "EdgeStage\logs\edgestage-$(Get-Date -Format yyyyMMdd).log"
$logStart = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

function Cmd($name) { Start-Process $app -ArgumentList '--test-cmd', $name -Wait }
function ShotWindow($title, $name) { Start-Process $spike -ArgumentList '--capture-window', "`"$title`"", "`"$out\$name`"", '1' -Wait }
function Write-Settings($theme, $accent) { Write-TestSettings $theme $accent }

$backdrops = @(Start-Backdrops $spike)
Start-Sleep -Milliseconds 800
$sources = @(
    Start-Process $spike -ArgumentList '--source', '"Quarterly report.xlsx"', '#3CB371', 60, 60, 760, 480 -PassThru
    Start-Process $spike -ArgumentList '--source', '"Inbox - Mail"', '#4682B4', 900, 120, 900, 560 -PassThru
    Start-Process $spike -ArgumentList '--source', '"Design notes"', '#CD5C5C', 1200, 650, 600, 360 -PassThru
)
Start-Sleep -Seconds 3
$env:EDGESTAGE_TEST_ONLY_PROCESS = 'ThumbnailSpike'

foreach ($run in @(@{ Theme = 'Light'; Accent = 'Purple' }, @{ Theme = 'Dark'; Accent = 'System' })) {
    $t = $run.Theme.ToLowerInvariant()
    Write-Settings $run.Theme $run.Accent
    $p = Start-Process $app -ArgumentList '--autostart' -PassThru
    Start-Sleep -Seconds 3
    foreach ($page in 'Appearance', 'Sidebar', 'Expose', 'HiddenApps', 'General') {
        Cmd "Settings$page"
        Start-Sleep -Milliseconds 700
        ShotWindow 'EdgeStage settings' "settings-$t-$($page.ToLowerInvariant())"
    }
    Cmd 'SettingsClose'
    Start-Sleep -Milliseconds 500
    if ($run.Theme -eq 'Light') {
        Start-Process $app -ArgumentList '--show' -Wait
        Start-Sleep -Milliseconds 1200
        ShotWindow 'EdgeStage Sidebar' 'sidebar-light'
        Cmd 'ExposeShow'
        Start-Sleep -Milliseconds 1000
        Start-Process $spike -ArgumentList '--capture-rect', '0', '0', '1920', '1032', "`"$out\expose-light`"" -Wait
        Cmd 'ExposeEscape'
        Start-Sleep -Milliseconds 800
    }
    Start-Process $app -ArgumentList '--exit' -Wait
    "[$($run.Theme)] exited cleanly: $($p.WaitForExit(5000))"
}

foreach ($s in $sources) { if (-not $s.HasExited) { $s.Kill() } }
foreach ($b in $backdrops) { if (-not $b.HasExited) { $b.Kill() } }
Restore-UserSettings
Restart-UserEdgeStage $userCopies $root

"--- captures ---"
Get-ChildItem "$out\*.png" | ForEach-Object { $_.Name }
"--- log (theme lines and errors) ---"
Get-Content $log | Select-Object -Skip $logStart | Select-String 'Theme:|ERROR|WARN|Settings (Loaded|Migrated)'
