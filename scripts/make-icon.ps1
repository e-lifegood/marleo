# Generates assets/marleo.ico (16-256 px) and assets/marleo-256.png with System.Drawing.
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File scripts/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$assets = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded square with a diagonal gradient
    $pad = [Math]::Max(1, [int]($size * 0.04)); $r = [int]($size * 0.22)
    $rect = New-Object System.Drawing.Rectangle $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 47, 111, 235)), ([System.Drawing.Color]::FromArgb(255, 124, 58, 237)), 45.0
    $g.FillPath($brush, $path)

    # "M" + down arrow, the Markdown mark
    $white = [System.Drawing.Brushes]::White
    $s = $size / 256.0
    $stroke = [float](26 * $s)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $m = @(
        (New-Object System.Drawing.PointF ([float](52 * $s)), ([float](180 * $s))),
        (New-Object System.Drawing.PointF ([float](52 * $s)), ([float](80 * $s))),
        (New-Object System.Drawing.PointF ([float](96 * $s)), ([float](132 * $s))),
        (New-Object System.Drawing.PointF ([float](140 * $s)), ([float](80 * $s))),
        (New-Object System.Drawing.PointF ([float](140 * $s)), ([float](180 * $s)))
    )
    $g.DrawLines($pen, $m)
    # arrow shaft
    $g.DrawLine($pen, [float](192 * $s), [float](80 * $s), [float](192 * $s), [float](150 * $s))
    # arrow head
    $head = @(
        (New-Object System.Drawing.PointF ([float](160 * $s)), ([float](140 * $s))),
        (New-Object System.Drawing.PointF ([float](224 * $s)), ([float](140 * $s))),
        (New-Object System.Drawing.PointF ([float](192 * $s)), ([float](186 * $s)))
    )
    $g.FillPolygon($white, $head)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $bmp.Save((Join-Path $assets 'marleo-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $b = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'marleo.ico'), $out.ToArray())
Write-Host "assets/marleo.ico ($($out.Length) bytes)"
