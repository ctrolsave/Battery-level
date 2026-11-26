# Dibuja el ícono de Battery Level (icono.ico, varios tamaños). Se corre una sola vez.
# Copyright (c) 2026 Ctrl + Save Design Studio. Todos los derechos reservados.
Add-Type -AssemblyName System.Drawing
Set-Location $PSScriptRoot

function Redondeado($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Cuadro($n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $n / 64.0
    # Fondo: cuadrado redondeado oscuro
    $fondo = Redondeado (1*$s) (1*$s) (62*$s) (62*$s) (14*$s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0,0)), ([System.Drawing.PointF]::new(0,$n)), ([System.Drawing.Color]::FromArgb(255,44,44,48)), ([System.Drawing.Color]::FromArgb(255,18,18,20))
    $g.FillPath($grad, $fondo)
    # Batería
    $grosor = [Math]::Max(1.5, 4*$s)
    $cuerpo = Redondeado (9*$s) (20*$s) (40*$s) (24*$s) (6*$s)
    $lapiz = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $grosor
    $g.DrawPath($lapiz, $cuerpo)
    $punta = Redondeado (51*$s) (27*$s) (5*$s) (10*$s) (2*$s)
    $g.FillPath([System.Drawing.Brushes]::White, $punta)
    $verde = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,68,214,44))
    $relleno = Redondeado (14*$s) (25*$s) (21*$s) (14*$s) (3*$s)
    $g.FillPath($verde, $relleno)
    $g.Dispose()
    return $bmp
}

$tamanos = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($t in $tamanos) {
    $b = Cuadro $t; $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); ,$ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$tamanos.Count)
$offset = 6 + 16 * $tamanos.Count
for ($i = 0; $i -lt $tamanos.Count; $i++) {
    $t = $tamanos[$i]; $lado = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$lado); $w.Write([byte]$lado); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icono.ico'), $out.ToArray())
$b = Cuadro 256; $b.Save((Join-Path $PSScriptRoot 'icono.png')); $b.Dispose()
'icono.ico listo'
