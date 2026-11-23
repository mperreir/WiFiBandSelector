# Génère assets/app.ico (icône de l'exécutable, du menu Démarrer et de l'installateur).
# Le dessin est volontairement identique à celui de l'icône de la zone de notification :
# un disque bleu et un glyphe Wi-Fi blanc, lisible dès 16 px.
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\assets\app.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $blue = [System.Drawing.Color]::FromArgb(255, 11, 87, 208)
    $background = New-Object System.Drawing.SolidBrush($blue)
    $inset = [Math]::Max(0.5, $size * 0.015)
    $graphics.FillEllipse($background, $inset, $inset, $size - 2 * $inset, $size - 2 * $inset)
    $background.Dispose()

    $white = [System.Drawing.Color]::White
    $penWidth = [Math]::Max(1.2, $size * 0.085)
    $pen = New-Object System.Drawing.Pen($white, $penWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $cx = $size * 0.5
    $cy = $size * 0.76

    foreach ($factor in @(0.18, 0.31, 0.44)) {
        $radius = $size * $factor
        $graphics.DrawArc($pen, $cx - $radius, $cy - $radius, 2 * $radius, 2 * $radius, 225, 90)
    }
    $pen.Dispose()

    $dotBrush = New-Object System.Drawing.SolidBrush($white)
    $dot = [Math]::Max(0.9, $size * 0.05)
    $graphics.FillEllipse($dotBrush, $cx - $dot, $cy - $dot, 2 * $dot, 2 * $dot)
    $dotBrush.Dispose()

    $graphics.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $pngs += , $stream.ToArray()
    $stream.Dispose()
}

$directory = Split-Path -Parent $Output
if (-not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$file = [System.IO.File]::Create($Output)
$writer = New-Object System.IO.BinaryWriter($file)

# ICONDIR
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $data = $pngs[$i]

    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$data.Length)
    $writer.Write([UInt32]$offset)

    $offset += $data.Length
}

foreach ($data in $pngs) {
    $writer.Write($data)
}

$writer.Flush()
$writer.Dispose()
$file.Dispose()

Write-Host "Icône écrite : $Output ($($sizes.Count) tailles, $((Get-Item $Output).Length) octets)"
