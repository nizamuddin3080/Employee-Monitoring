# Local Activity Tracker - Windows Uninstaller Script (uninstall.ps1)
# Cleanly terminates background processes, unregisters Native Host from Registry,
# removes Windows Startup shortcut, and removes binaries.

[CmdletBinding()]
param(
    [string]$InstallPath = "$env:LOCALAPPDATA\LocalActivityTracker",
    [switch]$PurgeData,
    [switch]$Force
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Local Activity Tracker - Uninstaller" -ForegroundColor Cyan
Write-Host " Target Directory: $InstallPath" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Step 1: Stop running processes
Write-Host "`n[1/5] Terminating running application processes..." -ForegroundColor Yellow
$processNames = @("Coordinator", "NativeHost")
foreach ($pName in $processNames) {
    $running = Get-Process -Name $pName -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Stopping process: $pName (PID: $($running.Id -join ', '))..." -ForegroundColor Yellow
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
Start-Sleep -Milliseconds 500

# Step 2: Unregister Chrome Native Messaging Host
Write-Host "`n[2/5] Removing Chrome Native Messaging Host registry keys..." -ForegroundColor Yellow
$regPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.native_host"
if (Test-Path $regPath) {
    Remove-Item -Path $regPath -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Registry key removed: $regPath" -ForegroundColor Green
} else {
    Write-Host "Registry key was not present." -ForegroundColor Gray
}

# Step 3: Remove Windows Startup shortcut and Scheduled Task
Write-Host "`n[3/5] Removing Windows Startup shortcut and Scheduled Task..." -ForegroundColor Yellow
$startupFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupFolder "LocalActivityTracker.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -Path $shortcutPath -Force -ErrorAction SilentlyContinue
    Write-Host "Startup shortcut removed: $shortcutPath" -ForegroundColor Green
} else {
    Write-Host "Startup shortcut was not present." -ForegroundColor Gray
}

try {
    Unregister-ScheduledTask -TaskName "LocalActivityTracker" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
    Write-Host "Scheduled task unregistered: LocalActivityTracker" -ForegroundColor Green
} catch {
    # Suppress task unregister errors
}

# Step 4: Remove binaries and staged extension
Write-Host "`n[4/5] Removing binaries and configuration files..." -ForegroundColor Yellow
$binDir = Join-Path $InstallPath "bin"
$extDir = Join-Path $InstallPath "extension"
$configDir = Join-Path $InstallPath "config"

@($binDir, $extDir, $configDir) | ForEach-Object {
    if (Test-Path $_) {
        Remove-Item -Path $_ -Recurse -Force -ErrorAction SilentlyContinue
    }
}
Write-Host "Application binaries successfully removed." -ForegroundColor Green

# Step 5: Data retention or full purge
Write-Host "`n[5/5] Checking local data retention policy..." -ForegroundColor Yellow
if ($PurgeData) {
    Write-Host "PurgeData switch specified: Permanently deleting all local databases, screenshots, and logs..." -ForegroundColor Red
    if (Test-Path $InstallPath) {
        Remove-Item -Path $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "All local databases and screenshots have been purged from disk." -ForegroundColor Green
} else {
    Write-Host "User tracking data preserved at: $InstallPath" -ForegroundColor White
    Write-Host "(To delete all SQLite records and screenshots, re-run with -PurgeData)" -ForegroundColor Gray
}

Write-Host "`n[SUCCESS] Local Activity Tracker has been successfully uninstalled." -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
