<#
.SYNOPSIS
  Registers the AI Native Runtime Core daemon as a per-user Windows
  Scheduled Task that starts at logon - the Windows equivalent of MAC's
  LaunchAgent (ADR-0014), mirroring its own decisions rather than
  reinventing them:

    - Per-user, not a Windows Service (ADR-0014's explicit Windows
      choice, deferred pending a real need for it) - this task starts
      only when the signed-in user logs on, exactly like a LaunchAgent
      needs a login session; it does not run before anyone logs in and
      does not survive a full logout with nobody signed in, which is
      ADR-0014's own stated Consequences limitation, restated here for
      Windows rather than silently assumed to differ.
    - The KeepAlive/Stop-Runtime interaction (Phase 1 report §4.3): a
      generated wrapper script (`run-daemon-supervised.cmd`) restarts the
      daemon only on a non-zero (crash) exit, never on a clean exit(0) -
      `runtime.stop`'s own coordinator-driven shutdown and a graceful
      Ctrl+C both exit 0, so a deliberate stop stays stopped. Windows
      Task Scheduler has no native "restart only on crash" primitive the
      way launchd's `KeepAlive = { SuccessfulExit = false }` does, so
      this wrapper supplies it directly rather than leaving the daemon
      to auto-relaunch and silently defeat "Stop Runtime".
    - `RUNTIME_DATA_DIR` is set explicitly by this script (not left to
      CORE's own platform default), matching the desktop app's own
      pipe/data-directory expectations - `desktop-shell-conventions.md`
      §7's cross-platform principle: the daemon and the desktop app must
      agree on exactly one location, decided once.

.PARAMETER DaemonPath
  Path to the built `ainativeruntime_node.exe`. Defaults to the sibling
  CORE repository's release build.
#>
param(
    [string]$DaemonPath = "$PSScriptRoot\..\..\AI-Native-Runtime-Rust\target\release\ainativeruntime_node.exe"
)

$ErrorActionPreference = "Stop"

$DaemonPath = (Resolve-Path $DaemonPath -ErrorAction Stop).Path
$DataDir = Join-Path $env:LOCALAPPDATA "AINativeRuntime"
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
$LogDir = Join-Path $env:LOCALAPPDATA "AINativeRuntime\Logs"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$WrapperTemplate = Join-Path $PSScriptRoot "run-daemon-supervised.cmd.template"
$WrapperPath = Join-Path $DataDir "run-daemon-supervised.cmd"
(Get-Content $WrapperTemplate -Raw) `
    -replace [regex]::Escape("__RUNTIME_DATA_DIR__"), $DataDir `
    -replace [regex]::Escape("__DAEMON_EXE__"), $DaemonPath `
    | Set-Content -Path $WrapperPath -Encoding ASCII

$TaskName = "AINativeRuntime.Daemon"
$Action = New-ScheduledTaskAction -Execute $WrapperPath `
    -WorkingDirectory $DataDir
$Trigger = New-ScheduledTaskTrigger -AtLogOn
$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 0 # restart-on-crash is the wrapper script's job, not Task Scheduler's

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger `
    -Settings $Settings -Description "AI Native Runtime Core daemon (per-user, ADR-0014)" `
    | Out-Null

Write-Host "Registered scheduled task: $TaskName"
Write-Host "  Daemon:          $DaemonPath"
Write-Host "  RUNTIME_DATA_DIR: $DataDir"
Write-Host "  Wrapper:         $WrapperPath"
Write-Host ""
Write-Host "Starting it now (it will also start automatically at your next logon)..."
Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 2
Get-ScheduledTaskInfo -TaskName $TaskName | Format-List
