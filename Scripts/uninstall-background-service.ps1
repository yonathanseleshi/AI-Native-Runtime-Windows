<#
.SYNOPSIS
  Removes the AI Native Runtime Core scheduled task installed by
  install-background-service.ps1, and stops the running daemon it started
  (a clean stop - exit code 0 - so nothing tries to "restart on crash" it).
#>
$TaskName = "AINativeRuntime.Daemon"

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Removed scheduled task: $TaskName"
} else {
    Write-Host "No scheduled task named $TaskName was registered."
}

$proc = Get-Process -Name "ainativeruntime_node" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "Note: ainativeruntime_node (pid $($proc.Id)) is still running - stop it via the desktop app's" `
        "'Stop Runtime' control (runtime.stop) for a clean, audited shutdown, rather than killing the process directly."
}
