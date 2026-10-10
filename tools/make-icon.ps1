# Renders the EdgeStage icon (a "stage" with a strip of thumbnails beside it) at several sizes with
# WPF and writes a PNG-compressed multi-resolution .ico. Run from the repository root:
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Output = "src\EdgeStage\Assets\EdgeStage.ico")

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$ErrorActionPreference = 'Stop'

function New-Brush([string]$hex, [double]$opacity = 1.0) {
    $b = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex))
    $b.Opacity = $opacity
    $b.Freeze()
    return $b
}

function Render-Icon([int]$size) {
    $s = $size / 64.0
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()

    # Rounded charcoal tile.
    $bg = New-Object System.Windows.Media.LinearGradientBrush(
        [System.Windows.Media.ColorConverter]::ConvertFromString("#FF333844"),
        [System.Windows.Media.ColorConverter]::ConvertFromString("#FF17191E"), 90)
    $pen = New-Object System.Windows.Media.Pen((New-Brush "#40FFFFFF"), [Math]::Max(1, 1.2 * $s))
    $dc.DrawRoundedRectangle($bg, $pen, (New-Object System.Windows.Rect (2*$s), (2*$s), (60*$s), (60*$s)), 14*$s, 14*$s)

    # Sidebar strip: three thumbnails, the top one highlighted with the accent.
    $dc.DrawRoundedRectangle((New-Brush "#FF60CDFF"), $null, (New-Object System.Windows.Rect (10*$s), (14*$s), (13*$s), (10*$s)), 2.5*$s, 2.5*$s)
    $dc.DrawRoundedRectangle((New-Brush "#FFFFFFFF" 0.45), $null, (New-Object System.Windows.Rect (10*$s), (27*$s), (13*$s), (10*$s)), 2.5*$s, 2.5*$s)
    $dc.DrawRoundedRectangle((New-Brush "#FFFFFFFF" 0.28), $null, (New-Object System.Windows.Rect (10*$s), (40*$s), (13*$s), (10*$s)), 2.5*$s, 2.5*$s)

    # The stage: the active window.
    $dc.DrawRoundedRectangle((New-Brush "#FFEDEFF3" 0.95), $null, (New-Object System.Windows.Rect (27*$s), (14*$s), (27*$s), (36*$s)), 4*$s, 4*$s)
    $dc.DrawRoundedRectangle((New-Brush "#FF60CDFF" 0.85), $null, (New-Object System.Windows.Rect (27*$s), (14*$s), (27*$s), (6*$s)), 4*$s, 4*$s)
    $dc.Close()

    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @()
foreach ($size in $sizes) { $images += ,(Render-Icon $size) }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                   # ICONDIRENTRY
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()

$full = Join-Path (Get-Location) $Output
New-Item -ItemType Directory -Force (Split-Path $full) | Out-Null
[System.IO.File]::WriteAllBytes($full, $out.ToArray())
"Wrote $full ($($out.Length) bytes, sizes: $($sizes -join ', '))"

# Preview PNG for review.
[System.IO.File]::WriteAllBytes((Join-Path $env:TEMP "EdgeStage-icon-preview.png"), $images[-1])
