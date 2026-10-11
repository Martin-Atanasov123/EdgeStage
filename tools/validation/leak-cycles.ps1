# Part of EdgeStage's validation scripts (see docs/TESTING.md). Requires a Debug build of the
# solution (dotnet build) because it uses DEBUG-only test hooks; never moves the mouse or types.
# Leak check: repeated sidebar and Expose open/close plus window churn, over synthetic windows only.
# (--test-show-edge opens the sidebar exactly like the edge trigger; it closes by itself ~400 ms
# later because the real pointer is not at the edge. Expose is opened and closed with test commands.)
param([int]$Cycles = 100, [int]$ExposeCycles = 50, [int]$Churn = 30)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\guard.ps1"
Assert-NoFullscreenApp
$root  = (Resolve-Path "$PSScriptRoot\..\..").Path
$app   = "$root\src\EdgeStage\bin\Debug\net10.0-windows\EdgeStage.exe"
$spike = "$root\spike\ThumbnailSpike\bin\Debug\net10.0-windows\ThumbnailSpike.exe"
Add-Type -Namespace Cyc -Name N -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern System.IntPtr OpenProcess(uint access, bool inherit, uint pid);
[DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr h);
[DllImport("user32.dll")] public static extern uint GetGuiResources(System.IntPtr h, uint flags);
'@ -ErrorAction SilentlyContinue
function Snap($p, $label) {
    $p.Refresh()
    $h = [Cyc.N]::OpenProcess(0x1000, $false, [uint32]$p.Id)
    $o = [pscustomobject]@{ Stage = $label; Handles = $p.HandleCount; GDI = [Cyc.N]::GetGuiResources($h, 0); USER = [Cyc.N]::GetGuiResources($h, 1);
        PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); Threads = $p.Threads.Count }
    [void][Cyc.N]::CloseHandle($h)
    $o
}
function Cmd($name) { Start-Process $app -ArgumentList '--test-cmd', $name -Wait }

# The user's own settings are restored afterwards (V2 rewrites a V1 file on load).
$settings = Join-Path $env:APPDATA "EdgeStage\settings.json"
$settingsBackup = Join-Path $env:TEMP "edgestage-settings.backup.json"
if (Test-Path $settings) { Copy-Item $settings $settingsBackup -Force }
$v1Backup = Join-Path $env:APPDATA "EdgeStage\settings.v1.json"
$hadV1Backup = Test-Path $v1Backup

Get-Process EdgeStage, ThumbnailSpike -ErrorAction SilentlyContinue | Stop-Process -Force
$log = Join-Path $env:LOCALAPPDATA "EdgeStage\logs\edgestage-$(Get-Date -Format yyyyMMdd).log"
$logStart = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

$backdrop = Start-Process $spike -ArgumentList '--backdrop', '0', '0', '1920', '1080' -PassThru
Start-Sleep -Milliseconds 700
$sources = @(
    Start-Process $spike -ArgumentList '--source', '"Quarterly report.xlsx"', '#3CB371', 60, 60, 760, 480 -PassThru
    Start-Process $spike -ArgumentList '--source-flip', '"Live build output"', '#FF8C00', 900, 120, 900, 560 -PassThru
    Start-Process $spike -ArgumentList '--source', '"Inbox - Mail"', '#4682B4', 300, 560, 700, 420 -PassThru
    Start-Process $spike -ArgumentList '--source-min', '"Music player"', '#BA55D3', 500, 300, 600, 400 -PassThru
)
Start-Sleep -Seconds 3

$env:EDGESTAGE_TEST_ONLY_PROCESS = 'ThumbnailSpike'
$p = Start-Process $app -ArgumentList '--autostart' -PassThru
Start-Sleep -Seconds 4

# Warm-up so one-time allocations (first XAML load, icon cache, JIT) are not counted.
1..3 | ForEach-Object { Start-Process $app -ArgumentList '--test-show-edge' -Wait; Start-Sleep -Milliseconds 900 }
1..2 | ForEach-Object { Cmd 'ExposeShow'; Start-Sleep -Milliseconds 700; Cmd 'ExposeEscape'; Start-Sleep -Milliseconds 600 }
$results = @(Snap $p "after warm-up")

$sw = [Diagnostics.Stopwatch]::StartNew()
for ($i = 1; $i -le $Cycles; $i++) {
    Start-Process $app -ArgumentList '--test-show-edge' -Wait
    Start-Sleep -Milliseconds 800
    if ($i % 50 -eq 0) { $results += Snap $p "after $i sidebar open/close cycles ($([math]::Round($sw.Elapsed.TotalSeconds)) s)" }
}

$sw.Restart()
for ($i = 1; $i -le $ExposeCycles; $i++) {
    Cmd 'ExposeShow'
    Start-Sleep -Milliseconds 600
    Cmd 'ExposeEscape'
    Start-Sleep -Milliseconds 500
    if ($i % 25 -eq 0) { $results += Snap $p "after $i Expose open/close cycles ($([math]::Round($sw.Elapsed.TotalSeconds)) s)" }
}

for ($i = 0; $i -lt $Churn; $i++) {
    $s = Start-Process $spike -ArgumentList '--source', "`"Churn window $i`"", '#808080' -PassThru
    Start-Sleep -Milliseconds 500
    $s.Kill()
}
Start-Sleep -Seconds 1
$results += Snap $p "after $Churn windows opened+closed while hidden"
Start-Process $app -ArgumentList '--test-show-edge' -Wait
Start-Sleep -Milliseconds 900
Cmd 'ExposeShow'; Start-Sleep -Milliseconds 700; Cmd 'ExposeEscape'; Start-Sleep -Milliseconds 700
$results += Snap $p "after one more open/close of each (MRU pruned)"

$results | Format-Table -AutoSize | Out-String -Width 200
Start-Process $app -ArgumentList '--exit' -Wait
"exited: $($p.WaitForExit(5000))"
foreach ($s in $sources) { if (-not $s.HasExited) { $s.Kill() } }
if (-not $backdrop.HasExited) { $backdrop.Kill() }
if (Test-Path $settingsBackup) { Copy-Item $settingsBackup $settings -Force; Remove-Item $settingsBackup }
if (-not $hadV1Backup -and (Test-Path $v1Backup)) { Remove-Item $v1Backup }

$lines = Get-Content $log | Select-Object -Skip $logStart
"log: sidebar opened=$(($lines | Select-String 'Sidebar opened').Count) dismissed=$(($lines | Select-String 'Sidebar dismissed').Count); " +
    "Expose opened=$(($lines | Select-String 'opened on monitor').Count) dismissed=$(($lines | Select-String 'Expos.* dismissed').Count); errors=$(($lines | Select-String '\[ERROR\]').Count)"
$lines | Select-String 'Shutdown complete'
