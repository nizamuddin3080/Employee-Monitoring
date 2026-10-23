using System.Diagnostics;
using LocalActivityTracker.Coordinator;
using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Storage;
using Xunit;

namespace LocalActivityTracker.Tests;

public class CoordinatorAppTests
{
    [Fact]
    public void CoordinatorApp_InteractiveSessionCheck_VerifiesNonZeroSession()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LAT_CoordTest_" + Guid.NewGuid().ToString("N"));
        var storage = new StoragePathProvider(tempDir);
        storage.EnsureDirectoriesExist();

        using var app = new CoordinatorApp(storage: storage, enableTray: false);

        var currentProcessSession = Process.GetCurrentProcess().SessionId;
        Assert.Equal(currentProcessSession, app.SessionId);
    }

    [Fact]
    public async Task CoordinatorApp_Lifecycle_StartsAppliesMigrationsAndStopsCleanly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LAT_CoordTest_" + Guid.NewGuid().ToString("N"));
        var storage = new StoragePathProvider(tempDir);
        storage.EnsureDirectoriesExist();

        using var app = new CoordinatorApp(storage: storage, enableTray: false);

        Assert.False(app.IsRunning);

        await app.StartAsync();
        Assert.True(app.IsRunning);
        Assert.False(app.IsTrackingPaused);

        // Verify database was migrated and created
        Assert.True(File.Exists(storage.DatabasePath));

        await app.StopAsync();
        Assert.False(app.IsRunning);

        // Clean up
        try { Directory.Delete(tempDir, true); } catch { }
    }

    [Fact]
    public async Task CoordinatorApp_CrashRecovery_ReconcilesUnclosedSessionsOnStartup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LAT_CoordTest_" + Guid.NewGuid().ToString("N"));
        var storage = new StoragePathProvider(tempDir);
        storage.EnsureDirectoriesExist();

        var factory = new SqliteConnectionFactory(storage);
        var migrationRunner = new MigrationRunner(factory);
        await migrationRunner.ApplyMigrationsAsync();

        var profRepo = new ProfileRepository(factory);
        var actRepo = new ActivityRepository(factory);

        var profileId = Guid.NewGuid().ToString();
        await profRepo.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "CrashRecoveryProfile"
        });

        // Insert unclosed URL session
        var unclosedUrlSessionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow.AddMinutes(-30);
        var heartbeatTime = DateTime.UtcNow.AddMinutes(-10);

        await actRepo.OpenUrlSessionAsync(new UrlActivitySession
        {
            SessionId = unclosedUrlSessionId,
            ProfileId = profileId,
            ChromeTabId = 202,
            Url = "https://example.com/unclosed",
            SanitizedUrl = "https://example.com/unclosed",
            Domain = "example.com",
            PageTitle = "Unclosed Page",
            StartTimeUtc = startTime,
            LastHeartbeatUtc = heartbeatTime,
            ActiveDurationSeconds = 0,
            IsCompleted = false,
            SessionStatus = "ACTIVE"
        });

        // Insert unclosed Non-Chrome Application session (zero window titles)
        var unclosedAppSessionId = Guid.NewGuid().ToString();
        await actRepo.OpenApplicationSessionAsync(new ApplicationSession
        {
            SessionId = unclosedAppSessionId,
            ApplicationDisplayName = "Visual Studio Code",
            ProcessName = "Code.exe",
            StartTimeUtc = startTime,
            LastHeartbeatUtc = heartbeatTime,
            DurationSeconds = 0,
            IsCompleted = false,
            SessionStatus = "ACTIVE"
        });

        // Verify they are uncompleted before Coordinator starts
        var preCheckUrl = await actRepo.GetUrlSessionByIdAsync(unclosedUrlSessionId);
        Assert.NotNull(preCheckUrl);
        Assert.False(preCheckUrl.IsCompleted);

        var preCheckApp = await actRepo.GetApplicationSessionByIdAsync(unclosedAppSessionId);
        Assert.NotNull(preCheckApp);
        Assert.False(preCheckApp.IsCompleted);

        // Start Coordinator: it must run crash recovery during startup
        using var app = new CoordinatorApp(storage: storage, connectionFactory: factory, enableTray: false);
        await app.StartAsync();

        // Verify sessions were recovered and closed
        var postCheckUrl = await actRepo.GetUrlSessionByIdAsync(unclosedUrlSessionId);
        Assert.NotNull(postCheckUrl);
        Assert.True(postCheckUrl.IsCompleted);
        Assert.Equal("RECOVERED_AFTER_CRASH", postCheckUrl.SessionStatus);
        Assert.True(postCheckUrl.ActiveDurationSeconds > 0);

        var postCheckApp = await actRepo.GetApplicationSessionByIdAsync(unclosedAppSessionId);
        Assert.NotNull(postCheckApp);
        Assert.True(postCheckApp.IsCompleted);
        Assert.Equal("RECOVERED_AFTER_CRASH", postCheckApp.SessionStatus);
        Assert.True(postCheckApp.DurationSeconds > 0);

        await app.StopAsync();

        // Clean up
        try { Directory.Delete(tempDir, true); } catch { }
    }
}
