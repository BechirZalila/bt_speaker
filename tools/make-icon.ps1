<#
.SYNOPSIS
    Génère l'icône de l'application (src\BtBatteryTray\app.ico) : une pile verte aux trois quarts pleine.
    Chaque taille est dessinée séparément pour rester nette, puis stockée en PNG dans le fichier .ico.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$output = Join-Path $PSScriptRoot '..\src\BtBatteryTray\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = [Math]::Min(2 * $r, [Math]::Min($w, $h))
    if ($d -le 0) { $path.AddRectangle([System.Drawing.RectangleF]::new($x, $y, $w, $h)); return $path }
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$s) {
    $bmp = [System.Drawing.Bitmap]::new($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $dark = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0x2B, 0x2B, 0x2B))
    $green = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0x3C, 0xC8, 0x50))
    $empty = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0x55, 0x55, 0x55))

    # Corps de la pile (horizontal) et borne à droite.
    $bodyX = [Math]::Round($s * 0.04); $bodyW = [Math]::Round($s * 0.82)
    $bodyH = [Math]::Round($s * 0.56); $bodyY = [Math]::Round(($s - $bodyH) / 2)
    $body = New-RoundedRect $bodyX $bodyY $bodyW $bodyH ($s * 0.10)
    $g.FillPath($dark, $body)

    $tipW = [Math]::Max(1, [Math]::Round($s * 0.09)); $tipH = [Math]::Round($bodyH * 0.45)
    $tip = New-RoundedRect ($bodyX + $bodyW) ([Math]::Round(($s - $tipH) / 2)) $tipW $tipH ($s * 0.03)
    $g.FillPath($dark, $tip)

    # Intérieur : fond « vide » puis charge verte à 75 %.
    $pad = [Math]::Max(1, [Math]::Round($s * 0.07))
    $inX = $bodyX + $pad; $inY = $bodyY + $pad
    $inW = $bodyW - 2 * $pad; $inH = $bodyH - 2 * $pad
    $g.FillPath($empty, (New-RoundedRect $inX $inY $inW $inH ($s * 0.05)))
    $g.FillPath($green, (New-RoundedRect $inX $inY ([Math]::Round($inW * 0.75)) $inH ($s * 0.05)))

    $g.Dispose()
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# Format ICO : en-tête (6 octets) + une entrée de 16 octets par image + données PNG.
$images = foreach ($s in $sizes) { , (New-IconPng $s) }
$out = [System.IO.MemoryStream]::new()
$w = [System.IO.BinaryWriter]::new($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $output)).Path + '\app.ico', $out.ToArray())
Write-Host "Icône générée : $output"
