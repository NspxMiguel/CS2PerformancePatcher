<#
.SYNOPSIS
    Records the command line the Paradox launcher uses to start the game.

.DESCRIPTION
    Mods do not load unless the Paradox launcher starts the game. Launching Cities2.exe
    directly gets no PDX session, so `Active Playset: (none)` and nothing is loaded — which
    makes the mod impossible to benchmark unattended.

    The launcher passes the session through on the command line. Capture it once and the
    same arguments can be replayed for every run afterwards, so the mod only needs a human
    to press Play a single time.

    Run this, then press Play in the launcher. It waits for the process, records the
    arguments, and leaves them where Run-Benchmark.ps1 can find them.

    The recorded file contains session tokens. It is written under .research/, which is not
    tracked by git, and the tokens stop working when the launcher session ends.
#>
[CmdletBinding()]
param(
    # How long to wait for someone to press Play.
    [int]$TimeoutSec = 300
)

$ErrorActionPreference = 'Stop'

$outDir = "$env:USERPROFILE\CS2PerformancePatcher\.research\bench"
$outFile = Join-Path $outDir 'launcher-args.txt'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Anything already running was started some other way and would record the wrong thing.
$existing = Get-Process Cities2 -ErrorAction SilentlyContinue
if ($existing) {
    Write-Warning 'Cities2 is already running. Close it first, or this will capture the wrong launch.'
    return
}

Write-Host 'Waiting for the game to start. Press Play in the Paradox launcher.' -ForegroundColor Cyan

$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    $proc = Get-CimInstance Win32_Process -Filter "Name = 'Cities2.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc -and $proc.CommandLine) {
        Set-Content -Path $outFile -Value $proc.CommandLine -Encoding utf8 -NoNewline

        # Show the shape of it without printing the tokens back to the terminal.
        $masked = $proc.CommandLine
        foreach ($secret in 'pdx-launcher-session-token', 'paradox-account-userid', 'accessToken', 'hubSessionId') {
            $masked = $masked -replace "($secret[= ])\S+", '$1<redacted>'
        }

        Write-Host "  captured -> $outFile" -ForegroundColor Green
        Write-Host "  $masked"
        return
    }
    Start-Sleep -Milliseconds 500
}

throw "The game did not start within ${TimeoutSec}s."
