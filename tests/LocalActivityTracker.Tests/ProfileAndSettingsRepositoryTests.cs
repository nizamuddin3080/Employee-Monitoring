using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Repositories;
using Xunit;

namespace LocalActivityTracker.Tests;

public class ProfileAndSettingsRepositoryTests
{
    [Fact]
    public async Task ProfileRepository_PerformsUpsert_AndUpdatesStatusCleanly()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "LAT_ProfTest_" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(tempDb);
        var runner = new MigrationRunner(factory);
        await runner.ApplyMigrationsAsync();

        var repo = new ProfileRepository(factory);

        try
        {
            var profileId = Guid.NewGuid().ToString();
            var profile = new ChromeProfile
            {
                ProfileId = profileId,
                ProfileName = "Work Profile",
                ChromeProfileDir = "Profile 1",
                IsEnrolled = true,
                ConnectionStatus = "CONNECTED",
                EnrolledAtUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow
            };

            // Act 1: Upsert
            await repo.UpsertProfileAsync(profile);

            // Assert 1: Get by ID
            var retrieved = await repo.GetProfileByIdAsync(profileId);
            Assert.NotNull(retrieved);
            Assert.Equal("Work Profile", retrieved.ProfileName);
            Assert.Equal("CONNECTED", retrieved.ConnectionStatus);
            Assert.True(retrieved.IsEnrolled);

            // Act 2: Update Connection Status
            await repo.UpdateConnectionStatusAsync(profileId, "DISCONNECTED");
            var updated = await repo.GetProfileByIdAsync(profileId);
            Assert.NotNull(updated);
            Assert.Equal("DISCONNECTED", updated.ConnectionStatus);

            // Act 3: GetAllProfiles
            var all = await repo.GetAllProfilesAsync();
            Assert.Single(all);
            Assert.Equal(profileId, all[0].ProfileId);
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

    [Fact]
    public async Task SettingsRepository_ReadsAndUpdatesSettings()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "LAT_SetTest_" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(tempDb);
        var runner = new MigrationRunner(factory);
        await runner.ApplyMigrationsAsync();

        var repo = new SettingsRepository(factory);

        try
        {
            // Default setting seeded from migration
            var interval = await repo.GetSettingAsync("screenshot_interval_seconds");
            Assert.Equal("600", interval);

            // Update setting
            await repo.SetSettingAsync("screenshot_interval_seconds", "300");
            var updatedInterval = await repo.GetSettingAsync("screenshot_interval_seconds");
            Assert.Equal("300", updatedInterval);

            // Insert new custom setting
            await repo.SetSettingAsync("custom_feature_flag", "true");
            var customVal = await repo.GetSettingAsync("custom_feature_flag");
            Assert.Equal("true", customVal);

            // Read all
            var all = await repo.GetAllSettingsAsync();
            Assert.True(all.Count >= 5);
            Assert.Equal("300", all["screenshot_interval_seconds"]);
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
