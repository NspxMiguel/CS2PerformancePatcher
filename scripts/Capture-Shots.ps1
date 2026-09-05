<#
.SYNOPSIS
    Photographs a benchmark run so two profiles can be compared by eye, not just by fps.

.DESCRIPTION
    A profile that doubles the frame rate and turns the city into putty is not a win, and no
    amount of frame-time data will say so. This grabs the screen at fixed points in the
    benchmark's camera path; because the path is identical every run, shot N of one run frames
    the same view as shot N of another.

    Run it alongside Run-Benchmark.ps1. It waits for the benchmark city to finish loading --
    watching the game's own log rather than guessing at a delay, because load time varies by
    a minute or more -- then captures on a fixed cadence.

    Two requirements, both easy to miss:

      * The game must be in a window, not exclusive fullscreen, or the capture comes back
        black. Pass -Set GraphicsSettings.displayMode=FullscreenWindow to Run-Benchmark.ps1.
      * The window must stay in front. It has to be anyway for the run to measure anything.

.EXAMPLE
    Start-Job { .\Run-Benchmark.ps1 -Label potato -TuningProfile potato -Set GraphicsSettings.displayMode=FullscreenWindow }
    .\Capture-Shots.ps1 -Label potato
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Label,

    # Seconds after the city finishes loading. The benchmark is 20s paused, then 35s at
    # speed 1, then 35s at speed 3, so these land one in each phase plus an early one.
    [int[]]$AtSeconds = @(6, 18, 40, 75),

    [int]$TimeoutSec = 300
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$userData = "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II"
$sceneLog = Join-Path $userData 'Logs\SceneFlow.log'
$outDir = "$env:USERPROFILE\CS2PerformancePatcher\.research\bench\shots"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Save-Screen([string]$path) {
    # The whole virtual screen, so this works whichever monitor the game landed on.
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bitmap.Size)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

Add-Type -AssemblyName System.Windows.Forms

# Only lines written from here on count. The log is not truncated between runs, so an earlier
# run's "Loading completed" would otherwise fire this immediately.
$startedAt = Get-Date
$deadline = $startedAt.AddSeconds($TimeoutSec)
$seenBenchmarkLoad = $false

Write-Host "Waiting for the benchmark city to load..." -ForegroundColor Cyan

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if (-not (Test-Path $sceneLog)) { continue }

    # "Starting game from 'Benchmark ...'" then "Loading completed" is the pair that means the
    # camera path is about to run.
    $lines = Get-Content $sceneLog -Tail 40 -ErrorAction SilentlyContinue
    $startIndex = -1
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        if ($lines[$i] -match "Starting game from 'Benchmark") { $startIndex = $i; break }
    }
    if ($startIndex -lt 0) { continue }

    for ($i = $startIndex; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'Loading completed') { $seenBenchmarkLoad = $true; break }
    }
    if ($seenBenchmarkLoad) { break }
}

if (-not $seenBenchmarkLoad) { throw "The benchmark city never finished loading within ${TimeoutSec}s." }

$loadedAt = Get-Date
Write-Host "  loaded at $($loadedAt.ToString('HH:mm:ss')) -- capturing" -ForegroundColor Green

$shot = 0
foreach ($offset in $AtSeconds) {
    $due = $loadedAt.AddSeconds($offset)
    $wait = ($due - (Get-Date)).TotalMilliseconds
    if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }

    $shot++
    $path = Join-Path $outDir ("{0}-{1}.png" -f $Label, $shot)
    Save-Screen $path
    Write-Host ("  +{0,3}s -> {1}" -f $offset, (Split-Path $path -Leaf))
}
