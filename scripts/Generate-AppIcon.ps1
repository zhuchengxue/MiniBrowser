$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeIcon {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assetDir = Join-Path $repoRoot "src\MiniBrowser.App\Assets"
New-Item -ItemType Directory -Path $assetDir -Force | Out-Null

function New-IconBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $pad = [Math]::Max(2, [int]($Size * 0.07))
    $radius = [int]($Size * 0.22)
    $rect = New-Object System.Drawing.Rectangle $pad, $pad, ($Size - 2 * $pad), ($Size - 2 * $pad)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 18, 22, 31)), ([System.Drawing.Color]::FromArgb(255, 3, 8, 18)), 45
    $g.FillPath($bg, $path)

    $shineRect = New-Object System.Drawing.Rectangle ($rect.X + 2), ($rect.Y + 2), ($rect.Width - 4), ([int]($rect.Height * 0.52))
    $shine = New-Object System.Drawing.Drawing2D.LinearGradientBrush $shineRect, ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
    $g.SetClip($path)
    $g.FillEllipse($shine, $shineRect)
    $g.ResetClip()

    $borderPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(80, 255, 255, 255)), ([Math]::Max(1, $Size * 0.018))
    $g.DrawPath($borderPen, $path)

    $cx = $Size / 2
    $cy = $Size / 2
    $ringSize = $Size * 0.56
    $ringRect = New-Object System.Drawing.RectangleF (($cx - $ringSize / 2), ($cy - $ringSize / 2), $ringSize, $ringSize)
    $ringPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(230, 118, 214, 255)), ([Math]::Max(2, $Size * 0.055))
    $ringPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($ringPen, $ringRect, 42, 286)

    $innerPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(160, 255, 255, 255)), ([Math]::Max(1, $Size * 0.012))
    $g.DrawEllipse($innerPen, $ringRect)

    $needle = New-Object System.Drawing.Drawing2D.GraphicsPath
    $needle.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($cx + $Size * 0.08), ($cy - $Size * 0.32)),
        (New-Object System.Drawing.PointF ($cx + $Size * 0.17), ($cy + $Size * 0.16)),
        (New-Object System.Drawing.PointF ($cx - $Size * 0.08), ($cy + $Size * 0.05))
    ))
    $needleBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $ringRect, ([System.Drawing.Color]::White), ([System.Drawing.Color]::FromArgb(255, 124, 220, 255)), 45
    $g.FillPath($needleBrush, $needle)

    $dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $dotSize = [Math]::Max(2, $Size * 0.08)
    $g.FillEllipse($dotBrush, ($cx - $dotSize / 2), ($cy - $dotSize / 2), $dotSize, $dotSize)

    $g.Dispose()
    return $bitmap
}

function New-IconPngBytes {
    param([int]$Size)

    $bitmap = New-IconBitmap $Size
    try {
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $bitmap.Dispose()
    }
}

$pngPath = Join-Path $assetDir "AppIcon.png"
[System.IO.File]::WriteAllBytes($pngPath, (New-IconPngBytes 1024))

$icoPath = Join-Path $assetDir "App.ico"
$iconBitmap = New-IconBitmap 256
$handle = $iconBitmap.GetHicon()
try {
    $icon = [System.Drawing.Icon]::FromHandle($handle)
    $stream = [System.IO.File]::Create($icoPath)
    try {
        $icon.Save($stream)
    }
    finally {
        $stream.Dispose()
        $icon.Dispose()
    }
}
finally {
    [NativeIcon]::DestroyIcon($handle) | Out-Null
    $iconBitmap.Dispose()
}

Write-Output "Generated:"
Write-Output $pngPath
Write-Output $icoPath
