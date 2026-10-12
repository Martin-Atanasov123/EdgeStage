# Part of EdgeStage's validation scripts (see docs/TESTING.md). The Expose selection transition for
# normal, maximized, minimized, minimized-while-maximized and other-monitor windows. Backdrops here are
# NOT topmost, so the synthetic windows sit above them and the captures show the hand-over from the
# preview to the real window; the user's windows stay beneath the backdrops. Requires a Debug build.
# Never moves the mouse or types.
param([string]$Config = "Debug")
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\guard.ps1"
Assert-NoFullscreenApp
$root  = (Resolve-Path "$PSScriptRoot\..\..").Path
$spike = "$root\spike\ThumbnailSpike\bin\Debug\net10.0-windows\ThumbnailSpike.exe"
$app   = "$root\src\EdgeStage\bin\$Config\net10.0-windows\EdgeStage.exe"
$out   = Join-Path $root "artifacts\validation\select"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\*" -ErrorAction SilentlyContinue

Backup-UserSettings
Write-TestSettings
$userCopies = Stop-UserEdgeStage $app
Get-Process ThumbnailSpike -ErrorAction SilentlyContinue | Stop-Process -Force
$log = Join-Path $env:LOCALAPPDATA "EdgeStage\logs\edgestage-$(Get-Date -Format yyyyMMdd).log"
$logStart = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

Add-Type -Namespace E2S -Name W -MemberDefinition '[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow(); [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern System.IntPtr FindWindow(string c, string t); [DllImport("user32.dll")] public static extern bool IsIconic(System.IntPtr h); [DllImport("user32.dll")] public static extern bool IsZoomed(System.IntPtr h); [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);' -ErrorAction SilentlyContinue
$ExposeTitle = 'EdgeStage Expos' + [char]0xE9
function Cmd($name) { Start-Process $app -ArgumentList '--test-cmd', $name -Wait }
$second = Get-SecondaryWorkArea

$backdrops = @(Start-LowBackdrops $spike)
Start-Sleep -Milliseconds 800
$sources = @(
    Start-Process $spike -ArgumentList '--source', '"Quarterly report.xlsx"', '#3CB371', 60, 60, 760, 480 -PassThru
    Start-Process $spike -ArgumentList '--source', '"Design notes"', '#CD5C5C', 1100, 560, 640, 400 -PassThru
    Start-Process $spike -ArgumentList '--source-max', '"Maximized window"', '#6A5ACD', 200, 200, 800, 500 -PassThru
    Start-Process $spike -ArgumentList '--source-min', '"Music player"', '#BA55D3', 500, 300, 600, 400 -PassThru
    Start-Process $spike -ArgumentList '--source-minmax', '"Minimized maximized"', '#DAA520', 300, 150, 700, 450 -PassThru
)
if ($second) { $sources += Start-Process $spike -ArgumentList '--source', '"Second display window"', '#20B2AA', ($second.X + 400), ($second.Y + 150), 900, 520 -PassThru }
Start-Sleep -Seconds 3

$env:EDGESTAGE_TEST_ONLY_PROCESS = 'ThumbnailSpike'
$env:EDGESTAGE_TEST_ANIMATION_SCALE = '0.3'   # slow motion for the captures (280 ms -> 933 ms)
$appProc = Start-Process $app -ArgumentList '--autostart' -PassThru
Start-Sleep -Seconds 2

function Select-Case($name, $selectCmd, $title, [switch]$OtherDisplay) {
    Cmd 'ExposeShow'
    Start-Sleep -Milliseconds 1300
    Cmd $selectCmd
    $h = [E2S.W]::FindWindow([NullString]::Value, $title)
    $wasMin = [E2S.W]::IsIconic($h)
    Start-Process $app -ArgumentList '--test-cmd', 'ExposeActivateSelected'
    if ($OtherDisplay) {
        Start-Process $spike -ArgumentList '--capture-rect', $second.X, $second.Y, $second.Width, $second.Height, "`"$out\$name`"", 16, 70 -Wait
    } else {
        Start-Process $spike -ArgumentList '--capture-rect', '0', '0', '1920', '1032', "`"$out\$name`"", 16, 70 -Wait
    }
    Start-Sleep -Milliseconds 600
    $fg = [E2S.W]::GetForegroundWindow()
    "[$name] '$title' minimized before=$wasMin -> foreground='$(Get-SafeTitle $fg)' isTarget=$($fg -eq $h) minimized=$([E2S.W]::IsIconic($h)) maximized=$([E2S.W]::IsZoomed($h)) exposeVisible=$([E2S.W]::IsWindowVisible([E2S.W]::FindWindow([NullString]::Value, $ExposeTitle)))"
    Start-Sleep -Milliseconds 500
}

Select-Case 'normal' 'ExposeSelectDesign' 'Design notes'
Select-Case 'maximized' 'ExposeSelectMaximized' 'Maximized window'
Select-Case 'minimized' 'ExposeSelectMusic' 'Music player'
Select-Case 'minimized-maximized' 'ExposeSelectMinMax' 'Minimized maximized'
if ($second) { Select-Case 'other-display' 'ExposeSelectOtherDisplay' 'Second display window' -OtherDisplay }

# Esc: back to the window that was active before (the one just chosen).
$before = [E2S.W]::GetForegroundWindow()
Cmd 'ExposeShow'
Start-Sleep -Milliseconds 1300
Start-Process $app -ArgumentList '--test-cmd', 'ExposeEscape'
Start-Process $spike -ArgumentList '--capture-rect', '0', '0', '1920', '1032', "`"$out\escape`"", 16, 70 -Wait
Start-Sleep -Milliseconds 600
"[escape] foreground restored: $([E2S.W]::GetForegroundWindow() -eq $before) ('$(Get-SafeTitle ([E2S.W]::GetForegroundWindow()))')"

Start-Process $app -ArgumentList '--exit' -Wait
"EdgeStage exited cleanly: $($appProc.WaitForExit(5000))"
foreach ($p in $sources) { if (-not $p.HasExited) { $p.Kill() } }
foreach ($b in $backdrops) { if (-not $b.HasExited) { $b.Kill() } }
Restore-UserSettings
Restart-UserEdgeStage $userCopies $root

"--- log (landing precision, animation, activation) ---"
Get-Content $log | Select-Object -Skip $logStart | Select-String 'landing|close animation|refused|Could not|ERROR|closed meanwhile' | ForEach-Object { $_.Line }
