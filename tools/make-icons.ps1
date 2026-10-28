# Builds LoafLock's two tray icons into assets\ from Rukus (art\rukus.ico, the same file as
# b0r3d.org's imgs/rukus.ico). Run it again after changing the art or the lock:
#   powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1
#   -Look gold|red|bars   the locked look that goes into loaflock-locked.ico (LoafLock's is
#                         gold, picked 2026-10-27; red and bars stay for the -Sheet comparison)
#   -Sheet <file.png>     also draws every look side by side on a dark and a light taskbar
# rukus.ico packs its pictures as PNG, which .NET Framework's icon loader turns into static
# under 256 px, so these icons hold 16-64 px pictures as plain bitmaps, plus a 256 px PNG.
param(
    [ValidateSet('gold', 'red', 'bars')][string]$Look = 'gold',
    [string]$Sheet = '',
    [string]$OutDir = (Join-Path $PSScriptRoot '..\assets')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Sizes = 16, 20, 24, 28, 32, 40, 48, 64, 256
$Ink = [Drawing.ColorTranslator]::FromHtml('#1E1E22')    # outlines, keyholes, bars
$Metal = [Drawing.ColorTranslator]::FromHtml('#D5D9DF')  # the shackle, at 28 px and up
$Fills = @{ gold = '#F4C430'; red = '#E0393E' }
$Holes = @{ gold = '#1E1E22'; red = '#FFFFFF' }

# the padlock pixel by pixel for the small sizes, where a drawn shape turns to mush:
# o = outline, f = the lock's colour, k = keyhole, . = Rukus shows through
$PixelLocks = @{
    16 = '..ooo..', '.o...o.', '.o...o.', 'ooooooo', 'offfffo', 'offkffo', 'offkffo', 'offfffo', 'ooooooo'
    20 = '...ooo...', '..o...o..', '..o...o..', '..o...o..', 'ooooooooo', 'offfffffo', 'offfkfffo', 'offfkfffo', 'offfffffo', 'ooooooooo'
    24 = '...ooooo...', '..o.....o..', '..o.....o..', '..o.....o..', '..o.....o..', 'ooooooooooo', 'offfffffffo', 'offffkffffo', 'offffkffffo', 'offffkffffo', 'offfffffffo', 'ooooooooooo'
}

# rukus.ico's own pictures, by size
function Read-Frames([string]$path) {
    $b = [IO.File]::ReadAllBytes($path)
    $frames = @{}
    for ($i = 0; $i -lt [BitConverter]::ToUInt16($b, 4); $i++) {
        $o = 6 + 16 * $i
        $size = if ($b[$o] -eq 0) { 256 } else { [int]$b[$o] }
        $len = [int][BitConverter]::ToUInt32($b, $o + 8)
        $off = [int][BitConverter]::ToUInt32($b, $o + 12)
        if ($b[$off] -ne 0x89) { throw "the $size px picture in $path isn't a PNG; this script only reads PNG pictures" }
        $img = [Drawing.Image]::FromStream([IO.MemoryStream]::new($b, $off, $len))
        $frames[$size] = [Drawing.Bitmap]::new($img)
        $img.Dispose()
    }
    return $frames
}
$Frames = Read-Frames (Join-Path $PSScriptRoot '..\art\rukus.ico')
if (-not $Frames.ContainsKey(256)) { throw 'art\rukus.ico has no 256 px picture' }

# Rukus at $size: rukus.ico's own picture when it has that size, otherwise scaled down from 256
function New-Rukus([int]$size) {
    if ($Frames.ContainsKey($size)) { return [Drawing.Bitmap]::new($Frames[$size]) }
    $bmp = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $wrap = [Drawing.Imaging.ImageAttributes]::new()
    $wrap.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)  # no faint border pulled in from past the edge
    $g.DrawImage($Frames[256], [Drawing.Rectangle]::new(0, 0, $size, $size), 0, 0, 256, 256, [Drawing.GraphicsUnit]::Pixel, $wrap)
    $g.Dispose()
    return $bmp
}

function Add-PixelLock([Drawing.Bitmap]$bmp, [string[]]$rows, [Drawing.Color]$fill, [Drawing.Color]$hole) {
    $x0 = $bmp.Width - $rows[0].Length
    $y0 = $bmp.Height - $rows.Count
    for ($y = 0; $y -lt $rows.Count; $y++) {
        for ($x = 0; $x -lt $rows[$y].Length; $x++) {
            switch ($rows[$y][$x]) {
                'o' { $bmp.SetPixel($x0 + $x, $y0 + $y, $Ink) }
                'f' { $bmp.SetPixel($x0 + $x, $y0 + $y, $fill) }
                'k' { $bmp.SetPixel($x0 + $x, $y0 + $y, $hole) }
            }
        }
    }
}

