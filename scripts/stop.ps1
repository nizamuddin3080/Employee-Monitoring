# Local Activity Tracker - Stop Running Background Processes
[CmdletBinding()]
param()

$processNames = @("Coordinator", "NativeHost")
$found = $false

foreach ($name in $processNames) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($procs) {
        $found = $true
        Write-Host "Stopping process: $name (PIDs: $($procs.Id -join ', '))..." -ForegroundColor Yellow
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

if ($found) {
    Write-Host "Local Activity Tracker processes have been stopped." -ForegroundColor Green
} else {
    Write-Host "No active Local Activity Tracker processes found." -ForegroundColor Gray
}
