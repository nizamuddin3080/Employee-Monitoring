# Local Activity Tracker - Storage ACL Hardening Script
# Enforces strict NTFS permissions on %LOCALAPPDATA%\LocalActivityTracker
# Grants FullControl exclusively to current Windows user and SYSTEM.
# Strips all inherited and world-readable/writable permissions.

[CmdletBinding()]
param(
    [string]$TargetDirectory = "$env:LOCALAPPDATA\LocalActivityTracker"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Local Activity Tracker - NTFS ACL Hardening Tool" -ForegroundColor Cyan
Write-Host " Target Directory: $TargetDirectory" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path -Path $TargetDirectory)) {
    Write-Host "Creating target directory: $TargetDirectory..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null
}

$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$systemUser = "NT AUTHORITY\SYSTEM"

Write-Host "Restricting permissions to:" -ForegroundColor Green
Write-Host " - Current User: $currentUser (Full Control)" -ForegroundColor Green
Write-Host " - System User:  $systemUser (Full Control)" -ForegroundColor Green

# Use icacls to strip inheritance and grant explicit access
& icacls.exe "$TargetDirectory" /inheritance:r /grant:r "${currentUser}:(OI)(CI)F" /grant:r "${systemUser}:(OI)(CI)F" | Out-Null

# Verify ACLs using Get-Acl
$acl = Get-Acl -Path $TargetDirectory
Write-Host "`nVerified Access Control Entries:" -ForegroundColor Cyan
foreach ($access in $acl.Access) {
    Write-Host "  Principal: $($access.IdentityReference) | Rights: $($access.FileSystemRights) | Type: $($access.AccessControlType)" -ForegroundColor White
}

Write-Host "`n[SUCCESS] NTFS filesystem ACL hardening completed successfully." -ForegroundColor Green
