# Part of EdgeStage's validation scripts (see docs/TESTING.md). Requires a Debug build of the
# solution (dotnet build) because it uses DEBUG-only test hooks; never moves the mouse or types.
# Leak check: repeated sidebar open/close and window churn. Uses the DEBUG build's test hook
# (--test-show-edge opens the sidebar exactly like the edge trigger; it closes by itself ~400 ms
# later because the real pointer is not at the edge).
param([int]$Cycles = 100, [int]$Churn = 30)
$ErrorActionPreference = 'Stop'
$root  = (Resolve-Path "$PSScriptRoot\..\..").Path
$app   = "$root\src\EdgeStage\bin\Debug\net10.0-windows\EdgeStage.exe"
$spike = "$root\spike\ThumbnailSpike\bin\Debug\net10.0-windows\ThumbnailSpike.exe"
Add-Type -Namespace Cyc -Name N -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern System.IntPtr OpenProcess(uint access, bool inherit, uint pid);
[DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr h);
[DllImport("user32.dll")] public static extern uint GetGuiResources(System.IntPtr h, uint flags);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern System.IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);
'@ -ErrorAction SilentlyContinue
function Snap($p, $label) {
    $p.Refresh()
    $h = [Cyc.N]::OpenProcess(0x1000, $false, [uint32]$p.Id)
    $o = [pscustomobject]@{ Stage = $label; Handles = $p.HandleCount; GDI = [Cyc.N]::GetGuiResources($h, 0); USER = [Cyc.N]::GetGuiResources($h, 1);
        PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); Threads = $p.Threads.Count }
    [void][Cyc.N]::CloseHandle($h)
    $o
}

Get-Process EdgeStage, ThumbnailSpike -ErrorAction SilentlyContinue | Stop-Process -Force
$log = Join-Path $env:LOCALAPPDATA "EdgeStage\logs\edgestage-$(Get-Date -Format yyyyMMdd).log"
$logStart = (Get-Content $log).Count
$p = Start-Process $app -PassThru
Start-Sleep -Seconds 4
$notice = [Cyc.N]::FindWindow([NullString]::Value, 'EdgeStage')
if ($notice -ne [IntPtr]::Zero) { [void][Cyc.N]::PostMessage($notice, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) }
Start-Sleep -Seconds 2

# Warm-up cycles so one-time allocations (first XAML load, icon cache, JIT) are not counted.
1..3 | ForEach-Object { Start-Process $app -ArgumentList '--test-show-edge' -Wait; Start-Sleep -Milliseconds 900 }
$results = @(Snap $p "after warm-up")

$sw = [Diagnostics.Stopwatch]::StartNew()
for ($i = 1; $i -le $Cycles; $i++) {
    Start-Process $app -ArgumentList '--test-show-edge' -Wait
    Start-Sleep -Milliseconds 800
    if ($i % 50 -eq 0) { $results += Snap $p "after $i open/close cycles ($([math]::Round($sw.Elapsed.TotalSeconds)) s)" }
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
$results += Snap $p "after one more open/close (MRU pruned)"

$results | Format-Table -AutoSize | Out-String -Width 200
Start-Process $app -ArgumentList '--exit' -Wait
"exited: $($p.WaitForExit(5000))"
$lines = Get-Content $log | Select-Object -Skip $logStart
"log: opened=$(($lines | Select-String 'Sidebar opened').Count) dismissed=$(($lines | Select-String 'Sidebar dismissed').Count) errors=$(($lines | Select-String '\[ERROR\]').Count)"
$lines | Select-String 'Shutdown complete'
