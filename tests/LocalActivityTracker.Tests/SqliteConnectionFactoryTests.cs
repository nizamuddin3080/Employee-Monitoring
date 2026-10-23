using LocalActivityTracker.Core.Database;
using Xunit;

namespace LocalActivityTracker.Tests;

public class SqliteConnectionFactoryTests
{
    [Fact]
    public async Task OpenConnectionAsync_ConfiguresWalModeAndPragmas()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "LAT_WalTest_" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(tempDb);

        try
        {
            using var connection = await factory.OpenConnectionAsync();

            // Verify journal_mode is wal
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode;";
                var journalMode = (string?)await cmd.ExecuteScalarAsync();
                Assert.Equal("wal", journalMode?.ToLowerInvariant());
            }

            // Verify foreign_keys is ON
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys;";
                var foreignKeys = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Assert.Equal(1, foreignKeys);
            }
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
