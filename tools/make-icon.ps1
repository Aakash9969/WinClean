# Generates src/WinClean.App/winclean.ico from WinClean's brand mark (dark rounded square,
# accent sparkle). Run from anywhere: powershell -File tools/make-icon.ps1

Add-Type -AssemblyName System.Drawing

$DarkBackground = [System.Drawing.Color]::FromArgb(255, 24, 24, 37)      # #181825
$BorderColor    = [System.Drawing.Color]::FromArgb(255, 69, 71, 90)      # #45475A
$Accent         = [System.Drawing.Color]::FromArgb(255, 137, 180, 250)   # #89B4FA

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-StarPoints([float]$scale, [float]$offsetX, [float]$offsetY) {
    # The 4-point sparkle used across the app UI, defined on a 24x24 grid.
    $coords = @(
        @(12.0, 0.8), @(14.5, 9.5), @(23.2, 12.0), @(14.5, 14.5),
        @(12.0, 23.2), @(9.5, 14.5), @(0.8, 12.0), @(9.5, 9.5)
    )
    $points = New-Object 'System.Drawing.PointF[]' $coords.Count
    for ($i = 0; $i -lt $coords.Count; $i++) {
        $points[$i] = [System.Drawing.PointF]::new(
            $offsetX + $coords[$i][0] * $scale,
            $offsetY + $coords[$i][1] * $scale)
    }
    return , $points
}

function New-IconBitmap([int]$size) {
    $unit = $size / 256.0
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        # Rounded-square background with a subtle border
        $radius = 56 * $unit
        $edge = [float](2 * $unit)
        $span = [float]($size - 4 * $unit)
        $path = New-RoundedRectPath $edge $edge $span $span $radius
        $backgroundBrush = [System.Drawing.SolidBrush]::new($DarkBackground)
        $borderPen = [System.Drawing.Pen]::new($BorderColor, [float](5 * $unit))
        $graphics.FillPath($backgroundBrush, $path)
        $graphics.DrawPath($borderPen, $path)

        # Main sparkle, centered
        $accentBrush = [System.Drawing.SolidBrush]::new($Accent)
        $graphics.FillPolygon($accentBrush, (New-StarPoints (8.0 * $unit) (32 * $unit) (32 * $unit)))

        # Small sparkle, top right
        $graphics.FillPolygon($accentBrush, (New-StarPoints (2.2 * $unit) (178 * $unit) (36 * $unit)))

        # Two accent dots
        $dot = [float](4.5 * $unit)
        $graphics.FillEllipse($accentBrush, (46 * $unit), (186 * $unit), $dot, $dot)
        $graphics.FillEllipse($accentBrush, (204 * $unit), (150 * $unit), $dot, $dot)
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

$root = Split-Path -Parent $PSScriptRoot
$sizes = @(16, 32, 48, 256)

$payloads = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $payloads += , ($stream.ToArray())
    $stream.Dispose()
    $bitmap.Dispose()
}

# ICO container: 6-byte header, one 16-byte directory entry per image, then the PNG blobs.
$out = New-Object System.IO.MemoryStream
$writer = [System.IO.BinaryWriter]::new($out)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)

$dataOffset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension)   # width
    $writer.Write([byte]$dimension)   # height
    $writer.Write([byte]0)            # palette color count
    $writer.Write([byte]0)            # reserved
    $writer.Write([uint16]1)           # color planes
    $writer.Write([uint16]32)          # bits per pixel
    $writer.Write([uint32]$payloads[$i].Length)
    $writer.Write([uint32]$dataOffset)
    $dataOffset += $payloads[$i].Length
}
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $writer.Write($payloads[$i])
}
$writer.Flush()

$icoPath = Join-Path $root "src\WinClean.App\winclean.ico"
[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
Write-Output "Icon written: $icoPath"

$previewDir = Join-Path $env:TEMP "opencode"
New-Item -ItemType Directory -Force -Path $previewDir | Out-Null
$previewPath = Join-Path $previewDir "winclean-icon-preview.png"
$preview = New-IconBitmap 256
$preview.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
Write-Output "Preview: $previewPath"
