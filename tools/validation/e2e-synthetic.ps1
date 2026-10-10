# Part of EdgeStage's validation scripts (see docs/TESTING.md). Requires a Debug build of the
# solution (dotnet build) because it uses DEBUG-only test hooks; never moves the mouse or types.
param([string]$Config = "Debug")
$ErrorActionPreference = 'Stop'
$root  = (Resolve-Path "$PSScriptRoot\..\..").Path
$spike = "$root\spike\ThumbnailSpike\bin\Debug\net10.0-windows\ThumbnailSpike.exe"
$app   = "$root\src\EdgeStage\bin\$Config\net10.0-windows\EdgeStage.exe"
$out   = Join-Path $root "artifacts\validation\e2e"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\*" -ErrorAction SilentlyContinue

Get-Process EdgeStage, ThumbnailSpike -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$log = Join-Path $env:LOCALAPPDATA "EdgeStage\logs\edgestage-$(Get-Date -Format yyyyMMdd).log"
$logStart = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

$sources = @{}
function Start-Source($mode, $title, $color) {
    $p = Start-Process $spike -ArgumentList $mode, "`"$title`"", $color -PassThru
    $script:sources[$title] = $p
}

$backdrop = Start-Process $spike -ArgumentList '--backdrop', '0', '0', '360', '1032' -PassThru
Start-Sleep -Milliseconds 600
Start-Source '--source'      'Quarterly report.xlsx'   '#3CB371'
Start-Source '--source-flip' 'Live build output'        '#FF8C00'
Start-Source '--source'      'Inbox - Mail'             '#4682B4'
Start-Source '--source-min'  'Music player'             '#BA55D3'
Start-Sleep -Seconds 3

$env:EDGESTAGE_TEST_ONLY_PROCESS = 'ThumbnailSpike'
$appProc = Start-Process $app -PassThru
Start-Sleep -Seconds 2
Start-Process $app -ArgumentList '--show' -Wait
Start-Sleep -Milliseconds 1000
Start-Process $spike -ArgumentList '--capture-window', '"EdgeStage Sidebar"', "`"$out\open`"", '3', '300' -Wait

# A window opens while the sidebar is visible.
Start-Source '--source' 'Newly opened window' '#CD5C5C'
Start-Sleep -Milliseconds 1500
Start-Process $spike -ArgumentList '--capture-window', '"EdgeStage Sidebar"', "`"$out\added`"", '1' -Wait

# A window closes while the sidebar is visible.
$sources['Inbox - Mail'].Kill()
Start-Sleep -Milliseconds 1200
Start-Process $spike -ArgumentList '--capture-window', '"EdgeStage Sidebar"', "`"$out\removed`"", '1' -Wait

# Activate the selected card (card #2 = the minimized "Music player") through the real code path.
Add-Type -Namespace E2E -Name W -MemberDefinition '[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow(); [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n); [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern System.IntPtr FindWindow(string c, string t); [DllImport("user32.dll")] public static extern bool IsIconic(System.IntPtr h); [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);' -ErrorAction SilentlyContinue
$music = [E2E.W]::FindWindow([NullString]::Value, 'Music player')
"Before activation: Music player minimized=$([E2E.W]::IsIconic($music))"
Start-Process $app -ArgumentList '--test-activate-selected' -Wait
Start-Sleep -Milliseconds 900
$sb = New-Object System.Text.StringBuilder 256
[void][E2E.W]::GetWindowText([E2E.W]::GetForegroundWindow(), $sb, 256)
$sidebar = [E2E.W]::FindWindow([NullString]::Value, 'EdgeStage Sidebar')
"After activation: foreground='$sb' musicMinimized=$([E2E.W]::IsIconic($music)) sidebarVisible=$([E2E.W]::IsWindowVisible($sidebar))"

# Edge-mode open must not take focus: the foreground window stays the same.
Start-Process $app -ArgumentList '--test-show-edge' -Wait
Start-Sleep -Milliseconds 900
$sb2 = New-Object System.Text.StringBuilder 256
[void][E2E.W]::GetWindowText([E2E.W]::GetForegroundWindow(), $sb2, 256)
"Edge-mode open: foreground='$sb2' (expected 'Music player') sidebarVisible=$([E2E.W]::IsWindowVisible($sidebar))"

# Settings window and the shortcut-conflict notice (screenshots for review).
Start-Process $app -ArgumentList '--test-open-settings' -Wait
Start-Sleep -Milliseconds 1200
Start-Process $spike -ArgumentList '--capture-window', '"EdgeStage settings"', "`"$out\settings`"", '1' -Wait
Start-Process $spike -ArgumentList '--capture-window', 'EdgeStage', "`"$out\notice`"", '1' -Wait

# Tray menu rendering (opened by a DEBUG hook at a fixed spot over a neutral backdrop).
$menuBackdrop = Start-Process $spike -ArgumentList '--backdrop', '780', '60', '320', '320' -PassThru
Start-Sleep -Milliseconds 700
Start-Process $app -ArgumentList '--test-tray-menu' -Wait
Start-Sleep -Milliseconds 700
Start-Process $spike -ArgumentList '--capture-rect', '790', '70', '300', '290', "`"$out\traymenu`"" -Wait
$menuBackdrop.Kill()

# Clean shutdown through the second-instance command.
Start-Process $app -ArgumentList '--exit' -Wait
$exited = $appProc.WaitForExit(5000)
"EdgeStage exited cleanly: $exited (exit code $(if ($exited) { $appProc.ExitCode } else { 'n/a' }))"

foreach ($p in $sources.Values) { if (-not $p.HasExited) { $p.Kill() } }
if (-not $backdrop.HasExited) { $backdrop.Kill() }

"--- capture notes ---"
Get-ChildItem "$out\*.txt" | ForEach-Object { Get-Content $_ }
"--- log (this run) ---"
Get-Content $log | Select-Object -Skip $logStart
