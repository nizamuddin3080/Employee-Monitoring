# Local Activity Tracker - Windows Installer Script (install.ps1)
# Production-grade setup script for Windows 10 & 11 interactive user session.

[CmdletBinding()]
param(
    [string]$InstallPath = "$env:LOCALAPPDATA\LocalActivityTracker",
    [switch]$SkipBuild,
    [switch]$LaunchAfterInstall
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Local Activity Tracker - Setup & Installation (Windows 10/11)" -ForegroundColor Cyan
Write-Host " Target Installation Directory: $InstallPath" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$root = Resolve-Path "$PSScriptRoot\.."

# Step 1: Build projects if not skipped
if (-not $SkipBuild) {
    Write-Host "`n[1/7] Building production binaries and bundling assets..." -ForegroundColor Yellow
    & "$root\build.ps1"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed with exit code $LASTEXITCODE. Installation aborted."
        exit 1
    }
} else {
    Write-Host "`n[1/7] Skipping build (using existing release artifacts)..." -ForegroundColor Gray
}

# Step 2: Create directory structure
Write-Host "`n[2/7] Initializing directory structure..." -ForegroundColor Yellow
$binDir = Join-Path $InstallPath "bin"
$coordDir = Join-Path $binDir "coordinator"
$hostDir = Join-Path $binDir "nativehost"
$extDir = Join-Path $InstallPath "extension"
$configDir = Join-Path $InstallPath "config"
$dataDir = Join-Path $InstallPath "data"
$screenshotsDir = Join-Path $InstallPath "screenshots"
$logsDir = Join-Path $InstallPath "logs"
$exportsDir = Join-Path $InstallPath "exports"
$backupsDir = Join-Path $InstallPath "backups"

@($InstallPath, $binDir, $coordDir, $hostDir, $extDir, $configDir, $dataDir, $screenshotsDir, $logsDir, $exportsDir, $backupsDir) | ForEach-Object {
    if (-not (Test-Path $_)) {
        New-Item -ItemType Directory -Path $_ -Force | Out-Null
    }
}

# Step 3: Copy compiled binaries
Write-Host "`n[3/7] Copying executables and static assets..." -ForegroundColor Yellow

$coordSource = Join-Path $root "src\Coordinator\bin\Release\net8.0\win-x64\publish"
if (-not (Test-Path $coordSource)) {
    $coordSource = Join-Path $root "src\Coordinator\bin\Release\net8.0"
}
$hostSource = Join-Path $root "src\NativeHost\bin\Release\net8.0\win-x64\publish"
if (-not (Test-Path $hostSource)) {
    $hostSource = Join-Path $root "src\NativeHost\bin\Release\net8.0"
}

Copy-Item -Path "$coordSource\*" -Destination $coordDir -Recurse -Force
Copy-Item -Path "$hostSource\*" -Destination $hostDir -Recurse -Force

# Copy bundled SPA to coordinator wwwroot
$wwwrootDir = Join-Path $coordDir "wwwroot"
if (-not (Test-Path $wwwrootDir)) { New-Item -ItemType Directory -Path $wwwrootDir -Force | Out-Null }
Copy-Item -Path "$dashSource\*" -Destination $wwwrootDir -Recurse -Force

# Copy Chrome extension bundle
Copy-Item -Path "$extSource\*" -Destination $extDir -Recurse -Force

Write-Host "Binaries successfully staged to: $binDir" -ForegroundColor Green

