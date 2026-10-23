# Local Activity Tracker - Auto-Start & Watchdog Task Registration
# Registers a Windows Scheduled Task for the current interactive user.
# Automatically launches Coordinator at logon and restarts it if it ever exits/crashes.

[CmdletBinding()]
param(
    [string]$InstallPath = "$env:LOCALAPPDATA\LocalActivityTracker"
)

$ErrorActionPreference = 'Stop'

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Local Activity Tracker - Auto-Start & Watchdog Setup" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$targetExe = Join-Path $InstallPath "bin\coordinator\Coordinator.exe"
$coordDir = Join-Path $InstallPath "bin\coordinator"

if (-not (Test-Path $targetExe)) {
    Write-Error "Coordinator.exe not found at: $targetExe. Please run install.ps1 first."
    exit 1
}

# 1. Update Windows Startup Folder Shortcut (Minimized)
$startupFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupFolder "LocalActivityTracker.lnk"

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $coordDir
$shortcut.WindowStyle = 7 # Minimized
$shortcut.Description = "Local Activity Tracker Interactive Coordinator"
$shortcut.Save()
Write-Host "[1/2] Startup folder shortcut updated at: $shortcutPath" -ForegroundColor Green

# 2. Register Windows Scheduled Task Watchdog
try {
    $action = New-ScheduledTaskAction -Execute $targetExe -WorkingDirectory $coordDir
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0 -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

    Register-ScheduledTask -TaskName "LocalActivityTracker" -Action $action -Trigger $trigger -Settings $settings -User $env:USERNAME -Force | Out-Null
    Write-Host "[2/2] Windows Scheduled Task registered: LocalActivityTracker" -ForegroundColor Green
    Write-Host "      - Trigger: At Logon of $env:USERNAME" -ForegroundColor Gray
    Write-Host "      - Watchdog: Auto-restart on crash (every 1 min)" -ForegroundColor Gray
    Write-Host "      - Execution Mode: Interactive User Session (Session 1+)" -ForegroundColor Gray
} catch {
    Write-Warning "Could not register Windows Scheduled Task: $_"
}

Write-Host "`nAutomation configured successfully! The tracker will run automatically on every PC boot." -ForegroundColor Cyan