# the padlock drawn as shapes, for 28 px and up, in the bottom-right corner
function Add-DrawnLock([Drawing.Bitmap]$bmp, [Drawing.Color]$fill, [Drawing.Color]$hole) {
    $S = $bmp.Width
    $bw = [math]::Round($S * 0.47)          # the lock's width
    $bodyH = [math]::Round($bw * 0.7)
    $ow = [math]::Max(1.0, $S / 28.0)       # outline
    $thick = [math]::Max(2.0, $bw * 0.16)   # shackle
    $shW = $bw * 0.56
    $x0 = $S - $bw
    $yBody = $S - $bodyH
    $sx = $x0 + ($bw - $shW) / 2
    $top = $yBody - $bw * 0.5 + $thick / 2 + $ow
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    $shackle = [Drawing.Drawing2D.GraphicsPath]::new()
    $shackle.AddLine([float]$sx, [float]($yBody + 1), [float]$sx, [float]($top + $shW / 2))
    $shackle.AddArc([float]$sx, [float]$top, [float]$shW, [float]$shW, 180, 180)
    $shackle.AddLine([float]($sx + $shW), [float]($top + $shW / 2), [float]($sx + $shW), [float]($yBody + 1))
    $g.DrawPath([Drawing.Pen]::new($Ink, [float]($thick + 2 * $ow)), $shackle)
    $g.DrawPath([Drawing.Pen]::new($Metal, [float]$thick), $shackle)

    $r = $bw * 0.14
    $bx = $x0 + $ow / 2; $by = $yBody + $ow / 2; $bwid = $bw - $ow; $bht = $bodyH - $ow
    $body = [Drawing.Drawing2D.GraphicsPath]::new()
    $body.AddArc([float]$bx, [float]$by, [float](2 * $r), [float](2 * $r), 180, 90)
    $body.AddArc([float]($bx + $bwid - 2 * $r), [float]$by, [float](2 * $r), [float](2 * $r), 270, 90)
    $body.AddArc([float]($bx + $bwid - 2 * $r), [float]($by + $bht - 2 * $r), [float](2 * $r), [float](2 * $r), 0, 90)
    $body.AddArc([float]$bx, [float]($by + $bht - 2 * $r), [float](2 * $r), [float](2 * $r), 90, 90)
    $body.CloseFigure()
    $g.FillPath([Drawing.SolidBrush]::new($fill), $body)
    $g.DrawPath([Drawing.Pen]::new($Ink, [float]$ow), $body)

    $cx = $x0 + $bw / 2; $cy = $yBody + $bodyH * 0.42; $kr = $bw * 0.09
    $brush = [Drawing.SolidBrush]::new($hole)
    $g.FillEllipse($brush, [float]($cx - $kr), [float]($cy - $kr), [float](2 * $kr), [float](2 * $kr))
    $g.FillRectangle($brush, [float]($cx - $kr * 0.5), [float]$cy, [float]$kr, [float]($bodyH * 0.3))
    $g.Dispose()
}

# three dark bars down Rukus, only where Rukus is
function Add-Bars([Drawing.Bitmap]$bmp) {
    $S = $bmp.Width
    $w = [math]::Max(1, [math]::Round($S / 14))
    $k = 0.85
    foreach ($c in 0.25, 0.5, 0.75) {
        $x1 = [int][math]::Round($S * $c - $w / 2)
        for ($x = $x1; $x -lt $x1 + $w; $x++) {
            for ($y = 0; $y -lt $S; $y++) {
                $p = $bmp.GetPixel($x, $y)
                if ($p.A -eq 0) { continue }
                $bmp.SetPixel($x, $y, [Drawing.Color]::FromArgb($p.A,
                    [int]($p.R * (1 - $k) + $Ink.R * $k), [int]($p.G * (1 - $k) + $Ink.G * $k), [int]($p.B * (1 - $k) + $Ink.B * $k)))
            }
        }
    }
}

# Rukus at $size with the given locked look ('' = unlocked)
function New-Frame([int]$size, [string]$look) {
    $bmp = New-Rukus $size
    if ($look -eq 'bars') { Add-Bars $bmp }
    elseif ($look) {
        $fill = [Drawing.ColorTranslator]::FromHtml($Fills[$look])
        $hole = [Drawing.ColorTranslator]::FromHtml($Holes[$look])
        if ($PixelLocks.ContainsKey($size)) { Add-PixelLock $bmp $PixelLocks[$size] $fill $hole }
        else { Add-DrawnLock $bmp $fill $hole }
    }
    return $bmp
}

