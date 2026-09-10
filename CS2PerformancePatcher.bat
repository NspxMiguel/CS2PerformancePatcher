@echo off
setlocal
set "CS2PP_LAUNCHER=%~f0"

rem Anything passed here is handed to the console tool instead of opening the window.
rem That is what lets Instalar-Mod.bat be four lines: it calls this file with
rem "install-mod" and inherits the whole download-and-verify path rather than
rem carrying a second copy of it that could drift.
set "CS2PP_ARGS=%*"

rem The only file a player downloads manually. The PowerShell payload lives below the
rem marker so this stays one portable file without a companion bootstrap script.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$self=$env:CS2PP_LAUNCHER; $text=[System.IO.File]::ReadAllText($self); $marker=':__CS2PP_POWERSHELL__'; $i=$text.LastIndexOf($marker,[System.StringComparison]::Ordinal); if($i -lt 0){Write-Host '[ERROR] Launcher is incomplete.' -ForegroundColor Red; exit 2}; $script=$text.Substring($i+$marker.Length); & ([scriptblock]::Create($script))"
exit /b %ERRORLEVEL%

:__CS2PP_POWERSHELL__
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Info([string] $Message) { Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Warn([string] $Message) { Write-Host "[WARNING] $Message" -ForegroundColor Yellow }
function Fail([string] $Message, [int] $Code = 1) {
    Write-Host "[ERROR] $Message" -ForegroundColor Red
    exit $Code
}

if (-not [Environment]::Is64BitOperatingSystem) {
    Fail 'CS2 Performance Patcher requires 64-bit Windows.' 3
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = if ($env:CS2PP_HOME) { $env:CS2PP_HOME } else { Join-Path $env:LOCALAPPDATA 'CS2PerformancePatcher' }
$base = if ($env:CS2PP_RELEASE_BASE) { $env:CS2PP_RELEASE_BASE.TrimEnd('/') } else { 'https://github.com/NspxMiguel/CS2PerformancePatcher/releases/latest/download' }
$zipUrl = "$base/CS2PerformancePatcher.zip"
$hashUrl = "$base/CS2PerformancePatcher.zip.sha256"
$archive = Join-Path $root 'CS2PerformancePatcher.zip'
$savedHash = Join-Path $root 'CS2PerformancePatcher.zip.sha256'
$current = Join-Path $root 'current'
$launcher = Join-Path $current 'CS2PerformancePatcher.exe'

function Read-ExpectedHash([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $match = [regex]::Match([System.IO.File]::ReadAllText($Path), '(?im)\b[a-f0-9]{64}\b')
    if (-not $match.Success) { return $null }
    return $match.Value.ToUpperInvariant()
}

function Get-Sha256([string] $Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
        finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Test-Payload([string] $PayloadDirectory) {
    $manifest = Join-Path $PayloadDirectory 'payload.sha256'
    if (-not (Test-Path -LiteralPath $manifest)) { return $false }
    foreach ($line in [System.IO.File]::ReadAllLines($manifest)) {
        $match = [regex]::Match($line, '^(?<hash>[a-fA-F0-9]{64})\s+\*?(?<path>.+)$')
        if (-not $match.Success) { return $false }
        $relative = $match.Groups['path'].Value.Replace('/', [IO.Path]::DirectorySeparatorChar)
        if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains('..')) { return $false }
        $file = Join-Path $PayloadDirectory $relative
        if (-not (Test-Path -LiteralPath $file)) { return $false }
        if ((Get-Sha256 $file).ToUpperInvariant() -ne $match.Groups['hash'].Value.ToUpperInvariant()) { return $false }
    }
    return (Test-Path -LiteralPath (Join-Path $PayloadDirectory 'CS2PerformancePatcher.exe'))
}
function Test-Archive([string] $ZipPath, [string] $HashPath) {
    $expected = Read-ExpectedHash $HashPath
    if (-not $expected -or -not (Test-Path -LiteralPath $ZipPath)) { return $false }
    $actual = (Get-Sha256 $ZipPath).ToUpperInvariant()
    return $actual -eq $expected
}

New-Item -ItemType Directory -Force -Path $root | Out-Null
$downloadedHash = Join-Path $root ("download-" + [Guid]::NewGuid().ToString('N') + '.sha256')
$downloadedZip = Join-Path $root ("download-" + [Guid]::NewGuid().ToString('N') + '.zip')
$useCache = $false
$refresh = $false

try {
    Info 'Checking latest version...'
    Invoke-WebRequest -UseBasicParsing -Uri $hashUrl -OutFile $downloadedHash
    $remoteHash = Read-ExpectedHash $downloadedHash
    if (-not $remoteHash) { throw 'The release checksum is missing or invalid.' }

    if ((Test-Archive $archive $savedHash) -and ((Read-ExpectedHash $savedHash) -eq $remoteHash)) {
        Info 'Latest version is already prepared.'
        $useCache = $true
    }
    else {
        Info 'Downloading update...'
        Invoke-WebRequest -UseBasicParsing -Uri $zipUrl -OutFile $downloadedZip
        $actualHash = (Get-Sha256 $downloadedZip).ToUpperInvariant()
        if ($actualHash -ne $remoteHash) {
            Remove-Item -LiteralPath $downloadedZip -Force -ErrorAction SilentlyContinue
            throw 'The download failed its security check. Nothing was installed.'
        }
        Copy-Item -LiteralPath $downloadedZip -Destination $archive -Force
        Copy-Item -LiteralPath $downloadedHash -Destination $savedHash -Force
        Remove-Item -LiteralPath $downloadedZip,$downloadedHash -Force
        $useCache = $true
        $refresh = $true
    }
}
catch {
    Remove-Item -LiteralPath $downloadedZip,$downloadedHash -Force -ErrorAction SilentlyContinue
    if (Test-Archive $archive $savedHash) {
        Warn "Could not check for an update ($($_.Exception.Message)). Starting the verified saved copy."
        $useCache = $true
    }
    else {
        Fail "Could not download CS2 Performance Patcher. Check your internet connection and try again. Details: $($_.Exception.Message)" 4
    }
}

if (-not $useCache) { Fail 'No verified application package is available.' 5 }

if ($refresh -or -not (Test-Payload $current)) {
    Info 'Preparing application...'
    $staging = Join-Path $root ("staging-" + [Guid]::NewGuid().ToString('N'))
    try {
        Expand-Archive -LiteralPath $archive -DestinationPath $staging -Force
        $stagedLauncher = Join-Path $staging 'CS2PerformancePatcher.exe'
        if (-not (Test-Path -LiteralPath $stagedLauncher)) { throw 'The downloaded package does not contain the application.' }
        $old = Join-Path $root ("previous-" + [Guid]::NewGuid().ToString('N'))
        if (Test-Path -LiteralPath $current) { Move-Item -LiteralPath $current -Destination $old }
        Move-Item -LiteralPath $staging -Destination $current
        Remove-Item -LiteralPath $old -Recurse -Force -ErrorAction SilentlyContinue
    }
    catch {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        Fail "Could not prepare the application. $($_.Exception.Message)" 6
    }
}

$requested = if ($env:CS2PP_ARGS) { $env:CS2PP_ARGS.Trim() } else { '' }

if (-not $requested) {
    Info 'Starting CS2 Performance Patcher...'
    try {
        Start-Process -FilePath $launcher -WorkingDirectory $current
    }
    catch {
        Fail "Could not start the application. $($_.Exception.Message)" 7
    }
    exit 0
}

# A console verb was asked for. Run it in this window and hand its exit code back, so a
# caller like Instalar-Mod.bat can tell success from failure without parsing the output.
$console = Join-Path $current 'cli\cs2patch.exe'
if (-not (Test-Path -LiteralPath $console)) {
    Fail 'This package does not contain the console tool. Download the latest release again.' 8
}

Info "Running: cs2patch $requested"
Write-Host ''

# Split on whitespace but keep quoted runs together, without evaluating anything. The
# arguments reaching here are verbs and flags from a companion .bat, not user text, and
# handing them to Invoke-Expression to get quoting for free would be handing a command
# line the ability to run arbitrary PowerShell.
$argv = @([regex]::Matches($requested, '"([^"]*)"|(\S+)') | ForEach-Object {
    if ($_.Groups[1].Success) { $_.Groups[1].Value } else { $_.Groups[2].Value }
})

& $console @argv
exit $LASTEXITCODE