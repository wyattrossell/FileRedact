<#
.SYNOPSIS
    Publishes FileRedact (self-contained, win-x64) and compiles the Inno Setup installer.

.PARAMETER Version
    Version stamped into the binaries and the installer file name. Defaults to the <Version> in Directory.Build.props.

.EXAMPLE
    .\installer\build-installer.ps1
    .\installer\build-installer.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'publish\FileRedact'
$outputDir = Join-Path $root 'publish'

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
Write-Host "Building FileRedact $Version" -ForegroundColor Cyan

if (-not $SkipPublish) {
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    dotnet publish (Join-Path $root 'src\FileRedact.App\FileRedact.App.csproj') -c $Configuration -r win-x64 --self-contained true `
        -p:Version=$Version -p:PublishReadyToRun=false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

# Locate the Inno Setup compiler (per-machine or per-user install, or on PATH).
$candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$outputDir" (Join-Path $PSScriptRoot 'FileRedact.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setup = Join-Path $outputDir "FileRedact-Setup-$Version.exe"
Write-Host "Installer written to $setup" -ForegroundColor Green
