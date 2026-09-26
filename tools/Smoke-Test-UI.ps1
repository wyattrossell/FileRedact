<#
.SYNOPSIS
    Drives the real FileRedact window through the main workflow using UI Automation and reports pass/fail.

.DESCRIPTION
    Launches FileRedact with a document, waits for the scan, then checks that Save and Print are enabled,
    toggles preview, clears/selects findings, adds an "always redact" term, saves a redacted PDF through the
    real Save dialog, opens and cancels the Print dialog, runs "Check for updates", and closes the app.
    Any running FileRedact instance is closed first so the freshly built binary is the one under test.

.EXAMPLE
    .\tools\Smoke-Test-UI.ps1 -Exe .\src\FileRedact.App\bin\Debug\net10.0-windows10.0.19041.0\FileRedact.exe
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [string]$Document,
    [string]$Output = (Join-Path $env:TEMP 'FileRedact-smoke-output.pdf'),
    [switch]$KeepOpen
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
if (-not $Exe) { $Exe = Join-Path $repoRoot 'src\FileRedact.App\bin\Debug\net10.0-windows10.0.19041.0\FileRedact.exe' }
if (-not $Document) { $Document = Join-Path $repoRoot 'samples\sample-incident-report.txt' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$script:failures = 0
function Pass($m) { Write-Host "  PASS  $m" -ForegroundColor Green }
function Fail($m) { Write-Host "  FAIL  $m" -ForegroundColor Red; $script:failures++ }
function Check($cond, $m) { if ($cond) { Pass $m } else { Fail $m } }

function Wait-Until([scriptblock]$Condition, [int]$Seconds = 30, [string]$What = 'condition') {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try { $r = & $Condition; if ($r) { return $r } } catch { }
        Start-Sleep -Milliseconds 300
    }
    throw "Timed out waiting for $What"
}

function Find-Window([string]$TitleStart, [int]$Seconds = 30, $Owner = $null) {
    # Owned dialogs (Save, Print, message boxes) show up as children of their owner window, not of the desktop.
    Wait-Until -Seconds $Seconds -What "window '$TitleStart'" {
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
        $parents = @([System.Windows.Automation.AutomationElement]::RootElement)
        if ($Owner) { $parents += $Owner }
        foreach ($p in $parents) {
            foreach ($w in $p.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
                if ($w.Current.Name.StartsWith($TitleStart)) { return $w }
            }
        }
        $null
    }
}

function Find-Element($Parent, [string]$Name, $ControlType, [int]$Seconds = 15) {
    Wait-Until -Seconds $Seconds -What "element '$Name'" {
        $c1 = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
        $c2 = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType)
        $cond = New-Object System.Windows.Automation.AndCondition($c1, $c2)
        $e = $Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) { $e } else { $null }
    }
}

function Find-ByAutomationId($Parent, [string]$Id, [int]$Seconds = 15) {
    Wait-Until -Seconds $Seconds -What "automation id '$Id'" {
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
        $e = $Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) { $e } else { $null }
    }
}

function Invoke-Element($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }

# Buttons that open a modal dialog must be clicked with the mouse: InvokePattern would block until the dialog closes.
Add-Type -Namespace Smoke -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@
# UI Automation reports physical pixels; without this, SetCursorPos would be scaled on high-DPI displays.
[Smoke.Native]::SetProcessDPIAware() | Out-Null
function Click-Element($e) {
    $pt = $e.GetClickablePoint()
    [Smoke.Native]::SetCursorPos([int]$pt.X, [int]$pt.Y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Smoke.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # left down
    [Smoke.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # left up
    Start-Sleep -Milliseconds 300
}
function Activate-Window($w) { try { [Smoke.Native]::SetForegroundWindow([IntPtr]$w.Current.NativeWindowHandle) | Out-Null } catch { }; Start-Sleep -Milliseconds 300 }
function Toggle-Element($e) { $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
function Set-Text($e, [string]$text) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }

function Get-StatusText($win) {
    # The status bar TextBlock is the one whose text starts with "Loaded" / "Saved" / etc.
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $texts = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($t in $texts) { $n = $t.Current.Name; if ($n -match '^(Loaded|Saved|Failed|Loading|Scanning|Sent|Added|Open a document)') { return $n } }
    ''
}
function Get-SummaryText($win) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) { $n = $t.Current.Name; if ($n -match 'item\(s\) found, \d+ selected') { return $n } }
    ''
}

