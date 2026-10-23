Add-Type -Path "C:\Users\h\AppData\Local\LocalActivityTracker\bin\coordinator\Microsoft.Data.Sqlite.dll"

$dbPath = "$env:LOCALAPPDATA\LocalActivityTracker\data\activity.db"
$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$dbPath")
$conn.Open()

Write-Host "=== TAB EVENTS ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT id, profile_id, event_type, chrome_tab_id, event_payload_json, event_time_utc FROM tab_events ORDER BY id DESC LIMIT 10"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host "$($r['id']) | $($r['profile_id']) | $($r['event_type']) | Tab: $($r['chrome_tab_id']) | $($r['event_time_utc'])"
}
$r.Close()

Write-Host "`n=== BROWSER SESSIONS ==="
$cmd.CommandText = "SELECT session_id, profile_id, start_time_utc, end_time_utc, duration_seconds, session_status FROM browser_sessions ORDER BY start_time_utc DESC LIMIT 5"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host "$($r['session_id']) | $($r['profile_id']) | $($r['start_time_utc']) | $($r['duration_seconds'])s | $($r['session_status'])"
}
$r.Close()

Write-Host "`n=== URL ACTIVITY SESSIONS ==="
$cmd.CommandText = "SELECT session_id, profile_id, domain, sanitized_url, active_duration_seconds, session_status FROM url_activity_sessions ORDER BY start_time_utc DESC LIMIT 5"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host "$($r['session_id']) | $($r['domain']) | $($r['sanitized_url']) | $($r['active_duration_seconds'])s | $($r['session_status'])"
}
$r.Close()

Write-Host "`n=== APPLICATION SESSIONS ==="
$cmd.CommandText = "SELECT session_id, process_name, app_name, start_time_utc, duration_seconds, session_status FROM application_sessions ORDER BY start_time_utc DESC LIMIT 5"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host "$($r['session_id']) | $($r['process_name']) | $($r['app_name']) | $($r['duration_seconds'])s | $($r['session_status'])"
}
$r.Close()

$conn.Close()
