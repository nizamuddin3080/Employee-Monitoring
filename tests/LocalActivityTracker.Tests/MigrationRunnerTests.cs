using LocalActivityTracker.Core.Database;
using Xunit;

namespace LocalActivityTracker.Tests;

public class MigrationRunnerTests
{
    [Fact]
    public async Task ApplyMigrationsAsync_CreatesAll10Tables_AndOmitsWindowTitle()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "LAT_MigTest_" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(tempDb);
        var runner = new MigrationRunner(factory);

        try
        {
            // Apply migrations
            await runner.ApplyMigrationsAsync();

            using var connection = await factory.OpenConnectionAsync();

            // Verify all 10 core tables exist
            var expectedTables = new[]
            {
                "schema_migrations",
                "chrome_profiles",
                "browser_windows",
                "browser_sessions",
                "url_activity_sessions",
                "tab_events",
                "screenshot_metadata",
                "application_sessions",
                "tracking_events",
                "application_settings"
            };

            foreach (var table in expectedTables)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @name;";
                cmd.Parameters.AddWithValue("@name", table);
                var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Assert.True(exists > 0, $"Expected table '{table}' was not found in database.");
            }

            // CRITICAL INVARIANT: Verify application_sessions does NOT contain window_title
            var columns = new List<string>();
            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA table_info(application_sessions);";
                using var reader = await pragmaCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1)); // column name
                }
            }

            Assert.DoesNotContain("window_title", columns);
            Assert.Contains("session_id", columns);
            Assert.Contains("application_display_name", columns);
            Assert.Contains("process_name", columns);
            Assert.Contains("start_time_utc", columns);
            Assert.Contains("end_time_utc", columns);
            Assert.Contains("duration_seconds", columns);

            // Verify migration recorded
            using (var migCmd = connection.CreateCommand())
            {
                migCmd.CommandText = "SELECT version, description, checksum FROM schema_migrations WHERE version = 1;";
                using var reader = await migCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1, reader.GetInt32(0));
                Assert.Contains("Initial schema", reader.GetString(1));
                Assert.False(string.IsNullOrWhiteSpace(reader.GetString(2)));
            }

            // Verify default settings seeded
            using (var setCmd = connection.CreateCommand())
            {
                setCmd.CommandText = "SELECT setting_value FROM application_settings WHERE setting_key = 'screenshot_interval_seconds';";
                var interval = (string?)await setCmd.ExecuteScalarAsync();
                Assert.Equal("600", interval);
            }

            // Verify idempotency: applying a second time should not throw
            await runner.ApplyMigrationsAsync();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) File.Delete(tempDb);
            var walFile = tempDb + "-wal";
            if (File.Exists(walFile)) File.Delete(walFile);
            var shmFile = tempDb + "-shm";
            if (File.Exists(shmFile)) File.Delete(shmFile);
        }
    }
}
