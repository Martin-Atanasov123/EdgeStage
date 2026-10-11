# Shared by the validation scripts (dot-source it). Never prints titles of the user's own windows,
# and refuses to start while a full-screen app (a game, a video) is in front of the primary monitor.
Add-Type -Namespace E2G -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n);
public struct RECT { public int Left, Top, Right, Bottom; }
'@ -ErrorAction SilentlyContinue

# Titles of the synthetic windows these scripts create; anything else is reported anonymously.
$script:SyntheticTitles = @('Quarterly report.xlsx', 'Live build output', 'Inbox - Mail', 'Music player',
    'Design notes', 'Newly opened window')

function Get-SafeTitle([System.IntPtr]$hwnd) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][E2G.W]::GetWindowText($hwnd, $sb, 256)
    $t = "$sb"
    if ($script:SyntheticTitles -contains $t -or $t -like 'EdgeStage*') { $t } else { '(a non-test window)' }
}

function Assert-NoFullscreenApp {
    $fg = [E2G.W]::GetForegroundWindow()
    $r = New-Object E2G.W+RECT
    if ($fg -ne [System.IntPtr]::Zero -and [E2G.W]::GetWindowRect($fg, [ref]$r) `
        -and $r.Left -le 0 -and $r.Top -le 0 -and $r.Right -ge 1920 -and $r.Bottom -ge 1080) {
        throw "A full-screen app is in front of the primary monitor; not running the desktop tests now."
    }
}
