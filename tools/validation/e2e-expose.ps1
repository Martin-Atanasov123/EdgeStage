# Part of EdgeStage's validation scripts (see docs/TESTING.md). Exercises Expose end to end with
# synthetic windows only: a full-screen opaque backdrop covers the primary monitor, EdgeStage lists
# only ThumbnailSpike windows (EDGESTAGE_TEST_ONLY_PROCESS), and only the backdrop area is captured.
# Requires a Debug build (DEBUG-only test commands). Never moves the mouse or types.
param([string]$Config = "Debug")
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\guard.ps1"
Assert-NoFullscreenApp
$root  = (Resolve-Path "$PSScriptRoot\..\..").Path
$spike = "$root\spike\ThumbnailSpike\bin\Debug\net10.0-windows\ThumbnailSpike.exe"
$app   = "$root\src\EdgeStage\bin\$Config\net10.0-windows\EdgeStage.exe"
$out   = Join-Path $root "artifacts\validation\expose"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\*" -ErrorAction SilentlyContinue

# The user's own settings are restored afterwards.
$settings = Join-Path $env:APPDATA "EdgeStage\settings.json"
$settingsBackup = Join-Path $out "settings.backup.json"
if (Test-Path $settings) { Copy-Item $settings $settingsBackup -Force }
$v1Backup = Join-Path $env:APPDATA "EdgeStage\settings.v1.json"
$hadV1Backup = Test-Path $v1Backup

Get-Process EdgeStage, ThumbnailSpike -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$log = Join-Path $env:LOCALAPPDATA "EdgeStage\logs\edgestage-$(Get-Date -Format yyyyMMdd).log"
$logStart = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

Add-Type -Namespace E2X -Name W -MemberDefinition '[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow(); [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n); [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern System.IntPtr FindWindow(string c, string t); [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h); [DllImport("user32.dll")] public static extern bool IsIconic(System.IntPtr h);' -ErrorAction SilentlyContinue
# Window title; this file stays ASCII because Windows PowerShell reads it as ANSI.
$ExposeTitle = 'EdgeStage Expos' + [char]0xE9
function Title($h) { Get-SafeTitle $h }
function Cmd($name) { Start-Process $app -ArgumentList '--test-cmd', $name -Wait }
function Shot($name, $frames = 1, $interval = 250) {
    # Work area of the primary monitor only (the taskbar row is never captured).
    Start-Process $spike -ArgumentList '--capture-rect', '0', '0', '1920', '1032', "`"$out\$name`"", $frames, $interval -Wait
}

$sources = @{}
function Start-Source($mode, $title, $color, $x, $y, $w, $h) {
    $p = Start-Process $spike -ArgumentList $mode, "`"$title`"", $color, $x, $y, $w, $h -PassThru
    $script:sources[$title] = $p
}

$backdrop = Start-Process $spike -ArgumentList '--backdrop', '0', '0', '1920', '1080' -PassThru
Start-Sleep -Milliseconds 700
Start-Source '--source'      'Quarterly report.xlsx' '#3CB371'   60   60  760 480
Start-Source '--source-flip' 'Live build output'     '#FF8C00'  900  120  900 560
Start-Source '--source'      'Inbox - Mail'          '#4682B4'  300  560  700 420
Start-Source '--source-min'  'Music player'          '#BA55D3'  500  300  600 400
Start-Source '--source'      'Design notes'          '#CD5C5C' 1200  650  600 360
Start-Sleep -Seconds 3

$env:EDGESTAGE_TEST_ONLY_PROCESS = 'ThumbnailSpike'
# Slow motion (durations / 0.3) so the captures catch the flight; frame timing is logged either way.
$env:EDGESTAGE_TEST_ANIMATION_SCALE = '0.3'
# --autostart: a shortcut conflict is reported by a quiet tray balloon instead of a window.
$appProc = Start-Process $app -ArgumentList '--autostart' -PassThru
Start-Sleep -Seconds 2


# 1. Open: capture the fly-in, then the settled grid.
Start-Process $app -ArgumentList '--test-cmd', 'ExposeShow'
Shot 'open-anim' 8 110
Start-Sleep -Milliseconds 500
Shot 'grid'
Cmd 'ExposeDescribe'
$expose = [E2X.W]::FindWindow([NullString]::Value, $ExposeTitle)
"Expose visible: $([E2X.W]::IsWindowVisible($expose)); foreground is Expose: $([E2X.W]::GetForegroundWindow() -eq $expose)"

