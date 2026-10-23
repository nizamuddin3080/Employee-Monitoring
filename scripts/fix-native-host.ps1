# Fix Native Host runtime and registration for Chrome Extension
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$hostPublishDir = Join-Path $repoRoot "src\NativeHost\bin\Release\net8.0\win-x64\publish"
$coordPublishDir = Join-Path $repoRoot "src\Coordinator\bin\Release\net8.0\win-x64\publish"

# Ensure publish outputs exist
if (-not (Test-Path "$hostPublishDir\NativeHost.exe")) {
    Write-Host "Publishing NativeHost self-contained..."
    dotnet publish "$repoRoot\src\NativeHost\NativeHost.csproj" -c Release -r win-x64 --self-contained true
}
if (-not (Test-Path "$coordPublishDir\Coordinator.exe")) {
    Write-Host "Publishing Coordinator self-contained..."
    dotnet publish "$repoRoot\src\Coordinator\Coordinator.csproj" -c Release -r win-x64 --self-contained true
}

# 1. Update repo bin directories so running from repo works
$repoHostBin = Join-Path $repoRoot "src\NativeHost\bin\Release\net8.0"
$repoCoordBin = Join-Path $repoRoot "src\Coordinator\bin\Release\net8.0"
Copy-Item -Path "$hostPublishDir\*" -Destination $repoHostBin -Recurse -Force
Copy-Item -Path "$coordPublishDir\*" -Destination $repoCoordBin -Recurse -Force

# 2. Update %LOCALAPPDATA%\LocalActivityTracker\bin directories
$installRoot = "$env:LOCALAPPDATA\LocalActivityTracker"
$installHostDir = Join-Path $installRoot "bin\nativehost"
$installCoordDir = Join-Path $installRoot "bin\coordinator"
$configDir = Join-Path $installRoot "config"

@($installHostDir, $installCoordDir, $configDir) | ForEach-Object {
    if (-not (Test-Path $_)) { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
}

try {
    Copy-Item -Path "$hostPublishDir\*" -Destination $installHostDir -Recurse -Force
} catch {
    Write-Warning "Could not update some files in host dir: $_"
}

try {
    Copy-Item -Path "$coordPublishDir\*" -Destination $installCoordDir -Recurse -Force
} catch {
    Write-Warning "Coordinator directory is currently locked by a running instance (skipping copy): $_"
}

# 3. Create manifest for com.localactivitytracker.agent
$hostExePath = (Join-Path $installHostDir "NativeHost.exe").Replace('\', '\\')
$manifestAgentPath = Join-Path $configDir "com.localactivitytracker.agent.json"
$manifestAgentJson = @"
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
[System.IO.File]::WriteAllText($manifestAgentPath, $manifestAgentJson, [System.Text.Encoding]::UTF8)

# 4. Create manifest for com.localactivitytracker.native_host
$manifestNativeHostPath = Join-Path $configDir "com.localactivitytracker.native_host.json"
$manifestNativeHostJson = @"
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
[System.IO.File]::WriteAllText($manifestNativeHostPath, $manifestNativeHostJson, [System.Text.Encoding]::UTF8)

# 5. Register BOTH manifests in HKCU
$regAgent = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.agent"
if (-not (Test-Path $regAgent)) { New-Item -Path $regAgent -Force | Out-Null }
Set-ItemProperty -Path $regAgent -Name "(Default)" -Value $manifestAgentPath -Force

$regNativeHost = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.native_host"
if (-not (Test-Path $regNativeHost)) { New-Item -Path $regNativeHost -Force | Out-Null }
Set-ItemProperty -Path $regNativeHost -Name "(Default)" -Value $manifestNativeHostPath -Force

Write-Host "SUCCESS: NativeHost is now self-contained and registered under both .agent and .native_host!" -ForegroundColor Green
