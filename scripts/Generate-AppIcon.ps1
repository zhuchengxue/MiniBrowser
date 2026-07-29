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

function Add-RoundedRectangle {
    param(
        [System.Drawing.Drawing2D.GraphicsPath]$Path,
        [System.Drawing.RectangleF]$Rect,
        [float]$Radius
    )

    $d = $Radius * 2
    $Path.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
    $Path.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
    $Path.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
    $Path.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
    $Path.CloseFigure()
}

function New-IconBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $pad = [float]($Size * 0.065)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($Size - 2 * $pad), ($Size - 2 * $pad)
    $bgPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRectangle $bgPath $rect ([float]($Size * 0.235))

    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 248, 249, 250))), $bgPath)

    $glyphBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 8, 12, 18))

    # Minimal browser mark: one rounded viewport, one address line, one search dot.
    $viewport = New-Object System.Drawing.RectangleF ([float]($Size * 0.20)), ([float]($Size * 0.295)), ([float]($Size * 0.60)), ([float]($Size * 0.42))
    $viewportPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRectangle $viewportPath $viewport ([float]($Size * 0.105))
    $windowPen = New-Object System.Drawing.Pen $glyphBrush, ([Math]::Max(2, $Size * 0.06))
    $windowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($windowPen, $viewportPath)

    $linePen = New-Object System.Drawing.Pen $glyphBrush, ([Math]::Max(2, $Size * 0.052))
    $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($linePen, ([float]($Size * 0.31)), ([float]($Size * 0.425)), ([float]($Size * 0.69)), ([float]($Size * 0.425)))

    $dotSize = [float]($Size * 0.095)
    $g.FillEllipse($glyphBrush, ([float]($Size * 0.452)), ([float]($Size * 0.545)), $dotSize, $dotSize)

    $g.Dispose()
    return $bitmap
}

function Save-Png {
    param([string]$Path, [int]$Size)
    $bitmap = New-IconBitmap $Size
    try {
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Save-Ico {
    param([string]$Path)
    $bitmap = New-IconBitmap 256
    $handle = $bitmap.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($handle)
        $stream = [System.IO.File]::Create($Path)
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
        $bitmap.Dispose()
    }
}

$pngPath = Join-Path $assetDir "AppIcon.png"
$icoPath = Join-Path $assetDir "App.ico"
Save-Png $pngPath 1024
Save-Ico $icoPath

Write-Output "Generated:"
Write-Output $pngPath
Write-Output $icoPath
