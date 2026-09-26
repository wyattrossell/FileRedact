<#
.SYNOPSIS
    End-to-end test of the automatic update: installs an "old" build silently, serves a fake release feed
    from localhost that advertises a "new" build, triggers the in-app update and verifies the upgrade.

.DESCRIPTION
    Requires two installers built with installer\build-installer.ps1 (for example -Version 0.1.0 and
    -Version 0.2.0). No administrator rights are needed: the installer is per-user and the printer task is
    not selected. The test cleans up by uninstalling FileRedact silently at the end unless -KeepInstalled.

.EXAMPLE
    .\tools\Test-UpdateFlow.ps1 -OldSetup .\publish\FileRedact-Setup-0.1.0.exe -NewSetup .\publish\FileRedact-Setup-0.2.0.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$OldSetup,
    [Parameter(Mandatory)] [string]$NewSetup,
    [string]$NewVersion = '0.2.0',
    [int]$Port = 8765,
    [switch]$KeepInstalled
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace UpdTest -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@
[UpdTest.Native]::SetProcessDPIAware() | Out-Null

$script:failures = 0
function Pass($m) { Write-Host "  PASS  $m" -ForegroundColor Green }
function Fail($m) { Write-Host "  FAIL  $m" -ForegroundColor Red; $script:failures++ }
function Check($cond, $m) { if ($cond) { Pass $m } else { Fail $m } }
function Wait-Until([scriptblock]$Condition, [int]$Seconds = 30, [string]$What = 'condition') {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { try { $r = & $Condition; if ($r) { return $r } } catch { }; Start-Sleep -Milliseconds 400 }
    throw "Timed out waiting for $What"
}
function Find-Window([string]$TitleStart, [int]$Seconds = 30, $Owner = $null) {
    Wait-Until -Seconds $Seconds -What "window '$TitleStart'" {
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
        $parents = @([System.Windows.Automation.AutomationElement]::RootElement); if ($Owner) { $parents += $Owner }
        foreach ($p in $parents) { foreach ($w in $p.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) { if ($w.Current.Name.StartsWith($TitleStart)) { return $w } } }
        $null
    }
}
function Find-Element($Parent, [string]$Name, $ControlType, [int]$Seconds = 15) {
    Wait-Until -Seconds $Seconds -What "element '$Name'" {
        $c1 = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
        $c2 = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType)
        $e = $Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.AndCondition($c1, $c2)))
        if ($e) { $e } else { $null }
    }
}
function Click-Element($e) {
    $pt = $e.GetClickablePoint(); [UpdTest.Native]::SetCursorPos([int]$pt.X, [int]$pt.Y) | Out-Null; Start-Sleep -Milliseconds 120
    [UpdTest.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); [UpdTest.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300
}

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\FileRedact'
$exe = Join-Path $installDir 'FileRedact.exe'
$OldSetup = (Resolve-Path $OldSetup).Path
$NewSetup = (Resolve-Path $NewSetup).Path
$feedDir = Join-Path $env:TEMP 'FileRedact-update-feed'
$server = $null

Write-Host "FileRedact update-flow test" -ForegroundColor Cyan
try {
    Get-Process FileRedact -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    # 1. Silent per-user install of the old build (no printer task, so no UAC).
    $p = Start-Process -FilePath $OldSetup -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="contextmenu" /MERGETASKS="!desktopicon,!installprinter,!autostart"' -Wait -PassThru
    Check ($p.ExitCode -eq 0) "old build installed silently (exit code $($p.ExitCode))"
    Check (Test-Path $exe) "installed executable present at $exe"
    $oldVer = (Get-Item $exe).VersionInfo.ProductVersion
    Write-Host "  installed version: $oldVer"

    # 2. Fake release feed on localhost with the new installer as the asset (with size and sha256 digest).
    New-Item -ItemType Directory -Force -Path $feedDir | Out-Null
    $assetName = "FileRedact-Setup-$NewVersion.exe"
    Copy-Item $NewSetup (Join-Path $feedDir $assetName) -Force
    $sha = (Get-FileHash -Algorithm SHA256 (Join-Path $feedDir $assetName)).Hash.ToLowerInvariant()
    $size = (Get-Item (Join-Path $feedDir $assetName)).Length
    $json = @{
        tag_name = "v$NewVersion"; name = "FileRedact $NewVersion"; body = "Test release"; draft = $false; prerelease = $false
        html_url = "http://localhost:$Port/"; published_at = (Get-Date).ToUniversalTime().ToString('o')
        assets = @(@{ name = $assetName; browser_download_url = "http://localhost:$Port/$assetName"; size = $size; digest = "sha256:$sha" })
    } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $feedDir 'latest.json'), $json)
    $server = Start-Process -FilePath 'python' -ArgumentList "-m http.server $Port --bind 127.0.0.1" -WorkingDirectory $feedDir -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    $probe = Invoke-WebRequest -UseBasicParsing "http://localhost:$Port/latest.json"
    Check ($probe.StatusCode -eq 200) "fake release feed is being served"

    # 3. Start the installed (old) app pointing at the fake feed and trigger the update from the toolbar.
    $env:FILEREDACT_UPDATE_URL = "http://localhost:$Port/latest.json"
    $app = Start-Process -FilePath $exe -PassThru
    $win = Find-Window 'FileRedact' 40
    Pass "old build started (pid $($app.Id))"
    [UpdTest.Native]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
    Click-Element (Find-Element $win 'Check for updates' ([System.Windows.Automation.ControlType]::Button))
    $upd = Find-Window 'FileRedact update' 60 $win
    Pass "update prompt appeared"
    $install = Find-Element $upd 'Install and restart' ([System.Windows.Automation.ControlType]::Button)
    Wait-Until -Seconds 180 -What 'download to finish' { if ($install.Current.IsEnabled) { $true } else { $null } } | Out-Null
    Pass "installer downloaded and verified inside the app"
    Click-Element $install

    # 4. The app exits, the installer runs silently, and the app comes back on the new version.
    Wait-Until -Seconds 60 -What 'old app to exit' { if ($app.HasExited) { $true } else { $null } } | Out-Null
    Pass "old app closed for the update"
    $newProc = Wait-Until -Seconds 240 -What 'updated app to start' {
        $q = Get-Process FileRedact -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $app.Id }
        if ($q) { $q | Select-Object -First 1 } else { $null }
    }
    Pass "updated app restarted automatically (pid $($newProc.Id))"
    Start-Sleep -Seconds 3
    $newVer = (Get-Item $exe).VersionInfo.ProductVersion
    Check ($newVer -like "$NewVersion*") "installed version is now $newVer (was $oldVer)"
    $log = Join-Path $env:LOCALAPPDATA 'FileRedact\update-install.log'
    Check (Test-Path $log) "installer wrote its log ($log)"
    $win2 = Find-Window 'FileRedact' 40
    Pass "updated app window is visible"
    $win2.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
catch {
    Fail "exception: $($_.Exception.Message)"
}
finally {
    Remove-Item Env:\FILEREDACT_UPDATE_URL -ErrorAction SilentlyContinue
    if ($server) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    Get-Process FileRedact -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if (-not $KeepInstalled) {
        $unins = Get-ChildItem $installDir -Filter 'unins*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($unins) {
            # Make sure the installed printer script is the current one (skips elevation when no printer exists).
            $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
            Copy-Item (Join-Path $scriptDir 'Install-FileRedactPrinter.ps1') (Join-Path $installDir 'tools\Install-FileRedactPrinter.ps1') -Force -ErrorAction SilentlyContinue
            $u = Start-Process -FilePath $unins.FullName -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru
            Check ($u.ExitCode -eq 0) "uninstalled silently (exit code $($u.ExitCode))"
            Check (-not (Test-Path $exe)) "installation folder removed"
        }
    }
    Remove-Item $feedDir -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failures -eq 0) { Write-Host "ALL CHECKS PASSED" -ForegroundColor Green; exit 0 }
else { Write-Host "$($script:failures) CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
