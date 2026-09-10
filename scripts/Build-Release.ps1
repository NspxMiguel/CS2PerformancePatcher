[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $Version = '0.0.0-dev',
    [Parameter(Mandatory = $false)]
    [string] $OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repoRoot 'artifacts\release' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $output 'stage'
$zip = Join-Path $output 'CS2PerformancePatcher.zip'
$hash = "$zip.sha256"
$mod = Join-Path $repoRoot 'assets\Cs2Saver.dll'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 8 SDK is required to build a release.'
}
if (-not (Test-Path -LiteralPath $mod)) {
    throw "Missing release mod binary: $mod. Build Cs2Saver.Mod on a machine with Cities: Skylines II installed, then copy only Cs2Saver.dll to assets\\."
}

Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$assemblyVersion = $Version.TrimStart([char[]]'vV')
$publish = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$assemblyVersion")

& dotnet publish (Join-Path $repoRoot 'src\Cs2Patcher.Gui\Cs2Patcher.Gui.csproj') @publish -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Could not publish the graphical application.' }

$cliStage = Join-Path $stage 'cli'
& dotnet publish (Join-Path $repoRoot 'src\Cs2Patcher.Cli\Cs2Patcher.Cli.csproj') @publish -o $cliStage
if ($LASTEXITCODE -ne 0) { throw 'Could not publish the command-line application.' }

Copy-Item -LiteralPath $mod -Destination (Join-Path $stage 'Cs2Saver.dll') -Force

$manifest = Join-Path $stage 'payload.sha256'
Get-ChildItem -LiteralPath $stage -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($stage.Length).TrimStart([IO.Path]::DirectorySeparatorChar).Replace('\', '/')
        $fileHash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$fileHash  $relative"
    } |
    Set-Content -LiteralPath $manifest -Encoding ascii

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hash -Value "$sha256  CS2PerformancePatcher.zip" -NoNewline -Encoding ascii

Write-Host "Release package: $zip"
Write-Host "SHA-256:        $sha256"
