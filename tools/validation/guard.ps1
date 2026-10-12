# Shared by the validation scripts (dot-source it). Never prints titles of the user's own windows,
# refuses to start while a full-screen app (a game, a video) is in front, covers every monitor with
# an opaque backdrop for captures, and puts the user's EdgeStage and settings back afterwards.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace E2G -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(System.IntPtr h, int id, uint mods, uint vk);
[DllImport("user32.dll")] public static extern bool UnregisterHotKey(System.IntPtr h, int id);
public struct RECT { public int Left, Top, Right, Bottom; }
'@ -ErrorAction SilentlyContinue

# Titles of the synthetic windows these scripts create; anything else is reported anonymously.
$script:SyntheticTitles = @('Quarterly report.xlsx', 'Live build output', 'Inbox - Mail', 'Music player',
    'Design notes', 'Newly opened window', 'Second display window', 'Stubborn window')

function Get-SafeTitle([System.IntPtr]$hwnd) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][E2G.W]::GetWindowText($hwnd, $sb, 256)
    $t = "$sb"
    if ($script:SyntheticTitles -contains $t -or $t -like 'EdgeStage*') { $t } else { '(a non-test window)' }
}

function Assert-NoFullscreenApp {
    $fg = [E2G.W]::GetForegroundWindow()
    $r = New-Object E2G.W+RECT
    if ($fg -ne [System.IntPtr]::Zero -and [E2G.W]::GetWindowRect($fg, [ref]$r)) {
        foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
            $b = $s.Bounds
            if ($r.Left -le $b.Left -and $r.Top -le $b.Top -and $r.Right -ge $b.Right -and $r.Bottom -ge $b.Bottom) {
                throw "A full-screen app is in front of a monitor; not running the desktop tests now."
            }
        }
    }
}

# Opaque backdrops on every monitor (topmost), so captures never contain the user's windows.
function Start-Backdrops([string]$spike) {
    foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
        $b = $s.Bounds
        Start-Process $spike -ArgumentList '--backdrop', $b.X, $b.Y, $b.Width, $b.Height -PassThru
    }
}

# Work area of the non-primary monitor (for captures there), or $null.
function Get-SecondaryWorkArea {
    $s = [System.Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Primary } | Select-Object -First 1
    if ($s) { $s.WorkingArea } else { $null }
}

# Asks a running EdgeStage (the user's own copy) to exit normally; returns the paths to restart later.
function Stop-UserEdgeStage([string]$anyEdgeStageExe) {
    $running = @(Get-Process EdgeStage -ErrorAction SilentlyContinue)
    $paths = @($running | ForEach-Object { $_.Path } | Where-Object { $_ } | Select-Object -Unique)
    if ($running.Count -gt 0) {
        Start-Process $anyEdgeStageExe -ArgumentList '--exit' -Wait
        foreach ($p in $running) { if (-not $p.WaitForExit(5000)) { $p.Kill() } }
    }
    $paths
}

function Restart-UserEdgeStage([string[]]$paths, [string]$testRoot) {
    # Only restart copies that are not this repository's Debug/Release build outputs under test.
    foreach ($path in $paths) {
        if ($path -and -not $path.StartsWith("$testRoot\src\")) {
            Start-Process $path -ArgumentList '--autostart'
            "restarted the user's EdgeStage ($path)"
        }
    }
}

$script:SettingsFile = Join-Path $env:APPDATA "EdgeStage\settings.json"
$script:SettingsBackup = Join-Path $env:TEMP "edgestage-settings.backup.json"
$script:V1Backup = Join-Path $env:APPDATA "EdgeStage\settings.v1.json"

function Backup-UserSettings {
    $script:HadSettings = Test-Path $script:SettingsFile
    if ($script:HadSettings) { Copy-Item $script:SettingsFile $script:SettingsBackup -Force }
    $script:HadV1Backup = Test-Path $script:V1Backup
}

function Restore-UserSettings {
    if ($script:HadSettings) { Copy-Item $script:SettingsBackup $script:SettingsFile -Force; Remove-Item $script:SettingsBackup }
    elseif (Test-Path $script:SettingsFile) { Remove-Item $script:SettingsFile }
    if (-not $script:HadV1Backup -and (Test-Path $script:V1Backup)) { Remove-Item $script:V1Backup }
}

# Settings for a test run (after Backup-UserSettings): the real edge trigger is off so a pointer
# resting at a screen edge cannot open the sidebar in the middle of a scripted check. Tests open the
# sidebar "like the edge" through a DEBUG hook instead.
function Write-TestSettings([string]$theme = 'Dark', [string]$accent = 'System') {
    $json = @"
{
  "schemaVersion": 2,
  "sidebar": { "hotkey": "Ctrl+Alt+Space", "edgeActivationEnabled": false },
  "expose": { "monitors": "EveryDisplay" },
  "appearance": { "theme": "$theme", "accent": "$accent" },
  "firstRunTipShown": true
}
"@
    New-Item -ItemType Directory -Force (Split-Path $script:SettingsFile) | Out-Null
    Set-Content -Path $script:SettingsFile -Value $json -Encoding UTF8
}

# True when the global shortcut can NOT be registered right now (someone, e.g. EdgeStage, holds it).
function Test-HotkeyTaken([uint32]$mods, [uint32]$vk) {
    if ([E2G.W]::RegisterHotKey([System.IntPtr]::Zero, 0x7AB1, $mods, $vk)) {
        [void][E2G.W]::UnregisterHotKey([System.IntPtr]::Zero, 0x7AB1)
        return $false
    }
    return $true
}
