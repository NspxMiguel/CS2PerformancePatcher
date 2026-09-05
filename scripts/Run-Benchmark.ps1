<#
.SYNOPSIS
    Runs the game's own benchmark once and files the result under a label.

.DESCRIPTION
    Cities: Skylines II ships a benchmark: a fixed camera path over a bundled city,
    90 seconds long (20s paused, 35s at speed 1, 35s at speed 3). It records per-frame
    CPU-game, CPU-render and GPU times and writes them to Benchmark.coc.

    That makes it a far better A/B bench than playing by hand: the camera path is
    identical every run, so two runs differ only by what we changed.

    This script drives one run end to end and copies the result somewhere it will
    survive the next run.

    Two things it handles that are easy to get wrong:

      * The game must be the foreground window for the whole run. Windows throttles
        background rendering and the game does not stream textures while it is behind
        another window, so a backgrounded run measures nothing. The script forces the
        window forward and keeps re-asserting it.

      * Launching Cities2.exe directly needs steam_appid.txt in the game folder, or
        the Steam platform service fails to initialise and the asset database dies
        with a null reference before reaching the menu.

.EXAMPLE
    .\Run-Benchmark.ps1 -Label baseline
    .\Run-Benchmark.ps1 -Label traffic -TuningProfile traffic
#>
[CmdletBinding()]
param(
    # Names the run. Becomes the result filename.
    [Parameter(Mandatory)][string]$Label,

    # Tuning profile to apply before the run. Omitted means run whatever is on disk.
    [string]$TuningProfile,

    # Extra settings to push past the profile, as Type.property=value. This is how a knob
    # gets measured on its own before anyone decides it belongs in a profile.
    [string[]]$Set = @(),

    # Restore the pristine settings before the run instead of applying a profile.
    [switch]$Revert,

    # How long to wait for the result before giving up. A run is 90s plus loading.
    [int]$TimeoutSec = 420
)

$ErrorActionPreference = 'Stop'

$gameDir  = 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
$exe      = Join-Path $gameDir 'Cities2.exe'
$userData = "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II"
$result   = Join-Path $userData 'Benchmark.coc'
$outDir   = "$env:USERPROFILE\CS2PerformancePatcher\.research\bench"
$patcher  = "$env:USERPROFILE\CS2PerformancePatcher\src\Cs2Patcher.Cli\bin\Release\net8.0-windows\cs2patch.exe"

Add-Type -Name Fg -Namespace Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool c);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
'@ -ErrorAction SilentlyContinue

function Stop-Game {
    # Killed rather than closed on purpose: the game rewrites Settings.coc on a clean
    # exit, which would undo the profile we are trying to measure.
    Get-Process Cities2 -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill(); $_.WaitForExit(15000) } catch { }
    }
}

function Set-GameForeground([IntPtr]$hwnd) {
    $fg = [Win32.Fg]::GetForegroundWindow()
    if ($fg -eq $hwnd) { return $true }
    $tidFg = 0
    [void][Win32.Fg]::GetWindowThreadProcessId($fg, [ref]$tidFg)
    $tidMe = [Win32.Fg]::GetCurrentThreadId()
    [void][Win32.Fg]::AttachThreadInput($tidFg, $tidMe, $true)
    [void][Win32.Fg]::ShowWindow($hwnd, 9)
    [void][Win32.Fg]::BringWindowToTop($hwnd)
    $ok = [Win32.Fg]::SetForegroundWindow($hwnd)
    [void][Win32.Fg]::AttachThreadInput($tidFg, $tidMe, $false)
    return $ok
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "== $Label ==" -ForegroundColor Cyan
Stop-Game

if ($Revert) {
    Write-Host '  reverting settings'
    & $patcher revert | Out-Null
} elseif ($TuningProfile) {
    $applyArgs = @('apply', $TuningProfile)
    foreach ($s in $Set) { $applyArgs += '--set'; $applyArgs += $s }

    $suffix = if ($Set.Count) { " + $($Set -join ' + ')" } else { '' }
    Write-Host "  applying profile '$TuningProfile'$suffix"

    $applied = & $patcher @applyArgs
    # A rejected override is worth seeing: it means the run measures something other than
    # what was asked for.
    $applied | Where-Object { $_ -match '^\s*!' } | ForEach-Object { Write-Warning $_.Trim() }
}

# Without this the game boots, fails to initialise the Steam platform service, and
# dies in AssetDatabase.PopulateFromDataSource. Costs nothing to keep in place.
if (-not (Test-Path (Join-Path $gameDir 'steam_appid.txt'))) {
    Set-Content -Path (Join-Path $gameDir 'steam_appid.txt') -Value '949230' -Encoding ascii -NoNewline
}

if (Test-Path $result) { Remove-Item $result -Force }
# Belt and braces: the flag goes on the command line, and in runOnce.txt so it still
# arrives if something else ends up spawning the process.
Set-Content -Path (Join-Path $userData 'runOnce.txt') -Value '--benchmark' -Encoding ascii -NoNewline

$started = Get-Date
Write-Host "  launching $($started.ToString('HH:mm:ss'))"
$proc = Start-Process -FilePath $exe -ArgumentList '--benchmark' -WorkingDirectory $gameDir -PassThru

$deadline = $started.AddSeconds($TimeoutSec)
$foregrounded = $false
$lastNudge = Get-Date

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3

    $proc.Refresh()
    if ($proc.HasExited) { throw "The game exited after $([int]((Get-Date) - $started).TotalSeconds)s without producing a result." }

    # Keep the window in front. Rendering is throttled and textures do not stream
    # while it is behind something else, so a backgrounded run is not a measurement.
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero -and ((Get-Date) - $lastNudge).TotalSeconds -ge 5) {
        $lastNudge = Get-Date
        if (Set-GameForeground $proc.MainWindowHandle) {
            if (-not $foregrounded) { Write-Host '  window brought to front'; $foregrounded = $true }
        }
    }

    if (Test-Path $result) {
        # Let the write settle before copying.
        Start-Sleep -Seconds 2
        $dest = Join-Path $outDir "Benchmark.$Label.coc"
        Copy-Item $result $dest -Force
        $elapsed = [int]((Get-Date) - $started).TotalSeconds
        Write-Host "  result after ${elapsed}s -> $dest" -ForegroundColor Green
        Stop-Game
        if (-not $foregrounded) {
            Write-Warning 'The window was never confirmed in the foreground. Treat this run as suspect.'
        }
        return
    }
}

Stop-Game
throw "No result within ${TimeoutSec}s."
