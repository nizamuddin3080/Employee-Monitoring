$token = (Invoke-RestMethod 'http://127.0.0.1:48120/api/auth/token').token
Write-Host "Token: $token"

$activity = Invoke-RestMethod 'http://127.0.0.1:48120/api/activity?page=1&pageSize=50' -Headers @{ Authorization = "Bearer $token" }
Write-Host "Total Records: $($activity.total_records)"
$activity.records | Format-Table -Property session_id, profile_name, domain, url, duration_seconds, session_status

$overview = Invoke-RestMethod 'http://127.0.0.1:48120/api/overview' -Headers @{ Authorization = "Bearer $token" }
Write-Host "Overview metrics:"
$overview | ConvertTo-Json
