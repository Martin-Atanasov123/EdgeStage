# Part of EdgeStage's validation scripts (see docs/TESTING.md). Requires a Debug build of the
# solution (dotnet build) because it uses DEBUG-only test hooks; never moves the mouse or types.
$ErrorActionPreference = 'Stop'
$root  = (Resolve-Path "$PSScriptRoot\..\..").Path
$spike = "$root\spike\ThumbnailSpike\bin\Debug\net10.0-windows\ThumbnailSpike.exe"
$app   = "$root\src\EdgeStage\bin\Debug\net10.0-windows\EdgeStage.exe"
$out   = Join-Path $root "artifacts\validation\e2e-titles"
New-Item -ItemType Directory -Force $out | Out-Null
Get-Process EdgeStage, ThumbnailSpike -ErrorAction SilentlyContinue | Stop-Process -Force
Copy-Item "$env:APPDATA\EdgeStage\settings.json" "$out\settings.backup.json" -Force

$procs = @()
$procs += Start-Process $spike -ArgumentList '--backdrop', '0', '0', '360', '1032' -PassThru
Start-Sleep -Milliseconds 500
$procs += Start-Process $spike -ArgumentList '--source', '"An extremely long window title that will certainly not fit on a single preview card - Quarterly planning workbook (final) (2).xlsx"', '#4682B4' -PassThru
$procs += Start-Process $spike -ArgumentList '--source-retitle', '"Terminal"', '#2E8B57' -PassThru
Start-Sleep -Seconds 2

$env:EDGESTAGE_TEST_ONLY_PROCESS = 'ThumbnailSpike'
$appProc = Start-Process $app -PassThru
Start-Sleep -Seconds 3
Start-Process $app -ArgumentList '--show' -Wait
Start-Sleep -Milliseconds 900
Start-Process $spike -ArgumentList '--capture-window', '"EdgeStage Sidebar"', "`"$out\t1`"" -Wait
Start-Sleep -Milliseconds 1300
Start-Process $spike -ArgumentList '--capture-window', '"EdgeStage Sidebar"', "`"$out\t2`"" -Wait

Start-Process $app -ArgumentList '--exit' -Wait
"exited: $($appProc.WaitForExit(5000))"
$procs | ForEach-Object { if (-not $_.HasExited) { $_.Kill() } }
Move-Item "$out\settings.backup.json" "$env:APPDATA\EdgeStage\settings.json" -Force
Get-ChildItem $out -Filter *.png | Select-Object Name