Write-Host "FileRedact UI smoke test" -ForegroundColor Cyan
Write-Host "  exe:      $Exe"
Write-Host "  document: $Document"
Get-Process FileRedact -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
if (Test-Path $Output) { Remove-Item $Output -Force }

$proc = Start-Process -FilePath (Resolve-Path $Exe) -ArgumentList ('"{0}"' -f (Resolve-Path $Document)) -PassThru
try {
    $win = Find-Window 'FileRedact' 40
    Pass "main window appeared"

    $status = Wait-Until -Seconds 90 -What 'document to load' { $s = Get-StatusText $win; if ($s -like 'Loaded*') { $s } else { $null } }
    Pass "document loaded: $status"

    $save = Find-Element $win 'Save redacted PDF…' ([System.Windows.Automation.ControlType]::Button)
    $print = Find-Element $win 'Print…' ([System.Windows.Automation.ControlType]::Button)
    Check $save.Current.IsEnabled  "'Save redacted PDF…' is enabled after loading"
    Check $print.Current.IsEnabled "'Print…' is enabled after loading"

    $summaryBefore = Get-SummaryText $win
    Check ($summaryBefore -match '(\d+) item\(s\) found') "findings summary shown: $summaryBefore"

    # Preview toggle
    $preview = Find-Element $win 'Preview redactions' ([System.Windows.Automation.ControlType]::Button)
    Toggle-Element $preview; Start-Sleep -Milliseconds 400
    Check ($preview.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On') "preview redactions switched on"
    Toggle-Element $preview; Start-Sleep -Milliseconds 300

    # Clear all / Select all
    Invoke-Element (Find-Element $win 'Clear all' ([System.Windows.Automation.ControlType]::Button)); Start-Sleep -Milliseconds 500
    Check ((Get-SummaryText $win) -match ', 0 selected') "'Clear all' deselects every finding"
    Invoke-Element (Find-Element $win 'Select all' ([System.Windows.Automation.ControlType]::Button)); Start-Sleep -Milliseconds 500
    $sel = Get-SummaryText $win
    Check ($sel -match '(\d+) item\(s\) found, \1 selected') "'Select all' selects every finding ($sel)"

    # Zoom
    Invoke-Element (Find-Element $win '+' ([System.Windows.Automation.ControlType]::Button)); Start-Sleep -Milliseconds 300
    Check ((Find-Element $win '125%' ([System.Windows.Automation.ControlType]::Button) -ne $null)) "zoom in shows 125%"
    Invoke-Element (Find-Element $win '125%' ([System.Windows.Automation.ControlType]::Button)); Start-Sleep -Milliseconds 300
    Check ((Find-Element $win '100%' ([System.Windows.Automation.ControlType]::Button) -ne $null)) "zoom reset shows 100%"

    # Always-redact term
    $editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $termBox = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
    Set-Text $termBox 'Springfield'
    Invoke-Element (Find-Element $win 'Add' ([System.Windows.Automation.ControlType]::Button)); Start-Sleep -Milliseconds 800
    $after = Get-SummaryText $win
    $nBefore = [int][regex]::Match($sel, '(\d+) item').Groups[1].Value
    $nAfter = [int][regex]::Match($after, '(\d+) item').Groups[1].Value
    Check ($nAfter -gt $nBefore) "custom term added findings ($nBefore -> $nAfter)"
    Invoke-Element (Find-Element $win '✕' ([System.Windows.Automation.ControlType]::Button)); Start-Sleep -Milliseconds 300
    Pass "custom term removed"

    # Save through the real dialog
    Activate-Window $win
    Click-Element $save
    $dlg = Find-Window 'Save redacted PDF' 20 $win
    $fileName = Find-ByAutomationId $dlg '1001'
    Set-Text $fileName $Output
    Click-Element (Find-Element $dlg 'Save' ([System.Windows.Automation.ControlType]::Button))
    $saved = Find-Window 'FileRedact - saved' 120 $win
    Pass "save completed and confirmation shown"
    Click-Element (Find-Element $saved 'No' ([System.Windows.Automation.ControlType]::Button))
    Start-Sleep -Milliseconds 500
    Check (Test-Path $Output) "redacted PDF written: $Output"
    if (Test-Path $Output) {
        $bytes = [IO.File]::ReadAllBytes($Output)
        Check ($bytes.Length -gt 10000 -and [Text.Encoding]::ASCII.GetString($bytes, 0, 4) -eq '%PDF') "output is a PDF ($([math]::Round($bytes.Length/1KB)) KB)"
        $raw = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
        Check (-not $raw.Contains('123-45-6789')) "SSN is not present in the output bytes"
        Check (-not $raw.Contains('Gonzalez')) "officer name is not present in the output bytes"
    }
    Check $save.Current.IsEnabled  "'Save redacted PDF…' still enabled after saving"
    Check $print.Current.IsEnabled "'Print…' still enabled after saving"
    Check ((Find-Element $win 'Show in folder' ([System.Windows.Automation.ControlType]::Button)) -ne $null) "'Show in folder' appears after saving"

    # Print dialog opens (then cancel)
    Activate-Window $win
    Click-Element $print
    $pd = Find-Window 'FileRedact - Print' 30 $win
    Pass "print dialog opened"
    Click-Element (Find-Element $pd 'Cancel' ([System.Windows.Automation.ControlType]::Button))
    Start-Sleep -Milliseconds 500

    # Update check reports something sensible
    Activate-Window $win
    Click-Element (Find-Element $win 'Check for updates' ([System.Windows.Automation.ControlType]::Button))
    $upd = Find-Window 'FileRedact update' 40 $win
    $msgCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $msg = ($upd.FindAll([System.Windows.Automation.TreeScope]::Descendants, $msgCond) | ForEach-Object { $_.Current.Name }) -join ' '
    Pass "update check answered: $msg"
    $okOrLater = $upd.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'OK')))
    if (-not $okOrLater) { $okOrLater = Find-Element $upd 'Not now' ([System.Windows.Automation.ControlType]::Button) }
    Click-Element $okOrLater
    Start-Sleep -Milliseconds 400
}
catch {
    Fail "exception: $($_.Exception.Message)"
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
        $shot = Join-Path $env:TEMP 'FileRedact-smoke-failure.png'
        $bmp.Save($shot)
        Write-Host "  screenshot saved to $shot"
    } catch { }
}
finally {
    if (-not $KeepOpen) {
        try { $win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
        $exited = $false
        for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; if (-not (Get-Process FileRedact -ErrorAction SilentlyContinue)) { $exited = $true; break } }
        if ($exited) { Pass "app exited when the window closed (after $([math]::Round($i*0.25,1)) s)" }
        else {
            $printerInstalled = Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers\FileRedact'
            if ($printerInstalled) { Write-Host "  note  app kept running in the tray (printer watcher active); stopped it" }
            else { Fail "app did not exit within 10 s after closing the window (printer not installed)" }
            Get-Process FileRedact -ErrorAction SilentlyContinue | Stop-Process -Force
        }
    }
}

if ($script:failures -eq 0) { Write-Host "ALL CHECKS PASSED" -ForegroundColor Green; exit 0 }
else { Write-Host "$($script:failures) CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
