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

    $pad = [float]($Size * 0.085)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($Size - 2 * $pad), ($Size - 2 * $pad)
    $radius = [float]($Size * 0.235)
    $bgPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRectangle $bgPath $rect $radius

    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 22, 26, 34)), ([System.Drawing.Color]::FromArgb(255, 7, 10, 15)), 90
    $g.FillPath($bg, $bgPath)

    $borderPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(48, 255, 255, 255)), ([Math]::Max(1, $Size * 0.012))
    $g.DrawPath($borderPen, $bgPath)

    $glyphColor = [System.Drawing.Color]::FromArgb(238, 236, 241, 246)
    $accentColor = [System.Drawing.Color]::FromArgb(255, 98, 205, 255)
    $mutedColor = [System.Drawing.Color]::FromArgb(125, 236, 241, 246)

    $browserRect = New-Object System.Drawing.RectangleF ($Size * 0.255), ($Size * 0.318), ($Size * 0.49), ($Size * 0.35)
    $browserPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRectangle $browserPath $browserRect ([float]($Size * 0.065))
    $windowPen = New-Object System.Drawing.Pen $glyphColor, ([Math]::Max(2, $Size * 0.032))
    $windowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($windowPen, $browserPath)

    $barPen = New-Object System.Drawing.Pen $mutedColor, ([Math]::Max(1, $Size * 0.018))
    $barY = [float]($Size * 0.405)
    $g.DrawLine($barPen, ($Size * 0.315), $barY, ($Size * 0.685), $barY)

    $arcRect = New-Object System.Drawing.RectangleF ($Size * 0.365), ($Size * 0.45), ($Size * 0.27), ($Size * 0.18)
    $arcPen = New-Object System.Drawing.Pen $accentColor, ([Math]::Max(2, $Size * 0.03))
    $arcPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $arcPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($arcPen, $arcRect, 205, 130)

    $dotBrush = New-Object System.Drawing.SolidBrush $accentColor
    $dot = [float]($Size * 0.044)
    $g.FillEllipse($dotBrush, ($Size * 0.478), ($Size * 0.535), $dot, $dot)

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
