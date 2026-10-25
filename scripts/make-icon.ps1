<#
.SYNOPSIS
  Genera assets\Tilde.ico: una píldora negra mate con la virgulilla «~» de Tilde.
  Diseño propio, dibujado por código (no hay imágenes de terceros).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
#>
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\Tilde.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fondo: cuadrado muy redondeado, negro mate con un borde gris sutil (visible en barras oscuras).
    $inset = [Math]::Max(0.5, $s * 0.03)
    $w = $s - 2 * $inset
    $r = $w * 0.30
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($inset, $inset, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($inset + $w - 2 * $r, $inset, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($inset + $w - 2 * $r, $inset + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($inset, $inset + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 12, 12, 13))), $path)
    $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 64, 64, 70)), ([Math]::Max(1.0, $s / 32.0))
    $g.DrawPath($border, $path)

    # La virgulilla: dos curvas de Bézier encadenadas, trazo blanco con extremos redondos.
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 245, 245, 245)), ([single]($s * 0.115))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $p = { param($x, $y) New-Object System.Drawing.PointF ([single]($x * $s)), ([single]($y * $s)) }
    $pts = [System.Drawing.PointF[]]@(
        (& $p 0.25 0.58), (& $p 0.32 0.40), (& $p 0.43 0.41), (& $p 0.50 0.50),
        (& $p 0.57 0.59), (& $p 0.68 0.60), (& $p 0.75 0.42)
    )
    $g.DrawBeziers($pen, $pts)

    $g.Dispose()
    return $bmp
}

# 256 px se guarda como PNG; los tamaños pequeños como DIB clásico (máxima compatibilidad).
function ConvertTo-IconEntry([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $ms.ToArray()
    }
    $bw = New-Object System.IO.BinaryWriter $ms
    $maskStride = [int][Math]::Ceiling($s / 32.0) * 4
    # BITMAPINFOHEADER: la altura se duplica (imagen + máscara AND).
    $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]($s * $s * 4 + $maskStride * $s)); $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
    $data = $bmp.LockBits($rect, 'ReadOnly', ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $row = New-Object byte[] ($s * 4)
    for ($y = $s - 1; $y -ge 0; $y--) {   # Las filas de un DIB van de abajo arriba.
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $s * 4)
        $bw.Write($row)
    }
    $bmp.UnlockBits($data)
    $bw.Write((New-Object byte[] ($maskStride * $s)))   # Máscara vacía: manda el canal alfa.
    $bw.Flush()
    return , $ms.ToArray()
}

# Formato ICO: cabecera + directorio de entradas + datos de cada imagen.
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -eq 256) { $bmp.Save((Join-Path $env:TEMP 'tilde-icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    , (ConvertTo-IconEntry $bmp)
    $bmp.Dispose()
}
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "Icono generado: $out"
