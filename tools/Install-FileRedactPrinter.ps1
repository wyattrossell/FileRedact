<#
.SYNOPSIS
    Installs (or removes) the "FileRedact" virtual printer without using the application UI.

.DESCRIPTION
    The printer reuses the "Microsoft Print To PDF" driver bundled with Windows, attached to a local port
    whose name is a file path. Print jobs are written silently to that file, where FileRedact's watcher
    picks them up. Administrator rights are required; when started without them the script relaunches
    itself elevated (one UAC prompt) and returns that run's exit code.

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
$printerExists = Test-Path ("HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers\" + $PrinterName)
if ($Uninstall -and -not $printerExists) {
    Write-Host "Printer '$PrinterName' is not installed; nothing to remove."
    exit 0
}
if (-not $isAdmin) {
    # Re-run this script elevated and hand back its exit code.
    $psArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', ('"{0}"' -f $PSCommandPath), '-PrinterName', ('"{0}"' -f $PrinterName), '-Inbox', ('"{0}"' -f $Inbox))
    if ($Uninstall) { $psArgs += '-Uninstall' }
    try {
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $psArgs -Verb RunAs -Wait -PassThru -WindowStyle Hidden
        exit $p.ExitCode
    } catch {
        Write-Error "Administrator approval is required to change printers: $($_.Exception.Message)"
        exit 1223
    }
}

if ($Uninstall) {
    if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) { Remove-Printer -Name $PrinterName }
    if (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue) { Remove-PrinterPort -Name $port }
    Write-Host "Printer '$PrinterName' removed."
    exit 0
}

New-Item -ItemType Directory -Force -Path $Inbox | Out-Null
# The spooler (SYSTEM) writes the file; interactive users must be able to move/delete it.
icacls $Inbox /grant '*S-1-5-32-545:(OI)(CI)M' /grant '*S-1-5-18:(OI)(CI)F' /T | Out-Null
if (Test-Path $port) { Remove-Item -Force $port }

if (-not (Get-PrinterDriver -Name $driver -ErrorAction SilentlyContinue)) {
    # The driver ships with Windows but can be switched off; turn the optional feature back on (offline, no reboot).
    try {
        Enable-WindowsOptionalFeature -Online -FeatureName 'Printing-PrintToPDFServices-Features' -All -NoRestart | Out-Null
    } catch {
        Write-Warning "Could not enable the 'Microsoft Print to PDF' feature: $($_.Exception.Message)"
    }
}
if (-not (Get-PrinterDriver -Name $driver -ErrorAction SilentlyContinue)) {
    Write-Error "The '$driver' driver is not installed. Enable the 'Microsoft Print to PDF' Windows feature and try again."
    exit 2
}
if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) { Add-PrinterPort -Name $port }
if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) { Remove-Printer -Name $PrinterName }
Add-Printer -Name $PrinterName -DriverName $driver -PortName $port -Comment 'Print here to open the document in FileRedact for PII redaction'
Write-Host "Printer '$PrinterName' installed. Jobs are written to $port and picked up by FileRedact."
exit 0