# Step 4: Generate and register Chrome Native Messaging Host manifests
Write-Host "`n[4/7] Registering Chrome Native Messaging Host..." -ForegroundColor Yellow
$hostExePath = (Join-Path $hostDir "NativeHost.exe").Replace("\", "\\")

# 1. Register com.localactivitytracker.agent (used by extension)
$manifestAgentPath = Join-Path $configDir "com.localactivitytracker.agent.json"
$manifestAgentContent = @"
{
  "name": "com.localactivitytracker.agent",
  "description": "Local Activity Tracker Chrome Native Messaging Host",
  "path": "$hostExePath",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://*/"
  ]
}
"@
[System.IO.File]::WriteAllText($manifestAgentPath, $manifestAgentContent, [System.Text.Encoding]::UTF8)
$regAgentPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.agent"
if (-not (Test-Path $regAgentPath)) { New-Item -Path $regAgentPath -Force | Out-Null }
Set-ItemProperty -Path $regAgentPath -Name "(Default)" -Value $manifestAgentPath -Force

# 2. Register com.localactivitytracker.native_host
$manifestNativePath = Join-Path $configDir "com.localactivitytracker.native_host.json"
$manifestNativeContent = @"
{
  "name": "com.localactivitytracker.native_host",
  "description": "Local Activity Tracker Chrome Native Messaging Host",
  "path": "$hostExePath",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://*/"
  ]
}
"@
[System.IO.File]::WriteAllText($manifestNativePath, $manifestNativeContent, [System.Text.Encoding]::UTF8)
$regNativePath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.native_host"
if (-not (Test-Path $regNativePath)) { New-Item -Path $regNativePath -Force | Out-Null }
Set-ItemProperty -Path $regNativePath -Name "(Default)" -Value $manifestNativePath -Force

Write-Host "Native host registered in registry under .agent and .native_host" -ForegroundColor Green

# Step 5: Create Windows Startup Shortcut
Write-Host "`n[5/7] Creating Windows Interactive Startup Shortcut..." -ForegroundColor Yellow
$startupFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupFolder "LocalActivityTracker.lnk"
$targetExe = Join-Path $coordDir "Coordinator.exe"

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $coordDir
$shortcut.Description = "Local Activity Tracker Interactive Coordinator"
$shortcut.Save()
Write-Host "Startup shortcut created at: $shortcutPath" -ForegroundColor Green

# Step 6: Enforce Strict NTFS Filesystem ACLs
Write-Host "`n[6/7] Enforcing strict NTFS ACLs (Current User + SYSTEM only)..." -ForegroundColor Yellow
& "$PSScriptRoot\harden-storage-acl.ps1" -TargetDirectory $InstallPath

# Step 7: Launch or display instructions
Write-Host "`n[7/7] Installation Complete!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Multi-Profile Chrome Extension Enrollment Instructions:" -ForegroundColor Cyan
Write-Host " 1. Open Google Chrome in the profile you wish to enroll." -ForegroundColor White
Write-Host " 2. Navigate to: chrome://extensions/" -ForegroundColor White
Write-Host " 3. Toggle ON 'Developer mode' in the top-right corner." -ForegroundColor White
Write-Host " 4. Click 'Load unpacked' and select the extension directory:" -ForegroundColor White
Write-Host "    $extDir" -ForegroundColor Yellow
Write-Host " 5. Click the Local Activity Tracker extension icon in your toolbar" -ForegroundColor White
Write-Host "    to verify the green CONNECTED badge." -ForegroundColor White
Write-Host " 6. Repeat steps 1-5 for every profile you want to enroll." -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Local Dashboard URL: http://127.0.0.1:48120" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Ensure DOTNET_ROOT is registered in User environment
if (-not $env:DOTNET_ROOT) {
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCmd) {
        $env:DOTNET_ROOT = Split-Path -Path $dotnetCmd.Source -Parent
    } elseif (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet") {
        $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
    }
}
if ($env:DOTNET_ROOT) {
    [System.Environment]::SetEnvironmentVariable('DOTNET_ROOT', $env:DOTNET_ROOT, 'User')
}

if ($LaunchAfterInstall) {
    Write-Host "`nLaunching Coordinator in interactive desktop session..." -ForegroundColor Green
    Start-Process -FilePath $targetExe -WorkingDirectory $coordDir
}
