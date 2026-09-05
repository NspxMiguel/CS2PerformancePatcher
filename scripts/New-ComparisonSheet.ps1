# Builds one contact sheet from a set of before/after captures.
#
# The pairs come from two benchmark runs photographed at the same marks on the benchmark's own
# clock (see -ShotAt in Run-Benchmark.ps1). Same marks, same camera, so pair N of one run frames
# the same city as pair N of the other and the sheet is a like-for-like comparison rather than a
# selection of flattering angles.
#
# ASCII only, and no accented labels: this text is rasterised into a bitmap, so anything the
# chosen font lacks comes out as a box rather than as a character.

param(
    [Parameter(Mandatory = $true)][string] $BeforePrefix,
    [Parameter(Mandatory = $true)][string] $AfterPrefix,
    [Parameter(Mandatory = $true)][string] $Out,
    [string] $ShotDir     = '.research\bench\shots',
    [int[]]  $Indexes     = @(1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
    [int]    $Columns     = 2,
    [int]    $CellWidth   = 760,
    [string] $BeforeLabel = 'ANTES',
    [string] $AfterLabel  = 'DEPOIS',
    [string] $Title       = ''
)

Add-Type -AssemblyName System.Drawing

function Load-Bitmap([string] $path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    return [System.Drawing.Bitmap]::FromStream((New-Object System.IO.MemoryStream(, $bytes)))
}

# Resolve first, draw second: a missing capture should be an error before anything is allocated,
# not a hole two thirds of the way down a finished sheet.
$pairs = @()
foreach ($i in $Indexes) {
    $n = '{0:00}' -f $i
    $before = Join-Path $ShotDir "$BeforePrefix-$n.png"
    $after  = Join-Path $ShotDir "$AfterPrefix-$n.png"
    if (-not (Test-Path $before)) { throw "Missing capture: $before" }
    if (-not (Test-Path $after))  { throw "Missing capture: $after" }
    $pairs += [pscustomobject]@{ Index = $i; Before = $before; After = $after }
}

$probe = Load-Bitmap $pairs[0].Before
$aspect = $probe.Height / [double]$probe.Width
$probe.Dispose()

$barH   = 30
$paneH  = [int][Math]::Round($CellWidth * $aspect)
$cellH  = ($barH + $paneH) * 2 + 14      # two labelled panes plus a gutter under the pair
$rows   = [int][Math]::Ceiling($pairs.Count / [double]$Columns)
$titleH = if ($Title) { 54 } else { 0 }

$canvas = New-Object System.Drawing.Bitmap(($CellWidth * $Columns), ($titleH + $cellH * $rows))
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.Clear([System.Drawing.Color]::FromArgb(14, 16, 19))

$titleFont = New-Object System.Drawing.Font('Segoe UI Semibold', 19)
$labelFont = New-Object System.Drawing.Font('Segoe UI Semibold', 12)
$white     = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(238, 241, 245))
$redBar    = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(62, 34, 34))
$greenBar  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(26, 54, 40))

if ($Title) { $g.DrawString($Title, $titleFont, $white, 20, 14) }

for ($i = 0; $i -lt $pairs.Count; $i++) {
    $col = $i % $Columns
    $row = [int][Math]::Floor($i / $Columns)
    $x   = $col * $CellWidth
    $y   = $titleH + $row * $cellH

    foreach ($pane in @(
        @{ path = $pairs[$i].Before; text = "$BeforeLabel  -  $($pairs[$i].Index)"; bar = $redBar },
        @{ path = $pairs[$i].After;  text = "$AfterLabel  -  $($pairs[$i].Index)";  bar = $greenBar })) {

        $g.FillRectangle($pane.bar, $x, $y, $CellWidth, $barH)
        $g.DrawString($pane.text, $labelFont, $white, ($x + 12), ($y + 5))
        $y += $barH

        $bm = Load-Bitmap $pane.path
        $g.DrawImage($bm, (New-Object System.Drawing.Rectangle($x, $y, $CellWidth, $paneH)))
        $bm.Dispose()
        $y += $paneH
    }
}

$g.Dispose()
$canvas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()

Write-Host ("wrote {0}  ({1} pairs, {2}x{3})" -f $Out, $pairs.Count, $canvas.Width, $canvas.Height)
