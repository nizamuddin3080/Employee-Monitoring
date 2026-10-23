using LocalActivityTracker.Core.Storage;

namespace LocalActivityTracker.Core.Database;

/// <summary>
/// Implements SQLite online backup (VACUUM INTO) and disk-safe data purge operations
/// unlinking physical screenshot files before database record deletion.
/// </summary>
public sealed class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly StoragePathProvider _storage;

    public DatabaseMaintenanceService(ISqliteConnectionFactory connectionFactory, StoragePathProvider storage)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        _storage.EnsureDirectoriesExist();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"activity_backup_{timestamp}.db";
        var backupFilePath = Path.Combine(_storage.BackupsDirectory, backupFileName);

        if (File.Exists(backupFilePath))
        {
            File.Delete(backupFilePath);
        }

        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        // SQLite online zero-locking backup
        cmd.CommandText = "VACUUM INTO @backup_path;";
        cmd.Parameters.AddWithValue("@backup_path", backupFilePath);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return backupFilePath;
    }

    public async Task<int> PurgeOldRecordsAsync(int olderThanDays, CancellationToken cancellationToken = default)
    {
        if (olderThanDays < 1)
        {
            throw new ArgumentException("Retention period must be at least 1 day.", nameof(olderThanDays));
        }

        var cutoffUtc = DateTime.UtcNow.AddDays(-olderThanDays).ToString("o");
        var deletedTotal = 0;

        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // 1. Locate and physically unlink associated screenshot PNG files from disk first
        var screenshotPathsToDelete = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT file_path FROM screenshot_metadata WHERE capture_time_utc < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", cutoffUtc);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    screenshotPathsToDelete.Add(reader.GetString(0));
                }
            }
        }

        foreach (var path in screenshotPathsToDelete)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Suppress disk unlinking errors
            }
        }

        // 2. Delete database records across all tracking tables within transaction
        using var transaction = connection.BeginTransaction();

        var tableQueries = new[]
        {
            ("screenshot_metadata", "capture_time_utc"),
            ("tab_events", "event_time_utc"),
            ("url_activity_sessions", "start_time_utc"),
            ("browser_sessions", "start_time_utc"),
            ("application_sessions", "start_time_utc"),
            ("tracking_events", "event_time_utc")
        };

        foreach (var (table, timeCol) in tableQueries)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $"DELETE FROM {table} WHERE {timeCol} < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", cutoffUtc);
            deletedTotal += await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return deletedTotal;
    }

    public async Task PurgeAllDataAsync(CancellationToken cancellationToken = default)
    {
        // 1. Delete all physical screenshot files from disk
        if (Directory.Exists(_storage.ScreenshotsDirectory))
        {
            try
            {
                var files = Directory.GetFiles(_storage.ScreenshotsDirectory, "*.png", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        // 2. Clear all tracking tables within transaction
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var tables = new[]
        {
            "screenshot_metadata",
            "tab_events",
            "url_activity_sessions",
            "browser_sessions",
            "application_sessions",
            "tracking_events"
        };

        foreach (var table in tables)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $"DELETE FROM {table};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();

        // 3. Reclaim freed SQLite disk space
        using (var vacuumCmd = connection.CreateCommand())
        {
            vacuumCmd.CommandText = "VACUUM;";
            await vacuumCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
