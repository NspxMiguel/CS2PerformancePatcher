# Cuts the same rectangle out of two captures and puts them side by side at 1:1.
#
# Whole-frame comparisons are the wrong instrument for the two things people complain about most.
# Aliasing and texture detail live at the pixel, and a 1920-wide frame scaled to fit a chat window
# has already resampled both away -- an image can look identical at 40% and be visibly stepped in
# the game. So this crops rather than scales, and refuses to scale at all.
#
# ASCII only; the labels are rasterised.

param(
    [Parameter(Mandatory = $true)][string] $Left,
    [Parameter(Mandatory = $true)][string] $Right,
    [Parameter(Mandatory = $true)][string] $Out,

    # Region in the source image, in pixels. Defaults to a block just left of centre, which on
    # this benchmark's path is usually roofline against sky -- the worst case for stepped edges.
    [int] $X      = 700,
    [int] $Y      = 300,
    [int] $Width  = 480,
    [int] $Height = 360,

    # Whole-number magnification. 1 is honest; 2 makes an edge visible in a screenshot of a
    # screenshot. Nearest-neighbour, so magnifying never invents a smoother edge than there is.
    [ValidateRange(1, 4)][int] $Zoom = 2,

    [string] $LeftLabel  = 'ANTES',
    [string] $RightLabel = 'DEPOIS'
)

Add-Type -AssemblyName System.Drawing

function Load-Bitmap([string] $path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    return [System.Drawing.Bitmap]::FromStream((New-Object System.IO.MemoryStream(, $bytes)))
}

$sources = @((Load-Bitmap $Left), (Load-Bitmap $Right))

foreach ($s in $sources) {
    if ($X + $Width -gt $s.Width -or $Y + $Height -gt $s.Height) {
        throw "Crop ${X},${Y} ${Width}x${Height} falls outside a $($s.Width)x$($s.Height) capture."
    }
}

$barH  = 28
$cellW = $Width * $Zoom
$cellH = $Height * $Zoom

$canvas = New-Object System.Drawing.Bitmap(($cellW * 2 + 6), ($barH + $cellH))
$g = [System.Drawing.Graphics]::FromImage($canvas)

# Nearest neighbour on purpose. Any smoothing here would soften exactly the stair-stepping the
# picture is being taken to show.
$g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.TextRenderingHint  = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.Clear([System.Drawing.Color]::FromArgb(14, 16, 19))

$font  = New-Object System.Drawing.Font('Segoe UI Semibold', 12)
$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(238, 241, 245))
$bars  = @(
    (New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(62, 34, 34))),
    (New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(26, 54, 40))))

$labels = @($LeftLabel, $RightLabel)
$source = New-Object System.Drawing.Rectangle($X, $Y, $Width, $Height)

for ($i = 0; $i -lt 2; $i++) {
    $x = $i * ($cellW + 6)
    $g.FillRectangle($bars[$i], $x, 0, $cellW, $barH)
    $g.DrawString($labels[$i], $font, $white, ($x + 10), 4)
    $g.DrawImage($sources[$i],
        (New-Object System.Drawing.Rectangle($x, $barH, $cellW, $cellH)),
        $source, [System.Drawing.GraphicsUnit]::Pixel)
}

$g.Dispose()
$canvas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
foreach ($s in $sources) { $s.Dispose() }

Write-Host ("wrote {0}  ({1}x{2} crop at {3}x)" -f $Out, $Width, $Height, $Zoom)