# 2. Keyboard navigation (same code path as the arrow keys).
Cmd 'ExposeRight'; Cmd 'ExposeDescribe'
Cmd 'ExposeDown'; Cmd 'ExposeDescribe'
Cmd 'ExposeLeft'; Cmd 'ExposeDescribe'
Cmd 'ExposeUp'; Cmd 'ExposeDescribe'

# 3. Live changes while open.
Start-Source '--source' 'Newly opened window' '#DAA520' 1300 80 500 320
Start-Sleep -Milliseconds 1500
Shot 'added'
Cmd 'ExposeDescribe'
$sources['Inbox - Mail'].Kill()
Start-Sleep -Milliseconds 1200
Shot 'removed'
Cmd 'ExposeDescribe'

# 3b. Search (case-insensitive, title + app name), then Esc clears the search before closing anything.
Cmd 'ExposeTypeSearch'
Start-Sleep -Milliseconds 600
Shot 'search'
Cmd 'ExposeDescribe'
Cmd 'ExposeEscapeKey'
Start-Sleep -Milliseconds 600
Cmd 'ExposeDescribe'
"After first Esc: Expose visible=$([E2X.W]::IsWindowVisible($expose)) (expected True: Esc only cleared the search)"

# 3c. Group by app.
Cmd 'ExposeToggleGrouping'
Start-Sleep -Milliseconds 700
Shot 'grouped'
Cmd 'ExposeDescribe'
Cmd 'ExposeToggleGrouping'
Start-Sleep -Milliseconds 500

# 4. Esc: previews fly back and the overlay closes (the return to the previous window is checked in 5b,
#    where that window is a known test window).
Start-Process $app -ArgumentList '--test-cmd', 'ExposeEscape'
Shot 'close-anim' 7 100
Start-Sleep -Milliseconds 800
$after = [E2X.W]::GetForegroundWindow()
"After Esc: Expose visible=$([E2X.W]::IsWindowVisible($expose))"

# 5. Select the second card (via navigation) and switch to it.
Cmd 'ExposeShow'
Start-Sleep -Milliseconds 900
Cmd 'ExposeDescribe'
$music = [E2X.W]::FindWindow([NullString]::Value, 'Music player')
"Music player minimized before: $([E2X.W]::IsIconic($music))"
Cmd 'ExposeRight'; Cmd 'ExposeDescribe'
Start-Process $app -ArgumentList '--test-cmd', 'ExposeActivateSelected'
Shot 'select-anim' 7 100
Start-Sleep -Milliseconds 900
$fg = [E2X.W]::GetForegroundWindow()
$selected = Title $fg
"After selection: foreground='$selected' Expose visible=$([E2X.W]::IsWindowVisible($expose)) musicMinimized=$([E2X.W]::IsIconic($music))"

# 5b. Esc returns to the window that was active when Expose opened (the one just selected).
Cmd 'ExposeShow'
Start-Sleep -Milliseconds 1300
Cmd 'ExposeEscape'
Start-Sleep -Milliseconds 1300
$fg = [E2X.W]::GetForegroundWindow()
"Esc returns to previous: foreground='$(Title $fg)' (expected '$selected') Expose visible=$([E2X.W]::IsWindowVisible($expose))"

# 6. Opening Expose from the sidebar's header button.
Cmd 'SidebarExposeButton'
Start-Sleep -Milliseconds 1500
Shot 'from-sidebar'
Cmd 'ExposeDescribe'
$sidebar = [E2X.W]::FindWindow([NullString]::Value, 'EdgeStage Sidebar')
"From sidebar: Expose visible=$([E2X.W]::IsWindowVisible($expose)) sidebar visible=$([E2X.W]::IsWindowVisible($sidebar))"
Cmd 'ExposeEscape'
Start-Sleep -Milliseconds 800

# Clean shutdown.
Start-Process $app -ArgumentList '--exit' -Wait
$exited = $appProc.WaitForExit(5000)
"EdgeStage exited cleanly: $exited (exit code $(if ($exited) { $appProc.ExitCode } else { 'n/a' }))"

foreach ($p in $sources.Values) { if (-not $p.HasExited) { $p.Kill() } }
if (-not $backdrop.HasExited) { $backdrop.Kill() }
if (Test-Path $settingsBackup) { Copy-Item $settingsBackup $settings -Force; Remove-Item $settingsBackup }
if (-not $hadV1Backup -and (Test-Path $v1Backup)) { Remove-Item $v1Backup }

"--- capture notes ---"
Get-ChildItem "$out\*.txt" | ForEach-Object { Get-Content $_ }
"--- log (this run) ---"
Get-Content $log | Select-Object -Skip $logStart
