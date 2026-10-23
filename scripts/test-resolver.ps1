Add-Type -Path "C:\Users\h\Music\local Monitoring system\src\Core\bin\Release\net8.0\win-x64\publish\Core.dll"

$resolver = New-Object LocalActivityTracker.Core.Win32.ProcessInfoResolver

$chromeProcs = Get-Process -Name chrome -ErrorAction SilentlyContinue
foreach ($p in $chromeProcs) {
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
        $info = $resolver.Resolve($p.MainWindowHandle)
        $isChrome = $resolver.IsChromeProcess($info.ProcessName)
        Write-Host "HWND: $($p.MainWindowHandle) | Process: $($info.ProcessName) | Display: $($info.ApplicationDisplayName) | isChrome: $isChrome"
    }
}
