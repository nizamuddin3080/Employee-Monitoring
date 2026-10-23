using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace LocalActivityTracker.Core.Repositories;

public class ProfileRepository : IProfileRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public ProfileRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertProfileAsync(ChromeProfile profile, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO chrome_profiles (
                profile_id, profile_name, chrome_profile_dir, is_enrolled, 
                connection_status, enrolled_at_utc, last_seen_utc, last_screenshot_at_utc
            ) VALUES (
                @profile_id, @profile_name, @chrome_profile_dir, @is_enrolled, 
                @connection_status, @enrolled_at_utc, @last_seen_utc, @last_screenshot_at_utc
            )
            ON CONFLICT(profile_id) DO UPDATE SET
                profile_name = excluded.profile_name,
                chrome_profile_dir = COALESCE(excluded.chrome_profile_dir, chrome_profiles.chrome_profile_dir),
                is_enrolled = excluded.is_enrolled,
                connection_status = excluded.connection_status,
                last_seen_utc = excluded.last_seen_utc;
        ";

        cmd.Parameters.AddWithValue("@profile_id", profile.ProfileId);
        cmd.Parameters.AddWithValue("@profile_name", profile.ProfileName);
        cmd.Parameters.AddWithValue("@chrome_profile_dir", (object?)profile.ChromeProfileDir ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is_enrolled", profile.IsEnrolled ? 1 : 0);
        cmd.Parameters.AddWithValue("@connection_status", profile.ConnectionStatus);
        cmd.Parameters.AddWithValue("@enrolled_at_utc", profile.EnrolledAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@last_seen_utc", profile.LastSeenUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@last_screenshot_at_utc", profile.LastScreenshotAtUtc.HasValue ? profile.LastScreenshotAtUtc.Value.ToString("o") : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ChromeProfile?> GetProfileByIdAsync(string profileId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            SELECT profile_id, profile_name, chrome_profile_dir, is_enrolled,
                   connection_status, enrolled_at_utc, last_seen_utc, last_screenshot_at_utc
            FROM chrome_profiles
            WHERE profile_id = @profile_id;
        ";
        cmd.Parameters.AddWithValue("@profile_id", profileId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapProfile(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<ChromeProfile>> GetAllProfilesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<ChromeProfile>();
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            SELECT profile_id, profile_name, chrome_profile_dir, is_enrolled,
                   connection_status, enrolled_at_utc, last_seen_utc, last_screenshot_at_utc
            FROM chrome_profiles
            ORDER BY profile_name ASC;
        ";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapProfile(reader));
        }

        return list;
    }

    public async Task UpdateConnectionStatusAsync(string profileId, string status, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            UPDATE chrome_profiles
            SET connection_status = @status, last_seen_utc = @now
            WHERE profile_id = @profile_id;
        ";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@profile_id", profileId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateLastSeenAsync(string profileId, DateTime lastSeenUtc, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            UPDATE chrome_profiles
            SET last_seen_utc = @last_seen
            WHERE profile_id = @profile_id;
        ";
        cmd.Parameters.AddWithValue("@last_seen", lastSeenUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@profile_id", profileId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateLastScreenshotTimeAsync(string profileId, DateTime screenshotTimeUtc, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            UPDATE chrome_profiles
            SET last_screenshot_at_utc = @screenshot_time
            WHERE profile_id = @profile_id;
        ";
        cmd.Parameters.AddWithValue("@screenshot_time", screenshotTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@profile_id", profileId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ChromeProfile MapProfile(SqliteDataReader reader)
    {
        return new ChromeProfile
        {
            ProfileId = reader.GetString(0),
            ProfileName = reader.GetString(1),
            ChromeProfileDir = reader.IsDBNull(2) ? null : reader.GetString(2),
            IsEnrolled = reader.GetInt32(3) == 1,
            ConnectionStatus = reader.GetString(4),
            EnrolledAtUtc = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            LastSeenUtc = DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
            LastScreenshotAtUtc = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}
