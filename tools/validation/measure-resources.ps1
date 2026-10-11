# Part of EdgeStage's validation scripts (see docs/TESTING.md). Works with any build:
#   powershell -File tools\validation\measure-resources.ps1 -Exe artifacts\SelfContained\EdgeStage.exe
# Resource measurements for EdgeStage. Read-only observation of the EdgeStage process.
param(
    [Parameter(Mandatory)] [string]$Exe,
    [int]$IdleSeconds = 60,
    [string]$Label = "build"
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\guard.ps1"
Assert-NoFullscreenApp
# The user's own settings are restored afterwards (V2 rewrites a V1 file on load).
$settings = Join-Path $env:APPDATA "EdgeStage\settings.json"
$settingsBackup = Join-Path $env:TEMP "edgestage-settings.backup.json"
if (Test-Path $settings) { Copy-Item $settings $settingsBackup -Force }
$v1Backup = Join-Path $env:APPDATA "EdgeStage\settings.v1.json"
$hadV1Backup = Test-Path $v1Backup
Add-Type -Namespace Meas -Name N -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern System.IntPtr OpenProcess(uint access, bool inherit, uint pid);
[DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr h);
[DllImport("user32.dll")] public static extern uint GetGuiResources(System.IntPtr h, uint flags);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern System.IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);
'@ -ErrorAction SilentlyContinue

function Snapshot($p) {
    $p.Refresh()
    $h = [Meas.N]::OpenProcess(0x1000, $false, [uint32]$p.Id)
    $gdi = [Meas.N]::GetGuiResources($h, 0); $user = [Meas.N]::GetGuiResources($h, 1)
    [void][Meas.N]::CloseHandle($h)
    [pscustomobject]@{
        WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1)
        PrivateMB    = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
        Handles      = $p.HandleCount
        Threads      = $p.Threads.Count
        GDI          = $gdi
        USER         = $user
        CpuSeconds   = [math]::Round($p.TotalProcessorTime.TotalSeconds, 3)
    }
}

Get-Process EdgeStage -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$p = Start-Process $Exe -ArgumentList '--autostart' -PassThru
Start-Sleep -Seconds 4
# Close the startup shortcut-conflict notice (this machine already uses Ctrl+Alt+Space), so the
# measurement reflects normal idle: nothing on screen.
$notice = [Meas.N]::FindWindow([NullString]::Value, 'EdgeStage')
if ($notice -ne [IntPtr]::Zero) { [void][Meas.N]::PostMessage($notice, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); "  (closed startup notice)" }
Start-Sleep -Seconds 6   # settle
$start = Snapshot $p
$t0 = Get-Date
Start-Sleep -Seconds $IdleSeconds
$end = Snapshot $p
$elapsed = ((Get-Date) - $t0).TotalSeconds
$cpu = $end.CpuSeconds - $start.CpuSeconds
"[$Label] idle (sidebar hidden, edge polling on) for $([math]::Round($elapsed,1)) s"
"  CPU time used: $([math]::Round($cpu*1000,1)) ms  =>  $([math]::Round(100*$cpu/$elapsed,3)) % of one core, $([math]::Round(100*$cpu/$elapsed/[Environment]::ProcessorCount,4)) % of total CPU"
"  start: $($start | ConvertTo-Json -Compress)"
"  end:   $($end | ConvertTo-Json -Compress)"

# Sidebar open and idle (no pointer movement): 15 s.
Start-Process $Exe -ArgumentList '--show' -Wait
Start-Sleep -Seconds 2
$o0 = Snapshot $p; $t1 = Get-Date
Start-Sleep -Seconds 15
$o1 = Snapshot $p
$el2 = ((Get-Date) - $t1).TotalSeconds; $cpu2 = $o1.CpuSeconds - $o0.CpuSeconds
"[$Label] sidebar OPEN and idle for $([math]::Round($el2,1)) s"
"  CPU time used: $([math]::Round($cpu2*1000,1)) ms  =>  $([math]::Round(100*$cpu2/$el2,3)) % of one core"
"  open:  $($o1 | ConvertTo-Json -Compress)"

# Expose open and idle (no pointer movement): 15 s. --expose toggles Expose in the running instance.
Start-Process $Exe -ArgumentList '--expose' -Wait
Start-Sleep -Seconds 2
$e0 = Snapshot $p; $t2 = Get-Date
Start-Sleep -Seconds 15
$e1 = Snapshot $p
$el3 = ((Get-Date) - $t2).TotalSeconds; $cpu3 = $e1.CpuSeconds - $e0.CpuSeconds
"[$Label] Expose OPEN and idle for $([math]::Round($el3,1)) s"
"  CPU time used: $([math]::Round($cpu3*1000,1)) ms  =>  $([math]::Round(100*$cpu3/$el3,3)) % of one core"
"  open:  $($e1 | ConvertTo-Json -Compress)"

# Closed again: idle cost must return to the hidden baseline.
Start-Process $Exe -ArgumentList '--expose' -Wait
Start-Sleep -Seconds 3
$h0 = Snapshot $p; $t3 = Get-Date
Start-Sleep -Seconds 20
$h1 = Snapshot $p
$el4 = ((Get-Date) - $t3).TotalSeconds; $cpu4 = $h1.CpuSeconds - $h0.CpuSeconds
"[$Label] hidden again after Expose, idle for $([math]::Round($el4,1)) s"
"  CPU time used: $([math]::Round($cpu4*1000,1)) ms  =>  $([math]::Round(100*$cpu4/$el4,3)) % of one core"
"  end:   $($h1 | ConvertTo-Json -Compress)"

Start-Process $Exe -ArgumentList '--exit' -Wait
"exited: $($p.WaitForExit(5000))"
if (Test-Path $settingsBackup) { Copy-Item $settingsBackup $settings -Force; Remove-Item $settingsBackup }
if (-not $hadV1Backup -and (Test-Path $v1Backup)) { Remove-Item $v1Backup }
