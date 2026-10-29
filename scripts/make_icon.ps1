# Draws the Garden Helper application icon (original artwork) and writes a multi-size .ico.
# Usage: pwsh scripts/make_icon.ps1 [-Out src/GardenHelper.App/Assets/GardenHelper.ico]
param([string]$Out = (Join-Path $PSScriptRoot "..\src\GardenHelper.App\Assets\GardenHelper.ico"))
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Rounded square, forest green
    $r = 56 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90); $path.AddArc(0, $size - $d, $d, $d, 90, 90); $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new(0, $size)), ([System.Drawing.Color]::FromArgb(255, 56, 110, 70)), ([System.Drawing.Color]::FromArgb(255, 31, 61, 43))
    $g.FillPath($bg, $path)

    # Leaf in ivory
    $leaf = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p0 = [System.Drawing.PointF]::new(70 * $s, 188 * $s); $p1 = [System.Drawing.PointF]::new(190 * $s, 66 * $s)
    $leaf.AddBezier($p0, [System.Drawing.PointF]::new(62 * $s, 104 * $s), [System.Drawing.PointF]::new(118 * $s, 62 * $s), $p1)
    $leaf.AddBezier($p1, [System.Drawing.PointF]::new(204 * $s, 138 * $s), [System.Drawing.PointF]::new(146 * $s, 196 * $s), $p0)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 247, 243, 232))), $leaf)

    # Vein and stem in green, small terracotta seed
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 47, 93, 58)), (9 * $s)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, 70 * $s, 188 * $s, 150 * $s, 108 * $s)
    $pen2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 247, 243, 232)), (9 * $s)
    $pen2.StartCap = 'Round'; $pen2.EndCap = 'Round'
    $g.DrawLine($pen2, 70 * $s, 188 * $s, 52 * $s, 208 * $s)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 196, 98, 58))), 168 * $s, 178 * $s, 30 * $s, 30 * $s)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{ Size = $sz; Data = $ms.ToArray() }
    $bmp.Dispose()
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$f.Data.Length); $bw.Write([uint32]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $bw.Write($f.Data) }
$bw.Close()
# Also keep a PNG for README / installer wizard use
$png = [System.IO.Path]::ChangeExtension($Out, "png")
[System.IO.File]::WriteAllBytes($png, ($frames | Where-Object Size -eq 256).Data)
Write-Host "Wrote $Out"