# A 32-bit bitmap frame: header, bottom-up BGRA rows, then an all-clear AND mask.
function Bmp-Frame([Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $maskRow = [int]([math]::Ceiling($n / 32.0) * 4)
    $w.Write([UInt32]40); $w.Write([Int32]$n); $w.Write([Int32]($n * 2))
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $w.Write([UInt32]($n * $n * 4 + $maskRow * $n))
    $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    $data = $bmp.LockBits((New-Object Drawing.Rectangle 0, 0, $n, $n), 'ReadOnly', ([Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $row = New-Object byte[] ($n * 4)
    for ($y = $n - 1; $y -ge 0; $y--) {
        [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $n * 4)
        $w.Write($row)
    }
    $bmp.UnlockBits($data)
    $w.Write((New-Object byte[] ($maskRow * $n)))
    $w.Flush()
    return , $ms.ToArray()  # the comma keeps PowerShell from unrolling the bytes
}

function Png-Frame([Drawing.Bitmap]$bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

function Save-Icon([string]$path, [string]$look) {
    $frames = foreach ($s in $Sizes) {
        $bmp = New-Frame $s $look
        if ($s -ge 256) { , (Png-Frame $bmp) } else { , (Bmp-Frame $bmp) }
        $bmp.Dispose()
    }
    $fs = [IO.File]::Create($path)
    $w = New-Object IO.BinaryWriter $fs
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$Sizes.Count)
    $offset = 6 + 16 * $Sizes.Count
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $s = $Sizes[$i]
        $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]([byte[]]$frames[$i]).Length); $w.Write([UInt32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($f in $frames) { $w.Write([byte[]]$f) }
    $w.Close()
    "wrote $path"
}

# every look at 16, 20, 24 and 32 px (the taskbar corner at 100, 125, 150 and 200 %) on a dark
# and a light taskbar, plus 16 px and 32 px blown up
function Save-Sheet([string]$path) {
    $rows = @(
        @{ Label = 'Unlocked'; Look = '' },
        @{ Label = 'A. Gold padlock'; Look = 'gold' },
        @{ Label = 'B. Red padlock'; Look = 'red' },
        @{ Label = 'C. Behind bars'; Look = 'bars' }
    )
    $dark = [Drawing.ColorTranslator]::FromHtml('#202020')
    $light = [Drawing.ColorTranslator]::FromHtml('#EEEEEE')
    $sheet = [Drawing.Bitmap]::new(862, 56 + $rows.Count * 144 + 8)
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.Clear([Drawing.Color]::White)
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.InterpolationMode = 'NearestNeighbor'
    $g.PixelOffsetMode = 'Half'
    $font = [Drawing.Font]::new('Segoe UI', 13, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $small = [Drawing.Font]::new('Segoe UI', 11, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $text = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#222222'))
    $grey = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#666666'))
    $g.DrawString('Dark taskbar', $font, $text, 210, 12)
    $g.DrawString('100 / 125 / 150 / 200 %', $small, $grey, 210, 30)
    $g.DrawString('Light taskbar', $font, $text, 386, 12)
    $g.DrawString('100 / 125 / 150 / 200 %', $small, $grey, 386, 30)
    $g.DrawString('16 px, 8x', $font, $text, 570, 12)
    $g.DrawString('32 px, 4x', $font, $text, 714, 12)
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $y = 56 + $i * 144
        $g.DrawString($rows[$i].Label, $font, $text, 20, $y + 56)
        $pics = @{}
        foreach ($s in 16, 20, 24, 32) { $pics[$s] = New-Frame $s $rows[$i].Look }
        foreach ($strip in @(@{ X = 210; Bg = $dark }, @{ X = 386; Bg = $light })) {
            $g.FillRectangle([Drawing.SolidBrush]::new($strip.Bg), $strip.X, $y + 40, 160, 48)
            $x = $strip.X + 10
            foreach ($s in 16, 20, 24, 32) {
                $g.DrawImage($pics[$s], [int]$x, [int]($y + 40 + (48 - $s) / 2), [int]$s, [int]$s)
                $x += $s + 12
            }
        }
        $g.FillRectangle([Drawing.SolidBrush]::new($dark), 570, $y, 128, 128)
        $g.DrawImage($pics[16], 570, $y, 128, 128)
        $g.FillRectangle([Drawing.SolidBrush]::new($light), 714, $y, 128, 128)
        $g.DrawImage($pics[32], 714, $y, 128, 128)
        foreach ($p in $pics.Values) { $p.Dispose() }
    }
    $g.Dispose()
    $sheet.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    "wrote $path"
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Save-Icon (Join-Path $OutDir 'loaflock.ico') ''
Save-Icon (Join-Path $OutDir 'loaflock-locked.ico') $Look
if ($Sheet) { Save-Sheet $Sheet }
