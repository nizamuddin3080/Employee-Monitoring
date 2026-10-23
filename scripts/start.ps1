# Local Activity Tracker - Start Coordinator in Interactive User Session
[CmdletBinding()]
param(
    [string]$InstallPath = "$env:LOCALAPPDATA\LocalActivityTracker"
)

$targetExe = Join-Path $InstallPath "bin\coordinator\Coordinator.exe"
if (-not (Test-Path $targetExe)) {
    # Check local solution output
    $root = Resolve-Path "$PSScriptRoot\.."
    $targetExe = Join-Path $root "src\Coordinator\bin\Release\net8.0\Coordinator.exe"
}

if (-not (Test-Path $targetExe)) {
    Write-Error "Coordinator.exe not found. Please run scripts\install.ps1 or build.ps1 first."
    exit 1
}

# Ensure DOTNET_ROOT is available in process and user environment
if (-not $env:DOTNET_ROOT) {
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCmd) {
        $env:DOTNET_ROOT = Split-Path -Path $dotnetCmd.Source -Parent
    } elseif (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet") {
        $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
    }
    if ($env:DOTNET_ROOT) {
        [System.Environment]::SetEnvironmentVariable('DOTNET_ROOT', $env:DOTNET_ROOT, 'User')
    }
}

$workingDir = Split-Path -Path $targetExe -Parent
Write-Host "Launching Local Activity Tracker Coordinator in interactive desktop session..." -ForegroundColor Green
Start-Process -FilePath $targetExe -WorkingDirectory $workingDir
Write-Host "Coordinator running. Local Dashboard available at: http://127.0.0.1:48120" -ForegroundColor Cyan
