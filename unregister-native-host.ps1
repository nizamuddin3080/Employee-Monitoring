# Unregister Chrome Native Messaging Host from HKCU
$ErrorActionPreference = 'Stop'

$regKeyPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.localactivitytracker.agent"
if (Test-Path $regKeyPath) {
    Remove-Item -Path $regKeyPath -Force -Recurse
    Write-Host "Unregistered Native Messaging Host: $regKeyPath"
} else {
    Write-Host "Native Messaging Host key was not present in registry."
}
