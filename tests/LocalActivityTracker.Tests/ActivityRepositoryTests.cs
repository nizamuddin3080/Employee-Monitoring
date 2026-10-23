using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Repositories;
using Xunit;

namespace LocalActivityTracker.Tests;

public class ActivityRepositoryTests
{
    [Fact]
    public async Task UrlActivitySession_Lifecycle_OpensUpdatesAndClosesCleanly()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "LAT_ActTest_" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(tempDb);
        var runner = new MigrationRunner(factory);
        await runner.ApplyMigrationsAsync();

        var profRepo = new ProfileRepository(factory);
        var actRepo = new ActivityRepository(factory);

        try
        {
            // Seed a profile
            var profileId = Guid.NewGuid().ToString();
            await profRepo.UpsertProfileAsync(new ChromeProfile
            {
                ProfileId = profileId,
                ProfileName = "Default"
            });

            // Act 1: Open URL session
            var sessionId = Guid.NewGuid().ToString();
            var startTime = DateTime.UtcNow.AddMinutes(-10);
            var session = new UrlActivitySession
            {
                SessionId = sessionId,
                ProfileId = profileId,
                ChromeTabId = 101,
                Url = "https://github.com/udaysharmadev/Gravitas",
                SanitizedUrl = "https://github.com/udaysharmadev/Gravitas",
                Domain = "github.com",
                PageTitle = "GitHub - udaysharmadev/Gravitas",
                StartTimeUtc = startTime,
                LastHeartbeatUtc = startTime,
                ActiveDurationSeconds = 0,
                IsCompleted = false,
                SessionStatus = "ACTIVE"
            };

            await actRepo.OpenUrlSessionAsync(session);

            var retrieved = await actRepo.GetUrlSessionByIdAsync(sessionId);
            Assert.NotNull(retrieved);
            Assert.False(retrieved.IsCompleted);
            Assert.Equal("ACTIVE", retrieved.SessionStatus);
            Assert.Equal("github.com", retrieved.Domain);

            // Act 2: Heartbeat
            var heartbeat = startTime.AddMinutes(5);
            await actRepo.UpdateUrlSessionHeartbeatAsync(sessionId, heartbeat);

            var afterHeartbeat = await actRepo.GetUrlSessionByIdAsync(sessionId);
            Assert.NotNull(afterHeartbeat);
            Assert.Equal(heartbeat.ToString("o"), afterHeartbeat.LastHeartbeatUtc.ToString("o"));

            // Act 3: Close
            var endTime = startTime.AddMinutes(10);
            await actRepo.CloseUrlSessionAsync(sessionId, endTime, 600, "COMPLETED");

            var closed = await actRepo.GetUrlSessionByIdAsync(sessionId);
            Assert.NotNull(closed);
            Assert.True(closed.IsCompleted);
            Assert.Equal("COMPLETED", closed.SessionStatus);
            Assert.Equal(600, closed.ActiveDurationSeconds);
            Assert.NotNull(closed.EndTimeUtc);

            // Act 4: Range Query
            var rangeList = await actRepo.GetUrlSessionsInRangeAsync(profileId, startTime.AddMinutes(-5), endTime.AddMinutes(5));
            Assert.Single(rangeList);
            Assert.Equal(sessionId, rangeList[0].SessionId);
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
    public async Task ApplicationSession_Lifecycle_TracksDurationWithoutWindowTitles()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "LAT_AppTest_" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(tempDb);
        var runner = new MigrationRunner(factory);
        await runner.ApplyMigrationsAsync();

        var actRepo = new ActivityRepository(factory);

        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var startTime = DateTime.UtcNow.AddMinutes(-30);
            var appSession = new ApplicationSession
            {
                SessionId = sessionId,
                ApplicationDisplayName = "Visual Studio Code",
                ProcessName = "Code.exe",
                StartTimeUtc = startTime,
                LastHeartbeatUtc = startTime,
                DurationSeconds = 0,
                IsCompleted = false,
                SessionStatus = "ACTIVE"
            };

            // Act 1: Open session
            await actRepo.OpenApplicationSessionAsync(appSession);

            var retrieved = await actRepo.GetApplicationSessionByIdAsync(sessionId);
            Assert.NotNull(retrieved);
            Assert.Equal("Code.exe", retrieved.ProcessName);
            Assert.Equal("Visual Studio Code", retrieved.ApplicationDisplayName);
            Assert.False(retrieved.IsCompleted);

            // Act 2: Close session
            var endTime = startTime.AddMinutes(30);
            await actRepo.CloseApplicationSessionAsync(sessionId, endTime, 1800, "COMPLETED");

            var closed = await actRepo.GetApplicationSessionByIdAsync(sessionId);
            Assert.NotNull(closed);
            Assert.True(closed.IsCompleted);
            Assert.Equal(1800, closed.DurationSeconds);
            Assert.Equal("COMPLETED", closed.SessionStatus);

            // Act 3: Range query
            var list = await actRepo.GetApplicationSessionsInRangeAsync(startTime.AddMinutes(-10), endTime.AddMinutes(10));
            Assert.Single(list);
            Assert.Equal(sessionId, list[0].SessionId);
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
    public async Task ScreenshotMetadata_Crud_FunctionsAccurately()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "LAT_ShotTest_" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(tempDb);
        var runner = new MigrationRunner(factory);
        await runner.ApplyMigrationsAsync();

        var profRepo = new ProfileRepository(factory);
        var actRepo = new ActivityRepository(factory);

        try
        {
            var profileId = Guid.NewGuid().ToString();
            await profRepo.UpsertProfileAsync(new ChromeProfile { ProfileId = profileId, ProfileName = "Default" });

            var shotId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            var meta = new ScreenshotMetadata
            {
                ScreenshotId = shotId,
                ProfileId = profileId,
                CaptureTimeUtc = now,
                Url = "https://github.com",
                FilePath = "C:\\test\\shot.png",
                FileSizeBytes = 102400,
                CaptureStatus = "SUCCESS"
            };

            // Act 1: Insert
            await actRepo.InsertScreenshotMetadataAsync(meta);

            // Act 2: Retrieve
            var retrieved = await actRepo.GetScreenshotMetadataByIdAsync(shotId);
            Assert.NotNull(retrieved);
            Assert.Equal("SUCCESS", retrieved.CaptureStatus);
            Assert.Equal(102400, retrieved.FileSizeBytes);

            // Act 3: Range query
            var list = await actRepo.GetScreenshotsInRangeAsync(profileId, now.AddMinutes(-5), now.AddMinutes(5));
            Assert.Single(list);

            // Act 4: Delete
            var deleted = await actRepo.DeleteScreenshotMetadataAsync(shotId);
            Assert.True(deleted);

            var afterDelete = await actRepo.GetScreenshotMetadataByIdAsync(shotId);
            Assert.Null(afterDelete);
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
