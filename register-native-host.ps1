# Register Chrome Native Messaging Host in HKCU (per-user registry)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$exePath = Join-Path $PSScriptRoot "src\NativeHost\bin\Release\net8.0\win-x64\publish\NativeHost.exe"
if (-not (Test-Path $exePath)) {
    $exePath = Join-Path $PSScriptRoot "src\NativeHost\bin\Release\net8.0\NativeHost.exe"
}
if (-not (Test-Path $exePath)) {
    $exePath = Join-Path $PSScriptRoot "src\NativeHost\bin\Debug\net8.0\NativeHost.exe"
}

if (-not (Test-Path $exePath)) {
    Write-Host "NativeHost.exe not found. Building NativeHost project..."
    if (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") {
        $env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"
    }
    dotnet build .\src\NativeHost\NativeHost.csproj -c Release
    $exePath = Join-Path $PSScriptRoot "src\NativeHost\bin\Release\net8.0\NativeHost.exe"
}

if (-not (Test-Path $exePath)) {
    throw "Failed to find or build NativeHost.exe at $exePath"
}

$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$configDir = Join-Path $localAppData "LocalActivityTracker\config"
if (-not (Test-Path $configDir)) {
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
}

$manifestPath = Join-Path $configDir "com.localactivitytracker.agent.json"
$escapedExePath = $exePath.Replace('\', '\\')

$manifestJson = @"
{
  "name": "com.localactivitytracker.agent",
  "description": "Local Activity Tracker Chrome Native Messaging Host",
  "path": "$escapedExePath",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://lbdhlaapcbkigemlepkcmdhmdjincjoc/",
    "chrome-extension://aaimpoinflcehfkndfccceceaafcmnoc/"
  ]
}
"@

[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.Encoding]::UTF8)
Write-Host "Generated native host manifest at: $manifestPath"

$regKeyPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.agent"
if (-not (Test-Path $regKeyPath)) {
    New-Item -Path $regKeyPath -Force | Out-Null
}

Set-ItemProperty -Path $regKeyPath -Name "(Default)" -Value $manifestPath
Write-Host "Registered Native Messaging Host in registry: $regKeyPath"

# Also register com.localactivitytracker.native_host alias
$nativeHostKeyPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.native_host"
if (-not (Test-Path $nativeHostKeyPath)) {
    New-Item -Path $nativeHostKeyPath -Force | Out-Null
}
$nativeHostManifest = Join-Path $configDir "com.localactivitytracker.native_host.json"
$manifestNativeJson = $manifestJson.Replace("com.localactivitytracker.agent", "com.localactivitytracker.native_host")
[System.IO.File]::WriteAllText($nativeHostManifest, $manifestNativeJson, [System.Text.Encoding]::UTF8)
Set-ItemProperty -Path $nativeHostKeyPath -Name "(Default)" -Value $nativeHostManifest

# Verify
$regVal = (Get-ItemProperty -Path $regKeyPath)."(Default)"
if ($regVal -eq $manifestPath) {
    Write-Host "Native host registration verified successfully."
} else {
    throw "Registry verification failed: expected $manifestPath but got $regVal"
}
