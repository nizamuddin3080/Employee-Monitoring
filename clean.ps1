$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Get-ChildItem -Path src,tests -Recurse -Directory -ErrorAction SilentlyContinue | 
  Where-Object { $_.FullName -notmatch '[\\/]node_modules[\\/]' -and $_.Name -in @('bin','obj','dist') } | 
  Remove-Item -Recurse -Force
Write-Host 'Generated build outputs removed (node_modules preserved).'
