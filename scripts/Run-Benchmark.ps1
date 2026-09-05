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

    # Cs2Saver preset to write before the run. The mod reads its own .coc on load, so this
    # is how the mod gets A/B tested without anyone opening its options page.
    [ValidateSet('Off', 'Balanced', 'TrafficFocus', 'Aggressive', 'Declutter', 'DeclutterMax', 'TreesOnly', 'PropsOnly')]
    [string]$ModPreset,

    # Cs2Saver colour grading preset. Same mechanism as -ModPreset.
    [ValidateSet('Off', 'Vivid', 'Toybox', 'Miniature', 'Showroom', 'Cel')]
    [string]$ModLook,

    # Cs2Saver material restyle. Same mechanism again.
    [ValidateSet('Off', 'Matte', 'Painted')]
    [string]$ModSurface,

    # Cs2Saver: stop computing skeletal animation. Rendering work, not simulation.
    [ValidateSet('true', 'false')]
    [string]$ModStopAnimating,

    # How long to wait for the result before giving up. A run is 90s plus loading.
    [int]$TimeoutSec = 420,

    # Refuse to measure if the machine is already this busy before the game even starts,
    # as a percentage of the whole machine.
    [int]$MaxHostLoadPercent = 20,

    # Measure anyway. For when you know what else is running and only care about GPU numbers.
    [switch]$IgnoreHostLoad
)

$ErrorActionPreference = 'Stop'

$gameDir  = 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
$exe      = Join-Path $gameDir 'Cities2.exe'
$userData = "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II"
$result   = Join-Path $userData 'Benchmark.coc'
$outDir   = "$env:USERPROFILE\CS2PerformancePatcher\.research\bench"
$patcher  = "$env:USERPROFILE\CS2PerformancePatcher\src\Cs2Patcher.Cli\bin\Release\net8.0-windows\cs2patch.exe"

# try/catch rather than -ErrorAction SilentlyContinue: the latter hides the message but still
# leaves the script exiting non-zero, which makes a good run look like a failed one.
try {
Add-Type -Name Fg -Namespace Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool c);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
'@
} catch { }

function Get-CpuSnapshot {
    # pid -> CPU milliseconds consumed so far. Protected processes deny access; they are
    # skipped rather than guessed at.
    $snap = @{}
    foreach ($p in Get-Process) {
        try { $snap[$p.Id] = @{ Name = $p.ProcessName; Ms = $p.TotalProcessorTime.TotalMilliseconds } } catch { }
    }
    return $snap
}

function Measure-CpuBetween($before, $after, [double]$elapsedMs, [string[]]$Exclude) {
    # What everything other than the game consumed, as a share of the whole machine.
    $cores = [Environment]::ProcessorCount
    $capacity = $elapsedMs * $cores
    $rows = @()
    $total = 0.0

    foreach ($id in $after.Keys) {
        $a = $after[$id]
        if ($Exclude -contains $a.Name) { continue }
        $b = $before[$id]
        if ($null -eq $b) { continue }
        $d = $a.Ms - $b.Ms
        if ($d -le 0) { continue }
        $total += $d
        $rows += [pscustomobject]@{ Name = $a.Name; Id = $id; Percent = [math]::Round($d / $capacity * 100, 2) }
    }

    return [pscustomobject]@{
        Percent   = [math]::Round($total / $capacity * 100, 1)
        Offenders = @($rows | Sort-Object Percent -Descending | Select-Object -First 5)
    }
}

function Stop-Game {
    # Killed rather than closed on purpose: the game rewrites Settings.coc on a clean
    # exit, which would undo the profile we are trying to measure.
    Get-Process Cities2 -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill(); [void]$_.WaitForExit(15000) } catch { }
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

# A busy machine quietly ruins a run. One WMI query running alongside a benchmark turned 24.1
# fps into 18.1, and a batch measured while other work was running reported the mod costing CPU
# when it was idle -- both looked like results.
#
# The obvious reading, Win32_Processor.LoadPercentage, is not usable here on two counts: the
# query itself costs about 1200ms of the very CPU it claims to be reporting, and it reads six
# times high (43% against a true 7%). Summing per-process CPU deltas costs 64ms and is honest.
# Sample after killing the game so the reading is of everything else.
$preA = Get-CpuSnapshot
Start-Sleep -Seconds 2
$preB = Get-CpuSnapshot
$pre = Measure-CpuBetween $preA $preB 2000 @('Cities2', 'Idle')

if ($pre.Percent -ge $MaxHostLoadPercent) {
    Write-Warning ("The machine is {0}% busy before this run even starts. Anything measured here is noise." -f $pre.Percent)
    foreach ($p in $pre.Offenders) { Write-Warning ("  {0} (pid {1}) {2}%" -f $p.Name, $p.Id, $p.Percent) }
    if (-not $IgnoreHostLoad) {
        throw "Refusing to measure on a $($pre.Percent)% busy machine. Close what is running, or pass -IgnoreHostLoad."
    }
}
Write-Host ("  host at {0}% before launch" -f $pre.Percent)

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

# What the player had before this script touched anything. A benchmark is a temporary thing and
# has no business leaving its own preset behind: an earlier version left a posterised colour
# grading switched on after a measurement run, which the player found by opening the game.
$modSettingsPath = Join-Path $userData 'Cs2Saver.coc'
$modSettingsBefore = if (Test-Path $modSettingsPath) { Get-Content $modSettingsPath -Raw } else { $null }

function Restore-ModSettings {
    if ($null -eq $modSettingsBefore) { return }
    $now = if (Test-Path $modSettingsPath) { Get-Content $modSettingsPath -Raw } else { $null }
    if ($now -eq $modSettingsBefore) { return }
    Set-Content -Path $modSettingsPath -Value $modSettingsBefore -Encoding utf8 -NoNewline
    Write-Host '  mod settings put back'
}

