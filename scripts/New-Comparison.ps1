# Builds a stacked before/after image from two benchmark captures.
#
# The benchmark runs its camera on a fixed path, so a capture taken at the same
# offset in two runs frames the same city -- but only roughly, because the path
# is frame-rate dependent and a faster run travels further per second. Every
# pair this script is pointed at has to be checked by eye first; the script
# does no matching of its own, it only stacks and labels.
#
# ASCII only. PowerShell 5.1 chokes on this file's parse if it is saved with
# anything else, and the labels are burned into a bitmap where accents would
# depend on the font anyway.

param(
    [Parameter(Mandatory = $true)][string] $Before,
    [Parameter(Mandatory = $true)][string] $After,
    [Parameter(Mandatory = $true)][string] $Out,
    [string] $BeforeLabel = 'ANTES  -  jogo original',
    [string] $AfterLabel  = 'DEPOIS  -  CS2 Performance Patcher',
    [string] $Caption     = '',
    [int]    $Width       = 1600
)

Add-Type -AssemblyName System.Drawing

function Load-Bitmap([string] $path) {
    # Read through a memory stream so the file handle is not held open, which
    # matters because these run out of a folder the benchmark keeps rewriting.
    $bytes  = [System.IO.File]::ReadAllBytes($path)
    $stream = New-Object System.IO.MemoryStream(,$bytes)
    return [System.Drawing.Bitmap]::FromStream($stream)
}

$src = @{ before = (Load-Bitmap $Before); after = (Load-Bitmap $After) }

$scale     = $Width / [double]$src.before.Width
$cellH     = [int][Math]::Round($src.before.Height * $scale)
$barH      = 46
$capH      = if ($Caption) { 40 } else { 0 }
$totalH    = ($barH + $cellH) * 2 + $capH

$canvas = New-Object System.Drawing.Bitmap($Width, $totalH)
$g      = [System.Drawing.Graphics]::FromImage($canvas)
$g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint  = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.Clear([System.Drawing.Color]::FromArgb(18, 20, 24))

$labelFont = New-Object System.Drawing.Font('Segoe UI Semibold', 15, [System.Drawing.FontStyle]::Regular)
$capFont   = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Regular)
$white     = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 238, 242))
$grey      = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 156, 166))
$redBar    = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(58, 34, 34))
$greenBar  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(28, 52, 40))

$y = 0
foreach ($pane in @(
    @{ img = $src.before; text = $BeforeLabel; bar = $redBar },
    @{ img = $src.after;  text = $AfterLabel;  bar = $greenBar })) {

    $g.FillRectangle($pane.bar, 0, $y, $Width, $barH)
    $g.DrawString($pane.text, $labelFont, $white, 18, ($y + 11))
    $y += $barH

    $g.DrawImage($pane.img, (New-Object System.Drawing.Rectangle(0, $y, $Width, $cellH)))
    $y += $cellH
}

if ($Caption) {
    $g.DrawString($Caption, $capFont, $grey, 18, ($y + 12))
}

$g.Dispose()
$canvas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
$src.before.Dispose()
$src.after.Dispose()

Write-Host ("wrote {0}  ({1}x{2})" -f $Out, $Width, $totalH)
