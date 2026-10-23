using LocalActivityTracker.Core.Arbitration;
using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Privacy;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Win32;
using Xunit;

namespace LocalActivityTracker.Tests;

public class ActivityArbiterTests
{
    private static async Task<(SqliteConnectionFactory, ActivityRepository, ProfileRepository)> CreateTestDatabaseAsync()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "LAT_ArbiterTest_" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(tempDb);
        var runner = new MigrationRunner(factory);
        await runner.ApplyMigrationsAsync();

        var actRepo = new ActivityRepository(factory);
        var profRepo = new ProfileRepository(factory);

        return (factory, actRepo, profRepo);
    }

    [Fact]
    public async Task Arbiter_ChromeFocusAndTabNavigation_OpensUrlActivitySession()
    {
        var (_, actRepo, profRepo) = await CreateTestDatabaseAsync();
        var profileId = Guid.NewGuid().ToString("N");

        await profRepo.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "ArbiterProfile"
        });

        using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());

        // 1. Chrome holds OS foreground focus
        var now = DateTime.UtcNow;
        await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
            new IntPtr(100), 1234, "chrome.exe", "Google Chrome", true, now));

        Assert.True(arbiter.IsChromeForeground);

        // 2. Tab navigated in Chrome
        var tabMsg = IpcMessage.Create(
            IpcMessage.EventTabNavigated,
            profileId,
            new
            {
                tab_id = 55,
                window_id = 1,
                raw_url = "https://github.com/udaysharmadev/Gravitas?token=secret123",
                page_title = "GitHub - udaysharmadev/Gravitas"
            });
        tabMsg.TimestampUtc = now;

        await arbiter.OnIpcMessageReceivedAsync(profileId, tabMsg);

        // Assert: URL activity session opened with sanitized URL
        Assert.NotNull(arbiter.CurrentUrlSession);
        Assert.Equal(profileId, arbiter.CurrentUrlSession.ProfileId);
        Assert.Equal(55, arbiter.CurrentUrlSession.ChromeTabId);
        Assert.Equal("https://github.com/udaysharmadev/Gravitas", arbiter.CurrentUrlSession.SanitizedUrl);
        Assert.DoesNotContain("token=secret123", arbiter.CurrentUrlSession.SanitizedUrl);
        Assert.Equal("ACTIVE", arbiter.CurrentUrlSession.SessionStatus);
    }

    [Fact]
    public async Task Arbiter_FocusSwitchToNonChromeApp_ClosesUrlSessionAndOpensAppSession_WithZeroWindowTitles()
    {
        var (_, actRepo, profRepo) = await CreateTestDatabaseAsync();
        var profileId = Guid.NewGuid().ToString("N");

        await profRepo.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "ArbiterProfile"
        });

        using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());

        var t0 = DateTime.UtcNow.AddMinutes(-5);
        await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
            new IntPtr(100), 1234, "chrome.exe", "Google Chrome", true, t0));

        var tabMsg = IpcMessage.Create(
            IpcMessage.EventTabNavigated,
            profileId,
            new
            {
                tab_id = 55,
                window_id = 1,
                raw_url = "https://github.com/udaysharmadev/Gravitas",
                page_title = "GitHub"
            });
        tabMsg.TimestampUtc = t0;

        await arbiter.OnIpcMessageReceivedAsync(profileId, tabMsg);
        var initialUrlSessionId = arbiter.CurrentUrlSession!.SessionId;

        // Switch focus to Notepad (non-Chrome app)
        var t1 = t0.AddSeconds(120);
        await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
            new IntPtr(200), 5678, "notepad.exe", "Notepad", false, t1));

        // Assert: Chrome lost focus, URL session closed
        Assert.False(arbiter.IsChromeForeground);
        Assert.Null(arbiter.CurrentUrlSession);

        var closedUrlSession = await actRepo.GetUrlSessionByIdAsync(initialUrlSessionId);
        Assert.NotNull(closedUrlSession);
        Assert.True(closedUrlSession.IsCompleted);
        Assert.Equal(120, closedUrlSession.ActiveDurationSeconds);

        // Assert: ApplicationSession opened for Notepad with ZERO window title
        Assert.NotNull(arbiter.CurrentAppSession);
        Assert.Equal("notepad.exe", arbiter.CurrentAppSession.ProcessName);
        Assert.Equal("Notepad", arbiter.CurrentAppSession.ApplicationDisplayName);

        var dbAppSession = await actRepo.GetApplicationSessionByIdAsync(arbiter.CurrentAppSession.SessionId);
        Assert.NotNull(dbAppSession);
        Assert.Equal("notepad.exe", dbAppSession.ProcessName);
        Assert.Equal("Notepad", dbAppSession.ApplicationDisplayName);
        Assert.False(dbAppSession.IsCompleted);
    }

    [Fact]
    public async Task Arbiter_SessionLock_ImmediatelyClosesActiveSessions()
    {
        var (_, actRepo, profRepo) = await CreateTestDatabaseAsync();
        var profileId = Guid.NewGuid().ToString("N");

        await profRepo.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "ArbiterProfile"
        });

        using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());

        var t0 = DateTime.UtcNow.AddMinutes(-2);
        await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
            new IntPtr(100), 1234, "chrome.exe", "Google Chrome", true, t0));

        var tabMsg = IpcMessage.Create(
            IpcMessage.EventTabNavigated,
            profileId,
            new
            {
                tab_id = 88,
                window_id = 1,
                raw_url = "https://example.com",
                page_title = "Example"
            });
        tabMsg.TimestampUtc = t0;

        await arbiter.OnIpcMessageReceivedAsync(profileId, tabMsg);
        var activeSessionId = arbiter.CurrentUrlSession!.SessionId;

        // Session lock event fires
        await arbiter.OnSessionLockedAsync();

        Assert.True(arbiter.IsTrackingPaused);
        Assert.Null(arbiter.CurrentUrlSession);
        Assert.Null(arbiter.CurrentAppSession);

        var closedSession = await actRepo.GetUrlSessionByIdAsync(activeSessionId);
        Assert.NotNull(closedSession);
        Assert.True(closedSession.IsCompleted);
    }

    [Fact]
    public async Task Arbiter_ExtensionDirectWindowFocusAndTabEvents_OpensUrlSessionAuthoritatively()
    {
        var (_, actRepo, profRepo) = await CreateTestDatabaseAsync();
        var profileId = Guid.NewGuid().ToString("N");

        await profRepo.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "FocusTestProfile"
        });

        using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());
        Assert.False(arbiter.IsChromeForeground);

        var now = DateTime.UtcNow;

        // 1. Chrome extension sends WINDOW_FOCUSED
        var focusMsg = IpcMessage.Create(
            IpcMessage.EventWindowFocused,
            profileId,
            new { window_id = 10 });
        focusMsg.TimestampUtc = now;

        await arbiter.OnIpcMessageReceivedAsync(profileId, focusMsg);
        Assert.True(arbiter.IsChromeForeground);

        // 2. Chrome extension sends TAB_NAVIGATED
        var tabMsg = IpcMessage.Create(
            IpcMessage.EventTabNavigated,
            profileId,
            new
            {
                tab_id = 99,
                window_id = 10,
                raw_url = "https://news.ycombinator.com/item?id=12345",
                page_title = "Hacker News"
            });
        tabMsg.TimestampUtc = now.AddSeconds(1);

        await arbiter.OnIpcMessageReceivedAsync(profileId, tabMsg);

        // Assert URL session opened
        Assert.NotNull(arbiter.CurrentUrlSession);
        Assert.Equal("https://news.ycombinator.com/item?id=12345", arbiter.CurrentUrlSession.SanitizedUrl);
        Assert.Equal("ycombinator.com", arbiter.CurrentUrlSession.Domain);

        // 3. Chrome extension sends WINDOW_FOCUSED with -1 (lost focus)
        var lostFocusMsg = IpcMessage.Create(
            IpcMessage.EventWindowFocused,
            profileId,
            new { window_id = -1 });
        lostFocusMsg.TimestampUtc = now.AddSeconds(10);

        await arbiter.OnIpcMessageReceivedAsync(profileId, lostFocusMsg);
        Assert.False(arbiter.IsChromeForeground);
        Assert.Null(arbiter.CurrentUrlSession);

        var list = await actRepo.GetUrlSessionsInRangeAsync(null, now.AddMinutes(-1), now.AddMinutes(1));
        Assert.Single(list);
        Assert.Equal("https://news.ycombinator.com/item?id=12345", list[0].SanitizedUrl);
        Assert.True(list[0].IsCompleted);
        Assert.Equal(9, list[0].ActiveDurationSeconds);
    }
}
