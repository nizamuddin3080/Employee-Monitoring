$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  if (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") {
    $env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"
  } else {
    throw '.NET SDK missing. Install .NET 8 SDK.'
  }
}
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'npm missing. Install Node.js LTS.' }

Write-Host 'Building .NET Solution...'
dotnet build .\LocalActivityTracker.sln -c Release
if ($LASTEXITCODE -ne 0) { throw '.NET build failed' }

Write-Host 'Running .NET Test Suite...'
dotnet test .\tests\LocalActivityTracker.Tests\LocalActivityTracker.Tests.csproj -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw '.NET tests failed' }

foreach ($dir in @('src/ChromeExtension','src/LocalDashboard')) {
  Push-Location $dir
  try {
    npm install --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "npm install failed in $dir" }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm build failed in $dir" }
  } finally { Pop-Location }
}
Write-Host 'All builds and tests passed successfully.'