function Set-ModFlag([string]$Key, [bool]$Value) {
    # Booleans are unquoted in this file, so they need their own writer rather than the
    # string one below.
    $modSettings = Join-Path $userData 'Cs2Saver.coc'
    if (-not (Test-Path $modSettings)) { throw "Cs2Saver.coc does not exist yet." }

    $literal = if ($Value) { 'true' } else { 'false' }
    $text = Get-Content $modSettings -Raw

    if ($text -match "`"$Key`"\s*:\s*(true|false)") {
        $updated = $text -replace "(`"$Key`"\s*:\s*)(true|false)", "`${1}$literal"
    }
    else {
        $updated = $text -replace '(\{\s*\r?\n)', "`${1}    `"$Key`": $literal,`r`n"
    }

    Set-Content -Path $modSettings -Value $updated -Encoding utf8 -NoNewline
    Write-Host "  mod $Key $literal"
}

function Set-ModSetting([string]$Key, [string]$Value) {
    # The mod's own settings file, same section-plus-JSON shape as everything else the game
    # writes. Rewriting it beats driving the options page, and the mod reads it at load.
    $modSettings = Join-Path $userData 'Cs2Saver.coc'
    if (-not (Test-Path $modSettings)) {
        throw "Cs2Saver.coc does not exist yet. The mod has to load once before $Key can be set."
    }

    $text = Get-Content $modSettings -Raw

    if ($text -match "`"$Key`"\s*:\s*`"[^`"]*`"") {
        $updated = $text -replace "(`"$Key`"\s*:\s*`")[^`"]*(`")", "`${1}$Value`${2}"
    }
    else {
        # The game drops any setting that equals its default when it writes these files, and
        # Off is the default for both of these -- so after one Off run the key is simply not
        # there any more. Put it back rather than failing on its absence.
        $updated = $text -replace '(\{\s*\r?\n)', "`${1}    `"$Key`": `"$Value`",`r`n"
    }

    if ($updated -notmatch "`"$Key`"\s*:\s*`"$Value`"") {
        throw "Could not set $Key to '$Value' in Cs2Saver.coc. Its shape has changed."
    }

    Set-Content -Path $modSettings -Value $updated -Encoding utf8 -NoNewline
    Write-Host "  mod $Key '$Value'"
}

if ($ModPreset) { Set-ModSetting 'Preset' $ModPreset }
if ($ModLook) { Set-ModSetting 'CityLook' $ModLook }
if ($ModSurface) { Set-ModSetting 'CitySurface' $ModSurface }
if ($ModStopAnimating) { Set-ModFlag 'StopAnimating' ($ModStopAnimating -eq 'true') }

# Mods only load when the game has a Paradox session, which arrives on the command line from
# the launcher. Replaying a captured one is what makes the mod benchmarkable unattended;
# without it the run still works, it just has no mods in it.
$launcherArgs = Join-Path $outDir 'launcher-args.txt'
$sessionArgs = @()
if (Test-Path $launcherArgs) {
    $captured = (Get-Content $launcherArgs -Raw) -replace '^\s*("[^"]*"|\S*Cities2\.exe)\s*', ''
    $sessionArgs = $captured.Trim() -split '\s+' | Where-Object { $_ }
}

try {

$started = Get-Date
Write-Host "  launching $($started.ToString('HH:mm:ss'))$(if ($sessionArgs) { ' with the captured session' })"

# Bracket the run with two snapshots rather than sampling inside it. The difference between
# them says exactly how much CPU everything other than the game consumed while the game was
# measuring, and it costs nothing during the run itself, which is the whole point: a check
# for interference must not be interference.
$runA = Get-CpuSnapshot

$proc = Start-Process -FilePath $exe -ArgumentList (@('--benchmark') + $sessionArgs) -WorkingDirectory $gameDir -PassThru

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
        $runB = Get-CpuSnapshot
        $runMs = ((Get-Date) - $started).TotalMilliseconds
        $during = Measure-CpuBetween $runA $runB $runMs @('Cities2', 'Idle')

        # Let the write settle before copying.
        Start-Sleep -Seconds 2
        $dest = Join-Path $outDir "Benchmark.$Label.coc"
        Copy-Item $result $dest -Force
        $elapsed = [int]((Get-Date) - $started).TotalSeconds
        Write-Host "  result after ${elapsed}s -> $dest" -ForegroundColor Green

        # Filed next to the result, because the question 'was the machine quiet for this one'
        # is asked weeks later, long after the terminal has scrolled away.
        [pscustomobject]@{
            label            = $Label
            startedAt        = $started.ToString('o')
            elapsedSec       = $elapsed
            hostBeforePct    = $pre.Percent
            hostDuringPct    = $during.Percent
            hostDuringTop    = $during.Offenders
            foregroundOk     = $foregrounded
        } | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $outDir "Benchmark.$Label.host.json") -Encoding ascii

        Write-Host ("  host at {0}% during the run" -f $during.Percent)
        if ($during.Percent -ge $MaxHostLoadPercent) {
            Write-Warning ("Something else used {0}% of the machine while this run was measuring. Treat it as suspect." -f $during.Percent)
            foreach ($p in $during.Offenders) { Write-Warning ("  {0} (pid {1}) {2}%" -f $p.Name, $p.Id, $p.Percent) }
        }

        Stop-Game
        if (-not $foregrounded) {
            Write-Warning 'The window was never confirmed in the foreground. Treat this run as suspect.'
        }
        return
    }
}

Stop-Game
throw "No result within ${TimeoutSec}s."

}
finally {
    # Whether the run succeeded, timed out or threw, the player gets their own mod
    # settings back. This script only borrowed them.
    Restore-ModSettings
}
