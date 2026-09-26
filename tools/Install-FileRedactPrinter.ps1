<#
.SYNOPSIS
    Installs (or removes) the "FileRedact" virtual printer without using the application UI.

.DESCRIPTION
    The printer reuses the "Microsoft Print To PDF" driver bundled with Windows, attached to a local port
    whose name is a file path. Print jobs are written silently to that file, where FileRedact's watcher
    picks them up. Must be run from an elevated PowerShell prompt.

.EXAMPLE
    .\Install-FileRedactPrinter.ps1
    .\Install-FileRedactPrinter.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [string]$PrinterName = 'FileRedact',
    [string]$Inbox = (Join-Path $env:ProgramData 'FileRedact\Inbox')
)

$ErrorActionPreference = 'Stop'
$driver = 'Microsoft Print To PDF'
$port = Join-Path $Inbox 'FileRedact-print.pdf'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script from an elevated (Administrator) PowerShell prompt.' }

if ($Uninstall) {
    if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) { Remove-Printer -Name $PrinterName }
    if (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue) { Remove-PrinterPort -Name $port }
    Write-Host "Printer '$PrinterName' removed."
    return
}

New-Item -ItemType Directory -Force -Path $Inbox | Out-Null
# The spooler (SYSTEM) writes the file; interactive users must be able to move/delete it.
icacls $Inbox /grant '*S-1-5-32-545:(OI)(CI)M' /grant '*S-1-5-18:(OI)(CI)F' /T | Out-Null
if (Test-Path $port) { Remove-Item -Force $port }

if (-not (Get-PrinterDriver -Name $driver -ErrorAction SilentlyContinue)) {
    throw "The '$driver' driver is not installed. Enable the 'Microsoft Print to PDF' Windows feature and try again."
}
if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) { Add-PrinterPort -Name $port }
if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) { Remove-Printer -Name $PrinterName }
Add-Printer -Name $PrinterName -DriverName $driver -PortName $port -Comment 'Print here to open the document in FileRedact for PII redaction'
Write-Host "Printer '$PrinterName' installed. Jobs are written to $port and picked up by FileRedact."
