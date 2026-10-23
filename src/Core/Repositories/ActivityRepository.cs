using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace LocalActivityTracker.Core.Repositories;

public class ActivityRepository : IActivityRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public ActivityRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // --- URL Activity Sessions ---

    public async Task OpenUrlSessionAsync(UrlActivitySession session, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO url_activity_sessions (
                session_id, profile_id, window_session_id, chrome_tab_id,
                url, sanitized_url, domain, page_title, start_time_utc,
                end_time_utc, last_heartbeat_utc, active_duration_seconds,
                is_completed, session_status, completeness_notes
            ) VALUES (
                @session_id, @profile_id, @window_session_id, @chrome_tab_id,
                @url, @sanitized_url, @domain, @page_title, @start_time_utc,
                @end_time_utc, @last_heartbeat_utc, @active_duration_seconds,
                @is_completed, @session_status, @completeness_notes
            );
        ";

        cmd.Parameters.AddWithValue("@session_id", session.SessionId);
        cmd.Parameters.AddWithValue("@profile_id", session.ProfileId);
        cmd.Parameters.AddWithValue("@window_session_id", (object?)session.WindowSessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@chrome_tab_id", session.ChromeTabId);
        cmd.Parameters.AddWithValue("@url", session.Url);
        cmd.Parameters.AddWithValue("@sanitized_url", session.SanitizedUrl);
        cmd.Parameters.AddWithValue("@domain", session.Domain);
        cmd.Parameters.AddWithValue("@page_title", session.PageTitle);
        cmd.Parameters.AddWithValue("@start_time_utc", session.StartTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@end_time_utc", session.EndTimeUtc.HasValue ? session.EndTimeUtc.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@last_heartbeat_utc", session.LastHeartbeatUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@active_duration_seconds", session.ActiveDurationSeconds);
        cmd.Parameters.AddWithValue("@is_completed", session.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@session_status", session.SessionStatus);
        cmd.Parameters.AddWithValue("@completeness_notes", (object?)session.CompletenessNotes ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateUrlSessionHeartbeatAsync(string sessionId, DateTime heartbeatUtc, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            UPDATE url_activity_sessions
            SET last_heartbeat_utc = @heartbeat
            WHERE session_id = @session_id;
        ";
        cmd.Parameters.AddWithValue("@heartbeat", heartbeatUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CloseUrlSessionAsync(string sessionId, DateTime endTimeUtc, int activeDurationSeconds, string status = "COMPLETED", CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            UPDATE url_activity_sessions
            SET end_time_utc = @end_time,
                active_duration_seconds = @duration,
                is_completed = 1,
                session_status = @status,
                last_heartbeat_utc = @end_time
            WHERE session_id = @session_id;
        ";
        cmd.Parameters.AddWithValue("@end_time", endTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@duration", activeDurationSeconds);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<UrlActivitySession?> GetUrlSessionByIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            SELECT session_id, profile_id, window_session_id, chrome_tab_id,
                   url, sanitized_url, domain, page_title, start_time_utc,
                   end_time_utc, last_heartbeat_utc, active_duration_seconds,
                   is_completed, session_status, completeness_notes
            FROM url_activity_sessions
            WHERE session_id = @session_id;
        ";
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapUrlSession(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<UrlActivitySession>> GetUrlSessionsInRangeAsync(string profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var list = new List<UrlActivitySession>();
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            SELECT session_id, profile_id, window_session_id, chrome_tab_id,
                   url, sanitized_url, domain, page_title, start_time_utc,
                   end_time_utc, last_heartbeat_utc, active_duration_seconds,
                   is_completed, session_status, completeness_notes
            FROM url_activity_sessions
            WHERE profile_id = @profile_id
              AND start_time_utc <= @end_range
              AND (end_time_utc IS NULL OR end_time_utc >= @start_range)
            ORDER BY start_time_utc ASC;
        ";
        cmd.Parameters.AddWithValue("@profile_id", profileId);
        cmd.Parameters.AddWithValue("@start_range", startUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@end_range", endUtc.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapUrlSession(reader));
        }
        return list;
    }

    // --- Browser Sessions (Chrome Foreground) ---

    public async Task OpenBrowserSessionAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO browser_sessions (session_id, profile_id, start_time_utc, end_time_utc, duration_seconds, is_completed, session_status)
            VALUES (@session_id, @profile_id, @start_time_utc, @end_time_utc, @duration_seconds, @is_completed, @session_status);
        ";
        cmd.Parameters.AddWithValue("@session_id", session.SessionId);
        cmd.Parameters.AddWithValue("@profile_id", session.ProfileId);
        cmd.Parameters.AddWithValue("@start_time_utc", session.StartTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@end_time_utc", session.EndTimeUtc.HasValue ? session.EndTimeUtc.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@duration_seconds", session.DurationSeconds);
        cmd.Parameters.AddWithValue("@is_completed", session.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@session_status", session.SessionStatus);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CloseBrowserSessionAsync(string sessionId, DateTime endTimeUtc, int durationSeconds, string status = "COMPLETED", CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            UPDATE browser_sessions
            SET end_time_utc = @end_time,
                duration_seconds = @duration,
                is_completed = 1,
                session_status = @status
            WHERE session_id = @session_id;
        ";
        cmd.Parameters.AddWithValue("@end_time", endTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@duration", durationSeconds);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BrowserSession>> GetBrowserSessionsInRangeAsync(string? profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var list = new List<BrowserSession>();
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(profileId) && !profileId.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            cmd.CommandText = @"
                SELECT session_id, profile_id, start_time_utc, end_time_utc,
                       duration_seconds, is_completed, session_status
                FROM browser_sessions
                WHERE profile_id = @profile_id
                  AND start_time_utc <= @end_range
                  AND (end_time_utc IS NULL OR end_time_utc >= @start_range)
                ORDER BY start_time_utc ASC;
            ";
            cmd.Parameters.AddWithValue("@profile_id", profileId);
        }
        else
        {
            cmd.CommandText = @"
                SELECT session_id, profile_id, start_time_utc, end_time_utc,
                       duration_seconds, is_completed, session_status
                FROM browser_sessions
                WHERE start_time_utc <= @end_range
                  AND (end_time_utc IS NULL OR end_time_utc >= @start_range)
                ORDER BY start_time_utc ASC;
            ";
        }

        cmd.Parameters.AddWithValue("@start_range", startUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@end_range", endUtc.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new BrowserSession
            {
                SessionId = reader.GetString(0),
                ProfileId = reader.GetString(1),
                StartTimeUtc = DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                EndTimeUtc = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
                DurationSeconds = reader.GetInt32(4),
                IsCompleted = reader.GetInt32(5) == 1,
                SessionStatus = reader.GetString(6)
            });
        }

        return list;
    }

    public async Task InsertTabEventAsync(TabEvent tabEvent, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO tab_events (
                profile_id, session_id, chrome_tab_id,
                event_type, event_payload_json, event_time_utc
            ) VALUES (
                @profile_id, @session_id, @chrome_tab_id,
                @event_type, @event_payload_json, @event_time_utc
            );
        ";
        cmd.Parameters.AddWithValue("@profile_id", tabEvent.ProfileId);
        cmd.Parameters.AddWithValue("@session_id", (object?)tabEvent.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@chrome_tab_id", tabEvent.ChromeTabId);
        cmd.Parameters.AddWithValue("@event_type", tabEvent.EventType);
        cmd.Parameters.AddWithValue("@event_payload_json", (object?)tabEvent.EventPayloadJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@event_time_utc", tabEvent.EventTimeUtc.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // --- Non-Chrome Application Sessions (Strictly NO window_title) ---

    public async Task OpenApplicationSessionAsync(ApplicationSession session, CancellationToken cancellationToken = default)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (string.Equals(session.ProcessName, "chrome.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("chrome.exe cannot be tracked in application_sessions. Chrome activity must be tracked via Chrome sessions.");
        }

        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO application_sessions (
                session_id, application_display_name, process_name,
                start_time_utc, end_time_utc, last_heartbeat_utc,
                duration_seconds, is_completed, session_status
            ) VALUES (
                @session_id, @app_name, @proc_name,
                @start_time, @end_time, @heartbeat,
                @duration, @is_completed, @status
            );
        ";
        cmd.Parameters.AddWithValue("@session_id", session.SessionId);
        cmd.Parameters.AddWithValue("@app_name", session.ApplicationDisplayName);
        cmd.Parameters.AddWithValue("@proc_name", session.ProcessName);
        cmd.Parameters.AddWithValue("@start_time", session.StartTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@end_time", session.EndTimeUtc.HasValue ? session.EndTimeUtc.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@heartbeat", session.LastHeartbeatUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@duration", session.DurationSeconds);
        cmd.Parameters.AddWithValue("@is_completed", session.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@status", session.SessionStatus);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateApplicationSessionHeartbeatAsync(string sessionId, DateTime heartbeatUtc, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            UPDATE application_sessions
            SET last_heartbeat_utc = @heartbeat
            WHERE session_id = @session_id;
        ";
        cmd.Parameters.AddWithValue("@heartbeat", heartbeatUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CloseApplicationSessionAsync(string sessionId, DateTime endTimeUtc, int durationSeconds, string status = "COMPLETED", CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            UPDATE application_sessions
            SET end_time_utc = @end_time,
                duration_seconds = @duration,
                is_completed = 1,
                session_status = @status,
                last_heartbeat_utc = @end_time
            WHERE session_id = @session_id;
        ";
        cmd.Parameters.AddWithValue("@end_time", endTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@duration", durationSeconds);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ApplicationSession?> GetApplicationSessionByIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            SELECT session_id, application_display_name, process_name,
                   start_time_utc, end_time_utc, last_heartbeat_utc,
                   duration_seconds, is_completed, session_status
            FROM application_sessions
            WHERE session_id = @session_id;
        ";
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapApplicationSession(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<ApplicationSession>> GetApplicationSessionsInRangeAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var list = new List<ApplicationSession>();
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            SELECT session_id, application_display_name, process_name,
                   start_time_utc, end_time_utc, last_heartbeat_utc,
                   duration_seconds, is_completed, session_status
            FROM application_sessions
            WHERE start_time_utc <= @end_range
              AND (end_time_utc IS NULL OR end_time_utc >= @start_range)
              AND LOWER(process_name) != 'chrome.exe'
            ORDER BY start_time_utc ASC;
        ";
        cmd.Parameters.AddWithValue("@start_range", startUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@end_range", endUtc.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapApplicationSession(reader));
        }
        return list;
    }

    // --- Screenshot Metadata ---

    public async Task InsertScreenshotMetadataAsync(ScreenshotMetadata metadata, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO screenshot_metadata (
                screenshot_id, profile_id, url_session_id, capture_time_utc,
                url, file_path, file_size_bytes, capture_status, failure_reason
            ) VALUES (
                @screenshot_id, @profile_id, @url_session_id, @capture_time_utc,
                @url, @file_path, @file_size_bytes, @capture_status, @failure_reason
            );
        ";
        cmd.Parameters.AddWithValue("@screenshot_id", metadata.ScreenshotId);
        cmd.Parameters.AddWithValue("@profile_id", metadata.ProfileId);
        cmd.Parameters.AddWithValue("@url_session_id", (object?)metadata.UrlSessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@capture_time_utc", metadata.CaptureTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@url", metadata.Url);
        cmd.Parameters.AddWithValue("@file_path", metadata.FilePath);
        cmd.Parameters.AddWithValue("@file_size_bytes", metadata.FileSizeBytes);
        cmd.Parameters.AddWithValue("@capture_status", metadata.CaptureStatus);
        cmd.Parameters.AddWithValue("@failure_reason", (object?)metadata.FailureReason ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ScreenshotMetadata?> GetScreenshotMetadataByIdAsync(string screenshotId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            SELECT screenshot_id, profile_id, url_session_id, capture_time_utc,
                   url, file_path, file_size_bytes, capture_status, failure_reason
            FROM screenshot_metadata
            WHERE screenshot_id = @screenshot_id;
        ";
        cmd.Parameters.AddWithValue("@screenshot_id", screenshotId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapScreenshotMetadata(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<ScreenshotMetadata>> GetScreenshotsInRangeAsync(string? profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var list = new List<ScreenshotMetadata>();
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        if (string.IsNullOrEmpty(profileId))
        {
            cmd.CommandText = @"
                SELECT screenshot_id, profile_id, url_session_id, capture_time_utc,
                       url, file_path, file_size_bytes, capture_status, failure_reason
                FROM screenshot_metadata
                WHERE capture_time_utc BETWEEN @start_range AND @end_range
                ORDER BY capture_time_utc DESC;
            ";
        }
        else
        {
            cmd.CommandText = @"
                SELECT screenshot_id, profile_id, url_session_id, capture_time_utc,
                       url, file_path, file_size_bytes, capture_status, failure_reason
                FROM screenshot_metadata
                WHERE profile_id = @profile_id
                  AND capture_time_utc BETWEEN @start_range AND @end_range
                ORDER BY capture_time_utc DESC;
            ";
            cmd.Parameters.AddWithValue("@profile_id", profileId);
        }

        cmd.Parameters.AddWithValue("@start_range", startUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@end_range", endUtc.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapScreenshotMetadata(reader));
        }
        return list;
    }

    public async Task<bool> DeleteScreenshotMetadataAsync(string screenshotId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "DELETE FROM screenshot_metadata WHERE screenshot_id = @screenshot_id;";
        cmd.Parameters.AddWithValue("@screenshot_id", screenshotId);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<int> RecoverDanglingSessionsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var totalRecovered = 0;

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                UPDATE url_activity_sessions
                SET is_completed = 1,
                    end_time_utc = COALESCE(last_heartbeat_utc, start_time_utc),
                    active_duration_seconds = MAX(0, CAST(ROUND((julianday(COALESCE(last_heartbeat_utc, start_time_utc)) - julianday(start_time_utc)) * 86400) AS INTEGER)),
                    session_status = 'RECOVERED_AFTER_CRASH'
                WHERE is_completed = 0;
            ";
            totalRecovered += await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                UPDATE application_sessions
                SET is_completed = 1,
                    end_time_utc = COALESCE(last_heartbeat_utc, start_time_utc),
                    duration_seconds = MAX(0, CAST(ROUND((julianday(COALESCE(last_heartbeat_utc, start_time_utc)) - julianday(start_time_utc)) * 86400) AS INTEGER)),
                    session_status = 'RECOVERED_AFTER_CRASH'
                WHERE is_completed = 0;
            ";
            totalRecovered += await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                UPDATE browser_sessions
                SET is_completed = 1,
                    end_time_utc = start_time_utc,
                    duration_seconds = 0,
                    session_status = 'RECOVERED_AFTER_CRASH'
                WHERE is_completed = 0;
            ";
            totalRecovered += await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return totalRecovered;
    }

    // --- Helper Mappers ---

    private static UrlActivitySession MapUrlSession(SqliteDataReader reader)
    {
        return new UrlActivitySession
        {
            SessionId = reader.GetString(0),
            ProfileId = reader.GetString(1),
            WindowSessionId = reader.IsDBNull(2) ? null : reader.GetString(2),
            ChromeTabId = reader.GetInt32(3),
            Url = reader.GetString(4),
            SanitizedUrl = reader.GetString(5),
            Domain = reader.GetString(6),
            PageTitle = reader.GetString(7),
            StartTimeUtc = DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind),
            EndTimeUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind),
            LastHeartbeatUtc = DateTime.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind),
            ActiveDurationSeconds = reader.GetInt32(11),
            IsCompleted = reader.GetInt32(12) == 1,
            SessionStatus = reader.GetString(13),
            CompletenessNotes = reader.IsDBNull(14) ? null : reader.GetString(14)
        };
    }

    private static ApplicationSession MapApplicationSession(SqliteDataReader reader)
    {
        return new ApplicationSession
        {
            SessionId = reader.GetString(0),
            ApplicationDisplayName = reader.GetString(1),
            ProcessName = reader.GetString(2),
            StartTimeUtc = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
            EndTimeUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            LastHeartbeatUtc = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            DurationSeconds = reader.GetInt32(6),
            IsCompleted = reader.GetInt32(7) == 1,
            SessionStatus = reader.GetString(8)
        };
    }

    private static ScreenshotMetadata MapScreenshotMetadata(SqliteDataReader reader)
    {
        return new ScreenshotMetadata
        {
            ScreenshotId = reader.GetString(0),
            ProfileId = reader.GetString(1),
            UrlSessionId = reader.IsDBNull(2) ? null : reader.GetString(2),
            CaptureTimeUtc = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
            Url = reader.GetString(4),
            FilePath = reader.GetString(5),
            FileSizeBytes = reader.GetInt64(6),
            CaptureStatus = reader.GetString(7),
            FailureReason = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }
}
